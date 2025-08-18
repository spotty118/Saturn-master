using System.Threading.Tasks;
using Saturn.Agents.Core;
using Saturn.OpenRouter;
using Saturn.Tools.Core;

namespace Saturn.UI
{
    public interface IDialogManager
    {
        /// <summary>
        /// Shows the model selection dialog
        /// </summary>
        Task ShowModelSelectionDialogAsync();
        
        /// <summary>
        /// Shows the temperature configuration dialog
        /// </summary>
        void ShowTemperatureDialog();
        
        /// <summary>
        /// Shows the max tokens configuration dialog
        /// </summary>
        void ShowMaxTokensDialog();
        
        /// <summary>
        /// Shows the top P configuration dialog
        /// </summary>
        void ShowTopPDialog();
        
        /// <summary>
        /// Shows the max history messages configuration dialog
        /// </summary>
        void ShowMaxHistoryDialog();
        
        /// <summary>
        /// Shows the system prompt editor dialog
        /// </summary>
        void ShowSystemPromptDialog();
        
        /// <summary>
        /// Shows the current configuration display dialog
        /// </summary>
        void ShowConfigurationDialog();
        
        /// <summary>
        /// Shows the mode selection dialog
        /// </summary>
        Task ShowModeSelectionDialogAsync();
        
        /// <summary>
        /// Shows the tool selection dialog
        /// </summary>
        Task ShowToolSelectionDialogAsync();
        
        /// <summary>
        /// Shows the load chat dialog
        /// </summary>
        Task ShowLoadChatDialogAsync();
        
        /// <summary>
        /// Initializes the dialog manager with required dependencies
        /// </summary>
        void Initialize(OpenRouterClient openRouterClient, ToolRegistry toolRegistry);
        
        /// <summary>
        /// Sets the current configuration for dialogs to use
        /// </summary>
        void SetCurrentConfiguration(UIAgentConfiguration config);
        
        /// <summary>
        /// Event raised when configuration changes are made through dialogs
        /// </summary>
        event System.Action<UIAgentConfiguration> ConfigurationChanged;
        
        /// <summary>
        /// Event raised when a chat session should be loaded
        /// </summary>
        event System.Action<string> LoadChatRequested;
    }
}