using AgenticAI.Desktop.Domain.Models;
using AgenticAI.Desktop.Infrastructure.Security;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AgenticAI.Desktop.Infrastructure.Security.Tests;

/// <summary>
/// Unit tests for SecurityManager
/// Tests authentication, authorization, encryption, and audit functionality
/// </summary>
public class SecurityManagerTests
{
    private readonly Mock<ILogger<SecurityManager>> _mockLogger;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly SecurityManager _securityManager;

    public SecurityManagerTests()
    {
        _mockLogger = new Mock<ILogger<SecurityManager>>();
        _mockConfiguration = new Mock<IConfiguration>();
        
        // Setup configuration mock
        _mockConfiguration.Setup(c => c["Security:EncryptionKey"]).Returns((string?)null);
        
        _securityManager = new SecurityManager(_mockLogger.Object, _mockConfiguration.Object);
    }

    [Fact]
    public void SecurityManager_ShouldInitializeWithCorrectDefaults()
    {
        // Act & Assert
        _securityManager.Should().NotBeNull();
        
        // Verify logger was called for initialization
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Security Manager initialized successfully")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task AuthenticateAsync_WithValidCredentials_ShouldReturnSuccess()
    {
        // Arrange
        var username = "admin";
        var password = "admin123";

        // Act
        var result = await _securityManager.AuthenticateAsync(username, password);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.SessionId.Should().NotBeNullOrEmpty();
        result.Username.Should().Be(username);
        result.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
        result.Permissions.Should().NotBeEmpty();
        result.Message.Should().Be("Authentication successful");
    }

    [Fact]
    public async Task AuthenticateAsync_WithInvalidCredentials_ShouldReturnFailure()
    {
        // Arrange
        var username = "invalid";
        var password = "invalid";

        // Act
        var result = await _securityManager.AuthenticateAsync(username, password);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.SessionId.Should().BeNull();
        result.ErrorCode.Should().Be("AUTHENTICATION_FAILED");
        result.Message.Should().Be("Invalid username or password");
    }

    [Theory]
    [InlineData(null, "password")]
    [InlineData("", "password")]
    [InlineData("username", null)]
    [InlineData("username", "")]
    [InlineData("   ", "password")]
    [InlineData("username", "   ")]
    public async Task AuthenticateAsync_WithInvalidInput_ShouldReturnFailure(string? username, string? password)
    {
        // Act
        var result = await _securityManager.AuthenticateAsync(username!, password!);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task AuthorizeAsync_WithValidSession_ShouldReturnSuccess()
    {
        // Arrange
        var authResult = await _securityManager.AuthenticateAsync("admin", "admin123");
        var sessionId = authResult.SessionId!;

        // Act
        var result = await _securityManager.AuthorizeAsync(sessionId, "agent", "read");

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Username.Should().Be("admin");
        result.SessionId.Should().Be(sessionId);
        result.Message.Should().Be("Access granted");
    }

    [Fact]
    public async Task AuthorizeAsync_WithInvalidSession_ShouldReturnFailure()
    {
        // Arrange
        var invalidSessionId = Guid.NewGuid().ToString();

        // Act
        var result = await _securityManager.AuthorizeAsync(invalidSessionId, "agent", "read");

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_SESSION");
        result.Message.Should().Be("Invalid or expired session");
    }

    [Fact]
    public async Task AuthorizeAsync_WithInsufficientPermissions_ShouldReturnFailure()
    {
        // Arrange
        var authResult = await _securityManager.AuthenticateAsync("user", "user123");
        var sessionId = authResult.SessionId!;

        // Act
        var result = await _securityManager.AuthorizeAsync(sessionId, "security", "admin");

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("ACCESS_DENIED");
        result.Message.Should().Be("Access denied");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task AuthorizeAsync_WithInvalidSessionId_ShouldReturnFailure(string? sessionId)
    {
        // Act
        var result = await _securityManager.AuthorizeAsync(sessionId!, "agent", "read");

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("MISSING_SESSION");
    }

    [Fact]
    public async Task EncryptDataAsync_WithValidData_ShouldReturnEncryptedString()
    {
        // Arrange
        var plainText = "This is sensitive data";

        // Act
        var encryptedData = await _securityManager.EncryptDataAsync(plainText);

        // Assert
        encryptedData.Should().NotBeNullOrEmpty();
        encryptedData.Should().NotBe(plainText);
        
        // Should be base64 encoded
        var isBase64 = IsBase64String(encryptedData);
        isBase64.Should().BeTrue();
    }

    [Fact]
    public async Task DecryptDataAsync_WithValidEncryptedData_ShouldReturnOriginalText()
    {
        // Arrange
        var originalText = "This is sensitive data";
        var encryptedData = await _securityManager.EncryptDataAsync(originalText);

        // Act
        var decryptedData = await _securityManager.DecryptDataAsync(encryptedData);

        // Assert
        decryptedData.Should().Be(originalText);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task EncryptDataAsync_WithEmptyData_ShouldReturnEmpty(string? plainText)
    {
        // Act
        var result = await _securityManager.EncryptDataAsync(plainText!);

        // Assert
        result.Should().Be(string.Empty);
    }

    [Fact]
    public async Task HashPasswordAsync_WithValidPassword_ShouldReturnHashedPassword()
    {
        // Arrange
        var password = "MySecurePassword123!";

        // Act
        var hashedPassword = await _securityManager.HashPasswordAsync(password);

        // Assert
        hashedPassword.Should().NotBeNullOrEmpty();
        hashedPassword.Should().NotBe(password);
        hashedPassword.Should().Contain(".");
        
        // Should have salt and hash parts
        var parts = hashedPassword.Split('.');
        parts.Should().HaveCount(2);
    }

    [Fact]
    public async Task VerifyPasswordAsync_WithCorrectPassword_ShouldReturnTrue()
    {
        // Arrange
        var password = "MySecurePassword123!";
        var hashedPassword = await _securityManager.HashPasswordAsync(password);

        // Act
        var isValid = await _securityManager.VerifyPasswordAsync(password, hashedPassword);

        // Assert
        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyPasswordAsync_WithIncorrectPassword_ShouldReturnFalse()
    {
        // Arrange
        var password = "MySecurePassword123!";
        var wrongPassword = "WrongPassword";
        var hashedPassword = await _securityManager.HashPasswordAsync(password);

        // Act
        var isValid = await _securityManager.VerifyPasswordAsync(wrongPassword, hashedPassword);

        // Assert
        isValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("", "hash")]
    [InlineData(null, "hash")]
    [InlineData("password", "")]
    [InlineData("password", null)]
    public async Task VerifyPasswordAsync_WithInvalidInput_ShouldReturnFalse(string? password, string? hash)
    {
        // Act
        var result = await _securityManager.VerifyPasswordAsync(password!, hash!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task LogoutAsync_WithValidSession_ShouldInvalidateSession()
    {
        // Arrange
        var authResult = await _securityManager.AuthenticateAsync("admin", "admin123");
        var sessionId = authResult.SessionId!;

        // Verify session is valid first
        var authResult1 = await _securityManager.AuthorizeAsync(sessionId, "agent", "read");
        authResult1.Success.Should().BeTrue();

        // Act
        await _securityManager.LogoutAsync(sessionId);

        // Assert - session should now be invalid
        var authResult2 = await _securityManager.AuthorizeAsync(sessionId, "agent", "read");
        authResult2.Success.Should().BeFalse();
        authResult2.ErrorCode.Should().Be("INVALID_SESSION");
    }

    [Fact]
    public async Task GetAuditLogAsync_ShouldReturnAuditEvents()
    {
        // Arrange - perform some operations to generate audit events
        await _securityManager.AuthenticateAsync("admin", "admin123");
        await _securityManager.AuthenticateAsync("invalid", "invalid");

        // Act
        var auditLog = await _securityManager.GetAuditLogAsync();

        // Assert
        auditLog.Should().NotBeNull();
        auditLog.Should().NotBeEmpty();
        
        var auditEvents = auditLog.ToList();
        auditEvents.Should().Contain(e => e.EventType == "AUTHENTICATION_SUCCESS");
        auditEvents.Should().Contain(e => e.EventType == "AUTHENTICATION_FAILED");
        
        // Events should be ordered by timestamp (newest first)
        var timestamps = auditEvents.Select(e => e.Timestamp).ToList();
        timestamps.Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task GetAuditLogAsync_WithDateFilter_ShouldReturnFilteredEvents()
    {
        // Arrange
        var fromDate = DateTime.UtcNow.AddMinutes(-1);
        await _securityManager.AuthenticateAsync("admin", "admin123");

        // Act
        var auditLog = await _securityManager.GetAuditLogAsync(fromDate);

        // Assert
        auditLog.Should().NotBeNull();
        var auditEvents = auditLog.ToList();
        auditEvents.Should().OnlyContain(e => e.Timestamp >= fromDate);
    }

    [Fact]
    public async Task HashPasswordAsync_WithNullPassword_ShouldThrowException()
    {
        // Act & Assert
        await FluentActions.Invoking(() => _securityManager.HashPasswordAsync(null!))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("Password cannot be null or empty*");
    }

    [Fact]
    public async Task DecryptDataAsync_WithInvalidData_ShouldThrowSecurityException()
    {
        // Arrange
        var invalidEncryptedData = "invalid-encrypted-data";

        // Act & Assert
        await FluentActions.Invoking(() => _securityManager.DecryptDataAsync(invalidEncryptedData))
            .Should().ThrowAsync<SecurityException>()
            .WithMessage("Failed to decrypt data");
    }

    private static bool IsBase64String(string value)
    {
        try
        {
            Convert.FromBase64String(value);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
