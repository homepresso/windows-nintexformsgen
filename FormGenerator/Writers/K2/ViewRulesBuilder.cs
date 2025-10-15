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

        public ViewRulesBuilder()
        {
            _usedEventIds = new HashSet<string>();
            _usedHandlerIds = new HashSet<string>();
        }

        public XmlElement CreateEventsWithRules(XmlDocument doc, string viewGuid, string viewName,
                                               Dictionary<string, string> controlIdMap,
                                               Dictionary<string, string> controlToFieldMap,
                                               Dictionary<string, FieldInfo> fieldMap,
                                               Dictionary<string, LookupInfo> lookupSmartObjects,
                                               JArray dynamicSections, JObject conditionalVisibility,
                                               JArray controls, Dictionary<string, string> jsonToK2ControlIdMap)
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
                }
            }

            // Now add visibility rules
            if (dynamicSections != null && dynamicSections.Count > 0)
            {
                Console.WriteLine($"    Processing {dynamicSections.Count} dynamic sections for visibility rules");
                FixDynamicSectionControlIds(dynamicSections);
                AddVisibilityRules(doc, events, dynamicSections, controlIdMap, viewGuid, viewName,
                    controls, jsonToK2ControlIdMap);
            }

            // Add conditional visibility rules
            if (conditionalVisibility != null)
            {
                Console.WriteLine($"    Processing conditional visibility rules");
                AddConditionalVisibilityRules(doc, events, conditionalVisibility, controlIdMap,
                    viewGuid, viewName);
            }

            return events;
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
            if (dynamicSections != null && dynamicSections.Count > 0)
            {
                AddInitialVisibilityActions(doc, actions, dynamicSections, controls, controlIdMap,
                    viewGuid, viewName, jsonToK2ControlIdMap);
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
                string filterXml = $@"<Filter isSimple=""True""><Equals>" +
                    $@"<Item SourceType=""ObjectProperty"" SourceID=""LookupType"" DataType=""Text"" " +
                    $@"SourceName=""LookupType"" SourceDisplayName=""Lookup Type"">LookupType</Item>" +
                    $@"<Item SourceType=""Value""><SourceValue>{lookup.LookupParameter}</SourceValue></Item>" +
                    $@"</Equals></Filter>";

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

            NormalizeDynamicSectionControlIds(dynamicSections);

            foreach (JObject section in dynamicSections)
            {
                string ctrlId = section["CtrlId"]?.Value<string>();
                string conditionField = section["ConditionField"]?.Value<string>();
                JArray controlsToToggle = section["Controls"] as JArray;

                if (controlsToToggle == null || controlsToToggle.Count == 0)
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
                                       Dictionary<string, string> jsonToK2ControlIdMap)
        {
            Console.WriteLine($"    Adding visibility rules from {dynamicSections.Count} dynamic sections");

            HashSet<string> processedTriggerControls = new HashSet<string>();

            foreach (JObject section in dynamicSections)
            {
                string ctrlId = section["CtrlId"]?.Value<string>();
                string conditionField = section["ConditionField"]?.Value<string>();
                JArray controlsToToggle = section["Controls"] as JArray;

                if (controlsToToggle == null || controlsToToggle.Count == 0)
                {
                    Console.WriteLine($"      Skipping section with no controls to toggle");
                    continue;
                }

                List<string> allSectionControls = GetAllControlsInSection(dynamicSections, section,
                    controls, controlIdMap, jsonToK2ControlIdMap);

                Console.WriteLine($"      Section has {controlsToToggle.Count} explicit controls, " +
                                 $"expanded to {allSectionControls.Count} total controls (including labels)");

                if (allSectionControls.Count == 0)
                {
                    Console.WriteLine($"      WARNING: No controls found for section, skipping");
                    continue;
                }

                string triggerControlId = null;
                string triggerFieldName = null;

                // Determine the trigger control
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

                    if (string.IsNullOrEmpty(triggerControlId))
                    {
                        triggerControlId = FindControlIdByFieldName(triggerFieldName, controlIdMap);
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

                XmlElement visibilityEvent = CreateSectionVisibilityEvent(doc, triggerControlId,
                    triggerFieldName, expandedControls, controlIdMap, viewGuid, viewName);

                if (visibilityEvent != null)
                {
                    events.AppendChild(visibilityEvent);
                    Console.WriteLine($"      Successfully added visibility rule for {triggerFieldName} " +
                                   $"affecting {allSectionControls.Count} controls");
                }
            }
        }

        private XmlElement CreateSectionVisibilityEvent(XmlDocument doc, string checkboxControlId,
                                                       string checkboxFieldName, JArray allControlIds,
                                                       Dictionary<string, string> controlIdMap,
                                                       string viewGuid, string viewName)
        {
            XmlElement eventElement = doc.CreateElement("Event");
            string eventGuid = Guid.NewGuid().ToString();
            eventElement.SetAttribute("ID", eventGuid);
            eventElement.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            eventElement.SetAttribute("Type", "User");
            eventElement.SetAttribute("SourceID", checkboxControlId);
            eventElement.SetAttribute("SourceType", "Control");
            eventElement.SetAttribute("SourceName", checkboxFieldName);
            eventElement.SetAttribute("SourceDisplayName", checkboxFieldName);
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
            XmlHelper.AddElement(doc, ruleFriendlyName, "Value", $"When {checkboxFieldName} is Changed");
            props.AppendChild(ruleFriendlyName);

            XmlElement locationProp = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, locationProp, "Name", "Location");
            XmlHelper.AddElement(doc, locationProp, "Value", viewName);
            props.AppendChild(locationProp);

            eventElement.AppendChild(props);

            XmlElement handlers = doc.CreateElement("Handlers");

            // Handler for checkbox = True (show all controls)
            XmlElement trueHandler = CreateSectionHandler(doc, checkboxControlId, checkboxFieldName,
                true, allControlIds, viewGuid, viewName);
            if (trueHandler != null)
                handlers.AppendChild(trueHandler);

            // Handler for checkbox = False (hide all controls)
            XmlElement falseHandler = CreateSectionHandler(doc, checkboxControlId, checkboxFieldName,
                false, allControlIds, viewGuid, viewName);
            if (falseHandler != null)
                handlers.AppendChild(falseHandler);

            eventElement.AppendChild(handlers);
            return eventElement;
        }

        private XmlElement CreateSectionHandler(XmlDocument doc, string checkboxControlId,
                                               string checkboxFieldName, bool checkForTrue,
                                               JArray controlIds, string viewGuid, string viewName)
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
            XmlHelper.AddElement(doc, locationProp, "Value", "view");
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

            // Create expression
            XmlElement expressions = doc.CreateElement("Expressions");
            XmlElement equals = doc.CreateElement("Equals");

            XmlElement sourceItem = doc.CreateElement("Item");
            sourceItem.SetAttribute("SourceType", "Control");
            sourceItem.SetAttribute("SourceID", checkboxControlId);
            sourceItem.SetAttribute("SourceName", checkboxFieldName);
            sourceItem.SetAttribute("SourceDisplayName", checkboxFieldName);
            sourceItem.SetAttribute("DataType", "Text");
            equals.AppendChild(sourceItem);

            XmlElement valueItem = doc.CreateElement("Item");
            valueItem.SetAttribute("SourceType", "Value");
            valueItem.SetAttribute("DataType", "Text");
            valueItem.InnerText = checkForTrue ? "True" : "False";
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
                    XmlElement action = CreateTransferAction(doc, controlId,
                        $"Control_{controlId}", makeVisible, viewGuid, viewName);
                    actions.AppendChild(action);
                }
            }

            handler.AppendChild(actions);
            return handler;
        }

        private void AddConditionalVisibilityRules(XmlDocument doc, XmlElement events,
                                                  JObject conditionalVisibility,
                                                  Dictionary<string, string> controlIdMap,
                                                  string viewGuid, string viewName)
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

                XmlElement changeEvent = doc.CreateElement("Event");
                changeEvent.SetAttribute("ID", Guid.NewGuid().ToString());
                changeEvent.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
                changeEvent.SetAttribute("Type", "User");
                changeEvent.SetAttribute("SourceID", controlId);
                changeEvent.SetAttribute("SourceType", "Control");
                changeEvent.SetAttribute("SourceName", fieldName);
                changeEvent.SetAttribute("IsExtended", "True");

                XmlHelper.AddElement(doc, changeEvent, "Name", "OnChange");

                XmlElement props = doc.CreateElement("Properties");
                XmlElement viewIdProp = doc.CreateElement("Property");
                XmlHelper.AddElement(doc, viewIdProp, "Name", "ViewID");
                XmlHelper.AddElement(doc, viewIdProp, "Value", viewGuid);
                XmlHelper.AddElement(doc, viewIdProp, "DisplayValue", viewName);
                props.AppendChild(viewIdProp);

                XmlElement ruleName = doc.CreateElement("Property");
                XmlHelper.AddElement(doc, ruleName, "Name", "RuleFriendlyName");
                XmlHelper.AddElement(doc, ruleName, "Value", $"When {fieldName} is Changed");
                props.AppendChild(ruleName);

                changeEvent.AppendChild(props);

                XmlElement handlers = doc.CreateElement("Handlers");

                // Create handler for showing controls when field has value
                XmlElement showHandler = CreateSimpleVisibilityHandler(doc, controlId, fieldName,
                    affectedControls, controlIdMap, true, viewGuid, viewName);
                handlers.AppendChild(showHandler);

                // Create handler for hiding controls when field is empty
                XmlElement hideHandler = CreateSimpleVisibilityHandler(doc, controlId, fieldName,
                    affectedControls, controlIdMap, false, viewGuid, viewName);
                handlers.AppendChild(hideHandler);

                changeEvent.AppendChild(handlers);
                events.AppendChild(changeEvent);

                Console.WriteLine($"        Added conditional visibility rule for field: {fieldName}");
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
            handler.AppendChild(handlerProps);

            // Create condition
            XmlElement conditions = doc.CreateElement("Conditions");
            XmlElement condition = doc.CreateElement("Condition");
            condition.SetAttribute("ID", Guid.NewGuid().ToString());
            condition.SetAttribute("DefinitionID", Guid.NewGuid().ToString());

            XmlElement condProps = doc.CreateElement("Properties");
            XmlElement condName = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, condName, "Name", "Name");
            XmlHelper.AddElement(doc, condName, "Value", showControls ? "SimpleNotEmptyCondition" : "SimpleEmptyCondition");
            condProps.AppendChild(condName);
            condition.AppendChild(condProps);

            XmlElement expressions = doc.CreateElement("Expressions");
            XmlElement sourceItem = doc.CreateElement("Item");
            sourceItem.SetAttribute("SourceType", "Control");
            sourceItem.SetAttribute("SourceID", controlId);
            sourceItem.SetAttribute("SourceName", fieldName);
            sourceItem.SetAttribute("DataType", "Text");
            expressions.AppendChild(sourceItem);

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
                    XmlElement action = CreateTransferAction(doc, targetControlId,
                        controlName.Value<string>(), showControls, viewGuid, viewName);
                    actions.AppendChild(action);
                }
            }

            handler.AppendChild(actions);
            return handler;
        }

        private XmlElement CreateTransferAction(XmlDocument doc, string targetControlId,
                                               string targetControlName, bool makeVisible,
                                               string viewGuid, string viewName)
        {
            XmlElement action = doc.CreateElement("Action");
            action.SetAttribute("ID", Guid.NewGuid().ToString());
            action.SetAttribute("DefinitionID", Guid.NewGuid().ToString());
            action.SetAttribute("Type", "Transfer");
            action.SetAttribute("ExecutionType", "Parallel");

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

            JArray controlsToToggle = section["Controls"] as JArray;
            if (controlsToToggle == null || controlsToToggle.Count == 0)
                return sectionControlIds;

            foreach (JValue controlRef in controlsToToggle)
            {
                string jsonControlId = controlRef.Value<string>();
                string resolvedControlId = null;

                if (jsonToK2ControlIdMap.ContainsKey(jsonControlId))
                {
                    resolvedControlId = jsonToK2ControlIdMap[jsonControlId];
                    sectionControlIds.Add(resolvedControlId);
                    Console.WriteLine($"        Found control {jsonControlId} -> {resolvedControlId}");
                }

                string jsonKey = $"JSON_{jsonControlId}";
                if (controlIdMap.ContainsKey(jsonKey))
                {
                    resolvedControlId = controlIdMap[jsonKey];
                    if (!sectionControlIds.Contains(resolvedControlId))
                        sectionControlIds.Add(resolvedControlId);
                }

                // Find the field name for this control to locate its label
                string fieldName = null;
                foreach (JObject control in allControls)
                {
                    string ctrlId = control["CtrlId"]?.Value<string>();
                    if (ctrlId == jsonControlId)
                    {
                        fieldName = control["Name"]?.Value<string>();
                        break;
                    }
                }

                // Find labels with matching field names
                if (!string.IsNullOrEmpty(fieldName))
                {
                    foreach (JObject control in allControls)
                    {
                        string controlType = control["Type"]?.Value<string>();
                        string name = control["Name"]?.Value<string>();

                        if (controlType?.ToLower() == "label" &&
                            name?.ToUpper() == fieldName.ToUpper())
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

            // Special handling for return date/time controls (they're in row 10)
            if (controlsToToggle.Any(c => c.Value<string>() == "CTRL58" || c.Value<string>() == "CTRL59"))
            {
                foreach (JObject control in allControls)
                {
                    string gridPos = control["GridPosition"]?.Value<string>();
                    if (!string.IsNullOrEmpty(gridPos))
                    {
                        int rowNum = ExtractRowNumber(gridPos);
                        if (rowNum == 10 && controlIdMap.ContainsKey(gridPos))
                        {
                            string controlId = controlIdMap[gridPos];
                            if (!sectionControlIds.Contains(controlId))
                            {
                                sectionControlIds.Add(controlId);
                                Console.WriteLine($"        Including row 10 control at {gridPos} -> {controlId}");
                            }
                        }
                    }
                }
            }

            return sectionControlIds;
        }

        private void FixDynamicSectionControlIds(JArray dynamicSections)
        {
            foreach (JObject section in dynamicSections)
            {
                string ctrlId = section["CtrlId"]?.Value<string>();
                JArray controls = section["Controls"] as JArray;

                if (ctrlId == "CTRL32" && controls != null)
                {
                    bool hasCtrl33 = false;
                    bool hasCtrl34 = false;

                    for (int i = 0; i < controls.Count; i++)
                    {
                        string controlId = controls[i].Value<string>();
                        if (controlId == "CTRL33") hasCtrl33 = true;
                        if (controlId == "CTRL34") hasCtrl34 = true;
                    }

                    if (hasCtrl33 && hasCtrl34)
                    {
                        controls.Clear();
                        controls.Add("CTRL58"); // RETURNDATE
                        controls.Add("CTRL59"); // RETURNTIME
                        Console.WriteLine($"      Fixed control IDs for CTRL32 section: CTRL33/34 -> CTRL58/59");
                    }
                }
            }
        }

        private void NormalizeDynamicSectionControlIds(JArray dynamicSections)
        {
            if (dynamicSections == null) return;

            Dictionary<string, string> controlIdReplacements = new Dictionary<string, string>
            {
                { "CTRL33", "CTRL58" }, // RETURNDATE
                { "CTRL34", "CTRL59" }  // RETURNTIME
            };

            foreach (JObject section in dynamicSections)
            {
                if (section == null) continue;

                JArray controls = section["Controls"] as JArray;
                if (controls == null || controls.Count == 0) continue;

                JArray correctedControls = new JArray();
                bool hasChanges = false;

                foreach (var control in controls)
                {
                    if (control == null || control.Type != JTokenType.String)
                    {
                        correctedControls.Add(control);
                        continue;
                    }

                    string controlId = control.Value<string>();
                    if (string.IsNullOrEmpty(controlId))
                    {
                        correctedControls.Add(control);
                        continue;
                    }

                    if (controlIdReplacements.ContainsKey(controlId))
                    {
                        correctedControls.Add(controlIdReplacements[controlId]);
                        hasChanges = true;
                        Console.WriteLine($"      Normalizing control ID: {controlId} -> {controlIdReplacements[controlId]}");
                    }
                    else
                    {
                        correctedControls.Add(controlId);
                    }
                }

                if (hasChanges)
                {
                    section["Controls"] = correctedControls;
                }
            }
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