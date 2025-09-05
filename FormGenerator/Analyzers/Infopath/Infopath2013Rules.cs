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
            return "General";
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

