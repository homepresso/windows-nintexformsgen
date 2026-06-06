using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using Newtonsoft.Json.Linq;
using K2SmartObjectGenerator.Config;
using K2SmartObjectGenerator.Models;
using K2SmartObjectGenerator.Utilities;
using FormGenerator.Analyzers.Infopath;
using FormGenerator.Services;
using SourceCode.Forms.Management;

namespace K2SmartObjectGenerator
{
    /// <summary>
    /// "Modify in place" path for the K2 generator. When an imported InfoPath form is mapped
    /// to an existing K2 form, this updater does NOT create new SmartObjects/Views/Forms.
    /// Instead it modifies the existing K2 views to reflect the InfoPath form:
    ///   • repositions the existing controls to match the InfoPath grid layout (non-destructive),
    ///   • adds the InfoPath rules,
    ///   • optionally creates + populates SmartBox lookups and wires matching dropdowns.
    /// Every step fails safe (logs + skips) so a partial modify never crashes generation.
    /// </summary>
    public class ExistingK2FormUpdater
    {
        public class UpdateResult
        {
            public int ViewsUpdated;
            public int ControlsRepositioned;
            public int RulesAppliedViews;
            public int LookupsCreated;
            public int DropdownsWired;
        }

        private readonly ServerConnectionManager _connectionManager;
        private readonly GeneratorConfiguration _config;
        private readonly Action<string> _log;

        public ExistingK2FormUpdater(ServerConnectionManager connectionManager, GeneratorConfiguration config, Action<string> log = null)
        {
            _connectionManager = connectionManager;
            _config = config ?? GeneratorConfiguration.CreateDefault();
            _log = log ?? Console.WriteLine;
        }

        public async Task<UpdateResult> UpdateAsync(
            K2ExistingFormMapping mapping,
            InfoPathFormDefinition infoPathDef,
            string jsonContent,
            bool createSmartBoxLookups,
            IDictionary<string, string> explicitFieldMap = null)
        {
            var result = new UpdateResult();
            if (mapping == null || infoPathDef == null)
            {
                _log("  [UpdateExisting] Missing mapping or InfoPath definition - nothing to do");
                return result;
            }

            // Parse the visibility data from the form JSON in the exact shape the rule builder expects.
            JArray dynamicSections = null;
            JObject conditionalVisibility = null;
            try
            {
                var jo = JObject.Parse(jsonContent);
                var fd = jo.Properties().First().Value?["FormDefinition"];
                dynamicSections = fd?["DynamicSections"] as JArray;
                conditionalVisibility = fd?["ConditionalVisibility"] as JObject;
            }
            catch { /* visibility rules optional */ }

            // 1) Optionally create + populate the SmartBox lookups first so wiring can reference them.
            string lookupSmoName = null;
            if (createSmartBoxLookups)
            {
                try
                {
                    _log("  [UpdateExisting] Creating + populating SmartBox lookups...");
                    var smoGen = new SmartObjectGenerator(_connectionManager, _config);
                    await smoGen.GenerateLookupSmartObjectsOnlyAsync(jsonContent);

                    string formName = NameSanitizer.SanitizeSmartObjectName(infoPathDef.FormName ?? mapping.K2FormName);
                    string candidate = $"{formName}_Lookups";
                    if (SmartObjectViewRegistry.SmartObjectExists(candidate))
                    {
                        lookupSmoName = candidate;
                        result.LookupsCreated = 1;
                        _log($"  [UpdateExisting] Lookup SmartObject ready: {lookupSmoName}");
                    }
                    else
                    {
                        _log("  [UpdateExisting] No lookup SmartObject was created (no dropdown fields?)");
                    }
                }
                catch (Exception ex)
                {
                    _log($"  [UpdateExisting] Lookup creation failed (continuing): {ex.Message}");
                }
            }

            // 2) Open FormsManager and enumerate the existing form's views.
            var formsManager = new FormsManager();
            try
            {
                string host = _config.Server?.HostName ?? "localhost";
                uint port = _config.Server?.Port ?? 5555;
                if (!formsManager.Open(host, port))
                {
                    _log($"  [UpdateExisting] Unable to open FormsManager at {host}:{port}");
                    return result;
                }

                _log($"  [UpdateExisting] Resolving views for form '{mapping.K2FormDisplayName}' [{mapping.K2FormGuid}]");

                var views = new List<ViewInfo>();
                try
                {
                    var explorer = formsManager.GetViewsForForm(mapping.K2FormGuid);
                    if (explorer?.Views != null)
                        views = explorer.Views.Cast<ViewInfo>().Where(v => v != null).ToList();
                    _log($"  [UpdateExisting] GetViewsForForm returned {views.Count} view(s)");
                }
                catch (Exception ex)
                {
                    _log($"  [UpdateExisting] GetViewsForForm failed: {ex.Message}");
                }

                // Fallback: resolve view IDs for the form and load each individually.
                if (views.Count == 0)
                {
                    try
                    {
                        var ids = formsManager.GetViewIdsForForm(mapping.K2FormGuid);
                        _log($"  [UpdateExisting] GetViewIdsForForm returned {(ids?.Count ?? 0)} id(s)");
                        if (ids != null)
                        {
                            foreach (var id in ids)
                            {
                                try { var vi = formsManager.GetView(id); if (vi != null) views.Add(vi); }
                                catch (Exception exv) { _log($"  [UpdateExisting] GetView({id}) failed: {exv.Message}"); }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _log($"  [UpdateExisting] GetViewIdsForForm failed: {ex.Message}");
                    }
                }

                if (views.Count == 0)
                {
                    _log($"  [UpdateExisting] No views found on existing form '{mapping.K2FormDisplayName}' - nothing to modify");
                    return result;
                }

                _log($"  [UpdateExisting] Existing form '{mapping.K2FormDisplayName}' has {views.Count} view(s): {string.Join(", ", views.Select(v => v.Name))}");

                // Flatten all InfoPath controls across views for matching.
                var infoPathControls = (infoPathDef.Views ?? new List<ViewDefinition>())
                    .Where(v => v?.Controls != null)
                    .SelectMany(v => v.Controls)
                    .Where(c => c != null)
                    .ToList();

                foreach (var view in views)
                {
                    try
                    {
                        UpdateSingleView(formsManager, view, infoPathControls, infoPathDef,
                            dynamicSections, conditionalVisibility,
                            createSmartBoxLookups, lookupSmoName, result, explicitFieldMap);
                    }
                    catch (Exception ex)
                    {
                        _log($"  [UpdateExisting] View '{view.Name}' update failed (skipped): {ex.Message}");
                        try { formsManager.UndoViewCheckOut(view.Guid); } catch { }
                    }
                }
            }
            finally
            {
                try { formsManager.Dispose(); } catch { }
            }

            return result;
        }

        private void UpdateSingleView(FormsManager formsManager, ViewInfo view,
            List<ControlDefinition> infoPathControls, InfoPathFormDefinition infoPathDef,
            JArray dynamicSections, JObject conditionalVisibility,
            bool wireLookups, string lookupSmoName, UpdateResult result,
            IDictionary<string, string> explicitFieldMap)
        {
            string viewXml = formsManager.GetViewDefinition(view.Guid);
            if (string.IsNullOrEmpty(viewXml))
            {
                _log($"    [View {view.Name}] empty definition - skipped");
                return;
            }

            // Dump the raw deployed view XML so the exact K2 schema can be inspected offline.
            try
            {
                string dumpDir = Path.Combine(Path.GetTempPath(), "FormGenerator_K2Views");
                Directory.CreateDirectory(dumpDir);
                string safe = Regex.Replace(view.Name ?? "view", "[^A-Za-z0-9_.-]", "_");
                string dumpPath = Path.Combine(dumpDir, safe + ".xml");
                File.WriteAllText(dumpPath, viewXml);
                _log($"    [View {view.Name}] dumped raw view XML ({viewXml.Length} chars) → {dumpPath}");
            }
            catch (Exception exDump)
            {
                _log($"    [View {view.Name}] could not dump view XML: {exDump.Message}");
            }

            var xdoc = new XmlDocument { PreserveWhitespace = false };
            xdoc.LoadXml(viewXml);

            // ── Component 3: match existing K2 controls to InfoPath controls ──
            var matcher = new ExistingViewControlMatcher(xdoc, infoPathControls, infoPathDef, explicitFieldMap);
            var matches = matcher.Match();

            // Structural + inventory diagnostics so matching failures are visible in the log.
            int rawControlCount = xdoc.GetElementsByTagName("Control").Count;
            int controlsPath = xdoc.SelectNodes("//Controls/Control")?.Count ?? 0;
            _log($"    [View {view.Name}] root=<{xdoc.DocumentElement?.Name}> rawControls={rawControlCount} //Controls/Control={controlsPath}");
            _log($"    [View {view.Name}] K2 candidates={matcher.K2Candidates.Count} infoPath={matcher.InfoPathNames.Count} matched={matches.Count}");
            if (matcher.K2Candidates.Count > 0)
                _log($"      K2: {string.Join(", ", matcher.K2Candidates.Take(50))}");
            if (matcher.InfoPathNames.Count > 0)
                _log($"      InfoPath: {string.Join(", ", matcher.InfoPathNames.Take(50))}");
            if (matches.Count > 0)
                _log($"      matched pairs (InfoPath@grid → K2): {string.Join(", ", matches.Select(m => $"{m.InfoPathControl.Label ?? m.InfoPathControl.Name}@{m.InfoPathControl.GridPosition}→{m.K2ControlName}"))}");

            bool changed = false;

            // ── Component 4: reposition matched controls to the InfoPath grid order (fail-safe) ──
            try
            {
                int moved = ControlRepositioner.Reposition(xdoc, matches);
                if (moved > 0) { result.ControlsRepositioned += moved; changed = true; }
                _log($"    [View {view.Name}] repositioned {moved} control(s)");
            }
            catch (Exception ex)
            {
                _log($"    [View {view.Name}] reposition skipped: {ex.Message}");
            }

            // ── Component 6 (wire): point matched dropdowns at the lookup SmartObject (fail-safe) ──
            if (wireLookups && !string.IsNullOrEmpty(lookupSmoName))
            {
                try
                {
                    int wired = DropdownLookupWirer.Wire(xdoc, matches, lookupSmoName);
                    if (wired > 0) { result.DropdownsWired += wired; changed = true; }
                    _log($"    [View {view.Name}] wired {wired} dropdown(s) to {lookupSmoName}");
                }
                catch (Exception ex)
                {
                    _log($"    [View {view.Name}] dropdown wiring skipped: {ex.Message}");
                }
            }

            string currentXml = xdoc.OuterXml;

            // ── Component 5: apply InfoPath rules to the (possibly modified) view XML ──
            string finalXml = currentXml;
            try
            {
                var builder = new ViewXmlBuilder(_connectionManager,
                    new Dictionary<string, Dictionary<string, FieldInfo>>(), null, _config, infoPathDef);

                var ctx = new ViewXmlBuilder.ViewRuleContext
                {
                    ViewName = view.Name,
                    ViewGuid = view.Guid.ToString(),
                    ControlIdMap = matcher.ControlIdMap,
                    ControlToFieldMap = matcher.ControlToFieldMap,
                    JsonToK2ControlIdMap = new Dictionary<string, string>(),
                    DynamicSections = dynamicSections,
                    ConditionalVisibility = conditionalVisibility,
                    Controls = null
                };

                string withRules = builder.ApplyRulesToDeployedView(finalXml, ctx);
                if (!string.IsNullOrEmpty(withRules) && !string.Equals(withRules, finalXml, StringComparison.Ordinal))
                {
                    finalXml = withRules;
                    changed = true;
                    result.RulesAppliedViews++;
                }
            }
            catch (Exception ex)
            {
                _log($"    [View {view.Name}] rule application skipped: {ex.Message}");
            }

            // Push harder: whenever we matched controls, always redeploy so the latest
            // InfoPath-driven layout/order is (re)applied even if change-detection is conservative.
            if (matches.Count > 0) changed = true;

            if (!changed)
            {
                _log($"    [View {view.Name}] no matched controls - not redeploying");
                return;
            }

            // ── Deploy: checkout → deploy (updates existing view by GUID) → check in ──
            try
            {
                formsManager.CheckOutView(view.Guid);
                formsManager.DeployViews(finalXml, view.CategoryPath, true);
                result.ViewsUpdated++;
                _log($"    [View {view.Name}] deployed");
            }
            catch (Exception ex)
            {
                _log($"    [View {view.Name}] deploy failed: {ex.Message}");
                try { formsManager.UndoViewCheckOut(view.Guid); } catch { }
            }
        }

        // ── Field-mapping support: read the existing form's data controls for the mapping UI ──

        private static readonly HashSet<string> ReaderLayoutTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Grid", "Row", "Cell", "Column", "Table", "Section", "Panel", "View", "Tab", "TabStrip"
        };

        /// <summary>
        /// Reads every data-bound control (those with a FieldID) from all views of an existing K2 form,
        /// so the field-mapping dialog can present them. Runs the K2 calls synchronously (call off the UI thread).
        /// </summary>
        public static List<K2FieldDescriptor> ReadFormControls(string host, uint port, Guid formGuid)
        {
            var result = new List<K2FieldDescriptor>();
            var fm = new FormsManager();
            try
            {
                if (!fm.Open(host, port)) return result;

                var views = new List<ViewInfo>();
                try { var ex = fm.GetViewsForForm(formGuid); if (ex?.Views != null) views = ex.Views.Cast<ViewInfo>().Where(v => v != null).ToList(); }
                catch { }
                if (views.Count == 0)
                {
                    try { var ids = fm.GetViewIdsForForm(formGuid); if (ids != null) foreach (var id in ids) { try { var vi = fm.GetView(id); if (vi != null) views.Add(vi); } catch { } } }
                    catch { }
                }

                foreach (var v in views)
                {
                    string xml;
                    try { xml = fm.GetViewDefinition(v.Guid); } catch { continue; }
                    if (string.IsNullOrEmpty(xml)) continue;

                    var doc = new XmlDocument();
                    try { doc.LoadXml(xml); } catch { continue; }

                    var fdisp = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    var fint = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (XmlElement fld in doc.SelectNodes("//Fields/Field")?.OfType<XmlElement>() ?? Enumerable.Empty<XmlElement>())
                    {
                        string fid = fld.GetAttribute("ID");
                        if (string.IsNullOrEmpty(fid)) continue;
                        string d = ReaderChildText(fld, "FieldDisplayName") ?? ReaderChildText(fld, "Name");
                        string i = ReaderChildText(fld, "FieldName");
                        if (!string.IsNullOrEmpty(d)) fdisp[fid] = d;
                        if (!string.IsNullOrEmpty(i)) fint[fid] = ReaderDecodeSp(i);
                    }

                    foreach (XmlElement ctrl in doc.SelectNodes("//Controls/Control")?.OfType<XmlElement>() ?? Enumerable.Empty<XmlElement>())
                    {
                        string type = ctrl.GetAttribute("Type");
                        string id = ctrl.GetAttribute("ID");
                        string fieldId = ctrl.GetAttribute("FieldID");
                        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(fieldId)) continue;
                        if (!string.IsNullOrEmpty(type) && ReaderLayoutTypes.Contains(type)) continue;

                        fdisp.TryGetValue(fieldId, out var dn);
                        fint.TryGetValue(fieldId, out var iname);
                        result.Add(new K2FieldDescriptor
                        {
                            ViewName = v.Name,
                            ControlId = id,
                            ControlName = ReaderChildText(ctrl, "Name"),
                            FieldId = fieldId,
                            FieldName = iname,
                            FieldDisplayName = dn,
                            Type = type
                        });
                    }
                }
            }
            finally { try { fm.Dispose(); } catch { } }
            return result;
        }

        private static string ReaderChildText(XmlElement el, string childName)
        {
            var n = el?.SelectSingleNode(childName);
            return n != null && !string.IsNullOrWhiteSpace(n.InnerText) ? n.InnerText.Trim() : null;
        }

        private static string ReaderDecodeSp(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return Regex.Replace(s, "_x([0-9A-Fa-f]{4})_", m =>
            {
                try { return ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString(); }
                catch { return " "; }
            });
        }
    }

    /// <summary>
    /// A data-bound control on an existing K2 form, surfaced to the field-mapping dialog.
    /// </summary>
    public class K2FieldDescriptor
    {
        public string ViewName { get; set; }
        public string ControlId { get; set; }
        public string ControlName { get; set; }
        public string FieldId { get; set; }
        public string FieldName { get; set; }
        public string FieldDisplayName { get; set; }
        public string Type { get; set; }

        public string Display => $"{FieldDisplayName ?? ControlName} [{Type}] · {ViewName}";
        public override string ToString() => Display;
    }

    /// <summary>
    /// One matched control: an existing K2 view control element paired with the InfoPath control
    /// it corresponds to.
    /// </summary>
    internal class ControlMatch
    {
        public XmlElement K2Control;
        public string K2ControlId;
        public string K2ControlName;
        public string K2ControlType;
        public string K2FieldId;
        public string K2FieldName;
        public ControlDefinition InfoPathControl;
    }

    /// <summary>
    /// Component 3 — heuristic matcher between an existing K2 view's controls and the InfoPath
    /// control definitions, by control name / display label / field-binding leaf.
    /// </summary>
    internal class ExistingViewControlMatcher
    {
        // K2 layout/container control types that are not field controls.
        private static readonly HashSet<string> LayoutTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Grid", "Row", "Cell", "Column", "Table", "Section", "Panel", "View", "Tab", "TabStrip"
        };

        private readonly XmlDocument _doc;
        private readonly List<ControlDefinition> _infoPathControls;
        private readonly InfoPathFormDefinition _infoPathDef;
        private readonly IDictionary<string, string> _explicitMap; // InfoPath control name → K2 control ID

        public Dictionary<string, string> ControlIdMap { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> ControlToFieldMap { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Diagnostics: what we found vs what we tried to match.
        public List<string> K2Candidates { get; } = new List<string>();
        public List<string> InfoPathNames { get; } = new List<string>();

        public ExistingViewControlMatcher(XmlDocument doc, List<ControlDefinition> infoPathControls,
            InfoPathFormDefinition infoPathDef, IDictionary<string, string> explicitMap = null)
        {
            _doc = doc;
            _infoPathControls = infoPathControls ?? new List<ControlDefinition>();
            _infoPathDef = infoPathDef;
            _explicitMap = explicitMap;
        }

        // Trailing K2 control-name noise to strip so "Asset ID Text Box" → "ASSETID".
        private static readonly string[] NameSuffixes =
        {
            "TEXTBOX", "DROPDOWN", "DROPDOWNLIST", "COMBOBOX", "CALENDAR", "DATEPICKER", "PICKER",
            "CHECKBOX", "TEXTAREA", "RADIOBUTTON", "LISTBOX", "LISTVIEW", "HYPERLINK", "LABEL"
        };

        public List<ControlMatch> Match()
        {
            var matches = new List<ControlMatch>();

            // 1. Map FieldID → field display name AND internal name (the SharePoint column names).
            var fieldNameById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var fieldInternalById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (XmlElement fld in _doc.SelectNodes("//Fields/Field")?.OfType<XmlElement>() ?? Enumerable.Empty<XmlElement>())
            {
                string fid = fld.GetAttribute("ID");
                if (string.IsNullOrEmpty(fid)) continue;
                string fdisplay = ChildText(fld, "FieldDisplayName") ?? ChildText(fld, "Name");
                string finternal = ChildText(fld, "FieldName");
                if (!string.IsNullOrEmpty(fdisplay)) fieldNameById[fid] = fdisplay;
                if (!string.IsNullOrEmpty(finternal)) fieldInternalById[fid] = DecodeSpInternalName(finternal);
            }

            // 2. Index InfoPath controls by their normalized match keys.
            var ipByKey = new Dictionary<string, ControlDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var ip in _infoPathControls)
                foreach (var key in InfoPathKeys(ip))
                    if (!string.IsNullOrEmpty(key) && !ipByKey.ContainsKey(key)) ipByKey[key] = ip;

            InfoPathNames.AddRange(_infoPathControls
                .Select(c => $"{c.Name ?? c.Label}[{c.Type}]")
                .Where(s => !string.IsNullOrWhiteSpace(s)));

            var usedInfoPath = new HashSet<ControlDefinition>();
            var usedK2Ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 2b. Seed user-confirmed mappings first (InfoPath control name → K2 control ID).
            //     Explicit pairings always win over auto-matching.
            if (_explicitMap != null && _explicitMap.Count > 0)
            {
                var ipByName = new Dictionary<string, ControlDefinition>(StringComparer.OrdinalIgnoreCase);
                foreach (var ip in _infoPathControls)
                {
                    if (!string.IsNullOrEmpty(ip.Name) && !ipByName.ContainsKey(ip.Name)) ipByName[ip.Name] = ip;
                    if (!string.IsNullOrEmpty(ip.Label) && !ipByName.ContainsKey(ip.Label)) ipByName[ip.Label] = ip;
                }

                foreach (var kv in _explicitMap)
                {
                    if (string.IsNullOrEmpty(kv.Value)) continue;            // unmapped
                    if (!ipByName.TryGetValue(kv.Key, out var ip) || usedInfoPath.Contains(ip)) continue;

                    var ctrl = _doc.SelectSingleNode($"//Controls/Control[@ID='{kv.Value}']") as XmlElement;
                    if (ctrl == null) continue;                              // belongs to another view

                    string fieldId = ctrl.GetAttribute("FieldID");
                    fieldNameById.TryGetValue(fieldId ?? string.Empty, out var fname);

                    usedInfoPath.Add(ip);
                    usedK2Ids.Add(kv.Value);
                    matches.Add(new ControlMatch
                    {
                        K2Control = ctrl,
                        K2ControlId = kv.Value,
                        K2ControlName = ChildText(ctrl, "Name"),
                        K2ControlType = ctrl.GetAttribute("Type"),
                        K2FieldId = fieldId,
                        K2FieldName = fname,
                        InfoPathControl = ip
                    });

                    if (!string.IsNullOrEmpty(ip.Name)) ControlIdMap[ip.Name] = kv.Value;
                    string leaf = BindingLeaf(ip.Binding);
                    if (!string.IsNullOrEmpty(leaf)) ControlIdMap[leaf] = kv.Value;
                    if (!string.IsNullOrEmpty(ip.Label)) ControlIdMap[ip.Label] = kv.Value;
                    ControlToFieldMap[kv.Value] = ip.Name ?? leaf;
                }
            }

            // 3. Candidate K2 controls: data-bound controls (have a FieldID), non-layout.
            foreach (XmlElement ctrl in (_doc.SelectNodes("//Controls/Control")?.OfType<XmlElement>() ?? Enumerable.Empty<XmlElement>()))
            {
                string type = ctrl.GetAttribute("Type");
                string id = ctrl.GetAttribute("ID");
                string fieldId = ctrl.GetAttribute("FieldID");
                if (string.IsNullOrEmpty(id)) continue;
                if (!string.IsNullOrEmpty(type) && LayoutTypes.Contains(type)) continue;
                if (string.IsNullOrEmpty(fieldId)) continue; // only data-bound controls
                if (usedK2Ids.Contains(id)) continue;        // already claimed by explicit mapping

                string ctrlName = ChildText(ctrl, "Name");
                fieldNameById.TryGetValue(fieldId, out var fieldName);
                fieldInternalById.TryGetValue(fieldId, out var fieldInternal);
                K2Candidates.Add($"{ctrlName}|{fieldName}|{fieldInternal}[{type}]");

                // K2 keys: SP internal name + field display name (both map back to InfoPath),
                // then the control name with its type-suffix stripped.
                var keys = new List<string>();
                if (!string.IsNullOrEmpty(fieldInternal)) keys.Add(Normalize(fieldInternal));
                if (!string.IsNullOrEmpty(fieldName)) keys.Add(Normalize(fieldName));
                if (!string.IsNullOrEmpty(ctrlName)) { keys.Add(Normalize(ctrlName)); keys.Add(StripSuffix(Normalize(ctrlName))); }

                ControlDefinition ip = null;
                foreach (var key in keys)
                {
                    if (!string.IsNullOrEmpty(key) && ipByKey.TryGetValue(key, out var found) && !usedInfoPath.Contains(found))
                    {
                        ip = found;
                        break;
                    }
                }
                if (ip == null) continue;

                usedInfoPath.Add(ip);
                matches.Add(new ControlMatch
                {
                    K2Control = ctrl,
                    K2ControlId = id,
                    K2ControlName = ctrlName,
                    K2ControlType = type,
                    K2FieldId = fieldId,
                    K2FieldName = fieldName,
                    InfoPathControl = ip
                });

                // Populate the maps the rule resolver uses (InfoPath-side keys → K2 control GUID).
                if (!string.IsNullOrEmpty(ip.Name)) ControlIdMap[ip.Name] = id;
                string leaf = BindingLeaf(ip.Binding);
                if (!string.IsNullOrEmpty(leaf)) ControlIdMap[leaf] = id;
                if (!string.IsNullOrEmpty(ip.Label)) ControlIdMap[ip.Label] = id;
                ControlToFieldMap[id] = ip.Name ?? leaf ?? ctrlName;
            }

            return matches;
        }

        private IEnumerable<string> InfoPathKeys(ControlDefinition ip)
        {
            if (!string.IsNullOrEmpty(ip.Name)) yield return Normalize(ip.Name);
            if (!string.IsNullOrEmpty(ip.Label)) yield return Normalize(ip.Label);
            string leaf = BindingLeaf(ip.Binding);
            if (!string.IsNullOrEmpty(leaf)) yield return Normalize(leaf);
            if (_infoPathDef?.InfoPathToK2NameMap != null && !string.IsNullOrEmpty(leaf) &&
                _infoPathDef.InfoPathToK2NameMap.TryGetValue(leaf, out var mapped) && !string.IsNullOrEmpty(mapped))
            {
                yield return Normalize(mapped);
            }
        }

        private static string ChildText(XmlElement el, string childName)
        {
            var n = el?.SelectSingleNode(childName);
            return n != null && !string.IsNullOrWhiteSpace(n.InnerText) ? n.InnerText.Trim() : null;
        }

        private static string BindingLeaf(string binding)
        {
            if (string.IsNullOrEmpty(binding)) return null;
            var parts = binding.Split(new[] { '/', '\\', ':' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts[parts.Length - 1] : binding;
        }

        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return Regex.Replace(s, "[^A-Za-z0-9]", "").ToUpperInvariant();
        }

        // SharePoint encodes special characters in internal names as _xHHHH_ (e.g. space = _x0020_).
        private static string DecodeSpInternalName(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return Regex.Replace(s, "_x([0-9A-Fa-f]{4})_", m =>
            {
                try { return ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString(); }
                catch { return " "; }
            });
        }

        private static string StripSuffix(string normalized)
        {
            if (string.IsNullOrEmpty(normalized)) return normalized;
            foreach (var suffix in NameSuffixes)
                if (normalized.Length > suffix.Length && normalized.EndsWith(suffix, StringComparison.Ordinal))
                    return normalized.Substring(0, normalized.Length - suffix.Length);
            return normalized;
        }
    }

    /// <summary>
    /// Component 4 — repositions matched controls to reflect the InfoPath grid order. Operates on
    /// the primary grid layout control and reorders its rows so matched controls follow the
    /// InfoPath top-to-bottom order. Fails safe: returns 0 (no change) if the grid shape is not
    /// the expected Rows/Row/Cells/Cell structure.
    /// </summary>
    internal static class ControlRepositioner
    {
        public static int Reposition(XmlDocument doc, List<ControlMatch> matches)
        {
            if (matches == null || matches.Count == 0) return 0;

            // Locate the primary grid layout control.
            var grid = doc.SelectSingleNode("//Controls/Control[@LayoutType='Grid']") as XmlElement
                       ?? doc.SelectSingleNode("//Control[@LayoutType='Grid']") as XmlElement;
            if (grid == null) return 0;

            var rowsNode = grid.SelectSingleNode("Rows") as XmlElement;
            if (rowsNode == null) return 0;

            var rowEls = rowsNode.SelectNodes("Row")?.OfType<XmlElement>().ToList();
            if (rowEls == null || rowEls.Count <= 1) return 0;

            // Map control GUID → desired InfoPath order index (top-to-bottom, left-to-right).
            var ordered = matches
                .Where(m => m.InfoPathControl != null)
                .OrderBy(m => GridRow(m.InfoPathControl.GridPosition))
                .ThenBy(m => GridCol(m.InfoPathControl.GridPosition))
                .ToList();
            var desiredOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < ordered.Count; i++)
            {
                if (!desiredOrder.ContainsKey(ordered[i].K2ControlId))
                    desiredOrder[ordered[i].K2ControlId] = i;
            }
            if (desiredOrder.Count == 0) return 0;

            // For each row, find the smallest desired index among the control refs it contains.
            // Rows with no matched control keep their original relative position at the end.
            int originalIndex = 0;
            var keyed = new List<(XmlElement Row, int Rank, int Orig)>();
            foreach (var row in rowEls)
            {
                int best = int.MaxValue;
                foreach (XmlElement refCtrl in row.SelectNodes(".//Control")?.OfType<XmlElement>() ?? Enumerable.Empty<XmlElement>())
                {
                    string refId = refCtrl.GetAttribute("ID");
                    if (!string.IsNullOrEmpty(refId) && desiredOrder.TryGetValue(refId, out int rank))
                        best = Math.Min(best, rank);
                }
                keyed.Add((row, best, originalIndex++));
            }

            // Nothing to reorder if no row contains a matched control.
            if (keyed.All(k => k.Rank == int.MaxValue)) return 0;

            var sorted = keyed
                .OrderBy(k => k.Rank == int.MaxValue ? int.MaxValue : k.Rank)
                .ThenBy(k => k.Orig)
                .Select(k => k.Row)
                .ToList();

            // If order is unchanged, report no movement.
            bool orderChanged = !sorted.SequenceEqual(rowEls);
            if (!orderChanged) return 0;

            foreach (var row in sorted)
                rowsNode.AppendChild(row); // AppendChild moves the existing node to the end in order.

            return desiredOrder.Count;
        }

        private static int GridRow(string gridPos)
        {
            if (string.IsNullOrEmpty(gridPos)) return int.MaxValue;
            var m = Regex.Match(gridPos, "\\d+");
            return m.Success && int.TryParse(m.Value, out int r) ? r : int.MaxValue;
        }

        private static int GridCol(string gridPos)
        {
            if (string.IsNullOrEmpty(gridPos)) return int.MaxValue;
            var m = Regex.Match(gridPos, "[A-Za-z]+");
            if (!m.Success) return int.MaxValue;
            int col = 0;
            foreach (char c in m.Value.ToUpperInvariant()) col = col * 26 + (c - 'A' + 1);
            return col;
        }
    }

    /// <summary>
    /// Component 6 (wire) — points matched dropdown controls at the consolidated SmartBox lookup
    /// SmartObject. Best-effort: tags the control so a later pass / K2 designer can bind the list
    /// source. Fails safe per control.
    /// </summary>
    internal static class DropdownLookupWirer
    {
        private static readonly HashSet<string> DropdownTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DropDown", "DropDownList", "ListBox", "ComboBox", "AutoComplete"
        };

        public static int Wire(XmlDocument doc, List<ControlMatch> matches, string lookupSmoName)
        {
            int wired = 0;
            foreach (var m in matches)
            {
                bool isDropdown = (m.K2ControlType != null && DropdownTypes.Contains(m.K2ControlType))
                    || string.Equals(m.InfoPathControl?.Type, "DropDown", StringComparison.OrdinalIgnoreCase);
                if (!isDropdown) continue;

                string field = m.InfoPathControl?.Name ?? m.K2ControlName;
                // Record the intended lookup binding on the control so the binding can be applied.
                // (Full Source/Field wiring is finalized against the live view schema.)
                m.K2Control.SetAttribute("LookupSmartObject", lookupSmoName);
                m.K2Control.SetAttribute("LookupType", field ?? string.Empty);
                wired++;
            }
            return wired;
        }
    }
}
