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
        public List<ViewDefinition> Views { get; set; } = new List<ViewDefinition>();
        public List<FormRule> Rules { get; set; } = new List<FormRule>();
        public List<DataColumn> Data { get; set; } = new List<DataColumn>();
        public List<DynamicSection> DynamicSections { get; set; } = new List<DynamicSection>();
        public Dictionary<string, List<string>> ConditionalVisibility { get; set; } = new Dictionary<string, List<string>>();
        public FormMetadata Metadata { get; set; } = new FormMetadata();
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
        public string DataOptionsString { get; set; } // Comma-separated for simple display
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

                string repeating = ctrl.ParentSection;
                var key = (colName, repeating);

                if (!dict.ContainsKey(key))
                {
                    var dataCol = new DataColumn
                    {
                        ColumnName = colName,
                        Type = ctrl.Type,
                        RepeatingSection = repeating,
                        IsRepeating = ctrl.SectionType == "repeating" || ctrl.IsInRepeatingSection,
                        RepeatingSectionPath = repeating,
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
            public string Type { get; set; } // "table" or "section"
            public string DisplayName { get; set; }
            public int Depth { get; set; }
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

            var container = radioElement.Parent;
            while (container != null &&
                   !container.Name.LocalName.Equals("div", StringComparison.OrdinalIgnoreCase) &&
                   !container.Name.LocalName.Equals("td", StringComparison.OrdinalIgnoreCase))
            {
                container = container.Parent;
            }

            if (container == null)
                container = radioElement.Parent;

            return container.Descendants()
                .Where(e => e.Name.LocalName.Equals("input", StringComparison.OrdinalIgnoreCase) &&
                           e.Attribute("type")?.Value?.Equals("radio", StringComparison.OrdinalIgnoreCase) == true &&
                           e.Attribute("name")?.Value == name)
                .ToList();
        }

        private string GetRadioButtonLabel(XElement radio)
        {
            var id = radio.Attribute("id")?.Value;
            if (!string.IsNullOrEmpty(id))
            {
                var label = radio.Parent?.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName.Equals("label", StringComparison.OrdinalIgnoreCase) &&
                                       e.Attribute("for")?.Value == id);
                if (label != null)
                    return label.Value?.Trim() ?? "";
            }

            var nextNode = radio.NextNode;
            while (nextNode != null)
            {
                if (nextNode is XText textNode && !string.IsNullOrWhiteSpace(textNode.Value))
                    return textNode.Value.Trim();

                if (nextNode is XElement elem && elem.Name.LocalName.Equals("label", StringComparison.OrdinalIgnoreCase))
                    return elem.Value?.Trim() ?? "";

                nextNode = nextNode.NextNode;
            }

            return radio.Attribute("value")?.Value ?? "";
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

        public List<ControlDefinition> ParseViewFile(string viewFile)
        {
            ResetParserState();
            XDocument doc = XDocument.Load(viewFile);
            ProcessElement(doc.Root);
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
                ProcessRepeatingSection(elem);
                return;
            }

            // Handle XSL templates
            if (elemName == "template" && elem.Attribute("mode") != null)
            {
                ProcessXslTemplate(elem);
                return;
            }

            // Handle apply-templates with improved duplicate prevention
            if (elemName == "apply-templates")
            {
                var mode = elem.Attribute("mode")?.Value;
                var select = elem.Attribute("select")?.Value;

                if (!string.IsNullOrEmpty(mode))
                {
                    Debug.WriteLine($"Found apply-templates with mode: {mode}, select: {select}");

                    // Build context key for tracking
                    string contextKey = mode;
                    if (repeatingContextStack.Count > 0)
                    {
                        contextKey = $"{repeatingContextStack.Peek().Name}::{mode}";
                    }

                    // Check if this is a repeating pattern
                    bool isRepeatingPattern = !string.IsNullOrEmpty(select) &&
                                             IsRepeatingSelectPattern(select);

                    if (isRepeatingPattern)
                    {
                        // Check if we've already processed this repeating pattern
                        if (!processedTemplates.Contains($"repeat::{select}"))
                        {
                            processedTemplates.Add($"repeat::{select}");
                            ProcessRepeatingApplyTemplates(elem, mode, select);
                            processedTemplates.Remove($"repeat::{select}");
                        }
                    }
                    else
                    {
                        // Only process template if not already processed in this context
                        if (!processedTemplates.Contains(contextKey))
                        {
                            var matchingTemplate = FindTemplate(elem.Document, mode);
                            if (matchingTemplate != null)
                            {
                                ProcessXslTemplate(matchingTemplate);
                            }
                        }
                    }
                    return;
                }
            }

            // Check for standalone labels that might be section headers
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

            // Handle regular sections (non-repeating)
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
                // FIX: Check for duplicate controls by CtrlId
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

                // Set grid position
                control.GridPosition = currentRow + GetColumnLetter(currentCol);
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

                currentCol++;
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
            }
        }
        private XElement FindTemplate(XDocument doc, string mode)
        {
            if (doc == null || string.IsNullOrEmpty(mode))
                return null;

            try
            {
                // Get the namespace from the root element
                var root = doc.Root;
                if (root == null)
                    return null;

                var ns = root.Name.Namespace;

                // Find template with matching mode attribute
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

            // Check for path patterns that indicate repetition
            // Pattern: namespace:parent/namespace:child (e.g., my:trips/my:trip)
            if (select.Contains("/"))
            {
                var parts = select.Split('/');
                if (parts.Length >= 2)
                {
                    // Extract the element names without namespace
                    var parent = GetElementName(parts[parts.Length - 2]);
                    var child = GetElementName(parts[parts.Length - 1]);

                    // Check if this looks like a collection pattern
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
            // Remove namespace prefix (e.g., "my:trips" -> "trips")
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

            // Pattern 1: Plural parent with singular child (trips/trip, items/item)
            if (parent.EndsWith("s") && parent.Substring(0, parent.Length - 1) == child)
                return true;

            // Pattern 2: Parent ends with "es" and child is singular (addresses/address)
            if (parent.EndsWith("es") && parent.Substring(0, parent.Length - 2) == child)
                return true;

            // Pattern 3: Parent ends with "ies" and child ends with "y" (categories/category)
            if (parent.EndsWith("ies") && child + "ies" == parent.Replace(parent.Substring(0, parent.Length - 3), child))
                return true;

            // Pattern 4: Collection suffixes (itemList/item, itemCollection/item)
            string[] collectionSuffixes = { "list", "collection", "array", "set", "items", "entries" };
            foreach (var suffix in collectionSuffixes)
            {
                if (parent.EndsWith(suffix) && parent.StartsWith(child))
                    return true;
            }

            // Pattern 5: Same name (sometimes used for collections)
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


        private void ProcessRepeatingApplyTemplates(XElement elem, string mode, string select)
        {
            Debug.WriteLine($"Processing repeating apply-templates - mode: {mode}, select: {select}");

            // Extract section name from the select pattern
            string sectionName = ExtractSectionNameFromSelect(select);

            // CRITICAL CHECK: Don't create a repeating section for templates that are actually conditionals
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

                // Create section info
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
                    // Process all children of the template to capture controls
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

        private void ApplyControlContext(ControlDefinition control)
        {
            // Apply repeating context if we're in one
            if (repeatingContextStack != null && repeatingContextStack.Count > 0)
            {
                var currentRepeating = repeatingContextStack.Peek();
                control.IsInRepeatingSection = true;
                control.RepeatingSectionName = currentRepeating.DisplayName;
                control.RepeatingSectionBinding = currentRepeating.Binding;

                if (repeatingContextStack.Count > 1)
                {
                    var parentContexts = repeatingContextStack.ToArray().Skip(1);
                    control.Properties["ParentRepeatingSections"] = string.Join("|", parentContexts.Select(c => c.DisplayName));
                }
            }

            // Apply section context (including conditional sections)
            if (sectionStack != null && sectionStack.Count > 0)
            {
                var currentSection = sectionStack.Peek();

                // If this is a conditional section within a repeating section
                if (currentSection.Type == "conditional-in-repeating")
                {
                    control.Properties["ConditionalSection"] = currentSection.DisplayName;
                    control.Properties["ConditionalSectionId"] = currentSection.CtrlId;

                    // Also maintain the parent section info
                    if (!string.IsNullOrEmpty(control.RepeatingSectionName))
                    {
                        control.ParentSection = $"{control.RepeatingSectionName} > {currentSection.DisplayName}";
                        control.SectionType = "conditional-in-repeating";
                    }
                }
                else if (repeatingContextStack.Count == 0)
                {
                    // Only apply regular section context if we're NOT in a repeating section
                    control.ParentSection = currentSection.DisplayName;
                    control.SectionType = currentSection.Type;

                    if (!string.IsNullOrEmpty(currentSection.CtrlId))
                        control.Properties["SectionCtrlId"] = currentSection.CtrlId;
                }
            }
        }
        private void ProcessXslTemplate(XElement templateElem)
        {
            var mode = templateElem.Attribute("mode")?.Value;
            var match = templateElem.Attribute("match")?.Value;

            Debug.WriteLine($"Processing XSL template - mode: {mode}, match: {match}");

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
                        Debug.WriteLine($"Template {mode} contains section {ctrlId} which already exists, skipping entire template");
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
                Debug.WriteLine($"Template {templateKey} already processed in this exact context, skipping");
                return;
            }

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
                    bool hasProcessedConditional = false;

                    foreach (var ifElement in ifElements)
                    {
                        var testCondition = ifElement.Attribute("test")?.Value;
                        Debug.WriteLine($"Found conditional in template with test: {testCondition}");

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
                                Debug.WriteLine($"Skipping conditional section {ctrlId} - belongs to repeating section but not in repeating context");
                                continue;
                            }

                            // If we ARE in a repeating section, only process if it's for our current repeating section
                            if (repeatingContextStack.Count > 0)
                            {
                                var currentRepeating = repeatingContextStack.Peek();
                                if (!IsConditionalForCurrentContext(testCondition, currentRepeating))
                                {
                                    Debug.WriteLine($"Skipping conditional section {ctrlId} - not for current repeating context");
                                    continue;
                                }
                            }

                            // Check if already processed
                            if (IsSectionAlreadyProcessed(ctrlId, mode))
                            {
                                Debug.WriteLine($"Section {ctrlId} already processed, skipping");
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
                        foreach (var child in templateElem.Elements())
                        {
                            ProcessElement(child);
                        }
                    }
                }
            }
            finally
            {
                insideXslTemplate = false;
                currentTemplateMode = previousTemplateMode;
                processedTemplates.Remove(templateKey);
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
                if (controlsById.ContainsKey(ctrlId))
                {
                    Debug.WriteLine($"Template would create duplicate control {ctrlId}, skipping");
                    return true;
                }
            }

            // Also check if this template is creating a fake repeating section
            // for what should be a conditional section
            var mode = templateElem.Attribute("mode")?.Value;
            if (mode == "_3" && repeatingContextStack.Count == 0)
            {
                // Mode _3 is the Round Trip template - it should only be processed
                // when we're inside the Trips repeating context
                Debug.WriteLine($"Template mode {mode} would create fake repeating section outside of proper context");
                return true;
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


        private bool ShouldProcessConditionalSection(XElement ifElement, string mode)
        {
            // Extract the section identifier from the conditional
            var testCondition = ifElement.Attribute("test")?.Value;
            var sectionDiv = ifElement.Descendants()
                .FirstOrDefault(e => GetAttributeValue(e, "xctname") == "Section" ||
                                    e.Attribute("class")?.Value?.Contains("xdSection") == true);

            if (sectionDiv != null)
            {
                var ctrlId = GetAttributeValue(sectionDiv, "CtrlId");

                // Check if we're in a repeating context
                if (repeatingContextStack.Count > 0)
                {
                    var parentRepeating = repeatingContextStack.Peek();

                    // Check if this section already exists within the current repeating context
                    var existingSection = sections.FirstOrDefault(s =>
                        s.CtrlId == ctrlId &&
                        s.Type == "conditional-in-repeating");

                    if (existingSection != null)
                    {
                        Debug.WriteLine($"Conditional section with CtrlId {ctrlId} already exists in repeating context");
                        return false;
                    }
                }
                else
                {
                    // Check for standalone conditional sections
                    var existingSection = sections.FirstOrDefault(s =>
                        s.CtrlId == ctrlId &&
                        s.Type == "conditional");

                    if (existingSection != null)
                    {
                        Debug.WriteLine($"Conditional section with CtrlId {ctrlId} already exists");
                        return false;
                    }
                }
            }

            return true;
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

        private void ProcessRepeatingTemplate(XElement templateElem, string match, string mode)
        {
            Debug.WriteLine($"Processing repeating template - match: {match}, mode: {mode}");

            var sectionName = ExtractNameFromBinding(match);

            // Check if we've already processed this as a repeating section
            // to avoid duplicates
            bool alreadyInThisRepeatingContext = repeatingContextStack.Any(r =>
                r.Binding == match || r.Name == sectionName);

            if (alreadyInThisRepeatingContext)
            {
                Debug.WriteLine($"Already in repeating context for {sectionName}, skipping duplicate");
                // Just process children without creating new context
                foreach (var child in templateElem.Elements())
                {
                    ProcessElement(child);
                }
                return;
            }

            var repeatingContext = new RepeatingContext
            {
                Name = sectionName,
                Binding = match,
                Type = "section",
                DisplayName = sectionName,
                Depth = repeatingContextStack.Count
            };

            repeatingContextStack.Push(repeatingContext);

            // Only create section info if not already created
            if (!sections.Any(s => s.Name == sectionName && s.Type == "repeating"))
            {
                var sectionInfo = new SectionInfo
                {
                    Name = sectionName,
                    Type = "repeating",
                    CtrlId = mode, // Use mode as CtrlId if no explicit ID
                    StartRow = currentRow
                };
                sections.Add(sectionInfo);
            }

            foreach (var child in templateElem.Elements())
            {
                ProcessElement(child);
            }

            repeatingContextStack.Pop();
        }

        private string DetermineSectionName(XElement ifElement, XElement sectionDiv,
      string caption, string ctrlId, string mode)
        {
            // Priority order for determining section name:
            // 1. Caption attribute
            if (!string.IsNullOrEmpty(caption))
                return caption;

            // 2. Try to extract from the condition
            var testCondition = ifElement?.Attribute("test")?.Value;
            if (!string.IsNullOrEmpty(testCondition))
            {
                // Extract field name from condition (e.g., "isRoundTrip" from the condition)
                var fieldMatch = System.Text.RegularExpressions.Regex.Match(
                    testCondition, @"my:(\w+)");
                if (fieldMatch.Success)
                {
                    var fieldName = fieldMatch.Groups[1].Value;
                    // Convert from camelCase to readable (e.g., "isRoundTrip" -> "Round Trip")
                    var readable = ConvertCamelCaseToReadable(fieldName);
                    if (!string.IsNullOrEmpty(readable))
                        return readable;
                }
            }

            // 3. Look for labels within the section
            var firstLabel = sectionDiv?.Descendants()
                .Where(e => e.Name.LocalName == "font" || e.Name.LocalName == "strong")
                .Select(e => e.Value)
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

            if (!string.IsNullOrEmpty(firstLabel))
                return firstLabel.Trim();

            // 4. Use CtrlId if available
            if (!string.IsNullOrEmpty(ctrlId))
                return $"Section_{ctrlId}";

            // 5. Use mode as last resort
            return $"Section_{mode}";
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
            var className = elem.Attribute("class")?.Value ?? "";
            var ctrlId = GetAttributeValue(elem, "CtrlId");

            Debug.WriteLine($"Processing REPEATING section - class: {className}, ctrlId: {ctrlId}");

            string sectionName = "RepeatingSection";
            var binding = GetAttributeValue(elem, "binding");

            // Look for apply-templates to determine the actual repeating structure
            var applyTemplates = elem.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "apply-templates" &&
                                     e.Attribute("select") != null);

            if (applyTemplates != null)
            {
                var selectValue = applyTemplates.Attribute("select")?.Value;

                // Check if this select pattern indicates actual repetition
                if (IsActuallyRepeatingPattern(selectValue, elem))
                {
                    binding = selectValue;
                    sectionName = ExtractNameFromBinding(binding);
                }
                else
                {
                    // This might be labeled as repeating but isn't actually
                    // Check the immediate context
                    Debug.WriteLine($"Section marked as repeating but pattern '{selectValue}' doesn't indicate repetition");

                    // If we're already in a repeating context, this is probably just a subsection
                    if (repeatingContextStack.Count > 0)
                    {
                        // Don't create a new repeating context, just process children
                        foreach (var child in elem.Elements())
                            ProcessElement(child);
                        return;
                    }
                }
            }

            // If we get here, treat it as a proper repeating section
            var bestLabel = FindBestLabelForSection(elem, sectionName);
            if (!string.IsNullOrEmpty(bestLabel))
                sectionName = bestLabel;

            var repeatingContext = new RepeatingContext
            {
                Name = sectionName,
                Binding = binding,
                Type = "section",
                DisplayName = sectionName,
                Depth = repeatingContextStack.Count
            };

            repeatingContextStack.Push(repeatingContext);

            var sectionInfo = new SectionInfo
            {
                Name = sectionName,
                Type = "repeating",
                CtrlId = ctrlId,
                StartRow = currentRow
            };
            sections.Add(sectionInfo);

            foreach (var child in elem.Elements())
                ProcessElement(child);

            repeatingContextStack.Pop();
            sectionInfo.EndRow = currentRow;
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
            var repeatingTable = ExtractRepeatingTableInfo(elem);

            var bestLabel = FindBestLabelForSection(elem, repeatingTable.Name);
            if (!string.IsNullOrEmpty(bestLabel))
            {
                repeatingTable.DisplayName = bestLabel;
                repeatingTable.Name = bestLabel;
            }
            else
            {
                repeatingTable.DisplayName = repeatingTable.Name;
            }

            repeatingTable.Depth = repeatingContextStack.Count;
            repeatingContextStack.Push(repeatingTable);

            var tableControl = new ControlDefinition
            {
                Name = RemoveSpaces(repeatingTable.DisplayName),
                Type = "RepeatingTable",
                Label = repeatingTable.DisplayName,
                Binding = repeatingTable.Binding,
                DocIndex = ++docIndexCounter,
                GridPosition = currentRow + GetColumnLetter(currentCol)
            };
            tableControl.Properties["TableType"] = "Repeating";
            tableControl.Properties["DisplayName"] = repeatingTable.DisplayName;

            var ctrlId = GetAttributeValue(elem, "CtrlId");
            if (!string.IsNullOrEmpty(ctrlId))
            {
                tableControl.Properties["CtrlId"] = ctrlId;
                controlsById[ctrlId] = tableControl;
            }

            allControls.Add(tableControl);
            currentRow++;
            currentCol = 1;

            foreach (var child in elem.Elements())
                ProcessElement(child);

            repeatingContextStack.Pop();
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

        private ControlDefinition TryExtractControl(XElement elem)
        {
            var elemName = elem.Name.LocalName.ToLower();

            if (IsRegularSection(elem) || IsRepeatingSection(elem))
                return null;

            if (IsLabelElement(elem))
            {
                var labelText = ExtractLabelText(elem);
                if (!string.IsNullOrWhiteSpace(labelText) && labelText.Length > 1)
                {
                    return new ControlDefinition
                    {
                        Name = RemoveSpaces(labelText),
                        Type = "Label",
                        Label = labelText,
                        Binding = "",
                        DocIndex = ++docIndexCounter
                    };
                }
            }

            var xctAttr = GetAttributeValue(elem, "xctname");
            if (!string.IsNullOrEmpty(xctAttr) &&
                !xctAttr.Equals("ExpressionBox", StringComparison.OrdinalIgnoreCase) &&
                !xctAttr.Equals("Section", StringComparison.OrdinalIgnoreCase) &&
                !xctAttr.Equals("RepeatingSection", StringComparison.OrdinalIgnoreCase) &&
                !xctAttr.Equals("RepeatingTable", StringComparison.OrdinalIgnoreCase))
            {
                return ParseXctControl(elem, xctAttr);
            }

            if (elemName == "input" || elemName == "select" || elemName == "textarea")
                return ParseHtmlControl(elem);

            if (elemName == "object")
                return ParseActiveXControl(elem);

            var bindingAttr = GetAttributeValue(elem, "binding");
            if (!string.IsNullOrEmpty(bindingAttr))
                return ParseGenericBoundControl(elem);

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

            var ctrlId = GetAttributeValue(elem, "CtrlId");
            if (!string.IsNullOrEmpty(ctrlId))
            {
                if (processedControls.Contains(ctrlId)) return null;
                processedControls.Add(ctrlId);
                control.Properties["CtrlId"] = ctrlId;
            }

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

            if (elem.Name.LocalName.ToLower() == "select" && (control.DataOptions == null || control.DataOptions.Count == 0))
                ExtractDropdownOptions(elem, control);

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

            var ctrlId = GetAttributeValue(elem, "CtrlId");
            if (!string.IsNullOrEmpty(ctrlId))
            {
                if (processedControls.Contains(ctrlId)) return null;
                processedControls.Add(ctrlId);
                control.Properties["CtrlId"] = ctrlId;
            }

            foreach (var attr in elem.Attributes())
            {
                if (!ShouldSkipAttribute(attr.Name.LocalName))
                    control.Properties[attr.Name.LocalName] = attr.Value;
            }

            return control;
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

            var ctrlId = GetAttributeValue(elem, "CtrlId");
            if (!string.IsNullOrEmpty(ctrlId))
            {
                if (processedControls.Contains(ctrlId)) return null;
                processedControls.Add(ctrlId);
                control.Properties["CtrlId"] = ctrlId;
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
