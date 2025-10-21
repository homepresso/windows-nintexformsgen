using System.Text;
using FormGenerator.Core.Interfaces;
using FormGenerator.Core.Models;
using FormGenerator.Writers.NAC.Models;
using FormGenerator.Writers.NAC.Services;
using Newtonsoft.Json;

namespace FormGenerator.Writers.NAC.Rebuilders
{
    /// <summary>
    /// Rebuilds InfoPath forms as Nintex Workflow Cloud forms
    /// Implements IFormRebuilder for integration with FormGenerator
    /// </summary>
    public class NintexFormRebuilder : IFormRebuilder
    {
        public string TargetPlatform => "Nintex Workflow Cloud";

        private readonly FormConverter _converter;
        private readonly FormAnalysisToSourceFormMapper _mapper;

        public NintexFormRebuilder()
        {
            _converter = new FormConverter();
            _mapper = new FormAnalysisToSourceFormMapper();
        }

        /// <summary>
        /// Rebuild an InfoPath form as a Nintex form
        /// </summary>
        public async Task<FormRebuildResult> RebuildFormAsync(FormAnalysisResult analysis)
        {
            var result = new FormRebuildResult
            {
                TargetPlatform = TargetPlatform
            };

            try
            {
                // Validate input
                if (analysis == null)
                {
                    result.Success = false;
                    result.ErrorMessage = "Analysis result is null";
                    return result;
                }

                if (analysis.FormDefinition == null)
                {
                    result.Success = false;
                    result.ErrorMessage = "Form definition is missing from analysis";
                    return result;
                }

                // Step 1: Map InfoPath analysis to NAC SourceForm format
                SourceForm sourceForm;
                try
                {
                    sourceForm = _mapper.MapToSourceForm(analysis);
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.ErrorMessage = $"Failed to map form structure: {ex.Message}";
                    return result;
                }

                // Step 2: Convert using NAC FormConverter
                FormDefinition nintexForm;
                try
                {
                    nintexForm = await Task.Run(() => _converter.ConvertForm(sourceForm));
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.ErrorMessage = $"Failed to convert form: {ex.Message}";
                    return result;
                }

                // Step 3: Serialize to JSON
                string formJson;
                try
                {
                    formJson = JsonConvert.SerializeObject(nintexForm, Formatting.Indented);
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.ErrorMessage = $"Failed to serialize form: {ex.Message}";
                    return result;
                }

                // Step 4: Create metadata
                string metadataJson = CreateMetadata(analysis, nintexForm);

                // Step 5: Populate result
                result.Success = true;
                result.OutputData = Encoding.UTF8.GetBytes(formJson);
                result.OutputPath = $"{analysis.FormName}_nintex.json";

                // Add artifacts
                result.Artifacts["form-definition.json"] = formJson;
                result.Artifacts["metadata.json"] = metadataJson;
                result.Artifacts["conversion-info.txt"] = CreateConversionInfo(analysis, nintexForm);

                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"Unexpected error during rebuild: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// Create metadata JSON for the conversion
        /// </summary>
        private string CreateMetadata(FormAnalysisResult analysis, FormDefinition nintexForm)
        {
            var metadata = new
            {
                ConversionInfo = new
                {
                    SourcePlatform = "InfoPath",
                    TargetPlatform = TargetPlatform,
                    ConversionDate = DateTime.Now.ToString("O"),
                    SourceFormName = analysis.FormName,
                    SourceFormType = analysis.FormType
                },
                SourceFormStatistics = new
                {
                    TotalViews = analysis.FormDefinition?.Views?.Count ?? 0,
                    TotalControls = analysis.FormDefinition?.Metadata?.TotalControls ?? 0,
                    TotalSections = analysis.FormDefinition?.Metadata?.TotalSections ?? 0,
                    RepeatingSections = analysis.FormDefinition?.Metadata?.RepeatingSectionCount ?? 0,
                    DynamicSections = analysis.FormDefinition?.Metadata?.DynamicSectionCount ?? 0,
                    Rules = analysis.FormDefinition?.Rules?.Count ?? 0,
                    Validations = analysis.FormDefinition?.Validations?.Count ?? 0
                },
                NintexFormStatistics = new
                {
                    TotalPages = nintexForm.PageSettings?.Pages?.Count ?? 0,
                    TotalRows = nintexForm.Rows?.Count ?? 0,
                    TotalControls = nintexForm.Rows?.SelectMany(r => r.Controls).Count() ?? 0,
                    Variables = nintexForm.VariableContext?.Variables?.Count ?? 0,
                    TranslationKeys = nintexForm.Translations?.Values.SelectMany(t => t.Keys).Distinct().Count() ?? 0
                },
                Warnings = analysis.Messages?
                    .Where(m => m.Severity == MessageSeverity.Warning)
                    .Select(m => m.Message)
                    .ToList() ?? new List<string>(),
                Notes = new List<string>
                {
                    "This form was automatically converted from InfoPath to Nintex Workflow Cloud",
                    "Please review all validation rules and business logic",
                    "Test thoroughly before deploying to production"
                }
            };

            return JsonConvert.SerializeObject(metadata, Formatting.Indented);
        }

        /// <summary>
        /// Create conversion information text file
        /// </summary>
        private string CreateConversionInfo(FormAnalysisResult analysis, FormDefinition nintexForm)
        {
            var sb = new StringBuilder();

            sb.AppendLine("=".PadRight(70, '='));
            sb.AppendLine("NINTEX FORM CONVERSION REPORT");
            sb.AppendLine("=".PadRight(70, '='));
            sb.AppendLine();

            sb.AppendLine("SOURCE FORM INFORMATION");
            sb.AppendLine("-".PadRight(70, '-'));
            sb.AppendLine($"Form Name: {analysis.FormName}");
            sb.AppendLine($"Form Type: {analysis.FormType}");
            sb.AppendLine($"Analysis Date: {analysis.AnalysisDate}");
            sb.AppendLine($"Analyzer Used: {analysis.AnalyzerUsed}");
            sb.AppendLine();

            sb.AppendLine("SOURCE FORM STATISTICS");
            sb.AppendLine("-".PadRight(70, '-'));
            sb.AppendLine($"Views: {analysis.FormDefinition?.Views?.Count ?? 0}");
            sb.AppendLine($"Controls: {analysis.FormDefinition?.Metadata?.TotalControls ?? 0}");
            sb.AppendLine($"Sections: {analysis.FormDefinition?.Metadata?.TotalSections ?? 0}");
            sb.AppendLine($"Repeating Sections: {analysis.FormDefinition?.Metadata?.RepeatingSectionCount ?? 0}");
            sb.AppendLine($"Dynamic Sections: {analysis.FormDefinition?.Metadata?.DynamicSectionCount ?? 0}");
            sb.AppendLine($"Rules: {analysis.FormDefinition?.Rules?.Count ?? 0}");
            sb.AppendLine($"Validations: {analysis.FormDefinition?.Validations?.Count ?? 0}");
            sb.AppendLine();

            sb.AppendLine("NINTEX FORM STATISTICS");
            sb.AppendLine("-".PadRight(70, '-'));
            sb.AppendLine($"Form Name: {nintexForm.Name}");
            sb.AppendLine($"Form Version: {nintexForm.Version}");
            sb.AppendLine($"Form Type: {nintexForm.FormType}");
            sb.AppendLine($"Pages: {nintexForm.PageSettings?.Pages?.Count ?? 0}");
            sb.AppendLine($"Rows: {nintexForm.Rows?.Count ?? 0}");
            sb.AppendLine($"Controls: {nintexForm.Rows?.SelectMany(r => r.Controls).Count() ?? 0}");
            sb.AppendLine($"Variables: {nintexForm.VariableContext?.Variables?.Count ?? 0}");
            sb.AppendLine($"Translation Keys: {nintexForm.Translations?.Values.SelectMany(t => t.Keys).Distinct().Count() ?? 0}");
            sb.AppendLine();

            // Control type distribution
            if (nintexForm.Rows != null && nintexForm.Rows.Any())
            {
                sb.AppendLine("CONTROL TYPE DISTRIBUTION");
                sb.AppendLine("-".PadRight(70, '-'));
                var controlTypes = nintexForm.Rows
                    .SelectMany(r => r.Controls)
                    .GroupBy(c => c.Widget)
                    .OrderByDescending(g => g.Count());

                foreach (var group in controlTypes)
                {
                    sb.AppendLine($"  {group.Key}: {group.Count()}");
                }
                sb.AppendLine();
            }

            // Messages from analysis
            if (analysis.Messages != null && analysis.Messages.Any())
            {
                sb.AppendLine("CONVERSION MESSAGES");
                sb.AppendLine("-".PadRight(70, '-'));

                var warnings = analysis.Messages.Where(m => m.Severity == MessageSeverity.Warning).ToList();
                if (warnings.Any())
                {
                    sb.AppendLine($"Warnings ({warnings.Count}):");
                    foreach (var msg in warnings)
                    {
                        sb.AppendLine($"  - {msg.Message}");
                    }
                    sb.AppendLine();
                }

                var errors = analysis.Messages.Where(m => m.Severity == MessageSeverity.Error).ToList();
                if (errors.Any())
                {
                    sb.AppendLine($"Errors ({errors.Count}):");
                    foreach (var msg in errors)
                    {
                        sb.AppendLine($"  - {msg.Message}");
                    }
                    sb.AppendLine();
                }
            }

            sb.AppendLine("NEXT STEPS");
            sb.AppendLine("-".PadRight(70, '-'));
            sb.AppendLine("1. Review the generated form-definition.json file");
            sb.AppendLine("2. Import into Nintex Workflow Cloud");
            sb.AppendLine("3. Test all controls and validation rules");
            sb.AppendLine("4. Update any custom business logic");
            sb.AppendLine("5. Configure workflow connections if needed");
            sb.AppendLine();

            sb.AppendLine("=".PadRight(70, '='));
            sb.AppendLine($"Conversion completed at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("=".PadRight(70, '='));

            return sb.ToString();
        }
    }
}
