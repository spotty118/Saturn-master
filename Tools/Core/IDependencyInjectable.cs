using Saturn.Agents.MultiAgent;

namespace Saturn.Tools.Core
{
    public interface IDependencyInjectable
    {
        void InjectDependencies(AgentManager agentManager);
    }
}