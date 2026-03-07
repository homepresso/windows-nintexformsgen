using System;

namespace SourceCode.Forms.Utilities
{
	public enum TargetType
	{
		Object,
		Method,

		Form,
		View,
		ControlType,
		Event,
		Listener,

		ProcessFolder,
		Process,
		Activity,
		ActivityEvent
	}
	//None,							0x0000000000000000
	//Objects,						0x0000000000000001
	//ObjectProperties,				0x0000000000000002
	//ObjectMethods,				0x0000000000000004
	//ObjectViews,					0x0000000000000008
	//MethodRequiredProperties,		0x0000000000000010
	//MethodOptionalProperties,		0x0000000000000020
	//MethodOptionalParameters,		0x0000000000000040
	//MethodInputs,					0x0000000000000070
	//MethodOutputs,				0x0000000000000080
	//MethodResultProperties,		0x0000000000000080
	//Forms,						0x0000000000000200
	//Views,						0x0000000000000400
	//ControlTypes,					0x0000000000001000
	//ControlTypeEvents,			0x0000000000002000
	//ObjectAssociations,			0x0000000000004000
	//ObjectAssociationProperties,	0x0000000000008000
	//MethodRequiredParameters,		0x0000000000080000
	//FormPanels,					0x0000000000100000
	//FormViews,					0x0000000000200000
	//FormEvents,					0x0000000000400000
	//FormStates,					0x0000000000800000
	//ViewFields,					0x0000000001000000
	//ViewControls,					0x0000000002000000
	//ViewControlEvents,			0x0000000004000000
	//ViewMethods,					0x0000000008000000
	//ViewEvents,					0x0000000010000000
	//SubViews,						0x0000000020000000
	//ViewListingControls,			0x0000000040000000
	//FormParameters,				0x0000000080000000
	//Dependants,					0x0000000100000000
	//Dependencies,					0x0000000200000000
	//FormControls,					0x0000000400000000
	//FormListingControls,			0x0000000800000000
	//ActionExecutionTypes,			0x0000001000000000
	//ActionItemStates,				0x0000002000000000
	//ControlMethods,				0x0000004000000000
	//FormE0xpressions,				0x0000008000000000
	//ProcessFolders,				0x0000010000000000
	//ProcessSets,					0x0000020000000000
	//Processes,					0x0000040000000000
	//Activities,					0x0000080000000000
	//ProcessDataFields,			0x0000100000000000
	//ProcessxmlFields,				0x0000200000000000
	//ActivityDataFields,			0x0000400000000000
	//ActivityxmlFields,			0x0000800000000000
	//Events,						0x0001000000000000
	//ProcessProperties,			0x0002000000000000
	//SubForms,						0x0004000000000000
	//ProcessItemReferences,		0x0008000000000000
	//ViewParameterEvents,			0x0010000000000000
	//FormParameterEvents,			0x0020000000000000
	//ControlFields					0x0040000000000000
	//ControlObjects,				0x0080000000000000
	//RenderMode,					0x0100000000000000
	//ActivityProperties,			0x0200000000000000
	//ViewExpressions,				0x0400000000000000
	//SPACE,                  		0x0800000000000000
	//ControlMethodParameters,		0x1000000000000000
	//ActivitiesWithClientEvents,	0x2000000000000000
	//ControlProperties,			0x4000000000000000
	//ViewParameters,				0x8000000000000000

	//The flags collection below is almost full 
	//Please see above for the sorted list look for "SPACE" and update accordingly
	//If there is no more space speak to Bernard -- Sean
	[Flags]
	public enum ResultTypes : ulong
	{
		None = 0x0000000000000000,

		Objects = 0x0000000000000001,
		ObjectProperties = 0x0000000000000002,
		ObjectMethods = 0x0000000000000004,
		ObjectViews = 0x0000000000000008,
		ObjectAssociations = 0x0000000000004000,
		ObjectAssociationProperties = 0x0000000000008000,

		MethodRequiredProperties = 0x0000000000000010,
		MethodOptionalProperties = 0x0000000000000020,
		MethodParameters = 0x0000000000080040,
		MethodResultProperties = 0x0000000000000080,
		MethodRequiredParameters = 0x0000000000080000,
		MethodOptionalParameters = 0x0000000000000040,

		MethodInputs = 0x0000000000080070,
		MethodOutputs = 0x0000000000000080,

		Forms = 0x0000000000000200,
		Views = 0x0000000000000400,

		ControlTypes = 0x0000000000001000,
		ControlTypeEvents = 0x0000000000002000,
		ControlObjects = 0x0080000000000000,
		ControlMethods = 0x0000004000000000,
		ControlMethodParameters = 0x1000000000000000,
		ControlFields = 0x0040000000000000,
		ControlProperties = 0x4000000000000000,

		FormPanels = 0x0000000000100000,
		FormViews = 0x0000000000200000,
		FormEvents = 0x0000000000400000,
		FormStates = 0x0000000000800000,
		FormControls = 0x0000000400000000,
		FormListingControls = 0x0000000800000000,
		FormParameters = 0x0000000080000000,
		FormExpressions = 0x0000008000000000,

		ViewFields = 0x0000000001000000,
		ViewControls = 0x0000000002000000,
		ViewControlEvents = 0x0000000004000000,
		ViewMethods = 0x0000000008000000,
		ViewEvents = 0x0000000010000000,
		SubViews = 0x0000000020000000,
		SubForms = 0x0004000000000000,

		ViewListingControls = 0x0000000040000000,
		ViewParameters = 0x8000000000000000,
		ViewExpressions = 0x0400000000000000,

		ViewParameterEvents = 0x0010000000000000,
		FormParameterEvents = 0x0020000000000000,

		Dependants = 0x0000000100000000,
		Dependencies = 0x0000000200000000,

		ActionExecutionTypes = 0x0000001000000000,
		ActionItemStates = 0x0000002000000000,


		ProcessFolders = 0x0000010000000000,
		ProcessSets = 0x0000020000000000,
		Processes = 0x0000040000000000,
		Activities = 0x0000080000000000,
		ActivitiesWithClientEvents = 0x2000000000000000,
		Events = 0x0001000000000000,
		ProcessDataFields = 0x0000100000000000,
		ProcessXmlFields = 0x0000200000000000,
		ProcessItemReferences = 0x0008000000000000,
		ProcessProperties = 0x0002000000000000,
		ActivityDataFields = 0x0000400000000000,
		ActivityXmlFields = 0x0000800000000000,
		ActivityProperties = 0x0200000000000000,

		RenderModes = 0x0100000000000000
	}

	public enum ItemType
	{
		Object,
		ObjectAssociation,
		ObjectAssociationProperty,
		ObjectProperty,
		Method,
		MethodParameter,
		MethodRequiredProperty,
		MethodOptionalProperty,
		MethodReturnedProperty,

		Form,
		FormEvent,
		FormParameter,
		FormState,
		Panel,
		Area,

		View,
		ViewEvent,
		ViewMethod,
		FieldContext,
		ViewField,
		ViewParameter,
		Control,
		ControlProperty,
		SubView,
		SubForm,
		Expression,

		ControlType,
		ControlEvent,
		ControlMethod,
		ControlMethodParameter,

		Dependant,
		Dependency,

		ActionExecutionType,
		ActionItemState,

		ProcessFolder,
		ProcessSet,
		Process,
		Activity,
		Event,
		ProcessDataField,
		ProcessXmlField,
		ProcessItemReference,
		ProcessProperty,
		ProcessInstance,
		ActivityDataField,
		ActivityXmlField,
		ActivityProperty,
		Result,

		ViewParameterEvent,
		FormParameterEvent,

		ControlField,

		RenderMode
	}

	[Flags]
	public enum SmartObjectTypes
	{
		None = 0x0000,
		User = 0x001,
		LookUp = 0x002,
		System = 0x004,
		Composite = 0x008
	}

	[Flags]
	public enum StyleResultTypes : long
	{
		Name = 0x0000000000000001,
		LCID = 0x0000000000000002,
		DisplayName = 0x0000000000000004,

		CurrencyDetails = 0x0000000000000008,
		NumberDetails = 0x0000000000000010,
		PercentageDetails = 0x0000000000000020,

		CurrencySymbol = 0x0000000000000040,
		PercentSymbol = 0x0000000000000080,

		NegativeSign = 0x0000000000000100,
		PositiveSign = 0x0000000000000200,

		NaNSymbol = 0x0000000000000400,
		NegativeInfinitySymbol = 0x0000000000000800,
		PositiveInfinitySymbol = 0x0000000000001000,

		DecimalDigits = 0x0000000000002000,
		DecimalSeparator = 0x0000000000004000,
		GroupSeparator = 0x0000000000008000,
		GroupSizes = 0x0000000000010000,
		PositivePattern = 0x0000000000020000,
		NegativePattern = 0x0000000000040000,

		ShortDatePattern = 0x0000000000080000,
		LongDatePattern = 0x0000000000100000,
		ShortTimePattern = 0x0000000000200000,
		LongTimePattern = 0x0000000000400000,
		FullDateTimePattern = 0x0000000000800000,

		SpecialDetails = 0x0000000001000000,

		ShortDateTimePattern = 0x0000000002000000
	}

	public enum PerformanceCategory
	{
		FormsRuntimeWeb,
		FormsRuntimeWebClient,
		FormsDesignerWeb,
		FormsDesignerWebClient,
		FormsClient,
		FormsManager,
		FormsClientServer,
		FormsManagementServer
	}

	public enum PerformanceOperation
	{
		RuntimeWebClientInitialize,
		RuntimeSmartObjectExecution,
		DesignerAppStudioAJAXCall
	}

	[Flags]
	public enum ResultFlags
	{
		None = 0x0000,
		Client = 0x0001
	}
}
