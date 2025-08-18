using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Saturn.Tools.Core;
using Saturn.Agents.MultiAgent;

namespace Saturn.Tools.MultiAgent
{
    public class WaitForAgentTool : ToolBase, IDependencyInjectable
    {
        private AgentManager agentManager = null!;
        
        public void InjectDependencies(AgentManager agentManager)
        {
            this.agentManager = agentManager;
        }
        
        public override string Name => "wait_for_agent";
        
        public override string Description => "Wait for one or more agents to complete their tasks";
        
        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                ["task_ids"] = new Dictionary<string, object>
                {
                    ["type"] = "array",
                    ["items"] = new Dictionary<string, object> { ["type"] = "string" },
                    ["description"] = "Array of task IDs to wait for"
                },
                ["timeout_ms"] = new Dictionary<string, object>
                {
                    ["type"] = "integer",
                    ["description"] = "Timeout in milliseconds (default: 60000)"
                }
            };
        }
        
        protected override string[] GetRequiredParameters()
        {
            return new[] { "task_ids" };
        }
        
        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            if (parameters.ContainsKey("task_ids") && parameters["task_ids"] is IEnumerable<object> taskIds)
            {
                var idList = taskIds.Cast<string>().ToList();
                var displayIds = idList.Count > 3 ? $"{string.Join(", ", idList.Take(3))}..." : string.Join(", ", idList);
                return $"Waiting for tasks: {displayIds}";
            }
            return "Waiting for tasks";
        }
        
        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            try
            {
                if (!(parameters["task_ids"] is IEnumerable<object> taskIdsObj))
                {
                    return CreateErrorResult("task_ids must be an array of strings");
                }
                
                var taskIds = taskIdsObj.Cast<string>().ToList();
                if (!taskIds.Any())
                {
                    return CreateErrorResult("At least one task ID must be provided");
                }
                
                var timeoutMs = parameters.ContainsKey("timeout_ms") && parameters["timeout_ms"] != null ? 
                    Convert.ToInt32(parameters["timeout_ms"]) : 60000;
                
                var startTime = DateTime.Now;
                var results = await agentManager.WaitForAllTasks(taskIds, timeoutMs);
                var elapsedMs = (DateTime.Now - startTime).TotalMilliseconds;
                
                var completedTasks = results.Where(r => r != null).ToList();
                var missingTasks = taskIds.Where(id => !completedTasks.Any(r => r.TaskId == id)).ToList();
                
                var resultData = completedTasks.Select(result => new Dictionary<string, object>
                {
                    ["task_id"] = result.TaskId,
                    ["agent_id"] = result.AgentId,
                    ["agent_name"] = result.AgentName,
                    ["success"] = result.Success,
                    ["result"] = result.Result,
                    ["duration_seconds"] = result.Duration.TotalSeconds
                }).ToList();
                
                var response = new Dictionary<string, object>
                {
                    ["completed_tasks"] = resultData,
                    ["completed_count"] = completedTasks.Count,
                    ["requested_count"] = taskIds.Count,
                    ["wait_time_ms"] = elapsedMs,
                    ["timed_out"] = completedTasks.Count < taskIds.Count
                };
                
                if (missingTasks.Any())
                {
                    response["missing_tasks"] = missingTasks;
                }
                
                var message = completedTasks.Count == taskIds.Count
                    ? $"All {taskIds.Count} tasks completed in {elapsedMs:F0}ms"
                    : $"Only {completedTasks.Count}/{taskIds.Count} tasks completed within timeout";
                
                return CreateSuccessResult(response, message);
            }
            catch (Exception ex)
            {
                return CreateErrorResult($"Failed to wait for agents: {ex.Message}");
            }
        }
    }
}