using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Saturn.Core.Logging
{
    /// <summary>
    /// Enhanced logging interface for operations with correlation tracking and performance monitoring.
    /// Replaces Console.WriteLine calls throughout the application with structured logging.
    /// </summary>
    public interface IOperationLogger
    {
        /// <summary>
        /// Starts a new operation scope with correlation tracking.
        /// </summary>
        /// <param name="operationName">Name of the operation</param>
        /// <param name="correlationId">Optional correlation ID for tracking</param>
        /// <returns>Disposable scope that automatically logs completion and duration</returns>
        IOperationScope BeginOperation(string operationName, string? correlationId = null);

        /// <summary>
        /// Logs operation start with context information.
        /// </summary>
        void LogOperationStart(string operationName, object? context = null, string? correlationId = null);

        /// <summary>
        /// Logs operation completion with success status and duration.
        /// </summary>
        void LogOperationComplete(string operationName, bool success, TimeSpan duration, object? result = null, string? correlationId = null);

        /// <summary>
        /// Logs operation failure with detailed error information.
        /// </summary>
        void LogOperationFailure(string operationName, Exception exception, TimeSpan duration, object? context = null, string? correlationId = null);

        /// <summary>
        /// Logs tool execution with parameters and results.
        /// </summary>
        void LogToolExecution(string toolName, object parameters, object? result, TimeSpan duration, bool success, string? correlationId = null);

        /// <summary>
        /// Logs API call details including request/response information.
        /// </summary>
        void LogApiCall(string apiName, string endpoint, object? request, object? response, TimeSpan duration, bool success, string? correlationId = null);

        /// <summary>
        /// Logs configuration changes with before/after values.
        /// </summary>
        void LogConfigurationChange(string section, object? oldValue, object? newValue, string? correlationId = null);

        /// <summary>
        /// Logs security events such as validation failures or unauthorized access attempts.
        /// </summary>
        void LogSecurityEvent(string eventType, string details, object? context = null, string? correlationId = null);
    }

    /// <summary>
    /// Performance-focused logging interface for monitoring system health and optimization.
    /// </summary>
    public interface IPerformanceLogger
    {
        /// <summary>
        /// Records performance metrics for operations.
        /// </summary>
        void RecordMetric(string metricName, double value, string unit, object? tags = null);

        /// <summary>
        /// Records timing information for operations.
        /// </summary>
        void RecordTiming(string operationName, TimeSpan duration, object? tags = null);

        /// <summary>
        /// Records memory usage metrics.
        /// </summary>
        void RecordMemoryUsage(string context, long memoryBytes, object? tags = null);

        /// <summary>
        /// Records file I/O operations and their performance.
        /// </summary>
        void RecordFileOperation(string operation, string filePath, long fileSizeBytes, TimeSpan duration, bool success);

        /// <summary>
        /// Records HTTP request performance.
        /// </summary>
        void RecordHttpRequest(string method, string url, int statusCode, TimeSpan duration, long? responseSize = null);

        /// <summary>
        /// Records agent operation performance.
        /// </summary>
        void RecordAgentOperation(string agentName, string operation, TimeSpan duration, bool success, object? metadata = null);

        /// <summary>
        /// Records diff operation performance (specific to Saturn's core functionality).
        /// </summary>
        void RecordDiffOperation(string strategy, string fileName, long fileSizeBytes, TimeSpan duration, bool success, bool fallbackUsed = false);
    }

    /// <summary>
    /// Disposable scope for automatic operation tracking and logging.
    /// </summary>
    public interface IOperationScope : IDisposable
    {
        /// <summary>
        /// The correlation ID for this operation.
        /// </summary>
        string CorrelationId { get; }

        /// <summary>
        /// The operation name.
        /// </summary>
        string OperationName { get; }

        /// <summary>
        /// Adds context information to the operation.
        /// </summary>
        void AddContext(string key, object value);

        /// <summary>
        /// Marks the operation as successful with optional result.
        /// </summary>
        void Complete(object? result = null);

        /// <summary>
        /// Marks the operation as failed with exception details.
        /// </summary>
        void Fail(Exception exception);

        /// <summary>
        /// Gets the elapsed time for this operation.
        /// </summary>
        TimeSpan Elapsed { get; }
    }

    /// <summary>
    /// Implementation of operation scope with automatic timing and logging.
    /// </summary>
    public class OperationScope : IOperationScope
    {
        private readonly IOperationLogger _logger;
        private readonly Stopwatch _stopwatch;
        private readonly Dictionary<string, object> _context = new();
        private bool _disposed = false;
        private bool _completed = false;
        private Exception? _exception;
        private object? _result;

        public string CorrelationId { get; }
        public string OperationName { get; }
        public TimeSpan Elapsed => _stopwatch.Elapsed;

        public OperationScope(IOperationLogger logger, string operationName, string correlationId)
        {
            _logger = logger;
            OperationName = operationName;
            CorrelationId = correlationId;
            _stopwatch = Stopwatch.StartNew();
            
            _logger.LogOperationStart(operationName, _context, correlationId);
        }

        public void AddContext(string key, object value)
        {
            _context[key] = value;
        }

        public void Complete(object? result = null)
        {
            _result = result;
            _completed = true;
        }

        public void Fail(Exception exception)
        {
            _exception = exception;
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            _stopwatch.Stop();
            
            if (_exception != null)
            {
                _logger.LogOperationFailure(OperationName, _exception, Elapsed, _context, CorrelationId);
            }
            else
            {
                _logger.LogOperationComplete(OperationName, _completed, Elapsed, _result, CorrelationId);
            }
            
            _disposed = true;
        }
    }
}