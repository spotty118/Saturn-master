using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Saturn.Agents.Core;

namespace Saturn.Configuration
{
    public class ConfigurationManager
    {
        private readonly string _appDataPath;
        private readonly string _configFilePath;
        private readonly JsonSerializerOptions _jsonOptions;

        public ConfigurationManager()
        {
            _appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Saturn"
            );
            
            _configFilePath = Path.Combine(_appDataPath, "agent-config.json");
            
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Converters = { new JsonStringEnumConverter() }
            };
        }

        public async Task<PersistedAgentConfiguration?> LoadConfigurationAsync()
        {
            try
            {
                if (!File.Exists(_configFilePath))
                {
                    return null;
                }

                var json = await File.ReadAllTextAsync(_configFilePath);
                var config = JsonSerializer.Deserialize<PersistedAgentConfiguration>(json, _jsonOptions);
                
                if (config != null && config.RequireCommandApproval == null)
                {
                    config.RequireCommandApproval = true;
                }
                
                return config;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading configuration: {ex.Message}");
                return null;
            }
        }

        public async Task SaveConfigurationAsync(PersistedAgentConfiguration config)
        {
            try
            {
                if (!Directory.Exists(_appDataPath))
                {
                    Directory.CreateDirectory(_appDataPath);
                }

                var json = JsonSerializer.Serialize(config, _jsonOptions);
                await File.WriteAllTextAsync(_configFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving configuration: {ex.Message}");
            }
        }

        public PersistedAgentConfiguration FromAgentConfiguration(AgentConfiguration agentConfig)
        {
            return new PersistedAgentConfiguration
            {
                Name = agentConfig.Name,
                Model = agentConfig.Model,
                Temperature = agentConfig.Temperature,
                MaxTokens = agentConfig.MaxTokens,
                TopP = agentConfig.TopP,
                EnableStreaming = agentConfig.EnableStreaming,
                MaintainHistory = agentConfig.MaintainHistory,
                MaxHistoryMessages = agentConfig.MaxHistoryMessages,
                EnableTools = agentConfig.EnableTools,
                ToolNames = agentConfig.ToolNames,
                RequireCommandApproval = agentConfig.RequireCommandApproval
            };
        }

        public void ApplyToAgentConfiguration(AgentConfiguration target, PersistedAgentConfiguration source)
        {
            target.Name = source.Name ?? target.Name;
            target.Model = source.Model ?? target.Model;
            target.Temperature = source.Temperature ?? target.Temperature;
            target.MaxTokens = source.MaxTokens ?? target.MaxTokens;
            target.TopP = source.TopP ?? target.TopP;
            target.EnableStreaming = source.EnableStreaming;
            target.MaintainHistory = source.MaintainHistory;
            target.MaxHistoryMessages = source.MaxHistoryMessages ?? target.MaxHistoryMessages;
            target.EnableTools = source.EnableTools;
            target.ToolNames = source.ToolNames ?? target.ToolNames;
            target.RequireCommandApproval = source.RequireCommandApproval ?? true;
        }
    }

    public class PersistedAgentConfiguration
    {
        public string? Name { get; set; }
        public string? Model { get; set; }
        public double? Temperature { get; set; }
        public int? MaxTokens { get; set; }
        public double? TopP { get; set; }
        public bool EnableStreaming { get; set; } = true;
        public bool MaintainHistory { get; set; } = true;
        public int? MaxHistoryMessages { get; set; }
        public bool EnableTools { get; set; } = true;
        public List<string>? ToolNames { get; set; }
        public bool? RequireCommandApproval { get; set; }
    }
}