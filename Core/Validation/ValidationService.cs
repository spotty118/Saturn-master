using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Saturn.Core.Validation
{
    /// <summary>
    /// Concrete implementation of validation service with comprehensive security checks.
    /// </summary>
    public class ValidationService : IValidationService
    {
        private readonly ILogger<ValidationService> _logger;
        private readonly IInputSanitizer _inputSanitizer;
        private readonly IFilePathValidator _filePathValidator;

        public ValidationService(
            ILogger<ValidationService> logger,
            IInputSanitizer inputSanitizer,
            IFilePathValidator filePathValidator)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _inputSanitizer = inputSanitizer ?? throw new ArgumentNullException(nameof(inputSanitizer));
            _filePathValidator = filePathValidator ?? throw new ArgumentNullException(nameof(filePathValidator));
        }

        public ValidationResult ValidateObject(object obj)
        {
            if (obj == null)
                return ValidationResult.Failure("Object cannot be null");

            var context = new ValidationContext(obj);
            var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
            
            var isValid = Validator.TryValidateObject(obj, context, results, validateAllProperties: true);

            if (isValid)
                return ValidationResult.Success();

            var errors = results.Select(r => new ValidationError
            {
                Message = r.ErrorMessage ?? "Validation failed",
                Field = r.MemberNames?.FirstOrDefault()
            });

            return ValidationResult.Failure(errors);
        }

        public async Task<ValidationResult> ValidateObjectAsync(object obj)
        {
            // For now, delegate to synchronous version
            // Future enhancement: Support for async validation attributes
            return await Task.FromResult(ValidateObject(obj));
        }

        public ValidationResult ValidateFilePath(string filePath, bool mustExist = false, bool allowCreate = true)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return ValidationResult.Failure("File path cannot be empty");

            var options = new PathValidationOptions
            {
                MustExist = mustExist,
                AllowCreate = allowCreate,
                RequireWorkspaceBoundary = true
            };

            return _filePathValidator.ValidatePath(filePath, options);
        }

        public ValidationResult ValidateApiKey(string apiKey, string providerName)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                return ValidationResult.Failure("API key cannot be empty");

            if (string.IsNullOrWhiteSpace(providerName))
                return ValidationResult.Failure("Provider name cannot be empty");

            // Use the custom validation attribute
            var attribute = new ApiKeyValidationAttribute(providerName);
            var context = new ValidationContext(new { ApiKey = apiKey });
            var result = attribute.GetValidationResult(apiKey, context);

            return result == System.ComponentModel.DataAnnotations.ValidationResult.Success 
                ? ValidationResult.Success() 
                : ValidationResult.Failure(result?.ErrorMessage ?? "API key validation failed");
        }

        public ValidationResult ValidateConfiguration(object configuration, string sectionName)
        {
            if (configuration == null)
                return ValidationResult.Failure($"Configuration section '{sectionName}' cannot be null");

            var validationResult = ValidateObject(configuration);
            
            if (!validationResult.IsValid)
            {
                _logger.LogWarning("Configuration validation failed for section '{SectionName}': {Errors}", 
                    sectionName, string.Join(", ", validationResult.Errors.Select(e => e.Message)));
            }

            return validationResult;
        }

        public ValidationResult ValidateUserInput(string input, InputType inputType)
        {
            if (string.IsNullOrEmpty(input))
                return ValidationResult.Success(); // Allow empty input, let required validation handle it

            var result = new ValidationResult { IsValid = true };

            // Check for common injection patterns
            if (ContainsInjectionPatterns(input))
            {
                result.AddError("Input contains potentially dangerous content");
                _logger.LogWarning("Potentially dangerous input detected: {InputType}", inputType);
            }

            // Type-specific validation
            switch (inputType)
            {
                case InputType.Code:
                    ValidateCodeInput(input, result);
                    break;
                case InputType.FilePath:
                    var pathResult = ValidateFilePath(input);
                    if (!pathResult.IsValid)
                    {
                        result.IsValid = false;
                        result.Errors.AddRange(pathResult.Errors);
                    }
                    break;
                case InputType.Url:
                    var urlResult = ValidateUrl(input);
                    if (!urlResult.IsValid)
                    {
                        result.IsValid = false;
                        result.Errors.AddRange(urlResult.Errors);
                    }
                    break;
                case InputType.Email:
                    if (!IsValidEmail(input))
                        result.AddError("Invalid email format");
                    break;
                case InputType.Json:
                    if (!IsValidJson(input))
                        result.AddError("Invalid JSON format");
                    break;
            }

            return result;
        }

        public ValidationResult ValidateUrl(string url, bool requireHttps = false)
        {
            if (string.IsNullOrWhiteSpace(url))
                return ValidationResult.Failure("URL cannot be empty");

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return ValidationResult.Failure("Invalid URL format");

            if (requireHttps && uri.Scheme != "https")
                return ValidationResult.Failure("HTTPS is required");

            if (uri.Scheme != "http" && uri.Scheme != "https")
                return ValidationResult.Failure("Only HTTP and HTTPS URLs are allowed");

            return ValidationResult.Success();
        }

        public ValidationResult ValidateAgentName(string agentName)
        {
            if (string.IsNullOrWhiteSpace(agentName))
                return ValidationResult.Failure("Agent name cannot be empty");

            // Agent name validation rules
            if (agentName.Length > 50)
                return ValidationResult.Failure("Agent name cannot exceed 50 characters");

            if (!Regex.IsMatch(agentName, @"^[a-zA-Z][a-zA-Z0-9_-]*$"))
                return ValidationResult.Failure("Agent name must start with a letter and contain only letters, numbers, underscores, or hyphens");

            return ValidationResult.Success();
        }

        public ValidationResult ValidateDiffContent(string diffContent)
        {
            if (string.IsNullOrWhiteSpace(diffContent))
                return ValidationResult.Failure("Diff content cannot be empty");

            var result = new ValidationResult { IsValid = true };

            // Check for potentially dangerous patterns in diff content
            if (ContainsDangerousFileOperations(diffContent))
            {
                result.AddWarning("Diff contains potentially dangerous file operations");
            }

            // Validate diff format
            if (!IsValidDiffFormat(diffContent))
            {
                result.AddError("Invalid diff format");
            }

            return result;
        }

        private bool ContainsInjectionPatterns(string input)
        {
            var patterns = new[]
            {
                @"<script\b[^<]*(?:(?!<\/script>)<[^<]*)*<\/script>", // Script tags
                @"javascript:", // JavaScript protocol
                @"data:.*base64", // Data URLs with base64
                @"vbscript:", // VBScript protocol
                @"on\w+\s*=", // Event handlers
                @"eval\s*\(", // eval() calls
                @"exec\s*\(", // exec() calls
                @"system\s*\(", // system() calls
                @"(--|#|\/\*|\*\/)", // SQL comment patterns
                @"(union|select|insert|update|delete|drop|create|alter)\s+", // SQL keywords
            };

            return patterns.Any(pattern => Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase));
        }

        private void ValidateCodeInput(string code, ValidationResult result)
        {
            // Check for potentially dangerous code patterns
            var dangerousPatterns = new[]
            {
                @"System\.Diagnostics\.Process",
                @"Process\.Start",
                @"File\.Delete",
                @"Directory\.Delete",
                @"Registry\.",
                @"Environment\.Exit",
                @"Assembly\.Load",
                @"Type\.GetType",
                @"Activator\.CreateInstance"
            };

            foreach (var pattern in dangerousPatterns)
            {
                if (Regex.IsMatch(code, pattern, RegexOptions.IgnoreCase))
                {
                    result.AddWarning($"Code contains potentially dangerous pattern: {pattern}");
                }
            }
        }

        private bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        private bool IsValidJson(string json)
        {
            try
            {
                System.Text.Json.JsonDocument.Parse(json);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool ContainsDangerousFileOperations(string diffContent)
        {
            var dangerousPatterns = new[]
            {
                @"rm\s+-rf",
                @"del\s+/[sq]",
                @"format\s+[a-z]:",
                @"\.\.[\\/]",
                @"[\\\/]etc[\\\/]",
                @"[\\\/]windows[\\\/]system32"
            };

            return dangerousPatterns.Any(pattern => Regex.IsMatch(diffContent, pattern, RegexOptions.IgnoreCase));
        }

        private bool IsValidDiffFormat(string diffContent)
        {
            // More robust diff format validation
            var lines = diffContent.Split('\n');
            if (lines.Length < 2) return false;

            var firstLine = lines[0];
            if (firstLine.StartsWith("--- ") || firstLine.StartsWith("diff --git"))
            {
                return true;
            }

            var secondLine = lines[1];
            if (secondLine.StartsWith("+++ "))
            {
                return true;
            }

            return lines.Any(line => line.StartsWith("@@ "));
        }
    }

    /// <summary>
    /// Input sanitization service implementation.
    /// </summary>
    public class InputSanitizer : IInputSanitizer
    {
        private readonly ILogger<InputSanitizer> _logger;

        public InputSanitizer(ILogger<InputSanitizer> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string SanitizeFilePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return string.Empty;

            // Remove dangerous patterns
            var sanitized = filePath
                .Replace("..", "")
                .Replace("~", "")
                .Replace("\\\\", "\\")
                .Replace("//", "/");

            // Normalize path separators
            sanitized = sanitized.Replace('\\', Path.DirectorySeparatorChar)
                                .Replace('/', Path.DirectorySeparatorChar);

            // Remove any null bytes
            sanitized = sanitized.Replace("\0", "");

            return sanitized.Trim();
        }

        public string SanitizeUserInput(string input, InputType inputType)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            return inputType switch
            {
                InputType.Html => SanitizeHtml(input),
                InputType.FilePath => SanitizeFilePath(input),
                InputType.Command => SanitizeCommandArgument(input),
                InputType.PlainText => SanitizePlainText(input),
                _ => SanitizePlainText(input)
            };
        }

        public string SanitizeHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return string.Empty;

            // Basic HTML encoding
            return HttpUtility.HtmlEncode(html);
        }

        public string SanitizeSql(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
                return string.Empty;

            // Basic SQL injection prevention
            return sql.Replace("'", "''")
                     .Replace(";", "")
                     .Replace("--", "")
                     .Replace("/*", "")
                     .Replace("*/", "")
                     .Replace("xp_", "")
                     .Replace("sp_", "");
        }

        public string SanitizeCommandArgument(string argument)
        {
            if (string.IsNullOrWhiteSpace(argument))
                return string.Empty;

            // Remove potentially dangerous command characters
            var dangerous = new[] { "|", "&", ";", "$", "`", "\\", "\"", "'", "<", ">", "(", ")", "{", "}" };
            
            var sanitized = argument;
            foreach (var chr in dangerous)
            {
                sanitized = sanitized.Replace(chr, "");
            }

            return sanitized.Trim();
        }

        public string SanitizeConfigurationValue(string value, string key)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            // Key-specific sanitization
            if (key.ToLowerInvariant().Contains("password") || key.ToLowerInvariant().Contains("key"))
            {
                // Don't log sensitive values
                _logger.LogDebug("Sanitizing sensitive configuration value for key: {Key}", key);
                return value.Trim();
            }

            return SanitizePlainText(value);
        }

        private string SanitizePlainText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            // Remove null bytes and control characters except common whitespace
            var sanitized = Regex.Replace(text, @"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", "");
            
            return sanitized.Trim();
        }
    }

    /// <summary>
    /// File path validation service implementation.
    /// </summary>
    public class FilePathValidator : IFilePathValidator
    {
        private readonly ILogger<FilePathValidator> _logger;

        public FilePathValidator(ILogger<FilePathValidator> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public ValidationResult ValidatePath(string path, PathValidationOptions? options = null)
        {
            options ??= new PathValidationOptions();
            var result = new ValidationResult { IsValid = true };

            if (string.IsNullOrWhiteSpace(path))
            {
                result.AddError("Path cannot be empty");
                return result;
            }

            // Check for dangerous patterns
            if (ContainsDangerousPatterns(path))
            {
                result.AddError("Path contains dangerous patterns");
            }

            // Normalize and validate
            var normalizedPath = NormalizePath(path);
            
            // Check workspace boundary
            if (options.RequireWorkspaceBoundary)
            {
                var workspacePath = Directory.GetCurrentDirectory();
                if (!IsPathInWorkspace(normalizedPath, workspacePath))
                {
                    result.AddError("Path must be within the workspace directory");
                }
            }

            // Check file existence
            if (options.MustExist && !File.Exists(normalizedPath) && !Directory.Exists(normalizedPath))
            {
                result.AddError("Path does not exist");
            }

            // Check allowed extensions
            if (options.AllowedExtensions?.Any() == true)
            {
                if (!IsAllowedExtension(normalizedPath, options.AllowedExtensions))
                {
                    result.AddError($"File extension not allowed. Allowed: {string.Join(", ", options.AllowedExtensions)}");
                }
            }

            // Check prohibited patterns
            if (options.ProhibitedPatterns?.Any() == true)
            {
                foreach (var pattern in options.ProhibitedPatterns)
                {
                    if (Regex.IsMatch(normalizedPath, pattern, RegexOptions.IgnoreCase))
                    {
                        result.AddError($"Path matches prohibited pattern: {pattern}");
                    }
                }
            }

            // Check file size if it exists
            if (options.MaxFileSize.HasValue && File.Exists(normalizedPath))
            {
                var fileInfo = new FileInfo(normalizedPath);
                if (fileInfo.Length > options.MaxFileSize.Value)
                {
                    result.AddError($"File size ({fileInfo.Length} bytes) exceeds maximum allowed size ({options.MaxFileSize.Value} bytes)");
                }
            }

            return result;
        }

        public bool IsPathInWorkspace(string path, string workspacePath)
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                var fullWorkspacePath = Path.GetFullPath(workspacePath);
                
                return fullPath.StartsWith(fullWorkspacePath, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to validate path workspace boundary: {Path}", path);
                return false;
            }
        }

        public bool IsAllowedExtension(string filePath, IEnumerable<string> allowedExtensions)
        {
            var extension = Path.GetExtension(filePath);
            return allowedExtensions.Any(ext => 
                string.Equals(ext, extension, StringComparison.OrdinalIgnoreCase));
        }

        public bool ContainsDangerousPatterns(string path)
        {
            var dangerousPatterns = new[]
            {
                @"\.\.",
                @"~",
                @"[\x00-\x1f]",
                @"[<>:""|?*]",
                @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)",
                @"(^|[\\/])\.",
                @"[\\/]{2,}"
            };

            return dangerousPatterns.Any(pattern => 
                Regex.IsMatch(path, pattern, RegexOptions.IgnoreCase));
        }

        public string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            try
            {
                // Convert to full path and normalize separators
                var normalized = Path.GetFullPath(path);
                return normalized;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to normalize path: {Path}", path);
                return path;
            }
        }
    }
}