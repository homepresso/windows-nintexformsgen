using Microsoft.Win32;
using NWConverter.Models;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.Windows;
using System.IO;

namespace NWConverter
{
    public partial class PreviewWindow : Window
    {
        private readonly FormDefinition _formDefinition;
        private readonly string _sourceFileName;

        public PreviewWindow(FormDefinition formDefinition, string sourceFileName)
        {
            InitializeComponent();
            _formDefinition = formDefinition;
            _sourceFileName = sourceFileName;

            TitleTextBlock.Text = $"Preview: {formDefinition.Name}";
            SubtitleTextBlock.Text = $"Converted from {sourceFileName}";

            PopulateFormStructure();
            PopulateFormDetails();
        }

        private void PopulateFormStructure()
        {
            var rootNode = new TreeNodeItem
            {
                Name = _formDefinition.Name,
                Icon = "FileDocument"
            };

            // Add form properties
            var propertiesNode = new TreeNodeItem
            {
                Name = "Properties",
                Icon = "Cog"
            };
            propertiesNode.Children.Add(new TreeNodeItem { Name = $"Version: {_formDefinition.Version}", Icon = "Information" });
            propertiesNode.Children.Add(new TreeNodeItem { Name = $"Form Type: {_formDefinition.FormType}", Icon = "Information" });
            propertiesNode.Children.Add(new TreeNodeItem { Name = $"Theme: {_formDefinition.Theme.Name}", Icon = "Palette" });
            rootNode.Children.Add(propertiesNode);

            // Add pages
            var pagesNode = new TreeNodeItem
            {
                Name = "Pages",
                Icon = "FileMultiple"
            };
            foreach (var page in _formDefinition.PageSettings.Pages)
            {
                pagesNode.Children.Add(new TreeNodeItem { Name = page.Name, Icon = "File" });
            }
            rootNode.Children.Add(pagesNode);

            // Add rows and controls
            var rowsNode = new TreeNodeItem
            {
                Name = "Form Layout",
                Icon = "ViewGrid"
            };
            for (int i = 0; i < _formDefinition.Rows.Count; i++)
            {
                var row = _formDefinition.Rows[i];
                var rowNode = new TreeNodeItem
                {
                    Name = $"Row {i + 1}",
                    Icon = "ViewGrid"
                };

                foreach (var control in row.Controls)
                {
                    var controlNode = new TreeNodeItem
                    {
                        Name = $"{control.Properties.Name} ({control.Widget})",
                        Icon = GetControlIcon(control.Widget)
                    };
                    rowNode.Children.Add(controlNode);
                }

                rowsNode.Children.Add(rowNode);
            }
            rootNode.Children.Add(rowsNode);

            // Add translations
            if (_formDefinition.Translations.ContainsKey("en") && _formDefinition.Translations["en"].Any())
            {
                var translationsNode = new TreeNodeItem
                {
                    Name = "Translations",
                    Icon = "Translate"
                };
                foreach (var translation in _formDefinition.Translations["en"].Take(10)) // Show first 10
                {
                    translationsNode.Children.Add(new TreeNodeItem { Name = $"{translation.Key}: {translation.Value}", Icon = "Text" });
                }
                if (_formDefinition.Translations["en"].Count > 10)
                {
                    translationsNode.Children.Add(new TreeNodeItem { Name = $"... and {_formDefinition.Translations["en"].Count - 10} more", Icon = "DotsHorizontal" });
                }
                rootNode.Children.Add(translationsNode);
            }

            FormStructureTreeView.ItemsSource = new ObservableCollection<TreeNodeItem> { rootNode };
        }

        private void PopulateFormDetails()
        {
            var json = JsonConvert.SerializeObject(_formDefinition, Formatting.Indented);
            FormDetailsTextBox.Text = json;
        }

        private string GetControlIcon(string widget)
        {
            return widget switch
            {
                "textbox" => "TextBox",
                "multilinetext" => "TextBoxMultiple",
                "datetime" => "Calendar",
                "choice" => "FormatListBulleted",
                "boolean" => "CheckBox",
                "number" => "Numeric",
                "currency" => "CurrencyUsd",
                "email" => "Email",
                "file-upload" => "FileUpload",
                "signature" => "Pen",
                "people-picker-core" => "AccountMultiple",
                "repeating-section" => "Table",
                "group-control" => "Folder",
                "image" => "Image",
                "barcode" => "Barcode",
                "geolocation" => "MapMarker",
                "data-lookup" => "Database",
                "button" => "ButtonCursor",
                "richtext-label" => "FormatText",
                "space" => "Space",
                "actionpanel" => "PlayCircle",
                _ => "HelpCircle"
            };
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var saveFileDialog = new SaveFileDialog
            {
                Title = "Export Form Definition",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName = $"{_formDefinition.Name}_nintex.json"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    var json = JsonConvert.SerializeObject(_formDefinition, Formatting.Indented);
                    File.WriteAllText(saveFileDialog.FileName, json);
                    System.Windows.MessageBox.Show("Form exported successfully!", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error exporting file: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    public class TreeNodeItem
    {
        public string Name { get; set; } = "";
        public string Icon { get; set; } = "HelpCircle";
        public ObservableCollection<TreeNodeItem> Children { get; set; } = new();
    }
}
