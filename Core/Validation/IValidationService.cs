using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading.Tasks;

namespace Saturn.Core.Validation
{
    /// <summary>
    /// Comprehensive validation service for Saturn application.
    /// Provides input validation, sanitization, and security checks.
    /// </summary>
    public interface IValidationService
    {
        /// <summary>
        /// Validates an object using data annotations and custom validators.
        /// </summary>
        /// <param name="obj">Object to validate</param>
        /// <returns>Validation result with success status and any errors</returns>
        ValidationResult ValidateObject(object obj);

        /// <summary>
        /// Validates an object asynchronously with support for async validators.
        /// </summary>
        Task<ValidationResult> ValidateObjectAsync(object obj);

        /// <summary>
        /// Validates a file path for security and accessibility.
        /// </summary>
        ValidationResult ValidateFilePath(string filePath, bool mustExist = false, bool allowCreate = true);

        /// <summary>
        /// Validates API key format and structure.
        /// </summary>
        ValidationResult ValidateApiKey(string apiKey, string providerName);

        /// <summary>
        /// Validates configuration section for completeness and correctness.
        /// </summary>
        ValidationResult ValidateConfiguration(object configuration, string sectionName);

        /// <summary>
        /// Validates user input for potential security threats.
        /// </summary>
        ValidationResult ValidateUserInput(string input, InputType inputType);

        /// <summary>
        /// Validates URL format and accessibility.
        /// </summary>
        ValidationResult ValidateUrl(string url, bool requireHttps = false);

        /// <summary>
        /// Validates agent name for compliance with naming conventions.
        /// </summary>
        ValidationResult ValidateAgentName(string agentName);

        /// <summary>
        /// Validates diff content for safety and format.
        /// </summary>
        ValidationResult ValidateDiffContent(string diffContent);
    }

    /// <summary>
    /// Input sanitization service for security and data integrity.
    /// </summary>
    public interface IInputSanitizer
    {
        /// <summary>
        /// Sanitizes file path to prevent path traversal attacks.
        /// </summary>
        string SanitizeFilePath(string filePath);

        /// <summary>
        /// Sanitizes user input to remove potentially harmful content.
        /// </summary>
        string SanitizeUserInput(string input, InputType inputType);

        /// <summary>
        /// Sanitizes HTML content to prevent XSS attacks.
        /// </summary>
        string SanitizeHtml(string html);

        /// <summary>
        /// Sanitizes SQL input to prevent injection attacks.
        /// </summary>
        string SanitizeSql(string sql);

        /// <summary>
        /// Sanitizes command line arguments to prevent injection.
        /// </summary>
        string SanitizeCommandArgument(string argument);

        /// <summary>
        /// Sanitizes configuration values to ensure they are safe.
        /// </summary>
        string SanitizeConfigurationValue(string value, string key);
    }

    /// <summary>
    /// File path validation service with security checks.
    /// </summary>
    public interface IFilePathValidator
    {
        /// <summary>
        /// Validates that a file path is safe and within allowed directories.
        /// </summary>
        ValidationResult ValidatePath(string path, PathValidationOptions? options = null);

        /// <summary>
        /// Checks if a path is within the allowed workspace boundaries.
        /// </summary>
        bool IsPathInWorkspace(string path, string workspacePath);

        /// <summary>
        /// Validates file extension against allowed extensions.
        /// </summary>
        bool IsAllowedExtension(string filePath, IEnumerable<string> allowedExtensions);

        /// <summary>
        /// Checks if a path contains potentially dangerous patterns.
        /// </summary>
        bool ContainsDangerousPatterns(string path);

        /// <summary>
        /// Normalizes a file path to prevent inconsistencies.
        /// </summary>
        string NormalizePath(string path);
    }

    /// <summary>
    /// Types of input that require different validation approaches.
    /// </summary>
    public enum InputType
    {
        PlainText,
        Code,
        FilePath,
        Url,
        Email,
        Command,
        Configuration,
        Html,
        Json,
        Xml
    }

    /// <summary>
    /// Options for file path validation.
    /// </summary>
    public class PathValidationOptions
    {
        public bool MustExist { get; set; } = false;
        public bool AllowCreate { get; set; } = true;
        public bool RequireWorkspaceBoundary { get; set; } = true;
        public IEnumerable<string>? AllowedExtensions { get; set; }
        public IEnumerable<string>? ProhibitedPatterns { get; set; }
        public long? MaxFileSize { get; set; }
    }

    /// <summary>
    /// Result of a validation operation.
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<ValidationError> Errors { get; set; } = new();
        public List<ValidationWarning> Warnings { get; set; } = new();

        public static ValidationResult Success() => new() { IsValid = true };

        public static ValidationResult Failure(string error, string? field = null)
        {
            return new()
            {
                IsValid = false,
                Errors = { new ValidationError { Message = error, Field = field } }
            };
        }

        public static ValidationResult Failure(IEnumerable<ValidationError> errors)
        {
            return new()
            {
                IsValid = false,
                Errors = errors.ToList()
            };
        }

        public void AddError(string message, string? field = null)
        {
            IsValid = false;
            Errors.Add(new ValidationError { Message = message, Field = field });
        }

        public void AddWarning(string message, string? field = null)
        {
            Warnings.Add(new ValidationWarning { Message = message, Field = field });
        }
    }

    /// <summary>
    /// Represents a validation error.
    /// </summary>
    public class ValidationError
    {
        public string Message { get; set; } = string.Empty;
        public string? Field { get; set; }
        public string? Code { get; set; }
        public object? AttemptedValue { get; set; }
    }

    /// <summary>
    /// Represents a validation warning.
    /// </summary>
    public class ValidationWarning
    {
        public string Message { get; set; } = string.Empty;
        public string? Field { get; set; }
        public string? Code { get; set; }
    }

    /// <summary>
    /// Custom validation attributes for Saturn-specific validation.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class ApiKeyValidationAttribute : ValidationAttribute
    {
        public string ProviderName { get; }

        public ApiKeyValidationAttribute(string providerName)
        {
            ProviderName = providerName;
        }

        protected override System.ComponentModel.DataAnnotations.ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is not string apiKey)
                return new System.ComponentModel.DataAnnotations.ValidationResult("API key must be a string");

            if (string.IsNullOrWhiteSpace(apiKey))
                return new System.ComponentModel.DataAnnotations.ValidationResult("API key cannot be empty");

            // Provider-specific validation
            return ProviderName.ToLowerInvariant() switch
            {
                "openrouter" => ValidateOpenRouterKey(apiKey),
                "openai" => ValidateOpenAIKey(apiKey),
                "anthropic" => ValidateAnthropicKey(apiKey),
                _ => System.ComponentModel.DataAnnotations.ValidationResult.Success
            };
        }

        private static System.ComponentModel.DataAnnotations.ValidationResult ValidateOpenRouterKey(string key)
        {
            if (!key.StartsWith("sk-or-"))
                return new System.ComponentModel.DataAnnotations.ValidationResult("OpenRouter API key must start with 'sk-or-'");
            
            if (key.Length < 20)
                return new System.ComponentModel.DataAnnotations.ValidationResult("OpenRouter API key appears to be too short");

            return System.ComponentModel.DataAnnotations.ValidationResult.Success;
        }

        private static System.ComponentModel.DataAnnotations.ValidationResult ValidateOpenAIKey(string key)
        {
            if (!key.StartsWith("sk-"))
                return new System.ComponentModel.DataAnnotations.ValidationResult("OpenAI API key must start with 'sk-'");
            
            if (key.Length < 20)
                return new System.ComponentModel.DataAnnotations.ValidationResult("OpenAI API key appears to be too short");

            return System.ComponentModel.DataAnnotations.ValidationResult.Success;
        }

        private static System.ComponentModel.DataAnnotations.ValidationResult ValidateAnthropicKey(string key)
        {
            if (!key.StartsWith("sk-ant-"))
                return new System.ComponentModel.DataAnnotations.ValidationResult("Anthropic API key must start with 'sk-ant-'");
            
            if (key.Length < 20)
                return new System.ComponentModel.DataAnnotations.ValidationResult("Anthropic API key appears to be too short");

            return System.ComponentModel.DataAnnotations.ValidationResult.Success;
        }
    }

    /// <summary>
    /// Validation attribute for safe file paths.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class SafeFilePathAttribute : ValidationAttribute
    {
        public bool MustExist { get; set; } = false;
        public bool AllowCreate { get; set; } = true;

        protected override System.ComponentModel.DataAnnotations.ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is not string filePath)
                return System.ComponentModel.DataAnnotations.ValidationResult.Success; // Let RequiredAttribute handle null checks

            if (string.IsNullOrWhiteSpace(filePath))
                return System.ComponentModel.DataAnnotations.ValidationResult.Success; // Let RequiredAttribute handle empty checks

            // Basic path traversal check
            if (filePath.Contains("..") || filePath.Contains("~"))
                return new System.ComponentModel.DataAnnotations.ValidationResult("File path contains potentially dangerous patterns");

            // Check for absolute paths outside workspace
            if (Path.IsPathRooted(filePath))
            {
                var workspacePath = Directory.GetCurrentDirectory();
                var fullPath = Path.GetFullPath(filePath);
                if (!fullPath.StartsWith(workspacePath, StringComparison.OrdinalIgnoreCase))
                    return new System.ComponentModel.DataAnnotations.ValidationResult("File path must be within the workspace directory");
            }

            return System.ComponentModel.DataAnnotations.ValidationResult.Success;
        }
    }
}