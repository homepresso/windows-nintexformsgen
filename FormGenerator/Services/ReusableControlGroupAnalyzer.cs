using System;
using System.Collections.Generic;
using System.Linq;
using FormGenerator.Analyzers.Infopath;

namespace FormGenerator.Services
{
    /// <summary>
    /// Service for analyzing and identifying reusable control groups across multiple forms
    /// </summary>
    public class ReusableControlGroupAnalyzer
    {
        public class ControlGroup
        {
            public string GroupId { get; set; }
            public List<ControlSignature> Controls { get; set; } = new List<ControlSignature>();
            public List<string> FoundInForms { get; set; } = new List<string>();
            public int OccurrenceCount => FoundInForms.Count;
            public string SuggestedName { get; set; }
            public bool IsSequential { get; set; }
            public string CommonSection { get; set; }
            public bool ContainsRepeatingControls { get; set; }
        }

        public class ControlSignature
        {
            public string Label { get; set; }
            public string Type { get; set; }
            public string Name { get; set; }
            public int RelativePosition { get; set; }
            public string NormalizedLabel { get; set; }

            public override bool Equals(object obj)
            {
                if (obj is ControlSignature other)
                {
                    return NormalizedLabel == other.NormalizedLabel &&
                           Type == other.Type;
                }
                return false;
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(NormalizedLabel, Type);
            }
        }

        public class AnalysisResult
        {
            public List<ControlGroup> IdentifiedGroups { get; set; } = new List<ControlGroup>();
            public Dictionary<string, int> ControlFrequency { get; set; } = new Dictionary<string, int>();
            public int TotalFormsAnalyzed { get; set; }
            public int TotalControlsAnalyzed { get; set; }
            public int ControlsInRepeatingSections { get; set; }
            public List<string> CommonPatterns { get; set; } = new List<string>();
            public List<RepeatingSectionInfo> RepeatingSections { get; set; } = new List<RepeatingSectionInfo>();
        }

        public class RepeatingSectionInfo
        {
            public string Name { get; set; }
            public string FormName { get; set; }
            public int ControlCount { get; set; }
            public List<string> ControlTypes { get; set; } = new List<string>();
        }

        private readonly int _proximityThreshold = 5;
        private readonly double _similarityThreshold = 0.8;

        /// <summary>
        /// Analyzes multiple forms to identify reusable control groups
        /// </summary>
        public AnalysisResult AnalyzeForReusableGroups(
            Dictionary<string, InfoPathFormDefinition> formDefinitions,
            int minOccurrences = 2,
            int minGroupSize = 2,
            int maxGroupSize = 10)
        {
            var result = new AnalysisResult
            {
                TotalFormsAnalyzed = formDefinitions.Count
            };

            // Step 1: Extract repeating sections information
            ExtractRepeatingSectionsInfo(formDefinitions, result);

            // Step 2: Extract control sequences from each form (excluding repeating section controls)
            var formControlSequences = new Dictionary<string, List<ControlSignature>>();

            foreach (var form in formDefinitions)
            {
                var controlSequence = ExtractControlSequence(form.Value);
                formControlSequences[form.Key] = controlSequence;
                result.TotalControlsAnalyzed += controlSequence.Count;

                // Count excluded controls
                var excludedCount = form.Value.Views
                    .SelectMany(v => v.Controls)
                    .Count(c => c.IsInRepeatingSection || c.SectionType == "repeating");
                result.ControlsInRepeatingSections += excludedCount;
            }

            // Step 3: Find common subsequences across forms
            var commonGroups = FindCommonControlGroups(
                formControlSequences,
                minGroupSize,
                maxGroupSize,
                minOccurrences);

            // Step 4: Merge similar groups
            var mergedGroups = MergeSimilarGroups(commonGroups);

            // Step 5: Rank and name the groups
            result.IdentifiedGroups = RankAndNameGroups(mergedGroups);

            // Step 6: Calculate control frequency
            result.ControlFrequency = CalculateControlFrequency(formControlSequences);

            // Step 7: Identify common patterns
            result.CommonPatterns = IdentifyCommonPatterns(result.IdentifiedGroups);

            return result;
        }

        /// <summary>
        /// Extracts information about repeating sections
        /// </summary>
        private void ExtractRepeatingSectionsInfo(
            Dictionary<string, InfoPathFormDefinition> formDefinitions,
            AnalysisResult result)
        {
            foreach (var form in formDefinitions)
            {
                var repeatingSections = new Dictionary<string, RepeatingSectionInfo>();

                foreach (var view in form.Value.Views)
                {
                    foreach (var control in view.Controls)
                    {
                        // Check if this is a repeating table control
                        if (control.Type == "RepeatingTable")
                        {
                            var sectionInfo = new RepeatingSectionInfo
                            {
                                Name = control.Label ?? control.Name,
                                FormName = form.Key,
                                ControlCount = control.Controls?.Count ?? 0,
                                ControlTypes = control.Controls?.Select(c => c.Type).Distinct().ToList() ?? new List<string>()
                            };
                            result.RepeatingSections.Add(sectionInfo);
                        }

                        // Track controls in repeating sections
                        if (control.IsInRepeatingSection && !string.IsNullOrEmpty(control.RepeatingSectionName))
                        {
                            if (!repeatingSections.ContainsKey(control.RepeatingSectionName))
                            {
                                repeatingSections[control.RepeatingSectionName] = new RepeatingSectionInfo
                                {
                                    Name = control.RepeatingSectionName,
                                    FormName = form.Key,
                                    ControlCount = 0,
                                    ControlTypes = new List<string>()
                                };
                            }

                            repeatingSections[control.RepeatingSectionName].ControlCount++;
                            if (!repeatingSections[control.RepeatingSectionName].ControlTypes.Contains(control.Type))
                            {
                                repeatingSections[control.RepeatingSectionName].ControlTypes.Add(control.Type);
                            }
                        }
                    }
                }

                result.RepeatingSections.AddRange(repeatingSections.Values);
            }
        }

        /// <summary>
        /// Extracts a normalized sequence of controls from a form
        /// </summary>
        private List<ControlSignature> ExtractControlSequence(InfoPathFormDefinition formDef)
        {
            var sequence = new List<ControlSignature>();
            int position = 0;

            foreach (var view in formDef.Views)
            {
                foreach (var control in view.Controls.Where(c => !c.IsMergedIntoParent))
                {
                    // Skip certain control types
                    if (control.Type == "Section" || control.Type == "RepeatingSection" ||
                        control.Type == "RepeatingTable")
                        continue;

                    // IMPORTANT: Skip Label controls - we only want actual input controls
                    if (control.Type == "Label")
                    {
                        System.Diagnostics.Debug.WriteLine($"Skipping label control: '{control.Label}'");
                        continue;
                    }

                    // IMPORTANT: Skip controls that are inside repeating sections
                    if (control.IsInRepeatingSection)
                    {
                        System.Diagnostics.Debug.WriteLine($"Skipping control '{control.Label ?? control.Name}' - inside repeating section '{control.RepeatingSectionName}'");
                        continue;
                    }

                    // Also skip if the control's section type indicates it's repeating
                    if (control.SectionType == "repeating")
                    {
                        System.Diagnostics.Debug.WriteLine($"Skipping control '{control.Label ?? control.Name}' - section type is repeating");
                        continue;
                    }

                    var signature = new ControlSignature
                    {
                        Label = control.Label ?? control.Name,
                        Type = control.Type,
                        Name = control.Name,
                        RelativePosition = position++,
                        NormalizedLabel = NormalizeLabel(control.Label ?? control.Name)
                    };

                    sequence.Add(signature);
                }
            }

            return sequence;
        }

        /// <summary>
        /// Finds common control groups using sliding window approach
        /// </summary>
        private List<ControlGroup> FindCommonControlGroups(
            Dictionary<string, List<ControlSignature>> formSequences,
            int minSize,
            int maxSize,
            int minOccurrences)
        {
            var groups = new Dictionary<string, ControlGroup>();

            foreach (var form in formSequences)
            {
                for (int size = minSize; size <= maxSize; size++)
                {
                    for (int start = 0; start <= form.Value.Count - size; start++)
                    {
                        var subsequence = form.Value.Skip(start).Take(size).ToList();
                        var groupKey = GenerateGroupKey(subsequence);

                        if (!groups.ContainsKey(groupKey))
                        {
                            groups[groupKey] = new ControlGroup
                            {
                                GroupId = groupKey,
                                Controls = new List<ControlSignature>(subsequence),
                                IsSequential = true
                            };
                        }

                        if (!groups[groupKey].FoundInForms.Contains(form.Key))
                        {
                            groups[groupKey].FoundInForms.Add(form.Key);
                        }
                    }
                }
            }

            return groups.Values
                .Where(g => g.OccurrenceCount >= minOccurrences)
                .ToList();
        }

        /// <summary>
        /// Merges groups that are similar
        /// </summary>
        private List<ControlGroup> MergeSimilarGroups(List<ControlGroup> groups)
        {
            var merged = new List<ControlGroup>();
            var processed = new HashSet<string>();

            foreach (var group in groups.OrderByDescending(g => g.OccurrenceCount))
            {
                if (processed.Contains(group.GroupId))
                    continue;

                var similarGroups = groups
                    .Where(g => !processed.Contains(g.GroupId) &&
                                g.GroupId != group.GroupId &&
                                CalculateSimilarity(group, g) >= _similarityThreshold)
                    .ToList();

                foreach (var similar in similarGroups)
                {
                    foreach (var form in similar.FoundInForms)
                    {
                        if (!group.FoundInForms.Contains(form))
                            group.FoundInForms.Add(form);
                    }
                    processed.Add(similar.GroupId);
                }

                merged.Add(group);
                processed.Add(group.GroupId);
            }

            return merged;
        }

        /// <summary>
        /// Calculates similarity between two control groups
        /// </summary>
        private double CalculateSimilarity(ControlGroup group1, ControlGroup group2)
        {
            if (group1.Controls.Count != group2.Controls.Count)
                return 0;

            int matches = 0;
            for (int i = 0; i < group1.Controls.Count; i++)
            {
                var c1 = group1.Controls[i];
                var c2 = group2.Controls[i];

                if (c1.Type == c2.Type)
                {
                    matches++;

                    if (AreLabelsSimilar(c1.NormalizedLabel, c2.NormalizedLabel))
                        matches++;
                }
            }

            return (double)matches / (group1.Controls.Count * 2);
        }

        /// <summary>
        /// Checks if two labels are similar
        /// </summary>
        private bool AreLabelsSimilar(string label1, string label2)
        {
            if (string.IsNullOrEmpty(label1) || string.IsNullOrEmpty(label2))
                return false;

            if (label1.Equals(label2, StringComparison.OrdinalIgnoreCase))
                return true;

            var distance = LevenshteinDistance(label1.ToLower(), label2.ToLower());
            var maxLength = Math.Max(label1.Length, label2.Length);
            var similarity = 1.0 - ((double)distance / maxLength);

            return similarity >= 0.7;
        }

        /// <summary>
        /// Calculates Levenshtein distance
        /// </summary>
        private int LevenshteinDistance(string s1, string s2)
        {
            int[,] d = new int[s1.Length + 1, s2.Length + 1];

            for (int i = 0; i <= s1.Length; i++)
                d[i, 0] = i;

            for (int j = 0; j <= s2.Length; j++)
                d[0, j] = j;

            for (int i = 1; i <= s1.Length; i++)
            {
                for (int j = 1; j <= s2.Length; j++)
                {
                    int cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }

            return d[s1.Length, s2.Length];
        }

        /// <summary>
        /// Ranks groups and generates suggested names
        /// </summary>
        private List<ControlGroup> RankAndNameGroups(List<ControlGroup> groups)
        {
            foreach (var group in groups)
            {
                group.SuggestedName = GenerateSuggestedName(group);
            }

            return groups
                .OrderByDescending(g => g.OccurrenceCount)
                .ThenByDescending(g => g.Controls.Count)
                .ToList();
        }

        /// <summary>
        /// Generates a suggested name for a control group
        /// </summary>
        private string GenerateSuggestedName(ControlGroup group)
        {
            var labels = group.Controls
                .Where(c => !string.IsNullOrWhiteSpace(c.Label))
                .Select(c => c.Label)
                .ToList();

            if (labels.Count == 0)
                return $"ControlGroup_{group.GroupId.Substring(0, Math.Min(8, group.GroupId.Length))}";

            // Common patterns
            if (ContainsPattern(labels, new[] { "first", "last", "name" }))
                return "NameFields";

            if (ContainsPattern(labels, new[] { "address", "city", "state", "zip" }))
                return "AddressFields";

            if (ContainsPattern(labels, new[] { "email", "phone" }))
                return "ContactFields";

            if (ContainsPattern(labels, new[] { "department", "division", "unit" }))
                return "OrganizationFields";

            if (ContainsPattern(labels, new[] { "date", "time" }))
                return "DateTimeFields";

            var firstName = labels.First().Replace(" ", "");
            var lastName = labels.Count > 1 ? labels.Last().Replace(" ", "") : "";

            return string.IsNullOrEmpty(lastName) ?
                $"{firstName}Group" :
                $"{firstName}To{lastName}";
        }

        /// <summary>
        /// Checks if labels contain a pattern
        /// </summary>
        private bool ContainsPattern(List<string> labels, string[] keywords)
        {
            var labelText = string.Join(" ", labels).ToLower();
            return keywords.Count(k => labelText.Contains(k)) >= keywords.Length / 2;
        }

        /// <summary>
        /// Normalizes a label for comparison
        /// </summary>
        private string NormalizeLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
                return "";

            return System.Text.RegularExpressions.Regex
                .Replace(label, @"[^a-zA-Z0-9]", "")
                .ToUpper();
        }

        /// <summary>
        /// Generates a unique key for a control group
        /// </summary>
        private string GenerateGroupKey(List<ControlSignature> controls)
        {
            var keyParts = controls.Select(c => $"{c.Type}_{c.NormalizedLabel}");
            return string.Join("|", keyParts);
        }

        /// <summary>
        /// Calculates frequency of individual controls
        /// </summary>
        private Dictionary<string, int> CalculateControlFrequency(
            Dictionary<string, List<ControlSignature>> formSequences)
        {
            var frequency = new Dictionary<string, int>();

            foreach (var form in formSequences)
            {
                var uniqueControls = form.Value
                    .Select(c => $"{c.Type}:{c.NormalizedLabel}")
                    .Distinct();

                foreach (var control in uniqueControls)
                {
                    if (!frequency.ContainsKey(control))
                        frequency[control] = 0;
                    frequency[control]++;
                }
            }

            return frequency.OrderByDescending(f => f.Value)
                          .ToDictionary(f => f.Key, f => f.Value);
        }

        /// <summary>
        /// Identifies common patterns in the groups
        /// </summary>
        private List<string> IdentifyCommonPatterns(List<ControlGroup> groups)
        {
            var patterns = new List<string>();

            var textFieldGroups = groups.Where(g =>
                g.Controls.Count >= 2 &&
                g.Controls.All(c => c.Type == "TextField"));

            if (textFieldGroups.Any())
                patterns.Add($"Found {textFieldGroups.Count()} groups of sequential text fields");

            var labelInputPairs = groups.Where(g =>
                g.Controls.Count == 2 &&
                g.Controls[0].Type == "Label" &&
                g.Controls[1].Type != "Label");

            if (labelInputPairs.Any())
                patterns.Add($"Found {labelInputPairs.Count()} label-input pairs");

            var dateTimeGroups = groups.Where(g =>
                g.Controls.Any(c => c.Type == "DatePicker") &&
                g.Controls.Count >= 2);

            if (dateTimeGroups.Any())
                patterns.Add($"Found {dateTimeGroups.Count()} date/time field combinations");

            return patterns;
        }
    }
}