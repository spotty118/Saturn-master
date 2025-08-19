using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Saturn.Core.Performance;

/// <summary>
/// High-performance parallel execution engine with ThreadPool optimization
/// </summary>
public class ParallelExecutor : IDisposable
{
    private readonly SemaphoreSlim _concurrencyLimiter;
    private readonly ConcurrentQueue<TaskCompletionSource<object>> _taskQueue;
    private readonly ThreadPoolMetrics _metrics;
    private readonly CancellationTokenSource _shutdownToken;
    private readonly int _maxConcurrency;
    private bool _disposed = false;

    public ParallelExecutor(int? maxConcurrency = null)
    {
        _maxConcurrency = maxConcurrency ?? Environment.ProcessorCount * 2;
        _concurrencyLimiter = new SemaphoreSlim(_maxConcurrency, _maxConcurrency);
        _taskQueue = new ConcurrentQueue<TaskCompletionSource<object>>();
        _metrics = new ThreadPoolMetrics();
        _shutdownToken = new CancellationTokenSource();
        
        // Initialize thread pool optimization
        ThreadPool.SetMinThreads(_maxConcurrency, _maxConcurrency);
        ThreadPool.SetMaxThreads(_maxConcurrency * 4, _maxConcurrency * 4);
    }

    /// <summary>
    /// Execute operations in parallel with optimal thread utilization
    /// </summary>
    public async Task<T[]> ExecuteParallelAsync<T>(
        IEnumerable<Func<CancellationToken, Task<T>>> operations,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ParallelExecutor));

        var operationArray = operations.ToArray();
        if (operationArray.Length == 0)
            return Array.Empty<T>();

        var tasks = operationArray.Select(operation => 
            ExecuteWithSemaphoreAsync(operation, cancellationToken)).ToArray();

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// Execute operations in parallel with dependency resolution
    /// </summary>
    public async Task<T[]> ExecuteParallelWithDependenciesAsync<T>(
        IEnumerable<ParallelOperation<T>> operations,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ParallelExecutor));

        var operationGraph = new DependencyGraph<T>(operations);
        var results = new ConcurrentDictionary<string, T>();
        var executionTasks = new List<Task>();

        await foreach (var batch in operationGraph.GetExecutionBatches())
        {
            var batchTasks = batch.Select(async op =>
            {
                var dependencies = op.Dependencies
                    .Select(dep => results[dep])
                    .ToArray();

                var result = await ExecuteWithSemaphoreAsync(
                    ct => op.Execute(dependencies, ct), 
                    cancellationToken).ConfigureAwait(false);

                results[op.Id] = result;
            });

            await Task.WhenAll(batchTasks).ConfigureAwait(false);
        }

        return results.Values.ToArray();
    }

    /// <summary>
    /// Process items in parallel using Parallel.ForEach with optimal partitioning
    /// </summary>
    public async Task<TResult[]> ProcessParallelAsync<TSource, TResult>(
        IEnumerable<TSource> source,
        Func<TSource, CancellationToken, Task<TResult>> processor,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ParallelExecutor));

        var sourceArray = source.ToArray();
        if (sourceArray.Length == 0)
            return Array.Empty<TResult>();

        var results = new TResult[sourceArray.Length];
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = _maxConcurrency,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(
            sourceArray.Select((item, index) => new { Item = item, Index = index }),
            options,
            async (indexed, ct) =>
            {
                _metrics.IncrementActiveThreads();
                try
                {
                    results[indexed.Index] = await processor(indexed.Item, ct).ConfigureAwait(false);
                }
                finally
                {
                    _metrics.DecrementActiveThreads();
                }
            }).ConfigureAwait(false);

        return results;
    }

    /// <summary>
    /// Execute CPU-intensive operations using ThreadPool optimization
    /// </summary>
    public async Task<T> ExecuteCpuIntensiveAsync<T>(
        Func<CancellationToken, T> operation,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ParallelExecutor));

        return await Task.Run(() =>
        {
            _metrics.IncrementCpuIntensiveTasks();
            try
            {
                return operation(cancellationToken);
            }
            finally
            {
                _metrics.DecrementCpuIntensiveTasks();
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Execute multiple CPU-intensive operations in parallel with optimal core utilization
    /// </summary>
    public async Task<T[]> ExecuteCpuIntensiveParallelAsync<T>(
        IEnumerable<Func<CancellationToken, T>> operations,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ParallelExecutor));

        var operationArray = operations.ToArray();
        if (operationArray.Length == 0)
            return Array.Empty<T>();

        var results = new T[operationArray.Length];
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount, // Use all CPU cores for CPU-intensive work
            CancellationToken = cancellationToken
        };

        Parallel.For(0, operationArray.Length, options, i =>
        {
            _metrics.IncrementCpuIntensiveTasks();
            try
            {
                results[i] = operationArray[i](cancellationToken);
            }
            finally
            {
                _metrics.DecrementCpuIntensiveTasks();
            }
        });

        return results;
    }

    /// <summary>
    /// Execute I/O operations with optimal async handling
    /// </summary>
    public async Task<T> ExecuteIoIntensiveAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ParallelExecutor));

        _metrics.IncrementIoIntensiveTasks();
        try
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _metrics.DecrementIoIntensiveTasks();
        }
    }

    private async Task<T> ExecuteWithSemaphoreAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        await _concurrencyLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _metrics.IncrementActiveThreads();
            return await Task.Run(async () => await operation(cancellationToken).ConfigureAwait(false), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _metrics.DecrementActiveThreads();
            _concurrencyLimiter.Release();
        }
    }

    public ThreadPoolMetrics GetMetrics() => _metrics.Clone();



    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _shutdownToken.Cancel();
                _concurrencyLimiter?.Dispose();
                _shutdownToken?.Dispose();
            }
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Represents a parallel operation with dependencies
/// </summary>
public class ParallelOperation<T>
{
    public string Id { get; set; } = string.Empty;
    public string[] Dependencies { get; set; } = Array.Empty<string>();
    public Func<T[], CancellationToken, Task<T>> Execute { get; set; } = null!;
}

/// <summary>
/// Thread pool performance metrics
/// </summary>
public class ThreadPoolMetrics
{
    private long _activeThreads;
    private long _cpuIntensiveTasks;
    private long _ioIntensiveTasks;
    private long _totalTasksExecuted;

    public long ActiveThreads => _activeThreads;
    public long CpuIntensiveTasks => _cpuIntensiveTasks;
    public long IoIntensiveTasks => _ioIntensiveTasks;
    public long TotalTasksExecuted => _totalTasksExecuted;

    public int WorkerThreads
    {
        get
        {
            ThreadPool.GetAvailableThreads(out var worker, out _);
            return worker;
        }
    }

    public int CompletionPortThreads
    {
        get
        {
            ThreadPool.GetAvailableThreads(out _, out var completion);
            return completion;
        }
    }

    internal void IncrementActiveThreads()
    {
        Interlocked.Increment(ref _activeThreads);
        Interlocked.Increment(ref _totalTasksExecuted);
    }

    internal void DecrementActiveThreads()
    {
        Interlocked.Decrement(ref _activeThreads);
    }

    internal void IncrementCpuIntensiveTasks()
    {
        Interlocked.Increment(ref _cpuIntensiveTasks);
    }

    internal void DecrementCpuIntensiveTasks()
    {
        Interlocked.Decrement(ref _cpuIntensiveTasks);
    }

    internal void IncrementIoIntensiveTasks()
    {
        Interlocked.Increment(ref _ioIntensiveTasks);
    }

    internal void DecrementIoIntensiveTasks()
    {
        Interlocked.Decrement(ref _ioIntensiveTasks);
    }

    public ThreadPoolMetrics Clone() => new()
    {
        _activeThreads = _activeThreads,
        _cpuIntensiveTasks = _cpuIntensiveTasks,
        _ioIntensiveTasks = _ioIntensiveTasks,
        _totalTasksExecuted = _totalTasksExecuted
    };
}

/// <summary>
/// Dependency graph for parallel operation ordering
/// </summary>
internal class DependencyGraph<T>
{
    private readonly Dictionary<string, ParallelOperation<T>> _operations;
    private readonly Dictionary<string, HashSet<string>> _dependencies;

    public DependencyGraph(IEnumerable<ParallelOperation<T>> operations)
    {
        _operations = operations.ToDictionary(op => op.Id);
        _dependencies = _operations.Values
            .ToDictionary(op => op.Id, op => op.Dependencies.ToHashSet());
    }

    public async IAsyncEnumerable<ParallelOperation<T>[]> GetExecutionBatches()
    {
        var completed = new HashSet<string>();
        var remaining = _operations.Keys.ToHashSet();

        while (remaining.Any())
        {
            var readyToExecute = remaining
                .Where(id => _dependencies[id].All(completed.Contains))
                .Select(id => _operations[id])
                .ToArray();

            if (readyToExecute.Length == 0)
            {
                throw new InvalidOperationException("Circular dependency detected in parallel operations");
            }

            yield return readyToExecute;

            foreach (var op in readyToExecute)
            {
                completed.Add(op.Id);
                remaining.Remove(op.Id);
            }
        }
    }
}