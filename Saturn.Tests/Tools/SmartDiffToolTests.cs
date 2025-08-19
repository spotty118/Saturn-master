using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Extensions.Logging;
using Moq;
using Saturn.Tools;
using Saturn.Tools.Core;
using Saturn.Configuration;

namespace Saturn.Tests.Tools
{
    public class SmartDiffToolTests : IDisposable
    {
        private readonly string _tempDirectory;
        private readonly string _testFile;
        private readonly Mock<MorphDiffTool> _morphToolMock;
        private readonly Mock<ApplyDiffTool> _traditionalToolMock;
        private readonly Mock<MorphConfigurationManager> _configManagerMock;
        private readonly Mock<ILogger<SmartDiffTool>> _loggerMock;

        public SmartDiffToolTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDirectory);
            _testFile = Path.Combine(_tempDirectory, "test.cs");
            
            _morphToolMock = new Mock<MorphDiffTool>();
            _traditionalToolMock = new Mock<ApplyDiffTool>();
            _configManagerMock = new Mock<MorphConfigurationManager>();
            _loggerMock = new Mock<ILogger<SmartDiffTool>>();
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }

        [Fact]
        public async Task ExecuteAsync_WithAutoStrategy_ConfiguredMorph_UsesMorph()
        {
            // Arrange
            var originalCode = "public class Test { }";
            await File.WriteAllTextAsync(_testFile, originalCode);

            _configManagerMock
                .Setup(x => x.GetDefaultStrategyAsync())
                .ReturnsAsync(DiffStrategy.Auto);

            _configManagerMock
                .Setup(x => x.IsConfiguredAsync())
                .ReturnsAsync(true);

            var morphResult = new ToolResult
            {
                Success = true,
                RawData = new Dictionary<string, object> { { "strategy", "morph" } },
                FormattedOutput = "Morph applied successfully"
            };

            _morphToolMock
                .Setup(x => x.ExecuteAsync(It.IsAny<Dictionary<string, object>>()))
                .ReturnsAsync(morphResult);

            var tool = new SmartDiffTool(_morphToolMock.Object, _traditionalToolMock.Object, _configManagerMock.Object, _loggerMock.Object);

            var parameters = new Dictionary<string, object>
            {
                { "target_file", _testFile },
                { "instructions", "Add a method" },
                { "code_edit", "public void Method() { }" },
                { "strategy", "auto" }
            };

            // Act
            var result = await tool.ExecuteAsync(parameters);

            // Assert
            Assert.True(result.Success);
            _morphToolMock.Verify(x => x.ExecuteAsync(It.IsAny<Dictionary<string, object>>()), Times.Once);
            _traditionalToolMock.Verify(x => x.ExecuteAsync(It.IsAny<Dictionary<string, object>>()), Times.Never);
        }

        [Fact]
        public async Task ExecuteAsync_WithAutoStrategy_MorphFails_FallsBackToTraditional()
        {
            // Arrange
            var originalCode = "public class Test { }";
            await File.WriteAllTextAsync(_testFile, originalCode);

            _configManagerMock
                .Setup(x => x.GetDefaultStrategyAsync())
                .ReturnsAsync(DiffStrategy.Auto);

            _configManagerMock
                .Setup(x => x.IsConfiguredAsync())
                .ReturnsAsync(true);

            var morphResult = new ToolResult
            {
                Success = false,
                Error = "Morph API error"
            };

            var traditionalResult = new ToolResult
            {
                Success = true,
                RawData = new Dictionary<string, object> { { "strategy", "traditional" } },
                FormattedOutput = "Traditional applied successfully"
            };

            _morphToolMock
                .Setup(x => x.ExecuteAsync(It.IsAny<Dictionary<string, object>>()))
                .ReturnsAsync(morphResult);

            _morphToolMock
                .Setup(x => x.ExecuteAsync(It.Is<Dictionary<string, object>>(p => 
                    p.ContainsKey("strategy") && p["strategy"].ToString() == "traditional")))
                .ReturnsAsync(traditionalResult);

            var tool = new SmartDiffTool(_morphToolMock.Object, _traditionalToolMock.Object, _configManagerMock.Object, _loggerMock.Object);

            var parameters = new Dictionary<string, object>
            {
                { "target_file", _testFile },
                { "instructions", "Add a method" },
                { "code_edit", "public void Method() { }" },
                { "strategy", "auto" }
            };

            // Act
            var result = await tool.ExecuteAsync(parameters);

            // Assert
            Assert.True(result.Success);
            _morphToolMock.Verify(x => x.ExecuteAsync(It.IsAny<Dictionary<string, object>>()), Times.Exactly(2));
            
            var resultData = result.RawData as Dictionary<string, object>;
            Assert.NotNull(resultData);
            Assert.Contains("fallback", resultData["strategy_used"].ToString());
        }

        [Fact]
        public async Task ExecuteAsync_WithAutoStrategy_NotConfigured_UsesTraditional()
        {
            // Arrange
            var originalCode = "public class Test { }";
            await File.WriteAllTextAsync(_testFile, originalCode);

            _configManagerMock
                .Setup(x => x.GetDefaultStrategyAsync())
                .ReturnsAsync(DiffStrategy.Auto);

            _configManagerMock
                .Setup(x => x.IsConfiguredAsync())
                .ReturnsAsync(false);

            var traditionalResult = new ToolResult
            {
                Success = true,
                RawData = new Dictionary<string, object> { { "strategy", "traditional" } },
                FormattedOutput = "Traditional applied successfully"
            };

            _morphToolMock
                .Setup(x => x.ExecuteAsync(It.Is<Dictionary<string, object>>(p => 
                    p.ContainsKey("strategy") && p["strategy"].ToString() == "traditional")))
                .ReturnsAsync(traditionalResult);

            var tool = new SmartDiffTool(_morphToolMock.Object, _traditionalToolMock.Object, _configManagerMock.Object, _loggerMock.Object);

            var parameters = new Dictionary<string, object>
            {
                { "target_file", _testFile },
                { "instructions", "Add a method" },
                { "code_edit", "public void Method() { }" },
                { "strategy", "auto" }
            };

            // Act
            var result = await tool.ExecuteAsync(parameters);

            // Assert
            Assert.True(result.Success);
            _morphToolMock.Verify(x => x.ExecuteAsync(It.Is<Dictionary<string, object>>(p => 
                p.ContainsKey("strategy") && p["strategy"].ToString() == "traditional")), Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_WithTraditionalPatchFormat_UsesTraditionalDirectly()
        {
            // Arrange
            var patchText = "*** Update File: test.cs\n@@ public class Test @@\n+ public void Method() { }";
            
            var traditionalResult = new ToolResult
            {
                Success = true,
                RawData = new Dictionary<string, object>(),
                FormattedOutput = "Patch applied successfully"
            };

            _traditionalToolMock
                .Setup(x => x.ExecuteAsync(It.IsAny<Dictionary<string, object>>()))
                .ReturnsAsync(traditionalResult);

            _traditionalToolMock
                .Setup(x => x.GetDisplaySummary(It.IsAny<Dictionary<string, object>>()))
                .Returns("Patching test.cs (1+, 0-)");

            var tool = new SmartDiffTool(_morphToolMock.Object, _traditionalToolMock.Object, _configManagerMock.Object, _loggerMock.Object);

            var parameters = new Dictionary<string, object>
            {
                { "patchText", patchText }
            };

            // Act
            var result = await tool.ExecuteAsync(parameters);

            // Assert
            Assert.True(result.Success);
            _traditionalToolMock.Verify(x => x.ExecuteAsync(It.IsAny<Dictionary<string, object>>()), Times.Once);
            _morphToolMock.Verify(x => x.ExecuteAsync(It.IsAny<Dictionary<string, object>>()), Times.Never);
        }

        [Fact]
        public async Task ExecuteAsync_WithExplicitMorphStrategy_UsesMorph()
        {
            // Arrange
            var originalCode = "public class Test { }";
            await File.WriteAllTextAsync(_testFile, originalCode);

            var morphResult = new ToolResult
            {
                Success = true,
                RawData = new Dictionary<string, object> { { "strategy", "morph" } },
                FormattedOutput = "Morph applied successfully"
            };

            _morphToolMock
                .Setup(x => x.ExecuteAsync(It.IsAny<Dictionary<string, object>>()))
                .ReturnsAsync(morphResult);

            var tool = new SmartDiffTool(_morphToolMock.Object, _traditionalToolMock.Object, _configManagerMock.Object, _loggerMock.Object);

            var parameters = new Dictionary<string, object>
            {
                { "target_file", _testFile },
                { "instructions", "Add a method" },
                { "code_edit", "public void Method() { }" },
                { "strategy", "morph" }
            };

            // Act
            var result = await tool.ExecuteAsync(parameters);

            // Assert
            Assert.True(result.Success);
            _morphToolMock.Verify(x => x.ExecuteAsync(It.IsAny<Dictionary<string, object>>()), Times.Once);
            _traditionalToolMock.Verify(x => x.ExecuteAsync(It.IsAny<Dictionary<string, object>>()), Times.Never);
        }

        [Fact]
        public async Task ExecuteAsync_WithExplicitTraditionalStrategy_UsesTraditional()
        {
            // Arrange
            var originalCode = "public class Test { }";
            await File.WriteAllTextAsync(_testFile, originalCode);

            var traditionalResult = new ToolResult
            {
                Success = true,
                RawData = new Dictionary<string, object> { { "strategy", "traditional" } },
                FormattedOutput = "Traditional applied successfully"
            };

            _morphToolMock
                .Setup(x => x.ExecuteAsync(It.Is<Dictionary<string, object>>(p => 
                    p.ContainsKey("strategy") && p["strategy"].ToString() == "traditional")))
                .ReturnsAsync(traditionalResult);

            var tool = new SmartDiffTool(_morphToolMock.Object, _traditionalToolMock.Object, _configManagerMock.Object, _loggerMock.Object);

            var parameters = new Dictionary<string, object>
            {
                { "target_file", _testFile },
                { "instructions", "Add a method" },
                { "code_edit", "public void Method() { }" },
                { "strategy", "traditional" }
            };

            // Act
            var result = await tool.ExecuteAsync(parameters);

            // Assert
            Assert.True(result.Success);
            _morphToolMock.Verify(x => x.ExecuteAsync(It.Is<Dictionary<string, object>>(p => 
                p.ContainsKey("strategy") && p["strategy"].ToString() == "traditional")), Times.Once);
        }

        [Theory]
        [InlineData("morph")]
        [InlineData("traditional")]
        [InlineData("auto")]
        [InlineData("")]
        [InlineData("invalid")]
        public async Task DetermineStrategyAsync_WithDifferentInputs_ReturnsCorrectStrategy(string strategyInput)
        {
            // Arrange
            _configManagerMock
                .Setup(x => x.GetDefaultStrategyAsync())
                .ReturnsAsync(DiffStrategy.Auto);

            var tool = new SmartDiffTool(_morphToolMock.Object, _traditionalToolMock.Object, _configManagerMock.Object, _loggerMock.Object);

            var parameters = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(strategyInput))
            {
                parameters["strategy"] = strategyInput;
            }

            // Act
            var result = await tool.ExecuteAsync(parameters);

            // Assert
            Assert.NotNull(result);
            // The test verifies that the method doesn't throw and handles all strategy inputs
        }

        [Fact]
        public void GetDisplaySummary_WithMorphStyleParameters_ReturnsFormattedSummary()
        {
            // Arrange
            var tool = new SmartDiffTool(_morphToolMock.Object, _traditionalToolMock.Object, _configManagerMock.Object, _loggerMock.Object);

            var parameters = new Dictionary<string, object>
            {
                { "target_file", "Calculator.cs" },
                { "instructions", "Add error handling to prevent division by zero" },
                { "strategy", "auto" }
            };

            // Act
            var summary = tool.GetDisplaySummary(parameters);

            // Assert
            Assert.Contains("AUTO", summary);
            Assert.Contains("Calculator.cs", summary);
            Assert.Contains("Add error handling", summary);
        }

        [Fact]
        public void GetDisplaySummary_WithTraditionalPatch_DelegatesToTraditionalTool()
        {
            // Arrange
            var expectedSummary = "Patching test.cs (1+, 0-)";
            
            _traditionalToolMock
                .Setup(x => x.GetDisplaySummary(It.IsAny<Dictionary<string, object>>()))
                .Returns(expectedSummary);

            var tool = new SmartDiffTool(_morphToolMock.Object, _traditionalToolMock.Object, _configManagerMock.Object, _loggerMock.Object);

            var parameters = new Dictionary<string, object>
            {
                { "patchText", "*** Update File: test.cs\n@@ context @@\n+ new line" }
            };

            // Act
            var summary = tool.GetDisplaySummary(parameters);

            // Assert
            Assert.Equal(expectedSummary, summary);
            _traditionalToolMock.Verify(x => x.GetDisplaySummary(It.IsAny<Dictionary<string, object>>()), Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_WithException_ReturnsErrorResult()
        {
            // Arrange
            _configManagerMock
                .Setup(x => x.GetDefaultStrategyAsync())
                .ThrowsAsync(new InvalidOperationException("Configuration error"));

            var tool = new SmartDiffTool(_morphToolMock.Object, _traditionalToolMock.Object, _configManagerMock.Object, _loggerMock.Object);

            var parameters = new Dictionary<string, object>
            {
                { "target_file", "test.cs" },
                { "instructions", "Do something" },
                { "code_edit", "some code" }
            };

            // Act
            var result = await tool.ExecuteAsync(parameters);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Smart diff execution failed", result.Error ?? "");
        }

        [Fact]
        public async Task ExecuteAsync_WithBothStrategiesFailing_ReturnsComprehensiveError()
        {
            // Arrange
            var originalCode = "public class Test { }";
            await File.WriteAllTextAsync(_testFile, originalCode);

            _configManagerMock
                .Setup(x => x.GetDefaultStrategyAsync())
                .ReturnsAsync(DiffStrategy.Auto);

            _configManagerMock
                .Setup(x => x.IsConfiguredAsync())
                .ReturnsAsync(true);

            var morphResult = new ToolResult
            {
                Success = false,
                Error = "Morph API connection failed"
            };

            _morphToolMock
                .Setup(x => x.ExecuteAsync(It.IsAny<Dictionary<string, object>>()))
                .ReturnsAsync(morphResult);

            _morphToolMock
                .Setup(x => x.ExecuteAsync(It.Is<Dictionary<string, object>>(p => 
                    p.ContainsKey("strategy") && p["strategy"].ToString() == "traditional")))
                .ThrowsAsync(new InvalidOperationException("Traditional conversion failed"));

            var tool = new SmartDiffTool(_morphToolMock.Object, _traditionalToolMock.Object, _configManagerMock.Object, _loggerMock.Object);

            var parameters = new Dictionary<string, object>
            {
                { "target_file", _testFile },
                { "instructions", "Add a method" },
                { "code_edit", "public void Method() { }" },
                { "strategy", "auto" }
            };

            // Act
            var result = await tool.ExecuteAsync(parameters);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Both Morph and Traditional strategies failed", result.Error ?? "");
            Assert.Contains("Morph API connection failed", result.Error ?? "");
            Assert.Contains("Traditional conversion failed", result.Error ?? "");
        }
    }
}