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
                        c.Name,
                        c.Type,
                        c.Label,
                        c.Binding,
                        c.GridPosition,
                        c.ParentSection,
                        c.IsInRepeatingSection,
                        c.RepeatingSectionName,
                        // Include data options if present
                        DataOptions = c.HasStaticData ? c.DataOptions : null,
                        DataValues = c.HasStaticData ? c.DataOptionsString : null,
                        DefaultValue = c.Properties.ContainsKey("DefaultValue")
                            ? c.Properties["DefaultValue"]
                            : null
                    }).ToList(),
                    Sections = v.Sections
                }).ToList(),
                Rules = formDef.Rules,
                Data = formDef.Data.Select(d => new
                {
                    d.ColumnName,
                    d.Type,
                    d.DisplayName,
                    d.IsRepeating,
                    d.RepeatingSectionPath,
                    d.IsConditional,
                    d.ConditionalOnField,
                    // Include valid values for columns
                    ValidValues = d.HasConstraints
                        ? d.ValidValues?.Select(v => new { v.Value, v.DisplayText, v.IsDefault })
                        : null,
                    d.DefaultValue
                }).ToList(),
                DynamicSections = formDef.DynamicSections,
                ConditionalVisibility = formDef.ConditionalVisibility,
                Metadata = formDef.Metadata
            };
        }
    }
}