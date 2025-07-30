using Elsa.Extensions;
using Elsa.Workflows.Management.Extensions;
using Elsa.EntityFrameworkCore.Extensions;
using Elsa.EntityFrameworkCore.Modules.Management;
using Elsa.EntityFrameworkCore.Modules.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using AgenticAI.Desktop.Shared.Contracts;

namespace AgenticAI.Desktop.Application.Workflow;

/// <summary>
/// Elsa Workflows configuration and dependency injection setup
/// Configures Elsa Workflows 3.x for AgenticAI system integration
/// </summary>
public static class ElsaWorkflowConfiguration
{
    /// <summary>
    /// Add Elsa Workflows services to dependency injection container
    /// </summary>
    public static IServiceCollection AddElsaWorkflows(this IServiceCollection services, IConfiguration configuration)
    {
        // Add Elsa core services
        services.AddElsa(elsa =>
        {
            // Configure workflow management
            elsa.UseWorkflowManagement(management =>
            {
                // Use SQLite for workflow definitions storage
                management.UseEntityFrameworkCore(ef =>
                {
                    ef.UseSqlite(configuration.GetConnectionString("DefaultConnection") 
                        ?? "Data Source=agentai_workflows.db");
                });
            });

            // Configure workflow runtime
            elsa.UseWorkflowRuntime(runtime =>
            {
                // Use SQLite for workflow instances storage
                runtime.UseEntityFrameworkCore(ef =>
                {
                    ef.UseSqlite(configuration.GetConnectionString("DefaultConnection") 
                        ?? "Data Source=agentai_workflows.db");
                });
            });

            // Add scheduling capabilities
            elsa.UseScheduling();

            // Add custom activities for AgenticAI integration
            elsa.AddActivitiesFrom<AgentExecuteActivity>();
            elsa.AddActivitiesFrom<LogActivity>();
            elsa.AddActivitiesFrom<DelayActivity>();
        });

        // Register our enhanced workflow manager
        services.AddScoped<IWorkflowManager, ElsaWorkflowManager>();

        return services;
    }

    /// <summary>
    /// Configure Elsa Workflows database
    /// </summary>
    public static async Task ConfigureElsaDatabaseAsync(IServiceProvider serviceProvider)
    {
        // Run database migrations for Elsa
        using var scope = serviceProvider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IElsaDbContextInitializer>().InitializeAsync();
    }
}
