using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FormGenerator.Core.Models;
using FormGenerator.Analyzers.Infopath;

namespace FormGenerator.Services
{
    /// <summary>
    /// Maps InfoPath rules to K2 SmartForms rule models
    /// </summary>
    public class InfoPathToK2RuleMapper
    {
        private readonly K2RuleXmlBuilder _ruleBuilder;
        private Dictionary<string, ControlMapping> _controlMappings;

        public InfoPathToK2RuleMapper()
        {
            _ruleBuilder = new K2RuleXmlBuilder();
            _controlMappings = new Dictionary<string, ControlMapping>();
        }

        /// <summary>
        /// Set control mappings from InfoPath field names to K2 control IDs
        /// </summary>
        public void SetControlMappings(Dictionary<string, ControlMapping> mappings)
        {
            _controlMappings = mappings ?? new Dictionary<string, ControlMapping>();
        }

        /// <summary>
        /// Map a ValidationRule to a K2Rule
        /// </summary>
        public K2Rule MapValidationRule(ValidationRule rule, string viewId, string viewName)
        {
            if (rule == null) return null;

            var controlMapping = GetControlMapping(rule.ControlId ?? rule.ControlName ?? rule.Binding);
            if (controlMapping == null) return null;

            if (rule.IsRequired)
            {
                return _ruleBuilder.BuildRequiredValidationRule(
                    controlMapping.K2ControlId,
                    controlMapping.K2ControlName,
                    rule.ErrorMessage ?? $"{controlMapping.K2ControlName} is required",
                    viewId,
                    viewName);
            }

            if (!string.IsNullOrEmpty(rule.Pattern))
            {
                return BuildPatternValidationRule(rule, controlMapping, viewId, viewName);
            }

            if (!string.IsNullOrEmpty(rule.MinValue) || !string.IsNullOrEmpty(rule.MaxValue))
            {
                return BuildRangeValidationRule(rule, controlMapping, viewId, viewName);
            }

            if (!string.IsNullOrEmpty(rule.Expression))
            {
                return BuildCustomValidationRule(rule, controlMapping, viewId, viewName);
            }

            return null;
        }

        /// <summary>
        /// Map a ConditionalRule (visibility) to a K2Rule
        /// </summary>
        public K2Rule MapVisibilityRule(ConditionalRule rule, string viewId, string viewName)
        {
            if (rule == null || rule.Type != "Visibility") return null;

            var sourceMapping = GetControlMapping(rule.SourceField);
            if (sourceMapping == null) return null;

            var targetMappings = rule.AffectedControls
                .Select(c => GetControlMapping(c))
                .Where(m => m != null)
                .ToList();

            if (!targetMappings.Any()) return null;

            // Parse the condition to extract the comparison value
            var conditionValue = ExtractConditionValue(rule.Condition);
            var showWhenTrue = !rule.Action?.Equals("Hide", StringComparison.OrdinalIgnoreCase) ?? true;

            // Build rule for first affected control (can extend to multiple)
            var targetMapping = targetMappings.First();

            return _ruleBuilder.BuildVisibilityRule(
                sourceMapping.K2ControlId,
                sourceMapping.K2ControlName,
                targetMapping.K2ControlId,
                targetMapping.K2ControlName,
                conditionValue,
                showWhenTrue,
                viewId,
                viewName);
        }

        /// <summary>
        /// Map a ConditionalRule (calculation) to a K2Rule
        /// </summary>
        public K2Rule MapCalculationRule(ConditionalRule rule, string viewId, string viewName)
        {
            if (rule == null || rule.Type != "Calculation") return null;

            var sourceMapping = GetControlMapping(rule.SourceField);
            if (sourceMapping == null) return null;
            var targetMapping = GetControlMapping(rule.TargetField);
            if (targetMapping == null) return null;

            if (string.IsNullOrEmpty(rule.Value))
            {
                // Direct field-to-field transfer
                return _ruleBuilder.BuildDataTransferRule(
                    sourceMapping.K2ControlId,
                    sourceMapping.K2ControlName,
                    targetMapping.K2ControlId,
                    targetMapping.K2ControlName,
                    viewId,
                    viewName);
            }
            else
            {
                // Set to literal value
                return _ruleBuilder.BuildSetValueRule(
                    sourceMapping.K2ControlId,
                    sourceMapping.K2ControlName,
                    targetMapping.K2ControlId,
                    targetMapping.K2ControlName,
                    rule.Value,
                    viewId,
                    viewName,
                    ExtractConditionValue(rule.Condition));
            }
        }

        /// <summary>
        /// Map a FormRule to a K2Rule
        /// </summary>
        public K2Rule MapFormRule(FormRule rule, string viewId, string viewName)
        {
            if (rule == null || !rule.Actions.Any()) return null;

            var k2Rule = new K2Rule
            {
                FriendlyName = rule.Name ?? "Mapped Rule",
                Location = "View"
            };

            // Determine event type based on rule type
            K2EventType eventType = K2EventType.ViewEvent;
            string eventName = "Init";

            if (rule.RuleType == "Action" && rule.Actions.Any(a => a.Type == "Submit" || a.Type == "Query"))
            {
                eventType = K2EventType.ViewControlEvent;
                eventName = "OnClick";
            }
            else if (rule.Actions.Any(a => a.Type == "SetValue" || a.Type == "Calculate"))
            {
                eventType = K2EventType.ViewControlEvent;
                eventName = "OnChange";
            }

            var sourceMapping = GetControlMapping(rule.AppliesTo);
            if (sourceMapping == null) return null;

            k2Rule.Event = new K2Event
            {
                EventType = eventType,
                Name = eventName,
                SourceId = sourceMapping.K2ControlId,
                SourceType = eventType == K2EventType.ViewEvent ? "View" : "Control",
                SourceName = sourceMapping.K2ControlName,
                SourceDisplayName = sourceMapping.K2ControlName,
                ViewId = viewId,
                ViewName = viewName,
                Type = "User"
            };

            var handler = new K2Handler
            {
                HandlerType = K2HandlerType.If,
                Name = "IfLogicalHandler",
                Location = "View"
            };

            // Add condition if present
            if (!string.IsNullOrEmpty(rule.Condition))
            {
                var condition = MapCondition(rule.Condition, sourceMapping);
                if (condition != null)
                {
                    handler.Conditions.Add(condition);
                }
            }

            // Map each action
            foreach (var action in rule.Actions)
            {
                var k2Action = MapAction(action, viewId, viewName);
                if (k2Action != null)
                {
                    handler.Actions.Add(k2Action);
                }
            }

            if (handler.Actions.Any())
            {
                k2Rule.Handlers.Add(handler);
            }

            return k2Rule;
        }

        /// <summary>
        /// Map a RuleMappingItem to a K2Rule
        /// </summary>
        public K2Rule MapFromRuleMappingItem(RuleMappingItem mapping, string viewId, string viewName)
        {
            if (mapping == null) return null;

            var k2Rule = new K2Rule
            {
                FriendlyName = mapping.InfoPathRuleName ?? "Mapped Rule",
                Location = "View"
            };

            // Determine event type
            K2EventType eventType;
            string eventName;
            string sourceType;

            switch (mapping.InfoPathRuleType?.ToLowerInvariant())
            {
                case "validation":
                    eventType = K2EventType.ViewEvent;
                    eventName = "Init";
                    sourceType = "View";
                    break;
                case "visibility":
                case "conditional":
                    eventType = K2EventType.ViewControlEvent;
                    eventName = "OnChange";
                    sourceType = "Control";
                    break;
                case "action":
                case "button":
                    eventType = K2EventType.ViewControlEvent;
                    eventName = "OnClick";
                    sourceType = "Control";
                    break;
                default:
                    eventType = K2EventType.ViewControlEvent;
                    eventName = "OnChange";
                    sourceType = "Control";
                    break;
            }

            var sourceMapping = GetControlMapping(mapping.InfoPathAppliesTo);
            if (sourceMapping == null) return null;

            k2Rule.Event = new K2Event
            {
                EventType = eventType,
                Name = eventName,
                SourceId = sourceMapping.K2ControlId,
                SourceType = sourceType,
                SourceName = sourceMapping.K2ControlName,
                SourceDisplayName = sourceMapping.K2ControlName,
                ViewId = viewId,
                ViewName = viewName,
                Type = "User"
            };

            var handler = new K2Handler
            {
                HandlerType = K2HandlerType.If,
                Name = "IfLogicalHandler",
                Location = "View"
            };

            // Add condition if present
            if (!string.IsNullOrEmpty(mapping.InfoPathCondition))
            {
                var condition = MapCondition(mapping.InfoPathCondition, sourceMapping);
                if (condition != null)
                {
                    handler.Conditions.Add(condition);
                }
            }

            // Map each action
            foreach (var actionMapping in mapping.InfoPathActions)
            {
                var k2Action = MapActionFromMapping(actionMapping, viewId, viewName);
                if (k2Action != null)
                {
                    handler.Actions.Add(k2Action);
                }
            }

            if (handler.Actions.Any())
            {
                k2Rule.Handlers.Add(handler);
            }

            return k2Rule;
        }

        #region Condition Mapping

        private K2Condition MapCondition(string infoPathCondition, ControlMapping sourceMapping)
        {
            if (string.IsNullOrEmpty(infoPathCondition)) return null;

            var condition = new K2Condition
            {
                ControlId = sourceMapping.K2ControlId,
                ControlName = sourceMapping.K2ControlName,
                ControlDisplayName = sourceMapping.K2ControlName,
                DataType = sourceMapping.DataType ?? "Text"
            };

            // Parse the InfoPath condition
            var normalizedCondition = NormalizeXPath(infoPathCondition);

            // Check for blank/empty conditions
            if (normalizedCondition.Contains("xd:isBlank") || normalizedCondition.Contains("= \"\""))
            {
                condition.ConditionType = K2ConditionType.SimpleBlankControlCondition;
                return condition;
            }

            if (normalizedCondition.Contains("not(xd:isBlank") || normalizedCondition.Contains("!= \"\""))
            {
                condition.ConditionType = K2ConditionType.SimpleNotBlankControlCondition;
                return condition;
            }

            // Check for equality conditions
            var equalMatch = Regex.Match(normalizedCondition, @"=\s*[""']([^""']*)[""']");
            if (equalMatch.Success)
            {
                condition.ConditionType = K2ConditionType.SimpleEqualControlCondition;
                condition.CompareValue = equalMatch.Groups[1].Value;
                return condition;
            }

            // Check for not-equal conditions
            var notEqualMatch = Regex.Match(normalizedCondition, @"!=\s*[""']([^""']*)[""']");
            if (notEqualMatch.Success)
            {
                condition.ConditionType = K2ConditionType.SimpleNotEqualControlCondition;
                condition.CompareValue = notEqualMatch.Groups[1].Value;
                return condition;
            }

            // For complex conditions, use AdvancedCondition
            condition.ConditionType = K2ConditionType.AdvancedCondition;
            condition.Expressions.Add(new K2Expression
            {
                Operator = "Equals",
                Left = new K2ExpressionItem
                {
                    SourceType = K2MappingSourceType.Control,
                    SourceId = sourceMapping.K2ControlId,
                    SourceName = sourceMapping.K2ControlName,
                    DataType = sourceMapping.DataType ?? "Text"
                },
                Right = new K2ExpressionItem
                {
                    SourceType = K2MappingSourceType.Value,
                    Value = ExtractConditionValue(infoPathCondition)
                }
            });

            return condition;
        }

        private string ExtractConditionValue(string condition)
        {
            if (string.IsNullOrEmpty(condition)) return "";

            // Extract value from various condition patterns
            // Pattern: field = "value" or field = 'value'
            var match = Regex.Match(condition, @"=\s*[""']([^""']*)[""']");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            // Pattern: field = number
            match = Regex.Match(condition, @"=\s*(\d+)");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            // Pattern: field = true/false
            match = Regex.Match(condition, @"=\s*(true|false)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            return "";
        }

        #endregion

        #region Action Mapping

        private K2Action MapAction(FormRuleAction action, string viewId, string viewName)
        {
            if (action == null) return null;

            var targetMapping = GetControlMapping(action.Target);
            if (targetMapping == null) return null;

            switch (action.Type?.ToLowerInvariant())
            {
                case "setvalue":
                case "calculate":
                    return CreateTransferAction(action, targetMapping, viewId, viewName);

                case "show":
                    return CreateVisibilityAction(action, targetMapping, viewId, viewName, true);

                case "hide":
                    return CreateVisibilityAction(action, targetMapping, viewId, viewName, false);

                case "submit":
                case "query":
                    return CreateExecuteAction(action, targetMapping, viewId, viewName);

                case "switchview":
                    return CreateNavigateAction(action, viewId, viewName);

                default:
                    return null;
            }
        }

        private K2Action MapActionFromMapping(RuleActionMapping actionMapping, string viewId, string viewName)
        {
            if (actionMapping == null) return null;

            var targetMapping = GetControlMapping(actionMapping.InfoPathTarget);
            if (targetMapping == null) return null;

            switch (actionMapping.InfoPathActionType?.ToLowerInvariant())
            {
                case "setvalue":
                case "calculate":
                    return CreateTransferActionFromMapping(actionMapping, targetMapping, viewId, viewName);

                case "show":
                    return CreateVisibilityActionFromMapping(actionMapping, targetMapping, viewId, viewName, true);

                case "hide":
                    return CreateVisibilityActionFromMapping(actionMapping, targetMapping, viewId, viewName, false);

                case "submit":
                case "query":
                    return CreateExecuteActionFromMapping(actionMapping, targetMapping, viewId, viewName);

                default:
                    return null;
            }
        }

        private K2Action CreateTransferAction(FormRuleAction action, ControlMapping targetMapping, string viewId, string viewName)
        {
            var k2Action = new K2Action
            {
                ActionType = K2ActionType.ControlTransfer,
                ViewId = viewId,
                ViewName = viewName,
                Location = "View"
            };

            if (!string.IsNullOrEmpty(action.Expression))
            {
                // Expression-based transfer (e.g., sum, concat)
                var convertedExpr = ConvertXPathToK2Expression(action.Expression);
                k2Action.Mappings.Add(new K2Mapping
                {
                    SourceType = K2MappingSourceType.Expression,
                    SourceValue = convertedExpr,
                    TargetType = K2MappingTargetType.ControlProperty,
                    TargetId = "Value",
                    TargetName = targetMapping.K2ControlName
                });
            }
            else if (action.Parameters.TryGetValue("value", out var value))
            {
                // Literal value
                k2Action.Mappings.Add(new K2Mapping
                {
                    SourceType = K2MappingSourceType.Value,
                    SourceValue = value,
                    TargetType = K2MappingTargetType.ControlProperty,
                    TargetId = "Value",
                    TargetName = targetMapping.K2ControlName
                });
            }
            else if (action.Parameters.TryGetValue("sourceField", out var sourceField))
            {
                // Field-to-field transfer
                var sourceMapping = GetControlMapping(sourceField);
                if (sourceMapping == null) return null;
                k2Action.Mappings.Add(new K2Mapping
                {
                    SourceType = K2MappingSourceType.Control,
                    SourceId = sourceMapping.K2ControlId,
                    SourceName = sourceMapping.K2ControlName,
                    TargetType = K2MappingTargetType.ControlProperty,
                    TargetId = "Value",
                    TargetName = targetMapping.K2ControlName
                });
            }

            return k2Action;
        }

        private K2Action CreateTransferActionFromMapping(RuleActionMapping actionMapping, ControlMapping targetMapping, string viewId, string viewName)
        {
            var k2Action = new K2Action
            {
                ActionType = K2ActionType.ControlTransfer,
                ViewId = viewId,
                ViewName = viewName,
                Location = "View"
            };

            if (!string.IsNullOrEmpty(actionMapping.InfoPathExpression))
            {
                var convertedExpr = ConvertXPathToK2Expression(actionMapping.InfoPathExpression);
                k2Action.Mappings.Add(new K2Mapping
                {
                    SourceType = K2MappingSourceType.Expression,
                    SourceValue = convertedExpr,
                    TargetType = K2MappingTargetType.ControlProperty,
                    TargetId = "Value",
                    TargetName = targetMapping.K2ControlName
                });
            }
            else if (actionMapping.InfoPathParameters.TryGetValue("value", out var value))
            {
                k2Action.Mappings.Add(new K2Mapping
                {
                    SourceType = K2MappingSourceType.Value,
                    SourceValue = value,
                    TargetType = K2MappingTargetType.ControlProperty,
                    TargetId = "Value",
                    TargetName = targetMapping.K2ControlName
                });
            }

            return k2Action;
        }

        private K2Action CreateVisibilityAction(FormRuleAction action, ControlMapping targetMapping, string viewId, string viewName, bool show)
        {
            return new K2Action
            {
                ActionType = show ? K2ActionType.ShowControl : K2ActionType.HideControl,
                ControlId = targetMapping.K2ControlId,
                ControlName = targetMapping.K2ControlName,
                ViewId = viewId,
                ViewName = viewName,
                Location = "View",
                Mappings = new List<K2Mapping>
                {
                    new K2Mapping
                    {
                        SourceType = K2MappingSourceType.Value,
                        SourceValue = show ? "True" : "False",
                        TargetType = K2MappingTargetType.ControlProperty,
                        TargetId = "isvisible",
                        TargetName = targetMapping.K2ControlName
                    }
                }
            };
        }

        private K2Action CreateVisibilityActionFromMapping(RuleActionMapping actionMapping, ControlMapping targetMapping, string viewId, string viewName, bool show)
        {
            return CreateVisibilityAction(new FormRuleAction { Target = actionMapping.InfoPathTarget }, targetMapping, viewId, viewName, show);
        }

        private K2Action CreateExecuteAction(FormRuleAction action, ControlMapping targetMapping, string viewId, string viewName)
        {
            return new K2Action
            {
                ActionType = K2ActionType.ViewMethodExecute,
                ViewId = viewId,
                ViewName = viewName,
                Location = "View",
                Method = action.Type == "Submit" ? "Save" : "GetList"
            };
        }

        private K2Action CreateExecuteActionFromMapping(RuleActionMapping actionMapping, ControlMapping targetMapping, string viewId, string viewName)
        {
            return CreateExecuteAction(new FormRuleAction { Type = actionMapping.InfoPathActionType }, targetMapping, viewId, viewName);
        }

        private K2Action CreateNavigateAction(FormRuleAction action, string viewId, string viewName)
        {
            var targetView = action.Parameters.TryGetValue("view", out var v) ? v : "";
            return new K2Action
            {
                ActionType = K2ActionType.FormNavigation,
                ViewId = viewId,
                ViewName = viewName,
                Location = "Form"
            };
        }

        #endregion

        #region Validation Rule Builders

        private K2Rule BuildPatternValidationRule(ValidationRule rule, ControlMapping controlMapping, string viewId, string viewName)
        {
            var k2Rule = new K2Rule
            {
                FriendlyName = $"Pattern validation for {controlMapping.K2ControlName}",
                Location = "View",
                Event = new K2Event
                {
                    EventType = K2EventType.ViewControlEvent,
                    Name = "OnChange",
                    SourceId = controlMapping.K2ControlId,
                    SourceType = "Control",
                    SourceName = controlMapping.K2ControlName,
                    SourceDisplayName = controlMapping.K2ControlName,
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
            var condition = new K2Condition
            {
                ConditionType = K2ConditionType.AdvancedCondition,
                ControlId = controlMapping.K2ControlId,
                ControlName = controlMapping.K2ControlName
            };

            // Pattern validation uses NotMatches
            condition.Expressions.Add(new K2Expression
            {
                Operator = "NotMatches",
                Left = new K2ExpressionItem
                {
                    SourceType = K2MappingSourceType.Control,
                    SourceId = controlMapping.K2ControlId,
                    SourceName = controlMapping.K2ControlName,
                    DataType = "Text"
                },
                Right = new K2ExpressionItem
                {
                    SourceType = K2MappingSourceType.Value,
                    Value = rule.Pattern
                }
            });

            handler.Conditions.Add(condition);

            var action = new K2Action
            {
                ActionType = K2ActionType.FormValidateCondition,
                ControlId = controlMapping.K2ControlId,
                ControlName = controlMapping.K2ControlName,
                ViewId = viewId,
                ViewName = viewName,
                Location = "View"
            };

            action.Properties["ValidationMessage"] = new K2Property
            {
                Name = "ValidationMessage",
                Value = rule.ErrorMessage ?? $"{controlMapping.K2ControlName} does not match the required format",
                DisplayValue = rule.ErrorMessage ?? $"{controlMapping.K2ControlName} does not match the required format"
            };

            handler.Actions.Add(action);
            k2Rule.Handlers.Add(handler);

            return k2Rule;
        }

        private K2Rule BuildRangeValidationRule(ValidationRule rule, ControlMapping controlMapping, string viewId, string viewName)
        {
            var k2Rule = new K2Rule
            {
                FriendlyName = $"Range validation for {controlMapping.K2ControlName}",
                Location = "View",
                Event = new K2Event
                {
                    EventType = K2EventType.ViewControlEvent,
                    Name = "OnChange",
                    SourceId = controlMapping.K2ControlId,
                    SourceType = "Control",
                    SourceName = controlMapping.K2ControlName,
                    SourceDisplayName = controlMapping.K2ControlName,
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

            var condition = new K2Condition
            {
                ConditionType = K2ConditionType.AdvancedCondition,
                ControlId = controlMapping.K2ControlId,
                ControlName = controlMapping.K2ControlName
            };

            // Add min value check if specified
            if (!string.IsNullOrEmpty(rule.MinValue))
            {
                condition.Expressions.Add(new K2Expression
                {
                    Operator = "LessThan",
                    Left = new K2ExpressionItem
                    {
                        SourceType = K2MappingSourceType.Control,
                        SourceId = controlMapping.K2ControlId,
                        SourceName = controlMapping.K2ControlName,
                        DataType = rule.DataType ?? "Number"
                    },
                    Right = new K2ExpressionItem
                    {
                        SourceType = K2MappingSourceType.Value,
                        Value = rule.MinValue
                    },
                    LogicalOperator = !string.IsNullOrEmpty(rule.MaxValue) ? "Or" : null
                });
            }

            // Add max value check if specified
            if (!string.IsNullOrEmpty(rule.MaxValue))
            {
                condition.Expressions.Add(new K2Expression
                {
                    Operator = "GreaterThan",
                    Left = new K2ExpressionItem
                    {
                        SourceType = K2MappingSourceType.Control,
                        SourceId = controlMapping.K2ControlId,
                        SourceName = controlMapping.K2ControlName,
                        DataType = rule.DataType ?? "Number"
                    },
                    Right = new K2ExpressionItem
                    {
                        SourceType = K2MappingSourceType.Value,
                        Value = rule.MaxValue
                    }
                });
            }

            handler.Conditions.Add(condition);

            var action = new K2Action
            {
                ActionType = K2ActionType.FormValidateCondition,
                ControlId = controlMapping.K2ControlId,
                ControlName = controlMapping.K2ControlName,
                ViewId = viewId,
                ViewName = viewName,
                Location = "View"
            };

            var message = BuildRangeErrorMessage(rule, controlMapping.K2ControlName);
            action.Properties["ValidationMessage"] = new K2Property
            {
                Name = "ValidationMessage",
                Value = message,
                DisplayValue = message
            };

            handler.Actions.Add(action);
            k2Rule.Handlers.Add(handler);

            return k2Rule;
        }

        private K2Rule BuildCustomValidationRule(ValidationRule rule, ControlMapping controlMapping, string viewId, string viewName)
        {
            var k2Rule = new K2Rule
            {
                FriendlyName = $"Custom validation for {controlMapping.K2ControlName}",
                Location = "View",
                Event = new K2Event
                {
                    EventType = K2EventType.ViewControlEvent,
                    Name = "OnChange",
                    SourceId = controlMapping.K2ControlId,
                    SourceType = "Control",
                    SourceName = controlMapping.K2ControlName,
                    SourceDisplayName = controlMapping.K2ControlName,
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

            // For custom expression, create advanced condition
            var condition = MapCondition(rule.Expression, controlMapping);
            if (condition != null)
            {
                handler.Conditions.Add(condition);
            }

            var action = new K2Action
            {
                ActionType = K2ActionType.FormValidateCondition,
                ControlId = controlMapping.K2ControlId,
                ControlName = controlMapping.K2ControlName,
                ViewId = viewId,
                ViewName = viewName,
                Location = "View"
            };

            action.Properties["ValidationMessage"] = new K2Property
            {
                Name = "ValidationMessage",
                Value = rule.ErrorMessage ?? $"{controlMapping.K2ControlName} validation failed",
                DisplayValue = rule.ErrorMessage ?? $"{controlMapping.K2ControlName} validation failed"
            };

            handler.Actions.Add(action);
            k2Rule.Handlers.Add(handler);

            return k2Rule;
        }

        private string BuildRangeErrorMessage(ValidationRule rule, string controlName)
        {
            if (!string.IsNullOrEmpty(rule.MinValue) && !string.IsNullOrEmpty(rule.MaxValue))
            {
                return rule.ErrorMessage ?? $"{controlName} must be between {rule.MinValue} and {rule.MaxValue}";
            }
            else if (!string.IsNullOrEmpty(rule.MinValue))
            {
                return rule.ErrorMessage ?? $"{controlName} must be at least {rule.MinValue}";
            }
            else
            {
                return rule.ErrorMessage ?? $"{controlName} must be at most {rule.MaxValue}";
            }
        }

        #endregion

        #region XPath Conversion

        /// <summary>
        /// Convert InfoPath XPath expression to K2 expression
        /// </summary>
        public string ConvertXPathToK2Expression(string xpath)
        {
            if (string.IsNullOrEmpty(xpath)) return "";

            var result = xpath;

            // Remove my: namespace prefix
            result = Regex.Replace(result, @"/my:", "/");
            result = Regex.Replace(result, @"my:", "");

            // Convert xd:isBlank to IsNullOrEmpty
            result = result.Replace("xd:isBlank(", "IsNullOrEmpty(");

            // Convert and/or/not
            result = Regex.Replace(result, @"\band\b", "&&", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"\bor\b", "||", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"\bnot\s*\(", "!(", RegexOptions.IgnoreCase);

            // Convert sum function
            result = Regex.Replace(result, @"sum\(([^)]+)\)", "Sum($1)", RegexOptions.IgnoreCase);

            // Convert count function
            result = Regex.Replace(result, @"count\(([^)]+)\)", "Count($1)", RegexOptions.IgnoreCase);

            // Convert concat function
            result = Regex.Replace(result, @"concat\(", "Concat(", RegexOptions.IgnoreCase);

            // Convert number function
            result = Regex.Replace(result, @"number\(([^)]+)\)", "ToNumber($1)", RegexOptions.IgnoreCase);

            // Convert string function
            result = Regex.Replace(result, @"string\(([^)]+)\)", "ToString($1)", RegexOptions.IgnoreCase);

            // Convert boolean comparisons
            result = Regex.Replace(result, @"=\s*true\(\)", "== true", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"=\s*false\(\)", "== false", RegexOptions.IgnoreCase);

            // Convert date functions
            result = Regex.Replace(result, @"xdDate:Today\(\)", "DateTime.Today", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"xdDate:Now\(\)", "DateTime.Now", RegexOptions.IgnoreCase);

            // Convert user functions
            result = Regex.Replace(result, @"xdUser:get_UserName\(\)", "Environment.UserName", RegexOptions.IgnoreCase);

            return result;
        }

        private string NormalizeXPath(string xpath)
        {
            if (string.IsNullOrEmpty(xpath)) return "";

            // Remove my: namespace prefix for easier parsing
            var result = Regex.Replace(xpath, @"/my:", "/");
            result = Regex.Replace(result, @"my:", "");

            return result.Trim();
        }

        #endregion

        #region Helper Methods

        private ControlMapping GetControlMapping(string fieldNameOrId)
        {
            if (string.IsNullOrEmpty(fieldNameOrId))
            {
                return null;
            }

            // Try to find in mappings
            if (_controlMappings.TryGetValue(fieldNameOrId, out var mapping))
            {
                return mapping;
            }

            // Try normalized name (strip XPath)
            var normalizedName = NormalizeFieldName(fieldNameOrId);
            if (_controlMappings.TryGetValue(normalizedName, out mapping))
            {
                return mapping;
            }

            // Try XPath-stripped match: /my:myFields/my:field1 -> field1
            var strippedName = fieldNameOrId;
            if (strippedName.Contains("/"))
            {
                var segments = strippedName.Split('/');
                strippedName = segments[segments.Length - 1].Replace("my:", "");
            }
            else
            {
                strippedName = strippedName.Replace("my:", "");
            }
            if (_controlMappings.TryGetValue(strippedName, out mapping))
            {
                return mapping;
            }

            // Try case-insensitive match across all mappings
            foreach (var kvp in _controlMappings)
            {
                if (string.Equals(kvp.Key, fieldNameOrId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(kvp.Key, normalizedName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(kvp.Key, strippedName, StringComparison.OrdinalIgnoreCase))
                {
                    return kvp.Value;
                }
            }

            // Return null instead of generating random GUIDs - callers will skip with a warning
            Console.WriteLine($"    Warning: Could not resolve control mapping for '{fieldNameOrId}'");
            return null;
        }

        private string NormalizeFieldName(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName)) return "";

            // Remove XPath notation
            var result = fieldName;
            result = Regex.Replace(result, @"^/+", "");
            result = Regex.Replace(result, @"/my:", "_");
            result = Regex.Replace(result, @"my:", "");
            result = Regex.Replace(result, @"/", "_");

            // Remove array notation
            result = Regex.Replace(result, @"\[\d+\]", "");

            return result;
        }

        #endregion
    }

    /// <summary>
    /// Represents a mapping between InfoPath field and K2 control
    /// </summary>
    public class ControlMapping
    {
        public string InfoPathFieldName { get; set; }
        public string InfoPathBinding { get; set; }
        public string K2ControlId { get; set; }
        public string K2ControlName { get; set; }
        public string K2ControlType { get; set; }
        public string DataType { get; set; }
        public string ViewId { get; set; }
        public string ViewName { get; set; }
    }
}
