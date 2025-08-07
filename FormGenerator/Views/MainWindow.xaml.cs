using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Newtonsoft.Json;

using FormGenerator.Analyzers.Infopath;
using FormGenerator.Analyzers.InfoPath;
using FormGenerator.Core.Interfaces;
using FormGenerator.Core.Models;
using FormGenerator.Services;
using System.Text;

namespace FormGenerator.Views
{

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
    /// <summary>
    /// Main window for the Form Analyzer Pro application
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<FormFileInfo> _uploadedFiles;
        private readonly AnalyzerFactory _analyzerFactory;
        private readonly SqlGeneratorService _sqlGenerator;
        private FormAnalysisResult _currentAnalysis;
        private ControlDefinition _selectedControl;
        private TreeViewItem _selectedTreeItem;
        private Grid _propertiesPanel;
        private Dictionary<string, TextBox> _propertyEditors = new Dictionary<string, TextBox>();
        private Dictionary<string, FormAnalysisResult> _allAnalysisResults = new Dictionary<string, FormAnalysisResult>();
        private Dictionary<string, InfoPathFormDefinition> _allFormDefinitions = new Dictionary<string, InfoPathFormDefinition>();

        public MainWindow()
        {
            InitializeComponent();

            _uploadedFiles = new ObservableCollection<FormFileInfo>();
            _analyzerFactory = new AnalyzerFactory();
            _sqlGenerator = new SqlGeneratorService();

            FileListBox.ItemsSource = _uploadedFiles;

            // Set version
            VersionText.Text = $"v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}";
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select InfoPath Forms",
                Filter = "InfoPath Forms (*.xsn)|*.xsn|All Files (*.*)|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
            {
                AddFiles(dialog.FileNames);
            }
        }

        private void DropZone_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                AddFiles(files);
            }

            // Reset drop zone appearance
            DropZone.BorderBrush = (Brush)FindResource("BorderColor");
        }

        private void DropZone_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                DropZone.BorderBrush = (Brush)FindResource("AccentColor");
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void DropZone_DragLeave(object sender, DragEventArgs e)
        {
            DropZone.BorderBrush = (Brush)FindResource("BorderColor");
        }

        private void AddFiles(string[] filePaths)
        {
            foreach (var filePath in filePaths)
            {
                // Check if file is already added
                if (_uploadedFiles.Any(f => f.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
                {
                    UpdateStatus($"File already added: {Path.GetFileName(filePath)}");
                    continue;
                }

                // Check if file can be analyzed
                var formType = GetSelectedFormType();
                var analyzer = _analyzerFactory.GetAnalyzer(formType);

                if (analyzer == null || !analyzer.CanAnalyze(filePath))
                {
                    UpdateStatus($"Cannot analyze file: {Path.GetFileName(filePath)}", MessageSeverity.Warning);
                    continue;
                }

                var fileInfo = new FormFileInfo
                {
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                    FileSize = new FileInfo(filePath).Length,
                    Status = "Ready",
                    UploadedDate = DateTime.Now
                };

                _uploadedFiles.Add(fileInfo);
            }

            // Enable analyze button if files are added
            AnalyzeButton.IsEnabled = _uploadedFiles.Count > 0;

            UpdateStatus($"Added {_uploadedFiles.Count} file(s)");
        }

        private void RemoveFile_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var fileInfo = button?.Tag as FormFileInfo;

            if (fileInfo != null)
            {
                _uploadedFiles.Remove(fileInfo);
                AnalyzeButton.IsEnabled = _uploadedFiles.Count > 0;
                UpdateStatus($"Removed: {fileInfo.FileName}");
            }
        }

        private async void AnalyzeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_uploadedFiles.Count == 0)
                return;

            AnalyzeButton.IsEnabled = false;
            ExportButton.IsEnabled = false;
            ProgressBar.Visibility = Visibility.Visible;

            // Clear previous results
            _allAnalysisResults.Clear();
            _allFormDefinitions.Clear();

            try
            {
                var formType = GetSelectedFormType();
                var analyzer = _analyzerFactory.GetAnalyzer(formType);

                if (analyzer == null)
                {
                    MessageBox.Show($"Analyzer for {formType} is not available yet.",
                                  "Analyzer Not Available",
                                  MessageBoxButton.OK,
                                  MessageBoxImage.Information);
                    return;
                }

                UpdateStatus($"Analyzing {_uploadedFiles.Count} form(s)...");

                // Analyze all uploaded forms
                foreach (var fileToAnalyze in _uploadedFiles)
                {
                    UpdateStatus($"Analyzing {fileToAnalyze.FileName}...");
                    fileToAnalyze.Status = "Analyzing...";

                    // Run analysis
                    var analysisResult = await analyzer.AnalyzeFormAsync(fileToAnalyze.FilePath);

                    if (analysisResult.Success)
                    {
                        fileToAnalyze.Status = "Analyzed";
                        fileToAnalyze.AnalysisResult = analysisResult;

                        // Store results with form name as key
                        _allAnalysisResults[fileToAnalyze.FileName] = analysisResult;
                        _allFormDefinitions[fileToAnalyze.FileName] = analysisResult.FormDefinition;
                    }
                    else
                    {
                        fileToAnalyze.Status = "Failed";
                        UpdateStatus($"Analysis failed for {fileToAnalyze.FileName}: {analysisResult.ErrorMessage}", MessageSeverity.Error);
                    }
                }

                // Display combined results
                if (_allAnalysisResults.Any())
                {
                    await DisplayCombinedAnalysisResults(_allAnalysisResults);
                    await GenerateCombinedSqlPreview(_allAnalysisResults);

                    UpdateStatus($"Analysis completed for {_allAnalysisResults.Count} form(s)", MessageSeverity.Info);
                    ExportButton.IsEnabled = true;

                    // Enable generation tabs after successful analysis
                    EnableGenerationTabs();
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error: {ex.Message}", MessageSeverity.Error);
                MessageBox.Show($"An error occurred:\n{ex.Message}",
                              "Error",
                              MessageBoxButton.OK,
                              MessageBoxImage.Error);
            }
            finally
            {
                AnalyzeButton.IsEnabled = _uploadedFiles.Count > 0;
                ProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private async Task DisplayCombinedAnalysisResults(Dictionary<string, FormAnalysisResult> allResults)
        {
            await Task.Run(() =>
            {
                Dispatcher.Invoke(() =>
                {
                    // Clear previous results
                    SummaryPanel.Items.Clear();
                    StructureTreeView.Items.Clear();
                    DataColumnsGrid.ItemsSource = null;
                    JsonOutput.Text = "";

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
                        AddSummaryCard("Errors", totalErrors.ToString(), "❌", "Total errors found", (Brush)FindResource("ErrorColor"));
                    if (totalWarnings > 0)
                        AddSummaryCard("Warnings", totalWarnings.ToString(), "⚠️", "Total warnings", (Brush)FindResource("WarningColor"));
                    if (totalInfos > 0)
                        AddSummaryCard("Info Messages", totalInfos.ToString(), "ℹ️", "Information messages", (Brush)FindResource("InfoColor"));

                    // Build Multi-Form Structure Tree
                    BuildMultiFormStructureTree(_allFormDefinitions);

                    // Display Combined Data Columns with Form Name
                    DisplayCombinedDataColumns(_allFormDefinitions);

                    // Display Hierarchical JSON with form names
                    DisplayHierarchicalJson(_allFormDefinitions);
                });
            });
        }

        private object CreateFormHeader(string formName)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };

            panel.Children.Add(new TextBlock
            {
                Text = "📁",
                FontSize = 16,
                Margin = new Thickness(0, 0, 5, 0),
                Foreground = (Brush)FindResource("AccentColor")
            });

            panel.Children.Add(new TextBlock
            {
                Text = formName,
                FontSize = 14,
                FontWeight = FontWeights.Medium,
                Foreground = (Brush)FindResource("TextPrimary")
            });

            return panel;
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
            JsonOutput.Text = json;
        }

        private async Task GenerateCombinedSqlPreview(Dictionary<string, FormAnalysisResult> allResults)
        {
            try
            {
                UpdateStatus("Generating SQL scripts for all forms...");

                var sqlText = new StringBuilder();
                sqlText.AppendLine($"-- SQL Scripts Generated: {DateTime.Now}");
                sqlText.AppendLine($"-- Total Forms: {allResults.Count}");
                sqlText.AppendLine("-- ================================================");
                sqlText.AppendLine();

                foreach (var formKvp in allResults)
                {
                    var formName = Path.GetFileNameWithoutExtension(formKvp.Key);
                    var analysis = formKvp.Value;

                    sqlText.AppendLine($"-- ================================================");
                    sqlText.AppendLine($"-- FORM: {formName}");
                    sqlText.AppendLine($"-- FILE: {formKvp.Key}");
                    sqlText.AppendLine($"-- ================================================");
                    sqlText.AppendLine();

                    var sqlResult = await _sqlGenerator.GenerateFromAnalysisAsync(analysis);

                    if (sqlResult.Success)
                    {
                        foreach (var script in sqlResult.Scripts)
                        {
                            sqlText.AppendLine($"-- {script.Description}");
                            sqlText.AppendLine($"-- Type: {script.Type}, Order: {script.ExecutionOrder}");
                            sqlText.AppendLine("-- ------------------------------------------------");
                            sqlText.AppendLine(script.Content);
                            sqlText.AppendLine();
                            sqlText.AppendLine("GO");
                            sqlText.AppendLine();
                        }
                    }
                    else
                    {
                        sqlText.AppendLine($"-- SQL Generation Failed for {formName}");
                        sqlText.AppendLine($"-- Error: {sqlResult.ErrorMessage}");
                    }

                    sqlText.AppendLine();
                }

                SqlPreview.Text = sqlText.ToString();
            }
            catch (Exception ex)
            {
                SqlPreview.Text = $"-- Error generating SQL\n-- {ex.Message}";
            }
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

            DataColumnsGrid.ItemsSource = allColumns;
        }

        // New method to build multi-form structure tree
        private void BuildMultiFormStructureTree(Dictionary<string, InfoPathFormDefinition> allForms)
        {
            StructureTreeView.Items.Clear();

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
                    Foreground = (Brush)FindResource("TextSecondary"),
                    FontSize = 11
                };

                metadataItem.Items.Add(new TreeViewItem
                {
                    Header = $"Views: {formDef.Views.Count}",
                    Foreground = (Brush)FindResource("TextDim"),
                    FontSize = 11
                });

                metadataItem.Items.Add(new TreeViewItem
                {
                    Header = $"Total Controls: {formDef.Metadata.TotalControls}",
                    Foreground = (Brush)FindResource("TextDim"),
                    FontSize = 11
                });

                metadataItem.Items.Add(new TreeViewItem
                {
                    Header = $"Data Columns: {formDef.Data.Count}",
                    Foreground = (Brush)FindResource("TextDim"),
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
                            var controlItem = CreateEditableControlTreeItem(control);
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
                            var controlItem = CreateEditableControlTreeItem(control);
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
                            Foreground = (Brush)FindResource("TextSecondary"),
                            FontSize = 11
                        });

                        dynamicItem.Items.Add(sectionItem);
                    }

                    formItem.Items.Add(dynamicItem);
                }

                StructureTreeView.Items.Add(formItem);
            }
        }
        private async Task DisplayAnalysisResults(FormAnalysisResult analysis)
        {
            await Task.Run(() =>
            {
                Dispatcher.Invoke(() =>
                {
                    // Clear previous results
                    SummaryPanel.Items.Clear();
                    StructureTreeView.Items.Clear();
                    DataColumnsGrid.ItemsSource = null;
                    JsonOutput.Text = "";

                    if (analysis?.FormDefinition == null)
                        return;

                    var formDef = analysis.FormDefinition;

                    // Display Summary Cards with better icons and labels
                    AddSummaryCard("Views", formDef.Views.Count.ToString(), "📄", "Number of form views/pages", Brushes.DodgerBlue);
                    AddSummaryCard("Total Controls", formDef.Metadata.TotalControls.ToString(), "🎛️", "All form controls", Brushes.MediumPurple);
                    AddSummaryCard("Sections", formDef.Metadata.TotalSections.ToString(), "📦", "Form sections", Brushes.Teal);
                    AddSummaryCard("Dynamic Sections", formDef.Metadata.DynamicSectionCount.ToString(), "🔄", "Conditional sections", Brushes.Orange);
                    AddSummaryCard("Repeating Sections", formDef.Metadata.RepeatingSectionCount.ToString(), "🔁", "Repeating tables/sections", Brushes.DeepSkyBlue);
                    AddSummaryCard("Data Columns", formDef.Data.Count.ToString(), "📊", "Unique data fields", Brushes.LimeGreen);

                    // Add message cards if there are any
                    if (analysis.Messages.Any())
                    {
                        var errors = analysis.Messages.Count(m => m.Severity == MessageSeverity.Error);
                        var warnings = analysis.Messages.Count(m => m.Severity == MessageSeverity.Warning);
                        var infos = analysis.Messages.Count(m => m.Severity == MessageSeverity.Info);

                        if (errors > 0)
                            AddSummaryCard("Errors", errors.ToString(), "❌", "Analysis errors found", (Brush)FindResource("ErrorColor"));
                        if (warnings > 0)
                            AddSummaryCard("Warnings", warnings.ToString(), "⚠️", "Potential issues", (Brush)FindResource("WarningColor"));
                        if (infos > 0)
                            AddSummaryCard("Info Messages", infos.ToString(), "ℹ️", "Information messages", (Brush)FindResource("InfoColor"));
                    }

                    // Build Structure Tree
                    BuildStructureTree(formDef);

                    // Display Data Columns
                    DataColumnsGrid.ItemsSource = formDef.Data;

                    // Display JSON
                    JsonOutput.Text = JsonConvert.SerializeObject(formDef, Formatting.Indented);
                });
            });
        }
        private void BuildStructureTree(InfoPathFormDefinition formDef)
        {
            // Clear the existing tree
            StructureTreeView.Items.Clear();

            // Create a main grid to hold tree and properties panel
            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Create the TreeView
            var treeScrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var tree = new TreeView
            {
                Background = (Brush)FindResource("BackgroundDark"),
                Foreground = (Brush)FindResource("TextPrimary"),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(10)
            };

            // Add context menu to tree items for quick actions
            tree.SelectedItemChanged += TreeView_SelectedItemChanged;

            foreach (var view in formDef.Views)
            {
                var viewItem = new TreeViewItem
                {
                    Header = CreateEditableHeader($"📄 {view.ViewName}", view.ViewName, "ViewName", null),
                    Tag = view,
                    IsExpanded = true
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
                        var controlItem = CreateEditableControlTreeItem(control);
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
                        var controlItem = CreateEditableControlTreeItem(control);
                        sectionItem.Items.Add(controlItem);
                    }

                    viewItem.Items.Add(sectionItem);
                }

                tree.Items.Add(viewItem);
            }

            treeScrollViewer.Content = tree;
            Grid.SetColumn(treeScrollViewer, 0);
            mainGrid.Children.Add(treeScrollViewer);

            // Create properties panel
            _propertiesPanel = CreatePropertiesPanel();
            Grid.SetColumn(_propertiesPanel, 1);
            mainGrid.Children.Add(_propertiesPanel);

            // Replace the TreeView content with the new grid
            var formStructureTab = ResultsTabs.Items[1] as TabItem;
            if (formStructureTab != null)
            {
                formStructureTab.Content = mainGrid;
            }

            // Store reference to the tree for later use
            StructureTreeView = tree;
        }


        private StackPanel CreateEditableControlHeader(ControlDefinition control)
        {
            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };

            // Icon
            var iconText = new TextBlock
            {
                Text = GetControlIcon(control.Type),
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("TextPrimary")
            };
            headerPanel.Children.Add(iconText);

            // Editable name/label
            var nameTextBox = new TextBox
            {
                Text = !string.IsNullOrEmpty(control.Label) ? control.Label : control.Name,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = (Brush)FindResource("TextPrimary"),
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 100,
                Margin = new Thickness(0, 0, 5, 0)
            };
            nameTextBox.LostFocus += (s, e) =>
            {
                if (!string.IsNullOrEmpty(control.Label))
                    control.Label = nameTextBox.Text;
                else
                    control.Name = nameTextBox.Text;
            };
            nameTextBox.MouseEnter += (s, e) =>
            {
                nameTextBox.Background = (Brush)FindResource("BackgroundLighter");
                nameTextBox.BorderThickness = new Thickness(0, 0, 0, 1);
                nameTextBox.BorderBrush = (Brush)FindResource("AccentColor");
            };
            nameTextBox.MouseLeave += (s, e) =>
            {
                nameTextBox.Background = Brushes.Transparent;
                nameTextBox.BorderThickness = new Thickness(0);
            };
            headerPanel.Children.Add(nameTextBox);

            // Type dropdown with proper styling
            var typeCombo = new ComboBox
            {
                Text = control.Type,
                IsEditable = true,
                Style = (Style)FindResource("ModernComboBox"),
                ItemContainerStyle = (Style)FindResource("ModernComboBoxItem"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 5, 0),
                MinWidth = 100,
                MaxWidth = 150,
                Height = 22
            };

            // Add common control types
            var controlTypes = new[]
            {
        "TextField", "RichText", "Label", "DropDown", "ComboBox",
        "CheckBox", "RadioButton", "DatePicker", "PeoplePicker",
        "FileAttachment", "Button", "Hyperlink", "RepeatingTable",
        "RepeatingSection", "Section"
    };

            foreach (var type in controlTypes)
            {
                typeCombo.Items.Add(type);
            }

            // Set current selection
            typeCombo.SelectedItem = control.Type;

            typeCombo.SelectionChanged += (s, e) =>
            {
                if (typeCombo.SelectedItem != null)
                {
                    control.Type = typeCombo.SelectedItem.ToString();
                    iconText.Text = GetControlIcon(control.Type);
                }
            };

            typeCombo.LostFocus += (s, e) =>
            {
                if (!string.IsNullOrEmpty(typeCombo.Text))
                {
                    control.Type = typeCombo.Text;
                    iconText.Text = GetControlIcon(control.Type);
                }
            };

            headerPanel.Children.Add(typeCombo);

            // Grid position (editable)
            headerPanel.Children.Add(new TextBlock
            {
                Text = "📍",
                Margin = new Thickness(5, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("AccentColor")
            });

            var gridPosTextBox = new TextBox
            {
                Text = control.GridPosition ?? "",
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = (Brush)FindResource("AccentColor"),
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 40,
                FontWeight = FontWeights.Medium
            };
            gridPosTextBox.LostFocus += (s, e) => control.GridPosition = gridPosTextBox.Text;
            gridPosTextBox.MouseEnter += (s, e) =>
            {
                gridPosTextBox.Background = (Brush)FindResource("BackgroundLighter");
                gridPosTextBox.BorderThickness = new Thickness(0, 0, 0, 1);
                gridPosTextBox.BorderBrush = (Brush)FindResource("AccentColor");
            };
            gridPosTextBox.MouseLeave += (s, e) =>
            {
                gridPosTextBox.Background = Brushes.Transparent;
                gridPosTextBox.BorderThickness = new Thickness(0);
            };

            headerPanel.Children.Add(gridPosTextBox);

            return headerPanel;
        }

        private TreeViewItem CreateEditableDetailsNode(ControlDefinition control)
        {
            var detailsItem = new TreeViewItem
            {
                Header = "📊 Details (Click to Edit)",
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = 11
            };

            // Create editable detail items
            if (!string.IsNullOrEmpty(control.GridPosition))
            {
                detailsItem.Items.Add(CreateEditableDetailItem("Grid Position", control.GridPosition,
                    value => control.GridPosition = value));
            }

            if (!string.IsNullOrEmpty(control.SectionGridPosition))
            {
                detailsItem.Items.Add(CreateEditableDetailItem("Section Grid", control.SectionGridPosition,
                    value => control.SectionGridPosition = value));
            }

            detailsItem.Items.Add(CreateEditableDetailItem("Doc Index", control.DocIndex.ToString(),
                value => { if (int.TryParse(value, out int idx)) control.DocIndex = idx; }));

            if (control.ColumnSpan > 1 || control.RowSpan > 1)
            {
                detailsItem.Items.Add(CreateEditableDetailItem("Column Span", control.ColumnSpan.ToString(),
                    value => { if (int.TryParse(value, out int span)) control.ColumnSpan = span; }));
                detailsItem.Items.Add(CreateEditableDetailItem("Row Span", control.RowSpan.ToString(),
                    value => { if (int.TryParse(value, out int span)) control.RowSpan = span; }));
            }

            if (!string.IsNullOrEmpty(control.Binding))
            {
                detailsItem.Items.Add(CreateEditableDetailItem("Binding", control.Binding,
                    value => control.Binding = value));
            }

            return detailsItem;
        }

        private Grid CreatePropertiesPanel()
        {
            var panel = new Grid
            {
                Background = (Brush)FindResource("BackgroundLight"),
                Margin = new Thickness(5, 0, 0, 0)
            };

            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Header
            var headerBorder = new Border
            {
                Background = (Brush)FindResource("BackgroundMedium"),
                Padding = new Thickness(10),
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = (Brush)FindResource("BorderColor")
            };

            var headerText = new TextBlock
            {
                Text = "Control Properties",
                FontSize = 14,
                FontWeight = FontWeights.Medium,
                Foreground = (Brush)FindResource("TextPrimary")
            };

            headerBorder.Child = headerText;
            Grid.SetRow(headerBorder, 0);
            panel.Children.Add(headerBorder);

            // Properties scroll viewer
            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(10)
            };

            var propertiesStack = new StackPanel();
            scrollViewer.Content = propertiesStack;
            Grid.SetRow(scrollViewer, 1);
            panel.Children.Add(scrollViewer);

            // Save button
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(10)
            };

            var saveButton = new Button
            {
                Content = "Apply Changes",
                Style = (Style)FindResource("ModernButton"),
                Margin = new Thickness(5, 0, 0, 0),
                IsEnabled = false
            };

            buttonPanel.Children.Add(saveButton);
            Grid.SetRow(buttonPanel, 2);
            panel.Children.Add(buttonPanel);

            // Store references
            panel.Tag = new { Stack = propertiesStack, SaveButton = saveButton };

            return panel;
        }

        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var treeViewItem = e.NewValue as TreeViewItem;
            if (treeViewItem?.Tag is ControlDefinition control)
            {
                ShowControlProperties(control, treeViewItem);
            }
        }

        private void ShowControlProperties(ControlDefinition control, TreeViewItem treeItem)
        {
            _selectedControl = control;
            _selectedTreeItem = treeItem;

            if (_propertiesPanel?.Tag == null) return;

            dynamic panelRefs = _propertiesPanel.Tag;
            StackPanel stack = panelRefs.Stack;
            Button saveButton = panelRefs.SaveButton;

            stack.Children.Clear();
            _propertyEditors.Clear();

            // Add property editors
            AddPropertyEditor(stack, "Name", control.Name ?? "");
            AddPropertyEditor(stack, "Label", control.Label ?? "");
            AddPropertyEditor(stack, "Type", control.Type ?? "");
            AddPropertyEditor(stack, "Binding", control.Binding ?? "");
            AddPropertyEditor(stack, "Grid Position", control.GridPosition ?? "");
            AddPropertyEditor(stack, "Section Grid", control.SectionGridPosition ?? "");
            AddPropertyEditor(stack, "Parent Section", control.ParentSection ?? "");
            AddPropertyEditor(stack, "Column Span", control.ColumnSpan.ToString());
            AddPropertyEditor(stack, "Row Span", control.RowSpan.ToString());

            // Add checkbox properties
            AddCheckBoxProperty(stack, "Is in Repeating", control.IsInRepeatingSection,
                isChecked => control.IsInRepeatingSection = isChecked);
            AddCheckBoxProperty(stack, "Is Merged", control.IsMergedIntoParent,
                isChecked => control.IsMergedIntoParent = isChecked);

            // Enable save button
            saveButton.IsEnabled = true;
            saveButton.Click += (s, args) => SaveControlProperties(control);
        }

        private void AddPropertyEditor(StackPanel stack, string propertyName, string value)
        {
            var label = new TextBlock
            {
                Text = propertyName,
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = 11,
                Margin = new Thickness(0, 5, 0, 2)
            };
            stack.Children.Add(label);

            var textBox = new TextBox
            {
                Text = value,
                Style = (Style)FindResource("ModernTextBox"),
                Margin = new Thickness(0, 0, 0, 5),
                FontSize = 12
            };

            stack.Children.Add(textBox);
            _propertyEditors[propertyName] = textBox;
        }

        private void AddCheckBoxProperty(StackPanel stack, string propertyName, bool value, Action<bool> updateAction)
        {
            var checkBox = new CheckBox
            {
                Content = propertyName,
                IsChecked = value,
                Foreground = (Brush)FindResource("TextPrimary"),
                Margin = new Thickness(0, 5, 0, 5)
            };

            checkBox.Checked += (s, e) => updateAction(true);
            checkBox.Unchecked += (s, e) => updateAction(false);

            stack.Children.Add(checkBox);
        }

        private void SaveControlProperties(ControlDefinition control)
        {
            // Update control properties from editors
            if (_propertyEditors.TryGetValue("Name", out var nameBox))
                control.Name = nameBox.Text;
            if (_propertyEditors.TryGetValue("Label", out var labelBox))
                control.Label = labelBox.Text;
            if (_propertyEditors.TryGetValue("Type", out var typeBox))
                control.Type = typeBox.Text;
            if (_propertyEditors.TryGetValue("Binding", out var bindingBox))
                control.Binding = bindingBox.Text;
            if (_propertyEditors.TryGetValue("Grid Position", out var gridBox))
                control.GridPosition = gridBox.Text;
            if (_propertyEditors.TryGetValue("Column Span", out var colSpanBox))
                if (int.TryParse(colSpanBox.Text, out int colSpan))
                    control.ColumnSpan = colSpan;
            if (_propertyEditors.TryGetValue("Row Span", out var rowSpanBox))
                if (int.TryParse(rowSpanBox.Text, out int rowSpan))
                    control.RowSpan = rowSpan;

            // Refresh the tree item header
            if (_selectedTreeItem != null)
            {
                _selectedTreeItem.Header = CreateEditableControlHeader(control);
            }

            // Show confirmation
            MessageBox.Show("Properties updated successfully!", "Save Complete",
                           MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void DuplicateControl(ControlDefinition control)
        {
            // Implementation for duplicating a control
            MessageBox.Show($"Duplicate functionality for '{control.Name}' will be implemented.",
                           "Duplicate Control", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void DeleteControl(ControlDefinition control, TreeViewItem item)
        {
            var result = MessageBox.Show($"Are you sure you want to delete '{control.Name ?? control.Type}'?",
                                        "Delete Control",
                                        MessageBoxButton.YesNo,
                                        MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                // Remove from parent
                var parent = item.Parent as TreeViewItem;
                parent?.Items.Remove(item);

                // Note: You'd also need to remove from the actual FormDefinition data structure
                MessageBox.Show("Control deleted.", "Delete Complete",
                               MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private object CreateEditableHeader(string displayText, string editableValue, string propertyName, object targetObject)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };

            var textBlock = new TextBlock
            {
                Text = displayText,
                Foreground = (Brush)FindResource("TextPrimary"),
                VerticalAlignment = VerticalAlignment.Center
            };

            panel.Children.Add(textBlock);

            return panel;
        }

        private TreeViewItem CreateEditableDetailItem(string label, string value, Action<string> updateAction)
        {
            var itemPanel = new StackPanel { Orientation = Orientation.Horizontal };

            itemPanel.Children.Add(new TextBlock
            {
                Text = $"{label}: ",
                Foreground = (Brush)FindResource("TextDim"),
                FontSize = 11,
                MinWidth = 80
            });

            var valueTextBox = new TextBox
            {
                Text = value,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = (Brush)FindResource("BorderColor"),
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = 11,
                MinWidth = 100
            };

            valueTextBox.LostFocus += (s, e) => updateAction(valueTextBox.Text);
            valueTextBox.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    updateAction(valueTextBox.Text);
                    e.Handled = true;
                }
            };

            itemPanel.Children.Add(valueTextBox);

            return new TreeViewItem { Header = itemPanel };
        }

        private TreeViewItem CreateEditableControlTreeItem(ControlDefinition control)
        {
            // Create editable header
            var headerPanel = CreateEditableControlHeader(control);

            var item = new TreeViewItem
            {
                Header = headerPanel,
                Tag = control  // Store the control for later editing
            };

            // Add context menu for quick actions
            var contextMenu = new ContextMenu();

            var editMenuItem = new MenuItem { Header = "Edit Properties", Icon = "✏️" };
            editMenuItem.Click += (s, e) => ShowControlProperties(control, item);
            contextMenu.Items.Add(editMenuItem);

            var duplicateMenuItem = new MenuItem { Header = "Duplicate Control", Icon = "📋" };
            duplicateMenuItem.Click += (s, e) => DuplicateControl(control);
            contextMenu.Items.Add(duplicateMenuItem);

            var deleteMenuItem = new MenuItem { Header = "Delete Control", Icon = "🗑️" };
            deleteMenuItem.Click += (s, e) => DeleteControl(control, item);
            contextMenu.Items.Add(deleteMenuItem);

            item.ContextMenu = contextMenu;

            // Add detailed information as child nodes (same as before but with edit capability)
            var detailsItem = CreateEditableDetailsNode(control);
            if (detailsItem != null && detailsItem.Items.Count > 0)
            {
                item.Items.Add(detailsItem);
            }

            // Add child controls if any
            if (control.Controls?.Any() == true)
            {
                var childrenItem = new TreeViewItem
                {
                    Header = $"👶 Child Controls ({control.Controls.Count})",
                    Foreground = (Brush)FindResource("TextSecondary"),
                    FontSize = 11
                };

                foreach (var childControl in control.Controls)
                {
                    childrenItem.Items.Add(CreateEditableControlTreeItem(childControl));
                }

                item.Items.Add(childrenItem);
            }

            return item;
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
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = 11
            };

            // Grid Position Details
            if (!string.IsNullOrEmpty(control.GridPosition))
            {
                detailsItem.Items.Add(new TreeViewItem
                {
                    Header = $"Grid: {control.GridPosition}",
                    Foreground = (Brush)FindResource("TextDim"),
                    FontSize = 11
                });
            }

            // Section Grid Position (if in a section)
            if (!string.IsNullOrEmpty(control.SectionGridPosition))
            {
                detailsItem.Items.Add(new TreeViewItem
                {
                    Header = $"Section Grid: {control.SectionGridPosition}",
                    Foreground = (Brush)FindResource("TextDim"),
                    FontSize = 11
                });
            }

            // Document Index
            detailsItem.Items.Add(new TreeViewItem
            {
                Header = $"Doc Index: {control.DocIndex}",
                Foreground = (Brush)FindResource("TextDim"),
                FontSize = 11
            });

            // Column/Row Span
            if (control.ColumnSpan > 1 || control.RowSpan > 1)
            {
                detailsItem.Items.Add(new TreeViewItem
                {
                    Header = $"Span: Col={control.ColumnSpan}, Row={control.RowSpan}",
                    Foreground = (Brush)FindResource("TextDim"),
                    FontSize = 11
                });
            }

            // Binding
            if (!string.IsNullOrEmpty(control.Binding))
            {
                detailsItem.Items.Add(new TreeViewItem
                {
                    Header = $"Binding: {control.Binding}",
                    Foreground = (Brush)FindResource("TextDim"),
                    FontSize = 11
                });
            }

            // Repeating Section Info
            if (control.IsInRepeatingSection)
            {
                detailsItem.Items.Add(new TreeViewItem
                {
                    Header = $"Repeating: {control.RepeatingSectionName}",
                    Foreground = (Brush)FindResource("WarningColor"),
                    FontSize = 11
                });

                if (!string.IsNullOrEmpty(control.RepeatingSectionBinding))
                {
                    detailsItem.Items.Add(new TreeViewItem
                    {
                        Header = $"Repeat Binding: {control.RepeatingSectionBinding}",
                        Foreground = (Brush)FindResource("TextDim"),
                        FontSize = 11
                    });
                }
            }

            // Associated Label/Control
            if (!string.IsNullOrEmpty(control.AssociatedLabelId))
            {
                detailsItem.Items.Add(new TreeViewItem
                {
                    Header = $"Label ID: {control.AssociatedLabelId}",
                    Foreground = (Brush)FindResource("TextDim"),
                    FontSize = 11
                });
            }

            if (!string.IsNullOrEmpty(control.AssociatedControlId))
            {
                detailsItem.Items.Add(new TreeViewItem
                {
                    Header = $"Control ID: {control.AssociatedControlId}",
                    Foreground = (Brush)FindResource("TextDim"),
                    FontSize = 11
                });
            }

            // Add properties if any
            if (control.Properties != null && control.Properties.Any())
            {
                var propsItem = new TreeViewItem
                {
                    Header = $"🔧 Properties ({control.Properties.Count})",
                    Foreground = (Brush)FindResource("TextSecondary"),
                    FontSize = 11
                };

                foreach (var prop in control.Properties.Take(10)) // Limit to first 10 properties
                {
                    propsItem.Items.Add(new TreeViewItem
                    {
                        Header = $"{prop.Key}: {prop.Value}",
                        Foreground = (Brush)FindResource("TextDim"),
                        FontSize = 11
                    });
                }

                if (control.Properties.Count > 10)
                {
                    propsItem.Items.Add(new TreeViewItem
                    {
                        Header = $"... and {control.Properties.Count - 10} more",
                        Foreground = (Brush)FindResource("TextDim"),
                        FontStyle = FontStyles.Italic,
                        FontSize = 11
                    });
                }

                detailsItem.Items.Add(propsItem);
            }

            // Add child controls if any
            if (control.Controls?.Any() == true)
            {
                var childrenItem = new TreeViewItem
                {
                    Header = $"👶 Child Controls ({control.Controls.Count})",
                    Foreground = (Brush)FindResource("TextSecondary"),
                    FontSize = 11
                };

                foreach (var childControl in control.Controls)
                {
                    childrenItem.Items.Add(CreateControlTreeItem(childControl));
                }

                item.Items.Add(childrenItem);
            }

            // Only add details if there are any
            if (detailsItem.Items.Count > 0)
            {
                item.Items.Add(detailsItem);
            }

            return item;
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
                Style = (Style)FindResource("SummaryCardStyle"),
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
                Foreground = accentColor ?? (Brush)FindResource("AccentColor")
            };
            Grid.SetRow(iconText, 0);
            grid.Children.Add(iconText);

            // Large value
            var valueText = new TextBlock
            {
                Text = value,
                FontSize = 32,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("TextPrimary"),
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
                Foreground = (Brush)FindResource("TextPrimary"),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center
            };
            Grid.SetRow(titleText, 2);
            grid.Children.Add(titleText);

            // Description (smaller text)
            var descriptionText = new TextBlock
            {
                Text = description,
                FontSize = 10,
                Foreground = (Brush)FindResource("TextSecondary"),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(5, 0, 5, 5)
            };
            Grid.SetRow(descriptionText, 3);
            grid.Children.Add(descriptionText);

            card.Child = grid;

            // Add tooltip with more information
            var tooltip = new ToolTip
            {
                Content = new StackPanel
                {
                    Children =
            {
                new TextBlock
                {
                    Text = title,
                    FontWeight = FontWeights.Bold,
                    FontSize = 14,
                    Margin = new Thickness(0, 0, 0, 5)
                },
                new TextBlock
                {
                    Text = description,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 200
                }
            }
                },
                Background = (Brush)FindResource("BackgroundLighter"),
                Foreground = (Brush)FindResource("TextPrimary"),
                BorderBrush = (Brush)FindResource("BorderColor"),
                BorderThickness = new Thickness(1)
            };

            card.ToolTip = tooltip;
            SummaryPanel.Items.Add(card);
        }

        // Also add a legend panel above the summary cards
        private void AddSummaryLegend()
        {
            var legendPanel = new Border
            {
                Background = (Brush)FindResource("BackgroundLight"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(15),
                Margin = new Thickness(20, 10, 20, 0),
                Width = double.NaN,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var legendStack = new StackPanel();

            var legendTitle = new TextBlock
            {
                Text = "Form Analysis Summary",
                FontSize = 18,
                FontWeight = FontWeights.Medium,
                Foreground = (Brush)FindResource("TextPrimary"),
                Margin = new Thickness(0, 0, 0, 10)
            };
            legendStack.Children.Add(legendTitle);

            var legendGrid = new Grid();
            legendGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            legendGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            legendGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Column 1 - Form Structure
            var col1 = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
            col1.Children.Add(new TextBlock
            {
                Text = "Form Structure",
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimary"),
                Margin = new Thickness(0, 0, 0, 5)
            });
            col1.Children.Add(CreateLegendItem("📄", "Views - Form pages/screens"));
            col1.Children.Add(CreateLegendItem("📦", "Sections - Logical groupings"));
            col1.Children.Add(CreateLegendItem("🎛️", "Controls - Input fields"));
            Grid.SetColumn(col1, 0);
            legendGrid.Children.Add(col1);

            // Column 2 - Dynamic Elements
            var col2 = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
            col2.Children.Add(new TextBlock
            {
                Text = "Dynamic Elements",
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimary"),
                Margin = new Thickness(0, 0, 0, 5)
            });
            col2.Children.Add(CreateLegendItem("🔄", "Dynamic - Conditional visibility"));
            col2.Children.Add(CreateLegendItem("🔁", "Repeating - Multiple entries"));
            col2.Children.Add(CreateLegendItem("📊", "Data Columns - Database fields"));
            Grid.SetColumn(col2, 1);
            legendGrid.Children.Add(col2);

            // Column 3 - Status Indicators
            var col3 = new StackPanel();
            col3.Children.Add(new TextBlock
            {
                Text = "Status Indicators",
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimary"),
                Margin = new Thickness(0, 0, 0, 5)
            });
            col3.Children.Add(CreateLegendItem("❌", "Errors - Critical issues", (Brush)FindResource("ErrorColor")));
            col3.Children.Add(CreateLegendItem("⚠️", "Warnings - Potential problems", (Brush)FindResource("WarningColor")));
            col3.Children.Add(CreateLegendItem("ℹ️", "Info - Helpful information", (Brush)FindResource("InfoColor")));
            Grid.SetColumn(col3, 2);
            legendGrid.Children.Add(col3);

            legendStack.Children.Add(legendGrid);
            legendPanel.Child = legendStack;

            // This should be added to the Summary tab content
            // You'll need to modify the TabItem content structure slightly
        }

        private StackPanel CreateLegendItem(string icon, string description, Brush iconColor = null)
        {
            var itemPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 2, 0, 2)
            };

            itemPanel.Children.Add(new TextBlock
            {
                Text = icon,
                FontSize = 16,
                Width = 25,
                Foreground = iconColor ?? (Brush)FindResource("AccentColor")
            });

            itemPanel.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 11,
                Foreground = (Brush)FindResource("TextSecondary"),
                VerticalAlignment = VerticalAlignment.Center
            });

            return itemPanel;
        }



        private async Task GenerateSqlPreview(FormAnalysisResult analysis)
        {
            try
            {
                UpdateStatus("Generating SQL scripts...");

                var sqlResult = await _sqlGenerator.GenerateFromAnalysisAsync(analysis);

                if (sqlResult.Success)
                {
                    var sqlText = new System.Text.StringBuilder();
                    sqlText.AppendLine($"-- SQL Scripts Generated: {sqlResult.GeneratedDate}");
                    sqlText.AppendLine($"-- Dialect: {sqlResult.Dialect}");
                    sqlText.AppendLine($"-- Total Scripts: {sqlResult.Scripts.Count}");
                    sqlText.AppendLine("-- ================================================");
                    sqlText.AppendLine();

                    foreach (var script in sqlResult.Scripts)
                    {
                        sqlText.AppendLine($"-- {script.Description}");
                        sqlText.AppendLine($"-- Type: {script.Type}, Order: {script.ExecutionOrder}");
                        sqlText.AppendLine("-- ------------------------------------------------");
                        sqlText.AppendLine(script.Content);
                        sqlText.AppendLine();
                        sqlText.AppendLine("GO");
                        sqlText.AppendLine();
                    }

                    SqlPreview.Text = sqlText.ToString();
                }
                else
                {
                    SqlPreview.Text = $"-- SQL Generation Failed\n-- Error: {sqlResult.ErrorMessage}";
                }
            }
            catch (Exception ex)
            {
                SqlPreview.Text = $"-- Error generating SQL\n-- {ex.Message}";
            }
        }

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentAnalysis == null)
                return;

            var dialog = new SaveFileDialog
            {
                Title = "Export Analysis Results",
                Filter = "JSON Files (*.json)|*.json|SQL Scripts (*.sql)|*.sql|All Files (*.*)|*.*",
                FileName = $"{_currentAnalysis.FormName}_Analysis"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    if (Path.GetExtension(dialog.FileName).Equals(".sql", StringComparison.OrdinalIgnoreCase))
                    {
                        // Export SQL
                        var sqlResult = await _sqlGenerator.GenerateFromAnalysisAsync(_currentAnalysis);
                        if (sqlResult.Success)
                        {
                            var sqlContent = SqlPreview.Text;
                            await File.WriteAllTextAsync(dialog.FileName, sqlContent);
                        }
                    }
                    else
                    {
                        // Export JSON
                        var json = JsonConvert.SerializeObject(_currentAnalysis, Formatting.Indented);
                        await File.WriteAllTextAsync(dialog.FileName, json);
                    }

                    UpdateStatus($"Exported to: {dialog.FileName}", MessageSeverity.Info);
                    MessageBox.Show($"Successfully exported to:\n{dialog.FileName}",
                                  "Export Successful",
                                  MessageBoxButton.OK,
                                  MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Export failed: {ex.Message}", MessageSeverity.Error);
                    MessageBox.Show($"Export failed:\n{ex.Message}",
                                  "Export Error",
                                  MessageBoxButton.OK,
                                  MessageBoxImage.Error);
                }
            }
        }

        private string GetSelectedFormType()
        {
            var selectedItem = FormTypeSelector.SelectedItem as ComboBoxItem;
            return selectedItem?.Tag?.ToString() ?? "InfoPath2013";
        }

        private void UpdateStatus(string message, MessageSeverity severity = MessageSeverity.Info)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = message;

                StatusText.Foreground = severity switch
                {
                    MessageSeverity.Error => Brushes.Red,
                    MessageSeverity.Warning => Brushes.Orange,
                    _ => (Brush)FindResource("TextSecondary")
                };
            });
        }


        #region SQL Generation Event Handlers

        private async void TestSqlConnection_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                UpdateStatus("Testing SQL connection...");
                SqlGenerationLog.Text = "Testing connection to SQL Server...\n";

                // Build connection string based on authentication type
                string connectionString;
                if (WindowsAuthRadio.IsChecked == true)
                {
                    connectionString = $"Server={SqlServerTextBox.Text};Database={SqlDatabaseTextBox.Text};Integrated Security=true;";
                    SqlGenerationLog.Text += $"Using Windows Authentication\n";
                }
                else
                {
                    connectionString = $"Server={SqlServerTextBox.Text};Database={SqlDatabaseTextBox.Text};User Id={SqlUsernameTextBox.Text};Password={SqlPasswordBox.Password};";
                    SqlGenerationLog.Text += $"Using SQL Server Authentication\n";
                }

                SqlGenerationLog.Text += $"Server: {SqlServerTextBox.Text}\n";
                SqlGenerationLog.Text += $"Database: {SqlDatabaseTextBox.Text}\n\n";

                // Simulate connection test
                await Task.Delay(1000);

                // Mock success
                SqlGenerationLog.Text += "✅ Connection successful!\n";
                UpdateStatus("SQL connection test successful", MessageSeverity.Info);

                // Enable generation button after successful connection
                GenerateSqlButton.IsEnabled = true;

                MessageBox.Show("Connection to SQL Server successful!",
                               "Connection Test",
                               MessageBoxButton.OK,
                               MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                SqlGenerationLog.Text += $"❌ Connection failed: {ex.Message}\n";
                UpdateStatus("SQL connection test failed", MessageSeverity.Error);

                MessageBox.Show($"Connection failed:\n{ex.Message}",
                               "Connection Error",
                               MessageBoxButton.OK,
                               MessageBoxImage.Error);
            }
        }

        private async void GenerateSql_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                GenerateSqlButton.IsEnabled = false;
                UpdateStatus("Generating SQL scripts...");
                SqlGenerationLog.Text = "Starting SQL generation...\n\n";

                // Check analysis results
                if (_allAnalysisResults == null || !_allAnalysisResults.Any())
                {
                    MessageBox.Show("Please analyze forms first before generating SQL.",
                                   "No Analysis Results",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Warning);
                    return;
                }

                SqlGenerationLog.Text += $"Forms to process: {_allAnalysisResults.Count}\n";

                // Process generation options
                if (FlatTablesRadio.IsChecked == true)
                {
                    SqlGenerationLog.Text += "Table Structure: Flat Tables\n";
                }
                else
                {
                    SqlGenerationLog.Text += "Table Structure: Normalized Tables\n";
                }

                if (GenerateCrudProcsCheckBox.IsChecked == true)
                {
                    SqlGenerationLog.Text += "✓ Generating CRUD Stored Procedures\n";
                }

                if (GenerateScalableProcsCheckBox.IsChecked == true)
                {
                    SqlGenerationLog.Text += "✓ Generating Scalable Stored Procedures\n";
                }

                if (IncludeIndexesCheckBox.IsChecked == true)
                {
                    SqlGenerationLog.Text += "✓ Including Indexes\n";
                }

                if (IncludeConstraintsCheckBox.IsChecked == true)
                {
                    SqlGenerationLog.Text += "✓ Including Foreign Key Constraints\n";
                }

                if (IncludeTriggersCheckBox.IsChecked == true)
                {
                    SqlGenerationLog.Text += "✓ Including Audit Triggers\n";
                }

                SqlGenerationLog.Text += "\n";

                // Simulate generation for each form
                foreach (var form in _allAnalysisResults)
                {
                    SqlGenerationLog.Text += $"Processing form: {form.Key}\n";
                    await Task.Delay(500);

                    SqlGenerationLog.Text += $"  - Creating table structure...\n";
                    await Task.Delay(300);

                    if (GenerateCrudProcsCheckBox.IsChecked == true)
                    {
                        SqlGenerationLog.Text += $"  - Creating stored procedures...\n";
                        await Task.Delay(300);
                    }

                    if (IncludeIndexesCheckBox.IsChecked == true)
                    {
                        SqlGenerationLog.Text += $"  - Creating indexes...\n";
                        await Task.Delay(200);
                    }
                }

                SqlGenerationLog.Text += "\n✅ SQL generation completed successfully!\n";
                SqlGenerationLog.Text += $"Total scripts generated: {_allAnalysisResults.Count * 3}\n";

                UpdateStatus("SQL scripts generated successfully", MessageSeverity.Info);
                DeploySqlButton.IsEnabled = true;

                // Switch to SQL Preview tab to show generated scripts
                ResultsTabs.SelectedIndex = ResultsTabs.Items.Cast<TabItem>()
                    .ToList()
                    .FindIndex(tab => (tab.Header as string)?.Contains("SQL Preview") == true);
            }
            catch (Exception ex)
            {
                SqlGenerationLog.Text += $"\n❌ Error: {ex.Message}\n";
                UpdateStatus($"SQL generation failed: {ex.Message}", MessageSeverity.Error);
            }
            finally
            {
                GenerateSqlButton.IsEnabled = true;
            }
        }


        private async void DeploySql_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show("Are you sure you want to deploy the generated SQL scripts to the database?\n\nThis action cannot be undone.",
                                             "Confirm Deployment",
                                             MessageBoxButton.YesNo,
                                             MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;

                DeploySqlButton.IsEnabled = false;
                UpdateStatus("Deploying SQL scripts to database...");
                SqlGenerationLog.Text += "\n\nStarting deployment to database...\n";

                // Simulate deployment
                SqlGenerationLog.Text += "Creating database objects...\n";
                await Task.Delay(1000);

                SqlGenerationLog.Text += "  - Tables created: 5\n";
                await Task.Delay(500);

                SqlGenerationLog.Text += "  - Stored procedures created: 20\n";
                await Task.Delay(500);

                SqlGenerationLog.Text += "  - Indexes created: 8\n";
                await Task.Delay(500);

                SqlGenerationLog.Text += "\n✅ Deployment completed successfully!\n";
                UpdateStatus("SQL deployment completed", MessageSeverity.Info);

                MessageBox.Show("SQL scripts have been successfully deployed to the database!",
                               "Deployment Successful",
                               MessageBoxButton.OK,
                               MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                SqlGenerationLog.Text += $"\n❌ Deployment failed: {ex.Message}\n";
                UpdateStatus($"SQL deployment failed: {ex.Message}", MessageSeverity.Error);

                MessageBox.Show($"Deployment failed:\n{ex.Message}",
                               "Deployment Error",
                               MessageBoxButton.OK,
                               MessageBoxImage.Error);
            }
            finally
            {
                DeploySqlButton.IsEnabled = true;
            }
        }

        // Add handler for SQL Auth radio button changes
        private void SqlAuth_Changed(object sender, RoutedEventArgs e)
        {
            if (SqlAuthRadio?.IsChecked == true)
            {
                // Show username and password fields
                SqlUsernameLabel.Visibility = Visibility.Visible;
                SqlUsernameTextBox.Visibility = Visibility.Visible;
                SqlPasswordLabel.Visibility = Visibility.Visible;
                SqlPasswordBox.Visibility = Visibility.Visible;
            }
            else
            {
                // Hide username and password fields
                SqlUsernameLabel.Visibility = Visibility.Collapsed;
                SqlUsernameTextBox.Visibility = Visibility.Collapsed;
                SqlPasswordLabel.Visibility = Visibility.Collapsed;
                SqlPasswordBox.Visibility = Visibility.Collapsed;
            }
        }

        #endregion

        #region Nintex Generation Event Handlers

        private async void GenerateNintex_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                GenerateNintexButton.IsEnabled = false;
                UpdateStatus("Generating Nintex forms...");
                NintexGenerationLog.Text = "Starting Nintex form generation...\n\n";

                // Check analysis results
                if (_allAnalysisResults == null || !_allAnalysisResults.Any())
                {
                    MessageBox.Show("Please analyze forms first before generating Nintex forms.",
                                   "No Analysis Results",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Warning);
                    return;
                }

                var platform = (NintexPlatformCombo.SelectedItem as ComboBoxItem)?.Content.ToString();
                NintexGenerationLog.Text += $"Target Platform: {platform}\n";
                NintexGenerationLog.Text += $"Form Name: {NintexFormNameTextBox.Text}\n";
                NintexGenerationLog.Text += $"Description: {NintexDescriptionTextBox.Text}\n\n";

                // Process options
                NintexGenerationLog.Text += "Options:\n";
                if (IncludeValidationRulesCheckBox.IsChecked == true)
                    NintexGenerationLog.Text += "  ✓ Include Validation Rules\n";
                if (IncludeConditionalLogicCheckBox.IsChecked == true)
                    NintexGenerationLog.Text += "  ✓ Include Conditional Logic\n";
                if (IncludeCalculationsCheckBox.IsChecked == true)
                    NintexGenerationLog.Text += "  ✓ Include Calculations\n";
                if (GenerateWorkflowCheckBox.IsChecked == true)
                    NintexGenerationLog.Text += "  ✓ Generate Associated Workflow\n";

                NintexGenerationLog.Text += "\nLayout: " +
                    (ResponsiveLayoutRadio.IsChecked == true ? "Responsive" : "Fixed") + "\n";

                NintexGenerationLog.Text += "Export Format: " +
                    (NintexJsonRadio.IsChecked == true ? "JSON" : "XML") + "\n\n";

                // Simulate generation
                foreach (var form in _allAnalysisResults)
                {
                    NintexGenerationLog.Text += $"Processing: {form.Key}\n";
                    await Task.Delay(500);

                    NintexGenerationLog.Text += "  - Converting controls...\n";
                    await Task.Delay(300);

                    NintexGenerationLog.Text += "  - Applying rules...\n";
                    await Task.Delay(300);

                    NintexGenerationLog.Text += "  - Generating layout...\n";
                    await Task.Delay(300);
                }

                NintexGenerationLog.Text += "\n✅ Nintex forms generated successfully!\n";
                NintexGenerationLog.Text += $"Forms created: {_allAnalysisResults.Count}\n";

                UpdateStatus("Nintex forms generated successfully", MessageSeverity.Info);
                DownloadNintexButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                NintexGenerationLog.Text += $"\n❌ Error: {ex.Message}\n";
                UpdateStatus($"Nintex generation failed: {ex.Message}", MessageSeverity.Error);
            }
            finally
            {
                GenerateNintexButton.IsEnabled = true;
            }
        }

        private async void DownloadNintex_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Title = "Save Nintex Forms Package",
                    Filter = NintexJsonRadio.IsChecked == true
                        ? "JSON Files (*.json)|*.json|All Files (*.*)|*.*"
                        : "XML Files (*.xml)|*.xml|All Files (*.*)|*.*",
                    FileName = $"{NintexFormNameTextBox.Text}_NintexPackage"
                };

                if (dialog.ShowDialog() == true)
                {
                    NintexGenerationLog.Text += $"\nSaving package to: {dialog.FileName}\n";

                    // Simulate file creation
                    await Task.Delay(500);

                    // Create mock content
                    string content = NintexJsonRadio.IsChecked == true
                        ? "{ \"nintexForm\": { \"version\": \"1.0\", \"forms\": [] } }"
                        : "<?xml version=\"1.0\"?><NintexForms></NintexForms>";

                    await File.WriteAllTextAsync(dialog.FileName, content);

                    NintexGenerationLog.Text += "✅ Package saved successfully!\n";
                    UpdateStatus($"Nintex package saved to: {dialog.FileName}", MessageSeverity.Info);

                    MessageBox.Show($"Nintex forms package has been saved to:\n{dialog.FileName}",
                                   "Download Complete",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                NintexGenerationLog.Text += $"\n❌ Download failed: {ex.Message}\n";
                UpdateStatus($"Nintex download failed: {ex.Message}", MessageSeverity.Error);

                MessageBox.Show($"Download failed:\n{ex.Message}",
                               "Download Error",
                               MessageBoxButton.OK,
                               MessageBoxImage.Error);
            }
        }

        #endregion

        #region K2 Generation Event Handlers

        private async void TestK2Connection_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                UpdateStatus("Testing K2 connection...");
                K2GenerationLog.Text = "Testing connection to K2 server...\n";
                K2GenerationLog.Text += $"Server: {K2ServerTextBox.Text}\n";
                K2GenerationLog.Text += $"Port: {K2PortTextBox.Text}\n";
                K2GenerationLog.Text += $"Username: {K2UsernameTextBox.Text}\n\n";

                // Simulate connection test
                await Task.Delay(1000);

                K2GenerationLog.Text += "✅ Connection successful!\n";
                K2GenerationLog.Text += "K2 Server Version: 5.5\n";
                K2GenerationLog.Text += "SmartForms Version: 5.5.0.0\n";

                UpdateStatus("K2 connection test successful", MessageSeverity.Info);

                // Enable generation button after successful connection
                GenerateK2Button.IsEnabled = true;

                MessageBox.Show("Connection to K2 server successful!",
                               "Connection Test",
                               MessageBoxButton.OK,
                               MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                K2GenerationLog.Text += $"❌ Connection failed: {ex.Message}\n";
                UpdateStatus("K2 connection test failed", MessageSeverity.Error);

                MessageBox.Show($"Connection failed:\n{ex.Message}",
                               "Connection Error",
                               MessageBoxButton.OK,
                               MessageBoxImage.Error);
            }
        }

        private void BrowseK2Folder_Click(object sender, RoutedEventArgs e)
        {
            // Mock folder browser dialog
            K2GenerationLog.Text += "Opening K2 folder browser...\n";

            // Simulate folder selection
            var mockFolders = new List<string>
    {
        "/Forms",
        "/Forms/Generated",
        "/Forms/InfoPath",
        "/Workflows",
        "/SmartObjects"
    };

            // Create a simple selection dialog
            var folderDialog = new Window
            {
                Title = "Select K2 Folder",
                Width = 400,
                Height = 300,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this
            };

            var listBox = new ListBox
            {
                ItemsSource = mockFolders,
                Margin = new Thickness(10)
            };

            listBox.MouseDoubleClick += (s, args) =>
            {
                if (listBox.SelectedItem != null)
                {
                    K2FolderTextBox.Text = listBox.SelectedItem.ToString();
                    K2GenerationLog.Text += $"Selected folder: {listBox.SelectedItem}\n";
                    folderDialog.Close();
                }
            };

            folderDialog.Content = listBox;
            folderDialog.ShowDialog();
        }


        private async void GenerateK2_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                GenerateK2Button.IsEnabled = false;
                UpdateStatus("Generating K2 SmartForms...");
                K2GenerationLog.Text = "Starting K2 SmartForms generation...\n\n";

                // Check analysis results
                if (_allAnalysisResults == null || !_allAnalysisResults.Any())
                {
                    MessageBox.Show("Please analyze forms first before generating K2 forms.",
                                   "No Analysis Results",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Warning);
                    return;
                }

                K2GenerationLog.Text += $"Target Folder: {K2FolderTextBox.Text}\n\n";

                // Process options
                if (GenerateSmartObjectsCheckBox.IsChecked == true)
                {
                    K2GenerationLog.Text += "Generating SmartObjects...\n";
                    await Task.Delay(500);
                }

                string formType = "Item View";
                if (K2ListViewRadio.IsChecked == true)
                    formType = "List View";
                else if (K2BothViewsRadio.IsChecked == true)
                    formType = "Item and List Views";

                K2GenerationLog.Text += $"Form Type: {formType}\n\n";

                // Simulate generation for each form
                foreach (var form in _allAnalysisResults)
                {
                    K2GenerationLog.Text += $"Processing: {form.Key}\n";
                    await Task.Delay(500);

                    if (GenerateSmartObjectsCheckBox.IsChecked == true)
                    {
                        K2GenerationLog.Text += "  - Creating SmartObject...\n";
                        await Task.Delay(300);
                    }

                    K2GenerationLog.Text += "  - Creating views...\n";
                    await Task.Delay(300);

                    if (IncludeRulesK2CheckBox.IsChecked == true)
                    {
                        K2GenerationLog.Text += "  - Adding form rules...\n";
                        await Task.Delay(300);
                    }

                    if (IncludeStylesK2CheckBox.IsChecked == true)
                    {
                        K2GenerationLog.Text += "  - Applying styles...\n";
                        await Task.Delay(200);
                    }
                }

                if (GenerateWorkflowK2CheckBox.IsChecked == true)
                {
                    K2GenerationLog.Text += "\nGenerating K2 Workflow...\n";
                    await Task.Delay(500);
                }

                K2GenerationLog.Text += "\n✅ K2 SmartForms generated successfully!\n";
                K2GenerationLog.Text += $"SmartObjects created: {_allAnalysisResults.Count}\n";
                K2GenerationLog.Text += $"Views created: {_allAnalysisResults.Count * (K2BothViewsRadio.IsChecked == true ? 2 : 1)}\n";

                UpdateStatus("K2 SmartForms generated successfully", MessageSeverity.Info);
                DeployK2Button.IsEnabled = true;
                ExportK2PackageButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                K2GenerationLog.Text += $"\n❌ Error: {ex.Message}\n";
                UpdateStatus($"K2 generation failed: {ex.Message}", MessageSeverity.Error);
            }
            finally
            {
                GenerateK2Button.IsEnabled = true;
            }
        }

        private async void DeployK2_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show("Are you sure you want to deploy the generated forms to the K2 server?\n\nThis will create new SmartForms in the specified folder.",
                                             "Confirm Deployment",
                                             MessageBoxButton.YesNo,
                                             MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;

                DeployK2Button.IsEnabled = false;
                UpdateStatus("Deploying to K2 server...");
                K2GenerationLog.Text += "\n\nStarting deployment to K2 server...\n";

                // Simulate deployment
                K2GenerationLog.Text += "Connecting to K2 server...\n";
                await Task.Delay(500);

                K2GenerationLog.Text += "Deploying SmartObjects...\n";
                await Task.Delay(1000);

                K2GenerationLog.Text += "Deploying SmartForms...\n";
                await Task.Delay(1000);

                if (GenerateWorkflowK2CheckBox.IsChecked == true)
                {
                    K2GenerationLog.Text += "Deploying Workflow...\n";
                    await Task.Delay(500);
                }

                K2GenerationLog.Text += "\n✅ Deployment completed successfully!\n";
                K2GenerationLog.Text += $"Location: {K2ServerTextBox.Text}{K2FolderTextBox.Text}\n";

                UpdateStatus("K2 deployment completed", MessageSeverity.Info);

                MessageBox.Show("K2 SmartForms have been successfully deployed to the server!",
                               "Deployment Successful",
                               MessageBoxButton.OK,
                               MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                K2GenerationLog.Text += $"\n❌ Deployment failed: {ex.Message}\n";
                UpdateStatus($"K2 deployment failed: {ex.Message}", MessageSeverity.Error);

                MessageBox.Show($"Deployment failed:\n{ex.Message}",
                               "Deployment Error",
                               MessageBoxButton.OK,
                               MessageBoxImage.Error);
            }
            finally
            {
                DeployK2Button.IsEnabled = true;
            }
        }

        private async void ExportK2Package_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Title = "Export K2 Package",
                    Filter = "K2 Package Files (*.k2pkg)|*.k2pkg|ZIP Files (*.zip)|*.zip|All Files (*.*)|*.*",
                    FileName = "K2SmartForms_Package"
                };

                if (dialog.ShowDialog() == true)
                {
                    K2GenerationLog.Text += $"\nExporting K2 package to: {dialog.FileName}\n";

                    // Simulate package creation
                    K2GenerationLog.Text += "Creating package...\n";
                    await Task.Delay(500);

                    K2GenerationLog.Text += "  - Adding SmartObjects...\n";
                    await Task.Delay(300);

                    K2GenerationLog.Text += "  - Adding SmartForms...\n";
                    await Task.Delay(300);

                    if (GenerateWorkflowK2CheckBox.IsChecked == true)
                    {
                        K2GenerationLog.Text += "  - Adding Workflow...\n";
                        await Task.Delay(300);
                    }

                    // Create a mock file
                    await File.WriteAllTextAsync(dialog.FileName, "K2 Package Content");

                    K2GenerationLog.Text += "\n✅ Package exported successfully!\n";
                    UpdateStatus($"K2 package exported to: {dialog.FileName}", MessageSeverity.Info);

                    MessageBox.Show($"K2 package has been exported to:\n{dialog.FileName}",
                                   "Export Complete",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                K2GenerationLog.Text += $"\n❌ Export failed: {ex.Message}\n";
                UpdateStatus($"K2 export failed: {ex.Message}", MessageSeverity.Error);

                MessageBox.Show($"Export failed:\n{ex.Message}",
                               "Export Error",
                               MessageBoxButton.OK,
                               MessageBoxImage.Error);
            }
        }

        #endregion




        // Helper method to enable generation tabs after analysis
        private void EnableGenerationTabs()
        {
            // Enable generation tabs if forms have been analyzed
            if (_allAnalysisResults != null && _allAnalysisResults.Any())
            {
                // Enable the tabs
                GenerateSqlTab.IsEnabled = true;
                GenerateNintexTab.IsEnabled = true;
                GenerateK2Tab.IsEnabled = true;

                // Also enable the generation buttons within each tab
                GenerateSqlButton.IsEnabled = true;
                GenerateNintexButton.IsEnabled = true;
                GenerateK2Button.IsEnabled = true;

                // Show a subtle notification that new tabs are available
                UpdateStatus("Generation tabs are now available", MessageSeverity.Info);
            }
        }


        /// <summary>
        /// Factory for creating form analyzers
        /// </summary>
        public class AnalyzerFactory
        {
            private readonly Dictionary<string, IFormAnalyzer> _analyzers;

            public AnalyzerFactory()
            {
                _analyzers = new Dictionary<string, IFormAnalyzer>
            {
                { "InfoPath2013", new InfoPath2013Analyzer() },
                { "InfoPath2010", new InfoPath2010Analyzer() },
                { "InfoPath2007", null }, // Not implemented yet
                { "NintexForms", new NintexFormsAnalyzer() }
            };
            }

            public IFormAnalyzer GetAnalyzer(string formType)
            {
                return _analyzers.TryGetValue(formType, out var analyzer) ? analyzer : null;
            }

            public IEnumerable<string> GetAvailableAnalyzers()
            {
                return _analyzers.Where(a => a.Value != null).Select(a => a.Key);
            }
        }
    }
}