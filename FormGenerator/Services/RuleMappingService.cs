using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using FormGenerator.Analyzers.Infopath;
using FormGenerator.Core.Models;

namespace FormGenerator.Services
{
    /// <summary>
    /// Service to analyze InfoPath rules and create K2 rule mappings
    /// </summary>
    public class RuleMappingService
    {
        private readonly Dictionary<string, K2RuleTemplate> _ruleTemplates;
        private readonly K2RuleXmlBuilder _ruleBuilder;

        public RuleMappingService()
        {
            _ruleTemplates = InitializeRuleTemplates();
            _ruleBuilder = new K2RuleXmlBuilder();
        }

        /// <summary>
        /// Analyzes all rules from form definitions and creates mappings
        /// </summary>
        public List<RuleMappingItem> AnalyzeRules(Dictionary<string, InfoPathFormDefinition> formDefinitions)
        {
            var mappings = new List<RuleMappingItem>();

            if (formDefinitions == null) return mappings;

            foreach (var kvp in formDefinitions)
            {
                var formName = kvp.Key ?? "Unknown";
                var formDef = kvp.Value;

                if (formDef == null) continue;

                // Process standard rules
                if (formDef.Rules != null)
                {
                    foreach (var rule in formDef.Rules)
                    {
                        if (rule == null) continue;
                        var mapping = CreateRuleMapping(formName, rule);
                        if (mapping != null)
                            mappings.Add(mapping);
                    }
                }

                // Process validation rules
                if (formDef.Validations != null)
                {
                    foreach (var validation in formDef.Validations)
                    {
                        if (validation == null) continue;
                        var mapping = CreateValidationMapping(formName, validation);
                        if (mapping != null)
                            mappings.Add(mapping);
                    }
                }

                // Process conditional rules
                if (formDef.ConditionalRules != null)
                {
                    foreach (var conditional in formDef.ConditionalRules)
                    {
                        if (conditional == null) continue;
                        var mapping = CreateConditionalMapping(formName, conditional);
                        if (mapping != null)
                            mappings.Add(mapping);
                    }
                }
            }

            return mappings;
        }

        /// <summary>
        /// Generates a summary of rule mappings
        /// </summary>
        public RuleMappingSummary GenerateSummary(string formName, List<RuleMappingItem> mappings)
        {
            var formMappings = mappings.Where(m => m.FormName == formName).ToList();

            var summary = new RuleMappingSummary
            {
                FormName = formName,
                TotalRules = formMappings.Count,
                SupportedRules = formMappings.Count(m => m.Status == RuleMappingStatus.Supported),
                PartialRules = formMappings.Count(m => m.Status == RuleMappingStatus.PartiallySupported),
                UnsupportedRules = formMappings.Count(m => m.Status == RuleMappingStatus.NotSupported),
                CustomRequiredRules = formMappings.Count(m => m.Status == RuleMappingStatus.RequiresCustomization),
                K2NativeRules = formMappings.Count(m => m.Status == RuleMappingStatus.K2Native)
            };

            // Identify common issues
            var issues = new HashSet<string>();
            foreach (var mapping in formMappings)
            {
                foreach (var feature in mapping.MissingFeatures)
                {
                    issues.Add(feature);
                }
            }
            summary.CommonIssues = issues.ToList();

            // Add recommendations
            if (summary.UnsupportedRules > 0)
            {
                summary.Recommendations.Add("Consider implementing custom K2 rules for unsupported features");
            }
            if (summary.PartialRules > 0)
            {
                summary.Recommendations.Add("Review partially supported rules for manual adjustments");
            }

            return summary;
        }

        private RuleMappingItem CreateRuleMapping(string formName, FormRule rule)
        {
            try
            {
                var mapping = new RuleMappingItem
                {
                    FormName = formName,
                    InfoPathRuleName = rule?.Name ?? "Unknown Rule",
                    InfoPathRuleType = rule?.RuleType ?? "Unknown",
                    InfoPathCondition = rule?.Condition,
                    InfoPathConditionExpression = rule?.ConditionExpression,
                    InfoPathAppliesTo = rule?.AppliesTo,
                    InfoPathIsEnabled = rule?.IsEnabled ?? false,
                    InfoPathErrorMessage = rule?.ErrorMessage
                };

                // Map actions
                if (rule?.Actions != null)
                {
                    foreach (var action in rule.Actions)
                    {
                        if (action == null) continue;

                        var actionMapping = new RuleActionMapping
                        {
                            InfoPathActionType = action.Type,
                            InfoPathTarget = action.Target,
                            InfoPathExpression = action.Expression,
                            InfoPathParameters = action.Parameters ?? new Dictionary<string, string>()
                        };

                        // Determine K2 action type
                        actionMapping.K2ActionType = MapActionType(action.Type);
                        actionMapping.IsSupported = IsActionSupported(action.Type);
                        actionMapping.K2ActionXml = GenerateK2ActionXml(action, mapping);

                        mapping.InfoPathActions.Add(actionMapping);
                    }
                }

                // Determine overall mapping status
                DetermineRuleMappingStatus(mapping, rule);

                // Generate K2 XML
                mapping.K2Xml = GenerateK2RuleXml(mapping, rule);

                return mapping;
            }
            catch (Exception ex)
            {
                // Return a basic mapping with error information
                return new RuleMappingItem
                {
                    FormName = formName,
                    InfoPathRuleName = rule?.Name ?? "Error Processing Rule",
                    InfoPathRuleType = "Error",
                    Status = RuleMappingStatus.NotSupported,
                    Notes = $"Error processing rule: {ex.Message}"
                };
            }
        }

        private RuleMappingItem CreateValidationMapping(string formName, ValidationRule validation)
        {
            try
            {
                var mapping = new RuleMappingItem
                {
                    FormName = formName,
                    InfoPathRuleName = $"Validation: {validation?.ControlName ?? "Unknown"}",
                    InfoPathRuleType = "Validation",
                    InfoPathCondition = validation?.Expression,
                    InfoPathAppliesTo = validation?.ControlId,
                    InfoPathIsEnabled = true,
                    InfoPathErrorMessage = validation?.ErrorMessage
                };

                // K2 validation mapping
                mapping.K2RuleType = "Validation";
                mapping.K2EventType = "OnChange";
                mapping.K2ActionType = "Validate";

                // Determine status based on validation type
                switch (validation?.ValidationType?.ToLower())
                {
                    case "required":
                        mapping.Status = RuleMappingStatus.Supported;
                        mapping.K2Xml = GenerateK2RequiredValidationXml(validation);
                        break;
                    case "pattern":
                        mapping.Status = RuleMappingStatus.Supported;
                        mapping.K2Xml = GenerateK2PatternValidationXml(validation);
                        break;
                    case "range":
                        mapping.Status = RuleMappingStatus.Supported;
                        mapping.K2Xml = GenerateK2RangeValidationXml(validation);
                        break;
                    case "custom":
                        mapping.Status = RuleMappingStatus.RequiresCustomization;
                        mapping.K2Xml = GenerateK2CustomValidationXml(validation);
                        mapping.Notes = "Custom validation requires manual implementation in K2";
                        break;
                    default:
                        mapping.Status = RuleMappingStatus.PartiallySupported;
                        mapping.K2Xml = "<!-- Validation type not fully mapped -->";
                        break;
                }

                return mapping;
            }
            catch (Exception ex)
            {
                return new RuleMappingItem
                {
                    FormName = formName,
                    InfoPathRuleName = "Error Processing Validation",
                    InfoPathRuleType = "Validation",
                    Status = RuleMappingStatus.NotSupported,
                    Notes = $"Error processing validation: {ex.Message}"
                };
            }
        }

        private RuleMappingItem CreateConditionalMapping(string formName, ConditionalRule conditional)
        {
            try
            {
                // For the orchestrator builders:
                // - InfoPathAppliesTo is used as the SOURCE/trigger control (what fires the event)
                // - Target controls come from InfoPathActions or the conditional's TargetField
                // The SourceField from the condition is the trigger, TargetField is what gets affected
                var sourceField = conditional?.SourceField;

                // If SourceField is empty, try to extract it from the condition expression
                if (string.IsNullOrEmpty(sourceField) && !string.IsNullOrEmpty(conditional?.Condition))
                {
                    sourceField = ExtractSourceFieldFromCondition(conditional.Condition);
                }

                // For calculation rules, the "condition" is actually the source value expression
                // The TargetField is what receives the calculated value
                var appliesTo = sourceField;
                if (string.IsNullOrEmpty(appliesTo))
                    appliesTo = conditional?.TargetField;

                var mapping = new RuleMappingItem
                {
                    FormName = formName,
                    InfoPathRuleName = conditional?.Name ?? $"Conditional: {conditional?.Type ?? "Unknown"}",
                    InfoPathRuleType = "Conditional",
                    InfoPathCondition = conditional?.Condition,
                    InfoPathConditionExpression = conditional?.Value,
                    InfoPathAppliesTo = appliesTo
                };

                mapping.K2EventType = "OnChange";

                // Check for K2 Native patterns before type-based classification
                if (IsK2NativePattern(mapping))
                {
                    mapping.Status = RuleMappingStatus.K2Native;
                    return mapping;
                }

                switch (conditional?.Type?.ToLower())
                {
                    case "visibility":
                        mapping.Status = RuleMappingStatus.Supported;
                        mapping.K2ActionType = "SetControlVisibility";
                        mapping.K2Xml = GenerateK2VisibilityRuleXml(conditional);
                        // Add synthetic show/hide actions so the builder knows what to target
                        if (!string.IsNullOrEmpty(conditional?.TargetField))
                        {
                            var targetName = ExtractFieldName(conditional.TargetField);
                            mapping.InfoPathActions.Add(new RuleActionMapping
                            {
                                InfoPathActionType = conditional.Action == "hide" ? "hide" : "show",
                                InfoPathTarget = targetName ?? conditional.TargetField,
                                K2ActionType = "Visibility",
                                IsSupported = true
                            });
                        }
                        // Also add targets from AffectedControls
                        if (conditional?.AffectedControls != null)
                        {
                            foreach (var ctrl in conditional.AffectedControls)
                            {
                                if (string.IsNullOrEmpty(ctrl)) continue;
                                var ctrlName = ExtractFieldName(ctrl);
                                mapping.InfoPathActions.Add(new RuleActionMapping
                                {
                                    InfoPathActionType = conditional.Action == "hide" ? "hide" : "show",
                                    InfoPathTarget = ctrlName ?? ctrl,
                                    K2ActionType = "Visibility",
                                    IsSupported = true
                                });
                            }
                        }
                        break;
                    case "formatting":
                        mapping.Status = RuleMappingStatus.PartiallySupported;
                        mapping.K2ActionType = "ApplyStyle";
                        mapping.K2Xml = GenerateK2FormattingRuleXml(conditional);
                        mapping.Notes = "K2 styling differs from InfoPath - review styling manually";
                        break;
                    case "calculation":
                        // K2 handles calculations via the Expressions section, not rule events.
                        // K2ExpressionBuilder processes these separately. Skip in rule pipeline.
                        mapping.Status = RuleMappingStatus.K2Native;
                        mapping.K2ActionType = "Expression";
                        mapping.Notes = "Handled by K2 Expressions section (not rule events)";
                        break;
                default:
                    mapping.Status = RuleMappingStatus.NotMapped;
                    mapping.K2Xml = $"<!-- Unknown conditional type: {conditional?.Type} -->";
                    break;
                }

                return mapping;
            }
            catch (Exception ex)
            {
                return new RuleMappingItem
                {
                    FormName = formName,
                    InfoPathRuleName = "Error Processing Conditional",
                    InfoPathRuleType = "Conditional",
                    Status = RuleMappingStatus.NotSupported,
                    Notes = $"Error processing conditional rule: {ex.Message}"
                };
            }
        }

        private void DetermineRuleMappingStatus(RuleMappingItem mapping, FormRule rule)
        {
            // Check for K2 Native patterns first
            if (IsK2NativePattern(mapping))
            {
                mapping.Status = RuleMappingStatus.K2Native;
                return;
            }

            var supportedActions = mapping.InfoPathActions.Count(a => a.IsSupported);
            var totalActions = mapping.InfoPathActions.Count;

            if (totalActions == 0)
            {
                mapping.Status = RuleMappingStatus.NotMapped;
                return;
            }

            if (supportedActions == totalActions)
            {
                mapping.Status = RuleMappingStatus.Supported;
            }
            else if (supportedActions > 0)
            {
                mapping.Status = RuleMappingStatus.PartiallySupported;
                mapping.MissingFeatures.AddRange(
                    mapping.InfoPathActions
                        .Where(a => !a.IsSupported)
                        .Select(a => $"Action type '{a.InfoPathActionType}' not supported"));
            }
            else
            {
                mapping.Status = RuleMappingStatus.NotSupported;
            }

            // Check for complex conditions
            if (!string.IsNullOrEmpty(rule.Condition) && IsComplexCondition(rule.Condition))
            {
                if (mapping.Status == RuleMappingStatus.Supported)
                {
                    mapping.Status = RuleMappingStatus.PartiallySupported;
                }
                mapping.Complexity = "Complex";
                mapping.Warnings.Add("Complex condition may require manual adjustment");
            }
            else
            {
                mapping.Complexity = "Simple";
            }
        }

        /// <summary>
        /// Detects InfoPath patterns that K2 handles natively through its architecture
        /// (no explicit rule needed - e.g., formatting, repeating table row selection, List View patterns)
        /// </summary>
        private bool IsK2NativePattern(RuleMappingItem mapping)
        {
            // Check both condition and action expressions for K2 Native patterns
            var condition = mapping.InfoPathCondition ?? mapping.InfoPathConditionExpression ?? "";

            // Also check action expressions (e.g., count(preceding-sibling::*) in setValue actions)
            if (mapping.InfoPathActions != null)
            {
                foreach (var action in mapping.InfoPathActions)
                {
                    var expr = action.InfoPathExpression ?? "";
                    if (!string.IsNullOrEmpty(expr))
                        condition = condition + " " + expr;
                }
            }
            var ruleType = mapping.InfoPathRuleType ?? "";
            var ruleName = mapping.InfoPathRuleName ?? "";

            // Number/date/currency formatting → K2 control DataType + Format properties
            if (condition.Contains("xdFormatting:", StringComparison.OrdinalIgnoreCase) ||
                condition.Contains("formatString(", StringComparison.OrdinalIgnoreCase))
            {
                mapping.Notes = "K2 Native: Handled by control DataType and Format properties";
                return true;
            }

            // Repeating table row position/selection → K2 List View handles natively
            if (condition.Contains("preceding-sibling::", StringComparison.OrdinalIgnoreCase) ||
                condition.Contains("position()", StringComparison.OrdinalIgnoreCase) ||
                condition.Contains("itemPosition", StringComparison.OrdinalIgnoreCase) ||
                condition.Contains("last()", StringComparison.OrdinalIgnoreCase))
            {
                mapping.Notes = "K2 Native: K2 List View architecture replaces this pattern";
                return true;
            }

            // Count-based repeating section logic → K2 List View
            if (condition.Contains("count(", StringComparison.OrdinalIgnoreCase) &&
                condition.Contains("sibling", StringComparison.OrdinalIgnoreCase))
            {
                mapping.Notes = "K2 Native: K2 List View handles repeating data natively";
                return true;
            }

            // InfoPath runtime feature checks → no K2 equivalent needed
            if (condition.Contains("function-available(", StringComparison.OrdinalIgnoreCase) ||
                condition.Contains("xdXDocument:GetDOM", StringComparison.OrdinalIgnoreCase))
            {
                mapping.Notes = "K2 Native: InfoPath runtime feature check, not applicable in K2";
                return true;
            }

            // XPath variable references in repeating context ($val=.) → K2 List View binding
            if (condition.Contains("$val=", StringComparison.OrdinalIgnoreCase) ||
                condition.Contains("$val =", StringComparison.OrdinalIgnoreCase))
            {
                mapping.Notes = "K2 Native: Repeating context variable binding handled by K2 List View";
                return true;
            }

            return false;
        }

        private bool IsComplexCondition(string condition)
        {
            if (string.IsNullOrEmpty(condition)) return false;

            // Only flag genuinely unsupported XPath functions as complex.
            // Simple "and"/"or"/"not(" are standard boolean operators that K2 supports.
            var complexIndicators = new[]
            {
                "sum(",
                "translate(", "substring(", "normalize-space(",
                "xdDate:", "xdMath:", "xdUser:"
            };

            return complexIndicators.Any(ind => condition.Contains(ind, StringComparison.OrdinalIgnoreCase));
        }

        private string MapActionType(string infoPathActionType)
        {
            return infoPathActionType?.ToLower() switch
            {
                "setvalue" => "Transfer",
                "hide" => "SetControlVisibility",
                "show" => "SetControlVisibility",
                "submit" => "Execute",
                "query" => "Execute",
                "switchview" => "Navigate",
                "close" => "Close",
                "calculate" => "Calculate",
                "formatting" => "ApplyStyle",
                "eventdefinition" => "Event",
                "eventproperty" => "EventProperty",
                _ => "Custom"
            };
        }

        private bool IsActionSupported(string actionType)
        {
            var supportedActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "setValue", "hide", "show", "switchView", "calculate",
                "query", "submit", "close", "formatting",
                "EventDefinition", "EventProperty"
            };

            return supportedActions.Contains(actionType ?? "");
        }

        private string GenerateK2ActionXml(FormRuleAction action, RuleMappingItem mapping)
        {
            var k2ActionType = MapActionType(action.Type);
            var sb = new StringBuilder();

            sb.AppendLine($"<Action ID=\"{Guid.NewGuid()}\" DefinitionID=\"{Guid.NewGuid()}\" Type=\"{k2ActionType}\" ExecutionType=\"Synchronous\">");

            switch (k2ActionType)
            {
                case "Transfer":
                    sb.AppendLine("  <Properties>");
                    sb.AppendLine("    <Property>");
                    sb.AppendLine("      <Name>TargetID</Name>");
                    sb.AppendLine($"      <Value>{action.Target ?? "CONTROL_ID"}</Value>");
                    sb.AppendLine("    </Property>");
                    sb.AppendLine("  </Properties>");
                    if (!string.IsNullOrEmpty(action.Expression))
                    {
                        sb.AppendLine("  <Parameters>");
                        sb.AppendLine("    <Parameter>");
                        sb.AppendLine($"      <Expression>{EscapeXml(action.Expression)}</Expression>");
                        sb.AppendLine("    </Parameter>");
                        sb.AppendLine("  </Parameters>");
                    }
                    break;

                case "SetControlVisibility":
                    sb.AppendLine("  <Properties>");
                    sb.AppendLine("    <Property>");
                    sb.AppendLine("      <Name>ControlID</Name>");
                    sb.AppendLine($"      <Value>{action.Target ?? "CONTROL_ID"}</Value>");
                    sb.AppendLine("    </Property>");
                    sb.AppendLine("    <Property>");
                    sb.AppendLine("      <Name>Visible</Name>");
                    sb.AppendLine($"      <Value>{(action.Type?.ToLower() == "show" ? "true" : "false")}</Value>");
                    sb.AppendLine("    </Property>");
                    sb.AppendLine("  </Properties>");
                    break;

                case "Navigate":
                    sb.AppendLine("  <Properties>");
                    sb.AppendLine("    <Property>");
                    sb.AppendLine("      <Name>TargetView</Name>");
                    sb.AppendLine($"      <Value>{action.Target ?? "VIEW_NAME"}</Value>");
                    sb.AppendLine("    </Property>");
                    sb.AppendLine("  </Properties>");
                    break;

                case "Calculate":
                    sb.AppendLine("  <Properties>");
                    sb.AppendLine("    <Property>");
                    sb.AppendLine("      <Name>TargetField</Name>");
                    sb.AppendLine($"      <Value>{action.Target ?? "FIELD_NAME"}</Value>");
                    sb.AppendLine("    </Property>");
                    sb.AppendLine("    <Property>");
                    sb.AppendLine("      <Name>Expression</Name>");
                    sb.AppendLine($"      <Value>{EscapeXml(action.Expression ?? "")}</Value>");
                    sb.AppendLine("    </Property>");
                    sb.AppendLine("  </Properties>");
                    break;

                default:
                    sb.AppendLine($"  <!-- InfoPath action type: {action.Type} -->");
                    sb.AppendLine($"  <!-- Target: {action.Target} -->");
                    sb.AppendLine($"  <!-- Expression: {EscapeXml(action.Expression ?? "")} -->");
                    break;
            }

            sb.AppendLine("</Action>");
            return sb.ToString();
        }

        private string GenerateK2RuleXml(RuleMappingItem mapping, FormRule rule)
        {
            var sb = new StringBuilder();

            sb.AppendLine("<!-- K2 SmartForms Rule -->");
            sb.AppendLine($"<!-- InfoPath Rule: {mapping.InfoPathRuleName} ({mapping.InfoPathRuleType}) -->");
            sb.AppendLine($"<!-- Status: {mapping.StatusDisplay} -->");
            sb.AppendLine();

            sb.AppendLine($"<Event ID=\"{Guid.NewGuid()}\" DefinitionID=\"{Guid.NewGuid()}\" Type=\"Rule\">");
            sb.AppendLine($"  <Name>{EscapeXml(mapping.InfoPathRuleName ?? "Rule")}</Name>");

            // Add condition if present
            if (!string.IsNullOrEmpty(mapping.InfoPathCondition))
            {
                sb.AppendLine("  <Conditions>");
                sb.AppendLine($"    <!-- Original InfoPath Condition: {EscapeXml(mapping.InfoPathCondition)} -->");
                sb.AppendLine("    <Condition>");
                sb.AppendLine($"      <Expression>{ConvertConditionToK2(mapping.InfoPathCondition)}</Expression>");
                sb.AppendLine("    </Condition>");
                sb.AppendLine("  </Conditions>");
            }

            sb.AppendLine("  <Handlers>");
            sb.AppendLine($"    <Handler ID=\"{Guid.NewGuid()}\" DefinitionID=\"{Guid.NewGuid()}\">");
            sb.AppendLine("      <Actions>");

            foreach (var action in mapping.InfoPathActions)
            {
                sb.Append(IndentXml(action.K2ActionXml, 8));
            }

            sb.AppendLine("      </Actions>");
            sb.AppendLine("    </Handler>");
            sb.AppendLine("  </Handlers>");
            sb.AppendLine("</Event>");

            return sb.ToString();
        }

        private string GenerateK2RequiredValidationXml(ValidationRule validation)
        {
            try
            {
                // Use K2RuleXmlBuilder to create proper K2 SDK-compliant XML
                var controlId = validation?.ControlId ?? Guid.NewGuid().ToString();
                var controlName = validation?.ControlName ?? validation?.ControlId ?? "Field";
                var errorMessage = validation?.ErrorMessage ?? $"{controlName} is required";
                var viewId = Guid.NewGuid().ToString(); // Preview uses placeholder
                var viewName = "MainView";

                var k2Rule = _ruleBuilder.BuildRequiredValidationRule(
                    controlId, controlName, errorMessage, viewId, viewName);

                return FormatK2RuleAsXml(k2Rule, "Required Field Validation", validation?.Expression);
            }
            catch (Exception ex)
            {
                return $"<!-- Error generating K2 XML: {EscapeXml(ex.Message)} -->";
            }
        }

        private string GenerateK2PatternValidationXml(ValidationRule validation)
        {
            try
            {
                var controlId = validation?.ControlId ?? Guid.NewGuid().ToString();
                var controlName = validation?.ControlName ?? validation?.ControlId ?? "Field";
                var errorMessage = validation?.ErrorMessage ?? "Invalid format";
                var viewId = Guid.NewGuid().ToString();
                var viewName = "MainView";

                // Build pattern validation rule using advanced condition
                var k2Rule = new K2Rule
                {
                    FriendlyName = $"Pattern validation for {controlName}",
                    Location = "View",
                    Event = new K2Event
                    {
                        EventType = K2EventType.ViewControlEvent,
                        Name = "OnChange",
                        SourceId = controlId,
                        SourceType = "Control",
                        SourceName = controlName,
                        SourceDisplayName = controlName,
                        ViewId = viewId,
                        ViewName = viewName,
                        Type = "User"
                    }
                };

                var handler = new K2Handler
                {
                    HandlerType = K2HandlerType.If,
                    Name = "IfLogicalHandler",
                    Location = "View"
                };

                // Advanced condition for pattern matching
                handler.Conditions.Add(new K2Condition
                {
                    ConditionType = K2ConditionType.AdvancedCondition,
                    Name = "Pattern validation",
                    ControlId = controlId,
                    ControlName = controlName,
                    DataType = "Text",
                    Expressions = new List<K2Expression>
                    {
                        new K2Expression
                        {
                            Operator = "NotMatches",
                            Left = new K2ExpressionItem
                            {
                                SourceType = K2MappingSourceType.Control,
                                SourceId = controlId,
                                SourceName = controlName,
                                DataType = "Text"
                            },
                            Right = new K2ExpressionItem
                            {
                                SourceType = K2MappingSourceType.Value,
                                Value = validation?.Pattern ?? ".*"
                            }
                        }
                    }
                });

                var action = new K2Action
                {
                    ActionType = K2ActionType.FormValidateCondition,
                    ControlId = controlId,
                    ControlName = controlName,
                    ViewId = viewId,
                    ViewName = viewName,
                    Location = "View"
                };
                action.Properties["ValidationMessage"] = new K2Property
                {
                    Name = "ValidationMessage",
                    Value = errorMessage,
                    DisplayValue = errorMessage
                };
                handler.Actions.Add(action);
                k2Rule.Handlers.Add(handler);

                return FormatK2RuleAsXml(k2Rule, "Pattern Validation", $"Pattern: {validation?.Pattern}");
            }
            catch (Exception ex)
            {
                return $"<!-- Error generating K2 XML: {EscapeXml(ex.Message)} -->";
            }
        }

        private string GenerateK2RangeValidationXml(ValidationRule validation)
        {
            try
            {
                var controlId = validation?.ControlId ?? Guid.NewGuid().ToString();
                var controlName = validation?.ControlName ?? validation?.ControlId ?? "Field";
                var errorMessage = validation?.ErrorMessage ?? "Value out of range";
                var viewId = Guid.NewGuid().ToString();
                var viewName = "MainView";

                // Build range validation rule with min/max conditions
                var k2Rule = new K2Rule
                {
                    FriendlyName = $"Range validation for {controlName}",
                    Location = "View",
                    Event = new K2Event
                    {
                        EventType = K2EventType.ViewControlEvent,
                        Name = "OnChange",
                        SourceId = controlId,
                        SourceType = "Control",
                        SourceName = controlName,
                        SourceDisplayName = controlName,
                        ViewId = viewId,
                        ViewName = viewName,
                        Type = "User"
                    }
                };

                var handler = new K2Handler
                {
                    HandlerType = K2HandlerType.If,
                    Name = "IfLogicalHandler",
                    Location = "View"
                };

                // Advanced condition for range check
                var expressions = new List<K2Expression>();
                if (!string.IsNullOrEmpty(validation?.MinValue))
                {
                    expressions.Add(new K2Expression
                    {
                        Operator = "LessThan",
                        Left = new K2ExpressionItem
                        {
                            SourceType = K2MappingSourceType.Control,
                            SourceId = controlId,
                            SourceName = controlName,
                            DataType = "Number"
                        },
                        Right = new K2ExpressionItem
                        {
                            SourceType = K2MappingSourceType.Value,
                            Value = validation.MinValue
                        }
                    });
                }
                if (!string.IsNullOrEmpty(validation?.MaxValue))
                {
                    expressions.Add(new K2Expression
                    {
                        Operator = "GreaterThan",
                        Left = new K2ExpressionItem
                        {
                            SourceType = K2MappingSourceType.Control,
                            SourceId = controlId,
                            SourceName = controlName,
                            DataType = "Number"
                        },
                        Right = new K2ExpressionItem
                        {
                            SourceType = K2MappingSourceType.Value,
                            Value = validation.MaxValue
                        }
                    });
                }

                handler.Conditions.Add(new K2Condition
                {
                    ConditionType = K2ConditionType.AdvancedCondition,
                    Name = "Range validation",
                    ControlId = controlId,
                    ControlName = controlName,
                    DataType = "Number",
                    Expressions = expressions
                });

                var action = new K2Action
                {
                    ActionType = K2ActionType.FormValidateCondition,
                    ControlId = controlId,
                    ControlName = controlName,
                    ViewId = viewId,
                    ViewName = viewName,
                    Location = "View"
                };
                action.Properties["ValidationMessage"] = new K2Property
                {
                    Name = "ValidationMessage",
                    Value = errorMessage,
                    DisplayValue = errorMessage
                };
                handler.Actions.Add(action);
                k2Rule.Handlers.Add(handler);

                return FormatK2RuleAsXml(k2Rule, "Range Validation", $"Min: {validation?.MinValue}, Max: {validation?.MaxValue}");
            }
            catch (Exception ex)
            {
                return $"<!-- Error generating K2 XML: {EscapeXml(ex.Message)} -->";
            }
        }

        private string GenerateK2CustomValidationXml(ValidationRule validation)
        {
            try
            {
                var controlId = validation?.ControlId ?? Guid.NewGuid().ToString();
                var controlName = validation?.ControlName ?? validation?.ControlId ?? "Field";
                var errorMessage = validation?.ErrorMessage ?? "Validation failed";
                var viewId = Guid.NewGuid().ToString();
                var viewName = "MainView";

                // Build custom validation rule structure
                var k2Rule = new K2Rule
                {
                    FriendlyName = $"Custom validation for {controlName}",
                    Location = "View",
                    Event = new K2Event
                    {
                        EventType = K2EventType.ViewControlEvent,
                        Name = "OnChange",
                        SourceId = controlId,
                        SourceType = "Control",
                        SourceName = controlName,
                        SourceDisplayName = controlName,
                        ViewId = viewId,
                        ViewName = viewName,
                        Type = "User"
                    }
                };

                var handler = new K2Handler
                {
                    HandlerType = K2HandlerType.If,
                    Name = "IfLogicalHandler",
                    Location = "View"
                };

                // Advanced condition placeholder - requires manual configuration
                handler.Conditions.Add(new K2Condition
                {
                    ConditionType = K2ConditionType.AdvancedCondition,
                    Name = "Custom validation condition",
                    ControlId = controlId,
                    ControlName = controlName,
                    DataType = "Text"
                });

                var action = new K2Action
                {
                    ActionType = K2ActionType.FormValidateCondition,
                    ControlId = controlId,
                    ControlName = controlName,
                    ViewId = viewId,
                    ViewName = viewName,
                    Location = "View"
                };
                action.Properties["ValidationMessage"] = new K2Property
                {
                    Name = "ValidationMessage",
                    Value = errorMessage,
                    DisplayValue = errorMessage
                };
                handler.Actions.Add(action);
                k2Rule.Handlers.Add(handler);

                var xml = FormatK2RuleAsXml(k2Rule, "Custom Validation - Requires Manual Implementation", validation?.Expression);
                return xml + "\n<!-- TODO: Configure the AdvancedCondition expressions based on your custom validation logic -->";
            }
            catch (Exception ex)
            {
                return $"<!-- Error generating K2 XML: {EscapeXml(ex.Message)} -->";
            }
        }

        private string GenerateK2VisibilityRuleXml(ConditionalRule conditional)
        {
            try
            {
                var sourceControlId = conditional?.SourceField ?? Guid.NewGuid().ToString();
                var sourceControlName = ExtractFieldName(conditional?.SourceField) ?? "TriggerField";
                var targetControlId = conditional?.TargetField ?? Guid.NewGuid().ToString();
                var targetControlName = ExtractFieldName(conditional?.TargetField) ?? "TargetControl";
                var conditionValue = ExtractConditionValue(conditional?.Condition);
                var viewId = Guid.NewGuid().ToString();
                var viewName = "MainView";

                // Use K2RuleXmlBuilder to create proper visibility rule
                var k2Rule = _ruleBuilder.BuildVisibilityRule(
                    sourceControlId, sourceControlName,
                    targetControlId, targetControlName,
                    conditionValue,
                    showWhenTrue: true, // Show when condition matches
                    viewId, viewName);

                return FormatK2RuleAsXml(k2Rule, "Visibility Rule", conditional?.Condition);
            }
            catch (Exception ex)
            {
                return $"<!-- Error generating K2 XML: {EscapeXml(ex.Message)} -->";
            }
        }

        private string GenerateK2FormattingRuleXml(ConditionalRule conditional)
        {
            try
            {
                var sourceControlId = conditional?.SourceField ?? Guid.NewGuid().ToString();
                var sourceControlName = ExtractFieldName(conditional?.SourceField) ?? "TriggerField";
                var targetControlId = conditional?.TargetField ?? Guid.NewGuid().ToString();
                var targetControlName = ExtractFieldName(conditional?.TargetField) ?? "TargetControl";
                var conditionValue = ExtractConditionValue(conditional?.Condition);
                var viewId = Guid.NewGuid().ToString();
                var viewName = "MainView";

                // Build formatting rule - K2 uses style classes
                var k2Rule = new K2Rule
                {
                    FriendlyName = $"Formatting rule for {targetControlName}",
                    Location = "View",
                    Event = new K2Event
                    {
                        EventType = K2EventType.ViewControlEvent,
                        Name = "OnChange",
                        SourceId = sourceControlId,
                        SourceType = "Control",
                        SourceName = sourceControlName,
                        SourceDisplayName = sourceControlName,
                        ViewId = viewId,
                        ViewName = viewName,
                        Type = "User"
                    }
                };

                var handler = new K2Handler
                {
                    HandlerType = K2HandlerType.If,
                    Name = "IfLogicalHandler",
                    Location = "View"
                };

                // Add condition
                if (!string.IsNullOrEmpty(conditionValue))
                {
                    handler.Conditions.Add(new K2Condition
                    {
                        ConditionType = K2ConditionType.SimpleEqualControlCondition,
                        ControlId = sourceControlId,
                        ControlName = sourceControlName,
                        ControlDisplayName = sourceControlName,
                        CompareValue = conditionValue,
                        DataType = "Text"
                    });
                }

                // Add style action using ControlTransfer to set style property
                var action = new K2Action
                {
                    ActionType = K2ActionType.ControlTransfer,
                    ControlId = targetControlId,
                    ControlName = targetControlName,
                    ViewId = viewId,
                    ViewName = viewName,
                    Location = "View",
                    Mappings = new List<K2Mapping>
                    {
                        new K2Mapping
                        {
                            SourceType = K2MappingSourceType.Value,
                            SourceValue = "highlight-style",
                            TargetType = K2MappingTargetType.ControlProperty,
                            TargetId = "cssclass",
                            TargetName = targetControlName
                        }
                    }
                };
                handler.Actions.Add(action);
                k2Rule.Handlers.Add(handler);

                // Add else handler to remove style
                // Reference: Both handlers use IfLogicalHandler with their own conditions
                var elseHandler = new K2Handler
                {
                    HandlerType = K2HandlerType.Else,
                    Name = "IfLogicalHandler",
                    Location = "View"
                };
                elseHandler.Actions.Add(new K2Action
                {
                    ActionType = K2ActionType.ControlTransfer,
                    ControlId = targetControlId,
                    ControlName = targetControlName,
                    ViewId = viewId,
                    ViewName = viewName,
                    Location = "View",
                    Mappings = new List<K2Mapping>
                    {
                        new K2Mapping
                        {
                            SourceType = K2MappingSourceType.Value,
                            SourceValue = "",
                            TargetType = K2MappingTargetType.ControlProperty,
                            TargetId = "cssclass",
                            TargetName = targetControlName
                        }
                    }
                });
                k2Rule.Handlers.Add(elseHandler);

                var xml = FormatK2RuleAsXml(k2Rule, "Formatting Rule", conditional?.Condition);
                return xml + "\n<!-- Note: K2 styling uses CSS classes. Define 'highlight-style' in your form's CSS. -->";
            }
            catch (Exception ex)
            {
                return $"<!-- Error generating K2 XML: {EscapeXml(ex.Message)} -->";
            }
        }

        private string GenerateK2CalculationRuleXml(ConditionalRule conditional)
        {
            try
            {
                var sourceControlId = conditional?.SourceField ?? Guid.NewGuid().ToString();
                var sourceControlName = ExtractFieldName(conditional?.SourceField) ?? "SourceField";
                var targetControlId = conditional?.TargetField ?? Guid.NewGuid().ToString();
                var targetControlName = ExtractFieldName(conditional?.TargetField) ?? "TargetField";
                var viewId = Guid.NewGuid().ToString();
                var viewName = "MainView";

                // Determine if this is a simple field transfer or a calculated value
                var expression = conditional?.Value;
                var isSimpleTransfer = !string.IsNullOrEmpty(expression) &&
                    !expression.Contains("(") && !expression.Contains("+") &&
                    !expression.Contains("-") && !expression.Contains("*");

                K2Rule k2Rule;

                if (isSimpleTransfer && !string.IsNullOrEmpty(conditional?.Value))
                {
                    // Use data transfer rule for simple field-to-field copies
                    k2Rule = _ruleBuilder.BuildDataTransferRule(
                        sourceControlId, sourceControlName,
                        targetControlId, targetControlName,
                        viewId, viewName, "OnChange");
                }
                else
                {
                    // Build calculated value rule
                    k2Rule = new K2Rule
                    {
                        FriendlyName = $"Calculate {targetControlName}",
                        Location = "View",
                        Event = new K2Event
                        {
                            EventType = K2EventType.ViewControlEvent,
                            Name = "OnChange",
                            SourceId = sourceControlId,
                            SourceType = "Control",
                            SourceName = sourceControlName,
                            SourceDisplayName = sourceControlName,
                            ViewId = viewId,
                            ViewName = viewName,
                            Type = "User"
                        }
                    };

                    var handler = new K2Handler
                    {
                        HandlerType = K2HandlerType.If,
                        Name = "IfLogicalHandler",
                        Location = "View"
                    };

                    var action = new K2Action
                    {
                        ActionType = K2ActionType.ControlTransfer,
                        ViewId = viewId,
                        ViewName = viewName,
                        Location = "View",
                        Mappings = new List<K2Mapping>
                        {
                            new K2Mapping
                            {
                                SourceType = K2MappingSourceType.Expression,
                                SourceValue = ConvertExpressionToK2(expression),
                                TargetType = K2MappingTargetType.ControlProperty,
                                TargetId = "Value",
                                TargetName = targetControlName
                            }
                        }
                    };
                    handler.Actions.Add(action);
                    k2Rule.Handlers.Add(handler);
                }

                return FormatK2RuleAsXml(k2Rule, "Calculation Rule", expression);
            }
            catch (Exception ex)
            {
                return $"<!-- Error generating K2 XML: {EscapeXml(ex.Message)} -->";
            }
        }

        /// <summary>
        /// Convert a K2Rule model to formatted XML string for preview
        /// </summary>
        private string FormatK2RuleAsXml(K2Rule rule, string ruleTypeHeader, string originalExpression)
        {
            var doc = new XmlDocument();
            var eventElement = _ruleBuilder.BuildEventElement(doc, rule);
            doc.AppendChild(eventElement);

            // Format the XML with proper indentation
            var sb = new StringBuilder();
            sb.AppendLine($"<!-- {ruleTypeHeader} -->");

            if (!string.IsNullOrEmpty(originalExpression))
            {
                sb.AppendLine($"<!-- Original InfoPath Expression: {EscapeXml(originalExpression)} -->");
            }

            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                OmitXmlDeclaration = true,
                NewLineChars = "\n",
                NewLineHandling = NewLineHandling.Replace
            };

            using (var writer = XmlWriter.Create(sb, settings))
            {
                doc.WriteTo(writer);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Extract the source/trigger field name from a condition expression.
        /// e.g., "my:category='Entertainment'" -> "my:category"
        ///       "not((../my:category != 'Gifts'))" -> "../my:category"
        ///       "." -> null (self-reference, not a named field)
        /// </summary>
        private string ExtractSourceFieldFromCondition(string condition)
        {
            if (string.IsNullOrEmpty(condition)) return null;

            // Match patterns like my:fieldName or ../my:fieldName at the start (before operator)
            var match = System.Text.RegularExpressions.Regex.Match(condition,
                @"(?:not\s*\(\s*\(?\s*)?(?:\.\./)?(?:my:[\w/]+:)*(my:[\w]+(?:/my:[\w]+)*)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (match.Success)
            {
                // Return the full XPath so the resolver can strip it
                return match.Groups[1].Value;
            }

            return null;
        }

        /// <summary>
        /// Extract a simple field name from an XPath or field reference
        /// </summary>
        private string ExtractFieldName(string fieldPath)
        {
            if (string.IsNullOrEmpty(fieldPath)) return null;

            // Split by / first to get path segments, then take the last one
            var segments = fieldPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0) return null;

            // Take the last segment and clean namespace prefixes
            var lastSegment = segments[segments.Length - 1]
                .Replace("my:", "")
                .Replace("@", "")
                .Trim();

            return string.IsNullOrEmpty(lastSegment) ? null : lastSegment;
        }

        /// <summary>
        /// Extract the comparison value from an InfoPath condition
        /// </summary>
        private string ExtractConditionValue(string condition)
        {
            if (string.IsNullOrEmpty(condition)) return "";

            // Look for quoted values like = "value" or = 'value'
            var doubleQuoteMatch = System.Text.RegularExpressions.Regex.Match(condition, @"=\s*""([^""]*)""|=\s*'([^']*)'");
            if (doubleQuoteMatch.Success)
            {
                return !string.IsNullOrEmpty(doubleQuoteMatch.Groups[1].Value)
                    ? doubleQuoteMatch.Groups[1].Value
                    : doubleQuoteMatch.Groups[2].Value;
            }

            // Look for numeric values
            var numericMatch = System.Text.RegularExpressions.Regex.Match(condition, @"=\s*(\d+(?:\.\d+)?)");
            if (numericMatch.Success)
            {
                return numericMatch.Groups[1].Value;
            }

            return "";
        }

        private string ConvertConditionToK2(string infoPathCondition)
        {
            if (string.IsNullOrEmpty(infoPathCondition)) return "";

            // Basic conversions - in production, this would be more sophisticated
            var k2Condition = infoPathCondition
                .Replace("/my:", "")
                .Replace("my:", "")
                .Replace("xd:isBlank", "IsNullOrEmpty")
                .Replace("= \"\"", "== \"\"")
                .Replace("!= \"\"", "!= \"\"")
                .Replace(" = ", " == ")
                .Replace(" and ", " &amp;&amp; ")
                .Replace(" or ", " || ");

            return $"<!-- TODO: Verify conversion -->{EscapeXml(k2Condition)}";
        }

        private string ConvertExpressionToK2(string infoPathExpression)
        {
            if (string.IsNullOrEmpty(infoPathExpression)) return "";

            // Basic expression conversion
            var k2Expression = infoPathExpression
                .Replace("/my:", "")
                .Replace("my:", "")
                .Replace("sum(", "Sum(")
                .Replace("count(", "Count(")
                .Replace("concat(", "Concat(");

            return EscapeXml(k2Expression);
        }

        private string EscapeXml(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }

        private string IndentXml(string xml, int spaces)
        {
            if (string.IsNullOrEmpty(xml)) return "";
            var indent = new string(' ', spaces);
            var lines = xml.Split('\n');
            return string.Join("\n", lines.Select(l => indent + l));
        }

        #region K2 Rule Model Generation

        /// <summary>
        /// Generate K2Rule models from RuleMappingItems for direct integration with K2 Writers
        /// </summary>
        public List<K2Rule> GenerateK2RuleModels(
            List<RuleMappingItem> mappings,
            Dictionary<string, ControlMapping> controlMappings,
            string viewId,
            string viewName)
        {
            if (mappings == null || mappings.Count == 0)
                return new List<K2Rule>();

            var mapper = new InfoPathToK2RuleMapper();
            mapper.SetControlMappings(controlMappings);

            var k2Rules = new List<K2Rule>();

            foreach (var mapping in mappings)
            {
                // Only process supported or partially supported rules
                if (mapping.Status != RuleMappingStatus.Supported &&
                    mapping.Status != RuleMappingStatus.PartiallySupported)
                {
                    continue;
                }

                try
                {
                    var k2Rule = mapper.MapFromRuleMappingItem(mapping, viewId, viewName);
                    if (k2Rule != null && k2Rule.Handlers.Any(h => h.Actions.Any()))
                    {
                        k2Rules.Add(k2Rule);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Failed to map rule '{mapping.InfoPathRuleName}': {ex.Message}");
                }
            }

            return k2Rules;
        }

        /// <summary>
        /// Generate K2Rule models directly from form definitions
        /// </summary>
        public List<K2Rule> GenerateK2RulesFromFormDefinition(
            InfoPathFormDefinition formDef,
            Dictionary<string, ControlMapping> controlMappings,
            string viewId,
            string viewName)
        {
            if (formDef == null)
                return new List<K2Rule>();

            var mapper = new InfoPathToK2RuleMapper();
            mapper.SetControlMappings(controlMappings);

            var k2Rules = new List<K2Rule>();

            // Map validation rules
            if (formDef.Validations != null)
            {
                foreach (var validation in formDef.Validations)
                {
                    try
                    {
                        var k2Rule = mapper.MapValidationRule(validation, viewId, viewName);
                        if (k2Rule != null)
                        {
                            k2Rules.Add(k2Rule);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Failed to map validation rule: {ex.Message}");
                    }
                }
            }

            // Map conditional rules (visibility, calculations)
            if (formDef.ConditionalRules != null)
            {
                foreach (var conditional in formDef.ConditionalRules)
                {
                    try
                    {
                        K2Rule k2Rule = null;
                        if (conditional.Type == "Visibility")
                        {
                            k2Rule = mapper.MapVisibilityRule(conditional, viewId, viewName);
                        }
                        else if (conditional.Type == "Calculation")
                        {
                            k2Rule = mapper.MapCalculationRule(conditional, viewId, viewName);
                        }

                        if (k2Rule != null)
                        {
                            k2Rules.Add(k2Rule);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Failed to map conditional rule: {ex.Message}");
                    }
                }
            }

            // Map standard form rules
            if (formDef.Rules != null)
            {
                foreach (var rule in formDef.Rules)
                {
                    try
                    {
                        var k2Rule = mapper.MapFormRule(rule, viewId, viewName);
                        if (k2Rule != null)
                        {
                            k2Rules.Add(k2Rule);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Failed to map form rule '{rule.Name}': {ex.Message}");
                    }
                }
            }

            return k2Rules;
        }

        /// <summary>
        /// Build control mappings from field information
        /// </summary>
        public Dictionary<string, ControlMapping> BuildControlMappings(
            Dictionary<string, string> controlIdMap,
            Dictionary<string, string> controlToFieldMap)
        {
            var mappings = new Dictionary<string, ControlMapping>();

            foreach (var kvp in controlIdMap)
            {
                var fieldName = kvp.Key;
                var controlId = kvp.Value;

                mappings[fieldName] = new ControlMapping
                {
                    InfoPathFieldName = fieldName,
                    K2ControlId = controlId,
                    K2ControlName = fieldName,
                    DataType = "Text"
                };
            }

            return mappings;
        }

        #endregion

        private Dictionary<string, K2RuleTemplate> InitializeRuleTemplates()
        {
            return new Dictionary<string, K2RuleTemplate>
            {
                ["visibility"] = new K2RuleTemplate
                {
                    TemplateName = "Visibility Rule",
                    Description = "Show/hide controls based on conditions",
                    InfoPathRuleType = "Conditional",
                    K2EventType = "OnChange",
                    K2ActionType = "SetControlVisibility"
                },
                ["calculation"] = new K2RuleTemplate
                {
                    TemplateName = "Calculation Rule",
                    Description = "Calculate field values",
                    InfoPathRuleType = "Calculation",
                    K2EventType = "OnChange",
                    K2ActionType = "Calculate"
                },
                ["validation"] = new K2RuleTemplate
                {
                    TemplateName = "Validation Rule",
                    Description = "Validate field input",
                    InfoPathRuleType = "Validation",
                    K2EventType = "OnChange",
                    K2ActionType = "Validate"
                },
                ["navigation"] = new K2RuleTemplate
                {
                    TemplateName = "Navigation Rule",
                    Description = "Navigate between views",
                    InfoPathRuleType = "Action",
                    K2EventType = "OnClick",
                    K2ActionType = "Navigate"
                }
            };
        }
    }
}
