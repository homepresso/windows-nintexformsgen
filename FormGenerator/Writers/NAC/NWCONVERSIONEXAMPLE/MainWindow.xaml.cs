using Microsoft.Win32;
using NWConverter.Models;
using NWConverter.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace NWConverter
{
    public partial class MainWindow : Window
    {
        private readonly FormConverter _formConverter;
        private readonly ObservableCollection<FileItem> _sourceFiles;
        private readonly ObservableCollection<FileItem> _outputFiles;
        private string _outputDirectory;

        public MainWindow()
        {
            InitializeComponent();
            _formConverter = new FormConverter();
            _sourceFiles = new ObservableCollection<FileItem>();
            _outputFiles = new ObservableCollection<FileItem>();
            _outputDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "NintexForms");

            SourceFilesListBox.ItemsSource = _sourceFiles;
            OutputFilesListBox.ItemsSource = _outputFiles;

            UpdateButtonStates();
        }

        private void AddFiles_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select Source Form Files",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                foreach (string fileName in openFileDialog.FileNames)
                {
                    AddSourceFile(fileName);
                }
            }
        }

        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            // For now, we'll use a simple approach - let user select multiple files
            // In a full implementation, you might want to use a third-party folder browser
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select JSON files from folder",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                foreach (string fileName in openFileDialog.FileNames)
                {
                    AddSourceFile(fileName);
                }
            }
        }

        private void AddSourceFile(string filePath)
        {
            var fileItem = new FileItem
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath)
            };

            if (!_sourceFiles.Any(f => f.FilePath == filePath))
            {
                _sourceFiles.Add(fileItem);
                UpdateButtonStates();
            }
        }

        private void RemoveFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is FileItem fileItem)
            {
                _sourceFiles.Remove(fileItem);
                UpdateButtonStates();
            }
        }

        private void Convert_Click(object sender, RoutedEventArgs e)
        {
            if (!_sourceFiles.Any())
            {
                System.Windows.MessageBox.Show("Please add source files first.", "No Files", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                ConvertButton.IsEnabled = false;
                ProgressBar.Visibility = Visibility.Visible;
                StatusTextBlock.Text = "Converting forms...";

                // Ensure output directory exists
                Directory.CreateDirectory(_outputDirectory);

                int processed = 0;
                int total = _sourceFiles.Count;

                foreach (var sourceFile in _sourceFiles)
                {
                    try
                    {
                        StatusTextBlock.Text = $"Converting {sourceFile.FileName}...";
                        ProgressBar.Value = (double)processed / total * 100;

                        var convertedForms = ConvertMultiple(sourceFile.FilePath);
                        foreach (var cf in convertedForms)
                        {
                            var outputPath = Path.Combine(_outputDirectory, cf.OutputFileName);
                            File.WriteAllText(outputPath, JsonConvert.SerializeObject(cf.Form, Formatting.Indented));
                            _outputFiles.Add(new FileItem { FilePath = outputPath, FileName = cf.OutputFileName });
                        }

                        processed++;
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"Error converting {sourceFile.FileName}: {ex.Message}", "Conversion Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }

                StatusTextBlock.Text = $"Conversion complete! {processed} of {total} files processed.";
                ProgressBar.Value = 100;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error during conversion: {ex.Message}", "Conversion Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ConvertButton.IsEnabled = true;
                ProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private void Preview_Click(object sender, RoutedEventArgs e)
        {
            if (!_sourceFiles.Any())
            {
                System.Windows.MessageBox.Show("Please add source files first.", "No Files", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var sourceFile = _sourceFiles.First();
                var convertedForms = ConvertMultiple(sourceFile.FilePath);
                var convertedForm = convertedForms.FirstOrDefault()?.Form;
                if (convertedForm != null)
                {
                    var previewWindow = new PreviewWindow(convertedForm, sourceFile.FileName);
                    previewWindow.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error creating preview: {ex.Message}", "Preview Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private List<ConvertedForm> ConvertMultiple(string filePath)
        {
            try
            {
                var jsonContent = File.ReadAllText(filePath);

                // Parse dynamically to support ANY root key (not just "Travel Request")
                var token = JToken.Parse(jsonContent);

                var results = new List<ConvertedForm>();

                void AddFormsFromSource(SourceForm form, string formDisplayName)
                {
                    if (form?.FormDefinition?.Views == null || !form.FormDefinition.Views.Any()) return;
                    foreach (var view in form.FormDefinition.Views)
                    {
                        var vn = view.ViewName ?? "view";
                        if (IsNonDesignView(vn)) continue;
                        var shortName = Path.GetFileNameWithoutExtension(vn);
                        var safeName = SanitizeFileName($"{formDisplayName}__{shortName}_nintex.json");
                        var nintex = _formConverter.ConvertFormForView(form, view, formDisplayName);
                        results.Add(new ConvertedForm { Form = nintex, OutputFileName = safeName });
                    }
                }

                if (token is JObject obj && obj.Properties().Any())
                {
                    foreach (var prop in obj.Properties())
                    {
                        if (prop.Value is JObject inner && inner["FormDefinition"] != null)
                        {
                            var sf = inner.ToObject<SourceForm>() ?? new SourceForm();
                            if (string.IsNullOrWhiteSpace(sf.FileName)) sf.FileName = prop.Name + ".xsn";
                            AddFormsFromSource(sf, prop.Name);
                        }
                    }
                    if (results.Any()) return results;
                }

                var direct = token.ToObject<SourceForm>();
                if (direct?.FormDefinition?.Views != null && direct.FormDefinition.Views.Any())
                {
                    var displayName = Path.GetFileNameWithoutExtension(direct.FileName ?? Path.GetFileNameWithoutExtension(filePath));
                    AddFormsFromSource(direct, displayName);
                    return results;
                }

                throw new Exception("Invalid form format (no FormDefinition found)");
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to parse {Path.GetFileName(filePath)}: {ex.Message}");
            }
        }

        private static bool IsNonDesignView(string viewName)
        {
            if (string.IsNullOrWhiteSpace(viewName)) return false;
            var s = viewName.ToLowerInvariant();
            return s.Contains("summary") || s.Contains("filter");
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }

        private void OpenOutput_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is FileItem fileItem)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = fileItem.FilePath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error opening file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OpenOutputFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _outputDirectory,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error opening output folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearOutput_Click(object sender, RoutedEventArgs e)
        {
            _outputFiles.Clear();
        }

        private void UpdateButtonStates()
        {
            bool hasSourceFiles = _sourceFiles.Any();
            ConvertButton.IsEnabled = hasSourceFiles;
            PreviewButton.IsEnabled = hasSourceFiles;
        }
    }

    public class ConvertedForm
    {
        public FormDefinition Form { get; set; } = new FormDefinition();
        public string OutputFileName { get; set; } = "";
    }

    public class FileItem
    {
        public string FilePath { get; set; } = "";
        public string FileName { get; set; } = "";
    }
}
