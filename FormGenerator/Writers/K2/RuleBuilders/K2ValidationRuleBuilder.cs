using System;
using System.Linq;
using System.Xml;
using FormGenerator.Core.Interfaces;
using FormGenerator.Core.Models;
using K2SmartObjectGenerator.Utilities;

namespace FormGenerator.Writers.K2.RuleBuilders
{
    /// <summary>
    /// Builds K2 validation rules (required fields, pattern/range validation).
    /// Reference: required-validation.xml
    /// Pattern: OnClick event on button, IfLogicalHandler with SimpleBlankControlCondition,
    ///          ShowMessage action with nested Source elements.
    /// </summary>
    public class K2ValidationRuleBuilder : IK2RuleTypeBuilder
    {
        public string RuleType => "Validation";

        public bool CanHandle(RuleMappingItem mapping)
        {
            if (mapping == null) return false;

            return string.Equals(mapping.InfoPathRuleType, "Validation", StringComparison.OrdinalIgnoreCase);
        }

        public XmlElement BuildEventXml(XmlDocument doc, RuleMappingItem mapping, K2RuleContext context)
        {
            if (mapping == null || context == null) return null;

            // Resolve the control being validated
            var controlField = mapping.InfoPathAppliesTo;
            var control = K2RuleBuilderBase.ResolveControl(context, controlField);
            if (control == null)
            {
                Console.WriteLine($"    Skipping validation rule '{mapping.InfoPathRuleName}': cannot resolve control '{controlField}'");
                return null;
            }

            // Build friendly name
            string friendlyName = $"When {control.ControlName} is Changed";

            // Create event - validation fires OnChange on the validated control
            var eventEl = K2RuleBuilderBase.CreateEventElement(doc,
                "OnChange",
                control.ControlId,
                "Control",
                control.ControlName,
                context.ViewGuid,
                context.ViewName,
                friendlyName);

            var handlers = doc.CreateElement("Handlers");

            // Build validation handler
            var handler = K2RuleBuilderBase.CreateHandlerElement(doc, "IfLogicalHandler");

            // Add condition - SimpleBlankControlCondition with IsBlank expression
            var conditions = doc.CreateElement("Conditions");
            var condition = K2CompoundConditionBuilder.BuildIsBlankCondition(
                doc, control.ControlId, control.ControlName);
            conditions.AppendChild(condition);
            handler.AppendChild(conditions);

            // Add ShowMessage action (reference pattern from required-validation.xml)
            var actions = doc.CreateElement("Actions");
            var errorMessage = mapping.InfoPathErrorMessage
                ?? $"{control.ControlName} is required";

            var showMessageAction = K2RuleBuilderBase.CreateShowMessageAction(doc,
                "Validation",
                "Validation Error",
                errorMessage);
            actions.AppendChild(showMessageAction);
            handler.AppendChild(actions);

            handlers.AppendChild(handler);
            eventEl.AppendChild(handlers);

            return eventEl;
        }
    }
}
