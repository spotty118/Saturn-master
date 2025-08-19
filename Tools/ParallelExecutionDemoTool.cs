using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Saturn.Tools.Core;
using Saturn.Core.Performance;

namespace Saturn.Tools
{
    /// <summary>
    /// Demonstration tool showcasing Saturn's ParallelExecutor patterns including CPU-intensive,
    /// I/O-intensive, and dependency-aware parallel execution with performance benchmarking.
    /// </summary>
    public class ParallelExecutionDemoTool : ToolBase
    {
        private readonly ParallelExecutor _parallelExecutor;

        public ParallelExecutionDemoTool()
        {
            _parallelExecutor = new ParallelExecutor();
        }

        public override string Name => "parallel_execution_demo";

        public override string Description => @"Demonstrate Saturn's ParallelExecutor patterns with benchmarking and performance analysis.
Shows different execution modes: CPU-intensive, I/O-intensive, dependency-aware, and mixed workloads.

Demo types:
- cpu: CPU-intensive computational tasks (prime calculations, sorting)
- io: I/O-intensive operations (file reads, network requests simulation)
- dependency: Tasks with dependencies showing execution ordering
- mixed: Combined CPU and I/O operations
- benchmark: Performance comparison between serial and parallel execution
- stress: Stress test with configurable load patterns

Workload sizes:
- small: 10 operations, quick demonstration
- medium: 50 operations, realistic workload
- large: 200 operations, performance testing
- custom: User-specified operation count

Output includes:
- Execution times and performance metrics
- Thread utilization statistics
- Throughput and efficiency analysis
- Resource usage patterns
- Recommendations for optimization

Examples:
- Quick demo: demoType='cpu', workloadSize='small'
- I/O benchmark: demoType='io', workloadSize='medium', includeBenchmark=true
- Complex workflow: demoType='dependency', workloadSize='large'
- Stress test: demoType='stress', customCount=500";

        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                { "demoType", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "Type of parallel execution demo: cpu, io, dependency, mixed, benchmark, stress" },
                        { "enum", new[] { "cpu", "io", "dependency", "mixed", "benchmark", "stress" } }
                    }
                },
                { "workloadSize", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "Workload size: small, medium, large, custom" },
                        { "enum", new[] { "small", "medium", "large", "custom" } }
                    }
                },
                { "customCount", new Dictionary<string, object>
                    {
                        { "type", "integer" },
                        { "description", "Custom operation count (required when workloadSize='custom')" }
                    }
                },
                { "includeBenchmark", new Dictionary<string, object>
                    {
                        { "type", "boolean" },
                        { "description", "Include serial vs parallel performance comparison. Default is false" }
                    }
                },
                { "showDetails", new Dictionary<string, object>
                    {
                        { "type", "boolean" },
                        { "description", "Show detailed execution information. Default is true" }
                    }
                },
                { "maxConcurrency", new Dictionary<string, object>
                    {
                        { "type", "integer" },
                        { "description", "Override max concurrency for testing. Default uses ParallelExecutor default" }
                    }
                }
            };
        }

        protected override string[] GetRequiredParameters()
        {
            return new[] { "demoType" };
        }

        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            var demoType = GetParameter<string>(parameters, "demoType");
            var workloadSize = GetParameter<string>(parameters, "workloadSize", "medium");
            var customCount = GetParameter<int?>(parameters, "customCount", null);
            
            var size = workloadSize == "custom" && customCount.HasValue ? $"{customCount} ops" : workloadSize;
            return $"Parallel execution demo: {demoType} ({size})";
        }

        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            try
            {
                var demoType = GetParameter<string>(parameters, "demoType");
                var workloadSize = GetParameter<string>(parameters, "workloadSize", "medium");
                var customCount = GetParameter<int?>(parameters, "customCount", null);
                var includeBenchmark = GetParameter<bool>(parameters, "includeBenchmark", false);
                var showDetails = GetParameter<bool>(parameters, "showDetails", true);
                var maxConcurrency = GetParameter<int?>(parameters, "maxConcurrency", null);

                var operationCount = GetOperationCount(workloadSize, customCount);
                
                // Create executor with custom concurrency if specified
                using var executor = maxConcurrency.HasValue ? 
                    new ParallelExecutor(maxConcurrency) : 
                    new ParallelExecutor();

                var demoResult = await RunDemoAsync(demoType, operationCount, executor, includeBenchmark, showDetails);

                var result = new
                {
                    DemoType = demoType,
                    OperationCount = operationCount,
                    WorkloadSize = workloadSize,
                    MaxConcurrency = maxConcurrency ?? Environment.ProcessorCount * 2,
                    Results = demoResult,
                    Timestamp = DateTime.UtcNow
                };

                return CreateSuccessResult(result, demoResult.Summary);
            }
            catch (Exception ex)
            {
                return CreateErrorResult($"Parallel execution demo failed: {ex.Message}");
            }
        }

        private async Task<DemoResult> RunDemoAsync(string demoType, int operationCount, ParallelExecutor executor, bool includeBenchmark, bool showDetails)
        {
            return demoType.ToLowerInvariant() switch
            {
                "cpu" => await RunCpuIntensiveDemoAsync(operationCount, executor, includeBenchmark, showDetails),
                "io" => await RunIoIntensiveDemoAsync(operationCount, executor, includeBenchmark, showDetails),
                "dependency" => await RunDependencyDemoAsync(operationCount, executor, showDetails),
                "mixed" => await RunMixedWorkloadDemoAsync(operationCount, executor, includeBenchmark, showDetails),
                "benchmark" => await RunBenchmarkDemoAsync(operationCount, executor, showDetails),
                "stress" => await RunStressTestDemoAsync(operationCount, executor, showDetails),
                _ => throw new ArgumentException($"Unknown demo type: {demoType}")
            };
        }

        private async Task<DemoResult> RunCpuIntensiveDemoAsync(int operationCount, ParallelExecutor executor, bool includeBenchmark, bool showDetails)
        {
            var result = new DemoResult { DemoType = "CPU-Intensive Operations" };
            
            // Generate CPU-intensive operations (prime calculations)
            var operations = Enumerable.Range(1, operationCount)
                .Select(i => new Func<CancellationToken, Task<int>>(ct => 
                    executor.ExecuteCpuIntensiveAsync(_ => CalculateNthPrime(100 + i * 10), ct)))
                .ToArray();

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var initialMetrics = executor.GetMetrics();

            var results = await executor.ExecuteParallelAsync(operations);

            stopwatch.Stop();
            var finalMetrics = executor.GetMetrics();

            result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            result.OperationsCompleted = results.Length;
            result.ThroughputOpsPerSec = results.Length / (stopwatch.ElapsedMilliseconds / 1000.0);
            result.MetricsDelta = CalculateMetricsDelta(initialMetrics, finalMetrics);

            if (includeBenchmark)
            {
                result.BenchmarkComparison = await RunSerialBenchmarkAsync(operations.Take(Math.Min(20, operationCount)).ToArray());
            }

            result.Summary = FormatCpuDemoSummary(result, showDetails);
            return result;
        }

        private async Task<DemoResult> RunIoIntensiveDemoAsync(int operationCount, ParallelExecutor executor, bool includeBenchmark, bool showDetails)
        {
            var result = new DemoResult { DemoType = "I/O-Intensive Operations" };
            
            // Generate I/O-intensive operations (simulated file operations)
            var operations = Enumerable.Range(1, operationCount)
                .Select(i => new Func<CancellationToken, Task<string>>(ct => 
                    executor.ExecuteIoIntensiveAsync(async _ => 
                    {
                        await Task.Delay(Random.Shared.Next(10, 50), ct); // Simulate I/O delay
                        return $"Operation_{i}_Result";
                    }, ct)))
                .ToArray();

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var initialMetrics = executor.GetMetrics();

            var results = await executor.ExecuteParallelAsync(operations);

            stopwatch.Stop();
            var finalMetrics = executor.GetMetrics();

            result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            result.OperationsCompleted = results.Length;
            result.ThroughputOpsPerSec = results.Length / (stopwatch.ElapsedMilliseconds / 1000.0);
            result.MetricsDelta = CalculateMetricsDelta(initialMetrics, finalMetrics);

            if (includeBenchmark)
            {
                result.BenchmarkComparison = await RunSerialBenchmarkAsync(operations.Take(Math.Min(20, operationCount)).ToArray());
            }

            result.Summary = FormatIoDemoSummary(result, showDetails);
            return result;
        }

        private async Task<DemoResult> RunDependencyDemoAsync(int operationCount, ParallelExecutor executor, bool showDetails)
        {
            var result = new DemoResult { DemoType = "Dependency-Aware Execution" };
            
            // Create a dependency graph: A -> B,C -> D -> E
            var operations = new List<ParallelOperation<string>>();
            
            for (int batch = 0; batch < operationCount / 4; batch++)
            {
                var batchPrefix = $"batch_{batch}";
                
                operations.Add(new ParallelOperation<string>
                {
                    Id = $"{batchPrefix}_A",
                    Dependencies = Array.Empty<string>(),
                    Execute = async (deps, ct) =>
                    {
                        await Task.Delay(50, ct); // Simulate work
                        return $"{batchPrefix}_A_completed";
                    }
                });

                operations.Add(new ParallelOperation<string>
                {
                    Id = $"{batchPrefix}_B",
                    Dependencies = new[] { $"{batchPrefix}_A" },
                    Execute = async (deps, ct) =>
                    {
                        await Task.Delay(30, ct);
                        return $"{batchPrefix}_B_completed_after_{deps[0]}";
                    }
                });

                operations.Add(new ParallelOperation<string>
                {
                    Id = $"{batchPrefix}_C",
                    Dependencies = new[] { $"{batchPrefix}_A" },
                    Execute = async (deps, ct) =>
                    {
                        await Task.Delay(40, ct);
                        return $"{batchPrefix}_C_completed_after_{deps[0]}";
                    }
                });

                operations.Add(new ParallelOperation<string>
                {
                    Id = $"{batchPrefix}_D",
                    Dependencies = new[] { $"{batchPrefix}_B", $"{batchPrefix}_C" },
                    Execute = async (deps, ct) =>
                    {
                        await Task.Delay(20, ct);
                        return $"{batchPrefix}_D_completed_after_[{string.Join(",", deps)}]";
                    }
                });
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var initialMetrics = executor.GetMetrics();

            var results = await executor.ExecuteParallelWithDependenciesAsync(operations);

            stopwatch.Stop();
            var finalMetrics = executor.GetMetrics();

            result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            result.OperationsCompleted = results.Length;
            result.ThroughputOpsPerSec = results.Length / (stopwatch.ElapsedMilliseconds / 1000.0);
            result.MetricsDelta = CalculateMetricsDelta(initialMetrics, finalMetrics);

            result.Summary = FormatDependencyDemoSummary(result, operations.Count, showDetails);
            return result;
        }

        private async Task<DemoResult> RunMixedWorkloadDemoAsync(int operationCount, ParallelExecutor executor, bool includeBenchmark, bool showDetails)
        {
            var result = new DemoResult { DemoType = "Mixed CPU/I/O Workload" };
            
            var operations = new List<Func<CancellationToken, Task<object>>>();
            
            // Mix of CPU and I/O operations
            for (int i = 0; i < operationCount; i++)
            {
                if (i % 3 == 0) // CPU-intensive
                {
                    operations.Add(ct => executor.ExecuteCpuIntensiveAsync<object>(_ => CalculateNthPrime(50 + i), ct));
                }
                else // I/O-intensive
                {
                    operations.Add(ct => executor.ExecuteIoIntensiveAsync<object>(async _ =>
                    {
                        await Task.Delay(Random.Shared.Next(5, 25), ct);
                        return $"IO_Operation_{i}";
                    }, ct));
                }
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var initialMetrics = executor.GetMetrics();

            var results = await executor.ExecuteParallelAsync(operations.ToArray());

            stopwatch.Stop();
            var finalMetrics = executor.GetMetrics();

            result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            result.OperationsCompleted = results.Length;
            result.ThroughputOpsPerSec = results.Length / (stopwatch.ElapsedMilliseconds / 1000.0);
            result.MetricsDelta = CalculateMetricsDelta(initialMetrics, finalMetrics);

            if (includeBenchmark)
            {
                result.BenchmarkComparison = await RunSerialBenchmarkAsync(operations.Take(Math.Min(20, operationCount)).ToArray());
            }

            result.Summary = FormatMixedDemoSummary(result, showDetails);
            return result;
        }

        private async Task<DemoResult> RunBenchmarkDemoAsync(int operationCount, ParallelExecutor executor, bool showDetails)
        {
            var result = new DemoResult { DemoType = "Serial vs Parallel Benchmark" };
            
            var operations = Enumerable.Range(1, operationCount)
                .Select(i => new Func<CancellationToken, Task<int>>(ct => 
                    Task.FromResult(CalculateNthPrime(20 + i))))
                .ToArray();

            // Parallel execution
            var parallelStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var parallelResults = await executor.ExecuteParallelAsync(operations);
            parallelStopwatch.Stop();

            // Serial execution
            var serialStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var serialResults = new int[operations.Length];
            for (int i = 0; i < operations.Length; i++)
            {
                serialResults[i] = await operations[i](CancellationToken.None);
            }
            serialStopwatch.Stop();

            result.ExecutionTimeMs = parallelStopwatch.ElapsedMilliseconds;
            result.OperationsCompleted = parallelResults.Length;
            result.ThroughputOpsPerSec = parallelResults.Length / (parallelStopwatch.ElapsedMilliseconds / 1000.0);

            var speedup = (double)serialStopwatch.ElapsedMilliseconds / parallelStopwatch.ElapsedMilliseconds;
            var efficiency = speedup / Environment.ProcessorCount;

            result.BenchmarkComparison = new BenchmarkResult
            {
                SerialTimeMs = serialStopwatch.ElapsedMilliseconds,
                ParallelTimeMs = parallelStopwatch.ElapsedMilliseconds,
                SpeedupFactor = speedup,
                EfficiencyPercent = efficiency * 100
            };

            result.Summary = FormatBenchmarkSummary(result, showDetails);
            return result;
        }

        private async Task<DemoResult> RunStressTestDemoAsync(int operationCount, ParallelExecutor executor, bool showDetails)
        {
            var result = new DemoResult { DemoType = "Stress Test" };
            
            // Create a large number of mixed operations
            var operations = new List<Func<CancellationToken, Task<object>>>();
            
            for (int i = 0; i < operationCount; i++)
            {
                var opType = i % 4;
                switch (opType)
                {
                    case 0: // Quick CPU
                        operations.Add(ct => executor.ExecuteCpuIntensiveAsync<object>(_ => i * i, ct));
                        break;
                    case 1: // Short I/O
                        operations.Add(ct => executor.ExecuteIoIntensiveAsync<object>(async _ =>
                        {
                            await Task.Delay(1, ct);
                            return $"Quick_IO_{i}";
                        }, ct));
                        break;
                    case 2: // Medium CPU
                        operations.Add(ct => executor.ExecuteCpuIntensiveAsync<object>(_ => CalculateNthPrime(10 + i % 20), ct));
                        break;
                    case 3: // Medium I/O
                        operations.Add(ct => executor.ExecuteIoIntensiveAsync<object>(async _ =>
                        {
                            await Task.Delay(Random.Shared.Next(5, 15), ct);
                            return $"Medium_IO_{i}";
                        }, ct));
                        break;
                }
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var initialMetrics = executor.GetMetrics();

            var results = await executor.ExecuteParallelAsync(operations.ToArray());

            stopwatch.Stop();
            var finalMetrics = executor.GetMetrics();

            result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            result.OperationsCompleted = results.Length;
            result.ThroughputOpsPerSec = results.Length / (stopwatch.ElapsedMilliseconds / 1000.0);
            result.MetricsDelta = CalculateMetricsDelta(initialMetrics, finalMetrics);

            result.Summary = FormatStressTestSummary(result, showDetails);
            return result;
        }

        private int GetOperationCount(string workloadSize, int? customCount)
        {
            if (workloadSize == "custom")
            {
                if (!customCount.HasValue || customCount <= 0)
                    throw new ArgumentException("Custom count must be positive when workloadSize is 'custom'");
                return customCount.Value;
            }

            return workloadSize.ToLowerInvariant() switch
            {
                "small" => 10,
                "medium" => 50,
                "large" => 200,
                _ => 50
            };
        }

        private int CalculateNthPrime(int n)
        {
            if (n < 1) return 2;
            
            var primes = new List<int> { 2 };
            int candidate = 3;
            
            while (primes.Count < n)
            {
                bool isPrime = true;
                foreach (int prime in primes)
                {
                    if (prime * prime > candidate) break;
                    if (candidate % prime == 0)
                    {
                        isPrime = false;
                        break;
                    }
                }
                
                if (isPrime) primes.Add(candidate);
                candidate += 2;
            }
            
            return primes[n - 1];
        }

        private MetricsDelta CalculateMetricsDelta(ThreadPoolMetrics initial, ThreadPoolMetrics final)
        {
            return new MetricsDelta
            {
                TasksExecutedDelta = final.TotalTasksExecuted - initial.TotalTasksExecuted,
                CpuTasksDelta = final.CpuIntensiveTasks - initial.CpuIntensiveTasks,
                IoTasksDelta = final.IoIntensiveTasks - initial.IoIntensiveTasks,
                PeakActiveThreads = Math.Max(final.ActiveThreads - initial.ActiveThreads, 0)
            };
        }

        private async Task<BenchmarkResult> RunSerialBenchmarkAsync<T>(Func<CancellationToken, Task<T>>[] operations)
        {
            var serialStopwatch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < operations.Length; i++)
            {
                await operations[i](CancellationToken.None);
            }
            serialStopwatch.Stop();

            var parallelStopwatch = System.Diagnostics.Stopwatch.StartNew();
            await _parallelExecutor.ExecuteParallelAsync(operations);
            parallelStopwatch.Stop();

            var speedup = (double)serialStopwatch.ElapsedMilliseconds / parallelStopwatch.ElapsedMilliseconds;
            return new BenchmarkResult
            {
                SerialTimeMs = serialStopwatch.ElapsedMilliseconds,
                ParallelTimeMs = parallelStopwatch.ElapsedMilliseconds,
                SpeedupFactor = speedup,
                EfficiencyPercent = (speedup / Environment.ProcessorCount) * 100
            };
        }

        private string FormatCpuDemoSummary(DemoResult result, bool showDetails)
        {
            var summary = $@"🔧 CPU-INTENSIVE PARALLEL EXECUTION DEMO
Operations: {result.OperationsCompleted} prime calculations
Execution Time: {result.ExecutionTimeMs}ms
Throughput: {result.ThroughputOpsPerSec:F1} ops/sec
Tasks Executed: {result.MetricsDelta?.TasksExecutedDelta ?? 0}
Peak Active Threads: {result.MetricsDelta?.PeakActiveThreads ?? 0}";

            if (result.BenchmarkComparison != null)
            {
                summary += $@"

📊 PERFORMANCE COMPARISON:
Serial Time: {result.BenchmarkComparison.SerialTimeMs}ms
Parallel Time: {result.BenchmarkComparison.ParallelTimeMs}ms  
Speedup: {result.BenchmarkComparison.SpeedupFactor:F2}x
Efficiency: {result.BenchmarkComparison.EfficiencyPercent:F1}%";
            }

            return summary;
        }

        private string FormatIoDemoSummary(DemoResult result, bool showDetails)
        {
            return $@"💾 I/O-INTENSIVE PARALLEL EXECUTION DEMO
Operations: {result.OperationsCompleted} simulated I/O operations
Execution Time: {result.ExecutionTimeMs}ms
Throughput: {result.ThroughputOpsPerSec:F1} ops/sec  
I/O Tasks: {result.MetricsDelta?.IoTasksDelta ?? 0}
Peak Active Threads: {result.MetricsDelta?.PeakActiveThreads ?? 0}

✅ Demonstrates efficient async I/O handling without blocking threads";
        }

        private string FormatDependencyDemoSummary(DemoResult result, int totalOps, bool showDetails)
        {
            return $@"🔗 DEPENDENCY-AWARE PARALLEL EXECUTION DEMO
Operations: {result.OperationsCompleted} (from {totalOps} defined)  
Execution Time: {result.ExecutionTimeMs}ms
Throughput: {result.ThroughputOpsPerSec:F1} ops/sec
Tasks Executed: {result.MetricsDelta?.TasksExecutedDelta ?? 0}

✅ Demonstrates automatic dependency resolution and batch execution";
        }

        private string FormatMixedDemoSummary(DemoResult result, bool showDetails)
        {
            return $@"⚡ MIXED WORKLOAD PARALLEL EXECUTION DEMO
Operations: {result.OperationsCompleted} (CPU + I/O mixed)
Execution Time: {result.ExecutionTimeMs}ms  
Throughput: {result.ThroughputOpsPerSec:F1} ops/sec
CPU Tasks: {result.MetricsDelta?.CpuTasksDelta ?? 0}
I/O Tasks: {result.MetricsDelta?.IoTasksDelta ?? 0}

✅ Demonstrates optimal resource utilization for mixed workloads";
        }

        private string FormatBenchmarkSummary(DemoResult result, bool showDetails)
        {
            return $@"🏁 SERIAL vs PARALLEL BENCHMARK COMPARISON
Operations: {result.OperationsCompleted}
Serial Time: {result.BenchmarkComparison?.SerialTimeMs}ms
Parallel Time: {result.BenchmarkComparison?.ParallelTimeMs}ms
Speedup Factor: {result.BenchmarkComparison?.SpeedupFactor:F2}x
Efficiency: {result.BenchmarkComparison?.EfficiencyPercent:F1}%

CPU Cores Available: {Environment.ProcessorCount}
Theoretical Max Speedup: {Environment.ProcessorCount}x";
        }

        private string FormatStressTestSummary(DemoResult result, bool showDetails)
        {
            return $@"🔥 STRESS TEST RESULTS
Operations: {result.OperationsCompleted}
Execution Time: {result.ExecutionTimeMs}ms
Throughput: {result.ThroughputOpsPerSec:F1} ops/sec
Total Tasks: {result.MetricsDelta?.TasksExecutedDelta ?? 0}
Peak Threads: {result.MetricsDelta?.PeakActiveThreads ?? 0}

✅ System handled {result.OperationsCompleted} concurrent operations successfully";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _parallelExecutor?.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // Data models for demo results
    public class DemoResult
    {
        public string DemoType { get; set; } = string.Empty;
        public long ExecutionTimeMs { get; set; }
        public int OperationsCompleted { get; set; }
        public double ThroughputOpsPerSec { get; set; }
        public MetricsDelta? MetricsDelta { get; set; }
        public BenchmarkResult? BenchmarkComparison { get; set; }
        public string Summary { get; set; } = string.Empty;
    }

    public class MetricsDelta
    {
        public long TasksExecutedDelta { get; set; }
        public long CpuTasksDelta { get; set; }
        public long IoTasksDelta { get; set; }
        public long PeakActiveThreads { get; set; }
    }

    public class BenchmarkResult
    {
        public long SerialTimeMs { get; set; }
        public long ParallelTimeMs { get; set; }
        public double SpeedupFactor { get; set; }
        public double EfficiencyPercent { get; set; }
    }
}
