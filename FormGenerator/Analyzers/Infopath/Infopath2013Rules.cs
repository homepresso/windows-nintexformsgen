using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using FormGenerator.Analyzers.Infopath;

namespace FormGenerator.Analyzers.Infopath
{
    public class RulesExtractor
    {
        private XNamespace xsf = "http://schemas.microsoft.com/office/infopath/2003/solutionDefinition";
        private XNamespace xd = "http://schemas.microsoft.com/office/infopath/2003";

        public void ExtractRules(string tempDir, InfoPathFormDefinition formDef)
        {
            // Extract from manifest.xsf
            var manifestPath = Path.Combine(tempDir, "manifest.xsf");
            if (File.Exists(manifestPath))
            {
                var manifestDoc = XDocument.Load(manifestPath);
                ExtractManifestRules(manifestDoc, formDef);
            }

            // Extract from view files
            var viewFiles = Directory.GetFiles(tempDir, "view*.xsl");
            foreach (var viewFile in viewFiles)
            {
                var viewDoc = XDocument.Load(viewFile);
                ExtractViewRules(viewDoc, formDef);
            }

            // Extract from schema
            var schemaFiles = Directory.GetFiles(tempDir, "*.xsd");
            foreach (var schemaFile in schemaFiles)
            {
                var schemaDoc = XDocument.Load(schemaFile);
                ExtractSchemaValidation(schemaDoc, formDef);
            }
        }

        private void ExtractManifestRules(XDocument manifest, InfoPathFormDefinition formDef)
        {
            // Look for rules in manifest
            var ruleElements = manifest.Descendants(xsf + "rule");

            foreach (var ruleElem in ruleElements)
            {
                var rule = new FormRule
                {
                    Name = ruleElem.Attribute("caption")?.Value ?? "Rule_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                    IsEnabled = ruleElem.Attribute("isEnabled")?.Value != "no",
                    RuleType = DetermineRuleType(ruleElem)
                };

                // Extract condition
                var conditionElem = ruleElem.Element(xsf + "condition");
                if (conditionElem != null)
                {
                    rule.Condition = conditionElem.Attribute("expression")?.Value;
                    rule.ConditionExpression = ExtractConditionExpression(conditionElem);
                }

                // Extract actions
                ExtractRuleActions(ruleElem, rule);

                formDef.Rules.Add(rule);
            }

            // ENHANCED: Extract events from States and Events sections
            ExtractEventRules(manifest, formDef);
        }

        private void ExtractEventRules(XDocument manifest, InfoPathFormDefinition formDef)
        {
            // Look for Event elements in States sections
            var eventElements = manifest.Descendants().Where(e => e.Name.LocalName == "Event");

            foreach (var eventElem in eventElements)
            {
                var eventId = eventElem.Attribute("ID")?.Value;
                var eventType = eventElem.Attribute("Type")?.Value;
                var sourceId = eventElem.Attribute("SourceID")?.Value;
                var sourceType = eventElem.Attribute("SourceType")?.Value;
                var sourceName = eventElem.Attribute("SourceName")?.Value;
                var sourceDisplayName = eventElem.Attribute("SourceDisplayName")?.Value;

                if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(eventType)) continue;

                var eventRule = new FormRule
                {
                    Name = $"Event_{sourceName ?? sourceDisplayName ?? eventId}",
                    RuleType = "Event",
                    IsEnabled = true
                };

                // Store event metadata (declare early to use in nested scope)
                var eventAction = new FormRuleAction
                {
                    Type = "EventDefinition"
                };

                // Extract event name
                var nameElem = eventElem.Element("Name");
                if (nameElem != null)
                {
                    eventRule.Name = nameElem.Value;
                }

                // Extract event properties
                var propertiesElem = eventElem.Element("Properties");
                if (propertiesElem != null)
                {
                    foreach (var property in propertiesElem.Elements("Property"))
                    {
                        var nameElem2 = property.Element("Name");
                        var valueElem = property.Element("Value");

                        if (nameElem2 != null && valueElem != null)
                        {
                            var action = new FormRuleAction
                            {
                                Type = "EventProperty",
                                Target = nameElem2.Value,
                                Expression = valueElem.Value
                            };
                            eventRule.Actions.Add(action);

                            // Check if this is a RuleFriendlyName that indicates button events
                            if (nameElem2.Value == "RuleFriendlyName")
                            {
                                var friendlyName = valueElem.Value;
                                if (IsButtonEvent(friendlyName))
                                {
                                    eventRule.RuleType = "Button";
                                    eventRule.Name = friendlyName;

                                    // Check if this is a repeating section button event
                                    if (IsRepeatingSectionButtonEvent(friendlyName, sourceName))
                                    {
                                        eventRule.RuleType = "RepeatingSectionButton";

                                        // Extract repeating section context
                                        var repeatingSectionInfo = ExtractRepeatingSectionFromEvent(friendlyName, sourceName);
                                        if (!string.IsNullOrEmpty(repeatingSectionInfo))
                                        {
                                            eventAction.Parameters["RepeatingSectionContext"] = repeatingSectionInfo;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                if (!string.IsNullOrEmpty(sourceId)) eventAction.Parameters["SourceID"] = sourceId;
                if (!string.IsNullOrEmpty(sourceType)) eventAction.Parameters["SourceType"] = sourceType;
                if (!string.IsNullOrEmpty(sourceName)) eventAction.Parameters["SourceName"] = sourceName;
                if (!string.IsNullOrEmpty(sourceDisplayName)) eventAction.Parameters["SourceDisplayName"] = sourceDisplayName;
                if (!string.IsNullOrEmpty(eventType)) eventAction.Parameters["EventType"] = eventType;

                eventRule.Actions.Add(eventAction);
                formDef.Rules.Add(eventRule);
            }

            // ENHANCED: Extract validation messages that contain button event errors
            ExtractValidationErrors(manifest, formDef);
        }

        private void ExtractValidationErrors(XDocument manifest, InfoPathFormDefinition formDef)
        {
            // Look for ValidationMessages attributes that contain event errors
            var elementsWithValidation = manifest.Descendants()
                .Where(e => e.Attribute("ValidationMessages") != null);

            foreach (var elem in elementsWithValidation)
            {
                var validationMessages = elem.Attribute("ValidationMessages")?.Value;
                if (string.IsNullOrEmpty(validationMessages)) continue;

                // Parse validation messages for button events
                var messages = validationMessages.Split(';');
                foreach (var message in messages)
                {
                    if (message.Contains("Add ToolBar Button is Clicked") ||
                        message.Contains("Add Button is Clicked") ||
                        message.Contains("Cancel is Clicked"))
                    {
                        // Extract the event name from the validation message
                        var parts = message.Split(',');
                        if (parts.Length >= 6)
                        {
                            var eventDescription = parts[5].Trim('"');

                            var validationRule = new FormRule
                            {
                                Name = $"ValidationError_{eventDescription}",
                                RuleType = "ValidationError",
                                IsEnabled = false,
                                ErrorMessage = message
                            };

                            formDef.Rules.Add(validationRule);
                        }
                    }
                }
            }
        }

        private void ExtractViewRules(XDocument viewDoc, InfoPathFormDefinition formDef)
        {
            var ns = viewDoc.Root.Name.Namespace;

            // Extract conditional visibility from xsl:if elements
            var ifElements = viewDoc.Descendants(ns + "if");

            foreach (var ifElem in ifElements)
            {
                var testCondition = ifElem.Attribute("test")?.Value;
                if (!string.IsNullOrEmpty(testCondition))
                {
                    var conditionalRule = new ConditionalRule
                    {
                        Name = "Conditional_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                        Type = "Visibility",
                        Condition = testCondition,
                        SourceField = ExtractFieldFromCondition(testCondition),
                        Action = "Show/Hide"
                    };

                    // Find affected controls
                    var affectedControls = ifElem.Descendants()
                        .Where(e => GetAttributeValue(e, "CtrlId") != null)
                        .Select(e => GetAttributeValue(e, "CtrlId"))
                        .Distinct()
                        .ToList();

                    conditionalRule.AffectedControls = affectedControls;
                    formDef.ConditionalRules.Add(conditionalRule);
                }
            }

            // Extract calculations from xsl:value-of
            var valueOfElements = viewDoc.Descendants(ns + "value-of");

            foreach (var valueOfElem in valueOfElements)
            {
                var select = valueOfElem.Attribute("select")?.Value;
                if (!string.IsNullOrEmpty(select) && IsCalculation(select))
                {
                    var calcRule = new ConditionalRule
                    {
                        Name = "Calculation_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                        Type = "Calculation",
                        Condition = select,
                        Action = "Calculate"
                    };

                    formDef.ConditionalRules.Add(calcRule);
                }
            }
        }

        private void ExtractSchemaValidation(XDocument schema, InfoPathFormDefinition formDef)
        {
            var xs = XNamespace.Get("http://www.w3.org/2001/XMLSchema");

            // Find all elements with restrictions
            var elements = schema.Descendants(xs + "element");

            foreach (var elem in elements)
            {
                var name = elem.Attribute("name")?.Value;
                if (string.IsNullOrEmpty(name)) continue;

                var validation = new ValidationRule
                {
                    ControlName = name,
                    Binding = "my:" + name
                };

                // Check for required
                var minOccurs = elem.Attribute("minOccurs")?.Value;
                if (minOccurs == "1")
                {
                    validation.IsRequired = true;
                    validation.ValidationType = "Required";
                }

                // Check for type restrictions
                var simpleType = elem.Descendants(xs + "simpleType").FirstOrDefault();
                if (simpleType != null)
                {
                    var restriction = simpleType.Element(xs + "restriction");
                    if (restriction != null)
                    {
                        validation.DataType = restriction.Attribute("base")?.Value;

                        // Pattern
                        var pattern = restriction.Element(xs + "pattern");
                        if (pattern != null)
                        {
                            validation.Pattern = pattern.Attribute("value")?.Value;
                            validation.ValidationType = "Pattern";
                        }

                        // Min/Max length
                        var minLength = restriction.Element(xs + "minLength");
                        if (minLength != null)
                        {
                            validation.MinLength = int.Parse(minLength.Attribute("value")?.Value ?? "0");
                        }

                        var maxLength = restriction.Element(xs + "maxLength");
                        if (maxLength != null)
                        {
                            validation.MaxLength = int.Parse(maxLength.Attribute("value")?.Value ?? "0");
                        }

                        // Min/Max value
                        var minInclusive = restriction.Element(xs + "minInclusive");
                        if (minInclusive != null)
                        {
                            validation.MinValue = minInclusive.Attribute("value")?.Value;
                        }

                        var maxInclusive = restriction.Element(xs + "maxInclusive");
                        if (maxInclusive != null)
                        {
                            validation.MaxValue = maxInclusive.Attribute("value")?.Value;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(validation.ValidationType))
                {
                    formDef.Validations.Add(validation);
                }
            }
        }

        private string ExtractFieldFromCondition(string condition)
        {
            // Extract field name from conditions like "my:field = 'value'"
            var match = System.Text.RegularExpressions.Regex.Match(condition, @"my:(\w+)");
            return match.Success ? match.Groups[1].Value : "";
        }

        private bool IsCalculation(string expression)
        {
            // Check if expression contains calculation operators
            return expression.Contains("+") || expression.Contains("-") ||
                   expression.Contains("*") || expression.Contains("/") ||
                   expression.Contains("sum(") || expression.Contains("count(") ||
                   expression.Contains("avg(") || expression.Contains("min(") ||
                   expression.Contains("max(");
        }

        private string DetermineRuleType(XElement ruleElem)
        {
            // Determine rule type based on actions
            if (ruleElem.Descendants().Any(e => e.Name.LocalName == "submit"))
                return "Submit";
            if (ruleElem.Descendants().Any(e => e.Name.LocalName == "setValue"))
                return "Action";
            if (ruleElem.Descendants().Any(e => e.Name.LocalName == "switchView"))
                return "Navigation";

            // Check for button events based on rule names or actions
            var ruleName = ruleElem.Attribute("caption")?.Value ?? "";
            if (IsButtonEvent(ruleName))
                return "Button";

            return "General";
        }

        private bool IsButtonEvent(string ruleName)
        {
            if (string.IsNullOrEmpty(ruleName)) return false;

            var buttonEventPatterns = new[]
            {
                "Add ToolBar Button is Clicked",
                "Add Button is Clicked",
                "Cancel is Clicked",
                "Delete is Clicked",
                "Remove is Clicked",
                "AddButton Button is Clicked",
                "CancelButton is Clicked",
                "DeleteButton is Clicked",
                "RemoveButton is Clicked",
                "when Add ToolBar Button",
                "when Add Button",
                "when Cancel",
                "when Delete",
                "when Remove"
            };

            return buttonEventPatterns.Any(pattern =>
                ruleName.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsRepeatingSectionButtonEvent(string friendlyName, string sourceName)
        {
            if (string.IsNullOrEmpty(friendlyName) || string.IsNullOrEmpty(sourceName)) return false;

            // Check if the source name contains patterns indicating repeating sections
            var repeatingSectionPatterns = new[]
            {
                "_Table_",
                "_List",
                "_Item",
                "CTRL",
                "Entertainment_Details", // Specific to your example
                "Table_CTRL"
            };

            return repeatingSectionPatterns.Any(pattern =>
                sourceName.Contains(pattern, StringComparison.OrdinalIgnoreCase)) &&
                IsButtonEvent(friendlyName);
        }

        private string ExtractRepeatingSectionFromEvent(string friendlyName, string sourceName)
        {
            if (string.IsNullOrEmpty(sourceName)) return "";

            // Extract repeating section information from source name
            // Pattern: Expense_Report_view1_Table_CTRL243_List
            // Pattern: Expense_Report_view1_Item_Entertainment_Details_Item

            var parts = sourceName.Split('_');
            if (parts.Length < 3) return "";

            // Look for patterns that indicate nested repeating sections
            if (sourceName.Contains("Entertainment_Details"))
            {
                return "Entertainment_Details"; // Nested section
            }
            else if (sourceName.Contains("Table_CTRL"))
            {
                // Extract the table control identifier
                var ctrlIndex = Array.FindIndex(parts, p => p.StartsWith("CTRL"));
                if (ctrlIndex >= 0 && ctrlIndex < parts.Length - 1)
                {
                    return $"Table_{parts[ctrlIndex]}"; // Main repeating section
                }
            }

            return sourceName; // Return the full source name as fallback
        }

        private void ExtractRuleActions(XElement ruleElem, FormRule rule)
        {
            foreach (var actionElem in ruleElem.Elements())
            {
                if (actionElem.Name.LocalName == "condition") continue;

                var action = new FormRuleAction
                {
                    Type = actionElem.Name.LocalName
                };

                // Extract action parameters
                foreach (var attr in actionElem.Attributes())
                {
                    action.Parameters[attr.Name.LocalName] = attr.Value;
                }

                if (actionElem.Name.LocalName == "assignmentAction")
                {
                    action.Target = actionElem.Attribute("target")?.Value;
                    action.Expression = actionElem.Attribute("expression")?.Value;
                }

                rule.Actions.Add(action);
            }
        }

        private string ExtractConditionExpression(XElement conditionElem)
        {
            // Build readable condition expression
            var expression = conditionElem.Attribute("expression")?.Value ?? "";

            // Try to make it more readable
            expression = expression.Replace("../", "parent/");
            expression = expression.Replace("my:", "");

            return expression;
        }

        private string GetAttributeValue(XElement elem, string attrName)
        {
            return elem?.Attributes()
                .FirstOrDefault(a => a.Name.LocalName.Equals(attrName, StringComparison.OrdinalIgnoreCase))
                ?.Value;
        }
    }

  
    }

