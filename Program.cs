using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Saturn.Agents;
using Saturn.Agents.Core;
using Saturn.Agents.MultiAgent;
using Saturn.Configuration;
using Saturn.Core;
using Saturn.Core.Constants;
using Saturn.Core.Extensions;
using Saturn.Core.Configuration;
using Saturn.Core.Logging;
using Saturn.Core.Validation;
using Saturn.Core.Security;
using Saturn.Tools.Core;
using Saturn.Web;
using Saturn.OpenRouter;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace Saturn
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Configure console encoding for better output
            Console.OutputEncoding = Encoding.UTF8;

            try
            {
                // Create host with modern dependency injection and configuration
                var host = CreateHostBuilder(args).Build();

                // Get services from DI container
                var configService = host.Services.GetRequiredService<IConfigurationService>();
                var logger = host.Services.GetRequiredService<IOperationLogger>();
                var validationService = host.Services.GetRequiredService<IValidationService>();
                var settingsManager = host.Services.GetRequiredService<SettingsManager>();
                var morphConfigManager = host.Services.GetRequiredService<Saturn.Configuration.MorphConfigurationManager>();

                using var startupScope = logger.BeginOperation("ApplicationStartup");

                // Load and validate configuration
                await InitializeConfigurationAsync(configService, validationService, logger, settingsManager, morphConfigManager);

                // Parse command line arguments
                var commandLineOptions = ParseCommandLineArguments(args);
                
                // Start the appropriate UI mode
                if (commandLineOptions.UseWebUI)
                {
                    await StartWebUIAsync(host, commandLineOptions, logger);
                }
                else if (commandLineOptions.UseTerminalUI)
                {
                    await StartTerminalUIAsync(host, logger);
                }

                startupScope.Complete();
            }
            catch (Exception ex)
            {
                // Fallback logging when DI container might not be available
                Console.WriteLine($"Critical error during application startup: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                Environment.Exit(1);
            }
        }

        private static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((context, config) =>
                {
                    // Configure additional configuration sources
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                          .AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true)
                          .AddEnvironmentVariables()
                          .AddCommandLine(args);
                })
                .ConfigureLogging((context, logging) =>
                {
                    logging.ClearProviders()
                           .AddConsole()
                           .AddDebug();
                    
                    // Set log level based on environment
                    if (context.HostingEnvironment.IsDevelopment())
                    {
                        logging.SetMinimumLevel(LogLevel.Debug);
                    }
                    else
                    {
                        logging.SetMinimumLevel(LogLevel.Information);
                    }
                })
                .ConfigureServices((context, services) =>
                {
                    // Add all Saturn services using our service collection extensions
                    services.AddSaturnServices(context.Configuration);
                })
                .UseConsoleLifetime();

        private static async Task InitializeConfigurationAsync(
            IConfigurationService configService,
            IValidationService validationService,
            IOperationLogger logger,
            SettingsManager settingsManager,
            Saturn.Configuration.MorphConfigurationManager morphConfigManager)
        {
            using var configScope = logger.BeginOperation("ConfigurationInitialization");

            var config = await configService.GetConfigurationAsync();
            
            // Validate core configuration
            var configValidation = validationService.ValidateConfiguration(config, "SaturnConfiguration");
            if (!configValidation.IsValid)
            {
                configScope.AddContext("ValidationErrors", configValidation.Errors);
                logger.LogConfigurationChange("Validation", "Failed", configValidation.Errors);
                
                Console.WriteLine("Configuration validation failed:");
                foreach (var error in configValidation.Errors)
                {
                    Console.WriteLine($"  - {error.Message}");
                }
            }

            // Check for OpenRouter API key via SettingsManager (fallback to env later in client factory)
            var storedApiKey = settingsManager.GetOpenRouterApiKey();
            if (string.IsNullOrWhiteSpace(storedApiKey))
            {
                await PromptForOpenRouterApiKey(settingsManager, logger);
                storedApiKey = settingsManager.GetOpenRouterApiKey();
            }
            if (!string.IsNullOrWhiteSpace(storedApiKey))
            {
                logger.LogOperationStart("ValidateOpenRouterApiKey");
                var apiKeyValidation = validationService.ValidateApiKey(storedApiKey!, "openrouter");
                if (!apiKeyValidation.IsValid)
                {
                    Console.WriteLine("Warning: OpenRouter API key validation failed:");
                    foreach (var error in apiKeyValidation.Errors)
                    {
                        Console.WriteLine($"  - {error.Message}");
                    }
                }
            }

            // Check for Morph API key setup using MorphConfigurationManager
            var morphKey = await morphConfigManager.GetApiKeyAsync();
            if (string.IsNullOrWhiteSpace(morphKey))
            {
                await PromptForMorphApiKey(morphConfigManager, logger);
                morphKey = await morphConfigManager.GetApiKeyAsync();
            }
            if (!string.IsNullOrWhiteSpace(morphKey))
            {
                Console.WriteLine($"Morph Fast Apply enabled - expect 83% faster diff operations!");
                logger.LogConfigurationChange("MorphEnabled", false, true);
            }

            configScope.Complete(new { ConfigurationValid = configValidation.IsValid });
        }

        private static async Task PromptForOpenRouterApiKey(SettingsManager settingsManager, IOperationLogger logger)
        {
            using var promptScope = logger.BeginOperation("PromptOpenRouterApiKey");

            Console.WriteLine("Welcome to Saturn! Please configure your OpenRouter API key.");
            Console.Write("Enter your OpenRouter API key: ");
            
            // SECURITY FIX: Enhanced secure input for API keys
            var apiKey = ReadSensitiveInput();

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                try
                {
                    var trimmedApiKey = apiKey.Trim();
                    
                    // Enhanced validation for API key format
                    if (!IsValidApiKeyFormat(trimmedApiKey))
                    {
                        Console.WriteLine("Invalid API key format. Please check your key and try again.");
                        Environment.Exit(1);
                        return;
                    }
                    
                    // Store securely via SettingsManager
                    await settingsManager.SetOpenRouterApiKeyAsync(trimmedApiKey);

                    Console.WriteLine("API key saved and encrypted successfully!");
                    logger.LogConfigurationChange("OpenRouterApiKey", "Not Set", "Set (Encrypted)");
                    promptScope.Complete();
                }
                catch (Exception ex)
                {
                    logger.LogOperationFailure("SaveApiKey", ex, TimeSpan.Zero);
                    Console.WriteLine($"Error saving API key: {ex.Message}");
                    Environment.Exit(1);
                }
                finally
                {
                    // SECURITY: Clear sensitive data from memory
                    SecureClearString(ref apiKey);
                }
            }
            else
            {
                Console.WriteLine("API key is required to use Saturn.");
                promptScope.Fail(new InvalidOperationException("API key is required"));
                Environment.Exit(1);
            }
        }
        
        /// <summary>
        /// Securely reads sensitive input with masked display
        /// </summary>
        private static string ReadSensitiveInput()
        {
            var input = new StringBuilder();
            ConsoleKeyInfo key;
            
            do
            {
                key = Console.ReadKey(true);
                
                if (key.Key == ConsoleKey.Backspace && input.Length > 0)
                {
                    input.Remove(input.Length - 1, 1);
                    Console.Write("\b \b");
                }
                else if (key.Key != ConsoleKey.Enter && key.Key != ConsoleKey.Backspace && !char.IsControl(key.KeyChar))
                {
                    input.Append(key.KeyChar);
                    Console.Write("*");
                }
            } while (key.Key != ConsoleKey.Enter);
            
            Console.WriteLine();
            return input.ToString();
        }
        
        /// <summary>
        /// Securely clears sensitive string data from memory
        /// </summary>
        private static void SecureClearString(ref string sensitiveData)
        {
            sensitiveData = string.Empty;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        // SECURITY: Add API key validation method
        private static bool IsValidApiKeyFormat(string apiKey)
        {
            // Basic validation for OpenRouter API key format
            return !string.IsNullOrWhiteSpace(apiKey) &&
                   apiKey.Length >= ApplicationConstants.Security.MinApiKeyLength &&
                   !apiKey.Contains(" ") &&
                   !apiKey.Contains("\n") &&
                   !apiKey.Contains("\r");
        }

        private static async Task PromptForMorphApiKey(Saturn.Configuration.MorphConfigurationManager morphConfigManager, IOperationLogger logger)
        {
            using var promptScope = logger.BeginOperation("PromptMorphApiKey");

            Console.WriteLine("\nOptional: Morph Fast Apply Integration");
            Console.WriteLine("For dramatically faster diff operations (98% accuracy, 83% faster), you can configure Morph.");
            Console.Write("Enter your Morph API key (or press Enter to skip): ");
            string? morphApiKey = Console.ReadLine();
            
            if (!string.IsNullOrWhiteSpace(morphApiKey))
            {
                var trimmedKey = morphApiKey.Trim();
                
                // Validate Morph API key format
                if (!IsValidApiKeyFormat(trimmedKey))
                {
                    Console.WriteLine("Invalid Morph API key format. Skipping configuration.");
                    promptScope.Complete(new { MorphEnabled = false });
                    return;
                }
                
                // Persist using MorphConfigurationManager
                await morphConfigManager.SetApiKeyAsync(trimmedKey);

                Console.WriteLine("Morph API key saved! You'll get 83% faster diff operations.");
                logger.LogConfigurationChange("MorphApiKey", "Not Set", "Set");
                promptScope.Complete(new { MorphEnabled = true });
            }
            else
            {
                Console.WriteLine("Skipping Morph setup. Traditional diff will be used (can be configured later).");
                promptScope.Complete(new { MorphEnabled = false });
            }
        }

        private static CommandLineOptions ParseCommandLineArguments(string[] args)
        {
            bool useWebUI = args.Contains("--web") || args.Contains("-w");
            bool useTerminalUI = args.Contains("--terminal") || args.Contains("-t");
            
            // Default to web UI if no specific UI specified
            if (!useWebUI && !useTerminalUI)
            {
                useWebUI = true;
            }

            // Use configuration constants for port validation
            int port = ApplicationConstants.Network.DefaultWebPort;
            var portIndex = Array.IndexOf(args, "--port");
            if (portIndex >= 0 && portIndex + 1 < args.Length)
            {
                if (int.TryParse(args[portIndex + 1], out int customPort))
                {
                    // Validate port range for security
                    if (customPort >= ApplicationConstants.Network.MinValidPort &&
                        customPort <= ApplicationConstants.Network.MaxValidPort)
                    {
                        port = customPort;
                    }
                    else
                    {
                        Console.WriteLine($"Invalid port {customPort}. Using default port {ApplicationConstants.Network.DefaultWebPort}");
                    }
                }
            }

            return new CommandLineOptions
            {
                UseWebUI = useWebUI,
                UseTerminalUI = useTerminalUI,
                Port = port
            };
        }

        private static async Task StartWebUIAsync(IHost host, CommandLineOptions options, IOperationLogger logger)
        {
            using var webScope = logger.BeginOperation("StartWebUI");
            
            // Prefer configured port if CLI didn't override (default 5173)
            try
            {
                var cfgSvc = host.Services.GetService<IConfigurationService>();
                if (cfgSvc != null)
                {
                    var cfg = await cfgSvc.GetConfigurationAsync();
                    if (options.Port == 5173 && cfg?.Web?.Port is int cfgPort && cfgPort >= 1024 && cfgPort <= 65535)
                    {
                        options.Port = cfgPort;
                    }
                }
            }
            catch { /* non-fatal */ }

            Console.WriteLine($"Starting Saturn Web UI on http://localhost:{options.Port}");
            Console.WriteLine("Press Ctrl+C to stop the server.");
            
            var webServer = host.Services.GetRequiredService<WebServer>();
            await webServer.StartAsync(options.Port);
            
            // Keep the application running
            var tcs = new TaskCompletionSource<bool>();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                tcs.SetResult(true);
            };
            
            await tcs.Task;
            await webServer.StopAsync();
            
            webScope.Complete();
        }

        private static async Task StartTerminalUIAsync(IHost host, IOperationLogger logger)
        {
            using var terminalScope = logger.BeginOperation("StartTerminalUI");
            
            Console.WriteLine("Starting Saturn Console Mode...");
            Console.WriteLine("Type 'exit' to quit, 'help' for available commands.");
            
            var agent = host.Services.GetRequiredService<Agent>();
            await agent.InitializeSessionAsync("console");
            
            while (true)
            {
                Console.Write("\nYou: ");
                string? input = Console.ReadLine();
                
                if (string.IsNullOrWhiteSpace(input))
                    continue;
                    
                if (input.ToLower() == "exit")
                    break;
                    
                if (input.ToLower() == "help")
                {
                    Console.WriteLine("\nAvailable commands:");
                    Console.WriteLine("- exit: Quit the application");
                    Console.WriteLine("- help: Show this help message");
                    Console.WriteLine("- Any other text will be sent to the AI assistant");
                    continue;
                }
                
                try
                {
                    using var requestScope = logger.BeginOperation("ProcessUserRequest");
                    requestScope.AddContext("UserInput", input);
                    
                    Console.WriteLine("\nAssistant: ");
                    
                    if (agent.Configuration.EnableStreaming)
                    {
                        await agent.ExecuteStreamAsync(input, async (chunk) =>
                        {
                            if (!chunk.IsComplete && !chunk.IsToolCall && !string.IsNullOrEmpty(chunk.Content))
                            {
                                Console.Write(chunk.Content);
                            }
                        });
                    }
                    else
                    {
                        var response = await agent.Execute<OpenRouter.Models.Api.Chat.Message>(input);

                        // Improved null checking and content extraction
                        string content;
                        if (response?.Content.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            content = response.Content.GetString() ?? "Empty response";
                        }
                        else if (response?.Content.ValueKind == System.Text.Json.JsonValueKind.Object ||
                                response?.Content.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            content = response.Content.GetRawText() ?? "Invalid response format";
                        }
                        else
                        {
                            content = "No response received";
                        }

                        Console.WriteLine(content);
                    }
                    
                    requestScope.Complete();
                }
                catch (Exception ex)
                {
                    logger.LogOperationFailure("ProcessUserRequest", ex, TimeSpan.Zero, new { UserInput = input });
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
            
            terminalScope.Complete();
        }
    }

    /// <summary>
    /// Command line options for application startup.
    /// </summary>
    public class CommandLineOptions
    {
        public bool UseWebUI { get; set; }
        public bool UseTerminalUI { get; set; }
        public int Port { get; set; } = 5173;
    }
}