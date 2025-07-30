using AgenticAI.Desktop.Domain.Models;
using AgenticAI.Desktop.Shared.Contracts;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;

namespace AgenticAI.Desktop.Infrastructure.MCP;

/// <summary>
/// MCP (Model-Command-Protocol) Protocol Engine
/// Handles standardized communication between Director and Agents
/// </summary>
public class McpProtocolEngine : IMcpProtocolEngine
{
    private readonly ILogger<McpProtocolEngine> _logger;
    private readonly ConcurrentDictionary<string, AgentConnection> _agentConnections;
    private readonly ConcurrentDictionary<string, List<Func<AgentEvent, Task>>> _eventSubscriptions;
    private readonly SemaphoreSlim _connectionSemaphore;
    private bool _isRunning;
    private CancellationTokenSource? _cancellationTokenSource;

    public McpProtocolEngine(ILogger<McpProtocolEngine> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _agentConnections = new ConcurrentDictionary<string, AgentConnection>();
        _eventSubscriptions = new ConcurrentDictionary<string, List<Func<AgentEvent, Task>>>();
        _connectionSemaphore = new SemaphoreSlim(1, 1);
    }

    /// <summary>
    /// Start the MCP protocol engine
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
            return;

        _logger.LogInformation("Starting MCP Protocol Engine...");

        try
        {
            _cancellationTokenSource = new CancellationTokenSource();
            
            // Start connection listener for incoming agent connections
            _ = Task.Run(() => ListenForAgentConnectionsAsync(_cancellationTokenSource.Token), cancellationToken);
            
            _isRunning = true;
            _logger.LogInformation("MCP Protocol Engine started successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start MCP Protocol Engine");
            throw;
        }
    }

    /// <summary>
    /// Stop the MCP protocol engine
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning)
            return;

        _logger.LogInformation("Stopping MCP Protocol Engine...");

        try
        {
            _cancellationTokenSource?.Cancel();
            
            // Close all agent connections
            var closeTasks = _agentConnections.Values.Select(conn => conn.CloseAsync());
            await Task.WhenAll(closeTasks);
            
            _agentConnections.Clear();
            _eventSubscriptions.Clear();
            
            _isRunning = false;
            _logger.LogInformation("MCP Protocol Engine stopped successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while stopping MCP Protocol Engine");
            throw;
        }
    }

    /// <summary>
    /// Send a message to a specific agent
    /// </summary>
    public async Task<McpResponse> SendMessageAsync(string agentId, McpMessage message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(agentId))
            throw new ArgumentException("Agent ID cannot be null or empty", nameof(agentId));
        
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        _logger.LogDebug("Sending message {MessageId} to agent {AgentId}", message.Id, agentId);

        if (!_agentConnections.TryGetValue(agentId, out var connection))
        {
            var errorResponse = new McpResponse
            {
                MessageId = message.Id,
                Success = false,
                Message = $"Agent {agentId} is not connected",
                ErrorCode = "AGENT_NOT_CONNECTED"
            };
            
            _logger.LogWarning("Agent {AgentId} is not connected", agentId);
            return errorResponse;
        }

        try
        {
            return await connection.SendMessageAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message {MessageId} to agent {AgentId}", message.Id, agentId);
            
            return new McpResponse
            {
                MessageId = message.Id,
                Success = false,
                Message = $"Failed to send message: {ex.Message}",
                ErrorCode = "SEND_FAILED"
            };
        }
    }

    /// <summary>
    /// Subscribe to events from a specific agent
    /// </summary>
    public async Task SubscribeToAgentEventsAsync(string agentId, Func<AgentEvent, Task> eventHandler, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(agentId))
            throw new ArgumentException("Agent ID cannot be null or empty", nameof(agentId));
        
        if (eventHandler == null)
            throw new ArgumentNullException(nameof(eventHandler));

        await Task.CompletedTask; // For async consistency
        
        _eventSubscriptions.AddOrUpdate(
            agentId,
            new List<Func<AgentEvent, Task>> { eventHandler },
            (key, existingHandlers) =>
            {
                existingHandlers.Add(eventHandler);
                return existingHandlers;
            });

        _logger.LogDebug("Subscribed to events from agent {AgentId}", agentId);
    }

    /// <summary>
    /// Listen for incoming agent connections
    /// </summary>
    private async Task ListenForAgentConnectionsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting to listen for agent connections...");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // In a real implementation, this would listen on a named pipe or TCP port
                // For now, we'll simulate the connection listening
                await Task.Delay(1000, cancellationToken);
                
                // Simulate periodic connection health checks
                await CheckConnectionHealthAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in connection listener");
                await Task.Delay(5000, cancellationToken); // Wait before retrying
            }
        }

        _logger.LogInformation("Stopped listening for agent connections");
    }

    /// <summary>
    /// Check the health of all agent connections
    /// </summary>
    private async Task CheckConnectionHealthAsync(CancellationToken cancellationToken)
    {
        var unhealthyConnections = new List<string>();

        foreach (var kvp in _agentConnections)
        {
            try
            {
                var isHealthy = await kvp.Value.CheckHealthAsync(cancellationToken);
                if (!isHealthy)
                {
                    unhealthyConnections.Add(kvp.Key);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Health check failed for agent {AgentId}", kvp.Key);
                unhealthyConnections.Add(kvp.Key);
            }
        }

        // Remove unhealthy connections
        foreach (var agentId in unhealthyConnections)
        {
            if (_agentConnections.TryRemove(agentId, out var connection))
            {
                await connection.CloseAsync();
                _logger.LogWarning("Removed unhealthy connection for agent {AgentId}", agentId);
            }
        }
    }

    /// <summary>
    /// Register a new agent connection
    /// </summary>
    public async Task<bool> RegisterAgentConnectionAsync(string agentId, AgentConnection connection, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(agentId))
            throw new ArgumentException("Agent ID cannot be null or empty", nameof(agentId));
        
        if (connection == null)
            throw new ArgumentNullException(nameof(connection));

        await _connectionSemaphore.WaitAsync(cancellationToken);
        
        try
        {
            if (_agentConnections.ContainsKey(agentId))
            {
                _logger.LogWarning("Agent {AgentId} is already connected", agentId);
                return false;
            }

            _agentConnections.TryAdd(agentId, connection);
            
            // Set up event forwarding
            connection.OnEventReceived += async (agentEvent) => await ForwardAgentEventAsync(agentId, agentEvent);
            
            _logger.LogInformation("Registered connection for agent {AgentId}", agentId);
            return true;
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    /// <summary>
    /// Forward agent events to subscribers
    /// </summary>
    private async Task ForwardAgentEventAsync(string agentId, AgentEvent agentEvent)
    {
        if (_eventSubscriptions.TryGetValue(agentId, out var handlers))
        {
            var tasks = handlers.Select(handler => 
            {
                try
                {
                    return handler(agentEvent);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in event handler for agent {AgentId}", agentId);
                    return Task.CompletedTask;
                }
            });

            await Task.WhenAll(tasks);
        }
    }

    /// <summary>
    /// Get connection statistics
    /// </summary>
    public McpConnectionStats GetConnectionStats()
    {
        return new McpConnectionStats
        {
            TotalConnections = _agentConnections.Count,
            ActiveConnections = _agentConnections.Values.Count(c => c.IsConnected),
            TotalSubscriptions = _eventSubscriptions.Values.Sum(handlers => handlers.Count)
        };
    }

    /// <summary>
    /// Dispose resources
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_isRunning)
        {
            await StopAsync();
        }
        
        _connectionSemaphore.Dispose();
        _cancellationTokenSource?.Dispose();
    }
}

/// <summary>
/// Represents a connection to an agent
/// </summary>
public class AgentConnection
{
    private readonly ILogger<AgentConnection> _logger;
    private readonly string _agentId;
    private NamedPipeClientStream? _pipeStream;
    private bool _isConnected;

    public event Func<AgentEvent, Task>? OnEventReceived;
    
    public bool IsConnected => _isConnected && _pipeStream?.IsConnected == true;

    public AgentConnection(string agentId, ILogger<AgentConnection> logger)
    {
        _agentId = agentId ?? throw new ArgumentNullException(nameof(agentId));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Connect to the agent
    /// </summary>
    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _pipeStream = new NamedPipeClientStream(".", $"AgenticAI_Agent_{_agentId}", PipeDirection.InOut);
            await _pipeStream.ConnectAsync(5000, cancellationToken);
            
            _isConnected = true;
            _logger.LogInformation("Connected to agent {AgentId}", _agentId);
            
            // Start listening for events
            _ = Task.Run(() => ListenForEventsAsync(cancellationToken), cancellationToken);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to agent {AgentId}", _agentId);
            return false;
        }
    }

    /// <summary>
    /// Send a message to the agent
    /// </summary>
    public async Task<McpResponse> SendMessageAsync(McpMessage message, CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Agent is not connected");

        try
        {
            var messageJson = JsonSerializer.Serialize(message);
            var messageBytes = System.Text.Encoding.UTF8.GetBytes(messageJson);
            
            await _pipeStream!.WriteAsync(messageBytes, cancellationToken);
            await _pipeStream.FlushAsync(cancellationToken);
            
            // Read response (simplified implementation)
            var buffer = new byte[4096];
            var bytesRead = await _pipeStream.ReadAsync(buffer, cancellationToken);
            var responseJson = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
            
            var response = JsonSerializer.Deserialize<McpResponse>(responseJson);
            return response ?? new McpResponse
            {
                MessageId = message.Id,
                Success = false,
                Message = "Invalid response received",
                ErrorCode = "INVALID_RESPONSE"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message to agent {AgentId}", _agentId);
            
            return new McpResponse
            {
                MessageId = message.Id,
                Success = false,
                Message = $"Communication error: {ex.Message}",
                ErrorCode = "COMMUNICATION_ERROR"
            };
        }
    }

    /// <summary>
    /// Check if the connection is healthy
    /// </summary>
    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
            return false;

        try
        {
            var pingMessage = new McpMessage
            {
                Type = "ping",
                Source = "director",
                Target = _agentId,
                Payload = new Dictionary<string, object>()
            };

            var response = await SendMessageAsync(pingMessage, cancellationToken);
            return response.Success;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Listen for events from the agent
    /// </summary>
    private async Task ListenForEventsAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        
        while (IsConnected && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var bytesRead = await _pipeStream!.ReadAsync(buffer, cancellationToken);
                if (bytesRead > 0)
                {
                    var eventJson = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    var agentEvent = JsonSerializer.Deserialize<AgentEvent>(eventJson);
                    
                    if (agentEvent != null && OnEventReceived != null)
                    {
                        await OnEventReceived(agentEvent);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listening for events from agent {AgentId}", _agentId);
                break;
            }
        }
    }

    /// <summary>
    /// Close the connection
    /// </summary>
    public async Task CloseAsync()
    {
        try
        {
            _isConnected = false;
            
            if (_pipeStream != null)
            {
                await _pipeStream.DisposeAsync();
                _pipeStream = null;
            }
            
            _logger.LogInformation("Closed connection to agent {AgentId}", _agentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing connection to agent {AgentId}", _agentId);
        }
    }
}

/// <summary>
/// MCP connection statistics
/// </summary>
public class McpConnectionStats
{
    public int TotalConnections { get; set; }
    public int ActiveConnections { get; set; }
    public int TotalSubscriptions { get; set; }
}
