using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using K2SmartObjectGenerator;
using K2SmartObjectGenerator.Config;
using K2SmartObjectGenerator.Utilities;
using FormGenerator.Analyzers.Infopath;
using FormGenerator.Analyzers.InfoPath;

namespace FormGenerator.Services
{
    /// <summary>
    /// Service wrapper for K2 SmartObject, View, and Form generation
    /// Provides async interface and UI-friendly status reporting
    /// </summary>
    public class K2GenerationService
    {
        public event EventHandler<string>? StatusUpdate;
        public event EventHandler<K2GenerationProgress>? ProgressUpdate;

        private ServerConnectionManager? _connectionManager;
        private GeneratorConfiguration? _config;

        public K2GenerationService()
        {
        }

        /// <summary>
        /// Tests connection to K2 server
        /// </summary>
        public async Task<K2ConnectionResult> TestConnectionAsync(string server, uint port, string? username = null, string? password = null)
        {
            return await Task.Run(() =>
            {
                ServerConnectionManager? connectionManager = null;

                try
                {
                    OnStatusUpdate("Testing connection to K2 server...");
                    OnStatusUpdate($"Server: {server}, Port: {port}");

                    // Create connection manager
                    OnStatusUpdate("Creating connection manager...");
                    connectionManager = new ServerConnectionManager(server, port);

                    // Try to connect to SmartObject Management server
                    OnStatusUpdate("Attempting to connect to K2 SmartObject Management Server...");
                    connectionManager.Connect();
                    OnStatusUpdate("Connect() method completed.");

                    // Verify we have a valid management server
                    OnStatusUpdate("Verifying management server connection...");
                    var smoServer = connectionManager.ManagementServer;

                    if (smoServer == null)
                    {
                        OnStatusUpdate("ERROR: Management server is null");
                        return new K2ConnectionResult
                        {
                            Success = false,
                            Message = "Failed to connect to K2 SmartObject Management Server - server object is null",
                            ServerVersion = "Unknown"
                        };
                    }

                    if (smoServer.Connection == null)
                    {
                        OnStatusUpdate("ERROR: Management server connection is null");
                        return new K2ConnectionResult
                        {
                            Success = false,
                            Message = "Failed to connect to K2 SmartObject Management Server - connection is null",
                            ServerVersion = "Unknown"
                        };
                    }

                    if (!smoServer.Connection.IsConnected)
                    {
                        OnStatusUpdate("ERROR: Management server is not connected");
                        return new K2ConnectionResult
                        {
                            Success = false,
                            Message = "Failed to connect to K2 SmartObject Management Server - connection not established",
                            ServerVersion = "Unknown"
                        };
                    }

                    OnStatusUpdate("Connection verified successfully!");

                    // Store the connection for later use
                    _connectionManager = connectionManager;

                    return new K2ConnectionResult
                    {
                        Success = true,
                        Message = "Successfully connected to K2 server",
                        ServerVersion = "K2 Five"
                    };
                }
                catch (System.Reflection.TargetInvocationException ex)
                {
                    var innerMessage = ex.InnerException?.Message ?? ex.Message;
                    var innerDetails = ex.InnerException?.ToString() ?? ex.ToString();
                    OnStatusUpdate($"ERROR: TargetInvocationException - {innerMessage}");

                    // Cleanup on error
                    if (connectionManager != null)
                    {
                        try { connectionManager.Disconnect(); } catch { }
                    }

                    return new K2ConnectionResult
                    {
                        Success = false,
                        Message = $"Connection failed - Method invocation error: {innerMessage}",
                        ErrorDetails = innerDetails,
                        ServerVersion = "Unknown"
                    };
                }
                catch (MissingMethodException ex)
                {
                    OnStatusUpdate($"ERROR: MissingMethodException - {ex.Message}");

                    // Cleanup on error
                    if (connectionManager != null)
                    {
                        try { connectionManager.Disconnect(); } catch { }
                    }

                    return new K2ConnectionResult
                    {
                        Success = false,
                        Message = $"Connection failed - K2 API method not found: {ex.Message}",
                        ErrorDetails = $"This may indicate a K2 DLL version mismatch. Ensure you have the correct K2 libraries installed.\n\n{ex.ToString()}",
                        ServerVersion = "Unknown"
                    };
                }
                catch (Exception ex)
                {
                    OnStatusUpdate($"ERROR: {ex.GetType().Name} - {ex.Message}");

                    // Cleanup on error
                    if (connectionManager != null)
                    {
                        try { connectionManager.Disconnect(); } catch { }
                    }

                    return new K2ConnectionResult
                    {
                        Success = false,
                        Message = $"Connection failed: {ex.Message}",
                        ErrorDetails = $"Exception Type: {ex.GetType().FullName}\n\nMessage: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}\n\nFull Details:\n{ex.ToString()}",
                        ServerVersion = "Unknown"
                    };
                }
            });
        }

        /// <summary>
        /// Generates K2 artifacts (SmartObjects, Views, Forms) from analyzed form data
        /// </summary>
        public async Task<K2GenerationResult> GenerateK2ArtifactsAsync(K2GenerationRequest request)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var result = new K2GenerationResult
                    {
                        Message = string.Empty,
                        FormName = string.Empty,
                        GeneratedArtifacts = new Dictionary<string, int>()
                    };

                    OnStatusUpdate("==============================================");
                    OnStatusUpdate("=== K2 GENERATION DIAGNOSTICS - START ===");
                    OnStatusUpdate("==============================================");

                    // 1. Setup configuration
                    OnStatusUpdate("Setting up K2 generation configuration...");
                    _config = CreateConfiguration(request);
                    OnStatusUpdate($"Target Folder configured: {_config.Form.TargetFolder}");
                    OnStatusUpdate($"[DIAG] Target Folder: {_config.Form.TargetFolder}");
                    OnStatusUpdate($"[DIAG] Form Theme: {_config.Form.Theme}");
                    OnStatusUpdate($"[DIAG] Force Cleanup: {_config.Form.ForceCleanup}");

                    // 2. Create fresh connection for EACH generation
                    // IMPORTANT: K2 connection must be fresh to avoid stale state issues
                    OnStatusUpdate("Establishing fresh connection to K2 server...");
                    OnStatusUpdate($"[DIAG] Connection Manager Before Disconnect: {(_connectionManager != null ? "EXISTS" : "NULL")}");

                    // Disconnect and completely null out existing connection to force fresh state
                    if (_connectionManager != null)
                    {
                        try
                        {
                            OnStatusUpdate("[DIAG] Disconnecting existing connection...");
                            _connectionManager.Disconnect();
                            OnStatusUpdate("[DIAG] Existing connection disconnected successfully");
                        }
                        catch (Exception disconnectEx)
                        {
                            OnStatusUpdate($"[DIAG] Disconnect warning: {disconnectEx.Message}");
                        }

                        // Null out to force garbage collection and truly fresh state
                        _connectionManager = null;
                        OnStatusUpdate("[DIAG] Connection manager nulled out for fresh start");
                    }

                    // Create completely new connection with fresh instance
                    OnStatusUpdate($"[DIAG] Creating new connection to: {request.ServerName}:{request.ServerPort}");
                    _connectionManager = new ServerConnectionManager(request.ServerName, request.ServerPort);
                    _connectionManager.Connect(); // Explicitly connect
                    OnStatusUpdate("[DIAG] New connection manager created and connected");
                    OnStatusUpdate("K2 connection established");

                    // 3. Clear registry for fresh generation
                    OnStatusUpdate("Initializing generation session...");
                    OnStatusUpdate($"[DIAG] Registry counts BEFORE Clear - SmartObjects: {SmartObjectViewRegistry.GetSmartObjectCount()}, Views: {SmartObjectViewRegistry.GetViewCount()}, Forms: {SmartObjectViewRegistry.GetFormCount()}");
                    SmartObjectViewRegistry.Clear();
                    OnStatusUpdate($"[DIAG] Registry counts AFTER Clear - SmartObjects: {SmartObjectViewRegistry.GetSmartObjectCount()}, Views: {SmartObjectViewRegistry.GetViewCount()}, Forms: {SmartObjectViewRegistry.GetFormCount()}");

                    // 4. Convert form definitions to JSON
                    OnStatusUpdate("Preparing form definitions for K2 generation...");
                    string jsonContent = ConvertFormDefinitionsToJson(request.FormDefinitions);

                    // 4.5. Extract form metadata for later use
                    JObject formData = JObject.Parse(jsonContent);
                    string formName = formData.Properties().First().Name.Replace(" ", "_");
                    string formDisplayName = formData.Properties().First().Name;
                    string targetCategory = $"{_config.Form.TargetFolder}\\{formDisplayName}";

                    OnStatusUpdate($"Target category: {targetCategory}");
                    OnStatusUpdate($"[DIAG] Form Name: {formName}");
                    OnStatusUpdate($"[DIAG] Form Display Name: {formDisplayName}");
                    OnStatusUpdate($"[DIAG] Target Category: {targetCategory}");

                    // 5. Optional cleanup
                    if (request.ForceCleanup)
                    {
                        OnStatusUpdate("Cleaning up existing K2 objects...");
                        OnProgressUpdate(new K2GenerationProgress { Stage = "Cleanup", PercentComplete = 10 });
                        OnStatusUpdate("[DIAG] Performing cleanup...");
                        PerformCleanup(jsonContent);
                        OnStatusUpdate("[DIAG] Cleanup completed");
                    }

                    // 6. Generate SmartObjects
                    OnStatusUpdate("Generating K2 SmartObjects...");
                    OnProgressUpdate(new K2GenerationProgress { Stage = "SmartObjects", PercentComplete = 25 });
                    OnStatusUpdate("[DIAG] Starting SmartObject generation...");

                    var smoGenerator = new SmartObjectGenerator(_connectionManager, _config);
                    smoGenerator.GenerateSmartObjectsFromJson(jsonContent);

                    result.SmartObjectsCreated = SmartObjectViewRegistry.GetSmartObjectCount();
                    OnStatusUpdate($"Created {result.SmartObjectsCreated} SmartObjects");
                    OnStatusUpdate($"[DIAG] SmartObjects created: {result.SmartObjectsCreated}");

                    // 7. Generate Views
                    OnStatusUpdate("Generating K2 Views...");
                    OnProgressUpdate(new K2GenerationProgress { Stage = "Views", PercentComplete = 50 });
                    OnStatusUpdate("[DIAG] Starting View generation...");
                    OnStatusUpdate($"[DIAG] ViewTitles count before generation: {0}");

                    var viewGenerator = new ViewGenerator(_connectionManager, smoGenerator.FieldMappings, smoGenerator, _config);
                    viewGenerator.GenerateViewsFromJson(jsonContent);

                    result.ViewsCreated = SmartObjectViewRegistry.GetViewCount();
                    OnStatusUpdate($"Created {result.ViewsCreated} Views");
                    OnStatusUpdate($"[DIAG] Views created: {result.ViewsCreated}");
                    OnStatusUpdate($"[DIAG] ViewTitles count after generation: {viewGenerator.ViewTitles.Count}");

                    // 8. Generate Forms with verification and retry
                    OnStatusUpdate("Generating K2 Forms...");
                    OnProgressUpdate(new K2GenerationProgress { Stage = "Forms", PercentComplete = 75 });
                    OnStatusUpdate("[DIAG] Starting Form generation...");
                    OnStatusUpdate($"[DIAG] Passing {viewGenerator.ViewTitles.Count} view titles to form generator");

                    var formGenerator = new K2SmartObjectGenerator.FormGenerator(_connectionManager, request.FormTheme, smoGenerator, _config);

                    // Form name was already extracted earlier for category creation
                    formGenerator.GenerateFormsFromJson(jsonContent, viewGenerator.ViewTitles);
                    OnStatusUpdate("[DIAG] Form generation completed");

                    // 9. Register everything
                    OnStatusUpdate("[DIAG] Starting form registration...");
                    var allSmartObjects = new List<string> { formName };
                    allSmartObjects.AddRange(SmartObjectViewRegistry.GetChildSmartObjects(formName));

                    var lookupSmo = $"{formName}_Lookups";
                    if (SmartObjectViewRegistry.SmartObjectExists(lookupSmo))
                    {
                        allSmartObjects.Add(lookupSmo);
                        OnStatusUpdate($"[DIAG] Added lookup SmartObject: {lookupSmo}");
                    }

                    OnStatusUpdate($"[DIAG] Total SmartObjects to register: {allSmartObjects.Count}");
                    foreach (var smo in allSmartObjects)
                    {
                        OnStatusUpdate($"[DIAG]   - SmartObject: {smo}");
                    }

                    var formViews = new List<string>();
                    foreach (var smo in allSmartObjects)
                    {
                        var views = SmartObjectViewRegistry.GetViewsForSmartObject(smo);
                        formViews.AddRange(views);
                        OnStatusUpdate($"[DIAG] SmartObject '{smo}' has {views.Count} views");
                    }

                    OnStatusUpdate($"[DIAG] Total form views: {formViews.Count}");
                    foreach (var view in formViews)
                    {
                        OnStatusUpdate($"[DIAG]   - View: {view}");
                    }

                    SmartObjectViewRegistry.RegisterForm(formName, formViews, allSmartObjects);
                    OnStatusUpdate($"[DIAG] Form '{formName}' registered in registry");

                    result.FormsCreated = SmartObjectViewRegistry.GetFormCount();
                    OnStatusUpdate($"Created {result.FormsCreated} Forms");
                    OnStatusUpdate($"[DIAG] Registry reports {result.FormsCreated} forms created");

                    // 10. Generate summary
                    OnStatusUpdate("K2 generation completed successfully!");
                    OnProgressUpdate(new K2GenerationProgress { Stage = "Complete", PercentComplete = 100 });

                    result.Success = true;
                    result.Message = "K2 artifacts generated successfully";
                    result.FormName = formName;
                    result.GeneratedArtifacts = GetGeneratedArtifactsSummary();

                    OnStatusUpdate("==============================================");
                    OnStatusUpdate("=== K2 GENERATION DIAGNOSTICS - END ===");
                    OnStatusUpdate($"=== SUCCESS: {result.FormsCreated} forms created ===");
                    OnStatusUpdate("==============================================");

                    // Clear K2 connection and force garbage collection to reset any cached state
                    // This ensures the next generation starts with a completely clean slate
                    OnStatusUpdate("[DIAG] Clearing K2 connection and cache...");
                    if (_connectionManager != null)
                    {
                        try
                        {
                            _connectionManager.Disconnect();
                            _connectionManager = null;
                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                            GC.Collect();
                            OnStatusUpdate("[DIAG] K2 connection cleared and garbage collected");
                        }
                        catch (Exception cleanupEx)
                        {
                            OnStatusUpdate($"[DIAG] Cleanup warning: {cleanupEx.Message}");
                        }
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    OnStatusUpdate($"K2 generation failed: {ex.Message}");
                    return new K2GenerationResult
                    {
                        Success = false,
                        Message = $"Generation failed: {ex.Message}",
                        ErrorDetails = ex.ToString(),
                        FormName = string.Empty,
                        GeneratedArtifacts = new Dictionary<string, int>()
                    };
                }
            });
        }

        /// <summary>
        /// Disposes the K2 server connection
        /// </summary>
        public void Dispose()
        {
            _connectionManager?.Disconnect();
            _connectionManager = null;
        }

        private GeneratorConfiguration CreateConfiguration(K2GenerationRequest request)
        {
            var config = new GeneratorConfiguration();

            config.Server.HostName = request.ServerName;
            config.Server.Port = request.ServerPort;
            config.Form.Theme = request.FormTheme;
            config.Form.UseTimestamp = request.UseTimestamp;
            config.Form.ForceCleanup = request.ForceCleanup;
            config.Form.TargetFolder = request.TargetFolder ?? "TestForms";
            config.K2.SmartBoxGuid = request.SmartBoxGuid ?? "e5609413-d844-4325-98c3-db3cacbd406d";

            return config;
        }

        private string ConvertFormDefinitionsToJson(Dictionary<string, Core.Models.FormAnalysisResult> formDefinitions)
        {
            // Convert the form definitions to the format expected by K2 generators
            // This uses the existing ToEnhancedJson functionality
            var combinedJson = new JObject();

            foreach (var kvp in formDefinitions)
            {
                var formDef = kvp.Value.FormDefinition as InfoPathFormDefinition;
                if (formDef != null)
                {
                    var formObject = formDef.ToEnhancedJson();
                    var formName = formDef.FormName.Replace(" ", "_");

                    // Convert the anonymous object to JObject and wrap it in FormDefinition structure
                    var formJsonString = JsonConvert.SerializeObject(formObject);
                    var formData = JObject.Parse(formJsonString);

                    // Wrap the form data in the expected structure
                    var wrappedFormData = new JObject();
                    wrappedFormData["FormDefinition"] = formData;

                    combinedJson[formName] = wrappedFormData;
                }
            }

            return combinedJson.ToString();
        }

        private void PerformCleanup(string jsonContent)
        {
            try
            {
                if (_connectionManager == null || _config == null)
                    return;

                JObject formData = JObject.Parse(jsonContent);
                string formName = formData.Properties().First().Name.Replace(" ", "_");
                JObject? formDefinition = formData[formData.Properties().First().Name] as JObject;

                // Clean in reverse dependency order: Forms → Views → SmartObjects
                var smoGenerator = new SmartObjectGenerator(_connectionManager, _config);
                var formGenerator = new K2SmartObjectGenerator.FormGenerator(_connectionManager, _config.Form.Theme, smoGenerator, _config);
                var viewGenerator = new ViewGenerator(_connectionManager, smoGenerator.FieldMappings, smoGenerator, _config);

                formGenerator.TryCleanupExistingForms(jsonContent);
                viewGenerator.TryCleanupExistingViews(jsonContent);

                // Cleanup SmartObjects
                JArray? dataArray = formDefinition?["FormDefinition"]?["Data"] as JArray;
                if (dataArray != null)
                {
                    var repeatingSections = new HashSet<string>();

                    foreach (JObject dataItem in dataArray)
                    {
                        bool isRepeating = dataItem["IsRepeating"]?.Value<bool>() ?? false;
                        string? sectionName = dataItem["RepeatingSectionName"]?.Value<string>();

                        if (isRepeating && !string.IsNullOrEmpty(sectionName))
                        {
                            repeatingSections.Add(sectionName);
                        }
                    }

                    smoGenerator.ForceDeleteSmartObject($"{formName}_Lookups");

                    foreach (var section in repeatingSections)
                    {
                        smoGenerator.ForceDeleteSmartObject($"{formName}_{section.Replace(" ", "_")}");
                    }

                    smoGenerator.ForceDeleteSmartObject(formName);
                }
            }
            catch (Exception ex)
            {
                OnStatusUpdate($"Cleanup warning: {ex.Message}");
            }
        }

        private Dictionary<string, int> GetGeneratedArtifactsSummary()
        {
            return new Dictionary<string, int>
            {
                { "SmartObjects", SmartObjectViewRegistry.GetSmartObjectCount() },
                { "MainSmartObjects", SmartObjectViewRegistry.GetSmartObjectCount(SmartObjectViewRegistry.SmartObjectType.Main) },
                { "ChildSmartObjects", SmartObjectViewRegistry.GetSmartObjectCount(SmartObjectViewRegistry.SmartObjectType.Child) },
                { "LookupSmartObjects", SmartObjectViewRegistry.GetSmartObjectCount(SmartObjectViewRegistry.SmartObjectType.Lookup) },
                { "Views", SmartObjectViewRegistry.GetViewCount() },
                { "CaptureViews", SmartObjectViewRegistry.GetViewCount(SmartObjectViewRegistry.ViewType.Capture) },
                { "ItemViews", SmartObjectViewRegistry.GetViewCount(SmartObjectViewRegistry.ViewType.Item) },
                { "ListViews", SmartObjectViewRegistry.GetViewCount(SmartObjectViewRegistry.ViewType.List) },
                { "Forms", SmartObjectViewRegistry.GetFormCount() }
            };
        }

        private void OnStatusUpdate(string status)
        {
            StatusUpdate?.Invoke(this, status);
        }

        private void OnProgressUpdate(K2GenerationProgress progress)
        {
            ProgressUpdate?.Invoke(this, progress);
        }
    }

    #region Request/Response Models

    public class K2GenerationRequest
    {
        public required string ServerName { get; set; }
        public uint ServerPort { get; set; }
        public required string FormTheme { get; set; }
        public bool UseTimestamp { get; set; }
        public bool ForceCleanup { get; set; }
        public string? SmartBoxGuid { get; set; }
        public string? TargetFolder { get; set; }
        public required Dictionary<string, Core.Models.FormAnalysisResult> FormDefinitions { get; set; }
    }

    public class K2GenerationResult
    {
        public bool Success { get; set; }
        public required string Message { get; set; }
        public string? ErrorDetails { get; set; }
        public required string FormName { get; set; }
        public int SmartObjectsCreated { get; set; }
        public int ViewsCreated { get; set; }
        public int FormsCreated { get; set; }
        public required Dictionary<string, int> GeneratedArtifacts { get; set; }
    }

    public class K2ConnectionResult
    {
        public bool Success { get; set; }
        public required string Message { get; set; }
        public string? ErrorDetails { get; set; }
        public required string ServerVersion { get; set; }
    }

    public class K2GenerationProgress
    {
        public required string Stage { get; set; }
        public int PercentComplete { get; set; }
        public string? CurrentItem { get; set; }
    }

    #endregion
}
