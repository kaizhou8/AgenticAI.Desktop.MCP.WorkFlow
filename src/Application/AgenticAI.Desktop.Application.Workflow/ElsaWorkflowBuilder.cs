using AgenticAI.Desktop.Domain.Models;
using Elsa.Workflows.Core;
using Elsa.Workflows.Core.Activities;
using Elsa.Workflows.Core.Models;
using Microsoft.Extensions.Logging;

namespace AgenticAI.Desktop.Application.Workflow;

/// <summary>
/// Workflow Builder for creating AgenticAI workflows with Elsa integration
/// Provides a fluent API for building complex workflows
/// </summary>
public class ElsaWorkflowBuilder
{
    private readonly ILogger<ElsaWorkflowBuilder> _logger;
    private readonly List<IActivity> _activities;
    private string _workflowName = string.Empty;
    private string _workflowDescription = string.Empty;
    private Dictionary<string, object> _variables;

    public ElsaWorkflowBuilder(ILogger<ElsaWorkflowBuilder> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _activities = new List<IActivity>();
        _variables = new Dictionary<string, object>();
    }

    /// <summary>
    /// Set workflow name and description
    /// </summary>
    public ElsaWorkflowBuilder WithName(string name, string? description = null)
    {
        _workflowName = name ?? throw new ArgumentNullException(nameof(name));
        _workflowDescription = description ?? string.Empty;
        return this;
    }

    /// <summary>
    /// Add a variable to the workflow
    /// </summary>
    public ElsaWorkflowBuilder WithVariable(string name, object value)
    {
        _variables[name] = value;
        return this;
    }

    /// <summary>
    /// Add a log activity to the workflow
    /// </summary>
    public ElsaWorkflowBuilder AddLog(string message, string logLevel = "Information", Dictionary<string, object>? context = null)
    {
        var logActivity = new LogActivity(_logger.CreateLogger<LogActivity>())
        {
            Message = new Input<string>(message),
            LogLevel = new Input<string>(logLevel),
            Context = new Input<Dictionary<string, object>?>(context)
        };

        _activities.Add(logActivity);
        return this;
    }

    /// <summary>
    /// Add a delay activity to the workflow
    /// </summary>
    public ElsaWorkflowBuilder AddDelay(int seconds, string? message = null)
    {
        var delayActivity = new DelayActivity(_logger.CreateLogger<DelayActivity>())
        {
            DelaySeconds = new Input<int>(seconds),
            Message = new Input<string?>(message)
        };

        _activities.Add(delayActivity);
        return this;
    }

    /// <summary>
    /// Add an agent execution activity to the workflow
    /// </summary>
    public ElsaWorkflowBuilder AddAgentExecution(string agentId, string commandType, Dictionary<string, object>? parameters = null, int timeoutSeconds = 30)
    {
        // Note: This would require proper DI setup in a real implementation
        // For now, we'll create a placeholder that can be properly injected later
        var agentActivity = new AgentExecuteActivity(
            _logger.CreateLogger<AgentExecuteActivity>(), 
            null!) // This would be injected properly in real usage
        {
            AgentId = new Input<string>(agentId),
            CommandType = new Input<string>(commandType),
            Parameters = new Input<Dictionary<string, object>?>(parameters),
            TimeoutSeconds = new Input<int>(timeoutSeconds)
        };

        _activities.Add(agentActivity);
        return this;
    }

    /// <summary>
    /// Add a conditional branch to the workflow
    /// </summary>
    public ElsaWorkflowBuilder AddCondition(string condition, Action<ElsaWorkflowBuilder> trueBuilder, Action<ElsaWorkflowBuilder>? falseBuilder = null)
    {
        var trueBranch = new ElsaWorkflowBuilder(_logger);
        trueBuilder(trueBranch);

        ElsaWorkflowBuilder? falseBranch = null;
        if (falseBuilder != null)
        {
            falseBranch = new ElsaWorkflowBuilder(_logger);
            falseBuilder(falseBranch);
        }

        // Create an If activity
        var ifActivity = new If
        {
            Condition = new Input<bool>(condition),
            Then = new Sequence { Activities = trueBranch._activities },
            Else = falseBranch != null ? new Sequence { Activities = falseBranch._activities } : null
        };

        _activities.Add(ifActivity);
        return this;
    }

    /// <summary>
    /// Add a parallel execution block
    /// </summary>
    public ElsaWorkflowBuilder AddParallel(params Action<ElsaWorkflowBuilder>[] branchBuilders)
    {
        var branches = new List<IActivity>();

        foreach (var branchBuilder in branchBuilders)
        {
            var branch = new ElsaWorkflowBuilder(_logger);
            branchBuilder(branch);
            branches.Add(new Sequence { Activities = branch._activities });
        }

        var parallelActivity = new Parallel
        {
            Branches = branches
        };

        _activities.Add(parallelActivity);
        return this;
    }

    /// <summary>
    /// Add a loop activity
    /// </summary>
    public ElsaWorkflowBuilder AddLoop(int iterations, Action<ElsaWorkflowBuilder> bodyBuilder)
    {
        var bodyBranch = new ElsaWorkflowBuilder(_logger);
        bodyBuilder(bodyBranch);

        var forActivity = new For
        {
            Start = new Input<int>(0),
            End = new Input<int>(iterations),
            Step = new Input<int>(1),
            Body = new Sequence { Activities = bodyBranch._activities }
        };

        _activities.Add(forActivity);
        return this;
    }

    /// <summary>
    /// Build the workflow definition
    /// </summary>
    public WorkflowDefinition Build()
    {
        if (string.IsNullOrEmpty(_workflowName))
        {
            throw new InvalidOperationException("Workflow name must be specified");
        }

        if (_activities.Count == 0)
        {
            throw new InvalidOperationException("Workflow must contain at least one activity");
        }

        // Create the main workflow sequence
        var mainSequence = new Sequence
        {
            Activities = _activities.ToList()
        };

        // Serialize the workflow to JSON (simplified)
        var workflowData = SerializeWorkflow(mainSequence);

        var workflow = new WorkflowDefinition
        {
            Id = Guid.NewGuid().ToString(),
            Name = _workflowName,
            Description = _workflowDescription,
            Version = "1.0.0",
            Data = workflowData,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _logger.LogInformation("Built workflow '{WorkflowName}' with {ActivityCount} activities", 
            _workflowName, _activities.Count);

        return workflow;
    }

    /// <summary>
    /// Serialize workflow to JSON format
    /// </summary>
    private string SerializeWorkflow(IActivity rootActivity)
    {
        // This is a simplified serialization
        // In a real implementation, this would use Elsa's proper serialization mechanisms
        var workflowData = new
        {
            Type = "Sequence",
            Activities = _activities.Select(a => new
            {
                Type = a.GetType().Name,
                Id = a.Id,
                // Additional activity properties would be serialized here
            }).ToList(),
            Variables = _variables
        };

        return System.Text.Json.JsonSerializer.Serialize(workflowData, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    /// <summary>
    /// Create a new workflow builder instance
    /// </summary>
    public static ElsaWorkflowBuilder Create(ILogger<ElsaWorkflowBuilder> logger)
    {
        return new ElsaWorkflowBuilder(logger);
    }
}
