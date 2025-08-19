using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Saturn.Tools.Core;

namespace Saturn.Tools
{
    public class ExecuteCommandTool : ToolBase, IDisposable
    {
        private const int DefaultTimeoutSeconds = 30;
        private const int MaxOutputLength = 1048576; // 1MB
        private readonly List<CommandHistory> _commandHistory = new();
        private readonly CommandExecutorConfig _config;
        private readonly ICommandApprovalService _approvalService;
        private readonly object _historyLock = new object();
        private bool _disposed = false;

        public ExecuteCommandTool() : this(new CommandExecutorConfig(), null!) { }

        public ExecuteCommandTool(CommandExecutorConfig config) : this(config, null!) { }

        public ExecuteCommandTool(CommandExecutorConfig config, ICommandApprovalService approvalService)
        {
            _config = config ?? new CommandExecutorConfig();
            _approvalService = approvalService ?? new CommandApprovalService(true);
        }

        public override string Name => "execute_command";

        public override string Description => "Executes system commands and returns their output";

        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                { "command", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "The system command to execute" }
                    }
                },
                { "workingDirectory", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "The working directory for command execution. Defaults to current directory" }
                    }
                },
                { "timeout", new Dictionary<string, object>
                    {
                        { "type", "integer" },
                        { "description", "Command timeout in seconds. Default is 30 seconds" }
                    }
                },
                { "captureOutput", new Dictionary<string, object>
                    {
                        { "type", "boolean" },
                        { "description", "Whether to capture command output. Default is true" }
                    }
                },
                { "runAsShell", new Dictionary<string, object>
                    {
                        { "type", "boolean" },
                        { "description", "Whether to run command through shell. Default is true" }
                    }
                }
            };
        }

        protected override string[] GetRequiredParameters()
        {
            return new[] { "command" };
        }
        
        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            var command = GetParameter<string>(parameters, "command", "");
            return $"$ {TruncateString(command, 50)}";
        }

        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> arguments)
        {
            if (_disposed)
            {
                return CreateErrorResult("ExecuteCommandTool has been disposed");
            }

            try
            {
                var command = GetParameter<string>(arguments, "command");
                if (string.IsNullOrWhiteSpace(command))
                {
                    return CreateErrorResult("Command parameter is required");
                }
                
                var workingDirectory = GetParameter<string>(arguments, "workingDirectory", Directory.GetCurrentDirectory());
                var timeoutSeconds = GetParameter<int>(arguments, "timeout", _config.DefaultTimeout);
                var captureOutput = GetParameter<bool>(arguments, "captureOutput", true);
                var runAsShell = GetParameter<bool>(arguments, "runAsShell", true);

                // STABILITY FIX: Add validation for timeout and working directory
                if (timeoutSeconds <= 0 || timeoutSeconds > 3600) // Max 1 hour
                {
                    return CreateErrorResult("Timeout must be between 1 and 3600 seconds");
                }

                if (AgentContext.RequireCommandApproval)
                {
                    var approved = await _approvalService.RequestApprovalAsync(command, workingDirectory).ConfigureAwait(false);
                    if (!approved)
                    {
                        return CreateErrorResult("Command execution denied by user");
                    }
                }

                if (_config.SecurityMode != SecurityMode.Unrestricted)
                {
                    var validationResult = ValidateCommand(command);
                    if (!validationResult.IsValid)
                    {
                        return CreateErrorResult($"Command blocked: {validationResult.Reason}");
                    }
                }

                if (!Directory.Exists(workingDirectory))
                {
                    return CreateErrorResult($"Working directory does not exist: {workingDirectory}");
                }

                var historyEntry = new CommandHistory
                {
                    Command = command,
                    WorkingDirectory = workingDirectory,
                    ExecutedAt = DateTime.UtcNow
                };

                // STABILITY FIX: Use proper cancellation token support
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds + 5)); // Add buffer
                
                try
                {
                    var result = await ExecuteCommandAsync(
                        command,
                        workingDirectory,
                        TimeSpan.FromSeconds(timeoutSeconds),
                        captureOutput,
                        runAsShell,
                        cts.Token).ConfigureAwait(false);

                    historyEntry.ExitCode = result.ExitCode;
                    historyEntry.Duration = result.Duration;
                    historyEntry.Success = result.ExitCode == 0;

                    // STABILITY FIX: Thread-safe history management
                    if (_config.EnableHistory)
                    {
                        lock (_historyLock)
                        {
                            _commandHistory.Add(historyEntry);
                            if (_commandHistory.Count > _config.MaxHistorySize)
                            {
                                _commandHistory.RemoveAt(0);
                            }
                        }
                    }

                    return FormatResult(result);
                }
                catch (OperationCanceledException)
                {
                    historyEntry.Success = false;
                    historyEntry.Error = "Command execution was cancelled or timed out";
                    
                    if (_config.EnableHistory)
                    {
                        lock (_historyLock)
                        {
                            _commandHistory.Add(historyEntry);
                        }
                    }

                    return CreateErrorResult("Command execution was cancelled or timed out");
                }
                catch (Exception ex)
                {
                    historyEntry.Success = false;
                    historyEntry.Error = ex.Message;
                    
                    if (_config.EnableHistory)
                    {
                        lock (_historyLock)
                        {
                            _commandHistory.Add(historyEntry);
                        }
                    }

                    throw;
                }
            }
            catch (Exception ex)
            {
                return CreateErrorResult($"Command execution failed: {ex.Message}");
            }
        }

        private async Task<CommandResult> ExecuteCommandAsync(
            string command,
            string workingDirectory,
            TimeSpan timeout,
            bool captureOutput,
            bool runAsShell,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            var processInfo = CreateProcessStartInfo(command, workingDirectory, captureOutput, runAsShell);

            using var process = new Process { StartInfo = processInfo };
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            if (captureOutput)
            {
                process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data != null && outputBuilder.Length < MaxOutputLength)
                    {
                        outputBuilder.AppendLine(e.Data);
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data != null && errorBuilder.Length < MaxOutputLength)
                    {
                        errorBuilder.AppendLine(e.Data);
                    }
                };
            }

            process.Start();

            if (captureOutput)
            {
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch { }

                throw new TimeoutException($"Command execution timed out after {timeout.TotalSeconds} seconds");
            }

            stopwatch.Stop();

            return new CommandResult
            {
                ExitCode = process.ExitCode,
                StandardOutput = outputBuilder.ToString(),
                StandardError = errorBuilder.ToString(),
                Duration = stopwatch.Elapsed,
                Command = command,
                WorkingDirectory = workingDirectory
            };
        }

        private ProcessStartInfo CreateProcessStartInfo(string command, string workingDirectory, bool captureOutput, bool runAsShell)
        {
            var startInfo = new ProcessStartInfo
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = captureOutput,
                RedirectStandardError = captureOutput,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (runAsShell)
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    startInfo.FileName = "cmd.exe";
                    startInfo.Arguments = $"/c {command}";
                }
                else
                {
                    startInfo.FileName = "/bin/sh";
                    startInfo.Arguments = $"-c \"{command.Replace("\"", "\\\"")}\"";
                }
            }
            else
            {
                var parts = ParseCommand(command);
                startInfo.FileName = parts.FileName;
                startInfo.Arguments = parts.Arguments;
            }

            return startInfo;
        }

        private (string FileName, string Arguments) ParseCommand(string command)
        {
            command = command.Trim();
            
            if (command.StartsWith("\""))
            {
                var endQuote = command.IndexOf("\"", 1);
                if (endQuote > 0)
                {
                    var fileName = command.Substring(1, endQuote - 1);
                    var arguments = command.Substring(endQuote + 1).Trim();
                    return (fileName, arguments);
                }
            }

            var firstSpace = command.IndexOf(' ');
            if (firstSpace > 0)
            {
                return (command.Substring(0, firstSpace), command.Substring(firstSpace + 1));
            }

            return (command, string.Empty);
        }

        private CommandValidationResult ValidateCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return new CommandValidationResult { IsValid = false, Reason = "Command cannot be empty" };
            }

            var validator = new CommandValidator(_config.SecurityMode);
            var validationResult = validator.Validate(command);

            if (!validationResult.IsValid)
            {
                return validationResult;
            }

            if (_config.CustomValidator != null)
            {
                return _config.CustomValidator(command);
            }

            return new CommandValidationResult { IsValid = true };
        }

        private ToolResult FormatResult(CommandResult result)
        {
            var output = new StringBuilder();
            
            output.AppendLine($"Command: {result.Command}");
            output.AppendLine($"Working Directory: {result.WorkingDirectory}");
            output.AppendLine($"Exit Code: {result.ExitCode}");
            output.AppendLine($"Duration: {result.Duration.TotalMilliseconds:F2}ms");
            output.AppendLine();

            if (!string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                output.AppendLine("=== Standard Output ===");
                output.AppendLine(TruncateOutput(result.StandardOutput));
            }

            if (!string.IsNullOrWhiteSpace(result.StandardError))
            {
                output.AppendLine("=== Standard Error ===");
                output.AppendLine(TruncateOutput(result.StandardError));
            }

            return result.ExitCode == 0 
                ? CreateSuccessResult(result, output.ToString()) 
                : CreateErrorResult($"Command exited with code {result.ExitCode}\n\n{output}");
        }

        private string TruncateOutput(string output)
        {
            if (output.Length <= MaxOutputLength)
                return output;

            return output.Substring(0, MaxOutputLength) + "\n\n... (output truncated)";
        }

        public IReadOnlyList<CommandHistory> GetCommandHistory()
        {
            lock (_historyLock)
            {
                return _commandHistory.AsReadOnly();
            }
        }

        public void ClearHistory()
        {
            lock (_historyLock)
            {
                _commandHistory.Clear();
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed resources
                    lock (_historyLock)
                    {
                        _commandHistory.Clear();
                    }
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    public class CommandExecutorConfig
    {
        public SecurityMode SecurityMode { get; set; } = SecurityMode.Restricted;
        public int DefaultTimeout { get; set; } = 30;
        public bool EnableHistory { get; set; } = true;
        public int MaxHistorySize { get; set; } = 100;
        public Func<string, CommandValidationResult>? CustomValidator { get; set; }
    }

    public enum SecurityMode
    {
        Unrestricted,
        Restricted,
        Strict
    }

    public class CommandValidationResult
    {
        public bool IsValid { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public class CommandResult
    {
        public int ExitCode { get; set; }
        public string StandardOutput { get; set; } = string.Empty;
        public string StandardError { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public string Command { get; set; } = string.Empty;
        public string WorkingDirectory { get; set; } = string.Empty;
    }

    public class CommandHistory
    {
        public string Command { get; set; } = string.Empty;
        public string WorkingDirectory { get; set; } = string.Empty;
        public DateTime ExecutedAt { get; set; }
        public int? ExitCode { get; set; }
        public TimeSpan? Duration { get; set; }
        public bool Success { get; set; }
        public string? Error { get; set; }
    }

    public class CommandValidator
    {
        private readonly SecurityMode _securityMode;
        
        // SECURITY FIX: Use allowlist instead of blacklist for commands
        private static readonly HashSet<string> AllowedCommands = new(StringComparer.OrdinalIgnoreCase)
        {
            // Version control
            "git", "svn", "hg",
            
            // File operations (safe subset)
            "ls", "dir", "cat", "type", "head", "tail", "find", "grep", "sort", "wc",
            
            // Development tools
            "dotnet", "npm", "node", "python", "pip", "mvn", "gradle", "cargo", "rustc",
            "gcc", "clang", "make", "cmake", "msbuild",
            
            // Text processing
            "echo", "printf", "sed", "awk", "cut", "tr", "uniq",
            
            // System info (safe)
            "whoami", "pwd", "date", "uptime", "uname", "hostname",
            
            // Package managers (safe operations)
            "apt", "yum", "brew", "choco", "winget",
            
            // Docker (if needed)
            "docker", "docker-compose",
            
            // Basic utilities
            "curl", "wget", "ping", "traceroute", "nslookup", "dig"
        };

        // Completely blocked commands - always dangerous
        private static readonly HashSet<string> AlwaysBlockedCommands = new(StringComparer.OrdinalIgnoreCase)
        {
            // File system destruction
            "rm", "rmdir", "del", "rd", "format", "fdisk", "mkfs", "dd",
            
            // System control
            "sudo", "su", "runas", "shutdown", "reboot", "halt", "poweroff",
            "systemctl", "service", "sc", "net",
            
            // Process control
            "kill", "killall", "pkill", "taskkill", "pskill",
            
            // Permission changes
            "chmod", "chown", "chgrp", "icacls", "attrib",
            
            // Network/security
            "iptables", "netsh", "route", "ifconfig", "ip",
            
            // Package installation (can be dangerous)
            "rpm", "dpkg", "pacman",
            
            // Disk operations
            "mount", "umount", "fsck", "partprobe"
        };

        // Dangerous argument patterns
        private static readonly string[] DangerousPatterns =
        {
            "--force", "-f", "--recursive", "-r", "--all", "-a",
            "/f", "/s", "/q", "/y",  // Windows force flags
            "$(", "`", "&&", "||", ";", "|",  // Command injection patterns
            "..", "~", "$HOME", "%USERPROFILE%"  // Path traversal
        };

        private static readonly char[] CommandSeparators = { ';', '&', '|', '\n', '\r' };

        public CommandValidator(SecurityMode securityMode)
        {
            _securityMode = securityMode;
        }

        public CommandValidationResult Validate(string command)
        {
            if (_securityMode == SecurityMode.Unrestricted)
            {
                return new CommandValidationResult { IsValid = true };
            }

            // Check for always blocked commands first
            var result = ValidateNotAlwaysBlocked(command);
            if (!result.IsValid)
            {
                return result;
            }

            // Split and validate each command in chain
            var commands = command.Split(CommandSeparators, StringSplitOptions.RemoveEmptyEntries);
            foreach (var cmd in commands)
            {
                var validationResult = ValidateSingleCommand(cmd.Trim());
                if (!validationResult.IsValid)
                {
                    return validationResult;
                }
            }

            return new CommandValidationResult { IsValid = true };
        }

        private CommandValidationResult ValidateNotAlwaysBlocked(string command)
        {
            var lowerCommand = command.ToLowerInvariant();
            foreach (var blocked in AlwaysBlockedCommands)
            {
                if (lowerCommand.Contains(blocked.ToLowerInvariant()))
                {
                    return new CommandValidationResult
                    {
                        IsValid = false,
                        Reason = $"Command contains always-blocked command '{blocked}'"
                    };
                }
            }
            return new CommandValidationResult { IsValid = true };
        }

        private CommandValidationResult ValidateSingleCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return new CommandValidationResult { IsValid = false, Reason = "Empty command" };
            }

            var commandParts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (commandParts.Length == 0)
            {
                return new CommandValidationResult { IsValid = false, Reason = "No command specified" };
            }

            var commandName = commandParts[0].ToLowerInvariant();
            
            // Remove path separators from command name for validation
            var baseCommand = Path.GetFileName(commandName);

            // SECURITY: Use allowlist - only allow explicitly permitted commands
            if (!AllowedCommands.Contains(baseCommand))
            {
                return new CommandValidationResult
                {
                    IsValid = false,
                    Reason = $"Command '{baseCommand}' is not in the allowed commands list"
                };
            }

            // Check for dangerous argument patterns
            if (_securityMode == SecurityMode.Strict)
            {
                foreach (var pattern in DangerousPatterns)
                {
                    if (command.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        return new CommandValidationResult
                        {
                            IsValid = false,
                            Reason = $"Command contains dangerous pattern '{pattern}'"
                        };
                    }
                }
            }

            return new CommandValidationResult { IsValid = true };
        }
    }
}