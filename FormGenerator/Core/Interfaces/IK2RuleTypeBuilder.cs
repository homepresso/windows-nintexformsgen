using System.Xml;
using FormGenerator.Core.Models;

namespace FormGenerator.Core.Interfaces
{
    /// <summary>
    /// Interface for modular K2 rule builders.
    /// Each implementation handles a specific rule type (visibility, validation, etc.)
    /// and creates XML elements directly matching K2 Designer output.
    /// </summary>
    public interface IK2RuleTypeBuilder
    {
        /// <summary>
        /// The rule type this builder handles (e.g., "Visibility", "Validation", "DataTransfer")
        /// </summary>
        string RuleType { get; }

        /// <summary>
        /// Determines whether this builder can handle the given rule mapping
        /// </summary>
        bool CanHandle(RuleMappingItem mapping);

        /// <summary>
        /// Builds a complete Event XML element for the given rule mapping.
        /// The output should match K2 Designer-exported XML structure.
        /// Returns null if the rule cannot be built (e.g., unresolvable controls).
        /// </summary>
        XmlElement BuildEventXml(XmlDocument doc, RuleMappingItem mapping, K2RuleContext context);
    }
}
