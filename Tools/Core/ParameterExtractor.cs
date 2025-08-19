using System;
using System.Collections.Generic;
using System.Text.Json;
using Saturn.Core.Constants;

namespace Saturn.Tools.Core
{
    /// <summary>
    /// Utility class for extracting and validating parameters from tool calls
    /// </summary>
    public static class ParameterExtractor
    {
        /// <summary>
        /// Extracts a parameter with type conversion and validation
        /// </summary>
        public static T GetParameter<T>(Dictionary<string, object> parameters, string key, T defaultValue = default!)
        {
            if (parameters.TryGetValue(key, out var value))
            {
                if (value is T typedValue)
                {
                    return typedValue;
                }

                if (value is JsonElement jsonElement)
                {
                    try
                    {
                        return jsonElement.Deserialize<T>() ?? defaultValue;
                    }
                    catch
                    {
                        return defaultValue;
                    }
                }

                try
                {
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch
                {
                    return defaultValue;
                }
            }

            return defaultValue;
        }

        /// <summary>
        /// Extracts a required parameter and throws if missing
        /// </summary>
        public static T GetRequiredParameter<T>(Dictionary<string, object> parameters, string key)
        {
            if (!parameters.ContainsKey(key))
            {
                throw new ArgumentException($"Required parameter '{key}' is missing");
            }

            var value = GetParameter<T>(parameters, key);
            if (value == null || (typeof(T) == typeof(string) && string.IsNullOrEmpty(value as string)))
            {
                throw new ArgumentException($"Required parameter '{key}' cannot be null or empty");
            }

            return value;
        }

        /// <summary>
        /// Validates a file path parameter
        /// </summary>
        public static string ValidateFilePath(Dictionary<string, object> parameters, string key, bool required = true)
        {
            var path = required 
                ? GetRequiredParameter<string>(parameters, key)
                : GetParameter<string>(parameters, key, string.Empty);

            if (string.IsNullOrEmpty(path))
            {
                if (required)
                    throw new ArgumentException($"File path parameter '{key}' cannot be empty");
                return string.Empty;
            }

            if (path.Length > ApplicationConstants.Validation.MaxPathLength)
            {
                throw new ArgumentException($"File path '{path}' exceeds maximum length");
            }

            return path;
        }

        /// <summary>
        /// Validates a numeric parameter within a range
        /// </summary>
        public static T ValidateNumericRange<T>(Dictionary<string, object> parameters, string key, T min, T max, T defaultValue = default!)
            where T : IComparable<T>
        {
            var value = GetParameter<T>(parameters, key, defaultValue);
            
            if (value.CompareTo(min) < 0 || value.CompareTo(max) > 0)
            {
                throw new ArgumentException($"Parameter '{key}' value {value} is outside valid range [{min}, {max}]");
            }

            return value;
        }

        /// <summary>
        /// Validates a string parameter length
        /// </summary>
        public static string ValidateStringLength(Dictionary<string, object> parameters, string key, int maxLength, bool required = true)
        {
            var value = required 
                ? GetRequiredParameter<string>(parameters, key)
                : GetParameter<string>(parameters, key, string.Empty);

            if (!string.IsNullOrEmpty(value) && value.Length > maxLength)
            {
                throw new ArgumentException($"Parameter '{key}' exceeds maximum length of {maxLength} characters");
            }

            return value;
        }

        /// <summary>
        /// Validates an array parameter
        /// </summary>
        public static T[] ValidateArray<T>(Dictionary<string, object> parameters, string key, int maxLength = 1000, bool required = true)
        {
            if (!parameters.ContainsKey(key))
            {
                if (required)
                    throw new ArgumentException($"Required array parameter '{key}' is missing");
                return Array.Empty<T>();
            }

            var value = parameters[key];
            T[] array;

            if (value is T[] directArray)
            {
                array = directArray;
            }
            else if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Array)
            {
                try
                {
                    array = jsonElement.Deserialize<T[]>() ?? Array.Empty<T>();
                }
                catch
                {
                    throw new ArgumentException($"Parameter '{key}' is not a valid array");
                }
            }
            else
            {
                throw new ArgumentException($"Parameter '{key}' is not an array");
            }

            if (array.Length > maxLength)
            {
                throw new ArgumentException($"Array parameter '{key}' exceeds maximum length of {maxLength} items");
            }

            return array;
        }

        /// <summary>
        /// Validates common tool parameters in a single call
        /// </summary>
        public static CommonToolParameters ValidateCommonParameters(Dictionary<string, object> parameters)
        {
            return new CommonToolParameters
            {
                Path = ValidateFilePath(parameters, "path", required: true),
                MaxResults = ValidateNumericRange(parameters, "maxResults", 1, ApplicationConstants.FileOperations.DefaultMaxResults, ApplicationConstants.FileOperations.DefaultMaxResults),
                Recursive = GetParameter<bool>(parameters, "recursive", false),
                IncludeHidden = GetParameter<bool>(parameters, "includeHidden", false),
                DryRun = GetParameter<bool>(parameters, "dryRun", false)
            };
        }
    }

    /// <summary>
    /// Common parameters used across multiple tools
    /// </summary>
    public class CommonToolParameters
    {
        public string Path { get; set; } = string.Empty;
        public int MaxResults { get; set; }
        public bool Recursive { get; set; }
        public bool IncludeHidden { get; set; }
        public bool DryRun { get; set; }
    }
}
