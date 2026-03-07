using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using FormGenerator.Core.Interfaces;
using FormGenerator.Core.Models;

namespace FormGenerator.Writers.K2.RuleBuilders
{
    /// <summary>
    /// Builds K2 visibility rules (show/hide controls based on conditions).
    /// Reference: checkbox-visibility.xml, compound-condition.xml
    /// Pattern: Two IfLogicalHandler handlers - one with true condition, one with false condition.
    /// </summary>
    public class K2VisibilityRuleBuilder : IK2RuleTypeBuilder
    {
        public string RuleType => "Visibility";

        public bool CanHandle(RuleMappingItem mapping)
        {
            if (mapping == null) return false;

            // Handle visibility conditional rules
            if (string.Equals(mapping.InfoPathRuleType, "Conditional", StringComparison.OrdinalIgnoreCase) &&
                mapping.K2ActionType != null &&
                mapping.K2ActionType.Contains("Visibility", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Handle rules with show/hide actions
            if (mapping.InfoPathActions != null &&
                mapping.InfoPathActions.Any(a =>
                    string.Equals(a.InfoPathActionType, "show", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a.InfoPathActionType, "hide", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return false;
        }

        public XmlElement BuildEventXml(XmlDocument doc, RuleMappingItem mapping, K2RuleContext context)
        {
            if (mapping == null || context == null) return null;

            // Resolve the trigger control (source field)
            var sourceField = mapping.InfoPathAppliesTo;
            var sourceControl = K2RuleBuilderBase.ResolveControl(context, sourceField);
            if (sourceControl == null)
            {
                Console.WriteLine($"    Skipping visibility rule '{mapping.InfoPathRuleName}': cannot resolve source '{sourceField}'");
                return null;
            }

            // Determine target controls from actions
            var targetActions = mapping.InfoPathActions?
                .Where(a => string.Equals(a.InfoPathActionType, "show", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(a.InfoPathActionType, "hide", StringComparison.OrdinalIgnoreCase))
                .ToList();

            bool hasExplicitActions = targetActions != null && targetActions.Count > 0;

            // Build friendly name matching K2 pattern
            string friendlyName = $"When {sourceControl.ControlName} is Changed";

            // Create the event element
            var eventEl = K2RuleBuilderBase.CreateEventElement(doc,
                "OnChange",
                sourceControl.ControlId,
                "Control",
                sourceControl.ControlName,
                context.ViewGuid,
                context.ViewName,
                friendlyName);

            var handlers = doc.CreateElement("Handlers");

            if (hasExplicitActions)
            {
                BuildHandlersFromActions(doc, handlers, mapping, context, sourceControl);
            }
            else
            {
                BuildSimpleVisibilityHandlers(doc, handlers, mapping, context, sourceControl);
            }

            if (handlers.ChildNodes.Count == 0)
            {
                Console.WriteLine($"    Skipping visibility rule '{mapping.InfoPathRuleName}': no valid handlers");
                return null;
            }

            eventEl.AppendChild(handlers);
            return eventEl;
        }

        private void BuildHandlersFromActions(XmlDocument doc, XmlElement handlers,
            RuleMappingItem mapping, K2RuleContext context, Services.ResolvedControl sourceControl)
        {
            // Reference: checkbox-visibility.xml
            // Handler 1: IfLogicalHandler with condition = true -> show controls
            // Handler 2: IfLogicalHandler with condition = false -> hide controls

            // Extract the condition value from the mapping
            string conditionValue = ExtractConditionValue(mapping.InfoPathCondition);

            // === Handler 1: When condition is met (show) ===
            var showHandler = K2RuleBuilderBase.CreateHandlerElement(doc, "IfLogicalHandler");

            var showConditions = doc.CreateElement("Conditions");
            if (!string.IsNullOrEmpty(mapping.InfoPathCondition))
            {
                var condition = K2CompoundConditionBuilder.BuildConditionFromInfoPath(
                    doc, mapping.InfoPathCondition,
                    sourceControl.ControlId, sourceControl.ControlName);
                if (condition != null)
                    showConditions.AppendChild(condition);
            }
            else
            {
                // Default: equals true for checkbox
                var condition = K2CompoundConditionBuilder.BuildEqualsCondition(
                    doc, sourceControl.ControlId, sourceControl.ControlName, "true");
                showConditions.AppendChild(condition);
            }
            showHandler.AppendChild(showConditions);

            var showActions = doc.CreateElement("Actions");
            bool hasShowActions = false;

            foreach (var actionMapping in mapping.InfoPathActions)
            {
                bool isShow = string.Equals(actionMapping.InfoPathActionType, "show", StringComparison.OrdinalIgnoreCase);
                bool isHide = string.Equals(actionMapping.InfoPathActionType, "hide", StringComparison.OrdinalIgnoreCase);
                if (!isShow && !isHide) continue;

                var targetControl = K2RuleBuilderBase.ResolveControl(context, actionMapping.InfoPathTarget);
                if (targetControl == null)
                {
                    Console.WriteLine($"        Skipping visibility action: cannot resolve target '{actionMapping.InfoPathTarget}'");
                    continue;
                }

                var action = K2RuleBuilderBase.CreateVisibilityTransferAction(doc,
                    targetControl.ControlId, targetControl.ControlName,
                    isShow, context.ViewGuid, context.ViewName);
                showActions.AppendChild(action);
                hasShowActions = true;
            }

            if (hasShowActions)
            {
                showHandler.AppendChild(showActions);
                handlers.AppendChild(showHandler);

                // === Handler 2: IfLogicalHandler with opposite condition ===
                var hideHandler = K2RuleBuilderBase.CreateHandlerElement(doc, "IfLogicalHandler");

                var hideConditions = doc.CreateElement("Conditions");
                // Build opposite condition
                string oppositeValue = GetOppositeConditionValue(conditionValue);
                var hideCondition = K2CompoundConditionBuilder.BuildEqualsCondition(
                    doc, sourceControl.ControlId, sourceControl.ControlName, oppositeValue);
                hideConditions.AppendChild(hideCondition);
                hideHandler.AppendChild(hideConditions);

                var hideActions = doc.CreateElement("Actions");
                foreach (var actionMapping in mapping.InfoPathActions)
                {
                    bool isShow = string.Equals(actionMapping.InfoPathActionType, "show", StringComparison.OrdinalIgnoreCase);
                    bool isHide = string.Equals(actionMapping.InfoPathActionType, "hide", StringComparison.OrdinalIgnoreCase);
                    if (!isShow && !isHide) continue;

                    var targetControl = K2RuleBuilderBase.ResolveControl(context, actionMapping.InfoPathTarget);
                    if (targetControl == null) continue;

                    // Opposite: show becomes hide, hide becomes show
                    var action = K2RuleBuilderBase.CreateVisibilityTransferAction(doc,
                        targetControl.ControlId, targetControl.ControlName,
                        !isShow, context.ViewGuid, context.ViewName);
                    hideActions.AppendChild(action);
                }
                hideHandler.AppendChild(hideActions);
                handlers.AppendChild(hideHandler);
            }
        }

        private void BuildSimpleVisibilityHandlers(XmlDocument doc, XmlElement handlers,
            RuleMappingItem mapping, K2RuleContext context, Services.ResolvedControl sourceControl)
        {
            // For conditional visibility rules without explicit actions,
            // try to resolve target from the AppliesTo field
            var targetField = mapping.InfoPathAppliesTo;
            var targetControl = K2RuleBuilderBase.ResolveControl(context, targetField);
            if (targetControl == null) return;

            // Handler 1: IfLogicalHandler - when source = true, show target
            var showHandler = K2RuleBuilderBase.CreateHandlerElement(doc, "IfLogicalHandler");
            var showConditions = doc.CreateElement("Conditions");
            var showCondition = K2CompoundConditionBuilder.BuildEqualsCondition(
                doc, sourceControl.ControlId, sourceControl.ControlName, "true");
            showConditions.AppendChild(showCondition);
            showHandler.AppendChild(showConditions);

            var showActions = doc.CreateElement("Actions");
            showActions.AppendChild(K2RuleBuilderBase.CreateVisibilityTransferAction(doc,
                targetControl.ControlId, targetControl.ControlName,
                true, context.ViewGuid, context.ViewName));
            showHandler.AppendChild(showActions);
            handlers.AppendChild(showHandler);

            // Handler 2: IfLogicalHandler - when source = false, hide target
            var hideHandler = K2RuleBuilderBase.CreateHandlerElement(doc, "IfLogicalHandler");
            var hideConditions = doc.CreateElement("Conditions");
            var hideCondition = K2CompoundConditionBuilder.BuildEqualsCondition(
                doc, sourceControl.ControlId, sourceControl.ControlName, "false");
            hideConditions.AppendChild(hideCondition);
            hideHandler.AppendChild(hideConditions);

            var hideActions = doc.CreateElement("Actions");
            hideActions.AppendChild(K2RuleBuilderBase.CreateVisibilityTransferAction(doc,
                targetControl.ControlId, targetControl.ControlName,
                false, context.ViewGuid, context.ViewName));
            hideHandler.AppendChild(hideActions);
            handlers.AppendChild(hideHandler);
        }

        private string ExtractConditionValue(string infoPathCondition)
        {
            if (string.IsNullOrEmpty(infoPathCondition)) return "true";

            // Try to extract the comparison value from the condition
            var match = Regex.Match(infoPathCondition, @"=\s*[""']([^""']*)[""']");
            if (match.Success) return match.Groups[1].Value;

            var boolMatch = Regex.Match(infoPathCondition, @"=\s*(true|false)", RegexOptions.IgnoreCase);
            if (boolMatch.Success) return boolMatch.Groups[1].Value;

            return "true";
        }

        private string GetOppositeConditionValue(string value)
        {
            if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)) return "false";
            if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) return "true";
            // For non-boolean, we can't easily invert - return empty string
            return "";
        }
    }
}
