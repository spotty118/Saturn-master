using Saturn.Agents.Core;
using Saturn.Core.Performance;
using Saturn.Tools.Core;

namespace Saturn.Agents
{
    public class Agent : AgentBase
    {
        public Agent(AgentConfiguration configuration, ToolRegistry? toolRegistry = null, ParallelExecutor? parallelExecutor = null)
            : base(configuration, toolRegistry, parallelExecutor)
        {
        }
    }
}