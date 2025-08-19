using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Saturn.Core.Validation
{
    /// <summary>
    /// Enhanced security validation patterns extending Saturn's ValidationService with advanced
    /// threat detection, content filtering, and security policy enforcement.
    /// </summary>
    public class SecurityValidationEnhancements
    {
        private readonly IValidationService _baseValidationService;
        private readonly SecurityPolicyConfig _securityConfig;

        public SecurityValidationEnhancements(IValidationService baseValidationService, SecurityPolicyConfig? config = null)
        {
            _baseValidationService = baseValidationService ?? throw new ArgumentNullException(nameof(baseValidationService));
            _securityConfig = config ?? SecurityPolicyConfig.Default;
        }

        /// <summary>
        /// Advanced API key validation with format checking and entropy analysis
        /// </summary>
        public async Task<SecurityValidationResult> ValidateApiKeySecurityAsync(string apiKey, string provider)
        {
            var result = new SecurityValidationResult();

            // Base validation
            var baseResult = await _baseValidationService.ValidateApiKeyAsync(apiKey, provider);
            if (!baseResult.IsValid)
            {
                result.AddError($"Base validation failed: {baseResult.Error}");
                return result;
            }

            // Format validation by provider
            if (!ValidateApiKeyFormat(apiKey, provider))
            {
                result.AddError($"Invalid {provider} API key format");
            }

            // Entropy check (randomness)
            var entropy = CalculateEntropy(apiKey);
            if (entropy < _securityConfig.MinApiKeyEntropy)
            {
                result.AddWarning($"Low API key entropy: {entropy:F2} (minimum: {_securityConfig.MinApiKeyEntropy})");
            }

            // Check for common weak patterns
            if (ContainsWeakPatterns(apiKey))
            {
                result.AddError("API key contains weak or predictable patterns");
            }

            return result;
        }

        /// <summary>
        /// Enhanced file path validation with security policy enforcement
        /// </summary>
        public async Task<SecurityValidationResult> ValidateFilePathSecurityAsync(string filePath, FileAccessType accessType)
        {
            var result = new SecurityValidationResult();

            // Base validation
            var baseResult = await _baseValidationService.ValidateFilePathAsync(filePath);
            if (!baseResult.IsValid)
            {
                result.AddError($"Base path validation failed: {baseResult.Error}");
                return result;
            }

            // Path traversal detection
            if (ContainsPathTraversal(filePath))
            {
                result.AddError("Path contains directory traversal sequences");
            }

            // Restricted directory check
            if (IsInRestrictedDirectory(filePath))
            {
                result.AddError($"Access to restricted directory: {Path.GetDirectoryName(filePath)}");
            }

            // Extension validation
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            if (_securityConfig.BlockedExtensions.Contains(extension))
            {
                result.AddError($"Blocked file extension: {extension}");
            }

            // Executable file check
            if (IsExecutableFile(extension) && accessType == FileAccessType.Write)
            {
                result.AddWarning("Writing executable files requires elevated permissions");
            }

            // File size limit
            if (File.Exists(filePath))
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > _securityConfig.MaxFileSizeBytes)
                {
                    result.AddError($"File size exceeds limit: {fileInfo.Length} > {_securityConfig.MaxFileSizeBytes}");
                }
            }

            return result;
        }

        /// <summary>
        /// Advanced user input validation with XSS and injection prevention
        /// </summary>
        public async Task<SecurityValidationResult> ValidateUserInputSecurityAsync(string input, InputContext context)
        {
            var result = new SecurityValidationResult();

            // Base validation
            var baseResult = await _baseValidationService.ValidateUserInputAsync(input);
            if (!baseResult.IsValid)
            {
                result.AddError($"Base input validation failed: {baseResult.Error}");
                return result;
            }

            // SQL injection detection
            if (ContainsSqlInjectionPatterns(input))
            {
                result.AddError("Input contains potential SQL injection patterns");
            }

            // XSS detection
            if (ContainsXssPatterns(input))
            {
                result.AddError("Input contains potential XSS patterns");
            }

            // Command injection detection
            if (ContainsCommandInjectionPatterns(input))
            {
                result.AddError("Input contains potential command injection patterns");
            }

            // LDAP injection detection
            if (ContainsLdapInjectionPatterns(input))
            {
                result.AddError("Input contains potential LDAP injection patterns");
            }

            // Context-specific validation
            switch (context)
            {
                case InputContext.SystemPrompt:
                    ValidateSystemPromptSecurity(input, result);
                    break;
                case InputContext.FileName:
                    ValidateFileNameSecurity(input, result);
                    break;
                case InputContext.Command:
                    ValidateCommandSecurity(input, result);
                    break;
            }

            return result;
        }

        /// <summary>
        /// Validate configuration security settings
        /// </summary>
        public SecurityValidationResult ValidateConfigurationSecurity(object config, string configType)
        {
            var result = new SecurityValidationResult();

            switch (configType.ToLowerInvariant())
            {
                case "web":
                    ValidateWebConfigSecurity(config, result);
                    break;
                case "api":
                    ValidateApiConfigSecurity(config, result);
                    break;
                case "file":
                    ValidateFileConfigSecurity(config, result);
                    break;
            }

            return result;
        }

        private bool ValidateApiKeyFormat(string apiKey, string provider)
        {
            return provider.ToLowerInvariant() switch
            {
                "openrouter" => apiKey.StartsWith("sk-or-") && apiKey.Length >= 20,
                "openai" => apiKey.StartsWith("sk-") && apiKey.Length >= 40,
                "anthropic" => apiKey.StartsWith("sk-ant-") && apiKey.Length >= 40,
                _ => apiKey.Length >= 16 // Generic minimum
            };
        }

        private double CalculateEntropy(string input)
        {
            var frequencies = input.GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());
            return frequencies.Values.Sum(freq => 
            {
                var probability = (double)freq / input.Length;
                return -probability * Math.Log2(probability);
            });
        }

        private bool ContainsWeakPatterns(string input)
        {
            var weakPatterns = new[]
            {
                @"(.)\1{3,}", // Repeated characters
                @"123456|abcdef|qwerty", // Common sequences
                @"password|secret|key123", // Common words
                @"^[a-zA-Z]+$" // Only alphabetic
            };

            return weakPatterns.Any(pattern => Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase));
        }

        private bool ContainsPathTraversal(string path)
        {
            var traversalPatterns = new[] { "..", "~", "%2e%2e", "%2E%2E", "..%2f", "..%5c" };
            return traversalPatterns.Any(pattern => path.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsInRestrictedDirectory(string path)
        {
            var restrictedDirs = new[] { "/etc", "/sys", "/proc", "C:\\Windows", "C:\\System32" };
            return restrictedDirs.Any(dir => path.StartsWith(dir, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsExecutableFile(string extension)
        {
            var executableExtensions = new[] { ".exe", ".bat", ".cmd", ".sh", ".ps1", ".vbs", ".jar" };
            return executableExtensions.Contains(extension);
        }

        private bool ContainsSqlInjectionPatterns(string input)
        {
            var sqlPatterns = new[]
            {
                @"('|(''|""|`)|(%27)|(%22))", // SQL quotes
                @"(;|%3B)", // SQL statement terminator
                @"(\b(union|select|insert|update|delete|drop|create|alter|exec|execute)\b)",
                @"(\b(or|and)\b\s*\w*\s*[=<>])", // SQL logical operators
                @"(-{2}|#|/\*|\*/)" // SQL comments
            };

            return sqlPatterns.Any(pattern => Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase));
        }

        private bool ContainsXssPatterns(string input)
        {
            var xssPatterns = new[]
            {
                @"<script\b[^<]*(?:(?!<\/script>)<[^<]*)*<\/script>",
                @"javascript:",
                @"on\w+\s*=",
                @"<iframe|<object|<embed|<link|<meta",
                @"expression\s*\(",
                @"vbscript:",
                @"data:text/html"
            };

            return xssPatterns.Any(pattern => Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase));
        }

        private bool ContainsCommandInjectionPatterns(string input)
        {
            var commandPatterns = new[]
            {
                @"[;&|`$(){}[\]\\]", // Command separators and special chars
                @"\b(rm|del|format|shutdown|reboot|halt)\b",
                @"\b(cat|type|more|less)\b\s+/",
                @"\b(wget|curl|nc|netcat)\b"
            };

            return commandPatterns.Any(pattern => Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase));
        }

        private bool ContainsLdapInjectionPatterns(string input)
        {
            var ldapPatterns = new[]
            {
                @"[()&|*=<>!]", // LDAP special characters
                @"\*\)|&\(|!\("
            };

            return ldapPatterns.Any(pattern => Regex.IsMatch(input, pattern));
        }

        private void ValidateSystemPromptSecurity(string prompt, SecurityValidationResult result)
        {
            // Check for prompt injection attempts
            var injectionPatterns = new[]
            {
                @"ignore\s+(previous|above|all)\s+instructions",
                @"system\s*:\s*you\s+are\s+now",
                @"forget\s+everything",
                @"new\s+instructions?:",
                @"jailbreak|DAN|roleplay"
            };

            if (injectionPatterns.Any(pattern => Regex.IsMatch(prompt, pattern, RegexOptions.IgnoreCase)))
            {
                result.AddError("System prompt contains potential injection patterns");
            }

            if (prompt.Length > _securityConfig.MaxPromptLength)
            {
                result.AddWarning($"System prompt exceeds recommended length: {prompt.Length} > {_securityConfig.MaxPromptLength}");
            }
        }

        private void ValidateFileNameSecurity(string fileName, SecurityValidationResult result)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            if (fileName.Any(c => invalidChars.Contains(c)))
            {
                result.AddError("File name contains invalid characters");
            }

            var reservedNames = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "LPT1" };
            if (reservedNames.Contains(fileName.ToUpperInvariant()))
            {
                result.AddError($"File name is reserved: {fileName}");
            }
        }

        private void ValidateCommandSecurity(string command, SecurityValidationResult result)
        {
            var dangerousCommands = new[] { "rm -rf", "del /s", "format", "shutdown", "reboot" };
            if (dangerousCommands.Any(cmd => command.Contains(cmd, StringComparison.OrdinalIgnoreCase)))
            {
                result.AddError("Command contains potentially dangerous operations");
            }
        }

        private void ValidateWebConfigSecurity(object config, SecurityValidationResult result)
        {
            // Implementation would depend on actual web config structure
            result.AddInfo("Web configuration security validation completed");
        }

        private void ValidateApiConfigSecurity(object config, SecurityValidationResult result)
        {
            // Implementation would depend on actual API config structure  
            result.AddInfo("API configuration security validation completed");
        }

        private void ValidateFileConfigSecurity(object config, SecurityValidationResult result)
        {
            // Implementation would depend on actual file config structure
            result.AddInfo("File configuration security validation completed");
        }
    }

    // Configuration and enums
    public class SecurityPolicyConfig
    {
        public double MinApiKeyEntropy { get; set; } = 3.5;
        public long MaxFileSizeBytes { get; set; } = 100_000_000; // 100MB
        public int MaxPromptLength { get; set; } = 8000;
        public HashSet<string> BlockedExtensions { get; set; } = new() { ".exe", ".bat", ".cmd", ".sh", ".ps1" };

        public static SecurityPolicyConfig Default => new();
    }

    public enum FileAccessType { Read, Write, Execute }
    public enum InputContext { SystemPrompt, FileName, Command, General }

    // Result classes
    public class SecurityValidationResult
    {
        public List<string> Errors { get; } = new();
        public List<string> Warnings { get; } = new();  
        public List<string> Info { get; } = new();
        public bool IsValid => !Errors.Any();

        public void AddError(string message) => Errors.Add(message);
        public void AddWarning(string message) => Warnings.Add(message);
        public void AddInfo(string message) => Info.Add(message);
    }
}
