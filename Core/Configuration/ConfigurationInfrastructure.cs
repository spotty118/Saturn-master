using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Saturn.Core.Configuration
{
    /// <summary>
    /// Basic configuration validator using DataAnnotations with simple helpers.
    /// </summary>
    public class SimpleConfigurationValidator : IConfigurationValidator
    {
        public Task<ConfigurationValidationResult> ValidateAsync<T>(T configuration) where T : class
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            var context = new ValidationContext(configuration);
            var results = new List<ValidationResult>();
            var isValid = Validator.TryValidateObject(configuration, context, results, validateAllProperties: true);

            var result = new ConfigurationValidationResult
            {
                IsValid = isValid,
                Errors = results.Select(r => r.ErrorMessage ?? "Validation error").ToList()
            };

            return Task.FromResult(result);
        }

        public Task<ApiKeyValidationResult> ValidateApiKeyAsync(string apiKey, string provider)
        {
            var res = new ApiKeyValidationResult
            {
                Provider = provider,
                IsValid = !string.IsNullOrWhiteSpace(apiKey),
                HasPermissions = false
            };

            // Lightweight format hints; real connectivity checks can be added later
            if (!res.IsValid)
            {
                res.ErrorMessage = "API key is empty";
            }
            else
            {
                // Basic pattern checks
                if (provider.Equals("openrouter", StringComparison.OrdinalIgnoreCase))
                {
                    // OpenRouter keys often start with sk-or-
                    if (!apiKey.StartsWith("sk-or-", StringComparison.OrdinalIgnoreCase))
                    {
                        res.ErrorMessage = "API key format appears invalid for OpenRouter (expected prefix 'sk-or-').";
                    }
                    else
                    {
                        res.IsValid = true;
                        res.ErrorMessage = null;
                    }
                }
            }

            return Task.FromResult(res);
        }

        public Task<FilePathValidationResult> ValidateFilePathAsync(string filePath, bool requireWrite = false)
        {
            var result = new FilePathValidationResult();

            if (string.IsNullOrWhiteSpace(filePath))
            {
                result.IsValid = false;
                result.ErrorMessage = "Path is empty";
                return Task.FromResult(result);
            }

            try
            {
                var full = Path.GetFullPath(filePath);
                result.Exists = File.Exists(full) || Directory.Exists(full);

                // Check read
                try
                {
                    if (File.Exists(full))
                    {
                        using var _ = File.Open(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        result.CanRead = true;
                    }
                    else
                    {
                        result.CanRead = Directory.Exists(full);
                    }
                }
                catch
                {
                    result.CanRead = false;
                }

                // Check write
                if (requireWrite)
                {
                    try
                    {
                        var targetDir = Directory.Exists(full) ? full : Path.GetDirectoryName(full) ?? ".";
                        var testPath = Path.Combine(targetDir, $".saturn_write_test_{Guid.NewGuid():N}.tmp");
                        File.WriteAllText(testPath, "test");
                        File.Delete(testPath);
                        result.CanWrite = true;
                    }
                    catch
                    {
                        result.CanWrite = false;
                    }
                }
                else
                {
                    result.CanWrite = true; // not required
                }

                result.IsValid = result.CanRead && (!requireWrite || result.CanWrite);
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.ErrorMessage = $"Path validation failed: {ex.Message}";
            }

            return Task.FromResult(result);
        }
    }

    /// <summary>
    /// In-memory configuration change notifier with simple subscription management.
    /// </summary>
    public class ConfigurationChangeNotifier : IConfigurationChangeNotifier
    {
        private readonly object _lock = new();
        // section -> list of handlers as object-based wrappers
        private readonly Dictionary<string, List<(Type Type, Func<object, object, Task> Handler, Delegate Original)>> _handlers = new(StringComparer.OrdinalIgnoreCase);

        public Task NotifyConfigurationChangedAsync(string section, object oldValue, object newValue)
        {
            List<(Type Type, Func<object, object, Task> Handler, Delegate Original)>? handlers;

            lock (_lock)
            {
                _handlers.TryGetValue(section, out handlers);
            }

            if (handlers == null || handlers.Count == 0)
            {
                return Task.CompletedTask;
            }

            return InvokeAll(handlers, oldValue, newValue);
        }

        public void Subscribe<T>(string section, Func<T, T, Task> handler) where T : class
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            var wrapper = new Func<object, object, Task>((oldObj, newObj) =>
            {
                return handler((T)oldObj, (T)newObj);
            });

            lock (_lock)
            {
                if (!_handlers.TryGetValue(section, out var list))
                {
                    list = new List<(Type, Func<object, object, Task>, Delegate)>();
                    _handlers[section] = list;
                }

                list.Add((typeof(T), wrapper, handler));
            }
        }

        public void Unsubscribe(string section, Delegate handler)
        {
            lock (_lock)
            {
                if (_handlers.TryGetValue(section, out var list))
                {
                    list.RemoveAll(x => x.Original == handler);
                    if (list.Count == 0)
                    {
                        _handlers.Remove(section);
                    }
                }
            }
        }

        private static async Task InvokeAll(List<(Type Type, Func<object, object, Task> Handler, Delegate Original)> handlers, object oldValue, object newValue)
        {
            foreach (var h in handlers)
            {
                try
                {
                    await h.Handler(oldValue, newValue);
                }
                catch
                {
                    // Swallow to ensure other handlers still receive notification
                }
            }
        }
    }
}