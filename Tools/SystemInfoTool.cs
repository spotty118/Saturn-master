using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Saturn.Tools.Core;
using Saturn.Core.Performance;

namespace Saturn.Tools
{
    /// <summary>
    /// Tool for gathering comprehensive system information including hardware, OS, performance metrics,
    /// and Saturn-specific runtime data. Demonstrates Saturn's ITool interface and auto-discovery patterns.
    /// </summary>
    public class SystemInfoTool : ToolBase
    {
        private readonly ParallelExecutor _parallelExecutor;

        public SystemInfoTool()
        {
            _parallelExecutor = new ParallelExecutor();
        }

        public override string Name => "system_info";

        public override string Description => @"Gather comprehensive system information including hardware specs, OS details, 
performance metrics, and Saturn runtime data. Useful for debugging, performance analysis, and system monitoring.

Categories available:
- hardware: CPU, memory, disk information
- os: Operating system details and environment
- performance: Current system performance metrics
- saturn: Saturn-specific runtime information
- network: Network configuration and connectivity
- all: Complete system information (default)

Output formats:
- summary: Human-readable overview (default)
- detailed: Comprehensive technical details
- json: Structured JSON output for programmatic use

Examples:
- Get overview: category='all', format='summary'  
- CPU details: category='hardware', format='detailed'
- Performance data: category='performance', format='json'
- Saturn metrics: category='saturn', format='detailed'";

        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                { "category", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "Information category: hardware, os, performance, saturn, network, or all" },
                        { "enum", new[] { "hardware", "os", "performance", "saturn", "network", "all" } }
                    }
                },
                { "format", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "Output format: summary, detailed, or json" },
                        { "enum", new[] { "summary", "detailed", "json" } }
                    }
                },
                { "includeMetrics", new Dictionary<string, object>
                    {
                        { "type", "boolean" },
                        { "description", "Include real-time performance metrics. Default is true" }
                    }
                },
                { "refreshCache", new Dictionary<string, object>
                    {
                        { "type", "boolean" },
                        { "description", "Force refresh of cached system data. Default is false" }
                    }
                }
            };
        }

        protected override string[] GetRequiredParameters()
        {
            return Array.Empty<string>(); // All parameters are optional
        }

        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            var category = GetParameter<string>(parameters, "category", "all");
            var format = GetParameter<string>(parameters, "format", "summary");
            
            return $"Gathering {category} system info ({format} format)";
        }

        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            try
            {
                var category = GetParameter<string>(parameters, "category", "all");
                var format = GetParameter<string>(parameters, "format", "summary");
                var includeMetrics = GetParameter<bool>(parameters, "includeMetrics", true);
                var refreshCache = GetParameter<bool>(parameters, "refreshCache", false);

                var systemInfo = await GatherSystemInfoAsync(category, includeMetrics, refreshCache);
                var output = FormatOutput(systemInfo, format);

                var result = new
                {
                    Category = category,
                    Format = format,
                    Timestamp = DateTime.UtcNow,
                    SystemInfo = systemInfo,
                    Summary = GetSystemSummary(systemInfo)
                };

                return CreateSuccessResult(result, output);
            }
            catch (Exception ex)
            {
                return CreateErrorResult($"Failed to gather system information: {ex.Message}");
            }
        }

        private async Task<SystemInformation> GatherSystemInfoAsync(string category, bool includeMetrics, bool refreshCache)
        {
            var info = new SystemInformation();

            // Use Saturn's ParallelExecutor for efficient data gathering
            var operations = new List<Func<CancellationToken, Task>>();

            if (category == "all" || category == "hardware")
                operations.Add(_ => Task.Run(() => info.Hardware = GatherHardwareInfo()));

            if (category == "all" || category == "os")
                operations.Add(_ => Task.Run(() => info.OperatingSystem = GatherOSInfo()));

            if (category == "all" || category == "performance")
                operations.Add(_ => Task.Run(() => info.Performance = GatherPerformanceInfo(includeMetrics)));

            if (category == "all" || category == "saturn")
                operations.Add(_ => Task.Run(() => info.Saturn = GatherSaturnInfo()));

            if (category == "all" || category == "network")
                operations.Add(_ => Task.Run(() => info.Network = GatherNetworkInfo()));

            // Execute all operations in parallel using Saturn's ParallelExecutor
            await _parallelExecutor.ExecuteParallelAsync(operations.ToArray());

            return info;
        }

        private HardwareInfo GatherHardwareInfo()
        {
            var process = Process.GetCurrentProcess();
            
            return new HardwareInfo
            {
                ProcessorCount = Environment.ProcessorCount,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                OSArchitecture = RuntimeInformation.OSArchitecture.ToString(),
                WorkingSet = process.WorkingSet64,
                PrivateMemory = process.PrivateMemorySize64,
                VirtualMemory = process.VirtualMemorySize64,
                MachineName = Environment.MachineName,
                ProcessorInfo = GetProcessorInfo()
            };
        }

        private OSInfo GatherOSInfo()
        {
            return new OSInfo
            {
                Platform = Environment.OSVersion.Platform.ToString(),
                Version = Environment.OSVersion.VersionString,
                Framework = RuntimeInformation.FrameworkDescription,
                Runtime = RuntimeInformation.RuntimeIdentifier,
                Is64BitOS = Environment.Is64BitOperatingSystem,
                Is64BitProcess = Environment.Is64BitProcess,
                SystemDirectory = Environment.SystemDirectory,
                UserName = Environment.UserName,
                UserDomainName = Environment.UserDomainName,
                CurrentDirectory = Environment.CurrentDirectory
            };
        }

        private PerformanceInfo GatherPerformanceInfo(bool includeMetrics)
        {
            var process = Process.GetCurrentProcess();
            
            var info = new PerformanceInfo
            {
                ProcessId = process.Id,
                ProcessName = process.ProcessName,
                StartTime = process.StartTime,
                TotalProcessorTime = process.TotalProcessorTime,
                UserProcessorTime = process.UserProcessorTime,
                HandleCount = process.HandleCount,
                ThreadCount = process.Threads.Count,
                Uptime = DateTime.Now - process.StartTime
            };

            if (includeMetrics)
            {
                // Get Saturn's ParallelExecutor metrics if available
                try
                {
                    var metrics = _parallelExecutor.GetMetrics();
                    info.SaturnMetrics = new Dictionary<string, object>
                    {
                        { "ActiveThreads", metrics.ActiveThreads },
                        { "CpuIntensiveTasks", metrics.CpuIntensiveTasks },
                        { "IoIntensiveTasks", metrics.IoIntensiveTasks },
                        { "TotalTasksExecuted", metrics.TotalTasksExecuted },
                        { "WorkerThreads", metrics.WorkerThreads },
                        { "CompletionPortThreads", metrics.CompletionPortThreads }
                    };
                }
                catch
                {
                    // Ignore if metrics unavailable
                }
            }

            return info;
        }

        private SaturnInfo GatherSaturnInfo()
        {
            var assembly = typeof(SystemInfoTool).Assembly;
            
            return new SaturnInfo
            {
                AssemblyVersion = assembly.GetName().Version?.ToString() ?? "Unknown",
                AssemblyLocation = assembly.Location,
                CodeBase = assembly.CodeBase,
                CreatedTime = DateTime.UtcNow,
                ToolsNamespace = typeof(SystemInfoTool).Namespace ?? "Unknown",
                RuntimeVersion = RuntimeInformation.FrameworkDescription
            };
        }

        private NetworkInfo GatherNetworkInfo()
        {
            return new NetworkInfo
            {
                MachineName = Environment.MachineName,
                UserDomainName = Environment.UserDomainName,
                HasNetworkConnection = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable()
            };
        }

        private string GetProcessorInfo()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "Unknown Windows Processor";
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    if (File.Exists("/proc/cpuinfo"))
                    {
                        var cpuInfo = File.ReadAllLines("/proc/cpuinfo");
                        foreach (var line in cpuInfo)
                        {
                            if (line.StartsWith("model name"))
                            {
                                return line.Split(':')[1].Trim();
                            }
                        }
                    }
                    return "Unknown Linux Processor";
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    return "macOS Processor";
                }
            }
            catch
            {
                // Ignore errors in processor detection
            }

            return $"{Environment.ProcessorCount}-core processor";
        }

        private string FormatOutput(SystemInformation info, string format)
        {
            return format.ToLowerInvariant() switch
            {
                "json" => System.Text.Json.JsonSerializer.Serialize(info, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
                "detailed" => FormatDetailed(info),
                _ => FormatSummary(info)
            };
        }

        private string FormatSummary(SystemInformation info)
        {
            var lines = new List<string>
            {
                "=== SYSTEM INFORMATION SUMMARY ===",
                $"Machine: {info.Hardware?.MachineName ?? "Unknown"}",
                $"OS: {info.OperatingSystem?.Platform} {info.OperatingSystem?.Version}",
                $"Architecture: {info.Hardware?.Architecture} / {info.Hardware?.OSArchitecture}",
                $"Processors: {info.Hardware?.ProcessorCount ?? 0}",
                $"Framework: {info.OperatingSystem?.Framework}",
                $"Process: {info.Performance?.ProcessName} (PID: {info.Performance?.ProcessId})",
                $"Uptime: {FormatTimeSpan(info.Performance?.Uptime)}",
                $"Memory: {FormatBytes(info.Hardware?.WorkingSet ?? 0)} working set",
                $"Threads: {info.Performance?.ThreadCount ?? 0}",
                $"Saturn Version: {info.Saturn?.AssemblyVersion}"
            };

            if (info.Performance?.SaturnMetrics != null)
            {
                lines.Add("=== SATURN METRICS ===");
                foreach (var metric in info.Performance.SaturnMetrics)
                {
                    lines.Add($"{metric.Key}: {metric.Value}");
                }
            }

            return string.Join(Environment.NewLine, lines);
        }

        private string FormatDetailed(SystemInformation info)
        {
            var summary = FormatSummary(info);
            var detailed = new List<string> { summary, "", "=== DETAILED INFORMATION ===" };

            if (info.Hardware != null)
            {
                detailed.AddRange(new[]
                {
                    "HARDWARE:",
                    $"  Processor: {info.Hardware.ProcessorInfo}",
                    $"  Working Set: {FormatBytes(info.Hardware.WorkingSet)}",
                    $"  Private Memory: {FormatBytes(info.Hardware.PrivateMemory)}",
                    $"  Virtual Memory: {FormatBytes(info.Hardware.VirtualMemory)}",
                    ""
                });
            }

            if (info.OperatingSystem != null)
            {
                detailed.AddRange(new[]
                {
                    "OPERATING SYSTEM:",
                    $"  Platform: {info.OperatingSystem.Platform}",
                    $"  Version: {info.OperatingSystem.Version}",
                    $"  Framework: {info.OperatingSystem.Framework}",
                    $"  Runtime: {info.OperatingSystem.Runtime}",
                    $"  64-bit OS: {info.OperatingSystem.Is64BitOS}",
                    $"  64-bit Process: {info.OperatingSystem.Is64BitProcess}",
                    $"  User: {info.OperatingSystem.UserDomainName}\\{info.OperatingSystem.UserName}",
                    $"  Current Directory: {info.OperatingSystem.CurrentDirectory}",
                    ""
                });
            }

            if (info.Performance != null)
            {
                detailed.AddRange(new[]
                {
                    "PERFORMANCE:",
                    $"  Start Time: {info.Performance.StartTime:yyyy-MM-dd HH:mm:ss}",
                    $"  Total CPU Time: {FormatTimeSpan(info.Performance.TotalProcessorTime)}",
                    $"  User CPU Time: {FormatTimeSpan(info.Performance.UserProcessorTime)}",
                    $"  Handle Count: {info.Performance.HandleCount}",
                    ""
                });
            }

            return string.Join(Environment.NewLine, detailed);
        }

        private string GetSystemSummary(SystemInformation info)
        {
            return $"{info.Hardware?.MachineName} running {info.OperatingSystem?.Platform} " +
                   $"with {info.Hardware?.ProcessorCount} cores, uptime {FormatTimeSpan(info.Performance?.Uptime)}";
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

        private string FormatTimeSpan(TimeSpan? timeSpan)
        {
            if (!timeSpan.HasValue) return "Unknown";
            
            var ts = timeSpan.Value;
            if (ts.TotalDays >= 1)
                return $"{ts.Days}d {ts.Hours}h {ts.Minutes}m";
            else if (ts.TotalHours >= 1)
                return $"{ts.Hours}h {ts.Minutes}m {ts.Seconds}s";
            else
                return $"{ts.Minutes}m {ts.Seconds}s";
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

    // Data models for system information
    public class SystemInformation
    {
        public HardwareInfo? Hardware { get; set; }
        public OSInfo? OperatingSystem { get; set; }
        public PerformanceInfo? Performance { get; set; }
        public SaturnInfo? Saturn { get; set; }
        public NetworkInfo? Network { get; set; }
    }

    public class HardwareInfo
    {
        public int ProcessorCount { get; set; }
        public string? Architecture { get; set; }
        public string? OSArchitecture { get; set; }
        public long WorkingSet { get; set; }
        public long PrivateMemory { get; set; }
        public long VirtualMemory { get; set; }
        public string? MachineName { get; set; }
        public string? ProcessorInfo { get; set; }
    }

    public class OSInfo
    {
        public string? Platform { get; set; }
        public string? Version { get; set; }
        public string? Framework { get; set; }
        public string? Runtime { get; set; }
        public bool Is64BitOS { get; set; }
        public bool Is64BitProcess { get; set; }
        public string? SystemDirectory { get; set; }
        public string? UserName { get; set; }
        public string? UserDomainName { get; set; }
        public string? CurrentDirectory { get; set; }
    }

    public class PerformanceInfo
    {
        public int ProcessId { get; set; }
        public string? ProcessName { get; set; }
        public DateTime StartTime { get; set; }
        public TimeSpan TotalProcessorTime { get; set; }
        public TimeSpan UserProcessorTime { get; set; }
        public int HandleCount { get; set; }
        public int ThreadCount { get; set; }
        public TimeSpan? Uptime { get; set; }
        public Dictionary<string, object>? SaturnMetrics { get; set; }
    }

    public class SaturnInfo
    {
        public string? AssemblyVersion { get; set; }
        public string? AssemblyLocation { get; set; }
        public string? CodeBase { get; set; }
        public DateTime CreatedTime { get; set; }
        public string? ToolsNamespace { get; set; }
        public string? RuntimeVersion { get; set; }
    }

    public class NetworkInfo
    {
        public string? MachineName { get; set; }
        public string? UserDomainName { get; set; }
        public bool HasNetworkConnection { get; set; }
    }
}
