using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FormGenerator.Core.Interfaces;
using FormGenerator.Core.Models;
using FormGenerator.Analyzers.Infopath;
using FormGenerator.Core.Interfaces;

namespace FormGenerator.Services
{
    /// <summary>
    /// SQL generation service for creating database schema from form analysis
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

                    // Generate main form table
                    scripts.Add(GenerateMainTable(formDef));

                    // Generate tables for repeating sections
                    scripts.AddRange(GenerateRepeatingTables(formDef));

                    // Generate stored procedures
                    scripts.AddRange(GenerateStoredProcedures(formDef));

                    // Generate views
                    scripts.Add(GenerateMainView(formDef));

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

        private SqlScript GenerateMainTable(InfoPathFormDefinition formDef)
        {
            var tableName = SanitizeTableName(formDef.Views.First().ViewName);
            var sb = new StringBuilder();

            if (Dialect == SqlDialect.SqlServer)
            {
                sb.AppendLine($"-- Main table for {tableName}");
                sb.AppendLine($"CREATE TABLE [dbo].[{tableName}] (");
                sb.AppendLine("    [Id] INT IDENTITY(1,1) PRIMARY KEY,");
                sb.AppendLine("    [FormId] UNIQUEIDENTIFIER DEFAULT NEWID() NOT NULL,");
                sb.AppendLine("    [CreatedDate] DATETIME2 DEFAULT GETDATE() NOT NULL,");
                sb.AppendLine("    [ModifiedDate] DATETIME2 DEFAULT GETDATE() NOT NULL,");
                sb.AppendLine("    [CreatedBy] NVARCHAR(255),");
                sb.AppendLine("    [ModifiedBy] NVARCHAR(255),");
                sb.AppendLine("    [Status] NVARCHAR(50) DEFAULT 'Draft',");

                // Add columns for non-repeating fields
                var processedColumns = new HashSet<string>();
                foreach (var column in formDef.Data.Where(d => !d.IsRepeating))
                {
                    var columnName = SanitizeColumnName(column.ColumnName);
                    if (!processedColumns.Contains(columnName))
                    {
                        processedColumns.Add(columnName);
                        var sqlType = GetSqlType(column.Type);
                        var nullable = column.IsConditional ? " NULL" : " NULL"; // Make all nullable for flexibility
                        sb.AppendLine($"    [{columnName}] {sqlType}{nullable},");
                    }
                }

                // Remove last comma
                if (sb.Length > 2)
                    sb.Length -= 3;
                sb.AppendLine();

                sb.AppendLine(");");
                sb.AppendLine();

                // Add indexes
                sb.AppendLine($"CREATE INDEX IX_{tableName}_FormId ON [dbo].[{tableName}] ([FormId]);");
                sb.AppendLine($"CREATE INDEX IX_{tableName}_CreatedDate ON [dbo].[{tableName}] ([CreatedDate]);");
                sb.AppendLine($"CREATE INDEX IX_{tableName}_Status ON [dbo].[{tableName}] ([Status]);");
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
                var sb = new StringBuilder();

                if (Dialect == SqlDialect.SqlServer)
                {
                    sb.AppendLine($"-- Repeating section table: {sectionTableName}");
                    sb.AppendLine($"CREATE TABLE [dbo].[{mainTableName}_{sectionTableName}] (");
                    sb.AppendLine("    [Id] INT IDENTITY(1,1) PRIMARY KEY,");
                    sb.AppendLine($"    [ParentFormId] UNIQUEIDENTIFIER NOT NULL,");
                    sb.AppendLine("    [ItemOrder] INT NOT NULL DEFAULT 0,");
                    sb.AppendLine("    [CreatedDate] DATETIME2 DEFAULT GETDATE() NOT NULL,");

                    // Add columns for this repeating section
                    var sectionColumns = formDef.Data
                        .Where(d => d.RepeatingSection == section)
                        .Select(d => d.ColumnName)
                        .Distinct();

                    foreach (var columnName in sectionColumns)
                    {
                        var column = formDef.Data.First(d => d.ColumnName == columnName && d.RepeatingSection == section);
                        var sanitizedName = SanitizeColumnName(column.ColumnName);
                        var sqlType = GetSqlType(column.Type);
                        sb.AppendLine($"    [{sanitizedName}] {sqlType} NULL,");
                    }

                    // Add foreign key constraint
                    sb.AppendLine($"    CONSTRAINT FK_{mainTableName}_{sectionTableName} FOREIGN KEY ([ParentFormId])");
                    sb.AppendLine($"        REFERENCES [dbo].[{mainTableName}] ([FormId])");
                    sb.AppendLine($"        ON DELETE CASCADE");
                    sb.AppendLine(");");
                    sb.AppendLine();

                    // Add index
                    sb.AppendLine($"CREATE INDEX IX_{mainTableName}_{sectionTableName}_ParentFormId ");
                    sb.AppendLine($"    ON [dbo].[{mainTableName}_{sectionTableName}] ([ParentFormId]);");
                }

                scripts.Add(new SqlScript
                {
                    Name = $"Create_{mainTableName}_{sectionTableName}_Table",
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

            // Generate Insert procedure
            scripts.Add(GenerateInsertProcedure(formDef, mainTableName));

            // Generate Update procedure
            scripts.Add(GenerateUpdateProcedure(formDef, mainTableName));

            // Generate Get procedure
            scripts.Add(GenerateGetProcedure(formDef, mainTableName));

            // Generate Delete procedure
            scripts.Add(GenerateDeleteProcedure(formDef, mainTableName));

            return scripts;
        }

        private SqlScript GenerateInsertProcedure(InfoPathFormDefinition formDef, string tableName)
        {
            var sb = new StringBuilder();

            if (Dialect == SqlDialect.SqlServer)
            {
                sb.AppendLine($"CREATE PROCEDURE [dbo].[sp_{tableName}_Insert]");

                // Add parameters
                var nonRepeatingColumns = formDef.Data.Where(d => !d.IsRepeating).ToList();
                foreach (var column in nonRepeatingColumns)
                {
                    var columnName = SanitizeColumnName(column.ColumnName);
                    var sqlType = GetSqlType(column.Type);
                    sb.AppendLine($"    @{columnName} {sqlType} = NULL,");
                }
                sb.AppendLine("    @CreatedBy NVARCHAR(255) = NULL,");
                sb.AppendLine("    @FormId UNIQUEIDENTIFIER OUTPUT");
                sb.AppendLine("AS");
                sb.AppendLine("BEGIN");
                sb.AppendLine("    SET NOCOUNT ON;");
                sb.AppendLine();
                sb.AppendLine("    SET @FormId = NEWID();");
                sb.AppendLine();
                sb.AppendLine($"    INSERT INTO [dbo].[{tableName}] (");
                sb.AppendLine("        [FormId],");
                sb.AppendLine("        [CreatedBy],");
                sb.AppendLine("        [ModifiedBy],");

                foreach (var column in nonRepeatingColumns)
                {
                    var columnName = SanitizeColumnName(column.ColumnName);
                    sb.AppendLine($"        [{columnName}],");
                }

                sb.Length -= 3; // Remove last comma
                sb.AppendLine();
                sb.AppendLine("    ) VALUES (");
                sb.AppendLine("        @FormId,");
                sb.AppendLine("        @CreatedBy,");
                sb.AppendLine("        @CreatedBy,");

                foreach (var column in nonRepeatingColumns)
                {
                    var columnName = SanitizeColumnName(column.ColumnName);
                    sb.AppendLine($"        @{columnName},");
                }

                sb.Length -= 3; // Remove last comma
                sb.AppendLine();
                sb.AppendLine("    );");
                sb.AppendLine();
                sb.AppendLine("    SELECT @FormId AS FormId;");
                sb.AppendLine("END");
            }

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

            if (Dialect == SqlDialect.SqlServer)
            {
                sb.AppendLine($"CREATE PROCEDURE [dbo].[sp_{tableName}_Update]");
                sb.AppendLine("    @FormId UNIQUEIDENTIFIER,");

                var nonRepeatingColumns = formDef.Data.Where(d => !d.IsRepeating).ToList();
                foreach (var column in nonRepeatingColumns)
                {
                    var columnName = SanitizeColumnName(column.ColumnName);
                    var sqlType = GetSqlType(column.Type);
                    sb.AppendLine($"    @{columnName} {sqlType} = NULL,");
                }

                sb.AppendLine("    @ModifiedBy NVARCHAR(255) = NULL");
                sb.AppendLine("AS");
                sb.AppendLine("BEGIN");
                sb.AppendLine("    SET NOCOUNT ON;");
                sb.AppendLine();
                sb.AppendLine($"    UPDATE [dbo].[{tableName}]");
                sb.AppendLine("    SET");
                sb.AppendLine("        [ModifiedDate] = GETDATE(),");
                sb.AppendLine("        [ModifiedBy] = @ModifiedBy,");

                foreach (var column in nonRepeatingColumns)
                {
                    var columnName = SanitizeColumnName(column.ColumnName);
                    sb.AppendLine($"        [{columnName}] = @{columnName},");
                }

                sb.Length -= 3; // Remove last comma
                sb.AppendLine();
                sb.AppendLine("    WHERE [FormId] = @FormId;");
                sb.AppendLine("END");
            }

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

            if (Dialect == SqlDialect.SqlServer)
            {
                sb.AppendLine($"CREATE PROCEDURE [dbo].[sp_{tableName}_Get]");
                sb.AppendLine("    @FormId UNIQUEIDENTIFIER");
                sb.AppendLine("AS");
                sb.AppendLine("BEGIN");
                sb.AppendLine("    SET NOCOUNT ON;");
                sb.AppendLine();
                sb.AppendLine("    -- Main form data");
                sb.AppendLine("    SELECT * FROM [dbo].[" + tableName + "]");
                sb.AppendLine("    WHERE [FormId] = @FormId;");
                sb.AppendLine();

                // Get repeating sections
                var repeatingSections = formDef.Data
                    .Where(d => d.IsRepeating)
                    .Select(d => d.RepeatingSection)
                    .Distinct()
                    .Where(s => !string.IsNullOrEmpty(s));

                foreach (var section in repeatingSections)
                {
                    var sectionTableName = SanitizeTableName(section);
                    sb.AppendLine($"    -- {section} repeating section data");
                    sb.AppendLine($"    SELECT * FROM [dbo].[{tableName}_{sectionTableName}]");
                    sb.AppendLine("    WHERE [ParentFormId] = @FormId");
                    sb.AppendLine("    ORDER BY [ItemOrder];");
                    sb.AppendLine();
                }

                sb.AppendLine("END");
            }

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

            if (Dialect == SqlDialect.SqlServer)
            {
                sb.AppendLine($"CREATE PROCEDURE [dbo].[sp_{tableName}_Delete]");
                sb.AppendLine("    @FormId UNIQUEIDENTIFIER");
                sb.AppendLine("AS");
                sb.AppendLine("BEGIN");
                sb.AppendLine("    SET NOCOUNT ON;");
                sb.AppendLine();
                sb.AppendLine("    -- Delete main record (cascades to child tables)");
                sb.AppendLine($"    DELETE FROM [dbo].[{tableName}]");
                sb.AppendLine("    WHERE [FormId] = @FormId;");
                sb.AppendLine("END");
            }

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

            if (Dialect == SqlDialect.SqlServer)
            {
                sb.AppendLine($"CREATE VIEW [dbo].[vw_{tableName}_Summary]");
                sb.AppendLine("AS");
                sb.AppendLine("SELECT");
                sb.AppendLine("    m.[Id],");
                sb.AppendLine("    m.[FormId],");
                sb.AppendLine("    m.[CreatedDate],");
                sb.AppendLine("    m.[ModifiedDate],");
                sb.AppendLine("    m.[CreatedBy],");
                sb.AppendLine("    m.[ModifiedBy],");
                sb.AppendLine("    m.[Status],");

                // Add a few key columns
                var keyColumns = formDef.Data
                    .Where(d => !d.IsRepeating)
                    .Take(10)
                    .ToList();

                foreach (var column in keyColumns)
                {
                    var columnName = SanitizeColumnName(column.ColumnName);
                    sb.AppendLine($"    m.[{columnName}],");
                }

                // Add counts for repeating sections
                var repeatingSections = formDef.Data
                    .Where(d => d.IsRepeating)
                    .Select(d => d.RepeatingSection)
                    .Distinct()
                    .Where(s => !string.IsNullOrEmpty(s));

                foreach (var section in repeatingSections)
                {
                    var sectionTableName = SanitizeTableName(section);
                    sb.AppendLine($"    (SELECT COUNT(*) FROM [dbo].[{tableName}_{sectionTableName}] r WHERE r.[ParentFormId] = m.[FormId]) AS [{sectionTableName}Count],");
                }

                sb.Length -= 3; // Remove last comma
                sb.AppendLine();
                sb.AppendLine($"FROM [dbo].[{tableName}] m;");
            }

            return new SqlScript
            {
                Name = $"vw_{tableName}_Summary",
                Type = ScriptType.View,
                Content = sb.ToString(),
                ExecutionOrder = 200,
                Description = $"Summary view for {tableName}"
            };
        }

        private string GetSqlType(string controlType)
        {
            if (Dialect == SqlDialect.SqlServer)
            {
                switch (controlType?.ToLower())
                {
                    case "textfield":
                    case "plaintext":
                        return "NVARCHAR(MAX)";
                    case "richtext":
                        return "NVARCHAR(MAX)";
                    case "datepicker":
                        return "DATETIME2";
                    case "checkbox":
                        return "BIT";
                    case "dropdown":
                    case "combobox":
                        return "NVARCHAR(255)";
                    case "fileattachment":
                        return "VARBINARY(MAX)";
                    case "peoplepicker":
                        return "NVARCHAR(500)";
                    case "number":
                        return "DECIMAL(18,4)";
                    default:
                        return "NVARCHAR(MAX)";
                }
            }

            // Add other dialects as needed
            return "VARCHAR(MAX)";
        }

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
            string[] reservedWords = { "User", "Date", "Time", "Table", "Index", "Key", "Primary", "Foreign", "References" };
            if (reservedWords.Contains(sanitized, StringComparer.OrdinalIgnoreCase))
                sanitized = sanitized + "_Field";

            // Limit length
            if (sanitized.Length > 128)
                sanitized = sanitized.Substring(0, 128);

            return string.IsNullOrEmpty(sanitized) ? "Column" : sanitized;
        }
    }
}