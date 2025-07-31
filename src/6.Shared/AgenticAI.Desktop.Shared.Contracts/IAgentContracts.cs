using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgenticAI.Desktop.Domain.Models;

namespace AgenticAI.Desktop.Shared.Contracts;

/// <summary>
/// Base interface for all agents in the system
/// </summary>
public interface IAgent
{
    /// <summary>
    /// Unique identifier for the agent
    /// </summary>
    string Id { get; }
    
    /// <summary>
    /// Display name of the agent
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// Description of agent capabilities
    /// </summary>
    string Description { get; }
    
    /// <summary>
    /// Version of the agent
    /// </summary>
    string Version { get; }
    
    /// <summary>
    /// List of capabilities this agent provides
    /// </summary>
    IReadOnlyList<AgentCapability> Capabilities { get; }
    
    /// <summary>
    /// Current status of the agent
    /// </summary>
    AgentStatus Status { get; }
    
    /// <summary>
    /// Initialize the agent
    /// </summary>
    Task<bool> InitializeAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Execute a command on the agent
    /// </summary>
    Task<AgentExecutionResult> ExecuteAsync(AgentCommand command, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Shutdown the agent gracefully
    /// </summary>
    Task ShutdownAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Health check for the agent
    /// </summary>
    Task<AgentHealthStatus> GetHealthStatusAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for the Director scheduling engine
/// </summary>
public interface IDirector
{
    /// <summary>
    /// Process a natural language request
    /// </summary>
    Task<DirectorResponse> ProcessRequestAsync(string request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Execute a workflow
    /// </summary>
    Task<WorkflowExecutionResult> ExecuteWorkflowAsync(string workflowId, Dictionary<string, object>? parameters = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Register an agent with the director
    /// </summary>
    Task RegisterAgentAsync(IAgent agent, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Unregister an agent from the director
    /// </summary>
    Task UnregisterAgentAsync(string agentId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get list of available agents
    /// </summary>
    Task<IReadOnlyList<AgentInfo>> GetAvailableAgentsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for the MCP protocol engine
/// </summary>
public interface IMcpProtocolEngine
{
    /// <summary>
    /// Send a message to an agent
    /// </summary>
    Task<McpResponse> SendMessageAsync(string agentId, McpMessage message, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Subscribe to agent events
    /// </summary>
    Task SubscribeToAgentEventsAsync(string agentId, Func<AgentEvent, Task> eventHandler, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Start the protocol engine
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Stop the protocol engine
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for workflow management
/// </summary>
public interface IWorkflowManager
{
    /// <summary>
    /// Create a new workflow
    /// </summary>
    Task<string> CreateWorkflowAsync(WorkflowDefinition definition, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Update an existing workflow
    /// </summary>
    Task UpdateWorkflowAsync(string workflowId, WorkflowDefinition definition, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Delete a workflow
    /// </summary>
    Task DeleteWorkflowAsync(string workflowId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get workflow by ID
    /// </summary>
    Task<WorkflowDefinition?> GetWorkflowAsync(string workflowId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// List all workflows
    /// </summary>
    Task<IReadOnlyList<WorkflowSummary>> ListWorkflowsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Execute a workflow
    /// </summary>
    Task<WorkflowExecutionResult> ExecuteWorkflowAsync(string workflowId, Dictionary<string, object>? parameters = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for LLM integration
/// </summary>
public interface ILlmAdapter
{
    /// <summary>
    /// Process a text prompt and return response
    /// </summary>
    Task<LlmResponse> ProcessPromptAsync(string prompt, LlmOptions? options = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Process a conversation and return response
    /// </summary>
    Task<LlmResponse> ProcessConversationAsync(IReadOnlyList<LlmMessage> messages, LlmOptions? options = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get available LLM providers
    /// </summary>
    Task<IReadOnlyList<LlmProviderInfo>> GetAvailableProvidersAsync(CancellationToken cancellationToken = default);
}
