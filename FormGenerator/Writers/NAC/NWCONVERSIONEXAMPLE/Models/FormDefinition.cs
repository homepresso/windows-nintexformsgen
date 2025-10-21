using Newtonsoft.Json;

namespace NWConverter.Models
{
    public class FormDefinition
    {
        [JsonProperty("id")]
        public string Id { get; set; } = "";

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("ruleGroups")]
        public List<object> RuleGroups { get; set; } = new();

        [JsonProperty("theme")]
        public Theme Theme { get; set; } = new();

        [JsonProperty("pageSettings")]
        public PageSettings PageSettings { get; set; } = new();

        [JsonProperty("translationSettings")]
        public TranslationSettings TranslationSettings { get; set; } = new();

        [JsonProperty("version")]
        public int Version { get; set; } = 26;

        [JsonProperty("formType")]
        public string FormType { get; set; } = "startform";

        [JsonProperty("contract")]
        public Contract Contract { get; set; } = new();

        [JsonProperty("variableContext")]
        public VariableContext VariableContext { get; set; } = new();

        [JsonProperty("translations")]
        public Dictionary<string, Dictionary<string, string>> Translations { get; set; } = new();

        [JsonProperty("plugins")]
        public Dictionary<string, object> Plugins { get; set; } = new();

        [JsonProperty("settings")]
        public Settings Settings { get; set; } = new();

        [JsonProperty("rows")]
        public List<Row> Rows { get; set; } = new();
    }

    public class Theme
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "Nintex_Default";

        [JsonProperty("styles")]
        public Styles Styles { get; set; } = new();
    }

    public class Styles
    {
        [JsonProperty("fontFamily")]
        public FontFamily FontFamily { get; set; } = new();

        [JsonProperty("color")]
        public Color Color { get; set; } = new();

        [JsonProperty("textSize")]
        public TextSize TextSize { get; set; } = new();

        [JsonProperty("borderStyle")]
        public string BorderStyle { get; set; } = "Rounded";
    }

    public class FontFamily
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "Open Sans";

        [JsonProperty("value")]
        public string Value { get; set; } = "\"Open Sans\", \"Helvetica\", \"Arial\", sans-serif";
    }

    public class Color
    {
        [JsonProperty("pageBackground")]
        public string PageBackground { get; set; } = "#EFF0F0";

        [JsonProperty("formBackground")]
        public string FormBackground { get; set; } = "#FFFFFF";

        [JsonProperty("userInterface")]
        public string UserInterface { get; set; } = "#575C61";

        [JsonProperty("highlight")]
        public string Highlight { get; set; } = "#006BD6";

        [JsonProperty("textInput")]
        public string TextInput { get; set; } = "#161718";

        [JsonProperty("fieldAndModal")]
        public string FieldAndModal { get; set; } = "#FFFFFF";

        [JsonProperty("error")]
        public string Error { get; set; } = "#C23934";

        [JsonProperty("border")]
        public string Border { get; set; } = "#898F94";
    }

    public class TextSize
    {
        [JsonProperty("textInput")]
        public int TextInput { get; set; } = 14;

        [JsonProperty("formText")]
        public int FormText { get; set; } = 14;
    }

    public class PageSettings
    {
        [JsonProperty("headerText")]
        public string HeaderText { get; set; } = "multiPage.FORMDESIGNER_CONTROL_PROP_HEADER_TEXT";

        [JsonProperty("footerText")]
        public string FooterText { get; set; } = "multiPage.FORMDESIGNER_CONTROL_PROP_FOOTER_TEXT";

        [JsonProperty("pages")]
        public List<Page> Pages { get; set; } = new();

        [JsonProperty("pageTitleSize")]
        public string PageTitleSize { get; set; } = "Heading 4";

        [JsonProperty("pageFlow")]
        public string PageFlow { get; set; } = "1";

        [JsonProperty("pageStyle")]
        public string PageStyle { get; set; } = "1";

        [JsonProperty("showTitle")]
        public bool ShowTitle { get; set; } = true;

        [JsonProperty("showPageNavigation")]
        public bool ShowPageNavigation { get; set; } = true;

        [JsonProperty("showPageNumberIcon")]
        public bool ShowPageNumberIcon { get; set; } = true;

        [JsonProperty("showHeader")]
        public bool ShowHeader { get; set; } = false;

        [JsonProperty("showFooter")]
        public bool ShowFooter { get; set; } = false;
    }

    public class Page
    {
        [JsonProperty("isEnabled")]
        public bool IsEnabled { get; set; } = true;

        [JsonProperty("name")]
        public string Name { get; set; } = "page_default";

        [JsonProperty("title")]
        public string Title { get; set; } = "page_default.FORMDESIGNER_CONTROL_PROP_TITLE";
    }

    public class TranslationSettings
    {
        [JsonProperty("selectedLanguages")]
        public List<string> SelectedLanguages { get; set; } = new();

        [JsonProperty("participantLanguage")]
        public string ParticipantLanguage { get; set; } = "en";

        [JsonProperty("baseLanguage")]
        public string BaseLanguage { get; set; } = "en";

        [JsonProperty("enableRuntimeLanguageSelector")]
        public bool EnableRuntimeLanguageSelector { get; set; } = true;
    }

    public class Contract
    {
        [JsonProperty("version")]
        public string Version { get; set; } = "v3";

        [JsonProperty("variablePrefix")]
        public string VariablePrefix { get; set; } = "se";
    }

    public class VariableContext
    {
        [JsonProperty("variables")]
        public List<object> Variables { get; set; } = new();
    }

    public class Settings
    {
        [JsonProperty("TimeZone")]
        public string TimeZone { get; set; } = "Eastern Standard Time";

        [JsonProperty("Region")]
        public string Region { get; set; } = "en-US";

        [JsonProperty("TimeFormat")]
        public string TimeFormat { get; set; } = "12H";

        [JsonProperty("GeneralDefaultFileUpLoadControl")]
        public string GeneralDefaultFileUpLoadControl { get; set; } = "ntx-no-default-control";

        [JsonProperty("GeneralSetValueRulesRunOption")]
        public int GeneralSetValueRulesRunOption { get; set; } = 0;
    }

    public class Row
    {
        [JsonProperty("controls")]
        public List<Control> Controls { get; set; } = new();

        [JsonProperty("sizes")]
        public List<int> Sizes { get; set; } = new();

        [JsonProperty("autoResizing")]
        public bool AutoResizing { get; set; } = true;

        [JsonProperty("restrictions")]
        public Dictionary<string, object> Restrictions { get; set; } = new();

        [JsonProperty("pageName")]
        public string PageName { get; set; } = "page_default";

        [JsonProperty("id")]
        public string Id { get; set; } = "";
    }

    public class Control
    {
        [JsonProperty("id")]
        public string Id { get; set; } = "";

        [JsonProperty("dataType")]
        public string DataType { get; set; } = "";

        [JsonProperty("widget")]
        public string Widget { get; set; } = "";

        [JsonProperty("widgetMinimumSize")]
        public int? WidgetMinimumSize { get; set; }

        [JsonProperty("source")]
        public Source Source { get; set; } = new();

        [JsonProperty("properties")]
        public ControlProperties Properties { get; set; } = new();

        [JsonProperty("sourceVariable")]
        public SourceVariable SourceVariable { get; set; } = new();

        [JsonProperty("rows")]
        public List<Row>? Rows { get; set; }
    }

    public class Source
    {
        [JsonProperty("createdBy")]
        public string CreatedBy { get; set; } = "user";
    }

    public class ControlProperties
    {
        [JsonProperty("required")]
        public bool Required { get; set; } = false;

        [JsonProperty("readOnly")]
        public bool ReadOnly { get; set; } = false;

        [JsonProperty("visible")]
        public bool Visible { get; set; } = true;

        [JsonProperty("isConnectedToVariable")]
        public bool IsConnectedToVariable { get; set; } = true;

        [JsonProperty("customizeName")]
        public bool CustomizeName { get; set; } = false;

        [JsonProperty("validationErrors")]
        public List<object> ValidationErrors { get; set; } = new();

        [JsonProperty("connectedToManagedFields")]
        public List<object> ConnectedToManagedFields { get; set; } = new();

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("title")]
        public string Title { get; set; } = "";

        [JsonProperty("format")]
        public string Format { get; set; } = "";

        [JsonProperty("helpText")]
        public string HelpText { get; set; } = "";

        [JsonProperty("tooltip")]
        public string Tooltip { get; set; } = "";

        [JsonProperty("helpLink")]
        public string HelpLink { get; set; } = "";

        [JsonProperty("helpLinkLabel")]
        public string HelpLinkLabel { get; set; } = "";

        [JsonProperty("requiredValidationMessage")]
        public string RequiredValidationMessage { get; set; } = "";

        [JsonProperty("defaultValue")]
        public string DefaultValue { get; set; } = "";

        [JsonProperty("connectedVariableId")]
        public string ConnectedVariableId { get; set; } = "";

        [JsonProperty("controlFillColor")]
        public string ControlFillColor { get; set; } = "";

        [JsonProperty("borderColor")]
        public string BorderColor { get; set; } = "";

        [JsonProperty("inputColor")]
        public string InputColor { get; set; } = "";

        [JsonProperty("backgroundColor")]
        public string BackgroundColor { get; set; } = "";

        [JsonProperty("containerBackgroundColor")]
        public string ContainerBackgroundColor { get; set; } = "";

        [JsonProperty("placeholder")]
        public string Placeholder { get; set; } = "";

        [JsonProperty("hideValidationErrors")]
        public bool HideValidationErrors { get; set; } = false;

        // DateTime control properties
        [JsonProperty("showDateOnly")]
        public bool? ShowDateOnly { get; set; }

        [JsonProperty("showSetTimeZone")]
        public bool? ShowSetTimeZone { get; set; }

        [JsonProperty("dateTimeZone")]
        public string? DateTimeZone { get; set; }

        [JsonProperty("restrictPastDates")]
        public bool? RestrictPastDates { get; set; }

        // Multiline text properties
        [JsonProperty("type")]
        public string? Type { get; set; }

        [JsonProperty("textAreaRows")]
        public string? TextAreaRows { get; set; }

        [JsonProperty("autoResize")]
        public bool? AutoResize { get; set; }

        // Repeating section properties
        [JsonProperty("showHeader")]
        public bool? ShowHeader { get; set; }

        [JsonProperty("showBorder")]
        public bool? ShowBorder { get; set; }

        [JsonProperty("translateHeader")]
        public bool? TranslateHeader { get; set; }

        [JsonProperty("isNestedControl")]
        public bool? IsNestedControl { get; set; }

        [JsonProperty("showHeaderBackground")]
        public bool? ShowHeaderBackground { get; set; }

        [JsonProperty("showHeaderDivider")]
        public bool? ShowHeaderDivider { get; set; }

        [JsonProperty("showExpandable")]
        public bool? ShowExpandable { get; set; }

        [JsonProperty("collapsedByDefault")]
        public bool? CollapsedByDefault { get; set; }

        [JsonProperty("showOnlyHeaderDivider")]
        public bool? ShowOnlyHeaderDivider { get; set; }

        [JsonProperty("addRowButtonLabel")]
        public string? AddRowButtonLabel { get; set; }

        [JsonProperty("minRows")]
        public int? MinRows { get; set; }

        [JsonProperty("maxRows")]
        public int? MaxRows { get; set; }

        [JsonProperty("defaultRows")]
        public int? DefaultRows { get; set; }

        [JsonProperty("alternateBackgroundColour")]
        public bool? AlternateBackgroundColour { get; set; }

        // Action panel properties
        [JsonProperty("captchaEnabled")]
        public bool? CaptchaEnabled { get; set; }

        [JsonProperty("showSubmitButton")]
        public bool? ShowSubmitButton { get; set; }

        [JsonProperty("showSaveAndContinueButton")]
        public bool? ShowSaveAndContinueButton { get; set; }

        [JsonProperty("isCancelRedirectUrlEnabled")]
        public bool? IsCancelRedirectUrlEnabled { get; set; }

        [JsonProperty("showAfterSubmitPrintButton")]
        public bool? ShowAfterSubmitPrintButton { get; set; }

        [JsonProperty("afterSubmitPrintButtonLabel")]
        public string? AfterSubmitPrintButtonLabel { get; set; }

        [JsonProperty("submitButtonText")]
        public string? SubmitButtonText { get; set; }

        [JsonProperty("afterSubmitType")]
        public string? AfterSubmitType { get; set; }

        [JsonProperty("nextButtonText")]
        public string? NextButtonText { get; set; }

        [JsonProperty("previousButtonText")]
        public string? PreviousButtonText { get; set; }

        [JsonProperty("saveAndContinueButtonText")]
        public string? SaveAndContinueButtonText { get; set; }

        [JsonProperty("cancelButtonText")]
        public string? CancelButtonText { get; set; }

        // Choice control properties
        [JsonProperty("viewType")]
        public string? ViewType { get; set; }

        [JsonProperty("layoutType")]
        public string? LayoutType { get; set; }

        [JsonProperty("showPleaseSelect")]
        public bool? ShowPleaseSelect { get; set; }

        [JsonProperty("searchable")]
        public bool? Searchable { get; set; }

        [JsonProperty("items")]
        public string? Items { get; set; }

        [JsonProperty("showDescription")]
        public bool? ShowDescription { get; set; }

        [JsonProperty("showHelpText")]
        public bool? ShowHelpText { get; set; }

        [JsonProperty("allowFillInOption")]
        public bool? AllowFillInOption { get; set; }

        [JsonProperty("fillInTextMaxLength")]
        public int? FillInTextMaxLength { get; set; }

        [JsonProperty("fillInPlaceholder")]
        public string? FillInPlaceholder { get; set; }

        [JsonProperty("filterDuplicateOptions")]
        public bool? FilterDuplicateOptions { get; set; }

        [JsonProperty("editableSearchTerm")]
        public bool? EditableSearchTerm { get; set; }

        // Repeating section template
        [JsonProperty("templateFormJson")]
        public string? TemplateFormJson { get; set; }

        [JsonProperty("templateForm")]
        public object? TemplateForm { get; set; }

        [JsonProperty("repeatingSectionDefaultValueType")]
        public string? RepeatingSectionDefaultValueType { get; set; }

        [JsonProperty("repeatingSectionDefaultValue")]
        public string? RepeatingSectionDefaultValue { get; set; }

        [JsonProperty("repeatingSectionJsonDefaultValue")]
        public object[]? RepeatingSectionJsonDefaultValue { get; set; }

        // Rich text label properties
        [JsonProperty("text")]
        public string? Text { get; set; }

        // Group control properties  
        [JsonProperty("rows")]
        public object[]? Rows { get; set; }

        [JsonProperty("headerText")]
        public string? HeaderText { get; set; }
    }

    public class SourceVariable
    {
        [JsonProperty("id")]
        public string Id { get; set; } = "";

        [JsonProperty("displayName")]
        public string DisplayName { get; set; } = "";

        [JsonProperty("autoGenerateName")]
        public bool AutoGenerateName { get; set; } = true;
    }
}
