using K2SmartObjectGenerator.Models;
using K2SmartObjectGenerator.Utilities;
using K2SmartObjectGenerator.Config;
using Newtonsoft.Json.Linq;
using SourceCode.Forms.Management;
using SourceCode.Forms.Utilities;
using SourceCode.SmartObjects.Authoring;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;

namespace K2SmartObjectGenerator
{
    public class ViewGenerator
    {
        private readonly ServerConnectionManager _connectionManager;
        private readonly ViewXmlBuilder _xmlBuilder;
        private readonly ViewRulesBuilder _rulesBuilder;
        private readonly Dictionary<string, Dictionary<string, FieldInfo>> _smoFieldMappings;
        private readonly SmartObjectGenerator _smoGenerator;
        private readonly GeneratorConfiguration _config;
        public Dictionary<string, Dictionary<string, ControlMapping>> ViewControlMappings { get; private set; }


        public Dictionary<string, string> ViewTitles { get; private set; }

        public ViewGenerator(ServerConnectionManager connectionManager,
                           Dictionary<string, Dictionary<string, FieldInfo>> smoFieldMappings,
                           SmartObjectGenerator smoGenerator,
                           GeneratorConfiguration config = null)
        {
            _connectionManager = connectionManager;
            _smoFieldMappings = smoFieldMappings;
            _smoGenerator = smoGenerator;
            _config = config ?? GeneratorConfiguration.CreateDefault();
            _xmlBuilder = new ViewXmlBuilder(connectionManager, smoFieldMappings, smoGenerator, _config);
            _rulesBuilder = new ViewRulesBuilder();
            ViewTitles = new Dictionary<string, string>();
            ViewControlMappings = new Dictionary<string, Dictionary<string, ControlMapping>>();
        }

        private string DetermineSegmentPosition(ViewSegment segment, List<ViewSegment> allSegments)
        {
            int segmentIndex = allSegments.IndexOf(segment);

            if (segmentIndex == 0)
                return "Beginning of form";
            else if (segmentIndex == allSegments.Count - 1)
                return "End of form";
            else
            {
                // Check what comes before and after
                var before = segmentIndex > 0 ? allSegments[segmentIndex - 1] : null;
                var after = segmentIndex < allSegments.Count - 1 ? allSegments[segmentIndex + 1] : null;

                if (before?.Type == SegmentType.RepeatingSection && after?.Type == SegmentType.RepeatingSection)
                    return $"Between repeating sections '{before.SectionName}' and '{after.SectionName}'";
                else if (before?.Type == SegmentType.RepeatingSection)
                    return $"After repeating section '{before.SectionName}'";
                else if (after?.Type == SegmentType.RepeatingSection)
                    return $"Before repeating section '{after.SectionName}'";
                else
                    return "Middle of form";
            }
        }

        public void GenerateViewsFromJson(string jsonContent)
        {
            JObject formData = JObject.Parse(jsonContent);
            string formDisplayName = formData.Properties().First().Name; // Keep original with spaces
            string formName = NameSanitizer.SanitizeSmartObjectName(formDisplayName); // Sanitize name to match SmartObject naming

            JObject formDefinition = formData[formDisplayName] as JObject;
            JObject formDefObject = formDefinition["FormDefinition"] as JObject;
            JArray viewsArray = formDefObject?["Views"] as JArray;

            if (viewsArray == null)
            {
                Console.WriteLine("No views found in JSON");
                return;
            }

            JArray dataArray = formDefObject?["Data"] as JArray;

            // Extract DynamicSections and ConditionalVisibility from FormDefinition level (form-wide rules)
            JArray dynamicSections = formDefObject?["DynamicSections"] as JArray ?? new JArray();
            JObject conditionalVisibility = formDefObject?["ConditionalVisibility"] as JObject ?? new JObject();

            if (dynamicSections.Count > 0)
            {
                Console.WriteLine($"  Found {dynamicSections.Count} dynamic section(s) with visibility rules at form level");
            }
            if (conditionalVisibility.Properties().Any())
            {
                Console.WriteLine($"  Found conditional visibility rules for {conditionalVisibility.Properties().Count()} field(s) at form level");
            }

            // Define base category paths - respect TargetFolder from configuration
            // STRUCTURE: {TargetFolder}\{formName}\Views (using sanitized name to match SmartObjects)
            string targetFolder = _config.Form.TargetFolder ?? "Generated";
            string baseCategory = $"{targetFolder}\\{formName}";

            Console.WriteLine($"  View base category: {baseCategory}");

            // Process each InfoPath view independently
            foreach (JObject viewDef in viewsArray)
            {
                string viewName = viewDef["ViewName"]?.Value<string>()?.Replace(".xsl", "");
                if (string.IsNullOrEmpty(viewName))
                {
                    Console.WriteLine("    WARNING: Found view with empty name, skipping...");
                    continue;
                }

                Console.WriteLine($"\n=== Processing InfoPath View: {viewName} ===");

                // Create a subfolder for views under the form
                string viewCategory = $"{baseCategory}\\Views";

                // Get view-specific controls
                JArray viewControls = viewDef["Controls"] as JArray;
                if (viewControls == null || viewControls.Count == 0)
                {
                    Console.WriteLine($"    No controls found for view: {viewName}");
                    continue;
                }

                // Use form-wide DynamicSections and ConditionalVisibility (already extracted above)

                if (dynamicSections.Count > 0)
                {
                    Console.WriteLine($"    Found {dynamicSections.Count} dynamic section(s) with visibility rules");
                }
                if (conditionalVisibility.Properties().Any())
                {
                    Console.WriteLine($"    Found conditional visibility rules for {conditionalVisibility.Properties().Count()} field(s)");
                }

                // Process nested tables and auto-generate "Add Item" buttons BEFORE segmentation
                Console.WriteLine("    Checking for nested RepeatingTables to auto-generate buttons...");
                var processedControls = NestedTableHandler.ProcessControlsWithNestedTableButtons(viewControls);
                var processedControlsArray = new JArray(processedControls);

                // Log nested table information
                var nestedTableInfo = NestedTableHandler.GetNestedTableInfo(viewControls);
                if (nestedTableInfo.Count > 0)
                {
                    Console.WriteLine($"    Found {nestedTableInfo.Count} nested table(s) with auto-generated buttons:");
                    foreach (var table in nestedTableInfo.Values)
                    {
                        Console.WriteLine($"      - {table.TableName} (Parent: {table.ParentSectionName}, Button: {table.ButtonName})");
                    }
                }

                // Split controls into segments based on repeating sections (using processed controls with buttons)
                var viewSegments = SplitControlsByRepeatingSections(processedControlsArray);

                // Generate views for each segment
                int regularSegmentIndex = 0;
                string firstLabelName = null;
                Dictionary<string, int> repeatingSectionCounts = new Dictionary<string, int>();

                foreach (var segment in viewSegments)
                {
                    if (segment.Type == SegmentType.Regular)
                    {
                        // Track which regular segment this is
                        regularSegmentIndex++;

                        // Find the first label to use as the view name if possible (only for first segment)
                        if (regularSegmentIndex == 1 && string.IsNullOrEmpty(firstLabelName) && segment.Controls.Count > 0)
                        {
                            var firstLabel = segment.Controls.FirstOrDefault(c =>
                                c["Type"]?.Value<string>()?.ToLower() == "label" &&
                                !string.IsNullOrEmpty(c["Label"]?.Value<string>()));

                            if (firstLabel != null)
                            {
                                firstLabelName = firstLabel["Label"].Value<string>()
                                    .Replace(" ", "_")
                                    .Replace(":", "")
                                    .Replace("-", "_");
                            }
                        }

                        // Generate view name based on position
                        string segmentViewName;
                        if (regularSegmentIndex == 1 && !string.IsNullOrEmpty(firstLabelName))
                        {
                            // First segment uses the first label name
                            segmentViewName = $"{formName}_{firstLabelName}";
                        }
                        else
                        {
                            // Subsequent segments use Part naming
                            segmentViewName = $"{formName}_{viewName}_Part{regularSegmentIndex}";
                        }

                        Console.WriteLine($"\nGenerating Regular View (Segment {regularSegmentIndex}): {segmentViewName}");
                        Console.WriteLine($"    Category: {viewCategory}");
                        Console.WriteLine($"    Contains {segment.Controls.Count} controls");

                        // Check the position of this segment relative to repeating sections
                        string positionInfo = DetermineSegmentPosition(segment, viewSegments);
                        Console.WriteLine($"    Position: {positionInfo}");

                        JArray segmentControlsArray = new JArray(segment.Controls);

                        // Generate the view
                        GenerateXmlBasedView(segmentViewName, formName, segmentControlsArray, dataArray,
                            viewCategory, dynamicSections, conditionalVisibility, false, viewName, regularSegmentIndex);
                    }
                    else if (segment.Type == SegmentType.RepeatingSection)
                    {
                        string sectionName = segment.SectionName;

                        // Track how many times we've seen this repeating section (for duplicate handling)
                        if (!repeatingSectionCounts.ContainsKey(sectionName))
                            repeatingSectionCounts[sectionName] = 0;
                        repeatingSectionCounts[sectionName]++;

                        // Construct the SmartObject name consistently
                        string childSmoName = ConstructSmartObjectName(formName, sectionName);

                        Console.WriteLine($"\n=== Processing Repeating Section: {sectionName} ===");
                        Console.WriteLine($"    InfoPath View: {viewName}");
                        Console.WriteLine($"    SmartObject: {childSmoName}");
                        Console.WriteLine($"    Controls in section: {segment.Controls.Count}");

                        // Verify the SmartObject exists - CHECK REGISTRY FIRST
                        bool smoExists = SmartObjectViewRegistry.SmartObjectExists(childSmoName);

                        if (!smoExists)
                        {
                            smoExists = _smoGenerator.CheckSmartObjectExists(childSmoName);
                        }

                        if (!smoExists)
                        {
                            Console.WriteLine($"    ERROR: SmartObject {childSmoName} not found, skipping view generation");
                            continue;
                        }

                        // IMPORTANT: Create view names that are unique per InfoPath view context
                        // This allows multiple view pairs for the same SmartObject
                        // Sanitize section name to match FormGenerator expectations and SmartObject naming
                        string normalizedSectionName = NameSanitizer.SanitizeSmartObjectName(sectionName);
                        string itemViewName = $"{formName}_{viewName}_{normalizedSectionName}_Item";
                        string listViewName = $"{formName}_{viewName}_{normalizedSectionName}_List";

                        // Add suffix if this is a duplicate section name
                        if (repeatingSectionCounts[sectionName] > 1)
                        {
                            itemViewName += $"_{repeatingSectionCounts[sectionName]}";
                            listViewName += $"_{repeatingSectionCounts[sectionName]}";
                        }

                        Console.WriteLine($"\nGenerating Repeating Section Views:");
                        Console.WriteLine($"    Item View: {itemViewName}");
                        Console.WriteLine($"    List View: {listViewName}");
                        Console.WriteLine($"    Category: {viewCategory}");

                        JArray sectionControlsArray = new JArray(segment.Controls);

                        // Generate item view
                        GenerateXmlBasedView(itemViewName, childSmoName, sectionControlsArray, dataArray,
                            viewCategory, dynamicSections, conditionalVisibility, true, viewName, 0, sectionName);

                        // Extract visible fields from controls using the improved method
                        List<string> visibleFields = ExtractVisibleFieldsFromControls(segment.Controls, dataArray);

                        // IMPORTANT: Ensure we have ALL fields from the SmartObject
                        visibleFields = GetFieldsForListView(childSmoName, visibleFields);

                        // IMPORTANT: Remove ParentID and ID from visible fields in list views
                        visibleFields = visibleFields.Where(f =>
                            !f.Equals("PARENTID", StringComparison.OrdinalIgnoreCase) &&
                            !f.Equals("PARENT_ID", StringComparison.OrdinalIgnoreCase) &&
                            !f.Equals("ID", StringComparison.OrdinalIgnoreCase)).ToList();

                        Console.WriteLine($"    Extracted {visibleFields.Count} visible fields (excluding ID/ParentID): {string.Join(", ", visibleFields)}");

                        // Validate field count
                        if (_smoFieldMappings.ContainsKey(childSmoName))
                        {
                            var smoFields = _smoFieldMappings[childSmoName];
                            int totalFieldCount = smoFields.Count(f =>
                                !f.Key.Equals("ID", StringComparison.OrdinalIgnoreCase) &&
                                !f.Key.Equals("PARENTID", StringComparison.OrdinalIgnoreCase));

                            if (visibleFields.Count < totalFieldCount)
                            {
                                Console.WriteLine($"    WARNING: List view has {visibleFields.Count} fields but SmartObject has {totalFieldCount} non-system fields");
                                Console.WriteLine($"    Attempting to add missing fields...");

                                // Log which fields are missing
                                foreach (var field in smoFields)
                                {
                                    if (!field.Key.Equals("ID", StringComparison.OrdinalIgnoreCase) &&
                                        !field.Key.Equals("PARENTID", StringComparison.OrdinalIgnoreCase))
                                    {
                                        bool found = visibleFields.Any(p => p.Equals(field.Key, StringComparison.OrdinalIgnoreCase));
                                        if (!found)
                                        {
                                            Console.WriteLine($"      Adding missing field: {field.Key}");
                                            visibleFields.Add(field.Key);
                                        }
                                    }
                                }

                                Console.WriteLine($"    Updated field count: {visibleFields.Count}");
                            }
                            else if (visibleFields.Count == totalFieldCount)
                            {
                                Console.WriteLine($"    ✓ Field count matches: List view and SmartObject both have {visibleFields.Count} non-system fields");
                            }
                        }

                        // Generate list view
                        GenerateListViewUsingAPI(listViewName, childSmoName, viewCategory, visibleFields,
                            viewName, sectionName);

                        // Register repeating section views with a unique key per InfoPath view
                        string registryKey = $"{viewName}_{sectionName}";
                        if (repeatingSectionCounts[sectionName] > 1)
                        {
                            registryKey += $"_{repeatingSectionCounts[sectionName]}";
                        }

                        SmartObjectViewRegistry.RegisterRepeatingSectionViews(
                            formName,
                            registryKey,
                            itemViewName,
                            listViewName,
                            childSmoName
                        );

                        Console.WriteLine($"    Successfully created view pair for {sectionName} in InfoPath view {viewName}");
                    }
                }
            }

            Console.WriteLine($"\n=== View Generation Complete ===");
        }


        // Helper method to construct SmartObject name consistently
        private string ConstructSmartObjectName(string formName, string sectionName)
        {
            // Sanitize section name to match SmartObjectGenerator naming
            string sanitizedSectionName = NameSanitizer.SanitizeSmartObjectName(sectionName);

            if (sanitizedSectionName.StartsWith("TABLECTRL", StringComparison.OrdinalIgnoreCase))
            {
                // Convert TABLECTRL338 to Table_CTRL338
                string ctrlNumber = sanitizedSectionName.Substring("TABLECTRL".Length);
                return $"{formName}_Table_CTRL{ctrlNumber}";
            }
            else if (sanitizedSectionName.StartsWith("Table_CTRL", StringComparison.OrdinalIgnoreCase))
            {
                return $"{formName}_{sanitizedSectionName}";
            }
            else
            {
                return $"{formName}_{sanitizedSectionName}";
            }
        }


        // New helper method to extract visible fields from controls
        private List<string> ExtractVisibleFieldsFromControls(List<JObject> controls, JArray dataArray)
        {
            List<string> visibleFields = new List<string>();
            HashSet<string> processedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Console.WriteLine($"        Extracting visible fields from {controls.Count} controls");

            // Strategy: Extract ALL data-bound controls from the repeating section
            // This ensures 1:1 parity between item view and list view fields

            foreach (JObject control in controls)
            {
                string controlType = control["Type"]?.Value<string>();
                string name = control["Name"]?.Value<string>();

                // Skip if no name
                if (string.IsNullOrEmpty(name))
                {
                    Console.WriteLine($"          Skipping control with no name (type: {controlType})");
                    continue;
                }

                // Check if this is a data control (not just a label or structural element)
                if (IsDataControl(controlType))
                {
                    string fieldName = NameSanitizer.SanitizePropertyName(name);

                    // Skip system fields
                    if (fieldName.Equals("ID", StringComparison.OrdinalIgnoreCase) ||
                        fieldName.Equals("PARENTID", StringComparison.OrdinalIgnoreCase) ||
                        fieldName.Equals("PARENT_ID", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"          Skipping system field: {fieldName}");
                        continue;
                    }

                    // Add field if not already processed
                    if (!processedNames.Contains(fieldName))
                    {
                        visibleFields.Add(fieldName);
                        processedNames.Add(fieldName);
                        Console.WriteLine($"        Found data field: {fieldName} from control type: {controlType}");
                    }
                }
                else if (controlType?.ToLower() == "label")
                {
                    // For labels, check if there's a corresponding data control with the same name
                    // This handles cases where labels define the field structure
                    bool hasMatchingDataControl = false;

                    foreach (JObject checkControl in controls)
                    {
                        string checkType = checkControl["Type"]?.Value<string>();
                        string checkName = checkControl["Name"]?.Value<string>();

                        if (IsDataControl(checkType) &&
                            !string.IsNullOrEmpty(checkName) &&
                            checkName.Equals(name, StringComparison.OrdinalIgnoreCase))
                        {
                            hasMatchingDataControl = true;
                            break;
                        }
                    }

                    // If this label doesn't have a matching data control, it might be a header label
                    // for a field in a repeating table
                    if (!hasMatchingDataControl)
                    {
                        // Check if this label is in a header row (typically row 1 of the repeating section)
                        JObject repeatingSectionInfo = control["RepeatingSectionInfo"] as JObject;
                        if (repeatingSectionInfo != null &&
                            repeatingSectionInfo["IsInRepeatingSection"]?.Value<bool>() == true)
                        {
                            string gridPos = control["GridPosition"]?.Value<string>();
                            if (!string.IsNullOrEmpty(gridPos))
                            {
                                int row = ExtractRowNumber(gridPos);

                                // Check if there's a data control in the same column but different row
                                int col = ExtractColumnNumber(gridPos);

                                foreach (JObject dataControl in controls)
                                {
                                    if (!IsDataControl(dataControl["Type"]?.Value<string>()))
                                        continue;

                                    string dataGridPos = dataControl["GridPosition"]?.Value<string>();
                                    if (!string.IsNullOrEmpty(dataGridPos))
                                    {
                                        int dataRow = ExtractRowNumber(dataGridPos);
                                        int dataCol = ExtractColumnNumber(dataGridPos);

                                        // If there's a data control in the same column
                                        if (dataCol == col && dataRow != row)
                                        {
                                            // Use the label name as the field name
                                            string fieldName = NameSanitizer.SanitizePropertyName(name);

                                            if (!fieldName.Equals("ID", StringComparison.OrdinalIgnoreCase) &&
                                                !fieldName.Equals("PARENTID", StringComparison.OrdinalIgnoreCase) &&
                                                !processedNames.Contains(fieldName))
                                            {
                                                visibleFields.Add(fieldName);
                                                processedNames.Add(fieldName);
                                                Console.WriteLine($"        Found field from header label: {fieldName}");
                                            }
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Also check the data array for any fields we might have missed
            // This ensures we catch all fields defined in the data model
            if (dataArray != null)
            {
                foreach (JObject dataItem in dataArray)
                {
                    string columnName = dataItem["ColumnName"]?.Value<string>();
                    bool isRepeating = dataItem["IsRepeating"]?.Value<bool>() ?? false;
                    string repeatingSectionName = dataItem["RepeatingSectionName"]?.Value<string>();

                    // Check if this data item belongs to the current repeating section
                    if (isRepeating && !string.IsNullOrEmpty(columnName))
                    {
                        // Check if any of our controls reference this column
                        bool isRelevantToThisSection = false;

                        foreach (JObject control in controls)
                        {
                            string controlName = control["Name"]?.Value<string>();
                            JObject repInfo = control["RepeatingSectionInfo"] as JObject;

                            if (repInfo != null &&
                                !string.IsNullOrEmpty(controlName) &&
                                controlName.Equals(columnName, StringComparison.OrdinalIgnoreCase))
                            {
                                isRelevantToThisSection = true;
                                break;
                            }
                        }

                        if (isRelevantToThisSection)
                        {
                            string fieldName = NameSanitizer.SanitizePropertyName(columnName);

                            if (!fieldName.Equals("ID", StringComparison.OrdinalIgnoreCase) &&
                                !fieldName.Equals("PARENTID", StringComparison.OrdinalIgnoreCase) &&
                                !processedNames.Contains(fieldName))
                            {
                                visibleFields.Add(fieldName);
                                processedNames.Add(fieldName);
                                Console.WriteLine($"        Found field from data array: {fieldName}");
                            }
                        }
                    }
                }
            }

            Console.WriteLine($"        Total visible fields extracted: {visibleFields.Count}");
            Console.WriteLine($"        Fields: {string.Join(", ", visibleFields)}");

            return visibleFields;
        }

        private List<string> GetFieldsForListView(string childSmoName, List<string> extractedFields)
        {
            List<string> finalFields = new List<string>();
            HashSet<string> addedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // First, add all extracted fields
            foreach (string field in extractedFields)
            {
                string sanitized = NameSanitizer.SanitizePropertyName(field);
                if (!sanitized.Equals("ID", StringComparison.OrdinalIgnoreCase) &&
                    !sanitized.Equals("PARENTID", StringComparison.OrdinalIgnoreCase) &&
                    !addedFields.Contains(sanitized))
                {
                    finalFields.Add(sanitized);
                    addedFields.Add(sanitized);
                }
            }

            // If we have field mappings for this SmartObject, ensure we include ALL fields
            // (except system fields)
            if (_smoFieldMappings.ContainsKey(childSmoName))
            {
                var smoFields = _smoFieldMappings[childSmoName];

                foreach (var field in smoFields)
                {
                    string fieldName = field.Key;

                    // Skip system fields
                    if (fieldName.Equals("ID", StringComparison.OrdinalIgnoreCase) ||
                        fieldName.Equals("PARENTID", StringComparison.OrdinalIgnoreCase) ||
                        fieldName.Equals("PARENT_ID", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Add if not already in the list
                    if (!addedFields.Contains(fieldName))
                    {
                        finalFields.Add(fieldName);
                        addedFields.Add(fieldName);
                        Console.WriteLine($"        Added missing field from SmartObject: {fieldName}");
                    }
                }
            }

            Console.WriteLine($"    Final field list for list view: {string.Join(", ", finalFields)}");
            return finalFields;
        }
        private int ExtractColumnNumber(string gridPosition)
        {
            if (string.IsNullOrEmpty(gridPosition))
                return 0;

            // Skip the row number digits
            string letterPart = new string(gridPosition.SkipWhile(char.IsDigit).ToArray());
            if (string.IsNullOrEmpty(letterPart))
                return 0;

            // Convert letter to column number (A=0, B=1, C=2, etc.)
            letterPart = letterPart.ToUpper();
            int column = 0;
            foreach (char c in letterPart)
            {
                column = column * 26 + (c - 'A');
            }
            return column;
        }

        private bool IsDataControl(string controlType)
        {
            if (string.IsNullOrEmpty(controlType))
                return false;

            string[] dataControlTypes = {
        "textfield", "textarea", "datepicker", "dtpicker",
        "dropdown", "combobox", "checkbox", "radiobutton",
        "richtext", "calendar", "autocomplete", "numberfield",
        "fileattachment", "imageattachment", "hyperlink",
        "multipleselectlist", "bulletedlist", "numberedlist"
    };

            return dataControlTypes.Contains(controlType.ToLower());
        }

        private void CheckInView(string viewName)
        {
            using (FormsManager formsManager = new FormsManager())
            {
                try
                {
                    formsManager.CreateConnection();
                    formsManager.Connection.Open(_connectionManager.ConnectionString.ConnectionString);

                    // Get the view definition XML
                    string viewXml = formsManager.GetViewDefinition(viewName);
                    if (!string.IsNullOrEmpty(viewXml))
                    {
                        // Parse the XML to get the GUID
                        XmlDocument doc = new XmlDocument();
                        doc.LoadXml(viewXml);

                        // Look for View element - it might be nested under SourceCode.Forms/Views
                        XmlNodeList viewNodes = doc.GetElementsByTagName("View");
                        if (viewNodes.Count > 0)
                        {
                            XmlElement viewElement = (XmlElement)viewNodes[0];
                            if (viewElement.HasAttribute("ID"))
                            {
                                string guidString = viewElement.GetAttribute("ID");
                                if (Guid.TryParse(guidString, out Guid viewGuid))
                                {
                                    // Check in the view using its GUID
                                    formsManager.CheckInView(viewGuid);
                                    Console.WriteLine($"    Checked in view: {viewName}");
                                }
                                else
                                {
                                    Console.WriteLine($"    WARNING: Could not parse GUID for view: {viewName}");
                                }
                            }
                            else
                            {
                                Console.WriteLine($"    WARNING: View element does not have ID attribute: {viewName}");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"    WARNING: Could not find View element in XML: {viewName}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"    WARNING: Could not find view to check in: {viewName}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"    WARNING: Could not check in view {viewName}: {ex.Message}");
                }
            }
        }

        private List<ViewSegment> SplitControlsByRepeatingSections(JArray controls)
        {
            List<ViewSegment> segments = new List<ViewSegment>();
            Dictionary<string, List<JObject>> repeatingSectionControls = new Dictionary<string, List<JObject>>();
            List<JObject> currentRegularControls = new List<JObject>();

            Console.WriteLine($"    Analyzing {controls.Count} controls for repeating sections...");

            // Process controls in order to maintain flow
            foreach (JObject control in controls)
            {
                string controlType = control["Type"]?.Value<string>();
                string controlName = control["Name"]?.Value<string>();
                string gridPosition = control["GridPosition"]?.Value<string>();

                // Skip the RepeatingTable control itself
                if (controlType?.ToLower() == "repeatingtable" ||
                    controlType?.ToLower() == "repeatingsection")
                {
                    Console.WriteLine($"        Skipping repeating container control: {controlName} ({controlType})");
                    continue;
                }

                // Check for RepeatingSectionName
                string repeatingSectionName = control["RepeatingSectionName"]?.Value<string>();

                if (string.IsNullOrEmpty(repeatingSectionName))
                {
                    JObject repeatingSectionInfo = control["RepeatingSectionInfo"] as JObject;
                    if (repeatingSectionInfo != null)
                    {
                        bool isInRepeating = repeatingSectionInfo["IsInRepeatingSection"]?.Value<bool>() ?? false;
                        if (isInRepeating)
                        {
                            repeatingSectionName = repeatingSectionInfo["RepeatingSectionName"]?.Value<string>();
                        }
                    }
                }

                if (!string.IsNullOrEmpty(repeatingSectionName))
                {
                    // We've encountered a repeating section control

                    // First, save any accumulated regular controls as a segment
                    if (currentRegularControls.Count > 0)
                    {
                        segments.Add(new ViewSegment
                        {
                            Type = SegmentType.Regular,
                            Controls = new List<JObject>(currentRegularControls),
                            SectionName = null,
                            TitleLabel = null
                        });

                        Console.WriteLine($"    Created regular segment with {currentRegularControls.Count} controls (before repeating section)");
                        currentRegularControls.Clear();
                    }

                    // Normalize the section name
                    string originalName = repeatingSectionName;
                    if (repeatingSectionName.StartsWith("TABLECTRL", StringComparison.OrdinalIgnoreCase))
                    {
                        string ctrlNumber = repeatingSectionName.Substring("TABLECTRL".Length);
                        repeatingSectionName = $"Table_CTRL{ctrlNumber}";
                    }

                    // Add control to repeating section
                    if (!repeatingSectionControls.ContainsKey(repeatingSectionName))
                    {
                        repeatingSectionControls[repeatingSectionName] = new List<JObject>();
                        Console.WriteLine($"        Found new repeating section: {repeatingSectionName} (original: {originalName})");
                    }

                    repeatingSectionControls[repeatingSectionName].Add(control);
                    Console.WriteLine($"        Control '{controlName}' belongs to repeating section: {repeatingSectionName}");
                }
                else
                {
                    // This is a regular control

                    // Check if we just finished processing a repeating section
                    // by checking if the last segment added was a repeating section
                    if (segments.Count > 0 && segments.Last().Type == SegmentType.RepeatingSection)
                    {
                        // We're starting a new regular segment after a repeating section
                        // Don't add to existing regular controls, start fresh
                        currentRegularControls.Add(control);
                    }
                    else
                    {
                        // Continue accumulating regular controls
                        currentRegularControls.Add(control);
                    }
                }
            }

            // Process any remaining regular controls
            if (currentRegularControls.Count > 0)
            {
                segments.Add(new ViewSegment
                {
                    Type = SegmentType.Regular,
                    Controls = currentRegularControls,
                    SectionName = null,
                    TitleLabel = null
                });
                Console.WriteLine($"    Created final regular segment with {currentRegularControls.Count} controls");
            }

            // Now we need to process the repeating sections and insert them in the correct order
            // based on their grid positions
            List<ViewSegment> finalSegments = new List<ViewSegment>();
            Dictionary<string, int> sectionPositions = new Dictionary<string, int>();

            // Determine the position of each repeating section based on first control's grid position
            foreach (var kvp in repeatingSectionControls)
            {
                if (kvp.Value.Count > 0)
                {
                    string firstGridPos = kvp.Value[0]["GridPosition"]?.Value<string>();
                    if (!string.IsNullOrEmpty(firstGridPos))
                    {
                        int row = ExtractRowNumber(firstGridPos);
                        sectionPositions[kvp.Key] = row;
                    }
                }
            }

            // Sort repeating sections by their position
            var sortedRepeatingSections = repeatingSectionControls
                .OrderBy(kvp => sectionPositions.ContainsKey(kvp.Key) ? sectionPositions[kvp.Key] : int.MaxValue)
                .ToList();

            // Rebuild segments in the correct order
            int regularSegmentIndex = 0;
            int lastProcessedRow = 0;

            foreach (var sectionKvp in sortedRepeatingSections)
            {
                int sectionRow = sectionPositions.ContainsKey(sectionKvp.Key) ? sectionPositions[sectionKvp.Key] : int.MaxValue;

                // Add any regular segments that come before this repeating section
                while (regularSegmentIndex < segments.Count && segments[regularSegmentIndex].Type == SegmentType.Regular)
                {
                    var regularSegment = segments[regularSegmentIndex];

                    // Check if this regular segment should come before the repeating section
                    bool shouldAddBefore = false;
                    if (regularSegment.Controls.Count > 0)
                    {
                        // Get the last control's position in this segment
                        var lastControl = regularSegment.Controls.Last();
                        string lastGridPos = lastControl["GridPosition"]?.Value<string>();
                        if (!string.IsNullOrEmpty(lastGridPos))
                        {
                            int lastRow = ExtractRowNumber(lastGridPos);
                            if (lastRow < sectionRow)
                            {
                                shouldAddBefore = true;
                            }
                        }
                    }

                    if (shouldAddBefore)
                    {
                        finalSegments.Add(regularSegment);
                        regularSegmentIndex++;

                        // Update last processed row
                        if (regularSegment.Controls.Count > 0)
                        {
                            string lastPos = regularSegment.Controls.Last()["GridPosition"]?.Value<string>();
                            if (!string.IsNullOrEmpty(lastPos))
                            {
                                lastProcessedRow = ExtractRowNumber(lastPos);
                            }
                        }
                    }
                    else
                    {
                        break;
                    }
                }

                // Add the repeating section segment
                finalSegments.Add(new ViewSegment
                {
                    Type = SegmentType.RepeatingSection,
                    Controls = sectionKvp.Value,
                    SectionName = sectionKvp.Key,
                    TitleLabel = null
                });

                Console.WriteLine($"    Added repeating section segment '{sectionKvp.Key}' with {sectionKvp.Value.Count} controls");

                // Update last processed row
                if (sectionKvp.Value.Count > 0)
                {
                    var lastControl = sectionKvp.Value.Last();
                    string lastPos = lastControl["GridPosition"]?.Value<string>();
                    if (!string.IsNullOrEmpty(lastPos))
                    {
                        lastProcessedRow = ExtractRowNumber(lastPos);
                    }
                }
            }

            // Add any remaining regular segments
            while (regularSegmentIndex < segments.Count)
            {
                if (segments[regularSegmentIndex].Type == SegmentType.Regular)
                {
                    finalSegments.Add(segments[regularSegmentIndex]);
                }
                regularSegmentIndex++;
            }

            // If we don't have any repeating sections, just return the original segments
            if (repeatingSectionControls.Count == 0)
            {
                return segments;
            }

            // Log summary
            Console.WriteLine($"    === Segmentation Summary ===");
            Console.WriteLine($"    Total segments created: {finalSegments.Count}");

            int regularCount = 0;
            int repeatingSectionCount = 0;
            foreach (var segment in finalSegments)
            {
                if (segment.Type == SegmentType.Regular)
                {
                    regularCount++;
                    Console.WriteLine($"    Regular segment #{regularCount}: {segment.Controls.Count} controls");
                }
                else
                {
                    repeatingSectionCount++;
                    Console.WriteLine($"    Repeating section '{segment.SectionName}': {segment.Controls.Count} controls");
                }
            }

            return finalSegments;
        }



        private int ExtractRowNumber(string gridPosition)
        {
            if (string.IsNullOrEmpty(gridPosition))
                return 1;

            string numericPart = new string(gridPosition.TakeWhile(char.IsDigit).ToArray());
            if (int.TryParse(numericPart, out int row))
            {
                return row;
            }
            return 1;
        }

        private void GenerateXmlBasedView(string viewName, string smoName, JArray controls,
                                    JArray dataArray, string categoryPath,
                                    JArray dynamicSections, JObject conditionalVisibility,
                                    bool isItemView, string infopathViewName = null,
                                    int partNumber = 0, string repeatingSectionName = null)
        {
            using (FormsManager formsManager = new FormsManager())
            {
                try
                {
                    formsManager.CreateConnection();
                    formsManager.Connection.Open(_connectionManager.ConnectionString.ConnectionString);

                    DeleteExistingView(formsManager, viewName);

                    _connectionManager.Connect();
                    string smoXml = _connectionManager.ManagementServer.GetSmartObjectDefinition(smoName);
                    SmartObjectDefinition smoDef = SmartObjectDefinition.Create(smoXml);
                    _connectionManager.Disconnect();

                    // Filter out non-renderable controls (nested table processing already done at top level)
                    JArray filteredControls = FilterOutNonRenderableControls(controls);

                    // Normalize control grid positions
                    JArray normalizedControls = NormalizeControlGridPositions(filteredControls);

                    // Create view
                    string viewTitle;
                    XmlDocument viewDoc = _xmlBuilder.CreateViewXmlStructure(viewName, smoDef.Guid.ToString(),
                        smoName, normalizedControls, dataArray,
                        dynamicSections, // Pass actual dynamic sections
                        conditionalVisibility, // Pass actual conditional visibility
                        isItemView, out viewTitle);

                    // Store the title if found
                    if (!string.IsNullOrEmpty(viewTitle))
                    {
                        ViewTitles[viewName] = viewTitle;
                        Console.WriteLine($"    Stored title '{viewTitle}' for view '{viewName}'");
                    }

                    formsManager.DeployViews(viewDoc.OuterXml, categoryPath, true);
                    Console.WriteLine($"    Successfully deployed: {viewName}");

                    // REGISTER WITH THE REGISTRY
                    var metadata = new SmartObjectViewRegistry.ViewMetadata
                    {
                        ViewTitle = viewTitle,
                        Category = categoryPath,
                        InfoPathViewName = infopathViewName,
                        PartNumber = partNumber,
                        IsRepeatingSection = isItemView,
                        RepeatingSectionName = repeatingSectionName
                    };

                    SmartObjectViewRegistry.RegisterView(
                        viewName,
                        smoName,
                        isItemView ? SmartObjectViewRegistry.ViewType.Item :
                                    SmartObjectViewRegistry.ViewType.Capture,
                        metadata
                    );

                    CheckInView(viewName);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"    Warning: Could not deploy view {viewName}: {ex.Message}");
                }
                finally
                {
                    _connectionManager.Disconnect();
                }
            }
        }

        private JArray FilterOutNonRenderableControls(JArray controls)
        {
            JArray filtered = new JArray();
            int removedCount = 0;

            foreach (JObject control in controls)
            {
                string controlType = control["Type"]?.Value<string>();
                string name = control["Name"]?.Value<string>();

                // Use configuration to determine non-renderable control types
                if (_config.ControlFilters.IsNonRenderable(controlType))
                {
                    Console.WriteLine($"        Filtering out non-renderable control: {name} ({controlType})");
                    removedCount++;
                    continue;
                }

                filtered.Add(control);
            }

            if (removedCount > 0)
            {
                Console.WriteLine($"        Filtered out {removedCount} non-renderable control(s)");
            }

            return filtered;
        }



        private JArray NormalizeControlGridPositions(JArray controls)
        {
            if (controls == null || controls.Count == 0)
                return controls;

            // Find all rows that actually have controls
            HashSet<int> rowsWithControls = new HashSet<int>();
            foreach (JObject control in controls)
            {
                string gridPos = control["GridPosition"]?.Value<string>();
                if (!string.IsNullOrEmpty(gridPos))
                {
                    int row = ExtractRowNumber(gridPos);
                    rowsWithControls.Add(row);
                }
            }

            if (rowsWithControls.Count == 0)
                return controls;

            // Create a mapping from old row numbers to new consecutive row numbers starting from 1
            Dictionary<int, int> oldRowToNewRow = new Dictionary<int, int>();
            int newRowNumber = 1;
            foreach (int oldRow in rowsWithControls.OrderBy(r => r))
            {
                oldRowToNewRow[oldRow] = newRowNumber++;
            }

            // Check if any remapping is needed
            bool needsRemapping = oldRowToNewRow.Any(kvp => kvp.Key != kvp.Value);

            if (!needsRemapping)
            {
                Console.WriteLine($"        Grid positions already normalized (no empty rows)");
                return controls;
            }

            Console.WriteLine($"        Normalizing grid positions: compacting {rowsWithControls.Count} rows");
            foreach (var mapping in oldRowToNewRow.Where(m => m.Key != m.Value))
            {
                Console.WriteLine($"          Row {mapping.Key} -> Row {mapping.Value}");
            }

            // Create a new array with adjusted positions
            JArray normalizedControls = new JArray();
            foreach (JObject control in controls)
            {
                JObject normalizedControl = (JObject)control.DeepClone();
                string gridPos = normalizedControl["GridPosition"]?.Value<string>();

                if (!string.IsNullOrEmpty(gridPos))
                {
                    int oldRow = ExtractRowNumber(gridPos);

                    if (oldRowToNewRow.ContainsKey(oldRow))
                    {
                        int newRow = oldRowToNewRow[oldRow];
                        string column = ExtractColumnLetter(gridPos);
                        string newGridPos = $"{newRow}{column}";
                        normalizedControl["GridPosition"] = newGridPos;

                        // Also update any section info if needed
                        JObject sectionInfo = normalizedControl["SectionInfo"] as JObject;
                        if (sectionInfo != null)
                        {
                            int? startRow = sectionInfo["StartRow"]?.Value<int>();
                            int? endRow = sectionInfo["EndRow"]?.Value<int>();

                            if (startRow.HasValue && oldRowToNewRow.ContainsKey(startRow.Value))
                            {
                                sectionInfo["StartRow"] = oldRowToNewRow[startRow.Value];
                            }
                            if (endRow.HasValue && oldRowToNewRow.ContainsKey(endRow.Value))
                            {
                                sectionInfo["EndRow"] = oldRowToNewRow[endRow.Value];
                            }
                        }
                    }
                }

                normalizedControls.Add(normalizedControl);
            }

            return normalizedControls;
        }


        private string ExtractColumnLetter(string gridPosition)
        {
            if (string.IsNullOrEmpty(gridPosition))
                return "A";

            // Extract letter part (e.g., "A", "B", "C", "D", "E", "F", etc.)
            string letterPart = new string(gridPosition.SkipWhile(char.IsDigit).ToArray());
            return string.IsNullOrEmpty(letterPart) ? "A" : letterPart.ToUpper();
        }

   


        private void GenerateListViewUsingAPI(string viewName, string smoName, string categoryPath,
                                            List<string> visibleFields = null,
                                            string infopathViewName = null,
                                            string repeatingSectionName = null)
        {
            using (FormsManager formsManager = new FormsManager())
            {
                try
                {
                    formsManager.CreateConnection();
                    formsManager.Connection.Open(_connectionManager.ConnectionString.ConnectionString);

                    // Delete existing view if it exists
                    try
                    {
                        var existingView = formsManager.GetViewDefinition(viewName);
                        if (existingView != null)
                        {
                            formsManager.DeleteView(viewName);
                            Console.WriteLine($"    Deleted existing view: {viewName}");
                        }
                    }
                    catch { }

                    // Check if there's a corresponding item view with a title
                    string itemViewTitle = null;
                    string baseName = ExtractRepeatingSectionName(viewName);
                    if (!string.IsNullOrEmpty(baseName))
                    {
                        // CHECK REGISTRY FOR ITEM VIEW
                        var itemViews = SmartObjectViewRegistry.GetViewsForSmartObject(smoName,
                            SmartObjectViewRegistry.ViewType.Item);

                        foreach (var itemViewName in itemViews)
                        {
                            if (itemViewName.Contains($"_{baseName}_Item"))
                            {
                                var itemViewInfo = SmartObjectViewRegistry.GetViewInfo(itemViewName);
                                if (itemViewInfo?.Metadata?.ViewTitle != null)
                                {
                                    itemViewTitle = itemViewInfo.Metadata.ViewTitle;
                                    Console.WriteLine($"        Found title '{itemViewTitle}' from item view for list view");
                                    break;
                                }
                            }
                        }
                    }

                    using (SourceCode.Forms.Utilities.AutoGenerator autoGenerator =
                        new SourceCode.Forms.Utilities.AutoGenerator(formsManager.Connection))
                    {
                        // Use IsEditable flag to generate proper editable list
                        SourceCode.Forms.Utilities.ViewCreationOption vcOptions =
                            SourceCode.Forms.Utilities.ViewCreationOption.LabelsToLeftOfControls |
                            SourceCode.Forms.Utilities.ViewCreationOption.IsEditable;

                        SourceCode.Forms.Utilities.ViewGenerator viewGenerator =
                            new SourceCode.Forms.Utilities.ViewGenerator(
                                SourceCode.Forms.Authoring.ViewType.List,
                                vcOptions);

                        // Get properties to display
                        List<string> displayProperties = new List<string>();

                        // IMPORTANT: Filter out ParentID and ID before processing
                        if (visibleFields != null && visibleFields.Count > 0)
                        {
                            Console.WriteLine($"    Processing {visibleFields.Count} visible fields from InfoPath view");

                            // Filter out ParentID and ID fields
                            visibleFields = visibleFields.Where(f =>
                                !f.Equals("PARENTID", StringComparison.OrdinalIgnoreCase) &&
                                !f.Equals("PARENT_ID", StringComparison.OrdinalIgnoreCase) &&
                                !f.Equals("ID", StringComparison.OrdinalIgnoreCase)).ToList();

                            Console.WriteLine($"    After filtering ID/ParentID: {visibleFields.Count} fields remain");

                            // Verify fields exist in SmartObject
                            if (_smoFieldMappings.ContainsKey(smoName))
                            {
                                List<string> validFields = new List<string>();
                                var smoFields = _smoFieldMappings[smoName];

                                foreach (string field in visibleFields)
                                {
                                    string sanitizedField = NameSanitizer.SanitizePropertyName(field);
                                    string upperField = sanitizedField.ToUpper();

                                    // Skip ID and ParentID again (in case they come through in different case)
                                    if (upperField == "ID" || upperField == "PARENTID" || upperField == "PARENT_ID")
                                    {
                                        Console.WriteLine($"        Skipping system field: {field}");
                                        continue;
                                    }

                                    // Check multiple variations of the field name
                                    bool fieldFound = false;
                                    string actualFieldName = null;

                                    // Check exact match first
                                    if (smoFields.ContainsKey(sanitizedField))
                                    {
                                        fieldFound = true;
                                        actualFieldName = sanitizedField;
                                    }
                                    // Check uppercase version
                                    else if (smoFields.ContainsKey(upperField))
                                    {
                                        fieldFound = true;
                                        actualFieldName = upperField;
                                    }
                                    // Check original field name
                                    else if (smoFields.ContainsKey(field))
                                    {
                                        fieldFound = true;
                                        actualFieldName = field;
                                    }
                                    // Check if any SmartObject field matches (case-insensitive)
                                    else
                                    {
                                        foreach (var smoField in smoFields.Keys)
                                        {
                                            if (smoField.Equals(field, StringComparison.OrdinalIgnoreCase) ||
                                                smoField.Equals(sanitizedField, StringComparison.OrdinalIgnoreCase))
                                            {
                                                fieldFound = true;
                                                actualFieldName = smoField;
                                                break;
                                            }
                                        }
                                    }

                                    if (fieldFound && !string.IsNullOrEmpty(actualFieldName))
                                    {
                                        validFields.Add(actualFieldName);
                                        Console.WriteLine($"        Validated field: {field} -> {actualFieldName}");
                                    }
                                    else
                                    {
                                        Console.WriteLine($"        WARNING: Field '{field}' not found in SmartObject {smoName}");
                                    }
                                }

                                if (validFields.Count > 0)
                                {
                                    displayProperties = validFields;
                                    Console.WriteLine($"    Using {validFields.Count} validated fields");
                                }
                                else
                                {
                                    Console.WriteLine($"        ERROR: No valid fields found, using all SmartObject fields except ID/ParentID");
                                    foreach (var field in smoFields)
                                    {
                                        string upperField = field.Key.ToUpper();
                                        if (upperField != "ID" && upperField != "PARENTID" && upperField != "PARENT_ID")
                                        {
                                            displayProperties.Add(field.Key);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                Console.WriteLine($"        WARNING: SmartObject {smoName} not found in field mappings");
                                // Still filter out ID/ParentID from the provided fields
                                displayProperties = visibleFields;
                            }
                        }
                        else if (_smoFieldMappings.ContainsKey(smoName))
                        {
                            // Use all fields from SmartObject as fallback (except ID/ParentID)
                            foreach (var field in _smoFieldMappings[smoName])
                            {
                                string upperField = field.Key.ToUpper();
                                if (upperField != "ID" && upperField != "PARENTID" && upperField != "PARENT_ID")
                                {
                                    displayProperties.Add(field.Key);
                                }
                            }
                            Console.WriteLine($"    Using all {displayProperties.Count} SmartObject fields as fallback (excluding ID/ParentID)");
                        }

                        // If still no fields, try to get them from the SmartObject definition
                        if (displayProperties.Count == 0)
                        {
                            try
                            {
                                _connectionManager.Connect();
                                string smoXml = _connectionManager.ManagementServer.GetSmartObjectDefinition(smoName);
                                SmartObjectDefinition smoDef = SmartObjectDefinition.Create(smoXml);

                                for (int i = 0; i < smoDef.Properties.Count; i++)
                                {
                                    string propName = smoDef.Properties[i].Name;
                                    string upperProp = propName.ToUpper();

                                    // Exclude ID and ParentID
                                    if (upperProp != "ID" && upperProp != "PARENTID" && upperProp != "PARENT_ID")
                                    {
                                        displayProperties.Add(propName);
                                    }
                                }
                                Console.WriteLine($"    Retrieved {displayProperties.Count} fields from SmartObject definition (excluding ID/ParentID)");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"        ERROR getting SmartObject definition: {ex.Message}");
                            }
                            finally
                            {
                                _connectionManager.Disconnect();
                            }
                        }

                        // Final validation - ensure we have at least one field
                        if (displayProperties.Count == 0)
                        {
                            Console.WriteLine($"        CRITICAL: No fields found for list view, adding default 'Name' field");
                            displayProperties.Add("Name");
                        }

                        Console.WriteLine($"    List view will display fields: {string.Join(", ", displayProperties)}");

                        // For editable list, use InputProperties
                        viewGenerator.InputProperties.AddRange(displayProperties);
                        viewGenerator.DefaultListMethod = "GetList";

                        // Add standard list methods
                        List<string> methods = new List<string>()
                {
                    "Create",
                    "Save",
                    "Delete",
                    "Load",
                    "GetList"
                };
                        viewGenerator.InstanceMethods.AddRange(methods);

                        // Generate the view
                        SourceCode.Forms.Authoring.View generatedView =
                            autoGenerator.Generate(viewGenerator, smoName, viewName);

                        // Get the XML and modify it
                        XmlDocument viewDoc = new XmlDocument();
                        viewDoc.LoadXml(generatedView.ToXml());

                        // Add all the modifications...
                        AddIDParameterToListView(viewDoc);
                        AddParentIdFilter(viewDoc, viewName);
                        AddInitEventCondition(viewDoc, viewName);
                        HideToolbarButtonsInInitEvent(viewDoc, viewName);
                        DisableFilteringInEditableList(viewDoc, viewName);
                        HideEditableListButtons(viewDoc, new[] { "Save", "Refresh", "Edit" });

                        // Find and store the Add and Delete button IDs
                        string addButtonId = null;
                        string deleteButtonId = null;
                        FindEditableListButtons(viewDoc, ref addButtonId, ref deleteButtonId);

                        // Remove the default Add and Delete button rules
                        RemoveButtonRules(viewDoc, addButtonId, deleteButtonId);

                        // Register buttons for form-level rule creation
                        if (!string.IsNullOrEmpty(addButtonId) && !string.IsNullOrEmpty(deleteButtonId))
                        {
                            ButtonTracker.ButtonInfo buttonInfo = new ButtonTracker.ButtonInfo
                            {
                                AddButtonId = addButtonId,
                                AddButtonName = "Add ToolBar Button",
                                DeleteButtonId = deleteButtonId,
                                DeleteButtonName = "Delete ToolBar Button"
                            };
                            ButtonTracker.RegisterViewButtons(viewName, buttonInfo);
                            Console.WriteLine($"        Registered Add button (ID: {addButtonId}) and Delete button (ID: {deleteButtonId}) for form-level rules");
                        }

                        // Add WrapText to all header labels
                        AddWrapTextToAllHeaderLabels(viewDoc, displayProperties);

                        // Register the editable controls from the generated view
                        RegisterEditableListControls(viewDoc, viewName, smoName, displayProperties);

                        // Verify the ID parameter is still present before deployment
                        VerifyIDParameter(viewDoc);

                        // Deploy the modified view
                        formsManager.DeployViews(viewDoc.OuterXml, categoryPath, false);

                        Console.WriteLine($"    Successfully deployed editable list view: {viewName}");
                        Console.WriteLine($"        SmartObject: {smoName}");
                        Console.WriteLine($"        InfoPath View: {infopathViewName ?? "N/A"}");
                        Console.WriteLine($"        Repeating Section: {repeatingSectionName ?? "N/A"}");
                        Console.WriteLine($"        Fields displayed: {displayProperties.Count}");

                        // REGISTER WITH THE REGISTRY
                        var metadata = new SmartObjectViewRegistry.ViewMetadata
                        {
                            ViewTitle = itemViewTitle,
                            Category = categoryPath,
                            InfoPathViewName = infopathViewName,
                            IsRepeatingSection = true,
                            RepeatingSectionName = repeatingSectionName
                        };

                        SmartObjectViewRegistry.RegisterView(
                            viewName,
                            smoName,
                            SmartObjectViewRegistry.ViewType.List,
                            metadata
                        );

                        // Store the title for this list view if we found one from the item view
                        if (!string.IsNullOrEmpty(itemViewTitle))
                        {
                            ViewTitles[viewName] = itemViewTitle;
                        }

                        // Check in the view after deployment
                        CheckInView(viewName);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"    Error generating list view {viewName}: {ex.Message}");
                    Console.WriteLine($"    Stack trace: {ex.StackTrace}");

                    if (ex.InnerException != null)
                    {
                        Console.WriteLine($"    Inner exception: {ex.InnerException.Message}");
                    }

                    throw; // Re-throw to handle at higher level
                }
            }
        }

        private void AddInitEventCondition(XmlDocument viewDoc, string viewName)
        {
            // Find the Events section
            XmlNodeList eventsNodes = viewDoc.GetElementsByTagName("Events");
            if (eventsNodes.Count == 0)
            {
                Console.WriteLine("        WARNING: No Events section found to add condition");
                return;
            }

            XmlElement events = (XmlElement)eventsNodes[0];

            // Find the User Init event with GetList
            XmlNodeList eventNodes = events.GetElementsByTagName("Event");
            foreach (XmlElement eventElement in eventNodes)
            {
                XmlNodeList nameNodes = eventElement.GetElementsByTagName("Name");
                string eventType = eventElement.GetAttribute("Type");

                if (nameNodes.Count > 0 && nameNodes[0].InnerText == "Init" && eventType == "User")
                {
                    // Check if this has GetList action
                    bool hasGetList = false;
                    XmlNodeList actionNodes = eventElement.GetElementsByTagName("Action");
                    foreach (XmlElement action in actionNodes)
                    {
                        XmlNodeList propNodes = action.GetElementsByTagName("Property");
                        foreach (XmlElement prop in propNodes)
                        {
                            XmlNodeList propNameNodes = prop.GetElementsByTagName("Name");
                            XmlNodeList propValueNodes = prop.GetElementsByTagName("Value");

                            if (propNameNodes.Count > 0 && propNameNodes[0].InnerText == "Method" &&
                                propValueNodes.Count > 0 && propValueNodes[0].InnerText == "GetList")
                            {
                                hasGetList = true;
                                break;
                            }
                        }
                        if (hasGetList) break;
                    }

                    if (!hasGetList) continue;

                    // Set IsExtended attribute
                    eventElement.SetAttribute("IsExtended", "True");

                    // Find the Handlers element
                    XmlNodeList handlersNodes = eventElement.GetElementsByTagName("Handlers");
                    if (handlersNodes.Count == 0) continue;

                    XmlElement handlers = (XmlElement)handlersNodes[0];

                    // Find the Handler element
                    XmlNodeList handlerNodes = handlers.GetElementsByTagName("Handler");
                    if (handlerNodes.Count == 0) continue;

                    XmlElement handler = (XmlElement)handlerNodes[0];

                    // Check if Handler already has Conditions
                    XmlNodeList existingConditions = handler.GetElementsByTagName("Conditions");
                    if (existingConditions.Count > 0)
                    {
                        Console.WriteLine("        Conditions already exist in Handler, skipping");
                        continue;
                    }

                    // Add HandlerName property if it doesn't exist
                    XmlNodeList handlerPropsNodes = handler.GetElementsByTagName("Properties");
                    XmlElement handlerProps = null;
                    if (handlerPropsNodes.Count == 0)
                    {
                        handlerProps = viewDoc.CreateElement("Properties");

                        // Insert Properties as first child of Handler
                        if (handler.FirstChild != null)
                        {
                            handler.InsertBefore(handlerProps, handler.FirstChild);
                        }
                        else
                        {
                            handler.AppendChild(handlerProps);
                        }
                    }
                    else
                    {
                        handlerProps = (XmlElement)handlerPropsNodes[0];
                    }

                    // Add HandlerName property
                    bool hasHandlerName = false;
                    XmlNodeList handlerPropNodes = handlerProps.GetElementsByTagName("Property");
                    foreach (XmlElement prop in handlerPropNodes)
                    {
                        XmlNodeList propNameNodes = prop.GetElementsByTagName("Name");
                        if (propNameNodes.Count > 0 && propNameNodes[0].InnerText == "HandlerName")
                        {
                            hasHandlerName = true;
                            break;
                        }
                    }

                    if (!hasHandlerName)
                    {
                        XmlElement handlerNameProp = viewDoc.CreateElement("Property");
                        XmlElement handlerNameName = viewDoc.CreateElement("Name");
                        handlerNameName.InnerText = "HandlerName";
                        XmlElement handlerNameValue = viewDoc.CreateElement("Value");
                        handlerNameValue.InnerText = "IfLogicalHandler";
                        handlerNameProp.AppendChild(handlerNameName);
                        handlerNameProp.AppendChild(handlerNameValue);
                        handlerProps.AppendChild(handlerNameProp);
                    }

                    // Create Conditions element
                    XmlElement conditions = viewDoc.CreateElement("Conditions");
                    XmlElement condition = viewDoc.CreateElement("Condition");
                    condition.SetAttribute("ID", Guid.NewGuid().ToString());
                    condition.SetAttribute("DefinitionID", Guid.NewGuid().ToString());

                    // Condition Properties
                    XmlElement condProps = viewDoc.CreateElement("Properties");

                    XmlElement locProp = viewDoc.CreateElement("Property");
                    XmlElement locName = viewDoc.CreateElement("Name");
                    locName.InnerText = "Location";
                    XmlElement locValue = viewDoc.CreateElement("Value");
                    locValue.InnerText = "View";
                    locProp.AppendChild(locName);
                    locProp.AppendChild(locValue);
                    condProps.AppendChild(locProp);

                    XmlElement nameProp = viewDoc.CreateElement("Property");
                    XmlElement nameElem = viewDoc.CreateElement("Name");
                    nameElem.InnerText = "Name";
                    XmlElement nameValue = viewDoc.CreateElement("Value");
                    nameValue.InnerText = "SimpleNotBlankViewParameterCondition";
                    nameProp.AppendChild(nameElem);
                    nameProp.AppendChild(nameValue);
                    condProps.AppendChild(nameProp);

                    condition.AppendChild(condProps);

                    // Expression with IsNotBlank
                    XmlElement expressions = viewDoc.CreateElement("Expressions");
                    XmlElement isNotBlank = viewDoc.CreateElement("IsNotBlank");
                    XmlElement item = viewDoc.CreateElement("Item");
                    item.SetAttribute("SourceType", "ViewParameter");
                    item.SetAttribute("SourceID", "ID");
                    item.SetAttribute("SourceName", "ID");
                    item.SetAttribute("DataType", "Text");

                    isNotBlank.AppendChild(item);
                    expressions.AppendChild(isNotBlank);
                    condition.AppendChild(expressions);
                    conditions.AppendChild(condition);

                    // Find Actions element in Handler
                    XmlNodeList actionsNodes = handler.GetElementsByTagName("Actions");
                    if (actionsNodes.Count > 0)
                    {
                        // Insert Conditions before Actions
                        handler.InsertBefore(conditions, actionsNodes[0]);
                    }
                    else
                    {
                        // Append if no Actions found
                        handler.AppendChild(conditions);
                    }

                    Console.WriteLine($"        Added 'ID parameter is not blank' condition to Init event");
                    return;
                }
            }
        }

        private void HideToolbarButtonsInInitEvent(XmlDocument viewDoc, string viewName)
        {
            // Find the Events section
            XmlNodeList eventsNodes = viewDoc.GetElementsByTagName("Events");
            if (eventsNodes.Count == 0)
            {
                Console.WriteLine("        WARNING: No Events section found to hide toolbar buttons");
                return;
            }

            XmlElement events = (XmlElement)eventsNodes[0];

            // Find Add and Delete button IDs first
            string addButtonId = null;
            string deleteButtonId = null;

            XmlNodeList allControls = viewDoc.GetElementsByTagName("Control");
            foreach (XmlElement control in allControls)
            {
                if (control.GetAttribute("Type") == "ToolBarButton")
                {
                    XmlNodeList nameNodes = control.GetElementsByTagName("Name");
                    if (nameNodes.Count > 0)
                    {
                        string name = nameNodes[0].InnerText;
                        if (name.Contains("Add ToolBar Button"))
                        {
                            addButtonId = control.GetAttribute("ID");
                        }
                        else if (name.Contains("Delete ToolBar Button"))
                        {
                            deleteButtonId = control.GetAttribute("ID");
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(addButtonId) || string.IsNullOrEmpty(deleteButtonId))
            {
                Console.WriteLine("        WARNING: Could not find Add or Delete toolbar button IDs");
                return;
            }

            // Find the User Init event with GetList
            XmlNodeList eventNodes = events.GetElementsByTagName("Event");
            foreach (XmlElement eventElement in eventNodes)
            {
                XmlNodeList nameNodes = eventElement.GetElementsByTagName("Name");
                string eventType = eventElement.GetAttribute("Type");

                if (nameNodes.Count > 0 && nameNodes[0].InnerText == "Init" && eventType == "User")
                {
                    // Find the Handler with Actions containing GetList
                    XmlNodeList handlersNodes = eventElement.GetElementsByTagName("Handlers");
                    if (handlersNodes.Count == 0) continue;

                    XmlElement handlers = (XmlElement)handlersNodes[0];
                    XmlNodeList handlerNodes = handlers.GetElementsByTagName("Handler");
                    if (handlerNodes.Count == 0) continue;

                    XmlElement handler = (XmlElement)handlerNodes[0];

                    // Find the Actions element
                    XmlNodeList actionsNodes = handler.GetElementsByTagName("Actions");
                    if (actionsNodes.Count == 0) continue;

                    XmlElement actions = (XmlElement)actionsNodes[0];

                    // Get the view GUID
                    string viewGuid = eventElement.GetAttribute("SourceID");

                    // Create Transfer action to hide Delete button
                    XmlElement hideDeleteAction = viewDoc.CreateElement("Action");
                    hideDeleteAction.SetAttribute("ID", Guid.NewGuid().ToString());
                    hideDeleteAction.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
                    hideDeleteAction.SetAttribute("Type", "Transfer");
                    hideDeleteAction.SetAttribute("ExecutionType", "Synchronous");

                    XmlElement deleteProps = viewDoc.CreateElement("Properties");

                    XmlElement deleteLocProp = viewDoc.CreateElement("Property");
                    XmlElement deleteLocName = viewDoc.CreateElement("Name");
                    deleteLocName.InnerText = "Location";
                    XmlElement deleteLocValue = viewDoc.CreateElement("Value");
                    deleteLocValue.InnerText = "View";
                    deleteLocProp.AppendChild(deleteLocName);
                    deleteLocProp.AppendChild(deleteLocValue);
                    deleteProps.AppendChild(deleteLocProp);

                    XmlElement deleteControlProp = viewDoc.CreateElement("Property");
                    XmlElement deleteControlName = viewDoc.CreateElement("Name");
                    deleteControlName.InnerText = "ControlID";
                    XmlElement deleteControlValue = viewDoc.CreateElement("Value");
                    deleteControlValue.InnerText = deleteButtonId;
                    XmlElement deleteControlDisplay = viewDoc.CreateElement("DisplayValue");
                    deleteControlDisplay.InnerText = "Delete ToolBar Button";
                    XmlElement deleteControlNameValue = viewDoc.CreateElement("NameValue");
                    deleteControlNameValue.InnerText = "Delete ToolBar Button";
                    deleteControlProp.AppendChild(deleteControlName);
                    deleteControlProp.AppendChild(deleteControlDisplay);
                    deleteControlProp.AppendChild(deleteControlNameValue);
                    deleteControlProp.AppendChild(deleteControlValue);
                    deleteProps.AppendChild(deleteControlProp);

                    XmlElement deleteViewProp = viewDoc.CreateElement("Property");
                    XmlElement deleteViewName = viewDoc.CreateElement("Name");
                    deleteViewName.InnerText = "ViewID";
                    XmlElement deleteViewValue = viewDoc.CreateElement("Value");
                    deleteViewValue.InnerText = viewGuid;
                    XmlElement deleteViewDisplay = viewDoc.CreateElement("DisplayValue");
                    deleteViewDisplay.InnerText = viewName;
                    XmlElement deleteViewNameValue = viewDoc.CreateElement("NameValue");
                    deleteViewNameValue.InnerText = viewName;
                    deleteViewProp.AppendChild(deleteViewName);
                    deleteViewProp.AppendChild(deleteViewDisplay);
                    deleteViewProp.AppendChild(deleteViewNameValue);
                    deleteViewProp.AppendChild(deleteViewValue);
                    deleteProps.AppendChild(deleteViewProp);

                    hideDeleteAction.AppendChild(deleteProps);

                    XmlElement deleteParams = viewDoc.CreateElement("Parameters");
                    XmlElement deleteParam = viewDoc.CreateElement("Parameter");
                    deleteParam.SetAttribute("SourceType", "Value");
                    deleteParam.SetAttribute("TargetID", "isvisible");
                    deleteParam.SetAttribute("TargetDisplayName", "Delete ToolBar Button");
                    deleteParam.SetAttribute("TargetType", "ControlProperty");
                    XmlElement deleteSourceValue = viewDoc.CreateElement("SourceValue");
                    deleteSourceValue.SetAttribute("xml:space", "preserve");
                    deleteSourceValue.InnerText = "false";
                    deleteParam.AppendChild(deleteSourceValue);
                    deleteParams.AppendChild(deleteParam);
                    hideDeleteAction.AppendChild(deleteParams);

                    actions.AppendChild(hideDeleteAction);

                    // Create Transfer action to hide Add button
                    XmlElement hideAddAction = viewDoc.CreateElement("Action");
                    hideAddAction.SetAttribute("ID", Guid.NewGuid().ToString());
                    hideAddAction.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
                    hideAddAction.SetAttribute("Type", "Transfer");
                    hideAddAction.SetAttribute("ExecutionType", "Synchronous");

                    XmlElement addProps = viewDoc.CreateElement("Properties");

                    XmlElement addLocProp = viewDoc.CreateElement("Property");
                    XmlElement addLocName = viewDoc.CreateElement("Name");
                    addLocName.InnerText = "Location";
                    XmlElement addLocValue = viewDoc.CreateElement("Value");
                    addLocValue.InnerText = "View";
                    addLocProp.AppendChild(addLocName);
                    addLocProp.AppendChild(addLocValue);
                    addProps.AppendChild(addLocProp);

                    XmlElement addControlProp = viewDoc.CreateElement("Property");
                    XmlElement addControlName = viewDoc.CreateElement("Name");
                    addControlName.InnerText = "ControlID";
                    XmlElement addControlValue = viewDoc.CreateElement("Value");
                    addControlValue.InnerText = addButtonId;
                    XmlElement addControlDisplay = viewDoc.CreateElement("DisplayValue");
                    addControlDisplay.InnerText = "Add ToolBar Button";
                    XmlElement addControlNameValue = viewDoc.CreateElement("NameValue");
                    addControlNameValue.InnerText = "Add ToolBar Button";
                    addControlProp.AppendChild(addControlName);
                    addControlProp.AppendChild(addControlDisplay);
                    addControlProp.AppendChild(addControlNameValue);
                    addControlProp.AppendChild(addControlValue);
                    addProps.AppendChild(addControlProp);

                    XmlElement addViewProp = viewDoc.CreateElement("Property");
                    XmlElement addViewName = viewDoc.CreateElement("Name");
                    addViewName.InnerText = "ViewID";
                    XmlElement addViewValue = viewDoc.CreateElement("Value");
                    addViewValue.InnerText = viewGuid;
                    XmlElement addViewDisplay = viewDoc.CreateElement("DisplayValue");
                    addViewDisplay.InnerText = viewName;
                    XmlElement addViewNameValue = viewDoc.CreateElement("NameValue");
                    addViewNameValue.InnerText = viewName;
                    addViewProp.AppendChild(addViewName);
                    addViewProp.AppendChild(addViewDisplay);
                    addViewProp.AppendChild(addViewNameValue);
                    addViewProp.AppendChild(addViewValue);
                    addProps.AppendChild(addViewProp);

                    hideAddAction.AppendChild(addProps);

                    XmlElement addParams = viewDoc.CreateElement("Parameters");
                    XmlElement addParam = viewDoc.CreateElement("Parameter");
                    addParam.SetAttribute("SourceType", "Value");
                    addParam.SetAttribute("TargetID", "isvisible");
                    addParam.SetAttribute("TargetDisplayName", "Add ToolBar Button");
                    addParam.SetAttribute("TargetType", "ControlProperty");
                    XmlElement addSourceValue = viewDoc.CreateElement("SourceValue");
                    addSourceValue.SetAttribute("xml:space", "preserve");
                    addSourceValue.InnerText = "false";
                    addParam.AppendChild(addSourceValue);
                    addParams.AppendChild(addParam);
                    hideAddAction.AppendChild(addParams);

                    actions.AppendChild(hideAddAction);

                    Console.WriteLine($"        Added Transfer actions to hide Add and Delete toolbar buttons");
                    return;
                }
            }
        }

        // Add this new method specifically for list views to ensure proper parameter addition
        private void AddIDParameterToListView(XmlDocument viewDoc)
        {
            // Find the View element
            XmlElement viewElement = null;

            // The generated view should have View as a top-level element or under SourceCode.Forms/Views
            XmlNodeList viewNodes = viewDoc.GetElementsByTagName("View");
            if (viewNodes.Count > 0)
            {
                viewElement = (XmlElement)viewNodes[0];
            }

            if (viewElement == null)
            {
                Console.WriteLine("        WARNING: No View element found in document");
                return;
            }

            // Check if Parameters section already exists
            XmlElement parametersElement = null;
            foreach (XmlNode child in viewElement.ChildNodes)
            {
                if (child.NodeType == XmlNodeType.Element && child.Name == "Parameters")
                {
                    parametersElement = (XmlElement)child;
                    Console.WriteLine("        Found existing Parameters section");
                    break;
                }
            }

            if (parametersElement == null)
            {
                // Create Parameters element if it doesn't exist
                parametersElement = viewDoc.CreateElement("Parameters");

                // Find the right insertion point
                // Parameters should come after Events/Expressions but before Description/DisplayName
                XmlNode insertBefore = null;

                // Try to find Description or DisplayName
                foreach (XmlNode child in viewElement.ChildNodes)
                {
                    if (child.NodeType == XmlNodeType.Element &&
                        (child.Name == "Description" || child.Name == "DisplayName"))
                    {
                        insertBefore = child;
                        break;
                    }
                }

                if (insertBefore != null)
                {
                    viewElement.InsertBefore(parametersElement, insertBefore);
                }
                else
                {
                    viewElement.AppendChild(parametersElement);
                }

                Console.WriteLine("        Created new Parameters section");
            }

            // Check if ID parameter already exists
            bool hasIdParameter = false;
            foreach (XmlNode param in parametersElement.GetElementsByTagName("Parameter"))
            {
                if (param is XmlElement paramElement)
                {
                    XmlNodeList nameNodes = paramElement.GetElementsByTagName("Name");
                    if (nameNodes.Count > 0 && nameNodes[0].InnerText == "ID")
                    {
                        hasIdParameter = true;
                        Console.WriteLine("        ID parameter already exists");
                        break;
                    }
                }
            }

            // Add ID parameter if it doesn't exist
            if (!hasIdParameter)
            {
                XmlElement idParameter = viewDoc.CreateElement("Parameter");
                string parameterGuid = Guid.NewGuid().ToString();
                idParameter.SetAttribute("ID", parameterGuid);
                idParameter.SetAttribute("DataType", "Text");

                XmlElement nameElement = viewDoc.CreateElement("Name");
                nameElement.InnerText = "ID";
                idParameter.AppendChild(nameElement);

                // Add DefaultValue element (empty for list views)
                XmlElement defaultValueElement = viewDoc.CreateElement("DefaultValue");
                defaultValueElement.InnerText = "";
                idParameter.AppendChild(defaultValueElement);

                parametersElement.AppendChild(idParameter);

                Console.WriteLine($"        Added ID parameter with GUID: {parameterGuid}");
            }
        }

        private void DisableFilteringInEditableList(XmlDocument doc, string viewName)
        {
            Console.WriteLine($"        Configuring editable list properties for view: {viewName}");

            XmlNodeList viewControls = doc.GetElementsByTagName("Control");
            XmlElement viewControl = null;

            foreach (XmlElement control in viewControls)
            {
                if (control.GetAttribute("Type") == "View")
                {
                    viewControl = control;
                    break;
                }
            }

            if (viewControl != null)
            {
                XmlElement propertiesElement = null;
                foreach (XmlNode child in viewControl.ChildNodes)
                {
                    if (child.NodeType == XmlNodeType.Element && child.Name == "Properties")
                    {
                        propertiesElement = (XmlElement)child;
                        break;
                    }
                }

                if (propertiesElement == null)
                {
                    propertiesElement = doc.CreateElement("Properties");
                    viewControl.AppendChild(propertiesElement);
                }

                // Remove ShowAddRow property completely
                RemovePropertyIfExists(propertiesElement, "ShowAddRow");

                // Set other properties
                SetOrUpdateProperty(doc, propertiesElement, "FilterDisplay", "false");
                SetOrUpdateProperty(doc, propertiesElement, "MultiSelectAllowed", "true");
                SetOrUpdateProperty(doc, propertiesElement, "CellContentSelectAllowed", "false");

                // Log verification
                Console.WriteLine($"            ShowAddRow property removed");
                Console.WriteLine($"            FilterDisplay set to false");
                Console.WriteLine($"            MultiSelectAllowed set to true");
                Console.WriteLine($"            CellContentSelectAllowed set to false");
            }
        }

        private void RemovePropertyIfExists(XmlElement propertiesElement, string propertyName)
        {
            XmlNodeList propertyNodes = propertiesElement.GetElementsByTagName("Property");
            XmlNode nodeToRemove = null;

            foreach (XmlNode propNode in propertyNodes)
            {
                if (propNode.NodeType == XmlNodeType.Element)
                {
                    XmlElement prop = (XmlElement)propNode;
                    XmlNodeList nameNodes = prop.GetElementsByTagName("Name");
                    if (nameNodes.Count > 0 && nameNodes[0].InnerText == propertyName)
                    {
                        nodeToRemove = propNode;
                        break;
                    }
                }
            }

            if (nodeToRemove != null)
            {
                propertiesElement.RemoveChild(nodeToRemove);
            }
        }

        // Add this verification method to ensure the parameter persists
        private void VerifyIDParameter(XmlDocument viewDoc)
        {
            bool hasIdParameter = false;
            XmlNodeList paramNodes = viewDoc.SelectNodes("//Parameters/Parameter");

            foreach (XmlNode param in paramNodes)
            {
                XmlNodeList nameNodes = param.SelectNodes("Name");
                if (nameNodes.Count > 0 && nameNodes[0].InnerText == "ID")
                {
                    hasIdParameter = true;
                    Console.WriteLine("        Verified: ID parameter is present in view XML");
                    break;
                }
            }

            if (!hasIdParameter)
            {
                Console.WriteLine("        WARNING: ID parameter not found during verification!");
                // Try to add it one more time
                AddIDParameterToListView(viewDoc);
            }
        }
       
        private void RegisterEditableListControls(XmlDocument viewDoc, string viewName, string smoName, List<string> visibleFields)
        {
            Dictionary<string, ControlMapping> editableControlMappings = new Dictionary<string, ControlMapping>();

            Console.WriteLine($"        Extracting editable controls from list view XML for: {viewName}");

            // Find all controls in the view
            XmlNodeList allControls = viewDoc.GetElementsByTagName("Control");

            // Track which fields we've found controls for
            HashSet<string> foundFields = new HashSet<string>();

            // In K2 editable lists, the edit row controls often have specific patterns
            // They're usually in a special row marked for editing

            foreach (XmlElement control in allControls)
            {
                string controlType = control.GetAttribute("Type");
                string controlId = control.GetAttribute("ID");

                // Skip non-editable control types
                if (!IsEditableControlType(controlType))
                    continue;

                // Get the control's name
                string controlName = "";
                XmlNodeList nameNodes = control.GetElementsByTagName("Name");
                if (nameNodes.Count > 0)
                {
                    controlName = nameNodes[0].InnerText;
                }

                // Get the display name which might be different
                string displayName = "";
                XmlNodeList displayNameNodes = control.GetElementsByTagName("DisplayName");
                if (displayNameNodes.Count > 0)
                {
                    displayName = displayNameNodes[0].InnerText;
                }

                // Check if this control has a Field property binding
                string boundFieldName = null;
                XmlNodeList properties = control.GetElementsByTagName("Property");
                foreach (XmlElement prop in properties)
                {
                    XmlNodeList propNameNodes = prop.GetElementsByTagName("Name");
                    if (propNameNodes.Count > 0 && propNameNodes[0].InnerText == "Field")
                    {
                        // This control is bound to a field
                        XmlNodeList fieldDisplayNodes = prop.GetElementsByTagName("DisplayValue");
                        if (fieldDisplayNodes.Count > 0)
                        {
                            boundFieldName = fieldDisplayNodes[0].InnerText.Replace(" ", "").Replace("_", "");
                        }
                        break;
                    }
                }

                // Try to match this control to one of our visible fields
                string matchedFieldName = null;

                if (!string.IsNullOrEmpty(boundFieldName))
                {
                    // First try exact match with bound field
                    foreach (string field in visibleFields)
                    {
                        string sanitizedField = NameSanitizer.SanitizePropertyName(field);
                        if (boundFieldName.Equals(sanitizedField, StringComparison.OrdinalIgnoreCase) ||
                            boundFieldName.Equals(field, StringComparison.OrdinalIgnoreCase))
                        {
                            matchedFieldName = field;
                            break;
                        }
                    }
                }

                // If no match yet, try matching by control name patterns
                if (string.IsNullOrEmpty(matchedFieldName))
                {
                    // K2 often names list edit controls like: "DEPARTURELOCATION Text Box"
                    // or just "DEPARTURELOCATION"
                    foreach (string field in visibleFields)
                    {
                        string fieldUpper = field.ToUpper();
                        string fieldNoUnderscore = field.Replace("_", "");

                        // Check various naming patterns
                        if (controlName.StartsWith(field, StringComparison.OrdinalIgnoreCase) ||
                            controlName.StartsWith(fieldUpper, StringComparison.OrdinalIgnoreCase) ||
                            controlName.StartsWith(fieldNoUnderscore, StringComparison.OrdinalIgnoreCase) ||
                            displayName.StartsWith(field, StringComparison.OrdinalIgnoreCase))
                        {
                            matchedFieldName = field;
                            break;
                        }
                    }
                }

                // If still no match, try more aggressive pattern matching
                if (string.IsNullOrEmpty(matchedFieldName))
                {
                    foreach (string field in visibleFields)
                    {
                        // Remove common suffixes from control name
                        string cleanedControlName = controlName;
                        string[] suffixes = { " Text Box", " TextBox", " Calendar", " Drop Down",
                                     " DropDown", " Check Box", " CheckBox" };
                        foreach (string suffix in suffixes)
                        {
                            if (cleanedControlName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                            {
                                cleanedControlName = cleanedControlName.Substring(0, cleanedControlName.Length - suffix.Length);
                                break;
                            }
                        }

                        // Compare cleaned name
                        if (cleanedControlName.Equals(field, StringComparison.OrdinalIgnoreCase) ||
                            cleanedControlName.Replace(" ", "").Equals(field.Replace("_", ""), StringComparison.OrdinalIgnoreCase))
                        {
                            matchedFieldName = field;
                            break;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(matchedFieldName))
                {
                    string sanitizedFieldName = NameSanitizer.SanitizePropertyName(matchedFieldName);

                    if (!foundFields.Contains(sanitizedFieldName))
                    {
                        editableControlMappings[sanitizedFieldName] = new ControlMapping
                        {
                            ControlId = controlId,
                            ControlName = controlName,
                            ControlType = controlType,
                            FieldName = sanitizedFieldName,
                            DataType = GetFieldDataType(smoName, sanitizedFieldName)
                        };

                        foundFields.Add(sanitizedFieldName);

                        Console.WriteLine($"            Found editable control: {controlName} (Type: {controlType}, ID: {controlId}) for field: {sanitizedFieldName}");
                    }
                }
            }

            // If we still haven't found controls, let's examine the XML structure more carefully
            if (editableControlMappings.Count == 0)
            {
                Console.WriteLine($"        No controls found with standard search. Examining view structure...");

                // Look for controls in specific row templates or sections
                XmlNodeList rows = viewDoc.GetElementsByTagName("Row");
                foreach (XmlElement row in rows)
                {
                    // Check if this is an edit row (might have specific properties)
                    XmlNodeList rowProps = row.GetElementsByTagName("Property");
                    bool isEditRow = false;

                    foreach (XmlElement prop in rowProps)
                    {
                        XmlNodeList propNames = prop.GetElementsByTagName("Name");
                        if (propNames.Count > 0)
                        {
                            string propName = propNames[0].InnerText;
                            if (propName == "Template" || propName == "RowTemplate")
                            {
                                XmlNodeList propValues = prop.GetElementsByTagName("Value");
                                if (propValues.Count > 0 &&
                                    (propValues[0].InnerText.Contains("Edit") ||
                                     propValues[0].InnerText.Contains("Add")))
                                {
                                    isEditRow = true;
                                    break;
                                }
                            }
                        }
                    }

                    if (isEditRow)
                    {
                        Console.WriteLine($"            Found edit/add row in list view");
                        // Process controls in this row
                        XmlNodeList rowControls = row.GetElementsByTagName("Control");
                        foreach (XmlElement control in rowControls)
                        {
                            ProcessControlForMapping(control, visibleFields, smoName,
                                                   editableControlMappings, foundFields);
                        }
                    }
                }
            }

            // Final fallback: If K2 generated the controls with the exact field names
            if (editableControlMappings.Count < visibleFields.Count)
            {
                Console.WriteLine($"        Attempting final fallback search for missing fields...");

                foreach (string field in visibleFields)
                {
                    string sanitizedField = NameSanitizer.SanitizePropertyName(field);

                    if (foundFields.Contains(sanitizedField))
                        continue;

                    // Look for any control with ID that might be for this field
                    foreach (XmlElement control in allControls)
                    {
                        string controlType = control.GetAttribute("Type");

                        if (!IsEditableControlType(controlType))
                            continue;

                        // Check if this control is already mapped
                        bool alreadyMapped = false;
                        foreach (var mapping in editableControlMappings.Values)
                        {
                            if (mapping.ControlId == control.GetAttribute("ID"))
                            {
                                alreadyMapped = true;
                                break;
                            }
                        }

                        if (!alreadyMapped)
                        {
                            // This unmapped control might be for our field
                            // Add it tentatively
                            string controlId = control.GetAttribute("ID");
                            string controlName = "Unknown_" + field;

                            XmlNodeList nameNodes = control.GetElementsByTagName("Name");
                            if (nameNodes.Count > 0)
                            {
                                controlName = nameNodes[0].InnerText;
                            }

                            editableControlMappings[sanitizedField] = new ControlMapping
                            {
                                ControlId = controlId,
                                ControlName = controlName,
                                ControlType = controlType,
                                FieldName = sanitizedField,
                                DataType = GetFieldDataType(smoName, sanitizedField)
                            };

                            foundFields.Add(sanitizedField);
                            Console.WriteLine($"            Tentatively mapped control: {controlName} (Type: {controlType}, ID: {controlId}) to field: {sanitizedField}");
                            break;
                        }
                    }
                }
            }

            if (editableControlMappings.Count > 0)
            {
                ControlMappingService.RegisterViewControls(viewName, editableControlMappings);
                Console.WriteLine($"        Registered {editableControlMappings.Count} editable control mappings for list view: {viewName}");

                if (editableControlMappings.Count < visibleFields.Count)
                {
                    Console.WriteLine($"        WARNING: Only found {editableControlMappings.Count} of {visibleFields.Count} expected fields");

                    // Log which fields are missing
                    foreach (string field in visibleFields)
                    {
                        string sanitized = NameSanitizer.SanitizePropertyName(field);
                        if (!editableControlMappings.ContainsKey(sanitized))
                        {
                            Console.WriteLine($"            Missing control for field: {field}");
                        }
                    }
                }
            }
            else
            {
                Console.WriteLine($"        WARNING: No editable controls found in list view!");
                Console.WriteLine($"        This may be because the list view is not properly configured for editing.");
            }
        }

        private void ProcessControlForMapping(XmlElement control, List<string> visibleFields,
                                     string smoName, Dictionary<string, ControlMapping> mappings,
                                     HashSet<string> foundFields)
        {
            string controlType = control.GetAttribute("Type");
            string controlId = control.GetAttribute("ID");

            if (!IsEditableControlType(controlType))
                return;

            string controlName = "";
            XmlNodeList nameNodes = control.GetElementsByTagName("Name");
            if (nameNodes.Count > 0)
            {
                controlName = nameNodes[0].InnerText;
            }

            // Try to match to a field
            foreach (string field in visibleFields)
            {
                string sanitizedField = NameSanitizer.SanitizePropertyName(field);

                if (foundFields.Contains(sanitizedField))
                    continue;

                // Check if this control matches the field
                if (controlName.Contains(field) ||
                    controlName.Contains(field.Replace("_", "")) ||
                    controlName.Contains(field.ToUpper()))
                {
                    mappings[sanitizedField] = new ControlMapping
                    {
                        ControlId = controlId,
                        ControlName = controlName,
                        ControlType = controlType,
                        FieldName = sanitizedField,
                        DataType = GetFieldDataType(smoName, sanitizedField)
                    };

                    foundFields.Add(sanitizedField);
                    Console.WriteLine($"                Mapped control in edit row: {controlName} to field: {sanitizedField}");
                    break;
                }
            }
        }
        private bool IsEditableControlType(string controlType)
        {
            string[] editableTypes = {
        "TextBox", "TextArea", "Calendar", "CheckBox",
        "DropDown", "AutoComplete", "RadioButtonList",
        "CheckBoxList", "Picker", "FilePostBack",
        "ImagePostBack", "SharePointHyperLink", "HTMLEditor"
    };

            return editableTypes.Contains(controlType);
        }

    
    

        private void RegisterListViewFieldMappings(string viewName, string smoName, List<string> visibleFields)
        {
            // This method is now deprecated - we don't register column definitions
            // The actual editable controls are registered in RegisterEditableListControls instead
            Console.WriteLine($"        Skipping column definition registration - will register editable controls instead");
        }





        private string GetFieldDataType(string smoName, string fieldName)
        {
            if (_smoFieldMappings != null && _smoFieldMappings.ContainsKey(smoName))
            {
                var fields = _smoFieldMappings[smoName];
                if (fields.ContainsKey(fieldName) || fields.ContainsKey(fieldName.ToUpper()))
                {
                    var field = fields.ContainsKey(fieldName) ? fields[fieldName] : fields[fieldName.ToUpper()];
                    return field.DataType;
                }
            }
            return "Text"; // Default
        }


       
        private void HideEditableListButtons(XmlDocument doc, string[] buttonNamesToHide)
        {
            Console.WriteLine($"        Hiding buttons: {string.Join(", ", buttonNamesToHide)}");

            XmlNodeList allControls = doc.GetElementsByTagName("Control");

            foreach (XmlElement control in allControls)
            {
                if (control.GetAttribute("Type") == "ToolBarButton")
                {
                    string controlName = "";
                    XmlNodeList nameNodes = control.GetElementsByTagName("Name");
                    if (nameNodes.Count > 0)
                    {
                        controlName = nameNodes[0].InnerText;
                    }

                    // Check if this is a button we want to hide
                    bool shouldHide = false;
                    foreach (string buttonName in buttonNamesToHide)
                    {
                        if (controlName.Contains(buttonName))
                        {
                            shouldHide = true;
                            break;
                        }
                    }

                    if (shouldHide)
                    {
                        XmlElement propertiesElement = null;
                        foreach (XmlNode child in control.ChildNodes)
                        {
                            if (child.NodeType == XmlNodeType.Element && child.Name == "Properties")
                            {
                                propertiesElement = (XmlElement)child;
                                break;
                            }
                        }

                        if (propertiesElement == null)
                        {
                            propertiesElement = doc.CreateElement("Properties");
                            control.AppendChild(propertiesElement);
                        }

                        // Set IsVisible to false
                        SetOrUpdateProperty(doc, propertiesElement, "IsVisible", "false");

                        Console.WriteLine($"            Hidden button: {controlName}");
                    }
                }
            }
        }
        private void FindEditableListButtons(XmlDocument doc, ref string addButtonId, ref string deleteButtonId)
        {
            XmlNodeList allControls = doc.GetElementsByTagName("Control");

            foreach (XmlElement control in allControls)
            {
                if (control.GetAttribute("Type") == "ToolBarButton")
                {
                    string controlName = "";
                    XmlNodeList nameNodes = control.GetElementsByTagName("Name");
                    if (nameNodes.Count > 0)
                    {
                        controlName = nameNodes[0].InnerText;
                    }

                    if (controlName.Contains("Add") && !controlName.Contains("Edit"))
                    {
                        addButtonId = control.GetAttribute("ID");
                        Console.WriteLine($"        Found Add button: {controlName} (ID: {addButtonId})");
                    }
                    else if (controlName.Contains("Delete"))
                    {
                        deleteButtonId = control.GetAttribute("ID");
                        Console.WriteLine($"        Found Delete button: {controlName} (ID: {deleteButtonId})");
                    }
                }
            }
        }

        private void RemoveButtonRules(XmlDocument doc, string addButtonId, string deleteButtonId)
        {
            if (string.IsNullOrEmpty(addButtonId) && string.IsNullOrEmpty(deleteButtonId))
                return;

            Console.WriteLine($"        Removing default rules for Add and Delete buttons");

            XmlNodeList eventsNodes = doc.GetElementsByTagName("Events");
            if (eventsNodes.Count == 0)
                return;

            XmlElement events = (XmlElement)eventsNodes[0];
            List<XmlNode> eventsToRemove = new List<XmlNode>();

            foreach (XmlNode eventNode in events.ChildNodes)
            {
                if (eventNode.NodeType == XmlNodeType.Element && eventNode.Name == "Event")
                {
                    XmlElement eventElement = (XmlElement)eventNode;
                    string sourceId = eventElement.GetAttribute("SourceID");

                    if (sourceId == addButtonId || sourceId == deleteButtonId)
                    {
                        eventsToRemove.Add(eventNode);
                        Console.WriteLine($"            Removing rule for button ID: {sourceId}");
                    }
                }
            }

            foreach (XmlNode eventNode in eventsToRemove)
            {
                events.RemoveChild(eventNode);
            }
        }



        private void SetOrUpdateProperty(XmlDocument doc, XmlElement propertiesElement, string propertyName, string propertyValue)
        {
            XmlElement existingProperty = null;
            foreach (XmlNode propNode in propertiesElement.ChildNodes)
            {
                if (propNode.NodeType == XmlNodeType.Element && propNode.Name == "Property")
                {
                    XmlElement prop = (XmlElement)propNode;
                    XmlNodeList nameNodes = prop.GetElementsByTagName("Name");
                    if (nameNodes.Count > 0 && nameNodes[0].InnerText == propertyName)
                    {
                        existingProperty = prop;
                        break;
                    }
                }
            }

            if (existingProperty != null)
            {
                UpdatePropertyValue(doc, existingProperty, propertyValue);
            }
            else
            {
                XmlElement newProperty = CreateProperty(doc, propertyName, propertyValue);
                propertiesElement.AppendChild(newProperty);
            }
        }


        private XmlElement CreateProperty(XmlDocument doc, string name, string value)
        {
            XmlElement property = doc.CreateElement("Property");

            XmlElement nameElement = doc.CreateElement("Name");
            nameElement.InnerText = name;
            property.AppendChild(nameElement);

            XmlElement valueElement = doc.CreateElement("Value");
            valueElement.InnerText = value;
            property.AppendChild(valueElement);

            XmlElement displayValueElement = doc.CreateElement("DisplayValue");
            displayValueElement.InnerText = value;
            property.AppendChild(displayValueElement);

            if (name != "FilterDisplay" && name != "IsVisible")
            {
                XmlElement nameValueElement = doc.CreateElement("NameValue");
                nameValueElement.InnerText = value;
                property.AppendChild(nameValueElement);
            }

            return property;
        }

        private void UpdatePropertyValue(XmlDocument doc, XmlElement property, string newValue)
        {
            // Update Value element
            XmlNodeList valueNodes = property.GetElementsByTagName("Value");
            if (valueNodes.Count > 0)
            {
                valueNodes[0].InnerText = newValue;
            }
            else
            {
                XmlElement valueElement = doc.CreateElement("Value");
                valueElement.InnerText = newValue;
                property.AppendChild(valueElement);
            }

            // Update DisplayValue element
            XmlNodeList displayValueNodes = property.GetElementsByTagName("DisplayValue");
            if (displayValueNodes.Count > 0)
            {
                displayValueNodes[0].InnerText = newValue;
            }
            else
            {
                XmlElement displayValueElement = doc.CreateElement("DisplayValue");
                displayValueElement.InnerText = newValue;
                property.AppendChild(displayValueElement);
            }

            // Update NameValue element if it exists or should exist
            XmlNodeList nameValueNodes = property.GetElementsByTagName("NameValue");
            if (nameValueNodes.Count > 0)
            {
                nameValueNodes[0].InnerText = newValue;
            }
            else if (property.FirstChild != null && property.FirstChild.Name == "Name")
            {
                string propName = property.FirstChild.InnerText;
                // Add NameValue for properties that typically have it
                if (propName != "FilterDisplay" && propName != "IsVisible" && propName != "AlternateRows")
                {
                    XmlElement nameValueElement = doc.CreateElement("NameValue");
                    nameValueElement.InnerText = newValue;
                    property.AppendChild(nameValueElement);
                }
            }
        }

        private void AddWrapTextToAllHeaderLabels(XmlDocument doc, List<string> displayProperties)
        {
            Console.WriteLine($"        Adding WrapText to header labels for {displayProperties.Count} columns");

            // Find all Control elements
            XmlNodeList allControls = doc.GetElementsByTagName("Control");
            int labelsModified = 0;

            foreach (XmlElement control in allControls)
            {
                // Check if this is a Label control
                if (control.GetAttribute("Type") != "Label")
                    continue;

                // Get the control's name to check if it's a header label
                string labelName = "";
                XmlNodeList nameNodes = control.GetElementsByTagName("Name");
                if (nameNodes.Count > 0)
                {
                    labelName = nameNodes[0].InnerText;
                }

                // Check if this label is a header for one of our display properties
                bool isHeaderLabel = false;
                foreach (string prop in displayProperties)
                {
                    // K2 names header labels as "[FieldName] Label" or similar patterns
                    string propNoUnderscore = prop.Replace("_", " ");

                    if (labelName.EndsWith(" Label") &&
                        (labelName.StartsWith(prop + " ") ||
                         labelName.StartsWith(propNoUnderscore + " ") ||
                         labelName.Equals(prop + " Label", StringComparison.OrdinalIgnoreCase) ||
                         labelName.Equals(propNoUnderscore + " Label", StringComparison.OrdinalIgnoreCase)))
                    {
                        isHeaderLabel = true;
                        break;
                    }
                }

                if (!isHeaderLabel)
                    continue;

                Console.WriteLine($"            Found header label: {labelName}");

                // Find the Properties element - it should be a direct child
                XmlElement propertiesElement = null;
                foreach (XmlNode child in control.ChildNodes)
                {
                    if (child.NodeType == XmlNodeType.Element && child.Name == "Properties")
                    {
                        propertiesElement = (XmlElement)child;
                        break;
                    }
                }

                // Create Properties element if it doesn't exist
                if (propertiesElement == null)
                {
                    propertiesElement = doc.CreateElement("Properties");

                    // Find where to insert Properties (should be after Styles but before Name)
                    XmlNode insertBefore = null;
                    foreach (XmlNode child in control.ChildNodes)
                    {
                        if (child.Name == "Name" || child.Name == "DisplayName" || child.Name == "NameValue")
                        {
                            insertBefore = child;
                            break;
                        }
                    }

                    if (insertBefore != null)
                    {
                        control.InsertBefore(propertiesElement, insertBefore);
                    }
                    else
                    {
                        control.AppendChild(propertiesElement);
                    }
                }

                // Check if WrapText property already exists
                bool hasWrapText = false;
                XmlElement wrapTextProperty = null;

                foreach (XmlNode propNode in propertiesElement.ChildNodes)
                {
                    if (propNode.NodeType == XmlNodeType.Element && propNode.Name == "Property")
                    {
                        XmlElement prop = (XmlElement)propNode;
                        XmlNodeList propNameNodes = prop.GetElementsByTagName("Name");
                        if (propNameNodes.Count > 0 && propNameNodes[0].InnerText == "WrapText")
                        {
                            hasWrapText = true;
                            wrapTextProperty = prop;
                            break;
                        }
                    }
                }

                if (hasWrapText && wrapTextProperty != null)
                {
                    // Update existing WrapText property
                    XmlNodeList valueNodes = wrapTextProperty.GetElementsByTagName("Value");
                    XmlNodeList displayValueNodes = wrapTextProperty.GetElementsByTagName("DisplayValue");

                    if (valueNodes.Count > 0)
                        valueNodes[0].InnerText = "true";
                    else
                    {
                        XmlElement valueElement = doc.CreateElement("Value");
                        valueElement.InnerText = "true";
                        wrapTextProperty.AppendChild(valueElement);
                    }

                    if (displayValueNodes.Count > 0)
                        displayValueNodes[0].InnerText = "true";
                    else
                    {
                        XmlElement displayValueElement = doc.CreateElement("DisplayValue");
                        displayValueElement.InnerText = "true";
                        wrapTextProperty.AppendChild(displayValueElement);
                    }

                    Console.WriteLine($"            Updated existing WrapText property for: {labelName}");
                }
                else
                {
                    // Add new WrapText property
                    XmlElement wrapTextProp = doc.CreateElement("Property");

                    XmlElement propName = doc.CreateElement("Name");
                    propName.InnerText = "WrapText";
                    wrapTextProp.AppendChild(propName);

                    XmlElement propValue = doc.CreateElement("Value");
                    propValue.InnerText = "true";
                    wrapTextProp.AppendChild(propValue);

                    XmlElement propDisplayValue = doc.CreateElement("DisplayValue");
                    propDisplayValue.InnerText = "true";
                    wrapTextProp.AppendChild(propDisplayValue);

                    propertiesElement.AppendChild(wrapTextProp);

                    labelsModified++;
                    Console.WriteLine($"            Added WrapText property to: {labelName}");
                }
            }

            Console.WriteLine($"        Modified {labelsModified} header labels with WrapText=true");
        }


      

      


        private string ExtractRepeatingSectionName(string viewName)
        {
            // Extract the repeating section name from view names
            // Examples:
            // "Travel_Request_Trips_List" -> "Trips"
            // "Travel_Request_view1_Trips_Item" -> "Trips"

            if (viewName.Contains("_List"))
            {
                // Find the part before "_List"
                int listIndex = viewName.LastIndexOf("_List");
                if (listIndex > 0)
                {
                    string beforeList = viewName.Substring(0, listIndex);
                    int lastUnderscore = beforeList.LastIndexOf('_');
                    if (lastUnderscore >= 0 && lastUnderscore < beforeList.Length - 1)
                    {
                        return beforeList.Substring(lastUnderscore + 1);
                    }
                }
            }
            else if (viewName.Contains("_Item"))
            {
                // Find the part before "_Item"
                int itemIndex = viewName.LastIndexOf("_Item");
                if (itemIndex > 0)
                {
                    string beforeItem = viewName.Substring(0, itemIndex);
                    int lastUnderscore = beforeItem.LastIndexOf('_');
                    if (lastUnderscore >= 0 && lastUnderscore < beforeItem.Length - 1)
                    {
                        return beforeItem.Substring(lastUnderscore + 1);
                    }
                }
            }

            return null;
        }


        private void AddParentIdFilter(XmlDocument viewDoc, string viewName)
        {
            // Find the Events section
            XmlNodeList eventsNodes = viewDoc.GetElementsByTagName("Events");
            if (eventsNodes.Count == 0)
            {
                Console.WriteLine("        WARNING: No Events section found to add filter");
                return;
            }

            XmlElement events = (XmlElement)eventsNodes[0];

            // Find the ParentID field GUID from the Sources section
            string parentIdFieldGuid = null;
            string sourceGuid = null;

            // Get the primary source and find ParentID field
            XmlNodeList sources = viewDoc.SelectNodes("//Sources/Source[@ContextType='Primary']");
            if (sources.Count > 0)
            {
                XmlElement source = (XmlElement)sources[0];
                sourceGuid = source.GetAttribute("ID");

                // Find ParentID field
                XmlNodeList fields = source.GetElementsByTagName("Field");
                foreach (XmlElement field in fields)
                {
                    XmlNodeList fieldNameNodes = field.GetElementsByTagName("FieldName");
                    XmlNodeList nameNodes = field.GetElementsByTagName("Name");

                    if ((fieldNameNodes.Count > 0 && fieldNameNodes[0].InnerText == "ParentID") ||
                        (nameNodes.Count > 0 && nameNodes[0].InnerText == "Parent ID"))
                    {
                        parentIdFieldGuid = field.GetAttribute("ID");
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(parentIdFieldGuid))
            {
                Console.WriteLine("        WARNING: Could not find Parent ID field GUID for filter");
                return;
            }

            // Find the Init event with GetList action
            XmlNodeList eventNodes = events.GetElementsByTagName("Event");
            foreach (XmlElement eventElement in eventNodes)
            {
                XmlNodeList nameNodes = eventElement.GetElementsByTagName("Name");
                if (nameNodes.Count > 0 && nameNodes[0].InnerText == "Init")
                {
                    // Find the GetList action in this event
                    XmlNodeList actions = eventElement.GetElementsByTagName("Action");
                    foreach (XmlElement action in actions)
                    {
                        // Check if this is a GetList action
                        XmlNodeList methodProps = action.SelectNodes(".//Property[Name='Method']");
                        bool isGetListAction = false;

                        foreach (XmlElement methodProp in methodProps)
                        {
                            XmlNodeList valueNodes = methodProp.GetElementsByTagName("Value");
                            if (valueNodes.Count > 0 && valueNodes[0].InnerText == "GetList")
                            {
                                isGetListAction = true;
                                break;
                            }
                        }

                        if (isGetListAction)
                        {
                            // Check if Filter property already exists
                            XmlNodeList filterProps = action.SelectNodes(".//Property[Name='Filter']");
                            if (filterProps.Count > 0)
                            {
                                Console.WriteLine("        Filter property already exists, skipping");
                                return;
                            }

                            // Add new Filter property
                            XmlNodeList propsNodes = action.GetElementsByTagName("Properties");
                            if (propsNodes.Count > 0)
                            {
                                XmlElement props = (XmlElement)propsNodes[0];

                                // Create the Filter property
                                XmlElement filterProp = viewDoc.CreateElement("Property");

                                XmlElement nameElement = viewDoc.CreateElement("Name");
                                nameElement.InnerText = "Filter";
                                filterProp.AppendChild(nameElement);

                                // Create the filter XML structure using actual field GUID
                                string filterXml = $@"<Filter isSimple=""True""><Equals>" +
                                    $@"<Item SourceType=""ViewField"" SourceID=""{parentIdFieldGuid}"" DataType=""number"" " +
                                    $@"Name=""{sourceGuid}_{parentIdFieldGuid}"" SourceName=""Parent ID"" SourceDisplayName=""Parent ID"">Parent ID</Item>" +
                                    @"<Item SourceType=""Value""><SourceValue>" +
                                    @"<Item SourceType=""ViewParameter"" DataType=""Text"" SourceID=""ID"" " +
                                    @"SourceName=""ID"" SourceDisplayName=""ID"" SourceSubFormID=""00000000-0000-0000-0000-000000000000"">ID</Item>" +
                                    @"</SourceValue></Item></Equals></Filter>";

                                XmlElement valueElement = viewDoc.CreateElement("Value");
                                valueElement.InnerText = filterXml;
                                filterProp.AppendChild(valueElement);

                                props.AppendChild(filterProp);
                                Console.WriteLine("        Added ParentID = ID filter to GetList action");
                            }
                            return; // We found and processed the GetList action
                        }
                    }
                }
            }
        }


        private void DeleteExistingView(FormsManager formsManager, string viewName)
        {
            try
            {
                var viewInfo = formsManager.GetViewDefinition(viewName);
                if (viewInfo != null)
                {
                    formsManager.DeleteView(viewName);
                    Console.WriteLine($"    Deleted existing view: {viewName}");

                    // REMOVE FROM REGISTRY IF EXISTS
                    if (SmartObjectViewRegistry.ViewExists(viewName))
                    {
                        var smoName = SmartObjectViewRegistry.GetSmartObjectForView(viewName);
                        if (!string.IsNullOrEmpty(smoName))
                        {
                            // This doesn't remove the SmartObject, just the view registration
                            var allViews = SmartObjectViewRegistry.GetViewsForSmartObject(smoName);
                            // Note: You might want to add a RemoveView method to the registry
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    Note: Could not delete view {viewName}: {ex.Message}");
            }
        }
   

public bool TryCleanupExistingViews(string jsonContent)
        {
            bool allCleaned = true;

            JObject formData = JObject.Parse(jsonContent);
            string formName = NameSanitizer.SanitizeSmartObjectName(formData.Properties().First().Name);
            JObject formDefinition = formData[formData.Properties().First().Name] as JObject;

            // Get all view names
            JArray viewsArray = formDefinition["FormDefinition"]["Views"] as JArray;
            if (viewsArray != null)
            {
                using (FormsManager formsManager = new FormsManager())
                {
                    try
                    {
                        formsManager.CreateConnection();
                        formsManager.Connection.Open(_connectionManager.ConnectionString.ConnectionString);

                        foreach (JObject viewDef in viewsArray)
                        {
                            string viewName = viewDef["ViewName"]?.Value<string>()?.Replace(".xsl", "");
                            if (!string.IsNullOrEmpty(viewName))
                            {
                                // Clean up main view and any part views
                                string fullViewName = $"{formName}_{viewName}";
                                try
                                {
                                    formsManager.DeleteView(fullViewName);
                                    Console.WriteLine($"  Cleaned up view: {fullViewName}");
                                }
                                catch (Exception ex)
                                {
                                    if (!ex.Message.Contains("does not exist"))
                                    {
                                        Console.WriteLine($"  Could not delete view {fullViewName}: {ex.Message}");
                                        allCleaned = false;
                                    }
                                }

                                // Try to clean up part views
                                for (int i = 1; i <= 10; i++)
                                {
                                    string partViewName = $"{formName}_{viewName}_Part{i}";
                                    try
                                    {
                                        formsManager.DeleteView(partViewName);
                                        Console.WriteLine($"  Cleaned up view: {partViewName}");
                                    }
                                    catch
                                    {
                                        // Stop trying after first missing part
                                        break;
                                    }
                                }

                                // Get repeating sections from this view's controls
                                JArray viewControls = viewDef["Controls"] as JArray;
                                if (viewControls != null)
                                {
                                    HashSet<string> viewRepeatingSections = new HashSet<string>();

                                    foreach (JObject control in viewControls)
                                    {
                                        JObject repeatingSectionInfo = control["RepeatingSectionInfo"] as JObject;
                                        if (repeatingSectionInfo != null)
                                        {
                                            bool isInRepeating = repeatingSectionInfo["IsInRepeatingSection"]?.Value<bool>() ?? false;
                                            string sectionName = repeatingSectionInfo["RepeatingSectionName"]?.Value<string>();

                                            if (isInRepeating && !string.IsNullOrEmpty(sectionName))
                                            {
                                                viewRepeatingSections.Add(sectionName);
                                            }
                                        }
                                    }



                                    // Clean up view-specific item and list views
                                    foreach (string section in viewRepeatingSections)
                                    {
                                        // FIX: Keep spaces in view names but use underscores for SmartObject references
                                        // Delete Item view
                                        string itemViewName = $"{formName}_{viewName}_{section}_Item";
                                        try
                                        {
                                            formsManager.DeleteView(itemViewName);
                                            Console.WriteLine($"  Cleaned up view: {itemViewName}");
                                        }
                                        catch (Exception ex)
                                        {
                                            if (!ex.Message.Contains("does not exist"))
                                            {
                                                Console.WriteLine($"  Could not delete view {itemViewName}: {ex.Message}");
                                                allCleaned = false;
                                            }
                                        }

                                        // Delete List view (now view-specific)
                                        string listViewName = $"{formName}_{viewName}_{section}_List";
                                        try
                                        {
                                            formsManager.DeleteView(listViewName);
                                            Console.WriteLine($"  Cleaned up view: {listViewName}");
                                        }
                                        catch (Exception ex)
                                        {
                                            if (!ex.Message.Contains("does not exist"))
                                            {
                                                Console.WriteLine($"  Could not delete view {listViewName}: {ex.Message}");
                                                allCleaned = false;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  Warning during view cleanup: {ex.Message}");
                        allCleaned = false;
                    }
                }
            }

            return allCleaned;
        }
        // Helper classes

        public class ControlMapping
        {
            public string ControlId { get; set; }
            public string ControlName { get; set; }
            public string ControlType { get; set; }
            public string FieldName { get; set; }
            public string DataType { get; set; }
        }
        private class ViewSegment
        {
            public SegmentType Type { get; set; }
            public List<JObject> Controls { get; set; }
            public string SectionName { get; set; }
            public JObject TitleLabel { get; set; }
        }

        private enum SegmentType
        {
            Regular,
            RepeatingSection
        }
    }
}