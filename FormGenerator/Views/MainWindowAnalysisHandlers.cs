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

                if (formDef.Metadata.RepeatingSectionCount > 0)
                {
                    metadataItem.Items.Add(new TreeViewItem
                    {
                        Header = $"Repeating Sections: {formDef.Metadata.RepeatingSectionCount} (will be K2 List Views)",
                        Foreground = (Brush)_mainWindow.FindResource("InfoColor"),
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

                    // Create a set of all section names for quick lookup
                    var sectionNames = new HashSet<string>(view.Sections.Select(s => s.Name));

                    // Group controls by their actual parent section
                    var controlsBySection = new Dictionary<string, List<ControlDefinition>>();
                    var rootControls = new List<ControlDefinition>();
                    var repeatingControls = new Dictionary<string, List<ControlDefinition>>();

                    foreach (var control in view.Controls.Where(c => !c.IsMergedIntoParent))
                    {
                        if (control.IsInRepeatingSection)
                        {
                            // Group repeating controls by their section name
                            var sectionKey = control.RepeatingSectionName ?? "Unknown Repeating Section";
                            if (!repeatingControls.ContainsKey(sectionKey))
                            {
                                repeatingControls[sectionKey] = new List<ControlDefinition>();
                            }
                            repeatingControls[sectionKey].Add(control);
                        }
                        else if (!string.IsNullOrEmpty(control.ParentSection))
                        {
                            // Group by parent section
                            if (!controlsBySection.ContainsKey(control.ParentSection))
                            {
                                controlsBySection[control.ParentSection] = new List<ControlDefinition>();
                            }
                            controlsBySection[control.ParentSection].Add(control);
                        }
                        else
                        {
                            // Root level controls
                            rootControls.Add(control);
                        }
                    }

                    // Add root controls if any
                    if (rootControls.Any())
                    {
                        var rootItem = new TreeViewItem
                        {
                            Header = "📦 Root Controls (Reusable View Candidates)",
                            IsExpanded = false,
                            Foreground = (Brush)_mainWindow.FindResource("SuccessColor")
                        };

                        foreach (var control in rootControls.OrderBy(c => c.DocIndex))
                        {
                            var controlItem = CreateControlTreeItem(control);
                            rootItem.Items.Add(controlItem);
                        }

                        viewItem.Items.Add(rootItem);
                    }

                    // Add regular sections with their controls
                    foreach (var section in view.Sections.Where(s => s.Type != "repeating"))
                    {
                        var sectionItem = new TreeViewItem
                        {
                            Header = $"{GetSectionIcon(section.Type)} {section.Name} ({section.Type})",
                            Tag = section,
                            IsExpanded = false
                        };

                        // Find controls for this section
                        // Check both by section.Name and any controls that might have this as ParentSection
                        var sectionControls = new List<ControlDefinition>();

                        if (controlsBySection.ContainsKey(section.Name))
                        {
                            sectionControls.AddRange(controlsBySection[section.Name]);
                        }

                        // Also check for controls where ParentSection matches any variation of the section name
                        foreach (var kvp in controlsBySection)
                        {
                            if (kvp.Key.Equals(section.Name, StringComparison.OrdinalIgnoreCase) &&
                                kvp.Key != section.Name)
                            {
                                sectionControls.AddRange(kvp.Value);
                            }
                        }

                        // Sort by DocIndex and add to tree
                        foreach (var control in sectionControls.OrderBy(c => c.DocIndex).Distinct())
                        {
                            var controlItem = CreateControlTreeItem(control);
                            sectionItem.Items.Add(controlItem);
                        }

                        // Only add section if it has controls or is explicitly defined
                        if (sectionItem.Items.Count > 0 || section.Type == "optional" || section.Type == "dynamic")
                        {
                            viewItem.Items.Add(sectionItem);
                        }
                    }

                    // Add repeating sections separately
                    if (repeatingControls.Any())
                    {
                        var repeatingSectionsItem = new TreeViewItem
                        {
                            Header = $"🔁 Repeating Sections (K2 List Views)",
                            IsExpanded = false,
                            Foreground = (Brush)_mainWindow.FindResource("InfoColor")
                        };

                        foreach (var sectionGroup in repeatingControls.OrderBy(g => g.Key))
                        {
                            var sectionItem = new TreeViewItem
                            {
                                Header = $"📋 {sectionGroup.Key} ({sectionGroup.Value.Count} controls)",
                                IsExpanded = false
                            };

                            foreach (var control in sectionGroup.Value.OrderBy(c => c.DocIndex))
                            {
                                var controlItem = CreateControlTreeItem(control);
                                controlItem.ToolTip = "Part of repeating section - will be in K2 List View";
                                sectionItem.Items.Add(controlItem);
                            }

                            repeatingSectionsItem.Items.Add(sectionItem);
                        }

                        viewItem.Items.Add(repeatingSectionsItem);
                    }

                    // Add any orphaned controls that weren't categorized
                    var allCategorizedControls = new HashSet<ControlDefinition>();
                    allCategorizedControls.UnionWith(rootControls);
                    allCategorizedControls.UnionWith(controlsBySection.Values.SelectMany(v => v));
                    allCategorizedControls.UnionWith(repeatingControls.Values.SelectMany(v => v));

                    var orphanedControls = view.Controls
                        .Where(c => !c.IsMergedIntoParent && !allCategorizedControls.Contains(c))
                        .ToList();

                    if (orphanedControls.Any())
                    {
                        var orphanedItem = new TreeViewItem
                        {
                            Header = $"❓ Uncategorized Controls ({orphanedControls.Count})",
                            IsExpanded = false,
                            Foreground = (Brush)_mainWindow.FindResource("WarningColor")
                        };

                        foreach (var control in orphanedControls.OrderBy(c => c.DocIndex))
                        {
                            var controlItem = CreateControlTreeItem(control);
                            orphanedItem.Items.Add(controlItem);
                        }

                        viewItem.Items.Add(orphanedItem);
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

                        if (dynSection.Controls != null && dynSection.Controls.Any())
                        {
                            sectionItem.Items.Add(new TreeViewItem
                            {
                                Header = $"Controls: {string.Join(", ", dynSection.Controls.Take(3))}...",
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

            // Add indicator if in repeating section
            if (control.IsInRepeatingSection)
            {
                headerText += " 🔁";
            }

            var item = new TreeViewItem { Header = headerText };

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

            if (detailsItem.Items.Count > 0)
            {
                item.Items.Add(detailsItem);
            }

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

                // Get the minimum and maximum group sizes
                int minGroupSize = 2; // Minimum 2 controls to form a group
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
                _mainWindow.UpdateStatus($"Found {_currentReusableAnalysis.IdentifiedGroups.Count} reusable control groups " +
                                        $"({_currentReusableAnalysis.ControlsInRepeatingSections} controls excluded from repeating sections)",
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

            // Control count
            var controlCountText = new TextBlock
            {
                Text = $"Contains {group.Controls.Count} controls (not in repeating sections)",
                Foreground = (Brush)_mainWindow.FindResource("TextSecondary"),
                Margin = new Thickness(0, 0, 0, 10)
            };
            _mainWindow.ReusableDetailsPanel.Children.Add(controlCountText);

            // Controls list
            var controlsHeader = new TextBlock
            {
                Text = "Controls in this group:",
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

            suggestion.AppendLine($"This control group appears in {group.OccurrenceCount} forms and is a good candidate for a reusable K2 SmartForm Item View.");
            suggestion.AppendLine();
            suggestion.AppendLine("Recommended implementation:");
            suggestion.AppendLine("• Create a SmartObject with properties for each control");
            suggestion.AppendLine("• Build a reusable Item View with these controls");
            suggestion.AppendLine("• Use subviews or subforms to include in multiple forms");
            suggestion.AppendLine();
            suggestion.AppendLine("Note: This group contains only non-repeating controls. Repeating sections are handled separately as K2 List Views.");

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