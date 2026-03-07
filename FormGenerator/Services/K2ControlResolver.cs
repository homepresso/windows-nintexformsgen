using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace FormGenerator.Services
{
    /// <summary>
    /// Multi-strategy resolver that maps InfoPath field names/XPaths to K2 control IDs.
    /// Returns null (not random GUIDs) when resolution fails.
    /// </summary>
    public class K2ControlResolver
    {
        private readonly Dictionary<string, string> _controlIdMap;
        private readonly Dictionary<string, string> _controlToFieldMap;
        private readonly Dictionary<string, string> _jsonToK2ControlIdMap;

        public K2ControlResolver(
            Dictionary<string, string> controlIdMap,
            Dictionary<string, string> controlToFieldMap,
            Dictionary<string, string> jsonToK2ControlIdMap)
        {
            _controlIdMap = controlIdMap ?? new Dictionary<string, string>();
            _controlToFieldMap = controlToFieldMap ?? new Dictionary<string, string>();
            _jsonToK2ControlIdMap = jsonToK2ControlIdMap ?? new Dictionary<string, string>();
        }

        /// <summary>
        /// Resolves a field name or XPath to a K2 control ID and name.
        /// Returns null if unresolvable - callers should skip the rule.
        /// </summary>
        public ResolvedControl Resolve(string fieldNameOrXPath)
        {
            if (string.IsNullOrEmpty(fieldNameOrXPath))
                return null;

            // Strategy 1: Direct name match in controlIdMap
            if (_controlIdMap.TryGetValue(fieldNameOrXPath, out var controlId))
            {
                return new ResolvedControl { ControlId = controlId, ControlName = fieldNameOrXPath };
            }

            // Strategy 2: XPath-stripped match (/my:myFields/my:field1 -> field1)
            var stripped = StripXPath(fieldNameOrXPath);
            if (!string.Equals(stripped, fieldNameOrXPath) && _controlIdMap.TryGetValue(stripped, out controlId))
            {
                return new ResolvedControl { ControlId = controlId, ControlName = stripped };
            }

            // Strategy 3: Sanitized name match (remove special chars, underscores)
            var sanitized = SanitizeName(fieldNameOrXPath);
            if (!string.Equals(sanitized, fieldNameOrXPath) && _controlIdMap.TryGetValue(sanitized, out controlId))
            {
                return new ResolvedControl { ControlId = controlId, ControlName = sanitized };
            }

            // Strategy 4: JSON CtrlId match
            if (_jsonToK2ControlIdMap.TryGetValue(fieldNameOrXPath, out controlId))
            {
                return new ResolvedControl { ControlId = controlId, ControlName = fieldNameOrXPath };
            }

            // Strategy 5: Case-insensitive search across controlIdMap
            foreach (var kvp in _controlIdMap)
            {
                if (string.Equals(kvp.Key, fieldNameOrXPath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(kvp.Key, stripped, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(kvp.Key, sanitized, StringComparison.OrdinalIgnoreCase))
                {
                    return new ResolvedControl { ControlId = kvp.Value, ControlName = kvp.Key };
                }
            }

            // Strategy 6: Try UPPERCASE version directly (K2 control names are often uppercase)
            var upper = stripped.ToUpperInvariant();
            if (!string.Equals(upper, stripped) && _controlIdMap.TryGetValue(upper, out controlId))
            {
                return new ResolvedControl { ControlId = controlId, ControlName = upper };
            }

            // Strategy 7: Reverse lookup through controlToFieldMap
            // controlToFieldMap maps ControlName -> FieldName, we need FieldName -> ControlName -> ControlId
            foreach (var kvp in _controlToFieldMap)
            {
                if (string.Equals(kvp.Value, fieldNameOrXPath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(kvp.Value, stripped, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(kvp.Key, fieldNameOrXPath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(kvp.Key, stripped, StringComparison.OrdinalIgnoreCase))
                {
                    // Found a matching field, now get the control ID
                    if (_controlIdMap.TryGetValue(kvp.Key, out controlId))
                    {
                        return new ResolvedControl { ControlId = controlId, ControlName = kvp.Key };
                    }
                }
            }

            // Not found - return null, NOT a random GUID
            return null;
        }

        /// <summary>
        /// Strip XPath notation to get the leaf field name.
        /// /my:myFields/my:field1 -> field1
        /// my:field1 -> field1
        /// </summary>
        private string StripXPath(string xpath)
        {
            if (string.IsNullOrEmpty(xpath)) return xpath;

            var result = xpath;

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

            return result.Trim();
        }

        /// <summary>
        /// Sanitize field name by normalizing XPath-like names into simple names
        /// </summary>
        private string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            var result = name;
            result = Regex.Replace(result, @"^/+", "");
            result = Regex.Replace(result, @"/my:", "_");
            result = result.Replace("my:", "");
            result = result.Replace("/", "_");
            result = Regex.Replace(result, @"\[\d+\]", "");

            return result.Trim();
        }
    }

    /// <summary>
    /// Result of a successful control resolution
    /// </summary>
    public class ResolvedControl
    {
        public string ControlId { get; set; }
        public string ControlName { get; set; }
        public string DataType { get; set; } = "Text";
    }
}
