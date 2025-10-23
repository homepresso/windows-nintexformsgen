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
// using FormGenerator.Analyzers.InfoPath; // (Duplicate/typo namespace—remove if not needed)
using FormGenerator.Core.Interfaces;
using FormGenerator.Core.Models;
using FormGenerator.Services;
using FormGenerator.Analyzers.InfoPath;

namespace FormGenerator.Views
{
    /// <summary>
    /// Main window for the Form Analyzer Pro application
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<FormFileInfo> _uploadedFiles;
        private readonly AnalyzerFactory _analyzerFactory;
        private readonly SqlGeneratorService _sqlGenerator;
        private FormAnalysisResult _currentAnalysis;
        internal Dictionary<string, FormAnalysisResult> _allAnalysisResults = new Dictionary<string, FormAnalysisResult>();
        internal Dictionary<string, InfoPathFormDefinition> _allFormDefinitions = new Dictionary<string, InfoPathFormDefinition>();

        // Partial class handlers - initialized before InitializeComponent
        private MainWindowGenerationHandlers _generationHandlers;
        private MainWindowAnalysisHandlers _analysisHandlers;

        public MainWindow()
        {
            // Initialize collections and services first
            _uploadedFiles = new ObservableCollection<FormFileInfo>();
            _analyzerFactory = new AnalyzerFactory();
            _sqlGenerator = new SqlGeneratorService();

            // Initialize handlers BEFORE InitializeComponent (important!)
            _generationHandlers = new MainWindowGenerationHandlers(this);
            _analysisHandlers = new MainWindowAnalysisHandlers(this);

            // Now initialize the UI components
            InitializeComponent();

            // Set up data bindings and UI elements
            FileListBox.ItemsSource = _uploadedFiles;

            // Set version
            VersionText.Text = $"v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}";

            // Wire up SQL Auth radio button events
            SqlAuthRadio.Checked += SqlAuth_Changed;
            WindowsAuthRadio.Checked += SqlAuth_Changed;

            // Wire up new toolbar buttons for editing features
            WireUpEditingControls();
        }

        #region Editing Controls Setup

        private void WireUpEditingControls()
        {
            // Setup simplified context menu for StructureTreeView
            SetupSimplifiedTreeViewContextMenu();

            // Keyboard shortcuts for StructureTreeView (keeping F2 for edit, Delete for delete)
            StructureTreeView.PreviewKeyDown += StructureTreeView_PreviewKeyDown;
        }

        private void ChangeConnection_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Show connection fields again
                ConnectionFieldsPanel.Visibility = Visibility.Visible;
                ConnectionSummaryPanel.Visibility = Visibility.Collapsed;
                ConnectionStatusPanel.Visibility = Visibility.Collapsed;

                // Reset buttons
                TestSqlConnectionButton.IsEnabled = true;
                GenerateSqlButton.IsEnabled = false;
                DeploySqlButton.IsEnabled = false;

                // Clear the current connection string
                if (_generationHandlers != null)
                {
                    _generationHandlers.ClearCurrentConnection();
                }

                // Clear the log
                SqlGenerationLog.Text = "Ready to configure new SQL connection...\n";

                UpdateStatus("SQL connection reset. Please configure new connection.", MessageSeverity.Info);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error resetting connection: {ex.Message}", MessageSeverity.Error);
            }
        }

        internal void ShowConnectionSuccess(string server, string database, bool isWindowsAuth)
        {
            try
            {
                // Update connection summary
                ConnectedServerText.Text = server;
                ConnectedDatabaseText.Text = database;
                ConnectedAuthText.Text = isWindowsAuth ? "Windows Authentication" : "SQL Server Authentication";

                // Show/hide appropriate panels
                ConnectionFieldsPanel.Visibility = Visibility.Collapsed;
                ConnectionSummaryPanel.Visibility = Visibility.Visible;
                ConnectionStatusPanel.Visibility = Visibility.Visible;

                // Enable deployment button immediately after successful connection
                DeploySqlButton.IsEnabled = true;

                // Keep generate button enabled based on analysis results
                GenerateSqlButton.IsEnabled = _allAnalysisResults != null && _allAnalysisResults.Any();
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error updating connection UI: {ex.Message}", MessageSeverity.Error);
            }
        }

        private void SetupSimplifiedTreeViewContextMenu()
        {
            // Create context menu if it doesn't exist
            if (StructureTreeView.ContextMenu == null)
            {
                StructureTreeView.ContextMenu = new ContextMenu();
            }

            var contextMenu = StructureTreeView.ContextMenu;
            contextMenu.Items.Clear();

            // Expand All
            var expandAllMenuItem = new MenuItem { Header = "Expand All" };
            expandAllMenuItem.Icon = new TextBlock { Text = "⊞", FontSize = 14 };
            expandAllMenuItem.Click += (s, e) => ExpandAllTreeItems(StructureTreeView.Items);
            contextMenu.Items.Add(expandAllMenuItem);

            // Collapse All
            var collapseAllMenuItem = new MenuItem { Header = "Collapse All" };
            collapseAllMenuItem.Icon = new TextBlock { Text = "⊟", FontSize = 14 };
            collapseAllMenuItem.Click += (s, e) => CollapseAllTreeItems(StructureTreeView.Items);
            contextMenu.Items.Add(collapseAllMenuItem);

            contextMenu.Items.Add(new Separator());

            // Copy entire tree as JSON
            var copyTreeJsonMenuItem = new MenuItem { Header = "Copy Tree as JSON" };
            copyTreeJsonMenuItem.Icon = new TextBlock { Text = "📋", FontSize = 14 };
            copyTreeJsonMenuItem.Click += (s, e) => CopyTreeAsJson();
            contextMenu.Items.Add(copyTreeJsonMenuItem);

            // Export tree structure
            var exportTreeMenuItem = new MenuItem { Header = "Export Tree Structure..." };
            exportTreeMenuItem.Icon = new TextBlock { Text = "📥", FontSize = 14 };
            exportTreeMenuItem.Click += (s, e) => ExportTreeStructure();
            contextMenu.Items.Add(exportTreeMenuItem);
        }




        private void TreeViewContextMenu_Opening(object sender, RoutedEventArgs e)
        {
            var selectedItem = StructureTreeView.SelectedItem as TreeViewItem;
            if (selectedItem == null) return;

            var contextMenu = StructureTreeView.ContextMenu;
            if (contextMenu == null) return;

            // Find menu items
            var editItem = contextMenu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header?.ToString() == "Edit");
            var deleteItem = contextMenu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header?.ToString() == "Delete");
            var convertItem = contextMenu.Items.OfType<MenuItem>().FirstOrDefault(m => (m.Header?.ToString() ?? "").Contains("Convert to Repeating"));
            var removeItem = contextMenu.Items.OfType<MenuItem>().FirstOrDefault(m => (m.Header?.ToString() ?? "").Contains("Remove from Repeating"));

            // Enable/disable based on what's selected
            bool isControl = selectedItem.Tag is ControlDefinition;
            bool isInRepeatingSection = (selectedItem.Tag as ControlDefinition)?.IsInRepeatingSection ?? false;

            if (editItem != null) editItem.IsEnabled = isControl;
            if (deleteItem != null) deleteItem.IsEnabled = isControl;
            if (convertItem != null) convertItem.IsEnabled = isControl;
            if (removeItem != null) removeItem.IsEnabled = isControl && isInRepeatingSection;
        }

        private void StructureTreeView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F2)
            {
                EditSelectedControl();
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                DeleteSelectedControl();
                e.Handled = true;
            }
            else if (e.Key == Key.A && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                var (view, section) = GetSelectedViewAndSection();
                if (view == null)
                {
                    UpdateStatus("Select a view (or a section within a view) first.", MessageSeverity.Warning);
                }
                else
                {
                    _analysisHandlers?.ShowAddControlDialog(view, section);
                }
                e.Handled = true;
            }
        }

        #endregion

        #region Selection Helpers (NEW)

        /// <summary>
        /// Returns the nearest ViewDefinition ancestor for the current selection,
        /// and the closest section name (string-tag) in the selection chain.
        /// </summary>
        private (ViewDefinition view, string parentSection) GetSelectedViewAndSection()
        {
            var selectedItem = StructureTreeView.SelectedItem as TreeViewItem;
            if (selectedItem == null) return (null, null);

            string section = null;
            TreeViewItem cursor = selectedItem;

            while (cursor != null)
            {
                if (section == null && cursor.Tag is string sec)
                    section = sec;

                if (cursor.Tag is ViewDefinition v)
                    return (v, section);

                cursor = ItemsControl.ItemsControlFromItemContainer(cursor) as TreeViewItem;
            }

            return (null, section);
        }


        #endregion

        #region Edit Control Methods

        private void EditSelectedControl()
        {
            var selectedItem = StructureTreeView.SelectedItem as TreeViewItem;
            if (selectedItem?.Tag is ControlDefinition control)
            {
                _analysisHandlers?.ShowEditPanel(control, selectedItem);
            }
        }

        private void CopyTreeAsJson()
        {
            try
            {
                if (_allFormDefinitions != null && _allFormDefinitions.Any())
                {
                    var json = JsonConvert.SerializeObject(_allFormDefinitions, Formatting.Indented);
                    Clipboard.SetText(json);
                    UpdateStatus("Tree structure copied to clipboard as JSON", MessageSeverity.Info);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Failed to copy tree: {ex.Message}", MessageSeverity.Error);
            }
        }

        private void SqlAuthRadio_Changed(object sender, RoutedEventArgs e)
        {
            // Check if the controls are initialized (to avoid null reference during startup)
            if (SqlAuthRadio == null || WindowsAuthRadio == null) return;

            if (SqlAuthRadio.IsChecked == true)
            {
                // Show SQL authentication fields
                if (SqlUsernameLabel != null) SqlUsernameLabel.Visibility = Visibility.Visible;
                if (SqlUsernameTextBox != null) SqlUsernameTextBox.Visibility = Visibility.Visible;
                if (SqlPasswordLabel != null) SqlPasswordLabel.Visibility = Visibility.Visible;
                if (SqlPasswordBox != null) SqlPasswordBox.Visibility = Visibility.Visible;
            }
            else
            {
                // Hide SQL authentication fields for Windows authentication
                if (SqlUsernameLabel != null) SqlUsernameLabel.Visibility = Visibility.Collapsed;
                if (SqlUsernameTextBox != null) SqlUsernameTextBox.Visibility = Visibility.Collapsed;
                if (SqlPasswordLabel != null) SqlPasswordLabel.Visibility = Visibility.Collapsed;
                if (SqlPasswordBox != null) SqlPasswordBox.Visibility = Visibility.Collapsed;
            }
        }

        private void ExportTreeStructure()
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Title = "Export Tree Structure",
                    Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                    FileName = $"TreeStructure_{DateTime.Now:yyyyMMdd_HHmmss}.json"
                };

                if (dialog.ShowDialog() == true)
                {
                    if (_allFormDefinitions != null && _allFormDefinitions.Any())
                    {
                        var json = JsonConvert.SerializeObject(_allFormDefinitions, Formatting.Indented);
                        File.WriteAllText(dialog.FileName, json);
                        UpdateStatus($"Tree structure exported to {dialog.FileName}", MessageSeverity.Info);
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Export failed: {ex.Message}", MessageSeverity.Error);
            }
        }


        private void DeleteSelectedControl()
        {
            var selectedItem = StructureTreeView.SelectedItem as TreeViewItem;
            if (selectedItem?.Tag is ControlDefinition control)
            {
                var result = MessageBox.Show(
                    $"Are you sure you want to delete '{control.Label ?? control.Name}'?",
                    "Confirm Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    _analysisHandlers?.DeleteControl(control);
                }
            }
        }



        private void ExpandAllTreeItems(ItemCollection items)
        {
            foreach (var obj in items)
            {
                if (obj is TreeViewItem item)
                {
                    item.IsExpanded = true;
                    ExpandAllTreeItems(item.Items);
                }
            }
        }

        private void CollapseAllTreeItems(ItemCollection items)
        {
            foreach (var obj in items)
            {
                if (obj is TreeViewItem item)
                {
                    item.IsExpanded = false;
                    CollapseAllTreeItems(item.Items);
                }
            }
        }

        private void SearchInTree()
        {
            var searchBox = FindName("TreeSearchBox") as TextBox;
            if (searchBox == null) return;

            var searchText = searchBox.Text?.ToLower();
            if (string.IsNullOrEmpty(searchText))
                return;

            // Reset all items to normal appearance
            ResetTreeItemsAppearance(StructureTreeView.Items);

            // Search and highlight
            SearchAndHighlight(StructureTreeView.Items, searchText);
        }

        private void ResetTreeItemsAppearance(ItemCollection items)
        {
            foreach (var obj in items)
            {
                if (obj is TreeViewItem item)
                {
                    item.Background = Brushes.Transparent;
                    ResetTreeItemsAppearance(item.Items);
                }
            }
        }

        private bool SearchAndHighlight(ItemCollection items, string searchText)
        {
            bool found = false;
            foreach (var obj in items)
            {
                if (obj is not TreeViewItem item) continue;

                bool itemFound = false;

                // Check if this item matches
                if (item.Tag is ControlDefinition control)
                {
                    if ((control.Name?.ToLower().Contains(searchText) == true) ||
                        (control.Label?.ToLower().Contains(searchText) == true) ||
                        (control.Type?.ToLower().Contains(searchText) == true))
                    {
                        itemFound = true;
                    }
                }
                else if (item.Tag is string sectionName)
                {
                    if (sectionName.ToLower().Contains(searchText))
                    {
                        itemFound = true;
                    }
                }

                // Check children
                bool childFound = SearchAndHighlight(item.Items, searchText);

                if (itemFound || childFound)
                {
                    if (itemFound)
                    {
                        item.Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 0)); // Highlight yellow
                    }
                    item.IsExpanded = true; // Expand to show found items
                    found = true;
                }
            }
            return found;
        }

        #endregion

        #region File Management

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select InfoPath Forms (or pre-extracted folders)",
                Filter = "InfoPath Forms (*.xsn)|*.xsn|All Files (*.*)|*.*",
                Multiselect = true,
                CheckFileExists = false,
                CheckPathExists = true
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
                    UpdateStatus($"Path already added: {Path.GetFileName(filePath)}");
                    continue;
                }

                // Check if this is a directory (pre-extracted folder)
                bool isDirectory = Directory.Exists(filePath);

                // For directories, check if they contain view files
                if (isDirectory)
                {
                    var viewFiles = Directory.GetFiles(filePath, "view*.xsl");
                    if (viewFiles.Length == 0)
                    {
                        UpdateStatus($"Folder does not contain view*.xsl files: {Path.GetFileName(filePath)}", MessageSeverity.Warning);
                        continue;
                    }
                }
                else
                {
                    // Check if file can be analyzed
                    var formType = GetSelectedFormType();
                    var analyzer = _analyzerFactory.GetAnalyzer(formType);

                    if (analyzer == null || !analyzer.CanAnalyze(filePath))
                    {
                        UpdateStatus($"Cannot analyze file: {Path.GetFileName(filePath)}", MessageSeverity.Warning);
                        continue;
                    }
                }

                var fileInfo = new FormFileInfo
                {
                    FilePath = filePath,
                    FileName = isDirectory ? Path.GetFileName(filePath) + " (folder)" : Path.GetFileName(filePath),
                    FileSize = isDirectory ? 0 : new FileInfo(filePath).Length,
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

        #endregion

        #region Analysis

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
                    await _analysisHandlers.DisplayCombinedAnalysisResults(_allAnalysisResults);
                    await _generationHandlers.GenerateCombinedSqlPreview(_allAnalysisResults);

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

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentAnalysis == null && !_allAnalysisResults.Any())
                return;

            var dialog = new SaveFileDialog
            {
                Title = "Export Analysis Results",
                Filter = "JSON Files (*.json)|*.json|SQL Scripts (*.sql)|*.sql|All Files (*.*)|*.*",
                FileName = $"FormAnalysis_{DateTime.Now:yyyyMMdd}"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    if (Path.GetExtension(dialog.FileName).Equals(".sql", StringComparison.OrdinalIgnoreCase))
                    {
                        // Export SQL
                        var sqlContent = SqlPreview.Text;
                        await NetFrameworkCompatibility.WriteAllTextAsync(dialog.FileName, sqlContent);
                    }
                    else
                    {
                        // Export JSON - export all analyzed forms
                        var json = JsonConvert.SerializeObject(_allFormDefinitions, Formatting.Indented);
                        await NetFrameworkCompatibility.WriteAllTextAsync(dialog.FileName, json);
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

        #endregion

        #region Generation Tab Event Handlers (Delegates)

        // SQL Generation
        private async void TestSqlConnection_Click(object sender, RoutedEventArgs e)
        {
            if (_generationHandlers != null)
                await _generationHandlers.TestSqlConnection();
        }

        private async void GenerateSql_Click(object sender, RoutedEventArgs e)
        {
            if (_generationHandlers != null)
                await _generationHandlers.GenerateSql();
        }

        private async void DeploySql_Click(object sender, RoutedEventArgs e)
        {
            if (_generationHandlers != null)
                await _generationHandlers.DeploySql();
        }

        // Nintex Generation
        private async void GenerateNintex_Click(object sender, RoutedEventArgs e)
        {
            if (_generationHandlers != null)
                await _generationHandlers.GenerateNintex();
        }

        private async void DownloadNintex_Click(object sender, RoutedEventArgs e)
        {
            if (_generationHandlers != null)
                await _generationHandlers.DownloadNintex();
        }

        // K2 Generation
        private async void TestK2Connection_Click(object sender, RoutedEventArgs e)
        {
            if (_generationHandlers != null)
                await _generationHandlers.TestK2Connection();
        }

        private void BrowseK2Folder_Click(object sender, RoutedEventArgs e)
        {
            _generationHandlers?.BrowseK2Folder();
        }

        private async void GenerateK2_Click(object sender, RoutedEventArgs e)
        {
            if (_generationHandlers != null)
                await _generationHandlers.GenerateK2();
        }

        private async void DeployK2_Click(object sender, RoutedEventArgs e)
        {
            if (_generationHandlers != null)
                await _generationHandlers.DeployK2();
        }

        private async void ExportK2Package_Click(object sender, RoutedEventArgs e)
        {
            if (_generationHandlers != null)
                await _generationHandlers.ExportK2Package();
        }

        // Reusable Views (K2 Tab)
        private async void RefreshReusableViews_Click(object sender, RoutedEventArgs e)
        {
            if (_analysisHandlers != null)
                await _analysisHandlers.RefreshReusableViews();
        }

        private async void SaveReusableViews_Click(object sender, RoutedEventArgs e)
        {
            if (_analysisHandlers != null)
                await _analysisHandlers.SaveReusableViews();
        }

        private void MinOccurrences_Changed(object sender, SelectionChangedEventArgs e)
        {
            _analysisHandlers?.MinOccurrences_Changed();
        }

        private void GroupBy_Changed(object sender, SelectionChangedEventArgs e)
        {
            _analysisHandlers?.GroupBy_Changed();
        }

        private void FilterType_Changed(object sender, SelectionChangedEventArgs e)
        {
            _analysisHandlers?.FilterType_Changed();
        }

        private async void CreateReusableView_Click(object sender, RoutedEventArgs e)
        {
            if (_analysisHandlers != null)
                await _analysisHandlers.CreateReusableView();
        }

        private async void ExportReusableControls_Click(object sender, RoutedEventArgs e)
        {
            if (_analysisHandlers != null)
                await _analysisHandlers.ExportReusableControls();
        }

        private void GroupNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _analysisHandlers?.GroupNameTextBox_TextChanged(sender, e);
        }

        #endregion

        #region JSON Context Menu Handlers

        private void JsonSelectAll_Click(object sender, RoutedEventArgs e)
        {
            JsonOutput.SelectAll();
            JsonOutput.Focus();
        }

        private void JsonCopy_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(JsonOutput.SelectedText))
            {
                Clipboard.SetText(JsonOutput.SelectedText);
                UpdateStatus("Selected JSON copied to clipboard", MessageSeverity.Info);
            }
            else
            {
                // If nothing is selected, copy all
                JsonOutput.SelectAll();
                Clipboard.SetText(JsonOutput.Text);
                UpdateStatus("All JSON copied to clipboard", MessageSeverity.Info);
            }
        }

        private void JsonCopyFormatted_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string jsonText = !string.IsNullOrEmpty(JsonOutput.SelectedText)
                    ? JsonOutput.SelectedText
                    : JsonOutput.Text;

                if (!string.IsNullOrEmpty(jsonText))
                {
                    // Parse and re-format with indentation
                    var parsed = Newtonsoft.Json.Linq.JToken.Parse(jsonText);
                    string formatted = parsed.ToString(Newtonsoft.Json.Formatting.Indented);
                    Clipboard.SetText(formatted);
                    UpdateStatus("Formatted JSON copied to clipboard", MessageSeverity.Info);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error formatting JSON: {ex.Message}", MessageSeverity.Error);
            }
        }

        private void JsonCopyMinified_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string jsonText = !string.IsNullOrEmpty(JsonOutput.SelectedText)
                    ? JsonOutput.SelectedText
                    : JsonOutput.Text;

                if (!string.IsNullOrEmpty(jsonText))
                {
                    // Parse and re-format without indentation (minified)
                    var parsed = Newtonsoft.Json.Linq.JToken.Parse(jsonText);
                    string minified = parsed.ToString(Newtonsoft.Json.Formatting.None);
                    Clipboard.SetText(minified);
                    UpdateStatus("Minified JSON copied to clipboard", MessageSeverity.Info);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error minifying JSON: {ex.Message}", MessageSeverity.Error);
            }
        }

        private async void JsonSaveToFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Save JSON Output",
                Filter = "JSON Files (*.json)|*.json|Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                FileName = $"FormAnalysis_{DateTime.Now:yyyyMMdd_HHmmss}.json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    string jsonText = !string.IsNullOrEmpty(JsonOutput.SelectedText)
                        ? JsonOutput.SelectedText
                        : JsonOutput.Text;

                    await NetFrameworkCompatibility.WriteAllTextAsync(dialog.FileName, jsonText);
                    UpdateStatus($"JSON saved to: {dialog.FileName}", MessageSeverity.Info);

                    MessageBox.Show($"JSON successfully saved to:\n{dialog.FileName}",
                                  "Save Successful",
                                  MessageBoxButton.OK,
                                  MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Save failed: {ex.Message}", MessageSeverity.Error);
                    MessageBox.Show($"Failed to save JSON:\n{ex.Message}",
                                  "Save Error",
                                  MessageBoxButton.OK,
                                  MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #region Helper Methods

        internal void UpdateStatus(string message, MessageSeverity severity = MessageSeverity.Info)
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

        private string GetSelectedFormType()
        {
            var selectedItem = FormTypeSelector.SelectedItem as ComboBoxItem;
            return selectedItem?.Tag?.ToString() ?? "InfoPath2013";
        }

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


        #region Form Structure Tab Event Handlers

        /// <summary>
        /// Handles Add Control button click
        /// </summary>
        private void AddControlButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Get the currently selected view
                var view = GetSelectedView();
                if (view != null)
                {
                    // Get selected section if any
                    string parentSection = null;
                    var selectedItem = StructureTreeView.SelectedItem as TreeViewItem;

                    if (selectedItem?.Tag is string sectionName)
                    {
                        parentSection = sectionName;
                    }
                    else if (selectedItem?.Tag is ViewDefinition)
                    {
                        // View is selected, no parent section
                        parentSection = null;
                    }

                    // Call the handler to show add control dialog
                    _analysisHandlers.ShowAddControlDialog(view, parentSection);
                }
                else
                {
                    MessageBox.Show("Please select a form view first.", "No View Selected",
                                  MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error adding control: {ex.Message}", MessageSeverity.Error);
            }
        }

        /// <summary>
        /// Handles Edit Control button click
        /// </summary>
        private void EditControlButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedItem = StructureTreeView.SelectedItem as TreeViewItem;
                if (selectedItem?.Tag is ControlDefinition control)
                {
                    // Call the ShowControlEditDialog directly, not ShowEditPanel
                    _analysisHandlers?.ShowControlEditDialog(control);
                }
                else
                {
                    MessageBox.Show("Please select a control to edit.",
                                  "No Control Selected",
                                  MessageBoxButton.OK,
                                  MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error editing control: {ex.Message}", MessageSeverity.Error);
            }
        }

        /// <summary>
        /// Handles Delete Control button click
        /// </summary>
        private void DeleteControlButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedItem = StructureTreeView.SelectedItem as TreeViewItem;
                if (selectedItem?.Tag is ControlDefinition control)
                {
                    var result = MessageBox.Show(
                        $"Are you sure you want to delete '{control.Label ?? control.Name}'?",
                        "Confirm Delete",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        _analysisHandlers?.DeleteControl(control);
                    }
                }
                else
                {
                    MessageBox.Show("Please select a control to delete.",
                                  "No Control Selected",
                                  MessageBoxButton.OK,
                                  MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error deleting control: {ex.Message}", MessageSeverity.Error);
            }
        }

        /// <summary>
        /// Handles Convert to Repeating button click
        /// </summary>
        private void ConvertToRepeatingButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedItem = StructureTreeView.SelectedItem as TreeViewItem;

                if (selectedItem?.Tag is ControlDefinition control)
                {
                    // Move control to repeating section
                    _analysisHandlers?.ShowMoveSectionDialog(control);
                }
                else if (selectedItem?.Tag is string sectionName)
                {
                    // Convert section to repeating
                    var view = GetSelectedView();
                    if (view != null)
                    {
                        var section = view.Sections.FirstOrDefault(s => s.Name == sectionName);
                        if (section != null && section.Type != "repeating")
                        {
                            var result = MessageBox.Show(
                                $"Convert '{section.Name}' to a repeating section?\n\n" +
                                "This will make all controls in this section repeatable.",
                                "Convert to Repeating Section",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Question);

                            if (result == MessageBoxResult.Yes)
                            {
                                section.Type = "repeating";
                                foreach (var ctrl in view.Controls.Where(c => c.ParentSection == section.Name))
                                {
                                    ctrl.IsInRepeatingSection = true;
                                    ctrl.RepeatingSectionName = section.Name;
                                    ctrl.SectionType = "repeating";
                                }

                                // Refresh the display
                                if (_allAnalysisResults != null)
                                {
                                    _analysisHandlers.DisplayCombinedAnalysisResults(_allAnalysisResults);
                                }

                                UpdateStatus($"Converted '{section.Name}' to repeating section", MessageSeverity.Info);
                            }
                        }
                        else if (section?.Type == "repeating")
                        {
                            MessageBox.Show("This section is already a repeating section.",
                                          "Already Repeating",
                                          MessageBoxButton.OK,
                                          MessageBoxImage.Information);
                        }
                    }
                }
                else
                {
                    MessageBox.Show("Please select a control or section to convert.",
                                  "No Selection",
                                  MessageBoxButton.OK,
                                  MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error converting section: {ex.Message}", MessageSeverity.Error);
            }
        }

        /// <summary>
        /// Handles Collapse All button click
        /// </summary>
        private void CollapseAllButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CollapseAllTreeItems(StructureTreeView.Items);
                UpdateStatus("Collapsed all tree items", MessageSeverity.Info);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error collapsing tree: {ex.Message}", MessageSeverity.Error);
            }
        }

        /// <summary>
        /// Handles Expand All button click
        /// </summary>
        private void ExpandAllButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ExpandAllTreeItems(StructureTreeView.Items);
                UpdateStatus("Expanded all tree items", MessageSeverity.Info);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error expanding tree: {ex.Message}", MessageSeverity.Error);
            }
        }


        /// <summary>
        /// Handles Tree Search button click
        /// </summary>
        private void TreeSearchButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SearchInTree();
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error searching tree: {ex.Message}", MessageSeverity.Error);
            }
        }

        /// <summary>
        /// Handles Tree Search text changed
        /// </summary>
        private void TreeSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                // Auto-search after 3 characters
                if (TreeSearchBox.Text?.Length >= 3)
                {
                    SearchInTree();
                }
                else if (string.IsNullOrWhiteSpace(TreeSearchBox.Text))
                {
                    // Clear highlighting when search is cleared
                    ResetTreeItemsAppearance(StructureTreeView.Items);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error in search: {ex.Message}", MessageSeverity.Error);
            }
        }


        /// <summary>
        /// Handles TreeView selection changed
        /// </summary>
        private void StructureTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            try
            {
                var selectedItem = e.NewValue as TreeViewItem;

                // Update status bar with selection info
                if (selectedItem?.Tag is ControlDefinition control)
                {
                    UpdateStatus($"Selected control: {control?.Label ?? control?.Name}", MessageSeverity.Info);
                }
                else if (selectedItem?.Tag is string)
                {
                    UpdateStatus($"Selected section", MessageSeverity.Info);
                }
                else if (selectedItem?.Tag is ViewDefinition view)
                {
                    UpdateStatus($"Selected view: {view?.ViewName}", MessageSeverity.Info);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error in selection: {ex.Message}", MessageSeverity.Error);
            }
        }
        #endregion

        #region Helper Methods for Form Structure Tab

        /// <summary>
        /// Gets the currently selected view from the tree
        /// </summary>
        private ViewDefinition GetSelectedView()
        {
            var selectedItem = StructureTreeView.SelectedItem as TreeViewItem;

            // Walk up the tree to find the view
            while (selectedItem != null)
            {
                if (selectedItem.Tag is ViewDefinition view)
                {
                    return view;
                }
                selectedItem = selectedItem.Parent as TreeViewItem;
            }

            // If no selection, try to find the first view
            if (_allFormDefinitions?.Values?.FirstOrDefault()?.Views?.FirstOrDefault() != null)
            {
                return _allFormDefinitions.Values.First().Views.First();
            }

            return null;
        }

        /// <summary>
        /// Collapses all tree view items recursively
        /// </summary>
        private void CollapseAllTreeViewItems(ItemCollection items)
        {
            foreach (TreeViewItem item in items)
            {
                item.IsExpanded = false;
                if (item.Items.Count > 0)
                {
                    CollapseAllTreeViewItems(item.Items);
                }
            }
        }

        /// <summary>
        /// Expands all tree view items recursively
        /// </summary>
        private void ExpandAllTreeViewItems(ItemCollection items)
        {
            foreach (TreeViewItem item in items)
            {
                item.IsExpanded = true;
                if (item.Items.Count > 0)
                {
                    ExpandAllTreeViewItems(item.Items);
                }
            }
        }

        /// <summary>
        /// Recursively searches tree view items
        /// </summary>
        private int SearchTreeViewItems(ItemCollection items, string searchText)
        {
            int matchCount = 0;

            foreach (TreeViewItem item in items)
            {
                bool isMatch = false;

                // Check if header contains search text
                if (item.Header != null)
                {
                    string headerText = "";

                    if (item.Header is string str)
                    {
                        headerText = str;
                    }
                    else if (item.Header is StackPanel panel)
                    {
                        // Extract text from StackPanel children
                        foreach (var child in panel.Children)
                        {
                            if (child is TextBlock textBlock)
                            {
                                headerText += textBlock.Text + " ";
                            }
                        }
                    }

                    if (headerText.ToLower().Contains(searchText))
                    {
                        isMatch = true;
                        matchCount++;
                    }
                }

                // Check tag for ControlDefinition
                if (item.Tag is ControlDefinition control)
                {
                    if ((control.Name?.ToLower().Contains(searchText) ?? false) ||
                        (control.Label?.ToLower().Contains(searchText) ?? false) ||
                        (control.Type?.ToLower().Contains(searchText) ?? false))
                    {
                        isMatch = true;
                        if (!isMatch) matchCount++;
                    }
                }

                // Highlight if match found
                if (isMatch)
                {
                    item.Background = new SolidColorBrush(Color.FromArgb(50, 0, 120, 212));
                    item.IsExpanded = true;

                    // Expand parent items
                    var parent = item.Parent as TreeViewItem;
                    while (parent != null)
                    {
                        parent.IsExpanded = true;
                        parent = parent.Parent as TreeViewItem;
                    }
                }

                // Search children
                if (item.Items.Count > 0)
                {
                    matchCount += SearchTreeViewItems(item.Items, searchText);
                }
            }

            return matchCount;
        }

        private void ClearSqlDeploymentInfo()
        {
            InfoPathFormDefinitionExtensions.CurrentSqlDeploymentInfo = null;
        }

        public async Task RefreshJsonOutputWithCurrentData()
        {
            if (_analysisHandlers != null && _allAnalysisResults != null && _allAnalysisResults.Any())
            {
                await _analysisHandlers.DisplayCombinedAnalysisResults(_allAnalysisResults);
            }
        }

        /// <summary>
        /// Resets tree view highlight
        /// </summary>
        private void ResetTreeViewHighlight(ItemCollection items)
        {
            foreach (TreeViewItem item in items)
            {
                item.Background = Brushes.Transparent;

                if (item.Items.Count > 0)
                {
                    ResetTreeViewHighlight(item.Items);
                }
            }
        }


        #endregion
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
