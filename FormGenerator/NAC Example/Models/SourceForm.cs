using Newtonsoft.Json;

namespace NWConverter.Models
{
    // Removed rigid root wrapper; we now parse root dynamically in MainWindow.xaml.cs

    public class SourceForm
    {
        [JsonProperty("FileName")]
        public string FileName { get; set; } = "";

        [JsonProperty("FormDefinition")]
        public SourceFormDefinition FormDefinition { get; set; } = new();
    }

    public class SourceFormDefinition
    {
        [JsonProperty("Views")]
        public List<SourceView> Views { get; set; } = new();

        [JsonProperty("Rules")]
        public List<object> Rules { get; set; } = new();

        [JsonProperty("Data")]
        public List<SourceDataItem> Data { get; set; } = new();

        [JsonProperty("DynamicSections")]
        public List<DynamicSection> DynamicSections { get; set; } = new();

        [JsonProperty("ConditionalVisibility")]
        public Dictionary<string, List<string>> ConditionalVisibility { get; set; } = new();

        [JsonProperty("Metadata")]
        public Metadata Metadata { get; set; } = new();

        [JsonProperty("SqlDeploymentInfo")]
        public SqlDeploymentInfo SqlDeploymentInfo { get; set; } = new();
    }

    public class SourceView
    {
        [JsonProperty("ViewName")]
        public string ViewName { get; set; } = "";

        [JsonProperty("Controls")]
        public List<SourceControl> Controls { get; set; } = new();

        [JsonProperty("Sections")]
        public List<SourceSection> Sections { get; set; } = new();
    }

    public class SourceControl
    {
        [JsonProperty("Name")]
        public string Name { get; set; } = "";

        [JsonProperty("OriginalName")]
        public string OriginalName { get; set; } = "";

        [JsonProperty("Type")]
        public string Type { get; set; } = "";

        [JsonProperty("Label")]
        public string Label { get; set; } = "";

        [JsonProperty("Binding")]
        public string Binding { get; set; } = "";

        [JsonProperty("GridPosition")]
        public string GridPosition { get; set; } = "";

        [JsonProperty("CtrlId")]
        public string? CtrlId { get; set; }

        [JsonProperty("SectionInfo")]
        public SectionInfo? SectionInfo { get; set; }

        [JsonProperty("RepeatingSectionInfo")]
        public RepeatingSectionInfo? RepeatingSectionInfo { get; set; }

        [JsonProperty("RepeatingSectionName")]
        public string? RepeatingSectionName { get; set; }

        [JsonProperty("DataOptions")]
        public List<DataOption>? DataOptions { get; set; }

        [JsonProperty("DataValues")]
        public string? DataValues { get; set; }

        [JsonProperty("DefaultValue")]
        public string? DefaultValue { get; set; }

        [JsonProperty("AdditionalProperties")]
        public Dictionary<string, object> AdditionalProperties { get; set; } = new();

        [JsonProperty("SqlMapping")]
        public SqlMapping? SqlMapping { get; set; }
    }

    public class SectionInfo
    {
        [JsonProperty("ParentSection")]
        public string? ParentSection { get; set; }

        [JsonProperty("SectionType")]
        public string? SectionType { get; set; }

        [JsonProperty("IsInSection")]
        public bool IsInSection { get; set; } = false;
    }

    public class RepeatingSectionInfo
    {
        [JsonProperty("IsInRepeatingSection")]
        public bool IsInRepeatingSection { get; set; } = false;

        [JsonProperty("RepeatingSectionName")]
        public string? RepeatingSectionName { get; set; }

        [JsonProperty("RepeatingSectionBinding")]
        public string? RepeatingSectionBinding { get; set; }
    }

    public class DataOption
    {
        [JsonProperty("Value")]
        public string Value { get; set; } = "";

        [JsonProperty("DisplayText")]
        public string DisplayText { get; set; } = "";

        [JsonProperty("IsDefault")]
        public bool IsDefault { get; set; } = false;

        [JsonProperty("Order")]
        public int Order { get; set; } = 0;
    }

    public class SqlMapping
    {
        [JsonProperty("TableName")]
        public string TableName { get; set; } = "";

        [JsonProperty("ColumnName")]
        public string ColumnName { get; set; } = "";

        [JsonProperty("SqlDataType")]
        public string SqlDataType { get; set; } = "";

        [JsonProperty("IsInMainTable")]
        public bool IsInMainTable { get; set; } = false;

        [JsonProperty("LookupTable")]
        public string? LookupTable { get; set; }
    }

    public class SourceSection
    {
        [JsonProperty("Name")]
        public string Name { get; set; } = "";

        [JsonProperty("Type")]
        public string Type { get; set; } = "";

        [JsonProperty("CtrlId")]
        public string CtrlId { get; set; } = "";

        [JsonProperty("StartRow")]
        public int StartRow { get; set; } = 0;

        [JsonProperty("EndRow")]
        public int EndRow { get; set; } = 0;

        [JsonProperty("ControlCount")]
        public int ControlCount { get; set; } = 0;

        [JsonProperty("SqlTableName")]
        public string? SqlTableName { get; set; }
    }

    public class SourceDataItem
    {
        [JsonProperty("ColumnName")]
        public string ColumnName { get; set; } = "";

        [JsonProperty("Type")]
        public string Type { get; set; } = "";

        [JsonProperty("DisplayName")]
        public string DisplayName { get; set; } = "";

        [JsonProperty("Section")]
        public string? Section { get; set; }

        [JsonProperty("RepeatingSectionName")]
        public string? RepeatingSectionName { get; set; }

        [JsonProperty("IsRepeating")]
        public bool IsRepeating { get; set; } = false;

        [JsonProperty("IsConditional")]
        public bool IsConditional { get; set; } = false;

        [JsonProperty("ConditionalOnField")]
        public string? ConditionalOnField { get; set; }

        [JsonProperty("ValidValues")]
        public List<DataOption>? ValidValues { get; set; }

        [JsonProperty("DefaultValue")]
        public string? DefaultValue { get; set; }

        [JsonProperty("SqlMapping")]
        public SqlMapping? SqlMapping { get; set; }
    }

    public class DynamicSection
    {
        [JsonProperty("Name")]
        public string Name { get; set; } = "";

        [JsonProperty("Mode")]
        public string Mode { get; set; } = "";

        [JsonProperty("CtrlId")]
        public string? CtrlId { get; set; }

        [JsonProperty("Caption")]
        public string? Caption { get; set; }

        [JsonProperty("Condition")]
        public string Condition { get; set; } = "";

        [JsonProperty("ConditionField")]
        public string ConditionField { get; set; } = "";

        [JsonProperty("ConditionValue")]
        public string ConditionValue { get; set; } = "";

        [JsonProperty("Controls")]
        public List<string> Controls { get; set; } = new();

        [JsonProperty("IsVisible")]
        public bool IsVisible { get; set; } = false;
    }

    public class Metadata
    {
        [JsonProperty("TotalControls")]
        public int TotalControls { get; set; } = 0;

        [JsonProperty("TotalSections")]
        public int TotalSections { get; set; } = 0;

        [JsonProperty("DynamicSectionCount")]
        public int DynamicSectionCount { get; set; } = 0;

        [JsonProperty("RepeatingSectionCount")]
        public int RepeatingSectionCount { get; set; } = 0;

        [JsonProperty("ConditionalFields")]
        public List<string> ConditionalFields { get; set; } = new();

        [JsonProperty("SectionsSummary")]
        public SectionsSummary SectionsSummary { get; set; } = new();

        [JsonProperty("ControlsWithIds")]
        public ControlsWithIds ControlsWithIds { get; set; } = new();

        [JsonProperty("RepeatingSectionMembership")]
        public RepeatingSectionMembership RepeatingSectionMembership { get; set; } = new();
    }

    public class SectionsSummary
    {
        [JsonProperty("TotalUniqueSections")]
        public int TotalUniqueSections { get; set; } = 0;

        [JsonProperty("Sections")]
        public List<SectionSummary> Sections { get; set; } = new();
    }

    public class SectionSummary
    {
        [JsonProperty("SectionName")]
        public string SectionName { get; set; } = "";

        [JsonProperty("Type")]
        public string Type { get; set; } = "";

        [JsonProperty("CtrlId")]
        public string CtrlId { get; set; } = "";

        [JsonProperty("OccurrencesInViews")]
        public int OccurrencesInViews { get; set; } = 0;

        [JsonProperty("Label")]
        public string Label { get; set; } = "";
    }

    public class ControlsWithIds
    {
        [JsonProperty("TotalControlsWithIds")]
        public int TotalControlsWithIds { get; set; } = 0;

        [JsonProperty("Controls")]
        public List<ControlWithId> Controls { get; set; } = new();
    }

    public class ControlWithId
    {
        [JsonProperty("CtrlId")]
        public string CtrlId { get; set; } = "";

        [JsonProperty("Name")]
        public string Name { get; set; } = "";

        [JsonProperty("Type")]
        public string Type { get; set; } = "";

        [JsonProperty("Label")]
        public string Label { get; set; } = "";
    }

    public class RepeatingSectionMembership
    {
        [JsonProperty("TotalRepeatingSections")]
        public int TotalRepeatingSections { get; set; } = 0;

        [JsonProperty("RepeatingSections")]
        public List<RepeatingSectionDetail> RepeatingSections { get; set; } = new();
    }

    public class RepeatingSectionDetail
    {
        [JsonProperty("RepeatingSectionName")]
        public string RepeatingSectionName { get; set; } = "";

        [JsonProperty("ControlCount")]
        public int ControlCount { get; set; } = 0;

        [JsonProperty("Controls")]
        public List<RepeatingSectionControl> Controls { get; set; } = new();
    }

    public class RepeatingSectionControl
    {
        [JsonProperty("Name")]
        public string Name { get; set; } = "";

        [JsonProperty("Label")]
        public string Label { get; set; } = "";

        [JsonProperty("Type")]
        public string Type { get; set; } = "";

        [JsonProperty("CtrlId")]
        public string? CtrlId { get; set; }
    }

    public class SqlDeploymentInfo
    {
        [JsonProperty("ServerName")]
        public string ServerName { get; set; } = "";

        [JsonProperty("DatabaseName")]
        public string DatabaseName { get; set; } = "";

        [JsonProperty("DeploymentDate")]
        public string DeploymentDate { get; set; } = "";

        [JsonProperty("AuthenticationType")]
        public string AuthenticationType { get; set; } = "";

        [JsonProperty("TableStructureType")]
        public string TableStructureType { get; set; } = "";

        [JsonProperty("MainTable")]
        public string MainTable { get; set; } = "";

        [JsonProperty("RepeatingSectionTables")]
        public List<RepeatingSectionTable> RepeatingSectionTables { get; set; } = new();

        [JsonProperty("LookupTables")]
        public List<LookupTable> LookupTables { get; set; } = new();

        [JsonProperty("StoredProcedures")]
        public List<string> StoredProcedures { get; set; } = new();

        [JsonProperty("Views")]
        public List<string> Views { get; set; } = new();
    }

    public class RepeatingSectionTable
    {
        [JsonProperty("SectionName")]
        public string SectionName { get; set; } = "";

        [JsonProperty("TableName")]
        public string TableName { get; set; } = "";

        [JsonProperty("ForeignKeyColumn")]
        public string ForeignKeyColumn { get; set; } = "";

        [JsonProperty("ColumnCount")]
        public int ColumnCount { get; set; } = 0;
    }

    public class LookupTable
    {
        [JsonProperty("FieldName")]
        public string FieldName { get; set; } = "";

        [JsonProperty("LookupTableName")]
        public string LookupTableName { get; set; } = "";

        [JsonProperty("ValueCount")]
        public int ValueCount { get; set; } = 0;

        [JsonProperty("Values")]
        public List<string> Values { get; set; } = new();
    }
}
