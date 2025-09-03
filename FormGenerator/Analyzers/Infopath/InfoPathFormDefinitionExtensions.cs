using System;
using System.Collections.Generic;
using System.Linq;
using FormGenerator.Analyzers.Infopath;
using FormGenerator.Core.Models;

namespace FormGenerator.Analyzers.InfoPath
{
    public static class InfoPathFormDefinitionExtensions
    {
        public static SqlDeploymentInfo CurrentSqlDeploymentInfo { get; set; }

        /// <summary>
        /// Simplified JSON representation that treats sections as cosmetic
        /// </summary>
        public static object ToSimplifiedJson(this InfoPathFormDefinition formDef)
        {
            // Flatten all controls from all views into a single list
            var allControls = new List<object>();

            foreach (var view in formDef.Views)
            {
                foreach (var control in view.Controls)
                {
                    // Skip merged controls
                    if (control.IsMergedIntoParent)
                        continue;

                    var simplifiedControl = new
                    {
                        // Core identification
                        Name = control.Name,
                        Type = control.Type,
                        Label = control.Label,
                        Binding = control.Binding,

                        // Position
                        View = view.ViewName,
                        GridPosition = control.GridPosition,
                        DocIndex = control.DocIndex,

                        // Section context (cosmetic only)
                        Section = !string.IsNullOrEmpty(control.ParentSection) ? new
                        {
                            Name = control.ParentSection,
                            Type = control.SectionType  // "normal", "optional", etc.
                        } : null,

                        // Repeating context (structural - important!)
                        RepeatingContext = control.IsInRepeatingSection ? new
                        {
                            SectionName = control.RepeatingSectionName,
                            Binding = control.RepeatingSectionBinding,
                            IsRepeating = true
                        } : null,

                        // Data options for dropdowns
                        Options = control.HasStaticData ?
                            control.DataOptions.Select(o => new
                            {
                                Value = o.Value,
                                Display = o.DisplayText,
                                IsDefault = o.IsDefault
                            }) : null,

                        // Key properties only
                        Properties = GetKeyProperties(control.Properties)
                    };

                    allControls.Add(simplifiedControl);
                }
            }

            // Build a simplified structure
            return new
            {
                Form = new
                {
                    Name = formDef.FormName,
                    FileName = formDef.FileName,
                    Title = formDef.Title
                },

                // All controls in a flat list
                Controls = allControls,

                // Summary of repeating structures (these affect data model)
                RepeatingStructures = GetRepeatingStructures(formDef),

                // Data columns for SQL generation
                DataColumns = formDef.Data.Select(d => new
                {
                    Name = d.ColumnName,
                    Type = d.Type,
                    DisplayName = d.DisplayName,
                    IsRepeating = d.IsRepeating,
                    RepeatingSectionName = d.RepeatingSection,
                    ValidValues = d.ValidValues?.Select(v => new
                    {
                        v.Value,
                        Display = v.DisplayText
                    }),
                    DefaultValue = d.DefaultValue
                }),

                // Metadata
                Summary = new
                {
                    TotalControls = allControls.Count,
                    TotalViews = formDef.Views.Count,
                    RepeatingSectionCount = formDef.Metadata.RepeatingSectionCount,
                    DynamicSectionCount = formDef.Metadata.DynamicSectionCount,
                    ControlTypes = GetControlTypeSummary(allControls)
                }
            };
        }

        /// <summary>
        /// Original enhanced JSON method (kept for compatibility)
        /// </summary>
        public static object ToEnhancedJson(this InfoPathFormDefinition formDef)
        {
            // Check if we have SQL deployment info for this form
            FormSqlMapping formMapping = null;
            if (CurrentSqlDeploymentInfo != null)
            {
                formMapping = CurrentSqlDeploymentInfo.FormMappings?.FirstOrDefault(f =>
                    f.FormName.Equals(formDef.FormName, StringComparison.OrdinalIgnoreCase));
            }

            return new
            {
                Views = formDef.Views.Select(v => new
                {
                    ViewName = v.ViewName,
                    Controls = v.Controls.Select(c => new
                    {
                        // Include section name in the control name if it's in a section
                        Name = BuildControlNameWithSection(c),
                        OriginalName = c.Name,  // Keep original for reference
                        c.Type,
                        c.Label,
                        c.Binding,
                        c.GridPosition,
                        // IMPORTANT: Include CtrlId at the top level
                        CtrlId = c.Properties != null && c.Properties.ContainsKey("CtrlId")
                            ? c.Properties["CtrlId"]
                            : null,
                        // Add section information as properties rather than in the name
                        SectionInfo = !string.IsNullOrEmpty(c.ParentSection) ? new
                        {
                            ParentSection = c.ParentSection,
                            SectionType = c.SectionType,
                            IsInSection = true
                        } : null,
                        // Repeating section info - ENHANCED TO ALWAYS SHOW WHEN IN REPEATING SECTION
                        RepeatingSectionInfo = c.IsInRepeatingSection ? new
                        {
                            c.IsInRepeatingSection,
                            RepeatingSectionName = c.RepeatingSectionName,  // THIS IS THE KEY ADDITION
                            c.RepeatingSectionBinding
                        } : null,
                        // ADD THIS NEW PROPERTY - Simple repeating section name at top level for easier access
                        RepeatingSectionName = c.IsInRepeatingSection ? c.RepeatingSectionName : null,
                        // Include data options if present
                        DataOptions = c.HasStaticData ? c.DataOptions : null,
                        DataValues = c.HasStaticData ? c.DataOptionsString : null,
                        DefaultValue = GetDefaultValue(c),
                        // Include other important properties (excluding CtrlId since it's at top level)
                        AdditionalProperties = c.Properties != null
                            ? c.Properties.Where(p => p.Key != "CtrlId" && p.Key != "DefaultValue")
                                         .ToDictionary(p => p.Key, p => p.Value)
                            : null,
                        // ADD SQL MAPPING INFO WHEN AVAILABLE
                        SqlMapping = formMapping != null ? GetControlSqlMapping(c, formMapping) : null
                    }).ToList(),
                    // Include sections summary for the view
                    Sections = v.Sections.Select(s => new
                    {
                        s.Name,
                        s.Type,
                        s.CtrlId,
                        s.StartRow,
                        s.EndRow,
                        ControlCount = s.ControlIds?.Count ?? 0,
                        // ADD SQL TABLE MAPPING WHEN AVAILABLE
                        SqlTableName = formMapping?.RepeatingSectionMappings?
                            .FirstOrDefault(rs => rs.SectionName.Equals(s.Name, StringComparison.OrdinalIgnoreCase))
                            ?.TableName
                    }).ToList()
                }).ToList(),
                Rules = formDef.Rules,
                Data = formDef.Data.Select(d => new
                {
                    // Use original column name
                    ColumnName = d.ColumnName,
                    d.Type,
                    d.DisplayName,
                    // Section context - INCLUDING REPEATING SECTION NAME
                    Section = !string.IsNullOrEmpty(d.RepeatingSection) ? d.RepeatingSection : null,
                    RepeatingSectionName = d.IsRepeating ? d.RepeatingSection : null,  // ADD THIS FOR CLARITY
                    d.IsRepeating,
                    // Include valid values for columns
                    ValidValues = d.HasConstraints
                        ? d.ValidValues?.Select(v => new { v.Value, v.DisplayText, v.IsDefault })
                        : null,
                    d.DefaultValue,
                    // ADD SQL MAPPING WHEN AVAILABLE
                    SqlMapping = formMapping != null ? GetDataColumnSqlMapping(d, formMapping) : null
                }).ToList(),
                DynamicSections = formDef.DynamicSections.Select(ds => new
                {
                    // Label dynamic sections clearly
                    Name = $"Dynamic Section: {ds.Caption ?? ds.Mode}",
                    ds.Mode,
                    ds.CtrlId,
                    ds.Caption,
                    ds.Condition,
                    ds.ConditionField,
                    ds.ConditionValue,
                    ds.Controls,
                    ds.IsVisible
                }).ToList(),
                ConditionalVisibility = formDef.ConditionalVisibility,
                Metadata = new
                {
                    formDef.Metadata.TotalControls,
                    formDef.Metadata.TotalSections,
                    formDef.Metadata.DynamicSectionCount,
                    formDef.Metadata.RepeatingSectionCount,
                    formDef.Metadata.ConditionalFields,
                    // Add summary of sections found
                    SectionsSummary = GetSectionsSummary(formDef),
                    // Add summary of controls with CtrlIds
                    ControlsWithIds = GetControlsWithIds(formDef),
                    // ADD THIS: Summary of which controls are in which repeating sections
                    RepeatingSectionMembership = GetRepeatingSectionMembership(formDef)
                },
                // ADD SQL DEPLOYMENT INFO WHEN AVAILABLE
                SqlDeploymentInfo = CurrentSqlDeploymentInfo != null && formMapping != null ? new
                {
                    ServerName = CurrentSqlDeploymentInfo.ServerName,
                    DatabaseName = CurrentSqlDeploymentInfo.DatabaseName,
                    DeploymentDate = CurrentSqlDeploymentInfo.DeploymentDate,
                    AuthenticationType = CurrentSqlDeploymentInfo.AuthenticationType,
                    TableStructureType = CurrentSqlDeploymentInfo.TableStructureType,
                    MainTable = formMapping.MainTableName,
                    RepeatingSectionTables = formMapping.RepeatingSectionMappings?.Select(rs => new
                    {
                        rs.SectionName,
                        rs.TableName,
                        rs.ForeignKeyColumn,
                        ColumnCount = rs.Columns?.Count ?? 0
                    }),
                    LookupTables = formMapping.LookupTableMappings?.Select(lt => new
                    {
                        lt.FieldName,
                        lt.LookupTableName,
                        lt.ValueCount,
                        Values = lt.LookupValues.Take(5) // Show first 5 values as sample
                    }),
                    StoredProcedures = formMapping.StoredProcedures,
                    Views = formMapping.Views
                } : null
            };
        }

        #region Helper Methods for Simplified JSON

        private static Dictionary<string, object> GetKeyProperties(Dictionary<string, string> props)
        {
            if (props == null || !props.Any())
                return null;

            var keyProps = new Dictionary<string, object>();

            // Only include important properties
            string[] importantKeys = { "CtrlId", "DefaultValue", "Required", "MaxLength", "Pattern", "ReadOnly" };

            foreach (var key in importantKeys)
            {
                if (props.ContainsKey(key) && !string.IsNullOrEmpty(props[key]))
                {
                    keyProps[key] = props[key];
                }
            }

            return keyProps.Any() ? keyProps : null;
        }

        private static List<object> GetRepeatingStructures(InfoPathFormDefinition formDef)
        {
            var structures = new List<object>();

            // Get unique repeating sections from all views
            var repeatingSections = formDef.Views
                .SelectMany(v => v.Controls)
                .Where(c => c.IsInRepeatingSection)
                .Select(c => c.RepeatingSectionName)
                .Distinct()
                .Where(name => !string.IsNullOrEmpty(name));

            foreach (var sectionName in repeatingSections)
            {
                var controls = formDef.Views
                    .SelectMany(v => v.Controls)
                    .Where(c => c.RepeatingSectionName == sectionName && !c.IsMergedIntoParent)
                    .ToList();

                structures.Add(new
                {
                    Name = sectionName,
                    Type = "RepeatingSection",
                    ControlCount = controls.Count,
                    ControlTypes = controls.GroupBy(c => c.Type)
                        .Select(g => new { Type = g.Key, Count = g.Count() })
                        .OrderByDescending(x => x.Count)
                        .ToList()
                });
            }

            // Add repeating tables
            var repeatingTables = formDef.Views
                .SelectMany(v => v.Controls)
                .Where(c => c.Type == "RepeatingTable")
                .Select(c => c.Name)
                .Distinct();

            foreach (var tableName in repeatingTables)
            {
                structures.Add(new
                {
                    Name = tableName,
                    Type = "RepeatingTable",
                    ControlCount = 0,
                    ControlTypes = (List<object>)null
                });
            }

            return structures;
        }

        private static Dictionary<string, int> GetControlTypeSummary(List<object> controls)
        {
            var summary = new Dictionary<string, int>();

            foreach (dynamic control in controls)
            {
                string type = control.Type;
                if (!summary.ContainsKey(type))
                    summary[type] = 0;
                summary[type]++;
            }

            return summary;
        }

        #endregion

        #region Helper Methods for Enhanced JSON (Original)

        private static object GetControlSqlMapping(ControlDefinition control, FormSqlMapping mapping)
        {
            // Skip labels and non-data controls
            if (control.Type == "Label" || control.Type == "span" || string.IsNullOrEmpty(control.Name))
                return null;

            var columnMapping = mapping.ColumnMappings?.FirstOrDefault(cm =>
                cm.FieldName.Equals(control.Name, StringComparison.OrdinalIgnoreCase));

            if (columnMapping == null)
                return null;

            string tableName = columnMapping.IsInMainTable ? mapping.MainTableName : null;

            // If in repeating section, find the table
            if (control.IsInRepeatingSection && !string.IsNullOrEmpty(control.RepeatingSectionName))
            {
                var sectionMapping = mapping.RepeatingSectionMappings?.FirstOrDefault(rs =>
                    rs.SectionName.Equals(control.RepeatingSectionName, StringComparison.OrdinalIgnoreCase));
                tableName = sectionMapping?.TableName;
            }

            // Check for lookup table
            string lookupTable = null;
            if (control.HasStaticData && control.DataOptions != null && control.DataOptions.Any())
            {
                var lookupMapping = mapping.LookupTableMappings?.FirstOrDefault(lt =>
                    lt.FieldName.Equals(control.Name, StringComparison.OrdinalIgnoreCase));
                lookupTable = lookupMapping?.LookupTableName;
            }

            return new
            {
                TableName = tableName,
                ColumnName = columnMapping.ColumnName,
                SqlDataType = columnMapping.SqlDataType,
                IsInMainTable = columnMapping.IsInMainTable,
                LookupTable = lookupTable
            };
        }

        private static object GetDataColumnSqlMapping(DataColumn dataColumn, FormSqlMapping mapping)
        {
            // Skip non-data columns
            if (dataColumn.Type == "RepeatingTable" || dataColumn.Type == "span" ||
                dataColumn.ColumnName?.StartsWith("string(") == true)
                return null;

            var columnMapping = mapping.ColumnMappings?.FirstOrDefault(cm =>
                cm.FieldName.Equals(dataColumn.ColumnName, StringComparison.OrdinalIgnoreCase));

            if (columnMapping == null)
                return null;

            string tableName = mapping.MainTableName;
            if (dataColumn.IsRepeating && !string.IsNullOrEmpty(dataColumn.RepeatingSection))
            {
                var sectionMapping = mapping.RepeatingSectionMappings?.FirstOrDefault(rs =>
                    rs.SectionName.Equals(dataColumn.RepeatingSection, StringComparison.OrdinalIgnoreCase));
                tableName = sectionMapping?.TableName ?? tableName;
            }

            return new
            {
                TableName = tableName,
                ColumnName = columnMapping.ColumnName,
                SqlDataType = columnMapping.SqlDataType,
                IsInMainTable = !dataColumn.IsRepeating
            };
        }

        private static object GetRepeatingSectionMembership(InfoPathFormDefinition formDef)
        {
            var membership = formDef.Views
                .SelectMany(v => v.Controls)
                .Where(c => c.IsInRepeatingSection && !string.IsNullOrEmpty(c.RepeatingSectionName))
                .GroupBy(c => c.RepeatingSectionName)
                .Select(g => new
                {
                    RepeatingSectionName = g.Key,
                    ControlCount = g.Count(),
                    Controls = g.Select(c => new
                    {
                        Name = c.Name,
                        Label = c.Label,
                        Type = c.Type,
                        CtrlId = c.Properties?.ContainsKey("CtrlId") == true ? c.Properties["CtrlId"] : null
                    }).OrderBy(c => c.Name).ToList()
                })
                .OrderBy(s => s.RepeatingSectionName)
                .ToList();

            return new
            {
                TotalRepeatingSections = membership.Count,
                RepeatingSections = membership
            };
        }

        private static string GetDefaultValue(ControlDefinition control)
        {
            // Safely get default value from properties
            if (control.Properties != null && control.Properties.ContainsKey("DefaultValue"))
            {
                return control.Properties["DefaultValue"];
            }
            return null;
        }

        private static string BuildControlNameWithSection(ControlDefinition control)
        {
            var baseName = control.Name;

            // Just return the original name - section info is already in the properties
            // We don't need to duplicate it in the name field
            return baseName;
        }

        private static string GetSectionLabel(string sectionType)
        {
            return sectionType?.ToLower() switch
            {
                "repeating" => "[Repeating Section]",
                "optional" => "[Optional Section]",
                "dynamic" => "[Dynamic Section]",
                "section" => "[Section]",
                _ => "[Section]"
            };
        }

        /// <summary>
        /// Creates a summary of all sections found
        /// </summary>
        private static object GetSectionsSummary(InfoPathFormDefinition formDef)
        {
            var allSections = formDef.Views
                .SelectMany(v => v.Sections)
                .GroupBy(s => s.Name)
                .Select(g => new
                {
                    SectionName = g.Key,
                    Type = g.First().Type,
                    CtrlId = g.First().CtrlId,
                    OccurrencesInViews = g.Count(),
                    Label = GetSectionLabel(g.First().Type)
                })
                .OrderBy(s => s.SectionName)
                .ToList();

            return new
            {
                TotalUniqueSections = allSections.Count,
                Sections = allSections
            };
        }

        /// <summary>
        /// Gets a summary of controls that have CtrlIds
        /// </summary>
        private static object GetControlsWithIds(InfoPathFormDefinition formDef)
        {
            var controlsWithIds = formDef.Views
                .SelectMany(v => v.Controls)
                .Where(c => c.Properties != null && c.Properties.ContainsKey("CtrlId"))
                .Select(c => new
                {
                    CtrlId = c.Properties["CtrlId"],
                    Name = c.Name,
                    Type = c.Type,
                    Label = c.Label
                })
                .OrderBy(c => c.CtrlId)
                .ToList();

            return new
            {
                TotalControlsWithIds = controlsWithIds.Count,
                Controls = controlsWithIds
            };
        }

        #endregion
    }
}