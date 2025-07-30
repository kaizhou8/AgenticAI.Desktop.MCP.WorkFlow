using AgenticAI.Desktop.Domain.Models;
using AgenticAI.Desktop.Shared.Contracts;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

namespace AgenticAI.Desktop.Application.Workflow;

/// <summary>
/// Enhanced Workflow Manager with future Elsa Workflows integration support
/// Provides professional workflow orchestration capabilities for the AgenticAI system
/// </summary>
public class ElsaWorkflowManager : IWorkflowManager
{
    private readonly ILogger<ElsaWorkflowManager> _logger;
    private readonly ConcurrentDictionary<string, WorkflowDefinition> _workflows;
    private readonly ConcurrentDictionary<string, WorkflowExecutionContext> _executionContexts;
    private readonly SemaphoreSlim _executionSemaphore;

    public ElsaWorkflowManager(ILogger<ElsaWorkflowManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _workflows = new ConcurrentDictionary<string, WorkflowDefinition>();
        _executionContexts = new ConcurrentDictionary<string, WorkflowExecutionContext>();
        _executionSemaphore = new SemaphoreSlim(10, 10); // Allow up to 10 concurrent workflow executions
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

        // Store the workflow definition
        _workflows.AddOrUpdate(definition.Id, definition, (key, existing) => definition);

        _logger.LogInformation("Created workflow {WorkflowId} ({WorkflowName})", 
            definition.Id, definition.Name);
        
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

        if (!_workflows.ContainsKey(workflowId))
        {
            throw new InvalidOperationException($"Workflow {workflowId} not found");
        }

        // Ensure the ID matches
        definition.Id = workflowId;
        definition.UpdatedAt = DateTime.UtcNow;

        // Validate workflow definition
        ValidateWorkflowDefinition(definition);

        // Update the workflow definition
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

        try
        {
            // Delete from Elsa workflow definition service
            var elsaWorkflow = await _workflowDefinitionService.FindByDefinitionIdAsync(workflowId, cancellationToken);
            if (elsaWorkflow != null)
            {
                await _workflowDefinitionService.DeleteAsync(elsaWorkflow, cancellationToken);
            }

            // Remove from AgenticAI workflow definitions
            if (_agenticWorkflows.TryRemove(workflowId, out var workflow))
            {
                _logger.LogInformation("Deleted workflow {WorkflowId} ({WorkflowName})", workflowId, workflow.Name);
            }
            else
            {
                _logger.LogWarning("Workflow {WorkflowId} not found for deletion", workflowId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete workflow {WorkflowId}", workflowId);
            throw;
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

        return _agenticWorkflows.TryGetValue(workflowId, out var workflow) ? workflow : null;
    }

    /// <summary>
    /// List all workflows
    /// </summary>
    public async Task<IEnumerable<WorkflowDefinition>> ListWorkflowsAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // For async consistency

        var workflows = _agenticWorkflows.Values.ToList();
        
        _logger.LogDebug("Retrieved {WorkflowCount} workflows", workflows.Count);
        
        return workflows;
    }

    /// <summary>
    /// Execute a workflow using Elsa runtime
    /// </summary>
    public async Task<WorkflowExecutionResult> ExecuteWorkflowAsync(string workflowId, Dictionary<string, object>? parameters = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(workflowId))
            throw new ArgumentException("Workflow ID cannot be null or empty", nameof(workflowId));

        parameters ??= new Dictionary<string, object>();

        await _executionSemaphore.WaitAsync(cancellationToken);
        
        try
        {
            var executionId = Guid.NewGuid().ToString();
            _logger.LogInformation("Starting workflow execution {ExecutionId} for workflow {WorkflowId}", 
                executionId, workflowId);

            // Get the workflow definition
            var workflow = await GetWorkflowAsync(workflowId, cancellationToken);
            if (workflow == null)
            {
                return new WorkflowExecutionResult
                {
                    ExecutionId = executionId,
                    WorkflowId = workflowId,
                    Success = false,
                    Message = $"Workflow {workflowId} not found",
                    StartTime = DateTime.UtcNow,
                    EndTime = DateTime.UtcNow
                };
            }

            var startTime = DateTime.UtcNow;

            try
            {
                // Start workflow execution using Elsa runtime
                var workflowRequest = new StartWorkflowRequest
                {
                    WorkflowDefinitionId = workflowId,
                    Input = parameters.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value),
                    CorrelationId = executionId
                };

                var runWorkflowResult = await _workflowRuntime.StartWorkflowAsync(workflowRequest, cancellationToken);
                
                var endTime = DateTime.UtcNow;
                var duration = endTime - startTime;

                _logger.LogInformation("Completed workflow execution {ExecutionId} for workflow {WorkflowId} in {Duration}ms", 
                    executionId, workflowId, duration.TotalMilliseconds);

                return new WorkflowExecutionResult
                {
                    ExecutionId = executionId,
                    WorkflowId = workflowId,
                    Success = runWorkflowResult.WorkflowInstance.WorkflowState.Status == WorkflowStatus.Finished,
                    Message = runWorkflowResult.WorkflowInstance.WorkflowState.Status == WorkflowStatus.Finished 
                        ? "Workflow completed successfully" 
                        : $"Workflow status: {runWorkflowResult.WorkflowInstance.WorkflowState.Status}",
                    StartTime = startTime,
                    EndTime = endTime,
                    Duration = duration,
                    Output = runWorkflowResult.WorkflowInstance.Output ?? new Dictionary<string, object>()
                };
            }
            catch (Exception ex)
            {
                var endTime = DateTime.UtcNow;
                var duration = endTime - startTime;

                _logger.LogError(ex, "Failed to execute workflow {WorkflowId} (execution {ExecutionId})", 
                    workflowId, executionId);

                return new WorkflowExecutionResult
                {
                    ExecutionId = executionId,
                    WorkflowId = workflowId,
                    Success = false,
                    Message = $"Workflow execution failed: {ex.Message}",
                    StartTime = startTime,
                    EndTime = endTime,
                    Duration = duration,
                    Output = new Dictionary<string, object>()
                };
            }
        }
        finally
        {
            _executionSemaphore.Release();
        }
    }

    /// <summary>
    /// Convert AgenticAI workflow definition to Elsa workflow definition
    /// </summary>
    private async Task<WorkflowDefinitionVersion> ConvertToElsaWorkflowAsync(WorkflowDefinition agenticWorkflow, CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // For async consistency

        // Create a basic Elsa workflow definition
        // In a real implementation, this would parse the workflow data and create appropriate Elsa activities
        var elsaWorkflow = new WorkflowDefinitionVersion
        {
            Id = Guid.NewGuid().ToString(),
            DefinitionId = agenticWorkflow.Id,
            Name = agenticWorkflow.Name,
            Description = agenticWorkflow.Description,
            Version = 1,
            IsLatest = true,
            IsPublished = agenticWorkflow.IsActive,
            CreatedAt = agenticWorkflow.CreatedAt,
            // Workflow definition would be built from agenticWorkflow.Data
            // This is a simplified implementation
        };

        return elsaWorkflow;
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
    /// Get workflow execution statistics
    /// </summary>
    public async Task<WorkflowExecutionStats> GetExecutionStatsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Get statistics from Elsa workflow instance store
            var totalWorkflows = _agenticWorkflows.Count;
            var activeWorkflows = _agenticWorkflows.Values.Count(w => w.IsActive);
            
            // Query running workflow instances from Elsa
            var runningInstances = await _workflowInstanceStore.CountAsync(
                new WorkflowInstanceFilter { WorkflowStatus = WorkflowStatus.Running }, 
                cancellationToken);

            var totalExecutions = await _workflowInstanceStore.CountAsync(
                new WorkflowInstanceFilter(), 
                cancellationToken);

            return new WorkflowExecutionStats
            {
                TotalWorkflows = totalWorkflows,
                ActiveWorkflows = activeWorkflows,
                RunningExecutions = (int)runningInstances,
                TotalExecutions = (int)totalExecutions
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get workflow execution statistics");
            
            // Return basic statistics if Elsa queries fail
            return new WorkflowExecutionStats
            {
                TotalWorkflows = _agenticWorkflows.Count,
                ActiveWorkflows = _agenticWorkflows.Values.Count(w => w.IsActive),
                RunningExecutions = 0,
                TotalExecutions = 0
            };
        }
    }
}
