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
            return await Task.Run(async () =>
            {
                // PERFORMANCE: Only capture console output when verbose logging is enabled
                // This prevents console I/O overhead from halting the app with large forms
                var originalConsoleOut = Console.Out;
                var consoleWriter = new System.IO.StringWriter();
                System.Threading.Timer? consoleFlushTimer = null;
                // Lock object to prevent race condition between timer callback
                // and FlushConsoleBuffer. Without this, both can call ToString()
                // simultaneously, causing duplicate output and wasted memory.
                var consoleLock = new object();

                // Helper to synchronously flush buffered Console output.
                // Called at form boundaries to prevent interleaving between forms.
                void FlushConsoleBuffer()
                {
                    if (!EnableVerboseLogging) return;
                    lock (consoleLock)
                    {
                        var output = consoleWriter.ToString();
                        if (!string.IsNullOrEmpty(output))
                        {
                            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var line in lines)
                            {
                                OnStatusUpdate($"[Console] {line}");
                            }
                            // Clear content AND trim excess capacity to release memory
                            var sb = consoleWriter.GetStringBuilder();
                            sb.Clear();
                            sb.Capacity = 0;
                        }
                    }
                }

                // Only redirect console if verbose logging is enabled
                if (EnableVerboseLogging)
                {
                    Console.SetOut(consoleWriter);
                }

                try
                {
                    // Initialize logger with appropriate log level
                    if (EnableVerboseLogging)
                    {
                        K2LoggingConfiguration.EnableDebug();
                        _logger = new K2Logger(OnStatusUpdate, "K2Gen");

                        // ONLY create console flush timer when verbose logging is enabled
                        // Create a background task to flush console output periodically
                        consoleFlushTimer = new System.Threading.Timer(_ =>
                        {
                            lock (consoleLock)
                            {
                                var output = consoleWriter.ToString();
                                if (!string.IsNullOrEmpty(output))
                                {
                                    var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                                    foreach (var line in lines)
                                    {
                                        OnStatusUpdate($"[Console] {line}");
                                    }
                                    var sb = consoleWriter.GetStringBuilder();
                                    sb.Clear();
                                    sb.Capacity = 0;
                                }
                            }
                        }, null, 500, 500); // Flush every 500ms
                    }
                    else
                    {
                        // PERFORMANCE: Disable all logging when verbose is off
                        // Set to Error-only (which effectively silences Info/Debug/Verbose logs)
                        K2LoggingConfiguration.CurrentLogLevel = K2LogLevel.Error;
                        // Create a null logger that does nothing
                        _logger = new K2Logger((msg) => { }, "K2Gen");
                    }

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

                    // 4. Convert form definitions to JSON (combines all uploaded forms)
                    _logger.LogSubSection("Form Definition Preparation");
                    _logger.Info($"Converting {request.FormDefinitions.Count} form definition(s) to JSON...");
                    string combinedJsonContent = ConvertFormDefinitionsToJson(request.FormDefinitions);
                    _logger.Debug($"JSON Content Length: {combinedJsonContent.Length} characters");

                    // Parse combined JSON to discover all form root properties
                    JObject allFormsData = JObject.Parse(combinedJsonContent);
                    var formProperties = allFormsData.Properties().ToList();
                    int totalForms = formProperties.Count;
                    _logger.Info($"Found {totalForms} form(s) to generate");

                    // Track all generated form names for the result
                    var generatedFormNames = new List<string>();

                    // Tallies for the "modify existing form" path (the registry-based aggregation
                    // below only counts newly-created artifacts, so updates are added separately).
                    int updatedViewsTotal = 0;
                    int createdLookupsTotal = 0;

                    // ════════════════════════════════════════════════
                    // MULTI-FORM LOOP: Process each form independently
                    // ════════════════════════════════════════════════
                    for (int formIndex = 0; formIndex < totalForms; formIndex++)
                    {
                        // Flush any remaining console output from previous form before starting next
                        if (formIndex > 0) FlushConsoleBuffer();

                        var formProperty = formProperties[formIndex];
                        string formDisplayName = formProperty.Name;
                        string formName = K2SmartObjectGenerator.Utilities.NameSanitizer.SanitizeSmartObjectName(formDisplayName);
                        string targetFolder = _config.Form.TargetFolder.Replace("\\", "/");
                        string targetCategory = $"{targetFolder}/{formDisplayName}";

                        _logger.LogSection($"FORM {formIndex + 1} OF {totalForms}: {formDisplayName}");
                        _logger.Info($"Form Name (sanitized): {formName}");
                        _logger.Verbose($"Form Display Name: {formDisplayName}");
                        _logger.Verbose($"Target Category: {targetCategory}");

                        // Create per-form JSON (single root property) for the generators
                        var singleFormJson = new JObject();
                        singleFormJson[formDisplayName] = formProperty.Value;
                        string jsonContent = singleFormJson.ToString();

                        // Calculate progress offsets for this form within the overall pipeline
                        int formBasePercent = (formIndex * 100) / totalForms;
                        int formRangePercent = 100 / totalForms;

                        // ── Existing-form mapping: modify the mapped K2 form in place instead of
                        //    creating new SmartObjects/Views/Forms. ──
                        K2ExistingFormMapping existingMapping = ResolveExistingMapping(request, formName, formDisplayName, out var mappedInfoPathDef, out var mappedKey);
                        if (existingMapping != null)
                        {
                            Dictionary<string, string> fieldMap = null;
                            if (mappedKey != null && request.ExistingFieldMappings != null)
                                request.ExistingFieldMappings.TryGetValue(mappedKey, out fieldMap);

                            _logger.LogSubSection($"Update Existing K2 Form ({formDisplayName} → {existingMapping.K2FormDisplayName})");
                            _logger.Info($"Mapped to existing K2 form '{existingMapping.K2FormDisplayName}' [{existingMapping.K2FormGuid}] - modifying in place (no SmartObject/View/Form creation)");
                            OnProgressUpdate(new K2GenerationProgress { Stage = $"Update Existing ({formDisplayName})", PercentComplete = formBasePercent + (formRangePercent * 50 / 100) });

                            try
                            {
                                // Route updater diagnostics through OnStatusUpdate so they are ALWAYS
                                // visible in the live log (independent of the verbose-logging level).
                                var updater = new ExistingK2FormUpdater(_connectionManager, _config, msg => OnStatusUpdate(msg));
                                var upd = await updater.UpdateAsync(existingMapping, mappedInfoPathDef, jsonContent, request.CreateSmartBoxLookups, fieldMap);

                                updatedViewsTotal += upd.ViewsUpdated;
                                createdLookupsTotal += upd.LookupsCreated;
                                _logger.Info($"✓ Updated existing form '{existingMapping.K2FormDisplayName}': {upd.ViewsUpdated} view(s) modified, {upd.ControlsRepositioned} control(s) repositioned, {upd.RulesAppliedViews} view(s) with rules, {upd.LookupsCreated} lookup SmartObject(s), {upd.DropdownsWired} dropdown(s) wired");
                                generatedFormNames.Add(existingMapping.K2FormName);
                            }
                            catch (Exception updEx)
                            {
                                _logger.Error($"❌ Failed to update existing K2 form '{existingMapping.K2FormDisplayName}': {updEx.Message}");
                            }

                            FlushConsoleBuffer();
                            continue; // Skip the create path for this mapped form.
                        }

                        // 5. Optional cleanup (per form)
                        if (request.ForceCleanup)
                        {
                            _logger.LogSubSection($"Cleanup Phase ({formDisplayName})");
                            _logger.Info("Cleaning up existing K2 objects...");
                            OnProgressUpdate(new K2GenerationProgress { Stage = $"Cleanup ({formDisplayName})", PercentComplete = formBasePercent + (formRangePercent * 10 / 100) });
                            PerformCleanup(jsonContent);
                            _logger.Info("✓ Cleanup completed");
                        }

                        // 6. Generate SmartObjects
                        _logger.LogSubSection($"SmartObject Generation ({formDisplayName})");
                        _logger.Info("Starting SmartObject generation...");
                        OnProgressUpdate(new K2GenerationProgress { Stage = $"SmartObjects ({formDisplayName})", PercentComplete = formBasePercent + (formRangePercent * 25 / 100) });

                        var smoGenerator = new SmartObjectGenerator(_connectionManager, _config);
                        await smoGenerator.GenerateSmartObjectsFromJsonAsync(jsonContent);

                        _logger.Info($"✓ SmartObjects created for {formDisplayName}");

                        // 7. Generate Views
                        _logger.LogSubSection($"View Generation ({formDisplayName})");
                        _logger.Info("Starting View generation...");
                        OnProgressUpdate(new K2GenerationProgress { Stage = $"Views ({formDisplayName})", PercentComplete = formBasePercent + (formRangePercent * 50 / 100) });

                        // Find the matching InfoPathFormDefinition for this specific form
                        InfoPathFormDefinition infoPathFormDef = null;
                        if (request.FormDefinitions != null)
                        {
                            // Try matching by form name (with and without spaces)
                            foreach (var defKvp in request.FormDefinitions)
                            {
                                var candidateDef = defKvp.Value?.FormDefinition as InfoPathFormDefinition;
                                if (candidateDef != null)
                                {
                                    string candidateName = candidateDef.FormName?.Replace(" ", "_");
                                    if (string.Equals(candidateName, formName, StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(candidateDef.FormName, formDisplayName, StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(defKvp.Key, formDisplayName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        infoPathFormDef = candidateDef;
                                        break;
                                    }
                                }
                            }
                            // Fallback: if only one form definition, use it
                            if (infoPathFormDef == null && request.FormDefinitions.Count == 1)
                            {
                                infoPathFormDef = request.FormDefinitions.Values.First()?.FormDefinition as InfoPathFormDefinition;
                            }
                            if (infoPathFormDef != null)
                                _logger.Info($"InfoPath form definition matched for rule generation ({infoPathFormDef.FormName})");
                            else
                                _logger.Warning($"No InfoPath form definition found for '{formDisplayName}' - rules may be limited");
                        }

                        var viewGenerator = new ViewGenerator(_connectionManager, smoGenerator.FieldMappings, smoGenerator, _config, infoPathFormDef);
                        viewGenerator.GenerateViewsFromJson(jsonContent);

                        _logger.Info($"✓ Views created for {formDisplayName}");
                        _logger.Verbose($"ViewTitles collected: {viewGenerator.ViewTitles.Count}");

                        // 8. Generate Forms
                        _logger.LogSubSection($"Form Generation ({formDisplayName})");

                        // Validate views exist before form generation
                        int viewCountForForm = SmartObjectViewRegistry.GetViewCount();
                        if (viewCountForForm == 0)
                        {
                            _logger.Error($"❌ Cannot generate form for {formDisplayName}: No views were created");
                            _logger.Warning("Skipping this form and continuing with next...");
                            continue;
                        }

                        _logger.Info("Starting Form generation...");
                        OnProgressUpdate(new K2GenerationProgress { Stage = $"Forms ({formDisplayName})", PercentComplete = formBasePercent + (formRangePercent * 75 / 100) });
                        _logger.Debug($"Passing {viewGenerator.ViewTitles.Count} view titles to form generator");
                        _logger.Debug($"Passing {viewGenerator.ViewGridPositions.Count} view grid positions to form generator");

                        var formGenerator = new K2SmartObjectGenerator.FormGenerator(_connectionManager, request.FormTheme, smoGenerator, infoPathFormDef);
                        formGenerator.GenerateFormsFromJson(jsonContent, viewGenerator.ViewTitles, viewGenerator.ViewGridPositions);
                        _logger.Info($"✓ Form generation completed for {formDisplayName}");

                        // 9. Register this form
                        _logger.LogSubSection($"Form Registration ({formDisplayName})");
                        var allSmartObjects = new List<string> { formName };
                        allSmartObjects.AddRange(SmartObjectViewRegistry.GetChildSmartObjects(formName));

                        var lookupSmo = $"{formName}_Lookups";
                        if (SmartObjectViewRegistry.SmartObjectExists(lookupSmo))
                        {
                            allSmartObjects.Add(lookupSmo);
                            _logger.Verbose($"Added lookup SmartObject: {lookupSmo}");
                        }

                        _logger.Verbose($"Total SmartObjects for {formDisplayName}: {allSmartObjects.Count}");

                        var formViews = new List<string>();
                        foreach (var smo in allSmartObjects)
                        {
                            var views = SmartObjectViewRegistry.GetViewsForSmartObject(smo);
                            formViews.AddRange(views);
                        }

                        SmartObjectViewRegistry.RegisterForm(formName, formViews, allSmartObjects);
                        _logger.Info($"✓ Form '{formName}' registered ({allSmartObjects.Count} SmartObjects, {formViews.Count} views)");

                        generatedFormNames.Add(formName);

                        // ── Memory cleanup: release generator objects between forms ──
                        // Each generator holds large data structures (XML documents, JSON,
                        // field mappings, SmartObject schemas) and they cross-reference
                        // each other (ViewGenerator→SmartObjectGenerator, FormGenerator→
                        // SmartObjectGenerator). Without explicit cleanup, all generators
                        // from ALL forms stay alive until the loop ends, causing memory
                        // to grow linearly with the number of forms.
                        smoGenerator = null;
                        viewGenerator = null;
                        formGenerator = null;
                        infoPathFormDef = null;
                        singleFormJson = null;
                        jsonContent = null;

                        // Flush console buffer at form boundary to prevent interleaving
                        // between forms in multi-form generation. Without this, the 500ms
                        // timer-based flush causes log lines from different forms to mix.
                        FlushConsoleBuffer();
                    }
                    // ════════════════════════════════════════════════
                    // END MULTI-FORM LOOP
                    // ════════════════════════════════════════════════

                    // Aggregate results across all forms (registry = newly created artifacts),
                    // then add the "modify existing form" tallies so updates are reflected too.
                    result.SmartObjectsCreated = SmartObjectViewRegistry.GetSmartObjectCount() + createdLookupsTotal;
                    result.ViewsCreated = SmartObjectViewRegistry.GetViewCount() + updatedViewsTotal;
                    result.FormsCreated = SmartObjectViewRegistry.GetFormCount();

                    // 10. Generate summary
                    _logger.LogSection("GENERATION COMPLETE");
                    OnProgressUpdate(new K2GenerationProgress { Stage = "Complete", PercentComplete = 100 });

                    result.Success = true;
                    result.Message = $"K2 artifacts generated successfully for {generatedFormNames.Count} form(s)";
                    result.FormName = string.Join(", ", generatedFormNames);
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

                    // Dispose timer and flush any remaining console output (only if verbose logging enabled)
                    consoleFlushTimer?.Dispose();
                    if (EnableVerboseLogging)
                    {
                        var finalOutput = consoleWriter.ToString();
                        if (!string.IsNullOrEmpty(finalOutput))
                        {
                            var lines = finalOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var line in lines)
                            {
                                OnStatusUpdate($"[Console] {line}");
                            }
                        }
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    // Dispose timer and flush any remaining console output (only if verbose logging enabled)
                    consoleFlushTimer?.Dispose();
                    if (EnableVerboseLogging)
                    {
                        var finalOutput = consoleWriter.ToString();
                        if (!string.IsNullOrEmpty(finalOutput))
                        {
                            var lines = finalOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var line in lines)
                            {
                                OnStatusUpdate($"[Console] {line}");
                            }
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

            // CRITICAL: Use the EnableVerboseLogging property set from the UI checkbox
            // When unchecked, all logging is disabled for maximum performance
            config.Logging.VerboseLogging = EnableVerboseLogging;
            config.Logging.ShowProgress = EnableVerboseLogging;

            // Normalize target folder path - remove leading/trailing slashes and convert to backslash
            var targetFolder = request.TargetFolder ?? "TestForms";
            targetFolder = targetFolder.Trim('/', '\\').Replace("/", "\\");
            config.Form.TargetFolder = string.IsNullOrEmpty(targetFolder) ? "Generated" : targetFolder;

            config.K2.SmartBoxGuid = request.SmartBoxGuid ?? "e5609413-d844-4325-98c3-db3cacbd406d";

            return config;
        }

        /// <summary>
        /// Finds the existing-K2-form mapping (if any) for the form currently being processed in
        /// the generation loop, correlating the JSON form name back to the file-name mapping key.
        /// </summary>
        private K2ExistingFormMapping ResolveExistingMapping(K2GenerationRequest request, string formName,
            string formDisplayName, out InfoPathFormDefinition mappedInfoPathDef, out string matchedKey)
        {
            mappedInfoPathDef = null;
            matchedKey = null;
            if (request?.ExistingFormMappings == null || request.ExistingFormMappings.Count == 0 || request.FormDefinitions == null)
                return null;

            foreach (var defKvp in request.FormDefinitions)
            {
                var candidateDef = defKvp.Value?.FormDefinition as InfoPathFormDefinition;
                if (candidateDef == null) continue;

                string candidateName = candidateDef.FormName?.Replace(" ", "_");
                bool matches = string.Equals(candidateName, formName, StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(candidateDef.FormName, formDisplayName, StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(defKvp.Key, formDisplayName, StringComparison.OrdinalIgnoreCase);

                if (matches && request.ExistingFormMappings.TryGetValue(defKvp.Key, out var mapping) && mapping != null)
                {
                    mappedInfoPathDef = candidateDef;
                    matchedKey = defKvp.Key;
                    return mapping;
                }
            }

            // Fallback: a single imported form mapped to a single existing form.
            if (request.FormDefinitions.Count == 1 && request.ExistingFormMappings.Count == 1)
            {
                var only = request.FormDefinitions.First();
                mappedInfoPathDef = only.Value?.FormDefinition as InfoPathFormDefinition;
                matchedKey = only.Key;
                return request.ExistingFormMappings.Values.First();
            }

            return null;
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

        /// <summary>
        /// Re-saves all views by reading K2's normalized XML and re-deploying it.
        /// This simulates opening a view in K2 Designer and saving it, which
        /// fixes AreaItemView,View,Warning validation issues.
        /// </summary>
        private void ResaveAllViews(string formName)
        {
            var allSmartObjects = new List<string> { formName };
            allSmartObjects.AddRange(SmartObjectViewRegistry.GetChildSmartObjects(formName));

            var lookupSmo = $"{formName}_Lookups";
            if (SmartObjectViewRegistry.SmartObjectExists(lookupSmo))
                allSmartObjects.Add(lookupSmo);

            var allViewNames = new List<string>();
            foreach (var smo in allSmartObjects)
            {
                var views = SmartObjectViewRegistry.GetViewsForSmartObject(smo);
                allViewNames.AddRange(views);
            }

            if (allViewNames.Count == 0)
            {
                Console.WriteLine("    No views found to re-save");
                return;
            }

            Console.WriteLine($"    Re-saving {allViewNames.Count} views...");

            using (SourceCode.Forms.Management.FormsManager formsManager = new SourceCode.Forms.Management.FormsManager())
            {
                formsManager.CreateConnection();
                formsManager.Connection.Open(_connectionManager.ConnectionString.ConnectionString);

                foreach (string viewName in allViewNames)
                {
                    try
                    {
                        // Step 1: Read the view XML as K2 has stored it (normalized)
                        string viewXml = formsManager.GetViewDefinition(viewName);
                        if (string.IsNullOrEmpty(viewXml))
                        {
                            Console.WriteLine($"      SKIP: Could not read view '{viewName}'");
                            continue;
                        }

                        // Step 2: Parse to get the view GUID
                        System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
                        doc.LoadXml(viewXml);
                        System.Xml.XmlNodeList viewNodes = doc.GetElementsByTagName("View");
                        if (viewNodes.Count == 0)
                        {
                            Console.WriteLine($"      SKIP: No View element in '{viewName}'");
                            continue;
                        }

                        System.Xml.XmlElement viewElement = (System.Xml.XmlElement)viewNodes[0];
                        string viewIdStr = viewElement.GetAttribute("ID");

                        // Step 3: Check out the view
                        if (Guid.TryParse(viewIdStr, out Guid viewGuid))
                        {
                            try { formsManager.CheckOutView(viewGuid); }
                            catch { /* Already checked out or not needed */ }
                        }

                        // Step 4: Get the category path for re-deployment
                        string categoryPath = null;
                        var viewInfo = SmartObjectViewRegistry.GetViewInfo(viewName);
                        if (viewInfo?.Metadata != null)
                            categoryPath = viewInfo.Metadata.Category;

                        if (string.IsNullOrEmpty(categoryPath))
                            categoryPath = $"Generated\\{formName}\\Views";

                        // Step 5: Re-deploy K2's own XML back (false = update existing)
                        formsManager.DeployViews(viewXml, categoryPath, false);

                        // Step 6: Check in the view
                        if (viewGuid != Guid.Empty)
                        {
                            try { formsManager.CheckInView(viewGuid); }
                            catch { /* Ignore check-in errors */ }
                        }

                        Console.WriteLine($"      Re-saved: {viewName}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"      WARNING: Failed to re-save view '{viewName}': {ex.Message}");
                    }
                }
            }

            Console.WriteLine($"    View re-save complete");
        }

        /// <summary>
        /// Re-saves all forms by reading K2's normalized XML and re-deploying it.
        /// This simulates opening a form in K2 Designer and saving it.
        /// Combined with removing IsGenerated and setting IsUserModified, this
        /// should fix AreaItemView,View,Warning validation issues.
        /// </summary>
        private void ResaveAllForms(string formName)
        {
            var allFormNames = SmartObjectViewRegistry.GetAllFormNames();

            if (allFormNames.Count == 0)
            {
                Console.WriteLine("    No forms found to re-save");
                return;
            }

            Console.WriteLine($"    Re-saving {allFormNames.Count} forms...");

            using (SourceCode.Forms.Management.FormsManager formsManager = new SourceCode.Forms.Management.FormsManager())
            {
                formsManager.CreateConnection();
                formsManager.Connection.Open(_connectionManager.ConnectionString.ConnectionString);

                foreach (string fName in allFormNames)
                {
                    try
                    {
                        // Step 1: Read the form XML as K2 has stored it (normalized)
                        string formXml = formsManager.GetFormDefinition(fName);
                        if (string.IsNullOrEmpty(formXml))
                        {
                            Console.WriteLine($"      SKIP: Could not read form '{fName}'");
                            continue;
                        }

                        // Step 2: Parse and ensure IsGenerated is removed and IsUserModified is set
                        System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
                        doc.LoadXml(formXml);
                        System.Xml.XmlNodeList formNodes = doc.GetElementsByTagName("Form");
                        if (formNodes.Count > 0)
                        {
                            System.Xml.XmlElement formElement = (System.Xml.XmlElement)formNodes[0];

                            // Remove IsGenerated (K2 AutoGenerator artifact)
                            if (formElement.HasAttribute("IsGenerated"))
                            {
                                formElement.RemoveAttribute("IsGenerated");
                                Console.WriteLine($"      Removed IsGenerated from '{fName}'");
                            }

                            // Set IsUserModified to True
                            formElement.SetAttribute("IsUserModified", "True");
                        }

                        // Step 3: Get the category path for re-deployment
                        string categoryPath = $"Generated\\{formName}\\Forms";

                        // Step 4: Re-deploy K2's normalized XML back (false = update existing)
                        formsManager.DeployForms(doc.OuterXml, categoryPath, false);

                        Console.WriteLine($"      Re-saved: {fName}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"      WARNING: Failed to re-save form '{fName}': {ex.Message}");
                    }
                }
            }

            Console.WriteLine($"    Form re-save complete");
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

        /// <summary>
        /// Imported InfoPath forms mapped to existing K2 forms, keyed by the same file-name
        /// key used in <see cref="FormDefinitions"/>. When a form has a mapping, generation
        /// modifies the existing K2 form in place instead of creating new artifacts.
        /// </summary>
        public Dictionary<string, K2ExistingFormMapping> ExistingFormMappings { get; set; } = new();

        /// <summary>
        /// User-confirmed field mappings for each mapped form, keyed by the same file-name key as
        /// <see cref="FormDefinitions"/>. Inner map: InfoPath control name → existing K2 control ID.
        /// </summary>
        public Dictionary<string, Dictionary<string, string>> ExistingFieldMappings { get; set; } = new();

        /// <summary>
        /// When modifying a mapped form, also create + populate SmartBox lookup SmartObjects
        /// from InfoPath dropdown options and wire matching dropdown controls to them.
        /// </summary>
        public bool CreateSmartBoxLookups { get; set; }
    }

    /// <summary>
    /// A mapping from an imported InfoPath form to an existing K2 form on the server.
    /// </summary>
    public class K2ExistingFormMapping
    {
        public string K2FormName { get; set; } = string.Empty;
        public string K2FormDisplayName { get; set; } = string.Empty;
        public Guid K2FormGuid { get; set; }
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
