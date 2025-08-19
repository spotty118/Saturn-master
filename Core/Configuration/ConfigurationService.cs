using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Linq;

namespace Saturn.Core.Configuration
{
    /// <summary>
    /// Unified configuration service implementation that consolidates all Saturn configuration management.
    /// Replaces SettingsManager, ConfigurationManager, and MorphConfigurationManager with a single service.
    /// </summary>
    public class ConfigurationService : IConfigurationService, IDisposable
    {
        private readonly ILogger<ConfigurationService> _logger;
        private readonly IConfigurationValidator _validator;
        private readonly IConfigurationChangeNotifier _changeNotifier;
        private readonly string _configurationPath;
        private readonly JsonSerializerOptions _jsonOptions;
        private SaturnConfiguration? _cachedConfiguration;
        private DateTime _cacheTimestamp;
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
        private readonly FileSystemWatcher? _fileWatcher;
        private volatile bool _disposed;

        public event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged;

        public ConfigurationService(
            ILogger<ConfigurationService> logger,
            IConfigurationValidator validator,
            IConfigurationChangeNotifier changeNotifier)
        {
            _logger = logger;
            _validator = validator;
            _changeNotifier = changeNotifier;
            
            // Configuration storage location
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Saturn"
            );
            Directory.CreateDirectory(appDataPath);
            _configurationPath = Path.Combine(appDataPath, "saturn-config.json");

            // JSON serialization options
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            };

            // Subscribe to change notifications
            _changeNotifier.Subscribe<SaturnConfiguration>("Saturn", OnConfigurationChanged);

            // Set up file system watcher for external config changes
            try
            {
                _fileWatcher = new FileSystemWatcher(Path.GetDirectoryName(_configurationPath)!)
                {
                    Filter = Path.GetFileName(_configurationPath),
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true
                };
                _fileWatcher.Changed += OnConfigurationFileChanged;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to setup file watcher for configuration changes");
            }
        }

        public async Task<SaturnConfiguration> GetConfigurationAsync()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ConfigurationService));
            
            try
            {
                _lock.EnterReadLock();
                try
                {
                    if (_cachedConfiguration != null && IsConfigurationCacheValid())
                    {
                        return _cachedConfiguration;
                    }
                }
                finally
                {
                    _lock.ExitReadLock();
                }

                var configuration = await LoadConfigurationFromFileAsync();
                
                _lock.EnterWriteLock();
                try
                {
                    _cachedConfiguration = configuration;
                    _cacheTimestamp = DateTime.UtcNow;
                }
                finally
                {
                    _lock.ExitWriteLock();
                }

                _logger.LogDebug("Configuration loaded successfully");
                return configuration;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load configuration, using defaults");
                return new SaturnConfiguration();
            }
        }

        public async Task UpdateConfigurationAsync(SaturnConfiguration configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            try
            {
                // Validate configuration
                var validationResult = await _validator.ValidateAsync(configuration);
                if (!validationResult.IsValid)
                {
                    throw new ConfigurationValidationException("Configuration validation failed", validationResult.Errors);
                }

                var oldConfiguration = await GetConfigurationAsync();

                // Save to file
                await SaveConfigurationToFileAsync(configuration);

                // Update cache
                _lock.EnterWriteLock();
                try
                {
                    _cachedConfiguration = configuration;
                    _cacheTimestamp = DateTime.UtcNow;
                }
                finally
                {
                    _lock.ExitWriteLock();
                }

                // Notify of changes
                await _changeNotifier.NotifyConfigurationChangedAsync("Saturn", oldConfiguration, configuration);
                ConfigurationChanged?.Invoke(this, new ConfigurationChangedEventArgs("Saturn", oldConfiguration, configuration));

                _logger.LogInformation("Configuration updated successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update configuration");
                throw;
            }
        }

        public async Task<T> GetSectionAsync<T>() where T : class, new()
        {
            var configuration = await GetConfigurationAsync();
            var sectionName = typeof(T).Name.Replace("Configuration", "");

            return sectionName switch
            {
                "Saturn" => configuration as T ?? new T(),
                "OpenRouter" => GetOpenRouterConfiguration(configuration) as T ?? new T(),
                "Morph" => GetMorphConfiguration(configuration) as T ?? new T(),
                "Agent" => GetAgentConfiguration(configuration) as T ?? new T(),
                "Web" => configuration.Web as T ?? new T(),
                "Logging" => configuration.Logging as T ?? new T(),
                "Performance" => configuration.Performance as T ?? new T(),
                "Security" => configuration.Security as T ?? new T(),
                _ => new T()
            };
        }

        public async Task UpdateSectionAsync<T>(T section) where T : class
        {
            if (section == null)
                throw new ArgumentNullException(nameof(section));

            var configuration = await GetConfigurationAsync();
            var sectionName = typeof(T).Name.Replace("Configuration", "");

            // Update the specific section
            switch (sectionName)
            {
                case "OpenRouter":
                    UpdateOpenRouterConfiguration(configuration, section as OpenRouterConfiguration);
                    break;
                case "Morph":
                    UpdateMorphConfiguration(configuration, section as MorphConfiguration);
                    break;
                case "Agent":
                    UpdateAgentConfiguration(configuration, section as AgentConfiguration);
                    break;
                case "Web":
                    configuration.Web = section as WebConfiguration ?? configuration.Web;
                    break;
                case "Logging":
                    configuration.Logging = section as LoggingConfiguration ?? configuration.Logging;
                    break;
                case "Performance":
                    configuration.Performance = section as PerformanceConfiguration ?? configuration.Performance;
                    break;
                case "Security":
                    configuration.Security = section as SecurityConfiguration ?? configuration.Security;
                    break;
                default:
                    throw new ArgumentException($"Unknown configuration section: {sectionName}");
            }

            await UpdateConfigurationAsync(configuration);
        }

        public async Task<ConfigurationValidationResult> ValidateConfigurationAsync()
        {
            try
            {
                var configuration = await GetConfigurationAsync();
                return await _validator.ValidateAsync(configuration);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to validate configuration");
                return new ConfigurationValidationResult
                {
                    IsValid = false,
                    Errors = new List<string> { $"Validation failed: {ex.Message}" }
                };
            }
        }

        public async Task ResetToDefaultsAsync()
        {
            _logger.LogInformation("Resetting configuration to defaults");
            
            var defaultConfiguration = new SaturnConfiguration();
            await UpdateConfigurationAsync(defaultConfiguration);
        }

        public async Task ExportConfigurationAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            try
            {
                var configuration = await GetConfigurationAsync();
                var json = JsonSerializer.Serialize(configuration, _jsonOptions);
                await File.WriteAllTextAsync(filePath, json);
                
                _logger.LogInformation("Configuration exported to {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to export configuration to {FilePath}", filePath);
                throw;
            }
        }

        public async Task ImportConfigurationAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Configuration file not found: {filePath}");

            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                var configuration = JsonSerializer.Deserialize<SaturnConfiguration>(json, _jsonOptions);
                
                if (configuration == null)
                    throw new InvalidOperationException("Failed to deserialize configuration");

                await UpdateConfigurationAsync(configuration);
                
                _logger.LogInformation("Configuration imported from {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import configuration from {FilePath}", filePath);
                throw;
            }
        }

        private async Task<SaturnConfiguration> LoadConfigurationFromFileAsync()
        {
            if (!File.Exists(_configurationPath))
            {
                _logger.LogInformation("Configuration file not found, creating default configuration");
                var defaultConfig = new SaturnConfiguration();
                await SaveConfigurationToFileAsync(defaultConfig);
                return defaultConfig;
            }

            try
            {
                var json = await File.ReadAllTextAsync(_configurationPath);
                var configuration = JsonSerializer.Deserialize<SaturnConfiguration>(json, _jsonOptions);
                return configuration ?? new SaturnConfiguration();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load configuration from file, using defaults");
                return new SaturnConfiguration();
            }
        }

        private async Task SaveConfigurationToFileAsync(SaturnConfiguration configuration)
        {
            try
            {
                var json = JsonSerializer.Serialize(configuration, _jsonOptions);
                await File.WriteAllTextAsync(_configurationPath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save configuration to file");
                throw;
            }
        }

        private OpenRouterConfiguration GetOpenRouterConfiguration(SaturnConfiguration config)
        {
            // Extract OpenRouter settings from the main configuration
            return new OpenRouterConfiguration
            {
                ApiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY") ?? string.Empty,
                BaseUrl = "https://openrouter.ai/api/v1",
                TimeoutSeconds = 100,
                MaxRetryAttempts = 3,
                RetryBaseDelayMs = 1000
            };
        }

        private MorphConfiguration GetMorphConfiguration(SaturnConfiguration config)
        {
            return new MorphConfiguration
            {
                ApiKey = Environment.GetEnvironmentVariable("MORPH_API_KEY") ?? string.Empty,
                Model = "morph-v3-large",
                DefaultStrategy = Tools.DiffStrategy.Auto,
                EnableFallback = true,
                TimeoutSeconds = 30,
                MaxFileSizeBytes = 10 * 1024 * 1024,
                EnablePerformanceTracking = true
            };
        }

        private AgentConfiguration GetAgentConfiguration(SaturnConfiguration config)
        {
            return new AgentConfiguration
            {
                MaxConcurrentAgents = 10,
                DefaultTimeoutMinutes = 30,
                EnableMultiAgent = true,
                StatePersistenceIntervalSeconds = 60
            };
        }

        private void UpdateOpenRouterConfiguration(SaturnConfiguration config, OpenRouterConfiguration? openRouterConfig)
        {
            // For now, OpenRouter config is environment-based
            // Future enhancement: store in configuration file
        }

        private void UpdateMorphConfiguration(SaturnConfiguration config, MorphConfiguration? morphConfig)
        {
            if (morphConfig != null)
            {
                config.MorphEnabled = !string.IsNullOrEmpty(morphConfig.ApiKey);
            }
        }

        private void UpdateAgentConfiguration(SaturnConfiguration config, AgentConfiguration? agentConfig)
        {
            // Agent configuration updates would be stored in the main config
        }

        private async Task OnConfigurationChanged(SaturnConfiguration oldConfig, SaturnConfiguration newConfig)
        {
            // Handle configuration change side effects
            try
            {
                _logger.LogInformation("Processing configuration changes");
                
                // Clear cache to force reload
                lock (_lock)
                {
                    _cachedConfiguration = null;
                }
                
                // Additional change handling logic can be added here
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing configuration changes");
            }
        }

        private bool IsConfigurationCacheValid()
        {
            // Cache is valid for 5 minutes or until file changes
            return (DateTime.UtcNow - _cacheTimestamp).TotalMinutes < 5;
        }

        private void OnConfigurationFileChanged(object sender, FileSystemEventArgs e)
        {
            Task.Run(async () =>
            {
                try
                {
                    // Debounce multiple file system events
                    await Task.Delay(200);
                    
                    _lock.EnterWriteLock();
                    try
                    {
                        _cachedConfiguration = null;
                        _cacheTimestamp = DateTime.MinValue;
                    }
                    finally
                    {
                        _lock.ExitWriteLock();
                    }
                    
                    _logger.LogDebug("Configuration cache invalidated due to file change");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error handling configuration file change");
                }
            });
        }

        private void OnConfigurationChanged<T>(T oldConfig, T newConfig) where T : class
        {
            _lock.EnterWriteLock();
            try
            {
                _cachedConfiguration = null;
                _cacheTimestamp = DateTime.MinValue;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _fileWatcher?.Dispose();
            _lock.Dispose();
        }
    }

    /// <summary>
    /// Event arguments for configuration change notifications.
    /// </summary>
    public class ConfigurationChangedEventArgs : EventArgs
    {
        public string SectionName { get; }
        public object OldValue { get; }
        public object NewValue { get; }

        public ConfigurationChangedEventArgs(string sectionName, object oldValue, object newValue)
        {
            SectionName = sectionName;
            OldValue = oldValue;
            NewValue = newValue;
        }
    }

    /// <summary>
    /// Exception thrown when configuration validation fails.
    /// </summary>
    public class ConfigurationValidationException : Exception
    {
        public IEnumerable<string> ValidationErrors { get; }

        public ConfigurationValidationException(string message, IEnumerable<string> validationErrors) 
            : base(message)
        {
            ValidationErrors = validationErrors;
        }
    }

    /// <summary>
    /// Result of configuration validation.
    /// </summary>
    public class ConfigurationValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    /// <summary>
    /// Result of API key validation.
    /// </summary>
    public class ApiKeyValidationResult
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Provider { get; set; }
        public bool HasPermissions { get; set; }
    }

    /// <summary>
    /// Result of file path validation.
    /// </summary>
    public class FilePathValidationResult
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public bool CanRead { get; set; }
        public bool CanWrite { get; set; }
        public bool Exists { get; set; }
    }
}