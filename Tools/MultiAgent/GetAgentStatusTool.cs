using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Saturn.Tools.Core;
using Saturn.Agents.MultiAgent;

namespace Saturn.Tools.MultiAgent
{
    public class GetAgentStatusTool : ToolBase, IDependencyInjectable
    {
        private AgentManager agentManager = null!;
        
        public void InjectDependencies(AgentManager agentManager)
        {
            this.agentManager = agentManager;
        }
        
        public override string Name => "get_agent_status";
        
        public override string Description => "Get status information for sub-agents";
        
        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                ["agent_id"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "ID of specific agent (optional - if not provided, returns all agents)"
                }
            };
        }
        
        protected override string[] GetRequiredParameters()
        {
            return new string[] { };
        }
        
        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            var agentId = GetParameter<string>(parameters, "agent_id", "");
            return string.IsNullOrEmpty(agentId) ? "Getting all agent statuses" : $"Getting status for: {agentId}";
        }
        
        public override Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            try
            {
                var agentId = parameters.ContainsKey("agent_id") ? parameters["agent_id"]?.ToString() : null;
                
                if (string.IsNullOrEmpty(agentId))
                {
                    var allStatuses = agentManager.GetAllAgentStatuses();
                    
                    if (!allStatuses.Any())
                    {
                        return Task.FromResult(CreateSuccessResult(
                            new Dictionary<string, object>
                            {
                                ["agents"] = new List<object>(),
                                ["count"] = 0,
                                ["capacity"] = $"0/{agentManager.GetMaxConcurrentAgents()}"
                            },
                            "No agents are currently running"
                        ));
                    }
                    
                    var agentData = allStatuses.Select(status => new Dictionary<string, object>
                    {
                        ["agent_id"] = status.AgentId,
                        ["name"] = status.Name,
                        ["status"] = status.Status,
                        ["current_task"] = status.CurrentTask ?? "None",
                        ["task_id"] = status.TaskId ?? "None",
                        ["is_idle"] = status.IsIdle,
                        ["running_time_seconds"] = status.RunningTime.TotalSeconds
                    }).ToList();
                    
                    var currentCount = agentManager.GetCurrentAgentCount();
                    var maxCount = agentManager.GetMaxConcurrentAgents();
                    
                    return Task.FromResult(CreateSuccessResult(
                        new Dictionary<string, object>
                        {
                            ["agents"] = agentData,
                            ["count"] = currentCount,
                            ["capacity"] = $"{currentCount}/{maxCount}",
                            ["idle_count"] = allStatuses.Count(a => a.IsIdle),
                            ["working_count"] = allStatuses.Count(a => !a.IsIdle)
                        },
                        $"Found {currentCount} agents ({allStatuses.Count(a => a.IsIdle)} idle, {allStatuses.Count(a => !a.IsIdle)} working)"
                    ));
                }
                else
                {
                    var status = agentManager.GetAgentStatus(agentId!);
                    
                    if (!status.Exists)
                    {
                        return Task.FromResult(CreateErrorResult($"Agent with ID '{agentId}' not found"));
                    }
                    
                    return Task.FromResult(CreateSuccessResult(
                        new Dictionary<string, object>
                        {
                            ["agent_id"] = status.AgentId,
                            ["name"] = status.Name,
                            ["status"] = status.Status,
                            ["current_task"] = status.CurrentTask ?? "None",
                            ["task_id"] = status.TaskId ?? "None",
                            ["is_idle"] = status.IsIdle,
                            ["running_time_seconds"] = status.RunningTime.TotalSeconds
                        },
                        $"Agent '{status.Name}' ({status.AgentId}) is {status.Status.ToLower()}"
                    ));
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateErrorResult($"Failed to get agent status: {ex.Message}"));
            }
        }
    }
}