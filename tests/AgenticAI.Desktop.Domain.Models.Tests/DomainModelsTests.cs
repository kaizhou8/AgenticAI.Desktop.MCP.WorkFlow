using AgenticAI.Desktop.Domain.Models;
using FluentAssertions;
using Xunit;

namespace AgenticAI.Desktop.Domain.Models.Tests;

/// <summary>
/// Unit tests for Domain Models to ensure consistency and proper initialization
/// </summary>
public class DomainModelsTests
{
    [Fact]
    public void AgentCapability_ShouldInitializeWithDefaults()
    {
        // Arrange & Act
        var capability = new AgentCapability();

        // Assert
        capability.Name.Should().Be(string.Empty);
        capability.Description.Should().Be(string.Empty);
        capability.Category.Should().Be(string.Empty);
        capability.Parameters.Should().NotBeNull().And.BeEmpty();
        capability.Version.Should().Be("1.0.0");
    }

    [Fact]
    public void AgentCommand_ShouldInitializeWithDefaults()
    {
        // Arrange & Act
        var command = new AgentCommand();

        // Assert
        command.Id.Should().NotBeNullOrEmpty();
        command.Type.Should().Be(string.Empty);
        command.Action.Should().Be(string.Empty);
        command.Parameters.Should().NotBeNull().And.BeEmpty();
        command.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        command.RequestId.Should().Be(string.Empty);
        command.TimeoutSeconds.Should().Be(30);
    }

    [Fact]
    public void AgentExecutionResult_ShouldInitializeWithDefaults()
    {
        // Arrange & Act
        var result = new AgentExecutionResult();

        // Assert
        result.ExecutionId.Should().NotBeNullOrEmpty();
        result.AgentId.Should().Be(string.Empty);
        result.CommandId.Should().Be(string.Empty);
        result.Success.Should().BeFalse();
        result.Message.Should().Be(string.Empty);
        result.Data.Should().NotBeNull().And.BeEmpty();
        result.OutputData.Should().NotBeNull().And.BeEmpty();
        result.StartedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        result.CompletedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        result.Duration.Should().Be(TimeSpan.Zero);
        result.ErrorCode.Should().BeNull();
        result.Exception.Should().BeNull();
    }

    [Fact]
    public void AgentHealthStatus_ShouldInitializeWithDefaults()
    {
        // Arrange & Act
        var healthStatus = new AgentHealthStatus();

        // Assert
        healthStatus.AgentId.Should().Be(string.Empty);
        healthStatus.IsHealthy.Should().BeFalse();
        healthStatus.Status.Should().Be(string.Empty);
        healthStatus.LastCheckTime.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        healthStatus.LastHeartbeat.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        healthStatus.ResponseTime.Should().Be(TimeSpan.Zero);
        healthStatus.MemoryUsage.Should().Be(0);
        healthStatus.CpuUsage.Should().Be(0);
        healthStatus.ErrorCount.Should().Be(0);
        healthStatus.LastError.Should().BeNull();
        healthStatus.Metrics.Should().NotBeNull().And.BeEmpty();
        healthStatus.Metadata.Should().NotBeNull().And.BeEmpty();
        healthStatus.Issues.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void AgentInfo_ShouldInitializeWithDefaults()
    {
        // Arrange & Act
        var agentInfo = new AgentInfo();

        // Assert
        agentInfo.Id.Should().Be(string.Empty);
        agentInfo.Name.Should().Be(string.Empty);
        agentInfo.Description.Should().Be(string.Empty);
        agentInfo.Version.Should().Be(string.Empty);
        agentInfo.Status.Should().Be(AgentStatus.Unknown);
        agentInfo.Capabilities.Should().NotBeNull().And.BeEmpty();
        agentInfo.RegisteredAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        agentInfo.LastSeenAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        agentInfo.LastHeartbeat.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        agentInfo.Metadata.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void McpMessage_ShouldInitializeWithDefaults()
    {
        // Arrange & Act
        var message = new McpMessage();

        // Assert
        message.Id.Should().NotBeNullOrEmpty();
        message.Type.Should().Be(string.Empty);
        message.Source.Should().Be(string.Empty);
        message.Target.Should().Be(string.Empty);
        message.Payload.Should().NotBeNull().And.BeEmpty();
        message.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        message.CorrelationId.Should().Be(string.Empty);
    }

    [Fact]
    public void McpResponse_ShouldInitializeWithDefaults()
    {
        // Arrange & Act
        var response = new McpResponse();

        // Assert
        response.MessageId.Should().Be(string.Empty);
        response.Success.Should().BeFalse();
        response.Message.Should().Be(string.Empty);
        response.Data.Should().NotBeNull().And.BeEmpty();
        response.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        response.ErrorCode.Should().BeNull();
    }

    [Fact]
    public void AgentStatus_ShouldHaveCorrectValues()
    {
        // Assert
        ((int)AgentStatus.Unknown).Should().Be(0);
        ((int)AgentStatus.Initializing).Should().Be(1);
        ((int)AgentStatus.Ready).Should().Be(2);
        ((int)AgentStatus.Busy).Should().Be(3);
        ((int)AgentStatus.Error).Should().Be(4);
        ((int)AgentStatus.Stopping).Should().Be(5);
        ((int)AgentStatus.Stopped).Should().Be(6);
        ((int)AgentStatus.Shutdown).Should().Be(7);
    }

    [Fact]
    public void WorkflowExecutionResult_ShouldInitializeWithDefaults()
    {
        // Arrange & Act
        var result = new WorkflowExecutionResult();

        // Assert
        result.WorkflowId.Should().Be(string.Empty);
        result.ExecutionId.Should().NotBeNullOrEmpty();
        result.Success.Should().BeFalse();
        result.Message.Should().Be(string.Empty);
    }

    [Fact]
    public void LlmMessage_ShouldInitializeWithDefaults()
    {
        // Arrange & Act
        var message = new LlmMessage();

        // Assert
        message.Role.Should().Be(string.Empty);
        message.Content.Should().Be(string.Empty);
        message.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        message.Metadata.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void LlmResponse_ShouldInitializeWithDefaults()
    {
        // Arrange & Act
        var response = new LlmResponse();

        // Assert
        response.Content.Should().Be(string.Empty);
        response.Success.Should().BeFalse();
        response.Model.Should().Be(string.Empty);
        response.TokensUsed.Should().Be(0);
        response.Duration.Should().Be(TimeSpan.Zero);
        response.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        response.Metadata.Should().NotBeNull().And.BeEmpty();
        response.ErrorMessage.Should().BeNull();
    }
}
