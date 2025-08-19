using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Saturn.Tools;
using Xunit;

namespace Saturn.Tests.Tools
{
    public class MorphDiffToolEdgeTests : IDisposable
    {
        private readonly string _tempDirectory;
        private readonly string _testFile;
        private readonly Mock<HttpMessageHandler> _httpHandler;
        private readonly HttpClient _httpClient;
        private readonly Mock<ILogger<MorphDiffTool>> _logger;
        private readonly ApplyDiffTool _fallbackTool;
        private readonly MorphConfiguration _config;

        public MorphDiffToolEdgeTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDirectory);
            _testFile = Path.Combine(_tempDirectory, "edge.cs");

            _httpHandler = new Mock<HttpMessageHandler>();
            _httpClient = new HttpClient(_httpHandler.Object);
            _logger = new Mock<ILogger<MorphDiffTool>>();
            _fallbackTool = new ApplyDiffTool();
            _config = new MorphConfiguration
            {
                ApiKey = "test-api-key",
                Model = "morph-v3-large",
                EnableFallback = true,
                TimeoutSeconds = 5
            };
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
            try
            {
                if (Directory.Exists(_tempDirectory))
                {
                    Directory.Delete(_tempDirectory, recursive: true);
                }
            }
            catch
            {
                // ignore cleanup errors
            }
        }

        [Fact]
        public async Task Auto_With429_AndPatch_FallsBack_Succeeds()
        {
            // Arrange
            var original = "public class Test {\n    public void Method() { }\n}";
            await File.WriteAllTextAsync(_testFile, original);

            _httpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("Rate limit exceeded")
                });

            var tool = new MorphDiffTool(_httpClient, _logger.Object, _fallbackTool, _config);

            // Provide a proper traditional patch so fallback can succeed deterministically
            var patchText = $"*** Update File: {_testFile}\n@@ public void Method() {{ }} @@\n+// added via fallback";
            var parameters = new Dictionary<string, object>
            {
                { "target_file", _testFile },
                { "instructions", "Add comment" },
                { "code_edit", patchText },
                { "strategy", "auto" }
            };

            // Act
            var result = await tool.ExecuteAsync(parameters);

            // Assert
            Assert.True(result.Success);
            var fileContent = await File.ReadAllTextAsync(_testFile);
            Assert.Contains("// added via fallback", fileContent);

            var data = result.RawData as Dictionary<string, object>;
            Assert.NotNull(data);
            Assert.Equal("traditional", data["strategy"]);
        }

        [Fact]
        public async Task Auto_With429_AndNonPatch_FallbackFails_ReturnsCombinedError()
        {
            // Arrange
            var original = "public class Test { }";
            await File.WriteAllTextAsync(_testFile, original);

            _httpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("Rate limit exceeded")
                });

            var tool = new MorphDiffTool(_httpClient, _logger.Object, _fallbackTool, _config);

            // Non-patch edit text that will be converted into a patch with a context
            // line unlikely to be found, causing the fallback to fail.
            var codeEdit = "// ... existing code ...\n// line that will not match any context\n// ... existing code ...";

            var parameters = new Dictionary<string, object>
            {
                { "target_file", _testFile },
                { "instructions", "Add a line that fails context" },
                { "code_edit", codeEdit },
                { "strategy", "auto" }
            };

            // Act
            var result = await tool.ExecuteAsync(parameters);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Morph API error", result.Error ?? "", StringComparison.OrdinalIgnoreCase);
            Assert.Contains("fallback failed", result.Error ?? "", StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Auto_With429_DryRun_DoesNotWrite()
        {
            // Arrange
            var original = "public class Test {\n    public void Method() { }\n}";
            await File.WriteAllTextAsync(_testFile, original);

            _httpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("Rate limit exceeded")
                });

            var tool = new MorphDiffTool(_httpClient, _logger.Object, _fallbackTool, _config);
            var patchText = $"*** Update File: {_testFile}\n@@ public void Method() {{ }} @@\n+// added via fallback (dryrun)";
            var parameters = new Dictionary<string, object>
            {
                { "target_file", _testFile },
                { "instructions", "Add comment via dry run" },
                { "code_edit", patchText },
                { "strategy", "auto" },
                { "dry_run", true }
            };

            // Act
            var result = await tool.ExecuteAsync(parameters);

            // Assert
            Assert.True(result.Success);
            var fileContent = await File.ReadAllTextAsync(_testFile);
            Assert.Equal(original, fileContent); // no write occurred
        }
    }
}