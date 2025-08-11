using System;
using System.Linq;
using FormGenerator.Analyzers.Infopath;

namespace FormGenerator.Analyzers.InfoPath
{
    public static class InfoPathFormDefinitionExtensions
    {
        public static object ToEnhancedJson(this InfoPathFormDefinition formDef)
        {
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
                        // Add section information as properties rather than in the name
                        SectionInfo = !string.IsNullOrEmpty(c.ParentSection) ? new
                        {
                            ParentSection = c.ParentSection,
                            SectionType = c.SectionType,
                            IsInSection = true
                        } : null,
                        // Repeating section info
                        RepeatingSectionInfo = c.IsInRepeatingSection ? new
                        {
                            c.IsInRepeatingSection,
                            c.RepeatingSectionName,
                            c.RepeatingSectionBinding
                        } : null,
                        // Include data options if present
                        DataOptions = c.HasStaticData ? c.DataOptions : null,
                        DataValues = c.HasStaticData ? c.DataOptionsString : null,
                        DefaultValue = GetDefaultValue(c)
                    }).ToList(),
                    // Don't include sections as separate items - they're now part of control info
                    // Sections = v.Sections  // REMOVED
                }).ToList(),
                Rules = formDef.Rules,
                Data = formDef.Data.Select(d => new
                {
                    // Use original column name
                    ColumnName = d.ColumnName,
                    d.Type,
                    d.DisplayName,
                    // Section context
                    Section = !string.IsNullOrEmpty(d.RepeatingSection) ? d.RepeatingSection : null,
                    d.IsRepeating,
                    d.IsConditional,
                    ConditionalOnField = d.IsConditional ? d.ConditionalOnField : null,
                    // Include valid values for columns
                    ValidValues = d.HasConstraints
                        ? d.ValidValues?.Select(v => new { v.Value, v.DisplayText, v.IsDefault })
                        : null,
                    d.DefaultValue
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
                    SectionsSummary = GetSectionsSummary(formDef)
                }
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


        private static string BuildDataColumnNameWithSection(DataColumn dataColumn)
        {
            var baseName = dataColumn.ColumnName;

            // Just return the original name - section info is already in the properties
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

        private static string BuildFullyQualifiedControlName(string viewName, ControlDefinition control)
        {
            var parts = new System.Collections.Generic.List<string>();

            // Add view context (optional)
            // parts.Add($"[{viewName}]");

            // Add section context if exists
            if (!string.IsNullOrEmpty(control.ParentSection))
            {
                var sectionLabel = control.SectionType switch
                {
                    "repeating" => "Repeating Section",
                    "optional" => "Optional Section",
                    "dynamic" => "Dynamic Section",
                    _ => "Section"
                };
                parts.Add($"{control.ParentSection} ({sectionLabel})");
            }
            else if (control.IsInRepeatingSection && !string.IsNullOrEmpty(control.RepeatingSectionName))
            {
                parts.Add($"{control.RepeatingSectionName} (Repeating)");
            }

            // Add control name
            parts.Add(control.Name ?? control.Binding ?? "Unnamed");

            return string.Join(" - ", parts);
        }

        /// <summary>
        /// Builds a fully qualified column name with all context
        /// </summary>
        private static string BuildFullyQualifiedColumnName(DataColumn column)
        {
            var parts = new System.Collections.Generic.List<string>();

            // Add section context if exists
            if (!string.IsNullOrEmpty(column.RepeatingSection))
            {
                var sectionType = column.IsRepeating ? "Repeating Section" : "Section";
                parts.Add($"{column.RepeatingSection} ({sectionType})");
            }

            // Add column name
            parts.Add(column.ColumnName);

            // Add conditional marker if applicable
            if (column.IsConditional)
            {
                parts.Add("[Conditional]");
            }

            return string.Join(" - ", parts);
        }
    }

}
