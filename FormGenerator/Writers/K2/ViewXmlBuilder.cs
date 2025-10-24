using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Newtonsoft.Json.Linq;
using SourceCode.SmartObjects.Authoring;
using K2SmartObjectGenerator.Models;
using K2SmartObjectGenerator.Utilities;
using K2SmartObjectGenerator.Config;
using static K2SmartObjectGenerator.ViewGenerator;

namespace K2SmartObjectGenerator
{
    public class ViewXmlBuilder
    {
        private readonly ServerConnectionManager _connectionManager;
        private readonly Dictionary<string, Dictionary<string, FieldInfo>> _smoFieldMappings;
        private readonly SmartObjectGenerator _smoGenerator;
        private readonly ViewRulesBuilder _rulesBuilder;
        private readonly GeneratorConfiguration _config;
        private Dictionary<string, string> _jsonToK2ControlIdMap;
        private HashSet<string> _usedControlNames;
        private int _controlCounter;
        private JObject _conditionalVisibility;
        private JArray _dynamicSections;

        public ViewXmlBuilder(ServerConnectionManager connectionManager,
                             Dictionary<string, Dictionary<string, FieldInfo>> smoFieldMappings,
                             SmartObjectGenerator smoGenerator,
                             GeneratorConfiguration config = null)
        {
            _connectionManager = connectionManager;
            _smoFieldMappings = smoFieldMappings;
            _smoGenerator = smoGenerator;
            _config = config ?? GeneratorConfiguration.CreateDefault();
            _rulesBuilder = new ViewRulesBuilder();
            _jsonToK2ControlIdMap = new Dictionary<string, string>();
            _usedControlNames = new HashSet<string>();
            _controlCounter = 1;
        }

        public XmlDocument CreateViewXmlStructure(string viewName, string smoGuid, string smoName,
                             JArray controls, JArray dataArray,
                             JArray dynamicSections, JObject conditionalVisibility,
                             bool isItemView, out string viewTitle)
        {
            // Store conditional visibility data for use during control creation
            _conditionalVisibility = conditionalVisibility;
            _dynamicSections = dynamicSections;

            XmlDocument doc = new XmlDocument();
            doc.PreserveWhitespace = false;

            // STEP 1: Compact all rows to remove any empty rows FIRST
            JArray compactedControls = CompactControlRows(controls);

            // STEP 2: Extract title from the compacted controls (now row 1 is the actual first content row)
            viewTitle = DetectAndExtractTitleLabel(compactedControls);

            // STEP 3: Remove title label if found
            JArray finalControls = compactedControls;
            if (!string.IsNullOrEmpty(viewTitle))
            {
                finalControls = RemoveTitleLabel(compactedControls);
                Console.WriteLine($"    Extracted view title: '{viewTitle}' and removed title label from view");
            }

            // Continue with the rest of the existing code using finalControls
            XmlElement root = doc.CreateElement("SourceCode.Forms");
            root.SetAttribute("Version", "29");
            doc.AppendChild(root);

            XmlElement views = doc.CreateElement("Views");
            root.AppendChild(views);

            XmlElement view = doc.CreateElement("View");
            string viewGuid = Guid.NewGuid().ToString();
            view.SetAttribute("ID", viewGuid);
            view.SetAttribute("Type", "Capture");
            view.SetAttribute("RenderVersion", "3");
            view.SetAttribute("IsUserModified", "True");
            view.SetAttribute("SOID", smoGuid);
            views.AppendChild(view);

            XmlHelper.AddElement(doc, view, "Name", viewName);

            // Track control and field information
            Dictionary<string, string> controlIdMap = new Dictionary<string, string>();
            Dictionary<string, FieldInfo> fieldMap = new Dictionary<string, FieldInfo>();
            Dictionary<string, string> controlToFieldMap = new Dictionary<string, string>();
            Dictionary<string, LookupInfo> lookupSmartObjects = new Dictionary<string, LookupInfo>();

            // Reset counters for this view
            _usedControlNames.Clear();
            _controlCounter = 1;

            // Create controls section - USE FINAL CONTROLS
            XmlElement controlsElement = CreateControlsSection(doc, viewGuid, viewName, smoName,
                finalControls, dataArray, controlIdMap, fieldMap, controlToFieldMap, lookupSmartObjects);
            view.AppendChild(controlsElement);

            // Create canvas - USE FINAL CONTROLS
            XmlElement canvas = CreateCanvasStructure(doc, finalControls, controlIdMap);
            view.AppendChild(canvas);

            // Add buttons only to item views
            if (isItemView)
            {
                AddItemViewButtons(doc, controlsElement, canvas, controlIdMap);
            }

            // Create sources
            XmlElement sources = CreateSourcesSection(doc, smoGuid, smoName, fieldMap, lookupSmartObjects);
            view.AppendChild(sources);

            // Create translations - USE FINAL CONTROLS
            XmlElement translations = CreateTranslationsSection(doc, viewName, finalControls);
            view.AppendChild(translations);

            // Create events section WITH visibility rules - USE FINAL CONTROLS
            XmlElement events = _rulesBuilder.CreateEventsWithRules(doc, viewGuid, viewName, controlIdMap,
                controlToFieldMap, fieldMap, lookupSmartObjects, dynamicSections, conditionalVisibility,
                finalControls, _jsonToK2ControlIdMap);
            view.AppendChild(events);

            // Add empty but required sections
            view.AppendChild(doc.CreateElement("Expressions"));

            // Create Parameters section with ID parameter
            XmlElement parameters = CreateParametersSection(doc);
            view.AppendChild(parameters);

            XmlHelper.AddElement(doc, view, "Description", "");
            // Use extracted view title if available, otherwise fall back to technical view name
            string displayName = !string.IsNullOrEmpty(viewTitle) ? viewTitle : viewName;
            XmlHelper.AddElement(doc, view, "DisplayName", displayName);

            return doc;
        }

        private JArray RemoveTitleLabel(JArray controls)
        {
            // First, check if we have a title label on row 1
            bool hasRow1Title = false;
            JObject titleControl = null;

            foreach (JObject control in controls)
            {
                string gridPos = control["GridPosition"]?.Value<string>();
                if (string.IsNullOrEmpty(gridPos))
                    continue;

                int rowNum = ExtractRowNumber(gridPos);
                string type = control["Type"]?.Value<string>()?.ToLower();

                if (rowNum == 1 && type == "label")
                {
                    // Check if this is the only control on row 1
                    int row1ControlCount = controls.Count(c =>
                    {
                        string pos = c["GridPosition"]?.Value<string>();
                        return !string.IsNullOrEmpty(pos) && ExtractRowNumber(pos) == 1;
                    });

                    if (row1ControlCount == 1)
                    {
                        hasRow1Title = true;
                        titleControl = control;
                        break;
                    }
                }
            }

            if (!hasRow1Title || titleControl == null)
                return controls; // No title to remove

            Console.WriteLine("        Removing title label from row 1 and shifting all rows up");

            // Remove the title control and shift all other rows up by 1
            JArray filteredControls = new JArray();

            foreach (JObject control in controls)
            {
                // Skip the title control
                if (control == titleControl)
                    continue;

                JObject adjustedControl = (JObject)control.DeepClone();
                string gridPos = adjustedControl["GridPosition"]?.Value<string>();

                if (!string.IsNullOrEmpty(gridPos))
                {
                    int rowNum = ExtractRowNumber(gridPos);

                    // Shift all rows up by 1
                    if (rowNum > 1)
                    {
                        string column = ExtractColumnLetter(gridPos);
                        int newRow = rowNum - 1;
                        adjustedControl["GridPosition"] = $"{newRow}{column}";

                        // Also adjust any SectionInfo if present
                        JObject sectionInfo = adjustedControl["SectionInfo"] as JObject;
                        if (sectionInfo != null)
                        {
                            int? startRow = sectionInfo["StartRow"]?.Value<int>();
                            int? endRow = sectionInfo["EndRow"]?.Value<int>();

                            if (startRow.HasValue && startRow.Value > 1)
                                sectionInfo["StartRow"] = startRow.Value - 1;
                            if (endRow.HasValue && endRow.Value > 1)
                                sectionInfo["EndRow"] = endRow.Value - 1;
                        }
                    }
                }

                filteredControls.Add(adjustedControl);
            }

            return filteredControls;
        }

        // Add the CompactControlRows method to remove empty rows
        private JArray CompactControlRows(JArray controls)
        {
            // Find all rows that have controls
            HashSet<int> rowsWithControls = new HashSet<int>();

            foreach (JObject control in controls)
            {
                string gridPos = control["GridPosition"]?.Value<string>();
                if (!string.IsNullOrEmpty(gridPos))
                {
                    int rowNum = ExtractRowNumber(gridPos);
                    rowsWithControls.Add(rowNum);
                }
            }

            if (rowsWithControls.Count == 0)
            {
                Console.WriteLine("        No controls with grid positions found");
                return controls;
            }

            // Create mapping from old row numbers to new consecutive row numbers
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
                Console.WriteLine("        No empty rows found, no compaction needed");
                return controls;
            }

            Console.WriteLine($"        Compacting rows to remove empty rows:");
            foreach (var mapping in oldRowToNewRow.Where(m => m.Key != m.Value))
            {
                Console.WriteLine($"          Row {mapping.Key} -> Row {mapping.Value}");
            }

            // Create new array with adjusted positions
            JArray compactedControls = new JArray();

            foreach (JObject control in controls)
            {
                JObject compactedControl = (JObject)control.DeepClone();
                string gridPos = compactedControl["GridPosition"]?.Value<string>();

                if (!string.IsNullOrEmpty(gridPos))
                {
                    int oldRowNum = ExtractRowNumber(gridPos);

                    if (oldRowToNewRow.ContainsKey(oldRowNum))
                    {
                        int newRowNum = oldRowToNewRow[oldRowNum];
                        string column = ExtractColumnLetter(gridPos);
                        string newGridPos = $"{newRowNum}{column}";

                        compactedControl["GridPosition"] = newGridPos;

                        // Also adjust any SectionInfo if present
                        JObject sectionInfo = compactedControl["SectionInfo"] as JObject;
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

                compactedControls.Add(compactedControl);
            }

            Console.WriteLine($"        Compacted {rowsWithControls.Count} rows with controls into {oldRowToNewRow.Count} consecutive rows");

            return compactedControls;
        }
        private XmlElement CreateParametersSection(XmlDocument doc)
        {
            XmlElement parameters = doc.CreateElement("Parameters");

            // Create ID parameter
            XmlElement idParameter = doc.CreateElement("Parameter");
            string parameterGuid = Guid.NewGuid().ToString();
            idParameter.SetAttribute("ID", parameterGuid);
            idParameter.SetAttribute("DataType", "Text");

            XmlHelper.AddElement(doc, idParameter, "Name", "ID");
            XmlHelper.AddElement(doc, idParameter, "DisplayName", "ID");

            parameters.AppendChild(idParameter);

            return parameters;
        }
        // Complete CreateControlsSection Method with Empty Row Fix

        private XmlElement CreateControlsSection(XmlDocument doc, string viewGuid, string viewName,
                                        string smoName, JArray controls, JArray dataArray,
                                        Dictionary<string, string> controlIdMap,
                                        Dictionary<string, FieldInfo> fieldMap,
                                        Dictionary<string, string> controlToFieldMap,
                                        Dictionary<string, LookupInfo> lookupSmartObjects)
        {
            XmlElement controlsElement = doc.CreateElement("Controls");

            // Create View control
            XmlElement viewControl = doc.CreateElement("Control");
            viewControl.SetAttribute("ID", viewGuid);
            viewControl.SetAttribute("Type", "View");

            XmlHelper.AddElement(doc, viewControl, "DisplayName", viewName);

            XmlElement viewProps = doc.CreateElement("Properties");
            XmlElement propControlName = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, propControlName, "Name", "ControlName");
            XmlHelper.AddElement(doc, propControlName, "DisplayValue", viewName);
            XmlHelper.AddElement(doc, propControlName, "Value", viewName);
            viewProps.AppendChild(propControlName);
            viewControl.AppendChild(viewProps);

            XmlElement viewStyles = doc.CreateElement("Styles");
            XmlElement viewStyle = doc.CreateElement("Style");
            viewStyle.SetAttribute("IsDefault", "True");
            viewStyles.AppendChild(viewStyle);
            viewControl.AppendChild(viewStyles);

            XmlHelper.AddElement(doc, viewControl, "Name", viewName);
            controlsElement.AppendChild(viewControl);

            // Create Table control
            string tableGuid = Guid.NewGuid().ToString();
            XmlElement tableControl = doc.CreateElement("Control");
            tableControl.SetAttribute("ID", tableGuid);
            tableControl.SetAttribute("Type", "Table");

            XmlElement tableProps = doc.CreateElement("Properties");
            XmlElement tableProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, tableProp, "Name", "ControlName");
            XmlHelper.AddElement(doc, tableProp, "DisplayValue", "Table");
            XmlHelper.AddElement(doc, tableProp, "NameValue", "Table");
            XmlHelper.AddElement(doc, tableProp, "Value", "Table");
            tableProps.AppendChild(tableProp);
            tableControl.AppendChild(tableProps);

            XmlElement tableStyles = doc.CreateElement("Styles");
            XmlElement tableStyle = doc.CreateElement("Style");
            tableStyle.SetAttribute("IsDefault", "True");
            tableStyles.AppendChild(tableStyle);
            tableControl.AppendChild(tableStyles);

            XmlHelper.AddElement(doc, tableControl, "Name", "Table");
            XmlHelper.AddElement(doc, tableControl, "DisplayName", "Table");
            XmlHelper.AddElement(doc, tableControl, "NameValue", "Table");
            controlsElement.AppendChild(tableControl);
            controlIdMap["Table"] = tableGuid;

            // Create Section control
            string sectionGuid = Guid.NewGuid().ToString();
            XmlElement sectionControl = doc.CreateElement("Control");
            sectionControl.SetAttribute("ID", sectionGuid);
            sectionControl.SetAttribute("Type", "Section");

            XmlElement sectionProps = doc.CreateElement("Properties");
            XmlElement typeProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, typeProp, "Name", "Type");
            XmlHelper.AddElement(doc, typeProp, "Value", "Body");
            XmlHelper.AddElement(doc, typeProp, "NameValue", "Body");
            XmlHelper.AddElement(doc, typeProp, "DisplayValue", "Body");
            sectionProps.AppendChild(typeProp);

            XmlElement sectionNameProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, sectionNameProp, "Name", "ControlName");
            XmlHelper.AddElement(doc, sectionNameProp, "DisplayValue", "Section");
            XmlHelper.AddElement(doc, sectionNameProp, "Value", "Section");
            sectionProps.AppendChild(sectionNameProp);
            sectionControl.AppendChild(sectionProps);

            XmlHelper.AddElement(doc, sectionControl, "Styles", "");
            XmlHelper.AddElement(doc, sectionControl, "Name", "Section");
            XmlHelper.AddElement(doc, sectionControl, "DisplayName", "Section");
            XmlHelper.AddElement(doc, sectionControl, "NameValue", "Section");
            controlsElement.AppendChild(sectionControl);
            controlIdMap["Section"] = sectionGuid;

            // DYNAMIC COLUMN CREATION - Determine how many columns we need
            int maxColumn = 4; // Default minimum of 4 columns
            foreach (JObject control in controls)
            {
                string gridPos = control["GridPosition"]?.Value<string>();
                if (!string.IsNullOrEmpty(gridPos))
                {
                    int colNum = ExtractColumnNumber(gridPos);
                    if (colNum + 1 > maxColumn)
                    {
                        maxColumn = colNum + 1;
                        Console.WriteLine($"        Found control at column {GetColumnLetter(colNum)}, expanding to {maxColumn} columns");
                    }
                }
            }

            Console.WriteLine($"        Creating {maxColumn} columns for the table");

            // Create Column controls dynamically based on maxColumn
            for (int i = 0; i < maxColumn; i++)
            {
                string columnGuid = Guid.NewGuid().ToString();
                XmlElement columnControl = doc.CreateElement("Control");
                columnControl.SetAttribute("ID", columnGuid);
                columnControl.SetAttribute("Type", "Column");

                XmlElement columnProps = doc.CreateElement("Properties");
                XmlElement columnNameProp = doc.CreateElement("Property");
                XmlHelper.AddElement(doc, columnNameProp, "Name", "ControlName");
                XmlHelper.AddElement(doc, columnNameProp, "DisplayValue", $"Column{i}");
                XmlHelper.AddElement(doc, columnNameProp, "NameValue", $"Column{i}");
                XmlHelper.AddElement(doc, columnNameProp, "Value", $"Column{i}");
                columnProps.AppendChild(columnNameProp);

                XmlElement sizeProp = doc.CreateElement("Property");
                XmlHelper.AddElement(doc, sizeProp, "Name", "Size");
                XmlHelper.AddElement(doc, sizeProp, "Value", "1fr");
                XmlHelper.AddElement(doc, sizeProp, "NameValue", "1fr");
                XmlHelper.AddElement(doc, sizeProp, "DisplayValue", "1fr");
                columnProps.AppendChild(sizeProp);
                columnControl.AppendChild(columnProps);

                XmlElement columnStyles = doc.CreateElement("Styles");
                XmlElement columnStyle = doc.CreateElement("Style");
                columnStyle.SetAttribute("IsDefault", "True");
                columnStyles.AppendChild(columnStyle);
                columnControl.AppendChild(columnStyles);

                XmlHelper.AddElement(doc, columnControl, "Name", $"Column{i}");
                XmlHelper.AddElement(doc, columnControl, "DisplayName", $"Column{i}");
                XmlHelper.AddElement(doc, columnControl, "NameValue", $"Column{i}");
                controlsElement.AppendChild(columnControl);
                controlIdMap[$"Column{i}"] = columnGuid;
            }

            // Now the controls are already compacted, so we just need to find the max row
            int maxRow = 1;
            foreach (JObject control in controls)
            {
                string gridPos = control["GridPosition"]?.Value<string>();
                if (!string.IsNullOrEmpty(gridPos))
                {
                    int rowNum = ExtractRowNumber(gridPos);
                    if (rowNum > maxRow)
                        maxRow = rowNum;
                }
            }

            // Create Row and Cell controls for all rows from 1 to maxRow (they're already compacted)
            Dictionary<int, string> rowGuids = new Dictionary<int, string>();
            Dictionary<string, string> cellGuids = new Dictionary<string, string>();

            for (int rowNum = 1; rowNum <= maxRow; rowNum++)
            {
                string rowGuid = Guid.NewGuid().ToString();
                XmlElement rowControl = doc.CreateElement("Control");
                rowControl.SetAttribute("ID", rowGuid);
                rowControl.SetAttribute("Type", "Row");

                XmlElement rowProps = doc.CreateElement("Properties");
                XmlElement rowNameProp = doc.CreateElement("Property");
                XmlHelper.AddElement(doc, rowNameProp, "Name", "ControlName");
                XmlHelper.AddElement(doc, rowNameProp, "DisplayValue", $"Row{rowNum}");
                XmlHelper.AddElement(doc, rowNameProp, "Value", $"Row{rowNum}");
                rowProps.AppendChild(rowNameProp);
                rowControl.AppendChild(rowProps);

                XmlElement rowStyles = doc.CreateElement("Styles");
                XmlElement rowStyle = doc.CreateElement("Style");
                rowStyle.SetAttribute("IsDefault", "True");
                rowStyles.AppendChild(rowStyle);
                rowControl.AppendChild(rowStyles);

                XmlHelper.AddElement(doc, rowControl, "Name", $"Row{rowNum}");
                XmlHelper.AddElement(doc, rowControl, "DisplayName", $"Row{rowNum}");
                XmlHelper.AddElement(doc, rowControl, "NameValue", $"Row{rowNum}");
                controlsElement.AppendChild(rowControl);
                rowGuids[rowNum] = rowGuid;
                controlIdMap[$"Row{rowNum}"] = rowGuid;

                // Create cells for this row
                for (int c = 0; c < maxColumn; c++)
                {
                    string cellKey = $"{rowNum}_{c}";
                    string cellGuid = Guid.NewGuid().ToString();
                    XmlElement cellControl = doc.CreateElement("Control");
                    cellControl.SetAttribute("ID", cellGuid);
                    cellControl.SetAttribute("Type", "Cell");

                    XmlElement cellProps = doc.CreateElement("Properties");
                    XmlElement cellNameProp = doc.CreateElement("Property");
                    XmlHelper.AddElement(doc, cellNameProp, "Name", "ControlName");
                    XmlHelper.AddElement(doc, cellNameProp, "DisplayValue", $"Cell{cellKey}");
                    XmlHelper.AddElement(doc, cellNameProp, "NameValue", $"Cell{cellKey}");
                    XmlHelper.AddElement(doc, cellNameProp, "Value", $"Cell{cellKey}");
                    cellProps.AppendChild(cellNameProp);
                    cellControl.AppendChild(cellProps);

                    XmlElement cellStyles = doc.CreateElement("Styles");
                    XmlElement cellStyle = doc.CreateElement("Style");
                    cellStyle.SetAttribute("IsDefault", "True");
                    cellStyles.AppendChild(cellStyle);
                    cellControl.AppendChild(cellStyles);

                    XmlHelper.AddElement(doc, cellControl, "Name", $"Cell{cellKey}");
                    XmlHelper.AddElement(doc, cellControl, "DisplayName", $"Cell{cellKey}");
                    XmlHelper.AddElement(doc, cellControl, "NameValue", $"Cell{cellKey}");
                    controlsElement.AppendChild(cellControl);
                    cellGuids[cellKey] = cellGuid;
                    controlIdMap[$"Cell{cellKey}"] = cellGuid;
                }
            }

            // Clear the JSON to K2 control ID mapping for this view
            _jsonToK2ControlIdMap.Clear();

            // Create actual form controls
            HashSet<string> checkboxNames = new HashSet<string>();
            Dictionary<string, string> gridPositionToControlId = new Dictionary<string, string>();
            Dictionary<string, string> jsonControlIdToK2Id = new Dictionary<string, string>();

            // Dictionary to store control mappings for this view
            Dictionary<string, ViewGenerator.ControlMapping> viewControlMappings =
                new Dictionary<string, ViewGenerator.ControlMapping>();

            // First pass: identify all checkbox controls
            foreach (JObject control in controls)
            {
                string controlType = control["Type"]?.Value<string>();
                string name = control["Name"]?.Value<string>();

                if (controlType?.ToLower() == "checkbox" && !string.IsNullOrEmpty(name))
                {
                    checkboxNames.Add(name.ToUpper());
                }
            }

            // Second pass: create controls, skipping labels that correspond to checkboxes AND buttons in item views
            foreach (JObject control in controls)
            {
                string controlType = control["Type"]?.Value<string>();
                string name = control["Name"]?.Value<string>();
                string gridPosition = control["GridPosition"]?.Value<string>();
                string jsonCtrlId = control["CtrlId"]?.Value<string>();

                // NEW: Skip button controls for item views
                bool isItemView = viewName.Contains("_Item") ||
                                  (viewName.Contains("_") && !viewName.Contains("_List") && !viewName.Contains("_Part"));

                if (isItemView && controlType?.ToLower() == "button")
                {
                    // Check if this is an auto-generated button from nested table handling
                    bool isAutoGenerated = control["AdditionalProperties"]?["isAutoGenerated"]?.Value<string>() == "true";

                    if (!isAutoGenerated)
                    {
                        Console.WriteLine($"      FILTERING OUT button control: {name} of type: {controlType} at position: {gridPosition}");
                        Console.WriteLine($"        Reason: This is an item view ({viewName}) and buttons from JSON should not be added");
                        continue; // Skip this control entirely
                    }
                    else
                    {
                        Console.WriteLine($"      KEEPING auto-generated button control: {name} of type: {controlType} at position: {gridPosition}");
                        Console.WriteLine($"        Reason: This is an auto-generated button for nested table handling");
                    }
                }

                // Skip labels that have a corresponding checkbox
                if (controlType?.ToLower() == "label" && !string.IsNullOrEmpty(name))
                {
                    if (checkboxNames.Contains(name.ToUpper()))
                    {
                        Console.WriteLine($"      Skipping label for checkbox: {name} at position {gridPosition}");
                        continue;
                    }
                }

                Console.WriteLine($"      Processing control: {name} of type: {controlType} at position: {gridPosition}");
                if (!string.IsNullOrEmpty(jsonCtrlId))
                {
                    Console.WriteLine($"        JSON Control ID: {jsonCtrlId}");
                }

                XmlElement controlElement = CreateControlFromJson(doc, control, dataArray, smoName,
                    viewName, viewGuid, controlIdMap, fieldMap, controlToFieldMap, lookupSmartObjects, jsonCtrlId);

                if (controlElement != null)
                {
                    controlsElement.AppendChild(controlElement);

                    string k2ControlId = controlElement.GetAttribute("ID");
                    string k2ControlType = controlElement.GetAttribute("Type");

                    // Map JSON CtrlId to K2 control ID
                    if (!string.IsNullOrEmpty(jsonCtrlId))
                    {
                        _jsonToK2ControlIdMap[jsonCtrlId] = k2ControlId;
                        jsonControlIdToK2Id[jsonCtrlId] = k2ControlId;
                        Console.WriteLine($"        Mapped JSON CtrlId {jsonCtrlId} -> K2 ID {k2ControlId}");
                    }

                    // Store the control ID by its grid position
                    if (!string.IsNullOrEmpty(gridPosition))
                    {
                        gridPositionToControlId[gridPosition] = k2ControlId;
                        controlIdMap[gridPosition] = k2ControlId;
                        Console.WriteLine($"        Mapped grid position {gridPosition} -> {k2ControlId}");
                    }

                    // Map by field name
                    if (!string.IsNullOrEmpty(name))
                    {
                        controlIdMap[name] = k2ControlId;
                        string sanitizedName = NameSanitizer.SanitizePropertyName(name);
                        controlIdMap[sanitizedName] = k2ControlId;
                        controlIdMap[name.ToUpper()] = k2ControlId;
                        controlIdMap[sanitizedName.ToUpper()] = k2ControlId;
                        string nameNoUnderscore = name.Replace("_", "");
                        controlIdMap[nameNoUnderscore] = k2ControlId;
                        controlIdMap[nameNoUnderscore.ToUpper()] = k2ControlId;

                        // Track control mapping for data-bound controls
                        if (IsDataBoundControl(k2ControlType))
                        {
                            string fieldName = NameSanitizer.SanitizePropertyName(name);
                            string controlDisplayName = GetControlTypeDisplayName(k2ControlType);

                            // Get the actual control name from the control element
                            XmlNodeList nameNodes = controlElement.GetElementsByTagName("Name");
                            if (nameNodes.Count > 0)
                            {
                                controlDisplayName = nameNodes[0].InnerText;
                            }

                            viewControlMappings[fieldName] = new ViewGenerator.ControlMapping
                            {
                                ControlId = k2ControlId,
                                ControlName = controlDisplayName,
                                ControlType = k2ControlType,
                                FieldName = fieldName,
                                DataType = GetControlDataType(k2ControlType)
                            };

                            Console.WriteLine($"        Tracked control mapping: {fieldName} -> {controlDisplayName} (ID: {k2ControlId})");
                        }
                    }

                    Console.WriteLine($"        Added control to XML with ID: {k2ControlId}");
                }
                else
                {
                    Console.WriteLine($"        WARNING: Control was null for: {name}");
                }
            }

            // Store the JSON to K2 mappings in the main controlIdMap
            foreach (var mapping in jsonControlIdToK2Id)
            {
                controlIdMap[$"JSON_{mapping.Key}"] = mapping.Value;
            }

            // Register the control mappings for this view using the static service
            if (viewControlMappings.Count > 0)
            {
                ControlMappingService.RegisterViewControls(viewName, viewControlMappings);
                Console.WriteLine($"    Registered {viewControlMappings.Count} control mappings for view: {viewName}");
            }

            Console.WriteLine($"\n    Control mapping summary:");
            Console.WriteLine($"      Total controls created: {gridPositionToControlId.Count}");
            Console.WriteLine($"      JSON control IDs mapped: {jsonControlIdToK2Id.Count}");
            Console.WriteLine($"      Control names mapped: {controlIdMap.Count}");
            Console.WriteLine($"      Data controls tracked: {viewControlMappings.Count}");
            Console.WriteLine($"      Columns created: {maxColumn}");
            Console.WriteLine($"      Rows created: {maxRow}");

            return controlsElement;
        }
        private XmlElement CreateCanvasStructure(XmlDocument doc, JArray controls,
                                             Dictionary<string, string> controlIdMap)
        {
            XmlElement canvas = doc.CreateElement("Canvas");
            XmlElement sections = doc.CreateElement("Sections");

            XmlElement section = doc.CreateElement("Section");
            section.SetAttribute("ID", controlIdMap["Section"]);
            section.SetAttribute("Type", "Body");

            XmlElement tableControl = doc.CreateElement("Control");
            tableControl.SetAttribute("ID", controlIdMap["Table"]);
            tableControl.SetAttribute("LayoutType", "Grid");

            // Get the row mapping that was created in CreateControlsSection
            Dictionary<int, int> oldRowToNewRow = new Dictionary<int, int>();
            if (controlIdMap.ContainsKey("__ROW_MAPPING__"))
            {
                string mappingJson = controlIdMap["__ROW_MAPPING__"];
                oldRowToNewRow = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<int, int>>(mappingJson);
                Console.WriteLine($"        Using row mapping from CreateControlsSection");
            }
            else
            {
                // Fallback: create the mapping here if it doesn't exist
                HashSet<int> rowsWithControls = new HashSet<int>();
                foreach (JObject control in controls)
                {
                    string gridPos = control["GridPosition"]?.Value<string>();
                    if (!string.IsNullOrEmpty(gridPos))
                    {
                        int rowNum = ExtractRowNumber(gridPos);
                        rowsWithControls.Add(rowNum);
                    }
                }

                int newRowNumber = 1;
                foreach (int oldRow in rowsWithControls.OrderBy(r => r))
                {
                    oldRowToNewRow[oldRow] = newRowNumber++;
                }
                Console.WriteLine($"        Created fallback row mapping in CreateCanvasStructure");
            }

            // DYNAMIC COLUMN CREATION - Determine how many columns we need based on actual controls
            int maxColumn = 0;
            foreach (JObject control in controls)
            {
                string gridPos = control["GridPosition"]?.Value<string>();
                if (!string.IsNullOrEmpty(gridPos))
                {
                    int colNum = ExtractColumnNumber(gridPos);
                    if (colNum + 1 > maxColumn)
                    {
                        maxColumn = colNum + 1;
                    }
                }
            }

            // If no controls have positions, default to 1 column
            if (maxColumn == 0)
            {
                maxColumn = 1;
                Console.WriteLine("        WARNING: No controls with grid positions found, defaulting to 1 column");
            }

            Console.WriteLine($"        Canvas will have {maxColumn} columns and {oldRowToNewRow.Count} rows");

            // Add columns dynamically
            XmlElement columns = doc.CreateElement("Columns");
            for (int i = 0; i < maxColumn; i++)
            {
                XmlElement column = doc.CreateElement("Column");
                column.SetAttribute("ID", controlIdMap[$"Column{i}"]);
                column.SetAttribute("Size", "1fr");
                columns.AppendChild(column);
            }
            tableControl.AppendChild(columns);

            // Build a map of which controls are in which rows
            Dictionary<int, Dictionary<int, JObject>> rowColumnControlsMap = new Dictionary<int, Dictionary<int, JObject>>();

            foreach (JObject control in controls)
            {
                string gridPos = control["GridPosition"]?.Value<string>();
                if (!string.IsNullOrEmpty(gridPos))
                {
                    int oldRowNum = ExtractRowNumber(gridPos);
                    int colNum = ExtractColumnNumber(gridPos);

                    // Map to new row number
                    if (oldRowToNewRow.ContainsKey(oldRowNum))
                    {
                        int newRowNum = oldRowToNewRow[oldRowNum];

                        if (!rowColumnControlsMap.ContainsKey(newRowNum))
                        {
                            rowColumnControlsMap[newRowNum] = new Dictionary<int, JObject>();
                        }
                        rowColumnControlsMap[newRowNum][colNum] = control;
                    }
                }
            }

            // Analyze each row for rich text controls and calculate their spans
            Dictionary<string, int> richTextSpans = new Dictionary<string, int>();

            foreach (var rowEntry in rowColumnControlsMap)
            {
                int newRowNum = rowEntry.Key;
                var columnsInRow = rowEntry.Value;

                foreach (var colEntry in columnsInRow.OrderBy(x => x.Key))
                {
                    int colNum = colEntry.Key;
                    JObject control = colEntry.Value;
                    string controlType = control["Type"]?.Value<string>();
                    string controlName = control["Name"]?.Value<string>();

                    if (controlType?.ToLower() == "richtext")
                    {
                        // Calculate how many columns this rich text should span
                        int spanColumns = 1;

                        // Check if there's a label before this control
                        bool hasLabelBefore = false;
                        if (colNum > 0 && columnsInRow.ContainsKey(colNum - 1))
                        {
                            JObject prevControl = columnsInRow[colNum - 1];
                            string prevType = prevControl["Type"]?.Value<string>();
                            string prevName = prevControl["Name"]?.Value<string>();

                            if (prevType?.ToLower() == "label" &&
                                !string.IsNullOrEmpty(controlName) &&
                                !string.IsNullOrEmpty(prevName) &&
                                prevName.Equals(controlName, StringComparison.OrdinalIgnoreCase))
                            {
                                hasLabelBefore = true;
                            }
                        }

                        // Find the next non-label control after this rich text
                        int nextControlColumn = maxColumn;

                        for (int checkCol = colNum + 1; checkCol < maxColumn; checkCol++)
                        {
                            if (columnsInRow.ContainsKey(checkCol))
                            {
                                JObject nextControl = columnsInRow[checkCol];
                                string nextType = nextControl["Type"]?.Value<string>();

                                if (nextType?.ToLower() != "label")
                                {
                                    nextControlColumn = checkCol;
                                    break;
                                }

                                string nextName = nextControl["Name"]?.Value<string>();
                                if (!string.IsNullOrEmpty(nextName) &&
                                    !nextName.Equals(controlName, StringComparison.OrdinalIgnoreCase))
                                {
                                    bool hasMatchingControl = false;
                                    foreach (var checkEntry in columnsInRow)
                                    {
                                        if (checkEntry.Key > checkCol)
                                        {
                                            JObject checkControl = checkEntry.Value;
                                            string checkName = checkControl["Name"]?.Value<string>();
                                            if (checkName?.Equals(nextName, StringComparison.OrdinalIgnoreCase) == true)
                                            {
                                                hasMatchingControl = true;
                                                nextControlColumn = checkCol;
                                                break;
                                            }
                                        }
                                    }

                                    if (hasMatchingControl)
                                        break;
                                }
                            }
                        }

                        spanColumns = nextControlColumn - colNum;

                        string cellKey = $"{newRowNum}_{colNum}";
                        richTextSpans[cellKey] = spanColumns;

                        Console.WriteLine($"        Rich Text control '{controlName}' at row {newRowNum}, column {colNum} will span {spanColumns} columns");
                    }
                }
            }

            // Create rows and cells - only for the new row numbers
            XmlElement rows = doc.CreateElement("Rows");
            Dictionary<string, XmlElement> cellElements = new Dictionary<string, XmlElement>();
            HashSet<string> mergedCells = new HashSet<string>();

            int maxNewRow = oldRowToNewRow.Values.Max();
            for (int newRowNum = 1; newRowNum <= maxNewRow; newRowNum++)
            {
                XmlElement row = doc.CreateElement("Row");
                string rowId = controlIdMap.ContainsKey($"Row{newRowNum}") ?
                    controlIdMap[$"Row{newRowNum}"] : Guid.NewGuid().ToString();
                row.SetAttribute("ID", rowId);

                XmlElement cells = doc.CreateElement("Cells");

                // Create cells for this row
                for (int colNum = 0; colNum < maxColumn; colNum++)
                {
                    string cellKey = $"{newRowNum}_{colNum}";

                    // Skip if this cell has been merged into another cell
                    if (mergedCells.Contains(cellKey))
                        continue;

                    // Check if there's a rich text control at this position
                    int columnSpan = 1;
                    if (richTextSpans.ContainsKey(cellKey))
                    {
                        columnSpan = richTextSpans[cellKey];

                        // Mark the cells that will be merged
                        for (int mergeCol = colNum + 1; mergeCol < colNum + columnSpan && mergeCol < maxColumn; mergeCol++)
                        {
                            mergedCells.Add($"{newRowNum}_{mergeCol}");
                        }
                    }

                    string cellId = controlIdMap.ContainsKey($"Cell{cellKey}") ?
                        controlIdMap[$"Cell{cellKey}"] : Guid.NewGuid().ToString();

                    XmlElement cell = doc.CreateElement("Cell");
                    cell.SetAttribute("ID", cellId);
                    cell.SetAttribute("ColumnSpan", columnSpan.ToString());
                    cell.SetAttribute("RowSpan", "1");

                    cellElements[cellKey] = cell;

                    // Also map the merged cells to this cell element for control placement
                    if (columnSpan > 1)
                    {
                        for (int mergeCol = colNum + 1; mergeCol < colNum + columnSpan && mergeCol < maxColumn; mergeCol++)
                        {
                            cellElements[$"{newRowNum}_{mergeCol}"] = cell;
                        }
                    }

                    cells.AppendChild(cell);
                }

                row.AppendChild(cells);
                rows.AppendChild(row);
            }

            // Place controls in their appropriate cells using the row mapping
            foreach (JObject control in controls)
            {
                string gridPos = control["GridPosition"]?.Value<string>();
                string controlType = control["Type"]?.Value<string>();
                string name = control["Name"]?.Value<string>();

                if (!string.IsNullOrEmpty(gridPos))
                {
                    int oldRowNum = ExtractRowNumber(gridPos);
                    int colNum = ExtractColumnNumber(gridPos);

                    // Map to new row number
                    if (!oldRowToNewRow.ContainsKey(oldRowNum))
                    {
                        Console.WriteLine($"        WARNING: Row {oldRowNum} not in mapping for control {name}");
                        continue;
                    }

                    int newRowNum = oldRowToNewRow[oldRowNum];
                    string remappedGridPos = $"{newRowNum}{ExtractColumnLetter(gridPos)}";
                    string cellKey = $"{newRowNum}_{colNum}";

                    if (cellElements.ContainsKey(cellKey))
                    {
                        XmlElement cell = cellElements[cellKey];

                        // Try both the remapped and original positions
                        string controlId = null;
                        if (controlIdMap.ContainsKey(remappedGridPos))
                        {
                            controlId = controlIdMap[remappedGridPos];
                        }
                        else if (controlIdMap.ContainsKey(gridPos))
                        {
                            controlId = controlIdMap[gridPos];
                        }

                        if (!string.IsNullOrEmpty(controlId))
                        {
                            XmlElement controlRef = doc.CreateElement("Control");
                            controlRef.SetAttribute("ID", controlId);
                            cell.AppendChild(controlRef);

                            string columnLetter = GetColumnLetter(colNum);

                            if (controlType?.ToLower() == "richtext" && richTextSpans.ContainsKey(cellKey))
                            {
                                int span = richTextSpans[cellKey];
                                Console.WriteLine($"        Placed RichText control {name} at Row {newRowNum} (was {oldRowNum}), Column {columnLetter} spanning {span} columns with ID {controlId}");
                            }
                            else
                            {
                                Console.WriteLine($"        Placed control {name} ({controlType}) at Row {newRowNum} (was {oldRowNum}), Column {columnLetter} with ID {controlId}");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"        WARNING: No control ID found for positions {remappedGridPos} or {gridPos} (control: {name})");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"        WARNING: Cell not found for position {cellKey} (Row {newRowNum}, Column {colNum})");
                    }
                }
            }

            tableControl.AppendChild(rows);
            section.AppendChild(tableControl);
            sections.AppendChild(section);
            canvas.AppendChild(sections);

            return canvas;
        }

        public XmlDocument CreateViewXmlStructureWithoutCompaction(string viewName, string smoGuid, string smoName,
                          JArray controls, JArray dataArray,
                          JArray dynamicSections, JObject conditionalVisibility,
                          bool isItemView, out string viewTitle)
        {
            // Store conditional visibility data for use during control creation
            _conditionalVisibility = conditionalVisibility;
            _dynamicSections = dynamicSections;

            XmlDocument doc = new XmlDocument();
            doc.PreserveWhitespace = false;

            // Controls are already normalized/compacted from ViewGenerator, so we just need to:

            // STEP 1: Extract title from the already-compacted controls
            viewTitle = DetectAndExtractTitleLabel(controls);

            // STEP 2: Remove title label if found
            JArray finalControls = controls;
            if (!string.IsNullOrEmpty(viewTitle))
            {
                finalControls = RemoveTitleLabel(controls);
                Console.WriteLine($"    Extracted view title: '{viewTitle}' and removed title label from view");
            }

            // Continue with the rest of the existing code using finalControls
            XmlElement root = doc.CreateElement("SourceCode.Forms");
            root.SetAttribute("Version", "29");
            doc.AppendChild(root);

            XmlElement views = doc.CreateElement("Views");
            root.AppendChild(views);

            XmlElement view = doc.CreateElement("View");
            string viewGuid = Guid.NewGuid().ToString();
            view.SetAttribute("ID", viewGuid);
            view.SetAttribute("Type", "Capture");
            view.SetAttribute("RenderVersion", "3");
            view.SetAttribute("IsUserModified", "True");
            view.SetAttribute("SOID", smoGuid);
            views.AppendChild(view);

            XmlHelper.AddElement(doc, view, "Name", viewName);

            // Track control and field information
            Dictionary<string, string> controlIdMap = new Dictionary<string, string>();
            Dictionary<string, FieldInfo> fieldMap = new Dictionary<string, FieldInfo>();
            Dictionary<string, string> controlToFieldMap = new Dictionary<string, string>();
            Dictionary<string, LookupInfo> lookupSmartObjects = new Dictionary<string, LookupInfo>();

            // Reset counters for this view
            _usedControlNames.Clear();
            _controlCounter = 1;

            // Create controls section - USE FINAL CONTROLS
            XmlElement controlsElement = CreateControlsSection(doc, viewGuid, viewName, smoName,
                finalControls, dataArray, controlIdMap, fieldMap, controlToFieldMap, lookupSmartObjects);
            view.AppendChild(controlsElement);

            // Create canvas - USE FINAL CONTROLS
            XmlElement canvas = CreateCanvasStructure(doc, finalControls, controlIdMap);
            view.AppendChild(canvas);

            // Add buttons only to item views
            if (isItemView)
            {
                AddItemViewButtons(doc, controlsElement, canvas, controlIdMap);
            }

            // Create sources
            XmlElement sources = CreateSourcesSection(doc, smoGuid, smoName, fieldMap, lookupSmartObjects);
            view.AppendChild(sources);

            // Create translations - USE FINAL CONTROLS
            XmlElement translations = CreateTranslationsSection(doc, viewName, finalControls);
            view.AppendChild(translations);

            // Create events section WITH visibility rules - USE FINAL CONTROLS
            XmlElement events = _rulesBuilder.CreateEventsWithRules(doc, viewGuid, viewName, controlIdMap,
                controlToFieldMap, fieldMap, lookupSmartObjects, dynamicSections, conditionalVisibility,
                finalControls, _jsonToK2ControlIdMap);
            view.AppendChild(events);

            // Add empty but required sections
            view.AppendChild(doc.CreateElement("Expressions"));

            // Create Parameters section with ID parameter
            XmlElement parameters = CreateParametersSection(doc);
            view.AppendChild(parameters);

            XmlHelper.AddElement(doc, view, "Description", "");
            // Use extracted view title if available, otherwise fall back to technical view name
            string displayName = !string.IsNullOrEmpty(viewTitle) ? viewTitle : viewName;
            XmlHelper.AddElement(doc, view, "DisplayName", displayName);

            return doc;
        }
        private string GetControlDataType(string k2ControlType)
        {
            switch (k2ControlType)
            {
                case "Calendar":
                    return "DateTime";
                case "CheckBox":
                    return "YesNo";
                case "TextArea":
                case "HTMLEditor":
                    return "Memo";
                case "FilePostBack":
                    return "File";
                case "ImagePostBack":
                    return "Image";
                case "SharePointHyperLink":
                    return "Hyperlink";
                default:
                    return "Text";
            }
        }

        private string GetColumnLetter(int columnNumber)
        {
            string columnLetter = "";
            while (columnNumber >= 0)
            {
                columnLetter = (char)('A' + (columnNumber % 26)) + columnLetter;
                columnNumber = columnNumber / 26 - 1;
            }
            return columnLetter;
        }
 

        private string ExtractColumnLetter(string gridPosition)
        {
            if (string.IsNullOrEmpty(gridPosition))
                return "A";

            // Extract letter part (e.g., "A", "B", "C", etc.)
            string letterPart = new string(gridPosition.SkipWhile(char.IsDigit).ToArray());
            return string.IsNullOrEmpty(letterPart) ? "A" : letterPart.ToUpper();
        }


        // Modified CreateControlFromJson method to skip button controls for item views
        // Modified CreateControlFromJson method to skip button controls for item views
        private XmlElement CreateControlFromJson(XmlDocument doc, JObject controlDef, JArray dataArray,
                                             string smoName, string viewName, string viewGuid,
                                             Dictionary<string, string> controlIdMap,
                                             Dictionary<string, FieldInfo> fieldMap,
                                             Dictionary<string, string> controlToFieldMap,
                                             Dictionary<string, LookupInfo> lookupSmartObjects,
                                             string jsonCtrlId)
        {
            string controlType = controlDef["Type"]?.Value<string>();
            string name = controlDef["Name"]?.Value<string>();
            string label = controlDef["Label"]?.Value<string>();

            if (string.IsNullOrEmpty(controlType) || string.IsNullOrEmpty(name))
                return null;

            // NEW: Skip button controls for item views
            // Check if this is an item view (they contain "_Item" in their name or are item views for repeating sections)
            bool isItemView = viewName.Contains("_Item") ||
                              (viewName.Contains("_") && !viewName.Contains("_List") && !viewName.Contains("_Part"));

            // Also check the actual view type from the isItemView parameter if it's being passed
            // Log for debugging
            Console.WriteLine($"        Processing control '{name}' of type '{controlType}' for view '{viewName}' (isItemView: {isItemView})");

            if (isItemView && controlType?.ToLower() == "button")
            {
                // Check if this is an auto-generated button from nested table handling
                bool isAutoGenerated = controlDef["AdditionalProperties"]?["isAutoGenerated"]?.Value<string>() == "true";

                if (!isAutoGenerated)
                {
                    Console.WriteLine($"        SKIPPING button control '{name}' in item view '{viewName}'");
                    return null;
                }
                else
                {
                    Console.WriteLine($"        CREATING auto-generated button control '{name}' in item view '{viewName}'");
                }
            }

            // Get the base form name (handles both main and child SmartObjects)
            string baseFormName = smoName;
            if (smoName.Contains("_"))
            {
                // Extract the base form name from child SmartObject names
                // e.g., "Expense_Report_Itemized_Expenses" -> "Expense_Report"
                string[] parts = smoName.Split('_');
                if (parts.Length >= 2)
                {
                    baseFormName = $"{parts[0]}_{parts[1]}";
                }
            }

            string consolidatedLookupName = $"{baseFormName}_Lookups";

            Console.WriteLine($"        Checking for consolidated lookup SmartObject: {consolidatedLookupName}");
            bool hasConsolidatedLookup = _smoGenerator.CheckSmartObjectExists(consolidatedLookupName);

            // Check if this is a dropdown control with DataOptions
            bool hasDropdownValues = false;
            JArray dataOptions = controlDef["DataOptions"] as JArray;
            if (dataOptions != null && dataOptions.Count > 0)
            {
                hasDropdownValues = true;
                Console.WriteLine($"        Found {dataOptions.Count} DataOptions in control definition");
            }

            // If not found in control definition, check in dataArray
            if (!hasDropdownValues && dataArray != null)
            {
                foreach (JObject dataItem in dataArray)
                {
                    string dataColumnName = dataItem["ColumnName"]?.Value<string>();
                    if (dataColumnName == name)
                    {
                        JArray validValues = dataItem["ValidValues"] as JArray;
                        if (validValues != null && validValues.Count > 0)
                        {
                            hasDropdownValues = true;
                            Console.WriteLine($"        Found {validValues.Count} ValidValues in dataArray");
                            break;
                        }
                    }
                }
            }

            // For dropdown controls with lookup values, ensure they're treated as dropdowns
            if (controlType?.ToLower() == "dropdown" && hasConsolidatedLookup && hasDropdownValues)
            {
                Console.WriteLine($"        Binding DropDown '{name}' to consolidated lookup");
            }

            string k2ControlType = MapJsonToK2ControlType(controlType);
            if (k2ControlType == null)
                return null;

            // Check for date formatting and upgrade TextBox to Calendar if needed
            if (k2ControlType == "TextBox" && controlDef != null)
            {
                string boundProp = controlDef["AdditionalProperties"]?["boundProp"]?.ToString();
                string datafmt = controlDef["AdditionalProperties"]?["datafmt"]?.ToString();

                // Check if this is date formatting
                bool isDateField = false;
                if (!string.IsNullOrEmpty(boundProp) && boundProp.ToLower() == "xd:date")
                {
                    isDateField = true;
                }
                else if (!string.IsNullOrEmpty(datafmt) && datafmt.ToLower().Contains("\"date\""))
                {
                    // Check for date datafmt like "date","dateFormat:Short Date;"
                    isDateField = true;
                }

                if (isDateField)
                {
                    k2ControlType = "Calendar";
                    Console.WriteLine($"        [FORMATTING] Upgraded TextBox to Calendar due to date formatting: boundProp='{boundProp}', datafmt='{datafmt}'");
                }
            }

            XmlElement control = doc.CreateElement("Control");
            string controlGuid = Guid.NewGuid().ToString();
            control.SetAttribute("ID", controlGuid);
            control.SetAttribute("Type", k2ControlType);

            if (!string.IsNullOrEmpty(jsonCtrlId))
            {
                control.SetAttribute("JSONCtrlId", jsonCtrlId);
            }

            // Special handling for DataLabel controls
            bool isDataLabel = false;

            // Create unique control name
            string baseName = NameSanitizer.SanitizePropertyName(name);
            string uniqueName = GetUniqueControlName(baseName, _usedControlNames, ref _controlCounter);
            _usedControlNames.Add(uniqueName);
            controlIdMap[uniqueName] = controlGuid;

            string displayName = uniqueName.Replace("_", " ");

            // For data-bound controls, get field info and add FieldID
            bool isDataBound = IsDataBoundControl(k2ControlType) && _smoFieldMappings.ContainsKey(smoName);

            if (isDataBound)
            {
                FieldInfo fieldInfo = null;
                string sanitizedFieldName = NameSanitizer.SanitizePropertyName(name);

                if (_smoFieldMappings[smoName].ContainsKey(sanitizedFieldName))
                {
                    fieldInfo = _smoFieldMappings[smoName][sanitizedFieldName];
                }
                else if (_smoFieldMappings[smoName].ContainsKey(sanitizedFieldName.ToUpper()))
                {
                    fieldInfo = _smoFieldMappings[smoName][sanitizedFieldName.ToUpper()];
                }

                if (fieldInfo != null)
                {
                    string fieldGuid = Guid.NewGuid().ToString();
                    control.SetAttribute("FieldID", fieldGuid);
                    fieldMap[fieldGuid] = fieldInfo;
                    controlToFieldMap[controlGuid] = fieldGuid;

                    // If this is a label control with field binding, it's a DataLabel
                    if (k2ControlType == "Label")
                    {
                        isDataLabel = true;
                        control.SetAttribute("Type", "DataLabel");
                        k2ControlType = "DataLabel";
                    }
                }
            }

            // Create control properties - pass the correct parameters for consolidated lookup
            XmlElement properties = CreateControlProperties(doc, k2ControlType, displayName,
                label, controlDef, controlToFieldMap, controlGuid, fieldMap,
                dataArray, name, consolidatedLookupName,
                (k2ControlType == "DropDown" && hasConsolidatedLookup && hasDropdownValues),
                lookupSmartObjects, uniqueName, jsonCtrlId);
            control.AppendChild(properties);

            // Add styles
            XmlElement styles = doc.CreateElement("Styles");
            XmlElement style = doc.CreateElement("Style");
            style.SetAttribute("IsDefault", "True");

            if (k2ControlType == "RadioButtonList" || k2ControlType == "CheckBoxList")
            {
                XmlElement border = doc.CreateElement("Border");
                XmlElement defaultBorder = doc.CreateElement("Default");
                XmlHelper.AddElement(doc, defaultBorder, "Style", "Solid");
                XmlHelper.AddElement(doc, defaultBorder, "Color", "#999");
                XmlHelper.AddElement(doc, defaultBorder, "Width", "1px");
                border.AppendChild(defaultBorder);
                style.AppendChild(border);
            }

            styles.AppendChild(style);
            control.AppendChild(styles);

            // Apply InfoPath formatting if this is a TextBox or Calendar control (after Styles element is created)
            if ((k2ControlType == "TextBox" || k2ControlType == "Calendar") && controlDef != null)
            {
                string boundProp = controlDef["AdditionalProperties"]?["boundProp"]?.ToString();
                string datafmt = controlDef["AdditionalProperties"]?["datafmt"]?.ToString();

                if (K2FormatBuilder.ShouldApplyFormatting("TextBox", datafmt, boundProp))
                {
                    Console.WriteLine($"        [FORMATTING] Applying formatting after Styles created: boundProp='{boundProp}', datafmt='{datafmt}'");
                    bool applied = K2FormatBuilder.ApplyFormattingToControl(control, datafmt, boundProp);
                    if (applied)
                    {
                        Console.WriteLine($"        Applied InfoPath formatting to TextBox: datafmt='{datafmt}', boundProp='{boundProp}'");
                        // Debug: Show if Format element was added to the Styles
                        var formatElements = control.SelectNodes(".//Format");
                        Console.WriteLine($"        [FORMATTING] Format elements found in control: {formatElements.Count}");
                        if (formatElements.Count > 0)
                        {
                            Console.WriteLine($"        [FORMATTING] Format XML: {formatElements[0].OuterXml}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"        [FORMATTING] Failed to apply formatting");
                    }
                }
            }

            // Apply read-only property if disableEditing is set
            if (controlDef != null)
            {
                string disableEditing = controlDef["AdditionalProperties"]?["disableEditing"]?.ToString();
                if (!string.IsNullOrEmpty(disableEditing) && disableEditing.ToLower() == "yes")
                {
                    // Find the Properties element and add IsReadOnly property
                    XmlElement propertiesElement = control.SelectSingleNode("Properties") as XmlElement;
                    if (propertiesElement != null)
                    {
                        XmlElement readOnlyProperty = doc.CreateElement("Property");
                        XmlHelper.AddElement(doc, readOnlyProperty, "Name", "IsReadOnly");
                        XmlHelper.AddElement(doc, readOnlyProperty, "Value", "true");
                        XmlHelper.AddElement(doc, readOnlyProperty, "DisplayValue", "true");
                        propertiesElement.AppendChild(readOnlyProperty);

                        Console.WriteLine($"        [READ-ONLY] Applied IsReadOnly=true due to disableEditing=yes");
                    }
                }
            }

            // Adjust the display name based on control type
            string controlTypeDisplayName = isDataLabel ? "Data Label" : GetControlTypeDisplayName(k2ControlType);
            XmlHelper.AddElement(doc, control, "Name", $"{displayName} {controlTypeDisplayName}");
            XmlHelper.AddElement(doc, control, "DisplayName", $"{displayName} {controlTypeDisplayName}");
            XmlHelper.AddElement(doc, control, "NameValue", $"{displayName} {controlTypeDisplayName}");

            // Register Add Item buttons in the registry
            if (isItemView && (controlType?.ToLower() == "button" || controlType?.ToLower() == "toolbarbutton"))
            {
                bool isAutoGenerated = controlDef["AdditionalProperties"]?["isAutoGenerated"]?.Value<string>() == "true";
                if (isAutoGenerated)
                {
                    string targetTableName = controlDef["AdditionalProperties"]?["targetTable"]?.Value<string>() ?? "Unknown";

                    // Register the add item button with complete information
                    SmartObjectViewRegistry.RegisterAddItemButton(name, controlGuid, viewName, viewGuid, targetTableName);
                }
            }

            return control;
        }

        // Helper method to determine if a control should be initially hidden based on conditional visibility rules
        private bool ShouldControlBeInitiallyHidden(string jsonCtrlId)
        {
            if (string.IsNullOrEmpty(jsonCtrlId))
                return false;

            if (_conditionalVisibility == null)
                return false;

            // Check if this control ID appears in any of the conditional visibility arrays
            foreach (var property in _conditionalVisibility.Properties())
            {
                JArray controlIds = property.Value as JArray;
                if (controlIds != null)
                {
                    foreach (string ctrlId in controlIds)
                    {
                        if (ctrlId == jsonCtrlId)
                        {
                            Console.WriteLine($"        [VISIBILITY] Control {jsonCtrlId} will be initially hidden (conditional on {property.Name})");
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private XmlElement CreateControlProperties(XmlDocument doc, string k2ControlType,
                                                  string displayName, string label, JObject controlDef,
                                                  Dictionary<string, string> controlToFieldMap,
                                                  string controlGuid, Dictionary<string, FieldInfo> fieldMap,
                                                  JArray dataArray, string originalName,
                                                  string lookupSmoName, bool hasLookupSmartObject,
                                                  Dictionary<string, LookupInfo> lookupSmartObjects,
                                                  string uniqueName, string jsonCtrlId)
        {

            Console.WriteLine($"        CreateControlProperties called:");
            Console.WriteLine($"          k2ControlType: {k2ControlType}");
            Console.WriteLine($"          hasLookupSmartObject: {hasLookupSmartObject}");
            Console.WriteLine($"          lookupSmoName: {lookupSmoName}");
            // For dropdowns with lookup SmartObjects, handle specially
            if (k2ControlType == "DropDown" && hasLookupSmartObject)
            {
                return CreateDropdownPropertiesWithLookup(doc, displayName, controlDef, controlToFieldMap,
                    controlGuid, fieldMap, dataArray, originalName, lookupSmoName,
                    lookupSmartObjects, uniqueName, jsonCtrlId);
            }

            // Otherwise use standard property creation
            XmlElement properties = doc.CreateElement("Properties");

            // Add common ControlName property
            XmlElement controlNameProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, controlNameProp, "Name", "ControlName");
            XmlHelper.AddElement(doc, controlNameProp, "DisplayValue", displayName);
            XmlHelper.AddElement(doc, controlNameProp, "NameValue", displayName);
            XmlHelper.AddElement(doc, controlNameProp, "Value", displayName);
            properties.AppendChild(controlNameProp);

            // Add control-specific properties based on type
            AddControlTypeSpecificProperties(doc, properties, k2ControlType, displayName, label,
                controlDef, controlToFieldMap, controlGuid, fieldMap);

            // Check if this control should be initially hidden based on conditional visibility rules
            bool shouldBeHidden = ShouldControlBeInitiallyHidden(jsonCtrlId);
            if (shouldBeHidden)
            {
                XmlElement isVisibleProp = doc.CreateElement("Property");
                XmlHelper.AddElement(doc, isVisibleProp, "Name", "IsVisible");
                XmlHelper.AddElement(doc, isVisibleProp, "DisplayValue", "false");
                XmlHelper.AddElement(doc, isVisibleProp, "Value", "false");
                properties.AppendChild(isVisibleProp);
                Console.WriteLine($"        [VISIBILITY] Added IsVisible=false to control {jsonCtrlId}");
            }

            return properties;
        }

        private XmlElement CreateDropdownPropertiesWithLookup(XmlDocument doc, string displayName,
                                                        JObject controlDef,
                                                        Dictionary<string, string> controlToFieldMap,
                                                        string controlGuid,
                                                        Dictionary<string, FieldInfo> fieldMap,
                                                        JArray dataArray, string originalName,
                                                        string lookupSmoName,
                                                        Dictionary<string, LookupInfo> lookupSmartObjects,
                                                        string uniqueName, string jsonCtrlId)
        {
            // Check for dropdown values in control definition first (for repeating sections)
            JArray validValues = controlDef["DataOptions"] as JArray;

            // If not found, check in dataArray
            if (validValues == null && dataArray != null)
            {
                foreach (JObject dataItem in dataArray)
                {
                    string dataColumnName = dataItem["ColumnName"]?.Value<string>();
                    if (dataColumnName == originalName)
                    {
                        validValues = dataItem["ValidValues"] as JArray;
                        break;
                    }
                }
            }

            XmlElement properties = doc.CreateElement("Properties");

            // Add common ControlName property
            XmlElement controlNameProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, controlNameProp, "Name", "ControlName");
            XmlHelper.AddElement(doc, controlNameProp, "DisplayValue", displayName);
            XmlHelper.AddElement(doc, controlNameProp, "NameValue", uniqueName);
            XmlHelper.AddElement(doc, controlNameProp, "Value", displayName);
            properties.AppendChild(controlNameProp);

            // Add field binding
            if (controlToFieldMap.ContainsKey(controlGuid) && fieldMap.ContainsKey(controlToFieldMap[controlGuid]))
            {
                FieldInfo fieldInfo = fieldMap[controlToFieldMap[controlGuid]];
                XmlElement fieldProp = doc.CreateElement("Property");
                XmlHelper.AddElement(doc, fieldProp, "Name", "Field");
                string fieldDisplayName = fieldInfo.DisplayName.Replace("_", " ");
                XmlHelper.AddElement(doc, fieldProp, "DisplayValue", fieldDisplayName);
                XmlHelper.AddElement(doc, fieldProp, "NameValue", fieldDisplayName);
                XmlHelper.AddElement(doc, fieldProp, "Value", controlToFieldMap[controlGuid]);
                properties.AppendChild(fieldProp);
            }

            // Add watermark
            XmlElement dropWatermarkProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, dropWatermarkProp, "Name", "WaterMarkText");
            XmlHelper.AddElement(doc, dropWatermarkProp, "DisplayValue", "Select an item");
            XmlHelper.AddElement(doc, dropWatermarkProp, "Value", "Select an item");
            properties.AppendChild(dropWatermarkProp);

            // Add data type
            XmlElement dropDataTypeProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, dropDataTypeProp, "Name", "DataType");
            XmlHelper.AddElement(doc, dropDataTypeProp, "DisplayValue", "text");
            XmlHelper.AddElement(doc, dropDataTypeProp, "Value", "text");
            properties.AppendChild(dropDataTypeProp);

            // Only bind to lookup SmartObject if we have valid values
            if (validValues != null && validValues.Count > 0)
            {
                // Get the consolidated lookup SmartObject GUID
                string lookupSmoGuid = null;
                try
                {
                    lookupSmoGuid = _smoGenerator.GetSmartObjectGuid(lookupSmoName);
                    Console.WriteLine($"        Got GUID for consolidated lookup: {lookupSmoGuid}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"        ERROR: Failed to get GUID for consolidated lookup SmartObject: {ex.Message}");
                }

                if (!string.IsNullOrEmpty(lookupSmoGuid))
                {
                    // Track this lookup SmartObject with the parameter info
                    lookupSmartObjects[originalName] = new LookupInfo
                    {
                        SmartObjectName = lookupSmoName,
                        SmartObjectGuid = lookupSmoGuid,
                        ControlId = controlGuid,
                        ControlName = uniqueName,
                        LookupParameter = originalName  // Store the field name as the parameter for filtering
                    };

                    Console.WriteLine($"        Successfully binding dropdown '{originalName}' to consolidated lookup");
                    Console.WriteLine($"        LookupParameter set to: {originalName}");

                    // Add DataSourceType property
                    XmlElement dataSourceTypeProp = doc.CreateElement("Property");
                    XmlHelper.AddElement(doc, dataSourceTypeProp, "Name", "DataSourceType");
                    XmlHelper.AddElement(doc, dataSourceTypeProp, "DisplayValue", "SmartObject");
                    XmlHelper.AddElement(doc, dataSourceTypeProp, "Value", "SmartObject");
                    properties.AppendChild(dataSourceTypeProp);

                    // Add AssociationSO property
                    XmlElement assocSOProp = doc.CreateElement("Property");
                    XmlHelper.AddElement(doc, assocSOProp, "Name", "AssociationSO");
                    XmlHelper.AddElement(doc, assocSOProp, "DisplayValue", lookupSmoName.Replace("_", " "));
                    XmlHelper.AddElement(doc, assocSOProp, "NameValue", lookupSmoName);
                    XmlHelper.AddElement(doc, assocSOProp, "Value", lookupSmoGuid);
                    properties.AppendChild(assocSOProp);

                    // Add AssociationMethod property
                    XmlElement assocMethodProp = doc.CreateElement("Property");
                    XmlHelper.AddElement(doc, assocMethodProp, "Name", "AssociationMethod");
                    XmlHelper.AddElement(doc, assocMethodProp, "DisplayValue", "Get List");
                    XmlHelper.AddElement(doc, assocMethodProp, "NameValue", "GetList");
                    XmlHelper.AddElement(doc, assocMethodProp, "Value", "GetList");
                    properties.AppendChild(assocMethodProp);

                    // Add ValueProperty
                    XmlElement valueProp = doc.CreateElement("Property");
                    XmlHelper.AddElement(doc, valueProp, "Name", "ValueProperty");
                    XmlHelper.AddElement(doc, valueProp, "DisplayValue", "Value");
                    XmlHelper.AddElement(doc, valueProp, "NameValue", "Value");
                    XmlHelper.AddElement(doc, valueProp, "Value", "Value");
                    properties.AppendChild(valueProp);

                    // Add DisplayTemplate
                    XmlElement displayTemplateProp = doc.CreateElement("Property");
                    XmlHelper.AddElement(doc, displayTemplateProp, "Name", "DisplayTemplate");
                    XmlHelper.AddElement(doc, displayTemplateProp, "DisplayValue", "[DisplayText]");
                    XmlHelper.AddElement(doc, displayTemplateProp, "Value",
                        "<Template><Item SourceType=\"ObjectProperty\" SourceID=\"DisplayText\" " +
                        "SourceName=\"DisplayText\" SourceDisplayName=\"Display Text\" DataType=\"Text\"/></Template>");
                    properties.AppendChild(displayTemplateProp);
                }
                else
                {
                    Console.WriteLine($"        WARNING: Could not get GUID for consolidated lookup, dropdown will not be bound");
                }
            }
            else
            {
                Console.WriteLine($"        WARNING: No valid values found for dropdown {originalName}");
            }

            // Check if this control should be initially hidden based on conditional visibility rules
            bool shouldBeHidden = ShouldControlBeInitiallyHidden(jsonCtrlId);
            if (shouldBeHidden)
            {
                XmlElement isVisibleProp = doc.CreateElement("Property");
                XmlHelper.AddElement(doc, isVisibleProp, "Name", "IsVisible");
                XmlHelper.AddElement(doc, isVisibleProp, "DisplayValue", "false");
                XmlHelper.AddElement(doc, isVisibleProp, "Value", "false");
                properties.AppendChild(isVisibleProp);
                Console.WriteLine($"        [VISIBILITY] Added IsVisible=false to dropdown control {jsonCtrlId}");
            }

            return properties;
        }

        private void AddControlTypeSpecificProperties(XmlDocument doc, XmlElement properties,
                                               string k2ControlType, string displayName,
                                               string label, JObject controlDef,
                                               Dictionary<string, string> controlToFieldMap,
                                               string controlGuid,
                                               Dictionary<string, FieldInfo> fieldMap)
        {
            switch (k2ControlType)
            {
                case "HTMLEditor":
                    AddHtmlEditorProperties(doc, properties, controlToFieldMap, controlGuid, fieldMap);
                    break;

                case "AutoComplete":
                    AddAutoCompleteProperties(doc, properties, controlDef, controlToFieldMap, controlGuid, fieldMap);
                    break;

                case "SharePointHyperLink":
                    AddHyperlinkProperties(doc, properties, controlToFieldMap, controlGuid, fieldMap);
                    break;

                case "RadioButtonList":
                    AddRadioButtonListProperties(doc, properties, controlDef, controlToFieldMap, controlGuid, fieldMap);
                    break;

                case "CheckBoxList":
                    AddCheckBoxListProperties(doc, properties, controlDef, controlToFieldMap, controlGuid, fieldMap);
                    break;

                case "Picker":
                    AddPickerProperties(doc, properties);
                    break;

                case "Picture":
                    AddPictureProperties(doc, properties, controlDef);
                    break;

                case "FilePostBack":
                    AddFilePostBackProperties(doc, properties, controlToFieldMap, controlGuid, fieldMap);
                    break;

                case "ImagePostBack":
                    AddImagePostBackProperties(doc, properties, controlToFieldMap, controlGuid, fieldMap);
                    break;

                case "Button":
                case "ToolBarButton":
                    AddButtonProperties(doc, properties, label ?? displayName, k2ControlType, controlDef);
                    break;

                case "TextBox":
                    AddTextBoxProperties(doc, properties, controlToFieldMap, controlGuid, fieldMap, controlDef);
                    break;

                case "TextArea":
                    AddTextAreaProperties(doc, properties, controlToFieldMap, controlGuid, fieldMap);
                    break;

                case "Calendar":
                    AddCalendarProperties(doc, properties, controlToFieldMap, controlGuid, fieldMap, controlDef);
                    break;

                case "CheckBox":
                    AddCheckBoxProperties(doc, properties, label ?? displayName, controlToFieldMap, controlGuid, fieldMap);
                    break;

                case "DropDown":
                    AddDropDownProperties(doc, properties, controlToFieldMap, controlGuid, fieldMap);
                    break;

                case "Label":
                    AddLabelProperties(doc, properties, label ?? displayName);
                    break;

                case "DataLabel":
                    AddDataLabelProperties(doc, properties, label ?? displayName, controlToFieldMap, controlGuid, fieldMap);
                    break;

                default:
                    Console.WriteLine($"        WARNING: No specific properties defined for control type: {k2ControlType}");
                    break;
            }
        }

        private void AddDataLabelProperties(XmlDocument doc, XmlElement properties, string text,
                                   Dictionary<string, string> controlToFieldMap,
                                   string controlGuid, Dictionary<string, FieldInfo> fieldMap)
        {
            // Add LiteralVal property first
            AddProperty(doc, properties, "LiteralVal", "False");

            // Add Field binding if available
            if (controlToFieldMap.ContainsKey(controlGuid) && fieldMap.ContainsKey(controlToFieldMap[controlGuid]))
            {
                FieldInfo fieldInfo = fieldMap[controlToFieldMap[controlGuid]];
                XmlElement fieldProp = doc.CreateElement("Property");
                XmlHelper.AddElement(doc, fieldProp, "Name", "Field");
                string fieldDisplayName = fieldInfo.DisplayName.Replace("_", " ");
                XmlHelper.AddElement(doc, fieldProp, "DisplayValue", fieldDisplayName);
                XmlHelper.AddElement(doc, fieldProp, "NameValue", fieldDisplayName);
                XmlHelper.AddElement(doc, fieldProp, "Value", controlToFieldMap[controlGuid]);
                properties.AppendChild(fieldProp);

                // Add DataType based on field info
                AddProperty(doc, properties, "DataType", fieldInfo.DataType);
            }

            // Add Text property
            AddProperty(doc, properties, "Text", text);

            // Always set WrapText to true for DataLabels
            AddProperty(doc, properties, "WrapText", "true");
        }


        // Individual property methods
        private void AddHtmlEditorProperties(XmlDocument doc, XmlElement properties,
                                            Dictionary<string, string> controlToFieldMap,
                                            string controlGuid, Dictionary<string, FieldInfo> fieldMap)
        {
            AddProperty(doc, properties, "DataType", "Memo");
            AddProperty(doc, properties, "WatermarkText", "Type a value");
            AddProperty(doc, properties, "Height", "250px");
            AddProperty(doc, properties, "Width", "100%");
            AddProperty(doc, properties, "TabIndex", "0");
            AddProperty(doc, properties, "HasToolbar", "true");
            AddProperty(doc, properties, "CustomControlViewDesign", "true");
            AddProperty(doc, properties, "CustomControlViewHtml", "true");
            AddProperty(doc, properties, "CustomControlViewPreview", "false");
            AddProperty(doc, properties, "ToolbarItems", "(Default)");
            AddProperty(doc, properties, "IsReadOnly", "false");
            AddFieldBinding(doc, properties, controlToFieldMap, controlGuid, fieldMap);
        }

        private void AddAutoCompleteProperties(XmlDocument doc, XmlElement properties, JObject controlDef,
                                              Dictionary<string, string> controlToFieldMap,
                                              string controlGuid, Dictionary<string, FieldInfo> fieldMap)
        {
            AddFieldBinding(doc, properties, controlToFieldMap, controlGuid, fieldMap);
            AddProperty(doc, properties, "WaterMarkText", "Type to search...");
            AddProperty(doc, properties, "DataType", "text");

            JArray validValues = controlDef["ValidValues"] as JArray;
            if (validValues != null && validValues.Count > 0)
            {
                List<string> displayValues = new List<string>();
                List<object> fixedListArray = new List<object>();

                foreach (JObject value in validValues)
                {
                    string itemValue = value["Value"]?.Value<string>() ?? "";
                    string displayText = value["DisplayText"]?.Value<string>() ?? itemValue;
                    bool isDefault = value["IsDefault"]?.Value<bool>() ?? false;

                    displayValues.Add(displayText);
                    var listItem = new
                    {
                        value = itemValue,
                        display = displayText,
                        isDefault = isDefault
                    };
                    fixedListArray.Add(listItem);
                }

                XmlElement fixedListProp = doc.CreateElement("Property");
                XmlHelper.AddElement(doc, fixedListProp, "Name", "FixedListItems");
                XmlHelper.AddElement(doc, fixedListProp, "DisplayValue", string.Join("; ", displayValues));
                string jsonValue = Newtonsoft.Json.JsonConvert.SerializeObject(fixedListArray);
                XmlHelper.AddElement(doc, fixedListProp, "Value", jsonValue);
                properties.AppendChild(fixedListProp);
            }
        }

        private void AddHyperlinkProperties(XmlDocument doc, XmlElement properties,
                                           Dictionary<string, string> controlToFieldMap,
                                           string controlGuid, Dictionary<string, FieldInfo> fieldMap)
        {
            AddProperty(doc, properties, "DataType", "Hyperlink");
            AddProperty(doc, properties, "ShowPreview", "false");
            AddProperty(doc, properties, "AddressWatermark", "Click here to specify hyperlink URL");
            AddProperty(doc, properties, "DescriptionWatermark", "Click here to specify hyperlink text");
            AddProperty(doc, properties, "TestLinkText", "Click here to test");
            AddProperty(doc, properties, "DisplayType", "input");
            AddProperty(doc, properties, "Delimiter", ", ");
            AddFieldBinding(doc, properties, controlToFieldMap, controlGuid, fieldMap);
        }

        private void AddRadioButtonListProperties(XmlDocument doc, XmlElement properties, JObject controlDef,
                                                 Dictionary<string, string> controlToFieldMap,
                                                 string controlGuid, Dictionary<string, FieldInfo> fieldMap)
        {
            AddProperty(doc, properties, "DataType", "Text");
            AddProperty(doc, properties, "WaterMarkText", "No items to display");
            AddFieldBinding(doc, properties, controlToFieldMap, controlGuid, fieldMap);
            AddStaticListItems(doc, properties, controlDef);
        }

        private void AddCheckBoxListProperties(XmlDocument doc, XmlElement properties, JObject controlDef,
                                              Dictionary<string, string> controlToFieldMap,
                                              string controlGuid, Dictionary<string, FieldInfo> fieldMap)
        {
            AddProperty(doc, properties, "DataType", "Text");
            AddProperty(doc, properties, "WaterMarkText", "No items to display");
            AddProperty(doc, properties, "SelectionMode", "Multiple");
            AddFieldBinding(doc, properties, controlToFieldMap, controlGuid, fieldMap);
            AddStaticListItems(doc, properties, controlDef);
        }

        private void AddStaticListItems(XmlDocument doc, XmlElement properties, JObject controlDef)
        {
            JArray validValues = controlDef["ValidValues"] as JArray;
            if (validValues != null && validValues.Count > 0)
            {
                List<string> displayValues = new List<string>();
                List<object> fixedListArray = new List<object>();

                foreach (JObject value in validValues)
                {
                    string itemValue = value["Value"]?.Value<string>() ?? "";
                    string displayText = value["DisplayText"]?.Value<string>() ?? itemValue;
                    bool isDefault = value["IsDefault"]?.Value<bool>() ?? false;

                    displayValues.Add(displayText);
                    var listItem = new
                    {
                        value = itemValue,
                        display = displayText,
                        isDefault = isDefault
                    };
                    fixedListArray.Add(listItem);
                }

                XmlElement fixedListProp = doc.CreateElement("Property");
                XmlHelper.AddElement(doc, fixedListProp, "Name", "FixedListItems");
                XmlHelper.AddElement(doc, fixedListProp, "DisplayValue", string.Join("; ", displayValues));
                string jsonValue = Newtonsoft.Json.JsonConvert.SerializeObject(fixedListArray);
                XmlHelper.AddElement(doc, fixedListProp, "Value", jsonValue);
                properties.AppendChild(fixedListProp);
            }
        }

        private void AddPickerProperties(XmlDocument doc, XmlElement properties)
        {
            AddProperty(doc, properties, "DataType", "Text");
            AddProperty(doc, properties, "WaterMarkText", "Type a value");
        }

        private void AddPictureProperties(XmlDocument doc, XmlElement properties, JObject controlDef)
        {
            if (controlDef["DefaultImageUrl"] != null)
            {
                string imageUrl = controlDef["DefaultImageUrl"].Value<string>();
                AddProperty(doc, properties, "Source", imageUrl);
            }
        }

        private void AddFilePostBackProperties(XmlDocument doc, XmlElement properties,
                                              Dictionary<string, string> controlToFieldMap,
                                              string controlGuid, Dictionary<string, FieldInfo> fieldMap)
        {
            AddProperty(doc, properties, "DataType", "File");
            AddProperty(doc, properties, "WatermarkText", "Click here to attach a file");
            AddFieldBinding(doc, properties, controlToFieldMap, controlGuid, fieldMap);
        }

        private void AddImagePostBackProperties(XmlDocument doc, XmlElement properties,
                                               Dictionary<string, string> controlToFieldMap,
                                               string controlGuid, Dictionary<string, FieldInfo> fieldMap)
        {
            AddProperty(doc, properties, "DataType", "Image");
            AddProperty(doc, properties, "WatermarkText", "Click here to attach an image");
            AddFieldBinding(doc, properties, controlToFieldMap, controlGuid, fieldMap);
        }

        private void AddButtonProperties(XmlDocument doc, XmlElement properties, string buttonText, string controlType, JObject controlDef)
        {
            AddProperty(doc, properties, "Text", buttonText);

            // Add toolbar button specific properties
            if (controlType == "ToolBarButton" && controlDef != null)
            {
                var additionalProps = controlDef["AdditionalProperties"];
                if (additionalProps != null)
                {
                    // Add button type for toolbar buttons
                    var buttonType = additionalProps["buttonType"]?.Value<string>();
                    if (!string.IsNullOrEmpty(buttonType))
                    {
                        AddProperty(doc, properties, "ButtonType", buttonType);
                    }

                    // Add icon class for toolbar buttons
                    var iconClass = additionalProps["iconClass"]?.Value<string>();
                    if (!string.IsNullOrEmpty(iconClass))
                    {
                        AddProperty(doc, properties, "IconClass", iconClass);
                    }

                    // Add image class for toolbar buttons
                    var imageClass = additionalProps["imageClass"]?.Value<string>();
                    if (!string.IsNullOrEmpty(imageClass))
                    {
                        AddProperty(doc, properties, "ImageClass", imageClass);
                    }
                }
            }
        }

        private void AddTextBoxProperties(XmlDocument doc, XmlElement properties,
                                         Dictionary<string, string> controlToFieldMap,
                                         string controlGuid, Dictionary<string, FieldInfo> fieldMap,
                                         JObject controlDef = null)
        {
            AddFieldBinding(doc, properties, controlToFieldMap, controlGuid, fieldMap);
            AddProperty(doc, properties, "WaterMarkText", "Type a value");

            // Check for InfoPath formatting properties in AdditionalProperties
            string boundProp = controlDef?["AdditionalProperties"]?["boundProp"]?.ToString();
            string datafmt = controlDef?["AdditionalProperties"]?["datafmt"]?.ToString();

            Console.WriteLine($"        [FORMATTING] Checking TextBox formatting: boundProp='{boundProp}', datafmt='{datafmt}'");

            // Apply DataType based on boundProp if available
            if (!string.IsNullOrEmpty(boundProp))
            {
                K2FormatBuilder.UpdateDataTypeFromBoundProp(properties.ParentNode as XmlElement, boundProp);
            }
            else
            {
                AddProperty(doc, properties, "DataType", "text");
            }

            // Apply InfoPath formatting if available
            bool shouldApply = K2FormatBuilder.ShouldApplyFormatting("TextBox", datafmt, boundProp);
            Console.WriteLine($"        [FORMATTING] ShouldApplyFormatting returned: {shouldApply}");
            if (shouldApply)
            {
                XmlElement controlElement = properties.ParentNode as XmlElement;
                Console.WriteLine($"        [FORMATTING] controlElement is null: {controlElement == null}");
                if (controlElement != null)
                {
                    Console.WriteLine($"        [FORMATTING] Calling ApplyFormattingToControl...");
                    bool applied = K2FormatBuilder.ApplyFormattingToControl(controlElement, datafmt, boundProp);
                    Console.WriteLine($"        [FORMATTING] ApplyFormattingToControl returned: {applied}");
                    if (applied)
                    {
                        Console.WriteLine($"        Applied InfoPath formatting to TextBox: datafmt='{datafmt}', boundProp='{boundProp}'");
                    }
                }
            }
        }

        private void AddTextAreaProperties(XmlDocument doc, XmlElement properties,
                                          Dictionary<string, string> controlToFieldMap,
                                          string controlGuid, Dictionary<string, FieldInfo> fieldMap)
        {
            AddFieldBinding(doc, properties, controlToFieldMap, controlGuid, fieldMap);
            AddProperty(doc, properties, "WaterMarkText", "Type a value");
            AddProperty(doc, properties, "DataType", "text");
            AddProperty(doc, properties, "Rows", "5");
        }

        private void AddCalendarProperties(XmlDocument doc, XmlElement properties,
                                          Dictionary<string, string> controlToFieldMap,
                                          string controlGuid, Dictionary<string, FieldInfo> fieldMap,
                                          JObject controlDef = null)
        {
            AddFieldBinding(doc, properties, controlToFieldMap, controlGuid, fieldMap);
            AddProperty(doc, properties, "DataType", "datetime");
            AddProperty(doc, properties, "CalendarPicker", "datePicker");
            AddProperty(doc, properties, "WaterMarkText", "Select a date");

            // Apply InfoPath date formatting if available
            string boundProp = controlDef?["AdditionalProperties"]?["boundProp"]?.ToString();
            string datafmt = controlDef?["AdditionalProperties"]?["datafmt"]?.ToString();

            if (K2FormatBuilder.ShouldApplyFormatting("TextBox", datafmt, boundProp))
            {
                XmlElement controlElement = properties.ParentNode as XmlElement;
                if (controlElement != null)
                {
                    bool applied = K2FormatBuilder.ApplyFormattingToControl(controlElement, datafmt, boundProp);
                    if (applied)
                    {
                        Console.WriteLine($"        Applied InfoPath date formatting to Calendar: datafmt='{datafmt}', boundProp='{boundProp}'");
                    }
                }
            }
        }

        private void AddCheckBoxProperties(XmlDocument doc, XmlElement properties, string text,
                                          Dictionary<string, string> controlToFieldMap,
                                          string controlGuid, Dictionary<string, FieldInfo> fieldMap)
        {
            AddFieldBinding(doc, properties, controlToFieldMap, controlGuid, fieldMap);
            AddProperty(doc, properties, "Text", text);
            AddProperty(doc, properties, "DataType", "yesno");
        }

        private void AddDropDownProperties(XmlDocument doc, XmlElement properties,
                                          Dictionary<string, string> controlToFieldMap,
                                          string controlGuid, Dictionary<string, FieldInfo> fieldMap)
        {
            AddFieldBinding(doc, properties, controlToFieldMap, controlGuid, fieldMap);
            AddProperty(doc, properties, "WaterMarkText", "Select an item");
            AddProperty(doc, properties, "DataType", "text");
        }

        private void AddLabelProperties(XmlDocument doc, XmlElement properties, string text)
        {
            AddProperty(doc, properties, "Text", text);
            AddProperty(doc, properties, "LiteralVal", "false");
            AddProperty(doc, properties, "WrapText", "true");
        }

        private void AddProperty(XmlDocument doc, XmlElement properties, string name, string value)
        {
            XmlElement prop = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, prop, "Name", name);
            XmlHelper.AddElement(doc, prop, "DisplayValue", value);
            XmlHelper.AddElement(doc, prop, "Value", value);
            properties.AppendChild(prop);
        }

        private void AddFieldBinding(XmlDocument doc, XmlElement properties,
                                    Dictionary<string, string> controlToFieldMap,
                                    string controlGuid, Dictionary<string, FieldInfo> fieldMap)
        {
            if (controlToFieldMap.ContainsKey(controlGuid) && fieldMap.ContainsKey(controlToFieldMap[controlGuid]))
            {
                FieldInfo fieldInfo = fieldMap[controlToFieldMap[controlGuid]];
                XmlElement fieldProp = doc.CreateElement("Property");
                XmlHelper.AddElement(doc, fieldProp, "Name", "Field");
                string fieldDisplayName = fieldInfo.DisplayName.Replace("_", " ");
                XmlHelper.AddElement(doc, fieldProp, "DisplayValue", fieldDisplayName);
                XmlHelper.AddElement(doc, fieldProp, "NameValue", fieldDisplayName);
                XmlHelper.AddElement(doc, fieldProp, "Value", controlToFieldMap[controlGuid]);
                properties.AppendChild(fieldProp);
            }
        }

 
        private string DetectAndExtractTitleLabel(JArray controls)
        {
            // Find all labels in row 1
            var row1Labels = controls
                .Where(c => c["Type"]?.Value<string>()?.ToLower() == "label" &&
                            c["GridPosition"]?.Value<string>() != null &&
                            ExtractRowNumber(c["GridPosition"].Value<string>()) == 1)
                .ToList();

            if (!row1Labels.Any())
                return null;

            // If there's exactly one label on row 1 and there are other controls on row 2+
            if (row1Labels.Count == 1)
            {
                // Check if there are any non-label controls on subsequent rows
                bool hasOtherControls = controls.Any(c =>
                {
                    string gridPos = c["GridPosition"]?.Value<string>();
                    if (string.IsNullOrEmpty(gridPos))
                        return false;

                    int row = ExtractRowNumber(gridPos);
                    string type = c["Type"]?.Value<string>()?.ToLower();

                    // Look for any control on row 2 or later that isn't a label
                    return row >= 2 && type != "label";
                });

                if (hasOtherControls)
                {
                    // This single label on row 1 is likely a title
                    string titleText = row1Labels.First()["Label"]?.Value<string>();
                    if (!string.IsNullOrEmpty(titleText))
                    {
                        Console.WriteLine($"        Detected title label: '{titleText}' (single label on row 1)");
                        return titleText;
                    }
                }
            }

            return null;
        }


        private XmlElement CreateSourcesSection(XmlDocument doc, string smoGuid, string smoName,
                                               Dictionary<string, FieldInfo> fieldMap,
                                               Dictionary<string, LookupInfo> lookupSmartObjects)
        {
            XmlElement sources = doc.CreateElement("Sources");

            // Add main source for the primary SmartObject
            XmlElement source = doc.CreateElement("Source");
            string sourceGuid = Guid.NewGuid().ToString();
            source.SetAttribute("ID", sourceGuid);
            source.SetAttribute("SourceType", "Object");
            source.SetAttribute("SourceID", smoGuid);
            source.SetAttribute("SourceName", smoName);
            source.SetAttribute("SourceDisplayName", smoName.Replace("_", " "));
            source.SetAttribute("ContextType", "Primary");
            source.SetAttribute("ContextID", smoGuid);

            XmlHelper.AddElement(doc, source, "Name", smoName);

            // Add Fields section for main SmartObject
            XmlElement fields = doc.CreateElement("Fields");
            foreach (var kvp in fieldMap)
            {
                XmlElement field = doc.CreateElement("Field");
                field.SetAttribute("ID", kvp.Key);
                field.SetAttribute("Type", "ObjectProperty");
                field.SetAttribute("DataType", kvp.Value.DataType);

                XmlHelper.AddElement(doc, field, "Name", kvp.Value.DisplayName);
                XmlHelper.AddElement(doc, field, "FieldName", kvp.Value.FieldName);
                XmlHelper.AddElement(doc, field, "FieldDisplayName", kvp.Value.DisplayName);

                fields.AppendChild(field);
            }
            source.AppendChild(fields);
            XmlHelper.AddElement(doc, source, "DisplayName", smoName.Replace("_", " "));
            sources.AppendChild(source);

            // Add sources for each lookup SmartObject
            foreach (var lookup in lookupSmartObjects)
            {
                XmlElement lookupSource = CreateLookupSource(doc, lookup.Value);
                sources.AppendChild(lookupSource);
            }

            return sources;
        }

        private XmlElement CreateLookupSource(XmlDocument doc, LookupInfo lookup)
        {
            XmlElement lookupSource = doc.CreateElement("Source");
            string lookupSourceGuid = Guid.NewGuid().ToString();

            lookupSource.SetAttribute("ID", lookupSourceGuid);
            lookupSource.SetAttribute("SourceType", "Object");
            lookupSource.SetAttribute("SourceID", lookup.SmartObjectGuid);
            lookupSource.SetAttribute("SourceName", lookup.SmartObjectName);
            lookupSource.SetAttribute("SourceDisplayName", lookup.SmartObjectName.Replace("_", " "));
            lookupSource.SetAttribute("ContextType", "External");
            lookupSource.SetAttribute("ContextID", lookup.ControlId);
            lookupSource.SetAttribute("ValidationStatus", "Auto");
            lookupSource.SetAttribute("ValidationMessages",
                $"SourceObject,Object,Auto,{lookup.SmartObjectGuid},{lookup.SmartObjectName},{lookup.SmartObjectName.Replace("_", " ")}");

            XmlHelper.AddElement(doc, lookupSource, "Name", lookup.SmartObjectName);

            // Add Fields for lookup SmartObject
            XmlElement lookupFields = doc.CreateElement("Fields");

            // ID field
            AddLookupField(doc, lookupFields, "ID", "ID", "autonumber");

            // Value field
            AddLookupField(doc, lookupFields, "Value", "Value", "text");

            // DisplayText field
            AddLookupField(doc, lookupFields, "Display Text", "DisplayText", "Text");

            // Sort Order field
            AddLookupField(doc, lookupFields, "Sort Order", "SortOrder", "Number", true);

            // IsActive field
            AddLookupField(doc, lookupFields, "Is Active", "IsActive", "YesNo", true);

            lookupSource.AppendChild(lookupFields);
            XmlHelper.AddElement(doc, lookupSource, "DisplayName", lookup.SmartObjectName.Replace("_", " "));

            return lookupSource;
        }

        private void AddLookupField(XmlDocument doc, XmlElement parent, string displayName,
                                   string fieldName, string dataType, bool isMissing = false)
        {
            XmlElement field = doc.CreateElement("Field");
            string fieldGuid = Guid.NewGuid().ToString();
            field.SetAttribute("ID", fieldGuid);
            field.SetAttribute("Type", "ObjectProperty");
            field.SetAttribute("DataType", dataType);

            if (isMissing)
            {
                field.SetAttribute("ValidationStatus", "Missing");
                field.SetAttribute("ValidationMessages",
                    $"FieldProperty,ObjectProperty,Missing,,{fieldName},{displayName}");
            }

            XmlHelper.AddElement(doc, field, "Name", displayName);
            XmlHelper.AddElement(doc, field, "FieldName", fieldName);
            XmlHelper.AddElement(doc, field, "FieldDisplayName", displayName);
            parent.AppendChild(field);
        }

        private XmlElement CreateTranslationsSection(XmlDocument doc, string viewName, JArray controls)
        {
            XmlElement translations = doc.CreateElement("Translations");

            foreach (JObject control in controls)
            {
                string label = control["Label"]?.Value<string>();
                string name = control["Name"]?.Value<string>();
                string type = control["Type"]?.Value<string>();

                if (!string.IsNullOrEmpty(label) && !string.IsNullOrEmpty(name))
                {
                    XmlElement translation = doc.CreateElement("Translation");
                    translation.SetAttribute("Key", GenerateTranslationKey(label));

                    string k2Type = MapJsonToK2ControlType(type);
                    string direction = $"View: {viewName} - Control: {k2Type} {NameSanitizer.SanitizePropertyName(name)} - Property: Text";
                    translation.SetAttribute("Direction", direction);
                    translation.InnerText = label;

                    translations.AppendChild(translation);
                }
            }

            return translations;
        }

        // Helper methods
        private string GetBaseFormName(string smoName)
        {
            if (smoName.Contains("_"))
            {
                int lastUnderscore = smoName.LastIndexOf('_');
                string possibleParentName = smoName.Substring(0, lastUnderscore);

                if (_smoGenerator.CheckSmartObjectExists(possibleParentName))
                {
                    return possibleParentName;
                }
            }

            return smoName;
        }

        private bool IsDataBoundControl(string k2ControlType)
        {
            string[] dataBoundTypes = {
                "TextBox", "TextArea", "HTMLEditor", "Calendar", "CheckBox",
                "DropDown", "AutoComplete", "RadioButtonList", "CheckBoxList",
                "Picker", "FilePostBack", "ImagePostBack", "SharePointHyperLink"
            };
            return dataBoundTypes.Contains(k2ControlType);
        }

        private string GetUniqueControlName(string baseName, HashSet<string> usedNames, ref int counter)
        {
            string uniqueName = baseName;
            while (usedNames.Contains(uniqueName))
            {
                uniqueName = $"{baseName}_{counter}";
                counter++;
            }
            return uniqueName;
        }

        private string GetControlTypeDisplayName(string k2ControlType)
        {
            switch (k2ControlType)
            {
                case "HTMLEditor": return "Rich Text";
                case "SharePointHyperLink": return "Hyperlink";
                case "RadioButtonList": return "Radio Button List";
                case "CheckBoxList": return "Check Box List";
                case "FilePostBack": return "File Attachment";
                case "ImagePostBack": return "Image Attachment";
                case "ToolBarButton": return "ToolBar Button";
                default: return k2ControlType;
            }
        }

        private string GenerateTranslationKey(string text)
        {
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text);
                byte[] hash = sha256.ComputeHash(bytes);
                return Convert.ToBase64String(hash).Substring(0, 43) + "=";
            }
        }

        private string MapJsonToK2ControlType(string jsonType)
        {
            Console.WriteLine($"        Mapping type: {jsonType}");

            switch (jsonType?.ToLower())
            {
                case "plaintext":
                case "textfield":
                    return "TextBox";
                case "richtext":
                    return "HTMLEditor";
                case "textarea":
                    return "TextArea";
                case "datepicker":
                case "dtpicker":
                    return "Calendar";
                case "dropdown":
                    return "DropDown";
                case "combobox":
                    return "AutoComplete";
                case "checkbox":
                    return "CheckBox";
                case "multipleselectlist":
                    return "RadioButtonList";
                case "bulletedlist":
                case "numberedlist":
                case "plainnumberedlist":
                    return "RadioButtonList";
                case "fileattachment":
                case "sharepoint:sharepointfileattachment":
                case "sharepointfileattachment":
                    return "FilePostBack";
                case "inlinepicture":
                    return "Picture";
                case "linkedpicture":
                    return "ImagePostBack";
                case "button":
                    return "Button";
                case "toolbarbutton":
                    return "ToolBarButton";
                case "hyperlink":
                    return "SharePointHyperLink";
                case "expresionbox":
                case "expressionbox":
                    return "TextBox";
                case "signatureline":
                    return "Picture";
                case "section":
                case "optionalsection":
                case "repeatingtable":
                case "repeatingsection":
                    return null; // Skip these - they create child SmartObjects
                case "label":
                    return "Label";
                default:
                    Console.WriteLine($"        WARNING: Unknown type '{jsonType}', skipping control");
                    return null;
            }
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

        // Replace the AddItemViewButtons method in ViewXmlBuilder.cs with this updated version:

        public void AddItemViewButtons(XmlDocument doc, XmlElement controlsElement,
                           XmlElement canvas, Dictionary<string, string> controlIdMap)
        {
            // Check if buttons have already been added to prevent duplicates
            if (controlIdMap.ContainsKey("ButtonRow"))
            {
                Console.WriteLine("        Buttons already added to view, skipping");
                return;
            }

            // Determine how many columns exist in the view
            int maxColumn = 4; // Default minimum
            foreach (var key in controlIdMap.Keys)
            {
                if (key.StartsWith("Column"))
                {
                    string columnNumber = key.Replace("Column", "");
                    if (int.TryParse(columnNumber, out int colNum))
                    {
                        if (colNum + 1 > maxColumn)
                        {
                            maxColumn = colNum + 1;
                        }
                    }
                }
            }

            Console.WriteLine($"        Adding buttons to item view with {maxColumn} columns");

            // Use simple, clean names without random suffixes
            string buttonRowName = "ButtonRow";

            // Create row control
            string buttonRowGuid = Guid.NewGuid().ToString();
            XmlElement buttonRowControl = doc.CreateElement("Control");
            buttonRowControl.SetAttribute("ID", buttonRowGuid);
            buttonRowControl.SetAttribute("Type", "Row");

            XmlElement rowProps = doc.CreateElement("Properties");
            XmlElement rowNameProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, rowNameProp, "Name", "ControlName");
            XmlHelper.AddElement(doc, rowNameProp, "DisplayValue", buttonRowName);
            XmlHelper.AddElement(doc, rowNameProp, "Value", buttonRowName);
            rowProps.AppendChild(rowNameProp);
            buttonRowControl.AppendChild(rowProps);

            XmlElement rowStyles = doc.CreateElement("Styles");
            XmlElement rowStyle = doc.CreateElement("Style");
            rowStyle.SetAttribute("IsDefault", "True");
            rowStyles.AppendChild(rowStyle);
            buttonRowControl.AppendChild(rowStyles);

            XmlHelper.AddElement(doc, buttonRowControl, "Name", buttonRowName);
            XmlHelper.AddElement(doc, buttonRowControl, "DisplayName", buttonRowName);
            controlsElement.AppendChild(buttonRowControl);
            controlIdMap[buttonRowName] = buttonRowGuid;

            // Create cell controls for all columns
            string[] cellGuids = new string[maxColumn];
            for (int i = 0; i < maxColumn; i++)
            {
                cellGuids[i] = Guid.NewGuid().ToString();
                XmlElement cellControl = doc.CreateElement("Control");
                cellControl.SetAttribute("ID", cellGuids[i]);
                cellControl.SetAttribute("Type", "Cell");

                string cellName = $"ButtonCell{i}";

                XmlElement cellProps = doc.CreateElement("Properties");
                XmlElement cellNameProp = doc.CreateElement("Property");
                XmlHelper.AddElement(doc, cellNameProp, "Name", "ControlName");
                XmlHelper.AddElement(doc, cellNameProp, "DisplayValue", cellName);
                XmlHelper.AddElement(doc, cellNameProp, "Value", cellName);
                cellProps.AppendChild(cellNameProp);
                cellControl.AppendChild(cellProps);

                // Add right alignment style to the last cell (for Add button)
                if (i == maxColumn - 1)
                {
                    XmlElement cellStyles = doc.CreateElement("Styles");
                    XmlElement cellStyle = doc.CreateElement("Style");
                    cellStyle.SetAttribute("IsDefault", "True");
                    XmlElement textElement = doc.CreateElement("Text");
                    XmlElement alignElement = doc.CreateElement("Align");
                    alignElement.InnerText = "Right";
                    textElement.AppendChild(alignElement);
                    cellStyle.AppendChild(textElement);
                    cellStyles.AppendChild(cellStyle);
                    cellControl.AppendChild(cellStyles);
                }
                else
                {
                    XmlElement cellStyles = doc.CreateElement("Styles");
                    XmlElement cellStyle = doc.CreateElement("Style");
                    cellStyle.SetAttribute("IsDefault", "True");
                    cellStyles.AppendChild(cellStyle);
                    cellControl.AppendChild(cellStyles);
                }

                XmlHelper.AddElement(doc, cellControl, "Name", cellName);
                XmlHelper.AddElement(doc, cellControl, "DisplayName", cellName);
                controlsElement.AppendChild(cellControl);
                controlIdMap[cellName] = cellGuids[i];
            }

            // Create Cancel button with clean name
            string cancelButtonGuid = Guid.NewGuid().ToString();
            string cancelButtonName = "Cancel";
            XmlElement cancelButton = CreateButton(doc, cancelButtonName, "Cancel", cancelButtonGuid);
            controlsElement.AppendChild(cancelButton);
            controlIdMap[cancelButtonName] = cancelButtonGuid;

            // Create Add button with clean name
            string addButtonGuid = Guid.NewGuid().ToString();
            string addButtonName = "Add";
            XmlElement addButton = CreateButton(doc, addButtonName, "Add", addButtonGuid);
            controlsElement.AppendChild(addButton);
            controlIdMap[addButtonName] = addButtonGuid;

            // Update canvas with the new button row structure
            AddButtonRowToCanvas(doc, canvas, buttonRowGuid, cellGuids, cancelButtonGuid,
                                addButtonGuid, maxColumn);

            // FIXED: Get the actual view name from the document
            string viewName = "";
            XmlNodeList viewNodes = doc.GetElementsByTagName("View");
            if (viewNodes.Count > 0)
            {
                XmlElement viewElement = (XmlElement)viewNodes[0];
                XmlNodeList nameNodes = viewElement.GetElementsByTagName("Name");
                if (nameNodes.Count > 0)
                {
                    viewName = nameNodes[0].InnerText;
                }
            }

            // If we still don't have the view name, try to get it from the View control
            if (string.IsNullOrEmpty(viewName))
            {
                XmlNodeList viewControls = doc.GetElementsByTagName("Control");
                foreach (XmlElement control in viewControls)
                {
                    if (control.GetAttribute("Type") == "View")
                    {
                        XmlNodeList nameNodes = control.GetElementsByTagName("Name");
                        if (nameNodes.Count > 0)
                        {
                            viewName = nameNodes[0].InnerText;
                            break;
                        }
                    }
                }
            }

            // Track the buttons with the correct view name
            if (!string.IsNullOrEmpty(viewName))
            {
                ButtonTracker.ButtonInfo buttonInfo = new ButtonTracker.ButtonInfo
                {
                    AddButtonId = addButtonGuid,
                    AddButtonName = "Add Button",
                    CancelButtonId = cancelButtonGuid,
                    CancelButtonName = "Cancel"
                };

                ButtonTracker.RegisterViewButtons(viewName, buttonInfo);
            }
            else
            {
                Console.WriteLine("        WARNING: Could not determine view name for button tracking");
            }

            Console.WriteLine($"        Added Cancel button in column 0 and Add button in column {maxColumn - 1} to item view");
        }
        private void AddButtonRowToCanvas(XmlDocument doc, XmlElement canvas,
                             string rowGuid, string[] cellGuids,
                             string cancelButtonGuid, string addButtonGuid,
                             int maxColumn)
        {
            XmlNodeList sections = canvas.GetElementsByTagName("Sections");
            if (sections.Count == 0) return;

            XmlElement section = (XmlElement)sections[0].FirstChild;
            XmlNodeList tableControls = section.GetElementsByTagName("Control");
            if (tableControls.Count == 0) return;

            XmlElement tableControl = (XmlElement)tableControls[0];
            XmlNodeList rows = tableControl.GetElementsByTagName("Rows");
            if (rows.Count == 0) return;

            XmlElement rowsElement = (XmlElement)rows[0];

            // Create button row in canvas
            XmlElement buttonRow = doc.CreateElement("Row");
            buttonRow.SetAttribute("ID", rowGuid);

            XmlElement cells = doc.CreateElement("Cells");

            // Create cells for all columns
            for (int i = 0; i < maxColumn; i++)
            {
                XmlElement cell = doc.CreateElement("Cell");
                cell.SetAttribute("ID", cellGuids[i]);
                cell.SetAttribute("ColumnSpan", "1");
                cell.SetAttribute("RowSpan", "1");

                if (i == 0)
                {
                    // First cell: Cancel button
                    XmlElement cancelControl = doc.CreateElement("Control");
                    cancelControl.SetAttribute("ID", cancelButtonGuid);
                    cell.AppendChild(cancelControl);
                }
                else if (i == maxColumn - 1)
                {
                    // Last cell: Add button (right-aligned)
                    XmlElement addControl = doc.CreateElement("Control");
                    addControl.SetAttribute("ID", addButtonGuid);
                    cell.AppendChild(addControl);
                }
                // All other cells remain empty

                cells.AppendChild(cell);
            }

            buttonRow.AppendChild(cells);
            rowsElement.AppendChild(buttonRow);
        }
        private XmlElement CreateButton(XmlDocument doc, string name, string text, string guid)
        {
            // Use the new reusable ControlFactory
            return ControlFactory.CreateViewButton(doc, guid, $"{name} Button", text);
        }
       
        private int ExtractColumnNumber(string gridPosition)
        {
            if (string.IsNullOrEmpty(gridPosition))
                return 0;

            // Extract letter part (e.g., "A", "B", "C", "D", "E", "F", etc.)
            string letterPart = new string(gridPosition.SkipWhile(char.IsDigit).ToArray());
            if (string.IsNullOrEmpty(letterPart))
                return 0;

            // Convert letter(s) to column number (A=0, B=1, ..., Z=25, AA=26, etc.)
            int columnNumber = 0;
            foreach (char c in letterPart.ToUpper())
            {
                columnNumber = columnNumber * 26 + (c - 'A');
            }
            return columnNumber;
        }
    }
}