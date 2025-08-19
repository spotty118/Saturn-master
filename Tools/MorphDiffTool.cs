using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Saturn.Tools.Core;
using Microsoft.Extensions.Logging;

namespace Saturn.Tools
{
    public class MorphDiffTool : ToolBase
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<MorphDiffTool>? _logger;
        private readonly ApplyDiffTool _fallbackTool;
        private readonly MorphConfiguration _config;

        public override string Name => "morph_diff";

        public override string Description => @"Use this tool to make fast, intelligent changes to files using AI-powered semantic diff application.

This tool uses Morph's Fast Apply technology for dramatically improved performance and accuracy:
- 98% accuracy vs traditional 86%
- ~6 seconds vs 35+ seconds execution time
- Semantic understanding vs exact string matching
- Automatic fallback to traditional diff if needed

Usage:
1. Provide clear instructions describing the change
2. Specify the target file to modify
3. Provide the code edit with minimal context using // ... existing code ...

The tool automatically applies changes intelligently, understanding code intent rather than requiring exact matches.

Example format:
```
target_file: src/Example.cs
instructions: Add error handling to the divide method
code_edit: 
// ... existing code ...
public double Divide(double a, double b)
{
    if (b == 0) throw new DivideByZeroException(""Cannot divide by zero"");
    return a / b;
}
// ... existing code ...
```";

        public MorphDiffTool() : this(new HttpClient(), null, new ApplyDiffTool(), MorphConfiguration.Default)
        {
        }

        public MorphDiffTool(HttpClient httpClient, ILogger<MorphDiffTool>? logger, ApplyDiffTool fallbackTool, MorphConfiguration config)
        {
            _httpClient = httpClient;
            _logger = logger;
            _fallbackTool = fallbackTool;
            _config = config;
            
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_config.ApiKey}");
            _httpClient.DefaultRequestHeaders.Add("Content-Type", "application/json");
            _httpClient.Timeout = TimeSpan.FromSeconds(_config.TimeoutSeconds);
        }

        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                { "target_file", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "The target file to modify. Must be an existing file." }
                    }
                },
                { "instructions", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "A single sentence describing what you are changing. Written in first person to help disambiguate the edit intent." }
                    }
                },
                { "code_edit", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "Specify ONLY the precise lines of code to edit. Use '// ... existing code ...' for unchanged sections. Provide minimal context around changes." }
                    }
                },
                { "strategy", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "Override diff strategy: 'morph', 'traditional', or 'auto'. Default is 'auto'." },
                        { "enum", new[] { "morph", "traditional", "auto" } }
                    }
                },
                { "dry_run", new Dictionary<string, object>
                    {
                        { "type", "boolean" },
                        { "description", "If true, shows what would be changed without modifying files. Default is false." }
                    }
                }
            };
        }

        protected override string[] GetRequiredParameters()
        {
            return new[] { "target_file", "instructions", "code_edit" };
        }

        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            var targetFile = GetParameter<string>(parameters, "target_file", "");
            var instructions = GetParameter<string>(parameters, "instructions", "");
            var strategy = GetParameter<string>(parameters, "strategy", "auto");
            
            var fileName = string.IsNullOrEmpty(targetFile) ? "file" : Path.GetFileName(targetFile);
            var shortInstructions = TruncateString(instructions, 40);
            
            return $"[{strategy.ToUpper()}] {shortInstructions} → {fileName}";
        }

        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            var targetFile = GetParameter<string>(parameters, "target_file");
            var instructions = GetParameter<string>(parameters, "instructions");
            var codeEdit = GetParameter<string>(parameters, "code_edit");
            var strategy = ParseStrategy(GetParameter<string>(parameters, "strategy", "auto"));
            var dryRun = GetParameter<bool>(parameters, "dry_run", false);

            // Validation
            if (string.IsNullOrEmpty(targetFile))
                return CreateErrorResult("target_file parameter is required");
            if (string.IsNullOrEmpty(instructions))
                return CreateErrorResult("instructions parameter is required");
            if (string.IsNullOrEmpty(codeEdit))
                return CreateErrorResult("code_edit parameter is required");

            try
            {
                ValidateFile(targetFile);
                
                var startTime = DateTime.UtcNow;
                var originalContent = await File.ReadAllTextAsync(targetFile);
                
                ToolResult result;
                string strategyUsed;

                // Strategy execution with fallback
                if (strategy == DiffStrategy.Traditional)
                {
                    result = await ExecuteTraditionalDiff(targetFile, instructions, codeEdit, dryRun, originalContent);
                    strategyUsed = "traditional";
                }
                else if (strategy == DiffStrategy.Morph && !string.IsNullOrEmpty(_config.ApiKey))
                {
                    result = await ExecuteMorphDiff(targetFile, instructions, codeEdit, dryRun, originalContent);
                    strategyUsed = "morph";
                }
                else if (strategy == DiffStrategy.Auto)
                {
                    if (!string.IsNullOrEmpty(_config.ApiKey))
                    {
                        result = await ExecuteMorphDiff(targetFile, instructions, codeEdit, dryRun, originalContent);
                        strategyUsed = "morph";
                        
                        // Fallback to traditional if Morph fails
                        if (!result.Success && _config.EnableFallback)
                        {
                            _logger?.LogWarning("Morph strategy failed, falling back to traditional: {Error}", result.Error);
                            result = await ExecuteTraditionalDiff(targetFile, instructions, codeEdit, dryRun, originalContent);
                            strategyUsed = "traditional (fallback)";
                        }
                    }
                    else
                    {
                        result = await ExecuteTraditionalDiff(targetFile, instructions, codeEdit, dryRun, originalContent);
                        strategyUsed = "traditional (no API key)";
                    }
                }
                else
                {
                    return CreateErrorResult("Morph strategy requires API key. Set MORPH_API_KEY environment variable or use traditional strategy.");
                }

                // Add performance metrics
                var executionTime = DateTime.UtcNow - startTime;
                if (result.Success && result.RawData is Dictionary<string, object> resultData)
                {
                    resultData["strategy_used"] = strategyUsed;
                    resultData["execution_time_ms"] = (int)executionTime.TotalMilliseconds;
                    resultData["file_size_bytes"] = originalContent.Length;
                }

                _logger?.LogInformation("MorphDiffTool completed: {Strategy} in {Duration}ms", 
                    strategyUsed, (int)executionTime.TotalMilliseconds);

                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "MorphDiffTool execution failed for file: {File}", targetFile);
                return CreateErrorResult($"Failed to apply diff: {ex.Message}");
            }
        }

        private async Task<ToolResult> ExecuteMorphDiff(string targetFile, string instructions, string codeEdit, bool dryRun, string originalContent)
        {
            try
            {
                var requestBody = new
                {
                    model = _config.Model,
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content = $"<instruction>{instructions}</instruction>\n<code>{originalContent}</code>\n<update>{codeEdit}</update>"
                        }
                    }
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("https://api.morphllm.com/v1/chat/completions", content);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new HttpRequestException($"Morph API error: {response.StatusCode} - {errorContent}");
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                var morphResponse = JsonSerializer.Deserialize<MorphApiResponse>(responseJson);

                if (morphResponse?.Choices == null || morphResponse.Choices.Count == 0)
                {
                    throw new InvalidOperationException("Invalid response from Morph API: no choices returned");
                }

                var updatedContent = morphResponse.Choices[0].Message?.Content;
                if (string.IsNullOrEmpty(updatedContent))
                {
                    throw new InvalidOperationException("Invalid response from Morph API: empty content");
                }

                var result = new
                {
                    strategy = "morph",
                    target_file = targetFile,
                    original_length = originalContent.Length,
                    updated_length = updatedContent.Length,
                    changes_detected = originalContent != updatedContent,
                    dry_run = dryRun
                };

                if (dryRun)
                {
                    var formattedOutput = $"[DRY RUN] Morph would update {Path.GetFileName(targetFile)} ({originalContent.Length} → {updatedContent.Length} chars)";
                    return CreateSuccessResult(result, formattedOutput);
                }

                // Apply the changes
                await File.WriteAllTextAsync(targetFile, updatedContent);

                var successOutput = $"Morph applied changes to {Path.GetFileName(targetFile)} ({originalContent.Length} → {updatedContent.Length} chars)";
                return CreateSuccessResult(result, successOutput);
            }
            catch (TaskCanceledException)
            {
                throw new TimeoutException($"Morph API request timed out after {_config.TimeoutSeconds} seconds");
            }
        }

        private async Task<ToolResult> ExecuteTraditionalDiff(string targetFile, string instructions, string codeEdit, bool dryRun, string originalContent)
        {
            // Convert to traditional patch format
            var patchText = ConvertToTraditionalPatch(targetFile, codeEdit);
            
            var traditionalParams = new Dictionary<string, object>
            {
                { "patchText", patchText },
                { "dryRun", dryRun }
            };

            var result = await _fallbackTool.ExecuteAsync(traditionalParams);
            
            // Enhance result with strategy info
            if (result.Success && result.RawData is Dictionary<string, object> resultData)
            {
                resultData["strategy"] = "traditional";
                resultData["original_instructions"] = instructions;
            }

            return result;
        }

        private string ConvertToTraditionalPatch(string targetFile, string codeEdit)
        {
            // Simple conversion - this could be enhanced for more complex scenarios
            var lines = codeEdit.Split('\n');
            var patchBuilder = new StringBuilder();
            
            patchBuilder.AppendLine($"*** Update File: {targetFile}");
            
            var contextFound = false;
            var changeLines = new List<string>();
            
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                
                if (trimmedLine.StartsWith("// ... existing code ...") || trimmedLine.StartsWith("/* ... existing code ... */"))
                {
                    if (changeLines.Count > 0 && !contextFound)
                    {
                        // Use first non-comment line as context
                        var contextLine = changeLines.FirstOrDefault(l => !l.Trim().StartsWith("//") && !l.Trim().StartsWith("/*") && !string.IsNullOrWhiteSpace(l.Trim()));
                        if (!string.IsNullOrEmpty(contextLine))
                        {
                            patchBuilder.AppendLine($"@@ {contextLine.Trim()} @@");
                            contextFound = true;
                            
                            foreach (var changeLine in changeLines)
                            {
                                patchBuilder.AppendLine($"+{changeLine}");
                            }
                            changeLines.Clear();
                        }
                    }
                }
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    changeLines.Add(line);
                }
            }
            
            // Add remaining changes
            if (changeLines.Count > 0 && !contextFound)
            {
                var contextLine = changeLines.FirstOrDefault(l => !l.Trim().StartsWith("//") && !string.IsNullOrWhiteSpace(l.Trim()));
                if (!string.IsNullOrEmpty(contextLine))
                {
                    patchBuilder.AppendLine($"@@ {contextLine.Trim()} @@");
                    foreach (var changeLine in changeLines)
                    {
                        patchBuilder.AppendLine($"+{changeLine}");
                    }
                }
            }
            
            return patchBuilder.ToString();
        }

        private void ValidateFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty");

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length > _config.MaxFileSizeBytes)
                throw new InvalidOperationException($"File too large ({fileInfo.Length} bytes). Maximum size is {_config.MaxFileSizeBytes} bytes.");
        }

        private DiffStrategy ParseStrategy(string strategy)
        {
            return strategy?.ToLowerInvariant() switch
            {
                "morph" => DiffStrategy.Morph,
                "traditional" => DiffStrategy.Traditional,
                "auto" => DiffStrategy.Auto,
                _ => DiffStrategy.Auto
            };
        }
    }

    public enum DiffStrategy
    {
        Auto,
        Morph,
        Traditional
    }

    public class MorphConfiguration
    {
        public string ApiKey { get; set; } = Environment.GetEnvironmentVariable("MORPH_API_KEY") ?? "";
        public string Model { get; set; } = "morph-v3-large";
        public DiffStrategy DefaultStrategy { get; set; } = DiffStrategy.Auto;
        public bool EnableFallback { get; set; } = true;
        public int TimeoutSeconds { get; set; } = 30;
        public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024; // 10MB

        public static MorphConfiguration Default => new();
    }

    // Morph API Response Models
    public class MorphApiResponse
    {
        public List<MorphChoice>? Choices { get; set; }
    }

    public class MorphChoice
    {
        public MorphMessage? Message { get; set; }
    }

    public class MorphMessage
    {
        public string? Content { get; set; }
    }
}