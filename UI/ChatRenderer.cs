using System;
using Terminal.Gui;
using Saturn.Agents;

namespace Saturn.UI
{
    public class ChatRenderer : IChatRenderer
    {
        private TextView? chatView;
        private MarkdownRenderer? markdownRenderer;

        public void Initialize(TextView chatView, MarkdownRenderer markdownRenderer)
        {
            this.chatView = chatView ?? throw new ArgumentNullException(nameof(chatView));
            this.markdownRenderer = markdownRenderer ?? throw new ArgumentNullException(nameof(markdownRenderer));
        }

        public string GetWelcomeMessage(Agent agent)
        {
            var message = "Welcome to Saturn\nDont forget to join our discord to stay updated.\n\"https://discord.gg/VSjW36MfYZ\"";
            message += "\n================================\n";
            message += $"Agent: {agent.Name}\n";
            message += $"Model: {agent.Configuration.Model}\n";
            message += $"Streaming: {(agent.Configuration.EnableStreaming ? "Enabled" : "Disabled")}\n";
            message += $"Tools: {(agent.Configuration.EnableTools ? "Enabled" : "Disabled")}\n";
            if (agent.Configuration.EnableTools && agent.Configuration.ToolNames != null && agent.Configuration.ToolNames.Count > 0)
            {
                message += $"Available Tools: {string.Join(", ", agent.Configuration.ToolNames)}\n";
            }
            message += "================================\n";
            message += "Type your message below and press Ctrl+Enter to send.\n";
            message += "Use the Options menu to clear chat or quit.\n\n";
            return message;
        }

        public void ScrollChatToBottom()
        {
            if (chatView != null && chatView.Lines > 0)
            {
                var lastLine = Math.Max(0, chatView.Lines - 1);
                chatView.CursorPosition = new Point(0, lastLine);
                
                if (chatView.Frame.Height > 0)
                {
                    var topRow = Math.Max(0, chatView.Lines - chatView.Frame.Height);
                    chatView.TopRow = topRow;
                }
                
                chatView.PositionCursor();
            }
        }

        public void AppendToChat(string content, bool isAssistant = false, bool applyMarkdown = false)
        {
            if (chatView == null) return;

            string finalContent = content;
            
            if (applyMarkdown && markdownRenderer != null)
            {
                var formattedText = markdownRenderer.RenderToFormattedText(content);
                AppendFormattedText(formattedText);
            }
            else
            {
                chatView.Text += finalContent;
            }

            ScrollChatToBottom();
        }

        private void AppendFormattedText(FormattedText formattedText)
        {
            if (chatView == null) return;

            foreach (var segment in formattedText.GetSegments())
            {
                Application.Driver.SetAttribute(segment.attribute);
                chatView.AddRune(chatView.CursorPosition.X, chatView.CursorPosition.Y, (Rune)segment.text[0]);
            }
        }

        public void ClearChat(Agent agent)
        {
            if (chatView == null) return;
            
            chatView.Text = GetWelcomeMessage(agent);
            chatView.CursorPosition = new Point(0, 0);
        }
    }
}