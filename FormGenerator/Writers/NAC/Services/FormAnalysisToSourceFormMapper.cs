using FormGenerator.Core.Models;
using FormGenerator.Analyzers.Infopath;
using FormGenerator.Writers.NAC.Models;
using InfoPathDataOption = FormGenerator.Analyzers.Infopath.DataOption;
using NacDataOption = FormGenerator.Writers.NAC.Models.DataOption;
using InfoPathSectionInfo = FormGenerator.Analyzers.Infopath.SectionInfo;
using NacSectionInfo = FormGenerator.Writers.NAC.Models.SectionInfo;
using InfoPathDynamicSection = FormGenerator.Analyzers.Infopath.DynamicSection;
using NacDynamicSection = FormGenerator.Writers.NAC.Models.DynamicSection;

namespace FormGenerator.Writers.NAC.Services
{
    /// <summary>
    /// Maps InfoPath FormAnalysisResult to NAC SourceForm format
    /// This is the bridge between InfoPath analysis and Nintex conversion
    /// </summary>
    public class FormAnalysisToSourceFormMapper
    {
        /// <summary>
        /// Convert FormAnalysisResult (InfoPath analysis) to SourceForm (NAC input)
        /// </summary>
        public SourceForm MapToSourceForm(FormAnalysisResult analysis)
        {
            if (analysis == null)
                throw new ArgumentNullException(nameof(analysis));

            if (analysis.FormDefinition == null)
                throw new ArgumentException("FormAnalysisResult must contain a FormDefinition", nameof(analysis));

            var infoPathForm = analysis.FormDefinition;

            var sourceForm = new SourceForm
            {
                FileName = infoPathForm.FileName ?? analysis.FormName ?? "UnknownForm",
                FormDefinition = new SourceFormDefinition
                {
                    Views = MapViews(infoPathForm.Views),
                    Data = MapDataColumns(infoPathForm.Data),
                    DynamicSections = MapDynamicSections(infoPathForm.DynamicSections),
                    Metadata = MapMetadata(infoPathForm.Metadata),
                    Rules = new List<object>() // Rules are converted differently in NAC
                }
            };

            return sourceForm;
        }

        /// <summary>
        /// Map InfoPath views to NAC SourceView format
        /// </summary>
        private List<SourceView> MapViews(List<ViewDefinition> infoPathViews)
        {
            if (infoPathViews == null || !infoPathViews.Any())
                return new List<SourceView>();

            return infoPathViews.Select(view => new SourceView
            {
                ViewName = view.ViewName ?? "view1.xsl",
                Controls = MapControls(view.Controls),
                Sections = MapSections(view.Sections)
            }).ToList();
        }

        /// <summary>
        /// Map InfoPath controls to NAC SourceControl format
        /// Recursively flattens nested controls from repeating sections
        /// </summary>
        private List<SourceControl> MapControls(List<ControlDefinition> infoPathControls)
        {
            if (infoPathControls == null || !infoPathControls.Any())
                return new List<SourceControl>();

            var result = new List<SourceControl>();

            foreach (var ctrl in infoPathControls)
            {
                // Create the source control for this control
                var sourceControl = new SourceControl
                {
                    Name = ctrl.Name ?? $"Control_{Guid.NewGuid():N}",
                    Type = ctrl.Type ?? "TextField",
                    Label = ctrl.Label,
                    Binding = ctrl.Binding,
                    GridPosition = ctrl.GridPosition,
                    DataOptions = MapDataOptions(ctrl.DataOptions),
                    RepeatingSectionInfo = MapRepeatingSectionInfo(ctrl),
                    RepeatingSectionName = ctrl.RepeatingSectionName,
                    CtrlId = ctrl.Name // Use Name as CtrlId if not set
                };

                // Store additional properties like IsReadOnly, IsRequired, etc.
                if (ctrl.Properties != null && ctrl.Properties.Any())
                {
                    foreach (var prop in ctrl.Properties)
                    {
                        sourceControl.AdditionalProperties[prop.Key] = prop.Value;
                    }
                }

                // Add common properties to AdditionalProperties for easier access
                if (ctrl.Properties?.ContainsKey("ReadOnly") == true)
                {
                    sourceControl.AdditionalProperties["IsReadOnly"] =
                        ctrl.Properties["ReadOnly"].ToLower() == "true";
                }
                if (ctrl.Properties?.ContainsKey("Required") == true)
                {
                    sourceControl.AdditionalProperties["IsRequired"] =
                        ctrl.Properties["Required"].ToLower() == "true";
                }

                // Add the control to the result list (including RepeatingTable/RepeatingSection containers)
                result.Add(sourceControl);

                // If this control has child controls (e.g., RepeatingSection, RepeatingTable),
                // recursively flatten them and add them to the result list too
                if (ctrl.Controls != null && ctrl.Controls.Any())
                {
                    var childControls = MapControls(ctrl.Controls);
                    result.AddRange(childControls);
                }
            }

            return result;
        }

        /// <summary>
        /// Map data options for dropdown/choice controls
        /// </summary>
        private List<NacDataOption> MapDataOptions(List<InfoPathDataOption> infoPathOptions)
        {
            if (infoPathOptions == null || !infoPathOptions.Any())
                return new List<NacDataOption>();

            return infoPathOptions.Select(opt => new NacDataOption
            {
                Value = opt.Value,
                DisplayText = opt.DisplayText ?? opt.Value,
                IsDefault = opt.IsDefault,
                Order = opt.Order
            }).ToList();
        }

        /// <summary>
        /// Map repeating section information if control is in repeating section
        /// </summary>
        private RepeatingSectionInfo? MapRepeatingSectionInfo(ControlDefinition ctrl)
        {
            if (!ctrl.IsInRepeatingSection)
                return null;

            return new RepeatingSectionInfo
            {
                IsInRepeatingSection = true,
                RepeatingSectionName = ctrl.RepeatingSectionName,
                RepeatingSectionBinding = ctrl.RepeatingSectionBinding
            };
        }

        /// <summary>
        /// Map section information
        /// </summary>
        private List<SourceSection> MapSections(List<InfoPathSectionInfo> infoPathSections)
        {
            if (infoPathSections == null || !infoPathSections.Any())
                return new List<SourceSection>();

            return infoPathSections.Select(section => new SourceSection
            {
                Name = section.Name,
                Type = section.Type,
                CtrlId = section.CtrlId,
                StartRow = section.StartRow,
                EndRow = section.EndRow,
                ControlCount = section.ControlIds?.Count ?? 0
            }).ToList();
        }

        /// <summary>
        /// Map InfoPath data columns to NAC SourceDataItem format
        /// </summary>
        private List<SourceDataItem> MapDataColumns(List<DataColumn> infoPathData)
        {
            if (infoPathData == null || !infoPathData.Any())
                return new List<SourceDataItem>();

            return infoPathData.Select(dataCol => new SourceDataItem
            {
                ColumnName = dataCol.ColumnName,
                DisplayName = dataCol.DisplayName ?? dataCol.ColumnName,
                Type = MapDataType(dataCol.Type ?? dataCol.DataType),
                ValidValues = MapValidValues(dataCol.ValidValues),
                IsRepeating = dataCol.IsRepeating
            }).ToList();
        }

        /// <summary>
        /// Map data type from InfoPath to NAC format
        /// </summary>
        private string MapDataType(string infoPathType)
        {
            if (string.IsNullOrEmpty(infoPathType))
                return "TextField";

            // Map InfoPath data types to control types
            return infoPathType.ToLower() switch
            {
                "string" or "text" => "TextField",
                "int" or "integer" or "wholnumber" => "Number",
                "decimal" or "float" or "double" => "Decimal",
                "date" => "DatePicker",
                "datetime" => "DatePicker",
                "boolean" or "bool" => "CheckBox",
                "choice" => "DropDown",
                "multichoice" => "Choice",
                "user" => "PeoplePicker",
                "hyperlink" or "url" => "TextField",
                "email" => "Email",
                "file" or "attachment" => "FileUpload",
                _ => "TextField"
            };
        }

        /// <summary>
        /// Map valid values for choice fields
        /// </summary>
        private List<NacDataOption> MapValidValues(List<InfoPathDataOption>? validValues)
        {
            if (validValues == null || !validValues.Any())
                return new List<NacDataOption>();

            return validValues.Select(vv => new NacDataOption
            {
                Value = vv.Value,
                DisplayText = vv.DisplayText ?? vv.Value,
                IsDefault = vv.IsDefault,
                Order = vv.Order
            }).ToList();
        }

        /// <summary>
        /// Map dynamic sections
        /// </summary>
        private List<NacDynamicSection> MapDynamicSections(List<InfoPathDynamicSection> infoPathSections)
        {
            if (infoPathSections == null || !infoPathSections.Any())
                return new List<NacDynamicSection>();

            // Both models have similar structures, map the properties
            return infoPathSections.Select(section => new NacDynamicSection
            {
                Name = section.CtrlId ?? "",
                Mode = section.Mode ?? "",
                CtrlId = section.CtrlId,
                Caption = section.Caption,
                Condition = section.Condition ?? "",
                ConditionField = section.ConditionField ?? "",
                ConditionValue = section.ConditionValue ?? "",
                Controls = section.Controls ?? new List<string>(),
                IsVisible = section.IsVisible
            }).ToList();
        }

        /// <summary>
        /// Map form metadata
        /// </summary>
        private Metadata MapMetadata(FormMetadata infoPathMetadata)
        {
            if (infoPathMetadata == null)
                return new Metadata();

            return new Metadata
            {
                TotalControls = infoPathMetadata.TotalControls,
                TotalSections = infoPathMetadata.TotalSections,
                DynamicSectionCount = infoPathMetadata.DynamicSectionCount,
                RepeatingSectionCount = infoPathMetadata.RepeatingSectionCount,
                ConditionalFields = infoPathMetadata.ConditionalFields ?? new List<string>()
            };
        }
    }
}
