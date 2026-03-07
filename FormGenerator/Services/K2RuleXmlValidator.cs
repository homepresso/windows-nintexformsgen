using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;

namespace FormGenerator.Services
{
    /// <summary>
    /// Structural comparison of generated K2 rule XML vs reference XML.
    /// Ignores GUIDs, compares element names, attribute names, nesting, and value patterns.
    /// </summary>
    public class K2RuleXmlValidator
    {
        /// <summary>
        /// Compare generated XML against reference XML structurally.
        /// Returns a list of differences found.
        /// </summary>
        public List<string> CompareStructure(string generatedXml, string referenceXml)
        {
            var differences = new List<string>();

            try
            {
                var genDoc = new XmlDocument();
                genDoc.LoadXml(generatedXml);

                var refDoc = new XmlDocument();
                refDoc.LoadXml(referenceXml);

                CompareElements(genDoc.DocumentElement, refDoc.DocumentElement, "", differences);
            }
            catch (Exception ex)
            {
                differences.Add($"XML parsing error: {ex.Message}");
            }

            return differences;
        }

        /// <summary>
        /// Validate that a generated Event element has the required K2 structure.
        /// </summary>
        public List<string> ValidateEventStructure(XmlElement eventElement)
        {
            var issues = new List<string>();

            if (eventElement == null)
            {
                issues.Add("Event element is null");
                return issues;
            }

            // Check required attributes
            CheckAttribute(eventElement, "ID", issues);
            CheckAttribute(eventElement, "DefinitionID", issues);
            CheckAttribute(eventElement, "Type", issues);
            CheckAttribute(eventElement, "SourceID", issues);
            CheckAttribute(eventElement, "SourceType", issues);

            // Check required child elements
            var name = eventElement.SelectSingleNode("Name");
            if (name == null)
                issues.Add("Missing <Name> element");

            var properties = eventElement.SelectSingleNode("Properties");
            if (properties == null)
                issues.Add("Missing <Properties> element");
            else
                ValidateProperties(properties, issues);

            var handlers = eventElement.SelectSingleNode("Handlers");
            if (handlers == null)
            {
                issues.Add("Missing <Handlers> element");
            }
            else
            {
                foreach (XmlElement handler in handlers.ChildNodes.OfType<XmlElement>())
                {
                    ValidateHandler(handler, issues);
                }
            }

            return issues;
        }

        private void ValidateProperties(XmlNode properties, List<string> issues)
        {
            bool hasViewId = false;
            bool hasLocation = false;

            foreach (XmlElement prop in properties.ChildNodes.OfType<XmlElement>())
            {
                var nameEl = prop.SelectSingleNode("Name");
                if (nameEl == null)
                {
                    issues.Add("Property element missing <Name>");
                    continue;
                }

                if (nameEl.InnerText == "ViewID") hasViewId = true;
                if (nameEl.InnerText == "Location") hasLocation = true;
            }

            if (!hasViewId) issues.Add("Properties missing ViewID");
            if (!hasLocation) issues.Add("Properties missing Location");
        }

        private void ValidateHandler(XmlElement handler, List<string> issues)
        {
            CheckAttribute(handler, "ID", issues);
            CheckAttribute(handler, "DefinitionID", issues);

            var handlerProps = handler.SelectSingleNode("Properties");
            if (handlerProps == null)
                issues.Add("Handler missing <Properties>");

            var actions = handler.SelectSingleNode("Actions");
            if (actions == null)
                issues.Add("Handler missing <Actions>");
            else
            {
                foreach (XmlElement action in actions.ChildNodes.OfType<XmlElement>())
                {
                    ValidateAction(action, issues);
                }
            }
        }

        private void ValidateAction(XmlElement action, List<string> issues)
        {
            CheckAttribute(action, "ID", issues);
            CheckAttribute(action, "DefinitionID", issues);
            CheckAttribute(action, "Type", issues);
            CheckAttribute(action, "ExecutionType", issues);

            var props = action.SelectSingleNode("Properties");
            if (props == null)
                issues.Add($"Action missing <Properties>");

            // If this is a Transfer action with Parameters, validate SourceValue structure
            var parameters = action.SelectSingleNode("Parameters");
            if (parameters != null)
            {
                foreach (XmlElement param in parameters.ChildNodes.OfType<XmlElement>())
                {
                    ValidateParameter(param, issues);
                }
            }
        }

        private void ValidateParameter(XmlElement param, List<string> issues)
        {
            var sourceType = param.GetAttribute("SourceType");

            if (sourceType == "Value")
            {
                // SourceValue should be a child element, not an attribute
                if (param.HasAttribute("SourceValue"))
                {
                    issues.Add("Parameter has SourceValue as attribute (should be child element)");
                }

                var sourceValueEl = param.SelectSingleNode("SourceValue");
                if (sourceValueEl == null)
                {
                    issues.Add("Parameter with SourceType='Value' missing <SourceValue> child element");
                }
                else
                {
                    var xmlSpace = ((XmlElement)sourceValueEl).GetAttribute("xml:space");
                    if (xmlSpace != "preserve")
                    {
                        issues.Add("SourceValue element missing xml:space='preserve'");
                    }
                }
            }
        }

        private void CompareElements(XmlElement generated, XmlElement reference, string path, List<string> differences)
        {
            var currentPath = string.IsNullOrEmpty(path) ? generated.Name : $"{path}/{generated.Name}";

            // Compare element name
            if (generated.Name != reference.Name)
            {
                differences.Add($"Element name mismatch at {path}: generated='{generated.Name}', reference='{reference.Name}'");
                return;
            }

            // Compare attribute names (ignoring GUID values)
            var genAttrs = new HashSet<string>(generated.Attributes.Cast<XmlAttribute>().Select(a => a.Name));
            var refAttrs = new HashSet<string>(reference.Attributes.Cast<XmlAttribute>().Select(a => a.Name));

            foreach (var attr in refAttrs.Except(genAttrs))
            {
                differences.Add($"Missing attribute '{attr}' at {currentPath}");
            }

            foreach (var attr in genAttrs.Except(refAttrs))
            {
                differences.Add($"Extra attribute '{attr}' at {currentPath}");
            }

            // Compare non-GUID attribute values
            foreach (var attr in genAttrs.Intersect(refAttrs))
            {
                var genVal = generated.GetAttribute(attr);
                var refVal = reference.GetAttribute(attr);

                // Skip GUID comparisons
                if (IsGuid(genVal) && IsGuid(refVal)) continue;

                if (genVal != refVal)
                {
                    differences.Add($"Attribute value mismatch at {currentPath}/@{attr}: generated='{genVal}', reference='{refVal}'");
                }
            }

            // Compare child element count and structure
            var genChildren = generated.ChildNodes.OfType<XmlElement>().ToList();
            var refChildren = reference.ChildNodes.OfType<XmlElement>().ToList();

            if (genChildren.Count != refChildren.Count)
            {
                differences.Add($"Child count mismatch at {currentPath}: generated={genChildren.Count}, reference={refChildren.Count}");
            }

            // Compare matching children
            int minCount = Math.Min(genChildren.Count, refChildren.Count);
            for (int i = 0; i < minCount; i++)
            {
                CompareElements(genChildren[i], refChildren[i], currentPath, differences);
            }
        }

        private void CheckAttribute(XmlElement element, string attrName, List<string> issues)
        {
            if (!element.HasAttribute(attrName))
            {
                issues.Add($"<{element.Name}> missing required attribute '{attrName}'");
            }
        }

        private bool IsGuid(string value)
        {
            return Guid.TryParse(value, out _);
        }
    }
}
