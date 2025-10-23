using K2SmartObjectGenerator.Utilities;
using Newtonsoft.Json.Linq;
using SourceCode.Forms.Authoring;
using SourceCode.Forms.Management;
using SourceCode.Forms.Utilities;
using SourceCode.SmartObjects.Authoring;
using SourceCode.SmartObjects.Management;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using static K2SmartObjectGenerator.ViewGenerator;
using XmlHelper = K2SmartObjectGenerator.Utilities.XmlHelper;

namespace K2SmartObjectGenerator
{


    public class FormGenerator
    {
        private readonly ServerConnectionManager _connectionManager;
        private readonly Dictionary<string, List<string>> _formViewMappings;
        private readonly string _formTheme;
        private Dictionary<string, string> _viewTitles;
        private readonly FormRulesBuilder _rulesBuilder;
        private readonly SmartObjectGenerator _smoGenerator;

        private JObject _originalJsonData;

        // NEW: Add field to store repeating section positions
        private Dictionary<string, Dictionary<string, RepeatingSectionPosition>> _repeatingSectionPositions;

        // NEW: Add field to store view grid positions for ordering
        private Dictionary<string, int> _viewGridPositions;


        public FormGenerator(ServerConnectionManager connectionManager, string formTheme = "Lithium", SmartObjectGenerator smoGenerator = null)
        {
            _connectionManager = connectionManager;
            _formViewMappings = new Dictionary<string, List<string>>();
            _formTheme = formTheme;
            _rulesBuilder = new FormRulesBuilder();
            _smoGenerator = smoGenerator;
            _repeatingSectionPositions = new Dictionary<string, Dictionary<string, RepeatingSectionPosition>>();
            _viewGridPositions = new Dictionary<string, int>();
        }



        public void GenerateFormsFromJson(string jsonContent, Dictionary<string, string> viewTitles = null)
        {
            _viewTitles = viewTitles ?? new Dictionary<string, string>();

            JObject formData = JObject.Parse(jsonContent);

            // NEW: Store the original JSON data
            _originalJsonData = formData;

            string rootName = formData.Properties().First().Name;
            string baseFormName = rootName.Replace(" ", "_");
            string baseFormDisplayName = rootName; // Keep original name with spaces for display
            JObject formDefinition = formData[rootName] as JObject;

            JObject formDefObject = formDefinition?["FormDefinition"] as JObject;
            JArray viewsArray = formDefObject?["Views"] as JArray;

            if (viewsArray == null || viewsArray.Count == 0)
            {
                Console.WriteLine("No views found in JSON to create forms from");
                return;
            }

            // NEW: Extract repeating section positions for all views
            ExtractAllRepeatingSectionPositions(viewsArray, baseFormName);

            // NEW: Extract view grid positions for ordering
            ExtractViewGridPositions(viewsArray, baseFormName);

            // Define base category path for forms - use display name with spaces
            // NEW STRUCTURE: forms go directly under {formName}
            string formCategory = baseFormDisplayName;

            // Track which views have been mapped to forms
            Dictionary<string, FormDefinition> infopathViewToForm = new Dictionary<string, FormDefinition>();

            // Process each InfoPath view to determine which K2 views belong to it
            foreach (JObject viewDef in viewsArray)
            {
                string viewName = viewDef["ViewName"]?.Value<string>()?.Replace(".xsl", "");
                if (string.IsNullOrEmpty(viewName))
                {
                    Console.WriteLine("    WARNING: Found view with empty name, skipping form generation...");
                    continue;
                }

                Console.WriteLine($"\n=== Preparing Form for InfoPath View: {viewName} ===");

                // Create a form definition for this InfoPath view
                FormDefinition formDef = new FormDefinition
                {
                    InfoPathViewName = viewName,
                    FormName = $"{baseFormName}_{viewName}_Form",
                    ViewNames = new List<string>(),
                    CategoryPath = formCategory,
                    IsListForm = false
                };

                // Map the K2 views that were created for this InfoPath view
                MapViewsToForm(formDef, baseFormName, viewName, viewDef);

                if (formDef.ViewNames.Count > 0)
                {
                    infopathViewToForm[viewName] = formDef;
                }
                else
                {
                    Console.WriteLine($"    WARNING: No K2 views found for InfoPath view {viewName}");
                }
            }

            // Generate all forms
            foreach (var kvp in infopathViewToForm)
            {
                GenerateForm(kvp.Value);
            }

            Console.WriteLine($"\n=== Form Generation Complete ===");
            Console.WriteLine($"    Total forms created: {infopathViewToForm.Count}");
        }

        private void ExtractAllRepeatingSectionPositions(JArray viewsArray, string baseFormName)
        {
            foreach (JObject viewDef in viewsArray)
            {
                string viewName = viewDef["ViewName"]?.Value<string>()?.Replace(".xsl", "");
                if (string.IsNullOrEmpty(viewName))
                    continue;

                var positions = new Dictionary<string, RepeatingSectionPosition>();
                JArray controls = viewDef["Controls"] as JArray;

                if (controls != null)
                {
                    foreach (JObject control in controls)
                    {
                        string type = control["Type"]?.Value<string>();
                        string controlName = control["Name"]?.Value<string>();
                        string gridPosition = control["GridPosition"]?.Value<string>();

                        // Check if it's a repeating table or if it's in a repeating section
                        if (type == "RepeatingTable")
                        {
                            // Get the display name or use the control name
                            string displayName = control["DisplayName"]?.Value<string>() ?? controlName;

                            if (!string.IsNullOrEmpty(displayName) && !string.IsNullOrEmpty(gridPosition))
                            {
                                int row = ExtractRowFromGridPosition(gridPosition);
                                positions[displayName] = new RepeatingSectionPosition
                                {
                                    GridRow = row,
                                    GridPosition = gridPosition,
                                    OriginalName = controlName,
                                    DisplayName = displayName
                                };

                                Console.WriteLine($"    Found repeating section '{displayName}' at position {gridPosition} (row {row})");
                            }
                        }

                        // Also check for controls that are IN repeating sections
                        JObject repeatingSectionInfo = control["RepeatingSectionInfo"] as JObject;
                        if (repeatingSectionInfo != null)
                        {
                            bool isInRepeating = repeatingSectionInfo["IsInRepeatingSection"]?.Value<bool>() ?? false;
                            string sectionName = repeatingSectionInfo["RepeatingSectionName"]?.Value<string>();

                            if (isInRepeating && !string.IsNullOrEmpty(sectionName) && !positions.ContainsKey(sectionName))
                            {
                                // This is the first control in this repeating section, use its position
                                if (!string.IsNullOrEmpty(gridPosition))
                                {
                                    int row = ExtractRowFromGridPosition(gridPosition);
                                    positions[sectionName] = new RepeatingSectionPosition
                                    {
                                        GridRow = row,
                                        GridPosition = gridPosition,
                                        OriginalName = sectionName,
                                        DisplayName = sectionName
                                    };

                                    Console.WriteLine($"    Found repeating section '{sectionName}' starting at position {gridPosition} (row {row})");
                                }
                            }
                        }
                    }
                }

                // Store positions for this view
                string fullViewName = $"{baseFormName}_{viewName}";
                _repeatingSectionPositions[fullViewName] = positions;
            }
        }

        private void ExtractViewGridPositions(JArray viewsArray, string baseFormName)
        {
            foreach (JObject viewDef in viewsArray)
            {
                string viewName = viewDef["ViewName"]?.Value<string>()?.Replace(".xsl", "");
                if (string.IsNullOrEmpty(viewName))
                    continue;

                Console.WriteLine($"    Extracting grid positions for view: {viewName}");

                // Get sections array to find repeating section positions
                JArray sections = viewDef["Sections"] as JArray;
                if (sections != null)
                {
                    foreach (JObject section in sections)
                    {
                        string sectionName = section["Name"]?.Value<string>();
                        string sectionType = section["Type"]?.Value<string>();
                        int startRow = section["StartRow"]?.Value<int>() ?? 0;

                        if (sectionType == "repeating" && !string.IsNullOrEmpty(sectionName))
                        {
                            // Map repeating section views to their start row for ordering
                            string listViewName = $"{baseFormName}_{sectionName}_List";
                            string itemViewName = $"{baseFormName}_{sectionName}_Item";

                            _viewGridPositions[listViewName] = startRow;
                            _viewGridPositions[itemViewName] = startRow; // Item view at same position as List view for repeating sections

                            Console.WriteLine($"      Mapped {listViewName} and {itemViewName} to grid row {startRow}");
                        }
                    }
                }

                // For Part views, determine position based on content
                if (viewName.Contains("_Part"))
                {
                    // Analyze the controls in this Part view to determine grid position
                    int partPosition = DeterminePartViewPosition(viewDef);
                    string fullViewName = $"{baseFormName}_{viewName}";
                    _viewGridPositions[fullViewName] = partPosition;

                    Console.WriteLine($"      Mapped {fullViewName} to grid row {partPosition}");
                }
                else if (!viewName.Contains("_List") && !viewName.Contains("_Item"))
                {
                    // For regular views (main view), determine position from first control
                    int mainPosition = DeterminePartViewPosition(viewDef);
                    string fullViewName = $"{baseFormName}_{viewName}";
                    _viewGridPositions[fullViewName] = mainPosition;

                    Console.WriteLine($"      Mapped {fullViewName} to grid row {mainPosition}");
                }

                // IMPORTANT: Also extract positions for controls that appear between repeating sections
                // This helps us understand the structure better
                ExtractControlPositionsBetweenSections(viewDef, sections, baseFormName, viewName);
            }
        }

        private int DeterminePartViewPosition(JObject viewDef)
        {
            JArray controls = viewDef["Controls"] as JArray;
            if (controls == null) return 0;

            int minRow = int.MaxValue;
            foreach (JObject control in controls)
            {
                string gridPosition = control["GridPosition"]?.Value<string>();
                if (!string.IsNullOrEmpty(gridPosition))
                {
                    int row = ExtractRowFromGridPosition(gridPosition);
                    if (row < minRow) minRow = row;
                }
            }
            return minRow == int.MaxValue ? 0 : minRow;
        }

        private void ExtractControlPositionsBetweenSections(JObject viewDef, JArray sections, string baseFormName, string viewName)
        {
            // Get all repeating section boundaries
            var sectionBoundaries = new List<(int start, int end, string name)>();
            if (sections != null)
            {
                foreach (JObject section in sections)
                {
                    if (section["Type"]?.Value<string>() == "repeating")
                    {
                        int start = section["StartRow"]?.Value<int>() ?? 0;
                        int end = section["EndRow"]?.Value<int>() ?? 0;
                        string name = section["Name"]?.Value<string>();
                        if (!string.IsNullOrEmpty(name))
                        {
                            sectionBoundaries.Add((start, end, name));
                        }
                    }
                }
            }

            // Analyze controls to find those that fall between or after repeating sections
            JArray controls = viewDef["Controls"] as JArray;
            if (controls != null)
            {
                foreach (JObject control in controls)
                {
                    string gridPosition = control["GridPosition"]?.Value<string>();
                    if (string.IsNullOrEmpty(gridPosition)) continue;

                    int controlRow = ExtractRowFromGridPosition(gridPosition);
                    string controlName = control["Name"]?.Value<string>();

                    // Check if this control is NOT in a repeating section
                    JObject repeatingSectionInfo = control["RepeatingSectionInfo"] as JObject;
                    bool isInRepeating = repeatingSectionInfo?["IsInRepeatingSection"]?.Value<bool>() ?? false;

                    if (!isInRepeating && !string.IsNullOrEmpty(controlName))
                    {
                        // This control is outside repeating sections - it might need its own Part view positioning
                        Console.WriteLine($"      Found control '{controlName}' outside repeating sections at row {controlRow}");

                        // Find which repeating section this control comes after
                        var precedingSection = sectionBoundaries
                            .Where(s => s.end < controlRow)
                            .OrderByDescending(s => s.end)
                            .FirstOrDefault();

                        if (precedingSection.name != null)
                        {
                            Console.WriteLine($"        Control '{controlName}' comes after section '{precedingSection.name}' (ends at row {precedingSection.end})");
                        }
                    }
                }
            }
        }

        private List<ViewWithGridPosition> CreateGridPositionOrderedViewList(
            List<OrderedView> orderedViews,
            Dictionary<string, XmlElement> listViews,
            Dictionary<string, XmlElement> itemViews,
            FormDefinition formDef)
        {
            var allViewsWithPositions = new List<ViewWithGridPosition>();

            Console.WriteLine($"    Creating grid position ordered view list...");
            Console.WriteLine($"    Available grid positions:");
            foreach (var kvp in _viewGridPositions)
            {
                Console.WriteLine($"      '{kvp.Key}' -> {kvp.Value}");
            }

            // Add regular views with their grid positions
            foreach (var orderedView in orderedViews)
            {
                int gridPosition = _viewGridPositions.ContainsKey(orderedView.ViewName)
                    ? _viewGridPositions[orderedView.ViewName]
                    : orderedView.Position * 1000; // Fallback to original position with high offset

                allViewsWithPositions.Add(new ViewWithGridPosition
                {
                    View = orderedView.View,
                    ViewName = orderedView.ViewName,
                    GridPosition = gridPosition,
                    IsRepeatingSection = false,
                    SectionName = null
                });

                Console.WriteLine($"      Regular view {orderedView.ViewName} mapped to grid position {gridPosition}");
            }

            // Add repeating section views with their grid positions
            foreach (var kvp in listViews)
            {
                string sectionName = kvp.Key;

                // Extract base form name from formDef.FormName (remove InfoPath view suffix)
                Console.WriteLine($"      formDef.FormName = '{formDef.FormName}'");
                string[] parts = formDef.FormName.Split('_');
                string baseFormName = parts.Length >= 2 ? $"{parts[0]}_{parts[1]}" : parts[0]; // Extract "Expense_Report" from "Expense_Report_view1_Form"
                string listViewName = $"{baseFormName}_{sectionName}_List";

                int gridPosition = _viewGridPositions.ContainsKey(listViewName)
                    ? _viewGridPositions[listViewName]
                    : 999999; // Place at end if position unknown

                Console.WriteLine($"      Looking for '{listViewName}' in grid positions: {(_viewGridPositions.ContainsKey(listViewName) ? "FOUND" : "NOT FOUND")}");

                allViewsWithPositions.Add(new ViewWithGridPosition
                {
                    View = null, // Will be handled specially in the main loop
                    ViewName = listViewName,
                    GridPosition = gridPosition,
                    IsRepeatingSection = true,
                    SectionName = sectionName
                });

                Console.WriteLine($"      Repeating section {sectionName} mapped to grid position {gridPosition}");
            }

            Console.WriteLine($"    Total views to order: {allViewsWithPositions.Count}");
            return allViewsWithPositions;
        }

        private int ExtractRowFromGridPosition(string gridPosition)
        {
            if (string.IsNullOrEmpty(gridPosition))
                return 999; // Default to end

            string rowPart = System.Text.RegularExpressions.Regex.Match(gridPosition, @"\d+").Value;
            if (int.TryParse(rowPart, out int row))
                return row;

            return 999; // Default to end
        }


        private void AddIDParameterToForm(XmlDocument formDoc)
        {
            // Find the Form element
            XmlElement formElement = null;

            // Try to find Form element - it could be at root or under SourceCode.Forms/Forms
            XmlNodeList formNodes = formDoc.GetElementsByTagName("Form");
            if (formNodes.Count > 0)
            {
                formElement = (XmlElement)formNodes[0];
            }

            if (formElement == null)
            {
                Console.WriteLine("        WARNING: No Form element found in document");
                return;
            }

            // Check if Parameters section already exists
            XmlElement parametersElement = null;
            foreach (XmlNode child in formElement.ChildNodes)
            {
                if (child.NodeType == XmlNodeType.Element && child.Name == "Parameters")
                {
                    parametersElement = (XmlElement)child;
                    Console.WriteLine("        Found existing Parameters section in form");
                    break;
                }
            }

            if (parametersElement == null)
            {
                // Create Parameters element if it doesn't exist
                parametersElement = formDoc.CreateElement("Parameters");

                // Find the right insertion point - Parameters should come near the end
                // typically after States but before Description/DisplayName
                XmlNode insertBefore = null;

                // Try to find Description or DisplayName
                foreach (XmlNode child in formElement.ChildNodes)
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
                    formElement.InsertBefore(parametersElement, insertBefore);
                }
                else
                {
                    formElement.AppendChild(parametersElement);
                }

                Console.WriteLine("        Created new Parameters section in form");
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
                        Console.WriteLine("        ID parameter already exists in form");
                        break;
                    }
                }
            }

            // Add ID parameter if it doesn't exist
            if (!hasIdParameter)
            {
                XmlElement idParameter = formDoc.CreateElement("Parameter");
                string parameterGuid = Guid.NewGuid().ToString();
                idParameter.SetAttribute("ID", parameterGuid);
                idParameter.SetAttribute("DataType", "Text");

                XmlElement nameElement = formDoc.CreateElement("Name");
                nameElement.InnerText = "ID";
                idParameter.AppendChild(nameElement);

                // Add DefaultValue element (empty for forms)
                XmlElement defaultValueElement = formDoc.CreateElement("DefaultValue");
                defaultValueElement.InnerText = "";
                idParameter.AppendChild(defaultValueElement);

                parametersElement.AppendChild(idParameter);

                Console.WriteLine($"        Added ID parameter to form with GUID: {parameterGuid}");
            }
        }

        private void MapViewsToForm(FormDefinition formDef, string baseFormName,
                                string infopathViewName, JObject viewDef)
        {
            // Get controls to identify sections and first label
            JArray viewControls = viewDef["Controls"] as JArray;
            if (viewControls == null || viewControls.Count == 0)
            {
                Console.WriteLine($"    No controls found for InfoPath view: {infopathViewName}");
                return;
            }

            // Find the first label for main view naming
            string firstLabelName = null;
            foreach (JObject control in viewControls)
            {
                if (control["Type"]?.Value<string>()?.ToLower() == "label" &&
                    !string.IsNullOrEmpty(control["Label"]?.Value<string>()))
                {
                    firstLabelName = control["Label"].Value<string>()
                        .Replace(" ", "_")
                        .Replace(":", "")
                        .Replace("-", "_");
                    break;
                }
            }

            // Add main view(s)
            if (!string.IsNullOrEmpty(firstLabelName))
            {
                // First segment uses the first label name
                string mainViewName = $"{baseFormName}_{firstLabelName}";
                if (CheckViewExists(mainViewName))
                {
                    formDef.ViewNames.Add(mainViewName);
                    Console.WriteLine($"    Adding main view: {mainViewName}");
                }
            }

            // Check for additional part views
            for (int i = 1; i <= 10; i++)
            {
                string partViewName = $"{baseFormName}_{infopathViewName}_Part{i}";
                if (CheckViewExists(partViewName))
                {
                    formDef.ViewNames.Add(partViewName);
                    Console.WriteLine($"    Adding part view: {partViewName}");
                }
                else if (i > 1)
                {
                    // Stop checking after first missing part (assumes sequential)
                    break;
                }
            }

            // Extract ALL repeating sections for this InfoPath view
            HashSet<string> repeatingSections = ExtractAllRepeatingSections(viewControls);

            Console.WriteLine($"    Found {repeatingSections.Count} repeating sections in InfoPath view: {infopathViewName}");
            foreach (string section in repeatingSections)
            {
                Console.WriteLine($"        - {section}");
            }

            // Add item views and list views for each repeating section
            // IMPORTANT: Create view names specific to this InfoPath view to allow multiple view pairs
            foreach (string sectionName in repeatingSections)
            {
                // View names include the InfoPath view name to ensure uniqueness
                string itemViewName = $"{baseFormName}_{infopathViewName}_{sectionName}_Item";
                string listViewName = $"{baseFormName}_{infopathViewName}_{sectionName}_List";

                // Check if item view exists and add it
                if (CheckViewExists(itemViewName))
                {
                    formDef.ViewNames.Add(itemViewName);
                    formDef.HasRepeatingSections = true;
                    Console.WriteLine($"    Adding item view: {itemViewName}");
                }
                else
                {
                    Console.WriteLine($"    WARNING: Item view not found: {itemViewName}");
                }

                // Check if list view exists and add it
                if (CheckViewExists(listViewName))
                {
                    formDef.ViewNames.Add(listViewName);
                    formDef.HasRepeatingSections = true;
                    Console.WriteLine($"    Adding list view: {listViewName}");
                }
                else
                {
                    Console.WriteLine($"    WARNING: List view not found: {listViewName}");
                }
            }

            Console.WriteLine($"    Total views mapped to form: {formDef.ViewNames.Count}");
            if (formDef.HasRepeatingSections)
            {
                Console.WriteLine($"    Form has repeating sections: Yes");
            }
        }

        private HashSet<string> ExtractAllRepeatingSections(JArray controls)
        {
            HashSet<string> sections = new HashSet<string>();

            if (controls == null || controls.Count == 0)
            {
                return sections;
            }

            foreach (JObject control in controls)
            {
                if (control == null) continue;

                // IMPORTANT: Check for RepeatingSectionName at root level first
                string sectionName = control["RepeatingSectionName"]?.Value<string>();

                // If not found at root, check RepeatingSectionInfo
                if (string.IsNullOrEmpty(sectionName))
                {
                    JObject repeatingSectionInfo = control["RepeatingSectionInfo"] as JObject;
                    if (repeatingSectionInfo != null)
                    {
                        bool isInRepeating = repeatingSectionInfo["IsInRepeatingSection"]?.Value<bool>() ?? false;
                        if (isInRepeating)
                        {
                            sectionName = repeatingSectionInfo["RepeatingSectionName"]?.Value<string>();
                        }
                    }
                }

                if (!string.IsNullOrEmpty(sectionName))
                {
                    // Normalize the section name to match what ViewGenerator creates
                    string normalizedName = NormalizeRepeatingSectionName(sectionName);

                    if (!sections.Contains(normalizedName))
                    {
                        sections.Add(normalizedName);
                        Console.WriteLine($"        Found repeating section: {sectionName} -> normalized to: {normalizedName}");
                    }
                }
            }

            return sections;
        }

        private string NormalizeRepeatingSectionName(string sectionName)
        {
            if (string.IsNullOrEmpty(sectionName))
                return sectionName;

            // Handle TABLECTRL naming convention
            if (sectionName.StartsWith("TABLECTRL", StringComparison.OrdinalIgnoreCase))
            {
                string ctrlNumber = sectionName.Substring("TABLECTRL".Length);
                return $"Table_CTRL{ctrlNumber}";
            }

            // Already normalized
            if (sectionName.StartsWith("Table_CTRL", StringComparison.OrdinalIgnoreCase))
            {
                return sectionName;
            }

            // Replace spaces with underscores for other section names
            return sectionName.Replace(" ", "_");
        }



        private Form RestructureFormToGroupListAndItemViews(Form generatedForm, FormDefinition formDef)
        {
            try
            {
                // Get the form's XML
                string formXml = generatedForm.ToXml();
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(formXml);

                // Update the Form element's Theme attribute to _Dynamic
                XmlNodeList formElements = doc.GetElementsByTagName("Form");
                if (formElements.Count > 0)
                {
                    XmlElement formElement = (XmlElement)formElements[0];
                    formElement.SetAttribute("Theme", "_Dynamic");
                    Console.WriteLine("    Updated form theme to _Dynamic");
                }

                // Update or add UseLegacyTheme property in the Form control
                UpdateFormControlLegacyTheme(doc);

                // Find all panels in the form
                XmlNodeList panels = doc.GetElementsByTagName("Panel");
                if (panels.Count == 0)
                {
                    Console.WriteLine("    No panels found to restructure");
                    return generatedForm;
                }

                // Track view pairs for rule generation
                Dictionary<string, ViewPairInfo> viewPairs = new Dictionary<string, ViewPairInfo>();

                // For each panel, reorganize the areas to group list and item views
                foreach (XmlElement panel in panels)
                {
                    XmlNodeList areas = panel.GetElementsByTagName("Area");
                    if (areas.Count == 0) continue;

                    // Categorize all views
                    Dictionary<string, XmlElement> listViews = new Dictionary<string, XmlElement>();
                    Dictionary<string, XmlElement> itemViews = new Dictionary<string, XmlElement>();
                    List<OrderedView> orderedViews = new List<OrderedView>();

                    Console.WriteLine("\n    === Analyzing Form Structure ===");

                    // Process each area and maintain order
                    int position = 0;
                    foreach (XmlElement area in areas)
                    {
                        XmlNodeList items = area.GetElementsByTagName("Item");
                        foreach (XmlElement item in items)
                        {
                            XmlNodeList nameNodes = item.GetElementsByTagName("Name");
                            if (nameNodes.Count > 0)
                            {
                                string viewName = nameNodes[0].InnerText;
                                Console.WriteLine($"      Position {position}: {viewName}");

                                // Update titles if we have them
                                string titleToUse = null;
                                if (_viewTitles != null && _viewTitles.ContainsKey(viewName))
                                {
                                    titleToUse = _viewTitles[viewName];
                                }
                                UpdateAreaItemControlTitle(doc, viewName, titleToUse);

                                // Check if it's a list or item view
                                if (viewName.Contains("_List"))
                                {
                                    string baseName = ExtractRepeatingSectionNameFromView(viewName, formDef.InfoPathViewName);
                                    if (!string.IsNullOrEmpty(baseName))
                                    {
                                        listViews[baseName] = item;
                                    }
                                }
                                else if (viewName.Contains("_Item"))
                                {
                                    string baseName = ExtractRepeatingSectionNameFromView(viewName, formDef.InfoPathViewName);
                                    if (!string.IsNullOrEmpty(baseName))
                                    {
                                        itemViews[baseName] = item;
                                    }
                                }
                                else
                                {
                                    // Regular view or Part view
                                    orderedViews.Add(new OrderedView
                                    {
                                        Position = position,
                                        View = item,
                                        ViewName = viewName,
                                        IsPartView = viewName.Contains("_Part")
                                    });
                                }

                                position++;
                            }
                        }
                    }

                    // Now rebuild the form
                    if (orderedViews.Count > 0 || listViews.Count > 0)
                    {
                        Console.WriteLine($"\n    === Rebuilding Form Structure ===");

                        // Clear all existing areas
                        ClearExistingAreas(panel);

                        // Create new area structure
                        XmlElement areasContainer = doc.CreateElement("Areas");

                        // NEW: GridPosition-based ordering strategy
                        // Combine all views (regular and repeating sections) with their grid positions
                        Console.WriteLine($"    === Using GridPosition-Based Ordering ===");

                        var allViewsWithPositions = CreateGridPositionOrderedViewList(orderedViews, listViews, itemViews, formDef);

                        // Sort all views by their grid position and add them to the form
                        foreach (var viewWithPos in allViewsWithPositions.OrderBy(v => v.GridPosition))
                        {
                            if (viewWithPos.IsRepeatingSection)
                            {
                                // Add repeating section (list + item view pair)
                                if (listViews.ContainsKey(viewWithPos.SectionName) && itemViews.ContainsKey(viewWithPos.SectionName))
                                {
                                    AddRepeatingSectionToContainer(doc, areasContainer, viewWithPos.SectionName,
                                        listViews, itemViews, formDef, viewPairs);
                                    Console.WriteLine($"      Added repeating section: {viewWithPos.SectionName} at grid position {viewWithPos.GridPosition}");

                                    // Remove from dictionaries to avoid duplication
                                    listViews.Remove(viewWithPos.SectionName);
                                    itemViews.Remove(viewWithPos.SectionName);
                                }
                            }
                            else
                            {
                                // Add regular view
                                XmlElement viewArea = doc.CreateElement("Area");
                                viewArea.SetAttribute("ID", Guid.NewGuid().ToString());
                                XmlElement viewItems = doc.CreateElement("Items");
                                XmlElement clonedItem = (XmlElement)viewWithPos.View.CloneNode(true);
                                viewItems.AppendChild(clonedItem);
                                viewArea.AppendChild(viewItems);
                                areasContainer.AppendChild(viewArea);
                                Console.WriteLine($"      Added view: {viewWithPos.ViewName} at grid position {viewWithPos.GridPosition}");
                            }
                        }

                        // Add any remaining repeating sections that weren't found in grid positions
                        foreach (var kvp in listViews.ToList())
                        {
                            if (itemViews.ContainsKey(kvp.Key))
                            {
                                AddRepeatingSectionToContainer(doc, areasContainer, kvp.Key,
                                    listViews, itemViews, formDef, viewPairs);
                                Console.WriteLine($"      Added remaining section: {kvp.Key} (no grid position found)");
                            }
                        }

                        // Add the button area with table at the end
                        Dictionary<string, string> buttonGuids;
                        XmlElement buttonArea = CreateButtonArea(doc, formDef.FormName, out buttonGuids);
                        areasContainer.AppendChild(buttonArea);
                        Console.WriteLine("      Added Form Action Table at the end");

                        // Replace the panel's areas with the new structure
                        panel.AppendChild(areasContainer);

                        // Add button controls to the form
                        if (buttonGuids.Count > 0)
                        {
                            AddButtonControlsToForm(doc, formDef.FormName, buttonGuids);
                        }
                    }
                }

                // Continue with the rest of the method (rules, etc.)
                CompleteFormRestructuring(doc, formDef, viewPairs);

                // Create a new form object from the modified XML
                Form modifiedForm = new Form(doc.OuterXml);
                Console.WriteLine("    Form restructuring completed successfully");
                return modifiedForm;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    WARNING: Could not restructure form: {ex.Message}");
                Console.WriteLine($"    Stack trace: {ex.StackTrace}");
                return generatedForm;
            }
        }

        private void AddRepeatingSectionToContainer(XmlDocument doc, XmlElement areasContainer,
            string sectionName, Dictionary<string, XmlElement> listViews,
            Dictionary<string, XmlElement> itemViews, FormDefinition formDef,
            Dictionary<string, ViewPairInfo> viewPairs)
        {
            if (listViews.ContainsKey(sectionName) && itemViews.ContainsKey(sectionName))
            {
                var result = CreateRepeatingSectionArea(
                    doc,
                    listViews[sectionName],
                    itemViews[sectionName],
                    sectionName,
                    formDef.InfoPathViewName,
                    viewPairs
                );

                areasContainer.AppendChild(result.Area);

                // Update the view that should be hidden (returned by CreateRepeatingSectionArea)
                UpdateItemViewVisibility(doc, result.ItemViewName);

                // For nested sections, also hide the list view
                if (!string.IsNullOrEmpty(result.ListViewName))
                {
                    UpdateItemViewVisibility(doc, result.ListViewName);
                    Console.WriteLine($"        Set both nested section views to invisible: {result.ItemViewName} and {result.ListViewName}");
                }

                Console.WriteLine($"      Added repeating section: {sectionName}");
            }
        }
        private void UpdateFormControlLegacyTheme(XmlDocument doc)
        {
            XmlNodeList formControls = doc.GetElementsByTagName("Control");
            foreach (XmlElement control in formControls)
            {
                if (control.GetAttribute("Type") == "Form")
                {
                    XmlNodeList propsNodes = control.GetElementsByTagName("Properties");
                    XmlElement propertiesElement = null;

                    if (propsNodes.Count == 0)
                    {
                        propertiesElement = doc.CreateElement("Properties");
                        control.AppendChild(propertiesElement);
                    }
                    else
                    {
                        propertiesElement = (XmlElement)propsNodes[0];
                    }

                    // Check if UseLegacyTheme property exists
                    bool hasLegacyThemeProperty = false;
                    XmlNodeList properties = propertiesElement.GetElementsByTagName("Property");

                    foreach (XmlElement prop in properties)
                    {
                        XmlNodeList propNameNodes = prop.GetElementsByTagName("Name");
                        if (propNameNodes.Count > 0 && propNameNodes[0].InnerText == "UseLegacyTheme")
                        {
                            hasLegacyThemeProperty = true;
                            SetPropertyValue(doc, prop, "false");
                            Console.WriteLine("    Updated UseLegacyTheme to false");
                            break;
                        }
                    }

                    // If no UseLegacyTheme property exists, add one
                    if (!hasLegacyThemeProperty)
                    {
                        XmlElement legacyThemeProp = doc.CreateElement("Property");

                        XmlElement propName = doc.CreateElement("Name");
                        propName.InnerText = "UseLegacyTheme";
                        legacyThemeProp.AppendChild(propName);

                        XmlElement propValue = doc.CreateElement("Value");
                        propValue.InnerText = "false";
                        legacyThemeProp.AppendChild(propValue);

                        XmlElement propDisplayValue = doc.CreateElement("DisplayValue");
                        propDisplayValue.InnerText = "false";
                        legacyThemeProp.AppendChild(propDisplayValue);

                        XmlElement propNameValue = doc.CreateElement("NameValue");
                        propNameValue.InnerText = "false";
                        legacyThemeProp.AppendChild(propNameValue);

                        propertiesElement.AppendChild(legacyThemeProp);
                        Console.WriteLine("    Added UseLegacyTheme=false property to Form control");
                    }
                    break; // Only process the first Form control
                }
            }
        }

        private void ClearExistingAreas(XmlElement panel)
        {
            XmlNodeList existingAreas = panel.GetElementsByTagName("Area");
            List<XmlNode> areasToRemove = new List<XmlNode>();
            foreach (XmlNode area in existingAreas)
            {
                areasToRemove.Add(area);
            }
            foreach (XmlNode area in areasToRemove)
            {
                area.ParentNode.RemoveChild(area);
            }
        }
        private RepeatingSectionAreaResult CreateRepeatingSectionArea(
            XmlDocument doc,
            XmlElement listView,
            XmlElement itemView,
            string sectionName,
            string infopathViewName,
            Dictionary<string, ViewPairInfo> viewPairs)
        {
            XmlElement sectionArea = doc.CreateElement("Area");
            string areaGuid = Guid.NewGuid().ToString();
            sectionArea.SetAttribute("ID", areaGuid);

            XmlElement sectionItems = doc.CreateElement("Items");

            // Determine if this is a top-level repeating section (no parent)
            bool isTopLevel = IsTopLevelRepeatingSection(sectionName, infopathViewName);

            if (isTopLevel)
            {
                // For top-level sections: Item view first (visible), List view second (hidden)
                XmlElement itemItem = (XmlElement)itemView.CloneNode(true);
                sectionItems.AppendChild(itemItem);

                XmlElement listItem = (XmlElement)listView.CloneNode(true);
                sectionItems.AppendChild(listItem);

                sectionArea.AppendChild(sectionItems);

                string listViewName = listView.GetElementsByTagName("Name")[0].InnerText;
                string itemViewName = itemView.GetElementsByTagName("Name")[0].InnerText;

                // Extract view IDs and instance IDs
                string listViewId = listItem.GetAttribute("ViewID");
                if (string.IsNullOrEmpty(listViewId))
                    listViewId = listItem.GetAttribute("ID");

                string itemViewId = itemItem.GetAttribute("ViewID");
                if (string.IsNullOrEmpty(itemViewId))
                    itemViewId = itemItem.GetAttribute("ID");

                // Instance IDs are the IDs used in the form context
                string listInstanceId = listItem.GetAttribute("ID");
                string itemInstanceId = itemItem.GetAttribute("ID");

                // Track view pair for rule generation
                viewPairs[sectionName] = new ViewPairInfo
                {
                    ListViewName = listViewName,
                    ItemViewName = itemViewName,
                    AreaGuid = areaGuid,
                    ListViewId = listViewId,
                    ItemViewId = itemViewId,
                    ListViewInstanceId = listInstanceId,
                    ItemViewInstanceId = itemInstanceId,
                    SectionName = sectionName,
                    IsTopLevel = true  // Add this flag to ViewPairInfo
                };

                Console.WriteLine($"        Grouped {itemViewName} (visible) with {listViewName} (hidden) - Top Level Section");

                return new RepeatingSectionAreaResult
                {
                    Area = sectionArea,
                    ItemViewName = listViewName  // Return list view name to be hidden
                };
            }
            else
            {
                // For nested sections: Both views initially invisible
                XmlElement listItem = (XmlElement)listView.CloneNode(true);
                sectionItems.AppendChild(listItem);

                XmlElement itemItem = (XmlElement)itemView.CloneNode(true);
                sectionItems.AppendChild(itemItem);

                sectionArea.AppendChild(sectionItems);

                string listViewName = listView.GetElementsByTagName("Name")[0].InnerText;
                string itemViewName = itemView.GetElementsByTagName("Name")[0].InnerText;

                // Extract view IDs and instance IDs
                string listViewId = listItem.GetAttribute("ViewID");
                if (string.IsNullOrEmpty(listViewId))
                    listViewId = listItem.GetAttribute("ID");

                string itemViewId = itemItem.GetAttribute("ViewID");
                if (string.IsNullOrEmpty(itemViewId))
                    itemViewId = itemItem.GetAttribute("ID");

                // Instance IDs are the IDs used in the form context
                string listInstanceId = listItem.GetAttribute("ID");
                string itemInstanceId = itemItem.GetAttribute("ID");

                // Track view pair for rule generation
                viewPairs[sectionName] = new ViewPairInfo
                {
                    ListViewName = listViewName,
                    ItemViewName = itemViewName,
                    AreaGuid = areaGuid,
                    ListViewId = listViewId,
                    ItemViewId = itemViewId,
                    ListViewInstanceId = listInstanceId,
                    ItemViewInstanceId = itemInstanceId,
                    SectionName = sectionName,
                    IsTopLevel = false
                };

                Console.WriteLine($"        Grouped {listViewName} (both invisible) with {itemViewName} (both invisible) - Nested Section");

                return new RepeatingSectionAreaResult
                {
                    Area = sectionArea,
                    ItemViewName = itemViewName,  // Return item view name to be hidden
                    ListViewName = listViewName   // Also return list view name to be hidden
                };
            }
        }

        private bool IsTopLevelRepeatingSection(string sectionName, string infopathViewName)
        {
            // Check the original JSON data to determine if this section has a parent
            if (_originalJsonData == null)
                return true; // Default to top-level if we can't determine

            string rootName = _originalJsonData.Properties().First().Name;
            JObject formDefinition = _originalJsonData[rootName] as JObject;
            JObject formDefObject = formDefinition?["FormDefinition"] as JObject;
            JArray viewsArray = formDefObject?["Views"] as JArray;

            if (viewsArray != null)
            {
                foreach (JObject viewDef in viewsArray)
                {
                    string viewName = viewDef["ViewName"]?.Value<string>()?.Replace(".xsl", "");
                    if (viewName == infopathViewName)
                    {
                        JArray controls = viewDef["Controls"] as JArray;
                        if (controls != null)
                        {
                            foreach (JObject control in controls)
                            {
                                JObject repeatingSectionInfo = control["RepeatingSectionInfo"] as JObject;
                                if (repeatingSectionInfo != null)
                                {
                                    string repSectionName = repeatingSectionInfo["RepeatingSectionName"]?.Value<string>();
                                    string normalizedName = NormalizeRepeatingSectionName(repSectionName);

                                    if (normalizedName == sectionName)
                                    {
                                        string parentSection = repeatingSectionInfo["ParentRepeatingSectionName"]?.Value<string>();
                                        return string.IsNullOrEmpty(parentSection);
                                    }
                                }
                            }
                        }
                        break;
                    }
                }
            }

            return true; // Default to top-level if not found
        }



        // NEW: Helper method to update item view visibility
        private void UpdateItemViewVisibility(XmlDocument doc, string itemViewName)
        {
            XmlNodeList allControls = doc.GetElementsByTagName("Control");

            foreach (XmlElement control in allControls)
            {
                if (control.GetAttribute("Type") == "AreaItem")
                {
                    XmlNodeList nameNodes = control.GetElementsByTagName("Name");
                    if (nameNodes.Count > 0 && nameNodes[0].InnerText == itemViewName)
                    {
                        Console.WriteLine($"      Found AreaItem control for: {itemViewName}, setting IsVisible=false");

                        // Find or create Properties element
                        XmlNodeList propsNodes = control.GetElementsByTagName("Properties");
                        XmlElement propertiesElement = null;

                        if (propsNodes.Count == 0)
                        {
                            propertiesElement = doc.CreateElement("Properties");
                            XmlNodeList styleNodes = control.GetElementsByTagName("Styles");
                            if (styleNodes.Count > 0)
                            {
                                control.InsertBefore(propertiesElement, styleNodes[0]);
                            }
                            else
                            {
                                control.AppendChild(propertiesElement);
                            }
                        }
                        else
                        {
                            propertiesElement = (XmlElement)propsNodes[0];
                        }

                        // Check if IsVisible property already exists
                        bool hasVisibilityProperty = false;
                        XmlNodeList properties = propertiesElement.GetElementsByTagName("Property");

                        foreach (XmlElement prop in properties)
                        {
                            XmlNodeList propNameNodes = prop.GetElementsByTagName("Name");
                            if (propNameNodes.Count > 0 && propNameNodes[0].InnerText == "IsVisible")
                            {
                                hasVisibilityProperty = true;
                                SetPropertyValue(doc, prop, "false");
                                break;
                            }
                        }

                        // If no visibility property exists, add one
                        if (!hasVisibilityProperty)
                        {
                            XmlElement visibilityProp = doc.CreateElement("Property");

                            XmlElement propName = doc.CreateElement("Name");
                            propName.InnerText = "IsVisible";
                            visibilityProp.AppendChild(propName);

                            XmlElement propValue = doc.CreateElement("Value");
                            propValue.InnerText = "false";
                            visibilityProp.AppendChild(propValue);

                            XmlElement propDisplayValue = doc.CreateElement("DisplayValue");
                            propDisplayValue.InnerText = "false";
                            visibilityProp.AppendChild(propDisplayValue);

                            XmlElement propNameValue = doc.CreateElement("NameValue");
                            propNameValue.InnerText = "false";
                            visibilityProp.AppendChild(propNameValue);

                            propertiesElement.AppendChild(visibilityProp);
                            Console.WriteLine($"        Added IsVisible=false property to AreaItem control: {itemViewName}");
                        }
                        break;
                    }
                }
            }
        }

        // NEW: Helper method to complete form restructuring
        private void CompleteFormRestructuring(XmlDocument doc, FormDefinition formDef, Dictionary<string, ViewPairInfo> viewPairs)
        {
            // Add rules for list/item view interactions if we have view pairs
            if (viewPairs.Count > 0)
            {
                Console.WriteLine("\n    === Adding Form Rules for List/Item View Interactions ===");
                _rulesBuilder.ApplyListItemViewRules(doc, viewPairs);
            }

            // ADD FORM-LEVEL RULES (Clear and Submit buttons)
            Console.WriteLine("\n    === Adding Form-Level Rules (Clear & Submit) ===");

            // Get form ID and name from the document
            string formId = null;
            string formName = formDef.FormName;

            XmlNodeList formElems = doc.GetElementsByTagName("Form");
            if (formElems.Count > 0)
            {
                XmlElement formElement = (XmlElement)formElems[0];
                formId = formElement.GetAttribute("ID");

                // If no ID exists, generate one
                if (string.IsNullOrEmpty(formId))
                {
                    formId = Guid.NewGuid().ToString();
                    formElement.SetAttribute("ID", formId);
                    Console.WriteLine($"      Generated new Form ID: {formId}");
                }
                else
                {
                    Console.WriteLine($"      Found existing Form ID: {formId}");
                }
            }

            // Call the AddFormLevelRules method to add Clear and Submit button rules
            if (!string.IsNullOrEmpty(formId))
            {
                _rulesBuilder.AddFormLevelRules(doc, formId, formName, viewPairs);
                Console.WriteLine("      Form-level rules added successfully");
            }
            else
            {
                Console.WriteLine("      WARNING: Could not determine Form ID for rule generation");
            }

            // Update Init rule with ID parameter condition
            AddInitRuleIDParameterCondition(doc);

            AddLoadActionToInitRule(doc, formDef.FormName);

            // Verify the form has an ID parameter
            VerifyFormIDParameter(doc);

            // ADD CALCULATION RULES for dynamic expressions after all views and rules are fully integrated
            Console.WriteLine("\n    === Adding Calculation Rules (After All Views and Rules Added) ===");
            _rulesBuilder.ApplyCalculationRules(doc, _originalJsonData);
        }


        private void AddInitialHideFormActionTableHandler(XmlDocument doc,
         string areaItemId, string areaItemName)
        {
            if (string.IsNullOrEmpty(areaItemId))
            {
                Console.WriteLine("      WARNING: Cannot add initial hide handler - Form Action Table not found");
                return;
            }

            Console.WriteLine("      Adding initial handler to hide Form Action Table");

            // Find the form initialization event
            XmlNodeList formEvents = doc.SelectNodes("//Event[@SourceType='Form']");

            foreach (XmlElement formEvent in formEvents)
            {
                XmlElement handlersContainer = formEvent.SelectSingleNode("Handlers") as XmlElement;
                if (handlersContainer == null) continue;

                // Create a new handler that runs FIRST (before any conditionals)
                XmlElement hideHandler = doc.CreateElement("Handler");
                hideHandler.SetAttribute("ID", Guid.NewGuid().ToString());
                hideHandler.SetAttribute("DefinitionID", Guid.NewGuid().ToString());

                XmlElement props = doc.CreateElement("Properties");
                XmlElement handlerNameProp = doc.CreateElement("Property");
                XmlHelper.AddElement(doc, handlerNameProp, "Name", "HandlerName");
                XmlHelper.AddElement(doc, handlerNameProp, "Value", "then");
                props.AppendChild(handlerNameProp);

                XmlElement locationProp = doc.CreateElement("Property");
                XmlHelper.AddElement(doc, locationProp, "Name", "Location");
                XmlHelper.AddElement(doc, locationProp, "Value", "form");
                props.AppendChild(locationProp);

                hideHandler.AppendChild(props);

                // Add action to hide Form Action Table
                XmlElement actions = doc.CreateElement("Actions");
                XmlElement hideAction = CreateFormActionTableVisibilityAction(doc,
                    areaItemId, areaItemName, false);
                actions.AppendChild(hideAction);
                hideHandler.AppendChild(actions);

                // Insert as FIRST handler (before all others)
                if (handlersContainer.FirstChild != null)
                {
                    handlersContainer.InsertBefore(hideHandler, handlersContainer.FirstChild);
                }
                else
                {
                    handlersContainer.AppendChild(hideHandler);
                }

                Console.WriteLine("        ✓ Added initial handler to hide Form Action Table");
                return; // Only add to first form event
            }
        }
        private void AddFormActionTableVisibilityAction(XmlDocument doc, XmlElement actionsContainer,
    string areaItemId, string areaItemName, bool show)
        {
            if (string.IsNullOrEmpty(areaItemId))
            {
                Console.WriteLine("        WARNING: Cannot add Form Action Table visibility - ID not found");
                return;
            }

            XmlElement visibilityAction = CreateFormActionTableVisibilityAction(doc,
                areaItemId, areaItemName, show);

            // Insert at the beginning of actions (before ListRefresh)
            if (actionsContainer.FirstChild != null)
            {
                actionsContainer.InsertBefore(visibilityAction, actionsContainer.FirstChild);
            }
            else
            {
                actionsContainer.AppendChild(visibilityAction);
            }

            Console.WriteLine($"        ✓ Added {(show ? "Show" : "Hide")} Form Action Table action");
        }

        private XmlElement CreateFormActionTableVisibilityAction(XmlDocument doc,
         string areaItemId, string areaItemName, bool show)
        {
            string visibility = show ? "Show" : "Hide";
            Console.WriteLine($"        Creating {visibility} Form Action Table action");

            XmlElement action = doc.CreateElement("Action");
            action.SetAttribute("ID", Guid.NewGuid().ToString());
            action.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            action.SetAttribute("Type", "Transfer");
            action.SetAttribute("ExecutionType", "Synchronous");

            XmlElement props = doc.CreateElement("Properties");

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", "Form");
            props.AppendChild(locationProp);

            action.AppendChild(props);

            XmlElement parameters = doc.CreateElement("Parameters");
            XmlElement parameter = doc.CreateElement("Parameter");
            parameter.SetAttribute("SourceType", "Value");
            parameter.SetAttribute("TargetID", areaItemId);
            parameter.SetAttribute("TargetName", "IsVisible");
            parameter.SetAttribute("TargetDisplayName", "IsVisible");
            parameter.SetAttribute("TargetType", "ControlProperty");

            XmlElement sourceValue = doc.CreateElement("SourceValue");
            sourceValue.SetAttribute("xml:space", "preserve");
            sourceValue.InnerText = show ? "true" : "false";
            parameter.AppendChild(sourceValue);

            parameters.AppendChild(parameter);
            action.AppendChild(parameters);

            return action;
        }




        private void AddInitRuleIDParameterCondition(XmlDocument doc)
        {
            Console.WriteLine("\n    === Adding ID Parameter Condition to Init Rule ===");

            // Find the Form Action Table control ID
            string formActionTableId = null;
            string formActionTableName = "Form Action Table";

            XmlNodeList tableControls = doc.SelectNodes("//Control[@Type='Table']");
            foreach (XmlElement control in tableControls)
            {
                XmlNode nameNode = control.SelectSingleNode("Name");
                if (nameNode != null && nameNode.InnerText.Contains("Form Action Table"))
                {
                    formActionTableId = control.GetAttribute("ID");
                    formActionTableName = nameNode.InnerText;
                    Console.WriteLine($"      Found Form Action Table: {formActionTableName} (ID: {formActionTableId})");
                    break;
                }
            }

            if (string.IsNullOrEmpty(formActionTableId))
            {
                Console.WriteLine("      WARNING: Form Action Table control not found");
                return;
            }

            // K2 AutoGenerator doesn't create an Init event - it creates form events with the form name
            // Look for form events that contain ListRefresh actions
            XmlNodeList allEvents = doc.SelectNodes("//Event[@SourceType='Form']");

            Console.WriteLine($"      Found {allEvents.Count} Form events");

            foreach (XmlElement formEvent in allEvents)
            {
                string eventSourceName = formEvent.GetAttribute("SourceName");
                Console.WriteLine($"      Checking Form event with SourceName: {eventSourceName}");

                XmlElement handlersContainer = formEvent.SelectSingleNode("Handlers") as XmlElement;
                if (handlersContainer == null)
                {
                    Console.WriteLine("        No handlers found");
                    continue;
                }

                XmlNodeList handlers = handlersContainer.SelectNodes("Handler");
                Console.WriteLine($"        Found {handlers.Count} handlers");

                int handlerIndex = 0;
                foreach (XmlElement handler in handlers)
                {
                    handlerIndex++;
                    XmlElement actionsElement = handler.SelectSingleNode("Actions") as XmlElement;
                    if (actionsElement == null)
                    {
                        Console.WriteLine($"          Handler {handlerIndex}: No actions");
                        continue;
                    }

                    // Check if this handler has Execute actions with ListRefresh method
                    XmlNodeList executeActions = actionsElement.SelectNodes("Action[@Type='Execute']");
                    Console.WriteLine($"          Handler {handlerIndex}: Found {executeActions.Count} Execute actions");

                    bool hasListRefresh = false;
                    bool hasInitialize = false;

                    foreach (XmlElement action in executeActions)
                    {
                        XmlNode methodNode = action.SelectSingleNode("Properties/Property[Name='Method']/Value");
                        if (methodNode != null)
                        {
                            string method = methodNode.InnerText;
                            if (method == "ListRefresh")
                            {
                                hasListRefresh = true;
                            }
                            else if (method == "Init" || method == "Initialize")
                            {
                                hasInitialize = true;
                            }
                        }
                    }

                    Console.WriteLine($"          Handler {handlerIndex}: Has Initialize={hasInitialize}, Has ListRefresh={hasListRefresh}");

                    // If this handler has both Initialize and ListRefresh, we need to split them
                    if (hasInitialize && hasListRefresh)
                    {
                        Console.WriteLine("        Found MIXED handler - splitting Initialize and ListRefresh actions");

                        // Create lists to hold the different action types
                        List<XmlElement> initActions = new List<XmlElement>();
                        List<XmlElement> listRefreshActions = new List<XmlElement>();

                        foreach (XmlElement action in executeActions)
                        {
                            XmlNode methodNode = action.SelectSingleNode("Properties/Property[Name='Method']/Value");
                            if (methodNode != null)
                            {
                                string method = methodNode.InnerText;
                                if (method == "ListRefresh")
                                {
                                    listRefreshActions.Add(action);
                                }
                                else if (method == "Init" || method == "Initialize")
                                {
                                    initActions.Add(action);
                                }
                            }
                        }

                        // Remove all ListRefresh actions from current handler
                        foreach (XmlElement action in listRefreshActions)
                        {
                            action.ParentNode.RemoveChild(action);
                        }

                        Console.WriteLine($"          Removed {listRefreshActions.Count} ListRefresh actions from handler");

                        // Create new conditional handler for ListRefresh actions
                        XmlElement newHandler = doc.CreateElement("Handler");
                        newHandler.SetAttribute("ID", Guid.NewGuid().ToString());
                        newHandler.SetAttribute("DefinitionID", Guid.NewGuid().ToString());

                        // Add handler properties
                        XmlElement props = doc.CreateElement("Properties");
                        XmlElement handlerNameProp = doc.CreateElement("Property");
                        XmlHelper.AddElement(doc, handlerNameProp, "Name", "HandlerName");
                        XmlHelper.AddElement(doc, handlerNameProp, "Value", "IfLogicalHandler");
                        props.AppendChild(handlerNameProp);

                        XmlElement locationProp = doc.CreateElement("Property");
                        XmlHelper.AddElement(doc, locationProp, "Name", "Location");
                        XmlHelper.AddElement(doc, locationProp, "Value", "form");
                        props.AppendChild(locationProp);

                        newHandler.AppendChild(props);

                        // Add ID parameter condition
                        XmlElement conditions = doc.CreateElement("Conditions");
                        XmlElement condition = CreateIDNotBlankCondition(doc);
                        conditions.AppendChild(condition);
                        newHandler.AppendChild(conditions);

                        // Add actions to new handler
                        XmlElement newActions = doc.CreateElement("Actions");

                        // FIRST ACTION: Hide Form Action Table (set IsVisible=false)
                        if (!string.IsNullOrEmpty(formActionTableId))
                        {
                            XmlElement hideTableAction = CreateFormActionTableControlVisibilityAction(doc,
                                formActionTableId, formActionTableName, false);
                            newActions.AppendChild(hideTableAction);
                            Console.WriteLine("        Added Hide Form Action Table action as first action in conditional handler");
                        }

                        // Then add the ListRefresh actions
                        foreach (XmlElement action in listRefreshActions)
                        {
                            newActions.AppendChild(action);
                        }
                        newHandler.AppendChild(newActions);

                        // Insert new handler after current one
                        handler.ParentNode.InsertAfter(newHandler, handler);

                        Console.WriteLine($"        Created new conditional handler with Hide Form Action Table and {listRefreshActions.Count} ListRefresh actions");
                        Console.WriteLine("        ✓ Successfully added ID parameter condition with Form Action Table visibility");
                        return;
                    }
                    // If handler only has ListRefresh actions, add condition to it
                    else if (hasListRefresh && !hasInitialize)
                    {
                        Console.WriteLine("        Found ListRefresh-only handler - adding condition");

                        // Check if it already has conditions
                        XmlElement existingConditions = handler.SelectSingleNode("Conditions") as XmlElement;
                        if (existingConditions == null)
                        {
                            // Add the ID parameter condition
                            XmlElement conditions = doc.CreateElement("Conditions");
                            XmlElement condition = CreateIDNotBlankCondition(doc);
                            conditions.AppendChild(condition);

                            // Insert before Actions
                            handler.InsertBefore(conditions, actionsElement);

                            // Update handler to IfLogicalHandler
                            XmlElement handlerProps = handler.SelectSingleNode("Properties") as XmlElement;
                            if (handlerProps != null)
                            {
                                XmlElement handlerNameProp = handlerProps.SelectSingleNode("Property[Name='HandlerName']") as XmlElement;
                                if (handlerNameProp != null)
                                {
                                    XmlElement valueElement = handlerNameProp.SelectSingleNode("Value") as XmlElement;
                                    if (valueElement != null)
                                    {
                                        valueElement.InnerText = "IfLogicalHandler";
                                    }
                                }
                                else
                                {
                                    // Add HandlerName property
                                    XmlElement newProp = doc.CreateElement("Property");
                                    XmlHelper.AddElement(doc, newProp, "Name", "HandlerName");
                                    XmlHelper.AddElement(doc, newProp, "Value", "IfLogicalHandler");
                                    handlerProps.AppendChild(newProp);
                                }
                            }

                            // Add Hide Form Action Table action to the beginning of actions
                            if (!string.IsNullOrEmpty(formActionTableId))
                            {
                                XmlElement hideTableAction = CreateFormActionTableControlVisibilityAction(doc,
                                    formActionTableId, formActionTableName, false);

                                if (actionsElement.FirstChild != null)
                                {
                                    actionsElement.InsertBefore(hideTableAction, actionsElement.FirstChild);
                                }
                                else
                                {
                                    actionsElement.AppendChild(hideTableAction);
                                }

                                Console.WriteLine("        ✓ Added Hide Form Action Table action to conditional handler");
                            }

                            Console.WriteLine("        ✓ Added ID parameter condition to ListRefresh handler");
                            return;
                        }
                    }
                }
            }

            Console.WriteLine("    === Could not find appropriate handler for ID condition ===");
        }
        private XmlElement CreateFormActionTableControlVisibilityAction(XmlDocument doc,
            string controlId, string controlName, bool show)
        {
            string visibility = show ? "Show" : "Hide";
            Console.WriteLine($"            Creating {visibility} Form Action Table control action");

            XmlElement action = doc.CreateElement("Action");
            action.SetAttribute("ID", Guid.NewGuid().ToString());
            action.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            action.SetAttribute("Type", "Transfer");
            action.SetAttribute("ExecutionType", "Synchronous");

            XmlElement props = doc.CreateElement("Properties");

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", "Form");
            props.AppendChild(locationProp);

            // Add ControlID property like in target XML
            XmlElement controlIdProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, controlIdProp, "Name", "ControlID");
            XmlHelper.AddElement(doc, controlIdProp, "DisplayValue", controlName);
            XmlHelper.AddElement(doc, controlIdProp, "NameValue", controlName);
            XmlHelper.AddElement(doc, controlIdProp, "Value", controlId);
            props.AppendChild(controlIdProp);

            action.AppendChild(props);

            XmlElement parameters = doc.CreateElement("Parameters");
            XmlElement parameter = doc.CreateElement("Parameter");
            parameter.SetAttribute("SourceType", "Value");
            parameter.SetAttribute("TargetID", "isvisible");
            parameter.SetAttribute("TargetDisplayName", controlName);
            parameter.SetAttribute("TargetType", "ControlProperty");

            XmlElement sourceValue = doc.CreateElement("SourceValue");
            sourceValue.SetAttribute("xml:space", "preserve");
            sourceValue.InnerText = show ? "true" : "false";
            parameter.AppendChild(sourceValue);

            parameters.AppendChild(parameter);
            action.AppendChild(parameters);

            return action;
        }

        private void AddFormActionTableVisibilityToInitRule(XmlDocument doc)
        {
            Console.WriteLine("\n    === Adding Form Action Table Visibility Logic ===");

            // Find the Form Action Table AreaItem control
            XmlNodeList areaItemControls = doc.SelectNodes("//Control[@Type='AreaItem']");
            string formActionTableAreaItemId = null;
            string formActionTableAreaItemName = null;

            foreach (XmlElement control in areaItemControls)
            {
                XmlNode nameNode = control.SelectSingleNode("Name");
                if (nameNode != null && nameNode.InnerText.Contains("Area Item"))
                {
                    // This is likely our Form Action Table area item
                    formActionTableAreaItemId = control.GetAttribute("ID");
                    formActionTableAreaItemName = nameNode.InnerText;
                    Console.WriteLine($"      Found Form Action Table AreaItem: {formActionTableAreaItemName} (ID: {formActionTableAreaItemId})");
                    break;
                }
            }

            if (string.IsNullOrEmpty(formActionTableAreaItemId))
            {
                Console.WriteLine("      WARNING: Could not find Form Action Table AreaItem");
                return;
            }

            // Find the conditional handler we created earlier with ID parameter condition
            XmlNodeList conditionalHandlers = doc.SelectNodes("//Event[@SourceType='Form']/Handlers/Handler[Conditions/Condition/Expressions/IsNotBlank/Item[@SourceID='ID']]");

            if (conditionalHandlers.Count > 0)
            {
                XmlElement targetHandler = (XmlElement)conditionalHandlers[0];
                XmlElement actionsElement = targetHandler.SelectSingleNode("Actions") as XmlElement;

                if (actionsElement != null)
                {
                    // Add action to SHOW the Form Action Table when ID is not blank
                    XmlElement showTableAction = CreateAreaItemVisibilityAction(doc,
                        formActionTableAreaItemId, formActionTableAreaItemName, "Show");
                    actionsElement.AppendChild(showTableAction);
                    Console.WriteLine("        ✓ Added Show Form Action Table action to conditional handler");
                }
            }

            // Now find the unconditional handler (for when ID is blank) or create one
            AddHideFormActionTableForBlankID(doc, formActionTableAreaItemId, formActionTableAreaItemName);
        }

        private void AddHideFormActionTableForBlankID(XmlDocument doc, string areaItemId, string areaItemName)
        {
            Console.WriteLine("      Adding Hide Form Action Table for blank ID condition");

            // Find the Form event handlers
            XmlNodeList formEvents = doc.SelectNodes("//Event[@SourceType='Form']");

            foreach (XmlElement formEvent in formEvents)
            {
                XmlElement handlersContainer = formEvent.SelectSingleNode("Handlers") as XmlElement;
                if (handlersContainer == null) continue;

                // Check if we need to add an else handler (for when ID is blank)
                XmlNodeList handlers = handlersContainer.SelectNodes("Handler");

                // Look for our conditional handler with ID check
                foreach (XmlElement handler in handlers)
                {
                    XmlElement conditions = handler.SelectSingleNode("Conditions") as XmlElement;
                    if (conditions != null)
                    {
                        // This is our conditional handler, add an else handler after it
                        XmlElement elseHandler = doc.CreateElement("Handler");
                        elseHandler.SetAttribute("ID", Guid.NewGuid().ToString());
                        elseHandler.SetAttribute("DefinitionID", Guid.NewGuid().ToString());

                        // Add properties
                        XmlElement props = doc.CreateElement("Properties");
                        XmlElement handlerNameProp = doc.CreateElement("Property");
                        XmlHelper.AddElement(doc, handlerNameProp, "Name", "HandlerName");
                        XmlHelper.AddElement(doc, handlerNameProp, "Value", "ElseLogicalHandler");
                        props.AppendChild(handlerNameProp);

                        XmlElement locationProp = doc.CreateElement("Property");
                        XmlHelper.AddElement(doc, locationProp, "Name", "Location");
                        XmlHelper.AddElement(doc, locationProp, "Value", "form");
                        props.AppendChild(locationProp);

                        elseHandler.AppendChild(props);

                        // Add action to HIDE the Form Action Table
                        XmlElement actions = doc.CreateElement("Actions");
                        XmlElement hideTableAction = CreateAreaItemVisibilityAction(doc,
                            areaItemId, areaItemName, "Hide");
                        actions.AppendChild(hideTableAction);
                        elseHandler.AppendChild(actions);

                        // Insert the else handler after the conditional handler
                        handler.ParentNode.InsertAfter(elseHandler, handler);
                        Console.WriteLine("        ✓ Added Else handler to hide Form Action Table when ID is blank");
                        return;
                    }
                }
            }
        }

        private XmlElement CreateAreaItemVisibilityAction(XmlDocument doc, string areaItemId,
    string areaItemName, string visibility)
        {
            Console.WriteLine($"          Creating {visibility} action for Form Action Table");

            XmlElement action = doc.CreateElement("Action");
            action.SetAttribute("ID", Guid.NewGuid().ToString());
            action.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            action.SetAttribute("Type", "Transfer");
            action.SetAttribute("ExecutionType", "Parallel");

            XmlElement props = doc.CreateElement("Properties");

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", "Form");
            props.AppendChild(locationProp);

            action.AppendChild(props);

            // Add parameter to set visibility
            XmlElement parameters = doc.CreateElement("Parameters");
            XmlElement parameter = doc.CreateElement("Parameter");
            parameter.SetAttribute("SourceType", "Value");
            parameter.SetAttribute("TargetID", areaItemId);
            parameter.SetAttribute("TargetName", "IsVisible");
            parameter.SetAttribute("TargetDisplayName", "IsVisible");
            parameter.SetAttribute("TargetType", "ControlProperty");

            XmlElement sourceValue = doc.CreateElement("SourceValue");
            sourceValue.SetAttribute("xml:space", "preserve");
            sourceValue.InnerText = (visibility == "Show") ? "true" : "false";
            parameter.AppendChild(sourceValue);

            parameters.AppendChild(parameter);
            action.AppendChild(parameters);

            return action;
        }

        private void AddLoadActionToInitRule(XmlDocument doc, string formName)
        {
            Console.WriteLine("\n    === Adding SmartObject Load Action to Init Rule ===");

            // Find the conditional handler we created/modified earlier with ListRefresh actions
            XmlNodeList handlers = doc.SelectNodes("//Event[@SourceType='Form']/Handlers/Handler[Conditions/Condition/Expressions/IsNotBlank/Item[@SourceID='ID']]");

            if (handlers.Count == 0)
            {
                Console.WriteLine("      WARNING: Could not find conditional handler with ID parameter condition");
                return;
            }

            XmlElement targetHandler = (XmlElement)handlers[0];
            XmlElement actionsElement = targetHandler.SelectSingleNode("Actions") as XmlElement;

            if (actionsElement == null)
            {
                Console.WriteLine("      ERROR: No Actions element found in conditional handler");
                return;
            }

            // Collect ALL main views and Part views (not List or Item views)
            XmlNodeList items = doc.SelectNodes("//Panel/Areas/Area/Items/Item[@ViewID]");
            List<ViewInfo> mainViews = new List<ViewInfo>();

            foreach (XmlElement item in items)
            {
                string viewName = item.SelectSingleNode("Name")?.InnerText ?? "";
                string viewId = item.GetAttribute("ViewID");
                string instanceId = item.GetAttribute("ID");

                // Include main views and Part views, but not List or Item views
                if (!viewName.Contains("_List") && !viewName.Contains("_Item"))
                {
                    mainViews.Add(new ViewInfo
                    {
                        Name = viewName,
                        ViewId = viewId,
                        InstanceId = instanceId
                    });
                    Console.WriteLine($"      Found view to map: {viewName}");
                }
            }

            if (mainViews.Count == 0)
            {
                Console.WriteLine("      No main views found to map");
                return;
            }

            // Determine the main SmartObject name from the first view
            string smoName = ExtractSmartObjectName(mainViews[0].Name);
            Console.WriteLine($"      Creating single Load action for SmartObject: {smoName}");

            // Create ONE Load action that maps to ALL views
            XmlElement loadAction = CreateLoadActionForMultipleViews(doc, smoName, mainViews);

            if (loadAction != null)
            {
                actionsElement.AppendChild(loadAction);
                Console.WriteLine($"        ✓ Added single Load action mapping to {mainViews.Count} views");
            }

            Console.WriteLine("    === Completed Adding Load Action ===");
        }

        private XmlElement CreateLoadActionForMultipleViews(XmlDocument doc, string smoName, List<ViewInfo> views)
        {
            XmlElement action = doc.CreateElement("Action");
            action.SetAttribute("ID", Guid.NewGuid().ToString());
            action.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            action.SetAttribute("Type", "Execute");
            action.SetAttribute("ExecutionType", "Synchronous");

            XmlElement props = doc.CreateElement("Properties");

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", "Form");
            props.AppendChild(locationProp);

            // Get the GUID and ensure it's properly formatted
            string smoGuid = GetSmartObjectGuid(smoName);

            // Ensure the GUID is in the correct format (lowercase with hyphens)
            if (!string.IsNullOrEmpty(smoGuid))
            {
                try
                {
                    Guid parsedGuid = Guid.Parse(smoGuid);
                    smoGuid = parsedGuid.ToString().ToLower(); // K2 often expects lowercase GUIDs
                }
                catch
                {
                    Console.WriteLine($"          WARNING: Invalid GUID format for {smoName}: {smoGuid}");
                }
            }

            XmlElement objectIdProp = doc.CreateElement("Property");
            objectIdProp.SetAttribute("ValidationStatus", "Auto");
            objectIdProp.SetAttribute("ValidationMessages",
                $"ActionObject,Object,Auto,{smoGuid},{smoName},{smoName.Replace("_", " ")}");
            XmlHelper.AddElement(doc, objectIdProp, "Name", "ObjectID");
            XmlHelper.AddElement(doc, objectIdProp, "DisplayValue", smoName.Replace("_", " "));
            XmlHelper.AddElement(doc, objectIdProp, "NameValue", smoName);
            XmlHelper.AddElement(doc, objectIdProp, "Value", smoGuid);
            props.AppendChild(objectIdProp);

            XmlElement methodProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, methodProp, "Name", "Method");
            XmlHelper.AddElement(doc, methodProp, "DisplayValue", "Load");
            XmlHelper.AddElement(doc, methodProp, "NameValue", "Load");
            XmlHelper.AddElement(doc, methodProp, "Value", "Load");
            props.AppendChild(methodProp);

            action.AppendChild(props);

            // Add Parameters - ID parameter maps to ID property
            XmlElement parameters = doc.CreateElement("Parameters");
            XmlElement param = doc.CreateElement("Parameter");
            param.SetAttribute("SourceID", "ID");
            param.SetAttribute("SourceName", "ID");
            param.SetAttribute("SourceType", "FormParameter");
            param.SetAttribute("TargetID", "ID");
            param.SetAttribute("TargetName", "ID");
            param.SetAttribute("TargetDisplayName", "ID");
            param.SetAttribute("TargetType", "ObjectProperty");
            param.SetAttribute("IsRequired", "True");
            parameters.AppendChild(param);
            action.AppendChild(parameters);

            // Add Results - map SmartObject properties to controls in ALL views
            XmlElement results = doc.CreateElement("Results");
            HashSet<string> mappedFields = new HashSet<string>(); // Track which fields we've already mapped

            foreach (var view in views)
            {
                Console.WriteLine($"          Processing controls for view: {view.Name}");

                // Get the controls for this view from ControlMappingService
                var viewControls = ControlMappingService.GetViewControls(view.Name);

                if (viewControls != null && viewControls.Count > 0)
                {
                    Console.WriteLine($"            Found {viewControls.Count} controls to map");

                    foreach (var control in viewControls)
                    {
                        // Create a result mapping for each control
                        XmlElement result = doc.CreateElement("Result");

                        // Source (SmartObject property)
                        result.SetAttribute("SourceID", control.Value.FieldName);
                        result.SetAttribute("SourceName", control.Value.FieldName);
                        result.SetAttribute("SourceDisplayName", GetFieldDisplayName(control.Value.FieldName));
                        result.SetAttribute("SourceType", "ObjectProperty");

                        // Target (View control)
                        result.SetAttribute("TargetInstanceID", view.InstanceId);
                        result.SetAttribute("TargetID", control.Value.ControlId);
                        result.SetAttribute("TargetName", control.Value.ControlName);
                        result.SetAttribute("TargetDisplayName", control.Value.ControlName);
                        result.SetAttribute("TargetType", "Control");

                        results.AppendChild(result);

                        Console.WriteLine($"            Mapped {control.Value.FieldName} -> {control.Value.ControlName} (View: {view.Name})");
                        mappedFields.Add(control.Value.FieldName);
                    }
                }
                else
                {
                    Console.WriteLine($"            WARNING: No controls found for view {view.Name}");

                    // If no controls found via ControlMappingService, try to get basic field mappings
                    // This is a fallback for views that might not have been properly registered
                    if (_smoGenerator != null && _smoGenerator.FieldMappings.ContainsKey(smoName))
                    {
                        var smoFields = _smoGenerator.FieldMappings[smoName];
                        Console.WriteLine($"            Attempting fallback mapping using {smoFields.Count} SmartObject fields");

                        foreach (var field in smoFields)
                        {
                            // Skip if we've already mapped this field
                            if (mappedFields.Contains(field.Key))
                                continue;

                            // Skip system fields
                            if (field.Key == "ID" || field.Key == "PARENTID")
                                continue;

                            // Create a generic result mapping
                            XmlElement result = doc.CreateElement("Result");

                            result.SetAttribute("SourceID", field.Value.FieldName);
                            result.SetAttribute("SourceName", field.Value.FieldName);
                            result.SetAttribute("SourceDisplayName", field.Value.DisplayName);
                            result.SetAttribute("SourceType", "ObjectProperty");

                            // For fallback, we'll need to generate a control ID
                            // This might not work perfectly but is better than nothing
                            result.SetAttribute("TargetInstanceID", view.InstanceId);
                            result.SetAttribute("TargetID", Guid.NewGuid().ToString());
                            result.SetAttribute("TargetName", field.Value.DisplayName);
                            result.SetAttribute("TargetDisplayName", field.Value.DisplayName);
                            result.SetAttribute("TargetType", "Control");

                            results.AppendChild(result);
                            mappedFields.Add(field.Key);

                            Console.WriteLine($"            Fallback mapped {field.Value.FieldName} -> {field.Value.DisplayName}");
                        }
                    }
                }
            }

            action.AppendChild(results);

            // Log summary information
            Console.WriteLine($"          Total fields mapped: {mappedFields.Count} unique fields to {results.ChildNodes.Count} controls");
            Console.WriteLine($"          Using SmartObject: {smoName}");
            Console.WriteLine($"          Using SmartObject GUID: {smoGuid}");

            // Verify we have at least some mappings
            if (results.ChildNodes.Count == 0)
            {
                Console.WriteLine($"          WARNING: No field mappings created for Load action!");
                Console.WriteLine($"          This Load action may not work correctly");
            }

            return action;
        }

        private XmlElement CreateLoadAction(XmlDocument doc, string smoName, string viewName, string viewId, string instanceId)
        {
            XmlElement action = doc.CreateElement("Action");
            action.SetAttribute("ID", Guid.NewGuid().ToString());
            action.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            action.SetAttribute("Type", "Execute");
            action.SetAttribute("ExecutionType", "Synchronous");

            XmlElement props = doc.CreateElement("Properties");

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", "Form");
            props.AppendChild(locationProp);

            // We need to get the SmartObject GUID - for now we'll use the name
            // In production, you'd look this up from the SmartObject service
            XmlElement objectIdProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, objectIdProp, "Name", "ObjectID");
            XmlHelper.AddElement(doc, objectIdProp, "DisplayValue", smoName.Replace("_", " "));
            XmlHelper.AddElement(doc, objectIdProp, "NameValue", smoName);
            // The Value should be the SmartObject GUID - you'll need to look this up
            XmlHelper.AddElement(doc, objectIdProp, "Value", GetSmartObjectGuid(smoName));
            props.AppendChild(objectIdProp);

            XmlElement methodProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, methodProp, "Name", "Method");
            XmlHelper.AddElement(doc, methodProp, "DisplayValue", "Load");
            XmlHelper.AddElement(doc, methodProp, "NameValue", "Load");
            XmlHelper.AddElement(doc, methodProp, "Value", "Load");
            props.AppendChild(methodProp);

            action.AppendChild(props);

            // Add Parameters - ID parameter maps to ID property
            XmlElement parameters = doc.CreateElement("Parameters");
            XmlElement param = doc.CreateElement("Parameter");
            param.SetAttribute("SourceID", "ID");
            param.SetAttribute("SourceName", "ID");
            param.SetAttribute("SourceType", "FormParameter");
            param.SetAttribute("TargetID", "ID");
            param.SetAttribute("TargetName", "ID");
            param.SetAttribute("TargetDisplayName", "ID");
            param.SetAttribute("TargetType", "ObjectProperty");
            param.SetAttribute("IsRequired", "True");
            parameters.AppendChild(param);
            action.AppendChild(parameters);

            // Add Results - map SmartObject properties to view controls
            XmlElement results = doc.CreateElement("Results");

            // Get the controls for this view from ControlMappingService
            var viewControls = ControlMappingService.GetViewControls(viewName);

            if (viewControls != null)
            {
                foreach (var control in viewControls)
                {
                    XmlElement result = doc.CreateElement("Result");
                    result.SetAttribute("SourceID", control.Value.FieldName);
                    result.SetAttribute("SourceName", control.Value.FieldName);
                    result.SetAttribute("SourceDisplayName", GetFieldDisplayName(control.Value.FieldName));
                    result.SetAttribute("SourceType", "ObjectProperty");
                    result.SetAttribute("TargetInstanceID", instanceId);
                    result.SetAttribute("TargetID", control.Value.ControlId);
                    result.SetAttribute("TargetName", control.Value.ControlName);
                    result.SetAttribute("TargetDisplayName", control.Value.ControlName);
                    result.SetAttribute("TargetType", "Control");
                    results.AppendChild(result);

                    Console.WriteLine($"          Mapped {control.Value.FieldName} -> {control.Value.ControlName}");
                }
            }

            action.AppendChild(results);

            return action;
        }
        private string GetFieldDisplayName(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName))
                return fieldName;

            string displayName = fieldName;

            // If field name is all uppercase, convert to title case
            if (displayName == displayName.ToUpper())
            {
                displayName = displayName.ToLower();
                displayName = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(displayName);
            }
            else
            {
                // Insert spaces before capital letters (for camelCase)
                displayName = System.Text.RegularExpressions.Regex.Replace(displayName, "([a-z])([A-Z])", "$1 $2");
            }

            // Insert spaces between uppercase letters followed by lowercase (for acronyms like XMLData -> XML Data)
            displayName = System.Text.RegularExpressions.Regex.Replace(displayName, "([A-Z]+)([A-Z][a-z])", "$1 $2");

            // Ensure first letter is uppercase
            if (displayName.Length > 0 && char.IsLower(displayName[0]))
            {
                displayName = char.ToUpper(displayName[0]) + displayName.Substring(1);
            }

            return displayName;
        }

        private string ExtractSmartObjectName(string viewName)
        {
            // Extract the base SmartObject name from the view name
            // For example: "Asset_Tracking_ASSET_TRACKING" -> "Asset_Tracking"

            // For views that end with repeated base name
            if (viewName.Contains("_"))
            {
                string[] parts = viewName.Split('_');
                if (parts.Length >= 2)
                {
                    // Take first two parts as base name (e.g., "Asset_Tracking")
                    return $"{parts[0]}_{parts[1]}";
                }
            }

            return viewName;
        }

        private string GetSmartObjectGuid(string smoName)
        {
            // First try to get from the SmartObject generator cache if available
            if (_smoGenerator != null)
            {
                try
                {
                    return _smoGenerator.GetSmartObjectGuid(smoName);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"          Could not get GUID from generator for {smoName}: {ex.Message}");
                }
            }

            // Fallback to server lookup
            var smoManagementServer = _connectionManager.ManagementServer;

            try
            {
                var smoInfo = smoManagementServer.GetSmartObjectDefinition(smoName);
                if (!string.IsNullOrEmpty(smoInfo))
                {
                    // Parse the XML to get the GUID
                    XmlDocument smoDoc = new XmlDocument();
                    smoDoc.LoadXml(smoInfo);

                    // Try different paths to find the GUID
                    XmlNode guidNode = smoDoc.SelectSingleNode("//smartobject/@guid");
                    if (guidNode == null)
                    {
                        guidNode = smoDoc.SelectSingleNode("//SmartObject/@Guid");
                    }
                    if (guidNode == null)
                    {
                        guidNode = smoDoc.SelectSingleNode("//SmartObject/@ID");
                    }

                    if (guidNode != null)
                    {
                        string guid = guidNode.Value;
                        Console.WriteLine($"          Retrieved GUID for SmartObject {smoName}: {guid}");
                        return guid;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"          WARNING: Could not get GUID for SmartObject {smoName}: {ex.Message}");
            }

            // Return a new GUID as fallback (this will cause issues)
            string fallbackGuid = Guid.NewGuid().ToString();
            Console.WriteLine($"          WARNING: Using fallback GUID for {smoName}: {fallbackGuid}");
            return fallbackGuid;
        }



        private void CreateConditionalListRefreshHandler(XmlDocument doc, XmlElement afterHandler, List<XmlElement> listRefreshActions)
        {
            XmlElement newHandler = doc.CreateElement("Handler");
            newHandler.SetAttribute("ID", Guid.NewGuid().ToString());
            newHandler.SetAttribute("DefinitionID", Guid.NewGuid().ToString());

            // Add Properties for the new handler
            XmlElement props = doc.CreateElement("Properties");

            XmlElement handlerNameProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, handlerNameProp, "Name", "HandlerName");
            XmlHelper.AddElement(doc, handlerNameProp, "Value", "IfLogicalHandler");
            props.AppendChild(handlerNameProp);

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", "form");
            props.AppendChild(locationProp);

            newHandler.AppendChild(props);

            // Add Conditions element with ID parameter condition
            XmlElement conditions = doc.CreateElement("Conditions");
            XmlElement condition = CreateIDNotBlankCondition(doc);
            conditions.AppendChild(condition);
            newHandler.AppendChild(conditions);

            // Add Actions element with ListRefresh actions
            XmlElement newActions = doc.CreateElement("Actions");
            foreach (XmlElement listRefreshAction in listRefreshActions)
            {
                newActions.AppendChild(listRefreshAction);
            }
            newHandler.AppendChild(newActions);

            // Insert the new handler after the current handler
            afterHandler.ParentNode.InsertAfter(newHandler, afterHandler);

            Console.WriteLine($"        Created second handler with ID condition and {listRefreshActions.Count} ListRefresh actions");
        }

        private void AddConditionToHandler(XmlDocument doc, XmlElement handler)
        {
            XmlElement actionsElement = handler.SelectSingleNode("Actions") as XmlElement;

            // Create Conditions element
            XmlElement conditions = doc.CreateElement("Conditions");
            XmlElement condition = CreateIDNotBlankCondition(doc);
            conditions.AppendChild(condition);

            // Insert Conditions before Actions
            handler.InsertBefore(conditions, actionsElement);

            // Update handler to be IfLogicalHandler
            XmlElement handlerProps = handler.SelectSingleNode("Properties") as XmlElement;
            if (handlerProps != null)
            {
                XmlElement handlerNameProp = handlerProps.SelectSingleNode("Property[Name='HandlerName']") as XmlElement;
                if (handlerNameProp != null)
                {
                    XmlElement valueElement = handlerNameProp.SelectSingleNode("Value") as XmlElement;
                    if (valueElement != null)
                    {
                        valueElement.InnerText = "IfLogicalHandler";
                    }
                }
                else
                {
                    // Add the property if it doesn't exist
                    XmlElement newProp = doc.CreateElement("Property");
                    XmlHelper.AddElement(doc, newProp, "Name", "HandlerName");
                    XmlHelper.AddElement(doc, newProp, "Value", "IfLogicalHandler");
                    handlerProps.AppendChild(newProp);
                }
            }

            Console.WriteLine("        ✓ Added ID condition to ListRefresh handler");
        }

        private XmlElement CreateIDNotBlankCondition(XmlDocument doc)
        {
            XmlElement condition = doc.CreateElement("Condition");
            condition.SetAttribute("ID", Guid.NewGuid().ToString());
            condition.SetAttribute("DefinitionID", Guid.NewGuid().ToString());

            // Condition Properties
            XmlElement condProps = doc.CreateElement("Properties");

            XmlElement condLocationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, condLocationProp, "Name", "Location");
            XmlHelper.AddElement(doc, condLocationProp, "Value", "Form");
            condProps.AppendChild(condLocationProp);

            XmlElement condNameProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, condNameProp, "Name", "Name");
            XmlHelper.AddElement(doc, condNameProp, "Value", "SimpleNotBlankFormParameterCondition");
            condProps.AppendChild(condNameProp);

            condition.AppendChild(condProps);

            // Add the IsNotBlank expression
            XmlElement expressions = doc.CreateElement("Expressions");
            XmlElement isNotBlank = doc.CreateElement("IsNotBlank");

            XmlElement item = doc.CreateElement("Item");
            item.SetAttribute("SourceType", "FormParameter");
            item.SetAttribute("SourceID", "ID");
            item.SetAttribute("SourceName", "ID");
            item.SetAttribute("DataType", "Text");

            isNotBlank.AppendChild(item);
            expressions.AppendChild(isNotBlank);
            condition.AppendChild(expressions);

            return condition;
        }




        // This method correctly splits the Init handler to match the target XML structure
        private void SplitInitHandlerForConditionalListRefresh(XmlDocument doc)
        {
            Console.WriteLine("\n    === Splitting Init Handler for Conditional List Refresh ===");

            // Find the Form Init event
            XmlNodeList allEvents = doc.SelectNodes("//Event[@SourceType='Form' and @SourceName='Init']");

            foreach (XmlElement initEvent in allEvents)
            {
                Console.WriteLine($"      Found Form Init event: {initEvent.GetAttribute("ID")}");

                XmlElement handlersContainer = initEvent.SelectSingleNode("Handlers") as XmlElement;
                if (handlersContainer == null)
                {
                    Console.WriteLine("        ERROR: No Handlers container found");
                    continue;
                }

                // Find the handler that has both Initialize and ListRefresh actions
                XmlNodeList handlers = handlersContainer.SelectNodes("Handler");

                foreach (XmlElement handler in handlers)
                {
                    XmlElement actionsElement = handler.SelectSingleNode("Actions") as XmlElement;
                    if (actionsElement == null) continue;

                    XmlNodeList allActions = actionsElement.SelectNodes("Action");
                    List<XmlElement> initActions = new List<XmlElement>();
                    List<XmlElement> listRefreshActions = new List<XmlElement>();

                    // Categorize actions by their method type
                    foreach (XmlElement action in allActions)
                    {
                        XmlNode methodValueNode = action.SelectSingleNode("Properties/Property[Name='Method']/Value");
                        if (methodValueNode != null)
                        {
                            string method = methodValueNode.InnerText;
                            if (method == "Init" || method == "Initialize")
                            {
                                initActions.Add(action);
                            }
                            else if (method == "ListRefresh")
                            {
                                listRefreshActions.Add(action);
                            }
                        }
                    }

                    // If we have both types of actions, we need to split them
                    if (initActions.Count > 0 && listRefreshActions.Count > 0)
                    {
                        Console.WriteLine($"        Found handler with {initActions.Count} Init and {listRefreshActions.Count} ListRefresh actions");
                        Console.WriteLine("        Splitting into two handlers...");

                        // Clear the current actions element
                        actionsElement.RemoveAll();

                        // Add back only Init actions to the first handler
                        foreach (XmlElement initAction in initActions)
                        {
                            actionsElement.AppendChild(initAction);
                        }
                        Console.WriteLine($"        First handler now has {initActions.Count} Init actions only");

                        // Create new handler for ListRefresh actions with ID condition
                        XmlElement newHandler = doc.CreateElement("Handler");
                        newHandler.SetAttribute("ID", Guid.NewGuid().ToString());
                        newHandler.SetAttribute("DefinitionID", Guid.NewGuid().ToString());

                        // Add Properties for the new handler
                        XmlElement props = doc.CreateElement("Properties");

                        XmlElement handlerNameProp = doc.CreateElement("Property");
                        XmlHelper.AddElement(doc, handlerNameProp, "Name", "HandlerName");
                        XmlHelper.AddElement(doc, handlerNameProp, "Value", "IfLogicalHandler");
                        props.AppendChild(handlerNameProp);

                        XmlElement locationProp = doc.CreateElement("Property");
                        XmlHelper.AddElement(doc, locationProp, "Name", "Location");
                        XmlHelper.AddElement(doc, locationProp, "Value", "form");
                        props.AppendChild(locationProp);

                        newHandler.AppendChild(props);

                        // Add Conditions element with ID parameter condition
                        XmlElement conditions = doc.CreateElement("Conditions");

                        // Create the ID not blank condition matching the target XML structure
                        XmlElement condition = doc.CreateElement("Condition");
                        condition.SetAttribute("ID", Guid.NewGuid().ToString());
                        condition.SetAttribute("DefinitionID", Guid.NewGuid().ToString());

                        // Condition Properties
                        XmlElement condProps = doc.CreateElement("Properties");

                        XmlElement condLocationProp = doc.CreateElement("Property");
                        XmlHelper.AddElement(doc, condLocationProp, "Name", "Location");
                        XmlHelper.AddElement(doc, condLocationProp, "Value", "Form");
                        condProps.AppendChild(condLocationProp);

                        XmlElement condNameProp = doc.CreateElement("Property");
                        XmlHelper.AddElement(doc, condNameProp, "Name", "Name");
                        XmlHelper.AddElement(doc, condNameProp, "Value", "SimpleNotBlankFormParameterCondition");
                        condProps.AppendChild(condNameProp);

                        condition.AppendChild(condProps);

                        // Add the IsNotBlank expression
                        XmlElement expressions = doc.CreateElement("Expressions");
                        XmlElement isNotBlank = doc.CreateElement("IsNotBlank");

                        XmlElement item = doc.CreateElement("Item");
                        item.SetAttribute("SourceType", "FormParameter");
                        item.SetAttribute("SourceID", "ID");
                        item.SetAttribute("SourceName", "ID");
                        item.SetAttribute("DataType", "Text");

                        isNotBlank.AppendChild(item);
                        expressions.AppendChild(isNotBlank);
                        condition.AppendChild(expressions);

                        conditions.AppendChild(condition);
                        newHandler.AppendChild(conditions);

                        // Add Actions element with ListRefresh actions
                        XmlElement newActions = doc.CreateElement("Actions");
                        foreach (XmlElement listRefreshAction in listRefreshActions)
                        {
                            newActions.AppendChild(listRefreshAction);
                        }
                        newHandler.AppendChild(newActions);

                        // Insert the new handler after the current handler
                        handler.ParentNode.InsertAfter(newHandler, handler);

                        Console.WriteLine($"        Created second handler with ID condition and {listRefreshActions.Count} ListRefresh actions");
                        Console.WriteLine("        ✓ Successfully split Init rule into two handlers");

                        // We're done - exit the loop
                        return;
                    }
                    else if (listRefreshActions.Count > 0 && initActions.Count == 0)
                    {
                        // This handler only has ListRefresh actions - just add the condition to it
                        Console.WriteLine($"        Found handler with {listRefreshActions.Count} ListRefresh actions only");

                        // Check if it already has conditions
                        XmlElement existingConditions = handler.SelectSingleNode("Conditions") as XmlElement;
                        if (existingConditions == null)
                        {
                            Console.WriteLine("        Adding ID parameter condition to existing ListRefresh handler");

                            // Create Conditions element
                            XmlElement conditions = doc.CreateElement("Conditions");

                            // Create the condition
                            XmlElement condition = doc.CreateElement("Condition");
                            condition.SetAttribute("ID", Guid.NewGuid().ToString());
                            condition.SetAttribute("DefinitionID", Guid.NewGuid().ToString());

                            XmlElement condProps = doc.CreateElement("Properties");

                            XmlElement condLocationProp = doc.CreateElement("Property");
                            XmlHelper.AddElement(doc, condLocationProp, "Name", "Location");
                            XmlHelper.AddElement(doc, condLocationProp, "Value", "Form");
                            condProps.AppendChild(condLocationProp);

                            XmlElement condNameProp = doc.CreateElement("Property");
                            XmlHelper.AddElement(doc, condNameProp, "Name", "Name");
                            XmlHelper.AddElement(doc, condNameProp, "Value", "SimpleNotBlankFormParameterCondition");
                            condProps.AppendChild(condNameProp);

                            condition.AppendChild(condProps);

                            XmlElement expressions = doc.CreateElement("Expressions");
                            XmlElement isNotBlank = doc.CreateElement("IsNotBlank");

                            XmlElement item = doc.CreateElement("Item");
                            item.SetAttribute("SourceType", "FormParameter");
                            item.SetAttribute("SourceID", "ID");
                            item.SetAttribute("SourceName", "ID");
                            item.SetAttribute("DataType", "Text");

                            isNotBlank.AppendChild(item);
                            expressions.AppendChild(isNotBlank);
                            condition.AppendChild(expressions);

                            conditions.AppendChild(condition);

                            // Insert Conditions before Actions
                            handler.InsertBefore(conditions, actionsElement);

                            // Update handler to be IfLogicalHandler
                            XmlElement handlerProps = handler.SelectSingleNode("Properties") as XmlElement;
                            if (handlerProps != null)
                            {
                                XmlElement handlerNameProp = handlerProps.SelectSingleNode("Property[Name='HandlerName']") as XmlElement;
                                if (handlerNameProp != null)
                                {
                                    XmlElement valueElement = handlerNameProp.SelectSingleNode("Value") as XmlElement;
                                    if (valueElement != null)
                                    {
                                        valueElement.InnerText = "IfLogicalHandler";
                                    }
                                }
                            }

                            Console.WriteLine("        ✓ Added ID condition to ListRefresh handler");
                        }
                        else
                        {
                            Console.WriteLine("        Handler already has conditions");
                        }
                    }
                }
            }

            Console.WriteLine("    === Completed Splitting Init Handler ===");
        }

        // Helper method to move ListRefresh actions from other handlers to the conditional handler
        private void MoveListRefreshActions(XmlDocument doc, XmlNodeList allHandlers, XmlElement targetHandler)
        {
            XmlElement actionsContainer = doc.CreateElement("Actions");

            foreach (XmlElement handler in allHandlers)
            {
                if (handler == targetHandler) continue; // Skip the target handler itself

                XmlNodeList actions = handler.SelectNodes("Actions/Action");
                List<XmlNode> actionsToMove = new List<XmlNode>();

                foreach (XmlElement action in actions)
                {
                    // Check if this is a ListRefresh action
                    XmlNode methodNode = action.SelectSingleNode("Properties/Property[Name='Method']/Value");
                    if (methodNode != null && methodNode.InnerText == "ListRefresh")
                    {
                        actionsToMove.Add(action);
                    }
                }

                // Move the actions
                foreach (XmlNode action in actionsToMove)
                {
                    action.ParentNode.RemoveChild(action);
                    actionsContainer.AppendChild(action);
                    Console.WriteLine("          Moved ListRefresh action to conditional handler");
                }
            }

            if (actionsContainer.HasChildNodes)
            {
                targetHandler.AppendChild(actionsContainer);
            }
        }
        private XmlElement CreateIDParameterNotBlankCondition(XmlDocument doc)
        {
            XmlElement condition = doc.CreateElement("Condition");
            condition.SetAttribute("ID", Guid.NewGuid().ToString());
            condition.SetAttribute("DefinitionID", Guid.NewGuid().ToString());

            // Add properties
            XmlElement properties = doc.CreateElement("Properties");

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", "Form");
            properties.AppendChild(locationProp);

            XmlElement nameProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, nameProp, "Name", "Name");
            XmlHelper.AddElement(doc, nameProp, "Value", "SimpleNotBlankFormParameterCondition");
            properties.AppendChild(nameProp);

            condition.AppendChild(properties);

            // Add the expression
            XmlElement expressions = doc.CreateElement("Expressions");
            XmlElement isNotBlank = doc.CreateElement("IsNotBlank");

            XmlElement item = doc.CreateElement("Item");
            item.SetAttribute("SourceType", "FormParameter");
            item.SetAttribute("SourceID", "ID");
            item.SetAttribute("SourceName", "ID");
            item.SetAttribute("DataType", "Text");

            isNotBlank.AppendChild(item);
            expressions.AppendChild(isNotBlank);
            condition.AppendChild(expressions);

            return condition;
        }

        private void UpdateHandlerToConditional(XmlDocument doc, XmlElement handler)
        {
            // Update the handler properties to indicate it's conditional
            XmlElement handlerProps = handler.SelectSingleNode("Properties") as XmlElement;

            if (handlerProps != null)
            {
                // Check if HandlerName property exists
                XmlElement handlerNameProp = handlerProps.SelectSingleNode("Property[Name='HandlerName']") as XmlElement;

                if (handlerNameProp != null)
                {
                    // Update the value to IfLogicalHandler for conditional execution
                    XmlElement valueElement = handlerNameProp.SelectSingleNode("Value") as XmlElement;
                    if (valueElement != null && valueElement.InnerText != "IfLogicalHandler")
                    {
                        valueElement.InnerText = "IfLogicalHandler";
                        Console.WriteLine("            Updated handler type to IfLogicalHandler");
                    }
                }
                else
                {
                    // Add HandlerName property if it doesn't exist
                    XmlElement newHandlerNameProp = doc.CreateElement("Property");
                    XmlHelper.AddElement(doc, newHandlerNameProp, "Name", "HandlerName");
                    XmlHelper.AddElement(doc, newHandlerNameProp, "Value", "IfLogicalHandler");
                    handlerProps.AppendChild(newHandlerNameProp);
                }
            }
        }

        private void VerifyFormIDParameter(XmlDocument formDoc)
        {
            bool hasIdParameter = false;
            XmlNodeList paramNodes = formDoc.SelectNodes("//Form/Parameters/Parameter");

            foreach (XmlNode param in paramNodes)
            {
                XmlNodeList nameNodes = param.SelectNodes("Name");
                if (nameNodes.Count > 0 && nameNodes[0].InnerText == "ID")
                {
                    hasIdParameter = true;
                    Console.WriteLine("        Verified: ID parameter is present in form XML after restructuring");
                    break;
                }
            }

            if (!hasIdParameter)
            {
                Console.WriteLine("        WARNING: ID parameter not found after restructuring, re-adding it");
                AddIDParameterToForm(formDoc);
            }
        }
        private string ExtractRepeatingSectionNameFromView(string viewName, string infopathViewName)
        {
            // For view-specific list/item views, the pattern is:
            // {baseFormName}_{infopathViewName}_{sectionName}_List
            // {baseFormName}_{infopathViewName}_{sectionName}_Item

            string suffix = "";
            if (viewName.Contains("_List"))
            {
                suffix = "_List";
            }
            else if (viewName.Contains("_Item"))
            {
                suffix = "_Item";
            }

            if (!string.IsNullOrEmpty(suffix))
            {
                // Remove the suffix
                string withoutSuffix = viewName.Substring(0, viewName.LastIndexOf(suffix));

                // Now extract the section name, which should be after the InfoPath view name
                if (!string.IsNullOrEmpty(infopathViewName) && withoutSuffix.Contains($"_{infopathViewName}_"))
                {
                    int viewNameIndex = withoutSuffix.IndexOf($"_{infopathViewName}_");
                    if (viewNameIndex >= 0)
                    {
                        int startIndex = viewNameIndex + infopathViewName.Length + 2; // +2 for the underscores
                        if (startIndex < withoutSuffix.Length)
                        {
                            return withoutSuffix.Substring(startIndex);
                        }
                    }
                }

                // Fallback to original logic if new pattern doesn't match
                int lastUnderscore = withoutSuffix.LastIndexOf('_');
                if (lastUnderscore >= 0 && lastUnderscore < withoutSuffix.Length - 1)
                {
                    return withoutSuffix.Substring(lastUnderscore + 1);
                }
            }

            return null;
        }
        private void UpdateAreaItemControlTitle(XmlDocument doc, string viewName, string title)
        {
            XmlNodeList allControls = doc.GetElementsByTagName("Control");

            foreach (XmlElement control in allControls)
            {
                if (control.GetAttribute("Type") == "AreaItem")
                {
                    XmlNodeList nameNodes = control.GetElementsByTagName("Name");
                    if (nameNodes.Count > 0 && nameNodes[0].InnerText == viewName)
                    {
                        XmlNodeList propsNodes = control.GetElementsByTagName("Properties");
                        XmlElement propertiesElement = null;

                        if (propsNodes.Count == 0)
                        {
                            propertiesElement = doc.CreateElement("Properties");
                            XmlNodeList styleNodes = control.GetElementsByTagName("Styles");
                            if (styleNodes.Count > 0)
                            {
                                control.InsertBefore(propertiesElement, styleNodes[0]);
                            }
                            else
                            {
                                control.AppendChild(propertiesElement);
                            }
                        }
                        else
                        {
                            propertiesElement = (XmlElement)propsNodes[0];
                        }

                        bool hasTitleProperty = false;
                        bool hasWrapTextProperty = false;
                        bool hasCollapsibleProperty = false;
                        XmlNodeList properties = propertiesElement.GetElementsByTagName("Property");

                        foreach (XmlElement prop in properties)
                        {
                            XmlNodeList propNameNodes = prop.GetElementsByTagName("Name");
                            if (propNameNodes.Count > 0)
                            {
                                string propName = propNameNodes[0].InnerText;
                                if (propName == "Title")
                                {
                                    hasTitleProperty = true;
                                    SetPropertyValue(doc, prop, title);
                                    Console.WriteLine($"        Updated title to '{title}' for AreaItem: {viewName}");
                                }
                                else if (propName == "WrapText")
                                {
                                    hasWrapTextProperty = true;
                                    SetPropertyValue(doc, prop, "true");
                                }
                                else if (propName == "IsCollapsible")
                                {
                                    hasCollapsibleProperty = true;
                                    // Update existing IsCollapsible based on whether we have a title
                                    if (string.IsNullOrEmpty(title))
                                    {
                                        SetPropertyValue(doc, prop, "false");
                                    }
                                }
                            }
                        }

                        // If no title is provided or title is empty, disable collapsibility
                        if (string.IsNullOrEmpty(title))
                        {
                            // Don't add a title property if there's no title
                            Console.WriteLine($"        No title provided for AreaItem: {viewName}, disabling collapsibility");

                            // Add or update IsCollapsible property to false
                            if (!hasCollapsibleProperty)
                            {
                                XmlElement collapsibleProp = doc.CreateElement("Property");

                                XmlElement propName = doc.CreateElement("Name");
                                propName.InnerText = "IsCollapsible";
                                collapsibleProp.AppendChild(propName);

                                XmlElement propValue = doc.CreateElement("Value");
                                propValue.InnerText = "false";
                                collapsibleProp.AppendChild(propValue);

                                XmlElement propDisplayValue = doc.CreateElement("DisplayValue");
                                propDisplayValue.InnerText = "false";
                                collapsibleProp.AppendChild(propDisplayValue);

                                XmlElement propNameValue = doc.CreateElement("NameValue");
                                propNameValue.InnerText = "false";
                                collapsibleProp.AppendChild(propNameValue);

                                propertiesElement.AppendChild(collapsibleProp);
                                Console.WriteLine($"        Added IsCollapsible=false to AreaItem control: {viewName}");
                            }
                        }
                        else
                        {
                            // We have a title, add it if needed
                            if (!hasTitleProperty)
                            {
                                XmlElement titleProp = doc.CreateElement("Property");

                                XmlElement propName = doc.CreateElement("Name");
                                propName.InnerText = "Title";
                                titleProp.AppendChild(propName);

                                XmlElement propValue = doc.CreateElement("Value");
                                propValue.InnerText = title;
                                titleProp.AppendChild(propValue);

                                XmlElement propDisplayValue = doc.CreateElement("DisplayValue");
                                propDisplayValue.InnerText = title;
                                titleProp.AppendChild(propDisplayValue);

                                XmlElement propNameValue = doc.CreateElement("NameValue");
                                propNameValue.InnerText = title;
                                titleProp.AppendChild(propNameValue);

                                propertiesElement.AppendChild(titleProp);
                                Console.WriteLine($"        Added title '{title}' to AreaItem control: {viewName}");
                            }
                        }

                        // Always add WrapText property if it doesn't exist
                        if (!hasWrapTextProperty)
                        {
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

                            XmlElement propNameValue = doc.CreateElement("NameValue");
                            propNameValue.InnerText = "true";
                            wrapTextProp.AppendChild(propNameValue);

                            propertiesElement.AppendChild(wrapTextProp);
                            Console.WriteLine($"        Added WrapText=true to AreaItem control: {viewName}");
                        }

                        break;
                    }
                }
            }
        }
        private void SetPropertyValue(XmlDocument doc, XmlElement prop, string value)
        {
            // Helper method to set all property value elements
            XmlNodeList valueNodes = prop.GetElementsByTagName("Value");
            XmlNodeList displayValueNodes = prop.GetElementsByTagName("DisplayValue");
            XmlNodeList nameValueNodes = prop.GetElementsByTagName("NameValue");

            if (valueNodes.Count > 0)
                valueNodes[0].InnerText = value;
            else
            {
                XmlElement valueElement = doc.CreateElement("Value");
                valueElement.InnerText = value;
                prop.AppendChild(valueElement);
            }

            if (displayValueNodes.Count > 0)
                displayValueNodes[0].InnerText = value;
            else
            {
                XmlElement displayValueElement = doc.CreateElement("DisplayValue");
                displayValueElement.InnerText = value;
                prop.AppendChild(displayValueElement);
            }

            if (nameValueNodes.Count > 0)
                nameValueNodes[0].InnerText = value;
            else
            {
                XmlElement nameValueElement = doc.CreateElement("NameValue");
                nameValueElement.InnerText = value;
                prop.AppendChild(nameValueElement);
            }
        }



        private XmlElement CreateButtonArea(XmlDocument doc, string formName, out Dictionary<string, string> buttonGuids)
        {
            // Initialize the dictionary to return all GUIDs
            buttonGuids = new Dictionary<string, string>();

            // Create the area for the button table
            XmlElement buttonArea = doc.CreateElement("Area");
            string buttonAreaGuid = Guid.NewGuid().ToString();
            buttonArea.SetAttribute("ID", buttonAreaGuid);
            buttonGuids["ButtonArea"] = buttonAreaGuid;

            XmlElement items = doc.CreateElement("Items");

            // Create the AreaItem
            XmlElement areaItem = doc.CreateElement("Item");
            string areaItemGuid = Guid.NewGuid().ToString();
            areaItem.SetAttribute("ID", areaItemGuid);
            buttonGuids["AreaItem"] = areaItemGuid;

            XmlElement canvas = doc.CreateElement("Canvas");

            // Create the table control
            XmlElement tableControl = doc.CreateElement("Control");
            string tableGuid = Guid.NewGuid().ToString();
            tableControl.SetAttribute("ID", tableGuid);
            tableControl.SetAttribute("LayoutType", "Grid");
            buttonGuids["Table"] = tableGuid;

            // Create columns (2 columns, each 1fr)
            XmlElement columns = doc.CreateElement("Columns");

            XmlElement column1 = doc.CreateElement("Column");
            string column1Guid = Guid.NewGuid().ToString();
            column1.SetAttribute("ID", column1Guid);
            column1.SetAttribute("Size", "1fr");
            columns.AppendChild(column1);
            buttonGuids["Column1"] = column1Guid;

            XmlElement column2 = doc.CreateElement("Column");
            string column2Guid = Guid.NewGuid().ToString();
            column2.SetAttribute("ID", column2Guid);
            column2.SetAttribute("Size", "1fr");
            columns.AppendChild(column2);
            buttonGuids["Column2"] = column2Guid;

            tableControl.AppendChild(columns);

            // Create the row
            XmlElement rows = doc.CreateElement("Rows");
            XmlElement row = doc.CreateElement("Row");
            string rowGuid = Guid.NewGuid().ToString();
            row.SetAttribute("ID", rowGuid);
            buttonGuids["Row"] = rowGuid;

            XmlElement cells = doc.CreateElement("Cells");

            // Create cell 1 (for Clear button)
            XmlElement cell1 = doc.CreateElement("Cell");
            string cell1Guid = Guid.NewGuid().ToString();
            cell1.SetAttribute("ID", cell1Guid);
            buttonGuids["Cell1"] = cell1Guid;

            XmlElement clearButtonControl = doc.CreateElement("Control");
            string clearButtonGuid = Guid.NewGuid().ToString();
            clearButtonControl.SetAttribute("ID", clearButtonGuid);
            cell1.AppendChild(clearButtonControl);
            cells.AppendChild(cell1);
            buttonGuids["ClearButton"] = clearButtonGuid;

            // Create cell 2 (for Submit button - right aligned)
            XmlElement cell2 = doc.CreateElement("Cell");
            string cell2Guid = Guid.NewGuid().ToString();
            cell2.SetAttribute("ID", cell2Guid);
            buttonGuids["Cell2"] = cell2Guid;

            XmlElement submitButtonControl = doc.CreateElement("Control");
            string submitButtonGuid = Guid.NewGuid().ToString();
            submitButtonControl.SetAttribute("ID", submitButtonGuid);
            cell2.AppendChild(submitButtonControl);
            cells.AppendChild(cell2);
            buttonGuids["SubmitButton"] = submitButtonGuid;

            row.AppendChild(cells);
            rows.AppendChild(row);
            tableControl.AppendChild(rows);
            canvas.AppendChild(tableControl);
            areaItem.AppendChild(canvas);
            items.AppendChild(areaItem);
            buttonArea.AppendChild(items);

            return buttonArea;
        }

        private void AddButtonControlsToForm(XmlDocument doc, string formName, Dictionary<string, string> buttonGuids)
        {
            // Find the Controls element in the form
            XmlNodeList controlsNodes = doc.GetElementsByTagName("Controls");
            if (controlsNodes.Count == 0) return;

            XmlElement controls = (XmlElement)controlsNodes[0];

            // Add Table control
            XmlElement tableControl = CreateTableControl(doc, buttonGuids["Table"], "Form Action Table");
            controls.AppendChild(tableControl);

            // Add Cell controls
            XmlElement cell1Control = CreateCellControl(doc, buttonGuids["Cell1"], "Cell");
            controls.AppendChild(cell1Control);

            XmlElement cell2Control = CreateCellControl(doc, buttonGuids["Cell2"], "Cell1", true); // Right aligned
            controls.AppendChild(cell2Control);

            // Add Row control
            XmlElement rowControl = CreateRowControl(doc, buttonGuids["Row"], "Row");
            controls.AppendChild(rowControl);

            // Add Column controls
            XmlElement column1Control = CreateColumnControl(doc, buttonGuids["Column1"], "Column");
            controls.AppendChild(column1Control);

            XmlElement column2Control = CreateColumnControl(doc, buttonGuids["Column2"], "Column1");
            controls.AppendChild(column2Control);

            // Add AreaItem control
            XmlElement areaItemControl = CreateButtonAreaItemControl(doc, buttonGuids["AreaItem"], $"{formName} Area Item");
            controls.AppendChild(areaItemControl);

            // Add Clear button
            XmlElement clearButton = CreateButtonControl(doc, buttonGuids["ClearButton"], "Clear Form Button", "Clear");
            controls.AppendChild(clearButton);

            // Add Submit button
            XmlElement submitButton = CreateButtonControl(doc, buttonGuids["SubmitButton"], "Submit", "Submit");
            controls.AppendChild(submitButton);
        }

        private XmlElement CreateTableControl(XmlDocument doc, string guid, string name)
        {
            XmlElement control = doc.CreateElement("Control");
            control.SetAttribute("ID", guid);
            control.SetAttribute("Type", "Table");

            XmlHelper.AddElement(doc, control, "Name", name);
            XmlHelper.AddElement(doc, control, "DisplayName", name);

            XmlElement properties = doc.CreateElement("Properties");
            XmlElement prop = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, prop, "Name", "ControlName");
            XmlHelper.AddElement(doc, prop, "Value", name);
            XmlHelper.AddElement(doc, prop, "DisplayValue", name);
            XmlHelper.AddElement(doc, prop, "NameValue", name);
            properties.AppendChild(prop);
            control.AppendChild(properties);

            XmlElement styles = doc.CreateElement("Styles");
            XmlElement style = doc.CreateElement("Style");
            style.SetAttribute("IsDefault", "True");
            styles.AppendChild(style);
            control.AppendChild(styles);

            return control;
        }

        private XmlElement CreateCellControl(XmlDocument doc, string guid, string name, bool rightAlign = false)
        {
            XmlElement control = doc.CreateElement("Control");
            control.SetAttribute("ID", guid);
            control.SetAttribute("Type", "Cell");

            XmlHelper.AddElement(doc, control, "Name", name);

            XmlElement properties = doc.CreateElement("Properties");
            XmlElement prop = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, prop, "Name", "ControlName");
            XmlHelper.AddElement(doc, prop, "Value", name);
            XmlHelper.AddElement(doc, prop, "DisplayValue", name);
            XmlHelper.AddElement(doc, prop, "NameValue", name);
            properties.AppendChild(prop);
            control.AppendChild(properties);

            XmlElement styles = doc.CreateElement("Styles");
            XmlElement style = doc.CreateElement("Style");
            style.SetAttribute("IsDefault", "True");

            if (rightAlign)
            {
                XmlElement text = doc.CreateElement("Text");
                XmlElement align = doc.CreateElement("Align");
                align.InnerText = "right";
                text.AppendChild(align);
                style.AppendChild(text);
            }

            styles.AppendChild(style);
            control.AppendChild(styles);

            return control;
        }

        private XmlElement CreateRowControl(XmlDocument doc, string guid, string name)
        {
            XmlElement control = doc.CreateElement("Control");
            control.SetAttribute("ID", guid);
            control.SetAttribute("Type", "Row");

            XmlHelper.AddElement(doc, control, "Name", name);

            XmlElement properties = doc.CreateElement("Properties");
            XmlElement prop = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, prop, "Name", "ControlName");
            XmlHelper.AddElement(doc, prop, "Value", name);
            XmlHelper.AddElement(doc, prop, "DisplayValue", name);
            XmlHelper.AddElement(doc, prop, "NameValue", name);
            properties.AppendChild(prop);
            control.AppendChild(properties);

            return control;
        }

        private XmlElement CreateColumnControl(XmlDocument doc, string guid, string name)
        {
            XmlElement control = doc.CreateElement("Control");
            control.SetAttribute("ID", guid);
            control.SetAttribute("Type", "Column");

            XmlElement properties = doc.CreateElement("Properties");

            XmlElement nameProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, nameProp, "Name", "ControlName");
            XmlHelper.AddElement(doc, nameProp, "Value", name);
            XmlHelper.AddElement(doc, nameProp, "NameValue", name);
            XmlHelper.AddElement(doc, nameProp, "DisplayValue", name);
            properties.AppendChild(nameProp);

            XmlElement sizeProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, sizeProp, "Name", "Size");
            XmlHelper.AddElement(doc, sizeProp, "Value", "1fr");
            XmlHelper.AddElement(doc, sizeProp, "NameValue", "1fr");
            XmlHelper.AddElement(doc, sizeProp, "DisplayValue", "1fr");
            properties.AppendChild(sizeProp);

            control.AppendChild(properties);

            XmlHelper.AddElement(doc, control, "Name", name);
            XmlHelper.AddElement(doc, control, "DisplayName", name);
            XmlHelper.AddElement(doc, control, "NameValue", name);

            return control;
        }

        private XmlElement CreateButtonAreaItemControl(XmlDocument doc, string guid, string name)
        {
            XmlElement control = doc.CreateElement("Control");
            control.SetAttribute("ID", guid);
            control.SetAttribute("Type", "AreaItem");

            XmlHelper.AddElement(doc, control, "Name", name);

            XmlElement properties = doc.CreateElement("Properties");
            XmlElement prop = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, prop, "Name", "ControlName");
            XmlHelper.AddElement(doc, prop, "Value", name);
            properties.AppendChild(prop);
            control.AppendChild(properties);

            return control;
        }

        private XmlElement CreateButtonControl(XmlDocument doc, string guid, string name, string text)
        {
            var builder = ButtonBuilder.Create(doc, guid)
                .WithName(name)
                .WithText(text);

            // Apply K2-specific button styles based on button type
            if (name.Contains("Clear"))
            {
                builder.WithProperty("ButtonStyle", "quietaction")
                       .WithStyle(ButtonStyle.Secondary);
            }
            else if (name.Contains("Submit"))
            {
                builder.WithProperty("ButtonStyle", "mainaction")
                       .WithStyle(ButtonStyle.Primary);
            }

            return builder.Build();
        }
        private string ExtractRepeatingSectionName(string viewName)
        {
            // This is for general use when we don't have the InfoPath view context
            if (viewName.Contains("_List"))
            {
                int listIndex = viewName.LastIndexOf("_List", StringComparison.Ordinal);
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
                int itemIndex = viewName.LastIndexOf("_Item", StringComparison.Ordinal);
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

        private bool CheckViewExists(string viewName)
        {
            using (FormsManager formsManager = new FormsManager())
            {
                try
                {
                    formsManager.CreateConnection();
                    formsManager.Connection.Open(_connectionManager.ConnectionString.ConnectionString);

                    var viewInfo = formsManager.GetViewDefinition(viewName);
                    return viewInfo != null;
                }
                catch
                {
                    return false;
                }
            }
        }

        private void GenerateForm(FormDefinition formDef)
        {
            Console.WriteLine($"\n=== Generating Form: {formDef.FormName} ===");
            Console.WriteLine($"    Category: {formDef.CategoryPath}");
            Console.WriteLine($"    Views to include: {formDef.ViewNames.Count}");
            Console.WriteLine($"    Theme: {_formTheme}");

            if (formDef.ViewNames.Count == 0)
            {
                Console.WriteLine("    WARNING: No views to add to form, skipping...");
                return;
            }

            using (FormsManager formsManager = new FormsManager())
            {
                try
                {
                    formsManager.CreateConnection();
                    formsManager.Connection.Open(_connectionManager.ConnectionString.ConnectionString);

                    // Check if form already exists and delete it
                    try
                    {
                        var existingForm = formsManager.GetFormDefinition(formDef.FormName);
                        if (existingForm != null)
                        {
                            formsManager.DeleteForm(formDef.FormName);
                            Console.WriteLine($"    Deleted existing form: {formDef.FormName}");
                        }
                    }
                    catch { }

                    // Set up form options - keeping it simple with basic options
                    FormBehaviorOption fbOptions = FormBehaviorOption.LoadFormListClick |
                                                  FormBehaviorOption.RefreshListFormLoad;

                    // Use NoTabs for all forms to keep it simple
                    FormGenerationOption fgOptions = FormGenerationOption.NoTabs;

                    // Use AutoGenerator to create the form
                    using (AutoGenerator autoGenerator = new AutoGenerator(formsManager.Connection))
                    {
                        // Create FormGenerator with options
                        SourceCode.Forms.Utilities.FormGenerator formGenerator =
                            new SourceCode.Forms.Utilities.FormGenerator(fgOptions, fbOptions, _formTheme);

                        // Generate the form with the specified views
                        Form generatedForm = autoGenerator.Generate(formGenerator,
                            formDef.ViewNames.ToArray(), formDef.FormName);

                        // Get the form's XML to modify it
                        string formXml = generatedForm.ToXml();
                        XmlDocument formDoc = new XmlDocument();
                        formDoc.LoadXml(formXml);

                        // ADD ID PARAMETER HERE - Before any other modifications
                        AddIDParameterToForm(formDoc);
                        Console.WriteLine($"    Added ID parameter to form: {formDef.FormName}");

                        // For forms WITHOUT repeating sections, add form-level rules here
                        if (!formDef.HasRepeatingSections)
                        {
                            Console.WriteLine("\n    === Processing Simple Form (No Repeating Sections) ===");

                            // First, we need to add the Clear and Submit buttons to the form
                            AddFormActionButtonsToSimpleForm(formDoc, formDef.FormName);

                            // Now add the form-level rules
                            Console.WriteLine("    === Adding Form-Level Rules (Clear & Submit) ===");

                            // Get or create form ID
                            string formId = null;
                            XmlNodeList formElements = formDoc.GetElementsByTagName("Form");
                            if (formElements.Count > 0)
                            {
                                XmlElement formElement = (XmlElement)formElements[0];
                                formId = formElement.GetAttribute("ID");

                                if (string.IsNullOrEmpty(formId))
                                {
                                    formId = Guid.NewGuid().ToString();
                                    formElement.SetAttribute("ID", formId);
                                    Console.WriteLine($"      Generated new Form ID: {formId}");
                                }
                                else
                                {
                                    Console.WriteLine($"      Found existing Form ID: {formId}");
                                }
                            }

                            // Call AddFormLevelRules with empty viewPairs for simple forms
                            var emptyViewPairs = new Dictionary<string, ViewPairInfo>();
                            _rulesBuilder.AddFormLevelRules(formDoc, formId, formDef.FormName, emptyViewPairs);
                            Console.WriteLine("      Form-level rules added to simple form");
                        }

                        // Create a new form object from the modified XML
                        generatedForm = new Form(formDoc.OuterXml);

                        // Restructure the form to group list and item views together
                        if (formDef.HasRepeatingSections)
                        {
                            Console.WriteLine($"    Restructuring form to group list and item views...");
                            generatedForm = RestructureFormToGroupListAndItemViews(generatedForm, formDef);
                            // Note: Form-level rules are added inside RestructureFormToGroupListAndItemViews for complex forms
                        }

                        // Deploy the form with the ID parameter and all rules
                        Console.WriteLine($"    [DEPLOY] About to call DeployForms:");
                        Console.WriteLine($"    [DEPLOY]   Form name: {formDef.FormName}");
                        Console.WriteLine($"    [DEPLOY]   Category path: {formDef.CategoryPath}");
                        Console.WriteLine($"    [DEPLOY]   Form XML length: {generatedForm.ToXml().Length} chars");

                        formsManager.DeployForms(generatedForm.ToXml(), formDef.CategoryPath, true);

                        Console.WriteLine($"    [DEPLOY] DeployForms call completed successfully");
                        Console.WriteLine($"    Successfully deployed form: {formDef.FormName} with ID parameter and rules");
                        Console.WriteLine($"    Location: {formDef.CategoryPath}");

                        // Log the views included, categorizing them
                        Console.WriteLine($"    Views included:");
                        foreach (string viewName in formDef.ViewNames)
                        {
                            string viewType = "Main";
                            if (viewName.Contains("_Item"))
                                viewType = "Item";
                            else if (viewName.Contains("_List"))
                                viewType = "List (Shared)";
                            else if (viewName.Contains("_Part"))
                                viewType = "Part";

                            Console.WriteLine($"      - {viewName} [{viewType}]");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"    ERROR: Failed to generate form {formDef.FormName}: {ex.Message}");
                    Console.WriteLine($"    Stack trace: {ex.StackTrace}");
                }
            }
        }

        // Helper method to add Clear and Submit buttons to simple forms
        private void AddFormActionButtonsToSimpleForm(XmlDocument doc, string formName)
        {
            Console.WriteLine("    === Adding Form Action Buttons to Simple Form ===");

            // Find the first panel in the form
            XmlNodeList panels = doc.GetElementsByTagName("Panel");
            if (panels.Count == 0)
            {
                Console.WriteLine("      WARNING: No panels found to add buttons");
                return;
            }

            XmlElement panel = (XmlElement)panels[0];

            // Check if Areas element exists
            XmlNodeList areasNodes = panel.GetElementsByTagName("Areas");
            XmlElement areasContainer = null;

            if (areasNodes.Count == 0)
            {
                areasContainer = doc.CreateElement("Areas");
                panel.AppendChild(areasContainer);
            }
            else
            {
                areasContainer = (XmlElement)areasNodes[0];
            }

            // Create the button area
            Dictionary<string, string> buttonGuids;
            XmlElement buttonArea = CreateButtonArea(doc, formName, out buttonGuids);
            areasContainer.AppendChild(buttonArea);

            // Add button controls to the Controls section
            if (buttonGuids.Count > 0)
            {
                AddButtonControlsToForm(doc, formName, buttonGuids);
                Console.WriteLine("      Added Clear and Submit buttons to form");
            }
        }
        public bool TryCleanupExistingForms(string jsonContent)
        {
            bool allCleaned = true;

            JObject formData = JObject.Parse(jsonContent);
            string rootName = formData.Properties().First().Name;
            string baseFormName = rootName.Replace(" ", "_");
            JObject formDefinition = formData[rootName] as JObject;

            JArray viewsArray = formDefinition?["FormDefinition"]?["Views"] as JArray;
            if (viewsArray != null)
            {
                using (FormsManager formsManager = new FormsManager())
                {
                    try
                    {
                        formsManager.CreateConnection();
                        formsManager.Connection.Open(_connectionManager.ConnectionString.ConnectionString);

                        // Delete forms for each InfoPath view
                        foreach (JObject viewDef in viewsArray)
                        {
                            string viewName = viewDef["ViewName"]?.Value<string>()?.Replace(".xsl", "");
                            if (!string.IsNullOrEmpty(viewName))
                            {
                                string formName = $"{baseFormName}_{viewName}_Form";
                                try
                                {
                                    formsManager.DeleteForm(formName);
                                    Console.WriteLine($"  Cleaned up form: {formName}");
                                }
                                catch (Exception ex)
                                {
                                    if (!ex.Message.Contains("does not exist"))
                                    {
                                        Console.WriteLine($"  Could not delete form {formName}: {ex.Message}");
                                        allCleaned = false;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  Warning during form cleanup: {ex.Message}");
                        allCleaned = false;
                    }
                }
            }

            return allCleaned;
        }

        private class ViewInfo
        {
            public string Name { get; set; }
            public string ViewId { get; set; }
            public string InstanceId { get; set; }
        }

        private class OrderedView
        {
            public int Position { get; set; }
            public XmlElement View { get; set; }
            public string ViewName { get; set; }
            public bool IsPartView { get; set; }
        }


        private class ViewWithPosition
        {
            public XmlElement View { get; set; }
            public int Position { get; set; }
            public string ViewName { get; set; }
        }

        private class AreaWithPosition
        {
            public XmlElement Area { get; set; }
            public int Position { get; set; }
            public bool IsRepeatingSection { get; set; }
            public string SectionName { get; set; }
            public string ViewName { get; set; }
        }

        private class RepeatingSectionPosition
        {
            public int GridRow { get; set; }
            public string GridPosition { get; set; }
            public string OriginalName { get; set; }
            public string DisplayName { get; set; }
        }

        private class RepeatingSectionAreaResult
        {
            public XmlElement Area { get; set; }
            public string ItemViewName { get; set; }
            public string ListViewName { get; set; }
        }

        private class OrderedArea
        {
            public int Order { get; set; }
            public string SectionName { get; set; }
            public XmlElement View { get; set; }
            public bool IsRepeatingSection { get; set; }
            public string ViewName { get; set; }
        }

        private class ViewWithGridPosition
        {
            public XmlElement View { get; set; }
            public string ViewName { get; set; }
            public int GridPosition { get; set; }
            public bool IsRepeatingSection { get; set; }
            public string SectionName { get; set; }
        }

        // Helper class to track form definitions
        private class FormDefinition
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
    }


}