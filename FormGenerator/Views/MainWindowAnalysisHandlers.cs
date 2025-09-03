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

        private Brush GetControlIconColor(string controlType)
        {
            return controlType?.ToLower() switch
            {
                "textfield" => new SolidColorBrush(Color.FromRgb(100, 200, 255)), // Light blue
                "richtext" => new SolidColorBrush(Color.FromRgb(150, 200, 255)), // Lighter blue
                "dropdown" => new SolidColorBrush(Color.FromRgb(255, 200, 100)), // Light orange
                "combobox" => new SolidColorBrush(Color.FromRgb(255, 180, 100)), // Orange
                "checkbox" => new SolidColorBrush(Color.FromRgb(100, 255, 150)), // Light green
                "datepicker" => new SolidColorBrush(Color.FromRgb(255, 150, 255)), // Light pink
                "peoplepicker" => new SolidColorBrush(Color.FromRgb(150, 150, 255)), // Light purple
                "fileattachment" => new SolidColorBrush(Color.FromRgb(200, 200, 200)), // Light gray
                "button" => new SolidColorBrush(Color.FromRgb(100, 255, 200)), // Mint green
                "repeatingtable" => new SolidColorBrush(Color.FromRgb(100, 200, 255)), // Light blue
                "repeatingsection" => new SolidColorBrush(Color.FromRgb(100, 180, 255)), // Blue
                "label" => new SolidColorBrush(Color.FromRgb(200, 200, 150)), // Light yellow-gray
                "radiobutton" => new SolidColorBrush(Color.FromRgb(200, 150, 255)), // Light violet
                "hyperlink" => new SolidColorBrush(Color.FromRgb(100, 150, 255)), // Link blue
                "inlinepicture" => new SolidColorBrush(Color.FromRgb(255, 200, 150)), // Light peach
                "signatureline" => new SolidColorBrush(Color.FromRgb(255, 255, 150)), // Light yellow
                _ => new SolidColorBrush(Color.FromRgb(180, 180, 180)) // Default light gray
            };
        }
        private TreeViewItem CreateEnhancedControlItem(ControlDefinition control)
        {
            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };

            // Control icon with better visibility
            var iconText = new TextBlock
            {
                Text = GetControlIcon(control.Type) + " ",
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 14,
                Foreground = GetControlIconColor(control.Type)
            };
            headerPanel.Children.Add(iconText);

            // Control label/name with better formatting (no control ID)
            var displayName = !string.IsNullOrEmpty(control.Label) ? control.Label :
                             !string.IsNullOrEmpty(control.Name) ? control.Name : "Unnamed";

            // Main display text - just the display name
            var mainText = new TextBlock
            {
                Text = displayName,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                MinWidth = 150
            };

            headerPanel.Children.Add(mainText);

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

            // Add spacer to push action buttons to the right
            headerPanel.Children.Add(new TextBlock
            {
                Text = " ",
                MinWidth = 20,
                HorizontalAlignment = HorizontalAlignment.Stretch
            });

            // Action buttons container with proper visibility
            var actionPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };

            // Edit button - visible gray by default
            var editButton = new Button
            {
                Content = "✏️",
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Padding = new Thickness(3),
                ToolTip = "Edit control properties",
                Tag = control,
                Width = 22,
                Height = 22,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 2, 0),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120)) // Gray by default
            };
            editButton.Click += (s, e) =>
            {
                e.Handled = true; // Prevent tree selection change
                ShowControlEditDialog(control);
            };
            editButton.MouseEnter += (s, e) => editButton.Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)); // Bright on hover
            editButton.MouseLeave += (s, e) => editButton.Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120)); // Back to gray
            actionPanel.Children.Add(editButton);

            // Move to section button - visible gray by default
            var moveButton = new Button
            {
                Content = "➡️",
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Padding = new Thickness(3),
                ToolTip = "Move to section",
                Tag = control,
                Width = 22,
                Height = 22,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 2, 0),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120)) // Gray by default
            };
            moveButton.Click += (s, e) =>
            {
                e.Handled = true;
                var view = GetViewForControl(control);
                if (view != null)
                    MoveControlToSection(control, view);
            };
            moveButton.MouseEnter += (s, e) => moveButton.Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)); // Bright on hover
            moveButton.MouseLeave += (s, e) => moveButton.Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120)); // Back to gray
            actionPanel.Children.Add(moveButton);

            // Delete button - visible gray by default
            var deleteButton = new Button
            {
                Content = "🗑️",
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Padding = new Thickness(3),
                ToolTip = "Delete control",
                Tag = control,
                Width = 22,
                Height = 22,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 2, 0),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120)) // Gray by default
            };
            deleteButton.Click += (s, e) =>
            {
                e.Handled = true;

                var result = MessageBox.Show(
                    $"Are you sure you want to delete '{control.Label ?? control.Name}'?",
                    "Confirm Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    var view = GetViewForControl(control);
                    if (view != null)
                    {
                        // Remove the control from the view
                        view.Controls.Remove(control);

                        // Update the form definition's metadata
                        if (_mainWindow._allFormDefinitions != null)
                        {
                            foreach (var formDef in _mainWindow._allFormDefinitions.Values)
                            {
                                if (formDef.Views.Contains(view))
                                {
                                    // Update control count in metadata
                                    formDef.Metadata.TotalControls = formDef.Views.Sum(v => v.Controls.Count);
                                    break;
                                }
                            }
                        }

                        // Refresh everything - tree, JSON, and data columns
                        RefreshAll();

                        _mainWindow.UpdateStatus($"Deleted control: {control.Label ?? control.Name}", MessageSeverity.Info);
                    }
                }
            };
            deleteButton.MouseEnter += (s, e) => deleteButton.Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100)); // Red on hover for delete
            deleteButton.MouseLeave += (s, e) => deleteButton.Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120)); // Back to gray
            actionPanel.Children.Add(deleteButton);

            // Remove the opacity changes since we're using color changes now
            // actionPanel.Opacity = 0.3;
            // headerPanel.MouseEnter += (s, e) => actionPanel.Opacity = 1.0;
            // headerPanel.MouseLeave += (s, e) => actionPanel.Opacity = 0.3;

            headerPanel.Children.Add(actionPanel);

            var item = new TreeViewItem
            {
                Header = headerPanel,
                Tag = control,
                MinHeight = 26
            };

            // Add comprehensive control properties
            AddDetailedControlProperties(item, control);

            // Add simplified context menu (without duplicate actions)
            AddSimplifiedControlContextMenu(item, control);

            return item;
        }


        /// <summary>
        /// Creates an enhanced metadata item with section summary
        /// </summary>
        private TreeViewItem CreateEnhancedMetadataItem(InfoPathFormDefinition formDef)
        {
            var metadataItem = new TreeViewItem
            {
                Header = "📊 Form Summary",
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)), // Brighter
                FontSize = 11,
                IsExpanded = false,
                MinHeight = 24
            };

            // Basic counts with brighter text
            metadataItem.Items.Add(new TreeViewItem
            {
                Header = $"Views: {formDef.Views.Count}",
                Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                FontSize = 10,
                MinHeight = 20,
                Padding = new Thickness(5, 2, 5, 2)
            });

            metadataItem.Items.Add(new TreeViewItem
            {
                Header = $"Total Controls: {formDef.Metadata.TotalControls}",
                Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                FontSize = 10,
                MinHeight = 20,
                Padding = new Thickness(5, 2, 5, 2)
            });

            metadataItem.Items.Add(new TreeViewItem
            {
                Header = $"Data Columns: {formDef.Data.Count}",
                Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                FontSize = 10,
                MinHeight = 20,
                Padding = new Thickness(5, 2, 5, 2)
            });

            // Section information with bright colors
            if (formDef.Metadata.RepeatingSectionCount > 0)
            {
                var repeatingSectionNames = GetRepeatingSectionNames(formDef);
                var repeatingSectionItem = new TreeViewItem
                {
                    Header = $"🔁 Repeating Sections: {formDef.Metadata.RepeatingSectionCount}",
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 200, 255)), // Bright blue
                    FontSize = 11,
                    IsExpanded = false,
                    MinHeight = 22
                };

                foreach (var sectionName in repeatingSectionNames)
                {
                    repeatingSectionItem.Items.Add(new TreeViewItem
                    {
                        Header = $"  • {sectionName}",
                        Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)), // Brighter
                        FontSize = 10,
                        MinHeight = 18,
                        Padding = new Thickness(5, 1, 5, 1)
                    });
                }

                metadataItem.Items.Add(repeatingSectionItem);
            }

            if (formDef.Metadata.DynamicSectionCount > 0)
            {
                metadataItem.Items.Add(new TreeViewItem
                {
                    Header = $"🔄 Dynamic Sections: {formDef.Metadata.DynamicSectionCount} (conditional visibility)",
                    Foreground = new SolidColorBrush(Color.FromRgb(200, 150, 255)), // Bright purple
                    FontSize = 11,
                    MinHeight = 22
                });
            }

            // Conditional fields with bright colors
            if (formDef.Metadata.ConditionalFields.Any())
            {
                var conditionalItem = new TreeViewItem
                {
                    Header = $"⚡ Conditional Fields: {formDef.Metadata.ConditionalFields.Count}",
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 100)), // Bright yellow
                    FontSize = 11,
                    IsExpanded = false,
                    MinHeight = 22
                };

                foreach (var field in formDef.Metadata.ConditionalFields)
                {
                    conditionalItem.Items.Add(new TreeViewItem
                    {
                        Header = $"  • {field}",
                        Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)), // Brighter
                        FontSize = 10,
                        MinHeight = 18,
                        Padding = new Thickness(5, 1, 5, 1)
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
            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };

            headerPanel.Children.Add(new TextBlock
            {
                Text = "📄 ",
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center
            });

            headerPanel.Children.Add(new TextBlock
            {
                Text = view.ViewName,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 200
            });

            // Add control button for views
            var addButton = new Button
            {
                Content = "➕",
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Padding = new Thickness(3),
                ToolTip = "Add new control",
                Width = 22,
                Height = 22,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 2, 0),
                FontSize = 12,
                Opacity = 0.5
            };

            addButton.Click += (s, e) =>
            {
                e.Handled = true;
                ShowAddControlDialog(view, null);
            };

            // Show/hide on hover
            headerPanel.MouseEnter += (s, e) => addButton.Opacity = 1.0;
            headerPanel.MouseLeave += (s, e) => addButton.Opacity = 0.5;

            headerPanel.Children.Add(addButton);

            var viewItem = new TreeViewItem
            {
                Header = headerPanel,
                Tag = view,
                IsExpanded = false,
                MinHeight = 26
            };

            // Build hierarchical structure of sections and controls
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


        private TreeViewItem CreateSectionTreeItem(SectionNode sectionNode, ViewDefinition view)
        {
            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };

            // Section icon with color
            var sectionIcon = new TextBlock
            {
                Text = GetSectionIcon(sectionNode.Type) + " ",
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = GetSectionIconColor(sectionNode.Type)
            };
            headerPanel.Children.Add(sectionIcon);

            // Section name
            headerPanel.Children.Add(new TextBlock
            {
                Text = sectionNode.Name,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 150,
                Margin = new Thickness(0, 0, 8, 0)
            });

            // Section type badge
            var typeBadge = new Border
            {
                Background = GetSectionTypeBrush(sectionNode.Type),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            typeBadge.Child = new TextBlock
            {
                Text = FormatSectionType(sectionNode.Type),
                Foreground = Brushes.White,
                FontSize = 10,
                FontWeight = FontWeights.Medium
            };
            headerPanel.Children.Add(typeBadge);

            // Control count
            var controlCount = sectionNode.Controls.Count + sectionNode.ChildSections.Sum(cs => CountAllControls(cs));
            headerPanel.Children.Add(new TextBlock
            {
                Text = $"({controlCount} controls)",
                Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                FontStyle = FontStyles.Italic,
                VerticalAlignment = VerticalAlignment.Center
            });

            // Add control button for sections - gray by default, bright on hover
            var addButton = new Button
            {
                Content = "➕",
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Padding = new Thickness(3),
                ToolTip = "Add control to section",
                Width = 22,
                Height = 22,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 2, 0),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120)) // Gray by default
            };

            addButton.Click += (s, e) =>
            {
                e.Handled = true;
                ShowAddControlDialog(view, sectionNode.Name);
            };

            // Bright green on hover for add
            addButton.MouseEnter += (s, e) => addButton.Foreground = new SolidColorBrush(Color.FromRgb(100, 255, 100));
            addButton.MouseLeave += (s, e) => addButton.Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120));

            headerPanel.Children.Add(addButton);

            var sectionItem = new TreeViewItem
            {
                Header = headerPanel,
                IsExpanded = false,
                Tag = sectionNode.Name,
                MinHeight = 26
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
                    var headerPanel2 = childItem.Header as StackPanel;
                    if (headerPanel2 == null)
                    {
                        headerPanel2 = new StackPanel { Orientation = Orientation.Horizontal };
                        headerPanel2.Children.Add(new TextBlock { Text = "⚡ " }); // Lightning bolt for conditional
                        if (childItem.Header is string headerText)
                        {
                            headerPanel2.Children.Add(new TextBlock { Text = headerText });
                        }
                        childItem.Header = headerPanel2;
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



        private int CountAllControls(SectionNode section)
        {
            int count = section.Controls.Count(c => !c.IsMergedIntoParent);
            foreach (var childSection in section.ChildSections)
            {
                count += CountAllControls(childSection);
            }
            return count;
        }

        private void AddSimplifiedControlContextMenu(TreeViewItem item, ControlDefinition control)
        {
            var contextMenu = new ContextMenu();

            // Duplicate Control
            var duplicateMenuItem = new MenuItem
            {
                Header = "Duplicate Control",
                Icon = new TextBlock { Text = "📋", FontSize = 14 }
            };
            duplicateMenuItem.Click += (s, e) => DuplicateControl(control);
            contextMenu.Items.Add(duplicateMenuItem);

            // Copy as JSON
            var copyJsonMenuItem = new MenuItem
            {
                Header = "Copy as JSON",
                Icon = new TextBlock { Text = "📄", FontSize = 14 }
            };
            copyJsonMenuItem.Click += (s, e) => CopyControlAsJson(control);
            contextMenu.Items.Add(copyJsonMenuItem);

            contextMenu.Items.Add(new Separator());

            // Convert to Repeating (if applicable)
            if (!control.IsInRepeatingSection)
            {
                var convertMenuItem = new MenuItem
                {
                    Header = "Convert to Repeating Section",
                    Icon = new TextBlock { Text = "🔁", FontSize = 14 }
                };
                convertMenuItem.Click += (s, e) => ShowMoveSectionDialog(control);
                contextMenu.Items.Add(convertMenuItem);
            }
            else
            {
                // Remove from Repeating Section
                var removeMenuItem = new MenuItem
                {
                    Header = "Remove from Repeating Section",
                    Icon = new TextBlock { Text = "➖", FontSize = 14 }
                };
                removeMenuItem.Click += (s, e) => RemoveFromRepeatingSectionWithRefresh(control);
                contextMenu.Items.Add(removeMenuItem);
            }

            item.ContextMenu = contextMenu;
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
                    IsExpanded = false,
                    MinHeight = 22
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
                        ToolTip = $"Value: {option.Value}",
                        MinHeight = 18,
                        Padding = new Thickness(5, 1, 5, 1)
                    };
                    dropdownItem.Items.Add(optionItem);
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
                    IsExpanded = false,
                    MinHeight = 22
                };

                foreach (var prop in control.Properties.OrderBy(p => p.Key))
                {
                    // Skip already displayed properties
                    if (prop.Key == "CtrlId") continue;

                    var propItem = new TreeViewItem
                    {
                        Header = $"  {prop.Key}: {prop.Value}",
                        Foreground = (Brush)_mainWindow.FindResource("TextDim"),
                        FontSize = 10,
                        MinHeight = 18,
                        Padding = new Thickness(5, 1, 5, 1)
                    };
                    propsItem.Items.Add(propItem);
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
       //                 IsConditional = column.IsConditional,
       //                 ConditionalOnField = column.ConditionalOnField,
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
                }

                // Add to view
                view.Controls.Add(newControl);

                // REFRESH BOTH TREE AND JSON
                RefreshAll();

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
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 5)
            };
        }





        public void ShowControlEditDialog(ControlDefinition control)
        {
            var dialog = new Window
            {
                Title = $"Edit Control - {control.Label ?? control.Name}",
                Width = 600,
                Height = 800,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = _mainWindow,
                Background = (Brush)_mainWindow.FindResource("BackgroundMedium")
            };

            // Create main scrollviewer for long forms
            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            // Create a grid for better spacing
            var mainGrid = new Grid { Margin = new Thickness(20) };

            // Define rows for proper spacing
            int rowCount = 0;
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Title
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) }); // Spacer
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Type label
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Type input
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(15) }); // Spacer
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Name label
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Name input
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(15) }); // Spacer
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Label label
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Label input
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(15) }); // Spacer
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Binding label
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Binding input
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(15) }); // Spacer
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Section label
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Section input
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(15) }); // Spacer
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Required
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(15) }); // Spacer
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Default label
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Default input
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(15) }); // Spacer
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Options (conditional)
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Spacer
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons

            // Title with control ID
            var titleText = new TextBlock
            {
                Text = "Edit Control Properties",
                FontSize = 20,
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
                    FontSize = 13,
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
                Text = control.Type,
                IsReadOnly = true,
                Background = new SolidColorBrush(Color.FromArgb(20, 128, 128, 128)),
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary"),
                BorderBrush = (Brush)_mainWindow.FindResource("BorderColor"),
                BorderThickness = new Thickness(1),
                Height = 40,  // Increased height
                Padding = new Thickness(10, 8, 10, 8),  // Better padding
                FontSize = 14,
                VerticalContentAlignment = VerticalAlignment.Center
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
                Text = control.Name ?? "",
                Background = (Brush)_mainWindow.FindResource("BackgroundLight"),
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary"),
                BorderBrush = (Brush)_mainWindow.FindResource("BorderColor"),
                BorderThickness = new Thickness(1),
                Height = 40,  // Increased height
                Padding = new Thickness(10, 8, 10, 8),  // Better padding
                FontSize = 14,
                VerticalContentAlignment = VerticalAlignment.Center
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
                Text = control.Label ?? "",
                Background = (Brush)_mainWindow.FindResource("BackgroundLight"),
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary"),
                BorderBrush = (Brush)_mainWindow.FindResource("BorderColor"),
                BorderThickness = new Thickness(1),
                Height = 40,  // Increased height
                Padding = new Thickness(10, 8, 10, 8),  // Better padding
                FontSize = 14,
                VerticalContentAlignment = VerticalAlignment.Center
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
                Text = control.Binding ?? "",
                Background = (Brush)_mainWindow.FindResource("BackgroundLight"),
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary"),
                BorderBrush = (Brush)_mainWindow.FindResource("BorderColor"),
                BorderThickness = new Thickness(1),
                Height = 40,  // Increased height
                Padding = new Thickness(10, 8, 10, 8),  // Better padding
                FontSize = 14,
                VerticalContentAlignment = VerticalAlignment.Center
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
                Text = control.IsInRepeatingSection ? $"Repeating: {control.RepeatingSectionName}" :
                       !string.IsNullOrEmpty(control.ParentSection) ? $"Section: {control.ParentSection}" : "(No Section)",
                IsReadOnly = true,
                Background = new SolidColorBrush(Color.FromArgb(20, 128, 128, 128)),
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary"),
                BorderBrush = (Brush)_mainWindow.FindResource("BorderColor"),
                BorderThickness = new Thickness(1),
                Height = 40,  // Increased height
                Padding = new Thickness(10, 8, 10, 8),  // Better padding
                FontSize = 14,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(currentSectionText, rowCount++);
            mainGrid.Children.Add(currentSectionText);

            rowCount++; // Skip spacer

            // Required checkbox
            var requiredCheck = new CheckBox
            {
                Content = "Required Field",
                IsChecked = control.Properties?.ContainsKey("Required") == true &&
                           control.Properties["Required"] == "true",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary"),
                FontSize = 14,
                Margin = new Thickness(0, 5, 0, 5)
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
                Text = control.Properties?.ContainsKey("DefaultValue") == true ?
                       control.Properties["DefaultValue"] : "",
                Background = (Brush)_mainWindow.FindResource("BackgroundLight"),
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary"),
                BorderBrush = (Brush)_mainWindow.FindResource("BorderColor"),
                BorderThickness = new Thickness(1),
                Height = 40,  // Increased height
                Padding = new Thickness(10, 8, 10, 8),  // Better padding
                FontSize = 14,
                VerticalContentAlignment = VerticalAlignment.Center
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
                    Height = 120,
                    AcceptsReturn = true,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Text = control.DataOptions != null ?
                           string.Join("\r\n", control.DataOptions.Select(o => o.DisplayText)) : "",
                    Background = (Brush)_mainWindow.FindResource("BackgroundLight"),
                    Foreground = (Brush)_mainWindow.FindResource("TextPrimary"),
                    BorderBrush = (Brush)_mainWindow.FindResource("BorderColor"),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(10, 8, 10, 8),
                    FontSize = 14,
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
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 20, 0, 0)
            };

            var saveButton = new Button
            {
                Content = "Save Changes",
                Width = 140,
                Height = 40,
                Margin = new Thickness(0, 0, 10, 0),
                Style = (Style)_mainWindow.FindResource("ModernButton"),
                FontSize = 14
            };

            var moveButton = new Button
            {
                Content = "Move to Section",
                Width = 140,
                Height = 40,
                Margin = new Thickness(0, 0, 10, 0),
                Style = (Style)_mainWindow.FindResource("ModernButton"),
                FontSize = 14
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 100,
                Height = 40,
                Style = (Style)_mainWindow.FindResource("ModernButton"),
                FontSize = 14
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
                }

                // REFRESH BOTH TREE VIEW AND JSON
                RefreshAll();

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

            scrollViewer.Content = mainGrid;
            dialog.Content = scrollViewer;
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

                    // REFRESH BOTH TREE AND JSON
                    RefreshAll();

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
            var view = GetViewForControl(control);
            if (view != null)
            {
                // Remove the control from the view
                view.Controls.Remove(control);

                // Update the form definition's metadata
                if (_mainWindow._allFormDefinitions != null)
                {
                    foreach (var formDef in _mainWindow._allFormDefinitions.Values)
                    {
                        if (formDef.Views.Contains(view))
                        {
                            // Update control count in metadata
                            formDef.Metadata.TotalControls = formDef.Views.Sum(v => v.Controls.Count);

                            // If this was in a repeating section, update that count too
                            if (control.IsInRepeatingSection)
                            {
                                var repeatingCount = formDef.Views
                                    .SelectMany(v => v.Controls)
                                    .Count(c => c.IsInRepeatingSection);
                                // Update repeating controls count if tracked
                            }
                            break;
                        }
                    }
                }

                // REFRESH BOTH TREE AND JSON
                RefreshAll();

                _mainWindow.UpdateStatus($"Deleted control: {control.Label ?? control.Name}", MessageSeverity.Info);
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

  

        private TreeViewItem CreateViewStatisticsItem(ViewDefinition view)
        {
            var statsItem = new TreeViewItem
            {
                Header = "📈 View Statistics",
                Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                FontSize = 11,
                IsExpanded = false,
                MinHeight = 24
            };

            var controlTypeGroups = view.Controls
                .Where(c => !c.IsMergedIntoParent)
                .GroupBy(c => c.Type)
                .OrderByDescending(g => g.Count());

            foreach (var typeGroup in controlTypeGroups)
            {
                var statItem = new TreeViewItem
                {
                    Header = $"{GetControlIcon(typeGroup.Key)} {typeGroup.Key}: {typeGroup.Count()}",
                    Foreground = (Brush)_mainWindow.FindResource("TextDim"),
                    FontSize = 10,
                    MinHeight = 20,
                    Padding = new Thickness(5, 2, 5, 2)
                };
                statsItem.Items.Add(statItem);
            }

            // Add dropdown controls count
            var dropdownControls = view.Controls.Count(c => c.HasStaticData);
            if (dropdownControls > 0)
            {
                var dropdownItem = new TreeViewItem
                {
                    Header = $"📋 Controls with dropdown values: {dropdownControls}",
                    Foreground = (Brush)_mainWindow.FindResource("SuccessColor"),
                    FontSize = 10,
                    MinHeight = 20,
                    Padding = new Thickness(5, 2, 5, 2)
                };
                statsItem.Items.Add(dropdownItem);
            }

            // Add repeating controls count
            var repeatingControls = view.Controls.Count(c => c.IsInRepeatingSection);
            if (repeatingControls > 0)
            {
                var repeatingItem = new TreeViewItem
                {
                    Header = $"🔁 Controls in repeating sections: {repeatingControls}",
                    Foreground = (Brush)_mainWindow.FindResource("InfoColor"),
                    FontSize = 10,
                    MinHeight = 20,
                    Padding = new Thickness(5, 2, 5, 2)
                };
                statsItem.Items.Add(repeatingItem);
            }

            return statsItem;
        }
        private TreeViewItem CreateInfoItem(string text, int fontSize = 11)
        {
            return new TreeViewItem
            {
                Header = text,
                Foreground = (Brush)_mainWindow.FindResource("TextDim"),
                FontSize = fontSize,
                MinHeight = 20,
                Padding = new Thickness(5, 2, 5, 2)
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
                "conditional" => "⚡",
                _ => "📦"
            };
        }

        private Brush GetSectionIconColor(string sectionType)
        {
            return sectionType?.ToLower() switch
            {
                "repeating" => new SolidColorBrush(Color.FromRgb(100, 200, 255)), // Light blue
                "optional" => new SolidColorBrush(Color.FromRgb(255, 200, 100)), // Light orange
                "dynamic" => new SolidColorBrush(Color.FromRgb(200, 150, 255)), // Light purple
                "conditional" => new SolidColorBrush(Color.FromRgb(255, 255, 100)), // Light yellow
                _ => new SolidColorBrush(Color.FromRgb(150, 200, 150)) // Light green
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

        private void RefreshJsonOutput()
        {
            if (_mainWindow._allFormDefinitions != null && _mainWindow._allFormDefinitions.Any())
            {
                DisplayEnhancedJson(_mainWindow._allFormDefinitions);
            }
        }

        private void RefreshAll()
        {
            // Rebuild the structure tree
            if (_mainWindow._allFormDefinitions != null)
            {
                BuildEnhancedStructureTree(_mainWindow._allFormDefinitions);
            }

            // Refresh the JSON output
            RefreshJsonOutput();

            // Also refresh the data columns grid if needed
            if (_mainWindow._allFormDefinitions != null)
            {
                DisplayEnhancedDataColumns(_mainWindow._allFormDefinitions);
            }
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