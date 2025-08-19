using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Saturn.Core.Validation;
using Saturn.Core.ErrorHandling;
using Saturn.Core.Logging;

namespace Saturn.Core.Configuration
{
    /// <summary>
    /// Comprehensive examples demonstrating Saturn's configuration validation patterns
    /// and integration with the validation service, error handling, and logging systems.
    /// </summary>
    public class ConfigurationValidationExamples
    {
        private readonly IValidationService _validationService;
        private readonly IOperationLogger _logger;

        public ConfigurationValidationExamples(IValidationService validationService, IOperationLogger logger)
        {
            _validationService = validationService ?? throw new ArgumentNullException(nameof(validationService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Example: Validate OpenRouter API configuration with comprehensive checks
        /// </summary>
        public async Task<ValidationResult> ValidateOpenRouterConfigAsync(OpenRouterConfig config)
        {
            return await StandardErrorHandler.HandleAsync(async () =>
            {
                var validationResults = new List<ValidationIssue>();

                // API Key validation
                if (string.IsNullOrWhiteSpace(config.ApiKey))
                {
                    // Try environment variable fallback
                    config.ApiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
                    if (string.IsNullOrWhiteSpace(config.ApiKey))
                    {
                        validationResults.Add(new ValidationIssue
                        {
                            Property = nameof(config.ApiKey),
                            Message = "OpenRouter API key is required. Set ApiKey property or OPENROUTER_API_KEY environment variable.",
                            Severity = ValidationSeverity.Error
                        });
                    }
                }
                else
                {
                    var apiKeyValidation = await _validationService.ValidateApiKeyAsync(config.ApiKey, "OpenRouter");
                    if (!apiKeyValidation.IsValid)
                    {
                        validationResults.Add(new ValidationIssue
                        {
                            Property = nameof(config.ApiKey),
                            Message = $"Invalid OpenRouter API key format: {apiKeyValidation.Error}",
                            Severity = ValidationSeverity.Error
                        });
                    }
                }

                // Base URL validation
                if (!string.IsNullOrWhiteSpace(config.BaseUrl))
                {
                    var urlValidation = await _validationService.ValidateUrlAsync(config.BaseUrl);
                    if (!urlValidation.IsValid)
                    {
                        validationResults.Add(new ValidationIssue
                        {
                            Property = nameof(config.BaseUrl),
                            Message = $"Invalid base URL: {urlValidation.Error}",
                            Severity = ValidationSeverity.Error
                        });
                    }
                    else if (!config.BaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        validationResults.Add(new ValidationIssue
                        {
                            Property = nameof(config.BaseUrl),
                            Message = "Base URL should use HTTPS for security",
                            Severity = ValidationSeverity.Warning
                        });
                    }
                }

                // Timeout validation
                if (config.TimeoutSeconds <= 0 || config.TimeoutSeconds > 300)
                {
                    validationResults.Add(new ValidationIssue
                    {
                        Property = nameof(config.TimeoutSeconds),
                        Message = "Timeout must be between 1 and 300 seconds",
                        Severity = config.TimeoutSeconds <= 0 ? ValidationSeverity.Error : ValidationSeverity.Warning
                    });
                }

                // Model validation
                if (!string.IsNullOrWhiteSpace(config.DefaultModel))
                {
                    var modelValidation = ValidateModelName(config.DefaultModel);
                    if (!modelValidation.IsValid)
                    {
                        validationResults.Add(new ValidationIssue
                        {
                            Property = nameof(config.DefaultModel),
                            Message = modelValidation.Message,
                            Severity = ValidationSeverity.Warning
                        });
                    }
                }

                // Rate limiting validation
                if (config.MaxRequestsPerMinute.HasValue && config.MaxRequestsPerMinute <= 0)
                {
                    validationResults.Add(new ValidationIssue
                    {
                        Property = nameof(config.MaxRequestsPerMinute),
                        Message = "Max requests per minute must be positive",
                        Severity = ValidationSeverity.Error
                    });
                }

                return new ValidationResult
                {
                    IsValid = !validationResults.Any(v => v.Severity == ValidationSeverity.Error),
                    Issues = validationResults,
                    ValidatedAt = DateTime.UtcNow
                };

            }, "ValidateOpenRouterConfig", _logger, new ValidationResult { IsValid = false });
        }

        /// <summary>
        /// Example: Validate agent configuration with security and performance checks
        /// </summary>
        public async Task<ValidationResult> ValidateAgentConfigAsync(AgentConfig config)
        {
            return await StandardErrorHandler.HandleAsync(async () =>
            {
                var validationResults = new List<ValidationIssue>();

                // Name validation
                var nameValidation = await _validationService.ValidateAgentNameAsync(config.Name);
                if (!nameValidation.IsValid)
                {
                    validationResults.Add(new ValidationIssue
                    {
                        Property = nameof(config.Name),
                        Message = $"Invalid agent name: {nameValidation.Error}",
                        Severity = ValidationSeverity.Error
                    });
                }

                // System prompt validation
                if (!string.IsNullOrWhiteSpace(config.SystemPrompt))
                {
                    var promptValidation = await _validationService.ValidateUserInputAsync(config.SystemPrompt);
                    if (!promptValidation.IsValid)
                    {
                        validationResults.Add(new ValidationIssue
                        {
                            Property = nameof(config.SystemPrompt),
                            Message = $"System prompt contains unsafe content: {promptValidation.Error}",
                            Severity = ValidationSeverity.Error
                        });
                    }

                    if (config.SystemPrompt.Length > 8000)
                    {
                        validationResults.Add(new ValidationIssue
                        {
                            Property = nameof(config.SystemPrompt),
                            Message = "System prompt exceeds recommended length (8000 characters)",
                            Severity = ValidationSeverity.Warning
                        });
                    }
                }

                // Temperature validation
                if (config.Temperature < 0 || config.Temperature > 2)
                {
                    validationResults.Add(new ValidationIssue
                    {
                        Property = nameof(config.Temperature),
                        Message = "Temperature must be between 0 and 2",
                        Severity = ValidationSeverity.Error
                    });
                }

                // Max tokens validation
                if (config.MaxTokens <= 0 || config.MaxTokens > 200000)
                {
                    validationResults.Add(new ValidationIssue
                    {
                        Property = nameof(config.MaxTokens),
                        Message = "Max tokens must be between 1 and 200,000",
                        Severity = config.MaxTokens <= 0 ? ValidationSeverity.Error : ValidationSeverity.Warning
                    });
                }

                // Tool configuration validation
                if (config.EnabledTools?.Any() == true)
                {
                    foreach (var tool in config.EnabledTools)
                    {
                        if (string.IsNullOrWhiteSpace(tool))
                        {
                            validationResults.Add(new ValidationIssue
                            {
                                Property = nameof(config.EnabledTools),
                                Message = "Tool names cannot be empty",
                                Severity = ValidationSeverity.Error
                            });
                        }
                    }
                }

                // Parallel execution limits
                if (config.MaxParallelTools.HasValue && config.MaxParallelTools <= 0)
                {
                    validationResults.Add(new ValidationIssue
                    {
                        Property = nameof(config.MaxParallelTools),
                        Message = "Max parallel tools must be positive",
                        Severity = ValidationSeverity.Error
                    });
                }

                return new ValidationResult
                {
                    IsValid = !validationResults.Any(v => v.Severity == ValidationSeverity.Error),
                    Issues = validationResults,
                    ValidatedAt = DateTime.UtcNow
                };

            }, "ValidateAgentConfig", _logger, new ValidationResult { IsValid = false });
        }

        /// <summary>
        /// Example: Validate file-based configuration with path security checks
        /// </summary>
        public async Task<ValidationResult> ValidateFileConfigAsync(FileConfig config)
        {
            return await StandardErrorHandler.HandleAsync(async () =>
            {
                var validationResults = new List<ValidationIssue>();

                // Working directory validation
                if (!string.IsNullOrWhiteSpace(config.WorkingDirectory))
                {
                    var pathValidation = await _validationService.ValidateFilePathAsync(config.WorkingDirectory);
                    if (!pathValidation.IsValid)
                    {
                        validationResults.Add(new ValidationIssue
                        {
                            Property = nameof(config.WorkingDirectory),
                            Message = $"Invalid working directory: {pathValidation.Error}",
                            Severity = ValidationSeverity.Error
                        });
                    }
                    else if (!Directory.Exists(config.WorkingDirectory))
                    {
                        validationResults.Add(new ValidationIssue
                        {
                            Property = nameof(config.WorkingDirectory),
                            Message = "Working directory does not exist",
                            Severity = ValidationSeverity.Warning
                        });
                    }
                }

                // Allowed paths validation
                if (config.AllowedPaths?.Any() == true)
                {
                    foreach (var path in config.AllowedPaths)
                    {
                        var pathValidation = await _validationService.ValidateFilePathAsync(path);
                        if (!pathValidation.IsValid)
                        {
                            validationResults.Add(new ValidationIssue
                            {
                                Property = nameof(config.AllowedPaths),
                                Message = $"Invalid allowed path '{path}': {pathValidation.Error}",
                                Severity = ValidationSeverity.Error
                            });
                        }
                    }
                }

                // Blocked extensions validation
                if (config.BlockedExtensions?.Any() == true)
                {
                    foreach (var ext in config.BlockedExtensions)
                    {
                        if (string.IsNullOrWhiteSpace(ext) || !ext.StartsWith("."))
                        {
                            validationResults.Add(new ValidationIssue
                            {
                                Property = nameof(config.BlockedExtensions),
                                Message = $"Invalid blocked extension '{ext}': must start with '.'",
                                Severity = ValidationSeverity.Error
                            });
                        }
                    }
                }

                // File size limits
                if (config.MaxFileSizeBytes <= 0)
                {
                    validationResults.Add(new ValidationIssue
                    {
                        Property = nameof(config.MaxFileSizeBytes),
                        Message = "Max file size must be positive",
                        Severity = ValidationSeverity.Error
                    });
                }
                else if (config.MaxFileSizeBytes > 100_000_000) // 100MB
                {
                    validationResults.Add(new ValidationIssue
                    {
                        Property = nameof(config.MaxFileSizeBytes),
                        Message = "Max file size exceeds 100MB - performance may be impacted",
                        Severity = ValidationSeverity.Warning
                    });
                }

                return new ValidationResult
                {
                    IsValid = !validationResults.Any(v => v.Severity == ValidationSeverity.Error),
                    Issues = validationResults,
                    ValidatedAt = DateTime.UtcNow
                };

            }, "ValidateFileConfig", _logger, new ValidationResult { IsValid = false });
        }

        /// <summary>
        /// Example: Comprehensive web configuration validation
        /// </summary>
        public async Task<ValidationResult> ValidateWebConfigAsync(WebConfig config)
        {
            return await StandardErrorHandler.HandleAsync(async () =>
            {
                var validationResults = new List<ValidationIssue>();

                // Port validation
                if (config.Port <= 0 || config.Port > 65535)
                {
                    validationResults.Add(new ValidationIssue
                    {
                        Property = nameof(config.Port),
                        Message = "Port must be between 1 and 65535",
                        Severity = ValidationSeverity.Error
                    });
                }
                else if (config.Port < 1024 && !IsRunningAsAdministrator())
                {
                    validationResults.Add(new ValidationIssue
                    {
                        Property = nameof(config.Port),
                        Message = "Ports below 1024 require administrator privileges",
                        Severity = ValidationSeverity.Warning
                    });
                }

                // CORS origins validation
                if (config.CorsOrigins?.Any() == true)
                {
                    foreach (var origin in config.CorsOrigins)
                    {
                        if (origin != "*" && !Uri.TryCreate(origin, UriKind.Absolute, out _))
                        {
                            validationResults.Add(new ValidationIssue
                            {
                                Property = nameof(config.CorsOrigins),
                                Message = $"Invalid CORS origin: {origin}",
                                Severity = ValidationSeverity.Error
                            });
                        }
                    }

                    if (config.CorsOrigins.Contains("*") && config.CorsOrigins.Count > 1)
                    {
                        validationResults.Add(new ValidationIssue
                        {
                            Property = nameof(config.CorsOrigins),
                            Message = "Wildcard CORS origin should not be combined with specific origins",
                            Severity = ValidationSeverity.Warning
                        });
                    }
                }

                // HTTPS configuration
                if (config.UseHttps)
                {
                    if (string.IsNullOrWhiteSpace(config.CertificatePath))
                    {
                        validationResults.Add(new ValidationIssue
                        {
                            Property = nameof(config.CertificatePath),
                            Message = "Certificate path is required for HTTPS",
                            Severity = ValidationSeverity.Error
                        });
                    }
                    else
                    {
                        var certValidation = await _validationService.ValidateFilePathAsync(config.CertificatePath);
                        if (!certValidation.IsValid)
                        {
                            validationResults.Add(new ValidationIssue
                            {
                                Property = nameof(config.CertificatePath),
                                Message = $"Invalid certificate path: {certValidation.Error}",
                                Severity = ValidationSeverity.Error
                            });
                        }
                        else if (!File.Exists(config.CertificatePath))
                        {
                            validationResults.Add(new ValidationIssue
                            {
                                Property = nameof(config.CertificatePath),
                                Message = "Certificate file does not exist",
                                Severity = ValidationSeverity.Error
                            });
                        }
                    }
                }

                // Static files validation
                if (!string.IsNullOrWhiteSpace(config.StaticFilesPath))
                {
                    var pathValidation = await _validationService.ValidateFilePathAsync(config.StaticFilesPath);
                    if (!pathValidation.IsValid)
                    {
                        validationResults.Add(new ValidationIssue
                        {
                            Property = nameof(config.StaticFilesPath),
                            Message = $"Invalid static files path: {pathValidation.Error}",
                            Severity = ValidationSeverity.Error
                        });
                    }
                }

                return new ValidationResult
                {
                    IsValid = !validationResults.Any(v => v.Severity == ValidationSeverity.Error),
                    Issues = validationResults,
                    ValidatedAt = DateTime.UtcNow
                };

            }, "ValidateWebConfig", _logger, new ValidationResult { IsValid = false });
        }

        /// <summary>
        /// Example: Validate performance configuration with resource constraints
        /// </summary>
        public ValidationResult ValidatePerformanceConfig(PerformanceConfig config)
        {
            return StandardErrorHandler.Handle(() =>
            {
                var validationResults = new List<ValidationIssue>();

                // Thread pool configuration
                if (config.MaxConcurrency <= 0)
                {
                    validationResults.Add(new ValidationIssue
                    {
                        Property = nameof(config.MaxConcurrency),
                        Message = "Max concurrency must be positive",
                        Severity = ValidationSeverity.Error
                    });
                }
                else if (config.MaxConcurrency > Environment.ProcessorCount * 4)
                {
                    validationResults.Add(new ValidationIssue
                    {
                        Property = nameof(config.MaxConcurrency),
                        Message = $"Max concurrency ({config.MaxConcurrency}) exceeds recommended limit ({Environment.ProcessorCount * 4})",
                        Severity = ValidationSeverity.Warning
                    });
                }

                // Memory limits
                if (config.MaxMemoryMB <= 0)
                {
                    validationResults.Add(new ValidationIssue
                    {
                        Property = nameof(config.MaxMemoryMB),
                        Message = "Max memory limit must be positive",
                        Severity = ValidationSeverity.Error
                    });
                }

                // Cache configuration
                if (config.CacheEnabled && config.CacheSizeMB <= 0)
                {
                    validationResults.Add(new ValidationIssue
                    {
                        Property = nameof(config.CacheSizeMB),
                        Message = "Cache size must be positive when caching is enabled",
                        Severity = ValidationSeverity.Error
                    });
                }

                // Logging performance impact
                if (config.DetailedLogging && config.MaxConcurrency > 10)
                {
                    validationResults.Add(new ValidationIssue
                    {
                        Property = nameof(config.DetailedLogging),
                        Message = "Detailed logging may impact performance at high concurrency",
                        Severity = ValidationSeverity.Warning
                    });
                }

                return new ValidationResult
                {
                    IsValid = !validationResults.Any(v => v.Severity == ValidationSeverity.Error),
                    Issues = validationResults,
                    ValidatedAt = DateTime.UtcNow
                };

            }, "ValidatePerformanceConfig", _logger, new ValidationResult { IsValid = false });
        }

        private (bool IsValid, string Message) ValidateModelName(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName))
                return (false, "Model name cannot be empty");

            // Common model name patterns
            var validPatterns = new[]
            {
                "openai/", "anthropic/", "google/", "meta-llama/", "mistralai/",
                "cohere/", "huggingface/", "microsoft/"
            };

            if (!validPatterns.Any(pattern => modelName.StartsWith(pattern, StringComparison.OrdinalIgnoreCase)))
            {
                return (false, $"Model name '{modelName}' doesn't match expected provider/model format");
            }

            if (modelName.Length > 100)
            {
                return (false, "Model name is too long");
            }

            return (true, "Valid model name");
        }

        private bool IsRunningAsAdministrator()
        {
            try
            {
                var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false; // Assume not admin if check fails
            }
        }
    }

    // Configuration model examples
    public class OpenRouterConfig
    {
        public string ApiKey { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";
        public int TimeoutSeconds { get; set; } = 30;
        public string? DefaultModel { get; set; }
        public int? MaxRequestsPerMinute { get; set; }
    }

    public class AgentConfig
    {
        public string Name { get; set; } = string.Empty;
        public string? SystemPrompt { get; set; }
        public double Temperature { get; set; } = 0.7;
        public int MaxTokens { get; set; } = 4000;
        public List<string>? EnabledTools { get; set; }
        public int? MaxParallelTools { get; set; }
    }

    public class FileConfig
    {
        public string? WorkingDirectory { get; set; }
        public List<string>? AllowedPaths { get; set; }
        public List<string>? BlockedExtensions { get; set; }
        public long MaxFileSizeBytes { get; set; } = 10_000_000; // 10MB
    }

    public class WebConfig
    {
        public int Port { get; set; } = 8080;
        public List<string>? CorsOrigins { get; set; }
        public bool UseHttps { get; set; } = false;
        public string? CertificatePath { get; set; }
        public string? StaticFilesPath { get; set; }
    }

    public class PerformanceConfig
    {
        public int MaxConcurrency { get; set; } = Environment.ProcessorCount * 2;
        public int MaxMemoryMB { get; set; } = 1000;
        public bool CacheEnabled { get; set; } = true;
        public int CacheSizeMB { get; set; } = 100;
        public bool DetailedLogging { get; set; } = false;
    }

    // Validation result models
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<ValidationIssue> Issues { get; set; } = new();
        public DateTime ValidatedAt { get; set; }
    }

    public class ValidationIssue
    {
        public string Property { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public ValidationSeverity Severity { get; set; }
    }

    public enum ValidationSeverity
    {
        Info,
        Warning,
        Error
    }
}
