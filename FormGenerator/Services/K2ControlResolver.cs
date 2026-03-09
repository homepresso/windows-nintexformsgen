using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using K2SmartObjectGenerator.Utilities;

namespace FormGenerator.Services
{
    /// <summary>
    /// Multi-strategy resolver that maps InfoPath field names/XPaths to K2 control IDs.
    /// Returns null (not random GUIDs) when resolution fails.
    /// Falls back to global ControlMappingService for cross-view resolution.
    /// </summary>
    public class K2ControlResolver
    {
        private readonly Dictionary<string, string> _controlIdMap;
        private readonly Dictionary<string, string> _controlToFieldMap;
        private readonly Dictionary<string, string> _jsonToK2ControlIdMap;

        /// <summary>
        /// When true, allows resolving controls from OTHER views via ControlMappingService.
        /// Set to false when resolving for rules that must reference controls in the current view only
        /// (e.g., InfoPath rules added via K2InfoPathRuleOrchestrator).
        /// Default: true for backward compatibility.
        /// </summary>
        public bool AllowGlobalFallback { get; set; } = true;

        /// <summary>
        /// Optional mapping from InfoPath binding leaf names to K2-matching control names.
        /// E.g., "isRoundTrip" -> "ISROUNDTRIP". Used as a pre-translation step before
        /// other resolution strategies, ensuring InfoPath field references match K2 controls.
        /// </summary>
        public Dictionary<string, string> InfoPathToK2NameMap { get; set; }

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

            // Strategy 0: Pre-translate InfoPath binding name to K2 control name using the map
            // This handles cases where InfoPath uses "isRoundTrip" but K2 control is "ISROUNDTRIP"
            if (InfoPathToK2NameMap != null && InfoPathToK2NameMap.TryGetValue(fieldNameOrXPath, out var k2Name))
            {
                // Try the K2 name first - it's more likely to match
                if (_controlIdMap.TryGetValue(k2Name, out var mappedControlId))
                {
                    Console.WriteLine($"      [RESOLVE via K2NameMap] '{fieldNameOrXPath}' -> K2 name '{k2Name}' -> {mappedControlId}");
                    return new ResolvedControl { ControlId = mappedControlId, ControlName = k2Name };
                }
                // Also try uppercase of the K2 name
                string k2NameUpper = k2Name.ToUpperInvariant();
                if (_controlIdMap.TryGetValue(k2NameUpper, out mappedControlId))
                {
                    Console.WriteLine($"      [RESOLVE via K2NameMap] '{fieldNameOrXPath}' -> K2 name '{k2NameUpper}' -> {mappedControlId}");
                    return new ResolvedControl { ControlId = mappedControlId, ControlName = k2NameUpper };
                }
            }

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

            // Strategy 8: Try with JSON_ prefix (InfoPath CTRL IDs stored with this prefix in controlIdMap)
            var jsonPrefixed = $"JSON_{fieldNameOrXPath}";
            if (_controlIdMap.TryGetValue(jsonPrefixed, out controlId))
            {
                return new ResolvedControl { ControlId = controlId, ControlName = fieldNameOrXPath };
            }
            // Also try uppercase version with JSON_ prefix
            var jsonPrefixedUpper = $"JSON_{upper}";
            if (_controlIdMap.TryGetValue(jsonPrefixedUpper, out controlId))
            {
                return new ResolvedControl { ControlId = controlId, ControlName = fieldNameOrXPath };
            }

            // Strategy 9: Try JSON_ prefix with stripped name
            if (!string.Equals(stripped, fieldNameOrXPath))
            {
                var jsonPrefixedStripped = $"JSON_{stripped}";
                if (_controlIdMap.TryGetValue(jsonPrefixedStripped, out controlId))
                {
                    return new ResolvedControl { ControlId = controlId, ControlName = stripped };
                }
            }

            // Strategy 10: Fallback to global ControlMappingService for cross-view resolution
            // This handles cases where a field exists in another view's controlIdMap
            // (e.g., visibility rules reference 'category' which exists in Table_CTRL243_Item view)
            // ONLY used when AllowGlobalFallback is true - disabled for InfoPath rules that
            // would incorrectly reference controls from other views
            if (AllowGlobalFallback)
            {
                var globalControlId = ResolveFromGlobalMapping(fieldNameOrXPath, stripped, sanitized, upper);
                if (globalControlId != null)
                {
                    return globalControlId;
                }
            }

            // Strategy 11 REMOVED: Fuzzy/suffix matching removed in favor of deterministic binding pipeline.
            // The controlIdMap now contains binding-derived keys, so direct lookups above should succeed.

            // Not found - dump diagnostic info
            Console.WriteLine($"      [RESOLVE FAIL] '{fieldNameOrXPath}' not found. Tried: exact='{fieldNameOrXPath}', stripped='{stripped}', sanitized='{sanitized}', upper='{upper}'");
            Console.WriteLine($"      [RESOLVE FAIL] controlIdMap has {_controlIdMap.Count} entries, jsonToK2Map has {_jsonToK2ControlIdMap.Count} entries, AllowGlobalFallback={AllowGlobalFallback}");
            // Show keys that contain the field name (case-insensitive partial match for debugging)
            var partialMatches = new System.Collections.Generic.List<string>();
            foreach (var key in _controlIdMap.Keys)
            {
                if (key.IndexOf(fieldNameOrXPath, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    fieldNameOrXPath.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    partialMatches.Add(key);
                }
            }
            if (partialMatches.Count > 0)
            {
                Console.WriteLine($"      [RESOLVE FAIL] Partial matches in controlIdMap: [{string.Join(", ", partialMatches)}]");
            }
            return null;
        }

        /// <summary>
        /// Quiet version of Resolve that doesn't log failures. Used for pre-filtering
        /// to check if a control exists in this view before attempting to build a rule.
        /// </summary>
        public ResolvedControl TryResolveQuiet(string fieldNameOrXPath)
        {
            if (string.IsNullOrEmpty(fieldNameOrXPath))
                return null;

            // Try K2 name map translation first
            if (InfoPathToK2NameMap != null && InfoPathToK2NameMap.TryGetValue(fieldNameOrXPath, out var k2Name))
            {
                if (_controlIdMap.TryGetValue(k2Name, out var id))
                    return new ResolvedControl { ControlId = id, ControlName = k2Name };
                string k2Upper = k2Name.ToUpperInvariant();
                if (_controlIdMap.TryGetValue(k2Upper, out id))
                    return new ResolvedControl { ControlId = id, ControlName = k2Upper };
            }

            // Direct match
            if (_controlIdMap.TryGetValue(fieldNameOrXPath, out var controlId))
                return new ResolvedControl { ControlId = controlId, ControlName = fieldNameOrXPath };

            // XPath stripped
            var stripped = StripXPath(fieldNameOrXPath);
            if (!string.Equals(stripped, fieldNameOrXPath) && _controlIdMap.TryGetValue(stripped, out controlId))
                return new ResolvedControl { ControlId = controlId, ControlName = stripped };

            // Sanitized
            var sanitized = SanitizeName(fieldNameOrXPath);
            if (!string.Equals(sanitized, fieldNameOrXPath) && _controlIdMap.TryGetValue(sanitized, out controlId))
                return new ResolvedControl { ControlId = controlId, ControlName = sanitized };

            // Case-insensitive
            foreach (var kvp in _controlIdMap)
            {
                if (string.Equals(kvp.Key, fieldNameOrXPath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(kvp.Key, stripped, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(kvp.Key, sanitized, StringComparison.OrdinalIgnoreCase))
                {
                    return new ResolvedControl { ControlId = kvp.Value, ControlName = kvp.Key };
                }
            }

            // UPPERCASE
            var upper = stripped.ToUpperInvariant();
            if (!string.Equals(upper, stripped) && _controlIdMap.TryGetValue(upper, out controlId))
                return new ResolvedControl { ControlId = controlId, ControlName = upper };

            return null;
        }

        /// <summary>
        /// Resolve a field from the global ControlMappingService across all views.
        /// This is a fallback for when a field is not in the current view's controlIdMap
        /// but exists in another view (e.g., visibility rules referencing fields in other views).
        /// </summary>
        private ResolvedControl ResolveFromGlobalMapping(string fieldName, string stripped, string sanitized, string upper)
        {
            try
            {
                var viewNames = ControlMappingService.GetMappedViewNames();
                if (viewNames == null || viewNames.Count == 0)
                    return null;

                // Try to find the field in any view's controls
                foreach (var viewName in viewNames)
                {
                    var viewControls = ControlMappingService.GetViewControls(viewName);
                    if (viewControls == null)
                        continue;

                    // Try direct match
                    if (viewControls.TryGetValue(fieldName, out var control))
                    {
                        return new ResolvedControl
                        {
                            ControlId = control.ControlId,
                            ControlName = control.ControlName
                        };
                    }

                    // Try stripped match
                    if (viewControls.TryGetValue(stripped, out control))
                    {
                        return new ResolvedControl
                        {
                            ControlId = control.ControlId,
                            ControlName = control.ControlName
                        };
                    }

                    // Try sanitized match
                    if (viewControls.TryGetValue(sanitized, out control))
                    {
                        return new ResolvedControl
                        {
                            ControlId = control.ControlId,
                            ControlName = control.ControlName
                        };
                    }

                    // Try case-insensitive match
                    foreach (var kvp in viewControls)
                    {
                        if (string.Equals(kvp.Key, fieldName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(kvp.Key, stripped, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(kvp.Key, sanitized, StringComparison.OrdinalIgnoreCase))
                        {
                            return new ResolvedControl
                            {
                                ControlId = kvp.Value.ControlId,
                                ControlName = kvp.Value.ControlName
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // If anything goes wrong with global mapping, just continue
                // (don't break the resolution process)
            }

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
