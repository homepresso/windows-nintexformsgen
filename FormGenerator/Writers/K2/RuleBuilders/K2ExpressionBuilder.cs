using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using FormGenerator.Analyzers.Infopath;
using FormGenerator.Core.Models;
using FormGenerator.Services;
using K2SmartObjectGenerator.Utilities;

namespace FormGenerator.Writers.K2.RuleBuilders
{
    /// <summary>
    /// Builds K2 Expression elements for calculated fields.
    /// K2 handles calculations via the Expressions section (NOT rule events).
    /// Reference: calculated-field.xml
    ///
    /// Pattern:
    ///   <Expressions>
    ///     <Expression ID="guid">
    ///       <Name>Number A + Number B Calculation</Name>
    ///       <DisplayValue>Number A + Number B</DisplayValue>
    ///       <Plus>
    ///         <Item SourceType="Control" SourceID="guid" SourceName="Number A"
    ///               SourceDisplayName="Number A" DisplayPath="viewName - Controls - Number A" DataType="Text"/>
    ///         <Item SourceType="Control" SourceID="guid" SourceName="Number B"
    ///               SourceDisplayName="Number B" DisplayPath="viewName - Controls - Number B" DataType="Text"/>
    ///       </Plus>
    ///     </Expression>
    ///   </Expressions>
    ///
    /// Target control becomes DataLabel with ExpressionID attribute and ControlExpression property.
    /// </summary>
    public static class K2ExpressionBuilder
    {
        /// <summary>
        /// Process all calculation rules from the InfoPath form definition and:
        /// 1. Create Expression elements in the Expressions section
        /// 2. Modify target controls to DataLabel with ExpressionID
        /// </summary>
        public static void AddCalculationExpressions(XmlDocument doc, XmlElement expressionsElement,
            InfoPathFormDefinition formDef, K2RuleContext context)
        {
            if (formDef?.ConditionalRules == null || context == null) return;

            var calcRules = formDef.ConditionalRules
                .Where(r => r != null &&
                       string.Equals(r.Type, "Calculation", StringComparison.OrdinalIgnoreCase) &&
                       !IsFormattingExpression(r.Condition) &&
                       !IsFormattingExpression(r.Value))
                .ToList();

            if (calcRules.Count == 0) return;

            Console.WriteLine($"    Processing {calcRules.Count} calculation rule(s) for expressions...");

            foreach (var rule in calcRules)
            {
                try
                {
                    AddExpressionForRule(doc, expressionsElement, rule, context);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"    WARNING: Failed to create expression for '{rule.Name}': {ex.Message}");
                }
            }
        }

        private static void AddExpressionForRule(XmlDocument doc, XmlElement expressionsElement,
            ConditionalRule rule, K2RuleContext context)
        {
            // Parse the expression to determine operator and source fields
            var parsedExpr = ParseExpression(rule.Value ?? rule.Condition);
            if (parsedExpr == null)
            {
                Console.WriteLine($"      Skipping calculation '{rule.Name}': could not parse expression");
                return;
            }

            // Resolve source controls
            var sourceControls = new List<ResolvedControl>();
            foreach (var fieldRef in parsedExpr.FieldReferences)
            {
                var resolved = K2RuleBuilderBase.ResolveControl(context, fieldRef);
                if (resolved == null)
                {
                    Console.WriteLine($"      Skipping calculation '{rule.Name}': could not resolve source field '{fieldRef}'");
                    return;
                }
                sourceControls.Add(resolved);
            }

            // Resolve target control
            var targetResolved = K2RuleBuilderBase.ResolveControl(context, rule.TargetField);
            if (targetResolved == null)
            {
                Console.WriteLine($"      Skipping calculation '{rule.Name}': could not resolve target field '{rule.TargetField}'");
                return;
            }

            // Create the Expression element
            string expressionId = Guid.NewGuid().ToString();
            string exprName = BuildExpressionName(sourceControls, parsedExpr.Operator);
            string exprDisplayValue = BuildExpressionDisplayValue(sourceControls, parsedExpr.Operator);

            var expressionEl = doc.CreateElement("Expression");
            expressionEl.SetAttribute("ID", expressionId);

            XmlHelper.AddElement(doc, expressionEl, "Name", exprName);
            XmlHelper.AddElement(doc, expressionEl, "DisplayValue", exprDisplayValue);

            // Create the operator element (Plus, Minus, Multiply, Divide, etc.)
            string operatorElementName = GetK2OperatorElementName(parsedExpr.Operator);
            var operatorEl = doc.CreateElement(operatorElementName);

            foreach (var sourceCtrl in sourceControls)
            {
                var item = doc.CreateElement("Item");
                item.SetAttribute("SourceType", "Control");
                item.SetAttribute("SourceID", sourceCtrl.ControlId);
                item.SetAttribute("SourceName", sourceCtrl.ControlName);
                item.SetAttribute("SourceDisplayName", sourceCtrl.ControlName);
                item.SetAttribute("DisplayPath", $"{context.ViewName} - Controls - {sourceCtrl.ControlName}");
                item.SetAttribute("DataType", sourceCtrl.DataType ?? "Text");
                operatorEl.AppendChild(item);
            }

            expressionEl.AppendChild(operatorEl);
            expressionsElement.AppendChild(expressionEl);

            // Modify the target control in the document
            ModifyTargetControlForExpression(doc, targetResolved, expressionId, exprName);

            Console.WriteLine($"      Added expression '{exprName}' -> {targetResolved.ControlName} (ID: {expressionId})");
        }

        /// <summary>
        /// Modifies the target control to be a DataLabel with ExpressionID attribute
        /// and ControlExpression property, matching the K2 reference pattern.
        /// </summary>
        private static void ModifyTargetControlForExpression(XmlDocument doc,
            ResolvedControl targetControl, string expressionId, string expressionName)
        {
            // Find the control element in the document
            var controlElements = doc.GetElementsByTagName("Control");
            XmlElement targetElement = null;

            foreach (XmlElement ctrl in controlElements)
            {
                if (ctrl.GetAttribute("ID") == targetControl.ControlId)
                {
                    targetElement = ctrl;
                    break;
                }
            }

            if (targetElement == null)
            {
                Console.WriteLine($"      WARNING: Could not find control element for '{targetControl.ControlName}' to add ExpressionID");
                return;
            }

            // Change control type to DataLabel
            targetElement.SetAttribute("Type", "DataLabel");

            // Add ExpressionID attribute
            targetElement.SetAttribute("ExpressionID", expressionId);

            // Find or create Properties element
            var propsElement = targetElement.SelectSingleNode("Properties") as XmlElement;
            if (propsElement == null)
            {
                propsElement = doc.CreateElement("Properties");
                targetElement.AppendChild(propsElement);
            }

            // Add ControlExpression property
            var exprProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, exprProp, "Name", "ControlExpression");
            XmlHelper.AddElement(doc, exprProp, "DisplayValue", expressionName);
            XmlHelper.AddElement(doc, exprProp, "NameValue", expressionName);
            XmlHelper.AddElement(doc, exprProp, "Value", expressionId);
            propsElement.AppendChild(exprProp);

            // Add LiteralVal=false property (required for DataLabel with expression)
            var literalProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, literalProp, "Name", "LiteralVal");
            XmlHelper.AddElement(doc, literalProp, "DisplayValue", "false");
            XmlHelper.AddElement(doc, literalProp, "Value", "false");
            propsElement.AppendChild(literalProp);

            // Add SanitizeHtml=false property
            var sanitizeProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, sanitizeProp, "Name", "SanitizeHtml");
            XmlHelper.AddElement(doc, sanitizeProp, "DisplayValue", "false");
            XmlHelper.AddElement(doc, sanitizeProp, "Value", "false");
            propsElement.AppendChild(sanitizeProp);
        }

        #region Expression Parsing

        /// <summary>
        /// Parse an InfoPath calculation expression to extract operator and field references.
        /// Handles patterns like:
        ///   "field1 + field2"
        ///   "/my:myFields/my:field1 + /my:myFields/my:field2"
        ///   "concat(field1, field2)"
        ///   "sum(field1)"
        /// </summary>
        private static ParsedExpression ParseExpression(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression)) return null;

            // Try arithmetic operator pattern: field1 op field2
            var arithmeticMatch = TryParseArithmeticExpression(expression);
            if (arithmeticMatch != null) return arithmeticMatch;

            // Try function pattern: func(field1, field2)
            var funcMatch = TryParseFunctionExpression(expression);
            if (funcMatch != null) return funcMatch;

            // Try simple field reference (direct value transfer - treated as assignment)
            var fieldRef = ExtractFieldName(expression.Trim());
            if (!string.IsNullOrEmpty(fieldRef))
            {
                return new ParsedExpression
                {
                    Operator = "Assign",
                    FieldReferences = new List<string> { fieldRef }
                };
            }

            return null;
        }

        private static ParsedExpression TryParseArithmeticExpression(string expression)
        {
            // Match patterns like: field1 + field2, /my:field1 - /my:field2
            // Support chained operations: field1 + field2 + field3
            var operatorMap = new Dictionary<string, string>
            {
                { "+", "Plus" },
                { "-", "Minus" },
                { "*", "Multiply" },
                { "/", "Divide" }
            };

            // Find the primary operator (split by +, -, *, /)
            // Be careful not to split on / inside XPath paths like /my:field
            foreach (var op in operatorMap)
            {
                var parts = SplitByOperator(expression, op.Key);
                if (parts != null && parts.Count >= 2)
                {
                    var fieldRefs = parts
                        .Select(p => ExtractFieldName(p.Trim()))
                        .Where(f => !string.IsNullOrEmpty(f))
                        .ToList();

                    if (fieldRefs.Count >= 2)
                    {
                        return new ParsedExpression
                        {
                            Operator = op.Value,
                            FieldReferences = fieldRefs
                        };
                    }
                }
            }

            return null;
        }

        private static ParsedExpression TryParseFunctionExpression(string expression)
        {
            // Match patterns like: concat(field1, field2), sum(field1)
            var funcMatch = Regex.Match(expression.Trim(),
                @"^(\w+)\s*\((.+)\)$", RegexOptions.Singleline);

            if (!funcMatch.Success) return null;

            string funcName = funcMatch.Groups[1].Value.ToLower();
            string args = funcMatch.Groups[2].Value;

            string k2Operator = funcName switch
            {
                "concat" => "Concat",
                "sum" => "Plus",
                "count" => "Count",
                _ => null
            };

            if (k2Operator == null) return null;

            // Split arguments by comma, but respect nested parentheses
            var argParts = SplitArguments(args);
            var fieldRefs = argParts
                .Select(a => ExtractFieldName(a.Trim()))
                .Where(f => !string.IsNullOrEmpty(f))
                .ToList();

            if (fieldRefs.Count > 0)
            {
                return new ParsedExpression
                {
                    Operator = k2Operator,
                    FieldReferences = fieldRefs
                };
            }

            return null;
        }

        /// <summary>
        /// Split expression by arithmetic operator, ignoring operators inside XPath paths.
        /// </summary>
        private static List<string> SplitByOperator(string expression, string op)
        {
            if (op == "/")
            {
                // For division, need to distinguish from XPath path separators
                // Look for ' / ' (with spaces) to distinguish from /my:field
                var parts = Regex.Split(expression, @"\s+/\s+")
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrEmpty(p))
                    .ToList();
                return parts.Count >= 2 ? parts : null;
            }

            if (op == "-")
            {
                // For subtraction, need to be careful with negative numbers
                var parts = Regex.Split(expression, @"\s+\-\s+")
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrEmpty(p))
                    .ToList();
                return parts.Count >= 2 ? parts : null;
            }

            // For + and *, simpler split
            var opPattern = op == "+" ? @"\s*\+\s*" : @"\s*\*\s*";
            var result = Regex.Split(expression, opPattern)
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();

            return result.Count >= 2 ? result : null;
        }

        private static List<string> SplitArguments(string args)
        {
            var result = new List<string>();
            int depth = 0;
            int start = 0;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == '(') depth++;
                else if (args[i] == ')') depth--;
                else if (args[i] == ',' && depth == 0)
                {
                    result.Add(args.Substring(start, i - start));
                    start = i + 1;
                }
            }

            result.Add(args.Substring(start));
            return result;
        }

        /// <summary>
        /// Extract a clean field name from an XPath or field reference.
        /// /my:myFields/my:field1 -> field1
        /// my:field1 -> field1
        /// field1 -> field1
        /// </summary>
        private static string ExtractFieldName(string fieldPath)
        {
            if (string.IsNullOrEmpty(fieldPath)) return null;

            // Remove quotes
            fieldPath = fieldPath.Trim('\'', '"');

            // If it's a numeric literal, skip it
            if (double.TryParse(fieldPath, out _)) return null;

            var result = fieldPath;

            // Take last segment if it contains path separators
            if (result.Contains("/"))
            {
                var segments = result.Split('/');
                result = segments[segments.Length - 1];
            }

            // Remove my: prefix
            result = result.Replace("my:", "");

            // Remove array notation [n]
            result = Regex.Replace(result, @"\[\d+\]", "");

            return string.IsNullOrWhiteSpace(result) ? null : result.Trim();
        }

        #endregion

        #region Name Building

        private static string BuildExpressionName(List<ResolvedControl> sourceControls, string operatorType)
        {
            string opSymbol = GetOperatorSymbol(operatorType);
            string fieldNames = string.Join($" {opSymbol} ", sourceControls.Select(c => c.ControlName));
            return $"{fieldNames} Calculation";
        }

        private static string BuildExpressionDisplayValue(List<ResolvedControl> sourceControls, string operatorType)
        {
            string opSymbol = GetOperatorSymbol(operatorType);
            return string.Join($" {opSymbol} ", sourceControls.Select(c => c.ControlName));
        }

        private static string GetOperatorSymbol(string operatorType)
        {
            return operatorType switch
            {
                "Plus" => "+",
                "Minus" => "-",
                "Multiply" => "*",
                "Divide" => "/",
                "Concat" => "+",
                _ => "+"
            };
        }

        private static string GetK2OperatorElementName(string operatorType)
        {
            return operatorType switch
            {
                "Plus" => "Plus",
                "Minus" => "Minus",
                "Multiply" => "Multiply",
                "Divide" => "Divide",
                "Concat" => "Concat",
                "Count" => "Count",
                "Assign" => "Plus", // Single-field assignment uses Plus with one item
                _ => "Plus"
            };
        }

        #endregion

        /// <summary>
        /// Detects formatting expressions that should NOT be treated as calculations.
        /// These are InfoPath formatting directives (xdFormatting, formatString, etc.)
        /// that K2 handles natively via control properties.
        /// </summary>
        private static bool IsFormattingExpression(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression)) return false;

            return expression.Contains("xdFormatting:") ||
                   expression.Contains("formatString(") ||
                   expression.Contains("xdDate:") ||
                   expression.Contains("format-date(") ||
                   expression.Contains("format-number(") ||
                   expression.Contains("format-time(") ||
                   expression.Contains("xdMath:") ||
                   expression.Contains("xdXDocument:") ||
                   expression.Contains("xdImage:") ||
                   expression.Contains("xdEnvironment:");
        }

        private class ParsedExpression
        {
            public string Operator { get; set; }
            public List<string> FieldReferences { get; set; } = new List<string>();
        }
    }
}
