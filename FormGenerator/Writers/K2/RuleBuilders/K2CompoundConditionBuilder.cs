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
        /// Build a "is not empty/not blank" condition element.
        /// Reference: K2-Live-Patterns-PurchaseOrders.xml - IsNotBlank expression wraps Item.
        /// </summary>
        public static XmlElement BuildIsNotEmptyCondition(XmlDocument doc, string controlId,
            string controlName, string dataType = "Text")
        {
            var condition = CreateConditionShell(doc, "SimpleNotBlankControlCondition");

            var expressions = doc.CreateElement("Expressions");
            var isNotBlank = doc.CreateElement("IsNotBlank");

            var item = doc.CreateElement("Item");
            item.SetAttribute("SourceType", "Control");
            item.SetAttribute("SourceID", controlId);
            item.SetAttribute("SourceName", controlName);
            item.SetAttribute("SourceDisplayName", controlName);
            item.SetAttribute("DataType", dataType);
            isNotBlank.AppendChild(item);

            expressions.AppendChild(isNotBlank);
            condition.AppendChild(expressions);
            return condition;
        }

        // ================================================================
        // Advanced Condition expression builders
        // Reference: K2 Designer "Advanced Condition" XML output
        // These use AdvancedCondition shell and SourceValue child elements
        // ================================================================

        /// <summary>
        /// Build a "contains" condition element for Advanced Conditions.
        /// Reference: K2 Designer Advanced Condition - Contains expression.
        /// XML: &lt;Contains&gt;&lt;Item SourceType="Control" .../&gt;&lt;Item SourceType="Value"&gt;&lt;SourceValue&gt;...&lt;/SourceValue&gt;&lt;/Item&gt;&lt;/Contains&gt;
        /// </summary>
        public static XmlElement BuildContainsCondition(XmlDocument doc, string controlId,
            string controlName, string compareValue, string dataType = "Text")
        {
            return BuildAdvancedTwoItemExpression(doc, "Contains", controlId, controlName, compareValue, dataType);
        }

        /// <summary>
        /// Build a "starts with" condition element for Advanced Conditions.
        /// Reference: K2 Designer Advanced Condition - StartsWith expression.
        /// </summary>
        public static XmlElement BuildStartsWithCondition(XmlDocument doc, string controlId,
            string controlName, string compareValue, string dataType = "Text")
        {
            return BuildAdvancedTwoItemExpression(doc, "StartsWith", controlId, controlName, compareValue, dataType);
        }

        /// <summary>
        /// Build an "ends with" condition element for Advanced Conditions.
        /// Reference: K2 Designer Advanced Condition - EndsWith expression.
        /// </summary>
        public static XmlElement BuildEndsWithCondition(XmlDocument doc, string controlId,
            string controlName, string compareValue, string dataType = "Text")
        {
            return BuildAdvancedTwoItemExpression(doc, "EndsWith", controlId, controlName, compareValue, dataType);
        }

        /// <summary>
        /// Build a "greater than" condition element for Advanced Conditions.
        /// Reference: K2 Designer Advanced Condition - GreaterThan expression.
        /// </summary>
        public static XmlElement BuildGreaterThanCondition(XmlDocument doc, string controlId,
            string controlName, string compareValue, string dataType = "Text")
        {
            return BuildAdvancedTwoItemExpression(doc, "GreaterThan", controlId, controlName, compareValue, dataType);
        }

        /// <summary>
        /// Build a "less than" condition element for Advanced Conditions.
        /// Reference: K2 Designer Advanced Condition - LessThan expression.
        /// </summary>
        public static XmlElement BuildLessThanCondition(XmlDocument doc, string controlId,
            string controlName, string compareValue, string dataType = "Text")
        {
            return BuildAdvancedTwoItemExpression(doc, "LessThan", controlId, controlName, compareValue, dataType);
        }

        /// <summary>
        /// Build a "greater than or equal" condition element for Advanced Conditions.
        /// Reference: K2 Designer Advanced Condition - GreaterThanEquals expression.
        /// </summary>
        public static XmlElement BuildGreaterThanEqualsCondition(XmlDocument doc, string controlId,
            string controlName, string compareValue, string dataType = "Text")
        {
            return BuildAdvancedTwoItemExpression(doc, "GreaterThanEquals", controlId, controlName, compareValue, dataType);
        }

        /// <summary>
        /// Build a "less than or equal" condition element for Advanced Conditions.
        /// Reference: K2 Designer Advanced Condition - LessThanEquals expression.
        /// </summary>
        public static XmlElement BuildLessThanEqualsCondition(XmlDocument doc, string controlId,
            string controlName, string compareValue, string dataType = "Text")
        {
            return BuildAdvancedTwoItemExpression(doc, "LessThanEquals", controlId, controlName, compareValue, dataType);
        }

        /// <summary>
        /// Build an Advanced Condition with multiple sub-expressions combined with AND logic.
        /// Reference: K2 Designer uses nested &lt;And&gt; elements for compound AND conditions
        /// within a single AdvancedCondition Condition element.
        /// Structure: Expressions > And > And > ... (nested) with leaf expressions.
        /// </summary>
        public static XmlElement BuildAdvancedAndCondition(XmlDocument doc,
            List<XmlElement> subExpressions)
        {
            if (subExpressions == null || subExpressions.Count == 0)
                return null;

            var condition = CreateConditionShell(doc, "AdvancedCondition");
            var expressions = doc.CreateElement("Expressions");

            if (subExpressions.Count == 1)
            {
                // Single expression - no And wrapper needed
                expressions.AppendChild(doc.ImportNode(subExpressions[0], true));
            }
            else
            {
                // Multiple expressions - nest with And elements
                // K2 pattern: And(And(And(expr1, expr2), expr3), expr4)
                XmlElement current = subExpressions[0];
                for (int i = 1; i < subExpressions.Count; i++)
                {
                    var andEl = doc.CreateElement("And");
                    andEl.AppendChild(doc.ImportNode(current, true));
                    andEl.AppendChild(doc.ImportNode(subExpressions[i], true));
                    current = andEl;
                }
                expressions.AppendChild(current);
            }

            condition.AppendChild(expressions);
            return condition;
        }

        /// <summary>
        /// Build an Advanced Condition with multiple sub-expressions combined with OR logic.
        /// Reference: K2 Designer uses nested &lt;Or&gt; elements for compound OR conditions.
        /// </summary>
        public static XmlElement BuildAdvancedOrCondition(XmlDocument doc,
            List<XmlElement> subExpressions)
        {
            if (subExpressions == null || subExpressions.Count == 0)
                return null;

            var condition = CreateConditionShell(doc, "AdvancedCondition");
            var expressions = doc.CreateElement("Expressions");

            if (subExpressions.Count == 1)
            {
                expressions.AppendChild(doc.ImportNode(subExpressions[0], true));
            }
            else
            {
                XmlElement current = subExpressions[0];
                for (int i = 1; i < subExpressions.Count; i++)
                {
                    var orEl = doc.CreateElement("Or");
                    orEl.AppendChild(doc.ImportNode(current, true));
                    orEl.AppendChild(doc.ImportNode(subExpressions[i], true));
                    current = orEl;
                }
                expressions.AppendChild(current);
            }

            condition.AppendChild(expressions);
            return condition;
        }

        /// <summary>
        /// Create a raw expression element (without Condition shell) for use in compound builders.
        /// Returns just the expression element (e.g. Contains, StartsWith) for embedding
        /// inside an AdvancedCondition's And/Or tree.
        /// </summary>
        public static XmlElement CreateAdvancedExpression(XmlDocument doc, string expressionType,
            string controlId, string controlName, string compareValue = null, string dataType = "Text")
        {
            var expr = doc.CreateElement(expressionType);

            var leftItem = doc.CreateElement("Item");
            leftItem.SetAttribute("SourceType", "Control");
            leftItem.SetAttribute("SourceID", controlId);
            leftItem.SetAttribute("SourceName", controlName);
            leftItem.SetAttribute("SourceDisplayName", controlName);
            leftItem.SetAttribute("DataType", dataType);
            expr.AppendChild(leftItem);

            // IsBlank/IsNotBlank have only one Item (no right value)
            if (!string.Equals(expressionType, "IsBlank", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(expressionType, "IsNotBlank", StringComparison.OrdinalIgnoreCase))
            {
                var rightItem = doc.CreateElement("Item");
                rightItem.SetAttribute("SourceType", "Value");
                rightItem.SetAttribute("DataType", dataType);

                var sourceValue = doc.CreateElement("SourceValue");
                sourceValue.SetAttribute("xml:space", "preserve");
                sourceValue.InnerText = compareValue ?? "";
                rightItem.AppendChild(sourceValue);

                expr.AppendChild(rightItem);
            }

            return expr;
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
        /// Compound condition builder using correct K2 Designer patterns.
        /// For AND with simple conditions: returns multiple Condition elements (use BuildAndConditions).
        /// For AND/OR with advanced expressions: uses nested And/Or elements inside AdvancedCondition.
        /// </summary>
        public static XmlElement BuildCompoundCondition(XmlDocument doc,
            List<XmlElement> subConditions, string logic = "And")
        {
            if (subConditions == null || subConditions.Count == 0)
                return null;

            if (subConditions.Count == 1)
                return subConditions[0];

            // For AND with simple conditions: K2 uses multiple Condition elements in Conditions block
            // Return first condition; caller should add all conditions to Conditions block
            if (string.Equals(logic, "And", StringComparison.OrdinalIgnoreCase))
                return subConditions[0];

            // For OR: build AdvancedCondition with nested Or elements
            // Reference: K2 Designer uses nested <Or> elements (same pattern as <And>)
            var condition = CreateConditionShell(doc, "AdvancedCondition");
            var expressions = doc.CreateElement("Expressions");

            // Extract expression elements from each sub-condition
            var subExpressions = new List<XmlNode>();
            foreach (var sub in subConditions)
            {
                var subExprs = sub.SelectSingleNode("Expressions");
                if (subExprs != null)
                {
                    foreach (XmlNode child in subExprs.ChildNodes)
                    {
                        subExpressions.Add(child);
                    }
                }
            }

            if (subExpressions.Count == 1)
            {
                expressions.AppendChild(doc.ImportNode(subExpressions[0], true));
            }
            else if (subExpressions.Count > 1)
            {
                // Build nested Or tree: Or(Or(expr1, expr2), expr3)
                XmlNode current = doc.ImportNode(subExpressions[0], true);
                for (int i = 1; i < subExpressions.Count; i++)
                {
                    var orEl = doc.CreateElement("Or");
                    orEl.AppendChild(current);
                    orEl.AppendChild(doc.ImportNode(subExpressions[i], true));
                    current = orEl;
                }
                expressions.AppendChild(current);
            }

            condition.AppendChild(expressions);
            return condition;
        }

        /// <summary>
        /// Helper: build a two-item expression wrapped in an AdvancedCondition.
        /// Used for Contains, StartsWith, EndsWith, GreaterThan, LessThan, etc.
        /// Advanced Conditions use SourceValue child elements instead of InnerText.
        /// </summary>
        private static XmlElement BuildAdvancedTwoItemExpression(XmlDocument doc,
            string expressionElementName, string controlId, string controlName,
            string compareValue, string dataType)
        {
            var condition = CreateConditionShell(doc, "AdvancedCondition");

            var expressions = doc.CreateElement("Expressions");
            var expr = doc.CreateElement(expressionElementName);

            var leftItem = doc.CreateElement("Item");
            leftItem.SetAttribute("SourceType", "Control");
            leftItem.SetAttribute("SourceID", controlId);
            leftItem.SetAttribute("SourceName", controlName);
            leftItem.SetAttribute("SourceDisplayName", controlName);
            leftItem.SetAttribute("DataType", dataType);
            expr.AppendChild(leftItem);

            var rightItem = doc.CreateElement("Item");
            rightItem.SetAttribute("SourceType", "Value");
            rightItem.SetAttribute("DataType", dataType);

            // Advanced conditions use SourceValue child element with xml:space="preserve"
            var sourceValue = doc.CreateElement("SourceValue");
            sourceValue.SetAttribute("xml:space", "preserve");
            sourceValue.InnerText = compareValue ?? "";
            rightItem.AppendChild(sourceValue);

            expr.AppendChild(rightItem);

            expressions.AppendChild(expr);
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
