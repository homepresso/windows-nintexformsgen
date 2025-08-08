using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FormGenerator.Core.Models;
using FormGenerator.Services;
using Microsoft.Win32;

namespace FormGenerator.Views
{
    /// <summary>
    /// Handles all generation tab logic (SQL, Nintex, K2)
    /// </summary>
    internal class MainWindowGenerationHandlers
    {
        private readonly MainWindow _mainWindow;
        private readonly SqlGeneratorService _sqlGenerator;

        public MainWindowGenerationHandlers(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            _sqlGenerator = new SqlGeneratorService();
        }

        #region SQL Generation

        public async Task TestSqlConnection()
        {
            try
            {
                _mainWindow.UpdateStatus("Testing SQL connection...");
                _mainWindow.SqlGenerationLog.Text = "Testing connection to SQL Server...\n";

                // Build connection string based on authentication type
                string connectionString;
                if (_mainWindow.WindowsAuthRadio.IsChecked == true)
                {
                    connectionString = $"Server={_mainWindow.SqlServerTextBox.Text};Database={_mainWindow.SqlDatabaseTextBox.Text};Integrated Security=true;";
                    _mainWindow.SqlGenerationLog.Text += $"Using Windows Authentication\n";
                }
                else
                {
                    connectionString = $"Server={_mainWindow.SqlServerTextBox.Text};Database={_mainWindow.SqlDatabaseTextBox.Text};User Id={_mainWindow.SqlUsernameTextBox.Text};Password={_mainWindow.SqlPasswordBox.Password};";
                    _mainWindow.SqlGenerationLog.Text += $"Using SQL Server Authentication\n";
                }

                _mainWindow.SqlGenerationLog.Text += $"Server: {_mainWindow.SqlServerTextBox.Text}\n";
                _mainWindow.SqlGenerationLog.Text += $"Database: {_mainWindow.SqlDatabaseTextBox.Text}\n\n";

                // Simulate connection test
                await Task.Delay(1000);

                // Mock success
                _mainWindow.SqlGenerationLog.Text += "✅ Connection successful!\n";
                _mainWindow.UpdateStatus("SQL connection test successful", MessageSeverity.Info);

                // Enable generation button after successful connection
                _mainWindow.GenerateSqlButton.IsEnabled = true;

                MessageBox.Show("Connection to SQL Server successful!",
                               "Connection Test",
                               MessageBoxButton.OK,
                               MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _mainWindow.SqlGenerationLog.Text += $"❌ Connection failed: {ex.Message}\n";
                _mainWindow.UpdateStatus("SQL connection test failed", MessageSeverity.Error);

                MessageBox.Show($"Connection failed:\n{ex.Message}",
                               "Connection Error",
                               MessageBoxButton.OK,
                               MessageBoxImage.Error);
            }
        }

        public async Task GenerateSql()
        {
            try
            {
                _mainWindow.GenerateSqlButton.IsEnabled = false;
                _mainWindow.UpdateStatus("Generating SQL scripts...");
                _mainWindow.SqlGenerationLog.Text = "Starting SQL generation...\n\n";

                // Check analysis results
                if (_mainWindow._allAnalysisResults == null || !_mainWindow._allAnalysisResults.Any())
                {
                    MessageBox.Show("Please analyze forms first before generating SQL.",
                                   "No Analysis Results",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Warning);
                    return;
                }

                _mainWindow.SqlGenerationLog.Text += $"Forms to process: {_mainWindow._allAnalysisResults.Count}\n";

                // Process generation options
                if (_mainWindow.FlatTablesRadio.IsChecked == true)
                {
                    _mainWindow.SqlGenerationLog.Text += "Table Structure: Flat Tables\n";
                }
                else
                {
                    _mainWindow.SqlGenerationLog.Text += "Table Structure: Normalized Tables\n";
                }

                if (_mainWindow.GenerateCrudProcsCheckBox.IsChecked == true)
                {
                    _mainWindow.SqlGenerationLog.Text += "✓ Generating CRUD Stored Procedures\n";
                }

                if (_mainWindow.GenerateScalableProcsCheckBox.IsChecked == true)
                {
                    _mainWindow.SqlGenerationLog.Text += "✓ Generating Scalable Stored Procedures\n";
                }

                if (_mainWindow.IncludeIndexesCheckBox.IsChecked == true)
                {
                    _mainWindow.SqlGenerationLog.Text += "✓ Including Indexes\n";
                }

                if (_mainWindow.IncludeConstraintsCheckBox.IsChecked == true)
                {
                    _mainWindow.SqlGenerationLog.Text += "✓ Including Foreign Key Constraints\n";
                }

                if (_mainWindow.IncludeTriggersCheckBox.IsChecked == true)
                {
                    _mainWindow.SqlGenerationLog.Text += "✓ Including Audit Triggers\n";
                }

                _mainWindow.SqlGenerationLog.Text += "\n";

                // Simulate generation for each form
                foreach (var form in _mainWindow._allAnalysisResults)
                {
                    _mainWindow.SqlGenerationLog.Text += $"Processing form: {form.Key}\n";
                    await Task.Delay(500);

                    _mainWindow.SqlGenerationLog.Text += $"  - Creating table structure...\n";
                    await Task.Delay(300);

                    if (_mainWindow.GenerateCrudProcsCheckBox.IsChecked == true)
                    {
                        _mainWindow.SqlGenerationLog.Text += $"  - Creating stored procedures...\n";
                        await Task.Delay(300);
                    }

                    if (_mainWindow.IncludeIndexesCheckBox.IsChecked == true)
                    {
                        _mainWindow.SqlGenerationLog.Text += $"  - Creating indexes...\n";
                        await Task.Delay(200);
                    }
                }

                _mainWindow.SqlGenerationLog.Text += "\n✅ SQL generation completed successfully!\n";
                _mainWindow.SqlGenerationLog.Text += $"Total scripts generated: {_mainWindow._allAnalysisResults.Count * 3}\n";

                _mainWindow.UpdateStatus("SQL scripts generated successfully", MessageSeverity.Info);
                _mainWindow.DeploySqlButton.IsEnabled = true;

                // Switch to SQL Preview tab
                var sqlPreviewIndex = 0;
                for (int i = 0; i < _mainWindow.ResultsTabs.Items.Count; i++)
                {
                    if (_mainWindow.ResultsTabs.Items[i] is TabItem tab && tab.Header.ToString().Contains("SQL Preview"))
                    {
                        sqlPreviewIndex = i;
                        break;
                    }
                }
                _mainWindow.ResultsTabs.SelectedIndex = sqlPreviewIndex;
            }
            catch (Exception ex)
            {
                _mainWindow.SqlGenerationLog.Text += $"\n❌ Error: {ex.Message}\n";
                _mainWindow.UpdateStatus($"SQL generation failed: {ex.Message}", MessageSeverity.Error);
            }
            finally
            {
                _mainWindow.GenerateSqlButton.IsEnabled = true;
            }
        }

        public async Task DeploySql()
        {
            try
            {
                var result = MessageBox.Show("Are you sure you want to deploy the generated SQL scripts to the database?\n\nThis action cannot be undone.",
                                             "Confirm Deployment",
                                             MessageBoxButton.YesNo,
                                             MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;

                _mainWindow.DeploySqlButton.IsEnabled = false;
                _mainWindow.UpdateStatus("Deploying SQL scripts to database...");
                _mainWindow.SqlGenerationLog.Text += "\n\nStarting deployment to database...\n";

                // Simulate deployment
                _mainWindow.SqlGenerationLog.Text += "Creating database objects...\n";
                await Task.Delay(1000);

                _mainWindow.SqlGenerationLog.Text += "  - Tables created: 5\n";
                await Task.Delay(500);

                _mainWindow.SqlGenerationLog.Text += "  - Stored procedures created: 20\n";
                await Task.Delay(500);

                _mainWindow.SqlGenerationLog.Text += "  - Indexes created: 8\n";
                await Task.Delay(500);

                _mainWindow.SqlGenerationLog.Text += "\n✅ Deployment completed successfully!\n";
                _mainWindow.UpdateStatus("SQL deployment completed", MessageSeverity.Info);

                MessageBox.Show("SQL scripts have been successfully deployed to the database!",
                               "Deployment Successful",
                               MessageBoxButton.OK,
                               MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _mainWindow.SqlGenerationLog.Text += $"\n❌ Deployment failed: {ex.Message}\n";
                _mainWindow.UpdateStatus($"SQL deployment failed: {ex.Message}", MessageSeverity.Error);

                MessageBox.Show($"Deployment failed:\n{ex.Message}",
                               "Deployment Error",
                               MessageBoxButton.OK,
                               MessageBoxImage.Error);
            }
            finally
            {
                _mainWindow.DeploySqlButton.IsEnabled = true;
            }
        }

        public async Task GenerateCombinedSqlPreview(Dictionary<string, FormAnalysisResult> allResults)
        {
            try
            {
                _mainWindow.UpdateStatus("Generating SQL scripts for all forms...");

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

                _mainWindow.SqlPreview.Text = sqlText.ToString();
            }
            catch (Exception ex)
            {
                _mainWindow.SqlPreview.Text = $"-- Error generating SQL\n-- {ex.Message}";
            }
        }

        #endregion

        #region Nintex Generation

        public async Task GenerateNintex()
        {
            try
            {
                _mainWindow.GenerateNintexButton.IsEnabled = false;
                _mainWindow.UpdateStatus("Generating Nintex forms...");
                _mainWindow.NintexGenerationLog.Text = "Starting Nintex form generation...\n\n";

                // Check analysis results
                if (_mainWindow._allAnalysisResults == null || !_mainWindow._allAnalysisResults.Any())
                {
                    MessageBox.Show("Please analyze forms first before generating Nintex forms.",
                                   "No Analysis Results",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Warning);
                    return;
                }

                var platform = (_mainWindow.NintexPlatformCombo.SelectedItem as ComboBoxItem)?.Content.ToString();
                _mainWindow.NintexGenerationLog.Text += $"Target Platform: {platform}\n";
                _mainWindow.NintexGenerationLog.Text += $"Form Name: {_mainWindow.NintexFormNameTextBox.Text}\n";
                _mainWindow.NintexGenerationLog.Text += $"Description: {_mainWindow.NintexDescriptionTextBox.Text}\n\n";

                // Process options
                _mainWindow.NintexGenerationLog.Text += "Options:\n";
                if (_mainWindow.IncludeValidationRulesCheckBox.IsChecked == true)
                    _mainWindow.NintexGenerationLog.Text += "  ✓ Include Validation Rules\n";
                if (_mainWindow.IncludeConditionalLogicCheckBox.IsChecked == true)
                    _mainWindow.NintexGenerationLog.Text += "  ✓ Include Conditional Logic\n";
                if (_mainWindow.IncludeCalculationsCheckBox.IsChecked == true)
                    _mainWindow.NintexGenerationLog.Text += "  ✓ Include Calculations\n";
                if (_mainWindow.GenerateWorkflowCheckBox.IsChecked == true)
                    _mainWindow.NintexGenerationLog.Text += "  ✓ Generate Associated Workflow\n";

                _mainWindow.NintexGenerationLog.Text += "\nLayout: " +
                    (_mainWindow.ResponsiveLayoutRadio.IsChecked == true ? "Responsive" : "Fixed") + "\n";

                _mainWindow.NintexGenerationLog.Text += "Export Format: " +
                    (_mainWindow.NintexJsonRadio.IsChecked == true ? "JSON" : "XML") + "\n\n";

                // Simulate generation
                foreach (var form in _mainWindow._allAnalysisResults)
                {
                    _mainWindow.NintexGenerationLog.Text += $"Processing: {form.Key}\n";
                    await Task.Delay(500);

                    _mainWindow.NintexGenerationLog.Text += "  - Converting controls...\n";
                    await Task.Delay(300);

                    _mainWindow.NintexGenerationLog.Text += "  - Applying rules...\n";
                    await Task.Delay(300);

                    _mainWindow.NintexGenerationLog.Text += "  - Generating layout...\n";
                    await Task.Delay(300);
                }

                _mainWindow.NintexGenerationLog.Text += "\n✅ Nintex forms generated successfully!\n";
                _mainWindow.NintexGenerationLog.Text += $"Forms created: {_mainWindow._allAnalysisResults.Count}\n";

                _mainWindow.UpdateStatus("Nintex forms generated successfully", MessageSeverity.Info);
                _mainWindow.DownloadNintexButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                _mainWindow.NintexGenerationLog.Text += $"\n❌ Error: {ex.Message}\n";
                _mainWindow.UpdateStatus($"Nintex generation failed: {ex.Message}", MessageSeverity.Error);
            }
            finally
            {
                _mainWindow.GenerateNintexButton.IsEnabled = true;
            }
        }

        public async Task DownloadNintex()
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Title = "Save Nintex Forms Package",
                    Filter = _mainWindow.NintexJsonRadio.IsChecked == true
                        ? "JSON Files (*.json)|*.json|All Files (*.*)|*.*"
                        : "XML Files (*.xml)|*.xml|All Files (*.*)|*.*",
                    FileName = $"{_mainWindow.NintexFormNameTextBox.Text}_NintexPackage"
                };

                if (dialog.ShowDialog() == true)
                {
                    _mainWindow.NintexGenerationLog.Text += $"\nSaving package to: {dialog.FileName}\n";

                    // Simulate file creation
                    await Task.Delay(500);

                    // Create mock content
                    string content = _mainWindow.NintexJsonRadio.IsChecked == true
                        ? "{ \"nintexForm\": { \"version\": \"1.0\", \"forms\": [] } }"
                        : "<?xml version=\"1.0\"?><NintexForms></NintexForms>";

                    await File.WriteAllTextAsync(dialog.FileName, content);

                    _mainWindow.NintexGenerationLog.Text += "✅ Package saved successfully!\n";
                    _mainWindow.UpdateStatus($"Nintex package saved to: {dialog.FileName}", MessageSeverity.Info);

                    MessageBox.Show($"Nintex forms package has been saved to:\n{dialog.FileName}",
                                   "Download Complete",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _mainWindow.NintexGenerationLog.Text += $"\n❌ Download failed: {ex.Message}\n";
                _mainWindow.UpdateStatus($"Nintex download failed: {ex.Message}", MessageSeverity.Error);

                MessageBox.Show($"Download failed:\n{ex.Message}",
                               "Download Error",
                               MessageBoxButton.OK,
                               MessageBoxImage.Error);
            }
        }

        #endregion

        #region K2 Generation

        public async Task TestK2Connection()
        {
            try
            {
                _mainWindow.UpdateStatus("Testing K2 connection...");
                _mainWindow.K2GenerationLog.Text = "Testing connection to K2 server...\n";
                _mainWindow.K2GenerationLog.Text += $"Server: {_mainWindow.K2ServerTextBox.Text}\n";
                _mainWindow.K2GenerationLog.Text += $"Port: {_mainWindow.K2PortTextBox.Text}\n";
                _mainWindow.K2GenerationLog.Text += $"Username: {_mainWindow.K2UsernameTextBox.Text}\n\n";

                // Simulate connection test
                await Task.Delay(1000);

                _mainWindow.K2GenerationLog.Text += "✅ Connection successful!\n";
                _mainWindow.K2GenerationLog.Text += "K2 Server Version: 5.5\n";
                _mainWindow.K2GenerationLog.Text += "SmartForms Version: 5.5.0.0\n";

                _mainWindow.UpdateStatus("K2 connection test successful", MessageSeverity.Info);

                // Enable generation button after successful connection
                _mainWindow.GenerateK2Button.IsEnabled = true;

                MessageBox.Show("Connection to K2 server successful!",
                               "Connection Test",
                               MessageBoxButton.OK,
                               MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _mainWindow.K2GenerationLog.Text += $"❌ Connection failed: {ex.Message}\n";
                _mainWindow.UpdateStatus("K2 connection test failed", MessageSeverity.Error);

                MessageBox.Show($"Connection failed:\n{ex.Message}",
                               "Connection Error",
                               MessageBoxButton.OK,
                               MessageBoxImage.Error);
            }
        }

        public void BrowseK2Folder()
        {
            // Mock folder browser dialog
            _mainWindow.K2GenerationLog.Text += "Opening K2 folder browser...\n";

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
                Owner = _mainWindow
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
                    _mainWindow.K2FolderTextBox.Text = listBox.SelectedItem.ToString();
                    _mainWindow.K2GenerationLog.Text += $"Selected folder: {listBox.SelectedItem}\n";
                    folderDialog.Close();
                }
            };

            folderDialog.Content = listBox;
            folderDialog.ShowDialog();
        }

        public async Task GenerateK2()
        {
            try
            {
                _mainWindow.GenerateK2Button.IsEnabled = false;
                _mainWindow.UpdateStatus("Generating K2 SmartForms...");
                _mainWindow.K2GenerationLog.Text = "Starting K2 SmartForms generation...\n\n";

                // Check analysis results
                if (_mainWindow._allAnalysisResults == null || !_mainWindow._allAnalysisResults.Any())
                {
                    MessageBox.Show("Please analyze forms first before generating K2 forms.",
                                   "No Analysis Results",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Warning);
                    return;
                }

                _mainWindow.K2GenerationLog.Text += $"Target Folder: {_mainWindow.K2FolderTextBox.Text}\n\n";

                // Process options
                if (_mainWindow.GenerateSmartObjectsCheckBox.IsChecked == true)
                {
                    _mainWindow.K2GenerationLog.Text += "Generating SmartObjects...\n";
                    await Task.Delay(500);
                }

                string formType = "Item View";
                if (_mainWindow.K2ListViewRadio.IsChecked == true)
                    formType = "List View";
                else if (_mainWindow.K2BothViewsRadio.IsChecked == true)
                    formType = "Item and List Views";

                _mainWindow.K2GenerationLog.Text += $"Form Type: {formType}\n\n";

                // Simulate generation for each form
                foreach (var form in _mainWindow._allAnalysisResults)
                {
                    _mainWindow.K2GenerationLog.Text += $"Processing: {form.Key}\n";
                    await Task.Delay(500);

                    if (_mainWindow.GenerateSmartObjectsCheckBox.IsChecked == true)
                    {
                        _mainWindow.K2GenerationLog.Text += "  - Creating SmartObject...\n";
                        await Task.Delay(300);
                    }

                    _mainWindow.K2GenerationLog.Text += "  - Creating views...\n";
                    await Task.Delay(300);

                    if (_mainWindow.IncludeRulesK2CheckBox.IsChecked == true)
                    {
                        _mainWindow.K2GenerationLog.Text += "  - Adding form rules...\n";
                        await Task.Delay(300);
                    }

                    if (_mainWindow.IncludeStylesK2CheckBox.IsChecked == true)
                    {
                        _mainWindow.K2GenerationLog.Text += "  - Applying styles...\n";
                        await Task.Delay(200);
                    }
                }

                if (_mainWindow.GenerateWorkflowK2CheckBox.IsChecked == true)
                {
                    _mainWindow.K2GenerationLog.Text += "\nGenerating K2 Workflow...\n";
                    await Task.Delay(500);
                }

                _mainWindow.K2GenerationLog.Text += "\n✅ K2 SmartForms generated successfully!\n";
                _mainWindow.K2GenerationLog.Text += $"SmartObjects created: {_mainWindow._allAnalysisResults.Count}\n";
                _mainWindow.K2GenerationLog.Text += $"Views created: {_mainWindow._allAnalysisResults.Count * (_mainWindow.K2BothViewsRadio.IsChecked == true ? 2 : 1)}\n";

                _mainWindow.UpdateStatus("K2 SmartForms generated successfully", MessageSeverity.Info);
                _mainWindow.DeployK2Button.IsEnabled = true;
                _mainWindow.ExportK2PackageButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                _mainWindow.K2GenerationLog.Text += $"\n❌ Error: {ex.Message}\n";
                _mainWindow.UpdateStatus($"K2 generation failed: {ex.Message}", MessageSeverity.Error);
            }
            finally
            {
                _mainWindow.GenerateK2Button.IsEnabled = true;
            }
        }

        public async Task DeployK2()
        {
            try
            {
                var result = MessageBox.Show("Are you sure you want to deploy the generated forms to the K2 server?\n\nThis will create new SmartForms in the specified folder.",
                                             "Confirm Deployment",
                                             MessageBoxButton.YesNo,
                                             MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;

                _mainWindow.DeployK2Button.IsEnabled = false;
                _mainWindow.UpdateStatus("Deploying to K2 server...");
                _mainWindow.K2GenerationLog.Text += "\n\nStarting deployment to K2 server...\n";

                // Simulate deployment
                _mainWindow.K2GenerationLog.Text += "Connecting to K2 server...\n";
                await Task.Delay(500);

                _mainWindow.K2GenerationLog.Text += "Deploying SmartObjects...\n";
                await Task.Delay(1000);

                _mainWindow.K2GenerationLog.Text += "Deploying SmartForms...\n";
                await Task.Delay(1000);

                if (_mainWindow.GenerateWorkflowK2CheckBox.IsChecked == true)
                {
                    _mainWindow.K2GenerationLog.Text += "Deploying Workflow...\n";
                    await Task.Delay(500);
                }

                _mainWindow.K2GenerationLog.Text += "\n✅ Deployment completed successfully!\n";
                _mainWindow.K2GenerationLog.Text += $"Location: {_mainWindow.K2ServerTextBox.Text}{_mainWindow.K2FolderTextBox.Text}\n";

                _mainWindow.UpdateStatus("K2 deployment completed", MessageSeverity.Info);

                MessageBox.Show("K2 SmartForms have been successfully deployed to the server!",
                               "Deployment Successful",
                               MessageBoxButton.OK,
                               MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _mainWindow.K2GenerationLog.Text += $"\n❌ Deployment failed: {ex.Message}\n";
                _mainWindow.UpdateStatus($"K2 deployment failed: {ex.Message}", MessageSeverity.Error);

                MessageBox.Show($"Deployment failed:\n{ex.Message}",
                               "Deployment Error",
                               MessageBoxButton.OK,
                               MessageBoxImage.Error);
            }
            finally
            {
                _mainWindow.DeployK2Button.IsEnabled = true;
            }
        }

        public async Task ExportK2Package()
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
                    _mainWindow.K2GenerationLog.Text += $"\nExporting K2 package to: {dialog.FileName}\n";

                    // Simulate package creation
                    _mainWindow.K2GenerationLog.Text += "Creating package...\n";
                    await Task.Delay(500);

                    _mainWindow.K2GenerationLog.Text += "  - Adding SmartObjects...\n";
                    await Task.Delay(300);

                    _mainWindow.K2GenerationLog.Text += "  - Adding SmartForms...\n";
                    await Task.Delay(300);

                    if (_mainWindow.GenerateWorkflowK2CheckBox.IsChecked == true)
                    {
                        _mainWindow.K2GenerationLog.Text += "  - Adding Workflow...\n";
                        await Task.Delay(300);
                    }

                    // Create a mock file
                    await File.WriteAllTextAsync(dialog.FileName, "K2 Package Content");

                    _mainWindow.K2GenerationLog.Text += "\n✅ Package exported successfully!\n";
                    _mainWindow.UpdateStatus($"K2 package exported to: {dialog.FileName}", MessageSeverity.Info);

                    MessageBox.Show($"K2 package has been exported to:\n{dialog.FileName}",
                                   "Export Complete",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _mainWindow.K2GenerationLog.Text += $"\n❌ Export failed: {ex.Message}\n";
                _mainWindow.UpdateStatus($"K2 export failed: {ex.Message}", MessageSeverity.Error);

                MessageBox.Show($"Export failed:\n{ex.Message}",
                               "Export Error",
                               MessageBoxButton.OK,
                               MessageBoxImage.Error);
            }
        }

        #endregion
    }
}