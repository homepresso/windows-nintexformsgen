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
    /// Handles analysis display and reusable views logic
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

        #region Display Analysis Results

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

                    // Build Multi-Form Structure Tree
                    BuildMultiFormStructureTree(_mainWindow._allFormDefinitions);

                    // Display Combined Data Columns with Form Name
                    DisplayCombinedDataColumns(_mainWindow._allFormDefinitions);

                    // Display Hierarchical JSON with form names
                    DisplayHierarchicalJson(_mainWindow._allFormDefinitions);
                });
            });
        }

        private void BuildMultiFormStructureTree(Dictionary<string, InfoPathFormDefinition> allForms)
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

                // Add form metadata summary
                var metadataItem = new TreeViewItem
                {
                    Header = "📊 Form Summary",
                    Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                    FontSize = 11
                };

                metadataItem.Items.Add(new TreeViewItem
                {
                    Header = $"Views: {formDef.Views.Count}",
                    Foreground = (Brush)_mainWindow.FindResource("TextDim"),
                    FontSize = 11
                });

                metadataItem.Items.Add(new TreeViewItem
                {
                    Header = $"Total Controls: {formDef.Metadata.TotalControls}",
                    Foreground = (Brush)_mainWindow.FindResource("TextDim"),
                    FontSize = 11
                });

                metadataItem.Items.Add(new TreeViewItem
                {
                    Header = $"Data Columns: {formDef.Data.Count}",
                    Foreground = (Brush)_mainWindow.FindResource("TextDim"),
                    FontSize = 11
                });

                if (formDef.Metadata.RepeatingSectionCount > 0)
                {
                    metadataItem.Items.Add(new TreeViewItem
                    {
                        Header = $"Repeating Sections: {formDef.Metadata.RepeatingSectionCount} (will be K2 List Views)",
                        Foreground = (Brush)_mainWindow.FindResource("InfoColor"),
                        FontSize = 11
                    });
                }

                if (formDef.Metadata.DynamicSectionCount > 0)
                {
                    metadataItem.Items.Add(new TreeViewItem
                    {
                        Header = $"Dynamic Sections: {formDef.Metadata.DynamicSectionCount} (conditional visibility)",
                        Foreground = (Brush)_mainWindow.FindResource("WarningColor"),
                        FontSize = 11
                    });
                }

                formItem.Items.Add(metadataItem);

                // Add views under the form
                foreach (var view in formDef.Views)
                {
                    var viewItem = new TreeViewItem
                    {
                        Header = $"📄 {view.ViewName}",
                        Tag = view,
                        IsExpanded = false
                    };

                    // Build the tree structure recursively to handle nested sections
                    var rootControls = view.Controls
                        .Where(c => !c.IsMergedIntoParent)
                        .OrderBy(c => c.DocIndex)
                        .ToList();

                    // Build a hierarchy map to understand nesting
                    var processedControls = new HashSet<ControlDefinition>();
                    BuildControlHierarchy(viewItem, rootControls, processedControls, null, null);

                    // Add view-level statistics
                    var viewStatsItem = new TreeViewItem
                    {
                        Header = $"📈 View Statistics",
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
                        viewStatsItem.Items.Add(new TreeViewItem
                        {
                            Header = $"{GetControlIcon(typeGroup.Key)} {typeGroup.Key}: {typeGroup.Count()}",
                            Foreground = (Brush)_mainWindow.FindResource("TextDim"),
                            FontSize = 10
                        });
                    }

                    viewItem.Items.Add(viewStatsItem);
                    formItem.Items.Add(viewItem);
                }

                // Add dynamic sections summary if any
                if (formDef.DynamicSections.Any())
                {
                    var dynamicItem = new TreeViewItem
                    {
                        Header = $"🔄 Dynamic Sections ({formDef.DynamicSections.Count})",
                        IsExpanded = false,
                        Foreground = (Brush)_mainWindow.FindResource("AccentColor")
                    };

                    foreach (var dynSection in formDef.DynamicSections)
                    {
                        var sectionItem = new TreeViewItem
                        {
                            Header = $"🔀 {dynSection.Caption ?? dynSection.Mode}",
                            Tag = dynSection
                        };

                        // Add condition info
                        sectionItem.Items.Add(new TreeViewItem
                        {
                            Header = $"Condition Field: {dynSection.ConditionField ?? "Unknown"}",
                            Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                            FontSize = 11
                        });

                        if (!string.IsNullOrEmpty(dynSection.ConditionValue))
                        {
                            sectionItem.Items.Add(new TreeViewItem
                            {
                                Header = $"Condition Value: {dynSection.ConditionValue}",
                                Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                                FontSize = 11
                            });
                        }

                        if (dynSection.Controls != null && dynSection.Controls.Any())
                        {
                            sectionItem.Items.Add(new TreeViewItem
                            {
                                Header = $"Affected Controls: {dynSection.Controls.Count}",
                                Foreground = (Brush)_mainWindow.FindResource("TextDim"),
                                FontSize = 11
                            });
                        }

                        dynamicItem.Items.Add(sectionItem);
                    }

                    formItem.Items.Add(dynamicItem);
                }

                _mainWindow.StructureTreeView.Items.Add(formItem);
            }
        }

        private void DisplayHierarchicalJson(Dictionary<string, InfoPathFormDefinition> allForms)
        {
            var hierarchicalStructure = new Dictionary<string, object>();

            foreach (var formKvp in allForms)
            {
                var formName = Path.GetFileNameWithoutExtension(formKvp.Key);

                // Use the new ToEnhancedJson method that includes section info in control names
                hierarchicalStructure[formName] = new
                {
                    FileName = formKvp.Key,
                    FormDefinition = formKvp.Value.ToEnhancedJson()  // This now uses the modified extension method
                };
            }

            var json = JsonConvert.SerializeObject(hierarchicalStructure, Formatting.Indented);
            _mainWindow.JsonOutput.Text = json;
        }

        private void DisplayCombinedDataColumns(Dictionary<string, InfoPathFormDefinition> allForms)
        {
            var allColumns = new List<DataColumnWithForm>();

            foreach (var formKvp in allForms)
            {
                var formName = formKvp.Key;
                var formDef = formKvp.Value;

                foreach (var column in formDef.Data)
                {
                    allColumns.Add(new DataColumnWithForm
                    {
                        FormName = formName,
                        ColumnName = column.ColumnName,
                        Type = column.Type,
                        DisplayName = column.DisplayName,
                        IsRepeating = column.IsRepeating,
                        IsConditional = column.IsConditional,
                        RepeatingSection = column.RepeatingSection,
                        ConditionalOnField = column.ConditionalOnField
                    });
                }
            }

            _mainWindow.DataColumnsGrid.ItemsSource = allColumns;
        }

        private TreeViewItem CreateSimplifiedControlTreeItem(ControlDefinition control)
        {
            // Create header with control info
            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };

            // Icon based on control type
            headerPanel.Children.Add(new TextBlock
            {
                Text = GetControlIcon(control.Type) + " ",
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 14
            });

            // Control name/label
            var nameText = !string.IsNullOrEmpty(control.Label) ? control.Label :
                          !string.IsNullOrEmpty(control.Name) ? control.Name : "Unnamed";
            headerPanel.Children.Add(new TextBlock
            {
                Text = nameText,
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

            // Section indicator (if in a section)
            if (!string.IsNullOrEmpty(control.ParentSection) || control.IsInRepeatingSection)
            {
                var sectionText = control.ParentSection ?? control.RepeatingSectionName;
                var sectionBadge = new Border
                {
                    Background = control.IsInRepeatingSection
                        ? new SolidColorBrush(Color.FromArgb(30, 3, 169, 244))  // Light blue for repeating
                        : new SolidColorBrush(Color.FromArgb(30, 76, 175, 80)),  // Light green for regular
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
                headerPanel.Children.Add(sectionBadge);
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

            // Dropdown indicator
            if (control.HasStaticData && control.DataOptions != null && control.DataOptions.Any())
            {
                headerPanel.Children.Add(new TextBlock
                {
                    Text = $" 📋 ({control.DataOptions.Count} options)",
                    Foreground = (Brush)_mainWindow.FindResource("SuccessColor"),
                    ToolTip = $"Has {control.DataOptions.Count} dropdown options",
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
                Margin = new Thickness(5, 0, 0, 0)
            };
            editButton.Click += EditControl_Click;
            headerPanel.Children.Add(editButton);

            var item = new TreeViewItem
            {
                Header = headerPanel,
                Tag = control
            };

            // Add properties as child items (collapsed by default)
            if (!string.IsNullOrEmpty(control.Binding))
            {
                item.Items.Add(new TreeViewItem
                {
                    Header = $"Binding: {control.Binding}",
                    Foreground = (Brush)_mainWindow.FindResource("TextDim"),
                    FontSize = 10
                });
            }

            // Add dropdown values if present
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

            // Add context menu
            var contextMenu = new System.Windows.Controls.ContextMenu();

            var editMenuItem = new MenuItem
            {
                Header = "Edit Control Properties",
                Icon = new TextBlock { Text = "✏️" }
            };
            editMenuItem.Click += (s, e) => ShowEditPanel(control, item);
            contextMenu.Items.Add(editMenuItem);

            if (control.IsInRepeatingSection)
            {
                var removeFromRepeatingMenuItem = new MenuItem
                {
                    Header = "Remove from Repeating Section",
                    Icon = new TextBlock { Text = "➖" }
                };
                removeFromRepeatingMenuItem.Click += (s, e) => RemoveFromRepeatingSection(control, item);
                contextMenu.Items.Add(removeFromRepeatingMenuItem);
            }

            var copyJsonMenuItem = new MenuItem
            {
                Header = "Copy as JSON",
                Icon = new TextBlock { Text = "📋" }
            };
            copyJsonMenuItem.Click += (s, e) => CopyControlAsJson(control);
            contextMenu.Items.Add(copyJsonMenuItem);

            item.ContextMenu = contextMenu;

            return item;
        }

        private object CreateSectionHeader(string sectionName, string sectionType, int controlCount)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };

            // Section icon based on type
            string icon = GetSectionIcon(sectionType);
            panel.Children.Add(new TextBlock
            {
                Text = icon + " ",
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

        private Brush GetSectionTypeBrush(string sectionType)
        {
            return sectionType?.ToLower() switch
            {
                "repeating" => new SolidColorBrush(Color.FromRgb(3, 169, 244)),  // Blue
                "optional" => new SolidColorBrush(Color.FromRgb(255, 152, 0)),   // Orange
                "dynamic" => new SolidColorBrush(Color.FromRgb(156, 39, 176)),   // Purple
                _ => new SolidColorBrush(Color.FromRgb(76, 175, 80))             // Green
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

        private void CopyControlAsJson(ControlDefinition control)
        {
            try
            {
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(new
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
                }, Newtonsoft.Json.Formatting.Indented);

                System.Windows.Clipboard.SetText(json);
                _mainWindow.UpdateStatus("Control JSON copied to clipboard", MessageSeverity.Info);
            }
            catch (Exception ex)
            {
                _mainWindow.UpdateStatus($"Failed to copy JSON: {ex.Message}", MessageSeverity.Error);
            }
        }

        /// <summary>
        /// Recursively builds the control hierarchy to handle nested sections
        /// </summary>
        private void BuildControlHierarchy(TreeViewItem parentItem, List<ControlDefinition> controls,
            HashSet<ControlDefinition> processedControls, string currentSection, string currentRepeatingSection)
        {
            for (int i = 0; i < controls.Count; i++)
            {
                var control = controls[i];

                // Skip if already processed
                if (processedControls.Contains(control))
                    continue;

                // Check if this control belongs to the current context
                bool belongsHere = false;

                if (string.IsNullOrEmpty(currentSection) && string.IsNullOrEmpty(currentRepeatingSection))
                {
                    // We're at root level - take controls that have no parent section and aren't in a repeating section
                    // OR controls that start a new section
                    belongsHere = (string.IsNullOrEmpty(control.ParentSection) && !control.IsInRepeatingSection) ||
                                 (!string.IsNullOrEmpty(control.ParentSection) && control.ParentSection != currentSection) ||
                                 (control.IsInRepeatingSection && control.RepeatingSectionName != currentRepeatingSection);
                }
                else
                {
                    // We're inside a section - check for nested sections or controls belonging to this section
                    if (!string.IsNullOrEmpty(currentRepeatingSection))
                    {
                        // We're in a repeating section
                        belongsHere = control.IsInRepeatingSection && control.RepeatingSectionName == currentRepeatingSection;
                    }
                    else if (!string.IsNullOrEmpty(currentSection))
                    {
                        // We're in a regular section
                        belongsHere = control.ParentSection == currentSection;
                    }
                }

                if (!belongsHere)
                    continue;

                // Check if this control starts a new section group
                bool startsNewSection = false;
                string newSectionName = null;
                string newSectionType = null;

                // Look ahead to see if there are more controls in the same section
                if (!string.IsNullOrEmpty(control.ParentSection) && control.ParentSection != currentSection)
                {
                    // This control starts a new regular section
                    newSectionName = control.ParentSection;
                    newSectionType = control.SectionType ?? "section";
                    startsNewSection = true;
                }
                else if (control.IsInRepeatingSection && control.RepeatingSectionName != currentRepeatingSection)
                {
                    // This control starts a new repeating section
                    newSectionName = control.RepeatingSectionName;
                    newSectionType = "repeating";
                    startsNewSection = true;
                }

                if (startsNewSection && !string.IsNullOrEmpty(newSectionName))
                {
                    // Find all controls in this new section
                    var sectionControls = controls
                        .Where(c => !processedControls.Contains(c) &&
                               ((newSectionType == "repeating" && c.IsInRepeatingSection && c.RepeatingSectionName == newSectionName) ||
                                (newSectionType != "repeating" && c.ParentSection == newSectionName)))
                        .OrderBy(c => c.DocIndex)
                        .ToList();

                    if (sectionControls.Any())
                    {
                        // Create section group item
                        var sectionItem = new TreeViewItem
                        {
                            Header = CreateSectionHeader(newSectionName, newSectionType, CountDirectChildControls(sectionControls)),
                            IsExpanded = false,
                            Tag = newSectionName
                        };

                        // Add color coding based on section type
                        switch (newSectionType?.ToLower())
                        {
                            case "repeating":
                                sectionItem.Foreground = (Brush)_mainWindow.FindResource("InfoColor");
                                break;
                            case "optional":
                                sectionItem.Foreground = (Brush)_mainWindow.FindResource("WarningColor");
                                break;
                            case "dynamic":
                                sectionItem.Foreground = (Brush)_mainWindow.FindResource("AccentColor");
                                break;
                            default:
                                sectionItem.Foreground = (Brush)_mainWindow.FindResource("TextPrimary");
                                break;
                        }

                        // Mark all controls in this section as being processed
                        foreach (var sc in sectionControls)
                        {
                            processedControls.Add(sc);
                        }

                        // Recursively build the hierarchy within this section
                        // This will handle nested sections
                        if (newSectionType == "repeating")
                        {
                            BuildControlHierarchy(sectionItem, sectionControls, processedControls, currentSection, newSectionName);
                        }
                        else
                        {
                            BuildControlHierarchy(sectionItem, sectionControls, processedControls, newSectionName, currentRepeatingSection);
                        }

                        // Add section type info
                        var sectionInfoItem = new TreeViewItem
                        {
                            Header = $"ℹ️ Section Type: {newSectionType}",
                            Foreground = (Brush)_mainWindow.FindResource("TextDim"),
                            FontSize = 10,
                            FontStyle = FontStyles.Italic
                        };
                        sectionItem.Items.Add(sectionInfoItem);

                        parentItem.Items.Add(sectionItem);
                    }
                }
                else
                {
                    // This is a regular control in the current context
                    var controlItem = CreateSimplifiedControlTreeItem(control);
                    parentItem.Items.Add(controlItem);
                    processedControls.Add(control);
                }
            }
        }

        /// <summary>
        /// Counts only direct child controls (not controls in nested sections)
        /// </summary>
        private int CountDirectChildControls(List<ControlDefinition> controls)
        {
            // Count controls that don't belong to a nested section
            var directControls = 0;
            var nestedSections = new HashSet<string>();

            foreach (var control in controls)
            {
                // Check if this control is in a nested section
                bool isNested = false;

                // Look for other controls that might indicate a nested section
                foreach (var other in controls)
                {
                    if (other == control) continue;

                    // If another control has a different section that this control also belongs to, it's nested
                    if (!string.IsNullOrEmpty(other.ParentSection) && other.ParentSection != control.ParentSection)
                    {
                        nestedSections.Add(other.ParentSection);
                    }
                    if (other.IsInRepeatingSection && other.RepeatingSectionName != control.RepeatingSectionName)
                    {
                        nestedSections.Add(other.RepeatingSectionName);
                    }
                }

                directControls++;
            }

            return directControls;
        }

        private TreeViewItem CreateControlTreeItem(ControlDefinition control)
        {
            // Create header with control info and grid position
            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };

            // Icon
            headerPanel.Children.Add(new TextBlock
            {
                Text = GetControlIcon(control.Type) + " ",
                VerticalAlignment = VerticalAlignment.Center
            });

            // Name/Label
            var nameText = !string.IsNullOrEmpty(control.Label) ? control.Label :
                          !string.IsNullOrEmpty(control.Name) ? control.Name : "Unnamed";
            headerPanel.Children.Add(new TextBlock
            {
                Text = nameText + " ",
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center
            });

            // Type
            headerPanel.Children.Add(new TextBlock
            {
                Text = $"[{control.Type}] ",
                Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                VerticalAlignment = VerticalAlignment.Center
            });

            // Grid position
            if (!string.IsNullOrEmpty(control.GridPosition))
            {
                headerPanel.Children.Add(new TextBlock
                {
                    Text = $"📍{control.GridPosition} ",
                    Foreground = (Brush)_mainWindow.FindResource("TextDim"),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            // Repeating indicator
            if (control.IsInRepeatingSection)
            {
                headerPanel.Children.Add(new TextBlock
                {
                    Text = "🔁 ",
                    Foreground = (Brush)_mainWindow.FindResource("InfoColor"),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            // Add dropdown values indicator if present
            if (control.HasStaticData && control.DataOptions != null && control.DataOptions.Any())
            {
                headerPanel.Children.Add(new TextBlock
                {
                    Text = $"📋({control.DataOptions.Count}) ",
                    Foreground = (Brush)_mainWindow.FindResource("SuccessColor"),
                    ToolTip = $"Has {control.DataOptions.Count} dropdown options",
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            // Add Edit button
            var editButton = new Button
            {
                Content = "✏️",
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Padding = new Thickness(5, 0, 5, 0),
                ToolTip = "Edit control properties",
                Tag = control,
                VerticalAlignment = VerticalAlignment.Center
            };
            editButton.Click += EditControl_Click;
            headerPanel.Children.Add(editButton);

            var item = new TreeViewItem
            {
                Header = headerPanel,
                Tag = control
            };

            // Color code based on status
            if (control.IsInRepeatingSection)
            {
                item.Foreground = (Brush)_mainWindow.FindResource("InfoColor");
            }

            // Add detailed information as child nodes
            var detailsItem = new TreeViewItem
            {
                Header = "📊 Details",
                Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                FontSize = 11
            };

            // Add various properties as detail items
            if (!string.IsNullOrEmpty(control.GridPosition))
            {
                detailsItem.Items.Add(new TreeViewItem
                {
                    Header = $"Grid: {control.GridPosition}",
                    Foreground = (Brush)_mainWindow.FindResource("TextDim"),
                    FontSize = 11
                });
            }

            if (!string.IsNullOrEmpty(control.Binding))
            {
                detailsItem.Items.Add(new TreeViewItem
                {
                    Header = $"Binding: {control.Binding}",
                    Foreground = (Brush)_mainWindow.FindResource("TextDim"),
                    FontSize = 11
                });
            }

            if (control.IsInRepeatingSection)
            {
                detailsItem.Items.Add(new TreeViewItem
                {
                    Header = $"Repeating Section: {control.RepeatingSectionName}",
                    Foreground = (Brush)_mainWindow.FindResource("InfoColor"),
                    FontSize = 11
                });
            }

            // Add dropdown values if present
            if (control.HasStaticData && control.DataOptions != null && control.DataOptions.Any())
            {
                var dropdownItem = new TreeViewItem
                {
                    Header = $"📋 Dropdown Values ({control.DataOptions.Count})",
                    Foreground = (Brush)_mainWindow.FindResource("SuccessColor"),
                    FontSize = 11,
                    FontWeight = FontWeights.Medium
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

                detailsItem.Items.Add(dropdownItem);
            }

            if (detailsItem.Items.Count > 0)
            {
                item.Items.Add(detailsItem);
            }

            // Add context menu for right-click editing
            var contextMenu = new System.Windows.Controls.ContextMenu();

            var editMenuItem = new MenuItem
            {
                Header = "Edit Control Properties",
                Icon = new TextBlock { Text = "✏️" }
            };
            editMenuItem.Click += (s, e) => ShowEditPanel(control, item);
            contextMenu.Items.Add(editMenuItem);

            if (control.IsInRepeatingSection)
            {
                var removeFromRepeatingMenuItem = new MenuItem
                {
                    Header = "Remove from Repeating Section",
                    Icon = new TextBlock { Text = "➖" }
                };
                removeFromRepeatingMenuItem.Click += (s, e) => RemoveFromRepeatingSection(control, item);
                contextMenu.Items.Add(removeFromRepeatingMenuItem);
            }

            item.ContextMenu = contextMenu;

            return item;
        }

        #endregion

        #region Reusable Views with Control Groups

        /// <summary>
        /// Refreshes the reusable views analysis using the new control group analyzer
        /// </summary>
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
                // Get the minimum occurrences from the combo box
                int minOccurrences = GetMinOccurrences();

                // UPDATED: Minimum 3 controls to form a meaningful reusable group
                int minGroupSize = 3; // Changed from 2 to 3
                int maxGroupSize = 10; // Maximum 10 controls in a group

                // Run the analysis
                _currentReusableAnalysis = await Task.Run(() =>
                    _reusableAnalyzer.AnalyzeForReusableGroups(
                        _mainWindow._allFormDefinitions,
                        minOccurrences,
                        minGroupSize,
                        maxGroupSize));

                // Display the results
                await DisplayReusableGroups();

                // Update statistics
                UpdateReusableStatistics();

                _mainWindow.SaveReusableViewsButton.IsEnabled = true;

                // Update status message to indicate labels are excluded
                _mainWindow.UpdateStatus($"Found {_currentReusableAnalysis.IdentifiedGroups.Count} reusable control groups " +
                                        $"(min 3 controls, labels excluded, {_currentReusableAnalysis.ControlsInRepeatingSections} controls in repeating sections excluded)",
                                        MessageSeverity.Info);
            }
            catch (Exception ex)
            {
                _mainWindow.UpdateStatus($"Error analyzing reusable views: {ex.Message}",
                                        MessageSeverity.Error);
            }
        }

        /// <summary>
        /// Displays the reusable control groups in the TreeView
        /// </summary>
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

                    // Show info about excluded controls if any
                    if (_currentReusableAnalysis != null && _currentReusableAnalysis.ControlsInRepeatingSections > 0)
                    {
                        var excludedInfo = new TreeViewItem
                        {
                            Header = $"ℹ️ {_currentReusableAnalysis.ControlsInRepeatingSections} controls in repeating sections were excluded (will be separate K2 List Views)",
                            Foreground = (Brush)_mainWindow.FindResource("InfoColor"),
                            FontStyle = FontStyles.Italic
                        };
                        _mainWindow.ReusableControlsTreeView.Items.Add(excludedInfo);
                    }
                    return;
                }

                // Add info about repeating sections at the top
                if (_currentReusableAnalysis.RepeatingSections != null && _currentReusableAnalysis.RepeatingSections.Any())
                {
                    var repeatingSectionGroups = _currentReusableAnalysis.RepeatingSections
                        .GroupBy(r => r.Name)
                        .ToList();

                    var repeatingSectionItem = new TreeViewItem
                    {
                        Header = CreateRepeatingSectionHeader(repeatingSectionGroups.Count),
                        IsExpanded = false,
                        FontWeight = FontWeights.Medium,
                        Foreground = (Brush)_mainWindow.FindResource("InfoColor")
                    };

                    foreach (var rsGroup in repeatingSectionGroups)
                    {
                        var sectionItem = new TreeViewItem
                        {
                            Header = $"🔁 {rsGroup.Key} ({rsGroup.Count()} form(s), {rsGroup.First().ControlCount} controls)",
                            FontStyle = FontStyles.Italic
                        };

                        foreach (var form in rsGroup)
                        {
                            var formItem = new TreeViewItem
                            {
                                Header = $"  • {form.FormName}: {string.Join(", ", form.ControlTypes)}",
                                FontSize = 11,
                                Foreground = (Brush)_mainWindow.FindResource("TextSecondary")
                            };
                            sectionItem.Items.Add(formItem);
                        }

                        repeatingSectionItem.Items.Add(sectionItem);
                    }

                    _mainWindow.ReusableControlsTreeView.Items.Add(repeatingSectionItem);

                    // Add separator
                    var separator = new TreeViewItem
                    {
                        Header = "─────────────────────────────────",
                        IsEnabled = false,
                        Foreground = (Brush)_mainWindow.FindResource("BorderColor")
                    };
                    _mainWindow.ReusableControlsTreeView.Items.Add(separator);
                }

                // Group by suggested name pattern
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

                        // Add control details
                        foreach (var control in group.Controls)
                        {
                            var controlItem = new TreeViewItem
                            {
                                Header = CreateControlHeader(control),
                                FontSize = 11
                            };
                            groupItem.Items.Add(controlItem);
                        }

                        // Add forms where this group appears
                        var formsItem = new TreeViewItem
                        {
                            Header = $"📁 Found in {group.OccurrenceCount} forms",
                            FontStyle = FontStyles.Italic,
                            Foreground = (Brush)_mainWindow.FindResource("TextSecondary")
                        };

                        foreach (var formName in group.FoundInForms)
                        {
                            var formItem = new TreeViewItem
                            {
                                Header = $"  • {formName}",
                                FontSize = 11
                            };
                            formsItem.Items.Add(formItem);
                        }
                        groupItem.Items.Add(formsItem);

                        // Add selection event handler
                        groupItem.Selected += GroupItem_Selected;

                        categoryItem.Items.Add(groupItem);
                    }

                    _mainWindow.ReusableControlsTreeView.Items.Add(categoryItem);
                }
            });
        }

        /// <summary>
        /// Handles selection of a control group
        /// </summary>
        private void GroupItem_Selected(object sender, RoutedEventArgs e)
        {
            var item = sender as TreeViewItem;
            if (item?.Tag is ReusableControlGroupAnalyzer.ControlGroup group)
            {
                _selectedGroup = group;
                DisplayGroupDetails(group);

                // Enable action buttons
                _mainWindow.GroupNameTextBox.IsEnabled = true;
                _mainWindow.GroupNameTextBox.Text = group.SuggestedName;
                _mainWindow.CreateReusableViewButton.IsEnabled = true;
                _mainWindow.ExportReusableControlsButton.IsEnabled = true;

                // Stop the event from bubbling up to parent items
                e.Handled = true;
            }
        }

        /// <summary>
        /// Displays detailed information about a selected group
        /// </summary>
        private void DisplayGroupDetails(ReusableControlGroupAnalyzer.ControlGroup group)
        {
            _mainWindow.ReusableDetailsPanel.Children.Clear();

            // Group summary
            var summaryText = new TextBlock
            {
                Text = $"Control Group: {group.SuggestedName}",
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary"),
                Margin = new Thickness(0, 0, 0, 10)
            };
            _mainWindow.ReusableDetailsPanel.Children.Add(summaryText);

            // Occurrence info
            var occurrenceText = new TextBlock
            {
                Text = $"Appears in {group.OccurrenceCount} of {_currentReusableAnalysis.TotalFormsAnalyzed} forms ({(group.OccurrenceCount * 100.0 / _currentReusableAnalysis.TotalFormsAnalyzed):F1}%)",
                Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                Margin = new Thickness(0, 0, 0, 5)
            };
            _mainWindow.ReusableDetailsPanel.Children.Add(occurrenceText);

            // Control count - emphasize that these are input controls only
            var controlCountText = new TextBlock
            {
                Text = $"Contains {group.Controls.Count} input controls (labels and repeating sections excluded)",
                Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                Margin = new Thickness(0, 0, 0, 10)
            };
            _mainWindow.ReusableDetailsPanel.Children.Add(controlCountText);

            // Controls list
            var controlsHeader = new TextBlock
            {
                Text = "Input controls in this group:",
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary"),
                Margin = new Thickness(0, 10, 0, 5)
            };
            _mainWindow.ReusableDetailsPanel.Children.Add(controlsHeader);

            int index = 1;
            foreach (var control in group.Controls)
            {
                var controlBorder = new Border
                {
                    Background = (Brush)_mainWindow.FindResource("BackgroundMedium"),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 5, 8, 5),
                    Margin = new Thickness(0, 2, 0, 2)
                };

                var controlStack = new StackPanel();

                var controlText = new TextBlock
                {
                    Text = $"{index}. {control.Label ?? control.Name}",
                    Foreground = (Brush)_mainWindow.FindResource("TextPrimary"),
                    FontSize = 12
                };
                controlStack.Children.Add(controlText);

                var typeText = new TextBlock
                {
                    Text = $"   Type: {control.Type}",
                    Foreground = (Brush)_mainWindow.FindResource("TextDim"),
                    FontSize = 11
                };
                controlStack.Children.Add(typeText);

                controlBorder.Child = controlStack;
                _mainWindow.ReusableDetailsPanel.Children.Add(controlBorder);
                index++;
            }

            // Suggested K2 implementation
            var implementationHeader = new TextBlock
            {
                Text = "K2 Implementation:",
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary"),
                Margin = new Thickness(0, 15, 0, 5)
            };
            _mainWindow.ReusableDetailsPanel.Children.Add(implementationHeader);

            var implementationText = new TextBlock
            {
                Text = GetK2ImplementationSuggestion(group),
                Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11
            };
            _mainWindow.ReusableDetailsPanel.Children.Add(implementationText);
        }

        /// <summary>
        /// Updates the statistics panel
        /// </summary>
        private void UpdateReusableStatistics()
        {
            // ReusableStatisticsPanel is a WrapPanel, so we can use Children
            var panel = _mainWindow.ReusableStatisticsPanel as WrapPanel;
            if (panel != null)
                panel.Children.Clear();

            if (_currentReusableAnalysis == null)
                return;

            // Total groups card
            AddStatCard("📊", "Total Groups",
                       _currentReusableAnalysis.IdentifiedGroups.Count.ToString(),
                       (Brush)_mainWindow.FindResource("AccentColor"));

            // High reuse groups (3+ forms)
            var highReuseCount = _currentReusableAnalysis.IdentifiedGroups
                .Count(g => g.OccurrenceCount >= 3);
            AddStatCard("🔄", "High Reuse (3+)",
                       highReuseCount.ToString(),
                       (Brush)_mainWindow.FindResource("SuccessColor"));

            // Total controls analyzed (excluding repeating)
            AddStatCard("🎛️", "Controls Analyzed",
                       _currentReusableAnalysis.TotalControlsAnalyzed.ToString(),
                       (Brush)_mainWindow.FindResource("InfoColor"));

            // Controls in repeating sections (excluded)
            if (_currentReusableAnalysis.ControlsInRepeatingSections > 0)
            {
                AddStatCard("🔁", "Excluded (Repeating)",
                           _currentReusableAnalysis.ControlsInRepeatingSections.ToString(),
                           new SolidColorBrush(Color.FromRgb(255, 152, 0))); // Orange
            }

            // Forms analyzed
            AddStatCard("📄", "Forms Analyzed",
                       _currentReusableAnalysis.TotalFormsAnalyzed.ToString(),
                       (Brush)_mainWindow.FindResource("WarningColor"));

            // Average group size
            if (_currentReusableAnalysis.IdentifiedGroups.Any())
            {
                var avgSize = _currentReusableAnalysis.IdentifiedGroups
                    .Average(g => g.Controls.Count);
                AddStatCard("📏", "Avg Group Size",
                           $"{avgSize:F1}",
                           new SolidColorBrush(Color.FromRgb(156, 39, 176))); // Purple
            }

            // Repeating sections count (will be separate reusable views)
            if (_currentReusableAnalysis.RepeatingSections != null && _currentReusableAnalysis.RepeatingSections.Any())
            {
                var uniqueRepeatingSections = _currentReusableAnalysis.RepeatingSections
                    .GroupBy(r => r.Name)
                    .Count();
                AddStatCard("📦", "Repeating Sections",
                           uniqueRepeatingSections.ToString(),
                           new SolidColorBrush(Color.FromRgb(0, 188, 212))); // Cyan
            }
        }

        /// <summary>
        /// Adds a statistics card to the panel
        /// </summary>
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

            // Add to the WrapPanel
            var panel = _mainWindow.ReusableStatisticsPanel as WrapPanel;
            if (panel != null)
                panel.Children.Add(card);
        }

        #region Event Handlers

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
                            ControlsInRepeatingSections = _currentReusableAnalysis.ControlsInRepeatingSections,
                            TotalGroups = _currentReusableAnalysis.IdentifiedGroups.Count,
                            RepeatingSections = _currentReusableAnalysis.RepeatingSections?.Count ?? 0
                        },
                        ReusableControlGroups = _currentReusableAnalysis.IdentifiedGroups,
                        RepeatingSections = _currentReusableAnalysis.RepeatingSections,
                        CommonPatterns = _currentReusableAnalysis.CommonPatterns
                    };

                    var json = JsonConvert.SerializeObject(exportData, Formatting.Indented);
                    await File.WriteAllTextAsync(dialog.FileName, json);

                    _mainWindow.UpdateStatus($"Saved reusable views analysis to {dialog.FileName}",
                                           MessageSeverity.Info);
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

            // This would integrate with K2 API to create the actual view
            MessageBox.Show($"Creating K2 SmartForm view: {_selectedGroup.SuggestedName}\n\n" +
                          $"This would create a reusable Item View with {_selectedGroup.Controls.Count} controls " +
                          $"that can be used across {_selectedGroup.OccurrenceCount} forms.\n\n" +
                          $"Note: Controls from repeating sections are handled separately as K2 List Views.",
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

                    _mainWindow.UpdateStatus($"Exported control group to {dialog.FileName}",
                                           MessageSeverity.Info);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error exporting: {ex.Message}", "Export Error",
                                  MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #endregion

        #region Control Editing

        private void EditControl_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var control = button?.Tag as ControlDefinition;
            if (control != null)
            {
                // Find the tree item that contains this control
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
            _selectedControl = control;
            _selectedTreeItem = treeItem;
            _currentFormDefinitions = _mainWindow._allFormDefinitions;

            // Create or update the edit panel
            var editWindow = new Window
            {
                Title = "Edit Control Properties",
                Width = 500,
                Height = 600,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = _mainWindow,
                Background = (Brush)_mainWindow.FindResource("BackgroundDark")
            };

            var mainGrid = new Grid();
            mainGrid.Margin = new Thickness(20);
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Title
            var titleText = new TextBlock
            {
                Text = "Edit Control Properties",
                FontSize = 18,
                FontWeight = FontWeights.Medium,
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary"),
                Margin = new Thickness(0, 0, 0, 20)
            };
            Grid.SetRow(titleText, 0);
            mainGrid.Children.Add(titleText);

            // Properties panel
            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(scrollViewer, 1);

            var propertiesStack = new StackPanel();
            _propertyEditors.Clear();

            // Control Name
            AddEditField(propertiesStack, "Name:", control.Name, "Name");

            // Control Label
            AddEditField(propertiesStack, "Label:", control.Label, "Label");

            // Control Type (ComboBox)
            AddTypeComboBox(propertiesStack, control.Type);

            // Grid Position
            AddEditField(propertiesStack, "Grid Position:", control.GridPosition, "GridPosition");

            // Binding
            AddEditField(propertiesStack, "Binding:", control.Binding, "Binding");

            // Repeating Section Management
            AddRepeatingSectionControls(propertiesStack, control);

            // Dropdown Values (if applicable)
            if (control.HasStaticData && control.DataOptions != null)
            {
                AddDropdownValuesEditor(propertiesStack, control);
            }

            scrollViewer.Content = propertiesStack;
            mainGrid.Children.Add(scrollViewer);

            // Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 20, 0, 0)
            };
            Grid.SetRow(buttonPanel, 2);

            var saveButton = new Button
            {
                Content = "Save Changes",
                Style = (Style)_mainWindow.FindResource("ModernButton"),
                Width = 120,
                Margin = new Thickness(0, 0, 10, 0)
            };
            saveButton.Click += (s, e) => SaveControlChanges(editWindow);
            buttonPanel.Children.Add(saveButton);

            var cancelButton = new Button
            {
                Content = "Cancel",
                Style = (Style)_mainWindow.FindResource("ModernButton"),
                Width = 120,
                Background = (Brush)_mainWindow.FindResource("BorderColor")
            };
            cancelButton.Click += (s, e) => editWindow.Close();
            buttonPanel.Children.Add(cancelButton);

            mainGrid.Children.Add(buttonPanel);
            editWindow.Content = mainGrid;
            editWindow.ShowDialog();
        }

        private void AddEditField(StackPanel parent, string label, string value, string propertyName)
        {
            var labelText = new TextBlock
            {
                Text = label,
                Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                Margin = new Thickness(0, 10, 0, 5)
            };
            parent.Children.Add(labelText);

            var textBox = new TextBox
            {
                Text = value ?? "",
                Style = (Style)_mainWindow.FindResource("ModernTextBox"),
                Margin = new Thickness(0, 0, 0, 10)
            };
            _propertyEditors[propertyName] = textBox;
            parent.Children.Add(textBox);
        }

        private void AddTypeComboBox(StackPanel parent, string currentType)
        {
            var labelText = new TextBlock
            {
                Text = "Control Type:",
                Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                Margin = new Thickness(0, 10, 0, 5)
            };
            parent.Children.Add(labelText);

            var typeCombo = new ComboBox
            {
                Style = (Style)_mainWindow.FindResource("ModernComboBox"),
                Margin = new Thickness(0, 0, 0, 10)
            };

            var controlTypes = new[]
            {
                "TextField", "RichText", "DropDown", "ComboBox", "CheckBox",
                "DatePicker", "PeoplePicker", "FileAttachment", "Button",
                "RadioButton", "Label", "Hyperlink", "InlinePicture", "SignatureLine"
            };

            foreach (var type in controlTypes)
            {
                var item = new ComboBoxItem { Content = type };
                if (type == currentType)
                    item.IsSelected = true;
                typeCombo.Items.Add(item);
            }

            _propertyEditors["Type"] = new TextBox { Text = currentType }; // Store as TextBox for consistency
            typeCombo.SelectionChanged += (s, e) =>
            {
                var selected = (typeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();
                if (selected != null)
                    _propertyEditors["Type"].Text = selected;
            };

            parent.Children.Add(typeCombo);
        }

        private void AddRepeatingSectionControls(StackPanel parent, ControlDefinition control)
        {
            var sectionLabel = new TextBlock
            {
                Text = "Repeating Section Management:",
                FontWeight = FontWeights.Medium,
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary"),
                Margin = new Thickness(0, 20, 0, 10)
            };
            parent.Children.Add(sectionLabel);

            // Current section status
            var statusText = new TextBlock
            {
                Text = control.IsInRepeatingSection
                    ? $"Currently in: {control.RepeatingSectionName}"
                    : "Not in a repeating section",
                Foreground = control.IsInRepeatingSection
                    ? (Brush)_mainWindow.FindResource("InfoColor")
                    : (Brush)_mainWindow.FindResource("TextSecondary"),
                Margin = new Thickness(0, 0, 0, 10)
            };
            parent.Children.Add(statusText);

            // Options
            var optionsPanel = new StackPanel();

            // Remove from repeating section checkbox
            if (control.IsInRepeatingSection)
            {
                var removeCheckBox = new CheckBox
                {
                    Content = "Remove from repeating section",
                    Style = (Style)_mainWindow.FindResource("ModernCheckBox"),
                    Margin = new Thickness(0, 5, 0, 5)
                };
                _propertyEditors["RemoveFromRepeating"] = new TextBox { Text = "false" };
                removeCheckBox.Checked += (s, e) => _propertyEditors["RemoveFromRepeating"].Text = "true";
                removeCheckBox.Unchecked += (s, e) => _propertyEditors["RemoveFromRepeating"].Text = "false";
                optionsPanel.Children.Add(removeCheckBox);
            }

            // Move to different repeating section
            var moveLabel = new TextBlock
            {
                Text = "Move to repeating section:",
                Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                Margin = new Thickness(0, 10, 0, 5)
            };
            optionsPanel.Children.Add(moveLabel);

            var sectionCombo = new ComboBox
            {
                Style = (Style)_mainWindow.FindResource("ModernComboBox"),
                Margin = new Thickness(0, 0, 0, 10)
            };

            // Add "None" option
            sectionCombo.Items.Add(new ComboBoxItem { Content = "(None)" });

            // Get all available repeating sections from the current form
            var repeatingSections = GetAllRepeatingSections();
            foreach (var section in repeatingSections)
            {
                var item = new ComboBoxItem { Content = section };
                if (section == control.RepeatingSectionName)
                    item.IsSelected = true;
                sectionCombo.Items.Add(item);
            }

            _propertyEditors["NewRepeatingSection"] = new TextBox { Text = control.RepeatingSectionName ?? "" };
            sectionCombo.SelectionChanged += (s, e) =>
            {
                var selected = (sectionCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();
                _propertyEditors["NewRepeatingSection"].Text = selected == "(None)" ? "" : (selected ?? "");
            };

            optionsPanel.Children.Add(sectionCombo);
            parent.Children.Add(optionsPanel);
        }

        private void AddDropdownValuesEditor(StackPanel parent, ControlDefinition control)
        {
            var valuesLabel = new TextBlock
            {
                Text = "Dropdown Values:",
                FontWeight = FontWeights.Medium,
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary"),
                Margin = new Thickness(0, 20, 0, 10)
            };
            parent.Children.Add(valuesLabel);

            var valuesPanel = new Border
            {
                Background = (Brush)_mainWindow.FindResource("BackgroundLight"),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10),
                MaxHeight = 200
            };

            var valuesScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var valuesStack = new StackPanel();

            foreach (var option in control.DataOptions.OrderBy(o => o.Order))
            {
                var optionPanel = new Grid();
                optionPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                optionPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                optionPanel.Margin = new Thickness(0, 2, 0, 0);

                var optionText = new TextBlock
                {
                    Text = $"• {option.DisplayText}",
                    Foreground = (Brush)_mainWindow.FindResource("TextPrimary"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(optionText, 0);
                optionPanel.Children.Add(optionText);

                if (option.IsDefault)
                {
                    var defaultBadge = new Border
                    {
                        Background = (Brush)_mainWindow.FindResource("SuccessColor"),
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(5, 2, 5, 2)
                    };
                    defaultBadge.Child = new TextBlock
                    {
                        Text = "Default",
                        Foreground = Brushes.White,
                        FontSize = 10
                    };
                    Grid.SetColumn(defaultBadge, 1);
                    optionPanel.Children.Add(defaultBadge);
                }

                valuesStack.Children.Add(optionPanel);
            }

            valuesScroll.Content = valuesStack;
            valuesPanel.Child = valuesScroll;
            parent.Children.Add(valuesPanel);

            // Note about editing dropdown values
            var noteText = new TextBlock
            {
                Text = "Note: Dropdown values are preserved from the original form and cannot be edited here.",
                Foreground = (Brush)_mainWindow.FindResource("TextDim"),
                FontStyle = FontStyles.Italic,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 5, 0, 0)
            };
            parent.Children.Add(noteText);
        }

        private List<string> GetAllRepeatingSections()
        {
            var sections = new HashSet<string>();

            if (_mainWindow._allFormDefinitions != null)
            {
                foreach (var formDef in _mainWindow._allFormDefinitions.Values)
                {
                    // Get from sections
                    foreach (var view in formDef.Views)
                    {
                        foreach (var section in view.Sections.Where(s => s.Type == "repeating"))
                        {
                            sections.Add(section.Name);
                        }
                    }

                    // Get from controls
                    foreach (var view in formDef.Views)
                    {
                        foreach (var control in view.Controls)
                        {
                            if (!string.IsNullOrEmpty(control.RepeatingSectionName))
                            {
                                sections.Add(control.RepeatingSectionName);
                            }
                        }
                    }
                }
            }

            return sections.OrderBy(s => s).ToList();
        }

        private void SaveControlChanges(Window editWindow)
        {
            if (_selectedControl == null) return;

            // Apply changes to the control
            if (_propertyEditors.ContainsKey("Name"))
                _selectedControl.Name = _propertyEditors["Name"].Text;

            if (_propertyEditors.ContainsKey("Label"))
                _selectedControl.Label = _propertyEditors["Label"].Text;

            if (_propertyEditors.ContainsKey("Type"))
                _selectedControl.Type = _propertyEditors["Type"].Text;

            if (_propertyEditors.ContainsKey("GridPosition"))
                _selectedControl.GridPosition = _propertyEditors["GridPosition"].Text;

            if (_propertyEditors.ContainsKey("Binding"))
                _selectedControl.Binding = _propertyEditors["Binding"].Text;

            // Handle repeating section changes
            if (_propertyEditors.ContainsKey("RemoveFromRepeating") &&
                _propertyEditors["RemoveFromRepeating"].Text == "true")
            {
                _selectedControl.IsInRepeatingSection = false;
                _selectedControl.RepeatingSectionName = null;
                _selectedControl.RepeatingSectionBinding = null;
            }
            else if (_propertyEditors.ContainsKey("NewRepeatingSection"))
            {
                var newSection = _propertyEditors["NewRepeatingSection"].Text;
                if (!string.IsNullOrEmpty(newSection))
                {
                    _selectedControl.IsInRepeatingSection = true;
                    _selectedControl.RepeatingSectionName = newSection;
                }
                else if (newSection == "")
                {
                    _selectedControl.IsInRepeatingSection = false;
                    _selectedControl.RepeatingSectionName = null;
                    _selectedControl.RepeatingSectionBinding = null;
                }
            }

            // Update the tree view
            RefreshTreeView();

            // UPDATE: Add JSON output refresh
            UpdateJsonOutput();

            // UPDATE: Add Data Columns grid refresh
            UpdateDataColumnsGrid();

            editWindow.Close();

            _mainWindow.UpdateStatus("Control properties updated successfully and JSON refreshed", MessageSeverity.Info);
        }

        private void RemoveFromRepeatingSection(ControlDefinition control, TreeViewItem treeItem)
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

                RefreshTreeView();

                // UPDATE: Add JSON output refresh
                UpdateJsonOutput();

                // UPDATE: Add Data Columns grid refresh
                UpdateDataColumnsGrid();

                _mainWindow.UpdateStatus("Control removed from repeating section and JSON updated", MessageSeverity.Info);
            }
        }

        /// <summary>
        /// Updates the JSON output tab with the current form definitions
        /// </summary>
        private void UpdateJsonOutput()
        {
            // Update the JSON output with the modified form definitions
            if (_mainWindow._allFormDefinitions != null && _mainWindow._allFormDefinitions.Any())
            {
                DisplayHierarchicalJson(_mainWindow._allFormDefinitions);

                // Log to console for debugging
                System.Diagnostics.Debug.WriteLine("JSON Output updated after control edit");
            }
        }

        /// <summary>
        /// Updates the Data Columns grid to reflect control property changes
        /// </summary>
        private void UpdateDataColumnsGrid()
        {
            // Rebuild the data columns to reflect any changes in control properties
            if (_mainWindow._allFormDefinitions != null && _mainWindow._allFormDefinitions.Any())
            {
                // First, update the Data collection in each form definition
                // to reflect the control changes
                foreach (var formKvp in _mainWindow._allFormDefinitions)
                {
                    UpdateFormDataColumns(formKvp.Value);

                    // Log for debugging
                    System.Diagnostics.Debug.WriteLine($"Updated data columns for form: {formKvp.Key}");
                }

                // Then refresh the grid display
                DisplayCombinedDataColumns(_mainWindow._allFormDefinitions);

                System.Diagnostics.Debug.WriteLine("Data Columns grid refreshed after control edit");
            }
        }

        private void UpdateFormDataColumns(InfoPathFormDefinition formDef)
        {
            // Rebuild the Data collection based on current control properties
            var updatedColumns = new List<DataColumn>();
            var processedColumns = new HashSet<string>();

            foreach (var view in formDef.Views)
            {
                foreach (var control in view.Controls.Where(c => !c.IsMergedIntoParent && c.Type != "Label"))
                {
                    // Create a unique key for this column considering its context
                    string columnKey = $"{control.Name ?? control.Binding}_{control.IsInRepeatingSection}_{control.RepeatingSectionName}";

                    // Skip if we've already processed this column or if it has no name/binding
                    if (!processedColumns.Contains(columnKey) && !string.IsNullOrWhiteSpace(control.Name ?? control.Binding))
                    {
                        processedColumns.Add(columnKey);

                        var dataColumn = new DataColumn
                        {
                            ColumnName = control.Name ?? control.Binding,
                            Type = control.Type,
                            DisplayName = control.Label ?? control.Name,
                            IsRepeating = control.IsInRepeatingSection,
                            RepeatingSectionPath = control.RepeatingSectionName,
                            RepeatingSection = control.RepeatingSectionName,
                            IsConditional = false // This would need more complex logic to determine
                        };

                        // Check if this control was previously marked as conditional
                        var existingColumn = formDef.Data.FirstOrDefault(d =>
                            d.ColumnName == dataColumn.ColumnName &&
                            d.RepeatingSection == dataColumn.RepeatingSection);

                        if (existingColumn != null)
                        {
                            // Preserve conditional status from existing column
                            dataColumn.IsConditional = existingColumn.IsConditional;
                            dataColumn.ConditionalOnField = existingColumn.ConditionalOnField;
                        }

                        // Copy over dropdown values if present
                        if (control.HasStaticData && control.DataOptions != null && control.DataOptions.Any())
                        {
                            dataColumn.ValidValues = new List<DataOption>(control.DataOptions);
                            var defaultOption = control.DataOptions.FirstOrDefault(o => o.IsDefault);
                            if (defaultOption != null)
                            {
                                dataColumn.DefaultValue = defaultOption.Value;
                            }
                        }

                        updatedColumns.Add(dataColumn);
                    }
                }
            }

            // Update the form definition's Data collection
            formDef.Data = updatedColumns;

            // Update metadata counts to reflect any changes
            UpdateFormMetadata(formDef);
        }

        private void UpdateFormMetadata(InfoPathFormDefinition formDef)
        {
            // Recalculate total controls (excluding merged labels)
            formDef.Metadata.TotalControls = formDef.Views
                .Sum(v => v.Controls.Count(c => !c.IsMergedIntoParent));

            // Recalculate repeating sections count
            var repeatingSectionNames = new HashSet<string>();

            // Count unique repeating section names from controls
            foreach (var view in formDef.Views)
            {
                foreach (var control in view.Controls)
                {
                    if (control.IsInRepeatingSection && !string.IsNullOrEmpty(control.RepeatingSectionName))
                    {
                        repeatingSectionNames.Add(control.RepeatingSectionName);
                    }
                }

                // Also count from sections
                foreach (var section in view.Sections.Where(s => s.Type == "repeating"))
                {
                    repeatingSectionNames.Add(section.Name);
                }
            }

            formDef.Metadata.RepeatingSectionCount = repeatingSectionNames.Count;

            // Update total sections count
            formDef.Metadata.TotalSections = formDef.Views
                .SelectMany(v => v.Sections)
                .Select(s => s.Name)
                .Distinct()
                .Count();

            // Update conditional fields list
            var conditionalFields = new HashSet<string>();
            foreach (var dataCol in formDef.Data.Where(d => d.IsConditional))
            {
                if (!string.IsNullOrEmpty(dataCol.ConditionalOnField))
                {
                    conditionalFields.Add(dataCol.ConditionalOnField);
                }
            }
            formDef.Metadata.ConditionalFields = conditionalFields.ToList();

            System.Diagnostics.Debug.WriteLine($"Updated metadata - Controls: {formDef.Metadata.TotalControls}, " +
                                             $"Repeating Sections: {formDef.Metadata.RepeatingSectionCount}");
        }

        private void RefreshTreeView()
        {
            // Store the current expansion state with full path
            var expansionState = new Dictionary<string, bool>();
            StoreExpansionStateWithPath(_mainWindow.StructureTreeView.Items, "", expansionState);

            // Rebuild the tree
            BuildMultiFormStructureTree(_mainWindow._allFormDefinitions);

            // Restore expansion state using paths
            RestoreExpansionStateWithPath(_mainWindow.StructureTreeView.Items, "", expansionState);
        }

        private void RestoreExpansionStateWithPath(ItemCollection items, string parentPath, Dictionary<string, bool> expansionState)
        {
            foreach (TreeViewItem item in items)
            {
                // Build the path for this item
                string itemPath = BuildItemPath(parentPath, item);

                // Restore the expansion state if we have it stored
                if (expansionState.ContainsKey(itemPath))
                {
                    item.IsExpanded = expansionState[itemPath];
                }
                else
                {
                    // Default to collapsed if we don't have state for this item
                    item.IsExpanded = false;
                }

                // Recursively restore children's state
                if (item.Items.Count > 0)
                {
                    RestoreExpansionStateWithPath(item.Items, itemPath, expansionState);
                }
            }
        }

        private string BuildItemPath(string parentPath, TreeViewItem item)
        {
            // Get a string representation of the header
            string headerText = GetHeaderText(item);

            // If the item has a tag, use it to make the path more unique
            if (item.Tag != null)
            {
                if (item.Tag is ControlDefinition control)
                {
                    headerText = $"Control_{control.Name ?? control.Binding ?? headerText}";
                }
                else if (item.Tag is ViewDefinition view)
                {
                    headerText = $"View_{view.ViewName}";
                }
                else if (item.Tag is SectionInfo section)
                {
                    headerText = $"Section_{section.Name}";
                }
                else if (item.Tag is InfoPathFormDefinition form)
                {
                    headerText = $"Form_{headerText}";
                }
            }

            // Build the full path
            return string.IsNullOrEmpty(parentPath)
                ? headerText
                : $"{parentPath}/{headerText}";
        }

        private string GetHeaderText(TreeViewItem item)
        {
            if (item.Header == null)
                return "Unknown";

            // If header is a string, return it directly
            if (item.Header is string headerString)
                return headerString;

            // If header is a StackPanel (our custom headers), extract text
            if (item.Header is StackPanel panel)
            {
                var textBlocks = panel.Children.OfType<TextBlock>();
                var texts = textBlocks.Select(tb => tb.Text?.Trim()).Where(t => !string.IsNullOrEmpty(t));
                return string.Join("_", texts);
            }

            // Default: use ToString
            return item.Header.ToString();
        }

        public void RefreshAllOutputs()
        {
            RefreshTreeView();
            UpdateJsonOutput();
            UpdateDataColumnsGrid();

            _mainWindow.UpdateStatus("All outputs refreshed", MessageSeverity.Info);
        }

        private void StoreExpansionStateWithPath(ItemCollection items, string parentPath, Dictionary<string, bool> expansionState)
        {
            foreach (TreeViewItem item in items)
            {
                // Build a unique path for this item
                string itemPath = BuildItemPath(parentPath, item);

                // Store whether this item is expanded
                expansionState[itemPath] = item.IsExpanded;

                // Recursively store children's state
                if (item.Items.Count > 0)
                {
                    StoreExpansionStateWithPath(item.Items, itemPath, expansionState);
                }
            }
        }

        private void RestoreExpansionState(ItemCollection items, List<string> expandedItems)
        {
            foreach (TreeViewItem item in items)
            {
                if (item.Header != null && expandedItems.Contains(item.Header.ToString()))
                {
                    item.IsExpanded = true;
                }
                RestoreExpansionState(item.Items, expandedItems);
            }
        }

        #endregion

        #region Helper Methods

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

            // Reuse indicator
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

            // Group name
            panel.Children.Add(new TextBlock
            {
                Text = group.SuggestedName,
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary"),
                VerticalAlignment = VerticalAlignment.Center
            });

            // Control count
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

            // Control type icon
            var typeIcon = GetControlIcon(control.Type);
            panel.Children.Add(new TextBlock
            {
                Text = typeIcon + " ",
                Foreground = GetControlTypeBrush(control.Type),
                FontSize = 14
            });

            // Control label
            panel.Children.Add(new TextBlock
            {
                Text = control.Label ?? control.Name ?? "Unnamed",
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary")
            });

            // Control type
            panel.Children.Add(new TextBlock
            {
                Text = $" [{control.Type}]",
                Foreground = (Brush)_mainWindow.FindResource("TextDim"),
                FontStyle = FontStyles.Italic
            });

            return panel;
        }

        private object CreateRepeatingSectionHeader(int count)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };

            panel.Children.Add(new TextBlock
            {
                Text = "📦 ",
                FontSize = 16,
                Foreground = (Brush)_mainWindow.FindResource("InfoColor")
            });

            panel.Children.Add(new TextBlock
            {
                Text = $"Repeating Sections ",
                FontWeight = FontWeights.Medium,
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary")
            });

            panel.Children.Add(new TextBlock
            {
                Text = $"({count} unique sections - will be separate K2 List Views)",
                Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
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

        private string GetK2ImplementationSuggestion(ReusableControlGroupAnalyzer.ControlGroup group)
        {
            var suggestion = new StringBuilder();

            suggestion.AppendLine($"This control group appears in {group.OccurrenceCount} forms and contains {group.Controls.Count} input controls.");
            suggestion.AppendLine();
            suggestion.AppendLine("Recommended implementation:");
            suggestion.AppendLine("• Create a SmartObject with properties for each input control");
            suggestion.AppendLine("• Build a reusable Item View with these controls");
            suggestion.AppendLine("• Use subviews or subforms to include in multiple forms");
            suggestion.AppendLine();
            suggestion.AppendLine("Note: This group contains only input controls (minimum 3 required). Labels and repeating sections are handled separately.");

            if (group.OccurrenceCount >= 4)
            {
                suggestion.AppendLine();
                suggestion.AppendLine("⭐ High reuse potential - This will significantly reduce development time!");
            }

            return suggestion.ToString();
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

            // Icon at top
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

            // Large value
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

            // Title
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

            // Description
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

        #endregion
    }

    #region Model Classes

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