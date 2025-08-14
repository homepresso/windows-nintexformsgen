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
    /// Enhanced SQL generation service with proper repeating section support and Q&A structure
    /// </summary>
    public class SqlGeneratorService : ISqlGenerator
    {
        public SqlDialect Dialect { get; set; } = SqlDialect.SqlServer;

        public async Task<SqlGenerationResult> GenerateFromAnalysisAsync(FormAnalysisResult analysis)
        {
            return await GenerateFromAnalysisAsync(analysis, TableStructureType.FlatTables);
        }

        public async Task<SqlGenerationResult> GenerateFromAnalysisAsync(FormAnalysisResult analysis, TableStructureType? structureType)
        {
            var result = new SqlGenerationResult
            {
                Dialect = Dialect,
                GeneratedDate = DateTime.Now,
                StructureType = structureType ?? TableStructureType.FlatTables
            };

            try
            {
                if (analysis?.FormDefinition == null)
                {
                    throw new ArgumentException("Invalid analysis result");
                }

                // Choose generation method based on structure type
                if (structureType == TableStructureType.NormalizedQA)
                {
                    // Use the Q&A normalized structure
                    return await GenerateNormalizedQAStructure(analysis.FormDefinition);
                }
                else
                {
                    // Use the flat table structure (default)
                    await Task.Run(() =>
                    {
                        var formDef = analysis.FormDefinition;
                        var scripts = new List<SqlScript>();

                        // Analyze the form structure to identify repeating sections
                        var repeatingSectionAnalysis = AnalyzeRepeatingSections(formDef);

                        // Generate drop statements first (for re-deployment)
                        scripts.Add(GenerateDropStatements(formDef, repeatingSectionAnalysis));

                        // Generate main form table
                        scripts.Add(GenerateMainTable(formDef, repeatingSectionAnalysis));

                        // Generate lookup tables for dropdowns
                        scripts.AddRange(GenerateLookupTables(formDef));

                        // Generate tables for repeating sections
                        scripts.AddRange(GenerateRepeatingTables(formDef, repeatingSectionAnalysis));

                        // Generate stored procedures
                        scripts.AddRange(GenerateStoredProcedures(formDef, repeatingSectionAnalysis));

                        // Generate views
                        scripts.Add(GenerateMainView(formDef, repeatingSectionAnalysis));

                        // Generate indexes
                        scripts.Add(GenerateIndexes(formDef, repeatingSectionAnalysis));

                        // Generate table types for bulk operations
                        scripts.AddRange(GenerateTableTypes(formDef, repeatingSectionAnalysis));

                        // Generate repeating section procedures
                        scripts.AddRange(GenerateRepeatingSectionProcedures(formDef, repeatingSectionAnalysis));

                        // Order scripts by execution order
                        result.Scripts = scripts.OrderBy(s => s.ExecutionOrder).ToList();
                    });

                    result.Success = true;
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Generate the complete normalized Q&A structure
        /// </summary>
        private async Task<SqlGenerationResult> GenerateNormalizedQAStructure(InfoPathFormDefinition formDef)
        {
            var result = new SqlGenerationResult
            {
                Dialect = this.Dialect,
                StructureType = TableStructureType.NormalizedQA,
                GeneratedDate = DateTime.Now
            };

            try
            {
                await Task.Run(() =>
                {
                    var scripts = new List<SqlScript>();
                    var analysis = AnalyzeRepeatingSections(formDef);

                    // Split table creation into separate scripts to ensure proper ordering

                    // 1. Drop existing Q&A objects
                    scripts.Add(GenerateNormalizedDropStatements());

                    // 2. Create base tables without foreign keys to avoid circular dependencies
                    scripts.Add(GenerateNormalizedBaseTables());

                    // 3. Create lookup table separately
                    scripts.Add(GenerateLookupTableOnly());

                    // 4. Add all foreign key constraints after all tables exist
                    scripts.Add(GenerateNormalizedForeignKeys());

                    // 5. Create form registration stored procedures
                    scripts.Add(GenerateFormRegistrationProcedures());

                    // 6. Register this specific form
                    scripts.Add(GenerateFormRegistrationScript(formDef, analysis));

                    // 7. Create generic submission procedures
                    scripts.Add(GenerateNormalizedSubmissionProcedures());

                    // 8. Create form-specific procedures (THIS IS THE KEY ADDITION)
                    scripts.Add(GenerateFormSpecificProcedures(formDef, analysis));

                    // 9. Create retrieval procedures
                    scripts.Add(GenerateNormalizedRetrievalProcedures());

                    // 10. Create reporting views
                    scripts.Add(GenerateNormalizedReportingViews());

                    // 11. Create indexes for performance
                    scripts.Add(GenerateNormalizedIndexes());

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
        /// Create base tables without foreign keys to LookupValues
        /// </summary>
        private SqlScript GenerateNormalizedBaseTables()
        {
            var sb = new StringBuilder();

            sb.AppendLine("-- ===============================================");
            sb.AppendLine("-- Base Tables for Normalized Q&A Structure (without FK to LookupValues)");
            sb.AppendLine("-- ===============================================");
            sb.AppendLine();

            // Forms table - stores form definitions
            sb.AppendLine("-- Forms table (all form definitions)");
            sb.AppendLine("IF OBJECT_ID('dbo.Forms', 'U') IS NOT NULL DROP TABLE [dbo].[Forms];");
            sb.AppendLine("CREATE TABLE [dbo].[Forms] (");
            sb.AppendLine("    [FormId] INT IDENTITY(1,1) NOT NULL,");
            sb.AppendLine("    [FormName] NVARCHAR(255) NOT NULL,");
            sb.AppendLine("    [FormFileName] NVARCHAR(255) NULL,");
            sb.AppendLine("    [FormVersion] NVARCHAR(50) NULL,");
            sb.AppendLine("    [IsActive] BIT DEFAULT 1 NOT NULL,");
            sb.AppendLine("    [CreatedDate] DATETIME2(3) DEFAULT GETDATE() NOT NULL,");
            sb.AppendLine("    [ModifiedDate] DATETIME2(3) DEFAULT GETDATE() NOT NULL,");
            sb.AppendLine("    CONSTRAINT [PK_Forms] PRIMARY KEY CLUSTERED ([FormId] ASC),");
            sb.AppendLine("    CONSTRAINT [UQ_Forms_Name] UNIQUE ([FormName])");
            sb.AppendLine(");");
            sb.AppendLine();

            // Questions table - stores all form fields/questions
            sb.AppendLine("-- Questions table (all form fields across all forms)");
            sb.AppendLine("IF OBJECT_ID('dbo.Questions', 'U') IS NOT NULL DROP TABLE [dbo].[Questions];");
            sb.AppendLine("CREATE TABLE [dbo].[Questions] (");
            sb.AppendLine("    [QuestionId] INT IDENTITY(1,1) NOT NULL,");
            sb.AppendLine("    [FormId] INT NOT NULL,");
            sb.AppendLine("    [FieldName] NVARCHAR(255) NOT NULL,");
            sb.AppendLine("    [DisplayName] NVARCHAR(500) NULL,");
            sb.AppendLine("    [FieldType] NVARCHAR(50) NOT NULL,");
            sb.AppendLine("    [SectionName] NVARCHAR(255) NULL,");
            sb.AppendLine("    [IsInRepeatingSection] BIT DEFAULT 0 NOT NULL,");
            sb.AppendLine("    [RepeatingSectionName] NVARCHAR(255) NULL,");
            sb.AppendLine("    [IsConditional] BIT DEFAULT 0 NOT NULL,");
            sb.AppendLine("    [ConditionalOnField] NVARCHAR(255) NULL,");
            sb.AppendLine("    [DisplayOrder] INT DEFAULT 0 NOT NULL,");
            sb.AppendLine("    [IsRequired] BIT DEFAULT 0 NOT NULL,");
            sb.AppendLine("    [DefaultValue] NVARCHAR(MAX) NULL,");
            sb.AppendLine("    [ValidationRule] NVARCHAR(MAX) NULL,");
            sb.AppendLine("    [HasLookupValues] BIT DEFAULT 0 NOT NULL,");
            sb.AppendLine("    [CreatedDate] DATETIME2(3) DEFAULT GETDATE() NOT NULL,");
            sb.AppendLine("    CONSTRAINT [PK_Questions] PRIMARY KEY CLUSTERED ([QuestionId] ASC),");
            sb.AppendLine("    CONSTRAINT [FK_Questions_Forms] FOREIGN KEY ([FormId]) REFERENCES [dbo].[Forms] ([FormId]),");
            sb.AppendLine("    CONSTRAINT [UQ_Questions_FormField] UNIQUE ([FormId], [FieldName], [RepeatingSectionName])");
            sb.AppendLine(");");
            sb.AppendLine();

            // Repeating Sections table
            sb.AppendLine("-- RepeatingSections table (defines all repeating sections)");
            sb.AppendLine("IF OBJECT_ID('dbo.RepeatingSections', 'U') IS NOT NULL DROP TABLE [dbo].[RepeatingSections];");
            sb.AppendLine("CREATE TABLE [dbo].[RepeatingSections] (");
            sb.AppendLine("    [SectionId] INT IDENTITY(1,1) NOT NULL,");
            sb.AppendLine("    [FormId] INT NOT NULL,");
            sb.AppendLine("    [SectionName] NVARCHAR(255) NOT NULL,");
            sb.AppendLine("    [SectionType] NVARCHAR(50) DEFAULT 'repeating' NOT NULL,");
            sb.AppendLine("    [ParentSectionId] INT NULL,");
            sb.AppendLine("    [MinItems] INT DEFAULT 0,");
            sb.AppendLine("    [MaxItems] INT NULL,");
            sb.AppendLine("    [CreatedDate] DATETIME2(3) DEFAULT GETDATE() NOT NULL,");
            sb.AppendLine("    CONSTRAINT [PK_RepeatingSections] PRIMARY KEY CLUSTERED ([SectionId] ASC),");
            sb.AppendLine("    CONSTRAINT [FK_RepeatingSections_Forms] FOREIGN KEY ([FormId]) REFERENCES [dbo].[Forms] ([FormId]),");
            sb.AppendLine("    CONSTRAINT [FK_RepeatingSections_Parent] FOREIGN KEY ([ParentSectionId]) REFERENCES [dbo].[RepeatingSections] ([SectionId]),");
            sb.AppendLine("    CONSTRAINT [UQ_RepeatingSections] UNIQUE ([FormId], [SectionName])");
            sb.AppendLine(");");
            sb.AppendLine();

            // Submissions table
            sb.AppendLine("-- Submissions table (all form submissions)");
            sb.AppendLine("IF OBJECT_ID('dbo.Submissions', 'U') IS NOT NULL DROP TABLE [dbo].[Submissions];");
            sb.AppendLine("CREATE TABLE [dbo].[Submissions] (");
            sb.AppendLine("    [SubmissionId] INT IDENTITY(1,1) NOT NULL,");
            sb.AppendLine("    [SubmissionGuid] UNIQUEIDENTIFIER DEFAULT NEWID() NOT NULL,");
            sb.AppendLine("    [FormId] INT NOT NULL,");
            sb.AppendLine("    [SubmittedBy] NVARCHAR(255) NULL,");
            sb.AppendLine("    [SubmissionStatus] NVARCHAR(50) DEFAULT 'Draft' NOT NULL,");
            sb.AppendLine("    [SubmittedDate] DATETIME2(3) DEFAULT GETDATE() NOT NULL,");
            sb.AppendLine("    [ModifiedDate] DATETIME2(3) DEFAULT GETDATE() NOT NULL,");
            sb.AppendLine("    [Version] INT DEFAULT 1 NOT NULL,");
            sb.AppendLine("    CONSTRAINT [PK_Submissions] PRIMARY KEY CLUSTERED ([SubmissionId] ASC),");
            sb.AppendLine("    CONSTRAINT [FK_Submissions_Forms] FOREIGN KEY ([FormId]) REFERENCES [dbo].[Forms] ([FormId]),");
            sb.AppendLine("    CONSTRAINT [UQ_Submissions_Guid] UNIQUE ([SubmissionGuid])");
            sb.AppendLine(");");
            sb.AppendLine();

            // Repeating Section Instances table
            sb.AppendLine("-- RepeatingSectionInstances table (tracks each instance of a repeating section)");
            sb.AppendLine("IF OBJECT_ID('dbo.RepeatingSectionInstances', 'U') IS NOT NULL DROP TABLE [dbo].[RepeatingSectionInstances];");
            sb.AppendLine("CREATE TABLE [dbo].[RepeatingSectionInstances] (");
            sb.AppendLine("    [InstanceId] INT IDENTITY(1,1) NOT NULL,");
            sb.AppendLine("    [SubmissionId] INT NOT NULL,");
            sb.AppendLine("    [SectionId] INT NOT NULL,");
            sb.AppendLine("    [ParentInstanceId] INT NULL,");
            sb.AppendLine("    [ItemOrder] INT NOT NULL DEFAULT 0,");
            sb.AppendLine("    [CreatedDate] DATETIME2(3) DEFAULT GETDATE() NOT NULL,");
            sb.AppendLine("    CONSTRAINT [PK_RepeatingSectionInstances] PRIMARY KEY CLUSTERED ([InstanceId] ASC),");
            sb.AppendLine("    CONSTRAINT [FK_RSI_Submissions] FOREIGN KEY ([SubmissionId]) REFERENCES [dbo].[Submissions] ([SubmissionId]) ON DELETE CASCADE,");
            sb.AppendLine("    CONSTRAINT [FK_RSI_Sections] FOREIGN KEY ([SectionId]) REFERENCES [dbo].[RepeatingSections] ([SectionId]),");
            sb.AppendLine("    CONSTRAINT [FK_RSI_Parent] FOREIGN KEY ([ParentInstanceId]) REFERENCES [dbo].[RepeatingSectionInstances] ([InstanceId])");
            sb.AppendLine(");");
            sb.AppendLine();

            // Answers table WITHOUT foreign key to LookupValues
            sb.AppendLine("-- Answers table (without FK to LookupValues yet)");
            sb.AppendLine("IF OBJECT_ID('dbo.Answers', 'U') IS NOT NULL DROP TABLE [dbo].[Answers];");
            sb.AppendLine("CREATE TABLE [dbo].[Answers] (");
            sb.AppendLine("    [AnswerId] INT IDENTITY(1,1) NOT NULL,");
            sb.AppendLine("    [SubmissionId] INT NOT NULL,");
            sb.AppendLine("    [QuestionId] INT NOT NULL,");
            sb.AppendLine("    [RepeatingSectionInstanceId] INT NULL,");
            sb.AppendLine("    [AnswerText] NVARCHAR(MAX) NULL,");
            sb.AppendLine("    [AnswerNumeric] DECIMAL(18,4) NULL,");
            sb.AppendLine("    [AnswerDate] DATETIME2(3) NULL,");
            sb.AppendLine("    [AnswerBit] BIT NULL,");
            sb.AppendLine("    [AnswerLookupId] INT NULL,"); // Will add FK later
            sb.AppendLine("    [CreatedDate] DATETIME2(3) DEFAULT GETDATE() NOT NULL,");
            sb.AppendLine("    [ModifiedDate] DATETIME2(3) DEFAULT GETDATE() NOT NULL,");
            sb.AppendLine("    CONSTRAINT [PK_Answers] PRIMARY KEY CLUSTERED ([AnswerId] ASC),");
            sb.AppendLine("    CONSTRAINT [FK_Answers_Submissions] FOREIGN KEY ([SubmissionId]) REFERENCES [dbo].[Submissions] ([SubmissionId]) ON DELETE CASCADE,");
            sb.AppendLine("    CONSTRAINT [FK_Answers_Questions] FOREIGN KEY ([QuestionId]) REFERENCES [dbo].[Questions] ([QuestionId]),");
            sb.AppendLine("    CONSTRAINT [FK_Answers_RSI] FOREIGN KEY ([RepeatingSectionInstanceId]) REFERENCES [dbo].[RepeatingSectionInstances] ([InstanceId])");
            sb.AppendLine(");");
            sb.AppendLine();

            return new SqlScript
            {
                Name = "Create_Normalized_Base_Tables",
                Type = ScriptType.Table,
                Content = sb.ToString(),
                ExecutionOrder = 1,
                Description = "Base tables for normalized Q&A structure without LookupValues FK"
            };
        }

        /// <summary>
        /// Create lookup table only
        /// </summary>
        private SqlScript GenerateLookupTableOnly()
        {
            var sb = new StringBuilder();

            sb.AppendLine("-- ===============================================");
            sb.AppendLine("-- Lookup Table for All Dropdown Values");
            sb.AppendLine("-- ===============================================");
            sb.AppendLine();

            sb.AppendLine("IF OBJECT_ID('dbo.LookupValues', 'U') IS NOT NULL DROP TABLE [dbo].[LookupValues];");
            sb.AppendLine("CREATE TABLE [dbo].[LookupValues] (");
            sb.AppendLine("    [LookupId] INT IDENTITY(1,1) NOT NULL,");
            sb.AppendLine("    [FormId] INT NULL,");
            sb.AppendLine("    [QuestionId] INT NULL,");
            sb.AppendLine("    [LookupCategory] NVARCHAR(255) NULL,");
            sb.AppendLine("    [LookupValue] NVARCHAR(500) NOT NULL,");
            sb.AppendLine("    [LookupDisplayText] NVARCHAR(500) NOT NULL,");
            sb.AppendLine("    [LookupCode] NVARCHAR(50) NULL,");
            sb.AppendLine("    [ParentLookupId] INT NULL,");
            sb.AppendLine("    [SortOrder] INT DEFAULT 0 NOT NULL,");
            sb.AppendLine("    [IsActive] BIT DEFAULT 1 NOT NULL,");
            sb.AppendLine("    [IsDefault] BIT DEFAULT 0 NOT NULL,");
            sb.AppendLine("    [CreatedDate] DATETIME2(3) DEFAULT GETDATE() NOT NULL,");
            sb.AppendLine("    [ModifiedDate] DATETIME2(3) DEFAULT GETDATE() NOT NULL,");
            sb.AppendLine("    CONSTRAINT [PK_LookupValues] PRIMARY KEY CLUSTERED ([LookupId] ASC),");
            sb.AppendLine("    CONSTRAINT [FK_LookupValues_Forms] FOREIGN KEY ([FormId]) REFERENCES [dbo].[Forms] ([FormId]),");
            sb.AppendLine("    CONSTRAINT [FK_LookupValues_Questions] FOREIGN KEY ([QuestionId]) REFERENCES [dbo].[Questions] ([QuestionId]),");
            sb.AppendLine("    CONSTRAINT [FK_LookupValues_Parent] FOREIGN KEY ([ParentLookupId]) REFERENCES [dbo].[LookupValues] ([LookupId])");
            sb.AppendLine(");");
            sb.AppendLine();

            return new SqlScript
            {
                Name = "Create_Lookup_Table",
                Type = ScriptType.Table,
                Content = sb.ToString(),
                ExecutionOrder = 2,
                Description = "Lookup table for all dropdown values"
            };
        }

        /// <summary>
        /// Add foreign key constraints after all tables exist
        /// </summary>
        private SqlScript GenerateNormalizedForeignKeys()
        {
            var sb = new StringBuilder();

            sb.AppendLine("-- ===============================================");
            sb.AppendLine("-- Add Foreign Key Constraints");
            sb.AppendLine("-- ===============================================");
            sb.AppendLine();

            sb.AppendLine("-- Add foreign key from Answers to LookupValues");
            sb.AppendLine("ALTER TABLE [dbo].[Answers]");
            sb.AppendLine("ADD CONSTRAINT [FK_Answers_Lookup] FOREIGN KEY ([AnswerLookupId])");
            sb.AppendLine("REFERENCES [dbo].[LookupValues] ([LookupId]);");
            sb.AppendLine();

            return new SqlScript
            {
                Name = "Add_Foreign_Keys",
                Type = ScriptType.Table,
                Content = sb.ToString(),
                ExecutionOrder = 3,
                Description = "Add foreign key constraints after all tables are created"
            };
        }

        /// <summary>
        /// Generate drop statements for normalized Q&A structure
        /// </summary>
        private SqlScript GenerateNormalizedDropStatements()
        {
            var sb = new StringBuilder();

            sb.AppendLine("-- ===============================================");
            sb.AppendLine("-- Drop existing Q&A structure objects if they exist");
            sb.AppendLine("-- ===============================================");
            sb.AppendLine();

            // Drop views first
            sb.AppendLine("IF OBJECT_ID('dbo.vw_FormAnswersPivot', 'V') IS NOT NULL DROP VIEW [dbo].[vw_FormAnswersPivot];");
            sb.AppendLine("IF OBJECT_ID('dbo.vw_AllSubmissions', 'V') IS NOT NULL DROP VIEW [dbo].[vw_AllSubmissions];");
            sb.AppendLine();

            // Drop stored procedures
            var procedures = new[] {
                "sp_GetSubmissionData", "sp_AddRepeatingSectionInstance",
                "sp_SubmitFormData", "sp_RegisterForm", "sp_UpdateSubmission",
                "sp_DeleteSubmission", "sp_ListSubmissions"
            };
            foreach (var proc in procedures)
            {
                sb.AppendLine($"IF OBJECT_ID('dbo.{proc}', 'P') IS NOT NULL DROP PROCEDURE [dbo].[{proc}];");
            }
            sb.AppendLine();

            // Drop foreign key constraints first
            sb.AppendLine("-- Drop foreign key constraints");
            sb.AppendLine("IF OBJECT_ID('dbo.FK_Answers_RSI', 'F') IS NOT NULL ALTER TABLE [dbo].[Answers] DROP CONSTRAINT [FK_Answers_RSI];");
            sb.AppendLine("IF OBJECT_ID('dbo.FK_Answers_Lookup', 'F') IS NOT NULL ALTER TABLE [dbo].[Answers] DROP CONSTRAINT [FK_Answers_Lookup];");
            sb.AppendLine("IF OBJECT_ID('dbo.FK_Answers_Questions', 'F') IS NOT NULL ALTER TABLE [dbo].[Answers] DROP CONSTRAINT [FK_Answers_Questions];");
            sb.AppendLine("IF OBJECT_ID('dbo.FK_Answers_Submissions', 'F') IS NOT NULL ALTER TABLE [dbo].[Answers] DROP CONSTRAINT [FK_Answers_Submissions];");
            sb.AppendLine();

            // Drop tables in correct order (due to foreign keys)
            sb.AppendLine("-- Drop tables");
            sb.AppendLine("IF OBJECT_ID('dbo.Answers', 'U') IS NOT NULL DROP TABLE [dbo].[Answers];");
            sb.AppendLine("IF OBJECT_ID('dbo.RepeatingSectionInstances', 'U') IS NOT NULL DROP TABLE [dbo].[RepeatingSectionInstances];");
            sb.AppendLine("IF OBJECT_ID('dbo.LookupValues', 'U') IS NOT NULL DROP TABLE [dbo].[LookupValues];");
            sb.AppendLine("IF OBJECT_ID('dbo.Questions', 'U') IS NOT NULL DROP TABLE [dbo].[Questions];");
            sb.AppendLine("IF OBJECT_ID('dbo.RepeatingSections', 'U') IS NOT NULL DROP TABLE [dbo].[RepeatingSections];");
            sb.AppendLine("IF OBJECT_ID('dbo.Submissions', 'U') IS NOT NULL DROP TABLE [dbo].[Submissions];");
            sb.AppendLine("IF OBJECT_ID('dbo.Forms', 'U') IS NOT NULL DROP TABLE [dbo].[Forms];");
            sb.AppendLine();

            return new SqlScript
            {
                Name = "Drop_QA_Structure_Objects",
                Type = ScriptType.Table,
                Content = sb.ToString(),
                ExecutionOrder = 0,
                Description = "Drop existing Q&A structure objects for clean deployment"
            };
        }

        /// <summary>
        /// Generate script to register the specific form and its structure
        /// </summary>
        private SqlScript GenerateFormRegistrationScript(InfoPathFormDefinition formDef, RepeatingSectionAnalysis analysis)
        {
            var sb = new StringBuilder();
            var formName = SanitizeTableName(formDef.FormName);

            sb.AppendLine("-- ===============================================");
            sb.AppendLine($"-- Register Form: {formName}");
            sb.AppendLine("-- ===============================================");
            sb.AppendLine();

            sb.AppendLine("DECLARE @FormId INT;");
            sb.AppendLine("DECLARE @QuestionId INT;");
            sb.AppendLine("DECLARE @SectionId INT;");
            sb.AppendLine();

            // Insert or update the form
            sb.AppendLine($"-- Register form: {formName}");
            sb.AppendLine($"IF NOT EXISTS (SELECT 1 FROM [dbo].[Forms] WHERE FormName = N'{formName}')");
            sb.AppendLine("BEGIN");
            sb.AppendLine($"    INSERT INTO [dbo].[Forms] (FormName, FormFileName, FormVersion)");
            sb.AppendLine($"    VALUES (N'{formName}', N'{formDef.FileName ?? formName}', '1.0');");
            sb.AppendLine("    SET @FormId = SCOPE_IDENTITY();");
            sb.AppendLine("END");
            sb.AppendLine("ELSE");
            sb.AppendLine("BEGIN");
            sb.AppendLine($"    SELECT @FormId = FormId FROM [dbo].[Forms] WHERE FormName = N'{formName}';");
            sb.AppendLine($"    UPDATE [dbo].[Forms] SET ModifiedDate = GETDATE() WHERE FormId = @FormId;");
            sb.AppendLine("END");
            sb.AppendLine();

            // Register repeating sections
            if (analysis.RepeatingSections.Any())
            {
                sb.AppendLine("-- Register repeating sections");
                foreach (var section in analysis.RepeatingSections)
                {
                    var sectionName = section.Key.Replace("'", "''");
                    sb.AppendLine($"IF NOT EXISTS (SELECT 1 FROM [dbo].[RepeatingSections] WHERE FormId = @FormId AND SectionName = N'{sectionName}')");
                    sb.AppendLine("BEGIN");
                    sb.AppendLine($"    INSERT INTO [dbo].[RepeatingSections] (FormId, SectionName, SectionType)");
                    sb.AppendLine($"    VALUES (@FormId, N'{sectionName}', 'repeating');");
                    sb.AppendLine("END");
                    sb.AppendLine();
                }
            }

            // Register questions (main table fields)
            if (analysis.MainTableColumns.Any())
            {
                sb.AppendLine("-- Register main form questions");
                int displayOrder = 0;
                foreach (var control in analysis.MainTableColumns.OrderBy(c => c.Name))
                {
                    var fieldName = SanitizeColumnName(control.Name);
                    var displayName = (control.Label ?? control.Name).Replace("'", "''");
                    var fieldType = control.Type;
                    var isRequired = control.Properties?.ContainsKey("Required") == true &&
                                   control.Properties["Required"] == "true" ? "1" : "0";
                    var hasLookup = control.HasStaticData && control.DataOptions != null && control.DataOptions.Any() ? "1" : "0";

                    sb.AppendLine($"-- Question: {fieldName}");
                    sb.AppendLine($"IF NOT EXISTS (SELECT 1 FROM [dbo].[Questions] WHERE FormId = @FormId AND FieldName = N'{fieldName}' AND RepeatingSectionName IS NULL)");
                    sb.AppendLine("BEGIN");
                    sb.AppendLine($"    INSERT INTO [dbo].[Questions] (FormId, FieldName, DisplayName, FieldType, DisplayOrder, IsRequired, HasLookupValues, IsInRepeatingSection)");
                    sb.AppendLine($"    VALUES (@FormId, N'{fieldName}', N'{displayName}', N'{fieldType}', {displayOrder}, {isRequired}, {hasLookup}, 0);");

                    if (hasLookup == "1")
                    {
                        sb.AppendLine("    SET @QuestionId = SCOPE_IDENTITY();");
                        sb.AppendLine();
                        sb.AppendLine($"    -- Insert lookup values for {fieldName}");
                        foreach (var option in control.DataOptions.OrderBy(o => o.Order))
                        {
                            var value = option.Value.Replace("'", "''");
                            var displayText = option.DisplayText.Replace("'", "''");
                            var isDefault = option.IsDefault ? "1" : "0";

                            sb.AppendLine($"    INSERT INTO [dbo].[LookupValues] (FormId, QuestionId, LookupCategory, LookupValue, LookupDisplayText, SortOrder, IsDefault)");
                            sb.AppendLine($"    VALUES (@FormId, @QuestionId, N'{fieldName}', N'{value}', N'{displayText}', {option.Order}, {isDefault});");
                        }
                    }

                    sb.AppendLine("END");
                    sb.AppendLine();
                    displayOrder++;
                }
            }

            // Register questions in repeating sections
            foreach (var section in analysis.RepeatingSections)
            {
                if (section.Value.Controls.Any())
                {
                    var sectionName = section.Key.Replace("'", "''");
                    sb.AppendLine($"-- Register questions for repeating section: {sectionName}");

                    int displayOrder = 0;
                    foreach (var control in section.Value.Controls.OrderBy(c => c.Name))
                    {
                        var fieldName = SanitizeColumnName(control.Name);
                        var displayName = (control.Label ?? control.Name).Replace("'", "''");
                        var fieldType = control.Type;
                        var isRequired = control.Properties?.ContainsKey("Required") == true &&
                                       control.Properties["Required"] == "true" ? "1" : "0";
                        var hasLookup = control.HasStaticData && control.DataOptions != null && control.DataOptions.Any() ? "1" : "0";

                        sb.AppendLine($"-- Repeating section question: {fieldName}");
                        sb.AppendLine($"IF NOT EXISTS (SELECT 1 FROM [dbo].[Questions] WHERE FormId = @FormId AND FieldName = N'{fieldName}' AND RepeatingSectionName = N'{sectionName}')");
                        sb.AppendLine("BEGIN");
                        sb.AppendLine($"    INSERT INTO [dbo].[Questions] (FormId, FieldName, DisplayName, FieldType, RepeatingSectionName, DisplayOrder, IsRequired, HasLookupValues, IsInRepeatingSection)");
                        sb.AppendLine($"    VALUES (@FormId, N'{fieldName}', N'{displayName}', N'{fieldType}', N'{sectionName}', {displayOrder}, {isRequired}, {hasLookup}, 1);");

                        if (hasLookup == "1" && control.DataOptions != null)
                        {
                            sb.AppendLine("    SET @QuestionId = SCOPE_IDENTITY();");
                            sb.AppendLine();
                            sb.AppendLine($"    -- Insert lookup values for {fieldName}");
                            foreach (var option in control.DataOptions.OrderBy(o => o.Order))
                            {
                                var value = option.Value.Replace("'", "''");
                                var displayText = option.DisplayText.Replace("'", "''");
                                var isDefault = option.IsDefault ? "1" : "0";

                                sb.AppendLine($"    INSERT INTO [dbo].[LookupValues] (FormId, QuestionId, LookupCategory, LookupValue, LookupDisplayText, SortOrder, IsDefault)");
                                sb.AppendLine($"    VALUES (@FormId, @QuestionId, N'{sectionName}_{fieldName}', N'{value}', N'{displayText}', {option.Order}, {isDefault});");
                            }
                        }

                        sb.AppendLine("END");
                        sb.AppendLine();
                        displayOrder++;
                    }
                }
            }

            sb.AppendLine($"PRINT 'Form registration completed for: {formName}';");

            return new SqlScript
            {
                Name = $"Register_{formName}_Form",
                Type = ScriptType.Data,
                Content = sb.ToString(),
                ExecutionOrder = 50,
                Description = $"Register form structure for {formName}"
            };
        }

        /// <summary>
        /// Generate form-specific stored procedures for submission and retrieval
        /// </summary>
        private SqlScript GenerateFormSpecificProcedures(InfoPathFormDefinition formDef, RepeatingSectionAnalysis analysis)
        {
            var sb = new StringBuilder();
            var formName = SanitizeTableName(formDef.FormName);

            sb.AppendLine("-- ===============================================");
            sb.AppendLine($"-- Form-Specific Procedures for: {formName}");
            sb.AppendLine("-- ===============================================");
            sb.AppendLine();

            // 3. Create table types for repeating sections FIRST (before procedures)
            foreach (var section in analysis.RepeatingSections)
            {
                var sectionName = SanitizeTableName(section.Key);

                sb.AppendLine($"-- Table type for {section.Key} repeating section");
                sb.AppendLine($"IF TYPE_ID(N'[dbo].[{formName}_{sectionName}_TableType]') IS NOT NULL");
                sb.AppendLine($"    DROP TYPE [dbo].[{formName}_{sectionName}_TableType];");
                sb.AppendLine("GO");
                sb.AppendLine();

                sb.AppendLine($"CREATE TYPE [dbo].[{formName}_{sectionName}_TableType] AS TABLE (");
                sb.AppendLine("    [ItemOrder] INT NOT NULL");

                // Track column names to ensure uniqueness
                var usedColumnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                usedColumnNames.Add("ItemOrder");

                foreach (var control in section.Value.Controls.OrderBy(c => c.Name))
                {
                    var colName = SanitizeColumnName(control.Name);

                    // Ensure column name is unique
                    var uniqueColName = colName;
                    int suffix = 1;
                    while (usedColumnNames.Contains(uniqueColName))
                    {
                        uniqueColName = $"{colName}_{suffix}";
                        suffix++;
                    }
                    usedColumnNames.Add(uniqueColName);

                    var sqlType = GetSqlType(control.Type);
                    sb.AppendLine($"    ,[{uniqueColName}] {sqlType} NULL -- {control.Label ?? control.Name}");
                }

                sb.AppendLine(");");
                sb.AppendLine("GO");
                sb.AppendLine();
            }

            // NOW create the submit procedure that uses these table types
            sb.AppendLine($"-- Submit procedure for {formName}");
            sb.AppendLine($"CREATE PROCEDURE [dbo].[sp_Submit_{formName}]");
            sb.AppendLine("    @SubmissionGuid UNIQUEIDENTIFIER OUTPUT,");
            sb.AppendLine("    @SubmittedBy NVARCHAR(255) = NULL,");
            sb.AppendLine("    @Status NVARCHAR(50) = 'Draft'");

            // Add parameters for each main field
            foreach (var control in analysis.MainTableColumns.OrderBy(c => c.Name))
            {
                var paramName = SanitizeColumnName(control.Name);
                var sqlType = GetSqlType(control.Type);
                sb.AppendLine($"    ,@{paramName} {sqlType} = NULL");
            }

            // Add table-valued parameters for each repeating section
            foreach (var section in analysis.RepeatingSections)
            {
                var sectionName = SanitizeTableName(section.Key);
                sb.AppendLine($"    ,@{sectionName}Data [dbo].[{formName}_{sectionName}_TableType] READONLY");
            }

            sb.AppendLine("AS");
            sb.AppendLine("BEGIN");
            sb.AppendLine("    SET NOCOUNT ON;");
            sb.AppendLine();
            sb.AppendLine("    DECLARE @FormId INT;");
            sb.AppendLine("    DECLARE @SubmissionId INT;");
            sb.AppendLine("    DECLARE @QuestionId INT;");
            sb.AppendLine();
            sb.AppendLine("    BEGIN TRY");
            sb.AppendLine("        BEGIN TRANSACTION;");
            sb.AppendLine();
            sb.AppendLine("        -- Get FormId");
            sb.AppendLine($"        SELECT @FormId = FormId FROM [dbo].[Forms] WHERE FormName = N'{formName}';");
            sb.AppendLine();
            sb.AppendLine("        IF @FormId IS NULL");
            sb.AppendLine($"            RAISERROR('Form {formName} not found in database', 16, 1);");
            sb.AppendLine();
            sb.AppendLine("        -- Create submission");
            sb.AppendLine("        SET @SubmissionGuid = ISNULL(@SubmissionGuid, NEWID());");
            sb.AppendLine();
            sb.AppendLine("        INSERT INTO [dbo].[Submissions] (SubmissionGuid, FormId, SubmittedBy, SubmissionStatus)");
            sb.AppendLine("        VALUES (@SubmissionGuid, @FormId, @SubmittedBy, @Status);");
            sb.AppendLine();
            sb.AppendLine("        SET @SubmissionId = SCOPE_IDENTITY();");
            sb.AppendLine();

            // Insert answers for main fields
            sb.AppendLine("        -- Insert main form answers");
            foreach (var control in analysis.MainTableColumns.OrderBy(c => c.Name))
            {
                var paramName = SanitizeColumnName(control.Name);
                var fieldName = SanitizeColumnName(control.Name);

                sb.AppendLine($"        -- {control.Label ?? control.Name}");
                sb.AppendLine($"        IF @{paramName} IS NOT NULL");
                sb.AppendLine("        BEGIN");
                sb.AppendLine($"            SELECT @QuestionId = QuestionId FROM [dbo].[Questions] WHERE FormId = @FormId AND FieldName = N'{fieldName}' AND RepeatingSectionName IS NULL;");
                sb.AppendLine();

                switch (control.Type?.ToLower())
                {
                    case "textfield":
                    case "richtext":
                        sb.AppendLine($"            INSERT INTO [dbo].[Answers] (SubmissionId, QuestionId, AnswerText)");
                        sb.AppendLine($"            VALUES (@SubmissionId, @QuestionId, @{paramName});");
                        break;
                    case "datepicker":
                        sb.AppendLine($"            INSERT INTO [dbo].[Answers] (SubmissionId, QuestionId, AnswerDate)");
                        sb.AppendLine($"            VALUES (@SubmissionId, @QuestionId, @{paramName});");
                        break;
                    case "checkbox":
                        sb.AppendLine($"            INSERT INTO [dbo].[Answers] (SubmissionId, QuestionId, AnswerBit)");
                        sb.AppendLine($"            VALUES (@SubmissionId, @QuestionId, @{paramName});");
                        break;
                    case "number":
                    case "integer":
                    case "decimal":
                        sb.AppendLine($"            INSERT INTO [dbo].[Answers] (SubmissionId, QuestionId, AnswerNumeric)");
                        sb.AppendLine($"            VALUES (@SubmissionId, @QuestionId, @{paramName});");
                        break;
                    case "dropdown":
                    case "radiobutton":
                    case "combobox":
                        sb.AppendLine($"            -- For dropdown, store the lookup value");
                        sb.AppendLine($"            DECLARE @LookupId_{paramName} INT;");
                        sb.AppendLine($"            SELECT @LookupId_{paramName} = LookupId FROM [dbo].[LookupValues] WHERE QuestionId = @QuestionId AND LookupValue = @{paramName};");
                        sb.AppendLine($"            INSERT INTO [dbo].[Answers] (SubmissionId, QuestionId, AnswerText, AnswerLookupId)");
                        sb.AppendLine($"            VALUES (@SubmissionId, @QuestionId, @{paramName}, @LookupId_{paramName});");
                        break;
                    default:
                        sb.AppendLine($"            INSERT INTO [dbo].[Answers] (SubmissionId, QuestionId, AnswerText)");
                        sb.AppendLine($"            VALUES (@SubmissionId, @QuestionId, @{paramName});");
                        break;
                }
                sb.AppendLine("        END");
                sb.AppendLine();
            }

            // Handle repeating sections
            foreach (var section in analysis.RepeatingSections)
            {
                var sectionName = SanitizeTableName(section.Key);

                sb.AppendLine($"        -- Insert {section.Key} repeating section data");
                sb.AppendLine($"        DECLARE @SectionId_{sectionName} INT;");
                sb.AppendLine($"        SELECT @SectionId_{sectionName} = SectionId FROM [dbo].[RepeatingSections] WHERE FormId = @FormId AND SectionName = N'{section.Key}';");
                sb.AppendLine();
                sb.AppendLine($"        INSERT INTO [dbo].[RepeatingSectionInstances] (SubmissionId, SectionId, ItemOrder)");
                sb.AppendLine($"        SELECT @SubmissionId, @SectionId_{sectionName}, ItemOrder");
                sb.AppendLine($"        FROM @{sectionName}Data;");
                sb.AppendLine();

                // Insert answers for each field in the repeating section
                sb.AppendLine($"        -- Insert answers for {section.Key} fields");
                sb.AppendLine($"        INSERT INTO [dbo].[Answers] (SubmissionId, QuestionId, RepeatingSectionInstanceId, AnswerText, AnswerNumeric, AnswerDate, AnswerBit)");
                sb.AppendLine("        SELECT");
                sb.AppendLine("            @SubmissionId,");
                sb.AppendLine("            q.QuestionId,");
                sb.AppendLine("            rsi.InstanceId,");

                // Build AnswerText CASE
                var textControls = section.Value.Controls.Where(c => c.Type?.ToLower() == "textfield" || c.Type?.ToLower() == "richtext").ToList();
                if (textControls.Any())
                {
                    sb.AppendLine("            CASE");
                    foreach (var control in textControls)
                    {
                        var colName = SanitizeColumnName(control.Name);
                        sb.AppendLine($"                WHEN q.FieldName = N'{colName}' THEN td.[{colName}]");
                    }
                    sb.AppendLine("                ELSE NULL");
                    sb.AppendLine("            END,");
                }
                else
                {
                    sb.AppendLine("            NULL, -- No text fields");
                }

                // Build AnswerNumeric CASE
                var numericControls = section.Value.Controls.Where(c => c.Type?.ToLower() == "number" || c.Type?.ToLower() == "decimal" || c.Type?.ToLower() == "integer").ToList();
                if (numericControls.Any())
                {
                    sb.AppendLine("            CASE");
                    foreach (var control in numericControls)
                    {
                        var colName = SanitizeColumnName(control.Name);
                        sb.AppendLine($"                WHEN q.FieldName = N'{colName}' THEN TRY_CAST(td.[{colName}] AS DECIMAL(18,4))");
                    }
                    sb.AppendLine("                ELSE NULL");
                    sb.AppendLine("            END,");
                }
                else
                {
                    sb.AppendLine("            NULL, -- No numeric fields");
                }

                // Build AnswerDate CASE
                var dateControls = section.Value.Controls.Where(c => c.Type?.ToLower() == "datepicker").ToList();
                if (dateControls.Any())
                {
                    sb.AppendLine("            CASE");
                    foreach (var control in dateControls)
                    {
                        var colName = SanitizeColumnName(control.Name);
                        sb.AppendLine($"                WHEN q.FieldName = N'{colName}' THEN TRY_CAST(td.[{colName}] AS DATETIME2)");
                    }
                    sb.AppendLine("                ELSE NULL");
                    sb.AppendLine("            END,");
                }
                else
                {
                    sb.AppendLine("            NULL, -- No date fields");
                }

                // Build AnswerBit CASE
                var bitControls = section.Value.Controls.Where(c => c.Type?.ToLower() == "checkbox").ToList();
                if (bitControls.Any())
                {
                    sb.AppendLine("            CASE");
                    foreach (var control in bitControls)
                    {
                        var colName = SanitizeColumnName(control.Name);
                        sb.AppendLine($"                WHEN q.FieldName = N'{colName}' THEN TRY_CAST(td.[{colName}] AS BIT)");
                    }
                    sb.AppendLine("                ELSE NULL");
                    sb.AppendLine("            END");
                }
                else
                {
                    sb.AppendLine("            NULL -- No checkbox fields");
                }

                sb.AppendLine($"        FROM @{sectionName}Data td");
                sb.AppendLine($"        CROSS JOIN [dbo].[Questions] q");
                sb.AppendLine($"        INNER JOIN [dbo].[RepeatingSectionInstances] rsi ON rsi.SubmissionId = @SubmissionId AND rsi.SectionId = @SectionId_{sectionName} AND rsi.ItemOrder = td.ItemOrder");
                sb.AppendLine($"        WHERE q.FormId = @FormId AND q.RepeatingSectionName = N'{section.Key}';");
                sb.AppendLine();
            }

            sb.AppendLine("        COMMIT TRANSACTION;");
            sb.AppendLine();
            sb.AppendLine($"        PRINT 'Submission created successfully for {formName}. SubmissionGuid: ' + CAST(@SubmissionGuid AS NVARCHAR(50));");
            sb.AppendLine();
            sb.AppendLine("    END TRY");
            sb.AppendLine("    BEGIN CATCH");
            sb.AppendLine("        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;");
            sb.AppendLine("        THROW;");
            sb.AppendLine("    END CATCH");
            sb.AppendLine("END");
            sb.AppendLine("GO");
            sb.AppendLine();

            // 2. Get procedure for this specific form
            sb.AppendLine($"-- Get procedure for {formName}");
            sb.AppendLine($"CREATE PROCEDURE [dbo].[sp_Get_{formName}]");
            sb.AppendLine("    @SubmissionGuid UNIQUEIDENTIFIER");
            sb.AppendLine("AS");
            sb.AppendLine("BEGIN");
            sb.AppendLine("    SET NOCOUNT ON;");
            sb.AppendLine();
            sb.AppendLine("    DECLARE @SubmissionId INT;");
            sb.AppendLine("    DECLARE @FormId INT;");
            sb.AppendLine();
            sb.AppendLine($"    SELECT @FormId = FormId FROM [dbo].[Forms] WHERE FormName = N'{formName}';");
            sb.AppendLine("    SELECT @SubmissionId = SubmissionId FROM [dbo].[Submissions] WHERE SubmissionGuid = @SubmissionGuid AND FormId = @FormId;");
            sb.AppendLine();
            sb.AppendLine("    IF @SubmissionId IS NULL");
            sb.AppendLine("    BEGIN");
            sb.AppendLine($"        RAISERROR('Submission not found for form {formName}', 16, 1);");
            sb.AppendLine("        RETURN;");
            sb.AppendLine("    END");
            sb.AppendLine();

            // Return submission metadata
            sb.AppendLine("    -- Return submission metadata");
            sb.AppendLine("    SELECT");
            sb.AppendLine("        s.SubmissionGuid,");
            sb.AppendLine("        s.SubmissionStatus,");
            sb.AppendLine("        s.SubmittedBy,");
            sb.AppendLine("        s.SubmittedDate,");
            sb.AppendLine("        s.ModifiedDate,");
            sb.AppendLine("        s.Version");
            sb.AppendLine("    FROM [dbo].[Submissions] s");
            sb.AppendLine("    WHERE s.SubmissionId = @SubmissionId;");
            sb.AppendLine();

            // Return main form fields as a single row
            sb.AppendLine("    -- Return main form data");
            if (analysis.MainTableColumns.Any())
            {
                sb.AppendLine("    SELECT");
                foreach (var control in analysis.MainTableColumns.OrderBy(c => c.Name))
                {
                    var colName = SanitizeColumnName(control.Name);
                    // Ensure we have a valid alias - use field name if label is empty
                    var aliasName = !string.IsNullOrWhiteSpace(control.Label) ? control.Label :
                                   !string.IsNullOrWhiteSpace(control.Name) ? control.Name :
                                   $"Field_{colName}";
                    // Sanitize the alias to ensure it's valid SQL
                    aliasName = SanitizeColumnName(aliasName);

                    sb.AppendLine($"        MAX(CASE WHEN q.FieldName = N'{colName}' THEN");

                    switch (control.Type?.ToLower())
                    {
                        case "textfield":
                        case "richtext":
                            sb.AppendLine("            a.AnswerText");
                            break;
                        case "datepicker":
                            sb.AppendLine("            CONVERT(NVARCHAR, a.AnswerDate, 121)");
                            break;
                        case "checkbox":
                            sb.AppendLine("            CASE WHEN a.AnswerBit = 1 THEN 'true' ELSE 'false' END");
                            break;
                        case "number":
                        case "decimal":
                        case "integer":
                            sb.AppendLine("            CAST(a.AnswerNumeric AS NVARCHAR)");
                            break;
                        case "dropdown":
                        case "radiobutton":
                        case "combobox":
                            sb.AppendLine("            ISNULL(lv.LookupValue, a.AnswerText)");
                            break;
                        default:
                            sb.AppendLine("            a.AnswerText");
                            break;
                    }

                    sb.AppendLine($"        END) AS [{aliasName}],");
                }
                // Remove last comma
                sb.Length -= 3;
                sb.AppendLine();

                sb.AppendLine("    FROM [dbo].[Questions] q");
                sb.AppendLine("    LEFT JOIN [dbo].[Answers] a ON q.QuestionId = a.QuestionId AND a.SubmissionId = @SubmissionId AND a.RepeatingSectionInstanceId IS NULL");
                sb.AppendLine("    LEFT JOIN [dbo].[LookupValues] lv ON a.AnswerLookupId = lv.LookupId");
                sb.AppendLine("    WHERE q.FormId = @FormId AND q.IsInRepeatingSection = 0;");
            }
            else
            {
                sb.AppendLine("    -- No main form fields");
                sb.AppendLine("    SELECT 'No main form data' AS Message;");
            }
            sb.AppendLine();

            // Return each repeating section's data
            foreach (var section in analysis.RepeatingSections)
            {
                sb.AppendLine($"    -- Return {section.Key} repeating section data");
                sb.AppendLine("    SELECT");
                sb.AppendLine("        rsi.ItemOrder");

                foreach (var control in section.Value.Controls.OrderBy(c => c.Name))
                {
                    var colName = SanitizeColumnName(control.Name);
                    // Ensure we have a valid alias
                    var aliasName = !string.IsNullOrWhiteSpace(control.Label) ? control.Label :
                                   !string.IsNullOrWhiteSpace(control.Name) ? control.Name :
                                   $"Field_{colName}";
                    aliasName = SanitizeColumnName(aliasName);

                    sb.AppendLine($"        ,MAX(CASE WHEN q.FieldName = N'{colName}' THEN");

                    switch (control.Type?.ToLower())
                    {
                        case "textfield":
                        case "richtext":
                            sb.AppendLine("            a.AnswerText");
                            break;
                        case "datepicker":
                            sb.AppendLine("            CONVERT(NVARCHAR, a.AnswerDate, 121)");
                            break;
                        case "checkbox":
                            sb.AppendLine("            CASE WHEN a.AnswerBit = 1 THEN 'true' ELSE 'false' END");
                            break;
                        case "number":
                        case "decimal":
                        case "integer":
                            sb.AppendLine("            CAST(a.AnswerNumeric AS NVARCHAR)");
                            break;
                        case "dropdown":
                        case "radiobutton":
                        case "combobox":
                            sb.AppendLine("            ISNULL(lv.LookupValue, a.AnswerText)");
                            break;
                        default:
                            sb.AppendLine("            a.AnswerText");
                            break;
                    }

                    sb.AppendLine($"        END) AS [{aliasName}]");
                }

                sb.AppendLine("    FROM [dbo].[RepeatingSectionInstances] rsi");
                sb.AppendLine("    INNER JOIN [dbo].[RepeatingSections] rs ON rsi.SectionId = rs.SectionId");
                sb.AppendLine("    INNER JOIN [dbo].[Questions] q ON q.FormId = @FormId AND q.RepeatingSectionName = rs.SectionName");
                sb.AppendLine("    LEFT JOIN [dbo].[Answers] a ON a.QuestionId = q.QuestionId AND a.RepeatingSectionInstanceId = rsi.InstanceId");
                sb.AppendLine("    LEFT JOIN [dbo].[LookupValues] lv ON a.AnswerLookupId = lv.LookupId");
                sb.AppendLine($"    WHERE rsi.SubmissionId = @SubmissionId AND rs.SectionName = N'{section.Key}'");
                sb.AppendLine("    GROUP BY rsi.ItemOrder");
                sb.AppendLine("    ORDER BY rsi.ItemOrder;");
                sb.AppendLine();
            }

            sb.AppendLine("END");
            sb.AppendLine("GO");
            sb.AppendLine();

            // 4. Create procedures to add single items to repeating sections
            foreach (var section in analysis.RepeatingSections)
            {
                var sectionName = SanitizeTableName(section.Key);

                // Create procedure to add single item to repeating section
                sb.AppendLine($"-- Add single item to {section.Key} repeating section");
                sb.AppendLine($"CREATE PROCEDURE [dbo].[sp_{formName}_{sectionName}_AddItem]");
                sb.AppendLine("    @SubmissionGuid UNIQUEIDENTIFIER,");
                sb.AppendLine("    @ItemOrder INT = NULL");

                // Track parameter names to ensure uniqueness
                var usedParamNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var parameterMapping = new Dictionary<string, string>(); // Maps original to unique param name

                foreach (var control in section.Value.Controls.OrderBy(c => c.Name))
                {
                    var paramName = SanitizeColumnName(control.Name);

                    // Ensure parameter name is unique
                    var uniqueParamName = paramName;
                    int suffix = 1;
                    while (usedParamNames.Contains(uniqueParamName))
                    {
                        uniqueParamName = $"{paramName}_{suffix}";
                        suffix++;
                    }
                    usedParamNames.Add(uniqueParamName);
                    parameterMapping[control.Name] = uniqueParamName;

                    var sqlType = GetSqlType(control.Type);
                    sb.AppendLine($"    ,@{uniqueParamName} {sqlType} = NULL -- {control.Label ?? control.Name}");
                }

                sb.AppendLine("AS");
                sb.AppendLine("BEGIN");
                sb.AppendLine("    SET NOCOUNT ON;");
                sb.AppendLine();
                sb.AppendLine("    DECLARE @SubmissionId INT;");
                sb.AppendLine("    DECLARE @FormId INT;");
                sb.AppendLine("    DECLARE @SectionId INT;");
                sb.AppendLine("    DECLARE @InstanceId INT;");
                sb.AppendLine();
                sb.AppendLine($"    SELECT @FormId = FormId FROM [dbo].[Forms] WHERE FormName = N'{formName}';");
                sb.AppendLine("    SELECT @SubmissionId = SubmissionId FROM [dbo].[Submissions] WHERE SubmissionGuid = @SubmissionGuid AND FormId = @FormId;");
                sb.AppendLine($"    SELECT @SectionId = SectionId FROM [dbo].[RepeatingSections] WHERE FormId = @FormId AND SectionName = N'{section.Key}';");
                sb.AppendLine();
                sb.AppendLine("    IF @ItemOrder IS NULL");
                sb.AppendLine("    BEGIN");
                sb.AppendLine("        SELECT @ItemOrder = ISNULL(MAX(ItemOrder), 0) + 1");
                sb.AppendLine("        FROM [dbo].[RepeatingSectionInstances]");
                sb.AppendLine("        WHERE SubmissionId = @SubmissionId AND SectionId = @SectionId;");
                sb.AppendLine("    END");
                sb.AppendLine();
                sb.AppendLine("    -- Create instance");
                sb.AppendLine("    INSERT INTO [dbo].[RepeatingSectionInstances] (SubmissionId, SectionId, ItemOrder)");
                sb.AppendLine("    VALUES (@SubmissionId, @SectionId, @ItemOrder);");
                sb.AppendLine();
                sb.AppendLine("    SET @InstanceId = SCOPE_IDENTITY();");
                sb.AppendLine();

                // Insert each field's answer using the unique parameter names
                foreach (var control in section.Value.Controls)
                {
                    var uniqueParamName = parameterMapping[control.Name];
                    var fieldName = SanitizeColumnName(control.Name);

                    sb.AppendLine($"    -- {control.Label ?? control.Name}");
                    sb.AppendLine($"    IF @{uniqueParamName} IS NOT NULL");
                    sb.AppendLine("    BEGIN");
                    sb.AppendLine("        INSERT INTO [dbo].[Answers] (SubmissionId, QuestionId, RepeatingSectionInstanceId, ");

                    switch (control.Type?.ToLower())
                    {
                        case "textfield":
                        case "richtext":
                            sb.AppendLine("AnswerText)");
                            sb.AppendLine($"        SELECT @SubmissionId, QuestionId, @InstanceId, @{uniqueParamName}");
                            break;
                        case "datepicker":
                            sb.AppendLine("AnswerDate)");
                            sb.AppendLine($"        SELECT @SubmissionId, QuestionId, @InstanceId, @{uniqueParamName}");
                            break;
                        case "checkbox":
                            sb.AppendLine("AnswerBit)");
                            sb.AppendLine($"        SELECT @SubmissionId, QuestionId, @InstanceId, @{uniqueParamName}");
                            break;
                        case "number":
                        case "decimal":
                        case "integer":
                            sb.AppendLine("AnswerNumeric)");
                            sb.AppendLine($"        SELECT @SubmissionId, QuestionId, @InstanceId, @{uniqueParamName}");
                            break;
                        default:
                            sb.AppendLine("AnswerText)");
                            sb.AppendLine($"        SELECT @SubmissionId, QuestionId, @InstanceId, @{uniqueParamName}");
                            break;
                    }

                    sb.AppendLine($"        FROM [dbo].[Questions]");
                    sb.AppendLine($"        WHERE FormId = @FormId AND FieldName = N'{fieldName}' AND RepeatingSectionName = N'{section.Key}';");
                    sb.AppendLine("    END");
                    sb.AppendLine();
                }

                sb.AppendLine("    SELECT @InstanceId AS NewInstanceId;");
                sb.AppendLine("END");
                sb.AppendLine("GO");
                sb.AppendLine();
            }

            return new SqlScript
            {
                Name = $"Create_{formName}_Procedures",
                Type = ScriptType.StoredProcedure,
                Content = sb.ToString(),
                ExecutionOrder = 110,
                Description = $"Form-specific procedures for {formName}"
            };
        }
        private SqlScript GenerateNormalizedIndexes()
        {
            var sb = new StringBuilder();

            sb.AppendLine("-- ===============================================");
            sb.AppendLine("-- Performance Indexes for Q&A Structure");
            sb.AppendLine("-- ===============================================");
            sb.AppendLine();

            // Forms indexes
            sb.AppendLine("-- Forms table indexes");
            sb.AppendLine("CREATE NONCLUSTERED INDEX [IX_Forms_Active] ON [dbo].[Forms] ([IsActive]) INCLUDE ([FormName]);");
            sb.AppendLine();

            // Questions indexes
            sb.AppendLine("-- Questions table indexes");
            sb.AppendLine("CREATE NONCLUSTERED INDEX [IX_Questions_FormId] ON [dbo].[Questions] ([FormId]);");
            sb.AppendLine("CREATE NONCLUSTERED INDEX [IX_Questions_Repeating] ON [dbo].[Questions] ([IsInRepeatingSection], [RepeatingSectionName]);");
            sb.AppendLine();

            // Submissions indexes
            sb.AppendLine("-- Submissions table indexes");
            sb.AppendLine("CREATE NONCLUSTERED INDEX [IX_Submissions_FormId] ON [dbo].[Submissions] ([FormId], [SubmittedDate] DESC);");
            sb.AppendLine("CREATE NONCLUSTERED INDEX [IX_Submissions_Status] ON [dbo].[Submissions] ([SubmissionStatus]) INCLUDE ([FormId], [SubmittedDate]);");
            sb.AppendLine();

            // Answers indexes
            sb.AppendLine("-- Answers table indexes");
            sb.AppendLine("CREATE NONCLUSTERED INDEX [IX_Answers_SubmissionId] ON [dbo].[Answers] ([SubmissionId], [QuestionId]);");
            sb.AppendLine("CREATE NONCLUSTERED INDEX [IX_Answers_RSI] ON [dbo].[Answers] ([RepeatingSectionInstanceId]) WHERE [RepeatingSectionInstanceId] IS NOT NULL;");
            sb.AppendLine();

            // RepeatingSectionInstances indexes
            sb.AppendLine("-- RepeatingSectionInstances table indexes");
            sb.AppendLine("CREATE NONCLUSTERED INDEX [IX_RSI_Submission] ON [dbo].[RepeatingSectionInstances] ([SubmissionId], [SectionId], [ItemOrder]);");
            sb.AppendLine();

            // LookupValues indexes
            sb.AppendLine("-- LookupValues table indexes");
            sb.AppendLine("CREATE NONCLUSTERED INDEX [IX_LookupValues_Category] ON [dbo].[LookupValues] ([LookupCategory], [IsActive]);");
            sb.AppendLine("CREATE NONCLUSTERED INDEX [IX_LookupValues_Question] ON [dbo].[LookupValues] ([QuestionId], [IsActive]) WHERE [QuestionId] IS NOT NULL;");
            sb.AppendLine();

            return new SqlScript
            {
                Name = "Create_QA_Indexes",
                Type = ScriptType.Index,
                Content = sb.ToString(),
                ExecutionOrder = 300,
                Description = "Performance indexes for Q&A structure"
            };
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
                .Where(c => c.HasStaticData && c.DataOptions != null && c.DataOptions.Count > 0)
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
      .Where(c => c.HasStaticData &&
             c.DataOptions != null &&
             c.DataOptions.Count > 0 &&
             c.DataOptions.Any(o => !string.IsNullOrWhiteSpace(o.DisplayText))) // Also check for non-empty options
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
            sb.AppendLine($"-- Insert procedure for {tableName} with repeating sections");
            sb.AppendLine("-- ===============================================");
            sb.AppendLine();

            sb.AppendLine($"CREATE PROCEDURE [dbo].[sp_{tableName}_Insert]");
            sb.AppendLine("    @FormId UNIQUEIDENTIFIER OUTPUT,");
            sb.AppendLine("    @CreatedBy NVARCHAR(255) = NULL,");
            sb.AppendLine("    @Status NVARCHAR(50) = 'Draft'");

            // Add parameters for main table columns
            foreach (var control in analysis.MainTableColumns.OrderBy(c => c.Name))
            {
                var columnName = SanitizeColumnName(control.Name);
                var sqlType = GetSqlType(control.Type);
                sb.AppendLine($"    ,@{columnName} {sqlType} = NULL");
            }

            // Add table-valued parameters for repeating sections
            foreach (var section in analysis.RepeatingSections)
            {
                var sectionTableName = SanitizeTableName(section.Key);
                sb.AppendLine($"    ,@{sectionTableName}Data {sectionTableName}TableType READONLY");
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

            // Insert main record
            sb.AppendLine("        -- Insert main form record");
            sb.AppendLine($"        INSERT INTO [dbo].[{tableName}] (");
            sb.AppendLine("            [FormId],");
            sb.AppendLine("            [CreatedBy],");
            sb.AppendLine("            [Status],");
            sb.AppendLine("            [CreatedDate],");
            sb.AppendLine("            [ModifiedDate]");

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

            foreach (var control in analysis.MainTableColumns)
            {
                var columnName = SanitizeColumnName(control.Name);
                sb.AppendLine($"            ,@{columnName}");
            }

            sb.AppendLine("        );");
            sb.AppendLine();

            // Insert repeating section records
            foreach (var section in analysis.RepeatingSections)
            {
                var sectionTableName = SanitizeTableName(section.Key);
                var fullTableName = $"{tableName}_{sectionTableName}";

                sb.AppendLine($"        -- Insert {section.Key} repeating section records");
                sb.AppendLine($"        INSERT INTO [dbo].[{fullTableName}] (");
                sb.AppendLine("            [ParentFormId],");
                sb.AppendLine("            [ItemOrder],");
                sb.AppendLine("            [CreatedDate],");
                sb.AppendLine("            [ModifiedDate]");

                // Add columns for this section
                foreach (var control in section.Value.Controls.OrderBy(c => c.Name))
                {
                    var columnName = SanitizeColumnName(control.Name);
                    sb.AppendLine($"            ,[{columnName}]");
                }

                sb.AppendLine("        )");
                sb.AppendLine($"        SELECT");
                sb.AppendLine("            @FormId,");
                sb.AppendLine("            ItemOrder,");
                sb.AppendLine("            GETDATE(),");
                sb.AppendLine("            GETDATE()");

                foreach (var control in section.Value.Controls.OrderBy(c => c.Name))
                {
                    var columnName = SanitizeColumnName(control.Name);
                    sb.AppendLine($"            ,[{columnName}]");
                }

                sb.AppendLine($"        FROM @{sectionTableName}Data;");
                sb.AppendLine();
            }

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
                Description = $"Insert procedure for {tableName} with repeating sections"
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

        private List<SqlScript> GenerateTableTypes(InfoPathFormDefinition formDef, RepeatingSectionAnalysis analysis)
        {
            var scripts = new List<SqlScript>();
            var mainTableName = SanitizeTableName(formDef.FormName);
            int order = 3; // Execute after tables but before stored procedures

            foreach (var section in analysis.RepeatingSections)
            {
                var sectionTableName = SanitizeTableName(section.Key);
                var sb = new StringBuilder();

                sb.AppendLine("-- ===============================================");
                sb.AppendLine($"-- Table Type for {section.Key} repeating section");
                sb.AppendLine("-- ===============================================");
                sb.AppendLine();

                // Drop if exists
                sb.AppendLine($"IF TYPE_ID(N'{sectionTableName}TableType') IS NOT NULL");
                sb.AppendLine($"    DROP TYPE {sectionTableName}TableType;");
                sb.AppendLine();

                sb.AppendLine($"CREATE TYPE {sectionTableName}TableType AS TABLE (");
                sb.AppendLine("    [ItemOrder] INT NOT NULL");

                // Add columns for this section
                foreach (var control in section.Value.Controls.OrderBy(c => c.Name))
                {
                    var columnName = SanitizeColumnName(control.Name);
                    var sqlType = GetSqlType(control.Type);
                    sb.AppendLine($"    ,[{columnName}] {sqlType} NULL");
                }

                sb.AppendLine(");");

                scripts.Add(new SqlScript
                {
                    Name = $"Create_{sectionTableName}_TableType",
                    Type = ScriptType.Other,
                    Content = sb.ToString(),
                    ExecutionOrder = order++,
                    Description = $"Table type for bulk insert of {section.Key} repeating section"
                });
            }

            return scripts;
        }

        private List<SqlScript> GenerateRepeatingSectionProcedures(InfoPathFormDefinition formDef, RepeatingSectionAnalysis analysis)
        {
            var scripts = new List<SqlScript>();
            var mainTableName = SanitizeTableName(formDef.FormName);
            int order = 105;

            foreach (var section in analysis.RepeatingSections)
            {
                var sectionTableName = SanitizeTableName(section.Key);
                var fullTableName = $"{mainTableName}_{sectionTableName}";

                // INSERT procedure for individual repeating section item
                var insertSb = new StringBuilder();
                insertSb.AppendLine($"-- Insert procedure for {section.Key} repeating section item");
                insertSb.AppendLine($"CREATE PROCEDURE [dbo].[sp_{fullTableName}_InsertItem]");
                insertSb.AppendLine("    @ParentFormId UNIQUEIDENTIFIER,");
                insertSb.AppendLine("    @ItemOrder INT = NULL");

                foreach (var control in section.Value.Controls.OrderBy(c => c.Name))
                {
                    var columnName = SanitizeColumnName(control.Name);
                    var sqlType = GetSqlType(control.Type);
                    insertSb.AppendLine($"    ,@{columnName} {sqlType} = NULL");
                }

                insertSb.AppendLine("AS");
                insertSb.AppendLine("BEGIN");
                insertSb.AppendLine("    SET NOCOUNT ON;");
                insertSb.AppendLine();
                insertSb.AppendLine("    -- Auto-generate ItemOrder if not provided");
                insertSb.AppendLine("    IF @ItemOrder IS NULL");
                insertSb.AppendLine("    BEGIN");
                insertSb.AppendLine($"        SELECT @ItemOrder = ISNULL(MAX(ItemOrder), 0) + 1");
                insertSb.AppendLine($"        FROM [dbo].[{fullTableName}]");
                insertSb.AppendLine("        WHERE [ParentFormId] = @ParentFormId;");
                insertSb.AppendLine("    END");
                insertSb.AppendLine();
                insertSb.AppendLine($"    INSERT INTO [dbo].[{fullTableName}] (");
                insertSb.AppendLine("        [ParentFormId],");
                insertSb.AppendLine("        [ItemOrder],");
                insertSb.AppendLine("        [CreatedDate],");
                insertSb.AppendLine("        [ModifiedDate]");

                foreach (var control in section.Value.Controls.OrderBy(c => c.Name))
                {
                    var columnName = SanitizeColumnName(control.Name);
                    insertSb.AppendLine($"        ,[{columnName}]");
                }

                insertSb.AppendLine("    ) VALUES (");
                insertSb.AppendLine("        @ParentFormId,");
                insertSb.AppendLine("        @ItemOrder,");
                insertSb.AppendLine("        GETDATE(),");
                insertSb.AppendLine("        GETDATE()");

                foreach (var control in section.Value.Controls.OrderBy(c => c.Name))
                {
                    var columnName = SanitizeColumnName(control.Name);
                    insertSb.AppendLine($"        ,@{columnName}");
                }

                insertSb.AppendLine("    );");
                insertSb.AppendLine();
                insertSb.AppendLine("    SELECT SCOPE_IDENTITY() AS NewId;");
                insertSb.AppendLine("END");

                scripts.Add(new SqlScript
                {
                    Name = $"sp_{fullTableName}_InsertItem",
                    Type = ScriptType.StoredProcedure,
                    Content = insertSb.ToString(),
                    ExecutionOrder = order++,
                    Description = $"Insert individual item for {section.Key} repeating section"
                });

                // UPDATE procedure for repeating section item
                var updateSb = new StringBuilder();
                updateSb.AppendLine($"-- Update procedure for {section.Key} repeating section item");
                updateSb.AppendLine($"CREATE PROCEDURE [dbo].[sp_{fullTableName}_UpdateItem]");
                updateSb.AppendLine("    @Id INT");

                foreach (var control in section.Value.Controls.OrderBy(c => c.Name))
                {
                    var columnName = SanitizeColumnName(control.Name);
                    var sqlType = GetSqlType(control.Type);
                    updateSb.AppendLine($"    ,@{columnName} {sqlType} = NULL");
                }

                updateSb.AppendLine("AS");
                updateSb.AppendLine("BEGIN");
                updateSb.AppendLine("    SET NOCOUNT ON;");
                updateSb.AppendLine();
                updateSb.AppendLine($"    UPDATE [dbo].[{fullTableName}]");
                updateSb.AppendLine("    SET");
                updateSb.AppendLine("        [ModifiedDate] = GETDATE()");

                foreach (var control in section.Value.Controls.OrderBy(c => c.Name))
                {
                    var columnName = SanitizeColumnName(control.Name);
                    updateSb.AppendLine($"        ,[{columnName}] = ISNULL(@{columnName}, [{columnName}])");
                }

                updateSb.AppendLine("    WHERE [Id] = @Id;");
                updateSb.AppendLine();
                updateSb.AppendLine("    IF @@ROWCOUNT = 0");
                updateSb.AppendLine("        RAISERROR('Record not found', 16, 1);");
                updateSb.AppendLine("END");

                scripts.Add(new SqlScript
                {
                    Name = $"sp_{fullTableName}_UpdateItem",
                    Type = ScriptType.StoredProcedure,
                    Content = updateSb.ToString(),
                    ExecutionOrder = order++,
                    Description = $"Update individual item for {section.Key} repeating section"
                });

                // DELETE procedure for repeating section item
                var deleteSb = new StringBuilder();
                deleteSb.AppendLine($"-- Delete procedure for {section.Key} repeating section item");
                deleteSb.AppendLine($"CREATE PROCEDURE [dbo].[sp_{fullTableName}_DeleteItem]");
                deleteSb.AppendLine("    @Id INT");
                deleteSb.AppendLine("AS");
                deleteSb.AppendLine("BEGIN");
                deleteSb.AppendLine("    SET NOCOUNT ON;");
                deleteSb.AppendLine();
                deleteSb.AppendLine($"    DELETE FROM [dbo].[{fullTableName}]");
                deleteSb.AppendLine("    WHERE [Id] = @Id;");
                deleteSb.AppendLine();
                deleteSb.AppendLine("    IF @@ROWCOUNT = 0");
                deleteSb.AppendLine("        RAISERROR('Record not found', 16, 1);");
                deleteSb.AppendLine("END");

                scripts.Add(new SqlScript
                {
                    Name = $"sp_{fullTableName}_DeleteItem",
                    Type = ScriptType.StoredProcedure,
                    Content = deleteSb.ToString(),
                    ExecutionOrder = order++,
                    Description = $"Delete individual item for {section.Key} repeating section"
                });

                // GET ALL items for a parent form
                var getAllSb = new StringBuilder();
                getAllSb.AppendLine($"-- Get all {section.Key} items for a form");
                getAllSb.AppendLine($"CREATE PROCEDURE [dbo].[sp_{fullTableName}_GetByParent]");
                getAllSb.AppendLine("    @ParentFormId UNIQUEIDENTIFIER");
                getAllSb.AppendLine("AS");
                getAllSb.AppendLine("BEGIN");
                getAllSb.AppendLine("    SET NOCOUNT ON;");
                getAllSb.AppendLine();
                getAllSb.AppendLine("    SELECT *");
                getAllSb.AppendLine($"    FROM [dbo].[{fullTableName}]");
                getAllSb.AppendLine("    WHERE [ParentFormId] = @ParentFormId");
                getAllSb.AppendLine("    ORDER BY [ItemOrder];");
                getAllSb.AppendLine("END");

                scripts.Add(new SqlScript
                {
                    Name = $"sp_{fullTableName}_GetByParent",
                    Type = ScriptType.StoredProcedure,
                    Content = getAllSb.ToString(),
                    ExecutionOrder = order++,
                    Description = $"Get all {section.Key} items for a parent form"
                });
            }

            return scripts;
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

        private async Task<SqlGenerationResult> GenerateNormalizedQAStructure(InfoPathFormDefinition formDef, RepeatingSectionAnalysis analysis)
        {
            var result = new SqlGenerationResult
            {
                Dialect = this.Dialect,
                StructureType = TableStructureType.NormalizedQA,
                GeneratedDate = DateTime.Now
            };

            try
            {
                var scripts = new List<SqlScript>();

                // 1. Create core system tables (shared across all forms)
                scripts.Add(GenerateNormalizedCoreTables());

                // 2. Create centralized lookup table for ALL dropdown values
                scripts.Add(GenerateCentralizedLookupTable());

                // 3. Create form registration stored procedures
                scripts.Add(GenerateFormRegistrationProcedures());

                // 4. Create submission procedures that handle repeating sections
                scripts.Add(GenerateNormalizedSubmissionProcedures());

                // 5. Create retrieval procedures
                scripts.Add(GenerateNormalizedRetrievalProcedures());

                // 6. Create reporting views
                scripts.Add(GenerateNormalizedReportingViews());

                result.Scripts = scripts.OrderBy(s => s.ExecutionOrder).ToList();
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private SqlScript GenerateNormalizedCoreTables()
        {
            var sb = new StringBuilder();

            sb.AppendLine("-- ===============================================");
            sb.AppendLine("-- Core Tables for Normalized Q&A Structure");
            sb.AppendLine("-- ===============================================");
            sb.AppendLine();

            // Forms table - stores form definitions
            sb.AppendLine("-- Forms table (all form definitions)");
            sb.AppendLine("IF OBJECT_ID('dbo.Forms', 'U') IS NOT NULL DROP TABLE [dbo].[Forms];");
            sb.AppendLine("CREATE TABLE [dbo].[Forms] (");
            sb.AppendLine("    [FormId] INT IDENTITY(1,1) NOT NULL,");
            sb.AppendLine("    [FormName] NVARCHAR(255) NOT NULL,");
            sb.AppendLine("    [FormFileName] NVARCHAR(255) NULL,");
            sb.AppendLine("    [FormVersion] NVARCHAR(50) NULL,");
            sb.AppendLine("    [IsActive] BIT DEFAULT 1 NOT NULL,");
            sb.AppendLine("    [CreatedDate] DATETIME2(3) DEFAULT GETDATE() NOT NULL,");
            sb.AppendLine("    [ModifiedDate] DATETIME2(3) DEFAULT GETDATE() NOT NULL,");
            sb.AppendLine("    CONSTRAINT [PK_Forms] PRIMARY KEY CLUSTERED ([FormId] ASC),");
            sb.AppendLine("    CONSTRAINT [UQ_Forms_Name] UNIQUE ([FormName])");
            sb.AppendLine(");");
            sb.AppendLine();

            // Questions table - stores all form fields/questions
            sb.AppendLine("-- Questions table (all form fields across all forms)");
            sb.AppendLine("IF OBJECT_ID('dbo.Questions', 'U') IS NOT NULL DROP TABLE [dbo].[Questions];");
            sb.AppendLine("CREATE TABLE [dbo].[Questions] (");
            sb.AppendLine("    [QuestionId] INT IDENTITY(1,1) NOT NULL,");
            sb.AppendLine("    [FormId] INT NOT NULL,");
            sb.AppendLine("    [FieldName] NVARCHAR(255) NOT NULL,");
            sb.AppendLine("    [DisplayName] NVARCHAR(500) NULL,");
            sb.AppendLine("    [FieldType] NVARCHAR(50) NOT NULL,");
            sb.AppendLine("    [SectionName] NVARCHAR(255) NULL,");
            sb.AppendLine("    [IsInRepeatingSection] BIT DEFAULT 0 NOT NULL,");
            sb.AppendLine("    [RepeatingSectionName] NVARCHAR(255) NULL,");
            sb.AppendLine("    [IsConditional] BIT DEFAULT 0 NOT NULL,");
            sb.AppendLine("    [ConditionalOnField] NVARCHAR(255) NULL,");
            sb.AppendLine("    [DisplayOrder] INT DEFAULT 0 NOT NULL,");
            sb.AppendLine("    [IsRequired] BIT DEFAULT 0 NOT NULL,");
            sb.AppendLine("    [DefaultValue] NVARCHAR(MAX) NULL,");
            sb.AppendLine("    [ValidationRule] NVARCHAR(MAX) NULL,");
            sb.AppendLine("    [HasLookupValues] BIT DEFAULT 0 NOT NULL,");
            sb.AppendLine("    [CreatedDate] DATETIME2(3) DEFAULT GETDATE() NOT NULL,");
            sb.AppendLine("    CONSTRAINT [PK_Questions] PRIMARY KEY CLUSTERED ([QuestionId] ASC),");
            sb.AppendLine("    CONSTRAINT [FK_Questions_Forms] FOREIGN KEY ([FormId]) REFERENCES [dbo].[Forms] ([FormId]),");
            sb.AppendLine("    CONSTRAINT [UQ_Questions_FormField] UNIQUE ([FormId], [FieldName], [RepeatingSectionName])");
            sb.AppendLine(");");
            sb.AppendLine();

            // Repeating Sections table - stores repeating section definitions
            sb.AppendLine("-- RepeatingSections table (defines all repeating sections)");
            sb.AppendLine("IF OBJECT_ID('dbo.RepeatingSections', 'U') IS NOT NULL DROP TABLE [dbo].[RepeatingSections];");
            sb.AppendLine("CREATE TABLE [dbo].[RepeatingSections] (");
            sb.AppendLine("    [SectionId] INT IDENTITY(1,1) NOT NULL,");
            sb.AppendLine("    [FormId] INT NOT NULL,");
            sb.AppendLine("    [SectionName] NVARCHAR(255) NOT NULL,");
            sb.AppendLine("    [SectionType] NVARCHAR(50) DEFAULT 'repeating' NOT NULL,");
            sb.AppendLine("    [ParentSectionId] INT NULL,"); // For nested repeating sections
            sb.AppendLine("    [MinItems] INT DEFAULT 0,");
            sb.AppendLine("    [MaxItems] INT NULL,");
            sb.AppendLine("    [CreatedDate] DATETIME2(3) DEFAULT GETDATE() NOT NULL,");
            sb.AppendLine("    CONSTRAINT [PK_RepeatingSections] PRIMARY KEY CLUSTERED ([SectionId] ASC),");
            sb.AppendLine("    CONSTRAINT [FK_RepeatingSections_Forms] FOREIGN KEY ([FormId]) REFERENCES [dbo].[Forms] ([FormId]),");
            sb.AppendLine("    CONSTRAINT [FK_RepeatingSections_Parent] FOREIGN KEY ([ParentSectionId]) REFERENCES [dbo].[RepeatingSections] ([SectionId]),");
            sb.AppendLine("    CONSTRAINT [UQ_RepeatingSections] UNIQUE ([FormId], [SectionName])");
            sb.AppendLine(");");
            sb.AppendLine();

            // Submissions table - stores form submissions
            sb.AppendLine("-- Submissions table (all form submissions)");
            sb.AppendLine("IF OBJECT_ID('dbo.Submissions', 'U') IS NOT NULL DROP TABLE [dbo].[Submissions];");
            sb.AppendLine("CREATE TABLE [dbo].[Submissions] (");
            sb.AppendLine("    [SubmissionId] INT IDENTITY(1,1) NOT NULL,");
            sb.AppendLine("    [SubmissionGuid] UNIQUEIDENTIFIER DEFAULT NEWID() NOT NULL,");
            sb.AppendLine("    [FormId] INT NOT NULL,");
            sb.AppendLine("    [SubmittedBy] NVARCHAR(255) NULL,");
            sb.AppendLine("    [SubmissionStatus] NVARCHAR(50) DEFAULT 'Draft' NOT NULL,");
            sb.AppendLine("    [SubmittedDate] DATETIME2(3) DEFAULT GETDATE() NOT NULL,");
            sb.AppendLine("    [ModifiedDate] DATETIME2(3) DEFAULT GETDATE() NOT NULL,");
            sb.AppendLine("    [Version] INT DEFAULT 1 NOT NULL,");
            sb.AppendLine("    CONSTRAINT [PK_Submissions] PRIMARY KEY CLUSTERED ([SubmissionId] ASC),");
            sb.AppendLine("    CONSTRAINT [FK_Submissions_Forms] FOREIGN KEY ([FormId]) REFERENCES [dbo].[Forms] ([FormId]),");
            sb.AppendLine("    CONSTRAINT [UQ_Submissions_Guid] UNIQUE ([SubmissionGuid])");
            sb.AppendLine(");");
            sb.AppendLine();

            // Answers table - stores all answers including repeating section instances
            sb.AppendLine("-- Answers table (all answers including repeating sections)");
            sb.AppendLine("IF OBJECT_ID('dbo.Answers', 'U') IS NOT NULL DROP TABLE [dbo].[Answers];");
            sb.AppendLine("CREATE TABLE [dbo].[Answers] (");
            sb.AppendLine("    [AnswerId] INT IDENTITY(1,1) NOT NULL,");
            sb.AppendLine("    [SubmissionId] INT NOT NULL,");
            sb.AppendLine("    [QuestionId] INT NOT NULL,");
            sb.AppendLine("    [RepeatingSectionInstanceId] INT NULL,"); // Links to repeating section instance
            sb.AppendLine("    [AnswerText] NVARCHAR(MAX) NULL,");
            sb.AppendLine("    [AnswerNumeric] DECIMAL(18,4) NULL,");
            sb.AppendLine("    [AnswerDate] DATETIME2(3) NULL,");
            sb.AppendLine("    [AnswerBit] BIT NULL,");
            sb.AppendLine("    [AnswerLookupId] INT NULL,"); // Links to centralized lookup table
            sb.AppendLine("    [CreatedDate] DATETIME2(3) DEFAULT GETDATE() NOT NULL,");
            sb.AppendLine("    [ModifiedDate] DATETIME2(3) DEFAULT GETDATE() NOT NULL,");
            sb.AppendLine("    CONSTRAINT [PK_Answers] PRIMARY KEY CLUSTERED ([AnswerId] ASC),");
            sb.AppendLine("    CONSTRAINT [FK_Answers_Submissions] FOREIGN KEY ([SubmissionId]) REFERENCES [dbo].[Submissions] ([SubmissionId]) ON DELETE CASCADE,");
            sb.AppendLine("    CONSTRAINT [FK_Answers_Questions] FOREIGN KEY ([QuestionId]) REFERENCES [dbo].[Questions] ([QuestionId]),");
            sb.AppendLine("    CONSTRAINT [FK_Answers_Lookup] FOREIGN KEY ([AnswerLookupId]) REFERENCES [dbo].[LookupValues] ([LookupId])");
            sb.AppendLine(");");
            sb.AppendLine();

            // Repeating Section Instances table
            sb.AppendLine("-- RepeatingSectionInstances table (tracks each instance of a repeating section)");
            sb.AppendLine("IF OBJECT_ID('dbo.RepeatingSectionInstances', 'U') IS NOT NULL DROP TABLE [dbo].[RepeatingSectionInstances];");
            sb.AppendLine("CREATE TABLE [dbo].[RepeatingSectionInstances] (");
            sb.AppendLine("    [InstanceId] INT IDENTITY(1,1) NOT NULL,");
            sb.AppendLine("    [SubmissionId] INT NOT NULL,");
            sb.AppendLine("    [SectionId] INT NOT NULL,");
            sb.AppendLine("    [ParentInstanceId] INT NULL,"); // For nested repeating sections
            sb.AppendLine("    [ItemOrder] INT NOT NULL DEFAULT 0,");
            sb.AppendLine("    [CreatedDate] DATETIME2(3) DEFAULT GETDATE() NOT NULL,");
            sb.AppendLine("    CONSTRAINT [PK_RepeatingSectionInstances] PRIMARY KEY CLUSTERED ([InstanceId] ASC),");
            sb.AppendLine("    CONSTRAINT [FK_RSI_Submissions] FOREIGN KEY ([SubmissionId]) REFERENCES [dbo].[Submissions] ([SubmissionId]) ON DELETE CASCADE,");
            sb.AppendLine("    CONSTRAINT [FK_RSI_Sections] FOREIGN KEY ([SectionId]) REFERENCES [dbo].[RepeatingSections] ([SectionId]),");
            sb.AppendLine("    CONSTRAINT [FK_RSI_Parent] FOREIGN KEY ([ParentInstanceId]) REFERENCES [dbo].[RepeatingSectionInstances] ([InstanceId])");
            sb.AppendLine(");");
            sb.AppendLine();

            // Add foreign key for Answers to RepeatingSectionInstances
            sb.AppendLine("-- Add foreign key constraint for repeating section instances");
            sb.AppendLine("ALTER TABLE [dbo].[Answers]");
            sb.AppendLine("ADD CONSTRAINT [FK_Answers_RSI] FOREIGN KEY ([RepeatingSectionInstanceId])");
            sb.AppendLine("REFERENCES [dbo].[RepeatingSectionInstances] ([InstanceId]);");
            sb.AppendLine();

            // Create indexes
            sb.AppendLine("-- Create indexes for performance");
            sb.AppendLine("CREATE NONCLUSTERED INDEX [IX_Questions_FormId] ON [dbo].[Questions] ([FormId]);");
            sb.AppendLine("CREATE NONCLUSTERED INDEX [IX_Questions_Repeating] ON [dbo].[Questions] ([IsInRepeatingSection], [RepeatingSectionName]);");
            sb.AppendLine("CREATE NONCLUSTERED INDEX [IX_Submissions_FormId] ON [dbo].[Submissions] ([FormId], [SubmittedDate] DESC);");
            sb.AppendLine("CREATE NONCLUSTERED INDEX [IX_Answers_SubmissionId] ON [dbo].[Answers] ([SubmissionId], [QuestionId]);");
            sb.AppendLine("CREATE NONCLUSTERED INDEX [IX_Answers_RSI] ON [dbo].[Answers] ([RepeatingSectionInstanceId]) WHERE [RepeatingSectionInstanceId] IS NOT NULL;");
            sb.AppendLine("CREATE NONCLUSTERED INDEX [IX_RSI_Submission] ON [dbo].[RepeatingSectionInstances] ([SubmissionId], [SectionId], [ItemOrder]);");
            sb.AppendLine();

            return new SqlScript
            {
                Name = "Create_Normalized_Core_Tables",
                Type = ScriptType.Table,
                Content = sb.ToString(),
                ExecutionOrder = 1,
                Description = "Core tables for normalized Q&A structure with repeating section support"
            };
        }

        // 2. Centralized Lookup Table for ALL Dropdown Values
        private SqlScript GenerateCentralizedLookupTable()
        {
            var sb = new StringBuilder();

            sb.AppendLine("-- ===============================================");
            sb.AppendLine("-- Centralized Lookup Table for All Dropdown Values");
            sb.AppendLine("-- ===============================================");
            sb.AppendLine();

            sb.AppendLine("IF OBJECT_ID('dbo.LookupValues', 'U') IS NOT NULL DROP TABLE [dbo].[LookupValues];");
            sb.AppendLine("CREATE TABLE [dbo].[LookupValues] (");
            sb.AppendLine("    [LookupId] INT IDENTITY(1,1) NOT NULL,");
            sb.AppendLine("    [FormId] INT NULL,"); // NULL means global lookup value
            sb.AppendLine("    [QuestionId] INT NULL,"); // Links to specific question
            sb.AppendLine("    [LookupCategory] NVARCHAR(255) NULL,"); // Category for grouping
            sb.AppendLine("    [LookupValue] NVARCHAR(500) NOT NULL,");
            sb.AppendLine("    [LookupDisplayText] NVARCHAR(500) NOT NULL,");
            sb.AppendLine("    [LookupCode] NVARCHAR(50) NULL,"); // Short code for the value
            sb.AppendLine("    [ParentLookupId] INT NULL,"); // For hierarchical lookups
            sb.AppendLine("    [SortOrder] INT DEFAULT 0 NOT NULL,");
            sb.AppendLine("    [IsActive] BIT DEFAULT 1 NOT NULL,");
            sb.AppendLine("    [IsDefault] BIT DEFAULT 0 NOT NULL,");
            sb.AppendLine("    [CreatedDate] DATETIME2(3) DEFAULT GETDATE() NOT NULL,");
            sb.AppendLine("    [ModifiedDate] DATETIME2(3) DEFAULT GETDATE() NOT NULL,");
            sb.AppendLine("    CONSTRAINT [PK_LookupValues] PRIMARY KEY CLUSTERED ([LookupId] ASC),");
            sb.AppendLine("    CONSTRAINT [FK_LookupValues_Forms] FOREIGN KEY ([FormId]) REFERENCES [dbo].[Forms] ([FormId]),");
            sb.AppendLine("    CONSTRAINT [FK_LookupValues_Questions] FOREIGN KEY ([QuestionId]) REFERENCES [dbo].[Questions] ([QuestionId]),");
            sb.AppendLine("    CONSTRAINT [FK_LookupValues_Parent] FOREIGN KEY ([ParentLookupId]) REFERENCES [dbo].[LookupValues] ([LookupId])");
            sb.AppendLine(");");
            sb.AppendLine();

            sb.AppendLine("-- Create indexes for lookup performance");
            sb.AppendLine("CREATE NONCLUSTERED INDEX [IX_LookupValues_Category] ON [dbo].[LookupValues] ([LookupCategory], [IsActive]);");
            sb.AppendLine("CREATE NONCLUSTERED INDEX [IX_LookupValues_Question] ON [dbo].[LookupValues] ([QuestionId], [IsActive]) WHERE [QuestionId] IS NOT NULL;");
            sb.AppendLine("CREATE NONCLUSTERED INDEX [IX_LookupValues_Value] ON [dbo].[LookupValues] ([LookupValue], [IsActive]);");
            sb.AppendLine();

            return new SqlScript
            {
                Name = "Create_Centralized_Lookup_Table",
                Type = ScriptType.Table,
                Content = sb.ToString(),
                ExecutionOrder = 2,
                Description = "Centralized lookup table for all dropdown values across all forms"
            };
        }

        // 3. Form Registration Procedures
        private SqlScript GenerateFormRegistrationProcedures()
        {
            var sb = new StringBuilder();

            sb.AppendLine("-- ===============================================");
            sb.AppendLine("-- Form Registration Stored Procedures");
            sb.AppendLine("-- ===============================================");
            sb.AppendLine();

            // Register Form procedure
            sb.AppendLine("CREATE PROCEDURE [dbo].[sp_RegisterForm]");
            sb.AppendLine("    @FormName NVARCHAR(255),");
            sb.AppendLine("    @FormFileName NVARCHAR(255) = NULL,");
            sb.AppendLine("    @FormDefinitionJSON NVARCHAR(MAX),"); // JSON definition of the form
            sb.AppendLine("    @FormId INT OUTPUT");
            sb.AppendLine("AS");
            sb.AppendLine("BEGIN");
            sb.AppendLine("    SET NOCOUNT ON;");
            sb.AppendLine();
            sb.AppendLine("    BEGIN TRY");
            sb.AppendLine("        BEGIN TRANSACTION;");
            sb.AppendLine();
            sb.AppendLine("        -- Check if form already exists");
            sb.AppendLine("        SELECT @FormId = FormId FROM [dbo].[Forms] WHERE FormName = @FormName;");
            sb.AppendLine();
            sb.AppendLine("        IF @FormId IS NULL");
            sb.AppendLine("        BEGIN");
            sb.AppendLine("            -- Insert new form");
            sb.AppendLine("            INSERT INTO [dbo].[Forms] (FormName, FormFileName, FormVersion)");
            sb.AppendLine("            VALUES (@FormName, @FormFileName, '1.0');");
            sb.AppendLine("            SET @FormId = SCOPE_IDENTITY();");
            sb.AppendLine("        END");
            sb.AppendLine("        ELSE");
            sb.AppendLine("        BEGIN");
            sb.AppendLine("            -- Update existing form");
            sb.AppendLine("            UPDATE [dbo].[Forms]");
            sb.AppendLine("            SET ModifiedDate = GETDATE(),");
            sb.AppendLine("                FormVersion = CAST(CAST(FormVersion AS FLOAT) + 0.1 AS NVARCHAR(50))");
            sb.AppendLine("            WHERE FormId = @FormId;");
            sb.AppendLine("        END");
            sb.AppendLine();
            sb.AppendLine("        -- Parse JSON and register questions and sections");
            sb.AppendLine("        -- This would typically parse the JSON and insert questions, sections, and lookup values");
            sb.AppendLine("        -- For brevity, showing the structure only");
            sb.AppendLine();
            sb.AppendLine("        COMMIT TRANSACTION;");
            sb.AppendLine("    END TRY");
            sb.AppendLine("    BEGIN CATCH");
            sb.AppendLine("        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;");
            sb.AppendLine("        THROW;");
            sb.AppendLine("    END CATCH");
            sb.AppendLine("END");
            sb.AppendLine("GO");
            sb.AppendLine();

            return new SqlScript
            {
                Name = "Create_Form_Registration_Procedures",
                Type = ScriptType.StoredProcedure,
                Content = sb.ToString(),
                ExecutionOrder = 100,
                Description = "Procedures for registering forms and their structure"
            };
        }

        // 4. Submission Procedures with Repeating Section Support
        private SqlScript GenerateNormalizedSubmissionProcedures()
        {
            var sb = new StringBuilder();

            sb.AppendLine("-- ===============================================");
            sb.AppendLine("-- Submission Procedures with Repeating Section Support");
            sb.AppendLine("-- ===============================================");
            sb.AppendLine();

            // Submit Form Data procedure
            sb.AppendLine("CREATE PROCEDURE [dbo].[sp_SubmitFormData]");
            sb.AppendLine("    @FormName NVARCHAR(255),");
            sb.AppendLine("    @SubmittedBy NVARCHAR(255) = NULL,");
            sb.AppendLine("    @SubmissionDataJSON NVARCHAR(MAX),"); // JSON with all form data including repeating sections
            sb.AppendLine("    @SubmissionGuid UNIQUEIDENTIFIER OUTPUT");
            sb.AppendLine("AS");
            sb.AppendLine("BEGIN");
            sb.AppendLine("    SET NOCOUNT ON;");
            sb.AppendLine();
            sb.AppendLine("    DECLARE @FormId INT;");
            sb.AppendLine("    DECLARE @SubmissionId INT;");
            sb.AppendLine();
            sb.AppendLine("    BEGIN TRY");
            sb.AppendLine("        BEGIN TRANSACTION;");
            sb.AppendLine();
            sb.AppendLine("        -- Get FormId");
            sb.AppendLine("        SELECT @FormId = FormId FROM [dbo].[Forms] WHERE FormName = @FormName;");
            sb.AppendLine();
            sb.AppendLine("        IF @FormId IS NULL");
            sb.AppendLine("            RAISERROR('Form not found', 16, 1);");
            sb.AppendLine();
            sb.AppendLine("        -- Create submission");
            sb.AppendLine("        SET @SubmissionGuid = NEWID();");
            sb.AppendLine("        INSERT INTO [dbo].[Submissions] (SubmissionGuid, FormId, SubmittedBy)");
            sb.AppendLine("        VALUES (@SubmissionGuid, @FormId, @SubmittedBy);");
            sb.AppendLine("        SET @SubmissionId = SCOPE_IDENTITY();");
            sb.AppendLine();
            sb.AppendLine("        -- Parse JSON and insert answers");
            sb.AppendLine("        -- Handle main form fields");
            sb.AppendLine("        INSERT INTO [dbo].[Answers] (SubmissionId, QuestionId, AnswerText, AnswerNumeric, AnswerDate, AnswerBit, AnswerLookupId)");
            sb.AppendLine("        SELECT");
            sb.AppendLine("            @SubmissionId,");
            sb.AppendLine("            q.QuestionId,");
            sb.AppendLine("            CASE WHEN q.FieldType IN ('TextField', 'RichText') THEN JSON_VALUE(@SubmissionDataJSON, '$.' + q.FieldName) END,");
            sb.AppendLine("            CASE WHEN q.FieldType IN ('Number') THEN TRY_CAST(JSON_VALUE(@SubmissionDataJSON, '$.' + q.FieldName) AS DECIMAL(18,4)) END,");
            sb.AppendLine("            CASE WHEN q.FieldType IN ('DatePicker') THEN TRY_CAST(JSON_VALUE(@SubmissionDataJSON, '$.' + q.FieldName) AS DATETIME2) END,");
            sb.AppendLine("            CASE WHEN q.FieldType IN ('CheckBox') THEN TRY_CAST(JSON_VALUE(@SubmissionDataJSON, '$.' + q.FieldName) AS BIT) END,");
            sb.AppendLine("            CASE WHEN q.FieldType IN ('DropDown', 'RadioButton', 'ComboBox')");
            sb.AppendLine("                THEN (SELECT TOP 1 LookupId FROM [dbo].[LookupValues] WHERE QuestionId = q.QuestionId AND LookupValue = JSON_VALUE(@SubmissionDataJSON, '$.' + q.FieldName))");
            sb.AppendLine("            END");
            sb.AppendLine("        FROM [dbo].[Questions] q");
            sb.AppendLine("        WHERE q.FormId = @FormId");
            sb.AppendLine("          AND q.IsInRepeatingSection = 0;");
            sb.AppendLine();
            sb.AppendLine("        -- Handle repeating sections");
            sb.AppendLine("        -- This would parse the repeating section arrays from JSON");
            sb.AppendLine("        -- Create RepeatingSectionInstances and associated Answers");
            sb.AppendLine();
            sb.AppendLine("        COMMIT TRANSACTION;");
            sb.AppendLine("    END TRY");
            sb.AppendLine("    BEGIN CATCH");
            sb.AppendLine("        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;");
            sb.AppendLine("        THROW;");
            sb.AppendLine("    END CATCH");
            sb.AppendLine("END");
            sb.AppendLine("GO");
            sb.AppendLine();

            // Add Repeating Section Instance procedure
            sb.AppendLine("CREATE PROCEDURE [dbo].[sp_AddRepeatingSectionInstance]");
            sb.AppendLine("    @SubmissionId INT,");
            sb.AppendLine("    @SectionName NVARCHAR(255),");
            sb.AppendLine("    @ItemOrder INT,");
            sb.AppendLine("    @InstanceDataJSON NVARCHAR(MAX),");
            sb.AppendLine("    @InstanceId INT OUTPUT");
            sb.AppendLine("AS");
            sb.AppendLine("BEGIN");
            sb.AppendLine("    SET NOCOUNT ON;");
            sb.AppendLine();
            sb.AppendLine("    DECLARE @SectionId INT;");
            sb.AppendLine("    DECLARE @FormId INT;");
            sb.AppendLine();
            sb.AppendLine("    BEGIN TRY");
            sb.AppendLine("        -- Get FormId and SectionId");
            sb.AppendLine("        SELECT @FormId = FormId FROM [dbo].[Submissions] WHERE SubmissionId = @SubmissionId;");
            sb.AppendLine("        SELECT @SectionId = SectionId FROM [dbo].[RepeatingSections] WHERE FormId = @FormId AND SectionName = @SectionName;");
            sb.AppendLine();
            sb.AppendLine("        -- Create instance");
            sb.AppendLine("        INSERT INTO [dbo].[RepeatingSectionInstances] (SubmissionId, SectionId, ItemOrder)");
            sb.AppendLine("        VALUES (@SubmissionId, @SectionId, @ItemOrder);");
            sb.AppendLine("        SET @InstanceId = SCOPE_IDENTITY();");
            sb.AppendLine();
            sb.AppendLine("        -- Insert answers for this instance");
            sb.AppendLine("        INSERT INTO [dbo].[Answers] (SubmissionId, QuestionId, RepeatingSectionInstanceId, AnswerText, AnswerNumeric, AnswerDate, AnswerBit, AnswerLookupId)");
            sb.AppendLine("        SELECT");
            sb.AppendLine("            @SubmissionId,");
            sb.AppendLine("            q.QuestionId,");
            sb.AppendLine("            @InstanceId,");
            sb.AppendLine("            CASE WHEN q.FieldType IN ('TextField', 'RichText') THEN JSON_VALUE(@InstanceDataJSON, '$.' + q.FieldName) END,");
            sb.AppendLine("            CASE WHEN q.FieldType IN ('Number') THEN TRY_CAST(JSON_VALUE(@InstanceDataJSON, '$.' + q.FieldName) AS DECIMAL(18,4)) END,");
            sb.AppendLine("            CASE WHEN q.FieldType IN ('DatePicker') THEN TRY_CAST(JSON_VALUE(@InstanceDataJSON, '$.' + q.FieldName) AS DATETIME2) END,");
            sb.AppendLine("            CASE WHEN q.FieldType IN ('CheckBox') THEN TRY_CAST(JSON_VALUE(@InstanceDataJSON, '$.' + q.FieldName) AS BIT) END,");
            sb.AppendLine("            CASE WHEN q.FieldType IN ('DropDown', 'RadioButton', 'ComboBox')");
            sb.AppendLine("                THEN (SELECT TOP 1 LookupId FROM [dbo].[LookupValues] WHERE QuestionId = q.QuestionId AND LookupValue = JSON_VALUE(@InstanceDataJSON, '$.' + q.FieldName))");
            sb.AppendLine("            END");
            sb.AppendLine("        FROM [dbo].[Questions] q");
            sb.AppendLine("        WHERE q.FormId = @FormId");
            sb.AppendLine("          AND q.RepeatingSectionName = @SectionName;");
            sb.AppendLine();
            sb.AppendLine("    END TRY");
            sb.AppendLine("    BEGIN CATCH");
            sb.AppendLine("        THROW;");
            sb.AppendLine("    END CATCH");
            sb.AppendLine("END");
            sb.AppendLine("GO");

            return new SqlScript
            {
                Name = "Create_Submission_Procedures",
                Type = ScriptType.StoredProcedure,
                Content = sb.ToString(),
                ExecutionOrder = 101,
                Description = "Procedures for submitting form data with repeating section support"
            };
        }

        // 5. Retrieval Procedures
        private SqlScript GenerateNormalizedRetrievalProcedures()
        {
            var sb = new StringBuilder();

            sb.AppendLine("-- ===============================================");
            sb.AppendLine("-- Data Retrieval Procedures");
            sb.AppendLine("-- ===============================================");
            sb.AppendLine();

            sb.AppendLine("CREATE PROCEDURE [dbo].[sp_GetSubmissionData]");
            sb.AppendLine("    @SubmissionGuid UNIQUEIDENTIFIER");
            sb.AppendLine("AS");
            sb.AppendLine("BEGIN");
            sb.AppendLine("    SET NOCOUNT ON;");
            sb.AppendLine();
            sb.AppendLine("    DECLARE @SubmissionId INT;");
            sb.AppendLine("    SELECT @SubmissionId = SubmissionId FROM [dbo].[Submissions] WHERE SubmissionGuid = @SubmissionGuid;");
            sb.AppendLine();
            sb.AppendLine("    -- Get submission metadata");
            sb.AppendLine("    SELECT s.*, f.FormName");
            sb.AppendLine("    FROM [dbo].[Submissions] s");
            sb.AppendLine("    INNER JOIN [dbo].[Forms] f ON s.FormId = f.FormId");
            sb.AppendLine("    WHERE s.SubmissionId = @SubmissionId;");
            sb.AppendLine();
            sb.AppendLine("    -- Get main form answers");
            sb.AppendLine("    SELECT");
            sb.AppendLine("        q.FieldName,");
            sb.AppendLine("        q.DisplayName,");
            sb.AppendLine("        q.FieldType,");
            sb.AppendLine("        COALESCE(a.AnswerText, CAST(a.AnswerNumeric AS NVARCHAR), CONVERT(NVARCHAR, a.AnswerDate, 121),");
            sb.AppendLine("                 CASE WHEN a.AnswerBit = 1 THEN 'true' ELSE 'false' END,");
            sb.AppendLine("                 lv.LookupValue) AS AnswerValue,");
            sb.AppendLine("        lv.LookupDisplayText");
            sb.AppendLine("    FROM [dbo].[Questions] q");
            sb.AppendLine("    LEFT JOIN [dbo].[Answers] a ON q.QuestionId = a.QuestionId AND a.SubmissionId = @SubmissionId AND a.RepeatingSectionInstanceId IS NULL");
            sb.AppendLine("    LEFT JOIN [dbo].[LookupValues] lv ON a.AnswerLookupId = lv.LookupId");
            sb.AppendLine("    WHERE q.FormId = (SELECT FormId FROM [dbo].[Submissions] WHERE SubmissionId = @SubmissionId)");
            sb.AppendLine("      AND q.IsInRepeatingSection = 0");
            sb.AppendLine("    ORDER BY q.DisplayOrder;");
            sb.AppendLine();
            sb.AppendLine("    -- Get repeating section instances");
            sb.AppendLine("    SELECT");
            sb.AppendLine("        rs.SectionName,");
            sb.AppendLine("        rsi.InstanceId,");
            sb.AppendLine("        rsi.ItemOrder");
            sb.AppendLine("    FROM [dbo].[RepeatingSectionInstances] rsi");
            sb.AppendLine("    INNER JOIN [dbo].[RepeatingSections] rs ON rsi.SectionId = rs.SectionId");
            sb.AppendLine("    WHERE rsi.SubmissionId = @SubmissionId");
            sb.AppendLine("    ORDER BY rs.SectionName, rsi.ItemOrder;");
            sb.AppendLine();
            sb.AppendLine("    -- Get repeating section answers");
            sb.AppendLine("    SELECT");
            sb.AppendLine("        rsi.InstanceId,");
            sb.AppendLine("        rs.SectionName,");
            sb.AppendLine("        q.FieldName,");
            sb.AppendLine("        q.DisplayName,");
            sb.AppendLine("        q.FieldType,");
            sb.AppendLine("        COALESCE(a.AnswerText, CAST(a.AnswerNumeric AS NVARCHAR), CONVERT(NVARCHAR, a.AnswerDate, 121),");
            sb.AppendLine("                 CASE WHEN a.AnswerBit = 1 THEN 'true' ELSE 'false' END,");
            sb.AppendLine("                 lv.LookupValue) AS AnswerValue,");
            sb.AppendLine("        lv.LookupDisplayText,");
            sb.AppendLine("        rsi.ItemOrder");
            sb.AppendLine("    FROM [dbo].[RepeatingSectionInstances] rsi");
            sb.AppendLine("    INNER JOIN [dbo].[RepeatingSections] rs ON rsi.SectionId = rs.SectionId");
            sb.AppendLine("    INNER JOIN [dbo].[Questions] q ON q.RepeatingSectionName = rs.SectionName AND q.FormId = rs.FormId");
            sb.AppendLine("    LEFT JOIN [dbo].[Answers] a ON q.QuestionId = a.QuestionId AND a.RepeatingSectionInstanceId = rsi.InstanceId");
            sb.AppendLine("    LEFT JOIN [dbo].[LookupValues] lv ON a.AnswerLookupId = lv.LookupId");
            sb.AppendLine("    WHERE rsi.SubmissionId = @SubmissionId");
            sb.AppendLine("    ORDER BY rs.SectionName, rsi.ItemOrder, q.DisplayOrder;");
            sb.AppendLine();
            sb.AppendLine("END");
            sb.AppendLine("GO");

            return new SqlScript
            {
                Name = "Create_Retrieval_Procedures",
                Type = ScriptType.StoredProcedure,
                Content = sb.ToString(),
                ExecutionOrder = 102,
                Description = "Procedures for retrieving submission data"
            };
        }

        // 6. Reporting Views
        private SqlScript GenerateNormalizedReportingViews()
        {
            var sb = new StringBuilder();

            sb.AppendLine("-- ===============================================");
            sb.AppendLine("-- Reporting Views for Easy Data Access");
            sb.AppendLine("-- ===============================================");
            sb.AppendLine();

            sb.AppendLine("CREATE VIEW [dbo].[vw_AllSubmissions]");
            sb.AppendLine("AS");
            sb.AppendLine("SELECT");
            sb.AppendLine("    s.SubmissionGuid,");
            sb.AppendLine("    f.FormName,");
            sb.AppendLine("    s.SubmittedBy,");
            sb.AppendLine("    s.SubmissionStatus,");
            sb.AppendLine("    s.SubmittedDate,");
            sb.AppendLine("    s.ModifiedDate,");
            sb.AppendLine("    s.Version,");
            sb.AppendLine("    (SELECT COUNT(*) FROM [dbo].[Answers] WHERE SubmissionId = s.SubmissionId AND RepeatingSectionInstanceId IS NULL) AS MainFieldCount,");
            sb.AppendLine("    (SELECT COUNT(DISTINCT InstanceId) FROM [dbo].[RepeatingSectionInstances] WHERE SubmissionId = s.SubmissionId) AS RepeatingSectionCount");
            sb.AppendLine("FROM [dbo].[Submissions] s");
            sb.AppendLine("INNER JOIN [dbo].[Forms] f ON s.FormId = f.FormId;");
            sb.AppendLine("GO");
            sb.AppendLine();

            sb.AppendLine("CREATE VIEW [dbo].[vw_FormAnswersPivot]");
            sb.AppendLine("AS");
            sb.AppendLine("-- This view would dynamically pivot answers for reporting");
            sb.AppendLine("-- Implementation would be form-specific");
            sb.AppendLine("SELECT");
            sb.AppendLine("    s.SubmissionGuid,");
            sb.AppendLine("    f.FormName,");
            sb.AppendLine("    q.FieldName,");
            sb.AppendLine("    q.DisplayName,");
            sb.AppendLine("    COALESCE(a.AnswerText, CAST(a.AnswerNumeric AS NVARCHAR), CONVERT(NVARCHAR, a.AnswerDate, 121),");
            sb.AppendLine("             CASE WHEN a.AnswerBit = 1 THEN 'true' ELSE 'false' END,");
            sb.AppendLine("             lv.LookupDisplayText) AS AnswerValue");
            sb.AppendLine("FROM [dbo].[Submissions] s");
            sb.AppendLine("INNER JOIN [dbo].[Forms] f ON s.FormId = f.FormId");
            sb.AppendLine("INNER JOIN [dbo].[Questions] q ON q.FormId = f.FormId");
            sb.AppendLine("LEFT JOIN [dbo].[Answers] a ON a.SubmissionId = s.SubmissionId AND a.QuestionId = q.QuestionId");
            sb.AppendLine("LEFT JOIN [dbo].[LookupValues] lv ON a.AnswerLookupId = lv.LookupId");
            sb.AppendLine("WHERE q.IsInRepeatingSection = 0;");
            sb.AppendLine("GO");

            return new SqlScript
            {
                Name = "Create_Reporting_Views",
                Type = ScriptType.View,
                Content = sb.ToString(),
                ExecutionOrder = 200,
                Description = "Views for reporting and data analysis"
            };
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