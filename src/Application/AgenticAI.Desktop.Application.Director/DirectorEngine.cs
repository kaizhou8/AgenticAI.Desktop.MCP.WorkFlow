using AgenticAI.Desktop.Domain.Models;
using AgenticAI.Desktop.Shared.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System.Collections.Concurrent;
using System.Text.Json;

namespace AgenticAI.Desktop.Application.Director;

/// <summary>
/// Director scheduling engine - the central nervous system of the AgenticAI system
/// Coordinates interactions between large language models and various desktop applications
/// </summary>
public class DirectorEngine : IDirector
{
    private readonly ILogger<DirectorEngine> _logger;
    private readonly ILlmAdapter _llmAdapter;
    private readonly IMcpProtocolEngine _mcpEngine;
    private readonly IWorkflowManager _workflowManager;
    private readonly ConcurrentDictionary<string, IAgent> _registeredAgents;
    private readonly ConcurrentDictionary<string, AgentInfo> _agentInfoCache;
    private readonly SemaphoreSlim _executionSemaphore;
    private bool _isInitialized;

    public DirectorEngine(
        ILogger<DirectorEngine> logger,
        ILlmAdapter llmAdapter,
        IMcpProtocolEngine mcpEngine,
        IWorkflowManager workflowManager)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _llmAdapter = llmAdapter ?? throw new ArgumentNullException(nameof(llmAdapter));
        _mcpEngine = mcpEngine ?? throw new ArgumentNullException(nameof(mcpEngine));
        _workflowManager = workflowManager ?? throw new ArgumentNullException(nameof(workflowManager));
        _registeredAgents = new ConcurrentDictionary<string, IAgent>();
        _agentInfoCache = new ConcurrentDictionary<string, AgentInfo>();
        _executionSemaphore = new SemaphoreSlim(10, 10); // Allow up to 10 concurrent executions
    }

    /// <summary>
    /// Initialize the Director engine
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
            return;

        _logger.LogInformation("Initializing Director Engine...");

        try
        {
            // Start the MCP protocol engine
            await _mcpEngine.StartAsync(cancellationToken);
            
            _isInitialized = true;
            _logger.LogInformation("Director Engine initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Director Engine");
            throw;
        }
    }

    /// <summary>
    /// Process a natural language request from the user
    /// </summary>
    public async Task<DirectorResponse> ProcessRequestAsync(string request, CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
            throw new InvalidOperationException("Director engine is not initialized");

        var requestId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;
        
        _logger.LogInformation("Processing request {RequestId}: {Request}", requestId, request);

        try
        {
            await _executionSemaphore.WaitAsync(cancellationToken);

            // Step 1: Analyze the request using LLM
            var analysisResult = await AnalyzeRequestAsync(request, cancellationToken);
            
            // Step 2: Plan execution strategy
            var executionPlan = await CreateExecutionPlanAsync(analysisResult, cancellationToken);
            
            // Step 3: Execute the plan
            var executionResult = await ExecutePlanAsync(executionPlan, cancellationToken);
            
            var response = new DirectorResponse
            {
                RequestId = requestId,
                Success = executionResult.Success,
                Message = executionResult.Message,
                Data = executionResult.Data,
                ExecutedActions = executionResult.ExecutedActions,
                CompletedAt = DateTime.UtcNow,
                Duration = DateTime.UtcNow - startTime,
                WorkflowId = executionResult.WorkflowId
            };

            _logger.LogInformation("Request {RequestId} processed successfully in {Duration}ms", 
                requestId, response.Duration.TotalMilliseconds);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process request {RequestId}", requestId);
            
            return new DirectorResponse
            {
                RequestId = requestId,
                Success = false,
                Message = $"Failed to process request: {ex.Message}",
                CompletedAt = DateTime.UtcNow,
                Duration = DateTime.UtcNow - startTime
            };
        }
        finally
        {
            _executionSemaphore.Release();
        }
    }

    /// <summary>
    /// Execute a predefined workflow
    /// </summary>
    public async Task<WorkflowExecutionResult> ExecuteWorkflowAsync(string workflowId, Dictionary<string, object>? parameters = null, CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
            throw new InvalidOperationException("Director engine is not initialized");

        _logger.LogInformation("Executing workflow {WorkflowId}", workflowId);

        try
        {
            await _executionSemaphore.WaitAsync(cancellationToken);
            
            return await _workflowManager.ExecuteWorkflowAsync(workflowId, parameters, cancellationToken);
        }
        finally
        {
            _executionSemaphore.Release();
        }
    }

    /// <summary>
    /// Register an agent with the director
    /// </summary>
    public async Task RegisterAgentAsync(IAgent agent, CancellationToken cancellationToken = default)
    {
        if (agent == null)
            throw new ArgumentNullException(nameof(agent));

        _logger.LogInformation("Registering agent {AgentId} ({AgentName})", agent.Id, agent.Name);

        try
        {
            // Initialize the agent if not already initialized
            if (agent.Status != AgentStatus.Ready)
            {
                var initialized = await agent.InitializeAsync(cancellationToken);
                if (!initialized)
                {
                    throw new InvalidOperationException($"Failed to initialize agent {agent.Id}");
                }
            }

            // Add to registered agents
            _registeredAgents.AddOrUpdate(agent.Id, agent, (key, oldValue) => agent);

            // Cache agent info
            var agentInfo = new AgentInfo
            {
                Id = agent.Id,
                Name = agent.Name,
                Description = agent.Description,
                Version = agent.Version,
                Status = agent.Status,
                Capabilities = agent.Capabilities.ToList(),
                RegisteredAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow
            };

            _agentInfoCache.AddOrUpdate(agent.Id, agentInfo, (key, oldValue) => agentInfo);

            _logger.LogInformation("Agent {AgentId} registered successfully with {CapabilityCount} capabilities", 
                agent.Id, agent.Capabilities.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register agent {AgentId}", agent.Id);
            throw;
        }
    }

    /// <summary>
    /// Unregister an agent from the director
    /// </summary>
    public async Task UnregisterAgentAsync(string agentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(agentId))
            throw new ArgumentException("Agent ID cannot be null or empty", nameof(agentId));

        _logger.LogInformation("Unregistering agent {AgentId}", agentId);

        try
        {
            if (_registeredAgents.TryRemove(agentId, out var agent))
            {
                await agent.ShutdownAsync(cancellationToken);
                _agentInfoCache.TryRemove(agentId, out _);
                
                _logger.LogInformation("Agent {AgentId} unregistered successfully", agentId);
            }
            else
            {
                _logger.LogWarning("Agent {AgentId} was not found in registered agents", agentId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unregister agent {AgentId}", agentId);
            throw;
        }
    }

    /// <summary>
    /// Get list of available agents
    /// </summary>
    public async Task<IReadOnlyList<AgentInfo>> GetAvailableAgentsAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // For async consistency
        
        var agentInfos = new List<AgentInfo>();
        
        foreach (var kvp in _agentInfoCache)
        {
            var agentInfo = kvp.Value;
            
            // Update last seen time if agent is still registered
            if (_registeredAgents.ContainsKey(kvp.Key))
            {
                agentInfo.LastSeenAt = DateTime.UtcNow;
            }
            
            agentInfos.Add(agentInfo);
        }
        
        return agentInfos.AsReadOnly();
    }

    /// <summary>
    /// Analyze user request using LLM to understand intent and required actions
    /// </summary>
    private async Task<RequestAnalysisResult> AnalyzeRequestAsync(string request, CancellationToken cancellationToken)
    {
        var systemPrompt = @"You are an intelligent task analyzer for an office automation system. 
Analyze the user's request and determine:
1. The main intent/goal
2. Required actions to accomplish the goal
3. Which agents/capabilities are needed
4. Input parameters and expected outputs
5. Whether this should be a one-time execution or a reusable workflow

Respond in JSON format with the following structure:
{
  ""intent"": ""Brief description of what the user wants to accomplish"",
  ""actions"": [""list of required actions""],
  ""requiredCapabilities"": [""list of agent capabilities needed""],
  ""parameters"": {""key-value pairs of input parameters""},
  ""isWorkflow"": true/false,
  ""complexity"": ""simple/medium/complex""
}";

        var llmOptions = new LlmOptions
        {
            SystemPrompt = systemPrompt,
            Temperature = 0.3,
            MaxTokens = 1000
        };

        var llmResponse = await _llmAdapter.ProcessPromptAsync(request, llmOptions, cancellationToken);
        
        if (!llmResponse.Success)
        {
            throw new InvalidOperationException($"Failed to analyze request: {llmResponse.ErrorMessage}");
        }

        try
        {
            var analysisData = JsonSerializer.Deserialize<Dictionary<string, object>>(llmResponse.Content);
            
            return new RequestAnalysisResult
            {
                Intent = analysisData?.GetValueOrDefault("intent")?.ToString() ?? "Unknown intent",
                Actions = ExtractStringList(analysisData, "actions"),
                RequiredCapabilities = ExtractStringList(analysisData, "requiredCapabilities"),
                Parameters = analysisData?.GetValueOrDefault("parameters") as Dictionary<string, object> ?? new(),
                IsWorkflow = ExtractBoolean(analysisData, "isWorkflow"),
                Complexity = analysisData?.GetValueOrDefault("complexity")?.ToString() ?? "medium"
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse LLM analysis response, using fallback analysis");
            
            // Fallback analysis
            return new RequestAnalysisResult
            {
                Intent = "Process user request",
                Actions = new List<string> { "execute_request" },
                RequiredCapabilities = new List<string>(),
                Parameters = new Dictionary<string, object> { { "original_request", request } },
                IsWorkflow = false,
                Complexity = "simple"
            };
        }
    }

    /// <summary>
    /// Create execution plan based on analysis result
    /// </summary>
    private async Task<ExecutionPlan> CreateExecutionPlanAsync(RequestAnalysisResult analysis, CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // For async consistency
        
        var plan = new ExecutionPlan
        {
            Id = Guid.NewGuid().ToString(),
            Intent = analysis.Intent,
            Actions = analysis.Actions,
            Parameters = analysis.Parameters,
            IsWorkflow = analysis.IsWorkflow,
            CreatedAt = DateTime.UtcNow
        };

        // Find suitable agents for required capabilities
        foreach (var capability in analysis.RequiredCapabilities)
        {
            var suitableAgents = _registeredAgents.Values
                .Where(agent => agent.Status == AgentStatus.Ready && 
                               agent.Capabilities.Any(cap => cap.Name.Contains(capability, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (suitableAgents.Any())
            {
                plan.SelectedAgents.Add(capability, suitableAgents.First().Id);
            }
            else
            {
                _logger.LogWarning("No suitable agent found for capability: {Capability}", capability);
            }
        }

        return plan;
    }

    /// <summary>
    /// Execute the planned actions
    /// </summary>
    private async Task<PlanExecutionResult> ExecutePlanAsync(ExecutionPlan plan, CancellationToken cancellationToken)
    {
        var result = new PlanExecutionResult
        {
            Success = true,
            Message = "Execution completed successfully",
            Data = new Dictionary<string, object>(),
            ExecutedActions = new List<string>(),
            WorkflowId = plan.IsWorkflow ? plan.Id : null
        };

        try
        {
            foreach (var action in plan.Actions)
            {
                _logger.LogDebug("Executing action: {Action}", action);
                
                // For now, simulate action execution
                // In a real implementation, this would dispatch to appropriate agents
                await Task.Delay(100, cancellationToken); // Simulate work
                
                result.ExecutedActions.Add(action);
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Execution failed: {ex.Message}";
            _logger.LogError(ex, "Failed to execute plan {PlanId}", plan.Id);
        }

        return result;
    }

    /// <summary>
    /// Helper method to extract string list from dictionary
    /// </summary>
    private static List<string> ExtractStringList(Dictionary<string, object>? dict, string key)
    {
        if (dict?.TryGetValue(key, out var value) == true && value is JsonElement element && element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToList();
        }
        return new List<string>();
    }

    /// <summary>
    /// Helper method to extract boolean from dictionary
    /// </summary>
    private static bool ExtractBoolean(Dictionary<string, object>? dict, string key)
    {
        if (dict?.TryGetValue(key, out var value) == true)
        {
            if (value is JsonElement element && element.ValueKind == JsonValueKind.True)
                return true;
            if (value is bool boolValue)
                return boolValue;
        }
        return false;
    }

    /// <summary>
    /// Dispose resources
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_isInitialized)
        {
            _logger.LogInformation("Shutting down Director Engine...");
            
            // Shutdown all registered agents
            var shutdownTasks = _registeredAgents.Values.Select(agent => agent.ShutdownAsync());
            await Task.WhenAll(shutdownTasks);
            
            // Stop MCP engine
            await _mcpEngine.StopAsync();
            
            _executionSemaphore.Dispose();
            _isInitialized = false;
            
            _logger.LogInformation("Director Engine shut down successfully");
        }
    }
}

/// <summary>
/// Internal class for request analysis results
/// </summary>
internal class RequestAnalysisResult
{
    public string Intent { get; set; } = string.Empty;
    public List<string> Actions { get; set; } = new();
    public List<string> RequiredCapabilities { get; set; } = new();
    public Dictionary<string, object> Parameters { get; set; } = new();
    public bool IsWorkflow { get; set; }
    public string Complexity { get; set; } = string.Empty;
}

/// <summary>
/// Internal class for execution plans
/// </summary>
internal class ExecutionPlan
{
    public string Id { get; set; } = string.Empty;
    public string Intent { get; set; } = string.Empty;
    public List<string> Actions { get; set; } = new();
    public Dictionary<string, object> Parameters { get; set; } = new();
    public Dictionary<string, string> SelectedAgents { get; set; } = new();
    public bool IsWorkflow { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Internal class for plan execution results
/// </summary>
internal class PlanExecutionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, object> Data { get; set; } = new();
    public List<string> ExecutedActions { get; set; } = new();
    public string? WorkflowId { get; set; }
}
