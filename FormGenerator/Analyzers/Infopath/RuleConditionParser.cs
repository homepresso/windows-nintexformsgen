using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FormGenerator.Analyzers.Infopath
{
    internal static class RuleConditionParser
    {
        private static readonly Regex FieldReferenceRegex = new Regex(
            @"(?:\b(?:my|xd):)([A-Za-z0-9_\-]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex FallbackPathSegmentRegex = new Regex(
            @"/(?:\.\.|\.\.)?([A-Za-z0-9_\-]+)(?=(?:[^A-Za-z0-9_\-]|$))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly HashSet<string> KnownXPathFunctions = new(StringComparer.OrdinalIgnoreCase)
        {
            "sum", "count", "avg", "min", "max", "concat", "translate", "substring",
            "normalize-space", "contains", "starts-with", "ends-with", "substring-before",
            "substring-after", "boolean", "not", "true", "false", "string", "number"
        };

        public static string ExtractLeafFieldName(string condition)
        {
            if (string.IsNullOrWhiteSpace(condition))
                return string.Empty;

            var cleaned = RemoveQuotedText(condition);
            var normalized = NormalizeCondition(cleaned);
            var fieldNames = ExtractFieldNames(normalized);

            if (fieldNames.Length > 0)
                return fieldNames[fieldNames.Length - 1];

            return string.Empty;
        }

        public static string[] ExtractFieldNames(string condition)
        {
            if (string.IsNullOrWhiteSpace(condition))
                return Array.Empty<string>();

            var cleaned = RemoveQuotedText(condition);
            var fieldNames = new List<string>();

            foreach (Match match in FieldReferenceRegex.Matches(cleaned))
            {
                var name = match.Groups[1].Value;
                if (!string.IsNullOrWhiteSpace(name))
                    fieldNames.Add(name);
            }

            if (fieldNames.Count > 0)
                return fieldNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            foreach (Match match in FallbackPathSegmentRegex.Matches(cleaned))
            {
                var name = match.Groups[1].Value;
                if (!string.IsNullOrWhiteSpace(name) && !KnownXPathFunctions.Contains(name))
                    fieldNames.Add(name);
            }

            return fieldNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public static string NormalizeCondition(string expression)
        {
            if (expression is null)
                return string.Empty;

            var normalized = expression.Trim();
            normalized = Regex.Replace(normalized, @"\s+", " ");
            normalized = Regex.Replace(normalized, @"[\r\n\t]+", " ");
            return normalized;
        }

        private static string RemoveQuotedText(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var result = new StringBuilder(value.Length);
            bool inSingleQuote = false;
            bool inDoubleQuote = false;

            foreach (var ch in value)
            {
                if (ch == '\'' && !inDoubleQuote)
                {
                    inSingleQuote = !inSingleQuote;
                    result.Append(' ');
                    continue;
                }

                if (ch == '"' && !inSingleQuote)
                {
                    inDoubleQuote = !inDoubleQuote;
                    result.Append(' ');
                    continue;
                }

                result.Append(inSingleQuote || inDoubleQuote ? ' ' : ch);
            }

            return result.ToString();
        }
    }
}
