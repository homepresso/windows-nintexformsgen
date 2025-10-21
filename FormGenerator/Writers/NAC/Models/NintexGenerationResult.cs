using FormGenerator.Core.Interfaces;

namespace FormGenerator.Writers.NAC.Models
{
    /// <summary>
    /// Result of Nintex form generation for multiple forms
    /// </summary>
    public class NintexGenerationResult
    {
        /// <summary>
        /// Overall success status
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Individual form rebuild results keyed by form name
        /// </summary>
        public Dictionary<string, FormRebuildResult> FormResults { get; set; } = new();

        /// <summary>
        /// Total number of forms processed
        /// </summary>
        public int TotalForms => FormResults.Count;

        /// <summary>
        /// Number of successfully generated forms
        /// </summary>
        public int SuccessfulForms => FormResults.Count(r => r.Value.Success);

        /// <summary>
        /// Number of failed forms
        /// </summary>
        public int FailedForms => FormResults.Count(r => !r.Value.Success);

        /// <summary>
        /// Overall error messages
        /// </summary>
        public List<string> Errors { get; set; } = new();

        /// <summary>
        /// Overall warning messages
        /// </summary>
        public List<string> Warnings { get; set; } = new();

        /// <summary>
        /// Overall informational messages
        /// </summary>
        public List<string> Messages { get; set; } = new();

        /// <summary>
        /// Generation start time
        /// </summary>
        public DateTime StartTime { get; set; } = DateTime.Now;

        /// <summary>
        /// Generation end time
        /// </summary>
        public DateTime? EndTime { get; set; }

        /// <summary>
        /// Total generation duration
        /// </summary>
        public TimeSpan? Duration => EndTime.HasValue ? EndTime.Value - StartTime : null;

        /// <summary>
        /// Statistics about the generation
        /// </summary>
        public GenerationStatistics Statistics { get; set; } = new();

        /// <summary>
        /// Get a formatted summary of the generation results
        /// </summary>
        public string GetSummary()
        {
            var lines = new List<string>
            {
                "=== Nintex Form Generation Summary ===",
                $"Total Forms: {TotalForms}",
                $"Successful: {SuccessfulForms}",
                $"Failed: {FailedForms}",
                $"Duration: {Duration?.ToString(@"mm\:ss") ?? "In progress"}",
                ""
            };

            if (Statistics != null)
            {
                lines.Add($"Total Controls: {Statistics.TotalControls}");
                lines.Add($"Total Variables: {Statistics.TotalVariables}");
                lines.Add($"Total Pages: {Statistics.TotalPages}");
                lines.Add("");
            }

            if (Errors.Any())
            {
                lines.Add($"Errors ({Errors.Count}):");
                lines.AddRange(Errors.Select(e => $"  - {e}"));
                lines.Add("");
            }

            if (Warnings.Any())
            {
                lines.Add($"Warnings ({Warnings.Count}):");
                lines.AddRange(Warnings.Select(w => $"  - {w}"));
                lines.Add("");
            }

            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>
    /// Statistics about the generation process
    /// </summary>
    public class GenerationStatistics
    {
        public int TotalControls { get; set; }
        public int TotalVariables { get; set; }
        public int TotalPages { get; set; }
        public int TotalRows { get; set; }
        public int TotalRepeatingSection { get; set; }
        public Dictionary<string, int> ControlTypeCounts { get; set; } = new();
        public Dictionary<string, int> WidgetTypeCounts { get; set; } = new();
    }
}
