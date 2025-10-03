using System;
using System.Collections.Generic;

namespace FormGenerator.Core.Models
{
    public class SqlDeploymentInfo
    {
        public string ServerName { get; set; }
        public string DatabaseName { get; set; }
        public DateTime DeploymentDate { get; set; }
        public string AuthenticationType { get; set; }
        public string TableStructureType { get; set; } // "FlatTables" or "NormalizedQA"
        public List<FormSqlMapping> FormMappings { get; set; } = new List<FormSqlMapping>();
    }

    public class FormSqlMapping
    {
        public string FormName { get; set; }
        public string MainTableName { get; set; }
        public List<ColumnMapping> ColumnMappings { get; set; } = new List<ColumnMapping>();
        public List<RepeatingSectionMapping> RepeatingSectionMappings { get; set; } = new List<RepeatingSectionMapping>();
        public List<LookupTableMapping> LookupTableMappings { get; set; } = new List<LookupTableMapping>();
        public List<string> StoredProcedures { get; set; } = new List<string>();
        public List<string> Views { get; set; } = new List<string>();
    }

    public class ColumnMapping
    {
        public string FieldName { get; set; }
        public string ColumnName { get; set; }
        public string SqlDataType { get; set; }
        public string ControlType { get; set; }
        public bool IsInMainTable { get; set; }
    }

    public class RepeatingSectionMapping
    {
        public string SectionName { get; set; }
        public string TableName { get; set; }
        public string ForeignKeyColumn { get; set; }
        public List<ColumnMapping> Columns { get; set; } = new List<ColumnMapping>();

        // Enhanced properties for nested section support
        public bool IsNested { get; set; }
        public string ParentSectionName { get; set; }
        public string ParentTableName { get; set; }
        public string ParentForeignKeyColumn { get; set; }
    }

    public class LookupTableMapping
    {
        public string FieldName { get; set; }
        public string LookupTableName { get; set; }
        public int ValueCount { get; set; }
        public List<string> LookupValues { get; set; } = new List<string>();
    }
}