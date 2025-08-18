using System;
using System.Linq;
using System.Threading.Tasks;
using Terminal.Gui;
using Saturn.Agents.Core;
using Saturn.OpenRouter;
using Saturn.OpenRouter.Models.Api.Models;
using Saturn.Tools.Core;
using Saturn.UI.Dialogs;

namespace Saturn.UI
{
    public class DialogManager : IDialogManager
    {
        private UIAgentConfiguration currentConfig = null!;
        private OpenRouterClient? openRouterClient;
        private ToolRegistry? toolRegistry;

        public event Action<UIAgentConfiguration>? ConfigurationChanged;
        public event Action<string>? LoadChatRequested;

        public void Initialize(OpenRouterClient openRouterClient, ToolRegistry toolRegistry)
        {
            this.openRouterClient = openRouterClient;
            this.toolRegistry = toolRegistry;
        }

        public void SetCurrentConfiguration(UIAgentConfiguration config)
        {
            currentConfig = config;
        }

        public async Task ShowModelSelectionDialogAsync()
        {
            if (openRouterClient == null) return;
            
            var models = await UIAgentConfiguration.GetAvailableModels(openRouterClient);
            var modelNames = models.Select(m => m.Name ?? m.Id).ToArray();
            var currentIndex = Array.FindIndex(modelNames, m => models[Array.IndexOf(modelNames, m)].Id == currentConfig.Model);
            if (currentIndex < 0) currentIndex = 0;

            var dialog = new Dialog("Select Model", 60, 20);
            dialog.ColorScheme = Colors.Dialog;

            var listView = new ListView(modelNames)
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill(1),
                Height = Dim.Fill(3),
                SelectedItem = currentIndex
            };

            var infoLabel = new Label("")
            {
                X = 1,
                Y = Pos.Bottom(listView) + 1,
                Width = Dim.Fill(1),
                Height = 1
            };

            Action selectModel = () =>
            {
                var selectedModel = models[listView.SelectedItem];
                currentConfig.Model = selectedModel.Id;
                
                if (selectedModel.Id.Contains("gpt-5", StringComparison.OrdinalIgnoreCase))
                {
                    currentConfig.Temperature = 1.0;
                }
                
                ConfigurationChanged?.Invoke(currentConfig);
                Application.RequestStop();
            };

            listView.SelectedItemChanged += (args) =>
            {
                var selectedModel = models[args.Item];
                var info = $"ID: {selectedModel.Id}";
                if (selectedModel.ContextLength.HasValue)
                    info += $" | Context: {selectedModel.ContextLength:N0} tokens";
                infoLabel.Text = info;
            };
            
            listView.OpenSelectedItem += (args) =>
            {
                selectModel();
            };

            var okButton = new Button(" _OK ", true)
            {
                X = Pos.Center() - 10,
                Y = Pos.Bottom(infoLabel) + 1
            };

            okButton.Clicked += () => selectModel();

            var cancelButton = new Button(" _Cancel ")
            {
                X = Pos.Center() + 5,
                Y = Pos.Bottom(infoLabel) + 1
            };

            cancelButton.Clicked += () => Application.RequestStop();

            dialog.Add(listView, infoLabel, okButton, cancelButton);
            
            if (models.Count > 0 && currentIndex >= 0)
            {
                var initialModel = models[currentIndex];
                var info = $"ID: {initialModel.Id}";
                if (initialModel.ContextLength.HasValue)
                    info += $" | Context: {initialModel.ContextLength:N0} tokens";
                infoLabel.Text = info;
            }
            
            listView.SetFocus();
            Application.Run(dialog);
        }

        public void ShowTemperatureDialog()
        {
            if (currentConfig.Model.Contains("gpt-5", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.ErrorQuery("Temperature Locked", 
                    "Temperature is locked to 1.0 for GPT-5 models and cannot be changed.", "OK");
                return;
            }
            
            var dialog = new Dialog("Set Temperature", 50, 10);
            dialog.ColorScheme = Colors.Dialog;

            var label = new Label($"Temperature (0.0 - 2.0): Current = {currentConfig.Temperature:F2}")
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill(1)
            };

            var textField = new TextField(currentConfig.Temperature.ToString("F2"))
            {
                X = 1,
                Y = 3,
                Width = Dim.Fill(1)
            };

            var okButton = new Button("OK", true)
            {
                X = Pos.Center() - 10,
                Y = 5
            };

            okButton.Clicked += () =>
            {
                if (double.TryParse(textField.Text.ToString(), out double temp) && temp >= 0 && temp <= 2)
                {
                    currentConfig.Temperature = temp;
                    ConfigurationChanged?.Invoke(currentConfig);
                    Application.RequestStop();
                }
                else
                {
                    MessageBox.ErrorQuery("Invalid Input", "Please enter a value between 0.0 and 2.0", "OK");
                }
            };

            var cancelButton = new Button("Cancel")
            {
                X = Pos.Center() + 5,
                Y = 5
            };

            cancelButton.Clicked += () => Application.RequestStop();

            dialog.Add(label, textField, okButton, cancelButton);
            textField.SetFocus();
            Application.Run(dialog);
        }

        public void ShowMaxTokensDialog()
        {
            var dialog = new Dialog("Set Max Tokens", 50, 10);
            dialog.ColorScheme = Colors.Dialog;

            var label = new Label($"Max Tokens: Current = {currentConfig.MaxTokens}")
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill(1)
            };

            var textField = new TextField(currentConfig.MaxTokens.ToString())
            {
                X = 1,
                Y = 3,
                Width = Dim.Fill(1)
            };

            var okButton = new Button("OK", true)
            {
                X = Pos.Center() - 10,
                Y = 5
            };

            okButton.Clicked += () =>
            {
                if (int.TryParse(textField.Text.ToString(), out int tokens) && tokens > 0 && tokens <= 200000)
                {
                    currentConfig.MaxTokens = tokens;
                    ConfigurationChanged?.Invoke(currentConfig);
                    Application.RequestStop();
                }
                else
                {
                    MessageBox.ErrorQuery("Invalid Input", "Please enter a value between 1 and 200000", "OK");
                }
            };

            var cancelButton = new Button("Cancel")
            {
                X = Pos.Center() + 5,
                Y = 5
            };

            cancelButton.Clicked += () => Application.RequestStop();

            dialog.Add(label, textField, okButton, cancelButton);
            textField.SetFocus();
            Application.Run(dialog);
        }

        public void ShowTopPDialog()
        {
            var dialog = new Dialog("Set Top P", 50, 10);
            dialog.ColorScheme = Colors.Dialog;

            var label = new Label($"Top P (0.0 - 1.0): Current = {currentConfig.TopP:F2}")
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill(1)
            };

            var textField = new TextField(currentConfig.TopP.ToString("F2"))
            {
                X = 1,
                Y = 3,
                Width = Dim.Fill(1)
            };

            var okButton = new Button("OK", true)
            {
                X = Pos.Center() - 10,
                Y = 5
            };

            okButton.Clicked += () =>
            {
                if (double.TryParse(textField.Text.ToString(), out double topP) && topP >= 0 && topP <= 1)
                {
                    currentConfig.TopP = topP;
                    ConfigurationChanged?.Invoke(currentConfig);
                    Application.RequestStop();
                }
                else
                {
                    MessageBox.ErrorQuery("Invalid Input", "Please enter a value between 0.0 and 1.0", "OK");
                }
            };

            var cancelButton = new Button("Cancel")
            {
                X = Pos.Center() + 5,
                Y = 5
            };

            cancelButton.Clicked += () => Application.RequestStop();

            dialog.Add(label, textField, okButton, cancelButton);
            textField.SetFocus();
            Application.Run(dialog);
        }

        public void ShowMaxHistoryDialog()
        {
            var dialog = new Dialog("Set Max History Messages", 50, 10);
            dialog.ColorScheme = Colors.Dialog;

            var label = new Label($"Max History Messages (0-100): Current = {currentConfig.MaxHistoryMessages}")
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill(1)
            };

            var textField = new TextField(currentConfig.MaxHistoryMessages.ToString())
            {
                X = 1,
                Y = 3,
                Width = Dim.Fill(1)
            };

            var okButton = new Button("OK", true)
            {
                X = Pos.Center() - 10,
                Y = 5
            };

            okButton.Clicked += () =>
            {
                if (int.TryParse(textField.Text.ToString(), out int maxHistory) && maxHistory >= 0 && maxHistory <= 100)
                {
                    currentConfig.MaxHistoryMessages = maxHistory;
                    ConfigurationChanged?.Invoke(currentConfig);
                    Application.RequestStop();
                }
                else
                {
                    MessageBox.ErrorQuery("Invalid Input", "Please enter a value between 0 and 100", "OK");
                }
            };

            var cancelButton = new Button("Cancel")
            {
                X = Pos.Center() + 5,
                Y = 5
            };

            cancelButton.Clicked += () => Application.RequestStop();

            dialog.Add(label, textField, okButton, cancelButton);
            textField.SetFocus();
            Application.Run(dialog);
        }

        public void ShowSystemPromptDialog()
        {
            var dialog = new Dialog("Edit System Prompt", 70, 20);
            dialog.ColorScheme = Colors.Dialog;

            var textView = new TextView()
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill(1),
                Height = Dim.Fill(3),
                Text = currentConfig.SystemPrompt,
                WordWrap = true
            };

            var okButton = new Button("OK", true)
            {
                X = Pos.Center() - 10,
                Y = Pos.Bottom(textView) + 1
            };

            okButton.Clicked += () =>
            {
                currentConfig.SystemPrompt = textView.Text.ToString();
                ConfigurationChanged?.Invoke(currentConfig);
                Application.RequestStop();
            };

            var cancelButton = new Button("Cancel")
            {
                X = Pos.Center() + 5,
                Y = Pos.Bottom(textView) + 1
            };

            cancelButton.Clicked += () => Application.RequestStop();

            dialog.Add(textView, okButton, cancelButton);
            textView.SetFocus();
            Application.Run(dialog);
        }

        public void ShowConfigurationDialog()
        {
            var config = $"Current Agent Configuration\n" +
                        $"===========================\n" +
                        $"Model: {currentConfig.Model}\n" +
                        $"Temperature: {currentConfig.Temperature:F2}\n" +
                        $"Max Tokens: {currentConfig.MaxTokens}\n" +
                        $"Top P: {currentConfig.TopP:F2}\n" +
                        $"Streaming: {(currentConfig.EnableStreaming ? "Enabled" : "Disabled")}\n" +
                        $"Maintain History: {(currentConfig.MaintainHistory ? "Enabled" : "Disabled")}\n" +
                        $"Command Approval: {(currentConfig.RequireCommandApproval ? "Enabled" : "Disabled")}\n" +
                        $"Max History Messages: {currentConfig.MaxHistoryMessages}\n" +
                        $"Tools: {(currentConfig.EnableTools ? $"Enabled ({currentConfig.ToolNames?.Count ?? 0} selected)" : "Disabled")}\n\n" +
                        $"System Prompt:\n{currentConfig.SystemPrompt}";

            MessageBox.Query("Agent Configuration", config, "OK");
        }

        public async Task ShowModeSelectionDialogAsync()
        {
            var dialog = new ModeSelectionDialog();
            Application.Run(dialog);
            
            if (dialog.SelectedMode != null)
            {
                try
                {
                    ApplyModeToConfiguration(dialog.SelectedMode);
                    ConfigurationChanged?.Invoke(currentConfig);
                }
                catch (Exception ex)
                {
                    MessageBox.ErrorQuery("Error", $"Failed to apply mode: {ex.Message}", "OK");
                }
            }
            else if (dialog.ShouldCreateNew)
            {
                await ShowModeEditorDialogAsync(null!);
            }
            else if (dialog.ModeToEdit != null)
            {
                await ShowModeEditorDialogAsync(dialog.ModeToEdit);
            }
        }

        private async Task ShowModeEditorDialogAsync(Mode? modeToEdit)
        {
            var editorDialog = new ModeEditorDialog(modeToEdit, openRouterClient);
            Application.Run(editorDialog);
            
            if (editorDialog.ResultMode != null)
            {
                var message = modeToEdit != null 
                    ? $"Mode '{editorDialog.ResultMode.Name}' updated successfully"
                    : $"Mode '{editorDialog.ResultMode.Name}' created successfully";
                    
                MessageBox.Query("Success", message, "OK");
                
                var applyNow = MessageBox.Query("Apply Mode", 
                    $"Would you like to apply the mode '{editorDialog.ResultMode.Name}' now?", 
                    "Yes", "No");
                    
                if (applyNow == 0)
                {
                    ApplyModeToConfiguration(editorDialog.ResultMode);
                    ConfigurationChanged?.Invoke(currentConfig);
                }
            }
        }

        private void ApplyModeToConfiguration(Mode mode)
        {
            currentConfig.Model = mode.Model;
            currentConfig.Temperature = mode.Temperature;
            currentConfig.MaxTokens = mode.MaxTokens;
            currentConfig.TopP = mode.TopP;
            currentConfig.EnableStreaming = mode.EnableStreaming;
            currentConfig.MaintainHistory = mode.MaintainHistory;
            currentConfig.RequireCommandApproval = mode.RequireCommandApproval;
            currentConfig.ToolNames = new System.Collections.Generic.List<string>(mode.ToolNames ?? new System.Collections.Generic.List<string>());
            currentConfig.EnableTools = mode.ToolNames?.Count > 0;
            
            if (!string.IsNullOrWhiteSpace(mode.SystemPromptOverride))
            {
                currentConfig.SystemPrompt = mode.SystemPromptOverride;
            }
        }

        public async Task ShowToolSelectionDialogAsync()
        {
            if (toolRegistry == null) return;
            
            var dialog = new ToolSelectionDialog(currentConfig.ToolNames, toolRegistry);
            Application.Run(dialog);
            
            if (dialog.SelectedTools.Count > 0 || currentConfig.ToolNames?.Count > 0)
            {
                currentConfig.ToolNames = dialog.SelectedTools;
                currentConfig.EnableTools = dialog.SelectedTools.Count > 0;
                ConfigurationChanged?.Invoke(currentConfig);
            }
        }

        public async Task ShowLoadChatDialogAsync()
        {
            var dialog = new LoadChatDialog();
            Application.Run(dialog);
            
            if (!string.IsNullOrEmpty(dialog.SelectedSessionId))
            {
                LoadChatRequested?.Invoke(dialog.SelectedSessionId);
            }
        }
    }
}