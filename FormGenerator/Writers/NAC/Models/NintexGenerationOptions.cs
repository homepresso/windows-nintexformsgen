namespace FormGenerator.Writers.NAC.Models
{
    /// <summary>
    /// Configuration options for Nintex form generation
    /// </summary>
    public class NintexGenerationOptions
    {
        /// <summary>
        /// Target Nintex platform (e.g., "Forms Online", "Forms Server")
        /// </summary>
        public string Platform { get; set; } = "Forms Online";

        /// <summary>
        /// Form name to use in generated output
        /// </summary>
        public string? FormName { get; set; }

        /// <summary>
        /// Form description to include in metadata
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Include validation rules from source form
        /// </summary>
        public bool IncludeValidationRules { get; set; } = true;

        /// <summary>
        /// Include conditional logic (show/hide rules)
        /// </summary>
        public bool IncludeConditionalLogic { get; set; } = true;

        /// <summary>
        /// Include calculations and formulas
        /// </summary>
        public bool IncludeCalculations { get; set; } = true;

        /// <summary>
        /// Generate accompanying workflow definition
        /// </summary>
        public bool GenerateWorkflow { get; set; } = false;

        /// <summary>
        /// Layout type ("Responsive" or "Fixed")
        /// </summary>
        public string LayoutType { get; set; } = "Responsive";

        /// <summary>
        /// Output format ("JSON" or "XML")
        /// </summary>
        public string OutputFormat { get; set; } = "JSON";

        /// <summary>
        /// Include detailed metadata in output
        /// </summary>
        public bool IncludeMetadata { get; set; } = true;

        /// <summary>
        /// Generate separate files for each view
        /// </summary>
        public bool SeparateViewFiles { get; set; } = false;

        /// <summary>
        /// Theme to apply to generated forms
        /// </summary>
        public string? ThemeName { get; set; }

        /// <summary>
        /// Default language for translations
        /// </summary>
        public string DefaultLanguage { get; set; } = "en";

        /// <summary>
        /// Include comments in generated output
        /// </summary>
        public bool IncludeComments { get; set; } = true;

        /// <summary>
        /// Prefix for generated variable IDs
        /// </summary>
        public string VariablePrefix { get; set; } = "se_";
    }
}
