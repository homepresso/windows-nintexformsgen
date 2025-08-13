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
    /// Enhanced SQL generation service for creating database schema from form analysis
    /// </summary>
    public class SqlGeneratorService : ISqlGenerator
    {
        public SqlDialect Dialect { get; set; } = SqlDialect.SqlServer;

        public async Task<SqlGenerationResult> GenerateFromAnalysisAsync(FormAnalysisResult analysis)
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

                    // Generate drop statements first (for re-deployment)
                    scripts.Add(GenerateDropStatements(formDef));

                    // Generate main form table
                    scripts.Add(GenerateMainTable(formDef));

                    // Generate lookup tables for dropdowns
                    scripts.AddRange(GenerateLookupTables(formDef));

                    // Generate tables for repeating sections
                    scripts.AddRange(GenerateRepeatingTables(formDef));

                    // Generate stored procedures
                    scripts.AddRange(GenerateStoredProcedures(formDef));

                    // Generate views
                    scripts.Add(GenerateMainView(formDef));

                    // Generate indexes
                    scripts.Add(GenerateIndexes(formDef));

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

        private SqlScript GenerateDropStatements(InfoPathFormDefinition formDef)
        {
            var tableName = SanitizeTableName(formDef.Views.First().ViewName);
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
            sb.AppendLine($"IF OBJECT_ID('dbo.sp_{tableName}_Insert', 'P') IS NOT NULL");
            sb.AppendLine($"    DROP PROCEDURE [dbo].[sp_{tableName}_Insert];");
            sb.AppendLine();

            sb.AppendLine($"IF OBJECT_ID('dbo.sp_{tableName}_Update', 'P') IS NOT NULL");
            sb.AppendLine($"    DROP PROCEDURE [dbo].[sp_{tableName}_Update];");
            sb.AppendLine();

            sb.AppendLine($"IF OBJECT_ID('dbo.sp_{tableName}_Get', 'P') IS NOT NULL");
            sb.AppendLine($"    DROP PROCEDURE [dbo].[sp_{tableName}_Get];");
            sb.AppendLine();

            sb.AppendLine($"IF OBJECT_ID('dbo.sp_{tableName}_Delete', 'P') IS NOT NULL");
            sb.AppendLine($"    DROP PROCEDURE [dbo].[sp_{tableName}_Delete];");
            sb.AppendLine();

            sb.AppendLine($"IF OBJECT_ID('dbo.sp_{tableName}_List', 'P') IS NOT NULL");
            sb.AppendLine($"    DROP PROCEDURE [dbo].[sp_{tableName}_List];");
            sb.AppendLine();

            // Drop repeating section tables
            var repeatingSections = formDef.Data
                .Where(d => d.IsRepeating)
                .Select(d => d.RepeatingSection)
                .Distinct()
                .Where(s => !string.IsNullOrEmpty(s));

            foreach (var section in repeatingSections)
            {
                var sectionTableName = SanitizeTableName(section);
                sb.AppendLine($"IF OBJECT_ID('dbo.{tableName}_{sectionTableName}', 'U') IS NOT NULL");
                sb.AppendLine($"    DROP TABLE [dbo].[{tableName}_{sectionTableName}];");
                sb.AppendLine();
            }

            // Drop lookup tables
            var controlsWithLookups = formDef.Views
                .SelectMany(v => v.Controls)
                .Where(c => c.HasStaticData && c.DataOptions.Count > 5)
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

            // Drop main table last (due to foreign key constraints)
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

        private SqlScript GenerateMainTable(InfoPathFormDefinition formDef)
        {
            var tableName = SanitizeTableName(formDef.Views.First().ViewName);
            var sb = new StringBuilder();

            if (Dialect == SqlDialect.SqlServer)
            {
                sb.AppendLine("-- ===============================================");
                sb.AppendLine($"-- Main table for {tableName}");
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
                sb.AppendLine("    -- Form Fields");

                // Track columns that need CHECK constraints
                var columnsWithConstraints = new List<(string columnName, List<string> validValues)>();

                // Add columns for non-repeating fields
                var processedColumns = new HashSet<string>();
                foreach (var column in formDef.Data.Where(d => !d.IsRepeating).OrderBy(d => d.ColumnName))
                {
                    var columnName = SanitizeColumnName(column.ColumnName);
                    if (!processedColumns.Contains(columnName))
                    {
                        processedColumns.Add(columnName);
                        var sqlType = GetSqlType(column.Type);

                        // FIXED: Default to NULL for safety since IsRequired doesn't exist
                        var nullable = " NULL";

                        // Check if this column has a default value from the control
                        var defaultValue = GetDefaultValueClause(column, formDef);

                        // Add comment for the column
                        var comment = string.IsNullOrEmpty(column.DisplayName) ? column.ColumnName : column.DisplayName;
                        sb.AppendLine($"    [{columnName}] {sqlType}{nullable}{defaultValue}, -- {comment}");

                        // Track if this column needs constraints
                        if (column.HasConstraints && column.ValidValues != null && column.ValidValues.Any())
                        {
                            columnsWithConstraints.Add((columnName,
                                column.ValidValues.Select(v => v.Value).ToList()));
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

                // Add table extended property for description
                sb.AppendLine("-- Add table description");
                sb.AppendLine($"EXEC sp_addextendedproperty ");
                sb.AppendLine($"    @name = N'MS_Description',");
                sb.AppendLine($"    @value = N'Main table for {formDef.Views.First().ViewName} form data',");
                sb.AppendLine($"    @level0type = N'SCHEMA', @level0name = N'dbo',");
                sb.AppendLine($"    @level1type = N'TABLE', @level1name = N'{tableName}';");
                sb.AppendLine();
            }

            return new SqlScript
            {
                Name = $"Create_{tableName}_Table",
                Type = ScriptType.Table,
                Content = sb.ToString(),
                ExecutionOrder = 1,
                Description = $"Main table for form {tableName}"
            };
        }


        private SqlScript GenerateIndexes(InfoPathFormDefinition formDef)
        {
            var tableName = SanitizeTableName(formDef.Views.First().ViewName);
            var sb = new StringBuilder();

            sb.AppendLine("-- ===============================================");
            sb.AppendLine($"-- Indexes for {tableName}");
            sb.AppendLine("-- ===============================================");
            sb.AppendLine();

            // Standard indexes
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

            sb.AppendLine("-- Index on CreatedBy for user queries");
            sb.AppendLine($"CREATE NONCLUSTERED INDEX [IX_{tableName}_CreatedBy]");
            sb.AppendLine($"ON [dbo].[{tableName}] ([CreatedBy])");
            sb.AppendLine("WHERE [CreatedBy] IS NOT NULL;");
            sb.AppendLine();

            // Add indexes for frequently searched text columns
            var searchableColumns = formDef.Data
                .Where(d => !d.IsRepeating &&
                       (d.Type == "TextField" || d.Type == "DropDown") &&
                       !d.ColumnName.Contains("Description") &&
                       !d.ColumnName.Contains("Comments"))
                .Take(3) // Limit to top 3 searchable columns
                .ToList();

            foreach (var column in searchableColumns)
            {
                var columnName = SanitizeColumnName(column.ColumnName);
                sb.AppendLine($"-- Index on {columnName} for searching");
                sb.AppendLine($"CREATE NONCLUSTERED INDEX [IX_{tableName}_{columnName}]");
                sb.AppendLine($"ON [dbo].[{tableName}] ([{columnName}])");
                sb.AppendLine($"WHERE [{columnName}] IS NOT NULL;");
                sb.AppendLine();
            }

            return new SqlScript
            {
                Name = $"Create_{tableName}_Indexes",
                Type = ScriptType.Index,
                Content = sb.ToString(),
                ExecutionOrder = 300,
                Description = $"Performance indexes for {tableName}"
            };
        }

        private List<SqlScript> GenerateLookupTables(InfoPathFormDefinition formDef)
        {
            var scripts = new List<SqlScript>();
            var tableName = SanitizeTableName(formDef.Views.First().ViewName);
            int order = 50; // Execute after main tables but before procedures

            // Find all controls with static data that might benefit from lookup tables
            var controlsWithData = formDef.Views
                .SelectMany(v => v.Controls)
                .Where(c => c.HasStaticData && c.DataOptions.Count > 5) // Only for controls with many options
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

                // Create index on Value for lookups
                sb.AppendLine($"CREATE NONCLUSTERED INDEX [IX_{lookupTableName}_Value]");
                sb.AppendLine($"ON [dbo].[{lookupTableName}] ([Value], [IsActive])");
                sb.AppendLine("INCLUDE ([DisplayText], [SortOrder]);");
                sb.AppendLine();

                // Insert the static values
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

        private string GetDefaultValueClause(DataColumn column, InfoPathFormDefinition formDef)
        {
            // Find the control that corresponds to this column
            var control = formDef.Views
                .SelectMany(v => v.Controls)
                .FirstOrDefault(c => c.Name == column.ColumnName || c.Binding == column.ColumnName);

            if (control == null)
                return "";

            // Check for default value in properties
            if (control.Properties != null && control.Properties.ContainsKey("DefaultValue"))
            {
                var defaultValue = control.Properties["DefaultValue"];

                // Format based on column type
                switch (column.Type?.ToLower())
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

            // Check for default option in DataOptions
            if (control.HasStaticData && control.DataOptions != null && control.DataOptions.Any(o => o.IsDefault))
            {
                var defaultOption = control.DataOptions.First(o => o.IsDefault);
                return $" DEFAULT N'{defaultOption.Value.Replace("'", "''")}'";
            }

            return "";
        }

        private List<SqlScript> GenerateRepeatingTables(InfoPathFormDefinition formDef)
        {
            var scripts = new List<SqlScript>();
            var mainTableName = SanitizeTableName(formDef.Views.First().ViewName);

            // Get unique repeating sections
            var repeatingSections = formDef.Data
                .Where(d => d.IsRepeating)
                .Select(d => d.RepeatingSection)
                .Distinct()
                .Where(s => !string.IsNullOrEmpty(s));

            int order = 2;
            foreach (var section in repeatingSections)
            {
                var sectionTableName = SanitizeTableName(section);
                var fullTableName = $"{mainTableName}_{sectionTableName}";
                var sb = new StringBuilder();

                if (Dialect == SqlDialect.SqlServer)
                {
                    sb.AppendLine("-- ===============================================");
                    sb.AppendLine($"-- Repeating section table: {sectionTableName}");
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
                    sb.AppendLine("    -- Section Fields");

                    // Add columns for this repeating section
                    var sectionColumns = formDef.Data
                        .Where(d => d.RepeatingSection == section)
                        .GroupBy(d => d.ColumnName)
                        .Select(g => g.First())
                        .OrderBy(d => d.ColumnName);

                    foreach (var column in sectionColumns)
                    {
                        var sanitizedName = SanitizeColumnName(column.ColumnName);
                        var sqlType = GetSqlType(column.Type);

                        // FIXED: Default to NULL for safety since IsRequired doesn't exist
                        var nullable = " NULL";

                        var defaultValue = GetDefaultValueClause(column, formDef);
                        var comment = string.IsNullOrEmpty(column.DisplayName) ? column.ColumnName : column.DisplayName;

                        sb.AppendLine($"    [{sanitizedName}] {sqlType}{nullable}{defaultValue}, -- {comment}");
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

                    // Add table description
                    sb.AppendLine("-- Add table description");
                    sb.AppendLine($"EXEC sp_addextendedproperty ");
                    sb.AppendLine($"    @name = N'MS_Description',");
                    sb.AppendLine($"    @value = N'Repeating section table for {section}',");
                    sb.AppendLine($"    @level0type = N'SCHEMA', @level0name = N'dbo',");
                    sb.AppendLine($"    @level1type = N'TABLE', @level1name = N'{fullTableName}';");
                }

                scripts.Add(new SqlScript
                {
                    Name = $"Create_{fullTableName}_Table",
                    Type = ScriptType.Table,
                    Content = sb.ToString(),
                    ExecutionOrder = order++,
                    Description = $"Repeating section table for {section}"
                });
            }

            return scripts;
        }

        private List<SqlScript> GenerateStoredProcedures(InfoPathFormDefinition formDef)
        {
            var scripts = new List<SqlScript>();
            var mainTableName = SanitizeTableName(formDef.Views.First().ViewName);

            // Generate CRUD procedures
            scripts.Add(GenerateInsertProcedure(formDef, mainTableName));
            scripts.Add(GenerateUpdateProcedure(formDef, mainTableName));
            scripts.Add(GenerateGetProcedure(formDef, mainTableName));
            scripts.Add(GenerateDeleteProcedure(formDef, mainTableName));
            scripts.Add(GenerateListProcedure(formDef, mainTableName));

            return scripts;
        }

        // Add these methods to your SqlGeneratorService class

        private SqlScript GenerateInsertProcedure(InfoPathFormDefinition formDef, string tableName)
        {
            var sb = new StringBuilder();

            sb.AppendLine("-- ===============================================");
            sb.AppendLine($"-- Insert procedure for {tableName}");
            sb.AppendLine("-- ===============================================");
            sb.AppendLine();

            sb.AppendLine($"CREATE PROCEDURE [dbo].[sp_{tableName}_Insert]");

            // Add parameters for non-repeating fields
            var nonRepeatingColumns = formDef.Data
                .Where(d => !d.IsRepeating)
                .GroupBy(d => d.ColumnName)
                .Select(g => g.First())
                .OrderBy(d => d.ColumnName)
                .ToList();

            // Add output parameter for FormId
            sb.AppendLine("    @FormId UNIQUEIDENTIFIER OUTPUT,");
            sb.AppendLine("    @CreatedBy NVARCHAR(255) = NULL,");
            sb.AppendLine("    @Status NVARCHAR(50) = 'Draft',");

            // Add parameters for each column
            for (int i = 0; i < nonRepeatingColumns.Count; i++)
            {
                var column = nonRepeatingColumns[i];
                var columnName = SanitizeColumnName(column.ColumnName);
                var sqlType = GetSqlType(column.Type);
                var isLast = i == nonRepeatingColumns.Count - 1;

                sb.AppendLine($"    @{columnName} {sqlType} = NULL{(isLast ? "" : ",")}");
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
            sb.AppendLine("            [ModifiedDate],");

            // Add column names
            foreach (var column in nonRepeatingColumns)
            {
                var columnName = SanitizeColumnName(column.ColumnName);
                sb.AppendLine($"            [{columnName}],");
            }

            // Remove last comma
            sb.Length -= 3;
            sb.AppendLine();

            sb.AppendLine("        ) VALUES (");
            sb.AppendLine("            @FormId,");
            sb.AppendLine("            @CreatedBy,");
            sb.AppendLine("            @Status,");
            sb.AppendLine("            GETDATE(),");
            sb.AppendLine("            GETDATE(),");

            // Add parameter values
            foreach (var column in nonRepeatingColumns)
            {
                var columnName = SanitizeColumnName(column.ColumnName);
                sb.AppendLine($"            @{columnName},");
            }

            // Remove last comma
            sb.Length -= 3;
            sb.AppendLine();
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

        private SqlScript GenerateUpdateProcedure(InfoPathFormDefinition formDef, string tableName)
        {
            var sb = new StringBuilder();

            sb.AppendLine("-- ===============================================");
            sb.AppendLine($"-- Update procedure for {tableName}");
            sb.AppendLine("-- ===============================================");
            sb.AppendLine();

            sb.AppendLine($"CREATE PROCEDURE [dbo].[sp_{tableName}_Update]");
            sb.AppendLine("    @FormId UNIQUEIDENTIFIER,");
            sb.AppendLine("    @ModifiedBy NVARCHAR(255) = NULL,");
            sb.AppendLine("    @Status NVARCHAR(50) = NULL,");

            // Add parameters for each column
            var nonRepeatingColumns = formDef.Data
                .Where(d => !d.IsRepeating)
                .GroupBy(d => d.ColumnName)
                .Select(g => g.First())
                .OrderBy(d => d.ColumnName)
                .ToList();

            for (int i = 0; i < nonRepeatingColumns.Count; i++)
            {
                var column = nonRepeatingColumns[i];
                var columnName = SanitizeColumnName(column.ColumnName);
                var sqlType = GetSqlType(column.Type);
                var isLast = i == nonRepeatingColumns.Count - 1;

                sb.AppendLine($"    @{columnName} {sqlType} = NULL{(isLast ? "" : ",")}");
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
            sb.AppendLine("            [Version] = [Version] + 1,");

            // Add update for each column
            foreach (var column in nonRepeatingColumns)
            {
                var columnName = SanitizeColumnName(column.ColumnName);
                sb.AppendLine($"            [{columnName}] = ISNULL(@{columnName}, [{columnName}]),");
            }

            // Remove last comma
            sb.Length -= 3;
            sb.AppendLine();

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

        private SqlScript GenerateGetProcedure(InfoPathFormDefinition formDef, string tableName)
        {
            var sb = new StringBuilder();

            sb.AppendLine("-- ===============================================");
            sb.AppendLine($"-- Get procedure for {tableName}");
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

            // Get repeating section data if exists
            var repeatingSections = formDef.Data
                .Where(d => d.IsRepeating)
                .Select(d => d.RepeatingSection)
                .Distinct()
                .Where(s => !string.IsNullOrEmpty(s));

            foreach (var section in repeatingSections)
            {
                var sectionTableName = SanitizeTableName(section);
                var fullTableName = $"{tableName}_{sectionTableName}";

                sb.AppendLine($"    -- Get {section} repeating section data");
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
                Description = $"Get procedure for {tableName}"
            };
        }

        private SqlScript GenerateDeleteProcedure(InfoPathFormDefinition formDef, string tableName)
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

        private SqlScript GenerateMainView(InfoPathFormDefinition formDef)
        {
            var tableName = SanitizeTableName(formDef.Views.First().ViewName);
            var sb = new StringBuilder();

            sb.AppendLine("-- ===============================================");
            sb.AppendLine($"-- Summary view for {tableName}");
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

            // Add key columns (first 5 non-repeating text fields)
            var keyColumns = formDef.Data
                .Where(d => !d.IsRepeating &&
                       (d.Type == "TextField" || d.Type == "DropDown"))
                .Take(5)
                .ToList();

            foreach (var column in keyColumns)
            {
                var columnName = SanitizeColumnName(column.ColumnName);
                sb.AppendLine($"    t.[{columnName}],");
            }

            // Add count of repeating section items
            var repeatingSections = formDef.Data
                .Where(d => d.IsRepeating)
                .Select(d => d.RepeatingSection)
                .Distinct()
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            if (repeatingSections.Any())
            {
                foreach (var section in repeatingSections)
                {
                    var sectionTableName = SanitizeTableName(section);
                    var fullTableName = $"{tableName}_{sectionTableName}";
                    sb.AppendLine($"    (SELECT COUNT(*) FROM [dbo].[{fullTableName}] WHERE [ParentFormId] = t.[FormId]) AS [{sectionTableName}_Count],");
                }
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
                Description = $"Summary view for {tableName}"
            };
        }
        private SqlScript GenerateListProcedure(InfoPathFormDefinition formDef, string tableName)
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

        // Keep other existing methods (GenerateInsertProcedure, GenerateUpdateProcedure, etc.) as they are
        // ... rest of the existing methods remain the same ...

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

            // Add other dialects as needed
            return "VARCHAR(MAX)";
        }

        // Keep SanitizeTableName and SanitizeColumnName methods as they are
        private string SanitizeTableName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "FormTable";

            // Remove file extension if present
            name = System.IO.Path.GetFileNameWithoutExtension(name);

            // Remove invalid characters and replace with underscore
            var sanitized = System.Text.RegularExpressions.Regex.Replace(name, @"[^a-zA-Z0-9]", "_");

            // Remove consecutive underscores
            sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"_+", "_");

            // Remove leading/trailing underscores
            sanitized = sanitized.Trim('_');

            // Ensure it starts with a letter
            if (sanitized.Length > 0 && char.IsDigit(sanitized[0]))
                sanitized = "Form_" + sanitized;

            // Limit length
            if (sanitized.Length > 64)
                sanitized = sanitized.Substring(0, 64);

            return string.IsNullOrEmpty(sanitized) ? "FormTable" : sanitized;
        }

        private string SanitizeColumnName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "Column";

            // Remove invalid characters
            var sanitized = System.Text.RegularExpressions.Regex.Replace(name, @"[^a-zA-Z0-9]", "_");

            // Remove consecutive underscores
            sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"_+", "_");

            // Remove leading/trailing underscores
            sanitized = sanitized.Trim('_');

            // Ensure it starts with a letter
            if (sanitized.Length > 0 && char.IsDigit(sanitized[0]))
                sanitized = "Col_" + sanitized;

            // Check for reserved words
            string[] reservedWords = { "User", "Date", "Time", "Table", "Index", "Key", "Primary", "Foreign", "References", "Order", "Group", "By" };
            if (reservedWords.Contains(sanitized, StringComparer.OrdinalIgnoreCase))
                sanitized = sanitized + "_Field";

            // Limit length
            if (sanitized.Length > 128)
                sanitized = sanitized.Substring(0, 128);

            return string.IsNullOrEmpty(sanitized) ? "Column" : sanitized;
        }
    }
}