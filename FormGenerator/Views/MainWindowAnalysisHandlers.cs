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

            // Add view statistics
            var statsItem = CreateViewStatisticsItem(view);
            viewItem.Items.Add(statsItem);

            return viewItem;
        }

        /// <summary>
        /// Groups controls by their section information
        /// </summary>
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

        /// <summary>
        /// Creates an enhanced section item with proper structure
        /// </summary>
        private TreeViewItem CreateEnhancedSectionItem(string sectionName, List<ControlDefinition> controls, ViewDefinition view)
        {
            // Determine section type from controls or view sections
            var sectionInfo = view.Sections.FirstOrDefault(s => s.Name == sectionName);
            string sectionType = sectionInfo?.Type ?? DetermineSectionType(controls);

            var sectionItem = new TreeViewItem
            {
                Header = CreateEnhancedSectionHeader(sectionName, sectionType, controls.Count(c => !c.IsMergedIntoParent)),
                IsExpanded = false,
                Tag = sectionName
            };

            // Apply color based on section type
            ApplySectionColor(sectionItem, sectionType);

            // Add section metadata
            if (sectionInfo != null)
            {
                sectionItem.Items.Add(CreateInfoItem($"Type: {sectionType}", 10));
                if (!string.IsNullOrEmpty(sectionInfo.CtrlId))
                {
                    sectionItem.Items.Add(CreateInfoItem($"ID: {sectionInfo.CtrlId}", 10));
                }
            }

            // Add controls in this section
            foreach (var control in controls.OrderBy(c => c.DocIndex))
            {
                if (!control.IsMergedIntoParent)
                {
                    sectionItem.Items.Add(CreateEnhancedControlItem(control));
                }
            }

            return sectionItem;
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

        #region Helper Methods for Enhanced Display

        private string DetermineSectionType(List<ControlDefinition> controls)
        {
            if (controls.Any(c => c.IsInRepeatingSection))
                return "repeating";
            if (controls.Any(c => c.SectionType == "optional"))
                return "optional";
            if (controls.Any(c => c.SectionType == "dynamic"))
                return "dynamic";
            return "section";
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

        private void AddControlContextMenu(TreeViewItem item, ControlDefinition control)
        {
            var contextMenu = new ContextMenu();

            var editMenuItem = new MenuItem
            {
                Header = "Edit Control Properties",
                Icon = new TextBlock { Text = "✏️" }
            };
            editMenuItem.Click += (s, e) => ShowEditPanel(control, item);
            contextMenu.Items.Add(editMenuItem);

            if (control.IsInRepeatingSection)
            {
                var removeMenuItem = new MenuItem
                {
                    Header = "Remove from Repeating Section",
                    Icon = new TextBlock { Text = "➖" }
                };
                removeMenuItem.Click += (s, e) => RemoveFromRepeatingSection(control, item);
                contextMenu.Items.Add(removeMenuItem);
            }

            var copyJsonMenuItem = new MenuItem
            {
                Header = "Copy as JSON",
                Icon = new TextBlock { Text = "📋" }
            };
            copyJsonMenuItem.Click += (s, e) => CopyControlAsJson(control);
            contextMenu.Items.Add(copyJsonMenuItem);

            item.ContextMenu = contextMenu;
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

        #endregion

        #region Reusable Views Methods

        public async Task RefreshReusableViews()
        {
            if (_mainWindow._allFormDefinitions == null || !_mainWindow._allFormDefinitions.Any())
            {
                MessageBox.Show("Please analyze forms first before identifying reusable views.",
                              "No Forms Analyzed",
                              MessageBoxButton.OK,
                              MessageBoxImage.Information);
                return;
            }

            _mainWindow.UpdateStatus("Analyzing for reusable control groups...");

            try
            {
                int minOccurrences = GetMinOccurrences();
                int minGroupSize = 3;
                int maxGroupSize = 10;

                _currentReusableAnalysis = await Task.Run(() =>
                    _reusableAnalyzer.AnalyzeForReusableGroups(
                        _mainWindow._allFormDefinitions,
                        minOccurrences,
                        minGroupSize,
                        maxGroupSize));

                await DisplayReusableGroups();
                UpdateReusableStatistics();

                _mainWindow.SaveReusableViewsButton.IsEnabled = true;
                _mainWindow.UpdateStatus($"Found {_currentReusableAnalysis.IdentifiedGroups.Count} reusable control groups",
                                        MessageSeverity.Info);
            }
            catch (Exception ex)
            {
                _mainWindow.UpdateStatus($"Error analyzing reusable views: {ex.Message}", MessageSeverity.Error);
            }
        }

        private async Task DisplayReusableGroups()
        {
            await _mainWindow.Dispatcher.InvokeAsync(() =>
            {
                _mainWindow.ReusableControlsTreeView.Items.Clear();

                if (_currentReusableAnalysis == null || !_currentReusableAnalysis.IdentifiedGroups.Any())
                {
                    var noDataItem = new TreeViewItem
                    {
                        Header = "No reusable control groups found with current criteria",
                        Foreground = (Brush)_mainWindow.FindResource("TextDim"),
                        FontStyle = FontStyles.Italic
                    };
                    _mainWindow.ReusableControlsTreeView.Items.Add(noDataItem);
                    return;
                }

                var groupedByPattern = _currentReusableAnalysis.IdentifiedGroups
                    .GroupBy(g => GetPatternCategory(g.SuggestedName))
                    .OrderByDescending(g => g.Sum(x => x.OccurrenceCount));

                foreach (var patternGroup in groupedByPattern)
                {
                    var categoryItem = new TreeViewItem
                    {
                        Header = CreateCategoryHeader(patternGroup.Key, patternGroup.Count()),
                        IsExpanded = true,
                        FontWeight = FontWeights.Medium
                    };

                    foreach (var group in patternGroup.OrderByDescending(g => g.OccurrenceCount))
                    {
                        var groupItem = new TreeViewItem
                        {
                            Header = CreateGroupHeader(group),
                            Tag = group,
                            IsExpanded = false
                        };

                        foreach (var control in group.Controls)
                        {
                            var controlItem = new TreeViewItem
                            {
                                Header = CreateControlHeader(control),
                                FontSize = 11
                            };
                            groupItem.Items.Add(controlItem);
                        }

                        groupItem.Selected += GroupItem_Selected;
                        categoryItem.Items.Add(groupItem);
                    }

                    _mainWindow.ReusableControlsTreeView.Items.Add(categoryItem);
                }
            });
        }

        private void GroupItem_Selected(object sender, RoutedEventArgs e)
        {
            var item = sender as TreeViewItem;
            if (item?.Tag is ReusableControlGroupAnalyzer.ControlGroup group)
            {
                _selectedGroup = group;
                DisplayGroupDetails(group);

                _mainWindow.GroupNameTextBox.IsEnabled = true;
                _mainWindow.GroupNameTextBox.Text = group.SuggestedName;
                _mainWindow.CreateReusableViewButton.IsEnabled = true;
                _mainWindow.ExportReusableControlsButton.IsEnabled = true;

                e.Handled = true;
            }
        }

        private void DisplayGroupDetails(ReusableControlGroupAnalyzer.ControlGroup group)
        {
            _mainWindow.ReusableDetailsPanel.Children.Clear();

            var summaryText = new TextBlock
            {
                Text = $"Control Group: {group.SuggestedName}",
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary"),
                Margin = new Thickness(0, 0, 0, 10)
            };
            _mainWindow.ReusableDetailsPanel.Children.Add(summaryText);

            var occurrenceText = new TextBlock
            {
                Text = $"Appears in {group.OccurrenceCount} of {_currentReusableAnalysis.TotalFormsAnalyzed} forms",
                Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                Margin = new Thickness(0, 0, 0, 10)
            };
            _mainWindow.ReusableDetailsPanel.Children.Add(occurrenceText);
        }

        private void UpdateReusableStatistics()
        {
            var panel = _mainWindow.ReusableStatisticsPanel as WrapPanel;
            if (panel != null)
                panel.Children.Clear();

            if (_currentReusableAnalysis == null)
                return;

            AddStatCard("📊", "Total Groups",
                       _currentReusableAnalysis.IdentifiedGroups.Count.ToString(),
                       (Brush)_mainWindow.FindResource("AccentColor"));
        }

        private void AddStatCard(string icon, string label, string value, Brush color)
        {
            var card = new Border
            {
                Background = (Brush)_mainWindow.FindResource("BackgroundLight"),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(5),
                MinWidth = 100
            };

            var stack = new StackPanel();

            var iconText = new TextBlock
            {
                Text = icon,
                FontSize = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 5)
            };
            stack.Children.Add(iconText);

            var valueText = new TextBlock
            {
                Text = value,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = color,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(valueText);

            var labelText = new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(labelText);

            card.Child = stack;

            var panel = _mainWindow.ReusableStatisticsPanel as WrapPanel;
            if (panel != null)
                panel.Children.Add(card);
        }

        public async Task SaveReusableViews()
        {
            if (_currentReusableAnalysis == null)
                return;

            var dialog = new SaveFileDialog
            {
                Title = "Save Reusable Views Analysis",
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                FileName = $"ReusableViews_{DateTime.Now:yyyyMMdd_HHmmss}.json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var exportData = new
                    {
                        AnalysisDate = DateTime.Now,
                        Summary = new
                        {
                            TotalFormsAnalyzed = _currentReusableAnalysis.TotalFormsAnalyzed,
                            TotalControlsAnalyzed = _currentReusableAnalysis.TotalControlsAnalyzed,
                            TotalGroups = _currentReusableAnalysis.IdentifiedGroups.Count
                        },
                        ReusableControlGroups = _currentReusableAnalysis.IdentifiedGroups
                    };

                    var json = JsonConvert.SerializeObject(exportData, Formatting.Indented);
                    await File.WriteAllTextAsync(dialog.FileName, json);

                    _mainWindow.UpdateStatus($"Saved reusable views analysis to {dialog.FileName}", MessageSeverity.Info);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error saving file: {ex.Message}", "Save Error",
                                  MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        public async Task CreateReusableView()
        {
            if (_selectedGroup == null)
                return;

            MessageBox.Show($"Creating K2 SmartForm view: {_selectedGroup.SuggestedName}\n\n" +
                          $"This would create a reusable Item View with {_selectedGroup.Controls.Count} controls.",
                          "Create Reusable View",
                          MessageBoxButton.OK,
                          MessageBoxImage.Information);
        }

        public async Task ExportReusableControls()
        {
            if (_selectedGroup == null)
                return;

            var dialog = new SaveFileDialog
            {
                Title = "Export Control Group",
                Filter = "JSON Files (*.json)|*.json|XML Files (*.xml)|*.xml|All Files (*.*)|*.*",
                FileName = $"{_selectedGroup.SuggestedName}_{DateTime.Now:yyyyMMdd}.json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var json = JsonConvert.SerializeObject(_selectedGroup, Formatting.Indented);
                    await File.WriteAllTextAsync(dialog.FileName, json);

                    _mainWindow.UpdateStatus($"Exported control group to {dialog.FileName}", MessageSeverity.Info);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error exporting: {ex.Message}", "Export Error",
                                  MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private int GetMinOccurrences()
        {
            var selected = _mainWindow.MinOccurrencesCombo.SelectedItem as ComboBoxItem;
            if (selected?.Tag != null && int.TryParse(selected.Tag.ToString(), out int min))
                return min;
            return 2;
        }

        private string GetPatternCategory(string suggestedName)
        {
            if (suggestedName.Contains("Name")) return "📝 Name Fields";
            if (suggestedName.Contains("Address")) return "📍 Address Fields";
            if (suggestedName.Contains("Contact")) return "📞 Contact Fields";
            if (suggestedName.Contains("Date") || suggestedName.Contains("Time")) return "📅 Date/Time Fields";
            if (suggestedName.Contains("Organization") || suggestedName.Contains("Department")) return "🏢 Organization Fields";
            return "📦 Other Field Groups";
        }

        private object CreateCategoryHeader(string category, int count)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };

            panel.Children.Add(new TextBlock
            {
                Text = $"{category} ",
                FontWeight = FontWeights.Medium,
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary")
            });

            panel.Children.Add(new TextBlock
            {
                Text = $"({count} groups)",
                Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                FontStyle = FontStyles.Italic
            });

            return panel;
        }

        private object CreateGroupHeader(ReusableControlGroupAnalyzer.ControlGroup group)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };

            var reuseIndicator = new Border
            {
                Background = GetReuseBrush(group.OccurrenceCount),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1, 4, 1),
                Margin = new Thickness(0, 0, 5, 0)
            };

            reuseIndicator.Child = new TextBlock
            {
                Text = $"{group.OccurrenceCount}x",
                Foreground = Brushes.White,
                FontSize = 10,
                FontWeight = FontWeights.Bold
            };
            panel.Children.Add(reuseIndicator);

            panel.Children.Add(new TextBlock
            {
                Text = group.SuggestedName,
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary"),
                VerticalAlignment = VerticalAlignment.Center
            });

            panel.Children.Add(new TextBlock
            {
                Text = $" ({group.Controls.Count} controls)",
                Foreground = (Brush)_mainWindow.FindResource("TextDim"),
                FontStyle = FontStyles.Italic,
                VerticalAlignment = VerticalAlignment.Center
            });

            return panel;
        }

        private object CreateControlHeader(ReusableControlGroupAnalyzer.ControlSignature control)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };

            var typeIcon = GetControlIcon(control.Type);
            panel.Children.Add(new TextBlock
            {
                Text = typeIcon + " ",
                Foreground = GetControlTypeBrush(control.Type),
                FontSize = 14
            });

            panel.Children.Add(new TextBlock
            {
                Text = control.Label ?? control.Name ?? "Unnamed",
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary")
            });

            panel.Children.Add(new TextBlock
            {
                Text = $" [{control.Type}]",
                Foreground = (Brush)_mainWindow.FindResource("TextDim"),
                FontStyle = FontStyles.Italic
            });

            return panel;
        }

        private Brush GetReuseBrush(int occurrenceCount)
        {
            if (occurrenceCount >= 5) return (Brush)_mainWindow.FindResource("SuccessColor");
            if (occurrenceCount >= 3) return (Brush)_mainWindow.FindResource("InfoColor");
            return (Brush)_mainWindow.FindResource("WarningColor");
        }

        private Brush GetControlTypeBrush(string type)
        {
            return type switch
            {
                "TextField" => new SolidColorBrush(Color.FromRgb(33, 150, 243)),
                "Label" => new SolidColorBrush(Color.FromRgb(158, 158, 158)),
                "DropDown" or "ComboBox" => new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                "DatePicker" => new SolidColorBrush(Color.FromRgb(255, 152, 0)),
                "CheckBox" or "RadioButton" => new SolidColorBrush(Color.FromRgb(156, 39, 176)),
                _ => (Brush)_mainWindow.FindResource("TextSecondary")
            };
        }

        public void MinOccurrences_Changed()
        {
            if (_currentReusableAnalysis != null)
            {
                Task.Run(async () => await RefreshReusableViews());
            }
        }

        public void GroupBy_Changed()
        {
            if (_currentReusableAnalysis != null)
            {
                Task.Run(async () => await DisplayReusableGroups());
            }
        }

        public void FilterType_Changed()
        {
            if (_currentReusableAnalysis != null)
            {
                Task.Run(async () => await DisplayReusableGroups());
            }
        }

        public void GroupNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_selectedGroup != null)
            {
                _selectedGroup.SuggestedName = _mainWindow.GroupNameTextBox.Text;
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

        private void ShowEditPanel(ControlDefinition control, TreeViewItem treeItem)
        {
            // Implementation remains the same as in original
            _selectedControl = control;
            _selectedTreeItem = treeItem;
            // ... rest of ShowEditPanel implementation
        }

        private void RemoveFromRepeatingSection(ControlDefinition control, TreeViewItem treeItem)
        {
            // Implementation remains the same as in original
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

                _mainWindow.UpdateStatus("Control removed from repeating section", MessageSeverity.Info);
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

        #region Helper Methods

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

    public class DataColumnWithForm
    {
        public string FormName { get; set; }
        public string ColumnName { get; set; }
        public string Type { get; set; }
        public string DisplayName { get; set; }
        public bool IsRepeating { get; set; }
        public bool IsConditional { get; set; }
        public string RepeatingSection { get; set; }
        public string ConditionalOnField { get; set; }
    }

    #endregion
}