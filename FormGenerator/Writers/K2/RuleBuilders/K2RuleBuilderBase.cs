using System;
using System.Xml;
using FormGenerator.Core.Models;
using FormGenerator.Services;
using K2SmartObjectGenerator.Utilities;

namespace FormGenerator.Writers.K2.RuleBuilders
{
    /// <summary>
    /// Shared XML element creation methods for rule builders.
    /// All patterns matched against K2 Designer reference XML exports.
    /// </summary>
    public static class K2RuleBuilderBase
    {
        /// <summary>
        /// Create an Event element (top-level rule container).
        /// Reference pattern from all 4 reference XMLs:
        ///   Event Type="User" SourceID SourceType SourceName SourceDisplayName [IsExtended="True"]
        ///   Properties: ViewID (with NameValue+DisplayValue), RuleFriendlyName, Location (= view name)
        /// </summary>
        public static XmlElement CreateEventElement(XmlDocument doc, string eventName,
            string sourceId, string sourceType, string sourceName,
            string viewGuid, string viewName, string friendlyName)
        {
            var eventEl = doc.CreateElement("Event");
            eventEl.SetAttribute("ID", Guid.NewGuid().ToString());
            eventEl.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            eventEl.SetAttribute("Type", "User");
            eventEl.SetAttribute("SourceID", sourceId);
            eventEl.SetAttribute("SourceType", sourceType);
            eventEl.SetAttribute("SourceName", sourceName);
            eventEl.SetAttribute("SourceDisplayName", sourceName);
            eventEl.SetAttribute("IsExtended", "True");

            XmlHelper.AddElement(doc, eventEl, "Name", eventName);

            var props = doc.CreateElement("Properties");

            // ViewID property - K2 Designer order: Name, DisplayValue, NameValue, Value
            var viewIdProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, viewIdProp, "Name", "ViewID");
            XmlHelper.AddElement(doc, viewIdProp, "DisplayValue", viewName);
            XmlHelper.AddElement(doc, viewIdProp, "NameValue", viewName);
            XmlHelper.AddElement(doc, viewIdProp, "Value", viewGuid);
            props.AppendChild(viewIdProp);

            // RuleFriendlyName
            if (!string.IsNullOrEmpty(friendlyName))
            {
                var ruleNameProp = doc.CreateElement("Property");
                XmlHelper.AddElement(doc, ruleNameProp, "Name", "RuleFriendlyName");
                XmlHelper.AddElement(doc, ruleNameProp, "Value", friendlyName);
                props.AppendChild(ruleNameProp);
            }

            // Location = view name (reference: "forma", "formb", etc.)
            var locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", viewName);
            props.AppendChild(locationProp);

            eventEl.AppendChild(props);

            return eventEl;
        }

        /// <summary>
        /// Create a Handler element (If/Else block).
        /// Reference: all handlers have HandlerName + Location properties.
        /// </summary>
        public static XmlElement CreateHandlerElement(XmlDocument doc, string handlerName = "IfLogicalHandler")
        {
            var handler = doc.CreateElement("Handler");
            handler.SetAttribute("ID", Guid.NewGuid().ToString());
            handler.SetAttribute("DefinitionID", Guid.NewGuid().ToString());

            var props = doc.CreateElement("Properties");

            var nameProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, nameProp, "Name", "HandlerName");
            XmlHelper.AddElement(doc, nameProp, "Value", handlerName);
            props.AppendChild(nameProp);

            // Location property - K2 Designer uses lowercase "view" for handlers
            var locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", "view");
            props.AppendChild(locationProp);

            handler.AppendChild(props);

            return handler;
        }

        /// <summary>
        /// Create a Transfer action for visibility (show/hide a control).
        /// Reference: checkbox-visibility.xml - ExecutionType="Synchronous", SourceValue as child element.
        /// </summary>
        public static XmlElement CreateVisibilityTransferAction(XmlDocument doc,
            string targetControlId, string targetControlName, bool makeVisible,
            string viewGuid, string viewName)
        {
            var action = doc.CreateElement("Action");
            action.SetAttribute("ID", Guid.NewGuid().ToString());
            action.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            action.SetAttribute("Type", "Transfer");
            action.SetAttribute("ExecutionType", "Synchronous");

            var props = doc.CreateElement("Properties");

            var locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", "View");
            props.AppendChild(locationProp);

            var controlProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, controlProp, "Name", "ControlID");
            XmlHelper.AddElement(doc, controlProp, "DisplayValue", targetControlName);
            XmlHelper.AddElement(doc, controlProp, "NameValue", targetControlName);
            XmlHelper.AddElement(doc, controlProp, "Value", targetControlId);
            props.AppendChild(controlProp);

            var viewProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, viewProp, "Name", "ViewID");
            XmlHelper.AddElement(doc, viewProp, "DisplayValue", viewName);
            XmlHelper.AddElement(doc, viewProp, "NameValue", viewName);
            XmlHelper.AddElement(doc, viewProp, "Value", viewGuid);
            props.AppendChild(viewProp);

            action.AppendChild(props);

            // Parameters with SourceValue child element
            var parameters = doc.CreateElement("Parameters");
            var parameter = doc.CreateElement("Parameter");
            parameter.SetAttribute("SourceType", "Value");
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
        /// Create a Transfer action for control-to-control data transfer.
        /// Reference: data-transfer.xml - Parameter with SourceID/SourceName/SourceDisplayName/SourceType
        /// and TargetID/TargetName/TargetDisplayName/TargetType="Control".
        /// </summary>
        public static XmlElement CreateControlToControlTransferAction(XmlDocument doc,
            string sourceControlId, string sourceControlName,
            string targetControlId, string targetControlName,
            string viewGuid, string viewName)
        {
            var action = doc.CreateElement("Action");
            action.SetAttribute("ID", Guid.NewGuid().ToString());
            action.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            action.SetAttribute("Type", "Transfer");
            action.SetAttribute("ExecutionType", "Synchronous");

            var props = doc.CreateElement("Properties");

            var locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", "View");
            props.AppendChild(locationProp);

            var viewProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, viewProp, "Name", "ViewID");
            XmlHelper.AddElement(doc, viewProp, "DisplayValue", viewName);
            XmlHelper.AddElement(doc, viewProp, "NameValue", viewName);
            XmlHelper.AddElement(doc, viewProp, "Value", viewGuid);
            props.AppendChild(viewProp);

            action.AppendChild(props);

            // Parameter: source and target are both controls (control-to-control transfer)
            var parameters = doc.CreateElement("Parameters");
            var parameter = doc.CreateElement("Parameter");
            parameter.SetAttribute("SourceID", sourceControlId);
            parameter.SetAttribute("SourceName", sourceControlName);
            parameter.SetAttribute("SourceDisplayName", sourceControlName);
            parameter.SetAttribute("SourceType", "Control");
            parameter.SetAttribute("TargetID", targetControlId);
            parameter.SetAttribute("TargetName", targetControlName);
            parameter.SetAttribute("TargetDisplayName", targetControlName);
            parameter.SetAttribute("TargetType", "Control");

            parameters.AppendChild(parameter);
            action.AppendChild(parameters);

            return action;
        }

        /// <summary>
        /// Create a Transfer action for value-to-control data transfer (literal or expression).
        /// </summary>
        public static XmlElement CreateDataTransferAction(XmlDocument doc,
            string sourceType, string sourceId, string sourceName, string sourceValue,
            string targetControlId, string targetControlName, string targetProperty,
            string viewGuid, string viewName)
        {
            var action = doc.CreateElement("Action");
            action.SetAttribute("ID", Guid.NewGuid().ToString());
            action.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            action.SetAttribute("Type", "Transfer");
            action.SetAttribute("ExecutionType", "Synchronous");

            var props = doc.CreateElement("Properties");

            var locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", "View");
            props.AppendChild(locationProp);

            var viewProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, viewProp, "Name", "ViewID");
            XmlHelper.AddElement(doc, viewProp, "DisplayValue", viewName);
            XmlHelper.AddElement(doc, viewProp, "NameValue", viewName);
            XmlHelper.AddElement(doc, viewProp, "Value", viewGuid);
            props.AppendChild(viewProp);

            action.AppendChild(props);

            // Parameters
            var parameters = doc.CreateElement("Parameters");
            var parameter = doc.CreateElement("Parameter");

            // K2 Designer pattern: Value-type transfers use SourceID="Sources"
            if (!string.IsNullOrEmpty(sourceId))
                parameter.SetAttribute("SourceID", sourceId);
            else if (sourceType == "Value")
                parameter.SetAttribute("SourceID", "Sources");

            parameter.SetAttribute("SourceType", sourceType);

            if (!string.IsNullOrEmpty(sourceName))
            {
                parameter.SetAttribute("SourceName", sourceName);
                parameter.SetAttribute("SourceDisplayName", sourceName);
            }

            parameter.SetAttribute("TargetID", targetControlId);
            parameter.SetAttribute("TargetName", targetControlName);
            parameter.SetAttribute("TargetDisplayName", targetControlName);
            parameter.SetAttribute("TargetType", targetProperty ?? "Control");

            // SourceValue as child element with xml:space="preserve"
            if (sourceType == "Value" && !string.IsNullOrEmpty(sourceValue))
            {
                var sv = doc.CreateElement("SourceValue");
                sv.SetAttribute("xml:space", "preserve");
                sv.InnerText = sourceValue;
                parameter.AppendChild(sv);
            }

            parameters.AppendChild(parameter);
            action.AppendChild(parameters);

            return action;
        }

        /// <summary>
        /// Create a ShowMessage action for validation.
        /// Reference: required-validation.xml - Type="ShowMessage" with nested Source elements.
        /// </summary>
        public static XmlElement CreateShowMessageAction(XmlDocument doc,
            string title, string heading, string body)
        {
            var action = doc.CreateElement("Action");
            action.SetAttribute("ID", Guid.NewGuid().ToString());
            action.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            action.SetAttribute("Type", "ShowMessage");
            action.SetAttribute("ExecutionType", "Synchronous");

            var props = doc.CreateElement("Properties");

            var locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", "View");
            props.AppendChild(locationProp);

            var msgLocationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, msgLocationProp, "Name", "MessageLocation");
            XmlHelper.AddElement(doc, msgLocationProp, "Value", "Popup");
            props.AppendChild(msgLocationProp);

            var headingLiteralProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, headingLiteralProp, "Name", "HeadingIsLiteral");
            XmlHelper.AddElement(doc, headingLiteralProp, "Value", "False");
            props.AppendChild(headingLiteralProp);

            var bodyLiteralProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, bodyLiteralProp, "Name", "BodyIsLiteral");
            XmlHelper.AddElement(doc, bodyLiteralProp, "Value", "False");
            props.AppendChild(bodyLiteralProp);

            action.AppendChild(props);

            // Parameters with nested Source elements inside SourceValue
            var parameters = doc.CreateElement("Parameters");

            AddShowMessageParameter(doc, parameters, "Size", "small");
            AddShowMessageParameter(doc, parameters, "Type", "message");
            AddShowMessageParameter(doc, parameters, "Title", title ?? "Validation");
            AddShowMessageParameter(doc, parameters, "Heading", heading ?? "Validation Error");
            AddShowMessageParameter(doc, parameters, "Body", body ?? "Please correct the errors.");

            action.AppendChild(parameters);

            return action;
        }

        private static void AddShowMessageParameter(XmlDocument doc, XmlElement parameters,
            string targetId, string value)
        {
            var parameter = doc.CreateElement("Parameter");
            parameter.SetAttribute("SourceID", "Sources");
            parameter.SetAttribute("SourceType", "Value");
            parameter.SetAttribute("TargetID", targetId);
            parameter.SetAttribute("TargetName", targetId);
            parameter.SetAttribute("TargetType", "MessageProperty");

            var sourceValue = doc.CreateElement("SourceValue");
            sourceValue.SetAttribute("xml:space", "preserve");

            var source = doc.CreateElement("Source");
            source.SetAttribute("SourceType", "Value");
            source.InnerText = value;
            sourceValue.AppendChild(source);

            parameter.AppendChild(sourceValue);
            parameters.AppendChild(parameter);
        }

        /// <summary>
        /// Resolve a field name to a K2 control via the context's resolver.
        /// Returns null if unresolvable.
        /// </summary>
        public static ResolvedControl ResolveControl(K2RuleContext context, string fieldName)
        {
            return context?.ControlResolver?.Resolve(fieldName);
        }
    }
}
