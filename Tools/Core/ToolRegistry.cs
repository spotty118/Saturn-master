using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Saturn.OpenRouter.Models.Api.Chat;
using Saturn.Agents.Tools.Core;
using Saturn.Agents.MultiAgent;

namespace Saturn.Tools.Core
{
    /// <summary>
    /// Registry for discovering and managing tools using reflection and dependency injection.
    /// Automatically discovers all ITool implementations and makes them available for use.
    /// Enhanced with caching for improved performance.
    /// </summary>
    public class ToolRegistry : IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ToolRegistry> _logger;
        private readonly ConcurrentDictionary<string, Type> _toolTypes;
        private readonly ConcurrentDictionary<string, ITool> _toolInstances;
        private readonly ConcurrentDictionary<Type, object[]> _parametersCache;
        private readonly ConcurrentDictionary<Type, string[]> _toolDefinitionsCache;
        private readonly AgentManager agentManager;
        private readonly object _registrationLock = new object();
        private volatile bool _disposed;

        public ToolRegistry(IServiceProvider serviceProvider, ILogger<ToolRegistry> logger, AgentManager agentManager)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _toolTypes = new ConcurrentDictionary<string, Type>();
            _toolInstances = new ConcurrentDictionary<string, ITool>();
            _parametersCache = new ConcurrentDictionary<Type, object[]>();
            _toolDefinitionsCache = new ConcurrentDictionary<Type, string[]>();
            this.agentManager = agentManager;
            
            DiscoverTools();
        }
        
        private void DiscoverTools()
        {
            if (_disposed) return;
            
            lock (_registrationLock)
            {
                try
                {
                    var toolType = typeof(ITool);
                    var assembly = Assembly.GetExecutingAssembly();
                    
                    var toolTypes = assembly.GetTypes()
                        .Where(t => t.IsClass && !t.IsAbstract && toolType.IsAssignableFrom(t))
                        .ToList(); // Materialize to avoid multiple enumeration
                    
                    foreach (var type in toolTypes)
                    {
                        try
                        {
                            // Cache the tool type for future reference
                            var tool = _serviceProvider.GetService(type) as ITool;
                            if (tool == null)
                            {
                                _logger.LogWarning("Failed to create instance of tool {ToolType}", type.Name);
                                continue;
                            }
                            
                            _toolTypes.TryAdd(tool.Name, type);
                            _logger.LogDebug("Registered tool: {ToolName} ({ToolType})", tool.Name, type.Name);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to auto-register tool {ToolType}", type.Name);
                        }
                    }
                    
                    _logger.LogInformation("Discovered {ToolCount} tools", _toolTypes.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Critical error during tool auto-registration");
                    throw;
                }
            }
        }
        
        public void Register(ITool tool)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ToolRegistry));
            if (tool == null) throw new ArgumentNullException(nameof(tool));
            if (string.IsNullOrWhiteSpace(tool.Name))
                throw new ArgumentException("Tool name cannot be null or empty", nameof(tool));
            
            _toolTypes.TryAdd(tool.Name, tool.GetType());
            _toolInstances.AddOrUpdate(tool.Name, tool, (key, oldValue) => tool);
        }
        
        public void Register<T>() where T : ITool, new()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ToolRegistry));
            
            try
            {
                var tool = _serviceProvider.GetService<T>() ?? new T();
                Register(tool);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to register tool of type {typeof(T).Name}", ex);
            }
        }
        
        public ITool? Get(string name)
        {
            if (_disposed || string.IsNullOrEmpty(name)) return null;
            
            return _toolInstances.GetOrAdd(name, toolName =>
            {
                if (_toolTypes.TryGetValue(toolName, out var toolType))
                {
                    return _serviceProvider.GetService(toolType) as ITool;
                }
                return null;
            }!);
        }
        
        public ITool? GetTool(string name) => Get(name);
        
        public T? Get<T>(string name) where T : class, ITool => Get(name) as T;
        
        public bool Contains(string name) => !string.IsNullOrEmpty(name) && _toolTypes.ContainsKey(name);
        
        public IEnumerable<ITool> GetAll()
        {
            if (_disposed) return Enumerable.Empty<ITool>();
            
            return _toolTypes.Keys.Select(Get).Where(t => t != null)!;
        }
        
        public IEnumerable<string> GetAllNames()
        {
            if (_disposed) return Enumerable.Empty<string>();
            return _toolTypes.Keys;
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
            if (_disposed) return new List<ToolDefinition>();
            
            var selected = toolNames != null && toolNames.Length > 0
                ? toolNames.Select(Get).Where(t => t != null)
                : GetAll();
            return OpenRouterToolAdapter.ToOpenRouterTools(selected!);
        }
        
        public void Clear()
        {
            if (_disposed) return;
            
            _toolTypes.Clear();
            _toolInstances.Clear();
            _parametersCache.Clear();
            _toolDefinitionsCache.Clear();
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            _disposed = true;
            Clear();
        }
    }
}