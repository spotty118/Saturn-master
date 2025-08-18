using System.Threading.Tasks;
using Saturn.Agents;
using Saturn.Agents.Core;
using Saturn.Configuration;
using Saturn.OpenRouter;

namespace Saturn.UI
{
    public interface IAgentConfigurationManager
    {
        /// <summary>
        /// Gets the current agent configuration
        /// </summary>
        UIAgentConfiguration CurrentConfig { get; }
        
        /// <summary>
        /// Reconfigures the agent with current settings
        /// </summary>
        Task ReconfigureAgentAsync();
        
        /// <summary>
        /// Applies a mode to the current configuration
        /// </summary>
        void ApplyModeToConfiguration(Mode mode);
        
        /// <summary>
        /// Toggles streaming mode on/off
        /// </summary>
        Task ToggleStreamingAsync();
        
        /// <summary>
        /// Toggles maintain history on/off
        /// </summary>
        Task ToggleMaintainHistoryAsync();
        
        /// <summary>
        /// Toggles command approval requirement on/off
        /// </summary>
        Task ToggleCommandApprovalAsync();
        
        /// <summary>
        /// Updates the agent's model
        /// </summary>
        Task UpdateModelAsync(string model);
        
        /// <summary>
        /// Updates the agent's temperature
        /// </summary>
        Task UpdateTemperatureAsync(double temperature);
        
        /// <summary>
        /// Updates the agent's max tokens
        /// </summary>
        Task UpdateMaxTokensAsync(int maxTokens);
        
        /// <summary>
        /// Updates the agent's top P value
        /// </summary>
        Task UpdateTopPAsync(double topP);
        
        /// <summary>
        /// Updates the agent's max history messages
        /// </summary>
        Task UpdateMaxHistoryMessagesAsync(int maxHistoryMessages);
        
        /// <summary>
        /// Updates the agent's system prompt
        /// </summary>
        Task UpdateSystemPromptAsync(string systemPrompt);
        
        /// <summary>
        /// Updates the agent's tool configuration
        /// </summary>
        Task UpdateToolsAsync(List<string> toolNames);
        
        /// <summary>
        /// Initializes the configuration manager with required dependencies
        /// </summary>
        void Initialize(Agent agent, OpenRouterClient openRouterClient, ConfigurationManager configurationManager);
        
        /// <summary>
        /// Event raised when the agent configuration changes
        /// </summary>
        event System.Action<Agent> AgentReconfigured;
    }
}