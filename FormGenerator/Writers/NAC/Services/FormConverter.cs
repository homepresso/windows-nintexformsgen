using FormGenerator.Writers.NAC.Models;
using System.Text;

namespace FormGenerator.Writers.NAC.Services
{
    public class FormConverter
    {
        private readonly Dictionary<string, string> _controlTypeMapping;
        private readonly Dictionary<string, string> _widgetMapping;
        private readonly Dictionary<string, string> _dataTypeMapping;
        private readonly HashSet<string> _usedVariableNames = new();

        public FormConverter()
        {
            _controlTypeMapping = InitializeControlTypeMapping();
            _widgetMapping = InitializeWidgetMapping();
            _dataTypeMapping = InitializeDataTypeMapping();
        }

        public FormDefinition ConvertForm(SourceForm sourceForm)
        {
            var formDefinition = new FormDefinition
            {
                Name = sourceForm.FileName.Replace(".xsn", ""),
                PageSettings = new PageSettings
                {
                    Pages = new List<Page>
                    {
                        new Page
                        {
                            Name = "page_default",
                            Title = "page_default.FORMDESIGNER_CONTROL_PROP_TITLE"
                        }
                    }
                },
                Translations = new Dictionary<string, Dictionary<string, string>>
                {
                    ["en"] = new Dictionary<string, string>()
                }
            };

            // Add default translations
            AddDefaultTranslations(formDefinition.Translations["en"]);

            // Process only the first view (view1.xsl) for form controls
            var mainView = sourceForm.FormDefinition.Views.FirstOrDefault(v => v.ViewName.Contains("view1") || sourceForm.FormDefinition.Views.Count == 1);
            if (mainView != null)
            {
                ProcessView(mainView, formDefinition);
            }

            // Always process variables from Data as a fallback to ensure dynamic coverage
            ProcessDataFallback(sourceForm, formDefinition);

            // Add action panel
            AddActionPanel(formDefinition);

            return formDefinition;
        }

        // New: convert a specific view of a form (multi-view output)
        public FormDefinition ConvertFormForView(SourceForm sourceForm, SourceView view, string formDisplayName)
        {
            var formDefinition = new FormDefinition
            {
                Name = formDisplayName,
                PageSettings = new PageSettings
                {
                    Pages = new List<Page> { new Page { Name = "page_default", Title = "page_default.FORMDESIGNER_CONTROL_PROP_TITLE" } }
                },
                Translations = new Dictionary<string, Dictionary<string, string>> { ["en"] = new Dictionary<string, string>() }
            };

            AddDefaultTranslations(formDefinition.Translations["en"]);

            // Process only this view
            ProcessView(view, formDefinition);

            // Add variables that were not rendered in this view
            ProcessDataFallback(sourceForm, formDefinition);

            AddActionPanel(formDefinition);

            return formDefinition;
        }

        // Build controls from FormDefinition.Data to ensure fully dynamic conversion
        private void ProcessDataFallback(SourceForm sourceForm, FormDefinition formDefinition)
        {
            if (sourceForm?.FormDefinition?.Data == null || !sourceForm.FormDefinition.Data.Any()) return;

            // Gather variable IDs already present to avoid duplicates
            var existingVariableIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var existingControlNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var existingRepeatingSectionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in formDefinition.Rows)
            {
                foreach (var control in row.Controls)
                {
                    var vid = control?.Properties?.ConnectedVariableId;
                    if (!string.IsNullOrEmpty(vid)) existingVariableIds.Add(vid);
                    if (!string.IsNullOrEmpty(control?.Properties?.Name)) existingControlNames.Add(Normalize(control.Properties.Name));
                    if (string.Equals(control?.Widget, "repeating-section", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(control?.Properties?.Name))
                    {
                        existingRepeatingSectionNames.Add(control.Properties.Name);
                    }
                }
            }

            // Group repeating items by section name
            var repeatingGroups = sourceForm.FormDefinition.Data
                .Where(d => d.IsRepeating && !string.IsNullOrEmpty(d.RepeatingSectionName))
                .GroupBy(d => d.RepeatingSectionName!, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Create repeating sections from data when not already added
            foreach (var group in repeatingGroups)
            {
                var sectionName = group.Key;
                if (existingRepeatingSectionNames.Contains(sectionName)) continue; // already exists from view processing

                // Filter out junk/filter items
                var templateControls = group.Where(i => !IsFilterOrNoise(i)).ToList();
                if (!templateControls.Any()) continue;
                // Build repeating section control
                var sectionId = GenerateControlId();
                var row = new Row { Controls = new List<Control>(), PageName = "page_default", Id = GenerateId() };
                var rsControl = new Control
                {
                    Id = sectionId,
                    DataType = "object",
                    Widget = "repeating-section",
                    Source = new Source { CreatedBy = "user" },
                    Properties = new ControlProperties
                    {
                        Name = sectionName,
                        Title = $"{sectionId}.FORMDESIGNER_CONTROL_PROP_TITLE",
                        Format = "repeating-section",
                        IsConnectedToVariable = true,
                        ConnectedVariableId = GenerateVariableId(sectionName),
                        ShowHeader = false,
                        ShowBorder = true,
                        TranslateHeader = false,
                        IsNestedControl = true,
                        ShowHeaderBackground = false,
                        ShowHeaderDivider = false,
                        ShowExpandable = false,
                        CollapsedByDefault = false,
                        ShowOnlyHeaderDivider = false,
                        AddRowButtonLabel = $"{sectionId}.FORMDESIGNER_CONTROL_REPEATING_SECTION_PROP_NEW_BUTTON_LABEL",
                        MinRows = 1,
                        MaxRows = 50,
                        DefaultRows = 1,
                        AlternateBackgroundColour = true,
                        TemplateForm = new { id = sectionId, name = sectionName, ruleGroups = Array.Empty<object>(), version = 26, variableContext = new { variables = Array.Empty<object>() } }
                    }
                };

                // Translations for section title and button
                formDefinition.Translations["en"][rsControl.Properties.Title] = sectionName;
                formDefinition.Translations["en"][$"{sectionId}.FORMDESIGNER_CONTROL_REPEATING_SECTION_PROP_NEW_BUTTON_LABEL"] = "Add new row";

                // Build template JSON rows from group items
                var tfj = BuildRepeatingTemplateFromData(templateControls, sectionId, sectionName, formDefinition.Translations["en"]);
                if (!string.IsNullOrEmpty(tfj))
                {
                    rsControl.Properties.TemplateFormJson = tfj;
                    row.Controls.Add(rsControl);
                    formDefinition.Rows.Add(row);
                }
            }

            // Non-repeating items not yet rendered
            var nonRepeating = sourceForm.FormDefinition.Data.Where(d => !d.IsRepeating && !IsFilterOrNoise(d)).ToList();
            var pendingControls = new List<Control>();
            foreach (var item in nonRepeating)
            {
                var control = CreateControlFromData(item, formDefinition.Translations["en"]);
                if (control == null) continue;
                var vid = control.Properties.ConnectedVariableId;
                var normName = Normalize(control.Properties.Name ?? "");
                if (existingControlNames.Contains(normName)) continue; // same field already rendered via views
                if (!string.IsNullOrEmpty(vid) && existingVariableIds.Contains(vid)) continue; // already present
                pendingControls.Add(control);
                if (!string.IsNullOrEmpty(vid)) existingVariableIds.Add(vid);
                if (!string.IsNullOrEmpty(normName)) existingControlNames.Add(normName);
            }

            if (pendingControls.Any())
            {
                // Lay out in rows of 2 controls
                for (int i = 0; i < pendingControls.Count; i += 2)
                {
                    var row = new Row { Controls = new List<Control>(), PageName = "page_default", Id = GenerateId() };
                    row.Controls.Add(pendingControls[i]);
                    if (i + 1 < pendingControls.Count) row.Controls.Add(pendingControls[i + 1]);
                    row.Sizes = CalculateRowSizes(row.Controls.Count);
                    formDefinition.Rows.Add(row);
                }
            }
        }

        private string? BuildRepeatingTemplateFromData(List<SourceDataItem> items, string containerId, string sectionName, Dictionary<string, string> translations)
        {
            var templateRows = new List<object>();
            var controls = new List<Control>();
            foreach (var item in items)
            {
                var control = CreateControlFromData(item, translations);
                if (control != null) controls.Add(control);
            }

            if (!controls.Any()) return null; // no meaningful fields for this section

            // Rows of 2
            for (int i = 0; i < controls.Count; i += 2)
            {
                var rowControls = new List<object>();
                rowControls.Add(ToTemplateControl(controls[i]));
                if (i + 1 < controls.Count) rowControls.Add(ToTemplateControl(controls[i + 1]));
                var sizes = CalculateRowSizes(rowControls.Count);
                templateRows.Add(new
                {
                    controls = rowControls.ToArray(),
                    sizes = sizes,
                    autoResizing = true,
                    restrictions = new object(),
                    pageName = "page_default",
                    containerName = containerId,
                    id = GenerateId()
                });
            }

            var templateForm = new
            {
                id = containerId,
                name = sectionName,
                ruleGroups = Array.Empty<object>(),
                version = 26,
                variableContext = new { variables = Array.Empty<object>() },
                rows = templateRows.ToArray()
            };
            return Newtonsoft.Json.JsonConvert.SerializeObject(templateForm);
        }

        private object ToTemplateControl(Control control)
        {
            return new
            {
                id = control.Id,
                dataType = control.DataType,
                widget = control.Widget,
                widgetMinimumSize = control.WidgetMinimumSize,
                source = control.Source,
                properties = control.Properties,
                sourceVariable = control.SourceVariable
            };
        }

        private Control? CreateControlFromData(SourceDataItem item, Dictionary<string, string> translations)
        {
            // Skip container-only items
            if (item.Type?.Equals("RepeatingTable", StringComparison.OrdinalIgnoreCase) ?? false)
            {
                return null;
            }

            var controlId = GenerateControlId();
            var displayName = string.IsNullOrWhiteSpace(item.DisplayName) ? item.ColumnName : item.DisplayName;
            var widget = GetWidgetType(item.Type);
            if (widget == "unknown") widget = "textbox";
            var dataType = GetDataType(item.Type);

            var control = new Control
            {
                Id = controlId,
                DataType = dataType,
                Widget = widget,
                WidgetMinimumSize = GetWidgetMinimumSize(widget),
                Source = new Source { CreatedBy = "user" },
                Properties = new ControlProperties
                {
                    Name = displayName,
                    Title = $"{controlId}.FORMDESIGNER_CONTROL_PROP_TITLE",
                    Format = GetFormat(item.Type),
                    IsConnectedToVariable = true,
                    ConnectedVariableId = GenerateVariableId(displayName)
                },
                SourceVariable = new SourceVariable { DisplayName = displayName, AutoGenerateName = true }
            };

            // Title translation
            translations[$"{controlId}.FORMDESIGNER_CONTROL_PROP_TITLE"] = displayName;

            // Choice/Dropdown options
            if ((item.Type?.Equals("DropDown", StringComparison.OrdinalIgnoreCase) ?? false) ||
                (item.Type?.Equals("Choice", StringComparison.OrdinalIgnoreCase) ?? false))
            {
                control.Widget = "choice";
                control.Properties.ViewType = "1"; // dropdown
                if (item.ValidValues != null && item.ValidValues.Any())
                {
                    var options = string.Join("\n", item.ValidValues.Select(v => v.DisplayText));
                    control.Properties.Items = options;
                    translations[$"{controlId}.FORMDESIGNER_CONTROL_PROP_CHOICE_OPTIONS_INSERT_LABEL"] = options;
                    translations[$"{controlId}.FORMDESIGNER_CONTROL_PROP_CHOICE_FILLIN_PLACEHOLDER"] = "Select...";
                }
            }

            // DatePicker specifics
            if (item.Type?.Equals("DatePicker", StringComparison.OrdinalIgnoreCase) ?? false)
            {
                control.Properties.ShowDateOnly = true;
                control.Properties.ShowSetTimeZone = false;
                control.Properties.DateTimeZone = "UTC";
                control.Properties.RestrictPastDates = false;
            }

            return control;
        }

        private bool IsFilterOrNoise(SourceDataItem item)
        {
            var name = (item.DisplayName ?? item.ColumnName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(name)) return true;
            if (name.Contains("FILTER", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.StartsWith("string(\"", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private string Normalize(string s)
        {
            return System.Text.RegularExpressions.Regex.Replace(s ?? string.Empty, "\\s+", " ").Trim().ToLowerInvariant();
        }

        private void ProcessView(SourceView view, FormDefinition formDefinition)
        {
            // Group controls by their grid row (numeric part of GridPosition)
            var controlsByRow = new Dictionary<int, List<(SourceControl control, int col)>>();
            var repeatingSections = new Dictionary<string, List<SourceControl>>(StringComparer.OrdinalIgnoreCase);
            var sectionHeadersByRow = new Dictionary<int, SourceControl>();
            var labelsByName = new Dictionary<string, SourceControl>();
            
            // First pass: collect all labels for pairing with data controls
            foreach (var sourceControl in view.Controls)
            {
                if (sourceControl.Type == "Label" && string.IsNullOrEmpty(sourceControl.Binding))
                {
                    // Store labels by their name for pairing with data controls
                    labelsByName[sourceControl.Name] = sourceControl;
                }
            }
            
            foreach (var sourceControl in view.Controls)
            {
                // Check if this control belongs to a repeating section first
                if (sourceControl.RepeatingSectionInfo?.IsInRepeatingSection == true)
                {
                    var sectionName = sourceControl.RepeatingSectionInfo.RepeatingSectionName;
                    if (!string.IsNullOrEmpty(sectionName))
                    {
                        if (!repeatingSections.ContainsKey(sectionName))
                        {
                            repeatingSections[sectionName] = new List<SourceControl>();
                        }
                        repeatingSections[sectionName].Add(sourceControl);
                    }
                }
                else if (sourceControl.Type == "Label" && string.IsNullOrEmpty(sourceControl.Binding))
                {
                    var (row, col) = ParseGridPosition(sourceControl.GridPosition);
                    if (row > 0 && !string.IsNullOrEmpty(sourceControl.Label))
                    {
                        // Check if this is a section header vs a field label
                        bool isFieldLabel = labelsByName.ContainsKey(sourceControl.Name) && 
                                          view.Controls.Any(c => c.Name == sourceControl.Name && c.Type != "Label");
                        
                        if (!isFieldLabel && col == 0) // Likely a section header
                        {
                            sectionHeadersByRow[row] = sourceControl;
                        }
                    }
                    // Field labels will be used as titles for their corresponding data controls
                }
                else if (sourceControl.Type != "RepeatingTable")
                {
                    // Parse grid position (e.g., "2B" -> row 2, col B)
                    var (row, col) = ParseGridPosition(sourceControl.GridPosition);
                    if (row > 0)
                    {
                        // Check if there's a corresponding label for this control
                        if (labelsByName.TryGetValue(sourceControl.Name, out var correspondingLabel))
                        {
                            // Use the label text as the control's label
                            sourceControl.Label = correspondingLabel.Label;
                        }
                        
                        if (!controlsByRow.ContainsKey(row))
                        {
                            controlsByRow[row] = new List<(SourceControl, int)>();
                        }
                        controlsByRow[row].Add((sourceControl, col));
                    }
                }
            }

            // Determine the intended start row for each repeating section dynamically
            var repeatingSectionStartRows = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in repeatingSections)
            {
                var name = kvp.Key;
                // Default to minimum control row within the section
                var minRow = kvp.Value
                    .Select(c => ParseGridPosition(c.GridPosition).row)
                    .Where(r => r > 0)
                    .DefaultIfEmpty(int.MaxValue)
                    .Min();

                // If we have an explicit section in metadata, prefer that row
                var metaRow = view.Sections?.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && (s.Type?.Equals("repeating", StringComparison.OrdinalIgnoreCase) ?? false))?.StartRow ?? int.MaxValue;

                // If we have a header label row that matches, consider that too
                var headerRow = sectionHeadersByRow
                    .Where(kv => string.Equals(kv.Value.Label, name, StringComparison.OrdinalIgnoreCase))
                    .Select(kv => kv.Key)
                    .DefaultIfEmpty(int.MaxValue)
                    .Min();

                var chosenRow = new[] { minRow, metaRow, headerRow }.Min();
                if (chosenRow != int.MaxValue)
                {
                    repeatingSectionStartRows[name] = chosenRow;
                }
            }

            // Process all rows in order, inserting repeating sections at their computed start rows
            var processedRepeatingSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // CRITICAL FIX: Include repeating section start rows in allRows so they get processed!
            var allRows = controlsByRow.Keys
                .Union(sectionHeadersByRow.Keys)
                .Union(repeatingSectionStartRows.Values)  // Add repeating section start rows!
                .OrderBy(r => r)
                .ToList();

            foreach (var row in allRows)
            {
                // If a repeating section begins here, insert it and mark as processed
                var sectionAtRow = repeatingSectionStartRows.FirstOrDefault(kv => kv.Value == row && !processedRepeatingSections.Contains(kv.Key));
                if (!string.IsNullOrEmpty(sectionAtRow.Key))
                {
                    ProcessRepeatingSection(sectionAtRow.Key, formDefinition, repeatingSections);
                    processedRepeatingSections.Add(sectionAtRow.Key);
                    // Do not add the section header label again for this row
                    continue;
                }

                // Otherwise, render the row normally
                ProcessSingleRow(formDefinition, row, controlsByRow, sectionHeadersByRow);
            }
        }

        private void ProcessFormWithSections(SourceView view, FormDefinition formDefinition,
            Dictionary<int, List<(SourceControl control, int col)>> controlsByRow,
            Dictionary<int, SourceControl> sectionHeadersByRow,
            Dictionary<string, List<SourceControl>> repeatingSections)
        {
            var processedRows = new HashSet<int>();
            var allRows = controlsByRow.Keys.Union(sectionHeadersByRow.Keys).OrderBy(r => r).ToList();
            
            Console.WriteLine($"ProcessFormWithSections: Found {repeatingSections.Count} repeating sections: {string.Join(", ", repeatingSections.Keys)}");
            Console.WriteLine($"ProcessFormWithSections: Found {view.Sections?.Count() ?? 0} sections in metadata");
            
            // Process rows in order, checking for sections
            foreach (var row in allRows)
            {
                if (processedRows.Contains(row)) continue;
                
                // Check if this row starts a section
                var section = view.Sections?.FirstOrDefault(s => s.StartRow == row);
                if (section != null)
                {
                    Console.WriteLine($"Row {row}: Found section '{section.Name}' (Type: {section.Type})");
                    if (section.Type?.ToLower() == "repeating" && repeatingSections.ContainsKey(section.Name))
                    {
                        Console.WriteLine($"Processing repeating section '{section.Name}' at row {row}");
                        // Process repeating section at its correct position
                        ProcessRepeatingSection(section.Name, formDefinition, repeatingSections);
                        
                        // Mark rows as processed
                        for (int sectionRow = section.StartRow; sectionRow <= section.EndRow; sectionRow++)
                        {
                            processedRows.Add(sectionRow);
                        }
                    }
                    else
                    {
                        // Process as group or simple section
                        ProcessSectionRows(formDefinition, section, controlsByRow, sectionHeadersByRow);
                        
                        // Mark rows as processed
                        for (int sectionRow = section.StartRow; sectionRow <= section.EndRow; sectionRow++)
                        {
                            processedRows.Add(sectionRow);
                        }
                    }
                }
                else
                {
                    // Process single row if not part of any section
                    ProcessSingleRow(formDefinition, row, controlsByRow, sectionHeadersByRow);
                    processedRows.Add(row);
                }
            }
        }

        private void ProcessFormSimple(FormDefinition formDefinition,
            Dictionary<int, List<(SourceControl control, int col)>> controlsByRow,
            Dictionary<int, SourceControl> sectionHeadersByRow,
            Dictionary<string, List<SourceControl>> repeatingSections)
        {
            var processedRepeatingSections = new HashSet<string>();
            var allRows = controlsByRow.Keys.Union(sectionHeadersByRow.Keys).OrderBy(r => r);
            
            Console.WriteLine($"ProcessFormSimple: Found {repeatingSections.Count} repeating sections: {string.Join(", ", repeatingSections.Keys)}");
            
            foreach (var row in allRows)
            {
                // Check if this row might contain a repeating section that we haven't processed yet
                bool rowHasRepeatingSection = false;
                if (controlsByRow.ContainsKey(row))
                {
                    foreach (var (control, _) in controlsByRow[row])
                    {
                        if (control.RepeatingSectionInfo?.IsInRepeatingSection == true)
                        {
                            var sectionName = control.RepeatingSectionInfo.RepeatingSectionName;
                            Console.WriteLine($"Row {row}: Found repeating section control '{control.Name}' for section '{sectionName}'");
                            if (!string.IsNullOrEmpty(sectionName) && !processedRepeatingSections.Contains(sectionName))
                            {
                                Console.WriteLine($"Processing repeating section '{sectionName}' at row {row}");
                                // Process this repeating section at its natural position
                                ProcessRepeatingSection(sectionName, formDefinition, repeatingSections);
                                processedRepeatingSections.Add(sectionName);
                                rowHasRepeatingSection = true;
                                break; // Only process one repeating section per row encounter
                            }
                        }
                    }
                }
                
                // If this row doesn't start a repeating section, process it normally
                if (!rowHasRepeatingSection)
                {
                    ProcessSingleRow(formDefinition, row, controlsByRow, sectionHeadersByRow);
                }
            }
        }

        private void ProcessRepeatingSection(string sectionName, FormDefinition formDefinition,
            Dictionary<string, List<SourceControl>> repeatingSections)
        {
            var sectionControls = repeatingSections[sectionName];

            // Add section label first
                var labelRow = new Row
                {
                    Controls = new List<Control>(),
                    PageName = "page_default",
                    Id = GenerateId()
                };

            var labelId = GenerateControlId();
            var label = new Control
            {
                Id = labelId,
                DataType = "string",
                Widget = "richtext-label",
                Source = new Source { CreatedBy = "user" },
                Properties = new ControlProperties
                {
                    Name = $"{sectionName.ToUpper()}_LABEL",
                    Title = $"{labelId}.FORMDESIGNER_CONTROL_PROP_TITLE",
                    Format = "",
                    Text = $"{labelId}.FORMDESIGNER_CONTROL_PROP_TEXT",
                    IsConnectedToVariable = true,
                    ConnectedVariableId = GenerateVariableId($"{sectionName}_LABEL")
                }
            };
            
            formDefinition.Translations["en"][$"{labelId}.FORMDESIGNER_CONTROL_PROP_TITLE"] = "";
            formDefinition.Translations["en"][$"{labelId}.FORMDESIGNER_CONTROL_PROP_TEXT"] = sectionName;
            
            labelRow.Controls.Add(label);
            formDefinition.Rows.Add(labelRow);
            
            // Create repeating section control
            var virtualRepeatingTable = new SourceControl
            {
                Name = $"{sectionName.ToUpper()}_REPEATING_SECTION",
                Label = sectionName,
                Type = "RepeatingTable",
                GridPosition = "1A" // Default position
            };
            
            var row = new Row
            {
                Controls = new List<Control>(),
                PageName = "page_default",
                Id = GenerateId()
            };

            var repeatingSectionControl = ConvertControl(virtualRepeatingTable, formDefinition.Translations["en"]);
            if (repeatingSectionControl != null)
            {
                var templateFormJson = BuildRepeatingTableTemplate(sectionControls, repeatingSectionControl.Id, sectionName, formDefinition.Translations["en"]);
                repeatingSectionControl.Properties.TemplateFormJson = templateFormJson;

                row.Controls.Add(repeatingSectionControl);
                formDefinition.Rows.Add(row);
            }
        }

        private void ProcessSectionRows(FormDefinition formDefinition, SourceSection section,
            Dictionary<int, List<(SourceControl control, int col)>> controlsByRow,
            Dictionary<int, SourceControl> sectionHeadersByRow)
        {
            // Determine if this should be a group based on control count
            var totalControls = 0;
            for (int row = section.StartRow; row <= section.EndRow; row++)
            {
                if (controlsByRow.ContainsKey(row))
                {
                    totalControls += controlsByRow[row].Count;
                }
            }

            if (totalControls >= 2 && !string.IsNullOrEmpty(section.Name))
            {
                ProcessAsGroup(formDefinition, section, controlsByRow);
            }
            else
            {
                // Process as simple rows
                for (int row = section.StartRow; row <= section.EndRow; row++)
                {
                    ProcessSingleRow(formDefinition, row, controlsByRow, sectionHeadersByRow);
                }
            }
        }

        private void ProcessAsGroup(FormDefinition formDefinition, SourceSection section,
            Dictionary<int, List<(SourceControl control, int col)>> controlsByRow)
        {
            var groupId = GenerateControlId();
            var groupRow = new Row
            {
                Controls = new List<Control>(),
                PageName = "page_default",
                Id = GenerateId()
            };
            
            var groupControl = new Control
            {
                Id = groupId,
                Widget = "group-control",
                WidgetMinimumSize = 6,
                Source = new Source { CreatedBy = "user" },
                Properties = new ControlProperties
                {
                    Name = section.Name,
                    Title = $"{groupId}.FORMDESIGNER_CONTROL_PROP_TITLE",
                    HeaderText = $"{groupId}.FORMDESIGNER_CONTROL_PROP_HEADER_TEXT",
                    ShowHeader = true,
                    ShowBorder = true,
                    TranslateHeader = false,
                    IsNestedControl = true,
                    ShowHeaderBackground = false,
                    ShowHeaderDivider = true,
                    ShowExpandable = false,
                    CollapsedByDefault = false,
                    ShowOnlyHeaderDivider = false,
                    IsConnectedToVariable = true,
                    ConnectedVariableId = GenerateVariableId(section.Name)
                },
                SourceVariable = new SourceVariable
                {
                    DisplayName = section.Name,
                    AutoGenerateName = true
                }
            };
            
            // Add translations
            formDefinition.Translations["en"][$"{groupId}.FORMDESIGNER_CONTROL_PROP_TITLE"] = section.Name;
            formDefinition.Translations["en"][$"{groupId}.FORMDESIGNER_CONTROL_PROP_HEADER_TEXT"] = $"<p>{section.Name}</p>";
            
            // Add controls within this section to the group
            var groupRows = new List<object>();
            for (int rowNum = section.StartRow; rowNum <= section.EndRow; rowNum++)
            {
                if (controlsByRow.ContainsKey(rowNum))
                {
                    var rowControls = controlsByRow[rowNum]
                        .Where(c => c.control.RepeatingSectionInfo?.IsInRepeatingSection != true) // Filter out repeating section controls
                        .OrderBy(c => c.col)
                        .Select(c => c.control)
                        .ToList();
                    var groupRowControls = new List<object>();
                    
                    foreach (var sourceControl in rowControls)
                    {
                        var control = ConvertControl(sourceControl, formDefinition.Translations["en"]);
                        if (control != null)
                        {
                            var groupRowControl = new
                            {
                                id = control.Id,
                                dataType = control.DataType,
                                widget = control.Widget,
                                widgetMinimumSize = control.WidgetMinimumSize,
                                source = control.Source,
                                properties = control.Properties,
                                sourceVariable = control.SourceVariable
                            };
                            groupRowControls.Add(groupRowControl);
                        }
                    }
                    
                    if (groupRowControls.Any())
                    {
                        var sizes = CalculateRowSizes(groupRowControls.Count);
                        var groupRowObj = new
                        {
                            controls = groupRowControls.ToArray(),
                            sizes = sizes,
                            autoResizing = true,
                            restrictions = new object(),
                            pageName = "page_default",
                            containerName = groupId,
                            id = GenerateId()
                        };
                        groupRows.Add(groupRowObj);
                    }
                }
            }
            
            groupControl.Properties.Rows = groupRows.ToArray();
            groupRow.Controls.Add(groupControl);
            formDefinition.Rows.Add(groupRow);
        }

        private void ProcessSingleRow(FormDefinition formDefinition, int rowNum,
            Dictionary<int, List<(SourceControl control, int col)>> controlsByRow,
            Dictionary<int, SourceControl> sectionHeadersByRow)
        {
            // Check if this row has a section header
            if (sectionHeadersByRow.ContainsKey(rowNum))
            {
                var labelControl = sectionHeadersByRow[rowNum];
                var labelRow = new Row
                {
                    Controls = new List<Control>(),
                    PageName = "page_default",
                    Id = GenerateId()
                };

                var sectionHeaderId = GenerateControlId();
                var sectionHeader = new Control
                {
                    Id = sectionHeaderId,
                    DataType = "string",
                    Widget = "richtext-label",
                    Source = new Source { CreatedBy = "user" },
                    Properties = new ControlProperties
                    {
                        Name = labelControl.Name,
                        Title = $"{sectionHeaderId}.FORMDESIGNER_CONTROL_PROP_TITLE",
                        Format = "",
                        Text = $"{sectionHeaderId}.FORMDESIGNER_CONTROL_PROP_TEXT",
                        IsConnectedToVariable = true,
                        ConnectedVariableId = GenerateVariableId(labelControl.Name)
                    }
                };
                
                formDefinition.Translations["en"][$"{sectionHeaderId}.FORMDESIGNER_CONTROL_PROP_TITLE"] = "";
                formDefinition.Translations["en"][$"{sectionHeaderId}.FORMDESIGNER_CONTROL_PROP_TEXT"] = labelControl.Label;
                
                labelRow.Controls.Add(sectionHeader);
                formDefinition.Rows.Add(labelRow);
            }

            // Process regular controls for this row, but skip repeating section controls
            if (controlsByRow.ContainsKey(rowNum))
            {
                var rowControls = controlsByRow[rowNum]
                    .Where(c => c.control.RepeatingSectionInfo?.IsInRepeatingSection != true) // Filter out repeating section controls
                    .OrderBy(c => c.col)
                    .Select(c => c.control)
                    .ToList();

                if (rowControls.Any())
                {
                var row = new Row
                {
                    Controls = new List<Control>(),
                    PageName = "page_default",
                    Id = GenerateId()
                };

                foreach (var sourceControl in rowControls)
                {
                    var control = ConvertControl(sourceControl, formDefinition.Translations["en"]);
                    if (control != null)
                    {
                        row.Controls.Add(control);
                    }
                }

                if (row.Controls.Any())
                {
                        row.Sizes = CalculateRowSizes(row.Controls.Count);
                    formDefinition.Rows.Add(row);
                    }
                }
            }
        }

        private void ProcessFormStructureDynamically(SourceView view, FormDefinition formDefinition,
            Dictionary<int, List<(SourceControl control, int col)>> controlsByRow,
            Dictionary<int, SourceControl> sectionHeadersByRow,
            Dictionary<string, List<SourceControl>> repeatingSections)
        {
            // Create a comprehensive grid analysis and store it for use across methods
            _currentGridAnalysis = AnalyzeFormGrid(view, controlsByRow, sectionHeadersByRow, repeatingSections);
            _currentRepeatingSections = repeatingSections;
            

            
            // Clear processed trackers
            _processedGroups.Clear();
            _processedRepeatingSections.Clear();
            
            // Process each row in order, respecting sections and groups
            foreach (var rowInfo in _currentGridAnalysis.OrderBy(r => r.RowNumber))
            {
                ProcessFormRow(rowInfo, formDefinition, view);
            }
        }

        private List<FormRowInfo> AnalyzeFormGrid(SourceView view,
            Dictionary<int, List<(SourceControl control, int col)>> controlsByRow,
            Dictionary<int, SourceControl> sectionHeadersByRow,
            Dictionary<string, List<SourceControl>> repeatingSections)
        {
            var rowInfos = new List<FormRowInfo>();
            var allRows = controlsByRow.Keys.Union(sectionHeadersByRow.Keys).OrderBy(r => r).ToList();
            
            // Determine section boundaries using metadata if available
            var sectionBoundaries = DetermineSectionBoundaries(view, allRows, controlsByRow, sectionHeadersByRow, repeatingSections);
            
            foreach (var row in allRows)
            {
                var rowInfo = new FormRowInfo
                {
                    RowNumber = row,
                    RowType = DetermineRowType(row, sectionHeadersByRow, controlsByRow, sectionBoundaries),
                    Controls = new List<GridControlInfo>()
                };
                
                // Add section header if exists
                if (sectionHeadersByRow.ContainsKey(row))
                {
                    rowInfo.SectionHeader = sectionHeadersByRow[row];
                    rowInfo.SectionInfo = sectionBoundaries.FirstOrDefault(s => s.StartRow <= row && s.EndRow >= row);
                }
                
                // Add regular controls with proper grid positioning
                if (controlsByRow.ContainsKey(row))
                {
                    foreach (var (control, col) in controlsByRow[row].OrderBy(c => c.col))
                    {
                        var gridInfo = new GridControlInfo
                        {
                            Control = control,
                            GridRow = row,
                            GridColumn = col,
                            NintexColumnStart = CalculateNintexColumn(col),
                            NintexColumnSpan = CalculateColumnSpan(control, controlsByRow[row])
                        };
                        rowInfo.Controls.Add(gridInfo);
                    }
                }
                
                rowInfos.Add(rowInfo);
            }
            
            return rowInfos;
        }

        private List<SectionInfo> DetermineSectionBoundaries(SourceView view, List<int> allRows,
            Dictionary<int, List<(SourceControl control, int col)>> controlsByRow,
            Dictionary<int, SourceControl> sectionHeadersByRow,
            Dictionary<string, List<SourceControl>> repeatingSections)
        {
            var sections = new List<SectionInfo>();
            
            // Use metadata if available
            if (view.Sections != null && view.Sections.Any())
            {
                foreach (var sourceSection in view.Sections.OrderBy(s => s.StartRow))
                {
                    var sectionType = DetermineDynamicSectionType(sourceSection, controlsByRow, repeatingSections);
                    sections.Add(new SectionInfo
                    {
                        Name = sourceSection.Name,
                        Type = sectionType,
                        StartRow = sourceSection.StartRow,
                        EndRow = sourceSection.EndRow
                    });
                }
            }
            else
            {
                // Fallback: analyze grid to infer sections
                sections = InferSectionsFromGrid(allRows, controlsByRow, sectionHeadersByRow, repeatingSections);
            }
            
            return sections;
        }

        private string DetermineDynamicSectionType(SourceSection sourceSection,
            Dictionary<int, List<(SourceControl control, int col)>> controlsByRow,
            Dictionary<string, List<SourceControl>> repeatingSections)
        {
            // Check if it's explicitly marked as repeating
            if (sourceSection.Type?.ToLower() == "repeating")
                return "repeating";
                
            // Check if there's a matching repeating section
            if (repeatingSections.ContainsKey(sourceSection.Name))
                return "repeating";

            // Analyze control density and types to determine if it should be a group
            var totalControls = 0;
            var totalRows = 0;
            
            for (int row = sourceSection.StartRow; row <= sourceSection.EndRow; row++)
            {
                if (controlsByRow.ContainsKey(row))
                {
                    totalControls += controlsByRow[row].Count;
                    totalRows++;
                }
            }

            // Consider it a group if it has multiple controls across multiple rows
            if (!string.IsNullOrEmpty(sourceSection.Name) && 
                !sourceSection.Name.Equals("miscellaneous", StringComparison.OrdinalIgnoreCase) &&
                totalControls >= 2 && totalRows >= 1)
            {
                return "group";
            }

            return "simple";
        }

        private List<SectionInfo> InferSectionsFromGrid(List<int> allRows,
            Dictionary<int, List<(SourceControl control, int col)>> controlsByRow,
            Dictionary<int, SourceControl> sectionHeadersByRow,
            Dictionary<string, List<SourceControl>> repeatingSections)
        {
            var sections = new List<SectionInfo>();
            
            if (!allRows.Any()) return sections;

            var currentSection = new SectionInfo { StartRow = allRows.First(), Type = "simple" };

            foreach (var row in allRows)
            {
                if (sectionHeadersByRow.ContainsKey(row) && currentSection.StartRow != row)
                {
                    // Finalize current section
                    currentSection.EndRow = row - 1;
                    if (currentSection.EndRow >= currentSection.StartRow)
                    {
                        sections.Add(currentSection);
                    }

                    // Start new section
                    var sectionName = sectionHeadersByRow[row].Label;
                    currentSection = new SectionInfo
                    {
                        Name = sectionName,
                        StartRow = row,
                        Type = repeatingSections.ContainsKey(sectionName) ? "repeating" : "group"
                    };
                }
                else if (sectionHeadersByRow.ContainsKey(row))
                {
                    currentSection.Name = sectionHeadersByRow[row].Label;
                }
            }

            // Finalize the last section
            currentSection.EndRow = allRows.Last();
            sections.Add(currentSection);

            return sections;
        }

        private string DetermineRowType(int row, 
            Dictionary<int, SourceControl> sectionHeadersByRow,
            Dictionary<int, List<(SourceControl control, int col)>> controlsByRow,
            List<SectionInfo> sectionBoundaries)
        {
            var currentSection = sectionBoundaries.FirstOrDefault(s => s.StartRow <= row && s.EndRow >= row);
            
            if (sectionHeadersByRow.ContainsKey(row))
            {
                if (currentSection?.Type == "group")
                    return "group_header";
                else if (currentSection?.Type == "repeating")
                    return "repeating_header";
                else
                    return "section_header";
            }
            
            if (currentSection?.Type == "group")
                return "group_content";
            else if (currentSection?.Type == "repeating")
                return "repeating_content";
            else
                return "simple";
        }

        private void ProcessFormRow(FormRowInfo rowInfo, FormDefinition formDefinition, SourceView view)
        {
            switch (rowInfo.RowType)
            {
                case "group_header":
                    // Group headers are processed when we create the group
                    break;
                    
                case "group_content":
                    // Group content is processed when we create the group
                    ProcessGroupRow(rowInfo, formDefinition);
                    break;
                    
                case "repeating_header":
                    // This is a section header for a repeating section - process the whole section
                    ProcessRepeatingRow(rowInfo, formDefinition, view);
                    break;
                    
                case "repeating_content":
                    // Repeating content rows are processed as part of the repeating section template
                    // Skip individual processing
                    break;
                    
                case "section_header":
                    ProcessSectionHeaderRow(rowInfo, formDefinition);
                    break;
                    
                default:
                    // Check if this row is part of a repeating section
                    if (rowInfo.SectionInfo?.Type == "repeating")
                    {
                        // Skip - repeating sections are processed as complete units
                        break;
                    }
                    
                    // Filter out repeating section controls for regular row processing
                    var nonRepeatingControls = rowInfo.Controls.Where(c => c.Control.RepeatingSectionInfo?.IsInRepeatingSection != true).ToList();
                    if (nonRepeatingControls.Any())
                    {
                        var filteredRowInfo = new FormRowInfo
                        {
                            RowNumber = rowInfo.RowNumber,
                            RowType = rowInfo.RowType,
                            Controls = nonRepeatingControls,
                            SectionHeader = rowInfo.SectionHeader,
                            SectionInfo = rowInfo.SectionInfo
                        };
                        ProcessSimpleRow(filteredRowInfo, formDefinition);
                    }
                    break;
            }
        }

        private int CalculateNintexColumn(int sourceColumn)
        {
            // Convert source column (A=0, B=1, C=2...) to Nintex 12-column grid position
            // This is a simple mapping - can be made more sophisticated based on actual column widths
            return sourceColumn * 3; // Each source column takes 3 Nintex columns
        }

        private int CalculateColumnSpan(SourceControl control, List<(SourceControl control, int col)> rowControls)
        {
            // Calculate how many Nintex columns this control should span
            var totalControls = rowControls.Count;
            if (totalControls == 1) return 12;
            if (totalControls == 2) return 6;
            if (totalControls == 3) return 4;
            if (totalControls == 4) return 3;
            return Math.Max(1, 12 / totalControls);
        }

        private void ProcessSimpleRow(FormRowInfo rowInfo, FormDefinition formDefinition)
        {
            if (!rowInfo.Controls.Any()) return;

                var row = new Row
                {
                    Controls = new List<Control>(),
                    PageName = "page_default",
                    Id = GenerateId()
                };

            foreach (var gridControl in rowInfo.Controls)
                {
                var control = ConvertControl(gridControl.Control, formDefinition.Translations["en"]);
                    if (control != null)
                    {
                        row.Controls.Add(control);
                    }
                }

                if (row.Controls.Any())
                {
                row.Sizes = CalculateGridBasedSizes(rowInfo.Controls);
                    formDefinition.Rows.Add(row);
                }
            }

        private void ProcessSectionHeaderRow(FormRowInfo rowInfo, FormDefinition formDefinition)
        {
            if (rowInfo.SectionHeader == null) return;

                    var labelRow = new Row
                    {
                        Controls = new List<Control>(),
                        PageName = "page_default",
                        Id = GenerateId()
                    };

                    var sectionHeaderId = GenerateControlId();
                    var sectionHeader = new Control
                    {
                        Id = sectionHeaderId,
                        DataType = "string",
                        Widget = "richtext-label",
                        Source = new Source { CreatedBy = "user" },
                        Properties = new ControlProperties
                        {
                    Name = rowInfo.SectionHeader.Name,
                            Title = $"{sectionHeaderId}.FORMDESIGNER_CONTROL_PROP_TITLE",
                            Format = "",
                    Text = $"{sectionHeaderId}.FORMDESIGNER_CONTROL_PROP_TEXT",
                    IsConnectedToVariable = true,
                    ConnectedVariableId = GenerateVariableId(rowInfo.SectionHeader.Name)
                        }
                    };
                    
            formDefinition.Translations["en"][$"{sectionHeaderId}.FORMDESIGNER_CONTROL_PROP_TITLE"] = "";
            formDefinition.Translations["en"][$"{sectionHeaderId}.FORMDESIGNER_CONTROL_PROP_TEXT"] = rowInfo.SectionHeader.Label;
                    
                    labelRow.Controls.Add(sectionHeader);
                    formDefinition.Rows.Add(labelRow);
                }

        private void ProcessGroupRow(FormRowInfo rowInfo, FormDefinition formDefinition)
        {
            // Groups are handled differently - we need to collect all group rows and process them together
            // This method will be called but the actual group processing happens in ProcessGroupSection
            // For now, we'll defer to ProcessGroupSection when we encounter the first group content row
            
            if (rowInfo.SectionInfo != null && !_processedGroups.Contains(rowInfo.SectionInfo.Name))
            {
                ProcessGroupSection(rowInfo.SectionInfo, formDefinition);
                _processedGroups.Add(rowInfo.SectionInfo.Name);
            }
        }

        private void ProcessRepeatingRow(FormRowInfo rowInfo, FormDefinition formDefinition, SourceView view)
        {
            // Similar to groups, repeating sections need to be processed as a whole
            if (rowInfo.SectionInfo != null && !_processedRepeatingSections.Contains(rowInfo.SectionInfo.Name))
            {
                ProcessRepeatingSection(rowInfo.SectionInfo, formDefinition, view);
                _processedRepeatingSections.Add(rowInfo.SectionInfo.Name);
            }
        }

        private readonly HashSet<string> _processedGroups = new();
        private readonly HashSet<string> _processedRepeatingSections = new();

        private void ProcessGroupSection(SectionInfo section, FormDefinition formDefinition)
        {
            // Create group control with all its content
            var groupId = GenerateControlId();
            var groupRow = new Row
            {
                Controls = new List<Control>(),
                PageName = "page_default",
                Id = GenerateId()
            };
            
            var groupControl = new Control
            {
                Id = groupId,
                Widget = "group-control",
                WidgetMinimumSize = 6,
                Source = new Source { CreatedBy = "user" },
                Properties = new ControlProperties
                {
                    Name = section.Name,
                    Title = $"{groupId}.FORMDESIGNER_CONTROL_PROP_TITLE",
                    HeaderText = $"{groupId}.FORMDESIGNER_CONTROL_PROP_HEADER_TEXT",
                    ShowHeader = true,
                    ShowBorder = true,
                    TranslateHeader = false,
                    IsNestedControl = true,
                    ShowHeaderBackground = false,
                    ShowHeaderDivider = true,
                    ShowExpandable = false,
                    CollapsedByDefault = false,
                    ShowOnlyHeaderDivider = false,
                    IsConnectedToVariable = true,
                    ConnectedVariableId = GenerateVariableId(section.Name)
                },
                SourceVariable = new SourceVariable
                {
                    DisplayName = section.Name,
                    AutoGenerateName = true
                }
            };
            
            // Add translations
            formDefinition.Translations["en"][$"{groupId}.FORMDESIGNER_CONTROL_PROP_TITLE"] = section.Name;
            formDefinition.Translations["en"][$"{groupId}.FORMDESIGNER_CONTROL_PROP_HEADER_TEXT"] = $"<p>{section.Name}</p>";
            
            // Build group content from _currentGridAnalysis
            var groupRows = BuildGroupContent(section, formDefinition);
            groupControl.Properties.Rows = groupRows.ToArray();
            
            groupRow.Controls.Add(groupControl);
            formDefinition.Rows.Add(groupRow);
        }

        private void ProcessRepeatingSection(SectionInfo section, FormDefinition formDefinition, SourceView view)
        {
            // Add section label first
            var labelRow = new Row
            {
                Controls = new List<Control>(),
                PageName = "page_default",
                Id = GenerateId()
            };

            var labelId = GenerateControlId();
            var label = new Control
            {
                Id = labelId,
                DataType = "string",
                Widget = "richtext-label",
                Source = new Source { CreatedBy = "user" },
                Properties = new ControlProperties
                {
                    Name = $"{section.Name.ToUpper()}_LABEL",
                    Title = $"{labelId}.FORMDESIGNER_CONTROL_PROP_TITLE",
                    Format = "",
                    Text = $"{labelId}.FORMDESIGNER_CONTROL_PROP_TEXT",
                    IsConnectedToVariable = true,
                    ConnectedVariableId = GenerateVariableId($"{section.Name}_LABEL")
                }
            };
            
            formDefinition.Translations["en"][$"{labelId}.FORMDESIGNER_CONTROL_PROP_TITLE"] = "";
            formDefinition.Translations["en"][$"{labelId}.FORMDESIGNER_CONTROL_PROP_TEXT"] = section.Name;
            
            labelRow.Controls.Add(label);
            formDefinition.Rows.Add(labelRow);
            
            // Create repeating section control
            var virtualRepeatingTable = new SourceControl
            {
                Name = $"{section.Name.ToUpper()}_REPEATING_SECTION",
                Label = section.Name,
                Type = "RepeatingTable",
                GridPosition = $"{section.StartRow}A"
            };
            
            var row = new Row
            {
                Controls = new List<Control>(),
                PageName = "page_default",
                Id = GenerateId()
            };

            var repeatingSectionControl = ConvertControl(virtualRepeatingTable, formDefinition.Translations["en"]);
                if (repeatingSectionControl != null)
                {
                // Get the actual repeating section controls from the stored data
                var repeatingSectionControls = _currentRepeatingSections.ContainsKey(section.Name) 
                    ? _currentRepeatingSections[section.Name] 
                    : new List<SourceControl>();
                    
                var templateFormJson = BuildRepeatingTableTemplateFromControls(repeatingSectionControls, repeatingSectionControl.Id, section.Name, formDefinition.Translations["en"]);
                repeatingSectionControl.Properties.TemplateFormJson = templateFormJson;
                
                row.Controls.Add(repeatingSectionControl);
                formDefinition.Rows.Add(row);
            }
        }

        private List<object> BuildGroupContent(SectionInfo section, FormDefinition formDefinition)
        {
            var groupRows = new List<object>();
            var sectionRowInfos = _currentGridAnalysis.Where(r => r.RowNumber >= section.StartRow && r.RowNumber <= section.EndRow && r.Controls.Any()).ToList();
            
            foreach (var rowInfo in sectionRowInfos)
            {
                var groupRowControls = new List<object>();
                
                foreach (var gridControl in rowInfo.Controls)
                {
                    var control = ConvertControl(gridControl.Control, formDefinition.Translations["en"]);
                    if (control != null)
                    {
                        var groupRowControl = new
                        {
                            id = control.Id,
                            dataType = control.DataType,
                            widget = control.Widget,
                            widgetMinimumSize = control.WidgetMinimumSize,
                            source = control.Source,
                            properties = control.Properties,
                            sourceVariable = control.SourceVariable
                        };
                        groupRowControls.Add(groupRowControl);
                    }
                }
                
                if (groupRowControls.Any())
                {
                    var sizes = CalculateGridBasedSizes(rowInfo.Controls);
                    var groupRowObj = new
                    {
                        controls = groupRowControls.ToArray(),
                        sizes = sizes,
                        autoResizing = true,
                        restrictions = new object(),
                        pageName = "page_default",
                        containerName = "",
                        id = GenerateId()
                    };
                    groupRows.Add(groupRowObj);
                }
            }
            
            return groupRows;
        }

        private string BuildRepeatingTableTemplateFromSection(SectionInfo section, string containerId, Dictionary<string, string> translations)
        {
                        var templateRows = new List<object>();
            var sectionRowInfos = _currentGridAnalysis.Where(r => r.RowNumber >= section.StartRow && r.RowNumber <= section.EndRow && r.Controls.Any()).ToList();
            
            foreach (var rowInfo in sectionRowInfos)
            {
                        var templateControls = new List<object>();
                
                foreach (var gridControl in rowInfo.Controls)
                {
                    var innerControl = ConvertControl(gridControl.Control, translations);
                    if (innerControl != null)
                    {
                        var templateControl = new
                        {
                            id = innerControl.Id,
                            dataType = innerControl.DataType,
                            widget = innerControl.Widget,
                            widgetMinimumSize = innerControl.WidgetMinimumSize,
                            source = innerControl.Source,
                            properties = innerControl.Properties,
                            sourceVariable = innerControl.SourceVariable
                        };
                        
                        templateControls.Add(templateControl);
                    }
                }
                
                if (templateControls.Any())
                {
                    var templateSizes = CalculateGridBasedSizes(rowInfo.Controls);
                    var templateRow = new
                    {
                        controls = templateControls.ToArray(),
                        sizes = templateSizes,
                        autoResizing = true,
                        restrictions = new object(),
                        pageName = "page_default",
                        containerName = containerId,
                        id = GenerateId()
                    };
                    
                    templateRows.Add(templateRow);
                }
            }
            
            var templateForm = new
            {
                id = containerId,
                name = section.Name,
                ruleGroups = new object[0],
                version = 26,
                // Ensure embedded template has a variable context to satisfy importer
                variableContext = new { variables = Array.Empty<object>() },
                rows = templateRows.ToArray()
            };
            
            return Newtonsoft.Json.JsonConvert.SerializeObject(templateForm);
        }

        private List<int> CalculateGridBasedSizes(List<GridControlInfo> gridControls)
        {
            var sizes = new List<int>();
            var totalControls = gridControls.Count;
            
            if (totalControls == 1)
            {
                sizes.Add(12);
            }
            else if (totalControls == 2)
            {
                sizes.AddRange(new[] { 6, 6 });
            }
            else if (totalControls == 3)
            {
                sizes.AddRange(new[] { 4, 4, 4 });
            }
            else if (totalControls == 4)
            {
                sizes.AddRange(new[] { 3, 3, 3, 3 });
            }
            else
            {
                var sizePerControl = Math.Max(1, 12 / totalControls);
                for (int i = 0; i < totalControls; i++)
                {
                    sizes.Add(sizePerControl);
                }
            }
            
            return sizes;
        }

        // Store current grid analysis for use across methods
        private List<FormRowInfo> _currentGridAnalysis = new();
        private Dictionary<string, List<SourceControl>> _currentRepeatingSections = new();

        private string BuildRepeatingTableTemplateFromControls(List<SourceControl> sectionControls, string containerId, string sectionName, Dictionary<string, string> translations)
        {
            var sectionControlsByRow = new Dictionary<int, List<SourceControl>>();
            var sectionLabelsByName = new Dictionary<string, SourceControl>();
            
            // First pass: collect labels
                        foreach (var sectionControl in sectionControls)
                        {
                if (sectionControl.Type == "Label" && string.IsNullOrEmpty(sectionControl.Binding))
                {
                    sectionLabelsByName[sectionControl.Name] = sectionControl;
                }
            }
            
            // Second pass: organize controls by row and apply labels
            foreach (var sectionControl in sectionControls)
            {
                            if (sectionControl.Type == "Label" && string.IsNullOrEmpty(sectionControl.Binding))
                                continue;
                            
                            if (sectionControl.Name.Contains("FILTER", StringComparison.OrdinalIgnoreCase))
                                continue;
                                
                if (sectionControl.Type == "span")
                    continue;
                    
                if (sectionLabelsByName.TryGetValue(sectionControl.Name, out var correspondingLabel))
                {
                    sectionControl.Label = correspondingLabel.Label;
                }
                    
                var (sectionRow, sectionCol) = ParseGridPosition(sectionControl.GridPosition);
                if (sectionRow > 0)
                {
                    if (!sectionControlsByRow.ContainsKey(sectionRow))
                    {
                        sectionControlsByRow[sectionRow] = new List<SourceControl>();
                    }
                    sectionControlsByRow[sectionRow].Add(sectionControl);
                }
            }
            
            var templateRows = new List<object>();
            
            foreach (var rowNum in sectionControlsByRow.Keys.OrderBy(k => k))
            {
                var rowControls = sectionControlsByRow[rowNum];
                var templateControls = new List<object>();
                
                foreach (var sectionControl in rowControls)
                {
                    var innerControl = ConvertControl(sectionControl, translations);
                            if (innerControl != null)
                            {
                                var templateControl = new
                                {
                                    id = innerControl.Id,
                                    dataType = innerControl.DataType,
                                    widget = innerControl.Widget,
                                    widgetMinimumSize = innerControl.WidgetMinimumSize,
                                    source = innerControl.Source,
                                    properties = innerControl.Properties,
                                    sourceVariable = innerControl.SourceVariable
                                };
                                
                                templateControls.Add(templateControl);
                            }
                        }
                        
                        if (templateControls.Any())
                        {
                    var templateSizes = CalculateRowSizes(templateControls.Count);
                            var templateRow = new
                            {
                                controls = templateControls.ToArray(),
                        sizes = templateSizes,
                                autoResizing = true,
                                restrictions = new object(),
                                pageName = "page_default",
                        containerName = containerId,
                                id = GenerateId()
                            };
                            
                            templateRows.Add(templateRow);
                }
                        }
                        
                        var templateForm = new
                        {
                id = containerId,
                name = sectionName,
                            ruleGroups = new object[0],
                            version = 26,
                            rows = templateRows.ToArray()
                        };
                        
            return Newtonsoft.Json.JsonConvert.SerializeObject(templateForm);
        }

                private string BuildRepeatingTableTemplate(List<SourceControl> sectionControls, string containerId, string sectionName, Dictionary<string, string> translations)
        {
            var sectionControlsByRow = new Dictionary<int, List<SourceControl>>();
            var sectionLabelsByName = new Dictionary<string, SourceControl>();

            // First pass: collect labels
            foreach (var sectionControl in sectionControls)
            {
                if (sectionControl.Type == "Label" && string.IsNullOrEmpty(sectionControl.Binding))
                {
                    sectionLabelsByName[sectionControl.Name] = sectionControl;
                }
            }
            
            // Second pass: process controls and filter out unwanted ones
            foreach (var sectionControl in sectionControls)
            {
                // Skip labels (they're used for control titles)
                if (sectionControl.Type == "Label" && string.IsNullOrEmpty(sectionControl.Binding))
                    continue;

                // KEY FIX: Filter out FILTER controls (from summary views)
                if (sectionControl.Name.Contains("FILTER", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Filter out span controls
                if (sectionControl.Type == "span")
                    continue;

                // Apply label text to control
                if (sectionLabelsByName.TryGetValue(sectionControl.Name, out var correspondingLabel))
                {
                    sectionControl.Label = correspondingLabel.Label;
                }

                var (sectionRow, sectionCol) = ParseGridPosition(sectionControl.GridPosition);
                if (sectionRow > 0)
                {
                    if (!sectionControlsByRow.ContainsKey(sectionRow))
                    {
                        sectionControlsByRow[sectionRow] = new List<SourceControl>();
                    }
                    sectionControlsByRow[sectionRow].Add(sectionControl);
                }
            }
            
            var templateRows = new List<object>();
            
            foreach (var rowNum in sectionControlsByRow.Keys.OrderBy(k => k))
            {
                var rowControls = sectionControlsByRow[rowNum];
                var templateControls = new List<object>();
                
                foreach (var sectionControl in rowControls)
                {
                    var innerControl = ConvertControl(sectionControl, translations);
                    if (innerControl != null)
                    {
                        var templateControl = new
                        {
                            id = innerControl.Id,
                            dataType = innerControl.DataType,
                            widget = innerControl.Widget,
                            widgetMinimumSize = innerControl.WidgetMinimumSize,
                            source = innerControl.Source,
                            properties = innerControl.Properties,
                            sourceVariable = innerControl.SourceVariable
                        };
                        
                        templateControls.Add(templateControl);
                    }
                }
                
                if (templateControls.Any())
                {
                    var templateSizes = CalculateRowSizes(templateControls.Count);
                    var templateRow = new
                    {
                        controls = templateControls.ToArray(),
                        sizes = templateSizes,
                        autoResizing = true,
                        restrictions = new object(),
                        pageName = "page_default",
                        containerName = containerId,
                        id = GenerateId()
                    };
                    
                    templateRows.Add(templateRow);
                }
            }
            
            var templateForm = new
            {
                id = containerId,
                name = sectionName,
                ruleGroups = new object[0],
                version = 26,
                rows = templateRows.ToArray()
            };

            return Newtonsoft.Json.JsonConvert.SerializeObject(templateForm);
        }

        // Helper classes for grid-based processing
        private class FormRowInfo
        {
            public int RowNumber { get; set; }
            public string RowType { get; set; } = "simple";
            public List<GridControlInfo> Controls { get; set; } = new();
            public SourceControl? SectionHeader { get; set; }
            public SectionInfo? SectionInfo { get; set; }
        }
        
        private class GridControlInfo
        {
            public SourceControl Control { get; set; } = new();
            public int GridRow { get; set; }
            public int GridColumn { get; set; }
            public int NintexColumnStart { get; set; }
            public int NintexColumnSpan { get; set; }
        }

        // Helper class for section information
        private class SectionInfo
        {
            public string Name { get; set; } = "";
            public string Type { get; set; } = "simple";
            public int StartRow { get; set; }
            public int EndRow { get; set; }
        }

        private List<int> CalculateRowSizes(int controlCount)
        {
            var sizes = new List<int>();
            
            if (controlCount == 1)
            {
                sizes.Add(12);
            }
            else if (controlCount == 2)
            {
                sizes.AddRange(new[] { 6, 6 });
            }
            else if (controlCount == 3)
            {
                sizes.AddRange(new[] { 4, 4, 4 });
            }
            else
            {
                var sizePerControl = 12 / controlCount;
                for (int i = 0; i < controlCount; i++)
                {
                    sizes.Add(sizePerControl);
                }
            }
            
            return sizes;
        }

        private Control? ConvertControl(SourceControl sourceControl, Dictionary<string, string> translations)
        {
            var controlId = GenerateControlId();
            var controlName = GetControlName(sourceControl);
            var widget = GetWidgetType(sourceControl.Type);
            var dataType = GetDataType(sourceControl.Type);

            if (widget == "unknown")
            {
                return null; // Skip unsupported controls
            }

            var control = new Control
            {
                Id = controlId,
                DataType = dataType,
                Widget = widget,
                WidgetMinimumSize = GetWidgetMinimumSize(widget),
                Source = new Source { CreatedBy = "user" },
                Properties = new ControlProperties
                {
                    Name = controlName,
                    Title = $"{controlId}.FORMDESIGNER_CONTROL_PROP_TITLE",
                    Format = GetFormat(sourceControl.Type)
                },
                SourceVariable = new SourceVariable
                {
                    DisplayName = controlName,
                    AutoGenerateName = true
                }
            };

            // Data controls need variables (field labels are no longer processed as separate controls)
            control.Properties.ConnectedVariableId = GenerateVariableId(controlName);
            control.Properties.IsConnectedToVariable = true;

            // Add translations
            translations[$"{controlId}.FORMDESIGNER_CONTROL_PROP_TITLE"] = sourceControl.Label;
            translations[$"{controlId}.FORMDESIGNER_CONTROL_PROP_DESCRIPTION"] = "";
            translations[$"{controlId}.FORMDESIGNER_CONTROL_PROP_TOOLTIP"] = "";
            translations[$"{controlId}.FORMDESIGNER_CONTROL_PROP_REQUIRED_ERROR_MESSAGE"] = "";

            // Handle specific control types
            HandleSpecificControlTypes(sourceControl, control, translations);

            return control;
        }

        private void HandleSpecificControlTypes(SourceControl sourceControl, Control control, Dictionary<string, string> translations)
        {
            switch (sourceControl.Type.ToLower())
            {
                case "dropdown":
                    HandleDropdownControl(sourceControl, control, translations);
                    break;
                case "choice":
                    HandleChoiceControl(sourceControl, control, translations);
                    break;
                case "checkbox":
                    HandleCheckboxControl(sourceControl, control, translations);
                    break;
                case "datepicker":
                    HandleDatePickerControl(sourceControl, control, translations);
                    break;
                case "richtext":
                    HandleRichTextControl(sourceControl, control, translations);
                    break;
                case "repeatingtable":
                    HandleRepeatingTableControl(sourceControl, control, translations);
                    break;
            }
        }

        private void HandleChoiceControl(SourceControl sourceControl, Control control, Dictionary<string, string> translations)
        {
            // For radio buttons, use choice widget with viewType = 0
            control.Properties.ViewType = "0"; // Radio button view
            control.Properties.LayoutType = "0";
            
            if (sourceControl.DataOptions != null && sourceControl.DataOptions.Any())
            {
                var options = string.Join("\n", sourceControl.DataOptions.Select(o => o.DisplayText));
                control.Properties.Items = options;
                translations[$"{control.Id}.FORMDESIGNER_CONTROL_PROP_CHOICE_OPTIONS_INSERT_LABEL"] = options;
                translations[$"{control.Id}.FORMDESIGNER_CONTROL_PROP_CHOICE_FILLIN_PLACEHOLDER"] = "Other";
            }
        }

        private void HandleDropdownControl(SourceControl sourceControl, Control control, Dictionary<string, string> translations)
        {
            // For dropdown, use choice widget with viewType = 1
            control.Widget = "choice";
            control.Properties.ViewType = "1"; // Dropdown view
            control.Properties.LayoutType = "0";
            control.Properties.ShowPleaseSelect = false;
            control.Properties.Searchable = true;
            
            if (sourceControl.DataOptions != null && sourceControl.DataOptions.Any())
            {
                var options = string.Join("\n", sourceControl.DataOptions.Select(o => o.DisplayText));
                control.Properties.Items = options;
                translations[$"{control.Id}.FORMDESIGNER_CONTROL_PROP_CHOICE_OPTIONS_INSERT_LABEL"] = options;
                translations[$"{control.Id}.FORMDESIGNER_CONTROL_PROP_CHOICE_FILLIN_PLACEHOLDER"] = "Select...";
            }
        }

        private void HandleCheckboxControl(SourceControl sourceControl, Control control, Dictionary<string, string> translations)
        {
            control.Properties.Format = "";
            control.DataType = "boolean";
        }

        private void HandleDatePickerControl(SourceControl sourceControl, Control control, Dictionary<string, string> translations)
        {
            control.Properties.Format = "date-time";
            control.Properties.ShowDateOnly = true;
            control.Properties.ShowSetTimeZone = false;
            control.Properties.DateTimeZone = "UTC";
            control.Properties.RestrictPastDates = false;
        }

        private void HandleRichTextControl(SourceControl sourceControl, Control control, Dictionary<string, string> translations)
        {
            control.Properties.Format = "multiline-plain";
            control.Properties.Type = "Plain";
            control.Properties.TextAreaRows = "7";
            control.Properties.AutoResize = true;
        }

        private void HandleRepeatingTableControl(SourceControl sourceControl, Control control, Dictionary<string, string> translations)
        {
            control.Properties.Format = "repeating-section";
            control.Properties.ShowHeader = false;
            control.Properties.ShowBorder = true;
            control.Properties.TranslateHeader = false;
            control.Properties.IsNestedControl = true;
            control.Properties.ShowHeaderBackground = false;
            control.Properties.ShowHeaderDivider = false;
            control.Properties.ShowExpandable = false;
            control.Properties.CollapsedByDefault = false;
            control.Properties.ShowOnlyHeaderDivider = false;
            control.Properties.AddRowButtonLabel = $"{control.Id}.FORMDESIGNER_CONTROL_REPEATING_SECTION_PROP_NEW_BUTTON_LABEL";
            control.Properties.MinRows = 1;
            control.Properties.MaxRows = 50;
            control.Properties.DefaultRows = 1;
            control.Properties.AlternateBackgroundColour = true;
            control.Properties.RepeatingSectionDefaultValueType = "collection";
            control.Properties.RepeatingSectionDefaultValue = "";
            control.Properties.RepeatingSectionJsonDefaultValue = new object[0];

            // Set templateForm object
            control.Properties.TemplateForm = new
            {
                id = control.Id,
                name = sourceControl.Label,
                ruleGroups = new object[0],
                version = 26,
                variableContext = new { variables = Array.Empty<object>() }
            };

            translations[$"{control.Id}.FORMDESIGNER_CONTROL_REPEATING_SECTION_PROP_NEW_BUTTON_LABEL"] = "Add new row";
        }

        private void AddActionPanel(FormDefinition formDefinition)
        {
            var actionPanelRow = new Row
            {
                Controls = new List<Control>
                {
                    new Control
                    {
                        Id = "actionpanel1",
                        Widget = "actionpanel",
                        Source = new Source { CreatedBy = "user" },
                        Properties = new ControlProperties
                        {
                            Name = "FORMDESIGNER_ACTIONPANEL_TEXT_ALT",
                            CaptchaEnabled = false,
                            ShowHeader = false,
                            ShowSubmitButton = true,
                            ShowSaveAndContinueButton = false,
                            IsCancelRedirectUrlEnabled = false,
                            ShowAfterSubmitPrintButton = false,
                            AfterSubmitPrintButtonLabel = "actionpanel1.FORMDESIGNER_CONTROL_PROP_PRINT_BUTTON_LABEL",
                            SubmitButtonText = "actionpanel1.FORMDESIGNER_ACTIONPANEL_SUBMIT_TEXT_TITLE",
                            AfterSubmitType = "0",
                            NextButtonText = "actionpanel1.FORMDESIGNER_ACTIONPANEL_NEXT_TEXT_TITLE",
                            PreviousButtonText = "actionpanel1.FORMDESIGNER_ACTIONPANEL_PREVIOUS_TEXT_TITLE",
                            SaveAndContinueButtonText = "actionpanel1.FORMDESIGNER_ACTIONPANEL_SAVE_CONTINUE_TEXT_TITLE",
                            CancelButtonText = "actionpanel1.FORMDESIGNER_ACTIONPANEL_CANCEL_TEXT_TITLE"
                        },
                        SourceVariable = new SourceVariable
                        {
                            DisplayName = "FORMDESIGNER_ACTIONPANEL_TEXT_ALT",
                            AutoGenerateName = true
                        }
                    }
                },
                PageName = "page_default",
                Id = GenerateId()
            };

            formDefinition.Rows.Add(actionPanelRow);
        }

        private void AddDefaultTranslations(Dictionary<string, string> translations)
        {
            translations["actionpanel1.FORMDESIGNER_CONTROL_PROP_PRINT_BUTTON_LABEL"] = "Print";
            translations["actionpanel1.FORMDESIGNER_ACTIONPANEL_SUBMIT_TEXT_TITLE"] = "Submit";
            translations["actionpanel1.FORMDESIGNER_ACTIONPANEL_NEXT_TEXT_TITLE"] = "Next";
            translations["actionpanel1.FORMDESIGNER_ACTIONPANEL_PREVIOUS_TEXT_TITLE"] = "Previous";
            translations["actionpanel1.FORMDESIGNER_ACTIONPANEL_SAVE_CONTINUE_TEXT_TITLE"] = "Save";
            translations["actionpanel1.FORMDESIGNER_ACTIONPANEL_CANCEL_TEXT_TITLE"] = "Cancel";
            translations["page_default.FORMDESIGNER_CONTROL_PROP_TITLE"] = "Page 1";
            translations["multiPage.FORMDESIGNER_CONTROL_PROP_HEADER_TEXT"] = "";
            translations["multiPage.FORMDESIGNER_CONTROL_PROP_FOOTER_TEXT"] = "";
        }

        private string GenerateId()
        {
            return $"_{Guid.NewGuid().ToString("N")[..8]}";
        }

        private string GenerateControlId()
        {
            return $"_{Guid.NewGuid().ToString("N")[..8]}";
        }

        private string GenerateVariableId(string controlName)
        {
            // Remove all special characters and spaces, keep only alphanumeric and underscores
            var cleanName = System.Text.RegularExpressions.Regex.Replace(controlName, @"[^a-zA-Z0-9_]", "_")
                .Replace("__", "_") // Replace double underscores with single
                .Trim('_') // Remove leading/trailing underscores
                .ToLower();
            
            // Ensure it starts with a letter
            if (!char.IsLetter(cleanName.FirstOrDefault()))
            {
                cleanName = $"field_{cleanName}";
            }
            
            var baseVariableName = $"se_{cleanName}";
            
            // Ensure uniqueness
            var counter = 1;
            var variableName = baseVariableName;
            while (_usedVariableNames.Contains(variableName))
            {
                variableName = $"{baseVariableName}_{counter}";
                counter++;
            }
            
            _usedVariableNames.Add(variableName);
            var suffix = Guid.NewGuid().ToString("N")[..8];
            return $"{variableName}_{suffix}";
        }

        private string GetControlName(SourceControl sourceControl)
        {
            if (!string.IsNullOrEmpty(sourceControl.Label))
            {
                return sourceControl.Label.Replace(":", "").Trim();
            }
            return sourceControl.Name;
        }

        private Dictionary<string, string> InitializeControlTypeMapping()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["TextField"] = "textbox",
                ["TextArea"] = "multilinetext",
                ["RichText"] = "multilinetext",
                ["DatePicker"] = "datetime",
                ["DropDown"] = "choice",
                ["Choice"] = "choice",
                ["CheckBox"] = "boolean",
                ["Number"] = "number",
                ["Currency"] = "currency",
                ["Email"] = "email",
                ["FileUpload"] = "file-upload",
                ["Signature"] = "signature",
                ["PeoplePicker"] = "people-picker-core",
                ["RepeatingTable"] = "repeating-section",
                ["Group"] = "group-control",
                ["Image"] = "image",
                ["Barcode"] = "barcode",
                ["Geolocation"] = "geolocation",
                ["DataLookup"] = "data-lookup",
                ["Button"] = "button",
                ["Label"] = "richtext-label",
                ["Space"] = "space"
            };
        }

        private Dictionary<string, string> InitializeWidgetMapping()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["TextField"] = "textbox",
                ["TextArea"] = "multilinetext",
                ["RichText"] = "multilinetext",
                ["DatePicker"] = "datetime",
                ["DropDown"] = "choice",
                ["Choice"] = "choice",
                ["CheckBox"] = "boolean",
                ["Number"] = "number",
                ["Currency"] = "currency",
                ["Email"] = "email",
                ["FileUpload"] = "file-upload",
                ["Signature"] = "signature",
                ["PeoplePicker"] = "people-picker-core",
                ["RepeatingTable"] = "repeating-section",
                ["Group"] = "group-control",
                ["Image"] = "image",
                ["Barcode"] = "barcode",
                ["Geolocation"] = "geolocation",
                ["DataLookup"] = "data-lookup",
                ["Button"] = "button",
                ["Label"] = "richtext-label",
                ["Space"] = "space"
            };
        }

        private Dictionary<string, string> InitializeDataTypeMapping()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["TextField"] = "string",
                ["TextArea"] = "string",
                ["RichText"] = "string",
                ["DatePicker"] = "string",
                ["DropDown"] = "string",
                ["Choice"] = "string",
                ["CheckBox"] = "boolean",
                ["Number"] = "number",
                ["Currency"] = "number",
                ["Email"] = "string",
                ["FileUpload"] = "array",
                ["Signature"] = "string",
                ["PeoplePicker"] = "array",
                ["RepeatingTable"] = "object",
                ["Group"] = "object",
                ["Image"] = "string",
                ["Barcode"] = "string",
                ["Geolocation"] = "string",
                ["DataLookup"] = "string",
                ["Button"] = "boolean",
                ["Label"] = "string",
                ["Space"] = "object"
            };
        }

        private string GetWidgetType(string sourceType)
        {
            return _widgetMapping.TryGetValue(sourceType, out var widget) ? widget : "unknown";
        }

        private string GetDataType(string sourceType)
        {
            return _dataTypeMapping.TryGetValue(sourceType, out var dataType) ? dataType : "string";
        }

        private string GetFormat(string sourceType)
        {
            return sourceType.ToLower() switch
            {
                "currency" => "currency",
                "datepicker" => "date-time",
                "richtext" => "multiline-plain",
                "fileupload" => "x-ntx-file-reference",
                "signature" => "x-ntx-file-reference",
                "peoplepicker" => "x-ntx-people",
                "repeatingtable" => "repeating-section",
                "number" => "integer",
                _ => ""
            };
        }

        private int? GetWidgetMinimumSize(string widget)
        {
            return widget switch
            {
                "textbox" => 3,
                "multilinetext" => 6,
                "datetime" => 3,
                "choice" => 3,
                "number" => 3,
                "currency" => 3,
                "email" => 3,
                "file-upload" => 6,
                "signature" => 6,
                "people-picker-core" => 3,
                "repeating-section" => 6,
                "group-control" => 6,
                "image" => 3,
                "barcode" => 3,
                "geolocation" => 3,
                "data-lookup" => 3,
                "button" => null,
                "richtext-label" => null,
                "space" => null,
                _ => null
            };
        }

        private (int row, int col) ParseGridPosition(string gridPosition)
        {
            if (string.IsNullOrEmpty(gridPosition))
                return (0, 0);

            // Parse positions like "2B", "10A", etc.
            var match = System.Text.RegularExpressions.Regex.Match(gridPosition, @"^(\d+)([A-Z])$");
            if (match.Success)
            {
                var row = int.Parse(match.Groups[1].Value);
                var col = match.Groups[2].Value[0] - 'A'; // Convert A=0, B=1, C=2, etc.
                return (row, col);
            }

            return (0, 0);
        }
    }
}