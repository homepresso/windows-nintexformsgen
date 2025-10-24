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
        private K2Logger? _logger;

        /// <summary>
        /// Enable or disable verbose/debug logging
        /// </summary>
        public bool EnableVerboseLogging { get; set; } = false;

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
                // Capture Console.WriteLine output and redirect to status updates
                var originalConsoleOut = Console.Out;
                var consoleWriter = new System.IO.StringWriter();
                System.Threading.Timer? consoleFlushTimer = null;
                Console.SetOut(consoleWriter);

                try
                {
                    // Initialize logger with appropriate log level
                    if (EnableVerboseLogging)
                    {
                        K2LoggingConfiguration.EnableDebug();
                    }
                    else
                    {
                        K2LoggingConfiguration.SetNormal();
                    }

                    _logger = new K2Logger(OnStatusUpdate, "K2Gen");

                    // Create a background task to flush console output periodically
                    consoleFlushTimer = new System.Threading.Timer(_ =>
                    {
                        var output = consoleWriter.ToString();
                        if (!string.IsNullOrEmpty(output))
                        {
                            // Split by lines and send each line
                            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var line in lines)
                            {
                                OnStatusUpdate($"[Console] {line}");
                            }
                            consoleWriter.GetStringBuilder().Clear();
                        }
                    }, null, 500, 500); // Flush every 500ms

                    var result = new K2GenerationResult
                    {
                        Message = string.Empty,
                        FormName = string.Empty,
                        GeneratedArtifacts = new Dictionary<string, int>()
                    };

                    _logger.LogSection("K2 GENERATION START");
                    _logger.Info($"Verbose Logging: {(EnableVerboseLogging ? "ENABLED" : "DISABLED")}");
                    _logger.Info($"Log Level: {K2LoggingConfiguration.CurrentLogLevel}");

                    // 1. Setup configuration
                    _logger.LogSubSection("Configuration Setup");
                    _logger.Info("Setting up K2 generation configuration...");
                    _config = CreateConfiguration(request);
                    _logger.Info($"Target Folder: {_config.Form.TargetFolder}");
                    _logger.Verbose($"Form Theme: {_config.Form.Theme}");
                    _logger.Verbose($"Force Cleanup: {_config.Form.ForceCleanup}");
                    _logger.Verbose($"Use Timestamp: {_config.Form.UseTimestamp}");
                    _logger.Debug($"Server: {_config.Server.HostName}:{_config.Server.Port}");
                    _logger.Debug($"SmartBox GUID: {_config.K2.SmartBoxGuid}");

                    // 2. Create fresh connection for EACH generation
                    // IMPORTANT: K2 connection must be fresh to avoid stale state issues
                    _logger.LogSubSection("Server Connection");
                    _logger.Info("Establishing fresh connection to K2 server...");
                    _logger.Debug($"Connection Manager State Before: {(_connectionManager != null ? "EXISTS" : "NULL")}");

                    // Disconnect and completely null out existing connection to force fresh state
                    if (_connectionManager != null)
                    {
                        try
                        {
                            _logger.Verbose("Disconnecting existing connection...");
                            _connectionManager.Disconnect();
                            _logger.Debug("Existing connection disconnected successfully");
                        }
                        catch (Exception disconnectEx)
                        {
                            _logger.Warning($"Disconnect warning: {disconnectEx.Message}");
                        }

                        // Null out to force garbage collection and truly fresh state
                        _connectionManager = null;
                        _logger.Debug("Connection manager nulled out for fresh start");
                    }

                    // Create completely new connection with fresh instance
                    _logger.Verbose($"Creating new connection to: {request.ServerName}:{request.ServerPort}");
                    _connectionManager = new ServerConnectionManager(request.ServerName, request.ServerPort);
                    _connectionManager.Connect(); // Explicitly connect
                    _logger.Debug("New connection manager created and connected");
                    _logger.Info("✓ K2 connection established");

                    // 3. Clear registry for fresh generation
                    _logger.LogSubSection("Registry Initialization");
                    _logger.Info("Initializing generation session...");
                    var registryBefore = new {
                        SmartObjects = SmartObjectViewRegistry.GetSmartObjectCount(),
                        Views = SmartObjectViewRegistry.GetViewCount(),
                        Forms = SmartObjectViewRegistry.GetFormCount()
                    };
                    _logger.Debug($"Registry BEFORE Clear - SmartObjects: {registryBefore.SmartObjects}, Views: {registryBefore.Views}, Forms: {registryBefore.Forms}");

                    SmartObjectViewRegistry.Clear();

                    var registryAfter = new {
                        SmartObjects = SmartObjectViewRegistry.GetSmartObjectCount(),
                        Views = SmartObjectViewRegistry.GetViewCount(),
                        Forms = SmartObjectViewRegistry.GetFormCount()
                    };
                    _logger.Debug($"Registry AFTER Clear - SmartObjects: {registryAfter.SmartObjects}, Views: {registryAfter.Views}, Forms: {registryAfter.Forms}");
                    _logger.Info("✓ Registry cleared successfully");

                    // 4. Convert form definitions to JSON
                    _logger.LogSubSection("Form Definition Preparation");
                    _logger.Info($"Converting {request.FormDefinitions.Count} form definition(s) to JSON...");
                    string jsonContent = ConvertFormDefinitionsToJson(request.FormDefinitions);
                    _logger.Debug($"JSON Content Length: {jsonContent.Length} characters");

                    // 4.5. Extract form metadata for later use
                    JObject formData = JObject.Parse(jsonContent);
                    string formDisplayName = formData.Properties().First().Name;
                    // PERFORMANCE FIX: Use proper name sanitization to match SmartObject names
                    string formName = K2SmartObjectGenerator.Utilities.NameSanitizer.SanitizeSmartObjectName(formDisplayName);
                    // Normalize path to use forward slashes for K2 category paths
                    string targetFolder = _config.Form.TargetFolder.Replace("\\", "/");
                    string targetCategory = $"{targetFolder}/{formDisplayName}";

                    _logger.Info($"Form Name (sanitized): {formName}");
                    _logger.Verbose($"Form Display Name: {formDisplayName}");
                    _logger.Verbose($"Target Category: {targetCategory}");

                    // 5. Optional cleanup
                    if (request.ForceCleanup)
                    {
                        _logger.LogSubSection("Cleanup Phase");
                        _logger.Info("Cleaning up existing K2 objects...");
                        OnProgressUpdate(new K2GenerationProgress { Stage = "Cleanup", PercentComplete = 10 });
                        _logger.Verbose("Starting cleanup of Forms, Views, and SmartObjects...");
                        PerformCleanup(jsonContent);
                        _logger.Info("✓ Cleanup completed");
                    }

                    // 6. Generate SmartObjects
                    _logger.LogSection("SMARTOBJECT GENERATION");
                    _logger.Info("Starting SmartObject generation...");
                    OnProgressUpdate(new K2GenerationProgress { Stage = "SmartObjects", PercentComplete = 25 });
                    _logger.Verbose("Initializing SmartObjectGenerator...");
                    _logger.Debug($"Passing configuration and connection to SmartObjectGenerator");

                    var smoGenerator = new SmartObjectGenerator(_connectionManager, _config);
                    _logger.Verbose("Calling GenerateSmartObjectsFromJson...");
                    smoGenerator.GenerateSmartObjectsFromJson(jsonContent);

                    result.SmartObjectsCreated = SmartObjectViewRegistry.GetSmartObjectCount();
                    _logger.Info($"✓ Created {result.SmartObjectsCreated} SmartObjects");
                    _logger.Debug($"Main SmartObjects: {SmartObjectViewRegistry.GetSmartObjectCount(SmartObjectViewRegistry.SmartObjectType.Main)}");
                    _logger.Debug($"Child SmartObjects: {SmartObjectViewRegistry.GetSmartObjectCount(SmartObjectViewRegistry.SmartObjectType.Child)}");
                    _logger.Debug($"Lookup SmartObjects: {SmartObjectViewRegistry.GetSmartObjectCount(SmartObjectViewRegistry.SmartObjectType.Lookup)}");

                    // 7. Generate Views
                    _logger.LogSection("VIEW GENERATION");
                    _logger.Info("Starting View generation...");
                    OnProgressUpdate(new K2GenerationProgress { Stage = "Views", PercentComplete = 50 });
                    _logger.Verbose("Initializing ViewGenerator with field mappings...");
                    _logger.Debug($"Field mappings count: {smoGenerator.FieldMappings.Count}");

                    var viewGenerator = new ViewGenerator(_connectionManager, smoGenerator.FieldMappings, smoGenerator, _config);
                    _logger.Verbose("Calling GenerateViewsFromJson...");
                    viewGenerator.GenerateViewsFromJson(jsonContent);

                    result.ViewsCreated = SmartObjectViewRegistry.GetViewCount();
                    _logger.Info($"✓ Created {result.ViewsCreated} Views");
                    _logger.Verbose($"ViewTitles collected: {viewGenerator.ViewTitles.Count}");
                    _logger.Debug($"Capture Views: {SmartObjectViewRegistry.GetViewCount(SmartObjectViewRegistry.ViewType.Capture)}");
                    _logger.Debug($"Item Views: {SmartObjectViewRegistry.GetViewCount(SmartObjectViewRegistry.ViewType.Item)}");
                    _logger.Debug($"List Views: {SmartObjectViewRegistry.GetViewCount(SmartObjectViewRegistry.ViewType.List)}");

                    // 8. Generate Forms with verification and retry
                    _logger.LogSection("FORM GENERATION");

                    // Validate that views were created before attempting form generation
                    if (result.ViewsCreated == 0)
                    {
                        _logger.Error("❌ Cannot generate forms: No views were created");
                        _logger.Warning("This usually means views from a previous run are still in use or checked out");
                        _logger.Warning("Please manually delete existing forms and views in K2 Designer before regenerating");
                        throw new InvalidOperationException("Cannot generate forms without views. Please clean up existing artifacts in K2 Designer (Forms→Views→SmartObjects) and try again.");
                    }

                    _logger.Info("Starting Form generation...");
                    OnProgressUpdate(new K2GenerationProgress { Stage = "Forms", PercentComplete = 75 });
                    _logger.Verbose($"Initializing FormGenerator with theme: {request.FormTheme}");
                    _logger.Debug($"Passing {viewGenerator.ViewTitles.Count} view titles to form generator");

                    var formGenerator = new K2SmartObjectGenerator.FormGenerator(_connectionManager, request.FormTheme, smoGenerator);

                    // Form name was already extracted earlier for category creation
                    _logger.Verbose("Calling GenerateFormsFromJson...");
                    formGenerator.GenerateFormsFromJson(jsonContent, viewGenerator.ViewTitles);
                    _logger.Info("✓ Form generation completed");

                    // 9. Register everything
                    _logger.LogSubSection("Form Registration");
                    _logger.Info("Registering forms in SmartObjectViewRegistry...");
                    var allSmartObjects = new List<string> { formName };
                    allSmartObjects.AddRange(SmartObjectViewRegistry.GetChildSmartObjects(formName));

                    var lookupSmo = $"{formName}_Lookups";
                    if (SmartObjectViewRegistry.SmartObjectExists(lookupSmo))
                    {
                        allSmartObjects.Add(lookupSmo);
                        _logger.Verbose($"Added lookup SmartObject: {lookupSmo}");
                    }

                    _logger.Verbose($"Total SmartObjects to register: {allSmartObjects.Count}");
                    if (_logger != null && K2LoggingConfiguration.ShouldLog(K2LogLevel.Debug))
                    {
                        foreach (var smo in allSmartObjects)
                        {
                            _logger.Debug($"  • SmartObject: {smo}");
                        }
                    }

                    var formViews = new List<string>();
                    foreach (var smo in allSmartObjects)
                    {
                        var views = SmartObjectViewRegistry.GetViewsForSmartObject(smo);
                        formViews.AddRange(views);
                        _logger.Verbose($"SmartObject '{smo}' has {views.Count} view(s)");
                    }

                    _logger.Verbose($"Total form views: {formViews.Count}");
                    if (_logger != null && K2LoggingConfiguration.ShouldLog(K2LogLevel.Debug))
                    {
                        foreach (var view in formViews)
                        {
                            _logger.Debug($"  • View: {view}");
                        }
                    }

                    SmartObjectViewRegistry.RegisterForm(formName, formViews, allSmartObjects);
                    _logger.Info($"✓ Form '{formName}' registered in registry");

                    result.FormsCreated = SmartObjectViewRegistry.GetFormCount();
                    _logger.Info($"✓ Registry reports {result.FormsCreated} form(s) created");

                    // 10. Generate summary
                    _logger.LogSection("GENERATION COMPLETE");
                    OnProgressUpdate(new K2GenerationProgress { Stage = "Complete", PercentComplete = 100 });

                    result.Success = true;
                    result.Message = "K2 artifacts generated successfully";
                    result.FormName = formName;
                    result.GeneratedArtifacts = GetGeneratedArtifactsSummary();

                    _logger.Info("════════════════════════════════════════════════");
                    _logger.Info($"✓ SUCCESS: K2 generation completed");
                    _logger.Info($"  • SmartObjects: {result.SmartObjectsCreated}");
                    _logger.Info($"  • Views: {result.ViewsCreated}");
                    _logger.Info($"  • Forms: {result.FormsCreated}");
                    _logger.Info("════════════════════════════════════════════════");

                    // Clear K2 connection and force garbage collection to reset any cached state
                    // This ensures the next generation starts with a completely clean slate
                    _logger.LogSubSection("Cleanup");
                    _logger.Verbose("Clearing K2 connection and cache...");
                    if (_connectionManager != null)
                    {
                        try
                        {
                            _connectionManager.Disconnect();
                            _connectionManager = null;
                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                            GC.Collect();
                            _logger.Debug("K2 connection cleared and garbage collected");
                        }
                        catch (Exception cleanupEx)
                        {
                            _logger.Warning($"Cleanup warning: {cleanupEx.Message}");
                        }
                    }

                    // Dispose timer and flush any remaining console output
                    consoleFlushTimer?.Dispose();
                    var finalOutput = consoleWriter.ToString();
                    if (!string.IsNullOrEmpty(finalOutput))
                    {
                        var lines = finalOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            OnStatusUpdate($"[Console] {line}");
                        }
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    // Dispose timer and flush any remaining console output
                    consoleFlushTimer?.Dispose();
                    var finalOutput = consoleWriter.ToString();
                    if (!string.IsNullOrEmpty(finalOutput))
                    {
                        var lines = finalOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            OnStatusUpdate($"[Console] {line}");
                        }
                    }

                    if (_logger != null)
                    {
                        _logger.Error($"K2 generation failed: {ex.Message}");
                        _logger.Debug($"Exception type: {ex.GetType().FullName}");
                        _logger.Debug($"Stack trace: {ex.StackTrace}");
                    }
                    else
                    {
                        OnStatusUpdate($"K2 generation failed: {ex.Message}");
                    }

                    return new K2GenerationResult
                    {
                        Success = false,
                        Message = $"Generation failed: {ex.Message}",
                        ErrorDetails = ex.ToString(),
                        FormName = string.Empty,
                        GeneratedArtifacts = new Dictionary<string, int>()
                    };
                }
                finally
                {
                    // Always restore original console output
                    Console.SetOut(originalConsoleOut);
                    consoleWriter?.Dispose();
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

            // Normalize target folder path - remove leading/trailing slashes and convert to backslash
            var targetFolder = request.TargetFolder ?? "TestForms";
            targetFolder = targetFolder.Trim('/', '\\').Replace("/", "\\");
            config.Form.TargetFolder = string.IsNullOrEmpty(targetFolder) ? "Generated" : targetFolder;

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

                    // Inject TargetFolder into the FormDefinition so generators can access it
                    if (_config != null && !string.IsNullOrEmpty(_config.Form.TargetFolder))
                    {
                        formData["TargetFolder"] = _config.Form.TargetFolder;
                    }

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
                var formGenerator = new K2SmartObjectGenerator.FormGenerator(_connectionManager, _config.Form.Theme, smoGenerator);
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
        public string ServerName { get; set; } = string.Empty;
        public uint ServerPort { get; set; }
        public string FormTheme { get; set; } = "Default";
        public bool UseTimestamp { get; set; }
        public bool ForceCleanup { get; set; }
        public string? SmartBoxGuid { get; set; }
        public string? TargetFolder { get; set; }
        public Dictionary<string, Core.Models.FormAnalysisResult> FormDefinitions { get; set; } = new();
    }

    public class K2GenerationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? ErrorDetails { get; set; }
        public string FormName { get; set; } = string.Empty;
        public int SmartObjectsCreated { get; set; }
        public int ViewsCreated { get; set; }
        public int FormsCreated { get; set; }
        public Dictionary<string, int> GeneratedArtifacts { get; set; } = new();
    }

    public class K2ConnectionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? ErrorDetails { get; set; }
        public string ServerVersion { get; set; } = string.Empty;
    }

    public class K2GenerationProgress
    {
        public string Stage { get; set; } = string.Empty;
        public int PercentComplete { get; set; }
        public string? CurrentItem { get; set; }
    }

    #endregion
}
