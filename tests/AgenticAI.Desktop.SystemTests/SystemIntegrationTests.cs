using AgenticAI.Desktop.Agents.Core;
using AgenticAI.Desktop.Agents.FileSystem;
using AgenticAI.Desktop.Application.Director;
using AgenticAI.Desktop.Domain.Models;
using AgenticAI.Desktop.Infrastructure.LLM;
using AgenticAI.Desktop.Infrastructure.MCP;
using AgenticAI.Desktop.Infrastructure.Security;
using AgenticAI.Desktop.Shared.Contracts;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AgenticAI.Desktop.SystemTests;

/// <summary>
/// System integration tests for AgenticAI Desktop
/// Tests the integration between all core modules: Agents, Director, Security, LLM, and MCP
/// </summary>
public class SystemIntegrationTests
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SystemIntegrationTests> _logger;

    public SystemIntegrationTests()
    {
        // Setup dependency injection container for system tests
        var services = new ServiceCollection();
        
        // Add configuration
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:EncryptionKey"] = "test-encryption-key-32-characters",
                ["LLM:DefaultProvider"] = "Mock",
                ["LLM:Providers:Mock:ApiKey"] = "test-key"
            })
            .Build();
        
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(builder => builder.AddConsole());
        
        // Register core services
        services.AddSingleton<ISecurityManager, SecurityManager>();
        services.AddSingleton<ILlmAdapter, LlmAdapter>();
        services.AddSingleton<IMcpProtocolEngine, McpProtocolEngine>();
        services.AddSingleton<IDirector, Director>();
        
        // Register agents
        services.AddTransient<FileSystemAgent>();
        
        _serviceProvider = services.BuildServiceProvider();
        _logger = _serviceProvider.GetRequiredService<ILogger<SystemIntegrationTests>>();
    }

    [Fact]
    public void ServiceProvider_ShouldResolveAllCoreServices()
    {
        // Act & Assert - All core services should be resolvable
        var securityManager = _serviceProvider.GetService<ISecurityManager>();
        var llmAdapter = _serviceProvider.GetService<ILlmAdapter>();
        var mcpEngine = _serviceProvider.GetService<IMcpProtocolEngine>();
        var director = _serviceProvider.GetService<IDirector>();
        var fileSystemAgent = _serviceProvider.GetService<FileSystemAgent>();

        securityManager.Should().NotBeNull();
        llmAdapter.Should().NotBeNull();
        mcpEngine.Should().NotBeNull();
        director.Should().NotBeNull();
        fileSystemAgent.Should().NotBeNull();
    }

    [Fact]
    public async Task SecurityManager_ShouldAuthenticateAndAuthorize()
    {
        // Arrange
        var securityManager = _serviceProvider.GetRequiredService<ISecurityManager>();

        // Act - Authenticate user
        var authResult = await securityManager.AuthenticateAsync("admin", "admin123");

        // Assert - Authentication should succeed
        authResult.Should().NotBeNull();
        authResult.Success.Should().BeTrue();
        authResult.SessionId.Should().NotBeNullOrEmpty();
        authResult.Username.Should().Be("admin");

        // Act - Authorize user for agent operations
        var authzResult = await securityManager.AuthorizeAsync(authResult.SessionId!, "agent", "execute");

        // Assert - Authorization should succeed
        authzResult.Should().NotBeNull();
        authzResult.Success.Should().BeTrue();
        authzResult.Username.Should().Be("admin");
    }

    [Fact]
    public async Task FileSystemAgent_ShouldExecuteBasicOperations()
    {
        // Arrange
        var fileSystemAgent = _serviceProvider.GetRequiredService<FileSystemAgent>();
        var tempDir = Path.GetTempPath();
        var testFile = Path.Combine(tempDir, $"test_{Guid.NewGuid()}.txt");
        var testContent = "Hello, AgenticAI Desktop System Test!";

        try
        {
            // Act & Assert - Write file
            var writeCommand = new AgentCommand
            {
                Id = Guid.NewGuid().ToString(),
                Type = "write_file",
                Parameters = new Dictionary<string, object>
                {
                    ["file_path"] = testFile,
                    ["content"] = testContent
                }
            };

            var writeResult = await fileSystemAgent.ExecuteAsync(writeCommand);
            writeResult.Should().NotBeNull();
            writeResult.Success.Should().BeTrue();

            // Act & Assert - Read file
            var readCommand = new AgentCommand
            {
                Id = Guid.NewGuid().ToString(),
                Type = "read_file",
                Parameters = new Dictionary<string, object>
                {
                    ["file_path"] = testFile
                }
            };

            var readResult = await fileSystemAgent.ExecuteAsync(readCommand);
            readResult.Should().NotBeNull();
            readResult.Success.Should().BeTrue();
            readResult.OutputData.Should().ContainKey("content");
            readResult.OutputData["content"].ToString().Should().Be(testContent);
        }
        finally
        {
            // Cleanup
            if (File.Exists(testFile))
            {
                File.Delete(testFile);
            }
        }
    }

    [Fact]
    public async Task LlmAdapter_ShouldProcessPrompts()
    {
        // Arrange
        var llmAdapter = _serviceProvider.GetRequiredService<ILlmAdapter>();

        // Act
        var response = await llmAdapter.ProcessPromptAsync("Hello, this is a test prompt");

        // Assert
        response.Should().NotBeNull();
        response.Should().NotBeEmpty();
        response.Should().Contain("test"); // Mock provider should echo the input
    }

    [Fact]
    public async Task Director_ShouldManageAgentLifecycle()
    {
        // Arrange
        var director = _serviceProvider.GetRequiredService<IDirector>();
        var agentId = "test-filesystem-agent";

        // Act - Register agent
        var agentInfo = new AgentInfo
        {
            Id = agentId,
            Name = "Test FileSystem Agent",
            Description = "Test agent for system integration",
            Version = "1.0.0",
            Status = AgentStatus.Stopped,
            Capabilities = new List<AgentCapability>
            {
                new() { Name = "read_file", Description = "Read file content" },
                new() { Name = "write_file", Description = "Write file content" }
            }
        };

        await director.RegisterAgentAsync(agentInfo);

        // Assert - Agent should be registered
        var registeredAgents = await director.GetAgentsAsync();
        registeredAgents.Should().Contain(a => a.Id == agentId);

        // Act - Start agent
        await director.StartAgentAsync(agentId);

        // Assert - Agent should be running
        var agentStatus = await director.GetAgentStatusAsync(agentId);
        agentStatus.Should().Be(AgentStatus.Running);

        // Act - Stop agent
        await director.StopAgentAsync(agentId);

        // Assert - Agent should be stopped
        agentStatus = await director.GetAgentStatusAsync(agentId);
        agentStatus.Should().Be(AgentStatus.Stopped);
    }

    [Fact]
    public async Task SecurityManager_ShouldEncryptAndDecryptData()
    {
        // Arrange
        var securityManager = _serviceProvider.GetRequiredService<ISecurityManager>();
        var sensitiveData = "This is sensitive information that needs encryption";

        // Act - Encrypt data
        var encryptedData = await securityManager.EncryptDataAsync(sensitiveData);

        // Assert - Data should be encrypted
        encryptedData.Should().NotBeNullOrEmpty();
        encryptedData.Should().NotBe(sensitiveData);

        // Act - Decrypt data
        var decryptedData = await securityManager.DecryptDataAsync(encryptedData);

        // Assert - Data should be decrypted correctly
        decryptedData.Should().Be(sensitiveData);
    }

    [Fact]
    public async Task SecurityManager_ShouldMaintainAuditLog()
    {
        // Arrange
        var securityManager = _serviceProvider.GetRequiredService<ISecurityManager>();

        // Act - Perform some operations that should be audited
        await securityManager.AuthenticateAsync("admin", "admin123");
        await securityManager.AuthenticateAsync("invalid", "invalid");
        await securityManager.HashPasswordAsync("test-password");

        // Act - Get audit log
        var auditLog = await securityManager.GetAuditLogAsync();

        // Assert - Audit log should contain events
        auditLog.Should().NotBeNull();
        auditLog.Should().NotBeEmpty();
        
        var auditEvents = auditLog.ToList();
        auditEvents.Should().Contain(e => e.EventType == "AUTHENTICATION_SUCCESS");
        auditEvents.Should().Contain(e => e.EventType == "AUTHENTICATION_FAILED");
    }

    [Fact]
    public async Task IntegratedWorkflow_ShouldExecuteSecureAgentOperations()
    {
        // Arrange
        var securityManager = _serviceProvider.GetRequiredService<ISecurityManager>();
        var director = _serviceProvider.GetRequiredService<IDirector>();
        var fileSystemAgent = _serviceProvider.GetRequiredService<FileSystemAgent>();

        // Step 1: Authenticate user
        var authResult = await securityManager.AuthenticateAsync("admin", "admin123");
        authResult.Success.Should().BeTrue();

        // Step 2: Authorize for agent operations
        var authzResult = await securityManager.AuthorizeAsync(authResult.SessionId!, "agent", "execute");
        authzResult.Success.Should().BeTrue();

        // Step 3: Register and start agent via director
        var agentInfo = new AgentInfo
        {
            Id = "integrated-test-agent",
            Name = "Integrated Test Agent",
            Description = "Agent for integrated workflow test",
            Version = "1.0.0",
            Status = AgentStatus.Stopped
        };

        await director.RegisterAgentAsync(agentInfo);
        await director.StartAgentAsync("integrated-test-agent");

        // Step 4: Execute secure file operation
        var tempFile = Path.Combine(Path.GetTempPath(), $"secure_test_{Guid.NewGuid()}.txt");
        var secureContent = "This is secure content processed through the integrated workflow";

        try
        {
            // Encrypt content before storing
            var encryptedContent = await securityManager.EncryptDataAsync(secureContent);

            // Execute file write operation
            var writeCommand = new AgentCommand
            {
                Id = Guid.NewGuid().ToString(),
                Type = "write_file",
                Parameters = new Dictionary<string, object>
                {
                    ["file_path"] = tempFile,
                    ["content"] = encryptedContent
                }
            };

            var writeResult = await fileSystemAgent.ExecuteAsync(writeCommand);
            writeResult.Success.Should().BeTrue();

            // Read back and decrypt
            var readCommand = new AgentCommand
            {
                Id = Guid.NewGuid().ToString(),
                Type = "read_file",
                Parameters = new Dictionary<string, object>
                {
                    ["file_path"] = tempFile
                }
            };

            var readResult = await fileSystemAgent.ExecuteAsync(readCommand);
            readResult.Success.Should().BeTrue();

            var readEncryptedContent = readResult.OutputData["content"].ToString()!;
            var decryptedContent = await securityManager.DecryptDataAsync(readEncryptedContent);

            // Assert - Content should match original
            decryptedContent.Should().Be(secureContent);

            // Step 5: Verify audit trail
            var auditLog = await securityManager.GetAuditLogAsync();
            auditLog.Should().Contain(e => e.EventType == "AUTHENTICATION_SUCCESS");
            auditLog.Should().Contain(e => e.EventType == "AUTHORIZATION_SUCCESS");
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
            
            await director.StopAgentAsync("integrated-test-agent");
            await securityManager.LogoutAsync(authResult.SessionId!);
        }
    }

    [Fact]
    public void AllCoreModels_ShouldHaveValidDefaults()
    {
        // Test Domain Models have proper default values
        var agentInfo = new AgentInfo();
        agentInfo.Id.Should().NotBeNull();
        agentInfo.Name.Should().NotBeNull();
        agentInfo.Capabilities.Should().NotBeNull();

        var agentCommand = new AgentCommand();
        agentCommand.Id.Should().NotBeNull();
        agentCommand.Parameters.Should().NotBeNull();

        var securitySession = new SecuritySession();
        securitySession.SessionId.Should().NotBeNull();
        securitySession.Permissions.Should().NotBeNull();
        securitySession.Metadata.Should().NotBeNull();

        var auditEvent = new SecurityAuditEvent();
        auditEvent.Id.Should().NotBeNull();
        auditEvent.AdditionalData.Should().NotBeNull();
        auditEvent.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }
}
