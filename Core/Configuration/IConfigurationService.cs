using System;
using System.Threading.Tasks;

namespace Saturn.Core.Configuration
{
    /// <summary>
    /// Unified configuration service interface that consolidates all Saturn configuration management.
    /// Replaces the multiple competing configuration systems with a single, coherent approach.
    /// </summary>
    public interface IConfigurationService
    {
        /// <summary>
        /// Gets the current Saturn configuration with all settings consolidated.
        /// </summary>
        Task<SaturnConfiguration> GetConfigurationAsync();

        /// <summary>
        /// Updates the Saturn configuration with validation and change notifications.
        /// </summary>
        Task UpdateConfigurationAsync(SaturnConfiguration configuration);

        /// <summary>
        /// Gets a specific configuration section by type.
        /// </summary>
        Task<T> GetSectionAsync<T>() where T : class, new();

        /// <summary>
        /// Updates a specific configuration section with validation.
        /// </summary>
        Task UpdateSectionAsync<T>(T section) where T : class;

        /// <summary>
        /// Validates the current configuration and returns any validation errors.
        /// </summary>
        Task<ConfigurationValidationResult> ValidateConfigurationAsync();

        /// <summary>
        /// Resets configuration to default values.
        /// </summary>
        Task ResetToDefaultsAsync();

        /// <summary>
        /// Exports current configuration to a file.
        /// </summary>
        Task ExportConfigurationAsync(string filePath);

        /// <summary>
        /// Imports configuration from a file with validation.
        /// </summary>
        Task ImportConfigurationAsync(string filePath);

        /// <summary>
        /// Event raised when configuration changes.
        /// </summary>
        event EventHandler<ConfigurationChangedEventArgs> ConfigurationChanged;
    }

    /// <summary>
    /// Service for validating configuration values and dependencies.
    /// </summary>
    public interface IConfigurationValidator
    {
        /// <summary>
        /// Validates a configuration object and returns validation results.
        /// </summary>
        Task<ConfigurationValidationResult> ValidateAsync<T>(T configuration) where T : class;

        /// <summary>
        /// Validates API key connectivity and permissions.
        /// </summary>
        Task<ApiKeyValidationResult> ValidateApiKeyAsync(string apiKey, string provider);

        /// <summary>
        /// Validates file path accessibility and permissions.
        /// </summary>
        Task<FilePathValidationResult> ValidateFilePathAsync(string filePath, bool requireWrite = false);
    }

    /// <summary>
    /// Service for managing configuration change notifications.
    /// </summary>
    public interface IConfigurationChangeNotifier
    {
        /// <summary>
        /// Notifies all subscribers about configuration changes.
        /// </summary>
        Task NotifyConfigurationChangedAsync(string section, object oldValue, object newValue);

        /// <summary>
        /// Subscribes to configuration change notifications for a specific section.
        /// </summary>
        void Subscribe<T>(string section, Func<T, T, Task> handler) where T : class;

        /// <summary>
        /// Unsubscribes from configuration change notifications.
        /// </summary>
        void Unsubscribe(string section, Delegate handler);
    }
}