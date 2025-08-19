using Microsoft.AspNetCore.SignalR;
using Saturn.Agents;
using Saturn.Tools.Core;
using Saturn.Core;
using System.Text.Json;

namespace Saturn.Web
{
    public class ChatHub : Hub
    {
        private readonly Agent _agent;
        private readonly ToolRegistry _toolRegistry;
        private readonly SettingsManager _settingsManager;

        public ChatHub(Agent agent, ToolRegistry toolRegistry, SettingsManager settingsManager)
        {
            _agent = agent;
            _toolRegistry = toolRegistry;
            _settingsManager = settingsManager;
        }

        public async Task SendMessage(string message)
        {
            try
            {
                // Initialize session if needed
                if (_agent.CurrentSessionId == null)
                {
                    await _agent.InitializeSessionAsync("web");
                }

                // Send user message to all clients
                await Clients.All.SendAsync("UserMessage", message);

                // Execute agent and stream response
                if (_agent.Configuration.EnableStreaming)
                {
                    await _agent.ExecuteStreamAsync(
                        message,
                        async (chunk) =>
                        {
                            if (!chunk.IsComplete && !chunk.IsToolCall && !string.IsNullOrEmpty(chunk.Content))
                            {
                                await Clients.All.SendAsync("AssistantChunk", chunk.Content);
                            }
                        });
                }
                else
                {
                    var response = await _agent.Execute<OpenRouter.Models.Api.Chat.Message>(message);
                    var responseText = response?.Content.ValueKind != System.Text.Json.JsonValueKind.Null &&
                                       response?.Content.ValueKind != System.Text.Json.JsonValueKind.Undefined
                        ? response.Content.ToString()
                        : "No response received";
                    await Clients.All.SendAsync("AssistantMessage", responseText);
                }

                // Signal completion
                await Clients.All.SendAsync("MessageComplete");
            }
            catch (Exception ex)
            {
                await Clients.All.SendAsync("Error", ex.Message);
            }
        }

        public async Task GetAgentStatus()
        {
            var status = new
            {
                MainAgent = _agent.CurrentSessionId != null ? "Active" : "Ready",
                SessionId = _agent.CurrentSessionId ?? "None",
                IsStreaming = _agent.Configuration.EnableStreaming,
                Model = _agent.Configuration.Model,
                Temperature = _agent.Configuration.Temperature,
                MaxTokens = _agent.Configuration.MaxTokens,
                ToolsEnabled = _agent.Configuration.EnableTools,
                ToolCount = _toolRegistry.GetAll().Count(),
                HistoryEnabled = _agent.Configuration.MaintainHistory,
                MaxHistory = _agent.Configuration.MaxHistoryMessages,
                RequiresApproval = _agent.Configuration.RequireCommandApproval
            };

            await Clients.Caller.SendAsync("AgentStatus", JsonSerializer.Serialize(status));
        }

        public async Task GetAvailableTools()
        {
            var tools = _toolRegistry.GetAll().Select(t => new
            {
                t.Name,
                t.Description,
                Parameters = t.GetParameters()
            }).ToList();

            await Clients.Caller.SendAsync("AvailableTools", JsonSerializer.Serialize(tools));
        }

        public async Task GetConfiguration()
        {
            try
            {
                var settings = await _settingsManager.LoadSettingsAsync();
                
                // Fetch available models from OpenRouter API
                string[] availableModels;
                try
                {
                    var modelsList = await _agent.Configuration.Client.Models.ListAllAsync();
                    availableModels = modelsList?.Data?
                        .Where(m => !string.IsNullOrEmpty(m.Id))
                        .OrderBy(m => m.Id)
                        .Select(m => m.Id)
                        .ToArray() ?? new string[0];
                }
                catch (Exception ex)
                {
                    // Fallback to a basic list if API call fails
                    Console.WriteLine($"Failed to fetch models from OpenRouter: {ex.Message}");
                    availableModels = new[]
                    {
                        "anthropic/claude-sonnet-4",
                        "anthropic/claude-3.5-sonnet",
                        "anthropic/claude-3-haiku",
                        "openai/gpt-4",
                        "openai/gpt-4-turbo",
                        "openai/gpt-3.5-turbo",
                        "meta-llama/llama-3.1-70b-instruct",
                        "google/gemini-pro-1.5",
                        "mistralai/mixtral-8x7b-instruct"
                    };
                }

                var config = new
                {
                    HasApiKey = !string.IsNullOrEmpty(settings.OpenRouterApiKey),
                    Model = settings.DefaultModel ?? _agent.Configuration.Model,
                    Temperature = settings.Temperature ?? _agent.Configuration.Temperature,
                    MaxTokens = settings.MaxTokens ?? _agent.Configuration.MaxTokens,
                    EnableStreaming = settings.EnableStreaming ?? _agent.Configuration.EnableStreaming,
                    RequireCommandApproval = settings.RequireCommandApproval ?? _agent.Configuration.RequireCommandApproval,
                    AvailableModels = availableModels
                };

                await Clients.Caller.SendAsync("Configuration", JsonSerializer.Serialize(config));
            }
            catch (Exception ex)
            {
                await Clients.All.SendAsync("Error", $"Failed to get configuration: {ex.Message}");
            }
        }

        public async Task UpdateConfiguration(string configJson)
        {
            try
            {
                var updateRequest = JsonSerializer.Deserialize<Dictionary<string, object>>(configJson);
                if (updateRequest == null) return;

                var settings = await _settingsManager.LoadSettingsAsync();
                bool configChanged = false;

                if (updateRequest.TryGetValue("apiKey", out var apiKeyObj) && apiKeyObj?.ToString() is string apiKey && !string.IsNullOrWhiteSpace(apiKey))
                {
                    await _settingsManager.SetApiKeyAsync(apiKey);
                    // Hot-apply to the current OpenRouter client if available
                    try
                    {
                        var client = _agent.Configuration?.Client;
                        if (client?.Options != null)
                        {
                            client.Options.ApiKey = apiKey;
                        }
                    }
                    catch { /* best-effort hot-apply */ }
                    configChanged = true;
                }

                if (updateRequest.TryGetValue("model", out var modelObj) && modelObj?.ToString() is string model)
                {
                    await _settingsManager.SetModelAsync(model);
                    _agent.Configuration.Model = model;
                    configChanged = true;
                }

                if (updateRequest.TryGetValue("temperature", out var tempObj) && double.TryParse(tempObj?.ToString(), out var temperature))
                {
                    settings.Temperature = temperature;
                    _agent.Configuration.Temperature = temperature;
                    configChanged = true;
                }

                if (updateRequest.TryGetValue("maxTokens", out var tokensObj) && int.TryParse(tokensObj?.ToString(), out var maxTokens))
                {
                    settings.MaxTokens = maxTokens;
                    _agent.Configuration.MaxTokens = maxTokens;
                    configChanged = true;
                }

                if (updateRequest.TryGetValue("enableStreaming", out var streamObj) && bool.TryParse(streamObj?.ToString(), out var enableStreaming))
                {
                    settings.EnableStreaming = enableStreaming;
                    _agent.Configuration.EnableStreaming = enableStreaming;
                    configChanged = true;
                }

                if (updateRequest.TryGetValue("requireCommandApproval", out var approvalObj) && bool.TryParse(approvalObj?.ToString(), out var requireApproval))
                {
                    settings.RequireCommandApproval = requireApproval;
                    _agent.Configuration.RequireCommandApproval = requireApproval;
                    configChanged = true;
                }

                if (configChanged)
                {
                    await _settingsManager.SaveSettingsAsync(settings);
                    await Clients.All.SendAsync("ConfigurationUpdated", "Configuration saved successfully");
                    
                    // Refresh agent status to reflect changes
                    await GetAgentStatus();
                }
            }
            catch (Exception ex)
            {
                await Clients.All.SendAsync("Error", $"Failed to update configuration: {ex.Message}");
            }
        }

        public override async Task OnConnectedAsync()
        {
            await Clients.Caller.SendAsync("Connected", "Welcome to Saturn Web UI!");
            await GetAgentStatus();
            await GetAvailableTools();
            await GetConfiguration();
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await base.OnDisconnectedAsync(exception);
        }
    }
}