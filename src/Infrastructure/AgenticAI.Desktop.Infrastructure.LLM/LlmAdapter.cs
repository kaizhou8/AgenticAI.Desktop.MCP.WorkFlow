using AgenticAI.Desktop.Domain.Models;
using AgenticAI.Desktop.Shared.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace AgenticAI.Desktop.Infrastructure.LLM;

/// <summary>
/// LLM Adapter for integrating with various Large Language Model providers
/// Supports OpenAI, Azure OpenAI, and other providers through Semantic Kernel
/// </summary>
public class LlmAdapter : ILlmAdapter
{
    private readonly ILogger<LlmAdapter> _logger;
    private readonly IConfiguration _configuration;
    private readonly ConcurrentDictionary<string, Kernel> _kernels;
    private readonly ConcurrentDictionary<string, LlmProviderInfo> _providers;
    private string _defaultProvider;

    public LlmAdapter(ILogger<LlmAdapter> logger, IConfiguration configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _kernels = new ConcurrentDictionary<string, Kernel>();
        _providers = new ConcurrentDictionary<string, LlmProviderInfo>();
        _defaultProvider = "openai";
        
        InitializeProvidersAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Process a text prompt and return response
    /// </summary>
    public async Task<LlmResponse> ProcessPromptAsync(string prompt, LlmOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("Prompt cannot be null or empty", nameof(prompt));

        var stopwatch = Stopwatch.StartNew();
        var requestOptions = options ?? new LlmOptions();
        var providerId = DetermineProvider(requestOptions.Model);

        _logger.LogDebug("Processing prompt with provider {ProviderId}, model {Model}", providerId, requestOptions.Model ?? "default");

        try
        {
            if (!_kernels.TryGetValue(providerId, out var kernel))
            {
                throw new InvalidOperationException($"Provider {providerId} is not available");
            }

            // Create the prompt with system message if provided
            var fullPrompt = requestOptions.SystemPrompt != null 
                ? $"System: {requestOptions.SystemPrompt}\n\nUser: {prompt}"
                : prompt;

            // Execute the prompt
            var result = await kernel.InvokePromptAsync(fullPrompt, cancellationToken: cancellationToken);
            var content = result.GetValue<string>() ?? string.Empty;

            stopwatch.Stop();

            var response = new LlmResponse
            {
                Content = content,
                Success = true,
                Model = requestOptions.Model ?? "default",
                TokensUsed = EstimateTokenCount(fullPrompt + content), // Simplified token estimation
                Duration = stopwatch.Elapsed,
                Timestamp = DateTime.UtcNow,
                Metadata = new Dictionary<string, object>
                {
                    { "provider", providerId },
                    { "temperature", requestOptions.Temperature },
                    { "max_tokens", requestOptions.MaxTokens }
                }
            };

            _logger.LogDebug("Prompt processed successfully in {Duration}ms, {TokensUsed} tokens used", 
                response.Duration.TotalMilliseconds, response.TokensUsed);

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            _logger.LogError(ex, "Failed to process prompt with provider {ProviderId}", providerId);
            
            return new LlmResponse
            {
                Content = string.Empty,
                Success = false,
                Model = requestOptions.Model ?? "default",
                Duration = stopwatch.Elapsed,
                Timestamp = DateTime.UtcNow,
                ErrorMessage = ex.Message,
                Metadata = new Dictionary<string, object>
                {
                    { "provider", providerId },
                    { "error_type", ex.GetType().Name }
                }
            };
        }
    }

    /// <summary>
    /// Process a conversation and return response
    /// </summary>
    public async Task<LlmResponse> ProcessConversationAsync(IReadOnlyList<LlmMessage> messages, LlmOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (messages == null || !messages.Any())
            throw new ArgumentException("Messages cannot be null or empty", nameof(messages));

        var stopwatch = Stopwatch.StartNew();
        var requestOptions = options ?? new LlmOptions();
        var providerId = DetermineProvider(requestOptions.Model);

        _logger.LogDebug("Processing conversation with {MessageCount} messages using provider {ProviderId}", 
            messages.Count, providerId);

        try
        {
            if (!_kernels.TryGetValue(providerId, out var kernel))
            {
                throw new InvalidOperationException($"Provider {providerId} is not available");
            }

            // Convert conversation to a single prompt (simplified approach)
            var conversationPrompt = BuildConversationPrompt(messages, requestOptions.SystemPrompt);
            
            // Execute the conversation
            var result = await kernel.InvokePromptAsync(conversationPrompt, cancellationToken: cancellationToken);
            var content = result.GetValue<string>() ?? string.Empty;

            stopwatch.Stop();

            var response = new LlmResponse
            {
                Content = content,
                Success = true,
                Model = requestOptions.Model ?? "default",
                TokensUsed = EstimateTokenCount(conversationPrompt + content),
                Duration = stopwatch.Elapsed,
                Timestamp = DateTime.UtcNow,
                Metadata = new Dictionary<string, object>
                {
                    { "provider", providerId },
                    { "message_count", messages.Count },
                    { "temperature", requestOptions.Temperature },
                    { "max_tokens", requestOptions.MaxTokens }
                }
            };

            _logger.LogDebug("Conversation processed successfully in {Duration}ms, {TokensUsed} tokens used", 
                response.Duration.TotalMilliseconds, response.TokensUsed);

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            _logger.LogError(ex, "Failed to process conversation with provider {ProviderId}", providerId);
            
            return new LlmResponse
            {
                Content = string.Empty,
                Success = false,
                Model = requestOptions.Model ?? "default",
                Duration = stopwatch.Elapsed,
                Timestamp = DateTime.UtcNow,
                ErrorMessage = ex.Message,
                Metadata = new Dictionary<string, object>
                {
                    { "provider", providerId },
                    { "message_count", messages.Count },
                    { "error_type", ex.GetType().Name }
                }
            };
        }
    }

    /// <summary>
    /// Initialize LLM providers based on configuration
    /// </summary>
    private async Task InitializeProvidersAsync()
    {
        _logger.LogInformation("Initializing LLM providers...");

        try
        {
            // Initialize OpenAI provider
            await InitializeOpenAIProviderAsync();
            
            // Initialize Azure OpenAI provider (if configured)
            await InitializeAzureOpenAIProviderAsync();
            
            // Initialize local/mock provider for testing
            await InitializeMockProviderAsync();

            _logger.LogInformation("Initialized {ProviderCount} LLM providers", _providers.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize LLM providers");
        }
    }

    /// <summary>
    /// Initialize OpenAI provider
    /// </summary>
    private async Task InitializeOpenAIProviderAsync()
    {
        var apiKey = _configuration["LLM:OpenAI:ApiKey"];
        var orgId = _configuration["LLM:OpenAI:OrganizationId"];

        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("OpenAI API key not configured, skipping OpenAI provider initialization");
            return;
        }

        try
        {
            var kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.AddOpenAIChatCompletion(
                modelId: "gpt-3.5-turbo",
                apiKey: apiKey,
                orgId: orgId);
            
            var kernel = kernelBuilder.Build();
            _kernels.TryAdd("openai", kernel);

            var providerInfo = new LlmProviderInfo
            {
                Id = "openai",
                Name = "OpenAI",
                Description = "OpenAI GPT models",
                SupportedModels = new List<string> { "gpt-3.5-turbo", "gpt-4", "gpt-4-turbo" },
                IsAvailable = true,
                Configuration = new Dictionary<string, object>
                {
                    { "has_api_key", !string.IsNullOrEmpty(apiKey) },
                    { "has_org_id", !string.IsNullOrEmpty(orgId) }
                }
            };

            _providers.TryAdd("openai", providerInfo);
            _logger.LogInformation("OpenAI provider initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize OpenAI provider");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Initialize Azure OpenAI provider
    /// </summary>
    private async Task InitializeAzureOpenAIProviderAsync()
    {
        var endpoint = _configuration["LLM:AzureOpenAI:Endpoint"];
        var apiKey = _configuration["LLM:AzureOpenAI:ApiKey"];
        var deploymentName = _configuration["LLM:AzureOpenAI:DeploymentName"];

        if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(deploymentName))
        {
            _logger.LogWarning("Azure OpenAI configuration incomplete, skipping Azure OpenAI provider initialization");
            return;
        }

        try
        {
            var kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.AddAzureOpenAIChatCompletion(
                deploymentName: deploymentName,
                endpoint: endpoint,
                apiKey: apiKey);
            
            var kernel = kernelBuilder.Build();
            _kernels.TryAdd("azure-openai", kernel);

            var providerInfo = new LlmProviderInfo
            {
                Id = "azure-openai",
                Name = "Azure OpenAI",
                Description = "Azure OpenAI Service",
                SupportedModels = new List<string> { deploymentName },
                IsAvailable = true,
                Configuration = new Dictionary<string, object>
                {
                    { "endpoint", endpoint },
                    { "deployment_name", deploymentName }
                }
            };

            _providers.TryAdd("azure-openai", providerInfo);
            _logger.LogInformation("Azure OpenAI provider initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Azure OpenAI provider");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Initialize mock provider for testing
    /// </summary>
    private async Task InitializeMockProviderAsync()
    {
        try
        {
            // Create a simple mock kernel that returns predefined responses
            var kernelBuilder = Kernel.CreateBuilder();
            var kernel = kernelBuilder.Build();
            
            _kernels.TryAdd("mock", kernel);

            var providerInfo = new LlmProviderInfo
            {
                Id = "mock",
                Name = "Mock Provider",
                Description = "Mock LLM provider for testing",
                SupportedModels = new List<string> { "mock-model" },
                IsAvailable = true,
                Configuration = new Dictionary<string, object>
                {
                    { "type", "mock" },
                    { "purpose", "testing" }
                }
            };

            _providers.TryAdd("mock", providerInfo);
            _logger.LogInformation("Mock provider initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize mock provider");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Determine which provider to use based on model name
    /// </summary>
    private string DetermineProvider(string? modelName)
    {
        if (string.IsNullOrEmpty(modelName))
            return _defaultProvider;

        // Simple model-to-provider mapping
        if (modelName.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase))
            return "openai";
        
        if (modelName.Contains("azure", StringComparison.OrdinalIgnoreCase))
            return "azure-openai";
        
        if (modelName.Contains("mock", StringComparison.OrdinalIgnoreCase))
            return "mock";

        return _defaultProvider;
    }

    /// <summary>
    /// Build conversation prompt from messages
    /// </summary>
    private static string BuildConversationPrompt(IReadOnlyList<LlmMessage> messages, string? systemPrompt)
    {
        var prompt = new System.Text.StringBuilder();
        
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            prompt.AppendLine($"System: {systemPrompt}");
            prompt.AppendLine();
        }

        foreach (var message in messages)
        {
            var roleLabel = message.Role switch
            {
                "system" => "System",
                "user" => "User",
                "assistant" => "Assistant",
                _ => "Unknown"
            };
            
            prompt.AppendLine($"{roleLabel}: {message.Content}");
        }

        return prompt.ToString();
    }

    /// <summary>
    /// Estimate token count (simplified implementation)
    /// </summary>
    private static int EstimateTokenCount(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        // Rough estimation: 1 token ≈ 4 characters for English text
        return (int)Math.Ceiling(text.Length / 4.0);
    }

    /// <summary>
    /// Set the default provider
    /// </summary>
    public void SetDefaultProvider(string providerId)
    {
        if (_providers.ContainsKey(providerId))
        {
            _defaultProvider = providerId;
            _logger.LogInformation("Default provider set to {ProviderId}", providerId);
        }
        else
        {
            _logger.LogWarning("Provider {ProviderId} not found, keeping current default {CurrentDefault}", 
                providerId, _defaultProvider);
        }
    }

    /// <summary>
    /// Get available LLM providers
    /// </summary>
    public async Task<IEnumerable<LlmProviderInfo>> GetAvailableProvidersAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // For async consistency
        
        var providerInfos = new List<LlmProviderInfo>();
        
        foreach (var provider in _providers.Values)
        {
            var providerInfo = new LlmProviderInfo
            {
                Id = provider.Id,
                Name = provider.Name,
                Description = provider.Description,
                SupportedModels = provider.SupportedModels.ToList(),
                IsAvailable = provider.IsAvailable,
                Configuration = new Dictionary<string, object>(provider.Configuration)
            };
            
            providerInfos.Add(providerInfo);
        }
        
        _logger.LogInformation("Retrieved {ProviderCount} available LLM providers", providerInfos.Count);
        return providerInfos;
    }

    /// <summary>
    /// Dispose resources
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        // Note: Kernel in Microsoft.SemanticKernel doesn't implement IDisposable
        // Just clear the collections to release references
        _kernels.Clear();
        _providers.Clear();
        
        await Task.CompletedTask;
    }
}
