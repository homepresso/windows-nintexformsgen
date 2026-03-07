using System.Collections.Generic;

namespace FormGenerator.Core.Models
{
    /// <summary>
    /// Context passed to rule builders providing view/control resolution information
    /// </summary>
    public class K2RuleContext
    {
        /// <summary>
        /// The GUID of the K2 view being built
        /// </summary>
        public string ViewGuid { get; set; }

        /// <summary>
        /// The display name of the K2 view
        /// </summary>
        public string ViewName { get; set; }

        /// <summary>
        /// Maps field names to K2 control GUIDs (e.g., "FieldName" -> "abc-123-...")
        /// </summary>
        public Dictionary<string, string> ControlIdMap { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Maps K2 control GUIDs to field names
        /// </summary>
        public Dictionary<string, string> ControlToFieldMap { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Maps JSON CtrlId values to K2 control GUIDs
        /// </summary>
        public Dictionary<string, string> JsonToK2ControlIdMap { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// The control resolver for multi-strategy field-to-control lookups
        /// </summary>
        public Services.K2ControlResolver ControlResolver { get; set; }
    }
}
