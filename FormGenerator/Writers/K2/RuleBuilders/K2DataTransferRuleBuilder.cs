using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using FormGenerator.Core.Interfaces;
using FormGenerator.Core.Models;
using K2SmartObjectGenerator.Utilities;

namespace FormGenerator.Writers.K2.RuleBuilders
{
    /// <summary>
    /// Builds K2 data transfer rules (control-to-control copy, set literal value, expressions).
    /// Reference: data-transfer.xml
    /// Pattern: OnChange event, IfLogicalHandler (no conditions for unconditional transfer),
    ///          Transfer action with SourceType="Control" and TargetType="Control".
    /// </summary>
    public class K2DataTransferRuleBuilder : IK2RuleTypeBuilder
    {
        public string RuleType => "DataTransfer";

        public bool CanHandle(RuleMappingItem mapping)
        {
            if (mapping == null) return false;

            // Handle calculation conditionals
            if (string.Equals(mapping.InfoPathRuleType, "Conditional", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(mapping.K2ActionType, "Calculate", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Handle rules with setValue/calculate actions
            if (mapping.InfoPathActions != null &&
                mapping.InfoPathActions.Any(a =>
                    string.Equals(a.InfoPathActionType, "setValue", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a.InfoPathActionType, "calculate", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            // Handle rules explicitly typed as Transfer
            if (string.Equals(mapping.K2ActionType, "Transfer", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        public XmlElement BuildEventXml(XmlDocument doc, RuleMappingItem mapping, K2RuleContext context)
        {
            if (mapping == null || context == null) return null;

            // Resolve the trigger control
            var sourceField = mapping.InfoPathAppliesTo;
            var sourceControl = K2RuleBuilderBase.ResolveControl(context, sourceField);
            if (sourceControl == null)
            {
                Console.WriteLine($"    Skipping data transfer rule '{mapping.InfoPathRuleName}': cannot resolve source '{sourceField}'");
                return null;
            }

            // Build friendly name
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

            // Build handler - reference shows IfLogicalHandler even without conditions
            var handler = K2RuleBuilderBase.CreateHandlerElement(doc, "IfLogicalHandler");

            // Add condition if present (unconditional transfer has no Conditions block)
            if (!string.IsNullOrEmpty(mapping.InfoPathCondition))
            {
                var conditions = doc.CreateElement("Conditions");
                var condition = K2CompoundConditionBuilder.BuildConditionFromInfoPath(
                    doc, mapping.InfoPathCondition,
                    sourceControl.ControlId, sourceControl.ControlName);
                if (condition != null)
                {
                    conditions.AppendChild(condition);
                    handler.AppendChild(conditions);
                }
            }

            // Add transfer actions
            var actions = doc.CreateElement("Actions");
            bool hasActions = false;

            if (mapping.InfoPathActions != null && mapping.InfoPathActions.Count > 0)
            {
                foreach (var actionMapping in mapping.InfoPathActions)
                {
                    var action = BuildTransferAction(doc, actionMapping, context, sourceControl);
                    if (action != null)
                    {
                        actions.AppendChild(action);
                        hasActions = true;
                    }
                }
            }
            else
            {
                // No explicit actions - try to build from mapping metadata
                var action = BuildTransferFromMetadata(doc, mapping, context, sourceControl);
                if (action != null)
                {
                    actions.AppendChild(action);
                    hasActions = true;
                }
            }

            if (!hasActions)
            {
                Console.WriteLine($"    Skipping data transfer rule '{mapping.InfoPathRuleName}': no valid actions");
                return null;
            }

            handler.AppendChild(actions);
            handlers.AppendChild(handler);
            eventEl.AppendChild(handlers);

            return eventEl;
        }

        private XmlElement BuildTransferAction(XmlDocument doc, RuleActionMapping actionMapping,
            K2RuleContext context, Services.ResolvedControl sourceControl)
        {
            // Resolve target control
            var targetControl = K2RuleBuilderBase.ResolveControl(context, actionMapping.InfoPathTarget);
            if (targetControl == null)
            {
                Console.WriteLine($"        Skipping transfer action: cannot resolve target '{actionMapping.InfoPathTarget}'");
                return null;
            }

            // Determine transfer type
            if (!string.IsNullOrEmpty(actionMapping.InfoPathExpression))
            {
                // Expression-based transfer (e.g., sum, concat)
                var convertedExpr = ConvertXPathToK2Expression(actionMapping.InfoPathExpression);
                return K2RuleBuilderBase.CreateDataTransferAction(doc,
                    "Expression", null, null, convertedExpr,
                    targetControl.ControlId, targetControl.ControlName, "Control",
                    context.ViewGuid, context.ViewName);
            }
            else if (actionMapping.InfoPathParameters != null &&
                     actionMapping.InfoPathParameters.TryGetValue("value", out var literalValue))
            {
                // Literal value transfer
                return K2RuleBuilderBase.CreateDataTransferAction(doc,
                    "Value", null, null, literalValue,
                    targetControl.ControlId, targetControl.ControlName, "Control",
                    context.ViewGuid, context.ViewName);
            }
            else if (actionMapping.InfoPathParameters != null &&
                     actionMapping.InfoPathParameters.TryGetValue("sourceField", out var srcField))
            {
                // Control-to-control transfer using the new dedicated method
                var srcControl = K2RuleBuilderBase.ResolveControl(context, srcField);
                if (srcControl == null) return null;

                return K2RuleBuilderBase.CreateControlToControlTransferAction(doc,
                    srcControl.ControlId, srcControl.ControlName,
                    targetControl.ControlId, targetControl.ControlName,
                    context.ViewGuid, context.ViewName);
            }
            else
            {
                // Default: control-to-control from the source trigger
                return K2RuleBuilderBase.CreateControlToControlTransferAction(doc,
                    sourceControl.ControlId, sourceControl.ControlName,
                    targetControl.ControlId, targetControl.ControlName,
                    context.ViewGuid, context.ViewName);
            }
        }

        private XmlElement BuildTransferFromMetadata(XmlDocument doc, RuleMappingItem mapping,
            K2RuleContext context, Services.ResolvedControl sourceControl)
        {
            // Try to use InfoPathConditionExpression as a transfer source
            if (!string.IsNullOrEmpty(mapping.InfoPathConditionExpression))
            {
                var targetControl = K2RuleBuilderBase.ResolveControl(context, mapping.InfoPathAppliesTo);
                if (targetControl == null) return null;

                var expr = ConvertXPathToK2Expression(mapping.InfoPathConditionExpression);
                return K2RuleBuilderBase.CreateDataTransferAction(doc,
                    "Expression", null, null, expr,
                    targetControl.ControlId, targetControl.ControlName, "Control",
                    context.ViewGuid, context.ViewName);
            }

            return null;
        }

        private string ConvertXPathToK2Expression(string xpath)
        {
            if (string.IsNullOrEmpty(xpath)) return "";

            var result = xpath;
            result = Regex.Replace(result, @"/my:", "/");
            result = Regex.Replace(result, @"my:", "");
            result = result.Replace("xd:isBlank(", "IsNullOrEmpty(");
            result = Regex.Replace(result, @"sum\(([^)]+)\)", "Sum($1)", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"count\(([^)]+)\)", "Count($1)", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"concat\(", "Concat(", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"number\(([^)]+)\)", "ToNumber($1)", RegexOptions.IgnoreCase);

            return result;
        }
    }
}
