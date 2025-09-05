using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;

namespace FormGenerator.Analyzers.Infopath
{
    #region InfoPath-Specific Models

    public class InfoPathFormDefinition
    {
        public string FormName { get; set; }
        public string FileName { get; set; }
        public string Title { get; set; }
        public List<ViewDefinition> Views { get; set; } = new List<ViewDefinition>();
        public List<FormRule> Rules { get; set; } = new List<FormRule>();
        public List<DataColumn> Data { get; set; } = new List<DataColumn>();
        public List<DynamicSection> DynamicSections { get; set; } = new List<DynamicSection>();
        public Dictionary<string, List<string>> ConditionalVisibility { get; set; } = new Dictionary<string, List<string>>();
        public FormMetadata Metadata { get; set; } = new FormMetadata();
        public DebugInfo DebugInfo { get; set; } = new DebugInfo();
    }

    public class DebugInfo
    {
        public List<string> SkippedElements { get; set; } = new List<string>();
        public List<string> ProcessedElements { get; set; } = new List<string>();
        public Dictionary<string, int> ElementTypeCounts { get; set; } = new Dictionary<string, int>();
        public List<string> ExpressionBoxContents { get; set; } = new List<string>();
        public int TotalElementsExamined { get; set; }
        public int ControlsExtracted { get; set; }
        public List<string> DuplicatesSkipped { get; set; } = new List<string>();
        public List<string> BindingPaths { get; set; } = new List<string>();
    }

    public class ViewDefinition
    {
        public string ViewName { get; set; }
        public List<ControlDefinition> Controls { get; set; } = new List<ControlDefinition>();
        public List<SectionInfo> Sections { get; set; } = new List<SectionInfo>();
    }

    public class ControlDefinition
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Label { get; set; }
        public string Binding { get; set; }
        public int DocIndex { get; set; }
        public string GridPosition { get; set; }
        public string SectionGridPosition { get; set; }
        public string ParentSection { get; set; }
        public string SectionType { get; set; }
        public int ColumnSpan { get; set; } = 1;
        public int RowSpan { get; set; } = 1;
        public string AssociatedLabelId { get; set; }
        public string AssociatedControlId { get; set; }
        public bool IsMultiLineLabel { get; set; }
        public bool IsMergedIntoParent { get; set; }
        public bool IsInRepeatingSection { get; set; }
        public string RepeatingSectionName { get; set; }
        public string RepeatingSectionBinding { get; set; }
        public bool WasInExpressionBox { get; set; }  // Track if control was inside ExpressionBox
        public Dictionary<string, string> Properties { get; set; }
        public List<ControlDefinition> Controls { get; set; }
        public List<DataOption> DataOptions { get; set; }
        public string DataOptionsString { get; set; }
        public bool HasStaticData => DataOptions != null && DataOptions.Any();

        public ControlDefinition()
        {
            Properties = new Dictionary<string, string>();
            Controls = new List<ControlDefinition>();
            DataOptions = new List<DataOption>();
        }
    }

    public class DataOption
    {
        public string Value { get; set; }
        public string DisplayText { get; set; }
        public bool IsDefault { get; set; }
        public int Order { get; set; }
    }

    public class SectionInfo
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string CtrlId { get; set; }
        public int StartRow { get; set; }
        public int EndRow { get; set; }
        public List<string> ControlIds { get; set; } = new List<string>();
    }

    public class FormRule
    {
        public string Name { get; set; }
        public string Condition { get; set; }
        public bool IsEnabled { get; set; }
        public List<FormRuleAction> Actions { get; set; } = new List<FormRuleAction>();
    }

    public class FormRuleAction
    {
        public string Type { get; set; }
        public string Target { get; set; }
        public string Expression { get; set; }
    }

    public class DataColumn
    {
        public string ColumnName { get; set; }
        public string Type { get; set; }
        public string RepeatingSection { get; set; }
        public bool IsRepeating { get; set; }
        public string RepeatingSectionPath { get; set; }
        public bool IsConditional { get; set; }
        public string ConditionalOnField { get; set; }
        public string DisplayName { get; set; }
        public List<DataOption> ValidValues { get; set; }
        public string DefaultValue { get; set; }
        public bool HasConstraints => ValidValues != null && ValidValues.Any();
    }

    public class DynamicSection
    {
        public string Mode { get; set; }
        public string CtrlId { get; set; }
        public string Caption { get; set; }
        public string Condition { get; set; }
        public string ConditionField { get; set; }
        public string ConditionValue { get; set; }
        public List<string> Controls { get; set; } = new List<string>();
        public bool IsVisible { get; set; }
    }

    public class FormMetadata
    {
        public int TotalControls { get; set; }
        public int TotalSections { get; set; }
        public int DynamicSectionCount { get; set; }
        public int RepeatingSectionCount { get; set; }
        public List<string> ConditionalFields { get; set; } = new List<string>();
    }

    #endregion

    #region Enhanced Parser

    public class EnhancedInfoPathParser
    {
        private DebugInfo debugInfo = new DebugInfo();

        public InfoPathFormDefinition ParseXsnFile(string xsnFilePath)
        {
            string tempDir = Path.Combine(
                Path.GetTempPath(),
                Path.GetFileNameWithoutExtension(xsnFilePath) + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                Console.WriteLine("========== ENHANCED DEBUG MODE ==========");
                Console.WriteLine($"Extracting XSN to {tempDir}");

                if (!ExtractXsn(xsnFilePath, tempDir))
                    throw new Exception("Failed to extract XSN file using all available methods.");

                var formDef = new InfoPathFormDefinition();
                formDef.DebugInfo = debugInfo;

                var viewFiles = Directory.GetFiles(tempDir, "view*.xsl");
                if (viewFiles.Length == 0)
                {
                    Console.WriteLine("No view*.xsl found in XSN.");
                }
                else
                {
                    Console.WriteLine($"Found {viewFiles.Length} view files");
                    foreach (var vf in viewFiles)
                    {
                        Console.WriteLine($"\n===== PARSING VIEW: {Path.GetFileName(vf)} =====");
                        var singleView = ParseSingleView(vf);

                        var xslDoc = XDocument.Load(vf);
                        var dynamicHandler = new DynamicSectionHandler();
                        var dynamicSections = dynamicHandler.ExtractDynamicSections(xslDoc);
                        formDef.DynamicSections.AddRange(dynamicSections);

                        formDef.Views.Add(singleView);
                    }
                }

                PostProcessFormDefinition(formDef);
                PrintDebugSummary();
                return formDef;
            }
            finally
            {
                try
                {
                    Directory.Delete(tempDir, true);
                    Console.WriteLine("Cleanup completed successfully.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Warning: Cleanup failed: " + ex.Message);
                }
            }
        }

        private void PrintDebugSummary()
        {
            Console.WriteLine("\n========== DEBUG SUMMARY ==========");
            Console.WriteLine($"Total elements examined: {debugInfo.TotalElementsExamined}");
            Console.WriteLine($"Controls extracted: {debugInfo.ControlsExtracted}");
            Console.WriteLine($"Duplicates skipped: {debugInfo.DuplicatesSkipped.Count}");
            Console.WriteLine($"Expression boxes processed: {debugInfo.ExpressionBoxContents.Count}");

            Console.WriteLine("\n--- Element Type Counts ---");
            foreach (var kvp in debugInfo.ElementTypeCounts.OrderByDescending(x => x.Value))
            {
                Console.WriteLine($"  {kvp.Key}: {kvp.Value}");
            }

            if (debugInfo.SkippedElements.Any())
            {
                Console.WriteLine($"\n--- Skipped Elements (first 20) ---");
                foreach (var elem in debugInfo.SkippedElements.Take(20))
                {
                    Console.WriteLine($"  {elem}");
                }
            }

            if (debugInfo.ExpressionBoxContents.Any())
            {
                Console.WriteLine($"\n--- Expression Box Contents ---");
                foreach (var content in debugInfo.ExpressionBoxContents)
                {
                    Console.WriteLine($"  {content}");
                }
            }

            if (debugInfo.BindingPaths.Any())
            {
                Console.WriteLine($"\n--- Unique Binding Paths ---");
                foreach (var binding in debugInfo.BindingPaths.Distinct().OrderBy(x => x))
                {
                    Console.WriteLine($"  {binding}");
                }
            }
        }

        private bool ExtractXsn(string xsnFilePath, string tempDir)
        {
            try
            {
                ExtractUsingExpandExe(xsnFilePath, tempDir);
                Console.WriteLine("Successfully extracted using expand.exe");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Expand.exe extraction failed: " + ex.Message);
            }

            try
            {
                ZipFile.ExtractToDirectory(xsnFilePath, tempDir);
                Console.WriteLine("Successfully extracted as ZIP");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("ZIP extraction failed: " + ex.Message);
            }

            try
            {
                ExtractUsingPowerShell(xsnFilePath, tempDir);
                Console.WriteLine("Successfully extracted using PowerShell");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("PowerShell extraction failed: " + ex.Message);
            }

            return false;
        }

        private ViewDefinition ParseSingleView(string viewFile)
        {
            var viewDef = new ViewDefinition
            {
                ViewName = Path.GetFileName(viewFile)
            };

            var sectionParser = new SectionAwareParser(debugInfo);
            var labelAssociator = new LabelControlAssociator();
            var labelHandler = new ComplexLabelHandler();

            var controls = sectionParser.ParseViewFile(viewFile);
            labelAssociator.AssociateLabelControls(controls);
            labelHandler.ProcessMultiLineLabels(controls);

            viewDef.Sections = sectionParser.GetSections();
            viewDef.Controls = controls;
            return viewDef;
        }

        private void PostProcessFormDefinition(InfoPathFormDefinition formDef)
        {
            try
            {
                foreach (var dynSection in formDef.DynamicSections)
                {
                    if (!string.IsNullOrEmpty(dynSection.ConditionField))
                    {
                        if (!formDef.ConditionalVisibility.TryGetValue(dynSection.ConditionField, out var list))
                        {
                            list = new List<string>();
                            formDef.ConditionalVisibility[dynSection.ConditionField] = list;
                        }

                        if (dynSection.Controls != null)
                        {
                            foreach (var ctrlId in dynSection.Controls)
                            {
                                if (!string.IsNullOrEmpty(ctrlId))
                                    list.Add(ctrlId);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Error building conditional visibility map: {ex.Message}");
            }

            try
            {
                var allCtrls = GetAllControlsFromAllViews(formDef);
                var dataCols = BuildEnhancedColumns(allCtrls, formDef);
                formDef.Data = dataCols;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Error building data columns: {ex.Message}");
                formDef.Data = new List<DataColumn>();
            }

            try
            {
                AddFormMetadata(formDef);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Error adding form metadata: {ex.Message}");
            }
        }

        private List<ControlDefinition> GetAllControlsFromAllViews(InfoPathFormDefinition formDef)
        {
            var all = new List<ControlDefinition>();
            foreach (var v in formDef.Views)
                all.AddRange(v.Controls);
            return all;
        }

        private List<DataColumn> BuildEnhancedColumns(List<ControlDefinition> allCtrls, InfoPathFormDefinition formDef)
        {
            var dict = new Dictionary<(string colName, string rep), DataColumn>();

            foreach (var ctrl in allCtrls)
            {
                if (ctrl.Type == "Label" || ctrl.IsMergedIntoParent) continue;

                string colName = string.IsNullOrWhiteSpace(ctrl.Name) ? ctrl.Binding : ctrl.Name;
                if (string.IsNullOrWhiteSpace(colName)) continue;

                string repeating = ctrl.IsInRepeatingSection ? ctrl.RepeatingSectionName : ctrl.ParentSection;
                var key = (colName, repeating);

                if (!dict.ContainsKey(key))
                {
                    var dataCol = new DataColumn
                    {
                        ColumnName = colName,
                        Type = ctrl.Type,
                        RepeatingSection = repeating,
                        IsRepeating = ctrl.SectionType == "repeating" || ctrl.IsInRepeatingSection,
                        RepeatingSectionPath = ctrl.IsInRepeatingSection ? ctrl.RepeatingSectionBinding : repeating,
                        DisplayName = ctrl.Label
                    };

                    if (ctrl.HasStaticData && ctrl.DataOptions != null && ctrl.DataOptions.Any())
                    {
                        dataCol.ValidValues = new List<DataOption>(ctrl.DataOptions);
                        var defaultOption = ctrl.DataOptions.FirstOrDefault(o => o.IsDefault);
                        if (defaultOption != null)
                        {
                            dataCol.DefaultValue = defaultOption.Value;
                        }
                        else if (ctrl.Properties != null && ctrl.Properties.ContainsKey("DefaultValue"))
                        {
                            dataCol.DefaultValue = ctrl.Properties["DefaultValue"];
                        }
                    }

                    if (ctrl.Properties != null && ctrl.Properties.ContainsKey("CtrlId"))
                    {
                        var ctrlId = ctrl.Properties["CtrlId"];
                        var condField = formDef.ConditionalVisibility
                            .FirstOrDefault(cv => cv.Value != null && cv.Value.Contains(ctrlId));

                        if (!condField.Equals(default(KeyValuePair<string, List<string>>)))
                        {
                            dataCol.IsConditional = true;
                            dataCol.ConditionalOnField = condField.Key;
                        }
                    }

                    dict[key] = dataCol;
                }
                else
                {
                    var existingCol = dict[key];
                    if (ctrl.HasStaticData && ctrl.DataOptions != null &&
                        ctrl.DataOptions.Any() && existingCol.ValidValues == null)
                    {
                        existingCol.ValidValues = new List<DataOption>(ctrl.DataOptions);
                        var defaultOption = ctrl.DataOptions.FirstOrDefault(o => o.IsDefault);
                        if (defaultOption != null && string.IsNullOrEmpty(existingCol.DefaultValue))
                        {
                            existingCol.DefaultValue = defaultOption.Value;
                        }
                    }
                }
            }

            return dict.Values.ToList();
        }

        private void AddFormMetadata(InfoPathFormDefinition formDef)
        {
            var allControls = GetAllControlsFromAllViews(formDef);
            var repeatingSectionCount = 0;

            var repeatingSections = formDef.Views
                .SelectMany(v => v.Sections)
                .Where(s => s.Type == "repeating")
                .ToList();
            repeatingSectionCount += repeatingSections.Count;

            Console.WriteLine($"Found {repeatingSections.Count} repeating sections:");
            foreach (var section in repeatingSections)
                Console.WriteLine($"  - {section.Name} (Type: {section.Type})");

            var repeatingTables = allControls
                .Where(c => c.Type == "RepeatingTable")
                .ToList();
            repeatingSectionCount += repeatingTables.Count;

            Console.WriteLine($"Found {repeatingTables.Count} repeating tables:");
            foreach (var table in repeatingTables)
                Console.WriteLine($"  - {table.Name} (Label: {table.Label})");

            var otherRepeating = allControls
                .Where(c => c.Type == "RepeatingSection")
                .ToList();
            repeatingSectionCount += otherRepeating.Count;

            Console.WriteLine($"Found {otherRepeating.Count} other repeating sections:");
            foreach (var other in otherRepeating)
                Console.WriteLine($"  - {other.Name} (Label: {other.Label})");

            // Log controls found in expression boxes
            var expressionBoxControls = allControls.Where(c => c.WasInExpressionBox).ToList();
            Console.WriteLine($"\nControls extracted from ExpressionBoxes: {expressionBoxControls.Count}");
            foreach (var ctrl in expressionBoxControls)
            {
                Console.WriteLine($"  - {ctrl.Name} ({ctrl.Type}) - Binding: {ctrl.Binding}");
            }

            Console.WriteLine($"Total repeating sections/tables: {repeatingSectionCount}");

            formDef.Metadata = new FormMetadata
            {
                TotalControls = allControls.Count(c => !c.IsMergedIntoParent),
                TotalSections = formDef.Views
                    .SelectMany(v => v.Sections)
                    .Select(s => s.Name)
                    .Distinct()
                    .Count(),
                DynamicSectionCount = formDef.DynamicSections.Count,
                RepeatingSectionCount = repeatingSectionCount,
                ConditionalFields = formDef.ConditionalVisibility.Keys.ToList()
            };
        }

        private void ExtractUsingExpandExe(string cabFile, string destFolder)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "expand.exe",
                Arguments = $"\"{cabFile}\" -F:* \"{destFolder}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (var process = Process.Start(startInfo))
            {
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    string error = process.StandardError.ReadToEnd();
                    throw new Exception($"Expand.exe failed with exit code {process.ExitCode}: {error}");
                }
            }
        }

        private void ExtractUsingPowerShell(string cabFile, string destFolder)
        {
            string script = $@"
                $shell = New-Object -ComObject Shell.Application
                $sourceFolder = $shell.NameSpace('{cabFile}')
                $destFolder = $shell.NameSpace('{destFolder}')
                $destFolder.CopyHere($sourceFolder.Items(), 16)
            ";

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (var process = Process.Start(startInfo))
            {
                process.WaitForExit(10000);
                Thread.Sleep(1000);
            }
        }
    }

    #endregion

    #region Section-Aware Parser with Enhanced Debugging

    public class SectionAwareParser
    {
        private DebugInfo debugInfo;
        private Stack<SectionContext> sectionStack;
        private List<SectionInfo> sections;
        private int docIndexCounter;
        private List<ControlDefinition> allControls;
        private int currentRow;
        private int currentCol;
        private bool inTableRow;
        private HashSet<string> processedControls;
        private Dictionary<string, int> sectionRowCounters;
        private Stack<RepeatingContext> repeatingContextStack;
        private Queue<LabelInfo> recentLabels;
        private const int LABEL_LOOKBACK_COUNT = 5;
        private Dictionary<string, ControlDefinition> controlsById;
        private bool insideXslTemplate = false;
        private string currentTemplateMode = null;
        private HashSet<string> processedTemplates;
        private int repeatingSectionCounter = 0;
        private Dictionary<string, int> sectionNameCounters = new Dictionary<string, int>();
        private HashSet<string> viewRepeatingSectionNames;
        private List<string> currentTableHeaders;
        private int currentTableColumn;
        private string currentSectionName = null;
        private bool insideExpressionBox = false;
        private int expressionBoxDepth = 0;

        public class LabelInfo
        {
            public string Text { get; set; }
            public int DocIndex { get; set; }
            public int Row { get; set; }
            public int Col { get; set; }
            public bool Used { get; set; }
        }

        public class SectionContext
        {
            public string Name { get; set; }
            public string Type { get; set; }
            public int StartRow { get; set; }
            public string CtrlId { get; set; }
            public string Binding { get; set; }
            public string DisplayName { get; set; }
        }

        public class RepeatingContext
        {
            public string Name { get; set; }
            public string Binding { get; set; }
            public string Type { get; set; }
            public string DisplayName { get; set; }
            public int Depth { get; set; }
        }

        public SectionAwareParser(DebugInfo debug)
        {
            debugInfo = debug ?? new DebugInfo();
            InitializeCollections();
        }

        private void InitializeCollections()
        {
            sectionStack = new Stack<SectionContext>();
            sections = new List<SectionInfo>();
            allControls = new List<ControlDefinition>();
            processedControls = new HashSet<string>();
            sectionRowCounters = new Dictionary<string, int>();
            repeatingContextStack = new Stack<RepeatingContext>();
            recentLabels = new Queue<LabelInfo>();
            controlsById = new Dictionary<string, ControlDefinition>();
            processedTemplates = new HashSet<string>();
            repeatingSectionCounter = 0;
            viewRepeatingSectionNames = new HashSet<string>();
            currentSectionName = null;
        }

        public List<ControlDefinition> ParseViewFile(string viewFile)
        {
            Debug.WriteLine($"\n========================================");
            Debug.WriteLine($"STARTING PARSE OF VIEW: {viewFile}");
            Debug.WriteLine($"========================================\n");

            ResetParserState();
            XDocument doc = XDocument.Load(viewFile);

            // Start the recursive processing
            ProcessElement(doc.Root);

            Debug.WriteLine($"\n========================================");
            Debug.WriteLine($"PARSE COMPLETE - Total Controls Found: {allControls.Count}");
            Debug.WriteLine($"Controls by Type:");
            var byType = allControls.GroupBy(c => c.Type)
                .Select(g => new { Type = g.Key, Count = g.Count() })
                .OrderBy(x => x.Type);
            foreach (var typeGroup in byType)
            {
                Debug.WriteLine($"  {typeGroup.Type}: {typeGroup.Count}");
            }

            Debug.WriteLine($"\nControls from ExpressionBoxes: {allControls.Count(c => c.WasInExpressionBox)}");

            Debug.WriteLine($"\nControls in Repeating Sections:");
            var inRepeating = allControls.Where(c => c.IsInRepeatingSection).ToList();
            Debug.WriteLine($"  Total: {inRepeating.Count}");
            var bySection = inRepeating.GroupBy(c => c.RepeatingSectionName)
                .Select(g => new { Section = g.Key, Count = g.Count() });
            foreach (var sectionGroup in bySection)
            {
                Debug.WriteLine($"  {sectionGroup.Section}: {sectionGroup.Count} controls");
            }

            Debug.WriteLine($"\nDETAILED CONTROL LIST:");
            foreach (var ctrl in allControls)
            {
                Debug.WriteLine($"  [{ctrl.DocIndex}] {ctrl.Name} ({ctrl.Type})");
                if (!string.IsNullOrEmpty(ctrl.Binding))
                    Debug.WriteLine($"      Binding: {ctrl.Binding}");
                if (ctrl.IsInRepeatingSection)
                    Debug.WriteLine($"      In Repeating: {ctrl.RepeatingSectionName}");
                if (!string.IsNullOrEmpty(ctrl.ParentSection))
                    Debug.WriteLine($"      In Section: {ctrl.ParentSection}");
                if (ctrl.WasInExpressionBox)
                    Debug.WriteLine($"      Was in ExpressionBox");
            }

            Debug.WriteLine($"========================================\n");

            debugInfo.ControlsExtracted = allControls.Count;
            return new List<ControlDefinition>(allControls);
        }

        private void ResetParserState()
        {
            sectionStack = new Stack<SectionContext>();
            sections = new List<SectionInfo>();
            allControls = new List<ControlDefinition>();
            processedControls = new HashSet<string>();
            sectionRowCounters = new Dictionary<string, int>();
            repeatingContextStack = new Stack<RepeatingContext>();
            recentLabels = new Queue<LabelInfo>();
            controlsById = new Dictionary<string, ControlDefinition>();
            processedTemplates = new HashSet<string>();
            docIndexCounter = 0;
            currentRow = 1;
            currentCol = 1;
            inTableRow = false;
            insideXslTemplate = false;
            currentTemplateMode = null;
            insideExpressionBox = false;
            expressionBoxDepth = 0;
        }

        public List<SectionInfo> GetSections()
            => new List<SectionInfo>(sections ?? new List<SectionInfo>());

        public Dictionary<string, ControlDefinition> GetControlsById()
            => new Dictionary<string, ControlDefinition>(controlsById ?? new Dictionary<string, ControlDefinition>());

        private void ProcessElement(XElement elem)
        {
            if (elem == null) return;

            debugInfo.TotalElementsExamined++;
            var elemName = elem.Name.LocalName.ToLower();

            // Track element types
            if (!debugInfo.ElementTypeCounts.ContainsKey(elemName))
                debugInfo.ElementTypeCounts[elemName] = 0;
            debugInfo.ElementTypeCounts[elemName]++;

            // Enhanced debug logging
            var binding = GetAttributeValue(elem, "binding");
            var xctname = GetAttributeValue(elem, "xctname");
            var ctrlId = GetAttributeValue(elem, "CtrlId");

            if (!string.IsNullOrEmpty(binding))
            {
                debugInfo.BindingPaths.Add(binding);
                Debug.WriteLine($"[PROCESS] Found element with binding: {binding}, type: {elemName}, xctname: {xctname}");
            }

            // SPECIAL HANDLING FOR FONT ELEMENTS
            // Font elements often wrap labels AND controls in InfoPath
            if (elemName == "font")
            {
                ProcessFontElement(elem);
                return;
            }

            // Check for ExpressionBox FIRST - handle specially
            if (IsExpressionBox(elem))
            {
                ProcessExpressionBox(elem);
                return; // Don't process children normally - we'll handle them specially
            }

            // Check if this is a repeating section div
            if (IsRepeatingSection(elem))
            {
                ProcessRepeatingSection(elem);
                return;
            }

            // Handle XSL templates
            if (elemName == "template" && elem.Attribute("mode") != null)
            {
                ProcessXslTemplate(elem);
                return;
            }

            // Handle apply-templates
            if (elemName == "apply-templates")
            {
                var mode = elem.Attribute("mode")?.Value;
                var select = elem.Attribute("select")?.Value;

                if (!string.IsNullOrEmpty(mode))
                {
                    Debug.WriteLine($"[PROCESS] Found apply-templates with mode: {mode}, select: {select}");
                    string contextKey = mode;
                    if (repeatingContextStack.Count > 0)
                    {
                        contextKey = $"{repeatingContextStack.Peek().Name}::{mode}";
                    }

                    bool isRepeatingPattern = !string.IsNullOrEmpty(select) && IsRepeatingSelectPattern(select);

                    if (isRepeatingPattern)
                    {
                        string repeatKey = $"repeat::{contextKey}::{select}";
                        if (!processedTemplates.Contains(repeatKey))
                        {
                            processedTemplates.Add(repeatKey);
                            ProcessRepeatingApplyTemplates(elem, mode, select);
                            processedTemplates.Remove(repeatKey);
                        }
                        else
                        {
                            Debug.WriteLine($"[PROCESS] Repeating pattern {select} already processed in context {contextKey}");
                        }
                    }
                    else
                    {
                        var matchingTemplate = FindTemplate(elem.Document, mode);
                        if (matchingTemplate != null)
                        {
                            ProcessXslTemplate(matchingTemplate);
                        }
                    }
                    return;
                }
            }

            // Check for standalone labels (but not font elements, which are handled above)
            if (IsStandaloneLabelElement(elem) && !insideXslTemplate)
            {
                TrackPotentialSectionLabel(elem);
            }

            // Check for new row indicators
            if (IsNewRowIndicator(elem))
            {
                HandleNewRow(elem);
            }

            // Check for placeholder text
            if (IsPlaceholderText(elem))
            {
                foreach (var child in elem.Elements())
                {
                    ProcessElement(child);
                }
                return;
            }

            // Handle regular sections
            if (IsRegularSection(elem))
            {
                ProcessRegularSection(elem);
                return;
            }

            // Handle repeating tables
            if (IsRepeatingTable(elem))
            {
                ProcessRepeatingTable(elem);
                return;
            }

            // Try to extract control
            var control = TryExtractControl(elem);
            if (control != null)
            {
                bool shouldSkip = false;

                if (control.Properties != null && control.Properties.ContainsKey("CtrlId"))
                {
                    var controlCtrlId = control.Properties["CtrlId"];
                    if (!string.IsNullOrEmpty(controlCtrlId) && controlsById.ContainsKey(controlCtrlId))
                    {
                        var existing = controlsById[controlCtrlId];
                        bool sameRepeatingContext = (existing.IsInRepeatingSection == (repeatingContextStack.Count > 0));

                        if (sameRepeatingContext && existing.IsInRepeatingSection)
                        {
                            var currentRepeatingSectionName = repeatingContextStack.Count > 0 ?
                                repeatingContextStack.Peek().Name : "";

                            if (existing.RepeatingSectionName == currentRepeatingSectionName)
                            {
                                Debug.WriteLine($"[DUPLICATE] Control {controlCtrlId} already exists in same repeating context, skipping");
                                debugInfo.DuplicatesSkipped.Add($"{controlCtrlId} in {currentRepeatingSectionName}");
                                shouldSkip = true;
                            }
                        }
                        else if (sameRepeatingContext && !existing.IsInRepeatingSection)
                        {
                            Debug.WriteLine($"[DUPLICATE] Control {controlCtrlId} already exists in main form, skipping");
                            debugInfo.DuplicatesSkipped.Add($"{controlCtrlId} in main form");
                            shouldSkip = true;
                        }
                    }
                }

                if (!shouldSkip)
                {
                    ApplyControlContext(control);
                    control.GridPosition = currentRow + GetColumnLetter(currentCol);
                    control.DocIndex = ++docIndexCounter;

                    // Mark if from ExpressionBox
                    if (insideExpressionBox)
                    {
                        control.WasInExpressionBox = true;
                    }

                    if (control.Properties != null && control.Properties.ContainsKey("CtrlId"))
                    {
                        var controlCtrlId = control.Properties["CtrlId"];
                        if (!string.IsNullOrEmpty(controlCtrlId))
                        {
                            string contextualKey = controlCtrlId;
                            if (repeatingContextStack.Count > 0)
                            {
                                contextualKey = $"{repeatingContextStack.Peek().Name}::{controlCtrlId}";
                            }
                            controlsById[contextualKey] = control;
                        }
                    }

                    currentCol++;
                    allControls.Add(control);
                    debugInfo.ProcessedElements.Add($"{control.Name} ({control.Type}) at {control.GridPosition}");

                    Debug.WriteLine($"[CONTROL DETECTED] Name: {control.Name}, Type: {control.Type}, Binding: {control.Binding}");
                    Debug.WriteLine($"  Grid: {control.GridPosition}, DocIndex: {control.DocIndex}");
                    if (control.IsInRepeatingSection)
                    {
                        Debug.WriteLine($"  In Repeating Section: {control.RepeatingSectionName}");
                    }
                    if (!string.IsNullOrEmpty(control.ParentSection))
                    {
                        Debug.WriteLine($"  In Section: {control.ParentSection}");
                    }
                    if (control.WasInExpressionBox)
                    {
                        Debug.WriteLine($"  Was in ExpressionBox");
                    }
                    Debug.WriteLine($"  Total controls so far: {allControls.Count}");
                }

                // Don't process children if this is a DatePicker (composite control)
                if (control.Type == "DatePicker")
                {
                    Debug.WriteLine($"[CONTROL DETECTED] DatePicker - skipping child processing");
                    return;
                }

                return; // Don't process children if we've extracted a control
            }
            else
            {
                // Log why we didn't extract a control from this element
                if (!string.IsNullOrEmpty(binding) || !string.IsNullOrEmpty(xctname) || !string.IsNullOrEmpty(ctrlId))
                {
                    debugInfo.SkippedElements.Add($"{elemName} - binding:{binding}, xctname:{xctname}, ctrlId:{ctrlId}");
                    Debug.WriteLine($"[SKIPPED] Element {elemName} with binding:{binding}, xctname:{xctname}, ctrlId:{ctrlId}");
                }
            }

            // Process child elements recursively
            foreach (var child in elem.Elements())
            {
                ProcessElement(child);
            }

            // Handle table row end
            if (elemName == "tr")
            {
                inTableRow = false;
            }
        }

        private bool IsExpressionBox(XElement elem)
        {
            var xctname = GetAttributeValue(elem, "xctname");
            if (xctname?.Equals("ExpressionBox", StringComparison.OrdinalIgnoreCase) == true)
                return true;

            var className = elem.Attribute("class")?.Value ?? "";
            if (className.Contains("xdExpressionBox"))
                return true;

            // Also check xd:xctname
            var xdXctname = GetAttributeValue(elem, "xd:xctname");
            if (xdXctname?.Equals("ExpressionBox", StringComparison.OrdinalIgnoreCase) == true)
                return true;

            return false;
        }

        private void ProcessExpressionBox(XElement elem)
        {
            expressionBoxDepth++;
            insideExpressionBox = true;

            var ctrlId = GetAttributeValue(elem, "CtrlId");
            var binding = GetAttributeValue(elem, "binding");

            Debug.WriteLine($"\n[EXPRESSION BOX] Processing ExpressionBox - CtrlId: {ctrlId}, binding: {binding}, depth: {expressionBoxDepth}");
            debugInfo.ExpressionBoxContents.Add($"ExpressionBox {ctrlId} with binding {binding}");

            // Look for controls INSIDE the expression box
            var innerControls = ExtractControlsFromExpressionBox(elem);

            Debug.WriteLine($"[EXPRESSION BOX] Found {innerControls.Count} controls inside ExpressionBox");

            foreach (var control in innerControls)
            {
                control.WasInExpressionBox = true;
                ApplyControlContext(control);
                control.GridPosition = currentRow + GetColumnLetter(currentCol);
                control.DocIndex = ++docIndexCounter;

                currentCol++;
                allControls.Add(control);

                Debug.WriteLine($"  Extracted from ExpressionBox: {control.Name} ({control.Type}) - {control.Binding}");
                debugInfo.ExpressionBoxContents.Add($"  -> {control.Name} ({control.Type})");
            }

            // If no controls found inside, process children normally
            if (innerControls.Count == 0)
            {
                Debug.WriteLine($"[EXPRESSION BOX] No direct controls found, processing children recursively");
                foreach (var child in elem.Elements())
                {
                    ProcessElement(child);
                }
            }

            expressionBoxDepth--;
            if (expressionBoxDepth == 0)
            {
                insideExpressionBox = false;
            }

            Debug.WriteLine($"[EXPRESSION BOX] Finished processing ExpressionBox {ctrlId}");
        }

        private List<ControlDefinition> ExtractControlsFromExpressionBox(XElement expressionBox)
        {
            var controls = new List<ControlDefinition>();

            // Look deeper into the ExpressionBox structure
            // ExpressionBoxes often contain xsl:value-of or text content

            // Check for xsl:value-of elements that reference data
            var valueOfElements = expressionBox.Descendants()
                .Where(e => e.Name.LocalName == "value-of" &&
                           e.Attribute("select") != null)
                .ToList();

            foreach (var valueOfElem in valueOfElements)
            {
                var select = valueOfElem.Attribute("select")?.Value;
                if (!string.IsNullOrEmpty(select) && select.StartsWith("my:"))
                {
                    // This is a data-bound expression box showing a field value
                    var control = new ControlDefinition
                    {
                        Type = "ExpressionBox",
                        Binding = select,
                        Name = GenerateControlName(expressionBox, "", select, "ExpressionBox"),
                        Label = expressionBox.Attribute("title")?.Value ?? "",
                        DocIndex = ++docIndexCounter,
                        WasInExpressionBox = true
                    };

                    controls.Add(control);
                    Debug.WriteLine($"    Extracted ExpressionBox field reference: {control.Name} with binding: {control.Binding}");
                }
            }

            // If no value-of elements, check if the ExpressionBox itself has a meaningful binding
            if (controls.Count == 0)
            {
                var binding = GetAttributeValue(expressionBox, "binding");
                if (!string.IsNullOrEmpty(binding) && !binding.StartsWith("string("))
                {
                    var control = new ControlDefinition
                    {
                        Type = "ExpressionBox",
                        Binding = binding,
                        Name = GenerateControlName(expressionBox, "", binding, "ExpressionBox"),
                        Label = expressionBox.Attribute("title")?.Value ?? "",
                        DocIndex = ++docIndexCounter,
                        WasInExpressionBox = true
                    };

                    controls.Add(control);
                }
            }

            return controls;
        }
        // [Include all the other methods from the original code - they remain the same]
        // I'm including key ones that were modified or are essential:

        private XElement FindTemplate(XDocument doc, string mode)
        {
            if (doc == null || string.IsNullOrEmpty(mode))
                return null;

            try
            {
                var root = doc.Root;
                if (root == null)
                    return null;

                var ns = root.Name.Namespace;
                var template = doc.Descendants(ns + "template")
                    .FirstOrDefault(t => t.Attribute("mode")?.Value == mode);

                if (template != null)
                {
                    Debug.WriteLine($"Found template with mode: {mode}");
                }
                else
                {
                    Debug.WriteLine($"No template found with mode: {mode}");
                }

                return template;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error finding template with mode {mode}: {ex.Message}");
                return null;
            }
        }

        private bool IsRepeatingSelectPattern(string select)
        {
            if (string.IsNullOrEmpty(select))
                return false;

            if (select.Contains("/"))
            {
                var parts = select.Split('/');
                if (parts.Length >= 2)
                {
                    var parent = GetElementName(parts[parts.Length - 2]);
                    var child = GetElementName(parts[parts.Length - 1]);

                    if (IsCollectionChildPattern(parent, child))
                    {
                        Debug.WriteLine($"Detected repeating pattern: {parent}/{child}");
                        return true;
                    }
                }
            }

            return false;
        }

        private string GetElementName(string qualifiedName)
        {
            if (qualifiedName.Contains(":"))
                return qualifiedName.Split(':').Last();
            return qualifiedName;
        }

        private bool IsCollectionChildPattern(string parent, string child)
        {
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(child))
                return false;

            parent = parent.ToLower();
            child = child.ToLower();

            if (parent.EndsWith("s") && parent.Substring(0, parent.Length - 1) == child)
                return true;

            if (parent.EndsWith("es") && parent.Substring(0, parent.Length - 2) == child)
                return true;

            if (parent.EndsWith("ies") && child + "ies" == parent.Replace(parent.Substring(0, parent.Length - 3), child))
                return true;

            string[] collectionSuffixes = { "list", "collection", "array", "set", "items", "entries" };
            foreach (var suffix in collectionSuffixes)
            {
                if (parent.EndsWith(suffix) && parent.StartsWith(child))
                    return true;
            }

            if (parent == child)
                return true;

            return false;
        }

        private bool IsConditionalTemplateMode(string mode, XDocument doc)
        {
            if (string.IsNullOrEmpty(mode) || doc == null)
                return false;

            // Find the template with this mode
            var ns = doc.Root?.Name.Namespace;
            if (ns == null)
                return false;

            var template = doc.Descendants(ns + "template")
                .FirstOrDefault(t => t.Attribute("mode")?.Value == mode);

            if (template == null)
                return false;

            // Check if the template contains xsl:if (conditional logic)
            var hasConditional = template.Descendants(ns + "if").Any();

            // Check if it has a section div with a CtrlId (like CTRL57 for Round Trip)
            var hasSectionDiv = template.Descendants()
                .Any(e => GetAttributeValue(e, "CtrlId") == "CTRL57" || // Round Trip specific
                          (GetAttributeValue(e, "xctname") == "Section" &&
                           e.Attribute("class")?.Value?.Contains("xdSection") == true));

            // If it has conditional logic AND a section div, it's a conditional section template
            return hasConditional && hasSectionDiv;
        }

        private void CreateRepeatingSection(string name, string binding, string type)
        {
            // Ensure we always have a name
            var sectionName = GetOrGenerateRepeatingSectionName(name, binding, type);

            var repeatingContext = new RepeatingContext
            {
                Name = sectionName,
                DisplayName = sectionName,
                Binding = binding,
                Type = type,
                Depth = repeatingContextStack.Count
            };

            repeatingContextStack.Push(repeatingContext);

            // Also add to sections list
            sections.Add(new SectionInfo
            {
                Name = sectionName,
                Type = "repeating",
                StartRow = currentRow
            });
        }

        private string CleanSectionName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            // Remove special characters
            name = Regex.Replace(name, @"[^\w\s]", " ");
            // Collapse multiple spaces
            name = Regex.Replace(name, @"\s+", " ").Trim();
            // Replace spaces with underscores for consistency
            name = name.Replace(" ", "_");

            // Ensure it's not empty after cleaning
            if (string.IsNullOrWhiteSpace(name))
                return null;

            return name;
        }

        private string CleanTableName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            // Remove special characters and clean up
            name = Regex.Replace(name, @"[^\w\s]", " ");
            name = Regex.Replace(name, @"\s+", "_").Trim('_');

            // Convert to proper case if all lowercase
            if (name.ToLower() == name)
            {
                var words = name.Split('_');
                for (int i = 0; i < words.Length; i++)
                {
                    if (words[i].Length > 0)
                    {
                        words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1);
                    }
                }
                name = string.Join("_", words);
            }

            // Ensure it's not empty after cleaning
            if (string.IsNullOrWhiteSpace(name))
                return null;

            return name;
        }

        private string ExtractNameFromTableHeaders(XElement tableElem)
        {
            // Look for meaningful text in header cells
            var headerCells = tableElem.Descendants()
                .Where(e => (e.Name.LocalName == "th" ||
                            (e.Name.LocalName == "td" &&
                             e.Ancestors().Any(a => a.Name.LocalName == "thead"))))
                .ToList();

            foreach (var cell in headerCells)
            {
                var text = GetDirectTextContent(cell).Trim();

                // Skip generic headers
                if (!string.IsNullOrWhiteSpace(text) &&
                    text.Length > 2 &&
                    !text.Equals("Date", StringComparison.OrdinalIgnoreCase) &&
                    !text.Equals("Description", StringComparison.OrdinalIgnoreCase) &&
                    !text.Equals("Category", StringComparison.OrdinalIgnoreCase) &&
                    !text.Equals("Cost", StringComparison.OrdinalIgnoreCase) &&
                    !text.Equals("Amount", StringComparison.OrdinalIgnoreCase))
                {
                    return text;
                }
            }

            // Look for a label before the table
            var prevSibling = GetPreviousSiblingWithContent(tableElem);
            if (prevSibling != null)
            {
                var labelText = GetDirectTextContent(prevSibling).Trim();
                if (!string.IsNullOrWhiteSpace(labelText) && labelText.Length > 2)
                {
                    return labelText;
                }
            }

            return "";
        }

        private XElement GetPreviousSiblingWithContent(XElement elem)
        {
            var parent = elem.Parent;
            if (parent == null) return null;

            bool foundCurrent = false;
            XElement previous = null;

            foreach (var child in parent.Elements().Reverse())
            {
                if (foundCurrent)
                {
                    var text = GetDirectTextContent(child).Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return child;
                    }
                }
                if (child == elem)
                {
                    foundCurrent = true;
                }
            }

            return null;
        }
        private string GetTableBinding(XElement elem)
        {
            var binding = "";

            // First check tbody for binding attributes
            var tbody = elem.Descendants().FirstOrDefault(e => e.Name.LocalName == "tbody");
            if (tbody != null)
            {
                binding = GetAttributeValue(tbody, "repeating") ??
                         GetAttributeValue(tbody, "xd:repeating") ??
                         GetAttributeValue(tbody, "binding") ??
                         GetAttributeValue(tbody, "xd:xmlToEdit");

                // Also check xctname on tbody
                var xctname = GetAttributeValue(tbody, "xctname");
                if (xctname == "repeatingtable" && string.IsNullOrEmpty(binding))
                {
                    // Try to find binding from other attributes
                    binding = GetAttributeValue(tbody, "xd:xmlToEdit");
                }
            }

            // If no binding found in tbody, check the table element itself
            if (string.IsNullOrEmpty(binding))
            {
                binding = GetAttributeValue(elem, "repeating") ??
                          GetAttributeValue(elem, "binding") ??
                          GetAttributeValue(elem, "xd:xmlToEdit");
            }

            return binding;
        }












        private string GetOrGenerateRepeatingSectionName(string rawName, string binding, string type)
        {
            string baseName = null;

            // Try to get a meaningful name from various sources

            // 1. Check if rawName is valid
            if (!string.IsNullOrWhiteSpace(rawName) &&
                !rawName.StartsWith("CTRL", StringComparison.OrdinalIgnoreCase) &&
                !rawName.Equals("RepeatingSection", StringComparison.OrdinalIgnoreCase) &&
                !rawName.Equals("RepeatingTable", StringComparison.OrdinalIgnoreCase))
            {
                baseName = CleanSectionName(rawName);
            }

            // 2. Try to extract from binding if no valid name yet
            if (string.IsNullOrWhiteSpace(baseName) && !string.IsNullOrWhiteSpace(binding))
            {
                baseName = ExtractNameFromBinding(binding);
                if (baseName == "RepeatingSection" || baseName == "RepeatingTable")
                {
                    baseName = null; // Reset if we got a generic name
                }
            }

            // 3. If still no name, generate a default one
            if (string.IsNullOrWhiteSpace(baseName))
            {
                repeatingSectionCounter++;
                baseName = type == "table"
                    ? $"Table_Repeating{repeatingSectionCounter}"
                    : $"Section_Repeating{repeatingSectionCounter}";
            }

            // Ensure uniqueness
            return EnsureUniqueSectionName(baseName);
        }

        private string EnsureUniqueSectionName(string baseName)
        {
            // If no base name provided, generate a default one
            if (string.IsNullOrWhiteSpace(baseName))
            {
                repeatingSectionCounter++;
                baseName = $"Section_Repeating{repeatingSectionCounter}";
            }

            // Check if this exact name has been used
            if (!viewRepeatingSectionNames.Contains(baseName))
            {
                viewRepeatingSectionNames.Add(baseName);
                return baseName;
            }

            // Name exists, need to make it unique by adding a counter
            int counter = 2;
            string uniqueName = $"{baseName}_{counter}";

            while (viewRepeatingSectionNames.Contains(uniqueName))
            {
                counter++;
                uniqueName = $"{baseName}_{counter}";
            }

            viewRepeatingSectionNames.Add(uniqueName);
            return uniqueName;
        }



        private void ProcessRepeatingApplyTemplates(XElement elem, string mode, string select)
        {
            Debug.WriteLine($"\n[REPEATING APPLY-TEMPLATES] Processing - mode: {mode}, select: {select}");

            // Extract section name from the select pattern
            string sectionName = ExtractSectionNameFromSelect(select);

            // If we're already in a repeating context, this is nested
            if (repeatingContextStack.Count > 0)
            {
                var parentContext = repeatingContextStack.Peek();

                // For nested sections, combine parent and child names
                string combinedName = $"{parentContext.DisplayName}_{sectionName}";

                Debug.WriteLine($"  Creating nested repeating section: {combinedName} (parent: {parentContext.DisplayName})");

                // Use the combined name
                sectionName = combinedName;
            }
            else
            {
                Debug.WriteLine($"  Creating top-level repeating section: {sectionName}");
            }

            // Ensure unique name
            sectionName = EnsureUniqueSectionName(sectionName);

            // Check if we're already in this repeating context
            bool alreadyInContext = repeatingContextStack.Any(r =>
                r.Binding == select || r.Name == sectionName);

            if (!alreadyInContext)
            {
                // Create repeating context with combined name for nested sections
                var repeatingContext = new RepeatingContext
                {
                    Name = sectionName,
                    Binding = select,
                    Type = "section",
                    DisplayName = sectionName,
                    Depth = repeatingContextStack.Count
                };

                repeatingContextStack.Push(repeatingContext);

                // Create section info
                var sectionInfo = new SectionInfo
                {
                    Name = sectionName,
                    Type = "repeating",
                    CtrlId = "",
                    StartRow = currentRow
                };
                sections.Add(sectionInfo);

                Debug.WriteLine($"  Created repeating section: {sectionName} at depth {repeatingContext.Depth}");
            }
            else
            {
                Debug.WriteLine($"  Already in context for {sectionName}, not creating duplicate");
            }

            // Find and process the corresponding template
            var doc = elem.Document;
            if (doc != null)
            {
                var ns = elem.Name.Namespace;
                var matchingTemplate = doc.Descendants(ns + "template")
                    .FirstOrDefault(t => t.Attribute("mode")?.Value == mode);

                if (matchingTemplate != null)
                {
                    Debug.WriteLine($"  Processing template for mode {mode}");
                    int controlCountBefore = allControls.Count;

                    foreach (var child in matchingTemplate.Elements())
                    {
                        ProcessElement(child);
                    }

                    int controlsAdded = allControls.Count - controlCountBefore;
                    Debug.WriteLine($"  Added {controlsAdded} controls from template");
                }
                else
                {
                    Debug.WriteLine($"  No matching template found for mode {mode}");
                }
            }

            if (!alreadyInContext)
            {
                repeatingContextStack.Pop();
                var sectionInfo = sections.LastOrDefault(s => s.Name == sectionName);
                if (sectionInfo != null)
                    sectionInfo.EndRow = currentRow;

                Debug.WriteLine($"  Finished processing repeating section {sectionName}");
            }
        }


        private string ExtractSectionNameFromSelect(string select)
        {
            if (string.IsNullOrWhiteSpace(select))
                return "Section_Repeating" + (++repeatingSectionCounter);

            // Get the last meaningful part of the path
            var parts = select.Split('/');
            string relevantPart = "";

            // Look for the most meaningful part
            for (int i = parts.Length - 1; i >= 0; i--)
            {
                var part = parts[i].Replace("my:", "").Trim();
                if (!string.IsNullOrEmpty(part) && part != "*")
                {
                    relevantPart = part;
                    break;
                }
            }

            if (string.IsNullOrEmpty(relevantPart))
                return "Section_Repeating" + (++repeatingSectionCounter);

            // Clean and format the name
            // Convert to readable format (entertainmentDetails -> Entertainment_Details)
            var formatted = ConvertToReadableName(relevantPart);

            return formatted;
        }

        private string ConvertToReadableName(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var result = new StringBuilder();

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];

                // Insert underscore before capitals (except first char)
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(input[i - 1]))
                {
                    result.Append('_');
                }

                // Capitalize first letter
                if (i == 0)
                {
                    result.Append(char.ToUpper(c));
                }
                else
                {
                    result.Append(c);
                }
            }

            return result.ToString();
        }

        private void ApplyControlContext(ControlDefinition control)
        {
            Debug.WriteLine($"[APPLY CONTEXT] Applying context to control {control.Name}");

            // Apply cosmetic section context
            if (!string.IsNullOrEmpty(currentSectionName))
            {
                control.ParentSection = currentSectionName;
                control.SectionType = "section";
                Debug.WriteLine($"  Set parent section: {currentSectionName}");
            }

            // Apply repeating context - use the LAST (most specific) repeating section
            if (repeatingContextStack != null && repeatingContextStack.Count > 0)
            {
                var currentRepeating = repeatingContextStack.Peek();

                control.IsInRepeatingSection = true;
                control.RepeatingSectionName = currentRepeating.DisplayName; // This will now be the combined name
                control.RepeatingSectionBinding = currentRepeating.Binding;

                Debug.WriteLine($"  Set repeating section: {currentRepeating.DisplayName}");
                Debug.WriteLine($"  Repeating binding: {currentRepeating.Binding}");

                // For debugging, track the nesting depth
                if (repeatingContextStack.Count > 1)
                {
                    control.Properties["NestingDepth"] = repeatingContextStack.Count.ToString();
                    Debug.WriteLine($"  Nesting depth: {repeatingContextStack.Count}");
                }
            }
        }

        private void ProcessXslTemplate(XElement templateElem)
        {
            var mode = templateElem.Attribute("mode")?.Value;
            var match = templateElem.Attribute("match")?.Value;

            Debug.WriteLine($"\n[XSL TEMPLATE] Processing template - mode: {mode}, match: {match}");

            // Log current context
            if (repeatingContextStack.Count > 0)
            {
                Debug.WriteLine($"  In repeating context: {string.Join(" > ", repeatingContextStack.Select(r => r.Name))}");
            }

            // Check if this template has already created its sections
            var ns = templateElem.Name.Namespace;
            var sectionDivs = templateElem.Descendants()
                .Where(e => GetAttributeValue(e, "xctname") == "Section" ||
                            e.Attribute("class")?.Value?.Contains("xdSection") == true)
                .ToList();

            foreach (var sectionDiv in sectionDivs)
            {
                var ctrlId = GetAttributeValue(sectionDiv, "CtrlId");
                if (!string.IsNullOrEmpty(ctrlId))
                {
                    // Check if we've already processed this section
                    var existingSection = sections.FirstOrDefault(s => s.CtrlId == ctrlId);
                    if (existingSection != null)
                    {
                        Debug.WriteLine($"[XSL TEMPLATE] Template {mode} contains section {ctrlId} which already exists, skipping entire template");
                        return; // Skip this entire template
                    }
                }
            }

            // Build a unique template key based on current context
            string templateKey = mode;

            // If we're in a repeating context, include that in the key
            if (repeatingContextStack.Count > 0)
            {
                var contexts = string.Join(">", repeatingContextStack.Select(r => r.Name));
                templateKey = $"{contexts}::{mode}";
            }

            // If we're in a section context, include that too
            if (sectionStack.Count > 0)
            {
                var sections = string.Join(">", sectionStack.Select(s => s.Name));
                templateKey = $"{templateKey}::{sections}";
            }

            // Check if we've already processed this exact template in this exact context
            if (processedTemplates.Contains(templateKey))
            {
                Debug.WriteLine($"[XSL TEMPLATE] Template {templateKey} already processed in this exact context, skipping");
                return;
            }

            Debug.WriteLine($"[XSL TEMPLATE] Processing new template with key: {templateKey}");
            processedTemplates.Add(templateKey);

            var previousTemplateMode = currentTemplateMode;
            currentTemplateMode = mode;
            insideXslTemplate = true;

            try
            {
                // Check for conditional content (xsl:if)
                var ifElements = templateElem.Descendants(ns + "if").ToList();

                if (ifElements.Any())
                {
                    Debug.WriteLine($"[XSL TEMPLATE] Found {ifElements.Count} conditional(s) in template");
                    bool hasProcessedConditional = false;

                    foreach (var ifElement in ifElements)
                    {
                        var testCondition = ifElement.Attribute("test")?.Value;
                        Debug.WriteLine($"  Found conditional with test: {testCondition}");

                        // Check if this conditional creates a section
                        var sectionDiv = ifElement.Descendants()
                            .FirstOrDefault(e => GetAttributeValue(e, "xctname") == "Section" ||
                                                e.Attribute("class")?.Value?.Contains("xdSection") == true);

                        if (sectionDiv != null)
                        {
                            var ctrlId = GetAttributeValue(sectionDiv, "CtrlId");

                            // CRITICAL: Only process if we're in the right context
                            // If we're NOT in a repeating section, skip conditional sections that belong to repeating sections
                            if (repeatingContextStack.Count == 0 && IsConditionalForRepeatingSection(testCondition, ctrlId))
                            {
                                Debug.WriteLine($"  Skipping conditional section {ctrlId} - belongs to repeating section but not in repeating context");
                                continue;
                            }

                            // If we ARE in a repeating section, only process if it's for our current repeating section
                            if (repeatingContextStack.Count > 0)
                            {
                                var currentRepeating = repeatingContextStack.Peek();
                                if (!IsConditionalForCurrentContext(testCondition, currentRepeating))
                                {
                                    Debug.WriteLine($"  Skipping conditional section {ctrlId} - not for current repeating context");
                                    continue;
                                }
                            }

                            // Check if already processed
                            if (IsSectionAlreadyProcessed(ctrlId, mode))
                            {
                                Debug.WriteLine($"  Section {ctrlId} already processed, skipping");
                                continue;
                            }

                            ProcessConditionalSection(ifElement, mode);
                            hasProcessedConditional = true;
                        }
                        else
                        {
                            // Process conditional content without creating a section
                            // BUT only if we're in the right context
                            if (ShouldProcessContentInCurrentContext(ifElement))
                            {
                                Debug.WriteLine($"  Processing conditional content (no section)");
                                foreach (var child in ifElement.Elements())
                                {
                                    ProcessElement(child);
                                }
                            }
                        }
                    }

                    // Process non-conditional elements ONLY if we haven't processed a conditional section
                    // AND we're not in a situation where this would create duplicates
                    if (!hasProcessedConditional && !WouldCreateDuplicates(templateElem))
                    {
                        var nonConditionalElements = templateElem.Elements()
                            .Where(e => e.Name.LocalName != "if");

                        Debug.WriteLine($"[XSL TEMPLATE] Processing non-conditional elements");
                        foreach (var child in nonConditionalElements)
                        {
                            ProcessElement(child);
                        }
                    }
                }
                else
                {
                    // No conditional logic - but still check if this would create duplicates
                    if (!WouldCreateDuplicates(templateElem))
                    {
                        Debug.WriteLine($"[XSL TEMPLATE] Processing template without conditionals");
                        foreach (var child in templateElem.Elements())
                        {
                            ProcessElement(child);
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"[XSL TEMPLATE] Skipping template - would create duplicates");
                    }
                }
            }
            finally
            {
                insideXslTemplate = false;
                currentTemplateMode = previousTemplateMode;
                processedTemplates.Remove(templateKey);
                Debug.WriteLine($"[XSL TEMPLATE] Finished processing template {mode}");
            }
        }


        private bool WouldCreateDuplicates(XElement templateElem)
        {
            // Check if this template contains controls that have already been processed
            var controlElements = templateElem.Descendants()
                .Where(e => !string.IsNullOrEmpty(GetAttributeValue(e, "CtrlId")))
                .ToList();

            foreach (var controlElem in controlElements)
            {
                var ctrlId = GetAttributeValue(controlElem, "CtrlId");

                // Build the contextual key to check
                string keyToCheck = ctrlId;
                if (repeatingContextStack.Count > 0)
                {
                    keyToCheck = $"{repeatingContextStack.Peek().Name}::{ctrlId}";
                }

                if (controlsById.ContainsKey(keyToCheck))
                {
                    Debug.WriteLine($"Template would create duplicate control {ctrlId} in same context, skipping");
                    return true;
                }

                // Also check if the control exists in a different context
                // If it exists in main form but we're in repeating, that's OK
                if (controlsById.ContainsKey(ctrlId) && repeatingContextStack.Count > 0)
                {
                    // Control exists in main form, but we're creating it for repeating section
                    // This is NOT a duplicate
                    continue;
                }
            }

            return false;
        }



        private bool IsConditionalForRepeatingSection(string testCondition, string ctrlId)
        {
            // Check if this is the Round Trip conditional (CTRL57)
            if (ctrlId == "CTRL57")
            {
                return true; // Round Trip belongs to Trips repeating section
            }

            // Check test condition for parent context indicators
            if (!string.IsNullOrEmpty(testCondition))
            {
                // Conditions with "../" typically reference parent context
                if (testCondition.Contains("../my:"))
                {
                    return true;
                }

                // Check for specific field patterns that indicate repeating context
                if (testCondition.Contains("my:isRoundTrip") ||
                    testCondition.Contains("my:roundTrip"))
                {
                    return true; // This is specifically for the round trip in trips
                }
            }

            return false;
        }

        private bool IsConditionalForCurrentContext(string testCondition, RepeatingContext currentContext)
        {
            // For Round Trip, it should only be processed when we're in the Trips context
            if (testCondition?.Contains("isRoundTrip") == true)
            {
                return currentContext.Name == "Trips" ||
                       currentContext.Binding?.Contains("trips") == true;
            }

            return true; // Default to true for other conditionals
        }

        private bool ShouldProcessContentInCurrentContext(XElement ifElement)
        {
            var testCondition = ifElement.Attribute("test")?.Value;

            // If we're not in a repeating context, don't process content meant for repeating sections
            if (repeatingContextStack.Count == 0)
            {
                if (testCondition?.Contains("../") == true ||
                    testCondition?.Contains("isRoundTrip") == true)
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsSectionAlreadyProcessed(string ctrlId, string mode)
        {
            if (string.IsNullOrEmpty(ctrlId))
                return false;

            // Check if section with this CtrlId already exists
            var existingSection = sections.FirstOrDefault(s => s.CtrlId == ctrlId);
            if (existingSection != null)
            {
                Debug.WriteLine($"Section with CtrlId {ctrlId} already exists: {existingSection.Name} (Type: {existingSection.Type})");
                return true;
            }

            return false;
        }




        private void ProcessConditionalSection(XElement ifElement, string mode)
        {
            if (ifElement == null) return;

            var testCondition = ifElement.Attribute("test")?.Value;
            Debug.WriteLine($"Processing conditional section - test: {testCondition}");

            // Find section div within the conditional
            var sectionDiv = ifElement.Descendants()
                .FirstOrDefault(e =>
                    e.Attribute("class")?.Value?.Contains("xdSection") == true ||
                    GetAttributeValue(e, "xctname") == "Section");

            if (sectionDiv != null)
            {
                var ctrlId = GetAttributeValue(sectionDiv, "CtrlId");
                var caption = GetAttributeValue(sectionDiv, "caption_0") ??
                              GetAttributeValue(sectionDiv, "caption");

                // Check if already processed
                if (IsSectionAlreadyProcessed(ctrlId, mode))
                {
                    Debug.WriteLine($"Section {ctrlId} already processed in ProcessConditionalSection, skipping");
                    return;
                }

                // Determine section name
                var sectionName = DetermineSectionNameFromCondition(testCondition, caption, ctrlId, mode);

                // Determine section type based on context
                var sectionType = "conditional";
                if (repeatingContextStack.Count > 0)
                {
                    sectionType = "conditional-in-repeating";
                    var parentRepeating = repeatingContextStack.Peek();
                    Debug.WriteLine($"Conditional section '{sectionName}' is within repeating '{parentRepeating.Name}'");
                }

                var conditionalSection = new SectionContext
                {
                    Name = sectionName,
                    Type = sectionType,
                    StartRow = currentRow,
                    CtrlId = ctrlId,
                    DisplayName = sectionName
                };

                sectionStack.Push(conditionalSection);

                var sectionInfo = new SectionInfo
                {
                    Name = sectionName,
                    Type = conditionalSection.Type,
                    CtrlId = ctrlId,
                    StartRow = currentRow
                };

                sections.Add(sectionInfo);

                // Track controls being added to this section
                var controlCountBefore = allControls.Count;

                // Process the section contents
                foreach (var child in sectionDiv.Elements())
                {
                    ProcessElement(child);
                }

                var controlsAdded = allControls.Count - controlCountBefore;
                Debug.WriteLine($"Added {controlsAdded} controls to section '{sectionName}'");

                sectionStack.Pop();
                sectionInfo.EndRow = currentRow;

                // Store control IDs that belong to this section
                if (!string.IsNullOrEmpty(ctrlId))
                {
                    sectionInfo.ControlIds = allControls
                        .Skip(controlCountBefore)
                        .Where(c => c.Properties?.ContainsKey("CtrlId") == true)
                        .Select(c => c.Properties["CtrlId"])
                        .ToList();
                }
            }
            else
            {
                // No explicit section div - but still check context before processing
                if (ShouldProcessContentInCurrentContext(ifElement))
                {
                    foreach (var child in ifElement.Elements())
                    {
                        ProcessElement(child);
                    }
                }
            }
        }
        private string DetermineSectionNameFromCondition(string testCondition, string caption, string ctrlId, string mode)
        {
            // Priority 1: Use caption if available
            if (!string.IsNullOrEmpty(caption))
                return caption;

            // Priority 2: Extract from condition
            if (!string.IsNullOrEmpty(testCondition))
            {
                // Pattern: Extract field name from condition
                var patterns = new[]
                {
            @"my:(\w+)\s*=",           // my:fieldName = 
            @"my:(\w+)\s*!=",          // my:fieldName !=
            @"\.\.\/my:(\w+)",         // ../my:fieldName
            @"not\(.*my:(\w+).*\)",    // not(...my:fieldName...)
            @"boolean\(my:(\w+)\)",    // boolean(my:fieldName)
        };

                foreach (var pattern in patterns)
                {
                    var match = System.Text.RegularExpressions.Regex.Match(testCondition, pattern);
                    if (match.Success)
                    {
                        var fieldName = match.Groups[1].Value;
                        return ConvertFieldNameToReadable(fieldName);
                    }
                }
            }

            // Priority 3: Use CtrlId
            if (!string.IsNullOrEmpty(ctrlId))
                return $"Section_{ctrlId}";

            // Priority 4: Use mode
            return $"Section_{mode}";
        }

        private string ConvertFieldNameToReadable(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName))
                return fieldName;

            // Remove common prefixes
            string[] prefixes = { "is", "has", "should", "can", "will" };
            foreach (var prefix in prefixes)
            {
                if (fieldName.StartsWith(prefix) && fieldName.Length > prefix.Length &&
                    char.IsUpper(fieldName[prefix.Length]))
                {
                    fieldName = fieldName.Substring(prefix.Length);
                    break;
                }
            }

            // Convert camelCase to Title Case
            var result = System.Text.RegularExpressions.Regex.Replace(
                fieldName,
                @"([a-z])([A-Z])",
                "$1 $2");

            // Capitalize first letter
            if (result.Length > 0)
                result = char.ToUpper(result[0]) + result.Substring(1);

            return result;
        }




        private string ConvertCamelCaseToReadable(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName))
                return fieldName;

            // Special cases
            if (fieldName.Equals("isRoundTrip", StringComparison.OrdinalIgnoreCase))
                return "Round Trip";

            // Remove "is" prefix if present
            if (fieldName.StartsWith("is") && fieldName.Length > 2 && char.IsUpper(fieldName[2]))
                fieldName = fieldName.Substring(2);

            // Insert spaces before capital letters
            var result = System.Text.RegularExpressions.Regex.Replace(
                fieldName,
                @"([a-z])([A-Z])",
                "$1 $2");

            // Capitalize first letter
            if (result.Length > 0)
                result = char.ToUpper(result[0]) + result.Substring(1);

            return result;
        }

        private bool IsActuallyRepeatingPattern(string match, XElement templateElem)
        {
            if (string.IsNullOrEmpty(match))
                return false;

            // Pattern 1: parent/child relationship (e.g., "my:trips/my:trip")
            // This indicates iteration over child elements
            if (match.Contains("/"))
            {
                var parts = match.Split('/');
                if (parts.Length >= 2)
                {
                    // Check if the last part is singular and second-to-last is plural
                    // Or if they follow a collection/item pattern
                    var parent = parts[parts.Length - 2].Split(':').Last();
                    var child = parts[parts.Length - 1].Split(':').Last();

                    // Check for collection patterns (plural parent, singular child)
                    if (IsCollectionPattern(parent, child))
                    {
                        Debug.WriteLine($"Detected collection pattern: {parent}/{child}");
                        return true;
                    }
                }
            }

            // Pattern 2: Check for xsl:for-each in the template
            var ns = templateElem.Name.Namespace;
            var hasForEach = templateElem.Descendants(ns + "for-each").Any();
            if (hasForEach)
            {
                Debug.WriteLine($"Template contains for-each - treating as repeating");
                return true;
            }

            // Pattern 3: Check if this template is called from within a repeating context
            // If we're already in a repeating context and this is a simple match (no /),
            // it's probably NOT another repeating level
            if (!match.Contains("/") && repeatingContextStack.Count > 0)
            {
                Debug.WriteLine($"Simple match '{match}' within existing repeating context - NOT repeating");
                return false;
            }

            return false;
        }

        private bool IsCollectionPattern(string parent, string child)
        {
            // Remove common prefixes/suffixes for comparison
            parent = parent.ToLower();
            child = child.ToLower();

            // Pattern: plural to singular (items/item, trips/trip, etc.)
            if (parent.EndsWith("s") && parent.Substring(0, parent.Length - 1) == child)
                return true;

            // Pattern: collection suffix (tripList/trip, tripCollection/trip)
            if ((parent.EndsWith("list") || parent.EndsWith("collection") || parent.EndsWith("array")) &&
                parent.StartsWith(child))
                return true;

            // Pattern: repeated element (trip/trip) - sometimes used for collections
            if (parent == child)
                return true;

            return false;
        }


        private bool IsStandaloneLabelElement(XElement elem)
        {
            var elemName = elem.Name.LocalName.ToLower();

            if (elemName == "strong" || elemName == "font" || elemName == "div")
            {
                var text = ExtractLabelText(elem);
                if (!string.IsNullOrWhiteSpace(text) && text.Length > 2 && !text.EndsWith(":"))
                    return true;
            }

            return false;
        }

        private bool IsRepeatingSection(XElement elem)
        {
            var className = elem.Attribute("class")?.Value ?? "";

            if (className.Contains("xdRepeatingSection") && className.Contains("xdRepeating"))
                return true;

            var xctname = GetAttributeValue(elem, "xctname");
            if (xctname == "RepeatingSection")
                return true;

            var hasRepeatingTemplate = elem.Elements()
                .Any(e => e.Name.LocalName == "apply-templates" &&
                          e.Attribute("mode") != null &&
                          e.Attribute("select") != null &&
                          e.Attribute("select").Value.Contains("/"));

            return hasRepeatingTemplate;
        }

        private void ProcessRepeatingSection(XElement elem)
        {
            Debug.WriteLine($"\n[REPEATING SECTION] Starting to process repeating section");

            var className = elem.Attribute("class")?.Value ?? "";
            var ctrlId = GetAttributeValue(elem, "CtrlId");
            var binding = GetAttributeValue(elem, "binding");
            var caption = GetAttributeValue(elem, "caption");
            var title = GetAttributeValue(elem, "title");

            // Look for apply-templates to determine the actual repeating structure
            var applyTemplates = elem.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "apply-templates" &&
                                     e.Attribute("select") != null);

            if (applyTemplates != null)
            {
                var selectValue = applyTemplates.Attribute("select")?.Value;
                binding = selectValue;
            }

            // Extract section name dynamically
            string sectionName = null;
            if (!string.IsNullOrWhiteSpace(caption) && !caption.StartsWith("CTRL"))
                sectionName = caption;
            else if (!string.IsNullOrWhiteSpace(title) && !title.StartsWith("CTRL"))
                sectionName = title;
            else if (!string.IsNullOrWhiteSpace(binding))
                sectionName = ExtractSectionNameFromSelect(binding);
            else if (!string.IsNullOrWhiteSpace(ctrlId))
                sectionName = $"Section_{ctrlId}";
            else
            {
                repeatingSectionCounter++;
                sectionName = $"Section_Repeating{repeatingSectionCounter}";
            }

            // Handle nesting dynamically
            if (repeatingContextStack.Count > 0)
            {
                var parentContext = repeatingContextStack.Peek();
                var originalName = sectionName;
                sectionName = $"{parentContext.DisplayName}_{sectionName}";
                Debug.WriteLine($"[REPEATING SECTION] Creating nested section: {sectionName} (parent: {parentContext.DisplayName})");
            }

            // Create the repeating section context
            CreateRepeatingSection(sectionName, binding, "section");

            Debug.WriteLine($"[REPEATING SECTION] Processing children of {sectionName}");
            int controlCountBefore = allControls.Count;

            // Process ONLY direct children, not nested apply-templates
            foreach (var child in elem.Elements())
            {
                // Skip nested apply-templates - they'll be handled separately
                if (child.Name.LocalName == "apply-templates" &&
                    child.Attribute("mode") != null &&
                    child.Attribute("select")?.Value?.Contains("/") == true)
                {
                    // This is a nested repeating section - process it separately
                    var mode = child.Attribute("mode")?.Value;
                    var select = child.Attribute("select")?.Value;

                    if (IsRepeatingSelectPattern(select))
                    {
                        // Process as nested repeating section
                        ProcessRepeatingApplyTemplates(child, mode, select);
                    }
                    else
                    {
                        // Process as regular template
                        var matchingTemplate = FindTemplate(elem.Document, mode);
                        if (matchingTemplate != null)
                        {
                            ProcessXslTemplate(matchingTemplate);
                        }
                    }
                }
                else
                {
                    ProcessElement(child);
                }
            }

            int controlsAdded = allControls.Count - controlCountBefore;
            Debug.WriteLine($"[REPEATING SECTION] Added {controlsAdded} controls to {sectionName}");

            // Pop the context
            var context = repeatingContextStack.Pop();
            var sectionInfo = sections.LastOrDefault(s => s.Name == context.DisplayName);
            if (sectionInfo != null)
                sectionInfo.EndRow = currentRow;

            Debug.WriteLine($"[REPEATING SECTION] Finished processing {sectionName}");
        }

        private bool IsRegularSection(XElement elem)
        {
            var className = elem.Attribute("class")?.Value ?? "";

            if (className.Contains("xdSection") && !className.Contains("xdRepeating"))
                return true;

            var xctname = GetAttributeValue(elem, "xctname");
            if (xctname == "Section" || xctname == "OptionalSection")
                return true;

            return false;
        }

        private void ProcessRegularSection(XElement elem)
        {
            var className = elem.Attribute("class")?.Value ?? "";
            var ctrlId = GetAttributeValue(elem, "CtrlId");
            var caption = GetAttributeValue(elem, "caption_0") ?? GetAttributeValue(elem, "caption");

            Debug.WriteLine($"Processing regular section - class: {className}, ctrlId: {ctrlId}");

            // If we're in a repeating context, check if this section adds value
            if (repeatingContextStack.Count > 0)
            {
                // This is a regular/conditional section within a repeating section
                // Check if it has meaningful structure or is just a layout container

                var hasConditionalLogic = elem.Descendants()
                    .Any(e => e.Name.LocalName == "if" ||
                             e.Name.LocalName == "when" ||
                             e.Name.LocalName == "choose");

                if (!hasConditionalLogic)
                {
                    // It's probably just a layout container - skip creating a section
                    Debug.WriteLine($"Section {ctrlId} within repeating context appears to be layout-only - flattening");

                    foreach (var child in elem.Elements())
                        ProcessElement(child);

                    return;
                }

                // It has conditional logic, so it might be meaningful
                // But still don't create a separate section context
                // Just mark controls as conditional
                Debug.WriteLine($"Section {ctrlId} has conditional logic but is within repeating context");

                foreach (var child in elem.Elements())
                    ProcessElement(child);

                return;
            }

            // Normal section processing for non-nested sections
            var sectionType = className.Contains("xdOptional") ? "optional" : "section";

            var sectionName = !string.IsNullOrEmpty(caption) ? caption :
                              !string.IsNullOrEmpty(ctrlId) ? ctrlId : "Section";

            var section = new SectionContext
            {
                Name = sectionName,
                Type = sectionType,
                StartRow = currentRow,
                CtrlId = ctrlId,
                DisplayName = sectionName
            };

            sectionStack.Push(section);

            var sectionInfo = new SectionInfo
            {
                Name = sectionName,
                Type = sectionType,
                CtrlId = ctrlId,
                StartRow = currentRow
            };
            sections.Add(sectionInfo);

            foreach (var child in elem.Elements())
                ProcessElement(child);

            ExitRegularSection();
        }

        private void ExitRegularSection()
        {
            if (sectionStack.Count > 0)
            {
                var section = sectionStack.Pop();
                var sectionInfo = sections.LastOrDefault(s => s.Name == section.Name);
                if (sectionInfo != null)
                    sectionInfo.EndRow = currentRow;
            }
        }

        private void TrackPotentialSectionLabel(XElement elem)
        {
            if (recentLabels == null)
                recentLabels = new Queue<LabelInfo>();

            var text = ExtractLabelText(elem);
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (text.StartsWith("Add ", StringComparison.OrdinalIgnoreCase) ||
                    text.StartsWith("Insert ", StringComparison.OrdinalIgnoreCase))
                    return;

                var labelInfo = new LabelInfo
                {
                    Text = text.Trim(),
                    DocIndex = docIndexCounter + 1,
                    Row = currentRow,
                    Col = currentCol,
                    Used = false
                };

                recentLabels.Enqueue(labelInfo);

                while (recentLabels.Count > LABEL_LOOKBACK_COUNT)
                    recentLabels.Dequeue();
            }
        }

        private void ProcessRepeatingTable(XElement elem)
        {
            var ctrlId = GetAttributeValue(elem, "CtrlId");

            Debug.WriteLine($"\n[REPEATING TABLE] Starting to process repeating table");
            Debug.WriteLine($"  CtrlId: {ctrlId}");

            // Skip if already processed
            if (!string.IsNullOrEmpty(ctrlId) && processedControls.Contains(ctrlId))
            {
                Debug.WriteLine($"  Table {ctrlId} already processed, skipping");
                return;
            }

            // Extract table name
            string tableName = ExtractTableName(elem, ctrlId);
            if (string.IsNullOrWhiteSpace(tableName))
            {
                repeatingSectionCounter++;
                tableName = $"Table_Repeating{repeatingSectionCounter}";
            }

            Debug.WriteLine($"  Table name: {tableName}");

            var binding = GetTableBinding(elem);
            Debug.WriteLine($"  Binding: {binding}");

            // Create the table control itself
            var tableControl = new ControlDefinition
            {
                Name = RemoveSpaces(tableName),
                Type = "RepeatingTable",
                Label = tableName,
                Binding = binding,
                DocIndex = ++docIndexCounter,
                GridPosition = currentRow + GetColumnLetter(currentCol)
            };

            // Check if nested
            if (repeatingContextStack.Count > 0)
            {
                var parentContext = repeatingContextStack.Peek();
                tableControl.IsInRepeatingSection = true;
                tableControl.RepeatingSectionName = parentContext.DisplayName;
                tableControl.RepeatingSectionBinding = parentContext.Binding;
            }

            tableControl.Properties["TableType"] = "Repeating";
            tableControl.Properties["DisplayName"] = tableName;

            if (!string.IsNullOrEmpty(ctrlId))
            {
                tableControl.Properties["CtrlId"] = ctrlId;
                controlsById[ctrlId] = tableControl;
                processedControls.Add(ctrlId);
            }

            allControls.Add(tableControl);
            currentRow++;
            currentCol = 1;

            // Process table header (creates label controls)
            ProcessTableHeader(elem);

            // Create repeating context
            CreateRepeatingSection(tableName, binding, "table");

            // Process the repeating rows
            ProcessTableStructure(elem);

            // Pop the repeating context
            if (repeatingContextStack.Count > 0)
            {
                var context = repeatingContextStack.Pop();
                var sectionInfo = sections.LastOrDefault(s => s.Name == context.DisplayName);
                if (sectionInfo != null)
                {
                    sectionInfo.EndRow = currentRow;
                }
            }

            // Process table footer (for totals, etc.)
            ProcessTableFooter(elem);

            Debug.WriteLine($"[REPEATING TABLE] Finished processing table {tableName}");
        }

        private void ProcessTableHeader(XElement tableElem)
        {
            Debug.WriteLine($"[TABLE HEADER] Processing table header for labels and controls");

            // Look for thead or tbody with xdTableHeader class
            var headerSection = tableElem.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "thead" ||
                                    (e.Name.LocalName == "tbody" &&
                                     e.Attribute("class")?.Value?.Contains("xdTableHeader") == true));

            if (headerSection == null)
            {
                Debug.WriteLine($"  No header section found");
                return;
            }

            // Store the starting row for the table
            int tableStartRow = currentRow;

            // Process header rows
            foreach (var row in headerSection.Elements().Where(e => e.Name.LocalName == "tr"))
            {
                currentCol = 1;
                var cells = row.Elements().Where(e => e.Name.LocalName == "td" || e.Name.LocalName == "th").ToList();

                // Extract header labels and store them
                var headerLabels = new List<string>();

                foreach (var cell in cells)
                {
                    var labelText = ExtractCellText(cell).Trim();
                    if (!string.IsNullOrWhiteSpace(labelText))
                    {
                        headerLabels.Add(labelText);

                        // Create a label control for this header
                        var headerLabel = new ControlDefinition
                        {
                            Name = GenerateControlName(cell, labelText, "", "Label"),
                            Type = "Label",
                            Label = labelText,
                            Binding = "",
                            DocIndex = ++docIndexCounter,
                            GridPosition = currentRow + GetColumnLetter(currentCol)
                        };

                        // Extract style attributes from the font element if present
                        var fontElem = cell.Descendants()
                            .FirstOrDefault(e => e.Name.LocalName == "font");

                        if (fontElem != null)
                        {
                            var color = fontElem.Attribute("color")?.Value;
                            var size = fontElem.Attribute("size")?.Value;
                            var face = fontElem.Attribute("face")?.Value;

                            if (!string.IsNullOrEmpty(color))
                                headerLabel.Properties["color"] = color;
                            if (!string.IsNullOrEmpty(size))
                                headerLabel.Properties["size"] = size;
                            if (!string.IsNullOrEmpty(face))
                                headerLabel.Properties["face"] = face;
                        }

                        headerLabel.Properties["IsTableHeader"] = "true";
                        headerLabel.Properties["TableColumn"] = currentCol.ToString();

                        ApplyControlContext(headerLabel);
                        allControls.Add(headerLabel);

                        Debug.WriteLine($"    Created header label: {headerLabel.Label} at {headerLabel.GridPosition}");
                    }
                    else
                    {
                        headerLabels.Add(""); // Empty placeholder for alignment
                    }

                    currentCol++;
                }

                // Store headers for use when processing data rows
                currentTableHeaders = headerLabels;
                currentRow++;
            }
        }


        private void ProcessTableFooter(XElement tableElem)
        {
            Debug.WriteLine($"[TABLE FOOTER] Processing table footer for non-repeating controls");

            // Look for tbody with xdTableFooter class
            var footerSection = tableElem.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "tbody" &&
                                    e.Attribute("class")?.Value?.Contains("xdTableFooter") == true);

            if (footerSection == null)
            {
                Debug.WriteLine($"  No footer section found");
                return;
            }

            // Process each row in the footer
            foreach (var row in footerSection.Elements().Where(e => e.Name.LocalName == "tr"))
            {
                currentCol = 1;
                Debug.WriteLine($"  Processing footer row at row {currentRow}");

                foreach (var cell in row.Elements().Where(e => e.Name.LocalName == "td"))
                {
                    // Extract label from the cell (often in the merged cells)
                    var labelElems = cell.Descendants()
                        .Where(e => e.Name.LocalName == "font" || e.Name.LocalName == "strong");

                    foreach (var labelElem in labelElems)
                    {
                        var labelText = ExtractLabelText(labelElem);
                        if (!string.IsNullOrWhiteSpace(labelText) && labelText.Length > 1)
                        {
                            var labelControl = new ControlDefinition
                            {
                                Name = GenerateControlName(labelElem, labelText, "", "Label"),
                                Type = "Label",
                                Label = labelText,
                                Binding = "",
                                DocIndex = ++docIndexCounter,
                                GridPosition = currentRow + GetColumnLetter(currentCol)
                            };

                            // Copy font attributes if present
                            var color = labelElem.Attribute("color")?.Value;
                            var face = labelElem.Attribute("face")?.Value;
                            var size = labelElem.Attribute("size")?.Value;

                            if (!string.IsNullOrEmpty(color))
                                labelControl.Properties["color"] = color;
                            if (!string.IsNullOrEmpty(face))
                                labelControl.Properties["face"] = face;
                            if (!string.IsNullOrEmpty(size))
                                labelControl.Properties["size"] = size;

                            allControls.Add(labelControl);
                            Debug.WriteLine($"    Added footer label: {labelControl.Name} - {labelControl.Label}");
                        }
                    }

                    // Look for input controls (like the total fields)
                    var controls = cell.Descendants()
                        .Where(e => e.Name.LocalName == "span" &&
                                   (!string.IsNullOrEmpty(GetAttributeValue(e, "binding")) ||
                                    !string.IsNullOrEmpty(GetAttributeValue(e, "xctname"))));

                    foreach (var ctrlElem in controls)
                    {
                        var control = TryExtractControl(ctrlElem);
                        if (control != null)
                        {
                            ApplyControlContext(control);
                            control.GridPosition = currentRow + GetColumnLetter(currentCol);
                            control.DocIndex = ++docIndexCounter;

                            // Footer controls are NOT in the repeating section
                            control.IsInRepeatingSection = false;
                            control.RepeatingSectionName = null;
                            control.RepeatingSectionBinding = null;

                            allControls.Add(control);
                            Debug.WriteLine($"    Added footer control: {control.Name} ({control.Type}) - {control.Label}");
                        }
                    }

                    currentCol++;
                }
                currentRow++;
            }
        }


        private void ProcessTableStructure(XElement tableElem)
        {
            Debug.WriteLine($"[TABLE STRUCTURE] Processing table for controls");

            // First, look for and process table headers to get column labels
            var headerLabels = ExtractTableHeaderLabels(tableElem);
            Debug.WriteLine($"  Found {headerLabels.Count} header labels: {string.Join(", ", headerLabels)}");

            // Store headers for use during cell processing
            currentTableHeaders = headerLabels;

            // Look for tbody with xd:xctname="repeatingtable"
            var tbody = tableElem.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "tbody" &&
                                    (GetAttributeValue(e, "xctname") == "repeatingtable" ||
                                     e.Attribute("class")?.Value?.Contains("repeatingtable") == true));

            if (tbody == null)
            {
                // If not found, look for any tbody that's not a header/footer
                tbody = tableElem.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "tbody" &&
                                        e.Attribute("class")?.Value?.Contains("TableHeader") != true &&
                                        e.Attribute("class")?.Value?.Contains("TableFooter") != true);
            }

            if (tbody != null)
            {
                Debug.WriteLine($"  Found table body, looking for xsl:for-each");

                // Look for xsl:for-each which indicates the repeating pattern
                var forEach = tbody.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "for-each");

                if (forEach != null)
                {
                    var select = forEach.Attribute("select")?.Value;
                    Debug.WriteLine($"  Found for-each with select: {select}");

                    // Process the content inside for-each
                    ProcessTableForEach(forEach);
                }
                else
                {
                    Debug.WriteLine($"  No for-each found, processing tbody children directly");
                    // Process tbody children directly
                    foreach (var child in tbody.Elements())
                    {
                        if (child.Name.LocalName == "tr")
                        {
                            ProcessTableRow(child);
                        }
                        else
                        {
                            ProcessElement(child);
                        }
                    }
                }
            }
            else
            {
                Debug.WriteLine($"  No tbody found, processing table children");
                // Process all children if no tbody found
                foreach (var child in tableElem.Elements())
                {
                    ProcessElement(child);
                }
            }

            // Clear the headers after processing the table
            currentTableHeaders = null;
        }


        private void ProcessTableForEach(XElement forEachElem)
        {
            Debug.WriteLine($"[TABLE FOR-EACH] Processing for-each content");
            var select = forEachElem.Attribute("select")?.Value;

            // Process the template row(s) inside for-each
            foreach (var tr in forEachElem.Elements().Where(e => e.Name.LocalName == "tr"))
            {
                Debug.WriteLine($"  Processing table row");
                ProcessTableRow(tr);
            }
        }

        private void ProcessTableRow(XElement rowElem)
        {
            Debug.WriteLine($"[TABLE ROW] Processing row for controls");

            // Store current column position for proper grid alignment
            currentCol = 1;
            currentTableColumn = 0;

            var cells = rowElem.Elements().Where(e => e.Name.LocalName == "td").ToList();

            foreach (var cell in cells)
            {
                Debug.WriteLine($"  Processing table cell at column {currentCol}");

                // Get the header label for this column if available
                string headerLabel = "";
                if (currentTableHeaders != null && currentTableColumn < currentTableHeaders.Count)
                {
                    headerLabel = currentTableHeaders[currentTableColumn];
                }

                // Process controls in this cell
                var controlsInCell = ExtractControlsFromCell(cell, headerLabel);

                if (controlsInCell.Count > 0)
                {
                    // For multiple controls in same cell, use sub-columns (1A, 1B, etc.)
                    int subCol = 0;
                    foreach (var control in controlsInCell)
                    {
                        control.GridPosition = currentRow + GetColumnLetter(currentCol + subCol);
                        control.DocIndex = ++docIndexCounter;

                        // Apply header label if control doesn't have one
                        if (string.IsNullOrEmpty(control.Label) && !string.IsNullOrEmpty(headerLabel))
                        {
                            control.Label = headerLabel;
                        }

                        ApplyControlContext(control);

                        if (control.Properties != null && control.Properties.ContainsKey("CtrlId"))
                        {
                            var ctrlId = control.Properties["CtrlId"];
                            if (!string.IsNullOrEmpty(ctrlId))
                            {
                                controlsById[ctrlId] = control;
                            }
                        }

                        allControls.Add(control);
                        Debug.WriteLine($"    Added control: {control.Name} ({control.Type}) at {control.GridPosition}");

                        subCol++;
                    }
                }

                currentCol++;
                currentTableColumn++;
            }

            currentRow++;
        }

        private List<ControlDefinition> ExtractControlsFromCell(XElement cellElem, string headerLabel)
        {
            var controls = new List<ControlDefinition>();

            // Look for all controls in the cell
            var controlElements = cellElem.Descendants()
                .Where(e =>
                    (e.Name.LocalName == "span" && !string.IsNullOrEmpty(GetAttributeValue(e, "binding"))) ||
                    e.Name.LocalName == "select" ||
                    e.Name.LocalName == "input" ||
                    e.Name.LocalName == "textarea" ||
                    (e.Name.LocalName == "div" && GetAttributeValue(e, "xctname") == "DTPicker") ||
                    !string.IsNullOrEmpty(GetAttributeValue(e, "xctname")))
                .Where(e => !IsDatePickerSubComponent(e)) // Skip DatePicker sub-components
                .ToList();

            foreach (var elem in controlElements)
            {
                var control = TryExtractControl(elem);
                if (control != null)
                {
                    controls.Add(control);
                }
            }

            return controls;
        }

        private bool IsDatePickerSubComponent(XElement elem)
        {
            var className = elem.Attribute("class")?.Value ?? "";
            return className.Contains("xdDTText") ||
                   className.Contains("xdDTButton") ||
                   !string.IsNullOrEmpty(GetAttributeValue(elem, "innerCtrl"));
        }



        private int ExtractControlsFromTableCell(XElement cellElem)
        {
            int controlsFound = 0;

            // Get the header label for this column if available
            string headerLabel = "";
            if (currentTableHeaders != null && currentTableColumn < currentTableHeaders.Count)
            {
                headerLabel = currentTableHeaders[currentTableColumn];
                Debug.WriteLine($"    Using header label: {headerLabel}");
            }

            // Look for span elements with binding (most common in InfoPath tables)
            var spans = cellElem.Descendants()
                .Where(e => e.Name.LocalName == "span" &&
                           !string.IsNullOrEmpty(GetAttributeValue(e, "binding")));

            foreach (var span in spans)
            {
                Debug.WriteLine($"    Found span with binding in table cell");
                var control = TryExtractControl(span);
                if (control != null)
                {
                    // For table controls, ALWAYS prefer the header label over title attribute
                    if (!string.IsNullOrEmpty(headerLabel))
                    {
                        control.Label = headerLabel;
                        Debug.WriteLine($"    Set label from header: {headerLabel}");
                    }

                    ApplyControlContext(control);
                    control.GridPosition = currentRow + GetColumnLetter(currentCol);
                    control.DocIndex = ++docIndexCounter;

                    if (control.Properties != null && control.Properties.ContainsKey("CtrlId"))
                    {
                        var ctrlId = control.Properties["CtrlId"];
                        if (!string.IsNullOrEmpty(ctrlId))
                        {
                            controlsById[ctrlId] = control;
                        }
                    }

                    allControls.Add(control);
                    controlsFound++;

                    Debug.WriteLine($"    Added table control: {control.Name} ({control.Type}) with binding {control.Binding}");
                    Debug.WriteLine($"      Final Label: {control.Label}");
                    if (control.IsInRepeatingSection)
                    {
                        Debug.WriteLine($"      In repeating: {control.RepeatingSectionName}");
                    }
                }
            }

            // Also check for other control types (select, input, etc.)
            var otherControls = cellElem.Descendants()
                .Where(e => (e.Name.LocalName == "select" ||
                            e.Name.LocalName == "input" ||
                            e.Name.LocalName == "textarea" ||
                            !string.IsNullOrEmpty(GetAttributeValue(e, "xctname"))) &&
                           !e.Ancestors().Any(a => a.Name.LocalName == "span" &&
                                                  !string.IsNullOrEmpty(GetAttributeValue(a, "binding"))));

            foreach (var elem in otherControls)
            {
                Debug.WriteLine($"    Found {elem.Name.LocalName} control in table cell");
                var control = TryExtractControl(elem);
                if (control != null)
                {
                    // For table controls, ALWAYS prefer the header label over title attribute
                    if (!string.IsNullOrEmpty(headerLabel))
                    {
                        control.Label = headerLabel;
                        Debug.WriteLine($"    Set label from header: {headerLabel}");
                    }

                    ApplyControlContext(control);
                    control.GridPosition = currentRow + GetColumnLetter(currentCol);
                    control.DocIndex = ++docIndexCounter;

                    if (control.Properties != null && control.Properties.ContainsKey("CtrlId"))
                    {
                        var ctrlId = control.Properties["CtrlId"];
                        if (!string.IsNullOrEmpty(ctrlId))
                        {
                            controlsById[ctrlId] = control;
                        }
                    }

                    allControls.Add(control);
                    controlsFound++;

                    Debug.WriteLine($"    Added table control: {control.Name} ({control.Type})");
                    Debug.WriteLine($"      Final Label: {control.Label}");
                }
            }

            // Increment column if we found controls
            if (controlsFound > 0)
            {
                currentCol++;
            }

            return controlsFound;
        }

        private List<string> ExtractTableHeaderLabels(XElement tableElem)
        {
            var labels = new List<string>();

            // Look for the header tbody (with class xdTableHeader)
            var headerBody = tableElem.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "tbody" &&
                                    e.Attribute("class")?.Value?.Contains("xdTableHeader") == true);

            if (headerBody != null)
            {
                // Get the first row in the header
                var headerRow = headerBody.Elements()
                    .FirstOrDefault(e => e.Name.LocalName == "tr");

                if (headerRow != null)
                {
                    // Get all td/th elements
                    var headerCells = headerRow.Elements()
                        .Where(e => e.Name.LocalName == "td" || e.Name.LocalName == "th");

                    foreach (var cell in headerCells)
                    {
                        // Extract the text from the cell
                        var labelText = ExtractCellText(cell).Trim();
                        labels.Add(labelText);

                        Debug.WriteLine($"    Header label: {labelText}");
                    }
                }
            }
            else
            {
                // Alternative: Look for thead element
                var thead = tableElem.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "thead");

                if (thead != null)
                {
                    var headerRow = thead.Elements()
                        .FirstOrDefault(e => e.Name.LocalName == "tr");

                    if (headerRow != null)
                    {
                        var headerCells = headerRow.Elements()
                            .Where(e => e.Name.LocalName == "td" || e.Name.LocalName == "th");

                        foreach (var cell in headerCells)
                        {
                            var labelText = ExtractCellText(cell).Trim();
                            labels.Add(labelText);
                        }
                    }
                }
            }

            return labels;
        }


        private string ExtractCellText(XElement cell)
        {
            var sb = new StringBuilder();

            foreach (var node in cell.Nodes())
            {
                if (node is XText textNode)
                {
                    sb.Append(textNode.Value);
                }
                else if (node is XElement elem)
                {
                    // Skip font tags but get their content
                    if (elem.Name.LocalName == "font" || elem.Name.LocalName == "span")
                    {
                        sb.Append(ExtractCellText(elem));
                    }
                    else
                    {
                        sb.Append(elem.Value);
                    }
                }
            }

            return sb.ToString().Trim();
        }


        private string ExtractTableName(XElement elem, string ctrlId)
        {
            // 1. Check for caption attribute
            var caption = GetAttributeValue(elem, "caption");
            if (!string.IsNullOrWhiteSpace(caption) && !caption.StartsWith("CTRL"))
            {
                return CleanTableName(caption);
            }

            // 2. Check for title attribute  
            var title = GetAttributeValue(elem, "title");
            if (!string.IsNullOrWhiteSpace(title) && !title.StartsWith("CTRL"))
            {
                return CleanTableName(title);
            }

            // 3. Try to extract from table headers
            var headerName = ExtractNameFromTableHeaders(elem);
            if (!string.IsNullOrWhiteSpace(headerName))
            {
                return CleanTableName(headerName);
            }

            // 4. Try binding path
            var binding = GetTableBinding(elem);
            if (!string.IsNullOrWhiteSpace(binding))
            {
                var name = ExtractNameFromBinding(binding);
                if (!string.IsNullOrWhiteSpace(name) &&
                    name != "RepeatingSection" &&
                    name != "RepeatingTable")
                {
                    return CleanTableName(name);
                }
            }

            // 5. Use CtrlId if available
            if (!string.IsNullOrWhiteSpace(ctrlId))
            {
                return $"Table_{ctrlId}";
            }

            // 6. Will be handled by caller to generate a default name
            return null;
        }



        private string FindBestLabelForSection(XElement elem, string defaultName)
        {
            var unusedLabels = recentLabels.Where(l => !l.Used).ToList();

            unusedLabels = unusedLabels.Where(l =>
                !l.Text.Equals("Notes", StringComparison.OrdinalIgnoreCase) &&
                !l.Text.Equals("Comments", StringComparison.OrdinalIgnoreCase) &&
                !l.Text.Equals("Description", StringComparison.OrdinalIgnoreCase) &&
                !l.Text.StartsWith("Add ", StringComparison.OrdinalIgnoreCase) &&
                !l.Text.StartsWith("Insert ", StringComparison.OrdinalIgnoreCase)
            ).ToList();

            var sectionLabels = unusedLabels
                .Where(l => IsSectionHeaderText(l.Text))
                .OrderByDescending(l => l.DocIndex)
                .ToList();

            foreach (var label in sectionLabels)
            {
                if (Math.Abs(label.Row - currentRow) <= 2)
                {
                    label.Used = true;
                    return label.Text;
                }
            }

            var binding = GetAttributeValue(elem, "binding");
            if (string.IsNullOrEmpty(binding))
            {
                var applyTemplate = elem.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "apply-templates" &&
                                         e.Attribute("select") != null);
                if (applyTemplate != null)
                    binding = applyTemplate.Attribute("select")?.Value;
            }

            if (!string.IsNullOrEmpty(binding))
                return ExtractNameFromBinding(binding);

            return defaultName;
        }

        private bool IsSectionHeaderText(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length < 3) return false;
            if (text.EndsWith(":")) return false;

            if (text.EndsWith("s", StringComparison.OrdinalIgnoreCase) ||
                text.EndsWith("es", StringComparison.OrdinalIgnoreCase) ||
                text.EndsWith("ies", StringComparison.OrdinalIgnoreCase))
                return true;

            var sectionKeywords = new[] { "List", "Items", "Details", "Information", "Data" };
            if (sectionKeywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)))
                return true;

            return true;
        }

        private RepeatingContext ExtractRepeatingTableInfo(XElement elem)
        {
            var caption = GetAttributeValue(elem, "caption");
            var title = GetAttributeValue(elem, "title");
            var binding = "";

            var tbody = elem.Descendants().FirstOrDefault(e => e.Name.LocalName == "tbody");
            if (tbody != null)
            {
                binding = GetAttributeValue(tbody, "repeating") ??
                         GetAttributeValue(tbody, "xd:repeating") ??
                         GetAttributeValue(tbody, "binding");
            }

            if (string.IsNullOrEmpty(binding))
            {
                binding = GetAttributeValue(elem, "repeating") ??
                          GetAttributeValue(elem, "binding");
            }

            var name = caption ?? title ?? "RepeatingTable";

            return new RepeatingContext
            {
                Name = name,
                Binding = binding,
                Type = "table"
            };
        }

        private string ExtractNameFromBinding(string binding)
        {
            if (string.IsNullOrEmpty(binding))
                return "RepeatingSection";

            var parts = binding.Split('/');
            string relevantPart = "";

            if (parts.Length >= 2)
            {
                relevantPart = parts[parts.Length - 2];
            }
            else if (parts.Length == 1)
            {
                relevantPart = parts[0];
            }

            if (relevantPart.Contains(':'))
                relevantPart = relevantPart.Split(':')[1];

            if (!string.IsNullOrEmpty(relevantPart))
            {
                relevantPart = char.ToUpper(relevantPart[0]) + relevantPart.Substring(1);
                relevantPart = Regex.Replace(relevantPart, "([a-z])([A-Z])", "$1 $2");
            }

            return string.IsNullOrEmpty(relevantPart) ? "RepeatingSection" : relevantPart;
        }


        private void HandleNewRow(XElement elem)
        {
            if (!inTableRow || elem.Name.LocalName.Equals("tr", StringComparison.OrdinalIgnoreCase))
            {
                if (currentCol > 1)
                {
                    currentRow++;
                    currentCol = 1;
                }

                if (sectionStack.Count > 0)
                {
                    var section = sectionStack.Peek();
                    if (!sectionRowCounters.ContainsKey(section.Name))
                        sectionRowCounters[section.Name] = 0;
                    sectionRowCounters[section.Name]++;
                }
            }

            if (elem.Name.LocalName.Equals("tr", StringComparison.OrdinalIgnoreCase))
                inTableRow = true;
        }

        private bool IsRepeatingTable(XElement elem)
        {
            if (!elem.Name.LocalName.Equals("table", StringComparison.OrdinalIgnoreCase))
                return false;

            var className = elem.Attribute("class")?.Value ?? "";
            var xctname = GetAttributeValue(elem, "xctname");

            if (className.Contains("xdRepeatingTable") ||
                xctname.Equals("repeatingtable", StringComparison.OrdinalIgnoreCase))
                return true;

            var hasRepeatingTbody = elem.Elements()
                .Any(e => e.Name.LocalName == "tbody" &&
                         (!string.IsNullOrEmpty(GetAttributeValue(e, "repeating")) ||
                          (!string.IsNullOrEmpty(GetAttributeValue(e, "xctname")) &&
                           GetAttributeValue(e, "xctname").Equals("repeatingtable", StringComparison.OrdinalIgnoreCase))));

            return hasRepeatingTbody;
        }

        private bool IsNewRowIndicator(XElement elem)
        {
            var elemName = elem.Name.LocalName.ToLower();
            var className = elem.Attribute("class")?.Value ?? "";

            if (elemName == "tr") return true;

            if (elemName == "div" && (className.Contains("xdSection") || className.Contains("xdRepeatingSection")))
                return true;

            if (elemName == "td" || elemName == "th")
            {
                var colspan = elem.Attribute("colspan")?.Value;
                if (colspan != null && int.TryParse(colspan, out var cs) && cs > 2)
                    return true;
            }

            if (elemName == "hr") return true;
            if (elemName == "img" && elem.Attribute("src")?.Value.Contains("line") == true) return true;

            var style = elem.Attribute("style")?.Value ?? "";
            if (style.Contains("border-top") && style.Contains("solid"))
            {
                if (style.Contains("2.25pt") || style.Contains("2pt") || style.Contains("3pt"))
                    return true;
            }

            if (className.Contains("xdTableHeader") ||
                className.Contains("xdHeadingRow") ||
                className.Contains("xdTitleRow"))
                return true;

            return false;
        }

        private bool IsPlaceholderText(XElement elem)
        {
            var className = elem.Attribute("class")?.Value ?? "";
            if (className.Contains("optionalPlaceholder"))
                return true;

            var action = GetAttributeValue(elem, "action");
            if (action == "xCollection::insert")
                return true;

            return false;
        }



        // NEW METHOD: Extract span controls (common in InfoPath)
        private ControlDefinition TryExtractSpanControl(XElement elem)
        {
            var bindingAttr = GetAttributeValue(elem, "binding") ??
                              GetAttributeValue(elem, "xd:binding");
            var xctname = GetAttributeValue(elem, "xctname") ??
                          GetAttributeValue(elem, "xd:xctname");
            var className = elem.Attribute("class")?.Value ?? "";

            // Check if this span represents a control
            if (!string.IsNullOrEmpty(bindingAttr) ||
                !string.IsNullOrEmpty(xctname) ||
                IsControlClass(className))
            {
                var control = new ControlDefinition
                {
                    DocIndex = ++docIndexCounter
                };

                // Determine control type
                if (!string.IsNullOrEmpty(xctname))
                {
                    control.Type = MapControlType(xctname);
                }
                else if (className.Contains("xdTextBox"))
                {
                    control.Type = "TextField";
                }
                else if (className.Contains("xdComboBox"))
                {
                    control.Type = "DropDown";
                }
                else if (className.Contains("xdDTPicker") || className.Contains("xdDatePicker"))
                {
                    control.Type = "DatePicker";
                }
                else
                {
                    control.Type = "TextField"; // Default for bound spans
                }

                control.Binding = bindingAttr;
                control.Label = elem.Attribute("title")?.Value ?? "";

                // Generate name - this is crucial
                control.Name = GenerateControlName(elem, control.Label, control.Binding, control.Type);

                // If name is "FIRM" or similar, make sure we capture it
                if (string.IsNullOrEmpty(control.Name) && !string.IsNullOrEmpty(bindingAttr))
                {
                    // Extract from binding path
                    var parts = bindingAttr.Split('/');
                    var lastPart = parts.Last().Split(':').Last();
                    control.Name = lastPart.ToUpper();
                }

                // Store all attributes
                foreach (var attr in elem.Attributes())
                {
                    if (!ShouldSkipAttribute(attr.Name.LocalName))
                    {
                        control.Properties[attr.Name.LocalName] = attr.Value;
                    }
                }

                return control;
            }

            return null;
        }
        private ControlDefinition TryExtractControl(XElement elem)
        {
            var elemName = elem.Name.LocalName.ToLower();
            var ctrlId = GetAttributeValue(elem, "CtrlId");
            var xctAttr = GetAttributeValue(elem, "xctname");
            var bindingAttr = GetAttributeValue(elem, "binding") ?? GetAttributeValue(elem, "xd:binding");
            var className = elem.Attribute("class")?.Value ?? "";

            // Skip DatePicker sub-components - only process the main container
            if (className.Contains("xdDTText") || className.Contains("xdDTButton"))
            {
                Debug.WriteLine($"[TRY EXTRACT] Skipping DatePicker sub-component: {className}");
                return null;
            }

            // Also skip if this is a nested element with innerCtrl attribute (part of DatePicker)
            var innerCtrl = GetAttributeValue(elem, "innerCtrl") ?? GetAttributeValue(elem, "xd:innerCtrl");
            if (!string.IsNullOrEmpty(innerCtrl))
            {
                Debug.WriteLine($"[TRY EXTRACT] Skipping DatePicker inner control: {innerCtrl}");
                return null;
            }

            // Also skip button elements that are part of DatePickers
            if (elemName == "button" && elem.Parent != null)
            {
                var parentClass = elem.Parent.Attribute("class")?.Value ?? "";
                if (parentClass.Contains("xdDTPicker"))
                {
                    Debug.WriteLine($"[TRY EXTRACT] Skipping DatePicker button");
                    return null;
                }
            }

            // Debug logging at start
            if (!string.IsNullOrEmpty(bindingAttr) || !string.IsNullOrEmpty(xctAttr) || !string.IsNullOrEmpty(ctrlId))
            {
                Debug.WriteLine($"\n[TRY EXTRACT] Examining element:");
                Debug.WriteLine($"  Element: {elemName}");
                Debug.WriteLine($"  CtrlId: {ctrlId}");
                Debug.WriteLine($"  xctname: {xctAttr}");
                Debug.WriteLine($"  binding: {bindingAttr}");
                Debug.WriteLine($"  class: {className}");

                if (repeatingContextStack.Count > 0)
                {
                    Debug.WriteLine($"  Repeating Context: {string.Join(" > ", repeatingContextStack.Select(r => r.Name))}");
                }

                if (insideExpressionBox)
                {
                    Debug.WriteLine($"  Inside ExpressionBox (depth: {expressionBoxDepth})");
                }
            }

            // Skip ExpressionBox itself (we handle it specially)
            if (IsExpressionBox(elem))
            {
                Debug.WriteLine($"[TRY EXTRACT] Skipping ExpressionBox element itself");
                debugInfo.SkippedElements.Add($"ExpressionBox {ctrlId}");
                return null;
            }

            // Skip if this is a table (should be handled by ProcessRepeatingTable)
            if (elemName == "table")
            {
                Debug.WriteLine($"[TRY EXTRACT] Skipping table element");
                return null;
            }

            // Skip font elements that contain controls - handled by ProcessFontElement
            if (elemName == "font")
            {
                var hasControls = elem.Descendants().Any(d =>
                    !string.IsNullOrEmpty(GetAttributeValue(d, "xctname")) ||
                    !string.IsNullOrEmpty(GetAttributeValue(d, "binding")));

                if (hasControls)
                {
                    Debug.WriteLine($"[TRY EXTRACT] Skipping font element with child controls");
                    return null;
                }
            }

            // CONTEXT-AWARE DUPLICATE CHECK
            string controlKey = "";
            if (!string.IsNullOrEmpty(ctrlId))
            {
                var keyParts = new List<string> { ctrlId };

                // Add repeating context to the key
                if (repeatingContextStack.Count > 0)
                {
                    var contextPath = string.Join(">", repeatingContextStack.Select(r => r.Name));
                    keyParts.Add($"R:{contextPath}");
                }

                // Add template mode to the key
                if (!string.IsNullOrEmpty(currentTemplateMode))
                {
                    keyParts.Add($"T:{currentTemplateMode}");
                }

                // Add section context if present
                if (sectionStack.Count > 0)
                {
                    var sectionPath = string.Join(">", sectionStack.Select(s => s.Name));
                    keyParts.Add($"S:{sectionPath}");
                }

                controlKey = string.Join("::", keyParts);

                // Check if this exact control in this exact context has been processed
                if (processedControls.Contains(controlKey))
                {
                    Debug.WriteLine($"[TRY EXTRACT] Control already processed with key: {controlKey}, skipping");
                    debugInfo.DuplicatesSkipped.Add(controlKey);
                    return null;
                }
            }

            ControlDefinition control = null;

            // Handle DatePicker container (div with class xdDTPicker)
            if (className.Contains("xdDTPicker") || xctAttr == "DTPicker")
            {
                control = new ControlDefinition
                {
                    Type = "DatePicker",
                    DocIndex = ++docIndexCounter
                };

                // Try to get binding from child span element if not on the div
                if (string.IsNullOrEmpty(bindingAttr))
                {
                    var dtText = elem.Descendants()
                        .FirstOrDefault(e => e.Attribute("class")?.Value?.Contains("xdDTText") == true);
                    if (dtText != null)
                    {
                        bindingAttr = GetAttributeValue(dtText, "binding") ?? GetAttributeValue(dtText, "xd:binding");
                    }
                }

                control.Binding = bindingAttr;
                control.Label = elem.Attribute("title")?.Value ?? "";
                control.Name = GenerateControlName(elem, control.Label, control.Binding, control.Type);

                Debug.WriteLine($"[TRY EXTRACT] Created DatePicker control: {control.Name}");

                // Don't need to continue processing - we've handled the DatePicker
            }
            // Priority 1: Check for span controls with binding (MOST IMPORTANT FOR INFOPATH)
            else if (elemName == "span" && !string.IsNullOrEmpty(bindingAttr))
            {
                control = new ControlDefinition
                {
                    Type = DetermineControlType(elem),
                    Binding = bindingAttr,
                    Label = elem.Attribute("title")?.Value ?? "",
                    DocIndex = ++docIndexCounter
                };

                control.Name = GenerateControlName(elem, control.Label, control.Binding, control.Type);
                Debug.WriteLine($"[TRY EXTRACT] Created span control: {control.Name} with binding {control.Binding}");
            }
            // Priority 2: Labels
            else if (control == null && string.IsNullOrEmpty(bindingAttr) && IsLabelElement(elem))
            {
                var labelText = ExtractLabelText(elem);
                if (!string.IsNullOrWhiteSpace(labelText) && labelText.Length > 1)
                {
                    control = new ControlDefinition
                    {
                        Name = GenerateControlName(elem, labelText, "", "Label"),
                        Type = "Label",
                        Label = labelText,
                        Binding = "",
                        DocIndex = ++docIndexCounter
                    };
                    Debug.WriteLine($"[TRY EXTRACT] Created label control: {control.Name} with text: {labelText}");
                }
            }
            // Priority 3: Check for xctname controls (but not structural elements)
            else if (control == null && !string.IsNullOrEmpty(xctAttr))
            {
                // Skip structural elements
                string[] structuralElements = {
            "ExpressionBox", "Section", "RepeatingSection",
            "RepeatingTable", "repeatingtable", "OptionalSection"
        };

                if (!structuralElements.Any(s => s.Equals(xctAttr, StringComparison.OrdinalIgnoreCase)))
                {
                    Debug.WriteLine($"[TRY EXTRACT] Processing xctname control: {xctAttr}");
                    control = ParseXctControl(elem, xctAttr);
                }
                else
                {
                    Debug.WriteLine($"[TRY EXTRACT] Skipping structural element: {xctAttr}");
                    debugInfo.SkippedElements.Add($"Structural: {xctAttr}");
                }
            }
            // Priority 4: HTML form controls
            else if (control == null && (elemName == "input" || elemName == "select" || elemName == "textarea"))
            {
                Debug.WriteLine($"[TRY EXTRACT] Processing HTML control: {elemName}");
                control = ParseHtmlControl(elem);
            }
            // Priority 5: ActiveX/Object controls
            else if (control == null && elemName == "object")
            {
                Debug.WriteLine($"[TRY EXTRACT] Processing ActiveX control");
                control = ParseActiveXControl(elem);
            }
            // Priority 6: Any other element with binding
            else if (control == null && !string.IsNullOrEmpty(bindingAttr))
            {
                Debug.WriteLine($"[TRY EXTRACT] Processing generic bound control");
                control = ParseGenericBoundControl(elem);
            }

            // If we found a control, finalize it
            if (control != null)
            {
                Debug.WriteLine($"[TRY EXTRACT] Successfully created control:");
                Debug.WriteLine($"  Name: {control.Name}");
                Debug.WriteLine($"  Type: {control.Type}");
                Debug.WriteLine($"  Binding: {control.Binding}");
                Debug.WriteLine($"  Label: {control.Label}");

                // Assign grid position BEFORE storing
                control.GridPosition = GetCurrentGridPosition();

                // Store CtrlId if present
                if (!string.IsNullOrEmpty(ctrlId))
                {
                    control.Properties["CtrlId"] = ctrlId;
                }

                // Mark this control as processed in this context
                if (!string.IsNullOrEmpty(controlKey))
                {
                    processedControls.Add(controlKey);
                    Debug.WriteLine($"[TRY EXTRACT] Marked as processed with key: {controlKey}");
                }

                // Copy relevant attributes
                foreach (var attr in elem.Attributes())
                {
                    var attrName = attr.Name.LocalName;
                    if (!ShouldSkipAttribute(attrName) && !control.Properties.ContainsKey(attrName))
                    {
                        control.Properties[attrName] = attr.Value;
                    }
                }

                // Extract dropdown options if applicable
                if (control.Type == "DropDown" || control.Type == "ComboBox" || control.Type == "ListBox")
                {
                    Debug.WriteLine($"[TRY EXTRACT] Extracting dropdown options for {control.Name}");
                    ExtractDropdownOptions(elem, control);
                }

                // Extract radio button options if applicable
                if (control.Type == "RadioButton" && elemName == "input")
                {
                    var inputType = elem.Attribute("type")?.Value;
                    if (inputType?.Equals("radio", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        Debug.WriteLine($"[TRY EXTRACT] Extracting radio button options for {control.Name}");
                        ExtractRadioButtonOptions(elem, control);
                    }
                }

                Debug.WriteLine($"[TRY EXTRACT] Final control: Name={control.Name}, Type={control.Type}, Binding={control.Binding}, Grid={control.GridPosition}");
            }

            return control;
        }
        private void ExtractDropdownOptions(XElement elem, ControlDefinition control)
        {
            try
            {
                Debug.WriteLine($"Extracting dropdown options for control: {control.Name} (Type: {control.Type})");

                var options = elem.Descendants()
                    .Where(e => e.Name.LocalName.Equals("option", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                Debug.WriteLine($"Found {options.Count} option elements");

                if (options.Any())
                {
                    control.DataOptions = new List<DataOption>();
                    int order = 0;

                    foreach (var option in options)
                    {
                        var dataOption = new DataOption
                        {
                            Value = option.Attribute("value")?.Value ?? "",
                            DisplayText = GetOptionDisplayText(option),
                            Order = order++
                        };

                        var selectedAttr = option.Attribute("selected");
                        if (selectedAttr != null)
                            dataOption.IsDefault = true;

                        control.DataOptions.Add(dataOption);
                    }

                    control.DataOptionsString = string.Join(", ",
                        control.DataOptions.Select(o => o.DisplayText));

                    Debug.WriteLine($"DataOptionsString: {control.DataOptionsString}");

                    control.Properties["DataValues"] = control.DataOptionsString;
                    if (control.DataOptions.Any(o => o.IsDefault))
                    {
                        var defaultOption = control.DataOptions.First(o => o.IsDefault);
                        control.Properties["DefaultValue"] = defaultOption.Value;
                    }
                }
                else
                {
                    Debug.WriteLine("No option elements found in dropdown");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error extracting dropdown options: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private string GetOptionDisplayText(XElement option)
        {
            var text = "";

            foreach (var node in option.Nodes())
            {
                if (node is XText textNode)
                {
                    var nodeText = textNode.Value?.Trim();
                    if (!string.IsNullOrEmpty(nodeText) && !nodeText.Equals("selected", StringComparison.OrdinalIgnoreCase))
                    {
                        text = nodeText;
                        break;
                    }
                }
                else if (node is XElement elem)
                {
                    if (!elem.Name.LocalName.Equals("if", StringComparison.OrdinalIgnoreCase))
                    {
                        var childText = GetDirectTextContent(elem)?.Trim();
                        if (!string.IsNullOrEmpty(childText) && !childText.Equals("selected", StringComparison.OrdinalIgnoreCase))
                        {
                            text = childText;
                            break;
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(text))
                text = option.Attribute("value")?.Value ?? "";

            return text;
        }



        private string GetCurrentGridPosition()
        {
            return currentRow + GetColumnLetter(currentCol);
        }

        private string DetermineControlType(XElement elem)
        {
            var xctname = GetAttributeValue(elem, "xctname");
            if (!string.IsNullOrEmpty(xctname))
            {
                return MapControlType(xctname);
            }

            var className = elem.Attribute("class")?.Value ?? "";

            if (className.Contains("xdDTPicker") || className.Contains("xdDatePicker"))
                return "DatePicker";
            if (className.Contains("xdTextBox"))
                return "TextField";
            if (className.Contains("xdComboBox"))
                return "DropDown";
            if (className.Contains("xdCheckBox") || className.Contains("xdBehavior_Boolean"))
                return "CheckBox";
            if (className.Contains("xdRichTextBox"))
                return "RichText";
            if (className.Contains("xdListBox"))
                return "ListBox";

            var datafmt = GetAttributeValue(elem, "datafmt");
            if (datafmt?.Contains("date") == true)
                return "DatePicker";

            return "TextField";
        }

        // Helper: Get binding from multiple possible attributes
        private string GetBindingAttribute(XElement elem)
        {
            // Try various binding attribute patterns used by InfoPath
            string[] bindingAttributes = {
        "binding",
        "xd:binding",
        "ref",
        "xd:ref",
        "xd:xmlToEdit",
        "xmlToEdit",
        "CtrlId_binding"
    };

            foreach (var attrName in bindingAttributes)
            {
                var value = GetAttributeValue(elem, attrName);
                if (!string.IsNullOrEmpty(value))
                {
                    Debug.WriteLine($"Found binding in attribute '{attrName}': {value}");
                    return value;
                }
            }

            // For some controls, check parent elements
            var parent = elem.Parent;
            if (parent != null)
            {
                foreach (var attrName in bindingAttributes)
                {
                    var value = GetAttributeValue(parent, attrName);
                    if (!string.IsNullOrEmpty(value))
                    {
                        Debug.WriteLine($"Found binding in parent attribute '{attrName}': {value}");
                        return value;
                    }
                }
            }

            return "";
        }

        // Helper: Dynamically infer binding from control name
        private string InferBindingDynamically(XElement elem, string controlName, string controlType)
        {
            if (string.IsNullOrEmpty(controlName))
                return "";

            // First, try to find actual binding from XSL context
            var contextBinding = FindBindingInContext(elem, controlName);
            if (!string.IsNullOrEmpty(contextBinding))
                return contextBinding;

            // Convert control name to camelCase for binding
            var camelCaseName = ConvertToCamelCase(controlName);

            // Build the namespace prefix (usually "my:")
            var nsPrefix = DetermineNamespacePrefix(elem);

            return $"{nsPrefix}{camelCaseName}";
        }

        // Helper: Find binding in XSL context
        private string FindBindingInContext(XElement elem, string controlName)
        {
            // Look for xsl:value-of or similar elements that might reveal the binding
            var parent = elem.Parent;
            int searchDepth = 0;

            while (parent != null && searchDepth < 5) // Limit search depth
            {
                // Check for value-of, copy-of, or apply-templates with select
                var xslElements = parent.Elements()
                    .Where(e => e.Name.LocalName == "value-of" ||
                               e.Name.LocalName == "copy-of" ||
                               e.Name.LocalName == "apply-templates")
                    .Where(e => e.Attribute("select") != null);

                foreach (var xslElem in xslElements)
                {
                    var select = xslElem.Attribute("select")?.Value;
                    if (!string.IsNullOrEmpty(select))
                    {
                        // Check if this might match our control
                        var parts = select.Split('/');
                        var lastPart = parts.Last().Replace("my:", "").Replace("@", "");

                        if (MatchesControlName(lastPart, controlName))
                        {
                            Debug.WriteLine($"Found matching binding in context: {select}");
                            return select;
                        }
                    }
                }

                parent = parent.Parent;
                searchDepth++;
            }

            return "";
        }

        // Helper: Check if names match (case-insensitive, various formats)
        private bool MatchesControlName(string name1, string name2)
        {
            if (string.IsNullOrEmpty(name1) || string.IsNullOrEmpty(name2))
                return false;

            // Direct match
            if (string.Equals(name1, name2, StringComparison.OrdinalIgnoreCase))
                return true;

            // Convert both to camelCase and compare
            var camel1 = ConvertToCamelCase(name1);
            var camel2 = ConvertToCamelCase(name2);

            return string.Equals(camel1, camel2, StringComparison.OrdinalIgnoreCase);
        }

        // Helper: Convert to camelCase dynamically
        private string ConvertToCamelCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return "";

            var result = new StringBuilder();
            bool nextUpper = false;
            bool allUpper = input.ToUpper() == input;
            bool firstChar = true;

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];

                // Handle separators
                if (c == '_' || c == '-' || c == ' ')
                {
                    nextUpper = true;
                    continue;
                }

                // Handle digits
                if (char.IsDigit(c))
                {
                    result.Append(c);
                    firstChar = false;
                    continue;
                }

                // Handle letters
                if (char.IsLetter(c))
                {
                    if (firstChar)
                    {
                        // First character is lowercase for camelCase
                        result.Append(char.ToLower(c));
                        firstChar = false;
                    }
                    else if (nextUpper)
                    {
                        result.Append(char.ToUpper(c));
                        nextUpper = false;
                    }
                    else if (allUpper)
                    {
                        result.Append(char.ToLower(c));
                    }
                    else
                    {
                        // For mixed case, preserve the pattern
                        // But if transitioning from lowercase to uppercase, keep it
                        if (i > 0 && char.IsLower(input[i - 1]) && char.IsUpper(c))
                        {
                            result.Append(c);
                        }
                        else
                        {
                            result.Append(char.ToLower(c));
                        }
                    }
                }
            }

            return result.ToString();
        }

        // Helper: Determine namespace prefix from document
        private string DetermineNamespacePrefix(XElement elem)
        {
            // Look for namespace declarations
            var doc = elem.Document;
            if (doc != null)
            {
                var root = doc.Root;
                var namespaces = root.Attributes()
                    .Where(a => a.IsNamespaceDeclaration)
                    .ToList();

                // Look for the "my" namespace (most common in InfoPath)
                var myNamespace = namespaces.FirstOrDefault(a => a.Name.LocalName == "my");
                if (myNamespace != null)
                {
                    return "my:";
                }

                // Look for other common prefixes
                foreach (var ns in namespaces)
                {
                    var prefix = ns.Name.LocalName;
                    if (prefix != "xmlns" && prefix != "xsl" && prefix != "xd")
                    {
                        return prefix + ":";
                    }
                }
            }

            // Default to "my:"
            return "my:";
        }

        // Helper: Copy relevant attributes to control properties
        private void CopyRelevantAttributes(XElement elem, ControlDefinition control)
        {
            // List of attributes to copy
            string[] relevantAttributes = {
        "required", "readonly", "disabled", "maxlength", "pattern",
        "min", "max", "step", "placeholder", "defaultValue",
        "datafmt", "boundProp", "disableEditing", "noWrap"
    };

            foreach (var attrName in relevantAttributes)
            {
                var value = GetAttributeValue(elem, attrName);
                if (!string.IsNullOrEmpty(value) && !control.Properties.ContainsKey(attrName))
                {
                    control.Properties[attrName] = value;
                }
            }
        }

        // Helper: Check if element name indicates a structural element
        private bool IsStructuralElement(string xctname)
        {
            if (string.IsNullOrEmpty(xctname))
                return false;

            string[] structural = {
        "Section", "RepeatingSection", "RepeatingTable",
        "repeatingtable", "ExpressionBox", "OptionalSection"
    };

            return structural.Any(s => s.Equals(xctname, StringComparison.OrdinalIgnoreCase));
        }

        // Helper: Check if class indicates a control
        private bool IsControlClass(string className)
        {
            if (string.IsNullOrEmpty(className))
                return false;

            // These patterns indicate controls in InfoPath
            string[] patterns = {
        "xdTextBox", "xdComboBox", "xdDTPicker", "xdDatePicker",
        "xdCheckBox", "xdRadioButton", "xdListBox", "xdRichTextBox",
        "xdBehavior_", "xdExpressionBox", "xdRepeating"
    };

            return patterns.Any(pattern => className.Contains(pattern));
        }

        private ControlDefinition ParseClassBasedControl(XElement elem, string className)
        {
            var control = new ControlDefinition
            {
                DocIndex = ++docIndexCounter
            };

            // Map class to control type
            if (className.Contains("xdTextBox"))
                control.Type = "TextField";
            else if (className.Contains("xdComboBox"))
                control.Type = "DropDown";
            else if (className.Contains("xdDTPicker") || className.Contains("xdDatePicker"))
                control.Type = "DatePicker";
            else if (className.Contains("xdCheckBox") || className.Contains("xdBehavior_Boolean"))
                control.Type = "CheckBox";
            else if (className.Contains("xdRadioButton"))
                control.Type = "RadioButton";
            else if (className.Contains("xdListBox"))
                control.Type = "ListBox";
            else if (className.Contains("xdRichTextBox"))
                control.Type = "RichText";
            else
                control.Type = "TextField"; // Default

            control.Binding = GetAttributeValue(elem, "binding") ??
                             GetAttributeValue(elem, "xd:binding");
            control.Label = elem.Attribute("title")?.Value ?? "";
            control.Name = GenerateControlName(elem, control.Label, control.Binding, control.Type);

            // Copy all attributes
            foreach (var attr in elem.Attributes())
            {
                if (!ShouldSkipAttribute(attr.Name.LocalName))
                {
                    control.Properties[attr.Name.LocalName] = attr.Value;
                }
            }

            return control;
        }

        private string GenerateControlName(XElement elem, string label, string binding, string controlType)
        {
            // Priority 1: Use explicit name attribute
            var nameAttr = elem.Attribute("name")?.Value;
            if (!string.IsNullOrWhiteSpace(nameAttr))
            {
                return RemoveSpaces(nameAttr).ToUpper();
            }

            // Priority 2: Try to extract from binding FIRST (more reliable than label)
            if (!string.IsNullOrWhiteSpace(binding))
            {
                var parts = binding.Split('/');
                var lastPart = parts.Last();

                if (lastPart.Contains(':'))
                {
                    lastPart = lastPart.Split(':').Last();
                }

                if (!string.IsNullOrWhiteSpace(lastPart))
                {
                    // Check if this might be "FIRM" or similar
                    var cleanName = RemoveSpaces(lastPart).ToUpper();
                    if (!string.IsNullOrEmpty(cleanName))
                    {
                        Debug.WriteLine($"Generated name from binding: {cleanName} (binding: {binding})");
                        return cleanName;
                    }
                }
            }

            // Priority 3: Use title/label if available and meaningful
            if (!string.IsNullOrWhiteSpace(label) && label.Length > 1)
            {
                var cleanLabel = label.TrimEnd(':').Trim();
                if (!string.IsNullOrWhiteSpace(cleanLabel))
                {
                    return RemoveSpaces(cleanLabel).ToUpper();
                }
            }

            // Priority 4: Use control ID if available
            var ctrlId = GetAttributeValue(elem, "CtrlId");
            if (!string.IsNullOrWhiteSpace(ctrlId))
            {
                return ctrlId.ToUpper();
            }

            // Priority 5: Generate based on control type and position
            return $"{controlType.ToUpper()}_{docIndexCounter}";
        }

        private XElement GetNextInputElement(XElement elem)
        {
            // Look for the next sibling that's an input control
            var parent = elem.Parent;
            if (parent == null) return null;

            bool foundCurrent = false;
            foreach (var sibling in parent.Elements())
            {
                if (foundCurrent)
                {
                    // Check if this is an input control
                    var elemName = sibling.Name.LocalName.ToLower();
                    if (elemName == "input" || elemName == "select" || elemName == "textarea")
                        return sibling;

                    var xctname = GetAttributeValue(sibling, "xctname");
                    if (!string.IsNullOrEmpty(xctname))
                        return sibling;

                    var binding = GetAttributeValue(sibling, "binding");
                    if (!string.IsNullOrEmpty(binding))
                        return sibling;

                    // Check first child
                    var childInput = sibling.Elements().FirstOrDefault(c =>
                    {
                        var childName = c.Name.LocalName.ToLower();
                        return childName == "input" || childName == "select" ||
                               !string.IsNullOrEmpty(GetAttributeValue(c, "binding"));
                    });

                    if (childInput != null)
                        return childInput;
                }

                if (sibling == elem)
                    foundCurrent = true;
            }

            return null;
        }




        private ControlDefinition ParseXctControl(XElement elem, string xctType)
        {
            var mappedType = xctType;
            if (xctType.StartsWith("{") && xctType.EndsWith("}"))
            {
                mappedType = xctType.Contains("61e40d31-993d-4777-8fa0-19ca59b6d0bb") ? "PeoplePicker" : "ActiveX-" + xctType;
            }
            else
            {
                mappedType = MapControlType(xctType);
            }

            var control = new ControlDefinition
            {
                Type = mappedType,
                DocIndex = ++docIndexCounter,
                Label = elem.Attribute("title")?.Value ?? "",
                Binding = GetAttributeValue(elem, "binding")
            };

            control.Name = string.IsNullOrEmpty(control.Label) ? "" : RemoveSpaces(control.Label);

            if (string.IsNullOrEmpty(control.Name) && !string.IsNullOrEmpty(control.Binding))
            {
                var parts = control.Binding.Split('/');
                var lastPart = parts.Last();
                control.Name = lastPart.Contains(':') ? lastPart.Split(':').Last() : lastPart;
            }

            // Generate a proper name if still empty
            if (string.IsNullOrEmpty(control.Name))
            {
                control.Name = GenerateControlName(elem, control.Label, control.Binding, control.Type);
            }

            // Extract dropdown options for dropdown/combobox types
            if (mappedType.Equals("DropDown", StringComparison.OrdinalIgnoreCase) ||
                mappedType.Equals("ComboBox", StringComparison.OrdinalIgnoreCase) ||
                xctType.Equals("dropdown", StringComparison.OrdinalIgnoreCase) ||
                elem.Name.LocalName.Equals("select", StringComparison.OrdinalIgnoreCase))
            {
                ExtractDropdownOptions(elem, control);
            }

            foreach (var attr in elem.Attributes())
            {
                if (!ShouldSkipAttribute(attr.Name.LocalName))
                    control.Properties[attr.Name.LocalName] = attr.Value;
            }

            return control;
        }

        private ControlDefinition ParseHtmlControl(XElement elem)
        {
            var control = new ControlDefinition { DocIndex = ++docIndexCounter };

            var xctname = GetAttributeValue(elem, "xctname");
            if (!string.IsNullOrEmpty(xctname))
            {
                if (xctname.StartsWith("{") && xctname.EndsWith("}"))
                {
                    control.Type = xctname.Contains("61e40d31-993d-4777-8fa0-19ca59b6d0bb")
                        ? "PeoplePicker" : "ActiveX-" + xctname;
                }
                else
                {
                    control.Type = MapControlType(xctname);
                }

                if (xctname.Equals("dropdown", StringComparison.OrdinalIgnoreCase) ||
                    control.Type.Equals("DropDown", StringComparison.OrdinalIgnoreCase) ||
                    control.Type.Equals("ComboBox", StringComparison.OrdinalIgnoreCase))
                {
                    ExtractDropdownOptions(elem, control);
                }
            }
            else
            {
                var name = elem.Name.LocalName.ToLower();
                if (name == "select")
                {
                    control.Type = "DropDown";
                    ExtractDropdownOptions(elem, control);
                }
                else if (name == "textarea")
                {
                    control.Type = "RichText";
                }
                else if (name == "input")
                {
                    var type = elem.Attribute("type")?.Value ?? "text";
                    control.Type = MapInputType(type);

                    if (type.Equals("radio", StringComparison.OrdinalIgnoreCase))
                        ExtractRadioButtonOptions(elem, control);

                    if (type.Equals("checkbox", StringComparison.OrdinalIgnoreCase))
                    {
                        var isChecked = elem.Attribute("checked") != null;
                        control.Properties["DefaultValue"] = isChecked.ToString();
                    }
                }
            }

            // Extract dropdown options if we haven't already
            if (elem.Name.LocalName.ToLower() == "select" &&
                (control.DataOptions == null || control.DataOptions.Count == 0))
            {
                ExtractDropdownOptions(elem, control);
            }

            control.Name = elem.Attribute("name")?.Value ?? "";
            control.Label = elem.Attribute("title")?.Value ?? "";
            control.Binding = GetAttributeValue(elem, "binding");

            var valueAttr = elem.Attribute("value")?.Value;
            if (!string.IsNullOrEmpty(valueAttr) && !control.Properties.ContainsKey("DefaultValue"))
                control.Properties["DefaultValue"] = valueAttr;

            if (string.IsNullOrEmpty(control.Name) && !string.IsNullOrEmpty(control.Binding))
            {
                var parts = control.Binding.Split('/');
                var lastPart = parts.Last();
                control.Name = lastPart.Contains(':') ? lastPart.Split(':').Last() : lastPart;
            }

            // Generate a proper name if still empty
            if (string.IsNullOrEmpty(control.Name))
            {
                control.Name = GenerateControlName(elem, control.Label, control.Binding, control.Type);
            }

            foreach (var attr in elem.Attributes())
            {
                if (!ShouldSkipAttribute(attr.Name.LocalName))
                    control.Properties[attr.Name.LocalName] = attr.Value;
            }

            return control;
        }

        private void ExtractRadioButtonOptions(XElement elem, ControlDefinition control)
        {
            try
            {
                var radioGroup = GetRadioButtonGroup(elem);
                if (radioGroup != null && radioGroup.Any())
                {
                    control.DataOptions = new List<DataOption>();
                    int order = 0;

                    foreach (var radio in radioGroup)
                    {
                        var dataOption = new DataOption
                        {
                            Value = radio.Attribute("value")?.Value ?? "",
                            DisplayText = GetRadioButtonLabel(radio),
                            Order = order++
                        };

                        var checkedAttr = radio.Attribute("checked");
                        if (checkedAttr != null)
                            dataOption.IsDefault = true;

                        control.DataOptions.Add(dataOption);
                    }

                    control.DataOptionsString = string.Join(", ",
                        control.DataOptions.Select(o => o.DisplayText));
                    control.Properties["DataValues"] = control.DataOptionsString;

                    // Set default value if one is selected
                    if (control.DataOptions.Any(o => o.IsDefault))
                    {
                        var defaultOption = control.DataOptions.First(o => o.IsDefault);
                        control.Properties["DefaultValue"] = defaultOption.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error extracting radio options: {ex.Message}");
            }
        }

        private List<XElement> GetRadioButtonGroup(XElement radioElement)
        {
            var name = radioElement.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name))
                return null;

            // Find the container element (div or td) that holds all radio buttons in this group
            var container = radioElement.Parent;
            while (container != null &&
                   !container.Name.LocalName.Equals("div", StringComparison.OrdinalIgnoreCase) &&
                   !container.Name.LocalName.Equals("td", StringComparison.OrdinalIgnoreCase))
            {
                container = container.Parent;
            }

            if (container == null)
                container = radioElement.Parent;

            // Find all radio buttons with the same name within the container
            return container.Descendants()
                .Where(e => e.Name.LocalName.Equals("input", StringComparison.OrdinalIgnoreCase) &&
                           e.Attribute("type")?.Value?.Equals("radio", StringComparison.OrdinalIgnoreCase) == true &&
                           e.Attribute("name")?.Value == name)
                .ToList();
        }

        private string GetRadioButtonLabel(XElement radio)
        {
            // Method 1: Check for associated label element with "for" attribute
            var id = radio.Attribute("id")?.Value;
            if (!string.IsNullOrEmpty(id))
            {
                var label = radio.Parent?.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName.Equals("label", StringComparison.OrdinalIgnoreCase) &&
                                       e.Attribute("for")?.Value == id);
                if (label != null)
                    return label.Value?.Trim() ?? "";
            }

            // Method 2: Check for text immediately following the radio button
            var nextNode = radio.NextNode;
            while (nextNode != null)
            {
                if (nextNode is XText textNode && !string.IsNullOrWhiteSpace(textNode.Value))
                    return textNode.Value.Trim();

                if (nextNode is XElement elem && elem.Name.LocalName.Equals("label", StringComparison.OrdinalIgnoreCase))
                    return elem.Value?.Trim() ?? "";

                nextNode = nextNode.NextNode;
            }

            // Method 3: If no label found, use the value attribute
            return radio.Attribute("value")?.Value ?? "";
        }

        private ControlDefinition ParseActiveXControl(XElement elem)
        {
            var control = new ControlDefinition { DocIndex = ++docIndexCounter };

            var xctname = GetAttributeValue(elem, "xctname");
            if (!string.IsNullOrEmpty(xctname))
            {
                if (xctname.Contains("61e40d31-993d-4777-8fa0-19ca59b6d0bb"))
                    control.Type = "PeoplePicker";
                else if (xctname.StartsWith("{") && xctname.EndsWith("}"))
                    control.Type = "ActiveX-" + xctname;
                else
                    control.Type = MapControlType(xctname);
            }
            else
            {
                var classId = elem.Attribute("classid")?.Value ?? "";
                control.Type = classId.Contains("61e40d31-993d-4777-8fa0-19ca59b6d0bb") ? "PeoplePicker" : "ActiveX";
            }

            var ctrlIdAttr = GetAttributeValue(elem, "CtrlId");
            if (!string.IsNullOrEmpty(ctrlIdAttr))
            {
                if (processedControls.Contains(ctrlIdAttr)) return null;
                processedControls.Add(ctrlIdAttr);
                control.Properties["CtrlId"] = ctrlIdAttr;
            }

            var ctrlIdParam = elem.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "param" && e.Attribute("name")?.Value == "CtrlId");
            if (ctrlIdParam != null)
            {
                var ctrlId = ctrlIdParam.Attribute("value")?.Value;
                if (!string.IsNullOrEmpty(ctrlId))
                {
                    if (processedControls.Contains(ctrlId)) return null;
                    processedControls.Add(ctrlId);
                    control.Properties["CtrlId"] = ctrlId;
                }
            }

            control.Binding = GetAttributeValue(elem, "binding");
            control.Label = elem.Attribute("title")?.Value ?? "";

            if (string.IsNullOrEmpty(control.Binding))
            {
                var bindingParam = elem.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "param" &&
                                         e.Attribute("name")?.Value == "binding");
                if (bindingParam != null)
                    control.Binding = bindingParam.Attribute("value")?.Value ?? "";
            }

            if (!string.IsNullOrEmpty(control.Binding))
            {
                var parts = control.Binding.Split('/');
                var lastPart = parts.Last();
                control.Name = lastPart.Contains(':') ? lastPart.Split(':').Last() : lastPart;
            }

            foreach (var param in elem.Descendants().Where(e => e.Name.LocalName == "param"))
            {
                var name = param.Attribute("name")?.Value;
                var value = param.Attribute("value")?.Value;
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(value))
                    control.Properties[name] = value;
            }

            foreach (var attr in elem.Attributes())
            {
                if (!ShouldSkipAttribute(attr.Name.LocalName))
                    control.Properties[attr.Name.LocalName] = attr.Value;
            }

            return control;
        }

        private void ProcessFontElement(XElement fontElem)
        {
            Debug.WriteLine($"[FONT ELEMENT] Processing font element");

            // Process in order: text nodes and element nodes
            foreach (var node in fontElem.Nodes())
            {
                if (node is XText textNode)
                {
                    var text = textNode.Value.Trim();
                    if (!string.IsNullOrWhiteSpace(text) && text.Length > 1)
                    {
                        // This is a label text directly in the font element
                        var labelControl = new ControlDefinition
                        {
                            Name = GenerateControlName(fontElem, text, "", "Label"),
                            Type = "Label",
                            Label = text,
                            Binding = "",
                            DocIndex = ++docIndexCounter,
                            GridPosition = currentRow + GetColumnLetter(currentCol)
                        };

                        // Copy font attributes
                        var color = fontElem.Attribute("color")?.Value;
                        var face = fontElem.Attribute("face")?.Value;
                        var size = fontElem.Attribute("size")?.Value;

                        if (!string.IsNullOrEmpty(color))
                            labelControl.Properties["color"] = color;
                        if (!string.IsNullOrEmpty(face))
                            labelControl.Properties["face"] = face;
                        if (!string.IsNullOrEmpty(size))
                            labelControl.Properties["size"] = size;

                        ApplyControlContext(labelControl);
                        allControls.Add(labelControl);
                        currentCol++;

                        Debug.WriteLine($"[FONT ELEMENT] Created label from text node: {labelControl.Name} - {labelControl.Label}");
                    }
                }
                else if (node is XElement childElem)
                {
                    // Process child element (which may be a control or another font)
                    ProcessElement(childElem);
                }
            }
        }
        private ControlDefinition ParseGenericBoundControl(XElement elem)
        {
            var control = new ControlDefinition { DocIndex = ++docIndexCounter };

            var elemName = elem.Name.LocalName.ToLower();
            var className = elem.Attribute("class")?.Value ?? "";

            if (className.Contains("xdBehavior_Boolean"))
            {
                control.Type = "CheckBox";
            }
            else if (className.Contains("xdTextBox"))
            {
                control.Type = "TextField";
            }
            else if (className.Contains("xdComboBox"))
            {
                control.Type = "DropDown";
                ExtractDropdownOptions(elem, control);
            }
            else if (className.Contains("xdDTPicker"))
            {
                control.Type = "DatePicker";
            }
            else if (elemName == "select")
            {
                control.Type = "DropDown";
                ExtractDropdownOptions(elem, control);
            }
            else if (elemName == "input")
            {
                var inputType = elem.Attribute("type")?.Value ?? "text";
                control.Type = MapInputType(inputType);
            }
            else
            {
                control.Type = elemName;
            }

            control.Binding = GetAttributeValue(elem, "binding");
            control.Label = elem.Attribute("title")?.Value ?? "";

            if (!string.IsNullOrEmpty(control.Binding))
            {
                var parts = control.Binding.Split('/');
                var lastPart = parts.Last();
                control.Name = lastPart.Contains(':') ? lastPart.Split(':').Last() : lastPart;
            }

            // Generate a proper name
            if (string.IsNullOrEmpty(control.Name))
            {
                control.Name = GenerateControlName(elem, control.Label, control.Binding, control.Type);
            }

            foreach (var attr in elem.Attributes())
            {
                if (!ShouldSkipAttribute(attr.Name.LocalName))
                    control.Properties[attr.Name.LocalName] = attr.Value;
            }

            return control;
        }

        private bool IsLabelElement(XElement elem)
        {
            var elemName = elem.Name.LocalName.ToLower();

            // Check for common label elements
            if (elemName == "strong" || elemName == "font" || elemName == "label" ||
                elemName == "em" || elemName == "b" || elemName == "i")
            {
                // Make sure this element doesn't contain input controls
                if (!elem.Descendants().Any(d =>
                    !string.IsNullOrEmpty(GetAttributeValue(d, "xctname")) ||
                    !string.IsNullOrEmpty(GetAttributeValue(d, "binding")) ||
                    d.Name.LocalName.Equals("object", StringComparison.OrdinalIgnoreCase) ||
                    d.Name.LocalName.Equals("input", StringComparison.OrdinalIgnoreCase) ||
                    d.Name.LocalName.Equals("select", StringComparison.OrdinalIgnoreCase) ||
                    d.Name.LocalName.Equals("textarea", StringComparison.OrdinalIgnoreCase)))
                {
                    // Check if it has meaningful text content
                    var text = GetDirectTextContent(elem).Trim();
                    return !string.IsNullOrWhiteSpace(text) && text.Length > 1;
                }
            }

            return false;
        }
        private string ExtractLabelText(XElement elem)
        {
            var sb = new StringBuilder();

            // Only get direct text nodes, not text from child elements
            foreach (var node in elem.Nodes())
            {
                if (node is XText textNode)
                {
                    sb.Append(textNode.Value);
                }
                // Don't recurse into child elements
            }

            var text = sb.ToString().Trim();

            // Clean up the text
            text = Regex.Replace(text, @"\s+", " ");

            return text;
        }

        private string GetDirectTextContent(XElement elem)
        {
            var sb = new StringBuilder();

            // Only get direct text nodes
            foreach (var node in elem.Nodes())
            {
                if (node is XText textNode)
                {
                    sb.Append(textNode.Value);
                }
                // Don't include text from child elements
            }

            return sb.ToString();
        }

        private bool IsControlElement(XElement elem)
        {
            var elemName = elem.Name.LocalName.ToLower();

            // Check if it's an input control
            if (elemName == "input" || elemName == "select" || elemName == "textarea" ||
                elemName == "object" || elemName == "button")
                return true;

            // Check if it has control attributes
            if (!string.IsNullOrEmpty(GetAttributeValue(elem, "xctname")) ||
                !string.IsNullOrEmpty(GetAttributeValue(elem, "binding")))
                return true;

            return false;
        }

        private string MapControlType(string rawType)
        {
            var typeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "plaintext", "TextField" },
                { "dtpicker", "DatePicker" },
                { "richtext", "RichText" },
                { "dropdown", "DropDown" },
                { "combobox", "ComboBox" },
                { "checkbox", "CheckBox" },
                { "button", "Button" },
                { "fileattachment", "FileAttachment" },
                { "sharepoint:sharePointFileAttachment", "SharePointFileAttachment" },
                { "section", "Section" },
                { "repeatingsection", "RepeatingSection" },
                { "optionalsection", "OptionalSection" },
                { "repeatingtable", "RepeatingTable" },
                { "bulletedlist", "BulletedList" },
                { "numberedlist", "NumberedList" },
                { "plainnumberedlist", "PlainNumberedList" },
                { "multipleselectlist", "MultipleSelectList" },
                { "expresionbox", "ExpressionBox" },
                { "hyperlink", "Hyperlink" },
                { "datepicker", "DatePicker" },
                { "inlinepicture", "InlinePicture" },
                { "linkedpicture", "LinkedPicture" },
                { "signatureline", "SignatureLine" }
            };

            if (rawType.StartsWith("{") && rawType.EndsWith("}"))
            {
                if (rawType.Contains("61e40d31-993d-4777-8fa0-19ca59b6d0bb"))
                    return "PeoplePicker";

                return "ActiveX-" + rawType;
            }

            return typeMap.ContainsKey(rawType) ? typeMap[rawType] : rawType;
        }

        private string MapInputType(string inputType)
        {
            var typeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "text", "TextField" },
                { "checkbox", "CheckBox" },
                { "radio", "RadioButton" },
                { "button", "Button" },
                { "submit", "Button" }
            };

            return typeMap.ContainsKey(inputType) ? typeMap[inputType] : "TextField";
        }

        private string RemoveSpaces(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            return Regex.Replace(text, @"[^a-zA-Z0-9/]", "").ToUpper();
        }

        private string GetAttributeValue(XElement elem, string localName)
        {
            var attr = elem.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));
            return attr?.Value ?? "";
        }

        private bool ShouldSkipAttribute(string attrName)
        {
            string[] skip = { "style", "class", "hidefocus", "tabindex", "contenteditable", "xctname", "title", "binding" };
            return skip.Contains(attrName, StringComparer.OrdinalIgnoreCase);
        }

        private string GetColumnLetter(int columnNumber)
        {
            string columnName = "";
            while (columnNumber > 0)
            {
                columnNumber--;
                columnName = (char)('A' + columnNumber % 26) + columnName;
                columnNumber /= 26;
            }
            return columnName;
        }
    }

    #endregion

    #region Label-Control Associator

    public class LabelControlAssociator
    {
        public void AssociateLabelControls(List<ControlDefinition> controls)
        {
            var labels = controls.Where(c => c.Type == "Label").ToList();
            var inputs = controls.Where(c => c.Type != "Label" && c.Type != "Section").ToList();

            foreach (var label in labels)
            {
                var associatedControl = FindAssociatedControl(label, inputs);
                if (associatedControl != null)
                {
                    label.AssociatedControlId = associatedControl.Name;
                    associatedControl.AssociatedLabelId = label.Name;

                    if (string.IsNullOrEmpty(associatedControl.Label))
                        associatedControl.Label = label.Label;
                }
            }
        }

        private ControlDefinition FindAssociatedControl(ControlDefinition label, List<ControlDefinition> inputs)
        {
            var labelRow = ExtractRow(label.GridPosition);
            var labelCol = ExtractColumn(label.GridPosition);

            var sameRowControls = inputs
                .Where(c => ExtractRow(c.GridPosition) == labelRow &&
                            ExtractColumn(c.GridPosition) > labelCol)
                .OrderBy(c => ExtractColumn(c.GridPosition))
                .ToList();

            if (sameRowControls.Any())
                return sameRowControls.First();

            var nextRowControls = inputs
                .Where(c => ExtractRow(c.GridPosition) == labelRow + 1)
                .OrderBy(c => ExtractColumn(c.GridPosition))
                .ToList();

            if (nextRowControls.Any())
                return nextRowControls.First();

            var nextControl = inputs
                .Where(c => c.DocIndex > label.DocIndex)
                .OrderBy(c => c.DocIndex)
                .FirstOrDefault();

            return nextControl;
        }

        private int ExtractRow(string gridPosition)
        {
            var match = Regex.Match(gridPosition ?? "", @"^(\d+)");
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }

        private int ExtractColumn(string gridPosition)
        {
            var match = Regex.Match(gridPosition ?? "", @"([A-Z]+)$");
            if (!match.Success) return 0;

            var letters = match.Groups[1].Value;
            int column = 0;
            for (int i = 0; i < letters.Length; i++)
                column = column * 26 + (letters[i] - 'A' + 1);
            return column;
        }
    }

    #endregion

    #region Complex Label Handler

    public class ComplexLabelHandler
    {
        public void ProcessMultiLineLabels(List<ControlDefinition> controls)
        {
            // Don't process labels at all - keep them separate
            // This is the simplest and most reliable approach
            return;
        }

        private bool AreLabelsRelated(ControlDefinition label1, ControlDefinition label2)
        {
            if (label1.GridPosition == label2.GridPosition)
                return true;

            if (label2.DocIndex - label1.DocIndex == 1)
            {
                var row1 = ExtractRow(label1.GridPosition);
                var row2 = ExtractRow(label2.GridPosition);
                return Math.Abs(row1 - row2) <= 1;
            }

            return false;
        }

        private int ExtractRow(string gridPosition)
        {
            var match = Regex.Match(gridPosition ?? "", @"^(\d+)");
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }
    }

    #endregion

    #region Dynamic Section Handler

    public class DynamicSectionHandler
    {
        public List<DynamicSection> ExtractDynamicSections(XDocument xslDoc)
        {
            var dynamicSections = new List<DynamicSection>();
            var ns = xslDoc.Root.Name.Namespace;

            var applyTemplates = xslDoc.Descendants(ns + "apply-templates")
                .Where(e => e.Attribute("mode") != null)
                .ToList();

            foreach (var applyTemplate in applyTemplates)
            {
                var mode = applyTemplate.Attribute("mode").Value;
                var template = xslDoc.Descendants(ns + "template")
                    .FirstOrDefault(t => t.Attribute("mode")?.Value == mode);

                if (template != null)
                {
                    var section = ParseDynamicSection(template, mode);
                    if (section != null)
                        dynamicSections.Add(section);
                }
            }

            return dynamicSections;
        }

        private DynamicSection ParseDynamicSection(XElement template, string mode)
        {
            var ns = template.Name.Namespace;

            var ifElement = template.Descendants(ns + "if").FirstOrDefault();
            if (ifElement == null) return null;

            var condition = ifElement.Attribute("test")?.Value ?? "";

            var sectionInfo = new DynamicSection
            {
                Mode = mode,
                Condition = condition,
                ConditionField = ExtractConditionField(condition),
                ConditionValue = ExtractConditionValue(condition),
                Controls = new List<string>()
            };

            var sectionDiv = ifElement.Descendants()
                .FirstOrDefault(e => e.Attributes().Any(a => a.Name.LocalName == "CtrlId"));

            if (sectionDiv != null)
            {
                sectionInfo.CtrlId = sectionDiv.Attributes().FirstOrDefault(a => a.Name.LocalName == "CtrlId")?.Value;
                sectionInfo.Caption = sectionDiv.Attributes().FirstOrDefault(a => a.Name.LocalName == "caption_0")?.Value
                                      ?? sectionDiv.Attributes().FirstOrDefault(a => a.Name.LocalName == "caption")?.Value;
                sectionInfo.Controls = ExtractSectionControls(sectionDiv);
            }

            return sectionInfo;
        }

        private string ExtractConditionField(string condition)
        {
            var match = Regex.Match(condition ?? "", @"my:(\w+)");
            return match.Success ? match.Groups[1].Value : "";
        }

        private string ExtractConditionValue(string condition)
        {
            var match = Regex.Match(condition ?? "", @"contains\([^,]+,\s*[""']([^""']+)[""']\)");
            return match.Success ? match.Groups[1].Value : "";
        }

        private List<string> ExtractSectionControls(XElement sectionElement)
        {
            var controlElements = sectionElement.Descendants()
                .Where(e => e.Attributes().Any(a => a.Name.LocalName == "CtrlId"))
                .Select(e => e.Attributes().FirstOrDefault(a => a.Name.LocalName == "CtrlId")?.Value)
                .Where(v => !string.IsNullOrEmpty(v))
                .ToList();

            return controlElements;
        }
    }
}

    #endregion
