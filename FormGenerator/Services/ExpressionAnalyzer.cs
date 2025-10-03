using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FormGenerator.Core.Models;

namespace FormGenerator.Services
{
    public class ExpressionAnalyzer
    {
        private readonly XPathFunctionParser _functionParser;

        public ExpressionAnalyzer()
        {
            _functionParser = new XPathFunctionParser();
        }

        public EnhancedExpression AnalyzeExpression(string expression)
        {
            if (string.IsNullOrEmpty(expression))
                return null;

            var enhancedExpression = new EnhancedExpression
            {
                OriginalExpression = expression,
                ParsedExpression = expression.Trim()
            };

            // Determine expression type
            enhancedExpression.Type = _functionParser.DetermineExpressionType(expression);

            // Extract components
            enhancedExpression.ReferencedFields = _functionParser.ExtractFieldReferences(expression);

            var functionCalls = _functionParser.ExtractFunctionCalls(expression);
            enhancedExpression.UsedFunctions = functionCalls.Select(f => f.Name).ToList();

            // Extract constants
            enhancedExpression.Constants = ExtractConstants(expression);

            // Analyze complexity
            AnalyzeComplexity(enhancedExpression, functionCalls);

            // Generate human-readable version
            enhancedExpression.HumanReadable = GenerateHumanReadable(enhancedExpression);

            // Determine return type
            enhancedExpression.ReturnType = DetermineReturnType(enhancedExpression, functionCalls);

            // Extract sub-expressions for complex expressions
            if (enhancedExpression.IsComplex)
            {
                enhancedExpression.SubExpressions = ExtractSubExpressions(expression);
            }

            // Generate translation hints
            enhancedExpression.TranslationHints = _functionParser.GetTranslationHints(expression)
                .ToDictionary(hint => Guid.NewGuid().ToString(), hint => hint);

            return enhancedExpression;
        }

        public string SimplifyExpression(string expression)
        {
            return _functionParser.SimplifyExpression(expression);
        }

        private void AnalyzeComplexity(EnhancedExpression expression, List<FunctionCall> functionCalls)
        {
            var complexityScore = 0;

            // Field references increase complexity
            complexityScore += expression.ReferencedFields.Count;

            // Functions increase complexity
            complexityScore += functionCalls.Count * 2;

            // Unknown functions are more complex
            complexityScore += functionCalls.Count(f => !f.IsKnownFunction) * 3;

            // Nested expressions are more complex
            if (HasNestedExpressions(expression.OriginalExpression))
            {
                complexityScore += 5;
                expression.HasNestedConditions = true;
            }

            // Conditional logic is complex
            if (HasConditionalLogic(expression.OriginalExpression))
            {
                complexityScore += 4;
            }

            // Data lookups are complex
            if (RequiresDataLookup(expression.OriginalExpression, functionCalls))
            {
                complexityScore += 3;
                expression.RequiresDataLookup = true;
            }

            // Set complexity flag
            expression.IsComplex = complexityScore > 5;
        }

        private string GenerateHumanReadable(EnhancedExpression expression)
        {
            var readable = expression.OriginalExpression;

            try
            {
                // Replace field references
                foreach (var field in expression.ReferencedFields)
                {
                    var fieldDisplay = field.Contains("/") ? field.Split('/').Last() : field;
                    readable = readable.Replace($"my:{field}", $"[{fieldDisplay}]");
                }

                // Replace common patterns
                readable = Regex.Replace(readable, @"string-length\(([^)]+)\)\s*>\s*0", "$1 is not empty");
                readable = Regex.Replace(readable, @"string-length\(([^)]+)\)\s*=\s*0", "$1 is empty");
                readable = Regex.Replace(readable, @"count\(([^)]+)\)\s*>\s*(\d+)", "$1 has more than $2 items");
                readable = Regex.Replace(readable, @"count\(([^)]+)\)\s*=\s*(\d+)", "$1 has exactly $2 items");
                readable = Regex.Replace(readable, @"sum\(([^)]+)\)", "sum of $1");
                readable = Regex.Replace(readable, @"concat\(([^)]+)\)", "combine $1");

                // Replace operators with words
                readable = readable.Replace(" and ", " AND ");
                readable = readable.Replace(" or ", " OR ");
                readable = readable.Replace("!=", " is not equal to ");
                readable = readable.Replace("=", " equals ");
                readable = readable.Replace(">", " is greater than ");
                readable = readable.Replace("<", " is less than ");
                readable = readable.Replace(">=", " is greater than or equal to ");
                readable = readable.Replace("<=", " is less than or equal to ");

                // Clean up extra spaces
                readable = Regex.Replace(readable, @"\s+", " ").Trim();

                return readable;
            }
            catch
            {
                return expression.OriginalExpression; // Fall back to original if parsing fails
            }
        }

        private string DetermineReturnType(EnhancedExpression expression, List<FunctionCall> functionCalls)
        {
            // If there are function calls, use the last one's return type
            var lastFunction = functionCalls.LastOrDefault(f => f.IsKnownFunction);
            if (lastFunction != null)
            {
                return lastFunction.Function.ReturnType;
            }

            // Analyze the expression pattern
            var expr = expression.OriginalExpression.ToLower();

            if (expr.Contains("=") || expr.Contains("!=") || expr.Contains(">") ||
                expr.Contains("<") || expr.Contains("and") || expr.Contains("or") ||
                expr.Contains("not("))
            {
                return "boolean";
            }

            if (expr.Contains("+") || expr.Contains("-") || expr.Contains("*") || expr.Contains("/") ||
                expr.Contains("sum(") || expr.Contains("count(") || expr.Contains("avg("))
            {
                return "number";
            }

            if (expr.Contains("today(") || expr.Contains("now(") || expr.Contains("adddays("))
            {
                return "date";
            }

            return "string"; // Default to string
        }

        private List<string> ExtractConstants(string expression)
        {
            var constants = new List<string>();

            // String literals
            var stringMatches = Regex.Matches(expression, @"""([^""]*)""");
            foreach (Match match in stringMatches)
            {
                constants.Add(match.Groups[1].Value);
            }

            var singleQuoteMatches = Regex.Matches(expression, @"'([^']*)'");
            foreach (Match match in singleQuoteMatches)
            {
                constants.Add(match.Groups[1].Value);
            }

            // Numeric literals
            var numberMatches = Regex.Matches(expression, @"\b\d+(?:\.\d+)?\b");
            foreach (Match match in numberMatches)
            {
                constants.Add(match.Value);
            }

            return constants.Distinct().ToList();
        }

        private List<EnhancedExpression> ExtractSubExpressions(string expression)
        {
            var subExpressions = new List<EnhancedExpression>();

            // Extract expressions within parentheses
            var parenMatches = Regex.Matches(expression, @"\(([^()]+)\)");
            foreach (Match match in parenMatches)
            {
                var subExpr = match.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(subExpr) && subExpr != expression)
                {
                    subExpressions.Add(AnalyzeExpression(subExpr));
                }
            }

            // Extract conditional branches (if they exist)
            if (expression.Contains(" and ") || expression.Contains(" or "))
            {
                var parts = SplitLogicalExpression(expression);
                foreach (var part in parts.Where(p => p != expression))
                {
                    subExpressions.Add(AnalyzeExpression(part));
                }
            }

            return subExpressions;
        }

        private bool HasNestedExpressions(string expression)
        {
            // Check for nested parentheses
            var depth = 0;
            var maxDepth = 0;

            foreach (char c in expression)
            {
                if (c == '(') depth++;
                else if (c == ')') depth--;
                maxDepth = Math.Max(maxDepth, depth);
            }

            return maxDepth > 1;
        }

        private bool HasConditionalLogic(string expression)
        {
            return expression.Contains(" and ") || expression.Contains(" or ") ||
                   expression.Contains("if(") || expression.Contains("choose(") ||
                   expression.Contains("not(");
        }

        private bool RequiresDataLookup(string expression, List<FunctionCall> functionCalls)
        {
            // Check for functions that typically require data lookups
            var lookupFunctions = new[] { "user", "username", "useremail", "role" };
            return functionCalls.Any(f => lookupFunctions.Contains(f.Name.ToLower())) ||
                   expression.Contains("../") || // Parent references might require lookups
                   expression.Contains("["); // Predicates often involve lookups
        }

        private List<string> SplitLogicalExpression(string expression)
        {
            var parts = new List<string>();

            // Simple splitting on 'and' and 'or' - this could be made more sophisticated
            var andParts = expression.Split(new[] { " and " }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var andPart in andParts)
            {
                var orParts = andPart.Split(new[] { " or " }, StringSplitOptions.RemoveEmptyEntries);
                parts.AddRange(orParts.Select(p => p.Trim()));
            }

            return parts.Where(p => !string.IsNullOrEmpty(p)).Distinct().ToList();
        }
    }
}