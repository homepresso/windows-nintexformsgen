using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FormGenerator.Core.Models
{
    /// <summary>
    /// Represents a mapping between an InfoPath rule and its K2 equivalent
    /// </summary>
    public class RuleMappingItem : INotifyPropertyChanged
    {
        private string _k2Xml;
        private RuleMappingStatus _status;
        private string _notes;

        public string Id { get; set; } = Guid.NewGuid().ToString();

        // InfoPath Source Rule Properties
        public string InfoPathRuleName { get; set; }
        public string InfoPathRuleType { get; set; }
        public string InfoPathCondition { get; set; }
        public string InfoPathConditionExpression { get; set; }
        public string InfoPathAppliesTo { get; set; }
        public bool InfoPathIsEnabled { get; set; }
        public List<RuleActionMapping> InfoPathActions { get; set; } = new List<RuleActionMapping>();
        public string InfoPathErrorMessage { get; set; }
        public string InfoPathSourceLocation { get; set; }

        // K2 Target Rule Properties
        public string K2RuleType { get; set; }
        public string K2EventType { get; set; }
        public string K2ActionType { get; set; }

        public string K2Xml
        {
            get => _k2Xml;
            set
            {
                _k2Xml = value;
                OnPropertyChanged();
            }
        }

        // Mapping Status
        public RuleMappingStatus Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusDisplay));
                OnPropertyChanged(nameof(StatusColor));
            }
        }

        public string StatusDisplay => Status switch
        {
            RuleMappingStatus.Supported => "Supported",
            RuleMappingStatus.PartiallySupported => "Partial",
            RuleMappingStatus.NotSupported => "Not Supported",
            RuleMappingStatus.RequiresCustomization => "Custom Required",
            RuleMappingStatus.NotMapped => "Not Mapped",
            RuleMappingStatus.K2Native => "K2 Native",
            _ => "Unknown"
        };

        public string StatusColor => Status switch
        {
            RuleMappingStatus.Supported => "#10B981",
            RuleMappingStatus.PartiallySupported => "#F59E0B",
            RuleMappingStatus.NotSupported => "#EF4444",
            RuleMappingStatus.RequiresCustomization => "#3B82F6",
            RuleMappingStatus.NotMapped => "#888888",
            RuleMappingStatus.K2Native => "#8B5CF6",
            _ => "#888888"
        };

        // Additional metadata
        public string Notes
        {
            get => _notes;
            set
            {
                _notes = value;
                OnPropertyChanged();
            }
        }

        public string Complexity { get; set; }
        public List<string> MissingFeatures { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();

        // Form context
        public string FormName { get; set; }
        public string ViewName { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Represents an action within a rule mapping
    /// </summary>
    public class RuleActionMapping
    {
        public string InfoPathActionType { get; set; }
        public string InfoPathTarget { get; set; }
        public string InfoPathExpression { get; set; }
        public Dictionary<string, string> InfoPathParameters { get; set; } = new Dictionary<string, string>();

        public string K2ActionType { get; set; }
        public string K2ActionXml { get; set; }
        public bool IsSupported { get; set; }
        public string MappingNotes { get; set; }
    }

    /// <summary>
    /// Status of rule mapping support
    /// </summary>
    public enum RuleMappingStatus
    {
        NotMapped,
        Supported,
        PartiallySupported,
        NotSupported,
        RequiresCustomization,
        K2Native
    }

    /// <summary>
    /// Summary of rule mappings for a form
    /// </summary>
    public class RuleMappingSummary
    {
        public string FormName { get; set; }
        public int TotalRules { get; set; }
        public int SupportedRules { get; set; }
        public int PartialRules { get; set; }
        public int UnsupportedRules { get; set; }
        public int CustomRequiredRules { get; set; }
        public int K2NativeRules { get; set; }

        public double SupportedPercentage => TotalRules > 0 ? (double)(SupportedRules + K2NativeRules) / TotalRules * 100 : 0;
        public double OverallMappingPercentage => TotalRules > 0
            ? (double)(SupportedRules + K2NativeRules + PartialRules) / TotalRules * 100
            : 0;

        public List<string> CommonIssues { get; set; } = new List<string>();
        public List<string> Recommendations { get; set; } = new List<string>();
    }

    /// <summary>
    /// K2 rule template for common rule patterns
    /// </summary>
    public class K2RuleTemplate
    {
        public string TemplateName { get; set; }
        public string Description { get; set; }
        public string InfoPathRuleType { get; set; }
        public string K2EventType { get; set; }
        public string K2ActionType { get; set; }
        public string XmlTemplate { get; set; }
        public List<string> RequiredParameters { get; set; } = new List<string>();
    }
}
