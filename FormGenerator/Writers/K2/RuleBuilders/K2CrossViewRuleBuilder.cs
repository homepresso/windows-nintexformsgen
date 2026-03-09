using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using FormGenerator.Analyzers.Infopath;
using K2SmartObjectGenerator.Utilities;

namespace FormGenerator.Writers.K2.RuleBuilders
{
    /// <summary>
    /// Builds cross-view rule Event elements for the form-level Events section.
    /// Cross-view rules reference controls across different views using InstanceID attributes.
    ///
    /// Reference: formG.xml (Form G)
    ///
    /// Key differences from view-level rules:
    ///   - Events live in States/State/Events at the FORM level
    ///   - Handler Location = "form" (lowercase)
    ///   - Condition/Action Location = "Form" (uppercase)
    ///   - InstanceID attributes on Event/Condition/Action/Parameter reference AreaItem IDs
    ///   - View show/hide: TargetID="display", TargetType="ViewProperty"
    ///   - Control visibility: TargetID="isvisible", TargetType="ControlProperty"
    /// </summary>
    public static class K2CrossViewRuleBuilder
    {
        /// <summary>
        /// View instance information extracted from the form XML.
        /// Maps between view names, view GUIDs, and AreaItem IDs.
        /// </summary>
        public class ViewInstanceInfo
        {
            public string ViewName { get; set; }
            public string ViewGuid { get; set; }
            public string AreaItemId { get; set; }
            public string AreaItemName { get; set; }
        }

        /// <summary>
        /// Extract view instance information from the form XML.
        /// Scans Panel/Areas/Area/Items/Item elements for ViewID and ID attributes.
        /// </summary>
        public static List<ViewInstanceInfo> ExtractViewInstances(XmlDocument formDoc)
        {
            var instances = new List<ViewInstanceInfo>();
            var items = formDoc.SelectNodes("//Panel/Areas/Area/Items/Item[@ViewID]");
            if (items == null) return instances;

            foreach (XmlElement item in items)
            {
                var viewGuid = item.GetAttribute("ViewID");
                var areaItemId = item.GetAttribute("ID");
                var nameNode = item.SelectSingleNode("Name") as XmlElement;
                var viewName = nameNode?.InnerText ?? "";

                if (!string.IsNullOrEmpty(viewGuid) && !string.IsNullOrEmpty(areaItemId))
                {
                    instances.Add(new ViewInstanceInfo
                    {
                        ViewName = viewName,
                        ViewGuid = viewGuid,
                        AreaItemId = areaItemId,
                        AreaItemName = viewName
                    });
                }
            }

            return instances;
        }

        /// <summary>
        /// Find a control's K2 ID and which view it belongs to by searching ControlMappingService
        /// across all registered views.
        /// </summary>
        public static (string controlId, string controlName, string viewName) FindControlAcrossViews(
            string fieldName, List<ViewInstanceInfo> viewInstances)
        {
            if (string.IsNullOrEmpty(fieldName)) return (null, null, null);

            // Strip XPath: /my:myFields/my:field1 -> field1
            var stripped = fieldName;
            if (stripped.Contains("/"))
            {
                var segments = stripped.Split('/');
                stripped = segments[segments.Length - 1];
            }
            stripped = stripped.Replace("my:", "");

            var registeredViews = ControlMappingService.GetMappedViewNames();
            foreach (var viewName in registeredViews)
            {
                var controls = ControlMappingService.GetViewControls(viewName);
                if (controls == null) continue;

                // Try direct match
                if (controls.TryGetValue(stripped, out var mapping))
                {
                    return (mapping.ControlId, mapping.ControlName, viewName);
                }

                // Try case-insensitive match
                var match = controls.FirstOrDefault(c =>
                    string.Equals(c.Key, stripped, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.Value.ControlName, stripped, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.Value.FieldName, stripped, StringComparison.OrdinalIgnoreCase));

                if (match.Value != null)
                {
                    return (match.Value.ControlId, match.Value.ControlName, viewName);
                }
            }

            return (null, null, null);
        }

        /// <summary>
        /// Create a cross-view visibility Event element.
        /// Used when a control on one view shows/hides a control on a different view,
        /// or shows/hides an entire view.
        /// </summary>
        public static XmlElement CreateCrossViewVisibilityEvent(XmlDocument doc,
            string sourceControlId, string sourceControlName,
            ViewInstanceInfo sourceViewInstance,
            ViewInstanceInfo targetViewInstance,
            string conditionValue,
            string targetControlId, string targetControlName,
            bool isViewVisibility)
        {
            // Create Event element with cross-view attributes
            var eventEl = doc.CreateElement("Event");
            eventEl.SetAttribute("ID", Guid.NewGuid().ToString());
            eventEl.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            eventEl.SetAttribute("Type", "User");
            eventEl.SetAttribute("SourceID", sourceControlId);
            eventEl.SetAttribute("SourceType", "Control");
            eventEl.SetAttribute("SourceName", sourceControlName);
            eventEl.SetAttribute("SourceDisplayName", sourceControlName);
            eventEl.SetAttribute("IsExtended", "True");
            eventEl.SetAttribute("InstanceID", sourceViewInstance.AreaItemId);
            eventEl.SetAttribute("ValidationStatus", "Auto");
            eventEl.SetAttribute("ValidationMessages",
                $"EventSource,Control,Auto,{sourceControlId},{sourceControlName},{sourceControlName}");

            XmlHelper.AddElement(doc, eventEl, "Name", "OnChange");

            // Properties
            var props = doc.CreateElement("Properties");

            var viewIdProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, viewIdProp, "Name", "ViewID");
            XmlHelper.AddElement(doc, viewIdProp, "DisplayValue", sourceViewInstance.ViewName);
            XmlHelper.AddElement(doc, viewIdProp, "NameValue", sourceViewInstance.ViewName);
            XmlHelper.AddElement(doc, viewIdProp, "Value", sourceViewInstance.ViewGuid);
            props.AppendChild(viewIdProp);

            var ruleFriendlyProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, ruleFriendlyProp, "Name", "RuleFriendlyName");
            XmlHelper.AddElement(doc, ruleFriendlyProp, "Value",
                $"On {sourceViewInstance.ViewName}, when {sourceControlName} is Changed");
            props.AppendChild(ruleFriendlyProp);

            var locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", sourceViewInstance.ViewName);
            props.AppendChild(locationProp);

            eventEl.AppendChild(props);

            // Handlers section
            var handlers = doc.CreateElement("Handlers");

            // Handler: If condition matches -> show target
            var showHandler = CreateCrossViewHandler(doc, sourceControlId, sourceControlName,
                sourceViewInstance, targetViewInstance, conditionValue,
                targetControlId, targetControlName, isViewVisibility, true);
            handlers.AppendChild(showHandler);

            // Handler: Else -> hide target
            var hideHandler = CreateCrossViewHandler(doc, sourceControlId, sourceControlName,
                sourceViewInstance, targetViewInstance, conditionValue,
                targetControlId, targetControlName, isViewVisibility, false);
            handlers.AppendChild(hideHandler);

            eventEl.AppendChild(handlers);

            return eventEl;
        }

        private static XmlElement CreateCrossViewHandler(XmlDocument doc,
            string sourceControlId, string sourceControlName,
            ViewInstanceInfo sourceViewInstance,
            ViewInstanceInfo targetViewInstance,
            string conditionValue,
            string targetControlId, string targetControlName,
            bool isViewVisibility, bool showAction)
        {
            var handler = doc.CreateElement("Handler");
            handler.SetAttribute("ID", Guid.NewGuid().ToString());
            handler.SetAttribute("DefinitionID", Guid.NewGuid().ToString());

            // Properties - Location = "form" (lowercase) for cross-view
            var handlerProps = doc.CreateElement("Properties");

            var nameProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, nameProp, "Name", "HandlerName");
            XmlHelper.AddElement(doc, nameProp, "Value", "IfLogicalHandler");
            handlerProps.AppendChild(nameProp);

            var locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", "form"); // lowercase for form-level
            handlerProps.AppendChild(locationProp);

            handler.AppendChild(handlerProps);

            // Conditions - with InstanceID referencing source view AreaItem
            var conditions = doc.CreateElement("Conditions");
            var condition = doc.CreateElement("Condition");
            condition.SetAttribute("ID", Guid.NewGuid().ToString());
            condition.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            condition.SetAttribute("InstanceID", sourceViewInstance.AreaItemId);

            var condProps = doc.CreateElement("Properties");

            var condLocationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, condLocationProp, "Name", "Location");
            XmlHelper.AddElement(doc, condLocationProp, "Value", "Form"); // uppercase for form-level
            condProps.AppendChild(condLocationProp);

            var condNameProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, condNameProp, "Name", "Name");
            XmlHelper.AddElement(doc, condNameProp, "Value", "SimpleEqualControlCondition");
            condProps.AppendChild(condNameProp);

            condition.AppendChild(condProps);

            // Expressions
            var expressions = doc.CreateElement("Expressions");
            var equals = doc.CreateElement("Equals");

            // Left side: control reference with SourceInstanceID
            var leftItem = doc.CreateElement("Item");
            leftItem.SetAttribute("SourceType", "Control");
            leftItem.SetAttribute("SourceInstanceID", sourceViewInstance.AreaItemId);
            leftItem.SetAttribute("SourceID", sourceControlId);
            leftItem.SetAttribute("SourceName", sourceControlName);
            leftItem.SetAttribute("SourceDisplayName", sourceControlName);
            leftItem.SetAttribute("DataType", "Text");
            leftItem.SetAttribute("ValidationStatus", "Auto");
            leftItem.SetAttribute("ValidationMessages",
                $"PropertyExpressionSource,Control,Auto,{sourceControlId},{sourceControlName},{sourceControlName}");
            equals.AppendChild(leftItem);

            // Right side: value
            var rightItem = doc.CreateElement("Item");
            rightItem.SetAttribute("SourceType", "Value");
            rightItem.SetAttribute("DataType", "Text");
            rightItem.InnerText = showAction ? conditionValue : GetOppositeValue(conditionValue);
            equals.AppendChild(rightItem);

            expressions.AppendChild(equals);
            condition.AppendChild(expressions);
            conditions.AppendChild(condition);
            handler.AppendChild(conditions);

            // Actions
            var actions = doc.CreateElement("Actions");

            if (isViewVisibility)
            {
                // Show/hide entire view
                var action = CreateViewVisibilityAction(doc, targetViewInstance,
                    showAction ? "Show" : "Hide");
                actions.AppendChild(action);
            }
            else
            {
                // Show/hide control on target view
                var action = CreateCrossViewControlVisibilityAction(doc, targetViewInstance,
                    targetControlId, targetControlName, showAction);
                actions.AppendChild(action);
            }

            handler.AppendChild(actions);

            return handler;
        }

        /// <summary>
        /// Create a Transfer action that shows/hides an entire view.
        /// Reference: formG.xml - TargetID="display", TargetType="ViewProperty"
        /// </summary>
        private static XmlElement CreateViewVisibilityAction(XmlDocument doc,
            ViewInstanceInfo targetViewInstance, string showOrHide)
        {
            var action = doc.CreateElement("Action");
            action.SetAttribute("ID", Guid.NewGuid().ToString());
            action.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            action.SetAttribute("Type", "Transfer");
            action.SetAttribute("ExecutionType", "Synchronous");
            action.SetAttribute("InstanceID", targetViewInstance.AreaItemId);

            // Properties
            var props = doc.CreateElement("Properties");

            var locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", "Form");
            props.AppendChild(locationProp);

            var viewIdProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, viewIdProp, "Name", "ViewID");
            XmlHelper.AddElement(doc, viewIdProp, "DisplayValue", targetViewInstance.ViewName);
            XmlHelper.AddElement(doc, viewIdProp, "NameValue", targetViewInstance.ViewName);
            XmlHelper.AddElement(doc, viewIdProp, "Value", targetViewInstance.ViewGuid);
            props.AppendChild(viewIdProp);

            action.AppendChild(props);

            // Parameters
            var parameters = doc.CreateElement("Parameters");
            var parameter = doc.CreateElement("Parameter");
            parameter.SetAttribute("SourceType", "Value");
            parameter.SetAttribute("TargetInstanceID", targetViewInstance.AreaItemId);
            parameter.SetAttribute("TargetID", "display");
            parameter.SetAttribute("TargetName", "display");
            parameter.SetAttribute("TargetDisplayName", targetViewInstance.ViewName);
            parameter.SetAttribute("TargetType", "ViewProperty");

            var sourceValue = doc.CreateElement("SourceValue");
            sourceValue.SetAttribute("xml:space", "preserve");
            sourceValue.InnerText = showOrHide;
            parameter.AppendChild(sourceValue);

            parameters.AppendChild(parameter);
            action.AppendChild(parameters);

            return action;
        }

        /// <summary>
        /// Create a Transfer action that shows/hides a control on a different view.
        /// Reference: formG.xml - TargetID="isvisible", TargetType="ControlProperty"
        /// </summary>
        private static XmlElement CreateCrossViewControlVisibilityAction(XmlDocument doc,
            ViewInstanceInfo targetViewInstance,
            string targetControlId, string targetControlName, bool makeVisible)
        {
            var action = doc.CreateElement("Action");
            action.SetAttribute("ID", Guid.NewGuid().ToString());
            action.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            action.SetAttribute("Type", "Transfer");
            action.SetAttribute("ExecutionType", "Synchronous");
            action.SetAttribute("InstanceID", targetViewInstance.AreaItemId);

            // Properties
            var props = doc.CreateElement("Properties");

            var locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", "Form");
            props.AppendChild(locationProp);

            var viewIdProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, viewIdProp, "Name", "ViewID");
            XmlHelper.AddElement(doc, viewIdProp, "DisplayValue", targetViewInstance.ViewName);
            XmlHelper.AddElement(doc, viewIdProp, "NameValue", targetViewInstance.ViewName);
            XmlHelper.AddElement(doc, viewIdProp, "Value", targetViewInstance.ViewGuid);
            props.AppendChild(viewIdProp);

            var controlProp = doc.CreateElement("Property");
            controlProp.SetAttribute("ValidationStatus", "Auto");
            controlProp.SetAttribute("ValidationMessages",
                $"ActionControl,Control,Auto,{targetControlId},{targetControlName},{targetControlName}");
            XmlHelper.AddElement(doc, controlProp, "Name", "ControlID");
            XmlHelper.AddElement(doc, controlProp, "DisplayValue", targetControlName);
            XmlHelper.AddElement(doc, controlProp, "NameValue", targetControlName);
            XmlHelper.AddElement(doc, controlProp, "Value", targetControlId);
            props.AppendChild(controlProp);

            action.AppendChild(props);

            // Parameters
            var parameters = doc.CreateElement("Parameters");
            var parameter = doc.CreateElement("Parameter");
            parameter.SetAttribute("SourceType", "Value");
            parameter.SetAttribute("TargetInstanceID", targetViewInstance.AreaItemId);
            parameter.SetAttribute("TargetID", "isvisible");
            parameter.SetAttribute("TargetDisplayName", targetControlName);
            parameter.SetAttribute("TargetType", "ControlProperty");

            var sourceValue = doc.CreateElement("SourceValue");
            sourceValue.SetAttribute("xml:space", "preserve");
            sourceValue.InnerText = makeVisible.ToString().ToLower();
            parameter.AppendChild(sourceValue);

            parameters.AppendChild(parameter);
            action.AppendChild(parameters);

            return action;
        }

        /// <summary>
        /// Process InfoPath conditional rules and add cross-view events to the form.
        /// Identifies rules where the source and target are on different K2 views.
        /// </summary>
        public static void AddCrossViewRules(XmlDocument formDoc, XmlElement eventsElement,
            InfoPathFormDefinition formDef)
        {
            if (formDef?.ConditionalRules == null) return;

            var viewInstances = ExtractViewInstances(formDoc);
            if (viewInstances.Count < 2)
            {
                // Need at least 2 views for cross-view rules
                return;
            }

            Console.WriteLine($"    Cross-view rule processing: {viewInstances.Count} view instances found");
            foreach (var vi in viewInstances)
            {
                Console.WriteLine($"      View: {vi.ViewName} (GUID: {vi.ViewGuid}, AreaItem: {vi.AreaItemId})");
            }

            var visibilityRules = formDef.ConditionalRules
                .Where(r => r != null &&
                       string.Equals(r.Type, "Visibility", StringComparison.OrdinalIgnoreCase))
                .ToList();

            int added = 0;
            foreach (var rule in visibilityRules)
            {
                try
                {
                    // Find source control across all views
                    var (sourceId, sourceName, sourceView) = FindControlAcrossViews(
                        rule.SourceField, viewInstances);
                    if (sourceId == null) continue;

                    // Find target control across all views
                    var (targetId, targetName, targetView) = FindControlAcrossViews(
                        rule.TargetField, viewInstances);
                    if (targetId == null) continue;

                    // Only process if source and target are on DIFFERENT views
                    if (string.Equals(sourceView, targetView, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Find view instances for source and target
                    var sourceInstance = viewInstances.FirstOrDefault(v =>
                        string.Equals(v.ViewName, sourceView, StringComparison.OrdinalIgnoreCase));
                    var targetInstance = viewInstances.FirstOrDefault(v =>
                        string.Equals(v.ViewName, targetView, StringComparison.OrdinalIgnoreCase));

                    if (sourceInstance == null || targetInstance == null) continue;

                    // Extract condition value
                    string conditionValue = ExtractConditionValue(rule.Condition);

                    // Determine if this is a view visibility or control visibility rule
                    bool isViewVisibility = IsViewVisibilityRule(rule, targetView);

                    var eventEl = CreateCrossViewVisibilityEvent(formDoc,
                        sourceId, sourceName, sourceInstance, targetInstance,
                        conditionValue, targetId, targetName, isViewVisibility);

                    eventsElement.AppendChild(eventEl);
                    added++;
                    Console.WriteLine($"      Added cross-view rule: {sourceName} ({sourceView}) -> {targetName} ({targetView})");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"      WARNING: Failed to create cross-view rule for '{rule.Name}': {ex.Message}");
                }
            }

            if (added > 0)
            {
                Console.WriteLine($"    Cross-view rules added: {added}");
            }
        }

        private static bool IsViewVisibilityRule(ConditionalRule rule, string targetView)
        {
            // If the target field matches a view name, it's a view visibility rule
            if (!string.IsNullOrEmpty(rule.TargetField))
            {
                var stripped = rule.TargetField.Replace("my:", "").Replace("/", "");
                if (stripped.Equals(targetView, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // If there are affected controls that match view names, it's view visibility
            if (rule.AffectedControls != null)
            {
                foreach (var ctrl in rule.AffectedControls)
                {
                    if (ctrl.Equals(targetView, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        private static string ExtractConditionValue(string condition)
        {
            if (string.IsNullOrEmpty(condition)) return "";

            var match = System.Text.RegularExpressions.Regex.Match(condition,
                @"=\s*""([^""]*)""|=\s*'([^']*)'");
            if (match.Success)
            {
                return !string.IsNullOrEmpty(match.Groups[1].Value)
                    ? match.Groups[1].Value
                    : match.Groups[2].Value;
            }

            var numMatch = System.Text.RegularExpressions.Regex.Match(condition, @"=\s*(\d+(?:\.\d+)?)");
            if (numMatch.Success)
            {
                return numMatch.Groups[1].Value;
            }

            return "";
        }

        private static string GetOppositeValue(string conditionValue)
        {
            // For boolean values
            if (conditionValue.Equals("true", StringComparison.OrdinalIgnoreCase))
                return "false";
            if (conditionValue.Equals("false", StringComparison.OrdinalIgnoreCase))
                return "true";

            // For other values, we can't determine the opposite automatically
            // The else handler will use NotEquals instead
            return conditionValue;
        }
    }
}
