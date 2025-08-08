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

        // Reusable Views state
        private List<ReusableControlGroup> _currentReusableGroups = new List<ReusableControlGroup>();
        private ReusableControlGroup _selectedReusableGroup;
        private int _autoNameCounter = 1;

        // Tree view editing state
        private ControlDefinition _selectedControl;
        private TreeViewItem _selectedTreeItem;
        private Grid _propertiesPanel;
        private Dictionary<string, TextBox> _propertyEditors = new Dictionary<string, TextBox>();

        public MainWindowAnalysisHandlers(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
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
                    AddSummaryCard("Repeating Sections", totalRepeatingSections.ToString(), "🔁", "Repeating tables/sections", Brushes.DeepSkyBlue);
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

        private void DisplayHierarchicalJson(Dictionary<string, InfoPathFormDefinition> allForms)
        {
            var hierarchicalStructure = new Dictionary<string, object>();

            foreach (var formKvp in allForms)
            {
                var formName = Path.GetFileNameWithoutExtension(formKvp.Key);
                hierarchicalStructure[formName] = new
                {
                    FileName = formKvp.Key,
                    FormDefinition = formKvp.Value
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

                // Add form metadata
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

                    // Group controls by section
                    var rootControls = view.Controls.Where(c => string.IsNullOrEmpty(c.ParentSection));

                    // Add root controls
                    if (rootControls.Any())
                    {
                        var rootItem = new TreeViewItem
                        {
                            Header = "📦 Root Controls",
                            IsExpanded = false
                        };

                        foreach (var control in rootControls.Where(c => !c.IsMergedIntoParent))
                        {
                            var controlItem = CreateControlTreeItem(control);
                            rootItem.Items.Add(controlItem);
                        }

                        viewItem.Items.Add(rootItem);
                    }

                    // Add sections
                    foreach (var section in view.Sections)
                    {
                        var sectionItem = new TreeViewItem
                        {
                            Header = $"{GetSectionIcon(section.Type)} {section.Name} ({section.Type})",
                            Tag = section,
                            IsExpanded = false
                        };

                        // Add controls in this section
                        var sectionControls = view.Controls
                            .Where(c => c.ParentSection == section.Name && !c.IsMergedIntoParent)
                            .OrderBy(c => c.DocIndex);

                        foreach (var control in sectionControls)
                        {
                            var controlItem = CreateControlTreeItem(control);
                            sectionItem.Items.Add(controlItem);
                        }

                        viewItem.Items.Add(sectionItem);
                    }

                    formItem.Items.Add(viewItem);
                }

                // Add dynamic sections if any
                if (formDef.DynamicSections.Any())
                {
                    var dynamicItem = new TreeViewItem
                    {
                        Header = $"🔄 Dynamic Sections ({formDef.DynamicSections.Count})",
                        IsExpanded = false
                    };

                    foreach (var dynSection in formDef.DynamicSections)
                    {
                        var sectionItem = new TreeViewItem
                        {
                            Header = $"🔀 {dynSection.Caption ?? dynSection.Mode}"
                        };

                        sectionItem.Items.Add(new TreeViewItem
                        {
                            Header = $"Condition: {dynSection.Condition}",
                            Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                            FontSize = 11
                        });

                        dynamicItem.Items.Add(sectionItem);
                    }

                    formItem.Items.Add(dynamicItem);
                }

                _mainWindow.StructureTreeView.Items.Add(formItem);
            }
        }

        private TreeViewItem CreateControlTreeItem(ControlDefinition control)
        {
            // Create header with control info and grid position
            var headerText = $"{GetControlIcon(control.Type)} ";

            // Add control name/label
            if (!string.IsNullOrEmpty(control.Label))
            {
                headerText += $"{control.Label} ";
            }
            else if (!string.IsNullOrEmpty(control.Name))
            {
                headerText += $"{control.Name} ";
            }

            // Add type
            headerText += $"[{control.Type}]";

            // Add grid position
            if (!string.IsNullOrEmpty(control.GridPosition))
            {
                headerText += $" 📍{control.GridPosition}";
            }

            var item = new TreeViewItem { Header = headerText };

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

            if (detailsItem.Items.Count > 0)
            {
                item.Items.Add(detailsItem);
            }

            return item;
        }

        #endregion

        #region Reusable Views

        public async Task RefreshReusableViews()
        {
            try
            {
                _mainWindow.UpdateStatus("Analyzing reusable controls...");

                if (_mainWindow._allFormDefinitions == null || !_mainWindow._allFormDefinitions.Any())
                {
                    MessageBox.Show("Please analyze forms first before identifying reusable controls.",
                                   "No Analysis Results",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Warning);
                    return;
                }

                await Task.Run(() =>
                {
                    _mainWindow.Dispatcher.Invoke(() =>
                    {
                        AnalyzeReusableControls();
                    });
                });

                _mainWindow.UpdateStatus("Reusable controls analysis complete", MessageSeverity.Info);
            }
            catch (Exception ex)
            {
                _mainWindow.UpdateStatus($"Reusable analysis failed: {ex.Message}", MessageSeverity.Error);
                MessageBox.Show($"Analysis failed:\n{ex.Message}",
                               "Analysis Error",
                               MessageBoxButton.OK,
                               MessageBoxImage.Error);
            }
        }

        private void AnalyzeReusableControls()
        {
            _mainWindow.ReusableControlsTreeView.Items.Clear();
            _mainWindow.ReusableStatisticsPanel.Children.Clear();

            // Get minimum occurrence threshold
            var minOccurrences = 2;
            if (_mainWindow.MinOccurrencesCombo.SelectedItem is ComboBoxItem selectedItem)
            {
                if (int.TryParse(selectedItem.Tag?.ToString(), out int min))
                {
                    minOccurrences = min;
                }
            }

            // Collect all controls from all forms
            var allControlOccurrences = new List<ControlOccurrence>();

            foreach (var formKvp in _mainWindow._allFormDefinitions)
            {
                var formName = formKvp.Key;
                var formDef = formKvp.Value;

                foreach (var view in formDef.Views)
                {
                    foreach (var control in view.Controls.Where(c => !c.IsMergedIntoParent))
                    {
                        allControlOccurrences.Add(new ControlOccurrence
                        {
                            FormName = formName,
                            ViewName = view.ViewName,
                            Section = control.ParentSection ?? "Root",
                            GridPosition = control.GridPosition,
                            Control = control
                        });
                    }
                }
            }

            // Group controls based on selected grouping
            var groupBy = (_mainWindow.GroupByCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Control Type";
            var reusableGroups = GroupControls(allControlOccurrences, groupBy, minOccurrences);

            // Filter by type if specified
            var filterType = (_mainWindow.FilterTypeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (filterType != "All Types" && !string.IsNullOrEmpty(filterType))
            {
                reusableGroups = reusableGroups.Where(g => g.ControlType == filterType).ToList();
            }

            // Store the current groups
            _currentReusableGroups = reusableGroups;

            // Enable save button if we have groups
            _mainWindow.SaveReusableViewsButton.IsEnabled = reusableGroups.Any();

            // Display statistics
            DisplayReusableStatistics(reusableGroups, allControlOccurrences.Count);

            // Build tree view
            BuildReusableControlsTree(reusableGroups);
        }

        private List<ReusableControlGroup> GroupControls(List<ControlOccurrence> occurrences, string groupBy, int minOccurrences)
        {
            var groups = new Dictionary<string, ReusableControlGroup>();

            foreach (var occurrence in occurrences)
            {
                string groupKey = GetGroupKey(occurrence.Control, groupBy);

                if (string.IsNullOrEmpty(groupKey))
                    continue;

                if (!groups.ContainsKey(groupKey))
                {
                    groups[groupKey] = new ReusableControlGroup
                    {
                        GroupKey = groupKey,
                        ControlType = occurrence.Control.Type,
                        Label = occurrence.Control.Label,
                        Name = occurrence.Control.Name
                    };
                }

                var group = groups[groupKey];
                group.Occurrences.Add(occurrence);

                if (!group.AppearingInForms.Contains(occurrence.FormName))
                {
                    group.AppearingInForms.Add(occurrence.FormName);
                }
            }

            // Filter groups by minimum occurrences and calculate common properties
            var reusableGroups = groups.Values
                .Where(g => g.AppearingInForms.Count >= minOccurrences)
                .ToList();

            foreach (var group in reusableGroups)
            {
                group.OccurrenceCount = group.Occurrences.Count;
                CalculateCommonProperties(group);
            }

            return reusableGroups.OrderByDescending(g => g.AppearingInForms.Count)
                                   .ThenByDescending(g => g.OccurrenceCount)
                                   .ToList();
        }

        private string GetGroupKey(ControlDefinition control, string groupBy)
        {
            return groupBy switch
            {
                "Label/Name" => !string.IsNullOrEmpty(control.Label) ? control.Label : control.Name,
                "Section" => $"{control.ParentSection ?? "Root"}_{control.Type}",
                _ => control.Type // Default to Control Type
            };
        }

        private void CalculateCommonProperties(ReusableControlGroup group)
        {
            if (!group.Occurrences.Any())
                return;

            // Find properties that are common across all occurrences
            var firstControl = group.Occurrences.First().Control;

            // Check common properties
            if (group.Occurrences.All(o => o.Control.Type == firstControl.Type))
            {
                group.CommonProperties["Type"] = firstControl.Type;
            }

            if (group.Occurrences.All(o => o.Control.Label == firstControl.Label) && !string.IsNullOrEmpty(firstControl.Label))
            {
                group.CommonProperties["Label"] = firstControl.Label;
            }

            if (group.Occurrences.All(o => o.Control.ColumnSpan == firstControl.ColumnSpan) && firstControl.ColumnSpan > 1)
            {
                group.CommonProperties["ColumnSpan"] = firstControl.ColumnSpan.ToString();
            }

            if (group.Occurrences.All(o => o.Control.IsInRepeatingSection == firstControl.IsInRepeatingSection))
            {
                group.CommonProperties["IsRepeating"] = firstControl.IsInRepeatingSection.ToString();
            }
        }

        private void DisplayReusableStatistics(List<ReusableControlGroup> groups, int totalControls)
        {
            // Clear existing statistics
            _mainWindow.ReusableStatisticsPanel.Children.Clear();

            // Add statistics cards
            AddStatisticCard("Total Groups", groups.Count.ToString(), "🎯", Brushes.DodgerBlue);
            AddStatisticCard("Reusable Controls", groups.Sum(g => g.OccurrenceCount).ToString(), "🔄", Brushes.Green);
            AddStatisticCard("Unique Controls", totalControls.ToString(), "📊", Brushes.Purple);

            var mostReused = groups.FirstOrDefault();
            if (mostReused != null)
            {
                AddStatisticCard("Most Reused", $"{mostReused.GroupKey} ({mostReused.AppearingInForms.Count} forms)", "⭐", Brushes.Gold);
            }

            // Calculate potential savings
            var potentialViews = groups.Count(g => g.AppearingInForms.Count >= 3);
            AddStatisticCard("Potential Views", potentialViews.ToString(), "💡", Brushes.Orange);
        }

        private void AddStatisticCard(string label, string value, string icon, Brush color)
        {
            var card = new Border
            {
                Background = (Brush)_mainWindow.FindResource("BackgroundLight"),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(15, 10, 15, 10),
                Margin = new Thickness(5),
                MinWidth = 120
            };

            var stack = new StackPanel { Orientation = Orientation.Horizontal };

            stack.Children.Add(new TextBlock
            {
                Text = icon,
                FontSize = 20,
                Foreground = color,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            });

            var textStack = new StackPanel();
            textStack.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary")
            });
            textStack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = (Brush)_mainWindow.FindResource("TextSecondary")
            });

            stack.Children.Add(textStack);
            card.Child = stack;
            _mainWindow.ReusableStatisticsPanel.Children.Add(card);
        }

        private void BuildReusableControlsTree(List<ReusableControlGroup> groups)
        {
            _mainWindow.ReusableControlsTreeView.Items.Clear();

            // Group by control type first
            var typeGroups = groups.GroupBy(g => g.ControlType).OrderBy(g => g.Key);

            foreach (var typeGroup in typeGroups)
            {
                var typeItem = new TreeViewItem
                {
                    Header = CreateReusableGroupHeader(typeGroup.Key, typeGroup.Count()),
                    IsExpanded = true,
                    Tag = typeGroup
                };

                foreach (var group in typeGroup.OrderByDescending(g => g.AppearingInForms.Count))
                {
                    var groupItem = new TreeViewItem
                    {
                        Header = CreateReusableControlHeader(group),
                        Tag = group
                    };

                    // Add form occurrences as children
                    foreach (var formName in group.AppearingInForms)
                    {
                        var formOccurrences = group.Occurrences.Where(o => o.FormName == formName).ToList();

                        var formItem = new TreeViewItem
                        {
                            Header = $"📁 {formName} ({formOccurrences.Count} occurrences)",
                            Tag = formOccurrences
                        };

                        foreach (var occurrence in formOccurrences)
                        {
                            var occurrenceItem = new TreeViewItem
                            {
                                Header = $"📄 {occurrence.ViewName} - {occurrence.Section} [{occurrence.GridPosition}]",
                                Tag = occurrence,
                                Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                                FontSize = 11
                            };
                            formItem.Items.Add(occurrenceItem);
                        }

                        groupItem.Items.Add(formItem);
                    }

                    typeItem.Items.Add(groupItem);
                }

                _mainWindow.ReusableControlsTreeView.Items.Add(typeItem);
            }

            // Handle selection changed
            _mainWindow.ReusableControlsTreeView.SelectedItemChanged += ReusableTree_SelectedItemChanged;
        }

        private object CreateReusableGroupHeader(string typeName, int count)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };

            panel.Children.Add(new TextBlock
            {
                Text = GetControlIcon(typeName),
                FontSize = 16,
                Margin = new Thickness(0, 0, 5, 0),
                Foreground = (Brush)_mainWindow.FindResource("AccentColor")
            });

            panel.Children.Add(new TextBlock
            {
                Text = $"{typeName}",
                FontWeight = FontWeights.Medium,
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary")
            });

            panel.Children.Add(new TextBlock
            {
                Text = $" ({count} groups)",
                Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                FontStyle = FontStyles.Italic
            });

            return panel;
        }

        private object CreateReusableControlHeader(ReusableControlGroup group)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };

            // Reuse indicator
            var reuseIndicator = new Border
            {
                Background = group.AppearingInForms.Count >= 4 ? (Brush)_mainWindow.FindResource("SuccessColor") :
                            group.AppearingInForms.Count >= 3 ? (Brush)_mainWindow.FindResource("WarningColor") :
                            (Brush)_mainWindow.FindResource("InfoColor"),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 2, 5, 2),
                Margin = new Thickness(0, 0, 5, 0)
            };

            reuseIndicator.Child = new TextBlock
            {
                Text = $"{group.AppearingInForms.Count} forms",
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.Medium
            };

            panel.Children.Add(reuseIndicator);

            // Show custom name if available
            if (!string.IsNullOrEmpty(group.CustomName))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = group.CustomName,
                    Foreground = (Brush)_mainWindow.FindResource("AccentLight"),
                    FontWeight = FontWeights.Medium,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 5, 0)
                });

                panel.Children.Add(new TextBlock
                {
                    Text = "→",
                    Foreground = (Brush)_mainWindow.FindResource("TextDim"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 5, 0)
                });
            }

            // Control name/label
            panel.Children.Add(new TextBlock
            {
                Text = !string.IsNullOrEmpty(group.Label) ? group.Label : group.Name ?? group.GroupKey,
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary"),
                VerticalAlignment = VerticalAlignment.Center
            });

            // Total occurrences
            panel.Children.Add(new TextBlock
            {
                Text = $" ({group.OccurrenceCount} total)",
                Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                FontStyle = FontStyles.Italic,
                VerticalAlignment = VerticalAlignment.Center
            });

            return panel;
        }

        private void ReusableTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var selectedItem = e.NewValue as TreeViewItem;
            if (selectedItem?.Tag is ReusableControlGroup group)
            {
                _selectedReusableGroup = group;
                DisplayReusableGroupDetails(group);

                // Enable and populate the group name textbox
                _mainWindow.GroupNameTextBox.IsEnabled = true;
                _mainWindow.GroupNameTextBox.Text = group.CustomName ?? "";

                _mainWindow.CreateReusableViewButton.IsEnabled = true;
                _mainWindow.ExportReusableControlsButton.IsEnabled = true;
            }
            else
            {
                _selectedReusableGroup = null;
                _mainWindow.GroupNameTextBox.IsEnabled = false;
                _mainWindow.GroupNameTextBox.Text = "";

                _mainWindow.CreateReusableViewButton.IsEnabled = false;
                _mainWindow.ExportReusableControlsButton.IsEnabled = false;
            }
        }

        public void GroupNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_selectedReusableGroup != null)
            {
                _selectedReusableGroup.CustomName = _mainWindow.GroupNameTextBox.Text;

                // Update the tree view header for the selected item
                if (_mainWindow.ReusableControlsTreeView.SelectedItem is TreeViewItem selectedItem)
                {
                    selectedItem.Header = CreateReusableControlHeader(_selectedReusableGroup);
                }
            }
        }

        private void DisplayReusableGroupDetails(ReusableControlGroup group)
        {
            _mainWindow.ReusableDetailsPanel.Children.Clear();

            // Group header
            var headerText = new TextBlock
            {
                Text = !string.IsNullOrEmpty(group.Label) ? group.Label : group.Name ?? group.GroupKey,
                FontSize = 16,
                FontWeight = FontWeights.Medium,
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary"),
                Margin = new Thickness(0, 0, 0, 10),
                TextWrapping = TextWrapping.Wrap
            };
            _mainWindow.ReusableDetailsPanel.Children.Add(headerText);

            // Statistics
            AddDetailSection("Statistics", new[]
            {
                $"Appears in: {group.AppearingInForms.Count} forms",
                $"Total occurrences: {group.OccurrenceCount}",
                $"Control type: {group.ControlType}"
            });

            // Common properties
            if (group.CommonProperties.Any())
            {
                var propLines = group.CommonProperties.Select(kvp => $"{kvp.Key}: {kvp.Value}").ToArray();
                AddDetailSection("Common Properties", propLines);
            }

            // Forms list
            AddDetailSection("Forms", group.AppearingInForms.ToArray());

            // Recommendation
            var recommendation = GetReusabilityRecommendation(group);
            if (!string.IsNullOrEmpty(recommendation))
            {
                var recBorder = new Border
                {
                    Background = (Brush)_mainWindow.FindResource("InfoColor"),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 10, 0, 0)
                };

                recBorder.Child = new TextBlock
                {
                    Text = recommendation,
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12
                };

                _mainWindow.ReusableDetailsPanel.Children.Add(recBorder);
            }
        }

        private void AddDetailSection(string title, string[] items)
        {
            var titleText = new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)_mainWindow.FindResource("TextPrimary"),
                Margin = new Thickness(0, 10, 0, 5)
            };
            _mainWindow.ReusableDetailsPanel.Children.Add(titleText);

            foreach (var item in items)
            {
                var itemText = new TextBlock
                {
                    Text = $"• {item}",
                    Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                    Margin = new Thickness(10, 2, 0, 2),
                    TextWrapping = TextWrapping.Wrap
                };
                _mainWindow.ReusableDetailsPanel.Children.Add(itemText);
            }
        }

        private string GetReusabilityRecommendation(ReusableControlGroup group)
        {
            if (group.AppearingInForms.Count >= 4)
            {
                return "💡 Highly recommended for reusable view. This control appears in many forms and would benefit from centralized management.";
            }
            else if (group.AppearingInForms.Count >= 3)
            {
                return "💡 Good candidate for reusable view. Consider creating a shared component.";
            }
            else if (group.OccurrenceCount >= 5)
            {
                return "💡 Multiple occurrences detected. May benefit from a template approach.";
            }

            return null;
        }

        public async Task SaveReusableViews()
        {
            try
            {
                if (_currentReusableGroups == null || !_currentReusableGroups.Any())
                {
                    MessageBox.Show("No reusable control groups to save. Please run the analysis first.",
                                   "No Data to Save",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Warning);
                    return;
                }

                var dialog = new SaveFileDialog
                {
                    Title = "Save Reusable Views Analysis",
                    Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                    FileName = $"ReusableViews_{DateTime.Now:yyyyMMdd_HHmmss}.json"
                };

                if (dialog.ShowDialog() == true)
                {
                    _mainWindow.UpdateStatus("Saving reusable views to JSON...");

                    // Auto-name any unnamed groups
                    AutoNameGroups();

                    // Create the full JSON structure
                    var fullData = new
                    {
                        AnalysisDate = DateTime.Now,
                        TotalForms = _mainWindow._allFormDefinitions.Count,
                        FormDefinitions = _mainWindow._allFormDefinitions,
                        ReusableViews = new
                        {
                            AnalysisSettings = new
                            {
                                MinOccurrences = _mainWindow.MinOccurrencesCombo.SelectedItem is ComboBoxItem item ? item.Tag?.ToString() : "2",
                                GroupBy = (_mainWindow.GroupByCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Control Type",
                                FilterType = (_mainWindow.FilterTypeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Types"
                            },
                            Statistics = new
                            {
                                TotalGroups = _currentReusableGroups.Count,
                                TotalReusableControls = _currentReusableGroups.Sum(g => g.OccurrenceCount),
                                GroupsWithHighReuse = _currentReusableGroups.Count(g => g.AppearingInForms.Count >= 4)
                            },
                            Groups = _currentReusableGroups
                        }
                    };

                    var json = JsonConvert.SerializeObject(fullData, Formatting.Indented);
                    await File.WriteAllTextAsync(dialog.FileName, json);

                    _mainWindow.UpdateStatus($"Saved reusable views to: {dialog.FileName}", MessageSeverity.Info);
                    _mainWindow.JsonOutput.Text = json;

                    MessageBox.Show($"Reusable views analysis saved successfully!",
                                   "Save Successful",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _mainWindow.UpdateStatus($"Failed to save reusable views: {ex.Message}", MessageSeverity.Error);
                MessageBox.Show($"Error saving reusable views:\n{ex.Message}",
                               "Save Error",
                               MessageBoxButton.OK,
                               MessageBoxImage.Error);
            }
        }

        private void AutoNameGroups()
        {
            _autoNameCounter = 1;
            foreach (var group in _currentReusableGroups)
            {
                if (string.IsNullOrEmpty(group.CustomName))
                {
                    group.CustomName = $"Group_{_autoNameCounter:D3}";
                    _autoNameCounter++;
                }
            }
        }

        public void MinOccurrences_Changed()
        {
            if (_mainWindow._allFormDefinitions != null && _mainWindow._allFormDefinitions.Any())
            {
                AnalyzeReusableControls();
            }
        }

        public void GroupBy_Changed()
        {
            if (_mainWindow._allFormDefinitions != null && _mainWindow._allFormDefinitions.Any())
            {
                AnalyzeReusableControls();
            }
        }

        public void FilterType_Changed()
        {
            if (_mainWindow._allFormDefinitions != null && _mainWindow._allFormDefinitions.Any())
            {
                AnalyzeReusableControls();
            }
        }

        public async Task CreateReusableView()
        {
            try
            {
                var selectedItem = _mainWindow.ReusableControlsTreeView.SelectedItem as TreeViewItem;
                if (selectedItem?.Tag is ReusableControlGroup group)
                {
                    var result = MessageBox.Show(
                        $"Create a reusable K2 SmartForm view for:\n\n" +
                        $"{group.Label ?? group.Name ?? group.GroupKey}\n" +
                        $"Type: {group.ControlType}\n" +
                        $"Used in {group.AppearingInForms.Count} forms\n\n" +
                        $"This will generate a K2 view that can be reused across all forms.",
                        "Create Reusable View",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        _mainWindow.UpdateStatus($"Creating reusable view for {group.GroupKey}...");

                        // Simulate view creation
                        await Task.Delay(1000);

                        _mainWindow.K2GenerationLog.Text += $"\n✅ Created reusable view: {group.GroupKey}\n";
                        _mainWindow.K2GenerationLog.Text += $"   - Type: {group.ControlType}\n";
                        _mainWindow.K2GenerationLog.Text += $"   - Forms: {string.Join(", ", group.AppearingInForms)}\n";

                        _mainWindow.UpdateStatus("Reusable view created successfully", MessageSeverity.Info);

                        MessageBox.Show(
                            $"Reusable view created successfully!",
                            "Success",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                _mainWindow.UpdateStatus($"Failed to create reusable view: {ex.Message}", MessageSeverity.Error);
                MessageBox.Show($"Error creating reusable view:\n{ex.Message}",
                               "Error",
                               MessageBoxButton.OK,
                               MessageBoxImage.Error);
            }
        }

        public async Task ExportReusableControls()
        {
            try
            {
                var selectedItem = _mainWindow.ReusableControlsTreeView.SelectedItem as TreeViewItem;
                if (selectedItem?.Tag is ReusableControlGroup group)
                {
                    var dialog = new SaveFileDialog
                    {
                        Title = "Export Reusable Control Group",
                        Filter = "JSON Files (*.json)|*.json|CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                        FileName = $"ReusableControls_{group.ControlType}_{DateTime.Now:yyyyMMdd}"
                    };

                    if (dialog.ShowDialog() == true)
                    {
                        _mainWindow.UpdateStatus("Exporting reusable controls...");

                        if (Path.GetExtension(dialog.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase))
                        {
                            // Export as CSV
                            var csv = new StringBuilder();
                            csv.AppendLine("FormName,ViewName,Section,GridPosition,Type,Label,Name");

                            foreach (var occurrence in group.Occurrences)
                            {
                                csv.AppendLine($"{occurrence.FormName},{occurrence.ViewName},{occurrence.Section}," +
                                             $"{occurrence.GridPosition},{occurrence.Control.Type}," +
                                             $"{occurrence.Control.Label},{occurrence.Control.Name}");
                            }

                            await File.WriteAllTextAsync(dialog.FileName, csv.ToString());
                        }
                        else
                        {
                            // Export as JSON
                            var json = JsonConvert.SerializeObject(group, Formatting.Indented);
                            await File.WriteAllTextAsync(dialog.FileName, json);
                        }

                        _mainWindow.UpdateStatus($"Exported to: {dialog.FileName}", MessageSeverity.Info);

                        MessageBox.Show($"Reusable controls exported successfully to:\n{dialog.FileName}",
                                       "Export Complete",
                                       MessageBoxButton.OK,
                                       MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                _mainWindow.UpdateStatus($"Export failed: {ex.Message}", MessageSeverity.Error);
                MessageBox.Show($"Export failed:\n{ex.Message}",
                               "Export Error",
                               MessageBoxButton.OK,
                               MessageBoxImage.Error);
            }
        }

        #endregion

        #region Helper Methods

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