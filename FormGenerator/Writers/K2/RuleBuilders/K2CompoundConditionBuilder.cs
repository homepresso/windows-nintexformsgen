using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Xml;
using K2SmartObjectGenerator.Utilities;

namespace FormGenerator.Writers.K2.RuleBuilders
{
    /// <summary>
    /// Helper for building K2 condition XML elements with AND/OR compound logic.
    /// Used by other rule builders to construct condition blocks.
    /// All patterns matched against K2 Designer reference XML exports.
    /// </summary>
    public static class K2CompoundConditionBuilder
    {
        /// <summary>
        /// Build a simple "equals value" condition element.
        /// Reference: checkbox-visibility.xml - SimpleEqualControlCondition with Equals expression.
        /// </summary>
        public static XmlElement BuildEqualsCondition(XmlDocument doc, string controlId,
            string controlName, string compareValue, string dataType = "Text")
        {
            var condition = CreateConditionShell(doc, "SimpleEqualControlCondition");

            var expressions = doc.CreateElement("Expressions");
            var equals = doc.CreateElement("Equals");

            var leftItem = doc.CreateElement("Item");
            leftItem.SetAttribute("SourceType", "Control");
            leftItem.SetAttribute("SourceID", controlId);
            leftItem.SetAttribute("SourceName", controlName);
            leftItem.SetAttribute("SourceDisplayName", controlName);
            leftItem.SetAttribute("DataType", dataType);
            equals.AppendChild(leftItem);

            var rightItem = doc.CreateElement("Item");
            rightItem.SetAttribute("SourceType", "Value");
            rightItem.SetAttribute("DataType", dataType);
            rightItem.InnerText = compareValue ?? "";
            equals.AppendChild(rightItem);

            expressions.AppendChild(equals);
            condition.AppendChild(expressions);

            return condition;
        }

        /// <summary>
        /// Build a simple "not equals value" condition element
        /// </summary>
        public static XmlElement BuildNotEqualsCondition(XmlDocument doc, string controlId,
            string controlName, string compareValue, string dataType = "Text")
        {
            var condition = CreateConditionShell(doc, "SimpleNotEqualControlCondition");

            var expressions = doc.CreateElement("Expressions");
            var notEquals = doc.CreateElement("NotEquals");

            var leftItem = doc.CreateElement("Item");
            leftItem.SetAttribute("SourceType", "Control");
            leftItem.SetAttribute("SourceID", controlId);
            leftItem.SetAttribute("SourceName", controlName);
            leftItem.SetAttribute("SourceDisplayName", controlName);
            leftItem.SetAttribute("DataType", dataType);
            notEquals.AppendChild(leftItem);

            var rightItem = doc.CreateElement("Item");
            rightItem.SetAttribute("SourceType", "Value");
            rightItem.SetAttribute("DataType", dataType);
            rightItem.InnerText = compareValue ?? "";
            notEquals.AppendChild(rightItem);

            expressions.AppendChild(notEquals);
            condition.AppendChild(expressions);

            return condition;
        }

        /// <summary>
        /// Build a "is blank" condition element.
        /// Reference: required-validation.xml - SimpleBlankControlCondition with IsBlank expression.
        /// </summary>
        public static XmlElement BuildIsBlankCondition(XmlDocument doc, string controlId,
            string controlName, string dataType = "Text")
        {
            var condition = CreateConditionShell(doc, "SimpleBlankControlCondition");

            var expressions = doc.CreateElement("Expressions");
            var isBlank = doc.CreateElement("IsBlank");

            var item = doc.CreateElement("Item");
            item.SetAttribute("SourceType", "Control");
            item.SetAttribute("SourceID", controlId);
            item.SetAttribute("SourceName", controlName);
            item.SetAttribute("SourceDisplayName", controlName);
            item.SetAttribute("DataType", dataType);
            isBlank.AppendChild(item);

            expressions.AppendChild(isBlank);
            condition.AppendChild(expressions);
            return condition;
        }

        /// <summary>
        /// Build a "is empty/blank" condition element (legacy pattern).
        /// </summary>
        public static XmlElement BuildIsEmptyCondition(XmlDocument doc, string controlId,
            string controlName, string dataType = "Text")
        {
            // Use the IsBlank pattern which matches K2 Designer output
            return BuildIsBlankCondition(doc, controlId, controlName, dataType);
        }

        /// <summary>
        /// Build a "is not empty/not blank" condition element
        /// </summary>
        public static XmlElement BuildIsNotEmptyCondition(XmlDocument doc, string controlId,
            string controlName, string dataType = "Text")
        {
            var condition = CreateConditionShell(doc, "SimpleNotEmptyCondition");

            var expressions = doc.CreateElement("Expressions");
            var item = doc.CreateElement("Item");
            item.SetAttribute("SourceType", "Control");
            item.SetAttribute("SourceID", controlId);
            item.SetAttribute("SourceName", controlName);
            item.SetAttribute("SourceDisplayName", controlName);
            item.SetAttribute("DataType", dataType);
            expressions.AppendChild(item);

            condition.AppendChild(expressions);
            return condition;
        }

        /// <summary>
        /// Parse an InfoPath condition string and build the appropriate K2 condition element.
        /// Supports: equals, not-equals, blank, not-blank comparisons.
        /// </summary>
        public static XmlElement BuildConditionFromInfoPath(XmlDocument doc, string infoPathCondition,
            string controlId, string controlName, string dataType = "Text")
        {
            if (string.IsNullOrEmpty(infoPathCondition))
                return null;

            var normalized = NormalizeXPath(infoPathCondition);

            // Check for blank/empty conditions
            if (normalized.Contains("xd:isBlank") || IsSimpleBlankCheck(normalized))
            {
                return BuildIsBlankCondition(doc, controlId, controlName, dataType);
            }

            if (normalized.Contains("not(xd:isBlank") || IsSimpleNotBlankCheck(normalized))
            {
                return BuildIsNotEmptyCondition(doc, controlId, controlName, dataType);
            }

            // Check for not-equal (must check before equal since != contains =)
            var notEqualMatch = Regex.Match(normalized, @"!=\s*[""']([^""']*)[""']");
            if (notEqualMatch.Success)
            {
                return BuildNotEqualsCondition(doc, controlId, controlName, notEqualMatch.Groups[1].Value, dataType);
            }

            // Check for equality
            var equalMatch = Regex.Match(normalized, @"=\s*[""']([^""']*)[""']");
            if (equalMatch.Success)
            {
                return BuildEqualsCondition(doc, controlId, controlName, equalMatch.Groups[1].Value, dataType);
            }

            // Check for boolean true/false
            var boolMatch = Regex.Match(normalized, @"=\s*(true|false)", RegexOptions.IgnoreCase);
            if (boolMatch.Success)
            {
                return BuildEqualsCondition(doc, controlId, controlName, boolMatch.Groups[1].Value, dataType);
            }

            // Check for numeric equality
            var numMatch = Regex.Match(normalized, @"=\s*(\d+(?:\.\d+)?)");
            if (numMatch.Success)
            {
                return BuildEqualsCondition(doc, controlId, controlName, numMatch.Groups[1].Value, dataType);
            }

            // Fallback: treat as not-empty condition
            return BuildIsNotEmptyCondition(doc, controlId, controlName, dataType);
        }

        /// <summary>
        /// Build compound AND conditions: returns a list of Condition elements.
        /// Reference: compound-condition.xml - AND logic = multiple Condition elements
        /// in the same Conditions block (no Composite wrapper needed).
        /// </summary>
        public static List<XmlElement> BuildAndConditions(XmlDocument doc,
            List<XmlElement> subConditions)
        {
            // K2 AND logic: just put multiple Condition elements in Conditions block
            return subConditions ?? new List<XmlElement>();
        }

        /// <summary>
        /// Legacy compound builder - wraps in Composite for OR logic only.
        /// For AND logic, use BuildAndConditions instead.
        /// </summary>
        public static XmlElement BuildCompoundCondition(XmlDocument doc,
            List<XmlElement> subConditions, string logic = "And")
        {
            if (subConditions == null || subConditions.Count == 0)
                return null;

            if (subConditions.Count == 1)
                return subConditions[0];

            // For AND: K2 uses multiple Condition elements, not Composite
            // Return first condition; caller should add all conditions to Conditions block
            if (string.Equals(logic, "And", StringComparison.OrdinalIgnoreCase))
                return subConditions[0];

            // For OR: use Composite element
            var condition = CreateConditionShell(doc, "AdvancedCondition");

            var expressions = doc.CreateElement("Expressions");
            var composite = doc.CreateElement("Composite");
            composite.SetAttribute("Logic", logic);

            foreach (var sub in subConditions)
            {
                var subExprs = sub.SelectSingleNode("Expressions");
                if (subExprs != null)
                {
                    foreach (XmlNode child in subExprs.ChildNodes)
                    {
                        composite.AppendChild(doc.ImportNode(child, true));
                    }
                }
            }

            expressions.AppendChild(composite);
            condition.AppendChild(expressions);

            return condition;
        }

        private static XmlElement CreateConditionShell(XmlDocument doc, string conditionName)
        {
            var condition = doc.CreateElement("Condition");
            condition.SetAttribute("ID", Guid.NewGuid().ToString());
            condition.SetAttribute("DefinitionID", Guid.NewGuid().ToString());

            var props = doc.CreateElement("Properties");

            // Location property - matches reference XML
            var locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", "View");
            props.AppendChild(locationProp);

            // Name property
            var nameProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, nameProp, "Name", "Name");
            XmlHelper.AddElement(doc, nameProp, "Value", conditionName);
            props.AppendChild(nameProp);

            condition.AppendChild(props);

            return condition;
        }

        private static string NormalizeXPath(string xpath)
        {
            if (string.IsNullOrEmpty(xpath)) return "";
            var result = Regex.Replace(xpath, @"/my:", "/");
            result = Regex.Replace(result, @"my:", "");
            return result.Trim();
        }

        private static bool IsSimpleBlankCheck(string normalized)
        {
            // Matches patterns like: field = "" or field = ''
            return Regex.IsMatch(normalized, @"=\s*[""'][""']") && !normalized.Contains("!=");
        }

        private static bool IsSimpleNotBlankCheck(string normalized)
        {
            return Regex.IsMatch(normalized, @"!=\s*[""'][""']");
        }
    }
}
