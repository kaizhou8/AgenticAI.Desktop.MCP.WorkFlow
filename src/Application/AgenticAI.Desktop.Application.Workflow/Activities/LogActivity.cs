using Elsa.Workflows.Core;
using Elsa.Workflows.Core.Attributes;
using Elsa.Workflows.Core.Models;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace AgenticAI.Desktop.Application.Workflow;

/// <summary>
/// Custom Elsa Activity for logging messages during workflow execution
/// Provides structured logging capabilities within workflows
/// </summary>
[Activity("AgenticAI", "Logging", "Log Message", Description = "Log a message during workflow execution")]
public class LogActivity : CodeActivity
{
    private readonly ILogger<LogActivity> _logger;

    public LogActivity(ILogger<LogActivity> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Log level for the message
    /// </summary>
    [Input(Description = "Log level (Information, Warning, Error, Debug)", DefaultValue = "Information")]
    public Input<string> LogLevel { get; set; } = new("Information");

    /// <summary>
    /// Message to log
    /// </summary>
    [Input(Description = "The message to log")]
    public Input<string> Message { get; set; } = default!;

    /// <summary>
    /// Additional context data
    /// </summary>
    [Input(Description = "Additional context data to include in the log")]
    public Input<Dictionary<string, object>?> Context { get; set; } = default!;

    /// <summary>
    /// Execute the logging activity
    /// </summary>
    protected override ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var logLevel = LogLevel.Get(context);
        var message = Message.Get(context);
        var contextData = Context.Get(context) ?? new Dictionary<string, object>();

        // Add workflow context information
        contextData["WorkflowInstanceId"] = context.WorkflowExecutionContext.Id;
        contextData["ActivityId"] = context.Activity.Id;
        contextData["ActivityName"] = context.Activity.Type;

        try
        {
            // Log based on specified level
            switch (logLevel.ToUpperInvariant())
            {
                case "DEBUG":
                    _logger.LogDebug("Workflow Log: {Message} {@Context}", message, contextData);
                    break;
                case "WARNING":
                    _logger.LogWarning("Workflow Log: {Message} {@Context}", message, contextData);
                    break;
                case "ERROR":
                    _logger.LogError("Workflow Log: {Message} {@Context}", message, contextData);
                    break;
                case "INFORMATION":
                default:
                    _logger.LogInformation("Workflow Log: {Message} {@Context}", message, contextData);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log workflow message: {Message}", message);
        }

        return ValueTask.CompletedTask;
    }
}
