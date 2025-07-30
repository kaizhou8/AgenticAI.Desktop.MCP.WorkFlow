using AgenticAI.Desktop.Agents.Core;
using AgenticAI.Desktop.Domain.Models;
using AgenticAI.Desktop.Shared.Contracts;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AgenticAI.Desktop.Agents.Core.Tests;

/// <summary>
/// Unit tests for BaseAgent to ensure interface consistency and proper functionality
/// </summary>
public class BaseAgentTests
{
    private readonly Mock<ILogger> _mockLogger;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly TestAgent _testAgent;

    public BaseAgentTests()
    {
        _mockLogger = new Mock<ILogger>();
        _mockConfiguration = new Mock<IConfiguration>();
        _testAgent = new TestAgent(_mockLogger.Object, _mockConfiguration.Object);
    }

    [Fact]
    public void BaseAgent_ShouldImplementIAgentInterface()
    {
        // Assert
        _testAgent.Should().BeAssignableTo<IAgent>();
    }

    [Fact]
    public void BaseAgent_ShouldInitializeWithCorrectDefaults()
    {
        // Assert
        _testAgent.Id.Should().NotBeNullOrEmpty();
        _testAgent.Name.Should().Be("TestAgent");
        _testAgent.Description.Should().Be("Test Agent for Unit Testing");
        _testAgent.Version.Should().Be("1.0.0");
        _testAgent.Status.Should().Be(AgentStatus.Initializing);
        _testAgent.Capabilities.Should().NotBeNull();
        _testAgent.LastHeartbeat.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        _testAgent.Metadata.Should().NotBeNull();
    }

    [Fact]
    public async Task GetHealthStatusAsync_ShouldReturnValidHealthStatus()
    {
        // Act
        var healthStatus = await _testAgent.GetHealthStatusAsync();

        // Assert
        healthStatus.Should().NotBeNull();
        healthStatus.AgentId.Should().Be(_testAgent.Id);
        healthStatus.Status.Should().Be(_testAgent.Status.ToString());
        healthStatus.LastHeartbeat.Should().Be(_testAgent.LastHeartbeat);
        healthStatus.IsHealthy.Should().BeTrue(); // Since status is Initializing, not Error or Stopped
        healthStatus.Metadata.Should().ContainKey("capabilities_count");
        healthStatus.Metadata.Should().ContainKey("version");
    }

    [Fact]
    public async Task GetAgentInfoAsync_ShouldReturnValidAgentInfo()
    {
        // Act
        var agentInfo = await _testAgent.GetAgentInfoAsync();

        // Assert
        agentInfo.Should().NotBeNull();
        agentInfo.Id.Should().Be(_testAgent.Id);
        agentInfo.Name.Should().Be(_testAgent.Name);
        agentInfo.Description.Should().Be(_testAgent.Description);
        agentInfo.Version.Should().Be(_testAgent.Version);
        agentInfo.Status.Should().Be(_testAgent.Status);
        agentInfo.Capabilities.Should().BeEquivalentTo(_testAgent.Capabilities);
        agentInfo.LastHeartbeat.Should().Be(_testAgent.LastHeartbeat);
        agentInfo.Metadata.Should().BeEquivalentTo(_testAgent.Metadata);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnExecutionResult()
    {
        // Arrange
        var command = new AgentCommand
        {
            Type = "test",
            Action = "test_action",
            Parameters = new Dictionary<string, object> { { "param1", "value1" } }
        };

        // Act
        var result = await _testAgent.ExecuteAsync(command);

        // Assert
        result.Should().NotBeNull();
        result.ExecutionId.Should().NotBeNullOrEmpty();
        result.AgentId.Should().Be(_testAgent.Id);
        result.CommandId.Should().Be(command.Id);
        result.Success.Should().BeTrue();
        result.Message.Should().Contain("test_action");
    }

    [Fact]
    public void Capabilities_ShouldBeReadOnly()
    {
        // Assert
        _testAgent.Capabilities.Should().BeAssignableTo<IReadOnlyList<AgentCapability>>();
    }

    [Fact]
    public void AgentProperties_ShouldMatchIAgentInterface()
    {
        // Arrange
        var agent = _testAgent as IAgent;

        // Assert
        agent.Id.Should().Be(_testAgent.Id);
        agent.Name.Should().Be(_testAgent.Name);
        agent.Description.Should().Be(_testAgent.Description);
        agent.Version.Should().Be(_testAgent.Version);
        agent.Status.Should().Be(_testAgent.Status);
        agent.Capabilities.Should().BeEquivalentTo(_testAgent.Capabilities);
    }

    /// <summary>
    /// Test implementation of BaseAgent for unit testing
    /// </summary>
    private class TestAgent : BaseAgent
    {
        public TestAgent(ILogger logger, IConfiguration configuration) 
            : base(logger, configuration, "TestAgent", "Test Agent for Unit Testing")
        {
        }

        protected override void InitializeCapabilities()
        {
            _capabilitiesList.Add(new AgentCapability
            {
                Name = "test_capability",
                Description = "Test capability for unit testing",
                Category = "Testing",
                Parameters = new Dictionary<string, object>
                {
                    { "param1", "string - Test parameter" }
                }
            });
        }

        protected override async Task<AgentExecutionResult> ExecuteCommandInternalAsync(AgentCommand command, CancellationToken cancellationToken)
        {
            await Task.Delay(10, cancellationToken); // Simulate some work

            return new AgentExecutionResult
            {
                CommandId = command.Id,
                Success = true,
                Message = $"Successfully executed {command.Action}",
                Data = new Dictionary<string, object>
                {
                    { "result", "test_result" },
                    { "action", command.Action }
                }
            };
        }
    }
}
