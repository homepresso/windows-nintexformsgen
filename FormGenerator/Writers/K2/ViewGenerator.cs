using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using K2SmartObjectGenerator.Config;
using K2SmartObjectGenerator.Utilities;

namespace K2SmartObjectGenerator
{
    /// <summary>
    /// Generates K2 Views (Capture, Item, List) from JSON form definitions
    /// </summary>
    public class ViewGenerator
    {
        private readonly ServerConnectionManager _connectionManager;
        private readonly Dictionary<string, string> _fieldMappings;
        private readonly SmartObjectGenerator _smartObjectGenerator;
        private readonly GeneratorConfiguration _config;
        private readonly Dictionary<string, string> _viewTitles = new();

        public ViewGenerator(
            ServerConnectionManager connectionManager,
            Dictionary<string, string> fieldMappings,
            SmartObjectGenerator smartObjectGenerator,
            GeneratorConfiguration config)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _fieldMappings = fieldMappings ?? throw new ArgumentNullException(nameof(fieldMappings));
            _smartObjectGenerator = smartObjectGenerator ?? throw new ArgumentNullException(nameof(smartObjectGenerator));
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// View titles mapped to SmartObject names
        /// </summary>
        public Dictionary<string, string> ViewTitles => _viewTitles;

        /// <summary>
        /// Generates Views from JSON form definition
        /// </summary>
        public void GenerateViewsFromJson(string jsonContent)
        {
            if (string.IsNullOrWhiteSpace(jsonContent))
                throw new ArgumentException("JSON content cannot be null or empty", nameof(jsonContent));

            var formData = JObject.Parse(jsonContent);

            foreach (var form in formData.Properties())
            {
                var formName = form.Name.Replace(" ", "_");
                var formDefinition = form.Value as JObject;

                if (formDefinition == null)
                    continue;

                // Create views for main SmartObject
                CreateViewsForSmartObject(formName, formDefinition, isRepeatingSection: false);

                // Create views for repeating sections
                CreateRepeatingSectionViews(formName, formDefinition);

                // Create views for lookup SmartObject if needed
                CreateLookupViews(formName, formDefinition);
            }
        }

        /// <summary>
        /// Attempts to cleanup existing views
        /// </summary>
        public void TryCleanupExistingViews(string jsonContent)
        {
            try
            {
                var formData = JObject.Parse(jsonContent);
                foreach (var form in formData.Properties())
                {
                    var formName = form.Name.Replace(" ", "_");

                    // Cleanup views for main SmartObject
                    DeleteViewIfExists($"{formName}_Capture");
                    DeleteViewIfExists($"{formName}_Item");
                    DeleteViewIfExists($"{formName}_List");

                    // Cleanup repeating section views
                    var formDefinition = form.Value as JObject;
                    var formDefData = formDefinition?["FormDefinition"] as JObject;
                    var dataArray = formDefData?["Data"] as JArray;

                    if (dataArray != null)
                    {
                        var sections = new HashSet<string>();
                        foreach (JObject dataItem in dataArray)
                        {
                            var isRepeating = dataItem["IsRepeating"]?.Value<bool>() ?? false;
                            var sectionName = dataItem["RepeatingSectionName"]?.Value<string>();

                            if (isRepeating && !string.IsNullOrEmpty(sectionName))
                            {
                                sections.Add(sectionName);
                            }
                        }

                        foreach (var section in sections)
                        {
                            var sectionName = section.Replace(" ", "_");
                            DeleteViewIfExists($"{formName}_{sectionName}_Capture");
                            DeleteViewIfExists($"{formName}_{sectionName}_Item");
                            DeleteViewIfExists($"{formName}_{sectionName}_List");
                        }
                    }
                }
            }
            catch
            {
                // Suppress errors during cleanup
            }
        }

        private void CreateViewsForSmartObject(string smartObjectName, JObject formDefinition, bool isRepeatingSection, string? sectionName = null)
        {
            var formDefData = formDefinition["FormDefinition"] as JObject;
            if (formDefData == null)
                return;

            var displayName = smartObjectName.Replace("_", " ");
            var prefix = isRepeatingSection && !string.IsNullOrEmpty(sectionName)
                ? $"{smartObjectName}_{sectionName.Replace(" ", "_")}"
                : smartObjectName;

            // Create Capture View (for creating new items)
            var captureViewName = $"{prefix}_Capture";
            CreateView(captureViewName, smartObjectName, SmartObjectViewRegistry.ViewType.Capture);
            _viewTitles[captureViewName] = $"New {displayName}";

            // Create Item View (for viewing/editing single item)
            var itemViewName = $"{prefix}_Item";
            CreateView(itemViewName, smartObjectName, SmartObjectViewRegistry.ViewType.Item);
            _viewTitles[itemViewName] = $"View {displayName}";

            // Create List View (for listing multiple items)
            var listViewName = $"{prefix}_List";
            CreateView(listViewName, smartObjectName, SmartObjectViewRegistry.ViewType.List);
            _viewTitles[listViewName] = $"{displayName} List";
        }

        private void CreateRepeatingSectionViews(string formName, JObject formDefinition)
        {
            var formDefData = formDefinition["FormDefinition"] as JObject;
            if (formDefData == null)
                return;

            var dataArray = formDefData["Data"] as JArray;
            if (dataArray == null)
                return;

            var repeatingSections = new HashSet<string>();

            foreach (JObject dataItem in dataArray)
            {
                var isRepeating = dataItem["IsRepeating"]?.Value<bool>() ?? false;
                var sectionName = dataItem["RepeatingSectionName"]?.Value<string>();

                if (isRepeating && !string.IsNullOrEmpty(sectionName))
                {
                    repeatingSections.Add(sectionName);
                }
            }

            foreach (var section in repeatingSections)
            {
                var sectionSmartObjectName = $"{formName}_{section.Replace(" ", "_")}";
                CreateViewsForSmartObject(formName, formDefinition, isRepeatingSection: true, sectionName: section);
            }
        }

        private void CreateLookupViews(string formName, JObject formDefinition)
        {
            var formDefData = formDefinition["FormDefinition"] as JObject;
            if (formDefData == null)
                return;

            var lookupsArray = formDefData["Lookups"] as JArray;
            if (lookupsArray == null || lookupsArray.Count == 0)
                return;

            var lookupSmartObjectName = $"{formName}_Lookups";

            // Create only List view for lookups
            var listViewName = $"{lookupSmartObjectName}_List";
            CreateView(listViewName, lookupSmartObjectName, SmartObjectViewRegistry.ViewType.List);
            _viewTitles[listViewName] = $"{formName} Lookups";
        }

        private void CreateView(string viewName, string smartObjectName, SmartObjectViewRegistry.ViewType viewType)
        {
            // Note: Actual K2 view creation would use K2 Forms API
            // This is a simplified stub that registers the view
            SmartObjectViewRegistry.RegisterView(viewName, smartObjectName, viewType);
        }

        private void DeleteViewIfExists(string viewName)
        {
            try
            {
                // Note: Actual K2 view deletion would use K2 Forms Management API
                // This is a stub for now
            }
            catch
            {
                // Suppress errors during cleanup
            }
        }
    }
}
