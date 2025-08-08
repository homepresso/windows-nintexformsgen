using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Text;
using System.Threading;
using FormGenerator.Analyzers.Infopath;

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
        public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();
        public List<ControlDefinition> Controls { get; set; } = new List<ControlDefinition>();
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
        private SectionAwareParser sectionParser = new SectionAwareParser();
        private LabelControlAssociator labelAssociator = new LabelControlAssociator();
        private DynamicSectionHandler dynamicHandler = new DynamicSectionHandler();
        private ComplexLabelHandler labelHandler = new ComplexLabelHandler();

        public InfoPathFormDefinition ParseXsnFile(string xsnFilePath)
        {
            string tempDir = Path.Combine(
                Path.GetTempPath(),
                Path.GetFileNameWithoutExtension(xsnFilePath) + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                Console.WriteLine("Extracting XSN to " + tempDir);

                // Extract using multiple methods
                bool extracted = ExtractXsn(xsnFilePath, tempDir);
                if (!extracted)
                {
                    throw new Exception("Failed to extract XSN file using all available methods.");
                }

                var formDef = new InfoPathFormDefinition();

                // Parse all view*.xsl files
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

                        // Extract dynamic sections
                        var xslDoc = XDocument.Load(vf);
                        var dynamicSections = dynamicHandler.ExtractDynamicSections(xslDoc);
                        formDef.DynamicSections.AddRange(dynamicSections);

                        formDef.Views.Add(singleView);
                    }
                }

                // Post-processing
                PostProcessFormDefinition(formDef);

                return formDef;
            }
            finally
            {
                // Cleanup
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
            // Method 1: Use Windows expand.exe for CAB files
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

            // Method 2: Try as ZIP
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

            // Method 3: Use PowerShell
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

            // Parse with enhanced parser
            var controls = sectionParser.ParseViewFile(viewFile);

            // Associate labels with controls
            labelAssociator.AssociateLabelControls(controls);

            // Handle complex labels
            labelHandler.ProcessMultiLineLabels(controls);

            // Extract section information
            viewDef.Sections = sectionParser.GetSections();

            viewDef.Controls = controls;
            return viewDef;
        }

        private void PostProcessFormDefinition(InfoPathFormDefinition formDef)
        {
            // Build conditional visibility map
            foreach (var dynSection in formDef.DynamicSections)
            {
                if (!formDef.ConditionalVisibility.ContainsKey(dynSection.ConditionField))
                {
                    formDef.ConditionalVisibility[dynSection.ConditionField] = new List<string>();
                }

                formDef.ConditionalVisibility[dynSection.ConditionField].AddRange(
                    dynSection.Controls);
            }

            // Build unique columns list with enhanced information
            var allCtrls = GetAllControlsFromAllViews(formDef);
            var dataCols = BuildEnhancedColumns(allCtrls, formDef);
            formDef.Data = dataCols;

            // Add form metadata
            AddFormMetadata(formDef);
        }

        private List<ControlDefinition> GetAllControlsFromAllViews(InfoPathFormDefinition formDef)
        {
            var all = new List<ControlDefinition>();
            foreach (var v in formDef.Views)
            {
                all.AddRange(v.Controls);
            }
            return all;
        }

        private List<DataColumn> BuildEnhancedColumns(List<ControlDefinition> allCtrls, InfoPathFormDefinition formDef)
        {
            var dict = new Dictionary<(string colName, string rep), DataColumn>();

            foreach (var ctrl in allCtrls)
            {
                // Skip labels and merged labels
                if (ctrl.Type == "Label" || ctrl.IsMergedIntoParent)
                    continue;

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
                        IsRepeating = ctrl.SectionType == "repeating",
                        RepeatingSectionPath = repeating,
                        DisplayName = ctrl.Label
                    };

                    // Check if conditional
                    var condField = formDef.ConditionalVisibility
                        .FirstOrDefault(cv => cv.Value.Contains(ctrl.Properties.GetValueOrDefault("CtrlId", "")));
                    if (!condField.Equals(default(KeyValuePair<string, List<string>>)))
                    {
                        dataCol.IsConditional = true;
                        dataCol.ConditionalOnField = condField.Key;
                    }

                    dict[key] = dataCol;
                }
            }

            return dict.Values.ToList();
        }

        private void AddFormMetadata(InfoPathFormDefinition formDef)
        {
            // Count all controls (excluding merged labels)
            var allControls = GetAllControlsFromAllViews(formDef);

            // Count repeating sections from multiple sources with debugging
            var repeatingSectionCount = 0;

            // 1. Count sections with type "repeating"
            var repeatingSections = formDef.Views
                .SelectMany(v => v.Sections)
                .Where(s => s.Type == "repeating")
                .ToList();
            repeatingSectionCount += repeatingSections.Count;

            Console.WriteLine($"Found {repeatingSections.Count} repeating sections:");
            foreach (var section in repeatingSections)
            {
                Console.WriteLine($"  - {section.Name} (Type: {section.Type})");
            }

            // 2. Count RepeatingTable controls
            var repeatingTables = allControls
                .Where(c => c.Type == "RepeatingTable")
                .ToList();
            repeatingSectionCount += repeatingTables.Count;

            Console.WriteLine($"Found {repeatingTables.Count} repeating tables:");
            foreach (var table in repeatingTables)
            {
                Console.WriteLine($"  - {table.Name} (Label: {table.Label})");
            }

            // 3. Count any other repeating sections that might be in controls
            var otherRepeating = allControls
                .Where(c => c.Type == "RepeatingSection")
                .ToList();
            repeatingSectionCount += otherRepeating.Count;

            Console.WriteLine($"Found {otherRepeating.Count} other repeating sections:");
            foreach (var other in otherRepeating)
            {
                Console.WriteLine($"  - {other.Name} (Label: {other.Label})");
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
}

#region Section-Aware Parser

public class SectionAwareParser
{
    private Stack<SectionContext> sectionStack = new Stack<SectionContext>();
    private List<SectionInfo> sections = new List<SectionInfo>();
    private int docIndexCounter = 0;
    private List<ControlDefinition> allControls = new List<ControlDefinition>();
    private int currentRow = 1;
    private int currentCol = 1;
    private bool inTableRow = false;
    private HashSet<string> processedControls = new HashSet<string>();
    private Dictionary<string, int> sectionRowCounters = new Dictionary<string, int>();
    private Stack<RepeatingContext> repeatingContextStack = new Stack<RepeatingContext>();

    // Track recent labels that might be section/table headers
    private Queue<LabelInfo> recentLabels = new Queue<LabelInfo>();
    private const int LABEL_LOOKBACK_COUNT = 5;

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

    public List<ControlDefinition> ParseViewFile(string viewFile)
    {
        allControls.Clear();
        sections.Clear();
        sectionStack.Clear();
        docIndexCounter = 0;
        currentRow = 1;
        currentCol = 1;
        repeatingContextStack.Clear();
        recentLabels.Clear();
        processedControls.Clear();
        sectionRowCounters.Clear();

        XDocument doc = XDocument.Load(viewFile);
        ProcessElement(doc.Root);

        return allControls;
    }

    public List<SectionInfo> GetSections()
    {
        return sections;
    }

    private void ProcessElement(XElement elem)
    {
        var elemName = elem.Name.LocalName.ToLower();

        // Track labels that might be section headers
        if (IsStandaloneLabelElement(elem))
        {
            TrackPotentialSectionLabel(elem);
        }

        // Check for repeating table BEFORE checking for sections
        if (IsRepeatingTable(elem))
        {
            ProcessRepeatingTable(elem);
            return;
        }

        // Check if entering a section
        if (IsSection(elem))
        {
            ProcessSection(elem);
        }

        // Check for new row indicators
        if (IsNewRowIndicator(elem))
        {
            HandleNewRow(elem);
        }

        // Skip processing placeholder text (like "Add item")
        if (IsPlaceholderText(elem))
        {
            // Process children but don't treat as control
            foreach (var child in elem.Elements())
            {
                ProcessElement(child);
            }
            return;
        }

        // Check if this element is a control or label
        var control = TryExtractControl(elem);
        if (control != null)
        {
            // Add repeating context information
            if (repeatingContextStack.Count > 0)
            {
                var currentContext = repeatingContextStack.Peek();
                control.IsInRepeatingSection = true;
                control.RepeatingSectionName = currentContext.DisplayName;
                control.RepeatingSectionBinding = currentContext.Binding;

                // Add parent repeating sections if nested
                if (repeatingContextStack.Count > 1)
                {
                    var parentContexts = repeatingContextStack.ToArray().Skip(1);
                    control.Properties["ParentRepeatingSections"] = string.Join("|",
                        parentContexts.Select(c => c.DisplayName));
                }
            }

            // Add section context
            // In InfoPathParser.cs, update the ProcessElement method where it sets control properties:

            // Around line 380 in ProcessElement method, update this section:
            if (sectionStack.Count > 0)
            {
                var currentSection = sectionStack.Peek();
                // Use DisplayName consistently for matching
                control.ParentSection = currentSection.DisplayName ?? currentSection.Name;
                control.SectionType = currentSection.Type;
                control.SectionGridPosition = $"{currentSection.Name}-{sectionRowCounters[currentSection.Name]}{GetColumnLetter(currentCol)}";

                // Find the section info by either Name or DisplayName
                var sectionInfo = sections.LastOrDefault(s =>
                    s.Name == currentSection.Name ||
                    s.Name == currentSection.DisplayName ||
                    s.Name == control.ParentSection);

                if (sectionInfo != null)
                {
                    sectionInfo.ControlIds.Add(control.Name);
                }
            }

            control.GridPosition = currentRow + GetColumnLetter(currentCol);
            control.DocIndex = ++docIndexCounter;

            // Check for spans
            var colspan = elem.Attribute("colspan")?.Value;
            var rowspan = elem.Attribute("rowspan")?.Value;
            if (colspan != null && int.TryParse(colspan, out int colSpan))
                control.ColumnSpan = colSpan;
            if (rowspan != null && int.TryParse(rowspan, out int rowSpan))
                control.RowSpan = rowSpan;

            currentCol++;
            allControls.Add(control);
            return;
        }

        // Process children
        foreach (var child in elem.Elements())
        {
            ProcessElement(child);
        }

        // Check if leaving a table row
        if (elemName == "tr")
        {
            inTableRow = false;
        }

        // Check if leaving a section
        if (IsSection(elem))
        {
            ExitSection();
        }
    }

    private bool IsStandaloneLabelElement(XElement elem)
    {
        // Check if this is a text element that might be a section label
        var elemName = elem.Name.LocalName.ToLower();

        // Look for strong, font, or div elements containing text
        if (elemName == "strong" || elemName == "font" || elemName == "div")
        {
            var text = ExtractLabelText(elem);
            if (!string.IsNullOrWhiteSpace(text) && text.Length > 2)
            {
                // Check if it's a potential section header (no trailing colon typically)
                if (!text.EndsWith(":"))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void TrackPotentialSectionLabel(XElement elem)
    {
        var text = ExtractLabelText(elem);
        if (!string.IsNullOrWhiteSpace(text))
        {
            // Skip placeholder text
            if (text.StartsWith("Add ", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("Insert ", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var labelInfo = new LabelInfo
            {
                Text = text.Trim(),
                DocIndex = docIndexCounter + 1,
                Row = currentRow,
                Col = currentCol,
                Used = false
            };

            recentLabels.Enqueue(labelInfo);

            // Keep only the most recent labels
            while (recentLabels.Count > LABEL_LOOKBACK_COUNT)
            {
                recentLabels.Dequeue();
            }
        }
    }

    private void ProcessRepeatingTable(XElement elem)
    {
        var repeatingTable = ExtractRepeatingTableInfo(elem);

        // Look for the best matching label from recent labels
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

        // Create a control for the repeating table
        var tableControl = new ControlDefinition
        {
            Name = RemoveSpaces(repeatingTable.DisplayName),
            Type = "RepeatingTable",
            Label = repeatingTable.DisplayName,
            Binding = repeatingTable.Binding,
            DocIndex = ++docIndexCounter,
            GridPosition = currentRow + GetColumnLetter(currentCol),
            Properties = new Dictionary<string, string>
            {
                ["TableType"] = "Repeating",
                ["DisplayName"] = repeatingTable.DisplayName
            },
            Controls = new List<ControlDefinition>()
        };

        // Copy attributes
        foreach (var attr in elem.Attributes())
        {
            if (!ShouldSkipAttribute(attr.Name.LocalName))
            {
                tableControl.Properties[attr.Name.LocalName] = attr.Value;
            }
        }

        allControls.Add(tableControl);
        currentRow++; // Move to next row after table header
        currentCol = 1;

        // Process children
        foreach (var child in elem.Elements())
        {
            ProcessElement(child);
        }

        // Exit context
        repeatingContextStack.Pop();
    }

    private void ProcessSection(XElement elem)
    {
        var section = ExtractSectionInfo(elem);

        // Look for a better name from recent labels
        var bestLabel = FindBestLabelForSection(elem, section.Name);
        if (!string.IsNullOrEmpty(bestLabel))
        {
            section.DisplayName = bestLabel;
        }
        else
        {
            section.DisplayName = section.Name;
        }

        sectionStack.Push(section);
        sectionRowCounters[section.Name] = 1;

        // Check if it's a repeating section
        if (section.Type == "repeating")
        {
            var repeatingContext = new RepeatingContext
            {
                Name = section.Name,
                Binding = section.Binding,
                Type = "section",
                DisplayName = section.DisplayName,
                Depth = repeatingContextStack.Count
            };
            repeatingContextStack.Push(repeatingContext);
        }

        var sectionInfo = new SectionInfo
        {
            Name = section.DisplayName,
            Type = section.Type,
            CtrlId = section.CtrlId,
            StartRow = currentRow
        };
        sections.Add(sectionInfo);
    }

    private string FindBestLabelForSection(XElement elem, string defaultName)
    {
        // Look through recent labels for the best match
        var unusedLabels = recentLabels.Where(l => !l.Used).ToList();

        // Filter out "Add" placeholder labels - these are not section headers
        unusedLabels = unusedLabels.Where(l =>
            !l.Text.StartsWith("Add ", StringComparison.OrdinalIgnoreCase) &&
            !l.Text.StartsWith("Insert ", StringComparison.OrdinalIgnoreCase)
        ).ToList();

        // Strategy 1: Look for labels in the same or previous row
        var nearbyLabels = unusedLabels
            .Where(l => Math.Abs(l.Row - currentRow) <= 1)
            .OrderByDescending(l => l.DocIndex)
            .ToList();

        foreach (var label in nearbyLabels)
        {
            // Check if this label makes sense as a section header
            if (IsSectionHeaderText(label.Text))
            {
                label.Used = true;
                return label.Text;
            }
        }

        // Strategy 2: Look at the most recent appropriate label
        var recentLabel = unusedLabels
            .Where(l => IsSectionHeaderText(l.Text))
            .OrderByDescending(l => l.DocIndex)
            .FirstOrDefault();

        if (recentLabel != null)
        {
            recentLabel.Used = true;
            return recentLabel.Text;
        }

        // Strategy 3: Try to extract from binding path or other attributes
        var binding = GetAttributeValue(elem, "binding");
        if (!string.IsNullOrEmpty(binding))
        {
            return ExtractNameFromBinding(binding);
        }

        // Strategy 4: Check if there's a caption or title attribute on the table
        var caption = GetAttributeValue(elem, "caption");
        if (!string.IsNullOrEmpty(caption))
            return caption;

        var title = GetAttributeValue(elem, "title");
        if (!string.IsNullOrEmpty(title))
            return title;

        return defaultName;
    }

    private bool IsSectionHeaderText(string text)
    {
        // Check if text looks like a section header
        if (string.IsNullOrWhiteSpace(text) || text.Length < 3)
            return false;

        // Section headers typically don't end with colons
        if (text.EndsWith(":"))
            return false;

        // Any text without a colon could be a section header
        // Don't restrict to specific keywords to keep it dynamic
        return true;
    }

    private RepeatingContext ExtractRepeatingTableInfo(XElement elem)
    {
        var caption = GetAttributeValue(elem, "caption");
        var title = GetAttributeValue(elem, "title");
        var binding = "";

        // Look for binding in tbody
        var tbody = elem.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "tbody");

        if (tbody != null)
        {
            binding = GetAttributeValue(tbody, "repeating") ??
                     GetAttributeValue(tbody, "xd:repeating") ??
                     GetAttributeValue(tbody, "binding");
        }

        // If no binding from tbody, check the table itself
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

    private SectionContext ExtractSectionInfo(XElement elem)
    {
        var className = elem.Attribute("class")?.Value ?? "";
        var ctrlId = GetAttributeValue(elem, "CtrlId");
        var caption = GetAttributeValue(elem, "caption_0") ?? GetAttributeValue(elem, "caption");
        var binding = GetAttributeValue(elem, "binding");

        var type = "section";
        if (className.Contains("xdRepeatingSection")) type = "repeating";
        if (className.Contains("xdOptional")) type = "optional";

        var name = !string.IsNullOrEmpty(caption) ? caption :
                  !string.IsNullOrEmpty(ctrlId) ? ctrlId : "Section";

        return new SectionContext
        {
            Name = name,
            Type = type,
            StartRow = currentRow,
            CtrlId = ctrlId,
            Binding = binding
        };
    }

    private string ExtractNameFromBinding(string binding)
    {
        if (string.IsNullOrEmpty(binding))
            return "";

        var parts = binding.Split('/');
        var lastPart = parts.Last();

        if (lastPart.Contains(':'))
            lastPart = lastPart.Split(':').Last();

        // Convert camelCase to readable format
        var readable = Regex.Replace(lastPart, "([a-z])([A-Z])", "$1 $2");
        readable = char.ToUpper(readable[0]) + readable.Substring(1);

        // Make plural if appropriate
        if (!readable.EndsWith("s") && !readable.EndsWith("es") && !readable.EndsWith("ies"))
        {
            if (readable.EndsWith("y"))
                readable = readable.Substring(0, readable.Length - 1) + "ies";
            else if (readable.EndsWith("Item"))
                readable = readable + "s";
            else if (!readable.Contains(" "))
                readable = readable + "s";
        }

        return readable;
    }

    private void HandleNewRow(XElement elem)
    {
        if (!inTableRow || elem.Name.LocalName.ToLower() == "tr")
        {
            if (currentCol > 1)
            {
                currentRow++;
                currentCol = 1;
            }

            if (sectionStack.Count > 0)
            {
                var section = sectionStack.Peek();
                sectionRowCounters[section.Name]++;
            }
        }

        if (elem.Name.LocalName.ToLower() == "tr")
        {
            inTableRow = true;
        }
    }

    private void ExitSection()
    {
        if (sections.Count > 0)
        {
            sections.Last().EndRow = currentRow;
        }

        if (sectionStack.Count > 0)
        {
            var poppedSection = sectionStack.Pop();

            // Reset repeating context if leaving a repeating section
            if (poppedSection.Type == "repeating" && repeatingContextStack.Count > 0)
            {
                var topContext = repeatingContextStack.Peek();
                if (topContext.Name == poppedSection.Name)
                {
                    repeatingContextStack.Pop();
                }
            }
        }
    }

    private bool IsRepeatingTable(XElement elem)
    {
        if (elem.Name.LocalName.ToLower() != "table")
            return false;

        var className = elem.Attribute("class")?.Value ?? "";
        var xctname = GetAttributeValue(elem, "xctname");

        if (className.Contains("xdRepeatingTable") ||
            xctname.Equals("repeatingtable", StringComparison.OrdinalIgnoreCase))
            return true;

        var hasRepeatingTbody = elem.Elements()
            .Any(e => e.Name.LocalName == "tbody" &&
                     (!string.IsNullOrEmpty(GetAttributeValue(e, "repeating")) ||
                      !string.IsNullOrEmpty(GetAttributeValue(e, "xctname")) &&
                      GetAttributeValue(e, "xctname").Equals("repeatingtable", StringComparison.OrdinalIgnoreCase)));

        return hasRepeatingTbody;
    }

    private bool IsSection(XElement elem)
    {
        var className = elem.Attribute("class")?.Value ?? "";

        if (className.Contains("xdSection") ||
            className.Contains("xdRepeatingSection"))
            return true;

        var xctname = GetAttributeValue(elem, "xctname");
        return xctname == "Section" || xctname == "RepeatingSection";
    }

    private bool IsNewRowIndicator(XElement elem)
    {
        var elemName = elem.Name.LocalName.ToLower();
        var className = elem.Attribute("class")?.Value ?? "";

        // Direct row indicators
        if (elemName == "tr") return true;

        // Section boundaries
        if (elemName == "div" && (className.Contains("xdSection") ||
                                 className.Contains("xdRepeatingSection")))
            return true;

        // Table headers with colspan
        if (elemName == "td" || elemName == "th")
        {
            var colspan = elem.Attribute("colspan")?.Value;
            if (colspan != null && int.Parse(colspan) > 2)
                return true;
        }

        // Visual separators
        if (elemName == "hr") return true;
        if (elemName == "img" && elem.Attribute("src")?.Value.Contains("line") == true)
            return true;

        // Style-based detection
        var style = elem.Attribute("style")?.Value ?? "";
        if (style.Contains("border-top") && style.Contains("solid"))
        {
            if (style.Contains("2.25pt") || style.Contains("2pt") || style.Contains("3pt"))
                return true;
        }

        // Class-based section headers
        if (className.Contains("xdTableHeader") ||
            className.Contains("xdHeadingRow") ||
            className.Contains("xdTitleRow"))
            return true;

        return false;
    }

    private bool IsPlaceholderText(XElement elem)
    {
        // Check if this element has the optionalPlaceholder class
        var className = elem.Attribute("class")?.Value ?? "";
        if (className.Contains("optionalPlaceholder"))
            return true;

        // Check for xd:action attributes which indicate placeholder elements
        var action = GetAttributeValue(elem, "action");
        if (action == "xCollection::insert")
            return true;

        return false;
    }

    private ControlDefinition TryExtractControl(XElement elem)
    {
        var elemName = elem.Name.LocalName.ToLower();

        // Skip sections - they're handled separately
        if (IsSection(elem))
            return null;

        // Try to extract label
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
                    DocIndex = ++docIndexCounter,
                    Properties = new Dictionary<string, string>(),
                    Controls = new List<ControlDefinition>()
                };
            }
        }

        // Try to extract control with xctname
        var xctAttr = GetAttributeValue(elem, "xctname");
        if (!string.IsNullOrEmpty(xctAttr) &&
            !xctAttr.Equals("ExpressionBox", StringComparison.OrdinalIgnoreCase) &&
            !xctAttr.Equals("Section", StringComparison.OrdinalIgnoreCase) &&
            !xctAttr.Equals("RepeatingSection", StringComparison.OrdinalIgnoreCase) &&
            !xctAttr.Equals("RepeatingTable", StringComparison.OrdinalIgnoreCase))
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
        // Handle GUID-based control types (like PeoplePicker)
        var mappedType = xctType;
        if (xctType.StartsWith("{") && xctType.EndsWith("}"))
        {
            // Check for known GUIDs
            if (xctType.Contains("61e40d31-993d-4777-8fa0-19ca59b6d0bb"))
            {
                mappedType = "PeoplePicker";
            }
            else
            {
                mappedType = "ActiveX-" + xctType;
            }
        }
        else
        {
            mappedType = MapControlType(xctType);
        }

        var control = new ControlDefinition
        {
            Type = mappedType,
            DocIndex = ++docIndexCounter,
            Properties = new Dictionary<string, string>(),
            Controls = new List<ControlDefinition>()
        };

        control.Label = elem.Attribute("title")?.Value ?? "";
        control.Name = string.IsNullOrEmpty(control.Label) ? "" : RemoveSpaces(control.Label);
        control.Binding = GetAttributeValue(elem, "binding");

        if (string.IsNullOrEmpty(control.Name) && !string.IsNullOrEmpty(control.Binding))
        {
            var parts = control.Binding.Split('/');
            var lastPart = parts.Last();
            control.Name = lastPart.Contains(':') ? lastPart.Split(':').Last() : lastPart;
        }

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

    private ControlDefinition ParseHtmlControl(XElement elem)
    {
        var control = new ControlDefinition
        {
            DocIndex = ++docIndexCounter,
            Properties = new Dictionary<string, string>(),
            Controls = new List<ControlDefinition>()
        };

        // Check if this HTML element has an xctname attribute
        var xctname = GetAttributeValue(elem, "xctname");
        if (!string.IsNullOrEmpty(xctname))
        {
            // Handle GUID-based control types
            if (xctname.StartsWith("{") && xctname.EndsWith("}"))
            {
                if (xctname.Contains("61e40d31-993d-4777-8fa0-19ca59b6d0bb"))
                {
                    control.Type = "PeoplePicker";
                }
                else
                {
                    control.Type = "ActiveX-" + xctname;
                }
            }
            else
            {
                control.Type = MapControlType(xctname);
            }
        }
        else
        {
            // Determine type from element name if no xctname
            if (elem.Name.LocalName.ToLower() == "select")
            {
                control.Type = "DropDown";
            }
            else if (elem.Name.LocalName.ToLower() == "textarea")
            {
                control.Type = "RichText";
            }
            else if (elem.Name.LocalName.ToLower() == "input")
            {
                var type = elem.Attribute("type")?.Value ?? "text";
                control.Type = MapInputType(type);
            }
        }

        control.Name = elem.Attribute("name")?.Value ?? "";
        control.Label = elem.Attribute("title")?.Value ?? "";
        control.Binding = GetAttributeValue(elem, "binding");

        // If no name, try to extract from binding
        if (string.IsNullOrEmpty(control.Name) && !string.IsNullOrEmpty(control.Binding))
        {
            var parts = control.Binding.Split('/');
            var lastPart = parts.Last();
            control.Name = lastPart.Contains(':') ? lastPart.Split(':').Last() : lastPart;
        }

        // Check for CtrlId
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
            DocIndex = ++docIndexCounter,
            Properties = new Dictionary<string, string>(),
            Controls = new List<ControlDefinition>()
        };

        // Check xctname attribute first
        var xctname = GetAttributeValue(elem, "xctname");
        if (!string.IsNullOrEmpty(xctname))
        {
            if (xctname.Contains("61e40d31-993d-4777-8fa0-19ca59b6d0bb"))
            {
                control.Type = "PeoplePicker";
            }
            else if (xctname.StartsWith("{") && xctname.EndsWith("}"))
            {
                control.Type = "ActiveX-" + xctname;
            }
            else
            {
                control.Type = MapControlType(xctname);
            }
        }
        else
        {
            // Fall back to classid
            var classId = elem.Attribute("classid")?.Value ?? "";
            if (classId.Contains("61e40d31-993d-4777-8fa0-19ca59b6d0bb"))
            {
                control.Type = "PeoplePicker";
            }
            else
            {
                control.Type = "ActiveX";
            }
        }

        // Check for duplicates by CtrlId
        var ctrlIdAttr = GetAttributeValue(elem, "CtrlId");
        if (!string.IsNullOrEmpty(ctrlIdAttr))
        {
            if (processedControls.Contains(ctrlIdAttr))
                return null;
            processedControls.Add(ctrlIdAttr);
            control.Properties["CtrlId"] = ctrlIdAttr;
        }

        // Check for CtrlId in params
        var ctrlIdParam = elem.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "param" &&
                               e.Attribute("name")?.Value == "CtrlId");
        if (ctrlIdParam != null)
        {
            var ctrlId = ctrlIdParam.Attribute("value")?.Value;
            if (!string.IsNullOrEmpty(ctrlId))
            {
                if (processedControls.Contains(ctrlId))
                    return null;
                processedControls.Add(ctrlId);
                control.Properties["CtrlId"] = ctrlId;
            }
        }

        control.Binding = GetAttributeValue(elem, "binding");
        control.Label = elem.Attribute("title")?.Value ?? "";

        // Get binding from params if not found
        if (string.IsNullOrEmpty(control.Binding))
        {
            var bindingParam = elem.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "param" &&
                                   e.Attribute("name")?.Value == "binding");
            if (bindingParam != null)
            {
                control.Binding = bindingParam.Attribute("value")?.Value ?? "";
            }
        }

        // Extract name from binding
        if (!string.IsNullOrEmpty(control.Binding))
        {
            var parts = control.Binding.Split('/');
            var lastPart = parts.Last();
            control.Name = lastPart.Contains(':') ? lastPart.Split(':').Last() : lastPart;
        }

        // Copy params as properties
        foreach (var param in elem.Descendants().Where(e => e.Name.LocalName == "param"))
        {
            var name = param.Attribute("name")?.Value;
            var value = param.Attribute("value")?.Value;
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(value))
            {
                control.Properties[name] = value;
            }
        }

        // Copy object attributes
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
            DocIndex = ++docIndexCounter,
            Properties = new Dictionary<string, string>(),
            Controls = new List<ControlDefinition>()
        };

        // Try to determine type from element and attributes
        var elemName = elem.Name.LocalName.ToLower();
        var className = elem.Attribute("class")?.Value ?? "";

        // Set type based on class or element name
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
        }
        else if (className.Contains("xdDTPicker"))
        {
            control.Type = "DatePicker";
        }
        else
        {
            control.Type = elemName; // Default to element name
        }

        control.Binding = GetAttributeValue(elem, "binding");
        control.Label = elem.Attribute("title")?.Value ?? "";

        // Extract name from binding
        if (!string.IsNullOrEmpty(control.Binding))
        {
            var parts = control.Binding.Split('/');
            var lastPart = parts.Last();
            control.Name = lastPart.Contains(':') ? lastPart.Split(':').Last() : lastPart;
        }

        // Check for CtrlId
        var ctrlId = GetAttributeValue(elem, "CtrlId");
        if (!string.IsNullOrEmpty(ctrlId))
        {
            if (processedControls.Contains(ctrlId))
                return null;
            processedControls.Add(ctrlId);
            control.Properties["CtrlId"] = ctrlId;
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

    private bool IsLabelElement(XElement elem)
    {
        var elemName = elem.Name.LocalName.ToLower();

        if (elemName == "strong" || elemName == "font" || elemName == "label" || elemName == "em")
        {
            if (!elem.Descendants().Any(d =>
                !string.IsNullOrEmpty(GetAttributeValue(d, "xctname")) ||
                d.Name.LocalName.ToLower() == "object" ||
                d.Name.LocalName.ToLower() == "input" ||
                d.Name.LocalName.ToLower() == "select"))
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
                if (childName == "strong" || childName == "em" || childName == "font" ||
                    childName == "span" || childName == "b" || childName == "i")
                {
                    sb.Append(GetDirectTextContent(childElem));
                }
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

        // Check for GUID-based types first
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
        var attr = elem.Attributes()
            .FirstOrDefault(a => a.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));
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
            var associatedControl = FindAssociatedControl(label, inputs, controls);
            if (associatedControl != null)
            {
                label.AssociatedControlId = associatedControl.Name;
                associatedControl.AssociatedLabelId = label.Name;

                if (string.IsNullOrEmpty(associatedControl.Label))
                {
                    associatedControl.Label = label.Label;
                }
            }
        }
    }

    private ControlDefinition FindAssociatedControl(
        ControlDefinition label,
        List<ControlDefinition> inputs,
        List<ControlDefinition> allControls)
    {
        var labelRow = ExtractRow(label.GridPosition);
        var labelCol = ExtractColumn(label.GridPosition);

        // Strategy 1: Next control in same row
        var sameRowControls = inputs
            .Where(c => ExtractRow(c.GridPosition) == labelRow &&
                       ExtractColumn(c.GridPosition) > labelCol)
            .OrderBy(c => ExtractColumn(c.GridPosition))
            .ToList();

        if (sameRowControls.Any())
            return sameRowControls.First();

        // Strategy 2: First control in next row
        var nextRowControls = inputs
            .Where(c => ExtractRow(c.GridPosition) == labelRow + 1)
            .OrderBy(c => ExtractColumn(c.GridPosition))
            .ToList();

        if (nextRowControls.Any())
            return nextRowControls.First();

        // Strategy 3: By document order
        var labelIndex = label.DocIndex;
        var nextControl = inputs
            .Where(c => c.DocIndex > labelIndex)
            .OrderBy(c => c.DocIndex)
            .FirstOrDefault();

        return nextControl;
    }

    private int ExtractRow(string gridPosition)
    {
        var match = Regex.Match(gridPosition, @"^(\d+)");
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    private int ExtractColumn(string gridPosition)
    {
        var match = Regex.Match(gridPosition, @"([A-Z]+)$");
        if (!match.Success) return 0;

        var letters = match.Groups[1].Value;
        int column = 0;
        for (int i = 0; i < letters.Length; i++)
        {
            column = column * 26 + (letters[i] - 'A' + 1);
        }
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
        var match = Regex.Match(gridPosition, @"^(\d+)");
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
            var select = applyTemplate.Attribute("select")?.Value ?? "";

            var template = xslDoc.Descendants(ns + "template")
                .FirstOrDefault(t => t.Attribute("mode")?.Value == mode);

            if (template != null)
            {
                var section = ParseDynamicSection(template, mode);
                if (section != null)
                {
                    dynamicSections.Add(section);
                }
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
            ConditionValue = ExtractConditionValue(condition)
        };

        // Find section content using namespace-aware attribute search
        var sectionDiv = ifElement.Descendants()
            .FirstOrDefault(e => e.Attributes()
                .Any(a => a.Name.LocalName == "CtrlId"));

        if (sectionDiv != null)
        {
            sectionInfo.CtrlId = sectionDiv.Attributes()
                .FirstOrDefault(a => a.Name.LocalName == "CtrlId")?.Value;
            sectionInfo.Caption = sectionDiv.Attributes()
                .FirstOrDefault(a => a.Name.LocalName == "caption_0")?.Value;
            sectionInfo.Controls = ExtractSectionControls(sectionDiv);
        }

        return sectionInfo;
    }

    private string ExtractConditionField(string condition)
    {
        var match = Regex.Match(condition, @"my:(\w+)");
        return match.Success ? match.Groups[1].Value : "";
    }

    private string ExtractConditionValue(string condition)
    {
        var match = Regex.Match(condition, @"contains\([^,]+,\s*[""']([^""']+)[""']\)");
        return match.Success ? match.Groups[1].Value : "";
    }

    private List<string> ExtractSectionControls(XElement sectionElement)
    {
        var controls = new List<string>();

        // Find all controls within this section using namespace-aware search
        var controlElements = sectionElement.Descendants()
            .Where(e => e.Attributes()
                .Any(a => a.Name.LocalName == "CtrlId"))
            .Select(e => e.Attributes()
                .FirstOrDefault(a => a.Name.LocalName == "CtrlId")?.Value)
            .Where(v => v != null)
            .ToList();

        return controlElements;
    }
}

    #endregion
