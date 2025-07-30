using AgenticAI.Desktop.Agents.FileSystem;
using AgenticAI.Desktop.Domain.Models;
using AgenticAI.Desktop.Shared.Contracts;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;

namespace AgenticAI.Desktop.Agents.FileSystem.Tests;

/// <summary>
/// Unit tests for FileSystemAgent to ensure proper file system operations and BaseAgent compliance
/// 文件系统代理单元测试，确保正确的文件系统操作和BaseAgent合规性
/// </summary>
public class FileSystemAgentTests : IDisposable
{
    private readonly Mock<ILogger<FileSystemAgent>> _mockLogger;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly FileSystemAgent _fileSystemAgent;
    private readonly string _testDirectory;

    public FileSystemAgentTests()
    {
        _mockLogger = new Mock<ILogger<FileSystemAgent>>();
        _mockConfiguration = new Mock<IConfiguration>();
        
        // Create a temporary test directory / 创建临时测试目录
        _testDirectory = Path.Combine(Path.GetTempPath(), $"FileSystemAgentTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        
        // Setup configuration mock to provide default values / 设置配置模拟以提供默认值
        var mockConfigSection = new Mock<IConfigurationSection>();
        mockConfigSection.Setup(x => x.Value).Returns(_testDirectory);
        _mockConfiguration.Setup(x => x.GetSection("FileSystemAgent:WorkingDirectory")).Returns(mockConfigSection.Object);
        
        var mockExtSection = new Mock<IConfigurationSection>();
        mockExtSection.Setup(x => x.Value).Returns(".txt,.md,.json,.xml,.csv,.log");
        _mockConfiguration.Setup(x => x.GetSection("FileSystemAgent:AllowedExtensions")).Returns(mockExtSection.Object);
        
        var mockSizeSection = new Mock<IConfigurationSection>();
        mockSizeSection.Setup(x => x.Value).Returns((10L * 1024L * 1024L).ToString());
        _mockConfiguration.Setup(x => x.GetSection("FileSystemAgent:MaxFileSize")).Returns(mockSizeSection.Object);
        
        _fileSystemAgent = new FileSystemAgent(_mockLogger.Object, _mockConfiguration.Object);
    }

    public void Dispose()
    {
        // Clean up test directory / 清理测试目录
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
        _fileSystemAgent?.Dispose();
    }

    [Fact]
    public void FileSystemAgent_ShouldImplementIAgentInterface()
    {
        // Assert
        _fileSystemAgent.Should().BeAssignableTo<IAgent>();
    }

    [Fact]
    public void FileSystemAgent_ShouldInitializeWithCorrectDefaults()
    {
        // Assert
        _fileSystemAgent.Id.Should().NotBeNullOrEmpty();
        _fileSystemAgent.Name.Should().Be("FileSystem Agent");
        _fileSystemAgent.Description.Should().Contain("file and directory operations");
        _fileSystemAgent.Version.Should().Be("1.0.0");
        _fileSystemAgent.Status.Should().Be(AgentStatus.Initializing);
        _fileSystemAgent.Capabilities.Should().NotBeNull();
        _fileSystemAgent.Capabilities.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public void FileSystemAgent_ShouldHaveExpectedCapabilities()
    {
        // Assert
        var capabilities = _fileSystemAgent.Capabilities;
        capabilities.Should().Contain(c => c.Name == "read_file");
        capabilities.Should().Contain(c => c.Name == "write_file");
        capabilities.Should().Contain(c => c.Name == "list_directory");
        capabilities.Should().Contain(c => c.Name == "create_directory");
        capabilities.Should().Contain(c => c.Name == "delete_file");
        capabilities.Should().Contain(c => c.Name == "copy_file");
        capabilities.Should().Contain(c => c.Name == "move_file");
        capabilities.Should().Contain(c => c.Name == "get_file_info");
    }

    [Fact]
    public async Task ExecuteAsync_ReadFile_ShouldReturnFileContent()
    {
        // Arrange
        var testFile = Path.Combine(_testDirectory, "test.txt");
        var testContent = "Hello, World! 你好世界！";
        await File.WriteAllTextAsync(testFile, testContent);

        var command = new AgentCommand
        {
            Type = "read_file",
            Action = "read_file",
            Parameters = new Dictionary<string, object>
            {
                { "file_path", testFile }
            }
        };

        // Act
        var result = await _fileSystemAgent.ExecuteAsync(command);

        // Assert
        result.Should().NotBeNull();
        if (!result.Success)
        {
            // Output error message for debugging
            throw new Exception($"FileSystem operation failed: {result.Message}");
        }
        result.Success.Should().BeTrue();
        result.OutputData.Should().ContainKey("content");
        result.OutputData["content"].Should().Be(testContent);
    }

    [Fact]
    public async Task ExecuteAsync_WriteFile_ShouldCreateFileWithContent()
    {
        // Arrange
        var testFile = Path.Combine(_testDirectory, "write_test.txt");
        var testContent = "Test content 测试内容";

        var command = new AgentCommand
        {
            Type = "write_file",
            Action = "write_file",
            Parameters = new Dictionary<string, object>
            {
                { "file_path", testFile },
                { "content", testContent }
            }
        };

        // Act
        var result = await _fileSystemAgent.ExecuteAsync(command);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        File.Exists(testFile).Should().BeTrue();
        var actualContent = await File.ReadAllTextAsync(testFile);
        actualContent.Should().Be(testContent);
    }

    [Fact]
    public async Task ExecuteAsync_ListDirectory_ShouldReturnDirectoryContents()
    {
        // Arrange
        var testSubDir = Path.Combine(_testDirectory, "subdir");
        Directory.CreateDirectory(testSubDir);
        var testFile1 = Path.Combine(_testDirectory, "file1.txt");
        var testFile2 = Path.Combine(_testDirectory, "file2.txt");
        await File.WriteAllTextAsync(testFile1, "content1");
        await File.WriteAllTextAsync(testFile2, "content2");

        var command = new AgentCommand
        {
            Type = "list_directory",
            Action = "list_directory",
            Parameters = new Dictionary<string, object>
            {
                { "directory_path", _testDirectory }
            }
        };

        // Act
        var result = await _fileSystemAgent.ExecuteAsync(command);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.OutputData.Should().ContainKey("items");
        
        var items = result.OutputData["items"];
        items.Should().NotBeNull();
        // The items should be a list with 2 files + 1 directory
        result.OutputData["total_count"].Should().Be(3);
    }

    [Fact]
    public async Task ExecuteAsync_CreateDirectory_ShouldCreateDirectory()
    {
        // Arrange
        var newDir = Path.Combine(_testDirectory, "new_directory");

        var command = new AgentCommand
        {
            Type = "create_directory",
            Action = "create_directory",
            Parameters = new Dictionary<string, object>
            {
                { "directory_path", newDir }
            }
        };

        // Act
        var result = await _fileSystemAgent.ExecuteAsync(command);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        Directory.Exists(newDir).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_GetFileInfo_ShouldReturnFileInformation()
    {
        // Arrange
        var testFile = Path.Combine(_testDirectory, "info_test.txt");
        var testContent = "Test content for file info";
        await File.WriteAllTextAsync(testFile, testContent);

        var command = new AgentCommand
        {
            Type = "get_file_info",
            Action = "get_file_info",
            Parameters = new Dictionary<string, object>
            {
                { "file_path", testFile }
            }
        };

        // Act
        var result = await _fileSystemAgent.ExecuteAsync(command);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.OutputData.Should().ContainKey("path");
        result.OutputData.Should().ContainKey("size");
        result.OutputData.Should().ContainKey("modified_at");
    }

    [Fact]
    public async Task ExecuteAsync_DeleteFile_ShouldRemoveFile()
    {
        // Arrange
        var testFile = Path.Combine(_testDirectory, "to_delete.txt");
        await File.WriteAllTextAsync(testFile, "content to delete");

        var command = new AgentCommand
        {
            Type = "delete_file",
            Action = "delete_file",
            Parameters = new Dictionary<string, object>
            {
                { "file_path", testFile }
            }
        };

        // Act
        var result = await _fileSystemAgent.ExecuteAsync(command);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        File.Exists(testFile).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_InvalidAction_ShouldReturnError()
    {
        // Arrange
        var command = new AgentCommand
        {
            Type = "invalid_action",
            Action = "invalid_action",
            Parameters = new Dictionary<string, object>()
        };

        // Act
        var result = await _fileSystemAgent.ExecuteAsync(command);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Unknown command type");
    }

    [Fact]
    public async Task ExecuteAsync_MissingParameters_ShouldReturnError()
    {
        // Arrange
        var command = new AgentCommand
        {
            Type = "read_file",
            Action = "read_file",
            Parameters = new Dictionary<string, object>() // Missing 'path' parameter
        };

        // Act
        var result = await _fileSystemAgent.ExecuteAsync(command);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("path");
    }

    [Fact]
    public async Task GetHealthStatusAsync_ShouldReturnValidHealthStatus()
    {
        // Act
        var healthStatus = await _fileSystemAgent.GetHealthStatusAsync();

        // Assert
        healthStatus.Should().NotBeNull();
        healthStatus.AgentId.Should().Be(_fileSystemAgent.Id);
        healthStatus.Status.Should().Be(_fileSystemAgent.Status.ToString());
        healthStatus.IsHealthy.Should().BeTrue();
        healthStatus.Metadata.Should().ContainKey("capabilities_count");
        healthStatus.Metadata.Should().ContainKey("version");
    }

    [Fact]
    public async Task GetAgentInfoAsync_ShouldReturnValidAgentInfo()
    {
        // Act
        var agentInfo = await _fileSystemAgent.GetAgentInfoAsync();

        // Assert
        agentInfo.Should().NotBeNull();
        agentInfo.Id.Should().Be(_fileSystemAgent.Id);
        agentInfo.Name.Should().Be(_fileSystemAgent.Name);
        agentInfo.Description.Should().Be(_fileSystemAgent.Description);
        agentInfo.Version.Should().Be(_fileSystemAgent.Version);
        agentInfo.Status.Should().Be(_fileSystemAgent.Status);
        agentInfo.Capabilities.Should().BeEquivalentTo(_fileSystemAgent.Capabilities);
    }
}
