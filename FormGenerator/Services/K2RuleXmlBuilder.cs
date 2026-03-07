using System;
using System.Collections.Generic;
using System.Xml;
using FormGenerator.Core.Models;

namespace FormGenerator.Services
{
    /// <summary>
    /// Builds K2 rule XML following the patterns from the K2 SDK RuleHelper.cs
    /// </summary>
    public class K2RuleXmlBuilder
    {
        /// <summary>
        /// Build a complete Event XML element from a K2Rule
        /// </summary>
        public XmlElement BuildEventElement(XmlDocument doc, K2Rule rule)
        {
            var eventElement = doc.CreateElement("Event");

            // Core attributes
            eventElement.SetAttribute("ID", rule.Event.Id.ToString().ToLowerInvariant());
            eventElement.SetAttribute("DefinitionID", rule.Event.DefinitionId.ToString().ToLowerInvariant());
            eventElement.SetAttribute("Type", rule.Event.Type);
            eventElement.SetAttribute("SourceID", rule.Event.SourceId ?? "");
            eventElement.SetAttribute("SourceType", rule.Event.SourceType);
            eventElement.SetAttribute("SourceName", rule.Event.SourceName ?? "");
            eventElement.SetAttribute("SourceDisplayName", rule.Event.SourceDisplayName ?? "");

            if (!string.IsNullOrEmpty(rule.Event.InstanceId))
            {
                eventElement.SetAttribute("InstanceID", rule.Event.InstanceId);
            }

            eventElement.SetAttribute("IsExtended", rule.Event.IsExtended.ToString());

            // Name element
            AddChildElement(doc, eventElement, "Name", rule.Event.Name);

            // Properties
            var propertiesElement = doc.CreateElement("Properties");
            eventElement.AppendChild(propertiesElement);

            if (!string.IsNullOrEmpty(rule.Event.ViewId))
            {
                AddPropertyElement(doc, propertiesElement, "ViewID", rule.Event.ViewId, rule.Event.ViewName, rule.Event.ViewName);
            }

            if (!string.IsNullOrEmpty(rule.FriendlyName))
            {
                AddPropertyElement(doc, propertiesElement, "RuleFriendlyName", rule.FriendlyName, rule.FriendlyName, null);
            }

            // K2 expects lowercase location values (e.g., "view" not "View")
            string normalizedLocation = (rule.Location ?? "view").ToLowerInvariant();
            AddPropertyElement(doc, propertiesElement, "Location", normalizedLocation, normalizedLocation, null);

            // Handlers
            var handlersElement = doc.CreateElement("Handlers");
            eventElement.AppendChild(handlersElement);

            foreach (var handler in rule.Handlers)
            {
                var handlerElement = BuildHandlerElement(doc, handler, normalizedLocation);
                handlersElement.AppendChild(handlerElement);
            }

            return eventElement;
        }

        /// <summary>
        /// Build a Handler XML element
        /// </summary>
        public XmlElement BuildHandlerElement(XmlDocument doc, K2Handler handler, string location)
        {
            var handlerElement = doc.CreateElement("Handler");

            handlerElement.SetAttribute("ID", handler.Id.ToString().ToLowerInvariant());
            handlerElement.SetAttribute("DefinitionID", handler.DefinitionId.ToString().ToLowerInvariant());

            // Handler properties
            var propertiesElement = doc.CreateElement("Properties");
            handlerElement.AppendChild(propertiesElement);

            string handlerName = GetHandlerName(handler.HandlerType);
            AddPropertyElement(doc, propertiesElement, "HandlerName", handlerName, handlerName, null);
            AddPropertyElement(doc, propertiesElement, "Location", location, location, null);

            // Conditions
            var conditionsElement = doc.CreateElement("Conditions");
            handlerElement.AppendChild(conditionsElement);

            foreach (var condition in handler.Conditions)
            {
                var conditionElement = BuildConditionElement(doc, condition);
                conditionsElement.AppendChild(conditionElement);
            }

            // Actions
            var actionsElement = doc.CreateElement("Actions");
            handlerElement.AppendChild(actionsElement);

            foreach (var action in handler.Actions)
            {
                var actionElement = BuildActionElement(doc, action);
                actionsElement.AppendChild(actionElement);
            }

            return handlerElement;
        }

        /// <summary>
        /// Build a Condition XML element
        /// </summary>
        public XmlElement BuildConditionElement(XmlDocument doc, K2Condition condition)
        {
            var conditionElement = doc.CreateElement("Condition");

            conditionElement.SetAttribute("ID", condition.Id.ToString().ToLowerInvariant());
            conditionElement.SetAttribute("DefinitionID", condition.DefinitionId.ToString().ToLowerInvariant());

            // Properties
            var propertiesElement = doc.CreateElement("Properties");
            conditionElement.AppendChild(propertiesElement);

            AddPropertyElement(doc, propertiesElement, "Name", condition.Name ?? GetConditionTypeName(condition.ConditionType), null, null);

            // Build expression based on condition type
            var expressionsElement = doc.CreateElement("Expressions");
            conditionElement.AppendChild(expressionsElement);

            switch (condition.ConditionType)
            {
                case K2ConditionType.SimpleEqualControlCondition:
                    BuildSimpleEqualExpression(doc, expressionsElement, condition);
                    break;
                case K2ConditionType.SimpleNotEqualControlCondition:
                    BuildSimpleNotEqualExpression(doc, expressionsElement, condition);
                    break;
                case K2ConditionType.SimpleBlankControlCondition:
                    BuildSimpleBlankExpression(doc, expressionsElement, condition);
                    break;
                case K2ConditionType.SimpleNotBlankControlCondition:
                    BuildSimpleNotBlankExpression(doc, expressionsElement, condition);
                    break;
                case K2ConditionType.AdvancedCondition:
                    BuildAdvancedExpression(doc, expressionsElement, condition);
                    break;
                default:
                    // For other condition types, build from expressions list
                    foreach (var expr in condition.Expressions)
                    {
                        var exprElement = BuildExpressionElement(doc, expr);
                        expressionsElement.AppendChild(exprElement);
                    }
                    break;
            }

            return conditionElement;
        }

        /// <summary>
        /// Build an Action XML element
        /// </summary>
        public XmlElement BuildActionElement(XmlDocument doc, K2Action action)
        {
            var actionElement = doc.CreateElement("Action");

            actionElement.SetAttribute("ID", action.Id.ToString().ToLowerInvariant());
            actionElement.SetAttribute("DefinitionID", action.DefinitionId.ToString().ToLowerInvariant());
            actionElement.SetAttribute("Type", GetActionTypeName(action.ActionType));
            actionElement.SetAttribute("ExecutionType", action.ExecutionType);

            if (!string.IsNullOrEmpty(action.InstanceId))
            {
                actionElement.SetAttribute("InstanceID", action.InstanceId);
            }

            // Properties
            var propertiesElement = doc.CreateElement("Properties");
            actionElement.AppendChild(propertiesElement);

            AddPropertyElement(doc, propertiesElement, "Location", action.Location, action.Location, null);

            // Add action-specific properties
            foreach (var prop in action.Properties)
            {
                AddPropertyElement(doc, propertiesElement, prop.Key, prop.Value.Value, prop.Value.DisplayValue, prop.Value.NameValue);
            }

            // For specific action types, add required properties
            switch (action.ActionType)
            {
                case K2ActionType.ShowControl:
                case K2ActionType.HideControl:
                    AddControlVisibilityProperties(doc, propertiesElement, action);
                    break;
                case K2ActionType.ControlTransfer:
                    AddControlTransferProperties(doc, propertiesElement, action);
                    break;
                case K2ActionType.ViewMethodExecute:
                    AddViewMethodExecuteProperties(doc, propertiesElement, action);
                    break;
                case K2ActionType.FormValidateCondition:
                    AddValidationProperties(doc, propertiesElement, action);
                    break;
            }

            // Parameters (mappings)
            if (action.Mappings.Count > 0)
            {
                var parametersElement = doc.CreateElement("Parameters");
                actionElement.AppendChild(parametersElement);

                foreach (var mapping in action.Mappings)
                {
                    var paramElement = BuildParameterElement(doc, mapping);
                    parametersElement.AppendChild(paramElement);
                }
            }

            // Results (for method execution)
            if (action.Results.Count > 0)
            {
                var resultsElement = doc.CreateElement("Results");
                actionElement.AppendChild(resultsElement);

                foreach (var result in action.Results)
                {
                    var resultElement = BuildResultElement(doc, result);
                    resultsElement.AppendChild(resultElement);
                }
            }

            return actionElement;
        }

        #region Condition Builders

        private void BuildSimpleEqualExpression(XmlDocument doc, XmlElement parent, K2Condition condition)
        {
            var equalsElement = doc.CreateElement("Equals");
            parent.AppendChild(equalsElement);

            // Left side - the control
            var leftItem = doc.CreateElement("Item");
            leftItem.SetAttribute("SourceType", "Control");
            leftItem.SetAttribute("SourceID", condition.ControlId ?? "");
            leftItem.SetAttribute("SourceName", condition.ControlName ?? "");
            leftItem.SetAttribute("SourceDisplayName", condition.ControlDisplayName ?? "");
            leftItem.SetAttribute("DataType", condition.DataType ?? "Text");
            equalsElement.AppendChild(leftItem);

            // Right side - the compare value
            var rightItem = doc.CreateElement("Item");
            rightItem.SetAttribute("SourceType", "Value");
            rightItem.InnerText = condition.CompareValue ?? "";
            equalsElement.AppendChild(rightItem);
        }

        private void BuildSimpleNotEqualExpression(XmlDocument doc, XmlElement parent, K2Condition condition)
        {
            var notEqualsElement = doc.CreateElement("NotEquals");
            parent.AppendChild(notEqualsElement);

            var leftItem = doc.CreateElement("Item");
            leftItem.SetAttribute("SourceType", "Control");
            leftItem.SetAttribute("SourceID", condition.ControlId ?? "");
            leftItem.SetAttribute("SourceName", condition.ControlName ?? "");
            leftItem.SetAttribute("SourceDisplayName", condition.ControlDisplayName ?? "");
            leftItem.SetAttribute("DataType", condition.DataType ?? "Text");
            notEqualsElement.AppendChild(leftItem);

            var rightItem = doc.CreateElement("Item");
            rightItem.SetAttribute("SourceType", "Value");
            rightItem.InnerText = condition.CompareValue ?? "";
            notEqualsElement.AppendChild(rightItem);
        }

        private void BuildSimpleBlankExpression(XmlDocument doc, XmlElement parent, K2Condition condition)
        {
            var isBlankElement = doc.CreateElement("IsEmpty");
            parent.AppendChild(isBlankElement);

            var item = doc.CreateElement("Item");
            item.SetAttribute("SourceType", "Control");
            item.SetAttribute("SourceID", condition.ControlId ?? "");
            item.SetAttribute("SourceName", condition.ControlName ?? "");
            item.SetAttribute("SourceDisplayName", condition.ControlDisplayName ?? "");
            item.SetAttribute("DataType", condition.DataType ?? "Text");
            isBlankElement.AppendChild(item);
        }

        private void BuildSimpleNotBlankExpression(XmlDocument doc, XmlElement parent, K2Condition condition)
        {
            var isNotBlankElement = doc.CreateElement("IsNotEmpty");
            parent.AppendChild(isNotBlankElement);

            var item = doc.CreateElement("Item");
            item.SetAttribute("SourceType", "Control");
            item.SetAttribute("SourceID", condition.ControlId ?? "");
            item.SetAttribute("SourceName", condition.ControlName ?? "");
            item.SetAttribute("SourceDisplayName", condition.ControlDisplayName ?? "");
            item.SetAttribute("DataType", condition.DataType ?? "Text");
            isNotBlankElement.AppendChild(item);
        }

        private void BuildAdvancedExpression(XmlDocument doc, XmlElement parent, K2Condition condition)
        {
            foreach (var expr in condition.Expressions)
            {
                var exprElement = BuildExpressionElement(doc, expr);
                parent.AppendChild(exprElement);
            }
        }

        private XmlElement BuildExpressionElement(XmlDocument doc, K2Expression expr)
        {
            var exprElement = doc.CreateElement(expr.Operator ?? "Equals");

            if (expr.Left != null)
            {
                var leftItem = BuildExpressionItemElement(doc, expr.Left);
                exprElement.AppendChild(leftItem);
            }

            if (expr.Right != null)
            {
                var rightItem = BuildExpressionItemElement(doc, expr.Right);
                exprElement.AppendChild(rightItem);
            }

            return exprElement;
        }

        private XmlElement BuildExpressionItemElement(XmlDocument doc, K2ExpressionItem item)
        {
            var itemElement = doc.CreateElement("Item");

            itemElement.SetAttribute("SourceType", GetSourceTypeName(item.SourceType));

            if (!string.IsNullOrEmpty(item.SourceId))
                itemElement.SetAttribute("SourceID", item.SourceId);
            if (!string.IsNullOrEmpty(item.SourceName))
                itemElement.SetAttribute("SourceName", item.SourceName);
            if (!string.IsNullOrEmpty(item.SourceDisplayName))
                itemElement.SetAttribute("SourceDisplayName", item.SourceDisplayName);
            if (!string.IsNullOrEmpty(item.DataType))
                itemElement.SetAttribute("DataType", item.DataType);
            if (!string.IsNullOrEmpty(item.ViewId))
                itemElement.SetAttribute("ViewID", item.ViewId);

            if (item.SourceType == K2MappingSourceType.Value && !string.IsNullOrEmpty(item.Value))
            {
                itemElement.InnerText = item.Value;
            }

            return itemElement;
        }

        #endregion

        #region Action Property Builders

        private void AddControlVisibilityProperties(XmlDocument doc, XmlElement propertiesElement, K2Action action)
        {
            if (!string.IsNullOrEmpty(action.ControlId))
            {
                AddPropertyElement(doc, propertiesElement, "ControlID", action.ControlId, action.ControlName, action.ControlName);
            }
            if (!string.IsNullOrEmpty(action.ViewId))
            {
                AddPropertyElement(doc, propertiesElement, "ViewID", action.ViewId, action.ViewName, action.ViewName);
            }
        }

        private void AddControlTransferProperties(XmlDocument doc, XmlElement propertiesElement, K2Action action)
        {
            if (!string.IsNullOrEmpty(action.ViewId))
            {
                AddPropertyElement(doc, propertiesElement, "ViewID", action.ViewId, action.ViewName, action.ViewName);
            }
        }

        private void AddViewMethodExecuteProperties(XmlDocument doc, XmlElement propertiesElement, K2Action action)
        {
            if (!string.IsNullOrEmpty(action.ViewId))
            {
                AddPropertyElement(doc, propertiesElement, "ViewID", action.ViewId, action.ViewName, action.ViewName);
            }
            if (!string.IsNullOrEmpty(action.Method))
            {
                AddPropertyElement(doc, propertiesElement, "Method", action.Method, action.Method, null);
            }
            if (!string.IsNullOrEmpty(action.ObjectId))
            {
                AddPropertyElement(doc, propertiesElement, "ObjectID", action.ObjectId, action.ObjectName, action.ObjectName);
            }
        }

        private void AddValidationProperties(XmlDocument doc, XmlElement propertiesElement, K2Action action)
        {
            if (!string.IsNullOrEmpty(action.ControlId))
            {
                AddPropertyElement(doc, propertiesElement, "ControlID", action.ControlId, action.ControlName, action.ControlName);
            }
            // Validation message property if present
            if (action.Properties.TryGetValue("ValidationMessage", out var validationMsg))
            {
                AddPropertyElement(doc, propertiesElement, "ValidationMessage", validationMsg.Value, validationMsg.DisplayValue, null);
            }
        }

        #endregion

        #region Parameter/Mapping Builders

        private XmlElement BuildParameterElement(XmlDocument doc, K2Mapping mapping)
        {
            var paramElement = doc.CreateElement("Parameter");

            paramElement.SetAttribute("SourceType", GetSourceTypeName(mapping.SourceType));

            if (!string.IsNullOrEmpty(mapping.SourceId))
                paramElement.SetAttribute("SourceID", mapping.SourceId);
            if (!string.IsNullOrEmpty(mapping.SourceName))
                paramElement.SetAttribute("SourceName", mapping.SourceName);

            paramElement.SetAttribute("TargetType", GetTargetTypeName(mapping.TargetType));
            paramElement.SetAttribute("TargetID", mapping.TargetId ?? "");

            if (!string.IsNullOrEmpty(mapping.TargetName))
                paramElement.SetAttribute("TargetName", mapping.TargetName);
            if (!string.IsNullOrEmpty(mapping.TargetInstanceId))
                paramElement.SetAttribute("TargetInstanceID", mapping.TargetInstanceId);

            // For value source type, include as child element with xml:space="preserve"
            if (mapping.SourceType == K2MappingSourceType.Value && !string.IsNullOrEmpty(mapping.SourceValue))
            {
                var sourceValueElement = doc.CreateElement("SourceValue");
                sourceValueElement.SetAttribute("xml:space", "preserve");
                sourceValueElement.InnerText = mapping.SourceValue;
                paramElement.AppendChild(sourceValueElement);
            }

            return paramElement;
        }

        private XmlElement BuildResultElement(XmlDocument doc, K2ActionResult result)
        {
            var resultElement = doc.CreateElement("Result");

            if (!string.IsNullOrEmpty(result.SourceId))
                resultElement.SetAttribute("SourceID", result.SourceId);
            if (!string.IsNullOrEmpty(result.SourceName))
                resultElement.SetAttribute("SourceName", result.SourceName);
            if (!string.IsNullOrEmpty(result.TargetId))
                resultElement.SetAttribute("TargetID", result.TargetId);
            if (!string.IsNullOrEmpty(result.TargetName))
                resultElement.SetAttribute("TargetName", result.TargetName);
            if (!string.IsNullOrEmpty(result.TargetType))
                resultElement.SetAttribute("TargetType", result.TargetType);

            return resultElement;
        }

        #endregion

        #region Helper Methods

        private void AddChildElement(XmlDocument doc, XmlElement parent, string name, string value)
        {
            var element = doc.CreateElement(name);
            element.InnerText = value ?? "";
            parent.AppendChild(element);
        }

        private void AddPropertyElement(XmlDocument doc, XmlElement propertiesElement, string name, string value, string displayValue, string nameValue)
        {
            var propertyElement = doc.CreateElement("Property");
            propertiesElement.AppendChild(propertyElement);

            AddChildElement(doc, propertyElement, "Name", name);
            AddChildElement(doc, propertyElement, "Value", value ?? "");

            if (!string.IsNullOrEmpty(displayValue))
            {
                AddChildElement(doc, propertyElement, "DisplayValue", displayValue);
            }

            if (!string.IsNullOrEmpty(nameValue))
            {
                AddChildElement(doc, propertyElement, "NameValue", nameValue);
            }
        }

        private string GetHandlerName(K2HandlerType handlerType)
        {
            switch (handlerType)
            {
                case K2HandlerType.If:
                    return "IfLogicalHandler";
                case K2HandlerType.Else:
                    return "then";
                case K2HandlerType.Error:
                    return "ErrorHandler";
                case K2HandlerType.ForEach:
                    return "ForEachHandler";
                default:
                    return "IfLogicalHandler";
            }
        }

        private string GetConditionTypeName(K2ConditionType conditionType)
        {
            return conditionType.ToString();
        }

        private string GetActionTypeName(K2ActionType actionType)
        {
            // Map action types to K2 type strings
            switch (actionType)
            {
                case K2ActionType.ControlTransfer:
                case K2ActionType.ServerControlTransfer:
                    return "Transfer";
                case K2ActionType.ShowControl:
                case K2ActionType.HideControl:
                    // K2 uses Transfer action with isvisible property for visibility
                    return "Transfer";
                case K2ActionType.EnableControl:
                    return "EnableControl";
                case K2ActionType.DisableControl:
                    return "DisableControl";
                case K2ActionType.ShowView:
                case K2ActionType.ShowViewFilter:
                    return "ShowView";
                case K2ActionType.HideView:
                case K2ActionType.HideViewFilter:
                    return "HideView";
                case K2ActionType.ViewMethodExecute:
                case K2ActionType.ServerViewMethodExecute:
                    return "Execute";
                case K2ActionType.ObjectMethodExecute:
                case K2ActionType.ServerObjectMethodExecute:
                    return "Execute";
                case K2ActionType.FormNavigation:
                    return "Navigate";
                case K2ActionType.FormValidateCondition:
                    return "Validate";
                case K2ActionType.ShowAlert:
                    return "ShowMessage";
                case K2ActionType.ShowConfirmation:
                    return "ShowConfirmation";
                default:
                    return actionType.ToString();
            }
        }

        private string GetSourceTypeName(K2MappingSourceType sourceType)
        {
            switch (sourceType)
            {
                case K2MappingSourceType.Value:
                    return "Value";
                case K2MappingSourceType.Control:
                    return "Control";
                case K2MappingSourceType.ViewField:
                    return "ViewField";
                case K2MappingSourceType.ViewParameter:
                    return "ViewParameter";
                case K2MappingSourceType.FormParameter:
                    return "FormParameter";
                case K2MappingSourceType.Expression:
                    return "Expression";
                case K2MappingSourceType.SystemVariable:
                    return "SystemVariable";
                case K2MappingSourceType.MethodParameter:
                    return "MethodParameter";
                case K2MappingSourceType.ControlProperty:
                    return "ControlProperty";
                default:
                    return sourceType.ToString();
            }
        }

        private string GetTargetTypeName(K2MappingTargetType targetType)
        {
            switch (targetType)
            {
                case K2MappingTargetType.ControlProperty:
                    return "ControlProperty";
                case K2MappingTargetType.ViewProperty:
                    return "ViewProperty";
                case K2MappingTargetType.ViewField:
                    return "ViewField";
                case K2MappingTargetType.MethodParameter:
                    return "MethodParameter";
                case K2MappingTargetType.MethodRequiredProperty:
                    return "MethodRequiredProperty";
                case K2MappingTargetType.MethodOptionalProperty:
                    return "MethodOptionalProperty";
                default:
                    return targetType.ToString();
            }
        }

        #endregion

        #region High-Level Rule Builders

        /// <summary>
        /// Build a visibility rule (show/hide control based on condition)
        /// </summary>
        public K2Rule BuildVisibilityRule(
            string triggerControlId,
            string triggerControlName,
            string targetControlId,
            string targetControlName,
            string conditionValue,
            bool showWhenTrue,
            string viewId,
            string viewName)
        {
            var rule = new K2Rule
            {
                FriendlyName = $"Visibility rule for {targetControlName}",
                Location = "View",
                Event = new K2Event
                {
                    EventType = K2EventType.ViewControlEvent,
                    Name = "OnChange",
                    SourceId = triggerControlId,
                    SourceType = "Control",
                    SourceName = triggerControlName,
                    SourceDisplayName = triggerControlName,
                    ViewId = viewId,
                    ViewName = viewName,
                    Type = "User"
                }
            };

            // Handler for when condition is true
            var trueHandler = new K2Handler
            {
                HandlerType = K2HandlerType.If,
                Name = "IfLogicalHandler",
                Location = "View"
            };

            trueHandler.Conditions.Add(new K2Condition
            {
                ConditionType = K2ConditionType.SimpleEqualControlCondition,
                ControlId = triggerControlId,
                ControlName = triggerControlName,
                ControlDisplayName = triggerControlName,
                CompareValue = conditionValue,
                DataType = "Text"
            });

            trueHandler.Actions.Add(new K2Action
            {
                ActionType = showWhenTrue ? K2ActionType.ShowControl : K2ActionType.HideControl,
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
                        SourceValue = showWhenTrue ? "True" : "False",
                        TargetType = K2MappingTargetType.ControlProperty,
                        TargetId = "isvisible",
                        TargetName = targetControlName
                    }
                }
            });

            rule.Handlers.Add(trueHandler);

            // Handler for else case (opposite visibility)
            var elseHandler = new K2Handler
            {
                HandlerType = K2HandlerType.Else,
                Name = "then",
                Location = "View"
            };

            elseHandler.Actions.Add(new K2Action
            {
                ActionType = showWhenTrue ? K2ActionType.HideControl : K2ActionType.ShowControl,
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
                        SourceValue = showWhenTrue ? "False" : "True",
                        TargetType = K2MappingTargetType.ControlProperty,
                        TargetId = "isvisible",
                        TargetName = targetControlName
                    }
                }
            });

            rule.Handlers.Add(elseHandler);

            return rule;
        }

        /// <summary>
        /// Build a required field validation rule
        /// </summary>
        public K2Rule BuildRequiredValidationRule(
            string controlId,
            string controlName,
            string validationMessage,
            string viewId,
            string viewName)
        {
            var rule = new K2Rule
            {
                FriendlyName = $"Required validation for {controlName}",
                Location = "View",
                Event = new K2Event
                {
                    EventType = K2EventType.ViewEvent,
                    Name = "Init",
                    SourceId = viewId,
                    SourceType = "View",
                    SourceName = viewName,
                    SourceDisplayName = viewName,
                    ViewId = viewId,
                    ViewName = viewName,
                    Type = "System"
                }
            };

            var handler = new K2Handler
            {
                HandlerType = K2HandlerType.If,
                Name = "IfLogicalHandler",
                Location = "View"
            };

            handler.Conditions.Add(new K2Condition
            {
                ConditionType = K2ConditionType.SimpleBlankControlCondition,
                ControlId = controlId,
                ControlName = controlName,
                ControlDisplayName = controlName,
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
                Value = validationMessage ?? $"{controlName} is required",
                DisplayValue = validationMessage ?? $"{controlName} is required"
            };

            handler.Actions.Add(action);
            rule.Handlers.Add(handler);

            return rule;
        }

        /// <summary>
        /// Build a data transfer rule (copy value from one control to another)
        /// </summary>
        public K2Rule BuildDataTransferRule(
            string sourceControlId,
            string sourceControlName,
            string targetControlId,
            string targetControlName,
            string viewId,
            string viewName,
            string triggerEvent = "OnChange")
        {
            var rule = new K2Rule
            {
                FriendlyName = $"Transfer {sourceControlName} to {targetControlName}",
                Location = "View",
                Event = new K2Event
                {
                    EventType = K2EventType.ViewControlEvent,
                    Name = triggerEvent,
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
                        SourceType = K2MappingSourceType.Control,
                        SourceId = sourceControlId,
                        SourceName = sourceControlName,
                        TargetType = K2MappingTargetType.ControlProperty,
                        TargetId = "Value",
                        TargetName = targetControlName
                    }
                }
            };

            handler.Actions.Add(action);
            rule.Handlers.Add(handler);

            return rule;
        }

        /// <summary>
        /// Build a set value rule (set control to literal value)
        /// </summary>
        public K2Rule BuildSetValueRule(
            string triggerControlId,
            string triggerControlName,
            string targetControlId,
            string targetControlName,
            string value,
            string viewId,
            string viewName,
            string conditionValue = null)
        {
            var rule = new K2Rule
            {
                FriendlyName = $"Set {targetControlName} value",
                Location = "View",
                Event = new K2Event
                {
                    EventType = K2EventType.ViewControlEvent,
                    Name = "OnChange",
                    SourceId = triggerControlId,
                    SourceType = "Control",
                    SourceName = triggerControlName,
                    SourceDisplayName = triggerControlName,
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

            // Add condition if specified
            if (!string.IsNullOrEmpty(conditionValue))
            {
                handler.Conditions.Add(new K2Condition
                {
                    ConditionType = K2ConditionType.SimpleEqualControlCondition,
                    ControlId = triggerControlId,
                    ControlName = triggerControlName,
                    ControlDisplayName = triggerControlName,
                    CompareValue = conditionValue,
                    DataType = "Text"
                });
            }

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
                        SourceType = K2MappingSourceType.Value,
                        SourceValue = value,
                        TargetType = K2MappingTargetType.ControlProperty,
                        TargetId = "Value",
                        TargetName = targetControlName
                    }
                }
            };

            handler.Actions.Add(action);
            rule.Handlers.Add(handler);

            return rule;
        }

        #endregion
    }
}
