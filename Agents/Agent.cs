using Saturn.Agents.Core;
using Saturn.Tools.Core;

namespace Saturn.Agents
{
    public class Agent : AgentBase
    {
        public Agent(AgentConfiguration configuration, ToolRegistry? toolRegistry = null) : base(configuration, toolRegistry)
        {
        }
    }
}