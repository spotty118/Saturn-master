using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;

namespace Saturn.Core.Logging
{
    /// <summary>
    /// Concrete implementation of operation logging with structured logging support.
    /// Replaces Console.WriteLine calls throughout Saturn with proper logging infrastructure.
    /// </summary>
    public class OperationLogger : IOperationLogger
    {
        private readonly ILogger<OperationLogger> _logger;
        private readonly IPerformanceLogger _performanceLogger;

        public OperationLogger(ILogger<OperationLogger> logger, IPerformanceLogger performanceLogger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _performanceLogger = performanceLogger ?? throw new ArgumentNullException(nameof(performanceLogger));
        }

        public IOperationScope BeginOperation(string operationName, string? correlationId = null)
        {
            correlationId ??= Guid.NewGuid().ToString("N")[..8];
            return new OperationScope(this, operationName, correlationId);
        }

        public void LogOperationStart(string operationName, object? context = null, string? correlationId = null)
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["OperationName"] = operationName,
                ["CorrelationId"] = correlationId ?? "unknown",
                ["Context"] = context ?? new { }
            });

            _logger.LogInformation("Operation started: {OperationName}", operationName);
        }

        public void LogOperationComplete(string operationName, bool success, TimeSpan duration, object? result = null, string? correlationId = null)
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["OperationName"] = operationName,
                ["CorrelationId"] = correlationId ?? "unknown",
                ["Duration"] = duration.TotalMilliseconds,
                ["Success"] = success,
                ["Result"] = result ?? new { }
            });

            _performanceLogger.RecordTiming(operationName, duration, new { Success = success });

            if (success)
            {
                _logger.LogInformation("Operation completed successfully: {OperationName} in {Duration}ms", 
                    operationName, duration.TotalMilliseconds);
            }
            else
            {
                _logger.LogWarning("Operation completed with issues: {OperationName} in {Duration}ms", 
                    operationName, duration.TotalMilliseconds);
            }
        }

        public void LogOperationFailure(string operationName, Exception exception, TimeSpan duration, object? context = null, string? correlationId = null)
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["OperationName"] = operationName,
                ["CorrelationId"] = correlationId ?? "unknown",
                ["Duration"] = duration.TotalMilliseconds,
                ["Context"] = context ?? new { }
            });

            _performanceLogger.RecordTiming(operationName, duration, new { Success = false, ErrorType = exception.GetType().Name });

            _logger.LogError(exception, "Operation failed: {OperationName} in {Duration}ms - {ErrorMessage}", 
                operationName, duration.TotalMilliseconds, exception.Message);
        }

        public void LogToolExecution(string toolName, object parameters, object? result, TimeSpan duration, bool success, string? correlationId = null)
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["ToolName"] = toolName,
                ["CorrelationId"] = correlationId ?? "unknown",
                ["Duration"] = duration.TotalMilliseconds,
                ["Success"] = success,
                ["Parameters"] = parameters,
                ["Result"] = result ?? new { }
            });

            _performanceLogger.RecordTiming($"tool.{toolName}", duration, new { Success = success });

            if (success)
            {
                _logger.LogInformation("Tool executed successfully: {ToolName} in {Duration}ms", 
                    toolName, duration.TotalMilliseconds);
            }
            else
            {
                _logger.LogWarning("Tool execution failed: {ToolName} in {Duration}ms", 
                    toolName, duration.TotalMilliseconds);
            }
        }

        public void LogApiCall(string apiName, string endpoint, object? request, object? response, TimeSpan duration, bool success, string? correlationId = null)
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["ApiName"] = apiName,
                ["Endpoint"] = endpoint,
                ["CorrelationId"] = correlationId ?? "unknown",
                ["Duration"] = duration.TotalMilliseconds,
                ["Success"] = success,
                ["Request"] = request ?? new { },
                ["Response"] = response ?? new { }
            });

            _performanceLogger.RecordTiming($"api.{apiName}", duration, new { Success = success, Endpoint = endpoint });

            if (success)
            {
                _logger.LogInformation("API call successful: {ApiName} -> {Endpoint} in {Duration}ms", 
                    apiName, endpoint, duration.TotalMilliseconds);
            }
            else
            {
                _logger.LogWarning("API call failed: {ApiName} -> {Endpoint} in {Duration}ms", 
                    apiName, endpoint, duration.TotalMilliseconds);
            }
        }

        public void LogConfigurationChange(string section, object? oldValue, object? newValue, string? correlationId = null)
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["ConfigurationSection"] = section,
                ["CorrelationId"] = correlationId ?? "unknown",
                ["OldValue"] = oldValue ?? new { },
                ["NewValue"] = newValue ?? new { }
            });

            _logger.LogInformation("Configuration changed: {Section}", section);
        }

        public void LogSecurityEvent(string eventType, string details, object? context = null, string? correlationId = null)
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["SecurityEventType"] = eventType,
                ["CorrelationId"] = correlationId ?? "unknown",
                ["Details"] = details,
                ["Context"] = context ?? new { }
            });

            _logger.LogWarning("Security event: {EventType} - {Details}", eventType, details);
        }
    }

    /// <summary>
    /// Concrete implementation of performance logging with metric collection.
    /// Supports Saturn's existing performance monitoring and expands capabilities.
    /// </summary>
    public class PerformanceLogger : IPerformanceLogger
    {
        private readonly ILogger<PerformanceLogger> _logger;

        public PerformanceLogger(ILogger<PerformanceLogger> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void RecordMetric(string metricName, double value, string unit, object? tags = null)
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["MetricName"] = metricName,
                ["Value"] = value,
                ["Unit"] = unit,
                ["Tags"] = tags ?? new { }
            });

            _logger.LogDebug("Metric recorded: {MetricName} = {Value} {Unit}", metricName, value, unit);
        }

        public void RecordTiming(string operationName, TimeSpan duration, object? tags = null)
        {
            RecordMetric($"timing.{operationName}", duration.TotalMilliseconds, "ms", tags);
        }

        public void RecordMemoryUsage(string context, long memoryBytes, object? tags = null)
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["Context"] = context,
                ["MemoryBytes"] = memoryBytes,
                ["MemoryMB"] = memoryBytes / (1024.0 * 1024.0),
                ["Tags"] = tags ?? new { }
            });

            _logger.LogDebug("Memory usage: {Context} = {MemoryMB:F2} MB", context, memoryBytes / (1024.0 * 1024.0));
            RecordMetric($"memory.{context}", memoryBytes, "bytes", tags);
        }

        public void RecordFileOperation(string operation, string filePath, long fileSizeBytes, TimeSpan duration, bool success)
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["FileOperation"] = operation,
                ["FilePath"] = filePath,
                ["FileSizeBytes"] = fileSizeBytes,
                ["FileSizeMB"] = fileSizeBytes / (1024.0 * 1024.0),
                ["Duration"] = duration.TotalMilliseconds,
                ["Success"] = success
            });

            _logger.LogDebug("File operation: {Operation} on {FilePath} ({FileSizeMB:F2} MB) in {Duration}ms - {Status}", 
                operation, filePath, fileSizeBytes / (1024.0 * 1024.0), duration.TotalMilliseconds, success ? "Success" : "Failed");

            RecordTiming($"file.{operation}", duration, new { Success = success, FileSizeBytes = fileSizeBytes });
        }

        public void RecordHttpRequest(string method, string url, int statusCode, TimeSpan duration, long? responseSize = null)
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["HttpMethod"] = method,
                ["Url"] = url,
                ["StatusCode"] = statusCode,
                ["Duration"] = duration.TotalMilliseconds,
                ["ResponseSize"] = responseSize
            });

            _logger.LogDebug("HTTP request: {Method} {Url} -> {StatusCode} in {Duration}ms", 
                method, url, statusCode, duration.TotalMilliseconds);

            RecordTiming("http.request", duration, new { Method = method, StatusCode = statusCode });
            
            if (responseSize.HasValue)
            {
                RecordMetric("http.response_size", responseSize.Value, "bytes", new { Method = method, StatusCode = statusCode });
            }
        }

        public void RecordAgentOperation(string agentName, string operation, TimeSpan duration, bool success, object? metadata = null)
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["AgentName"] = agentName,
                ["AgentOperation"] = operation,
                ["Duration"] = duration.TotalMilliseconds,
                ["Success"] = success,
                ["Metadata"] = metadata ?? new { }
            });

            _logger.LogDebug("Agent operation: {AgentName}.{Operation} in {Duration}ms - {Status}", 
                agentName, operation, duration.TotalMilliseconds, success ? "Success" : "Failed");

            RecordTiming($"agent.{agentName}.{operation}", duration, new { Success = success });
        }

        public void RecordDiffOperation(string strategy, string fileName, long fileSizeBytes, TimeSpan duration, bool success, bool fallbackUsed = false)
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["DiffStrategy"] = strategy,
                ["FileName"] = fileName,
                ["FileSizeBytes"] = fileSizeBytes,
                ["FileSizeMB"] = fileSizeBytes / (1024.0 * 1024.0),
                ["Duration"] = duration.TotalMilliseconds,
                ["Success"] = success,
                ["FallbackUsed"] = fallbackUsed
            });

            _logger.LogDebug("Diff operation: {Strategy} on {FileName} ({FileSizeMB:F2} MB) in {Duration}ms - {Status}{Fallback}", 
                strategy, fileName, fileSizeBytes / (1024.0 * 1024.0), duration.TotalMilliseconds, 
                success ? "Success" : "Failed", fallbackUsed ? " (fallback used)" : "");

            RecordTiming($"diff.{strategy}", duration, new { 
                Success = success, 
                FallbackUsed = fallbackUsed, 
                FileSizeBytes = fileSizeBytes 
            });
        }
    }
}