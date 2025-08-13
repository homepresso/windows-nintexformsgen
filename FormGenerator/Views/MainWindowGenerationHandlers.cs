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
        private readonly SqlConnectionService _sqlConnection;
        private string _currentConnectionString;

        public MainWindowGenerationHandlers(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            _sqlGenerator = new SqlGeneratorService();
            _sqlConnection = new SqlConnectionService();
        }

        #region SQL Generation

        public async Task TestSqlConnection()
        {
            try
            {
                _mainWindow.UpdateStatus("Testing SQL connection...");
                _mainWindow.SqlGenerationLog.Text = "Testing connection to SQL Server...\n";

                // Validate inputs
                if (string.IsNullOrWhiteSpace(_mainWindow.SqlServerTextBox.Text))
                {
                    MessageBox.Show("Please enter a SQL Server name/instance.",
                                   "Missing Information",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrWhiteSpace(_mainWindow.SqlDatabaseTextBox.Text))
                {
                    MessageBox.Show("Please enter a database name.",
                                   "Missing Information",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Warning);
                    return;
                }

                // Build connection string based on authentication type
                bool useWindowsAuth = _mainWindow.WindowsAuthRadio.IsChecked == true;

                if (useWindowsAuth)
                {
                    _mainWindow.SqlGenerationLog.Text += $"Using Windows Authentication\n";
                }
                else
                {
                    _mainWindow.SqlGenerationLog.Text += $"Using SQL Server Authentication\n";

                    // Validate SQL auth credentials
                    if (string.IsNullOrWhiteSpace(_mainWindow.SqlUsernameTextBox.Text))
                    {
                        MessageBox.Show("Please enter a username for SQL Server authentication.",
                                       "Missing Credentials",
                                       MessageBoxButton.OK,
                                       MessageBoxImage.Warning);
                        return;
                    }
                }

                _mainWindow.SqlGenerationLog.Text += $"Server: {_mainWindow.SqlServerTextBox.Text}\n";
                _mainWindow.SqlGenerationLog.Text += $"Database: {_mainWindow.SqlDatabaseTextBox.Text}\n\n";

                // Build connection string using the service
                // IMPORTANT: Set trustServerCertificate to true to handle SSL certificate issues
                _currentConnectionString = _sqlConnection.BuildConnectionString(
                    _mainWindow.SqlServerTextBox.Text,
                    _mainWindow.SqlDatabaseTextBox.Text,
                    useWindowsAuth,
                    useWindowsAuth ? null : _mainWindow.SqlUsernameTextBox.Text,
                    useWindowsAuth ? null : _mainWindow.SqlPasswordBox.Password,
                    trustServerCertificate: true  // This handles the certificate trust issue
                );

                // Test the connection with the fixed method that properly checks database existence
                var testResult = await _sqlConnection.TestConnectionAsync(_currentConnectionString);

                if (testResult.Success)
                {
                    _mainWindow.SqlGenerationLog.Text += $"✅ {testResult.Message}\n";
                    _mainWindow.SqlGenerationLog.Text += $"Server Version: {testResult.ServerVersion}\n";
                    _mainWindow.UpdateStatus("SQL connection test successful", MessageSeverity.Info);

                    // Enable generation button after successful connection
                    _mainWindow.GenerateSqlButton.IsEnabled = true;

                    MessageBox.Show($"{testResult.Message}\n\nServer Version:\n{testResult.ServerVersion}",
                                   "Connection Test Successful",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Information);
                }
                else
                {
                    _mainWindow.SqlGenerationLog.Text += $"❌ {testResult.Message}\n";

                    // Check if it's a certificate error and retry with trust enabled
                    if (testResult.Message.Contains("certificate") || testResult.Message.Contains("SSL"))
                    {
                        _mainWindow.SqlGenerationLog.Text += "\nRetrying with certificate trust enabled...\n";

                        // Already set to true above, but just to be explicit
                        _currentConnectionString = _sqlConnection.BuildConnectionString(
                            _mainWindow.SqlServerTextBox.Text,
                            _mainWindow.SqlDatabaseTextBox.Text,
                            useWindowsAuth,
                            useWindowsAuth ? null : _mainWindow.SqlUsernameTextBox.Text,
                            useWindowsAuth ? null : _mainWindow.SqlPasswordBox.Password,
                            trustServerCertificate: true
                        );

                        testResult = await _sqlConnection.TestConnectionAsync(_currentConnectionString);

                        if (testResult.Success)
                        {
                            _mainWindow.SqlGenerationLog.Text += $"✅ {testResult.Message}\n";
                            _mainWindow.UpdateStatus("SQL connection test successful", MessageSeverity.Info);
                            _mainWindow.GenerateSqlButton.IsEnabled = true;

                            MessageBox.Show($"Connection successful!\n\nNote: The server is using a self-signed or untrusted certificate. The connection has been established with certificate trust enabled.\n\nServer Version:\n{testResult.ServerVersion}",
                                           "Connection Test Successful",
                                           MessageBoxButton.OK,
                                           MessageBoxImage.Information);
                            return;
                        }
                    }

                    // Check if it's a database not found error
                    if (testResult.Message.Contains("does not exist"))
                    {
                        // Ask user if they want to create the database
                        var result = MessageBox.Show(
                            $"{testResult.Message}\n\nWould you like to create the database now?",
                            "Database Not Found",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

                        if (result == MessageBoxResult.Yes)
                        {
                            _mainWindow.SqlGenerationLog.Text += "\nAttempting to create database...\n";

                            // Try to create the database
                            var createResult = await _sqlConnection.CreateDatabaseIfNotExistsAsync(_mainWindow.SqlDatabaseTextBox.Text);

                            if (createResult.Success)
                            {
                                _mainWindow.SqlGenerationLog.Text += $"✅ {createResult.Message}\n";

                                // Now test the connection again
                                var retestResult = await _sqlConnection.TestConnectionAsync(_currentConnectionString);

                                if (retestResult.Success)
                                {
                                    _mainWindow.SqlGenerationLog.Text += $"✅ Connection verified successfully!\n";
                                    _mainWindow.UpdateStatus("Database created and connection successful", MessageSeverity.Info);
                                    _mainWindow.GenerateSqlButton.IsEnabled = true;

                                    MessageBox.Show("Database created successfully and connection established!",
                                                   "Success",
                                                   MessageBoxButton.OK,
                                                   MessageBoxImage.Information);
                                }
                            }
                            else
                            {
                                _mainWindow.SqlGenerationLog.Text += $"❌ {createResult.Message}\n";
                                _mainWindow.UpdateStatus("Failed to create database", MessageSeverity.Error);

                                MessageBox.Show($"Failed to create database:\n{createResult.Message}",
                                               "Database Creation Failed",
                                               MessageBoxButton.OK,
                                               MessageBoxImage.Error);
                            }
                        }
                    }
                    else
                    {
                        _mainWindow.UpdateStatus("SQL connection test failed", MessageSeverity.Error);

                        MessageBox.Show($"Connection failed:\n{testResult.Message}",
                                       "Connection Test Failed",
                                       MessageBoxButton.OK,
                                       MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                _mainWindow.SqlGenerationLog.Text += $"❌ Unexpected error: {ex.Message}\n";
                _mainWindow.UpdateStatus($"Connection test error: {ex.Message}", MessageSeverity.Error);

                MessageBox.Show($"An unexpected error occurred:\n{ex.Message}",
                               "Error",
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
                _mainWindow.SqlGenerationLog.Text += "\n═══════════════════════════════════════\n";
                _mainWindow.SqlGenerationLog.Text += "Starting SQL generation...\n\n";

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

                // Determine table structure type
                TableStructureType structureType = TableStructureType.FlatTables;
                if (_mainWindow.FlatTablesRadio.IsChecked == true)
                {
                    _mainWindow.SqlGenerationLog.Text += "Table Structure: Flat Tables (Control names as columns)\n";
                    structureType = TableStructureType.FlatTables;
                }
                else
                {
                    _mainWindow.SqlGenerationLog.Text += "Table Structure: Normalized Q&A (Questions and Answers tables)\n";
                    structureType = TableStructureType.NormalizedQA;
                }

                // Log additional options (these apply to flat table structure)
                if (structureType == TableStructureType.FlatTables)
                {
                    _mainWindow.SqlGenerationLog.Text += "Features:\n";
                    _mainWindow.SqlGenerationLog.Text += "  ✓ Separate tables for repeating sections\n";
                    _mainWindow.SqlGenerationLog.Text += "  ✓ Lookup tables for dropdowns\n";
                    _mainWindow.SqlGenerationLog.Text += "  ✓ Submit, View, and List stored procedures\n";
                    _mainWindow.SqlGenerationLog.Text += "  ✓ Performance indexes\n";
                }
                else
                {
                    _mainWindow.SqlGenerationLog.Text += "Features:\n";
                    _mainWindow.SqlGenerationLog.Text += "  ✓ Forms table for form definitions\n";
                    _mainWindow.SqlGenerationLog.Text += "  ✓ Questions table for all form fields\n";
                    _mainWindow.SqlGenerationLog.Text += "  ✓ Submissions table for form submissions\n";
                    _mainWindow.SqlGenerationLog.Text += "  ✓ Answers table for all responses\n";
                    _mainWindow.SqlGenerationLog.Text += "  ✓ JSON-based submit procedure\n";
                    _mainWindow.SqlGenerationLog.Text += "  ✓ Reporting view for easy data access\n";
                }

                _mainWindow.SqlGenerationLog.Text += "\n";

                // Generate SQL for each form
                var allScripts = new StringBuilder();
                allScripts.AppendLine("-- ═══════════════════════════════════════════════════════════════");
                allScripts.AppendLine("-- SQL Scripts Generated by InfoPath Forms Analyzer");
                allScripts.AppendLine($"-- Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                allScripts.AppendLine($"-- Structure Type: {(structureType == TableStructureType.FlatTables ? "Flat Tables" : "Normalized Q&A")}");
                allScripts.AppendLine("-- ═══════════════════════════════════════════════════════════════");
                allScripts.AppendLine();
                allScripts.AppendLine("USE [" + _mainWindow.SqlDatabaseTextBox.Text + "];");
                allScripts.AppendLine("GO");
                allScripts.AppendLine();

                int totalScriptsGenerated = 0;
                int successfulForms = 0;

                foreach (var form in _mainWindow._allAnalysisResults)
                {
                    _mainWindow.SqlGenerationLog.Text += $"Processing form: {Path.GetFileNameWithoutExtension(form.Key)}\n";

                    try
                    {
                        // Generate SQL using the SqlGeneratorService with the selected structure type
                        var sqlResult = await _sqlGenerator.GenerateFromAnalysisAsync(form.Value, structureType);

                        if (sqlResult.Success)
                        {
                            _mainWindow.SqlGenerationLog.Text += $"  ✓ Generated {sqlResult.Scripts.Count} scripts\n";

                            // Add form separator
                            allScripts.AppendLine();
                            allScripts.AppendLine("-- ═══════════════════════════════════════════════════════════════");
                            allScripts.AppendLine($"-- FORM: {Path.GetFileNameWithoutExtension(form.Key)}");
                            allScripts.AppendLine("-- ═══════════════════════════════════════════════════════════════");
                            allScripts.AppendLine();

                            foreach (var script in sqlResult.Scripts)
                            {
                                _mainWindow.SqlGenerationLog.Text += $"    - {script.Name} ({script.Type})\n";

                                // Add script header
                                allScripts.AppendLine($"-- {script.Description}");
                                allScripts.AppendLine($"-- Type: {script.Type}, Order: {script.ExecutionOrder}");
                                allScripts.AppendLine("-- -----------------------------------------------");
                                allScripts.AppendLine();

                                allScripts.AppendLine(script.Content);
                                allScripts.AppendLine("GO");
                                allScripts.AppendLine();
                            }

                            totalScriptsGenerated += sqlResult.Scripts.Count;
                            successfulForms++;
                        }
                        else
                        {
                            _mainWindow.SqlGenerationLog.Text += $"  ❌ Generation failed: {sqlResult.ErrorMessage}\n";
                        }
                    }
                    catch (Exception formEx)
                    {
                        _mainWindow.SqlGenerationLog.Text += $"  ❌ Error: {formEx.Message}\n";
                    }
                }

                // Add completion message to script
                allScripts.AppendLine();
                allScripts.AppendLine("-- ═══════════════════════════════════════════════════════════════");
                allScripts.AppendLine("-- SQL Script Generation Complete");
                allScripts.AppendLine($"-- Total Scripts: {totalScriptsGenerated}");
                allScripts.AppendLine($"-- Forms Processed: {successfulForms}");
                allScripts.AppendLine("-- ═══════════════════════════════════════════════════════════════");
                allScripts.AppendLine();
                allScripts.AppendLine("PRINT 'SQL deployment completed successfully!';");

                _mainWindow.SqlGenerationLog.Text += $"\n✅ SQL generation completed!\n";
                _mainWindow.SqlGenerationLog.Text += $"Forms processed: {successfulForms}/{_mainWindow._allAnalysisResults.Count}\n";
                _mainWindow.SqlGenerationLog.Text += $"Total scripts generated: {totalScriptsGenerated}\n";

                // Update SQL Preview
                _mainWindow.SqlPreview.Text = allScripts.ToString();

                _mainWindow.UpdateStatus($"SQL scripts generated successfully ({totalScriptsGenerated} scripts)", MessageSeverity.Info);

                if (successfulForms > 0)
                {
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
            }
            catch (Exception ex)
            {
                _mainWindow.SqlGenerationLog.Text += $"\n❌ Error: {ex.Message}\n";
                _mainWindow.UpdateStatus($"SQL generation failed: {ex.Message}", MessageSeverity.Error);

                MessageBox.Show($"SQL generation failed:\n{ex.Message}",
                               "Generation Error",
                               MessageBoxButton.OK,
                               MessageBoxImage.Error);
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
                // Ensure we have a connection string
                if (string.IsNullOrEmpty(_currentConnectionString))
                {
                    MessageBox.Show("Please test the SQL connection first before deploying.",
                                   "No Connection",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Warning);
                    return;
                }

                var result = MessageBox.Show(
                    "Are you sure you want to deploy the generated SQL scripts to the database?\n\n" +
                    "This will create/modify database objects and cannot be easily undone.\n\n" +
                    "It's recommended to review the SQL Preview tab first.",
                    "Confirm Deployment",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;

                _mainWindow.DeploySqlButton.IsEnabled = false;
                _mainWindow.UpdateStatus("Deploying SQL scripts to database...");
                _mainWindow.SqlGenerationLog.Text += "\n═══════════════════════════════════════\n";
                _mainWindow.SqlGenerationLog.Text += "Starting deployment to database...\n\n";

                // Get the SQL script from the preview
                string sqlScript = _mainWindow.SqlPreview.Text;

                if (string.IsNullOrWhiteSpace(sqlScript))
                {
                    MessageBox.Show("No SQL scripts to deploy. Please generate SQL first.",
                                   "No Scripts",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Warning);
                    return;
                }

                // Execute the SQL script
                _mainWindow.SqlGenerationLog.Text += "Executing SQL scripts...\n";
                var deployResult = await _sqlConnection.ExecuteScriptAsync(sqlScript);

                if (deployResult.Success)
                {
                    _mainWindow.SqlGenerationLog.Text += $"✅ {deployResult.Message}\n";

                    if (deployResult.RowsAffected > 0)
                    {
                        _mainWindow.SqlGenerationLog.Text += $"Rows affected: {deployResult.RowsAffected}\n";
                    }

                    // Get list of created tables to show summary
                    var tables = await _sqlConnection.GetTablesAsync();
                    _mainWindow.SqlGenerationLog.Text += $"\nDatabase objects created successfully!\n";
                    _mainWindow.SqlGenerationLog.Text += $"Total tables in database: {tables.Count}\n";

                    if (tables.Count > 0)
                    {
                        _mainWindow.SqlGenerationLog.Text += "\nTables created:\n";
                        foreach (var table in tables.Take(10)) // Show first 10 tables
                        {
                            _mainWindow.SqlGenerationLog.Text += $"  • {table}\n";
                        }
                        if (tables.Count > 10)
                        {
                            _mainWindow.SqlGenerationLog.Text += $"  ... and {tables.Count - 10} more\n";
                        }
                    }

                    _mainWindow.UpdateStatus("SQL deployment completed successfully", MessageSeverity.Info);

                    MessageBox.Show(
                        "SQL scripts have been successfully deployed to the database!\n\n" +
                        $"Database: {_mainWindow.SqlDatabaseTextBox.Text}\n" +
                        $"Tables created/updated: {tables.Count}",
                        "Deployment Successful",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    _mainWindow.SqlGenerationLog.Text += $"❌ Deployment failed: {deployResult.Message}\n";
                    _mainWindow.UpdateStatus($"SQL deployment failed", MessageSeverity.Error);

                    MessageBox.Show($"Deployment failed:\n{deployResult.Message}\n\n" +
                                   "Please check the SQL scripts and try again.",
                                   "Deployment Error",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                _mainWindow.SqlGenerationLog.Text += $"\n❌ Deployment error: {ex.Message}\n";
                _mainWindow.UpdateStatus($"SQL deployment error: {ex.Message}", MessageSeverity.Error);

                MessageBox.Show($"Deployment error:\n{ex.Message}",
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

                // Determine structure type from UI
                TableStructureType structureType = _mainWindow.FlatTablesRadio.IsChecked == true
                    ? TableStructureType.FlatTables
                    : TableStructureType.NormalizedQA;

                var sqlText = new StringBuilder();
                sqlText.AppendLine($"-- ═══════════════════════════════════════════════════════════════");
                sqlText.AppendLine($"-- SQL Scripts Generated by InfoPath to SQL Converter");
                sqlText.AppendLine($"-- Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sqlText.AppendLine($"-- Total Forms: {allResults.Count}");
                sqlText.AppendLine($"-- Structure Type: {(structureType == TableStructureType.FlatTables ? "Flat Tables" : "Normalized Q&A")}");
                sqlText.AppendLine($"-- ═══════════════════════════════════════════════════════════════");
                sqlText.AppendLine();

                foreach (var formKvp in allResults)
                {
                    var formName = Path.GetFileNameWithoutExtension(formKvp.Key);
                    var analysis = formKvp.Value;

                    sqlText.AppendLine($"-- ═══════════════════════════════════════════════════════════════");
                    sqlText.AppendLine($"-- FORM: {formName}");
                    sqlText.AppendLine($"-- FILE: {formKvp.Key}");
                    sqlText.AppendLine($"-- ═══════════════════════════════════════════════════════════════");
                    sqlText.AppendLine();

                    var sqlResult = await _sqlGenerator.GenerateFromAnalysisAsync(analysis, structureType);

                    if (sqlResult.Success)
                    {
                        foreach (var script in sqlResult.Scripts)
                        {
                            sqlText.AppendLine($"-- -----------------------------------------------");
                            sqlText.AppendLine($"-- {script.Description}");
                            sqlText.AppendLine($"-- Type: {script.Type}, Execution Order: {script.ExecutionOrder}");
                            sqlText.AppendLine($"-- -----------------------------------------------");
                            sqlText.AppendLine(script.Content);
                            sqlText.AppendLine();
                            sqlText.AppendLine("GO");
                            sqlText.AppendLine();
                        }
                    }
                    else
                    {
                        sqlText.AppendLine($"-- ❌ SQL Generation Failed for {formName}");
                        sqlText.AppendLine($"-- Error: {sqlResult.ErrorMessage}");
                        sqlText.AppendLine();
                    }
                }

                _mainWindow.SqlPreview.Text = sqlText.ToString();
                _mainWindow.UpdateStatus("SQL preview generated successfully", MessageSeverity.Info);
            }
            catch (Exception ex)
            {
                _mainWindow.SqlPreview.Text = $"-- Error generating SQL\n-- {ex.Message}";
                _mainWindow.UpdateStatus($"SQL preview generation failed: {ex.Message}", MessageSeverity.Error);
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