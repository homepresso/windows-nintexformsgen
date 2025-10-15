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
            _smoFieldMappings = new Dictionary<string, Dictionary<string, FieldInfo>>();
        }

        public Dictionary<string, Dictionary<string, FieldInfo>> FieldMappings => _smoFieldMappings;
        public Dictionary<string, string> CreatedSmartObjectGuids => _createdSmartObjectGuids;

        public void GenerateSmartObjectsFromJson(string jsonContent)
        {
            JObject formData = JObject.Parse(jsonContent);
            string formDisplayName = formData.Properties().First().Name; // Keep original name with spaces
            string formName = formDisplayName.Replace(" ", "_"); // Use underscores for object names
            JObject formDefinition = formData[formDisplayName] as JObject;

            Console.WriteLine($"\nProcessing form: {formName}");

            JArray dataArray = formDefinition["FormDefinition"]["Data"] as JArray;
            if (dataArray == null)
            {
                Console.WriteLine("No data columns found in JSON");
                return;
            }

            // Generate lookup SmartObjects first
            GenerateLookupSmartObjects(formName, formDisplayName, dataArray);

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
            Console.WriteLine($"\nCreating main SmartObject: {formName}");
            SmartObjectDefinition mainSmo = CreateMainSmartObject(formName, mainFields, formDisplayName);
            publishSmo.SmartObjects.Add(mainSmo);

            // Create child SmartObjects for repeating sections
            foreach (var section in repeatingSections)
            {
                string childSmoName = $"{formName}_{section.Key.Replace(" ", "_")}";
                Console.WriteLine($"Creating child SmartObject: {childSmoName}");
                SmartObjectDefinition childSmo = CreateChildSmartObject(childSmoName, section.Value, formName, formDisplayName);
                publishSmo.SmartObjects.Add(childSmo);
            }

            // Publish SmartObjects first
            Console.WriteLine("\nPublishing SmartObjects...");
            PublishSmartObjects(publishSmo);

            // Create associations
            if (repeatingSections.Count > 0)
            {
                Console.WriteLine("\nCreating associations...");
                publishSmo.Dispose();
                publishSmo = new SmartObjectDefinitionsPublish();

                foreach (var section in repeatingSections)
                {
                    string childSmoName = $"{formName}_{section.Key.Replace(" ", "_")}";
                    Console.WriteLine($"Creating association: {formName} -> {childSmoName}");
                    SmartObjectDefinition association = CreateAssociation(formName, childSmoName,
                        SourceCode.SmartObjects.Authoring.AssociationType.OneToMany, formDisplayName);
                    publishSmo.SmartObjects.Add(association);
                }

                // Publish associations
                PublishSmartObjects(publishSmo);
            }
        }

        private void GenerateLookupSmartObjects(string formName, string formDisplayName, JArray dataArray)
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
            Console.WriteLine($"Creating consolidated lookup SmartObject: {consolidatedLookupName}");

            SmartObjectDefinitionsPublish publishSmo = new SmartObjectDefinitionsPublish();
            SmartObjectDefinition lookupSmo = CreateConsolidatedLookupSmartObject(consolidatedLookupName, formDisplayName);
            publishSmo.SmartObjects.Add(lookupSmo);

            // Store the GUID for later reference
            string lookupGuid = lookupSmo.Guid.ToString();
            _createdSmartObjectGuids[consolidatedLookupName] = lookupGuid;

            Console.WriteLine($"Publishing consolidated lookup SmartObject...");
            PublishSmartObjects(publishSmo);

            // Add a small delay to ensure server synchronization
            System.Threading.Thread.Sleep(1000);

            // Populate the consolidated lookup with all data
            Console.WriteLine($"Populating consolidated lookup SmartObject with data...");
            PopulateConsolidatedLookupData(consolidatedLookupName, allLookupData);
        }

        private SmartObjectDefinition CreateConsolidatedLookupSmartObject(string smoName, string formName = null)
        {
            try
            {
                DeleteSmartObject(smoName);

                _connectionManager.Connect();

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
                // Use configured target folder (user can customize this in the UI)
                string category = string.IsNullOrEmpty(formName) ? $"{_config.Form.TargetFolder}\\Lookups" : $"{_config.Form.TargetFolder}\\{formName}\\Lookups";
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

                Console.WriteLine($"  Created Consolidated Lookup SmartObject {smoName} with GUID: {smoGuid}");

                return smoDefinition;
            }
            finally
            {
                _connectionManager.Disconnect();
            }
        }

        private SmartObjectDefinition CreateMainSmartObject(string smoName, List<JObject> fields, string formName = null)
        {
            try
            {
                DeleteSmartObject(smoName);

                _connectionManager.Connect();

                ServiceInstance serviceInstance = ServiceInstance.Create(
                    _connectionManager.ManagementServer.GetServiceInstanceForExtend(new Guid(_config.K2.SmartBoxGuid), string.Empty));
                ExtendObject extendObject = serviceInstance.GetCreateExtender();

                extendObject.Name = smoName;
                extendObject.Metadata.DisplayName = smoName.Replace("_", " ");

                _smoFieldMappings[smoName] = new Dictionary<string, FieldInfo>();

                HashSet<string> usedPropertyNames = new HashSet<string>();

                // Add ID property
                ExtendObjectProperty idProperty = new ExtendObjectProperty();
                idProperty.Name = "ID";
                idProperty.Metadata.DisplayName = "ID";
                idProperty.Type = PropertyDefinitionType.Autonumber;
                idProperty.ExtendType = ExtendPropertyType.UniqueIdAuto;
                extendObject.Properties.Add(idProperty);
                usedPropertyNames.Add("ID");

                _smoFieldMappings[smoName]["ID"] = new FieldInfo
                {
                    FieldGuid = Guid.NewGuid().ToString(),
                    FieldName = "ID",
                    DisplayName = "ID",
                    DataType = "autonumber"
                };

                // Add properties from JSON
                foreach (var field in fields)
                {
                    ExtendObjectProperty property = CreatePropertyFromJson(field, usedPropertyNames);
                    if (property != null)
                    {
                        extendObject.Properties.Add(property);
                        Console.WriteLine($"  Added property: {property.Name} ({property.Type})");

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
                // Use configured target folder (user can customize this in the UI)
                string category = string.IsNullOrEmpty(formName) ? $"{_config.Form.TargetFolder}\\SmartObjects" : $"{_config.Form.TargetFolder}\\{formName}\\SmartObjects";
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

                Console.WriteLine($"  Created SmartObject {smoName} with GUID: {smoGuid}");

                return smoDefinition;
            }
            finally
            {
                _connectionManager.Disconnect();
            }
        }

        private SmartObjectDefinition CreateChildSmartObject(string smoName, List<JObject> fields, string parentSmoName, string formName = null)
        {
            try
            {
                DeleteSmartObject(smoName);

                _connectionManager.Connect();

                ServiceInstance serviceInstance = ServiceInstance.Create(
                    _connectionManager.ManagementServer.GetServiceInstanceForExtend(new Guid(_config.K2.SmartBoxGuid), string.Empty));
                ExtendObject extendObject = serviceInstance.GetCreateExtender();

                extendObject.Name = smoName;
                extendObject.Metadata.DisplayName = smoName.Replace("_", " ");

                _smoFieldMappings[smoName] = new Dictionary<string, FieldInfo>();

                HashSet<string> usedPropertyNames = new HashSet<string>();

                // Add ID property
                ExtendObjectProperty idProperty = new ExtendObjectProperty();
                idProperty.Name = "ID";
                idProperty.Metadata.DisplayName = "ID";
                idProperty.Type = PropertyDefinitionType.Autonumber;
                idProperty.ExtendType = ExtendPropertyType.UniqueIdAuto;
                extendObject.Properties.Add(idProperty);
                usedPropertyNames.Add("ID");

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
                    ExtendObjectProperty property = CreatePropertyFromJson(field, usedPropertyNames);
                    if (property != null)
                    {
                        extendObject.Properties.Add(property);
                        Console.WriteLine($"  Added property: {property.Name} ({property.Type})");

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
                // Use configured target folder (user can customize this in the UI)
                string category = string.IsNullOrEmpty(formName) ? $"{_config.Form.TargetFolder}\\SmartObjects" : $"{_config.Form.TargetFolder}\\{formName}\\SmartObjects";
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

                Console.WriteLine($"  Created Child SmartObject {smoName} with GUID: {smoGuid}");
                Console.WriteLine($"  Parent SmartObject: {parentSmoName}");

                return smoDefinition;
            }
            finally
            {
                _connectionManager.Disconnect();
            }
        }

        private void PopulateConsolidatedLookupData(string smoName, Dictionary<string, JArray> allLookupData)
        {
            SmartObjectClientServer smoClient = null;
            try
            {
                System.Threading.Thread.Sleep(500);

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
                        Console.WriteLine($"    Added to {lookupType}: '{displayText}' = '{itemValue}' (Order: {order}, Default: {isDefault})");
                        order++;
                    }
                }

                Console.WriteLine($"  Successfully populated consolidated lookup with data for {allLookupData.Count} fields");
            }
            catch (Exception ex)
            {
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

     
        private ExtendObjectProperty CreatePropertyFromJson(JObject field, HashSet<string> usedPropertyNames)
        {
            string columnName = field["ColumnName"]?.Value<string>();
            string type = field["Type"]?.Value<string>();
            string displayName = field["DisplayName"]?.Value<string>();

            if (string.IsNullOrEmpty(columnName))
                return null;

            string sanitizedName = NameSanitizer.SanitizePropertyName(columnName);

            if (usedPropertyNames.Contains(sanitizedName))
            {
                Console.WriteLine($"    Warning: Duplicate property name '{sanitizedName}' found. Skipping field.");
                return null;
            }

            usedPropertyNames.Add(sanitizedName);

            ExtendObjectProperty property = new ExtendObjectProperty();
            property.Name = sanitizedName;

            if (!string.IsNullOrEmpty(displayName))
            {
                property.Metadata.DisplayName = displayName.Replace("_", " ");
            }
            else
            {
                property.Metadata.DisplayName = columnName.Replace("_", " ");
            }

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

        private SmartObjectDefinition CreateAssociation(string parentSmo, string childSmo,
            SourceCode.SmartObjects.Authoring.AssociationType associationType, string formName = null)
        {
            try
            {
                _connectionManager.Connect();

                string parentXml = _connectionManager.ManagementServer.GetSmartObjectDefinition(parentSmo);
                SmartObjectDefinition parentDefinition = SmartObjectDefinition.Create(parentXml);

                string childXml = _connectionManager.ManagementServer.GetAssociationSmartObject(childSmo);
                AssociationSmartObject childDefinition = AssociationSmartObject.Create(childXml);

                var parentIdProperty = childDefinition.Properties[1]; // ParentID
                var parentPrimaryKey = parentDefinition.Properties[0]; // ID

                parentDefinition.AddAssociation(childDefinition, parentIdProperty, parentPrimaryKey,
                    associationType, $"{parentSmo} to {childSmo} association");
                // Use configured target folder (user can customize this in the UI)
                string category = string.IsNullOrEmpty(formName) ? $"{_config.Form.TargetFolder}\\SmartObjects" : $"{_config.Form.TargetFolder}\\{formName}\\SmartObjects";
                parentDefinition.AddDeploymentCategory(category);
                parentDefinition.Build();

                return parentDefinition;
            }
            finally
            {
                _connectionManager.Disconnect();
            }
        }

        public void DeleteSmartObject(string smoName)
        {
            try
            {
                _connectionManager.Connect();
                SmartObjectExplorer checkSmartObjectExist = _connectionManager.ManagementServer.GetSmartObjects(smoName);

                if (checkSmartObjectExist.SmartObjects.Count > 0)
                {
                    _connectionManager.ManagementServer.DeleteSmartObject(smoName, true);
                    Console.WriteLine($"  Deleted existing SmartObject: {smoName}");

                    // REMOVE FROM REGISTRY
                    SmartObjectViewRegistry.RemoveSmartObject(smoName);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Note: Could not delete SmartObject {smoName}: {ex.Message}");
            }
            finally
            {
                _connectionManager.Disconnect();
            }
        }

        public bool ForceDeleteSmartObject(string smoName)
        {
            try
            {
                _connectionManager.ManagementServer.DeleteSmartObject(smoName, true);
                Console.WriteLine($"  Cleaned up SmartObject: {smoName}");

                // REMOVE FROM REGISTRY
                SmartObjectViewRegistry.RemoveSmartObject(smoName);

                return true;
            }
            catch (Exception ex1)
            {
                if (ex1.Message.Contains("does not exist"))
                {
                    Console.WriteLine($"  SmartObject {smoName} does not exist (nothing to clean up)");
                    return true;
                }

                Console.WriteLine($"  First delete attempt failed for {smoName}: {ex1.Message}");

                try
                {
                    var smoExplorer = _connectionManager.ManagementServer.GetSmartObjects(smoName);
                    if (smoExplorer.SmartObjects.Count > 0)
                    {
                        var smoGuid = smoExplorer.SmartObjects[0].Guid;
                        _connectionManager.ManagementServer.DeleteSmartObject(smoGuid, true);
                        Console.WriteLine($"  Cleaned up SmartObject by GUID: {smoName}");

                        // REMOVE FROM REGISTRY
                        SmartObjectViewRegistry.RemoveSmartObject(smoName);

                        return true;
                    }
                }
                catch (Exception ex2)
                {
                    Console.WriteLine($"  Second delete attempt failed for {smoName}: {ex2.Message}");
                }

                return false;
            }
        }

        private void PublishSmartObjects(SmartObjectDefinitionsPublish publishSmo)
        {
            try
            {
                _connectionManager.Connect();
                _connectionManager.ManagementServer.PublishSmartObjects(publishSmo.ToPublishXml());
                Console.WriteLine("SmartObjects published successfully");
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to publish SmartObjects: {ex.Message}", ex);
            }
            finally
            {
                _connectionManager.Disconnect();
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
            Console.WriteLine("  Cleared SmartObject GUID cache");
        }

        public string GetSmartObjectGuid(string smoName)
        {
            // CHECK REGISTRY FIRST
            var smoInfo = SmartObjectViewRegistry.GetSmartObjectInfo(smoName);
            if (smoInfo != null && !string.IsNullOrEmpty(smoInfo.Guid))
            {
                Console.WriteLine($"  Retrieved GUID from registry for {smoName}: {smoInfo.Guid}");
                return smoInfo.Guid;
            }

            // First check if we created this SmartObject in this session
            if (_createdSmartObjectGuids.ContainsKey(smoName))
            {
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
                        System.Threading.Thread.Sleep(500); // Wait before retry
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

                    Console.WriteLine($"  Retrieved GUID from definition for {smoName}: {defGuid}");
                    return defGuid;
                }
                catch (Exception ex)
                {
                    lastException = ex;
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