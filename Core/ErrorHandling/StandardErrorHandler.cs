using Microsoft.Extensions.Logging;
using Saturn.Core.Logging;
using System;
using System.Threading.Tasks;

namespace Saturn.Core.ErrorHandling
{
    /// <summary>
    /// Standardized error handling utility for consistent exception management across the application
    /// </summary>
    public static class StandardErrorHandler
    {
        /// <summary>
        /// Handles exceptions with consistent logging and error response formatting
        /// </summary>
        /// <typeparam name="T">Return type for success case</typeparam>
        /// <param name="operation">The operation to execute</param>
        /// <param name="operationName">Name of the operation for logging</param>
        /// <param name="logger">Logger instance</param>
        /// <param name="defaultValue">Default value to return on error</param>
        /// <param name="context">Additional context for logging</param>
        /// <returns>Result of operation or default value on error</returns>
        public static async Task<T> HandleAsync<T>(
            Func<Task<T>> operation,
            string operationName,
            IOperationLogger logger,
            T defaultValue = default!,
            object? context = null)
        {
            using var scope = logger.BeginOperation(operationName);
            if (context != null)
            {
                scope.AddContext("OperationContext", context);
            }

            try
            {
                var result = await operation();
                scope.Complete(result);
                return result;
            }
            catch (Exception ex)
            {
                scope.Fail(ex);
                logger.LogOperationFailure(operationName, ex, scope.Elapsed, context);
                return defaultValue;
            }
        }

        /// <summary>
        /// Handles synchronous operations with consistent error handling
        /// </summary>
        public static T Handle<T>(
            Func<T> operation,
            string operationName,
            IOperationLogger logger,
            T defaultValue = default!,
            object? context = null)
        {
            using var scope = logger.BeginOperation(operationName);
            if (context != null)
            {
                scope.AddContext("OperationContext", context);
            }

            try
            {
                var result = operation();
                scope.Complete(result);
                return result;
            }
            catch (Exception ex)
            {
                scope.Fail(ex);
                logger.LogOperationFailure(operationName, ex, scope.Elapsed, context);
                return defaultValue;
            }
        }

        /// <summary>
        /// Handles operations that don't return values
        /// </summary>
        public static async Task HandleAsync(
            Func<Task> operation,
            string operationName,
            IOperationLogger logger,
            object? context = null)
        {
            using var scope = logger.BeginOperation(operationName);
            if (context != null)
            {
                scope.AddContext("OperationContext", context);
            }

            try
            {
                await operation();
                scope.Complete();
            }
            catch (Exception ex)
            {
                scope.Fail(ex);
                logger.LogOperationFailure(operationName, ex, scope.Elapsed, context);
                throw; // Re-throw for void operations
            }
        }

        /// <summary>
        /// Safely executes an operation and returns success/failure status
        /// </summary>
        public static async Task<(bool Success, T? Result, Exception? Error)> TryExecuteAsync<T>(
            Func<Task<T>> operation,
            string operationName,
            IOperationLogger logger,
            object? context = null)
        {
            using var scope = logger.BeginOperation(operationName);
            if (context != null)
            {
                scope.AddContext("OperationContext", context);
            }

            try
            {
                var result = await operation();
                scope.Complete(result);
                return (true, result, null);
            }
            catch (Exception ex)
            {
                scope.Fail(ex);
                logger.LogOperationFailure(operationName, ex, scope.Elapsed, context);
                return (false, default, ex);
            }
        }

        /// <summary>
        /// Creates a standardized error message for user display
        /// </summary>
        public static string CreateUserFriendlyErrorMessage(Exception ex, string operation)
        {
            return ex switch
            {
                UnauthorizedAccessException => $"Access denied during {operation}. Check permissions.",
                System.IO.FileNotFoundException => $"Required file not found during {operation}.",
                System.IO.DirectoryNotFoundException => $"Required directory not found during {operation}.",
                System.IO.IOException ioEx => $"File system error during {operation}: {ioEx.Message}",
                ArgumentException argEx => $"Invalid input for {operation}: {argEx.Message}",
                InvalidOperationException opEx => $"Operation failed: {opEx.Message}",
                TimeoutException => $"Operation {operation} timed out.",
                _ => $"An error occurred during {operation}. Please try again."
            };
        }

        /// <summary>
        /// Determines if an exception should be logged as an error or warning
        /// </summary>
        public static LogLevel GetLogLevel(Exception ex)
        {
            return ex switch
            {
                ArgumentException => LogLevel.Warning,
                UnauthorizedAccessException => LogLevel.Warning,
                System.IO.FileNotFoundException => LogLevel.Warning,
                System.IO.DirectoryNotFoundException => LogLevel.Warning,
                TimeoutException => LogLevel.Warning,
                _ => LogLevel.Error
            };
        }
    }
}
