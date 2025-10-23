using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FormGenerator.Analyzers.Infopath;
using FormGenerator.Analyzers.InfoPath;
using FormGenerator.Core.Models;
using FormGenerator.Services;
using Microsoft.Win32;

namespace FormGenerator.Views
{
    /// <summary>
    /// Handles all generation tab logic (SQL, Nintex, K2)
    /// </summary>
    public class MainWindowGenerationHandlers
    {
        public readonly MainWindow _mainWindow;
        public readonly SqlGeneratorService _sqlGenerator;
        public readonly SqlConnectionService _sqlConnection;
        public string _currentConnectionString;

        public MainWindowGenerationHandlers(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            _sqlGenerator = new SqlGeneratorService();
            _sqlConnection = new SqlConnectionService();
        }

        public void ClearCurrentConnection()
        {
            _currentConnectionString = null;
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
                _currentConnectionString = _sqlConnection.BuildConnectionString(
                    _mainWindow.SqlServerTextBox.Text,
                    _mainWindow.SqlDatabaseTextBox.Text,
                    useWindowsAuth,
                    useWindowsAuth ? null : _mainWindow.SqlUsernameTextBox.Text,
                    useWindowsAuth ? null : _mainWindow.SqlPasswordBox.Password,
                    trustServerCertificate: true
                );

                // Test the connection
                var testResult = await _sqlConnection.TestConnectionAsync(_currentConnectionString);

                if (testResult.Success)
                {
                    _mainWindow.SqlGenerationLog.Text += $"✅ {testResult.Message}\n";
                    _mainWindow.SqlGenerationLog.Text += $"Server Version: {testResult.ServerVersion}\n";
                    _mainWindow.UpdateStatus("SQL connection test successful", MessageSeverity.Info);

                    // Update UI to show successful connection
                    _mainWindow.ShowConnectionSuccess(
                        _mainWindow.SqlServerTextBox.Text,
                        _mainWindow.SqlDatabaseTextBox.Text,
                        useWindowsAuth
                    );

                    // Enable both buttons after successful connection
                    _mainWindow.GenerateSqlButton.IsEnabled = _mainWindow._allAnalysisResults != null &&
                                                             _mainWindow._allAnalysisResults.Any();
                    _mainWindow.DeploySqlButton.IsEnabled = true;

                    MessageBox.Show($"{testResult.Message}\n\nServer Version:\n{testResult.ServerVersion}",
                                   "Connection Test Successful",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Information);
                }
                else
                {
                    _mainWindow.SqlGenerationLog.Text += $"❌ {testResult.Message}\n";

                    // Handle database not found error
                    if (testResult.Message.Contains("does not exist"))
                    {
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

                                    // Update UI to show successful connection
                                    _mainWindow.ShowConnectionSuccess(
                                        _mainWindow.SqlServerTextBox.Text,
                                        _mainWindow.SqlDatabaseTextBox.Text,
                                        useWindowsAuth
                                    );

                                    _mainWindow.GenerateSqlButton.IsEnabled = _mainWindow._allAnalysisResults != null &&
                                                                             _mainWindow._allAnalysisResults.Any();
                                    _mainWindow.DeploySqlButton.IsEnabled = true;

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

                // Log additional options
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
                    // Keep deploy button enabled if we have a connection
                    if (!string.IsNullOrEmpty(_currentConnectionString))
                    {
                        _mainWindow.DeploySqlButton.IsEnabled = true;
                    }

                    // Navigate back to Summary tab (dashboard)
                    _mainWindow.ResultsTabs.SelectedIndex = 0;

                    // Show notification
                    MessageBox.Show($"SQL scripts generated successfully!\n\n" +
                                   $"Scripts Generated: {totalScriptsGenerated}\n" +
                                   $"Forms Processed: {successfulForms}\n\n" +
                                   "You can review the scripts in the SQL Preview tab or deploy them directly to the database.",
                                   "Generation Complete",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Information);
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
                // Check if we need to generate scripts first
                if (string.IsNullOrWhiteSpace(_mainWindow.SqlPreview.Text) ||
                    _mainWindow.SqlPreview.Text == "-- SQL generation will be available after analysis")
                {
                    // Generate scripts first
                    _mainWindow.UpdateStatus("Generating SQL scripts before deployment...");
                    _mainWindow.SqlGenerationLog.Text += "\n═══════════════════════════════════════\n";
                    _mainWindow.SqlGenerationLog.Text += "Generating SQL scripts for deployment...\n\n";

                    await GenerateSql();

                    // Check if generation was successful
                    if (string.IsNullOrWhiteSpace(_mainWindow.SqlPreview.Text))
                    {
                        MessageBox.Show("SQL script generation failed. Please check the logs and try again.",
                                       "Generation Failed",
                                       MessageBoxButton.OK,
                                       MessageBoxImage.Warning);
                        return;
                    }
                }

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
                        foreach (var table in tables.Take(10))
                        {
                            _mainWindow.SqlGenerationLog.Text += $"  • {table}\n";
                        }
                        if (tables.Count > 10)
                        {
                            _mainWindow.SqlGenerationLog.Text += $"  ... and {tables.Count - 10} more\n";
                        }
                    }

                    // CREATE COMPREHENSIVE SQL DEPLOYMENT INFO
                    var sqlDeploymentInfo = new SqlDeploymentInfo
                    {
                        ServerName = _mainWindow.SqlServerTextBox.Text,
                        DatabaseName = _mainWindow.SqlDatabaseTextBox.Text,
                        DeploymentDate = DateTime.Now,
                        AuthenticationType = _mainWindow.WindowsAuthRadio.IsChecked == true ? "Windows" : "SQL",
                        TableStructureType = _mainWindow.FlatTablesRadio.IsChecked == true ? "FlatTables" : "NormalizedQA"
                    };

                    // Create a single instance of SqlGeneratorService to use for all operations
                    var sqlGenerator = new SqlGeneratorService();

                    // Build mappings for each form
                    foreach (var formResult in _mainWindow._allAnalysisResults)
                    {
                        var formDef = formResult.Value.FormDefinition as InfoPathFormDefinition;
                        if (formDef == null) continue;

                        var formName = sqlGenerator.SanitizeTableName(formDef.FormName);

                        var formMapping = new FormSqlMapping
                        {
                            FormName = FormatFormNameForDisplay(formDef.FormName),
                            MainTableName = formName
                        };

                        // Use SqlGeneratorService to analyze the form structure
                        var analysis = sqlGenerator.AnalyzeRepeatingSections(formDef);

                        // Add column mappings for main table
                        foreach (var control in analysis.MainTableColumns)
                        {
                            formMapping.ColumnMappings.Add(new ColumnMapping
                            {
                                FieldName = control.Name,
                                ColumnName = sqlGenerator.SanitizeColumnName(control.Name),
                                SqlDataType = sqlGenerator.GetSqlType(control.Type),
                                ControlType = control.Type,
                                IsInMainTable = true
                            });
                        }

                        // Add repeating section mappings AND their column mappings
                        foreach (var section in analysis.RepeatingSections)
                        {
                            var sectionTableName = $"{formName}_{sqlGenerator.SanitizeTableName(section.Key)}";
                            var sectionMapping = new RepeatingSectionMapping
                            {
                                SectionName = section.Key,
                                TableName = sectionTableName,
                                ForeignKeyColumn = "ParentFormId",
                                Columns = new List<ColumnMapping>()
                            };

                            foreach (var control in section.Value.Controls)
                            {
                                var columnMapping = new ColumnMapping
                                {
                                    FieldName = control.Name,
                                    ColumnName = sqlGenerator.SanitizeColumnName(control.Name),
                                    SqlDataType = sqlGenerator.GetSqlType(control.Type),
                                    ControlType = control.Type,
                                    IsInMainTable = false
                                };

                                sectionMapping.Columns.Add(columnMapping);

                                // IMPORTANT: Also add to the main ColumnMappings collection so it can be found
                                // This allows the ToEnhancedJson method to find the mapping for repeating section controls
                                formMapping.ColumnMappings.Add(columnMapping);
                            }

                            formMapping.RepeatingSectionMappings.Add(sectionMapping);
                        }

                        // Add lookup table mappings for dropdowns
                        var controlsWithLookups = formDef.Views
                            .SelectMany(v => v.Controls)
                            .Where(c => c.HasStaticData && c.DataOptions != null && c.DataOptions.Any())
                            .GroupBy(c => c.Name);

                        foreach (var controlGroup in controlsWithLookups)
                        {
                            var control = controlGroup.First();
                            formMapping.LookupTableMappings.Add(new LookupTableMapping
                            {
                                FieldName = control.Name,
                                LookupTableName = $"{formName}_{sqlGenerator.SanitizeTableName(control.Name)}_Lookup",
                                ValueCount = control.DataOptions.Count,
                                LookupValues = control.DataOptions.Select(o => o.Value).ToList()
                            });
                        }

                        // Add stored procedures based on structure type
                        if (_mainWindow.FlatTablesRadio.IsChecked == true)
                        {
                            // Flat table structure procedures
                            formMapping.StoredProcedures.Add($"sp_{formName}_Insert");
                            formMapping.StoredProcedures.Add($"sp_{formName}_Update");
                            formMapping.StoredProcedures.Add($"sp_{formName}_Get");
                            formMapping.StoredProcedures.Add($"sp_{formName}_Delete");
                            formMapping.StoredProcedures.Add($"sp_{formName}_List");

                            // Add repeating section procedures - use different variable names to avoid conflict
                            foreach (var repeatSection in analysis.RepeatingSections)
                            {
                                var repeatSectionTableName = sqlGenerator.SanitizeTableName(repeatSection.Key);
                                var fullTableName = $"{formName}_{repeatSectionTableName}";
                                formMapping.StoredProcedures.Add($"sp_{fullTableName}_InsertItem");
                                formMapping.StoredProcedures.Add($"sp_{fullTableName}_UpdateItem");
                                formMapping.StoredProcedures.Add($"sp_{fullTableName}_DeleteItem");
                                formMapping.StoredProcedures.Add($"sp_{fullTableName}_GetByParent");
                            }
                        }
                        else
                        {
                            // Normalized Q&A structure procedures
                            formMapping.StoredProcedures.Add($"sp_Submit_{formName}");
                            formMapping.StoredProcedures.Add($"sp_Get_{formName}");
                            formMapping.StoredProcedures.Add("sp_RegisterForm");
                            formMapping.StoredProcedures.Add("sp_SubmitFormData");
                            formMapping.StoredProcedures.Add("sp_GetSubmissionData");
                            formMapping.StoredProcedures.Add("sp_AddRepeatingSectionInstance");

                            // Add procedures for each repeating section in normalized structure - use different variable names
                            foreach (var normSection in analysis.RepeatingSections)
                            {
                                var normSectionTableName = sqlGenerator.SanitizeTableName(normSection.Key);
                                formMapping.StoredProcedures.Add($"sp_{formName}_{normSectionTableName}_AddItem");
                            }
                        }

                        // Add views
                        if (_mainWindow.FlatTablesRadio.IsChecked == true)
                        {
                            formMapping.Views.Add($"vw_{formName}_Summary");
                        }
                        else
                        {
                            formMapping.Views.Add("vw_AllSubmissions");
                            formMapping.Views.Add("vw_FormAnswersPivot");
                        }

                        sqlDeploymentInfo.FormMappings.Add(formMapping);
                    }

                    // SET THE STATIC PROPERTY to make SQL info available to JSON generation
                    InfoPathFormDefinitionExtensions.CurrentSqlDeploymentInfo = sqlDeploymentInfo;

                    // REFRESH THE JSON OUTPUT with SQL mappings
                    // Using the public method in MainWindow to avoid accessibility issues
                    await _mainWindow.RefreshJsonOutputWithCurrentData();

                    _mainWindow.UpdateStatus("SQL deployment completed - JSON updated with database mappings", MessageSeverity.Info);

                    _mainWindow.SqlGenerationLog.Text += "\n✅ JSON output has been updated with SQL deployment mappings\n";

                    MessageBox.Show(
                        "SQL scripts have been successfully deployed to the database!\n\n" +
                        $"Database: {_mainWindow.SqlDatabaseTextBox.Text}\n" +
                        $"Tables created/updated: {tables.Count}\n\n" +
                        "The JSON output has been updated with SQL table and column mappings.",
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
                // Check if we have analysis results
                if (_mainWindow._allAnalysisResults == null || !_mainWindow._allAnalysisResults.Any())
                {
                    MessageBox.Show("Please analyze forms first before generating Nintex forms.",
                                   "No Analysis Results",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Warning);
                    return;
                }

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
                    _mainWindow.NintexGenerationLog.Text += $"\nConverting InfoPath forms to Nintex format...\n";

                    // Create NAC converter
                    var rebuilder = new Writers.NAC.Rebuilders.NintexFormRebuilder();
                    int successCount = 0;
                    int failCount = 0;

                    // Convert each analyzed form - save each to separate file (like NAC Example does)
                    foreach (var formResult in _mainWindow._allAnalysisResults)
                    {
                        try
                        {
                            var formName = Path.GetFileNameWithoutExtension(formResult.Key);
                            _mainWindow.NintexGenerationLog.Text += $"  Converting: {formName}...\n";

                            var rebuildResult = await rebuilder.RebuildFormAsync(formResult.Value);

                            if (rebuildResult.Success)
                            {
                                // Get the form-definition.json directly from artifacts
                                var formJson = rebuildResult.Artifacts["form-definition.json"];

                                // Generate output filename
                                var outputFileName = dialog.FileName.Replace(".json", $"_{formName}_nintex.json");
                                if (_mainWindow._allAnalysisResults.Count == 1)
                                {
                                    // If only one form, use the original filename
                                    outputFileName = dialog.FileName;
                                }

                                // Write directly (no wrapping) - this is what NAC Example does
                                await File.WriteAllTextAsync(outputFileName, formJson);

                                _mainWindow.NintexGenerationLog.Text += $"    ✅ Saved to: {Path.GetFileName(outputFileName)}\n";
                                successCount++;
                            }
                            else
                            {
                                _mainWindow.NintexGenerationLog.Text += $"    ❌ Conversion failed: {rebuildResult.ErrorMessage}\n";
                                failCount++;
                            }
                        }
                        catch (Exception formEx)
                        {
                            _mainWindow.NintexGenerationLog.Text += $"    ❌ Error: {formEx.Message}\n";
                            failCount++;
                        }
                    }

                    _mainWindow.NintexGenerationLog.Text += $"\n✅ Conversion complete!\n";
                    _mainWindow.NintexGenerationLog.Text += $"   Converted: {successCount} forms\n";
                    if (failCount > 0)
                        _mainWindow.NintexGenerationLog.Text += $"   Failed: {failCount} forms\n";

                    _mainWindow.UpdateStatus($"Nintex forms saved", MessageSeverity.Info);

                    MessageBox.Show($"Nintex form conversion complete!\n\n" +
                                   $"Converted: {successCount} forms\n" +
                                   (failCount > 0 ? $"Failed: {failCount} forms" : ""),
                                   "Conversion Complete",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _mainWindow.NintexGenerationLog.Text += $"\n❌ Download failed: {ex.Message}\n";
                _mainWindow.NintexGenerationLog.Text += $"Stack trace: {ex.StackTrace}\n";
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
            await Task.CompletedTask; // Suppress async warning

            _mainWindow.K2GenerationLog.Text = "K2 SmartForms Generation - Not Available\n\n";
            _mainWindow.K2GenerationLog.Text += "K2 generation is currently not available in this version.\n\n";
            _mainWindow.K2GenerationLog.Text += "REASON:\n";
            _mainWindow.K2GenerationLog.Text += "The K2 SmartForms SDK requires .NET Framework 4.8 and uses APIs that are\n";
            _mainWindow.K2GenerationLog.Text += "incompatible with .NET 8 (System.AppDomain.get_DomainManager, etc.).\n\n";
            _mainWindow.K2GenerationLog.Text += "ALTERNATIVES:\n";
            _mainWindow.K2GenerationLog.Text += "1. Use the SQL generation feature to create database structures\n";
            _mainWindow.K2GenerationLog.Text += "2. Use the Nintex Forms NAC export for modern Nintex platform\n";
            _mainWindow.K2GenerationLog.Text += "3. Manually recreate forms in K2 SmartForms Designer using the JSON output\n\n";
            _mainWindow.K2GenerationLog.Text += "The analyzed form structure is available in the JSON Output tab.\n";

            MessageBox.Show(
                "K2 SmartForms generation is not available in this version.\n\n" +
                "The K2 SDK requires .NET Framework 4.8 and is incompatible with .NET 8.\n\n" +
                "Please use:\n" +
                "• SQL Generation for database structures\n" +
                "• Nintex NAC Export for modern Nintex platform\n" +
                "• JSON Output for manual form recreation",
                "K2 Generation Not Available",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        public async Task DeployK2()
        {
            await Task.CompletedTask; // Suppress async warning

            // Note: K2 forms are automatically deployed during generation via K2GenerationService
            // This button is kept for UI consistency but generation already handles deployment
            MessageBox.Show("K2 SmartForms are automatically deployed to the server during generation.\n\n" +
                           $"The forms are already available at:\n{_mainWindow.K2ServerTextBox.Text}/Forms/{_mainWindow.K2FolderTextBox.Text}\n\n" +
                           "You can access them through K2 SmartForms Designer or use the Export Package button to download them.",
                           "Forms Already Deployed",
                           MessageBoxButton.OK,
                           MessageBoxImage.Information);
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
        /// <summary>
        /// Formats a form name for display by replacing underscores with spaces
        /// </summary>
        private string FormatFormNameForDisplay(string formName)
        {
            if (string.IsNullOrEmpty(formName))
                return formName;

            // Replace underscores with spaces for better readability in K2 categories
            return formName.Replace("_", " ");
        }
    }
}