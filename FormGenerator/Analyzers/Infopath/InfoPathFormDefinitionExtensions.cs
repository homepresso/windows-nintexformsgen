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
                        DefaultValue = GetDefaultValue(c),
                        // Include other important properties (excluding CtrlId since it's at top level)
                        AdditionalProperties = c.Properties != null
                            ? c.Properties.Where(p => p.Key != "CtrlId" && p.Key != "DefaultValue")
                                         .ToDictionary(p => p.Key, p => p.Value)
                            : null
                    }).ToList(),
                    // Include sections summary for the view
                    Sections = v.Sections.Select(s => new
                    {
                        s.Name,
                        s.Type,
                        s.CtrlId,
                        s.StartRow,
                        s.EndRow,
                        ControlCount = s.ControlIds?.Count ?? 0
                    }).ToList()
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
                    SectionsSummary = GetSectionsSummary(formDef),
                    // Add summary of controls with CtrlIds
                    ControlsWithIds = GetControlsWithIds(formDef)
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

       

    }
}