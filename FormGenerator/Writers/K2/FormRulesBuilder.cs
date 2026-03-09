using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Newtonsoft.Json.Linq;
using K2SmartObjectGenerator.Utilities;
using static K2SmartObjectGenerator.ViewGenerator;
using InfoPathFormDefinition = global::FormGenerator.Analyzers.Infopath.InfoPathFormDefinition;

namespace K2SmartObjectGenerator
{
    public class CalculationFieldInfo
    {
        public string CtrlId { get; set; }
        public string Name { get; set; }
        public string Label { get; set; }
        public string Type { get; set; }
        public string OperatorType { get; set; } // null=ListSum, "Minus", "Plus", "Multiply", "Divide"
        public List<SourceFieldInfo> SourceFields { get; set; } = new List<SourceFieldInfo>();
    }

    public class SourceFieldInfo
    {
        public string CtrlId { get; set; }
        public string Name { get; set; }
        public string Label { get; set; }
        public string RepeatingSectionName { get; set; }
    }

    public class ControlInfoForCalculation
    {
        public string ControlId { get; set; }
        public string ControlName { get; set; }
        public string InstanceId { get; set; }
    }

    public class ControlInfoInForm
    {
        public string ControlId { get; set; }
        public string ViewInstanceId { get; set; }
        public string ControlName { get; set; }
        public string ViewName { get; set; }
    }

    public class FormRulesBuilder
    {
        private readonly Dictionary<string, string> _eventDefinitionIds;
        private readonly Dictionary<string, string> _actionDefinitionIds;
        private readonly Dictionary<string, string> _handlerDefinitionIds;

        public FormRulesBuilder()
        {
     
        }

        public void ApplyListItemViewRules(XmlDocument doc, Dictionary<string, ViewPairInfo> viewPairs)
        {
            Console.WriteLine("\n    ========== FORM RULES BUILDER DEBUG START ==========");

            if (viewPairs == null || viewPairs.Count == 0)
            {
                Console.WriteLine("    No view pairs found for rule generation");
                return;
            }

            // Validate the XML document structure
            ValidateFormXmlStructure(doc);

            // Log all registered views in the service
            Console.WriteLine("\n    === Registered Views in ControlMappingService ===");
            var registeredViews = ControlMappingService.GetMappedViewNames();
            foreach (var viewName in registeredViews)
            {
                var controls = ControlMappingService.GetViewControls(viewName);
                Console.WriteLine($"      {viewName}: {controls?.Count ?? 0} controls");
            }
            Console.WriteLine("    === End of Registered Views ===\n");

            XmlElement statesElement = GetOrCreateStatesElement(doc);
            if (statesElement == null)
            {
                Console.WriteLine("    ERROR: Could not find or create States element");
                return;
            }

            XmlElement baseState = GetBaseState(statesElement);
            if (baseState == null)
            {
                Console.WriteLine("    ERROR: Could not find base state");
                return;
            }

            XmlElement eventsElement = GetOrCreateEventsElement(baseState);

            // Get form information
            XmlElement formElement = (XmlElement)doc.GetElementsByTagName("Form")[0];
            string formId = formElement?.GetAttribute("ID");
            string formName = formElement?.GetElementsByTagName("Name")[0]?.InnerText;

            Console.WriteLine($"\n    === Form Information ===");
            Console.WriteLine($"      Form ID: {formId}");
            Console.WriteLine($"      Form Name: {formName}");

            if (string.IsNullOrEmpty(formId))
            {
                formId = Guid.NewGuid().ToString();
                Console.WriteLine($"      Generated new Form ID: {formId}");
            }

            var trackedButtons = ButtonTracker.GetAllButtons();
            Console.WriteLine($"\n    === Button Tracking ===");
            Console.WriteLine($"      Found {trackedButtons.Count} tracked button sets");

            foreach (var viewPair in viewPairs.Values)
            {
                viewPair.FormId = formId;
                viewPair.FormName = formName;

                Console.WriteLine($"\n    ================================================");
                Console.WriteLine($"    === Processing View Pair: {viewPair.SectionName} ===");
                Console.WriteLine($"    ================================================");

                // Debug view pair details
                DebugViewPairInfo(viewPair);

                // Attempt to retrieve control mappings from the ControlMappingService
                var itemControls = ControlMappingService.GetViewControls(viewPair.ItemViewName);
                var listControls = ControlMappingService.GetViewControls(viewPair.ListViewName);

                Console.WriteLine($"\n      Control Mappings Retrieved:");
                Console.WriteLine($"        Item View Controls: {(itemControls != null ? itemControls.Count.ToString() : "NULL")}");
                Console.WriteLine($"        List View Controls: {(listControls != null ? listControls.Count.ToString() : "NULL")}");

                if (itemControls != null && listControls != null)
                {
                    PopulateFieldMappingsFromViewData(viewPair, itemControls, listControls);
                }
                else
                {
                    Console.WriteLine($"      WARNING: Could not retrieve control mappings");

                    // Try case-insensitive search as fallback
                    Console.WriteLine($"      Attempting case-insensitive search...");
                    foreach (var registeredView in registeredViews)
                    {
                        if (registeredView.Equals(viewPair.ItemViewName, StringComparison.OrdinalIgnoreCase))
                        {
                            Console.WriteLine($"        Found item view with different case: '{registeredView}'");
                            itemControls = ControlMappingService.GetViewControls(registeredView);
                        }
                        if (registeredView.Equals(viewPair.ListViewName, StringComparison.OrdinalIgnoreCase))
                        {
                            Console.WriteLine($"        Found list view with different case: '{registeredView}'");
                            listControls = ControlMappingService.GetViewControls(registeredView);
                        }
                    }

                    if (itemControls != null && listControls != null)
                    {
                        Console.WriteLine($"      Retry successful!");
                        PopulateFieldMappingsFromViewData(viewPair, itemControls, listControls);
                    }
                }

                var listViewButtons = ButtonTracker.GetViewButtons(viewPair.ListViewName);
                var itemViewButtons = ButtonTracker.GetViewButtons(viewPair.ItemViewName);

                Console.WriteLine($"\n      === Button Information ===");
                Console.WriteLine($"        List View Buttons: {(listViewButtons != null ? "Found" : "Not Found")}");
                if (listViewButtons != null)
                {
                    Console.WriteLine($"          Add Button ID: {listViewButtons.AddButtonId ?? "NULL"}");
                    Console.WriteLine($"          Add Button Name: {listViewButtons.AddButtonName ?? "NULL"}");
                }
                Console.WriteLine($"        Item View Buttons: {(itemViewButtons != null ? "Found" : "Not Found")}");
                if (itemViewButtons != null)
                {
                    Console.WriteLine($"          Add Button ID: {itemViewButtons.AddButtonId ?? "NULL"}");
                    Console.WriteLine($"          Add Button Name: {itemViewButtons.AddButtonName ?? "NULL"}");
                    Console.WriteLine($"          Cancel Button ID: {itemViewButtons.CancelButtonId ?? "NULL"}");
                    Console.WriteLine($"          Cancel Button Name: {itemViewButtons.CancelButtonName ?? "NULL"}");
                }

                // Rule 1: List View Add Toolbar Button
                if (listViewButtons != null && !string.IsNullOrEmpty(listViewButtons.AddButtonId))
                {
                    Console.WriteLine($"\n      === Creating Rule 1: List → Item Navigation ===");
                    AddListViewAddToolbarButtonRule(doc, eventsElement, viewPair,
                        listViewButtons.AddButtonId, listViewButtons.AddButtonName ?? "Add ToolBar Button");
                }
                else
                {
                    Console.WriteLine($"\n      === Skipping Rule 1: No Add button found for list view ===");
                }

                // Rule 2: Item View Add Button with comprehensive actions
                if (itemViewButtons != null && !string.IsNullOrEmpty(itemViewButtons.AddButtonId))
                {
                    Console.WriteLine($"\n      === Creating Rule 2: Item → List with Data Transfer ===");

                    // Log the field mappings before creating the rule
                    if (viewPair.FieldMappings != null && viewPair.FieldMappings.Count > 0)
                    {
                        Console.WriteLine($"        Field mappings to be used:");
                        foreach (var mapping in viewPair.FieldMappings)
                        {
                            Console.WriteLine($"          {mapping.FieldName}: {mapping.ItemControlName} -> {mapping.ListControlName}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"        WARNING: No field mappings available for data transfer!");
                    }

                    AddComprehensiveItemViewAddButtonRule(doc, eventsElement, viewPair,
                        itemViewButtons.AddButtonId, itemViewButtons.AddButtonName ?? "Add Button");
                }
                else
                {
                    Console.WriteLine($"\n      === Skipping Rule 2: No Add button found for item view ===");
                }

                // Rule 3: Item View Cancel Button
                if (itemViewButtons != null && !string.IsNullOrEmpty(itemViewButtons.CancelButtonId))
                {
                    Console.WriteLine($"\n      === Creating Rule 3: Cancel Navigation ===");
                    AddItemViewCancelButtonRule(doc, eventsElement, viewPair,
                        itemViewButtons.CancelButtonId, itemViewButtons.CancelButtonName ?? "Cancel");
                }
                else
                {
                    Console.WriteLine($"\n      === Skipping Rule 3: No Cancel button found for item view ===");
                }

                // Rule 4: Delete ToolBar Button on List View → RemoveItem
                if (listViewButtons != null && !string.IsNullOrEmpty(listViewButtons.DeleteButtonId))
                {
                    Console.WriteLine($"\n      === Creating Rule 4: Delete ToolBar Button → RemoveItem ===");
                    AddDeleteToolbarButtonRule(doc, eventsElement, viewPair,
                        listViewButtons.DeleteButtonId, listViewButtons.DeleteButtonName ?? "Delete ToolBar Button");
                }
                else
                {
                    Console.WriteLine($"\n      === Skipping Rule 4: No Delete button found for list view ===");
                }
            }

            // Final validation of created rules
            ValidateCreatedRules(doc);

            Console.WriteLine($"\n    === Successfully added rules for {viewPairs.Count} view pairs ===");
            Console.WriteLine("    ========== FORM RULES BUILDER DEBUG END ==========\n");
        }

        public void ApplyCalculationRules(XmlDocument doc, JObject formData, InfoPathFormDefinition infoPathFormDef = null)
        {
            Console.WriteLine("\n    ========== CALCULATION RULES GENERATION START ==========");

            try
            {
                // Analyze JSON + InfoPath data to identify calculation fields and their sources
                var calculationFields = AnalyzeCalculationFields(formData, infoPathFormDef);

                if (calculationFields.Count == 0)
                {
                    Console.WriteLine("    No calculation fields found in form data");
                    return;
                }

                Console.WriteLine($"    Found {calculationFields.Count} calculation fields");

                // Get or create Expressions element in the Form
                XmlElement formElement = (XmlElement)doc.GetElementsByTagName("Form")[0];
                XmlElement expressionsElement = GetOrCreateExpressionsElement(doc, formElement);

                // Generate one expression per calculation field
                foreach (var calcField in calculationFields)
                {
                    GenerateCalculationExpressionOnly(doc, expressionsElement, calcField);
                    Console.WriteLine($"    Generated expression for {calcField.Name} (operator: {calcField.OperatorType ?? "ListSum"}, sources: {string.Join(",", calcField.SourceFields.Select(sf => sf.Name))})");
                }

                // Debug: Save form XML with expressions to file
                try
                {
                    string debugPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "form_with_expressions_debug.xml");
                    doc.Save(debugPath);
                    Console.WriteLine($"    DEBUG: Saved form XML with expressions to {debugPath}");
                }
                catch (Exception saveEx)
                {
                    Console.WriteLine($"    DEBUG: Could not save form XML: {saveEx.Message}");
                }

                Console.WriteLine("    Successfully generated calculation expressions (expressions only)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    ERROR generating calculation rules: {ex.Message}");
            }

            Console.WriteLine("    ========== CALCULATION RULES GENERATION END ==========\n");
        }

        private List<CalculationFieldInfo> AnalyzeCalculationFields(JObject formData, InfoPathFormDefinition infoPathFormDef)
        {
            var calculationFields = new List<CalculationFieldInfo>();

            try
            {
                // Build a lookup of calculation expressions from InfoPath ConditionalRules (Type="Calculation")
                var calcExpressions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                Console.WriteLine($"    InfoPathFormDef is {(infoPathFormDef == null ? "NULL" : "available")}");
                if (infoPathFormDef != null)
                {
                    Console.WriteLine($"    InfoPathFormDef.ConditionalRules has {infoPathFormDef.ConditionalRules?.Count ?? 0} rules");
                    if (infoPathFormDef.ConditionalRules != null)
                    {
                        foreach (var rule in infoPathFormDef.ConditionalRules)
                        {
                            Console.WriteLine($"    ConditionalRule: Type='{rule.Type}', Target='{rule.TargetField}', Condition='{rule.Condition}', Action='{rule.Action}'");
                            if (string.Equals(rule.Type, "Calculation", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(rule.TargetField) && !string.IsNullOrEmpty(rule.Condition))
                            {
                                // TargetField may be an XPath like "my:subTotal" - extract just the field name
                                string targetName = ExtractFieldNameFromXPath(rule.TargetField);
                                if (!string.IsNullOrEmpty(targetName))
                                {
                                    calcExpressions[targetName] = rule.Condition;
                                    Console.WriteLine($"    CalcRule: target='{targetName}' expression='{rule.Condition}'");
                                }
                            }
                        }
                    }
                }
                Console.WriteLine($"    Found {calcExpressions.Count} calculation expressions from InfoPath rules");

                var fields = GetFieldsFromFormData(formData);
                Console.WriteLine($"    Found {fields?.Count ?? 0} total fields in form data");

                // Collect all field names for reference (needed for dynamic source resolution)
                var allFieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var repeatingFieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (fields != null)
                {
                    foreach (JObject f in fields)
                    {
                        string fn = f["Name"]?.ToString();
                        if (!string.IsNullOrEmpty(fn)) allFieldNames.Add(fn);

                        var rsi = f["RepeatingSectionInfo"] as JObject;
                        if (rsi?["IsInRepeatingSection"]?.ToObject<bool>() == true && !string.IsNullOrEmpty(fn))
                            repeatingFieldNames.Add(fn);
                    }
                }

                if (fields != null)
                {
                    foreach (JObject field in fields)
                    {
                        string fieldName = field["Name"]?.ToString();
                        string fieldType = field["Type"]?.ToString();
                        var dataOptions = field["DataOptions"] as JObject;
                        bool isDisabled = dataOptions?["disableEditing"]?.ToString() == "yes";

                        // Check if this field has a calculation expression in InfoPath DataColumn.DefaultValue
                        bool hasCalcExpression = !string.IsNullOrEmpty(fieldName) && calcExpressions.ContainsKey(fieldName);

                        Console.WriteLine($"    Checking field: {fieldName} (Type: {fieldType}, Disabled: {isDisabled}, HasCalcExpr: {hasCalcExpression})");

                        // A calculation field is a TextField that either has disableEditing=yes OR has a calculation expression
                        if (fieldType == "TextField" && (isDisabled || hasCalcExpression))
                        {
                            var calcField = new CalculationFieldInfo
                            {
                                CtrlId = field["CtrlId"]?.ToString(),
                                Name = fieldName,
                                Label = field["Label"]?.ToString(),
                                Type = fieldType
                            };

                            // Find source fields dynamically from calculation expressions
                            calcField.SourceFields = FindSourceFieldsDynamic(
                                formData, calcField, calcExpressions, allFieldNames, repeatingFieldNames);

                            if (calcField.SourceFields.Count > 0)
                            {
                                calculationFields.Add(calcField);
                                Console.WriteLine($"    Identified calculation field: {calcField.Name} (ID: {calcField.CtrlId}) " +
                                    $"operator: {calcField.OperatorType ?? "ListSum"}, sources: {string.Join(", ", calcField.SourceFields.Select(sf => sf.Name))}");
                            }
                            else
                            {
                                Console.WriteLine($"    Skipping calculation field '{fieldName}': no source fields resolved");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    ERROR analyzing calculation fields: {ex.Message}");
            }

            return calculationFields;
        }

        private JArray GetFieldsFromFormData(JObject formData)
        {
            try
            {
                // Navigate through the JSON structure to find controls
                // Structure: Root -> FormDefinition -> Views -> Controls
                var rootKeys = formData.Properties().Select(p => p.Name).ToList();

                foreach (var rootKey in rootKeys)
                {
                    var rootValue = formData[rootKey] as JObject;
                    if (rootValue != null)
                    {
                        var formDefinition = rootValue["FormDefinition"] as JObject;
                        if (formDefinition != null)
                        {
                            var views = formDefinition["Views"] as JArray;
                            if (views != null && views.Count > 0)
                            {
                                // Get the first view's controls
                                var firstView = views[0] as JObject;
                                if (firstView != null)
                                {
                                    var controls = firstView["Controls"] as JArray;
                                    if (controls != null)
                                    {
                                        Console.WriteLine($"      Found {controls.Count} controls in first view");
                                        return controls;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    ERROR getting fields from form data: {ex.Message}");
            }

            return null;
        }

        private bool IsCalculationField(JObject field)
        {
            try
            {
                string type = field["Type"]?.ToString();

                // Must be a TextField (data input control)
                if (type != "TextField") return false;

                // Primary signal: disableEditing = yes (InfoPath marks calculated fields as read-only)
                var dataOptions = field["DataOptions"] as JObject;
                bool isDisabled = dataOptions?["disableEditing"]?.ToString() == "yes";

                return isDisabled;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Dynamically finds source fields for a calculation by parsing the actual XPath expression
        /// from the InfoPath DataColumn's DefaultValue. No hardcoded field name patterns.
        /// </summary>
        private List<SourceFieldInfo> FindSourceFieldsDynamic(JObject formData, CalculationFieldInfo calcField,
            Dictionary<string, string> calcExpressions, HashSet<string> allFieldNames, HashSet<string> repeatingFieldNames)
        {
            var sourceFields = new List<SourceFieldInfo>();

            try
            {
                var fields = GetFieldsFromFormData(formData);
                if (fields == null) return sourceFields;

                // Build a lookup from field name to JObject for quick access
                var fieldLookup = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
                foreach (JObject f in fields)
                {
                    string fn = f["Name"]?.ToString();
                    if (!string.IsNullOrEmpty(fn) && !fieldLookup.ContainsKey(fn))
                        fieldLookup[fn] = f;
                }

                // Try to find the calculation expression from DataColumn DefaultValue
                string expression = null;
                if (calcExpressions.TryGetValue(calcField.Name, out var expr))
                    expression = expr;

                if (!string.IsNullOrEmpty(expression))
                {
                    Console.WriteLine($"      Parsing calculation expression for '{calcField.Name}': {expression}");
                    ParseExpressionForSources(expression, calcField, sourceFields, fieldLookup, repeatingFieldNames);
                }
                else
                {
                    Console.WriteLine($"      No DefaultValue expression found for '{calcField.Name}'");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    ERROR finding source fields: {ex.Message}");
            }

            return sourceFields;
        }

        /// <summary>
        /// Parses an XPath calculation expression to extract source field references and operator type.
        /// Handles: sum(field), field1 + field2, field1 - field2, etc.
        /// </summary>
        private void ParseExpressionForSources(string expression, CalculationFieldInfo calcField,
            List<SourceFieldInfo> sourceFields, Dictionary<string, JObject> fieldLookup,
            HashSet<string> repeatingFieldNames)
        {
            // Strip formatting wrappers: xdFormatting:formatString(expr, 'format') → expr
            string cleanExpr = StripFormattingWrappers(expression);

            // Detect aggregate functions: sum(...), count(...), avg(...)
            var aggregateMatch = System.Text.RegularExpressions.Regex.Match(cleanExpr,
                @"^(sum|count|avg|min|max)\s*\((.+)\)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (aggregateMatch.Success)
            {
                // Aggregate function — extract the field being aggregated
                string innerPath = aggregateMatch.Groups[2].Value;
                string fieldName = ExtractFieldNameFromXPath(innerPath);

                if (!string.IsNullOrEmpty(fieldName))
                {
                    // For aggregates like sum(), find ALL instances of this field in repeating sections
                    AddSourceFieldByName(fieldName, sourceFields, fieldLookup);
                    // ListSum is the default operator (no OperatorType set)
                    Console.WriteLine($"      Aggregate {aggregateMatch.Groups[1].Value}({fieldName}) → ListSum");
                }
                return;
            }

            // Detect arithmetic: field1 op field2
            // Split by arithmetic operators with whitespace
            var arithmeticOps = new[] { " + ", " - ", " * ", " div " };
            string detectedOp = null;
            string[] parts = null;

            foreach (var op in arithmeticOps)
            {
                if (cleanExpr.Contains(op))
                {
                    parts = cleanExpr.Split(new[] { op }, StringSplitOptions.RemoveEmptyEntries);
                    detectedOp = op.Trim();
                    break;
                }
            }

            if (parts != null && parts.Length >= 2)
            {
                // Map XPath operators to K2 operator element names
                calcField.OperatorType = detectedOp switch
                {
                    "+" => "Plus",
                    "-" => "Minus",
                    "*" => "Multiply",
                    "div" => "Divide",
                    _ => "Plus"
                };

                foreach (var part in parts)
                {
                    string fieldName = ExtractFieldNameFromXPath(part.Trim());
                    if (!string.IsNullOrEmpty(fieldName))
                    {
                        AddSourceFieldByName(fieldName, sourceFields, fieldLookup);
                    }
                }
                Console.WriteLine($"      Arithmetic: {string.Join($" {detectedOp} ", parts.Select(p => p.Trim()))} → {calcField.OperatorType}");
                return;
            }

            // Detect xdMath:Min/Max/Eval aggregate patterns
            // e.g., ../my:items/my:item/my:date[translate(...) = xdMath:Min(xdMath:Eval(...))]
            var xdMathMatch = System.Text.RegularExpressions.Regex.Match(cleanExpr,
                @"xdMath:(Min|Max|Avg|Eval)\s*\(", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (xdMathMatch.Success)
            {
                // Extract all field references from the expression (any my:fieldName patterns)
                var fieldRefs = System.Text.RegularExpressions.Regex.Matches(cleanExpr,
                    @"my:([\w]+)(?:\s*[,\[\])\s]|$)");
                HashSet<string> addedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (System.Text.RegularExpressions.Match fieldRef in fieldRefs)
                {
                    string refFieldName = fieldRef.Groups[1].Value;
                    if (!addedFields.Contains(refFieldName))
                    {
                        AddSourceFieldByName(refFieldName, sourceFields, fieldLookup);
                        addedFields.Add(refFieldName);
                    }
                }

                string funcName = xdMathMatch.Groups[1].Value;
                if (funcName.Equals("Min", StringComparison.OrdinalIgnoreCase))
                    calcField.OperatorType = null; // Will need special handling as aggregate Min
                else if (funcName.Equals("Max", StringComparison.OrdinalIgnoreCase))
                    calcField.OperatorType = null; // Will need special handling as aggregate Max

                Console.WriteLine($"      xdMath:{funcName} aggregate expression → extracted {addedFields.Count} source field(s): {string.Join(", ", addedFields)}");
                return;
            }

            // Single field reference (assignment)
            string singleField = ExtractFieldNameFromXPath(cleanExpr);
            if (!string.IsNullOrEmpty(singleField))
            {
                AddSourceFieldByName(singleField, sourceFields, fieldLookup);
                Console.WriteLine($"      Single field reference: {singleField}");
            }
        }

        /// <summary>
        /// Strips formatting wrapper functions to get the core calculation expression.
        /// e.g., xdFormatting:formatString(sum(amount), '$#,###.00') → sum(amount)
        /// </summary>
        private string StripFormattingWrappers(string expression)
        {
            var result = expression.Trim();

            // Strip xdFormatting:formatString(expr, 'format')
            var fmtMatch = System.Text.RegularExpressions.Regex.Match(result,
                @"xdFormatting:formatString\s*\((.+?)\s*,\s*['""].*?['""]\s*\)$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (fmtMatch.Success)
                result = fmtMatch.Groups[1].Value.Trim();

            // Strip format-number(expr, 'format')
            var numFmtMatch = System.Text.RegularExpressions.Regex.Match(result,
                @"format-number\s*\((.+?)\s*,\s*['""].*?['""]\s*\)$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (numFmtMatch.Success)
                result = numFmtMatch.Groups[1].Value.Trim();

            // Strip xdMath:Nz() wrappers — replace xdMath:Nz(expr) with just expr
            // This handles e.g. sum(xdMath:Nz(../my:item/my:amount)) → sum(../my:item/my:amount)
            // and xdMath:Nz(../my:subTotal) - xdMath:Nz(../my:cashAdvance) → ../my:subTotal - ../my:cashAdvance
            result = System.Text.RegularExpressions.Regex.Replace(result,
                @"xdMath:Nz\(([^)]+)\)", "$1");

            return result;
        }

        /// <summary>
        /// Extracts the leaf field name from an XPath path.
        /// ../my:items/my:amount → amount, my:subTotal → subTotal
        /// </summary>
        private string ExtractFieldNameFromXPath(string xpath)
        {
            if (string.IsNullOrEmpty(xpath)) return null;

            var cleaned = xpath.Trim();

            // Remove quotes
            cleaned = cleaned.Trim('\'', '"');

            // Skip numeric literals
            if (double.TryParse(cleaned, out _)) return null;

            // Take the last path segment
            if (cleaned.Contains("/"))
            {
                var segments = cleaned.Split('/');
                cleaned = segments[segments.Length - 1];
            }

            // Remove namespace prefix (my:)
            cleaned = cleaned.Replace("my:", "");

            // Remove predicates like [position()] - handle nested brackets by counting depth
            int bracketStart = cleaned.IndexOf('[');
            if (bracketStart >= 0)
            {
                cleaned = cleaned.Substring(0, bracketStart);
            }

            return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned.Trim();
        }

        /// <summary>
        /// Adds a source field by name, looking it up in the field data.
        /// Tries exact match first, then case-insensitive.
        /// </summary>
        private void AddSourceFieldByName(string fieldName, List<SourceFieldInfo> sourceFields,
            Dictionary<string, JObject> fieldLookup)
        {
            JObject field = null;

            // Try exact match
            if (fieldLookup.TryGetValue(fieldName, out field))
            {
                // Found
            }
            // Try uppercase (K2 convention)
            else if (fieldLookup.TryGetValue(fieldName.ToUpperInvariant(), out field))
            {
                // Found
            }
            else
            {
                Console.WriteLine($"        Could not find field '{fieldName}' in form data");
                return;
            }

            var rsi = field["RepeatingSectionInfo"] as JObject;

            sourceFields.Add(new SourceFieldInfo
            {
                CtrlId = field["CtrlId"]?.ToString(),
                Name = field["Name"]?.ToString(),
                Label = field["Label"]?.ToString(),
                RepeatingSectionName = rsi?["RepeatingSectionName"]?.ToString()
            });

            Console.WriteLine($"      Found source field: {field["Name"]} (ID: {field["CtrlId"]})");
        }

        private void GenerateCalculationExpressionOnly(XmlDocument doc, XmlElement expressionsElement, CalculationFieldInfo calcField)
        {
            try
            {
                Console.WriteLine($"    Generating calculation expression for: {calcField.Name}");

                string expressionId = Guid.NewGuid().ToString();

                XmlElement expression = doc.CreateElement("Expression");
                expression.SetAttribute("ID", expressionId);

                bool hasOperands;
                if (!string.IsNullOrEmpty(calcField.OperatorType))
                {
                    hasOperands = GenerateArithmeticExpression(doc, expression, calcField);
                }
                else
                {
                    hasOperands = GenerateListSumExpression(doc, expression, calcField);
                }

                // Only add expression if it has operands — empty operators cause K2 runtime errors
                if (!hasOperands)
                {
                    Console.WriteLine($"      Skipping expression for {calcField.Name}: no operands resolved");
                    return;
                }

                expressionsElement.AppendChild(expression);

                // Modify the target control to be a DataLabel with ExpressionID
                ModifyTargetControlForExpression(doc, calcField, expressionId);

                Console.WriteLine($"      Successfully created expression for {calcField.Name}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    ERROR generating calculation expression for {calcField.Name}: {ex.Message}");
            }
        }

        /// <summary>Returns true if at least 2 operands were resolved (required for binary operators).</summary>
        private bool GenerateArithmeticExpression(XmlDocument doc, XmlElement expression, CalculationFieldInfo calcField)
        {
            string opSymbol = calcField.OperatorType == "Minus" ? "-" : "+";

            XmlElement nameElement = doc.CreateElement("Name");
            nameElement.InnerText = $"{string.Join($" {opSymbol} ", calcField.SourceFields.Select(sf => sf.Name))} Calculation";
            expression.AppendChild(nameElement);

            XmlElement operatorElement = doc.CreateElement(calcField.OperatorType);
            int operandCount = 0;
            var resolvedDisplayNames = new List<string>();

            foreach (var sourceField in calcField.SourceFields)
            {
                var controlInfo = FindControlByName(doc, sourceField.Name);
                if (controlInfo != null)
                {
                    // Build display name: use ControlName if it's meaningful, otherwise use field name + " TextBox"
                    string controlDisplayName = controlInfo.ControlName;
                    if (string.IsNullOrEmpty(controlDisplayName) || controlDisplayName == "ControlName")
                        controlDisplayName = $"{sourceField.Name} TextBox";

                    XmlElement itemElement = doc.CreateElement("Item");
                    itemElement.SetAttribute("SourceType", "Control");
                    itemElement.SetAttribute("SourceID", controlInfo.ControlId);
                    itemElement.SetAttribute("SourceInstanceID", controlInfo.ViewInstanceId);
                    itemElement.SetAttribute("Type", "TextBox");
                    itemElement.SetAttribute("DataType", "Text");
                    itemElement.SetAttribute("SourceName", controlDisplayName);
                    itemElement.SetAttribute("SourceDisplayName", controlDisplayName);
                    itemElement.SetAttribute("DisplayPath", $"{controlInfo.ViewName} - Controls - {controlDisplayName}");

                    var displayChild = doc.CreateElement("Display");
                    displayChild.InnerText = controlDisplayName;
                    itemElement.AppendChild(displayChild);

                    operatorElement.AppendChild(itemElement);
                    operandCount++;
                    resolvedDisplayNames.Add(controlDisplayName);
                    Console.WriteLine($"        Added {calcField.OperatorType} source: {controlDisplayName} (Control: {controlInfo.ControlId}, View: {controlInfo.ViewInstanceId})");
                }
                else
                {
                    Console.WriteLine($"        WARNING: Could not find control for field {sourceField.Name} in form XML");
                }
            }

            if (operandCount < 2)
            {
                Console.WriteLine($"        ERROR: Binary operator {calcField.OperatorType} needs 2 operands, got {operandCount}");
                return false;
            }

            expression.AppendChild(operatorElement);

            var exprDisplay = doc.CreateElement("Display");
            exprDisplay.InnerText = string.Join($" {opSymbol} ", resolvedDisplayNames);
            expression.AppendChild(exprDisplay);

            return true;
        }

        /// <summary>Returns true if at least 1 operand was resolved.</summary>
        private bool GenerateListSumExpression(XmlDocument doc, XmlElement expression, CalculationFieldInfo calcField)
        {
            string fieldLabel = calcField.SourceFields[0].Label ?? calcField.SourceFields[0].Name;

            XmlElement nameElement = doc.CreateElement("Name");
            nameElement.InnerText = $"Sum of all {calcField.SourceFields[0].Name} fields";
            expression.AppendChild(nameElement);

            XmlElement listSumElement = doc.CreateElement("ListSum");

            // For ListSum, find all instances of the source field across views
            var fieldGroups = calcField.SourceFields.GroupBy(sf => sf.Name).ToList();

            foreach (var fieldGroup in fieldGroups)
            {
                string fieldName = fieldGroup.Key;

                // First try parameter-based search (existing method)
                var allControlInfos = FindAllControlsInFormXml(doc, fieldName);

                // Fallback: search by control name directly
                if (allControlInfos.Count == 0)
                {
                    var directControl = FindControlByName(doc, fieldName);
                    if (directControl != null)
                    {
                        allControlInfos.Add(new ControlInfoInForm
                        {
                            ControlId = directControl.ControlId,
                            ViewInstanceId = directControl.ViewInstanceId,
                            ControlName = fieldName
                        });
                    }
                }

                if (allControlInfos.Count > 0)
                {
                    foreach (var controlInfo in allControlInfos)
                    {
                        // Use actual display name if available, otherwise construct one
                        string controlDisplayName = controlInfo.ControlName;
                        if (string.IsNullOrEmpty(controlDisplayName) || controlDisplayName == "ControlName" || controlDisplayName == fieldName)
                            controlDisplayName = $"{fieldName} TextBox";

                        XmlElement itemElement = doc.CreateElement("Item");
                        itemElement.SetAttribute("SourceType", "Control");
                        itemElement.SetAttribute("SourceInstanceID", controlInfo.ViewInstanceId ?? "");
                        itemElement.SetAttribute("SourceID", controlInfo.ControlId);
                        itemElement.SetAttribute("SourceName", controlDisplayName);
                        itemElement.SetAttribute("SourceDisplayName", controlDisplayName);
                        itemElement.SetAttribute("Type", "TextBox");
                        itemElement.SetAttribute("DataType", "Text");
                        itemElement.SetAttribute("DisplayPath", $"{controlInfo.ViewName ?? ""} - Controls - {controlDisplayName}");

                        var displayChild = doc.CreateElement("Display");
                        displayChild.InnerText = controlDisplayName;
                        itemElement.AppendChild(displayChild);

                        listSumElement.AppendChild(itemElement);
                        Console.WriteLine($"        Added ListSum source: {controlDisplayName} (Control: {controlInfo.ControlId}, View: {controlInfo.ViewName}, ViewInstance: {controlInfo.ViewInstanceId})");
                    }
                }
                else
                {
                    Console.WriteLine($"        WARNING: Could not find any controls for field {fieldName} in form XML");
                }
            }

            if (!listSumElement.HasChildNodes)
            {
                Console.WriteLine($"        ERROR: ListSum has no operands");
                return false;
            }

            expression.AppendChild(listSumElement);

            var exprDisplay = doc.CreateElement("Display");
            exprDisplay.InnerText = $"List Sum( {fieldLabel} TextBox )";
            expression.AppendChild(exprDisplay);

            return true;
        }

        /// <summary>
        /// Modifies the target control to be a DataLabel with ExpressionID.
        /// K2 pattern: calculated fields display as DataLabels bound to expressions.
        /// </summary>
        private void ModifyTargetControlForExpression(XmlDocument doc, CalculationFieldInfo calcField, string expressionId)
        {
            // Find the control via ControlMappingService first, then fallback to parameter search
            string targetControlId = null;
            var controlInfo = FindControlByName(doc, calcField.Name);
            if (controlInfo != null)
            {
                targetControlId = controlInfo.ControlId;
            }
            else
            {
                var controlInfos = FindAllControlsInFormXml(doc, calcField.Name);
                if (controlInfos.Count > 0)
                    targetControlId = controlInfos[0].ControlId;
            }

            if (string.IsNullOrEmpty(targetControlId))
            {
                Console.WriteLine($"        WARNING: Could not find target control {calcField.Name} to set ExpressionID");
                return;
            }

            // Find the actual Control element and modify it
            var controlElements = doc.GetElementsByTagName("Control");
            foreach (XmlElement ctrl in controlElements)
            {
                if (ctrl.GetAttribute("ID") == targetControlId)
                {
                    ctrl.SetAttribute("Type", "DataLabel");
                    ctrl.SetAttribute("ExpressionID", expressionId);

                    // Add ControlExpression property
                    var propsElement = ctrl.SelectSingleNode("Properties") as XmlElement;
                    if (propsElement == null)
                    {
                        propsElement = doc.CreateElement("Properties");
                        ctrl.AppendChild(propsElement);
                    }

                    var exprProp = doc.CreateElement("Property");
                    XmlHelper.AddElement(doc, exprProp, "Name", "ControlExpression");
                    XmlHelper.AddElement(doc, exprProp, "Value", expressionId);
                    propsElement.AppendChild(exprProp);

                    var literalProp = doc.CreateElement("Property");
                    XmlHelper.AddElement(doc, literalProp, "Name", "LiteralVal");
                    XmlHelper.AddElement(doc, literalProp, "Value", "false");
                    propsElement.AppendChild(literalProp);

                    Console.WriteLine($"        Modified target control {calcField.Name} to DataLabel with ExpressionID");
                    break;
                }
            }
        }

// Removed transfer action methods - expressions only
        private XmlElement GetOrCreateExpressionsElement(XmlDocument doc, XmlElement formElement)
        {
            XmlElement expressionsElement = formElement.SelectSingleNode("Expressions") as XmlElement;
            if (expressionsElement == null)
            {
                expressionsElement = doc.CreateElement("Expressions");
                // Insert before Views element if it exists
                XmlElement viewsElement = formElement.SelectSingleNode("Views") as XmlElement;
                if (viewsElement != null)
                {
                    formElement.InsertBefore(expressionsElement, viewsElement);
                }
                else
                {
                    formElement.AppendChild(expressionsElement);
                }
            }
            return expressionsElement;
        }

        private void ValidateFormXmlStructure(XmlDocument doc)
        {
            Console.WriteLine("\n    === Validating Form XML Structure ===");

            // Check for Form element
            XmlNodeList formNodes = doc.GetElementsByTagName("Form");
            Console.WriteLine($"      Form elements found: {formNodes.Count}");

            if (formNodes.Count > 0)
            {
                XmlElement form = (XmlElement)formNodes[0];
                Console.WriteLine($"        Form has ID: {!string.IsNullOrEmpty(form.GetAttribute("ID"))}");

                // Check for Views section
                XmlNodeList viewsNodes = form.GetElementsByTagName("Views");
                Console.WriteLine($"        Views sections found: {viewsNodes.Count}");

                if (viewsNodes.Count > 0)
                {
                    XmlElement views = (XmlElement)viewsNodes[0];
                    XmlNodeList viewNodes = views.GetElementsByTagName("View");
                    Console.WriteLine($"        Individual views found: {viewNodes.Count}");

                    foreach (XmlElement view in viewNodes)
                    {
                        string viewId = view.GetAttribute("ID");
                        string instanceId = view.GetAttribute("InstanceID");
                        XmlNode nameNode = view.SelectSingleNode("Name");
                        string viewName = nameNode?.InnerText ?? "Unknown";

                        Console.WriteLine($"          View: {viewName}");
                        Console.WriteLine($"            ID: {viewId}");
                        Console.WriteLine($"            InstanceID: {instanceId}");
                    }
                }
            }
        }

        private void DebugViewPairInfo(ViewPairInfo viewPair)
        {
            Console.WriteLine($"\n      === View Pair Debug Info ===");
            Console.WriteLine($"        Section Name: {viewPair.SectionName}");
            Console.WriteLine($"        List View Name: {viewPair.ListViewName}");
            Console.WriteLine($"        List View ID: {viewPair.ListViewId ?? "NULL"}");
            Console.WriteLine($"        List View Instance ID: {viewPair.ListViewInstanceId ?? "NULL"}");
            Console.WriteLine($"        Item View Name: {viewPair.ItemViewName}");
            Console.WriteLine($"        Item View ID: {viewPair.ItemViewId ?? "NULL"}");
            Console.WriteLine($"        Item View Instance ID: {viewPair.ItemViewInstanceId ?? "NULL"}");
            Console.WriteLine($"        Area GUID: {viewPair.AreaGuid ?? "NULL"}");
            Console.WriteLine($"        Form Name: {viewPair.FormName ?? "NULL"}");
            Console.WriteLine($"        Form ID: {viewPair.FormId ?? "NULL"}");
        }

        private void ValidateCreatedRules(XmlDocument doc)
        {
            Console.WriteLine("\n    === Validating Created Rules ===");

            XmlNodeList eventNodes = doc.SelectNodes("//Event");
            Console.WriteLine($"      Total events created: {eventNodes.Count}");

            foreach (XmlElement eventElement in eventNodes)
            {
                string eventId = eventElement.GetAttribute("ID");
                string sourceName = eventElement.GetAttribute("SourceName");
                string sourceType = eventElement.GetAttribute("SourceType");
                string instanceId = eventElement.GetAttribute("InstanceID");

                Console.WriteLine($"\n      Event: {sourceName}");
                Console.WriteLine($"        ID: {eventId}");
                Console.WriteLine($"        Source Type: {sourceType}");
                Console.WriteLine($"        Instance ID: {instanceId ?? "MISSING"}");

                // Check for required properties
                XmlNodeList props = eventElement.SelectNodes("Properties/Property");
                Console.WriteLine($"        Properties: {props.Count}");

                bool hasViewId = false;
                bool hasRuleName = false;
                bool hasLocation = false;

                foreach (XmlElement prop in props)
                {
                    string propName = prop.SelectSingleNode("Name")?.InnerText;
                    string propValue = prop.SelectSingleNode("Value")?.InnerText;

                    if (propName == "ViewID") hasViewId = true;
                    if (propName == "RuleFriendlyName") hasRuleName = true;
                    if (propName == "Location") hasLocation = true;

                    Console.WriteLine($"          {propName}: {propValue ?? "NULL"}");
                }

                // Validate required properties
                if (!hasViewId) Console.WriteLine($"        WARNING: Missing ViewID property!");
                if (!hasRuleName) Console.WriteLine($"        WARNING: Missing RuleFriendlyName property!");
                if (!hasLocation) Console.WriteLine($"        WARNING: Missing Location property!");

                // Check handlers
                XmlNodeList handlers = eventElement.SelectNodes("Handlers/Handler");
                Console.WriteLine($"        Handlers: {handlers.Count}");

                foreach (XmlElement handler in handlers)
                {
                    string handlerId = handler.GetAttribute("ID");
                    XmlNodeList actions = handler.SelectNodes("Actions/Action");
                    Console.WriteLine($"          Handler ID: {handlerId}");
                    Console.WriteLine($"            Actions: {actions.Count}");

                    foreach (XmlElement action in actions)
                    {
                        string actionType = action.GetAttribute("Type");
                        string actionInstanceId = action.GetAttribute("InstanceID");
                        Console.WriteLine($"              Action Type: {actionType}, Instance ID: {actionInstanceId ?? "MISSING"}");

                        // For Transfer actions, check parameters
                        if (actionType == "Transfer")
                        {
                            XmlNodeList parameters = action.SelectNodes("Parameters/Parameter");
                            Console.WriteLine($"                Parameters: {parameters.Count}");

                            foreach (XmlElement param in parameters)
                            {
                                string targetType = param.GetAttribute("TargetType");
                                string targetId = param.GetAttribute("TargetID");
                                string targetInstanceId = param.GetAttribute("TargetInstanceID");

                                if (targetType == "ViewProperty" && targetId == "display")
                                {
                                    string displayValue = param.SelectSingleNode("SourceValue")?.InnerText;
                                    Console.WriteLine($"                  View visibility: {displayValue} (Instance: {targetInstanceId ?? "MISSING"})");

                                    if (string.IsNullOrEmpty(targetInstanceId))
                                    {
                                        Console.WriteLine($"                  ERROR: Missing TargetInstanceID for view visibility action!");
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private void PopulateFieldMappingsFromViewData(ViewPairInfo viewPair,
            Dictionary<string, ViewGenerator.ControlMapping> itemViewControls,
            Dictionary<string, ViewGenerator.ControlMapping> listViewControls)
        {
            Console.WriteLine($"\n        === Populating Field Mappings ===");
            viewPair.FieldMappings = new List<FieldMapping>();

            if (itemViewControls == null || listViewControls == null)
            {
                Console.WriteLine($"          WARNING: Null control mappings");
                Console.WriteLine($"            Item controls null: {itemViewControls == null}");
                Console.WriteLine($"            List controls null: {listViewControls == null}");
                return;
            }

            Console.WriteLine($"          Item view has {itemViewControls.Count} controls:");
            foreach (var ctrl in itemViewControls)
            {
                Console.WriteLine($"            - {ctrl.Key}: {ctrl.Value.ControlName} (Type: {ctrl.Value.ControlType}, ID: {ctrl.Value.ControlId})");
            }

            Console.WriteLine($"          List view has {listViewControls.Count} controls:");
            foreach (var ctrl in listViewControls)
            {
                Console.WriteLine($"            - {ctrl.Key}: {ctrl.Value.ControlName} (Type: {ctrl.Value.ControlType}, ID: {ctrl.Value.ControlId})");
            }

            // Build a reverse lookup: SmoPropertyName -> list control key, for fallback matching
            var listSmoToKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var lc in listViewControls)
            {
                if (!string.IsNullOrEmpty(lc.Value.SmoPropertyName))
                    listSmoToKey[lc.Value.SmoPropertyName] = lc.Key;
                // Also index by the key itself (which is often the SMO property name for list views)
                if (!listSmoToKey.ContainsKey(lc.Key))
                    listSmoToKey[lc.Key] = lc.Key;
            }

            // Track which list controls have been matched (to report unmatched ones)
            var matchedListKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Match controls by field name, with SmoPropertyName fallback
            foreach (var itemControl in itemViewControls)
            {
                string fieldName = itemControl.Key;
                ViewGenerator.ControlMapping listControl = null;
                string matchedListKey = null;
                string matchMethod = "direct key";

                // 1. Direct key match
                if (listViewControls.ContainsKey(fieldName))
                {
                    listControl = listViewControls[fieldName];
                    matchedListKey = fieldName;
                }
                // 2. Fallback: match item's SmoPropertyName against list view keys or SmoPropertyNames
                else if (!string.IsNullOrEmpty(itemControl.Value.SmoPropertyName))
                {
                    string itemSmoProp = itemControl.Value.SmoPropertyName;
                    if (listViewControls.ContainsKey(itemSmoProp))
                    {
                        listControl = listViewControls[itemSmoProp];
                        matchedListKey = itemSmoProp;
                        matchMethod = "item SmoPropertyName -> list key";
                    }
                    else if (listSmoToKey.ContainsKey(itemSmoProp))
                    {
                        string listKey = listSmoToKey[itemSmoProp];
                        listControl = listViewControls[listKey];
                        matchedListKey = listKey;
                        matchMethod = "item SmoPropertyName -> list SmoPropertyName";
                    }
                }
                // 3. Fallback: match item key against list SmoPropertyNames
                if (listControl == null && listSmoToKey.ContainsKey(fieldName))
                {
                    string listKey = listSmoToKey[fieldName];
                    if (listKey != fieldName) // avoid re-checking direct match
                    {
                        listControl = listViewControls[listKey];
                        matchedListKey = listKey;
                        matchMethod = "item key -> list SmoPropertyName";
                    }
                }

                if (listControl != null)
                {
                    viewPair.FieldMappings.Add(new FieldMapping
                    {
                        ItemControlId = itemControl.Value.ControlId,
                        ItemControlName = itemControl.Value.ControlName,
                        ListControlId = listControl.ControlId,
                        ListControlName = listControl.ControlName,
                        FieldName = fieldName
                    });

                    matchedListKeys.Add(matchedListKey);
                    Console.WriteLine($"          ✓ Mapped field '{fieldName}' ({matchMethod}):");
                    Console.WriteLine($"              Item: {itemControl.Value.ControlName} (ID: {itemControl.Value.ControlId}, SmoProp: {itemControl.Value.SmoPropertyName ?? "N/A"})");
                    Console.WriteLine($"              List: {listControl.ControlName} (ID: {listControl.ControlId}, SmoProp: {listControl.SmoPropertyName ?? "N/A"})");
                }
                else
                {
                    Console.WriteLine($"          ✗ Field '{fieldName}' (SmoProp: {itemControl.Value.SmoPropertyName ?? "N/A"}) exists in item view but NO match found in list view");
                }
            }

            // Log any fields that exist in list but not item (for debugging)
            foreach (var listControl in listViewControls)
            {
                if (!matchedListKeys.Contains(listControl.Key))
                {
                    Console.WriteLine($"          ✗ Field '{listControl.Key}' (SmoProp: {listControl.Value.SmoPropertyName ?? "N/A"}) exists in list view but was NOT matched to any item view control");
                }
            }

            Console.WriteLine($"          Total mappings created: {viewPair.FieldMappings.Count}");
        }

        private void AddListViewAddToolbarButtonRule(XmlDocument doc, XmlElement eventsElement,
                                           ViewPairInfo viewPair, string buttonId, string buttonName)
        {
            Console.WriteLine($"        Creating List View Add Toolbar Button Rule");
            Console.WriteLine($"          Button ID: {buttonId}");
            Console.WriteLine($"          Button Name: {buttonName}");
            Console.WriteLine($"          List View Instance ID: {viewPair.ListViewInstanceId ?? "NULL"}");
            Console.WriteLine($"          Item View Instance ID: {viewPair.ItemViewInstanceId ?? "NULL"}");

            XmlElement eventElement = doc.CreateElement("Event");
            string eventGuid = Guid.NewGuid().ToString();
            eventElement.SetAttribute("ID", eventGuid);
            eventElement.SetAttribute("DefinitionID", Guid.NewGuid().ToString()); // Always new GUID
            eventElement.SetAttribute("Type", "User");
            eventElement.SetAttribute("SourceID", buttonId);
            eventElement.SetAttribute("SourceType", "Control");
            eventElement.SetAttribute("SourceName", buttonName);
            eventElement.SetAttribute("SourceDisplayName", buttonName);
            eventElement.SetAttribute("IsExtended", "True");
            eventElement.SetAttribute("InstanceID", viewPair.ListViewInstanceId ?? viewPair.ListViewId);

            XmlHelper.AddElement(doc, eventElement, "Name", "OnClick");

            XmlElement eventProps = doc.CreateElement("Properties");

            XmlElement viewIdProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, viewIdProp, "Name", "ViewID");
            XmlHelper.AddElement(doc, viewIdProp, "DisplayValue", viewPair.ListViewName);
            XmlHelper.AddElement(doc, viewIdProp, "NameValue", viewPair.ListViewName);
            XmlHelper.AddElement(doc, viewIdProp, "Value", viewPair.ListViewId);
            eventProps.AppendChild(viewIdProp);

            XmlElement ruleProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, ruleProp, "Name", "RuleFriendlyName");
            XmlHelper.AddElement(doc, ruleProp, "Value", $"On {viewPair.ListViewName}, when {buttonName} is Clicked");
            eventProps.AppendChild(ruleProp);

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", viewPair.ListViewName);
            eventProps.AppendChild(locationProp);

            eventElement.AppendChild(eventProps);

            XmlElement handlers = doc.CreateElement("Handlers");
            XmlElement handler = CreateVisibilityHandler(doc, viewPair, true);
            handlers.AppendChild(handler);
            eventElement.AppendChild(handlers);

            eventsElement.AppendChild(eventElement);
            Console.WriteLine($"        ✓ Rule created with Event ID: {eventGuid}");
        }

        private XmlElement CreateVisibilityHandler(XmlDocument doc, ViewPairInfo viewPair, bool isListToItem)
        {
            Console.WriteLine($"          Creating Visibility Handler");
            Console.WriteLine($"            Direction: {(isListToItem ? "List to Item" : "Item to List")}");
            Console.WriteLine($"            Hide View: {(isListToItem ? viewPair.ListViewName : viewPair.ItemViewName)}");
            Console.WriteLine($"            Show View: {(isListToItem ? viewPair.ItemViewName : viewPair.ListViewName)}");

            XmlElement handler = doc.CreateElement("Handler");
            string handlerId = Guid.NewGuid().ToString();
            handler.SetAttribute("ID", handlerId);
            handler.SetAttribute("DefinitionID", Guid.NewGuid().ToString()); // Always new GUID

            XmlElement props = doc.CreateElement("Properties");
            XmlElement handlerNameProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, handlerNameProp, "Name", "HandlerName");
            XmlHelper.AddElement(doc, handlerNameProp, "Value", "then");
            props.AppendChild(handlerNameProp);

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", "form");
            props.AppendChild(locationProp);

            handler.AppendChild(props);

            XmlElement actions = doc.CreateElement("Actions");

            if (isListToItem)
            {
                XmlElement hideAction = CreateViewVisibilityAction(doc,
                    viewPair.ListViewInstanceId ?? viewPair.ListViewId,
                    viewPair.ListViewName, "Hide",
                    viewPair.ListViewId);
                actions.AppendChild(hideAction);

                XmlElement showAction = CreateViewVisibilityAction(doc,
                    viewPair.ItemViewInstanceId ?? viewPair.ItemViewId,
                    viewPair.ItemViewName, "Show",
                    viewPair.ItemViewId);
                actions.AppendChild(showAction);
            }
            else
            {
                XmlElement hideAction = CreateViewVisibilityAction(doc,
                    viewPair.ItemViewInstanceId ?? viewPair.ItemViewId,
                    viewPair.ItemViewName, "Hide",
                    viewPair.ItemViewId);
                actions.AppendChild(hideAction);

                XmlElement showAction = CreateViewVisibilityAction(doc,
                    viewPair.ListViewInstanceId ?? viewPair.ListViewId,
                    viewPair.ListViewName, "Show",
                    viewPair.ListViewId);
                actions.AppendChild(showAction);
            }

            handler.AppendChild(actions);
            Console.WriteLine($"            Handler created with ID: {handlerId}");
            return handler;
        }
        private XmlElement CreateViewVisibilityAction(XmlDocument doc, string viewInstanceId,
           string viewName, string visibility, string viewId = null)
        {
            Console.WriteLine($"              Creating {visibility} action for {viewName}");
            Console.WriteLine($"                View Instance ID (AreaItem): {viewInstanceId}");
            Console.WriteLine($"                View ID (actual view): {viewId ?? "(not provided)"}");

            // K2 Designer requires ViewID property and TargetName/TargetDisplayName on Parameter
            // for the action to be "transformable" (renderable in the Rule Designer).
            // Without these, K2 shows "One or more actions could not be transformed" and
            // drops the visibility actions from the visual representation.
            //
            // Pattern (matching other working actions like AddRow, AcceptItem, Clear):
            // <Action Type="Transfer" ExecutionType="Synchronous" InstanceID="{AREA_ITEM_ID}">
            //   <Properties>
            //     <Property><Name>Location</Name><Value>Form</Value></Property>
            //     <Property><Name>ViewID</Name><DisplayValue>{viewName}</DisplayValue>
            //       <NameValue>{viewName}</NameValue><Value>{viewId}</Value></Property>
            //   </Properties>
            //   <Parameters>
            //     <Parameter SourceType="Value" TargetInstanceID="{AREA_ITEM_ID}"
            //                TargetID="display" TargetName="display"
            //                TargetDisplayName="{viewName}" TargetType="ViewProperty">
            //       <SourceValue xml:space="preserve">Show|Hide</SourceValue>
            //     </Parameter>
            //   </Parameters>
            // </Action>
            XmlElement action = doc.CreateElement("Action");
            string actionId = Guid.NewGuid().ToString();
            action.SetAttribute("ID", actionId);
            action.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            action.SetAttribute("Type", "Transfer");
            action.SetAttribute("ExecutionType", "Synchronous");
            action.SetAttribute("InstanceID", viewInstanceId);

            XmlElement props = doc.CreateElement("Properties");

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", "Form");
            props.AppendChild(locationProp);

            // ViewID property - required for K2 Designer to transform the action
            // Use actual view ID if provided, fall back to AreaItem ID
            string effectiveViewId = !string.IsNullOrEmpty(viewId) ? viewId : viewInstanceId;
            XmlElement viewIdProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, viewIdProp, "Name", "ViewID");
            XmlHelper.AddElement(doc, viewIdProp, "DisplayValue", viewName);
            XmlHelper.AddElement(doc, viewIdProp, "NameValue", viewName);
            XmlHelper.AddElement(doc, viewIdProp, "Value", effectiveViewId);
            props.AppendChild(viewIdProp);

            action.AppendChild(props);

            XmlElement parameters = doc.CreateElement("Parameters");
            XmlElement parameter = doc.CreateElement("Parameter");
            parameter.SetAttribute("SourceType", "Value");
            parameter.SetAttribute("TargetInstanceID", viewInstanceId);
            parameter.SetAttribute("TargetID", "display");
            parameter.SetAttribute("TargetName", "display");
            parameter.SetAttribute("TargetDisplayName", viewName);
            parameter.SetAttribute("TargetType", "ViewProperty");

            XmlElement sourceValue = doc.CreateElement("SourceValue");
            sourceValue.SetAttribute("xml:space", "preserve");
            sourceValue.InnerText = visibility;
            parameter.AppendChild(sourceValue);

            parameters.AppendChild(parameter);
            action.AppendChild(parameters);

            Console.WriteLine($"                Action created with ID: {actionId}");
            Console.WriteLine($"                Target Instance ID: {viewInstanceId}");
            Console.WriteLine($"                View ID in properties: {effectiveViewId}");
            Console.WriteLine($"                Visibility: {visibility}");

            return action;
        }

        public void AddFormLevelRules(XmlDocument doc, string formId, string formName,
      Dictionary<string, ViewPairInfo> viewPairs)
        {
            Console.WriteLine("\n    === Adding Form-Level Rules (Clear & Submit) ===");

            // Get the base state and events element
            XmlElement statesElement = GetOrCreateStatesElement(doc);
            if (statesElement == null)
            {
                Console.WriteLine("    ERROR: Could not find or create States element");
                return;
            }

            XmlElement baseState = GetBaseState(statesElement);
            if (baseState == null)
            {
                Console.WriteLine("    ERROR: Could not find base state");
                return;
            }

            XmlElement eventsElement = GetOrCreateEventsElement(baseState);

            // Find the Clear and Submit button IDs from the form
            string clearButtonId = FindButtonId(doc, "Clear Form Button");
            string submitButtonId = FindButtonId(doc, "Submit");

            // Add Clear button handler with direct actions
            if (!string.IsNullOrEmpty(clearButtonId))
            {
                AddClearButtonClickHandler(doc, eventsElement, formId, formName, clearButtonId);
            }
            else
            {
                Console.WriteLine("    WARNING: Clear button not found in form");
            }

            // Add Submit rule
            if (!string.IsNullOrEmpty(submitButtonId))
            {
                AddSubmitFormRule(doc, eventsElement, formId, formName, submitButtonId, viewPairs);
            }
            else
            {
                Console.WriteLine("    WARNING: Submit button not found in form");
            }

            // Add Add Item button rules for nested sections
            AddAddItemButtonRules(doc, eventsElement, formId, formName);
        }

        private void AddClearButtonClickHandler(XmlDocument doc, XmlElement eventsElement,
         string formId, string formName, string clearButtonId)
        {
            Console.WriteLine($"      Creating Clear button click handler with direct actions");

            // Create the button click event with clear actions directly
            XmlElement clearButtonEvent = doc.CreateElement("Event");
            string clearButtonEventId = Guid.NewGuid().ToString();
            clearButtonEvent.SetAttribute("ID", clearButtonEventId);
            clearButtonEvent.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            clearButtonEvent.SetAttribute("Type", "User");
            clearButtonEvent.SetAttribute("SourceID", clearButtonId);
            clearButtonEvent.SetAttribute("SourceType", "Control");
            clearButtonEvent.SetAttribute("SourceName", "Clear Form Button");
            clearButtonEvent.SetAttribute("SourceDisplayName", "Clear Form Button");
            clearButtonEvent.SetAttribute("IsExtended", "True");

            XmlHelper.AddElement(doc, clearButtonEvent, "Name", "OnClick");

            // Add properties
            XmlElement props = doc.CreateElement("Properties");

            XmlElement ruleFriendlyProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, ruleFriendlyProp, "Name", "RuleFriendlyName");
            XmlHelper.AddElement(doc, ruleFriendlyProp, "Value", "When Clear Form Button is Clicked");
            props.AppendChild(ruleFriendlyProp);

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", formName);
            props.AppendChild(locationProp);

            // NOTE: Do NOT add ViewID to event Properties for form-level control events.
            // K2 Designer expects only RuleFriendlyName + Location here.
            // ViewID belongs in Action Properties (targeting which view the action operates on).

            clearButtonEvent.AppendChild(props);

            // Create handler with direct clear actions
            XmlElement handlers = doc.CreateElement("Handlers");
            XmlElement handler = doc.CreateElement("Handler");
            string handlerId = Guid.NewGuid().ToString();
            handler.SetAttribute("ID", handlerId);
            handler.SetAttribute("DefinitionID", Guid.NewGuid().ToString());

            XmlElement handlerProps = doc.CreateElement("Properties");
            XmlElement handlerNameProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, handlerNameProp, "Name", "HandlerName");
            XmlHelper.AddElement(doc, handlerNameProp, "Value", "then");
            handlerProps.AppendChild(handlerNameProp);

            XmlElement handlerLocationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, handlerLocationProp, "Name", "Location");
            XmlHelper.AddElement(doc, handlerLocationProp, "Value", "form");
            handlerProps.AppendChild(handlerLocationProp);

            handler.AppendChild(handlerProps);

            // Add Clear actions for all views directly
            XmlElement actions = doc.CreateElement("Actions");

            // Find all views in the form's panel structure
            XmlNodeList items = doc.SelectNodes("//Panel/Areas/Area/Items/Item[@ViewID]");
            Console.WriteLine($"        Found {items.Count} views to clear");

            foreach (XmlElement item in items)
            {
                string viewId = item.GetAttribute("ViewID");
                string instanceId = item.GetAttribute("ID");
                XmlElement nameNode = item.SelectSingleNode("Name") as XmlElement;
                string viewName = nameNode?.InnerText ?? "";

                if (!string.IsNullOrEmpty(viewId) && !string.IsNullOrEmpty(viewName))
                {
                    Console.WriteLine($"          Adding Clear action for view: {viewName}");

                    // Create a clear action for this view
                    XmlElement clearAction = doc.CreateElement("Action");
                    clearAction.SetAttribute("ID", Guid.NewGuid().ToString());
                    clearAction.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
                    clearAction.SetAttribute("Type", "Execute");
                    clearAction.SetAttribute("ExecutionType", "Synchronous");
                    clearAction.SetAttribute("InstanceID", instanceId);

                    XmlElement actionProps = doc.CreateElement("Properties");

                    XmlElement actionLocationProp = doc.CreateElement("Property");
                    XmlHelper.AddElement(doc, actionLocationProp, "Name", "Location");
                    XmlHelper.AddElement(doc, actionLocationProp, "Value", "Form");
                    actionProps.AppendChild(actionLocationProp);

                    XmlElement viewIdProp = doc.CreateElement("Property");
                    XmlHelper.AddElement(doc, viewIdProp, "Name", "ViewID");
                    XmlHelper.AddElement(doc, viewIdProp, "DisplayValue", viewName);
                    XmlHelper.AddElement(doc, viewIdProp, "NameValue", viewName);
                    XmlHelper.AddElement(doc, viewIdProp, "Value", viewId);
                    actionProps.AppendChild(viewIdProp);

                    XmlElement methodProp = doc.CreateElement("Property");
                    XmlHelper.AddElement(doc, methodProp, "Name", "Method");
                    XmlHelper.AddElement(doc, methodProp, "DisplayValue", "Clear");
                    XmlHelper.AddElement(doc, methodProp, "NameValue", "Clear");
                    XmlHelper.AddElement(doc, methodProp, "Value", "Clear");
                    actionProps.AppendChild(methodProp);

                    clearAction.AppendChild(actionProps);
                    actions.AppendChild(clearAction);
                }
            }

            handler.AppendChild(actions);
            handlers.AppendChild(handler);
            clearButtonEvent.AppendChild(handlers);

            // IMPORTANT: Do NOT add DesignProperties with IsStub=True
            // This would mark it as incomplete

            // Add at the END of events
            eventsElement.AppendChild(clearButtonEvent);
            Console.WriteLine($"        ✓ Clear button handler created with {items.Count} clear actions");
        }

     


        private string FindButtonId(XmlDocument doc, string buttonName)
        {
            XmlNodeList controls = doc.SelectNodes($"//Control[Name='{buttonName}']");
            if (controls.Count > 0)
            {
                XmlElement control = (XmlElement)controls[0];
                return control.GetAttribute("ID");
            }
            return null;
        }

        private void AddClearFormRule(XmlDocument doc, XmlElement eventsElement,
          string formId, string formName, string clearButtonId,
          Dictionary<string, ViewPairInfo> viewPairs)
        {
            Console.WriteLine($"\n      === Creating Clear Form Rule ===");

            // IMPORTANT: The order matters! 
            // 1. First, create the Clear event definition (the reusable rule)
            string clearEventDefId = "c260c3cd-6dab-4684-a01b-86c76bd416f5"; // Use consistent GUID
            XmlElement clearEventDef = CreateClearEventDefinition(doc, clearEventDefId,
                formId, formName, viewPairs);

            // Insert at the beginning of events to ensure it exists before references
            if (eventsElement.FirstChild != null)
            {
                eventsElement.InsertBefore(clearEventDef, eventsElement.FirstChild);
            }
            else
            {
                eventsElement.AppendChild(clearEventDef);
            }
            Console.WriteLine($"        ✓ Clear event definition created with ID: {clearEventDefId}");

            // 2. Then create the button click event that calls the Clear event
            XmlElement clearButtonEvent = doc.CreateElement("Event");
            string clearButtonEventId = Guid.NewGuid().ToString();
            clearButtonEvent.SetAttribute("ID", clearButtonEventId);
            clearButtonEvent.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            clearButtonEvent.SetAttribute("Type", "User");
            clearButtonEvent.SetAttribute("SourceID", clearButtonId);
            clearButtonEvent.SetAttribute("SourceType", "Control");
            clearButtonEvent.SetAttribute("SourceName", "Clear Form Button");
            clearButtonEvent.SetAttribute("SourceDisplayName", "Clear Form Button");
            clearButtonEvent.SetAttribute("IsExtended", "True");

            XmlHelper.AddElement(doc, clearButtonEvent, "Name", "OnClick");

            // Add properties
            XmlElement props = doc.CreateElement("Properties");

            XmlElement ruleFriendlyProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, ruleFriendlyProp, "Name", "RuleFriendlyName");
            XmlHelper.AddElement(doc, ruleFriendlyProp, "Value", "When Clear Form Button is Clicked");
            props.AppendChild(ruleFriendlyProp);

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", formName);
            props.AppendChild(locationProp);

            // NOTE: Do NOT add ViewID to event Properties for form-level control events.
            // K2 Designer expects only RuleFriendlyName + Location here.

            clearButtonEvent.AppendChild(props);

            // Create handler that executes the Clear event
            XmlElement handlers = doc.CreateElement("Handlers");
            XmlElement handler = doc.CreateElement("Handler");
            handler.SetAttribute("ID", Guid.NewGuid().ToString());
            handler.SetAttribute("DefinitionID", Guid.NewGuid().ToString());

            XmlElement handlerProps = doc.CreateElement("Properties");
            XmlElement handlerNameProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, handlerNameProp, "Name", "HandlerName");
            XmlHelper.AddElement(doc, handlerNameProp, "Value", "IfLogicalHandler");
            handlerProps.AppendChild(handlerNameProp);

            XmlElement handlerLocationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, handlerLocationProp, "Name", "Location");
            XmlHelper.AddElement(doc, handlerLocationProp, "Value", "form");
            handlerProps.AppendChild(handlerLocationProp);

            handler.AppendChild(handlerProps);

            // Add action to execute the Clear event
            XmlElement actions = doc.CreateElement("Actions");
            XmlElement executeAction = doc.CreateElement("Action");
            executeAction.SetAttribute("ID", Guid.NewGuid().ToString());
            executeAction.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            executeAction.SetAttribute("Type", "Execute");
            executeAction.SetAttribute("ExecutionType", "Synchronous");

            XmlElement actionProps = doc.CreateElement("Properties");

            XmlElement actionLocationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, actionLocationProp, "Name", "Location");
            XmlHelper.AddElement(doc, actionLocationProp, "Value", "Form");
            actionProps.AppendChild(actionLocationProp);

            XmlElement eventIdProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, eventIdProp, "Name", "EventID");
            XmlHelper.AddElement(doc, eventIdProp, "DisplayValue", "Clear");
            XmlHelper.AddElement(doc, eventIdProp, "NameValue", "Clear");
            XmlHelper.AddElement(doc, eventIdProp, "Value", clearEventDefId);
            actionProps.AppendChild(eventIdProp);

            executeAction.AppendChild(actionProps);
            actions.AppendChild(executeAction);
            handler.AppendChild(actions);
            handlers.AppendChild(handler);
            clearButtonEvent.AppendChild(handlers);

            eventsElement.AppendChild(clearButtonEvent);
            Console.WriteLine($"        ✓ Clear button rule created");
        }
        private XmlElement CreateClearEventDefinition(XmlDocument doc, string eventDefId,
       string formId, string formName, Dictionary<string, ViewPairInfo> viewPairs)
        {
            XmlElement clearEvent = doc.CreateElement("Event");
            clearEvent.SetAttribute("ID", eventDefId);
            clearEvent.SetAttribute("DefinitionID", Guid.NewGuid().ToString()); // Always generate new GUID
            clearEvent.SetAttribute("Type", "User");
            clearEvent.SetAttribute("SourceID", formId);
            clearEvent.SetAttribute("SourceType", "Rule");
            clearEvent.SetAttribute("SourceName", "Rule");
            clearEvent.SetAttribute("SourceDisplayName", "Clear");
            clearEvent.SetAttribute("IsExtended", "True");

            XmlHelper.AddElement(doc, clearEvent, "Name", Guid.NewGuid().ToString());

            // Add properties
            XmlElement props = doc.CreateElement("Properties");

            XmlElement isCustomNameProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, isCustomNameProp, "Name", "IsCustomName");
            XmlHelper.AddElement(doc, isCustomNameProp, "Value", "true");
            props.AppendChild(isCustomNameProp);

            XmlElement ruleNameProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, ruleNameProp, "Name", "RuleName");
            XmlHelper.AddElement(doc, ruleNameProp, "Value", "Clear");
            props.AppendChild(ruleNameProp);

            XmlElement ruleFriendlyProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, ruleFriendlyProp, "Name", "RuleFriendlyName");
            XmlHelper.AddElement(doc, ruleFriendlyProp, "Value", "Clear");
            props.AppendChild(ruleFriendlyProp);

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", formName);
            props.AppendChild(locationProp);

            clearEvent.AppendChild(props);

            // Create handler with Clear actions for all views
            XmlElement handlers = doc.CreateElement("Handlers");
            XmlElement handler = doc.CreateElement("Handler");
            string handlerId = Guid.NewGuid().ToString();
            handler.SetAttribute("ID", handlerId);
            handler.SetAttribute("DefinitionID", Guid.NewGuid().ToString()); // Generate new GUID

            XmlElement handlerProps = doc.CreateElement("Properties");
            XmlElement handlerNameProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, handlerNameProp, "Name", "HandlerName");
            XmlHelper.AddElement(doc, handlerNameProp, "Value", "then");
            handlerProps.AppendChild(handlerNameProp);

            XmlElement handlerLocationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, handlerLocationProp, "Name", "Location");
            XmlHelper.AddElement(doc, handlerLocationProp, "Value", "form");
            handlerProps.AppendChild(handlerLocationProp);

            handler.AppendChild(handlerProps);

            // Add Clear actions for all views
            XmlElement actions = doc.CreateElement("Actions");

            // Find all views in the form's panel structure
            XmlNodeList items = doc.SelectNodes("//Panel/Areas/Area/Items/Item[@ViewID]");
            Console.WriteLine($"        Found {items.Count} views to clear");

            foreach (XmlElement item in items)
            {
                string viewId = item.GetAttribute("ViewID");
                string instanceId = item.GetAttribute("ID");
                XmlElement nameNode = item.SelectSingleNode("Name") as XmlElement;
                string viewName = nameNode?.InnerText ?? "";

                if (!string.IsNullOrEmpty(viewId) && !string.IsNullOrEmpty(viewName))
                {
                    Console.WriteLine($"          Adding Clear action for view: {viewName}");

                    // Create a clear action for this view
                    XmlElement clearAction = doc.CreateElement("Action");
                    clearAction.SetAttribute("ID", Guid.NewGuid().ToString());

                    // IMPORTANT: Always generate a new GUID for DefinitionID to avoid duplicates
                    clearAction.SetAttribute("DefinitionID", Guid.NewGuid().ToString());

                    clearAction.SetAttribute("Type", "Execute");
                    clearAction.SetAttribute("ExecutionType", "Synchronous");
                    clearAction.SetAttribute("InstanceID", instanceId);

                    XmlElement actionProps = doc.CreateElement("Properties");

                    XmlElement actionLocationProp = doc.CreateElement("Property");
                    XmlHelper.AddElement(doc, actionLocationProp, "Name", "Location");
                    XmlHelper.AddElement(doc, actionLocationProp, "Value", "Form");
                    actionProps.AppendChild(actionLocationProp);

                    XmlElement viewIdProp = doc.CreateElement("Property");
                    XmlHelper.AddElement(doc, viewIdProp, "Name", "ViewID");
                    XmlHelper.AddElement(doc, viewIdProp, "DisplayValue", viewName);
                    XmlHelper.AddElement(doc, viewIdProp, "NameValue", viewName);
                    XmlHelper.AddElement(doc, viewIdProp, "Value", viewId);
                    actionProps.AppendChild(viewIdProp);

                    XmlElement methodProp = doc.CreateElement("Property");
                    XmlHelper.AddElement(doc, methodProp, "Name", "Method");
                    XmlHelper.AddElement(doc, methodProp, "DisplayValue", "Clear");
                    XmlHelper.AddElement(doc, methodProp, "NameValue", "Clear");
                    XmlHelper.AddElement(doc, methodProp, "Value", "Clear");
                    actionProps.AppendChild(methodProp);

                    clearAction.AppendChild(actionProps);
                    actions.AppendChild(clearAction);
                }
            }

            handler.AppendChild(actions);
            handlers.AppendChild(handler);
            clearEvent.AppendChild(handlers);

            // Note: We're NOT adding DesignProperties with IsStub=True
            // This ensures the rule is considered complete

            return clearEvent;
        }
        private XmlElement CreateClearViewAction(XmlDocument doc, string viewId,
          string viewName, string instanceId)
        {
            XmlElement action = doc.CreateElement("Action");
            string actionId = Guid.NewGuid().ToString();
            action.SetAttribute("ID", actionId);

            // Use consistent definition IDs based on your XML sample
            string definitionId = Guid.NewGuid().ToString();
            // You might want to use specific definition IDs that K2 recognizes
            // For Clear actions, these typically follow a pattern
            action.SetAttribute("DefinitionID", definitionId);
            action.SetAttribute("Type", "Execute");
            action.SetAttribute("ExecutionType", "Synchronous");
            action.SetAttribute("InstanceID", instanceId);

            XmlElement props = doc.CreateElement("Properties");

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", "Form");
            props.AppendChild(locationProp);

            XmlElement viewIdProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, viewIdProp, "Name", "ViewID");
            XmlHelper.AddElement(doc, viewIdProp, "DisplayValue", viewName);
            XmlHelper.AddElement(doc, viewIdProp, "NameValue", viewName);
            XmlHelper.AddElement(doc, viewIdProp, "Value", viewId);
            props.AppendChild(viewIdProp);

            XmlElement methodProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, methodProp, "Name", "Method");
            XmlHelper.AddElement(doc, methodProp, "DisplayValue", "Clear");
            XmlHelper.AddElement(doc, methodProp, "NameValue", "Clear");
            XmlHelper.AddElement(doc, methodProp, "Value", "Clear");
            props.AppendChild(methodProp);

            action.AppendChild(props);
            return action;
        }

        private void AnalyzeAndPopulateNestingHierarchy(Dictionary<string, ViewPairInfo> viewPairs)
        {
            Console.WriteLine("\n      === Analyzing Nesting Hierarchy ===");

            // First, identify nested sections from the JSON data
            // In a real implementation, this would parse the NestedRepeatingSections from JSON
            // For now, we'll use naming conventions to detect nesting
            var nestedSectionMappings = new Dictionary<string, string>
            {
                { "Table_CTRL329", "Item_Entertainment_Details" },
                { "Table_CTRL338", "Item_Entertainment_Details" }
            };

            // Set nesting levels and parent relationships
            foreach (var viewPair in viewPairs.Values)
            {
                string sectionName = viewPair.SectionName;

                // Check if this is a nested section
                if (nestedSectionMappings.ContainsKey(sectionName))
                {
                    string parentSectionName = nestedSectionMappings[sectionName];
                    viewPair.IsNestedSection = true;
                    viewPair.ParentSectionName = parentSectionName;
                    viewPair.NestingLevel = 2; // Second level (main form = 0, first child = 1, nested = 2)
                    viewPair.ParentViewPairKey = parentSectionName;

                    Console.WriteLine($"        Found nested section: {sectionName} -> parent: {parentSectionName}");

                    // Add this as a child to the parent
                    if (viewPairs.ContainsKey(parentSectionName))
                    {
                        viewPairs[parentSectionName].ChildSectionNames.Add(sectionName);
                    }
                }
                else if (viewPair.IsTopLevel == false)
                {
                    // This is a first-level child section
                    viewPair.NestingLevel = 1;
                    viewPair.ParentSectionName = "MAIN_FORM";
                    viewPair.ParentViewPairKey = "MAIN_FORM";
                    Console.WriteLine($"        Found first-level section: {sectionName}");
                }
                else
                {
                    // This is the main form or top-level section
                    viewPair.NestingLevel = 0;
                    Console.WriteLine($"        Found top-level section: {sectionName}");
                }
            }

            // Log the complete hierarchy
            Console.WriteLine($"        ✓ Analyzed hierarchy for {viewPairs.Count} view pairs");
            foreach (var viewPair in viewPairs.Values.OrderBy(vp => vp.NestingLevel))
            {
                string indent = new string(' ', viewPair.NestingLevel * 4);
                Console.WriteLine($"        {indent}Level {viewPair.NestingLevel}: {viewPair.SectionName}" +
                    (viewPair.IsNestedSection ? $" (nested under {viewPair.ParentSectionName})" : ""));
            }
        }

        private void CreateDependencyAwareHandlers(XmlDocument doc, XmlElement handlers,
            string formId, string formName, Dictionary<string, ViewPairInfo> viewPairs)
        {
            Console.WriteLine("\n      === Creating Dependency-Aware Handlers ===");

            // Group viewPairs by nesting level and create handlers in dependency order
            var viewPairsByLevel = viewPairs.Values
                .Where(vp => !string.IsNullOrEmpty(vp.ListViewId))
                .GroupBy(vp => vp.NestingLevel)
                .OrderBy(g => g.Key)
                .ToList();

            Console.WriteLine($"        Found {viewPairsByLevel.Count} nesting levels to process");

            foreach (var levelGroup in viewPairsByLevel)
            {
                int level = levelGroup.Key;
                var viewPairsAtLevel = levelGroup.ToList();

                Console.WriteLine($"        Processing Level {level} ({viewPairsAtLevel.Count} sections):");

                if (level == 1)
                {
                    // Level 1: First-level children - can be processed in parallel
                    // These depend only on the main form (which was saved in Handler 1)
                    foreach (var viewPair in viewPairsAtLevel)
                    {
                        Console.WriteLine($"          Creating ForEach handler for Level 1: {viewPair.SectionName}");
                        XmlElement forEachHandler = CreateForEachListHandler(doc, formId, formName, viewPair);
                        handlers.AppendChild(forEachHandler);
                    }
                }
                else if (level >= 2)
                {
                    // Level 2+: Nested sections - need sequential execution with parent dependency
                    // Add safety check to ensure we have valid nested sections
                    var validNestedSections = viewPairsAtLevel.Where(vp =>
                        !string.IsNullOrEmpty(vp.ListViewId) &&
                        !string.IsNullOrEmpty(vp.ListViewInstanceId) &&
                        !string.IsNullOrEmpty(vp.ParentSectionName)).ToList();

                    if (validNestedSections.Count > 0)
                    {
                        CreateSequentialNestedHandlers(doc, handlers, formId, formName, validNestedSections, viewPairs);
                    }
                    else
                    {
                        Console.WriteLine($"          No valid nested sections found at level {level}, falling back to standard ForEach handlers");
                        // Fallback to standard ForEach handlers for invalid nested sections
                        foreach (var viewPair in viewPairsAtLevel)
                        {
                            Console.WriteLine($"          Creating fallback ForEach handler for: {viewPair.SectionName}");
                            XmlElement forEachHandler = CreateForEachListHandler(doc, formId, formName, viewPair);
                            handlers.AppendChild(forEachHandler);
                        }
                    }
                }
            }

            Console.WriteLine($"        ✓ Created dependency-aware handlers for all nesting levels");
        }

        private void CreateSequentialNestedHandlers(XmlDocument doc, XmlElement handlers,
            string formId, string formName, List<ViewPairInfo> nestedViewPairs,
            Dictionary<string, ViewPairInfo> allViewPairs)
        {
            Console.WriteLine($"        Creating sequential handlers for {nestedViewPairs.Count} nested sections");

            foreach (var nestedViewPair in nestedViewPairs)
            {
                // Validate that the nested view pair has all required properties
                if (string.IsNullOrEmpty(nestedViewPair.ListViewId) ||
                    string.IsNullOrEmpty(nestedViewPair.ListViewInstanceId) ||
                    string.IsNullOrEmpty(nestedViewPair.ListViewName))
                {
                    Console.WriteLine($"          Skipping nested handler for {nestedViewPair.SectionName}: Missing required properties");
                    Console.WriteLine($"            ListViewId: {nestedViewPair.ListViewId ?? "NULL"}");
                    Console.WriteLine($"            ListViewInstanceId: {nestedViewPair.ListViewInstanceId ?? "NULL"}");
                    Console.WriteLine($"            ListViewName: {nestedViewPair.ListViewName ?? "NULL"}");
                    continue;
                }

                Console.WriteLine($"          Creating nested handler for: {nestedViewPair.SectionName} (parent: {nestedViewPair.ParentSectionName})");

                // Create a specialized handler that waits for parent completion
                XmlElement nestedHandler = CreateNestedSectionHandler(doc, formId, formName, nestedViewPair, allViewPairs);
                handlers.AppendChild(nestedHandler);
            }
        }

        private XmlElement CreateNestedSectionHandler(XmlDocument doc, string formId, string formName,
            ViewPairInfo nestedViewPair, Dictionary<string, ViewPairInfo> allViewPairs)
        {
            // For nested sections, we need a handler that iterates through the parent section's records
            // and for each parent record, saves all the child records that belong to it

            XmlElement handler = doc.CreateElement("Handler");
            handler.SetAttribute("ID", Guid.NewGuid().ToString());
            handler.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            handler.SetAttribute("Type", "ForEach");

            XmlElement handlerProps = doc.CreateElement("Properties");
            XmlElement handlerNameProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, handlerNameProp, "Name", "HandlerName");
            XmlHelper.AddElement(doc, handlerNameProp, "Value", "ForEachParentRecordHandler");
            handlerProps.AppendChild(handlerNameProp);

            XmlElement handlerLocationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, handlerLocationProp, "Name", "Location");
            XmlHelper.AddElement(doc, handlerLocationProp, "Value", "form");
            handlerProps.AppendChild(handlerLocationProp);

            handler.AppendChild(handlerProps);

            // Add nested action to save child records for each parent
            XmlElement actions = doc.CreateElement("Actions");
            XmlElement nestedForEachAction = CreateNestedForEachAction(doc, nestedViewPair, allViewPairs);
            actions.AppendChild(nestedForEachAction);
            handler.AppendChild(actions);

            // Add function to iterate through parent records that were just saved
            XmlElement function = doc.CreateElement("Function");
            function.SetAttribute("ID", Guid.NewGuid().ToString());

            // Get the parent ViewPair to know which list view to iterate through
            ViewPairInfo parentViewPair = null;
            if (!string.IsNullOrEmpty(nestedViewPair.ParentSectionName) &&
                allViewPairs.ContainsKey(nestedViewPair.ParentSectionName))
            {
                parentViewPair = allViewPairs[nestedViewPair.ParentSectionName];
                if (!string.IsNullOrEmpty(parentViewPair.ListViewInstanceId))
                {
                    function.SetAttribute("InstanceID", parentViewPair.ListViewInstanceId);
                }
                else
                {
                    Console.WriteLine($"          Warning: Parent view {parentViewPair.ListViewName} has no ListViewInstanceId");
                    // Fallback to using the nested view's own instance ID
                    function.SetAttribute("InstanceID", nestedViewPair.ListViewInstanceId);
                }
            }
            else
            {
                Console.WriteLine($"          Warning: Parent section {nestedViewPair.ParentSectionName} not found, using nested view instance ID");
                function.SetAttribute("InstanceID", nestedViewPair.ListViewInstanceId);
            }

            XmlElement viewItemsCollection = doc.CreateElement("ViewItemsCollection");

            if (parentViewPair != null && !string.IsNullOrEmpty(parentViewPair.ListViewId) && !string.IsNullOrEmpty(parentViewPair.ListViewName))
            {
                // Item for the parent view itself
                XmlElement viewItem = doc.CreateElement("Item");
                viewItem.SetAttribute("SourceType", "View");
                viewItem.SetAttribute("SourceID", parentViewPair.ListViewId);
                viewItem.SetAttribute("SourceName", parentViewPair.ListViewName);
                viewItem.SetAttribute("SourceDisplayName", parentViewPair.ListViewName);
                viewItem.SetAttribute("DataType", "Guid");
                viewItemsCollection.AppendChild(viewItem);

                // Item for the state filter (Added items only)
                XmlElement stateItem = doc.CreateElement("Item");
                stateItem.SetAttribute("SourceType", "ItemState");
                stateItem.SetAttribute("SourceID", "Added");
                stateItem.SetAttribute("SourceName", "Added");
                stateItem.SetAttribute("SourceDisplayName", "Added");
                stateItem.SetAttribute("DataType", "Text");
                viewItemsCollection.AppendChild(stateItem);
            }

            function.AppendChild(viewItemsCollection);
            handler.AppendChild(function);

            return handler;
        }

        private XmlElement CreateNestedForEachAction(XmlDocument doc, ViewPairInfo nestedViewPair,
            Dictionary<string, ViewPairInfo> allViewPairs)
        {
            // This creates a ForEach action that will iterate through the nested section's records
            // for the current parent record being processed
            XmlElement action = doc.CreateElement("Action");
            action.SetAttribute("ID", Guid.NewGuid().ToString());
            action.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            action.SetAttribute("Type", "ForEach");
            action.SetAttribute("ExecutionType", "Synchronous");

            XmlElement props = doc.CreateElement("Properties");

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", "Form");
            props.AppendChild(locationProp);

            action.AppendChild(props);

            // Add the actual save action for nested records
            XmlElement actions = doc.CreateElement("Actions");
            XmlElement saveAction = CreateNestedRecordSaveAction(doc, nestedViewPair, allViewPairs);
            actions.AppendChild(saveAction);
            action.AppendChild(actions);

            // Add function to iterate through nested section records
            XmlElement function = doc.CreateElement("Function");
            function.SetAttribute("ID", Guid.NewGuid().ToString());

            if (!string.IsNullOrEmpty(nestedViewPair.ListViewInstanceId))
            {
                function.SetAttribute("InstanceID", nestedViewPair.ListViewInstanceId);
            }
            else
            {
                Console.WriteLine($"          Warning: Nested view {nestedViewPair.ListViewName} has no ListViewInstanceId");
                // Generate a new GUID as fallback
                function.SetAttribute("InstanceID", Guid.NewGuid().ToString());
            }

            XmlElement viewItemsCollection = doc.CreateElement("ViewItemsCollection");

            // Item for the nested view itself - only add if we have valid values
            if (!string.IsNullOrEmpty(nestedViewPair.ListViewId) && !string.IsNullOrEmpty(nestedViewPair.ListViewName))
            {
                XmlElement viewItem = doc.CreateElement("Item");
                viewItem.SetAttribute("SourceType", "View");
                viewItem.SetAttribute("SourceID", nestedViewPair.ListViewId);
                viewItem.SetAttribute("SourceName", nestedViewPair.ListViewName);
                viewItem.SetAttribute("SourceDisplayName", nestedViewPair.ListViewName);
                viewItem.SetAttribute("DataType", "Guid");
                viewItemsCollection.AppendChild(viewItem);
            }
            else
            {
                Console.WriteLine($"          Warning: Nested view {nestedViewPair.SectionName} has missing ListViewId or ListViewName");
            }

            // Item for the state filter (Added items)
            XmlElement stateItem = doc.CreateElement("Item");
            stateItem.SetAttribute("SourceType", "ItemState");
            stateItem.SetAttribute("SourceID", "Added");
            stateItem.SetAttribute("SourceName", "Added");
            stateItem.SetAttribute("SourceDisplayName", "Added");
            stateItem.SetAttribute("DataType", "Text");
            viewItemsCollection.AppendChild(stateItem);

            function.AppendChild(viewItemsCollection);
            action.AppendChild(function);

            return action;
        }

        private XmlElement CreateNestedRecordSaveAction(XmlDocument doc, ViewPairInfo nestedViewPair,
            Dictionary<string, ViewPairInfo> allViewPairs)
        {
            XmlElement action = doc.CreateElement("Action");
            action.SetAttribute("ID", Guid.NewGuid().ToString());
            action.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            action.SetAttribute("Type", "Execute");
            action.SetAttribute("ExecutionType", "Synchronous");
            action.SetAttribute("InstanceID", nestedViewPair.ListViewInstanceId);

            XmlElement props = doc.CreateElement("Properties");

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", "Form");
            props.AppendChild(locationProp);

            XmlElement viewIdProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, viewIdProp, "Name", "ViewID");
            XmlHelper.AddElement(doc, viewIdProp, "DisplayValue", nestedViewPair.ListViewName);
            XmlHelper.AddElement(doc, viewIdProp, "NameValue", nestedViewPair.ListViewName);
            XmlHelper.AddElement(doc, viewIdProp, "Value", nestedViewPair.ListViewId);
            props.AppendChild(viewIdProp);

            XmlElement methodProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, methodProp, "Name", "Method");
            XmlHelper.AddElement(doc, methodProp, "DisplayValue", "Create");
            XmlHelper.AddElement(doc, methodProp, "NameValue", "Create");
            XmlHelper.AddElement(doc, methodProp, "Value", "Create");
            props.AppendChild(methodProp);

            action.AppendChild(props);

            // Add enhanced parameters for nested parent ID resolution
            XmlElement parameters = CreateEnhancedChildRecordParameters(doc, nestedViewPair, allViewPairs);
            action.AppendChild(parameters);

            return action;
        }

        private XmlElement CreateEnhancedChildRecordParameters(XmlDocument doc, ViewPairInfo nestedViewPair,
            Dictionary<string, ViewPairInfo> allViewPairs)
        {
            XmlElement parameters = doc.CreateElement("Parameters");

            // For nested sections, the ParentID should come from the immediate parent section,
            // not the main form. This is the key improvement for nested section handling.
            XmlElement parentIdParam = doc.CreateElement("Parameter");

            if (nestedViewPair.IsNestedSection && !string.IsNullOrEmpty(nestedViewPair.ParentSectionName))
            {
                // Nested section: ParentID comes from the parent section's ID
                ViewPairInfo parentViewPair = null;
                if (allViewPairs.ContainsKey(nestedViewPair.ParentSectionName))
                {
                    parentViewPair = allViewPairs[nestedViewPair.ParentSectionName];
                }

                if (parentViewPair != null && !string.IsNullOrEmpty(parentViewPair.ListViewInstanceId))
                {
                    // Source: The ID of the currently processed parent record
                    parentIdParam.SetAttribute("SourceInstanceID", parentViewPair.ListViewInstanceId);
                    parentIdParam.SetAttribute("SourceID", "ID");
                    parentIdParam.SetAttribute("SourceName", "ID");
                    parentIdParam.SetAttribute("SourceDisplayName", "Parent Record ID");
                    parentIdParam.SetAttribute("SourceType", "ObjectProperty");

                    Console.WriteLine($"            Enhanced: Nested section {nestedViewPair.SectionName} will use ParentID from {nestedViewPair.ParentSectionName}");
                }
                else
                {
                    // Fallback to form parameter if parent not found
                    parentIdParam.SetAttribute("SourceID", "ID");
                    parentIdParam.SetAttribute("SourceName", "ID");
                    parentIdParam.SetAttribute("SourceType", "FormParameter");

                    Console.WriteLine($"            Warning: Parent section {nestedViewPair.ParentSectionName} not found, using form parameter");
                }
            }
            else
            {
                // First-level child: ParentID comes from main form
                parentIdParam.SetAttribute("SourceID", "ID");
                parentIdParam.SetAttribute("SourceName", "ID");
                parentIdParam.SetAttribute("SourceType", "FormParameter");

                Console.WriteLine($"            Standard: First-level section {nestedViewPair.SectionName} will use ParentID from main form");
            }

            // Target: Always the ParentID field in the child SmartObject
            parentIdParam.SetAttribute("TargetInstanceID", nestedViewPair.ListViewInstanceId);
            parentIdParam.SetAttribute("TargetID", "ParentID");
            parentIdParam.SetAttribute("TargetName", "ParentID");
            parentIdParam.SetAttribute("TargetDisplayName", "Parent ID");
            parentIdParam.SetAttribute("TargetType", "ObjectProperty");
            parameters.AppendChild(parentIdParam);

            // Map all the list view controls to their SmartObject properties (same as original)
            var listControls = ControlMappingService.GetViewControls(nestedViewPair.ListViewName);
            if (listControls != null)
            {
                foreach (var control in listControls)
                {
                    string smoPropN = !string.IsNullOrEmpty(control.Value.SmoPropertyName) ? control.Value.SmoPropertyName : control.Value.FieldName;
                    string smoDispN = !string.IsNullOrEmpty(control.Value.SmoDisplayName) ? control.Value.SmoDisplayName : control.Value.FieldName;

                    XmlElement param = doc.CreateElement("Parameter");
                    param.SetAttribute("SourceInstanceID", nestedViewPair.ListViewInstanceId);
                    param.SetAttribute("SourceID", control.Value.ControlId);
                    param.SetAttribute("SourceName", control.Value.ControlName);
                    param.SetAttribute("SourceDisplayName", control.Value.ControlName);
                    param.SetAttribute("SourceType", "Control");
                    param.SetAttribute("TargetInstanceID", nestedViewPair.ListViewInstanceId);
                    param.SetAttribute("TargetID", smoPropN);
                    param.SetAttribute("TargetName", smoPropN);
                    param.SetAttribute("TargetDisplayName", smoDispN);
                    param.SetAttribute("TargetType", "ObjectProperty");
                    parameters.AppendChild(param);
                }

                Console.WriteLine($"            Mapped {listControls.Count} controls for {nestedViewPair.SectionName}");
            }

            return parameters;
        }

        private void AddSubmitFormRule(XmlDocument doc, XmlElement eventsElement,
      string formId, string formName, string submitButtonId,
      Dictionary<string, ViewPairInfo> viewPairs)
        {
            Console.WriteLine($"\n      === Creating Submit Form Rule ===");

            // TEMPORARY: Disable nesting analysis to prevent nullable errors
            // AnalyzeAndPopulateNestingHierarchy(viewPairs);

            XmlElement submitEvent = doc.CreateElement("Event");
            string submitEventId = Guid.NewGuid().ToString();
            submitEvent.SetAttribute("ID", submitEventId);
            submitEvent.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            submitEvent.SetAttribute("Type", "User");
            submitEvent.SetAttribute("SourceID", submitButtonId);
            submitEvent.SetAttribute("SourceType", "Control");
            submitEvent.SetAttribute("SourceName", "Submit");
            submitEvent.SetAttribute("SourceDisplayName", "Submit");
            submitEvent.SetAttribute("IsExtended", "True");

            XmlHelper.AddElement(doc, submitEvent, "Name", "OnClick");

            // Add properties
            XmlElement props = doc.CreateElement("Properties");

            XmlElement ruleFriendlyProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, ruleFriendlyProp, "Name", "RuleFriendlyName");
            XmlHelper.AddElement(doc, ruleFriendlyProp, "Value", "When Submit is Clicked");
            props.AppendChild(ruleFriendlyProp);

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", formName);
            props.AppendChild(locationProp);

            // NOTE: Do NOT add ViewID to event Properties for form-level control events.
            // K2 Designer expects only RuleFriendlyName + Location here.

            submitEvent.AppendChild(props);

            // Create handlers
            XmlElement handlers = doc.CreateElement("Handlers");

            // Handler 1: Save main form data
            XmlElement mainHandler = CreateMainFormSaveHandler(doc, formId, formName, viewPairs);
            handlers.AppendChild(mainHandler);

            // Handler 2: Use original ForEach handler approach (enhanced logic disabled)
            foreach (var viewPair in viewPairs.Values)
            {
                if (!string.IsNullOrEmpty(viewPair.ListViewId))
                {
                    XmlElement forEachHandler = CreateForEachListHandler(doc, formId, formName, viewPair);
                    handlers.AppendChild(forEachHandler);
                }
            }

            // Handler 3: Clear form after save
            XmlElement clearHandler = CreateClearAfterSaveHandler(doc, formId, formName);
            handlers.AppendChild(clearHandler);

            submitEvent.AppendChild(handlers);
            eventsElement.AppendChild(submitEvent);
            Console.WriteLine($"        ✓ Submit button rule created with proper parent-child mapping");
        }

        private XmlElement CreateMainFormSaveHandler(XmlDocument doc, string formId, string formName,
            Dictionary<string, ViewPairInfo> viewPairs)
        {
            XmlElement handler = doc.CreateElement("Handler");
            handler.SetAttribute("ID", Guid.NewGuid().ToString());
            handler.SetAttribute("DefinitionID", Guid.NewGuid().ToString());

            XmlElement handlerProps = doc.CreateElement("Properties");
            XmlElement handlerNameProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, handlerNameProp, "Name", "HandlerName");
            XmlHelper.AddElement(doc, handlerNameProp, "Value", "IfLogicalHandler");
            handlerProps.AppendChild(handlerNameProp);

            XmlElement handlerLocationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, handlerLocationProp, "Name", "Location");
            XmlHelper.AddElement(doc, handlerLocationProp, "Value", "form");
            handlerProps.AppendChild(handlerLocationProp);

            handler.AppendChild(handlerProps);

            XmlElement actions = doc.CreateElement("Actions");

            // Find the main form view (not list/item views)
            XmlNodeList items = doc.SelectNodes("//Panel/Areas/Area/Items/Item[@ViewID]");
            foreach (XmlElement item in items)
            {
                string viewName = item.SelectSingleNode("Name")?.InnerText ?? "";

                // Save main entity view (e.g., Travel_Request_TRAVEL_REQUEST)
                if (!viewName.Contains("List") && !viewName.Contains("Item") &&
                    !viewName.Contains("Part") && !viewName.Contains("Trips"))
                {
                    string viewId = item.GetAttribute("ViewID");
                    string instanceId = item.GetAttribute("ID");

                    Console.WriteLine($"          Adding Create action for main view: {viewName}");

                    XmlElement createAction = CreateMainFormCreateAction(doc, viewId, viewName, instanceId, item);
                    actions.AppendChild(createAction);
                }
            }

            handler.AppendChild(actions);
            return handler;
        }

        private XmlElement CreateMainFormCreateAction(XmlDocument doc, string viewId, string viewName,
       string instanceId, XmlElement viewItem)
        {
            XmlElement action = doc.CreateElement("Action");
            action.SetAttribute("ID", Guid.NewGuid().ToString());
            action.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            action.SetAttribute("Type", "Execute");
            action.SetAttribute("ExecutionType", "Synchronous");
            action.SetAttribute("InstanceID", instanceId);

            XmlElement props = doc.CreateElement("Properties");

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", "Form");
            props.AppendChild(locationProp);

            XmlElement viewIdProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, viewIdProp, "Name", "ViewID");
            XmlHelper.AddElement(doc, viewIdProp, "DisplayValue", viewName);
            XmlHelper.AddElement(doc, viewIdProp, "NameValue", viewName);
            XmlHelper.AddElement(doc, viewIdProp, "Value", viewId);
            props.AppendChild(viewIdProp);

            XmlElement methodProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, methodProp, "Name", "Method");
            XmlHelper.AddElement(doc, methodProp, "DisplayValue", "Create");
            XmlHelper.AddElement(doc, methodProp, "NameValue", "Create");
            XmlHelper.AddElement(doc, methodProp, "Value", "Create");
            props.AppendChild(methodProp);

            action.AppendChild(props);

            // Add parameters to map ALL controls from the main view and other views to SmartObject properties
            XmlElement parameters = CreateMainFormParameters(doc, instanceId, viewName);
            if (parameters != null && parameters.HasChildNodes)
            {
                action.AppendChild(parameters);
            }

            // Add result to capture the ID
            XmlElement results = doc.CreateElement("Results");
            XmlElement result = doc.CreateElement("Result");
            result.SetAttribute("SourceID", "ID");
            result.SetAttribute("SourceName", "ID");
            result.SetAttribute("SourceDisplayName", "ID");
            result.SetAttribute("SourceType", "ObjectProperty");
            result.SetAttribute("TargetID", "ID");
            result.SetAttribute("TargetName", "ID");
            result.SetAttribute("TargetType", "FormParameter");
            results.AppendChild(result);
            action.AppendChild(results);

            return action;
        }

        private XmlElement CreateMainFormParameters(XmlDocument doc, string mainViewInstanceId, string mainViewName)
        {
            XmlElement parameters = doc.CreateElement("Parameters");

            // We need to map controls from all views (main and part views) to the main SmartObject
            // Look through all the views in the form to find controls
            XmlNodeList items = doc.SelectNodes("//Panel/Areas/Area/Items/Item[@ViewID]");

            foreach (XmlElement item in items)
            {
                string viewName = item.SelectSingleNode("Name")?.InnerText ?? "";
                string viewId = item.GetAttribute("ViewID");
                string instanceId = item.GetAttribute("ID");

                // Include main view and "Part" views but exclude List/Item views
                if (!viewName.Contains("List") && !viewName.Contains("Item"))
                {
                    // Get the controls for this view from the ControlMappingService
                    var viewControls = ControlMappingService.GetViewControls(viewName);

                    if (viewControls != null)
                    {
                        foreach (var control in viewControls)
                        {
                            // Resolve SmartObject property name and display name
                            string smoPropertyName = !string.IsNullOrEmpty(control.Value.SmoPropertyName) ? control.Value.SmoPropertyName : control.Value.FieldName.ToUpper();
                            string smoDisplayName = !string.IsNullOrEmpty(control.Value.SmoDisplayName) ? control.Value.SmoDisplayName : GetFieldDisplayName(control.Value.FieldName);

                            // Create parameter mapping for each control
                            XmlElement param = doc.CreateElement("Parameter");
                            param.SetAttribute("SourceInstanceID", instanceId);
                            param.SetAttribute("SourceID", control.Value.ControlId);
                            param.SetAttribute("SourceName", control.Value.ControlName);
                            param.SetAttribute("SourceDisplayName", control.Value.ControlName);
                            param.SetAttribute("SourceType", "Control");

                            // Target is the SmartObject property for the Create action
                            param.SetAttribute("TargetInstanceID", mainViewInstanceId);
                            param.SetAttribute("TargetID", smoPropertyName);
                            param.SetAttribute("TargetName", smoPropertyName);
                            param.SetAttribute("TargetDisplayName", smoDisplayName);
                            param.SetAttribute("TargetType", "ObjectProperty");

                            parameters.AppendChild(param);

                            Console.WriteLine($"            Mapped control {control.Value.ControlName} to SmartObject property {smoPropertyName} ({smoDisplayName})");
                        }
                    }
                }
            }

            return parameters;
        }

        private bool IsSimpleNestedSection(string sectionName)
        {
            // Simple detection: sections with "Table_CTRL" pattern are likely nested tables
            if (string.IsNullOrEmpty(sectionName))
                return false;

            return sectionName.StartsWith("Table_CTRL", StringComparison.OrdinalIgnoreCase);
        }

        private string GetFieldDisplayName(string fieldName)
        {
            // Convert field name to display name (e.g., BUSINESSPURPOSE -> Business purpose)
            if (string.IsNullOrEmpty(fieldName))
                return fieldName;

            string displayName = fieldName;

            // Apply special case replacements FIRST (before ToTitleCase which changes case)
            string upper = displayName.ToUpper();
            if (upper == "EMAILADDRESS" || upper == "EMAIL") return upper == "EMAIL" ? "E-mail address" : "E-mail address";
            if (upper == "REQUESTDATE") return "Request date";
            if (upper == "BUSINESSPURPOSE" || upper == "PURPOSE") return upper == "PURPOSE" ? "Business purpose" : "Business purpose";
            if (upper == "TRIPCLASS") return "Trip class";
            if (upper == "CARCLASS") return "Car class";
            if (upper == "SEATLOCATION") return "Seat location";
            if (upper == "NONSMOKINGHOTELROOMREQUIRED" || upper == "NONSMOKINGROOM") return "Non-smoking hotel room required";
            if (upper == "NOTES") return "Notes";
            if (upper == "NAME") return "Name";
            if (upper == "DEPARTURELOCATION") return "Departure location";
            if (upper == "DESTINATION") return "Destination location";
            if (upper == "DEPARTUREDATE") return "Departure date";
            if (upper == "DEPARTURETIME") return "Departure time";
            if (upper == "INCLUDEHOTEL") return "Include hotel";
            if (upper == "INCLUDECAR" || upper == "INCLUDECARRENTAL") return "Include car rental";
            if (upper == "ISROUNDTRIP" || upper == "ROUNDTRIP") return "Round trip";
            if (upper == "RETURNDATE") return "Return date";
            if (upper == "RETURNTIME") return "Return time";

            // Insert spaces before capital letters (for camelCase)
            displayName = System.Text.RegularExpressions.Regex.Replace(displayName, "([a-z])([A-Z])", "$1 $2");

            // Handle all caps fields - convert to title case
            if (displayName == displayName.ToUpper())
            {
                displayName = displayName.ToLower();
                displayName = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(displayName);
            }

            // Ensure first letter is uppercase
            if (displayName.Length > 0)
            {
                displayName = char.ToUpper(displayName[0]) + displayName.Substring(1);
            }

            return displayName;
        }

        private XmlElement CreateForEachListHandler(XmlDocument doc, string formId, string formName,
    ViewPairInfo viewPair)
        {
            XmlElement handler = doc.CreateElement("Handler");
            handler.SetAttribute("ID", Guid.NewGuid().ToString());
            handler.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            handler.SetAttribute("Type", "ForEach");

            XmlElement handlerProps = doc.CreateElement("Properties");
            XmlElement handlerNameProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, handlerNameProp, "Name", "HandlerName");
            XmlHelper.AddElement(doc, handlerNameProp, "Value", "ForEachListViewRowHandler");
            handlerProps.AppendChild(handlerNameProp);

            XmlElement handlerLocationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, handlerLocationProp, "Name", "Location");
            XmlHelper.AddElement(doc, handlerLocationProp, "Value", "form");
            handlerProps.AppendChild(handlerLocationProp);

            handler.AppendChild(handlerProps);

            // Add action to create child records
            XmlElement actions = doc.CreateElement("Actions");
            XmlElement createAction = CreateChildRecordAction(doc, viewPair);
            actions.AppendChild(createAction);
            handler.AppendChild(actions);

            // Add function to iterate through list items
            XmlElement function = doc.CreateElement("Function");
            function.SetAttribute("ID", Guid.NewGuid().ToString());
            function.SetAttribute("InstanceID", viewPair.ListViewInstanceId);

            XmlElement viewItemsCollection = doc.CreateElement("ViewItemsCollection");

            // Item for the view itself
            XmlElement viewItem = doc.CreateElement("Item");
            viewItem.SetAttribute("SourceType", "View");
            viewItem.SetAttribute("SourceID", viewPair.ListViewId);
            viewItem.SetAttribute("SourceName", viewPair.ListViewName);
            viewItem.SetAttribute("SourceDisplayName", viewPair.ListViewName);
            viewItem.SetAttribute("DataType", "Guid");
            viewItemsCollection.AppendChild(viewItem);

            // Item for the state filter (Added items)
            XmlElement stateItem = doc.CreateElement("Item");
            stateItem.SetAttribute("SourceType", "ItemState");
            stateItem.SetAttribute("SourceID", "Added");
            stateItem.SetAttribute("SourceName", "Added");
            stateItem.SetAttribute("SourceDisplayName", "Added");
            stateItem.SetAttribute("DataType", "Text");
            viewItemsCollection.AppendChild(stateItem);

            function.AppendChild(viewItemsCollection);
            handler.AppendChild(function);

            return handler;
        }

        private XmlElement CreateChildRecordAction(XmlDocument doc, ViewPairInfo viewPair)
        {
            XmlElement action = doc.CreateElement("Action");
            action.SetAttribute("ID", Guid.NewGuid().ToString());
            action.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            action.SetAttribute("Type", "Execute");
            action.SetAttribute("ExecutionType", "Synchronous");
            action.SetAttribute("InstanceID", viewPair.ListViewInstanceId);

            XmlElement props = doc.CreateElement("Properties");

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", "Form");
            props.AppendChild(locationProp);

            XmlElement viewIdProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, viewIdProp, "Name", "ViewID");
            XmlHelper.AddElement(doc, viewIdProp, "DisplayValue", viewPair.ListViewName);
            XmlHelper.AddElement(doc, viewIdProp, "NameValue", viewPair.ListViewName);
            XmlHelper.AddElement(doc, viewIdProp, "Value", viewPair.ListViewId);
            props.AppendChild(viewIdProp);

            XmlElement methodProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, methodProp, "Name", "Method");
            XmlHelper.AddElement(doc, methodProp, "DisplayValue", "Create");
            XmlHelper.AddElement(doc, methodProp, "NameValue", "Create");
            XmlHelper.AddElement(doc, methodProp, "Value", "Create");
            props.AppendChild(methodProp);

            action.AppendChild(props);

            // Add parameters - map Parent ID and all list view controls
            XmlElement parameters = CreateChildRecordParameters(doc, viewPair);
            action.AppendChild(parameters);

            return action;
        }

        private XmlElement CreateChildRecordParameters(XmlDocument doc, ViewPairInfo viewPair)
        {
            XmlElement parameters = doc.CreateElement("Parameters");

            // Enhanced: Detect nested sections using simple naming convention
            bool isNestedSection = IsSimpleNestedSection(viewPair.SectionName);

            // First parameter: Map appropriate parent ID to ParentID field
            XmlElement parentIdParam = doc.CreateElement("Parameter");
            parentIdParam.SetAttribute("SourceID", "ID");
            parentIdParam.SetAttribute("SourceName", "ID");
            parentIdParam.SetAttribute("SourceType", "FormParameter");
            parentIdParam.SetAttribute("TargetInstanceID", viewPair.ListViewInstanceId);
            parentIdParam.SetAttribute("TargetID", "ParentID");
            parentIdParam.SetAttribute("TargetName", "ParentID");
            parentIdParam.SetAttribute("TargetDisplayName", "Parent ID");
            parentIdParam.SetAttribute("TargetType", "ObjectProperty");

            if (isNestedSection)
            {
                Console.WriteLine($"            Detected nested section: {viewPair.SectionName} - using standard FormParameter approach");
                // For now, still use FormParameter but log that we detected a nested section
                // Future enhancement: could implement parent section ID resolution here
            }

            parameters.AppendChild(parentIdParam);

            // Map all the list view controls to their SmartObject properties
            // You'll need to get these mappings from the view controls
            // This is a simplified example - you'd need to get actual control IDs
            var listControls = ControlMappingService.GetViewControls(viewPair.ListViewName);
            if (listControls != null)
            {
                foreach (var control in listControls)
                {
                    string smoPropN = !string.IsNullOrEmpty(control.Value.SmoPropertyName) ? control.Value.SmoPropertyName : control.Value.FieldName;
                    string smoDispN = !string.IsNullOrEmpty(control.Value.SmoDisplayName) ? control.Value.SmoDisplayName : control.Value.FieldName;

                    XmlElement param = doc.CreateElement("Parameter");
                    param.SetAttribute("SourceInstanceID", viewPair.ListViewInstanceId);
                    param.SetAttribute("SourceID", control.Value.ControlId);
                    param.SetAttribute("SourceName", control.Value.ControlName);
                    param.SetAttribute("SourceDisplayName", control.Value.ControlName);
                    param.SetAttribute("SourceType", "Control");
                    param.SetAttribute("TargetInstanceID", viewPair.ListViewInstanceId);
                    param.SetAttribute("TargetID", smoPropN);
                    param.SetAttribute("TargetName", smoPropN);
                    param.SetAttribute("TargetDisplayName", smoDispN);
                    param.SetAttribute("TargetType", "ObjectProperty");
                    parameters.AppendChild(param);
                }
            }

            return parameters;
        }

        private XmlElement CreateClearAfterSaveHandler(XmlDocument doc, string formId, string formName)
        {
            XmlElement handler = doc.CreateElement("Handler");
            handler.SetAttribute("ID", Guid.NewGuid().ToString());
            handler.SetAttribute("DefinitionID", Guid.NewGuid().ToString());

            XmlElement handlerProps = doc.CreateElement("Properties");
            XmlElement handlerNameProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, handlerNameProp, "Name", "HandlerName");
            XmlHelper.AddElement(doc, handlerNameProp, "Value", "IfLogicalHandler");
            handlerProps.AppendChild(handlerNameProp);

            XmlElement handlerLocationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, handlerLocationProp, "Name", "Location");
            XmlHelper.AddElement(doc, handlerLocationProp, "Value", "form");
            handlerProps.AppendChild(handlerLocationProp);

            handler.AppendChild(handlerProps);

            // Execute the Clear event
            XmlElement actions = doc.CreateElement("Actions");
            XmlElement executeAction = doc.CreateElement("Action");
            executeAction.SetAttribute("ID", Guid.NewGuid().ToString());
            executeAction.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            executeAction.SetAttribute("Type", "Execute");
            executeAction.SetAttribute("ExecutionType", "Synchronous");

            XmlElement actionProps = doc.CreateElement("Properties");

            XmlElement actionLocationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, actionLocationProp, "Name", "Location");
            XmlHelper.AddElement(doc, actionLocationProp, "Value", "Form");
            actionProps.AppendChild(actionLocationProp);

            XmlElement eventIdProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, eventIdProp, "Name", "EventID");
            XmlHelper.AddElement(doc, eventIdProp, "DisplayValue", "When Clear Form Button is Clicked");

            // Find the Clear button event ID
            string clearEventId = FindClearButtonEventId(doc);
            XmlHelper.AddElement(doc, eventIdProp, "Value", clearEventId);
            actionProps.AppendChild(eventIdProp);

            executeAction.AppendChild(actionProps);
            actions.AppendChild(executeAction);
            handler.AppendChild(actions);

            return handler;
        }

        private string FindClearButtonEventId(XmlDocument doc)
        {
            // Find the Clear button click event
            XmlNodeList events = doc.SelectNodes("//Event[@SourceName='Clear Form Button']");
            if (events.Count > 0)
            {
                XmlElement clearEvent = (XmlElement)events[0];
                return clearEvent.GetAttribute("DefinitionID");
            }
            return Guid.NewGuid().ToString();
        }

        private XmlElement CreateMainFormParameters(XmlDocument doc, XmlElement viewItem, string instanceId)
        {
            XmlElement parameters = doc.CreateElement("Parameters");

            // You would need to map the actual controls from the view
            // This is a simplified example - in reality you'd need to get the control mappings
            // from the view definition and map them to SmartObject properties

            return parameters;
        }

        private XmlElement CreateSaveViewAction(XmlDocument doc, string viewId,
            string viewName, string instanceId, string method)
        {
            XmlElement action = doc.CreateElement("Action");
            action.SetAttribute("ID", Guid.NewGuid().ToString());
            action.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            action.SetAttribute("Type", "Execute");
            action.SetAttribute("ExecutionType", "Synchronous");
            action.SetAttribute("InstanceID", instanceId);

            XmlElement props = doc.CreateElement("Properties");

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", "Form");
            props.AppendChild(locationProp);

            XmlElement viewIdProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, viewIdProp, "Name", "ViewID");
            XmlHelper.AddElement(doc, viewIdProp, "DisplayValue", viewName);
            XmlHelper.AddElement(doc, viewIdProp, "NameValue", viewName);
            XmlHelper.AddElement(doc, viewIdProp, "Value", viewId);
            props.AppendChild(viewIdProp);

            XmlElement methodProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, methodProp, "Name", "Method");
            XmlHelper.AddElement(doc, methodProp, "DisplayValue", method);
            XmlHelper.AddElement(doc, methodProp, "NameValue", method);
            XmlHelper.AddElement(doc, methodProp, "Value", method);
            props.AppendChild(methodProp);

            action.AppendChild(props);
            return action;
        }

        private XmlElement CreateSaveListAction(XmlDocument doc, string viewId,
            string viewName, string instanceId)
        {
            XmlElement action = doc.CreateElement("Action");
            action.SetAttribute("ID", Guid.NewGuid().ToString());
            action.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            action.SetAttribute("Type", "Execute");
            action.SetAttribute("ExecutionType", "Synchronous");
            action.SetAttribute("InstanceID", instanceId);

            XmlElement props = doc.CreateElement("Properties");

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", "Form");
            props.AppendChild(locationProp);

            XmlElement viewIdProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, viewIdProp, "Name", "ViewID");
            XmlHelper.AddElement(doc, viewIdProp, "DisplayValue", viewName);
            XmlHelper.AddElement(doc, viewIdProp, "NameValue", viewName);
            XmlHelper.AddElement(doc, viewIdProp, "Value", viewId);
            props.AppendChild(viewIdProp);

            XmlElement methodProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, methodProp, "Name", "Method");
            XmlHelper.AddElement(doc, methodProp, "DisplayValue", "Save");
            XmlHelper.AddElement(doc, methodProp, "NameValue", "Save");
            XmlHelper.AddElement(doc, methodProp, "Value", "Save");
            props.AppendChild(methodProp);

            action.AppendChild(props);
            return action;
        }

        private XmlElement CreateMessageAction(XmlDocument doc, string message)
        {
            XmlElement action = doc.CreateElement("Action");
            action.SetAttribute("ID", Guid.NewGuid().ToString());
            action.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            action.SetAttribute("Type", "ShowMessage");
            action.SetAttribute("ExecutionType", "Synchronous");

            XmlElement props = doc.CreateElement("Properties");

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", "Form");
            props.AppendChild(locationProp);

            XmlElement messageTitleProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, messageTitleProp, "Name", "Title");
            XmlHelper.AddElement(doc, messageTitleProp, "Value", "Success");
            props.AppendChild(messageTitleProp);

            XmlElement messageBodyProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, messageBodyProp, "Name", "Message");
            XmlHelper.AddElement(doc, messageBodyProp, "Value", message);
            props.AppendChild(messageBodyProp);

            XmlElement messageTypeProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, messageTypeProp, "Name", "MessageBoxType");
            XmlHelper.AddElement(doc, messageTypeProp, "Value", "Information");
            props.AppendChild(messageTypeProp);

            action.AppendChild(props);

            return action;
        }


        private void AddComprehensiveItemViewAddButtonRule(XmlDocument doc, XmlElement eventsElement,
                                                  ViewPairInfo viewPair, string buttonId, string buttonName)
        {
            Console.WriteLine($"        Creating Comprehensive Item View Add Button Rule");
            Console.WriteLine($"          Button ID: {buttonId}");
            Console.WriteLine($"          Button Name: {buttonName}");

            XmlElement eventElement = doc.CreateElement("Event");
            string eventGuid = Guid.NewGuid().ToString();
            eventElement.SetAttribute("ID", eventGuid);
            eventElement.SetAttribute("DefinitionID", Guid.NewGuid().ToString()); // Always new GUID
            eventElement.SetAttribute("Type", "User");
            eventElement.SetAttribute("SourceID", buttonId);
            eventElement.SetAttribute("SourceType", "Control");
            eventElement.SetAttribute("SourceName", buttonName);
            eventElement.SetAttribute("SourceDisplayName", buttonName);
            eventElement.SetAttribute("IsExtended", "True");
            eventElement.SetAttribute("InstanceID", viewPair.ItemViewInstanceId ?? viewPair.ItemViewId);

            XmlHelper.AddElement(doc, eventElement, "Name", "OnClick");

            XmlElement eventProps = doc.CreateElement("Properties");

            XmlElement viewIdProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, viewIdProp, "Name", "ViewID");
            XmlHelper.AddElement(doc, viewIdProp, "DisplayValue", viewPair.ItemViewName);
            XmlHelper.AddElement(doc, viewIdProp, "NameValue", viewPair.ItemViewName);
            XmlHelper.AddElement(doc, viewIdProp, "Value", viewPair.ItemViewId);
            eventProps.AppendChild(viewIdProp);

            XmlElement ruleProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, ruleProp, "Name", "RuleFriendlyName");
            XmlHelper.AddElement(doc, ruleProp, "Value", $"On {viewPair.ItemViewName}, when {buttonName} is Clicked");
            eventProps.AppendChild(ruleProp);

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", viewPair.ItemViewName);
            eventProps.AppendChild(locationProp);

            eventElement.AppendChild(eventProps);

            XmlElement handlers = doc.CreateElement("Handlers");
            XmlElement handler = CreateComprehensiveItemToListHandler(doc, viewPair);
            handlers.AppendChild(handler);
            eventElement.AppendChild(handlers);

            eventsElement.AppendChild(eventElement);
            Console.WriteLine($"        ✓ Comprehensive rule created with Event ID: {eventGuid}");
        }

        private void AddItemViewCancelButtonRule(XmlDocument doc, XmlElement eventsElement,
                                             ViewPairInfo viewPair, string buttonId, string buttonName)
        {
            Console.WriteLine($"        Creating Item View Cancel Button Rule");
            Console.WriteLine($"          Button ID: {buttonId}");
            Console.WriteLine($"          Button Name: {buttonName}");

            XmlElement eventElement = doc.CreateElement("Event");
            string eventGuid = Guid.NewGuid().ToString();
            eventElement.SetAttribute("ID", eventGuid);
            eventElement.SetAttribute("DefinitionID", Guid.NewGuid().ToString()); // Always new GUID
            eventElement.SetAttribute("Type", "User");
            eventElement.SetAttribute("SourceID", buttonId);
            eventElement.SetAttribute("SourceType", "Control");
            eventElement.SetAttribute("SourceName", buttonName);
            eventElement.SetAttribute("SourceDisplayName", buttonName);
            eventElement.SetAttribute("IsExtended", "True");
            eventElement.SetAttribute("InstanceID", viewPair.ItemViewInstanceId ?? viewPair.ItemViewId);

            XmlHelper.AddElement(doc, eventElement, "Name", "OnClick");

            XmlElement eventProps = doc.CreateElement("Properties");

            XmlElement viewIdProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, viewIdProp, "Name", "ViewID");
            XmlHelper.AddElement(doc, viewIdProp, "DisplayValue", viewPair.ItemViewName);
            XmlHelper.AddElement(doc, viewIdProp, "NameValue", viewPair.ItemViewName);
            XmlHelper.AddElement(doc, viewIdProp, "Value", viewPair.ItemViewId);
            eventProps.AppendChild(viewIdProp);

            XmlElement ruleProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, ruleProp, "Name", "RuleFriendlyName");
            XmlHelper.AddElement(doc, ruleProp, "Value", $"On {viewPair.ItemViewName}, when {buttonName} is Clicked");
            eventProps.AppendChild(ruleProp);

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", viewPair.ItemViewName);
            eventProps.AppendChild(locationProp);

            eventElement.AppendChild(eventProps);

            XmlElement handlers = doc.CreateElement("Handlers");
            XmlElement handler = CreateVisibilityHandler(doc, viewPair, false);
            handlers.AppendChild(handler);
            eventElement.AppendChild(handlers);

            eventsElement.AppendChild(eventElement);
            Console.WriteLine($"        ✓ Rule created with Event ID: {eventGuid}");
        }

        /// <summary>
        /// Rule 4: Delete ToolBar Button clicked on List View → RemoveItem
        /// When the user selects a row in the list and clicks the Delete toolbar button,
        /// this rule executes RemoveItem to remove the selected row from the list.
        /// </summary>
        private void AddDeleteToolbarButtonRule(XmlDocument doc, XmlElement eventsElement,
                                            ViewPairInfo viewPair, string buttonId, string buttonName)
        {
            Console.WriteLine($"        Creating Delete ToolBar Button Rule");
            Console.WriteLine($"          Button ID: {buttonId}");
            Console.WriteLine($"          Button Name: {buttonName}");
            Console.WriteLine($"          List View: {viewPair.ListViewName}");
            Console.WriteLine($"          List View ID: {viewPair.ListViewId}");
            Console.WriteLine($"          List View Instance ID: {viewPair.ListViewInstanceId ?? "NULL"}");

            // ── Event: OnClick for Delete ToolBar Button on the List view ──
            XmlElement eventElement = doc.CreateElement("Event");
            string eventGuid = Guid.NewGuid().ToString();
            eventElement.SetAttribute("ID", eventGuid);
            eventElement.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            eventElement.SetAttribute("Type", "User");
            eventElement.SetAttribute("SourceID", buttonId);
            eventElement.SetAttribute("SourceType", "Control");
            eventElement.SetAttribute("SourceName", buttonName);
            eventElement.SetAttribute("SourceDisplayName", buttonName);
            eventElement.SetAttribute("IsExtended", "True");
            eventElement.SetAttribute("InstanceID", viewPair.ListViewInstanceId ?? viewPair.ListViewId);

            XmlHelper.AddElement(doc, eventElement, "Name", "OnClick");

            // Event Properties
            XmlElement eventProps = doc.CreateElement("Properties");

            XmlElement viewIdProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, viewIdProp, "Name", "ViewID");
            XmlHelper.AddElement(doc, viewIdProp, "DisplayValue", viewPair.ListViewName);
            XmlHelper.AddElement(doc, viewIdProp, "NameValue", viewPair.ListViewName);
            XmlHelper.AddElement(doc, viewIdProp, "Value", viewPair.ListViewId);
            eventProps.AppendChild(viewIdProp);

            XmlElement ruleProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, ruleProp, "Name", "RuleFriendlyName");
            XmlHelper.AddElement(doc, ruleProp, "Value", $"On {viewPair.ListViewName}, when {buttonName} is Clicked");
            eventProps.AppendChild(ruleProp);

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", viewPair.ListViewName);
            eventProps.AppendChild(locationProp);

            eventElement.AppendChild(eventProps);

            // ── Handler: IfLogicalHandler with RemoveItem action ──
            XmlElement handlers = doc.CreateElement("Handlers");

            XmlElement handler = doc.CreateElement("Handler");
            string handlerId = Guid.NewGuid().ToString();
            handler.SetAttribute("ID", handlerId);
            handler.SetAttribute("DefinitionID", Guid.NewGuid().ToString());

            XmlElement handlerProps = doc.CreateElement("Properties");
            XmlElement handlerNameProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, handlerNameProp, "Name", "HandlerName");
            XmlHelper.AddElement(doc, handlerNameProp, "Value", "then");
            handlerProps.AppendChild(handlerNameProp);

            XmlElement handlerLocationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, handlerLocationProp, "Name", "Location");
            XmlHelper.AddElement(doc, handlerLocationProp, "Value", "form");
            handlerProps.AppendChild(handlerLocationProp);

            handler.AppendChild(handlerProps);

            // ── Action: RemoveItem on the List view ──
            XmlElement actions = doc.CreateElement("Actions");

            XmlElement removeAction = doc.CreateElement("Action");
            string actionId = Guid.NewGuid().ToString();
            removeAction.SetAttribute("ID", actionId);
            removeAction.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            removeAction.SetAttribute("Type", "List");
            removeAction.SetAttribute("ExecutionType", "Synchronous");
            removeAction.SetAttribute("InstanceID", viewPair.ListViewInstanceId ?? viewPair.ListViewId);

            XmlElement actionProps = doc.CreateElement("Properties");

            XmlElement actionLocationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, actionLocationProp, "Name", "Location");
            XmlHelper.AddElement(doc, actionLocationProp, "Value", "Form");
            actionProps.AppendChild(actionLocationProp);

            XmlElement actionViewIdProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, actionViewIdProp, "Name", "ViewID");
            XmlHelper.AddElement(doc, actionViewIdProp, "DisplayValue", viewPair.ListViewName);
            XmlHelper.AddElement(doc, actionViewIdProp, "NameValue", viewPair.ListViewName);
            XmlHelper.AddElement(doc, actionViewIdProp, "Value", viewPair.ListViewId);
            actionProps.AppendChild(actionViewIdProp);

            XmlElement methodProp = doc.CreateElement("Property");
            methodProp.SetAttribute("ValidationStatus", "Auto");
            methodProp.SetAttribute("ValidationMessages", "ActionMethod,ViewMethod,Auto,,RemoveItem,Remove item");
            XmlHelper.AddElement(doc, methodProp, "Name", "Method");
            XmlHelper.AddElement(doc, methodProp, "DisplayValue", "Remove item");
            XmlHelper.AddElement(doc, methodProp, "NameValue", "RemoveItem");
            XmlHelper.AddElement(doc, methodProp, "Value", "RemoveItem");
            actionProps.AppendChild(methodProp);

            removeAction.AppendChild(actionProps);
            actions.AppendChild(removeAction);

            handler.AppendChild(actions);
            handlers.AppendChild(handler);
            eventElement.AppendChild(handlers);

            eventsElement.AppendChild(eventElement);
            Console.WriteLine($"        ✓ Delete rule created: Event ID={eventGuid}, Action=RemoveItem");
        }

        // Keep all other existing methods as they are, but add similar debug logging...

        private XmlElement CreateComprehensiveItemToListHandler(XmlDocument doc, ViewPairInfo viewPair)
        {
            Console.WriteLine($"          Creating Comprehensive Item to List Handler");

            XmlElement handler = doc.CreateElement("Handler");
            handler.SetAttribute("ID", Guid.NewGuid().ToString());
            handler.SetAttribute("DefinitionID", Guid.NewGuid().ToString()); // Always new GUID

            XmlElement props = doc.CreateElement("Properties");
            XmlElement handlerNameProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, handlerNameProp, "Name", "HandlerName");
            XmlHelper.AddElement(doc, handlerNameProp, "Value", "then");
            props.AppendChild(handlerNameProp);

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", "form");
            props.AppendChild(locationProp);

            handler.AppendChild(props);

            XmlElement actions = doc.CreateElement("Actions");

            Console.WriteLine($"            Adding Add Row action");
            actions.AppendChild(CreateAddRowAction(doc, viewPair));

            Console.WriteLine($"            Adding Data Transfer action");
            actions.AppendChild(CreateDataTransferAction(doc, viewPair));

            Console.WriteLine($"            Adding Accept Item action");
            actions.AppendChild(CreateAcceptItemAction(doc, viewPair));

            Console.WriteLine($"            Adding Clear View action");
            actions.AppendChild(CreateClearViewAction(doc, viewPair));

            Console.WriteLine($"            Adding Hide Item View action");
            XmlElement hideItemAction = CreateViewVisibilityAction(doc,
                viewPair.ItemViewInstanceId ?? viewPair.ItemViewId,
                viewPair.ItemViewName, "Hide",
                viewPair.ItemViewId);
            actions.AppendChild(hideItemAction);

            Console.WriteLine($"            Adding Show List View action");
            XmlElement showListAction = CreateViewVisibilityAction(doc,
                viewPair.ListViewInstanceId ?? viewPair.ListViewId,
                viewPair.ListViewName, "Show",
                viewPair.ListViewId);
            actions.AppendChild(showListAction);

            handler.AppendChild(actions);
            return handler;
        }

        private XmlElement CreateAddRowAction(XmlDocument doc, ViewPairInfo viewPair)
        {
            XmlElement action = doc.CreateElement("Action");
            action.SetAttribute("ID", Guid.NewGuid().ToString());
            action.SetAttribute("DefinitionID", Guid.NewGuid().ToString()); // Always new GUID
            action.SetAttribute("Type", "List");
            action.SetAttribute("ExecutionType", "Synchronous");
            action.SetAttribute("InstanceID", viewPair.ListViewInstanceId ?? viewPair.ListViewId);

            XmlElement props = doc.CreateElement("Properties");

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", "Form");
            props.AppendChild(locationProp);

            XmlElement viewIdProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, viewIdProp, "Name", "ViewID");
            XmlHelper.AddElement(doc, viewIdProp, "DisplayValue", viewPair.ListViewName);
            XmlHelper.AddElement(doc, viewIdProp, "NameValue", viewPair.ListViewName);
            XmlHelper.AddElement(doc, viewIdProp, "Value", viewPair.ListViewId);
            props.AppendChild(viewIdProp);

            XmlElement methodProp = doc.CreateElement("Property");
            methodProp.SetAttribute("ValidationStatus", "Auto");
            XmlHelper.AddElement(doc, methodProp, "Name", "Method");
            XmlHelper.AddElement(doc, methodProp, "DisplayValue", "Add item");
            XmlHelper.AddElement(doc, methodProp, "NameValue", "AddItem");
            XmlHelper.AddElement(doc, methodProp, "Value", "AddItem");
            props.AppendChild(methodProp);

            action.AppendChild(props);

            return action;
        }

        private XmlElement CreateDataTransferAction(XmlDocument doc, ViewPairInfo viewPair)
        {
            XmlElement action = doc.CreateElement("Action");
            action.SetAttribute("ID", Guid.NewGuid().ToString());
            action.SetAttribute("DefinitionID", Guid.NewGuid().ToString()); // Always new GUID
            action.SetAttribute("Type", "Transfer");
            action.SetAttribute("ExecutionType", "Synchronous");

            XmlElement props = doc.CreateElement("Properties");

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", "Form");
            props.AppendChild(locationProp);

            XmlElement formIdProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, formIdProp, "Name", "FormID");
            XmlHelper.AddElement(doc, formIdProp, "DisplayValue", viewPair.FormName);
            XmlHelper.AddElement(doc, formIdProp, "NameValue", viewPair.FormName);
            XmlHelper.AddElement(doc, formIdProp, "Value", viewPair.FormId);
            props.AppendChild(formIdProp);

            action.AppendChild(props);

            XmlElement parameters = doc.CreateElement("Parameters");

            if (viewPair.FieldMappings != null && viewPair.FieldMappings.Count > 0)
            {
                foreach (var fieldMapping in viewPair.FieldMappings)
                {
                    XmlElement parameter = doc.CreateElement("Parameter");
                    parameter.SetAttribute("SourceInstanceID", viewPair.ItemViewInstanceId);
                    parameter.SetAttribute("SourceID", fieldMapping.ItemControlId);
                    parameter.SetAttribute("SourceName", fieldMapping.ItemControlName);
                    parameter.SetAttribute("SourceDisplayName", fieldMapping.ItemControlName);
                    parameter.SetAttribute("SourceType", "Control");
                    parameter.SetAttribute("TargetInstanceID", viewPair.ListViewInstanceId);
                    parameter.SetAttribute("TargetID", fieldMapping.ListControlId);
                    parameter.SetAttribute("TargetName", fieldMapping.ListControlName);
                    parameter.SetAttribute("TargetDisplayName", fieldMapping.ListControlName);
                    parameter.SetAttribute("TargetType", "Control");
                    parameters.AppendChild(parameter);
                }
            }

            action.AppendChild(parameters);
            return action;
        }

        private XmlElement CreateAcceptItemAction(XmlDocument doc, ViewPairInfo viewPair)
        {
            XmlElement action = doc.CreateElement("Action");
            action.SetAttribute("ID", Guid.NewGuid().ToString());
            action.SetAttribute("DefinitionID", Guid.NewGuid().ToString()); // Always new GUID
            action.SetAttribute("Type", "List");
            action.SetAttribute("ExecutionType", "Synchronous");
            action.SetAttribute("InstanceID", viewPair.ListViewInstanceId ?? viewPair.ListViewId);

            XmlElement props = doc.CreateElement("Properties");

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", "Form");
            props.AppendChild(locationProp);

            XmlElement viewIdProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, viewIdProp, "Name", "ViewID");
            XmlHelper.AddElement(doc, viewIdProp, "DisplayValue", viewPair.ListViewName);
            XmlHelper.AddElement(doc, viewIdProp, "NameValue", viewPair.ListViewName);
            XmlHelper.AddElement(doc, viewIdProp, "Value", viewPair.ListViewId);
            props.AppendChild(viewIdProp);

            XmlElement methodProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, methodProp, "Name", "Method");
            XmlHelper.AddElement(doc, methodProp, "DisplayValue", "Accept item");
            XmlHelper.AddElement(doc, methodProp, "NameValue", "AcceptItem");
            XmlHelper.AddElement(doc, methodProp, "Value", "AcceptItem");
            props.AppendChild(methodProp);

            action.AppendChild(props);

            return action;
        }

        private XmlElement CreateClearViewAction(XmlDocument doc, ViewPairInfo viewPair)
        {
            XmlElement action = doc.CreateElement("Action");
            action.SetAttribute("ID", Guid.NewGuid().ToString());
            action.SetAttribute("DefinitionID", Guid.NewGuid().ToString()); // Always new GUID
            action.SetAttribute("Type", "Execute");
            action.SetAttribute("ExecutionType", "Synchronous");
            action.SetAttribute("InstanceID", viewPair.ItemViewInstanceId ?? viewPair.ItemViewId);

            XmlElement props = doc.CreateElement("Properties");

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", "Form");
            props.AppendChild(locationProp);

            XmlElement viewIdProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, viewIdProp, "Name", "ViewID");
            XmlHelper.AddElement(doc, viewIdProp, "DisplayValue", viewPair.ItemViewName);
            XmlHelper.AddElement(doc, viewIdProp, "NameValue", viewPair.ItemViewName);
            XmlHelper.AddElement(doc, viewIdProp, "Value", viewPair.ItemViewId);
            props.AppendChild(viewIdProp);

            XmlElement methodProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, methodProp, "Name", "Method");
            XmlHelper.AddElement(doc, methodProp, "DisplayValue", "Clear");
            XmlHelper.AddElement(doc, methodProp, "NameValue", "Clear");
            XmlHelper.AddElement(doc, methodProp, "Value", "Clear");
            props.AppendChild(methodProp);

            action.AppendChild(props);

            return action;
        }

        private XmlElement GetOrCreateStatesElement(XmlDocument doc)
        {
            XmlNodeList statesList = doc.GetElementsByTagName("States");
            if (statesList.Count > 0)
            {
                return (XmlElement)statesList[0];
            }

            XmlNodeList forms = doc.GetElementsByTagName("Form");
            if (forms.Count > 0)
            {
                XmlElement form = (XmlElement)forms[0];
                XmlElement states = doc.CreateElement("States");
                form.AppendChild(states);
                return states;
            }

            return null;
        }

        private XmlElement GetBaseState(XmlElement statesElement)
        {
            foreach (XmlElement state in statesElement.GetElementsByTagName("State"))
            {
                if (state.GetAttribute("IsBase") == "True")
                {
                    return state;
                }
            }

            XmlElement baseState = statesElement.OwnerDocument.CreateElement("State");
            baseState.SetAttribute("ID", Guid.NewGuid().ToString());
            baseState.SetAttribute("IsBase", "True");
            statesElement.AppendChild(baseState);
            return baseState;
        }

        private XmlElement GetOrCreateEventsElement(XmlElement state)
        {
            XmlNodeList eventsList = state.GetElementsByTagName("Events");
            if (eventsList.Count > 0)
            {
                return (XmlElement)eventsList[0];
            }

            XmlElement events = state.OwnerDocument.CreateElement("Events");
            state.AppendChild(events);
            return events;
        }

        // Keep existing nested classes unchanged
        private class ViewControl
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Type { get; set; }
            public string FieldName { get; set; }
        }

        private void AddAddItemButtonRules(XmlDocument doc, XmlElement eventsElement, string formId, string formName)
        {
            Console.WriteLine("      Creating Add Item button rules for nested sections...");

            // Get all add item buttons from the registry
            var addItemButtons = SmartObjectViewRegistry.GetAllAddItemButtons();

            if (addItemButtons == null || addItemButtons.Count == 0)
            {
                Console.WriteLine("        No add item buttons found in registry");
                return;
            }

            // In multi-form generation, the registry accumulates buttons from ALL forms.
            // Filter to only include buttons whose ViewName belongs to the current form.
            // formName is "{BaseFormName}_{viewName}_Form" (e.g. "Expense_Report_view1_Form")
            // Button ViewNames follow: "{BaseFormName}_{viewX}_SectionName_Item"
            // Extract base form name by removing the trailing "_viewX_Form" suffix.
            string baseFormName = formName;
            int viewSuffix = formName.LastIndexOf("_view", StringComparison.OrdinalIgnoreCase);
            if (viewSuffix > 0)
            {
                baseFormName = formName.Substring(0, viewSuffix);
            }

            int totalButtons = addItemButtons.Count;
            int skippedButtons = 0;
            int createdButtons = 0;

            foreach (var buttonInfo in addItemButtons.Values)
            {
                // Skip buttons that belong to a different form
                if (!string.IsNullOrEmpty(baseFormName) && !string.IsNullOrEmpty(buttonInfo.ViewName) &&
                    !buttonInfo.ViewName.StartsWith(baseFormName + "_", StringComparison.OrdinalIgnoreCase))
                {
                    skippedButtons++;
                    continue;
                }

                Console.WriteLine($"        Creating rule for button: {buttonInfo.ButtonName}");
                CreateAddItemButtonRule(doc, eventsElement, formId, buttonInfo);
                createdButtons++;
            }

            if (skippedButtons > 0)
            {
                Console.WriteLine($"        Skipped {skippedButtons} button(s) belonging to other forms");
            }
            Console.WriteLine($"        ✓ Created {createdButtons} add item button rules for {formName}");
        }

        private void CreateAddItemButtonRule(XmlDocument doc, XmlElement eventsElement, string formId, SmartObjectViewRegistry.AddItemButtonInfo buttonInfo)
        {
            // Convert TABLECTRL338 -> Table_CTRL338
            string normalizedTableName = NormalizeTableName(buttonInfo.TargetTableName);

            // Find the nested Item view instance ID (the view that should be SHOWN)
            string nestedItemViewInstanceId = FindViewInstanceId(doc, normalizedTableName + "_Item");
            if (string.IsNullOrEmpty(nestedItemViewInstanceId))
            {
                Console.WriteLine($"          WARNING: Could not find instance ID for nested Item view: {normalizedTableName}_Item");
                return;
            }

            // Find the nested List view instance ID (the view that should be HIDDEN)
            string nestedListViewInstanceId = FindViewInstanceId(doc, normalizedTableName + "_List");
            if (string.IsNullOrEmpty(nestedListViewInstanceId))
            {
                Console.WriteLine($"          WARNING: Could not find instance ID for nested List view: {normalizedTableName}_List");
                return;
            }

            // Find actual ViewIDs (distinct from AreaItem instance IDs)
            string nestedItemViewId = FindTargetViewId(doc, normalizedTableName);
            string nestedListViewId = FindListViewId(doc, normalizedTableName);

            // Find the source view instance ID (the view containing the button)
            string sourceViewInstanceId = FindViewInstanceId(doc, buttonInfo.ViewName);
            if (string.IsNullOrEmpty(sourceViewInstanceId))
            {
                Console.WriteLine($"          WARNING: Could not find source view instance ID for: {buttonInfo.ViewName}");
                return;
            }

            Console.WriteLine($"        Creating Add Item Button Rule (nested section: {normalizedTableName})");
            Console.WriteLine($"          Button ID: {buttonInfo.ButtonControlId}");
            Console.WriteLine($"          Button Name: {buttonInfo.ButtonName}");
            Console.WriteLine($"          Source View: {buttonInfo.ViewName} (InstanceID: {sourceViewInstanceId})");
            Console.WriteLine($"          Nested Item View: {normalizedTableName}_Item (InstanceID: {nestedItemViewInstanceId}, ViewID: {nestedItemViewId ?? "N/A"})");
            Console.WriteLine($"          Nested List View: {normalizedTableName}_List (InstanceID: {nestedListViewInstanceId}, ViewID: {nestedListViewId ?? "N/A"})");

            // Create the OnClick event
            XmlElement eventElement = doc.CreateElement("Event");
            string eventGuid = Guid.NewGuid().ToString();
            eventElement.SetAttribute("ID", eventGuid);
            eventElement.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            eventElement.SetAttribute("Type", "User");
            eventElement.SetAttribute("SourceID", buttonInfo.ButtonControlId);
            eventElement.SetAttribute("SourceType", "Control");
            eventElement.SetAttribute("SourceName", buttonInfo.ButtonName + " Button");
            eventElement.SetAttribute("SourceDisplayName", buttonInfo.ButtonName + " Button");
            eventElement.SetAttribute("IsExtended", "True");
            eventElement.SetAttribute("InstanceID", sourceViewInstanceId);

            XmlHelper.AddElement(doc, eventElement, "Name", "OnClick");

            XmlElement eventProps = doc.CreateElement("Properties");

            // ViewID property - the view containing the button
            XmlElement viewIdProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, viewIdProp, "Name", "ViewID");
            XmlHelper.AddElement(doc, viewIdProp, "DisplayValue", buttonInfo.ViewName);
            XmlHelper.AddElement(doc, viewIdProp, "NameValue", buttonInfo.ViewName);
            XmlHelper.AddElement(doc, viewIdProp, "Value", buttonInfo.ViewId);
            eventProps.AppendChild(viewIdProp);

            XmlElement ruleProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, ruleProp, "Name", "RuleFriendlyName");
            XmlHelper.AddElement(doc, ruleProp, "Value", $"On {buttonInfo.ViewName}, when {buttonInfo.ButtonName} Button is Clicked");
            eventProps.AppendChild(ruleProp);

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", buttonInfo.ViewName);
            eventProps.AppendChild(locationProp);

            eventElement.AppendChild(eventProps);

            // Add handlers - use Hide List + Show Item pattern (same as top-level Add ToolBar Button)
            XmlElement handlers = doc.CreateElement("Handlers");
            XmlElement handler = CreateAddItemButtonHandler(doc,
                nestedListViewInstanceId, normalizedTableName + "_List", nestedListViewId,
                nestedItemViewInstanceId, normalizedTableName + "_Item", nestedItemViewId);
            handlers.AppendChild(handler);
            eventElement.AppendChild(handlers);

            eventsElement.AppendChild(eventElement);
            Console.WriteLine($"        ✓ Add Item Button rule created with Event ID: {eventGuid}");
        }

        private XmlElement CreateAddItemButtonHandler(XmlDocument doc,
            string listViewInstanceId, string listViewName, string listViewId,
            string itemViewInstanceId, string itemViewName, string itemViewId)
        {
            Console.WriteLine($"          Creating Add Item Button Handler (Hide List + Show Item)");

            XmlElement handler = doc.CreateElement("Handler");
            handler.SetAttribute("ID", Guid.NewGuid().ToString());
            handler.SetAttribute("DefinitionID", Guid.NewGuid().ToString());

            XmlElement props = doc.CreateElement("Properties");
            XmlElement handlerNameProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, handlerNameProp, "Name", "HandlerName");
            XmlHelper.AddElement(doc, handlerNameProp, "Value", "then");
            props.AppendChild(handlerNameProp);

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", "form");
            props.AppendChild(locationProp);

            handler.AppendChild(props);

            XmlElement actions = doc.CreateElement("Actions");

            // ACTION 1: Hide the nested List view (same pattern as top-level Add ToolBar Button)
            Console.WriteLine($"            Adding Hide Nested List View action: {listViewName}");
            XmlElement hideListAction = CreateViewVisibilityAction(doc, listViewInstanceId,
                listViewName, "Hide", listViewId);
            actions.AppendChild(hideListAction);

            // ACTION 2: Show the nested Item view
            Console.WriteLine($"            Adding Show Nested Item View action: {itemViewName}");
            XmlElement showItemAction = CreateViewVisibilityAction(doc, itemViewInstanceId,
                itemViewName, "Show", itemViewId);
            actions.AppendChild(showItemAction);

            handler.AppendChild(actions);
            return handler;
        }

        /// <summary>
        /// Finds the actual ViewID (not AreaItem ID) for a List view matching the pattern.
        /// </summary>
        private string FindListViewId(XmlDocument doc, string targetTableName)
        {
            XmlNodeList items = doc.GetElementsByTagName("Item");
            foreach (XmlElement item in items)
            {
                string viewName = item.SelectSingleNode("Name")?.InnerText ?? "";
                if (viewName.Contains(targetTableName) && viewName.EndsWith("_List"))
                {
                    return item.GetAttribute("ViewID");
                }
            }
            return null;
        }

        private string FindViewInstanceId(XmlDocument doc, string viewNamePattern)
        {
            XmlNodeList items = doc.GetElementsByTagName("Item");

            // First try exact match
            foreach (XmlElement item in items)
            {
                string viewName = item.SelectSingleNode("Name")?.InnerText ?? "";
                if (string.Equals(viewName, viewNamePattern, StringComparison.OrdinalIgnoreCase))
                {
                    return item.GetAttribute("ID");
                }
            }

            // Fallback: partial match - works for both _Item and _List views
            // View names may have a prefix like "Expense_Report_view1_" that isn't in the pattern
            foreach (XmlElement item in items)
            {
                string viewName = item.SelectSingleNode("Name")?.InnerText ?? "";
                if (viewName.Contains(viewNamePattern))
                {
                    return item.GetAttribute("ID");
                }
            }
            return null;
        }

        private string FindTargetViewId(XmlDocument doc, string targetTableName)
        {
            XmlNodeList items = doc.GetElementsByTagName("Item");
            foreach (XmlElement item in items)
            {
                string viewName = item.SelectSingleNode("Name")?.InnerText ?? "";
                if (viewName.Contains(targetTableName) && viewName.EndsWith("_Item"))
                {
                    return item.GetAttribute("ViewID");
                }
            }
            return null;
        }

        private string NormalizeTableName(string tableName)
        {
            // Convert TABLECTRL338 -> Table_CTRL338
            if (tableName.StartsWith("TABLE"))
            {
                return tableName.Replace("TABLE", "Table_");
            }

            // If it's just CTRL338, add Table_ prefix
            if (tableName.StartsWith("CTRL"))
            {
                return "Table_" + tableName;
            }

            return tableName;
        }



        /// <summary>
        /// Finds a control in the form XML by field name (like AMOUNT)
        /// </summary>
        private ControlInfoInForm FindControlInFormXml(XmlDocument doc, string fieldName)
        {
            try
            {
                Console.WriteLine($"          Searching for field '{fieldName}' in form XML...");

                // The field controls are actually inside embedded views, not in the form's Controls section
                // We need to search within the view contents that are embedded in the form

                // Search for Parameter elements that target our field name
                // The controls are embedded in Parameter elements, not simple Control elements
                XmlNodeList parameterElements = doc.SelectNodes("//Parameter[@TargetID]");
                var matchingControls = new List<XmlElement>();
                var viewInstanceMappings = new Dictionary<string, string>(); // controlId -> viewInstanceId

                Console.WriteLine($"          Examining {parameterElements.Count} parameter elements for '{fieldName}'...");
                var foundControlIds = new HashSet<string>(); // Track unique control IDs

                foreach (XmlElement parameter in parameterElements)
                {
                    // Check if the TargetID or TargetName matches our field
                    string targetId = parameter.GetAttribute("TargetID");
                    string targetName = parameter.GetAttribute("TargetName");

                    if (targetId.Equals(fieldName, StringComparison.OrdinalIgnoreCase) ||
                        targetName.Equals(fieldName, StringComparison.OrdinalIgnoreCase))
                    {
                        // Extract the source control information
                        string sourceId = parameter.GetAttribute("SourceID");
                        string sourceInstanceId = parameter.GetAttribute("SourceInstanceID");
                        string sourceName = parameter.GetAttribute("SourceName");

                        if (!string.IsNullOrEmpty(sourceId) && !string.IsNullOrEmpty(sourceInstanceId) &&
                            !foundControlIds.Contains(sourceId)) // Avoid duplicates
                        {
                            Console.WriteLine($"            Found parameter: TargetID='{targetId}', SourceID='{sourceId}', SourceInstanceID='{sourceInstanceId}', SourceName='{sourceName}'");

                            // Create a pseudo control element with the information we need
                            var pseudoControl = doc.CreateElement("Control");
                            pseudoControl.SetAttribute("ID", sourceId);

                            matchingControls.Add(pseudoControl);
                            viewInstanceMappings[sourceId] = sourceInstanceId;
                            foundControlIds.Add(sourceId);

                            Console.WriteLine($"            Mapped control ID: {sourceId} -> view instance: {sourceInstanceId}");
                            // Don't break - we want to find ALL matching controls for this field
                        }
                    }
                }

                Console.WriteLine($"          Found {matchingControls.Count} matching controls via parameters");

                if (matchingControls.Count == 0)
                {
                    Console.WriteLine($"          No controls found for field '{fieldName}'");
                    return null;
                }

                // Use the first matching control
                var firstMatch = matchingControls[0];
                string controlId = firstMatch.GetAttribute("ID");
                string viewInstanceId = viewInstanceMappings.ContainsKey(controlId) ? viewInstanceMappings[controlId] : controlId;

                // Get control name
                string controlName = fieldName; // Default to field name
                XmlNodeList nameNodes = firstMatch.GetElementsByTagName("Name");
                if (nameNodes.Count > 0)
                {
                    controlName = nameNodes[0].InnerText;
                }

                Console.WriteLine($"          Selected control '{controlName}' (ID: {controlId}) with view instance: {viewInstanceId}");

                return new ControlInfoInForm
                {
                    ControlId = controlId,
                    ViewInstanceId = viewInstanceId,
                    ControlName = controlName
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"          Error finding control for field '{fieldName}': {ex.Message}");
                return null;
            }
        }

        private XmlElement FindParentViewInstance(XmlElement controlElement)
        {
            // Walk up the XML tree to find the parent Item element that represents a view instance
            XmlNode current = controlElement.ParentNode;
            while (current != null)
            {
                if (current is XmlElement element && element.Name == "Item" && element.HasAttribute("ViewID"))
                {
                    return element;
                }
                current = current.ParentNode;
            }
            return null;
        }

        /// <summary>
        /// Finds ALL controls in the form XML that match the given field name
        /// </summary>
        private List<ControlInfoInForm> FindAllControlsInFormXml(XmlDocument doc, string fieldName)
        {
            var result = new List<ControlInfoInForm>();

            try
            {
                Console.WriteLine($"          Searching for ALL instances of field '{fieldName}' in form XML...");

                // Search for Parameter elements that target our field name
                XmlNodeList parameterElements = doc.SelectNodes("//Parameter[@TargetID]");
                var foundControlIds = new HashSet<string>(); // Track unique control IDs

                Console.WriteLine($"          Examining {parameterElements.Count} parameter elements for '{fieldName}'...");

                foreach (XmlElement parameter in parameterElements)
                {
                    // Check if the TargetID or TargetName matches our field
                    string targetId = parameter.GetAttribute("TargetID");
                    string targetName = parameter.GetAttribute("TargetName");

                    if (targetId.Equals(fieldName, StringComparison.OrdinalIgnoreCase) ||
                        targetName.Equals(fieldName, StringComparison.OrdinalIgnoreCase))
                    {
                        // Extract the source control information
                        string sourceId = parameter.GetAttribute("SourceID");
                        string sourceInstanceId = parameter.GetAttribute("SourceInstanceID");
                        string sourceName = parameter.GetAttribute("SourceName");

                        if (!string.IsNullOrEmpty(sourceId) && !string.IsNullOrEmpty(sourceInstanceId) &&
                            !foundControlIds.Contains(sourceId)) // Avoid duplicates
                        {
                            // For AMOUNT fields, exclude the entertainment section controls
                            // We only want the main itemized expense controls, not the entertainment detail controls
                            bool shouldInclude = true;
                            if (fieldName.Equals("AMOUNT", StringComparison.OrdinalIgnoreCase))
                            {
                                // Exclude "Amount Text Box" controls (from entertainment section)
                                // Include only "Itemized Expense Cost Text Box" controls (from main table)
                                if (sourceName.Contains("Amount Text Box") && !sourceName.Contains("Itemized Expense"))
                                {
                                    shouldInclude = false;
                                    Console.WriteLine($"            Excluding entertainment section control: '{sourceName}' (ID: {sourceId})");
                                }
                            }

                            if (shouldInclude)
                            {
                                Console.WriteLine($"            Found parameter: TargetID='{targetId}', SourceID='{sourceId}', SourceInstanceID='{sourceInstanceId}', SourceName='{sourceName}'");

                                result.Add(new ControlInfoInForm
                                {
                                    ControlId = sourceId,
                                    ViewInstanceId = sourceInstanceId,
                                    ControlName = fieldName
                                });

                                foundControlIds.Add(sourceId);
                                Console.WriteLine($"            Mapped control ID: {sourceId} -> view instance: {sourceInstanceId}");
                            }
                        }
                    }
                }

                Console.WriteLine($"          Found {result.Count} matching controls via parameters");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"          Error finding controls for field '{fieldName}': {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Finds a control by searching Properties/Property[Name='ControlName'] values in the form XML.
        /// K2 controls don't have a Name attribute — the name is in Properties.
        /// Returns control ID, view instance ID, and view name needed for expression Items.
        /// </summary>
        private ControlInfoInForm FindControlByName(XmlDocument doc, string fieldName)
        {
            try
            {
                string upperFieldName = fieldName.ToUpperInvariant();

                // Strategy 1: Use ControlMappingService (has accurate field→control mappings)
                // Build a flat lookup to avoid repeated GetViewControls calls
                var registeredViews = ControlMappingService.GetMappedViewNames();
                foreach (var viewName in registeredViews)
                {
                    Dictionary<string, ViewGenerator.ControlMapping> controls;
                    try
                    {
                        controls = ControlMappingService.GetViewControls(viewName);
                    }
                    catch { continue; }
                    if (controls == null) continue;

                    // Look for field name match (case-insensitive)
                    ViewGenerator.ControlMapping match = null;
                    foreach (var kvp in controls)
                    {
                        if (string.Equals(kvp.Key, fieldName, StringComparison.OrdinalIgnoreCase) &&
                            kvp.Value.ControlType != "Label")
                        {
                            match = kvp.Value;
                            break;
                        }
                    }

                    if (match != null)
                    {
                        string controlId = match.ControlId;
                        string controlDisplayName = match.ControlName ?? fieldName;

                        // Find the view instance ID from the form XML
                        string viewInstanceId = FindViewInstanceId(doc, viewName) ?? "";

                        Console.WriteLine($"          Found control '{fieldName}' via ControlMappingService: " +
                            $"Control='{controlDisplayName}', ID={controlId}, View='{viewName}', ViewInstance={viewInstanceId}");
                        return new ControlInfoInForm
                        {
                            ControlId = controlId,
                            ViewInstanceId = viewInstanceId,
                            ControlName = controlDisplayName,
                            ViewName = viewName
                        };
                    }
                }

                Console.WriteLine($"          Could not find control '{fieldName}' in ControlMappingService");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"          Error in FindControlByName for '{fieldName}': {ex.Message}");
            }
            return null;
        }

        private string FindBestViewInstanceForField(XmlDocument doc, string fieldName)
        {
            try
            {
                // For AMOUNT fields, we want to find view instances that represent repeating sections
                // These are typically Item views that contain data entry controls
                XmlNodeList items = doc.SelectNodes("//Item[@ViewID]");

                // Look for items that likely contain AMOUNT controls (repeating sections with data entry)
                foreach (XmlElement item in items)
                {
                    string itemId = item.GetAttribute("ID");
                    string itemName = "";

                    // Get the item name
                    XmlNodeList nameNodes = item.GetElementsByTagName("Name");
                    if (nameNodes.Count > 0)
                    {
                        itemName = nameNodes[0].InnerText;
                    }

                    // Look for items that are likely to contain AMOUNT fields
                    // These would be in Table or Item views (not List views for expressions)
                    if (itemName.IndexOf("Table", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        itemName.IndexOf("Item", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Console.WriteLine($"          Found candidate view instance for {fieldName}: {itemName} (ID: {itemId})");
                        return itemId;
                    }
                }

                // Fallback: use any item view that's not a List
                foreach (XmlElement item in items)
                {
                    string itemId = item.GetAttribute("ID");
                    string itemName = "";

                    XmlNodeList nameNodes = item.GetElementsByTagName("Name");
                    if (nameNodes.Count > 0)
                    {
                        itemName = nameNodes[0].InnerText;
                    }

                    if (itemName.IndexOf("List", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        Console.WriteLine($"          Using fallback view instance: {itemName} (ID: {itemId})");
                        return itemId;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"          Error finding best view instance: {ex.Message}");
                return null;
            }
        }

    }


    public class FormDefinition
    {
        public string InfoPathViewName { get; set; }
        public string FormName { get; set; }
        public List<string> ViewNames { get; set; }
        public string CategoryPath { get; set; }
        public bool IsListForm { get; set; }
        public bool HasRepeatingSections { get; set; }

        public FormDefinition()
        {
            ViewNames = new List<string>();
        }
    }

    public class ViewPairInfo
    {
        public string ListViewName { get; set; }
        public string ItemViewName { get; set; }
        public string ListViewId { get; set; }
        public string ItemViewId { get; set; }
        public string ListViewInstanceId { get; set; }
        public string ItemViewInstanceId { get; set; }
        public string AreaGuid { get; set; }
        public string SectionName { get; set; }
        public string FormName { get; set; }
        public string FormId { get; set; }

        public bool IsTopLevel { get; set; }
        public List<FieldMapping> FieldMappings { get; set; } = new List<FieldMapping>();

        // Enhanced nesting support
        public int NestingLevel { get; set; } = 0;  // 0 = main form, 1 = first level child, 2 = nested child, etc.
        public string ParentSectionName { get; set; }  // Direct parent section name
        public string ParentViewPairKey { get; set; }  // Key to find parent ViewPairInfo in dictionary
        public List<string> ChildSectionNames { get; set; } = new List<string>();  // Direct children
        public bool IsNestedSection { get; set; } = false;  // True if this is a nested section within another repeating section
    }

    public class FieldMapping
    {
        public string ItemControlId { get; set; }
        public string ItemControlName { get; set; }
        public string ListControlId { get; set; }
        public string ListControlName { get; set; }
        public string FieldName { get; set; }
    }

    public static class ButtonTracker
    {
        private static Dictionary<string, ButtonInfo> _viewButtons = new Dictionary<string, ButtonInfo>();

        public class ButtonInfo
        {
            public string AddButtonId { get; set; }
            public string AddButtonName { get; set; }
            public string DeleteButtonId { get; set; }
            public string DeleteButtonName { get; set; }
            public string CancelButtonId { get; set; }
            public string CancelButtonName { get; set; }
        }

        public static void RegisterViewButtons(string viewName, ButtonInfo buttons)
        {
            _viewButtons[viewName] = buttons;
            Console.WriteLine($"    ButtonTracker: Registered buttons for view {viewName}");
            if (!string.IsNullOrEmpty(buttons.AddButtonId))
                Console.WriteLine($"      Add Button ID: {buttons.AddButtonId}");
            if (!string.IsNullOrEmpty(buttons.DeleteButtonId))
                Console.WriteLine($"      Delete Button ID: {buttons.DeleteButtonId}");
            if (!string.IsNullOrEmpty(buttons.CancelButtonId))
                Console.WriteLine($"      Cancel Button ID: {buttons.CancelButtonId}");
        }

        public static ButtonInfo GetViewButtons(string viewName)
        {
            if (_viewButtons.ContainsKey(viewName))
                return _viewButtons[viewName];

            foreach (var kvp in _viewButtons)
            {
                if (kvp.Key.Contains(viewName) || viewName.Contains(kvp.Key))
                    return kvp.Value;
            }

            return null;
        }

        public static void Clear()
        {
            _viewButtons.Clear();
            Console.WriteLine("    ButtonTracker: Cleared all button mappings");
        }

        public static Dictionary<string, ButtonInfo> GetAllButtons()
        {
            return new Dictionary<string, ButtonInfo>(_viewButtons);
        }
    }
}