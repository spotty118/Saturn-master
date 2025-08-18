using Saturn.Agents.Core;

namespace Saturn.Tools.Core
{
    public static class AgentContext
    {
        private static readonly AsyncLocal<AgentConfiguration> _currentConfiguration = new();
        
        public static AgentConfiguration? CurrentConfiguration
        {
            get => _currentConfiguration.Value;
            set => _currentConfiguration.Value = value;
        }
        
        public static bool RequireCommandApproval => _currentConfiguration.Value?.RequireCommandApproval ?? true;
    }
}