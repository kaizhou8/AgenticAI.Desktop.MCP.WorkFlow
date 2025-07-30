using AgenticAI.Desktop.Domain.Models;
using AgenticAI.Desktop.Shared.Contracts;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

namespace AgenticAI.Desktop.Application.Workflow;

/// <summary>
/// Workflow Manager - handles workflow definition, storage, and execution
/// Provides workflow orchestration capabilities for the AgenticAI system
/// </summary>
public class WorkflowManager : IWorkflowManager
{
    private readonly ILogger<WorkflowManager> _logger;
    private readonly ConcurrentDictionary<string, WorkflowDefinition> _workflows;
    private readonly ConcurrentDictionary<string, WorkflowExecutionContext> _executionContexts;
    private readonly SemaphoreSlim _executionSemaphore;

    public WorkflowManager(ILogger<WorkflowManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _workflows = new ConcurrentDictionary<string, WorkflowDefinition>();
        _executionContexts = new ConcurrentDictionary<string, WorkflowExecutionContext>();
        _executionSemaphore = new SemaphoreSlim(5, 5); // Allow up to 5 concurrent workflow executions
    }

    /// <summary>
    /// Create a new workflow
    /// </summary>
    public async Task<string> CreateWorkflowAsync(WorkflowDefinition definition, CancellationToken cancellationToken = default)
    {
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));

        await Task.CompletedTask; // For async consistency

        // Generate ID if not provided
        if (string.IsNullOrEmpty(definition.Id))
        {
            definition.Id = Guid.NewGuid().ToString();
        }

        // Set timestamps
        definition.CreatedAt = DateTime.UtcNow;
        definition.UpdatedAt = DateTime.UtcNow;

        // Validate workflow definition
        ValidateWorkflowDefinition(definition);

        // Store the workflow
        _workflows.AddOrUpdate(definition.Id, definition, (key, existing) => definition);

        _logger.LogInformation("Created workflow {WorkflowId} ({WorkflowName})", definition.Id, definition.Name);
        
        return definition.Id;
    }

    /// <summary>
    /// Update an existing workflow
    /// </summary>
    public async Task UpdateWorkflowAsync(string workflowId, WorkflowDefinition definition, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(workflowId))
            throw new ArgumentException("Workflow ID cannot be null or empty", nameof(workflowId));
        
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));

        await Task.CompletedTask; // For async consistency

        if (!_workflows.ContainsKey(workflowId))
        {
            throw new InvalidOperationException($"Workflow {workflowId} not found");
        }

        // Ensure the ID matches
        definition.Id = workflowId;
        definition.UpdatedAt = DateTime.UtcNow;

        // Validate workflow definition
        ValidateWorkflowDefinition(definition);

        // Update the workflow
        _workflows.AddOrUpdate(workflowId, definition, (key, existing) => 
        {
            // Preserve creation date
            definition.CreatedAt = existing.CreatedAt;
            return definition;
        });

        _logger.LogInformation("Updated workflow {WorkflowId} ({WorkflowName})", workflowId, definition.Name);
    }

    /// <summary>
    /// Delete a workflow
    /// </summary>
    public async Task DeleteWorkflowAsync(string workflowId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(workflowId))
            throw new ArgumentException("Workflow ID cannot be null or empty", nameof(workflowId));

        await Task.CompletedTask; // For async consistency

        if (_workflows.TryRemove(workflowId, out var workflow))
        {
            _logger.LogInformation("Deleted workflow {WorkflowId} ({WorkflowName})", workflowId, workflow.Name);
        }
        else
        {
            _logger.LogWarning("Workflow {WorkflowId} not found for deletion", workflowId);
        }
    }

    /// <summary>
    /// Get workflow by ID
    /// </summary>
    public async Task<WorkflowDefinition?> GetWorkflowAsync(string workflowId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(workflowId))
            throw new ArgumentException("Workflow ID cannot be null or empty", nameof(workflowId));

        await Task.CompletedTask; // For async consistency

        _workflows.TryGetValue(workflowId, out var workflow);
        return workflow;
    }

    /// <summary>
    /// List all workflows
    /// </summary>
    public async Task<IReadOnlyList<WorkflowSummary>> ListWorkflowsAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // For async consistency

        var summaries = _workflows.Values.Select(workflow => new WorkflowSummary
        {
            Id = workflow.Id,
            Name = workflow.Name,
            Description = workflow.Description,
            Version = workflow.Version,
            CreatedBy = workflow.CreatedBy,
            CreatedAt = workflow.CreatedAt,
            UpdatedAt = workflow.UpdatedAt,
            IsActive = workflow.IsActive,
            Category = workflow.Category,
            Tags = workflow.Tags.ToList(),
            ExecutionCount = GetExecutionCount(workflow.Id),
            LastExecutedAt = GetLastExecutionTime(workflow.Id)
        }).ToList();

        return summaries.AsReadOnly();
    }

    /// <summary>
    /// Execute a workflow
    /// </summary>
    public async Task<WorkflowExecutionResult> ExecuteWorkflowAsync(string workflowId, Dictionary<string, object>? parameters = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(workflowId))
            throw new ArgumentException("Workflow ID cannot be null or empty", nameof(workflowId));

        var executionId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        _logger.LogInformation("Starting execution {ExecutionId} for workflow {WorkflowId}", executionId, workflowId);

        try
        {
            await _executionSemaphore.WaitAsync(cancellationToken);

            // Get workflow definition
            if (!_workflows.TryGetValue(workflowId, out var workflow))
            {
                throw new InvalidOperationException($"Workflow {workflowId} not found");
            }

            if (!workflow.IsActive)
            {
                throw new InvalidOperationException($"Workflow {workflowId} is not active");
            }

            // Create execution context
            var context = new WorkflowExecutionContext
            {
                ExecutionId = executionId,
                WorkflowId = workflowId,
                WorkflowDefinition = workflow,
                Parameters = parameters ?? new Dictionary<string, object>(),
                StartTime = startTime,
                Status = WorkflowExecutionStatus.Running
            };

            _executionContexts.TryAdd(executionId, context);

            // Execute the workflow steps
            var result = await ExecuteWorkflowStepsAsync(context, cancellationToken);

            // Update execution context
            context.EndTime = DateTime.UtcNow;
            context.Status = result.Success ? WorkflowExecutionStatus.Completed : WorkflowExecutionStatus.Failed;
            context.Result = result;

            _logger.LogInformation("Completed execution {ExecutionId} for workflow {WorkflowId} in {Duration}ms", 
                executionId, workflowId, result.Duration.TotalMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute workflow {WorkflowId}", workflowId);
            
            return new WorkflowExecutionResult
            {
                WorkflowId = workflowId,
                ExecutionId = executionId,
                Success = false,
                Message = $"Workflow execution failed: {ex.Message}",
                StartedAt = startTime,
                CompletedAt = DateTime.UtcNow,
                Duration = DateTime.UtcNow - startTime,
                ErrorCode = "EXECUTION_FAILED"
            };
        }
        finally
        {
            _executionSemaphore.Release();
            
            // Clean up execution context after some time
            _ = Task.Delay(TimeSpan.FromMinutes(30), cancellationToken)
                .ContinueWith(_ => _executionContexts.TryRemove(executionId, out WorkflowExecutionContext _), cancellationToken);
        }
    }

    /// <summary>
    /// Execute workflow steps
    /// </summary>
    private async Task<WorkflowExecutionResult> ExecuteWorkflowStepsAsync(WorkflowExecutionContext context, CancellationToken cancellationToken)
    {
        var result = new WorkflowExecutionResult
        {
            WorkflowId = context.WorkflowId,
            ExecutionId = context.ExecutionId,
            Success = true,
            Message = "Workflow executed successfully",
            StartedAt = context.StartTime,
            StepResults = new List<WorkflowStepResult>()
        };

        try
        {
            // Parse workflow data (simplified implementation)
            var workflowSteps = ParseWorkflowSteps(context.WorkflowDefinition.WorkflowData);
            
            foreach (var step in workflowSteps)
            {
                var stepStartTime = DateTime.UtcNow;
                
                _logger.LogDebug("Executing step {StepId} ({StepName}) in workflow {WorkflowId}", 
                    step.Id, step.Name, context.WorkflowId);

                try
                {
                    // Execute the step (simplified implementation)
                    var stepResult = await ExecuteWorkflowStepAsync(step, context, cancellationToken);
                    
                    stepResult.StartedAt = stepStartTime;
                    stepResult.CompletedAt = DateTime.UtcNow;
                    stepResult.Duration = stepResult.CompletedAt - stepResult.StartedAt;
                    
                    result.StepResults.Add(stepResult);
                    
                    if (!stepResult.Success)
                    {
                        result.Success = false;
                        result.Message = $"Step {step.Name} failed: {stepResult.Message}";
                        break;
                    }
                }
                catch (Exception ex)
                {
                    var stepResult = new WorkflowStepResult
                    {
                        StepId = step.Id,
                        StepName = step.Name,
                        Success = false,
                        Message = $"Step execution failed: {ex.Message}",
                        StartedAt = stepStartTime,
                        CompletedAt = DateTime.UtcNow,
                        Duration = DateTime.UtcNow - stepStartTime
                    };
                    
                    result.StepResults.Add(stepResult);
                    result.Success = false;
                    result.Message = $"Step {step.Name} failed with exception: {ex.Message}";
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Workflow execution failed: {ex.Message}";
            _logger.LogError(ex, "Error executing workflow steps for {WorkflowId}", context.WorkflowId);
        }
        finally
        {
            result.CompletedAt = DateTime.UtcNow;
            result.Duration = result.CompletedAt - result.StartedAt;
        }

        return result;
    }

    /// <summary>
    /// Execute a single workflow step
    /// </summary>
    private async Task<WorkflowStepResult> ExecuteWorkflowStepAsync(WorkflowStep step, WorkflowExecutionContext context, CancellationToken cancellationToken)
    {
        // Simplified step execution - in a real implementation, this would dispatch to appropriate agents
        await Task.Delay(100, cancellationToken); // Simulate work
        
        return new WorkflowStepResult
        {
            StepId = step.Id,
            StepName = step.Name,
            Success = true,
            Message = "Step executed successfully",
            OutputData = new Dictionary<string, object>
            {
                { "step_type", step.Type },
                { "execution_time", DateTime.UtcNow }
            }
        };
    }

    /// <summary>
    /// Parse workflow steps from workflow data
    /// </summary>
    private List<WorkflowStep> ParseWorkflowSteps(string workflowData)
    {
        if (string.IsNullOrEmpty(workflowData))
        {
            return new List<WorkflowStep>();
        }

        try
        {
            // Simplified parsing - in a real implementation, this would parse actual workflow definition format
            var steps = JsonSerializer.Deserialize<List<WorkflowStep>>(workflowData);
            return steps ?? new List<WorkflowStep>();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse workflow data, returning empty steps list");
            return new List<WorkflowStep>();
        }
    }

    /// <summary>
    /// Validate workflow definition
    /// </summary>
    private void ValidateWorkflowDefinition(WorkflowDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            throw new ArgumentException("Workflow name cannot be empty");
        }

        if (string.IsNullOrWhiteSpace(definition.Version))
        {
            definition.Version = "1.0.0";
        }

        // Additional validation logic can be added here
    }

    /// <summary>
    /// Get execution count for a workflow
    /// </summary>
    private int GetExecutionCount(string workflowId)
    {
        // Simplified implementation - in a real system, this would query execution history
        return _executionContexts.Values.Count(ctx => ctx.WorkflowId == workflowId);
    }

    /// <summary>
    /// Get last execution time for a workflow
    /// </summary>
    private DateTime? GetLastExecutionTime(string workflowId)
    {
        // Simplified implementation - in a real system, this would query execution history
        var lastExecution = _executionContexts.Values
            .Where(ctx => ctx.WorkflowId == workflowId)
            .OrderByDescending(ctx => ctx.StartTime)
            .FirstOrDefault();

        return lastExecution?.StartTime;
    }

    /// <summary>
    /// Get workflow execution statistics
    /// </summary>
    public WorkflowExecutionStats GetExecutionStats()
    {
        return new WorkflowExecutionStats
        {
            TotalWorkflows = _workflows.Count,
            ActiveWorkflows = _workflows.Values.Count(w => w.IsActive),
            RunningExecutions = _executionContexts.Values.Count(ctx => ctx.Status == WorkflowExecutionStatus.Running),
            TotalExecutions = _executionContexts.Count
        };
    }
}

/// <summary>
/// Workflow execution context
/// </summary>
internal class WorkflowExecutionContext
{
    public string ExecutionId { get; set; } = string.Empty;
    public string WorkflowId { get; set; } = string.Empty;
    public WorkflowDefinition WorkflowDefinition { get; set; } = null!;
    public Dictionary<string, object> Parameters { get; set; } = new();
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public WorkflowExecutionStatus Status { get; set; }
    public WorkflowExecutionResult? Result { get; set; }
}

/// <summary>
/// Workflow execution status
/// </summary>
internal enum WorkflowExecutionStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}

/// <summary>
/// Workflow step definition
/// </summary>
internal class WorkflowStep
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
}

/// <summary>
/// Workflow execution statistics
/// </summary>
public class WorkflowExecutionStats
{
    public int TotalWorkflows { get; set; }
    public int ActiveWorkflows { get; set; }
    public int RunningExecutions { get; set; }
    public int TotalExecutions { get; set; }
}
