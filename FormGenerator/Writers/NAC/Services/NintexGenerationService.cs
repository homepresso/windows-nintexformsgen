using System.IO;
using System.IO.Compression;
using System.Text;
using FormGenerator.Core.Models;
using FormGenerator.Writers.NAC.Models;
using FormGenerator.Writers.NAC.Rebuilders;

namespace FormGenerator.Writers.NAC.Services
{
    /// <summary>
    /// Service for generating Nintex forms from InfoPath analysis results
    /// Provides batch processing with progress reporting for UI integration
    /// </summary>
    public class NintexGenerationService
    {
        private readonly NintexFormRebuilder _rebuilder;

        /// <summary>
        /// Event fired when generation progress updates
        /// </summary>
        public event EventHandler<string>? ProgressUpdated;

        /// <summary>
        /// Event fired when a specific form completes
        /// </summary>
        public event EventHandler<FormGenerationProgressEventArgs>? FormCompleted;

        public NintexGenerationService()
        {
            _rebuilder = new NintexFormRebuilder();
        }

        /// <summary>
        /// Generate Nintex forms for multiple InfoPath analysis results
        /// </summary>
        public async Task<NintexGenerationResult> GenerateFormsAsync(
            Dictionary<string, FormAnalysisResult> analyses,
            NintexGenerationOptions? options = null)
        {
            options ??= new NintexGenerationOptions();

            var result = new NintexGenerationResult
            {
                StartTime = DateTime.Now
            };

            OnProgressUpdated($"Starting Nintex form generation for {analyses.Count} form(s)...");

            int completed = 0;
            int totalForms = analyses.Count;

            foreach (var kvp in analyses)
            {
                var fileName = kvp.Key;
                var analysis = kvp.Value;
                try
                {
                    OnProgressUpdated($"Processing {fileName} ({completed + 1}/{totalForms})...");

                    // Override form name if specified in options
                    if (!string.IsNullOrEmpty(options.FormName) && analyses.Count == 1)
                    {
                        analysis.FormName = options.FormName;
                    }

                    // Generate form
                    var rebuildResult = await _rebuilder.RebuildFormAsync(analysis);

                    // Store result
                    result.FormResults[fileName] = rebuildResult;

                    // Update statistics
                    if (rebuildResult.Success)
                    {
                        UpdateStatistics(result, rebuildResult, analysis);
                        OnProgressUpdated($"  ✓ {fileName} converted successfully");
                    }
                    else
                    {
                        result.Errors.Add($"{fileName}: {rebuildResult.ErrorMessage}");
                        OnProgressUpdated($"  ✗ {fileName} failed: {rebuildResult.ErrorMessage}");
                    }

                    completed++;

                    // Fire form completed event
                    OnFormCompleted(new FormGenerationProgressEventArgs
                    {
                        FormName = fileName,
                        Success = rebuildResult.Success,
                        CurrentForm = completed,
                        TotalForms = totalForms,
                        PercentComplete = (double)completed / totalForms * 100
                    });
                }
                catch (Exception ex)
                {
                    var errorMsg = $"{fileName}: Unexpected error - {ex.Message}";
                    result.Errors.Add(errorMsg);
                    OnProgressUpdated($"  ✗ {errorMsg}");

                    result.FormResults[fileName] = new FormRebuildResult
                    {
                        Success = false,
                        ErrorMessage = ex.Message,
                        TargetPlatform = "Nintex Workflow Cloud"
                    };

                    completed++;
                }
            }

            result.EndTime = DateTime.Now;
            result.Success = result.SuccessfulForms > 0;

            // Generate final summary
            OnProgressUpdated("");
            OnProgressUpdated("=== Generation Complete ===");
            OnProgressUpdated($"Total Forms: {result.TotalForms}");
            OnProgressUpdated($"Successful: {result.SuccessfulForms}");
            OnProgressUpdated($"Failed: {result.FailedForms}");
            OnProgressUpdated($"Duration: {result.Duration?.ToString(@"mm\:ss")}");
            OnProgressUpdated("");

            if (result.Statistics != null)
            {
                OnProgressUpdated($"Total Controls Converted: {result.Statistics.TotalControls}");
                OnProgressUpdated($"Total Variables Created: {result.Statistics.TotalVariables}");
                OnProgressUpdated($"Total Pages: {result.Statistics.TotalPages}");
            }

            return result;
        }

        /// <summary>
        /// Export generated forms as a zip file
        /// </summary>
        public async Task<string> ExportAsZipAsync(
            NintexGenerationResult result,
            string outputPath)
        {
            if (result == null || !result.Success)
                throw new ArgumentException("Result is null or not successful");

            try
            {
                OnProgressUpdated($"Creating export package at {outputPath}...");

                // Delete existing file if present
                if (File.Exists(outputPath))
                    File.Delete(outputPath);

                using (var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create))
                {
                    foreach (var formKvp in result.FormResults)
                    {
                        var formName = formKvp.Key;
                        var formResult = formKvp.Value;

                        if (!formResult.Success)
                            continue;

                        // Add main form definition
                        if (formResult.OutputData != null && formResult.OutputData.Length > 0)
                        {
                            var formEntry = archive.CreateEntry($"{formName}/form-definition.json");
                            using (var entryStream = formEntry.Open())
                            {
                                await entryStream.WriteAsync(formResult.OutputData, 0, formResult.OutputData.Length);
                            }
                        }

                        // Add artifacts
                        if (formResult.Artifacts != null)
                        {
                            foreach (var artifactKvp in formResult.Artifacts)
                            {
                                var artifactName = artifactKvp.Key;
                                var artifactContent = artifactKvp.Value;

                                if (string.IsNullOrEmpty(artifactContent))
                                    continue;

                                var artifactEntry = archive.CreateEntry($"{formName}/{artifactName}");
                                using (var entryStream = artifactEntry.Open())
                                using (var writer = new StreamWriter(entryStream, Encoding.UTF8))
                                {
                                    await writer.WriteAsync(artifactContent);
                                }
                            }
                        }
                    }

                    // Add overall summary
                    var summaryEntry = archive.CreateEntry("_SUMMARY.txt");
                    using (var entryStream = summaryEntry.Open())
                    using (var writer = new StreamWriter(entryStream, Encoding.UTF8))
                    {
                        await writer.WriteAsync(result.GetSummary());
                    }
                }

                OnProgressUpdated($"  ✓ Export package created successfully");
                return outputPath;
            }
            catch (Exception ex)
            {
                OnProgressUpdated($"  ✗ Export failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Update statistics based on conversion results
        /// </summary>
        private void UpdateStatistics(
            NintexGenerationResult result,
            FormRebuildResult rebuildResult,
            FormAnalysisResult analysis)
        {
            if (result.Statistics == null)
                result.Statistics = new GenerationStatistics();

            // Parse form definition from artifacts to get detailed stats
            if (rebuildResult.Artifacts?.ContainsKey("form-definition.json") == true)
            {
                try
                {
                    var formJson = rebuildResult.Artifacts["form-definition.json"];
                    var formDef = Newtonsoft.Json.JsonConvert.DeserializeObject<FormDefinition>(formJson);

                    if (formDef != null)
                    {
                        result.Statistics.TotalPages += formDef.PageSettings?.Pages?.Count ?? 0;
                        result.Statistics.TotalRows += formDef.Rows?.Count ?? 0;

                        var controls = formDef.Rows?.SelectMany(r => r.Controls).ToList() ?? new List<Control>();
                        result.Statistics.TotalControls += controls.Count;

                        result.Statistics.TotalVariables += formDef.VariableContext?.Variables?.Count ?? 0;

                        // Count control types
                        foreach (var control in controls)
                        {
                            var widget = control.Widget ?? "unknown";
                            if (!result.Statistics.WidgetTypeCounts.ContainsKey(widget))
                                result.Statistics.WidgetTypeCounts[widget] = 0;
                            result.Statistics.WidgetTypeCounts[widget]++;
                        }

                        // Count repeating sections
                        var repeatingSections = controls.Count(c => c.Widget == "repeating-section");
                        result.Statistics.TotalRepeatingSection += repeatingSections;
                    }
                }
                catch
                {
                    // If parsing fails, just skip statistics update
                }
            }

            // Update from source analysis
            if (analysis?.FormDefinition != null)
            {
                var formDef = analysis.FormDefinition;
                foreach (var view in formDef.Views ?? new List<Analyzers.Infopath.ViewDefinition>())
                {
                    foreach (var control in view.Controls ?? new List<Analyzers.Infopath.ControlDefinition>())
                    {
                        var controlType = control.Type ?? "unknown";
                        if (!result.Statistics.ControlTypeCounts.ContainsKey(controlType))
                            result.Statistics.ControlTypeCounts[controlType] = 0;
                        result.Statistics.ControlTypeCounts[controlType]++;
                    }
                }
            }
        }

        /// <summary>
        /// Raise progress updated event
        /// </summary>
        protected virtual void OnProgressUpdated(string message)
        {
            ProgressUpdated?.Invoke(this, message);
        }

        /// <summary>
        /// Raise form completed event
        /// </summary>
        protected virtual void OnFormCompleted(FormGenerationProgressEventArgs e)
        {
            FormCompleted?.Invoke(this, e);
        }
    }

    /// <summary>
    /// Progress event arguments for form generation
    /// </summary>
    public class FormGenerationProgressEventArgs : EventArgs
    {
        public string FormName { get; set; } = "";
        public bool Success { get; set; }
        public int CurrentForm { get; set; }
        public int TotalForms { get; set; }
        public double PercentComplete { get; set; }
    }
}
