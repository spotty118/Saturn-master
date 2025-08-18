using System;
using System.Collections.Generic;
using System.Linq;
using Terminal.Gui;
using Saturn.Tools.Core;

namespace Saturn.UI.Dialogs
{
    public class ToolSelectionDialog : Dialog
    {
        private ListView toolsListView = null!;
        private Label descriptionLabel = null!;
        private readonly List<ITool> availableTools;
        private readonly string[] toolDisplayNames;
        private readonly bool[] selectedStates;
        public List<string> SelectedTools { get; private set; } = new List<string>();

        public ToolSelectionDialog(List<string>? currentlySelected = null, ToolRegistry? toolRegistry = null)
            : base("Select Tools", 70, 25)
        {
            var registry = toolRegistry ?? new ToolRegistry(null!); // Fallback for backward compatibility
            availableTools = registry.GetAll().OrderBy(t => t.Name).ToList();
            toolDisplayNames = new string[availableTools.Count];
            selectedStates = new bool[availableTools.Count];

            for (int i = 0; i < availableTools.Count; i++)
            {
                var tool = availableTools[i];
                toolDisplayNames[i] = $"{tool.Name} - {tool.Description}";
                selectedStates[i] = currentlySelected?.Contains(tool.Name) == true;
            }

            SelectedTools = currentlySelected?.ToList() ?? new List<string>();
            CreateControls();
        }

        private void CreateControls()
        {
            toolsListView = new ListView(toolDisplayNames)
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill(1),
                Height = Dim.Fill(5),
                CanFocus = true
            };

            toolsListView.SelectedItemChanged += OnSelectedItemChanged;
            toolsListView.KeyPress += OnListViewKeyPress;

            var separatorLine = new Label(new string('─', 68))
            {
                X = 0,
                Y = Pos.Bottom(toolsListView) + 1,
                Width = Dim.Fill()
            };

            descriptionLabel = new Label("Select a tool to see its full description")
            {
                X = 1,
                Y = Pos.Bottom(separatorLine),
                Width = Dim.Fill(1),
                Height = 2,
                TextAlignment = TextAlignment.Left
            };

            var buttonSeparator = new Label(new string('─', 68))
            {
                X = 0,
                Y = Pos.Bottom(descriptionLabel) + 1,
                Width = Dim.Fill()
            };

            var selectAllButton = new Button("Select _All")
            {
                X = Pos.Center() - 25,
                Y = Pos.Bottom(buttonSeparator) + 1
            };
            selectAllButton.Clicked += OnSelectAllClicked;

            var clearAllButton = new Button("_Clear All")
            {
                X = Pos.Right(selectAllButton) + 2,
                Y = Pos.Top(selectAllButton)
            };
            clearAllButton.Clicked += OnClearAllClicked;

            var okButton = new Button("_OK", true)
            {
                X = Pos.Right(clearAllButton) + 4,
                Y = Pos.Top(selectAllButton)
            };
            okButton.Clicked += () => 
            {
                UpdateSelectedTools();
                Application.RequestStop();
            };

            var cancelButton = new Button("_Cancel")
            {
                X = Pos.Right(okButton) + 2,
                Y = Pos.Top(selectAllButton)
            };
            cancelButton.Clicked += () =>
            {
                SelectedTools.Clear();
                Application.RequestStop();
            };

            Add(toolsListView, separatorLine, descriptionLabel, buttonSeparator,
                selectAllButton, clearAllButton, okButton, cancelButton);

            UpdateToolDisplayNames();
            
            if (availableTools.Count > 0)
            {
                OnSelectedItemChanged(new ListViewItemEventArgs(0, null));
            }

            toolsListView.SetFocus();
            UpdateTitle();
        }

        private void OnSelectedItemChanged(ListViewItemEventArgs args)
        {
            if (args.Item >= 0 && args.Item < availableTools.Count)
            {
                var tool = availableTools[args.Item];
                descriptionLabel.Text = $"Description: {tool.Description}";
            }
        }

        private void OnListViewKeyPress(KeyEventEventArgs args)
        {
            if (args.KeyEvent.Key == Key.Space || args.KeyEvent.Key == Key.Enter)
            {
                ToggleSelectedItem();
                args.Handled = true;
            }
            else if (args.KeyEvent.Key == (Key.CtrlMask | Key.A))
            {
                SelectAll();
                args.Handled = true;
            }
            else if (args.KeyEvent.Key == (Key.CtrlMask | Key.D))
            {
                ClearAll();
                args.Handled = true;
            }
        }

        private void ToggleSelectedItem()
        {
            var selectedIndex = toolsListView.SelectedItem;
            if (selectedIndex >= 0 && selectedIndex < availableTools.Count)
            {
                var tool = availableTools[selectedIndex];
                selectedStates[selectedIndex] = !selectedStates[selectedIndex];

                UpdateToolDisplayNames();
                toolsListView.SetSource(toolDisplayNames);
                toolsListView.SelectedItem = selectedIndex;
                UpdateTitle();
            }
        }

        private void OnSelectAllClicked()
        {
            SelectAll();
        }

        private void SelectAll()
        {
            for (int i = 0; i < availableTools.Count; i++)
            {
                selectedStates[i] = true;
            }
            UpdateToolDisplayNames();
            toolsListView.SetSource(toolDisplayNames);
            UpdateTitle();
        }

        private void OnClearAllClicked()
        {
            ClearAll();
        }

        private void ClearAll()
        {
            for (int i = 0; i < availableTools.Count; i++)
            {
                selectedStates[i] = false;
            }
            UpdateToolDisplayNames();
            toolsListView.SetSource(toolDisplayNames);
            UpdateTitle();
        }

        private void UpdateToolDisplayNames()
        {
            for (int i = 0; i < availableTools.Count; i++)
            {
                var tool = availableTools[i];
                var prefix = selectedStates[i] ? "[X] " : "[ ] ";
                toolDisplayNames[i] = $"{prefix}{tool.Name} - {tool.Description}";
            }
        }

        private void UpdateSelectedTools()
        {
            SelectedTools.Clear();
            for (int i = 0; i < availableTools.Count; i++)
            {
                if (selectedStates[i])
                {
                    SelectedTools.Add(availableTools[i].Name);
                }
            }
        }

        private void UpdateTitle()
        {
            var selectedCount = selectedStates.Count(s => s);
            Title = $"Select Tools ({selectedCount} of {availableTools.Count} selected)";
        }
    }
}