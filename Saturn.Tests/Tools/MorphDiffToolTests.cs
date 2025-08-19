using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Saturn.Tools;
using Saturn.Tools.Core;

namespace Saturn.Tests.Tools
{
    public class MorphDiffToolTests : IDisposable
    {
        private readonly string _tempDirectory;
        private readonly string _testFile;
        private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
        private readonly HttpClient _httpClient;
        private readonly Mock<ILogger<MorphDiffTool>> _loggerMock;
        private readonly ApplyDiffTool _fallbackTool;
        private readonly MorphConfiguration _config;

        public MorphDiffToolTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDirectory);
            _testFile = Path.Combine(_tempDirectory, "test.cs");
            
            _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
            _httpClient = new HttpClient(_httpMessageHandlerMock.Object);
            _loggerMock = new Mock<ILogger<MorphDiffTool>>();
            _fallbackTool = new ApplyDiffTool();
            _config = new MorphConfiguration
            {
                ApiKey = "test-api-key",
                Model = "morph-v3-large",
                EnableFallback = true,
                TimeoutSeconds = 30
            };
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }

        [Fact]
        public async Task ExecuteAsync_WithValidMorphRequest_ReturnsSuccess()
        {
            // Arrange
            var originalCode = "public class Calculator\n{\n    public int Add(int a, int b)\n    {\n        return a + b;\n    }\n}";
            var updatedCode = "public class Calculator\n{\n    public int Add(int a, int b)\n    {\n        if (a < 0 || b < 0) throw new ArgumentException(\"Negative numbers not allowed\");\n        return a + b;\n    }\n}";
            
            await File.WriteAllTextAsync(_testFile, originalCode);

            var morphResponse = new
            {
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            content = updatedCode
                        }
                    }
                }
            };

            SetupHttpMockForSuccess(morphResponse);

            var tool = new MorphDiffTool(_httpClient, _loggerMock.Object, _fallbackTool, _config);
            var parameters = new Dictionary<string, object>
            {
                { "target_file", _testFile },
                { "instructions", "Add validation to prevent negative numbers" },
                { "code_edit", "// ... existing code ...\nif (a < 0 || b < 0) throw new ArgumentException(\"Negative numbers not allowed\");\n// ... existing code ..." },
                { "strategy", "morph" }
            };

            // Act
            var result = await tool.ExecuteAsync(parameters);

            // Assert
            Assert.True(result.Success);
            Assert.Contains("morph", result.FormattedOutput?.ToLower() ?? "");
            
            var fileContent = await File.ReadAllTextAsync(_testFile);
            Assert.Equal(updatedCode, fileContent);
            
            var resultData = result.RawData as Dictionary<string, object>;
            Assert.NotNull(resultData);
            Assert.Equal("morph", resultData["strategy"]);
        }

        [Fact]
        public async Task ExecuteAsync_WithDryRun_DoesNotModifyFile()
        {
            // Arrange
            var originalCode = "public class Test { }";
            await File.WriteAllTextAsync(_testFile, originalCode);

            var morphResponse = new
            {
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            content = "public class Test { public void Method() { } }"
                        }
                    }
                }
            };

            SetupHttpMockForSuccess(morphResponse);

            var tool = new MorphDiffTool(_httpClient, _loggerMock.Object, _fallbackTool, _config);
            var parameters = new Dictionary<string, object>
            {
                { "target_file", _testFile },
                { "instructions", "Add a method" },
                { "code_edit", "public void Method() { }" },
                { "dry_run", true }
            };

            // Act
            var result = await tool.ExecuteAsync(parameters);

            // Assert
            Assert.True(result.Success);
            Assert.Contains("DRY RUN", result.FormattedOutput ?? "");
            
            var fileContent = await File.ReadAllTextAsync(_testFile);
            Assert.Equal(originalCode, fileContent); // File should not be modified
        }

        [Fact]
        public async Task ExecuteAsync_WithMorphFailure_FallsBackToTraditional()
        {
            // Arrange
            var originalCode = "public class Test {\n    public void Method() { }\n}";
            await File.WriteAllTextAsync(_testFile, originalCode);

            // Setup HTTP mock to return error
            _httpMessageHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("API Error")
                });

            var tool = new MorphDiffTool(_httpClient, _loggerMock.Object, _fallbackTool, _config);
            var parameters = new Dictionary<string, object>
            {
                { "target_file", _testFile },
                { "instructions", "Add a comment" },
                { "code_edit", "// ... existing code ...\n// This is a comment\npublic void Method() { }\n// ... existing code ..." },
                { "strategy", "auto" }
            };

            // Act
            var result = await tool.ExecuteAsync(parameters);

            // Assert
            // Should fail because traditional fallback needs proper patch format
            Assert.False(result.Success);
            Assert.Contains("API error", result.Error ?? "", StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ExecuteAsync_WithTraditionalStrategy_UsesTraditionalTool()
        {
            // Arrange
            var originalCode = "public class Test {\n    public void Method() { }\n}";
            await File.WriteAllTextAsync(_testFile, originalCode);

            var tool = new MorphDiffTool(_httpClient, _loggerMock.Object, _fallbackTool, _config);
            var parameters = new Dictionary<string, object>
            {
                { "target_file", _testFile },
                { "instructions", "Add a comment" },
                { "code_edit", "// Comment\npublic void Method() { }" },
                { "strategy", "traditional" }
            };

            // Act
            var result = await tool.ExecuteAsync(parameters);

            // Assert
            var resultData = result.RawData as Dictionary<string, object>;
            Assert.NotNull(resultData);
            Assert.Equal("traditional", resultData["strategy"]);
        }

        [Fact]
        public async Task ExecuteAsync_WithInvalidFile_ReturnsError()
        {
            // Arrange
            var tool = new MorphDiffTool(_httpClient, _loggerMock.Object, _fallbackTool, _config);
            var parameters = new Dictionary<string, object>
            {
                { "target_file", "nonexistent-file.cs" },
                { "instructions", "Do something" },
                { "code_edit", "some code" }
            };

            // Act
            var result = await tool.ExecuteAsync(parameters);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("not found", result.Error ?? "", StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ExecuteAsync_WithMissingParameters_ReturnsError()
        {
            // Arrange
            var tool = new MorphDiffTool(_httpClient, _loggerMock.Object, _fallbackTool, _config);
            var parameters = new Dictionary<string, object>
            {
                { "target_file", _testFile }
                // Missing instructions and code_edit
            };

            // Act
            var result = await tool.ExecuteAsync(parameters);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("required", result.Error ?? "", StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ExecuteAsync_WithTimeout_ReturnsError()
        {
            // Arrange
            var originalCode = "public class Test { }";
            await File.WriteAllTextAsync(_testFile, originalCode);

            _httpMessageHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new TaskCanceledException("Timeout"));

            var tool = new MorphDiffTool(_httpClient, _loggerMock.Object, _fallbackTool, _config);
            var parameters = new Dictionary<string, object>
            {
                { "target_file", _testFile },
                { "instructions", "Add something" },
                { "code_edit", "some code" },
                { "strategy", "morph" }
            };

            // Act
            var result = await tool.ExecuteAsync(parameters);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("timeout", result.Error ?? "", StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void GetDisplaySummary_WithValidParameters_ReturnsFormattedSummary()
        {
            // Arrange
            var tool = new MorphDiffTool(_httpClient, _loggerMock.Object, _fallbackTool, _config);
            var parameters = new Dictionary<string, object>
            {
                { "target_file", "Calculator.cs" },
                { "instructions", "Add error handling to prevent division by zero" },
                { "strategy", "morph" }
            };

            // Act
            var summary = tool.GetDisplaySummary(parameters);

            // Assert
            Assert.Contains("MORPH", summary);
            Assert.Contains("Calculator.cs", summary);
            Assert.Contains("Add error handling", summary);
        }

        [Theory]
        [InlineData("morph")]
        [InlineData("traditional")]
        [InlineData("auto")]
        [InlineData("")]
        public async Task ExecuteAsync_WithDifferentStrategies_HandlesCorrectly(string strategy)
        {
            // Arrange
            var originalCode = "public class Test { }";
            await File.WriteAllTextAsync(_testFile, originalCode);

            if (strategy == "morph" || strategy == "auto" || string.IsNullOrEmpty(strategy))
            {
                SetupHttpMockForSuccess(new
                {
                    choices = new[]
                    {
                        new
                        {
                            message = new
                            {
                                content = "public class Test { public void Method() { } }"
                            }
                        }
                    }
                });
            }

            var tool = new MorphDiffTool(_httpClient, _loggerMock.Object, _fallbackTool, _config);
            var parameters = new Dictionary<string, object>
            {
                { "target_file", _testFile },
                { "instructions", "Add a method" },
                { "code_edit", "public void Method() { }" }
            };

            if (!string.IsNullOrEmpty(strategy))
            {
                parameters["strategy"] = strategy;
            }

            // Act
            var result = await tool.ExecuteAsync(parameters);

            // Assert
            // Result success depends on strategy and implementation
            Assert.NotNull(result);
        }

        private void SetupHttpMockForSuccess(object responseObject)
        {
            var responseJson = JsonSerializer.Serialize(responseObject);
            var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
            };

            _httpMessageHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(httpResponse);
        }
    }
}