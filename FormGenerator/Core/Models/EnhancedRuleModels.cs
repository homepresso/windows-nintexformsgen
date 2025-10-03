using System;
using System.Collections.Generic;
using System.Linq;

namespace FormGenerator.Core.Models
{
    public enum RuleComplexity
    {
        Simple,      // Single field operations
        Moderate,    // Multiple fields, basic functions
        Complex,     // Nested expressions, advanced functions
        Advanced     // Multiple dependencies, complex business logic
    }

    public enum ExpressionType
    {
        Static,          // Static values
        FieldReference,  // Simple field reference
        Calculation,     // Mathematical operations
        Concatenation,   // String concatenation
        Conditional,     // If-then-else logic
        Lookup,          // Data lookups
        DateFunction,    // Date/time functions
        StringFunction,  // String manipulation
        Aggregation,     // Sum, count, average, etc.
        CustomFunction   // Custom XPath functions
    }

    public class EnhancedFormRule
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; }
        public string Description { get; set; }
        public bool IsEnabled { get; set; } = true;
        public RuleComplexity Complexity { get; set; }
        public string Category { get; set; } // Business, Validation, Formatting, Navigation, etc.

        // Enhanced condition handling
        public EnhancedExpression Condition { get; set; }
        public string OriginalXPath { get; set; }
        public string SimplifiedCondition { get; set; }

        // Actions and effects
        public List<EnhancedRuleAction> Actions { get; set; } = new List<EnhancedRuleAction>();
        public List<string> AffectedFields { get; set; } = new List<string>();
        public List<string> DependentFields { get; set; } = new List<string>();

        // Rule relationships
        public List<string> TriggeredByRules { get; set; } = new List<string>();
        public List<string> TriggersRules { get; set; } = new List<string>();

        // Metadata
        public string SourceLocation { get; set; } // manifest.xsf, view1.xsl, etc.
        public DateTime ExtractedDate { get; set; } = DateTime.Now;
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    public class EnhancedExpression
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public ExpressionType Type { get; set; }
        public string OriginalExpression { get; set; }
        public string ParsedExpression { get; set; }
        public string HumanReadable { get; set; }

        // Expression components
        public List<string> ReferencedFields { get; set; } = new List<string>();
        public List<string> UsedFunctions { get; set; } = new List<string>();
        public List<string> Constants { get; set; } = new List<string>();
        public List<EnhancedExpression> SubExpressions { get; set; } = new List<EnhancedExpression>();

        // Analysis results
        public bool IsComplex { get; set; }
        public bool HasNestedConditions { get; set; }
        public bool RequiresDataLookup { get; set; }
        public string ReturnType { get; set; } // string, number, boolean, date

        // Translation hints
        public Dictionary<string, string> TranslationHints { get; set; } = new Dictionary<string, string>();
    }

    public class EnhancedRuleAction
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Type { get; set; } // setValue, switchView, submit, query, etc.
        public string Target { get; set; }
        public EnhancedExpression Expression { get; set; }
        public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();

        // Action effects
        public List<string> ModifiedFields { get; set; } = new List<string>();
        public string Effect { get; set; } // Show, Hide, Enable, Disable, Calculate, etc.
    }

    public class EnhancedValidationRule
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string FieldName { get; set; }
        public string ValidationType { get; set; } // Required, Pattern, Range, Custom, etc.
        public EnhancedExpression ValidationExpression { get; set; }
        public string ErrorMessage { get; set; }
        public string HelpText { get; set; }

        // Validation parameters
        public string Pattern { get; set; }
        public string MinValue { get; set; }
        public string MaxValue { get; set; }
        public int? MinLength { get; set; }
        public int? MaxLength { get; set; }
        public List<string> AllowedValues { get; set; } = new List<string>();

        public bool IsCustomValidation { get; set; }
        public RuleComplexity Complexity { get; set; }
    }

    public class CalculationRule
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; }
        public string TargetField { get; set; }
        public EnhancedExpression CalculationExpression { get; set; }

        // Calculation metadata
        public string CalculationType { get; set; } // Sum, Average, Count, Formula, etc.
        public List<string> SourceFields { get; set; } = new List<string>();
        public bool IsRepeatingCalculation { get; set; }
        public string RepeatingSource { get; set; }

        // Update triggers
        public List<string> RecalculateWhenChanged { get; set; } = new List<string>();
        public bool RecalculateOnFormLoad { get; set; } = true;
    }

    public class BusinessLogicRule
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; }
        public string BusinessPurpose { get; set; }
        public RuleComplexity Complexity { get; set; }

        public List<EnhancedFormRule> ComponentRules { get; set; } = new List<EnhancedFormRule>();
        public List<EnhancedValidationRule> Validations { get; set; } = new List<EnhancedValidationRule>();
        public List<CalculationRule> Calculations { get; set; } = new List<CalculationRule>();

        // Business context
        public string Department { get; set; }
        public string BusinessProcess { get; set; }
        public List<string> ComplianceRequirements { get; set; } = new List<string>();
    }

    public class RuleAnalysisResult
    {
        public int TotalRules { get; set; }
        public int SimpleRules { get; set; }
        public int ComplexRules { get; set; }
        public int ValidationRules { get; set; }
        public int CalculationRules { get; set; }
        public int BusinessLogicRules { get; set; }

        public List<string> UsedXPathFunctions { get; set; } = new List<string>();
        public List<string> CustomFunctions { get; set; } = new List<string>();
        public List<string> UnsupportedFeatures { get; set; } = new List<string>();

        public Dictionary<string, int> RulesByCategory { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> ComplexityDistribution { get; set; } = new Dictionary<string, int>();
    }
}