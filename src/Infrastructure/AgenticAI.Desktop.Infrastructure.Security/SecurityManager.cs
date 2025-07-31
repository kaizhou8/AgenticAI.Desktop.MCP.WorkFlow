using AgenticAI.Desktop.Domain.Models;
using AgenticAI.Desktop.Shared.Contracts;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AgenticAI.Desktop.Infrastructure.Security;

/// <summary>
/// Security Manager for AgenticAI Desktop system
/// Provides authentication, authorization, encryption, and audit capabilities
/// </summary>
public class SecurityManager : ISecurityManager
{
    private readonly ILogger<SecurityManager> _logger;
    private readonly IConfiguration _configuration;
    private readonly ConcurrentDictionary<string, SecuritySession> _activeSessions;
    private readonly ConcurrentDictionary<string, SecurityPolicy> _policies;
    private readonly List<SecurityAuditEvent> _auditLog;
    private readonly byte[] _encryptionKey;
    private readonly SemaphoreSlim _auditSemaphore;

    public SecurityManager(ILogger<SecurityManager> logger, IConfiguration configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _activeSessions = new ConcurrentDictionary<string, SecuritySession>();
        _policies = new ConcurrentDictionary<string, SecurityPolicy>();
        _auditLog = new List<SecurityAuditEvent>();
        _auditSemaphore = new SemaphoreSlim(1, 1);
        
        // Initialize encryption key
        _encryptionKey = InitializeEncryptionKey();
        
        // Initialize default security policies
        InitializeDefaultPolicies();
        
        _logger.LogInformation("Security Manager initialized successfully");
    }

    /// <summary>
    /// Authenticate user with credentials
    /// </summary>
    public async Task<SecurityAuthenticationResult> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            await LogSecurityEventAsync("AUTHENTICATION_FAILED", $"Invalid credentials provided for user: {username}");
            return new SecurityAuthenticationResult
            {
                Success = false,
                Message = "Invalid username or password",
                ErrorCode = "INVALID_CREDENTIALS"
            };
        }

        try
        {
            // In a real implementation, this would validate against a user database
            // For now, we'll use a simple validation mechanism
            var isValidUser = await ValidateUserCredentialsAsync(username, password, cancellationToken);
            
            if (!isValidUser)
            {
                await LogSecurityEventAsync("AUTHENTICATION_FAILED", $"Authentication failed for user: {username}");
                return new SecurityAuthenticationResult
                {
                    Success = false,
                    Message = "Invalid username or password",
                    ErrorCode = "AUTHENTICATION_FAILED"
                };
            }

            // Create security session
            var sessionId = Guid.NewGuid().ToString();
            var session = new SecuritySession
            {
                SessionId = sessionId,
                Username = username,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(8), // 8-hour session
                IsActive = true,
                Permissions = await GetUserPermissionsAsync(username, cancellationToken)
            };

            _activeSessions.TryAdd(sessionId, session);
            
            await LogSecurityEventAsync("AUTHENTICATION_SUCCESS", $"User authenticated successfully: {username}");

            return new SecurityAuthenticationResult
            {
                Success = true,
                SessionId = sessionId,
                Username = username,
                ExpiresAt = session.ExpiresAt,
                Permissions = session.Permissions,
                Message = "Authentication successful"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during authentication for user: {Username}", username);
            await LogSecurityEventAsync("AUTHENTICATION_ERROR", $"Authentication error for user {username}: {ex.Message}");
            
            return new SecurityAuthenticationResult
            {
                Success = false,
                Message = "Authentication service error",
                ErrorCode = "AUTHENTICATION_ERROR"
            };
        }
    }

    /// <summary>
    /// Validate session and check authorization
    /// </summary>
    public async Task<SecurityAuthorizationResult> AuthorizeAsync(string sessionId, string resource, string action, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return new SecurityAuthorizationResult
            {
                Success = false,
                Message = "Session ID is required",
                ErrorCode = "MISSING_SESSION"
            };
        }

        try
        {
            // Validate session
            if (!_activeSessions.TryGetValue(sessionId, out var session))
            {
                await LogSecurityEventAsync("AUTHORIZATION_FAILED", $"Invalid session ID: {sessionId}");
                return new SecurityAuthorizationResult
                {
                    Success = false,
                    Message = "Invalid or expired session",
                    ErrorCode = "INVALID_SESSION"
                };
            }

            // Check session expiration
            if (session.ExpiresAt <= DateTime.UtcNow || !session.IsActive)
            {
                _activeSessions.TryRemove(sessionId, out _);
                await LogSecurityEventAsync("AUTHORIZATION_FAILED", $"Expired session for user: {session.Username}");
                return new SecurityAuthorizationResult
                {
                    Success = false,
                    Message = "Session has expired",
                    ErrorCode = "SESSION_EXPIRED"
                };
            }

            // Check permissions
            var hasPermission = await CheckPermissionAsync(session.Permissions, resource, action, cancellationToken);
            
            if (!hasPermission)
            {
                await LogSecurityEventAsync("AUTHORIZATION_DENIED", $"Access denied for user {session.Username} to {resource}:{action}");
                return new SecurityAuthorizationResult
                {
                    Success = false,
                    Message = "Access denied",
                    ErrorCode = "ACCESS_DENIED"
                };
            }

            // Update session last accessed time
            session.LastAccessedAt = DateTime.UtcNow;
            
            await LogSecurityEventAsync("AUTHORIZATION_SUCCESS", $"Access granted for user {session.Username} to {resource}:{action}");

            return new SecurityAuthorizationResult
            {
                Success = true,
                Username = session.Username,
                SessionId = sessionId,
                Message = "Access granted"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during authorization for session: {SessionId}", sessionId);
            await LogSecurityEventAsync("AUTHORIZATION_ERROR", $"Authorization error for session {sessionId}: {ex.Message}");
            
            return new SecurityAuthorizationResult
            {
                Success = false,
                Message = "Authorization service error",
                ErrorCode = "AUTHORIZATION_ERROR"
            };
        }
    }

    /// <summary>
    /// Encrypt sensitive data
    /// </summary>
    public async Task<string> EncryptDataAsync(string plainText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        try
        {
            await Task.CompletedTask; // For async consistency

            using var aes = Aes.Create();
            aes.Key = _encryptionKey;
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();
            using var msEncrypt = new MemoryStream();
            using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
            using (var swEncrypt = new StreamWriter(csEncrypt))
            {
                swEncrypt.Write(plainText);
            }

            var encrypted = msEncrypt.ToArray();
            var result = new byte[aes.IV.Length + encrypted.Length];
            Array.Copy(aes.IV, 0, result, 0, aes.IV.Length);
            Array.Copy(encrypted, 0, result, aes.IV.Length, encrypted.Length);

            return Convert.ToBase64String(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error encrypting data");
            throw new SecurityException("Failed to encrypt data", ex);
        }
    }

    /// <summary>
    /// Decrypt sensitive data
    /// </summary>
    public async Task<string> DecryptDataAsync(string encryptedText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(encryptedText))
            return string.Empty;

        try
        {
            await Task.CompletedTask; // For async consistency

            var fullCipher = Convert.FromBase64String(encryptedText);

            using var aes = Aes.Create();
            aes.Key = _encryptionKey;

            var iv = new byte[aes.IV.Length];
            var cipher = new byte[fullCipher.Length - iv.Length];

            Array.Copy(fullCipher, iv, iv.Length);
            Array.Copy(fullCipher, iv.Length, cipher, 0, cipher.Length);

            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            using var msDecrypt = new MemoryStream(cipher);
            using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
            using var srDecrypt = new StreamReader(csDecrypt);

            return srDecrypt.ReadToEnd();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error decrypting data");
            throw new SecurityException("Failed to decrypt data", ex);
        }
    }

    /// <summary>
    /// Generate secure hash for passwords
    /// </summary>
    public async Task<string> HashPasswordAsync(string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password cannot be null or empty", nameof(password));

        try
        {
            await Task.CompletedTask; // For async consistency

            // Generate a 128-bit salt using a secure PRNG
            byte[] salt = new byte[128 / 8];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            // Hash the password with PBKDF2
            string hashed = Convert.ToBase64String(KeyDerivation.Pbkdf2(
                password: password,
                salt: salt,
                prf: KeyDerivationPrf.HMACSHA256,
                iterationCount: 10000,
                numBytesRequested: 256 / 8));

            // Combine salt and hash
            return $"{Convert.ToBase64String(salt)}.{hashed}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error hashing password");
            throw new SecurityException("Failed to hash password", ex);
        }
    }

    /// <summary>
    /// Verify password against hash
    /// </summary>
    public async Task<bool> VerifyPasswordAsync(string password, string hash, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hash))
            return false;

        try
        {
            await Task.CompletedTask; // For async consistency

            var parts = hash.Split('.');
            if (parts.Length != 2)
                return false;

            var salt = Convert.FromBase64String(parts[0]);
            var storedHash = parts[1];

            // Hash the provided password with the stored salt
            string computedHash = Convert.ToBase64String(KeyDerivation.Pbkdf2(
                password: password,
                salt: salt,
                prf: KeyDerivationPrf.HMACSHA256,
                iterationCount: 10000,
                numBytesRequested: 256 / 8));

            return computedHash == storedHash;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying password");
            return false;
        }
    }

    /// <summary>
    /// Logout and invalidate session
    /// </summary>
    public async Task LogoutAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return;

        try
        {
            await Task.CompletedTask; // For async consistency

            if (_activeSessions.TryRemove(sessionId, out var session))
            {
                session.IsActive = false;
                await LogSecurityEventAsync("LOGOUT", $"User logged out: {session.Username}");
                _logger.LogInformation("Session {SessionId} for user {Username} has been terminated", sessionId, session.Username);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout for session: {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// Get security audit log
    /// </summary>
    public async Task<IEnumerable<SecurityAuditEvent>> GetAuditLogAsync(DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default)
    {
        await _auditSemaphore.WaitAsync(cancellationToken);
        try
        {
            var query = _auditLog.AsEnumerable();

            if (fromDate.HasValue)
                query = query.Where(e => e.Timestamp >= fromDate.Value);

            if (toDate.HasValue)
                query = query.Where(e => e.Timestamp <= toDate.Value);

            return query.OrderByDescending(e => e.Timestamp).ToList();
        }
        finally
        {
            _auditSemaphore.Release();
        }
    }

    /// <summary>
    /// Initialize encryption key
    /// </summary>
    private byte[] InitializeEncryptionKey()
    {
        var keyString = _configuration["Security:EncryptionKey"];
        if (!string.IsNullOrEmpty(keyString))
        {
            return Convert.FromBase64String(keyString);
        }

        // Generate a new key if not configured
        using var aes = Aes.Create();
        aes.GenerateKey();
        var key = aes.Key;

        _logger.LogWarning("No encryption key configured. Generated a new key. This should be configured in production.");
        return key;
    }

    /// <summary>
    /// Initialize default security policies
    /// </summary>
    private void InitializeDefaultPolicies()
    {
        var adminPolicy = new SecurityPolicy
        {
            Id = "admin",
            Name = "Administrator Policy",
            Permissions = new List<string>
            {
                "agent:*", "workflow:*", "security:*", "system:*"
            }
        };

        var userPolicy = new SecurityPolicy
        {
            Id = "user",
            Name = "Standard User Policy",
            Permissions = new List<string>
            {
                "agent:read", "agent:execute", "workflow:read", "workflow:execute"
            }
        };

        _policies.TryAdd("admin", adminPolicy);
        _policies.TryAdd("user", userPolicy);
    }

    /// <summary>
    /// Validate user credentials (simplified implementation)
    /// </summary>
    private async Task<bool> ValidateUserCredentialsAsync(string username, string password, CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // For async consistency

        // In a real implementation, this would validate against a database
        // For demo purposes, we'll accept specific test credentials
        return (username == "admin" && password == "admin123") ||
               (username == "user" && password == "user123") ||
               (username == "demo" && password == "demo123");
    }

    /// <summary>
    /// Get user permissions
    /// </summary>
    private async Task<List<string>> GetUserPermissionsAsync(string username, CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // For async consistency

        // In a real implementation, this would query user roles and permissions from database
        return username switch
        {
            "admin" => _policies["admin"].Permissions,
            "user" => _policies["user"].Permissions,
            "demo" => _policies["user"].Permissions,
            _ => new List<string>()
        };
    }

    /// <summary>
    /// Check if user has permission for resource and action
    /// </summary>
    private async Task<bool> CheckPermissionAsync(List<string> userPermissions, string resource, string action, CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // For async consistency

        if (userPermissions == null || userPermissions.Count == 0)
            return false;

        var requiredPermission = $"{resource}:{action}";
        var wildcardPermission = $"{resource}:*";
        var fullWildcard = "*";

        return userPermissions.Contains(requiredPermission) ||
               userPermissions.Contains(wildcardPermission) ||
               userPermissions.Contains(fullWildcard);
    }

    /// <summary>
    /// Log security event for audit trail
    /// </summary>
    private async Task LogSecurityEventAsync(string eventType, string description)
    {
        await _auditSemaphore.WaitAsync();
        try
        {
            var auditEvent = new SecurityAuditEvent
            {
                Id = Guid.NewGuid().ToString(),
                EventType = eventType,
                Description = description,
                Timestamp = DateTime.UtcNow,
                Source = "SecurityManager"
            };

            _auditLog.Add(auditEvent);

            // Keep only last 10000 audit events to prevent memory issues
            if (_auditLog.Count > 10000)
            {
                _auditLog.RemoveRange(0, _auditLog.Count - 10000);
            }

            _logger.LogInformation("Security audit event: {EventType} - {Description}", eventType, description);
        }
        finally
        {
            _auditSemaphore.Release();
        }
    }
}
