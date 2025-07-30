using Elsa.Workflows.Core;
using Elsa.Workflows.Core.Attributes;
using Elsa.Workflows.Core.Models;
using AgenticAI.Desktop.Domain.Models;
using AgenticAI.Desktop.Shared.Contracts;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace AgenticAI.Desktop.Application.Workflow;

/// <summary>
/// Custom Elsa Activity for executing Agent commands
/// Integrates AgenticAI Agent system with Elsa Workflows
/// </summary>
[Activity("AgenticAI", "Agent", "Execute Agent Command", Description = "Execute a command using an AgenticAI Agent")]
public class AgentExecuteActivity : CodeActivity<AgentExecutionResult>
{
    private readonly ILogger<AgentExecuteActivity> _logger;
    private readonly IAgentRegistry _agentRegistry;

    public AgentExecuteActivity(ILogger<AgentExecuteActivity> logger, IAgentRegistry agentRegistry)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _agentRegistry = agentRegistry ?? throw new ArgumentNullException(nameof(agentRegistry));
    }

    /// <summary>
    /// Agent ID to execute the command
    /// </summary>
    [Input(Description = "The ID of the agent to execute the command")]
    public Input<string> AgentId { get; set; } = default!;

    /// <summary>
    /// Command type to execute
    /// </summary>
    [Input(Description = "The type of command to execute")]
    public Input<string> CommandType { get; set; } = default!;

    /// <summary>
    /// Command parameters
    /// </summary>
    [Input(Description = "Parameters for the command execution")]
    public Input<Dictionary<string, object>?> Parameters { get; set; } = default!;

    /// <summary>
    /// Timeout for command execution
    /// </summary>
    [Input(Description = "Timeout in seconds for command execution", DefaultValue = 30)]
    public Input<int> TimeoutSeconds { get; set; } = new(30);

    /// <summary>
    /// Execute the agent command
    /// </summary>
    protected override async ValueTask<AgentExecutionResult> ExecuteAsync(ActivityExecutionContext context)
    {
        var agentId = AgentId.Get(context);
        var commandType = CommandType.Get(context);
        var parameters = Parameters.Get(context) ?? new Dictionary<string, object>();
        var timeoutSeconds = TimeoutSeconds.Get(context);

        _logger.LogInformation("Executing agent command: Agent={AgentId}, Command={CommandType}", 
            agentId, commandType);

        try
        {
            // Get the agent from registry
            var agent = await _agentRegistry.GetAgentAsync(agentId);
            if (agent == null)
            {
                var errorResult = new AgentExecutionResult
                {
                    Success = false,
                    Message = $"Agent {agentId} not found",
                    ErrorCode = "AGENT_NOT_FOUND"
                };

                _logger.LogError("Agent {AgentId} not found", agentId);
                return errorResult;
            }

            // Create command
            var command = new AgentCommand
            {
                Id = Guid.NewGuid().ToString(),
                Type = commandType,
                Parameters = parameters,
                CreatedAt = DateTime.UtcNow
            };

            // Execute command with timeout
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            var result = await agent.ExecuteAsync(command, cts.Token);

            _logger.LogInformation("Agent command executed: Agent={AgentId}, Command={CommandType}, Success={Success}", 
                agentId, commandType, result.Success);

            return result;
        }
        catch (OperationCanceledException)
        {
            var timeoutResult = new AgentExecutionResult
            {
                Success = false,
                Message = $"Agent command timed out after {timeoutSeconds} seconds",
                ErrorCode = "TIMEOUT"
            };

            _logger.LogWarning("Agent command timed out: Agent={AgentId}, Command={CommandType}, Timeout={TimeoutSeconds}s", 
                agentId, commandType, timeoutSeconds);

            return timeoutResult;
        }
        catch (Exception ex)
        {
            var errorResult = new AgentExecutionResult
            {
                Success = false,
                Message = $"Agent command execution failed: {ex.Message}",
                ErrorCode = "EXECUTION_ERROR"
            };

            _logger.LogError(ex, "Agent command execution failed: Agent={AgentId}, Command={CommandType}", 
                agentId, commandType);

            return errorResult;
        }
    }
}
