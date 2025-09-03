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

        private HashSet<string> processedTemplateModes;
        private Dictionary<string, string> tableNamesById;
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
        public InfoPathFormDefinition ParseXsnFile(string xsnFilePath)
        {
            string tempDir = Path.Combine(
                Path.GetTempPath(),
                Path.GetFileNameWithoutExtension(xsnFilePath) + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                Console.WriteLine("Extracting XSN to " + tempDir);

                if (!ExtractXsn(xsnFilePath, tempDir))
                    throw new Exception("Failed to extract XSN file using all available methods.");

                var formDef = new InfoPathFormDefinition();

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
                        Console.WriteLine($"Parsing view: {Path.GetFileName(vf)}");
                        var singleView = ParseSingleView(vf);

                        var xslDoc = XDocument.Load(vf);
                        var dynamicHandler = new DynamicSectionHandler();
                        var dynamicSections = dynamicHandler.ExtractDynamicSections(xslDoc);
                        formDef.DynamicSections.AddRange(dynamicSections);

                        formDef.Views.Add(singleView);
                    }
                }

                PostProcessFormDefinition(formDef);
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

            var sectionParser = new SectionAwareParser();
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

                string repeating = ctrl.IsInRepeatingSection ?
                    ctrl.RepeatingSectionName :
                    ctrl.ParentSection;

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

            var repeatingTables = allControls
                .Where(c => c.Type == "RepeatingTable")
                .ToList();
            repeatingSectionCount += repeatingTables.Count;

            var otherRepeating = allControls
                .Where(c => c.Type == "RepeatingSection")
                .ToList();
            repeatingSectionCount += otherRepeating.Count;

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

    #region Section-Aware Parser

    public class SectionAwareParser
    {
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

        private HashSet<string> viewRepeatingSectionNames;
        private string currentViewName;

        private HashSet<string> processedTemplateModes;
        private Dictionary<string, string> tableNamesById;

        private Stack<GridContext> gridContextStack;
        private Dictionary<string, GridPosition> sectionGridPositions;

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

        public class GridContext
        {
            public int Row { get; set; }
            public int Col { get; set; }
            public string ContextName { get; set; }
        }

        public class GridPosition
        {
            public int StartRow { get; set; }
            public int CurrentRow { get; set; }
            public int CurrentCol { get; set; }
        }

        public SectionAwareParser()
        {
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
            viewRepeatingSectionNames = new HashSet<string>();
            processedTemplateModes = new HashSet<string>(); // Add this
            tableNamesById = new Dictionary<string, string>();
            gridContextStack = new Stack<GridContext>(); // Add this
            sectionGridPositions = new Dictionary<string, GridPosition>();

        }

        public List<ControlDefinition> ParseViewFile(string viewFile)
        {
            ResetParserState();
            currentViewName = Path.GetFileName(viewFile);
            viewRepeatingSectionNames = new HashSet<string>();

            XDocument doc = XDocument.Load(viewFile);
            ProcessElement(doc.Root);
            return new List<ControlDefinition>(allControls);
        }

        private void ResetParserState()
        {
            InitializeCollections();
            docIndexCounter = 0;
            currentRow = 1;
            currentCol = 1;
            inTableRow = false;
            insideXslTemplate = false;
            currentTemplateMode = null;
        }

        public List<SectionInfo> GetSections()
            => new List<SectionInfo>(sections ?? new List<SectionInfo>());

        public Dictionary<string, ControlDefinition> GetControlsById()
            => new Dictionary<string, ControlDefinition>(controlsById ?? new Dictionary<string, ControlDefinition>());

        private void ProcessElement(XElement elem)
        {
            var elemName = elem.Name.LocalName.ToLower();

            // Check if this is a repeating section div
            if (IsRepeatingSection(elem))
            {
                var ctrlId = GetAttributeValue(elem, "CtrlId");
                if (!string.IsNullOrEmpty(ctrlId) && processedControls.Contains(ctrlId))
                    return;

                // Save current grid position before processing repeating section
                var savedRow = currentRow;
                var savedCol = currentCol;

                ProcessRepeatingSection(elem);

                // Move to next row after repeating section
                currentRow = Math.Max(currentRow, savedRow + 1);
                currentCol = 1;

                if (!string.IsNullOrEmpty(ctrlId))
                    processedControls.Add(ctrlId);
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
                    if (processedTemplateModes.Contains(mode))
                        return;

                    if (!processedTemplates.Contains(mode))
                    {
                        processedTemplates.Add(mode);

                        if (!string.IsNullOrEmpty(select) && IsRepeatingSelectPattern(select))
                        {
                            ProcessRepeatingApplyTemplates(elem, mode, select);
                        }
                        else
                        {
                            var matchingTemplate = FindTemplate(elem.Document, mode);
                            if (matchingTemplate != null)
                            {
                                ProcessXslTemplate(matchingTemplate);
                            }
                        }

                        processedTemplates.Remove(mode);
                    }
                    return;
                }
            }

            // Handle repeating tables
            if (IsRepeatingTable(elem))
            {
                var ctrlId = GetAttributeValue(elem, "CtrlId");
                if (!string.IsNullOrEmpty(ctrlId) && processedControls.Contains(ctrlId))
                    return;

                ProcessRepeatingTable(elem);

                if (!string.IsNullOrEmpty(ctrlId))
                    processedControls.Add(ctrlId);
                return;
            }

            // Handle new row indicators (like <tr>)
            if (elemName == "tr")
            {
                if (currentCol > 1)
                {
                    currentRow++;
                    currentCol = 1;
                }
                inTableRow = true;
            }

            // Try to extract control
            var control = TryExtractControl(elem);
            if (control != null)
            {
                // Check for duplicate controls by CtrlId
                if (control.Properties != null && control.Properties.ContainsKey("CtrlId"))
                {
                    var ctrlId = control.Properties["CtrlId"];
                    if (!string.IsNullOrEmpty(ctrlId) && controlsById.ContainsKey(ctrlId))
                    {
                        Debug.WriteLine($"Control with CtrlId {ctrlId} already processed, skipping duplicate");
                        return;
                    }
                }

                // Apply context (section/repeating information)
                ApplyControlContext(control);

                // Set grid position based on current context
                control.GridPosition = GetCurrentGridPosition();
                control.DocIndex = ++docIndexCounter;

                // Track control by ID if it has one
                if (control.Properties != null && control.Properties.ContainsKey("CtrlId"))
                {
                    var ctrlId = control.Properties["CtrlId"];
                    if (!string.IsNullOrEmpty(ctrlId))
                    {
                        controlsById[ctrlId] = control;
                    }
                }

                // Increment column for next control
                currentCol++;

                // Check if we need to wrap to next row (e.g., after certain number of columns)
                if (currentCol > 10) // Adjust this threshold as needed
                {
                    currentRow++;
                    currentCol = 1;
                }

                allControls.Add(control);

                // Don't process children if we've extracted a control
                return;
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
                if (currentCol > 1)
                {
                    currentRow++;
                    currentCol = 1;
                }
            }
        }

        private string GetCurrentGridPosition()
        {
            // Maintain grid position relative to current context
            // Don't reset for nested sections
            return currentRow + GetColumnLetter(currentCol);
        }



        private void ApplyControlContext(ControlDefinition control)
        {
            if (repeatingContextStack != null && repeatingContextStack.Count > 0)
            {
                var currentRepeating = repeatingContextStack.Peek();

                control.IsInRepeatingSection = true;
                control.RepeatingSectionName = currentRepeating.DisplayName;
                control.RepeatingSectionBinding = currentRepeating.Binding;

                // Track nesting depth for proper context
                if (repeatingContextStack.Count > 1)
                {
                    // Build the full path for nested sections
                    var sectionPath = string.Join("/",
                        repeatingContextStack.Reverse().Select(r => r.DisplayName));
                    control.Properties["FullSectionPath"] = sectionPath;

                    // Store immediate parent
                    var parentContext = repeatingContextStack.ElementAt(1);
                    control.Properties["ParentRepeatingSectionName"] = parentContext.DisplayName;
                }
            }
        }

        private void CreateRepeatingSection(string name, string binding, string type)
        {
            // Simple, consistent naming
            var sectionName = GetSimpleSectionName(name, binding);

            // Make unique within this view
            sectionName = EnsureUniqueInView(sectionName);

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
        private string GetSimpleSectionName(string rawName, string binding = null)
        {
            // Don't duplicate parent section names in nested contexts
            if (repeatingContextStack.Count > 0)
            {
                var parentName = repeatingContextStack.Peek().DisplayName;
                if (!string.IsNullOrEmpty(rawName) && rawName.StartsWith(parentName))
                {
                    // Remove parent prefix to avoid duplication
                    rawName = rawName.Substring(parentName.Length).TrimStart('_');
                }
            }

            if (!string.IsNullOrEmpty(rawName) &&
                !rawName.StartsWith("CTRL") &&
                !rawName.Equals("RepeatingSection") &&
                !rawName.Equals("RepeatingTable"))
            {
                return CleanSectionName(rawName);
            }

            if (!string.IsNullOrEmpty(binding))
            {
                var parts = binding.Split('/');
                var lastPart = parts.Last().Split(':').Last();
                if (!string.IsNullOrEmpty(lastPart))
                {
                    return char.ToUpper(lastPart[0]) + lastPart.Substring(1);
                }
            }

            return "RepeatingSection";
        }

        private string CleanSectionName(string name)
        {
            // Remove special characters
            name = Regex.Replace(name, @"[^\w\s]", " ");
            // Collapse multiple spaces
            name = Regex.Replace(name, @"\s+", " ").Trim();
            // Replace spaces with underscores for consistency
            name = name.Replace(" ", "_");
            return name;
        }


        private string EnsureUniqueInView(string baseName)
        {
            if (!viewRepeatingSectionNames.Contains(baseName))
            {
                viewRepeatingSectionNames.Add(baseName);
                return baseName;
            }

            int counter = 2;
            while (viewRepeatingSectionNames.Contains($"{baseName}_{counter}"))
                counter++;

            var uniqueName = $"{baseName}_{counter}";
            viewRepeatingSectionNames.Add(uniqueName);
            return uniqueName;
        }

        private void ProcessRepeatingSection(XElement elem)
        {
            var className = elem.Attribute("class")?.Value ?? "";
            var ctrlId = GetAttributeValue(elem, "CtrlId");
            var binding = GetAttributeValue(elem, "binding");

            // Look for apply-templates to determine the actual repeating structure
            var applyTemplates = elem.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "apply-templates" &&
                                     e.Attribute("select") != null);

            if (applyTemplates != null)
            {
                var selectValue = applyTemplates.Attribute("select")?.Value;
                binding = selectValue;
            }

            var sectionName = ExtractNameFromBinding(binding);
            CreateRepeatingSection(sectionName, binding, "section");

            // IMPORTANT: Don't reset grid position when entering repeating section
            // Just continue with current position

            foreach (var child in elem.Elements())
                ProcessElement(child);

            var context = repeatingContextStack.Pop();
            var sectionInfo = sections.LastOrDefault(s => s.Name == context.DisplayName);
            if (sectionInfo != null)
                sectionInfo.EndRow = currentRow;
        }

        private string ExtractTableName(XElement elem, string ctrlId)
        {
            // 1. Check for caption attribute
            var caption = GetAttributeValue(elem, "caption");
            if (!string.IsNullOrEmpty(caption) && !caption.StartsWith("CTRL"))
            {
                return CleanTableName(caption);
            }

            // 2. Check for title attribute  
            var title = GetAttributeValue(elem, "title");
            if (!string.IsNullOrEmpty(title) && !title.StartsWith("CTRL"))
            {
                return CleanTableName(title);
            }

            // 3. Try to extract from table headers
            var headerName = ExtractNameFromTableHeaders(elem);
            if (!string.IsNullOrEmpty(headerName))
            {
                return CleanTableName(headerName);
            }

            // 4. Try binding path
            var binding = GetTableBinding(elem);
            if (!string.IsNullOrEmpty(binding))
            {
                var name = ExtractNameFromBinding(binding);
                if (!string.IsNullOrEmpty(name) && name != "RepeatingSection")
                {
                    return CleanTableName(name);
                }
            }

            // 5. Default to Table_CTRL{id}
            return !string.IsNullOrEmpty(ctrlId) ? $"Table_{ctrlId}" : "RepeatingTable";
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

        private string CleanTableName(string name)
        {
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

            return name;
        }






        private void ProcessRepeatingTable(XElement elem)
        {
            var ctrlId = GetAttributeValue(elem, "CtrlId");

            // Skip if already processed
            if (!string.IsNullOrEmpty(ctrlId) && processedControls.Contains(ctrlId))
                return;

            // Extract meaningful table name
            var tableName = ExtractTableName(elem, ctrlId);
            var binding = GetTableBinding(elem);

            // Create the table control itself (not as a section member)
            var tableControl = new ControlDefinition
            {
                Name = RemoveSpaces(tableName),
                Type = "RepeatingTable",
                Label = tableName,
                Binding = binding,
                DocIndex = ++docIndexCounter,
                GridPosition = currentRow + GetColumnLetter(currentCol)
            };

            // Check if this table is nested in another repeating section
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

            // Move to next row for table contents
            currentRow++;
            currentCol = 1;

            // Create repeating context for the table's contents
            CreateRepeatingSection(tableName, binding, "table");

            // Process table headers
            var thead = elem.Elements().FirstOrDefault(e => e.Name.LocalName == "thead");
            if (thead != null)
            {
                foreach (var row in thead.Elements().Where(e => e.Name.LocalName == "tr"))
                {
                    currentCol = 1;
                    foreach (var cell in row.Elements().Where(e => e.Name.LocalName == "td" || e.Name.LocalName == "th"))
                    {
                        // Process cell contents
                        foreach (var cellChild in cell.Elements())
                        {
                            ProcessElement(cellChild);
                        }
                        currentCol++;
                    }
                    currentRow++;
                    currentCol = 1;
                }
            }

            // Process table body
            var tbody = elem.Elements().FirstOrDefault(e => e.Name.LocalName == "tbody");
            if (tbody != null)
            {
                // Look for xsl:for-each which indicates the repeating rows
                var forEach = tbody.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "for-each");

                if (forEach != null)
                {
                    // Process the template rows
                    foreach (var row in forEach.Elements().Where(e => e.Name.LocalName == "tr"))
                    {
                        currentCol = 1;
                        foreach (var cell in row.Elements().Where(e => e.Name.LocalName == "td"))
                        {
                            foreach (var cellChild in cell.Elements())
                            {
                                ProcessElement(cellChild);
                            }
                            currentCol++;
                        }
                        currentRow++;
                        currentCol = 1;
                    }
                }
            }

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
        }

        private void ProcessTableStructure(XElement tableElem)
        {
            // Process header
            var thead = tableElem.Elements().FirstOrDefault(e => e.Name.LocalName == "thead");
            if (thead != null)
            {
                foreach (var row in thead.Elements().Where(e => e.Name.LocalName == "tr"))
                {
                    currentCol = 1;
                    foreach (var cell in row.Elements().Where(e => e.Name.LocalName == "td" || e.Name.LocalName == "th"))
                    {
                        ProcessTableCell(cell);
                        currentCol++;
                    }
                    currentRow++;
                    currentCol = 1;
                }
            }

            // Process body
            var tbody = tableElem.Elements().FirstOrDefault(e => e.Name.LocalName == "tbody");
            if (tbody != null)
            {
                // Check for xsl:for-each (the repeating row template)
                var forEach = tbody.Elements().FirstOrDefault(e => e.Name.LocalName == "for-each");
                if (forEach != null)
                {
                    foreach (var row in forEach.Elements().Where(e => e.Name.LocalName == "tr"))
                    {
                        currentCol = 1;
                        foreach (var cell in row.Elements().Where(e => e.Name.LocalName == "td"))
                        {
                            ProcessTableCell(cell);
                            currentCol++;
                        }
                        currentRow++;
                        currentCol = 1;
                    }
                }

                // Process any direct rows (non-repeating parts)
                foreach (var row in tbody.Elements().Where(e => e.Name.LocalName == "tr"))
                {
                    currentCol = 1;
                    foreach (var cell in row.Elements().Where(e => e.Name.LocalName == "td"))
                    {
                        ProcessTableCell(cell);
                        currentCol++;
                    }
                    currentRow++;
                    currentCol = 1;
                }
            }
        }

        private void ProcessTableCell(XElement cell)
        {
            foreach (var child in cell.Elements())
            {
                ProcessElement(child);
            }
        }


        private string GetTableName(XElement elem, string ctrlId)
        {
            // Try various sources for table name
            var caption = GetAttributeValue(elem, "caption");
            if (!string.IsNullOrEmpty(caption))
                return CleanSectionName(caption);

            var title = GetAttributeValue(elem, "title");
            if (!string.IsNullOrEmpty(title))
                return CleanSectionName(title);

            // Try to extract from headers
            var headerName = ExtractTableNameFromHeaders(elem);
            if (!string.IsNullOrEmpty(headerName))
                return CleanSectionName(headerName);

            // Try from binding
            var binding = GetTableBinding(elem);
            if (!string.IsNullOrEmpty(binding))
            {
                var name = ExtractNameFromBinding(binding);
                if (!string.IsNullOrEmpty(name) && name != "RepeatingSection")
                    return name;
            }

            // Default name
            return !string.IsNullOrEmpty(ctrlId) ? $"Table_{ctrlId}" : "RepeatingTable";
        }




        private string ExtractTableNameFromHeaders(XElement tableElem)
        {
            var headers = new List<string>();

            // Look for th elements or td elements in thead
            var headerCells = tableElem.Descendants()
                .Where(e => e.Name.LocalName == "th" ||
                           (e.Name.LocalName == "td" &&
                            e.Ancestors().Any(a => a.Name.LocalName == "thead")))
                .ToList();

            foreach (var cell in headerCells)
            {
                var text = GetDirectTextContent(cell).Trim();
                if (!string.IsNullOrWhiteSpace(text) && text.Length > 1 && text.Length < 50)
                {
                    headers.Add(text);
                }
            }

            if (headers.Any())
            {
                // Use first meaningful header or combination
                if (headers.Count == 1)
                {
                    return headers[0];
                }
                else if (headers.Count >= 2)
                {
                    // Combine first two headers
                    return $"{headers[0]}_{headers[1]}";
                }
            }

            return "";
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

        private string ExtractSectionNameFromSelect(string select)
        {
            if (string.IsNullOrEmpty(select))
                return "RepeatingSection";

            // Split by '/' and get the parent element (collection name)
            var parts = select.Split('/');
            string relevantPart = "";

            if (parts.Length >= 2)
            {
                // Use the parent (collection) name
                relevantPart = GetElementName(parts[parts.Length - 2]);
            }
            else if (parts.Length == 1)
            {
                relevantPart = GetElementName(parts[0]);
            }

            // Convert to readable format
            if (!string.IsNullOrEmpty(relevantPart))
            {
                // Capitalize first letter
                relevantPart = char.ToUpper(relevantPart[0]) + relevantPart.Substring(1);

                // Insert spaces before capitals (camelCase to Title Case)
                relevantPart = System.Text.RegularExpressions.Regex.Replace(
                    relevantPart,
                    @"([a-z])([A-Z])",
                    "$1 $2");
            }

            return string.IsNullOrEmpty(relevantPart) ? "RepeatingSection" : relevantPart;
        }



        private string EnsureUniqueRepeatingSectionName(string baseName, string binding = null)
        {
            // viewRepeatingSectionNames is already a HashSet<string>, not a Dictionary
            if (viewRepeatingSectionNames == null)
                viewRepeatingSectionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // If the base name hasn't been used, use it
            if (!viewRepeatingSectionNames.Contains(baseName))
            {
                viewRepeatingSectionNames.Add(baseName);
                return baseName;
            }

            // Name exists, need to make it unique
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

            // Check if it has a section div with a CtrlId
            var hasSectionDiv = template.Descendants()
                .Any(e => GetAttributeValue(e, "CtrlId") != null &&
                          (GetAttributeValue(e, "xctname") == "Section" ||
                           e.Attribute("class")?.Value?.Contains("xdSection") == true));

            // If it has conditional logic AND a section div, it's a conditional section template
            return hasConditional && hasSectionDiv;
        }


        private void ProcessRepeatingApplyTemplates(XElement elem, string mode, string select)
        {
            Debug.WriteLine($"Processing repeating apply-templates - mode: {mode}, select: {select}");

            // Extract section name from the select pattern
            string sectionName = ExtractSectionNameFromSelect(select);

            // Check if this mode represents a conditional section rather than a true repeating section
            if (IsConditionalTemplateMode(mode, elem.Document))
            {
                Debug.WriteLine($"Mode {mode} is for a conditional section, not creating repeating context");

                // Just process the template without creating a repeating context
                var matchingTemplate = FindTemplate(elem.Document, mode);
                if (matchingTemplate != null)
                {
                    ProcessXslTemplate(matchingTemplate);
                }
                return;
            }

            // Ensure unique name for the section
            sectionName = EnsureUniqueRepeatingSectionName(sectionName, select);

            // Check if we're already in this repeating context
            bool alreadyInContext = repeatingContextStack.Any(r =>
                r.Binding == select || r.Name == sectionName);

            if (!alreadyInContext)
            {
                // Create repeating context
                var repeatingContext = new RepeatingContext
                {
                    Name = sectionName,
                    Binding = select,
                    Type = "section",
                    DisplayName = sectionName,
                    Depth = repeatingContextStack.Count
                };

                repeatingContextStack.Push(repeatingContext);

                // Create section info with current row
                var sectionInfo = new SectionInfo
                {
                    Name = sectionName,
                    Type = "repeating",
                    CtrlId = "",
                    StartRow = currentRow
                };
                sections.Add(sectionInfo);
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
                    // Process all children of the template
                    // IMPORTANT: Don't reset grid position here
                    foreach (var child in matchingTemplate.Elements())
                    {
                        ProcessElement(child);
                    }
                }
            }

            if (!alreadyInContext)
            {
                repeatingContextStack.Pop();
                var sectionInfo = sections.LastOrDefault(s => s.Name == sectionName);
                if (sectionInfo != null)
                    sectionInfo.EndRow = currentRow;
            }
        }

        private void ProcessXslTemplate(XElement templateElem)
        {
            var mode = templateElem.Attribute("mode")?.Value;

            // Mark this template mode as fully processed
            if (!string.IsNullOrEmpty(mode))
            {
                if (processedTemplateModes.Contains(mode))
                {
                    Debug.WriteLine($"Template mode {mode} already fully processed, skipping");
                    return;
                }
                processedTemplateModes.Add(mode);
            }

            // Process all children once
            foreach (var child in templateElem.Elements())
            {
                ProcessElement(child);
            }
        }

        private bool IsRepeatingSection(XElement elem)
        {
            var className = elem.Attribute("class")?.Value ?? "";

            if (className.Contains("xdRepeatingSection") && className.Contains("xdRepeating"))
                return true;

            var xctname = GetAttributeValue(elem, "xctname");
            if (xctname == "RepeatingSection")
                return true;

            return false;
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

        private bool IsRepeatingSelectPattern(string select)
        {
            if (string.IsNullOrEmpty(select))
                return false;

            // Check for path patterns that indicate repetition
            if (select.Contains("/"))
            {
                var parts = select.Split('/');
                if (parts.Length >= 2)
                {
                    var parent = GetElementName(parts[parts.Length - 2]);
                    var child = GetElementName(parts[parts.Length - 1]);

                    if (IsCollectionChildPattern(parent, child))
                    {
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

            // Pattern: Plural parent with singular child
            if (parent.EndsWith("s") && parent.Substring(0, parent.Length - 1) == child)
                return true;

            // Pattern: Collection suffixes
            string[] collectionSuffixes = { "list", "collection", "array", "set", "items", "entries" };
            foreach (var suffix in collectionSuffixes)
            {
                if (parent.EndsWith(suffix) && parent.StartsWith(child))
                    return true;
            }

            // Pattern: Same name (sometimes used for collections)
            if (parent == child)
                return true;

            return false;
        }

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

                return template;
            }
            catch
            {
                return null;
            }
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

        private ControlDefinition TryExtractControl(XElement elem)
        {
            var elemName = elem.Name.LocalName.ToLower();

            // Skip if this is a table (should be handled by ProcessRepeatingTable)
            if (elemName == "table")
                return null;

            // Skip if this has already been processed as a repeating structure
            var ctrlId = GetAttributeValue(elem, "CtrlId");
            if (!string.IsNullOrEmpty(ctrlId) && processedControls.Contains(ctrlId))
                return null;

            // Try to extract label
            if (IsLabelElement(elem))
            {
                var labelText = ExtractLabelText(elem);
                if (!string.IsNullOrWhiteSpace(labelText) && labelText.Length > 1)
                {
                    var labelControl = new ControlDefinition
                    {
                        Name = GenerateControlName(elem, labelText, "", "Label"),
                        Type = "Label",
                        Label = labelText,
                        Binding = "",
                        DocIndex = ++docIndexCounter
                    };

                    return labelControl;
                }
            }

            // Try to extract control with xctname
            var xctAttr = GetAttributeValue(elem, "xctname");
            if (!string.IsNullOrEmpty(xctAttr) &&
                !xctAttr.Equals("ExpressionBox", StringComparison.OrdinalIgnoreCase) &&
                !xctAttr.Equals("Section", StringComparison.OrdinalIgnoreCase) &&
                !xctAttr.Equals("RepeatingSection", StringComparison.OrdinalIgnoreCase) &&
                !xctAttr.Equals("RepeatingTable", StringComparison.OrdinalIgnoreCase) &&
                !xctAttr.Equals("repeatingtable", StringComparison.OrdinalIgnoreCase)) // Add lowercase check
            {
                return ParseXctControl(elem, xctAttr);
            }

            // Try to extract HTML form control
            if (elemName == "input" || elemName == "select" || elemName == "textarea")
            {
                return ParseHtmlControl(elem);
            }

            // Try to extract ActiveX control
            if (elemName == "object")
            {
                return ParseActiveXControl(elem);
            }

            // Check for controls with xd:binding attribute but no xctname
            var bindingAttr = GetAttributeValue(elem, "binding");
            if (!string.IsNullOrEmpty(bindingAttr))
            {
                return ParseGenericBoundControl(elem);
            }

            return null;
        }

        private ControlDefinition ParseXctControl(XElement elem, string xctType)
        {
            var mappedType = MapControlType(xctType);

            var control = new ControlDefinition
            {
                Type = mappedType,
                DocIndex = ++docIndexCounter
            };

            control.Label = elem.Attribute("title")?.Value ?? "";
            control.Binding = GetAttributeValue(elem, "binding");
            control.Name = GenerateControlName(elem, control.Label, control.Binding, mappedType);

            var ctrlId = GetAttributeValue(elem, "CtrlId");
            if (!string.IsNullOrEmpty(ctrlId))
            {
                if (processedControls.Contains(ctrlId))
                    return null;
                processedControls.Add(ctrlId);
                control.Properties["CtrlId"] = ctrlId;
            }

            // Handle dropdown options extraction
            if (mappedType.Equals("DropDown", StringComparison.OrdinalIgnoreCase) ||
                mappedType.Equals("ComboBox", StringComparison.OrdinalIgnoreCase))
            {
                ExtractDropdownOptions(elem, control);
            }

            // Copy attributes
            foreach (var attr in elem.Attributes())
            {
                if (!ShouldSkipAttribute(attr.Name.LocalName))
                {
                    control.Properties[attr.Name.LocalName] = attr.Value;
                }
            }

            return control;
        }

        private ControlDefinition ParseHtmlControl(XElement elem)
        {
            var control = new ControlDefinition
            {
                DocIndex = ++docIndexCounter
            };

            var xctname = GetAttributeValue(elem, "xctname");
            if (!string.IsNullOrEmpty(xctname))
            {
                control.Type = MapControlType(xctname);
                if (xctname.Equals("dropdown", StringComparison.OrdinalIgnoreCase))
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
                }
            }

            control.Label = elem.Attribute("title")?.Value ?? "";
            control.Binding = GetAttributeValue(elem, "binding");
            control.Name = GenerateControlName(elem, control.Label, control.Binding, control.Type);

            var ctrlId = GetAttributeValue(elem, "CtrlId");
            if (!string.IsNullOrEmpty(ctrlId))
            {
                if (processedControls.Contains(ctrlId))
                    return null;
                processedControls.Add(ctrlId);
                control.Properties["CtrlId"] = ctrlId;
            }

            foreach (var attr in elem.Attributes())
            {
                if (!ShouldSkipAttribute(attr.Name.LocalName))
                {
                    control.Properties[attr.Name.LocalName] = attr.Value;
                }
            }

            return control;
        }

        private ControlDefinition ParseActiveXControl(XElement elem)
        {
            var control = new ControlDefinition
            {
                DocIndex = ++docIndexCounter
            };

            var xctname = GetAttributeValue(elem, "xctname");
            if (!string.IsNullOrEmpty(xctname))
            {
                if (xctname.Contains("61e40d31-993d-4777-8fa0-19ca59b6d0bb"))
                {
                    control.Type = "PeoplePicker";
                }
                else
                {
                    control.Type = "ActiveX";
                }
            }
            else
            {
                control.Type = "ActiveX";
            }

            var ctrlIdAttr = GetAttributeValue(elem, "CtrlId");
            if (!string.IsNullOrEmpty(ctrlIdAttr))
            {
                if (processedControls.Contains(ctrlIdAttr))
                    return null;
                processedControls.Add(ctrlIdAttr);
                control.Properties["CtrlId"] = ctrlIdAttr;
            }

            control.Binding = GetAttributeValue(elem, "binding");
            control.Label = elem.Attribute("title")?.Value ?? "";
            control.Name = GenerateControlName(elem, control.Label, control.Binding, control.Type);

            foreach (var attr in elem.Attributes())
            {
                if (!ShouldSkipAttribute(attr.Name.LocalName))
                {
                    control.Properties[attr.Name.LocalName] = attr.Value;
                }
            }

            return control;
        }

        private ControlDefinition ParseGenericBoundControl(XElement elem)
        {
            var control = new ControlDefinition
            {
                DocIndex = ++docIndexCounter
            };

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
            else if (className.Contains("xdRichTextBox"))
            {
                control.Type = "RichText";
            }
            else
            {
                control.Type = elemName;
            }

            control.Binding = GetAttributeValue(elem, "binding");
            control.Label = elem.Attribute("title")?.Value ?? "";
            control.Name = GenerateControlName(elem, control.Label, control.Binding, control.Type);

            var ctrlId = GetAttributeValue(elem, "CtrlId");
            if (!string.IsNullOrEmpty(ctrlId))
            {
                if (processedControls.Contains(ctrlId))
                    return null;
                processedControls.Add(ctrlId);
                control.Properties["CtrlId"] = ctrlId;
            }

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

            // Priority 2: Use title/label if available and meaningful
            if (!string.IsNullOrWhiteSpace(label) && label.Length > 1)
            {
                var cleanLabel = label.TrimEnd(':').Trim();
                if (!string.IsNullOrWhiteSpace(cleanLabel))
                {
                    return RemoveSpaces(cleanLabel).ToUpper();
                }
            }

            // Priority 3: Extract from binding path
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
                    return RemoveSpaces(lastPart).ToUpper();
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

        private void ExtractDropdownOptions(XElement elem, ControlDefinition control)
        {
            try
            {
                var options = elem.Descendants()
                    .Where(e => e.Name.LocalName.Equals("option", StringComparison.OrdinalIgnoreCase))
                    .ToList();

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

                    control.Properties["DataValues"] = control.DataOptionsString;
                }
            }
            catch
            {
                // Ignore extraction errors
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
                    if (!string.IsNullOrEmpty(nodeText))
                    {
                        text = nodeText;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(text))
                text = option.Attribute("value")?.Value ?? "";

            return text;
        }

        private bool IsLabelElement(XElement elem)
        {
            var elemName = elem.Name.LocalName.ToLower();

            if (elemName == "strong" || elemName == "font" || elemName == "label" || elemName == "em")
            {
                if (!elem.Descendants().Any(d =>
                    !string.IsNullOrEmpty(GetAttributeValue(d, "xctname")) ||
                    d.Name.LocalName.Equals("object", StringComparison.OrdinalIgnoreCase) ||
                    d.Name.LocalName.Equals("input", StringComparison.OrdinalIgnoreCase) ||
                    d.Name.LocalName.Equals("select", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }

        private string ExtractLabelText(XElement elem)
        {
            var text = GetDirectTextContent(elem).Trim();
            text = Regex.Replace(text, @"\s+", " ");
            return text;
        }

        private string GetDirectTextContent(XElement elem)
        {
            var sb = new StringBuilder();

            foreach (var node in elem.Nodes())
            {
                if (node is XText textNode)
                {
                    sb.Append(textNode.Value);
                }
                else if (node is XElement childElem)
                {
                    var childName = childElem.Name.LocalName.ToLower();
                    if (childName is "strong" or "em" or "font" or "span" or "b" or "i")
                        sb.Append(GetDirectTextContent(childElem));
                }
            }

            return sb.ToString();
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
                { "repeatingtable", "RepeatingTable" },
                { "datepicker", "DatePicker" }
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
            for (int i = 0; i < controls.Count - 1; i++)
            {
                var current = controls[i];
                var next = controls[i + 1];

                if (current.Type == "Label" && next.Type == "Label")
                {
                    if (AreLabelsRelated(current, next))
                    {
                        current.Label = current.Label + " " + next.Label;
                        current.IsMultiLineLabel = true;
                        next.IsMergedIntoParent = true;
                    }
                }
            }
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

    #endregion
}