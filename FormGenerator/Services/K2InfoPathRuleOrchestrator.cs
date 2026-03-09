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

            // Register rule builders.
            // NOTE: K2VisibilityRuleBuilder is intentionally excluded here because
            // visibility rules are already handled by ViewRulesBuilder via the
            // ConditionalVisibility and DynamicSections JSON data. Including it here
            // would create duplicate events/handlers for the same source controls,
            // resulting in bloated XML (e.g., 16+ handlers instead of 2).
            _builders = new List<IK2RuleTypeBuilder>
            {
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

            // Track events by SourceID to group multiple rules for the same source control
            // into a single Event element (K2 requires one Event per source control).
            var eventsBySourceId = new Dictionary<string, XmlElement>(StringComparer.OrdinalIgnoreCase);

            foreach (var mapping in mappings)
            {
                // K2 Native rules (like calculations) are handled by the Expressions pipeline, not here
                if (mapping.Status == RuleMappingStatus.K2Native)
                {
                    skipped++;
                    Console.WriteLine($"    K2 Native rule '{mapping.InfoPathRuleName}' will be handled by K2 Expressions pipeline: {mapping.Notes}");
                    continue;
                }

                // Only process supported or partially supported rules
                if (mapping.Status != RuleMappingStatus.Supported &&
                    mapping.Status != RuleMappingStatus.PartiallySupported)
                {
                    skipped++;
                    Console.WriteLine($"    Skipping rule '{mapping.InfoPathRuleName}' (status: {mapping.StatusDisplay})");
                    continue;
                }

                // Pre-filter: Only apply rules to the view where the trigger (source) control exists.
                // This prevents cross-view rule application which would reference wrong control GUIDs.
                var sourceField = mapping.InfoPathAppliesTo;
                if (!string.IsNullOrEmpty(sourceField) && context.ControlResolver != null)
                {
                    var sourceCheck = context.ControlResolver.TryResolveQuiet(sourceField);
                    if (sourceCheck == null)
                    {
                        // Source control doesn't exist in this view - skip silently
                        // (the rule will be applied when the correct view is processed)
                        skipped++;
                        continue;
                    }
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
                    // Build the Event XML element
                    var eventElement = builder.BuildEventXml(doc, mapping, context);
                    if (eventElement != null)
                    {
                        string sourceId = eventElement.GetAttribute("SourceID");

                        // Group by SourceID: if we already have an event for this source control,
                        // merge the new handlers into the existing event rather than creating a duplicate.
                        if (!string.IsNullOrEmpty(sourceId) && eventsBySourceId.ContainsKey(sourceId))
                        {
                            var existingEvent = eventsBySourceId[sourceId];
                            var existingHandlers = existingEvent.SelectSingleNode("Handlers") as XmlElement;
                            var newHandlers = eventElement.SelectSingleNode("Handlers") as XmlElement;

                            if (newHandlers != null && existingHandlers != null)
                            {
                                foreach (XmlNode handler in newHandlers.ChildNodes)
                                {
                                    existingHandlers.AppendChild(doc.ImportNode(handler, true));
                                }
                            }
                            added++;
                            Console.WriteLine($"    Merged {builder.RuleType} rule '{mapping.InfoPathRuleName}' into existing event for {eventElement.GetAttribute("SourceName")}");
                        }
                        else
                        {
                            events.AppendChild(eventElement);
                            if (!string.IsNullOrEmpty(sourceId))
                                eventsBySourceId[sourceId] = eventElement;
                            added++;
                            Console.WriteLine($"    Added {builder.RuleType} rule: {mapping.InfoPathRuleName}");
                        }
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
