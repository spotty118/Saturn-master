using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Saturn.Agents;
using Saturn.Agents.Core;
using Saturn.Configuration;
using Saturn.OpenRouter;
using Saturn.OpenRouter.Models.Api.Models;

namespace Saturn.UI
{
    public class AgentConfigurationManager : IAgentConfigurationManager
    {
        private Agent? agent;
        private OpenRouterClient? openRouterClient;
        private ConfigurationManager? configurationManager;
        private UIAgentConfiguration currentConfig = null!;

        public UIAgentConfiguration CurrentConfig => currentConfig;

        public event Action<Agent>? AgentReconfigured;

        public void Initialize(Agent agent, OpenRouterClient openRouterClient, ConfigurationManager configurationManager)
        {
            this.agent = agent ?? throw new ArgumentNullException(nameof(agent));
            this.openRouterClient = openRouterClient ?? throw new ArgumentNullException(nameof(openRouterClient));
            this.configurationManager = configurationManager ?? throw new ArgumentNullException(nameof(configurationManager));
            
            // Initialize current config from agent
            currentConfig = new UIAgentConfiguration
            {
                Model = agent.Configuration.Model,
                Temperature = agent.Configuration.Temperature ?? 0.15,
                MaxTokens = agent.Configuration.MaxTokens ?? 4096,
                TopP = agent.Configuration.TopP ?? 0.25,
                EnableStreaming = agent.Configuration.EnableStreaming,
                MaintainHistory = agent.Configuration.MaintainHistory,
                MaxHistoryMessages = agent.Configuration.MaxHistoryMessages ?? 10,
                SystemPrompt = agent.Configuration.SystemPrompt?.ToString() ?? "",
                EnableTools = agent.Configuration.EnableTools,
                ToolNames = agent.Configuration.ToolNames ?? new List<string>(),
                RequireCommandApproval = agent.Configuration.RequireCommandApproval
            };
        }

        public async Task ReconfigureAgentAsync()
        {
            if (agent == null || openRouterClient == null || configurationManager == null) return;

            try
            {
                var temperature = currentConfig.Temperature;
                if (currentConfig.Model.Contains("gpt-5", StringComparison.OrdinalIgnoreCase))
                {
                    temperature = 1.0;
                }
                
                var newConfig = new Saturn.Agents.Core.AgentConfiguration
                {
                    Name = agent.Name,
                    SystemPrompt = await SystemPrompt.Create(currentConfig.SystemPrompt),
                    Client = openRouterClient,
                    Model = currentConfig.Model,
                    Temperature = temperature,
                    MaxTokens = currentConfig.MaxTokens,
                    TopP = currentConfig.TopP,
                    MaintainHistory = currentConfig.MaintainHistory,
                    MaxHistoryMessages = currentConfig.MaxHistoryMessages,
                    EnableTools = currentConfig.EnableTools,
                    EnableStreaming = currentConfig.EnableStreaming,
                    ToolNames = currentConfig.ToolNames ?? new List<string>(),
                    RequireCommandApproval = currentConfig.RequireCommandApproval
                };

                await configurationManager.SaveConfigurationAsync(
                    configurationManager.FromAgentConfiguration(newConfig));

                agent = new Agent(newConfig);
                AgentReconfigured?.Invoke(agent);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to reconfigure agent: {ex.Message}", ex);
            }
        }

        public void ApplyModeToConfiguration(Mode mode)
        {
            currentConfig.Model = mode.Model;
            currentConfig.Temperature = mode.Temperature;
            currentConfig.MaxTokens = mode.MaxTokens;
            currentConfig.TopP = mode.TopP;
            currentConfig.EnableStreaming = mode.EnableStreaming;
            currentConfig.MaintainHistory = mode.MaintainHistory;
            currentConfig.RequireCommandApproval = mode.RequireCommandApproval;
            currentConfig.ToolNames = new List<string>(mode.ToolNames ?? new List<string>());
            currentConfig.EnableTools = mode.ToolNames?.Count > 0;
            
            if (!string.IsNullOrWhiteSpace(mode.SystemPromptOverride))
            {
                currentConfig.SystemPrompt = mode.SystemPromptOverride;
            }
        }

        public async Task ToggleStreamingAsync()
        {
            currentConfig.EnableStreaming = !currentConfig.EnableStreaming;
            await ReconfigureAgentAsync();
        }

        public async Task ToggleMaintainHistoryAsync()
        {
            currentConfig.MaintainHistory = !currentConfig.MaintainHistory;
            await ReconfigureAgentAsync();
        }

        public async Task ToggleCommandApprovalAsync()
        {
            currentConfig.RequireCommandApproval = !currentConfig.RequireCommandApproval;
            await ReconfigureAgentAsync();
        }

        public async Task UpdateModelAsync(string model)
        {
            currentConfig.Model = model;
            if (model.Contains("gpt-5", StringComparison.OrdinalIgnoreCase))
            {
                currentConfig.Temperature = 1.0;
            }
            await ReconfigureAgentAsync();
        }

        public async Task UpdateTemperatureAsync(double temperature)
        {
            if (!currentConfig.Model.Contains("gpt-5", StringComparison.OrdinalIgnoreCase))
            {
                currentConfig.Temperature = temperature;
                await ReconfigureAgentAsync();
            }
        }

        public async Task UpdateMaxTokensAsync(int maxTokens)
        {
            currentConfig.MaxTokens = maxTokens;
            await ReconfigureAgentAsync();
        }

        public async Task UpdateTopPAsync(double topP)
        {
            currentConfig.TopP = topP;
            await ReconfigureAgentAsync();
        }

        public async Task UpdateMaxHistoryMessagesAsync(int maxHistoryMessages)
        {
            currentConfig.MaxHistoryMessages = maxHistoryMessages;
            await ReconfigureAgentAsync();
        }

        public async Task UpdateSystemPromptAsync(string systemPrompt)
        {
            currentConfig.SystemPrompt = systemPrompt;
            await ReconfigureAgentAsync();
        }

        public async Task UpdateToolsAsync(List<string> toolNames)
        {
            currentConfig.ToolNames = toolNames;
            currentConfig.EnableTools = toolNames.Count > 0;
            await ReconfigureAgentAsync();
        }
    }

    // Helper class to bridge between dialog and agent configurations
    public class UIAgentConfiguration
    {
        public string Model { get; set; } = "anthropic/claude-sonnet-4";
        public double Temperature { get; set; } = 0.15;
        public int MaxTokens { get; set; } = 4096;
        public double TopP { get; set; } = 0.25;
        public bool EnableStreaming { get; set; } = true;
        public bool MaintainHistory { get; set; } = true;
        public int MaxHistoryMessages { get; set; } = 10;
        public string SystemPrompt { get; set; } = "";
        public bool EnableTools { get; set; } = false;
        public List<string> ToolNames { get; set; } = new List<string>();
        public bool RequireCommandApproval { get; set; } = true;

        public static async Task<List<Model>> GetAvailableModels(OpenRouterClient client)
        {
            try
            {
                var modelsResponse = await client.Models.ListAllAsync();
                return modelsResponse.Data?.ToList() ?? new List<Model>();
            }
            catch
            {
                return new List<Model>();
            }
        }
    }
}