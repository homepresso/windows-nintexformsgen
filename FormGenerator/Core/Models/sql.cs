// Add to FormGenerator/Core/Models/SqlDeploymentMetadata.cs
using System;
using System.Collections.Generic;

namespace FormGenerator.Core.Models
{
    /// <summary>
    /// Captures SQL deployment information for each form
    /// </summary>
    public class SqlDeploymentMetadata
    {
        public string FormName { get; set; }
        public string DeploymentDate { get; set; }
        public TableStructureType StructureType { get; set; }
        public string DatabaseName { get; set; }
        public string ServerName { get; set; }

        // Main table information
        public TableInfo MainTable { get; set; }

        // Repeating section tables
        public List<RepeatingSectionTableInfo> RepeatingSectionTables { get; set; } = new List<RepeatingSectionTableInfo>();

        // Lookup tables for dropdowns
        public List<LookupTableInfo> LookupTables { get; set; } = new List<LookupTableInfo>();

        // Stored procedures
        public StoredProcedureInfo StoredProcedures { get; set; }

        // Field to table/column mappings
        public List<FieldMapping> FieldMappings { get; set; } = new List<FieldMapping>();

        // Views
        public List<string> Views { get; set; } = new List<string>();

        // Indexes
        public List<string> Indexes { get; set; } = new List<string>();
    }

    public class TableInfo
    {
        public string TableName { get; set; }
        public string Schema { get; set; } = "dbo";
        public List<ColumnInfo> Columns { get; set; } = new List<ColumnInfo>();
        public string PrimaryKey { get; set; }
        public List<string> UniqueConstraints { get; set; } = new List<string>();
    }

    public class ColumnInfo
    {
        public string ColumnName { get; set; }
        public string DataType { get; set; }
        public bool IsNullable { get; set; }
        public string DefaultValue { get; set; }
        public string SourceControlName { get; set; }
        public string SourceControlType { get; set; }
    }

    public class RepeatingSectionTableInfo : TableInfo
    {
        public string SectionName { get; set; }
        public string ParentTableName { get; set; }
        public string ForeignKeyColumn { get; set; }
        public List<string> ChildControls { get; set; } = new List<string>();
    }

    public class LookupTableInfo : TableInfo
    {
        public string SourceFieldName { get; set; }
        public string SourceControlName { get; set; }
        public List<LookupValue> Values { get; set; } = new List<LookupValue>();
    }

    public class LookupValue
    {
        public string Value { get; set; }
        public string DisplayText { get; set; }
        public int SortOrder { get; set; }
        public bool IsDefault { get; set; }
    }

    public class StoredProcedureInfo
    {
        // CRUD operations
        public ProcedureDetail Insert { get; set; }
        public ProcedureDetail Update { get; set; }
        public ProcedureDetail Get { get; set; }
        public ProcedureDetail Delete { get; set; }
        public ProcedureDetail List { get; set; }

        // Form-specific procedures for normalized structure
        public ProcedureDetail Submit { get; set; }
        public ProcedureDetail Retrieve { get; set; }

        // Repeating section procedures
        public Dictionary<string, RepeatingSectionProcedures> RepeatingSectionProcedures { get; set; }
            = new Dictionary<string, RepeatingSectionProcedures>();
    }

    public class ProcedureDetail
    {
        public string ProcedureName { get; set; }
        public List<ParameterInfo> Parameters { get; set; } = new List<ParameterInfo>();
        public List<string> ReturnSets { get; set; } = new List<string>();
    }

    public class ParameterInfo
    {
        public string Name { get; set; }
        public string DataType { get; set; }
        public string Direction { get; set; } = "INPUT";
        public bool IsRequired { get; set; }
        public string MapsToField { get; set; }
    }

    public class RepeatingSectionProcedures
    {
        public string SectionName { get; set; }
        public ProcedureDetail InsertItem { get; set; }
        public ProcedureDetail UpdateItem { get; set; }
        public ProcedureDetail DeleteItem { get; set; }
        public ProcedureDetail GetByParent { get; set; }
    }

    public class FieldMapping
    {
        public string FieldName { get; set; }
        public string ControlName { get; set; }
        public string ControlType { get; set; }
        public string TableName { get; set; }
        public string ColumnName { get; set; }
        public string DataType { get; set; }
        public bool IsInRepeatingSection { get; set; }
        public string RepeatingSectionName { get; set; }
        public bool HasLookup { get; set; }
        public string LookupTableName { get; set; }
    }
}