using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Saturn.Tools.Core;
using Saturn.Agents.MultiAgent;

namespace Saturn.Tools.MultiAgent
{
    public class GetTaskResultTool : ToolBase, IDependencyInjectable
    {
        private AgentManager agentManager = null!;
        
        public void InjectDependencies(AgentManager agentManager)
        {
            this.agentManager = agentManager;
        }
        
        public override string Name => "get_task_result";
        
        public override string Description => "Get the result of a completed task";
        
        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                ["task_id"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "ID of the task to get results for"
                }
            };
        }
        
        protected override string[] GetRequiredParameters()
        {
            return new[] { "task_id" };
        }
        
        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            var taskId = GetParameter<string>(parameters, "task_id", "");
            return $"Getting result for task: {taskId}";
        }
        
        public override Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            try
            {
                var taskId = parameters["task_id"].ToString()!;
                var result = agentManager.GetTaskResult(taskId);
                
                if (result == null)
                {
                    return Task.FromResult(CreateErrorResult($"Task '{taskId}' not found or not yet completed"));
                }
                
                return Task.FromResult(CreateSuccessResult(
                    new Dictionary<string, object>
                    {
                        ["task_id"] = result.TaskId,
                        ["agent_id"] = result.AgentId,
                        ["agent_name"] = result.AgentName,
                        ["success"] = result.Success,
                        ["result"] = result.Result,
                        ["completed_at"] = result.CompletedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                        ["duration_seconds"] = result.Duration.TotalSeconds
                    },
                    result.Success ? 
                        $"Task completed successfully by {result.AgentName}" : 
                        $"Task failed: {result.Result}"
                ));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateErrorResult($"Error getting task result: {ex.Message}"));
            }
        }
    }
}