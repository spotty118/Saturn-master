using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Saturn.Tools.Core
{
    public class DiffPerformanceMetrics
    {
        public DateTime Timestamp { get; set; }
        public string Strategy { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public int FileSizeBytes { get; set; }
        public int ExecutionTimeMs { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public int OriginalLength { get; set; }
        public int UpdatedLength { get; set; }
        public bool FallbackUsed { get; set; }
        public string? FallbackReason { get; set; }
    }

    public class DiffPerformanceTracker
    {
        private readonly string _metricsPath;
        private readonly object _lock = new object();

        public DiffPerformanceTracker()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Saturn"
            );
            _metricsPath = Path.Combine(appDataPath, "diff-metrics.jsonl");
            
            // Ensure directory exists
            Directory.CreateDirectory(appDataPath);
        }

        public async Task RecordMetricAsync(DiffPerformanceMetrics metric)
        {
            try
            {
                var json = JsonSerializer.Serialize(metric);
                
                lock (_lock)
                {
                    File.AppendAllText(_metricsPath, json + Environment.NewLine);
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                // Don't fail the operation if metrics recording fails
                Console.WriteLine($"Failed to record performance metric: {ex.Message}");
            }
        }

        public async Task<List<DiffPerformanceMetrics>> GetMetricsAsync(DateTime? since = null, int maxRecords = 1000)
        {
            var metrics = new List<DiffPerformanceMetrics>();

            try
            {
                if (!File.Exists(_metricsPath))
                    return metrics;

                var lines = await File.ReadAllLinesAsync(_metricsPath);
                var cutoff = since ?? DateTime.MinValue;

                // Read from the end to get most recent records first
                for (int i = lines.Length - 1; i >= 0 && metrics.Count < maxRecords; i--)
                {
                    if (string.IsNullOrWhiteSpace(lines[i]))
                        continue;

                    try
                    {
                        var metric = JsonSerializer.Deserialize<DiffPerformanceMetrics>(lines[i]);
                        if (metric != null && metric.Timestamp >= cutoff)
                        {
                            metrics.Add(metric);
                        }
                    }
                    catch
                    {
                        // Skip malformed lines
                    }
                }

                // Reverse to get chronological order
                metrics.Reverse();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to read performance metrics: {ex.Message}");
            }

            return metrics;
        }

        public async Task<DiffPerformanceReport> GenerateReportAsync(TimeSpan period)
        {
            var since = DateTime.UtcNow - period;
            var metrics = await GetMetricsAsync(since);

            var report = new DiffPerformanceReport
            {
                Period = period,
                TotalOperations = metrics.Count,
                SuccessRate = metrics.Count == 0 ? 0 : metrics.Count(m => m.Success) / (double)metrics.Count,
                StrategyBreakdown = new Dictionary<string, DiffStrategyStats>()
            };

            // Group by strategy and calculate stats
            var strategyGroups = metrics.GroupBy(m => m.Strategy);
            
            foreach (var group in strategyGroups)
            {
                var strategyMetrics = group.ToList();
                var successfulMetrics = strategyMetrics.Where(m => m.Success).ToList();

                var stats = new DiffStrategyStats
                {
                    TotalOperations = strategyMetrics.Count,
                    SuccessfulOperations = successfulMetrics.Count,
                    SuccessRate = strategyMetrics.Count == 0 ? 0 : successfulMetrics.Count / (double)strategyMetrics.Count,
                    AverageExecutionTimeMs = successfulMetrics.Any() ? (int)successfulMetrics.Average(m => m.ExecutionTimeMs) : 0,
                    MedianExecutionTimeMs = CalculateMedian(successfulMetrics.Select(m => m.ExecutionTimeMs).ToList()),
                    FallbackRate = strategyMetrics.Count == 0 ? 0 : strategyMetrics.Count(m => m.FallbackUsed) / (double)strategyMetrics.Count,
                    AverageFileSizeBytes = successfulMetrics.Any() ? (int)successfulMetrics.Average(m => m.FileSizeBytes) : 0
                };

                report.StrategyBreakdown[group.Key] = stats;
            }

            // Overall stats
            if (metrics.Any())
            {
                var successfulMetrics = metrics.Where(m => m.Success).ToList();
                report.AverageExecutionTimeMs = successfulMetrics.Any() ? (int)successfulMetrics.Average(m => m.ExecutionTimeMs) : 0;
                report.MedianExecutionTimeMs = CalculateMedian(successfulMetrics.Select(m => m.ExecutionTimeMs).ToList());
                report.TotalFallbacks = metrics.Count(m => m.FallbackUsed);
            }

            return report;
        }

        private int CalculateMedian(List<int> values)
        {
            if (!values.Any()) return 0;
            
            values.Sort();
            int count = values.Count;
            
            if (count % 2 == 0)
            {
                return (values[count / 2 - 1] + values[count / 2]) / 2;
            }
            else
            {
                return values[count / 2];
            }
        }

        public async Task CleanupOldMetricsAsync(TimeSpan retentionPeriod)
        {
            try
            {
                var cutoff = DateTime.UtcNow - retentionPeriod;
                var metrics = await GetMetricsAsync();
                var recentMetrics = metrics.Where(m => m.Timestamp >= cutoff).ToList();

                // Rewrite file with only recent metrics
                lock (_lock)
                {
                    var lines = recentMetrics.Select(m => JsonSerializer.Serialize(m));
                    File.WriteAllLines(_metricsPath, lines);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to cleanup old metrics: {ex.Message}");
            }
        }
    }

    public class DiffPerformanceReport
    {
        public TimeSpan Period { get; set; }
        public int TotalOperations { get; set; }
        public double SuccessRate { get; set; }
        public int AverageExecutionTimeMs { get; set; }
        public int MedianExecutionTimeMs { get; set; }
        public int TotalFallbacks { get; set; }
        public Dictionary<string, DiffStrategyStats> StrategyBreakdown { get; set; } = new();

        public override string ToString()
        {
            var report = $@"Diff Performance Report ({Period.TotalDays:F1} days)
======================================
Total Operations: {TotalOperations}
Success Rate: {SuccessRate:P1}
Average Execution: {AverageExecutionTimeMs}ms
Median Execution: {MedianExecutionTimeMs}ms
Total Fallbacks: {TotalFallbacks}

Strategy Breakdown:";

            foreach (var kvp in StrategyBreakdown)
            {
                var stats = kvp.Value;
                report += $@"
{kvp.Key.ToUpper()}:
  Operations: {stats.TotalOperations} (Success: {stats.SuccessRate:P1})
  Avg Time: {stats.AverageExecutionTimeMs}ms
  Fallback Rate: {stats.FallbackRate:P1}
  Avg File Size: {FormatByteSize(stats.AverageFileSizeBytes)}";
            }

            return report;
        }

        private string FormatByteSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double size = bytes;
            int order = 0;
            
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            
            return $"{size:0.##} {sizes[order]}";
        }
    }

    public class DiffStrategyStats
    {
        public int TotalOperations { get; set; }
        public int SuccessfulOperations { get; set; }
        public double SuccessRate { get; set; }
        public int AverageExecutionTimeMs { get; set; }
        public int MedianExecutionTimeMs { get; set; }
        public double FallbackRate { get; set; }
        public int AverageFileSizeBytes { get; set; }
    }
}