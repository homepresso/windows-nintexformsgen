using System;
using System.Collections.Generic;
using System.Diagnostics;
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
// using FormGenerator.Analyzers.InfoPath; // (Duplicate/typo namespace�remove if not needed)
using FormGenerator.Core.Interfaces;
using FormGenerator.Core.Models;
using FormGenerator.Services;
using FormGenerator.Analyzers.InfoPath;
using SourceCode.Forms.Authoring;
using SourceCode.Hosting.Client.BaseAPI;

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

        // Mappings from imported InfoPath forms (keyed by the _allFormDefinitions file-name key)
        // to existing K2 forms selected via the "Map Existing Forms" dialog.
        internal readonly Dictionary<string, K2FormMapping> _k2FormMappings =
            new Dictionary<string, K2FormMapping>(StringComparer.OrdinalIgnoreCase);

        internal sealed class K2FormMapping
        {
            public string InfoPathFormKey { get; set; }
            public string InfoPathFormDisplay { get; set; }
            public string K2FormName { get; set; }
            public string K2FormDisplayName { get; set; }
            public Guid K2FormGuid { get; set; }
        }

        // User-confirmed field mappings, keyed by the _allFormDefinitions file-name key.
        // Inner map: InfoPath control name → existing K2 control ID.
        internal readonly Dictionary<string, Dictionary<string, string>> _k2FieldMappings =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        // Per-repeating-section SmartObject choices, keyed by file key → section name → mapping.
        internal readonly Dictionary<string, Dictionary<string, K2SectionMapping>> _k2SectionMappings =
            new Dictionary<string, Dictionary<string, K2SectionMapping>>(StringComparer.OrdinalIgnoreCase);

        internal sealed class K2SectionMapping
        {
            public string SmoName { get; set; }                 // chosen child SmartObject (existing or to-create)
            public bool CreateIfMissing { get; set; }           // create a SmartBox child if it doesn't exist
            // InfoPath control name → child SmartObject column (internal/system name).
            public Dictionary<string, string> Fields { get; set; } =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        // ComboBox item wrapper for the mapping dialog (Form == null means "do not map").
        private sealed class K2FormChoice
        {
            public string Display { get; set; }
            public SourceCode.Forms.Management.FormInfo Form { get; set; }
            public override string ToString() => Display;
        }

        // Partial class handlers - initialized before InitializeComponent
        private MainWindowGenerationHandlers _generationHandlers;
        private MainWindowAnalysisHandlers _analysisHandlers;
        private MainWindowRulesMappingHandlers _rulesMappingHandlers;

        public MainWindow()
        {
            // Initialize collections and services first
            _uploadedFiles = new ObservableCollection<FormFileInfo>();
            _analyzerFactory = new AnalyzerFactory();
            _sqlGenerator = new SqlGeneratorService();

            // Initialize handlers BEFORE InitializeComponent (important!)
            _generationHandlers = new MainWindowGenerationHandlers(this);
            _analysisHandlers = new MainWindowAnalysisHandlers(this);
            _rulesMappingHandlers = new MainWindowRulesMappingHandlers(this);

            // Now initialize the UI components
            InitializeComponent();

            // Set up data bindings and UI elements
            FileListBox.ItemsSource = _uploadedFiles;

            // Set version
            VersionText.Text = $"v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}";

            // Set default K2 password
            K2PasswordBox.Password = "K2pass!!K2pass!!";

            // Wire up SQL Auth radio button events
            SqlAuthRadio.Checked += SqlAuth_Changed;
            WindowsAuthRadio.Checked += SqlAuth_Changed;

            // Wire up new toolbar buttons for editing features
            WireUpEditingControls();
        }


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
            expandAllMenuItem.Icon = new TextBlock { Text = "?", FontSize = 14 };
            expandAllMenuItem.Click += (s, e) => ExpandAllTreeItems(StructureTreeView.Items);
            contextMenu.Items.Add(expandAllMenuItem);

            // Collapse All
            var collapseAllMenuItem = new MenuItem { Header = "Collapse All" };
            collapseAllMenuItem.Icon = new TextBlock { Text = "?", FontSize = 14 };
            collapseAllMenuItem.Click += (s, e) => CollapseAllTreeItems(StructureTreeView.Items);
            contextMenu.Items.Add(collapseAllMenuItem);

            contextMenu.Items.Add(new Separator());

            // Copy entire tree as JSON
            var copyTreeJsonMenuItem = new MenuItem { Header = "Copy Tree as JSON" };
            copyTreeJsonMenuItem.Icon = new TextBlock { Text = "??", FontSize = 14 };
            copyTreeJsonMenuItem.Click += (s, e) => CopyTreeAsJson();
            contextMenu.Items.Add(copyTreeJsonMenuItem);

            // Export tree structure
            var exportTreeMenuItem = new MenuItem { Header = "Export Tree Structure..." };
            exportTreeMenuItem.Icon = new TextBlock { Text = "??", FontSize = 14 };
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



        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select InfoPath Forms (or pre-extracted folders)",
                Filter = "InfoPath Forms (*.xsn;*.cab)|*.xsn;*.cab|XSN Files (*.xsn)|*.xsn|CAB Files (*.cab)|*.cab|All Files (*.*)|*.*",
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



        private async void AnalyzeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_uploadedFiles.Count == 0)
                return;

            AnalyzeButton.IsEnabled = false;
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

                    // Populate rules mapping tab
                    _rulesMappingHandlers.PopulateRulesMappings(_allFormDefinitions);

                    UpdateStatus($"Analysis completed for {_allAnalysisResults.Count} form(s)", MessageSeverity.Info);

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

        private async void BrowseK2Folder_Click(object sender, RoutedEventArgs e)
        {
            if (_generationHandlers != null)
                await _generationHandlers.BrowseK2Folder();
        }

        private async void GenerateSPCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_generationHandlers != null)
                await _generationHandlers.GenerateSharePointCsv();
        }

        private async void GenerateSPPowerShell_Click(object sender, RoutedEventArgs e)
        {
            if (_generationHandlers != null)
                await _generationHandlers.GenerateSharePointPowerShell();
        }

        private async void GenerateK2_Click(object sender, RoutedEventArgs e)
        {
            if (_generationHandlers != null)
                await _generationHandlers.GenerateK2();
        }

        private void UseExistingK2FormsCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            ExistingK2FormMappingSection.Visibility = Visibility.Visible;
        }

        private void UseExistingK2FormsCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            ExistingK2FormMappingSection.Visibility = Visibility.Collapsed;
        }

        private async void MapExistingK2Forms_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_allFormDefinitions == null || _allFormDefinitions.Count == 0)
                {
                    MessageBox.Show("Import and analyze at least one InfoPath form before mapping it to an existing K2 form.",
                                    "No Imported Forms",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Information);
                    return;
                }

                MapExistingK2FormsButton.IsEnabled = false;
                K2GenerationLog.Text += "Loading existing K2 forms from server...\n";
                UpdateStatus("Loading existing K2 forms...", MessageSeverity.Info);

                // Read UI values on the UI thread; the K2 call runs on a background thread.
                var serverName = (K2ServerTextBox.Text ?? string.Empty).Trim()
                    .Replace("https://", "").Replace("http://", "");
                if (string.IsNullOrEmpty(serverName))
                {
                    serverName = "localhost";
                }

                uint port = 5555;
                if (!string.IsNullOrWhiteSpace(K2PortTextBox.Text))
                {
                    uint.TryParse(K2PortTextBox.Text.Trim(), out port);
                }

                var forms = await Task.Run(() => LoadExistingK2Forms(serverName, port));

                K2GenerationLog.Text += $"Found {forms.Count} K2 form(s) on the server.\n";

                if (forms.Count == 0)
                {
                    MessageBox.Show("No K2 forms were found on the server. Please verify your connection settings and try again.",
                                    "No Forms Found",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Information);
                    return;
                }

                ShowExistingK2FormsMappingDialog(forms);
            }
            catch (Exception ex)
            {
                K2GenerationLog.Text += $"❌ Failed to load K2 forms: {ex.Message}\n";
                if (ex.InnerException != null)
                {
                    K2GenerationLog.Text += $"   Details: {ex.InnerException.Message}\n";
                }
                UpdateStatus("Failed to load existing K2 forms", MessageSeverity.Error);
                MessageBox.Show($"Failed to load existing K2 forms:\n{ex.Message}",
                                "Connection Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
            }
            finally
            {
                MapExistingK2FormsButton.IsEnabled = true;
            }
        }

        // ── Field-level mapping (InfoPath control → existing K2 control) ──

        private sealed class K2FieldChoice
        {
            public string Display { get; set; }
            public K2SmartObjectGenerator.K2FieldDescriptor Field { get; set; } // null = do not map
            public override string ToString() => Display;
        }

        private async void MapK2Fields_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_k2FormMappings == null || _k2FormMappings.Count == 0)
                {
                    MessageBox.Show("Map your InfoPath form(s) to existing K2 forms first using \"Map Existing Forms\".",
                                    "No Form Mapping", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var serverName = (K2ServerTextBox.Text ?? string.Empty).Trim()
                    .Replace("https://", "").Replace("http://", "");
                if (string.IsNullOrEmpty(serverName)) serverName = "localhost";
                uint port = 5555;
                if (!string.IsNullOrWhiteSpace(K2PortTextBox.Text)) uint.TryParse(K2PortTextBox.Text.Trim(), out port);

                MapK2FieldsButton.IsEnabled = false;

                foreach (var kv in _k2FormMappings.ToList())
                {
                    string fileKey = kv.Key;
                    var mapping = kv.Value;
                    if (!_allFormDefinitions.TryGetValue(fileKey, out var formDef) || formDef == null) continue;

                    K2GenerationLog.Text += $"Loading K2 fields for '{mapping.K2FormDisplayName}'...\n";
                    var guid = mapping.K2FormGuid;
                    var k2Fields = await Task.Run(() => K2SmartObjectGenerator.ExistingK2FormUpdater.ReadFormControls(serverName, port, guid));
                    K2GenerationLog.Text += $"  Found {k2Fields.Count} K2 field control(s).\n";

                    if (k2Fields.Count == 0)
                    {
                        MessageBox.Show($"No data controls were found on K2 form '{mapping.K2FormDisplayName}'.",
                                        "No Fields", MessageBoxButton.OK, MessageBoxImage.Information);
                        continue;
                    }

                    ShowK2FieldMappingDialog(fileKey, formDef, mapping, k2Fields);
                }
            }
            catch (Exception ex)
            {
                K2GenerationLog.Text += $"❌ Field mapping failed: {ex.Message}\n";
                MessageBox.Show($"Field mapping failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                MapK2FieldsButton.IsEnabled = true;
            }
        }

        private void ShowK2FieldMappingDialog(string fileKey, InfoPathFormDefinition formDef,
            K2FormMapping mapping, List<K2SmartObjectGenerator.K2FieldDescriptor> k2Fields)
        {
            // Data-bound InfoPath controls (skip pure layout/structure controls).
            var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Label", "RepeatingTable", "Section", "OptionalSection", "Button", "Image" };
            var ipControls = (formDef.Views ?? new List<ViewDefinition>())
                .Where(v => v?.Controls != null)
                .SelectMany(v => v.Controls)
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Name) && !skip.Contains(c.Type ?? ""))
                .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            if (ipControls.Count == 0)
            {
                MessageBox.Show("No data-bound InfoPath controls were found to map.",
                                "Nothing to map", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var choices = new List<K2FieldChoice> { new K2FieldChoice { Display = "— Do not map —", Field = null } };
            choices.AddRange(k2Fields.Select(f => new K2FieldChoice { Display = f.Display, Field = f }));

            _k2FieldMappings.TryGetValue(fileKey, out var existing);

            var rowPanel = new StackPanel { Margin = new Thickness(12, 4, 12, 4) };
            var rows = new List<(string IpName, ComboBox Combo)>();

            var comboItemStyle = new Style(typeof(ComboBoxItem));
            comboItemStyle.Setters.Add(new Setter(System.Windows.Controls.Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
            comboItemStyle.Setters.Add(new Setter(System.Windows.Controls.Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));

            foreach (var ip in ipControls)
            {
                string ipDisplay = !string.IsNullOrWhiteSpace(ip.Label) ? $"{ip.Label}  ({ip.Name})" : ip.Name;

                var rowGrid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });

                var label = new TextBlock { Text = ipDisplay, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), TextTrimming = TextTrimming.CharacterEllipsis };
                Grid.SetColumn(label, 0);

                // Searchable, per-row filtered combo (the K2 field list can be large with child SmartObjects).
                var view = new System.Windows.Data.ListCollectionView(choices);
                string filterText = null;
                view.Filter = item =>
                {
                    if (!(item is K2FieldChoice c) || string.IsNullOrEmpty(c.Display)) return false;
                    if (string.IsNullOrWhiteSpace(filterText)) return true;
                    if (choices.Any(x => string.Equals(x.Display, filterText, StringComparison.Ordinal))) return true;
                    return c.Display.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0;
                };
                var combo = new ComboBox
                {
                    ItemsSource = view,
                    IsEditable = true,
                    IsTextSearchEnabled = false,
                    StaysOpenOnEdit = true,
                    IsSynchronizedWithCurrentItem = false,
                    SelectedIndex = 0,
                    VerticalAlignment = VerticalAlignment.Center,
                    MaxDropDownHeight = 300,
                    ItemContainerStyle = comboItemStyle
                };
                combo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                    new TextChangedEventHandler((s, e) =>
                    {
                        filterText = (e.OriginalSource as TextBox)?.Text ?? combo.Text;
                        view.Refresh();
                        if (combo.IsKeyboardFocusWithin && !string.IsNullOrEmpty(filterText)) combo.IsDropDownOpen = true;
                    }));

                // Restore an existing mapping; otherwise auto-match by normalized name.
                if (existing != null && existing.TryGetValue(ip.Name, out var savedId) && !string.IsNullOrEmpty(savedId))
                {
                    var m = choices.FirstOrDefault(c => c.Field != null && string.Equals(c.Field.ControlId, savedId, StringComparison.OrdinalIgnoreCase));
                    if (m != null) combo.SelectedItem = m;
                }
                else
                {
                    var auto = AutoMatchField(ip, choices);
                    if (auto != null) combo.SelectedItem = auto;
                }

                Grid.SetColumn(combo, 1);
                rowGrid.Children.Add(label);
                rowGrid.Children.Add(combo);
                rowPanel.Children.Add(rowGrid);
                rows.Add((ip.Name, combo));
            }

            var header = new TextBlock
            {
                Text = $"Map each InfoPath control (left) to a control on K2 form '{mapping.K2FormDisplayName}' (right). {k2Fields.Count} K2 field(s).",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(12, 12, 12, 6)
            };

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = rowPanel };
            var okButton = new Button { Content = "Save Mapping", Width = 120, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var cancelButton = new Button { Content = "Cancel", Width = 90, IsCancel = true };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12) };
            buttons.Children.Add(okButton);
            buttons.Children.Add(cancelButton);

            var layout = new DockPanel();
            DockPanel.SetDock(header, Dock.Top);
            DockPanel.SetDock(buttons, Dock.Bottom);
            layout.Children.Add(header);
            layout.Children.Add(buttons);
            layout.Children.Add(scroll);

            var dialog = new Window
            {
                Title = $"Map Fields → {mapping.K2FormDisplayName}",
                Width = 760,
                Height = 560,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Content = layout
            };
            okButton.Click += (s, e) => { dialog.DialogResult = true; };

            if (dialog.ShowDialog() != true) return;

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int mapped = 0;
            foreach (var (ipName, combo) in rows)
            {
                if (combo.SelectedItem is K2FieldChoice choice && choice.Field != null)
                {
                    map[ipName] = choice.Field.ControlId;
                    mapped++;
                }
            }
            _k2FieldMappings[fileKey] = map;

            SelectedK2FormTextBlock.Text = $"{mapping.K2FormDisplayName}: {mapped} of {rows.Count} field(s) mapped.";
            K2GenerationLog.Text += $"Saved {mapped} field mapping(s) for '{mapping.K2FormDisplayName}'.\n";
            UpdateStatus($"Saved {mapped} K2 field mapping(s)", MessageSeverity.Info);
        }

        // ── Per-repeating-section SmartObject mapping (pick SmartObject, then field-map) ──

        private sealed class SmoChoice
        {
            public string Display { get; set; }
            public string Name { get; set; }      // SmartObject name; null for "create new" / "do not map"
            public bool CreateNew { get; set; }
            public override string ToString() => Display;
        }

        private List<(string Section, List<ControlDefinition> Controls)> GetRepeatingSectionsForMapping(InfoPathFormDefinition formDef)
        {
            var dict = new Dictionary<string, List<ControlDefinition>>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in formDef.Views ?? new List<ViewDefinition>())
                foreach (var c in v.Controls ?? new List<ControlDefinition>())
                {
                    if (c == null || string.IsNullOrWhiteSpace(c.Name)) continue;
                    string sec = c.RepeatingSectionName;
                    if (string.IsNullOrWhiteSpace(sec) || !c.IsInRepeatingSection) continue;
                    if (string.Equals(c.Type, "RepeatingTable", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(c.Type, "Label", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!dict.TryGetValue(sec, out var list)) { list = new List<ControlDefinition>(); dict[sec] = list; }
                    list.Add(c);
                }
            return dict.Select(kv => (kv.Key, kv.Value)).ToList();
        }

        private async void MapK2Sections_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_k2FormMappings == null || _k2FormMappings.Count == 0)
                {
                    MessageBox.Show("Map your InfoPath form(s) to existing K2 forms first using \"Map Existing Forms\".",
                                    "No Form Mapping", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var serverName = (K2ServerTextBox.Text ?? string.Empty).Trim()
                    .Replace("https://", "").Replace("http://", "");
                if (string.IsNullOrEmpty(serverName)) serverName = "localhost";
                uint port = 5555;
                if (!string.IsNullOrWhiteSpace(K2PortTextBox.Text)) uint.TryParse(K2PortTextBox.Text.Trim(), out port);

                MapK2SectionsButton.IsEnabled = false;
                K2GenerationLog.Text += "Loading K2 SmartObjects for section mapping...\n";
                var allSmoNames = await Task.Run(() => K2SmartObjectGenerator.ExistingK2FormUpdater.LoadAllSmartObjectNames(serverName, port));
                K2GenerationLog.Text += $"  Found {allSmoNames.Count} SmartObject(s).\n";

                foreach (var kv in _k2FormMappings.ToList())
                {
                    string fileKey = kv.Key;
                    var mapping = kv.Value;
                    if (!_allFormDefinitions.TryGetValue(fileKey, out var formDef) || formDef == null) continue;

                    var sections = GetRepeatingSectionsForMapping(formDef);
                    if (sections.Count == 0)
                    {
                        K2GenerationLog.Text += $"  '{mapping.InfoPathFormDisplay}': no repeating sections.\n";
                        continue;
                    }

                    var picks = ShowSectionSmoPickerDialog(fileKey, mapping, sections, allSmoNames);
                    if (picks == null) continue; // cancelled

                    if (!_k2SectionMappings.TryGetValue(fileKey, out var secMap))
                    {
                        secMap = new Dictionary<string, K2SectionMapping>(StringComparer.OrdinalIgnoreCase);
                        _k2SectionMappings[fileKey] = secMap;
                    }

                    int mappedSections = 0;
                    foreach (var sec in sections)
                    {
                        if (!picks.TryGetValue(sec.Section, out var pick) || pick == null) continue;
                        if (!pick.CreateNew && string.IsNullOrEmpty(pick.Name)) continue; // "do not map"

                        var sm = new K2SectionMapping { SmoName = pick.Name, CreateIfMissing = pick.CreateNew };

                        if (pick.CreateNew)
                        {
                            foreach (var c in sec.Controls)
                                if (!string.IsNullOrWhiteSpace(c.Name)) sm.Fields[c.Name] = c.Name;
                        }
                        else
                        {
                            var cols = await Task.Run(() => K2SmartObjectGenerator.ExistingK2FormUpdater.LoadSmartObjectColumns(serverName, port, pick.Name));
                            var fieldMap = ShowSectionFieldMappingDialog(sec.Section, pick.Name, sec.Controls, cols);
                            if (fieldMap == null) continue; // cancelled this section
                            sm.Fields = fieldMap;
                        }

                        secMap[sec.Section] = sm;
                        mappedSections++;
                    }

                    K2GenerationLog.Text += $"  '{mapping.InfoPathFormDisplay}': mapped {mappedSections} of {sections.Count} section(s).\n";
                }

                UpdateStatus("Saved repeating-section SmartObject mappings", MessageSeverity.Info);
            }
            catch (Exception ex)
            {
                K2GenerationLog.Text += $"❌ Section mapping failed: {ex.Message}\n";
                MessageBox.Show($"Section mapping failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                MapK2SectionsButton.IsEnabled = true;
            }
        }

        // Stage 1: choose a SmartObject (existing or create-new) for each repeating section.
        private Dictionary<string, SmoChoice> ShowSectionSmoPickerDialog(string fileKey, K2FormMapping mapping,
            List<(string Section, List<ControlDefinition> Controls)> sections, List<string> allSmoNames)
        {
            var baseChoices = new List<SmoChoice>
            {
                new SmoChoice { Display = "➕ Create new SmartObject", Name = null, CreateNew = true },
                new SmoChoice { Display = "— Do not map —", Name = null, CreateNew = false }
            };
            baseChoices.AddRange(allSmoNames.Select(n => new SmoChoice { Display = n, Name = n, CreateNew = false }));

            _k2SectionMappings.TryGetValue(fileKey, out var existingSecMap);

            var comboItemStyle = new Style(typeof(ComboBoxItem));
            comboItemStyle.Setters.Add(new Setter(System.Windows.Controls.Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));

            var rowPanel = new StackPanel { Margin = new Thickness(12, 4, 12, 4) };
            var rows = new List<(string Section, ComboBox Combo)>();

            foreach (var sec in sections)
            {
                var rowGrid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });

                var label = new TextBlock { Text = $"{sec.Section}  ({sec.Controls.Count} field(s))", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), TextTrimming = TextTrimming.CharacterEllipsis };
                Grid.SetColumn(label, 0);

                var view = new System.Windows.Data.ListCollectionView(baseChoices);
                string filterText = null;
                view.Filter = item =>
                {
                    if (!(item is SmoChoice c) || string.IsNullOrEmpty(c.Display)) return false;
                    if (string.IsNullOrWhiteSpace(filterText)) return true;
                    if (baseChoices.Any(x => string.Equals(x.Display, filterText, StringComparison.Ordinal))) return true;
                    return c.Display.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0;
                };
                var combo = new ComboBox
                {
                    ItemsSource = view,
                    IsEditable = true,
                    IsTextSearchEnabled = false,
                    StaysOpenOnEdit = true,
                    IsSynchronizedWithCurrentItem = false,
                    VerticalAlignment = VerticalAlignment.Center,
                    MaxDropDownHeight = 320,
                    ItemContainerStyle = comboItemStyle
                };
                combo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                    new TextChangedEventHandler((s, ev) =>
                    {
                        filterText = (ev.OriginalSource as TextBox)?.Text ?? combo.Text;
                        view.Refresh();
                        if (combo.IsKeyboardFocusWithin && !string.IsNullOrEmpty(filterText)) combo.IsDropDownOpen = true;
                    }));

                // Default: restore prior choice, else best-match an existing SmartObject by section name, else create-new.
                SmoChoice preset = null;
                if (existingSecMap != null && existingSecMap.TryGetValue(sec.Section, out var prior) && prior != null)
                {
                    preset = prior.CreateIfMissing
                        ? baseChoices.First(c => c.CreateNew)
                        : baseChoices.FirstOrDefault(c => string.Equals(c.Name, prior.SmoName, StringComparison.OrdinalIgnoreCase));
                }
                if (preset == null)
                {
                    string secNorm = FieldNormalize(sec.Section);
                    preset = baseChoices.FirstOrDefault(c => !c.CreateNew && c.Name != null
                                 && FieldNormalize(c.Name).Contains(secNorm)
                                 && !FieldNormalize(c.Name).Contains("ATTACHMENT"))
                             ?? baseChoices.First(c => c.CreateNew);
                }
                combo.SelectedItem = preset;

                Grid.SetColumn(combo, 1);
                rowGrid.Children.Add(label);
                rowGrid.Children.Add(combo);
                rowPanel.Children.Add(rowGrid);
                rows.Add((sec.Section, combo));
            }

            var header = new TextBlock
            {
                Text = $"Choose the K2 SmartObject for each repeating section on '{mapping.InfoPathFormDisplay}'. Pick an existing SmartObject to bind to, or “Create new” to generate one. {allSmoNames.Count} SmartObject(s).",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(12, 12, 12, 6)
            };
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = rowPanel };
            var okButton = new Button { Content = "Next", Width = 120, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var cancelButton = new Button { Content = "Cancel", Width = 90, IsCancel = true };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12) };
            buttons.Children.Add(okButton);
            buttons.Children.Add(cancelButton);
            var dock = new DockPanel();
            DockPanel.SetDock(header, Dock.Top);
            DockPanel.SetDock(buttons, Dock.Bottom);
            dock.Children.Add(header);
            dock.Children.Add(buttons);
            dock.Children.Add(scroll);

            var dialog = new Window
            {
                Title = $"Map Sections → {mapping.K2FormDisplayName}",
                Width = 760,
                Height = 480,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Content = dock
            };
            okButton.Click += (s, ev) => { dialog.DialogResult = true; };
            if (dialog.ShowDialog() != true) return null;

            var result = new Dictionary<string, SmoChoice>(StringComparer.OrdinalIgnoreCase);
            foreach (var (section, combo) in rows)
            {
                var choice = combo.SelectedItem as SmoChoice;
                if (choice == null && combo.Text != null)
                    choice = baseChoices.FirstOrDefault(c => string.Equals(c.Display, combo.Text, StringComparison.OrdinalIgnoreCase));
                result[section] = choice ?? baseChoices.First(c => c.CreateNew);
            }
            return result;
        }

        // Stage 2: map a section's InfoPath controls to the chosen SmartObject's real columns.
        private Dictionary<string, string> ShowSectionFieldMappingDialog(string section, string smoName,
            List<ControlDefinition> controls, List<K2SmartObjectGenerator.K2FieldDescriptor> columns)
        {
            var choices = new List<K2FieldChoice> { new K2FieldChoice { Display = "— Do not map —", Field = null } };
            choices.AddRange(columns.Select(f => new K2FieldChoice { Display = f.Display, Field = f }));

            var comboItemStyle = new Style(typeof(ComboBoxItem));
            comboItemStyle.Setters.Add(new Setter(System.Windows.Controls.Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));

            var rowPanel = new StackPanel { Margin = new Thickness(12, 4, 12, 4) };
            var rows = new List<(string IpName, ComboBox Combo)>();

            foreach (var ip in controls)
            {
                string ipDisplay = !string.IsNullOrWhiteSpace(ip.Label) ? $"{ip.Label}  ({ip.Name})" : ip.Name;
                var rowGrid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
                var label = new TextBlock { Text = ipDisplay, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), TextTrimming = TextTrimming.CharacterEllipsis };
                Grid.SetColumn(label, 0);

                var view = new System.Windows.Data.ListCollectionView(choices);
                string filterText = null;
                view.Filter = item =>
                {
                    if (!(item is K2FieldChoice c) || string.IsNullOrEmpty(c.Display)) return false;
                    if (string.IsNullOrWhiteSpace(filterText)) return true;
                    if (choices.Any(x => string.Equals(x.Display, filterText, StringComparison.Ordinal))) return true;
                    return c.Display.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0;
                };
                var combo = new ComboBox
                {
                    ItemsSource = view, IsEditable = true, IsTextSearchEnabled = false, StaysOpenOnEdit = true,
                    IsSynchronizedWithCurrentItem = false, VerticalAlignment = VerticalAlignment.Center,
                    MaxDropDownHeight = 300, ItemContainerStyle = comboItemStyle
                };
                combo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                    new TextChangedEventHandler((s, ev) =>
                    {
                        filterText = (ev.OriginalSource as TextBox)?.Text ?? combo.Text;
                        view.Refresh();
                        if (combo.IsKeyboardFocusWithin && !string.IsNullOrEmpty(filterText)) combo.IsDropDownOpen = true;
                    }));

                var auto = AutoMatchField(ip, choices);
                if (auto != null) combo.SelectedItem = auto;

                Grid.SetColumn(combo, 1);
                rowGrid.Children.Add(label);
                rowGrid.Children.Add(combo);
                rowPanel.Children.Add(rowGrid);
                rows.Add((ip.Name, combo));
            }

            var header = new TextBlock
            {
                Text = $"Map section '{section}' fields (left) to columns of SmartObject '{smoName}' (right). {columns.Count} column(s).",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 12, 12, 6)
            };
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = rowPanel };
            var okButton = new Button { Content = "Save", Width = 120, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var cancelButton = new Button { Content = "Skip", Width = 90, IsCancel = true };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12) };
            buttons.Children.Add(okButton);
            buttons.Children.Add(cancelButton);
            var dock = new DockPanel();
            DockPanel.SetDock(header, Dock.Top);
            DockPanel.SetDock(buttons, Dock.Bottom);
            dock.Children.Add(header);
            dock.Children.Add(buttons);
            dock.Children.Add(scroll);

            var dialog = new Window
            {
                Title = $"Map '{section}' → {smoName}",
                Width = 760, Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, Content = dock
            };
            okButton.Click += (s, ev) => { dialog.DialogResult = true; };
            if (dialog.ShowDialog() != true) return null;

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (ipName, combo) in rows)
                if (combo.SelectedItem is K2FieldChoice choice && choice.Field != null)
                    map[ipName] = choice.Field.FieldName; // exact internal column name
            return map;
        }

        private static K2FieldChoice AutoMatchField(ControlDefinition ip, List<K2FieldChoice> choices)
        {
            string ipName = FieldNormalize(ip.Name);
            string ipLabel = FieldNormalize(ip.Label);
            string ipLeaf = FieldNormalize(BindingLeafName(ip.Binding));

            // Short token = label after the section prefix ("Discussion Item: Title" → "Title").
            string shortLabel = ip.Label;
            if (!string.IsNullOrEmpty(shortLabel) && shortLabel.Contains(":"))
                shortLabel = shortLabel.Substring(shortLabel.LastIndexOf(':') + 1);
            string ipShort = FieldNormalize(shortLabel);

            // Section hint = RepeatingSectionName, else the label prefix before ":".
            string sectionHint = FieldNormalize(ip.RepeatingSectionName);
            if (string.IsNullOrEmpty(sectionHint) && !string.IsNullOrEmpty(ip.Label) && ip.Label.Contains(":"))
                sectionHint = FieldNormalize(ip.Label.Substring(0, ip.Label.IndexOf(':')));

            K2FieldChoice best = null;
            int bestScore = 0;
            foreach (var c in choices)
            {
                if (c.Field == null) continue;

                string colDisp = FieldNormalize(c.Field.FieldDisplayName);
                string colName = FieldNormalize(c.Field.FieldName);
                string colCtrl = FieldStripSuffix(FieldNormalize(c.Field.ControlName));
                string smo = FieldNormalize(c.Field.ViewName);
                bool sectionRelated = !string.IsNullOrEmpty(sectionHint) && !string.IsNullOrEmpty(smo)
                    && (smo.Contains(sectionHint) || sectionHint.Contains(smo));

                int score = 0;
                foreach (var col in new[] { colDisp, colName, colCtrl })
                {
                    if (string.IsNullOrEmpty(col) || col.Length < 2) continue;
                    if (col == ipName || col == ipLabel || col == ipLeaf || col == ipShort)
                        score = Math.Max(score, 100);
                    else if (col.Length >= 3 && (ipName.EndsWith(col, StringComparison.Ordinal)
                             || ipLabel.EndsWith(col, StringComparison.Ordinal)
                             || ipLeaf.EndsWith(col, StringComparison.Ordinal)))
                        score = Math.Max(score, 55);
                    else if (ipShort.Length >= 3 && col.EndsWith(ipShort, StringComparison.Ordinal))
                        score = Math.Max(score, 50);
                }
                if (score > 0 && sectionRelated) score += 30;

                // Penalise sub-lists (e.g. "…_Attachments") so a non-attachment field maps to the
                // real child list rather than its attachments sub-SmartObject.
                bool smoIsAttachment = smo.Contains("ATTACHMENT");
                bool ipIsAttachment = (ip.Type ?? string.Empty).IndexOf("attach", StringComparison.OrdinalIgnoreCase) >= 0
                    || (ip.Label ?? string.Empty).IndexOf("attach", StringComparison.OrdinalIgnoreCase) >= 0;
                if (score > 0 && smoIsAttachment && !ipIsAttachment) score -= 40;

                if (score > bestScore) { bestScore = score; best = c; }
            }

            return bestScore >= 50 ? best : null;
        }

        private static string BindingLeafName(string binding)
        {
            if (string.IsNullOrEmpty(binding)) return null;
            var parts = binding.Split(new[] { '/', '\\', ':' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts[parts.Length - 1] : binding;
        }

        private static string FieldNormalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return System.Text.RegularExpressions.Regex.Replace(s, "[^A-Za-z0-9]", "").ToUpperInvariant();
        }

        private static readonly string[] _fieldSuffixes =
        { "TEXTBOX", "DROPDOWN", "DROPDOWNLIST", "COMBOBOX", "CALENDAR", "DATEPICKER", "PICKER", "CHECKBOX", "TEXTAREA", "RADIOBUTTON", "LISTBOX", "LISTVIEW", "HYPERLINK", "LABEL" };

        private static string FieldStripSuffix(string normalized)
        {
            if (string.IsNullOrEmpty(normalized)) return normalized;
            foreach (var suffix in _fieldSuffixes)
                if (normalized.Length > suffix.Length && normalized.EndsWith(suffix, StringComparison.Ordinal))
                    return normalized.Substring(0, normalized.Length - suffix.Length);
            return normalized;
        }

        /// <summary>
        /// Lists every form on the K2 server using the Forms management API
        /// (SourceCode.Forms.Management.FormsManager.GetForms()).
        /// </summary>
        private List<SourceCode.Forms.Management.FormInfo> LoadExistingK2Forms(string serverName, uint port)
        {
            using var formsManager = new SourceCode.Forms.Management.FormsManager();

            if (!formsManager.Open(serverName, port))
            {
                throw new InvalidOperationException(
                    $"Unable to open a connection to the K2 Forms management server at {serverName}:{port}.");
            }

            var explorer = formsManager.GetForms();
            var forms = explorer?.Forms?
                .Cast<SourceCode.Forms.Management.FormInfo>()
                .Where(f => f != null)
                .OrderBy(f => f.DisplayName ?? f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return forms ?? new List<SourceCode.Forms.Management.FormInfo>();
        }
        private async void DeployK2_Click(object sender, RoutedEventArgs e)
        {
            await Task.CompletedTask;
        }

        private async void DownloadK2Log_Click(object sender, RoutedEventArgs e)
        {
            if (_generationHandlers != null)
                await _generationHandlers.DownloadK2ConversionLog();
        }

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

        #region Rules Mapping Handlers

        private void RefreshRulesMapping_Click(object sender, RoutedEventArgs e)
        {
            _rulesMappingHandlers?.RefreshRulesMapping();
        }

        private void ExportRulesMapping_Click(object sender, RoutedEventArgs e)
        {
            _rulesMappingHandlers?.ExportRulesMappings();
        }

        private void RuleFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            _rulesMappingHandlers?.OnRuleFilterChanged(RuleFilterTextBox.Text);
        }

        private void RuleStatusFilter_Changed(object sender, SelectionChangedEventArgs e)
        {
            _rulesMappingHandlers?.OnStatusFilterChanged(RuleStatusFilterCombo.SelectedIndex);
        }

        private void RulesMappingList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedItem = RulesMappingListBox.SelectedItem as RuleMappingItem;
            _rulesMappingHandlers?.OnRuleSelected(selectedItem);
        }

        private void K2XmlOutput_TextChanged(object sender, TextChangedEventArgs e)
        {
            _rulesMappingHandlers?.OnK2XmlChanged(K2XmlOutput.Text);
        }

        private void MappingNotes_TextChanged(object sender, TextChangedEventArgs e)
        {
            _rulesMappingHandlers?.OnMappingNotesChanged(MappingNotesText.Text);
        }

        private void CopyK2Xml_Click(object sender, RoutedEventArgs e)
        {
            _rulesMappingHandlers?.CopyK2Xml();
        }

        private void SaveRuleMapping_Click(object sender, RoutedEventArgs e)
        {
            _rulesMappingHandlers?.SaveRuleMapping();
        }

        private void CopyRuleDetails_Click(object sender, RoutedEventArgs e)
        {
            _rulesMappingHandlers?.CopyRuleDetailsToClipboard();
        }

        private void CopyAllFilteredRules_Click(object sender, RoutedEventArgs e)
        {
            _rulesMappingHandlers?.CopyAllFilteredRulesToClipboard();
        }

        #endregion

        #region Form Structure Tree Handlers

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

        /// <summary>
        /// Opens the andyhayes.ai support website in the default browser
        /// </summary>
        private void OpenSupportWebsite_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://andyhayes.ai",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open browser: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Refreshes the JSON output using the currently loaded analysis data.
        /// </summary>
        public async Task RefreshJsonOutputWithCurrentData()
        {
            if (_analysisHandlers != null && _allAnalysisResults != null && _allAnalysisResults.Any())
            {
                await _analysisHandlers.DisplayCombinedAnalysisResults(_allAnalysisResults);
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
                GenerateSPTab.IsEnabled = true;

                // Also enable the generation buttons within each tab
                GenerateSqlButton.IsEnabled = true;
                DownloadNintexButton.IsEnabled = true;
                GenerateK2Button.IsEnabled = true;
                GenerateSPCsvButton.IsEnabled = true;
                GenerateSPPowerShellButton.IsEnabled = true;

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

        /// <summary>
        /// Presents every imported InfoPath form alongside a dropdown of the existing K2 forms,
        /// letting the user map each InfoPath form to a K2 form. Results are stored in
        /// <see cref="_k2FormMappings"/> for use during K2 generation.
        /// </summary>
        private void ShowExistingK2FormsMappingDialog(List<SourceCode.Forms.Management.FormInfo> forms)
        {
            // Build the dropdown choices: a "do not map" sentinel followed by every K2 form.
            var choices = new List<K2FormChoice>
            {
                new K2FormChoice { Display = "— Do not map —", Form = null }
            };
            choices.AddRange(forms.Select(f => new K2FormChoice
            {
                Display = string.IsNullOrWhiteSpace(f.CategoryPath)
                    ? $"{f.DisplayName ?? f.Name}  ({f.Name})"
                    : $"{f.DisplayName ?? f.Name}  ({f.Name})  ·  {f.CategoryPath}",
                Form = f
            }));

            var rowPanel = new StackPanel { Margin = new Thickness(12, 4, 12, 4) };
            var rowControls = new List<(string Key, string Display, ComboBox Combo)>();

            // Explicit container alignment so the default ComboBoxItem template does not emit
            // the noisy (and harmless) "Cannot find source for binding ... ComboBoxItem" warnings.
            var comboItemStyle = new Style(typeof(ComboBoxItem));
            comboItemStyle.Setters.Add(new Setter(System.Windows.Controls.Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
            comboItemStyle.Setters.Add(new Setter(System.Windows.Controls.Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));

            foreach (var kvp in _allFormDefinitions.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                var def = kvp.Value;
                var ipDisplay = !string.IsNullOrWhiteSpace(def?.Title) ? def.Title
                                : !string.IsNullOrWhiteSpace(def?.FormName) ? def.FormName
                                : kvp.Key;

                var rowGrid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });

                var label = new TextBlock
                {
                    Text = ipDisplay,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = kvp.Key
                };
                Grid.SetColumn(label, 0);

                // Each combo gets its own independent view so typing in one row does not
                // filter the dropdowns of the other rows.
                var view = new System.Windows.Data.ListCollectionView(choices);

                // The filter text is captured per-combo and read straight off the editable
                // TextBox. We must NOT read ComboBox.Text inside the filter: refreshing the
                // view resets the view's current item, and a current-item-synchronized combo
                // would then overwrite the text the user just typed (search appears dead).
                string filterText = null;

                view.Filter = item =>
                {
                    if (!(item is K2FormChoice c) || string.IsNullOrEmpty(c.Display)) return false;
                    if (string.IsNullOrWhiteSpace(filterText)) return true;
                    // A complete selection (text equals an item) shows the full list again.
                    if (choices.Any(x => string.Equals(x.Display, filterText, StringComparison.Ordinal))) return true;
                    return c.Display.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0;
                };

                var combo = new ComboBox
                {
                    ItemsSource = view,
                    IsEditable = true,
                    IsTextSearchEnabled = false,
                    StaysOpenOnEdit = true,
                    // Decouple the combo selection from the view's current item so refreshing
                    // the filter does not reset the typed text.
                    IsSynchronizedWithCurrentItem = false,
                    SelectedIndex = 0,
                    VerticalAlignment = VerticalAlignment.Center,
                    MaxDropDownHeight = 300,
                    ItemContainerStyle = comboItemStyle
                };

                combo.AddHandler(
                    System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                    new TextChangedEventHandler((s, e) =>
                    {
                        // Read the actual editable TextBox text (ComboBox.Text can lag here).
                        filterText = (e.OriginalSource as TextBox)?.Text ?? combo.Text;
                        view.Refresh();
                        if (combo.IsKeyboardFocusWithin && !string.IsNullOrEmpty(filterText))
                        {
                            combo.IsDropDownOpen = true;
                        }
                    }));

                // Restore an existing mapping, else attempt a best-effort auto-match by name.
                if (_k2FormMappings.TryGetValue(kvp.Key, out var existing) && existing != null)
                {
                    var match = choices.FirstOrDefault(c => c.Form != null && c.Form.Guid == existing.K2FormGuid);
                    if (match != null) combo.SelectedItem = match;
                }
                else
                {
                    var auto = choices.FirstOrDefault(c => c.Form != null &&
                        (string.Equals(c.Form.DisplayName, ipDisplay, StringComparison.OrdinalIgnoreCase) ||
                         (!string.IsNullOrWhiteSpace(def?.FormName) &&
                          string.Equals(c.Form.Name, def.FormName, StringComparison.OrdinalIgnoreCase))));
                    if (auto != null) combo.SelectedItem = auto;
                }

                Grid.SetColumn(combo, 1);
                rowGrid.Children.Add(label);
                rowGrid.Children.Add(combo);
                rowPanel.Children.Add(rowGrid);

                rowControls.Add((kvp.Key, ipDisplay, combo));
            }

            var header = new TextBlock
            {
                Text = $"Map each imported InfoPath form (left) to an existing K2 form (right). " +
                       $"{forms.Count} K2 form(s) available.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(12, 12, 12, 6)
            };

            var columnHeaders = new Grid { Margin = new Thickness(12, 0, 12, 4) };
            columnHeaders.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            columnHeaders.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
            var ipHeader = new TextBlock { Text = "InfoPath form", FontWeight = FontWeights.SemiBold };
            var k2Header = new TextBlock { Text = "K2 form", FontWeight = FontWeights.SemiBold };
            Grid.SetColumn(ipHeader, 0);
            Grid.SetColumn(k2Header, 1);
            columnHeaders.Children.Add(ipHeader);
            columnHeaders.Children.Add(k2Header);

            var topPanel = new StackPanel();
            topPanel.Children.Add(header);
            topPanel.Children.Add(columnHeaders);

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = rowPanel
            };

            var okButton = new Button { Content = "Save Mapping", Width = 120, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var cancelButton = new Button { Content = "Cancel", Width = 90, IsCancel = true };
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(12)
            };
            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);

            var layout = new DockPanel();
            DockPanel.SetDock(topPanel, Dock.Top);
            DockPanel.SetDock(buttonPanel, Dock.Bottom);
            layout.Children.Add(topPanel);
            layout.Children.Add(buttonPanel);
            layout.Children.Add(scroll);

            var dialog = new Window
            {
                Title = "Map InfoPath Forms to Existing K2 Forms",
                Width = 760,
                Height = 540,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Content = layout
            };

            okButton.Click += (s, e) => { dialog.DialogResult = true; };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            // Persist the selected mappings.
            _k2FormMappings.Clear();
            foreach (var (key, display, combo) in rowControls)
            {
                var choice = combo.SelectedItem as K2FormChoice;

                // If the user typed a name but did not click an entry, resolve it from the text.
                if (choice == null || !string.Equals(choice.Display, combo.Text, StringComparison.Ordinal))
                {
                    var typed = combo.Text?.Trim();
                    if (!string.IsNullOrEmpty(typed))
                    {
                        choice = choices.FirstOrDefault(c => string.Equals(c.Display, typed, StringComparison.OrdinalIgnoreCase))
                                 ?? choices.FirstOrDefault(c => c.Form != null && (
                                        string.Equals(c.Form.Name, typed, StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(c.Form.DisplayName, typed, StringComparison.OrdinalIgnoreCase)));
                    }
                }

                if (choice != null && choice.Form != null)
                {
                    _k2FormMappings[key] = new K2FormMapping
                    {
                        InfoPathFormKey = key,
                        InfoPathFormDisplay = display,
                        K2FormName = choice.Form.Name,
                        K2FormDisplayName = choice.Form.DisplayName,
                        K2FormGuid = choice.Form.Guid
                    };
                }
            }

            var mapped = _k2FormMappings.Count;
            SelectedK2FormTextBlock.Text = mapped == 0
                ? "No InfoPath forms mapped to existing K2 forms."
                : $"{mapped} of {rowControls.Count} InfoPath form(s) mapped to existing K2 forms.";

            K2GenerationLog.Text += $"Saved {mapped} K2 form mapping(s).\n";
            foreach (var m in _k2FormMappings.Values)
            {
                K2GenerationLog.Text += $"   • {m.InfoPathFormDisplay} → {m.K2FormDisplayName} [{m.K2FormName}]\n";
            }

            UpdateStatus($"Saved {mapped} K2 form mapping(s)", MessageSeverity.Info);
        }

        #endregion

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
