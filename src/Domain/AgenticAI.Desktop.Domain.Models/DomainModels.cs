using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AgenticAI.Desktop.Domain.Models;

/// <summary>
/// Represents an agent capability
/// </summary>
public class AgentCapability
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
    public string Version { get; set; } = "1.0.0";
}

/// <summary>
/// Agent status enumeration
/// </summary>
public enum AgentStatus
{
    Unknown = 0,
    Initializing = 1,
    Ready = 2,
    Busy = 3,
    Error = 4,
    Stopping = 5,
    Stopped = 6,
    Shutdown = 7
}

/// <summary>
/// Agent command for execution
/// </summary>
public class AgentCommand
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Type { get; set; } = string.Empty; // Command type/category
    public string Action { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string RequestId { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Result of agent command execution
/// </summary>
public class AgentExecutionResult
{
    public string ExecutionId { get; set; } = Guid.NewGuid().ToString(); // Unique execution identifier
    public string AgentId { get; set; } = string.Empty; // Agent that executed the command
    public string CommandId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, object> Data { get; set; } = new();
    public Dictionary<string, object> OutputData { get; set; } = new(); // Output data for command results
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
    public TimeSpan Duration { get; set; }
    public string? ErrorCode { get; set; }
    public Exception? Exception { get; set; }
}

/// <summary>
/// Agent health status
/// </summary>
public class AgentHealthStatus
{
    public string AgentId { get; set; } = string.Empty;
    public bool IsHealthy { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime LastCheckTime { get; set; } = DateTime.UtcNow;
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
    public TimeSpan ResponseTime { get; set; }
    public double MemoryUsage { get; set; }
    public double CpuUsage { get; set; }
    public int ErrorCount { get; set; }
    public string? LastError { get; set; }
    public Dictionary<string, object> Metrics { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
    public List<string> Issues { get; set; } = new();
}

/// <summary>
/// Agent information summary
/// </summary>
public class AgentInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public AgentStatus Status { get; set; }
    public List<AgentCapability> Capabilities { get; set; } = new();
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow; // Last heartbeat timestamp
    public Dictionary<string, object> Metadata { get; set; } = new(); // Additional agent metadata
}

/// <summary>
/// Director response to user requests
/// </summary>
public class DirectorResponse
{
    public string RequestId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, object> Data { get; set; } = new();
    public List<string> ExecutedActions { get; set; } = new();
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
    public TimeSpan Duration { get; set; }
    public string? WorkflowId { get; set; }
}

/// <summary>
/// Workflow execution result
/// </summary>
public class WorkflowExecutionResult
{
    public string WorkflowId { get; set; } = string.Empty;
    public string ExecutionId { get; set; } = Guid.NewGuid().ToString();
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, object> OutputData { get; set; } = new();
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
    public TimeSpan Duration { get; set; }
    public List<WorkflowStepResult> StepResults { get; set; } = new();
    public string? ErrorCode { get; set; }
}

/// <summary>
/// Individual workflow step result
/// </summary>
public class WorkflowStepResult
{
    public string StepId { get; set; } = string.Empty;
    public string StepName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, object> OutputData { get; set; } = new();
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Workflow definition
/// </summary>
public class WorkflowDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public string Category { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public Dictionary<string, object> InputParameters { get; set; } = new();
    public Dictionary<string, object> OutputParameters { get; set; } = new();
    public string WorkflowData { get; set; } = string.Empty; // JSON serialized workflow
}

/// <summary>
/// Workflow summary for listing
/// </summary>
public class WorkflowSummary
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; }
    public string Category { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public int ExecutionCount { get; set; }
    public DateTime? LastExecutedAt { get; set; }
}

/// <summary>
/// MCP protocol message
/// </summary>
public class McpMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Type { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public Dictionary<string, object> Payload { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// MCP protocol response
/// </summary>
public class McpResponse
{
    public string MessageId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, object> Data { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? ErrorCode { get; set; }
}

/// <summary>
/// Agent event for notifications
/// </summary>
public class AgentEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string AgentId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, object> Data { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Severity { get; set; } = "Info";
}

/// <summary>
/// LLM message for conversation
/// </summary>
public class LlmMessage
{
    public string Role { get; set; } = string.Empty; // system, user, assistant
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// LLM response
/// </summary>
public class LlmResponse
{
    public string Content { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Model { get; set; } = string.Empty;
    public int TokensUsed { get; set; }
    public TimeSpan Duration { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Metadata { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// LLM processing options
/// </summary>
public class LlmOptions
{
    public string? Model { get; set; }
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 1000;
    public string? SystemPrompt { get; set; }
    public Dictionary<string, object> AdditionalParameters { get; set; } = new();
}

/// <summary>
/// LLM provider information
/// </summary>
public class LlmProviderInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> SupportedModels { get; set; } = new();
    public bool IsAvailable { get; set; }
    public Dictionary<string, object> Configuration { get; set; } = new();
}
