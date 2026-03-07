using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using FormGenerator.Analyzers.Infopath;
using FormGenerator.Core.Interfaces;
using FormGenerator.Core.Models;
using FormGenerator.Writers.K2.RuleBuilders;

namespace FormGenerator.Services
{
    /// <summary>
    /// Orchestrates the conversion of InfoPath rules to K2 rule XML using modular builders.
    /// Replaces the broken pipeline of RuleMappingService -> InfoPathToK2RuleMapper -> K2RuleXmlBuilder.
    /// </summary>
    public class K2InfoPathRuleOrchestrator
    {
        private readonly List<IK2RuleTypeBuilder> _builders;
        private readonly RuleMappingService _ruleMappingService;

        public K2InfoPathRuleOrchestrator()
        {
            _ruleMappingService = new RuleMappingService();

            // Register all available rule builders
            _builders = new List<IK2RuleTypeBuilder>
            {
                new K2VisibilityRuleBuilder(),
                new K2ValidationRuleBuilder(),
                new K2DataTransferRuleBuilder()
            };
        }

        /// <summary>
        /// Process InfoPath rules from a form definition and add them as Event elements to the view.
        /// </summary>
        public void AddInfoPathRules(XmlDocument doc, XmlElement events,
            InfoPathFormDefinition formDef, K2RuleContext context)
        {
            if (formDef == null || context == null) return;

            // Step 1: Run RuleMappingService to analyze rules and determine status
            var formDefinitions = new Dictionary<string, InfoPathFormDefinition>
            {
                { formDef.FormName ?? "Form", formDef }
            };
            var mappings = _ruleMappingService.AnalyzeRules(formDefinitions);

            if (mappings == null || mappings.Count == 0)
            {
                Console.WriteLine("    No InfoPath rules found to map");
                return;
            }

            int added = 0;
            int skipped = 0;

            foreach (var mapping in mappings)
            {
                // Only process supported or partially supported rules
                if (mapping.Status != RuleMappingStatus.Supported &&
                    mapping.Status != RuleMappingStatus.PartiallySupported)
                {
                    skipped++;
                    Console.WriteLine($"    Skipping rule '{mapping.InfoPathRuleName}' (status: {mapping.StatusDisplay})");
                    continue;
                }

                // Find a builder that can handle this rule
                var builder = _builders.FirstOrDefault(b => b.CanHandle(mapping));
                if (builder == null)
                {
                    skipped++;
                    Console.WriteLine($"    No builder for rule '{mapping.InfoPathRuleName}' (type: {mapping.InfoPathRuleType}, action: {mapping.K2ActionType})");
                    continue;
                }

                try
                {
                    // Build the Event XML element directly
                    var eventElement = builder.BuildEventXml(doc, mapping, context);
                    if (eventElement != null)
                    {
                        events.AppendChild(eventElement);
                        added++;
                        Console.WriteLine($"    Added {builder.RuleType} rule: {mapping.InfoPathRuleName}");
                    }
                    else
                    {
                        skipped++;
                    }
                }
                catch (Exception ex)
                {
                    skipped++;
                    Console.WriteLine($"    Error building rule '{mapping.InfoPathRuleName}': {ex.Message}");
                }
            }

            Console.WriteLine($"    InfoPath rule mapping complete: {added} added, {skipped} skipped (of {mappings.Count} total)");
        }
    }
}
