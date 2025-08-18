using System.Threading.Tasks;
using Saturn.Agents;
using Terminal.Gui;

namespace Saturn.UI
{
    public interface IChatSessionManager
    {
        /// <summary>
        /// Loads a chat session by ID
        /// </summary>
        Task LoadChatSessionAsync(string sessionId);
        
        /// <summary>
        /// Initializes the session manager with required UI components and dependencies
        /// </summary>
        void Initialize(Agent agent, TextView chatView, TextView toolCallsView, MarkdownRenderer markdownRenderer);
        
        /// <summary>
        /// Event raised when agent status should be updated
        /// </summary>
        event System.Action<string> StatusUpdated;
    }
}