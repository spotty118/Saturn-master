using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Saturn.Tools;

namespace Saturn.Core.Configuration
{
    /// <summary>
    /// Main Saturn application configuration that consolidates all settings.
    /// Provides a single source of truth for application configuration.
    /// </summary>
    public class SaturnConfiguration
    {
        /// <summary>
        /// Application workspace path for file operations and data storage.
        /// </summary>
        [Required]
        public string WorkspacePath { get; set; } = Environment.CurrentDirectory;

        /// <summary>
        /// Default model to use for AI operations.
        /// </summary>
        [Required]
        public string DefaultModel { get; set; } = "anthropic/claude-sonnet-4";

        /// <summary>
        /// Temperature setting for AI model responses (0.0 to 2.0).
        /// </summary>
        [Range(0.0, 2.0)]
        public double Temperature { get; set; } = 0.7;

        /// <summary>
        /// Maximum tokens for AI model responses.
        /// </summary>
        [Range(1, 200000)]
        public int MaxTokens { get; set; } = 4096;

        /// <summary>
        /// Whether to enable streaming responses.
        /// </summary>
        public bool EnableStreaming { get; set; } = true;

        /// <summary>
        /// Whether to require command approval for potentially dangerous operations.
        /// </summary>
        public bool RequireCommandApproval { get; set; } = true;

        /// <summary>
        /// Whether to maintain chat history persistence.
        /// </summary>
        public bool MaintainHistory { get; set; } = true;

        /// <summary>
        /// Maximum number of history messages to maintain.
        /// </summary>
        [Range(1, 1000)]
        public int MaxHistoryMessages { get; set; } = 50;

        /// <summary>
        /// Whether Morph integration is enabled.
        /// </summary>
        public bool MorphEnabled { get; set; } = true;

        /// <summary>
        /// Web server configuration.
        /// </summary>
        public WebConfiguration Web { get; set; } = new();

        /// <summary>
        /// Logging configuration.
        /// </summary>
        public LoggingConfiguration Logging { get; set; } = new();

        /// <summary>
        /// Performance monitoring configuration.
        /// </summary>
        public PerformanceConfiguration Performance { get; set; } = new();

        /// <summary>
        /// Security configuration settings.
        /// </summary>
        public SecurityConfiguration Security { get; set; } = new();
    }

    /// <summary>
    /// OpenRouter API configuration settings.
    /// </summary>
    public class OpenRouterConfiguration
    {
        /// <summary>
        /// OpenRouter API key for authentication.
        /// </summary>
        [Required]
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Base URL for OpenRouter API.
        /// </summary>
        [Url]
        public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";

        /// <summary>
        /// Request timeout in seconds.
        /// </summary>
        [Range(1, 600)]
        public int TimeoutSeconds { get; set; } = 100;

        /// <summary>
        /// Application referer header for attribution.
        /// </summary>
        public string? Referer { get; set; }

        /// <summary>
        /// Application title header for attribution.
        /// </summary>
        public string? Title { get; set; }

        /// <summary>
        /// Maximum retry attempts for failed requests.
        /// </summary>
        [Range(0, 10)]
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>
        /// Base delay for exponential backoff retry logic (in milliseconds).
        /// </summary>
        [Range(100, 30000)]
        public int RetryBaseDelayMs { get; set; } = 1000;
    }

    /// <summary>
    /// Morph API configuration settings (extends the existing MorphConfiguration).
    /// </summary>
    public class MorphConfiguration
    {
        /// <summary>
        /// Morph API key. If empty, will fallback to OpenRouter API key.
        /// </summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Morph model to use for diff operations.
        /// </summary>
        public string Model { get; set; } = "morph-v3-large";

        /// <summary>
        /// Default strategy for diff operations.
        /// </summary>
        public DiffStrategy DefaultStrategy { get; set; } = DiffStrategy.Auto;

        /// <summary>
        /// Whether to enable fallback to traditional diff when Morph fails.
        /// </summary>
        public bool EnableFallback { get; set; } = true;

        /// <summary>
        /// Request timeout in seconds.
        /// </summary>
        [Range(1, 300)]
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Maximum file size for Morph operations (in bytes).
        /// </summary>
        [Range(1024, 50 * 1024 * 1024)] // 1KB to 50MB
        public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024; // 10MB

        /// <summary>
        /// Whether to track performance metrics for Morph operations.
        /// </summary>
        public bool EnablePerformanceTracking { get; set; } = true;
    }

    /// <summary>
    /// Agent system configuration.
    /// </summary>
    public class AgentConfiguration
    {
        /// <summary>
        /// Maximum number of concurrent agents.
        /// </summary>
        [Range(1, 50)]
        public int MaxConcurrentAgents { get; set; } = 10;

        /// <summary>
        /// Default agent timeout in minutes.
        /// </summary>
        [Range(1, 1440)] // 1 minute to 24 hours
        public int DefaultTimeoutMinutes { get; set; } = 30;

        /// <summary>
        /// Whether to enable multi-agent coordination.
        /// </summary>
        public bool EnableMultiAgent { get; set; } = true;

        /// <summary>
        /// Agent state persistence interval in seconds.
        /// </summary>
        [Range(10, 3600)]
        public int StatePersistenceIntervalSeconds { get; set; } = 60;
    }

    /// <summary>
    /// Web server configuration.
    /// </summary>
    public class WebConfiguration
    {
        /// <summary>
        /// Port for the web server.
        /// </summary>
        [Range(1024, 65535)]
        public int Port { get; set; } = 3000;

        /// <summary>
        /// Whether to enable HTTPS.
        /// </summary>
        public bool EnableHttps { get; set; } = false;

        /// <summary>
        /// Whether to enable CORS.
        /// </summary>
        public bool EnableCors { get; set; } = true;

        /// <summary>
        /// Allowed CORS origins (if empty, allows all).
        /// </summary>
        public List<string> CorsOrigins { get; set; } = new();

        /// <summary>
        /// SignalR configuration.
        /// </summary>
        public SignalRConfiguration SignalR { get; set; } = new();
    }

    /// <summary>
    /// SignalR-specific configuration.
    /// </summary>
    public class SignalRConfiguration
    {
        /// <summary>
        /// Maximum message size in bytes.
        /// </summary>
        [Range(1024, 10 * 1024 * 1024)] // 1KB to 10MB
        public long MaxMessageSize { get; set; } = 1024 * 1024; // 1MB

        /// <summary>
        /// Whether to enable detailed error messages.
        /// </summary>
        public bool EnableDetailedErrors { get; set; } = false;

        /// <summary>
        /// Connection timeout in seconds.
        /// </summary>
        [Range(5, 300)]
        public int ConnectionTimeoutSeconds { get; set; } = 60;
    }

    /// <summary>
    /// Logging configuration settings.
    /// </summary>
    public class LoggingConfiguration
    {
        /// <summary>
        /// Default log level.
        /// </summary>
        public string LogLevel { get; set; } = "Information";

        /// <summary>
        /// Whether to enable console logging.
        /// </summary>
        public bool EnableConsole { get; set; } = true;

        /// <summary>
        /// Whether to enable file logging.
        /// </summary>
        public bool EnableFile { get; set; } = true;

        /// <summary>
        /// Log file path (relative to workspace).
        /// </summary>
        public string LogFilePath { get; set; } = "logs/saturn.log";

        /// <summary>
        /// Maximum log file size in MB before rotation.
        /// </summary>
        [Range(1, 1000)]
        public int MaxLogFileSizeMB { get; set; } = 100;

        /// <summary>
        /// Number of log files to retain.
        /// </summary>
        [Range(1, 50)]
        public int RetainedFileCountLimit { get; set; } = 10;

        /// <summary>
        /// Whether to include performance logging.
        /// </summary>
        public bool EnablePerformanceLogging { get; set; } = true;

        /// <summary>
        /// Whether to enable structured logging with correlation IDs.
        /// </summary>
        public bool EnableStructuredLogging { get; set; } = true;
    }

    /// <summary>
    /// Performance monitoring configuration.
    /// </summary>
    public class PerformanceConfiguration
    {
        /// <summary>
        /// Whether to enable performance metrics collection.
        /// </summary>
        public bool EnableMetrics { get; set; } = true;

        /// <summary>
        /// Whether to enable memory usage tracking.
        /// </summary>
        public bool EnableMemoryTracking { get; set; } = true;

        /// <summary>
        /// Whether to enable operation timing.
        /// </summary>
        public bool EnableOperationTiming { get; set; } = true;

        /// <summary>
        /// Metrics collection interval in seconds.
        /// </summary>
        [Range(1, 3600)]
        public int MetricsIntervalSeconds { get; set; } = 60;

        /// <summary>
        /// Metrics retention period in days.
        /// </summary>
        [Range(1, 365)]
        public int MetricsRetentionDays { get; set; } = 30;
    }

    /// <summary>
    /// Security configuration settings.
    /// </summary>
    public class SecurityConfiguration
    {
        /// <summary>
        /// Whether to enable path traversal protection.
        /// </summary>
        public bool EnablePathTraversalProtection { get; set; } = true;

        /// <summary>
        /// Whether to validate file extensions for safety.
        /// </summary>
        public bool ValidateFileExtensions { get; set; } = true;

        /// <summary>
        /// Allowed file extensions for operations.
        /// </summary>
        public List<string> AllowedFileExtensions { get; set; } = new()
        {
            ".cs", ".js", ".ts", ".py", ".java", ".cpp", ".h", ".hpp",
            ".html", ".css", ".json", ".xml", ".yaml", ".yml",
            ".txt", ".md", ".rst", ".sql"
        };

        /// <summary>
        /// Maximum file size for operations (in bytes).
        /// </summary>
        [Range(1024, 100 * 1024 * 1024)] // 1KB to 100MB
        public long MaxFileSize { get; set; } = 50 * 1024 * 1024; // 50MB

        /// <summary>
        /// Whether to enable content sanitization.
        /// </summary>
        public bool EnableContentSanitization { get; set; } = true;

        /// <summary>
        /// Directories that are prohibited for file operations.
        /// </summary>
        public List<string> ProhibitedDirectories { get; set; } = new()
        {
            "/bin", "/sbin", "/usr/bin", "/usr/sbin", "/etc",
            "C:\\Windows", "C:\\Program Files", "C:\\Program Files (x86)"
        };
    }
}