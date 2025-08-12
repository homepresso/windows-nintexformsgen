using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
        private Grid _propertiesPanel;
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
        /// Creates an enhanced metadata item with section summary
        /// </summary>
        private TreeViewItem CreateEnhancedMetadataItem(InfoPathFormDefinition formDef)
        {
            var metadataItem = new TreeViewItem
            {
                Header = "📊 Form Summary",
                Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                FontSize = 11,
                IsExpanded = false
            };

            // Basic counts
            metadataItem.Items.Add(CreateInfoItem($"Views: {formDef.Views.Count}"));
            metadataItem.Items.Add(CreateInfoItem($"Total Controls: {formDef.Metadata.TotalControls}"));
            metadataItem.Items.Add(CreateInfoItem($"Data Columns: {formDef.Data.Count}"));

            // Section information with details
            if (formDef.Metadata.RepeatingSectionCount > 0)
            {
                var repeatingSectionNames = GetRepeatingSectionNames(formDef);
                var repeatingSectionItem = new TreeViewItem
                {
                    Header = $"🔁 Repeating Sections: {formDef.Metadata.RepeatingSectionCount}",
                    Foreground = (Brush)_mainWindow.FindResource("InfoColor"),
                    FontSize = 11,
                    IsExpanded = false
                };

                foreach (var sectionName in repeatingSectionNames)
                {
                    repeatingSectionItem.Items.Add(new TreeViewItem
                    {
                        Header = $"  • {sectionName}",
                        Foreground = (Brush)_mainWindow.FindResource("TextDim"),
                        FontSize = 10
                    });
                }

                metadataItem.Items.Add(repeatingSectionItem);
            }

            if (formDef.Metadata.DynamicSectionCount > 0)
            {
                metadataItem.Items.Add(new TreeViewItem
                {
                    Header = $"🔄 Dynamic Sections: {formDef.Metadata.DynamicSectionCount} (conditional visibility)",
                    Foreground = (Brush)_mainWindow.FindResource("WarningColor"),
                    FontSize = 11
                });
            }

            // Conditional fields
            if (formDef.Metadata.ConditionalFields.Any())
            {
                var conditionalItem = new TreeViewItem
                {
                    Header = $"⚡ Conditional Fields: {formDef.Metadata.ConditionalFields.Count}",
                    Foreground = (Brush)_mainWindow.FindResource("AccentColor"),
                    FontSize = 11,
                    IsExpanded = false
                };

                foreach (var field in formDef.Metadata.ConditionalFields)
                {
                    conditionalItem.Items.Add(new TreeViewItem
                    {
                        Header = $"  • {field}",
                        Foreground = (Brush)_mainWindow.FindResource("TextDim"),
                        FontSize = 10
                    });
                }

                metadataItem.Items.Add(conditionalItem);
            }

            return metadataItem;
        }

        /// <summary>
        /// Creates an enhanced view item with proper section grouping
        /// </summary>
        private TreeViewItem CreateEnhancedViewItem(ViewDefinition view)
        {
            var viewItem = new TreeViewItem
            {
                Header = $"📄 {view.ViewName}",
                Tag = view,
                IsExpanded = false
            };

            // Add context menu to view for adding controls
            var viewContextMenu = new ContextMenu();
            var addControlToView = new MenuItem
            {
                Header = "Add Control...",
                Icon = new TextBlock { Text = "➕" }
            };
            addControlToView.Click += (s, e) => AddNewControl(view);
            viewContextMenu.Items.Add(addControlToView);
            viewItem.ContextMenu = viewContextMenu;

            // Build hierarchical structure of sections and controls
            var rootControls = new List<ControlDefinition>();
            var sectionHierarchy = BuildSectionHierarchy(view);

            // Add controls without sections first
            foreach (var control in view.Controls.Where(c => !c.IsMergedIntoParent).OrderBy(c => c.DocIndex))
            {
                // Skip controls that belong to sections - they'll be added within their sections
                if (string.IsNullOrEmpty(control.ParentSection) &&
                    !control.IsInRepeatingSection &&
                    control.Type != "RepeatingTable" &&
                    control.Type != "RepeatingSection")
                {
                    viewItem.Items.Add(CreateEnhancedControlItem(control));
                }
            }

            // Add sections with their hierarchical structure
            foreach (var topLevelSection in sectionHierarchy)
            {
                viewItem.Items.Add(CreateSectionTreeItem(topLevelSection, view));
            }

            // Add view statistics
            var statsItem = CreateViewStatisticsItem(view);
            viewItem.Items.Add(statsItem);

            return viewItem;
        }

        private List<SectionNode> BuildSectionHierarchy(ViewDefinition view)
        {
            var sectionNodes = new Dictionary<string, SectionNode>();
            var rootSections = new List<SectionNode>();

            // First, create nodes for all sections (including repeating tables/sections from controls)
            foreach (var section in view.Sections)
            {
                if (!sectionNodes.ContainsKey(section.Name))
                {
                    sectionNodes[section.Name] = new SectionNode
                    {
                        Name = section.Name,
                        Type = section.Type,
                        CtrlId = section.CtrlId,
                        Controls = new List<ControlDefinition>(),
                        ChildSections = new List<SectionNode>()
                    };
                }
            }

            // Add repeating tables as sections
            foreach (var control in view.Controls.Where(c => c.Type == "RepeatingTable"))
            {
                var sectionName = control.Label ?? control.Name;
                if (!string.IsNullOrEmpty(sectionName) && !sectionNodes.ContainsKey(sectionName))
                {
                    sectionNodes[sectionName] = new SectionNode
                    {
                        Name = sectionName,
                        Type = "repeating",
                        IsRepeatingTable = true,
                        Controls = new List<ControlDefinition>(),
                        ChildSections = new List<SectionNode>()
                    };
                }
            }

            // Now assign controls to their sections and build parent-child relationships
            foreach (var control in view.Controls.Where(c => !c.IsMergedIntoParent))
            {
                string sectionKey = null;

                // Determine which section this control belongs to
                if (!string.IsNullOrEmpty(control.ParentSection))
                {
                    sectionKey = control.ParentSection;
                }
                else if (control.IsInRepeatingSection && !string.IsNullOrEmpty(control.RepeatingSectionName))
                {
                    sectionKey = control.RepeatingSectionName;
                }

                if (!string.IsNullOrEmpty(sectionKey) && sectionNodes.ContainsKey(sectionKey))
                {
                    sectionNodes[sectionKey].Controls.Add(control);
                }
            }

            // Build parent-child relationships based on control nesting
            foreach (var sectionNode in sectionNodes.Values)
            {
                // Check if this section's controls indicate it's nested within another section
                var firstControl = sectionNode.Controls.FirstOrDefault();
                if (firstControl != null)
                {
                    // Check if the controls in this section have a parent repeating section
                    if (firstControl.Properties != null &&
                        firstControl.Properties.ContainsKey("ParentRepeatingSections"))
                    {
                        var parentSections = firstControl.Properties["ParentRepeatingSections"].Split('|');
                        if (parentSections.Length > 0)
                        {
                            var immediateParent = parentSections[0];
                            if (sectionNodes.ContainsKey(immediateParent) && immediateParent != sectionNode.Name)
                            {
                                sectionNodes[immediateParent].ChildSections.Add(sectionNode);
                                sectionNode.ParentSection = immediateParent;
                            }
                        }
                    }
                }
            }

            // Identify root sections (those without parents)
            foreach (var sectionNode in sectionNodes.Values)
            {
                if (string.IsNullOrEmpty(sectionNode.ParentSection))
                {
                    rootSections.Add(sectionNode);
                }
            }

            return rootSections;
        }

        private TreeViewItem CreateSectionTreeItem(SectionNode sectionNode, ViewDefinition view)
        {
            var sectionItem = new TreeViewItem
            {
                Header = CreateEnhancedSectionHeader(
                    sectionNode.Name,
                    sectionNode.Type,
                    sectionNode.Controls.Count + sectionNode.ChildSections.Sum(cs => CountAllControls(cs))
                ),
                IsExpanded = false,
                Tag = sectionNode.Name
            };

            // Apply color based on section type
            ApplySectionColor(sectionItem, sectionNode.Type);

            // Add context menu for section operations
            var sectionInfo = view.Sections.FirstOrDefault(s => s.Name == sectionNode.Name);
            if (sectionInfo != null)
            {
                AddSectionContextMenu(sectionItem, sectionInfo, view);

                // Add section metadata
                sectionItem.Items.Add(CreateInfoItem($"Type: {sectionNode.Type}", 10));
                if (!string.IsNullOrEmpty(sectionInfo.CtrlId))
                {
                    sectionItem.Items.Add(CreateInfoItem($"ID: {sectionInfo.CtrlId}", 10));
                }
            }

            // If this is a repeating table, add a special indicator
            if (sectionNode.IsRepeatingTable)
            {
                sectionItem.Items.Add(CreateInfoItem($"📊 Repeating Table", 10));
            }

            // Add child sections first (nested sections)
            foreach (var childSection in sectionNode.ChildSections)
            {
                sectionItem.Items.Add(CreateSectionTreeItem(childSection, view));
            }

            // Then add controls in this section (but not in child sections)
            foreach (var control in sectionNode.Controls.OrderBy(c => c.DocIndex))
            {
                // Skip controls that are in child sections
                bool isInChildSection = false;
                foreach (var childSection in sectionNode.ChildSections)
                {
                    if (IsControlInSection(control, childSection))
                    {
                        isInChildSection = true;
                        break;
                    }
                }

                if (!isInChildSection && !control.IsMergedIntoParent)
                {
                    sectionItem.Items.Add(CreateEnhancedControlItem(control));
                }
            }

            return sectionItem;
        }

        private class SectionNode
        {
            public string Name { get; set; }
            public string Type { get; set; }
            public string CtrlId { get; set; }
            public string ParentSection { get; set; }
            public bool IsRepeatingTable { get; set; }
            public List<ControlDefinition> Controls { get; set; }
            public List<SectionNode> ChildSections { get; set; }
        }

        private int CountAllControls(SectionNode section)
        {
            int count = section.Controls.Count(c => !c.IsMergedIntoParent);
            foreach (var childSection in section.ChildSections)
            {
                count += CountAllControls(childSection);
            }
            return count;
        }

        /// <summary>
        /// Checks if a control belongs to a specific section or its children
        /// </summary>
        private bool IsControlInSection(ControlDefinition control, SectionNode section)
        {
            // Check direct membership
            if (control.ParentSection == section.Name ||
                (control.IsInRepeatingSection && control.RepeatingSectionName == section.Name))
            {
                return true;
            }

            // Check child sections
            foreach (var childSection in section.ChildSections)
            {
                if (IsControlInSection(control, childSection))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Creates an enhanced control item with all properties
        /// </summary>
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
            AddControlTypeBadge(headerPanel, control.Type);

            // Section indicator (if in a section)
            if (!string.IsNullOrEmpty(control.ParentSection) || control.IsInRepeatingSection)
            {
                AddSectionIndicator(headerPanel, control);
            }

            // Grid position
            if (!string.IsNullOrEmpty(control.GridPosition))
            {
                headerPanel.Children.Add(new TextBlock
                {
                    Text = $"[{control.GridPosition}]",
                    Foreground = (Brush)_mainWindow.FindResource("TextDim"),
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(5, 0, 0, 0)
                });
            }

            // Dropdown indicator with count
            if (control.HasStaticData && control.DataOptions != null && control.DataOptions.Any())
            {
                AddDropdownIndicator(headerPanel, control);
            }

            // Default value indicator
            if (control.Properties.ContainsKey("DefaultValue") && !string.IsNullOrEmpty(control.Properties["DefaultValue"]))
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
            AddEditButton(headerPanel, control);

            var item = new TreeViewItem
            {
                Header = headerPanel,
                Tag = control
            };

            // Add control properties
            AddControlProperties(item, control);

            // Add context menu
            AddControlContextMenu(item, control);

            return item;
        }

        /// <summary>
        /// Creates enhanced dynamic sections item
        /// </summary>
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

                // Add condition info
                if (!string.IsNullOrEmpty(dynSection.ConditionField))
                {
                    sectionItem.Items.Add(CreateInfoItem($"Condition Field: {dynSection.ConditionField}", 11));
                }

                if (!string.IsNullOrEmpty(dynSection.ConditionValue))
                {
                    sectionItem.Items.Add(CreateInfoItem($"Condition Value: {dynSection.ConditionValue}", 11));
                }

                if (!string.IsNullOrEmpty(dynSection.Condition))
                {
                    sectionItem.Items.Add(CreateInfoItem($"Full Condition: {dynSection.Condition}", 10));
                }

                // Add visibility status
                sectionItem.Items.Add(CreateInfoItem($"Visible by Default: {dynSection.IsVisible}", 11));

                // Add affected controls
                if (dynSection.Controls != null && dynSection.Controls.Any())
                {
                    var controlsItem = new TreeViewItem
                    {
                        Header = $"Affected Controls ({dynSection.Controls.Count})",
                        Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                        FontSize = 11,
                        IsExpanded = false
                    };

                    foreach (var ctrlId in dynSection.Controls)
                    {
                        controlsItem.Items.Add(CreateInfoItem($"  • {ctrlId}", 10));
                    }

                    sectionItem.Items.Add(controlsItem);
                }

                dynamicItem.Items.Add(sectionItem);
            }

            return dynamicItem;
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

        #region Control Management Methods

        /// <summary>
        /// Adds a new control to the form structure
        /// </summary>
        private void AddNewControl(ViewDefinition view, string parentSection = null)
        {
            // Create dialog for control properties
            var dialog = new Window
            {
                Title = "Add New Control",
                Width = 450,
                Height = 550,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = _mainWindow,
                Background = (Brush)_mainWindow.FindResource("BackgroundMedium")
            };

            var grid = new Grid { Margin = new Thickness(20) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            int row = 0;

            // Control Type
            AddDialogComboBox(grid, row++, "Control Type:", out ComboBox typeCombo);
            var controlTypes = new[] { "TextField", "DropDown", "DatePicker", "CheckBox", "RadioButton",
                                       "RichText", "PeoplePicker", "FileAttachment", "Button", "Label" };
            typeCombo.ItemsSource = controlTypes;
            typeCombo.SelectedIndex = 0;

            // Control Name
            AddDialogTextBox(grid, row++, "Control Name:", out TextBox nameText);
            nameText.Text = $"NewControl{++_autoNameCounter}";

            // Control Label
            AddDialogTextBox(grid, row++, "Label:", out TextBox labelText);
            labelText.Text = "New Control";

            // Binding
            AddDialogTextBox(grid, row++, "Binding:", out TextBox bindingText);
            bindingText.Text = "";

            // Target Section
            AddDialogComboBox(grid, row++, "Add to Section:", out ComboBox sectionCombo);
            var sections = GetAllSections(view);
            sections.Insert(0, "(No Section)");
            sectionCombo.ItemsSource = sections;
            if (!string.IsNullOrEmpty(parentSection))
            {
                sectionCombo.SelectedItem = sections.FirstOrDefault(s => s.Contains(parentSection));
            }
            else
            {
                sectionCombo.SelectedIndex = 0;
            }

            // Is Required
            AddDialogCheckBox(grid, row++, "Required:", out CheckBox requiredCheck);

            // Default Value
            AddDialogTextBox(grid, row++, "Default Value:", out TextBox defaultText);

            // Dropdown Options (shown only for dropdown types)
            var optionsLabel = new TextBlock
            {
                Text = "Options (one per line):",
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary"),
                Margin = new Thickness(0, 10, 0, 5),
                Visibility = Visibility.Collapsed
            };
            Grid.SetRow(optionsLabel, row++);
            Grid.SetColumn(optionsLabel, 1);
            grid.Children.Add(optionsLabel);

            var optionsText = new TextBox
            {
                Height = 80,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = (Brush)_mainWindow.FindResource("BackgroundLight"),
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary"),
                Visibility = Visibility.Collapsed
            };
            Grid.SetRow(optionsText, row++);
            Grid.SetColumn(optionsText, 1);
            grid.Children.Add(optionsText);

            // Show/hide options based on control type
            typeCombo.SelectionChanged += (s, e) =>
            {
                var selectedType = typeCombo.SelectedItem?.ToString();
                if (selectedType == "DropDown" || selectedType == "RadioButton" || selectedType == "ComboBox")
                {
                    optionsLabel.Visibility = Visibility.Visible;
                    optionsText.Visibility = Visibility.Visible;
                }
                else
                {
                    optionsLabel.Visibility = Visibility.Collapsed;
                    optionsText.Visibility = Visibility.Collapsed;
                }
            };

            // Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 20, 0, 0)
            };

            var addButton = new Button
            {
                Content = "Add Control",
                Width = 100,
                Height = 35,
                Margin = new Thickness(0, 0, 10, 0),
                Style = (Style)_mainWindow.FindResource("ModernButton")
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 100,
                Height = 35,
                Style = (Style)_mainWindow.FindResource("ModernButton")
            };

            addButton.Click += (s, e) =>
            {
                // Create new control
                var newControl = new ControlDefinition
                {
                    Name = nameText.Text,
                    Type = typeCombo.SelectedItem?.ToString() ?? "TextField",
                    Label = labelText.Text,
                    Binding = bindingText.Text,
                    DocIndex = view.Controls.Count + 1,
                    GridPosition = $"{view.Controls.Count + 1}A"
                };

                // Set properties
                newControl.Properties["Required"] = requiredCheck.IsChecked?.ToString() ?? "false";
                if (!string.IsNullOrEmpty(defaultText.Text))
                {
                    newControl.Properties["DefaultValue"] = defaultText.Text;
                }

                // Handle section assignment
                var selectedSection = sectionCombo.SelectedItem?.ToString();
                if (selectedSection != null && selectedSection != "(No Section)")
                {
                    // Extract section name from display text
                    var sectionName = ExtractSectionName(selectedSection);
                    var sectionInfo = view.Sections.FirstOrDefault(s => s.Name == sectionName);

                    if (sectionInfo != null)
                    {
                        if (sectionInfo.Type == "repeating")
                        {
                            newControl.IsInRepeatingSection = true;
                            newControl.RepeatingSectionName = sectionName;
                        }
                        else
                        {
                            newControl.ParentSection = sectionName;
                            newControl.SectionType = sectionInfo.Type;
                        }
                    }
                }

                // Handle dropdown options
                if (!string.IsNullOrEmpty(optionsText.Text))
                {
                    var options = optionsText.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    newControl.DataOptions = new List<DataOption>();
                    for (int i = 0; i < options.Length; i++)
                    {
                        newControl.DataOptions.Add(new DataOption
                        {
                            Value = options[i].Trim(),
                            DisplayText = options[i].Trim(),
                            Order = i,
                            IsDefault = i == 0
                        });
                    }
                    newControl.DataOptionsString = string.Join(", ", newControl.DataOptions.Select(o => o.DisplayText));
                }

                // Add to view
                view.Controls.Add(newControl);

                // Refresh tree view
                RefreshStructureTree();

                dialog.DialogResult = true;
                dialog.Close();
            };

            cancelButton.Click += (s, e) =>
            {
                dialog.DialogResult = false;
                dialog.Close();
            };

            buttonPanel.Children.Add(addButton);
            buttonPanel.Children.Add(cancelButton);

            Grid.SetRow(buttonPanel, grid.RowDefinitions.Count - 1);
            Grid.SetColumn(buttonPanel, 1);
            grid.Children.Add(buttonPanel);

            dialog.Content = grid;
            dialog.ShowDialog();
        }

        /// <summary>
        /// Moves a control to a different section (including repeating sections)
        /// </summary>
        private void MoveControlToSection(ControlDefinition control, ViewDefinition view)
        {
            var dialog = new Window
            {
                Title = "Move Control to Section",
                Width = 400,
                Height = 250,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = _mainWindow,
                Background = (Brush)_mainWindow.FindResource("BackgroundMedium")
            };

            var grid = new Grid { Margin = new Thickness(20) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Current location
            var currentLabel = new TextBlock
            {
                Text = $"Current Location: {GetControlLocation(control)}",
                Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(currentLabel, 0);
            grid.Children.Add(currentLabel);

            // Target section
            var targetLabel = new TextBlock
            {
                Text = "Move to Section:",
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary"),
                Margin = new Thickness(0, 0, 0, 5)
            };
            Grid.SetRow(targetLabel, 1);
            grid.Children.Add(targetLabel);

            var sectionCombo = new ComboBox
            {
                Style = (Style)_mainWindow.FindResource("ModernComboBox"),
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(sectionCombo, 2);
            grid.Children.Add(sectionCombo);

            // Populate sections
            var sections = GetAllSectionsWithType(view);
            sections.Insert(0, new SectionOption { Display = "(No Section)", Name = "", Type = "" });
            sectionCombo.ItemsSource = sections.Select(s => s.Display);
            sectionCombo.SelectedIndex = 0;

            // Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 20, 0, 0)
            };

            var moveButton = new Button
            {
                Content = "Move",
                Width = 100,
                Height = 35,
                Margin = new Thickness(0, 0, 10, 0),
                Style = (Style)_mainWindow.FindResource("ModernButton")
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 100,
                Height = 35,
                Style = (Style)_mainWindow.FindResource("ModernButton")
            };

            moveButton.Click += (s, e) =>
            {
                var selectedIndex = sectionCombo.SelectedIndex;
                if (selectedIndex >= 0 && selectedIndex < sections.Count)
                {
                    var targetSection = sections[selectedIndex];

                    // Clear current section assignments
                    control.ParentSection = null;
                    control.SectionType = null;
                    control.IsInRepeatingSection = false;
                    control.RepeatingSectionName = null;
                    control.RepeatingSectionBinding = null;

                    // Set new section assignment
                    if (!string.IsNullOrEmpty(targetSection.Name))
                    {
                        if (targetSection.Type == "repeating")
                        {
                            control.IsInRepeatingSection = true;
                            control.RepeatingSectionName = targetSection.Name;
                        }
                        else
                        {
                            control.ParentSection = targetSection.Name;
                            control.SectionType = targetSection.Type;
                        }
                    }

                    // Refresh tree view
                    RefreshStructureTree();

                    _mainWindow.UpdateStatus($"Moved control to: {targetSection.Display}", MessageSeverity.Info);
                }

                dialog.DialogResult = true;
                dialog.Close();
            };

            cancelButton.Click += (s, e) =>
            {
                dialog.DialogResult = false;
                dialog.Close();
            };

            buttonPanel.Children.Add(moveButton);
            buttonPanel.Children.Add(cancelButton);

            Grid.SetRow(buttonPanel, 4);
            grid.Children.Add(buttonPanel);

            dialog.Content = grid;
            dialog.ShowDialog();
        }

        /// <summary>
        /// Converts a regular section to a repeating section
        /// </summary>
        private void ConvertSectionToRepeating(SectionInfo section, ViewDefinition view)
        {
            var result = MessageBox.Show(
                $"Convert '{section.Name}' to a repeating section?\n\n" +
                "This will make all controls in this section repeatable.",
                "Convert to Repeating Section",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // Update section type
                section.Type = "repeating";

                // Update all controls in this section
                foreach (var control in view.Controls.Where(c => c.ParentSection == section.Name))
                {
                    control.IsInRepeatingSection = true;
                    control.RepeatingSectionName = section.Name;
                    control.SectionType = "repeating";
                }

                // Refresh tree view
                RefreshStructureTree();

                _mainWindow.UpdateStatus($"Converted '{section.Name}' to repeating section", MessageSeverity.Info);
            }
        }

        /// <summary>
        /// Moves an entire section into a repeating section (nesting)
        /// </summary>
        private void MoveSectionToRepeatingSection(SectionInfo section, ViewDefinition view)
        {
            var dialog = new Window
            {
                Title = "Move Section to Repeating Section",
                Width = 400,
                Height = 250,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = _mainWindow,
                Background = (Brush)_mainWindow.FindResource("BackgroundMedium")
            };

            var grid = new Grid { Margin = new Thickness(20) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Info
            var infoLabel = new TextBlock
            {
                Text = $"Move '{section.Name}' into a repeating section",
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary"),
                FontWeight = FontWeights.Medium,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(infoLabel, 0);
            grid.Children.Add(infoLabel);

            // Target repeating section
            var targetLabel = new TextBlock
            {
                Text = "Target Repeating Section:",
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary"),
                Margin = new Thickness(0, 0, 0, 5)
            };
            Grid.SetRow(targetLabel, 1);
            grid.Children.Add(targetLabel);

            var repeatingSectionCombo = new ComboBox
            {
                Style = (Style)_mainWindow.FindResource("ModernComboBox"),
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(repeatingSectionCombo, 2);
            grid.Children.Add(repeatingSectionCombo);

            // Get all repeating sections (excluding self)
            var repeatingSections = view.Sections
                .Where(s => s.Type == "repeating" && s.Name != section.Name)
                .Select(s => s.Name)
                .ToList();

            // Add repeating tables
            var repeatingTables = view.Controls
                .Where(c => c.Type == "RepeatingTable")
                .Select(c => c.Label ?? c.Name)
                .ToList();

            repeatingSections.AddRange(repeatingTables);

            if (!repeatingSections.Any())
            {
                MessageBox.Show("No repeating sections available to move into.",
                              "No Target Available",
                              MessageBoxButton.OK,
                              MessageBoxImage.Information);
                return;
            }

            repeatingSectionCombo.ItemsSource = repeatingSections;
            repeatingSectionCombo.SelectedIndex = 0;

            // Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 20, 0, 0)
            };

            var moveButton = new Button
            {
                Content = "Move Section",
                Width = 100,
                Height = 35,
                Margin = new Thickness(0, 0, 10, 0),
                Style = (Style)_mainWindow.FindResource("ModernButton")
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 100,
                Height = 35,
                Style = (Style)_mainWindow.FindResource("ModernButton")
            };

            moveButton.Click += (s, e) =>
            {
                var targetSection = repeatingSectionCombo.SelectedItem?.ToString();
                if (!string.IsNullOrEmpty(targetSection))
                {
                    // Update all controls in this section to be in the target repeating section
                    foreach (var control in view.Controls.Where(c => c.ParentSection == section.Name))
                    {
                        control.IsInRepeatingSection = true;
                        control.RepeatingSectionName = targetSection;
                        control.Properties["ParentRepeatingSections"] = targetSection;

                        // Keep the original section as a subsection marker
                        control.Properties["OriginalSection"] = section.Name;
                    }

                    // Update section to indicate it's nested
                    section.Type = "nested-repeating";

                    // Refresh tree view
                    RefreshStructureTree();

                    _mainWindow.UpdateStatus($"Moved section '{section.Name}' into repeating section '{targetSection}'",
                                            MessageSeverity.Info);
                }

                dialog.DialogResult = true;
                dialog.Close();
            };

            cancelButton.Click += (s, e) =>
            {
                dialog.DialogResult = false;
                dialog.Close();
            };

            buttonPanel.Children.Add(moveButton);
            buttonPanel.Children.Add(cancelButton);

            Grid.SetRow(buttonPanel, 4);
            grid.Children.Add(buttonPanel);

            dialog.Content = grid;
            dialog.ShowDialog();
        }

        #endregion

        #region Helper Methods for Control Management

        private void AddDialogTextBox(Grid grid, int row, string label, out TextBox textBox)
        {
            var labelControl = new TextBlock
            {
                Text = label,
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary"),
                Margin = new Thickness(0, 5, 0, 5)
            };
            Grid.SetRow(labelControl, row);
            Grid.SetColumn(labelControl, 0);
            grid.Children.Add(labelControl);

            textBox = new TextBox
            {
                Style = (Style)_mainWindow.FindResource("ModernTextBox"),
                Margin = new Thickness(0, 5, 0, 5),
                Height = 28
            };
            Grid.SetRow(textBox, row);
            Grid.SetColumn(textBox, 1);
            grid.Children.Add(textBox);
        }

        private void AddDialogComboBox(Grid grid, int row, string label, out ComboBox comboBox)
        {
            var labelControl = new TextBlock
            {
                Text = label,
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary"),
                Margin = new Thickness(0, 5, 0, 5)
            };
            Grid.SetRow(labelControl, row);
            Grid.SetColumn(labelControl, 0);
            grid.Children.Add(labelControl);

            comboBox = new ComboBox
            {
                Style = (Style)_mainWindow.FindResource("ModernComboBox"),
                Margin = new Thickness(0, 5, 0, 5),
                Height = 28
            };
            Grid.SetRow(comboBox, row);
            Grid.SetColumn(comboBox, 1);
            grid.Children.Add(comboBox);
        }

        private void AddDialogCheckBox(Grid grid, int row, string label, out CheckBox checkBox)
        {
            var labelControl = new TextBlock
            {
                Text = label,
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary"),
                Margin = new Thickness(0, 5, 0, 5)
            };
            Grid.SetRow(labelControl, row);
            Grid.SetColumn(labelControl, 0);
            grid.Children.Add(labelControl);

            checkBox = new CheckBox
            {
                Style = (Style)_mainWindow.FindResource("ModernCheckBox"),
                Margin = new Thickness(0, 5, 0, 5),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(checkBox, row);
            Grid.SetColumn(checkBox, 1);
            grid.Children.Add(checkBox);
        }

        private List<string> GetAllSections(ViewDefinition view)
        {
            var sections = new List<string>();

            // Regular sections
            foreach (var section in view.Sections)
            {
                var type = section.Type == "repeating" ? "🔁" : "📦";
                sections.Add($"{type} {section.Name}");
            }

            // Repeating tables
            foreach (var control in view.Controls.Where(c => c.Type == "RepeatingTable"))
            {
                sections.Add($"🔁 {control.Label ?? control.Name}");
            }

            return sections;
        }

        private List<SectionOption> GetAllSectionsWithType(ViewDefinition view)
        {
            var sections = new List<SectionOption>();

            // Regular sections
            foreach (var section in view.Sections)
            {
                var icon = section.Type == "repeating" ? "🔁" : "📦";
                sections.Add(new SectionOption
                {
                    Display = $"{icon} {section.Name}",
                    Name = section.Name,
                    Type = section.Type
                });
            }

            // Repeating tables
            foreach (var control in view.Controls.Where(c => c.Type == "RepeatingTable"))
            {
                var name = control.Label ?? control.Name;
                sections.Add(new SectionOption
                {
                    Display = $"🔁 {name}",
                    Name = name,
                    Type = "repeating"
                });
            }

            return sections;
        }

        private string ExtractSectionName(string displayText)
        {
            // Remove icon and return section name
            if (displayText.Contains(" "))
            {
                return displayText.Substring(displayText.IndexOf(' ') + 1);
            }
            return displayText;
        }

        private string GetControlLocation(ControlDefinition control)
        {
            if (!string.IsNullOrEmpty(control.RepeatingSectionName))
                return $"Repeating Section: {control.RepeatingSectionName}";
            if (!string.IsNullOrEmpty(control.ParentSection))
                return $"Section: {control.ParentSection}";
            return "Root Level";
        }

        private void RefreshStructureTree()
        {
            // Rebuild the structure tree with current data
            if (_mainWindow._allFormDefinitions != null)
            {
                BuildEnhancedStructureTree(_mainWindow._allFormDefinitions);
            }
        }

        private class SectionOption
        {
            public string Display { get; set; }
            public string Name { get; set; }
            public string Type { get; set; }
        }

        #endregion

        #region Updated Context Menu Creation

        private void AddControlContextMenu(TreeViewItem item, ControlDefinition control)
        {
            var contextMenu = new ContextMenu();

            // Edit Properties
            var editMenuItem = new MenuItem
            {
                Header = "Edit Control Properties",
                Icon = new TextBlock { Text = "✏️" }
            };
            editMenuItem.Click += (s, e) => ShowEditPanel(control, item);
            contextMenu.Items.Add(editMenuItem);

            // Move to Section
            var moveMenuItem = new MenuItem
            {
                Header = "Move to Section...",
                Icon = new TextBlock { Text = "➡️" }
            };
            moveMenuItem.Click += (s, e) =>
            {
                var view = GetViewForControl(control);
                if (view != null)
                    MoveControlToSection(control, view);
            };
            contextMenu.Items.Add(moveMenuItem);

            contextMenu.Items.Add(new Separator());

            // Duplicate Control
            var duplicateMenuItem = new MenuItem
            {
                Header = "Duplicate Control",
                Icon = new TextBlock { Text = "📋" }
            };
            duplicateMenuItem.Click += (s, e) => DuplicateControl(control);
            contextMenu.Items.Add(duplicateMenuItem);

            // Delete Control
            var deleteMenuItem = new MenuItem
            {
                Header = "Delete Control",
                Icon = new TextBlock { Text = "🗑️" }
            };
            deleteMenuItem.Click += (s, e) => DeleteControl(control);
            contextMenu.Items.Add(deleteMenuItem);

            contextMenu.Items.Add(new Separator());

            // Copy as JSON
            var copyJsonMenuItem = new MenuItem
            {
                Header = "Copy as JSON",
                Icon = new TextBlock { Text = "📄" }
            };
            copyJsonMenuItem.Click += (s, e) => CopyControlAsJson(control);
            contextMenu.Items.Add(copyJsonMenuItem);

            item.ContextMenu = contextMenu;
        }

        private void AddSectionContextMenu(TreeViewItem item, SectionInfo section, ViewDefinition view)
        {
            var contextMenu = new ContextMenu();

            // Add Control to Section
            var addControlMenuItem = new MenuItem
            {
                Header = "Add Control...",
                Icon = new TextBlock { Text = "➕" }
            };
            addControlMenuItem.Click += (s, e) => AddNewControl(view, section.Name);
            contextMenu.Items.Add(addControlMenuItem);

            if (section.Type != "repeating")
            {
                // Convert to Repeating Section
                var convertMenuItem = new MenuItem
                {
                    Header = "Convert to Repeating Section",
                    Icon = new TextBlock { Text = "🔁" }
                };
                convertMenuItem.Click += (s, e) => ConvertSectionToRepeating(section, view);
                contextMenu.Items.Add(convertMenuItem);

                // Move to Repeating Section
                var moveMenuItem = new MenuItem
                {
                    Header = "Move to Repeating Section...",
                    Icon = new TextBlock { Text = "➡️" }
                };
                moveMenuItem.Click += (s, e) => MoveSectionToRepeatingSection(section, view);
                contextMenu.Items.Add(moveMenuItem);
            }

            item.ContextMenu = contextMenu;
        }

        private ViewDefinition GetViewForControl(ControlDefinition control)
        {
            if (_mainWindow._allFormDefinitions != null)
            {
                foreach (var formDef in _mainWindow._allFormDefinitions.Values)
                {
                    foreach (var view in formDef.Views)
                    {
                        if (view.Controls.Contains(control))
                            return view;
                    }
                }
            }
            return null;
        }

        private void DuplicateControl(ControlDefinition control)
        {
            var view = GetViewForControl(control);
            if (view != null)
            {
                var newControl = new ControlDefinition
                {
                    Name = control.Name + "_Copy",
                    Type = control.Type,
                    Label = control.Label + " (Copy)",
                    Binding = control.Binding,
                    DocIndex = view.Controls.Count + 1,
                    GridPosition = $"{view.Controls.Count + 1}A",
                    ParentSection = control.ParentSection,
                    SectionType = control.SectionType,
                    IsInRepeatingSection = control.IsInRepeatingSection,
                    RepeatingSectionName = control.RepeatingSectionName,
                    RepeatingSectionBinding = control.RepeatingSectionBinding
                };

                // Copy properties
                foreach (var prop in control.Properties)
                {
                    newControl.Properties[prop.Key] = prop.Value;
                }

                // Copy data options
                if (control.DataOptions != null)
                {
                    newControl.DataOptions = new List<DataOption>(control.DataOptions);
                    newControl.DataOptionsString = control.DataOptionsString;
                }

                view.Controls.Add(newControl);
                RefreshStructureTree();

                _mainWindow.UpdateStatus($"Duplicated control: {control.Label}", MessageSeverity.Info);
            }
        }

        // Made public for accessibility
        public void DeleteControl(ControlDefinition control)
        {
            var result = MessageBox.Show(
                $"Delete control '{control.Label ?? control.Name}'?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                var view = GetViewForControl(control);
                if (view != null)
                {
                    view.Controls.Remove(control);
                    RefreshStructureTree();
                    _mainWindow.UpdateStatus($"Deleted control: {control.Label}", MessageSeverity.Info);
                }
            }
        }

        #endregion

        #region Control Editing Methods

        private void EditControl_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var control = button?.Tag as ControlDefinition;
            if (control != null)
            {
                var treeItem = FindTreeItemForControl(_mainWindow.StructureTreeView.Items, control);
                ShowEditPanel(control, treeItem);
            }
        }

        private TreeViewItem FindTreeItemForControl(ItemCollection items, ControlDefinition control)
        {
            foreach (TreeViewItem item in items)
            {
                if (item.Tag == control)
                    return item;

                var result = FindTreeItemForControl(item.Items, control);
                if (result != null)
                    return result;
            }
            return null;
        }

        // Made public for accessibility
        public void ShowEditPanel(ControlDefinition control, TreeViewItem treeItem)
        {
            // TODO: Implementation for edit panel
            _selectedControl = control;
            _selectedTreeItem = treeItem;

            MessageBox.Show($"Edit panel for control: {control.Label ?? control.Name}\n" +
                          $"Type: {control.Type}\n" +
                          $"Binding: {control.Binding}",
                          "Edit Control Properties",
                          MessageBoxButton.OK,
                          MessageBoxImage.Information);
        }

        // Added missing method
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

                // Refresh the tree view
                RefreshStructureTree();

                _mainWindow.UpdateStatus("Control removed from repeating section", MessageSeverity.Info);
            }
        }

        // Added missing method
        public void ShowAddControlDialog(ViewDefinition view, string parentSection = null)
        {
            AddNewControl(view, parentSection);
        }

        // Added missing method
        public void ShowMoveSectionDialog(ControlDefinition control)
        {
            var view = GetViewForControl(control);
            if (view != null)
            {
                MoveControlToSection(control, view);
            }
        }

        private void CopyControlAsJson(ControlDefinition control)
        {
            try
            {
                var json = JsonConvert.SerializeObject(new
                {
                    Name = control.Name,
                    Type = control.Type,
                    Label = control.Label,
                    Binding = control.Binding,
                    Section = control.ParentSection ?? control.RepeatingSectionName,
                    SectionType = control.SectionType,
                    IsInRepeatingSection = control.IsInRepeatingSection,
                    GridPosition = control.GridPosition,
                    HasDropdownValues = control.HasStaticData,
                    DropdownCount = control.DataOptions?.Count ?? 0
                }, Formatting.Indented);

                System.Windows.Clipboard.SetText(json);
                _mainWindow.UpdateStatus("Control JSON copied to clipboard", MessageSeverity.Info);
            }
            catch (Exception ex)
            {
                _mainWindow.UpdateStatus($"Failed to copy JSON: {ex.Message}", MessageSeverity.Error);
            }
        }

        #endregion

        #region Helper Methods for Enhanced Display

        private void AddControlTypeBadge(StackPanel panel, string controlType)
        {
            var typeBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(50, 100, 100, 100)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(0, 0, 5, 0)
            };
            typeBadge.Child = new TextBlock
            {
                Text = controlType,
                Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            panel.Children.Add(typeBadge);
        }

        private void AddSectionIndicator(StackPanel panel, ControlDefinition control)
        {
            var sectionText = control.ParentSection ?? control.RepeatingSectionName;
            var sectionBadge = new Border
            {
                Background = control.IsInRepeatingSection
                    ? new SolidColorBrush(Color.FromArgb(30, 3, 169, 244))
                    : new SolidColorBrush(Color.FromArgb(30, 76, 175, 80)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(0, 0, 5, 0)
            };
            sectionBadge.Child = new TextBlock
            {
                Text = $"📍 {sectionText}",
                Foreground = control.IsInRepeatingSection
                    ? (Brush)_mainWindow.FindResource("InfoColor")
                    : (Brush)_mainWindow.FindResource("SuccessColor"),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            panel.Children.Add(sectionBadge);
        }

        private void AddDropdownIndicator(StackPanel panel, ControlDefinition control)
        {
            var dropdownPanel = new StackPanel { Orientation = Orientation.Horizontal };

            dropdownPanel.Children.Add(new TextBlock
            {
                Text = " 📋",
                Foreground = (Brush)_mainWindow.FindResource("SuccessColor"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            });

            dropdownPanel.Children.Add(new TextBlock
            {
                Text = $"({control.DataOptions.Count})",
                Foreground = (Brush)_mainWindow.FindResource("SuccessColor"),
                ToolTip = $"Has {control.DataOptions.Count} dropdown options",
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            });

            panel.Children.Add(dropdownPanel);
        }

        private void AddEditButton(StackPanel panel, ControlDefinition control)
        {
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
                Margin = new Thickness(5, 0, 0, 0)
            };
            editButton.Click += EditControl_Click;
            panel.Children.Add(editButton);
        }

        private void AddControlProperties(TreeViewItem item, ControlDefinition control)
        {
            // Binding
            if (!string.IsNullOrEmpty(control.Binding))
            {
                item.Items.Add(CreateInfoItem($"Binding: {control.Binding}", 10));
            }

            // Dropdown values
            if (control.HasStaticData && control.DataOptions != null && control.DataOptions.Any())
            {
                var dropdownItem = new TreeViewItem
                {
                    Header = $"📋 Dropdown Values ({control.DataOptions.Count})",
                    Foreground = (Brush)_mainWindow.FindResource("SuccessColor"),
                    FontSize = 11,
                    IsExpanded = false
                };

                foreach (var option in control.DataOptions.OrderBy(o => o.Order))
                {
                    var optionText = option.DisplayText;
                    if (option.IsDefault)
                        optionText += " ⭐ (default)";

                    dropdownItem.Items.Add(new TreeViewItem
                    {
                        Header = $"  • {optionText}",
                        Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                        FontSize = 10,
                        ToolTip = $"Value: {option.Value}"
                    });
                }

                item.Items.Add(dropdownItem);
            }

            // Default value
            if (control.Properties.ContainsKey("DefaultValue") && !string.IsNullOrEmpty(control.Properties["DefaultValue"]))
            {
                item.Items.Add(CreateInfoItem($"Default: {control.Properties["DefaultValue"]}", 10));
            }
        }

        private object CreateEnhancedSectionHeader(string sectionName, string sectionType, int controlCount)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };

            // Section icon
            panel.Children.Add(new TextBlock
            {
                Text = GetSectionIcon(sectionType) + " ",
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center
            });

            // Section name
            panel.Children.Add(new TextBlock
            {
                Text = sectionName,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });

            // Section type badge
            var typeBadge = new Border
            {
                Background = GetSectionTypeBrush(sectionType),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 8, 0)
            };
            typeBadge.Child = new TextBlock
            {
                Text = FormatSectionType(sectionType),
                Foreground = Brushes.White,
                FontSize = 10,
                FontWeight = FontWeights.Medium
            };
            panel.Children.Add(typeBadge);

            // Control count
            panel.Children.Add(new TextBlock
            {
                Text = $"({controlCount} controls)",
                Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                FontStyle = FontStyles.Italic,
                VerticalAlignment = VerticalAlignment.Center
            });

            return panel;
        }

        private TreeViewItem CreateViewStatisticsItem(ViewDefinition view)
        {
            var statsItem = new TreeViewItem
            {
                Header = "📈 View Statistics",
                Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                FontSize = 11,
                IsExpanded = false
            };

            var controlTypeGroups = view.Controls
                .Where(c => !c.IsMergedIntoParent)
                .GroupBy(c => c.Type)
                .OrderByDescending(g => g.Count());

            foreach (var typeGroup in controlTypeGroups)
            {
                statsItem.Items.Add(new TreeViewItem
                {
                    Header = $"{GetControlIcon(typeGroup.Key)} {typeGroup.Key}: {typeGroup.Count()}",
                    Foreground = (Brush)_mainWindow.FindResource("TextDim"),
                    FontSize = 10
                });
            }

            // Add dropdown controls count
            var dropdownControls = view.Controls.Count(c => c.HasStaticData);
            if (dropdownControls > 0)
            {
                statsItem.Items.Add(new TreeViewItem
                {
                    Header = $"📋 Controls with dropdown values: {dropdownControls}",
                    Foreground = (Brush)_mainWindow.FindResource("SuccessColor"),
                    FontSize = 10
                });
            }

            // Add repeating controls count
            var repeatingControls = view.Controls.Count(c => c.IsInRepeatingSection);
            if (repeatingControls > 0)
            {
                statsItem.Items.Add(new TreeViewItem
                {
                    Header = $"🔁 Controls in repeating sections: {repeatingControls}",
                    Foreground = (Brush)_mainWindow.FindResource("InfoColor"),
                    FontSize = 10
                });
            }

            return statsItem;
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

        private HashSet<string> GetRepeatingSectionNames(InfoPathFormDefinition formDef)
        {
            var names = new HashSet<string>();

            foreach (var view in formDef.Views)
            {
                // From sections
                foreach (var section in view.Sections.Where(s => s.Type == "repeating"))
                {
                    names.Add(section.Name);
                }

                // From controls
                foreach (var control in view.Controls.Where(c => c.IsInRepeatingSection && !string.IsNullOrEmpty(c.RepeatingSectionName)))
                {
                    names.Add(control.RepeatingSectionName);
                }
            }

            return names;
        }

        private Brush GetSectionTypeBrush(string sectionType)
        {
            return sectionType?.ToLower() switch
            {
                "repeating" => new SolidColorBrush(Color.FromRgb(3, 169, 244)),
                "optional" => new SolidColorBrush(Color.FromRgb(255, 152, 0)),
                "dynamic" => new SolidColorBrush(Color.FromRgb(156, 39, 176)),
                _ => new SolidColorBrush(Color.FromRgb(76, 175, 80))
            };
        }

        private string FormatSectionType(string sectionType)
        {
            return sectionType?.ToLower() switch
            {
                "repeating" => "Repeating",
                "optional" => "Optional",
                "dynamic" => "Dynamic",
                _ => "Section"
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

        private void ApplySectionColor(TreeViewItem item, string sectionType)
        {
            item.Foreground = sectionType?.ToLower() switch
            {
                "repeating" => (Brush)_mainWindow.FindResource("InfoColor"),
                "optional" => (Brush)_mainWindow.FindResource("WarningColor"),
                "dynamic" => (Brush)_mainWindow.FindResource("AccentColor"),
                _ => (Brush)_mainWindow.FindResource("TextPrimary")
            };
        }

        #endregion

        #region Reusable Views Methods (Stubs for now)

        public async Task RefreshReusableViews()
        {
            // Implementation for reusable views analysis
            await Task.CompletedTask;
        }

        public async Task SaveReusableViews()
        {
            // Implementation for saving reusable views
            await Task.CompletedTask;
        }

        public async Task CreateReusableView()
        {
            // Implementation for creating reusable view
            await Task.CompletedTask;
        }

        public async Task ExportReusableControls()
        {
            // Implementation for exporting reusable controls
            await Task.CompletedTask;
        }

        public void MinOccurrences_Changed()
        {
            // Implementation for min occurrences change
        }

        public void GroupBy_Changed()
        {
            // Implementation for group by change
        }

        public void FilterType_Changed()
        {
            // Implementation for filter type change
        }

        public void GroupNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Implementation for group name text change
        }

        #endregion
    }

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
        public List<ControlOccurrence> Occurrences { get; set; } = new List<ControlOccurrence>();
        public Dictionary<string, string> CommonProperties { get; set; } = new Dictionary<string, string>();

        public string DisplayName => !string.IsNullOrEmpty(CustomName) ? CustomName :
                                 !string.IsNullOrEmpty(Label) ? Label :
                                 !string.IsNullOrEmpty(Name) ? Name :
                                 GroupKey;
    }

    public class ControlOccurrence
    {
        public string FormName { get; set; }
        public string ViewName { get; set; }
        public string Section { get; set; }
        public string GridPosition { get; set; }
        public ControlDefinition Control { get; set; }
    }

    #endregion
}