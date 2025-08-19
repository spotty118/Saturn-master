using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Saturn.Tools.Core;
using Saturn.Configuration;
using Microsoft.Extensions.Logging;

namespace Saturn.Tools
{
    /// <summary>
    /// Smart diff tool that automatically selects the best strategy (Morph or Traditional)
    /// based on configuration, availability, and fallback settings.
    /// </summary>
    public class SmartDiffTool : ToolBase
    {
        private readonly MorphDiffTool _morphTool;
        private readonly ApplyDiffTool _traditionalTool;
        private readonly MorphConfigurationManager _configManager;
        private readonly ILogger<SmartDiffTool>? _logger;

        public override string Name => "apply_diff";

        public override string Description => @"Use this tool to make intelligent changes to files with automatic strategy selection.

Features:
🚀 Automatic strategy selection (Morph AI or Traditional)
⚡ 98% accuracy with Morph vs 86% traditional
🔄 Automatic fallback if primary strategy fails
⚙️ Configurable via settings or environment variables

Strategies:
- MORPH: AI-powered semantic understanding (~6 seconds, 98% accuracy)
- TRADITIONAL: Exact pattern matching (~35 seconds, 86% accuracy)  
- AUTO: Try Morph first, fallback to Traditional (recommended)

Setup:
1. Set MORPH_API_KEY environment variable for AI-powered editing
2. Use 'strategy' parameter to override default behavior
3. Enable 'dry_run' to preview changes without applying

Example usage:
```
target_file: src/Calculator.cs
instructions: Add null check to the divide method
code_edit:
// ... existing code ...
public double Divide(double a, double b)
{
    if (b == 0) throw new ArgumentException(""Cannot divide by zero"");
    return a / b;
}
// ... existing code ...
```

The tool automatically selects the optimal strategy and applies changes intelligently.";

        public SmartDiffTool(
            MorphDiffTool morphTool,
            ApplyDiffTool traditionalTool,
            MorphConfigurationManager configManager,
            ILogger<SmartDiffTool>? logger = null)
        {
            _morphTool = morphTool;
            _traditionalTool = traditionalTool;
            _configManager = configManager;
            _logger = logger;
        }

        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                { "target_file", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "The target file to modify. Can be existing file for updates or new file path for creation." }
                    }
                },
                { "instructions", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "Clear description of what you are changing, written in first person. Helps AI understand intent." }
                    }
                },
                { "code_edit", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "The code changes to apply. For Morph: use '// ... existing code ...' markers. For Traditional: use full patch format." }
                    }
                },
                { "strategy", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "Override strategy: 'auto' (recommended), 'morph', 'traditional'. Default uses configuration setting." },
                        { "enum", new[] { "auto", "morph", "traditional" } }
                    }
                },
                { "dry_run", new Dictionary<string, object>
                    {
                        { "type", "boolean" },
                        { "description", "Preview changes without applying them. Shows which strategy would be used and what would change." }
                    }
                },
                { "patchText", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "Traditional patch format (alternative to target_file + code_edit). Automatically uses traditional strategy." }
                    }
                }
            };
        }

        protected override string[] GetRequiredParameters()
        {
            return new string[0]; // No absolutely required parameters - we'll validate based on what's provided
        }

        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            var targetFile = GetParameter<string>(parameters, "target_file", "");
            var instructions = GetParameter<string>(parameters, "instructions", "");
            var patchText = GetParameter<string>(parameters, "patchText", "");
            var strategy = GetParameter<string>(parameters, "strategy", "auto");

            if (!string.IsNullOrEmpty(patchText))
            {
                // Traditional patch format
                return _traditionalTool.GetDisplaySummary(parameters);
            }
            else if (!string.IsNullOrEmpty(targetFile))
            {
                // Morph-style format
                var fileName = Path.GetFileName(targetFile);
                var shortInstructions = TruncateString(instructions, 30);
                return $"[{strategy.ToUpper()}] {shortInstructions} → {fileName}";
            }

            return "Smart diff application";
        }

        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            try
            {
                var strategy = await DetermineStrategyAsync(parameters);
                var startTime = DateTime.UtcNow;

                _logger?.LogInformation("SmartDiffTool executing with strategy: {Strategy}", strategy);

                ToolResult result;

                // Route to appropriate tool based on determined strategy
                if (UseTraditionalPatchFormat(parameters))
                {
                    // Traditional patch format provided - use traditional tool directly
                    result = await _traditionalTool.ExecuteAsync(parameters);
                    await LogResult(result, "traditional (patch format)", startTime);
                }
                else if (strategy == DiffStrategy.Traditional)
                {
                    // Convert to traditional and execute
                    result = await ExecuteWithTraditional(parameters);
                    await LogResult(result, "traditional", startTime);
                }
                else if (strategy == DiffStrategy.Morph)
                {
                    // Execute with Morph
                    result = await _morphTool.ExecuteAsync(parameters);
                    await LogResult(result, "morph", startTime);
                }
                else // Auto strategy
                {
                    // Try Morph first, fallback to Traditional
                    result = await ExecuteWithAutoStrategy(parameters);
                    await LogResult(result, "auto", startTime);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "SmartDiffTool execution failed");
                return CreateErrorResult($"Smart diff execution failed: {ex.Message}");
            }
        }

        private async Task<DiffStrategy> DetermineStrategyAsync(Dictionary<string, object> parameters)
        {
            // Check for explicit strategy parameter
            var explicitStrategy = GetParameter<string>(parameters, "strategy", "");
            if (!string.IsNullOrEmpty(explicitStrategy))
            {
                return explicitStrategy.ToLowerInvariant() switch
                {
                    "morph" => DiffStrategy.Morph,
                    "traditional" => DiffStrategy.Traditional,
                    "auto" => DiffStrategy.Auto,
                    _ => DiffStrategy.Auto
                };
            }

            // Use configuration default
            try
            {
                return await _configManager.GetDefaultStrategyAsync();
            }
            catch
            {
                // Fallback to auto if config fails
                return DiffStrategy.Auto;
            }
        }

        private bool UseTraditionalPatchFormat(Dictionary<string, object> parameters)
        {
            var patchText = GetParameter<string>(parameters, "patchText", "");
            return !string.IsNullOrEmpty(patchText);
        }

        private async Task<ToolResult> ExecuteWithTraditional(Dictionary<string, object> parameters)
        {
            // For traditional execution, we need to convert Morph-style parameters to traditional patch format
            var targetFile = GetParameter<string>(parameters, "target_file", "");
            var instructions = GetParameter<string>(parameters, "instructions", "");
            var codeEdit = GetParameter<string>(parameters, "code_edit", "");

            if (string.IsNullOrEmpty(targetFile) || string.IsNullOrEmpty(codeEdit))
            {
                return CreateErrorResult("target_file and code_edit parameters are required for traditional strategy");
            }

            // Let MorphDiffTool handle the conversion to traditional patch format
            var morphParams = new Dictionary<string, object>(parameters)
            {
                ["strategy"] = "traditional"
            };

            return await _morphTool.ExecuteAsync(morphParams);
        }

        private async Task<ToolResult> ExecuteWithAutoStrategy(Dictionary<string, object> parameters)
        {
            // Check if Morph is available
            var isConfigured = await _configManager.IsConfiguredAsync();
            
            if (!isConfigured)
            {
                _logger?.LogInformation("Morph not configured, using traditional strategy");
                return await ExecuteWithTraditional(parameters);
            }

            // Try Morph first
            var morphParams = new Dictionary<string, object>(parameters)
            {
                ["strategy"] = "morph"
            };

            var result = await _morphTool.ExecuteAsync(morphParams);

            // If Morph fails, try traditional as fallback
            if (!result.Success)
            {
                _logger?.LogWarning("Morph strategy failed, falling back to traditional: {Error}", result.Error);
                
                try
                {
                    result = await ExecuteWithTraditional(parameters);
                    
                    // Mark as fallback in the result
                    if (result.Success && result.RawData is Dictionary<string, object> resultData)
                    {
                        resultData["strategy_used"] = "traditional (fallback)";
                        resultData["morph_fallback_reason"] = "Morph strategy failed";
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Traditional fallback also failed");
                    return CreateErrorResult($"Both Morph and Traditional strategies failed. Morph: {result.Error}, Traditional: {ex.Message}");
                }
            }

            return result;
        }

        private async Task LogResult(ToolResult result, string strategy, DateTime startTime)
        {
            var duration = DateTime.UtcNow - startTime;
            
            if (result.Success)
            {
                _logger?.LogInformation("SmartDiffTool completed successfully with {Strategy} in {Duration}ms", 
                    strategy, (int)duration.TotalMilliseconds);
            }
            else
            {
                _logger?.LogWarning("SmartDiffTool failed with {Strategy} after {Duration}ms: {Error}", 
                    strategy, (int)duration.TotalMilliseconds, result.Error);
            }

            await Task.CompletedTask;
        }
    }
}