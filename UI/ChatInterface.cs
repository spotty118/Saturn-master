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
using Saturn.OpenRouter.Models.Api.Chat;
using Saturn.Tools.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Saturn.UI
{
    public class ChatInterface
    {
        private TextView chatView = null!;
        private TextView inputField = null!;
        private Button sendButton = null!;
        private Toplevel app = null!;
        private FrameView toolCallsPanel = null!;
        private FrameView agentStatusPanel = null!;
        private TextView toolCallsView = null!;
        private TextView agentStatusView = null!;
        private Agent agent;
        private bool isProcessing;
        private CancellationTokenSource? cancellationTokenSource;
        private OpenRouterClient? openRouterClient;
        private MarkdownRenderer markdownRenderer;
        
        // DI services
        private readonly AgentManager agentManager;
        private readonly ToolRegistry toolRegistry;
        private readonly ConfigurationManager configurationManager;
        
        // Extracted services
        private readonly IChatRenderer chatRenderer;
        private readonly IDialogManager dialogManager;
        private readonly IAgentConfigurationManager agentConfigurationManager;
        private readonly IChatSessionManager chatSessionManager;

        public ChatInterface(Agent aiAgent, OpenRouterClient? client = null, IServiceProvider? serviceProvider = null)
        {
            agent = aiAgent ?? throw new ArgumentNullException(nameof(aiAgent));
            openRouterClient = client ?? agent.Configuration.Client as OpenRouterClient;
            isProcessing = false;
            
            // Get services from DI container
            if (serviceProvider == null)
                throw new ArgumentNullException(nameof(serviceProvider));
                
            agentManager = serviceProvider.GetRequiredService<AgentManager>();
            toolRegistry = serviceProvider.GetRequiredService<ToolRegistry>();
            configurationManager = serviceProvider.GetRequiredService<ConfigurationManager>();
            
            // Get extracted services
            chatRenderer = serviceProvider.GetRequiredService<IChatRenderer>();
            dialogManager = serviceProvider.GetRequiredService<IDialogManager>();
            agentConfigurationManager = serviceProvider.GetRequiredService<IAgentConfigurationManager>();
            chatSessionManager = serviceProvider.GetRequiredService<IChatSessionManager>();
            
            agent.OnToolCall += (toolName, args) => UpdateToolCall(toolName, args);
            markdownRenderer = new MarkdownRenderer();
            
            InitializeExtractedServices();
            InitializeAgentManager();
        }
        
        private void InitializeExtractedServices()
        {
            // Initialize dialog manager
            dialogManager.Initialize(openRouterClient!, toolRegistry);
            dialogManager.ConfigurationChanged += async (config) =>
            {
                await agentConfigurationManager.ReconfigureAgentAsync();
                chatRenderer.ClearChat(agent);
                Application.Refresh();
            };
            dialogManager.LoadChatRequested += async (sessionId) =>
            {
                try
                {
                    await chatSessionManager.LoadChatSessionAsync(sessionId);
                }
                catch (Exception ex)
                {
                    MessageBox.ErrorQuery("Error", $"Failed to load chat: {ex.Message}", "OK");
                }
            };
            
            // Initialize agent configuration manager
            agentConfigurationManager.Initialize(agent, openRouterClient!, configurationManager);
            agentConfigurationManager.AgentReconfigured += (newAgent) =>
            {
                agent = newAgent;
                agent.OnToolCall += (toolName, args) => UpdateToolCall(toolName, args);
                chatRenderer.ClearChat(agent);
                dialogManager.SetCurrentConfiguration(agentConfigurationManager.CurrentConfig);
            };
            
            // Initialize chat session manager (will be done after UI components are created)
            chatSessionManager.StatusUpdated += (status) => UpdateAgentStatus(status);
        }
        
        private void InitializeAgentManager()
        {
            agentManager.Initialize(openRouterClient!);
            
            agentManager.OnAgentCreated += (agentId, name) =>
            {
                UpdateAgentStatus("Managing sub-agents", 1, new List<string> { $"{name} ({agentId})" });
            };
            
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
            var mainContainer = CreateChatContainer();
            var inputContainer = CreateInputContainer();
            toolCallsPanel = CreateToolCallsPanel();
            agentStatusPanel = CreateAgentStatusPanel();
            
            // Now that UI components are created, initialize services that need them
            chatRenderer.Initialize(chatView, markdownRenderer);
            chatSessionManager.Initialize(agent, chatView, toolCallsView, markdownRenderer);
            dialogManager.SetCurrentConfiguration(agentConfigurationManager.CurrentConfig);
            
            SetupScrollBar(mainContainer);
            SetupInputHandlers();
            
            inputContainer.Add(inputField, sendButton);
            app.Add(menu, mainContainer, inputContainer, toolCallsPanel, agentStatusPanel);
            
            SetInitialFocus();
        }

        public void Run()
        {
            Application.Run(app);
            Application.Shutdown();
        }

        private void SetupTheme()
        {
            Colors.Base.Normal = Application.Driver.MakeAttribute(Color.Gray, Color.Black);
            Colors.Base.Focus = Application.Driver.MakeAttribute(Color.White, Color.Black);
            Colors.Base.HotNormal = Application.Driver.MakeAttribute(Color.Gray, Color.Black);
            Colors.Base.HotFocus = Application.Driver.MakeAttribute(Color.BrightMagenta, Color.Black);
            Colors.Menu.Normal = Application.Driver.MakeAttribute(Color.Gray, Color.Black);
            Colors.Menu.Focus = Application.Driver.MakeAttribute(Color.White, Color.Black);
            Colors.Menu.HotNormal = Application.Driver.MakeAttribute(Color.White, Color.Black);
            Colors.Menu.HotFocus = Application.Driver.MakeAttribute(Color.BrightMagenta, Color.Black);
            Colors.Menu.Disabled = Application.Driver.MakeAttribute(Color.DarkGray, Color.Black);
            Colors.Dialog.Normal = Application.Driver.MakeAttribute(Color.Gray, Color.Black);
            Colors.Dialog.Focus = Application.Driver.MakeAttribute(Color.White, Color.Black);
            Colors.Dialog.HotNormal = Application.Driver.MakeAttribute(Color.White, Color.Black);
            Colors.Dialog.HotFocus = Application.Driver.MakeAttribute(Color.BrightMagenta, Color.Black);
            Colors.Error.Normal = Application.Driver.MakeAttribute(Color.Red, Color.Black);
            Colors.Error.Focus = Application.Driver.MakeAttribute(Color.White, Color.Black);
            Colors.Error.HotNormal = Application.Driver.MakeAttribute(Color.BrightRed, Color.Black);
            Colors.Error.HotFocus = Application.Driver.MakeAttribute(Color.BrightRed, Color.Black);
        }

        private MenuBar CreateMenu()
        {
            return new MenuBar(new MenuBarItem[]
            {
                new MenuBarItem("_Options", new MenuItem[]
                {
                    new MenuItem("_Load Chat...", "", async () => await dialogManager.ShowLoadChatDialogAsync()),
                    new MenuItem("_Clear Chat", "", () =>
                    {
                        if (chatView != null)
                        {
                            if (isProcessing && cancellationTokenSource != null)
                            {
                                cancellationTokenSource.Cancel();
                                cancellationTokenSource?.Dispose();
                                cancellationTokenSource = null;
                            }
                            isProcessing = false;
                            
                            chatRenderer.ClearChat(agent);
                            
                            inputField.Text = "";
                            inputField.ReadOnly = false;
                            
                            sendButton.Text = "Send";
                            sendButton.Enabled = true;
                            
                            agent?.ClearHistory();
                            
                            toolCallsView.Text = "No tool calls yet...\n";
                            
                            agentManager.TerminateAllAgents();
                            
                            UpdateAgentStatus("Ready");
                            
                            inputField.SetFocus();
                            Application.Refresh();
                        }
                    }),
                    null!,
                    new MenuItem("_Quit", "", () =>
                    {
                        Application.RequestStop();
                    })
                }),
                new MenuBarItem("_Agent", new MenuItem?[]
                {
                    new MenuItem("_Modes...", "", async () => await dialogManager.ShowModeSelectionDialogAsync()),
                    null,
                    new MenuItem("_Select Model...", "", async () => await dialogManager.ShowModelSelectionDialogAsync()),
                    new MenuItem("_Temperature...", "", () => dialogManager.ShowTemperatureDialog()),
                    new MenuItem("_Max Tokens...", "", () => dialogManager.ShowMaxTokensDialog()),
                    new MenuItem("Top _P...", "", () => dialogManager.ShowTopPDialog()),
                    new MenuItem("Select _Tools...", "", async () => await dialogManager.ShowToolSelectionDialogAsync()),
                    null,
                    new MenuItem("_Streaming", "", async () => await agentConfigurationManager.ToggleStreamingAsync()) 
                        { Checked = agentConfigurationManager.CurrentConfig.EnableStreaming },
                    new MenuItem("_Maintain History", "", async () => await agentConfigurationManager.ToggleMaintainHistoryAsync()) 
                        { Checked = agentConfigurationManager.CurrentConfig.MaintainHistory },
                    new MenuItem("_Command Approval", "", async () => await agentConfigurationManager.ToggleCommandApprovalAsync()) 
                        { Checked = agentConfigurationManager.CurrentConfig.RequireCommandApproval },
                    new MenuItem("Max _History Messages...", "", () => dialogManager.ShowMaxHistoryDialog()),
                    null,
                    new MenuItem("_Edit System Prompt...", "", () => dialogManager.ShowSystemPromptDialog()),
                    new MenuItem("_View Configuration...", "", () => dialogManager.ShowConfigurationDialog())
                }),
            });
        }

        private Toplevel CreateMainWindow()
        {
            return new Toplevel()
            {
                ColorScheme = Colors.Base
            };
        }

        private FrameView CreateChatContainer()
        {
            var mainContainer = new FrameView("AI Chat")
            {
                X = 0,
                Y = 1,
                Width = Dim.Percent(75),
                Height = Dim.Fill(3),
                ColorScheme = Colors.Base
            };

            chatView = new TextView()
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(1),
                Height = Dim.Fill(),
                ReadOnly = true,
                WordWrap = true,
                Text = "", // Will be set by chatRenderer after initialization
                ColorScheme = Colors.Base
            };

            mainContainer.Add(chatView);
            return mainContainer;
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

        private FrameView CreateInputContainer()
        {
            var inputContainer = new FrameView("Input (Ctrl+Enter to send)")
            {
                X = 0,
                Y = Pos.AnchorEnd(3),
                Width = Dim.Percent(75),
                Height = 3,
                ColorScheme = Colors.Base
            };

            inputField = new TextView()
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(10),
                Height = Dim.Fill(),
                CanFocus = true,
                ColorScheme = Colors.Base,
                WordWrap = true
            };

            sendButton = new Button("Send")
            {
                X = Pos.Right(inputField) + 1,
                Y = Pos.Center(),
                ColorScheme = Colors.Base
            };

            return inputContainer;
        }

        private FrameView CreateToolCallsPanel()
        {
            var panel = new FrameView("Tool Calls")
            {
                X = Pos.Percent(75),
                Y = 1,
                Width = Dim.Fill(),
                Height = Dim.Percent(50) - 2,
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
                Text = "No tool calls yet...\n",
                ColorScheme = Colors.Base
            };

            panel.Add(toolCallsView);
            return panel;
        }

        private FrameView CreateAgentStatusPanel()
        {
            var panel = new FrameView("Agent Status")
            {
                X = Pos.Percent(75),
                Y = Pos.Percent(50) - 1,
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
            var status = "Main Agent: Ready\n";
            status += "═════════════════\n\n";
            status += "Status: Idle\n";
            status += "Tasks: 0 pending\n\n";
            status += "Sub-agents:\n";
            status += "• None active\n\n";
            return status;
        }

        public void UpdateToolCall(string toolName, string arguments)
        {
            Application.MainLoop.Invoke(() =>
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                var currentText = toolCallsView.Text.ToString();
                
                if (currentText == "No tool calls yet...\n")
                {
                    currentText = "";
                }
                
                var summary = GetToolSummary(toolName, arguments);
                var newEntry = $"[{timestamp}] {toolName}: {summary}\n";
                newEntry += "───────────────\n";
                
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
            catch
            {
            }
            
            return toolName;
        }
        
        private object GetJsonValue(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    return element.GetString();
                case JsonValueKind.Number:
                    if (element.TryGetInt32(out var intValue))
                        return intValue;
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

        public void UpdateAgentStatus(string status, int activeTasks = 0, List<string>? subAgents = null)
        {
            Application.MainLoop.Invoke(() =>
            {
                var agents = agentManager.GetAllAgentStatuses();
                var completedTasks = agents.Sum(a => a.CurrentTask != null ? 1 : 0);
                var currentCount = agentManager.GetCurrentAgentCount();
                var maxCount = agentManager.GetMaxConcurrentAgents();
                
                var statusText = $"Main Agent: {status}\n";
                statusText += "═════════════════\n\n";
                statusText += $"Status: {status}\n";
                statusText += $"Active Tasks: {activeTasks}\n";
                statusText += $"Total Agents: {currentCount}/{maxCount}\n\n";
                statusText += "Sub-agents:\n";
                
                if (agents.Any())
                {
                    foreach (var agent in agents)
                    {
                        statusText += $"• {agent.Name}\n";
                        statusText += $"  Status: {agent.Status}\n";
                        if (!string.IsNullOrEmpty(agent.CurrentTask))
                        {
                            statusText += $"  Task: {agent.CurrentTask}\n";
                            statusText += $"  Time: {agent.RunningTime.TotalSeconds:F1}s\n";
                        }
                    }
                }
                else if (subAgents != null && subAgents.Count > 0)
                {
                    foreach (var agent in subAgents)
                    {
                        statusText += $"• {agent}\n";
                    }
                }
                else
                {
                    statusText += "• None active\n";
                }
                
                agentStatusView.Text = statusText;
            });
        }

        private void SetupInputHandlers()
        {
            inputField.KeyDown += (e) =>
            {
                if (e.KeyEvent.Key == (Key.CtrlMask | Key.Enter))
                {
                    sendButton.OnClicked();
                    e.Handled = true;
                }
            };

            sendButton.Clicked += async () =>
            {
                if (isProcessing && cancellationTokenSource != null)
                {
                    cancellationTokenSource.Cancel();
                    
                    agentManager.TerminateAllAgents();
                    
                    sendButton.Text = "Send";
                    sendButton.Enabled = true;
                    inputField.ReadOnly = false;
                    isProcessing = false;
                    
                    UpdateAgentStatus("Cancelled");
                    
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
                chatView.Text = chatRenderer.GetWelcomeMessage(agent);
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
                
                chatView.Text += $"You: {message}\n";
                inputField.Text = "";
                
                sendButton.Text = " Stop";
                inputField.ReadOnly = true;
                UpdateAgentStatus("Processing", 1);
                
                chatRenderer.ScrollChatToBottom();
                Application.Refresh();
                chatRenderer.ScrollChatToBottom();

                chatView.Text += "\nAssistant: ";
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
                                            var currentText = chatView.Text.Substring(0, startPosition);
                                            var renderedResponse = markdownRenderer.RenderToTerminal(responseBuilder.ToString());
                                            chatView.Text = currentText + renderedResponse;
                                            chatRenderer.ScrollChatToBottom();
                                            Application.Refresh();
                                        });
                                    }
                                },
                                cancellationTokenSource.Token);

                            Application.MainLoop.Invoke(() =>
                            {
                                var currentText = chatView.Text;
                                var lastResponse = responseBuilder.ToString().TrimEnd();
                                
                                if (!string.IsNullOrEmpty(lastResponse))
                                {
                                    bool endsWithNewline = currentText.EndsWith("\n");
                                    bool endsWithDoubleNewline = currentText.EndsWith("\n\n");
                                    
                                    char lastChar = lastResponse.Length > 0 ? lastResponse[lastResponse.Length - 1] : '\0';
                                    bool endsWithPunctuation = ".!?:;)]}\"'`".Contains(lastChar);
                                    
                                    if (!endsWithNewline)
                                    {
                                        if (endsWithPunctuation)
                                        {
                                            chatView.Text += "\n\n";
                                        }
                                        else
                                        {
                                            chatView.Text += " ";
                                        }
                                    }
                                    else if (!endsWithDoubleNewline)
                                    {
                                        chatView.Text += "\n";
                                    }
                                }
                                else
                                {
                                    if (!currentText.EndsWith("\n\n"))
                                    {
                                        if (currentText.EndsWith("\n"))
                                            chatView.Text += "\n";
                                        else
                                            chatView.Text += "\n\n";
                                    }
                                }
                                
                                chatRenderer.ScrollChatToBottom();
                            });
                        }
                        else
                        {
                            Message response = await agent.Execute<Message>(message);
                            var responseText = response.Content.ToString();
                            var renderedResponse = markdownRenderer.RenderToTerminal(responseText);

                            Application.MainLoop.Invoke(() =>
                            {
                                var currentText = chatView.Text;
                                
                                chatView.Text += renderedResponse;
                                
                                var updatedText = chatView.Text;
                                bool endsWithNewline = updatedText.EndsWith("\n\n");
                                bool endsWithDoubleNewline = updatedText.EndsWith("\n\n");
                                
                                var trimmedResponse = renderedResponse.TrimEnd();
                                char lastChar = trimmedResponse.Length > 0 ? trimmedResponse[trimmedResponse.Length - 1] : '\0';
                                bool endsWithPunctuation = ".!?:;)]}\"'`".Contains(lastChar);
                                
                                if (!endsWithNewline)
                                {
                                    if (endsWithPunctuation)
                                    {
                                        chatView.Text += "\n\n";
                                    }
                                    else
                                    {
                                        chatView.Text += " ";
                                    }
                                }
                                else if (!endsWithDoubleNewline)
                                {
                                    chatView.Text += "\n";
                                }
                                
                                chatRenderer.ScrollChatToBottom();
                            });
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Application.MainLoop.Invoke(() =>
                        {
                            chatView.Text += " [Cancelled]\n\n";
                            chatRenderer.ScrollChatToBottom();
                        });
                    }
                    catch (Exception ex)
                    {
                        Application.MainLoop.Invoke(() =>
                        {
                            chatView.Text += $"[Error: {ex.Message}]\n\n";
                            chatRenderer.ScrollChatToBottom();
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                chatView.Text += $"\n[Error processing message: {ex.Message}]\n\n";
                chatRenderer.ScrollChatToBottom();
            }
            finally
            {
                sendButton.Text = "Send";
                sendButton.Enabled = true;
                inputField.ReadOnly = false;
                isProcessing = false;
                cancellationTokenSource?.Dispose();
                cancellationTokenSource = null;
                UpdateAgentStatus("Ready");
                inputField.SetFocus();
                Application.Refresh();
            }
        }
    }
}