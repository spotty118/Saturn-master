namespace Saturn.Core.Performance
{
    /// <summary>
    /// Comprehensive system and thread pool utilization metrics for multi-threading analysis
    /// </summary>
    public class SystemUtilizationMetrics
    {
        /// <summary>
        /// Number of logical processors available to the system
        /// </summary>
        public int ProcessorCount { get; set; }
        
        /// <summary>
        /// Maximum configured concurrency for the ParallelExecutor
        /// </summary>
        public int MaxConcurrency { get; set; }
        
        /// <summary>
        /// Number of available worker threads in the ThreadPool
        /// </summary>
        public int AvailableWorkerThreads { get; set; }
        
        /// <summary>
        /// Maximum number of worker threads in the ThreadPool
        /// </summary>
        public int MaxWorkerThreads { get; set; }
        
        /// <summary>
        /// Number of available completion port threads in the ThreadPool
        /// </summary>
        public int AvailableCompletionPortThreads { get; set; }
        
        /// <summary>
        /// Maximum number of completion port threads in the ThreadPool
        /// </summary>
        public int MaxCompletionPortThreads { get; set; }
        
        /// <summary>
        /// Current number of active threads managed by ParallelExecutor
        /// </summary>
        public int ActiveThreads { get; set; }
        
        /// <summary>
        /// Current number of CPU-intensive tasks being executed
        /// </summary>
        public int CpuIntensiveTasks { get; set; }
        
        /// <summary>
        /// Current number of I/O-intensive tasks being executed
        /// </summary>
        public int IoIntensiveTasks { get; set; }
        
        /// <summary>
        /// ThreadPool utilization percentage (0-100)
        /// </summary>
        public double ThreadPoolUtilization { get; set; }
        
        /// <summary>
        /// Indicates if the system is effectively utilizing multiple CPU cores
        /// </summary>
        public bool IsMultiCoreUtilized => ActiveThreads > 1 && ProcessorCount > 1;
        
        /// <summary>
        /// Indicates if ThreadPool is under high utilization (>80%)
        /// </summary>
        public bool IsHighUtilization => ThreadPoolUtilization > 80.0;
        
        /// <summary>
        /// Recommended action based on current metrics
        /// </summary>
        public string GetRecommendation()
        {
            if (ThreadPoolUtilization > 90)
                return "High ThreadPool utilization detected. Consider reducing concurrency or optimizing operations.";
            
            if (ActiveThreads == 0 && ProcessorCount > 1)
                return "No parallel operations detected. Consider using ParallelExecutor for multi-core utilization.";
            
            if (ActiveThreads == 1 && ProcessorCount > 4)
                return "Single-threaded execution on multi-core system. Parallel processing could improve performance.";
            
            if (CpuIntensiveTasks > ProcessorCount * 2)
                return "Too many CPU-intensive tasks may cause thread contention. Consider task batching.";
            
            return "System utilization appears optimal for current workload.";
        }
        
        public override string ToString()
        {
            return $"CPU Cores: {ProcessorCount}, Active Threads: {ActiveThreads}, " +
                   $"ThreadPool Utilization: {ThreadPoolUtilization:F1}%, " +
                   $"CPU Tasks: {CpuIntensiveTasks}, I/O Tasks: {IoIntensiveTasks}";
        }
    }
}
