using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using FormGenerator.Analyzers.Infopath;
using FormGenerator.Analyzers.InfoPath;
using FormGenerator.Core.Models;
using FormGenerator.Services;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace FormGenerator.Views
{
    /// <summary>
    /// Handles analysis display and reusable views logic with enhanced JSON support
    /// </summary>
    internal class MainWindowAnalysisHandlers
    {
        private readonly MainWindow _mainWindow;
        private readonly ReusableControlGroupAnalyzer _reusableAnalyzer;

        // Reusable Views state
        private ReusableControlGroupAnalyzer.AnalysisResult _currentReusableAnalysis;
        private ReusableControlGroupAnalyzer.ControlGroup _selectedGroup;
        private List<ReusableControlGroup> _currentReusableGroups = new List<ReusableControlGroup>();
        private ReusableControlGroup _selectedReusableGroup;
        private int _autoNameCounter = 1;

        // Tree view editing state
        private ControlDefinition _selectedControl;
        private TreeViewItem _selectedTreeItem;
        private ViewDefinition _selectedView;
        private string _selectedFormName;
        private Window _editWindow;
        private Dictionary<string, TextBox> _propertyEditors = new Dictionary<string, TextBox>();
        private Dictionary<string, InfoPathFormDefinition> _currentFormDefinitions;

        public MainWindowAnalysisHandlers(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            _reusableAnalyzer = new ReusableControlGroupAnalyzer();
        }

        #region Display Analysis Results with Enhanced JSON

        public async Task DisplayCombinedAnalysisResults(Dictionary<string, FormAnalysisResult> allResults)
        {
            await Task.Run(() =>
            {
                _mainWindow.Dispatcher.Invoke(() =>
                {
                    // Clear previous results
                    _mainWindow.SummaryPanel.Items.Clear();
                    _mainWindow.StructureTreeView.Items.Clear();
                    _mainWindow.DataColumnsGrid.ItemsSource = null;
                    _mainWindow.JsonOutput.Text = "";

                    // Calculate combined summary
                    int totalViews = 0;
                    int totalControls = 0;
                    int totalSections = 0;
                    int totalDynamicSections = 0;
                    int totalRepeatingSections = 0;
                    int totalDataColumns = 0;
                    int totalErrors = 0;
                    int totalWarnings = 0;
                    int totalInfos = 0;

                    foreach (var kvp in allResults)
                    {
                        var formDef = kvp.Value.FormDefinition;
                        if (formDef != null)
                        {
                            totalViews += formDef.Views.Count;
                            totalControls += formDef.Metadata.TotalControls;
                            totalSections += formDef.Metadata.TotalSections;
                            totalDynamicSections += formDef.Metadata.DynamicSectionCount;
                            totalRepeatingSections += formDef.Metadata.RepeatingSectionCount;
                            totalDataColumns += formDef.Data.Count;
                        }

                        totalErrors += kvp.Value.Messages.Count(m => m.Severity == MessageSeverity.Error);
                        totalWarnings += kvp.Value.Messages.Count(m => m.Severity == MessageSeverity.Warning);
                        totalInfos += kvp.Value.Messages.Count(m => m.Severity == MessageSeverity.Info);
                    }

                    // Display Summary Cards
                    AddSummaryCard("Forms Analyzed", allResults.Count.ToString(), "📁", "Total forms processed", Brushes.CornflowerBlue);
                    AddSummaryCard("Total Views", totalViews.ToString(), "📄", "All form views/pages", Brushes.DodgerBlue);
                    AddSummaryCard("Total Controls", totalControls.ToString(), "🎛️", "All form controls", Brushes.MediumPurple);
                    AddSummaryCard("Total Sections", totalSections.ToString(), "📦", "All form sections", Brushes.Teal);
                    AddSummaryCard("Dynamic Sections", totalDynamicSections.ToString(), "🔄", "Conditional sections", Brushes.Orange);
                    AddSummaryCard("Repeating Sections", totalRepeatingSections.ToString(), "🔁", "Will be K2 List Views", Brushes.DeepSkyBlue);
                    AddSummaryCard("Data Columns", totalDataColumns.ToString(), "📊", "Unique data fields", Brushes.LimeGreen);

                    // Add message cards if there are any
                    if (totalErrors > 0)
                        AddSummaryCard("Errors", totalErrors.ToString(), "❌", "Total errors found", (Brush)_mainWindow.FindResource("ErrorColor"));
                    if (totalWarnings > 0)
                        AddSummaryCard("Warnings", totalWarnings.ToString(), "⚠️", "Total warnings", (Brush)_mainWindow.FindResource("WarningColor"));
                    if (totalInfos > 0)
                        AddSummaryCard("Info Messages", totalInfos.ToString(), "ℹ️", "Information messages", (Brush)_mainWindow.FindResource("InfoColor"));

                    // Build Enhanced Structure Tree with section-aware controls
                    BuildEnhancedStructureTree(_mainWindow._allFormDefinitions);

                    // Display Enhanced Data Columns
                    DisplayEnhancedDataColumns(_mainWindow._allFormDefinitions);

                    // Display Enhanced JSON with section information
                    DisplayEnhancedJson(_mainWindow._allFormDefinitions);
                });
            });
        }

        /// <summary>
        /// Builds the structure tree using the enhanced JSON format
        /// </summary>
        private void BuildEnhancedStructureTree(Dictionary<string, InfoPathFormDefinition> allForms)
        {
            _mainWindow.StructureTreeView.Items.Clear();

            foreach (var formKvp in allForms)
            {
                var formName = formKvp.Key;
                var formDef = formKvp.Value;

                // Create top-level form node
                var formItem = new TreeViewItem
                {
                    Header = CreateFormHeader(formName),
                    Tag = formDef,
                    IsExpanded = false
                };

                // Add enhanced metadata summary
                var metadataItem = CreateEnhancedMetadataItem(formDef);
                formItem.Items.Add(metadataItem);

                // Process each view with enhanced section information
                foreach (var view in formDef.Views)
                {
                    var viewItem = CreateEnhancedViewItem(view);
                    formItem.Items.Add(viewItem);
                }

                // Add dynamic sections with enhanced info
                if (formDef.DynamicSections.Any())
                {
                    var dynamicItem = CreateEnhancedDynamicSectionsItem(formDef.DynamicSections);
                    formItem.Items.Add(dynamicItem);
                }

                _mainWindow.StructureTreeView.Items.Add(formItem);
            }
        }

        /// <summary>
        /// Displays the enhanced JSON with section information
        /// </summary>
        private void DisplayEnhancedJson(Dictionary<string, InfoPathFormDefinition> allForms)
        {
            var hierarchicalStructure = new Dictionary<string, object>();

            foreach (var formKvp in allForms)
            {
                var formName = Path.GetFileNameWithoutExtension(formKvp.Key);

                // Use the enhanced JSON method from the extension
                hierarchicalStructure[formName] = new
                {
                    FileName = formKvp.Key,
                    FormDefinition = formKvp.Value.ToEnhancedJson()
                };
            }

            var json = JsonConvert.SerializeObject(hierarchicalStructure, Formatting.Indented);
            _mainWindow.JsonOutput.Text = json;
        }

        /// <summary>
        /// Displays enhanced data columns with section context
        /// </summary>
        private void DisplayEnhancedDataColumns(Dictionary<string, InfoPathFormDefinition> allForms)
        {
            var allColumns = new List<EnhancedDataColumn>();

            foreach (var formKvp in allForms)
            {
                var formName = formKvp.Key;
                var formDef = formKvp.Value;

                foreach (var column in formDef.Data)
                {
                    allColumns.Add(new EnhancedDataColumn
                    {
                        FormName = formName,
                        ColumnName = column.ColumnName,
                        Type = column.Type,
                        DisplayName = column.DisplayName,
                        Section = column.RepeatingSection,
                        IsRepeating = column.IsRepeating,
                        IsConditional = column.IsConditional,
                        ConditionalOnField = column.ConditionalOnField,
                        HasConstraints = column.HasConstraints,
                        DefaultValue = column.DefaultValue,
                        ValidValuesCount = column.ValidValues?.Count ?? 0
                    });
                }
            }

            _mainWindow.DataColumnsGrid.ItemsSource = allColumns;
        }

        #endregion

        #region Control Editing Methods - PUBLIC

        public void ShowEditPanel(ControlDefinition control, TreeViewItem treeItem)
        {
            _selectedControl = control;
            _selectedTreeItem = treeItem;

            // Find the view and form this control belongs to
            FindControlContext(control);

            // Create edit window
            _editWindow = new Window
            {
                Title = $"Edit Control: {control.Label ?? control.Name ?? "Unnamed"}",
                Width = 600,
                Height = 700,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = _mainWindow,
                Background = (Brush)_mainWindow.FindResource("BackgroundMedium")
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Create scrollable content area
            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(20)
            };

            var contentPanel = new StackPanel();

            // Basic Properties Section
            AddSectionHeader(contentPanel, "Basic Properties");
            AddEditableProperty(contentPanel, "Name", control.Name ?? "");
            AddEditableProperty(contentPanel, "Label", control.Label ?? "");
            AddEditableProperty(contentPanel, "Type", control.Type ?? "", true); // Read-only
            AddEditableProperty(contentPanel, "Binding", control.Binding ?? "");

            // Control ID Section
            if (control.Properties?.ContainsKey("CtrlId") == true)
            {
                AddEditableProperty(contentPanel, "CtrlId", control.Properties["CtrlId"], true);
            }

            // Position Section
            AddSectionHeader(contentPanel, "Position");
            AddEditableProperty(contentPanel, "GridPosition", control.GridPosition ?? "");
            AddEditableProperty(contentPanel, "DocIndex", control.DocIndex.ToString());

            // Section Information
            if (!string.IsNullOrEmpty(control.ParentSection) || control.IsInRepeatingSection)
            {
                AddSectionHeader(contentPanel, "Section Information");
                AddEditableProperty(contentPanel, "ParentSection", control.ParentSection ?? "", true);
                AddEditableProperty(contentPanel, "SectionType", control.SectionType ?? "", true);

                if (control.IsInRepeatingSection)
                {
                    AddEditableProperty(contentPanel, "RepeatingSectionName", control.RepeatingSectionName ?? "", true);

                    // Add button to remove from repeating section
                    var removeButton = new Button
                    {
                        Content = "➖ Remove from Repeating Section",
                        Style = (Style)_mainWindow.FindResource("ModernButton"),
                        Background = (Brush)_mainWindow.FindResource("WarningColor"),
                        Margin = new Thickness(0, 10, 0, 0)
                    };
                    removeButton.Click += (s, args) => RemoveFromRepeatingSectionWithRefresh(control);
                    contentPanel.Children.Add(removeButton);
                }
            }

            // Dropdown Values Section
            if (control.HasStaticData && control.DataOptions?.Any() == true)
            {
                AddSectionHeader(contentPanel, $"Dropdown Values ({control.DataOptions.Count})");
                AddDropdownEditor(contentPanel, control);
            }

            // Default Value
            if (control.Properties?.ContainsKey("DefaultValue") == true)
            {
                AddSectionHeader(contentPanel, "Default Value");
                AddEditableProperty(contentPanel, "DefaultValue", control.Properties["DefaultValue"]);
            }

            // Additional Properties
            if (control.Properties?.Count > 2)
            {
                AddSectionHeader(contentPanel, "Additional Properties");
                foreach (var prop in control.Properties.Where(p => p.Key != "CtrlId" && p.Key != "DefaultValue"))
                {
                    AddEditableProperty(contentPanel, prop.Key, prop.Value);
                }
            }

            scrollViewer.Content = contentPanel;
            Grid.SetRow(scrollViewer, 0);
            mainGrid.Children.Add(scrollViewer);

            // Button Panel
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(20)
            };

            // Delete Button
            var deleteButton = new Button
            {
                Content = "🗑️ Delete Control",
                Style = (Style)_mainWindow.FindResource("ModernButton"),
                Background = (Brush)_mainWindow.FindResource("ErrorColor"),
                Width = 150,
                Margin = new Thickness(0, 0, 10, 0)
            };
            deleteButton.Click += (s, args) => DeleteControl(control);
            buttonPanel.Children.Add(deleteButton);

            // Save Button
            var saveButton = new Button
            {
                Content = "💾 Save Changes",
                Style = (Style)_mainWindow.FindResource("ModernButton"),
                Background = (Brush)_mainWindow.FindResource("SuccessColor"),
                Width = 150,
                Margin = new Thickness(0, 0, 10, 0)
            };
            saveButton.Click += (s, args) => SaveControlChanges(control);
            buttonPanel.Children.Add(saveButton);

            // Cancel Button
            var cancelButton = new Button
            {
                Content = "Cancel",
                Style = (Style)_mainWindow.FindResource("ModernButton"),
                Width = 100
            };
            cancelButton.Click += (s, args) => _editWindow.Close();
            buttonPanel.Children.Add(cancelButton);

            Grid.SetRow(buttonPanel, 1);
            mainGrid.Children.Add(buttonPanel);

            _editWindow.Content = mainGrid;
            _editWindow.ShowDialog();
        }

        public void DeleteControl(ControlDefinition control)
        {
            var result = MessageBox.Show(
                $"Are you sure you want to delete the control '{control.Label ?? control.Name ?? "Unnamed"}'?\n\nThis action cannot be undone.",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                // Find and remove the control from the view
                if (_selectedView != null)
                {
                    _selectedView.Controls.Remove(control);

                    // Also remove from data columns if exists
                    var formDef = _mainWindow._allFormDefinitions[_selectedFormName];
                    formDef.Data.RemoveAll(d => d.ColumnName == control.Name);

                    // Update metadata
                    formDef.Metadata.TotalControls--;

                    // Refresh displays
                    RefreshTreeView();
                    DisplayEnhancedJson(_mainWindow._allFormDefinitions);

                    _editWindow?.Close();
                    _mainWindow.UpdateStatus($"Control '{control.Label ?? control.Name}' deleted", MessageSeverity.Info);
                }
            }
        }

        public void RemoveFromRepeatingSectionWithRefresh(ControlDefinition control)
        {
            var result = MessageBox.Show(
                $"Remove this control from the repeating section '{control.RepeatingSectionName}'?",
                "Confirm Removal",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                control.IsInRepeatingSection = false;
                control.RepeatingSectionName = null;
                control.RepeatingSectionBinding = null;
                control.SectionType = null;
                control.ParentSection = null;

                // Update the data column if exists
                var formDef = _mainWindow._allFormDefinitions[_selectedFormName];
                var dataColumn = formDef.Data.FirstOrDefault(d => d.ColumnName == control.Name);
                if (dataColumn != null)
                {
                    dataColumn.IsRepeating = false;
                    dataColumn.RepeatingSection = null;
                }

                RefreshTreeView();
                DisplayEnhancedJson(_mainWindow._allFormDefinitions);
                _editWindow?.Close();
                _mainWindow.UpdateStatus("Control removed from repeating section", MessageSeverity.Info);
            }
        }

        #endregion

        #region Add Control Functionality

        public void ShowAddControlDialog()
        {
            // Get selected context (view or section)
            var selectedItem = _mainWindow.StructureTreeView.SelectedItem as TreeViewItem;
            if (selectedItem == null)
            {
                MessageBox.Show("Please select a view or section where you want to add the control.",
                               "No Selection",
                               MessageBoxButton.OK,
                               MessageBoxImage.Information);
                return;
            }

            // Find the view context
            ViewDefinition targetView = null;
            string targetSection = null;
            string formName = null;

            // Traverse up to find the view
            var current = selectedItem;
            while (current != null)
            {
                if (current.Tag is ViewDefinition view)
                {
                    targetView = view;
                    break;
                }
                else if (current.Tag is string sectionName)
                {
                    targetSection = sectionName;
                }
                current = current.Parent as TreeViewItem;
            }

            // Find the form name
            current = selectedItem;
            while (current != null)
            {
                if (current.Tag is InfoPathFormDefinition)
                {
                    // Find form name from tree structure
                    var header = current.Header;
                    if (header is StackPanel panel && panel.Children.Count > 1)
                    {
                        if (panel.Children[1] is TextBlock textBlock)
                        {
                            formName = textBlock.Text;
                            break;
                        }
                    }
                }
                current = current.Parent as TreeViewItem;
            }

            if (targetView == null || formName == null)
            {
                MessageBox.Show("Could not determine the target view. Please select a valid view or section.",
                               "Invalid Selection",
                               MessageBoxButton.OK,
                               MessageBoxImage.Warning);
                return;
            }

            // Create add control dialog
            var addWindow = new Window
            {
                Title = "Add New Control",
                Width = 500,
                Height = 600,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = _mainWindow,
                Background = (Brush)_mainWindow.FindResource("BackgroundMedium")
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var contentPanel = new StackPanel
            {
                Margin = new Thickness(20)
            };

            // Control Type Selection
            contentPanel.Children.Add(new TextBlock
            {
                Text = "Control Type:",
                Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                Margin = new Thickness(0, 0, 0, 5)
            });

            var typeCombo = new ComboBox
            {
                Style = (Style)_mainWindow.FindResource("ModernComboBox"),
                Margin = new Thickness(0, 0, 0, 15)
            };

            var controlTypes = new[]
            {
                "TextField", "RichText", "DropDown", "ComboBox", "CheckBox",
                "RadioButton", "DatePicker", "PeoplePicker", "FileAttachment",
                "Button", "Label", "Hyperlink"
            };

            foreach (var type in controlTypes)
            {
                typeCombo.Items.Add(new ComboBoxItem { Content = type });
            }
            typeCombo.SelectedIndex = 0;
            contentPanel.Children.Add(typeCombo);

            // Control Properties
            var propertyEditors = new Dictionary<string, TextBox>();

            void AddPropertyEditor(string propertyName, string defaultValue = "")
            {
                contentPanel.Children.Add(new TextBlock
                {
                    Text = propertyName + ":",
                    Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                    Margin = new Thickness(0, 10, 0, 5)
                });

                var textBox = new TextBox
                {
                    Text = defaultValue,
                    Style = (Style)_mainWindow.FindResource("ModernTextBox")
                };
                propertyEditors[propertyName] = textBox;
                contentPanel.Children.Add(textBox);
            }

            AddPropertyEditor("Name", "NewControl" + DateTime.Now.Ticks);
            AddPropertyEditor("Label", "New Control");
            AddPropertyEditor("Binding", "");
            AddPropertyEditor("Grid Position", "1A");

            // Target Section Display
            contentPanel.Children.Add(new TextBlock
            {
                Text = $"Target: {(string.IsNullOrEmpty(targetSection) ? targetView.ViewName : targetSection)}",
                Foreground = (Brush)_mainWindow.FindResource("InfoColor"),
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 20, 0, 0)
            });

            var scrollViewer = new ScrollViewer
            {
                Content = contentPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(scrollViewer, 0);
            mainGrid.Children.Add(scrollViewer);

            // Button Panel
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(20)
            };

            var addButton = new Button
            {
                Content = "➕ Add Control",
                Style = (Style)_mainWindow.FindResource("ModernButton"),
                Background = (Brush)_mainWindow.FindResource("SuccessColor"),
                Width = 150,
                Margin = new Thickness(0, 0, 10, 0)
            };
            addButton.Click += (s, args) =>
            {
                // Create new control
                var newControl = new ControlDefinition
                {
                    Name = propertyEditors["Name"].Text,
                    Label = propertyEditors["Label"].Text,
                    Type = (typeCombo.SelectedItem as ComboBoxItem)?.Content.ToString(),
                    Binding = propertyEditors["Binding"].Text,
                    GridPosition = propertyEditors["Grid Position"].Text,
                    DocIndex = targetView.Controls.Count + 1,
                    Properties = new Dictionary<string, string>
                    {
                        ["CtrlId"] = "CTRL_" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper()
                    }
                };

                // Set section information if adding to a section
                if (!string.IsNullOrEmpty(targetSection))
                {
                    var sectionInfo = targetView.Sections.FirstOrDefault(s => s.Name == targetSection);
                    if (sectionInfo != null)
                    {
                        newControl.ParentSection = targetSection;
                        newControl.SectionType = sectionInfo.Type;

                        if (sectionInfo.Type == "repeating")
                        {
                            newControl.IsInRepeatingSection = true;
                            newControl.RepeatingSectionName = targetSection;
                        }
                    }
                }

                // Add to view
                targetView.Controls.Add(newControl);

                // Add to data columns
                var formDef = _mainWindow._allFormDefinitions[formName];
                formDef.Data.Add(new DataColumn
                {
                    ColumnName = newControl.Name,
                    Type = newControl.Type,
                    DisplayName = newControl.Label,
                    IsRepeating = newControl.IsInRepeatingSection,
                    RepeatingSection = newControl.RepeatingSectionName
                });

                // Update metadata
                formDef.Metadata.TotalControls++;

                // Refresh displays
                RefreshTreeView();
                DisplayEnhancedJson(_mainWindow._allFormDefinitions);

                addWindow.Close();
                _mainWindow.UpdateStatus($"Control '{newControl.Label}' added successfully", MessageSeverity.Info);
            };
            buttonPanel.Children.Add(addButton);

            var cancelButton = new Button
            {
                Content = "Cancel",
                Style = (Style)_mainWindow.FindResource("ModernButton"),
                Width = 100
            };
            cancelButton.Click += (s, args) => addWindow.Close();
            buttonPanel.Children.Add(cancelButton);

            Grid.SetRow(buttonPanel, 1);
            mainGrid.Children.Add(buttonPanel);

            addWindow.Content = mainGrid;
            addWindow.ShowDialog();
        }

        #endregion

        #region Move Section to Repeating

        public void ShowMoveSectionDialog()
        {
            var selectedItem = _mainWindow.StructureTreeView.SelectedItem as TreeViewItem;
            if (selectedItem == null || !(selectedItem.Tag is string sectionName))
            {
                MessageBox.Show("Please select a section to convert to repeating.",
                               "No Section Selected",
                               MessageBoxButton.OK,
                               MessageBoxImage.Information);
                return;
            }

            // Find the view and form context
            ViewDefinition view = null;
            string formName = null;
            var current = selectedItem.Parent as TreeViewItem;

            while (current != null)
            {
                if (current.Tag is ViewDefinition v)
                {
                    view = v;
                    break;
                }
                current = current.Parent as TreeViewItem;
            }

            // Find form name
            current = selectedItem;
            while (current != null)
            {
                var header = current.Header;
                if (header is StackPanel panel && panel.Children.Count > 1)
                {
                    if (panel.Children[1] is TextBlock textBlock &&
                        _mainWindow._allFormDefinitions.ContainsKey(textBlock.Text))
                    {
                        formName = textBlock.Text;
                        break;
                    }
                }
                current = current.Parent as TreeViewItem;
            }

            if (view == null || formName == null)
            {
                MessageBox.Show("Could not determine the section context.",
                               "Error",
                               MessageBoxButton.OK,
                               MessageBoxImage.Error);
                return;
            }

            var sectionInfo = view.Sections.FirstOrDefault(s => s.Name == sectionName);
            if (sectionInfo == null)
            {
                MessageBox.Show("Section information not found.",
                               "Error",
                               MessageBoxButton.OK,
                               MessageBoxImage.Error);
                return;
            }

            if (sectionInfo.Type == "repeating")
            {
                MessageBox.Show("This section is already a repeating section.",
                               "Already Repeating",
                               MessageBoxButton.OK,
                               MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Convert section '{sectionName}' to a repeating section?\n\n" +
                "This will:\n" +
                "• Make all controls in this section repeatable\n" +
                "• Create a separate table in SQL generation\n" +
                "• Allow multiple instances of this section\n\n" +
                "Continue?",
                "Convert to Repeating Section",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // Update section type
                sectionInfo.Type = "repeating";

                // Update all controls in this section
                var controlsInSection = view.Controls
                    .Where(c => c.ParentSection == sectionName)
                    .ToList();

                foreach (var control in controlsInSection)
                {
                    control.SectionType = "repeating";
                    control.IsInRepeatingSection = true;
                    control.RepeatingSectionName = sectionName;
                    control.RepeatingSectionBinding = $"my:{sectionName.Replace(" ", "")}";
                }

                // Update data columns
                var formDef = _mainWindow._allFormDefinitions[formName];
                foreach (var control in controlsInSection)
                {
                    var dataColumn = formDef.Data.FirstOrDefault(d => d.ColumnName == control.Name);
                    if (dataColumn != null)
                    {
                        dataColumn.IsRepeating = true;
                        dataColumn.RepeatingSection = sectionName;
                    }
                }

                // Update metadata
                formDef.Metadata.RepeatingSectionCount++;

                // Refresh displays
                RefreshTreeView();
                DisplayEnhancedJson(_mainWindow._allFormDefinitions);

                _mainWindow.UpdateStatus($"Section '{sectionName}' converted to repeating section", MessageSeverity.Info);
            }
        }

        #endregion

        #region Reusable Views Methods - PUBLIC stubs for now

        public async Task RefreshReusableViews()
        {
            // This would be implemented with the actual reusable views logic
            await Task.CompletedTask;
        }

        public async Task SaveReusableViews()
        {
            // This would be implemented with the actual save logic
            await Task.CompletedTask;
        }

        public void MinOccurrences_Changed()
        {
            // This would be implemented with the actual filter logic
        }

        public void GroupBy_Changed()
        {
            // This would be implemented with the actual grouping logic
        }

        public void FilterType_Changed()
        {
            // This would be implemented with the actual filter logic
        }

        public async Task CreateReusableView()
        {
            // This would be implemented with the actual create logic
            await Task.CompletedTask;
        }

        public async Task ExportReusableControls()
        {
            // This would be implemented with the actual export logic
            await Task.CompletedTask;
        }

        public void GroupNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // This would be implemented with the actual text change logic
        }

        #endregion

        #region Helper Methods - Continue in next part...

        private void AddSectionHeader(StackPanel panel, string title)
        {
            var header = new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)_mainWindow.FindResource("AccentColor"),
                Margin = new Thickness(0, 10, 0, 5)
            };
            panel.Children.Add(header);

            var separator = new Border
            {
                Height = 1,
                Background = (Brush)_mainWindow.FindResource("BorderColor"),
                Margin = new Thickness(0, 0, 0, 10)
            };
            panel.Children.Add(separator);
        }

       

        #endregion
    

    // Add these methods to the MainWindowAnalysisHandlers class (Part 2):

        #region Helper Methods for Editing

        private void AddEditableProperty(StackPanel panel, string propertyName, string value, bool isReadOnly = false)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var label = new TextBlock
            {
                Text = propertyName + ":",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                Margin = new Thickness(0, 5, 0, 0)
            };
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);

            var textBox = new TextBox
            {
                Text = value,
                Style = (Style)_mainWindow.FindResource("ModernTextBox"),
                IsReadOnly = isReadOnly,
                Background = isReadOnly
                    ? (Brush)_mainWindow.FindResource("BackgroundLighter")
                    : (Brush)_mainWindow.FindResource("BackgroundLight"),
                Margin = new Thickness(0, 5, 0, 0)
            };
            Grid.SetColumn(textBox, 1);
            grid.Children.Add(textBox);

            _propertyEditors[propertyName] = textBox;
            panel.Children.Add(grid);
        }

        private void AddDropdownEditor(StackPanel panel, ControlDefinition control)
        {
            var listBox = new ListBox
            {
                Background = (Brush)_mainWindow.FindResource("BackgroundLight"),
                BorderBrush = (Brush)_mainWindow.FindResource("BorderColor"),
                MaxHeight = 200
            };

            foreach (var option in control.DataOptions.OrderBy(o => o.Order))
            {
                var item = new ListBoxItem
                {
                    Content = $"{option.DisplayText} (Value: {option.Value}){(option.IsDefault ? " ⭐" : "")}",
                    Tag = option
                };
                listBox.Items.Add(item);
            }

            panel.Children.Add(listBox);
        }

        private void SaveControlChanges(ControlDefinition control)
        {
            // Update control properties from editors
            if (_propertyEditors.ContainsKey("Name"))
                control.Name = _propertyEditors["Name"].Text;

            if (_propertyEditors.ContainsKey("Label"))
                control.Label = _propertyEditors["Label"].Text;

            if (_propertyEditors.ContainsKey("Binding"))
                control.Binding = _propertyEditors["Binding"].Text;

            if (_propertyEditors.ContainsKey("GridPosition"))
                control.GridPosition = _propertyEditors["GridPosition"].Text;

            if (_propertyEditors.ContainsKey("DocIndex") && int.TryParse(_propertyEditors["DocIndex"].Text, out int docIndex))
                control.DocIndex = docIndex;

            if (_propertyEditors.ContainsKey("DefaultValue"))
            {
                if (control.Properties == null)
                    control.Properties = new Dictionary<string, string>();
                control.Properties["DefaultValue"] = _propertyEditors["DefaultValue"].Text;
            }

            // Refresh the tree view
            RefreshTreeView();

            // Refresh JSON
            DisplayEnhancedJson(_mainWindow._allFormDefinitions);

            _editWindow.Close();
            _mainWindow.UpdateStatus("Control changes saved", MessageSeverity.Info);
        }

        private void FindControlContext(ControlDefinition control)
        {
            foreach (var formKvp in _mainWindow._allFormDefinitions)
            {
                foreach (var view in formKvp.Value.Views)
                {
                    if (view.Controls.Contains(control))
                    {
                        _selectedView = view;
                        _selectedFormName = formKvp.Key;
                        return;
                    }
                }
            }
        }

        private void RefreshTreeView()
        {
            // Save expansion state
            var expandedItems = new List<string>();
            SaveExpansionState(_mainWindow.StructureTreeView.Items, expandedItems);

            // Rebuild tree
            BuildEnhancedStructureTree(_mainWindow._allFormDefinitions);

            // Restore expansion state
            RestoreExpansionState(_mainWindow.StructureTreeView.Items, expandedItems);
        }

        private void SaveExpansionState(ItemCollection items, List<string> expandedItems)
        {
            foreach (TreeViewItem item in items)
            {
                if (item.IsExpanded)
                {
                    // Save a unique identifier for this item
                    if (item.Tag != null)
                        expandedItems.Add(item.Tag.ToString());
                }
                SaveExpansionState(item.Items, expandedItems);
            }
        }

        private void RestoreExpansionState(ItemCollection items, List<string> expandedItems)
        {
            foreach (TreeViewItem item in items)
            {
                if (item.Tag != null && expandedItems.Contains(item.Tag.ToString()))
                {
                    item.IsExpanded = true;
                }
                RestoreExpansionState(item.Items, expandedItems);
            }
        }

        #endregion

        #region Tree View Creation Methods

        private TreeViewItem CreateEnhancedMetadataItem(InfoPathFormDefinition formDef)
        {
            var metadataItem = new TreeViewItem
            {
                Header = "📊 Form Summary",
                Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                FontSize = 11,
                IsExpanded = false
            };

            metadataItem.Items.Add(CreateInfoItem($"Views: {formDef.Views.Count}"));
            metadataItem.Items.Add(CreateInfoItem($"Total Controls: {formDef.Metadata.TotalControls}"));
            metadataItem.Items.Add(CreateInfoItem($"Data Columns: {formDef.Data.Count}"));

            return metadataItem;
        }

        private TreeViewItem CreateEnhancedViewItem(ViewDefinition view)
        {
            var viewItem = new TreeViewItem
            {
                Header = $"📄 {view.ViewName}",
                Tag = view,
                IsExpanded = false
            };

            // Group controls by section
            var controlsBySection = GroupControlsBySection(view.Controls);

            // Add controls without sections first
            if (controlsBySection.ContainsKey(""))
            {
                var rootControls = controlsBySection[""];
                foreach (var control in rootControls.OrderBy(c => c.DocIndex))
                {
                    if (!control.IsMergedIntoParent)
                    {
                        viewItem.Items.Add(CreateEnhancedControlItem(control));
                    }
                }
            }

            // Add sectioned controls
            foreach (var section in controlsBySection.Where(s => s.Key != "").OrderBy(s => s.Key))
            {
                var sectionItem = CreateEnhancedSectionItem(section.Key, section.Value, view);
                viewItem.Items.Add(sectionItem);
            }

            return viewItem;
        }

        private TreeViewItem CreateEnhancedControlItem(ControlDefinition control)
        {
            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };

            // Control icon
            headerPanel.Children.Add(new TextBlock
            {
                Text = GetControlIcon(control.Type) + " ",
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 14
            });

            // Control label/name
            var displayName = !string.IsNullOrEmpty(control.Label) ? control.Label :
                             !string.IsNullOrEmpty(control.Name) ? control.Name : "Unnamed";
            headerPanel.Children.Add(new TextBlock
            {
                Text = displayName,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });

            // Control type badge
            var typeBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(50, 100, 100, 100)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(0, 0, 5, 0)
            };
            typeBadge.Child = new TextBlock
            {
                Text = control.Type,
                Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            headerPanel.Children.Add(typeBadge);

            // CtrlId badge (if exists)
            if (control.Properties != null && control.Properties.ContainsKey("CtrlId"))
            {
                var ctrlIdBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(50, 156, 39, 176)), // Purple tint
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(5, 1, 5, 1),
                    Margin = new Thickness(0, 0, 5, 0)
                };
                ctrlIdBadge.Child = new TextBlock
                {
                    Text = $"ID: {control.Properties["CtrlId"]}",
                    Foreground = new SolidColorBrush(Color.FromRgb(156, 39, 176)), // Purple
                    FontSize = 10,
                    FontFamily = new FontFamily("Consolas"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                headerPanel.Children.Add(ctrlIdBadge);
            }

            // Grid position badge
            if (!string.IsNullOrEmpty(control.GridPosition))
            {
                var gridBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(50, 76, 175, 80)), // Green tint
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(5, 1, 5, 1),
                    Margin = new Thickness(0, 0, 5, 0)
                };
                gridBadge.Child = new TextBlock
                {
                    Text = $"Grid: {control.GridPosition}",
                    Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80)), // Green
                    FontSize = 10,
                    FontFamily = new FontFamily("Consolas"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                headerPanel.Children.Add(gridBadge);
            }

            // Section indicator (if in a section)
            if (!string.IsNullOrEmpty(control.ParentSection) || control.IsInRepeatingSection)
            {
                var sectionText = control.ParentSection ?? control.RepeatingSectionName;
                var sectionBadge = new Border
                {
                    Background = control.IsInRepeatingSection
                        ? new SolidColorBrush(Color.FromArgb(30, 3, 169, 244))  // Blue tint for repeating
                        : new SolidColorBrush(Color.FromArgb(30, 255, 152, 0)), // Orange tint for regular
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(5, 1, 5, 1),
                    Margin = new Thickness(0, 0, 5, 0)
                };
                sectionBadge.Child = new TextBlock
                {
                    Text = $"📍 {sectionText}",
                    Foreground = control.IsInRepeatingSection
                        ? (Brush)_mainWindow.FindResource("InfoColor")
                        : (Brush)_mainWindow.FindResource("WarningColor"),
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center
                };
                headerPanel.Children.Add(sectionBadge);
            }

            // Dropdown indicator with count
            if (control.HasStaticData && control.DataOptions != null && control.DataOptions.Any())
            {
                var dropdownBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(30, 139, 195, 74)), // Light green
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(5, 1, 5, 1),
                    Margin = new Thickness(0, 0, 5, 0)
                };

                var dropdownPanel = new StackPanel { Orientation = Orientation.Horizontal };
                dropdownPanel.Children.Add(new TextBlock
                {
                    Text = "📋",
                    Foreground = (Brush)_mainWindow.FindResource("SuccessColor"),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center
                });
                dropdownPanel.Children.Add(new TextBlock
                {
                    Text = $" ({control.DataOptions.Count})",
                    Foreground = (Brush)_mainWindow.FindResource("SuccessColor"),
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center
                });

                dropdownBadge.Child = dropdownPanel;
                headerPanel.Children.Add(dropdownBadge);
            }

            // Default value indicator
            if (control.Properties != null && control.Properties.ContainsKey("DefaultValue") &&
                !string.IsNullOrEmpty(control.Properties["DefaultValue"]))
            {
                headerPanel.Children.Add(new TextBlock
                {
                    Text = " 🎯",
                    Foreground = (Brush)_mainWindow.FindResource("SuccessColor"),
                    ToolTip = $"Has default value: {control.Properties["DefaultValue"]}",
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            // Edit button
            var editButton = new Button
            {
                Content = "✏️",
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Padding = new Thickness(5, 0, 5, 0),
                ToolTip = "Edit control properties",
                Tag = control,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 0, 0),
                FontSize = 12
            };
            editButton.Click += (s, e) => ShowEditPanel(control, null);
            headerPanel.Children.Add(editButton);

            var item = new TreeViewItem
            {
                Header = headerPanel,
                Tag = control
            };

            // Add detailed properties as child items
            AddControlPropertiesWithDetails(item, control);

            return item;
        }

        private void AddControlPropertiesWithDetails(TreeViewItem item, ControlDefinition control)
        {
            // CtrlId (show first if exists)
            if (control.Properties != null && control.Properties.ContainsKey("CtrlId"))
            {
                item.Items.Add(CreatePropertyItem("Control ID", control.Properties["CtrlId"], "🔑"));
            }

            // Name (if different from label)
            if (!string.IsNullOrEmpty(control.Name) && control.Name != control.Label)
            {
                item.Items.Add(CreatePropertyItem("Name", control.Name, "📛"));
            }

            // Binding
            if (!string.IsNullOrEmpty(control.Binding))
            {
                item.Items.Add(CreatePropertyItem("Binding", control.Binding, "🔗"));
            }

            // Grid Position
            if (!string.IsNullOrEmpty(control.GridPosition))
            {
                item.Items.Add(CreatePropertyItem("Grid Position", control.GridPosition, "📍"));
            }

            // Document Index
            if (control.DocIndex > 0)
            {
                item.Items.Add(CreatePropertyItem("Doc Index", control.DocIndex.ToString(), "📑"));
            }

            // Section information
            if (!string.IsNullOrEmpty(control.ParentSection))
            {
                item.Items.Add(CreatePropertyItem("Parent Section", control.ParentSection, "📦"));
            }

            if (control.IsInRepeatingSection && !string.IsNullOrEmpty(control.RepeatingSectionName))
            {
                item.Items.Add(CreatePropertyItem("Repeating Section", control.RepeatingSectionName, "🔁"));
            }

            // Dropdown values
            if (control.HasStaticData && control.DataOptions != null && control.DataOptions.Any())
            {
                var dropdownItem = new TreeViewItem
                {
                    Header = CreatePropertyHeader($"📋 Dropdown Values ({control.DataOptions.Count})", (Brush)_mainWindow.FindResource("SuccessColor")),
                    FontSize = 11,
                    IsExpanded = false
                };

                foreach (var option in control.DataOptions.OrderBy(o => o.Order))
                {
                    var optionText = option.DisplayText;
                    if (option.IsDefault)
                        optionText += " ⭐ (default)";

                    var optionItem = new TreeViewItem
                    {
                        Header = $"  • {optionText}",
                        Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                        FontSize = 10,
                        ToolTip = $"Value: {option.Value}"
                    };
                    dropdownItem.Items.Add(optionItem);
                }

                item.Items.Add(dropdownItem);
            }

            // Default value
            if (control.Properties != null && control.Properties.ContainsKey("DefaultValue") &&
                !string.IsNullOrEmpty(control.Properties["DefaultValue"]))
            {
                item.Items.Add(CreatePropertyItem("Default Value", control.Properties["DefaultValue"], "🎯"));
            }

            // Column and Row Span
            if (control.ColumnSpan > 1)
            {
                item.Items.Add(CreatePropertyItem("Column Span", control.ColumnSpan.ToString(), "↔️"));
            }

            if (control.RowSpan > 1)
            {
                item.Items.Add(CreatePropertyItem("Row Span", control.RowSpan.ToString(), "↕️"));
            }

            // Other properties (if any)
            if (control.Properties != null && control.Properties.Count > 2)
            {
                var otherProps = control.Properties.Where(p =>
                    p.Key != "CtrlId" &&
                    p.Key != "DefaultValue" &&
                    !string.IsNullOrEmpty(p.Value)).ToList();

                if (otherProps.Any())
                {
                    var otherPropsItem = new TreeViewItem
                    {
                        Header = CreatePropertyHeader($"⚙️ Additional Properties ({otherProps.Count})", (Brush)_mainWindow.FindResource("TextDim")),
                        FontSize = 11,
                        IsExpanded = false
                    };

                    foreach (var prop in otherProps)
                    {
                        otherPropsItem.Items.Add(new TreeViewItem
                        {
                            Header = $"  {prop.Key}: {prop.Value}",
                            Foreground = (Brush)_mainWindow.FindResource("TextDim"),
                            FontSize = 10
                        });
                    }

                    item.Items.Add(otherPropsItem);
                }
            }
        }

        private TreeViewItem CreatePropertyItem(string propertyName, string value, string icon)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };

            panel.Children.Add(new TextBlock
            {
                Text = $"{icon} ",
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            });

            panel.Children.Add(new TextBlock
            {
                Text = $"{propertyName}: ",
                Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            });

            panel.Children.Add(new TextBlock
            {
                Text = value,
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary"),
                FontSize = 11,
                FontFamily = new FontFamily("Consolas"),
                VerticalAlignment = VerticalAlignment.Center
            });

            return new TreeViewItem
            {
                Header = panel,
                Foreground = (Brush)_mainWindow.FindResource("TextDim"),
                FontSize = 11
            };
        }

        // Helper method to create styled headers
        private object CreatePropertyHeader(string text, Brush foreground)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = foreground,
                FontWeight = FontWeights.Medium
            };
        }

        private TreeViewItem CreateEnhancedSectionItem(string sectionName, List<ControlDefinition> controls, ViewDefinition view)
        {
            var sectionInfo = view.Sections.FirstOrDefault(s => s.Name == sectionName);
            string sectionType = sectionInfo?.Type ?? "section";

            var sectionItem = new TreeViewItem
            {
                Header = $"{GetSectionIcon(sectionType)} {sectionName}",
                IsExpanded = false,
                Tag = sectionName
            };

            foreach (var control in controls.OrderBy(c => c.DocIndex))
            {
                if (!control.IsMergedIntoParent)
                {
                    sectionItem.Items.Add(CreateEnhancedControlItem(control));
                }
            }

            return sectionItem;
        }

        private TreeViewItem CreateEnhancedDynamicSectionsItem(List<DynamicSection> dynamicSections)
        {
            var dynamicItem = new TreeViewItem
            {
                Header = $"🔄 Dynamic Sections ({dynamicSections.Count})",
                IsExpanded = false,
                Foreground = (Brush)_mainWindow.FindResource("AccentColor")
            };

            foreach (var dynSection in dynamicSections)
            {
                var sectionName = !string.IsNullOrEmpty(dynSection.Caption) ? dynSection.Caption : dynSection.Mode;
                var sectionItem = new TreeViewItem
                {
                    Header = $"🔀 {sectionName}",
                    Tag = dynSection
                };
                dynamicItem.Items.Add(sectionItem);
            }

            return dynamicItem;
        }

        private TreeViewItem CreateInfoItem(string text, int fontSize = 11)
        {
            return new TreeViewItem
            {
                Header = text,
                Foreground = (Brush)_mainWindow.FindResource("TextDim"),
                FontSize = fontSize
            };
        }

        #endregion

        #region Helper Methods

        private Dictionary<string, List<ControlDefinition>> GroupControlsBySection(List<ControlDefinition> controls)
        {
            var grouped = new Dictionary<string, List<ControlDefinition>>();

            foreach (var control in controls)
            {
                string sectionKey = "";

                if (!string.IsNullOrEmpty(control.ParentSection))
                {
                    sectionKey = control.ParentSection;
                }
                else if (control.IsInRepeatingSection && !string.IsNullOrEmpty(control.RepeatingSectionName))
                {
                    sectionKey = control.RepeatingSectionName;
                }

                if (!grouped.ContainsKey(sectionKey))
                {
                    grouped[sectionKey] = new List<ControlDefinition>();
                }

                grouped[sectionKey].Add(control);
            }

            return grouped;
        }

        private void AddSummaryCard(string title, string value, string icon, string description, Brush accentColor = null)
        {
            var card = new Border
            {
                Style = (Style)_mainWindow.FindResource("SummaryCardStyle"),
                Width = 180,
                Height = 120
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var iconText = new TextBlock
            {
                Text = icon,
                FontSize = 28,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 5),
                Foreground = accentColor ?? (Brush)_mainWindow.FindResource("AccentColor")
            };
            Grid.SetRow(iconText, 0);
            grid.Children.Add(iconText);

            var valueText = new TextBlock
            {
                Text = value,
                FontSize = 32,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 5)
            };
            Grid.SetRow(valueText, 1);
            grid.Children.Add(valueText);

            var titleText = new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = FontWeights.Medium,
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary"),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center
            };
            Grid.SetRow(titleText, 2);
            grid.Children.Add(titleText);

            var descriptionText = new TextBlock
            {
                Text = description,
                FontSize = 10,
                Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(5, 0, 5, 5)
            };
            Grid.SetRow(descriptionText, 3);
            grid.Children.Add(descriptionText);

            card.Child = grid;
            _mainWindow.SummaryPanel.Items.Add(card);
        }

        private object CreateFormHeader(string formName)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };

            panel.Children.Add(new TextBlock
            {
                Text = "📁",
                FontSize = 16,
                Margin = new Thickness(0, 0, 5, 0),
                Foreground = (Brush)_mainWindow.FindResource("AccentColor")
            });

            panel.Children.Add(new TextBlock
            {
                Text = formName,
                FontSize = 14,
                FontWeight = FontWeights.Medium,
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary")
            });

            return panel;
        }

        private string GetControlIcon(string controlType)
        {
            return controlType?.ToLower() switch
            {
                "textfield" => "📝",
                "richtext" => "📄",
                "dropdown" => "📋",
                "combobox" => "🔽",
                "checkbox" => "☑️",
                "datepicker" => "📅",
                "peoplepicker" => "👤",
                "fileattachment" => "📎",
                "button" => "🔘",
                "repeatingtable" => "🔁",
                "repeatingsection" => "🔂",
                "label" => "🏷️",
                "radiobutton" => "⭕",
                "hyperlink" => "🔗",
                "inlinepicture" => "🖼️",
                "signatureline" => "✍️",
                _ => "🎛️"
            };
        }

        private string GetSectionIcon(string sectionType)
        {
            return sectionType?.ToLower() switch
            {
                "repeating" => "🔁",
                "optional" => "❓",
                "dynamic" => "🔄",
                _ => "📦"
            };
        }
    }

        #endregion

        #region Enhanced Model Classes

    public class EnhancedDataColumn
    {
        public string FormName { get; set; }
        public string ColumnName { get; set; }
        public string Type { get; set; }
        public string DisplayName { get; set; }
        public string Section { get; set; }
        public bool IsRepeating { get; set; }
        public bool IsConditional { get; set; }
        public string ConditionalOnField { get; set; }
        public bool HasConstraints { get; set; }
        public string DefaultValue { get; set; }
        public int ValidValuesCount { get; set; }
    }

    public class ReusableControlGroup
    {
        public string GroupKey { get; set; }
        public string CustomName { get; set; }
        public string ControlType { get; set; }
        public string Label { get; set; }
        public string Name { get; set; }
        public int OccurrenceCount { get; set; }
        public List<string> AppearingInForms { get; set; } = new List<string>();
    }

    #endregion
}