using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Saturn.Tools.Core;
using Saturn.Agents.MultiAgent;

namespace Saturn.Tools.MultiAgent
{
    public class TerminateAgentTool : ToolBase, IDependencyInjectable
    {
        private AgentManager agentManager = null!;
        
        public void InjectDependencies(AgentManager agentManager)
        {
            this.agentManager = agentManager;
        }
        
        public override string Name => "terminate_agent";
        
        public override string Description => "Terminate a specific sub-agent or all sub-agents";
        
        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                ["agent_id"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "ID of the agent to terminate (use 'all' to terminate all agents)"
                }
            };
        }
        
        protected override string[] GetRequiredParameters()
        {
            return new[] { "agent_id" };
        }
        
        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            var agentId = GetParameter<string>(parameters, "agent_id", "");
            return agentId == "all" ? "Terminating all agents" : $"Terminating agent: {agentId}";
        }
        
        public override Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            try
            {
                var agentId = parameters["agent_id"].ToString()!;
                
                if (agentId.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    var agents = agentManager.GetAllAgentStatuses();
                    
                    if (agents.Any())
                    {
                        var agentList = string.Join(", ", agents.Select(a => $"{a.Name} ({a.AgentId})"));
                        
                        agentManager.TerminateAllAgents();
                        
                        return Task.FromResult(CreateSuccessResult(
                            new Dictionary<string, object>
                            {
                                ["terminated_agents"] = agentList,
                                ["count"] = agents.Count
                            },
                            $"Terminated {agents.Count} agents: {agentList}"
                        ));
                    }
                    else
                    {
                        return Task.FromResult(CreateSuccessResult(
                            new Dictionary<string, object> { ["count"] = 0 },
                            "No agents were running"
                        ));
                    }
                }
                else
                {
                    agentManager.TerminateAgent(agentId);
                    
                    var currentCount = agentManager.GetCurrentAgentCount();
                    var maxCount = agentManager.GetMaxConcurrentAgents();
                    
                    return Task.FromResult(CreateSuccessResult(
                        new Dictionary<string, object>
                        {
                            ["agent_id"] = agentId,
                            ["remaining_agents"] = currentCount,
                            ["capacity"] = $"{currentCount}/{maxCount}"
                        },
                        $"Terminated agent {agentId}. Remaining agents: {currentCount}/{maxCount}"
                    ));
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateErrorResult($"Failed to terminate agent(s): {ex.Message}"));
            }
        }
    }
}