using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FormGenerator.Core.Interfaces;
using FormGenerator.Core.Models;
using FormGenerator.Analyzers.Infopath;

namespace FormGenerator.Services
{
    /// <summary>
    /// Enhanced SQL generation service with proper repeating section support
    /// </summary>
    public class SqlGeneratorService : ISqlGenerator
    {
        public SqlDialect Dialect { get; set; } = SqlDialect.SqlServer;

        public async Task<SqlGenerationResult> GenerateFromAnalysisAsync(FormAnalysisResult analysis)
        {
            return await GenerateFromAnalysisAsync(analysis, null);
        }

        public async Task<SqlGenerationResult> GenerateFromAnalysisAsync(FormAnalysisResult analysis, TableStructureType? structureType)
        {
            var result = new SqlGenerationResult
            {
                Dialect = Dialect,
                GeneratedDate = DateTime.Now
            };

            try
            {
                if (analysis?.FormDefinition == null)
                {
                    throw new ArgumentException("Invalid analysis result");
                }

                await Task.Run(() =>
                {
                    var formDef = analysis.FormDefinition;
                    var scripts = new List<SqlScript>();

                    // Analyze the form structure to identify repeating sections
                    var repeatingSectionAnalysis = AnalyzeRepeatingSections(formDef);

                    // Apply structure type if specified
                    if (structureType.HasValue)
                    {
                        // You can add logic here to handle different structure types
                        // For now, we'll use the enhanced repeating section approach
                    }

                    // Generate drop statements first (for re-deployment)
                    scripts.Add(GenerateDropStatements(formDef, repeatingSectionAnalysis));

                    // Generate main form table
                    scripts.Add(GenerateMainTable(formDef, repeatingSectionAnalysis));

                    // Generate lookup tables for dropdowns
                    scripts.AddRange(GenerateLookupTables(formDef));

                    // Generate tables for repeating sections - ENHANCED
                    scripts.AddRange(GenerateRepeatingTables(formDef, repeatingSectionAnalysis));

                    // Generate stored procedures
                    scripts.AddRange(GenerateStoredProcedures(formDef, repeatingSectionAnalysis));

                    // Generate views
                    scripts.Add(GenerateMainView(formDef, repeatingSectionAnalysis));

                    // Generate indexes
                    scripts.Add(GenerateIndexes(formDef, repeatingSectionAnalysis));

                    // Order scripts by execution order
                    result.Scripts = scripts.OrderBy(s => s.ExecutionOrder).ToList();
                });

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Enhanced repeating section analysis - focuses ONLY on true repeating sections from Data
        /// </summary>
        private RepeatingSectionAnalysis AnalyzeRepeatingSections(InfoPathFormDefinition formDef)
        {
            var analysis = new RepeatingSectionAnalysis();

            // PRIMARY APPROACH: Use Data columns to identify repeating sections
            // This is the most reliable source as it represents the actual data structure
            var repeatingSectionNames = formDef.Data
                .Where(d => d.IsRepeating && !string.IsNullOrEmpty(d.RepeatingSection))
                .Select(d => d.RepeatingSection)
                .Distinct()
                .ToList();

            foreach (var sectionName in repeatingSectionNames)
            {
                analysis.RepeatingSections[sectionName] = new RepeatingSectionInfo
                {
                    Name = sectionName,
                    SectionType = "repeating",
                    Controls = new List<ControlDefinition>(),
                    ChildSections = new List<string>()
                };

                // Find ALL controls for this repeating section from the Data (including conditional ones)
                var sectionColumns = formDef.Data
                    .Where(d => d.IsRepeating && d.RepeatingSection == sectionName)
                    .ToList();

                // Map data columns to actual controls
                foreach (var dataColumn in sectionColumns)
                {
                    var matchingControl = formDef.Views
                        .SelectMany(v => v.Controls)
                        .FirstOrDefault(c => c.Name == dataColumn.ColumnName ||
                                           c.Binding == dataColumn.ColumnName ||
                                           c.Label == dataColumn.DisplayName);

                    if (matchingControl != null &&
                        !analysis.RepeatingSections[sectionName].Controls.Any(existing => existing.Name == matchingControl.Name))
                    {
                        analysis.RepeatingSections[sectionName].Controls.Add(matchingControl);
                    }
                }
            }

            // SECONDARY APPROACH: Use controls with IsInRepeatingSection property as backup
            foreach (var view in formDef.Views)
            {
                foreach (var control in view.Controls)
                {
                    if (control.IsInRepeatingSection && !string.IsNullOrEmpty(control.RepeatingSectionName))
                    {
                        var sectionName = control.RepeatingSectionName;

                        if (!analysis.RepeatingSections.ContainsKey(sectionName))
                        {
                            analysis.RepeatingSections[sectionName] = new RepeatingSectionInfo
                            {
                                Name = sectionName,
                                SectionType = "repeating",
                                Controls = new List<ControlDefinition>(),
                                ChildSections = new List<string>()
                            };
                        }

                        // Add control if it's not a label and not already added
                        if (control.Type != "Label" &&
                            !analysis.RepeatingSections[sectionName].Controls.Any(existing => existing.Name == control.Name))
                        {
                            analysis.RepeatingSections[sectionName].Controls.Add(control);
                        }
                    }
                }
            }

            // Identify columns for non-repeating sections (main table)
            analysis.MainTableColumns = formDef.Views
                .SelectMany(v => v.Controls)
                .Where(c => !c.IsInRepeatingSection &&
                           c.Type != "Label" && c.Type != "Section" &&
                           c.Type != "RepeatingSection" && c.Type != "RepeatingTable")
                .GroupBy(c => c.Name)
                .Select(g => g.First())
                .ToList();

            // Also add from Data columns that are not repeating
            var mainTableDataColumns = formDef.Data
                .Where(d => !d.IsRepeating)
                .ToList();

            foreach (var dataColumn in mainTableDataColumns)
            {
                var matchingControl = formDef.Views
                    .SelectMany(v => v.Controls)
                    .FirstOrDefault(c => c.Name == dataColumn.ColumnName ||
                                       c.Binding == dataColumn.ColumnName ||
                                       c.Label == dataColumn.DisplayName);

                if (matchingControl != null &&
                    !analysis.MainTableColumns.Any(existing => existing.Name == matchingControl.Name) &&
                    matchingControl.Type != "Label")
                {
                    analysis.MainTableColumns.Add(matchingControl);
                }
            }

            return analysis;
        }

        private SqlScript GenerateDropStatements(InfoPathFormDefinition formDef, RepeatingSectionAnalysis analysis)
        {
            var tableName = SanitizeTableName(formDef.FormName);
            var sb = new StringBuilder();

            sb.AppendLine("-- ===============================================");
            sb.AppendLine("-- Drop existing objects if they exist");
            sb.AppendLine("-- ===============================================");
            sb.AppendLine();

            // Drop view
            sb.AppendLine($"IF OBJECT_ID('dbo.vw_{tableName}_Summary', 'V') IS NOT NULL");
            sb.AppendLine($"    DROP VIEW [dbo].[vw_{tableName}_Summary];");
            sb.AppendLine();

            // Drop stored procedures
            var procedures = new[] { "Insert", "Update", "Get", "Delete", "List" };
            foreach (var proc in procedures)
            {
                sb.AppendLine($"IF OBJECT_ID('dbo.sp_{tableName}_{proc}', 'P') IS NOT NULL");
                sb.AppendLine($"    DROP PROCEDURE [dbo].[sp_{tableName}_{proc}];");
                sb.AppendLine();
            }

            // Drop repeating section tables ONLY (not conditional sections)
            foreach (var section in analysis.RepeatingSections.Values)
            {
                var sectionTableName = SanitizeTableName(section.Name);
                var fullTableName = $"{tableName}_{sectionTableName}";
                sb.AppendLine($"IF OBJECT_ID('dbo.{fullTableName}', 'U') IS NOT NULL");
                sb.AppendLine($"    DROP TABLE [dbo].[{fullTableName}];");
                sb.AppendLine();
            }

            // Drop lookup tables
            var controlsWithLookups = formDef.Views
                .SelectMany(v => v.Controls)
                .Where(c => c.HasStaticData && c.DataOptions != null && c.DataOptions.Count > 5)
                .GroupBy(c => c.Name)
                .ToList();

            foreach (var controlGroup in controlsWithLookups)
            {
                var control = controlGroup.First();
                var lookupTableName = $"{tableName}_{SanitizeTableName(control.Name)}_Lookup";
                sb.AppendLine($"IF OBJECT_ID('dbo.{lookupTableName}', 'U') IS NOT NULL");
                sb.AppendLine($"    DROP TABLE [dbo].[{lookupTableName}];");
                sb.AppendLine();
            }

            // Drop main table last
            sb.AppendLine($"IF OBJECT_ID('dbo.{tableName}', 'U') IS NOT NULL");
            sb.AppendLine($"    DROP TABLE [dbo].[{tableName}];");
            sb.AppendLine();

            return new SqlScript
            {
                Name = $"Drop_{tableName}_Objects",
                Type = ScriptType.Table,
                Content = sb.ToString(),
                ExecutionOrder = 0,
                Description = "Drop existing objects for clean deployment"
            };
        }

        private SqlScript GenerateMainTable(InfoPathFormDefinition formDef, RepeatingSectionAnalysis analysis)
        {
            var tableName = SanitizeTableName(formDef.FormName);
            var sb = new StringBuilder();

            if (Dialect == SqlDialect.SqlServer)
            {
                sb.AppendLine("-- ===============================================");
                sb.AppendLine($"-- Main table for {tableName}");
                sb.AppendLine($"-- Main table controls: {analysis.MainTableColumns.Count}");
                sb.AppendLine($"-- Repeating sections: {analysis.RepeatingSections.Count}");
                foreach (var section in analysis.RepeatingSections)
                {
                    sb.AppendLine($"--   {section.Key}: {section.Value.Controls.Count} controls");
                }
                sb.AppendLine("-- ===============================================");
                sb.AppendLine();

                sb.AppendLine($"CREATE TABLE [dbo].[{tableName}] (");
                sb.AppendLine("    -- Primary Keys");
                sb.AppendLine("    [Id] INT IDENTITY(1,1) NOT NULL,");
                sb.AppendLine("    [FormId] UNIQUEIDENTIFIER DEFAULT NEWID() NOT NULL,");
                sb.AppendLine();
                sb.AppendLine("    -- Audit Fields");
                sb.AppendLine("    [CreatedDate] DATETIME2(3) DEFAULT GETDATE() NOT NULL,");
                sb.AppendLine("    [ModifiedDate] DATETIME2(3) DEFAULT GETDATE() NOT NULL,");
                sb.AppendLine("    [CreatedBy] NVARCHAR(255) NULL,");
                sb.AppendLine("    [ModifiedBy] NVARCHAR(255) NULL,");
                sb.AppendLine("    [Status] NVARCHAR(50) DEFAULT 'Draft' NOT NULL,");
                sb.AppendLine("    [Version] INT DEFAULT 1 NOT NULL,");
                sb.AppendLine();

                if (analysis.MainTableColumns.Any())
                {
                    sb.AppendLine("    -- Form Fields");

                    // Track columns that need CHECK constraints
                    var columnsWithConstraints = new List<(string columnName, List<string> validValues)>();

                    // Add columns for non-repeating fields only
                    var processedColumns = new HashSet<string>();
                    foreach (var control in analysis.MainTableColumns.OrderBy(c => c.Name))
                    {
                        var columnName = SanitizeColumnName(control.Name);
                        if (!processedColumns.Contains(columnName))
                        {
                            processedColumns.Add(columnName);
                            var sqlType = GetSqlType(control.Type);
                            var nullable = " NULL";
                            var defaultValue = GetDefaultValueClause(control);
                            var comment = string.IsNullOrEmpty(control.Label) ? control.Name : control.Label;

                            sb.AppendLine($"    [{columnName}] {sqlType}{nullable}{defaultValue}, -- {comment}");

                            // Track if this column needs constraints
                            if (control.HasStaticData && control.DataOptions != null && control.DataOptions.Any())
                            {
                                columnsWithConstraints.Add((columnName,
                                    control.DataOptions.Select(v => v.Value).ToList()));
                            }
                        }
                    }

                    sb.AppendLine();
                    sb.AppendLine("    -- Constraints");
                    sb.AppendLine($"    CONSTRAINT [PK_{tableName}] PRIMARY KEY CLUSTERED ([Id] ASC),");
                    sb.AppendLine($"    CONSTRAINT [UQ_{tableName}_FormId] UNIQUE ([FormId])");
                    sb.AppendLine(");");
                    sb.AppendLine();

                    // Add CHECK constraints for columns with valid values
                    foreach (var (columnName, validValues) in columnsWithConstraints)
                    {
                        sb.AppendLine($"-- Add CHECK constraint for {columnName}");
                        var constraintName = $"CK_{tableName}_{columnName}";
                        var valuesList = string.Join(", ", validValues.Select(v => $"N'{v.Replace("'", "''")}'"));
                        sb.AppendLine($"ALTER TABLE [dbo].[{tableName}]");
                        sb.AppendLine($"ADD CONSTRAINT [{constraintName}]");
                        sb.AppendLine($"CHECK ([{columnName}] IN ({valuesList}) OR [{columnName}] IS NULL);");
                        sb.AppendLine();
                    }
                }
                else
                {
                    sb.AppendLine("    -- No main form fields found");
                    sb.AppendLine("    [PlaceholderField] NVARCHAR(MAX) NULL,");
                    sb.AppendLine();
                    sb.AppendLine("    -- Constraints");
                    sb.AppendLine($"    CONSTRAINT [PK_{tableName}] PRIMARY KEY CLUSTERED ([Id] ASC),");
                    sb.AppendLine($"    CONSTRAINT [UQ_{tableName}_FormId] UNIQUE ([FormId])");
                    sb.AppendLine(");");
                    sb.AppendLine();
                }

                // Add table extended property for description (check if exists first)
                sb.AppendLine("-- Add table description");
                sb.AppendLine($"IF NOT EXISTS (");
                sb.AppendLine($"    SELECT 1 FROM sys.extended_properties ep");
                sb.AppendLine($"    INNER JOIN sys.tables t ON ep.major_id = t.object_id");
                sb.AppendLine($"    WHERE t.name = N'{tableName}' AND ep.name = N'MS_Description' AND ep.minor_id = 0");
                sb.AppendLine($")");
                sb.AppendLine($"BEGIN");
                sb.AppendLine($"    EXEC sp_addextendedproperty ");
                sb.AppendLine($"        @name = N'MS_Description',");
                sb.AppendLine($"                                @value = N'Main table for {formDef.FormName} form data. Contains {analysis.MainTableColumns.Count} main fields.',");
                sb.AppendLine($"        @level0type = N'SCHEMA', @level0name = N'dbo',");
                sb.AppendLine($"        @level1type = N'TABLE', @level1name = N'{tableName}';");
                sb.AppendLine($"END");
                sb.AppendLine();
            }

            return new SqlScript
            {
                Name = $"Create_{tableName}_Table",
                Type = ScriptType.Table,
                Content = sb.ToString(),
                ExecutionOrder = 1,
                Description = $"Main table for form {tableName} with {analysis.MainTableColumns.Count} fields"
            };
        }

        private List<SqlScript> GenerateRepeatingTables(InfoPathFormDefinition formDef, RepeatingSectionAnalysis analysis)
        {
            var scripts = new List<SqlScript>();
            var mainTableName = SanitizeTableName(formDef.FormName);
            int order = 2;

            foreach (var sectionKvp in analysis.RepeatingSections)
            {
                var section = sectionKvp.Value;
                var sectionTableName = SanitizeTableName(section.Name);
                var fullTableName = $"{mainTableName}_{sectionTableName}";
                var sb = new StringBuilder();

                if (Dialect == SqlDialect.SqlServer)
                {
                    sb.AppendLine("-- ===============================================");
                    sb.AppendLine($"-- Repeating section table: {section.Name}");
                    sb.AppendLine($"-- Controls in section: {section.Controls.Count}");
                    if (section.ChildSections.Any())
                    {
                        sb.AppendLine($"-- Child sections: {string.Join(", ", section.ChildSections)}");
                    }
                    if (section.Controls.Any())
                    {
                        sb.AppendLine($"-- Control types: {string.Join(", ", section.Controls.Select(c => c.Type).Distinct())}");
                    }
                    sb.AppendLine("-- ===============================================");
                    sb.AppendLine();

                    sb.AppendLine($"CREATE TABLE [dbo].[{fullTableName}] (");
                    sb.AppendLine("    -- Primary Key");
                    sb.AppendLine("    [Id] INT IDENTITY(1,1) NOT NULL,");
                    sb.AppendLine("    [ParentFormId] UNIQUEIDENTIFIER NOT NULL,");
                    sb.AppendLine("    [ItemOrder] INT NOT NULL DEFAULT 0,");
                    sb.AppendLine();
                    sb.AppendLine("    -- Audit Fields");
                    sb.AppendLine("    [CreatedDate] DATETIME2(3) DEFAULT GETDATE() NOT NULL,");
                    sb.AppendLine("    [ModifiedDate] DATETIME2(3) DEFAULT GETDATE() NOT NULL,");
                    sb.AppendLine();

                    if (section.Controls.Any())
                    {
                        sb.AppendLine("    -- Section Fields");

                        // Add columns for each control in this repeating section
                        var processedControlNames = new HashSet<string>();
                        foreach (var control in section.Controls.OrderBy(c => c.Name))
                        {
                            var sanitizedName = SanitizeColumnName(control.Name);

                            // Avoid duplicate columns
                            if (!processedControlNames.Contains(sanitizedName))
                            {
                                processedControlNames.Add(sanitizedName);
                                var sqlType = GetSqlType(control.Type);
                                var nullable = " NULL";
                                var defaultValue = GetDefaultValueClause(control);
                                var comment = string.IsNullOrEmpty(control.Label) ? control.Name : control.Label;

                                sb.AppendLine($"    [{sanitizedName}] {sqlType}{nullable}{defaultValue}, -- {comment} ({control.Type})");
                            }
                        }
                    }
                    else
                    {
                        sb.AppendLine("    -- No controls found in this section");
                        sb.AppendLine("    [PlaceholderData] NVARCHAR(MAX) NULL, -- Placeholder for section data");
                    }

                    sb.AppendLine();
                    sb.AppendLine("    -- Constraints");
                    sb.AppendLine($"    CONSTRAINT [PK_{fullTableName}] PRIMARY KEY CLUSTERED ([Id] ASC),");
                    sb.AppendLine($"    CONSTRAINT [FK_{fullTableName}_Parent] FOREIGN KEY ([ParentFormId])");
                    sb.AppendLine($"        REFERENCES [dbo].[{mainTableName}] ([FormId])");
                    sb.AppendLine($"        ON DELETE CASCADE");
                    sb.AppendLine(");");
                    sb.AppendLine();

                    // Add indexes
                    sb.AppendLine("-- Indexes for performance");
                    sb.AppendLine($"CREATE NONCLUSTERED INDEX [IX_{fullTableName}_ParentFormId]");
                    sb.AppendLine($"ON [dbo].[{fullTableName}] ([ParentFormId], [ItemOrder]);");
                    sb.AppendLine();

                    // Add table description (check if exists first)
                    sb.AppendLine("-- Add table description");
                    sb.AppendLine($"IF NOT EXISTS (");
                    sb.AppendLine($"    SELECT 1 FROM sys.extended_properties ep");
                    sb.AppendLine($"    INNER JOIN sys.tables t ON ep.major_id = t.object_id");
                    sb.AppendLine($"    WHERE t.name = N'{fullTableName}' AND ep.name = N'MS_Description' AND ep.minor_id = 0");
                    sb.AppendLine($")");
                    sb.AppendLine($"BEGIN");
                    sb.AppendLine($"    EXEC sp_addextendedproperty ");
                    sb.AppendLine($"        @name = N'MS_Description',");
                    sb.AppendLine($"        @value = N'Repeating section table for {section.Name} containing {section.Controls.Count} controls',");
                    sb.AppendLine($"        @level0type = N'SCHEMA', @level0name = N'dbo',");
                    sb.AppendLine($"        @level1type = N'TABLE', @level1name = N'{fullTableName}';");
                    sb.AppendLine($"END");
                    sb.AppendLine();

                    // Add column descriptions (only for unique columns to avoid duplicates)
                    var processedDescriptions = new HashSet<string>();
                    foreach (var control in section.Controls)
                    {
                        var sanitizedName = SanitizeColumnName(control.Name);

                        // Skip if we've already added description for this column
                        if (!processedDescriptions.Contains(sanitizedName))
                        {
                            processedDescriptions.Add(sanitizedName);
                            var description = string.IsNullOrEmpty(control.Label) ? control.Name : control.Label;
                            var safeDescription = description.Replace("'", "''"); // Escape single quotes

                            sb.AppendLine($"-- Add column description for {sanitizedName}");
                            sb.AppendLine($"IF NOT EXISTS (");
                            sb.AppendLine($"    SELECT 1 FROM sys.extended_properties ep");
                            sb.AppendLine($"    INNER JOIN sys.columns c ON ep.major_id = c.object_id AND ep.minor_id = c.column_id");
                            sb.AppendLine($"    INNER JOIN sys.tables t ON c.object_id = t.object_id");
                            sb.AppendLine($"    WHERE t.name = N'{fullTableName}' AND c.name = N'{sanitizedName}' AND ep.name = N'MS_Description'");
                            sb.AppendLine($")");
                            sb.AppendLine($"BEGIN");
                            sb.AppendLine($"    EXEC sp_addextendedproperty ");
                            sb.AppendLine($"        @name = N'MS_Description',");
                            sb.AppendLine($"        @value = N'{safeDescription} (Type: {control.Type})',");
                            sb.AppendLine($"        @level0type = N'SCHEMA', @level0name = N'dbo',");
                            sb.AppendLine($"        @level1type = N'TABLE', @level1name = N'{fullTableName}',");
                            sb.AppendLine($"        @level2type = N'COLUMN', @level2name = N'{sanitizedName}';");
                            sb.AppendLine($"END");
                            sb.AppendLine();
                        }
                    }
                }

                scripts.Add(new SqlScript
                {
                    Name = $"Create_{fullTableName}_Table",
                    Type = ScriptType.Table,
                    Content = sb.ToString(),
                    ExecutionOrder = order++,
                    Description = $"Repeating section table for {section.Name} with {section.Controls.Count} controls"
                });
            }

            return scripts;
        }

        // Keep all the existing methods but update signatures where needed
        private List<SqlScript> GenerateLookupTables(InfoPathFormDefinition formDef)
        {
            var scripts = new List<SqlScript>();
            var tableName = SanitizeTableName(formDef.FormName);
            int order = 50;

            var controlsWithData = formDef.Views
                .SelectMany(v => v.Controls)
                .Where(c => c.HasStaticData && c.DataOptions != null && c.DataOptions.Count > 5)
                .GroupBy(c => c.Name)
                .ToList();

            foreach (var controlGroup in controlsWithData)
            {
                var control = controlGroup.First();
                var lookupTableName = $"{tableName}_{SanitizeTableName(control.Name)}_Lookup";

                var sb = new StringBuilder();
                sb.AppendLine("-- ===============================================");
                sb.AppendLine($"-- Lookup table for {control.Name}");
                sb.AppendLine("-- ===============================================");
                sb.AppendLine();

                sb.AppendLine($"CREATE TABLE [dbo].[{lookupTableName}] (");
                sb.AppendLine("    [Id] INT IDENTITY(1,1) NOT NULL,");
                sb.AppendLine("    [Value] NVARCHAR(255) NOT NULL,");
                sb.AppendLine("    [DisplayText] NVARCHAR(500) NULL,");
                sb.AppendLine("    [Description] NVARCHAR(1000) NULL,");
                sb.AppendLine("    [SortOrder] INT NOT NULL DEFAULT 0,");
                sb.AppendLine("    [IsActive] BIT NOT NULL DEFAULT 1,");
                sb.AppendLine("    [IsDefault] BIT NOT NULL DEFAULT 0,");
                sb.AppendLine("    [CreatedDate] DATETIME2(3) DEFAULT GETDATE() NOT NULL,");
                sb.AppendLine("    [ModifiedDate] DATETIME2(3) DEFAULT GETDATE() NOT NULL,");
                sb.AppendLine();
                sb.AppendLine($"    CONSTRAINT [PK_{lookupTableName}] PRIMARY KEY CLUSTERED ([Id] ASC),");
                sb.AppendLine($"    CONSTRAINT [UQ_{lookupTableName}_Value] UNIQUE ([Value])");
                sb.AppendLine(");");
                sb.AppendLine();

                sb.AppendLine($"CREATE NONCLUSTERED INDEX [IX_{lookupTableName}_Value]");
                sb.AppendLine($"ON [dbo].[{lookupTableName}] ([Value], [IsActive])");
                sb.AppendLine("INCLUDE ([DisplayText], [SortOrder]);");
                sb.AppendLine();

                sb.AppendLine($"-- Insert static values for {control.Name}");
                sb.AppendLine($"INSERT INTO [dbo].[{lookupTableName}] ([Value], [DisplayText], [SortOrder], [IsDefault])");
                sb.AppendLine("VALUES");

                var optionsList = control.DataOptions.OrderBy(o => o.Order).ToList();
                for (int i = 0; i < optionsList.Count; i++)
                {
                    var option = optionsList[i];
                    var value = option.Value.Replace("'", "''");
                    var displayText = option.DisplayText.Replace("'", "''");
                    var isDefault = option.IsDefault ? "1" : "0";

                    sb.Append($"    (N'{value}', N'{displayText}', {option.Order}, {isDefault})");
                    sb.AppendLine(i < optionsList.Count - 1 ? "," : ";");
                }
                sb.AppendLine();

                scripts.Add(new SqlScript
                {
                    Name = $"Create_{lookupTableName}",
                    Type = ScriptType.Table,
                    Content = sb.ToString(),
                    ExecutionOrder = order++,
                    Description = $"Lookup table for {control.Name} dropdown values"
                });
            }

            return scripts;
        }

        private List<SqlScript> GenerateStoredProcedures(InfoPathFormDefinition formDef, RepeatingSectionAnalysis analysis)
        {
            var scripts = new List<SqlScript>();
            var mainTableName = SanitizeTableName(formDef.FormName);

            scripts.Add(GenerateInsertProcedure(formDef, mainTableName, analysis));
            scripts.Add(GenerateUpdateProcedure(formDef, mainTableName, analysis));
            scripts.Add(GenerateGetProcedure(formDef, mainTableName, analysis));
            scripts.Add(GenerateDeleteProcedure(formDef, mainTableName, analysis));
            scripts.Add(GenerateListProcedure(formDef, mainTableName, analysis));

            return scripts;
        }

        private SqlScript GenerateInsertProcedure(InfoPathFormDefinition formDef, string tableName, RepeatingSectionAnalysis analysis)
        {
            var sb = new StringBuilder();

            sb.AppendLine("-- ===============================================");
            sb.AppendLine($"-- Insert procedure for {tableName}");
            sb.AppendLine("-- ===============================================");
            sb.AppendLine();

            sb.AppendLine($"CREATE PROCEDURE [dbo].[sp_{tableName}_Insert]");

            // Add output parameter for FormId
            sb.AppendLine("    @FormId UNIQUEIDENTIFIER OUTPUT,");
            sb.AppendLine("    @CreatedBy NVARCHAR(255) = NULL,");
            sb.AppendLine("    @Status NVARCHAR(50) = 'Draft'");

            // Add parameters for each main table column
            foreach (var control in analysis.MainTableColumns.OrderBy(c => c.Name))
            {
                var columnName = SanitizeColumnName(control.Name);
                var sqlType = GetSqlType(control.Type);
                sb.AppendLine($"    ,@{columnName} {sqlType} = NULL");
            }

            sb.AppendLine("AS");
            sb.AppendLine("BEGIN");
            sb.AppendLine("    SET NOCOUNT ON;");
            sb.AppendLine();
            sb.AppendLine("    -- Generate new FormId if not provided");
            sb.AppendLine("    IF @FormId IS NULL");
            sb.AppendLine("        SET @FormId = NEWID();");
            sb.AppendLine();
            sb.AppendLine("    BEGIN TRY");
            sb.AppendLine("        BEGIN TRANSACTION;");
            sb.AppendLine();
            sb.AppendLine($"        INSERT INTO [dbo].[{tableName}] (");
            sb.AppendLine("            [FormId],");
            sb.AppendLine("            [CreatedBy],");
            sb.AppendLine("            [Status],");
            sb.AppendLine("            [CreatedDate],");
            sb.AppendLine("            [ModifiedDate]");

            // Add column names
            foreach (var control in analysis.MainTableColumns)
            {
                var columnName = SanitizeColumnName(control.Name);
                sb.AppendLine($"            ,[{columnName}]");
            }

            sb.AppendLine("        ) VALUES (");
            sb.AppendLine("            @FormId,");
            sb.AppendLine("            @CreatedBy,");
            sb.AppendLine("            @Status,");
            sb.AppendLine("            GETDATE(),");
            sb.AppendLine("            GETDATE()");

            // Add parameter values
            foreach (var control in analysis.MainTableColumns)
            {
                var columnName = SanitizeColumnName(control.Name);
                sb.AppendLine($"            ,@{columnName}");
            }

            sb.AppendLine("        );");
            sb.AppendLine();
            sb.AppendLine("        COMMIT TRANSACTION;");
            sb.AppendLine("    END TRY");
            sb.AppendLine("    BEGIN CATCH");
            sb.AppendLine("        IF @@TRANCOUNT > 0");
            sb.AppendLine("            ROLLBACK TRANSACTION;");
            sb.AppendLine("        THROW;");
            sb.AppendLine("    END CATCH");
            sb.AppendLine("END");

            return new SqlScript
            {
                Name = $"sp_{tableName}_Insert",
                Type = ScriptType.StoredProcedure,
                Content = sb.ToString(),
                ExecutionOrder = 100,
                Description = $"Insert procedure for {tableName}"
            };
        }

        private SqlScript GenerateUpdateProcedure(InfoPathFormDefinition formDef, string tableName, RepeatingSectionAnalysis analysis)
        {
            var sb = new StringBuilder();

            sb.AppendLine("-- ===============================================");
            sb.AppendLine($"-- Update procedure for {tableName}");
            sb.AppendLine("-- ===============================================");
            sb.AppendLine();

            sb.AppendLine($"CREATE PROCEDURE [dbo].[sp_{tableName}_Update]");
            sb.AppendLine("    @FormId UNIQUEIDENTIFIER,");
            sb.AppendLine("    @ModifiedBy NVARCHAR(255) = NULL,");
            sb.AppendLine("    @Status NVARCHAR(50) = NULL");

            // Add parameters for each column
            foreach (var control in analysis.MainTableColumns.OrderBy(c => c.Name))
            {
                var columnName = SanitizeColumnName(control.Name);
                var sqlType = GetSqlType(control.Type);
                sb.AppendLine($"    ,@{columnName} {sqlType} = NULL");
            }

            sb.AppendLine("AS");
            sb.AppendLine("BEGIN");
            sb.AppendLine("    SET NOCOUNT ON;");
            sb.AppendLine();
            sb.AppendLine("    BEGIN TRY");
            sb.AppendLine("        BEGIN TRANSACTION;");
            sb.AppendLine();
            sb.AppendLine($"        UPDATE [dbo].[{tableName}]");
            sb.AppendLine("        SET");
            sb.AppendLine("            [ModifiedDate] = GETDATE(),");
            sb.AppendLine("            [ModifiedBy] = ISNULL(@ModifiedBy, [ModifiedBy]),");
            sb.AppendLine("            [Status] = ISNULL(@Status, [Status]),");
            sb.AppendLine("            [Version] = [Version] + 1");

            // Add update for each column
            foreach (var control in analysis.MainTableColumns)
            {
                var columnName = SanitizeColumnName(control.Name);
                sb.AppendLine($"            ,[{columnName}] = ISNULL(@{columnName}, [{columnName}])");
            }

            sb.AppendLine("        WHERE [FormId] = @FormId;");
            sb.AppendLine();
            sb.AppendLine("        IF @@ROWCOUNT = 0");
            sb.AppendLine("            RAISERROR('Record not found', 16, 1);");
            sb.AppendLine();
            sb.AppendLine("        COMMIT TRANSACTION;");
            sb.AppendLine("    END TRY");
            sb.AppendLine("    BEGIN CATCH");
            sb.AppendLine("        IF @@TRANCOUNT > 0");
            sb.AppendLine("            ROLLBACK TRANSACTION;");
            sb.AppendLine("        THROW;");
            sb.AppendLine("    END CATCH");
            sb.AppendLine("END");

            return new SqlScript
            {
                Name = $"sp_{tableName}_Update",
                Type = ScriptType.StoredProcedure,
                Content = sb.ToString(),
                ExecutionOrder = 101,
                Description = $"Update procedure for {tableName}"
            };
        }

        private SqlScript GenerateGetProcedure(InfoPathFormDefinition formDef, string tableName, RepeatingSectionAnalysis analysis)
        {
            var sb = new StringBuilder();

            sb.AppendLine("-- ===============================================");
            sb.AppendLine($"-- Get procedure for {tableName} with repeating sections");
            sb.AppendLine("-- ===============================================");
            sb.AppendLine();

            sb.AppendLine($"CREATE PROCEDURE [dbo].[sp_{tableName}_Get]");
            sb.AppendLine("    @FormId UNIQUEIDENTIFIER = NULL,");
            sb.AppendLine("    @Id INT = NULL");
            sb.AppendLine("AS");
            sb.AppendLine("BEGIN");
            sb.AppendLine("    SET NOCOUNT ON;");
            sb.AppendLine();
            sb.AppendLine("    -- Get main record");
            sb.AppendLine("    SELECT *");
            sb.AppendLine($"    FROM [dbo].[{tableName}]");
            sb.AppendLine("    WHERE (@FormId IS NOT NULL AND [FormId] = @FormId)");
            sb.AppendLine("       OR (@Id IS NOT NULL AND [Id] = @Id);");
            sb.AppendLine();

            // Get repeating section data
            foreach (var sectionKvp in analysis.RepeatingSections)
            {
                var section = sectionKvp.Value;
                var sectionTableName = SanitizeTableName(section.Name);
                var fullTableName = $"{tableName}_{sectionTableName}";

                sb.AppendLine($"    -- Get {section.Name} repeating section data ({section.Controls.Count} controls)");
                sb.AppendLine("    IF @FormId IS NOT NULL");
                sb.AppendLine("    BEGIN");
                sb.AppendLine("        SELECT *");
                sb.AppendLine($"        FROM [dbo].[{fullTableName}]");
                sb.AppendLine("        WHERE [ParentFormId] = @FormId");
                sb.AppendLine("        ORDER BY [ItemOrder];");
                sb.AppendLine("    END");
                sb.AppendLine();
            }

            sb.AppendLine("END");

            return new SqlScript
            {
                Name = $"sp_{tableName}_Get",
                Type = ScriptType.StoredProcedure,
                Content = sb.ToString(),
                ExecutionOrder = 102,
                Description = $"Get procedure for {tableName} with repeating sections"
            };
        }

        private SqlScript GenerateDeleteProcedure(InfoPathFormDefinition formDef, string tableName, RepeatingSectionAnalysis analysis)
        {
            var sb = new StringBuilder();

            sb.AppendLine("-- ===============================================");
            sb.AppendLine($"-- Delete procedure for {tableName}");
            sb.AppendLine("-- ===============================================");
            sb.AppendLine();

            sb.AppendLine($"CREATE PROCEDURE [dbo].[sp_{tableName}_Delete]");
            sb.AppendLine("    @FormId UNIQUEIDENTIFIER = NULL,");
            sb.AppendLine("    @Id INT = NULL,");
            sb.AppendLine("    @SoftDelete BIT = 1");
            sb.AppendLine("AS");
            sb.AppendLine("BEGIN");
            sb.AppendLine("    SET NOCOUNT ON;");
            sb.AppendLine();
            sb.AppendLine("    BEGIN TRY");
            sb.AppendLine("        BEGIN TRANSACTION;");
            sb.AppendLine();
            sb.AppendLine("        IF @SoftDelete = 1");
            sb.AppendLine("        BEGIN");
            sb.AppendLine("            -- Soft delete: just update status");
            sb.AppendLine($"            UPDATE [dbo].[{tableName}]");
            sb.AppendLine("            SET [Status] = 'Deleted',");
            sb.AppendLine("                [ModifiedDate] = GETDATE()");
            sb.AppendLine("            WHERE (@FormId IS NOT NULL AND [FormId] = @FormId)");
            sb.AppendLine("               OR (@Id IS NOT NULL AND [Id] = @Id);");
            sb.AppendLine("        END");
            sb.AppendLine("        ELSE");
            sb.AppendLine("        BEGIN");
            sb.AppendLine("            -- Hard delete: remove record (cascade will handle child records)");
            sb.AppendLine($"            DELETE FROM [dbo].[{tableName}]");
            sb.AppendLine("            WHERE (@FormId IS NOT NULL AND [FormId] = @FormId)");
            sb.AppendLine("               OR (@Id IS NOT NULL AND [Id] = @Id);");
            sb.AppendLine("        END");
            sb.AppendLine();
            sb.AppendLine("        IF @@ROWCOUNT = 0");
            sb.AppendLine("            RAISERROR('Record not found', 16, 1);");
            sb.AppendLine();
            sb.AppendLine("        COMMIT TRANSACTION;");
            sb.AppendLine("    END TRY");
            sb.AppendLine("    BEGIN CATCH");
            sb.AppendLine("        IF @@TRANCOUNT > 0");
            sb.AppendLine("            ROLLBACK TRANSACTION;");
            sb.AppendLine("        THROW;");
            sb.AppendLine("    END CATCH");
            sb.AppendLine("END");

            return new SqlScript
            {
                Name = $"sp_{tableName}_Delete",
                Type = ScriptType.StoredProcedure,
                Content = sb.ToString(),
                ExecutionOrder = 103,
                Description = $"Delete procedure for {tableName}"
            };
        }

        private SqlScript GenerateListProcedure(InfoPathFormDefinition formDef, string tableName, RepeatingSectionAnalysis analysis)
        {
            var sb = new StringBuilder();

            sb.AppendLine("-- ===============================================");
            sb.AppendLine($"-- List procedure with paging and filtering");
            sb.AppendLine("-- ===============================================");
            sb.AppendLine();

            sb.AppendLine($"CREATE PROCEDURE [dbo].[sp_{tableName}_List]");
            sb.AppendLine("    @Status NVARCHAR(50) = NULL,");
            sb.AppendLine("    @CreatedBy NVARCHAR(255) = NULL,");
            sb.AppendLine("    @FromDate DATETIME2 = NULL,");
            sb.AppendLine("    @ToDate DATETIME2 = NULL,");
            sb.AppendLine("    @SearchTerm NVARCHAR(100) = NULL,");
            sb.AppendLine("    @PageNumber INT = 1,");
            sb.AppendLine("    @PageSize INT = 50,");
            sb.AppendLine("    @SortBy NVARCHAR(50) = 'CreatedDate',");
            sb.AppendLine("    @SortDirection NVARCHAR(4) = 'DESC'");
            sb.AppendLine("AS");
            sb.AppendLine("BEGIN");
            sb.AppendLine("    SET NOCOUNT ON;");
            sb.AppendLine();
            sb.AppendLine("    -- Validate parameters");
            sb.AppendLine("    IF @PageNumber < 1 SET @PageNumber = 1;");
            sb.AppendLine("    IF @PageSize < 1 SET @PageSize = 50;");
            sb.AppendLine("    IF @PageSize > 1000 SET @PageSize = 1000;");
            sb.AppendLine();
            sb.AppendLine("    -- Calculate offset");
            sb.AppendLine("    DECLARE @Offset INT = (@PageNumber - 1) * @PageSize;");
            sb.AppendLine();
            sb.AppendLine("    -- Get total count");
            sb.AppendLine("    SELECT COUNT(*) AS TotalCount");
            sb.AppendLine($"    FROM [dbo].[{tableName}]");
            sb.AppendLine("    WHERE 1=1");
            sb.AppendLine("        AND (@Status IS NULL OR [Status] = @Status)");
            sb.AppendLine("        AND (@CreatedBy IS NULL OR [CreatedBy] = @CreatedBy)");
            sb.AppendLine("        AND (@FromDate IS NULL OR [CreatedDate] >= @FromDate)");
            sb.AppendLine("        AND (@ToDate IS NULL OR [CreatedDate] <= @ToDate);");
            sb.AppendLine();
            sb.AppendLine("    -- Get paged results");
            sb.AppendLine("    SELECT *");
            sb.AppendLine($"    FROM [dbo].[{tableName}]");
            sb.AppendLine("    WHERE 1=1");
            sb.AppendLine("        AND (@Status IS NULL OR [Status] = @Status)");
            sb.AppendLine("        AND (@CreatedBy IS NULL OR [CreatedBy] = @CreatedBy)");
            sb.AppendLine("        AND (@FromDate IS NULL OR [CreatedDate] >= @FromDate)");
            sb.AppendLine("        AND (@ToDate IS NULL OR [CreatedDate] <= @ToDate)");
            sb.AppendLine("    ORDER BY");
            sb.AppendLine("        CASE WHEN @SortBy = 'CreatedDate' AND @SortDirection = 'ASC' THEN [CreatedDate] END ASC,");
            sb.AppendLine("        CASE WHEN @SortBy = 'CreatedDate' AND @SortDirection = 'DESC' THEN [CreatedDate] END DESC,");
            sb.AppendLine("        CASE WHEN @SortBy = 'ModifiedDate' AND @SortDirection = 'ASC' THEN [ModifiedDate] END ASC,");
            sb.AppendLine("        CASE WHEN @SortBy = 'ModifiedDate' AND @SortDirection = 'DESC' THEN [ModifiedDate] END DESC,");
            sb.AppendLine("        CASE WHEN @SortBy = 'Status' AND @SortDirection = 'ASC' THEN [Status] END ASC,");
            sb.AppendLine("        CASE WHEN @SortBy = 'Status' AND @SortDirection = 'DESC' THEN [Status] END DESC");
            sb.AppendLine("    OFFSET @Offset ROWS");
            sb.AppendLine("    FETCH NEXT @PageSize ROWS ONLY;");
            sb.AppendLine("END");

            return new SqlScript
            {
                Name = $"sp_{tableName}_List",
                Type = ScriptType.StoredProcedure,
                Content = sb.ToString(),
                ExecutionOrder = 104,
                Description = $"List procedure with paging for {tableName}"
            };
        }

        private SqlScript GenerateMainView(InfoPathFormDefinition formDef, RepeatingSectionAnalysis analysis)
        {
            var tableName = SanitizeTableName(formDef.FormName);
            var sb = new StringBuilder();

            sb.AppendLine("-- ===============================================");
            sb.AppendLine($"-- Summary view for {tableName} with repeating section counts");
            sb.AppendLine("-- ===============================================");
            sb.AppendLine();

            sb.AppendLine($"CREATE VIEW [dbo].[vw_{tableName}_Summary]");
            sb.AppendLine("AS");
            sb.AppendLine("SELECT");
            sb.AppendLine("    t.[Id],");
            sb.AppendLine("    t.[FormId],");
            sb.AppendLine("    t.[Status],");
            sb.AppendLine("    t.[CreatedDate],");
            sb.AppendLine("    t.[ModifiedDate],");
            sb.AppendLine("    t.[CreatedBy],");
            sb.AppendLine("    t.[ModifiedBy],");
            sb.AppendLine("    t.[Version],");

            // Add key columns (first 5 main table columns)
            var keyColumns = analysis.MainTableColumns.Take(5).ToList();
            foreach (var control in keyColumns)
            {
                var columnName = SanitizeColumnName(control.Name);
                sb.AppendLine($"    t.[{columnName}],");
            }

            // Add count of repeating section items
            foreach (var sectionKvp in analysis.RepeatingSections)
            {
                var section = sectionKvp.Value;
                var sectionTableName = SanitizeTableName(section.Name);
                var fullTableName = $"{tableName}_{sectionTableName}";
                sb.AppendLine($"    (SELECT COUNT(*) FROM [dbo].[{fullTableName}] WHERE [ParentFormId] = t.[FormId]) AS [{sectionTableName}_Count],");
            }

            // Remove last comma
            sb.Length -= 3;
            sb.AppendLine();

            sb.AppendLine($"FROM [dbo].[{tableName}] t");
            sb.AppendLine("WHERE t.[Status] != 'Deleted';");

            return new SqlScript
            {
                Name = $"vw_{tableName}_Summary",
                Type = ScriptType.View,
                Content = sb.ToString(),
                ExecutionOrder = 200,
                Description = $"Summary view for {tableName} with repeating section counts"
            };
        }

        private SqlScript GenerateIndexes(InfoPathFormDefinition formDef, RepeatingSectionAnalysis analysis)
        {
            var tableName = SanitizeTableName(formDef.FormName);
            var sb = new StringBuilder();

            sb.AppendLine("-- ===============================================");
            sb.AppendLine($"-- Indexes for {tableName} and repeating sections");
            sb.AppendLine("-- ===============================================");
            sb.AppendLine();

            // Standard indexes for main table
            sb.AppendLine("-- Index on FormId for quick lookups");
            sb.AppendLine($"CREATE NONCLUSTERED INDEX [IX_{tableName}_FormId]");
            sb.AppendLine($"ON [dbo].[{tableName}] ([FormId])");
            sb.AppendLine("INCLUDE ([Status], [CreatedDate], [ModifiedDate]);");
            sb.AppendLine();

            sb.AppendLine("-- Index on CreatedDate for sorting and filtering");
            sb.AppendLine($"CREATE NONCLUSTERED INDEX [IX_{tableName}_CreatedDate]");
            sb.AppendLine($"ON [dbo].[{tableName}] ([CreatedDate] DESC)");
            sb.AppendLine("INCLUDE ([FormId], [Status], [CreatedBy]);");
            sb.AppendLine();

            sb.AppendLine("-- Index on Status for filtering");
            sb.AppendLine($"CREATE NONCLUSTERED INDEX [IX_{tableName}_Status]");
            sb.AppendLine($"ON [dbo].[{tableName}] ([Status])");
            sb.AppendLine("INCLUDE ([FormId], [CreatedDate]);");
            sb.AppendLine();

            // Add indexes for frequently searched main table columns
            var searchableColumns = analysis.MainTableColumns
                .Where(c => c.Type == "TextField" || c.Type == "DropDown")
                .Take(3)
                .ToList();

            foreach (var control in searchableColumns)
            {
                var columnName = SanitizeColumnName(control.Name);
                sb.AppendLine($"-- Index on {columnName} for searching");
                sb.AppendLine($"CREATE NONCLUSTERED INDEX [IX_{tableName}_{columnName}]");
                sb.AppendLine($"ON [dbo].[{tableName}] ([{columnName}])");
                sb.AppendLine($"WHERE [{columnName}] IS NOT NULL;");
                sb.AppendLine();
            }

            // Add indexes for repeating section tables
            foreach (var sectionKvp in analysis.RepeatingSections)
            {
                var section = sectionKvp.Value;
                var sectionTableName = SanitizeTableName(section.Name);
                var fullTableName = $"{tableName}_{sectionTableName}";

                sb.AppendLine($"-- Additional indexes for {section.Name} repeating section");
                sb.AppendLine($"CREATE NONCLUSTERED INDEX [IX_{fullTableName}_Parent_Order]");
                sb.AppendLine($"ON [dbo].[{fullTableName}] ([ParentFormId], [ItemOrder])");
                sb.AppendLine("INCLUDE ([CreatedDate], [ModifiedDate]);");
                sb.AppendLine();
            }

            return new SqlScript
            {
                Name = $"Create_{tableName}_Indexes",
                Type = ScriptType.Index,
                Content = sb.ToString(),
                ExecutionOrder = 300,
                Description = $"Performance indexes for {tableName} and repeating sections"
            };
        }

        // Helper methods
        private string GetDefaultValueClause(ControlDefinition control)
        {
            if (control.Properties != null && control.Properties.ContainsKey("DefaultValue"))
            {
                var defaultValue = control.Properties["DefaultValue"];

                switch (control.Type?.ToLower())
                {
                    case "checkbox":
                        return defaultValue.ToLower() == "true" ? " DEFAULT 1" : " DEFAULT 0";
                    case "textfield":
                    case "dropdown":
                    case "combobox":
                        return $" DEFAULT N'{defaultValue.Replace("'", "''")}'";
                    case "number":
                        if (decimal.TryParse(defaultValue, out var numValue))
                            return $" DEFAULT {numValue}";
                        break;
                    case "datepicker":
                        if (defaultValue.ToLower() == "today" || defaultValue.ToLower() == "now")
                            return " DEFAULT GETDATE()";
                        break;
                }
            }

            if (control.HasStaticData && control.DataOptions != null && control.DataOptions.Any(o => o.IsDefault))
            {
                var defaultOption = control.DataOptions.First(o => o.IsDefault);
                return $" DEFAULT N'{defaultOption.Value.Replace("'", "''")}'";
            }

            return "";
        }

        private string GetSqlType(string controlType)
        {
            if (Dialect == SqlDialect.SqlServer)
            {
                switch (controlType?.ToLower())
                {
                    case "textfield":
                    case "plaintext":
                        return "NVARCHAR(500)";
                    case "richtext":
                        return "NVARCHAR(MAX)";
                    case "datepicker":
                        return "DATETIME2(3)";
                    case "checkbox":
                        return "BIT";
                    case "dropdown":
                    case "combobox":
                    case "radiobutton":
                        return "NVARCHAR(255)";
                    case "fileattachment":
                        return "VARBINARY(MAX)";
                    case "peoplepicker":
                        return "NVARCHAR(500)";
                    case "number":
                    case "integer":
                        return "INT";
                    case "decimal":
                    case "currency":
                        return "DECIMAL(18,4)";
                    case "email":
                        return "NVARCHAR(255)";
                    case "url":
                    case "hyperlink":
                        return "NVARCHAR(2000)";
                    case "phone":
                        return "NVARCHAR(50)";
                    default:
                        return "NVARCHAR(MAX)";
                }
            }

            return "VARCHAR(MAX)";
        }

        private string SanitizeTableName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "FormTable";

            name = System.IO.Path.GetFileNameWithoutExtension(name);
            var sanitized = System.Text.RegularExpressions.Regex.Replace(name, @"[^a-zA-Z0-9]", "_");
            sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"_+", "_");
            sanitized = sanitized.Trim('_');

            if (sanitized.Length > 0 && char.IsDigit(sanitized[0]))
                sanitized = "Form_" + sanitized;

            if (sanitized.Length > 64)
                sanitized = sanitized.Substring(0, 64);

            return string.IsNullOrEmpty(sanitized) ? "FormTable" : sanitized;
        }

        private string SanitizeColumnName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "Column";

            var sanitized = System.Text.RegularExpressions.Regex.Replace(name, @"[^a-zA-Z0-9]", "_");
            sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"_+", "_");
            sanitized = sanitized.Trim('_');

            if (sanitized.Length > 0 && char.IsDigit(sanitized[0]))
                sanitized = "Col_" + sanitized;

            string[] reservedWords = { "User", "Date", "Time", "Table", "Index", "Key", "Primary", "Foreign", "References", "Order", "Group", "By" };
            if (reservedWords.Contains(sanitized, StringComparer.OrdinalIgnoreCase))
                sanitized = sanitized + "_Field";

            if (sanitized.Length > 128)
                sanitized = sanitized.Substring(0, 128);

            return string.IsNullOrEmpty(sanitized) ? "Column" : sanitized;
        }
    }

    /// <summary>
    /// Enhanced analysis structure for repeating sections
    /// </summary>
    public class RepeatingSectionAnalysis
    {
        public Dictionary<string, RepeatingSectionInfo> RepeatingSections { get; set; } = new Dictionary<string, RepeatingSectionInfo>();
        public List<ControlDefinition> MainTableColumns { get; set; } = new List<ControlDefinition>();
    }

    /// <summary>
    /// Detailed information about a repeating section
    /// </summary>
    public class RepeatingSectionInfo
    {
        public string Name { get; set; }
        public string SectionType { get; set; }
        public int StartRow { get; set; }
        public int EndRow { get; set; }
        public List<ControlDefinition> Controls { get; set; } = new List<ControlDefinition>();
        public List<string> ChildSections { get; set; } = new List<string>();
    }
}