using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Terminal.Gui;
using Saturn.Agents;
using Saturn.Data;
using Saturn.Data.Models;
using Saturn.OpenRouter.Models.Api.Chat;

namespace Saturn.UI
{
    public class ChatSessionManager : IChatSessionManager
    {
        private Agent? agent;
        private TextView? chatView;
        private TextView? toolCallsView;
        private MarkdownRenderer? markdownRenderer;

        public event Action<string>? StatusUpdated;

        public void Initialize(Agent agent, TextView chatView, TextView toolCallsView, MarkdownRenderer markdownRenderer)
        {
            this.agent = agent ?? throw new ArgumentNullException(nameof(agent));
            this.chatView = chatView ?? throw new ArgumentNullException(nameof(chatView));
            this.toolCallsView = toolCallsView ?? throw new ArgumentNullException(nameof(toolCallsView));
            this.markdownRenderer = markdownRenderer ?? throw new ArgumentNullException(nameof(markdownRenderer));
        }

        public async Task LoadChatSessionAsync(string sessionId)
        {
            if (agent == null || chatView == null || toolCallsView == null || markdownRenderer == null)
                throw new InvalidOperationException("ChatSessionManager not properly initialized");

            try
            {
                var repository = new ChatHistoryRepository();
                var session = await repository.GetSessionAsync(sessionId);
                
                if (session == null)
                {
                    throw new InvalidOperationException("Session not found");
                }

                var messages = await repository.GetMessagesAsync(sessionId);
                var toolCalls = await repository.GetToolCallsAsync(sessionId);
                
                // Clear existing content
                agent.ClearHistory();
                chatView.Text = "";
                toolCallsView.Text = "";
                
                // Set the current session
                agent.CurrentSessionId = sessionId;
                
                // Add system prompt to agent history if present
                if (!string.IsNullOrEmpty(session.SystemPrompt))
                {
                    agent.ChatHistory.Add(new Message
                    {
                        Role = "system",
                        Content = JsonDocument.Parse(JsonSerializer.Serialize(session.SystemPrompt)).RootElement
                    });
                }
                
                // Build chat content
                var chatContent = new StringBuilder();
                chatContent.AppendLine($"=== Loaded Chat: {session.Title} ===");
                chatContent.AppendLine($"Model: {session.Model} | Created: {session.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}");
                chatContent.AppendLine();
                
                // Process messages
                foreach (var message in messages)
                {
                    var timestamp = message.Timestamp.ToLocalTime().ToString("HH:mm:ss");
                    
                    if (message.Role == "system")
                    {
                        chatContent.AppendLine($"[System Prompt]\n{message.Content}\n");
                        continue;
                    }
                    else if (message.Role == "user")
                    {
                        chatContent.AppendLine($"[{timestamp}] You:\n{message.Content}\n");
                    }
                    else if (message.Role == "assistant")
                    {
                        if (!string.IsNullOrEmpty(message.ToolCallsJson))
                        {
                            chatContent.AppendLine($"[{timestamp}] Assistant: [Making tool calls...]\n");
                        }
                        else if (message.Content != "null" && !string.IsNullOrEmpty(message.Content))
                        {
                            var renderedContent = markdownRenderer.RenderToTerminal(message.Content);
                            chatContent.AppendLine($"[{timestamp}] Assistant:\n{renderedContent}\n");
                        }
                    }
                    else if (message.Role == "tool")
                    {
                        var toolResult = message.Content.Length > 500 
                            ? message.Content.Substring(0, 500) + "...\n[Output truncated]" 
                            : message.Content;
                        chatContent.AppendLine($"[{timestamp}] Tool Result ({message.Name}):\n{toolResult}\n");
                    }
                    
                    // Add message to agent history
                    Message openRouterMessage;
                    try
                    {
                        var jsonDoc = JsonDocument.Parse(message.Content);
                        openRouterMessage = new Message
                        {
                            Role = message.Role,
                            Content = jsonDoc.RootElement,
                            Name = message.Name,
                            ToolCallId = message.ToolCallId
                        };
                    }
                    catch
                    {
                        openRouterMessage = new Message
                        {
                            Role = message.Role,
                            Content = JsonDocument.Parse(JsonSerializer.Serialize(message.Content)).RootElement,
                            Name = message.Name,
                            ToolCallId = message.ToolCallId
                        };
                    }
                    
                    // Add tool calls if present
                    if (!string.IsNullOrEmpty(message.ToolCallsJson))
                    {
                        try
                        {
                            openRouterMessage.ToolCalls = JsonSerializer.Deserialize<ToolCallRequest[]>(message.ToolCallsJson);
                        }
                        catch { }
                    }
                    
                    agent.ChatHistory.Add(openRouterMessage);
                }
                
                // Update tool calls view if there are any
                if (toolCalls.Any())
                {
                    var toolCallsContent = new StringBuilder();
                    toolCallsContent.AppendLine("=== Tool Call History ===");
                    foreach (var toolCall in toolCalls)
                    {
                        var timestamp = toolCall.Timestamp.ToLocalTime().ToString("HH:mm:ss");
                        toolCallsContent.AppendLine($"[{timestamp}] {toolCall.ToolName}");
                        if (!string.IsNullOrEmpty(toolCall.AgentName))
                        {
                            toolCallsContent.AppendLine($"  Agent: {toolCall.AgentName}");
                        }
                        toolCallsContent.AppendLine($"  Duration: {toolCall.DurationMs}ms");
                        if (!string.IsNullOrEmpty(toolCall.Error))
                        {
                            toolCallsContent.AppendLine($"  Error: {toolCall.Error}");
                        }
                        toolCallsContent.AppendLine("───────────────");
                    }
                    toolCallsView.Text = toolCallsContent.ToString();
                }
                
                // Update chat view
                chatView.Text = chatContent.ToString();
                chatView.CursorPosition = new Point(0, chatView.Lines);
                
                // Update status
                if (session.ChatType == "agent" && !string.IsNullOrEmpty(session.ParentSessionId))
                {
                    StatusUpdated?.Invoke($"Loaded agent session: {session.AgentName}");
                }
                else
                {
                    StatusUpdated?.Invoke("Chat history loaded");
                }
                
                repository.Dispose();
                Application.Refresh();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load chat: {ex.Message}", ex);
            }
        }
    }
}