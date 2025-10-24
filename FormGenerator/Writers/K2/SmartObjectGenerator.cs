using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using SourceCode.SmartObjects.Authoring;
using SourceCode.SmartObjects.Management;
using SourceCode.SmartObjects.Client;
using K2SmartObjectGenerator.Models;
using K2SmartObjectGenerator.Utilities;
using K2SmartObjectGenerator.Config;

namespace K2SmartObjectGenerator
{
    public class SmartObjectGenerator
    {
        private readonly ServerConnectionManager _connectionManager;
        private readonly Dictionary<string, Dictionary<string, FieldInfo>> _smoFieldMappings;
        private readonly GeneratorConfiguration _config;
        private readonly Dictionary<string, string> _createdSmartObjectGuids = new Dictionary<string, string>();

        public SmartObjectGenerator(ServerConnectionManager connectionManager, GeneratorConfiguration config = null)
        {
            _connectionManager = connectionManager;
            _config = config ?? GeneratorConfiguration.CreateDefault();

            // CRITICAL: Ensure logging config is initialized and disabled for performance
            if (_config.Logging == null)
                _config.Logging = new LoggingConfiguration();

            _smoFieldMappings = new Dictionary<string, Dictionary<string, FieldInfo>>();
        }

        public Dictionary<string, Dictionary<string, FieldInfo>> FieldMappings => _smoFieldMappings;
        public Dictionary<string, string> CreatedSmartObjectGuids => _createdSmartObjectGuids;

        /// <summary>
        /// Synchronous wrapper for GenerateSmartObjectsFromJsonAsync - for backward compatibility
        /// </summary>
        public void GenerateSmartObjectsFromJson(string jsonContent)
        {
            GenerateSmartObjectsFromJsonAsync(jsonContent).GetAwaiter().GetResult();
        }

        public async Task GenerateSmartObjectsFromJsonAsync(string jsonContent)
        {
            // PERFORMANCE FIX: Open connection once for entire generation process
            try
            {
                _connectionManager.Connect();
                if (_config.Logging.ShowProgress)
                    Console.WriteLine("✓ Connected to K2 server - Connection will be reused for all operations");

                JObject formData = JObject.Parse(jsonContent);
                string formDisplayName = formData.Properties().First().Name; // Keep original name with spaces
                // PERFORMANCE FIX: Use proper name sanitization to match K2's internal rules
                // This prevents name mismatches when deleting/creating SmartObjects
                string formName = NameSanitizer.SanitizeSmartObjectName(formDisplayName);
                JObject formDefinition = formData[formDisplayName] as JObject;

                if (_config.Logging.ShowProgress)
                    Console.WriteLine($"\nProcessing form: {formName}");

                // Extract TargetFolder from JSON (injected by K2GenerationService)
                JObject formDefJson = formDefinition?["FormDefinition"] as JObject;
                string targetFolder = formDefJson?["TargetFolder"]?.Value<string>();
                if (string.IsNullOrEmpty(targetFolder))
                {
                    targetFolder = _config?.Form?.TargetFolder ?? "Generated";
                }
                if (_config.Logging.VerboseLogging)
                Console.WriteLine($"  SmartObject target folder: {targetFolder}");

                JArray dataArray = formDefinition["FormDefinition"]["Data"] as JArray;
                if (dataArray == null)
                {
                    Console.WriteLine("No data columns found in JSON");
                    return;
                }

                // Generate lookup SmartObjects first
                await GenerateLookupSmartObjectsAsync(formName, formDisplayName, dataArray, targetFolder);

                // Separate main fields and repeating sections
                var mainFields = new List<JObject>();
                var repeatingSections = new Dictionary<string, List<JObject>>();

                foreach (JObject dataItem in dataArray)
                {
                    bool isRepeating = dataItem["IsRepeating"]?.Value<bool>() ?? false;
                    string sectionName = dataItem["RepeatingSectionName"]?.Value<string>();

                    if (isRepeating && !string.IsNullOrEmpty(sectionName))
                    {
                        if (!repeatingSections.ContainsKey(sectionName))
                        {
                            repeatingSections[sectionName] = new List<JObject>();
                        }
                        repeatingSections[sectionName].Add(dataItem);
                    }
                    else if (!isRepeating)
                    {
                        mainFields.Add(dataItem);
                    }
                }

                SmartObjectDefinitionsPublish publishSmo = new SmartObjectDefinitionsPublish();

                // Create main SmartObject and track its fields
                if (_config.Logging.ShowProgress)
                    Console.WriteLine($"\nCreating main SmartObject: {formName}");
                SmartObjectDefinition mainSmo = CreateMainSmartObject(formName, mainFields, formDisplayName, targetFolder);
                publishSmo.SmartObjects.Add(mainSmo);

                // Create child SmartObjects for repeating sections
                foreach (var section in repeatingSections)
                {
                    // PERFORMANCE FIX: Sanitize section names to match K2's rules
                    string sectionName = NameSanitizer.SanitizeSmartObjectName(section.Key);
                    string childSmoName = $"{formName}_{sectionName}";
                    if (_config.Logging.ShowProgress)
                        Console.WriteLine($"Creating child SmartObject: {childSmoName}");
                    SmartObjectDefinition childSmo = CreateChildSmartObject(childSmoName, section.Value, formName, formDisplayName, targetFolder);
                    publishSmo.SmartObjects.Add(childSmo);
                }

                // Publish SmartObjects first
                if (_config.Logging.ShowProgress)
                    Console.WriteLine("\nPublishing SmartObjects...");
                await PublishSmartObjectsAsync(publishSmo);

                // PERFORMANCE FIX: Give K2 server time to index published SmartObjects
                // This is necessary before retrieving them for association creation
                if (repeatingSections.Count > 0)
                {
                    // CRITICAL: Scale delay aggressively with form complexity
                    // Large forms need significantly more time for K2 server indexing
                    // Base 10s + 1s per section, max 30s
                    int indexingDelay = Math.Min(10000 + (repeatingSections.Count * 1000), 30000);
                    if (_config.Logging.ShowProgress)
                        Console.WriteLine($"Waiting {indexingDelay / 1000}s for K2 server indexing ({repeatingSections.Count + 2} SmartObjects)...");
                    await Task.Delay(indexingDelay);
                }

                // Create associations
                if (repeatingSections.Count > 0)
                {
                    if (_config.Logging.ShowProgress)
                        Console.WriteLine("\nCreating associations...");
                    publishSmo.Dispose();
                    publishSmo = new SmartObjectDefinitionsPublish();

                    foreach (var section in repeatingSections)
                    {
                        // PERFORMANCE FIX: Sanitize section names to match K2's rules (must match child SmartObject creation)
                        string sectionName = NameSanitizer.SanitizeSmartObjectName(section.Key);
                        string childSmoName = $"{formName}_{sectionName}";
                        if (_config.Logging.VerboseLogging)
                            Console.WriteLine($"Creating association: {formName} -> {childSmoName}");
                        SmartObjectDefinition association = await CreateAssociationAsync(formName, childSmoName,
                            SourceCode.SmartObjects.Authoring.AssociationType.OneToMany, formDisplayName, targetFolder);
                        publishSmo.SmartObjects.Add(association);
                    }

                    // Publish associations
                    await PublishSmartObjectsAsync(publishSmo);
                }
            }
            finally
            {
                _connectionManager.Disconnect();
                if (_config.Logging.ShowProgress)
                    Console.WriteLine("✓ Disconnected from K2 server");
            }
        }

        private async Task GenerateLookupSmartObjectsAsync(string formName, string formDisplayName, JArray dataArray, string targetFolder)
        {
            // Collect all dropdown fields and their values
            Dictionary<string, JArray> allLookupData = new Dictionary<string, JArray>();

            foreach (JObject dataItem in dataArray)
            {
                string type = dataItem["Type"]?.Value<string>();
                string columnName = dataItem["ColumnName"]?.Value<string>();

                if (type?.ToLower() == "dropdown")
                {
                    JArray validValues = dataItem["ValidValues"] as JArray;
                    if (validValues != null && validValues.Count > 0)
                    {
                        allLookupData[columnName] = validValues;
                        Console.WriteLine($"  Found dropdown field: {columnName} with {validValues.Count} values");
                    }
                }
            }

            if (allLookupData.Count == 0)
            {
                Console.WriteLine("  No dropdown fields found, skipping lookup SmartObject creation");
                return;
            }

            // Create a single consolidated lookup SmartObject
            string consolidatedLookupName = $"{formName}_Lookups";
            if (_config.Logging.ShowProgress)
                Console.WriteLine($"Creating consolidated lookup SmartObject: {consolidatedLookupName}");

            SmartObjectDefinitionsPublish publishSmo = new SmartObjectDefinitionsPublish();
            SmartObjectDefinition lookupSmo = CreateConsolidatedLookupSmartObject(consolidatedLookupName, formDisplayName, targetFolder);
            publishSmo.SmartObjects.Add(lookupSmo);

            // Store the GUID for later reference
            string lookupGuid = lookupSmo.Guid.ToString();
            _createdSmartObjectGuids[consolidatedLookupName] = lookupGuid;

            if (_config.Logging.VerboseLogging)
                Console.WriteLine($"Publishing consolidated lookup SmartObject...");
            await PublishSmartObjectsAsync(publishSmo);

            // Add a small delay to ensure server synchronization
            await Task.Delay(1000);

            // Populate the consolidated lookup with all data
            if (_config.Logging.ShowProgress)
                Console.WriteLine($"Populating consolidated lookup SmartObject with data...");
            await PopulateConsolidatedLookupDataAsync(consolidatedLookupName, allLookupData);
        }

        private SmartObjectDefinition CreateConsolidatedLookupSmartObject(string smoName, string formName = null, string targetFolder = null)
        {
            // PERFORMANCE FIX: Reuses existing connection instead of creating new one
            DeleteSmartObject(smoName);

            ServiceInstance serviceInstance = ServiceInstance.Create(
                _connectionManager.ManagementServer.GetServiceInstanceForExtend(new Guid(_config.K2.SmartBoxGuid), string.Empty));
            ExtendObject extendObject = serviceInstance.GetCreateExtender();

            extendObject.Name = smoName;
            extendObject.Metadata.DisplayName = smoName.Replace("_", " ");

            // Add ID property (autonumber)
            ExtendObjectProperty idProperty = new ExtendObjectProperty();
            idProperty.Name = "ID";
            idProperty.Metadata.DisplayName = "ID";
            idProperty.Type = PropertyDefinitionType.Autonumber;
            idProperty.ExtendType = ExtendPropertyType.UniqueIdAuto;
            extendObject.Properties.Add(idProperty);

            // Add LookupType property
            ExtendObjectProperty lookupTypeProperty = new ExtendObjectProperty();
            lookupTypeProperty.Name = "LookupType";
            lookupTypeProperty.Metadata.DisplayName = "Lookup Type";
            lookupTypeProperty.Type = PropertyDefinitionType.Text;
            extendObject.Properties.Add(lookupTypeProperty);

            // Add Value property
            ExtendObjectProperty valueProperty = new ExtendObjectProperty();
            valueProperty.Name = "Value";
            valueProperty.Metadata.DisplayName = "Value";
            valueProperty.Type = PropertyDefinitionType.Text;
            extendObject.Properties.Add(valueProperty);

            // Add DisplayText property
            ExtendObjectProperty displayProperty = new ExtendObjectProperty();
            displayProperty.Name = "DisplayText";
            displayProperty.Metadata.DisplayName = "Display Text";
            displayProperty.Type = PropertyDefinitionType.Text;
            extendObject.Properties.Add(displayProperty);

            // Add IsDefault property
            ExtendObjectProperty defaultProperty = new ExtendObjectProperty();
            defaultProperty.Name = "IsDefault";
            defaultProperty.Metadata.DisplayName = "Is Default";
            defaultProperty.Type = PropertyDefinitionType.YesNo;
            extendObject.Properties.Add(defaultProperty);

            // Add Order property
            ExtendObjectProperty orderProperty = new ExtendObjectProperty();
            orderProperty.Name = "Order";
            orderProperty.Metadata.DisplayName = "Order";
            orderProperty.Type = PropertyDefinitionType.Number;
            extendObject.Properties.Add(orderProperty);

            SmartObjectDefinition smoDefinition = new SmartObjectDefinition();
            smoDefinition.Create(extendObject);
            // STRUCTURE: {TargetFolder}\{formName}\Lookups
            string category;
            if (string.IsNullOrEmpty(targetFolder))
            {
                category = string.IsNullOrEmpty(formName) ? "Generated Lookups" : $"{formName}\\Lookups";
            }
            else
            {
                category = string.IsNullOrEmpty(formName) ? $"{targetFolder}\\Lookups" : $"{targetFolder}\\{formName}\\Lookups";
            }
            smoDefinition.AddDeploymentCategory(category);
            smoDefinition.Build();

            string smoGuid = smoDefinition.Guid.ToString();
            _createdSmartObjectGuids[smoName] = smoGuid;

            // REGISTER WITH THE REGISTRY
            SmartObjectViewRegistry.RegisterSmartObject(
                smoName,
                smoGuid,
                SmartObjectViewRegistry.SmartObjectType.Consolidated
            );

            if (_config.Logging.VerboseLogging)
                Console.WriteLine($"  Created Consolidated Lookup SmartObject {smoName} with GUID: {smoGuid}");

            return smoDefinition;
        }

        private SmartObjectDefinition CreateMainSmartObject(string smoName, List<JObject> fields, string formName = null, string targetFolder = null)
        {
            // PERFORMANCE FIX: Reuses existing connection instead of creating new one
            DeleteSmartObject(smoName);

            ServiceInstance serviceInstance = ServiceInstance.Create(
                _connectionManager.ManagementServer.GetServiceInstanceForExtend(new Guid(_config.K2.SmartBoxGuid), string.Empty));
            ExtendObject extendObject = serviceInstance.GetCreateExtender();

            extendObject.Name = smoName;
            extendObject.Metadata.DisplayName = smoName.Replace("_", " ");

            _smoFieldMappings[smoName] = new Dictionary<string, FieldInfo>();

            HashSet<string> usedPropertyNames = new HashSet<string>();
            HashSet<string> usedDisplayNames = new HashSet<string>();

            // Add ID property
            ExtendObjectProperty idProperty = new ExtendObjectProperty();
            idProperty.Name = "ID";
            idProperty.Metadata.DisplayName = "ID";
            idProperty.Type = PropertyDefinitionType.Autonumber;
            idProperty.ExtendType = ExtendPropertyType.UniqueIdAuto;
            extendObject.Properties.Add(idProperty);
            usedPropertyNames.Add("ID");
            usedDisplayNames.Add("ID");

            _smoFieldMappings[smoName]["ID"] = new FieldInfo
            {
                FieldGuid = Guid.NewGuid().ToString(),
                FieldName = "ID",
                DisplayName = "ID",
                DataType = "autonumber"
            };

            // Add properties from JSON
            if (_config.Logging.VerboseLogging)
                Console.WriteLine($"\n=== Processing {fields.Count} fields for SmartObject '{smoName}' ===");

            foreach (var field in fields)
            {
                string columnName = field["ColumnName"]?.Value<string>();
                string displayName = field["DisplayName"]?.Value<string>();

                if (_config.Logging.VerboseLogging)
                    Console.WriteLine($"  Processing field: ColumnName='{columnName}', DisplayName='{displayName}'");

                ExtendObjectProperty property = CreatePropertyFromJson(field, usedPropertyNames, usedDisplayNames);
                if (property != null)
                {
                    extendObject.Properties.Add(property);

                    if (_config.Logging.VerboseLogging)
                        Console.WriteLine($"  ✓ Added property: Name='{property.Name}' | DisplayName='{property.Metadata.DisplayName}' ({property.Type})");

                    _smoFieldMappings[smoName][property.Name] = new FieldInfo
                    {
                        FieldGuid = Guid.NewGuid().ToString(),
                        FieldName = property.Name.ToUpper().Replace(" ", ""),
                        DisplayName = property.Metadata.DisplayName.Replace("_", " "),
                        DataType = GetDataTypeString(property.Type)
                    };
                }
            }

            if (_config.Logging.VerboseLogging)
                Console.WriteLine($"=== Finished processing fields. Total properties in ExtendObject: {extendObject.Properties.Count} ===\n");

            SmartObjectDefinition smoDefinition = new SmartObjectDefinition();
            smoDefinition.Create(extendObject);
            // STRUCTURE: {TargetFolder}\{formName}\SmartObjects
            string category;
            if (string.IsNullOrEmpty(targetFolder))
            {
                category = string.IsNullOrEmpty(formName) ? "Generated SmartObjects" : $"{formName}\\SmartObjects";
            }
            else
            {
                category = string.IsNullOrEmpty(formName) ? $"{targetFolder}\\SmartObjects" : $"{targetFolder}\\{formName}\\SmartObjects";
            }
            smoDefinition.AddDeploymentCategory(category);
            smoDefinition.Build();

            string smoGuid = smoDefinition.Guid.ToString();
            _createdSmartObjectGuids[smoName] = smoGuid;

            // REGISTER WITH THE REGISTRY
            SmartObjectViewRegistry.RegisterSmartObject(
                smoName,
                smoGuid,
                SmartObjectViewRegistry.SmartObjectType.Main
            );

            if (_config.Logging.VerboseLogging)
                Console.WriteLine($"  Created SmartObject {smoName} with GUID: {smoGuid}");

            return smoDefinition;
        }

        private SmartObjectDefinition CreateChildSmartObject(string smoName, List<JObject> fields, string parentSmoName, string formName = null, string targetFolder = null)
        {
            // PERFORMANCE FIX: Reuses existing connection instead of creating new one
            DeleteSmartObject(smoName);

            ServiceInstance serviceInstance = ServiceInstance.Create(
                _connectionManager.ManagementServer.GetServiceInstanceForExtend(new Guid(_config.K2.SmartBoxGuid), string.Empty));
            ExtendObject extendObject = serviceInstance.GetCreateExtender();

            extendObject.Name = smoName;
            extendObject.Metadata.DisplayName = smoName.Replace("_", " ");

            _smoFieldMappings[smoName] = new Dictionary<string, FieldInfo>();

            HashSet<string> usedPropertyNames = new HashSet<string>();
            HashSet<string> usedDisplayNames = new HashSet<string>();

            // Add ID property
            ExtendObjectProperty idProperty = new ExtendObjectProperty();
            idProperty.Name = "ID";
            idProperty.Metadata.DisplayName = "ID";
            idProperty.Type = PropertyDefinitionType.Autonumber;
            idProperty.ExtendType = ExtendPropertyType.UniqueIdAuto;
            extendObject.Properties.Add(idProperty);
            usedPropertyNames.Add("ID");
            usedDisplayNames.Add("ID");

            _smoFieldMappings[smoName]["ID"] = new FieldInfo
            {
                FieldGuid = Guid.NewGuid().ToString(),
                FieldName = "ID",
                DisplayName = "ID",
                DataType = "autonumber"
            };

            // Add ParentID property for foreign key
            ExtendObjectProperty parentIdProperty = new ExtendObjectProperty();
            parentIdProperty.Name = "ParentID";
            parentIdProperty.Metadata.DisplayName = "Parent ID";
            parentIdProperty.Type = PropertyDefinitionType.Number;
            extendObject.Properties.Add(parentIdProperty);
            usedPropertyNames.Add("ParentID");
            usedDisplayNames.Add("Parent ID");

            _smoFieldMappings[smoName]["ParentID"] = new FieldInfo
            {
                FieldGuid = Guid.NewGuid().ToString(),
                FieldName = "PARENTID",
                DisplayName = "Parent ID",
                DataType = "number"
            };

            // Add properties from JSON
            foreach (var field in fields)
            {
                ExtendObjectProperty property = CreatePropertyFromJson(field, usedPropertyNames, usedDisplayNames);
                if (property != null)
                {
                    extendObject.Properties.Add(property);
                    if (_config.Logging.VerboseLogging)
                        Console.WriteLine($"  Added property: {property.Name} | DisplayName: {property.Metadata.DisplayName} ({property.Type})");

                    _smoFieldMappings[smoName][property.Name] = new FieldInfo
                    {
                        FieldGuid = Guid.NewGuid().ToString(),
                        FieldName = property.Name.ToUpper().Replace(" ", ""),
                        DisplayName = property.Metadata.DisplayName.Replace("_", " "),
                        DataType = GetDataTypeString(property.Type)
                    };
                }
            }

            SmartObjectDefinition smoDefinition = new SmartObjectDefinition();
            smoDefinition.Create(extendObject);
            // STRUCTURE: {TargetFolder}\{formName}\SmartObjects
            string category;
            if (string.IsNullOrEmpty(targetFolder))
            {
                category = string.IsNullOrEmpty(formName) ? "Generated SmartObjects" : $"{formName}\\SmartObjects";
            }
            else
            {
                category = string.IsNullOrEmpty(formName) ? $"{targetFolder}\\SmartObjects" : $"{targetFolder}\\{formName}\\SmartObjects";
            }
            smoDefinition.AddDeploymentCategory(category);
            smoDefinition.Build();

            string smoGuid = smoDefinition.Guid.ToString();
            _createdSmartObjectGuids[smoName] = smoGuid;

            // REGISTER WITH THE REGISTRY
            SmartObjectViewRegistry.RegisterSmartObject(
                smoName,
                smoGuid,
                SmartObjectViewRegistry.SmartObjectType.Child,
                parentSmoName
            );

            if (_config.Logging.VerboseLogging)
            {
                Console.WriteLine($"  Created Child SmartObject {smoName} with GUID: {smoGuid}");
                Console.WriteLine($"  Parent SmartObject: {parentSmoName}");
            }

            return smoDefinition;
        }

        private async Task PopulateConsolidatedLookupDataAsync(string smoName, Dictionary<string, JArray> allLookupData)
        {
            SmartObjectClientServer smoClient = null;
            try
            {
                await Task.Delay(500);

                smoClient = new SmartObjectClientServer();
                smoClient.CreateConnection();
                smoClient.Connection.Open(_connectionManager.ConnectionString.ConnectionString);

                SmartObject smo = smoClient.GetSmartObject(smoName);

                foreach (var lookupEntry in allLookupData)
                {
                    string lookupType = lookupEntry.Key;
                    JArray validValues = lookupEntry.Value;

                    // REGISTER LOOKUP FIELD WITH REGISTRY
                    SmartObjectViewRegistry.RegisterLookupSmartObject(
                        lookupType,
                        smoName,
                        _createdSmartObjectGuids[smoName]
                    );

                    int order = 0;
                    foreach (JObject value in validValues)
                    {
                        smo.MethodToExecute = "Create";

                        foreach (SmartProperty prop in smo.Properties)
                        {
                            prop.Value = null;
                        }

                        smo.Properties["LookupType"].Value = lookupType;

                        string itemValue = value["Value"]?.Value<string>() ?? "";
                        smo.Properties["Value"].Value = itemValue;

                        string displayText = value["DisplayText"]?.Value<string>() ?? itemValue;
                        if (string.IsNullOrEmpty(displayText) && string.IsNullOrEmpty(itemValue))
                        {
                            displayText = "Select...";
                        }
                        smo.Properties["DisplayText"].Value = displayText;

                        bool isDefault = value["IsDefault"]?.Value<bool>() ?? false;
                        smo.Properties["IsDefault"].Value = isDefault ? "True" : "False";

                        if (value["Order"] != null)
                        {
                            smo.Properties["Order"].Value = value["Order"].Value<int>().ToString();
                        }
                        else
                        {
                            smo.Properties["Order"].Value = order.ToString();
                        }

                        smoClient.ExecuteScalar(smo);
                        if (_config.Logging.VerboseLogging)
                            Console.WriteLine($"    Added to {lookupType}: '{displayText}' = '{itemValue}' (Order: {order}, Default: {isDefault})");
                        order++;
                    }
                }

                if (_config.Logging.VerboseLogging)
                    Console.WriteLine($"  Successfully populated consolidated lookup with data for {allLookupData.Count} fields");
            }
            catch (Exception ex)
            {
                // Always show errors
                Console.WriteLine($"    ERROR: Could not populate consolidated lookup data: {ex.Message}");
            }
            finally
            {
                if (smoClient != null && smoClient.Connection != null && smoClient.Connection.IsConnected)
                {
                    smoClient.Connection.Close();
                }
            }
        }

        // Rest of the methods remain the same...
        private ExtendObjectProperty CreatePropertyFromJson(JObject field, HashSet<string> usedPropertyNames, HashSet<string> usedDisplayNames)
        {
            string columnName = field["ColumnName"]?.Value<string>();
            string type = field["Type"]?.Value<string>();
            string displayName = field["DisplayName"]?.Value<string>();

            if (string.IsNullOrEmpty(columnName))
                return null;

            string sanitizedName = NameSanitizer.SanitizePropertyName(columnName);

            // Check for duplicate property name
            if (usedPropertyNames.Contains(sanitizedName))
            {
                Console.WriteLine($"    Warning: Duplicate property name '{sanitizedName}' (from field '{columnName}'). Skipping field.");
                return null;
            }

            usedPropertyNames.Add(sanitizedName);

            ExtendObjectProperty property = new ExtendObjectProperty();
            property.Name = sanitizedName;

            // Determine the base display name
            string baseDisplayName;
            if (!string.IsNullOrEmpty(displayName))
            {
                baseDisplayName = displayName.Replace("_", " ");
            }
            else
            {
                baseDisplayName = columnName.Replace("_", " ");
            }

            // Handle duplicate display names by adding a suffix (case-insensitive comparison)
            string finalDisplayName = baseDisplayName;
            int suffix = 1;
            while (usedDisplayNames.Any(d => string.Equals(d, finalDisplayName, StringComparison.OrdinalIgnoreCase)))
            {
                suffix++;
                finalDisplayName = $"{baseDisplayName} ({suffix})";
                Console.WriteLine($"    Warning: Duplicate display name '{baseDisplayName}' found. Using '{finalDisplayName}' instead.");
            }

            usedDisplayNames.Add(finalDisplayName);
            property.Metadata.DisplayName = finalDisplayName;

            // Map field types to K2 property types
            switch (type?.ToLower())
            {
                case "textfield":
                case "richtext":
                case "label":
                case "span":
                    property.Type = PropertyDefinitionType.Text;
                    break;

                case "datepicker":
                    property.Type = PropertyDefinitionType.DateTime;
                    break;

                case "checkbox":
                    property.Type = PropertyDefinitionType.YesNo;
                    break;

                case "dropdown":
                    property.Type = PropertyDefinitionType.Text;
                    break;

                case "repeatingtable":
                    return null;

                default:
                    property.Type = PropertyDefinitionType.Text;
                    break;
            }

            // Handle SQL mapping if present
            JObject sqlMapping = field["SqlMapping"] as JObject;
            if (sqlMapping != null)
            {
                string sqlDataType = sqlMapping["SqlDataType"]?.Value<string>();
                if (!string.IsNullOrEmpty(sqlDataType))
                {
                    if (sqlDataType.Contains("INT") || sqlDataType.Contains("NUMERIC"))
                        property.Type = PropertyDefinitionType.Number;
                    else if (sqlDataType.Contains("BIT"))
                        property.Type = PropertyDefinitionType.YesNo;
                    else if (sqlDataType.Contains("DATE") || sqlDataType.Contains("TIME"))
                        property.Type = PropertyDefinitionType.DateTime;
                    else if (sqlDataType.Contains("DECIMAL") || sqlDataType.Contains("FLOAT"))
                        property.Type = PropertyDefinitionType.Decimal;
                }
            }

            return property;
        }

        /// <summary>
        /// Retrieves SmartObject definition with retry logic to handle K2 server indexing delays
        /// Validates that the SmartObject has the expected properties before considering it successful
        /// </summary>
        private async Task<string> GetSmartObjectWithRetryAsync(string smoName, string smoType, int maxRetries = 8, int delayMs = 3000)
        {
            Exception lastException = null;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    if (_config.Logging.VerboseLogging)
                        Console.WriteLine($"[Retry {attempt}/{maxRetries}] Attempting to get {smoType} SmartObject: {smoName}");

                    string xml = _connectionManager.ManagementServer.GetSmartObjectDefinition(smoName);

                    // PERFORMANCE FIX: Validate the SmartObject has properties before returning
                    // K2 server may return XML before properties are fully indexed
                    try
                    {
                        SmartObjectDefinition testObj = SmartObjectDefinition.Create(xml);
                        if (testObj.Properties.Count < 1)
                        {
                            throw new Exception($"SmartObject retrieved but has no properties. Server may still be indexing.");
                        }
                    }
                    catch (Exception validateEx)
                    {
                        if (attempt < maxRetries)
                        {
                            // Exponential backoff: delay increases with each attempt
                            int actualDelay = delayMs * attempt;
                            if (_config.Logging.VerboseLogging)
                            {
                                Console.WriteLine($"[Retry {attempt}/{maxRetries}] ✗ Validation failed for {smoType} SmartObject '{smoName}': {validateEx.Message}");
                                Console.WriteLine($"[Retry {attempt}/{maxRetries}] Waiting {actualDelay}ms before retry (exponential backoff)...");
                            }
                            await Task.Delay(actualDelay);
                            continue;
                        }
                        throw;
                    }

                    if (_config.Logging.VerboseLogging)
                        Console.WriteLine($"[Retry {attempt}/{maxRetries}] ✓ Successfully retrieved and validated {smoType} SmartObject: {smoName}");
                    return xml;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    if (_config.Logging.VerboseLogging)
                        Console.WriteLine($"[Retry {attempt}/{maxRetries}] ✗ Failed to get {smoType} SmartObject '{smoName}': {ex.Message}");

                    if (attempt < maxRetries)
                    {
                        // Exponential backoff: delay increases with each attempt
                        int actualDelay = delayMs * attempt;
                        if (_config.Logging.VerboseLogging)
                            Console.WriteLine($"[Retry {attempt}/{maxRetries}] Waiting {actualDelay}ms before retry (exponential backoff)...");
                        await Task.Delay(actualDelay);
                    }
                }
            }

            throw new Exception($"Failed to retrieve {smoType} SmartObject '{smoName}' after {maxRetries} attempts. Last error: {lastException?.Message}", lastException);
        }

        /// <summary>
        /// Retrieves Association SmartObject with retry logic to handle K2 server indexing delays
        /// Validates that the SmartObject has the expected properties before considering it successful
        /// </summary>
        private async Task<string> GetAssociationSmartObjectWithRetryAsync(string smoName, string smoType, int maxRetries = 8, int delayMs = 3000)
        {
            Exception lastException = null;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    if (_config.Logging.VerboseLogging)
                        Console.WriteLine($"[Retry {attempt}/{maxRetries}] Attempting to get {smoType} Association SmartObject: {smoName}");

                    string xml = _connectionManager.ManagementServer.GetAssociationSmartObject(smoName);

                    // PERFORMANCE FIX: Validate the SmartObject has properties before returning
                    // K2 server may return XML before properties are fully indexed
                    try
                    {
                        AssociationSmartObject testObj = AssociationSmartObject.Create(xml);
                        if (testObj.Properties.Count < 2)
                        {
                            throw new Exception($"SmartObject retrieved but only has {testObj.Properties.Count} properties (expected at least 2). Server may still be indexing.");
                        }
                    }
                    catch (Exception validateEx)
                    {
                        if (attempt < maxRetries)
                        {
                            // Exponential backoff: delay increases with each attempt
                            int actualDelay = delayMs * attempt;
                            if (_config.Logging.VerboseLogging)
                            {
                                Console.WriteLine($"[Retry {attempt}/{maxRetries}] ✗ Validation failed for {smoType} Association SmartObject '{smoName}': {validateEx.Message}");
                                Console.WriteLine($"[Retry {attempt}/{maxRetries}] Waiting {actualDelay}ms before retry (exponential backoff)...");
                            }
                            await Task.Delay(actualDelay);
                            continue;
                        }
                        throw;
                    }

                    if (_config.Logging.VerboseLogging)
                        Console.WriteLine($"[Retry {attempt}/{maxRetries}] ✓ Successfully retrieved and validated {smoType} Association SmartObject: {smoName}");
                    return xml;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    if (_config.Logging.VerboseLogging)
                        Console.WriteLine($"[Retry {attempt}/{maxRetries}] ✗ Failed to get {smoType} Association SmartObject '{smoName}': {ex.Message}");

                    if (attempt < maxRetries)
                    {
                        // Exponential backoff: delay increases with each attempt
                        int actualDelay = delayMs * attempt;
                        if (_config.Logging.VerboseLogging)
                            Console.WriteLine($"[Retry {attempt}/{maxRetries}] Waiting {actualDelay}ms before retry (exponential backoff)...");
                        await Task.Delay(actualDelay);
                    }
                }
            }

            throw new Exception($"Failed to retrieve {smoType} Association SmartObject '{smoName}' after {maxRetries} attempts. Last error: {lastException?.Message}", lastException);
        }

        private async Task<SmartObjectDefinition> CreateAssociationAsync(string parentSmo, string childSmo,
            SourceCode.SmartObjects.Authoring.AssociationType associationType, string formName = null, string targetFolder = null)
        {
            // PERFORMANCE FIX: Reuses existing connection instead of creating new one

            // Retry logic for SmartObject retrieval (K2 server needs time to index after publish)
            string parentXml = await GetSmartObjectWithRetryAsync(parentSmo, "parent");
            SmartObjectDefinition parentDefinition = SmartObjectDefinition.Create(parentXml);

            string childXml = await GetAssociationSmartObjectWithRetryAsync(childSmo, "child");
            AssociationSmartObject childDefinition = AssociationSmartObject.Create(childXml);

            // SAFETY CHECK: Verify properties exist before accessing by index
            if (parentDefinition.Properties.Count < 1)
            {
                throw new Exception($"Parent SmartObject '{parentSmo}' does not have expected properties. Properties count: {parentDefinition.Properties.Count}");
            }
            if (childDefinition.Properties.Count < 2)
            {
                throw new Exception($"Child SmartObject '{childSmo}' does not have expected properties. Properties count: {childDefinition.Properties.Count}. Expected at least 2 properties (ID and ParentID).");
            }

            var parentIdProperty = childDefinition.Properties[1]; // ParentID
            var parentPrimaryKey = parentDefinition.Properties[0]; // ID

            parentDefinition.AddAssociation(childDefinition, parentIdProperty, parentPrimaryKey,
                associationType, $"{parentSmo} to {childSmo} association");
            // STRUCTURE: {TargetFolder}\{formName}\SmartObjects
            string category;
            if (string.IsNullOrEmpty(targetFolder))
            {
                category = string.IsNullOrEmpty(formName) ? "Generated SmartObjects" : $"{formName}\\SmartObjects";
            }
            else
            {
                category = string.IsNullOrEmpty(formName) ? $"{targetFolder}\\SmartObjects" : $"{targetFolder}\\{formName}\\SmartObjects";
            }
            parentDefinition.AddDeploymentCategory(category);
            parentDefinition.Build();

            return parentDefinition;
        }

        public void DeleteSmartObject(string smoName)
        {
            // PERFORMANCE FIX: Reuses existing connection instead of creating new one
            try
            {
                SmartObjectExplorer checkSmartObjectExist = _connectionManager.ManagementServer.GetSmartObjects(smoName);

                if (checkSmartObjectExist.SmartObjects.Count > 0)
                {
                    _connectionManager.ManagementServer.DeleteSmartObject(smoName, true);
                    if (_config.Logging.VerboseLogging)
                        Console.WriteLine($"  Deleted existing SmartObject: {smoName}");

                    // REMOVE FROM REGISTRY
                    SmartObjectViewRegistry.RemoveSmartObject(smoName);
                }
            }
            catch (Exception ex)
            {
                if (_config.Logging.VerboseLogging)
                    Console.WriteLine($"  Note: Could not delete SmartObject {smoName}: {ex.Message}");
            }
        }

        public bool ForceDeleteSmartObject(string smoName)
        {
            try
            {
                _connectionManager.ManagementServer.DeleteSmartObject(smoName, true);
                if (_config.Logging.VerboseLogging)
                    Console.WriteLine($"  Cleaned up SmartObject: {smoName}");

                // REMOVE FROM REGISTRY
                SmartObjectViewRegistry.RemoveSmartObject(smoName);

                return true;
            }
            catch (Exception ex1)
            {
                if (ex1.Message.Contains("does not exist"))
                {
                    if (_config.Logging.VerboseLogging)
                        Console.WriteLine($"  SmartObject {smoName} does not exist (nothing to clean up)");
                    return true;
                }

                if (_config.Logging.VerboseLogging)
                    Console.WriteLine($"  First delete attempt failed for {smoName}: {ex1.Message}");

                try
                {
                    var smoExplorer = _connectionManager.ManagementServer.GetSmartObjects(smoName);
                    if (smoExplorer.SmartObjects.Count > 0)
                    {
                        var smoGuid = smoExplorer.SmartObjects[0].Guid;
                        _connectionManager.ManagementServer.DeleteSmartObject(smoGuid, true);
                        if (_config.Logging.VerboseLogging)
                            Console.WriteLine($"  Cleaned up SmartObject by GUID: {smoName}");

                        // REMOVE FROM REGISTRY
                        SmartObjectViewRegistry.RemoveSmartObject(smoName);

                        return true;
                    }
                }
                catch (Exception ex2)
                {
                    if (_config.Logging.VerboseLogging)
                        Console.WriteLine($"  Second delete attempt failed for {smoName}: {ex2.Message}");
                }

                return false;
            }
        }

        private async Task PublishSmartObjectsAsync(SmartObjectDefinitionsPublish publishSmo)
        {
            // PERFORMANCE FIX: Reuses existing connection instead of creating new one
            try
            {
                _connectionManager.ManagementServer.PublishSmartObjects(publishSmo.ToPublishXml());
                if (_config.Logging.VerboseLogging)
                    Console.WriteLine("SmartObjects published successfully");
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to publish SmartObjects: {ex.Message}", ex);
            }
        }

        private string GetDataTypeString(PropertyDefinitionType type)
        {
            switch (type)
            {
                case PropertyDefinitionType.Autonumber:
                    return "autonumber";
                case PropertyDefinitionType.Number:
                    return "number";
                case PropertyDefinitionType.Text:
                    return "text";
                case PropertyDefinitionType.DateTime:
                    return "datetime";
                case PropertyDefinitionType.YesNo:
                    return "yesno";
                case PropertyDefinitionType.Decimal:
                    return "decimal";
                default:
                    return "text";
            }
        }

        public bool CheckSmartObjectExists(string smoName)
        {
            // CHECK REGISTRY FIRST
            if (SmartObjectViewRegistry.SmartObjectExists(smoName))
            {
                return true;
            }

            try
            {
                _connectionManager.Connect();
                SmartObjectExplorer explorer = _connectionManager.ManagementServer.GetSmartObjects(smoName);
                bool exists = explorer.SmartObjects.Count > 0;

                // If it exists on server but not in registry, add it
                if (exists && explorer.SmartObjects.Count > 0)
                {
                    var smo = explorer.SmartObjects[0];
                    SmartObjectViewRegistry.RegisterSmartObject(
                        smoName,
                        smo.Guid.ToString(),
                        SmartObjectViewRegistry.SmartObjectType.Main
                    );
                }

                return exists;
            }
            catch
            {
                return false;
            }
            finally
            {
                _connectionManager.Disconnect();
            }
        }

        public void ClearGuidCache()
        {
            _createdSmartObjectGuids.Clear();
            if (_config.Logging.VerboseLogging)
                Console.WriteLine("  Cleared SmartObject GUID cache");
        }

        /// <summary>
        /// Synchronous wrapper for GetSmartObjectGuidAsync - for backward compatibility
        /// </summary>
        public string GetSmartObjectGuid(string smoName)
        {
            return GetSmartObjectGuidAsync(smoName).GetAwaiter().GetResult();
        }

        public async Task<string> GetSmartObjectGuidAsync(string smoName)
        {
            // CHECK REGISTRY FIRST
            var smoInfo = SmartObjectViewRegistry.GetSmartObjectInfo(smoName);
            if (smoInfo != null && !string.IsNullOrEmpty(smoInfo.Guid))
            {
                if (_config.Logging.VerboseLogging)
                    Console.WriteLine($"  Retrieved GUID from registry for {smoName}: {smoInfo.Guid}");
                return smoInfo.Guid;
            }

            // First check if we created this SmartObject in this session
            if (_createdSmartObjectGuids.ContainsKey(smoName))
            {
                if (_config.Logging.VerboseLogging)
                    Console.WriteLine($"  Retrieved GUID from cache for {smoName}: {_createdSmartObjectGuids[smoName]}");
                return _createdSmartObjectGuids[smoName];
            }

            // Otherwise, try to retrieve it from the server with retry logic
            int retryCount = 3;
            Exception lastException = null;

            for (int i = 0; i < retryCount; i++)
            {
                try
                {
                    if (i > 0)
                    {
                        await Task.Delay(500); // Wait before retry
                    }

                    _connectionManager.Connect();

                    // Try different methods to get the SmartObject
                    SmartObjectExplorer explorer = _connectionManager.ManagementServer.GetSmartObjects(smoName);
                    if (explorer.SmartObjects.Count > 0)
                    {
                        string guid = explorer.SmartObjects[0].Guid.ToString();
                        _createdSmartObjectGuids[smoName] = guid;

                        // REGISTER WITH REGISTRY
                        SmartObjectViewRegistry.RegisterSmartObject(
                            smoName,
                            guid,
                            SmartObjectViewRegistry.SmartObjectType.Main
                        );

                        if (_config.Logging.VerboseLogging)
                            Console.WriteLine($"  Retrieved GUID from server for {smoName}: {guid}");
                        return guid;
                    }

                    // If that didn't work, try getting the definition
                    string smoXml = _connectionManager.ManagementServer.GetSmartObjectDefinition(smoName);
                    SmartObjectDefinition smoDef = SmartObjectDefinition.Create(smoXml);
                    string defGuid = smoDef.Guid.ToString();
                    _createdSmartObjectGuids[smoName] = defGuid;

                    // REGISTER WITH REGISTRY
                    SmartObjectViewRegistry.RegisterSmartObject(
                        smoName,
                        defGuid,
                        SmartObjectViewRegistry.SmartObjectType.Main
                    );

                    if (_config.Logging.VerboseLogging)
                        Console.WriteLine($"  Retrieved GUID from definition for {smoName}: {defGuid}");
                    return defGuid;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    if (_config.Logging.VerboseLogging)
                        Console.WriteLine($"  Attempt {i + 1} failed to retrieve GUID for {smoName}: {ex.Message}");
                }
                finally
                {
                    _connectionManager.Disconnect();
                }
            }

            Console.WriteLine($"  ERROR: Could not retrieve GUID for SmartObject {smoName} after {retryCount} attempts");
            throw new InvalidOperationException($"Cannot retrieve GUID for SmartObject '{smoName}'", lastException);
        }
    }
}