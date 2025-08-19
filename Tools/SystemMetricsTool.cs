using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Saturn.Core.Performance;
using Saturn.Tools.Core;

namespace Saturn.Tools
{
    /// <summary>
    /// Tool for monitoring system performance and multi-threading utilization
    /// </summary>
    public class SystemMetricsTool : ToolBase
    {
        private readonly ParallelExecutor? _parallelExecutor;
        
        public override string Name => "system_metrics";
        public override string Description => "Monitor system performance, CPU utilization, and multi-threading metrics";
        
        public SystemMetricsTool(ParallelExecutor? parallelExecutor = null)
        {
            _parallelExecutor = parallelExecutor;
        }
        
        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            var includeThreadPool = GetParameter<bool>(parameters, "includeThreadPool", true);
            var includeProcess = GetParameter<bool>(parameters, "includeProcess", true);
            var includeSystem = GetParameter<bool>(parameters, "includeSystem", true);
            
            try
            {
                var metrics = new Dictionary<string, object>();
                
                if (includeSystem)
                {
                    metrics["system"] = GetSystemMetrics();
                }
                
                if (includeProcess)
                {
                    metrics["process"] = await GetProcessMetricsAsync();
                }
                
                if (includeThreadPool && _parallelExecutor != null)
                {
                    metrics["threadPool"] = _parallelExecutor.GetSystemMetrics();
                    metrics["parallelExecutor"] = _parallelExecutor.GetMetrics();
                }
                
                return CreateSuccessResult(metrics, FormatMetricsOutput(metrics));
            }
            catch (Exception ex)
            {
                return CreateErrorResult($"Failed to collect system metrics: {ex.Message}");
            }
        }
        
        private object GetSystemMetrics()
        {
            return new
            {
                ProcessorCount = Environment.ProcessorCount,
                Is64BitOperatingSystem = Environment.Is64BitOperatingSystem,
                Is64BitProcess = Environment.Is64BitProcess,
                MachineName = Environment.MachineName,
                OSVersion = Environment.OSVersion.ToString(),
                SystemPageSize = Environment.SystemPageSize,
                TickCount = Environment.TickCount64,
                WorkingSet = Environment.WorkingSet
            };
        }
        
        private async Task<object> GetProcessMetricsAsync()
        {
            return await Task.Run(() =>
            {
                using var process = Process.GetCurrentProcess();
                return new
                {
                    ProcessId = process.Id,
                    ProcessName = process.ProcessName,
                    StartTime = process.StartTime,
                    TotalProcessorTime = process.TotalProcessorTime,
                    UserProcessorTime = process.UserProcessorTime,
                    PrivilegedProcessorTime = process.PrivilegedProcessorTime,
                    WorkingSet64 = process.WorkingSet64,
                    VirtualMemorySize64 = process.VirtualMemorySize64,
                    PrivateMemorySize64 = process.PrivateMemorySize64,
                    PagedMemorySize64 = process.PagedMemorySize64,
                    NonpagedSystemMemorySize64 = process.NonpagedSystemMemorySize64,
                    PagedSystemMemorySize64 = process.PagedSystemMemorySize64,
                    Threads = process.Threads.Count,
                    HandleCount = process.HandleCount
                };
            });
        }
        
        private string FormatMetricsOutput(Dictionary<string, object> metrics)
        {
            var output = new System.Text.StringBuilder();
            output.AppendLine("🖥️  SYSTEM PERFORMANCE METRICS");
            output.AppendLine("=" + new string('=', 50));
            
            if (metrics.ContainsKey("system"))
            {
                var system = metrics["system"];
                output.AppendLine($"💻 System Information:");
                output.AppendLine($"   • CPU Cores: {Environment.ProcessorCount}");
                output.AppendLine($"   • Architecture: {(Environment.Is64BitProcess ? "64-bit" : "32-bit")}");
                output.AppendLine($"   • OS: {Environment.OSVersion}");
                output.AppendLine($"   • Working Set: {Environment.WorkingSet / 1024 / 1024:N0} MB");
                output.AppendLine();
            }
            
            if (metrics.ContainsKey("threadPool") && metrics["threadPool"] is SystemUtilizationMetrics threadPool)
            {
                output.AppendLine($"🧵 Multi-Threading Utilization:");
                output.AppendLine($"   • Active Threads: {threadPool.ActiveThreads}");
                output.AppendLine($"   • Max Concurrency: {threadPool.MaxConcurrency}");
                output.AppendLine($"   • ThreadPool Usage: {threadPool.ThreadPoolUtilization:F1}%");
                output.AppendLine($"   • CPU Tasks: {threadPool.CpuIntensiveTasks}");
                output.AppendLine($"   • I/O Tasks: {threadPool.IoIntensiveTasks}");
                output.AppendLine($"   • Multi-Core Utilized: {(threadPool.IsMultiCoreUtilized ? "✅ Yes" : "❌ No")}");
                output.AppendLine($"   • Recommendation: {threadPool.GetRecommendation()}");
                output.AppendLine();
            }
            
            if (metrics.ContainsKey("process"))
            {
                output.AppendLine($"⚡ Process Performance:");
                // Add process-specific formatting here
                output.AppendLine();
            }
            
            return output.ToString();
        }
    }
}
