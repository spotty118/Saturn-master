using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Terminal.Gui;
using Saturn.Agents;
using Saturn.Agents.Core;
using Saturn.Agents.MultiAgent;
using Saturn.Configuration;
using Saturn.OpenRouter;
using Saturn.Tools.Core;

namespace Saturn.UI
{
    public class ChatInterface
    {
        // Field declarations
        private readonly IChatRenderer chatRenderer;
        private readonly IChatSessionManager chatSessionManager;
        private readonly IDialogManager dialogManager;
        private readonly Agent agent;
        private readonly AgentManager agentManager;
        private readonly IAgentConfigurationManager agentConfigurationManager;
        private readonly ToolRegistry toolRegistry;
        private readonly MarkdownRenderer markdownRenderer;

        // UI Components
        private Toplevel app;
        private TextView chatView;
        private TextView inputField;
        private Button sendButton;
        private TextView toolCallsView;
        private TextView agentStatusView;
        private TabView statusTabView;
        private FrameView toolCallsPanel;
        private FrameView agentStatusPanel;

        // State management
        private bool isProcessing = false;
        private CancellationTokenSource cancellationTokenSource;

        public ChatInterface(
            IChatRenderer chatRenderer,
            IChatSessionManager chatSessionManager,
            IDialogManager dialogManager,
            Agent agent,
            AgentManager agentManager,
            IAgentConfigurationManager agentConfigurationManager,
            ToolRegistry toolRegistry,
            MarkdownRenderer markdownRenderer)
        {
            this.chatRenderer = chatRenderer ?? throw new ArgumentNullException(nameof(chatRenderer));
            this.chatSessionManager = chatSessionManager ?? throw new ArgumentNullException(nameof(chatSessionManager));
            this.dialogManager = dialogManager ?? throw new ArgumentNullException(nameof(dialogManager));
            this.agent = agent ?? throw new ArgumentNullException(nameof(agent));
            this.agentManager = agentManager ?? throw new ArgumentNullException(nameof(agentManager));
            this.agentConfigurationManager = agentConfigurationManager ?? throw new ArgumentNullException(nameof(agentConfigurationManager));
            this.toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
            this.markdownRenderer = markdownRenderer ?? throw new ArgumentNullException(nameof(markdownRenderer));

            SetupAgentManagerEvents();
        }

        private void SetupAgentManagerEvents()
        {
            agentManager.OnAgentStatusChanged += (agentId, name, status) =>
            {
                var agents = agentManager.GetAllAgentStatuses();
                var agentList = agents.Select(a => $"{a.Name}: {a.Status}").ToList();
                UpdateAgentStatus("Active", agents.Count(a => !a.IsIdle), agentList);
            };
            
            agentManager.OnTaskCompleted += (taskId, result) =>
            {
                Application.MainLoop.Invoke(() =>
                {
                    var timestamp = DateTime.Now.ToString("HH:mm:ss");
                    var currentText = toolCallsView.Text.ToString();
                    
                    var newEntry = $"[{timestamp}] Task Completed: {taskId}\n";
                    newEntry += $"  Agent: {result.AgentName}\n";
                    newEntry += $"  Status: {(result.Success ? "Success" : "Failed")}\n";
                    newEntry += $"  Duration: {result.Duration.TotalSeconds:F1}s\n";
                    newEntry += "───────────────\n";
                    
                    toolCallsView.Text = newEntry + currentText;
                });
            };
        }

        public void Initialize()
        {
            Application.Init();
            SetupTheme();

            var menu = CreateMenu();
            app = CreateMainWindow();

            // Root container: horizontal split (left: chat+input, right: status tabs)
            var rootContainer = new FrameView()
            {
                X = 0,
                Y = 1, // below menu
                Width = Dim.Fill(),
                Height = Dim.Fill(1), // leave space for status bar
                ColorScheme = Colors.Base
            };

            // Left: vertical stack (chat, input)
            var leftPanel = new FrameView()
            {
                X = 0,
                Y = 0,
                Width = Dim.Percent(70),
                Height = Dim.Fill(),
                ColorScheme = Colors.Base,
                Border = new Border() { BorderStyle = BorderStyle.None }
            };

            // Main chat container
            var mainContainer = new FrameView("Chat")
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(3),
                ColorScheme = Colors.Base
            };
            var chatViewLocal = new TextView()
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(1),
                Height = Dim.Fill(),
                ReadOnly = true,
                WordWrap = true,
                Text = GetWelcomeMessage(),
                ColorScheme = Colors.Base
            };
            mainContainer.Add(chatViewLocal);
            chatView = chatViewLocal;

            // Input container
            var inputContainer = new FrameView("Input (Enter: Send, Ctrl+Enter: New Line)")
            {
                X = 0,
                Y = Pos.AnchorEnd(3),
                Width = Dim.Fill(),
                Height = 3,
                ColorScheme = Colors.Base
            };
            var inputFieldLocal = new TextView()
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(12),
                Height = Dim.Fill(),
                CanFocus = true,
                ColorScheme = Colors.Base,
                WordWrap = true,
                Text = ""
            };
            var sendButtonLocal = new Button("Send")
            {
                X = Pos.Right(inputFieldLocal) + 1,
                Y = Pos.Center(),
                ColorScheme = Colors.Base,
                Text = "Send"
            };
            var charCounter = new Label("0 chars")
            {
                X = Pos.Right(sendButtonLocal) + 1,
                Y = Pos.Center(),
                ColorScheme = Colors.Base,
                Text = "0 chars"
            };
            inputContainer.Add(inputFieldLocal, sendButtonLocal, charCounter);
            inputField = inputFieldLocal;
            sendButton = sendButtonLocal;

            leftPanel.Add(mainContainer);
            leftPanel.Add(inputContainer);

            // Right: status tabs
            statusTabView = new TabView
            {
                X = Pos.Right(leftPanel),
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                ColorScheme = Colors.Base
            };
            toolCallsPanel = CreateToolCallsPanel();
            agentStatusPanel = CreateAgentStatusPanel();
            var helpPanel = CreateHelpPanel();
            statusTabView.AddTab(new TabView.Tab("Tools", toolCallsPanel), false);
            statusTabView.AddTab(new TabView.Tab("Agents", agentStatusPanel), false);
            statusTabView.AddTab(new TabView.Tab("Help", helpPanel), false);

            // Add both panels to root
            rootContainer.Add(leftPanel, statusTabView);

            // Now that UI components are created, initialize services that need them
            chatRenderer.Initialize(chatView, markdownRenderer);
            chatSessionManager.Initialize(agent, chatView, toolCallsView, markdownRenderer);
            dialogManager.SetCurrentConfiguration(agentConfigurationManager.CurrentConfig);
            SetupScrollBar(mainContainer);
            SetupInputHandlers();

            // Add status bar
            var statusBar = CreateStatusBar();
            app.Add(menu, rootContainer, statusBar);
            SetInitialFocus();
        }

        private void SetupTheme()
        {
            // Clean, professional theme with high contrast
            Colors.Base.Normal = Application.Driver.MakeAttribute(Color.White, Color.Black);
            Colors.Base.Focus = Application.Driver.MakeAttribute(Color.Black, Color.Gray);
            Colors.Base.HotNormal = Application.Driver.MakeAttribute(Color.White, Color.Black);
            Colors.Base.HotFocus = Application.Driver.MakeAttribute(Color.Black, Color.White);
            
            Colors.Menu.Normal = Application.Driver.MakeAttribute(Color.White, Color.Blue);
            Colors.Menu.Focus = Application.Driver.MakeAttribute(Color.White, Color.DarkGray);
            Colors.Menu.HotNormal = Application.Driver.MakeAttribute(Color.White, Color.Blue);
            Colors.Menu.HotFocus = Application.Driver.MakeAttribute(Color.White, Color.DarkGray);
            Colors.Menu.Disabled = Application.Driver.MakeAttribute(Color.Gray, Color.Blue);
            
            Colors.Dialog.Normal = Application.Driver.MakeAttribute(Color.Black, Color.Gray);
            Colors.Dialog.Focus = Application.Driver.MakeAttribute(Color.White, Color.DarkGray);
            Colors.Dialog.HotNormal = Application.Driver.MakeAttribute(Color.White, Color.Gray);
            Colors.Dialog.HotFocus = Application.Driver.MakeAttribute(Color.White, Color.DarkGray);
            
            Colors.Error.Normal = Application.Driver.MakeAttribute(Color.Red, Color.Black);
            Colors.Error.Focus = Application.Driver.MakeAttribute(Color.White, Color.Red);
            Colors.Error.HotNormal = Application.Driver.MakeAttribute(Color.Red, Color.Black);
            Colors.Error.HotFocus = Application.Driver.MakeAttribute(Color.White, Color.Red);
            
            Colors.TopLevel.Normal = Application.Driver.MakeAttribute(Color.White, Color.Black);
            Colors.TopLevel.Focus = Application.Driver.MakeAttribute(Color.Black, Color.White);
        }

        private MenuBar CreateMenu()
        {
            return new MenuBar(new MenuBarItem[]
            {
                new MenuBarItem("modes", new MenuItem[]
                {
                    new MenuItem("code", "Switch to code mode", () => ChangeMode("code"), shortcut: Key.F2),
                    new MenuItem("research", "Switch to research mode", () => ChangeMode("research"), shortcut: Key.F3),
                    new MenuItem("debug", "Switch to debug mode", () => ChangeMode("debug"), shortcut: Key.F4)
                }),
                new MenuBarItem("session", new MenuItem[]
                {
                    new MenuItem("save", "Save current chat session", () => SaveSession(), shortcut: Key.CtrlMask | Key.S),
                    new MenuItem("load", "Load a saved session", () => LoadSession(), shortcut: Key.CtrlMask | Key.L),
                    new MenuItem("clear", "Clear current chat", () => ClearChat(), shortcut: Key.CtrlMask | Key.K)
                }),
                new MenuBarItem("theme", new MenuItem[]
                {
                    new MenuItem("default", "Default theme", () => SetTheme("Default")),
                    new MenuItem("dark", "Dark theme", () => SetTheme("Dark")),
                    new MenuItem("light", "Light theme", () => SetTheme("Light"))
                }),
                new MenuBarItem("help", new MenuItem[]
                {
                    new MenuItem("help", "Show help and shortcuts", () => ShowQuickHelp(), shortcut: Key.F1)
                })
            });
        }

        private MenuItem[] GetToolMenuItems()
        {
            var menuItems = new List<MenuItem>();
            var tools = toolRegistry.GetAll();

            // Group tools by category
            var fileTools = tools.Where(t => t.Name.Contains("file") || t.Name.Contains("grep") || t.Name.Contains("glob")).ToList();
            var agentTools = tools.Where(t => t.Name.Contains("agent")).ToList();
            var otherTools = tools.Where(t => !t.Name.Contains("file") && !t.Name.Contains("grep") && !t.Name.Contains("glob") && !t.Name.Contains("agent")).ToList();

            if (fileTools.Any())
            {
                menuItems.Add(null!); // Separator
                foreach (var tool in fileTools)
                {
                    menuItems.Add(new MenuItem($"{tool.Name}", tool.Description, () => OnToolClicked(tool)));
                }
            }

            if (agentTools.Any())
            {
                menuItems.Add(null!); // Separator
                foreach (var tool in agentTools)
                {
                    menuItems.Add(new MenuItem($"{tool.Name}", tool.Description, () => OnToolClicked(tool)));
                }
            }

            if (otherTools.Any())
            {
                menuItems.Add(null!); // Separator
                foreach (var tool in otherTools)
                {
                    menuItems.Add(new MenuItem($"{tool.Name}", tool.Description, () => OnToolClicked(tool)));
                }
            }

            return menuItems.ToArray();
        }

        private void ChangeMode(string mode)
        {
            // Implementation for changing modes
            MessageBox.Query("Mode Change", $"Switching to {mode} mode...", "OK");
        }

        private void SaveSession()
        {
            // Implementation for saving session
            MessageBox.Query("Save Session", "Session saved successfully!", "OK");
        }

        private void LoadSession()
        {
            // Implementation for loading session
            MessageBox.Query("Load Session", "Select a session to load...", "OK");
        }

        private void ClearChat()
        {
            chatView.Text = GetWelcomeMessage();
            Application.Refresh();
        }

        private void ShowQuickHelp()
        {
            var helpText = GetHelpText();
            MessageBox.Query("❓ Saturn Help", helpText, "OK");
        }

        private void OnToolClicked(ITool tool)
        {
            var toolInfo = $"{tool.Name}\n\n";
            toolInfo += $"Description:\n{tool.Description}\n\n";
            
            var parameters = tool.GetParameters();
            if (parameters.Any())
            {
                toolInfo += "Parameters:\n";
                foreach (var param in parameters)
                {
                    toolInfo += $"• {param.Key}: {param.Value}\n";
                }
            }
            else
            {
                toolInfo += "Parameters: None required\n";
            }
            
            toolInfo += "\nTip: You can also use this tool directly in chat by describing what you want to do!";
            
            MessageBox.Query($"{tool.Name}", toolInfo, "OK");
        }

        private void SetTheme(string themeName)
        {
            switch (themeName)
            {
                case "Dark":
                    // Modern dark theme with better contrast
                    Colors.Base.Normal = Application.Driver.MakeAttribute(Color.White, Color.Black);
                    Colors.Base.Focus = Application.Driver.MakeAttribute(Color.Black, Color.White);
                    Colors.Base.HotNormal = Application.Driver.MakeAttribute(Color.BrightCyan, Color.Black);
                    Colors.Base.HotFocus = Application.Driver.MakeAttribute(Color.BrightCyan, Color.DarkGray);
                    
                    Colors.Menu.Normal = Application.Driver.MakeAttribute(Color.White, Color.DarkGray);
                    Colors.Menu.Focus = Application.Driver.MakeAttribute(Color.Black, Color.White);
                    Colors.Menu.HotNormal = Application.Driver.MakeAttribute(Color.BrightCyan, Color.DarkGray);
                    Colors.Menu.HotFocus = Application.Driver.MakeAttribute(Color.BrightCyan, Color.White);
                    Colors.Menu.Disabled = Application.Driver.MakeAttribute(Color.Gray, Color.DarkGray);
                    
                    Colors.Dialog.Normal = Application.Driver.MakeAttribute(Color.White, Color.DarkGray);
                    Colors.Dialog.Focus = Application.Driver.MakeAttribute(Color.Black, Color.White);
                    Colors.Dialog.HotNormal = Application.Driver.MakeAttribute(Color.BrightCyan, Color.DarkGray);
                    Colors.Dialog.HotFocus = Application.Driver.MakeAttribute(Color.BrightCyan, Color.White);
                    
                    Colors.Error.Normal = Application.Driver.MakeAttribute(Color.BrightRed, Color.Black);
                    Colors.Error.Focus = Application.Driver.MakeAttribute(Color.White, Color.Red);
                    Colors.Error.HotNormal = Application.Driver.MakeAttribute(Color.BrightRed, Color.Black);
                    Colors.Error.HotFocus = Application.Driver.MakeAttribute(Color.White, Color.Red);
                    
                    Colors.TopLevel.Normal = Application.Driver.MakeAttribute(Color.White, Color.Black);
                    Colors.TopLevel.Focus = Application.Driver.MakeAttribute(Color.Black, Color.White);
                    
                    break;
                    
                case "Light":
                    // Clean light theme with better readability
                    Colors.Base.Normal = Application.Driver.MakeAttribute(Color.Black, Color.White);
                    Colors.Base.Focus = Application.Driver.MakeAttribute(Color.Black, Color.Gray);
                    Colors.Base.HotNormal = Application.Driver.MakeAttribute(Color.Blue, Color.White);
                    Colors.Base.HotFocus = Application.Driver.MakeAttribute(Color.Blue, Color.Gray);
                    
                    Colors.Menu.Normal = Application.Driver.MakeAttribute(Color.Black, Color.White);
                    Colors.Menu.Focus = Application.Driver.MakeAttribute(Color.Black, Color.Gray);
                    Colors.Menu.HotNormal = Application.Driver.MakeAttribute(Color.Blue, Color.White);
                    Colors.Menu.HotFocus = Application.Driver.MakeAttribute(Color.Blue, Color.Gray);
                    Colors.Menu.Disabled = Application.Driver.MakeAttribute(Color.DarkGray, Color.White);
                    
                    Colors.Dialog.Normal = Application.Driver.MakeAttribute(Color.Black, Color.White);
                    Colors.Dialog.Focus = Application.Driver.MakeAttribute(Color.Black, Color.Gray);
                    Colors.Dialog.HotNormal = Application.Driver.MakeAttribute(Color.Blue, Color.White);
                    Colors.Dialog.HotFocus = Application.Driver.MakeAttribute(Color.Blue, Color.Gray);
                    
                    Colors.Error.Normal = Application.Driver.MakeAttribute(Color.Red, Color.White);
                    Colors.Error.Focus = Application.Driver.MakeAttribute(Color.Black, Color.Red);
                    Colors.Error.HotNormal = Application.Driver.MakeAttribute(Color.Red, Color.White);
                    Colors.Error.HotFocus = Application.Driver.MakeAttribute(Color.White, Color.Red);
                    
                    Colors.TopLevel.Normal = Application.Driver.MakeAttribute(Color.Black, Color.White);
                    Colors.TopLevel.Focus = Application.Driver.MakeAttribute(Color.Black, Color.Gray);
                    
                    break;
                    
                default:
                    SetupTheme();
                    break;
            }
            Application.Refresh();
        }

        private Toplevel CreateMainWindow()
        {
            return new Toplevel()
            {
                ColorScheme = Colors.TopLevel
            };
        }

        private void SetupScrollBar(FrameView mainContainer)
        {
            var chatScrollBar = new ScrollBarView(chatView, true, false)
            {
                X = Pos.Right(chatView),
                Y = 0,
                Height = Dim.Fill(),
                Width = 1,
                ColorScheme = Colors.Base
            };
            mainContainer.Add(chatScrollBar);
        }

        private FrameView CreateToolCallsPanel()
        {
            var panel = new FrameView("Tool Activity")
            {
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                ColorScheme = Colors.Base
            };

            toolCallsView = new TextView()
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                ReadOnly = true,
                WordWrap = true,
                Text = "No tools used yet.\n\nCommon: read_file, write_file\ngrep, web_fetch, create_agent",
                ColorScheme = Colors.Base
            };

            panel.Add(toolCallsView);
            return panel;
        }

        private FrameView CreateAgentStatusPanel()
        {
            var panel = new FrameView("Agent Status")
            {
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                ColorScheme = Colors.Base
            };

            agentStatusView = new TextView()
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                ReadOnly = true,
                WordWrap = true,
                Text = GetInitialAgentStatus(),
                ColorScheme = Colors.Base
            };

            panel.Add(agentStatusView);
            return panel;
        }

        private string GetInitialAgentStatus()
        {
            var status = "Ready | Idle | 0 Tasks | 0/10 Agents\n";
            status += "=============================\n";
            status += "Sub-agents: None\n";
            status += "F2-F4:Modes Ctrl+S:Save";
            return status;
        }

        public void UpdateToolCall(string toolName, string arguments)
        {
            Application.MainLoop.Invoke(() =>
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                var currentText = toolCallsView.Text.ToString();
                
                if (currentText.Contains("No tool calls yet."))
                {
                    currentText = "";
                }
                
                var summary = GetToolSummary(toolName, arguments);
                var newEntry = $"[{timestamp}] {toolName}: {summary}\n";
                newEntry += "─────────────────────────────────\n";
                
                toolCallsView.Text = newEntry + currentText;
                
                if (toolCallsView.Text.Length > 5000)
                {
                    toolCallsView.Text = toolCallsView.Text.Substring(0, 4000);
                }
            });
        }
        
        private string GetToolSummary(string toolName, string arguments)
        {
            try
            {
                var tool = toolRegistry.GetTool(toolName);
                if (tool != null)
                {
                    var jsonDoc = JsonDocument.Parse(arguments);
                    var parameters = new Dictionary<string, object>();
                    
                    foreach (var property in jsonDoc.RootElement.EnumerateObject())
                    {
                        parameters[property.Name] = GetJsonValue(property.Value);
                    }
                    
                    return tool.GetDisplaySummary(parameters);
                }
            }
            catch (Exception ex)
            {
                return $"Error parsing arguments: {ex.Message}";
            }
            
            return $"Called with: {arguments.Substring(0, Math.Min(50, arguments.Length))}...";
        }

        private object GetJsonValue(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    return element.GetString();
                case JsonValueKind.Number:
                    if (element.TryGetInt64(out var longValue))
                        return longValue;
                    return element.GetDouble();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.Null:
                    return null;
                case JsonValueKind.Array:
                    var list = new List<object>();
                    foreach (var item in element.EnumerateArray())
                    {
                        list.Add(GetJsonValue(item));
                    }
                    return list;
                case JsonValueKind.Object:
                    var dict = new Dictionary<string, object>();
                    foreach (var property in element.EnumerateObject())
                    {
                        dict[property.Name] = GetJsonValue(property.Value);
                    }
                    return dict;
                default:
                    return element.ToString();
            }
        }

        private string GetToolIcon(string toolName)
        {
            return toolName switch
            {
                "read_file" => "R",
                "write_file" => "W",
                "delete_file" => "D",
                "grep" => "S",
                "glob" => "G",
                "apply_diff" => "M",
                "search_and_replace" => "R",
                "web_fetch" => "W",
                "execute_command" => "X",
                "create_agent" => "A",
                "hand_off_to_agent" => "H",
                "get_agent_status" => "S",
                "wait_for_agent" => "W",
                "get_task_result" => "T",
                "terminate_agent" => "K",
                _ => "T"
            };
        }

        public void UpdateAgentStatus(string status, int activeTasks = 0, List<string>? subAgents = null)
        {
            Application.MainLoop.Invoke(() =>
            {
                var agents = agentManager.GetAllAgentStatuses();
                var currentCount = agentManager.GetCurrentAgentCount();
                var maxCount = agentManager.GetMaxConcurrentAgents();
                
                var statusText = $"{status} | {activeTasks} Tasks | {currentCount}/{maxCount} Agents\n";
                statusText += "=============================\n";
                
                if (agents.Any())
                {
                    var agent = agents.First();
                    statusText += $"Sub: {agent.Name} ({agent.Status})\n";
                    if (agents.Count() > 1)
                    {
                        statusText += $"+{agents.Count() - 1} more\n";
                    }
                }
                else
                {
                    statusText += "Sub-agents: None\n";
                }
                
                statusText += "F2-F4:Modes Ctrl+S:Save";
                
                agentStatusView.Text = statusText;
                
                // Update status bar
                var additionalInfo = $"Agents: {currentCount}/{maxCount} | Tasks: {activeTasks}";
                UpdateStatusBar(status, additionalInfo);
            });
        }

        private string GetStatusIcon(string status)
        {
            return status.ToLower() switch
            {
                "ready" => "[Ready]",
                "idle" => "[Idle]",
                "processing" => "[Processing]",
                "thinking" => "[Thinking]",
                "waiting" => "[Waiting]",
                "error" => "[Error]",
                "busy" => "[Busy]",
                _ => "[Status]"
            };
        }

        private string GetAgentStatusIcon(string status)
        {
            return status.ToLower() switch
            {
                "idle" => "[Idle]",
                "working" => "[Working]",
                "completed" => "[Done]",
                "failed" => "[Failed]",
                "waiting" => "[Waiting]",
                "terminated" => "[Stopped]",
                _ => "[Status]"
            };
        }

        private string GetUptime()
        {
            // This would need to be tracked from startup
            return "Just started";
        }

        public void UpdateStatusBar(string status, string? additionalInfo = null)
        {
            Application.MainLoop.Invoke(() =>
            {
                var statusText = $"{status}";
                if (!string.IsNullOrEmpty(additionalInfo))
                {
                    statusText += $" | {additionalInfo}";
                }
                statusText += " | F1: Help | F2-F4: Modes | Ctrl+S: Save | Ctrl+L: Load | Ctrl+K: Clear";
                
                // Find and update the status label
                if (app != null)
                {
                    foreach (var view in app.Subviews)
                    {
                        if (view is FrameView frameView && frameView.Title == "Status")
                        {
                            foreach (var subView in frameView.Subviews)
                            {
                                if (subView is Label label)
                                {
                                    label.Text = statusText;
                                    break;
                                }
                            }
                            break;
                        }
                    }
                }
                Application.Refresh();
            });
        }

        private void SetupInputHandlers()
        {
            inputField.KeyDown += (e) =>
            {
                if (e.KeyEvent.Key == Key.Enter && !e.KeyEvent.IsCtrl)
                {
                    // Regular Enter sends the message
                    sendButton.OnClicked();
                    e.Handled = true;
                }
                else if (e.KeyEvent.Key == Key.Enter && e.KeyEvent.IsCtrl)
                {
                    // Ctrl+Enter adds a new line
                    inputField.Text += "\n";
                    e.Handled = true;
                }
                else if (e.KeyEvent.Key == Key.Esc)
                {
                    // Escape clears the input
                    inputField.Text = "";
                    e.Handled = true;
                }
            };

            sendButton.Clicked += async () =>
            {
                if (isProcessing && cancellationTokenSource != null)
                {
                    cancellationTokenSource.Cancel();
                    
                    agentManager.TerminateAllAgents();
                    
                    sendButton.Text = "🚀 Send";
                    sendButton.Enabled = true;
                    inputField.ReadOnly = false;
                    isProcessing = false;
                    
                    UpdateAgentStatus("Cancelled");
                    UpdateStatusBar("Cancelled", "Operation was cancelled");
                    
                    chatView.Text += " [Cancelled]\n\n";
                    chatRenderer.ScrollChatToBottom();
                    
                    inputField.SetFocus();
                    Application.Refresh();
                }
                else
                {
                    await ProcessMessage();
                }
            };
        }

        private void SetInitialFocus()
        {
            Application.MainLoop.AddTimeout(TimeSpan.FromMilliseconds(100), (timer) =>
            {
                inputField.SetFocus();
                // Set welcome message after components are initialized
                chatView.Text = GetWelcomeMessage();
                return false;
            });
        }

        private async Task ProcessMessage()
        {
            if (isProcessing)
                return;

            var message = inputField.Text.ToString();
            if (string.IsNullOrWhiteSpace(message))
                return;

            isProcessing = true;
            cancellationTokenSource = new CancellationTokenSource();

            try
            {
                if (agent.CurrentSessionId == null)
                {
                    await agent.InitializeSessionAsync("main");
                    
                    if (agent.CurrentSessionId != null)
                    {
                        agentManager.SetParentSessionId(agent.CurrentSessionId);
                    }
                }
                
                chatRenderer.AppendToChat($"You: {message}\n");
                inputField.Text = "";
                
                sendButton.Text = " Stop";
                inputField.ReadOnly = true;
                UpdateAgentStatus("Processing", 1);
                UpdateStatusBar("Processing your request...", "AI is thinking...");
                
                chatRenderer.ScrollChatToBottom();
                Application.Refresh();
                chatRenderer.ScrollChatToBottom();

                chatRenderer.AppendToChat("\nAssistant: ");
                chatRenderer.ScrollChatToBottom();
                var startPosition = chatView.Text.Length;
                var responseBuilder = new StringBuilder();

                await Task.Run(async () =>
                {
                    try
                    {
                        if (agent.Configuration.EnableStreaming)
                        {
                            await agent.ExecuteStreamAsync(
                                message,
                                async (chunk) =>
                                {
                                    if (!chunk.IsComplete && !chunk.IsToolCall && !string.IsNullOrEmpty(chunk.Content))
                                    {
                                        responseBuilder.Append(chunk.Content);
                                        Application.MainLoop.Invoke(() =>
                                        {
                                            var currentText = chatView.Text.ToString().Substring(0, startPosition);
                                            chatRenderer.AppendToChat(currentText, applyMarkdown: false);
                                            chatRenderer.AppendToChat(responseBuilder.ToString(), applyMarkdown: true);
                                            chatRenderer.ScrollChatToBottom();
                                            Application.Refresh();
                                        });
                                    }
                                },
                                cancellationTokenSource.Token);

                            Application.MainLoop.Invoke(() =>
                            {
                                var lastResponse = responseBuilder.ToString().TrimEnd();
                                var currentChatText = chatView.Text.ToString();
                                
                                if (!string.IsNullOrEmpty(lastResponse))
                                {
                                    bool endsWithNewline = currentChatText.EndsWith("\n");
                                    bool endsWithDoubleNewline = currentChatText.EndsWith("\n\n");
                                    
                                    char lastChar = lastResponse.Length > 0 ? lastResponse[lastResponse.Length - 1] : '\0';
                                    bool endsWithPunctuation = ".!?:;)]}\"'`".Contains(lastChar);
                                    
                                    if (!endsWithNewline)
                                    {
                                        if (endsWithPunctuation)
                                        {
                                            chatRenderer.AppendToChat("\n\n");
                                        }
                                        else
                                        {
                                            chatRenderer.AppendToChat(" ");
                                        }
                                    }
                                    else if (!endsWithDoubleNewline)
                                    {
                                        chatRenderer.AppendToChat("\n");
                                    }
                                }
                                else
                                {
                                    if (!currentChatText.EndsWith("\n\n"))
                                    {
                                        if (currentChatText.EndsWith("\n"))
                                            chatRenderer.AppendToChat("\n");
                                        else
                                            chatRenderer.AppendToChat("\n\n");
                                    }
                                }
                                
                                chatRenderer.ScrollChatToBottom();
                            });
                        }
                        else
                        {
                            var response = await agent.Execute<OpenRouter.Models.Api.Chat.Message>(message);
                            var responseText = response.Content.ToString();
                            var renderedResponse = markdownRenderer.RenderToTerminal(responseText);

                            Application.MainLoop.Invoke(() =>
                            {
                                chatRenderer.AppendToChat(renderedResponse, applyMarkdown: true);
                                
                                var updatedText = chatView.Text.ToString();
                                bool endsWithNewline = updatedText.EndsWith("\n\n");
                                bool endsWithDoubleNewline = updatedText.EndsWith("\n\n");
                                
                                var trimmedResponse = renderedResponse.TrimEnd();
                                char lastChar = trimmedResponse.Length > 0 ? trimmedResponse[trimmedResponse.Length - 1] : '\0';
                                bool endsWithPunctuation = ".!?:;)]}\"'`".Contains(lastChar);
                                
                                if (!endsWithNewline)
                                {
                                    if (endsWithPunctuation)
                                    {
                                        chatRenderer.AppendToChat("\n\n");
                                    }
                                    else
                                    {
                                        chatRenderer.AppendToChat(" ");
                                    }
                                }
                                else if (!endsWithDoubleNewline)
                                {
                                    chatRenderer.AppendToChat("\n");
                                }
                                
                                chatRenderer.ScrollChatToBottom();
                            });
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Application.MainLoop.Invoke(() =>
                        {
                            chatRenderer.AppendToChat(" [Cancelled]\n\n");
                            chatRenderer.ScrollChatToBottom();
                        });
                    }
                    catch (Exception ex)
                    {
                        Application.MainLoop.Invoke(() =>
                        {
                            chatRenderer.AppendToChat($"[Error: {ex.Message}]\n\n");
                            chatRenderer.ScrollChatToBottom();
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                chatRenderer.AppendToChat($"\n[Error processing message: {ex.Message}]\n\n");
                chatRenderer.ScrollChatToBottom();
            }
            finally
            {
                sendButton.Text = "🚀 Send";
                sendButton.Enabled = true;
                inputField.ReadOnly = false;
                isProcessing = false;
                cancellationTokenSource?.Dispose();
                cancellationTokenSource = null;
                UpdateAgentStatus("Ready");
                UpdateStatusBar("Ready", "Waiting for your input...");
                inputField.SetFocus();
                Application.Refresh();
            }
        }

        private FrameView CreateHelpPanel()
        {
            var panel = new FrameView("Quick Help")
            {
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                ColorScheme = Colors.Base
            };

            var helpView = new TextView()
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                ReadOnly = true,
                WordWrap = true,
                Text = GetHelpText(),
                ColorScheme = Colors.Base
            };

            panel.Add(helpView);
            return panel;
        }

        private string GetHelpText()
        {
            var help = "Saturn Help\n";
            help += "===========\n";
            help += "F1-Help F2-F4:Modes Ctrl+L:Load Ctrl+S:Save\n";
            help += "Enter:Send Ctrl+Enter:NewLine Ctrl+K:Clear\n\n";
            help += "Commands: Read,Fix,Test,Refactor\n";
            help += "Features: Multi-agent,Parallel,Background\n";
            help += "Tips: Be specific, Use agents for complex tasks";
            
            return help;
        }

        private FrameView CreateStatusBar()
        {
            var statusBar = new FrameView("Status")
            {
                X = 0,
                Y = Pos.AnchorEnd(1),
                Width = Dim.Fill(),
                Height = 1,
                ColorScheme = Colors.Base
            };

            var statusLabel = new Label("Ready | F1: Help | F2-F4: Modes | Ctrl+S: Save | Ctrl+L: Load | Ctrl+K: Clear")
            {
                X = 0,
                Y = 0,
                ColorScheme = Colors.Base,
                Text = "Ready | F1: Help | F2-F4: Modes | Ctrl+S: Save | Ctrl+L: Load | Ctrl+K: Clear"
            };

            statusBar.Add(statusLabel);
            return statusBar;
        }

        private string GetWelcomeMessage()
        {
            var welcome = "Welcome to Saturn!\n\n";
            welcome += "AI coding assistant with multi-agent capabilities.\n\n";
            welcome += "I can help with:\n";
            welcome += "• Code analysis and debugging\n";
            welcome += "• File operations and refactoring\n";
            welcome += "• Multi-agent task delegation\n";
            welcome += "• Web research and documentation\n";
            welcome += "• Testing and code generation\n\n";
            welcome += "Quick Start:\n";
            welcome += "• Type your request below\n";
            welcome += "• Press F2-F4 to change modes\n";
            welcome += "• Press F1 for help\n\n";
            welcome += "Example: \"Read the main.cs file and explain the main function\"\n\n";
            welcome += "Ready to help! What would you like me to do?";
            
            return welcome;
        }

        public void Run()
        {
            Application.Run(app);
            Application.Shutdown();
        }
    }
}