using AgenticAI.Desktop.Agents.Core;
using AgenticAI.Desktop.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AgenticAI.Desktop.Agents.FileSystem;

/// <summary>
/// FileSystem Agent - handles file and directory operations
/// Provides capabilities for file management, directory operations, and file content manipulation
/// </summary>
public class FileSystemAgent : BaseAgent
{
    private readonly string _workingDirectory;
    private readonly HashSet<string> _allowedExtensions;
    private readonly long _maxFileSize;

    public FileSystemAgent(ILogger<FileSystemAgent> logger, IConfiguration configuration)
        : base(logger, configuration, "FileSystem Agent", "Handles file and directory operations with security restrictions")
    {
        _workingDirectory = configuration.GetValue<string>("FileSystemAgent:WorkingDirectory") 
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AgenticAI");
        
        var allowedExtensions = configuration.GetValue<string>("FileSystemAgent:AllowedExtensions") 
            ?? ".txt,.md,.json,.xml,.csv,.log";
        _allowedExtensions = allowedExtensions.Split(',').Select(ext => ext.Trim().ToLowerInvariant()).ToHashSet();
        
        _maxFileSize = configuration.GetValue<long>("FileSystemAgent:MaxFileSize") ?? 10 * 1024 * 1024; // 10MB default
        
        // Ensure working directory exists
        Directory.CreateDirectory(_workingDirectory);
        
        _logger.LogInformation("FileSystem Agent initialized with working directory: {WorkingDirectory}", _workingDirectory);
    }

    /// <summary>
    /// Initialize agent capabilities
    /// </summary>
    protected override void InitializeCapabilities()
    {
        Capabilities.AddRange(new[]
        {
            new AgentCapability
            {
                Name = "read_file",
                Description = "Read content from a file",
                Parameters = new Dictionary<string, object>
                {
                    { "file_path", "string - Path to the file to read" },
                    { "encoding", "string - File encoding (optional, default: UTF-8)" }
                }
            },
            new AgentCapability
            {
                Name = "write_file",
                Description = "Write content to a file",
                Parameters = new Dictionary<string, object>
                {
                    { "file_path", "string - Path to the file to write" },
                    { "content", "string - Content to write to the file" },
                    { "encoding", "string - File encoding (optional, default: UTF-8)" },
                    { "append", "boolean - Whether to append to existing file (optional, default: false)" }
                }
            },
            new AgentCapability
            {
                Name = "list_directory",
                Description = "List files and directories in a specified path",
                Parameters = new Dictionary<string, object>
                {
                    { "directory_path", "string - Path to the directory to list" },
                    { "include_subdirectories", "boolean - Whether to include subdirectories (optional, default: false)" },
                    { "file_pattern", "string - File pattern filter (optional, e.g., *.txt)" }
                }
            },
            new AgentCapability
            {
                Name = "create_directory",
                Description = "Create a new directory",
                Parameters = new Dictionary<string, object>
                {
                    { "directory_path", "string - Path to the directory to create" }
                }
            },
            new AgentCapability
            {
                Name = "delete_file",
                Description = "Delete a file",
                Parameters = new Dictionary<string, object>
                {
                    { "file_path", "string - Path to the file to delete" }
                }
            },
            new AgentCapability
            {
                Name = "copy_file",
                Description = "Copy a file from source to destination",
                Parameters = new Dictionary<string, object>
                {
                    { "source_path", "string - Source file path" },
                    { "destination_path", "string - Destination file path" },
                    { "overwrite", "boolean - Whether to overwrite existing file (optional, default: false)" }
                }
            },
            new AgentCapability
            {
                Name = "move_file",
                Description = "Move a file from source to destination",
                Parameters = new Dictionary<string, object>
                {
                    { "source_path", "string - Source file path" },
                    { "destination_path", "string - Destination file path" },
                    { "overwrite", "boolean - Whether to overwrite existing file (optional, default: false)" }
                }
            },
            new AgentCapability
            {
                Name = "get_file_info",
                Description = "Get information about a file or directory",
                Parameters = new Dictionary<string, object>
                {
                    { "path", "string - Path to the file or directory" }
                }
            }
        });

        // Add metadata
        Metadata["working_directory"] = _workingDirectory;
        Metadata["allowed_extensions"] = string.Join(", ", _allowedExtensions);
        Metadata["max_file_size"] = _maxFileSize;
    }

    /// <summary>
    /// Execute command implementation
    /// </summary>
    protected override async Task<AgentExecutionResult> ExecuteCommandInternalAsync(AgentCommand command, CancellationToken cancellationToken)
    {
        try
        {
            return command.Type.ToLowerInvariant() switch
            {
                "read_file" => await ReadFileAsync(command, cancellationToken),
                "write_file" => await WriteFileAsync(command, cancellationToken),
                "list_directory" => await ListDirectoryAsync(command, cancellationToken),
                "create_directory" => await CreateDirectoryAsync(command, cancellationToken),
                "delete_file" => await DeleteFileAsync(command, cancellationToken),
                "copy_file" => await CopyFileAsync(command, cancellationToken),
                "move_file" => await MoveFileAsync(command, cancellationToken),
                "get_file_info" => await GetFileInfoAsync(command, cancellationToken),
                _ => new AgentExecutionResult
                {
                    Success = false,
                    Message = $"Unknown command type: {command.Type}",
                    ErrorCode = "UNKNOWN_COMMAND"
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing FileSystem command {CommandType}", command.Type);
            return new AgentExecutionResult
            {
                Success = false,
                Message = $"Command execution failed: {ex.Message}",
                ErrorCode = "EXECUTION_ERROR"
            };
        }
    }

    /// <summary>
    /// Read file content
    /// </summary>
    private async Task<AgentExecutionResult> ReadFileAsync(AgentCommand command, CancellationToken cancellationToken)
    {
        var filePath = GetParameterValue<string>(command, "file_path");
        var encoding = GetParameterValue<string>(command, "encoding") ?? "UTF-8";

        if (string.IsNullOrEmpty(filePath))
        {
            return CreateErrorResult("File path is required", "MISSING_PARAMETER");
        }

        var fullPath = GetSecurePath(filePath);
        if (fullPath == null)
        {
            return CreateErrorResult("Invalid or unauthorized file path", "SECURITY_VIOLATION");
        }

        if (!File.Exists(fullPath))
        {
            return CreateErrorResult($"File not found: {filePath}", "FILE_NOT_FOUND");
        }

        if (!IsAllowedFileExtension(fullPath))
        {
            return CreateErrorResult($"File extension not allowed: {Path.GetExtension(fullPath)}", "EXTENSION_NOT_ALLOWED");
        }

        var fileInfo = new FileInfo(fullPath);
        if (fileInfo.Length > _maxFileSize)
        {
            return CreateErrorResult($"File too large: {fileInfo.Length} bytes (max: {_maxFileSize})", "FILE_TOO_LARGE");
        }

        try
        {
            var content = await File.ReadAllTextAsync(fullPath, System.Text.Encoding.GetEncoding(encoding), cancellationToken);
            
            return new AgentExecutionResult
            {
                Success = true,
                Message = $"File read successfully: {filePath}",
                OutputData = new Dictionary<string, object>
                {
                    { "content", content },
                    { "file_path", filePath },
                    { "file_size", fileInfo.Length },
                    { "encoding", encoding },
                    { "last_modified", fileInfo.LastWriteTime }
                }
            };
        }
        catch (Exception ex)
        {
            return CreateErrorResult($"Failed to read file: {ex.Message}", "READ_ERROR");
        }
    }

    /// <summary>
    /// Write file content
    /// </summary>
    private async Task<AgentExecutionResult> WriteFileAsync(AgentCommand command, CancellationToken cancellationToken)
    {
        var filePath = GetParameterValue<string>(command, "file_path");
        var content = GetParameterValue<string>(command, "content");
        var encoding = GetParameterValue<string>(command, "encoding") ?? "UTF-8";
        var append = GetParameterValue<bool>(command, "append");

        if (string.IsNullOrEmpty(filePath))
        {
            return CreateErrorResult("File path is required", "MISSING_PARAMETER");
        }

        if (content == null)
        {
            return CreateErrorResult("Content is required", "MISSING_PARAMETER");
        }

        var fullPath = GetSecurePath(filePath);
        if (fullPath == null)
        {
            return CreateErrorResult("Invalid or unauthorized file path", "SECURITY_VIOLATION");
        }

        if (!IsAllowedFileExtension(fullPath))
        {
            return CreateErrorResult($"File extension not allowed: {Path.GetExtension(fullPath)}", "EXTENSION_NOT_ALLOWED");
        }

        try
        {
            // Ensure directory exists
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var encodingObj = System.Text.Encoding.GetEncoding(encoding);
            
            if (append && File.Exists(fullPath))
            {
                await File.AppendAllTextAsync(fullPath, content, encodingObj, cancellationToken);
            }
            else
            {
                await File.WriteAllTextAsync(fullPath, content, encodingObj, cancellationToken);
            }

            var fileInfo = new FileInfo(fullPath);
            
            return new AgentExecutionResult
            {
                Success = true,
                Message = $"File {(append ? "appended" : "written")} successfully: {filePath}",
                OutputData = new Dictionary<string, object>
                {
                    { "file_path", filePath },
                    { "file_size", fileInfo.Length },
                    { "encoding", encoding },
                    { "append_mode", append },
                    { "last_modified", fileInfo.LastWriteTime }
                }
            };
        }
        catch (Exception ex)
        {
            return CreateErrorResult($"Failed to write file: {ex.Message}", "WRITE_ERROR");
        }
    }

    /// <summary>
    /// List directory contents
    /// </summary>
    private async Task<AgentExecutionResult> ListDirectoryAsync(AgentCommand command, CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // For async consistency
        
        var directoryPath = GetParameterValue<string>(command, "directory_path") ?? ".";
        var includeSubdirectories = GetParameterValue<bool>(command, "include_subdirectories");
        var filePattern = GetParameterValue<string>(command, "file_pattern") ?? "*";

        var fullPath = GetSecurePath(directoryPath);
        if (fullPath == null)
        {
            return CreateErrorResult("Invalid or unauthorized directory path", "SECURITY_VIOLATION");
        }

        if (!Directory.Exists(fullPath))
        {
            return CreateErrorResult($"Directory not found: {directoryPath}", "DIRECTORY_NOT_FOUND");
        }

        try
        {
            var searchOption = includeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = Directory.GetFiles(fullPath, filePattern, searchOption)
                .Where(f => IsAllowedFileExtension(f))
                .Select(f => new
                {
                    name = Path.GetFileName(f),
                    path = Path.GetRelativePath(_workingDirectory, f),
                    size = new FileInfo(f).Length,
                    last_modified = new FileInfo(f).LastWriteTime,
                    type = "file"
                });

            var directories = Directory.GetDirectories(fullPath, "*", searchOption)
                .Select(d => new
                {
                    name = Path.GetFileName(d),
                    path = Path.GetRelativePath(_workingDirectory, d),
                    size = 0L,
                    last_modified = new DirectoryInfo(d).LastWriteTime,
                    type = "directory"
                });

            var items = files.Concat(directories).OrderBy(item => item.type).ThenBy(item => item.name).ToList();

            return new AgentExecutionResult
            {
                Success = true,
                Message = $"Directory listed successfully: {directoryPath} ({items.Count} items)",
                OutputData = new Dictionary<string, object>
                {
                    { "directory_path", directoryPath },
                    { "items", items },
                    { "total_count", items.Count },
                    { "file_count", items.Count(i => i.type == "file") },
                    { "directory_count", items.Count(i => i.type == "directory") }
                }
            };
        }
        catch (Exception ex)
        {
            return CreateErrorResult($"Failed to list directory: {ex.Message}", "LIST_ERROR");
        }
    }

    /// <summary>
    /// Create directory
    /// </summary>
    private async Task<AgentExecutionResult> CreateDirectoryAsync(AgentCommand command, CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // For async consistency
        
        var directoryPath = GetParameterValue<string>(command, "directory_path");

        if (string.IsNullOrEmpty(directoryPath))
        {
            return CreateErrorResult("Directory path is required", "MISSING_PARAMETER");
        }

        var fullPath = GetSecurePath(directoryPath);
        if (fullPath == null)
        {
            return CreateErrorResult("Invalid or unauthorized directory path", "SECURITY_VIOLATION");
        }

        try
        {
            Directory.CreateDirectory(fullPath);
            
            return new AgentExecutionResult
            {
                Success = true,
                Message = $"Directory created successfully: {directoryPath}",
                OutputData = new Dictionary<string, object>
                {
                    { "directory_path", directoryPath },
                    { "full_path", fullPath },
                    { "created_at", DateTime.UtcNow }
                }
            };
        }
        catch (Exception ex)
        {
            return CreateErrorResult($"Failed to create directory: {ex.Message}", "CREATE_ERROR");
        }
    }

    /// <summary>
    /// Delete file
    /// </summary>
    private async Task<AgentExecutionResult> DeleteFileAsync(AgentCommand command, CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // For async consistency
        
        var filePath = GetParameterValue<string>(command, "file_path");

        if (string.IsNullOrEmpty(filePath))
        {
            return CreateErrorResult("File path is required", "MISSING_PARAMETER");
        }

        var fullPath = GetSecurePath(filePath);
        if (fullPath == null)
        {
            return CreateErrorResult("Invalid or unauthorized file path", "SECURITY_VIOLATION");
        }

        if (!File.Exists(fullPath))
        {
            return CreateErrorResult($"File not found: {filePath}", "FILE_NOT_FOUND");
        }

        try
        {
            File.Delete(fullPath);
            
            return new AgentExecutionResult
            {
                Success = true,
                Message = $"File deleted successfully: {filePath}",
                OutputData = new Dictionary<string, object>
                {
                    { "file_path", filePath },
                    { "deleted_at", DateTime.UtcNow }
                }
            };
        }
        catch (Exception ex)
        {
            return CreateErrorResult($"Failed to delete file: {ex.Message}", "DELETE_ERROR");
        }
    }

    /// <summary>
    /// Copy file
    /// </summary>
    private async Task<AgentExecutionResult> CopyFileAsync(AgentCommand command, CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // For async consistency
        
        var sourcePath = GetParameterValue<string>(command, "source_path");
        var destinationPath = GetParameterValue<string>(command, "destination_path");
        var overwrite = GetParameterValue<bool>(command, "overwrite");

        if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(destinationPath))
        {
            return CreateErrorResult("Source and destination paths are required", "MISSING_PARAMETER");
        }

        var sourceFullPath = GetSecurePath(sourcePath);
        var destFullPath = GetSecurePath(destinationPath);
        
        if (sourceFullPath == null || destFullPath == null)
        {
            return CreateErrorResult("Invalid or unauthorized file paths", "SECURITY_VIOLATION");
        }

        if (!File.Exists(sourceFullPath))
        {
            return CreateErrorResult($"Source file not found: {sourcePath}", "FILE_NOT_FOUND");
        }

        if (!IsAllowedFileExtension(sourceFullPath) || !IsAllowedFileExtension(destFullPath))
        {
            return CreateErrorResult("File extension not allowed", "EXTENSION_NOT_ALLOWED");
        }

        try
        {
            // Ensure destination directory exists
            var destDirectory = Path.GetDirectoryName(destFullPath);
            if (!string.IsNullOrEmpty(destDirectory))
            {
                Directory.CreateDirectory(destDirectory);
            }

            File.Copy(sourceFullPath, destFullPath, overwrite);
            
            var destFileInfo = new FileInfo(destFullPath);
            
            return new AgentExecutionResult
            {
                Success = true,
                Message = $"File copied successfully from {sourcePath} to {destinationPath}",
                OutputData = new Dictionary<string, object>
                {
                    { "source_path", sourcePath },
                    { "destination_path", destinationPath },
                    { "file_size", destFileInfo.Length },
                    { "overwrite", overwrite },
                    { "copied_at", DateTime.UtcNow }
                }
            };
        }
        catch (Exception ex)
        {
            return CreateErrorResult($"Failed to copy file: {ex.Message}", "COPY_ERROR");
        }
    }

    /// <summary>
    /// Move file
    /// </summary>
    private async Task<AgentExecutionResult> MoveFileAsync(AgentCommand command, CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // For async consistency
        
        var sourcePath = GetParameterValue<string>(command, "source_path");
        var destinationPath = GetParameterValue<string>(command, "destination_path");
        var overwrite = GetParameterValue<bool>(command, "overwrite");

        if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(destinationPath))
        {
            return CreateErrorResult("Source and destination paths are required", "MISSING_PARAMETER");
        }

        var sourceFullPath = GetSecurePath(sourcePath);
        var destFullPath = GetSecurePath(destinationPath);
        
        if (sourceFullPath == null || destFullPath == null)
        {
            return CreateErrorResult("Invalid or unauthorized file paths", "SECURITY_VIOLATION");
        }

        if (!File.Exists(sourceFullPath))
        {
            return CreateErrorResult($"Source file not found: {sourcePath}", "FILE_NOT_FOUND");
        }

        if (!IsAllowedFileExtension(sourceFullPath) || !IsAllowedFileExtension(destFullPath))
        {
            return CreateErrorResult("File extension not allowed", "EXTENSION_NOT_ALLOWED");
        }

        try
        {
            // Ensure destination directory exists
            var destDirectory = Path.GetDirectoryName(destFullPath);
            if (!string.IsNullOrEmpty(destDirectory))
            {
                Directory.CreateDirectory(destDirectory);
            }

            if (File.Exists(destFullPath) && !overwrite)
            {
                return CreateErrorResult($"Destination file already exists: {destinationPath}", "FILE_EXISTS");
            }

            File.Move(sourceFullPath, destFullPath, overwrite);
            
            var destFileInfo = new FileInfo(destFullPath);
            
            return new AgentExecutionResult
            {
                Success = true,
                Message = $"File moved successfully from {sourcePath} to {destinationPath}",
                OutputData = new Dictionary<string, object>
                {
                    { "source_path", sourcePath },
                    { "destination_path", destinationPath },
                    { "file_size", destFileInfo.Length },
                    { "overwrite", overwrite },
                    { "moved_at", DateTime.UtcNow }
                }
            };
        }
        catch (Exception ex)
        {
            return CreateErrorResult($"Failed to move file: {ex.Message}", "MOVE_ERROR");
        }
    }

    /// <summary>
    /// Get file information
    /// </summary>
    private async Task<AgentExecutionResult> GetFileInfoAsync(AgentCommand command, CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // For async consistency
        
        var path = GetParameterValue<string>(command, "path");

        if (string.IsNullOrEmpty(path))
        {
            return CreateErrorResult("Path is required", "MISSING_PARAMETER");
        }

        var fullPath = GetSecurePath(path);
        if (fullPath == null)
        {
            return CreateErrorResult("Invalid or unauthorized path", "SECURITY_VIOLATION");
        }

        try
        {
            if (File.Exists(fullPath))
            {
                var fileInfo = new FileInfo(fullPath);
                
                return new AgentExecutionResult
                {
                    Success = true,
                    Message = $"File information retrieved: {path}",
                    OutputData = new Dictionary<string, object>
                    {
                        { "path", path },
                        { "type", "file" },
                        { "name", fileInfo.Name },
                        { "extension", fileInfo.Extension },
                        { "size", fileInfo.Length },
                        { "created_at", fileInfo.CreationTime },
                        { "modified_at", fileInfo.LastWriteTime },
                        { "accessed_at", fileInfo.LastAccessTime },
                        { "is_readonly", fileInfo.IsReadOnly },
                        { "attributes", fileInfo.Attributes.ToString() }
                    }
                };
            }
            else if (Directory.Exists(fullPath))
            {
                var dirInfo = new DirectoryInfo(fullPath);
                var fileCount = dirInfo.GetFiles().Length;
                var subdirCount = dirInfo.GetDirectories().Length;
                
                return new AgentExecutionResult
                {
                    Success = true,
                    Message = $"Directory information retrieved: {path}",
                    OutputData = new Dictionary<string, object>
                    {
                        { "path", path },
                        { "type", "directory" },
                        { "name", dirInfo.Name },
                        { "file_count", fileCount },
                        { "subdirectory_count", subdirCount },
                        { "total_items", fileCount + subdirCount },
                        { "created_at", dirInfo.CreationTime },
                        { "modified_at", dirInfo.LastWriteTime },
                        { "accessed_at", dirInfo.LastAccessTime },
                        { "attributes", dirInfo.Attributes.ToString() }
                    }
                };
            }
            else
            {
                return CreateErrorResult($"Path not found: {path}", "PATH_NOT_FOUND");
            }
        }
        catch (Exception ex)
        {
            return CreateErrorResult($"Failed to get file info: {ex.Message}", "INFO_ERROR");
        }
    }

    /// <summary>
    /// Get parameter value from command
    /// </summary>
    private T? GetParameterValue<T>(AgentCommand command, string parameterName)
    {
        if (command.Parameters.TryGetValue(parameterName, out var value))
        {
            if (value is JsonElement jsonElement)
            {
                return JsonSerializer.Deserialize<T>(jsonElement.GetRawText());
            }
            return (T?)value;
        }
        return default;
    }

    /// <summary>
    /// Get secure path within working directory
    /// </summary>
    private string? GetSecurePath(string relativePath)
    {
        try
        {
            var fullPath = Path.IsPathRooted(relativePath) 
                ? relativePath 
                : Path.Combine(_workingDirectory, relativePath);
            
            var normalizedPath = Path.GetFullPath(fullPath);
            
            // Ensure the path is within the working directory
            if (!normalizedPath.StartsWith(_workingDirectory, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Security violation: Path outside working directory: {Path}", relativePath);
                return null;
            }
            
            return normalizedPath;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid path: {Path}", relativePath);
            return null;
        }
    }

    /// <summary>
    /// Check if file extension is allowed
    /// </summary>
    private bool IsAllowedFileExtension(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return string.IsNullOrEmpty(extension) || _allowedExtensions.Contains(extension);
    }

    /// <summary>
    /// Create error result
    /// </summary>
    private AgentExecutionResult CreateErrorResult(string message, string errorCode)
    {
        return new AgentExecutionResult
        {
            Success = false,
            Message = message,
            ErrorCode = errorCode
        };
    }
}
