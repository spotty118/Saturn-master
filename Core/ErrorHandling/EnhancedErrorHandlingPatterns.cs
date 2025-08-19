using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Saturn.Core.Logging;

namespace Saturn.Core.ErrorHandling
{
    /// <summary>
    /// Enhanced error handling patterns extending Saturn's StandardErrorHandler with advanced
    /// retry logic, circuit breaker patterns, and context-aware error recovery strategies.
    /// </summary>
    public static class EnhancedErrorHandlingPatterns
    {
        /// <summary>
        /// Execute operation with exponential backoff retry and jitter
        /// </summary>
        public static async Task<T> HandleWithRetryAsync<T>(
            Func<Task<T>> operation,
            string operationName,
            IOperationLogger logger,
            RetryOptions? retryOptions = null,
            T defaultValue = default!,
            [CallerMemberName] string callerName = "",
            [CallerFilePath] string callerPath = "",
            [CallerLineNumber] int callerLine = 0)
        {
            retryOptions ??= RetryOptions.Default;
            var context = new OperationContext(operationName, callerName, callerPath, callerLine);

            using var scope = logger.BeginOperation($"{operationName}_WithRetry");
            scope.AddContext("RetryConfig", retryOptions);
            scope.AddContext("CallerContext", context);

            for (int attempt = 1; attempt <= retryOptions.MaxAttempts; attempt++)
            {
                try
                {
                    var result = await operation();
                    if (attempt > 1)
                    {
                        logger.LogInformation($"Operation {operationName} succeeded on attempt {attempt}");
                    }
                    scope.Complete(result);
                    return result;
                }
                catch (Exception ex) when (ShouldRetry(ex, attempt, retryOptions))
                {
                    var delay = CalculateDelay(attempt, retryOptions);
                    logger.LogWarning($"Operation {operationName} failed on attempt {attempt}/{retryOptions.MaxAttempts}. " +
                                    $"Retrying in {delay.TotalMilliseconds}ms. Error: {ex.Message}");

                    scope.AddContext($"Attempt_{attempt}_Error", ex.Message);
                    
                    if (attempt < retryOptions.MaxAttempts)
                    {
                        await Task.Delay(delay);
                    }
                }
                catch (Exception ex)
                {
                    scope.Fail(ex);
                    logger.LogOperationFailure($"{operationName}_NonRetryable", ex, scope.Elapsed, context);
                    return defaultValue;
                }
            }

            var finalException = new OperationFailedException($"Operation {operationName} failed after {retryOptions.MaxAttempts} attempts");
            scope.Fail(finalException);
            logger.LogOperationFailure($"{operationName}_ExhaustedRetries", finalException, scope.Elapsed, context);
            return defaultValue;
        }

        /// <summary>
        /// Execute operation with circuit breaker pattern for external service calls
        /// </summary>
        public static async Task<T> HandleWithCircuitBreakerAsync<T>(
            Func<Task<T>> operation,
            string operationName,
            IOperationLogger logger,
            CircuitBreakerOptions? circuitOptions = null,
            T defaultValue = default!,
            [CallerMemberName] string callerName = "",
            [CallerFilePath] string callerPath = "",
            [CallerLineNumber] int callerLine = 0)
        {
            circuitOptions ??= CircuitBreakerOptions.Default;
            var context = new OperationContext(operationName, callerName, callerPath, callerLine);

            var circuitBreaker = CircuitBreakerRegistry.GetOrCreate(operationName, circuitOptions);

            using var scope = logger.BeginOperation($"{operationName}_WithCircuitBreaker");
            scope.AddContext("CircuitState", circuitBreaker.State);
            scope.AddContext("CallerContext", context);

            if (circuitBreaker.State == CircuitState.Open)
            {
                var error = new CircuitBreakerOpenException($"Circuit breaker is OPEN for {operationName}");
                scope.Fail(error);
                logger.LogWarning($"Circuit breaker OPEN for {operationName}, returning default value");
                return defaultValue;
            }

            try
            {
                var result = await operation();
                circuitBreaker.RecordSuccess();
                scope.Complete(result);
                return result;
            }
            catch (Exception ex)
            {
                circuitBreaker.RecordFailure();
                scope.Fail(ex);

                if (IsTransientError(ex))
                {
                    logger.LogWarning($"Transient error in {operationName}: {ex.Message}");
                    return defaultValue;
                }
                else
                {
                    logger.LogOperationFailure(operationName, ex, scope.Elapsed, context);
                    throw;
                }
            }
        }

        /// <summary>
        /// Execute operation with timeout and cancellation support
        /// </summary>
        public static async Task<T> HandleWithTimeoutAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            string operationName,
            IOperationLogger logger,
            TimeSpan timeout,
            T defaultValue = default!,
            [CallerMemberName] string callerName = "",
            [CallerFilePath] string callerPath = "",
            [CallerLineNumber] int callerLine = 0)
        {
            var context = new OperationContext(operationName, callerName, callerPath, callerLine);

            using var scope = logger.BeginOperation($"{operationName}_WithTimeout");
            scope.AddContext("Timeout", timeout);
            scope.AddContext("CallerContext", context);

            using var cts = new CancellationTokenSource(timeout);
            
            try
            {
                var result = await operation(cts.Token);
                scope.Complete(result);
                return result;
            }
            catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
            {
                var timeoutEx = new TimeoutException($"Operation {operationName} timed out after {timeout.TotalMilliseconds}ms");
                scope.Fail(timeoutEx);
                logger.LogOperationFailure($"{operationName}_Timeout", timeoutEx, scope.Elapsed, context);
                return defaultValue;
            }
            catch (Exception ex)
            {
                scope.Fail(ex);
                logger.LogOperationFailure(operationName, ex, scope.Elapsed, context);
                return defaultValue;
            }
        }

        /// <summary>
        /// Execute operation with comprehensive error handling including retry, circuit breaker, and timeout
        /// </summary>
        public static async Task<T> HandleWithFullProtectionAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            string operationName,
            IOperationLogger logger,
            FullProtectionOptions? options = null,
            T defaultValue = default!,
            [CallerMemberName] string callerName = "",
            [CallerFilePath] string callerPath = "",
            [CallerLineNumber] int callerLine = 0)
        {
            options ??= FullProtectionOptions.Default;
            var context = new OperationContext(operationName, callerName, callerPath, callerLine);

            using var scope = logger.BeginOperation($"{operationName}_FullProtection");
            scope.AddContext("ProtectionConfig", options);
            scope.AddContext("CallerContext", context);

            return await HandleWithRetryAsync(async () =>
            {
                return await HandleWithCircuitBreakerAsync(async () =>
                {
                    return await HandleWithTimeoutAsync(operation, operationName, logger, options.Timeout, defaultValue, callerName, callerPath, callerLine);
                }, operationName, logger, options.CircuitBreaker, defaultValue, callerName, callerPath, callerLine);
            }, operationName, logger, options.Retry, defaultValue, callerName, callerPath, callerLine);
        }

        /// <summary>
        /// Handle file operations with specific file system error recovery
        /// </summary>
        public static async Task<T> HandleFileOperationAsync<T>(
            Func<Task<T>> operation,
            string filePath,
            string operationName,
            IOperationLogger logger,
            T defaultValue = default!,
            [CallerMemberName] string callerName = "",
            [CallerFilePath] string callerPath = "",
            [CallerLineNumber] int callerLine = 0)
        {
            var context = new FileOperationContext(operationName, filePath, callerName, callerPath, callerLine);

            using var scope = logger.BeginOperation($"FileOp_{operationName}");
            scope.AddContext("FilePath", filePath);
            scope.AddContext("CallerContext", context);

            try
            {
                // Pre-operation file system checks
                ValidateFileSystemState(filePath, operationName);

                var result = await operation();
                scope.Complete(result);
                return result;
            }
            catch (FileNotFoundException ex)
            {
                scope.Fail(ex);
                logger.LogWarning($"File not found for {operationName}: {filePath}");
                
                // Attempt to create parent directory if needed
                if (operationName.Contains("Write") || operationName.Contains("Create"))
                {
                    try
                    {
                        var directory = Path.GetDirectoryName(filePath);
                        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                            logger.LogInformation($"Created missing directory: {directory}");
                            
                            // Retry the operation once
                            var retryResult = await operation();
                            scope.Complete(retryResult);
                            return retryResult;
                        }
                    }
                    catch (Exception createEx)
                    {
                        logger.LogError($"Failed to create directory for {filePath}: {createEx.Message}");
                    }
                }
                
                return defaultValue;
            }
            catch (DirectoryNotFoundException ex)
            {
                scope.Fail(ex);
                logger.LogOperationFailure($"{operationName}_DirectoryNotFound", ex, scope.Elapsed, context);
                return defaultValue;
            }
            catch (UnauthorizedAccessException ex)
            {
                scope.Fail(ex);
                logger.LogOperationFailure($"{operationName}_AccessDenied", ex, scope.Elapsed, context);
                return defaultValue;
            }
            catch (IOException ex) when (IsFileInUseError(ex))
            {
                // Retry with exponential backoff for file in use
                var retryOptions = new RetryOptions
                {
                    MaxAttempts = 3,
                    BaseDelay = TimeSpan.FromMilliseconds(100),
                    MaxDelay = TimeSpan.FromSeconds(1)
                };

                logger.LogWarning($"File in use for {operationName}: {filePath}, retrying...");
                return await HandleWithRetryAsync(operation, $"{operationName}_FileInUse", logger, retryOptions, defaultValue, callerName, callerPath, callerLine);
            }
            catch (Exception ex)
            {
                scope.Fail(ex);
                logger.LogOperationFailure(operationName, ex, scope.Elapsed, context);
                return defaultValue;
            }
        }

        /// <summary>
        /// Handle network operations with connection-specific error recovery
        /// </summary>
        public static async Task<T> HandleNetworkOperationAsync<T>(
            Func<Task<T>> operation,
            string endpoint,
            string operationName,
            IOperationLogger logger,
            NetworkErrorOptions? networkOptions = null,
            T defaultValue = default!,
            [CallerMemberName] string callerName = "",
            [CallerFilePath] string callerPath = "",
            [CallerLineNumber] int callerLine = 0)
        {
            networkOptions ??= NetworkErrorOptions.Default;
            var context = new NetworkOperationContext(operationName, endpoint, callerName, callerPath, callerLine);

            using var scope = logger.BeginOperation($"NetworkOp_{operationName}");
            scope.AddContext("Endpoint", endpoint);
            scope.AddContext("NetworkConfig", networkOptions);
            scope.AddContext("CallerContext", context);

            var retryOptions = new RetryOptions
            {
                MaxAttempts = networkOptions.MaxRetries,
                BaseDelay = networkOptions.BaseDelay,
                MaxDelay = networkOptions.MaxDelay,
                BackoffMultiplier = 2.0
            };

            return await HandleWithRetryAsync(async () =>
            {
                try
                {
                    return await operation();
                }
                catch (HttpRequestException ex)
                {
                    logger.LogWarning($"HTTP request failed for {endpoint}: {ex.Message}");
                    throw;
                }
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
                {
                    logger.LogWarning($"Network timeout for {endpoint}: {ex.Message}");
                    throw new TimeoutException($"Network operation timed out: {endpoint}", ex);
                }
            }, $"{operationName}_Network", logger, retryOptions, defaultValue, callerName, callerPath, callerLine);
        }

        /// <summary>
        /// Execute multiple operations with coordinated error handling and partial success tracking
        /// </summary>
        public static async Task<BatchOperationResult<T>> HandleBatchOperationAsync<T>(
            IEnumerable<Func<Task<T>>> operations,
            string batchName,
            IOperationLogger logger,
            BatchOperationOptions? batchOptions = null,
            [CallerMemberName] string callerName = "",
            [CallerFilePath] string callerPath = "",
            [CallerLineNumber] int callerLine = 0)
        {
            batchOptions ??= BatchOperationOptions.Default;
            var context = new OperationContext(batchName, callerName, callerPath, callerLine);
            var operationList = operations.ToList();

            using var scope = logger.BeginOperation($"Batch_{batchName}");
            scope.AddContext("OperationCount", operationList.Count);
            scope.AddContext("BatchConfig", batchOptions);
            scope.AddContext("CallerContext", context);

            var results = new List<T>();
            var errors = new List<Exception>();
            var successCount = 0;

            using var semaphore = new SemaphoreSlim(batchOptions.MaxConcurrency);

            var tasks = operationList.Select(async (operation, index) =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var result = await StandardErrorHandler.TryExecuteAsync(
                        operation,
                        $"{batchName}_Operation_{index}",
                        logger,
                        context);

                    lock (results)
                    {
                        if (result.Success && result.Result != null)
                        {
                            results.Add(result.Result);
                            successCount++;
                        }
                        else if (result.Error != null)
                        {
                            errors.Add(result.Error);
                        }
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            var batchResult = new BatchOperationResult<T>
            {
                Results = results,
                Errors = errors,
                TotalOperations = operationList.Count,
                SuccessfulOperations = successCount,
                FailedOperations = errors.Count,
                SuccessRate = operationList.Count > 0 ? (double)successCount / operationList.Count : 0
            };

            if (batchResult.SuccessRate >= batchOptions.MinimumSuccessRate)
            {
                scope.Complete(batchResult);
                logger.LogInformation($"Batch {batchName} completed: {successCount}/{operationList.Count} successful");
            }
            else
            {
                var batchError = new BatchOperationException($"Batch {batchName} failed minimum success rate: {batchResult.SuccessRate:P} < {batchOptions.MinimumSuccessRate:P}");
                scope.Fail(batchError);
                logger.LogOperationFailure($"Batch_{batchName}_InsufficientSuccess", batchError, scope.Elapsed, context);
            }

            return batchResult;
        }

        // Helper methods
        private static bool ShouldRetry(Exception ex, int attempt, RetryOptions options)
        {
            if (attempt >= options.MaxAttempts) return false;

            return ex switch
            {
                TimeoutException => true,
                HttpRequestException => true,
                IOException when IsTransientIOError(ex) => true,
                SocketException => true,
                TaskCanceledException when ex.InnerException is TimeoutException => true,
                _ => options.RetryOnAllExceptions
            };
        }

        private static TimeSpan CalculateDelay(int attempt, RetryOptions options)
        {
            var baseDelay = TimeSpan.FromMilliseconds(
                options.BaseDelay.TotalMilliseconds * Math.Pow(options.BackoffMultiplier, attempt - 1));

            // Add jitter to prevent thundering herd
            var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, (int)(baseDelay.TotalMilliseconds * 0.1)));
            var totalDelay = baseDelay + jitter;

            return totalDelay > options.MaxDelay ? options.MaxDelay : totalDelay;
        }

        private static bool IsTransientError(Exception ex)
        {
            return ex is TimeoutException or HttpRequestException or IOException or SocketException;
        }

        private static bool IsTransientIOError(Exception ex)
        {
            return ex.Message.Contains("being used by another process") ||
                   ex.Message.Contains("temporarily unavailable") ||
                   ex.HResult == -2147024864; // ERROR_SHARING_VIOLATION
        }

        private static bool IsFileInUseError(Exception ex)
        {
            return ex.Message.Contains("being used by another process") ||
                   ex.HResult == -2147024864; // ERROR_SHARING_VIOLATION
        }

        private static void ValidateFileSystemState(string filePath, string operationName)
        {
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    if (operationName.Contains("Read") || operationName.Contains("Open"))
                    {
                        throw new DirectoryNotFoundException($"Directory does not exist: {directory}");
                    }
                }

                // Check disk space for write operations
                if (operationName.Contains("Write") || operationName.Contains("Create"))
                {
                    var drive = new DriveInfo(Path.GetPathRoot(filePath) ?? "C:");
                    if (drive.AvailableFreeSpace < 1024 * 1024) // Less than 1MB
                    {
                        throw new IOException($"Insufficient disk space on drive {drive.Name}");
                    }
                }
            }
            catch (Exception ex) when (!(ex is DirectoryNotFoundException or IOException))
            {
                // Ignore validation errors that aren't critical
            }
        }
    }

    // Configuration classes
    public class RetryOptions
    {
        public int MaxAttempts { get; set; } = 3;
        public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(100);
        public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);
        public double BackoffMultiplier { get; set; } = 2.0;
        public bool RetryOnAllExceptions { get; set; } = false;

        public static RetryOptions Default => new();
    }

    public class CircuitBreakerOptions
    {
        public int FailureThreshold { get; set; } = 5;
        public TimeSpan RecoveryTimeout { get; set; } = TimeSpan.FromMinutes(1);
        public int SamplingDuration { get; set; } = 10;

        public static CircuitBreakerOptions Default => new();
    }

    public class FullProtectionOptions
    {
        public RetryOptions Retry { get; set; } = RetryOptions.Default;
        public CircuitBreakerOptions CircuitBreaker { get; set; } = CircuitBreakerOptions.Default;
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

        public static FullProtectionOptions Default => new();
    }

    public class NetworkErrorOptions
    {
        public int MaxRetries { get; set; } = 3;
        public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);
        public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(10);

        public static NetworkErrorOptions Default => new();
    }

    public class BatchOperationOptions
    {
        public int MaxConcurrency { get; set; } = Environment.ProcessorCount;
        public double MinimumSuccessRate { get; set; } = 0.8; // 80%

        public static BatchOperationOptions Default => new();
    }

    // Context classes
    public class OperationContext
    {
        public string OperationName { get; }
        public string CallerName { get; }
        public string CallerPath { get; }
        public int CallerLine { get; }
        public DateTime StartTime { get; } = DateTime.UtcNow;

        public OperationContext(string operationName, string callerName, string callerPath, int callerLine)
        {
            OperationName = operationName;
            CallerName = callerName;
            CallerPath = Path.GetFileName(callerPath);
            CallerLine = callerLine;
        }
    }

    public class FileOperationContext : OperationContext
    {
        public string FilePath { get; }

        public FileOperationContext(string operationName, string filePath, string callerName, string callerPath, int callerLine)
            : base(operationName, callerName, callerPath, callerLine)
        {
            FilePath = filePath;
        }
    }

    public class NetworkOperationContext : OperationContext
    {
        public string Endpoint { get; }

        public NetworkOperationContext(string operationName, string endpoint, string callerName, string callerPath, int callerLine)
            : base(operationName, callerName, callerPath, callerLine)
        {
            Endpoint = endpoint;
        }
    }

    // Result classes
    public class BatchOperationResult<T>
    {
        public List<T> Results { get; set; } = new();
        public List<Exception> Errors { get; set; } = new();
        public int TotalOperations { get; set; }
        public int SuccessfulOperations { get; set; }
        public int FailedOperations { get; set; }
        public double SuccessRate { get; set; }
    }

    // Exception classes
    public class OperationFailedException : Exception
    {
        public OperationFailedException(string message) : base(message) { }
        public OperationFailedException(string message, Exception innerException) : base(message, innerException) { }
    }

    public class CircuitBreakerOpenException : Exception
    {
        public CircuitBreakerOpenException(string message) : base(message) { }
    }

    public class BatchOperationException : Exception
    {
        public BatchOperationException(string message) : base(message) { }
    }

    // Circuit breaker implementation
    public enum CircuitState { Closed, Open, HalfOpen }

    public class CircuitBreaker
    {
        private readonly CircuitBreakerOptions _options;
        private int _failureCount;
        private DateTime _lastFailureTime;
        private CircuitState _state = CircuitState.Closed;

        public CircuitState State => _state;

        public CircuitBreaker(CircuitBreakerOptions options)
        {
            _options = options;
        }

        public void RecordSuccess()
        {
            _failureCount = 0;
            _state = CircuitState.Closed;
        }

        public void RecordFailure()
        {
            _failureCount++;
            _lastFailureTime = DateTime.UtcNow;

            if (_failureCount >= _options.FailureThreshold)
            {
                _state = CircuitState.Open;
            }
        }
    }

    public static class CircuitBreakerRegistry
    {
        private static readonly Dictionary<string, CircuitBreaker> _circuitBreakers = new();
        private static readonly object _lock = new object();

        public static CircuitBreaker GetOrCreate(string name, CircuitBreakerOptions options)
        {
            lock (_lock)
            {
                if (!_circuitBreakers.TryGetValue(name, out var circuitBreaker))
                {
                    circuitBreaker = new CircuitBreaker(options);
                    _circuitBreakers[name] = circuitBreaker;
                }
                return circuitBreaker;
            }
        }
    }
}
