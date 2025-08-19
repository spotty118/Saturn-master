using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Saturn.Agents;
using Saturn.Agents.Core;
using Saturn.Agents.MultiAgent;
using Saturn.Configuration;
using Saturn.Core;
using Saturn.Data;
using Saturn.OpenRouter;
using Saturn.Tools.Core;
using Saturn.Tools;
using Saturn.Web;
using Saturn.Core.Configuration;
using Saturn.Core.Logging;
using Saturn.Core.Performance;
using Saturn.Core.Validation;
using System.Reflection;

namespace Saturn.Core.Extensions
{
    /// <summary>
    /// Extension methods for configuring Saturn services in the dependency injection container.
    /// Provides a centralized, organized approach to service registration following modern .NET patterns.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds all Saturn core services to the service collection.
        /// This is the main entry point for configuring Saturn's dependency injection.
        /// </summary>
        /// <param name="services">The service collection to configure</param>
        /// <param name="configuration">Application configuration</param>
        /// <returns>The service collection for method chaining</returns>
        public static IServiceCollection AddSaturnServices(this IServiceCollection services, IConfiguration configuration)
        {
            // Core infrastructure services
            services.AddSaturnLogging(configuration);
            services.AddSaturnConfiguration(configuration);
            services.AddSaturnValidation();
            
            // Performance infrastructure
            services.AddSaturnPerformance();
            
            // Data layer services
            services.AddSaturnData(configuration);
            
            // OpenRouter integration
            services.AddSaturnOpenRouter(configuration);
            
            // Tool system
            services.AddSaturnTools();
            
            // Agent system
            services.AddSaturnAgents();
            
            // Web services (if needed)
            services.AddSaturnWeb();
            
            return services;
        }

        /// <summary>
        /// Configures structured logging throughout the application.
        /// Replaces Console.WriteLine calls with proper ILogger infrastructure.
        /// </summary>
        public static IServiceCollection AddSaturnLogging(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddLogging(builder =>
            {
                builder.ClearProviders();
                
                // Console logging with structured format
                builder.AddConsole();
                
                // Configure log levels from configuration
                builder.AddConfiguration(configuration.GetSection("Logging"));
                
                // Add custom log filters
                builder.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
                builder.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
                builder.AddFilter("System.Net.Http", LogLevel.Warning);
            });

            // Register custom logging services
            services.AddSingleton<ILoggerFactory, LoggerFactory>();
            services.AddScoped<IOperationLogger, OperationLogger>();
            services.AddScoped<IPerformanceLogger, PerformanceLogger>();

            return services;
        }

        /// <summary>
        /// Configures the unified configuration management system.
        /// Consolidates SettingsManager, ConfigurationManager, and MorphConfigurationManager.
        /// </summary>
        public static IServiceCollection AddSaturnConfiguration(this IServiceCollection services, IConfiguration configuration)
        {
            // Bind configuration models (stored/managed by ConfigurationService)
            services.Configure<SaturnConfiguration>(configuration.GetSection("Saturn"));

            // Unified configuration service
            services.AddSingleton<IConfigurationService, ConfigurationService>();

            // Validators and change notifier implementations
            services.AddSingleton<IConfigurationValidator, SimpleConfigurationValidator>();
            services.AddSingleton<IConfigurationChangeNotifier, ConfigurationChangeNotifier>();

            // Legacy configuration managers (for backward compatibility during transition)
            // SettingsManager can be a singleton; it uses internal locking and file I/O per call
            services.AddSingleton<SettingsManager>();
            services.AddScoped<MorphConfigurationManager>();

            return services;
        }

        /// <summary>
        /// Adds comprehensive input validation and sanitization services.
        /// Provides security and data integrity throughout the application.
        /// </summary>
        public static IServiceCollection AddSaturnValidation(this IServiceCollection services)
        {
            // Core validation services (implemented)
            services.AddScoped<IValidationService, ValidationService>();
            services.AddScoped<IInputSanitizer, InputSanitizer>();
            services.AddScoped<IFilePathValidator, FilePathValidator>();

            return services;
        }

        /// <summary>
        /// Configures high-performance parallel execution infrastructure.
        /// Provides thread pool optimization and parallel processing capabilities.
        /// </summary>
        public static IServiceCollection AddSaturnPerformance(this IServiceCollection services)
        {
            // Parallel execution engine with ThreadPool optimization
            services.AddSingleton<ParallelExecutor>(provider =>
            {
                // Configure optimal concurrency based on system capabilities
                var maxConcurrency = Environment.ProcessorCount * 2;
                return new ParallelExecutor(maxConcurrency);
            });

            return services;
        }

        /// <summary>
        /// Configures data layer services including chat history and persistence.
        /// </summary>
        public static IServiceCollection AddSaturnData(this IServiceCollection services, IConfiguration configuration)
        {
            // Repository pattern implementation with proper DI
            services.AddScoped<IChatHistoryRepository, ChatHistoryRepository>(provider =>
            {
                var config = provider.GetRequiredService<IOptions<SaturnConfiguration>>().Value;
                var logger = provider.GetRequiredService<ILogger<ChatHistoryRepository>>();
                return new ChatHistoryRepository(config.WorkspacePath, logger);
            });

            return services;
        }

        /// <summary>
        /// Configures OpenRouter API integration.
        /// </summary>
        public static IServiceCollection AddSaturnOpenRouter(this IServiceCollection services, IConfiguration configuration)
        {
            // Register OpenRouter client using options; the client internally manages services
            services.AddSingleton<OpenRouterClient>(sp =>
            {
                var section = configuration.GetSection("OpenRouter");
                var settingsManager = sp.GetRequiredService<SettingsManager>();
                var apiKey = settingsManager.GetOpenRouterApiKey() ?? Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");

                var options = new OpenRouterOptions
                {
                    ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey,
                    Referer = section["Referer"],
                    Title = section["Title"]
                };
                return new OpenRouterClient(options);
            });

            return services;
        }

        /// <summary>
        /// Configures the tool system.
        /// ToolRegistry performs reflection-based auto-registration at runtime.
        /// </summary>
        public static IServiceCollection AddSaturnTools(this IServiceCollection services)
        {
            services.AddSingleton<ToolRegistry>();
            return services;
        }

        /// <summary>
        /// Configures the agent system with proper lifecycle management.
        /// </summary>
        public static IServiceCollection AddSaturnAgents(this IServiceCollection services)
        {
            // Agent manager (singleton) initialized with OpenRouter client
            services.AddSingleton<AgentManager>(sp =>
            {
                var mgr = new AgentManager();
                var client = sp.GetRequiredService<OpenRouterClient>();
                mgr.Initialize(client);
                return mgr;
            });

            // Main agent built from configuration + ToolRegistry + ParallelExecutor
            services.AddScoped<Agent>(sp =>
            {
                var configService = sp.GetRequiredService<IConfigurationService>();
                var saturnConfig = configService.GetConfigurationAsync().GetAwaiter().GetResult();
                var openRouterClient = sp.GetRequiredService<OpenRouterClient>();
                var toolRegistry = sp.GetRequiredService<ToolRegistry>();
                var parallelExecutor = sp.GetRequiredService<ParallelExecutor>();

                var agentConfig = new Saturn.Agents.Core.AgentConfiguration
                {
                    Name = "Saturn Assistant",
                    SystemPrompt = "Hello! I'm Saturn, your AI coding assistant. I'm here to help you with your development projects. What would you like to work on today?",
                    Client = openRouterClient,
                    Model = saturnConfig.DefaultModel,
                    Temperature = saturnConfig.Temperature,
                    MaxTokens = saturnConfig.MaxTokens,
                    EnableStreaming = saturnConfig.EnableStreaming,
                    RequireCommandApproval = saturnConfig.RequireCommandApproval,
                    EnableTools = true,
                    MaintainHistory = saturnConfig.MaintainHistory,
                    MaxHistoryMessages = saturnConfig.MaxHistoryMessages
                };

                return new Agent(agentConfig, toolRegistry, parallelExecutor);
            });

            return services;
        }

        /// <summary>
        /// Configures web-related services including SignalR and web server.
        /// </summary>
        public static IServiceCollection AddSaturnWeb(this IServiceCollection services)
        {
            // SignalR for real-time communication
            services.AddSignalR(options =>
            {
                options.EnableDetailedErrors = true;
                options.MaximumReceiveMessageSize = 1024 * 1024; // 1MB
            });

            // Web services
            services.AddScoped<ChatHub>();
            services.AddSingleton<WebServer>();

            return services;
        }

        /// <summary>
        /// Automatically discovers and registers all tool implementations.
        /// Uses reflection to find ITool implementations and registers them with appropriate lifetimes.
        /// </summary>
        private static void RegisterToolImplementations(IServiceCollection services)
        {
            var toolType = typeof(ITool);
            var assembly = Assembly.GetExecutingAssembly();

            var toolTypes = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && toolType.IsAssignableFrom(t))
                .ToList();

            foreach (var type in toolTypes)
            {
                // Register each tool as transient (new instance per request)
                services.AddTransient(toolType, type);
                services.AddTransient(type);
            }

            // Special registrations for tools with dependencies
            services.AddTransient<ApplyDiffTool>();
            services.AddTransient<MorphDiffTool>();
            services.AddTransient<SmartDiffTool>();
        }

        /// <summary>
        /// Validates that all required services are properly registered.
        /// Called during application startup to ensure configuration integrity.
        /// </summary>
        public static void ValidateServiceRegistration(this IServiceProvider serviceProvider)
        {
            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
            
            try
            {
                // Validate core services
                _ = serviceProvider.GetRequiredService<IConfigurationService>();
                _ = serviceProvider.GetRequiredService<IValidationService>();
                _ = serviceProvider.GetRequiredService<ParallelExecutor>();
                _ = serviceProvider.GetRequiredService<ToolRegistry>();
                _ = serviceProvider.GetRequiredService<AgentManager>();
                _ = serviceProvider.GetRequiredService<Agent>();
                _ = serviceProvider.GetRequiredService<OpenRouterClient>();
                
                logger.LogInformation("All Saturn essential services successfully registered and validated");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Service registration validation failed");
                throw;
            }
        }
    }
}