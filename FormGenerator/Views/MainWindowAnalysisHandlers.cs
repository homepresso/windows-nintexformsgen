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

            // First, create nodes for all sections
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

            // Handle the Trips repeating section specially
            if (sectionNodes.ContainsKey("Trips"))
            {
                var tripsSection = sectionNodes["Trips"];

                // Look for Round Trip conditional section
                if (sectionNodes.ContainsKey("Round Trip"))
                {
                    var roundTripSection = sectionNodes["Round Trip"];
                    roundTripSection.ParentSection = "Trips";
                    roundTripSection.Type = "conditional"; // Mark as conditional
                    tripsSection.ChildSections.Add(roundTripSection);
                }
            }

            // Now assign controls to their sections
            foreach (var control in view.Controls.Where(c => !c.IsMergedIntoParent))
            {
                string sectionKey = null;

                // Check for nested section structure (e.g., "Trips > Round Trip")
                if (!string.IsNullOrEmpty(control.ParentSection) && control.ParentSection.Contains(" > "))
                {
                    var parts = control.ParentSection.Split(new[] { " > " }, StringSplitOptions.None);
                    sectionKey = parts.Last(); // Use the innermost section
                }
                else if (!string.IsNullOrEmpty(control.ParentSection))
                {
                    sectionKey = control.ParentSection;
                }
                else if (control.IsInRepeatingSection && !string.IsNullOrEmpty(control.RepeatingSectionName))
                {
                    sectionKey = control.RepeatingSectionName;
                }

                // Also check for conditional section property
                if (control.Properties != null && control.Properties.ContainsKey("ConditionalSection"))
                {
                    sectionKey = control.Properties["ConditionalSection"];
                }

                if (!string.IsNullOrEmpty(sectionKey) && sectionNodes.ContainsKey(sectionKey))
                {
                    sectionNodes[sectionKey].Controls.Add(control);
                }
                else if (control.IsInRepeatingSection && !string.IsNullOrEmpty(control.RepeatingSectionName))
                {
                    // Add to the repeating section even if not in a sub-section
                    if (sectionNodes.ContainsKey(control.RepeatingSectionName))
                    {
                        sectionNodes[control.RepeatingSectionName].Controls.Add(control);
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

            // Add section metadata
            var sectionInfo = view.Sections.FirstOrDefault(s => s.Name == sectionNode.Name);
            if (sectionInfo != null)
            {
                sectionItem.Items.Add(CreateInfoItem($"Type: {sectionNode.Type}", 10));
                if (!string.IsNullOrEmpty(sectionInfo.CtrlId))
                {
                    sectionItem.Items.Add(CreateInfoItem($"ID: {sectionInfo.CtrlId}", 10));
                }

                // Add conditional indicator if this is a conditional section
                if (sectionNode.Type == "conditional" || sectionNode.Type == "conditional-in-repeating")
                {
                    sectionItem.Items.Add(CreateInfoItem($"⚡ Conditionally visible section", 10));
                }
            }

            // If this is a repeating table, add a special indicator
            if (sectionNode.IsRepeatingTable)
            {
                sectionItem.Items.Add(CreateInfoItem($"📊 Repeating Table", 10));
            }

            // Add child sections first (nested sections like Round Trip within Trips)
            foreach (var childSection in sectionNode.ChildSections)
            {
                var childItem = CreateSectionTreeItem(childSection, view);

                // Add visual indicator for conditional child sections
                if (childSection.Type == "conditional")
                {
                    var headerPanel = childItem.Header as StackPanel;
                    if (headerPanel == null)
                    {
                        headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
                        headerPanel.Children.Add(new TextBlock { Text = "⚡ " }); // Lightning bolt for conditional
                        if (childItem.Header is string headerText)
                        {
                            headerPanel.Children.Add(new TextBlock { Text = headerText });
                        }
                        childItem.Header = headerPanel;
                    }
                }

                sectionItem.Items.Add(childItem);
            }

            // Then add controls in this section
            foreach (var control in sectionNode.Controls.OrderBy(c => c.DocIndex))
            {
                if (!control.IsMergedIntoParent)
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

            // Control label/name with better formatting
            var displayName = !string.IsNullOrEmpty(control.Label) ? control.Label :
                             !string.IsNullOrEmpty(control.Name) ? control.Name : "Unnamed";

            // Main display text
            var mainText = new TextBlock
            {
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };

            // Add CtrlId if present
            if (control.Properties != null && control.Properties.ContainsKey("CtrlId"))
            {
                mainText.Text = $"[{control.Properties["CtrlId"]}] {displayName}";
            }
            else
            {
                mainText.Text = displayName;
            }

            headerPanel.Children.Add(mainText);

            // Control type badge
            AddControlTypeBadge(headerPanel, control.Type);

            // Control Name (if different from label)
            if (!string.IsNullOrEmpty(control.Name) && control.Name != control.Label)
            {
                var nameBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(30, 128, 128, 128)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(5, 1, 5, 1),
                    Margin = new Thickness(0, 0, 5, 0)
                };
                nameBadge.Child = new TextBlock
                {
                    Text = $"Name: {control.Name}",
                    Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center
                };
                headerPanel.Children.Add(nameBadge);
            }

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

            // Edit button - FIXED
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
            editButton.Click += (s, e) => ShowControlEditDialog(control);
            headerPanel.Children.Add(editButton);

            var item = new TreeViewItem
            {
                Header = headerPanel,
                Tag = control
            };

            // Add comprehensive control properties
            AddDetailedControlProperties(item, control);

            // Add context menu
            AddControlContextMenu(item, control);

            return item;
        }

        private void AddDetailedControlProperties(TreeViewItem item, ControlDefinition control)
        {
            // Control ID
            if (control.Properties != null && control.Properties.ContainsKey("CtrlId"))
            {
                item.Items.Add(CreateInfoItem($"Control ID: {control.Properties["CtrlId"]}", 10));
            }

            // Control Name (internal name)
            if (!string.IsNullOrEmpty(control.Name))
            {
                item.Items.Add(CreateInfoItem($"Internal Name: {control.Name}", 10));
            }

            // Binding
            if (!string.IsNullOrEmpty(control.Binding))
            {
                item.Items.Add(CreateInfoItem($"Binding: {control.Binding}", 10));
            }

            // Doc Index
            item.Items.Add(CreateInfoItem($"Document Index: {control.DocIndex}", 10));

            // Section information
            if (control.IsInRepeatingSection)
            {
                item.Items.Add(CreateInfoItem($"🔁 Repeating Section: {control.RepeatingSectionName}", 10));
                if (!string.IsNullOrEmpty(control.RepeatingSectionBinding))
                {
                    item.Items.Add(CreateInfoItem($"  Section Binding: {control.RepeatingSectionBinding}", 9));
                }
            }
            else if (!string.IsNullOrEmpty(control.ParentSection))
            {
                item.Items.Add(CreateInfoItem($"📦 Parent Section: {control.ParentSection}", 10));
                if (!string.IsNullOrEmpty(control.SectionType))
                {
                    item.Items.Add(CreateInfoItem($"  Section Type: {control.SectionType}", 9));
                }
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

            // Additional properties
            if (control.Properties != null && control.Properties.Any())
            {
                var propsItem = new TreeViewItem
                {
                    Header = $"⚙️ Properties ({control.Properties.Count})",
                    Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                    FontSize = 11,
                    IsExpanded = false
                };

                foreach (var prop in control.Properties.OrderBy(p => p.Key))
                {
                    // Skip already displayed properties
                    if (prop.Key == "CtrlId") continue;

                    propsItem.Items.Add(new TreeViewItem
                    {
                        Header = $"  {prop.Key}: {prop.Value}",
                        Foreground = (Brush)_mainWindow.FindResource("TextDim"),
                        FontSize = 10
                    });
                }

                item.Items.Add(propsItem);
            }
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
        // Replace the AddNewControl method in MainWindowAnalysisHandlers.cs with this properly formatted version:

        private void AddNewControl(ViewDefinition view, string parentSection = null)
        {
            // Create dialog for control properties
            var dialog = new Window
            {
                Title = "Add New Control",
                Width = 500,
                Height = 700,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = _mainWindow,
                Background = (Brush)_mainWindow.FindResource("BackgroundMedium")
            };

            // Main grid with proper row definitions
            var mainGrid = new Grid { Margin = new Thickness(20) };

            // Define rows for each field
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Title
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Control Type
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Control Name
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Label
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Binding
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Section
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Required
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Default Value
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Options
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Spacer
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons

            int currentRow = 0;

            // Title
            var titleText = new TextBlock
            {
                Text = "Add New Control",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary"),
                Margin = new Thickness(0, 0, 0, 20)
            };
            Grid.SetRow(titleText, currentRow++);
            mainGrid.Children.Add(titleText);

            // Control Type
            var typeLabel = CreateLabel("Control Type:");
            Grid.SetRow(typeLabel, currentRow++);
            mainGrid.Children.Add(typeLabel);

            var typeCombo = new ComboBox
            {
                Style = (Style)_mainWindow.FindResource("ModernComboBox"),
                Height = 32,
                Margin = new Thickness(0, 0, 0, 15)
            };
            var controlTypes = new[] {
        "TextField", "DropDown", "DatePicker", "CheckBox", "RadioButton",
        "RichText", "PeoplePicker", "FileAttachment", "Button", "Label"
    };
            typeCombo.ItemsSource = controlTypes;
            typeCombo.SelectedIndex = 0;
            Grid.SetRow(typeCombo, currentRow++);
            mainGrid.Children.Add(typeCombo);

            // Control Name
            var nameLabel = CreateLabel("Control Name:");
            Grid.SetRow(nameLabel, currentRow++);
            mainGrid.Children.Add(nameLabel);

            var nameText = new TextBox
            {
                Style = (Style)_mainWindow.FindResource("ModernTextBox"),
                Text = $"NewControl{++_autoNameCounter}",
                Height = 32,
                Margin = new Thickness(0, 0, 0, 15)
            };
            Grid.SetRow(nameText, currentRow++);
            mainGrid.Children.Add(nameText);

            // Control Label
            var labelLabel = CreateLabel("Label:");
            Grid.SetRow(labelLabel, currentRow++);
            mainGrid.Children.Add(labelLabel);

            var labelText = new TextBox
            {
                Style = (Style)_mainWindow.FindResource("ModernTextBox"),
                Text = "New Control",
                Height = 32,
                Margin = new Thickness(0, 0, 0, 15)
            };
            Grid.SetRow(labelText, currentRow++);
            mainGrid.Children.Add(labelText);

            // Binding
            var bindingLabel = CreateLabel("Binding:");
            Grid.SetRow(bindingLabel, currentRow++);
            mainGrid.Children.Add(bindingLabel);

            var bindingText = new TextBox
            {
                Style = (Style)_mainWindow.FindResource("ModernTextBox"),
                Height = 32,
                Margin = new Thickness(0, 0, 0, 15)
            };
            Grid.SetRow(bindingText, currentRow++);
            mainGrid.Children.Add(bindingText);

            // Section Selection
            var sectionLabel = CreateLabel("Add to Section:");
            Grid.SetRow(sectionLabel, currentRow++);
            mainGrid.Children.Add(sectionLabel);

            var sectionCombo = new ComboBox
            {
                Style = (Style)_mainWindow.FindResource("ModernComboBox"),
                Height = 32,
                Margin = new Thickness(0, 0, 0, 15)
            };

            // Get all sections including repeating ones
            var sections = new List<SectionOption>();
            sections.Add(new SectionOption { Display = "(No Section)", Name = "", Type = "" });

            foreach (var section in view.Sections)
            {
                var typeText = section.Type == "repeating" ? " (repeating)" :
                               section.Type == "optional" ? " (optional)" :
                               section.Type == "dynamic" ? " (dynamic)" : "";
                sections.Add(new SectionOption
                {
                    Display = $"{section.Name}{typeText}",
                    Name = section.Name,
                    Type = section.Type
                });
            }

            foreach (var control in view.Controls.Where(c => c.Type == "RepeatingTable"))
            {
                sections.Add(new SectionOption
                {
                    Display = $"{control.Label ?? control.Name} (repeating table)",
                    Name = control.Label ?? control.Name,
                    Type = "repeating"
                });
            }

            sectionCombo.ItemsSource = sections.Select(s => s.Display);

            if (!string.IsNullOrEmpty(parentSection))
            {
                var matchingSection = sections.FindIndex(s => s.Name == parentSection);
                if (matchingSection >= 0)
                    sectionCombo.SelectedIndex = matchingSection;
            }
            else
            {
                sectionCombo.SelectedIndex = 0;
            }

            Grid.SetRow(sectionCombo, currentRow++);
            mainGrid.Children.Add(sectionCombo);

            // Required checkbox
            var requiredCheck = new CheckBox
            {
                Style = (Style)_mainWindow.FindResource("ModernCheckBox"),
                Content = "Required Field",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 15)
            };
            Grid.SetRow(requiredCheck, currentRow++);
            mainGrid.Children.Add(requiredCheck);

            // Default Value
            var defaultLabel = CreateLabel("Default Value:");
            Grid.SetRow(defaultLabel, currentRow++);
            mainGrid.Children.Add(defaultLabel);

            var defaultText = new TextBox
            {
                Style = (Style)_mainWindow.FindResource("ModernTextBox"),
                Height = 32,
                Margin = new Thickness(0, 0, 0, 15)
            };
            Grid.SetRow(defaultText, currentRow++);
            mainGrid.Children.Add(defaultText);

            // Dropdown Options (shown only for dropdown types)
            var optionsPanel = new StackPanel
            {
                Visibility = Visibility.Collapsed
            };

            var optionsLabel = CreateLabel("Options (one per line):");
            optionsPanel.Children.Add(optionsLabel);

            var optionsText = new TextBox
            {
                Style = (Style)_mainWindow.FindResource("ModernTextBox"),
                Height = 100,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 5, 0, 15)
            };
            optionsPanel.Children.Add(optionsText);

            Grid.SetRow(optionsPanel, currentRow++);
            mainGrid.Children.Add(optionsPanel);

            // Show/hide options based on control type
            typeCombo.SelectionChanged += (s, e) =>
            {
                var selectedType = typeCombo.SelectedItem?.ToString();
                if (selectedType == "DropDown" || selectedType == "RadioButton" || selectedType == "ComboBox")
                {
                    optionsPanel.Visibility = Visibility.Visible;
                }
                else
                {
                    optionsPanel.Visibility = Visibility.Collapsed;
                }
            };

            // Spacer row takes remaining space
            currentRow++; // Skip the star-sized row

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
                Width = 120,
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
                    GridPosition = $"{view.Controls.Count + 1}A",
                    Properties = new Dictionary<string, string>()
                };

                // Generate unique CtrlId
                var existingCtrlIds = view.Controls
                    .Where(c => c.Properties != null && c.Properties.ContainsKey("CtrlId"))
                    .Select(c => c.Properties["CtrlId"])
                    .ToList();

                int ctrlIdNum = 1;
                string newCtrlId;
                do
                {
                    newCtrlId = $"CTRL{ctrlIdNum++}";
                } while (existingCtrlIds.Contains(newCtrlId));

                newControl.Properties["CtrlId"] = newCtrlId;
                newControl.Properties["Required"] = requiredCheck.IsChecked?.ToString() ?? "false";

                if (!string.IsNullOrEmpty(defaultText.Text))
                {
                    newControl.Properties["DefaultValue"] = defaultText.Text;
                }

                // Handle section assignment
                var selectedIndex = sectionCombo.SelectedIndex;
                if (selectedIndex > 0 && selectedIndex < sections.Count)
                {
                    var targetSection = sections[selectedIndex];

                    if (targetSection.Type == "repeating")
                    {
                        newControl.IsInRepeatingSection = true;
                        newControl.RepeatingSectionName = targetSection.Name;
                        newControl.SectionType = "repeating";
                    }
                    else
                    {
                        newControl.ParentSection = targetSection.Name;
                        newControl.SectionType = targetSection.Type;
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
                    // HasStaticData is read-only - it's automatically determined by having DataOptions
                }

                // Add to view
                view.Controls.Add(newControl);

                // Refresh tree view
                RefreshStructureTree();

                _mainWindow.UpdateStatus($"Added control: {newControl.Label} [{newCtrlId}]", MessageSeverity.Info);

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

            Grid.SetRow(buttonPanel, currentRow);
            mainGrid.Children.Add(buttonPanel);

            dialog.Content = mainGrid;
            dialog.ShowDialog();
        }

        // Helper method to create consistent labels
        private TextBlock CreateLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary"),
                FontWeight = FontWeights.Medium,
                Margin = new Thickness(0, 0, 0, 5)
            };
        }
        // Helper method to create consistent labels
      

        private StackPanel CreateLabeledControl(string label)
        {
            var panel = new StackPanel();
            var labelControl = new TextBlock
            {
                Text = label,
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary"),
                FontWeight = FontWeights.Medium,
                Margin = new Thickness(0, 0, 0, 5)
            };
            panel.Children.Add(labelControl);
            return panel;
        }

        // In MainWindowAnalysisHandlers.cs, make sure ShowControlEditDialog is public and properly updates JSON:

        // In MainWindowAnalysisHandlers.cs, make sure ShowControlEditDialog is public and properly updates JSON:

        public void ShowControlEditDialog(ControlDefinition control)
        {
            var dialog = new Window
            {
                Title = $"Edit Control - {control.Label ?? control.Name}",
                Width = 550,
                Height = 700,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = _mainWindow,
                Background = (Brush)_mainWindow.FindResource("BackgroundMedium")
            };

            // Create a grid for better spacing
            var mainGrid = new Grid { Margin = new Thickness(20) };

            // Define rows for proper spacing
            int rowCount = 0;
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Title
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(15) }); // Spacer
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Type label
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Type input
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) }); // Spacer
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Name label
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Name input
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) }); // Spacer
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Label label
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Label input
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) }); // Spacer
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Binding label
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Binding input
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) }); // Spacer
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Section label
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Section input
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) }); // Spacer
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Required
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) }); // Spacer
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Default label
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Default input
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) }); // Spacer
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Options (conditional)
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Spacer
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons

            // Title with control ID
            var titleText = new TextBlock
            {
                Text = "Edit Control Properties",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary")
            };
            Grid.SetRow(titleText, rowCount++);
            mainGrid.Children.Add(titleText);

            if (control.Properties?.ContainsKey("CtrlId") == true)
            {
                var idText = new TextBlock
                {
                    Text = $"Control ID: {control.Properties["CtrlId"]}",
                    FontSize = 12,
                    Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                    Margin = new Thickness(0, 5, 0, 0)
                };
                Grid.SetRow(idText, rowCount - 1);
                Grid.SetRowSpan(idText, 1);
                mainGrid.Children.Add(idText);
            }

            rowCount++; // Skip spacer

            // Control Type (read-only)
            var typeLabel = CreateLabel("Control Type:");
            Grid.SetRow(typeLabel, rowCount++);
            mainGrid.Children.Add(typeLabel);

            var typeText = new TextBox
            {
                Style = (Style)_mainWindow.FindResource("ModernTextBox"),
                Text = control.Type,
                IsReadOnly = true,
                Background = new SolidColorBrush(Color.FromArgb(20, 128, 128, 128)),
                Height = 32
            };
            Grid.SetRow(typeText, rowCount++);
            mainGrid.Children.Add(typeText);

            rowCount++; // Skip spacer

            // Control Name
            var nameLabel = CreateLabel("Control Name:");
            Grid.SetRow(nameLabel, rowCount++);
            mainGrid.Children.Add(nameLabel);

            var nameText = new TextBox
            {
                Style = (Style)_mainWindow.FindResource("ModernTextBox"),
                Text = control.Name ?? "",
                Height = 32
            };
            Grid.SetRow(nameText, rowCount++);
            mainGrid.Children.Add(nameText);

            rowCount++; // Skip spacer

            // Control Label
            var labelLabel = CreateLabel("Label:");
            Grid.SetRow(labelLabel, rowCount++);
            mainGrid.Children.Add(labelLabel);

            var labelText = new TextBox
            {
                Style = (Style)_mainWindow.FindResource("ModernTextBox"),
                Text = control.Label ?? "",
                Height = 32
            };
            Grid.SetRow(labelText, rowCount++);
            mainGrid.Children.Add(labelText);

            rowCount++; // Skip spacer

            // Binding
            var bindingLabel = CreateLabel("Binding:");
            Grid.SetRow(bindingLabel, rowCount++);
            mainGrid.Children.Add(bindingLabel);

            var bindingText = new TextBox
            {
                Style = (Style)_mainWindow.FindResource("ModernTextBox"),
                Text = control.Binding ?? "",
                Height = 32
            };
            Grid.SetRow(bindingText, rowCount++);
            mainGrid.Children.Add(bindingText);

            rowCount++; // Skip spacer

            // Current Section (read-only)
            var currentSectionLabel = CreateLabel("Current Section:");
            Grid.SetRow(currentSectionLabel, rowCount++);
            mainGrid.Children.Add(currentSectionLabel);

            var currentSectionText = new TextBox
            {
                Style = (Style)_mainWindow.FindResource("ModernTextBox"),
                Text = control.IsInRepeatingSection ? $"Repeating: {control.RepeatingSectionName}" :
                       !string.IsNullOrEmpty(control.ParentSection) ? $"Section: {control.ParentSection}" : "(No Section)",
                IsReadOnly = true,
                Background = new SolidColorBrush(Color.FromArgb(20, 128, 128, 128)),
                Height = 32
            };
            Grid.SetRow(currentSectionText, rowCount++);
            mainGrid.Children.Add(currentSectionText);

            rowCount++; // Skip spacer

            // Required checkbox
            var requiredCheck = new CheckBox
            {
                Style = (Style)_mainWindow.FindResource("ModernCheckBox"),
                Content = "Required Field",
                IsChecked = control.Properties?.ContainsKey("Required") == true &&
                           control.Properties["Required"] == "true",
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(requiredCheck, rowCount++);
            mainGrid.Children.Add(requiredCheck);

            rowCount++; // Skip spacer

            // Default Value
            var defaultLabel = CreateLabel("Default Value:");
            Grid.SetRow(defaultLabel, rowCount++);
            mainGrid.Children.Add(defaultLabel);

            var defaultText = new TextBox
            {
                Style = (Style)_mainWindow.FindResource("ModernTextBox"),
                Text = control.Properties?.ContainsKey("DefaultValue") == true ?
                       control.Properties["DefaultValue"] : "",
                Height = 32
            };
            Grid.SetRow(defaultText, rowCount++);
            mainGrid.Children.Add(defaultText);

            rowCount++; // Skip spacer

            // Dropdown Options (if applicable)
            TextBox optionsTextBox = null;
            if (control.Type == "DropDown" || control.Type == "RadioButton" || control.Type == "ComboBox")
            {
                var optionsPanel = new StackPanel();
                var optionsLabel = CreateLabel("Options (one per line):");
                optionsPanel.Children.Add(optionsLabel);

                optionsTextBox = new TextBox
                {
                    Style = (Style)_mainWindow.FindResource("ModernTextBox"),
                    Height = 100,
                    AcceptsReturn = true,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Text = control.DataOptions != null ?
                           string.Join("\r\n", control.DataOptions.Select(o => o.DisplayText)) : "",
                    Margin = new Thickness(0, 5, 0, 0)
                };
                optionsPanel.Children.Add(optionsTextBox);

                Grid.SetRow(optionsPanel, rowCount++);
                mainGrid.Children.Add(optionsPanel);
            }

            // Skip to buttons row
            rowCount = mainGrid.RowDefinitions.Count - 1;

            // Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var saveButton = new Button
            {
                Content = "Save Changes",
                Width = 120,
                Height = 35,
                Margin = new Thickness(0, 0, 10, 0),
                Style = (Style)_mainWindow.FindResource("ModernButton")
            };

            var moveButton = new Button
            {
                Content = "Move to Section...",
                Width = 130,
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

            saveButton.Click += (s, e) =>
            {
                // Update control properties
                control.Name = nameText.Text;
                control.Label = labelText.Text;
                control.Binding = bindingText.Text;

                if (control.Properties == null)
                    control.Properties = new Dictionary<string, string>();

                control.Properties["Required"] = requiredCheck.IsChecked?.ToString() ?? "false";

                if (!string.IsNullOrEmpty(defaultText.Text))
                    control.Properties["DefaultValue"] = defaultText.Text;
                else if (control.Properties.ContainsKey("DefaultValue"))
                    control.Properties.Remove("DefaultValue");

                // Update dropdown options if applicable
                if (optionsTextBox != null && !string.IsNullOrEmpty(optionsTextBox.Text))
                {
                    var options = optionsTextBox.Text.Split(new[] { '\r', '\n' },
                        StringSplitOptions.RemoveEmptyEntries);
                    control.DataOptions = new List<DataOption>();
                    for (int i = 0; i < options.Length; i++)
                    {
                        control.DataOptions.Add(new DataOption
                        {
                            Value = options[i].Trim(),
                            DisplayText = options[i].Trim(),
                            Order = i,
                            IsDefault = i == 0
                        });
                    }
                    control.DataOptionsString = string.Join(", ", control.DataOptions.Select(o => o.DisplayText));
                    // HasStaticData is read-only - it's automatically determined by having DataOptions
                }

                // Refresh tree view
                RefreshStructureTree();

                // UPDATE JSON OUTPUT - this is the key part
                if (_mainWindow._allFormDefinitions != null && _mainWindow._allFormDefinitions.Any())
                {
                    DisplayEnhancedJson(_mainWindow._allFormDefinitions);
                }

                _mainWindow.UpdateStatus($"Updated control: {control.Label}", MessageSeverity.Info);

                dialog.DialogResult = true;
                dialog.Close();
            };

            moveButton.Click += (s, e) =>
            {
                dialog.Close();
                var view = GetViewForControl(control);
                if (view != null)
                    MoveControlToSection(control, view);
            };

            cancelButton.Click += (s, e) =>
            {
                dialog.DialogResult = false;
                dialog.Close();
            };

            buttonPanel.Children.Add(saveButton);
            buttonPanel.Children.Add(moveButton);
            buttonPanel.Children.Add(cancelButton);

            Grid.SetRow(buttonPanel, rowCount);
            mainGrid.Children.Add(buttonPanel);

            dialog.Content = mainGrid;
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
            // Just redirect to ShowControlEditDialog
            ShowControlEditDialog(control);
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