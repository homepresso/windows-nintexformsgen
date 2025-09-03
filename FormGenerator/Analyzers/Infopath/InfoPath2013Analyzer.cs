using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using FormGenerator.Core.Interfaces;
using FormGenerator.Core.Models;
using FormGenerator.Analyzers.Infopath;

namespace FormGenerator.Analyzers.InfoPath
{
    /// <summary>
    /// InfoPath 2013 specific analyzer implementation
    /// </summary>
    public class InfoPath2013Analyzer : IFormAnalyzer
    {
        private readonly EnhancedInfoPathParser _parser;

        public string AnalyzerName => "InfoPath 2013 Analyzer";
        public string SupportedVersion => "2013";
        public string[] SupportedFileExtensions => new[] { ".xsn" };

        public InfoPath2013Analyzer()
        {
            _parser = new EnhancedInfoPathParser();
        }

        /// <summary>
        /// Checks if this analyzer can handle the given file
        /// </summary>
        public bool CanAnalyze(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;

            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return Array.Exists(SupportedFileExtensions, ext => ext.Equals(extension, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Analyzes an InfoPath form asynchronously
        /// </summary>
        public async Task<FormAnalysisResult> AnalyzeFormAsync(string filePath)
        {
            var result = new FormAnalysisResult
            {
                FormName = Path.GetFileNameWithoutExtension(filePath),
                FormType = "InfoPath",
                AnalyzerUsed = AnalyzerName,
                AnalysisDate = DateTime.Now
            };

            var stopwatch = Stopwatch.StartNew();

            try
            {
                Debug.WriteLine($"Starting analysis of {filePath}");

                // Run analysis in background thread
                var formDefinition = await Task.Run(() => _parser.ParseXsnFile(filePath));

                if (formDefinition != null)
                {
                    // Set basic properties
                    formDefinition.FormName = Path.GetFileNameWithoutExtension(filePath);
                    formDefinition.FileName = Path.GetFileName(filePath);

                    if (string.IsNullOrEmpty(formDefinition.Title))
                    {
                        formDefinition.Title = formDefinition.FormName;
                    }

                    // Build simplified data columns
                    BuildSimplifiedDataColumns(formDefinition);

                    result.FormDefinition = formDefinition;
                    result.Success = true;

                    // Add simplified JSON representation
                    result.SimplifiedJson = formDefinition.ToSimplifiedJson();

                    // Add analysis messages
                    AddSimplifiedAnalysisMessages(result, formDefinition);

                    Debug.WriteLine($"Analysis completed successfully for {filePath}");
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.Messages.Add(new AnalysisMessage
                {
                    Severity = MessageSeverity.Error,
                    Message = "Analysis failed",
                    Details = ex.ToString(),
                    Source = "InfoPath2013Analyzer"
                });
                Debug.WriteLine($"Analysis error: {ex}");
            }
            finally
            {
                stopwatch.Stop();
                result.AnalysisDuration = stopwatch.Elapsed;
                Debug.WriteLine($"Analysis duration: {stopwatch.Elapsed.TotalSeconds:F2} seconds");
            }

            return result;
        }

        private void BuildSimplifiedDataColumns(InfoPathFormDefinition formDef)
        {
            var dataColumns = new Dictionary<string, DataColumn>();

            // Iterate through all controls in all views
            foreach (var view in formDef.Views)
            {
                foreach (var control in view.Controls)
                {
                    // Skip non-data controls
                    if (IsNonDataControl(control))
                        continue;

                    var columnName = !string.IsNullOrEmpty(control.Binding)
                        ? ExtractColumnName(control.Binding)
                        : control.Name;

                    if (string.IsNullOrEmpty(columnName))
                        continue;

                    // Create unique key
                    var key = control.IsInRepeatingSection
                        ? $"{control.RepeatingSectionName}.{columnName}"
                        : columnName;

                    if (!dataColumns.ContainsKey(key))
                    {
                        dataColumns[key] = new DataColumn
                        {
                            ColumnName = columnName,
                            DisplayName = control.Label ?? control.Name,
                            Type = control.Type,
                            IsRepeating = control.IsInRepeatingSection,
                            RepeatingSection = control.RepeatingSectionName,
                            ValidValues = control.DataOptions,
                            DefaultValue = control.Properties?.ContainsKey("DefaultValue") == true
                                ? control.Properties["DefaultValue"]
                                : null
                        };
                    }
                }
            }

            formDef.Data = dataColumns.Values.ToList();
        }

        private bool IsNonDataControl(ControlDefinition control)
        {
            string[] nonDataTypes = { "Label", "Button", "Section", "RepeatingSection", "RepeatingTable" };
            return nonDataTypes.Contains(control.Type) ||
                   control.IsMergedIntoParent ||
                   string.IsNullOrEmpty(control.Name);
        }

        private string ExtractColumnName(string binding)
        {
            if (string.IsNullOrEmpty(binding))
                return "";

            // Extract last part of binding path
            var parts = binding.Split('/');
            var lastPart = parts.Last();

            // Remove namespace prefix if present
            if (lastPart.Contains(':'))
                lastPart = lastPart.Split(':').Last();

            return lastPart;
        }

        private void AddSimplifiedAnalysisMessages(FormAnalysisResult result, InfoPathFormDefinition formDef)
        {
            // Count controls (excluding labels and merged)
            var controlCount = formDef.Views
                .SelectMany(v => v.Controls)
                .Count(c => !c.IsMergedIntoParent && c.Type != "Label");

            // Count repeating structures
            var repeatingSections = formDef.Views
                .SelectMany(v => v.Controls)
                .Where(c => c.IsInRepeatingSection)
                .Select(c => c.RepeatingSectionName)
                .Distinct()
                .Count();

            var repeatingTables = formDef.Views
                .SelectMany(v => v.Controls)
                .Count(c => c.Type == "RepeatingTable");

            var totalRepeating = repeatingSections + repeatingTables;

            // Basic success message
            result.Messages.Add(new AnalysisMessage
            {
                Severity = MessageSeverity.Info,
                Message = $"Successfully analyzed form: {formDef.FormName}",
                Details = $"Found {formDef.Views.Count} view(s) with {controlCount} controls",
                Source = "Analysis"
            });

            // Repeating structures message
            if (totalRepeating > 0)
            {
                result.Messages.Add(new AnalysisMessage
                {
                    Severity = MessageSeverity.Info,
                    Message = $"Form contains {totalRepeating} repeating structure(s)",
                    Details = $"Sections: {repeatingSections}, Tables: {repeatingTables}. " +
                             "These will be created as separate related tables in SQL.",
                    Source = "Structure"
                });
            }

            // Data columns message
            if (formDef.Data.Count > 0)
            {
                var repeatingCols = formDef.Data.Count(d => d.IsRepeating);
                var dropdownCols = formDef.Data.Count(d => d.HasConstraints);

                result.Messages.Add(new AnalysisMessage
                {
                    Severity = MessageSeverity.Info,
                    Message = $"Data structure: {formDef.Data.Count} columns",
                    Details = $"Standard: {formDef.Data.Count - repeatingCols}, " +
                             $"Repeating: {repeatingCols}, " +
                             $"Dropdowns: {dropdownCols}",
                    Source = "Data"
                });
            }

            // Add metadata
            result.Metadata["ControlCount"] = controlCount;
            result.Metadata["RepeatingSections"] = repeatingSections;
            result.Metadata["RepeatingTables"] = repeatingTables;
            result.Metadata["DataColumns"] = formDef.Data.Count;
            result.Metadata["Views"] = formDef.Views.Select(v => v.ViewName).ToList();
        }



        /// <summary>
        /// Ensures Data columns are unique and properly structured
        /// </summary>
        private void EnsureUniqueDataColumns(InfoPathFormDefinition formDef)
        {
            if (formDef.Data == null || formDef.Data.Count == 0)
            {
                // If Data is empty, try to build it from Views
                formDef.Data = BuildDataColumnsFromViews(formDef);
            }
            else
            {
                // Ensure uniqueness by column name
                var uniqueColumns = new Dictionary<string, DataColumn>();

                foreach (var column in formDef.Data)
                {
                    var key = column.ColumnName;

                    // If repeating, include the section in the key to maintain uniqueness
                    if (column.IsRepeating && !string.IsNullOrEmpty(column.RepeatingSection))
                    {
                        key = $"{column.RepeatingSection}.{column.ColumnName}";
                    }

                    if (!uniqueColumns.ContainsKey(key))
                    {
                        uniqueColumns[key] = column;
                    }
                    else
                    {
                        // Merge properties if needed
                        var existing = uniqueColumns[key];

                        // Preserve the most complete information
                        if (string.IsNullOrEmpty(existing.DisplayName) && !string.IsNullOrEmpty(column.DisplayName))
                        {
                            existing.DisplayName = column.DisplayName;
                        }

                        if (string.IsNullOrEmpty(existing.Type) && !string.IsNullOrEmpty(column.Type))
                        {
                            existing.Type = column.Type;
                        }

                        // Merge ValidValues if they exist
                        if (column.ValidValues != null && column.ValidValues.Count > 0)
                        {
                            if (existing.ValidValues == null)
                            {
                                existing.ValidValues = column.ValidValues;
                            }
                            else
                            {
                                // Merge unique values
                                foreach (var val in column.ValidValues)
                                {
                                    if (!existing.ValidValues.Any(v => v.Value == val.Value))
                                    {
                                        existing.ValidValues.Add(val);
                                    }
                                }
                            }
                        }
                    }
                }

                formDef.Data = uniqueColumns.Values.ToList();
            }
        }
        /// <summary>
        /// Builds Data columns from Views if Data is empty
        /// </summary>
        private List<DataColumn> BuildDataColumnsFromViews(InfoPathFormDefinition formDef)
        {
            var dataColumns = new Dictionary<string, DataColumn>();

            foreach (var view in formDef.Views)
            {
                foreach (var control in view.Controls)
                {
                    // Skip labels and non-data controls
                    if (control.Type == "Label" || control.Type == "Button" ||
                        control.Type == "Section" || string.IsNullOrEmpty(control.Binding))
                    {
                        continue;
                    }

                    var columnName = !string.IsNullOrEmpty(control.Binding) ? control.Binding : control.Name;

                    if (string.IsNullOrEmpty(columnName))
                        continue;

                    // Create a unique key for the column
                    var key = columnName;
                    if (control.IsInRepeatingSection && !string.IsNullOrEmpty(control.RepeatingSectionName))
                    {
                        key = $"{control.RepeatingSectionName}.{columnName}";
                    }

                    if (!dataColumns.ContainsKey(key))
                    {
                        var dataColumn = new DataColumn
                        {
                            ColumnName = columnName,
                            DisplayName = !string.IsNullOrEmpty(control.Label) ? control.Label : control.Name,
                            Type = control.Type,
                            IsRepeating = control.IsInRepeatingSection,
                            RepeatingSection = control.RepeatingSectionName,  // FIX: Use RepeatingSectionName directly
                            RepeatingSectionPath = control.RepeatingSectionBinding,
                            IsConditional = CheckIfConditional(control)
                        };

                        // Convert DataOptions to ValidValues
                        if (control.HasStaticData && control.DataOptions != null && control.DataOptions.Count > 0)
                        {
                            dataColumn.ValidValues = new List<DataOption>(control.DataOptions);
                        }

                        dataColumns[key] = dataColumn;
                    }
                }
            }

            return dataColumns.Values.ToList();
        }

        private bool CheckIfConditional(ControlDefinition control)
        {
            if (control.Properties != null)
            {
                // Check various possible property names for conditional status
                if (control.Properties.ContainsKey("IsConditional") &&
                    bool.TryParse(control.Properties["IsConditional"], out bool isConditional))
                {
                    return isConditional;
                }

                if (control.Properties.ContainsKey("HasConditionalFormatting") &&
                    bool.TryParse(control.Properties["HasConditionalFormatting"], out bool hasConditional))
                {
                    return hasConditional;
                }

                if (control.Properties.ContainsKey("ConditionalVisibility"))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Process dropdown values from controls and ensure they're in Data columns
        /// </summary>
        private void ProcessDropdownValues(InfoPathFormDefinition formDef)
        {
            // Map dropdown values from controls to data columns
            foreach (var view in formDef.Views)
            {
                foreach (var control in view.Controls)
                {
                    if ((control.Type == "DropDown" || control.Type == "ComboBox" || control.Type == "ListBox")
                        && control.HasStaticData && control.DataOptions != null && control.DataOptions.Count > 0)
                    {
                        // Find the corresponding data column
                        var bindingName = !string.IsNullOrEmpty(control.Binding) ? control.Binding : control.Name;

                        var dataColumn = formDef.Data.FirstOrDefault(d =>
                            d.ColumnName == bindingName ||
                            d.ColumnName == control.Name);

                        if (dataColumn != null)
                        {
                            // Ensure ValidValues are set
                            if (dataColumn.ValidValues == null || dataColumn.ValidValues.Count == 0)
                            {
                                dataColumn.ValidValues = new List<DataOption>(control.DataOptions);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Adds analysis messages based on the form structure
        /// </summary>
        private void AddAnalysisMessages(FormAnalysisResult result, InfoPathFormDefinition formDef)
        {
            // Basic success message
            result.Messages.Add(new AnalysisMessage
            {
                Severity = MessageSeverity.Info,
                Message = $"Successfully analyzed form: {formDef.FormName}",
                Details = $"Found {formDef.Views.Count} view(s) with {formDef.Metadata.TotalControls} controls",
                Source = "ViewAnalysis"
            });

            // Check for complex repeating structures
            if (formDef.Metadata.RepeatingSectionCount > 5)
            {
                result.Messages.Add(new AnalysisMessage
                {
                    Severity = MessageSeverity.Warning,
                    Message = "Form contains many repeating sections",
                    Details = $"Found {formDef.Metadata.RepeatingSectionCount} repeating sections. This may impact SQL generation complexity and performance.",
                    Source = "StructureAnalysis"
                });
            }
            else if (formDef.Metadata.RepeatingSectionCount > 0)
            {
                result.Messages.Add(new AnalysisMessage
                {
                    Severity = MessageSeverity.Info,
                    Message = $"Form contains {formDef.Metadata.RepeatingSectionCount} repeating section(s)",
                    Details = "Repeating sections will be created as separate related tables in SQL",
                    Source = "StructureAnalysis"
                });
            }

            // Check for dynamic/conditional sections
            if (formDef.DynamicSections.Count > 0)
            {
                result.Messages.Add(new AnalysisMessage
                {
                    Severity = MessageSeverity.Info,
                    Message = $"Form contains {formDef.DynamicSections.Count} conditional section(s)",
                    Details = "Conditional logic will need to be handled in the target platform",
                    Source = "ConditionalLogicAnalysis"
                });

                // List the conditional fields
                if (formDef.Metadata.ConditionalFields.Any())
                {
                    result.Messages.Add(new AnalysisMessage
                    {
                        Severity = MessageSeverity.Info,
                        Message = "Conditional fields detected",
                        Details = $"Fields with conditional logic: {string.Join(", ", formDef.Metadata.ConditionalFields)}",
                        Source = "ConditionalLogicAnalysis"
                    });
                }
            }

            // Check for dropdowns with values
            var dropdownCount = formDef.Data.Count(d => d.HasConstraints && d.ValidValues != null && d.ValidValues.Count > 0);
            if (dropdownCount > 0)
            {
                result.Messages.Add(new AnalysisMessage
                {
                    Severity = MessageSeverity.Info,
                    Message = $"Found {dropdownCount} dropdown field(s) with predefined values",
                    Details = "Lookup tables will be created for dropdown values",
                    Source = "DataAnalysis"
                });
            }

            // Check for complex control types
            AnalyzeComplexControls(result, formDef);

            // Check for potential migration issues
            CheckMigrationIssues(result, formDef);

            // Add data structure summary
            if (formDef.Data.Count > 0)
            {
                var repeatingCount = formDef.Data.Count(d => d.IsRepeating);
                var conditionalCount = formDef.Data.Count(d => d.IsConditional);

                result.Messages.Add(new AnalysisMessage
                {
                    Severity = MessageSeverity.Info,
                    Message = $"Data structure analyzed: {formDef.Data.Count} unique columns",
                    Details = $"Standard fields: {formDef.Data.Count - repeatingCount}, " +
                             $"Repeating fields: {repeatingCount}, " +
                             $"Conditional fields: {conditionalCount}",
                    Source = "DataAnalysis"
                });
            }
        }

        /// <summary>
        /// Analyzes complex controls that might need special handling
        /// </summary>
        private void AnalyzeComplexControls(FormAnalysisResult result, InfoPathFormDefinition formDef)
        {
            var complexControlTypes = new Dictionary<string, string>
            {
                { "PeoplePicker", "People Picker controls will need user lookup functionality" },
                { "FileAttachment", "File attachments will require binary storage in database" },
                { "SharePointFileAttachment", "SharePoint attachments may need special handling" },
                { "ActiveX", "ActiveX controls may not be supported in modern platforms" },
                { "InlinePicture", "Inline pictures will need binary storage" },
                { "SignatureLine", "Digital signatures will need special security handling" }
            };

            var foundComplexControls = new Dictionary<string, List<string>>();

            foreach (var view in formDef.Views)
            {
                foreach (var control in view.Controls)
                {
                    foreach (var complexType in complexControlTypes.Keys)
                    {
                        if (control.Type.Contains(complexType))
                        {
                            if (!foundComplexControls.ContainsKey(complexType))
                            {
                                foundComplexControls[complexType] = new List<string>();
                            }

                            var controlIdentifier = !string.IsNullOrEmpty(control.Label)
                                ? control.Label
                                : control.Name;

                            if (!string.IsNullOrEmpty(controlIdentifier))
                            {
                                foundComplexControls[complexType].Add(controlIdentifier);
                            }
                        }
                    }
                }
            }

            // Report findings
            foreach (var kvp in foundComplexControls)
            {
                result.Messages.Add(new AnalysisMessage
                {
                    Severity = MessageSeverity.Warning,
                    Message = $"Complex control type detected: {kvp.Key}",
                    Details = $"Controls: {string.Join(", ", kvp.Value.Take(5))}. " +
                             $"{complexControlTypes[kvp.Key]}",
                    Source = "ControlAnalysis"
                });
            }
        }

        /// <summary>
        /// Checks for potential issues during migration
        /// </summary>
        private void CheckMigrationIssues(FormAnalysisResult result, InfoPathFormDefinition formDef)
        {
            // Check for very large forms
            if (formDef.Metadata.TotalControls > 100)
            {
                result.Messages.Add(new AnalysisMessage
                {
                    Severity = MessageSeverity.Warning,
                    Message = "Large form detected",
                    Details = $"This form has {formDef.Metadata.TotalControls} controls. Consider breaking it into smaller forms for better performance.",
                    Source = "MigrationAnalysis"
                });
            }

            // Check for deeply nested repeating sections
            var nestedRepeating = 0;
            foreach (var view in formDef.Views)
            {
                foreach (var control in view.Controls)
                {
                    if (control.Properties.ContainsKey("ParentRepeatingSections"))
                    {
                        var parents = control.Properties["ParentRepeatingSections"].Split('|');
                        if (parents.Length > 1)
                        {
                            nestedRepeating++;
                        }
                    }
                }
            }

            if (nestedRepeating > 0)
            {
                result.Messages.Add(new AnalysisMessage
                {
                    Severity = MessageSeverity.Warning,
                    Message = "Nested repeating sections detected",
                    Details = "Nested repeating sections increase complexity and may need special handling in the target platform",
                    Source = "MigrationAnalysis"
                });
            }

            // Check for forms with no data columns
            if (formDef.Data.Count == 0)
            {
                result.Messages.Add(new AnalysisMessage
                {
                    Severity = MessageSeverity.Warning,
                    Message = "No data columns detected",
                    Details = "This form may be display-only or the data structure could not be determined",
                    Source = "DataAnalysis"
                });
            }

            // Check for business rules
            if (formDef.Rules.Any())
            {
                result.Messages.Add(new AnalysisMessage
                {
                    Severity = MessageSeverity.Info,
                    Message = $"Form contains {formDef.Rules.Count} business rule(s)",
                    Details = "Business rules will need to be recreated in the target platform",
                    Source = "RulesAnalysis"
                });
            }
        }

        /// <summary>
        /// Adds metadata about the file and analysis
        /// </summary>
        private void AddMetadata(FormAnalysisResult result, string filePath, InfoPathFormDefinition formDef)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                result.Metadata["FileSize"] = fileInfo.Length;
                result.Metadata["FileSizeFormatted"] = FormatFileSize(fileInfo.Length);
                result.Metadata["FileCreated"] = fileInfo.CreationTime;
                result.Metadata["FileModified"] = fileInfo.LastWriteTime;
                result.Metadata["FormName"] = formDef.FormName;
                result.Metadata["FileName"] = formDef.FileName;
            }
            catch
            {
                // Ignore file metadata errors
            }

            // Add form structure metadata
            result.Metadata["ViewCount"] = formDef.Views.Count;
            result.Metadata["TotalControls"] = formDef.Metadata.TotalControls;
            result.Metadata["TotalSections"] = formDef.Metadata.TotalSections;
            result.Metadata["RepeatingSections"] = formDef.Metadata.RepeatingSectionCount;
            result.Metadata["DynamicSections"] = formDef.Metadata.DynamicSectionCount;
            result.Metadata["UniqueDataColumns"] = formDef.Data.Count;
            result.Metadata["DropdownFields"] = formDef.Data.Count(d => d.HasConstraints && d.ValidValues != null && d.ValidValues.Count > 0);

            // Add control type breakdown
            var controlTypes = new Dictionary<string, int>();
            foreach (var view in formDef.Views)
            {
                foreach (var control in view.Controls.Where(c => !c.IsMergedIntoParent))
                {
                    if (!controlTypes.ContainsKey(control.Type))
                    {
                        controlTypes[control.Type] = 0;
                    }
                    controlTypes[control.Type]++;
                }
            }
            result.Metadata["ControlTypes"] = controlTypes;

            // Add view names
            result.Metadata["ViewNames"] = formDef.Views.Select(v => v.ViewName).ToList();
        }

        /// <summary>
        /// Formats file size to human-readable format
        /// </summary>
        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }
    }



/// <summary>
/// Placeholder for future InfoPath 2010 analyzer
/// </summary>
public class InfoPath2010Analyzer : IFormAnalyzer
    {
        public string AnalyzerName => "InfoPath 2010 Analyzer";
        public string SupportedVersion => "2010";
        public string[] SupportedFileExtensions => new[] { ".xsn" };

        public bool CanAnalyze(string filePath)
        {
            // TODO: Implement when ready
            // For now, return false to indicate this analyzer is not yet implemented
            return false;
        }

        public Task<FormAnalysisResult> AnalyzeFormAsync(string filePath)
        {
            throw new NotImplementedException("InfoPath 2010 analyzer is not yet implemented. Please use InfoPath 2013 analyzer.");
        }
    }

    /// <summary>
    /// Placeholder for future InfoPath 2007 analyzer
    /// </summary>
    public class InfoPath2007Analyzer : IFormAnalyzer
    {
        public string AnalyzerName => "InfoPath 2007 Analyzer";
        public string SupportedVersion => "2007";
        public string[] SupportedFileExtensions => new[] { ".xsn" };

        public bool CanAnalyze(string filePath)
        {
            // TODO: Implement when ready
            return false;
        }

        public Task<FormAnalysisResult> AnalyzeFormAsync(string filePath)
        {
            throw new NotImplementedException("InfoPath 2007 analyzer is not yet implemented. Please use InfoPath 2013 analyzer.");
        }
    }

    /// <summary>
    /// Placeholder for future Nintex Forms analyzer
    /// </summary>
    public class NintexFormsAnalyzer : IFormAnalyzer
    {
        public string AnalyzerName => "Nintex Forms Analyzer";
        public string SupportedVersion => "Latest";
        public string[] SupportedFileExtensions => new[] { ".nfp", ".xml" };

        public bool CanAnalyze(string filePath)
        {
            // TODO: Implement when ready
            // Check for Nintex-specific file extensions
            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            // For now, return false as it's not implemented
            return false;
        }

        public Task<FormAnalysisResult> AnalyzeFormAsync(string filePath)
        {
            throw new NotImplementedException("Nintex Forms analyzer is not yet implemented.");
        }
    }
}