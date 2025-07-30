using AgenticAI.Desktop.Domain.Models;
using AgenticAI.Desktop.Shared.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;

namespace AgenticAI.Desktop.Agents.Core;

/// <summary>
/// Base Agent class - provides common functionality for all agents
/// All specific agents should inherit from this base class
/// </summary>
public abstract class BaseAgent : IAgent, IDisposable
{
    protected readonly ILogger _logger;
    protected readonly IConfiguration _configuration;
    protected readonly ConcurrentDictionary<string, object> _state;
    protected readonly SemaphoreSlim _executionSemaphore;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private NamedPipeClientStream? _pipeClient;
    private StreamWriter? _pipeWriter;
    private StreamReader? _pipeReader;
    private Task? _messageListenerTask;
    private bool _disposed = false;

    public string Id { get; protected set; }
    public string Name { get; protected set; }
    public string Description { get; protected set; }
    public string Version { get; protected set; }
    public List<AgentCapability> Capabilities { get; protected set; }
    public AgentStatus Status { get; protected set; }
    public DateTime LastHeartbeat { get; protected set; }
    public Dictionary<string, object> Metadata { get; protected set; }

    protected BaseAgent(ILogger logger, IConfiguration configuration, string name, string description)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _state = new ConcurrentDictionary<string, object>();
        _executionSemaphore = new SemaphoreSlim(3, 3); // Allow up to 3 concurrent executions
        _cancellationTokenSource = new CancellationTokenSource();

        Id = Guid.NewGuid().ToString();
        Name = name;
        Description = description;
        Version = "1.0.0";
        Capabilities = new List<AgentCapability>();
        Status = AgentStatus.Initializing;
        LastHeartbeat = DateTime.UtcNow;
        Metadata = new Dictionary<string, object>();

        InitializeCapabilities();
    }

    /// <summary>
    /// Initialize agent capabilities - to be implemented by derived classes
    /// </summary>
    protected abstract void InitializeCapabilities();

    /// <summary>
    /// Initialize the agent
    /// </summary>
    public virtual async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing agent {AgentName} ({AgentId})", Name, Id);

        try
        {
            // Connect to Director via MCP protocol
            await ConnectToDirectorAsync(cancellationToken);

            // Register with Director
            await RegisterWithDirectorAsync(cancellationToken);

            // Start message listener
            _messageListenerTask = ListenForMessagesAsync(_cancellationTokenSource.Token);

            Status = AgentStatus.Ready;
            LastHeartbeat = DateTime.UtcNow;

            _logger.LogInformation("Agent {AgentName} initialized successfully", Name);
        }
        catch (Exception ex)
        {
            Status = AgentStatus.Error;
            _logger.LogError(ex, "Failed to initialize agent {AgentName}", Name);
            throw;
        }
    }

    /// <summary>
    /// Execute a command
    /// </summary>
    public async Task<AgentExecutionResult> ExecuteCommandAsync(AgentCommand command, CancellationToken cancellationToken = default)
    {
        if (command == null)
            throw new ArgumentNullException(nameof(command));

        var executionId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        _logger.LogInformation("Executing command {CommandType} with execution ID {ExecutionId}", command.Type, executionId);

        try
        {
            await _executionSemaphore.WaitAsync(cancellationToken);

            Status = AgentStatus.Busy;
            LastHeartbeat = DateTime.UtcNow;

            // Execute the command using the derived class implementation
            var result = await ExecuteCommandInternalAsync(command, cancellationToken);

            result.ExecutionId = executionId;
            result.AgentId = Id;
            result.StartedAt = startTime;
            result.CompletedAt = DateTime.UtcNow;
            result.Duration = result.CompletedAt - result.StartedAt;

            Status = AgentStatus.Ready;
            LastHeartbeat = DateTime.UtcNow;

            _logger.LogInformation("Command {CommandType} executed successfully in {Duration}ms", 
                command.Type, result.Duration.TotalMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            Status = AgentStatus.Error;
            _logger.LogError(ex, "Failed to execute command {CommandType}", command.Type);

            return new AgentExecutionResult
            {
                ExecutionId = executionId,
                AgentId = Id,
                Success = false,
                Message = $"Command execution failed: {ex.Message}",
                StartedAt = startTime,
                CompletedAt = DateTime.UtcNow,
                Duration = DateTime.UtcNow - startTime,
                ErrorCode = "EXECUTION_FAILED"
            };
        }
        finally
        {
            _executionSemaphore.Release();
        }
    }

    /// <summary>
    /// Execute command implementation - to be implemented by derived classes
    /// </summary>
    protected abstract Task<AgentExecutionResult> ExecuteCommandInternalAsync(AgentCommand command, CancellationToken cancellationToken);

    /// <summary>
    /// Get agent health status
    /// </summary>
    public virtual Task<AgentHealthStatus> GetHealthStatusAsync(CancellationToken cancellationToken = default)
    {
        var healthStatus = new AgentHealthStatus
        {
            AgentId = Id,
            Status = Status,
            LastHeartbeat = LastHeartbeat,
            IsHealthy = Status != AgentStatus.Error && Status != AgentStatus.Stopped,
            ResponseTime = TimeSpan.FromMilliseconds(50), // Simulated response time
            MemoryUsage = GC.GetTotalMemory(false),
            CpuUsage = 0.0, // Would need performance counters for real CPU usage
            ErrorCount = 0, // Would track actual errors
            LastError = null,
            Metadata = new Dictionary<string, object>
            {
                { "capabilities_count", Capabilities.Count },
                { "state_entries", _state.Count },
                { "version", Version }
            }
        };

        return Task.FromResult(healthStatus);
    }

    /// <summary>
    /// Get agent information
    /// </summary>
    public virtual Task<AgentInfo> GetAgentInfoAsync(CancellationToken cancellationToken = default)
    {
        var agentInfo = new AgentInfo
        {
            Id = Id,
            Name = Name,
            Description = Description,
            Version = Version,
            Capabilities = Capabilities.ToList(),
            Status = Status,
            LastHeartbeat = LastHeartbeat,
            Metadata = new Dictionary<string, object>(Metadata)
        };

        return Task.FromResult(agentInfo);
    }

    /// <summary>
    /// Stop the agent
    /// </summary>
    public virtual async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping agent {AgentName}", Name);

        try
        {
            Status = AgentStatus.Stopping;
            
            // Cancel all operations
            _cancellationTokenSource.Cancel();

            // Wait for message listener to complete
            if (_messageListenerTask != null)
            {
                await _messageListenerTask;
            }

            // Disconnect from Director
            await DisconnectFromDirectorAsync();

            Status = AgentStatus.Stopped;
            _logger.LogInformation("Agent {AgentName} stopped successfully", Name);
        }
        catch (Exception ex)
        {
            Status = AgentStatus.Error;
            _logger.LogError(ex, "Error stopping agent {AgentName}", Name);
            throw;
        }
    }

    /// <summary>
    /// Connect to Director via named pipe
    /// </summary>
    private async Task ConnectToDirectorAsync(CancellationToken cancellationToken)
    {
        var pipeName = _configuration.GetValue<string>("MCP:PipeName") ?? "AgenticAI_MCP";
        
        _pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        
        _logger.LogDebug("Connecting to Director via pipe {PipeName}", pipeName);
        
        await _pipeClient.ConnectAsync(5000, cancellationToken); // 5 second timeout
        
        _pipeWriter = new StreamWriter(_pipeClient) { AutoFlush = true };
        _pipeReader = new StreamReader(_pipeClient);
        
        _logger.LogDebug("Connected to Director successfully");
    }

    /// <summary>
    /// Register with Director
    /// </summary>
    private async Task RegisterWithDirectorAsync(CancellationToken cancellationToken)
    {
        var registrationMessage = new McpMessage
        {
            Type = "agent_registration",
            SenderId = Id,
            Data = JsonSerializer.Serialize(new
            {
                agent_info = await GetAgentInfoAsync(cancellationToken)
            })
        };

        await SendMessageToDirectorAsync(registrationMessage);
        _logger.LogDebug("Registration message sent to Director");
    }

    /// <summary>
    /// Send message to Director
    /// </summary>
    private async Task SendMessageToDirectorAsync(McpMessage message)
    {
        if (_pipeWriter == null)
            throw new InvalidOperationException("Not connected to Director");

        var messageJson = JsonSerializer.Serialize(message);
        await _pipeWriter.WriteLineAsync(messageJson);
    }

    /// <summary>
    /// Listen for messages from Director
    /// </summary>
    private async Task ListenForMessagesAsync(CancellationToken cancellationToken)
    {
        if (_pipeReader == null)
            return;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var messageJson = await _pipeReader.ReadLineAsync();
                if (string.IsNullOrEmpty(messageJson))
                    continue;

                try
                {
                    var message = JsonSerializer.Deserialize<McpMessage>(messageJson);
                    if (message != null)
                    {
                        await HandleMessageFromDirectorAsync(message, cancellationToken);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize message from Director: {Message}", messageJson);
                }
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Error in message listener");
        }
    }

    /// <summary>
    /// Handle message from Director
    /// </summary>
    private async Task HandleMessageFromDirectorAsync(McpMessage message, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Received message from Director: {MessageType}", message.Type);

        try
        {
            switch (message.Type)
            {
                case "command_execution":
                    await HandleCommandExecutionMessage(message, cancellationToken);
                    break;
                case "health_check":
                    await HandleHealthCheckMessage(message, cancellationToken);
                    break;
                case "agent_info_request":
                    await HandleAgentInfoRequestMessage(message, cancellationToken);
                    break;
                default:
                    _logger.LogWarning("Unknown message type received: {MessageType}", message.Type);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling message from Director: {MessageType}", message.Type);
        }
    }

    /// <summary>
    /// Handle command execution message
    /// </summary>
    private async Task HandleCommandExecutionMessage(McpMessage message, CancellationToken cancellationToken)
    {
        try
        {
            var command = JsonSerializer.Deserialize<AgentCommand>(message.Data);
            if (command != null)
            {
                var result = await ExecuteCommandAsync(command, cancellationToken);
                
                var responseMessage = new McpMessage
                {
                    Type = "command_result",
                    SenderId = Id,
                    CorrelationId = message.CorrelationId,
                    Data = JsonSerializer.Serialize(result)
                };

                await SendMessageToDirectorAsync(responseMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling command execution message");
        }
    }

    /// <summary>
    /// Handle health check message
    /// </summary>
    private async Task HandleHealthCheckMessage(McpMessage message, CancellationToken cancellationToken)
    {
        try
        {
            var healthStatus = await GetHealthStatusAsync(cancellationToken);
            
            var responseMessage = new McpMessage
            {
                Type = "health_status",
                SenderId = Id,
                CorrelationId = message.CorrelationId,
                Data = JsonSerializer.Serialize(healthStatus)
            };

            await SendMessageToDirectorAsync(responseMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling health check message");
        }
    }

    /// <summary>
    /// Handle agent info request message
    /// </summary>
    private async Task HandleAgentInfoRequestMessage(McpMessage message, CancellationToken cancellationToken)
    {
        try
        {
            var agentInfo = await GetAgentInfoAsync(cancellationToken);
            
            var responseMessage = new McpMessage
            {
                Type = "agent_info",
                SenderId = Id,
                CorrelationId = message.CorrelationId,
                Data = JsonSerializer.Serialize(agentInfo)
            };

            await SendMessageToDirectorAsync(responseMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling agent info request message");
        }
    }

    /// <summary>
    /// Disconnect from Director
    /// </summary>
    private async Task DisconnectFromDirectorAsync()
    {
        try
        {
            if (_pipeWriter != null)
            {
                var disconnectMessage = new McpMessage
                {
                    Type = "agent_disconnect",
                    SenderId = Id,
                    Data = JsonSerializer.Serialize(new { reason = "shutdown" })
                };

                await SendMessageToDirectorAsync(disconnectMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error sending disconnect message");
        }
        finally
        {
            _pipeWriter?.Dispose();
            _pipeReader?.Dispose();
            _pipeClient?.Dispose();
        }
    }

    /// <summary>
    /// Dispose resources
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _cancellationTokenSource?.Cancel();
            _messageListenerTask?.Wait(TimeSpan.FromSeconds(5));
            
            _executionSemaphore?.Dispose();
            _cancellationTokenSource?.Dispose();
            _pipeWriter?.Dispose();
            _pipeReader?.Dispose();
            _pipeClient?.Dispose();
            
            _disposed = true;
        }
    }
}

/// <summary>
/// Agent Host Service - manages agent lifecycle
/// </summary>
public class AgentHostService : BackgroundService
{
    private readonly ILogger<AgentHostService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly List<IAgent> _agents;

    public AgentHostService(ILogger<AgentHostService> logger, IServiceProvider serviceProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _agents = new List<IAgent>();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Agent Host Service starting");

        try
        {
            // Initialize and start all registered agents
            var agentTypes = GetAgentTypes();
            
            foreach (var agentType in agentTypes)
            {
                try
                {
                    var agent = (IAgent)_serviceProvider.GetService(agentType)!;
                    if (agent != null)
                    {
                        await agent.InitializeAsync(stoppingToken);
                        _agents.Add(agent);
                        _logger.LogInformation("Started agent: {AgentName}", agent.Name);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to start agent of type {AgentType}", agentType.Name);
                }
            }

            // Keep the service running
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Agent Host Service stopping");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Agent Host Service");
        }
        finally
        {
            // Stop all agents
            foreach (var agent in _agents)
            {
                try
                {
                    await agent.StopAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error stopping agent {AgentName}", agent.Name);
                }
            }
        }
    }

    /// <summary>
    /// Get all agent types from the service provider
    /// </summary>
    private IEnumerable<Type> GetAgentTypes()
    {
        // In a real implementation, this would discover agent types from assemblies
        // For now, return empty list - specific agents will be registered manually
        return new List<Type>();
    }
}
