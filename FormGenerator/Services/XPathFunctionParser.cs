using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FormGenerator.Core.Models;

namespace FormGenerator.Services
{
    public class XPathFunctionParser
    {
        // InfoPath-specific XPath functions and their patterns
        private readonly Dictionary<string, XPathFunction> _infoPathFunctions = new Dictionary<string, XPathFunction>();

        public XPathFunctionParser()
        {
            InitializeInfoPathFunctions();
        }

        private void InitializeInfoPathFunctions()
        {
            _infoPathFunctions.Clear();

            var functions = new Dictionary<string, XPathFunction>
            {
                // Date/Time Functions
                ["today"] = new XPathFunction("today", "Returns current date", "date", new[] { "()" }),
                ["now"] = new XPathFunction("now", "Returns current date and time", "dateTime", new[] { "()" }),
                ["addDays"] = new XPathFunction("addDays", "Adds days to a date", "date", new[] { "(date, number)" }),
                ["addMonths"] = new XPathFunction("addMonths", "Adds months to a date", "date", new[] { "(date, number)" }),
                ["addYears"] = new XPathFunction("addYears", "Adds years to a date", "date", new[] { "(date, number)" }),
                ["formatDate"] = new XPathFunction("formatDate", "Formats a date", "string", new[] { "(date, format)" }),

                // String Functions
                ["concat"] = new XPathFunction("concat", "Concatenates strings", "string", new[] { "(string, string, ...)" }),
                ["substring"] = new XPathFunction("substring", "Extracts substring", "string", new[] { "(string, start)", "(string, start, length)" }),
                ["substring-before"] = new XPathFunction("substring-before", "String before separator", "string", new[] { "(string, separator)" }),
                ["substring-after"] = new XPathFunction("substring-after", "String after separator", "string", new[] { "(string, separator)" }),
                ["string-length"] = new XPathFunction("string-length", "Length of string", "number", new[] { "(string)" }),
                ["normalize-space"] = new XPathFunction("normalize-space", "Normalizes whitespace", "string", new[] { "(string)" }),
                ["translate"] = new XPathFunction("translate", "Translates characters", "string", new[] { "(string, from, to)" }),
                ["contains"] = new XPathFunction("contains", "Checks if string contains substring", "boolean", new[] { "(string, substring)" }),
                ["starts-with"] = new XPathFunction("starts-with", "Checks if string starts with prefix", "boolean", new[] { "(string, prefix)" }),
                ["ends-with"] = new XPathFunction("ends-with", "Checks if string ends with suffix", "boolean", new[] { "(string, suffix)" }),

                // Math Functions
                ["sum"] = new XPathFunction("sum", "Sum of nodes", "number", new[] { "(nodeset)" }),
                ["count"] = new XPathFunction("count", "Count of nodes", "number", new[] { "(nodeset)" }),
                ["avg"] = new XPathFunction("avg", "Average of nodes", "number", new[] { "(nodeset)" }),
                ["min"] = new XPathFunction("min", "Minimum value", "number", new[] { "(nodeset)" }),
                ["max"] = new XPathFunction("max", "Maximum value", "number", new[] { "(nodeset)" }),
                ["round"] = new XPathFunction("round", "Rounds number", "number", new[] { "(number)" }),
                ["ceiling"] = new XPathFunction("ceiling", "Rounds up", "number", new[] { "(number)" }),
                ["floor"] = new XPathFunction("floor", "Rounds down", "number", new[] { "(number)" }),
                ["abs"] = new XPathFunction("abs", "Absolute value", "number", new[] { "(number)" }),

                // Logical Functions
                ["not"] = new XPathFunction("not", "Logical NOT", "boolean", new[] { "(boolean)" }),
                ["true"] = new XPathFunction("true", "Boolean true", "boolean", new[] { "()" }),
                ["false"] = new XPathFunction("false", "Boolean false", "boolean", new[] { "()" }),

                // Node Functions
                ["position"] = new XPathFunction("position", "Current position", "number", new[] { "()" }),
                ["last"] = new XPathFunction("last", "Last position", "number", new[] { "()" }),
                ["node-set"] = new XPathFunction("node-set", "Creates node set", "nodeset", new[] { "(object)" }),

                // InfoPath Specific
                ["user"] = new XPathFunction("user", "Current user info", "string", new[] { "()" }),
                ["userName"] = new XPathFunction("userName", "Current user name", "string", new[] { "()" }),
                ["userEmail"] = new XPathFunction("userEmail", "Current user email", "string", new[] { "()" }),
                ["role"] = new XPathFunction("role", "User role", "string", new[] { "(roleName)" }),

                // Conversion Functions
                ["number"] = new XPathFunction("number", "Converts to number", "number", new[] { "(object)" }),
                ["string"] = new XPathFunction("string", "Converts to string", "string", new[] { "(object)" }),
                ["boolean"] = new XPathFunction("boolean", "Converts to boolean", "boolean", new[] { "(object)" })
            };

            foreach (var kvp in functions)
            {
                _infoPathFunctions[kvp.Key] = kvp.Value;
            }
        }

        public List<FunctionCall> ExtractFunctionCalls(string expression)
        {
            var functionCalls = new List<FunctionCall>();

            // Pattern to match function calls: functionName(arguments)
            var functionPattern = @"(\w+)\s*\(([^)]*)\)";
            var matches = Regex.Matches(expression, functionPattern);

            foreach (Match match in matches)
            {
                var functionName = match.Groups[1].Value;
                var arguments = match.Groups[2].Value;

                var functionCall = new FunctionCall
                {
                    Name = functionName,
                    Arguments = ParseArguments(arguments),
                    OriginalCall = match.Value
                };

                if (_infoPathFunctions.ContainsKey(functionName))
                {
                    functionCall.Function = _infoPathFunctions[functionName];
                    functionCall.IsKnownFunction = true;
                }
                else
                {
                    functionCall.IsKnownFunction = false;
                    functionCall.Function = new XPathFunction(functionName, "Unknown function", "unknown", new[] { "(...)" });
                }

                functionCalls.Add(functionCall);
            }

            return functionCalls;
        }

        public bool IsCalculationExpression(string expression)
        {
            if (string.IsNullOrEmpty(expression)) return false;

            // Check for mathematical operators (but exclude comparison operators)
            if (Regex.IsMatch(expression, @"[+\-*/]") && !Regex.IsMatch(expression, @"[<>=!]")) return true;

            // Check for explicit mathematical operations
            if (Regex.IsMatch(expression, @"\b\d+\s*[+\-*/]\s*\d+\b")) return true;
            if (Regex.IsMatch(expression, @"my:[\w/]+\s*[+\-*/]\s*(my:[\w/]+|\d+)")) return true;

            // Check for calculation functions
            var calculationFunctions = new[] { "sum", "count", "avg", "min", "max", "round", "ceiling", "floor", "abs", "mod", "div" };
            if (calculationFunctions.Any(func => expression.Contains($"{func}("))) return true;

            // Check for numeric functions that indicate calculations
            var numericFunctions = new[] { "number(", "format-number(", "ceiling(", "floor(", "round(" };
            if (numericFunctions.Any(func => expression.Contains(func))) return true;

            // Check for expressions with multiple field references (often calculations)
            var fieldMatches = Regex.Matches(expression, @"my:[\w/]+");
            if (fieldMatches.Count > 1 && expression.Contains("*")) return true;

            // Check for string-to-number conversions (often used in calculations)
            if (expression.Contains("number(my:") && (expression.Contains("+") || expression.Contains("*"))) return true;

            // Check for calculated field references (common calculation patterns)
            if (IsCalculatedFieldReference(expression)) return true;

            return false;
        }

        private bool IsCalculatedFieldReference(string expression)
        {
            // Fields that commonly represent calculated values
            var calculatedFieldPatterns = new[]
            {
                @"my:[\w/]*(?:total|sum|subtotal|amount|cost|price|value|calculated)[\w/]*",
                @"my:[\w/]*(?:qty|quantity|count|number)[\w/]*\s*\*",
                @"my:[\w/]+/my:(?:total|sum|subtotal|amount|cost|price|value|calculated)[\w/]*"
            };

            foreach (var pattern in calculatedFieldPatterns)
            {
                if (Regex.IsMatch(expression, pattern, RegexOptions.IgnoreCase))
                    return true;
            }

            // Check for aggregation field references (like my:items/my:subTotal)
            if (Regex.IsMatch(expression, @"my:\w+/my:(?:total|sum|subtotal|amount|cost|price|value|calculated)", RegexOptions.IgnoreCase))
                return true;

            return false;
        }

        public ExpressionType DetermineExpressionType(string expression)
        {
            if (string.IsNullOrEmpty(expression)) return ExpressionType.Static;

            // Check for various expression types
            if (expression.Contains("concat(")) return ExpressionType.Concatenation;
            if (IsCalculationExpression(expression)) return ExpressionType.Calculation;
            if (expression.Contains("if(") || expression.Contains("choose(")) return ExpressionType.Conditional;
            if (ContainsDateFunctions(expression)) return ExpressionType.DateFunction;
            if (ContainsStringFunctions(expression)) return ExpressionType.StringFunction;
            if (ContainsAggregationFunctions(expression)) return ExpressionType.Aggregation;
            if (expression.StartsWith("my:") && !expression.Contains("(")) return ExpressionType.FieldReference;

            return ExpressionType.Static;
        }

        public List<string> ExtractFieldReferences(string expression)
        {
            var fields = new List<string>();

            // Pattern to match field references: my:fieldName or my:section/fieldName
            var fieldPattern = @"my:([a-zA-Z_][\w]*(?:/[a-zA-Z_][\w]*)*)";
            var matches = Regex.Matches(expression, fieldPattern);

            foreach (Match match in matches)
            {
                fields.Add(match.Groups[1].Value);
            }

            return fields.Distinct().ToList();
        }

        public string SimplifyExpression(string expression)
        {
            if (string.IsNullOrEmpty(expression)) return expression;

            var simplified = expression;

            // Replace namespace prefixes
            simplified = simplified.Replace("my:", "");
            simplified = simplified.Replace("../", "parent/");

            // Replace common XPath patterns with readable text
            simplified = Regex.Replace(simplified, @"string-length\(([^)]+)\)\s*>\s*0", "$1 is not empty");
            simplified = Regex.Replace(simplified, @"string-length\(([^)]+)\)\s*=\s*0", "$1 is empty");
            simplified = Regex.Replace(simplified, @"count\(([^)]+)\)\s*>\s*0", "$1 has items");
            simplified = Regex.Replace(simplified, @"not\(([^)]+)\)", "NOT $1");

            return simplified.Trim();
        }

        public List<string> GetTranslationHints(string expression, string targetPlatform = "general")
        {
            var hints = new List<string>();

            var functionCalls = ExtractFunctionCalls(expression);

            foreach (var call in functionCalls)
            {
                if (!call.IsKnownFunction)
                {
                    hints.Add($"Unknown function '{call.Name}' - may need custom implementation");
                    continue;
                }

                switch (call.Name.ToLower())
                {
                    case "today":
                    case "now":
                        hints.Add($"Date function '{call.Name}' - use current date/time functions in target platform");
                        break;
                    case "user":
                    case "username":
                    case "useremail":
                        hints.Add($"User context function '{call.Name}' - implement user context service");
                        break;
                    case "sum":
                    case "count":
                    case "avg":
                        hints.Add($"Aggregation function '{call.Name}' - may need database aggregation or client-side calculation");
                        break;
                    case "concat":
                        hints.Add("String concatenation - use string interpolation or concatenation operators");
                        break;
                    default:
                        if (_infoPathFunctions.ContainsKey(call.Name))
                        {
                            hints.Add($"Standard function '{call.Name}' - should be available in most platforms");
                        }
                        break;
                }
            }

            return hints;
        }

        private List<string> ParseArguments(string argumentString)
        {
            if (string.IsNullOrEmpty(argumentString.Trim())) return new List<string>();

            var arguments = new List<string>();
            var current = "";
            var depth = 0;
            var inQuotes = false;
            var quoteChar = '"';

            for (int i = 0; i < argumentString.Length; i++)
            {
                char c = argumentString[i];

                if (!inQuotes && (c == '"' || c == '\''))
                {
                    inQuotes = true;
                    quoteChar = c;
                }
                else if (inQuotes && c == quoteChar)
                {
                    inQuotes = false;
                }
                else if (!inQuotes && c == '(')
                {
                    depth++;
                }
                else if (!inQuotes && c == ')')
                {
                    depth--;
                }
                else if (!inQuotes && c == ',' && depth == 0)
                {
                    arguments.Add(current.Trim());
                    current = "";
                    continue;
                }

                current += c;
            }

            if (!string.IsNullOrEmpty(current.Trim()))
            {
                arguments.Add(current.Trim());
            }

            return arguments;
        }

        private bool ContainsDateFunctions(string expression)
        {
            var dateFunctions = new[] { "today", "now", "addDays", "addMonths", "addYears", "formatDate" };
            return dateFunctions.Any(func => expression.Contains($"{func}("));
        }

        private bool ContainsStringFunctions(string expression)
        {
            var stringFunctions = new[] { "concat", "substring", "string-length", "normalize-space", "translate", "contains", "starts-with" };
            return stringFunctions.Any(func => expression.Contains($"{func}("));
        }

        private bool ContainsAggregationFunctions(string expression)
        {
            var aggFunctions = new[] { "sum", "count", "avg", "min", "max" };
            return aggFunctions.Any(func => expression.Contains($"{func}("));
        }
    }

    public class XPathFunction
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string ReturnType { get; set; }
        public string[] Signatures { get; set; }

        public XPathFunction(string name, string description, string returnType, string[] signatures)
        {
            Name = name;
            Description = description;
            ReturnType = returnType;
            Signatures = signatures;
        }
    }

    public class FunctionCall
    {
        public string Name { get; set; }
        public List<string> Arguments { get; set; } = new List<string>();
        public string OriginalCall { get; set; }
        public XPathFunction Function { get; set; }
        public bool IsKnownFunction { get; set; }
    }
}