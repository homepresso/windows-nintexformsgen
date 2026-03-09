using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Newtonsoft.Json.Linq;
using K2SmartObjectGenerator.Models;
using K2SmartObjectGenerator.Utilities;

namespace K2SmartObjectGenerator
{
    public class ViewRulesBuilder
    {
        private HashSet<string> _usedEventIds;
        private HashSet<string> _usedHandlerIds;

        // Maps K2 control GUIDs to their deployed K2 display names (e.g., "ROUNDTRIP CheckBox")
        // Built from the deployed XML during Phase 2 so rules use actual K2 control names.
        private Dictionary<string, string> _deployedControlNameMap;
        private string _deployedViewDisplayName;

        public ViewRulesBuilder()
        {
            _usedEventIds = new HashSet<string>();
            _usedHandlerIds = new HashSet<string>();
            _deployedControlNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Builds a map from control GUID to K2 control display name by parsing the deployed view XML.
        /// Also extracts the view's deployed display name.
        /// </summary>
        private void BuildDeployedControlNameMap(XmlDocument doc, string fallbackViewName)
        {
            _deployedControlNameMap.Clear();
            _deployedViewDisplayName = fallbackViewName;

            try
            {
                // Get the view display name
                var viewNode = doc.SelectSingleNode("//View") as XmlElement;
                if (viewNode != null)
                {
                    var viewDisplayNameNode = viewNode.SelectSingleNode("DisplayName");
                    if (viewDisplayNameNode != null && !string.IsNullOrEmpty(viewDisplayNameNode.InnerText))
                    {
                        _deployedViewDisplayName = viewDisplayNameNode.InnerText;
                    }
                    else
                    {
                        var viewNameNode = viewNode.SelectSingleNode("Name");
                        if (viewNameNode != null && !string.IsNullOrEmpty(viewNameNode.InnerText))
                        {
                            _deployedViewDisplayName = viewNameNode.InnerText;
                        }
                    }
                }

                // Build control GUID → display name map
                var controls = doc.GetElementsByTagName("Control");
                foreach (XmlElement ctrl in controls)
                {
                    string id = ctrl.GetAttribute("ID");
                    if (string.IsNullOrEmpty(id)) continue;

                    // First try the Name child element
                    var nameNode = ctrl.SelectSingleNode("Name");
                    string controlName = nameNode?.InnerText;

                    // Fall back to ControlName property
                    if (string.IsNullOrEmpty(controlName))
                    {
                        var propsNode = ctrl.SelectSingleNode("Properties");
                        if (propsNode != null)
                        {
                            foreach (XmlNode prop in propsNode.ChildNodes)
                            {
                                var propName = prop.SelectSingleNode("Name");
                                if (propName?.InnerText == "ControlName")
                                {
                                    controlName = prop.SelectSingleNode("Value")?.InnerText;
                                    break;
                                }
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(controlName) && !_deployedControlNameMap.ContainsKey(id))
                    {
                        _deployedControlNameMap[id] = controlName;
                    }
                }

                Console.WriteLine($"    Built deployed control name map: {_deployedControlNameMap.Count} controls, view display name: '{_deployedViewDisplayName}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    WARNING: Failed to build control name map: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the K2 deployed display name for a control, falling back to the provided name.
        /// </summary>
        private string GetDeployedControlName(string controlId, string fallbackName)
        {
            if (!string.IsNullOrEmpty(controlId) && _deployedControlNameMap.TryGetValue(controlId, out string deployedName))
            {
                return deployedName;
            }
            return fallbackName;
        }

        /// <summary>
        /// Creates the full events section with structural events AND rule events.
        /// This is the legacy method kept for backward compatibility.
        /// For two-phase deployment, use CreateStructuralEvents() first, then AddRuleEvents() after deploy.
        /// </summary>
        public XmlElement CreateEventsWithRules(XmlDocument doc, string viewGuid, string viewName,
                                               Dictionary<string, string> controlIdMap,
                                               Dictionary<string, string> controlToFieldMap,
                                               Dictionary<string, FieldInfo> fieldMap,
                                               Dictionary<string, LookupInfo> lookupSmartObjects,
                                               JArray dynamicSections, JObject conditionalVisibility,
                                               JArray controls, Dictionary<string, string> jsonToK2ControlIdMap)
        {
            // Phase 1: Create structural events only
            var controlEventMap = new Dictionary<string, XmlElement>(StringComparer.OrdinalIgnoreCase);
            XmlElement events = CreateStructuralEvents(doc, viewGuid, viewName, controlIdMap,
                controlToFieldMap, fieldMap, lookupSmartObjects, dynamicSections, controls,
                jsonToK2ControlIdMap, controlEventMap);

            // Phase 2: Add rule events on top
            AddRuleEvents(doc, events, viewGuid, viewName, controlIdMap,
                dynamicSections, conditionalVisibility, controls, jsonToK2ControlIdMap, controlEventMap);

            return events;
        }

        /// <summary>
        /// Creates ONLY structural events: Init (data loading) + OnChange (data binding).
        /// These are required for the view to function and should be deployed FIRST.
        /// Rule events (visibility, conditional, InfoPath) are added later via AddRuleEvents().
        /// </summary>
        public XmlElement CreateStructuralEvents(XmlDocument doc, string viewGuid, string viewName,
                                               Dictionary<string, string> controlIdMap,
                                               Dictionary<string, string> controlToFieldMap,
                                               Dictionary<string, FieldInfo> fieldMap,
                                               Dictionary<string, LookupInfo> lookupSmartObjects,
                                               JArray dynamicSections,
                                               JArray controls, Dictionary<string, string> jsonToK2ControlIdMap,
                                               Dictionary<string, XmlElement> controlEventMap)
        {
            XmlElement events = doc.CreateElement("Events");

            // Create standard Init event with visibility initialization
            XmlElement initEvent = CreateInitEvent(doc, viewGuid, viewName, controlIdMap,
                controlToFieldMap, fieldMap, lookupSmartObjects, dynamicSections, controls, jsonToK2ControlIdMap);
            events.AppendChild(initEvent);

            // Add standard OnChange events for data binding
            foreach (var mapping in controlToFieldMap)
            {
                string controlId = mapping.Key;
                string fieldId = mapping.Value;
                string controlName = controlIdMap.FirstOrDefault(x => x.Value == controlId).Key;

                if (!string.IsNullOrEmpty(controlName) && fieldMap.ContainsKey(fieldId))
                {
                    XmlElement changeEvent = CreateOnChangeEvent(doc, controlId, controlName,
                        viewGuid, viewName, fieldId);
                    events.AppendChild(changeEvent);
                    controlEventMap[controlId] = changeEvent;
                }
            }

            return events;
        }

        /// <summary>
        /// Adds rule events (visibility + conditional visibility) to an existing events section.
        /// Called in phase 2 AFTER the view has been deployed with structural events.
        /// Works on either freshly-built XML or XML fetched back from K2 via GetViewDefinition().
        /// </summary>
        public void AddRuleEvents(XmlDocument doc, XmlElement events, string viewGuid, string viewName,
                                  Dictionary<string, string> controlIdMap,
                                  JArray dynamicSections, JObject conditionalVisibility,
                                  JArray controls, Dictionary<string, string> jsonToK2ControlIdMap,
                                  Dictionary<string, XmlElement> controlEventMap)
        {
            // Build the deployed control name map from the XML so we can use actual K2 control names
            BuildDeployedControlNameMap(doc, viewName);

            // Add visibility rules for dynamic sections
            if (dynamicSections != null && dynamicSections.Count > 0)
            {
                Console.WriteLine($"    Processing {dynamicSections.Count} dynamic sections for visibility rules");
                AddVisibilityRules(doc, events, dynamicSections, controlIdMap, viewGuid, viewName,
                    controls, jsonToK2ControlIdMap, controlEventMap);
            }

            // Add conditional visibility rules
            if (conditionalVisibility != null)
            {
                Console.WriteLine($"    Processing conditional visibility rules");
                AddConditionalVisibilityRules(doc, events, conditionalVisibility, controlIdMap,
                    viewGuid, viewName, controlEventMap);
            }
        }

        private XmlElement CreateInitEvent(XmlDocument doc, string viewGuid, string viewName,
                                         Dictionary<string, string> controlIdMap,
                                         Dictionary<string, string> controlToFieldMap,
                                         Dictionary<string, FieldInfo> fieldMap,
                                         Dictionary<string, LookupInfo> lookupSmartObjects,
                                         JArray dynamicSections, JArray controls,
                                         Dictionary<string, string> jsonToK2ControlIdMap)
        {
            XmlElement initEvent = doc.CreateElement("Event");
            string eventGuid = Guid.NewGuid().ToString();
            initEvent.SetAttribute("ID", eventGuid);
            initEvent.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            initEvent.SetAttribute("Type", "System");
            initEvent.SetAttribute("SourceID", viewGuid);
            initEvent.SetAttribute("SourceType", "View");
            initEvent.SetAttribute("SourceName", viewName);
            initEvent.SetAttribute("SourceDisplayName", viewName);

            XmlHelper.AddElement(doc, initEvent, "Name", "Init");

            // Add Properties element with ViewID, RuleFriendlyName, and Location
            XmlElement eventProps = doc.CreateElement("Properties");

            XmlElement evtViewIdProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, evtViewIdProp, "Name", "ViewID");
            XmlHelper.AddElement(doc, evtViewIdProp, "DisplayValue", viewName);
            XmlHelper.AddElement(doc, evtViewIdProp, "NameValue", viewName);
            XmlHelper.AddElement(doc, evtViewIdProp, "Value", viewGuid);
            eventProps.AppendChild(evtViewIdProp);

            XmlElement evtRuleFriendlyProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, evtRuleFriendlyProp, "Name", "RuleFriendlyName");
            XmlHelper.AddElement(doc, evtRuleFriendlyProp, "Value", $"When the View '{viewName}' executed Initialized");
            eventProps.AppendChild(evtRuleFriendlyProp);

            XmlElement evtLocationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, evtLocationProp, "Name", "Location");
            XmlHelper.AddElement(doc, evtLocationProp, "Value", viewName);
            eventProps.AppendChild(evtLocationProp);

            initEvent.AppendChild(eventProps);

            XmlElement handlers = doc.CreateElement("Handlers");
            XmlElement handler = doc.CreateElement("Handler");
            handler.SetAttribute("ID", Guid.NewGuid().ToString());
            handler.SetAttribute("DefinitionID", Guid.NewGuid().ToString());

            XmlElement actions = doc.CreateElement("Actions");

            // Add Calculate action
            XmlElement calcAction = doc.CreateElement("Action");
            calcAction.SetAttribute("ID", Guid.NewGuid().ToString());
            calcAction.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            calcAction.SetAttribute("Type", "Calculate");
            calcAction.SetAttribute("ExecutionType", "Synchronous");
            actions.AppendChild(calcAction);

            // Add ApplyStyle action
            XmlElement styleAction = doc.CreateElement("Action");
            styleAction.SetAttribute("ID", Guid.NewGuid().ToString());
            styleAction.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            styleAction.SetAttribute("Type", "ApplyStyle");
            styleAction.SetAttribute("ExecutionType", "Synchronous");
            actions.AppendChild(styleAction);

            // Add Transfer action for field mappings
            XmlElement transferAction = doc.CreateElement("Action");
            transferAction.SetAttribute("ID", Guid.NewGuid().ToString());
            transferAction.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            transferAction.SetAttribute("Type", "Transfer");
            transferAction.SetAttribute("ExecutionType", "Synchronous");

            XmlElement transferProps = doc.CreateElement("Properties");
            XmlElement viewIdProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, viewIdProp, "Name", "ViewID");
            XmlHelper.AddElement(doc, viewIdProp, "DisplayValue", viewName);
            XmlHelper.AddElement(doc, viewIdProp, "NameValue", viewName);
            XmlHelper.AddElement(doc, viewIdProp, "Value", viewGuid);
            transferProps.AppendChild(viewIdProp);
            transferAction.AppendChild(transferProps);

            // Add parameters for field mappings
            XmlElement parameters = doc.CreateElement("Parameters");
            foreach (var mapping in controlToFieldMap)
            {
                string controlId = mapping.Key;
                string fieldId = mapping.Value;

                if (fieldMap.ContainsKey(fieldId))
                {
                    XmlElement parameter = doc.CreateElement("Parameter");
                    parameter.SetAttribute("SourceID", controlId);
                    parameter.SetAttribute("SourceType", "Control");

                    string controlName = controlIdMap.FirstOrDefault(x => x.Value == controlId).Key;
                    if (!string.IsNullOrEmpty(controlName))
                    {
                        parameter.SetAttribute("SourceName", controlName);
                        parameter.SetAttribute("SourceDisplayName", controlName);
                    }

                    parameter.SetAttribute("TargetID", fieldId);
                    parameter.SetAttribute("TargetType", "ViewField");
                    parameters.AppendChild(parameter);
                }
            }
            transferAction.AppendChild(parameters);
            actions.AppendChild(transferAction);

            // Add Execute actions for each dropdown with SmartObject binding
            foreach (var lookup in lookupSmartObjects)
            {
                XmlElement executeAction = CreateDropdownExecuteAction(doc, lookup.Value, viewGuid, viewName);
                actions.AppendChild(executeAction);
                Console.WriteLine($"        Added GetList action for dropdown: {lookup.Value.ControlName}");
            }

            // Add initial visibility actions based on checkbox default values
            // Skip for item views - item views are shown/hidden as a whole via AreaItem visibility,
            // not by hiding individual controls. Section-level visibility rules (e.g., itemPosition)
            // are InfoPath-specific and don't apply in K2's item/list view pattern.
            bool isItemView = viewName.EndsWith("_Item", StringComparison.OrdinalIgnoreCase);
            if (dynamicSections != null && dynamicSections.Count > 0 && !isItemView)
            {
                AddInitialVisibilityActions(doc, actions, dynamicSections, controls, controlIdMap,
                    viewGuid, viewName, jsonToK2ControlIdMap);
            }
            else if (isItemView)
            {
                Console.WriteLine("        Skipping initial visibility actions for item view (controls always visible)");
            }

            handler.AppendChild(actions);
            handlers.AppendChild(handler);
            initEvent.AppendChild(handlers);

            return initEvent;
        }

        private XmlElement CreateDropdownExecuteAction(XmlDocument doc, LookupInfo lookup,
                                                    string viewGuid, string viewName)
        {
            XmlElement executeAction = doc.CreateElement("Action");
            executeAction.SetAttribute("ID", Guid.NewGuid().ToString());
            executeAction.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            executeAction.SetAttribute("Type", "Execute");
            executeAction.SetAttribute("ExecutionType", "Synchronous");

            XmlElement executeProps = doc.CreateElement("Properties");

            XmlElement methodProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, methodProp, "Name", "Method");
            XmlHelper.AddElement(doc, methodProp, "DisplayValue", "Get List");
            XmlHelper.AddElement(doc, methodProp, "NameValue", "GetList");
            XmlHelper.AddElement(doc, methodProp, "Value", "GetList");
            executeProps.AppendChild(methodProp);

            XmlElement executeViewIdProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, executeViewIdProp, "Name", "ViewID");
            XmlHelper.AddElement(doc, executeViewIdProp, "DisplayValue", viewName);
            XmlHelper.AddElement(doc, executeViewIdProp, "NameValue", viewName);
            XmlHelper.AddElement(doc, executeViewIdProp, "Value", viewGuid);
            executeProps.AppendChild(executeViewIdProp);

            XmlElement controlIdProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, controlIdProp, "Name", "ControlID");
            XmlHelper.AddElement(doc, controlIdProp, "DisplayValue", lookup.ControlName);
            XmlHelper.AddElement(doc, controlIdProp, "NameValue", lookup.ControlName);
            XmlHelper.AddElement(doc, controlIdProp, "Value", lookup.ControlId);
            executeProps.AppendChild(controlIdProp);

            XmlElement objectIdProp = doc.CreateElement("Property");
            objectIdProp.SetAttribute("ValidationStatus", "Auto");
            objectIdProp.SetAttribute("ValidationMessages",
                $"ActionObject,Object,Auto,{lookup.SmartObjectGuid},{lookup.SmartObjectName},{lookup.SmartObjectName.Replace("_", " ")}");
            XmlHelper.AddElement(doc, objectIdProp, "Name", "ObjectID");
            XmlHelper.AddElement(doc, objectIdProp, "DisplayValue", lookup.SmartObjectName.Replace("_", " "));
            XmlHelper.AddElement(doc, objectIdProp, "NameValue", lookup.SmartObjectName);
            XmlHelper.AddElement(doc, objectIdProp, "Value", lookup.SmartObjectGuid);
            executeProps.AppendChild(objectIdProp);

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "DisplayValue", viewName);
            XmlHelper.AddElement(doc, locationProp, "NameValue", viewName);
            XmlHelper.AddElement(doc, locationProp, "Value", "View");
            executeProps.AppendChild(locationProp);

            // ADD FILTER PROPERTY HERE for consolidated lookup
            if (!string.IsNullOrEmpty(lookup.LookupParameter))
            {
                XmlElement filterProp = doc.CreateElement("Property");
                XmlHelper.AddElement(doc, filterProp, "Name", "Filter");

                // Create filter XML for LookupType = parameter value
                // K2 requires PropertyExpressions (Equals) to be wrapped in a LogicalExpression (And)
                string filterXml = $@"<Filter isSimple=""True""><And><Equals>" +
                    $@"<Item SourceType=""ObjectProperty"" SourceID=""LookupType"" DataType=""Text"" " +
                    $@"SourceName=""LookupType"" SourceDisplayName=""Lookup Type"">LookupType</Item>" +
                    $@"<Item SourceType=""Value""><SourceValue>{lookup.LookupParameter}</SourceValue></Item>" +
                    $@"</Equals></And></Filter>";

                XmlHelper.AddElement(doc, filterProp, "Value", filterXml);
                executeProps.AppendChild(filterProp);

                Console.WriteLine($"          Added filter for LookupType = '{lookup.LookupParameter}'");
            }

            executeAction.AppendChild(executeProps);

            XmlElement results = doc.CreateElement("Results");
            XmlElement result = doc.CreateElement("Result");
            result.SetAttribute("SourceID", lookup.SmartObjectGuid);
            result.SetAttribute("SourceName", lookup.SmartObjectName);
            result.SetAttribute("SourceDisplayName", lookup.SmartObjectName.Replace("_", " "));
            result.SetAttribute("SourceType", "Result");
            result.SetAttribute("TargetID", lookup.ControlId);
            result.SetAttribute("TargetName", lookup.ControlName);
            result.SetAttribute("TargetDisplayName", lookup.ControlName);
            result.SetAttribute("TargetType", "Control");
            result.SetAttribute("ValidationStatus", "Auto");
            result.SetAttribute("ValidationMessages",
                $"ResultMappingSource,Object,Auto,{lookup.SmartObjectGuid},{lookup.SmartObjectName},{lookup.SmartObjectName.Replace("_", " ")}");
            results.AppendChild(result);
            executeAction.AppendChild(results);

            return executeAction;
        }

        private void AddInitialVisibilityActions(XmlDocument doc, XmlElement actions, JArray dynamicSections,
                                                JArray controls, Dictionary<string, string> controlIdMap,
                                                string viewGuid, string viewName,
                                                Dictionary<string, string> jsonToK2ControlIdMap)
        {
            Console.WriteLine("        Setting initial visibility states based on default values");

            HashSet<string> processedTriggers = new HashSet<string>();

            foreach (JObject section in dynamicSections)
            {
                string ctrlId = section["CtrlId"]?.Value<string>();
                string conditionField = section["ConditionField"]?.Value<string>();
                JArray controlsToToggle = section["Controls"] as JArray;
                JArray fieldNames = section["FieldNames"] as JArray;

                // Skip sections with no controls AND no field names
                if ((controlsToToggle == null || controlsToToggle.Count == 0) &&
                    (fieldNames == null || fieldNames.Count == 0))
                    continue;

                string triggerControlId = null;
                string triggerFieldName = null;

                // Determine trigger control dynamically
                if (!string.IsNullOrEmpty(ctrlId))
                {
                    if (jsonToK2ControlIdMap.ContainsKey(ctrlId))
                    {
                        triggerControlId = jsonToK2ControlIdMap[ctrlId];

                        foreach (JObject ctrl in controls)
                        {
                            if (ctrl["CtrlId"]?.Value<string>() == ctrlId)
                            {
                                triggerFieldName = ctrl["Name"]?.Value<string>()?.ToUpper();
                                break;
                            }
                        }

                        if (string.IsNullOrEmpty(triggerFieldName))
                        {
                            triggerFieldName = ctrlId;
                        }
                    }
                }

                if (string.IsNullOrEmpty(triggerControlId) && !string.IsNullOrEmpty(conditionField))
                {
                    triggerFieldName = conditionField.ToUpper();

                    foreach (JObject ctrl in controls)
                    {
                        string name = ctrl["Name"]?.Value<string>();

                        if (name?.ToUpper() == triggerFieldName ||
                            (conditionField.StartsWith("is") && conditionField.Length > 2 &&
                             name?.ToUpper() == conditionField.Substring(2).ToUpper()))
                        {
                            string controlCtrlId = ctrl["CtrlId"]?.Value<string>();
                            if (!string.IsNullOrEmpty(controlCtrlId) && jsonToK2ControlIdMap.ContainsKey(controlCtrlId))
                            {
                                triggerControlId = jsonToK2ControlIdMap[controlCtrlId];
                                break;
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(triggerControlId))
                    {
                        triggerControlId = FindControlIdByFieldName(triggerFieldName, controlIdMap);
                    }
                }

                if (string.IsNullOrEmpty(triggerControlId) ||
                    string.IsNullOrEmpty(triggerFieldName) ||
                    processedTriggers.Contains(triggerControlId))
                {
                    continue;
                }

                processedTriggers.Add(triggerControlId);

                // Get the default value of the checkbox
                bool defaultValue = GetCheckboxDefaultValue(triggerFieldName, controls, ctrlId, conditionField);

                // Get all controls in the section
                List<string> allSectionControls = GetAllControlsInSection(dynamicSections, section,
                    controls, controlIdMap, jsonToK2ControlIdMap);

                if (allSectionControls.Count == 0)
                {
                    Console.WriteLine($"        No controls found to set initial visibility for {triggerFieldName}");
                    continue;
                }

                Console.WriteLine($"        {triggerFieldName} default value: {defaultValue}, " +
                                 $"setting initial visibility for {allSectionControls.Count} controls");

                // Create Transfer actions to set initial visibility for each control
                foreach (string targetControlId in allSectionControls)
                {
                    if (!string.IsNullOrEmpty(targetControlId))
                    {
                        XmlElement visibilityAction = CreateTransferAction(doc, targetControlId,
                            controlIdMap.FirstOrDefault(x => x.Value == targetControlId).Key,
                            defaultValue, viewGuid, viewName);
                        actions.AppendChild(visibilityAction);
                    }
                }
            }
        }

        private bool GetCheckboxDefaultValue(string checkboxFieldName, JArray controls,
                                            string ctrlId, string conditionField)
        {
            foreach (JObject control in controls)
            {
                if (control == null) continue;

                string controlCtrlId = control["CtrlId"]?.Value<string>();
                string name = control["Name"]?.Value<string>();
                string type = control["Type"]?.Value<string>();

                bool isMatch = false;

                if (!string.IsNullOrEmpty(controlCtrlId) && controlCtrlId == ctrlId)
                {
                    isMatch = true;
                }
                else if (!string.IsNullOrEmpty(conditionField) && !string.IsNullOrEmpty(name))
                {
                    string fieldToMatch = conditionField;
                    if (conditionField.StartsWith("is") && conditionField.Length > 2)
                    {
                        fieldToMatch = conditionField.Substring(2);
                    }

                    if (name.Equals(fieldToMatch, StringComparison.OrdinalIgnoreCase) ||
                        name.Equals(checkboxFieldName, StringComparison.OrdinalIgnoreCase))
                    {
                        isMatch = true;
                    }
                }

                if (isMatch && type?.ToLower() == "checkbox")
                {
                    JToken defaultValueToken = control["DefaultValue"];
                    if (defaultValueToken != null && defaultValueToken.Type != JTokenType.Null)
                    {
                        if (defaultValueToken.Type == JTokenType.Boolean)
                        {
                            return defaultValueToken.Value<bool>();
                        }
                        else if (defaultValueToken.Type == JTokenType.String)
                        {
                            bool result;
                            if (bool.TryParse(defaultValueToken.Value<string>(), out result))
                                return result;
                        }
                    }
                    else
                    {
                        JToken isCheckedToken = control["IsChecked"];
                        if (isCheckedToken != null && isCheckedToken.Type != JTokenType.Null)
                        {
                            if (isCheckedToken.Type == JTokenType.Boolean)
                            {
                                return isCheckedToken.Value<bool>();
                            }
                            else if (isCheckedToken.Type == JTokenType.String)
                            {
                                bool result;
                                if (bool.TryParse(isCheckedToken.Value<string>(), out result))
                                    return result;
                            }
                        }
                    }
                    return false;
                }
            }

            return false;
        }

        private XmlElement CreateOnChangeEvent(XmlDocument doc, string controlId, string controlName,
                                              string viewGuid, string viewName, string fieldId)
        {
            XmlElement changeEvent = doc.CreateElement("Event");
            changeEvent.SetAttribute("ID", Guid.NewGuid().ToString());
            changeEvent.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            changeEvent.SetAttribute("Type", "System");
            changeEvent.SetAttribute("SourceID", controlId);
            changeEvent.SetAttribute("SourceType", "Control");
            changeEvent.SetAttribute("SourceName", controlName);
            changeEvent.SetAttribute("SourceDisplayName", controlName);

            XmlHelper.AddElement(doc, changeEvent, "Name", "OnChange");

            XmlElement handlers = doc.CreateElement("Handlers");
            XmlElement handler = doc.CreateElement("Handler");
            handler.SetAttribute("ID", Guid.NewGuid().ToString());
            handler.SetAttribute("DefinitionID", Guid.NewGuid().ToString());

            XmlElement actions = doc.CreateElement("Actions");

            // Add Transfer action
            XmlElement transferAction = doc.CreateElement("Action");
            transferAction.SetAttribute("ID", Guid.NewGuid().ToString());
            transferAction.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            transferAction.SetAttribute("Type", "Transfer");
            transferAction.SetAttribute("ExecutionType", "Synchronous");

            XmlElement props = doc.CreateElement("Properties");
            XmlElement viewIdProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, viewIdProp, "Name", "ViewID");
            XmlHelper.AddElement(doc, viewIdProp, "DisplayValue", viewName);
            XmlHelper.AddElement(doc, viewIdProp, "NameValue", viewName);
            XmlHelper.AddElement(doc, viewIdProp, "Value", viewGuid);
            props.AppendChild(viewIdProp);
            transferAction.AppendChild(props);

            XmlElement parameters = doc.CreateElement("Parameters");
            XmlElement parameter = doc.CreateElement("Parameter");
            parameter.SetAttribute("SourceID", controlId);
            parameter.SetAttribute("SourceName", controlName);
            parameter.SetAttribute("SourceDisplayName", controlName);
            parameter.SetAttribute("SourceType", "Control");
            parameter.SetAttribute("TargetID", fieldId);
            parameter.SetAttribute("TargetType", "ViewField");
            parameters.AppendChild(parameter);
            transferAction.AppendChild(parameters);

            actions.AppendChild(transferAction);

            // Add Calculate action
            XmlElement calcAction = doc.CreateElement("Action");
            calcAction.SetAttribute("ID", Guid.NewGuid().ToString());
            calcAction.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            calcAction.SetAttribute("Type", "Calculate");
            calcAction.SetAttribute("ExecutionType", "Synchronous");
            actions.AppendChild(calcAction);

            // Add ApplyStyle action
            XmlElement styleAction = doc.CreateElement("Action");
            styleAction.SetAttribute("ID", Guid.NewGuid().ToString());
            styleAction.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            styleAction.SetAttribute("Type", "ApplyStyle");
            styleAction.SetAttribute("ExecutionType", "Synchronous");
            actions.AppendChild(styleAction);

            // Add Validate action
            XmlElement validateAction = doc.CreateElement("Action");
            validateAction.SetAttribute("ID", Guid.NewGuid().ToString());
            validateAction.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            validateAction.SetAttribute("Type", "Validate");
            validateAction.SetAttribute("ExecutionType", "Synchronous");

            XmlElement validateProps = doc.CreateElement("Properties");
            XmlElement valViewIdProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, valViewIdProp, "Name", "ViewID");
            XmlHelper.AddElement(doc, valViewIdProp, "DisplayValue", viewName);
            XmlHelper.AddElement(doc, valViewIdProp, "NameValue", viewName);
            XmlHelper.AddElement(doc, valViewIdProp, "Value", viewGuid);
            validateProps.AppendChild(valViewIdProp);

            XmlElement valControlIdProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, valControlIdProp, "Name", "ControlID");
            XmlHelper.AddElement(doc, valControlIdProp, "DisplayValue", controlName);
            XmlHelper.AddElement(doc, valControlIdProp, "NameValue", controlName);
            XmlHelper.AddElement(doc, valControlIdProp, "Value", controlId);
            validateProps.AppendChild(valControlIdProp);
            validateAction.AppendChild(validateProps);
            actions.AppendChild(validateAction);

            handler.AppendChild(actions);
            handlers.AppendChild(handler);
            changeEvent.AppendChild(handlers);

            return changeEvent;
        }

        private void AddVisibilityRules(XmlDocument doc, XmlElement events, JArray dynamicSections,
                                       Dictionary<string, string> controlIdMap, string viewGuid,
                                       string viewName, JArray controls,
                                       Dictionary<string, string> jsonToK2ControlIdMap,
                                       Dictionary<string, XmlElement> controlEventMap = null)
        {
            Console.WriteLine($"    Adding visibility rules from {dynamicSections.Count} dynamic sections");

            HashSet<string> processedTriggerControls = new HashSet<string>();

            foreach (JObject section in dynamicSections)
            {
                string ctrlId = section["CtrlId"]?.Value<string>();
                string conditionField = section["ConditionField"]?.Value<string>();
                string conditionValue = section["ConditionValue"]?.Value<string>();
                JArray controlsToToggle = section["Controls"] as JArray;
                JArray fieldNames = section["FieldNames"] as JArray;

                Console.WriteLine($"      --- Section CtrlId={ctrlId}, ConditionField={conditionField}, ConditionValue={conditionValue}");
                Console.WriteLine($"          Controls: [{(controlsToToggle != null ? string.Join(", ", controlsToToggle.Select(c => c.Value<string>())) : "null")}]");
                Console.WriteLine($"          FieldNames: [{(fieldNames != null ? string.Join(", ", fieldNames.Select(f => f.Value<string>())) : "null")}]");

                if ((controlsToToggle == null || controlsToToggle.Count == 0) &&
                    (fieldNames == null || fieldNames.Count == 0))
                {
                    Console.WriteLine($"      Skipping section with no controls and no field names to toggle");
                    continue;
                }

                List<string> allSectionControls = GetAllControlsInSection(dynamicSections, section,
                    controls, controlIdMap, jsonToK2ControlIdMap);

                int explicitCount = controlsToToggle != null ? controlsToToggle.Count : 0;
                Console.WriteLine($"      Section has {explicitCount} explicit controls + {(fieldNames != null ? fieldNames.Count : 0)} field names, " +
                                 $"expanded to {allSectionControls.Count} total controls (including labels)");

                if (allSectionControls.Count == 0)
                {
                    Console.WriteLine($"      WARNING: No controls found for section, skipping");
                    continue;
                }

                string triggerControlId = null;
                string triggerFieldName = null;

                // Strategy 1: Use conditionField FIRST to find the actual data control (e.g. checkbox).
                // The section's CtrlId (e.g., CTRL57) often maps to a K2 Section layout control,
                // NOT the data checkbox. conditionField (e.g., "isRoundTrip") points to the actual
                // data field that controls visibility.
                if (!string.IsNullOrEmpty(conditionField))
                {
                    triggerFieldName = conditionField.ToUpper();

                    // Try to find a data control matching conditionField
                    foreach (JObject ctrl in controls)
                    {
                        string name = ctrl["Name"]?.Value<string>();

                        if (name?.ToUpper() == triggerFieldName ||
                            (conditionField.StartsWith("is") && name?.ToUpper() == conditionField.Substring(2).ToUpper()))
                        {
                            string controlCtrlId = ctrl["CtrlId"]?.Value<string>();
                            if (!string.IsNullOrEmpty(controlCtrlId) && jsonToK2ControlIdMap.ContainsKey(controlCtrlId))
                            {
                                triggerControlId = jsonToK2ControlIdMap[controlCtrlId];
                                break;
                            }
                        }
                    }

                    // Also try controlIdMap (which maps K2 field names to K2 control GUIDs)
                    if (string.IsNullOrEmpty(triggerControlId))
                    {
                        triggerControlId = FindControlIdByFieldName(triggerFieldName, controlIdMap);
                    }

                    // Also try with "is" prefix stripped (e.g., "isRoundTrip" -> "ROUNDTRIP")
                    if (string.IsNullOrEmpty(triggerControlId) && conditionField.StartsWith("is") && conditionField.Length > 2)
                    {
                        triggerControlId = FindControlIdByFieldName(conditionField.Substring(2).ToUpper(), controlIdMap);
                        if (!string.IsNullOrEmpty(triggerControlId))
                        {
                            triggerFieldName = conditionField.Substring(2).ToUpper();
                        }
                    }
                }

                // Strategy 2: Fall back to ctrlId only if conditionField didn't resolve
                if (string.IsNullOrEmpty(triggerControlId) && !string.IsNullOrEmpty(ctrlId))
                {
                    if (jsonToK2ControlIdMap.ContainsKey(ctrlId))
                    {
                        triggerControlId = jsonToK2ControlIdMap[ctrlId];

                        foreach (JObject ctrl in controls)
                        {
                            if (ctrl["CtrlId"]?.Value<string>() == ctrlId)
                            {
                                triggerFieldName = ctrl["Name"]?.Value<string>()?.ToUpper();
                                break;
                            }
                        }

                        if (string.IsNullOrEmpty(triggerFieldName))
                        {
                            triggerFieldName = ctrlId;
                        }
                    }
                }

                // Final name resolution: reverse-lookup the K2 control name from controlIdMap
                // to ensure we use the actual K2 control name, not a JSON artifact
                if (!string.IsNullOrEmpty(triggerControlId))
                {
                    foreach (var kvp in controlIdMap)
                    {
                        if (kvp.Value == triggerControlId)
                        {
                            triggerFieldName = kvp.Key;
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(triggerControlId))
                {
                    Console.WriteLine($"      WARNING: Could not find trigger control for CtrlId={ctrlId}, ConditionField={conditionField}");
                    continue;
                }

                if (processedTriggerControls.Contains(triggerControlId))
                {
                    Console.WriteLine($"      Skipping duplicate visibility rule for {triggerFieldName} (already processed)");
                    continue;
                }

                processedTriggerControls.Add(triggerControlId);

                Console.WriteLine($"      Creating visibility rule for {triggerFieldName} (ID: {triggerControlId})");

                JArray expandedControls = new JArray();
                foreach (string controlId in allSectionControls)
                {
                    expandedControls.Add(controlId);
                }

                // Check if there's already an event for this control (e.g., OnChange data binding)
                // K2 Form.EventInstance has a unique constraint that prevents two events with the same SourceID
                if (controlEventMap != null && controlEventMap.ContainsKey(triggerControlId))
                {
                    // Merge visibility handlers into the existing event
                    XmlElement existingEvent = controlEventMap[triggerControlId];
                    XmlElement visibilityEvent = CreateSectionVisibilityEvent(doc, triggerControlId,
                        triggerFieldName, expandedControls, controlIdMap, viewGuid, viewName);
                    if (visibilityEvent != null)
                    {
                        // Copy handlers from visibility event into existing event
                        var handlersNode = existingEvent.SelectSingleNode("Handlers") as XmlElement;
                        if (handlersNode == null)
                        {
                            handlersNode = doc.CreateElement("Handlers");
                            existingEvent.AppendChild(handlersNode);
                        }
                        foreach (XmlNode handler in visibilityEvent.SelectNodes("Handlers/Handler"))
                        {
                            handlersNode.AppendChild(doc.ImportNode(handler, true));
                        }
                        Console.WriteLine($"      Merged visibility rule into existing event for {triggerFieldName} " +
                                       $"affecting {allSectionControls.Count} controls");
                    }
                }
                else
                {
                    XmlElement visibilityEvent = CreateSectionVisibilityEvent(doc, triggerControlId,
                        triggerFieldName, expandedControls, controlIdMap, viewGuid, viewName);

                    if (visibilityEvent != null)
                    {
                        events.AppendChild(visibilityEvent);
                        if (controlEventMap != null)
                            controlEventMap[triggerControlId] = visibilityEvent;
                        Console.WriteLine($"      Successfully added visibility rule for {triggerFieldName} " +
                                       $"affecting {allSectionControls.Count} controls");
                    }
                }
            }
        }

        private XmlElement CreateSectionVisibilityEvent(XmlDocument doc, string checkboxControlId,
                                                       string checkboxFieldName, JArray allControlIds,
                                                       Dictionary<string, string> controlIdMap,
                                                       string viewGuid, string viewName)
        {
            // Use deployed K2 control name for display (e.g., "ROUNDTRIP CheckBox" instead of "ROUNDTRIP")
            string k2ControlName = GetDeployedControlName(checkboxControlId, checkboxFieldName);
            string viewDisplayName = _deployedViewDisplayName ?? viewName;

            XmlElement eventElement = doc.CreateElement("Event");
            string eventGuid = Guid.NewGuid().ToString();
            eventElement.SetAttribute("ID", eventGuid);
            eventElement.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            eventElement.SetAttribute("Type", "User");
            eventElement.SetAttribute("SourceID", checkboxControlId);
            eventElement.SetAttribute("SourceType", "Control");
            eventElement.SetAttribute("SourceName", k2ControlName);
            eventElement.SetAttribute("SourceDisplayName", k2ControlName);
            eventElement.SetAttribute("IsExtended", "True");

            XmlHelper.AddElement(doc, eventElement, "Name", "OnChange");

            XmlElement props = doc.CreateElement("Properties");

            XmlElement viewIdProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, viewIdProp, "Name", "ViewID");
            XmlHelper.AddElement(doc, viewIdProp, "NameValue", viewName);
            XmlHelper.AddElement(doc, viewIdProp, "Value", viewGuid);
            XmlHelper.AddElement(doc, viewIdProp, "DisplayValue", viewName);
            props.AppendChild(viewIdProp);

            XmlElement ruleFriendlyName = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, ruleFriendlyName, "Name", "RuleFriendlyName");
            XmlHelper.AddElement(doc, ruleFriendlyName, "Value", $"When {k2ControlName} is Changed");
            props.AppendChild(ruleFriendlyName);

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", viewName);
            props.AppendChild(locationProp);

            eventElement.AppendChild(props);

            XmlElement handlers = doc.CreateElement("Handlers");

            // Handler for checkbox = True (show all controls)
            XmlElement trueHandler = CreateSectionHandler(doc, checkboxControlId, checkboxFieldName,
                true, allControlIds, viewGuid, viewName, controlIdMap);
            if (trueHandler != null)
                handlers.AppendChild(trueHandler);

            // Handler for checkbox = False (hide all controls)
            XmlElement falseHandler = CreateSectionHandler(doc, checkboxControlId, checkboxFieldName,
                false, allControlIds, viewGuid, viewName, controlIdMap);
            if (falseHandler != null)
                handlers.AppendChild(falseHandler);

            // If no handlers resolved, don't create an empty event (would cause K2 errors)
            if (handlers.ChildNodes.Count == 0)
            {
                Console.WriteLine($"      WARNING: No handlers resolved for visibility event on '{checkboxFieldName}' - skipping event creation");
                return null;
            }

            eventElement.AppendChild(handlers);
            return eventElement;
        }

        private XmlElement CreateSectionHandler(XmlDocument doc, string checkboxControlId,
                                               string checkboxFieldName, bool checkForTrue,
                                               JArray controlIds, string viewGuid, string viewName,
                                               Dictionary<string, string> controlIdMap = null)
        {
            XmlElement handler = doc.CreateElement("Handler");
            handler.SetAttribute("ID", Guid.NewGuid().ToString());
            handler.SetAttribute("DefinitionID", Guid.NewGuid().ToString());

            XmlElement handlerProps = doc.CreateElement("Properties");

            XmlElement handlerName = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, handlerName, "Name", "HandlerName");
            XmlHelper.AddElement(doc, handlerName, "Value", "IfLogicalHandler");
            handlerProps.AppendChild(handlerName);

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", "view");  // Must be lowercase per K2 reference
            handlerProps.AppendChild(locationProp);

            handler.AppendChild(handlerProps);

            // Create condition
            XmlElement conditions = doc.CreateElement("Conditions");
            XmlElement condition = doc.CreateElement("Condition");
            condition.SetAttribute("ID", Guid.NewGuid().ToString());
            condition.SetAttribute("DefinitionID", Guid.NewGuid().ToString());

            XmlElement condProps = doc.CreateElement("Properties");

            XmlElement locProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locProp, "Name", "Location");
            XmlHelper.AddElement(doc, locProp, "Value", "View");
            condProps.AppendChild(locProp);

            XmlElement nameProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, nameProp, "Name", "Name");
            XmlHelper.AddElement(doc, nameProp, "Value", "SimpleEqualControlCondition");
            condProps.AppendChild(nameProp);

            condition.AppendChild(condProps);

            // Create expression - Equals goes directly in Expressions (no And wrapper)
            // Reference: checkbox-visibility.xml shows <Expressions><Equals>...</Equals></Expressions>
            XmlElement expressions = doc.CreateElement("Expressions");
            XmlElement equals = doc.CreateElement("Equals");

            // Use deployed K2 control names for accurate display
            string k2CheckboxName = GetDeployedControlName(checkboxControlId, checkboxFieldName);

            XmlElement sourceItem = doc.CreateElement("Item");
            sourceItem.SetAttribute("SourceType", "Control");
            sourceItem.SetAttribute("SourceID", checkboxControlId);
            sourceItem.SetAttribute("SourceName", k2CheckboxName);
            sourceItem.SetAttribute("SourceDisplayName", k2CheckboxName);
            sourceItem.SetAttribute("DataType", "Text");
            equals.AppendChild(sourceItem);

            XmlElement valueItem = doc.CreateElement("Item");
            valueItem.SetAttribute("SourceType", "Value");
            valueItem.SetAttribute("DataType", "Text");
            valueItem.InnerText = checkForTrue ? "true" : "false";
            equals.AppendChild(valueItem);

            expressions.AppendChild(equals);
            condition.AppendChild(expressions);
            conditions.AppendChild(condition);
            handler.AppendChild(conditions);

            // Create actions for ALL controls in the section
            XmlElement actions = doc.CreateElement("Actions");

            foreach (string controlId in controlIds)
            {
                if (!string.IsNullOrEmpty(controlId))
                {
                    bool makeVisible = checkForTrue;
                    // Use deployed K2 control name; fall back to reverse lookup from controlIdMap
                    string targetName = GetDeployedControlName(controlId, null);
                    if (string.IsNullOrEmpty(targetName))
                    {
                        targetName = $"Control_{controlId}";
                        if (controlIdMap != null)
                        {
                            foreach (var kvp in controlIdMap)
                            {
                                if (kvp.Value == controlId)
                                {
                                    targetName = kvp.Key;
                                    break;
                                }
                            }
                        }
                    }
                    XmlElement action = CreateTransferAction(doc, controlId,
                        targetName, makeVisible, viewGuid, viewName);
                    actions.AppendChild(action);
                }
            }

            // Only append if we have at least one action - empty Actions can cause red errors in K2
            if (actions.ChildNodes.Count > 0)
            {
                handler.AppendChild(actions);
            }
            else
            {
                Console.WriteLine($"      WARNING: No actions resolved for section handler (checkForTrue={checkForTrue}) - handler will have empty actions");
                return null;
            }
            return handler;
        }

        private void AddConditionalVisibilityRules(XmlDocument doc, XmlElement events,
                                                  JObject conditionalVisibility,
                                                  Dictionary<string, string> controlIdMap,
                                                  string viewGuid, string viewName,
                                                  Dictionary<string, XmlElement> controlEventMap = null)
        {
            foreach (var property in conditionalVisibility.Properties())
            {
                string fieldName = property.Name;
                JArray affectedControls = property.Value as JArray;

                if (affectedControls == null || affectedControls.Count == 0)
                    continue;

                string controlId = FindControlIdByFieldName(fieldName, controlIdMap);
                if (string.IsNullOrEmpty(controlId))
                    continue;

                // Create the visibility handlers
                XmlElement showHandler = CreateSimpleVisibilityHandler(doc, controlId, fieldName,
                    affectedControls, controlIdMap, true, viewGuid, viewName);
                XmlElement hideHandler = CreateSimpleVisibilityHandler(doc, controlId, fieldName,
                    affectedControls, controlIdMap, false, viewGuid, viewName);

                // Skip if neither handler resolved any actions
                if (showHandler == null && hideHandler == null)
                {
                    Console.WriteLine($"        Skipping conditional visibility for {fieldName} - no target controls resolved");
                    continue;
                }

                // Check if there's already an event for this control
                if (controlEventMap != null && controlEventMap.ContainsKey(controlId))
                {
                    // Merge handlers into existing event
                    XmlElement existingEvent = controlEventMap[controlId];
                    var handlersNode = existingEvent.SelectSingleNode("Handlers") as XmlElement;
                    if (handlersNode == null)
                    {
                        handlersNode = doc.CreateElement("Handlers");
                        existingEvent.AppendChild(handlersNode);
                    }
                    if (showHandler != null) handlersNode.AppendChild(showHandler);
                    if (hideHandler != null) handlersNode.AppendChild(hideHandler);
                    Console.WriteLine($"        Merged conditional visibility rule into existing event for field: {fieldName}");
                }
                else
                {
                    // Create new event - use deployed K2 control and view names
                    string k2ControlName = GetDeployedControlName(controlId, fieldName);
                    string viewDisplayName = _deployedViewDisplayName ?? viewName;

                    XmlElement changeEvent = doc.CreateElement("Event");
                    changeEvent.SetAttribute("ID", Guid.NewGuid().ToString());
                    changeEvent.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
                    changeEvent.SetAttribute("Type", "User");
                    changeEvent.SetAttribute("SourceID", controlId);
                    changeEvent.SetAttribute("SourceType", "Control");
                    changeEvent.SetAttribute("SourceName", k2ControlName);
                    changeEvent.SetAttribute("SourceDisplayName", k2ControlName);
                    changeEvent.SetAttribute("IsExtended", "True");

                    XmlHelper.AddElement(doc, changeEvent, "Name", "OnChange");

                    XmlElement props = doc.CreateElement("Properties");
                    XmlElement viewIdProp = doc.CreateElement("Property");
                    XmlHelper.AddElement(doc, viewIdProp, "Name", "ViewID");
                    XmlHelper.AddElement(doc, viewIdProp, "NameValue", viewName);
                    XmlHelper.AddElement(doc, viewIdProp, "Value", viewGuid);
                    XmlHelper.AddElement(doc, viewIdProp, "DisplayValue", viewName);
                    props.AppendChild(viewIdProp);

                    XmlElement ruleName = doc.CreateElement("Property");
                    XmlHelper.AddElement(doc, ruleName, "Name", "RuleFriendlyName");
                    XmlHelper.AddElement(doc, ruleName, "Value", $"When {k2ControlName} is Changed");
                    props.AppendChild(ruleName);

                    XmlElement locationProp = doc.CreateElement("Property");
                    XmlHelper.AddElement(doc, locationProp, "Name", "Location");
                    XmlHelper.AddElement(doc, locationProp, "Value", viewName);
                    props.AppendChild(locationProp);

                    changeEvent.AppendChild(props);

                    XmlElement handlers = doc.CreateElement("Handlers");
                    if (showHandler != null) handlers.AppendChild(showHandler);
                    if (hideHandler != null) handlers.AppendChild(hideHandler);

                    changeEvent.AppendChild(handlers);
                    events.AppendChild(changeEvent);

                    if (controlEventMap != null)
                        controlEventMap[controlId] = changeEvent;

                    Console.WriteLine($"        Added conditional visibility rule for field: {fieldName}");
                }
            }
        }

        private XmlElement CreateSimpleVisibilityHandler(XmlDocument doc, string controlId,
                                                        string fieldName, JArray affectedControls,
                                                        Dictionary<string, string> controlIdMap,
                                                        bool showControls, string viewGuid, string viewName)
        {
            XmlElement handler = doc.CreateElement("Handler");
            handler.SetAttribute("ID", Guid.NewGuid().ToString());
            handler.SetAttribute("DefinitionID", Guid.NewGuid().ToString());

            XmlElement handlerProps = doc.CreateElement("Properties");
            XmlElement handlerName = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, handlerName, "Name", "HandlerName");
            XmlHelper.AddElement(doc, handlerName, "Value", "IfLogicalHandler");
            handlerProps.AppendChild(handlerName);

            XmlElement handlerLocationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, handlerLocationProp, "Name", "Location");
            XmlHelper.AddElement(doc, handlerLocationProp, "Value", "view");
            handlerProps.AppendChild(handlerLocationProp);

            handler.AppendChild(handlerProps);

            // Create condition
            XmlElement conditions = doc.CreateElement("Conditions");
            XmlElement condition = doc.CreateElement("Condition");
            condition.SetAttribute("ID", Guid.NewGuid().ToString());
            condition.SetAttribute("DefinitionID", Guid.NewGuid().ToString());

            XmlElement condProps = doc.CreateElement("Properties");

            XmlElement condLocationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, condLocationProp, "Name", "Location");
            XmlHelper.AddElement(doc, condLocationProp, "Value", "View");
            condProps.AppendChild(condLocationProp);

            XmlElement condName = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, condName, "Name", "Name");
            XmlHelper.AddElement(doc, condName, "Value", "SimpleEqualControlCondition");
            condProps.AppendChild(condName);
            condition.AppendChild(condProps);

            // Create expression - comparison goes directly in Expressions (no And wrapper)
            // Reference: checkbox-visibility.xml shows <Expressions><Equals>...</Equals></Expressions>
            // BOTH handlers use <Equals> - the show handler checks =true, the hide handler checks =false
            XmlElement expressions = doc.CreateElement("Expressions");
            XmlElement equalsWrapper = doc.CreateElement("Equals");

            // Use deployed K2 control name for display
            string k2SourceName = GetDeployedControlName(controlId, fieldName);

            XmlElement sourceItem = doc.CreateElement("Item");
            sourceItem.SetAttribute("SourceType", "Control");
            sourceItem.SetAttribute("SourceID", controlId);
            sourceItem.SetAttribute("SourceName", k2SourceName);
            sourceItem.SetAttribute("SourceDisplayName", k2SourceName);
            sourceItem.SetAttribute("DataType", "Text");
            equalsWrapper.AppendChild(sourceItem);

            // Compare against "true" or "false" (not empty string)
            // Reference: checkbox-visibility.xml shows <Item SourceType="Value" DataType="Text">true</Item>
            XmlElement comparisonValue = doc.CreateElement("Item");
            comparisonValue.SetAttribute("SourceType", "Value");
            comparisonValue.SetAttribute("DataType", "Text");
            comparisonValue.InnerText = showControls ? "true" : "false";
            equalsWrapper.AppendChild(comparisonValue);

            expressions.AppendChild(equalsWrapper);

            condition.AppendChild(expressions);
            conditions.AppendChild(condition);
            handler.AppendChild(conditions);

            // Create actions
            XmlElement actions = doc.CreateElement("Actions");

            foreach (JValue controlName in affectedControls)
            {
                string targetControlId = FindControlIdByFieldName(controlName.Value<string>(), controlIdMap);
                if (!string.IsNullOrEmpty(targetControlId))
                {
                    // Use deployed K2 control name for display
                    string k2TargetName = GetDeployedControlName(targetControlId, controlName.Value<string>());
                    XmlElement action = CreateTransferAction(doc, targetControlId,
                        k2TargetName, showControls, viewGuid, viewName);
                    actions.AppendChild(action);
                }
            }

            // Only append if we have at least one action - empty Actions can cause red errors in K2
            if (actions.ChildNodes.Count > 0)
            {
                handler.AppendChild(actions);
            }
            else
            {
                Console.WriteLine($"      WARNING: No actions resolved for conditional visibility handler (showControls={showControls}) - returning null");
                return null;
            }
            return handler;
        }

        private XmlElement CreateTransferAction(XmlDocument doc, string targetControlId,
                                               string targetControlName, bool makeVisible,
                                               string viewGuid, string viewName)
        {
            string viewDisplayName = _deployedViewDisplayName ?? viewName;

            XmlElement action = doc.CreateElement("Action");
            action.SetAttribute("ID", Guid.NewGuid().ToString());
            action.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            action.SetAttribute("Type", "Transfer");
            action.SetAttribute("ExecutionType", "Synchronous");

            XmlElement props = doc.CreateElement("Properties");

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", "View");
            props.AppendChild(locationProp);

            XmlElement controlProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, controlProp, "Name", "ControlID");
            XmlHelper.AddElement(doc, controlProp, "DisplayValue", targetControlName);
            XmlHelper.AddElement(doc, controlProp, "NameValue", targetControlName);
            XmlHelper.AddElement(doc, controlProp, "Value", targetControlId);
            props.AppendChild(controlProp);

            XmlElement viewProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, viewProp, "Name", "ViewID");
            XmlHelper.AddElement(doc, viewProp, "NameValue", viewName);
            XmlHelper.AddElement(doc, viewProp, "Value", viewGuid);
            XmlHelper.AddElement(doc, viewProp, "DisplayValue", viewName);
            props.AppendChild(viewProp);

            action.AppendChild(props);

            // Parameters (set visibility)
            XmlElement parameters = doc.CreateElement("Parameters");
            XmlElement parameter = doc.CreateElement("Parameter");
            parameter.SetAttribute("SourceType", "Value");
            parameter.SetAttribute("TargetID", "isvisible");
            parameter.SetAttribute("TargetDisplayName", targetControlName);
            parameter.SetAttribute("TargetType", "ControlProperty");

            XmlElement sourceValue = doc.CreateElement("SourceValue");
            sourceValue.SetAttribute("xml:space", "preserve");
            sourceValue.InnerText = makeVisible.ToString().ToLower();
            parameter.AppendChild(sourceValue);

            parameters.AppendChild(parameter);
            action.AppendChild(parameters);

            return action;
        }

        private List<string> GetAllControlsInSection(JArray dynamicSections, JObject section,
                                                    JArray allControls, Dictionary<string, string> controlIdMap,
                                                    Dictionary<string, string> jsonToK2ControlIdMap)
        {
            List<string> sectionControlIds = new List<string>();

            // PRIMARY PATH: Use FieldNames for deterministic resolution.
            // The analysis phase resolves CtrlIds to field names. Since K2 has no sections,
            // we look up each field's K2 control directly by name from the controlIdMap.
            JArray fieldNames = section["FieldNames"] as JArray;
            if (fieldNames != null && fieldNames.Count > 0)
            {
                foreach (var fn in fieldNames)
                {
                    string fieldName = fn.Value<string>();
                    if (string.IsNullOrEmpty(fieldName)) continue;

                    // Look up the field control in controlIdMap (try exact, then uppercase)
                    string resolvedId = null;
                    if (controlIdMap.ContainsKey(fieldName))
                        resolvedId = controlIdMap[fieldName];
                    else if (controlIdMap.ContainsKey(fieldName.ToUpper()))
                        resolvedId = controlIdMap[fieldName.ToUpper()];

                    if (!string.IsNullOrEmpty(resolvedId) && !sectionControlIds.Contains(resolvedId))
                    {
                        sectionControlIds.Add(resolvedId);
                        Console.WriteLine($"        Resolved field '{fieldName}' -> {resolvedId}");
                    }

                    // Also find the label for this field
                    if (allControls != null)
                    {
                        foreach (JObject control in allControls)
                        {
                            string controlType = control["Type"]?.Value<string>();
                            string name = control["Name"]?.Value<string>();

                            if (controlType?.Equals("Label", StringComparison.OrdinalIgnoreCase) == true &&
                                name?.Equals(fieldName, StringComparison.OrdinalIgnoreCase) == true)
                            {
                                string gridPos = control["GridPosition"]?.Value<string>();
                                if (!string.IsNullOrEmpty(gridPos) && controlIdMap.ContainsKey(gridPos))
                                {
                                    string labelId = controlIdMap[gridPos];
                                    if (!sectionControlIds.Contains(labelId))
                                    {
                                        sectionControlIds.Add(labelId);
                                        Console.WriteLine($"        Found label for {fieldName} at {gridPos} -> {labelId}");
                                    }
                                }
                            }
                        }
                    }
                }

                if (sectionControlIds.Count > 0)
                    return sectionControlIds;
            }

            // FALLBACK: Use Controls (CtrlIds) for backward compatibility
            JArray controlsToToggle = section["Controls"] as JArray;
            if (controlsToToggle == null || controlsToToggle.Count == 0)
                return sectionControlIds;

            foreach (JValue controlRef in controlsToToggle)
            {
                string jsonControlId = controlRef.Value<string>();
                string resolvedControlId = null;

                // Try direct CtrlId -> K2 control ID lookup
                if (jsonToK2ControlIdMap.ContainsKey(jsonControlId))
                {
                    resolvedControlId = jsonToK2ControlIdMap[jsonControlId];
                    if (!sectionControlIds.Contains(resolvedControlId))
                        sectionControlIds.Add(resolvedControlId);
                    Console.WriteLine($"        Found control {jsonControlId} -> {resolvedControlId}");
                }

                // Try JSON_ prefix lookup
                string jsonKey = $"JSON_{jsonControlId}";
                if (string.IsNullOrEmpty(resolvedControlId) && controlIdMap.ContainsKey(jsonKey))
                {
                    resolvedControlId = controlIdMap[jsonKey];
                    if (!sectionControlIds.Contains(resolvedControlId))
                        sectionControlIds.Add(resolvedControlId);
                }

                // Try to find control by CtrlId in allControls and use its Name for lookup
                if (string.IsNullOrEmpty(resolvedControlId) && allControls != null)
                {
                    foreach (JObject control in allControls)
                    {
                        string ctrlId = control["CtrlId"]?.Value<string>();
                        if (ctrlId == jsonControlId)
                        {
                            string controlName = control["Name"]?.Value<string>();
                            if (!string.IsNullOrEmpty(controlName))
                            {
                                if (controlIdMap.ContainsKey(controlName))
                                    resolvedControlId = controlIdMap[controlName];
                                else if (controlIdMap.ContainsKey(controlName.ToUpper()))
                                    resolvedControlId = controlIdMap[controlName.ToUpper()];

                                if (!string.IsNullOrEmpty(resolvedControlId))
                                    Console.WriteLine($"        Resolved control {jsonControlId} by Name '{controlName}' -> {resolvedControlId}");
                                else
                                    Console.WriteLine($"        WARNING: Control {jsonControlId} (Name: {controlName}) not in controlIdMap");
                            }
                            break;
                        }
                    }

                    if (string.IsNullOrEmpty(resolvedControlId))
                    {
                        Console.WriteLine($"        WARNING: Control {jsonControlId} not found in any control map - may be a section container");
                    }
                }

                if (!string.IsNullOrEmpty(resolvedControlId) && !sectionControlIds.Contains(resolvedControlId))
                {
                    sectionControlIds.Add(resolvedControlId);
                }
            }

            return sectionControlIds;
        }

        private string FindControlIdByFieldName(string fieldName, Dictionary<string, string> controlIdMap)
        {
            if (string.IsNullOrEmpty(fieldName))
                return null;

            if (controlIdMap.ContainsKey(fieldName))
                return controlIdMap[fieldName];

            string sanitized = NameSanitizer.SanitizePropertyName(fieldName);
            if (controlIdMap.ContainsKey(sanitized))
                return controlIdMap[sanitized];

            string upper = fieldName.ToUpper();
            if (controlIdMap.ContainsKey(upper))
                return controlIdMap[upper];

            foreach (var kvp in controlIdMap)
            {
                if (kvp.Key.Equals(fieldName, StringComparison.OrdinalIgnoreCase) ||
                    kvp.Key.Equals(sanitized, StringComparison.OrdinalIgnoreCase))
                {
                    return kvp.Value;
                }
            }

            return null;
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

    }
}