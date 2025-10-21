using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using K2SmartObjectGenerator.Config;
using K2SmartObjectGenerator.Utilities;
using SourceCode.SmartObjects.Management;

namespace K2SmartObjectGenerator
{
    /// <summary>
    /// Simple SmartObject model for K2 generation
    /// </summary>
    internal class SmartObject
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public List<SmartProperty> Properties { get; set; } = new();
    }

    /// <summary>
    /// Simple SmartProperty model for K2 generation
    /// </summary>
    internal class SmartProperty
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Type { get; set; } = "Text";
    }

    /// <summary>
    /// Generates K2 SmartObjects from JSON form definitions
    /// </summary>
    public class SmartObjectGenerator
    {
        private readonly ServerConnectionManager _connectionManager;
        private readonly GeneratorConfiguration _config;
        private readonly Dictionary<string, string> _fieldMappings = new();

        public SmartObjectGenerator(ServerConnectionManager connectionManager, GeneratorConfiguration config)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// Field mappings between form fields and SmartObject properties
        /// </summary>
        public Dictionary<string, string> FieldMappings => _fieldMappings;

        /// <summary>
        /// Generates SmartObjects from JSON form definition
        /// </summary>
        public void GenerateSmartObjectsFromJson(string jsonContent)
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

                // Create main SmartObject
                CreateMainSmartObject(formName, formDefinition);

                // Create child SmartObjects for repeating sections
                CreateRepeatingSectionSmartObjects(formName, formDefinition);

                // Create lookup SmartObject if needed
                CreateLookupSmartObject(formName, formDefinition);
            }
        }

        /// <summary>
        /// Attempts to delete an existing SmartObject
        /// </summary>
        public void ForceDeleteSmartObject(string smartObjectName)
        {
            try
            {
                var mgmtServer = _connectionManager.ManagementServer;
                if (mgmtServer?.Connection == null || !mgmtServer.Connection.IsConnected)
                    return;

                // Note: Actual K2 SmartObject deletion requires proper API usage
                // This is a stub that would need to be implemented with correct K2 API calls
                // Example: mgmtServer.DeleteSmartObject(smartObjectName, SmartObjectRoot.SmartObjects);
            }
            catch
            {
                // Suppress errors during cleanup
            }
        }

        private void CreateMainSmartObject(string formName, JObject formDefinition)
        {
            var formDefData = formDefinition["FormDefinition"] as JObject;
            if (formDefData == null)
                return;

            var dataArray = formDefData["Data"] as JArray;
            if (dataArray == null)
                return;

            var smartObject = new SmartObject();
            smartObject.Name = formName;
            smartObject.DisplayName = formName.Replace("_", " ");

            // Add properties from form fields (excluding repeating sections)
            foreach (JObject dataItem in dataArray)
            {
                var isRepeating = dataItem["IsRepeating"]?.Value<bool>() ?? false;
                if (!isRepeating)
                {
                    AddPropertyFromFormField(smartObject, dataItem);
                }
            }

            // Deploy the SmartObject
            DeploySmartObject(smartObject);

            // Register it
            SmartObjectViewRegistry.RegisterSmartObject(formName, SmartObjectViewRegistry.SmartObjectType.Main);
        }

        private void CreateRepeatingSectionSmartObjects(string formName, JObject formDefinition)
        {
            var formDefData = formDefinition["FormDefinition"] as JObject;
            if (formDefData == null)
                return;

            var dataArray = formDefData["Data"] as JArray;
            if (dataArray == null)
                return;

            var repeatingSections = new Dictionary<string, List<JObject>>();

            // Group repeating section fields
            foreach (JObject dataItem in dataArray)
            {
                var isRepeating = dataItem["IsRepeating"]?.Value<bool>() ?? false;
                var sectionName = dataItem["RepeatingSectionName"]?.Value<string>();

                if (isRepeating && !string.IsNullOrEmpty(sectionName))
                {
                    if (!repeatingSections.ContainsKey(sectionName))
                    {
                        repeatingSections[sectionName] = new List<JObject>();
                    }
                    repeatingSections[sectionName].Add(dataItem);
                }
            }

            // Create SmartObject for each repeating section
            foreach (var section in repeatingSections)
            {
                var sectionSmartObjectName = $"{formName}_{section.Key.Replace(" ", "_")}";
                var smartObject = new SmartObject();
                smartObject.Name = sectionSmartObjectName;
                smartObject.DisplayName = section.Key;

                // Add properties from section fields
                foreach (var field in section.Value)
                {
                    AddPropertyFromFormField(smartObject, field);
                }

                // Deploy the SmartObject
                DeploySmartObject(smartObject);

                // Register it
                SmartObjectViewRegistry.RegisterSmartObject(sectionSmartObjectName, SmartObjectViewRegistry.SmartObjectType.Child, formName);
            }
        }

        private void CreateLookupSmartObject(string formName, JObject formDefinition)
        {
            var formDefData = formDefinition["FormDefinition"] as JObject;
            if (formDefData == null)
                return;

            var lookupsArray = formDefData["Lookups"] as JArray;
            if (lookupsArray == null || lookupsArray.Count == 0)
                return;

            var lookupSmartObjectName = $"{formName}_Lookups";
            var smartObject = new SmartObject();
            smartObject.Name = lookupSmartObjectName;
            smartObject.DisplayName = $"{formName} Lookups";

            // Add properties for lookup fields
            foreach (JObject lookup in lookupsArray)
            {
                var lookupName = lookup["Name"]?.Value<string>();
                if (!string.IsNullOrEmpty(lookupName))
                {
                    var property = new SmartProperty();
                    property.Name = lookupName.Replace(" ", "_");
                    property.DisplayName = lookupName;
                    property.Type = "Text";
                    smartObject.Properties.Add(property);
                }
            }

            // Deploy the SmartObject
            DeploySmartObject(smartObject);

            // Register it
            SmartObjectViewRegistry.RegisterSmartObject(lookupSmartObjectName, SmartObjectViewRegistry.SmartObjectType.Lookup, formName);
        }

        private void AddPropertyFromFormField(SmartObject smartObject, JObject field)
        {
            var fieldName = field["Name"]?.Value<string>();
            var fieldType = field["Type"]?.Value<string>();

            if (string.IsNullOrEmpty(fieldName))
                return;

            var property = new SmartProperty();
            property.Name = fieldName.Replace(" ", "_");
            property.DisplayName = fieldName;
            property.Type = MapFieldTypeToSmartObjectType(fieldType);

            smartObject.Properties.Add(property);

            // Track field mapping
            _fieldMappings[fieldName] = property.Name;
        }

        private string MapFieldTypeToSmartObjectType(string? fieldType)
        {
            return fieldType?.ToLower() switch
            {
                "text" => "Text",
                "number" => "Number",
                "decimal" => "Decimal",
                "date" => "DateTime",
                "datetime" => "DateTime",
                "boolean" => "YesNo",
                "checkbox" => "YesNo",
                _ => "Text"
            };
        }

        private void DeploySmartObject(SmartObject smartObject)
        {
            var mgmtServer = _connectionManager.ManagementServer;
            if (mgmtServer?.Connection == null || !mgmtServer.Connection.IsConnected)
                throw new InvalidOperationException("Not connected to K2 Management Server");

            // Check if SmartObject already exists and delete it
            try
            {
                // Note: Actual K2 SmartObject checking and deletion requires proper API usage
                // This is a stub that would need to be implemented with correct K2 API calls
                // Example:
                // var existing = mgmtServer.GetSmartObjects(smartObject.Name);
                // if (existing.TotalCount > 0)
                // {
                //     mgmtServer.DeleteSmartObject(smartObject.Name, SmartObjectRoot.SmartObjects);
                // }
            }
            catch
            {
                // SmartObject doesn't exist, which is fine
            }

            // Create the SmartObject
            // Note: This is simplified - actual K2 deployment requires service instance configuration
            // and proper SmartObject definition using SourceCode.SmartObjects.Authoring types
            // mgmtServer.CreateSmartObject(smartObject);
        }
    }
}
