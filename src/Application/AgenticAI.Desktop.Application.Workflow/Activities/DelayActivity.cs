using Elsa.Workflows.Core;
using Elsa.Workflows.Core.Attributes;
using Elsa.Workflows.Core.Models;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace AgenticAI.Desktop.Application.Workflow;

/// <summary>
/// Custom Elsa Activity for introducing delays in workflow execution
/// Provides timing control capabilities within workflows
/// </summary>
[Activity("AgenticAI", "Control", "Delay", Description = "Introduce a delay in workflow execution")]
public class DelayActivity : CodeActivity
{
    private readonly ILogger<DelayActivity> _logger;

    public DelayActivity(ILogger<DelayActivity> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Delay duration in seconds
    /// </summary>
    [Input(Description = "Delay duration in seconds", DefaultValue = 1)]
    public Input<int> DelaySeconds { get; set; } = new(1);

    /// <summary>
    /// Optional message to log during delay
    /// </summary>
    [Input(Description = "Optional message to log during delay")]
    public Input<string?> Message { get; set; } = default!;

    /// <summary>
    /// Execute the delay activity
    /// </summary>
    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var delaySeconds = DelaySeconds.Get(context);
        var message = Message.Get(context);

        if (delaySeconds <= 0)
        {
            _logger.LogWarning("Invalid delay duration: {DelaySeconds}s. Skipping delay.", delaySeconds);
            return;
        }

        var workflowId = context.WorkflowExecutionContext.Id;
        
        if (!string.IsNullOrEmpty(message))
        {
            _logger.LogInformation("Workflow {WorkflowId}: Starting delay of {DelaySeconds}s - {Message}", 
                workflowId, delaySeconds, message);
        }
        else
        {
            _logger.LogDebug("Workflow {WorkflowId}: Starting delay of {DelaySeconds}s", 
                workflowId, delaySeconds);
        }

        try
        {
            // Use cancellation token from context for proper cancellation support
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), context.CancellationToken);
            
            _logger.LogDebug("Workflow {WorkflowId}: Completed delay of {DelaySeconds}s", 
                workflowId, delaySeconds);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Workflow {WorkflowId}: Delay of {DelaySeconds}s was cancelled", 
                workflowId, delaySeconds);
            throw; // Re-throw to properly handle workflow cancellation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow {WorkflowId}: Error during delay of {DelaySeconds}s", 
                workflowId, delaySeconds);
            throw;
        }
    }
}
