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
using SourceCode.SmartObjects.Management;
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
            IDictionary<string, string> explicitFieldMap = null,
            IDictionary<string, K2ExistingSectionMapping> sectionMappings = null)
        {
            var result = new UpdateResult();
            if (mapping == null || infoPathDef == null)
            {
                _log("  [UpdateExisting] Missing mapping or InfoPath definition - nothing to do");
                return result;
            }

            // Parse the InfoPath model from the form JSON: views/controls, data, and visibility rules.
            JArray dynamicSections = null;
            JObject conditionalVisibility = null;
            JArray viewsArray = null;
            JArray dataArray = null;
            try
            {
                var jo = JObject.Parse(jsonContent);
                var fd = jo.Properties().First().Value?["FormDefinition"];
                dynamicSections = fd?["DynamicSections"] as JArray;
                conditionalVisibility = fd?["ConditionalVisibility"] as JObject;
                viewsArray = fd?["Views"] as JArray;
                dataArray = fd?["Data"] as JArray;
            }
            catch { /* tolerate */ }

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

                // Retry once on a fresh FormsManager — the SmartObject operations during lookup creation
                // can leave the form/view catalog query briefly returning nothing.
                if (views.Count == 0)
                {
                    _log("  [UpdateExisting] 0 views - retrying with a fresh FormsManager...");
                    try { formsManager.Dispose(); } catch { }
                    System.Threading.Thread.Sleep(1500);
                    formsManager = new FormsManager();
                    if (formsManager.Open(host, port))
                    {
                        try
                        {
                            var ex2 = formsManager.GetViewsForForm(mapping.K2FormGuid);
                            if (ex2?.Views != null) views = ex2.Views.Cast<ViewInfo>().Where(v => v != null).ToList();
                        }
                        catch { }
                        if (views.Count == 0)
                        {
                            try
                            {
                                var ids = formsManager.GetViewIdsForForm(mapping.K2FormGuid);
                                if (ids != null) foreach (var id in ids) { try { var vi = formsManager.GetView(id); if (vi != null) views.Add(vi); } catch { } }
                            }
                            catch { }
                        }
                        _log($"  [UpdateExisting] retry resolved {views.Count} view(s)");
                    }
                }

                // NOTE: do NOT bail when there are 0 existing views — the repeating-section item/list
                // pair generation is independent of the existing form's views and must still run.
                if (views.Count > 0)
                    _log($"  [UpdateExisting] Existing form '{mapping.K2FormDisplayName}' has {views.Count} view(s): {string.Join(", ", views.Select(v => v.Name))}");
                else
                    _log("  [UpdateExisting] No existing views resolved - will still generate repeating-section views");

                // Reconnect (lookup creation above disconnects) so dropdown lookup resolution works.
                try { _connectionManager.Connect(); } catch { }
                var smoGen = new SmartObjectGenerator(_connectionManager, _config);

                // One-time: dump every existing view's XML so list-view structure can be templated.
                try
                {
                    string dumpDir = Path.Combine(Path.GetTempPath(), "FormGenerator_K2Views");
                    Directory.CreateDirectory(dumpDir);
                    foreach (var v in views)
                    {
                        try
                        {
                            string vx = formsManager.GetViewDefinition(v.Guid);
                            if (string.IsNullOrEmpty(vx)) continue;
                            string vp = Path.Combine(dumpDir, "_VIEW_" + Regex.Replace(v.Name ?? v.Guid.ToString(), "[^A-Za-z0-9_.-]", "_") + ".xml");
                            File.WriteAllText(vp, vx);
                        }
                        catch { }
                    }
                }
                catch { }

                foreach (var view in views)
                {
                    // Rebuild only capture/data-entry views; skip SharePoint attachment/list views.
                    string vname = view.Name ?? string.Empty;
                    bool isAttachmentOrList = vname.IndexOf("Attachment", StringComparison.OrdinalIgnoreCase) >= 0
                        || vname.EndsWith("_List", StringComparison.OrdinalIgnoreCase)
                        || (view.Type.ToString().IndexOf("List", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (isAttachmentOrList)
                    {
                        _log($"    [View {vname}] skipped (attachment/list view)");
                        continue;
                    }

                    try
                    {
                        RebuildView(formsManager, view, viewsArray, dataArray,
                            dynamicSections, conditionalVisibility, infoPathDef,
                            explicitFieldMap, smoGen, result);
                    }
                    catch (Exception ex)
                    {
                        _log($"  [UpdateExisting] View '{view.Name}' update failed (skipped): {ex.Message}");
                        try { formsManager.UndoViewCheckOut(view.Guid); } catch { }
                    }
                }

                // ── Phase B groundwork: detect repeating sections and dump the existing form XML ──
                // so the item/list-pair insertion can be implemented against the real form schema.
                try
                {
                    InspectRepeatingSectionsAndForm(formsManager, mapping, viewsArray, infoPathDef);
                }
                catch (Exception ex)
                {
                    _log($"  [UpdateExisting] repeating-section inspection failed (continuing): {ex.Message}");
                }

                // Phase B step 1: generate the item/list view pairs bound to the mapped child SmartObjects.
                List<(string Section, string ItemView, string ListView, int GridRow)> generatedPairs = null;
                try
                {
                    generatedPairs = GenerateRepeatingSectionViews(mapping, viewsArray, dataArray, infoPathDef, explicitFieldMap, sectionMappings, smoGen, result);
                }
                catch (Exception ex)
                {
                    _log($"  [RepeatingSections] view-pair generation failed (continuing): {ex.Message}");
                }

                // Phase B step 2: insert the generated pairs into the existing form as new areas.
                try
                {
                    InsertRepeatingPairsIntoForm(formsManager, mapping, generatedPairs);
                }
                catch (Exception ex)
                {
                    _log($"  [FormInsert] failed (continuing): {ex.Message}");
                }
            }
            finally
            {
                try { formsManager.Dispose(); } catch { }
            }

            return result;
        }

        /// <summary>
        /// Phase B groundwork: logs the InfoPath repeating sections (and the child SmartObject name
        /// we'd bind/create per the naming convention) and dumps the existing K2 form's XML so the
        /// item/list view-pair insertion can be built against the real form schema.
        /// </summary>
        private void InspectRepeatingSectionsAndForm(FormsManager formsManager, K2ExistingFormMapping mapping,
            JArray viewsArray, InfoPathFormDefinition infoPathDef)
        {
            // Repeating sections from the InfoPath JSON (controls with a RepeatingSectionName).
            var sections = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (JObject view in (viewsArray?.OfType<JObject>() ?? Enumerable.Empty<JObject>()))
            {
                foreach (JObject c in ((view["Controls"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>()))
                {
                    string sec = c["RepeatingSectionName"]?.Value<string>();
                    bool isRep = c["IsInRepeatingSection"]?.Value<bool>() ?? false;
                    if (string.IsNullOrWhiteSpace(sec)) continue;
                    if (!isRep && !string.Equals(c["Type"]?.Value<string>(), "RepeatingTable", StringComparison.OrdinalIgnoreCase)) continue;
                    sections.TryGetValue(sec, out int n);
                    sections[sec] = n + 1;
                }
            }

            string formName = NameSanitizer.SanitizeSmartObjectName(infoPathDef?.FormName ?? mapping.K2FormName);
            if (sections.Count == 0)
                _log("  [RepeatingSections] none detected in the InfoPath form");
            else
                foreach (var s in sections)
                    _log($"  [RepeatingSections] '{s.Key}' ({s.Value} control(s)) → child SmartObject would be '{formName}_{NameSanitizer.SanitizeSmartObjectName(s.Key)}'");

            // Dump the existing form XML for offline schema inspection (mirrors the view dump).
            try
            {
                string formXml = formsManager.GetFormDefinition(mapping.K2FormGuid);
                if (!string.IsNullOrEmpty(formXml))
                {
                    string dumpDir = Path.Combine(Path.GetTempPath(), "FormGenerator_K2Views");
                    Directory.CreateDirectory(dumpDir);
                    string path = Path.Combine(dumpDir, "_FORM_" + Regex.Replace(mapping.K2FormName ?? "form", "[^A-Za-z0-9_.-]", "_") + ".xml");
                    File.WriteAllText(path, formXml);
                    _log($"  [RepeatingSections] dumped existing form XML ({formXml.Length} chars) → {path}");
                }
            }
            catch (Exception ex)
            {
                _log($"  [RepeatingSections] could not dump form XML: {ex.Message}");
            }
        }

        /// <summary>
        /// Phase B step 1: for each InfoPath repeating section, resolve the target child SmartObject
        /// from the per-section mapping (bind to an existing SmartObject, or create a SmartBox child if
        /// flagged), then generate + deploy an item/list view pair bound to it.
        /// </summary>
        private List<(string Section, string ItemView, string ListView, int GridRow)> GenerateRepeatingSectionViews(
            K2ExistingFormMapping mapping, JArray viewsArray, JArray dataArray,
            InfoPathFormDefinition infoPathDef, IDictionary<string, string> explicitFieldMap,
            IDictionary<string, K2ExistingSectionMapping> sectionMappings,
            SmartObjectGenerator smoGen, UpdateResult result)
        {
            var pairs = new List<(string Section, string ItemView, string ListView, int GridRow)>();
            string formName = NameSanitizer.SanitizeSmartObjectName(infoPathDef?.FormName ?? mapping.K2FormName);
            string targetFolder = _config.Form?.TargetFolder ?? "Generated";
            string viewCategory = $"{targetFolder}\\{formName}\\Views";

            foreach (JObject ipView in (viewsArray?.OfType<JObject>() ?? Enumerable.Empty<JObject>()))
            {
                string ipViewName = (ipView["ViewName"]?.Value<string>() ?? "View").Replace(".xsl", "");
                var ctrls = (ipView["Controls"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>();

                var bySection = new Dictionary<string, List<JObject>>(StringComparer.OrdinalIgnoreCase);
                foreach (var c in ctrls)
                {
                    string sec = c["RepeatingSectionName"]?.Value<string>();
                    if (string.IsNullOrWhiteSpace(sec)) continue;
                    if (!(c["IsInRepeatingSection"]?.Value<bool>() ?? false)) continue;
                    if (string.Equals(c["Type"]?.Value<string>(), "RepeatingTable", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!bySection.TryGetValue(sec, out var list)) { list = new List<JObject>(); bySection[sec] = list; }
                    list.Add(c);
                }

                foreach (var kv in bySection)
                {
                    string sectionName = kv.Key;
                    var sectionControls = kv.Value;

                    string childSmoName = null;
                    var fieldByControl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    K2ExistingSectionMapping secMap = null;
                    sectionMappings?.TryGetValue(sectionName, out secMap);

                    if (secMap != null)
                    {
                        foreach (var f in secMap.Fields) fieldByControl[f.Key] = f.Value;

                        if (secMap.CreateIfMissing)
                        {
                            // Reuse the from-scratch path: create a SmartBox child SmartObject (works with
                            // K2's list-view generator, unlike SharePoint-list SmartObjects).
                            try
                            {
                                var sectionData = GetSectionDataFields(dataArray, sectionName);
                                childSmoName = smoGen.EnsureChildSmartObject(formName, sectionName, sectionData, targetFolder);
                                _log($"  [RepeatingSections] '{sectionName}': SmartBox child ready '{childSmoName}' (create-if-missing)");
                                // Bind controls to the created columns (sanitized binding/name).
                                fieldByControl.Clear();
                                foreach (var c in sectionControls)
                                {
                                    string cname = c["Name"]?.Value<string>();
                                    if (string.IsNullOrEmpty(cname)) continue;
                                    string col = NameSanitizer.SanitizePropertyName(
                                        NameSanitizer.ExtractFieldNameFromBinding(c["Binding"]?.Value<string>()) ?? cname);
                                    if (!string.IsNullOrEmpty(col)) fieldByControl[cname] = col;
                                }
                            }
                            catch (Exception ex)
                            {
                                _log($"  [RepeatingSections] '{sectionName}': child SmartObject create failed: {ex.Message}");
                                continue;
                            }
                        }
                        else
                        {
                            childSmoName = secMap.SmoName;
                        }
                    }
                    else if (explicitFieldMap != null && explicitFieldMap.Count > 0)
                    {
                        // Fallback: derive the child SmartObject from the SMO::smo::field map.
                        var smoVotes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        foreach (var c in sectionControls)
                        {
                            string cname = c["Name"]?.Value<string>();
                            if (string.IsNullOrEmpty(cname)) continue;
                            if (!explicitFieldMap.TryGetValue(cname, out var val) || string.IsNullOrEmpty(val)) continue;
                            if (!val.StartsWith("SMO::", StringComparison.Ordinal)) continue;
                            var parts = val.Substring("SMO::".Length).Split(new[] { "::" }, StringSplitOptions.None);
                            if (parts.Length < 2) continue;
                            smoVotes[parts[0]] = smoVotes.TryGetValue(parts[0], out var n) ? n + 1 : 1;
                            fieldByControl[cname] = parts[1];
                        }
                        if (smoVotes.Count > 0) childSmoName = smoVotes.OrderByDescending(x => x.Value).First().Key;
                    }

                    if (string.IsNullOrEmpty(childSmoName))
                    {
                        _log($"  [RepeatingSections] '{sectionName}': no SmartObject mapping - skipping (use 'Map Sections')");
                        continue;
                    }

                    // Binding bridge for the item view: InfoPath control keys -> child column.
                    var inner = new Dictionary<string, FieldInfo>(StringComparer.OrdinalIgnoreCase);
                    foreach (var c in sectionControls)
                    {
                        string cname = c["Name"]?.Value<string>();
                        if (cname == null || !fieldByControl.TryGetValue(cname, out var field) || string.IsNullOrEmpty(field)) continue;
                        var fi = new FieldInfo
                        {
                            FieldGuid = Guid.NewGuid().ToString(),
                            FieldName = field,
                            DisplayName = field,
                            DataType = MapInfoPathToK2DataType(c["Type"]?.Value<string>())
                        };
                        AddSmoKey(inner, cname, fi);
                        AddSmoKey(inner, NameSanitizer.ExtractFieldNameFromBinding(c["Binding"]?.Value<string>()), fi);
                        AddSmoKey(inner, field, fi);
                    }

                    var smoFieldMappings = new Dictionary<string, Dictionary<string, FieldInfo>> { [childSmoName] = inner };

                    try
                    {
                        var vg = new ViewGenerator(_connectionManager, smoFieldMappings, smoGen, _config, infoPathDef);
                        var sectionArray = new JArray(sectionControls.Cast<object>().ToArray());
                        var res = vg.GenerateChildSectionViewPair(formName, ipViewName, sectionName, childSmoName,
                            sectionArray, dataArray ?? new JArray(), viewCategory);
                        result.ViewsUpdated += 2;
                        pairs.Add((sectionName, res.ItemView, res.ListView, res.GridRow));
                        _log($"  [RepeatingSections] '{sectionName}' → child '{childSmoName}': item '{res.ItemView}' + list '{res.ListView}' generated (gridRow {res.GridRow})");
                    }
                    catch (Exception ex)
                    {
                        _log($"  [RepeatingSections] '{sectionName}' view-pair generation failed: {ex.Message}");
                    }
                }
            }

            return pairs;
        }

        /// <summary>
        /// Phase B step 2: insert each generated item/list view pair into the existing K2 form as new
        /// areas (after the main view), so the repeating sections appear on the form. Fail-safe.
        /// </summary>
        private void InsertRepeatingPairsIntoForm(FormsManager fm, K2ExistingFormMapping mapping,
            List<(string Section, string ItemView, string ListView, int GridRow)> pairs)
        {
            if (pairs == null || pairs.Count == 0) { _log("  [FormInsert] no pairs to insert"); return; }

            string formXml;
            try { formXml = fm.GetFormDefinition(mapping.K2FormGuid); }
            catch (Exception ex) { _log($"  [FormInsert] could not read form: {ex.Message}"); return; }
            if (string.IsNullOrEmpty(formXml)) { _log("  [FormInsert] empty form definition"); return; }

            var doc = new XmlDocument();
            try { doc.LoadXml(formXml); } catch (Exception ex) { _log($"  [FormInsert] form XML parse failed: {ex.Message}"); return; }

            var panels = doc.SelectSingleNode("//Panels");
            var form = panels?.ParentNode as XmlElement;
            var controls = form?.SelectSingleNode("Controls") as XmlElement;
            var areas = panels?.SelectSingleNode("Panel/Areas") as XmlElement;
            if (controls == null || areas == null) { _log("  [FormInsert] form Controls/Areas not found - cannot insert"); return; }

            // Resolve view GUIDs and build the insertion list: list view then item view per section, ordered by grid row.
            var toInsert = new List<(string ViewName, string ViewGuid)>();
            foreach (var p in pairs.OrderBy(p => p.GridRow))
            {
                foreach (var vn in new[] { p.ListView, p.ItemView })
                {
                    if (string.IsNullOrEmpty(vn) || vn == "(list failed)") continue;
                    string vguid = null;
                    try { vguid = fm.GetView(vn)?.Guid.ToString(); } catch { }
                    if (string.IsNullOrEmpty(vguid)) { _log($"  [FormInsert] could not resolve view GUID for '{vn}' - skipping"); continue; }
                    if (areas.SelectSingleNode($".//Item[@ViewID='{vguid}']") != null) { _log($"  [FormInsert] '{vn}' already on form"); continue; }
                    toInsert.Add((vn, vguid));
                }
            }
            if (toInsert.Count == 0) { _log("  [FormInsert] nothing new to insert"); return; }

            // Insert after the first existing area (the main view area) so sections sit below the main fields.
            XmlNode anchor = areas.SelectSingleNode("Area");
            foreach (var v in toInsert)
            {
                string areaGuid = Guid.NewGuid().ToString();
                string areaItemGuid = Guid.NewGuid().ToString();

                controls.AppendChild(MakeFormControlDef(doc, areaGuid, "Area", "Area_" + v.ViewName));
                controls.AppendChild(MakeFormControlDef(doc, areaItemGuid, "AreaItem", v.ViewName));

                var areaEl = doc.CreateElement("Area");
                areaEl.SetAttribute("ID", areaGuid);
                var itemsEl = doc.CreateElement("Items");
                var itemEl = doc.CreateElement("Item");
                itemEl.SetAttribute("ID", areaItemGuid);
                itemEl.SetAttribute("ViewID", v.ViewGuid);
                itemEl.SetAttribute("ViewName", v.ViewName);
                itemEl.SetAttribute("ViewDisplayName", v.ViewName);
                AddChild(doc, itemEl, "Name", v.ViewName);
                AddChild(doc, itemEl, "DisplayName", v.ViewName);
                itemsEl.AppendChild(itemEl);
                areaEl.AppendChild(itemsEl);

                if (anchor != null) { areas.InsertAfter(areaEl, anchor); anchor = areaEl; }
                else areas.AppendChild(areaEl);
            }

            try
            {
                try { fm.CheckOutForm(mapping.K2FormGuid); } catch { }
                fm.DeployForms(doc.OuterXml, true);
                _log($"  [FormInsert] inserted {toInsert.Count} view area(s) into form '{mapping.K2FormDisplayName}'");
            }
            catch (Exception ex)
            {
                _log($"  [FormInsert] form deploy failed: {ex.Message}");
                try { fm.UndoFormCheckOut(mapping.K2FormGuid); } catch { }
            }
        }

        private static XmlElement MakeFormControlDef(XmlDocument doc, string id, string type, string name)
        {
            var c = doc.CreateElement("Control");
            c.SetAttribute("ID", id);
            c.SetAttribute("Type", type);
            AddChild(doc, c, "Name", name);
            var props = doc.CreateElement("Properties");
            var prop = doc.CreateElement("Property");
            AddChild(doc, prop, "Name", "ControlName");
            AddChild(doc, prop, "Value", name);
            props.AppendChild(prop);
            c.AppendChild(props);
            return c;
        }

        private static List<JObject> GetSectionDataFields(JArray dataArray, string sectionName)
        {
            var list = new List<JObject>();
            foreach (JObject d in (dataArray?.OfType<JObject>() ?? Enumerable.Empty<JObject>()))
            {
                if (!(d["IsRepeating"]?.Value<bool>() ?? false)) continue;
                if (!string.Equals(d["RepeatingSectionName"]?.Value<string>(), sectionName, StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(d);
            }
            return list;
        }

        private static void AddSmoKey(Dictionary<string, FieldInfo> map, string raw, FieldInfo fi)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            string k = NameSanitizer.SanitizePropertyName(raw);
            if (string.IsNullOrEmpty(k)) return;
            map[k] = fi;
            map[k.ToUpperInvariant()] = fi;
        }

        private static int GridRowOf(string gridPos)
        {
            if (string.IsNullOrEmpty(gridPos)) return int.MaxValue;
            var m = Regex.Match(gridPos, "\\d+");
            return m.Success && int.TryParse(m.Value, out int r) ? r : int.MaxValue;
        }

        private static string MapInfoPathToK2DataType(string ipType)
        {
            switch ((ipType ?? string.Empty).ToLowerInvariant())
            {
                case "datepicker":
                case "date": return "DateTime";
                case "checkbox": return "YesNo";
                case "number":
                case "decimal": return "Number";
                case "richtext": return "Memo";
                default: return "Text";
            }
        }

        /// <summary>
        /// Rebuilds an existing capture view's content from the InfoPath model using the real
        /// generation engine (ViewXmlBuilder), bound to the view's EXISTING SmartObject and
        /// deployed over the EXISTING view GUID. Only InfoPath controls that resolve to an
        /// existing SmartObject field are placed; the rest are skipped.
        /// </summary>
        private void RebuildView(FormsManager formsManager, ViewInfo view, JArray viewsArray, JArray dataArray,
            JArray dynamicSections, JObject conditionalVisibility, InfoPathFormDefinition infoPathDef,
            IDictionary<string, string> explicitFieldMap, SmartObjectGenerator smoGen, UpdateResult result)
        {
            string viewXml = formsManager.GetViewDefinition(view.Guid);
            if (string.IsNullOrEmpty(viewXml)) { _log($"    [View {view.Name}] empty definition - skipped"); return; }

            // Dump raw XML for offline inspection.
            try
            {
                string dumpDir = Path.Combine(Path.GetTempPath(), "FormGenerator_K2Views");
                Directory.CreateDirectory(dumpDir);
                File.WriteAllText(Path.Combine(dumpDir, Regex.Replace(view.Name ?? "view", "[^A-Za-z0-9_.-]", "_") + ".xml"), viewXml);
            }
            catch { }

            var xdoc = new XmlDocument { PreserveWhitespace = false };
            xdoc.LoadXml(viewXml);

            string existingViewGuid = (xdoc.SelectSingleNode("//View") as XmlElement)?.GetAttribute("ID") ?? view.Guid.ToString();

            // Primary SmartObject source (binding target).
            var primarySource = (xdoc.SelectSingleNode("//Sources/Source[@ContextType='Primary']")
                                 ?? xdoc.SelectSingleNode("//Sources/Source[@SourceType='Object']")) as XmlElement;
            if (primarySource == null) { _log($"    [View {view.Name}] no primary SmartObject source - skipping rebuild"); return; }
            string smoGuid = primarySource.GetAttribute("SourceID");
            string smoName = primarySource.GetAttribute("SourceName");

            // FieldID → FieldInfo (from the primary source's <Fields>).
            var fieldByFieldId = new Dictionary<string, FieldInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (XmlElement fld in primarySource.SelectNodes("Fields/Field")?.OfType<XmlElement>() ?? Enumerable.Empty<XmlElement>())
            {
                string fid = fld.GetAttribute("ID");
                string fname = ReaderChildText(fld, "FieldName") ?? ReaderChildText(fld, "Name");
                if (string.IsNullOrEmpty(fid) || string.IsNullOrEmpty(fname)) continue;
                string fdisp = ReaderChildText(fld, "FieldDisplayName") ?? ReaderChildText(fld, "Name") ?? fname;
                string dtype = fld.GetAttribute("DataType");
                fieldByFieldId[fid] = new FieldInfo
                {
                    // Reuse the existing view's field ID so rebuilt controls bind to the same Source fields.
                    FieldGuid = fid,
                    FieldName = fname,
                    DisplayName = fdisp,
                    DataType = string.IsNullOrEmpty(dtype) ? "Text" : dtype
                };
            }

            // K2 control ID → FieldID (so the field-mapping UI can resolve to a SmartObject field).
            var fieldIdByControlId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (XmlElement ctrl in xdoc.SelectNodes("//Controls/Control[@FieldID]")?.OfType<XmlElement>() ?? Enumerable.Empty<XmlElement>())
            {
                string cid = ctrl.GetAttribute("ID");
                string f = ctrl.GetAttribute("FieldID");
                if (!string.IsNullOrEmpty(cid) && !string.IsNullOrEmpty(f)) fieldIdByControlId[cid] = f;
            }

            // Build the binding bridge: _smoFieldMappings[smoName][sanitizedKey] = FieldInfo(existing field).
            var fieldMap = new Dictionary<string, FieldInfo>(StringComparer.OrdinalIgnoreCase);
            void AddKey(string rawKey, FieldInfo fi)
            {
                if (string.IsNullOrWhiteSpace(rawKey) || fi == null) return;
                string k = NameSanitizer.SanitizePropertyName(rawKey);
                if (string.IsNullOrEmpty(k)) return;
                fieldMap[k] = fi;
                fieldMap[k.ToUpperInvariant()] = fi;
            }

            // (a) every existing field is matchable by its own name / display name.
            foreach (var fi in fieldByFieldId.Values) { AddKey(fi.FieldName, fi); AddKey(fi.DisplayName, fi); }

            // InfoPath controls for this rebuild (first InfoPath view).
            JObject ipView = viewsArray?.OfType<JObject>().FirstOrDefault();
            JArray ipControls = ipView?["Controls"] as JArray ?? new JArray();
            var ipByName = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            foreach (JObject c in ipControls.OfType<JObject>())
            {
                var nm = c["Name"]?.Value<string>();
                if (!string.IsNullOrEmpty(nm) && !ipByName.ContainsKey(nm)) ipByName[nm] = c;
            }

            // (b) user-confirmed field mappings win: InfoPath control name → K2 control → field.
            if (explicitFieldMap != null)
            {
                foreach (var kv in explicitFieldMap)
                {
                    if (string.IsNullOrEmpty(kv.Value)) continue;
                    if (!fieldIdByControlId.TryGetValue(kv.Value, out var fid)) continue;
                    if (!fieldByFieldId.TryGetValue(fid, out var fi)) continue;
                    AddKey(kv.Key, fi);
                    if (ipByName.TryGetValue(kv.Key, out var ipc))
                        AddKey(NameSanitizer.ExtractFieldNameFromBinding(ipc["Binding"]?.Value<string>()), fi);
                }
            }

            var smoFieldMappings = new Dictionary<string, Dictionary<string, FieldInfo>> { [smoName] = fieldMap };

            // Compute the grid-row range of each repeating section so we can keep the main view clean
            // (repeating-section controls + their column labels belong in the item/list views).
            int repMinRow = int.MaxValue, repMaxRow = int.MinValue;
            foreach (JObject c in ipControls.OfType<JObject>())
            {
                bool inRep = (c["IsInRepeatingSection"]?.Value<bool>() ?? false)
                             || string.Equals(c["Type"]?.Value<string>(), "RepeatingTable", StringComparison.OrdinalIgnoreCase)
                             || !string.IsNullOrWhiteSpace(c["RepeatingSectionName"]?.Value<string>());
                if (!inRep) continue;
                int r = GridRowOf(c["GridPosition"]?.Value<string>());
                if (r == int.MaxValue) continue;
                if (r < repMinRow) repMinRow = r;
                if (r > repMaxRow) repMaxRow = r;
            }

            // Keep non-data controls (labels/layout); keep data controls that resolve to a field; drop the rest.
            // Exclude everything belonging to (or sitting within) a repeating section.
            var filtered = new JArray();
            int kept = 0, dropped = 0;
            foreach (JObject c in ipControls.OfType<JObject>())
            {
                string binding = c["Binding"]?.Value<string>();
                string name = c["Name"]?.Value<string>();

                bool inRepeating = (c["IsInRepeatingSection"]?.Value<bool>() ?? false)
                    || string.Equals(c["Type"]?.Value<string>(), "RepeatingTable", StringComparison.OrdinalIgnoreCase)
                    || !string.IsNullOrWhiteSpace(c["RepeatingSectionName"]?.Value<string>());
                if (!inRepeating && repMinRow != int.MaxValue)
                {
                    // A label sitting on a row owned by a repeating section is a column header for it.
                    int r = GridRowOf(c["GridPosition"]?.Value<string>());
                    if (r >= repMinRow && r <= repMaxRow) inRepeating = true;
                }
                if (inRepeating) { dropped++; continue; }

                bool isData = !string.IsNullOrWhiteSpace(binding);
                if (!isData) { filtered.Add(c.DeepClone()); continue; }

                string k1 = NameSanitizer.SanitizePropertyName(NameSanitizer.ExtractFieldNameFromBinding(binding) ?? string.Empty);
                string k2 = NameSanitizer.SanitizePropertyName(name ?? string.Empty);
                bool resolvable = (!string.IsNullOrEmpty(k1) && fieldMap.ContainsKey(k1))
                               || (!string.IsNullOrEmpty(k2) && fieldMap.ContainsKey(k2));
                if (resolvable) { filtered.Add(c.DeepClone()); kept++; }
                else dropped++;
            }

            _log($"    [View {view.Name}] rebuild → SmartObject '{smoName}' [{smoGuid}], {fieldByFieldId.Count} field(s); controls kept={kept} dropped={dropped}");
            if (kept == 0) { _log($"    [View {view.Name}] no bindable InfoPath controls - skipping rebuild"); return; }

            // Build the view XML via the real engine, reusing the existing view GUID + SmartObject.
            string rebuiltXml;
            try
            {
                var builder = new ViewXmlBuilder(_connectionManager, smoFieldMappings, smoGen, _config, infoPathDef);
                var newDoc = builder.CreateViewXmlStructure(
                    view.Name, smoGuid, smoName, filtered,
                    dataArray ?? new JArray(), dynamicSections ?? new JArray(), conditionalVisibility ?? new JObject(),
                    isItemView: false, out _, existingViewGuid: existingViewGuid);

                // Carry over the data-source-wired buttons and auto-generated controls so they
                // keep working after the rebuild.
                PreserveButtonsAndWiring(xdoc, newDoc, view.Name);

                rebuiltXml = newDoc.OuterXml;
            }
            catch (Exception ex)
            {
                _log($"    [View {view.Name}] rebuild failed (engine): {ex.Message} - leaving view untouched");
                return;
            }

            // Deploy over the existing view.
            try
            {
                formsManager.CheckOutView(view.Guid);
                formsManager.DeployViews(rebuiltXml, view.CategoryPath, true);
                result.ViewsUpdated++;
                _log($"    [View {view.Name}] rebuilt & deployed ({kept} control(s) bound to {smoName})");
            }
            catch (Exception ex)
            {
                _log($"    [View {view.Name}] deploy failed: {ex.Message}");
                try { formsManager.UndoViewCheckOut(view.Guid); } catch { }
            }
        }

        /// <summary>
        /// Copies the original view's buttons, auto-generated controls, their layout rows, and their
        /// events — plus the original Sources — into the rebuilt view, so the data-source-wired
        /// buttons (Save / Create List Item, Cancel) keep working after a rebuild. Fail-safe.
        /// </summary>
        private void PreserveButtonsAndWiring(XmlDocument origDoc, XmlDocument newDoc, string viewName)
        {
            try
            {
                var newControls = newDoc.SelectSingleNode("//View/Controls") as XmlElement;
                var newGrid = (newDoc.SelectSingleNode("//View/Canvas//Control[@LayoutType='Grid']")
                               ?? newDoc.SelectSingleNode("//Control[@LayoutType='Grid']")) as XmlElement;
                var newRows = newGrid?.SelectSingleNode("Rows") as XmlElement;
                var newEvents = newDoc.SelectSingleNode("//View/Events") as XmlElement;
                if (newControls == null || newRows == null) { _log($"    [View {viewName}] preserve: rebuilt Controls/Rows missing - skipped"); return; }

                // 1) Replace rebuilt Sources with the original (keeps the SmartObject source element +
                //    method bindings the buttons reference; rebuilt controls use existing field IDs).
                var origSources = origDoc.SelectSingleNode("//View/Sources");
                var newSources = newDoc.SelectSingleNode("//View/Sources");
                if (origSources != null && newSources?.ParentNode != null)
                    newSources.ParentNode.ReplaceChild(newDoc.ImportNode(origSources, true), newSources);

                // 2) Preserved controls: buttons + auto-generated controls (keep document order).
                var preserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var buttonCtrls = new List<XmlElement>();
                var hiddenCtrls = new List<XmlElement>();
                foreach (XmlElement c in origDoc.SelectNodes("//View/Controls/Control")?.OfType<XmlElement>() ?? Enumerable.Empty<XmlElement>())
                {
                    string id = c.GetAttribute("ID");
                    if (string.IsNullOrEmpty(id)) continue;
                    bool isButton = string.Equals(c.GetAttribute("Type"), "Button", StringComparison.OrdinalIgnoreCase);
                    bool autoGen = string.Equals(c.SelectSingleNode("Properties/Property[Name='AutoGenerated']/Value")?.InnerText, "true", StringComparison.OrdinalIgnoreCase);
                    if (isButton) { buttonCtrls.Add(c); preserved.Add(id); }
                    else if (autoGen) { hiddenCtrls.Add(c); preserved.Add(id); }
                }
                if (preserved.Count == 0) { _log($"    [View {viewName}] preserve: no buttons/auto-generated controls found"); return; }

                // 3) Carry the button + hidden control definitions into the rebuilt <Controls>.
                int ctlCount = 0;
                foreach (var c in buttonCtrls.Concat(hiddenCtrls))
                {
                    string id = c.GetAttribute("ID");
                    if (newControls.SelectSingleNode($"Control[@ID='{id}']") != null) continue;
                    newControls.AppendChild(newDoc.ImportNode(c, true));
                    ctlCount++;
                }

                // 4) Build ONE fresh button row so the buttons stay side by side (one right-aligned
                //    cell spanning all columns, with both buttons inline). Hidden controls ride along.
                int colCount = newGrid?.SelectNodes("Columns/Column")?.Count ?? 1;
                if (colCount < 1) colCount = 1;
                string rowId = Guid.NewGuid().ToString();
                string cellId = Guid.NewGuid().ToString();
                newControls.AppendChild(CreateLayoutControlDef(newDoc, rowId, "Row", "Buttons Row", false));
                newControls.AppendChild(CreateLayoutControlDef(newDoc, cellId, "Cell", "Buttons Cell", true));

                var rowEl = newDoc.CreateElement("Row");
                rowEl.SetAttribute("ID", rowId);
                var cellsEl = newDoc.CreateElement("Cells");
                var cellEl = newDoc.CreateElement("Cell");
                cellEl.SetAttribute("ID", cellId);
                if (colCount > 1) cellEl.SetAttribute("ColumnSpan", colCount.ToString());
                foreach (var c in buttonCtrls.Concat(hiddenCtrls))
                    cellEl.AppendChild(ControlRef(newDoc, c.GetAttribute("ID")));
                cellsEl.AppendChild(cellEl);
                rowEl.AppendChild(cellsEl);
                newRows.AppendChild(rowEl);
                int rowCount = 1;

                // 6) Carry events whose triggering control is a preserved control (button OnClick, etc.).
                int evtCount = 0;
                var origEvents = origDoc.SelectSingleNode("//View/Events");
                if (origEvents != null && newEvents != null)
                {
                    foreach (XmlElement ev in origEvents.SelectNodes("Event")?.OfType<XmlElement>() ?? Enumerable.Empty<XmlElement>())
                    {
                        if (!preserved.Contains(ev.GetAttribute("SourceID"))) continue;
                        newEvents.AppendChild(newDoc.ImportNode(ev, true));
                        evtCount++;
                    }
                }

                _log($"    [View {viewName}] preserved {preserved.Count} button/auto control(s) → {ctlCount} def(s), {rowCount} row(s), {evtCount} event(s)");
            }
            catch (Exception ex)
            {
                _log($"    [View {viewName}] button preservation failed (continuing): {ex.Message}");
            }
        }

        private static XmlElement CreateLayoutControlDef(XmlDocument doc, string id, string type, string name, bool alignRight)
        {
            var c = doc.CreateElement("Control");
            c.SetAttribute("ID", id);
            c.SetAttribute("Type", type);
            AddChild(doc, c, "Name", name);
            AddChild(doc, c, "DisplayName", name);
            if (alignRight)
            {
                var styles = doc.CreateElement("Styles");
                var style = doc.CreateElement("Style");
                style.SetAttribute("IsDefault", "True");
                var text = doc.CreateElement("Text");
                AddChild(doc, text, "Align", "Right");
                style.AppendChild(text);
                styles.AppendChild(style);
                c.AppendChild(styles);
            }
            var props = doc.CreateElement("Properties");
            var prop = doc.CreateElement("Property");
            AddChild(doc, prop, "Name", "ControlName");
            AddChild(doc, prop, "Value", name);
            props.AppendChild(prop);
            c.AppendChild(props);
            return c;
        }

        private static XmlElement ControlRef(XmlDocument doc, string id)
        {
            var e = doc.CreateElement("Control");
            e.SetAttribute("ID", id);
            return e;
        }

        private static void AddChild(XmlDocument doc, XmlElement parent, string name, string value)
        {
            var e = doc.CreateElement(name);
            e.InnerText = value ?? string.Empty;
            parent.AppendChild(e);
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

                string mainSmoName = null;
                foreach (var v in views)
                {
                    string xml;
                    try { xml = fm.GetViewDefinition(v.Guid); } catch { continue; }
                    if (string.IsNullOrEmpty(xml)) continue;

                    var doc = new XmlDocument();
                    try { doc.LoadXml(xml); } catch { continue; }

                    // Capture the primary SmartObject name (used to find sibling/child list SmartObjects).
                    if (string.IsNullOrEmpty(mainSmoName))
                    {
                        var ps = (doc.SelectSingleNode("//Sources/Source[@ContextType='Primary']")
                                  ?? doc.SelectSingleNode("//Sources/Source[@SourceType='Object']")) as XmlElement;
                        mainSmoName = ps?.GetAttribute("SourceName");
                    }

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

                // Surface sibling/child SmartObject columns (repeating-section lists on the same site)
                // so the field-mapping dialog can map repeating InfoPath controls to them.
                try { AppendSiblingSmartObjectFields(host, port, mainSmoName, result); } catch { }
            }
            finally { try { fm.Dispose(); } catch { } }
            return result;
        }

        private static readonly HashSet<string> SpSystemFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ContentType","ContentTypeId","_UIVersionString","Author","Author_Value","Editor","Editor_Value",
            "FileLeafRef","Folder","LinkFilename","LinkToItem","Created","Modified","ComplianceAssetId",
            "Attachments","SharePoint_TimeZone","Version","ID","ParentID","Parent_ID"
        };

        private static bool IsSystemSpField(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            if (name.StartsWith("K2_Int_", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.StartsWith("_", StringComparison.Ordinal)) return true;
            return SpSystemFields.Contains(name);
        }

        /// <summary>
        /// Adds the columns of sibling/child SmartObjects (other SharePoint lists on the same site,
        /// i.e. the repeating-section lists) to the field-mapping options. Each is given a synthetic
        /// ControlId "SMO::{smoName}::{field}" so it is distinguishable from a real view control.
        /// Best-effort and capped.
        /// </summary>
        private static void AppendSiblingSmartObjectFields(string host, uint port, string mainSmoName, List<K2FieldDescriptor> result)
        {
            if (string.IsNullOrWhiteSpace(mainSmoName)) return;

            // Prefix up to and including "_Lists_" → all SharePoint-list SmartObjects on the site.
            string prefix = mainSmoName;
            int idx = mainSmoName.IndexOf("_Lists_", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0) prefix = mainSmoName.Substring(0, idx + "_Lists_".Length);

            var conn = new ServerConnectionManager(host, port);
            try
            {
                conn.Connect();
                var mgmt = conn.ManagementServer;
                var explorer = mgmt.GetSmartObjectsStartsWith(prefix);
                var infos = (explorer?.SmartObjects?.Cast<SmartObjectInfo>() ?? Enumerable.Empty<SmartObjectInfo>())
                    .Where(s => s != null && !string.Equals(s.Name, mainSmoName, StringComparison.OrdinalIgnoreCase))
                    .Take(50)
                    .ToList();

                foreach (var info in infos)
                {
                    SmartObjectInfo full;
                    try { full = mgmt.GetSmartObjectInfo(info.Guid); } catch { full = info; }
                    foreach (SmartPropertyInfo p in (full?.Properties?.Cast<SmartPropertyInfo>() ?? Enumerable.Empty<SmartPropertyInfo>()))
                    {
                        string sysName = p?.Name;                       // exact internal column (used for binding)
                        if (string.IsNullOrEmpty(sysName) || IsSystemSpField(sysName)) continue;
                        string disp = p.Metadata?.DisplayName;          // friendly name (shown + auto-matched)
                        if (string.IsNullOrWhiteSpace(disp)) disp = sysName;
                        result.Add(new K2FieldDescriptor
                        {
                            ViewName = info.Name,
                            ControlId = $"SMO::{info.Name}::{sysName}",  // store the EXACT internal column
                            ControlName = disp,
                            FieldId = info.Guid.ToString(),
                            FieldName = sysName,
                            FieldDisplayName = disp,
                            Type = p.Type.ToString()
                        });
                    }
                }
            }
            catch { /* best-effort */ }
            finally { try { conn.Disconnect(); } catch { } }
        }

        /// <summary>
        /// Lists all SmartObject names on the K2 server (for the per-section SmartObject picker).
        /// Lightweight (names only). Optional prefix narrows to a site's lists.
        /// </summary>
        public static List<string> LoadAllSmartObjectNames(string host, uint port, string prefix = null)
        {
            var names = new List<string>();
            var conn = new ServerConnectionManager(host, port);
            try
            {
                conn.Connect();
                var mgmt = conn.ManagementServer;
                var explorer = string.IsNullOrWhiteSpace(prefix) ? mgmt.GetSmartObjects() : mgmt.GetSmartObjectsStartsWith(prefix);
                foreach (SmartObjectInfo s in (explorer?.SmartObjects?.Cast<SmartObjectInfo>() ?? Enumerable.Empty<SmartObjectInfo>()))
                    if (!string.IsNullOrEmpty(s?.Name)) names.Add(s.Name);
            }
            catch { /* best-effort */ }
            finally { try { conn.Disconnect(); } catch { } }
            return names.Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                        .ToList();
        }

        /// <summary>
        /// Loads a single SmartObject's real columns (friendly name shown, exact internal name stored
        /// in ControlId as "SMO::{smo}::{sysname}"). Used for field-mapping a section to a chosen SmartObject.
        /// </summary>
        public static List<K2FieldDescriptor> LoadSmartObjectColumns(string host, uint port, string smoName)
        {
            var result = new List<K2FieldDescriptor>();
            if (string.IsNullOrWhiteSpace(smoName)) return result;
            var conn = new ServerConnectionManager(host, port);
            try
            {
                conn.Connect();
                var mgmt = conn.ManagementServer;
                var explorer = mgmt.GetSmartObjects(smoName);
                var info = (explorer?.SmartObjects?.Cast<SmartObjectInfo>() ?? Enumerable.Empty<SmartObjectInfo>())
                    .FirstOrDefault(s => string.Equals(s?.Name, smoName, StringComparison.OrdinalIgnoreCase))
                    ?? (explorer?.SmartObjects?.Cast<SmartObjectInfo>() ?? Enumerable.Empty<SmartObjectInfo>()).FirstOrDefault();
                if (info != null)
                {
                    SmartObjectInfo full;
                    try { full = mgmt.GetSmartObjectInfo(info.Guid); } catch { full = info; }
                    foreach (SmartPropertyInfo p in (full?.Properties?.Cast<SmartPropertyInfo>() ?? Enumerable.Empty<SmartPropertyInfo>()))
                    {
                        string sysName = p?.Name;
                        if (string.IsNullOrEmpty(sysName) || IsSystemSpField(sysName)) continue;
                        string disp = p.Metadata?.DisplayName;
                        if (string.IsNullOrWhiteSpace(disp)) disp = sysName;
                        result.Add(new K2FieldDescriptor
                        {
                            ViewName = smoName,
                            ControlId = $"SMO::{smoName}::{sysName}",
                            ControlName = disp,
                            FieldId = info.Guid.ToString(),
                            FieldName = sysName,
                            FieldDisplayName = disp,
                            Type = p.Type.ToString()
                        });
                    }
                }
            }
            catch { /* best-effort */ }
            finally { try { conn.Disconnect(); } catch { } }
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
