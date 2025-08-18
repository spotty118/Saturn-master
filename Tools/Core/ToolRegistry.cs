using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Saturn.OpenRouter.Models.Api.Chat;
using Saturn.Agents.Tools.Core;
using Saturn.Agents.MultiAgent;

namespace Saturn.Tools.Core
{
    public class ToolRegistry
    {
        private readonly Dictionary<string, ITool> _tools = new Dictionary<string, ITool>(StringComparer.OrdinalIgnoreCase);
        private readonly AgentManager agentManager;
        
        public ToolRegistry(AgentManager agentManager)
        {
            this.agentManager = agentManager;
            AutoRegisterTools();
        }
        
        private void AutoRegisterTools()
        {
            var toolType = typeof(ITool);
            var assembly = Assembly.GetExecutingAssembly();
            
            var toolTypes = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && toolType.IsAssignableFrom(t));
            
            foreach (var type in toolTypes)
            {
                try
                {
                    var tool = (ITool)Activator.CreateInstance(type);
                    
                    // Inject dependencies if the tool needs them
                    if (tool is IDependencyInjectable injectable)
                    {
                        injectable.InjectDependencies(agentManager);
                    }
                    
                    Register(tool);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to auto-register tool {type.Name}: {ex.Message}");
                }
            }
        }
        
        public void Register(ITool tool)
        {
            if (tool == null)
            {
                throw new ArgumentNullException(nameof(tool));
            }
            
            _tools[tool.Name] = tool;
        }
        
        public void Register<T>() where T : ITool, new()
        {
            var tool = new T();
            
            // Inject dependencies if the tool needs them
            if (tool is IDependencyInjectable injectable)
            {
                injectable.InjectDependencies(agentManager);
            }
            
            Register(tool);
        }
        
        public ITool Get(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }
            
            _tools.TryGetValue(name, out var tool);
            return tool;
        }
        
        public ITool GetTool(string name)
        {
            return Get(name);
        }
        
        public T Get<T>(string name) where T : class, ITool
        {
            return Get(name) as T;
        }
        
        public bool Contains(string name)
        {
            return !string.IsNullOrEmpty(name) && _tools.ContainsKey(name);
        }
        
        public IEnumerable<ITool> GetAll()
        {
            return _tools.Values;
        }
        
        public IEnumerable<string> GetAllNames()
        {
            return _tools.Keys;
        }

        /// <summary>
        /// Returns OpenRouter-ready tool definitions for all registered tools.
        /// </summary>
        public List<ToolDefinition> GetOpenRouterToolDefinitions()
        {
            return OpenRouterToolAdapter.ToOpenRouterTools(GetAll());
        }

        /// <summary>
        /// Returns OpenRouter-ready tool definitions filtered by provided tool names.
        /// </summary>
        public List<ToolDefinition> GetOpenRouterToolDefinitions(params string[] toolNames)
        {
            var selected = toolNames != null && toolNames.Length > 0
                ? toolNames.Select(Get).Where(t => t != null)
                : GetAll();
            return OpenRouterToolAdapter.ToOpenRouterTools(selected!);
        }
        
        public void Clear()
        {
            _tools.Clear();
        }
    }
}