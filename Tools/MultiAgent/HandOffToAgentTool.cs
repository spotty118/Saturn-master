using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Saturn.Tools.Core;
using Saturn.Agents.MultiAgent;

namespace Saturn.Tools.MultiAgent
{
    public class HandOffToAgentTool : ToolBase, IDependencyInjectable
    {
        private AgentManager agentManager = null!;
        
        public void InjectDependencies(AgentManager agentManager)
        {
            this.agentManager = agentManager;
        }
        
        public override string Name => "hand_off_to_agent";
        
        public override string Description => "Hand off a task to an existing sub-agent";
        
        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                ["agent_id"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "ID of the agent to hand off the task to"
                },
                ["task"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Task description for the agent to execute"
                },
                ["context"] = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["description"] = "Optional context data to provide to the agent"
                }
            };
        }
        
        protected override string[] GetRequiredParameters()
        {
            return new[] { "agent_id", "task" };
        }
        
        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            var agentId = GetParameter<string>(parameters, "agent_id", "");
            var task = GetParameter<string>(parameters, "task", "");
            var displayTask = TruncateString(task, 40);
            return $"Hand off to {agentId}: {displayTask}";
        }
        
        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            try
            {
                var agentId = parameters["agent_id"].ToString()!;
                var task = parameters["task"].ToString()!;
                
                Dictionary<string, object>? context = null;
                if (parameters.ContainsKey("context") && parameters["context"] != null)
                {
                    if (parameters["context"] is Dictionary<string, object> contextDict)
                    {
                        context = contextDict;
                    }
                }

                var taskId = await agentManager.HandOffTask(agentId, task, context);

                return CreateSuccessResult(
                    new Dictionary<string, object>
                    {
                        ["task_id"] = taskId,
                        ["agent_id"] = agentId,
                        ["status"] = "handed_off"
                    },
                    $"Task handed off to agent {agentId}. Task ID: {taskId}"
                );
            }
            catch (InvalidOperationException ex)
            {
                return CreateErrorResult($"Cannot hand off task: {ex.Message}");
            }
            catch (Exception ex)
            {
                return CreateErrorResult($"Failed to hand off task: {ex.Message}");
            }
        }
    }
}