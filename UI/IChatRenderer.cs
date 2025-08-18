using System;
using Terminal.Gui;
using Saturn.Agents;

namespace Saturn.UI
{
    public interface IChatRenderer
    {
        /// <summary>
        /// Gets the welcome message for the chat interface
        /// </summary>
        string GetWelcomeMessage(Agent agent);
        
        /// <summary>
        /// Scrolls the chat view to the bottom
        /// </summary>
        void ScrollChatToBottom();
        
        /// <summary>
        /// Initializes the chat renderer with the required UI components
        /// </summary>
        void Initialize(TextView chatView, MarkdownRenderer markdownRenderer);
        
        /// <summary>
        /// Renders content and appends it to the chat view with proper formatting
        /// </summary>
        void AppendToChat(string content, bool isAssistant = false, bool applyMarkdown = false);
        
        /// <summary>
        /// Clears the chat content and displays the welcome message
        /// </summary>
        void ClearChat(Agent agent);
    }
}