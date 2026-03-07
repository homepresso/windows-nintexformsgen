using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using FormGenerator.Analyzers.Infopath;
using FormGenerator.Core.Models;
using FormGenerator.Services;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace FormGenerator.Views
{
    /// <summary>
    /// Handles Rules Mapping tab functionality
    /// </summary>
    internal class MainWindowRulesMappingHandlers
    {
        private readonly MainWindow _mainWindow;
        private readonly RuleMappingService _ruleMappingService;

        private List<RuleMappingItem> _allRuleMappings = new List<RuleMappingItem>();
        private List<RuleMappingItem> _filteredRuleMappings = new List<RuleMappingItem>();
        private RuleMappingItem _selectedMapping;

        public MainWindowRulesMappingHandlers(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            _ruleMappingService = new RuleMappingService();
        }

        /// <summary>
        /// Populates the rules mapping tab with analyzed rules
        /// </summary>
        public void PopulateRulesMappings(Dictionary<string, InfoPathFormDefinition> formDefinitions)
        {
            if (formDefinitions == null || _ruleMappingService == null) return;

            try
            {
                // Analyze rules and create mappings
                _allRuleMappings = _ruleMappingService.AnalyzeRules(formDefinitions) ?? new List<RuleMappingItem>();
                _filteredRuleMappings = new List<RuleMappingItem>(_allRuleMappings);

                // Update UI
                UpdateRulesList();
                UpdateSummaryStats();

                // Clear selection
                ClearRuleDetails();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error analyzing rules: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Refreshes the rules mapping display
        /// </summary>
        public void RefreshRulesMapping()
        {
            if (_mainWindow?._allFormDefinitions != null && _mainWindow._allFormDefinitions.Any())
            {
                PopulateRulesMappings(_mainWindow._allFormDefinitions);
            }
        }

        /// <summary>
        /// Handles rule filter text change
        /// </summary>
        public void OnRuleFilterChanged(string filterText)
        {
            ApplyFilters(filterText ?? "", GetSelectedStatusFilter());
        }

        /// <summary>
        /// Handles status filter change
        /// </summary>
        public void OnStatusFilterChanged(int selectedIndex)
        {
            var filterText = _mainWindow?.RuleFilterTextBox?.Text ?? "";
            ApplyFilters(filterText, selectedIndex);
        }

        /// <summary>
        /// Handles rule selection change
        /// </summary>
        public void OnRuleSelected(RuleMappingItem selectedItem)
        {
            _selectedMapping = selectedItem;

            if (selectedItem == null)
            {
                ClearRuleDetails();
                return;
            }

            DisplayRuleDetails(selectedItem);
        }

        /// <summary>
        /// Handles K2 XML text change
        /// </summary>
        public void OnK2XmlChanged(string newXml)
        {
            if (_selectedMapping != null)
            {
                _selectedMapping.K2Xml = newXml;
            }
        }

        /// <summary>
        /// Handles mapping notes text change
        /// </summary>
        public void OnMappingNotesChanged(string newNotes)
        {
            if (_selectedMapping != null)
            {
                _selectedMapping.Notes = newNotes;
            }
        }

        /// <summary>
        /// Copies K2 XML to clipboard
        /// </summary>
        public void CopyK2Xml()
        {
            var xml = _mainWindow.K2XmlOutput.Text;
            if (!string.IsNullOrEmpty(xml))
            {
                Clipboard.SetText(xml);
                MessageBox.Show("K2 XML copied to clipboard.", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        /// <summary>
        /// Saves current rule mapping changes
        /// </summary>
        public void SaveRuleMapping()
        {
            if (_selectedMapping == null)
            {
                MessageBox.Show("No rule selected.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Update mapping with current values
            _selectedMapping.K2Xml = _mainWindow.K2XmlOutput.Text;
            _selectedMapping.Notes = _mainWindow.MappingNotesText.Text;

            // Refresh the list display
            UpdateRulesList();

            MessageBox.Show("Rule mapping saved.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Exports all rule mappings to file
        /// </summary>
        public void ExportRulesMappings()
        {
            if (_allRuleMappings == null || !_allRuleMappings.Any())
            {
                MessageBox.Show("No rule mappings to export.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var saveDialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|XML files (*.xml)|*.xml|All files (*.*)|*.*",
                DefaultExt = "json",
                FileName = "RulesMappings"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    var extension = Path.GetExtension(saveDialog.FileName).ToLower();

                    if (extension == ".xml")
                    {
                        ExportAsXml(saveDialog.FileName);
                    }
                    else
                    {
                        ExportAsJson(saveDialog.FileName);
                    }

                    MessageBox.Show($"Rule mappings exported to {saveDialog.FileName}", "Export Complete",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error exporting: {ex.Message}", "Export Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ApplyFilters(string filterText, int statusIndex)
        {
            if (_allRuleMappings == null)
            {
                _filteredRuleMappings = new List<RuleMappingItem>();
                UpdateRulesList();
                return;
            }

            _filteredRuleMappings = new List<RuleMappingItem>(_allRuleMappings);

            // Apply text filter
            if (!string.IsNullOrWhiteSpace(filterText))
            {
                filterText = filterText.ToLower();
                _filteredRuleMappings = _filteredRuleMappings
                    .Where(r =>
                        (r.InfoPathRuleName?.ToLower().Contains(filterText) == true) ||
                        (r.InfoPathCondition?.ToLower().Contains(filterText) == true) ||
                        (r.InfoPathRuleType?.ToLower().Contains(filterText) == true) ||
                        (r.FormName?.ToLower().Contains(filterText) == true))
                    .ToList();
            }

            // Apply status filter
            if (statusIndex > 0)
            {
                var targetStatus = statusIndex switch
                {
                    1 => RuleMappingStatus.Supported,
                    2 => RuleMappingStatus.PartiallySupported,
                    3 => RuleMappingStatus.NotSupported,
                    4 => RuleMappingStatus.RequiresCustomization,
                    5 => RuleMappingStatus.K2Native,
                    _ => (RuleMappingStatus?)null
                };

                if (targetStatus.HasValue)
                {
                    _filteredRuleMappings = _filteredRuleMappings
                        .Where(r => r.Status == targetStatus.Value)
                        .ToList();
                }
            }

            UpdateRulesList();
        }

        private int GetSelectedStatusFilter()
        {
            return _mainWindow?.RuleStatusFilterCombo?.SelectedIndex ?? 0;
        }

        private void UpdateRulesList()
        {
            if (_mainWindow?.RulesMappingListBox == null) return;

            _mainWindow.RulesMappingListBox.ItemsSource = null;
            _mainWindow.RulesMappingListBox.ItemsSource = _filteredRuleMappings;
        }

        private void UpdateSummaryStats()
        {
            if (_mainWindow == null || _allRuleMappings == null) return;

            if (_mainWindow.TotalRulesCount != null)
                _mainWindow.TotalRulesCount.Text = _allRuleMappings.Count.ToString();
            if (_mainWindow.SupportedRulesCount != null)
                _mainWindow.SupportedRulesCount.Text = _allRuleMappings.Count(r => r.Status == RuleMappingStatus.Supported).ToString();
            if (_mainWindow.PartialRulesCount != null)
                _mainWindow.PartialRulesCount.Text = _allRuleMappings.Count(r => r.Status == RuleMappingStatus.PartiallySupported).ToString();
            if (_mainWindow.UnsupportedRulesCount != null)
                _mainWindow.UnsupportedRulesCount.Text = _allRuleMappings.Count(r => r.Status == RuleMappingStatus.NotSupported).ToString();
            if (_mainWindow.CustomRulesCount != null)
                _mainWindow.CustomRulesCount.Text = _allRuleMappings.Count(r => r.Status == RuleMappingStatus.RequiresCustomization).ToString();
        }

        private void DisplayRuleDetails(RuleMappingItem mapping)
        {
            if (_mainWindow == null || mapping == null) return;

            // InfoPath details
            if (_mainWindow.InfoPathRuleNameText != null)
                _mainWindow.InfoPathRuleNameText.Text = mapping.InfoPathRuleName ?? "";
            if (_mainWindow.InfoPathRuleTypeText != null)
                _mainWindow.InfoPathRuleTypeText.Text = mapping.InfoPathRuleType ?? "";
            if (_mainWindow.InfoPathConditionText != null)
                _mainWindow.InfoPathConditionText.Text = mapping.InfoPathCondition ?? "";
            if (_mainWindow.InfoPathExpressionText != null)
                _mainWindow.InfoPathExpressionText.Text = mapping.InfoPathConditionExpression ?? "";
            if (_mainWindow.InfoPathAppliesToText != null)
                _mainWindow.InfoPathAppliesToText.Text = mapping.InfoPathAppliesTo ?? "";
            if (_mainWindow.InfoPathActionsPanel != null)
                _mainWindow.InfoPathActionsPanel.ItemsSource = mapping.InfoPathActions;

            // K2 XML
            if (_mainWindow.K2XmlOutput != null)
                _mainWindow.K2XmlOutput.Text = mapping.K2Xml ?? "";

            // Notes and warnings
            if (_mainWindow.MappingNotesText != null)
                _mainWindow.MappingNotesText.Text = mapping.Notes ?? "";
            if (_mainWindow.WarningsPanel != null)
                _mainWindow.WarningsPanel.ItemsSource = mapping.Warnings;
            if (_mainWindow.MissingFeaturesPanel != null)
                _mainWindow.MissingFeaturesPanel.ItemsSource = mapping.MissingFeatures;
        }

        private void ClearRuleDetails()
        {
            if (_mainWindow == null) return;

            if (_mainWindow.InfoPathRuleNameText != null)
                _mainWindow.InfoPathRuleNameText.Text = "";
            if (_mainWindow.InfoPathRuleTypeText != null)
                _mainWindow.InfoPathRuleTypeText.Text = "";
            if (_mainWindow.InfoPathConditionText != null)
                _mainWindow.InfoPathConditionText.Text = "";
            if (_mainWindow.InfoPathExpressionText != null)
                _mainWindow.InfoPathExpressionText.Text = "";
            if (_mainWindow.InfoPathAppliesToText != null)
                _mainWindow.InfoPathAppliesToText.Text = "";
            if (_mainWindow.InfoPathActionsPanel != null)
                _mainWindow.InfoPathActionsPanel.ItemsSource = null;
            if (_mainWindow.K2XmlOutput != null)
                _mainWindow.K2XmlOutput.Text = "";
            if (_mainWindow.MappingNotesText != null)
                _mainWindow.MappingNotesText.Text = "";
            if (_mainWindow.WarningsPanel != null)
                _mainWindow.WarningsPanel.ItemsSource = null;
            if (_mainWindow.MissingFeaturesPanel != null)
                _mainWindow.MissingFeaturesPanel.ItemsSource = null;
        }

        /// <summary>
        /// Copies selected rule details to clipboard in a prompt-friendly format
        /// </summary>
        public void CopyRuleDetailsToClipboard()
        {
            var selected = _mainWindow?.RulesMappingListBox?.SelectedItem as RuleMappingItem;
            if (selected == null)
            {
                MessageBox.Show("No rule selected.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var text = FormatRuleForClipboard(selected);
            Clipboard.SetText(text);
        }

        /// <summary>
        /// Copies all currently filtered rules to clipboard
        /// </summary>
        public void CopyAllFilteredRulesToClipboard()
        {
            if (_filteredRuleMappings == null || !_filteredRuleMappings.Any())
            {
                MessageBox.Show("No rules to copy.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"# InfoPath to K2 Rule Mappings ({_filteredRuleMappings.Count} rules)");
            sb.AppendLine();

            foreach (var rule in _filteredRuleMappings)
            {
                sb.AppendLine(FormatRuleForClipboard(rule));
                sb.AppendLine("---");
                sb.AppendLine();
            }

            Clipboard.SetText(sb.ToString());
        }

        private string FormatRuleForClipboard(RuleMappingItem rule)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"## Rule: {rule.InfoPathRuleName}");
            sb.AppendLine($"- **Form**: {rule.FormName}");
            sb.AppendLine($"- **Type**: {rule.InfoPathRuleType}");
            sb.AppendLine($"- **Status**: {rule.StatusDisplay}");

            if (!string.IsNullOrEmpty(rule.InfoPathCondition))
                sb.AppendLine($"- **Condition**: `{rule.InfoPathCondition}`");

            if (!string.IsNullOrEmpty(rule.InfoPathConditionExpression))
                sb.AppendLine($"- **Expression**: `{rule.InfoPathConditionExpression}`");

            if (!string.IsNullOrEmpty(rule.InfoPathAppliesTo))
                sb.AppendLine($"- **Applies To**: {rule.InfoPathAppliesTo}");

            if (rule.InfoPathActions != null && rule.InfoPathActions.Any())
            {
                sb.AppendLine("- **Actions**:");
                foreach (var action in rule.InfoPathActions)
                {
                    sb.Append($"  - {action.InfoPathActionType}");
                    if (!string.IsNullOrEmpty(action.InfoPathTarget))
                        sb.Append($" → {action.InfoPathTarget}");
                    if (!string.IsNullOrEmpty(action.InfoPathExpression))
                        sb.Append($" = `{action.InfoPathExpression}`");
                    sb.Append($" (K2: {action.K2ActionType}, {(action.IsSupported ? "Supported" : "Not Supported")})");
                    sb.AppendLine();
                }
            }

            if (!string.IsNullOrEmpty(rule.K2Xml))
            {
                sb.AppendLine("- **K2 XML**:");
                sb.AppendLine("```xml");
                sb.AppendLine(rule.K2Xml);
                sb.AppendLine("```");
            }

            if (!string.IsNullOrEmpty(rule.Notes))
                sb.AppendLine($"- **Notes**: {rule.Notes}");

            if (rule.Warnings != null && rule.Warnings.Any())
                sb.AppendLine($"- **Warnings**: {string.Join("; ", rule.Warnings)}");

            if (rule.MissingFeatures != null && rule.MissingFeatures.Any())
                sb.AppendLine($"- **Missing Features**: {string.Join("; ", rule.MissingFeatures)}");

            return sb.ToString();
        }

        private void ExportAsJson(string filePath)
        {
            var exportData = new
            {
                ExportDate = DateTime.Now,
                TotalRules = _allRuleMappings.Count,
                Summary = new
                {
                    Supported = _allRuleMappings.Count(r => r.Status == RuleMappingStatus.Supported),
                    Partial = _allRuleMappings.Count(r => r.Status == RuleMappingStatus.PartiallySupported),
                    NotSupported = _allRuleMappings.Count(r => r.Status == RuleMappingStatus.NotSupported),
                    CustomRequired = _allRuleMappings.Count(r => r.Status == RuleMappingStatus.RequiresCustomization)
                },
                Mappings = _allRuleMappings.Select(m => new
                {
                    m.FormName,
                    m.InfoPathRuleName,
                    m.InfoPathRuleType,
                    m.InfoPathCondition,
                    m.InfoPathConditionExpression,
                    m.InfoPathAppliesTo,
                    Status = m.StatusDisplay,
                    m.K2Xml,
                    m.Notes,
                    m.Warnings,
                    m.MissingFeatures,
                    Actions = m.InfoPathActions.Select(a => new
                    {
                        a.InfoPathActionType,
                        a.InfoPathTarget,
                        a.InfoPathExpression,
                        a.K2ActionType,
                        a.IsSupported
                    })
                })
            };

            var json = JsonConvert.SerializeObject(exportData, Formatting.Indented);
            File.WriteAllText(filePath, json);
        }

        private void ExportAsXml(string filePath)
        {
            using var writer = new StreamWriter(filePath);
            writer.WriteLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            writer.WriteLine("<RulesMappingExport>");
            writer.WriteLine($"  <ExportDate>{DateTime.Now:yyyy-MM-dd HH:mm:ss}</ExportDate>");
            writer.WriteLine($"  <TotalRules>{_allRuleMappings.Count}</TotalRules>");
            writer.WriteLine("  <Summary>");
            writer.WriteLine($"    <Supported>{_allRuleMappings.Count(r => r.Status == RuleMappingStatus.Supported)}</Supported>");
            writer.WriteLine($"    <Partial>{_allRuleMappings.Count(r => r.Status == RuleMappingStatus.PartiallySupported)}</Partial>");
            writer.WriteLine($"    <NotSupported>{_allRuleMappings.Count(r => r.Status == RuleMappingStatus.NotSupported)}</NotSupported>");
            writer.WriteLine($"    <CustomRequired>{_allRuleMappings.Count(r => r.Status == RuleMappingStatus.RequiresCustomization)}</CustomRequired>");
            writer.WriteLine("  </Summary>");
            writer.WriteLine("  <Mappings>");

            foreach (var mapping in _allRuleMappings)
            {
                writer.WriteLine("    <RuleMapping>");
                writer.WriteLine($"      <FormName>{EscapeXml(mapping.FormName)}</FormName>");
                writer.WriteLine($"      <InfoPathRuleName>{EscapeXml(mapping.InfoPathRuleName)}</InfoPathRuleName>");
                writer.WriteLine($"      <InfoPathRuleType>{EscapeXml(mapping.InfoPathRuleType)}</InfoPathRuleType>");
                writer.WriteLine($"      <Status>{mapping.StatusDisplay}</Status>");
                writer.WriteLine("      <InfoPathDetails>");
                writer.WriteLine($"        <Condition>{EscapeXml(mapping.InfoPathCondition)}</Condition>");
                writer.WriteLine($"        <AppliesTo>{EscapeXml(mapping.InfoPathAppliesTo)}</AppliesTo>");
                writer.WriteLine("      </InfoPathDetails>");
                writer.WriteLine("      <K2Xml>");
                writer.WriteLine($"        <![CDATA[{mapping.K2Xml ?? ""}]]>");
                writer.WriteLine("      </K2Xml>");
                if (!string.IsNullOrEmpty(mapping.Notes))
                {
                    writer.WriteLine($"      <Notes>{EscapeXml(mapping.Notes)}</Notes>");
                }
                writer.WriteLine("    </RuleMapping>");
            }

            writer.WriteLine("  </Mappings>");
            writer.WriteLine("</RulesMappingExport>");
        }

        private string EscapeXml(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }
    }
}
