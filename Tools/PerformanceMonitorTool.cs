using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Saturn.Tools.Core;
using Saturn.Core.Performance;

namespace Saturn.Tools
{
    /// <summary>
    /// Performance monitoring dashboard tool that aggregates and displays Saturn's performance metrics
    /// including diff operations, parallel execution stats, and system resource utilization.
    /// Demonstrates integration with Saturn's existing metrics infrastructure.
    /// </summary>
    public class PerformanceMonitorTool : ToolBase
    {
        private readonly ParallelExecutor _parallelExecutor;
        private readonly DiffPerformanceTracker _diffTracker;

        public PerformanceMonitorTool()
        {
            _parallelExecutor = new ParallelExecutor();
            _diffTracker = new DiffPerformanceTracker();
        }

        public override string Name => "performance_monitor";

        public override string Description => @"Comprehensive performance monitoring dashboard for Saturn operations. 
Aggregates and analyzes performance metrics from diff operations, parallel execution, and system resources.

Available reports:
- overview: High-level performance summary (default)
- diff: Detailed diff operation performance analysis  
- parallel: Thread pool and parallel execution metrics
- trending: Performance trends over time periods
- alerts: Performance issues and recommendations
- export: Export metrics data for external analysis

Time periods:
- 1h, 6h, 24h: Recent performance data
- 7d, 30d: Historical trending analysis
- all: Complete performance history

Output formats:
- dashboard: Formatted performance dashboard (default)
- json: Raw metrics data in JSON format
- csv: Tabular data for spreadsheet analysis
- alerts: Issues and recommendations only

Examples:
- Quick overview: report='overview', period='24h'
- Diff analysis: report='diff', period='7d', format='dashboard'  
- Export data: report='trending', format='csv', period='30d'
- Check alerts: report='alerts', format='dashboard'";

        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                { "report", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "Report type: overview, diff, parallel, trending, alerts, export" },
                        { "enum", new[] { "overview", "diff", "parallel", "trending", "alerts", "export" } }
                    }
                },
                { "period", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "Time period: 1h, 6h, 24h, 7d, 30d, all" },
                        { "enum", new[] { "1h", "6h", "24h", "7d", "30d", "all" } }
                    }
                },
                { "format", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "Output format: dashboard, json, csv, alerts" },
                        { "enum", new[] { "dashboard", "json", "csv", "alerts" } }
                    }
                },
                { "includeRecommendations", new Dictionary<string, object>
                    {
                        { "type", "boolean" },
                        { "description", "Include performance optimization recommendations. Default is true" }
                    }
                },
                { "refreshMetrics", new Dictionary<string, object>
                    {
                        { "type", "boolean" },
                        { "description", "Refresh all metrics before generating report. Default is false" }
                    }
                }
            };
        }

        protected override string[] GetRequiredParameters()
        {
            return Array.Empty<string>();
        }

        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            var report = GetParameter<string>(parameters, "report", "overview");
            var period = GetParameter<string>(parameters, "period", "24h");
            var format = GetParameter<string>(parameters, "format", "dashboard");
            
            return $"Performance {report} report ({period}, {format} format)";
        }

        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            try
            {
                var report = GetParameter<string>(parameters, "report", "overview");
                var period = GetParameter<string>(parameters, "period", "24h");
                var format = GetParameter<string>(parameters, "format", "dashboard");
                var includeRecommendations = GetParameter<bool>(parameters, "includeRecommendations", true);
                var refreshMetrics = GetParameter<bool>(parameters, "refreshMetrics", false);

                var timeSpan = ParseTimePeriod(period);
                var performanceData = await GatherPerformanceDataAsync(timeSpan, refreshMetrics);
                var output = await GenerateReportAsync(report, performanceData, format, includeRecommendations);

                var result = new
                {
                    ReportType = report,
                    Period = period,
                    Format = format,
                    Timestamp = DateTime.UtcNow,
                    MetricsSummary = GetMetricsSummary(performanceData),
                    PerformanceData = format == "json" ? performanceData : null
                };

                return CreateSuccessResult(result, output);
            }
            catch (Exception ex)
            {
                return CreateErrorResult($"Failed to generate performance report: {ex.Message}");
            }
        }

        private async Task<PerformanceData> GatherPerformanceDataAsync(TimeSpan period, bool refresh)
        {
            var data = new PerformanceData
            {
                CollectionTime = DateTime.UtcNow,
                Period = period
            };

            // Gather metrics in parallel using Saturn's ParallelExecutor
            var operations = new[]
            {
                new Func<CancellationToken, Task>(async _ => 
                {
                    data.DiffMetrics = await _diffTracker.GetMetricsAsync(DateTime.UtcNow - period);
                    data.DiffReport = await _diffTracker.GenerateReportAsync(period);
                }),
                new Func<CancellationToken, Task>(_ => 
                {
                    data.ThreadPoolMetrics = _parallelExecutor.GetMetrics();
                    return Task.CompletedTask;
                }),
                new Func<CancellationToken, Task>(_ => 
                {
                    data.SystemMetrics = GatherSystemMetrics();
                    return Task.CompletedTask;
                })
            };

            await _parallelExecutor.ExecuteParallelAsync(operations);

            if (refresh)
            {
                await RefreshMetricsAsync();
            }

            return data;
        }

        private async Task<string> GenerateReportAsync(string reportType, PerformanceData data, string format, bool includeRecommendations)
        {
            return reportType.ToLowerInvariant() switch
            {
                "overview" => await GenerateOverviewReportAsync(data, format, includeRecommendations),
                "diff" => GenerateDiffReport(data, format),
                "parallel" => GenerateParallelReport(data, format),
                "trending" => GenerateTrendingReport(data, format),
                "alerts" => GenerateAlertsReport(data, includeRecommendations),
                "export" => GenerateExportReport(data, format),
                _ => await GenerateOverviewReportAsync(data, format, includeRecommendations)
            };
        }

        private async Task<string> GenerateOverviewReportAsync(PerformanceData data, string format, bool includeRecommendations)
        {
            if (format == "json")
            {
                return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            }

            var sb = new StringBuilder();
            sb.AppendLine("╔══════════════════════════════════════════════════════════════╗");
            sb.AppendLine("║                    SATURN PERFORMANCE DASHBOARD              ║");
            sb.AppendLine("╠══════════════════════════════════════════════════════════════╣");
            sb.AppendLine($"║ Report Time: {data.CollectionTime:yyyy-MM-dd HH:mm:ss UTC}                    ║");
            sb.AppendLine($"║ Period: {FormatPeriod(data.Period)}                                          ║");
            sb.AppendLine("╚══════════════════════════════════════════════════════════════╝");
            sb.AppendLine();

            // Diff Operations Summary
            if (data.DiffReport != null)
            {
                sb.AppendLine("🔧 DIFF OPERATIONS");
                sb.AppendLine($"   Operations: {data.DiffReport.TotalOperations}");
                sb.AppendLine($"   Success Rate: {data.DiffReport.SuccessRate:P1}");
                sb.AppendLine($"   Avg Execution: {data.DiffReport.AverageExecutionTimeMs}ms");
                sb.AppendLine($"   Fallbacks: {data.DiffReport.TotalFallbacks}");
                sb.AppendLine();
            }

            // Parallel Execution Summary  
            if (data.ThreadPoolMetrics != null)
            {
                sb.AppendLine("⚡ PARALLEL EXECUTION");
                sb.AppendLine($"   Active Threads: {data.ThreadPoolMetrics.ActiveThreads}");
                sb.AppendLine($"   CPU Tasks: {data.ThreadPoolMetrics.CpuIntensiveTasks}");
                sb.AppendLine($"   I/O Tasks: {data.ThreadPoolMetrics.IoIntensiveTasks}");
                sb.AppendLine($"   Total Executed: {data.ThreadPoolMetrics.TotalTasksExecuted}");
                sb.AppendLine($"   Worker Threads: {data.ThreadPoolMetrics.WorkerThreads}");
                sb.AppendLine();
            }

            // System Resources
            if (data.SystemMetrics != null)
            {
                sb.AppendLine("💻 SYSTEM RESOURCES");
                sb.AppendLine($"   CPU Cores: {data.SystemMetrics.ProcessorCount}");
                sb.AppendLine($"   Working Set: {FormatBytes(data.SystemMetrics.WorkingSetBytes)}");
                sb.AppendLine($"   Private Memory: {FormatBytes(data.SystemMetrics.PrivateMemoryBytes)}");
                sb.AppendLine($"   Handle Count: {data.SystemMetrics.HandleCount}");
                sb.AppendLine();
            }

            // Performance Alerts
            if (includeRecommendations)
            {
                var alerts = AnalyzePerformanceIssues(data);
                if (alerts.Any())
                {
                    sb.AppendLine("⚠️  PERFORMANCE ALERTS");
                    foreach (var alert in alerts.Take(5))
                    {
                        sb.AppendLine($"   • {alert}");
                    }
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        private string GenerateDiffReport(PerformanceData data, string format)
        {
            if (format == "json")
            {
                return JsonSerializer.Serialize(data.DiffReport, new JsonSerializerOptions { WriteIndented = true });
            }

            if (data.DiffReport == null)
            {
                return "No diff operation data available for the specified period.";
            }

            var sb = new StringBuilder();
            sb.AppendLine("🔧 DIFF OPERATIONS PERFORMANCE REPORT");
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine(data.DiffReport.ToString());
            
            if (data.DiffMetrics?.Any() == true)
            {
                sb.AppendLine("\n📊 RECENT OPERATIONS:");
                foreach (var metric in data.DiffMetrics.Take(10))
                {
                    var status = metric.Success ? "✅" : "❌";
                    sb.AppendLine($"   {status} {metric.FileName} - {metric.ExecutionTimeMs}ms ({metric.Strategy})");
                }
            }

            return sb.ToString();
        }

        private string GenerateParallelReport(PerformanceData data, string format)
        {
            if (format == "json")
            {
                return JsonSerializer.Serialize(data.ThreadPoolMetrics, new JsonSerializerOptions { WriteIndented = true });
            }

            var sb = new StringBuilder();
            sb.AppendLine("⚡ PARALLEL EXECUTION REPORT");
            sb.AppendLine("═══════════════════════════");
            
            if (data.ThreadPoolMetrics != null)
            {
                var metrics = data.ThreadPoolMetrics;
                sb.AppendLine($"Active Threads: {metrics.ActiveThreads}");
                sb.AppendLine($"CPU-Intensive Tasks: {metrics.CpuIntensiveTasks}");
                sb.AppendLine($"I/O-Intensive Tasks: {metrics.IoIntensiveTasks}");
                sb.AppendLine($"Total Tasks Executed: {metrics.TotalTasksExecuted}");
                sb.AppendLine($"Available Worker Threads: {metrics.WorkerThreads}");
                sb.AppendLine($"Available Completion Port Threads: {metrics.CompletionPortThreads}");
                
                // Calculate efficiency metrics
                var totalActive = metrics.CpuIntensiveTasks + metrics.IoIntensiveTasks;
                var efficiency = totalActive > 0 ? (double)metrics.TotalTasksExecuted / totalActive : 0;
                sb.AppendLine($"Thread Pool Efficiency: {efficiency:F2}");
            }
            else
            {
                sb.AppendLine("No parallel execution metrics available.");
            }

            return sb.ToString();
        }

        private string GenerateTrendingReport(PerformanceData data, string format)
        {
            var sb = new StringBuilder();
            sb.AppendLine("📈 PERFORMANCE TRENDING ANALYSIS");
            sb.AppendLine("═══════════════════════════════");
            
            if (data.DiffMetrics?.Any() == true)
            {
                var recentMetrics = data.DiffMetrics.OrderBy(m => m.Timestamp).ToList();
                var hourlyGroups = recentMetrics.GroupBy(m => m.Timestamp.Hour).ToList();
                
                sb.AppendLine("HOURLY DIFF OPERATION TRENDS:");
                foreach (var group in hourlyGroups.OrderBy(g => g.Key))
                {
                    var avgTime = group.Average(m => m.ExecutionTimeMs);
                    var successRate = group.Count(m => m.Success) / (double)group.Count();
                    sb.AppendLine($"  Hour {group.Key:D2}: {group.Count()} ops, {avgTime:F1}ms avg, {successRate:P1} success");
                }
            }
            else
            {
                sb.AppendLine("Insufficient data for trending analysis.");
            }

            return sb.ToString();
        }

        private string GenerateAlertsReport(PerformanceData data, bool includeRecommendations)
        {
            var alerts = AnalyzePerformanceIssues(data);
            var sb = new StringBuilder();
            
            sb.AppendLine("⚠️  PERFORMANCE ALERTS & RECOMMENDATIONS");
            sb.AppendLine("═══════════════════════════════════════");
            
            if (!alerts.Any())
            {
                sb.AppendLine("✅ No performance issues detected.");
                return sb.ToString();
            }

            for (int i = 0; i < alerts.Count; i++)
            {
                sb.AppendLine($"{i + 1}. {alerts[i]}");
            }

            if (includeRecommendations)
            {
                sb.AppendLine("\n💡 OPTIMIZATION RECOMMENDATIONS:");
                sb.AppendLine("   • Monitor thread pool utilization during peak loads");
                sb.AppendLine("   • Consider increasing ParallelExecutor concurrency for I/O operations");
                sb.AppendLine("   • Review diff operation strategies for large files");
                sb.AppendLine("   • Implement performance metric alerts for automated monitoring");
            }

            return sb.ToString();
        }

        private string GenerateExportReport(PerformanceData data, string format)
        {
            if (format == "csv" && data.DiffMetrics?.Any() == true)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Timestamp,Strategy,FileName,FileSizeBytes,ExecutionTimeMs,Success,FallbackUsed");
                
                foreach (var metric in data.DiffMetrics)
                {
                    sb.AppendLine($"{metric.Timestamp:yyyy-MM-dd HH:mm:ss},{metric.Strategy},{metric.FileName},{metric.FileSizeBytes},{metric.ExecutionTimeMs},{metric.Success},{metric.FallbackUsed}");
                }
                
                return sb.ToString();
            }
            
            return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        }

        private List<string> AnalyzePerformanceIssues(PerformanceData data)
        {
            var issues = new List<string>();

            // Check diff operation performance
            if (data.DiffReport != null)
            {
                if (data.DiffReport.SuccessRate < 0.95)
                    issues.Add($"Low diff success rate: {data.DiffReport.SuccessRate:P1} (target: >95%)");

                if (data.DiffReport.AverageExecutionTimeMs > 1000)
                    issues.Add($"Slow diff operations: {data.DiffReport.AverageExecutionTimeMs}ms average (target: <1000ms)");

                if (data.DiffReport.TotalFallbacks > data.DiffReport.TotalOperations * 0.1)
                    issues.Add($"High fallback rate: {data.DiffReport.TotalFallbacks} fallbacks in {data.DiffReport.TotalOperations} operations");
            }

            // Check thread pool metrics
            if (data.ThreadPoolMetrics != null)
            {
                if (data.ThreadPoolMetrics.WorkerThreads < 5)
                    issues.Add("Low worker thread availability - consider reducing concurrent operations");

                if (data.ThreadPoolMetrics.ActiveThreads > Environment.ProcessorCount * 2)
                    issues.Add("High thread contention detected - review parallel execution strategy");
            }

            // Check system resources
            if (data.SystemMetrics != null)
            {
                if (data.SystemMetrics.WorkingSetBytes > 1_000_000_000) // 1GB
                    issues.Add($"High memory usage: {FormatBytes(data.SystemMetrics.WorkingSetBytes)}");

                if (data.SystemMetrics.HandleCount > 10000)
                    issues.Add($"High handle count: {data.SystemMetrics.HandleCount} (potential resource leak)");
            }

            return issues;
        }

        private SystemMetrics GatherSystemMetrics()
        {
            var process = System.Diagnostics.Process.GetCurrentProcess();
            return new SystemMetrics
            {
                ProcessorCount = Environment.ProcessorCount,
                WorkingSetBytes = process.WorkingSet64,
                PrivateMemoryBytes = process.PrivateMemorySize64,
                HandleCount = process.HandleCount,
                ThreadCount = process.Threads.Count
            };
        }

        private async Task RefreshMetricsAsync()
        {
            // Cleanup old metrics to keep data fresh
            await _diffTracker.CleanupOldMetricsAsync(TimeSpan.FromDays(30));
        }

        private TimeSpan ParseTimePeriod(string period)
        {
            return period.ToLowerInvariant() switch
            {
                "1h" => TimeSpan.FromHours(1),
                "6h" => TimeSpan.FromHours(6),
                "24h" => TimeSpan.FromHours(24),
                "7d" => TimeSpan.FromDays(7),
                "30d" => TimeSpan.FromDays(30),
                "all" => TimeSpan.FromDays(365),
                _ => TimeSpan.FromHours(24)
            };
        }

        private string FormatPeriod(TimeSpan period)
        {
            if (period.TotalDays >= 30) return "Last 30 days";
            if (period.TotalDays >= 7) return "Last 7 days";
            if (period.TotalDays >= 1) return "Last 24 hours";
            if (period.TotalHours >= 6) return "Last 6 hours";
            return "Last hour";
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            int order = 0;
            
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            
            return $"{size:0.##} {sizes[order]}";
        }

        private object GetMetricsSummary(PerformanceData data)
        {
            return new
            {
                DiffOperations = data.DiffReport?.TotalOperations ?? 0,
                DiffSuccessRate = data.DiffReport?.SuccessRate ?? 0,
                ActiveThreads = data.ThreadPoolMetrics?.ActiveThreads ?? 0,
                TotalTasksExecuted = data.ThreadPoolMetrics?.TotalTasksExecuted ?? 0,
                WorkingSetMB = (data.SystemMetrics?.WorkingSetBytes ?? 0) / (1024 * 1024),
                HasPerformanceIssues = AnalyzePerformanceIssues(data).Any()
            };
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

    // Data models for performance monitoring
    public class PerformanceData
    {
        public DateTime CollectionTime { get; set; }
        public TimeSpan Period { get; set; }
        public List<DiffPerformanceMetrics>? DiffMetrics { get; set; }
        public DiffPerformanceReport? DiffReport { get; set; }
        public ThreadPoolMetrics? ThreadPoolMetrics { get; set; }
        public SystemMetrics? SystemMetrics { get; set; }
    }

    public class SystemMetrics
    {
        public int ProcessorCount { get; set; }
        public long WorkingSetBytes { get; set; }
        public long PrivateMemoryBytes { get; set; }
        public int HandleCount { get; set; }
        public int ThreadCount { get; set; }
    }
}
