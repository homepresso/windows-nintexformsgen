using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FormGenerator.Analyzers.Infopath;
using FormGenerator.Core.Models;
using FormGenerator.Services;

namespace FormGenerator.Core.Interfaces
{
    /// <summary>
    /// Base interface for all form analyzers
    /// </summary>
    public interface IFormAnalyzer
    {
        string AnalyzerName { get; }
        string SupportedVersion { get; }
        string[] SupportedFileExtensions { get; }

        Task<FormAnalysisResult> AnalyzeFormAsync(string filePath);
        bool CanAnalyze(string filePath);
    }

    /// <summary>
    /// Interface for SQL generation from form definitions
    /// </summary>
    public interface ISqlGenerator
    {
        SqlDialect Dialect { get; set; }
        Task<SqlGenerationResult> GenerateFromAnalysisAsync(FormAnalysisResult analysis);
        Task<SqlGenerationResult> GenerateFromAnalysisAsync(FormAnalysisResult analysis, TableStructureType? structureType);
    }

    /// <summary>
    /// Interface for form rebuilders (K2, NAC, etc.)
    /// </summary>
    public interface IFormRebuilder
    {
        string TargetPlatform { get; }
        Task<FormRebuildResult> RebuildFormAsync(FormAnalysisResult analysis);
    }

    /// <summary>
    /// Interface for export functionality
    /// </summary>
    public interface IExporter
    {
        string FormatName { get; }
        string FileExtension { get; }
        Task ExportAsync(FormAnalysisResult analysis, string outputPath);
    }
}

namespace FormGenerator.Core.Models
{
    /// <summary>
    /// Result of form analysis
    /// </summary>
    public class FormAnalysisResult
    {
        public string FormName { get; set; }
        public string FormType { get; set; }
        public string AnalyzerUsed { get; set; }
        public DateTime AnalysisDate { get; set; }
        public TimeSpan AnalysisDuration { get; set; }

        public InfoPathFormDefinition FormDefinition { get; set; }
        public List<AnalysisMessage> Messages { get; set; } = new List<AnalysisMessage>();
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }

        public object SimplifiedJson { get; set; }

        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();

        public string GetSimplifiedJsonString()
        {
            if (SimplifiedJson == null)
                return "{}";

            return System.Text.Json.JsonSerializer.Serialize(SimplifiedJson, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });
        }
    }
}

    /// <summary>
    /// Analysis message (info, warning, error)
    /// </summary>
    public class AnalysisMessage
    {
        public MessageSeverity Severity { get; set; }
        public string Message { get; set; }
        public string Details { get; set; }
        public string Source { get; set; }
    }

    public enum MessageSeverity
    {
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// SQL generation result
    /// </summary>
    public class SqlGenerationResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public List<SqlScript> Scripts { get; set; } = new List<SqlScript>();
        public SqlDialect Dialect { get; set; }
        public TableStructureType StructureType { get; set; }
        public DateTime GeneratedDate { get; set; }
        public int TotalScripts => Scripts?.Count ?? 0;
    }

    public class SqlScript
    {
        public string Name { get; set; }
        public string Content { get; set; }
        public ScriptType Type { get; set; }
        public int ExecutionOrder { get; set; }
        public string Description { get; set; }
    }
    public enum SqlDialect
    {
        SqlServer,
        MySql,
        PostgreSql,
        Oracle
    }

    public enum ScriptType
    {
        Table,
        Index,
        Constraint,
        StoredProcedure,
        View,
        Trigger,
        Data,
        Other
    }

    public enum TableStructureType
    {
        FlatTables,
        NormalizedQA
    }

    /// <summary>
    /// Form rebuild result
    /// </summary>
    public class FormRebuildResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string TargetPlatform { get; set; }
        public byte[] OutputData { get; set; }
        public string OutputPath { get; set; }
        public Dictionary<string, string> Artifacts { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// File information for UI
    /// </summary>
    public class FormFileInfo
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public string Status { get; set; }
        public long FileSize { get; set; }
        public DateTime UploadedDate { get; set; }
        public FormAnalysisResult AnalysisResult { get; set; }
    }
