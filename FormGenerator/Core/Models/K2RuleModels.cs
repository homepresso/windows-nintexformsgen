using System;
using System.Collections.Generic;

namespace FormGenerator.Core.Models
{
    /// <summary>
    /// K2 Event Types - based on RuleHelper.cs BuildRuleEvent patterns
    /// </summary>
    public enum K2EventType
    {
        // Form-level events
        FormEvent,
        FormControlEvent,
        FormParameterEvent,
        FormServerPreRenderEvent,

        // View-level events
        ViewEvent,
        ViewControlEvent,
        ViewParameterEvent,
        ViewServerPreRenderEvent,

        // SubView events (for nested views)
        SubViewEvent,
        SubViewControlEvent,
        SubViewParameterEvent,
        SubViewServerPreRenderEvent,

        // SubForm events (for embedded forms)
        SubFormEvent,
        SubFormControlEvent,
        SubFormParameterEvent,
        SubFormServerPreRenderEvent,

        // Workflow events
        FormWorkflowViewEvent,
        ViewWorkflowViewEvent,
        FormWorkflowActioned,
        WorkflowActioned,

        // Popup/opened form events
        OpenedFormEvent,
        OpenedFormCloseEvent,
        OpenedViewCloseEvent,
        OpenedFormViewEvent,
        OpenedFormControlEvent
    }

    /// <summary>
    /// K2 Condition Types - based on RuleHelper.cs BuildRuleCondition patterns
    /// </summary>
    public enum K2ConditionType
    {
        // Simple conditions (single control comparison)
        SimpleEqualControlCondition,
        SimpleNotEqualControlCondition,
        SimpleBlankControlCondition,
        SimpleNotBlankControlCondition,
        SimpleGreaterThanCondition,
        SimpleLessThanCondition,
        SimpleContainsCondition,

        // SubView variants
        SubViewSimpleBlankControlCondition,
        SubViewSimpleNotBlankControlCondition,
        SubViewSimpleEqualControlCondition,
        SubViewSimpleNotEqualControlCondition,

        // SubForm variants
        SubFormSimpleBlankControlCondition,
        SubFormSimpleNotBlankControlCondition,

        // Advanced conditions (complex expressions)
        AdvancedCondition,
        ServerAdvancedCondition
    }

    /// <summary>
    /// K2 Action Types - based on RuleHelper.cs BuildRuleAction patterns
    /// </summary>
    public enum K2ActionType
    {
        // Data transfer actions
        ControlTransfer,
        ServerControlTransfer,

        // Method execution actions
        ViewMethodExecute,
        ServerViewMethodExecute,
        ObjectMethodExecute,
        ServerObjectMethodExecute,
        ControlMethodExecute,
        FormControlMethodExecute,

        // Item state execution (for list operations)
        ViewMethodExecuteItemsState,
        ObjectMethodExecuteItemsState,

        // Visibility actions
        ShowControl,
        HideControl,
        SubViewShowControl,
        SubViewHideControl,
        SubFormShowControl,
        SubFormHideControl,

        // Enable/Disable actions
        EnableControl,
        DisableControl,
        SubViewEnableControl,
        SubViewDisableControl,

        // View visibility actions
        ShowView,
        HideView,
        ShowViewFilter,
        HideViewFilter,
        SubFormShowView,
        SubFormHideView,

        // View enable/disable
        EnableView,
        DisableView,
        SubViewEnableView,
        SubViewDisableView,

        // View expand/collapse
        ExpandView,
        CollapseView,

        // Navigation actions
        FormNavigation,
        FormNavigationViewMethodExecute,

        // Subform/subview actions
        SubViewOpen,
        SubViewOpenMethodExecute,
        SubViewCloseMethodExecute,
        SubformClose,
        SubViewTransferData,
        ServerSubViewTransferData,

        // Form actions
        FormOpen,
        FormDisable,
        FormEnable,
        FormExecute,
        SubFormExecute,

        // Panel actions
        ShowPanel,
        HidePanel,
        PanelFocus,
        ViewFocus,

        // Message actions
        ShowAlert,
        ShowConfirmation,
        BrowseMessage,

        // Validation actions
        FormValidateCondition,

        // Editable list actions
        EditableListAddRow,
        EditableListEditRow,
        EditableListRemoveRow,
        EditableListApplyRow,
        EditableListCancelRow,

        // SubView list actions
        SubViewEditableListAddRow,
        SubViewEditableListEditRow,
        SubViewEditableListRemoveRow,

        // Property setting actions
        SetViewControlProperties,
        SetFormControlProperties,
        SetFormViewControlProperties,
        SetAreaItemProperties,
        ServerSetViewControlProperties,
        ServerSetFormControlProperties,

        // List control population
        ViewListControlPopulation,
        ServerViewListControlPopulation,
        ViewListControlPreLoadData,
        ViewListControlPopulateFromData,

        // Process/workflow actions
        ProcessStart,
        ProcessLoad,
        ProcessAction,
        ViewProcessStart,
        ViewProcessLoad,
        ViewProcessAction,

        // Rule control actions
        RuleExecute,
        ServerRuleExecute,
        RuleExit,
        RuleContinue,

        // Opened form actions
        OpenedFormTransfer,
        ServerOpenedFormTransfer,
        OpenedFormViewMethodExecute
    }

    /// <summary>
    /// K2 Handler Types
    /// </summary>
    public enum K2HandlerType
    {
        If,
        Else,
        Error,
        ForEach
    }

    /// <summary>
    /// K2 Mapping Source Types - how the source value is determined
    /// </summary>
    public enum K2MappingSourceType
    {
        Value,          // Literal value
        Control,        // Control value
        ViewField,      // View field
        ViewParameter,  // View parameter
        FormParameter,  // Form parameter
        Expression,     // Calculated expression
        SystemVariable, // System variable (CurrentUser, CurrentDate, etc.)
        MethodParameter,// Method parameter
        ControlProperty // Control property
    }

    /// <summary>
    /// K2 Mapping Target Types - where the value goes
    /// </summary>
    public enum K2MappingTargetType
    {
        ControlProperty,    // Control property (e.g., isvisible, display)
        ViewProperty,       // View property
        ViewField,          // View data field
        MethodParameter,    // Method input parameter
        MethodRequiredProperty,
        MethodOptionalProperty
    }

    /// <summary>
    /// K2 Rule - top-level container for an event and its handlers
    /// </summary>
    public class K2Rule
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid DefinitionId { get; set; } = Guid.NewGuid();
        public K2Event Event { get; set; }
        public List<K2Handler> Handlers { get; set; } = new List<K2Handler>();
        public string FriendlyName { get; set; }
        public string Location { get; set; } = "View"; // View or Form
        public bool IsEnabled { get; set; } = true;
        public string Comments { get; set; }
    }

    /// <summary>
    /// K2 Event - the trigger for a rule (e.g., Init, OnChange, OnClick)
    /// </summary>
    public class K2Event
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid DefinitionId { get; set; } = Guid.NewGuid();
        public K2EventType EventType { get; set; }
        public string Name { get; set; }  // e.g., "Init", "OnChange", "OnClick"
        public string SourceId { get; set; }  // Control/View/Form GUID
        public string SourceType { get; set; } = "Control"; // Control, View, Form
        public string SourceName { get; set; }  // Human-readable name
        public string SourceDisplayName { get; set; }
        public string ViewId { get; set; }  // Parent view GUID
        public string ViewName { get; set; }
        public string InstanceId { get; set; }  // For view instances
        public bool IsExtended { get; set; } = true; // Reference: K2 Designer exports use IsExtended="True" for User events
        public string Type { get; set; } = "User"; // System or User
    }

    /// <summary>
    /// K2 Handler - contains conditions and actions (If/Else/Error blocks)
    /// </summary>
    public class K2Handler
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid DefinitionId { get; set; } = Guid.NewGuid();
        public K2HandlerType HandlerType { get; set; } = K2HandlerType.If;
        public string Name { get; set; }  // e.g., "IfLogicalHandler", "then"
        public string Location { get; set; } = "View";
        public List<K2Condition> Conditions { get; set; } = new List<K2Condition>();
        public List<K2Action> Actions { get; set; } = new List<K2Action>();
        public bool IsEnabled { get; set; } = true;
        public bool IsReference { get; set; } = false;
        public bool IsInherited { get; set; } = false;
        public string Comments { get; set; }
    }

    /// <summary>
    /// K2 Condition - evaluates whether actions should execute
    /// </summary>
    public class K2Condition
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid DefinitionId { get; set; } = Guid.NewGuid();
        public K2ConditionType ConditionType { get; set; }
        public string Name { get; set; }
        public string Location { get; set; } = "View";
        public List<K2Expression> Expressions { get; set; } = new List<K2Expression>();
        public bool IsEnabled { get; set; } = true;
        public string Comments { get; set; }

        // For simple conditions - the control being evaluated
        public string ControlId { get; set; }
        public string ControlName { get; set; }
        public string ControlDisplayName { get; set; }
        public string DataType { get; set; }

        // For comparison conditions
        public string CompareValue { get; set; }
        public string CompareOperator { get; set; } // Equals, NotEquals, Contains, etc.
    }

    /// <summary>
    /// K2 Expression - part of a condition (left operand, operator, right operand)
    /// </summary>
    public class K2Expression
    {
        public string Operator { get; set; }  // Equals, NotEquals, GreaterThan, LessThan, Contains, etc.
        public K2ExpressionItem Left { get; set; }
        public K2ExpressionItem Right { get; set; }
        public string LogicalOperator { get; set; }  // And, Or (for combining multiple expressions)
    }

    /// <summary>
    /// K2 Expression Item - one side of a comparison
    /// </summary>
    public class K2ExpressionItem
    {
        public K2MappingSourceType SourceType { get; set; }
        public string SourceId { get; set; }
        public string SourceName { get; set; }
        public string SourceDisplayName { get; set; }
        public string Value { get; set; }  // For literal values
        public string DataType { get; set; }
        public string ViewId { get; set; }
        public string InstanceId { get; set; }
    }

    /// <summary>
    /// K2 Action - what to do when conditions are met
    /// </summary>
    public class K2Action
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid DefinitionId { get; set; } = Guid.NewGuid();
        public K2ActionType ActionType { get; set; }
        public string Name { get; set; }
        public string Location { get; set; } = "View";
        public string ExecutionType { get; set; } = "Synchronous"; // Synchronous, Asynchronous, Parallel
        public string InstanceId { get; set; }
        public Dictionary<string, K2Property> Properties { get; set; } = new Dictionary<string, K2Property>();
        public List<K2Mapping> Mappings { get; set; } = new List<K2Mapping>();
        public List<K2ActionResult> Results { get; set; } = new List<K2ActionResult>();
        public bool IsEnabled { get; set; } = true;
        public string Comments { get; set; }

        // For method execution
        public string Method { get; set; }
        public string ObjectId { get; set; }
        public string ObjectName { get; set; }

        // For control-specific actions
        public string ControlId { get; set; }
        public string ControlName { get; set; }

        // For view-specific actions
        public string ViewId { get; set; }
        public string ViewName { get; set; }
    }

    /// <summary>
    /// K2 Property - a named property with value and display info
    /// </summary>
    public class K2Property
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public string DisplayValue { get; set; }
        public string NameValue { get; set; }
        public string ValidationStatus { get; set; }
        public string ValidationMessages { get; set; }
    }

    /// <summary>
    /// K2 Mapping - defines data flow between source and target
    /// </summary>
    public class K2Mapping
    {
        public K2MappingSourceType SourceType { get; set; }
        public string SourceId { get; set; }
        public string SourceName { get; set; }
        public string SourceDisplayName { get; set; }
        public string SourceValue { get; set; }  // For literal values
        public string SourceDataType { get; set; }

        public K2MappingTargetType TargetType { get; set; }
        public string TargetId { get; set; }  // Property name (e.g., "isvisible", "display", "Value")
        public string TargetName { get; set; }
        public string TargetDisplayName { get; set; }
        public string TargetInstanceId { get; set; }

        public string ViewId { get; set; }
        public string ViewName { get; set; }
        public Guid? TargetSubFormGuid { get; set; }
        public Guid? TargetSubFormInstanceGuid { get; set; }
        public Guid? TargetInstanceGuid { get; set; }
    }

    /// <summary>
    /// K2 Action Result - for method execution results
    /// </summary>
    public class K2ActionResult
    {
        public string SourceId { get; set; }
        public string SourceName { get; set; }
        public string TargetId { get; set; }
        public string TargetName { get; set; }
        public string TargetType { get; set; }
    }

    /// <summary>
    /// K2 List Sum Expression - for aggregation calculations
    /// </summary>
    public class K2ListSumExpression
    {
        public string Name { get; set; }
        public string DisplayValue { get; set; }
        public List<K2ExpressionItem> Items { get; set; } = new List<K2ExpressionItem>();
    }

    /// <summary>
    /// K2 Filter - for SmartObject filtering
    /// </summary>
    public class K2Filter
    {
        public bool IsSimple { get; set; } = true;
        public List<K2FilterExpression> Expressions { get; set; } = new List<K2FilterExpression>();
    }

    /// <summary>
    /// K2 Filter Expression - part of a filter
    /// </summary>
    public class K2FilterExpression
    {
        public string Operator { get; set; }  // Equals, NotEquals, Contains, etc.
        public string FieldName { get; set; }
        public string FieldId { get; set; }
        public string Value { get; set; }
        public K2MappingSourceType ValueSourceType { get; set; }
    }
}
