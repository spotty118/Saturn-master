using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Saturn.Agents.Core;
using Saturn.Data;
using Saturn.Data.Models;
using Saturn.OpenRouter;
using Saturn.OpenRouter.Models.Api.Chat;
using Saturn.Tools.Core;
using Message = Saturn.OpenRouter.Models.Api.Chat.Message;

namespace Saturn.Agents
{
    public class StreamingChatChunk
    {
        public string Content { get; set; } = "";
        public bool IsComplete { get; set; }
        public bool IsToolCall { get; set; }
    }

    public class AgentBase
    {
        private readonly ToolRegistry toolRegistry;
        public string Name => Configuration.Name;
        public AgentConfiguration Configuration { get; private set; }
        public List<Message> ChatHistory { get; private set; }
        public string? CurrentSessionId { get; set; }
        public event Action<string, string>? OnToolCall;

        public AgentBase(AgentConfiguration configuration, ToolRegistry? toolRegistry = null)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            this.toolRegistry = toolRegistry ?? new ToolRegistry(null!); // Fallback for backward compatibility
            ChatHistory = new List<Message>();
        }

        public async Task<T> Execute<T>(string message) where T : class
        {
            var result = await ExecuteInternal(message);
            return (result as T) ?? throw new InvalidOperationException($"Cannot convert result to {typeof(T).Name}");
        }

        public async Task ExecuteStreamAsync(string message, Func<StreamingChatChunk, Task>? onChunk = null, CancellationToken cancellationToken = default)
        {
            await ExecuteStreamInternal(message, onChunk, cancellationToken);
        }

        public void ClearHistory()
        {
            ChatHistory.Clear();
        }

        public async Task InitializeSessionAsync(string chatType, string? parentSessionId = null)
        {
            if (CurrentSessionId != null)
                return;

            var repository = new ChatHistoryRepository();
            try
            {
                var session = await repository.CreateSessionAsync(
                    $"Session {DateTime.Now:yyyy-MM-dd HH:mm}",
                    chatType,
                    parentSessionId,
                    Name,
                    Configuration.Model,
                    Configuration.SystemPrompt?.ToString(),
                    Configuration.Temperature,
                    Configuration.MaxTokens
                );
                CurrentSessionId = session.Id;

                if (Configuration.SystemPrompt != null)
                {
                    var systemMessage = new Message
                    {
                        Role = "system",
                        Content = JsonDocument.Parse(JsonSerializer.Serialize(Configuration.SystemPrompt.ToString())).RootElement
                    };
                    ChatHistory.Add(systemMessage);
                    await repository.SaveMessageAsync(CurrentSessionId, systemMessage);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize session: {ex.Message}");
            }
            finally
            {
                repository.Dispose();
            }
        }

        private Message[] BuildChatHistory(string message)
        {
            var messages = new List<Message>(ChatHistory);
            
            var userMessage = new Message
            {
                Role = "user",
                Content = JsonDocument.Parse(JsonSerializer.Serialize(message)).RootElement
            };
            messages.Add(userMessage);

            if (CurrentSessionId != null)
            {
                var repository = new ChatHistoryRepository();
                try
                {
                    await repository.SaveMessageAsync(CurrentSessionId, userMessage);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to save user message: {ex.Message}");
                }
                finally
                {
                    repository.Dispose();
                }
            }

            if (Configuration.MaintainHistory && Configuration.MaxHistoryMessages.HasValue)
            {
                var maxMessages = Configuration.MaxHistoryMessages.Value;
                if (messages.Count > maxMessages)
                {
                    var systemMessages = messages.Where(m => m.Role == "system").ToList();
                    var nonSystemMessages = messages.Where(m => m.Role != "system").ToList();
                    
                    if (nonSystemMessages.Count > maxMessages - systemMessages.Count)
                    {
                        var toKeep = maxMessages - systemMessages.Count;
                        nonSystemMessages = nonSystemMessages.Skip(nonSystemMessages.Count - toKeep).ToList();
                    }
                    
                    messages = systemMessages.Concat(nonSystemMessages).ToList();
                }
            }

            return messages.ToArray();
        }
        
        private async Task<Message> ExecuteInternal(string message, CancellationToken cancellationToken = default)
        {
            if (Configuration.Client == null)
                throw new InvalidOperationException("OpenRouter client is not configured");

            var request = new ChatCompletionRequest
            {
                Model = Configuration.Model,
                Messages = BuildChatHistory(message),
                Temperature = Configuration.Temperature,
                MaxTokens = Configuration.MaxTokens,
                TopP = Configuration.TopP,
                Stream = false
            };

            if (Configuration.EnableTools)
            {
                request.Tools = (Configuration.ToolNames != null && Configuration.ToolNames.Count > 0)
                    ? toolRegistry.GetOpenRouterToolDefinitions(Configuration.ToolNames.ToArray()).ToArray()
                    : toolRegistry.GetOpenRouterToolDefinitions().ToArray();
            }

            var response = await Configuration.Client.Chat.CreateAsync(request, cancellationToken);
            
            if (response?.Choices?.FirstOrDefault()?.Message == null)
                throw new InvalidOperationException("Invalid response from OpenRouter API");

            var assistantMessageResponse = response.Choices.First().Message;
            var responseMessage = new Message
            {
                Role = assistantMessageResponse.Role,
                Content = JsonDocument.Parse(JsonSerializer.Serialize(assistantMessageResponse.Content)).RootElement,
                ToolCalls = assistantMessageResponse.ToolCalls?.Select(tc => new ToolCallRequest
                {
                    Id = tc.Id,
                    Type = tc.Type,
                    Function = new ToolCallRequest.FunctionCall
                    {
                        Name = tc.Function.Name,
                        Arguments = tc.Function.Arguments
                    }
                }).ToArray()
            };
            
            if (responseMessage.ToolCalls != null && responseMessage.ToolCalls.Length > 0)
            {
                ChatHistory.Add(responseMessage);

                if (CurrentSessionId != null)
                {
                    var repository = new ChatHistoryRepository();
                    try
                    {
                        await repository.SaveMessageAsync(CurrentSessionId, responseMessage);
                        repository.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to save assistant message: {ex.Message}");
                    }
                }

                foreach (var toolCall in responseMessage.ToolCalls)
                {
                    var stopwatch = Stopwatch.StartNew();
                    var tool = toolRegistry.Get(toolCall.Function.Name);

                    if (tool == null)
                    {
                        var errorResult = JsonDocument.Parse(JsonSerializer.Serialize($"Tool '{toolCall.Function.Name}' not found"));
                        var errorMessage = new Message
                        {
                            Role = "tool",
                            ToolCallId = toolCall.Id,
                            Content = errorResult.RootElement
                        };
                        ChatHistory.Add(errorMessage);
                        continue;
                    }

                    OnToolCall?.Invoke(toolCall.Function.Name, toolCall.Function.Arguments ?? "{}");

                    try
                    {
                        var argumentsDict = string.IsNullOrEmpty(toolCall.Function.Arguments) 
                            ? new Dictionary<string, object>() 
                            : JsonSerializer.Deserialize<Dictionary<string, object>>(toolCall.Function.Arguments);

                        var toolResult = await tool.ExecuteAsync(argumentsDict ?? new Dictionary<string, object>());
                        stopwatch.Stop();

                        var resultContent = JsonDocument.Parse(JsonSerializer.Serialize(toolResult));
                        var toolMessage = new Message
                        {
                            Role = "tool",
                            ToolCallId = toolCall.Id,
                            Content = resultContent.RootElement,
                            Name = toolCall.Function.Name
                        };

                        ChatHistory.Add(toolMessage);

                        if (CurrentSessionId != null)
                        {
                            var repository = new ChatHistoryRepository();
                            try
                            {
                                var messageId = await repository.SaveMessageAsync(CurrentSessionId, toolMessage);
                                var toolCallRecord = await repository.SaveToolCallAsync(messageId.Id, CurrentSessionId, toolCall.Function.Name, toolCall.Function.Arguments ?? "{}", Name);
                                await repository.UpdateToolCallResultAsync(toolCallRecord.Id, toolResult.ToString(), null, (int)stopwatch.ElapsedMilliseconds);
                                repository.Dispose();
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Failed to save tool call: {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        stopwatch.Stop();
                        var errorResult = JsonDocument.Parse(JsonSerializer.Serialize($"Error executing tool: {ex.Message}"));
                        var errorMessage = new Message
                        {
                            Role = "tool",
                            ToolCallId = toolCall.Id,
                            Content = errorResult.RootElement,
                            Name = toolCall.Function.Name
                        };
                        ChatHistory.Add(errorMessage);

                        if (CurrentSessionId != null)
                        {
                            var repository = new ChatHistoryRepository();
                            try
                            {
                                var messageId = await repository.SaveMessageAsync(CurrentSessionId, errorMessage);
                                var toolCallRecord = await repository.SaveToolCallAsync(messageId.Id, CurrentSessionId, toolCall.Function.Name, toolCall.Function.Arguments ?? "{}", Name);
                                await repository.UpdateToolCallResultAsync(toolCallRecord.Id, null, ex.Message, (int)stopwatch.ElapsedMilliseconds);
                                repository.Dispose();
                            }
                            catch (Exception saveEx)
                            {
                                Console.WriteLine($"Failed to save tool call error: {saveEx.Message}");
                            }
                        }
                    }
                }

                return await ExecuteInternal("Please continue with your response", cancellationToken);
            }

            ChatHistory.Add(responseMessage);

            if (CurrentSessionId != null)
            {
                var repository = new ChatHistoryRepository();
                try
                {
                    await repository.SaveMessageAsync(CurrentSessionId, responseMessage);
                    repository.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to save assistant message: {ex.Message}");
                }
            }

            return responseMessage;
        }

        private async Task ExecuteStreamInternal(string message, Func<StreamingChatChunk, Task>? onChunk = null, CancellationToken cancellationToken = default)
        {
            if (Configuration.Client == null)
                throw new InvalidOperationException("OpenRouter client is not configured");

            var request = new ChatCompletionRequest
            {
                Model = Configuration.Model,
                Messages = BuildChatHistory(message),
                Temperature = Configuration.Temperature,
                MaxTokens = Configuration.MaxTokens,
                TopP = Configuration.TopP,
                Stream = true
            };

            if (Configuration.EnableTools)
            {
                request.Tools = (Configuration.ToolNames != null && Configuration.ToolNames.Count > 0)
                    ? toolRegistry.GetOpenRouterToolDefinitions(Configuration.ToolNames.ToArray()).ToArray()
                    : toolRegistry.GetOpenRouterToolDefinitions().ToArray();
            }

            var responseBuilder = new StringBuilder();
            var isComplete = false;
            var toolCalls = new List<ToolCall>();

            await foreach (var chunk in Configuration.Client.ChatStreaming.StreamAsync(request, cancellationToken))
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                if (chunk.Choices?.FirstOrDefault()?.Delta != null)
                {
                    var delta = chunk.Choices.First().Delta;
                    
                    if (!string.IsNullOrEmpty(delta.Content))
                    {
                        responseBuilder.Append(delta.Content);
                        
                        var streamChunk = new StreamingChatChunk
                        {
                            Content = delta.Content,
                            IsComplete = false,
                            IsToolCall = false
                        };
                        
                        if (onChunk != null)
                        {
                            await onChunk(streamChunk);
                        }
                    }
                    
                    if (delta.ToolCalls != null)
                    {
                        toolCalls.AddRange(delta.ToolCalls);
                        
                        var toolChunk = new StreamingChatChunk
                        {
                            Content = "",
                            IsComplete = false,
                            IsToolCall = true
                        };
                        
                        if (onChunk != null)
                        {
                            await onChunk(toolChunk);
                        }
                    }
                }
                
                if (chunk.Choices?.FirstOrDefault()?.FinishReason == "stop" || 
                    chunk.Choices?.FirstOrDefault()?.FinishReason == "tool_calls")
                {
                    isComplete = true;
                    
                    var completeChunk = new StreamingChatChunk
                    {
                        Content = "",
                        IsComplete = true,
                        IsToolCall = false
                    };
                    
                    if (onChunk != null)
                    {
                        await onChunk(completeChunk);
                    }
                }
            }

            var finalContent = responseBuilder.ToString();
            var finalMessage = new Message
            {
                Role = "assistant",
                Content = string.IsNullOrEmpty(finalContent) ? 
                    JsonDocument.Parse("\"\"").RootElement : 
                    JsonDocument.Parse(JsonSerializer.Serialize(finalContent)).RootElement,
                ToolCalls = toolCalls.Select(tc => new ToolCallRequest
                {
                    Id = tc.Id,
                    Type = tc.Type,
                    Function = new ToolCallRequest.FunctionCall
                    {
                        Name = tc.Function.Name,
                        Arguments = tc.Function.Arguments
                    }
                }).ToArray()
            };

            ChatHistory.Add(finalMessage);

            if (CurrentSessionId != null)
            {
                var repository = new ChatHistoryRepository();
                try
                {
                    await repository.SaveMessageAsync(CurrentSessionId, finalMessage);
                    repository.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to save assistant message: {ex.Message}");
                }
            }

            if (toolCalls != null && toolCalls.Count > 0)
            {
                foreach (var toolCall in toolCalls)
                {
                    var stopwatch = Stopwatch.StartNew();
                    var tool = toolRegistry.Get(toolCall.Function.Name);

                    if (tool == null)
                    {
                        var errorResult = JsonDocument.Parse(JsonSerializer.Serialize($"Tool '{toolCall.Function.Name}' not found"));
                        var errorMessage = new Message
                        {
                            Role = "tool",
                            ToolCallId = toolCall.Id,
                            Content = errorResult.RootElement
                        };
                        ChatHistory.Add(errorMessage);
                        continue;
                    }

                    OnToolCall?.Invoke(toolCall.Function.Name, toolCall.Function.Arguments ?? "{}");

                    try
                    {
                        var argumentsDict = string.IsNullOrEmpty(toolCall.Function.Arguments) 
                            ? new Dictionary<string, object>() 
                            : JsonSerializer.Deserialize<Dictionary<string, object>>(toolCall.Function.Arguments);

                        var toolResult = await tool.ExecuteAsync(argumentsDict ?? new Dictionary<string, object>());
                        stopwatch.Stop();

                        var resultContent = JsonDocument.Parse(JsonSerializer.Serialize(toolResult));
                        var toolMessage = new Message
                        {
                            Role = "tool",
                            ToolCallId = toolCall.Id,
                            Content = resultContent.RootElement,
                            Name = toolCall.Function.Name
                        };

                        ChatHistory.Add(toolMessage);

                        if (CurrentSessionId != null)
                        {
                            var repository = new ChatHistoryRepository();
                            try
                            {
                                var messageId = await repository.SaveMessageAsync(CurrentSessionId, toolMessage);
                                var toolCallRecord = await repository.SaveToolCallAsync(messageId.Id, CurrentSessionId, toolCall.Function.Name, toolCall.Function.Arguments ?? "{}", Name);
                                await repository.UpdateToolCallResultAsync(toolCallRecord.Id, toolResult.ToString(), null, (int)stopwatch.ElapsedMilliseconds);
                                repository.Dispose();
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Failed to save tool call: {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        stopwatch.Stop();
                        var errorResult = JsonDocument.Parse(JsonSerializer.Serialize($"Error executing tool: {ex.Message}"));
                        var errorMessage = new Message
                        {
                            Role = "tool",
                            ToolCallId = toolCall.Id,
                            Content = errorResult.RootElement,
                            Name = toolCall.Function.Name
                        };
                        ChatHistory.Add(errorMessage);

                        if (CurrentSessionId != null)
                        {
                            var repository = new ChatHistoryRepository();
                            try
                            {
                                var messageId = await repository.SaveMessageAsync(CurrentSessionId, errorMessage);
                                var toolCallRecord = await repository.SaveToolCallAsync(messageId.Id, CurrentSessionId, toolCall.Function.Name, toolCall.Function.Arguments ?? "{}", Name);
                                await repository.UpdateToolCallResultAsync(toolCallRecord.Id, null, ex.Message, (int)stopwatch.ElapsedMilliseconds);
                                repository.Dispose();
                            }
                            catch (Exception saveEx)
                            {
                                Console.WriteLine($"Failed to save tool call error: {saveEx.Message}");
                            }
                        }
                    }
                }

                await ExecuteStreamInternal("Please continue with your response", onChunk, cancellationToken);
            }
        }
    }
}