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
        }

        #region File Management

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
                        await File.WriteAllTextAsync(dialog.FileName, sqlContent);
                    }
                    else
                    {
                        // Export JSON - export all analyzed forms
                        var json = JsonConvert.SerializeObject(_allFormDefinitions, Formatting.Indented);
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

                    await File.WriteAllTextAsync(dialog.FileName, jsonText);
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