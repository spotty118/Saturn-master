using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Saturn.Tools;
using Saturn.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Saturn.Configuration
{
    public class MorphConfigurationManager
    {
        private readonly string _appDataPath;
        private readonly string _configFilePath;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly SettingsManager? _settingsManager;

        public MorphConfigurationManager(SettingsManager? settingsManager = null)
        {
            _appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Saturn"
            );
            
            _configFilePath = Path.Combine(_appDataPath, "morph-config.json");
            _settingsManager = settingsManager;
            
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Converters = { new JsonStringEnumConverter() }
            };
        }

        public async Task<MorphConfiguration> LoadConfigurationAsync()
        {
            try
            {
                if (!File.Exists(_configFilePath))
                {
                    var defaultConfig = MorphConfiguration.Default;
                    await SaveConfigurationAsync(defaultConfig);
                    return defaultConfig;
                }

                var json = await File.ReadAllTextAsync(_configFilePath);
                var config = JsonSerializer.Deserialize<MorphConfiguration>(json, _jsonOptions);
                
                return config ?? MorphConfiguration.Default;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading Morph configuration: {ex.Message}");
                return MorphConfiguration.Default;
            }
        }

        public async Task SaveConfigurationAsync(MorphConfiguration config)
        {
            try
            {
                if (!Directory.Exists(_appDataPath))
                {
                    Directory.CreateDirectory(_appDataPath);
                }

                var json = JsonSerializer.Serialize(config, _jsonOptions);
                await File.WriteAllTextAsync(_configFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving Morph configuration: {ex.Message}");
            }
        }

        public async Task<bool> IsConfiguredAsync()
        {
            var apiKey = await GetApiKeyAsync();
            return !string.IsNullOrEmpty(apiKey);
        }

        public async Task SetApiKeyAsync(string apiKey)
        {
            var config = await LoadConfigurationAsync();
            config.ApiKey = apiKey;
            await SaveConfigurationAsync(config);
        }

        public async Task<string> GetApiKeyAsync()
        {
            // Priority: 1) Environment variable, 2) Dedicated Morph config, 3) OpenRouter fallback
            
            // Check environment variable first
            var envKey = Environment.GetEnvironmentVariable("MORPH_API_KEY");
            if (!string.IsNullOrEmpty(envKey))
            {
                return envKey;
            }

            // Check dedicated Morph configuration
            var config = await LoadConfigurationAsync();
            if (!string.IsNullOrEmpty(config.ApiKey))
            {
                return config.ApiKey;
            }

            // Fall back to OpenRouter API key if available
            if (_settingsManager != null)
            {
                var openRouterKey = await _settingsManager.GetApiKeyAsync();
                if (!string.IsNullOrEmpty(openRouterKey))
                {
                    return openRouterKey;
                }
            }

            return string.Empty;
        }

        public async Task<string> GetApiKeySourceAsync()
        {
            var envKey = Environment.GetEnvironmentVariable("MORPH_API_KEY");
            if (!string.IsNullOrEmpty(envKey))
            {
                return "Environment Variable (MORPH_API_KEY)";
            }

            var config = await LoadConfigurationAsync();
            if (!string.IsNullOrEmpty(config.ApiKey))
            {
                return "Dedicated Morph Configuration";
            }

            if (_settingsManager != null)
            {
                var openRouterKey = await _settingsManager.GetApiKeyAsync();
                if (!string.IsNullOrEmpty(openRouterKey))
                {
                    return "OpenRouter API Key (Fallback)";
                }
            }

            return "No API Key Available";
        }

        public async Task<bool> IsMorphEnabledAsync()
        {
            // Consider Morph "enabled" if any API key source is configured
            var apiKey = await GetApiKeyAsync();
            return !string.IsNullOrEmpty(apiKey);
        }

        public async Task SetDefaultStrategyAsync(DiffStrategy strategy)
        {
            var config = await LoadConfigurationAsync();
            config.DefaultStrategy = strategy;
            await SaveConfigurationAsync(config);
        }

        public async Task<DiffStrategy> GetDefaultStrategyAsync()
        {
            var config = await LoadConfigurationAsync();
            return config.DefaultStrategy;
        }
    }

    public static class MorphConfigurationExtensions
    {
        public static void AddMorphConfiguration(this Microsoft.Extensions.DependencyInjection.IServiceCollection services)
        {
            services.AddSingleton<MorphConfigurationManager>();
            services.AddHttpClient<MorphDiffTool>();
        }
    }
}