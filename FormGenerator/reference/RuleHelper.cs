using SourceCode.Forms.Authoring;
using SourceCode.Forms.Authoring.Eventing;
using SourceCode.Forms.Authoring.Filters;
using SourceCode.Forms.Utilities.Design;
using SourceCode.Hosting.Client.BaseAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml;
using WSA = SourceCode.Forms.Authoring;
using WSC = SourceCode.Forms.Client;
using WSF = SourceCode.Framework;
using WSM = SourceCode.Forms.Management;

namespace SourceCode.Forms.Utilities
{
    public class RuleHelper : IDisposable
    {
        /// <summary>
        /// The Artifact context type
        /// </summary>
        public enum ContextType
        {
            /// <summary>
            /// Context type not specified
            /// </summary>
            UNDEFINED,
            /// <summary>
            /// SmartForms Form
            /// </summary>
            FORM,
            /// <summary>
            /// SmartForms View
            /// </summary>
            VIEW,
            /// <summary>
            /// SmartForms Control
            /// </summary>
            CONTROL
        }

        /// <summary>
        /// Parses the Context type from a string value
        /// </summary>
        /// <param name="contextType"></param>
        /// <returns></returns>
        public static ContextType ParseContextType(string contextType)
        {
            ContextType returnType;
            if (!string.IsNullOrWhiteSpace(contextType) && Enum.TryParse(contextType.ToUpperInvariant(), out returnType))
            {
                return returnType;
            }
            return ContextType.UNDEFINED;
        }

        public IStateContainer Origin { get; set; }

        #region Constructors
        private RuleHelper(HashSet<string> enabledFeatures)
        {
            if (enabledFeatures != null)
                _enabledFeatures = enabledFeatures;
            else
                _enabledFeatures = new HashSet<string>();
        }

        public RuleHelper(string connectionString, HashSet<string> enabledFeatures = null) : this(enabledFeatures)
        {
            _connectionString = connectionString;
        }

        public RuleHelper(string connectionString, IRuleInfoProvider infoProvider, HashSet<string> enabledFeatures = null)
            : this(connectionString, enabledFeatures)
        {
            _infoProvider = infoProvider;
        }

        public RuleHelper(BaseAPIConnection connection, HashSet<string> enabledFeatures = null) : this(enabledFeatures)
        {
            _connection = connection;
        }

        public RuleHelper(BaseAPIConnection connection, IRuleInfoProvider infoProvider, HashSet<string> enabledFeatures = null)
            : this(connection, enabledFeatures)
        {
            _infoProvider = infoProvider;
        }

        #endregion

        #region IDisposable Members

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            if ((_closeConnection) && (_connection != null))
            {
                _connection.Close();
                _connection = null;
                _closeConnection = false;
            }
        }

        #endregion
        #region Static members
        private static Dictionary<string, XmlDocument> _ruleDefinitionsCache = new Dictionary<string, XmlDocument>();
        private Dictionary<Guid, IBaseNamedObject> _formAndViewCache = new Dictionary<Guid, IBaseNamedObject>();
        private Helper _helper = new Helper();
        #endregion

        #region Fields
        private readonly string _connectionString;
        private WSC.FormsClient _wsClient;
        private WSM.FormsManager _wsManager;
        private BaseAPIConnection _connection;
        private IRuleInfoProvider _infoProvider;
        private List<Guid> _eventBuildContextPerformed = new List<Guid>();
        private bool _closeConnection = false;
        private HashSet<string> _enabledFeatures;
        #endregion

        #region Properties
        public IRuleInfoProvider InfoProvider
        {
            get
            {
                if (_infoProvider == null)
                {
                    _infoProvider = new RuleInfoProvider(GetConnection());
                }

                return _infoProvider;
            }

            set
            {
                _infoProvider = value;
            }
        }

        private XmlDocument ruleDefinition { get; set; }
        #endregion

        #region Nested Struct: InvalidPart
        public struct InvalidPart
        {
            public string DisplayText;
            public WSF.ValidationError Error;

            public InvalidPart(string DisplayText, WSF.ValidationError Error)
            {
                this.DisplayText = DisplayText;
                this.Error = Error;
            }
        }
        #endregion

        #region Nested Class : Context
        public class Context
        {
            private Dictionary<string, InvalidPart> _invalidPartsDictionary;
            private string _eventName;

            public string RuleEventName { get; set; }
            public string RuleFriendlyName { get; set; }
            public string RuleActionName { get; set; }
            public string RuleConditionName { get; set; }
            public string ActionFriendlyName { get; set; }
            public string EventFriendlyName { get; set; }
            public string ConditionFriendlyName { get; set; }
            public string HandlerFriendlyName { get; set; }
            public string formName { get; set; }
            public string viewName { get; set; }
            public string viewMviName { get; set; }
            public string formSystemName { get; set; }
            public string viewSystemName { get; set; }
            public string controlName { get; set; }
            public string controlSystemName { get; set; }
            public string panelName { get; set; }
            public string panelSystemName { get; set; }
            public string parameterName { get; set; }
            public string parameterDisplayName { get; set; }
            public string parameterDataType { get; set; }
            public Guid parameterGuid { get; set; }
            public ViewParameter viewParameter { get; set; }
            public FormParameter formParameter { get; set; }
            public string methodDisplayName { get; set; }
            public string ObjectName { get; set; }
            public string ObjectSystemName { get; set; }
            public WSA.Form Form { get; set; }
            public WSA.View View { get; set; }
            public WSA.Control Control { get; set; }
            public WSA.Panel Panel { get; set; }
            public Guid formGuid { get; set; }
            public Guid viewGuid { get; set; }
            public Guid panelGuid { get; set; }
            public Guid controlGuid { get; set; }
            public Guid InstanceGuid { get; set; }
            public Guid SubformGuid { get; set; }
            public Guid SubformInstanceGuid { get; set; }
            public Guid EventGuid { get; set; }
            public Guid TargetEventGuid { get; set; } //used for execute another Rule
            public String OperatorValue { get; set; }
            public String Operator { get; set; }
            public String Location { get; set; }
            public Event Event { get; set; }
            public Authoring.Eventing.Action Action { get; set; }
            public Authoring.Eventing.Action SubItemAction { get; set; }
            public Authoring.Eventing.Condition Condition { get; set; }
            public LogicalExpression LogicalExpression { get; set; }
            public Guid ObjectGuid { get; set; }
            public HandlerType handlerType { get; set; }
            public string RuleHandlerName { get; set; }
            public string context { get; set; }
            public XmlDocument ruleDefinition { get; set; }
            public XmlDocument ruleInstance { get; set; }
            public ActionItemState itemState { get; set; }
            public Handler handler { get; set; }
            public HandlerFunction handlerFunction { get; set; }
            public int processID { get; set; }
            public string processFullName { get; set; }
            public string processDisplayName { get; set; }
            public string activityDisplayName { get; set; }
            public string activityName { get; set; }
            public string activityFullName { get; set; }
            public string renderMode { get; set; }
            public string viewType { get; set; }

            public Dictionary<string, InvalidPart> InvalidPartsDictionary
            {
                get
                {
                    if (_invalidPartsDictionary == null)
                    {
                        _invalidPartsDictionary = new Dictionary<string, InvalidPart>();
                    }

                    return _invalidPartsDictionary;
                }
            }

            public String EventName
            {
                get
                {
                    return _eventName;
                }
                set { _eventName = value; }
            }
        }
        #endregion

        #region Nested Class : ValidationMessageParts
        internal struct ValidationMessageParts
        {
            internal string ReferenceAs, RefName, RefDisplayName;
            internal ReferenceType RefType;
            internal ReferenceStatus RefStatus;
            internal Guid RefGuid;
            internal WSF.ValidationError SourceError;

            internal ValidationMessageParts(WSF.ValidationError SourceError)
            {
                this.SourceError = SourceError;
                ReferenceAs = string.Empty;
                RefName = string.Empty;
                RefDisplayName = string.Empty;
                RefType = ReferenceType.Unknown;
                RefStatus = ReferenceStatus.None;
                RefGuid = Guid.Empty;
                string message = this.SourceError.Message;

                if (string.IsNullOrEmpty(message))
                    return;

                Regex regSplit = new Regex("(?:^|,)(\"(?:[^\"]+|\"\")*\"|[^,]*)", RegexOptions.Compiled);
                List<string> list = new List<string>();
                string curr = null;
                foreach (Match match in regSplit.Matches(message))
                {
                    curr = match.Value;
                    if (0 == curr.Length)
                    {
                        list.Add("");
                    }

                    list.Add(curr.TrimStart(','));
                }

                var messageStrings = list.ToArray<string>();

                if (messageStrings.Length == 3 && messageStrings[0] == "ValidationError")
                {
                    ReferenceAs = messageStrings[0];
                    RefType = Design.ReferenceType.Parse(messageStrings[1]);
                    RefStatus = ReferenceStatus.Error;
                }
                else
                {
                    if (messageStrings.Length > 0)
                    {
                        ReferenceAs = messageStrings[0];
                    }

                    if (messageStrings.Length > 1)
                    {
                        RefType = Design.ReferenceType.Parse(messageStrings[1]);
                    }

                    if (messageStrings.Length > 2)
                    {
                        EnumParser<ReferenceStatus>.TryParse(messageStrings[2], false, out RefStatus);
                    }

                    if (messageStrings.Length > 3)
                    {
                        GuidHelper.TryParse(messageStrings[3], out RefGuid);
                    }

                    if (messageStrings.Length > 4)
                    {
                        RefName = messageStrings[4];
                    }

                    if (messageStrings.Length > 5)
                    {
                        RefDisplayName = messageStrings[5];
                    }
                }
            }
        }
        #endregion

        #region Public Methods
        public string GetHandlerFriendlyName(Authoring.Eventing.Handler handler)
        {
            Context context = BuildContext(handler);

            return context.HandlerFriendlyName;
        }

        /// <summary>
        /// This method assumes that the analyzer has already been run on the containing View or Form and no longer applies validation to the Event.
        /// </summary>
        /// <param name="ev">The Event for which to retrieve the human readable generated name</param>
        /// <param name="useExistingValue">Added for P&D as they do not want to do validation. This will be refactored at a later stage</param>
        public string GetEventFriendlyName(Authoring.Eventing.Event ev, Boolean? useExistingValue = false)
        {
            Context context = null;
            string friendlyName = string.Empty;

            if (useExistingValue == true && !string.IsNullOrEmpty(ev.Properties["RuleFriendlyName"]))
            {
                friendlyName = ev.Properties["RuleFriendlyName"];
            }
            else
            {
                context = BuildContext(ev);
                friendlyName = context.RuleFriendlyName;
            }

            return friendlyName;
        }

        /// <summary>
        /// This method assumes that the analyzer has already been run on the containing View or Form and no longer applies validation to the Rule.
        /// </summary>
        public string GetRuleFriendlyName(Authoring.Eventing.Event ev)
        {
            Context context = BuildContext(ev);

            return context.RuleFriendlyName;
        }

        /// <summary>
        /// This method assumes that the analyzer has already been run on the containing View or Form and no longer applies validation to the Action.
        /// </summary>
        public string GetActionFriendlyName(Authoring.Eventing.Action action)
        {
            Context context = BuildContext(action);

            return context.ActionFriendlyName;
        }

        /// <summary>
        /// This method assumes that the analyzer has already been run on the containing View or Form and no longer applies validation to the Event.
        /// </summary>
        public string GetEventLocation(Authoring.Eventing.Event ev)
        {
            Context context = BuildContext(ev);

            return context.Location;
        }

        /// <summary>
        /// This method assumes that the analyzer has already been run on the containing View or Form and no longer applies validation to the Event.
        /// </summary>
        public Context GetRuleContext(Authoring.Eventing.Event ev)
        {
            Context context = BuildContext(ev);

            return context;
        }

        public string GetConditionFriendlyName(Authoring.Eventing.Condition condition)
        {
            string value = string.Empty;

            foreach (Authoring.Filters.LogicalExpression lp in condition.Expressions)
            {
                Context context = BuildContext(lp, condition);

                value = context.ConditionFriendlyName;
            }

            return value;
        }

        public string TransformAuthoringDefinitionToRuleDefinition(string context, string xmlDefinition, string stateID, Guid eventID, bool transformAll = false)
        {
            var contextType = ParseContextType(context);
            ruleDefinition = GetRuleDefinition(contextType);
            XmlDocument ruleInstance = new XmlDocument();
            ruleInstance.PreserveWhitespace = true;
            XmlNode rulesNode = ruleInstance.CreateElement("Rules");
            ruleInstance.AppendChild(rulesNode);

            switch (contextType)
            {
                case ContextType.VIEW:
                case ContextType.CONTROL:
                    var view = new View(xmlDefinition);
                    Origin = view;

                    using (var analyzer = ConnectionClass.GetAnalyzer(GetConnection(), _enabledFeatures))
                    {
                        analyzer.Analyze(view);
                    }

                    foreach (Event ev in view.Events)
                    {
                        if (ev.EventType == EventType.User && (transformAll || eventID.Equals(ev.Guid)))
                        {
                            XmlNode ruleNode = ruleInstance.CreateElement("Rule");
                            XmlNode ruleNameNode = ruleInstance.CreateElement("Name");
                            XmlNode ruleFriendlyNameNode = ruleInstance.CreateElement("FriendlyName");
                            XmlNode ruleDescriptionNode = ruleInstance.CreateElement("Description");
                            XmlNode eventsNode = ruleInstance.CreateElement("Events");
                            XmlNode handlersNode = ruleInstance.CreateElement("Handlers");
                            XmlNode eventNode = ruleInstance.CreateElement("Event");

                            ruleNode.Attributes.Append(ruleInstance.CreateAttribute("ID"));
                            ruleNode.Attributes["ID"].Value = ev.Guid.ToString();
                            eventNode.Attributes.Append(ruleInstance.CreateAttribute("IsCurrentHandler"));
                            eventNode.Attributes["IsCurrentHandler"].Value = (!ev.IsReference).ToString();
                            eventNode.Attributes.Append(ruleInstance.CreateAttribute("ID"));
                            eventNode.Attributes["ID"].Value = ev.Guid.ToString().ToLowerInvariant();
                            eventNode.Attributes.Append(ruleInstance.CreateAttribute("DefinitionID"));
                            eventNode.Attributes["DefinitionID"].Value = ev.DefinitionGuid.ToString().ToLowerInvariant();

                            if (!string.IsNullOrEmpty(ev.Properties["Comments"]))
                            {
                                XmlNode commentsNode = ruleInstance.CreateElement("Comments");
                                commentsNode.AppendChild(ruleInstance.CreateTextNode(ev.Properties["Comments"]));
                                eventNode.AppendChild(commentsNode);
                            }

                            ruleNode.AppendChild(ruleNameNode);
                            ruleNode.AppendChild(ruleFriendlyNameNode);
                            ruleNode.AppendChild(ruleDescriptionNode);
                            if (!string.IsNullOrEmpty(ev.Properties["IsCustomName"]))
                            {
                                XmlNode isCustomNameNode = eventNode.OwnerDocument.CreateElement("IsCustomName");
                                isCustomNameNode.AppendChild(eventNode.OwnerDocument.CreateTextNode(ev.Properties["IsCustomName"]));
                                ruleNode.AppendChild(isCustomNameNode);
                            }
                            ruleNode.AppendChild(eventsNode);
                            eventsNode.AppendChild(eventNode);
                            ruleNode.AppendChild(handlersNode);
                            rulesNode.AppendChild(ruleNode);

                            if (!string.IsNullOrEmpty(ev.Properties["SingleSpinner"]))
                            {
                                XmlNode isSingleSpinnerNameNode = eventNode.OwnerDocument.CreateElement("SingleSpinner");
                                isSingleSpinnerNameNode.AppendChild(eventNode.OwnerDocument.CreateTextNode(ev.Properties["SingleSpinner"]));
                                ruleNode.AppendChild(isSingleSpinnerNameNode);
                            }

                            //Create EventXml in Rule Format
                            Context eventContext = BuildRuleEvent(ev, eventNode);

                            ruleNameNode.AppendChild(ruleInstance.CreateTextNode(ev.Properties["RuleName"]));
                            ruleFriendlyNameNode.AppendChild(ruleInstance.CreateTextNode(eventContext.RuleFriendlyName));
                            ruleDescriptionNode.AppendChild(ruleInstance.CreateTextNode(ev.Properties["RuleDescription"]));

                            foreach (Authoring.Eventing.Handler handler in ev.Handlers)
                            {
                                TransformAuthoringHandlerToRuleHandler(context, ruleDefinition, ruleInstance, handler, handlersNode);
                            }
                        }
                    }

                    break;
                case ContextType.FORM:
                    var form = new Form(xmlDefinition);
                    Origin = form;

                    //Remove states not required

                    int i = form.States.Count;
                    while (i-- > 0)
                    {
                        State state = form.States[i];
                        bool isStateWeAreTransforming = (state.IsBase || (!string.IsNullOrEmpty(stateID) && state.Guid.Equals(new Guid(stateID))));
                        if (!isStateWeAreTransforming)
                        {
                            form.States.Remove(state);
                        }
                    }

                    EventStubHelper eventStubHelper = new EventStubHelper();
                    eventStubHelper.DeStub(form);

                    using (var analyzer = ConnectionClass.GetAnalyzer(GetConnection(), _enabledFeatures))
                    {
                        analyzer.Analyze(form);
                    }

                    foreach (State state in form.States)
                    {
                        if ((string.IsNullOrEmpty(stateID) && state.IsBase) || (!string.IsNullOrEmpty(stateID) && state.Guid.Equals(new Guid(stateID))))
                        {
                            foreach (Event ev in state.Events)
                            {
                                if (ev.EventType == EventType.User && (transformAll || eventID.Equals(ev.Guid)))
                                {
                                    XmlNode ruleNode = ruleInstance.CreateElement("Rule");
                                    XmlNode ruleNameNode = ruleInstance.CreateElement("Name");
                                    XmlNode ruleFriendlyNameNode = ruleInstance.CreateElement("FriendlyName");
                                    XmlNode ruleDescriptionNode = ruleInstance.CreateElement("Description");
                                    XmlNode eventsNode = ruleInstance.CreateElement("Events");
                                    XmlNode handlersNode = ruleInstance.CreateElement("Handlers");
                                    XmlNode eventNode = ruleInstance.CreateElement("Event");

                                    ruleNode.Attributes.Append(ruleInstance.CreateAttribute("ID"));
                                    ruleNode.Attributes["ID"].Value = ev.Guid.ToString();
                                    eventNode.Attributes.Append(ruleInstance.CreateAttribute("IsCurrentHandler"));
                                    eventNode.Attributes["IsCurrentHandler"].Value = (!ev.IsReference).ToString();
                                    eventNode.Attributes.Append(ruleInstance.CreateAttribute("ID"));
                                    eventNode.Attributes["ID"].Value = ev.Guid.ToString().ToLowerInvariant();
                                    eventNode.Attributes.Append(ruleInstance.CreateAttribute("DefinitionID"));
                                    eventNode.Attributes["DefinitionID"].Value = ev.DefinitionGuid.ToString().ToLowerInvariant();

                                    if (!string.IsNullOrEmpty(ev.Properties["Comments"]))
                                    {
                                        XmlNode commentsNode = ruleInstance.CreateElement("Comments");
                                        commentsNode.AppendChild(ruleInstance.CreateTextNode(ev.Properties["Comments"]));
                                        eventNode.AppendChild(commentsNode);
                                    }

                                    ruleNode.AppendChild(ruleNameNode);
                                    ruleNode.AppendChild(ruleFriendlyNameNode);
                                    ruleNode.AppendChild(ruleDescriptionNode);
                                    if (!string.IsNullOrEmpty(ev.Properties["IsCustomName"]))
                                    {
                                        XmlNode isCustomNameNode = eventNode.OwnerDocument.CreateElement("IsCustomName");
                                        isCustomNameNode.AppendChild(eventNode.OwnerDocument.CreateTextNode(ev.Properties["IsCustomName"]));
                                        ruleNode.AppendChild(isCustomNameNode);
                                    }
                                    ruleNode.AppendChild(eventsNode);
                                    eventsNode.AppendChild(eventNode);
                                    ruleNode.AppendChild(handlersNode);
                                    rulesNode.AppendChild(ruleNode);

                                    if (!state.IsBase)
                                    {
                                        eventNode.Attributes.Append(ruleInstance.CreateAttribute("StateID"));
                                        eventNode.Attributes["StateID"].Value = state.Guid.ToString();
                                    }

                                    //Create EventXml in Rule Format
                                    Context eventContext = BuildRuleEvent(ev, eventNode);

                                    ruleNameNode.AppendChild(ruleInstance.CreateTextNode(ev.Properties["RuleName"]));
                                    ruleFriendlyNameNode.AppendChild(ruleInstance.CreateTextNode(eventContext.RuleFriendlyName));
                                    ruleDescriptionNode.AppendChild(ruleInstance.CreateTextNode(ev.Properties["RuleDescription"]));

                                    foreach (Authoring.Eventing.Handler handler in ev.Handlers)
                                    {
                                        TransformAuthoringHandlerToRuleHandler(context, ruleDefinition, ruleInstance, handler, handlersNode);
                                    }
                                }
                            }
                        }
                    }
                    break;
            }

            return ruleInstance.InnerXml;
        }

        public EventCollection TransformRuleDefinitionToAuthoringDefinition(string context, XmlDocument ruleInstance, EventCollection eventCollection, View currentView, Authoring.Form currentForm, Guid ruleGuid)
        {
            var contextType = ParseContextType(context);
            ruleDefinition = GetRuleDefinition(contextType);
            PushToFormAndViewCache(currentForm);
            PushToFormAndViewCache(currentView);

            XmlNode ruleNode = ruleInstance.SelectSingleNode("Rules/Rule[@ID='" + ruleGuid + "']");
            Guid instanecGuid = ruleNode.Attributes["InstanceID"] != null ? new Guid(ruleNode.Attributes["InstanceID"].Value) : Guid.Empty;
            XmlNode currentEventNode = ruleNode.SelectSingleNode("Events/Event");
            bool eventIsReference = true;
            if (currentEventNode != null && currentEventNode.Attributes["IsCurrentHandler"] != null)
            {
                eventIsReference = bool.Parse(currentEventNode.Attributes["IsCurrentHandler"].Value);
            }
            int eventIndex = eventCollection.Count == 0 ? 0 : eventCollection.Count;
            Guid eventDefinitionGuid = Guid.Empty;
            Event existingEvent = null;

            if (currentEventNode != null && currentEventNode.Attributes["DefinitionID"] != null)
            {
                eventDefinitionGuid = new Guid(currentEventNode.Attributes["DefinitionID"].Value);
            }

            // Event //
            Event ev = new Event(ruleGuid);

            if (eventCollection.Contains(ruleGuid))
            {
                eventIndex = eventCollection.IndexOf(eventCollection[ruleGuid]);
                existingEvent = eventCollection[ruleGuid];
                eventCollection.Remove(ruleGuid);
            }

            eventCollection.Insert(eventIndex, ev);

            ev.IsReference = !eventIsReference;
            if (eventDefinitionGuid != Guid.Empty) { ev.DefinitionGuid = eventDefinitionGuid; }

            if (instanecGuid != Guid.Empty) { ev.InstanceGuid = instanecGuid; }
            ev = CreateEventFromRuleXML(ruleNode, currentForm, currentView, ev, existingEvent);
            // Event //

            ev.Properties.Set("RuleDescription", ruleNode.SelectSingleNode("Description").InnerText);
            XmlNode isCustomName = ruleNode.SelectSingleNode("IsCustomName");
            if (isCustomName != null)
            {
                ev.Properties.Set("IsCustomName", isCustomName.InnerText);
                ev.Properties.Set("RuleName", ruleNode.SelectSingleNode("Name").InnerText);
            }

            //Check if inheritance was lost
            if (existingEvent != null)
            {
                if (ev.IsReference && existingEvent.IsInherited)
                {
                    ev.IsInherited = true;
                }
                if (!existingEvent.IsEnabled.Equals(ev.IsEnabled) && ev.IsReference)
                {
                    ev.IsInherited = false;
                }

                if (ev.IsReference)
                {
                    ev.InstanceGuid = existingEvent.InstanceGuid;
                }
            }

            Dictionary<Guid, WSA.Eventing.Action> existingHandlerActionCollection = new Dictionary<Guid, WSA.Eventing.Action>();
            Dictionary<Guid, WSA.Eventing.Condition> existingHandlerConditionsCollection = new Dictionary<Guid, Condition>();
            Dictionary<Guid, WSA.Eventing.Handler> existingHandlersCollection = new Dictionary<Guid, Handler>();

            if (existingEvent != null)
            {
                AddItemsToCollections(existingEvent, existingHandlerActionCollection, existingHandlersCollection, existingHandlerConditionsCollection);
            }

            // Handlers //
            XmlNodeList handlersNode = ruleNode.SelectNodes("Handlers/Handler");

            foreach (XmlNode handlerNode in handlersNode)
            {
                var handler = CreateHandlerFromRuleXML(handlerNode, existingHandlersCollection, context);

                //	// Add Handler to event //
                ev.Handlers.Add(handler);
                //	// Add Handler to event //

                // Conditions //
                XmlNodeList ruleConditionsNodes = handlerNode.SelectNodes("Conditions/Condition");
                CreateConditionsFromRuleXML(ruleNode, ev, context, currentForm, handler, ruleConditionsNodes, existingHandlerConditionsCollection, existingHandlersCollection);
                //Conditions //

                // Actions //
                XmlNodeList ruleListenersNode = handlerNode.SelectNodes("Actions/Action");
                CreateActionsFromRuleXML(ruleNode, ev, context, currentView, currentForm, handler, ruleListenersNode, existingHandlerActionCollection, existingHandlerConditionsCollection, existingHandlersCollection);
                // Actions //
            }

            //reset for testing
            ev.IsExtended = false;

            ev.IsExtended = Helper.IsExtended(ev);

            // Merge to sub-states
            if (ev.Form != null)
            {
                _helper.SetSubFormState(ev.State);
                foreach (State state in ev.Form.States)
                {
                    if (state.ParentGuid == ev.State.Guid)
                    {
                        _helper.MergeState(ev.State, state, Guid.Empty, Guid.Empty, true, false);
                    }
                }
            }
            else
            {
                _helper.SetSubFormState(ev.View.States[0]);
            }

            using (var formsAnalyzer = ConnectionClass.GetAnalyzer(GetConnection(), _enabledFeatures))
            {
                if (currentForm != null)
                {
                    RemoveUnusedValidationGroups(currentForm.States, currentForm.ValidationGroups);
                    formsAnalyzer.Analyze(currentForm);
                }
                else
                {
                    RemoveUnusedValidationGroups(currentView.States, currentView.ValidationGroups);
                    formsAnalyzer.Analyze(currentView);
                }
            }

            return eventCollection;
        }

        private void AddHandlerAction(XmlNode ruleNode, Event ev, string context, View currentView, Form currentForm,
            XmlNode ruleListenerNode, WSA.Eventing.Action handlerAction,
            Dictionary<Guid, WSA.Eventing.Action> existingHandlerActionCollection, Dictionary<Guid,
            WSA.Eventing.Condition> existingHandlerConditionsCollection, Dictionary<Guid, WSA.Eventing.Handler> existingHandlersCollection)
        {
            // Handlers //
            XmlNodeList handlersNode = ruleListenerNode.SelectNodes("Handlers/Handler");
            Handler handler = null;
            foreach (XmlNode handlerNode in handlersNode)
            {
                handler = CreateHandlerFromRuleXML(handlerNode, existingHandlersCollection, context);
                handlerAction.Handlers.Add(handler);

                // Conditions //
                XmlNodeList ruleConditionsNodes = handlerNode.SelectNodes("Conditions/Condition");
                CreateConditionsFromRuleXML(ruleNode,
                    ev,
                    context,
                    currentForm,
                    handler,
                    ruleConditionsNodes,
                    existingHandlerConditionsCollection,
                    existingHandlersCollection);
                //Conditions //

                // Actions //
                XmlNodeList ruleListenersNode = handlerNode.SelectNodes("Actions/Action");
                CreateActionsFromRuleXML(ruleNode,
                    ev,
                    context,
                    currentView,
                    currentForm,
                    handler,
                    ruleListenersNode,
                    existingHandlerActionCollection,
                    existingHandlerConditionsCollection,
                    existingHandlersCollection);
                // Actions //
            }
        }

        private void AddItemsToCollections(Event @event, Dictionary<Guid, WSA.Eventing.Action> actions, Dictionary<Guid, WSA.Eventing.Handler> handlers, Dictionary<Guid, WSA.Eventing.Condition> conditions)
        {
            foreach (Handler handler in @event.Handlers)
            {
                AddHandlerToCollection(handler, actions, handlers, conditions);
            }
        }

        private void AddHandlerToCollection(Handler handler, Dictionary<Guid, WSA.Eventing.Action> actions, Dictionary<Guid, WSA.Eventing.Handler> handlers, Dictionary<Guid, WSA.Eventing.Condition> conditions)
        {
            handlers.Add(handler.Guid, handler);

            foreach (Condition condition in handler.Conditions)
            {
                conditions.Add(condition.Guid, condition);
            }

            foreach (WSA.Eventing.Action action in handler.Actions)
            {
                actions.Add(action.Guid, action);

                if (action.ActionType == ActionType.Handler)
                {
                    foreach (Handler actionHandler in action.Handlers)
                    {
                        AddHandlerToCollection(actionHandler, actions, handlers, conditions);
                    }
                }
            }
        }

        public XmlDocument GetRuleDefinition(ContextType contextType)
        {
            XmlDocument xd;
            string contextKey = string.Concat(contextType, "_", System.Threading.Thread.CurrentThread.CurrentUICulture.LCID.ToString());
            bool cached;
            string exclusionPath = string.Empty;

            // Disable features by default.
            List<string> featuresToExclude = new List<string>();
            if (!_enabledFeatures.Contains("SSREEnabled"))
                featuresToExclude.Add("SSREEnabled");

            string xp = "(@FeatureToggle={0})";
            foreach (string feature in featuresToExclude)
            {
                if (!string.IsNullOrEmpty(exclusionPath))
                {
                    exclusionPath += " or ";
                }
                exclusionPath += string.Format(xp, XmlHelper.XPathParameterEncode(feature));
                contextKey = string.Concat(contextKey, "_", feature.ToUpperInvariant());
            }

            lock (_ruleDefinitionsCache)
            {
                cached = _ruleDefinitionsCache.TryGetValue(contextKey, out xd);
            }
            if (!cached)
            {
                Assembly executingAssembly = Assembly.GetExecutingAssembly();
                xd = new XmlDocument();
                switch (contextType)
                {
                    case ContextType.VIEW:
                        xd.Load(executingAssembly.GetManifestResourceStream("SourceCode.Forms.Utilities.Resources.ViewRules.xml"));
                        break;
                    case ContextType.CONTROL:
                        xd.Load(executingAssembly.GetManifestResourceStream("SourceCode.Forms.Utilities.Resources.ControlRules.xml"));
                        break;
                    case ContextType.FORM:
                        xd.Load(executingAssembly.GetManifestResourceStream("SourceCode.Forms.Utilities.Resources.FormRules.xml"));
                        break;
                }

                if (!string.IsNullOrEmpty(exclusionPath))//feature exclusions
                {
                    XmlNodeList excludedNodes = xd.SelectNodes(".//*[" + exclusionPath + "]");
                    if (excludedNodes.Count > 0)
                    {
                        foreach (XmlElement xe in excludedNodes)
                        {
                            xe.ParentNode.RemoveChild(xe);
                        }
                    }
                }

                XmlNodeList nl = xd.SelectNodes("//*[@ResourceName]");

                foreach (XmlElement xe in nl)
                {
                    if (xe.Name == "Setting")
                    {
                        #region Find and replace settings resources
                        StringBuilder data = new StringBuilder();
                        string resourceValue = Resources.Rules.ResourceManager.GetString(xe.GetAttribute("ResourceName"));
                        XmlAttribute attrib = xd.CreateAttribute("Name");
                        attrib.Value = resourceValue;
                        xe.Attributes.Append(attrib);
                        xe.RemoveAttribute("ResourceName");
                        #endregion
                    }
                    else
                    {
                        StringBuilder data = new StringBuilder();
                        object lrObj = Resources.Rules.ResourceManager.GetString(xe.GetAttribute("ResourceName"));

                        if (xe.Name == "Part")
                        {
                            data.Append('[');
                            data.Append(lrObj);
                            data.Append(']');
                        }
                        else
                            data.Append(lrObj);

                        xe.InsertBefore(xd.CreateCDataSection(data.ToString()), xe.FirstChild);
                        xe.RemoveAttribute("ResourceName");
                    }
                }

                XmlNodeList el = xd.SelectNodes("//*[@EnumName]");

                foreach (XmlElement xe in el)
                {
                    XmlElement valueE = xd.CreateElement("Value");
                    XmlElement displayE = xd.CreateElement("Display");

                    valueE.InnerText = xe.GetAttribute("EnumName");
                    displayE.InnerText = Resources.Rules.ResourceManager.GetString(xe.GetAttribute("EnumResourceName")).ToString();

                    xe.AppendChild(valueE);
                    xe.AppendChild(displayE);

                    xe.RemoveAttribute("EnumName");
                    xe.RemoveAttribute("ResourceName");
                }


                lock (_ruleDefinitionsCache)
                {
                    _ruleDefinitionsCache[contextKey] = xd;
                }

            }


            return xd;
        }

        public Context BuildContext(Event ev)
        {
            string tmpEventName = string.Empty;
            Context result = new Context();
            _eventBuildContextPerformed.Add(ev.Guid);
            result.Location = Resources.Rules.ErrorRuleLocationNotResolved;
            result.RuleFriendlyName = ev.Properties["RuleName"];

            try
            {
                result.Event = ev;
                result.EventGuid = ev.Guid;
                result.InstanceGuid = ev.InstanceGuid;
                result.SubformGuid = ev.SubFormGuid;

                switch (ev.SourceType)
                {
                    case EventSourceType.Form:
                        #region
                        ResolveForm(result, ev);
                        result.EventName = GetResource("FormEventDisplayName_{0}", ev.Name);

                        if (ev.Name == "WorkflowActioned")
                        {
                            if (ev.SubFormGuid == Guid.Empty)
                            {
                                result.RuleEventName = "FormWorkflowActioned";
                                result.EventFriendlyName = Resources.RuleHelper.RuleNameWorkflowViewSubmitted;
                                result.Location = result.formName;
                            }
                            else
                            {
                                result.RuleEventName = "SubFormWorkflowActioned";
                                GetSubFormAction(result, result.SubformGuid, ev);

                                if (result.SubItemAction != null)
                                {
                                    ResolveExternalForm(result);
                                    result.Location = string.Format(Resources.RuleHelper.SubFormPartDisplayName, result.formName, GetEventFriendlyNameForSubForm(result.SubItemAction));
                                    result.EventFriendlyName = FormatEventName(Resources.RuleHelper.RuleNameSubFormWorkflowActioned, result.formName, null, null);
                                }
                            }
                        }
                        else if (ev.Name == "WorkflowSubmit")
                        {
                            if (ev.SubFormGuid == Guid.Empty)
                            {
                                result.RuleEventName = "FormWorkflowViewEvent";
                                result.EventFriendlyName = Resources.RuleHelper.RuleNameWorkflowViewSubmitted;
                                result.Location = result.formName;
                            }
                            else
                            {
                                result.RuleEventName = "SubFormWorkflowViewEvent";
                                ResolveExternalForm(result, ev);
                                GetSubFormAction(result, result.SubformGuid, ev);
                                result.Location = string.Format(Resources.RuleHelper.SubFormPartDisplayName, result.formName, GetEventFriendlyNameForSubForm(result.SubItemAction));
                                result.EventFriendlyName = FormatEventName(Resources.RuleHelper.RuleNameFormEventNameOther, result.formName, null, result.EventName);
                            }
                        }
                        else if (ev.Name == "ServerPreRender")
                        {
                            if (ev.SubFormGuid == Guid.Empty)
                            {
                                result.RuleEventName = "FormServerPreRenderEvent";
                                result.Location = result.formName;
                                result.EventFriendlyName = Resources.RuleHelper.RuleNameFormServerPreRender;
                            }
                            else
                            {
                                result.RuleEventName = "SubFormServerPreRenderEvent";
                                ResolveExternalForm(result, ev);
                                GetSubFormAction(result, result.SubformGuid, ev);
                                result.Location = string.Format(Resources.RuleHelper.SubFormPartDisplayName, result.formName, GetEventFriendlyNameForSubForm(result.SubItemAction));
                                result.EventFriendlyName = FormatEventName(Resources.RuleHelper.RuleNameSubFormServerPreRender, result.formName, null);
                            }
                        }
                        else
                        {
                            if (ev.SubFormGuid == Guid.Empty)
                            {
                                result.RuleEventName = "FormEvent";
                                result.Location = result.formName;
                                result.EventFriendlyName = FormatEventName(Resources.RuleHelper.RuleNameFormEventNameCurrent, result.formName, null, result.EventName);
                            }
                            else
                            {
                                ResolveExternalForm(result, ev);
                                GetSubFormAction(result, result.SubformGuid, ev);

                                if (result.SubItemAction != null)
                                {
                                    if (ev.Name == "Closed")
                                    {
                                        result.RuleEventName = "OpenedFormCloseEvent";
                                        result.Location = string.Format(Resources.RuleHelper.SubFormPartDisplayName, result.formName, GetEventFriendlyNameForSubForm(result.SubItemAction));
                                        result.EventFriendlyName = FormatEventName(Resources.RuleHelper.RuleNameFormEventNameOther, result.formName, null, result.EventName);
                                    }
                                    else
                                    {
                                        result.RuleEventName = "OpenedFormEvent";
                                        result.Location = string.Format(Resources.RuleHelper.SubFormPartDisplayName, result.formName, GetEventFriendlyNameForSubForm(result.SubItemAction));
                                        result.EventFriendlyName = FormatEventName(Resources.RuleHelper.RuleNameFormEventNameOther, result.formName, null, result.EventName);
                                    }
                                }
                            }
                        }
                        break;
                    #endregion
                    case EventSourceType.View:
                        #region
                        result.EventName = GetResource("ViewEventDisplayName_{0}", ev.Name);
                        ResolveView(result, ev);

                        if (ev.Name == "WorkflowActioned")
                        {
                            if (ev.SubFormGuid.Equals(Guid.Empty))
                            {
                                result.RuleEventName = "WorkflowActioned";
                                if (result.InstanceGuid.Equals(Guid.Empty))
                                {
                                    result.EventFriendlyName = Resources.RuleHelper.RuleNameWorkflowActioned;
                                }
                                else
                                {
                                    ResolveForm(result, ev);
                                    ResolveFormView(result, ev.Validation);
                                    result.RuleEventName = "FormViewWorkflowActioned";
                                    result.EventFriendlyName = string.Format(Resources.RuleHelper.RuleNameViewWorkflowActioned, result.viewMviName);
                                }

                                result.Location = result.viewMviName;
                            }
                            else
                            {
                                GetSubFormAction(result, ev.SubFormGuid, ev);
                                if (result.SubItemAction != null)
                                {
                                    if (result.SubItemAction.FormGuid.Equals(Guid.Empty))
                                    {
                                        result.RuleEventName = "SubViewWorkflowActioned";
                                        ResolveExternalView(result);

                                        result.Location = string.Format(Resources.RuleHelper.SubFormPartDisplayName, result.viewMviName, GetEventFriendlyNameForSubForm(result.SubItemAction));
                                        result.EventFriendlyName = string.Format(Resources.RuleHelper.RuleNameSubViewWorkflowActioned, result.viewName);
                                    }
                                    else
                                    {
                                        result.RuleEventName = "SubFormViewWorkflowActioned";
                                        ResolveExternalForm(result);

                                        if (result.Form != null)
                                        {
                                            ResolveFormView(result, ev.Validation);

                                            result.Location = string.Format(Resources.RuleHelper.SubFormViewPartDisplayName, result.formName, result.viewMviName, GetEventFriendlyNameForSubForm(result.SubItemAction));
                                            result.EventFriendlyName = string.Format(Resources.RuleHelper.RuleNameSubFormViewWorkflowActioned, result.formName, result.viewMviName);
                                        }
                                    }
                                }
                            }
                        }
                        else if (ev.Name == "WorkflowSubmit")
                        {
                            if (ev.SubFormGuid.Equals(Guid.Empty))
                            {
                                result.RuleEventName = "ViewWorkflowViewEvent";
                                if (result.InstanceGuid.Equals(Guid.Empty))
                                {
                                    result.EventFriendlyName = Resources.RuleHelper.RuleNameWorkflowViewSubmitted;
                                }
                                else
                                {
                                    ResolveForm(result, ev);
                                    ResolveFormView(result, ev.Validation);
                                    result.EventFriendlyName = string.Format(Resources.RuleHelper.RuleNameViewWorkflowViewSubmitted, result.viewMviName);
                                }

                                result.Location = result.viewMviName;

                            }
                            else
                            {
                                GetSubFormAction(result, ev.SubFormGuid, ev);
                                if (result.SubItemAction != null)
                                {
                                    if (result.SubItemAction.FormGuid.Equals(Guid.Empty))
                                    {
                                        result.RuleEventName = "SubViewWorkflowViewEvent";
                                        ResolveExternalView(result);
                                        result.Location = string.Format(Resources.RuleHelper.SubFormPartDisplayName, result.viewMviName, GetEventFriendlyNameForSubForm(result.SubItemAction));
                                        result.EventFriendlyName = string.Format(Resources.RuleHelper.RuleNameSubViewWorkflowViewEvent, result.viewName);
                                    }
                                    else
                                    {
                                        result.RuleEventName = "SubFormViewWorkflowViewEvent";
                                        ResolveExternalForm(result);
                                        ResolveFormView(result, ev.Validation);
                                        result.Location = string.Format(Resources.RuleHelper.SubFormViewPartDisplayName, result.formName, result.viewMviName, GetEventFriendlyNameForSubForm(result.SubItemAction));
                                        result.EventFriendlyName = string.Format(Resources.RuleHelper.RuleNameSubFormViewWorkflowViewEvent, result.formName, result.viewMviName);
                                    }
                                }
                            }
                        }
                        else if (ev.Name == "ServerPreRender")
                        {
                            if (ev.SubFormGuid != Guid.Empty)
                            {
                                GetSubFormAction(result, result.SubformGuid, ev);

                                if (result.SubItemAction.FormGuid != Guid.Empty)
                                {
                                    ResolveExternalForm(result);
                                    result.RuleEventName = "SubFormViewServerPreRenderEvent";
                                    ResolveFormView(result, ev.Validation);
                                    result.EventFriendlyName = FormatEventName(Resources.RuleHelper.RuleNameSubFormViewServerPreRender, result.formName, result.viewMviName);
                                    result.Location = string.Format(Resources.RuleHelper.SubFormViewPartDisplayName, result.formName, result.viewMviName, GetEventFriendlyNameForSubForm(result.SubItemAction));
                                }
                                else
                                {
                                    ResolveExternalView(result);
                                    result.RuleEventName = "SubViewServerPreRenderEvent";
                                    result.EventFriendlyName = FormatEventName(Resources.RuleHelper.RuleNameSubViewServerPreRender, null, result.viewMviName);
                                    result.Location = string.Format(Resources.RuleHelper.SubFormPartDisplayName, result.viewMviName, GetEventFriendlyNameForSubForm(result.SubItemAction));
                                }
                            }
                            else
                            {
                                result.RuleEventName = "ViewServerPreRenderEvent";
                                if (result.InstanceGuid.Equals(Guid.Empty))
                                {
                                    result.EventFriendlyName = Resources.RuleHelper.RuleNameViewServerPreRender;
                                }
                                else
                                {
                                    ResolveForm(result, ev);
                                    ResolveFormView(result, ev.Validation);
                                    result.EventFriendlyName = string.Format(Resources.RuleHelper.RuleNameFormViewServerPreRender, result.viewMviName);
                                }
                                result.Location = result.viewMviName;
                            }
                        }
                        else
                        {
                            // If it is a Form/View SubForm
                            if (ev.SubFormGuid != Guid.Empty)
                            {
                                GetSubFormAction(result, result.SubformGuid, ev);

                                if (result.SubItemAction.FormGuid != Guid.Empty)
                                {
                                    ResolveExternalForm(result);
                                    result.RuleEventName = "OpenedFormViewEvent";
                                    ResolveFormView(result, ev.Validation);
                                    // #SubFormViewPartDisplayName
                                    result.Location = string.Format(Resources.RuleHelper.SubFormViewPartDisplayName, result.formName, result.viewMviName, GetEventFriendlyNameForSubForm(result.SubItemAction));
                                    result.EventFriendlyName = FormatEventName(Resources.RuleHelper.RuleNameFormViewEventOther, result.formName, result.viewMviName, result.EventName);
                                }
                                else
                                {
                                    ResolveExternalView(result);

                                    if (ev.Name == "Closed")
                                    {
                                        result.RuleEventName = "OpenedViewCloseEvent";
                                        result.EventFriendlyName = FormatEventName(Resources.RuleHelper.RuleNameViewEventNameOther, null, result.viewMviName, result.EventName);
                                        result.Location = string.Format(Resources.RuleHelper.SubFormPartDisplayName, result.viewMviName, GetEventFriendlyNameForSubForm(result.SubItemAction));
                                    }
                                    else
                                    {
                                        result.RuleEventName = "SubViewEvent";
                                        result.EventFriendlyName = FormatEventName(Resources.RuleHelper.RuleNameViewEventNameOther, null, result.viewMviName, result.EventName);
                                        result.Location = string.Format(Resources.RuleHelper.SubFormPartDisplayName, result.viewMviName, GetEventFriendlyNameForSubForm(result.SubItemAction));
                                    }
                                }
                            }
                            else // Not a subform
                            {
                                result.RuleEventName = "ViewEvent";

                                // Current View
                                if (ev.InstanceGuid == Guid.Empty)
                                {
                                    ResolveView(result, ev);
                                    result.Location = result.viewMviName;
                                    result.EventFriendlyName = FormatEventName(Resources.RuleHelper.RuleNameViewEventNameCurrent, null, result.viewName, result.EventName);
                                }
                                else // View on Form
                                {
                                    ResolveForm(result, ev);
                                    ResolveFormView(result, ev.Validation);
                                    result.Location = result.viewMviName;
                                    result.EventFriendlyName = FormatEventName(Resources.RuleHelper.RuleNameFormViewEventCurrent, result.formName, result.Location, result.EventName);
                                }
                            }
                        }
                        break;
                    #endregion
                    case EventSourceType.Control:
                        #region
                        result.EventName = GetResource("ControlEventDisplayName_{0}", ev.Name);
                        result.controlGuid = ev.SourceGuid;
                        // Not a subform
                        if (ev.SubFormGuid == Guid.Empty)
                        {
                            // View Control Event
                            if (ev.InstanceGuid == Guid.Empty && ev.Form == null)
                            {
                                ResolveView(result, ev);
                                result.RuleEventName = "ViewControlEvent";
                                ResolveControl(result, result.View != null ? result.View.Controls : new ControlCollection(null), ev);
                                result.Location = result.viewMviName;
                                result.EventFriendlyName = FormatEventName(Resources.RuleHelper.RuleNameControlEventNameCurrent, null, result.viewName, result.controlName, result.EventName);
                            }
                            else
                            {
                                ResolveForm(result, ev);
                                if (result.Event.InstanceGuid.Equals(Guid.Empty))
                                {
                                    // Form control event
                                    ResolveControl(result, result.Form != null ? result.Form.Controls : new ControlCollection(null), ev);
                                    result.RuleEventName = "FormControlEvent";
                                    result.Location = result.formName;
                                    result.EventFriendlyName = FormatEventName(Resources.RuleHelper.RuleNameControlEventNameCurrent, result.formName, null, result.controlName, result.EventName);
                                }
                                else // View on Form control event
                                {
                                    ResolveFormViewControlName(ev, ev.SourceGuid, result, ev.SubFormGuid);
                                    result.RuleEventName = "ViewControlEvent";
                                    result.Location = result.viewMviName;
                                    result.EventFriendlyName = FormatEventName(Resources.RuleHelper.RuleNameFormViewControlEventCurrent, result.formName, result.viewMviName, result.controlName, result.EventName);
                                }
                            }
                        }
                        else // Is a subform
                        {
                            GetSubFormAction(result, result.SubformGuid, ev);

                            if (result.SubItemAction != null)
                            {
                                if (result.SubItemAction.FormGuid != Guid.Empty)
                                {
                                    ResolveExternalForm(result);
                                    if (result.Event.InstanceGuid.Equals(Guid.Empty) || (result.Event.InstanceGuid == result.SubItemAction.InstanceGuid) && result.Event.Properties["ViewID"] == null) // SubForm Control Event, InstanceID is that of the opener, not the target
                                    {
                                        ResolveControl(result, result.Form != null ? result.Form.Controls : new ControlCollection(null), ev);
                                        result.RuleEventName = "OpenedFormControlEvent";
                                        result.Location = string.Format(Resources.RuleHelper.SubFormPartDisplayName, result.formName, GetEventFriendlyNameForSubForm(result.SubItemAction));
                                        result.EventFriendlyName = FormatEventName(Resources.RuleHelper.RuleNameFormControlEventOther, result.formName, null, result.controlName, result.EventName);
                                    }
                                    else// SubForm , View control event
                                    {
                                        ResolveFormView(result);
                                        ResolveControl(result, result.View != null ? result.View.Controls : new ControlCollection(null), ev);
                                        result.RuleEventName = "OpenedFormViewControlEvent";
                                        result.Location = string.Format(Resources.RuleHelper.SubFormViewPartDisplayName, result.formName, result.viewMviName, GetEventFriendlyNameForSubForm(result.SubItemAction));
                                        result.EventFriendlyName = FormatEventName(Resources.RuleHelper.RuleNameFormViewControlEventOther, result.formName, result.viewMviName, result.controlName, result.EventName);
                                    }
                                }// Subform View Control Event
                                else if (result.SubItemAction.ViewGuid != Guid.Empty)
                                {
                                    ResolveExternalView(result);
                                    result.RuleEventName = "SubViewControlEvent";
                                    ResolveControl(result, result.View != null ? result.View.Controls : new ControlCollection(null), ev);
                                    result.Location = string.Format(Resources.RuleHelper.SubFormPartDisplayName, result.viewMviName, GetEventFriendlyNameForSubForm(result.SubItemAction));
                                    result.EventFriendlyName = FormatEventName(Resources.RuleHelper.RuleNameControlEventNameOther, null, result.viewMviName, result.controlName, result.EventName);
                                }
                            }
                        }
                        break;
                    #endregion
                    case EventSourceType.ViewParameter:
                    case EventSourceType.FormParameter:
                        #region
                        result.EventName = GetResource("ParameterEventDisplayName_{0}", ev.Name);
                        if (ev.SubFormGuid == Guid.Empty)
                        {
                            ResolveForm(result, ev);
                            if (result.InstanceGuid == Guid.Empty && result.formGuid == Guid.Empty)
                            {
                                ResolveView(result, ev);
                                result.RuleEventName = "ViewParameterEvent";
                                ResolveViewParameter(result, ev.SourceGuid, ev.Validation);
                                result.Location = result.viewMviName;
                                result.EventFriendlyName = FormatEventName(Resources.RuleHelper.RuleNameViewParameterCurrent, null, null, result.parameterName, result.EventName);
                            }
                            else if (result.formGuid != null)
                            {
                                if (ev.SourceType == EventSourceType.FormParameter)
                                {
                                    ResolveFormParameter(result, ev.SourceGuid, ev.Validation);
                                    result.RuleEventName = "FormParameterEvent";
                                    result.Location = result.formName;
                                    result.EventFriendlyName = FormatEventName(Resources.RuleHelper.RuleNameFormParameterCurrent, result.formName, null, result.parameterName, result.EventName);
                                }
                                else
                                {
                                    result.RuleEventName = "FormViewParameterEvent";
                                    ResolveFormView(result, ev.Validation);
                                    ResolveViewParameter(result, ev.SourceGuid, ev.Validation);
                                    result.Location = result.viewMviName;
                                    result.EventFriendlyName = FormatEventName(Resources.RuleHelper.RuleNameFormViewParameterCurrent, result.formName, result.viewMviName, result.parameterName, result.EventName);
                                }
                            }
                        }
                        else
                        {
                            GetSubFormAction(result, result.SubformGuid, ev);

                            if (result.SubItemAction != null)
                            {
                                if (result.SubItemAction.FormGuid != Guid.Empty)
                                {
                                    ResolveExternalForm(result);

                                    if (result.Event.InstanceGuid.Equals(Guid.Empty) || (result.SubItemAction.InstanceGuid.Equals(result.Event.InstanceGuid) && ev.SourceType == EventSourceType.FormParameter))
                                    {
                                        ResolveFormParameter(result, ev.SourceGuid, ev.Validation);
                                        result.RuleEventName = "SubFormParameterEvent";
                                        result.Location = string.Format(Resources.RuleHelper.SubFormPartDisplayName, result.formName, GetEventFriendlyNameForSubForm(result.SubItemAction));
                                        result.EventFriendlyName = FormatEventName(Resources.RuleHelper.RuleNameFormParameterOther, result.formName, null, result.parameterName, result.EventName);

                                    }
                                    else
                                    {
                                        ResolveFormView(result, ev.Validation);
                                        result.RuleEventName = "SubFormViewParameterEvent";
                                        ResolveViewParameter(result, ev.SourceGuid, ev.Validation);
                                        result.Location = string.Format(Resources.RuleHelper.SubFormPartDisplayName, result.formName, GetEventFriendlyNameForSubForm(result.SubItemAction));
                                        result.EventFriendlyName = FormatEventName(Resources.RuleHelper.RuleNameFormViewParameterOther, result.formName, result.viewMviName, result.parameterName, result.EventName);
                                    }
                                }
                                else if (result.SubItemAction.ViewGuid != Guid.Empty)
                                {
                                    ResolveExternalView(result);
                                    result.RuleEventName = "SubViewParameterEvent";
                                    ResolveViewParameter(result, ev.SourceGuid, ev.Validation);
                                    result.Location = string.Format(Resources.RuleHelper.SubFormPartDisplayName, result.viewMviName, GetEventFriendlyNameForSubForm(result.SubItemAction));
                                    result.EventFriendlyName = FormatEventName(Resources.RuleHelper.RuleNameViewParameterOther, null, result.viewMviName, result.parameterName, result.EventName);
                                }
                            }
                        }
                        break;
                    #endregion
                    case EventSourceType.Rule:
                        #region
                        result.EventName = ev.Name;
                        result.EventFriendlyName = ev.Name;
                        result.RuleEventName = "Rule";

                        if (ev.SubFormGuid != Guid.Empty) // Is on subview or subform
                        {
                            GetSubFormAction(result, result.SubformGuid, ev);
                            if (result.SubItemAction != null)
                            {
                                if (result.SubItemAction.FormGuid != Guid.Empty)
                                {
                                    ResolveExternalForm(result);
                                    if (ev.SourceGuid == result.Form.Guid)
                                    {
                                        result.Location = string.Format(Resources.RuleHelper.SubFormPartDisplayName, result.formName, GetEventFriendlyNameForSubForm(result.SubItemAction));
                                    }
                                    else
                                    {
                                        ResolveFormView(result, ev.Validation);
                                        //see #SubFormViewPartDisplayName
                                        result.Location = string.Format(Resources.RuleHelper.SubFormViewPartDisplayName, result.formName, result.viewMviName, GetEventFriendlyNameForSubForm(result.SubItemAction));
                                    }
                                }
                                else
                                {
                                    ResolveExternalView(result);
                                    if (ev.SourceGuid == result.viewGuid)
                                    {
                                        result.Location = string.Format(Resources.RuleHelper.SubFormPartDisplayName, result.viewMviName, GetEventFriendlyNameForSubForm(result.SubItemAction));
                                    }
                                }
                            }

                        }
                        else // Is on current View or Form
                        {
                            if (result.Event.Form != null)
                            {
                                ResolveForm(result, ev);
                                if (result.Event.InstanceGuid.Equals(Guid.Empty))
                                {
                                    result.Location = result.formName;
                                }
                                else
                                {
                                    ResolveFormView(result, ev.Validation);
                                    result.Location = result.viewMviName;
                                }
                            }
                            else
                            {
                                ResolveView(result, ev);
                                result.Location = result.viewMviName;
                            }
                        }

                        break;
                    #endregion
                    default:
                        break;
                }

                result.RuleFriendlyName = ev.Properties["RuleName"];

                if (!string.IsNullOrEmpty(result.RuleEventName))
                {
                    ResolveEvent(result);
                }

                if (!string.IsNullOrEmpty(ev.Properties["IsCustomName"]))
                {
                    if (result.formGuid != Guid.Empty && result.viewGuid != Guid.Empty)
                    {
                        result.RuleFriendlyName = string.Format(Resources.RuleHelper.RuleFriendlyShortName, result.RuleFriendlyName, result.Location);
                    }

                    if (ev.IsReference)
                    {
                        if (!ev.SubFormGuid.Equals(Guid.Empty))
                        {
                            GetSubFormAction(result, ev.SubFormGuid, ev);
                        }

                        if (result.SubItemAction != null)
                        {
                            if (result.SubItemAction.FormGuid != Guid.Empty)
                            {
                                ResolveExternalForm(result);
                                if (!string.IsNullOrEmpty(result.formName))
                                {
                                    result.RuleFriendlyName = string.Format(Resources.RuleHelper.RuleFriendlyName, result.RuleFriendlyName, result.formName, GetEventFriendlyNameForSubForm(result.SubItemAction));
                                }
                                else
                                {
                                    result.RuleFriendlyName = string.Format(Resources.Rules.ErrorEventFriendlyNameNotResolved, result.RuleFriendlyName);
                                }
                            }
                            else
                            {
                                ResolveExternalView(result);
                                if (!string.IsNullOrEmpty(result.viewName))
                                {
                                    result.RuleFriendlyName = string.Format(Resources.RuleHelper.RuleFriendlyName, result.RuleFriendlyName, result.viewMviName, GetEventFriendlyNameForSubForm(result.SubItemAction));
                                }
                                else
                                {
                                    result.RuleFriendlyName = string.Format(Resources.Rules.ErrorEventFriendlyNameNotResolved, result.RuleFriendlyName);
                                }
                            }
                        }
                    }

                    result.EventFriendlyName = result.RuleFriendlyName;
                }

                if (string.IsNullOrEmpty(ev.Properties["IsCustomName"]))
                {
                    BuildEventFriendlyName(result);
                }
            }

            catch (Exception ex)
            {
                Log.Message(Log.Level.Error, "RuleHelper", "BuildContext(Event): Event could not be resolved: {0} - {1}: {2}", result.RuleEventName, ev.Guid, ex.ToString().Replace(Environment.NewLine, "\\"));
            }

            ev.Properties["RuleFriendlyName"] = result.RuleFriendlyName;
            ev.Properties["Location"] = result.Location;

            return result;
        }

        public Context BuildContext(Handler handler)
        {
            Context result = new Context();
            result.handlerType = handler.HandlerType;
            result.handler = handler;
            Event ev = GetEvent(handler);
            result.Event = ev;
            result.EventGuid = ev.Guid;

            foreach (WSA.Property property in handler.Properties)
            {
                switch (property.Name)
                {
                    case "Location":
                        result.Location = property.Value;
                        break;
                }
            }

            switch (result.handlerType)
            {
                case HandlerType.Else:
                    result.RuleHandlerName = "ElseLogicalHandler";
                    break;
                case HandlerType.Error:
                    result.RuleHandlerName = "ErrorHandler";
                    break;
                case HandlerType.ForEach:
                    HandlerFunction handlerFunction = result.handler.Function;
                    result.SubformGuid = handlerFunction.SubFormGuid;
                    result.InstanceGuid = handlerFunction.InstanceGuid;
                    result.itemState = (ActionItemState)Enum.Parse(typeof(ActionItemState), GetPropertyExpressionBySourceType(handlerFunction.Parameters, PropertyExpressionSourceType.ItemState).SourceID);

                    if (handlerFunction.Name == "ViewItemsCollection")
                    {
                        if (result.SubformGuid == Guid.Empty)
                        {
                            if (result.InstanceGuid != Guid.Empty)
                            {
                                ResolveForm(result, result.Event);
                                ResolveFormView(result, handlerFunction);
                            }
                            else
                            {
                                ResolveView(result, result.Event);
                            }
                            result.RuleHandlerName = "ForEachListViewRowHandler";

                            result.HandlerFriendlyName = string.Format(Resources.RuleHelper.ForEachListViewRowHandler, result.viewName, result.itemState.ToString());
                        }
                        else
                        {
                            GetSubFormAction(result, handlerFunction.SubFormGuid, ev);
                            result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);

                            if (result.SubItemAction.FormGuid == Guid.Empty)
                            {
                                result.RuleHandlerName = "SubViewForEachListViewRowHandler";
                                ResolveExternalView(result);
                                result.HandlerFriendlyName = string.Format(Resources.RuleHelper.SubViewForEachListViewRowHandler, result.viewName, result.itemState.ToString());
                            }
                            else
                            {
                                result.RuleHandlerName = "SubFormViewForEachListViewRowHandler";
                                ResolveExternalForm(result);
                                result.InstanceGuid = handlerFunction.InstanceGuid;
                                ResolveFormView(result, handlerFunction);
                                result.HandlerFriendlyName = string.Format(Resources.RuleHelper.SubFormViewForEachListViewRowHandler, result.formName, result.viewName, result.itemState.ToString());
                            }
                        }
                    }
                    else
                    {
                        //handlerFunction.Name == "ControlItemsCollection"
                        PropertyExpression control = GetPropertyExpressionBySourceType(handlerFunction.Parameters, PropertyExpressionSourceType.Control);

                        if (control != null && control.SourceID != null)
                        {
                            result.controlGuid = new Guid(control.SourceID);
                        }

                        if (result.SubformGuid == Guid.Empty)
                        {
                            if (handlerFunction.InstanceGuid != Guid.Empty)
                            {
                                ResolveForm(result, result.Event);
                                result.InstanceGuid = handlerFunction.InstanceGuid;
                                ResolveFormView(result, handlerFunction);
                                result.RuleHandlerName = "ForEachListControlOnViewItemHandler";
                            }
                            else
                            {
                                ResolveView(result, result.Event);
                                result.RuleHandlerName = "ForEachListControlItemHandler";
                            }
                        }
                        else
                        {
                            GetSubFormAction(result, handlerFunction.SubFormGuid, ev);
                            result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);

                            if (result.SubItemAction.FormGuid == Guid.Empty)
                            {
                                result.RuleHandlerName = "SubViewForEachListControlItemHandler";
                                ResolveExternalView(result);
                            }
                            else
                            {
                                result.RuleHandlerName = "SubFormViewForEachListControlItemHandler";
                                ResolveExternalForm(result);
                                ResolveFormView(result, handlerFunction);
                            }
                        }

                        ResolveControl(result, result.View != null ? result.View.Controls : new ControlCollection(null), control);
                    }
                    break;
                default:
                    result.RuleHandlerName = "IfLogicalHandler";
                    break;
            }

            ResolveHandler(result);
            BuildHandlerFriendlyName(result);

            return result;
        }

        public Context BuildContext(LogicalExpression lp, Condition condition)
        {
            Context result = new Context();

            try
            {
                result.Condition = condition;
                result.LogicalExpression = lp;
                result.InstanceGuid = condition.InstanceGuid;
                result.RuleConditionName = condition.Properties["Name"];
                result.SubformGuid = condition.SubFormGuid;

                result.Event = GetEvent(condition);
                result.EventGuid = result.Event.Guid;

                XmlDocument xmlDoc = XmlHelper.CreateXmlDocument(lp.ToXml().ToString());
                string parameterName = string.Empty;
                XmlAttribute sourceAttribute;
                PropertyExpression controlPropEx;
                PropertyExpression paramPropEx;

                switch (result.RuleConditionName)
                {
                    case "SimpleEqualControlCondition":
                    case "SubViewSimpleEqualControlCondition":
                    case "SubFormSimpleEqualControlCondition":
                        sourceAttribute = xmlDoc.SelectSingleNode("Equals/Item[@SourceType = 'Control']").Attributes["SourceID"];
                        result.controlGuid = sourceAttribute != null ? new Guid(sourceAttribute.Value) : Guid.Empty;
                        result.OperatorValue = xmlDoc.SelectSingleNode("Equals/Item[@SourceType = 'Value']").InnerText;
                        result.Operator = "Equals";

                        controlPropEx = GetExpressionBySourceTypeFromOperands(result, PropertyExpressionSourceType.Control);
                        if (result.InstanceGuid != controlPropEx.SourceInstanceGuid && controlPropEx.SourceInstanceGuid != Guid.Empty)
                        {
                            result.InstanceGuid = controlPropEx.SourceInstanceGuid;
                        }
                        if (result.SubformGuid != controlPropEx.SourceSubFormGuid && controlPropEx.SourceSubFormGuid != Guid.Empty)
                        {
                            result.SubformGuid = controlPropEx.SourceSubFormGuid;
                        }

                        ResolveFormViewControlName(result.Event, result.controlGuid, result, result.SubformGuid);

                        if (result.SubformGuid != Guid.Empty)
                        {
                            GetSubFormAction(result, result.SubformGuid, result.Event);
                            if (result.SubItemAction != null)
                            {
                                if (result.SubItemAction.FormGuid == Guid.Empty)
                                {
                                    result.RuleConditionName = "SubViewSimpleEqualControlCondition";
                                    result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionSubFormNameEquals, result.viewName, result.controlName, result.OperatorValue, condition);
                                    result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                                }
                                else
                                {
                                    result.RuleConditionName = "SubFormSimpleEqualControlCondition";
                                    result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionSubFormFormNameEquals, result.formName, result.viewName, result.controlName, result.OperatorValue, condition);
                                    result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                                }
                            }
                        }
                        else
                        {
                            result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionNameEquals, result.viewName, result.controlName, result.OperatorValue, condition);
                        }
                        break;
                    case "SimpleEqualFormControlCondition":
                    case "SubFormSimpleEqualFormControlCondition":
                        sourceAttribute = xmlDoc.SelectSingleNode("Equals/Item[@SourceType = 'Control']").Attributes["SourceID"];
                        result.controlGuid = sourceAttribute != null ? new Guid(sourceAttribute.Value) : Guid.Empty;
                        result.OperatorValue = xmlDoc.SelectSingleNode("Equals/Item[@SourceType = 'Value']").InnerText;
                        result.Operator = "Equals";

                        controlPropEx = GetExpressionBySourceTypeFromOperands(result, PropertyExpressionSourceType.Control);
                        if (result.SubformGuid != controlPropEx.SourceSubFormGuid && controlPropEx.SourceSubFormGuid != Guid.Empty)
                        {
                            result.SubformGuid = controlPropEx.SourceSubFormGuid;
                        }

                        if (result.SubformGuid != Guid.Empty)
                        {
                            result.RuleConditionName = "SubFormSimpleEqualFormControlCondition";

                            GetSubFormAction(result, result.SubformGuid, result.Event);
                            ResolveExternalForm(result);
                            ResolveControl(result, result.Form != null ? result.Form.Controls : new ControlCollection(null), controlPropEx);
                            result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionFormSubFormNameEquals, result.formName, result.controlName, result.OperatorValue, condition);
                            result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                        }
                        else
                        {
                            ResolveForm(result, result.Event);
                            ResolveControl(result, result.Form != null ? result.Form.Controls : new ControlCollection(null), controlPropEx);
                            result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionFormNameEquals, result.formName, result.controlName, result.OperatorValue, condition);
                        }
                        break;
                    case "SimpleNotEqualControlCondition":
                    case "SubViewSimpleNotEqualControlCondition":
                    case "SubFormSimpleNotEqualControlCondition":
                        sourceAttribute = xmlDoc.SelectSingleNode("NotEquals/Item[@SourceType = 'Control']").Attributes["SourceID"];
                        result.controlGuid = sourceAttribute != null ? new Guid(sourceAttribute.Value) : Guid.Empty;
                        result.OperatorValue = xmlDoc.SelectSingleNode("NotEquals/Item[@SourceType = 'Value']").InnerText;
                        result.Operator = "NotEquals";

                        controlPropEx = GetExpressionBySourceTypeFromOperands(result, PropertyExpressionSourceType.Control);
                        if (result.InstanceGuid != controlPropEx.SourceInstanceGuid && controlPropEx.SourceInstanceGuid != Guid.Empty)
                        {
                            result.InstanceGuid = controlPropEx.SourceInstanceGuid;
                        }
                        if (result.SubformGuid != controlPropEx.SourceSubFormGuid && controlPropEx.SourceSubFormGuid != Guid.Empty)
                        {
                            result.SubformGuid = controlPropEx.SourceSubFormGuid;
                        }

                        ResolveFormViewControlName(result.Event, result.controlGuid, result, result.SubformGuid);

                        if (result.SubformGuid != Guid.Empty)
                        {
                            GetSubFormAction(result, result.SubformGuid, result.Event);
                            if (result.SubItemAction != null)
                            {
                                if (result.SubItemAction.FormGuid == Guid.Empty)
                                {
                                    result.RuleConditionName = "SubViewSimpleNotEqualControlCondition";
                                    result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionSubFormNameNotEquals, result.viewName, result.controlName, result.OperatorValue, condition);
                                    result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                                }
                                else
                                {
                                    result.RuleConditionName = "SubFormSimpleNotEqualControlCondition";
                                    result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionSubFormFormNameNotEquals, result.formName, result.viewName, result.controlName, result.OperatorValue, condition);
                                    result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                                }
                            }
                        }
                        else
                        {
                            result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionNameNotEquals, result.viewName, result.controlName, result.OperatorValue, condition);
                        }
                        break;
                    case "SimpleNotEqualFormControlCondition":
                    case "SubFormSimpleNotEqualFormControlCondition":
                        sourceAttribute = xmlDoc.SelectSingleNode("NotEquals/Item[@SourceType = 'Control']").Attributes["SourceID"];
                        result.controlGuid = sourceAttribute != null ? new Guid(sourceAttribute.Value) : Guid.Empty;
                        result.OperatorValue = xmlDoc.SelectSingleNode("NotEquals/Item[@SourceType = 'Value']").InnerText;
                        result.Operator = "NotEquals";

                        controlPropEx = GetExpressionBySourceTypeFromOperands(result, PropertyExpressionSourceType.Control);
                        if (result.SubformGuid != controlPropEx.SourceSubFormGuid && controlPropEx.SourceSubFormGuid != Guid.Empty)
                        {
                            result.SubformGuid = controlPropEx.SourceSubFormGuid;
                        }

                        if (result.SubformGuid != Guid.Empty)
                        {
                            result.RuleConditionName = "SubFormSimpleNotEqualFormControlCondition";
                            GetSubFormAction(result, result.SubformGuid, result.Event);
                            ResolveExternalForm(result);
                            ResolveControl(result, result.Form != null ? result.Form.Controls : new ControlCollection(null), controlPropEx);

                            result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionFormSubFormNameNotEquals, result.formName, result.controlName, result.OperatorValue, condition);
                            result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                        }
                        else
                        {
                            ResolveForm(result, result.Event);
                            ResolveControl(result, result.Form != null ? result.Form.Controls : new ControlCollection(null), controlPropEx);
                            result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionFormNameNotEquals, result.formName, result.controlName, result.OperatorValue, condition);
                        }
                        break;
                    case "SimpleBlankControlCondition":
                    case "SubViewSimpleBlankControlCondition":
                    case "SubFormSimpleBlankControlCondition":
                        sourceAttribute = xmlDoc.SelectSingleNode("IsBlank/Item[@SourceType = 'Control']").Attributes["SourceID"];
                        result.controlGuid = sourceAttribute != null ? new Guid(sourceAttribute.Value) : Guid.Empty;
                        result.Operator = "IsBlank";

                        controlPropEx = GetExpressionBySourceTypeFromOperands(result, PropertyExpressionSourceType.Control);
                        if (result.InstanceGuid != controlPropEx.SourceInstanceGuid && controlPropEx.SourceInstanceGuid != Guid.Empty)
                        {
                            result.InstanceGuid = controlPropEx.SourceInstanceGuid;
                        }
                        if (result.SubformGuid != controlPropEx.SourceSubFormGuid && controlPropEx.SourceSubFormGuid != Guid.Empty)
                        {
                            result.SubformGuid = controlPropEx.SourceSubFormGuid;
                        }

                        ResolveFormViewControlName(result.Event, result.controlGuid, result, result.SubformGuid);

                        if (result.SubformGuid != Guid.Empty)
                        {
                            GetSubFormAction(result, result.SubformGuid, result.Event);
                            if (result.SubItemAction.FormGuid == Guid.Empty)
                            {
                                result.RuleConditionName = "SubViewSimpleBlankControlCondition";
                                result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionSubFormNameBlank, result.viewName, result.controlName, null, condition);
                                result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                            }
                            else
                            {
                                result.RuleConditionName = "SubFormSimpleBlankControlCondition";
                                result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionSubFormFormNameBlank, result.formName, result.viewName, result.controlName, null, condition);
                                result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                            }
                        }
                        else
                        {
                            result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionNameBlank, result.viewName, result.controlName, null, condition);
                        }
                        break;

                    case "SimpleBlankFormControlCondition":
                    case "SubFormSimpleBlankFormControlCondition":
                        sourceAttribute = xmlDoc.SelectSingleNode("IsBlank/Item[@SourceType = 'Control']").Attributes["SourceID"];
                        result.controlGuid = sourceAttribute != null ? new Guid(sourceAttribute.Value) : Guid.Empty;
                        result.Operator = "IsBlank";

                        controlPropEx = GetExpressionBySourceTypeFromOperands(result, PropertyExpressionSourceType.Control);
                        if (result.SubformGuid != controlPropEx.SourceSubFormGuid && controlPropEx.SourceSubFormGuid != Guid.Empty)
                        {
                            result.SubformGuid = controlPropEx.SourceSubFormGuid;
                        }

                        if (result.SubformGuid != Guid.Empty)
                        {
                            result.RuleConditionName = "SubFormSimpleBlankFormControlCondition";
                            GetSubFormAction(result, result.SubformGuid, result.Event);
                            ResolveExternalForm(result);
                            ResolveControl(result, result.Form != null ? result.Form.Controls : new ControlCollection(null), controlPropEx);
                            result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionFormSubFormNameBlank, result.formName, result.controlName, null, condition);
                            result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                        }
                        else
                        {
                            ResolveForm(result, result.Event);
                            ResolveControl(result, result.Form != null ? result.Form.Controls : new ControlCollection(null), controlPropEx);
                            result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionFormNameBlank, result.formName, result.controlName, null, condition);
                        }
                        break;
                    case "SimpleNotBlankControlCondition":
                    case "SubViewSimpleNotBlankControlCondition":
                    case "SubFormSimpleNotBlankControlCondition":
                        sourceAttribute = xmlDoc.SelectSingleNode("IsNotBlank/Item[@SourceType = 'Control']").Attributes["SourceID"];
                        result.controlGuid = sourceAttribute != null ? new Guid(sourceAttribute.Value) : Guid.Empty;
                        result.Operator = "IsNotBlank";
                        controlPropEx = GetExpressionBySourceTypeFromOperands(result, PropertyExpressionSourceType.Control);
                        if (result.InstanceGuid != controlPropEx.SourceInstanceGuid && controlPropEx.SourceInstanceGuid != Guid.Empty)
                        {
                            result.InstanceGuid = controlPropEx.SourceInstanceGuid;
                        }
                        if (result.SubformGuid != controlPropEx.SourceSubFormGuid && controlPropEx.SourceSubFormGuid != Guid.Empty)
                        {
                            result.SubformGuid = controlPropEx.SourceSubFormGuid;
                        }

                        ResolveFormViewControlName(result.Event, result.controlGuid, result, result.SubformGuid);

                        if (result.SubformGuid != Guid.Empty)
                        {
                            GetSubFormAction(result, result.SubformGuid, result.Event);
                            if (result.SubItemAction.FormGuid == Guid.Empty)
                            {
                                result.RuleConditionName = "SubViewSimpleNotBlankControlCondition";
                                result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionSubFormNameNotBlank, result.viewName, result.controlName, null, condition);
                                result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                            }
                            else
                            {
                                result.RuleConditionName = "SubFormSimpleNotBlankControlCondition";
                                result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionSubFormFormNameNotBlank, result.formName, result.viewName, result.controlName, null, condition);
                                result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                            }
                        }
                        else
                        {
                            result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionNameNotBlank, result.viewName, result.controlName, null, condition);
                        }

                        break;
                    case "SimpleNotBlankFormControlCondition":
                    case "SubFormSimpleNotBlankFormControlCondition":
                        sourceAttribute = xmlDoc.SelectSingleNode("IsNotBlank/Item[@SourceType = 'Control']").Attributes["SourceID"];
                        result.controlGuid = sourceAttribute != null ? new Guid(sourceAttribute.Value) : Guid.Empty;
                        result.Operator = "IsNotBlank";

                        controlPropEx = GetExpressionBySourceTypeFromOperands(result, PropertyExpressionSourceType.Control);
                        if (result.SubformGuid != controlPropEx.SourceSubFormGuid && controlPropEx.SourceSubFormGuid != Guid.Empty)
                        {
                            result.SubformGuid = controlPropEx.SourceSubFormGuid;
                        }

                        if (result.SubformGuid != Guid.Empty)
                        {
                            result.RuleConditionName = "SubFormSimpleNotBlankFormControlCondition";
                            GetSubFormAction(result, result.SubformGuid, result.Event);
                            ResolveExternalForm(result);
                            ResolveControl(result, result.Form != null ? result.Form.Controls : new ControlCollection(null), controlPropEx);
                            result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionFormSubFormNameNotBlank, result.formName, result.controlName, null, condition);
                            result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                        }
                        else
                        {
                            ResolveForm(result, result.Event);
                            ResolveControl(result, result.Form != null ? result.Form.Controls : new ControlCollection(null), controlPropEx);
                            result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionFormNameNotBlank, result.formName, result.controlName, null, condition);
                        }
                        break;

                    case "SimpleBlankFormParameterCondition":
                    case "SubFormSimpleBlankFormParameterCondition":
                        sourceAttribute = xmlDoc.SelectSingleNode("IsBlank/Item[@SourceType = 'FormParameter']").Attributes["SourceID"];
                        parameterName = sourceAttribute != null ? sourceAttribute.Value : string.Empty;
                        result.Operator = "IsBlank";

                        paramPropEx = GetExpressionBySourceTypeFromOperands(result, PropertyExpressionSourceType.FormParameter);
                        if (result.SubformGuid != paramPropEx.SourceSubFormGuid && paramPropEx.SourceSubFormGuid != Guid.Empty)
                        {
                            result.SubformGuid = paramPropEx.SourceSubFormGuid;
                        }

                        if (result.SubformGuid != Guid.Empty)
                        {
                            GetSubFormAction(result, result.SubformGuid, result.Event);
                            ResolveExternalForm(result);
                            ResolveFormParameter(result, parameterName);
                            result.RuleConditionName = "SubFormSimpleBlankFormParameterCondition";
                            result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionFormSubFormParameterBlank, result.Form.DisplayName, result.parameterName, null, condition);
                            result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                        }
                        else
                        {
                            ResolveForm(result, result.Event);
                            ResolveFormParameter(result, parameterName);
                            result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionFormParameterBlank, result.formName, result.parameterName, null, condition);
                        }
                        break;

                    case "SimpleNotBlankFormParameterCondition":
                    case "SubFormSimpleNotBlankFormParameterCondition":
                        sourceAttribute = xmlDoc.SelectSingleNode("IsNotBlank/Item[@SourceType = 'FormParameter']").Attributes["SourceID"];
                        parameterName = sourceAttribute != null ? sourceAttribute.Value : string.Empty;
                        result.Operator = "IsNotBlank";

                        paramPropEx = GetExpressionBySourceTypeFromOperands(result, PropertyExpressionSourceType.FormParameter);
                        if (result.SubformGuid != paramPropEx.SourceSubFormGuid && paramPropEx.SourceSubFormGuid != Guid.Empty)
                        {
                            result.SubformGuid = paramPropEx.SourceSubFormGuid;
                        }

                        if (result.SubformGuid != Guid.Empty)
                        {
                            GetSubFormAction(result, result.SubformGuid, GetEvent(condition));
                            ResolveExternalForm(result);
                            ResolveFormParameter(result, parameterName);
                            result.RuleConditionName = "SubFormSimpleNotBlankFormParameterCondition";
                            result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionFormSubFormParameterNotBlank, result.formName, result.parameterName, null, condition);
                            result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                        }
                        else
                        {
                            ResolveForm(result, result.Event);
                            ResolveFormParameter(result, parameterName);
                            result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionFormParameterNotBlank, result.formName, result.parameterName, null, condition);
                        }
                        break;

                    case "SimpleBlankViewParameterCondition":
                    case "SubViewSimpleBlankViewParameterCondition":
                    case "SubFormViewSimpleBlankViewParameterCondition":
                        sourceAttribute = xmlDoc.SelectSingleNode("IsBlank/Item[@SourceType = 'ViewParameter']").Attributes["SourceID"];
                        parameterName = sourceAttribute != null ? sourceAttribute.Value : string.Empty;
                        result.Operator = "IsBlank";

                        paramPropEx = GetExpressionBySourceTypeFromOperands(result, PropertyExpressionSourceType.ViewParameter);
                        if (result.InstanceGuid != paramPropEx.SourceInstanceGuid && paramPropEx.SourceInstanceGuid != Guid.Empty)
                        {
                            result.InstanceGuid = paramPropEx.SourceInstanceGuid;
                        }
                        if (result.SubformGuid != paramPropEx.SourceSubFormGuid && paramPropEx.SourceSubFormGuid != Guid.Empty)
                        {
                            result.SubformGuid = paramPropEx.SourceSubFormGuid;
                        }

                        if (result.SubformGuid != Guid.Empty)
                        {
                            GetSubFormAction(result, result.SubformGuid, result.Event);
                            if (result.SubItemAction.ViewGuid != Guid.Empty)
                            {
                                ResolveExternalView(result);
                                ResolveViewParameter(result, parameterName);
                                result.RuleConditionName = "SubViewSimpleBlankViewParameterCondition";
                                result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionViewSubViewNameBlank, result.viewName, result.parameterName, null, condition);
                                result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                            }
                            else
                            {
                                ResolveExternalForm(result);
                                ResolveFormView(result, condition.Validation);
                                ResolveViewParameter(result, parameterName);
                                result.RuleConditionName = "SubFormViewSimpleBlankViewParameterCondition";
                                string friendlyName = Resources.RuleHelper.ConditionSubFormViewParameterBlank;
                                result.ConditionFriendlyName = FormatConditionName(friendlyName, result.viewName, result.parameterName, result.Operator, condition);
                                result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                            }
                        }
                        else if (result.InstanceGuid != Guid.Empty)
                        {
                            ResolveForm(result, result.Event);
                            ResolveFormView(result, condition.Validation);
                            ResolveViewParameter(result, parameterName);
                            result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionFormViewParameterBlank, result.viewName, result.parameterName, null, condition);
                        }
                        else
                        {
                            ResolveView(result, result.Event);
                            ResolveViewParameter(result, parameterName);
                            result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionFormViewParameterBlank, result.viewName, result.parameterName, null, condition);
                        }
                        break;

                    case "SimpleNotBlankViewParameterCondition":
                    case "SubViewSimpleNotBlankViewParameterCondition":
                    case "SubFormViewSimpleNotBlankViewParameterCondition":
                        sourceAttribute = xmlDoc.SelectSingleNode("IsNotBlank/Item[@SourceType = 'ViewParameter']").Attributes["SourceID"];
                        parameterName = sourceAttribute != null ? sourceAttribute.Value : string.Empty;
                        result.Operator = "IsNotBlank";

                        paramPropEx = GetExpressionBySourceTypeFromOperands(result, PropertyExpressionSourceType.ViewParameter);
                        if (result.InstanceGuid != paramPropEx.SourceInstanceGuid && paramPropEx.SourceInstanceGuid != Guid.Empty)
                        {
                            result.InstanceGuid = paramPropEx.SourceInstanceGuid;
                        }
                        if (result.SubformGuid != paramPropEx.SourceSubFormGuid && paramPropEx.SourceSubFormGuid != Guid.Empty)
                        {
                            result.SubformGuid = paramPropEx.SourceSubFormGuid;
                        }

                        if (result.SubformGuid != Guid.Empty)
                        {
                            GetSubFormAction(result, result.SubformGuid, result.Event);
                            if (result.SubItemAction.ViewGuid != Guid.Empty)
                            {
                                result.RuleConditionName = "SubViewSimpleNotBlankViewParameterCondition";
                                ResolveExternalView(result);
                                ResolveViewParameter(result, parameterName);
                                result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionViewSubViewNameBlank, result.viewName, result.parameterName, result.OperatorValue, condition);
                                result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                            }
                            else
                            {
                                result.RuleConditionName = "SubFormViewSimpleNotBlankViewParameterCondition";
                                ResolveExternalForm(result);
                                ResolveFormView(result, condition.Validation);
                                ResolveViewParameter(result, parameterName);
                                result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionSubFormViewParameterBlank, result.viewName, result.parameterName, result.Operator, condition);
                                result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                            }
                        }
                        else if (result.InstanceGuid != Guid.Empty)
                        {
                            ResolveForm(result, result.Event);
                            ResolveFormView(result, condition.Validation);
                            ResolveViewParameter(result, parameterName);
                            result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionFormViewParameterNotBlank, result.viewName, result.parameterName, null, condition);
                        }
                        else
                        {
                            ResolveView(result, result.Event);
                            ResolveViewParameter(result, parameterName);
                            result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionFormViewParameterNotBlank, result.viewName, result.parameterName, null, condition);
                        }
                        break;

                    case "FormViewSimpleBlankViewParameterCondition":
                        result.Operator = "IsBlank";
                        paramPropEx = GetExpressionBySourceTypeFromOperands(result, PropertyExpressionSourceType.ViewParameter);
                        if (result.InstanceGuid != paramPropEx.SourceInstanceGuid && paramPropEx.SourceInstanceGuid != Guid.Empty)
                        {
                            result.InstanceGuid = paramPropEx.SourceInstanceGuid;
                        }

                        ResolveForm(result, result.Event);
                        ResolveFormView(result, condition.Validation);
                        sourceAttribute = xmlDoc.SelectSingleNode("IsBlank/Item[@SourceType = 'ViewParameter']").Attributes["SourceID"];
                        parameterName = sourceAttribute != null ? sourceAttribute.Value : string.Empty;
                        ResolveViewParameter(result, parameterName);
                        result.RuleConditionName = "FormViewSimpleBlankViewParameterCondition";
                        result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionFormViewParameterBlank, result.viewName, result.parameterName, result.Operator, condition);
                        break;

                    case "FormViewSimpleNotBlankViewParameterCondition":
                        result.Operator = "IsNotBlank";
                        paramPropEx = GetExpressionBySourceTypeFromOperands(result, PropertyExpressionSourceType.ViewParameter);
                        if (result.InstanceGuid != paramPropEx.SourceInstanceGuid && paramPropEx.SourceInstanceGuid != Guid.Empty)
                        {
                            result.InstanceGuid = paramPropEx.SourceInstanceGuid;
                        }

                        ResolveForm(result, result.Event);
                        ResolveFormView(result, condition.Validation);
                        sourceAttribute = xmlDoc.SelectSingleNode("IsNotBlank/Item[@SourceType = 'ViewParameter']").Attributes["SourceID"];
                        parameterName = sourceAttribute != null ? sourceAttribute.Value : string.Empty;
                        ResolveViewParameter(result, parameterName);
                        result.RuleConditionName = "FormViewSimpleNotBlankViewParameterCondition";
                        result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionFormViewParameterNotBlank, result.viewName, result.parameterName, result.Operator, condition);
                        break;

                    case "SimpleEqualViewParameterCondition":
                    case "SubViewSimpleEqualViewParameterCondition":
                    case "SubFormViewSimpleEqualViewParameterCondition":
                        sourceAttribute = xmlDoc.SelectSingleNode("Equals/Item[@SourceType = 'ViewParameter']").Attributes["SourceID"];
                        parameterName = sourceAttribute != null ? sourceAttribute.Value : string.Empty;
                        result.parameterDataType = xmlDoc.SelectSingleNode("Equals/Item[@SourceType = 'ViewParameter']").Attributes["DataType"].Value;
                        result.OperatorValue = xmlDoc.SelectSingleNode("Equals/Item[@SourceType = 'Value']").InnerText;
                        result.Operator = "Equals";

                        paramPropEx = GetExpressionBySourceTypeFromOperands(result, PropertyExpressionSourceType.ViewParameter);
                        if (result.InstanceGuid != paramPropEx.SourceInstanceGuid && paramPropEx.SourceInstanceGuid != Guid.Empty)
                        {
                            result.InstanceGuid = paramPropEx.SourceInstanceGuid;
                        }
                        if (result.SubformGuid != paramPropEx.SourceSubFormGuid && paramPropEx.SourceSubFormGuid != Guid.Empty)
                        {
                            result.SubformGuid = paramPropEx.SourceSubFormGuid;
                        }

                        if (result.SubformGuid != Guid.Empty)
                        {
                            GetSubFormAction(result, result.SubformGuid, result.Event);
                            if (result.SubItemAction.ViewGuid != Guid.Empty)
                            {
                                result.RuleConditionName = "SubViewSimpleEqualViewParameterCondition";
                                ResolveExternalView(result);
                                ResolveViewParameter(result, parameterName);
                                result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionSubViewParameterEquals, result.viewName, result.parameterName, result.OperatorValue, condition);
                                result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                            }
                            else
                            {
                                result.RuleConditionName = "SubFormViewSimpleEqualViewParameterCondition";
                                ResolveExternalForm(result);
                                ResolveFormView(result, condition.Validation);
                                ResolveViewParameter(result, parameterName);
                                result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionSubFormViewParameterEquals, result.formName, result.viewName, result.parameterName, result.OperatorValue, condition);
                                result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                            }
                        }
                        else
                        {
                            result.RuleConditionName = "SimpleEqualViewParameterCondition";

                            if (result.InstanceGuid.Equals(Guid.Empty))
                            {
                                ResolveView(result, result.Event);
                                ResolveViewParameter(result, parameterName);
                                result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionViewParameterEquals, result.viewName, result.parameterName, result.OperatorValue, condition);
                            }
                            else
                            {
                                ResolveForm(result, result.Event);
                                ResolveFormView(result, condition.Validation);
                                ResolveViewParameter(result, parameterName);
                                result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionViewParameterEquals, result.viewName, result.parameterName, result.OperatorValue, condition);
                            }
                        }
                        break;
                    case "SimpleNotEqualViewParameterCondition":
                    case "SubViewSimpleNotEqualViewParameterCondition":
                    case "SubFormViewSimpleNotEqualViewParameterCondition":
                        sourceAttribute = xmlDoc.SelectSingleNode("NotEquals/Item[@SourceType = 'ViewParameter']").Attributes["SourceID"];
                        parameterName = sourceAttribute != null ? sourceAttribute.Value : string.Empty;
                        result.parameterDataType = xmlDoc.SelectSingleNode("NotEquals/Item[@SourceType = 'ViewParameter']").Attributes["DataType"].Value;
                        result.OperatorValue = xmlDoc.SelectSingleNode("NotEquals/Item[@SourceType = 'Value']").InnerText;
                        result.Operator = "NotEquals";

                        paramPropEx = GetExpressionBySourceTypeFromOperands(result, PropertyExpressionSourceType.ViewParameter);
                        if (result.InstanceGuid != paramPropEx.SourceInstanceGuid && paramPropEx.SourceInstanceGuid != Guid.Empty)
                        {
                            result.InstanceGuid = paramPropEx.SourceInstanceGuid;
                        }
                        if (result.SubformGuid != paramPropEx.SourceSubFormGuid && paramPropEx.SourceSubFormGuid != Guid.Empty)
                        {
                            result.SubformGuid = paramPropEx.SourceSubFormGuid;
                        }

                        if (result.SubformGuid != Guid.Empty)
                        {
                            GetSubFormAction(result, result.SubformGuid, result.Event);
                            if (result.SubItemAction.ViewGuid != Guid.Empty)
                            {
                                result.RuleConditionName = "SubViewSimpleNotEqualViewParameterCondition";
                                ResolveExternalView(result);
                                ResolveViewParameter(result, parameterName);
                                result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionSubViewParameterNotEquals, result.viewName, result.parameterName, result.OperatorValue, condition);
                                result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                            }
                            else
                            {
                                result.RuleConditionName = "SubFormViewSimpleNotEqualViewParameterCondition";
                                ResolveExternalForm(result);
                                ResolveFormView(result, condition.Validation);
                                ResolveViewParameter(result, parameterName);
                                result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionSubFormViewParameterNotEquals, result.formName, result.viewName, result.parameterName, result.Operator, condition);
                                result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                            }
                        }
                        else
                        {
                            result.RuleConditionName = "SimpleNotEqualViewParameterCondition";

                            if (result.InstanceGuid.Equals(Guid.Empty))
                            {
                                ResolveView(result, result.Event);
                                ResolveViewParameter(result, parameterName);
                                result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionViewParameterNotEquals, result.viewName, result.parameterName, result.OperatorValue, condition);
                            }
                            else
                            {
                                ResolveForm(result, result.Event);
                                ResolveFormView(result, condition.Validation);
                                ResolveViewParameter(result, parameterName);
                                result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionViewParameterEquals, result.viewName, result.parameterName, result.OperatorValue, condition);
                            }
                        }
                        break;

                    case "SimpleEqualFormParameterCondition":
                    case "SubFormSimpleEqualFormParameterCondition":
                        sourceAttribute = xmlDoc.SelectSingleNode("Equals/Item[@SourceType = 'FormParameter']").Attributes["SourceID"];
                        parameterName = sourceAttribute != null ? sourceAttribute.Value : string.Empty;
                        result.parameterDataType = xmlDoc.SelectSingleNode("Equals/Item[@SourceType = 'FormParameter']").Attributes["DataType"].Value;
                        result.OperatorValue = xmlDoc.SelectSingleNode("Equals/Item[@SourceType = 'Value']").InnerText;
                        result.Operator = "Equals";

                        paramPropEx = GetExpressionBySourceTypeFromOperands(result, PropertyExpressionSourceType.FormParameter);
                        if (result.SubformGuid != paramPropEx.SourceSubFormGuid && paramPropEx.SourceSubFormGuid != Guid.Empty)
                        {
                            result.SubformGuid = paramPropEx.SourceSubFormGuid;
                        }

                        if (result.SubformGuid != Guid.Empty)
                        {
                            result.RuleConditionName = "SubFormSimpleEqualFormParameterCondition";
                            GetSubFormAction(result, result.SubformGuid, result.Event);
                            ResolveExternalForm(result);
                            ResolveFormParameter(result, parameterName);
                            result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionFormSubFormParameterEquals, result.formName, result.parameterName, result.OperatorValue, condition);
                            result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                        }
                        else
                        {
                            ResolveForm(result, result.Event);
                            ResolveFormParameter(result, parameterName);
                            result.RuleConditionName = "SimpleEqualFormParameterCondition";
                            result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionFormParameterEquals, result.formName, result.parameterName, result.OperatorValue, condition);
                        }
                        break;

                    case "SimpleNotEqualFormParameterCondition":
                    case "SubFormSimpleNotEqualFormParameterCondition":
                        sourceAttribute = xmlDoc.SelectSingleNode("NotEquals/Item[@SourceType = 'FormParameter']").Attributes["SourceID"];
                        parameterName = sourceAttribute != null ? sourceAttribute.Value : string.Empty;
                        result.parameterDataType = xmlDoc.SelectSingleNode("NotEquals/Item[@SourceType = 'FormParameter']").Attributes["DataType"].Value;
                        result.OperatorValue = xmlDoc.SelectSingleNode("NotEquals/Item[@SourceType = 'Value']").InnerText;
                        result.Operator = "NotEquals";

                        paramPropEx = GetExpressionBySourceTypeFromOperands(result, PropertyExpressionSourceType.FormParameter);
                        if (result.SubformGuid != paramPropEx.SourceSubFormGuid && paramPropEx.SourceSubFormGuid != Guid.Empty)
                        {
                            result.SubformGuid = paramPropEx.SourceSubFormGuid;
                        }

                        if (result.SubformGuid != Guid.Empty)
                        {
                            result.RuleConditionName = "SubFormSimpleNotEqualFormParameterCondition";
                            GetSubFormAction(result, result.SubformGuid, result.Event);
                            ResolveExternalForm(result);
                            ResolveFormParameter(result, parameterName);
                            result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionFormSubFormParameterNotEquals, result.formName, result.parameterName, result.OperatorValue, condition);
                            result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                        }
                        else
                        {
                            ResolveForm(result, result.Event);
                            ResolveFormParameter(result, parameterName);
                            result.RuleConditionName = "SimpleNotEqualFormParameterCondition";
                            result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionFormParameterNotEquals, result.formName, result.parameterName, result.OperatorValue, condition);
                        }
                        break;

                    case "IsCurrentActivityContextCondition": // This has te exist to support previous versions < 4.6.11
                    case "ViewIsCurrentActivityContextCondition":
                    case "SubViewIsCurrentActivityContextCondition":
                    case "SubFormIsCurrentActivityContextCondition":
                    case "SubFormViewIsCurrentActivityContextCondition":
                    case "FormViewIsCurrentActivityContextCondition":
                    case "FormIsCurrentActivityContextCondition":
                    case "ServerIsCurrentActivityContextCondition":
                    case "ServerViewIsCurrentActivityContextCondition":
                    case "ServerSubViewIsCurrentActivityContextCondition":
                    case "ServerSubFormIsCurrentActivityContextCondition":
                    case "ServerSubFormViewIsCurrentActivityContextCondition":
                    case "ServerFormViewIsCurrentActivityContextCondition":
                    case "ServerFormIsCurrentActivityContextCondition":

                        string processFullName = string.Empty;
                        string activityName = string.Empty;
                        int index = 0;
                        string processActivityDisplayName;
                        string conditionPrefix = string.Empty;

                        var workflowActivityItem = xmlDoc.SelectSingleNode("Equals/Item[@SourceType='WorkflowActivity']");

                        if (workflowActivityItem != null)
                        {
                            result.parameterName = workflowActivityItem.Attributes["SourceID"].Value;
                            result.activityDisplayName = (workflowActivityItem.Attributes["SourceDisplayName"] != null)
                                ? workflowActivityItem.Attributes["SourceDisplayName"].Value : null;
                        }
                        else
                        {
                            workflowActivityItem = xmlDoc.SelectSingleNode("Equals/Item[@SourceType='Value']");
                            result.parameterName = workflowActivityItem.InnerText;
                        }

                        result.Operator = "Equals";
                        ResolveForm(result, result.Event);

                        if (condition.Handler.Conditions.IndexOf(condition) > 0)
                        {
                            conditionPrefix = Resources.RuleHelper.ConditionAnd;
                        }
                        else
                        {
                            conditionPrefix = Resources.RuleHelper.ConditionIf;
                        }

                        index = result.parameterName.LastIndexOf('\\');

                        if (index == -1)
                        {
                            throw new ArgumentException("activityFullname");
                        }

                        processFullName = result.parameterName.Substring(0, index);
                        result.activityName = result.parameterName.Substring(index + 1);
                        result.activityFullName = result.parameterName;

                        // Attempt to read the activity's display name from the process definition using the info provider
                        // InfoProvider.TryGetActivityDisplayName will return false if the activity was not found...
                        if (string.IsNullOrEmpty(result.activityDisplayName))
                        {
                            if (InfoProvider.TryGetActivityDisplayName(processFullName, result.activityName, out processActivityDisplayName))
                            {
                                result.activityDisplayName = processActivityDisplayName;
                            }
                            else
                            {
                                result.activityDisplayName = result.activityName; // If it was not found use the system name
                            }
                        }

                        if (result.SubformGuid.Equals(Guid.Empty))
                        {
                            if (result.InstanceGuid.Equals(Guid.Empty))
                            {
                                if (result.formGuid != Guid.Empty)
                                {
                                    if (condition.Properties["DesignTemplate"] != null && condition.Properties["DesignTemplate"] == "ServerProcessCondition")
                                    {
                                        result.RuleConditionName = "ServerFormIsCurrentActivityContextCondition";
                                    }
                                    else
                                    {
                                        result.RuleConditionName = "FormIsCurrentActivityContextCondition";
                                    }

                                    result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionIsCurrentActivityContext, result.activityDisplayName, condition);
                                }
                                else
                                {
                                    if (condition.Properties["DesignTemplate"] != null && condition.Properties["DesignTemplate"] == "ServerProcessCondition")
                                    {
                                        result.RuleConditionName = "ServerViewIsCurrentActivityContextCondition";
                                    }
                                    else
                                    {
                                        result.RuleConditionName = "ViewIsCurrentActivityContextCondition";
                                    }

                                    result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionIsCurrentActivityContext, result.activityDisplayName, condition);
                                }
                            }
                            else
                            {
                                ResolveFormView(result, condition.Validation);

                                if (condition.Properties["DesignTemplate"] != null && condition.Properties["DesignTemplate"] == "ServerProcessCondition")
                                {
                                    result.RuleConditionName = "ServerFormViewIsCurrentActivityContextCondition";
                                }
                                else
                                {
                                    result.RuleConditionName = "FormViewIsCurrentActivityContextCondition";
                                }

                                if (result.View != null)
                                {
                                    result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionFormViewIsCurrentActivity, result.View.DisplayName, result.activityDisplayName, condition);
                                }
                            }
                        }
                        else
                        {
                            GetSubFormAction(result, result.SubformGuid, result.Event);
                            if (result.SubItemAction != null)
                            {
                                if (result.SubItemAction.ViewGuid.Equals(Guid.Empty))
                                {
                                    ResolveExternalForm(result);
                                    if (result.InstanceGuid.Equals(Guid.Empty) || result.SubItemAction.InstanceGuid.Equals(result.InstanceGuid))
                                    {
                                        if (condition.Properties["DesignTemplate"] != null && condition.Properties["DesignTemplate"] == "ServerProcessCondition")
                                        {
                                            result.RuleConditionName = "ServerSubFormIsCurrentActivityContextCondition";
                                        }
                                        else
                                        {
                                            result.RuleConditionName = "SubFormIsCurrentActivityContextCondition";
                                        }

                                        result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                                        result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionSubFormIsCurrentActivity, result.formName, result.activityDisplayName, condition);
                                    }
                                    else
                                    {
                                        ResolveFormView(result, condition.Validation);

                                        if (condition.Properties["DesignTemplate"] != null && condition.Properties["DesignTemplate"] == "ServerProcessCondition")
                                        {
                                            result.RuleConditionName = "ServerSubFormViewIsCurrentActivityContextCondition";
                                        }
                                        else
                                        {
                                            result.RuleConditionName = "SubFormViewIsCurrentActivityContextCondition";
                                        }

                                        result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                                        result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionSubFormViewIsCurrentActivity, result.formName, result.viewName, result.activityDisplayName, condition);
                                    }
                                }
                                else
                                {
                                    ResolveExternalView(result);

                                    if (condition.Properties["DesignTemplate"] != null && condition.Properties["DesignTemplate"] == "ServerProcessCondition")
                                    {
                                        result.RuleConditionName = "ServerSubViewIsCurrentActivityContextCondition";
                                    }
                                    else
                                    {
                                        result.RuleConditionName = "SubViewIsCurrentActivityContextCondition";
                                    }

                                    result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                                    result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionSubViewIsCurrentActivity, result.viewName, result.activityDisplayName, condition);
                                }
                            }
                        }
                        break;
                    case "ViewRenderModeCondition":
                    case "SubViewRenderModeCondition":
                    case "SubFormViewRenderModeCondition":
                    case "SubFormRenderModeCondition":
                    case "FormRenderModeCondition":
                    case "FormViewRenderModeCondition":
                        result.renderMode = xmlDoc.SelectSingleNode("Equals/Item[@SourceType = 'Value']").InnerText;
                        result.Operator = "Equals";

                        if (result.SubformGuid != Guid.Empty)
                        {
                            GetSubFormAction(result, result.SubformGuid, result.Event);
                            result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                            if (result.SubItemAction != null)
                            {
                                if (result.SubItemAction.FormGuid == Guid.Empty) // Subview condition
                                {
                                    ResolveExternalView(result);
                                    result.RuleConditionName = "SubViewRenderModeCondition";
                                    result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionSubViewRenderMode,
                                    result.viewName, result.renderMode, condition);
                                }
                                else //SubForm or SubformView render condition
                                {
                                    ResolveExternalForm(result);
                                    result.RuleConditionName = "SubFormRenderModeCondition";
                                    result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionSubFormRenderMode,
                                        result.formName, result.renderMode, condition);
                                    if (!result.InstanceGuid.Equals(Guid.Empty))
                                    {
                                        result.RuleConditionName = "SubFormViewRenderModeCondition";
                                        ResolveFormView(result, condition.Validation);
                                        result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionSubFormViewRenderMode,
                                            result.formName, result.viewName, result.renderMode, condition);
                                    }
                                }
                            }
                        }
                        else
                        {
                            ResolveForm(result, result.Event);
                            if (result.formGuid != Guid.Empty)
                            {
                                result.RuleConditionName = "FormRenderModeCondition";
                                result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionFormRenderMode,
                                    result.renderMode, condition);
                                if (!result.InstanceGuid.Equals(Guid.Empty))
                                {
                                    result.RuleConditionName = "FormViewRenderModeCondition";
                                    ResolveFormView(result, condition.Validation);
                                    result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionFormViewRenderMode,
                                        result.viewName, result.renderMode, condition);
                                }
                            }
                            else
                            {
                                result.RuleConditionName = "ViewRenderModeCondition";
                                ResolveView(result, GetEvent(condition));
                                result.ConditionFriendlyName = FormatConditionName(Resources.RuleHelper.ConditionViewRenderMode,
                                    result.viewName, result.renderMode, condition);
                            }
                        }
                        break;
                    default:
                        result.ConditionFriendlyName = Resources.RuleHelper.ConditionAdvanced;
                        break;
                }

                if (!string.IsNullOrEmpty(result.RuleConditionName))
                {
                    ResolveCondition(result);
                }
            }
            catch (Exception ex)
            {
                Log.Message(Log.Level.Error, "RuleHelper", "BuildContext(LogicalExpression, Condition): Condition could not be resolved: {0} - {1}: {2}", result.RuleConditionName, condition.Guid, ex.ToString().Replace(Environment.NewLine, "\\"));
            }

            return result;
        }

        public Context BuildContext(Authoring.Eventing.Action action)
        {
            Context result = new Context();
            result.InstanceGuid = action.InstanceGuid;
            result.SubformGuid = action.SubFormGuid;
            result.SubformInstanceGuid = action.SubFormInstanceGuid;
            result.Action = action;
            result.viewGuid = action.ViewGuid;

            try
            {
                result.Event = GetEvent(action);
                result.EventGuid = result.Event.Guid;
                if (action.IsReference)
                {
                    if (action.ActionType == ActionType.Transfer || action.ActionType == ActionType.Execute)
                    {
                        MergeReferencedAction(action);
                    }
                }

                switch (action.ActionType)
                {
                    case ActionType.Exit:
                        result.RuleActionName = "RuleExit";
                        break;

                    case ActionType.Continue:
                        result.RuleActionName = "RuleContinue";
                        break;

                    case ActionType.Execute:
                        #region Execute

                        if (action.Properties.Contains("EventID"))
                        {
                            Event userEvent;
                            result.TargetEventGuid = new Guid(action.Properties["EventID"]);

                            var isServerExecute = action.Properties["DesignTemplate"] != null && action.Properties["DesignTemplate"] == "ServerRuleExecute";

                            userEvent = GetEvent(Origin, result);

                            var subFormAction = GetSubFormAction(result, action.SubFormGuid, result.Event);

                            if (action.SubFormGuid != Guid.Empty)
                            {
                                if (result.SubItemAction.FormGuid == Guid.Empty)
                                {
                                    //todo add subview logic here when the rules exist
                                    //should be similar to the below
                                    //ResolveExternalView(result);
                                    //result.RuleActionName = isServerExecute ? "ServerSubViewRuleExecute" : "SubViewRuleExecute";

                                    //result.EventFriendlyName = GetEventFriendlyNameForSubForm(subFormAction);

                                    //remove from
                                    result.SubItemAction = null;
                                    result.RuleActionName = isServerExecute ? "ServerRuleExecute" : "RuleExecute";

                                    if (result.Event.View == null)
                                    {
                                        ResolveForm(result, result.Event);
                                        userEvent = GetEvent(result.Form, result);
                                    }
                                    else
                                    {
                                        ResolveView(result, action);
                                        userEvent = GetEvent(result.View, result);
                                    }
                                    //remove until
                                }
                                else
                                {
                                    ResolveExternalForm(result);
                                    ResolveFormView(result);

                                    result.RuleActionName = isServerExecute ? "ServerOpenedFormRuleExecute" : "OpenedFormRuleExecute";

                                    result.EventFriendlyName = GetEventFriendlyNameForSubForm(subFormAction);
                                }
                            }
                            else
                            {
                                result.RuleActionName = isServerExecute ? "ServerRuleExecute" : "RuleExecute";

                                if (result.Event.View == null)
                                {
                                    ResolveForm(result, result.Event);
                                    userEvent = GetEvent(result.Form, result);
                                }
                                else
                                {
                                    ResolveView(result, action);
                                    userEvent = GetEvent(result.View, result);
                                }
                            }

                            if (userEvent != null)
                            {
                                userEvent.Properties.Set("RuleFriendlyName", GetRuleFriendlyName(userEvent));
                                result.RuleFriendlyName = userEvent.Properties["RuleFriendlyName"];
                            }

                        }
                        else if (action.ViewGuid != Guid.Empty && !string.IsNullOrEmpty(action.Method) && action.SubFormGuid != Guid.Empty && action.PanelGuid == Guid.Empty && action.ObjectGuid == Guid.Empty && action.ControlGuid == Guid.Empty && action.ItemState == ActionItemState.All)
                        {
                            GetSubFormAction(result, action.SubFormGuid, result.Event);

                            if (result.SubItemAction != null)
                            {
                                if (result.SubItemAction.FormGuid == Guid.Empty)
                                {
                                    ResolveExternalView(result);

                                    if (action.Properties["DesignTemplate"] != null && action.Properties["DesignTemplate"] == "ServerViewExecute")
                                    {
                                        result.RuleActionName = "ServerSubViewEventAction";
                                    }
                                    else
                                    {
                                        result.RuleActionName = "SubViewEventAction";
                                    }
                                }
                                else
                                {
                                    if (action.Properties["DesignTemplate"] != null && action.Properties["DesignTemplate"] == "ServerViewExecute")
                                    {
                                        result.RuleActionName = "ServerOpenedFormViewMethodExecute";
                                    }
                                    else
                                    {
                                        result.RuleActionName = "OpenedFormViewMethodExecute";
                                    }

                                    ResolveExternalForm(result);
                                    ResolveFormView(result);
                                }

                                result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                            }
                        }
                        else if (action.ViewGuid != Guid.Empty && !string.IsNullOrEmpty(action.Method) && action.ControlGuid == Guid.Empty && action.ObjectGuid == Guid.Empty && action.PanelGuid == Guid.Empty && action.ItemState != ActionItemState.All) //All is default value and is not used in Actions, if its not All, then the user has set it
                        {
                            if (result.SubformGuid != Guid.Empty)
                            {
                                result.RuleActionName = "Sub";
                                GetSubFormAction(result, action.SubFormGuid, result.Event);

                                if (result.SubItemAction.FormGuid != Guid.Empty)
                                {
                                    ResolveExternalForm(result);
                                    ResolveFormView(result);
                                    result.RuleActionName += "Form";
                                }
                                else
                                {
                                    ResolveExternalView(result);
                                    result.RuleActionName += "View";
                                }

                                result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                            }
                            else
                            {
                                if (result.Event.View == null)
                                {
                                    ResolveForm(result, result.Event);
                                    ResolveFormView(result);
                                }
                                else
                                {
                                    ResolveView(result, action);
                                }
                            }

                            result.RuleActionName += "ViewMethodExecuteItemsState";
                        }
                        else if (action.ViewGuid != Guid.Empty && !string.IsNullOrEmpty(action.Method) && action.ControlGuid == Guid.Empty && action.PanelGuid == Guid.Empty && action.ObjectGuid != Guid.Empty && action.ItemState != ActionItemState.All)
                        {
                            if (action.SubFormGuid != Guid.Empty)
                            {
                                result.RuleActionName = "Sub";
                                GetSubFormAction(result, action.SubFormGuid, result.Event);

                                if (result.SubItemAction.FormGuid != Guid.Empty)
                                {
                                    ResolveExternalForm(result);
                                    ResolveFormView(result);
                                    result.RuleActionName += "Form";
                                }
                                else
                                {
                                    result.RuleActionName += "View";
                                    ResolveExternalView(result);
                                }

                                result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                            }
                            else
                            {
                                if (result.Event.View == null)
                                {
                                    ResolveForm(result, result.Event);
                                    ResolveFormView(result);
                                }
                                else
                                {
                                    ResolveView(result, action);
                                }
                            }

                            result.RuleActionName += "ObjectMethodExecuteItemsState";

                            WSA.Property SmartObject = action.Properties.Get("ObjectID");
                            result.ObjectGuid = action.ObjectGuid;
                            result.ObjectSystemName = SmartObject.NameValue;
                            result.ObjectName = SmartObject.DisplayValue;
                        }
                        else if (action.ViewGuid != Guid.Empty && !string.IsNullOrEmpty(action.Method) && action.FormGuid == Guid.Empty && action.ControlGuid == Guid.Empty && action.PanelGuid == Guid.Empty && action.ObjectGuid == Guid.Empty && action.SubFormGuid == Guid.Empty && action.ItemState == ActionItemState.All)
                        {
                            if (action.Properties["DesignTemplate"] != null && action.Properties["DesignTemplate"] == "ServerViewExecute")
                            {
                                result.RuleActionName = "ServerViewMethodExecute";
                            }
                            else
                            {
                                result.RuleActionName = "ViewMethodExecute";
                            }

                            if (result.Event.View == null)
                            {
                                ResolveForm(result, result.Event);
                                ResolveFormView(result);
                            }
                            else
                            {
                                ResolveView(result, action);
                            }
                        }
                        else if (action.ViewGuid != Guid.Empty && !string.IsNullOrEmpty(action.Method) && action.ControlGuid != Guid.Empty && action.ObjectGuid != Guid.Empty && action.PanelGuid == Guid.Empty && action.SubFormGuid == Guid.Empty && action.FormGuid == Guid.Empty && action.ItemState == ActionItemState.All && action.Results.Count > 0)
                        {
                            if (action.Properties["DesignTemplate"] != null && action.Properties["DesignTemplate"] == "ServerControlPopulation")
                            {
                                result.RuleActionName = "ServerViewListControlPopulation";
                            }
                            else
                            {
                                result.RuleActionName = "ViewListControlPopulation";
                            }


                            WSA.Property SmartObject = action.Properties.Get("ObjectID");
                            result.ObjectGuid = action.ObjectGuid;
                            result.ObjectSystemName = SmartObject.NameValue;
                            result.ObjectName = SmartObject.DisplayValue;

                            if (result.Event.View == null)
                            {
                                ResolveForm(result, result.Event);
                                ResolveFormView(result);
                            }
                            else
                            {
                                ResolveView(result, action);
                            }

                            ResolveControl(result, result.View != null ? result.View.Controls : new ControlCollection(null), action);
                            if (result.Control != null && result.Control.Properties["AssociationSO"] == null)
                            {
                                result.Control = null;
                            }
                        }
                        else if (action.ViewGuid != Guid.Empty && !string.IsNullOrEmpty(action.Method) && action.ControlGuid != Guid.Empty && action.ObjectGuid != Guid.Empty && action.PanelGuid == Guid.Empty && action.SubFormGuid != Guid.Empty && action.ItemState == ActionItemState.All && action.Results.Count > 0)
                        {
                            GetSubFormAction(result, action.SubFormGuid, result.Event);

                            WSA.Property SmartObject = action.Properties.Get("ObjectID");
                            result.ObjectGuid = action.ObjectGuid;
                            result.ObjectSystemName = SmartObject.NameValue;
                            result.ObjectName = SmartObject.DisplayValue;

                            if (result.SubItemAction != null)
                            {
                                if (result.SubItemAction.FormGuid == Guid.Empty)
                                {

                                    if (action.Properties["DesignTemplate"] != null && action.Properties["DesignTemplate"] == "ServerControlPopulation")
                                    {
                                        result.RuleActionName = "ServerSubViewListControlPopulation";
                                    }
                                    else
                                    {
                                        result.RuleActionName = "SubViewListControlPopulation";
                                    }

                                    ResolveExternalView(result);
                                }
                                else
                                {
                                    if (action.Properties["DesignTemplate"] != null && action.Properties["DesignTemplate"] == "ServerControlPopulation")
                                    {
                                        result.RuleActionName = "ServerSubFormListControlPopulation";
                                    }
                                    else
                                    {
                                        result.RuleActionName = "SubFormListControlPopulation";
                                    }

                                    ResolveExternalForm(result);
                                    ResolveFormView(result);
                                }

                                result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                                ResolveControl(result, result.View != null ? result.View.Controls : new ControlCollection(null), action);
                                if (result.Control != null && result.Control.Properties["AssociationSO"] == null)
                                {
                                    result.Control = null;
                                }
                            }
                        }
                        else if (action.ViewGuid != Guid.Empty && !string.IsNullOrEmpty(action.Method) && action.ControlGuid != Guid.Empty && action.ObjectGuid != Guid.Empty && action.PanelGuid == Guid.Empty && (action.ItemState == ActionItemState.Checked || action.ItemState == ActionItemState.Unchecked))
                        {
                            if (result.SubformGuid != Guid.Empty)
                            {
                                GetSubFormAction(result, action.SubFormGuid, result.Event);

                                if (result.SubItemAction != null)
                                {
                                    if (result.SubItemAction.ViewGuid != Guid.Empty)
                                    {
                                        result.RuleActionName = "Sub";
                                        ResolveExternalView(result);
                                    }
                                    else if (result.SubItemAction.FormGuid != Guid.Empty)
                                    {
                                        result.RuleActionName = "SubForm";
                                        ResolveExternalForm(result);
                                        ResolveFormView(result);
                                    }
                                }

                                result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                            }
                            else
                            {
                                if (result.Event.View == null)
                                {
                                    ResolveForm(result, result.Event);
                                    ResolveFormView(result);
                                }
                                else
                                {
                                    ResolveView(result, action);
                                }
                            }

                            result.RuleActionName += "ViewControlMethodExecuteItemsState";

                            WSA.Property SmartObject = action.Properties.Get("ObjectID");
                            result.ObjectGuid = action.ObjectGuid;
                            result.ObjectSystemName = SmartObject.NameValue;
                            result.ObjectName = SmartObject.DisplayValue;

                            ResolveControl(result, result.View != null ? result.View.Controls : new ControlCollection(null), action);
                        }
                        else if (action.ViewGuid != Guid.Empty && !string.IsNullOrEmpty(action.Method) && action.ControlGuid != Guid.Empty && action.ObjectGuid != Guid.Empty && action.PanelGuid == Guid.Empty && action.ItemState == ActionItemState.All && action.Results.Count == 0)
                        {
                            if (action.SubFormGuid.Equals(Guid.Empty))
                            {
                                result.RuleActionName = "ViewListControlPreLoadData";

                                WSA.Property SmartObject = action.Properties.Get("ObjectID");
                                result.ObjectGuid = action.ObjectGuid;
                                result.ObjectSystemName = SmartObject.NameValue;
                                result.ObjectName = SmartObject.DisplayValue;

                                if (result.Event.View == null)
                                {
                                    ResolveForm(result, result.Event);
                                    ResolveFormView(result);
                                }
                                else
                                {
                                    ResolveView(result, action);
                                }

                                ResolveControl(result, result.View != null ? result.View.Controls : new ControlCollection(null), action);
                                if (result.Control != null && result.Control.Properties["AssociationSO"] == null)
                                {
                                    result.Control = null;
                                }
                            }
                            else
                            {
                                GetSubFormAction(result, action.SubFormGuid, result.Event);

                                WSA.Property SmartObject = action.Properties.Get("ObjectID");
                                result.ObjectGuid = action.ObjectGuid;
                                result.ObjectSystemName = SmartObject.NameValue;
                                result.ObjectName = SmartObject.DisplayValue;

                                if (result.SubItemAction != null)
                                {
                                    if (result.SubItemAction.FormGuid == Guid.Empty)
                                    {
                                        ResolveExternalView(result, action);
                                        result.RuleActionName = "SubViewListControlPreLoadData";
                                    }
                                    else
                                    {
                                        result.RuleActionName = "SubFormViewListControlPreLoadData";
                                        ResolveExternalForm(result);
                                        ResolveFormView(result);
                                    }

                                    ResolveControl(result, result.View != null ? result.View.Controls : new ControlCollection(null), action);
                                    if (result.Control != null && result.Control.Properties["AssociationSO"] == null)
                                    {
                                        result.Control = null;
                                    }
                                    result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                                }
                            }
                        }
                        else if (action.ViewGuid == Guid.Empty && !string.IsNullOrEmpty(action.Method) && action.ControlGuid == Guid.Empty && action.ObjectGuid != Guid.Empty && action.PanelGuid == Guid.Empty && action.FormGuid == Guid.Empty && action.ItemState == ActionItemState.All)
                        {
                            if (action.Properties["DesignTemplate"] != null && action.Properties["DesignTemplate"] == "ServerObjectMethodExecute")
                            {
                                result.RuleActionName = "ServerObjectMethodExecute";
                            }
                            else
                            {
                                result.RuleActionName = "ObjectMethodExecute";
                            }

                            WSA.Property SmartObject = action.Properties.Get("ObjectID");
                            result.ObjectGuid = action.ObjectGuid;
                            result.ObjectSystemName = SmartObject.NameValue;
                            result.ObjectName = SmartObject.DisplayValue;
                        }
                        else if (action.ViewGuid == Guid.Empty && !string.IsNullOrEmpty(action.Method) && action.ControlGuid == Guid.Empty && action.ObjectGuid == Guid.Empty && action.PanelGuid == Guid.Empty && action.FormGuid != Guid.Empty && action.ItemState == ActionItemState.All)
                        {
                            if (action.SubFormGuid != Guid.Empty)
                            {
                                GetSubFormAction(result, action.SubFormGuid, result.Event);
                                ResolveExternalForm(result);
                                result.RuleActionName = "Sub";
                                result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                            }
                            else
                            {
                                ResolveForm(result, result.Event);
                            }

                            result.RuleActionName += "FormExecute";
                        }

                        #endregion

                        break;
                    case ActionType.PopulateControl:

                        if (action.SubFormGuid.Equals(Guid.Empty))
                        {
                            result.RuleActionName = "ViewListControlPopulateFromData";

                            WSA.Property SmartObject = action.Properties.Get("ObjectID");
                            result.ObjectGuid = action.ObjectGuid;
                            result.ObjectSystemName = SmartObject.NameValue;
                            result.ObjectName = SmartObject.DisplayValue;

                            if (result.Event.View == null)
                            {
                                ResolveForm(result, result.Event);
                                ResolveFormView(result);
                            }
                            else
                            {
                                ResolveView(result, action);
                            }

                            ResolveControl(result, result.View != null ? result.View.Controls : new ControlCollection(null), action);
                            if (result.Control != null && result.Control.Properties["AssociationSO"] == null)
                            {
                                result.Control = null;
                            }
                        }
                        else
                        {
                            GetSubFormAction(result, action.SubFormGuid, result.Event);

                            if (result.SubItemAction != null)
                            {
                                WSA.Property SmartObject = action.Properties.Get("ObjectID");
                                result.ObjectGuid = action.ObjectGuid;
                                result.ObjectSystemName = SmartObject.NameValue;
                                result.ObjectName = SmartObject.DisplayValue;

                                if (result.SubItemAction.FormGuid == Guid.Empty)
                                {
                                    ResolveExternalView(result);
                                    result.RuleActionName = "SubViewListControlPopulateFromData";
                                }
                                else
                                {
                                    result.RuleActionName = "SubFormViewListControlPopulateFromData";
                                    ResolveExternalForm(result);
                                    ResolveFormView(result);
                                }

                                ResolveControl(result, result.View != null ? result.View.Controls : new ControlCollection(null), action);
                                if (result.Control != null && result.Control.Properties["AssociationSO"] == null)
                                {
                                    result.Control = null;
                                }
                                result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                            }
                        }

                        break;

                    case ActionType.Popup:
                        #region Popup
                        ResolveExternalView(result);
                        result.RuleActionName = "SubViewOpen";

                        if (!string.IsNullOrEmpty(action.Method))
                        {
                            result.RuleActionName += "MethodExecute";
                        }

                        result.EventFriendlyName = GetEventFriendlyNameForSubForm(action);
                        #endregion
                        break;

                    case ActionType.Close:
                        #region Close
                        if (action.Properties["CloseTarget"] != null && action.Properties["CloseTarget"] == "Form")
                        {
                            if (action.ViewGuid != Guid.Empty && !string.IsNullOrEmpty(action.Method))
                            {
                                result.RuleActionName = "SubViewCloseMethodExecute";

                                if (action.InstanceGuid != Guid.Empty)
                                {
                                    ResolveForm(result, result.Event);
                                    ResolveFormView(result);
                                }
                                else
                                {
                                    ResolveView(result, action);
                                }
                            }
                            else
                            {
                                result.RuleActionName = "SubformClose";
                            }

                            result.EventFriendlyName = GetEventFriendlyNameForSubForm(action);
                        }
                        else
                        {
                            result.RuleActionName = "BrowserClose";
                        }
                        #endregion
                        break;

                    case ActionType.Navigate:
                        #region Navigate
                        if (!string.IsNullOrEmpty(action.Properties["Url"]))
                        {
                            result.RuleActionName = "BrowserNavigate";
                        }
                        else
                        {
                            ResolveExternalForm(result);
                            result.RuleActionName = "FormNavigation";
                            if (!string.IsNullOrEmpty(action.Method))
                            {
                                result.RuleActionName += "ViewMethodExecute";
                                ResolveFormView(result);
                            }
                        }
                        #endregion
                        break;

                    case ActionType.Transfer:
                        ResolveTransferActionTemplate(action, result);
                        break;

                    case ActionType.Focus:
                        #region Focus
                        if (action.PanelGuid != Guid.Empty)
                        {
                            if (action.Properties["DesignTemplate"] != null && action.Properties["DesignTemplate"] == "ServerPanelFocus")
                            {
                                if (action.SubFormGuid != Guid.Empty)
                                {
                                    result.RuleActionName = "ServerSubFormPanelFocus";
                                    GetSubFormAction(result, action.SubFormGuid, result.Event);
                                    result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                                    if (result.SubItemAction != null)
                                    {
                                        ResolveExternalForm(result);
                                    }
                                }
                                else
                                {
                                    ResolveForm(result, result.Event);
                                    result.RuleActionName = "ServerPanelFocus";
                                }

                                ResolvePanel(result, action);
                            }
                            else
                            {
                                if (action.FormGuid != Guid.Empty && action.SubFormGuid == Guid.Empty)
                                {
                                    result.RuleActionName = "FormNavigationPanelFocus";
                                    ResolveExternalForm(result);
                                    ResolvePanel(result, action);
                                }
                                else
                                {
                                    if (action.SubFormGuid != Guid.Empty)
                                    {
                                        result.RuleActionName = "SubForm";
                                        GetSubFormAction(result, action.SubFormGuid, result.Event);
                                        result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);

                                        if (result.SubItemAction != null)
                                        {
                                            if (result.SubItemAction.FormGuid != action.FormGuid && action.FormGuid != Guid.Empty)
                                            {
                                                result.RuleActionName = "FormNavigation";
                                                ResolveExternalForm(result, action);
                                            }
                                            else
                                            {
                                                ResolveExternalForm(result);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        ResolveForm(result, result.Event);
                                    }

                                    result.RuleActionName += "PanelFocus";
                                }
                                ResolvePanel(result, action);
                            }
                        }
                        else if (action.ViewGuid != Guid.Empty)
                        {
                            if (action.Properties["DesignTemplate"] != null && action.Properties["DesignTemplate"] == "ServerViewFocus")
                            {
                                if (action.SubFormGuid != Guid.Empty)
                                {
                                    result.RuleActionName = "ServerSubFormViewFocus";
                                    GetSubFormAction(result, action.SubFormGuid, result.Event);
                                    result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                                    if (result.SubItemAction != null)
                                    {
                                        ResolveExternalForm(result);
                                    }
                                }
                                else
                                {
                                    ResolveForm(result, result.Event);
                                    result.RuleActionName = "ServerViewFocus";
                                }

                                ResolveFormView(result);
                            }
                            else
                            {
                                if (action.FormGuid != Guid.Empty)
                                {
                                    result.RuleActionName = "FormNavigationViewFocus";
                                    ResolveExternalForm(result);
                                    ResolveFormView(result);
                                }
                                else
                                {
                                    if (action.SubFormGuid != Guid.Empty)
                                    {
                                        GetSubFormAction(result, action.SubFormGuid, result.Event);
                                        ResolveExternalForm(result);
                                    }
                                    else
                                    {
                                        ResolveForm(result, result.Event);
                                    }

                                    ResolveFormView(result);
                                    result.RuleActionName = "ViewFocus";
                                }
                            }
                        }
                        #endregion
                        break;

                    case ActionType.Prompt:
                        result.RuleActionName = "ShowConfirmation";
                        break;

                    case ActionType.ShowMessage:
                        #region ShowMessage
                        if (action.Properties["MessageLocation"] == "Popup")
                        {
                            result.RuleActionName = "ShowAlert";
                        }
                        else
                        {
                            result.RuleActionName = "BrowseMessage";
                        }
                        #endregion
                        break;

                    case ActionType.Open:
                        #region Open
                        ResolveExternalForm(result, action);
                        result.RuleActionName = "FormOpen";
                        result.EventFriendlyName = GetEventFriendlyNameForSubForm(action);
                        #endregion
                        break;

                    case ActionType.Disable:
                        #region Disable
                        if (action.SubFormGuid != Guid.Empty)
                        {
                            result.RuleActionName = "Sub";
                            GetSubFormAction(result, action.SubFormGuid, result.Event);

                            if (result.SubItemAction != null)
                            {
                                ResolveExternalForm(result);
                                result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                            }
                        }
                        result.RuleActionName += "FormDisable";
                        #endregion
                        break;

                    case ActionType.Enable:
                        #region Enable
                        if (action.SubFormGuid != Guid.Empty)
                        {
                            result.RuleActionName = "Sub";
                            GetSubFormAction(result, action.SubFormGuid, result.Event);

                            if (result.SubItemAction != null)
                            {
                                ResolveExternalForm(result);
                                result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                            }
                        }

                        result.RuleActionName += "FormEnable";
                        #endregion
                        break;

                    case ActionType.ExecuteWorkflow:

                        #region ExecuteWorkflow
                        string processFullName = action.Properties["ProcessName"];
                        result.activityFullName = action.Properties["ActivityFullName"];
                        int index = 0;
                        string actionProcessDisplayName;
                        string processActivityDisplayName = action.Properties.GetDisplayValue("ActivityFullName");

                        switch (action.Method)
                        {
                            case "ActionProcess":
                                index = result.activityFullName.LastIndexOf('\\');

                                if (index == -1)
                                {
                                    throw new ArgumentException("activityFullname");
                                }

                                processFullName = result.activityFullName.Substring(0, index);
                                result.activityName = result.activityFullName.Substring(index + 1);

                                // Attempt to read the activity's display name from the process definition using the info provider
                                // InfoProvider.TryGetActivityDisplayName will return false if the activity was not found...
                                if (InfoProvider.TryGetActivityDisplayName(processFullName, result.activityName, out processActivityDisplayName))
                                {
                                    result.activityDisplayName = processActivityDisplayName;
                                }
                                else
                                {
                                    processActivityDisplayName = action.Properties.GetDisplayValue("ActivityFullName"); // Read from the reference details

                                    if (string.IsNullOrEmpty(processActivityDisplayName))
                                    {
                                        processActivityDisplayName = result.activityName; // If no display name found use system name
                                    }

                                    result.activityDisplayName = processActivityDisplayName;
                                }

                                if (!action.SubFormGuid.Equals(Guid.Empty))
                                {
                                    GetSubFormAction(result, action.SubFormGuid, result.Event);

                                    if (result.SubItemAction != null)
                                    {
                                        result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                                        if (!result.SubItemAction.ViewGuid.Equals(Guid.Empty))
                                        {
                                            result.RuleActionName = "SubViewProcessAction";
                                            ResolveExternalView(result);
                                        }
                                        else
                                        {
                                            result.RuleActionName = "SubFormProcessAction";
                                            ResolveExternalForm(result);

                                            if (!action.SubFormInstanceGuid.Equals(Guid.Empty))
                                            {
                                                result.RuleActionName = "SubFormViewProcessAction";
                                                ResolveFormView(result, action.Validation);
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    if (action.InstanceGuid.Equals(Guid.Empty))
                                    {
                                        ResolveView(result, action);
                                        result.RuleActionName = "ProcessAction";
                                    }
                                    else
                                    {
                                        ResolveForm(result, result.Event);
                                        ResolveFormView(result, action.Validation);
                                        result.RuleActionName = "ViewProcessAction";
                                    }
                                }

                                break;
                            case "StartProcess":
                                if (InfoProvider.TryGetProcessDisplayName(processFullName, out actionProcessDisplayName))
                                {
                                    result.processDisplayName = actionProcessDisplayName;
                                }

                                if (!action.SubFormGuid.Equals(Guid.Empty))
                                {
                                    result.SubformGuid = action.SubFormGuid;
                                    GetSubFormAction(result, result.SubformGuid, result.Event);
                                    result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);

                                    if (result.SubItemAction != null)
                                    {
                                        if (!result.SubItemAction.ViewGuid.Equals(Guid.Empty))
                                        {
                                            result.RuleActionName = "SubViewProcessStart";
                                            ResolveExternalView(result);
                                        }
                                        else
                                        {
                                            ResolveExternalForm(result);
                                            result.RuleActionName = "SubFormProcessStart";

                                            if (!action.SubFormInstanceGuid.Equals(Guid.Empty))
                                            {
                                                ResolveFormView(result, action.Validation);
                                                result.RuleActionName = "SubFormViewProcessStart";
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    if (action.InstanceGuid.Equals(Guid.Empty))
                                    {
                                        ResolveView(result, action);
                                        result.RuleActionName = "ProcessStart";
                                    }
                                    else
                                    {
                                        result.RuleActionName = "ViewProcessStart";
                                        ResolveForm(result, result.Event);
                                        ResolveFormView(result, action.Validation);
                                    }
                                }

                                break;
                            case "LoadProcess":
                                index = result.activityFullName.LastIndexOf('\\');

                                if (index == -1)
                                {
                                    throw new ArgumentException("activityFullname");
                                }

                                processFullName = result.activityFullName.Substring(0, index);
                                result.activityName = result.activityFullName.Substring(index + 1);

                                // Attempt to read the activity's display name from the process definition using the info provider
                                // InfoProvider.TryGetActivityDisplayName will return false if the activity was not found...
                                if (InfoProvider.TryGetActivityDisplayName(processFullName, result.activityName, out processActivityDisplayName))
                                {
                                    result.activityDisplayName = processActivityDisplayName;
                                }
                                else
                                {
                                    processActivityDisplayName = action.Properties.GetDisplayValue("ActivityFullName"); // Read from the reference details

                                    if (string.IsNullOrEmpty(processActivityDisplayName))
                                    {
                                        processActivityDisplayName = result.activityName; // If no display name found use system name
                                    }

                                    result.activityDisplayName = processActivityDisplayName;
                                }

                                if (!action.SubFormGuid.Equals(Guid.Empty))
                                {
                                    result.SubformGuid = action.SubFormGuid;
                                    GetSubFormAction(result, result.SubformGuid, result.Event);
                                    result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);

                                    if (result.SubItemAction != null)
                                    {
                                        if (!result.SubItemAction.ViewGuid.Equals(Guid.Empty))
                                        {
                                            if (action.Properties["DesignTemplate"] != null && action.Properties["DesignTemplate"] == "ServerProcessAction")
                                            {
                                                result.RuleActionName = "ServerSubViewProcessLoad";
                                            }
                                            else
                                            {
                                                result.RuleActionName = "SubViewProcessLoad";
                                            }
                                            ResolveExternalView(result);
                                        }
                                        else
                                        {

                                            ResolveExternalForm(result);

                                            if (action.Properties["DesignTemplate"] != null && action.Properties["DesignTemplate"] == "ServerProcessAction")
                                            {
                                                result.RuleActionName = "ServerSubFormProcessLoad";
                                            }
                                            else
                                            {
                                                result.RuleActionName = "SubFormProcessLoad";
                                            }


                                            if (!action.SubFormInstanceGuid.Equals(Guid.Empty))
                                            {
                                                ResolveFormView(result, action.Validation);

                                                if (action.Properties["DesignTemplate"] != null && action.Properties["DesignTemplate"] == "ServerProcessAction")
                                                {
                                                    result.RuleActionName = "ServerSubFormViewProcessLoad";
                                                }
                                                else
                                                {
                                                    result.RuleActionName = "SubFormViewProcessLoad";
                                                }

                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    if (action.InstanceGuid.Equals(Guid.Empty))
                                    {
                                        ResolveView(result, action);

                                        if (action.Properties["DesignTemplate"] != null && action.Properties["DesignTemplate"] == "ServerProcessAction")
                                        {
                                            result.RuleActionName = "ServerProcessLoad";
                                        }
                                        else
                                        {
                                            result.RuleActionName = "ProcessLoad";
                                        }
                                    }
                                    else
                                    {
                                        if (action.Properties["DesignTemplate"] != null && action.Properties["DesignTemplate"] == "ServerProcessAction")
                                        {
                                            result.RuleActionName = "ServerViewProcessLoad";
                                        }
                                        else
                                        {
                                            result.RuleActionName = "ViewProcessLoad";
                                        }

                                        ResolveForm(result, result.Event);
                                        ResolveFormView(result, action.Validation);
                                    }
                                }

                                break;
                        }
                        #endregion
                        break;

                    case ActionType.Validate:
                        result.RuleActionName = "FormValidateCondition";
                        break;

                    case ActionType.List:
                        #region List
                        if (action.SubFormGuid != Guid.Empty)
                        {
                            result.RuleActionName = "Sub";

                            GetSubFormAction(result, action.SubFormGuid, result.Event);
                            if (result.SubItemAction.FormGuid != Guid.Empty)
                            {
                                result.RuleActionName += "Form";
                            }
                            else
                            {
                                result.RuleActionName += "View";
                            }

                            result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                        }

                        switch (action.Method)
                        {
                            case "AddItem":
                                result.RuleActionName += "EditableListAddRow";
                                break;
                            case "EditItem":
                                result.RuleActionName += "EditableListEditRow";
                                break;
                            case "RemoveItem":
                                result.RuleActionName += "EditableListRemoveRow";
                                break;
                            case "AcceptItem":
                                result.RuleActionName += "EditableListApplyRow";
                                break;
                            case "CancelItem":
                                result.RuleActionName += "EditableListCancelRow";
                                break;
                        }

                        if (!action.SubFormGuid.Equals(Guid.Empty))
                        {
                            if (!result.SubItemAction.FormGuid.Equals(Guid.Empty))
                            {
                                ResolveExternalForm(result);
                                ResolveFormView(result);
                            }
                            else
                            {
                                ResolveExternalView(result);
                            }
                        }
                        else
                        {
                            if (result.Event.View == null)
                            {
                                ResolveForm(result, result.Event);
                                ResolveFormView(result);
                            }
                            else
                            {
                                ResolveView(result, action);
                            }
                        }
                        #endregion
                        break;

                    case ActionType.SendMail:
                        result.RuleActionName = "SendMail";
                        break;

                    case ActionType.ExecuteSharePoint:
                        result.RuleActionName = action.Method;
                        break;

                    case ActionType.ExecuteControl:
                        #region ExecuteControl
                        StringBuilder ruleActionName = new StringBuilder();
                        if (action.SubFormGuid != Guid.Empty)
                        {
                            GetSubFormAction(result, action.SubFormGuid, result.Event);

                            if (result.SubItemAction != null)
                            {
                                ruleActionName.Append("Sub");
                                if (result.SubItemAction.ViewGuid != Guid.Empty)
                                {
                                    ruleActionName.Append("View");
                                    ResolveExternalView(result);
                                    ResolveControl(result, result.View != null ? result.View.Controls : new ControlCollection(null), action);
                                }
                                else if (result.SubItemAction.FormGuid != Guid.Empty)
                                {
                                    ResolveExternalForm(result);
                                    ruleActionName.Append("Form");
                                    if (result.Action.SubFormInstanceGuid.Equals(Guid.Empty))
                                    {
                                        ResolveControl(result, result.Form != null ? result.Form.Controls : new ControlCollection(null), result.Action);
                                    }
                                    else
                                    {
                                        ruleActionName.Append("View");
                                        ResolveFormView(result);
                                        ResolveControl(result, result.View != null ? result.View.Controls : new ControlCollection(null), result.Action);
                                    }
                                }

                                result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                            }
                        }
                        else
                        {
                            if (result.Event.View == null && result.Event.Form != null)
                            {
                                ruleActionName.Append("Form");
                                ResolveForm(result, result.Event);
                                if (result.Action.InstanceGuid.Equals(Guid.Empty))
                                {
                                    ResolveControl(result, result.Form != null ? result.Form.Controls : new ControlCollection(null), action);
                                }
                                else
                                {
                                    ruleActionName.Append("View");
                                    ResolveFormView(result);
                                    ResolveControl(result, result.View != null ? result.View.Controls : new ControlCollection(null), action);
                                }
                            }
                            else
                            {
                                ResolveView(result, result.Action);
                                ResolveControl(result, result.View != null ? result.View.Controls : new ControlCollection(null), action);
                            }
                        }
                        ruleActionName.Append("ControlMethodExecute");
                        if (result.Control != null && !string.IsNullOrEmpty(result.Action.Method))
                        {
                            var controlTypes = InfoProvider.GetControlTypes();
                            var controlTypeInfo = controlTypes.FirstOrDefault(c => c.Name == result.Control.Type);
                            var method = controlTypeInfo.Methods.FirstOrDefault(m => m.Name == result.Action.Method);

                            result.methodDisplayName = method.DisplayName;
                        }

                        result.RuleActionName = ruleActionName.ToString();

                        #endregion

                        break;
                    case ActionType.Handler:
                        result.RuleActionName = "HandlerAction";
                        break;
                }

                if (result.RuleActionName != "FormValidateCondition" && !string.IsNullOrEmpty(result.RuleActionName))
                {
                    ResolveAction(result);
                }

                if (!string.IsNullOrEmpty(result.RuleActionName)
                    && action.ActionType != ActionType.Handler)
                {
                    BuildActionFriendlyName(result);
                }
            }
            catch (Exception ex)
            {
                Log.Message(Log.Level.Error, "RuleHelper", "BuildContext(Action): Action could not be resolved: {0} - {1}: {2}", result.RuleActionName, action.Guid, ex.ToString().Replace(Environment.NewLine, "\\"));
            }

            return result;
        }

        private void RemoveUnusedValidationGroups(StateCollection states, ValidationGroupCollection validationGroups)
        {
            // Get list of validaton group id's used in actions under all states
            List<Guid> validationGroupsReferencedByActions = new List<Guid>();
            foreach (State state in states)
            {
                foreach (Event ev in state.Events)
                {
                    foreach (Handler handler in ev.Handlers)
                    {
                        FindValidationGroupsByHandler(handler, validationGroupsReferencedByActions);
                    }
                }
            }

            // Find Validation Groups that are not in the above list
            List<Guid> validationGroupsToRemove = new List<Guid>();
            bool found = false;
            foreach (ValidationGroup validationGroup in validationGroups)
            {
                found = false;
                for (int i = 0; i < validationGroupsReferencedByActions.Count && !found; i++)
                {
                    if (validationGroup.Guid.Equals(validationGroupsReferencedByActions[i]))
                    {
                        found = true;
                        validationGroupsReferencedByActions.RemoveAt(i); //we know it exists, no need to keep comparing it
                    }
                }

                if (!found)
                {
                    validationGroupsToRemove.Add(validationGroup.Guid);
                }
            }

            // In theory validationGroupsReferencedByActions should now be empty. Otherwise there are missing validation groups.
            // Remove validation groups that are not used in the events.
            foreach (Guid guid in validationGroupsToRemove)
            {
                validationGroups.Remove(guid);
            }
        }

        private void FindValidationGroupsByHandler(Handler handler, List<Guid> validationGroups)
        {
            foreach (Authoring.Eventing.Action action in handler.Actions)
            {
                switch (action.ActionType)
                {
                    case ActionType.Handler:
                        foreach (Handler subHandler in action.Handlers)
                        {
                            // recursive for nested handlers
                            FindValidationGroupsByHandler(subHandler, validationGroups);
                        }
                        break;
                    case ActionType.Validate:
                        foreach (Authoring.Property prop in action.Properties)
                        {
                            if (prop.Name == "GroupID")
                            {
                                validationGroups.Add(new Guid(prop.Value));
                            }
                        }
                        break;
                    default:
                        break;
                }
            }
        }

        public string GetActionFriendlyName(Authoring.Eventing.Action listener, WSA.Form currentForm, WSA.View currentView)
        {
            var result = GetActionFriendlyNameValues(listener, currentForm, currentView);
            return string.Format(result.Key, result.Value.ToArray());
        }
        public string GetActionFriendlyName(Authoring.Eventing.Action listener, WSA.Form currentForm)
        {
            var result = GetActionFriendlyNameValues(listener, currentForm);
            return string.Format(result.Key, result.Value.ToArray());
        }
        public KeyValuePair<string, List<string>> GetActionFriendlyNameValues(Authoring.Eventing.Action listener, WSA.Form currentForm)
        {
            return GetActionFriendlyNameValues(listener, currentForm, null);
        }
        public KeyValuePair<string, List<string>> GetActionFriendlyNameValues(Authoring.Eventing.Action listener, WSA.Form currentForm, WSA.View currentView)
        {
            List<string> parts = new List<string>();

            XmlDocument ruleDefinition = XmlHelper.CreateXmlDocument("<Rule></Rule>");

            if (listener.ActionType != WSA.Eventing.ActionType.Validate)
            {
                XmlDocument ruleInstance = new XmlDocument();
                XmlNode actionNode = ruleInstance.CreateElement("Actions");
                ruleInstance.AppendChild(actionNode);
                //TODO: Change to use new helper stuff
                //rt.CreateAction(listener, ruleDefinition, actionNode, ruleInstance, currentView, context, currentForm);


                string actionName = actionNode.Attributes["Name"].Value;

                var context = (currentView == null) ? ContextType.FORM : ContextType.VIEW;
                XmlDocument objectXML = GetRuleDefinition(context);

                XmlNode ruleNode = objectXML.SelectSingleNode("SourceCode.Forms/RuleDefinitions/Actions/Action[@Name=" + XmlHelper.XPathParameterEncode(actionName) + "]");
                XmlNodeList list = ruleNode.SelectNodes("Parts/Part");
                foreach (XmlElement node in list)
                {
                    string name = node.GetAttribute("Name");
                    XmlNode partDisplay = actionNode.SelectSingleNode("Parts/Part[@Name=" + XmlHelper.XPathParameterEncode(name) + "]/Display");
                    parts.Add(partDisplay.InnerText);
                }

                string resourceName = ruleNode.SelectSingleNode("Description").Attributes["ResourceName"].Value;

                return new KeyValuePair<string, List<string>>(Resources.Rules.ResourceManager.GetString(resourceName), parts);
            }
            else
            {
                return new KeyValuePair<string, List<string>>(Resources.RuleHelper.ConditionValidate.ToString(), parts);
            }
        }
        #endregion

        #region Private Methods
        private void SetProperty(Authoring.Eventing.Action action, string propertyName, string value, string name, string displayName)
        {
            // Note that null should be changed back to the name parameter for V2
            action.Properties.Set(propertyName, value, null, displayName);
        }

        private Context BuildRuleEvent(Event ev, XmlNode eventNode)
        {
            Context context = BuildContext(ev);

            if ((ev.Validation.Status & Framework.ValidationStatus.Error) == Framework.ValidationStatus.Error
                || (ev.Validation.Status & Framework.ValidationStatus.Missing) == Framework.ValidationStatus.Missing)
            {
                AnnotateNodeAndRule(eventNode, context, ev.Validation);
            }

            if (!string.IsNullOrEmpty(context.RuleEventName))
            {
                switch (context.RuleEventName)
                {
                    case "FormWorkflowViewEvent":
                        BuildFormWorkflowViewEvent(eventNode, context);
                        break;
                    case "ViewWorkflowViewEvent":
                        BuildViewWorkflowViewEvent(eventNode, context);
                        break;
                    case "SubViewWorkflowViewEvent":
                        BuildSubViewWorkflowViewEvent(eventNode, context);
                        break;
                    case "SubFormWorkflowViewEvent":
                        BuildSubFormWorkflowViewEvent(eventNode, context);
                        break;
                    case "SubFormViewWorkflowViewEvent":
                        BuildSubFormViewWorkflowViewEvent(eventNode, context);
                        break;
                    case "FormWorkflowActioned":
                        BuildFormWorkflowActionedEvent(eventNode, context);
                        break;
                    case "WorkflowActioned":
                        BuildWorkflowActionedEvent(eventNode, context);
                        break;
                    case "SubViewWorkflowActioned":
                        BuildSubViewWorkflowActionedEvent(eventNode, context);
                        break;
                    case "SubFormWorkflowActioned":
                        BuildSubFormWorkflowActionedEvent(eventNode, context);
                        break;
                    case "SubFormViewWorkflowActioned":
                        BuildSubFormViewWorkflowActionedEvent(eventNode, context);
                        break;
                    case "FormViewWorkflowActioned":
                        BuildViewWorkflowActionedEvent(eventNode, context);
                        break;
                    case "FormEvent":
                        BuildFormEvent(eventNode, context);
                        break;
                    case "OpenedFormCloseEvent":
                        BuildOtherFormEvent(eventNode, context);
                        break;
                    case "OpenedViewCloseEvent":
                        BuildSubformClosedEvent(eventNode, context);
                        break;
                    case "SubViewEvent":
                        BuildSubformEvent(eventNode, context);
                        break;
                    case "OpenedFormViewEvent":
                        BuildFormPopupViewEvent(eventNode, context);
                        break;
                    case "ViewEvent":
                        BuildViewEvent(eventNode, context);
                        break;
                    case "ViewControlEvent":
                        BuildViewControlEvent(eventNode, context);
                        break;
                    case "FormControlEvent":
                        BuildFormControlEvent(eventNode, context);
                        break;
                    case "OpenedFormControlEvent":
                        BuildFormOpenedControlEvent(eventNode, context);
                        break;
                    case "SubViewControlEvent":
                        BuildSubformViewControlEvent(eventNode, context);
                        break;
                    case "OpenedFormViewControlEvent":
                        BuildFormPopupViewControlEvent(eventNode, context);
                        break;
                    case "OpenedFormEvent":
                        BuildOpenedFormEvent(eventNode, context);
                        break;
                    case "ViewParameterEvent":
                        BuildViewParameterEvent(eventNode, context);
                        break;
                    case "FormParameterEvent":
                        BuildFormParameterEvent(eventNode, context);
                        break;
                    case "FormViewParameterEvent":
                        BuildFormViewParameterEvent(eventNode, context);
                        break;
                    case "SubViewParameterEvent":
                        BuildSubViewParameterEvent(eventNode, context);
                        break;
                    case "SubFormParameterEvent":
                        BuildSubFormParameterEvent(eventNode, context);
                        break;
                    case "SubFormViewParameterEvent":
                        BuildSubFormViewParameterEvent(eventNode, context);
                        break;
                    case "FormServerPreRenderEvent":
                        BuildFormServerPreRenderEvent(eventNode, context);
                        break;
                    case "ViewServerPreRenderEvent":
                        BuildViewServerPreRenderEvent(eventNode, context);
                        break;
                    case "SubViewServerPreRenderEvent":
                        BuildSubViewServerPreRenderEvent(eventNode, context);
                        break;
                    case "SubFormServerPreRenderEvent":
                        BuildSubFormServerPreRenderEvent(eventNode, context);
                        break;
                    case "SubFormViewServerPreRenderEvent":
                        BuildSubFormViewServerPreRenderEvent(eventNode, context);
                        break;
                    default:
                        BuildEventlessRule(eventNode, context);
                        break;
                }
            }
            else
            {
                eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("TransformFailed"));
                eventNode.Attributes["TransformFailed"].Value = "true";
            }

            return context;
        }

        public void TransformAuthoringHandlerToRuleHandler(string context, XmlDocument ruleDefinition, XmlDocument ruleInstance, Handler handler, XmlNode handlersNode)
        {
            if (handler.Actions.Count == 0 && handler.Conditions.Count == 0) // Added incase there are empty handlers then they need to be removed
            {
                return;
            }

            Context handlerContext = BuildContext(handler);

            XmlNode handlerNode = ruleInstance.CreateElement("Handler");
            handlersNode.AppendChild(handlerNode);

            handlerNode.Attributes.Append(ruleInstance.CreateAttribute("ID"));
            handlerNode.Attributes["ID"].Value = handler.Guid.ToString().ToLowerInvariant();
            handlerNode.Attributes.Append(ruleInstance.CreateAttribute("IsReference"));
            handlerNode.Attributes["IsReference"].Value = handler.IsReference.ToString();
            handlerNode.Attributes.Append(ruleInstance.CreateAttribute("DefinitionID"));
            handlerNode.Attributes["DefinitionID"].Value = handler.DefinitionGuid.ToString().ToLowerInvariant();
            handlerNode.Attributes.Append(ruleInstance.CreateAttribute("IsInherited"));
            handlerNode.Attributes["IsInherited"].Value = (handler.IsInherited).ToString();
            handlerNode.Attributes.Append(ruleInstance.CreateAttribute("HandlerType"));
            handlerNode.Attributes["HandlerType"].Value = (handler.HandlerType).ToString();
            handlerNode.Attributes.Append(ruleInstance.CreateAttribute("Context"));
            handlerNode.Attributes["Context"].Value = !string.IsNullOrEmpty(handler.Properties["Location"]) ? handler.Properties["Location"] : context;
            handlerNode.Attributes.Append(ruleInstance.CreateAttribute("IsEnabled"));
            handlerNode.Attributes["IsEnabled"].Value = handler.IsEnabled.ToString();

            if ((handler.Validation.Status & Framework.ValidationStatus.Error) == Framework.ValidationStatus.Error)
            {
                handlerNode.Attributes.Append(handlerNode.OwnerDocument.CreateAttribute("Invalid"));
                handlerNode.Attributes["Invalid"].Value = "true";
            }

            handlerNode.Attributes.Append(handlerNode.OwnerDocument.CreateAttribute("Name"));
            handlerNode.Attributes["Name"].Value = handlerContext.RuleHandlerName;

            switch (handlerContext.handlerType)
            {
                case HandlerType.If:
                    BuildIfHandler(handler, handlerNode, handlerContext);
                    break;
                case HandlerType.Else:
                    BuildElseHandler(handler, handlerNode, handlerContext);
                    break;
                case HandlerType.Error:
                    BuildErrorHandler(handler, handlerNode, handlerContext);
                    break;
                case HandlerType.ForEach:
                    BuildFunctionHandler(handler, handlerNode, handlerContext);
                    break;
            }

            if (!string.IsNullOrEmpty(handler.Properties["Comments"]))
            {
                XmlNode commentsNode = ruleInstance.CreateElement("Comments");
                commentsNode.AppendChild(ruleInstance.CreateTextNode(handler.Properties["Comments"]));
                handlerNode.AppendChild(commentsNode);
            }

            XmlNode conditionsNode = ruleInstance.CreateElement("Conditions");
            handlerNode.AppendChild(conditionsNode);

            if (handler.Conditions.Count > 0)
            {
                foreach (Authoring.Eventing.Condition condition in handler.Conditions)
                {
                    foreach (Authoring.Filters.LogicalExpression lp in condition.Expressions)
                    {
                        XmlNode conditionNode = ruleInstance.CreateElement("Condition");
                        conditionsNode.AppendChild(conditionNode);

                        conditionNode.Attributes.Append(ruleInstance.CreateAttribute("IsCurrentHandler"));
                        conditionNode.Attributes["IsCurrentHandler"].Value = (!condition.IsReference).ToString();
                        conditionNode.Attributes.Append(ruleInstance.CreateAttribute("Enabled"));
                        conditionNode.Attributes["Enabled"].Value = condition.IsEnabled.ToString();
                        conditionNode.Attributes.Append(ruleInstance.CreateAttribute("ID"));
                        conditionNode.Attributes["ID"].Value = condition.Guid.ToString().ToLowerInvariant();
                        conditionNode.Attributes.Append(ruleInstance.CreateAttribute("DefinitionID"));
                        conditionNode.Attributes["DefinitionID"].Value = condition.DefinitionGuid.ToString().ToLowerInvariant();
                        conditionNode.Attributes.Append(ruleInstance.CreateAttribute("Context"));
                        conditionNode.Attributes["Context"].Value = condition.Properties["Location"] != null ? condition.Properties["Location"] : context;

                        if (!string.IsNullOrEmpty(condition.Properties["Comments"]))
                        {
                            XmlNode commentsNode = ruleInstance.CreateElement("Comments");
                            commentsNode.AppendChild(ruleInstance.CreateTextNode(condition.Properties["Comments"]));
                            conditionNode.AppendChild(commentsNode);
                        }

                        BuildRuleCondition(lp, conditionNode, condition);

                        conditionsNode.AppendChild(conditionNode);
                    }
                }
            }

            if (handler.Actions.Count > 0)
            {
                XmlNode actionsNode = ruleInstance.CreateElement("Actions");
                handlerNode.AppendChild(actionsNode);

                foreach (Authoring.Eventing.Action action in handler.Actions)
                {
                    XmlNode actionNode = ruleInstance.CreateElement("Action");
                    actionsNode.AppendChild(actionNode);

                    actionNode.Attributes.Append(ruleInstance.CreateAttribute("IsCurrentHandler"));
                    actionNode.Attributes["IsCurrentHandler"].Value = (!action.IsReference).ToString();
                    actionNode.Attributes.Append(ruleInstance.CreateAttribute("Enabled"));
                    actionNode.Attributes["Enabled"].Value = action.IsEnabled.ToString();
                    actionNode.Attributes.Append(ruleInstance.CreateAttribute("ID"));
                    actionNode.Attributes["ID"].Value = action.Guid.ToString().ToLowerInvariant();
                    actionNode.Attributes.Append(ruleInstance.CreateAttribute("DefinitionID"));
                    actionNode.Attributes["DefinitionID"].Value = action.DefinitionGuid.ToString().ToLowerInvariant();
                    actionNode.Attributes.Append(ruleInstance.CreateAttribute("Context"));
                    actionNode.Attributes["Context"].Value = action.Properties["Location"] != null ? action.Properties["Location"] : context;

                    if (!string.IsNullOrEmpty(action.Properties["Comments"]))
                    {
                        XmlNode commentsNode = ruleInstance.CreateElement("Comments");
                        commentsNode.AppendChild(ruleInstance.CreateTextNode(action.Properties["Comments"]));
                        actionNode.AppendChild(commentsNode);
                    }

                    BuildRuleAction(action, actionNode, context, ruleDefinition, ruleInstance);
                }
            }
        }

        private void BuildRuleCondition(LogicalExpression lp, XmlNode conditionNode, Condition condition)
        {
            Context context = BuildContext(lp, condition);

            if ((condition.Validation.Status & Framework.ValidationStatus.Error) == Framework.ValidationStatus.Error)
            {
                AnnotateNodeAndRule(conditionNode, context, condition.Validation);
            }

            if (!string.IsNullOrEmpty(context.RuleConditionName))
            {
                conditionNode.Attributes.Append(conditionNode.OwnerDocument.CreateAttribute("Name"));
                conditionNode.Attributes["Name"].Value = context.RuleConditionName;

                switch (context.RuleConditionName)
                {
                    case "ServerAdvancedCondition":
                    case "AdvancedCondition":
                        BuildAdvancedCondition(lp, conditionNode, condition, context);
                        break;
                    case "SimpleEqualControlCondition":
                        BuildSimpleEqualControlCondition(conditionNode, context);
                        break;
                    case "SimpleNotBlankControlCondition":
                        BuildSimpleNotBlankControlCondition(conditionNode, context);
                        break;
                    case "SimpleNotEqualControlCondition":
                        BuildSimpleNotEqualControlCondition(conditionNode, context);
                        break;
                    case "SimpleBlankControlCondition":
                        BuildSimpleBlankControlCondition(conditionNode, context);
                        break;
                    case "SubViewSimpleBlankControlCondition":
                        BuildSubViewSimpleBlankControlCondition(conditionNode, context);
                        break;
                    case "SubViewSimpleNotBlankControlCondition":
                        BuildSubViewSimpleNotBlankControlCondition(conditionNode, context);
                        break;
                    case "SubViewSimpleEqualControlCondition":
                        BuildSubViewSimpleEqualControlCondition(conditionNode, context);
                        break;
                    case "SubViewSimpleNotEqualControlCondition":
                        BuildSubViewSimpleNotEqualControlCondition(conditionNode, context);
                        break;
                    case "SubFormSimpleNotBlankControlCondition":
                        BuildSubFormSimpleNotBlankControlCondition(conditionNode, context);
                        break;
                    case "SubFormSimpleBlankControlCondition":
                        BuildSubFormSimpleBlankControlCondition(conditionNode, context);
                        break;
                    case "SubFormSimpleEqualControlCondition":
                        BuildSubFormSimpleEqualControlCondition(conditionNode, context);
                        break;
                    case "SubFormSimpleNotEqualControlCondition":
                        BuildSubFormSimpleNotEqualControlCondition(conditionNode, context);
                        break;
                    case "SimpleEqualFormControlCondition":
                        BuildSimpleEqualFormControlCondition(conditionNode, context);
                        break;
                    case "SimpleNotBlankFormControlCondition":
                        BuildSimpleNotBlankFormControlCondition(conditionNode, context);
                        break;
                    case "SimpleNotEqualFormControlCondition":
                        BuildSimpleNotEqualFormControlCondition(conditionNode, context);
                        break;
                    case "SimpleBlankFormControlCondition":
                        BuildSimpleBlankFormControlCondition(conditionNode, context);
                        break;
                    case "SubFormSimpleNotBlankFormControlCondition":
                        BuildSubFormSimpleNotBlankFormControlCondition(conditionNode, context);
                        break;
                    case "SubFormSimpleBlankFormControlCondition":
                        BuildSubFormSimpleBlankFormControlCondition(conditionNode, context);
                        break;
                    case "SubFormSimpleEqualFormControlCondition":
                        BuildSubFormSimpleEqualFormControlCondition(conditionNode, context);
                        break;
                    case "SubFormSimpleNotEqualFormControlCondition":
                        BuildSubFormSimpleNotEqualFormControlCondition(conditionNode, context);
                        break;
                    case "SimpleNotBlankFormParameterCondition":
                        BuildSimpleNotBlankFormParameterCondition(conditionNode, context);
                        break;
                    case "SimpleBlankFormParameterCondition":
                        BuildSimpleBlankFormParameterCondition(conditionNode, context);
                        break;
                    case "SubFormSimpleBlankFormParameterCondition":
                        BuildSubFormSimpleBlankFormParameterCondition(conditionNode, context);
                        break;
                    case "SubFormSimpleNotBlankFormParameterCondition":
                        BuildSubFormSimpleNotBlankFormParameterCondition(conditionNode, context);
                        break;

                    case "SimpleBlankViewParameterCondition":
                        BuildSimpleBlankFormParameterCondition(conditionNode, context, true);
                        break;
                    case "SimpleNotBlankViewParameterCondition":
                        BuildSimpleNotBlankFormParameterCondition(conditionNode, context, true);
                        break;
                    case "SubViewSimpleBlankViewParameterCondition":
                        BuildSubViewSimpleBlankViewParameterCondition(conditionNode, context);
                        break;
                    case "SubViewSimpleNotBlankViewParameterCondition":
                        BuildSubViewSimpleNotBlankViewParameterCondition(conditionNode, context);
                        break;

                    case "FormViewSimpleBlankViewParameterCondition":
                        BuildFormViewSimpleViewParameterCondition(conditionNode, context);
                        break;

                    case "SubFormViewSimpleBlankViewParameterCondition":
                    case "SubFormViewSimpleNotBlankViewParameterCondition":
                        BuildSubFormViewSimpleViewParameterCondition(conditionNode, context);
                        break;

                    case "FormViewSimpleNotBlankViewParameterCondition":
                        BuildFormViewSimpleViewParameterCondition(conditionNode, context);
                        break;
                    case "SimpleEqualViewParameterCondition":
                    case "SimpleNotEqualViewParameterCondition":
                        BuildSimpleViewParameterCondition(conditionNode, context);
                        break;
                    case "SubViewSimpleEqualViewParameterCondition":
                    case "SubViewSimpleNotEqualViewParameterCondition":
                        BuildSubViewSimpleViewParameterCondition(conditionNode, context);
                        break;
                    case "SubFormViewSimpleEqualViewParameterCondition":
                    case "SubFormViewSimpleNotEqualViewParameterCondition":
                        BuildSubFormViewSimpleEqualViewParameterCondition(conditionNode, context);
                        break;
                    case "SimpleEqualFormParameterCondition":
                    case "SimpleNotEqualFormParameterCondition":
                        BuildSimpleFormParameterCondition(conditionNode, context);
                        break;
                    case "SubFormSimpleEqualFormParameterCondition":
                    case "SubFormSimpleNotEqualFormParameterCondition":
                        BuildSubFormSimpleEqualFormParameterCondition(conditionNode, context);
                        break;
                    case "ViewIsCurrentActivityContextCondition":
                    case "ServerViewIsCurrentActivityContextCondition":
                        BuildViewIsCurrentActivityContextCondition(conditionNode, context);
                        break;
                    case "SubViewIsCurrentActivityContextCondition":
                    case "ServerSubViewIsCurrentActivityContextCondition":
                        BuildSubViewIsCurrentActivityContextCondition(conditionNode, context);
                        break;
                    case "SubFormIsCurrentActivityContextCondition":
                    case "ServerSubFormIsCurrentActivityContextCondition":
                        BuildSubFormIsCurrentActivityContextCondition(conditionNode, context);
                        break;
                    case "SubFormViewIsCurrentActivityContextCondition":
                    case "ServerSubFormViewIsCurrentActivityContextCondition":
                        BuildSubFormViewIsCurrentActivityContextCondition(conditionNode, context);
                        break;
                    case "FormViewIsCurrentActivityContextCondition":
                    case "ServerFormViewIsCurrentActivityContextCondition":
                        BuildFormViewIsCurrentActivityContextCondition(conditionNode, context);
                        break;
                    case "FormIsCurrentActivityContextCondition":
                    case "ServerFormIsCurrentActivityContextCondition":
                        BuildFormIsCurrentActivityContextCondition(conditionNode, context);
                        break;
                    case "ViewRenderModeCondition":
                        BuildViewRenderModeCondition(conditionNode, context);
                        break;
                    case "FormViewRenderModeCondition":
                        BuildFormViewRenderModeCondition(conditionNode, context);
                        break;
                    case "SubViewRenderModeCondition":
                        BuildSubViewRenderModeCondition(conditionNode, context);
                        break;
                    case "SubFormRenderModeCondition":
                        BuildSubFormRenderModeCondition(conditionNode, context);
                        break;
                    case "SubFormViewRenderModeCondition":
                        BuildSubFormViewRenderModeCondition(conditionNode, context);
                        break;
                    case "FormRenderModeCondition":
                        BuildFormRenderModeCondition(conditionNode, context);
                        break;
                    default:
                        break;
                }
            }
            else
            {
                conditionNode.Attributes.Append(conditionNode.OwnerDocument.CreateAttribute("TransformFailed"));
                conditionNode.Attributes["TransformFailed"].Value = "true";
            }
        }

        private void BuildRuleAction(Authoring.Eventing.Action action, XmlNode actionNode, string designerContext, XmlDocument ruleDefinition, XmlDocument ruleInstance)
        {
            Context context = BuildContext(action);
            context.context = designerContext;
            context.ruleDefinition = ruleDefinition;
            context.ruleInstance = ruleInstance;

            if ((action.Validation.Status & Framework.ValidationStatus.Error) == Framework.ValidationStatus.Error
                || (action.Validation.Status & Framework.ValidationStatus.Missing) == Framework.ValidationStatus.Missing)
            {
                AnnotateNodeAndRule(actionNode, context, action.Validation);
            }

            if (!string.IsNullOrEmpty(context.RuleActionName))
            {
                switch (context.RuleActionName)
                {
                    case "RuleExecute":
                    case "ServerRuleExecute":
                    case "ServerOpenedFormRuleExecute":
                    case "OpenedFormRuleExecute":
                        BuildRuleExecuteAction(actionNode, context);
                        break;
                    case "RuleExit":
                        BuildRuleExitAction(actionNode, context);
                        break;
                    case "RuleContinue":
                        BuildRuleContinueAction(actionNode, context);
                        break;
                    case "SubViewEventAction":
                    case "ServerSubViewEventAction":
                        BuildSubViewEventAction(actionNode, context);
                        break;
                    case "OpenedFormViewMethodExecute":
                    case "ServerOpenedFormViewMethodExecute":
                        BuildSubFormViewMethodExecute(actionNode, context);
                        break;
                    case "ViewMethodExecuteItemsState":
                        BuildViewMethodExecuteItemsState(actionNode, context);
                        break;
                    case "ObjectMethodExecuteItemsState":
                        BuildObjectMethodExecuteItemsState(actionNode, context);
                        break;
                    case "SubViewViewMethodExecuteItemsState":
                        BuildSubViewViewMethodExecuteItemsState(actionNode, context);
                        break;
                    case "SubViewObjectMethodExecuteItemsState":
                        BuildSubViewObjectMethodExecuteItemsState(actionNode, context);
                        break;
                    case "SubFormViewMethodExecuteItemsState":
                        BuildSubFormViewMethodExecuteItemsState(actionNode, context);
                        break;
                    case "SubFormObjectMethodExecuteItemsState":
                        BuildSubFormObjectMethodExecuteItemsState(actionNode, context);
                        break;
                    case "ViewMethodExecute":
                    case "ServerViewMethodExecute":
                        BuildViewAction(actionNode, context);
                        break;
                    case "FormExecute":
                        BuildFormAction(actionNode, context);
                        break;
                    case "SubFormExecute":
                        BuildSubFormAction(actionNode, context);
                        break;
                    case "ViewListControlPopulation":
                    case "ServerViewListControlPopulation":
                        BuildListControlPopulation(actionNode, context);
                        break;
                    case "SubViewListControlPopulation":
                    case "ServerSubViewListControlPopulation":
                        BuildSubViewListControlPopulation(actionNode, context);
                        break;
                    case "SubFormListControlPopulation":
                    case "ServerSubFormListControlPopulation":
                        BuildSubFormListControlPopulation(actionNode, context);
                        break;
                    case "ViewListControlPreLoadData":
                        BuildViewListControlPreLoadData(actionNode, context);
                        break;
                    case "SubViewListControlPreLoadData":
                        BuildSubViewListControlPreLoadData(actionNode, context);
                        break;
                    case "SubFormViewListControlPreLoadData":
                        BuildSubFormViewListControlPreLoadData(actionNode, context);
                        break;
                    case "ViewListControlPopulateFromData":
                        BuildViewListControlPopulateFromData(actionNode, context);
                        break;
                    case "SubViewListControlPopulateFromData":
                        BuildSubViewListControlPopulateFromData(actionNode, context);
                        break;
                    case "SubFormViewListControlPopulateFromData":
                        BuildSubFormViewListControlPopulateFromData(actionNode, context);
                        break;
                    case "ViewControlMethodExecuteItemsState":
                        BuildViewControlMethodExecuteItemsState(actionNode, context);
                        break;
                    case "SubViewControlMethodExecuteItemsState":
                        BuildSubViewControlMethodExecuteItemsState(actionNode, context);
                        break;
                    case "SubFormViewControlMethodExecuteItemsState":
                        BuildSubFormViewControlMethodExecuteItemsState(actionNode, context);
                        break;
                    case "ObjectMethodExecute":
                    case "ServerObjectMethodExecute":
                        BuildObjectMethodExecute(actionNode, context);
                        break;
                    case "SubViewOpenMethodExecute":
                    case "SubViewOpen":
                        BuildSubformAction(actionNode, context);
                        break;
                    case "SubViewCloseMethodExecute":
                    case "SubformClose":
                        BuildSubformCloseAction(actionNode, context);
                        break;
                    case "FormDisable":
                    case "SubFormDisable":
                        BuildFormDisable(actionNode, context);
                        break;
                    case "FormEnable":
                    case "SubFormEnable":
                        BuildFormEnable(actionNode, context);
                        break;
                    case "FormNavigationViewMethodExecute":
                    case "FormNavigation":
                        BuildNavigateAction(actionNode, context);
                        break;
                    case "ServerSubViewTransferData":
                    case "SubViewTransferData":
                        BuildSubformAction(actionNode, context);
                        break;
                    case "ServerOpenedFormTransfer":
                    case "OpenedFormTransfer":
                        BuildOpenedFormTransfer(actionNode, context);
                        break;
                    case "ShowControl":
                    case "HideControl":
                        BuildShowHideControl(actionNode, context);
                        break;
                    case "SubViewShowControl":
                    case "SubViewHideControl":
                        BuildSubformShowHideControl(actionNode, context);
                        break;
                    case "SubFormShowControl":
                    case "SubFormHideControl":
                        BuildSubFormShowHideControl(actionNode, context);
                        break;
                    case "EnableControl":
                    case "DisableControl":
                        BuildEnableDisableControl(actionNode, context);
                        break;
                    case "SubViewEnableControl":
                    case "SubViewDisableControl":
                        BuildSubformEnableDisableControl(actionNode, context);
                        break;
                    case "SubFormEnableControl":
                    case "SubFormDisableControl":
                        BuildSubFormEnableDisableControl(actionNode, context);
                        break;
                    case "HideView":
                    case "ShowView":
                    case "HideViewFilter":
                    case "ShowViewFilter":
                    case "SubViewHideViewFilter":
                    case "SubViewShowViewFilter":
                        BuildViewShowHide(actionNode, context);
                        break;
                    case "SubFormHideViewFilter":
                    case "SubFormShowViewFilter":
                        BuildSubFormViewFilterShowHide(actionNode, context);
                        break;
                    case "EnableView":
                    case "DisableView":
                        BuildViewEnableDisable(actionNode, context);
                        break;
                    case "SubViewEnableView":
                    case "SubViewDisableView":
                        BuildSubformViewEnableDisable(actionNode, context);
                        break;
                    case "SubFormEnableView":
                    case "SubFormDisableView":
                        BuildSubFormViewEnableDisable(actionNode, context);
                        break;
                    case "ExpandView":
                    case "CollapseView":
                        BuildViewExpandCollapse(actionNode, context);
                        break;
                    case "SubViewExpandView":
                    case "SubViewCollapseView":
                        BuildSubformViewExpandCollapse(actionNode, context);
                        break;
                    case "SubFormExpandView":
                    case "SubFormCollapseView":
                        BuildSubFormViewExpandCollapse(actionNode, context);
                        break;
                    case "SubFormHideView":
                    case "SubFormShowView":
                        BuildSubFormViewShowHide(actionNode, context);
                        break;
                    case "ServerControlTransfer":
                    case "ControlTransfer":
                        BuildControlTransfer(actionNode, context);
                        break;
                    case "ShowFormControl":
                    case "HideFormControl":
                    case "EnableFormControl":
                    case "DisableFormControl":
                        BuildFormControlActionParts(actionNode, context);
                        break;
                    case "ShowSubFormControl":
                    case "HideSubFormControl":
                    case "EnableSubFormControl":
                    case "DisableSubFormControl":
                        BuildSubFormControlActionParts(actionNode, context);
                        break;
                    case "HidePanel":
                    case "ShowPanel":
                        BuildPanelShowHide(actionNode, context);
                        break;
                    case "SubFormHidePanel":
                    case "SubFormShowPanel":
                        BuildSubFormPanelShowHide(actionNode, context);
                        break;
                    case "FormNavigationPanelFocus":
                    case "PanelFocus":
                    case "SubFormPanelFocus":
                        BuildPanelFocus(actionNode, context);
                        break;
                    case "ServerPanelFocus":
                    case "ServerSubFormPanelFocus":
                        BuildServerPanelFocus(actionNode, context);
                        break;
                    case "FormNavigationViewFocus":
                    case "ViewFocus":
                        BuildViewFocus(actionNode, context);
                        break;
                    case "ServerViewFocus":
                    case "ServerSubFormViewFocus":
                        BuildServerViewFocus(actionNode, context);
                        break;
                    case "ShowConfirmation":
                        BuildPromptAction(actionNode, context);
                        break;
                    case "ShowAlert":
                    case "BrowseMessage":
                        BuildMessageAction(actionNode, context);
                        break;
                    case "FormOpen":
                        BuildFormOpenAction(actionNode, context);
                        break;
                    case "ProcessAction":
                        BuildActionProcessAction(actionNode, context);
                        break;
                    case "ViewProcessAction":
                        BuildActionViewProcessAction(actionNode, context);
                        break;
                    case "SubViewProcessAction":
                        BuildActionSubViewProcessAction(actionNode, context);
                        break;
                    case "SubFormProcessAction":
                        BuildActionSubFormProcessAction(actionNode, context);
                        break;
                    case "SubFormViewProcessAction":
                        BuildActionSubFormViewProcessAction(actionNode, context);
                        break;
                    case "ProcessStart":
                        BuildProcessStartAction(actionNode, context);
                        break;
                    case "ViewProcessStart":
                        BuildViewProcessStartAction(actionNode, context);
                        break;
                    case "SubViewProcessStart":
                        BuildSubViewProcessStartAction(actionNode, context);
                        break;
                    case "SubFormProcessStart":
                        BuildSubFormProcessStartAction(actionNode, context);
                        break;
                    case "SubFormViewProcessStart":
                        BuildSubFormViewProcessStartAction(actionNode, context);
                        break;
                    case "ProcessLoad":
                    case "ServerProcessLoad":
                        BuildProcessLoadAction(actionNode, context);
                        break;
                    case "ViewProcessLoad":
                    case "ServerViewProcessLoad":
                        BuildViewProcessLoadAction(actionNode, context);
                        break;
                    case "SubViewProcessLoad":
                    case "ServerSubViewProcessLoad":
                        BuildSubViewProcessLoadAction(actionNode, context);
                        break;
                    case "SubFormProcessLoad":
                    case "ServerSubFormProcessLoad":
                        BuildSubFormProcessLoadAction(actionNode, context);
                        break;
                    case "SubFormViewProcessLoad":
                    case "ServerSubFormViewProcessLoad":
                        BuildSubFormViewProcessLoadAction(actionNode, context);
                        break;
                    case "FormValidateCondition":
                        BuildFormValidateCondition(actionNode, context);
                        break;
                    case "EditableListAddRow":
                    case "EditableListEditRow":
                    case "EditableListRemoveRow":
                    case "EditableListApplyRow":
                    case "EditableListCancelRow":
                        BuildCaptureListRowFunctionality(actionNode, context);
                        break;
                    case "SubViewEditableListAddRow":
                    case "SubViewEditableListEditRow":
                    case "SubViewEditableListRemoveRow":
                    case "SubViewEditableListApplyRow":
                    case "SubViewEditableListCancelRow":
                        BuildSubViewCaptureListRowFunctionality(actionNode, context);
                        break;
                    case "SubFormEditableListAddRow":
                    case "SubFormEditableListEditRow":
                    case "SubFormEditableListRemoveRow":
                    case "SubFormEditableListApplyRow":
                    case "SubFormEditableListCancelRow":
                        BuildSubFormCaptureListRowFunctionality(actionNode, context);
                        break;
                    case "SubViewControlReadOnly":
                        BuildSubViewControlReadOnly(actionNode, context);
                        break;
                    case "SubFormControlReadOnly":
                        BuildSubFormControlReadOnly(actionNode, context);
                        break;
                    case "SetViewControlProperties":
                        BuildSetViewControlProperties(actionNode, context);
                        break;
                    case "SubViewSetViewControlProperties":
                        BuildSetSubViewControlProperties(actionNode, context);
                        break;
                    case "SubFormSetViewControlProperties":
                        BuildSetSubFormViewControlProperties(actionNode, context);
                        break;
                    case "SetAreaItemProperties":
                    case "ServerSetAreaItemProperties":
                        BuildSetFormAreaItemProperties(actionNode, context);
                        break;
                    case "SubFormSetAreaItemProperties":
                    case "ServerSetSubFormAreaItemProperties":
                        BuildSubFormSetFormAreaItemProperties(actionNode, context);
                        break;
                    case "SetFormControlProperties":
                        BuildSetFormControlProperties(actionNode, context);
                        break;
                    case "SetFormViewControlProperties":
                        BuildSetFormViewControlProperties(actionNode, context);
                        break;
                    case "SubFormSetFormControlProperties":
                        BuildSetSubFormControlProperties(actionNode, context);
                        break;
                    case "ServerSetViewControlProperties":
                        BuildServerSetViewControlProperties(actionNode, context);
                        break;
                    case "ServerSetFormControlProperties":
                        BuildServerSetFormControlProperties(actionNode, context);
                        break;
                    case "ServerSetFormViewControlProperties":
                        BuildServerSetFormViewControlProperties(actionNode, context);
                        break;
                    case "ServerSetSubViewControlProperties":
                        BuildServerSetSubViewControlProperties(actionNode, context);
                        break;
                    case "ServerSetSubFormViewControlProperties":
                        BuildServerSetSubFormViewControlProperties(actionNode, context);
                        break;
                    case "ServerSetSubFormControlProperties":
                        BuildServerSetSubFormControlProperties(actionNode, context);
                        break;
                    case "ControlMethodExecute":
                        BuildControlMethodExecute(actionNode, context);
                        break;
                    case "SubViewControlMethodExecute":
                        BuildSubViewControlMethodExecute(actionNode, context);
                        break;
                    case "FormControlMethodExecute":
                        BuildFormControlMethodExecute(actionNode, context);
                        break;
                    case "FormViewControlMethodExecute":
                        BuildFormViewControlMethodExecute(actionNode, context);
                        break;
                    case "SubFormControlMethodExecute":
                        BuildSubFormControlMethodExecute(actionNode, context);
                        break;
                    case "SubFormViewControlMethodExecute":
                        BuildSubFormViewControlMethodExecute(actionNode, context);
                        break;
                    case "SetSubFormProperties":
                    case "ServerSetSubFormProperties":
                        BuildSetSubFormProperties(actionNode, context);
                        break;
                    case "HandlerAction":
                        BuildHandlerAction(actionNode, context);
                        break;
                    default:
                        BuildCommonActionParts(actionNode, context, true);
                        break;
                }
                BuildMappingXML(actionNode, context);

                if (context.RuleActionName == "ShowAlert" && !action.IsReference) //Exclude IsReference ShowAlert action as the mappings are stripped out
                {
                    if (!action.Properties.GetBoolean("HeadingIsLiteral", true))
                    {
                        XmlNode headingValueNode = actionNode.SelectSingleNode("Mappings/Mapping[Item[@Name='Heading']]/Item[@ContextType='value']");

                        if (headingValueNode != null)
                        {
                            XmlAttribute headingLiteralAttribute = headingValueNode.OwnerDocument.CreateAttribute("Literal");
                            headingLiteralAttribute.Value = "False";
                            headingValueNode.Attributes.Append(headingLiteralAttribute);
                        }
                    }

                    if (!action.Properties.GetBoolean("BodyIsLiteral", true))
                    {
                        XmlNode bodyValueNode = actionNode.SelectSingleNode("Mappings/Mapping[Item[@Name='Body']]/Item[@ContextType='value']");

                        if (bodyValueNode != null)
                        {
                            XmlAttribute bodyLiteralAttribute = bodyValueNode.OwnerDocument.CreateAttribute("Literal");
                            bodyLiteralAttribute.Value = "False";
                            bodyValueNode.Attributes.Append(bodyLiteralAttribute);
                        }
                    }
                }
            }
            else
            {
                actionNode.Attributes.Append(actionNode.OwnerDocument.CreateAttribute("TransformFailed"));
                actionNode.Attributes["TransformFailed"].Value = "true";
            }
        }

        private Event CreateEventFromRuleXML(XmlNode ruleNode, Authoring.Form currentForm, Authoring.View currentView, Event ev, Event existingEvent)
        {
            XmlNode ruleEventNode = ruleNode.SelectSingleNode("Events/Event");
            XmlNodeList eventPartsList = ruleEventNode.SelectNodes("Parts/Part");
            string eventName = ruleEventNode.Attributes["Name"].Value;
            Guid subFormID = Guid.Empty;
            Guid instanceID = Guid.Empty;

            if (ruleEventNode.SelectSingleNode("Parts/Part/Data/Item[@SubFormID]") != null && !string.IsNullOrEmpty(ruleEventNode.SelectSingleNode("Parts/Part/Data/Item[@SubFormID]").Attributes["SubFormID"].Value))
            {
                subFormID = new Guid(ruleEventNode.SelectSingleNode("Parts/Part/Data/Item[@SubFormID]").Attributes["SubFormID"].Value);
            }

            if (ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']") != null)
            {
                if (ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Data/Item/InstanceID") != null && !string.IsNullOrEmpty(ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Data/Item/InstanceID").InnerText))
                {
                    instanceID = new Guid(ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Data/Item/InstanceID").InnerText);
                }
            }
            else
            {
                if (ruleEventNode.SelectSingleNode("Parts/Part[@Name='Form']/Data/Item/InstanceID") != null && !string.IsNullOrEmpty(ruleEventNode.SelectSingleNode("Parts/Part[@Name='Form']/Data/Item/InstanceID").InnerText))
                {
                    instanceID = new Guid(ruleEventNode.SelectSingleNode("Parts/Part[@Name='Form']/Data/Item/InstanceID").InnerText);
                }
            }

            if (ruleEventNode.SelectSingleNode("Comments") != null)
            {
                string commentsValue = ruleEventNode.SelectSingleNode("Comments").InnerText;
                if (string.IsNullOrEmpty(commentsValue) && ev.Properties["Comments"] != null)
                {
                    ev.Properties.Remove("Comments");
                }
                else
                {
                    ev.Properties.Set("Comments", commentsValue);
                }
            }

            if (instanceID != Guid.Empty) { ev.InstanceGuid = instanceID; }
            if (subFormID != Guid.Empty) { ev.SubFormGuid = subFormID; }

            XmlNode isSingleSpinnerNode = ruleNode.SelectSingleNode("SingleSpinner");
            if (isSingleSpinnerNode != null && isSingleSpinnerNode.InnerText.ToLowerInvariant() == "true")
            {
                ev.Properties["SingleSpinner"] = "true";
            }

            XmlAttribute sourceNameAttr = ruleEventNode.Attributes["SourceName"];
            if (sourceNameAttr != null)
            {
                ev.SourceName = sourceNameAttr.Value;
            }

            XmlAttribute sourceDisplayNameAttr = ruleEventNode.Attributes["SourceDisplayName"];
            if (sourceDisplayNameAttr != null)
            {
                ev.SourceDisplayName = sourceDisplayNameAttr.Value;
            }

            string viewId, viewDisplayName;
            string formId, formDisplayName;

            switch (eventName)
            {
                case "ViewEvent":
                case "SubViewEvent":
                    ev.SourceType = EventSourceType.View;
                    ev.SourceGuid = new Guid(ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Value").InnerText);
                    ev.Name = ruleEventNode.SelectSingleNode("Parts/Part[@Name='ViewMethod']/Value").InnerText;
                    ev.Properties.Set("ViewID", ev.SourceGuid.ToString(), ev.SourceName, ev.SourceDisplayName);
                    break;
                case "ViewControlEvent":
                case "SubViewControlEvent":
                    ev.SourceType = EventSourceType.Control;
                    ev.SourceGuid = new Guid(ruleEventNode.SelectSingleNode("Parts/Part[@Name='ViewControl']/Value").InnerText);
                    ev.Name = ruleEventNode.SelectSingleNode("Parts/Part[@Name='ControlEvent']/Value").InnerText;
                    viewId = ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Value").InnerText;
                    viewDisplayName = ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Display")?.InnerText;
                    ev.Properties.Set("ViewID", viewId, string.Empty, viewDisplayName);
                    break;
                case "FormEvent":
                    ev.SourceType = EventSourceType.Form;
                    ev.Name = ruleEventNode.SelectSingleNode("Parts/Part[@Name='FormEvent']/Value").InnerText;
                    ev.SourceGuid = currentForm.Guid;
                    break;
                case "OpenedFormViewControlEvent":
                    ev.SourceType = EventSourceType.Control;
                    ev.SourceGuid = new Guid(ruleEventNode.SelectSingleNode("Parts/Part[@Name='ViewControl']/Value").InnerText);
                    ev.Name = ruleEventNode.SelectSingleNode("Parts/Part[@Name='ControlEvent']/Value").InnerText;
                    viewId = ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Value").InnerText;
                    viewDisplayName = ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Display")?.InnerText;
                    ev.Properties.Set("ViewID", viewId, string.Empty, viewDisplayName);

                    if (subFormID != Guid.Empty)
                    {
                        formId = ruleEventNode.SelectSingleNode("Parts/Part[@Name='Form']/Value").InnerText;
                        formDisplayName = ruleEventNode.SelectSingleNode("Parts/Part[@Name='Form']/Display")?.InnerText;
                        ev.Properties.Set("FormID", formId, string.Empty, formDisplayName);
                    }
                    break;
                case "FormControlEvent":
                case "OpenedFormControlEvent":
                    ev.SourceType = EventSourceType.Control;
                    ev.SourceGuid = new Guid(ruleEventNode.SelectSingleNode("Parts/Part[@Name='FormControl']/Value").InnerText);
                    ev.Name = ruleEventNode.SelectSingleNode("Parts/Part[@Name='ControlEvent']/Value").InnerText;

                    if (subFormID != Guid.Empty)
                    {
                        formId = ruleEventNode.SelectSingleNode("Parts/Part[@Name='Form']/Value").InnerText;
                        formDisplayName = ruleEventNode.SelectSingleNode("Parts/Part[@Name='Form']/Display")?.InnerText;
                        ev.Properties.Set("FormID", formId, string.Empty, formDisplayName);
                    }
                    break;
                case "OpenedFormViewEvent":
                    ev.SourceType = EventSourceType.View;
                    ev.SourceGuid = new Guid(ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Value").InnerText);
                    ev.Name = ruleEventNode.SelectSingleNode("Parts/Part[@Name='ViewMethod']/Value").InnerText;
                    viewId = ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Value").InnerText;
                    viewDisplayName = ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Display")?.InnerText;
                    ev.Properties.Set("ViewID", viewId, viewDisplayName, viewDisplayName);

                    if (subFormID != Guid.Empty)
                    {
                        formId = ruleEventNode.SelectSingleNode("Parts/Part[@Name='Form']/Value").InnerText;
                        formDisplayName = ruleEventNode.SelectSingleNode("Parts/Part[@Name='Form']/Display")?.InnerText;
                        ev.Properties.Set("FormID", formId, string.Empty, formDisplayName);
                    }
                    break;
                case "OpenedFormCloseEvent":
                    ev.SourceType = EventSourceType.Form;
                    ev.SourceGuid = new Guid(ruleEventNode.SelectSingleNode("Parts/Part[@Name='Form']/Value").InnerText);
                    ev.Name = "Closed";

                    if (subFormID != Guid.Empty)
                    {
                        formId = ruleEventNode.SelectSingleNode("Parts/Part[@Name='Form']/Value").InnerText;
                        formDisplayName = ruleEventNode.SelectSingleNode("Parts/Part[@Name='Form']/Display")?.InnerText;
                        ev.Properties.Set("FormID", formId, string.Empty, formDisplayName);
                    }
                    break;
                case "OpenedFormEvent":
                    ev.SourceType = EventSourceType.Form;
                    ev.SourceGuid = new Guid(ruleEventNode.SelectSingleNode("Parts/Part[@Name='Form']/Value").InnerText);
                    ev.Name = ruleEventNode.SelectSingleNode("Parts/Part[@Name='FormEvent']/Value").InnerText;

                    if (subFormID != Guid.Empty)
                    {
                        formId = ruleEventNode.SelectSingleNode("Parts/Part[@Name='Form']/Value").InnerText;
                        formDisplayName = ruleEventNode.SelectSingleNode("Parts/Part[@Name='Form']/Display")?.InnerText;
                        ev.Properties.Set("FormID", formId, string.Empty, formDisplayName);
                    }
                    break;
                case "OpenedViewCloseEvent":
                    ev.SourceType = EventSourceType.View;
                    ev.SourceGuid = new Guid(ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Value").InnerText);
                    ev.Name = "Closed";

                    if (subFormID != Guid.Empty)
                    {
                        viewId = ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Value").InnerText;
                        viewDisplayName = ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Display")?.InnerText;
                        ev.Properties.Set("ViewID", viewId, viewDisplayName, viewDisplayName);
                    }
                    break;
                case "ViewWorkflowViewEvent":
                    ev.SourceGuid = new Guid(ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Value").InnerText);
                    ev.SourceType = EventSourceType.View;
                    ev.Name = "WorkflowSubmit";

                    break;
                case "FormWorkflowViewEvent":
                    ev.SourceGuid = currentForm.Guid;
                    ev.SourceType = EventSourceType.Form;
                    ev.Name = "WorkflowSubmit";

                    break;
                case "SubViewWorkflowViewEvent":
                    if (subFormID != Guid.Empty)
                    {
                        viewId = ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Value").InnerText;
                        viewDisplayName = ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Display")?.InnerText;
                        ev.Properties.Set("ViewID", viewId, viewDisplayName, viewDisplayName);
                    }

                    ev.SourceGuid = new Guid(ev.Properties["ViewID"]);
                    ev.SourceType = EventSourceType.View;
                    ev.Name = "WorkflowSubmit";
                    break;
                case "SubFormWorkflowViewEvent":
                    if (subFormID != Guid.Empty)
                    {
                        formId = ruleEventNode.SelectSingleNode("Parts/Part[@Name='Form']/Value").InnerText;
                        formDisplayName = ruleEventNode.SelectSingleNode("Parts/Part[@Name='Form']/Display")?.InnerText;
                        ev.Properties.Set("FormID", formId, string.Empty, formDisplayName);
                    }

                    ev.SourceGuid = new Guid(ev.Properties["FormID"]);
                    ev.SourceType = EventSourceType.Form;
                    ev.Name = "WorkflowSubmit";
                    break;
                case "SubFormViewWorkflowViewEvent":
                    if (subFormID != Guid.Empty)
                    {
                        formId = ruleEventNode.SelectSingleNode("Parts/Part[@Name='Form']/Value").InnerText;
                        formDisplayName = ruleEventNode.SelectSingleNode("Parts/Part[@Name='Form']/Display")?.InnerText;
                        ev.Properties.Set("FormID", formId, string.Empty, formDisplayName);
                    }

                    viewId = ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Value").InnerText;
                    viewDisplayName = ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Display")?.InnerText;
                    ev.Properties.Set("ViewID", viewId, viewDisplayName, viewDisplayName);

                    ev.SourceGuid = new Guid(ev.Properties["ViewID"]);
                    ev.SourceType = EventSourceType.View;
                    ev.Name = "WorkflowSubmit";
                    break;
                case "WorkflowActioned":
                    ev.SourceGuid = currentView.Guid;
                    ev.SourceType = EventSourceType.View;
                    ev.Name = "WorkflowActioned";
                    break;
                case "FormWorkflowActioned":
                    ev.SourceGuid = currentForm.Guid;
                    ev.SourceType = EventSourceType.Form;
                    ev.Name = "WorkflowActioned";
                    break;
                case "FormViewWorkflowActioned":
                    ev.SourceGuid = new Guid(ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Value").InnerText);
                    ev.SourceType = EventSourceType.View;
                    ev.Name = "WorkflowActioned";
                    break;
                case "SubViewWorkflowActioned":
                    if (subFormID != Guid.Empty)
                    {
                        viewId = ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Value").InnerText;
                        viewDisplayName = ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Display")?.InnerText;
                        ev.Properties.Set("ViewID", viewId, viewDisplayName, viewDisplayName);
                    }

                    ev.SourceGuid = new Guid(ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Value").InnerText);
                    ev.SourceType = EventSourceType.View;
                    ev.Name = "WorkflowActioned";
                    break;
                case "SubFormWorkflowActioned":
                    if (subFormID != Guid.Empty)
                    {
                        formId = ruleEventNode.SelectSingleNode("Parts/Part[@Name='Form']/Value").InnerText;
                        formDisplayName = ruleEventNode.SelectSingleNode("Parts/Part[@Name='Form']/Display")?.InnerText;
                        ev.Properties.Set("FormID", formId, string.Empty, formDisplayName);
                    }

                    ev.SourceGuid = new Guid(ruleEventNode.SelectSingleNode("Parts/Part[@Name='Form']/Value").InnerText);
                    ev.SourceType = EventSourceType.Form;
                    ev.Name = "WorkflowActioned";
                    break;
                case "SubFormViewWorkflowActioned":
                    if (subFormID != Guid.Empty)
                    {
                        formId = ruleEventNode.SelectSingleNode("Parts/Part[@Name='Form']/Value").InnerText;
                        formDisplayName = ruleEventNode.SelectSingleNode("Parts/Part[@Name='Form']/Display")?.InnerText;
                        ev.Properties.Set("FormID", formId, string.Empty, formDisplayName);
                    }

                    viewId = ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Value").InnerText;
                    viewDisplayName = ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Display")?.InnerText;
                    ev.Properties.Set("ViewID", viewId, viewDisplayName, viewDisplayName);
                    ev.SourceGuid = new Guid(ev.Properties["ViewID"]);
                    ev.SourceType = EventSourceType.View;
                    ev.Name = "WorkflowActioned";
                    break;
                case "ViewParameterEvent":
                case "SubViewParameterEvent":
                    ev.Name = ruleEventNode.SelectSingleNode("Parts/Part[@Name='ParameterEvent']/Value").InnerText;
                    ev.SourceType = EventSourceType.ViewParameter;
                    ev.SourceGuid = new Guid(ruleEventNode.SelectSingleNode("Parts/Part[@Name='ViewParameter']/Data/Item/@Guid").InnerText);
                    viewId = ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Value").InnerText;
                    viewDisplayName = ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Display")?.InnerText;
                    ev.Properties.Set("ViewID", viewId, viewDisplayName, viewDisplayName);
                    break;
                case "FormParameterEvent":
                case "SubFormParameterEvent":
                    ev.Name = ruleEventNode.SelectSingleNode("Parts/Part[@Name='ParameterEvent']/Value").InnerText;
                    ev.SourceType = EventSourceType.FormParameter;
                    ev.SourceGuid = new Guid(ruleEventNode.SelectSingleNode("Parts/Part[@Name='FormParameter']/Data/Item/@Guid").InnerText);

                    if (subFormID != Guid.Empty)
                    {
                        formId = ruleEventNode.SelectSingleNode("Parts/Part[@Name='Form']/Value").InnerText;
                        formDisplayName = ruleEventNode.SelectSingleNode("Parts/Part[@Name='Form']/Display")?.InnerText;
                        ev.Properties.Set("FormID", formId, string.Empty, formDisplayName);
                    }
                    break;
                case "SubFormViewParameterEvent":
                    ev.Name = ruleEventNode.SelectSingleNode("Parts/Part[@Name='ParameterEvent']/Value").InnerText;
                    ev.SourceType = EventSourceType.ViewParameter;
                    ev.SourceGuid = new Guid(ruleEventNode.SelectSingleNode("Parts/Part[@Name='ViewParameter']/Data/Item/@Guid").InnerText);
                    viewId = ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Value").InnerText;
                    viewDisplayName = ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Display")?.InnerText;
                    ev.Properties.Set("ViewID", viewId, viewDisplayName, viewDisplayName);

                    if (subFormID != Guid.Empty)
                    {
                        formId = ruleEventNode.SelectSingleNode("Parts/Part[@Name='Form']/Value").InnerText;
                        formDisplayName = ruleEventNode.SelectSingleNode("Parts/Part[@Name='Form']/Display")?.InnerText;
                        ev.Properties.Set("FormID", formId, string.Empty, formDisplayName);
                    }
                    break;
                case "FormViewParameterEvent":
                    ev.Name = ruleEventNode.SelectSingleNode("Parts/Part[@Name='ParameterEvent']/Value").InnerText;
                    ev.SourceType = EventSourceType.ViewParameter;
                    ev.SourceGuid = new Guid(ruleEventNode.SelectSingleNode("Parts/Part[@Name='ViewParameter']/Data/Item/@Guid").InnerText);
                    viewId = ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Value").InnerText;
                    viewDisplayName = ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Display")?.InnerText;
                    ev.Properties.Set("ViewID", viewId, viewDisplayName, viewDisplayName);
                    break;
                case "FormServerPreRenderEvent":
                    ev.Name = "ServerPreRender";
                    ev.SourceType = EventSourceType.Form;
                    ev.SourceGuid = currentForm.Guid;
                    break;
                case "ViewServerPreRenderEvent":
                    ev.SourceType = EventSourceType.View;
                    ev.Name = "ServerPreRender";
                    viewId = ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Value").InnerText;
                    viewDisplayName = ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Display")?.InnerText;
                    ev.Properties.Set("ViewID", viewId, viewDisplayName, viewDisplayName);
                    ev.SourceGuid = new Guid(viewId);
                    break;
                case "SubViewServerPreRenderEvent":
                    ev.SourceType = EventSourceType.View;
                    ev.Name = "ServerPreRender";
                    ev.SourceGuid = new Guid(ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Value").InnerText);
                    ev.Properties.Set("ViewID", ev.SourceGuid.ToString(), ev.SourceName, ev.SourceDisplayName);
                    break;
                case "SubFormServerPreRenderEvent":
                    ev.SourceType = EventSourceType.Form;
                    ev.Name = "ServerPreRender";
                    ev.SourceGuid = new Guid(ruleEventNode.SelectSingleNode("Parts/Part[@Name='Form']/Value").InnerText);

                    if (subFormID != Guid.Empty)
                    {
                        formId = ruleEventNode.SelectSingleNode("Parts/Part[@Name='Form']/Value").InnerText;
                        formDisplayName = ruleEventNode.SelectSingleNode("Parts/Part[@Name='Form']/Display")?.InnerText;
                        ev.Properties.Set("FormID", formId, string.Empty, formDisplayName);
                    }
                    break;
                case "SubFormViewServerPreRenderEvent":
                    ev.SourceType = EventSourceType.View;
                    ev.Name = "ServerPreRender";
                    ev.SourceGuid = new Guid(ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Value").InnerText);
                    viewId = ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Value").InnerText;
                    viewDisplayName = ruleEventNode.SelectSingleNode("Parts/Part[@Name='View']/Display")?.InnerText;
                    ev.Properties.Set("ViewID", viewId, viewDisplayName, viewDisplayName);

                    if (subFormID != Guid.Empty)
                    {
                        formId = ruleEventNode.SelectSingleNode("Parts/Part[@Name='Form']/Value").InnerText;
                        formDisplayName = ruleEventNode.SelectSingleNode("Parts/Part[@Name='Form']/Display")?.InnerText;
                        ev.Properties.Set("FormID", formId, string.Empty, formDisplayName);
                    }
                    break;
                default:
                    ev.SourceType = EventSourceType.Rule;

                    if (existingEvent != null)
                    {
                        ev.Name = existingEvent.Name;
                    }
                    else
                    {
                        ev.Name = Guid.NewGuid().ToString();
                    }

                    if (existingEvent != null && !existingEvent.SourceGuid.Equals(Guid.Empty) && existingEvent.IsReference)
                    {
                        ev.SourceGuid = existingEvent.SourceGuid;
                        ev.SubFormGuid = existingEvent.SubFormGuid;
                        ev.InstanceGuid = existingEvent.InstanceGuid;
                    }
                    else
                    {
                        if (currentForm != null)
                        {
                            ev.SourceGuid = currentForm.Guid;
                        }
                        else
                        {
                            ev.SourceGuid = currentView.Guid;
                            ev.Properties["ViewID"] = ev.SourceGuid.ToString();
                        }
                    }

                    break;
            }

            ev.EventType = EventType.User;

            StateCollection stateCollection = null;
            if (ev.Form == null)
            {
                stateCollection = ev.View.States;
            }
            else
            {
                stateCollection = ev.Form.States;
            }

            if (existingEvent != null && (!existingEvent.InstanceGuid.Equals(ev.InstanceGuid) || !existingEvent.SubFormGuid.Equals(ev.SubFormGuid)))
            {
                UpdateLocalEventReferences(stateCollection, ev.DefinitionGuid, ev.InstanceGuid, ev.SubFormGuid, ev.SubFormInstanceGuid);
            }

            return ev;
        }

        private void UpdateLocalEventReferences(StateCollection stateCollection, Guid eventDefinitionID, Guid eventInstanceID, Guid subformGuid, Guid subformInstanceGuid)
        {
            foreach (State state in stateCollection)
            {
                foreach (Event ev in state.Events)
                {
                    if (ev.EventType == EventType.User)
                    {
                        foreach (Handler handler in ev.Handlers)
                        {
                            foreach (Authoring.Eventing.Action action in handler.Actions)
                            {
                                string eventID = action.Properties["EventID"];
                                if (!string.IsNullOrEmpty(eventID) && string.Equals(eventID, eventDefinitionID.ToString(), StringComparison.OrdinalIgnoreCase))
                                {
                                    action.InstanceGuid = eventInstanceID;
                                    action.SubFormGuid = subformGuid;
                                    action.SubFormInstanceGuid = subformInstanceGuid;
                                }
                            }
                        }
                    }
                }
            }
        }

        private Handler CreateHandlerFromRuleXML(XmlNode handlerNode, Dictionary<Guid, WSA.Eventing.Handler> existingHandlersCollection, string context)
        {
            Handler handler = null;
            Guid handlerID = new Guid(handlerNode.Attributes["ID"].Value);
            bool handlerIsEnabled = handlerNode.Attributes["IsEnabled"] != null ? Boolean.Parse(handlerNode.Attributes["IsEnabled"].Value) : true;
            string location;
            if (existingHandlersCollection != null && existingHandlersCollection.ContainsKey(handlerID))
            {
                handler = existingHandlersCollection[handlerID].Clone<WSA.Eventing.Handler>();
                // Check if inheritance was lost
                if (!handler.IsEnabled.Equals(handlerIsEnabled))
                {
                    handler.IsInherited = false;
                }
                location = handler.Properties["location"];
                existingHandlersCollection.Remove(handlerID);
                handler.Actions.Clear();
                handler.Conditions.Clear();
                handler.Properties.Clear();
            }
            else
            {
                location = handlerNode.Attributes["Context"] != null ? handlerNode.Attributes["Context"].Value : context;
                handler = new Handler(handlerID);
                handler.DefinitionGuid = handlerNode.Attributes["DefinitionID"] != null ? new Guid(handlerNode.Attributes["DefinitionID"].Value) : Guid.NewGuid();
                handler.IsInherited = handlerNode.Attributes["IsInherited"] != null ? Boolean.Parse(handlerNode.Attributes["IsInherited"].Value) : false;
                handler.IsReference = handlerNode.Attributes["IsReference"] != null ? Boolean.Parse(handlerNode.Attributes["IsReference"].Value) : false;

            }

            handler.HandlerType = (HandlerType)Enum.Parse(typeof(HandlerType), handlerNode.Attributes["HandlerType"].Value, true);
            handler.IsEnabled = handlerIsEnabled;

            Guid instanceID = Guid.Empty;
            Guid subFormID = Guid.Empty;
            if (handlerNode.SelectSingleNode("Parts/Part/Data/Item[@SubFormID]") != null)
            {
                string handlerSubFormID = handlerNode.SelectSingleNode("Parts/Part/Data/Item[@SubFormID]").Attributes["SubFormID"].Value;
                subFormID = !string.IsNullOrEmpty(handlerSubFormID) ? new Guid(handlerSubFormID) : Guid.Empty;
            }

            if (handlerNode.SelectSingleNode("Parts/Part[@Name='View']") != null)
            {
                if (handlerNode.SelectSingleNode("Parts/Part[@Name='View']/Data/Item/InstanceID") != null && !string.IsNullOrEmpty(handlerNode.SelectSingleNode("Parts/Part[@Name='View']/Data/Item/InstanceID").InnerText))
                {
                    instanceID = new Guid(handlerNode.SelectSingleNode("Parts/Part[@Name='View']/Data/Item/InstanceID").InnerText);
                }
            }
            else
            {
                if (handlerNode.SelectSingleNode("Parts/Part[@Name='Form']/Data/Item/InstanceID") != null && !string.IsNullOrEmpty(handlerNode.SelectSingleNode("Parts/Part[@Name='Form']/Data/Item/InstanceID").InnerText))
                {
                    instanceID = new Guid(handlerNode.SelectSingleNode("Parts/Part[@Name='Form']/Data/Item/InstanceID").InnerText);
                }
            }

            if (instanceID == Guid.Empty)
            {
                if (handlerNode.Attributes["InstanceID"] != null)
                {
                    instanceID = new Guid(handlerNode.Attributes["InstanceID"].Value);
                }
                else
                {
                    instanceID = Guid.Empty;
                }
            }

            handler.Properties.Set("HandlerName", handlerNode.Attributes["Name"].Value);
            handler.Properties.Set("Location", location);

            if (handler.HandlerType == HandlerType.ForEach)
            {
                HandlerFunction handlerFunction = null;
                PropertyExpression viewId = null;
                PropertyExpression controlId = null;
                PropertyExpression itemState = null;

                string handlerName = handlerNode.Attributes["Name"].Value;
                handlerFunction = new HandlerFunction();
                handler.Function = handlerFunction;
                XmlNode handlerPartsNode = handlerNode.SelectSingleNode("Parts");
                XmlNode partNode = null;

                #region handler function parts

                partNode = handlerPartsNode.SelectSingleNode("Part[@Name='View']");

                viewId = new PropertyExpression(
                    PropertyExpressionSourceType.View,
                    ExpressionType.Guid,
                    partNode.SelectSingleNode("./Value").InnerText,                             // SourceID
                    partNode.SelectSingleNode("./Data/Item/Name|./Display").InnerText,          // SourceName
                    partNode.SelectSingleNode("./Data/Item/DisplayName|./Display").InnerText    // SourceDisplayName
                );

                partNode = handlerPartsNode.SelectSingleNode("Part[@Name='ItemStates']");

                itemState = new PropertyExpression(
                    PropertyExpressionSourceType.ItemState,
                    ExpressionType.Text,
                    partNode.SelectSingleNode("./Value").InnerText,                             // SourceID
                    partNode.SelectSingleNode("./Data/Item/Name|./Display").InnerText,          // SourceName
                    partNode.SelectSingleNode("./Data/Item/DisplayName|./Display").InnerText    // SourceDisplayName
                );

                switch (handlerName)
                {
                    case "ForEachListViewRowHandler":
                    case "SubViewForEachListViewRowHandler":
                    case "SubFormViewForEachListViewRowHandler":
                        handler.Function.Name = "ViewItemsCollection";

                        handlerFunction.Parameters.Add(viewId);
                        handlerFunction.Parameters.Add(itemState);

                        if (subFormID != Guid.Empty)
                        {
                            handlerFunction.SubFormGuid = subFormID;
                        }
                        if (instanceID != Guid.Empty)
                        {
                            handlerFunction.InstanceGuid = instanceID;
                        }
                        break;
                    case "ForEachListControlItemHandler":
                    case "ForEachListControlOnViewItemHandler":
                    case "SubViewForEachListControlItemHandler":
                    case "SubFormViewForEachListControlItemHandler":
                        handlerFunction.Name = "ControlItemsCollection";

                        partNode = handlerPartsNode.SelectSingleNode("Part[@Name='ViewControl']");

                        controlId = new PropertyExpression(
                            PropertyExpressionSourceType.Control,
                            ExpressionType.Guid,
                            partNode.SelectSingleNode("./Value").InnerText,                             // SourceID
                            partNode.SelectSingleNode("./Data/Item/Name|./Display").InnerText,          // SourceName
                            partNode.SelectSingleNode("./Data/Item/DisplayName|./Display").InnerText    // SourceDisplayName
                        );

                        handlerFunction.Parameters.Add(viewId);
                        handlerFunction.Parameters.Add(controlId);
                        handlerFunction.Parameters.Add(itemState);

                        if (subFormID != Guid.Empty)
                        {
                            handlerFunction.SubFormGuid = subFormID;
                        }
                        if (instanceID != Guid.Empty)
                        {
                            handlerFunction.InstanceGuid = instanceID;
                        }
                        break;
                }
                #endregion
            }

            return handler;
        }

        private void CreateConditionsFromRuleXML(XmlNode ruleNode, Event ev, string context, Authoring.Form currentForm, Handler handler, XmlNodeList ruleConditionsNodes, Dictionary<Guid, WSA.Eventing.Condition> existingConditions, Dictionary<Guid, WSA.Eventing.Handler> existingHandlersCollection)
        {
            for (var c = 0; c < ruleConditionsNodes.Count; c++)
            {
                XmlNode conditionElem = ruleConditionsNodes[c];
                string conditionName = conditionElem.Attributes["Name"].Value;
                Authoring.Filters.LogicalExpression lp;
                string leftValue = string.Empty;
                string rightValue = string.Empty;
                string itemDataType = string.Empty;
                string conditionValue = string.Empty;
                PropertyExpression left = null;
                string leftDisplay = string.Empty;
                ValueExpression right = null;
                Equals equals = null;
                IsNotBlank notBlank = null;
                NotEquals notEquals = null;
                IsBlank isBlank = null;
                Guid conditionGuid;
                Guid conditionDefinitionGuid;
                string location = conditionElem.Attributes["Context"] != null ? conditionElem.Attributes["Context"].Value : context;
                Guid subFormID = conditionElem.SelectSingleNode("Parts/Part/Data/Item[@SubFormID]") != null ? new Guid(conditionElem.SelectSingleNode("Parts/Part/Data/Item[@SubFormID]").Attributes["SubFormID"].Value) : Guid.Empty;
                Guid instanceID = Guid.Empty;

                if (conditionElem.SelectSingleNode("Parts/Part[@Name='View']") != null)
                {
                    if (conditionElem.SelectSingleNode("Parts/Part[@Name='View']/Data/Item/InstanceID") != null && !string.IsNullOrEmpty(conditionElem.SelectSingleNode("Parts/Part[@Name='View']/Data/Item/InstanceID").InnerText))
                    {
                        instanceID = new Guid(conditionElem.SelectSingleNode("Parts/Part[@Name='View']/Data/Item/InstanceID").InnerText);
                    }
                }
                else
                {
                    if (conditionElem.SelectSingleNode("Parts/Part[@Name='Form']/Data/Item/InstanceID") != null && !string.IsNullOrEmpty(conditionElem.SelectSingleNode("Parts/Part[@Name='Form']/Data/Item/InstanceID").InnerText))
                    {
                        instanceID = new Guid(conditionElem.SelectSingleNode("Parts/Part[@Name='Form']/Data/Item/InstanceID").InnerText);
                    }
                }

                if (instanceID == Guid.Empty)
                {
                    instanceID = conditionElem.Attributes["InstanceID"] != null ? new Guid(conditionElem.Attributes["InstanceID"].Value) : Guid.Empty;
                }

                if (conditionElem.Attributes["ID"] != null)
                {
                    conditionGuid = new Guid(conditionElem.Attributes["ID"].Value);
                }
                else { conditionGuid = Guid.NewGuid(); }

                if (conditionElem.Attributes["DefinitionID"] != null)
                {
                    conditionDefinitionGuid = new Guid(conditionElem.Attributes["DefinitionID"].Value);
                }
                else { conditionDefinitionGuid = Guid.NewGuid(); }

                Condition condition = new Condition(conditionGuid);
                condition.DefinitionGuid = conditionDefinitionGuid;
                condition.IsEnabled = Boolean.Parse(conditionElem.Attributes["Enabled"].Value);
                condition.IsReference = !(Boolean.Parse(conditionElem.Attributes["IsCurrentHandler"].Value));
                if (instanceID != Guid.Empty) { condition.InstanceGuid = instanceID; }
                condition.Properties.Set("Location", location);
                condition.Properties.Set("Name", conditionName);

                // Check if inheritance was lost
                if (existingConditions != null)
                {
                    if (existingConditions.ContainsKey(conditionGuid))
                    {
                        if (condition.IsReference && existingConditions[conditionGuid].IsInherited)
                        {
                            condition.IsInherited = true;
                        }
                        if (!condition.IsEnabled.Equals(existingConditions[conditionGuid].IsEnabled) && condition.IsReference)
                        {
                            condition.IsInherited = false;
                        }
                    }
                }

                if (conditionElem.SelectSingleNode("Comments") != null)
                {
                    string commentsValue = conditionElem.SelectSingleNode("Comments").InnerText;
                    if (string.IsNullOrEmpty(commentsValue) && condition.Properties["Comments"] != null)
                    {
                        condition.Properties.Remove("Comments");
                    }
                    else
                    {
                        condition.Properties.Set("Comments", commentsValue);
                    }
                }

                switch (conditionName)
                {
                    case "ServerAdvancedCondition":
                    case "AdvancedCondition":
                        XmlNode conditionValueNode = conditionElem.SelectSingleNode("Parts/Part/Value");

                        XmlDocument xmlDoc = XmlHelper.CreateXmlDocument(conditionValueNode.InnerText);

                        XmlNode valueNode = xmlDoc.SelectSingleNode("Conditions");

                        lp = (Authoring.Filters.LogicalExpression)Authoring.Filters.Expression.FromXml(valueNode.InnerXml);

                        condition.Expressions.Add(lp);
                        handler.Conditions.Add(condition);
                        break;
                    case "SimpleEqualControlCondition":
                    case "SubViewSimpleEqualControlCondition":
                    case "SubFormSimpleEqualControlCondition":
                        leftValue = conditionElem.SelectSingleNode("Parts/Part[@Name='ViewControl']/Value").InnerText;
                        leftDisplay = conditionElem.SelectSingleNode("Parts/Part[@Name='ViewControl']/Display").InnerText;
                        rightValue = conditionElem.SelectSingleNode("Parts/Part[@Name='ValueInput']/Value").InnerText;

                        left = new PropertyExpression(
                            PropertyExpressionSourceType.Control, ExpressionType.Text, leftValue, string.Empty, leftDisplay);

                        right = new ValueExpression(rightValue);

                        equals = new Equals(left, right);

                        condition.Expressions.Add(equals);

                        if (subFormID != Guid.Empty)
                        {
                            condition.SubFormGuid = subFormID;
                            left.SourceSubFormGuid = subFormID;
                        }
                        if (instanceID != Guid.Empty)
                        {
                            left.SourceInstanceGuid = instanceID;
                        }

                        handler.Conditions.Add(condition);
                        break;
                    case "SimpleEqualFormControlCondition":
                    case "SubFormSimpleEqualFormControlCondition":
                        leftValue = conditionElem.SelectSingleNode("Parts/Part[@Name='FormControl']/Value").InnerText;
                        leftDisplay = conditionElem.SelectSingleNode("Parts/Part[@Name='FormControl']/Display").InnerText;
                        rightValue = conditionElem.SelectSingleNode("Parts/Part[@Name='ValueInput']/Value").InnerText;

                        left = new PropertyExpression(
                            PropertyExpressionSourceType.Control, ExpressionType.Text, leftValue, string.Empty, leftDisplay);

                        right = new ValueExpression(rightValue);

                        equals = new Equals(left, right);

                        condition.Expressions.Add(equals);

                        if (subFormID != Guid.Empty)
                        {
                            condition.SubFormGuid = subFormID;
                            left.SourceSubFormGuid = subFormID;
                        }

                        handler.Conditions.Add(condition);
                        break;
                    case "SubFormSimpleNotBlankControlCondition":
                    case "SubViewSimpleNotBlankControlCondition":
                    case "SimpleNotBlankControlCondition":
                        leftValue = conditionElem.SelectSingleNode("Parts/Part[@Name='ViewControl']/Value").InnerText;
                        leftDisplay = conditionElem.SelectSingleNode("Parts/Part[@Name='ViewControl']/Display").InnerText;

                        left = new PropertyExpression(
                            PropertyExpressionSourceType.Control, ExpressionType.Text, leftValue, string.Empty, leftDisplay);

                        notBlank = new IsNotBlank(left);

                        condition.Expressions.Add(notBlank);

                        if (subFormID != Guid.Empty)
                        {
                            condition.SubFormGuid = subFormID;
                            left.SourceSubFormGuid = subFormID;
                        }
                        if (instanceID != Guid.Empty)
                        {
                            left.SourceInstanceGuid = instanceID;
                        }

                        handler.Conditions.Add(condition);
                        break;
                    case "SubFormSimpleNotBlankFormControlCondition":
                    case "SimpleNotBlankFormControlCondition":
                        leftValue = conditionElem.SelectSingleNode("Parts/Part[@Name='FormControl']/Value").InnerText;
                        leftDisplay = conditionElem.SelectSingleNode("Parts/Part[@Name='FormControl']/Display").InnerText;

                        left = new PropertyExpression(
                            PropertyExpressionSourceType.Control, ExpressionType.Text, leftValue, string.Empty, leftDisplay);

                        notBlank = new IsNotBlank(left);

                        condition.Expressions.Add(notBlank);

                        if (subFormID != Guid.Empty)
                        {
                            condition.SubFormGuid = subFormID;
                            left.SourceSubFormGuid = subFormID;
                        }

                        handler.Conditions.Add(condition);
                        break;
                    case "SimpleNotEqualControlCondition":
                    case "SubViewSimpleNotEqualControlCondition":
                    case "SubFormSimpleNotEqualControlCondition":
                        leftValue = conditionElem.SelectSingleNode("Parts/Part[@Name='ViewControl']/Value").InnerText;
                        rightValue = conditionElem.SelectSingleNode("Parts/Part[@Name='ValueInput']/Value").InnerText;
                        leftDisplay = conditionElem.SelectSingleNode("Parts/Part[@Name='ViewControl']/Display").InnerText;

                        left = new PropertyExpression(
                            PropertyExpressionSourceType.Control, ExpressionType.Text, leftValue, string.Empty, leftDisplay);

                        right = new ValueExpression(rightValue);

                        notEquals = new NotEquals(left, right);

                        condition.Expressions.Add(notEquals);

                        if (subFormID != Guid.Empty)
                        {
                            condition.SubFormGuid = subFormID;
                            left.SourceSubFormGuid = subFormID;
                        }
                        if (instanceID != Guid.Empty)
                        {
                            left.SourceInstanceGuid = instanceID;
                        }

                        handler.Conditions.Add(condition);
                        break;
                    case "SimpleNotEqualFormControlCondition":
                    case "SubFormSimpleNotEqualFormControlCondition":
                        leftValue = conditionElem.SelectSingleNode("Parts/Part[@Name='FormControl']/Value").InnerText;
                        rightValue = conditionElem.SelectSingleNode("Parts/Part[@Name='ValueInput']/Value").InnerText;
                        leftDisplay = conditionElem.SelectSingleNode("Parts/Part[@Name='FormControl']/Display").InnerText;

                        left = new PropertyExpression(
                            PropertyExpressionSourceType.Control, ExpressionType.Text, leftValue, string.Empty, leftDisplay);

                        right = new ValueExpression(rightValue);

                        notEquals = new NotEquals(left, right);

                        condition.Expressions.Add(notEquals);

                        if (subFormID != Guid.Empty)
                        {
                            condition.SubFormGuid = subFormID;
                            left.SourceSubFormGuid = subFormID;
                        }

                        handler.Conditions.Add(condition);
                        break;
                    case "SubFormSimpleBlankControlCondition":
                    case "SubViewSimpleBlankControlCondition":
                    case "SimpleBlankControlCondition":
                        leftValue = conditionElem.SelectSingleNode("Parts/Part[@Name='ViewControl']/Value").InnerText;
                        leftDisplay = conditionElem.SelectSingleNode("Parts/Part[@Name='ViewControl']/Display").InnerText;

                        left = new PropertyExpression(
                            PropertyExpressionSourceType.Control, ExpressionType.Text, leftValue, string.Empty, leftDisplay);

                        isBlank = new IsBlank(left);

                        condition.Expressions.Add(isBlank);

                        if (subFormID != Guid.Empty)
                        {
                            condition.SubFormGuid = subFormID;
                            left.SourceSubFormGuid = subFormID;
                        }
                        if (instanceID != Guid.Empty)
                        {
                            left.SourceInstanceGuid = instanceID;
                        }

                        handler.Conditions.Add(condition);
                        break;
                    case "SubFormSimpleBlankFormControlCondition":
                    case "SimpleBlankFormControlCondition":
                        leftValue = conditionElem.SelectSingleNode("Parts/Part[@Name='FormControl']/Value").InnerText;
                        leftDisplay = conditionElem.SelectSingleNode("Parts/Part[@Name='FormControl']/Display").InnerText;

                        left = new PropertyExpression(
                            PropertyExpressionSourceType.Control, ExpressionType.Text, leftValue, string.Empty, leftDisplay);

                        isBlank = new IsBlank(left);

                        condition.Expressions.Add(isBlank);

                        if (subFormID != Guid.Empty)
                        {
                            condition.SubFormGuid = subFormID;
                            left.SourceSubFormGuid = subFormID;
                        }

                        handler.Conditions.Add(condition);
                        break;
                    case "SimpleBlankViewParameterCondition":
                    case "SubViewSimpleBlankViewParameterCondition":
                        leftValue = conditionElem.SelectSingleNode("Parts/Part[@Name='ViewParameter']/Value").InnerText;
                        leftDisplay = conditionElem.SelectSingleNode("Parts/Part[@Name='ViewParameter']/Display").InnerText;

                        left = new PropertyExpression(
                            PropertyExpressionSourceType.ViewParameter, ExpressionType.Text, leftValue, string.Empty, leftDisplay);

                        isBlank = new IsBlank(left);

                        condition.Expressions.Add(isBlank);

                        if (subFormID != Guid.Empty)
                        {
                            condition.SubFormGuid = subFormID;
                            left.SourceSubFormGuid = subFormID;
                        }

                        if (instanceID != Guid.Empty)
                        {
                            left.SourceInstanceGuid = instanceID;
                        }

                        handler.Conditions.Add(condition);
                        break;
                    case "SimpleNotBlankViewParameterCondition":
                    case "SubViewSimpleNotBlankViewParameterCondition":
                        leftValue = conditionElem.SelectSingleNode("Parts/Part[@Name='ViewParameter']/Value").InnerText;
                        leftDisplay = conditionElem.SelectSingleNode("Parts/Part[@Name='ViewParameter']/Display").InnerText;

                        left = new PropertyExpression(
                            PropertyExpressionSourceType.ViewParameter, ExpressionType.Text, leftValue, string.Empty, leftDisplay);

                        notBlank = new IsNotBlank(left);

                        condition.Expressions.Add(notBlank);

                        if (subFormID != Guid.Empty)
                        {
                            condition.SubFormGuid = subFormID;
                            left.SourceSubFormGuid = subFormID;
                        }

                        if (instanceID != Guid.Empty)
                        {
                            left.SourceInstanceGuid = instanceID;
                        }

                        handler.Conditions.Add(condition);
                        break;
                    case "SimpleEqualViewParameterCondition":
                    case "SubViewSimpleEqualViewParameterCondition":
                        leftValue = conditionElem.SelectSingleNode("Parts/Part[@Name='ViewParameter']/Value").InnerText;
                        rightValue = conditionElem.SelectSingleNode("Parts/Part[@Name='ValueInput']/Value").InnerText;
                        itemDataType = conditionElem.SelectSingleNode("Parts/Part[@Name='ViewParameter']/Data/Item/@SubType").InnerText;
                        leftDisplay = conditionElem.SelectSingleNode("Parts/Part[@Name='ViewParameter']/Display").InnerText;

                        left = new PropertyExpression(
                            PropertyExpressionSourceType.ViewParameter, (ExpressionType)Enum.Parse(typeof(ExpressionType), itemDataType, true), leftValue, string.Empty, leftDisplay);

                        right = new ValueExpression(rightValue);

                        equals = new Equals(left, right);

                        condition.Expressions.Add(equals);

                        if (subFormID != Guid.Empty)
                        {
                            condition.SubFormGuid = subFormID;
                            left.SourceSubFormGuid = subFormID;
                        }

                        if (instanceID != Guid.Empty)
                        {
                            left.SourceInstanceGuid = instanceID;
                        }

                        handler.Conditions.Add(condition);
                        break;
                    case "SimpleNotEqualViewParameterCondition":
                    case "SubViewSimpleNotEqualViewParameterCondition":
                        leftValue = conditionElem.SelectSingleNode("Parts/Part[@Name='ViewParameter']/Value").InnerText;
                        rightValue = conditionElem.SelectSingleNode("Parts/Part[@Name='ValueInput']/Value").InnerText;
                        itemDataType = conditionElem.SelectSingleNode("Parts/Part[@Name='ViewParameter']/Data/Item/@SubType").InnerText;
                        leftDisplay = conditionElem.SelectSingleNode("Parts/Part[@Name='ViewParameter']/Display").InnerText;

                        left = new PropertyExpression(
                            PropertyExpressionSourceType.ViewParameter, (ExpressionType)Enum.Parse(typeof(ExpressionType), itemDataType, true), leftValue, string.Empty, leftDisplay);

                        right = new ValueExpression(rightValue);

                        notEquals = new NotEquals(left, right);

                        condition.Expressions.Add(notEquals);

                        if (subFormID != Guid.Empty)
                        {
                            condition.SubFormGuid = subFormID;
                            left.SourceSubFormGuid = subFormID;
                        }

                        if (instanceID != Guid.Empty)
                        {
                            left.SourceInstanceGuid = instanceID;
                        }

                        handler.Conditions.Add(condition);
                        break;
                    case "SimpleBlankFormParameterCondition":
                    case "SubFormSimpleBlankFormParameterCondition":
                        leftValue = conditionElem.SelectSingleNode("Parts/Part[@Name='FormParameter']/Value").InnerText;
                        leftDisplay = conditionElem.SelectSingleNode("Parts/Part[@Name='FormParameter']/Display").InnerText;

                        left = new PropertyExpression(
                            PropertyExpressionSourceType.FormParameter, ExpressionType.Text, leftValue, string.Empty, leftDisplay);

                        isBlank = new IsBlank(left);

                        condition.Expressions.Add(isBlank);

                        if (subFormID != Guid.Empty)
                        {
                            condition.SubFormGuid = subFormID;
                            left.SourceSubFormGuid = subFormID;
                        }

                        handler.Conditions.Add(condition);
                        break;
                    case "SimpleNotBlankFormParameterCondition":
                    case "SubFormSimpleNotBlankFormParameterCondition":
                        leftValue = conditionElem.SelectSingleNode("Parts/Part[@Name='FormParameter']/Value").InnerText;
                        leftDisplay = conditionElem.SelectSingleNode("Parts/Part[@Name='FormParameter']/Display").InnerText;

                        left = new PropertyExpression(
                            PropertyExpressionSourceType.FormParameter, ExpressionType.Text, leftValue, string.Empty, leftDisplay);

                        notBlank = new IsNotBlank(left);

                        condition.Expressions.Add(notBlank);

                        if (subFormID != Guid.Empty)
                        {
                            condition.SubFormGuid = subFormID;
                            left.SourceSubFormGuid = subFormID;
                        }

                        handler.Conditions.Add(condition);
                        break;
                    case "SimpleEqualFormParameterCondition":
                    case "SubFormSimpleEqualFormParameterCondition":
                        leftValue = conditionElem.SelectSingleNode("Parts/Part[@Name='FormParameter']/Value").InnerText;
                        rightValue = conditionElem.SelectSingleNode("Parts/Part[@Name='ValueInput']/Value").InnerText;
                        itemDataType = conditionElem.SelectSingleNode("Parts/Part[@Name='FormParameter']/Data/Item/@SubType").InnerText;
                        leftDisplay = conditionElem.SelectSingleNode("Parts/Part[@Name='FormParameter']/Display").InnerText;

                        left = new PropertyExpression(
                            PropertyExpressionSourceType.FormParameter, (ExpressionType)Enum.Parse(typeof(ExpressionType), itemDataType, true), leftValue, string.Empty, leftDisplay);

                        right = new ValueExpression(rightValue);

                        equals = new Equals(left, right);

                        condition.Expressions.Add(equals);

                        if (subFormID != Guid.Empty)
                        {
                            condition.SubFormGuid = subFormID;
                            left.SourceSubFormGuid = subFormID;
                        }

                        handler.Conditions.Add(condition);
                        break;
                    case "SimpleNotEqualFormParameterCondition":
                    case "SubFormSimpleNotEqualFormParameterCondition":
                        leftValue = conditionElem.SelectSingleNode("Parts/Part[@Name='FormParameter']/Value").InnerText;
                        rightValue = conditionElem.SelectSingleNode("Parts/Part[@Name='ValueInput']/Value").InnerText;
                        itemDataType = conditionElem.SelectSingleNode("Parts/Part[@Name='FormParameter']/Data/Item/@SubType").InnerText;
                        leftDisplay = conditionElem.SelectSingleNode("Parts/Part[@Name='FormParameter']/Display").InnerText;

                        left = new PropertyExpression(
                            PropertyExpressionSourceType.FormParameter, (ExpressionType)Enum.Parse(typeof(ExpressionType), itemDataType, true), leftValue, string.Empty, leftDisplay);

                        right = new ValueExpression(rightValue);

                        notEquals = new NotEquals(left, right);

                        condition.Expressions.Add(notEquals);

                        if (subFormID != Guid.Empty)
                        {
                            condition.SubFormGuid = subFormID;
                            left.SourceSubFormGuid = subFormID;
                        }

                        handler.Conditions.Add(condition);
                        break;
                    case "SubFormViewSimpleBlankViewParameterCondition":
                        leftValue = conditionElem.SelectSingleNode("Parts/Part[@Name='ViewParameter']/Value").InnerText;
                        leftDisplay = conditionElem.SelectSingleNode("Parts/Part[@Name='ViewParameter']/Display").InnerText;

                        left = new PropertyExpression(
                            PropertyExpressionSourceType.ViewParameter, ExpressionType.Text, leftValue, string.Empty, leftDisplay);

                        isBlank = new IsBlank(left);

                        condition.Expressions.Add(isBlank);

                        if (subFormID != Guid.Empty)
                        {
                            condition.SubFormGuid = subFormID;
                            left.SourceSubFormGuid = subFormID;
                        }
                        if (instanceID != Guid.Empty)
                        {
                            left.SourceInstanceGuid = instanceID;
                        }

                        handler.Conditions.Add(condition);
                        break;

                    case "SubFormViewSimpleNotBlankViewParameterCondition":
                        leftValue = conditionElem.SelectSingleNode("Parts/Part[@Name='ViewParameter']/Value").InnerText;
                        leftDisplay = conditionElem.SelectSingleNode("Parts/Part[@Name='ViewParameter']/Display").InnerText;

                        left = new PropertyExpression(
                            PropertyExpressionSourceType.ViewParameter, ExpressionType.Text, leftValue, string.Empty, leftDisplay);

                        notBlank = new IsNotBlank(left);

                        condition.Expressions.Add(notBlank);

                        if (subFormID != Guid.Empty)
                        {
                            condition.SubFormGuid = subFormID;
                            left.SourceSubFormGuid = subFormID;
                        }
                        if (instanceID != Guid.Empty)
                        {
                            left.SourceInstanceGuid = instanceID;
                        }

                        handler.Conditions.Add(condition);
                        break;
                    case "SubFormViewSimpleEqualViewParameterCondition":
                        leftValue = conditionElem.SelectSingleNode("Parts/Part[@Name='ViewParameter']/Value").InnerText;
                        rightValue = conditionElem.SelectSingleNode("Parts/Part[@Name='ValueInput']/Value").InnerText;
                        itemDataType = conditionElem.SelectSingleNode("Parts/Part[@Name='ViewParameter']/Data/Item/@SubType").InnerText;
                        leftDisplay = conditionElem.SelectSingleNode("Parts/Part[@Name='ViewParameter']/Display").InnerText;

                        left = new PropertyExpression(
                            PropertyExpressionSourceType.ViewParameter, (ExpressionType)Enum.Parse(typeof(ExpressionType), itemDataType, true), leftValue, string.Empty, leftDisplay);

                        right = new ValueExpression(rightValue);

                        equals = new Equals(left, right);

                        condition.Expressions.Add(equals);

                        if (subFormID != Guid.Empty)
                        {
                            condition.SubFormGuid = subFormID;
                            left.SourceSubFormGuid = subFormID;
                        }

                        if (instanceID != Guid.Empty)
                        {
                            left.SourceInstanceGuid = instanceID;
                        }

                        handler.Conditions.Add(condition);
                        break;
                    case "SubFormViewSimpleNotEqualViewParameterCondition":
                        leftValue = conditionElem.SelectSingleNode("Parts/Part[@Name='ViewParameter']/Value").InnerText;
                        rightValue = conditionElem.SelectSingleNode("Parts/Part[@Name='ValueInput']/Value").InnerText;
                        itemDataType = conditionElem.SelectSingleNode("Parts/Part[@Name='ViewParameter']/Data/Item/@SubType").InnerText;
                        leftDisplay = conditionElem.SelectSingleNode("Parts/Part[@Name='ViewParameter']/Display").InnerText;

                        left = new PropertyExpression(
                            PropertyExpressionSourceType.ViewParameter, (ExpressionType)Enum.Parse(typeof(ExpressionType), itemDataType, true), leftValue, string.Empty, leftDisplay);

                        right = new ValueExpression(rightValue);

                        notEquals = new NotEquals(left, right);

                        condition.Expressions.Add(notEquals);

                        if (subFormID != Guid.Empty)
                        {
                            condition.SubFormGuid = subFormID;
                            left.SourceSubFormGuid = subFormID;
                        }

                        if (instanceID != Guid.Empty)
                        {
                            left.SourceInstanceGuid = instanceID;
                        }

                        handler.Conditions.Add(condition);
                        break;
                    case "FormValidateCondition":
                        conditionValue = conditionElem.SelectSingleNode("Parts/Part[@Name='ConfigureCondition']/Value").InnerText;
                        if (!String.IsNullOrEmpty(conditionValue))
                        {
                            XmlDocument doc = XmlHelper.CreateXmlDocument(conditionValue);
                            ValidationGroup vg = null;

                            XmlNodeList nodes = doc.SelectNodes("ValidationGroups/ValidationGroup/Source");

                            Guid validationGroupID = new Guid(doc.SelectSingleNode("ValidationGroups/ValidationGroup").Attributes["ID"].Value);

                            if (context == "View" || context == "Control")
                            {
                                if (ev.View.ValidationGroups.Contains(validationGroupID))
                                {
                                    vg = ev.View.ValidationGroups[validationGroupID];
                                    vg.Controls.Clear();
                                }
                                else
                                {
                                    if (!condition.IsReference)
                                    {
                                        vg = new ValidationGroup();
                                        vg.Name = "ValidationGroupForEvent";
                                        validationGroupID = vg.Guid;

                                        ev.View.ValidationGroups.Add(vg);
                                    }
                                }
                            }
                            else
                            {
                                if (currentForm.ValidationGroups.Contains(validationGroupID))
                                {
                                    vg = currentForm.ValidationGroups[validationGroupID];

                                    if (!condition.IsReference)
                                    {
                                        vg.Controls.Clear();
                                    }
                                }
                                else
                                {
                                    if (!condition.IsReference)
                                    {
                                        vg = new ValidationGroup();
                                        vg.Name = "ValidationGroupForEvent";
                                        validationGroupID = vg.Guid;

                                        currentForm.ValidationGroups.Add(vg);
                                    }
                                }
                            }

                            XmlNode validationGroup = doc.SelectSingleNode("ValidationGroups/ValidationGroup");
                            string type = validationGroup.Attributes["Type"].Value;
                            string ignoreHiddenControls = validationGroup.Attributes["IgnoreInvisibleControls"].Value;
                            string ignoreDisabledControls = validationGroup.Attributes["IgnoreDisabledControls"].Value;
                            string ignoreReadOnlyControls = validationGroup.Attributes["IgnoreReadOnlyControls"].Value;

                            if (!condition.IsReference && vg != null)
                            {
                                Guid controlGuid = Guid.Empty;
                                bool isRequired = false;

                                foreach (XmlNode node in nodes)
                                {
                                    ValidationGroupControl vgc = new ValidationGroupControl();
                                    controlGuid = new Guid(node.Attributes["ID"].Value);
                                    isRequired = node.Attributes["IsRequired"].Value.ToLowerInvariant() == "true" ? true : false;
                                    vgc.ControlGuid = controlGuid;
                                    vgc.IsRequired = isRequired;
                                    vgc.InstanceGuid = node.Attributes["InstanceID"] != null ? new Guid(node.Attributes["InstanceID"].Value) : Guid.Empty;

                                    vg.Controls.Add(vgc);
                                }
                            }

                            Authoring.Eventing.Action li = new Authoring.Eventing.Action();
                            li.ActionType = Authoring.Eventing.ActionType.Validate;
                            li.Properties.Set("Location", location);
                            li.Properties.Set("MessageLocation", type);
                            li.Properties.Set("GroupID", validationGroupID.ToString());
                            li.Properties.Set("IgnoreInvisibleControls", ignoreHiddenControls);
                            li.Properties.Set("IgnoreDisabledControls", ignoreDisabledControls);
                            li.Properties.Set("IgnoreReadOnlyControls", ignoreReadOnlyControls);
                            li.IsReference = !(Boolean.Parse(conditionElem.Attributes["IsCurrentHandler"].Value));
                            li.DefinitionGuid = conditionDefinitionGuid;
                            li.IsEnabled = conditionElem.Attributes["Enabled"] != null ? Boolean.Parse(conditionElem.Attributes["Enabled"].Value) : true;
                            li.IsInherited = condition.IsInherited;

                            if (conditionElem.SelectSingleNode("Comments") != null)
                            {
                                string commentsValue = conditionElem.SelectSingleNode("Comments").InnerText;
                                li.Properties.Set("Comments", commentsValue);
                            }

                            if (instanceID != Guid.Empty) { li.InstanceGuid = instanceID; }

                            if (GetEvent(handler).SubFormGuid != Guid.Empty)
                            {
                                li.SubFormGuid = GetEvent(handler).SubFormGuid;
                            }

                            handler.Actions.Add(li);
                        }

                        break;
                    case "ViewIsCurrentActivityContextCondition":
                    case "SubViewIsCurrentActivityContextCondition":
                    case "SubFormIsCurrentActivityContextCondition":
                    case "SubFormViewIsCurrentActivityContextCondition":
                    case "FormViewIsCurrentActivityContextCondition":
                    case "FormIsCurrentActivityContextCondition":
                    case "ServerViewIsCurrentActivityContextCondition":
                    case "ServerSubViewIsCurrentActivityContextCondition":
                    case "ServerSubFormIsCurrentActivityContextCondition":
                    case "ServerSubFormViewIsCurrentActivityContextCondition":
                    case "ServerFormViewIsCurrentActivityContextCondition":
                    case "ServerFormIsCurrentActivityContextCondition":
                        left = new PropertyExpression(PropertyExpressionSourceType.SystemVariable, "CurrentWorkflowActivityName", ExpressionType.Text);
                        if (subFormID != Guid.Empty)
                        {
                            condition.SubFormGuid = subFormID;
                            left.SourceSubFormGuid = subFormID;
                        }
                        if (instanceID != Guid.Empty)
                        {
                            left.SourceInstanceGuid = instanceID;
                        }

                        CreatePropertiesFromMappingsXml(conditionElem.SelectSingleNode("Parts/Part[@Name='ConfigureActivity']"), condition);

                        rightValue = conditionElem.SelectSingleNode("Parts/Part[@Name='Activity']/Value").InnerText;

                        PropertyExpression rightPropExp = new PropertyExpression(
                            PropertyExpressionSourceType.WorkflowActivity,
                            rightValue,
                            ExpressionType.Text
                        );
                        rightPropExp.SourceName = rightValue;
                        rightPropExp.SourceDisplayName = conditionElem.SelectSingleNode("Parts/Part[@Name='Activity']/Display").InnerText;

                        condition.Expressions.Add(new Equals(left, rightPropExp));
                        handler.Conditions.Add(condition);

                        if (conditionName.Equals("ServerViewIsCurrentActivityContextCondition") || conditionName.Equals("ServerSubViewIsCurrentActivityContextCondition")
                            || conditionName.Equals("ServerSubFormIsCurrentActivityContextCondition") || conditionName.Equals("ServerSubFormViewIsCurrentActivityContextCondition")
                            || conditionName.Equals("ServerFormViewIsCurrentActivityContextCondition") || conditionName.Equals("ServerFormIsCurrentActivityContextCondition"))
                        {
                            condition.Properties.Set("DesignTemplate", "ServerProcessCondition");
                        }

                        break;
                    case "ViewRenderModeCondition":
                    case "SubViewRenderModeCondition":
                    case "SubFormViewRenderModeCondition":
                    case "SubFormRenderModeCondition":
                    case "FormRenderModeCondition":
                    case "FormViewRenderModeCondition":
                        rightValue = conditionElem.SelectSingleNode("Parts/Part[@Name='RenderMode']/Value").InnerText;

                        left = new PropertyExpression(PropertyExpressionSourceType.SystemVariable, "RenderMode", ExpressionType.Text);

                        equals = new Equals(left, new ValueExpression(rightValue));

                        condition.Expressions.Add(equals);

                        if (subFormID != Guid.Empty)
                        {
                            condition.SubFormGuid = subFormID;
                            left.SourceSubFormGuid = subFormID;
                        }
                        if (instanceID != Guid.Empty)
                        {
                            left.SourceInstanceGuid = instanceID;
                        }

                        handler.Conditions.Add(condition);
                        break;
                    default:
                        break;
                }
            }
        }

        private void CreateActionsFromRuleXML(XmlNode ruleNode, Event ev, string context, View currentView, Authoring.Form currentForm, Handler handler, XmlNodeList ruleActionsNode, Dictionary<Guid, WSA.Eventing.Action> existingActions, Dictionary<Guid, WSA.Eventing.Condition> existingConditions, Dictionary<Guid, WSA.Eventing.Handler> existingHandlersCollection)
        {
            string actionName;
            Authoring.Eventing.Action action;

            for (var l = 0; l < ruleActionsNode.Count; l++)
            {
                Guid viewGuid = Guid.Empty;
                Guid objectGuid = Guid.Empty;
                string objectName = null;
                string objectDisplayName = null;
                string containerName = null;
                Guid controlGuid = Guid.Empty;
                Guid actionGuid = Guid.Empty;
                Guid actionDefinitionGuid = Guid.Empty;
                XmlNode ruleAction = ruleActionsNode[l];
                Mapping lp;
                WSA.View thisClientView = null;
                Control thisControl = null;

                if (ruleAction.Attributes["ID"] != null)
                {
                    actionGuid = new Guid(ruleAction.Attributes["ID"].Value);
                }
                else { actionGuid = Guid.NewGuid(); }

                actionName = ruleAction.Attributes["Name"].Value;
                XmlNode actionDefinition = ruleDefinition.SelectSingleNode("SourceCode.Forms/RuleDefinitions/Actions/Action[@Name=" + XmlHelper.XPathParameterEncode(actionName) + "]");

                action = new Authoring.Eventing.Action(actionGuid);
                handler.Actions.Add(action);

                XmlAttribute subformGuidAttr = ruleAction.SelectSingleNode("Parts/Part/Data/Item[@SubFormID]")?.Attributes["SubFormID"];
                if (subformGuidAttr != null && !string.IsNullOrEmpty(subformGuidAttr.Value))
                {
                    action.SubFormGuid = new Guid(subformGuidAttr.Value);
                }

                string instanceGuid;
                if (action.SubFormGuid == Guid.Empty)
                {
                    // No subform, can use any if found
                    instanceGuid = ruleAction.SelectSingleNode("Parts/Part/Data/Item/InstanceID")?.InnerText;
                }
                else
                {
                    // Must use the InstanceID of the SubForm
                    instanceGuid = ruleAction.SelectSingleNode("Parts/Part[@Name='Form']/Data/Item/InstanceID")?.InnerText;
                    if (string.IsNullOrEmpty(instanceGuid)) //still not found, might be subview opened by view instance
                    {
                        instanceGuid = ruleAction.SelectSingleNode("Parts[not(Part[@Name='Form'])]/Part[@Name='View']/Data/Item/InstanceID")?.InnerText;
                    }
                }

                if (!string.IsNullOrEmpty(instanceGuid))
                {
                    action.InstanceGuid = new Guid(instanceGuid);
                }

                // if it does not have a subform guid, then it cannot have a subforminstance guid.
                if (action.SubFormGuid != Guid.Empty)
                {
                    // also, if it doesnt have a Form part, then it cant be a view on a form
                    string subformInstanceGuid = ruleAction.SelectSingleNode("Parts[Part[@Name='Form']]/Part[@Name='View']/Data/Item/InstanceID")?.InnerText;
                    if (!string.IsNullOrEmpty(subformInstanceGuid))
                    {
                        action.SubFormInstanceGuid = new Guid(subformInstanceGuid);
                    }
                }

                if (ruleAction.Attributes["DefinitionID"] != null)
                {
                    actionDefinitionGuid = new Guid(ruleAction.Attributes["DefinitionID"].Value);
                }
                else { actionDefinitionGuid = Guid.NewGuid(); }

                UpdateActionCollectionsFromMappingsXml(ruleAction, action);

                action.DefinitionGuid = actionDefinitionGuid;
                action.IsEnabled = ruleAction.Attributes["Enabled"] != null ? Boolean.Parse(ruleAction.Attributes["Enabled"].Value) : true;
                action.IsReference = ruleAction.Attributes["IsCurrentHandler"] != null ? !(Boolean.Parse(ruleAction.Attributes["IsCurrentHandler"].Value)) : false;

                string location = ruleAction.Attributes["Context"] != null ? ruleAction.Attributes["Context"].Value : context;
                action.Properties.Set("Location", location);

                if (actionName != "HandlerAction")
                {
                    action.ActionType = (ActionType)Enum.Parse(typeof(ActionType), actionDefinition.Attributes["Type"]?.Value, true);
                    XmlNodeList definitionPartNodes = actionDefinition.SelectNodes("Parts/Part");
                    foreach (XmlNode partNode in definitionPartNodes)
                    {
                        string partName = partNode.Attributes["Name"].Value;
                        switch (partName)
                        {
                            case "ExecutionType":
                                string executionType = ruleAction.SelectSingleNode("Parts/Part[@Name='ExecutionType']/Value").InnerText;
                                action.ExecutionType = (ActionExecutionType)Enum.Parse(typeof(ActionExecutionType), executionType, true);
                                break;

                            case "Form":
                                XmlNode formNode = ruleAction.SelectSingleNode("Parts/Part[@Name='Form']");
                                SetProperty(
                                    action,
                                    "FormID",
                                    formNode.SelectSingleNode("Value").InnerText,
                                    formNode.SelectSingleNode("Data/Item/Name")?.InnerText,
                                    formNode.SelectSingleNode("Display").InnerText
                                );
                                break;

                            case "View":
                                XmlNode viewNode = ruleAction.SelectSingleNode("Parts/Part[@Name='View']");
                                string viewGuidValue = viewNode.SelectSingleNode("Value").InnerText;
                                viewGuid = new Guid(viewGuidValue);

                                containerName = viewNode.SelectSingleNode("Data/Item/Name")?.InnerText; // Display name
                                if (string.IsNullOrEmpty(containerName))
                                {
                                    containerName = viewNode.SelectSingleNode("Display").InnerText; // MVI name
                                }

                                SetProperty(
                                    action,
                                    "ViewID",
                                    viewGuidValue,
                                    containerName,
                                    containerName
                                );
                                break;

                            case "Object":
                                XmlNode objectNode = ruleAction.SelectSingleNode("Parts/Part[@Name='Object']");
                                SetProperty(
                                    action,
                                    "ObjectID",
                                    objectNode.SelectSingleNode("Value").InnerText,
                                    objectNode.SelectSingleNode("Data/Item/Name")?.InnerText,
                                    objectNode.SelectSingleNode("Display").InnerText
                                );
                                break;

                            case "FormMethod":
                                XmlNode formMethodNode = ruleAction.SelectSingleNode("Parts/Part[@Name='FormMethod']");
                                SetProperty(
                                    action,
                                    "Method",
                                    formMethodNode.SelectSingleNode("Value").InnerText,
                                    formMethodNode.SelectSingleNode("Value").InnerText,
                                    formMethodNode.SelectSingleNode("Display").InnerText
                                );
                                break;

                            case "ObjectMethod":
                                XmlNode objectMethodNode = ruleAction.SelectSingleNode("Parts/Part[@Name='ObjectMethod']");
                                SetProperty(
                                    action,
                                    "Method",
                                    objectMethodNode.SelectSingleNode("Value").InnerText,
                                    objectMethodNode.SelectSingleNode("Value").InnerText,
                                    objectMethodNode.SelectSingleNode("Display").InnerText
                                );
                                break;

                            case "ViewMethod":
                                XmlNode methodNode = ruleAction.SelectSingleNode("Parts/Part[@Name='ViewMethod']");
                                SetProperty(
                                    action,
                                    "Method",
                                    methodNode.SelectSingleNode("Value").InnerText,
                                    methodNode.SelectSingleNode("Value").InnerText,
                                    methodNode.SelectSingleNode("Display").InnerText
                                );
                                break;

                            case "ControlMethod":
                                XmlNode controlMethodNode = ruleAction.SelectSingleNode("Parts/Part[@Name='ControlMethod']");
                                SetProperty(
                                    action,
                                    "Method",
                                    controlMethodNode.SelectSingleNode("Value").InnerText,
                                    controlMethodNode.SelectSingleNode("Value").InnerText,
                                    controlMethodNode.SelectSingleNode("Display").InnerText
                                );
                                break;

                            case "FormControl":
                                XmlNode formControlNode = ruleAction.SelectSingleNode("Parts/Part[@Name='FormControl']");
                                SetProperty(
                                    action,
                                    "ControlID",
                                    formControlNode.SelectSingleNode("Value").InnerText,
                                    formControlNode.SelectSingleNode("Data/Item/Name")?.InnerText,
                                    formControlNode.SelectSingleNode("Display").InnerText
                                );
                                break;

                            case "ViewControl":
                                XmlNode viewControlNode = ruleAction.SelectSingleNode("Parts/Part[@Name='ViewControl']");
                                string controlGuidValue = viewControlNode.SelectSingleNode("Value").InnerText;
                                controlGuid = new Guid(controlGuidValue);
                                SetProperty(
                                    action,
                                    "ControlID",
                                    controlGuidValue,
                                    viewControlNode.SelectSingleNode("Data/Item/Name")?.InnerText,
                                    viewControlNode.SelectSingleNode("Display").InnerText
                                );
                                break;

                            case "Control":
                                XmlNode controlNode = ruleAction.SelectSingleNode("Parts/Part[@Name='Control']");
                                string actionControlGuid = string.Empty;
                                XmlNode actionFormControlGuid = controlNode.SelectSingleNode("Value");
                                if (actionFormControlGuid != null && !string.IsNullOrEmpty(actionFormControlGuid.InnerText))
                                    actionControlGuid = actionFormControlGuid.InnerText;

                                if (!string.IsNullOrEmpty(actionControlGuid))
                                {
                                    SetProperty(
                                        action,
                                        "ControlID",
                                        actionControlGuid,
                                        controlNode.SelectSingleNode("Data/Item/Name")?.InnerText,
                                        controlNode.SelectSingleNode("Display").InnerText
                                    );
                                }
                                else
                                    throw new ArgumentException(string.Format(Resources.RuleHelper.ErrorRulePartMissing, "Control"));
                                break;

                            case "Panel":
                                XmlNode panelNode = ruleAction.SelectSingleNode("Parts/Part[@Name='Panel']");
                                SetProperty(
                                    action,
                                    "PanelID",
                                    panelNode.SelectSingleNode("Value").InnerText,
                                    panelNode.SelectSingleNode("Data/Item/Name")?.InnerText,
                                    panelNode.SelectSingleNode("Display").InnerText
                                );
                                break;

                            case "AreaItem":
                                XmlNode areaItemNode = ruleAction.SelectSingleNode("Parts/Part[@Name='AreaItem']");
                                Guid areaItemGuid = Guid.Empty;
                                if (areaItemNode != null)
                                {
                                    string areaItemGuidValue = areaItemNode.SelectSingleNode("Value").InnerText;
                                    string areaItemNameValue = areaItemNode.SelectSingleNode("Data/Item/Name")?.InnerText;
                                    string areaItemDisplayValue = areaItemNode.SelectSingleNode("Display").InnerText;
                                    areaItemGuid = new Guid(areaItemGuidValue);
                                    SetProperty(
                                        action,
                                        "ControlID",
                                        areaItemGuidValue,
                                        areaItemNameValue,
                                        areaItemDisplayValue
                                    );
                                    SetProperty(
                                        action,
                                        "InstanceGuid",
                                        areaItemGuidValue,
                                        areaItemNameValue,
                                        areaItemDisplayValue
                                    );
                                }
                                break;

                            case "ItemStates":
                                string itemState = ruleAction.SelectSingleNode("Parts/Part[@Name='ItemStates']/Value").InnerText;
                                action.ItemState = (ActionItemState)Enum.Parse(typeof(ActionItemState), itemState, true);
                                break;

                            case "HeadingValueInput":
                                string headingValue = ruleAction.SelectSingleNode("Parts/Part[@Name='HeadingValueInput']/Value").InnerText;
                                action.Properties.Set("Heading", headingValue);
                                break;

                            case "MessageValueInput":
                                string messageValue = ruleAction.SelectSingleNode("Parts/Part[@Name='MessageValueInput']/Value").InnerText;
                                if (actionName == "ShowConfirmation")
                                {
                                    var valueXml = XmlHelper.CreateXmlDocument(messageValue);
                                    action.Properties.Set("Message", valueXml.SelectSingleNode("/Message/Value").InnerText);
                                    action.Properties.Set("MessageIsLiteral", bool.Parse(valueXml.SelectSingleNode("/Message/Checked").InnerText));
                                }
                                else
                                {
                                    action.Properties.Set("Message", messageValue);
                                }
                                break;

                            case "Rule":
                                XmlNode targetRuleNode = ruleAction.SelectSingleNode("Parts/Part[@Name='Rule']");
                                SetProperty(
                                    action,
                                    "EventID",
                                    targetRuleNode.SelectSingleNode("Value").InnerText,
                                    targetRuleNode.SelectSingleNode("Value").InnerText,
                                    targetRuleNode.SelectSingleNode("Display").InnerText
                                );

                                XmlAttribute instanceIdAttr, subformIdAttr, subformInstanceIdAttr;

                                subformIdAttr = ruleAction.SelectSingleNode(".//Data/Item[@SubFormID]")?.Attributes["SubFormID"];
                                if (subformIdAttr != null && !string.IsNullOrEmpty(subformIdAttr.Value))
                                {
                                    action.SubFormGuid = new Guid(subformIdAttr.Value);
                                }

                                instanceIdAttr = ruleAction.SelectSingleNode(".//Data/Item[@InstanceID]")?.Attributes["InstanceID"];
                                if (instanceIdAttr != null && !string.IsNullOrEmpty(instanceIdAttr.Value))
                                {
                                    action.InstanceGuid = new Guid(instanceIdAttr.Value);
                                }

                                subformInstanceIdAttr = ruleAction.SelectSingleNode(".//Data/Item[@SubFormInstanceID]")?.Attributes["SubFormInstanceID"];
                                if (subformInstanceIdAttr != null && !string.IsNullOrEmpty(subformInstanceIdAttr.Value))
                                {
                                    action.SubFormInstanceGuid = new Guid(subformInstanceIdAttr.InnerText);
                                }

                                break;

                            case "Process":
                                XmlNode processNode = ruleAction.SelectSingleNode("Parts/Part[@Name='Process']");
                                SetProperty(
                                    action,
                                    "ProcessName",
                                    (processNode.SelectSingleNode("Value").InnerText).Replace(@"\\", @"\"),
                                    (processNode.SelectSingleNode("Display").InnerText).Replace(@"\\", @"\"),
                                    (processNode.SelectSingleNode("Display").InnerText).Replace(@"\\", @"\")
                                );
                                break;

                            case "Activity":
                                XmlNode activityNode = ruleAction.SelectSingleNode("Parts/Part[@Name='Activity']");
                                SetProperty(
                                    action,
                                    "ActivityFullName",
                                    (activityNode.SelectSingleNode("Value").InnerText).Replace(@"\\", @"\"),
                                    (activityNode.SelectSingleNode("Display").InnerText).Replace(@"\\", @"\"),
                                    (activityNode.SelectSingleNode("Display").InnerText).Replace(@"\\", @"\")
                                );
                                break;

                            default:
                                break;
                        }
                    }
                }

                if (ruleAction.SelectSingleNode("Comments") != null)
                {
                    string commentsValue = ruleAction.SelectSingleNode("Comments").InnerText;
                    if (string.IsNullOrEmpty(commentsValue) && action.Properties["Comments"] != null)
                    {
                        action.Properties.Remove("Comments");
                    }
                    else
                    {
                        action.Properties.Set("Comments", commentsValue);
                    }
                }

                // Check if inheritance was lost
                if (existingActions != null)
                {
                    if (existingActions.ContainsKey(actionGuid))
                    {
                        if (action.IsReference && existingActions[actionGuid].IsInherited)
                        {
                            action.IsInherited = true;
                        }
                        if (!action.IsEnabled.Equals(existingActions[actionGuid].IsEnabled) && existingActions[actionGuid].IsReference)
                        {
                            action.IsInherited = false;
                        }

                        if (action.IsReference)
                        {
                            action.InstanceGuid = existingActions[actionGuid].InstanceGuid;
                        }
                    }
                }

                switch (actionName)
                {
                    case "FormExecute":
                        SetProperty(
                            action,
                            "FormID",
                            ev.State.Form.Guid.ToString(),
                            ev.State.Form.Name,
                            ev.State.Form.DisplayName
                        );
                        break;

                    case "ViewControlMethodExecuteItemsState":
                    case "SubViewControlMethodExecuteItemsState":
                    case "SubFormViewControlMethodExecuteItemsState":
                        if (action.SubFormGuid != Guid.Empty)
                        {
                            WSA.View clientView = InfoProvider.GetView(action.ViewGuid);
                            thisControl = clientView?.Controls[controlGuid];

                            if (clientView == null || thisControl == null)
                            {
                                var objectItemNode = ruleAction.SelectSingleNode("Parts/Part[@Name='ObjectMethod']/Data/Item[@ItemType='Object']");
                                Guid.TryParse(objectItemNode.Attributes["ID"].InnerText, out objectGuid);
                                objectName = objectItemNode.Attributes["Name"].InnerText;
                                objectDisplayName = objectItemNode.Attributes["DisplayName"].InnerText;
                            }
                            else
                            {
                                objectGuid = new Guid(thisControl.Properties["AssociationSO"]);
                                objectName = thisControl.Properties.GetNameValue("AssociationSO");
                                objectDisplayName = thisControl.Properties.GetDisplayValue("AssociationSO");
                            }
                        }
                        else
                        {
                            if (context == "View" || context == "Control")
                            {
                                thisControl = currentView.Controls[controlGuid];
                                if (thisControl == null)
                                {
                                    var objectItemNode = ruleAction.SelectSingleNode("Parts/Part[@Name='ObjectMethod']/Data/Item[@ItemType='Object']");
                                    Guid.TryParse(objectItemNode.Attributes["ID"].InnerText, out objectGuid);
                                    objectName = objectItemNode.Attributes["Name"].InnerText;
                                    objectDisplayName = objectItemNode.Attributes["DisplayName"].InnerText;
                                }
                                else
                                {
                                    objectGuid = new Guid(thisControl.Properties["AssociationSO"]);
                                    objectName = thisControl.Properties.GetNameValue("AssociationSO");
                                    objectDisplayName = thisControl.Properties.GetDisplayValue("AssociationSO");
                                }
                            }
                            else
                            {
                                WSA.View clientView = InfoProvider.GetView(action.ViewGuid);
                                thisControl = clientView?.Controls[controlGuid];

                                if (clientView == null || thisControl == null)
                                {
                                    var objectItemNode = ruleAction.SelectSingleNode("Parts/Part[@Name='ObjectMethod']/Data/Item[@ItemType='Object']");
                                    Guid.TryParse(objectItemNode.Attributes["ID"].InnerText, out objectGuid);
                                    objectName = objectItemNode.Attributes["Name"].InnerText;
                                    objectDisplayName = objectItemNode.Attributes["DisplayName"].InnerText;
                                }
                                else
                                {
                                    objectGuid = new Guid(thisControl.Properties["AssociationSO"]);
                                    objectName = thisControl.Properties.GetNameValue("AssociationSO");
                                    objectDisplayName = thisControl.Properties.GetDisplayValue("AssociationSO");
                                }
                            }
                        }

                        SetProperty(
                            action,
                            "ObjectID",
                            objectGuid.ToString(),
                            objectName,
                            objectDisplayName
                        );
                        break;

                    case "FormNavigation":
                    case "SubFormDisable":
                    case "SubFormEnable":
                    case "BrowserClose":
                        action.ViewGuid = Guid.Empty;
                        break;

                    case "FormDisable":
                    case "FormEnable":
                        action.ViewGuid = Guid.Empty;
                        SetProperty(
                            action,
                            "FormID",
                            ev.State.Form.Guid.ToString(),
                            ev.State.Form.Name,
                            ev.State.Form.DisplayName
                        );
                        break;

                    case "SubViewOpen":
                    case "SubViewOpenMethodExecute":
                        // Add subform events to parent
                        View subformView = InfoProvider.GetView(action.ViewGuid);
                        if (!action.IsReference && subformView != null)
                        {
                            Helper.Merge(subformView.BaseState, GetEvent(action.Handler).State, Guid.Empty, action.SubFormGuid, true, false);
                        }
                        break;

                    case "FormOpen":
                        // Add subform events to parent
                        Form subformForm = InfoProvider.GetForm(action.FormGuid);
                        if (!action.IsReference && subformForm != null)
                        {
                            Helper.Merge(subformForm.BaseState, GetEvent(action).State, Guid.Empty, action.SubFormGuid, true, false);
                        }
                        break;

                    case "SubformClose":
                    case "SubViewCloseMethodExecute":
                        action.Properties["CloseTarget"] = "Form";
                        break;

                    case "ServerSubViewListControlPopulation":
                    case "ServerSubFormListControlPopulation":
                    case "SubViewListControlPopulation":
                    case "SubFormListControlPopulation":
                    case "SubViewListControlPopulateFromData":
                    case "SubFormViewListControlPopulateFromData":
                    case "SubViewListControlPreLoadData":
                    case "SubFormViewListControlPreLoadData":

                        if (actionName.Equals("ServerSubViewListControlPopulation") || actionName.Equals("ServerSubFormListControlPopulation"))
                        {
                            action.Properties.Set("DesignTemplate", "ServerControlPopulation");
                        }

                        thisClientView = InfoProvider.GetView(action.ViewGuid);
                        thisControl = thisClientView?.Controls[controlGuid];

                        if (thisClientView == null || thisControl == null)
                        {
                            var objectItemNode = ruleAction.SelectSingleNode("Parts/Part[@Name='ObjectMethod']/Data/Item[@ItemType='Object']");
                            Guid.TryParse(objectItemNode.Attributes["ID"].InnerText, out objectGuid);
                            objectName = objectItemNode.Attributes["Name"].InnerText;
                            objectDisplayName = objectItemNode.Attributes["DisplayName"].InnerText;
                        }
                        else
                        {
                            if (string.IsNullOrEmpty(thisControl.Properties["DisplaySO"]))
                            {
                                objectGuid = new Guid(thisControl.Properties["AssociationSO"]);
                                objectName = thisControl.Properties.GetNameValue("AssociationSO");
                                objectDisplayName = thisControl.Properties.GetDisplayValue("AssociationSO");
                            }
                            else
                            {
                                if (string.IsNullOrEmpty(thisControl.Properties["IsComposite"]) || thisControl.Properties["IsComposite"].ToLower() == "false")
                                {
                                    objectGuid = new Guid(thisControl.Properties["AssociationSO"]);
                                    objectName = thisControl.Properties.GetNameValue("AssociationSO");
                                    objectDisplayName = thisControl.Properties.GetDisplayValue("AssociationSO");
                                }
                                else
                                {
                                    objectGuid = new Guid(thisControl.Properties["DisplaySO"]);
                                    objectName = thisControl.Properties.GetNameValue("DisplaySO");
                                    objectDisplayName = thisControl.Properties.GetDisplayValue("DisplaySO");
                                }
                            }
                        }
                        SetProperty(
                            action,
                            "ObjectID",
                            objectGuid.ToString(),
                            objectName,
                            objectDisplayName
                        );

                        if (actionName == "SubViewListControlPopulation" || actionName == "SubFormListControlPopulation" || actionName.Equals("ServerSubViewListControlPopulation") || actionName.Equals("ServerSubFormListControlPopulation"))
                        {
                            CreateControlListenerResults(action);
                        }
                        break;

                    case "ServerViewListControlPopulation":
                    case "ViewListControlPopulation":
                    case "ViewListControlPopulateFromData":
                    case "ViewListControlPreLoadData":

                        if (actionName.Equals("ServerViewListControlPopulation"))
                        {
                            action.Properties.Set("DesignTemplate", "ServerControlPopulation");
                        }

                        if (context == "View" || context == "Control")
                        {
                            thisControl = currentView.Controls[controlGuid];

                            if (thisControl == null)
                            {
                                var objectItemNode = ruleAction.SelectSingleNode("Parts/Part[@Name='ObjectMethod']/Data/Item[@ItemType='Object']");
                                Guid.TryParse(objectItemNode.Attributes["ID"].InnerText, out objectGuid);
                                objectName = objectItemNode.Attributes["Name"].InnerText;
                                objectDisplayName = objectItemNode.Attributes["DisplayName"].InnerText;
                            }
                            else
                            {
                                if (string.IsNullOrEmpty(thisControl.Properties["DisplaySO"]))
                                {
                                    objectGuid = new Guid(thisControl.Properties["AssociationSO"]);
                                    objectName = thisControl.Properties.GetNameValue("AssociationSO");
                                    objectDisplayName = thisControl.Properties.GetDisplayValue("AssociationSO");
                                }
                                else
                                {
                                    if (string.IsNullOrEmpty(thisControl.Properties["IsComposite"]) || thisControl.Properties["IsComposite"].ToLower() == "false")
                                    {
                                        objectGuid = new Guid(thisControl.Properties["AssociationSO"]);
                                        objectName = thisControl.Properties.GetNameValue("AssociationSO");
                                        objectDisplayName = thisControl.Properties.GetDisplayValue("AssociationSO");
                                    }
                                    else
                                    {
                                        objectGuid = new Guid(thisControl.Properties["DisplaySO"]);
                                        objectName = thisControl.Properties.GetNameValue("DisplaySO");
                                        objectDisplayName = thisControl.Properties.GetDisplayValue("DisplaySO");
                                    }
                                }
                            }

                            SetProperty(
                                action,
                                "ObjectID",
                                objectGuid.ToString(),
                                objectName,
                                objectDisplayName
                            );
                        }
                        else
                        {
                            thisClientView = InfoProvider.GetView(action.ViewGuid);
                            thisControl = thisClientView?.Controls[controlGuid];

                            if (thisClientView == null || thisControl == null)
                            {
                                var objectItemNode = ruleAction.SelectSingleNode("Parts/Part[@Name='ObjectMethod']/Data/Item[@ItemType='Object']");
                                Guid.TryParse(objectItemNode.Attributes["ID"].InnerText, out objectGuid);
                                objectName = objectItemNode.Attributes["Name"].InnerText;
                                objectDisplayName = objectItemNode.Attributes["DisplayName"].InnerText;
                            }
                            else
                            {

                                if (string.IsNullOrEmpty(thisClientView.Controls[controlGuid].Properties["DisplaySO"]))
                                {
                                    objectGuid = new Guid(thisClientView.Controls[controlGuid].Properties["AssociationSO"]);
                                    objectName = thisClientView.Controls[controlGuid].Properties.GetNameValue("AssociationSO");
                                    objectDisplayName = thisClientView.Controls[controlGuid].Properties.GetDisplayValue("AssociationSO");
                                }
                                else
                                {
                                    if (string.IsNullOrEmpty(thisClientView.Controls[controlGuid].Properties["IsComposite"]) || thisClientView.Controls[controlGuid].Properties["IsComposite"].ToLower() == "false")
                                    {
                                        objectGuid = new Guid(thisClientView.Controls[controlGuid].Properties["AssociationSO"]);
                                        objectName = thisClientView.Controls[controlGuid].Properties.GetNameValue("AssociationSO");
                                        objectDisplayName = thisClientView.Controls[controlGuid].Properties.GetDisplayValue("AssociationSO");
                                    }
                                    else
                                    {
                                        objectGuid = new Guid(thisClientView.Controls[controlGuid].Properties["DisplaySO"]);
                                        objectName = thisClientView.Controls[controlGuid].Properties.GetNameValue("DisplaySO");
                                        objectDisplayName = thisClientView.Controls[controlGuid].Properties.GetDisplayValue("DisplaySO");
                                    }
                                }
                            }

                            SetProperty(
                                action,
                                "ObjectID",
                                objectGuid.ToString(),
                                objectName,
                                objectDisplayName
                            );
                        }

                        if (actionName == "ViewListControlPopulation" || actionName.Equals("ServerViewListControlPopulation"))
                        {
                            CreateControlListenerResults(action);
                        }
                        break;

                    case "HideControl":
                    case "SubViewHideControl":
                    case "SubFormHideControl":
                    case "HideFormControl":
                    case "HideSubFormControl":
                        lp = new Mapping();
                        lp.SourceType = MappingSourceType.Value;
                        lp.SourceValue = "false";
                        lp.TargetID = "isvisible";
                        lp.SourceID = "";
                        lp.TargetType = MappingTargetType.ControlProperty;
                        lp.TargetName = action.Properties.GetNameValue("ControlID");
                        lp.TargetDisplayName = action.Properties.GetDisplayValue("ControlID");
                        lp.TargetSubFormGuid = action.SubFormGuid;
                        lp.TargetSubFormInstanceGuid = action.SubFormInstanceGuid;
                        lp.TargetInstanceGuid = action.InstanceGuid;

                        action.Parameters.Add(lp);
                        break;

                    case "ShowControl":
                    case "SubViewShowControl":
                    case "SubFormShowControl":
                    case "ShowFormControl":
                    case "ShowSubFormControl":
                        lp = new Mapping();
                        lp.SourceType = MappingSourceType.Value;
                        lp.SourceValue = "true";
                        lp.TargetID = "isvisible";
                        lp.SourceID = "";
                        lp.TargetType = MappingTargetType.ControlProperty;
                        lp.TargetName = action.Properties.GetNameValue("ControlID");
                        lp.TargetDisplayName = action.Properties.GetDisplayValue("ControlID");
                        lp.TargetSubFormGuid = action.SubFormGuid;
                        lp.TargetSubFormInstanceGuid = action.SubFormInstanceGuid;
                        lp.TargetInstanceGuid = action.InstanceGuid;

                        action.Parameters.Add(lp);
                        break;

                    case "EnableControl":
                    case "SubViewEnableControl":
                    case "SubFormEnableControl":
                    case "EnableFormControl":
                    case "EnableSubFormControl":
                        lp = new Mapping();
                        lp.SourceType = MappingSourceType.Value;
                        lp.SourceValue = "true";
                        lp.TargetID = "isenabled";
                        lp.SourceID = "";
                        lp.TargetType = MappingTargetType.ControlProperty;
                        lp.TargetName = action.Properties.GetNameValue("ControlID");
                        lp.TargetDisplayName = action.Properties.GetDisplayValue("ControlID");
                        lp.TargetSubFormGuid = action.SubFormGuid;
                        lp.TargetSubFormInstanceGuid = action.SubFormInstanceGuid;
                        lp.TargetInstanceGuid = action.InstanceGuid;

                        action.Parameters.Add(lp);
                        break;

                    case "DisableControl":
                    case "SubViewDisableControl":
                    case "SubFormDisableControl":
                    case "DisableFormControl":
                    case "DisableSubFormControl":
                        lp = new Mapping();
                        lp.SourceType = MappingSourceType.Value;
                        lp.SourceValue = "false";
                        lp.TargetID = "isenabled";
                        lp.SourceID = "";
                        lp.TargetType = MappingTargetType.ControlProperty;
                        lp.TargetName = action.Properties.GetNameValue("ControlID");
                        lp.TargetDisplayName = action.Properties.GetDisplayValue("ControlID");
                        lp.TargetSubFormGuid = action.SubFormGuid;
                        lp.TargetSubFormInstanceGuid = action.SubFormInstanceGuid;
                        lp.TargetInstanceGuid = action.InstanceGuid;

                        action.Parameters.Add(lp);
                        break;

                    case "BrowserNavigate":
                        XmlNode urlValueNode = ruleAction.SelectSingleNode("Parts/Part[@Name='Url']/Value");
                        if (urlValueNode != null)
                        {
                            action.Properties.Set("Url", urlValueNode.InnerText);
                        }
                        else
                        {
                            action.Properties.Set("Url", ruleAction.SelectSingleNode("Mappings/Mapping/Item[@ContextType='value']").InnerXml);
                        }
                        break;

                    case "ShowPanel":
                    case "SubFormShowPanel":
                        lp = new Mapping();
                        lp.SourceType = MappingSourceType.Value;
                        lp.SourceValue = "Show";
                        lp.TargetID = "display";
                        lp.SourceID = "";
                        lp.TargetType = MappingTargetType.PanelProperty;
                        lp.TargetName = action.Properties.GetNameValue("PanelID");
                        lp.TargetDisplayName = action.Properties.GetDisplayValue("PanelID");
                        lp.TargetSubFormGuid = action.SubFormGuid;
                        lp.TargetSubFormInstanceGuid = action.SubFormInstanceGuid;
                        lp.TargetInstanceGuid = action.InstanceGuid;

                        action.Parameters.Add(lp);
                        break;

                    case "HidePanel":
                    case "SubFormHidePanel":
                        lp = new Mapping();
                        lp.SourceType = MappingSourceType.Value;
                        lp.SourceValue = "Hide";
                        lp.TargetID = "display";
                        lp.SourceID = "";
                        lp.TargetType = MappingTargetType.PanelProperty;
                        lp.TargetName = action.Properties.GetNameValue("PanelID");
                        lp.TargetDisplayName = action.Properties.GetDisplayValue("PanelID");
                        lp.TargetSubFormGuid = action.SubFormGuid;
                        lp.TargetSubFormInstanceGuid = action.SubFormInstanceGuid;
                        lp.TargetInstanceGuid = action.InstanceGuid;

                        action.Parameters.Add(lp);
                        break;

                    case "HideView":
                    case "SubFormHideView":
                        lp = new Mapping();
                        lp.SourceType = MappingSourceType.Value;
                        lp.SourceValue = "Hide";
                        lp.TargetID = "display";
                        lp.SourceID = "";
                        lp.TargetType = MappingTargetType.ViewProperty;
                        lp.TargetName = action.Properties.GetNameValue("ViewID");
                        lp.TargetDisplayName = action.Properties.GetDisplayValue("ViewID");
                        lp.TargetSubFormGuid = action.SubFormGuid;
                        lp.TargetSubFormInstanceGuid = action.SubFormInstanceGuid;
                        lp.TargetInstanceGuid = action.InstanceGuid;

                        action.Parameters.Add(lp);
                        break;

                    case "ShowView":
                    case "SubFormShowView":
                        lp = new Mapping();
                        lp.SourceType = MappingSourceType.Value;
                        lp.SourceValue = "Show";
                        lp.TargetID = "display";
                        lp.SourceID = "";
                        lp.TargetType = MappingTargetType.ViewProperty;
                        lp.TargetName = action.Properties.GetNameValue("ViewID");
                        lp.TargetDisplayName = action.Properties.GetDisplayValue("ViewID");
                        lp.TargetSubFormGuid = action.SubFormGuid;
                        lp.TargetSubFormInstanceGuid = action.SubFormInstanceGuid;
                        lp.TargetInstanceGuid = action.InstanceGuid;

                        action.Parameters.Add(lp);
                        break;

                    case "EnableView":
                    case "SubViewEnableView":
                    case "SubFormEnableView":
                        lp = new Mapping();
                        lp.SourceType = MappingSourceType.Value;
                        lp.SourceValue = "Enable";
                        lp.TargetID = "display";
                        lp.SourceID = "";
                        lp.TargetType = MappingTargetType.ViewProperty;
                        lp.TargetName = action.Properties.GetNameValue("ViewID");
                        lp.TargetDisplayName = action.Properties.GetDisplayValue("ViewID");
                        lp.TargetSubFormGuid = action.SubFormGuid;
                        lp.TargetSubFormInstanceGuid = action.SubFormInstanceGuid;
                        lp.TargetInstanceGuid = action.InstanceGuid;

                        action.Parameters.Add(lp);
                        break;

                    case "DisableView":
                    case "SubViewDisableView":
                    case "SubFormDisableView":
                        lp = new Mapping();
                        lp.SourceType = MappingSourceType.Value;
                        lp.SourceValue = "Disable";
                        lp.TargetID = "display";
                        lp.SourceID = "";
                        lp.TargetType = MappingTargetType.ViewProperty;
                        lp.TargetName = action.Properties.GetNameValue("ViewID");
                        lp.TargetDisplayName = action.Properties.GetDisplayValue("ViewID");
                        lp.TargetSubFormGuid = action.SubFormGuid;
                        lp.TargetSubFormInstanceGuid = action.SubFormInstanceGuid;
                        lp.TargetInstanceGuid = action.InstanceGuid;

                        action.Parameters.Add(lp);
                        break;

                    case "ExpandView":
                    case "SubViewExpandView":
                    case "SubFormExpandView":
                        lp = new Mapping();
                        lp.SourceType = MappingSourceType.Value;
                        lp.SourceValue = "Expand";
                        lp.TargetID = "display";
                        lp.SourceID = "";
                        lp.TargetType = MappingTargetType.ViewProperty;
                        lp.TargetName = action.Properties.GetNameValue("ViewID");
                        lp.TargetDisplayName = action.Properties.GetDisplayValue("ViewID");
                        lp.TargetSubFormGuid = action.SubFormGuid;
                        lp.TargetSubFormInstanceGuid = action.SubFormInstanceGuid;
                        lp.TargetInstanceGuid = action.InstanceGuid;

                        action.Parameters.Add(lp);
                        break;

                    case "CollapseView":
                    case "SubViewCollapseView":
                    case "SubFormCollapseView":
                        lp = new Mapping();
                        lp.SourceType = MappingSourceType.Value;
                        lp.SourceValue = "Collapse";
                        lp.TargetID = "display";
                        lp.SourceID = "";
                        lp.TargetType = MappingTargetType.ViewProperty;
                        lp.TargetName = action.Properties.GetNameValue("ViewID");
                        lp.TargetDisplayName = action.Properties.GetDisplayValue("ViewID");
                        lp.TargetSubFormGuid = action.SubFormGuid;
                        lp.TargetSubFormInstanceGuid = action.SubFormInstanceGuid;
                        lp.TargetInstanceGuid = action.InstanceGuid;

                        action.Parameters.Add(lp);
                        break;

                    case "ShowViewFilter":
                    case "SubViewShowViewFilter":
                    case "SubFormShowViewFilter":
                        lp = new Mapping();
                        lp.SourceType = MappingSourceType.Value;
                        lp.SourceValue = "Show";
                        lp.TargetID = "filterdisplay";
                        lp.SourceID = "";
                        lp.TargetType = MappingTargetType.ViewProperty;
                        lp.TargetName = action.Properties.GetNameValue("ViewID");
                        lp.TargetDisplayName = action.Properties.GetDisplayValue("ViewID");
                        lp.TargetSubFormGuid = action.SubFormGuid;
                        lp.TargetSubFormInstanceGuid = action.SubFormInstanceGuid;
                        lp.TargetInstanceGuid = action.InstanceGuid;

                        action.Parameters.Add(lp);
                        break;

                    case "HideViewFilter":
                    case "SubViewHideViewFilter":
                    case "SubFormHideViewFilter":
                        lp = new Mapping();
                        lp.SourceType = MappingSourceType.Value;
                        lp.SourceValue = "Hide";
                        lp.TargetID = "filterdisplay";
                        lp.SourceID = "";
                        lp.TargetType = MappingTargetType.ViewProperty;
                        lp.TargetName = action.Properties.GetNameValue("ViewID");
                        lp.TargetDisplayName = action.Properties.GetDisplayValue("ViewID");
                        lp.TargetSubFormGuid = action.SubFormGuid;
                        lp.TargetSubFormInstanceGuid = action.SubFormInstanceGuid;
                        lp.TargetInstanceGuid = action.InstanceGuid;

                        action.Parameters.Add(lp);
                        break;

                    case "ControlTransfer":
                        if (context == "Form")
                        {
                            SetProperty(
                                action,
                                "FormID",
                                currentForm.Guid.ToString(),
                                currentForm.Name,
                                currentForm.DisplayName
                            );
                        }
                        break;
                    case "ServerControlTransfer":
                        action.Properties.Set("DesignTemplate", "ServerDataTransfer");
                        if (context == "Form")
                        {
                            SetProperty(
                                action,
                                "FormID",
                                currentForm.Guid.ToString(),
                                currentForm.Name,
                                currentForm.DisplayName
                            );
                        }
                        break;
                    case "ServerOpenedFormTransfer":
                    case "ServerSubViewTransferData":
                        action.Properties.Set("DesignTemplate", "ServerDataTransfer");
                        break;

                    case "ShowConfirmation":
                        action.Properties.Set("PromptType", "Confirmation");
                        break;

                    case "ShowAlert":
                        action.Properties.Set("MessageLocation", "Popup");

                        XmlNode headingIsLiteralFalseNode = ruleAction.SelectSingleNode("Mappings/Mapping[Item[@Name='Heading']]/Item[@ContextType='value' and @Literal='False']");
                        XmlNode bodyIsLiteralFalseNode = ruleAction.SelectSingleNode("Mappings/Mapping[Item[@Name='Body']]/Item[@ContextType='value' and @Literal='False']");

                        // only add if deviates from default
                        if (headingIsLiteralFalseNode != null)
                        {
                            action.Properties.Set("HeadingIsLiteral", false);
                        }

                        if (bodyIsLiteralFalseNode != null)
                        {
                            action.Properties.Set("BodyIsLiteral", false);
                        }

                        break;

                    case "BrowseMessage":
                        action.Properties.Set("MessageLocation", "Open");
                        break;

                    case "ProcessStart":
                    case "ViewProcessStart":
                    case "SubViewProcessStart":
                    case "SubFormProcessStart":
                    case "SubFormViewProcessStart":
                        action.Method = "StartProcess";
                        break;

                    case "ProcessAction":
                    case "ViewProcessAction":
                    case "SubViewProcessAction":
                    case "SubFormProcessAction":
                    case "SubFormViewProcessAction":
                        action.Method = "ActionProcess";
                        break;

                    case "ProcessLoad":
                    case "ViewProcessLoad":
                    case "SubViewProcessLoad":
                    case "SubFormProcessLoad":
                    case "SubFormViewProcessLoad":
                    case "ServerProcessLoad":
                    case "ServerViewProcessLoad":
                    case "ServerSubViewProcessLoad":
                    case "ServerSubFormProcessLoad":
                    case "ServerSubFormViewProcessLoad":
                        if (actionName.Equals("ServerProcessLoad") || actionName.Equals("ServerViewProcessLoad") ||
                            actionName.Equals("ServerSubViewProcessLoad") || actionName.Equals("ServerSubFormProcessLoad") ||
                            actionName.Equals("ServerSubFormViewProcessLoad"))
                        {
                            action.Properties.Set("DesignTemplate", "ServerProcessAction");
                        }

                        action.Method = "LoadProcess";
                        break;

                    case "EditableListAddRow":
                    case "SubViewEditableListAddRow":
                    case "SubFormEditableListAddRow":
                        action.Method = "AddItem";
                        break;

                    case "EditableListEditRow":
                    case "SubViewEditableListEditRow":
                    case "SubFormEditableListEditRow":
                        action.Method = "EditItem";
                        break;

                    case "EditableListRemoveRow":
                    case "SubViewEditableListRemoveRow":
                    case "SubFormEditableListRemoveRow":
                        action.Method = "RemoveItem";
                        break;

                    case "EditableListApplyRow":
                    case "SubViewEditableListApplyRow":
                    case "SubFormEditableListApplyRow":
                        action.Method = "AcceptItem";
                        break;

                    case "EditableListCancelRow":
                    case "SubViewEditableListCancelRow":
                    case "SubFormEditableListCancelRow":
                        action.Method = "CancelItem";
                        break;

                    case "SubFormSetViewControlProperties":
                    case "SubFormSetFormControlProperties":
                    case "SubViewSetViewControlProperties":
                    case "SetViewControlProperties":
                    case "SetFormControlProperties":
                    case "SetFormViewControlProperties":
                        action.Properties.Set("DesignTemplate", "SetControlProperties");
                        break;

                    case "ServerSetViewControlProperties":
                    case "ServerSetFormViewControlProperties":
                    case "ServerSetFormControlProperties":
                    case "ServerSetSubViewControlProperties":
                    case "ServerSetSubFormViewControlProperties":
                    case "ServerSetSubFormControlProperties":
                        action.Properties.Set("DesignTemplate", "ServerSetControlProperties");
                        break;

                    case "SetAreaItemProperties":
                    case "SubFormSetAreaItemProperties":
                        action.Properties.Set("DesignTemplate", "SetAreaItemProperties");
                        break;
                    case "ServerSetAreaItemProperties":
                    case "ServerSetSubFormAreaItemProperties":
                        action.Properties.Set("DesignTemplate", "ServerSetAreaItemProperties");
                        break;

                    case "SetFormProperties":
                        action.Properties.Set("DesignTemplate", "SetFormProperties");
                        SetProperty(
                            action,
                            "FormID",
                            ev.Form.Guid.ToString(),
                            ev.Form.Name,
                            ev.Form.DisplayName
                        );
                        break;
                    case "ServerSetFormProperties":
                        action.Properties.Set("DesignTemplate", "ServerSetFormProperties");
                        SetProperty(
                            action,
                            "FormID",
                            ev.Form.Guid.ToString(),
                            ev.Form.Name,
                            ev.Form.DisplayName
                        );
                        break;

                    case "SetSubFormProperties":
                        action.Properties.Set("DesignTemplate", "SetFormProperties");
                        break;
                    case "ServerSetSubFormProperties":
                        action.Properties.Set("DesignTemplate", "ServerSetFormProperties");
                        break;

                    case "ServerRuleExecute":
                        action.Properties.Set("DesignTemplate", "ServerRuleExecute");
                        break;

                    case "HandlerAction":
                        action.ActionType = ActionType.Handler;
                        AddHandlerAction(ruleNode,
                            ev,
                            context,
                            currentView,
                            currentForm,
                            ruleAction,
                            action,
                            existingActions,
                            existingConditions,
                            existingHandlersCollection);
                        break;
                    case "ServerObjectMethodExecute":
                        action.Properties.Set("DesignTemplate", "ServerObjectMethodExecute");
                        break;
                    case "ServerPanelFocus":
                    case "ServerSubFormPanelFocus":
                        action.Properties.Set("DesignTemplate", "ServerPanelFocus");
                        break;
                    case "ServerViewFocus":
                    case "ServerSubFormViewFocus":
                        action.Properties.Set("DesignTemplate", "ServerViewFocus");
                        break;
                    case "ServerSubViewEventAction":
                    case "ServerViewMethodExecute":
                    case "ServerOpenedFormViewMethodExecute":
                        action.Properties.Set("DesignTemplate", "ServerViewExecute");
                        break;
                    default:
                        break;
                }
            }
        }

        /// <summary>
        /// Merges the referenced action to the referencing action (<see cref="referencingAction"/>).
        /// </summary>
        /// <param name="referencingAction">The referencing action to find the referenced action.</param>
        /// <param name="mergedReferencedActionGuids">Action guids that have already been merged.</param>
        /// <remarks>
        /// After the merge the referencing action was updated from the referenced action.
        /// 
        /// This is only required till we can capture the Designer template for actions.
        /// </remarks>
        private void MergeReferencedAction(WSA.Eventing.Action referencingAction, HashSet<Guid> mergedReferencedActionGuids = null)
        {

            // F(na)
            //   F-VI(2)
            //     F-VI(5a)-SV(5a)
            //       F-VI-SV-SV (ex)
            //       F-VI-SV-SF (ex)
            //     F-VI(5b)-SF(5b)
            //       F-VI-SF-VI (prob)
            //       F-VI-SF-SV (ex)
            //       F-VI-SF-SF (ex)
            //   F-SV(3a)
            //     F-SV-SV (ex)
            //     F-SV-SF (ex)
            //   F-SF(3b)
            //     F-SF(4)-VI(4)
            //       F-SF-VI-SV (ex)
            //       F-SF-VI-SF (ex)
            //     F-SF-SV (ex)
            //     F-SF-SF (ex)


            // V(na)
            //   V-SV(3a) 
            //   V-SF(3b)
            //     V-SF(4)-VI(4)
            //       V-SF-VI-SV (ex)
            //       V-SF-VI-SF (ex)


            // baseState

            // hasVIID


            // hasSFID
            // V->SV
            // V->SF
            // F->SV
            // F->SF
            // F.VI->SV
            // F.VI->SF
            // F(.VI)->SV
            // F(.VI)->SF

            // CASE 0: Recursive
            // Ignore and exist (usually a missing action in self referencing scenarios)

            // CASE 1: State > Base State
            // Resolve further via Base State if action is an intermediate referencing action, then merge current action from Base State's merged action. 

            // CASE 2: Form > View Instance (ignoring Form > View Instance > SubForms/View - see case 5)
            // Resolve further via View if action is an intermediate referencing action, then merge current action from View's merged action as a view instance action. 

            // CASE 3: Form/View > SubForm/View (excluding Form/View > SubForm > View Instance - see case 6)
            // Check for actions on view or sub form (ignoring * > SubForm > View Instances) 

            //     CASE 3a: * > SubView
            //     Resolve further via View if action is an intermediate referencing action, then merge current action from View's merged action as a subview action. 

            //     CASE 3b: * > SubForm (ignoring *>SubForm>View Instances)
            //     Resolve further via Form if action is an intermediate referencing action, then merge current action from Form's merged action as a subform action. 

            // CASE 4a: * > SubForm > View Instance (via target action InstanceID)
            // Resolve further via Form if action is an intermediate referencing action, then merge current action from Form's merged action as a subform action.

            // CASE 4b: * > SubForm > View Instance (via open action InstanceID)
            // Resolve further via Form if action is an intermediate referencing action, then merge current action from Form's merged action as a subform action.


            // CASE 5: Form > View Instance > SubForm / SubView 
            // Resolve further via View if action is an intermediate referencing action, then merge current action from View's merged action as a subform action.

            //     CASE 5a: Form > View Instance > SubForm
            //     Resolve further via Form if action is an intermediate referencing action, then merge current action from Form's merged action as a subform action. 

            //     CASE 5b: Form > View Instance > SubView
            //     Resolve further via View if action is an intermediate referencing action, then merge current action from View's merged action as a subview action. 

            #region CASE 0: Recursive
            // Ignore and exist (usually a missing action in self referencing scenarios)

            // Prevent endless loops (crashing for self-reversing subforms)
            if (mergedReferencedActionGuids == null)
                mergedReferencedActionGuids = new HashSet<Guid>();

            if (mergedReferencedActionGuids.Contains(referencingAction.Guid))
            {
                return;
            }
            mergedReferencedActionGuids.Add(referencingAction.Guid);
            if (referencingAction.Parameters.Count > 0 || referencingAction.Results.Count > 0)
            {
                return;
            }

            #endregion

            if (referencingAction.IsReference)
            {
                #region Variables and simple initialization

                WSA.Eventing.Event actionEvent = GetEvent(referencingAction);
                WSA.Eventing.Action subFormOpenAction = null;
                WSA.Form form = actionEvent.State.Form;
                WSA.Form subForm = null;
                WSA.View view = actionEvent.State.View;
                WSA.View subView = null;

                WSA.View viewInstance = null;
                WSA.View viewInstanceViaOpen = null;
                WSA.View subFormViewInstance = null;

                bool hasInstanceGuid = !referencingAction.InstanceGuid.Equals(Guid.Empty);
                bool hasSubFormGuid = !referencingAction.SubFormGuid.Equals(Guid.Empty);
                bool hasSubFormInstanceGuid = !referencingAction.SubFormInstanceGuid.Equals(Guid.Empty);

                #endregion

                if (!actionEvent.State.IsBase)
                {
                    #region CASE 1: State > Base State
                    // Resolve further via Base State if action is an intermediate referencing action, then merge current action from Base State's merged action. 

                    WSA.Eventing.State state = actionEvent.State.ParentState;

                    if (state != null)
                    {
                        if (TryMergeReferencedAction(state, referencingAction, referencingAction.InstanceGuid, referencingAction.SubFormGuid, referencingAction.SubFormInstanceGuid, referencingAction.InstanceGuid, referencingAction.SubFormGuid, mergedReferencedActionGuids))
                        {
                            return;
                        }
                    }

                    #endregion
                }

                #region Additional variable initialization

                if (hasSubFormGuid)
                {
                    // Find the local instance of the subForm open action, subForm and subView
                    subFormOpenAction = GetSubFormAction(null, referencingAction.SubFormGuid, actionEvent);
                    if (subFormOpenAction != null && subFormOpenAction.IsReference)
                    {
                        var @event = subFormOpenAction.GetParentEvent();
                        if (@event != null)
                        {
                            WSA.Eventing.Action otherOpenAction = GetSubFormAction(null, @event.SubFormGuid, actionEvent);
                            if (otherOpenAction != null)
                            {
                                subFormOpenAction = otherOpenAction;
                            }
                        }
                    }
                    subForm = subFormOpenAction.FormGuid.Equals(Guid.Empty) ? null : InfoProvider.GetForm(subFormOpenAction.FormGuid);
                    subView = subFormOpenAction.ViewGuid.Equals(Guid.Empty) ? null : InfoProvider.GetView(subFormOpenAction.ViewGuid);
                }
                if (hasInstanceGuid && form != null) // View on Form
                {
                    Context context = new Context();
                    context.Form = form;
                    context.InstanceGuid = referencingAction.InstanceGuid;
                    viewInstance = ResolveFormView(context, referencingAction.Validation);
                }
                if (hasSubFormGuid && (hasInstanceGuid || hasSubFormInstanceGuid))
                {
                    if (form != null && hasInstanceGuid) // View on Form opens Subview?
                    {
                        Context context = new Context();
                        context.Form = form;
                        context.InstanceGuid = subFormOpenAction.InstanceGuid;
                        viewInstanceViaOpen = ResolveFormView(context, referencingAction.Validation);
                    }

                    if (subForm != null && hasSubFormInstanceGuid) // Subform view instance
                    {
                        Context context = new Context();
                        context.Form = subForm;
                        context.InstanceGuid = referencingAction.InstanceGuid;
                        context.SubformInstanceGuid = referencingAction.SubFormInstanceGuid;
                        subFormViewInstance = ResolveFormView(context, referencingAction.Validation);
                    }
                }

                #endregion

                if (form != null && subForm != null && form.Guid.Equals(subForm.Guid)) // Self Referencing Form/SubForm?
                {
                    if (TryMergeReferencedAction(form.DefaultState, referencingAction, Guid.Empty, Guid.Empty, referencingAction.SubFormInstanceGuid, Guid.Empty, referencingAction.SubFormGuid, mergedReferencedActionGuids))
                    {
                        return;
                    }
                    if (TryMergeReferencedAction(form.DefaultState, referencingAction, Guid.Empty, referencingAction.SubFormGuid, Guid.Empty, Guid.Empty, Guid.Empty, mergedReferencedActionGuids))
                    {
                        return;
                    }
                }

                if (view != null && subView != null && view.Guid.Equals(subView.Guid)) // Self Referencing View/SubView?
                {
                    if (TryMergeReferencedAction(view.DefaultState, referencingAction, Guid.Empty, Guid.Empty, referencingAction.SubFormInstanceGuid, Guid.Empty, referencingAction.SubFormGuid, mergedReferencedActionGuids))
                    {
                        return;
                    }
                    if (TryMergeReferencedAction(view.DefaultState, referencingAction, Guid.Empty, referencingAction.SubFormGuid, Guid.Empty, Guid.Empty, Guid.Empty, mergedReferencedActionGuids))
                    {
                        return;
                    }
                }

                if (hasInstanceGuid && !hasSubFormGuid)
                {
                    #region CASE 2: Form > View Instance (ignoring Form > View Instance > SubForms/View - done later)
                    // Resolve further via View if action is an intermediate referencing action, then merge current action from View's merged action as a view instance action. 

                    if (form != null)
                    {
                        if (viewInstance != null)
                        {
                            if (TryMergeReferencedAction(viewInstance.DefaultState, referencingAction, Guid.Empty, Guid.Empty, Guid.Empty, referencingAction.InstanceGuid, Guid.Empty, mergedReferencedActionGuids))
                            {
                                return;
                            }
                        }
                    }

                    #endregion
                }

                if (!(hasInstanceGuid || hasSubFormInstanceGuid) && hasSubFormGuid)
                {
                    if (subView != null)
                    {
                        #region CASE 3a: * > SubView
                        // Resolve further via View if action is an intermediate referencing action, then merge current action from View's merged action as a subview action. 

                        if (TryMergeReferencedAction(subView.DefaultState, referencingAction, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, referencingAction.SubFormGuid, mergedReferencedActionGuids))
                        {
                            return;
                        }

                        if (TryMergeReferencedAction(subView.DefaultState, referencingAction, Guid.Empty, referencingAction.SubFormGuid, Guid.Empty, Guid.Empty, referencingAction.SubFormGuid, mergedReferencedActionGuids))
                        {
                            return;
                        }

                        #endregion
                    }

                    if (subForm != null)
                    {
                        #region CASE 3b: * > SubForm (excluding * > SubForm > View Instance - done later)
                        // Resolve further via Form if action is an intermediate referencing action, then merge current action from Form's merged action as a subform action. 

                        if (TryMergeReferencedAction(subForm.DefaultState, referencingAction, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, referencingAction.SubFormGuid, mergedReferencedActionGuids))
                        {
                            return;
                        }

                        if (TryMergeReferencedAction(subForm.DefaultState, referencingAction, Guid.Empty, referencingAction.SubFormGuid, Guid.Empty, Guid.Empty, referencingAction.SubFormGuid, mergedReferencedActionGuids))
                        {
                            return;
                        }

                        #endregion
                    }
                }

                if (hasSubFormGuid && (hasInstanceGuid || hasSubFormInstanceGuid))
                {
                    if (subForm != null)
                    {
                        if (subFormViewInstance != null)
                        {
                            #region CASE 4: * > SubForm > SubFormViewInstance (opened from *)

                            if (TryMergeReferencedAction(subForm.DefaultState, referencingAction, referencingAction.InstanceGuid, Guid.Empty, referencingAction.SubFormInstanceGuid, Guid.Empty, referencingAction.SubFormGuid, mergedReferencedActionGuids))
                            {
                                return;
                            }

                            if (TryMergeReferencedAction(subFormViewInstance.DefaultState, referencingAction, Guid.Empty, referencingAction.SubFormGuid, referencingAction.SubFormInstanceGuid, referencingAction.SubFormInstanceGuid, referencingAction.SubFormGuid, mergedReferencedActionGuids))
                            {
                                return;
                            }

                            #endregion
                        }
                    }

                    if (subView != null)
                    {
                        if (viewInstance != null)
                        {
                            #region CASE 5a: Form > ViewInstance > SubView (opened in ViewInstance)

                            if (TryMergeReferencedAction(viewInstance.DefaultState, referencingAction, Guid.Empty, referencingAction.SubFormGuid, Guid.Empty, referencingAction.InstanceGuid, Guid.Empty, mergedReferencedActionGuids))
                            {
                                return;
                            }

                            #endregion
                        }
                    }

                    if (subForm != null)
                    {
                        if (viewInstance != null)
                        {
                            #region CASE 5b: Form > ViewInstance > SubForm (opened in ViewInstance)

                            if (TryMergeReferencedAction(viewInstance.DefaultState, referencingAction, Guid.Empty, referencingAction.SubFormGuid, Guid.Empty, referencingAction.InstanceGuid, Guid.Empty, mergedReferencedActionGuids))
                            {
                                return;
                            }

                            #endregion
                        }

                        if (viewInstanceViaOpen != null)
                        {
                            #region CASE 5c: Form > ViewInstance > SubForm (opened in ViewInstance)

                            if (TryMergeReferencedAction(viewInstanceViaOpen.DefaultState, referencingAction, referencingAction.InstanceGuid, referencingAction.SubFormGuid, referencingAction.SubFormInstanceGuid, subFormOpenAction.InstanceGuid, Guid.Empty, mergedReferencedActionGuids))
                            {
                                return;
                            }

                            #endregion
                        }

                        //if (subFormViewInstance != null)
                        //{
                        //	#region CASE 5d: Form > ViewInstance > SubForm > SubFormViewInstance (opened in ViewInstance)

                        //	if (TryMergeReferencedAction(subForm.DefaultState, referencingAction, Guid.Empty, Guid.Empty, referencingAction.SubFormInstanceGuid, Guid.Empty, Guid.Empty, mergedReferencedActionGuids))
                        //	{
                        //		return;
                        //	}

                        //	#endregion
                        //}
                    }

                    if (subView != null)
                    {
                        #region CASE 5e: * > SubView > SubForm > SubFormViewInstance

                        if (TryMergeReferencedAction(subView.DefaultState, referencingAction, Guid.Empty, referencingAction.SubFormGuid, referencingAction.SubFormInstanceGuid, Guid.Empty, referencingAction.SubFormGuid, mergedReferencedActionGuids))
                        {
                            return;
                        }

                        #endregion
                    }
                }

                if (TryMergeReferencedAction(actionEvent.State, referencingAction, referencingAction.InstanceGuid, referencingAction.SubFormGuid, referencingAction.SubFormInstanceGuid, referencingAction.InstanceGuid, referencingAction.SubFormGuid, mergedReferencedActionGuids))
                {
                    return;
                }
            }
        }

        private bool TryMergeReferencedAction(WSA.Eventing.State state, WSA.Eventing.Action action, Guid instanceGuid, Guid subFormGuid, Guid subFormInstanceGuid, Guid asInstanceGuid, Guid asSubFormGuid, HashSet<Guid> mergedReferencedActionGuids)
        {
            WSA.Eventing.Action referencedAction = FindReferencedAction(state, action, instanceGuid, subFormGuid, subFormInstanceGuid);

            if (referencedAction != null)
            {
                // First merge found action
                MergeReferencedAction(referencedAction, new HashSet<Guid>(mergedReferencedActionGuids));

                // Merge the result to the supplied referencing action
                WSA.Eventing.Action referencedClone = referencedAction.Clone<WSA.Eventing.Action>();
                Helper.MergeAction(referencedClone, action, asInstanceGuid, asSubFormGuid);

                return true;
            }
            else
                return false;
        }

        private static WSA.Eventing.Action FindReferencedAction(State state, WSA.Eventing.Action action, Guid instanceGuid, Guid subFormGuid, Guid subFormInstanceGuid)
        {
            WSA.Eventing.Action referencedAction = null;
            List<WSA.Eventing.Action> referencedActions = new List<WSA.Eventing.Action>();

            foreach (WSA.Eventing.Event @event in state.Events)
            {
                if (@event.EventType == EventType.User)
                {
                    foreach (WSA.Eventing.Handler handler in @event.Handlers)
                    {
                        referencedAction = FindReferencedAction(handler, action, instanceGuid, subFormGuid, subFormInstanceGuid, referencedActions);
                        if (referencedAction != null) break;
                    }
                    if (referencedAction != null) break;
                }
            }

            if (referencedAction == null)
            {
                // Use a IsReference=true candidate if no isReference=false candidate was found
                if (referencedActions.Count > 0)
                {
                    referencedAction = referencedActions[0];
                }
            }

            return referencedAction;
        }

        private static WSA.Eventing.Action FindReferencedAction(WSA.Eventing.Handler handler, WSA.Eventing.Action action, Guid instanceGuid, Guid subFormGuid, Guid subFormInstanceGuid, List<WSA.Eventing.Action> referencedActions)
        {
            WSA.Eventing.Action referencedAction = null;

            foreach (WSA.Eventing.Action otherAction in handler.Actions)
            {
                if (otherAction.ActionType == ActionType.Handler)
                {
                    referencedAction = FindReferencedActionHandlerAction(action, otherAction);
                    if (referencedAction != null) break;
                }
                else if (otherAction.DefinitionGuid.Equals(action.DefinitionGuid)
                    && (otherAction.InstanceGuid.Equals(instanceGuid) || otherAction.InstanceGuid.Equals(subFormInstanceGuid))
                    && otherAction.SubFormGuid.Equals(subFormGuid)
                    && !otherAction.Guid.Equals(action.Guid))
                {
                    if (otherAction.IsReference)
                    {
                        referencedActions.Add(otherAction);
                    }
                    else
                    {
                        referencedAction = otherAction;
                        break;
                    }
                }

                foreach (WSA.Eventing.Handler childHandler in otherAction.Handlers)
                {
                    referencedAction = FindReferencedAction(childHandler, action, instanceGuid, subFormGuid, subFormInstanceGuid, referencedActions);
                    if (referencedAction != null) break;
                }

                if (referencedAction != null) break;
            }

            return referencedAction;
        }

        private static WSA.Eventing.Action FindReferencedActionHandlerAction(WSA.Eventing.Action orgAction, WSA.Eventing.Action action)
        {
            WSA.Eventing.Action referencedAction = null;

            foreach (WSA.Eventing.Handler handler in action.Handlers)
            {
                foreach (WSA.Eventing.Action otherAction in handler.Actions)
                {
                    if (otherAction.ActionType == ActionType.Handler)
                    {
                        referencedAction = FindReferencedActionHandlerAction(orgAction, otherAction);
                        if (referencedAction != null)
                        {
                            break;
                        }
                    }
                    else if (otherAction.DefinitionGuid.Equals(orgAction.DefinitionGuid))
                    {
                        referencedAction = otherAction;
                        break;
                    }
                }

                if (referencedAction != null)
                {
                    break;
                }
            }

            return referencedAction;
        }

        private void ResolveTransferActionTemplate(Authoring.Eventing.Action action, Context result)
        {
            result.SubformGuid = action.SubFormGuid;

            // check view properties first
            if (action.ControlGuid != Guid.Empty && action.PanelGuid == Guid.Empty && action.ObjectGuid == Guid.Empty && string.IsNullOrEmpty(action.Method) && action.ActionType == ActionType.Transfer && !string.IsNullOrEmpty(action.Properties["DesignTemplate"]) && action.Properties["DesignTemplate"].ToUpperInvariant() == "SETAREAITEMPROPERTIES")
            {
                if (action.SubFormGuid.Equals(Guid.Empty))
                {
                    result.RuleActionName = "SetAreaItemProperties";
                    ResolveForm(result, result.Event);
                    result.InstanceGuid = action.ControlGuid; // so that we have an instance ID, so that we can resolve the view name
                    ResolveFormView(result, action.Validation);
                    ResolveControl(result, result.Form != null ? result.Form.Controls : new ControlCollection(null), action);
                }
                else
                {
                    result.RuleActionName = "SubFormSetAreaItemProperties";
                    GetSubFormAction(result, action.SubFormGuid, result.Event);

                    if (result.SubItemAction != null)
                    {
                        ResolveExternalForm(result);
                        ResolveControl(result, result.Form != null ? result.Form.Controls : new ControlCollection(null), action);
                        result.InstanceGuid = action.ControlGuid;
                        ResolveFormView(result, action.Validation);
                        result.EventFriendlyName = GetEventFriendlyNameForSubForm(action);
                    }
                }
            }
            else if (action.ControlGuid != Guid.Empty && action.PanelGuid == Guid.Empty && action.ObjectGuid == Guid.Empty && string.IsNullOrEmpty(action.Method) && action.ActionType == ActionType.Transfer && !string.IsNullOrEmpty(action.Properties["DesignTemplate"]) && action.Properties["DesignTemplate"].ToUpperInvariant() == "SERVERSETAREAITEMPROPERTIES")
            {
                if (action.SubFormGuid != Guid.Empty)
                {
                    result.RuleActionName = "ServerSetSubFormAreaItemProperties";

                    GetSubFormAction(result, action.SubFormGuid, result.Event);

                    if (result.SubItemAction != null)
                    {
                        ResolveExternalForm(result);
                        ResolveControl(result, result.Form != null ? result.Form.Controls : new ControlCollection(null), action);
                        result.InstanceGuid = action.ControlGuid;
                        ResolveFormView(result, action.Validation);
                        result.EventFriendlyName = GetEventFriendlyNameForSubForm(action);
                    }
                }
                else
                {
                    result.RuleActionName = "ServerSetAreaItemProperties";
                    ResolveForm(result, result.Event);
                    result.InstanceGuid = action.ControlGuid; // so that we have an instance ID, so that we can resolve the view name
                    ResolveFormView(result, action.Validation);
                    ResolveControl(result, result.Form != null ? result.Form.Controls : new ControlCollection(null), action);
                }
            }
            else if ((action.ViewGuid != Guid.Empty || action.FormGuid != Guid.Empty) && string.IsNullOrEmpty(action.Method) && action.ControlGuid != Guid.Empty && action.PanelGuid == Guid.Empty && action.ObjectGuid == Guid.Empty && !string.IsNullOrEmpty(action.Properties["DesignTemplate"]) && action.Properties["DesignTemplate"].ToUpperInvariant() == "SETCONTROLPROPERTIES")
            {
                if (action.SubFormGuid != Guid.Empty)
                {
                    GetSubFormAction(result, action.SubFormGuid, result.Event);

                    if (result.SubItemAction != null)
                    {
                        if (result.SubItemAction.ViewGuid != Guid.Empty)
                        {
                            result.RuleActionName = "SubViewSetViewControlProperties";
                            ResolveExternalView(result);
                            ResolveControl(result, result.View != null ? result.View.Controls : new ControlCollection(null), action);
                        }
                        else if (result.SubItemAction.FormGuid != Guid.Empty)
                        {
                            ResolveExternalForm(result);
                            if (result.Action.SubFormInstanceGuid.Equals(Guid.Empty))
                            {
                                ResolveControl(result, result.Form != null ? result.Form.Controls : new ControlCollection(null), action);
                                result.RuleActionName = "SubFormSetFormControlProperties";
                            }
                            else
                            {
                                ResolveFormView(result);
                                ResolveControl(result, result.View != null ? result.View.Controls : new ControlCollection(null), action);
                                result.RuleActionName = "SubFormSetViewControlProperties";
                            }
                        }
                        result.EventFriendlyName = GetEventFriendlyNameForSubForm(action);
                    }
                }
                else
                {
                    if (action.ViewGuid == Guid.Empty)
                    {
                        ResolveForm(result, result.Event);
                        ResolveControl(result, result.Form != null ? result.Form.Controls : new ControlCollection(null), action);
                        result.RuleActionName = "SetFormControlProperties";
                    }
                    else
                    {
                        if (action.InstanceGuid.Equals(Guid.Empty))
                        {
                            ResolveView(result, action);
                            ResolveControl(result, result.View != null ? result.View.Controls : new ControlCollection(null), action);
                            result.RuleActionName = "SetViewControlProperties";
                        }
                        else
                        {
                            ResolveForm(result, result.Event);
                            ResolveFormView(result);
                            ResolveControl(result, result.View != null ? result.View.Controls : new ControlCollection(null), action);
                            result.RuleActionName = "SetFormViewControlProperties";
                        }
                    }
                }
            }
            else if ((action.ViewGuid != Guid.Empty || action.FormGuid != Guid.Empty) && string.IsNullOrEmpty(action.Method) && action.ControlGuid != Guid.Empty && action.PanelGuid == Guid.Empty && action.ObjectGuid == Guid.Empty && !string.IsNullOrEmpty(action.Properties["DesignTemplate"]) && action.Properties["DesignTemplate"].ToUpperInvariant() == "SERVERSETCONTROLPROPERTIES")
            {
                if (action.SubFormGuid != Guid.Empty)
                {
                    GetSubFormAction(result, action.SubFormGuid, result.Event);
                    if (result.SubItemAction != null)
                    {
                        if (result.SubItemAction.ViewGuid != Guid.Empty)
                        {
                            result.RuleActionName = "ServerSetSubViewControlProperties";
                            ResolveExternalView(result);
                            ResolveControl(result, result.View != null ? result.View.Controls : new ControlCollection(null), action);
                        }
                        else if (result.SubItemAction.FormGuid != Guid.Empty)
                        {
                            ResolveExternalForm(result);
                            if (result.Action.SubFormInstanceGuid.Equals(Guid.Empty))
                            {
                                ResolveControl(result, result.Form != null ? result.Form.Controls : new ControlCollection(null), action);
                                result.RuleActionName = "ServerSetSubFormControlProperties";
                            }
                            else
                            {
                                ResolveFormView(result);
                                ResolveControl(result, result.View != null ? result.View.Controls : new ControlCollection(null), action);
                                result.RuleActionName = "ServerSetSubFormViewControlProperties";
                            }
                        }
                        result.EventFriendlyName = GetEventFriendlyNameForSubForm(action);
                    }
                }
                else
                {
                    if (action.ViewGuid == Guid.Empty)
                    {
                        ResolveForm(result, result.Event);
                        ResolveControl(result, result.Form != null ? result.Form.Controls : new ControlCollection(null), action);
                        result.RuleActionName = "ServerSetFormControlProperties";
                    }
                    else
                    {
                        if (action.InstanceGuid.Equals(Guid.Empty))
                        {
                            ResolveView(result, action);
                            ResolveControl(result, result.View != null ? result.View.Controls : new ControlCollection(null), action);
                            result.RuleActionName = "ServerSetViewControlProperties";
                        }
                        else
                        {
                            ResolveForm(result, result.Event);
                            ResolveFormView(result);
                            ResolveControl(result, result.View != null ? result.View.Controls : new ControlCollection(null), action);
                            result.RuleActionName = "ServerSetFormViewControlProperties";
                        }
                    }
                }
            }
            else if (action.FormGuid != Guid.Empty && string.IsNullOrEmpty(action.Method) && action.ControlGuid == Guid.Empty && action.PanelGuid == Guid.Empty && action.ObjectGuid == Guid.Empty && !string.IsNullOrEmpty(action.Properties["DesignTemplate"]) && action.Properties["DesignTemplate"].ToUpperInvariant() == "SETFORMPROPERTIES")
            {
                if (action.SubFormGuid.Equals(Guid.Empty))
                {
                    ResolveForm(result, result.Event);
                    result.RuleActionName = "SetFormProperties";
                }
                else
                {
                    GetSubFormAction(result, action.SubFormGuid, result.Event);

                    if (result.SubItemAction != null)
                    {
                        ResolveExternalForm(result);
                        result.RuleActionName = "SetSubFormProperties";
                        result.EventFriendlyName = GetEventFriendlyNameForSubForm(action);
                    }
                }
            }
            else if (action.FormGuid != Guid.Empty && string.IsNullOrEmpty(action.Method) && action.ControlGuid == Guid.Empty && action.PanelGuid == Guid.Empty && action.ObjectGuid == Guid.Empty && !string.IsNullOrEmpty(action.Properties["DesignTemplate"]) && action.Properties["DesignTemplate"].ToUpperInvariant() == "SERVERSETFORMPROPERTIES")
            {
                if (action.SubFormGuid.Equals(Guid.Empty))
                {
                    ResolveForm(result, result.Event);
                    result.RuleActionName = "ServerSetFormProperties";
                }
                else
                {
                    result.RuleActionName = "ServerSetSubFormProperties";

                    GetSubFormAction(result, action.SubFormGuid, result.Event);

                    if (result.SubItemAction != null)
                    {
                        ResolveExternalForm(result);
                    }

                    result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                }
            }
            else if (action.ViewGuid != Guid.Empty && string.IsNullOrEmpty(action.Method) && action.ControlGuid == Guid.Empty && action.PanelGuid == Guid.Empty && action.ObjectGuid == Guid.Empty && action.Parameters.Count > 0 && (action.Parameters[0].TargetID.ToUpperInvariant() == "DISPLAY" || action.Parameters[0].TargetID.ToUpperInvariant() == "FILTERDISPLAY"))
            {
                if (action.Parameters.Count > 0)
                {
                    if (action.SubFormGuid != Guid.Empty)
                    {
                        result.RuleActionName = "Sub";

                        GetSubFormAction(result, action.SubFormGuid, result.Event);
                        if (result.SubItemAction.FormGuid != Guid.Empty)
                        {
                            ResolveExternalForm(result);
                            ResolveFormView(result);
                            result.RuleActionName += "Form";
                        }
                        else
                        {
                            ResolveExternalView(result);
                            result.RuleActionName += "View";
                        }

                        result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                    }
                    else
                    {
                        if (result.Event.View == null)
                        {
                            ResolveForm(result, result.Event);
                            ResolveFormView(result);
                        }
                        else
                        {
                            ResolveView(result, action);
                        }
                    }

                    if (action.Parameters[0].TargetID.ToUpperInvariant() == "DISPLAY")
                    {
                        if (action.Parameters[0].SourceValue.ToUpperInvariant() == "HIDE" || action.Parameters[0].SourceValue.ToUpperInvariant() == "SHOW")
                        {
                            if (action.Parameters[0].SourceValue.ToUpperInvariant() == "HIDE")
                            {
                                result.RuleActionName += "HideView";
                            }
                            else
                            {
                                result.RuleActionName += "ShowView";
                            }
                        }
                        else if (action.Parameters[0].SourceValue.ToUpperInvariant() == "ENABLE" || action.Parameters[0].SourceValue.ToUpperInvariant() == "DISABLE")
                        {
                            if (action.Parameters[0].SourceValue.ToUpperInvariant() == "ENABLE")
                            {
                                result.RuleActionName += "EnableView";
                            }
                            else
                            {
                                result.RuleActionName += "DisableView";
                            }
                        }
                        else if (action.Parameters[0].SourceValue.ToUpperInvariant() == "COLLAPSE" || action.Parameters[0].SourceValue.ToUpperInvariant() == "EXPAND")
                        {
                            if (action.Parameters[0].SourceValue.ToUpperInvariant() == "COLLAPSE")
                            {
                                result.RuleActionName += "CollapseView";
                            }
                            else
                            {
                                result.RuleActionName += "ExpandView";
                            }
                        }
                    }
                    else if (action.Parameters[0].TargetID.ToUpperInvariant() == "FILTERDISPLAY")
                    {
                        if (action.Parameters[0].SourceValue.ToUpperInvariant() == "HIDE")
                        {
                            result.RuleActionName += "HideViewFilter";
                        }
                        else
                        {
                            result.RuleActionName += "ShowViewFilter";
                        }
                    }
                }
            }
            else if (string.IsNullOrEmpty(action.Method) && action.ControlGuid == Guid.Empty && action.PanelGuid == Guid.Empty && action.ObjectGuid == Guid.Empty && action.Parameters.Count > 0 && action.Parameters[0].TargetType == MappingTargetType.ControlProperty && action.Parameters[0].TargetPath != string.Empty)
            {
                if (action.SubFormGuid != Guid.Empty)
                {
                    GetSubFormAction(result, action.SubFormGuid, result.Event);

                    if (result.SubItemAction != null)
                    {
                        if (result.SubItemAction.ViewGuid != Guid.Empty)
                        {
                            result.RuleActionName = "SubView";
                            ResolveExternalView(result);
                        }
                        else if (result.SubItemAction.FormGuid != Guid.Empty)
                        {
                            result.RuleActionName = "SubForm";
                            ResolveExternalForm(result);
                        }

                        result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                    }
                }

                result.RuleActionName += "ControlReadOnly";
            }
            else if (action.SubFormGuid != Guid.Empty && string.IsNullOrEmpty(action.Method) && action.ControlGuid == Guid.Empty && action.PanelGuid == Guid.Empty && action.ObjectGuid == Guid.Empty)
            {
                GetSubFormAction(result, action.SubFormGuid, result.Event);
                if (result.SubItemAction.ViewGuid != Guid.Empty)
                {
                    if (action.Properties["DesignTemplate"] != null && action.Properties["DesignTemplate"] == "ServerDataTransfer")
                    {
                        result.RuleActionName = "ServerSubViewTransferData";
                    }
                    else
                    {
                        result.RuleActionName = "SubViewTransferData";
                    }

                    ResolveExternalView(result);
                    result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                }
                else if (result.SubItemAction.FormGuid != Guid.Empty)
                {
                    if (action.Properties["DesignTemplate"] != null && action.Properties["DesignTemplate"] == "ServerDataTransfer")
                    {
                        result.RuleActionName = "ServerOpenedFormTransfer";
                    }
                    else
                    {
                        result.RuleActionName = "OpenedFormTransfer";
                    }

                    ResolveExternalForm(result);
                    result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                }
            }
            // Show/ Hide Contorl
            else if (action.ViewGuid != Guid.Empty && string.IsNullOrEmpty(action.Method) && action.ControlGuid != Guid.Empty && action.PanelGuid == Guid.Empty && action.ObjectGuid == Guid.Empty && action.Parameters.Count > 0 && action.Parameters[0].TargetType == MappingTargetType.ControlProperty && action.Parameters[0].TargetID.ToUpperInvariant() == "ISVISIBLE")
            {
                Mapping lp = action.Parameters[0];

                if (action.SubFormGuid != Guid.Empty)
                {
                    result.RuleActionName = "Sub";

                    GetSubFormAction(result, action.SubFormGuid, result.Event);


                    if (result.SubItemAction.FormGuid != Guid.Empty)
                    {
                        result.RuleActionName += "Form";
                        ResolveExternalForm(result);
                        ResolveFormView(result);
                    }
                    else
                    {
                        ResolveExternalView(result);
                        result.RuleActionName += "View";
                    }

                    ResolveControl(result, result.View != null ? result.View.Controls : new ControlCollection(null), action);
                    result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                }
                else
                {
                    if (result.Event.View == null)
                    {
                        ResolveForm(result, result.Event);
                        ResolveFormView(result);
                    }
                    else
                    {
                        ResolveView(result, action);
                    }

                    ResolveControl(result, result.View != null ? result.View.Controls : new ControlCollection(null), action);
                }

                if (lp.SourceValue.ToUpperInvariant() == "TRUE")
                {
                    result.RuleActionName += "ShowControl";
                }
                else
                {
                    result.RuleActionName += "HideControl";
                }
            }
            // For enable/disable control
            else if (action.ViewGuid != Guid.Empty && string.IsNullOrEmpty(action.Method) && action.ControlGuid != Guid.Empty && action.PanelGuid == Guid.Empty && action.ObjectGuid == Guid.Empty && action.Parameters.Count > 0 && action.Parameters[0].TargetType == MappingTargetType.ControlProperty && action.Parameters[0].TargetID.ToUpperInvariant() == "ISENABLED")
            {
                Mapping lp = action.Parameters[0];

                if (action.SubFormGuid != Guid.Empty)
                {
                    result.RuleActionName = "Sub";

                    GetSubFormAction(result, action.SubFormGuid, result.Event);

                    if (result.SubItemAction.FormGuid != Guid.Empty)
                    {
                        result.RuleActionName += "Form";
                        ResolveExternalForm(result);
                        ResolveFormView(result);
                    }
                    else
                    {
                        ResolveExternalView(result);
                        result.RuleActionName += "View";
                    }

                    ResolveControl(result, result.View != null ? result.View.Controls : new ControlCollection(null), action);
                    result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                }
                else
                {
                    if (result.Event.View == null)
                    {
                        ResolveForm(result, result.Event);
                        ResolveFormView(result);
                    }
                    else
                    {
                        ResolveView(result, action);
                    }

                    ResolveControl(result, result.View != null ? result.View.Controls : new ControlCollection(null), action);
                }

                if (lp.SourceValue.ToUpperInvariant() == "TRUE")
                {
                    result.RuleActionName += "EnableControl";
                }
                else
                {
                    result.RuleActionName += "DisableControl";
                }
            }
            else if ((action.ViewGuid != Guid.Empty || action.FormGuid != Guid.Empty) && action.SubFormGuid == Guid.Empty && string.IsNullOrEmpty(action.Method) && action.ControlGuid == Guid.Empty && action.PanelGuid == Guid.Empty && action.ObjectGuid == Guid.Empty)
            {
                ResolveView(result, GetEvent(action));

                if (action.Properties["DesignTemplate"] != null && action.Properties["DesignTemplate"] == "ServerDataTransfer")
                {
                    result.RuleActionName = "ServerControlTransfer";
                }
                else
                {
                    result.RuleActionName = "ControlTransfer";
                }
            }
            else if (action.ViewGuid == Guid.Empty && string.IsNullOrEmpty(action.Method) && action.ControlGuid != Guid.Empty && action.PanelGuid == Guid.Empty && action.ObjectGuid == Guid.Empty && action.Parameters.Count > 0 && action.Parameters[0].TargetID.ToUpperInvariant() == "ISVISIBLE" && action.Parameters[0].TargetType == MappingTargetType.ControlProperty)
            {
                Mapping lp = action.Parameters[0];

                if (lp.SourceValue.ToUpperInvariant() == "TRUE")
                {
                    result.RuleActionName = "Show";
                }
                if (lp.SourceValue.ToUpperInvariant() == "FALSE")
                {
                    result.RuleActionName = "Hide";
                }

                if (action.SubFormGuid != Guid.Empty)
                {
                    result.RuleActionName += "Sub";

                    GetSubFormAction(result, action.SubFormGuid, result.Event);

                    if (result.SubItemAction != null)
                    {
                        ResolveExternalForm(result);
                        ResolveControl(result, result.Form != null ? result.Form.Controls : new ControlCollection(null), action);
                        result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                    }
                }
                else
                {
                    ResolveForm(result, result.Event);
                    ResolveControl(result, result.Form != null ? result.Form.Controls : new ControlCollection(null), action);
                }

                result.RuleActionName += "FormControl";
            }
            else if (action.ViewGuid == Guid.Empty && string.IsNullOrEmpty(action.Method) && action.ControlGuid != Guid.Empty && action.PanelGuid == Guid.Empty && action.ObjectGuid == Guid.Empty && action.Parameters.Count > 0 && action.Parameters[0].TargetID.ToUpperInvariant() == "ISENABLED" && action.Parameters[0].TargetType == MappingTargetType.ControlProperty)
            {
                Mapping lp = action.Parameters[0];

                if (lp.SourceValue.ToUpperInvariant() == "TRUE")
                {
                    result.RuleActionName = "Enable";
                }
                else
                {
                    result.RuleActionName = "Disable";
                }

                if (action.SubFormGuid != Guid.Empty)
                {
                    result.RuleActionName += "Sub";

                    GetSubFormAction(result, action.SubFormGuid, result.Event);

                    if (result.SubItemAction != null)
                    {
                        ResolveExternalForm(result);
                        ResolveControl(result, result.Form != null ? result.Form.Controls : new ControlCollection(null), action);
                        result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                    }
                }
                else
                {
                    ResolveForm(result, result.Event);
                    ResolveControl(result, result.Form != null ? result.Form.Controls : new ControlCollection(null), action);
                }

                result.RuleActionName += "FormControl";
            }
            else if (action.ViewGuid == Guid.Empty && string.IsNullOrEmpty(action.Method) && action.ControlGuid == Guid.Empty && action.PanelGuid != Guid.Empty && action.ObjectGuid == Guid.Empty && action.Parameters.Count > 0 && action.Parameters[0].TargetType == MappingTargetType.PanelProperty)
            {
                if (action.SubFormGuid != Guid.Empty)
                {
                    result.RuleActionName += "SubForm";
                    GetSubFormAction(result, action.SubFormGuid, result.Event);

                    if (result.SubItemAction != null)
                    {
                        ResolveExternalForm(result);
                        ResolvePanel(result, action);
                        result.EventFriendlyName = GetEventFriendlyNameForSubForm(result.SubItemAction);
                    }
                }
                else
                {
                    ResolveForm(result, result.Event);
                    ResolvePanel(result, action);
                }

                if (action.Parameters[0].SourceValue.ToUpperInvariant() == "HIDE")
                {
                    result.RuleActionName += "HidePanel";
                }
                else
                {
                    result.RuleActionName += "ShowPanel";
                }
            }
        }

        private void AnnotateNodeAndRule(XmlNode node, Context context, WSF.ValidationResult validationResult)
        {
            node.Attributes.Append(node.OwnerDocument.CreateAttribute("Invalid"));
            node.Attributes["Invalid"].Value = "true";
            node.Attributes.Append(node.OwnerDocument.CreateAttribute("ValidationMessage"));
            StringBuilder messages = new StringBuilder();
            foreach (WSF.ValidationError error in validationResult.Messages)
            {
                ValidationMessageParts parts = new ValidationMessageParts(error);
                if (parts.RefStatus == ReferenceStatus.Missing)
                {
                    if (messages.Length != 0)
                    {
                        messages.Append(";");
                    }

                    messages.Append(error.Message);
                }
            }

            node.Attributes["ValidationMessage"].Value = messages.ToString();
            XmlNode ruleNode = node.OwnerDocument.SelectSingleNode(string.Format("./Rules/Rule[@ID='{0}']", context.EventGuid));
            ruleNode.Attributes.Append(node.OwnerDocument.CreateAttribute("Invalid"));
            ruleNode.Attributes["Invalid"].Value = "true";
        }

        #region Event Builders
        private void BuildFormWorkflowViewEvent(XmlNode eventNode, Context context)
        {
            XmlNode partsNode = eventNode.OwnerDocument.CreateElement("Parts");
            eventNode.AppendChild(partsNode);

            eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("Name"));
            eventNode.Attributes["Name"].Value = context.RuleEventName;

            ValidatePartValues(partsNode, context);
        }

        private void BuildViewWorkflowViewEvent(XmlNode eventNode, Context context)
        {
            XmlNode partsNode = eventNode.OwnerDocument.CreateElement("Parts");
            eventNode.AppendChild(partsNode);

            eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("Name"));
            eventNode.Attributes["Name"].Value = context.RuleEventName;

            XmlNode viewPartNode = BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(context.InstanceGuid), GetGuidString(context.SubformGuid), context.viewName, null, "View", viewPartNode);
            partsNode.AppendChild(viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubViewWorkflowViewEvent(XmlNode eventNode, Context context)
        {
            XmlNode partsNode = eventNode.OwnerDocument.CreateElement("Parts");
            eventNode.AppendChild(partsNode);

            eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("Name"));
            eventNode.Attributes["Name"].Value = context.RuleEventName;

            XmlNode viewPartNode = BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, GetEventFriendlyNameForSubForm(context.Event)), "View");
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(context.InstanceGuid), GetGuidString(context.SubformGuid), context.viewName, null, "View", viewPartNode);
            partsNode.AppendChild(viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormWorkflowViewEvent(XmlNode eventNode, Context context)
        {
            XmlNode partsNode = eventNode.OwnerDocument.CreateElement("Parts");
            eventNode.AppendChild(partsNode);

            eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("Name"));
            eventNode.Attributes["Name"].Value = context.RuleEventName;

            XmlNode formPartNode = BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, GetEventFriendlyNameForSubForm(context.Event)), "Form");
            Guid instanceIdToUse = context.SubItemAction != null ? context.SubItemAction.InstanceGuid : context.InstanceGuid;
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(instanceIdToUse), GetGuidString(context.SubformGuid), context.formName, null, "Form", formPartNode);
            partsNode.AppendChild(formPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormViewWorkflowViewEvent(XmlNode eventNode, Context context)
        {
            XmlNode partsNode = eventNode.OwnerDocument.CreateElement("Parts");
            eventNode.AppendChild(partsNode);

            eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("Name"));
            eventNode.Attributes["Name"].Value = context.RuleEventName;

            XmlNode formPartNode = BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, GetEventFriendlyNameForSubForm(context.Event)), "Form");
            Guid instanceIdToUse = context.SubItemAction != null ? context.SubItemAction.InstanceGuid : context.InstanceGuid;
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(instanceIdToUse), GetGuidString(context.SubformGuid), context.formName, null, "Form", formPartNode);
            partsNode.AppendChild(formPartNode);

            XmlNode viewPartNode = BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(context.InstanceGuid), GetGuidString(context.SubformGuid), context.viewName, null, "View", viewPartNode);
            partsNode.AppendChild(viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildWorkflowActionedEvent(XmlNode eventNode, Context context)
        {
            XmlNode partsNode = eventNode.OwnerDocument.CreateElement("Parts");
            eventNode.AppendChild(partsNode);

            eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("Name"));
            eventNode.Attributes["Name"].Value = context.RuleEventName;

            XmlNode viewPartNode = BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(context.InstanceGuid), GetGuidString(context.SubformGuid), context.viewName, null, "View", viewPartNode);
            partsNode.AppendChild(viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildFormWorkflowActionedEvent(XmlNode eventNode, Context context)
        {
            XmlNode partsNode = eventNode.OwnerDocument.CreateElement("Parts");
            eventNode.AppendChild(partsNode);

            eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("Name"));
            eventNode.Attributes["Name"].Value = context.RuleEventName;

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubViewWorkflowActionedEvent(XmlNode eventNode, Context context)
        {
            XmlNode partsNode = eventNode.OwnerDocument.CreateElement("Parts");
            eventNode.AppendChild(partsNode);

            eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("Name"));
            eventNode.Attributes["Name"].Value = context.RuleEventName;

            XmlNode viewPartNode = BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, GetEventFriendlyNameForSubForm(context.Event)), "View");
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(context.InstanceGuid), GetGuidString(context.SubformGuid), context.viewName, null, "View", viewPartNode);
            partsNode.AppendChild(viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormWorkflowActionedEvent(XmlNode eventNode, Context context)
        {
            XmlNode partsNode = eventNode.OwnerDocument.CreateElement("Parts");
            eventNode.AppendChild(partsNode);

            eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("Name"));
            eventNode.Attributes["Name"].Value = context.RuleEventName;

            XmlNode formPartNode = BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, GetEventFriendlyNameForSubForm(context.Event)), "Form");
            Guid instanceIdToUse = context.SubItemAction != null ? context.SubItemAction.InstanceGuid : context.InstanceGuid;
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(instanceIdToUse), GetGuidString(context.SubformGuid), context.formName, null, "Form", formPartNode);
            partsNode.AppendChild(formPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormViewWorkflowActionedEvent(XmlNode eventNode, Context context)
        {
            XmlNode partsNode = eventNode.OwnerDocument.CreateElement("Parts");
            eventNode.AppendChild(partsNode);

            eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("Name"));
            eventNode.Attributes["Name"].Value = context.RuleEventName;

            XmlNode formPartNode = BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, GetEventFriendlyNameForSubForm(context.Event)), "Form");
            Guid instanceIdToUse = context.SubItemAction != null ? context.SubItemAction.InstanceGuid : context.InstanceGuid;
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(instanceIdToUse), GetGuidString(context.SubformGuid), context.formName, null, "Form", formPartNode);
            partsNode.AppendChild(formPartNode);

            XmlNode viewPartNode = BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(context.InstanceGuid), GetGuidString(context.SubformGuid), context.viewName, null, "View", viewPartNode);
            partsNode.AppendChild(viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildViewWorkflowActionedEvent(XmlNode eventNode, Context context)
        {
            XmlNode partsNode = eventNode.OwnerDocument.CreateElement("Parts");
            eventNode.AppendChild(partsNode);

            eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("Name"));
            eventNode.Attributes["Name"].Value = context.RuleEventName;

            XmlNode viewPartNode = BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(context.InstanceGuid), GetGuidString(context.SubformGuid), context.viewName, null, "View", viewPartNode);
            partsNode.AppendChild(viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildFormEvent(XmlNode eventNode, Context context)
        {
            XmlNode partsNode = eventNode.OwnerDocument.CreateElement("Parts");
            eventNode.AppendChild(partsNode);

            eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("Name"));
            eventNode.Attributes["Name"].Value = "FormEvent";

            partsNode.AppendChild(BuildPartNode(eventNode.OwnerDocument, context.Event.Name, context.methodDisplayName, "FormEvent"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildOtherFormEvent(XmlNode eventNode, Context context)
        {
            eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("Name"));
            eventNode.Attributes["Name"].Value = "OpenedFormCloseEvent";

            XmlNode partsNode = eventNode.OwnerDocument.CreateElement("Parts");
            eventNode.AppendChild(partsNode);

            XmlNode formPartNode = BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, GetEventFriendlyNameForSubForm(context.Event)), "Form");
            partsNode.AppendChild(formPartNode);
            partsNode.AppendChild(BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.formGuid), context.formName, "FormEvent"));
            Guid instanceIdToUse = context.SubItemAction != null ? context.SubItemAction.InstanceGuid : context.InstanceGuid;
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(instanceIdToUse), GetGuidString(context.SubformGuid), context.formName, null, "Form", formPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubformClosedEvent(XmlNode eventNode, Context context)
        {
            XmlNode partsNode = eventNode.OwnerDocument.CreateElement("Parts");
            eventNode.AppendChild(partsNode);

            eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("Name"));
            eventNode.Attributes["Name"].Value = "OpenedViewCloseEvent";

            XmlNode viewPartNode = BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, GetEventFriendlyNameForSubForm(context.Event)), "View");
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(context.InstanceGuid), GetGuidString(context.SubformGuid), context.viewName, null, "View", viewPartNode);
            partsNode.AppendChild(viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubformEvent(XmlNode eventNode, Context context)
        {
            XmlNode partsNode = eventNode.OwnerDocument.CreateElement("Parts");
            eventNode.AppendChild(partsNode);

            eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("Name"));
            eventNode.Attributes["Name"].Value = "SubViewEvent";

            XmlNode viewPartNode = BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, GetEventFriendlyNameForSubForm(context.Event)), "View");
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(context.InstanceGuid), GetGuidString(context.SubformGuid), context.viewName, null, "View", viewPartNode);

            partsNode.AppendChild(viewPartNode);
            partsNode.AppendChild(BuildPartNode(eventNode.OwnerDocument, context.Event.Name, context.methodDisplayName, "ViewMethod"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildFormPopupViewEvent(XmlNode eventNode, Context context)
        {
            XmlNode partsNode = eventNode.OwnerDocument.CreateElement("Parts");
            eventNode.AppendChild(partsNode);

            eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("Name"));
            eventNode.Attributes["Name"].Value = "OpenedFormViewEvent";

            XmlNode formPartNode = BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, GetEventFriendlyNameForSubForm(context.Event)), "Form");
            Guid instanceIdToUse = context.SubItemAction != null ? context.SubItemAction.InstanceGuid : context.InstanceGuid;
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(instanceIdToUse), GetGuidString(context.SubformGuid), context.formName, null, "Form", formPartNode);
            partsNode.AppendChild(formPartNode);

            XmlNode viewPartNode = BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(context.InstanceGuid), GetGuidString(context.SubformGuid), context.viewName, null, "View", viewPartNode);
            partsNode.AppendChild(viewPartNode);

            partsNode.AppendChild(BuildPartNode(eventNode.OwnerDocument, context.Event.Name, context.methodDisplayName, "ViewMethod"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildViewEvent(XmlNode eventNode, Context context)
        {
            XmlNode partsNode = eventNode.OwnerDocument.CreateElement("Parts");
            eventNode.AppendChild(partsNode);

            eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("Name"));
            eventNode.Attributes["Name"].Value = "ViewEvent";

            XmlNode viewPartNode = BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(context.InstanceGuid), GetGuidString(context.SubformGuid), context.viewName, null, "View", viewPartNode);
            partsNode.AppendChild(viewPartNode);

            partsNode.AppendChild(BuildPartNode(eventNode.OwnerDocument, context.Event.Name, context.methodDisplayName, "ViewMethod"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildViewControlEvent(XmlNode eventNode, Context context)
        {
            XmlNode partsNode = eventNode.OwnerDocument.CreateElement("Parts");
            eventNode.AppendChild(partsNode);

            eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("Name"));
            eventNode.Attributes["Name"].Value = "ViewControlEvent";

            XmlNode viewPartNode = BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(context.InstanceGuid), GetGuidString(context.SubformGuid), context.viewName, null, "View", viewPartNode);
            partsNode.AppendChild(viewPartNode);

            partsNode.AppendChild(BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl"));
            partsNode.AppendChild(BuildPartNode(eventNode.OwnerDocument, context.Event.Name, context.EventName, "ControlEvent"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildViewParameterEvent(XmlNode eventNode, Context context)
        {
            XmlNode partsNode = eventNode.OwnerDocument.CreateElement("Parts");
            eventNode.AppendChild(partsNode);

            eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("Name"));
            eventNode.Attributes["Name"].Value = "ViewParameterEvent";

            XmlNode viewPartNode = BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(context.InstanceGuid), GetGuidString(context.SubformGuid), context.viewName, null, "View", viewPartNode);
            partsNode.AppendChild(viewPartNode);

            XmlNode viewParameterNode = BuildPartNode(eventNode.OwnerDocument, context.parameterName, context.parameterName, "ViewParameter");
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(context.InstanceGuid), GetGuidString(context.SubformGuid), context.parameterName, context.parameterGuid, "ViewParameter", viewParameterNode);
            partsNode.AppendChild(viewParameterNode);
            partsNode.AppendChild(BuildPartNode(eventNode.OwnerDocument, context.Event.Name, context.EventName, "ParameterEvent"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildFormViewParameterEvent(XmlNode eventNode, Context context)
        {
            XmlNode partsNode = eventNode.OwnerDocument.CreateElement("Parts");
            eventNode.AppendChild(partsNode);

            eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("Name"));
            eventNode.Attributes["Name"].Value = "FormViewParameterEvent";

            XmlNode viewPartNode = BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(context.InstanceGuid), GetGuidString(context.SubformGuid), context.viewName, null, "View", viewPartNode);
            partsNode.AppendChild(viewPartNode);

            XmlNode viewParameterNode = BuildPartNode(eventNode.OwnerDocument, context.parameterName, context.parameterName, "ViewParameter");
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(context.InstanceGuid), GetGuidString(context.SubformGuid), context.parameterName, context.parameterGuid, "ViewParameter", viewParameterNode);
            partsNode.AppendChild(viewParameterNode);
            partsNode.AppendChild(BuildPartNode(eventNode.OwnerDocument, context.Event.Name, context.EventName, "ParameterEvent"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildFormParameterEvent(XmlNode eventNode, Context context)
        {
            XmlNode partsNode = eventNode.OwnerDocument.CreateElement("Parts");
            eventNode.AppendChild(partsNode);

            eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("Name"));
            eventNode.Attributes["Name"].Value = "FormParameterEvent";

            XmlNode formParameterNode = BuildPartNode(eventNode.OwnerDocument, context.parameterName, context.parameterName, "FormParameter");
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(context.InstanceGuid), GetGuidString(context.SubformGuid), context.parameterName, context.parameterGuid, "FormParameter", formParameterNode);
            partsNode.AppendChild(formParameterNode);
            partsNode.AppendChild(BuildPartNode(eventNode.OwnerDocument, context.Event.Name, context.EventName, "ParameterEvent"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubViewParameterEvent(XmlNode eventNode, Context context)
        {
            XmlNode partsNode = eventNode.OwnerDocument.CreateElement("Parts");
            eventNode.AppendChild(partsNode);

            eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("Name"));
            eventNode.Attributes["Name"].Value = "SubViewParameterEvent";

            XmlNode viewPartNode = BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, GetEventFriendlyNameForSubForm(context.Event)), "View");
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(context.InstanceGuid), GetGuidString(context.SubformGuid), context.viewName, null, "View", viewPartNode);
            partsNode.AppendChild(viewPartNode);

            XmlNode viewParameterNode = BuildPartNode(eventNode.OwnerDocument, context.parameterName, context.parameterName, "ViewParameter");
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(context.InstanceGuid), GetGuidString(context.SubformGuid), context.parameterName, context.parameterGuid, "ViewParameter", viewParameterNode);
            partsNode.AppendChild(viewParameterNode);
            partsNode.AppendChild(BuildPartNode(eventNode.OwnerDocument, context.Event.Name, context.EventName, "ParameterEvent"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormParameterEvent(XmlNode eventNode, Context context)
        {
            XmlNode partsNode = eventNode.OwnerDocument.CreateElement("Parts");
            eventNode.AppendChild(partsNode);

            eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("Name"));
            eventNode.Attributes["Name"].Value = "SubFormParameterEvent";

            XmlNode formPartNode = BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, GetEventFriendlyNameForSubForm(context.Event)), "Form");
            Guid instanceIdToUse = context.SubItemAction != null ? context.SubItemAction.InstanceGuid : context.InstanceGuid;
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(instanceIdToUse), GetGuidString(context.SubformGuid), context.formName, null, "Form", formPartNode);
            partsNode.AppendChild(formPartNode);

            XmlNode formParameterNode = BuildPartNode(eventNode.OwnerDocument, context.parameterName, context.parameterName, "FormParameter");
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(context.InstanceGuid), GetGuidString(context.SubformGuid), context.parameterName, context.parameterGuid, "FormParameter", formParameterNode);
            partsNode.AppendChild(formParameterNode);
            partsNode.AppendChild(BuildPartNode(eventNode.OwnerDocument, context.Event.Name, context.EventName, "ParameterEvent"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormViewParameterEvent(XmlNode eventNode, Context context)
        {
            XmlNode partsNode = eventNode.OwnerDocument.CreateElement("Parts");
            eventNode.AppendChild(partsNode);

            eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("Name"));
            eventNode.Attributes["Name"].Value = "SubFormViewParameterEvent";

            XmlNode formPartNode = BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, GetEventFriendlyNameForSubForm(context.Event)), "Form");
            Guid instanceIdToUse = context.SubItemAction != null ? context.SubItemAction.InstanceGuid : context.InstanceGuid;
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(instanceIdToUse), GetGuidString(context.SubformGuid), context.formName, null, "Form", formPartNode);
            partsNode.AppendChild(formPartNode);

            XmlNode viewPartNode = BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(context.InstanceGuid), GetGuidString(context.SubformGuid), context.viewName, null, "View", viewPartNode);
            partsNode.AppendChild(viewPartNode);

            XmlNode viewParameterNode = BuildPartNode(eventNode.OwnerDocument, context.parameterName, context.parameterName, "ViewParameter");
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(context.InstanceGuid), GetGuidString(context.SubformGuid), context.parameterName, context.parameterGuid, "ViewParameter", viewParameterNode);
            partsNode.AppendChild(viewParameterNode);

            partsNode.AppendChild(BuildPartNode(eventNode.OwnerDocument, context.Event.Name, context.EventName, "ParameterEvent"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildFormControlEvent(XmlNode eventNode, Context context)
        {
            XmlNode partsNode = eventNode.OwnerDocument.CreateElement("Parts");
            eventNode.AppendChild(partsNode);

            eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("Name"));
            eventNode.Attributes["Name"].Value = "FormControlEvent";

            partsNode.AppendChild(BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "FormControl"));
            partsNode.AppendChild(BuildPartNode(eventNode.OwnerDocument, context.Event.Name, context.EventName, "ControlEvent"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildFormViewControlEvent(XmlNode eventNode, Context context)
        {
            XmlNode partsNode = eventNode.OwnerDocument.CreateElement("Parts");
            eventNode.AppendChild(partsNode);

            eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("Name"));
            eventNode.Attributes["Name"].Value = "ViewControlEvent";

            XmlNode viewPartNode = BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(context.InstanceGuid), GetGuidString(context.SubformGuid), context.viewName, null, "View", viewPartNode);
            partsNode.AppendChild(viewPartNode);

            partsNode.AppendChild(BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl"));
            partsNode.AppendChild(BuildPartNode(eventNode.OwnerDocument, context.Event.Name, context.EventName, "ControlEvent"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildFormOpenedControlEvent(XmlNode eventNode, Context context)
        {
            XmlNode partsNode = eventNode.OwnerDocument.CreateElement("Parts");
            eventNode.AppendChild(partsNode);

            eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("Name"));
            eventNode.Attributes["Name"].Value = "OpenedFormControlEvent";

            XmlNode formPartNode = BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, GetEventFriendlyNameForSubForm(context.Event)), "Form");
            partsNode.AppendChild(formPartNode);
            partsNode.AppendChild(BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "FormControl"));
            partsNode.AppendChild(BuildPartNode(eventNode.OwnerDocument, context.Event.Name, context.EventName, "ControlEvent"));
            Guid instanceIdToUse = context.SubItemAction != null ? context.SubItemAction.InstanceGuid : context.InstanceGuid;
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(instanceIdToUse), GetGuidString(context.SubformGuid), context.formName, null, "Form", formPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubformViewControlEvent(XmlNode eventNode, Context context)
        {
            XmlNode partsNode = eventNode.OwnerDocument.CreateElement("Parts");
            eventNode.AppendChild(partsNode);

            eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("Name"));
            eventNode.Attributes["Name"].Value = "SubViewControlEvent";

            XmlNode viewPartNode = BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, GetEventFriendlyNameForSubForm(context.Event)), "View");
            partsNode.AppendChild(viewPartNode);
            partsNode.AppendChild(BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl"));
            partsNode.AppendChild(BuildPartNode(eventNode.OwnerDocument, context.Event.Name, context.EventName, "ControlEvent"));
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(context.InstanceGuid), GetGuidString(context.SubformGuid), context.viewName, null, "View", viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildFormPopupViewControlEvent(XmlNode eventNode, Context context)
        {
            XmlNode partsNode = eventNode.OwnerDocument.CreateElement("Parts");
            eventNode.AppendChild(partsNode);

            eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("Name"));
            eventNode.Attributes["Name"].Value = "OpenedFormViewControlEvent";

            XmlNode formPartNode = BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, GetEventFriendlyNameForSubForm(context.Event)), "Form");
            Guid instanceIdToUse = context.SubItemAction != null ? context.SubItemAction.InstanceGuid : context.InstanceGuid;
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(instanceIdToUse), GetGuidString(context.SubformGuid), context.formName, null, "Form", formPartNode);
            partsNode.AppendChild(formPartNode);

            XmlNode viewPartNode = BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(context.InstanceGuid), GetGuidString(context.SubformGuid), context.viewName, null, "View", viewPartNode);
            partsNode.AppendChild(viewPartNode);

            partsNode.AppendChild(BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl"));
            partsNode.AppendChild(BuildPartNode(eventNode.OwnerDocument, context.Event.Name, context.EventName, "ControlEvent"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildOpenedFormEvent(XmlNode eventNode, Context context)
        {
            eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("Name"));
            eventNode.Attributes["Name"].Value = "OpenedFormEvent";

            XmlNode partsNode = eventNode.OwnerDocument.CreateElement("Parts");
            eventNode.AppendChild(partsNode);

            XmlNode formPartNode = BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, GetEventFriendlyNameForSubForm(context.Event)), "Form");
            partsNode.AppendChild(formPartNode);
            partsNode.AppendChild(BuildPartNode(eventNode.OwnerDocument, context.Event.Name, context.methodDisplayName, "FormEvent"));
            Guid instanceIdToUse = context.SubItemAction != null ? context.SubItemAction.InstanceGuid : context.InstanceGuid;
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(instanceIdToUse), GetGuidString(context.SubformGuid), context.formName, null, "Form", formPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildEventlessRule(XmlNode eventNode, Context context)
        {
            eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("Name"));
            eventNode.Attributes["Name"].Value = "Rule";

            XmlNode partsNode = eventNode.OwnerDocument.CreateElement("Parts");
            eventNode.AppendChild(partsNode);

            if (context.View != null)
            {
                XmlNode viewPartNode = BuildPartNode(
                    eventNode.OwnerDocument,
                    GetGuidString(context.viewGuid),
                    context.viewMviName,
                    "View"
                );

                BuildPartDataNode(
                    eventNode.OwnerDocument,
                    GetGuidString(context.InstanceGuid),
                    GetGuidString(context.SubformGuid),
                    context.viewName,
                    null,
                    "View",
                    viewPartNode
                );

                partsNode.AppendChild(viewPartNode);
            }

            if (context.Form != null)
            {
                XmlNode formPartNode = BuildPartNode(
                    eventNode.OwnerDocument,
                    GetGuidString(context.formGuid),
                    context.formName,
                    "Form"
                );

                Guid instanceIdToUse = context.SubItemAction != null ? context.SubItemAction.InstanceGuid : context.InstanceGuid;

                BuildPartDataNode(
                    eventNode.OwnerDocument,
                    GetGuidString(instanceIdToUse),
                    GetGuidString(context.SubformGuid),
                    context.formName,
                    null,
                    "Form",
                    formPartNode
                );

                partsNode.AppendChild(formPartNode);
            }

            ValidatePartValues(partsNode, context);
        }

        private void BuildFormServerPreRenderEvent(XmlNode eventNode, Context context)
        {
            XmlNode partsNode = eventNode.OwnerDocument.CreateElement("Parts");
            eventNode.AppendChild(partsNode);

            eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("Name"));
            eventNode.Attributes["Name"].Value = "FormServerPreRenderEvent";

            partsNode.AppendChild(BuildPartNode(eventNode.OwnerDocument, context.Event.Name, context.methodDisplayName, "FormServerPreRenderEvent"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildViewServerPreRenderEvent(XmlNode eventNode, Context context)
        {
            XmlNode partsNode = eventNode.OwnerDocument.CreateElement("Parts");
            eventNode.AppendChild(partsNode);

            eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("Name"));
            eventNode.Attributes["Name"].Value = "ViewServerPreRenderEvent";

            partsNode.AppendChild(BuildPartNode(eventNode.OwnerDocument, context.Event.Name, context.methodDisplayName, "ViewServerPreRenderEvent"));

            XmlNode viewPartNode = BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(context.InstanceGuid), GetGuidString(context.SubformGuid), context.viewName, null, "View", viewPartNode);
            partsNode.AppendChild(viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubViewServerPreRenderEvent(XmlNode eventNode, Context context)
        {
            XmlNode partsNode = eventNode.OwnerDocument.CreateElement("Parts");
            eventNode.AppendChild(partsNode);

            eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("Name"));
            eventNode.Attributes["Name"].Value = "SubViewServerPreRenderEvent";

            partsNode.AppendChild(BuildPartNode(eventNode.OwnerDocument, context.Event.Name, context.methodDisplayName, "SubViewServerPreRenderEvent"));

            XmlNode viewPartNode = BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, GetEventFriendlyNameForSubForm(context.Event)), "View");
            partsNode.AppendChild(viewPartNode);
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(context.InstanceGuid), GetGuidString(context.SubformGuid), context.viewName, null, "View", viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormServerPreRenderEvent(XmlNode eventNode, Context context)
        {
            XmlNode partsNode = eventNode.OwnerDocument.CreateElement("Parts");
            eventNode.AppendChild(partsNode);

            eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("Name"));
            eventNode.Attributes["Name"].Value = "SubFormServerPreRenderEvent";

            partsNode.AppendChild(BuildPartNode(eventNode.OwnerDocument, context.Event.Name, context.methodDisplayName, "SubFormServerPreRenderEvent"));

            XmlNode formPartNode = BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, GetEventFriendlyNameForSubForm(context.Event)), "Form");
            Guid instanceIdToUse = context.SubItemAction != null ? context.SubItemAction.InstanceGuid : context.InstanceGuid;
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(instanceIdToUse), GetGuidString(context.SubformGuid), context.formName, null, "Form", formPartNode);
            partsNode.AppendChild(formPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormViewServerPreRenderEvent(XmlNode eventNode, Context context)
        {
            XmlNode partsNode = eventNode.OwnerDocument.CreateElement("Parts");
            eventNode.AppendChild(partsNode);

            eventNode.Attributes.Append(eventNode.OwnerDocument.CreateAttribute("Name"));
            eventNode.Attributes["Name"].Value = "SubFormViewServerPreRenderEvent";

            partsNode.AppendChild(BuildPartNode(eventNode.OwnerDocument, context.Event.Name, context.methodDisplayName, "SubFormViewServerPreRenderEvent"));

            XmlNode formPartNode = BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, GetEventFriendlyNameForSubForm(context.Event)), "Form");
            Guid instanceIdToUse = context.SubItemAction != null ? context.SubItemAction.InstanceGuid : context.InstanceGuid;
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(instanceIdToUse), GetGuidString(context.SubformGuid), context.formName, null, "Form", formPartNode);
            partsNode.AppendChild(formPartNode);

            XmlNode viewPartNode = BuildPartNode(eventNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            BuildPartDataNode(eventNode.OwnerDocument, GetGuidString(context.InstanceGuid), GetGuidString(context.SubformGuid), context.viewName, null, "View", viewPartNode);
            partsNode.AppendChild(viewPartNode);

            ValidatePartValues(partsNode, context);
        }
        #endregion

        #region Handler Builders
        private void BuildIfHandler(Handler handler, XmlNode handlerNode, Context context)
        { /*do nothing yet*/ }

        private void BuildElseHandler(Handler handler, XmlNode handlerNode, Context context)
        { /*do nothing yet*/ }

        private void BuildErrorHandler(Handler handler, XmlNode handlerNode, Context context)
        { /*do nothing yet*/ }

        private void BuildFunctionHandler(Handler handler, XmlNode handlerNode, Context context)
        {
            XmlDocument ownerDocument = handlerNode.OwnerDocument;
            XmlNode partsNode = ownerDocument.CreateElement("Parts");
            handlerNode.AppendChild(partsNode);

            if (!context.formGuid.Equals(Guid.Empty))
            {
                string formName = context.formName;
                if (context.SubformGuid != Guid.Empty)
                {
                    formName = string.Format(Resources.RuleHelper.SubFormPartDisplayName, formName, context.EventFriendlyName);
                }

                XmlNode formPartNode = BuildPartNode(ownerDocument, GetGuidString(context.formGuid), formName, "Form");
                if (context.SubformGuid != Guid.Empty)
                {
                    BuildPartSubFormDataNode(ownerDocument, GetGuidString(context.SubformGuid), GetGuidString(context.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);
                }

                partsNode.AppendChild(formPartNode);
            }

            string viewName = context.viewMviName;
            if (!context.SubformGuid.Equals(Guid.Empty) && context.formGuid.Equals(Guid.Empty))
            {
                viewName = string.Format(Resources.RuleHelper.SubFormPartDisplayName, viewName, context.EventFriendlyName);
            }

            XmlNode viewPartNode = BuildPartNode(ownerDocument, GetGuidString(context.viewGuid), viewName, "View");
            BuildPartSubFormDataNode(ownerDocument, GetGuidString(context.SubformGuid), GetGuidString(context.InstanceGuid), context.viewName, "View", GetGuidString(context.viewGuid), viewPartNode);
            partsNode.AppendChild(viewPartNode);

            if (!string.IsNullOrEmpty(context.controlName))
            {
                partsNode.AppendChild(BuildPartNode(ownerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl"));
            }

            partsNode.AppendChild(BuildPartNode(ownerDocument, context.itemState.ToString(), context.itemState.ToString(), "ItemStates"));

            ValidatePartValues(partsNode, context);
        }
        #endregion

        #region Condition Builders
        private void BuildAdvancedCondition(LogicalExpression lp, XmlNode conditionNode, Condition condition, Context context)
        {
            string value = lp.ToXml().ToString();

            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            XmlNode defaultConditionsNode = conditionNode.OwnerDocument.CreateElement("Conditions");
            defaultConditionsNode.InnerXml = value;

            if (condition.InstanceGuid != Guid.Empty)
            {
                conditionNode.Attributes.Append(conditionNode.OwnerDocument.CreateAttribute("InstanceID"));
                conditionNode.Attributes["InstanceID"].Value = condition.InstanceGuid.ToString().ToLowerInvariant();
            }

            XmlNode advancedConditionNode = BuildPartNode(conditionNode.OwnerDocument, defaultConditionsNode.OuterXml.ToString(), Resources.Rules.AdvancedCondition, "ConfigureCondition");
            if ((condition.Validation.Status & Framework.ValidationStatus.Error) == Framework.ValidationStatus.Error)
            {
                advancedConditionNode.Attributes.Append(conditionNode.OwnerDocument.CreateAttribute("Invalid"));
                advancedConditionNode.Attributes["Invalid"].Value = "true";
            }

            partsNode.AppendChild(advancedConditionNode);
        }

        private void BuildViewRenderModeCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.renderMode, context.renderMode, "RenderMode"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildFormRenderModeCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.renderMode, context.renderMode, "RenderMode"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubViewRenderModeCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            XmlNode viewPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.viewGuid),
                string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, context.EventFriendlyName), "View");
            BuildPartSubFormDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.SubFormGuid),
                GetGuidString(context.Condition.InstanceGuid),
                context.viewName, "View", GetGuidString(context.viewGuid), viewPartNode);

            partsNode.AppendChild(viewPartNode);
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.renderMode, context.renderMode, "RenderMode"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildFormViewRenderModeCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            XmlNode viewPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            BuildPartDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.InstanceGuid),
                GetGuidString(context.Condition.SubFormGuid), context.viewName, null, "View", viewPartNode);

            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.renderMode, context.renderMode, "RenderMode"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormRenderModeCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            XmlNode formPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.formGuid),
                string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            BuildPartSubFormDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.SubFormGuid),
                GetGuidString(context.Condition.InstanceGuid),
                context.formName, "Form", GetGuidString(context.formGuid), formPartNode);

            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.renderMode, context.renderMode, "RenderMode"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormViewRenderModeCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            XmlNode formPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.formGuid),
                string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            BuildPartSubFormDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.SubFormGuid),
                GetGuidString(context.Condition.InstanceGuid),
                context.formName, "Form", GetGuidString(context.formGuid), formPartNode);

            XmlNode viewPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            BuildPartDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.InstanceGuid),
                GetGuidString(context.Condition.SubFormGuid), context.viewName, null, "View", viewPartNode);

            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.renderMode, context.renderMode, "RenderMode"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSimpleEqualControlCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            XmlNode viewPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            BuildPartDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.InstanceGuid), GetGuidString(context.Condition.SubFormGuid), context.viewName, null, "View", viewPartNode);

            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.Operator, context.Operator, "Operator"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.OperatorValue, context.OperatorValue, "ValueInput"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormViewSimpleViewParameterCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            XmlNode formPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            BuildPartSubFormDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.SubFormGuid), GetGuidString(context.Condition.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);

            XmlNode viewPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            BuildPartDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.InstanceGuid), GetGuidString(context.Condition.SubFormGuid), context.viewName, null, "View", viewPartNode);

            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.parameterName, context.parameterDisplayName, "ViewParameter"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.Operator, context.Operator, "Operator"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildFormViewSimpleViewParameterCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            XmlNode viewPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            BuildPartDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.InstanceGuid), GetGuidString(context.Condition.SubFormGuid), context.viewName, null, "View", viewPartNode);

            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.parameterName, context.parameterDisplayName, "ViewParameter"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.Operator, context.Operator, "Operator"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSimpleEqualFormControlCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "FormControl"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.Operator, context.Operator, "Operator"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.OperatorValue, context.OperatorValue, "ValueInput"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubViewSimpleEqualControlCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            XmlNode viewPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, context.EventFriendlyName), "View");
            BuildPartSubFormDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.SubFormGuid), GetGuidString(context.Condition.InstanceGuid), context.viewName, "View", GetGuidString(context.viewGuid), viewPartNode);

            partsNode.AppendChild(viewPartNode);
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.Operator, context.Operator, "Operator"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.OperatorValue, context.OperatorValue, "ValueInput"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormSimpleEqualControlCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            XmlNode formPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            BuildPartSubFormDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.SubFormGuid), GetGuidString(context.Condition.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);

            XmlNode viewPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            BuildPartDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.InstanceGuid), GetGuidString(context.Condition.SubFormGuid), context.viewName, null, "View", viewPartNode);

            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.Operator, context.Operator, "Operator"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.OperatorValue, context.OperatorValue, "ValueInput"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormSimpleEqualFormControlCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            XmlNode formPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            BuildPartSubFormDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.SubFormGuid), GetGuidString(context.Condition.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "FormControl"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.Operator, context.Operator, "Operator"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.OperatorValue, context.OperatorValue, "ValueInput"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSimpleNotEqualControlCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            XmlNode viewPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            BuildPartDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.InstanceGuid), GetGuidString(context.Condition.SubFormGuid), context.viewName, null, "View", viewPartNode);

            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.Operator, context.Operator, "Operator"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.OperatorValue, context.OperatorValue, "ValueInput"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSimpleNotEqualFormControlCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "FormControl"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.Operator, context.Operator, "Operator"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.OperatorValue, context.OperatorValue, "ValueInput"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubViewSimpleNotEqualControlCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            XmlNode viewPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, context.EventFriendlyName), "View");
            BuildPartSubFormDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.SubFormGuid), GetGuidString(context.Condition.InstanceGuid), context.viewName, "View", GetGuidString(context.viewGuid), viewPartNode);

            partsNode.AppendChild(viewPartNode);
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.Operator, context.Operator, "Operator"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.OperatorValue, context.OperatorValue, "ValueInput"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormSimpleNotEqualControlCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            XmlNode formPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            BuildPartSubFormDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.SubFormGuid), GetGuidString(context.Condition.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);

            XmlNode viewPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            BuildPartDataNode(conditionNode.OwnerDocument, context.Condition.InstanceGuid.ToString(), GetGuidString(context.Condition.SubFormGuid), context.viewName, null, "View", viewPartNode);

            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.Operator, context.Operator, "Operator"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.OperatorValue, context.OperatorValue, "ValueInput"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormSimpleNotEqualFormControlCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            XmlNode formPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            BuildPartSubFormDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.SubFormGuid), GetGuidString(context.Condition.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "FormControl"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.Operator, context.Operator, "Operator"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.OperatorValue, context.OperatorValue, "ValueInput"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSimpleBlankControlCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            XmlNode viewPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            BuildPartDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.InstanceGuid), GetGuidString(context.Condition.SubFormGuid), context.viewName, null, "View", viewPartNode);

            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.Operator, context.Operator, "Operator"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSimpleBlankFormControlCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "FormControl"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.Operator, context.Operator, "Operator"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSimpleBlankFormParameterCondition(XmlNode conditionNode, Context context, bool isViewParameter = false)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            if (isViewParameter)
            {
                partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.parameterName, context.parameterDisplayName, "ViewParameter"));

                XmlNode viewPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
                partsNode.AppendChild(viewPartNode);

                BuildPartDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.InstanceGuid), GetGuidString(context.Condition.SubFormGuid), context.viewName, null, "View", viewPartNode);
            }
            else
            {
                partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.parameterName, context.parameterDisplayName, "FormParameter"));
            }

            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.Operator, context.Operator, "Operator"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSimpleViewParameterCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            XmlNode viewPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            BuildPartDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.InstanceGuid), GetGuidString(context.Condition.SubFormGuid), context.viewName, null, "View", viewPartNode);

            XmlNode viewParameterPartNode = BuildPartNode(conditionNode.OwnerDocument, context.parameterName, context.parameterDisplayName, "ViewParameter");
            BuildPartDataNode(conditionNode.OwnerDocument, context.InstanceGuid.ToString(), context.SubformGuid.ToString(), context.parameterName, context.parameterGuid, "ViewParameter", context.parameterDataType, viewParameterPartNode);
            partsNode.AppendChild(viewParameterPartNode);

            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.Operator, context.Operator, "Operator"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.OperatorValue, context.OperatorValue, "ValueInput"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubViewSimpleViewParameterCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            XmlNode viewPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, context.EventFriendlyName), "View");
            BuildPartSubFormDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.SubFormGuid), GetGuidString(context.Condition.InstanceGuid), context.viewName, "View", GetGuidString(context.viewGuid), viewPartNode);
            partsNode.AppendChild(viewPartNode);

            XmlNode viewParameterPartNode = BuildPartNode(conditionNode.OwnerDocument, context.parameterName, context.parameterDisplayName, "ViewParameter");
            BuildPartDataNode(conditionNode.OwnerDocument, context.InstanceGuid.ToString(), context.SubformGuid.ToString(), context.parameterName, context.parameterGuid, "ViewParameter", context.parameterDataType, viewParameterPartNode);
            partsNode.AppendChild(viewParameterPartNode);

            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.OperatorValue, context.OperatorValue, "ValueInput"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.Operator, context.Operator, "Operator"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSimpleFormParameterCondition(XmlNode conditionNode, Context context, bool isViewParameter = false)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            XmlNode formParameterPartNode = BuildPartNode(conditionNode.OwnerDocument, context.parameterName, context.parameterDisplayName, "FormParameter");
            BuildPartDataNode(conditionNode.OwnerDocument, context.InstanceGuid.ToString(), context.SubformGuid.ToString(), context.parameterName, context.parameterGuid, "FormParameter", context.parameterDataType, formParameterPartNode);
            partsNode.AppendChild(formParameterPartNode);

            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.OperatorValue, context.OperatorValue, "ValueInput"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.Operator, context.Operator, "Operator"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormViewSimpleEqualViewParameterCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            XmlNode formPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            BuildPartSubFormDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.SubFormGuid), GetGuidString(context.Condition.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);
            partsNode.AppendChild(formPartNode);

            XmlNode viewPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            BuildPartDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.InstanceGuid), GetGuidString(context.Condition.SubFormGuid), context.viewName, null, "View", viewPartNode);
            partsNode.AppendChild(viewPartNode);

            XmlNode viewParameterPartNode = BuildPartNode(conditionNode.OwnerDocument, context.parameterName, context.parameterDisplayName, "ViewParameter");
            BuildPartDataNode(conditionNode.OwnerDocument, context.InstanceGuid.ToString(), context.SubformGuid.ToString(), context.parameterName, context.parameterGuid, "ViewParameter", context.parameterDataType, viewParameterPartNode);
            partsNode.AppendChild(viewParameterPartNode);

            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.OperatorValue, context.OperatorValue, "ValueInput"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.Operator, context.Operator, "Operator"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormSimpleEqualFormParameterCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            XmlNode formPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            BuildPartSubFormDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.SubFormGuid), GetGuidString(context.Condition.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);
            partsNode.AppendChild(formPartNode);

            XmlNode formParameterPartNode = BuildPartNode(conditionNode.OwnerDocument, context.parameterName, context.parameterDisplayName, "FormParameter");
            BuildPartDataNode(conditionNode.OwnerDocument, context.InstanceGuid.ToString(), context.SubformGuid.ToString(), context.parameterName, context.parameterGuid, "FormParameter", context.parameterDataType, formParameterPartNode);
            partsNode.AppendChild(formParameterPartNode);

            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.OperatorValue, context.OperatorValue, "ValueInput"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.Operator, context.Operator, "Operator"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubViewSimpleBlankControlCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            XmlNode viewPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, context.EventFriendlyName), "View");
            BuildPartSubFormDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.SubFormGuid), GetGuidString(context.Condition.InstanceGuid), context.viewName, "View", GetGuidString(context.viewGuid), viewPartNode);

            partsNode.AppendChild(viewPartNode);
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.Operator, context.Operator, "Operator"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormSimpleBlankControlCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            XmlNode formPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            BuildPartSubFormDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.SubFormGuid), GetGuidString(context.Condition.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);

            XmlNode viewPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            BuildPartDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.InstanceGuid), GetGuidString(context.Condition.SubFormGuid), context.viewName, null, "View", viewPartNode);

            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.Operator, context.Operator, "Operator"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormSimpleBlankFormControlCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            XmlNode formPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            BuildPartSubFormDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.SubFormGuid), GetGuidString(context.Condition.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "FormControl"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.Operator, context.Operator, "Operator"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubViewSimpleBlankViewParameterCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            XmlNode viewPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, context.EventFriendlyName), "View");
            partsNode.AppendChild(viewPartNode);

            BuildPartSubFormDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.SubFormGuid), GetGuidString(context.Condition.InstanceGuid), context.viewName, "View", GetGuidString(context.viewGuid), viewPartNode);

            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.parameterName, context.parameterDisplayName, "ViewParameter"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.Operator, context.Operator, "Operator"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormSimpleBlankFormParameterCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            XmlNode formPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            BuildPartSubFormDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.SubFormGuid), GetGuidString(context.Condition.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);

            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.parameterName, context.parameterDisplayName, "FormParameter"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.Operator, context.Operator, "Operator"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSimpleNotBlankControlCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            XmlNode viewPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            BuildPartDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.InstanceGuid), GetGuidString(context.Condition.SubFormGuid), context.viewName, null, "View", viewPartNode);

            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.Operator, context.Operator, "Operator"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSimpleNotBlankFormControlCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "FormControl"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.Operator, context.Operator, "Operator"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSimpleNotBlankFormParameterCondition(XmlNode conditionNode, Context context, bool viewParameter = false)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            if (viewParameter)
            {
                partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.parameterName, context.parameterDisplayName, "ViewParameter"));

                XmlNode viewPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
                partsNode.AppendChild(viewPartNode);

                BuildPartDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.InstanceGuid), GetGuidString(context.Condition.SubFormGuid), context.viewName, null, "View", viewPartNode);
            }
            else
            {
                partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.parameterName, context.parameterName, "FormParameter"));
            }

            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.Operator, context.Operator, "Operator"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubViewSimpleNotBlankControlCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            XmlNode viewPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, context.EventFriendlyName), "View");
            BuildPartSubFormDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.SubFormGuid), GetGuidString(context.Condition.InstanceGuid), context.viewName, "View", GetGuidString(context.viewGuid), viewPartNode);

            partsNode.AppendChild(viewPartNode);
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.Operator, context.Operator, "Operator"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormSimpleNotBlankControlCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            XmlNode formPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            BuildPartSubFormDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.SubFormGuid), GetGuidString(context.Condition.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);

            XmlNode viewPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            BuildPartDataNode(conditionNode.OwnerDocument, context.Condition.InstanceGuid.ToString(), GetGuidString(context.Condition.SubFormGuid), context.viewName, null, "View", viewPartNode);

            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.Operator, context.Operator, "Operator"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormSimpleNotBlankFormControlCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            XmlNode formPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            BuildPartSubFormDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.SubFormGuid), GetGuidString(context.Condition.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "FormControl"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.Operator, context.Operator, "Operator"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubViewSimpleNotBlankViewParameterCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            XmlNode viewPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, context.EventFriendlyName), "View");
            partsNode.AppendChild(viewPartNode);

            BuildPartSubFormDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.SubFormGuid), GetGuidString(context.Condition.InstanceGuid), context.viewName, "View", GetGuidString(context.viewGuid), viewPartNode);

            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.parameterName, context.parameterName, "ViewParameter"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.Operator, context.Operator, "Operator"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormSimpleNotBlankFormParameterCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            XmlNode formPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            BuildPartSubFormDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.SubFormGuid), GetGuidString(context.Condition.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);

            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.parameterName, context.parameterDisplayName, "FormParameter"));
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, context.Operator, context.Operator, "Operator"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildViewIsCurrentActivityContextCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            var display = context.activityDisplayName;
            var value = context.parameterName;

            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, value, display, "Activity"));

            string mappingsNode = BuildMappingXMLForParts(conditionNode.OwnerDocument, context).OuterXml;
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, mappingsNode, Resources.RuleHelper.BracketConfigureText, "ConfigureActivity"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubViewIsCurrentActivityContextCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            var display = context.activityDisplayName;
            var value = context.parameterName;

            XmlNode viewPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, context.EventFriendlyName), "View");
            BuildPartSubFormDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.SubFormGuid), GetGuidString(context.Condition.InstanceGuid), context.viewName, "View", GetGuidString(context.viewGuid), viewPartNode);
            partsNode.AppendChild(viewPartNode);

            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, value, display, "Activity"));

            string mappingsNode = BuildMappingXMLForParts(conditionNode.OwnerDocument, context).OuterXml;
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, mappingsNode, Resources.RuleHelper.BracketConfigureText, "ConfigureActivity"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormIsCurrentActivityContextCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            var display = context.activityDisplayName;
            var value = context.parameterName;

            XmlNode formPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            BuildPartSubFormDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.SubFormGuid), GetGuidString(context.Condition.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);
            partsNode.AppendChild(formPartNode);

            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, value, display, "Activity"));

            string mappingsNode = BuildMappingXMLForParts(conditionNode.OwnerDocument, context).OuterXml;
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, mappingsNode, Resources.RuleHelper.BracketConfigureText, "ConfigureActivity"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormViewIsCurrentActivityContextCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            var display = context.activityDisplayName;
            var value = context.parameterName;

            XmlNode formPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            BuildPartSubFormDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.SubFormGuid), GetGuidString(context.Condition.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);
            partsNode.AppendChild(formPartNode);

            XmlNode viewPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            BuildPartDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.InstanceGuid), GetGuidString(context.Condition.SubFormGuid), context.viewName, null, "View", viewPartNode);
            partsNode.AppendChild(viewPartNode);

            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, value, display, "Activity"));

            string mappingsNode = BuildMappingXMLForParts(conditionNode.OwnerDocument, context).OuterXml;
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, mappingsNode, Resources.RuleHelper.BracketConfigureText, "ConfigureActivity"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildFormViewIsCurrentActivityContextCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            var display = context.activityDisplayName;
            var value = context.parameterName;

            XmlNode viewPartNode = BuildPartNode(conditionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            BuildPartDataNode(conditionNode.OwnerDocument, GetGuidString(context.Condition.InstanceGuid), GetGuidString(context.Condition.SubFormGuid), context.viewName, null, "View", viewPartNode);
            partsNode.AppendChild(viewPartNode);

            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, value, display, "Activity"));

            string mappingsNode = BuildMappingXMLForParts(conditionNode.OwnerDocument, context).OuterXml;
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, mappingsNode, Resources.RuleHelper.BracketConfigureText, "ConfigureActivity"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildFormIsCurrentActivityContextCondition(XmlNode conditionNode, Context context)
        {
            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            var display = context.activityDisplayName;

            var value = context.parameterName;

            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, value, display, "Activity"));

            string mappingsNode = BuildMappingXMLForParts(conditionNode.OwnerDocument, context).OuterXml;
            partsNode.AppendChild(BuildPartNode(conditionNode.OwnerDocument, mappingsNode, Resources.RuleHelper.BracketConfigureText, "ConfigureActivity"));

            ValidatePartValues(partsNode, context);
        }
        #endregion

        #region Action Builders
        private XmlNode BuildCommonActionParts(XmlNode actionNode, Context context, bool validateParts = false)
        {
            actionNode.Attributes.Append(actionNode.OwnerDocument.CreateAttribute("Name"));
            actionNode.Attributes["Name"].Value = context.RuleActionName;

            XmlNode partsNode = actionNode.OwnerDocument.CreateElement("Parts");
            actionNode.AppendChild(partsNode);

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, context.Action.ExecutionType.ToString(), TransformListenerToDisplayName(context.Action.ExecutionType.ToString()), "ExecutionType"));

            if (validateParts)
            {
                ValidatePartValues(partsNode, context);
            }

            return partsNode;
        }

        private void BuildRuleExecuteAction(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            XmlNode rulePartNode = BuildPartNode(actionNode.OwnerDocument, context.Action.Properties["EventID"], context.RuleFriendlyName, "Rule");
            partsNode.AppendChild(rulePartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), GetGuidString(context.Action.SubFormInstanceGuid), context.RuleFriendlyName, "Rule", context.Action.Properties["EventID"], rulePartNode);


            ValidatePartValues(partsNode, context);
        }

        private void BuildRuleExitAction(XmlNode actionNode, Context context)
        {
            actionNode.Attributes.Append(actionNode.OwnerDocument.CreateAttribute("Name"));
            actionNode.Attributes["Name"].Value = context.RuleActionName;

            XmlNode partsNode = actionNode.OwnerDocument.CreateElement("Parts");
            actionNode.AppendChild(partsNode);

            XmlNode execPartNode = BuildPartNode(actionNode.OwnerDocument, context.Action.ExecutionType.ToString(), TransformListenerToDisplayName(context.Action.ExecutionType.ToString()), "ExecutionType");
            partsNode.AppendChild(execPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), TransformListenerToDisplayName(context.Action.ExecutionType.ToString()), "ExecutionType", context.Action.ExecutionType.ToString(), execPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildRuleContinueAction(XmlNode actionNode, Context context)
        {
            actionNode.Attributes.Append(actionNode.OwnerDocument.CreateAttribute("Name"));
            actionNode.Attributes["Name"].Value = context.RuleActionName;

            XmlNode partsNode = actionNode.OwnerDocument.CreateElement("Parts");
            actionNode.AppendChild(partsNode);

            XmlNode execPartNode = BuildPartNode(actionNode.OwnerDocument, context.Action.ExecutionType.ToString(), TransformListenerToDisplayName(context.Action.ExecutionType.ToString()), "ExecutionType");
            partsNode.AppendChild(execPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), TransformListenerToDisplayName(context.Action.ExecutionType.ToString()), "ExecutionType", context.Action.ExecutionType.ToString(), execPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubViewEventAction(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, context.EventFriendlyName), "View");
            partsNode.AppendChild(viewPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.viewName, "View", GetGuidString(context.viewGuid), viewPartNode);

            XmlNode viewMethodPartNode = BuildPartNode(actionNode.OwnerDocument, context.Action.Method, context.methodDisplayName, "ViewMethod");
            partsNode.AppendChild(viewMethodPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormViewMethodExecute(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);

            XmlNode viewMethodPartNode = BuildPartNode(actionNode.OwnerDocument, context.Action.Method, context.methodDisplayName, "ViewMethod");
            partsNode.AppendChild(viewMethodPartNode);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildViewMethodExecuteItemsState(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.InstanceGuid), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, context.viewType, context.viewType, "ViewType"));
            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, context.Action.ItemState.ToString(), context.Action.ItemState.ToString(), "ItemStates"));

            XmlNode viewMethodPartNode = BuildPartNode(actionNode.OwnerDocument, context.Action.Method, context.methodDisplayName, "ViewMethod");
            partsNode.AppendChild(viewMethodPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildObjectMethodExecuteItemsState(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.InstanceGuid), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.ObjectGuid), context.ObjectName, "Object"));
            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, context.Action.ItemState.ToString(), context.Action.ItemState.ToString(), "ItemStates"));

            XmlNode objectMethodPartNode = BuildPartNode(actionNode.OwnerDocument, context.Action.Method, context.methodDisplayName, "ObjectMethod");
            BuildObjectPartDataNode(actionNode.OwnerDocument, context.ObjectGuid, context.ObjectSystemName, context.ObjectName, objectMethodPartNode);
            partsNode.AppendChild(objectMethodPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubViewViewMethodExecuteItemsState(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, context.EventFriendlyName), "View");
            partsNode.AppendChild(viewPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.viewName, "View", GetGuidString(context.viewGuid), viewPartNode);
            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, context.Action.ItemState.ToString(), context.Action.ItemState.ToString(), "ItemStates"));

            XmlNode viewMethodPartNode = BuildPartNode(actionNode.OwnerDocument, context.Action.Method, context.methodDisplayName, "ViewMethod");
            partsNode.AppendChild(viewMethodPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubViewObjectMethodExecuteItemsState(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, context.EventFriendlyName), "View");
            partsNode.AppendChild(viewPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.viewName, "View", GetGuidString(context.viewGuid), viewPartNode);
            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.ObjectGuid), context.ObjectName, "Object"));
            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, context.Action.Method, context.methodDisplayName, "ObjectMethod"));
            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, context.Action.ItemState.ToString(), context.Action.ItemState.ToString(), "ItemStates"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormViewMethodExecuteItemsState(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);
            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, context.Action.ItemState.ToString(), context.Action.ItemState.ToString(), "ItemStates"));

            XmlNode viewMethodPartNode = BuildPartNode(actionNode.OwnerDocument, context.Action.Method, context.methodDisplayName, "ViewMethod");
            partsNode.AppendChild(viewMethodPartNode);

            Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormObjectMethodExecuteItemsState(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);
            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.ObjectGuid), context.ObjectName, "Object"));
            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, context.Action.ItemState.ToString(), context.Action.ItemState.ToString(), "ItemStates"));

            XmlNode objectMethodPartNode = BuildPartNode(actionNode.OwnerDocument, context.Action.Method, context.methodDisplayName, "ObjectMethod");
            BuildObjectPartDataNode(actionNode.OwnerDocument, context.ObjectGuid, context.ObjectSystemName, context.ObjectName, objectMethodPartNode);
            partsNode.AppendChild(objectMethodPartNode);

            Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildViewAction(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode viewMethodPartNode = BuildPartNode(actionNode.OwnerDocument, context.Action.Method, context.methodDisplayName, "ViewMethod");
            partsNode.AppendChild(viewMethodPartNode);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildFormAction(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            BuildMappingXML(actionNode, context);

            XmlNode formMethodPartNode = BuildPartNode(actionNode.OwnerDocument, context.Action.Method, context.methodDisplayName, "FormMethod");
            partsNode.AppendChild(formMethodPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormAction(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            BuildMappingXML(actionNode, context);

            XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);

            XmlNode formMethodPartNode = BuildPartNode(actionNode.OwnerDocument, context.Action.Method, context.methodDisplayName, "FormMethod");
            partsNode.AppendChild(formMethodPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildViewListControlPopulateFromData(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl"));

            XmlNode objectMethodPartNode = BuildPartNode(actionNode.OwnerDocument, context.Action.Method, context.methodDisplayName, "ObjectMethod");
            BuildObjectPartDataNode(actionNode.OwnerDocument, context.ObjectGuid, context.ObjectSystemName, context.ObjectName, objectMethodPartNode);
            partsNode.AppendChild(objectMethodPartNode);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubViewListControlPopulateFromData(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl"));

            XmlNode objectMethodPartNode = BuildPartNode(actionNode.OwnerDocument, context.Action.Method, context.methodDisplayName, "ObjectMethod");
            BuildObjectPartDataNode(actionNode.OwnerDocument, context.ObjectGuid, context.ObjectSystemName, context.ObjectName, objectMethodPartNode);
            partsNode.AppendChild(objectMethodPartNode);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, context.EventFriendlyName), "View");
            partsNode.AppendChild(viewPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.viewName, "View", GetGuidString(context.viewGuid), viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormViewListControlPopulateFromData(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl"));

            XmlNode objectMethodPartNode = BuildPartNode(actionNode.OwnerDocument, context.Action.Method, context.methodDisplayName, "ObjectMethod");
            BuildObjectPartDataNode(actionNode.OwnerDocument, context.ObjectGuid, context.ObjectSystemName, context.ObjectName, objectMethodPartNode);
            partsNode.AppendChild(objectMethodPartNode);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);

            XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildListControlPopulation(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl"));

            XmlNode objectMethodPartNode = BuildPartNode(actionNode.OwnerDocument, context.Action.Method, context.methodDisplayName, "ObjectMethod");
            BuildObjectPartDataNode(actionNode.OwnerDocument, context.ObjectGuid, context.ObjectSystemName, context.ObjectName, objectMethodPartNode);
            partsNode.AppendChild(objectMethodPartNode);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubViewListControlPopulation(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl"));

            XmlNode objectMethodPartNode = BuildPartNode(actionNode.OwnerDocument, context.Action.Method, context.methodDisplayName, "ObjectMethod");
            BuildObjectPartDataNode(actionNode.OwnerDocument, context.ObjectGuid, context.ObjectSystemName, context.ObjectName, objectMethodPartNode);
            partsNode.AppendChild(objectMethodPartNode);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, context.EventFriendlyName), "View");
            partsNode.AppendChild(viewPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.viewName, "View", GetGuidString(context.viewGuid), viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormListControlPopulation(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl"));

            XmlNode objectMethodPartNode = BuildPartNode(actionNode.OwnerDocument, context.Action.Method, context.methodDisplayName, "ObjectMethod");
            BuildObjectPartDataNode(actionNode.OwnerDocument, context.ObjectGuid, context.ObjectSystemName, context.ObjectName, objectMethodPartNode);
            partsNode.AppendChild(objectMethodPartNode);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);

            XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildViewListControlPreLoadData(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl"));

            XmlNode objectMethodPartNode = BuildPartNode(actionNode.OwnerDocument, context.Action.Method, context.methodDisplayName, "ObjectMethod");
            BuildObjectPartDataNode(actionNode.OwnerDocument, context.ObjectGuid, context.ObjectSystemName, context.ObjectName, objectMethodPartNode);
            partsNode.AppendChild(objectMethodPartNode);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubViewListControlPreLoadData(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl"));

            XmlNode objectMethodPartNode = BuildPartNode(actionNode.OwnerDocument, context.Action.Method, context.methodDisplayName, "ObjectMethod");
            BuildObjectPartDataNode(actionNode.OwnerDocument, context.ObjectGuid, context.ObjectSystemName, context.ObjectName, objectMethodPartNode);
            partsNode.AppendChild(objectMethodPartNode);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, context.EventFriendlyName), "View");
            partsNode.AppendChild(viewPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.viewName, "View", GetGuidString(context.viewGuid), viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormViewListControlPreLoadData(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl"));

            XmlNode objectMethodPartNode = BuildPartNode(actionNode.OwnerDocument, context.Action.Method, context.methodDisplayName, "ObjectMethod");
            BuildObjectPartDataNode(actionNode.OwnerDocument, context.ObjectGuid, context.ObjectSystemName, context.ObjectName, objectMethodPartNode);
            partsNode.AppendChild(objectMethodPartNode);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);

            XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildViewControlMethodExecuteItemsState(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl"));
            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, context.Action.ItemState.ToString(), context.Action.ItemState.ToString(), "ItemStates"));

            XmlNode objectMethodPartNode = BuildPartNode(actionNode.OwnerDocument, context.Action.Method, context.methodDisplayName, "ObjectMethod");
            BuildObjectPartDataNode(actionNode.OwnerDocument, context.ObjectGuid, context.ObjectSystemName, context.ObjectName, objectMethodPartNode);
            partsNode.AppendChild(objectMethodPartNode);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubViewControlMethodExecuteItemsState(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);
            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl"));
            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, context.Action.ItemState.ToString(), context.Action.ItemState.ToString(), "ItemStates"));

            XmlNode objectMethodPartNode = BuildPartNode(actionNode.OwnerDocument, context.Action.Method, context.methodDisplayName, "ObjectMethod");
            BuildObjectPartDataNode(actionNode.OwnerDocument, context.ObjectGuid, context.ObjectSystemName, context.ObjectName, objectMethodPartNode);
            partsNode.AppendChild(objectMethodPartNode);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, context.EventFriendlyName), "View");
            partsNode.AppendChild(viewPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.viewName, "View", GetGuidString(context.viewGuid), viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormViewControlMethodExecuteItemsState(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl"));
            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, context.Action.ItemState.ToString(), context.Action.ItemState.ToString(), "ItemStates"));

            XmlNode objectMethodPartNode = BuildPartNode(actionNode.OwnerDocument, context.Action.Method, context.methodDisplayName, "ObjectMethod");
            BuildObjectPartDataNode(actionNode.OwnerDocument, context.ObjectGuid, context.ObjectSystemName, context.ObjectName, objectMethodPartNode);
            partsNode.AppendChild(objectMethodPartNode);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);

            XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildObjectMethodExecute(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.ObjectGuid), context.ObjectName, "Object"));

            XmlNode objectMethodPartNode = BuildPartNode(actionNode.OwnerDocument, context.Action.Method, context.methodDisplayName, "ObjectMethod");
            BuildObjectPartDataNode(actionNode.OwnerDocument, context.ObjectGuid, context.ObjectSystemName, context.ObjectName, objectMethodPartNode);
            partsNode.AppendChild(objectMethodPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubformAction(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, context.EventFriendlyName), "View");
            partsNode.AppendChild(viewPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.viewName, "View", GetGuidString(context.viewGuid), viewPartNode);

            if (context.RuleActionName == "SubViewOpenMethodExecute")
            {
                XmlNode viewMethodPartNode = BuildPartNode(actionNode.OwnerDocument, context.Action.Method, context.methodDisplayName, "ViewMethod");
                partsNode.AppendChild(viewMethodPartNode);
            }

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubformCloseAction(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            if (context.RuleActionName == "SubViewCloseMethodExecute")
            {
                XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");

                Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
                BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);

                partsNode.AppendChild(viewPartNode);
                XmlNode viewMethodPartNode = BuildPartNode(actionNode.OwnerDocument, context.Action.Method, context.methodDisplayName, "ViewMethod");
                partsNode.AppendChild(viewMethodPartNode);
            }

            ValidatePartValues(partsNode, context);
        }

        private void BuildNavigateAction(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), context.formName, "Form"));

            if (context.RuleActionName == "FormNavigationViewMethodExecute")
            {

                XmlNode viewMethodPartNode = BuildPartNode(actionNode.OwnerDocument, context.Action.Method, context.methodDisplayName, "ViewMethod");
                partsNode.AppendChild(viewMethodPartNode);
                partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), context.formName, "Form"));

                XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
                partsNode.AppendChild(viewPartNode);

                Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
                BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);
            }

            ValidatePartValues(partsNode, context);
        }

        private void BuildOpenedFormTransfer(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildShowHideControl(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl"));

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubformShowHideControl(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl"));

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, context.EventFriendlyName), "View");
            partsNode.AppendChild(viewPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.viewName, "View", GetGuidString(context.viewGuid), viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormShowHideControl(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl"));

            XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildEnableDisableControl(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl"));

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubformEnableDisableControl(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl"));

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, context.EventFriendlyName), "View");
            partsNode.AppendChild(viewPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.viewName, "View", GetGuidString(context.viewGuid), viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormEnableDisableControl(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl"));

            XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildViewShowHide(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            string viewDisplayName = context.viewMviName;
            if (context.SubformGuid != Guid.Empty)
            {
                viewDisplayName = string.Format(Resources.RuleHelper.SubFormPartDisplayName, viewDisplayName, context.EventFriendlyName);
            }

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), viewDisplayName, "View");
            partsNode.AppendChild(viewPartNode);

            Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormViewFilterShowHide(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildViewEnableDisable(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubformViewEnableDisable(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, context.EventFriendlyName), "View");
            partsNode.AppendChild(viewPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.viewName, "View", GetGuidString(context.viewGuid), viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormViewEnableDisable(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildViewExpandCollapse(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubformViewExpandCollapse(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, context.EventFriendlyName), "View");
            partsNode.AppendChild(viewPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.viewName, "View", GetGuidString(context.viewGuid), viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormViewExpandCollapse(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormViewShowHide(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildControlTransfer(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            if (!context.viewGuid.Equals(Guid.Empty))
            {
                XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
                partsNode.AppendChild(viewPartNode);

                Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
                BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);
            }

            ValidatePartValues(partsNode, context);
        }

        private void BuildFormControlActionParts(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);
            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "FormControl"));
            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormControlActionParts(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "FormControl"));

            XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildPanelShowHide(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);
            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.panelGuid), context.panelName, "Panel"));
            ValidatePartValues(partsNode, context);
        }

        private void BuildSetViewControlProperties(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, context.EventFriendlyName), "View");
            partsNode.AppendChild(viewPartNode);

            XmlNode controlPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl");
            partsNode.AppendChild(controlPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSetFormControlProperties(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode controlPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "FormControl");
            partsNode.AppendChild(controlPartNode);

            XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildServerSetViewControlProperties(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, context.EventFriendlyName), "View");
            partsNode.AppendChild(viewPartNode);

            XmlNode controlPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl");
            partsNode.AppendChild(controlPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildServerSetFormControlProperties(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode controlPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "FormControl");
            partsNode.AppendChild(controlPartNode);

            XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            ValidatePartValues(partsNode, context);
        }

        public void BuildSetFormAreaItemProperties(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode areaItemPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "AreaItem");
            XmlNode areaItemItemDataNode = BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.InstanceGuid), GetGuidString(context.Action.SubFormGuid), context.viewName, context.controlGuid, "AreaItem", areaItemPartNode);
            partsNode.AppendChild(areaItemPartNode);

            ValidatePartValues(partsNode, context);
        }

        public void BuildSubFormSetFormAreaItemProperties(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);
            partsNode.AppendChild(formPartNode);

            XmlNode areaItemPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "AreaItem");
            XmlNode areaItemItemDataNode = BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.InstanceGuid), GetGuidString(context.Action.SubFormGuid), context.viewName, context.controlGuid, "AreaItem", areaItemPartNode);
            partsNode.AppendChild(areaItemPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSetSubFormProperties(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);
            partsNode.AppendChild(formPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSetFormViewControlProperties(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);

            XmlNode controlPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl");
            partsNode.AppendChild(controlPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildServerSetFormViewControlProperties(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);

            XmlNode controlPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl");
            partsNode.AppendChild(controlPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSetSubFormViewControlProperties(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);

            XmlNode controlPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl");
            partsNode.AppendChild(controlPartNode);

            XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);
            ValidatePartValues(partsNode, context);
        }

        private void BuildServerSetSubFormViewControlProperties(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);

            XmlNode controlPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl");
            partsNode.AppendChild(controlPartNode);

            XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);
            ValidatePartValues(partsNode, context);
        }

        private void BuildSetSubViewControlProperties(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, context.EventFriendlyName), "View");
            partsNode.AppendChild(viewPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.viewName, "View", GetGuidString(context.viewGuid), viewPartNode);

            XmlNode controlPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl");
            partsNode.AppendChild(controlPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildServerSetSubViewControlProperties(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, context.EventFriendlyName), "View");
            partsNode.AppendChild(viewPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.viewName, "View", GetGuidString(context.viewGuid), viewPartNode);

            XmlNode controlPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "ViewControl");
            partsNode.AppendChild(controlPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSetSubFormControlProperties(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode controlPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "FormControl");
            partsNode.AppendChild(controlPartNode);

            XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);
            ValidatePartValues(partsNode, context);
        }

        private void BuildServerSetSubFormControlProperties(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode controlPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "FormControl");
            partsNode.AppendChild(controlPartNode);

            XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);
            ValidatePartValues(partsNode, context);
        }

        private void BuildSubViewControlReadOnly(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, context.EventFriendlyName), "View");
            partsNode.AppendChild(viewPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.viewName, "View", GetGuidString(context.viewGuid), viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormControlReadOnly(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);

            ValidatePartValues(partsNode, context);
        }
        //TFS 564835
        private void BuildControlMethodExecuteShared(XmlNode actionNode, XmlNode partsNode, Context context)
        {
            XmlNode controlPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.controlGuid), context.controlName, "Control");
            partsNode.AppendChild(controlPartNode);

            XmlNode methodPartNode = BuildPartNode(actionNode.OwnerDocument, context.Action.Method, context.methodDisplayName, "ControlMethod");
            partsNode.AppendChild(methodPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildControlMethodExecute(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            if (context.viewGuid != null && context.viewGuid != Guid.Empty)
            {
                XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
                partsNode.AppendChild(viewPartNode);
            }

            BuildControlMethodExecuteShared(actionNode, partsNode, context);
        }

        private void BuildSubViewControlMethodExecute(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            if (context.viewGuid != null && context.viewGuid != Guid.Empty)
            {
                XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, context.EventFriendlyName), "View");
                partsNode.AppendChild(viewPartNode);
                if (context.Action.SubFormGuid != null && context.viewGuid != Guid.Empty)
                {
                    BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.viewName, "View", GetGuidString(context.viewGuid), viewPartNode);
                }
            }

            BuildControlMethodExecuteShared(actionNode, partsNode, context);
        }

        private void BuildFormControlMethodExecute(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            if (context.formGuid != null && context.formGuid != Guid.Empty)
            {
                XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), context.formName, "Form");
                partsNode.AppendChild(formPartNode);
            }

            BuildControlMethodExecuteShared(actionNode, partsNode, context);
        }

        private void BuildFormViewControlMethodExecute(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            if (context.formGuid != null && context.formGuid != Guid.Empty)
            {
                XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), context.formName, "Form");
                partsNode.AppendChild(formPartNode);
            }

            if (context.viewGuid != null && context.viewGuid != Guid.Empty)
            {
                XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
                Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
                BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);
                partsNode.AppendChild(viewPartNode);
            }

            BuildControlMethodExecuteShared(actionNode, partsNode, context);
        }

        private void BuildSubFormViewControlMethodExecute(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            if (context.formGuid != null && context.formGuid != Guid.Empty)
            {
                XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
                partsNode.AppendChild(formPartNode);

                if (context.Action.SubFormGuid != null)
                {
                    BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);
                }
            }

            if (context.viewGuid != null && context.viewGuid != Guid.Empty)
            {
                XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
                Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
                BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);
                partsNode.AppendChild(viewPartNode);
            }

            BuildControlMethodExecuteShared(actionNode, partsNode, context);
        }

        private void BuildSubFormControlMethodExecute(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            if (context.formGuid != null && context.formGuid != Guid.Empty)
            {
                XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
                partsNode.AppendChild(formPartNode);
                if (context.Action.SubFormGuid != null && context.formGuid != Guid.Empty)
                {
                    BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);
                }
            }

            BuildControlMethodExecuteShared(actionNode, partsNode, context);
        }

        private void BuildSubFormPanelShowHide(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.panelGuid), context.panelName, "Panel"));

            XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildPanelFocus(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.panelGuid), context.panelName, "Panel"));

            if (context.RuleActionName == "FormNavigationPanelFocus" || context.RuleActionName == "SubFormPanelFocus")
            {
                XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
                partsNode.AppendChild(formPartNode);

                BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);
            }

            ValidatePartValues(partsNode, context);
        }

        private void BuildServerPanelFocus(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.panelGuid), context.panelName, "Panel"));

            if (context.RuleActionName == "ServerSubFormPanelFocus")
            {
                XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
                partsNode.AppendChild(formPartNode);

                BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);
            }

            ValidatePartValues(partsNode, context);
        }

        private void BuildViewFocus(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            Guid instanceGuidToUse = context.Action.SubFormInstanceGuid != Guid.Empty ? context.Action.SubFormInstanceGuid : context.Action.InstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);
            if (context.RuleActionName == "FormNavigationViewFocus")
            {
                partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), context.formName, "Form"));
            }

            ValidatePartValues(partsNode, context);
        }

        private void BuildServerViewFocus(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            Guid instanceGuidToUse = context.Action.SubFormInstanceGuid != Guid.Empty ? context.Action.SubFormInstanceGuid : context.Action.InstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);
            if (context.RuleActionName == "ServerSubFormViewFocus")
            {
                XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
                partsNode.AppendChild(formPartNode);

                BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);
            }

            ValidatePartValues(partsNode, context);
        }

        private void BuildPromptAction(XmlNode actionNode, Context context)
        {
            var messageIsLiteral = context.Action.Properties["MessageIsLiteral"] ?? "true";
            var messageValue = $"<Message><Value>{HttpUtility.HtmlEncode(context.Action.Properties["Message"])}</Value><Checked>{HttpUtility.HtmlEncode(messageIsLiteral) }</Checked></Message>";

            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, context.Action.Properties["Heading"], context.Action.Properties["Heading"], "HeadingValueInput"));
            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, messageValue, context.Action.Properties["Message"], "MessageValueInput"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildMessageAction(XmlNode actionNode, Context context)
        {
            actionNode.Attributes.Append(actionNode.OwnerDocument.CreateAttribute("Name"));
            actionNode.Attributes["Name"].Value = context.RuleActionName;

            XmlNode partsNode = actionNode.OwnerDocument.CreateElement("Parts");
            actionNode.AppendChild(partsNode);

            XmlNode execPartNode = BuildPartNode(actionNode.OwnerDocument, context.Action.ExecutionType.ToString(), TransformListenerToDisplayName(context.Action.ExecutionType.ToString()), "ExecutionType");
            partsNode.AppendChild(execPartNode);

            if (context.Action.SubFormGuid != Guid.Empty)
            {
                BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), execPartNode);
            }

            ValidatePartValues(partsNode, context);
        }

        private void BuildFormOpenAction(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), context.formName, "Form");
            partsNode.AppendChild(formPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildFormDisable(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            if (context.Action.SubFormGuid != Guid.Empty)
            {
                XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
                partsNode.AppendChild(formPartNode);

                BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);
            }

            ValidatePartValues(partsNode, context);
        }

        private void BuildFormEnable(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            if (context.Action.SubFormGuid != Guid.Empty)
            {
                XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
                partsNode.AppendChild(formPartNode);

                BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);
            }

            ValidatePartValues(partsNode, context);
        }

        private void BuildActionProcessAction(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            string activityDisplayName = context.Action.Properties.GetDisplayValue("ActivityFullName");

            if (string.IsNullOrEmpty(activityDisplayName))
            {
                activityDisplayName = context.Action.Properties["ActivityFullName"].Substring(context.Action.Properties["ActivityFullName"].LastIndexOf('\\') + 1);
            }

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, context.activityFullName, context.activityDisplayName, "Activity"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildActionViewProcessAction(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);
            partsNode.AppendChild(viewPartNode);

            string activityDisplayName = context.Action.Properties.GetDisplayValue("ActivityFullName");

            if (string.IsNullOrEmpty(activityDisplayName))
            {
                activityDisplayName = context.Action.Properties["ActivityFullName"].Substring(context.Action.Properties["ActivityFullName"].LastIndexOf('\\') + 1);
            }

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, context.activityFullName, context.activityDisplayName, "Activity"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildActionSubViewProcessAction(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, context.EventFriendlyName), "View");
            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.viewName, "View", GetGuidString(context.viewGuid), viewPartNode);
            partsNode.AppendChild(viewPartNode);

            string activityDisplayName = context.Action.Properties.GetDisplayValue("ActivityFullName");

            if (string.IsNullOrEmpty(activityDisplayName))
            {
                activityDisplayName = context.Action.Properties["ActivityFullName"].Substring(context.Action.Properties["ActivityFullName"].LastIndexOf('\\') + 1);
            }

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, context.activityFullName, context.activityDisplayName, "Activity"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildActionSubFormProcessAction(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);
            partsNode.AppendChild(formPartNode);

            string activityDisplayName = context.Action.Properties.GetDisplayValue("ActivityFullName");

            if (string.IsNullOrEmpty(activityDisplayName))
            {
                activityDisplayName = context.Action.Properties["ActivityFullName"].Substring(context.Action.Properties["ActivityFullName"].LastIndexOf('\\') + 1);
            }

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, context.activityFullName, context.activityDisplayName, "Activity"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildActionSubFormViewProcessAction(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);
            partsNode.AppendChild(formPartNode);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);
            partsNode.AppendChild(viewPartNode);

            string activityDisplayName = context.Action.Properties.GetDisplayValue("ActivityFullName");

            if (string.IsNullOrEmpty(activityDisplayName))
            {
                activityDisplayName = context.Action.Properties["ActivityFullName"].Substring(context.Action.Properties["ActivityFullName"].LastIndexOf('\\') + 1);
            }

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, context.activityFullName, context.activityDisplayName, "Activity"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildProcessStartAction(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            string processDisplayName = context.Action.Properties.GetDisplayValue("ProcessName");

            if (string.IsNullOrEmpty(processDisplayName))
            {
                processDisplayName = context.Action.Properties["ProcessName"].Substring(context.Action.Properties["ProcessName"].LastIndexOf('\\') + 1);
            }

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, context.Action.Properties["ProcessName"], processDisplayName, "Process"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildViewProcessStartAction(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);
            partsNode.AppendChild(viewPartNode);

            string processDisplayName = context.Action.Properties.GetDisplayValue("ProcessName");

            if (string.IsNullOrEmpty(processDisplayName))
            {
                processDisplayName = context.Action.Properties["ProcessName"].Substring(context.Action.Properties["ProcessName"].LastIndexOf('\\') + 1);
            }

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, context.Action.Properties["ProcessName"], processDisplayName, "Process"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubViewProcessStartAction(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, context.EventFriendlyName), "View");
            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.viewName, "View", GetGuidString(context.viewGuid), viewPartNode);
            partsNode.AppendChild(viewPartNode);

            string processDisplayName = context.Action.Properties.GetDisplayValue("ProcessName");

            if (string.IsNullOrEmpty(processDisplayName))
            {
                processDisplayName = context.Action.Properties["ProcessName"].Substring(context.Action.Properties["ProcessName"].LastIndexOf('\\') + 1);
            }

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, context.Action.Properties["ProcessName"], processDisplayName, "Process"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormProcessStartAction(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);
            partsNode.AppendChild(formPartNode);

            string processDisplayName = context.Action.Properties.GetDisplayValue("ProcessName");

            if (string.IsNullOrEmpty(processDisplayName))
            {
                processDisplayName = context.Action.Properties["ProcessName"].Substring(context.Action.Properties["ProcessName"].LastIndexOf('\\') + 1);
            }

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, context.Action.Properties["ProcessName"], processDisplayName, "Process"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormViewProcessStartAction(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);
            partsNode.AppendChild(formPartNode);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);
            partsNode.AppendChild(viewPartNode);

            string processDisplayName = context.Action.Properties.GetDisplayValue("ProcessName");

            if (string.IsNullOrEmpty(processDisplayName))
            {
                processDisplayName = context.Action.Properties["ProcessName"].Substring(context.Action.Properties["ProcessName"].LastIndexOf('\\') + 1);
            }

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, context.Action.Properties["ProcessName"], processDisplayName, "Process"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildProcessLoadAction(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            string activityDisplayName = context.Action.Properties.GetDisplayValue("ActivityFullName");

            if (string.IsNullOrEmpty(activityDisplayName))
            {
                activityDisplayName = context.Action.Properties["ActivityFullName"].Substring(context.Action.Properties["ActivityFullName"].LastIndexOf('\\') + 1);
            }

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, context.activityFullName, context.activityDisplayName, "Activity"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildViewProcessLoadAction(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);
            partsNode.AppendChild(viewPartNode);

            string activityDisplayName = context.Action.Properties.GetDisplayValue("ActivityFullName");

            if (string.IsNullOrEmpty(activityDisplayName))
            {
                activityDisplayName = context.Action.Properties["ActivityFullName"].Substring(context.Action.Properties["ActivityFullName"].LastIndexOf('\\') + 1);
            }

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, context.activityFullName, context.activityDisplayName, "Activity"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubViewProcessLoadAction(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, context.EventFriendlyName), "View");
            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.viewName, "View", GetGuidString(context.viewGuid), viewPartNode);
            partsNode.AppendChild(viewPartNode);

            string activityDisplayName = context.Action.Properties.GetDisplayValue("ActivityFullName");

            if (string.IsNullOrEmpty(activityDisplayName))
            {
                activityDisplayName = context.Action.Properties["ActivityFullName"].Substring(context.Action.Properties["ActivityFullName"].LastIndexOf('\\') + 1);
            }

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, context.activityFullName, context.activityDisplayName, "Activity"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormProcessLoadAction(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);
            partsNode.AppendChild(formPartNode);

            string activityDisplayName = context.Action.Properties.GetDisplayValue("ActivityFullName");

            if (string.IsNullOrEmpty(activityDisplayName))
            {
                activityDisplayName = context.Action.Properties["ActivityFullName"].Substring(context.Action.Properties["ActivityFullName"].LastIndexOf('\\') + 1);
            }

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, context.activityFullName, context.activityDisplayName, "Activity"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormViewProcessLoadAction(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);
            partsNode.AppendChild(formPartNode);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);
            partsNode.AppendChild(viewPartNode);

            string activityDisplayName = context.Action.Properties.GetDisplayValue("ActivityFullName");

            if (string.IsNullOrEmpty(activityDisplayName))
            {
                activityDisplayName = context.Action.Properties["ActivityFullName"].Substring(context.Action.Properties["ActivityFullName"].LastIndexOf('\\') + 1);
            }

            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, context.activityFullName, context.activityDisplayName, "Activity"));

            ValidatePartValues(partsNode, context);
        }

        private void BuildHandlerAction(XmlNode actionNode, Context context)
        {
            actionNode.Attributes.Append(actionNode.OwnerDocument.CreateAttribute("Name"));
            actionNode.Attributes["Name"].Value = context.RuleActionName;

            XmlNode handlersNode = actionNode.OwnerDocument.CreateElement("Handlers");
            actionNode.AppendChild(handlersNode);

            foreach (Handler handler in context.Action.Handlers)
            {
                TransformAuthoringHandlerToRuleHandler(context.context,
                    context.ruleDefinition,
                    context.ruleInstance,
                    handler,
                    handlersNode);
            }
        }

        // SFIID - TODO as part of conditions, might show up as incorrectly badged till its done.
        private void BuildFormValidateCondition(XmlNode actionNode, Context context)
        {
            string ruleID = GetEvent(context.Action).Guid.ToString();
            XmlNode conditionNode = actionNode.OwnerDocument.CreateElement("Condition");
            XmlNode conditionsNode = actionNode.OwnerDocument.SelectSingleNode(string.Format("Rules/Rule[@ID={0}]//Handler[@ID='{1}']/Conditions", XmlHelper.XPathParameterEncode(ruleID), context.Action.Handler.Guid.ToString()));

            conditionsNode.AppendChild(conditionNode);

            conditionNode.Attributes.Append(conditionNode.OwnerDocument.CreateAttribute("Name"));
            conditionNode.Attributes["Name"].Value = context.RuleActionName;

            XmlNode partsNode = conditionNode.OwnerDocument.CreateElement("Parts");
            conditionNode.AppendChild(partsNode);

            ValidationGroup validationgroup = null;
            Guid validationGroupID = Guid.Empty;

            XmlNode ValidationGroupNode = conditionNode.OwnerDocument.CreateElement("ValidationGroup");
            XmlAttribute validationGroupIDAttr = conditionNode.OwnerDocument.CreateAttribute("ID");
            XmlAttribute validateInvisibleControlsAttr = conditionNode.OwnerDocument.CreateAttribute("IgnoreInvisibleControls");
            XmlAttribute validateDisabledControlsAttr = conditionNode.OwnerDocument.CreateAttribute("IgnoreDisabledControls");
            XmlAttribute validateReadOnlyControlsAttr = conditionNode.OwnerDocument.CreateAttribute("IgnoreReadOnlyControls");

            string validateHiddenControls = context.Action.Properties["IgnoreInvisibleControls"] != null ? context.Action.Properties["IgnoreInvisibleControls"] : "";
            string validateDisControls = context.Action.Properties["IgnoreDisabledControls"] != null ? context.Action.Properties["IgnoreDisabledControls"] : "";
            string validateROControls = context.Action.Properties["IgnoreReadOnlyControls"] != null ? context.Action.Properties["IgnoreReadOnlyControls"] : "";
            validationGroupID = new Guid(context.Action.Properties["GroupID"]);

            validationGroupIDAttr.Value = validationGroupID.ToString();
            validateInvisibleControlsAttr.Value = validateHiddenControls;
            validateDisabledControlsAttr.Value = validateDisControls;
            validateReadOnlyControlsAttr.Value = validateROControls;

            ValidationGroupNode.Attributes.Append(validationGroupIDAttr);
            ValidationGroupNode.Attributes.Append(validateInvisibleControlsAttr);
            ValidationGroupNode.Attributes.Append(validateDisabledControlsAttr);
            ValidationGroupNode.Attributes.Append(validateReadOnlyControlsAttr);

            conditionNode.Attributes.Append(conditionNode.OwnerDocument.CreateAttribute("IsCurrentHandler"));
            conditionNode.Attributes["IsCurrentHandler"].Value = (!context.Action.IsReference).ToString();
            conditionNode.Attributes.Append(conditionNode.OwnerDocument.CreateAttribute("Enabled"));
            conditionNode.Attributes["Enabled"].Value = context.Action.IsEnabled.ToString();
            conditionNode.Attributes.Append(conditionNode.OwnerDocument.CreateAttribute("ID"));
            conditionNode.Attributes["ID"].Value = context.Action.Guid.ToString().ToLowerInvariant();
            conditionNode.Attributes.Append(conditionNode.OwnerDocument.CreateAttribute("HandlerID"));
            conditionNode.Attributes["HandlerID"].Value = context.Action.Handler.Guid.ToString();
            conditionNode.Attributes.Append(conditionNode.OwnerDocument.CreateAttribute("Context"));
            conditionNode.Attributes["Context"].Value = context.Action.Properties["Location"] != null ? context.Action.Properties["Location"] : "";
            conditionNode.Attributes.Append(conditionNode.OwnerDocument.CreateAttribute("DefinitionID"));
            conditionNode.Attributes["DefinitionID"].Value = context.Action.DefinitionGuid.ToString().ToLowerInvariant();

            //Hack for instance ID until this action is changed to a condition
            if (context.Action.InstanceGuid != Guid.Empty)
            {
                conditionNode.Attributes.Append(conditionNode.OwnerDocument.CreateAttribute("InstanceID"));
                conditionNode.Attributes["InstanceID"].Value = context.Action.InstanceGuid.ToString().ToLowerInvariant();
            }
            //Hack for instance ID until this action is changed to a condition

            if (GetEvent(context.Action).View != null && context.Action.SubFormGuid == Guid.Empty)
            {
                XmlNode viewPartNode = conditionNode.OwnerDocument.CreateElement("Part");

                XmlNode viewPartValueNode = conditionNode.OwnerDocument.CreateElement("Value");
                XmlNode viewPartDisplayNode = conditionNode.OwnerDocument.CreateElement("Display");

                viewPartNode.Attributes.Append(conditionNode.OwnerDocument.CreateAttribute("Name"));
                viewPartNode.Attributes["Name"].Value = "View";

                ResolveView(context, GetEvent(context.Action));
                viewPartValueNode.AppendChild(conditionNode.OwnerDocument.CreateCDataSection(context.viewGuid.ToString()));
                viewPartDisplayNode.AppendChild(conditionNode.OwnerDocument.CreateCDataSection(context.viewMviName));

                viewPartNode.AppendChild(viewPartValueNode);
                viewPartNode.AppendChild(viewPartDisplayNode);

                partsNode.AppendChild(viewPartNode);

                validationgroup = context.View.ValidationGroups[validationGroupID];
            }
            else
            {
                if (context.Action.SubFormGuid != Guid.Empty)
                {
                    GetSubFormAction(context, context.Action.SubFormGuid, GetEvent(context.Action));

                    if (context.SubItemAction.FormGuid == Guid.Empty)
                    {
                        ResolveExternalView(context);
                        validationgroup = context.View?.ValidationGroups[validationGroupID];
                    }
                    else
                    {
                        ResolveExternalForm(context);

                        if (context.Form != null)
                        {
                            if (context.Form.ValidationGroups.Contains(validationGroupID))
                            {
                                validationgroup = context.Form.ValidationGroups[validationGroupID];
                            }
                            else
                            {
                                context.InstanceGuid = context.Action.InstanceGuid;
                                ResolveFormView(context, context.Action.Validation);
                                validationgroup = context.View?.ValidationGroups[validationGroupID];
                            }
                        }
                    }
                }
                else
                {
                    ResolveForm(context, GetEvent(context.Action));
                    if (context.Form.ValidationGroups.Contains(validationGroupID))
                    {
                        validationgroup = context.Form?.ValidationGroups[validationGroupID];
                    }
                    else
                    {
                        context.InstanceGuid = context.Action.InstanceGuid;
                        ResolveFormView(context, context.Action.Validation);
                        validationgroup = context.View?.ValidationGroups[validationGroupID];
                    }
                }
            }

            XmlNode conditionPartNode = conditionNode.OwnerDocument.CreateElement("Part");

            XmlNode conditionPartValueNode = conditionNode.OwnerDocument.CreateElement("Value");
            XmlNode conditionPartDisplayNode = conditionNode.OwnerDocument.CreateElement("Display");

            conditionPartNode.Attributes.Append(conditionNode.OwnerDocument.CreateAttribute("Name"));
            conditionPartNode.Attributes["Name"].Value = "ConfigureCondition";

            XmlNode validationGroupsNode = conditionNode.OwnerDocument.CreateElement("ValidationGroups");

            string validationGroupType = context.Action.Properties["MessageLocation"];
            ValidationGroupNode.Attributes.Append(conditionNode.OwnerDocument.CreateAttribute("Type"));
            ValidationGroupNode.Attributes["Type"].Value = validationGroupType;

            if (validationgroup != null)
            {
                foreach (ValidationGroupControl vgControl in validationgroup.Controls)
                {
                    XmlNode sourceNode = conditionNode.OwnerDocument.CreateElement("Source");

                    XmlAttribute sourceTypeAttr = conditionNode.OwnerDocument.CreateAttribute("SourceType");
                    XmlAttribute sourceIDAttr = conditionNode.OwnerDocument.CreateAttribute("ID");
                    XmlAttribute isRequiredAttr = conditionNode.OwnerDocument.CreateAttribute("IsRequired");
                    XmlAttribute instanceIDAttr = conditionNode.OwnerDocument.CreateAttribute("InstanceID");

                    sourceTypeAttr.Value = "Control";
                    sourceIDAttr.Value = vgControl.ControlGuid.ToString();
                    isRequiredAttr.Value = vgControl.IsRequired.ToString();
                    instanceIDAttr.Value = vgControl.InstanceGuid.ToString();

                    sourceNode.Attributes.Append(sourceTypeAttr);
                    sourceNode.Attributes.Append(sourceIDAttr);
                    sourceNode.Attributes.Append(isRequiredAttr);
                    sourceNode.Attributes.Append(instanceIDAttr);

                    if ((vgControl.Validation.Status & Framework.ValidationStatus.Missing) == Framework.ValidationStatus.Missing)
                    {
                        AnnotateNodeAndRule(sourceNode, context, vgControl.Validation);
                    }

                    ValidationGroupNode.AppendChild(sourceNode);
                }

                if (validationgroup.Controls.Count > 0)
                {
                    validationGroupsNode.AppendChild(ValidationGroupNode);

                    conditionPartValueNode.AppendChild(conditionNode.OwnerDocument.CreateCDataSection(validationGroupsNode.OuterXml));
                    conditionPartDisplayNode.AppendChild(conditionNode.OwnerDocument.CreateCDataSection("Configured"));
                }
            }

            if (actionNode.SelectSingleNode("Comments") != null && !string.IsNullOrEmpty(actionNode.SelectSingleNode("Comments").InnerText))
            {
                XmlNode commentsNode = conditionNode.OwnerDocument.CreateElement("Comments");
                commentsNode.AppendChild(conditionNode.OwnerDocument.CreateTextNode(actionNode.SelectSingleNode("Comments").InnerText));
                conditionNode.AppendChild(commentsNode);
            }

            var actionValidationStatusError = (context.Action.Validation.Status & Framework.ValidationStatus.Error) == Framework.ValidationStatus.Error;
            var groupValidationStatusError = validationgroup != null && (validationgroup.Validation.Status & Framework.ValidationStatus.Error) == Framework.ValidationStatus.Error;

            if (actionValidationStatusError ||
                (validationgroup == null && context.Action != null && !context.Action.IsReference) ||
                groupValidationStatusError
            )
            {
                conditionPartNode.Attributes.Append(conditionNode.OwnerDocument.CreateAttribute("Invalid"));
                conditionPartNode.Attributes["Invalid"].Value = "true";

                conditionNode.Attributes.Append(conditionNode.OwnerDocument.CreateAttribute("Invalid"));
                conditionNode.Attributes["Invalid"].Value = "true";
            }

            conditionPartNode.AppendChild(conditionPartValueNode);
            conditionPartNode.AppendChild(conditionPartDisplayNode);

            partsNode.AppendChild(conditionPartNode);

            actionNode.ParentNode.RemoveChild(actionNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildCaptureListRowFunctionality(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubViewCaptureListRowFunctionality(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.viewMviName, context.EventFriendlyName), "View");
            partsNode.AppendChild(viewPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.viewName, "View", GetGuidString(context.viewGuid), viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildSubFormCaptureListRowFunctionality(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);

            XmlNode formPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), string.Format(Resources.RuleHelper.SubFormPartDisplayName, context.formName, context.EventFriendlyName), "Form");
            partsNode.AppendChild(formPartNode);

            BuildPartSubFormDataNode(actionNode.OwnerDocument, GetGuidString(context.Action.SubFormGuid), GetGuidString(context.Action.InstanceGuid), context.formName, "Form", GetGuidString(context.formGuid), formPartNode);

            XmlNode viewPartNode = BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.viewGuid), context.viewMviName, "View");
            partsNode.AppendChild(viewPartNode);

            Guid instanceGuidToUse = context.Action.SubFormGuid == Guid.Empty ? context.Action.InstanceGuid : context.Action.SubFormInstanceGuid;
            BuildPartDataNode(actionNode.OwnerDocument, GetGuidString(instanceGuidToUse), GetGuidString(context.Action.SubFormGuid), context.viewName, null, "View", viewPartNode);

            ValidatePartValues(partsNode, context);
        }

        private void BuildFormCloseAction(XmlNode actionNode, Context context)
        {
            XmlNode partsNode = BuildCommonActionParts(actionNode, context);
            partsNode.AppendChild(BuildPartNode(actionNode.OwnerDocument, GetGuidString(context.formGuid), context.formName, "Form"));
            ValidatePartValues(partsNode, context);
        }

        #endregion

        #region Helpers
        private void ResolveHandler(Context context)
        {
            ValidationMessageParts validationMessageParts;
            var currentContext = context.Event.Form != null ? ContextType.FORM : ContextType.VIEW;
            XmlDocument ruleDefinition = GetRuleDefinition(currentContext);
            XmlNode handlerDefinition = ruleDefinition.SelectSingleNode("SourceCode.Forms/RuleDefinitions/Handlers/Handler[@Name=" + XmlHelper.XPathParameterEncode(context.RuleHandlerName) + "]");
            XmlNodeList handlerParts = handlerDefinition.SelectNodes("Parts/Part");
            string handlerMessageText = handlerDefinition.SelectSingleNode("Message").InnerText;

            if (string.IsNullOrEmpty(context.Location))
            {
                context.Location = Resources.Rules.ErrorRuleLocationNotResolved;
            }

            foreach (XmlNode partNode in handlerParts)
            {
                var partName = partNode.Attributes["Name"].Value;
                var partDisplayText = partNode.SelectSingleNode("Display").InnerText;

                switch (partName)
                {
                    case "ViewControl":
                        if (context.Control == null)
                        {
                            PropertyExpression viewControl = GetPropertyExpressionBySourceType(context.handler.Function.Parameters, PropertyExpressionSourceType.Control);
                            if (viewControl != null && !string.IsNullOrEmpty(viewControl.SourceDisplayName))
                            {
                                partDisplayText = viewControl.SourceDisplayName;
                            }
                            validationMessageParts = GetValidationMessageParts(viewControl != null ? viewControl.Validation : null, ReferenceType.Control, partDisplayText);
                            context.controlName = validationMessageParts.RefDisplayName;
                            context.controlSystemName = validationMessageParts.RefName;
                            if (context.controlGuid == Guid.Empty)
                            {
                                context.controlGuid = validationMessageParts.RefGuid;
                            }
                            context.InvalidPartsDictionary.Add(partName, new InvalidPart(context.controlName, validationMessageParts.SourceError));
                        }
                        break;
                    case "View":
                        if (context.View == null)
                        {
                            ReferenceType[] referenceTypeArray = new ReferenceType[3] { ReferenceType.View, ReferenceType.ViewInstance, ReferenceType.SubForm };
                            // If the view is missing, it will be annotated here.
                            PropertyExpression view = GetPropertyExpressionBySourceType(context.handler.Function.Parameters, PropertyExpressionSourceType.View);
                            if (view != null && !string.IsNullOrEmpty(view.SourceDisplayName))
                            {
                                partDisplayText = view.SourceDisplayName;
                            }
                            validationMessageParts = GetValidationMessageParts(view != null ? view.Validation : null, referenceTypeArray, partDisplayText);
                            if (validationMessageParts.SourceError == null && (view != null && view.SourceID != null))
                            {
                                // if the view instance is missing, it will show up here.
                                validationMessageParts = GetValidationMessageParts(context.handler.Function.Validation, referenceTypeArray, partDisplayText);
                            }
                            context.viewName = validationMessageParts.RefDisplayName;
                            context.viewSystemName = validationMessageParts.RefName;
                            context.viewMviName = validationMessageParts.RefDisplayName;
                            context.viewGuid = validationMessageParts.RefGuid;
                            context.InvalidPartsDictionary.Add(partName, new InvalidPart(context.viewName, validationMessageParts.SourceError));
                        }
                        else
                        {
                            context.viewName = context.viewMviName;
                        }
                        break;
                    case "Form":
                        if (context.Form == null)
                        {
                            if (string.IsNullOrEmpty(context.formName))
                            {
                                context.formName = partDisplayText;
                            }
                            validationMessageParts = GetValidationMessageParts(context.handler.Function.Validation, ReferenceType.Form, partDisplayText);
                            context.formSystemName = validationMessageParts.RefName;
                            context.InvalidPartsDictionary.Add(partName, new InvalidPart(context.formName, validationMessageParts.SourceError));
                        }
                        break;
                }
            }
        }

        private void ResolveCondition(Context context)
        {
            ValidationMessageParts validationMessageParts;
            var currentContext = GetEvent(context.Condition).Form != null ? ContextType.FORM : ContextType.VIEW;
            XmlDocument ruleDefinition = GetRuleDefinition(currentContext);
            XmlNode conditionDefinition = ruleDefinition.SelectSingleNode("SourceCode.Forms/RuleDefinitions/Conditions/Condition[@Name=" + XmlHelper.XPathParameterEncode(context.RuleConditionName) + "]");
            XmlNodeList conditionParts = conditionDefinition.SelectNodes("Parts/Part");
            string conditionMessageText = conditionDefinition.SelectSingleNode("Message").InnerText;
            Authoring.Filters.PropertyExpression expressionProperty;

            if (string.IsNullOrEmpty(context.ConditionFriendlyName))
            {
                context.ConditionFriendlyName = string.Format(Resources.Rules.ErrorConditionFriendlyNameNotResolved, conditionMessageText);
            }

            foreach (XmlNode partNode in conditionParts)
            {
                var partName = partNode.Attributes["Name"].Value;
                var partDisplayText = partNode.SelectSingleNode("Display").InnerText;
                validationMessageParts = new ValidationMessageParts();

                switch (partName)
                {
                    case "View":
                        if (context.View == null)
                        {
                            ReferenceType[] referenceTypeArray = new ReferenceType[3] { ReferenceType.View, ReferenceType.ViewInstance, ReferenceType.SubForm };

                            PropertyExpression result = context.Condition.Expressions[0].Operands[0] as PropertyExpression;
                            validationMessageParts = GetValidationMessageParts(result.Validation, referenceTypeArray, partDisplayText);
                            // If the values are already populated, dont replace them. A missing view is not always returned above.
                            context.viewMviName = string.IsNullOrWhiteSpace(context.viewMviName) ? validationMessageParts.RefDisplayName : context.viewMviName;
                            context.viewName = string.IsNullOrWhiteSpace(context.viewName) ? validationMessageParts.RefDisplayName : context.viewName;
                            context.viewSystemName = string.IsNullOrWhiteSpace(context.viewSystemName) ? validationMessageParts.RefName : context.viewSystemName;
                            context.viewGuid = context.viewGuid.Equals(Guid.Empty) ? validationMessageParts.RefGuid : context.viewGuid;
                            context.InvalidPartsDictionary.Add(partName, new InvalidPart(context.viewName, validationMessageParts.SourceError));
                        }
                        break;
                    case "Form":
                        if (context.Form == null)
                        {
                            Authoring.Eventing.Action openAction = GetSubFormAction(null, context.Condition.SubFormGuid, context.Event);
                            WSA.Property formProp = GetPropertyByName(openAction.Properties, "FormID");
                            validationMessageParts = GetValidationMessageParts(formProp.Validation, ReferenceType.Form, partDisplayText);
                            context.formName = validationMessageParts.RefDisplayName;
                            context.formSystemName = validationMessageParts.RefName;
                            context.formGuid = validationMessageParts.RefGuid;
                            context.InvalidPartsDictionary.Add(partName, new InvalidPart(context.formName, validationMessageParts.SourceError));
                        }
                        break;
                    case "FormControl":
                    case "ViewControl":
                    case "Control":
                        if (context.Control == null)
                        {
                            expressionProperty = GetExpressionBySourceTypeFromOperands(context, PropertyExpressionSourceType.Control);
                            if (expressionProperty != null && !string.IsNullOrEmpty(expressionProperty.SourceDisplayName))
                            {
                                partDisplayText = expressionProperty.SourceDisplayName;
                            }
                            validationMessageParts = GetValidationMessageParts(expressionProperty.Validation, ReferenceType.Control, partDisplayText);
                            context.controlName = validationMessageParts.RefDisplayName;
                            context.controlSystemName = validationMessageParts.RefName;
                            context.controlGuid = validationMessageParts.RefGuid;
                            context.InvalidPartsDictionary.Add(partName, new InvalidPart(context.controlName, validationMessageParts.SourceError));
                        }
                        else
                        {
                            context.controlGuid = context.Control.Guid;
                            context.controlName = !string.IsNullOrEmpty(context.Control.DisplayName) ? context.Control.DisplayName : context.Control.Name;
                            context.controlSystemName = context.Control.Name;
                        }
                        break;

                    case "FormParameter":
                        if (context.formParameter == null)
                        {
                            PropertyExpression formParamProp = GetExpressionBySourceTypeFromOperands(context, PropertyExpressionSourceType.FormParameter);
                            validationMessageParts = GetValidationMessageParts(formParamProp.Validation, ReferenceType.FormParameter, partDisplayText);
                            context.parameterName = validationMessageParts.RefName;
                            context.parameterDisplayName = validationMessageParts.RefDisplayName;
                            context.parameterGuid = validationMessageParts.RefGuid;
                            context.InvalidPartsDictionary.Add(partName, new InvalidPart(context.parameterName, validationMessageParts.SourceError));
                        }
                        break;

                    case "ViewParameter":
                        if (context.viewParameter == null)
                        {
                            PropertyExpression viewParamProp = GetExpressionBySourceTypeFromOperands(context, PropertyExpressionSourceType.ViewParameter);
                            validationMessageParts = GetValidationMessageParts(viewParamProp.Validation, ReferenceType.ViewParameter, partDisplayText);
                            context.parameterName = validationMessageParts.RefName;
                            context.parameterDisplayName = validationMessageParts.RefDisplayName;
                            context.parameterGuid = validationMessageParts.RefGuid;
                            context.InvalidPartsDictionary.Add(partName, new InvalidPart(context.parameterName, validationMessageParts.SourceError));
                        }
                        break;

                    case "Activity":
                        if (string.IsNullOrEmpty(context.parameterName) || string.IsNullOrEmpty(context.activityDisplayName))
                        {
                            context.parameterName = partDisplayText;
                            context.InvalidPartsDictionary.Add(partName, new InvalidPart(context.parameterName, new WSF.ValidationError("")));
                        }
                        else
                        {
                            PropertyExpression activityExpression = GetExpressionBySourceTypeFromOperands(context, PropertyExpressionSourceType.WorkflowActivity);

                            if ((activityExpression.Validation.Status & Framework.ValidationStatus.Error) == Framework.ValidationStatus.Error
                                || (activityExpression.Validation.Status & Framework.ValidationStatus.Missing) == Framework.ValidationStatus.Missing)
                            {
                                context.InvalidPartsDictionary.Add(partName, new InvalidPart(context.activityDisplayName, new WSF.ValidationError(activityExpression.Validation.Messages[0].Message)));
                            }
                        }
                        break;
                    case "ConfigureActivity":
                        if (context.Condition.Properties["Name"] != null)
                        {
                            switch (context.Condition.Properties["Name"])
                            {
                                case "IsCurrentActivityContextCondition":
                                case "ViewIsCurrentActivityContextCondition":
                                case "SubViewIsCurrentActivityContextCondition":
                                case "SubFormIsCurrentActivityContextCondition":
                                case "SubFormViewIsCurrentActivityContextCondition":
                                case "FormViewIsCurrentActivityContextCondition":
                                case "FormIsCurrentActivityContextCondition":
                                case "ServerIsCurrentActivityContextCondition":
                                case "ServerViewIsCurrentActivityContextCondition":
                                case "ServerSubViewIsCurrentActivityContextCondition":
                                case "ServerSubFormIsCurrentActivityContextCondition":
                                case "ServerSubFormViewIsCurrentActivityContextCondition":
                                case "ServerFormViewIsCurrentActivityContextCondition":
                                case "ServerFormIsCurrentActivityContextCondition":

                                    string sourcesProp = context.Condition.Properties["SerialNumber"];

                                    if (string.IsNullOrEmpty(sourcesProp))
                                    {
                                        context.InvalidPartsDictionary.Add(partName, new InvalidPart("Configure", new WSF.ValidationError("")));
                                    }
                                    else
                                    {
                                        XmlDocument sourcesDoc = XmlHelper.CreateXmlDocument(sourcesProp);
                                        XmlNodeList sourcesInError = sourcesDoc.SelectNodes("//*[contains(@ValidationStatus,'Error') or contains(@ValidationStatus,'Missing')]");

                                        if (sourcesInError.Count > 0)
                                        {
                                            context.InvalidPartsDictionary.Add(partName, new InvalidPart("Configure", new WSF.ValidationError("")));
                                        }
                                    }

                                    break;
                            }
                        }

                        break;
                }
            }
        }

        private void ResolveEvent(Context context)
        {
            var currentContext = context.Event.Form != null ? ContextType.FORM : ContextType.VIEW;
            XmlDocument ruleDefinition = GetRuleDefinition(currentContext);
            XmlNode eventDefinition = ruleDefinition.SelectSingleNode("SourceCode.Forms/RuleDefinitions/Events/Event[@Name=" + XmlHelper.XPathParameterEncode(context.RuleEventName) + "]");
            XmlNodeList eventParts = eventDefinition.SelectNodes("Parts/Part");
            string eventMessageText = eventDefinition.SelectSingleNode("Message").InnerText;
            ValidationMessageParts validationMessageParts;

            if (string.IsNullOrEmpty(context.EventFriendlyName))
            {
                context.EventFriendlyName = string.Format(Resources.Rules.ErrorEventFriendlyNameNotResolved, eventMessageText);
            }

            if (string.IsNullOrEmpty(context.Location))
            {
                context.Location = Resources.Rules.ErrorRuleLocationNotResolved;
            }

            foreach (XmlNode partNode in eventParts)
            {
                var partName = partNode.Attributes["Name"].Value;
                var partDisplayText = partNode.SelectSingleNode("Display").InnerText; // Object Ref
                var partValue = partNode.SelectSingleNode("Value")?.InnerText;
                switch (partName)
                {
                    case "View":
                        if (context.View == null)
                        {
                            WSA.Property eventProp = GetPropertyByName(context.Event.Properties, "ViewID");
                            if (eventProp != null)
                            {
                                if (!string.IsNullOrEmpty(eventProp.DisplayValue))
                                {
                                    partDisplayText = eventProp.DisplayValue;
                                }
                                if (!string.IsNullOrEmpty(eventProp.Value))
                                {
                                    partValue = eventProp.Value;
                                }
                            }
                            validationMessageParts = GetValidationMessageParts(context.Event.Validation, ReferenceType.EventProperty, partDisplayText, true);
                            partDisplayText = validationMessageParts.RefDisplayName; // this seems to be a bit inconsistent, if it can find a name, use it else keep it as it placeholder text.
                            context.viewSystemName = validationMessageParts.RefName;
                            validationMessageParts = GetValidationMessageParts(context.Event.Validation, ReferenceType.View, partDisplayText);
                            context.viewName = validationMessageParts.RefDisplayName; // If this one cant find a name, use the result of the event property, which will still fall back to "select view" if it really cant find a name
                            context.viewMviName = validationMessageParts.RefDisplayName;
                            if (string.IsNullOrEmpty(context.viewSystemName))
                            {
                                context.viewSystemName = validationMessageParts.RefName;
                            }

                            context.viewGuid = validationMessageParts.RefGuid;
                            if (string.IsNullOrEmpty(context.viewName))
                            {
                                context.viewName = partDisplayText;
                            }
                            if (string.IsNullOrEmpty(context.viewName))
                            {
                                context.viewMviName = partDisplayText;
                            }

                            if (context.viewGuid.Equals(Guid.Empty) && GuidHelper.IsGuid(partValue))
                            {
                                context.viewGuid = new Guid(partValue);
                            }
                            context.InvalidPartsDictionary.Add(partName, new InvalidPart(context.viewName, validationMessageParts.SourceError));
                        }
                        break;
                    case "Form":
                        if (context.Form == null)
                        {
                            validationMessageParts = GetValidationMessageParts(context.Event.Validation, ReferenceType.Form, partDisplayText);
                            context.formName = validationMessageParts.RefDisplayName;
                            context.formSystemName = validationMessageParts.RefName;
                            context.formGuid = validationMessageParts.RefGuid;
                            context.InvalidPartsDictionary.Add(partName, new InvalidPart(context.formName, validationMessageParts.SourceError));
                        }
                        break;
                    case "FormControl":
                    case "ViewControl":
                    case "Control":
                        if (context.Control == null)
                        {
                            validationMessageParts = GetValidationMessageParts(context.Event.Validation, ReferenceType.Control, partDisplayText);
                            if (string.IsNullOrEmpty(context.controlName))
                            {
                                context.controlName = validationMessageParts.RefDisplayName;
                                context.controlGuid = validationMessageParts.RefGuid;
                            }
                            context.controlSystemName = validationMessageParts.RefName;
                            context.InvalidPartsDictionary.Add(partName, new InvalidPart(context.controlName, validationMessageParts.SourceError));
                        }
                        break;
                    case "ViewMethod":
                        context.methodDisplayName = GetMethodDisplayName(context, context.Event.Name);
                        if (string.IsNullOrEmpty(context.methodDisplayName))
                        {
                            validationMessageParts = GetValidationMessageParts(context.Event.Validation, ReferenceType.ViewMethod, partDisplayText);
                            context.methodDisplayName = validationMessageParts.RefDisplayName;
                            if (context.methodDisplayName == partDisplayText) //in case it was an object method
                            {
                                validationMessageParts = GetValidationMessageParts(context.Event.Validation, ReferenceType.ObjectMethod, partDisplayText);
                                context.methodDisplayName = validationMessageParts.RefDisplayName;
                            }
                            // if it still fails
                            if (context.methodDisplayName == partDisplayText)
                            {
                                // test if it was one of the standard ones.
                                string methodDisplayName = GetMethodDisplayName(context, context.Event.Name, true);
                                if (!string.IsNullOrEmpty(methodDisplayName))
                                {
                                    context.methodDisplayName = methodDisplayName;
                                }
                                else if (!string.IsNullOrEmpty(context.Event.Name)) // fall back to the event name.
                                {
                                    context.methodDisplayName = context.Event.Name;
                                }
                            }
                            context.InvalidPartsDictionary.Add(partName, new InvalidPart(context.methodDisplayName, validationMessageParts.SourceError));
                        }
                        break;
                    case "ControlEvent":
                        if (string.IsNullOrEmpty(context.EventName))
                        {
                            validationMessageParts = GetValidationMessageParts(context.Event.Validation, ReferenceType.ControlEvent, partDisplayText);
                            context.EventName = validationMessageParts.RefDisplayName;
                            context.InvalidPartsDictionary.Add(partName, new InvalidPart(context.panelName, validationMessageParts.SourceError));
                        }
                        else if ((context.Event.Validation.Status & Framework.ValidationStatus.Missing) == Framework.ValidationStatus.Missing)
                        {
                            validationMessageParts = GetValidationMessageParts(context.Event.Validation, ReferenceType.ControlEvent, context.EventName);
                            if (validationMessageParts.ReferenceAs == "EventMethod")
                            {
                                if (validationMessageParts.SourceError.Message.EndsWith(","))
                                {
                                    // restore display name for the tooltip, otherwise it shows the system name
                                    validationMessageParts.SourceError.Message += context.EventName;
                                }
                                context.InvalidPartsDictionary.Add(partName, new InvalidPart(context.EventName, validationMessageParts.SourceError));
                            }
                        }
                        break;
                    case "FormEvent":
                        context.methodDisplayName = GetMethodDisplayName(context, context.Event.Name);
                        if (string.IsNullOrEmpty(context.methodDisplayName))
                        {
                            validationMessageParts = GetValidationMessageParts(context.Event.Validation, ReferenceType.FormEvent, partDisplayText);
                            context.methodDisplayName = validationMessageParts.RefDisplayName;
                            context.InvalidPartsDictionary.Add(partName, new InvalidPart(context.methodDisplayName, validationMessageParts.SourceError));
                        }
                        break;
                    case "ViewParameter":
                        if (context.viewParameter == null)
                        {
                            validationMessageParts = GetValidationMessageParts(context.Event.Validation, ReferenceType.ViewParameter, partDisplayText);
                            context.parameterName = validationMessageParts.RefDisplayName;
                            context.InvalidPartsDictionary.Add(partName, new InvalidPart(context.parameterName, validationMessageParts.SourceError));
                        }
                        break;
                    case "FormParameter":
                        if (context.formParameter == null)
                        {
                            validationMessageParts = GetValidationMessageParts(context.Event.Validation, ReferenceType.FormParameter, partDisplayText);
                            context.parameterName = validationMessageParts.RefDisplayName;
                            context.InvalidPartsDictionary.Add(partName, new InvalidPart(context.parameterName, validationMessageParts.SourceError));
                        }
                        break;
                }
            }

        }

        private void ResolveAction(Context context)
        {
            ValidationMessageParts validationMessageParts;
            var currentContext = GetEvent(context.Action).Form != null ? ContextType.FORM : ContextType.VIEW;
            XmlDocument ruleDefinition = GetRuleDefinition(currentContext);
            XmlNode actionDefinition = ruleDefinition.SelectSingleNode("SourceCode.Forms/RuleDefinitions/Actions/Action[@Name=" + XmlHelper.XPathParameterEncode(context.RuleActionName) + "]");
            Authoring.Property actionProperty;

            if (actionDefinition != null)
            {
                XmlNodeList actionParts = actionDefinition.SelectNodes("Parts/Part");
                string actionMessageText = actionDefinition.SelectSingleNode("Message").InnerText;

                if (string.IsNullOrEmpty(context.ActionFriendlyName))
                {
                    context.ActionFriendlyName = string.Format(Resources.Rules.ErrorActionFriendlyNameNotResolved, actionMessageText);
                }

                foreach (XmlNode partNode in actionParts)
                {
                    var partName = partNode.Attributes["Name"].Value;
                    var partDisplayText = partNode.SelectSingleNode("Display").InnerText;
                    ReferenceType[] referenceTypeArray;

                    switch (partName)
                    {
                        case "View":
                            if (context.View == null)
                            {
                                referenceTypeArray = new ReferenceType[3] { ReferenceType.View, ReferenceType.ViewInstance, ReferenceType.SubForm };
                                actionProperty = GetPropertyByName(context.Action.Properties, "ViewID");
                                if (actionProperty != null && !string.IsNullOrEmpty(actionProperty.DisplayValue))
                                {
                                    partDisplayText = actionProperty.DisplayValue;
                                    Guid viewGuid = Guid.Empty;
                                    if (Guid.TryParse(actionProperty.Value, out viewGuid))
                                    {
                                        context.viewGuid = viewGuid;
                                    }
                                }

                                validationMessageParts = GetValidationMessageParts(actionProperty.Validation, referenceTypeArray, partDisplayText);
                                context.viewMviName = validationMessageParts.RefDisplayName;
                                context.viewName = validationMessageParts.RefDisplayName;
                                context.viewSystemName = validationMessageParts.RefName;
                                context.viewGuid = context.viewGuid.Equals(Guid.Empty) ? validationMessageParts.RefGuid : context.viewGuid; //if it has a value, dont overwrite it
                                context.InvalidPartsDictionary.Add(partName, new InvalidPart(context.viewName, validationMessageParts.SourceError));
                            }
                            else
                            {
                                context.viewName = context.viewMviName;
                                context.viewSystemName = context.View.Name;
                            }
                            break;
                        case "Form":
                            if (context.Form == null)
                            {
                                referenceTypeArray = new ReferenceType[2] { ReferenceType.Form, ReferenceType.SubForm };
                                actionProperty = GetPropertyByName(context.Action.Properties, "FormID");
                                if (actionProperty != null && !string.IsNullOrEmpty(actionProperty.DisplayValue))
                                {
                                    partDisplayText = actionProperty.DisplayValue;
                                }

                                validationMessageParts = GetValidationMessageParts(actionProperty.Validation, referenceTypeArray, partDisplayText);
                                context.formName = validationMessageParts.RefDisplayName;
                                context.formSystemName = validationMessageParts.RefName;
                                context.formGuid = validationMessageParts.RefGuid;
                                context.InvalidPartsDictionary.Add(partName, new InvalidPart(context.formName, validationMessageParts.SourceError));
                            }
                            else
                            {
                                context.formName = context.Form.DisplayName;
                                context.formSystemName = context.Form.Name;
                                context.formGuid = context.Form.Guid;
                            }
                            break;
                        case "AreaItem":
                            if (context.Control == null)
                            {
                                actionProperty = GetPropertyByName(context.Action.Properties, "ControlID");
                                if (actionProperty != null && !string.IsNullOrEmpty(actionProperty.DisplayValue))
                                {
                                    partDisplayText = actionProperty.DisplayValue;
                                }

                                validationMessageParts = GetValidationMessageParts(actionProperty.Validation, ReferenceType.Control, partDisplayText);
                                context.controlName = validationMessageParts.RefDisplayName;
                                context.controlSystemName = validationMessageParts.RefName;
                                context.InvalidPartsDictionary.Add(partName, new InvalidPart(context.controlName, validationMessageParts.SourceError));
                            }
                            else
                            {
                                context.controlGuid = context.Control.Guid;
                                context.controlName = context.viewMviName;
                                context.controlSystemName = context.Control.Name;
                            }
                            break;
                        case "FormControl":
                        case "ViewControl":
                        case "Control":
                            if (context.Control == null)
                            {
                                actionProperty = GetPropertyByName(context.Action.Properties, "ControlID");
                                if (actionProperty != null && !string.IsNullOrEmpty(actionProperty.DisplayValue))
                                {
                                    partDisplayText = actionProperty.DisplayValue;
                                }

                                validationMessageParts = GetValidationMessageParts(actionProperty.Validation, ReferenceType.Control, partDisplayText);
                                context.controlName = validationMessageParts.RefDisplayName;
                                context.controlSystemName = validationMessageParts.RefName;
                                context.controlGuid = validationMessageParts.RefGuid;
                                context.InvalidPartsDictionary.Add(partName, new InvalidPart(context.controlName, validationMessageParts.SourceError));
                            }
                            else
                            {
                                context.controlGuid = context.Control.Guid;
                                context.controlName = !string.IsNullOrEmpty(context.Control.DisplayName) ? context.Control.DisplayName : context.Control.Name;
                                context.controlSystemName = context.Control.Name;
                            }
                            break;
                        case "Panel":
                            if (context.Panel == null)
                            {
                                actionProperty = GetPropertyByName(context.Action.Properties, "PanelID");
                                if (actionProperty != null && !string.IsNullOrEmpty(actionProperty.DisplayValue))
                                {
                                    partDisplayText = actionProperty.DisplayValue;
                                }

                                validationMessageParts = GetValidationMessageParts(actionProperty.Validation, ReferenceType.Panel, partDisplayText);
                                context.panelName = validationMessageParts.RefDisplayName;
                                context.panelSystemName = validationMessageParts.RefName;
                                context.panelGuid = validationMessageParts.RefGuid;
                                context.InvalidPartsDictionary.Add(partName, new InvalidPart(context.panelName, validationMessageParts.SourceError));
                            }
                            else
                            {
                                if (!string.IsNullOrEmpty(context.Panel.DisplayName))
                                {
                                    context.panelName = context.Panel.DisplayName;
                                }
                                else
                                {
                                    context.panelName = context.Panel.Properties["Title"];
                                }
                                context.panelSystemName = context.Panel.Name;
                                context.panelGuid = context.Panel.Guid;
                            }
                            break;
                        case "ViewMethod":
                            // Retrieve method name from property rather than lookup against SmO
                            actionProperty = GetPropertyByName(context.Action.Properties, "Method");
                            context.methodDisplayName = actionProperty.DisplayValue ?? actionProperty.Value;
                            if ((actionProperty.Validation.Status & WSF.ValidationStatus.Missing) == WSF.ValidationStatus.Missing
                                || (actionProperty.Validation.Status & WSF.ValidationStatus.Error) == WSF.ValidationStatus.Error)
                            {
                                referenceTypeArray = new ReferenceType[2] { ReferenceType.ObjectMethod, ReferenceType.ViewMethod };
                                if (actionProperty != null && !string.IsNullOrEmpty(actionProperty.DisplayValue))
                                {
                                    partDisplayText = actionProperty.DisplayValue;
                                }

                                validationMessageParts = GetValidationMessageParts(actionProperty.Validation, referenceTypeArray, partDisplayText);
                                context.methodDisplayName = validationMessageParts.RefDisplayName;
                                context.InvalidPartsDictionary.Add(partName, new InvalidPart(context.methodDisplayName, validationMessageParts.SourceError));
                            }
                            break;
                        case "Object":
                            if (string.IsNullOrEmpty(context.ObjectName))
                            {
                                actionProperty = GetPropertyByName(context.Action.Properties, "ObjectID");
                                if (actionProperty != null && !string.IsNullOrEmpty(actionProperty.DisplayValue))
                                {
                                    partDisplayText = actionProperty.DisplayValue;
                                }

                                validationMessageParts = GetValidationMessageParts(actionProperty.Validation, ReferenceType.Object, partDisplayText);
                                context.ObjectName = validationMessageParts.RefDisplayName;
                                context.ObjectSystemName = validationMessageParts.RefName;
                                context.ObjectGuid = validationMessageParts.RefGuid;
                                context.InvalidPartsDictionary.Add(partName, new InvalidPart(context.ObjectName, validationMessageParts.SourceError));
                            }
                            break;
                        case "ObjectMethod":
                            // Retrieve method name from property rather than lookup against SmO
                            actionProperty = GetPropertyByName(context.Action.Properties, "Method");
                            context.methodDisplayName = actionProperty.DisplayValue;
                            if ((actionProperty.Validation.Status & WSF.ValidationStatus.Missing) == WSF.ValidationStatus.Missing
                                || (actionProperty.Validation.Status & WSF.ValidationStatus.Error) == WSF.ValidationStatus.Error)
                            {

                                if (actionProperty != null && !string.IsNullOrEmpty(actionProperty.DisplayValue))
                                {
                                    partDisplayText = actionProperty.DisplayValue;
                                }

                                validationMessageParts = GetValidationMessageParts(actionProperty.Validation, ReferenceType.ObjectMethod, partDisplayText);
                                context.methodDisplayName = validationMessageParts.RefDisplayName;
                                context.InvalidPartsDictionary.Add(partName, new InvalidPart(context.methodDisplayName, validationMessageParts.SourceError));
                            }
                            break;
                        case "FormMethod":
                            context.methodDisplayName = GetMethodDisplayName(context, context.Action.Method);
                            if (string.IsNullOrEmpty(context.methodDisplayName))
                            {
                                context.methodDisplayName = partDisplayText;
                                context.InvalidPartsDictionary.Add(partName, new InvalidPart(context.methodDisplayName, new WSF.ValidationError("")));
                            }
                            break;
                        case "Rule":
                            if (string.IsNullOrEmpty(context.RuleFriendlyName))
                            {
                                actionProperty = GetPropertyByName(context.Action.Properties, "EventID");
                                if (actionProperty != null && !string.IsNullOrEmpty(actionProperty.DisplayValue))
                                {
                                    partDisplayText = actionProperty.DisplayValue;
                                }

                                validationMessageParts = GetValidationMessageParts(actionProperty.Validation, ReferenceType.Event, partDisplayText);
                                context.RuleFriendlyName = validationMessageParts.RefDisplayName.TrimStart(new char[] { '"' }).TrimEnd(new char[] { '"' }); // Remove leading and trailing quote symbols added by the analyzer
                                context.InvalidPartsDictionary.Add(partName, new InvalidPart(context.RuleFriendlyName, validationMessageParts.SourceError));
                            }
                            break;
                        case "Process":
                            if (string.IsNullOrEmpty(context.processDisplayName)
                                || (context.Action.Properties.Get("ProcessName").Validation.Status & Framework.ValidationStatus.Error) == Framework.ValidationStatus.Error
                                || (context.Action.Properties.Get("ProcessName").Validation.Status & Framework.ValidationStatus.Missing) == Framework.ValidationStatus.Missing)
                            {
                                actionProperty = GetPropertyByName(context.Action.Properties, "ProcessName");
                                if (actionProperty != null && !string.IsNullOrEmpty(actionProperty.DisplayValue))
                                {
                                    partDisplayText = actionProperty.DisplayValue;
                                }

                                validationMessageParts = GetValidationMessageParts(actionProperty.Validation, ReferenceType.WorkflowProcessProperty, partDisplayText);
                                context.processFullName = validationMessageParts.RefDisplayName;
                                context.InvalidPartsDictionary.Add(partName, new InvalidPart(context.processFullName, validationMessageParts.SourceError));
                            }
                            break;
                        case "Activity":
                            if (string.IsNullOrEmpty(context.activityDisplayName)
                                || (context.Action.Properties.Get("ActivityFullName").Validation.Status & Framework.ValidationStatus.Error) == Framework.ValidationStatus.Error
                                || (context.Action.Properties.Get("ActivityFullName").Validation.Status & Framework.ValidationStatus.Missing) == Framework.ValidationStatus.Missing)
                            {
                                actionProperty = GetPropertyByName(context.Action.Properties, "ActivityFullName");
                                if (actionProperty != null && !string.IsNullOrEmpty(actionProperty.DisplayValue))
                                {
                                    partDisplayText = actionProperty.DisplayValue;
                                }

                                validationMessageParts = GetValidationMessageParts(actionProperty.Validation, ReferenceType.WorkflowActivityProperty, partDisplayText);
                                context.activityName = validationMessageParts.RefDisplayName;
                                context.InvalidPartsDictionary.Add(partName, new InvalidPart(context.activityName, validationMessageParts.SourceError));
                            }
                            break;
                        case "ControlMethod":
                            actionProperty = GetPropertyByName(context.Action.Properties, "Method");
                            context.methodDisplayName = actionProperty.DisplayValue;
                            if ((actionProperty.Validation.Status & WSF.ValidationStatus.Missing) == WSF.ValidationStatus.Missing
                                || (actionProperty.Validation.Status & WSF.ValidationStatus.Error) == WSF.ValidationStatus.Error)
                            {
                                referenceTypeArray = new ReferenceType[1] { ReferenceType.ControlMethod };
                                if (actionProperty != null && !string.IsNullOrEmpty(actionProperty.DisplayValue))
                                {
                                    partDisplayText = actionProperty.DisplayValue;
                                }

                                validationMessageParts = GetValidationMessageParts(actionProperty.Validation, referenceTypeArray, partDisplayText);
                                context.methodDisplayName = validationMessageParts.RefDisplayName;
                                context.InvalidPartsDictionary.Add(partName, new InvalidPart(context.methodDisplayName, validationMessageParts.SourceError));
                            }
                            break;
                    }
                }
            }
        }

        private PropertyExpression GetExpressionBySourceTypeFromOperands(Context context, PropertyExpressionSourceType sourceType)
        {
            PropertyExpression result = null;
            for (int j = 0; j < context.Condition.Expressions.Count && result == null; j++)
            {
                ExpressionCollection operands = context.Condition.Expressions[j].Operands;
                for (int i = 0; i < operands.Count && result == null; i++)
                {
                    PropertyExpression prop = operands[i] as PropertyExpression;
                    if (prop != null && prop.SourceType == sourceType)
                    {
                        result = prop;
                    }
                }
            }
            return result;
        }

        // Required because Action.Properties[index] != Action.Properties["Name"], ditto Events, etc
        // the former returns the properties object (correct) the later returns the property value (incorrect)
        private Authoring.Property GetPropertyByName(WSA.PropertyCollection properties, string name)
        {
            Authoring.Property prop = null;
            for (int i = 0; i < properties.Count && prop == null; i++)
            {
                if (properties[i].Name.Equals(name, StringComparison.InvariantCultureIgnoreCase))
                {
                    prop = properties[i];
                }
            }
            return prop;
        }

        private PropertyExpression GetPropertyExpressionBySourceType(ExpressionCollection expressions, PropertyExpressionSourceType sourceType)
        {
            PropertyExpression prop = null;
            for (int i = 0; i < expressions.Count && prop == null; i++)
            {
                if (((PropertyExpression)expressions[i]).SourceType == sourceType)
                {
                    prop = (PropertyExpression)expressions[i];
                }
            }
            return prop;
        }

        private ValidationMessageParts GetValidationMessageParts(WSF.ValidationResult validation, ReferenceType[] refType, string defaultDisplayValue, bool useDefaultInsteadOfSystemName = false)
        {
            ValidationMessageParts parts = new ValidationMessageParts();
            if (validation == null)
            {
                parts.RefDisplayName = defaultDisplayValue;
                parts.RefGuid = Guid.Empty;
                return parts;
            }

            WSF.ValidationError error = null;
            for (int i = 0; i < validation.Messages.Count && error == null; i++)
            {
                parts = new ValidationMessageParts(validation.Messages[i]);
                if (refType.Contains(parts.RefType))
                {
                    error = validation.Messages[i];
                }
            }

            if (error == null)
            {
                parts = new ValidationMessageParts();
                parts.RefDisplayName = defaultDisplayValue;
                parts.RefGuid = Guid.Empty;
                return parts;
            }

            string refDisplayName = string.Empty;

            if (!string.IsNullOrEmpty(parts.RefDisplayName))
            {
                refDisplayName = parts.RefDisplayName; // display name
            }

            if (string.IsNullOrEmpty(refDisplayName) && !useDefaultInsteadOfSystemName)
            {
                refDisplayName = parts.RefName; // system name?
            }

            if (string.IsNullOrEmpty(refDisplayName))
            {
                refDisplayName = defaultDisplayValue;
            }

            parts.RefDisplayName = refDisplayName;

            return parts;
        }

        private ValidationMessageParts GetValidationMessageParts(WSF.ValidationResult validation, ReferenceType refType, string defaultDisplayValue, bool useDefaultInsteadOfSystemName = false)
        {
            ValidationMessageParts parts = new ValidationMessageParts();
            if (validation == null)
            {
                parts.RefDisplayName = defaultDisplayValue;
                parts.RefGuid = Guid.Empty;
                return parts;
            }

            WSF.ValidationError error = null;
            for (int i = 0; i < validation.Messages.Count && error == null; i++)
            {
                parts = new ValidationMessageParts(validation.Messages[i]);
                if (parts.RefType == refType)
                {
                    error = validation.Messages[i];
                }
            }

            if (error == null)
            {
                parts = new ValidationMessageParts();
                parts.RefDisplayName = defaultDisplayValue;
                parts.RefGuid = Guid.Empty;
                return parts;
            }

            string refDisplayName = string.Empty;

            if (!string.IsNullOrEmpty(parts.RefDisplayName))
            {
                refDisplayName = parts.RefDisplayName; // display name
            }

            if (string.IsNullOrEmpty(refDisplayName) && !useDefaultInsteadOfSystemName)
            {
                refDisplayName = parts.RefName; // system name?
            }

            if (string.IsNullOrEmpty(refDisplayName))
            {
                refDisplayName = defaultDisplayValue;
            }

            parts.RefDisplayName = refDisplayName;

            return parts;
        }

        private void ValidatePartValues(XmlNode partsNode, Context context)
        {
            if (context.InvalidPartsDictionary != null && context.InvalidPartsDictionary.Count > 0)
            {
                foreach (KeyValuePair<string, InvalidPart> item in context.InvalidPartsDictionary)
                {
                    XmlNode partNode = partsNode.SelectSingleNode("Part[@Name=" + XmlHelper.XPathParameterEncode(item.Key) + "]");
                    if (partNode != null)
                    {
                        if (partsNode.Attributes["Invalid"] == null)
                        {
                            partNode.Attributes.Append(partNode.OwnerDocument.CreateAttribute("Invalid"));
                        }

                        partNode.Attributes["Invalid"].Value = "true";

                        if (item.Value.Error != null && !string.IsNullOrEmpty(item.Value.Error.Message))
                        {
                            if (partsNode.Attributes["ValidationMessages"] == null)
                            {
                                partNode.Attributes.Append(partNode.OwnerDocument.CreateAttribute("ValidationMessages"));
                                partNode.Attributes["ValidationMessages"].Value = item.Value.Error.Message;
                            }
                            else
                            {
                                partNode.Attributes["ValidationMessages"].Value = string.Format("{0};{1}", partNode.Attributes["ValidationMessages"].Value, item.Value.Error.Message);
                            }
                        }
                    }
                }
            }
        }

        private string GetMethodDisplayName(Context context, string methodName, bool force = false)
        {
            string methodDisplayName = string.Empty;
            string displayName;
            FormsHelper helper;

            if (!context.ObjectGuid.Equals(Guid.Empty) && InfoProvider.TryGetSmartMethodDisplayName(context.ObjectGuid, methodName, out displayName))
            {
                methodDisplayName = displayName;
            }

            if ((force || context.View != null) && string.IsNullOrEmpty(methodDisplayName))
            {
                if (context.View != null && context.View.Source != null)
                {
                    Guid smartObjectGuid = new Guid(context.View.Source.SourceID);

                    if (!smartObjectGuid.Equals(Guid.Empty) && InfoProvider.TryGetSmartMethodDisplayName(smartObjectGuid, methodName, out displayName))
                    {
                        methodDisplayName = displayName;
                    }
                }

                if (string.IsNullOrEmpty(methodDisplayName))
                {
                    helper = new FormsHelper(GetConnection(), _enabledFeatures);
                    ItemCollection items = helper.GetItems(ResultTypes.ViewEvents | ResultTypes.ViewMethods);

                    foreach (Item item in items)
                    {
                        if (item.Name == methodName)
                        {
                            methodDisplayName = item.DisplayName;
                            break;
                        }
                    }
                }
            }

            if ((force || context.Form != null) && string.IsNullOrEmpty(methodDisplayName))
            {
                helper = new FormsHelper(GetConnection(), _enabledFeatures);
                ItemCollection items = helper.GetItems(ResultTypes.FormEvents);

                foreach (Item item in items)
                {
                    if (item.Name == methodName)
                    {
                        methodDisplayName = item.DisplayName;
                        break;
                    }
                }
            }

            return methodDisplayName;
        }

        private string GetGuidString(Guid guid)
        {
            return guid == Guid.Empty ? "" : guid.ToString();
        }

        private Authoring.Eventing.Action GetSubFormAction(Context context, Guid subformGuid, Event @event)
        {
            var result = @event.State.FindSubActions((action) =>
            {
                return false;
            }, (action) => action.SubFormGuid == subformGuid, true);


            if (result.Count > 1 && @event.InstanceGuid != Guid.Empty)
            {
                var instanceMatchResults = result.Where(action => action.InstanceGuid == @event.InstanceGuid)
                               .ToList();

                if (instanceMatchResults.Count > 0)
                {
                    result = instanceMatchResults;
                }
            }

            if (result.Count > 1)
            {
                //Best effort/hack to prioritize non references when we can't determine which one is being searched for
                var nonRefList = result.Where(action => !action.IsReference)
                               .ToList();

                if (nonRefList.Count > 0)
                {
                    result = nonRefList;
                }
            }

            if (result != null && result.Count > 0)
            {
                if (context != null)
                {
                    context.SubItemAction = result[0];
                }
                return result[0];
            }
            return null;
        }

        private string GetEventFriendlyNameForSubForm(Event @event)
        {
            string value = string.Empty;
            if (@event.Form == null && @event.View == null)
            {
                return value;
            }

            Guid subformGuid = @event.SubFormGuid;
            StateCollection states = @event.Form != null ? @event.Form.States : @event.View.States;

            var result = states.FindSubActions((action) =>
            {
                return false;
            }, (action) => action.SubFormGuid == subformGuid);

            
            if (result.Count > 1 && @event.InstanceGuid != Guid.Empty)
            {
                var instanceMatchResults = result.Where(action => action.InstanceGuid == @event.InstanceGuid)
                               .ToList();
                if (instanceMatchResults.Count > 0)
                {
                    result = instanceMatchResults;
                }
            }

            {
                WSA.Eventing.Action action = null;

                if (result != null && result.Count > 0)
                {
                    action = result[0];
                }
                if (action != null)
                {
                    var ev = action.Handler.Event;
                    if (string.IsNullOrEmpty(ev.Properties["RuleFriendlyName"]))
                    {
                        Context context = BuildContext(ev);
                        value = context.RuleFriendlyName;
                        ev.Properties.Set("RuleFriendlyName", value);
                    }
                    else
                    {
                        value = ev.Properties["RuleFriendlyName"];
                    }
                }
            }


            return value;
        }

        private string GetEventFriendlyNameForSubForm(Authoring.Eventing.Action action)
        {
            string value = string.Empty;
            Event ev = GetEvent(action);
            if ((string.IsNullOrEmpty(ev.Properties["RuleFriendlyName"]) || action.IsReference) && !_eventBuildContextPerformed.Contains(ev.Guid))
            {
                Context context = BuildContext(ev);
                value = context.RuleFriendlyName;
            }
            else
            {
                value = ev.Properties["RuleFriendlyName"];
            }

            return value;
        }

        private string GetResource(string resourceName, string value)
        {
            string tmpValue = SR.GetString(string.Format(resourceName, value));

            if (string.IsNullOrEmpty(tmpValue))
            {
                return value;
            }
            else
            {
                return tmpValue;
            }
        }

        private void BuildEventFriendlyName(Context context)
        {
            var eventContext = context.Event.View == null ? ContextType.FORM : ContextType.VIEW;
            XmlDocument ruleDefinition = GetRuleDefinition(eventContext);

            XmlNode ruleDefinitionEvent = ruleDefinition.SelectSingleNode("SourceCode.Forms/RuleDefinitions/Events/Event[@Name=" + XmlHelper.XPathParameterEncode(context.RuleEventName) + "]");
            if (ruleDefinitionEvent != null)
            {
                string ruleDefinintionEventDescription = ruleDefinitionEvent.SelectSingleNode("Description").InnerText;
                XmlNodeList ruleDefinitionEventParts = ruleDefinitionEvent.SelectNodes("Parts/Part");
                List<string> partsValueList = new List<string>();

                foreach (XmlNode part in ruleDefinitionEventParts)
                {
                    string partName = part.Attributes["Name"].Value;

                    if (part.Attributes["Hidden"] == null || part.Attributes["Hidden"].Value != "True")
                    {
                        switch (partName)
                        {
                            case "View":
                                partsValueList.Add(context.viewMviName);
                                break;
                            case "Form":
                                partsValueList.Add(context.formName);
                                break;
                            case "ViewControl":
                            case "FormControl":
                            case "Control":
                                partsValueList.Add(context.controlName);
                                break;
                            case "ViewParameter":
                            case "FormParameter":
                                partsValueList.Add(context.parameterName);
                                break;
                            case "ControlEvent":
                                partsValueList.Add(context.EventName);
                                break;
                            case "ViewMethod":
                                partsValueList.Add(context.methodDisplayName);
                                break;
                            case "FormEvent":
                                partsValueList.Add(context.methodDisplayName);
                                break;
                            case "ObjectMethod":
                                partsValueList.Add(context.methodDisplayName);
                                break;
                            case "ItemStates":
                                partsValueList.Add(context.Action.ItemState.ToString());
                                break;
                            case "ValueInput":
                                partsValueList.Add(context.Action.Properties["ValueInput"]);
                                break;
                            case "HeadingValueInput":
                                partsValueList.Add(context.Action.Properties["Heading"]);
                                break;
                            case "MessageValueInput":
                                partsValueList.Add(context.Action.Properties["Message"]);
                                break;
                            case "Url":
                                partsValueList.Add(context.Action.Properties["Url"]);
                                break;
                            case "Rule":
                                partsValueList.Add(context.RuleEventName);
                                break;
                            case "Process":
                                string processDisplayName = context.Action.Properties.GetDisplayValue("ProcessName");

                                if (string.IsNullOrEmpty(processDisplayName))
                                {
                                    string[] procNameSplits = context.Action.Properties["ProcessName"].Split("\\".ToCharArray());
                                    processDisplayName = procNameSplits[procNameSplits.Length - 1];
                                }

                                partsValueList.Add(processDisplayName);
                                break;
                            case "Activity":
                                if (context.Action.Properties.Contains("ActivityFullName"))
                                {
                                    string activityDisplayName = context.Action.Properties.GetDisplayValue("ActivityFullName");

                                    if (string.IsNullOrEmpty(activityDisplayName))
                                    {
                                        string[] actNameSplits = context.Action.Properties["ActivityFullName"].Split("\\".ToCharArray());
                                        activityDisplayName = actNameSplits[actNameSplits.Length - 1];
                                    }
                                    partsValueList.Add(activityDisplayName);
                                }
                                else
                                {
                                    partsValueList.Add("");
                                }
                                break;
                            case "Panel":
                                partsValueList.Add(context.panelName);
                                break;
                            case "Object":
                                partsValueList.Add(context.ObjectName);
                                break;
                            case "ParameterEvent":
                                partsValueList.Add(context.EventName);
                                break;
                        }
                    }
                    else
                    {
                        partsValueList.Add("");
                    }
                }

                context.EventFriendlyName = string.Format(ruleDefinintionEventDescription, partsValueList.ToArray());
                context.RuleFriendlyName = context.EventFriendlyName;
            }
        }

        private void BuildActionFriendlyName(Context context)
        {
            var actionContext = context.Event.View == null ? ContextType.FORM : ContextType.VIEW;
            XmlDocument ruleDefinition = GetRuleDefinition(actionContext);

            if (context.Action.ActionType != ActionType.Validate)
            {
                XmlNode ruleDefinitionAction = ruleDefinition.SelectSingleNode("SourceCode.Forms/RuleDefinitions/Actions/Action[@Name=" + XmlHelper.XPathParameterEncode(context.RuleActionName) + "]");

                if (ruleDefinitionAction != null)
                {
                    string ruleDefinintionActionDescription = ruleDefinitionAction.SelectSingleNode("Description").InnerText;
                    XmlNodeList ruleDefinitionActionParts = ruleDefinitionAction.SelectNodes("Parts/Part");
                    List<string> partsValueList = new List<string>();

                    foreach (XmlNode part in ruleDefinitionActionParts)
                    {
                        string partName = part.Attributes["Name"].Value;

                        if (part.Attributes["Hidden"] == null || part.Attributes["Hidden"].Value != "True")
                        {
                            switch (partName)
                            {
                                case "View":
                                    partsValueList.Add(context.viewMviName);
                                    break;
                                case "Form":
                                    partsValueList.Add(context.formName);
                                    break;
                                case "AreaItem":
                                    partsValueList.Add(context.controlName);
                                    break;
                                case "ViewControl":
                                case "FormControl":
                                case "Control":
                                    partsValueList.Add(context.controlName);
                                    break;
                                case "ViewMethod":
                                    partsValueList.Add(context.methodDisplayName);
                                    break;
                                case "FormMethod":
                                    partsValueList.Add(context.methodDisplayName);
                                    break;
                                case "ExecutionType":
                                    partsValueList.Add(TransformListenerToDisplayName(context.Action.ExecutionType.ToString()));
                                    break;
                                case "ObjectMethod":
                                    partsValueList.Add(context.methodDisplayName);
                                    break;
                                case "ItemStates":
                                    partsValueList.Add(context.Action.ItemState.ToString());
                                    break;
                                case "ValueInput":
                                    partsValueList.Add(context.Action.Properties["ValueInput"]);
                                    break;
                                case "HeadingValueInput":
                                    partsValueList.Add(context.Action.Properties["Heading"]);
                                    break;
                                case "MessageValueInput":
                                    partsValueList.Add(context.Action.Properties["Message"]);
                                    break;
                                case "Url":
                                    partsValueList.Add(context.Action.Properties["Url"]);
                                    break;
                                case "Rule":
                                    partsValueList.Add(context.RuleFriendlyName);
                                    break;
                                case "Process":
                                    string processDisplayName = context.Action.Properties.GetDisplayValue("ProcessName");

                                    if (string.IsNullOrEmpty(processDisplayName))
                                    {
                                        string[] procNameSplits = context.Action.Properties["ProcessName"].Split("\\".ToCharArray());
                                        processDisplayName = procNameSplits[procNameSplits.Length - 1];
                                    }

                                    partsValueList.Add(processDisplayName);
                                    break;
                                case "Activity":
                                    if (context.Action.Properties.Contains("ActivityFullName"))
                                    {
                                        string activityDisplayName = context.Action.Properties.GetDisplayValue("ActivityFullName");

                                        if (string.IsNullOrEmpty(activityDisplayName))
                                        {
                                            string[] actNameSplits = context.Action.Properties["ActivityFullName"].Split("\\".ToCharArray());
                                            activityDisplayName = actNameSplits[actNameSplits.Length - 1];
                                        }

                                        partsValueList.Add(activityDisplayName);
                                    }
                                    else
                                    {
                                        partsValueList.Add("");
                                    }
                                    break;
                                case "Panel":
                                    partsValueList.Add(context.panelName);
                                    break;
                                case "Object":
                                    partsValueList.Add(context.ObjectName);
                                    break;
                                case "ControlMethod":
                                    partsValueList.Add(context.methodDisplayName);
                                    break;
                            }
                        }
                        else
                        {
                            partsValueList.Add("");
                        }
                    }

                    context.ActionFriendlyName = string.Format(ruleDefinintionActionDescription, partsValueList.ToArray());
                }
            }
            else
            {
                context.ActionFriendlyName = Resources.RuleHelper.ConditionValidate.ToString();
            }
        }

        private void BuildHandlerFriendlyName(Context context)
        {
            var handlerContext = context.Event.Form != null ? ContextType.FORM : ContextType.VIEW;
            XmlDocument ruleDefinition = GetRuleDefinition(handlerContext);
            XmlNode handlerDefinition = ruleDefinition.SelectSingleNode("SourceCode.Forms/RuleDefinitions/Handlers/Handler[@Name=" + XmlHelper.XPathParameterEncode(context.RuleHandlerName) + "]");
            XmlNodeList handlerParts = handlerDefinition.SelectNodes("Parts/Part");
            string handlerMessageText = handlerDefinition.SelectSingleNode("Message").InnerText;

            if (string.IsNullOrEmpty(context.Location))
            {
                context.Location = Resources.Rules.ErrorRuleLocationNotResolved;
            }

            if (handlerDefinition != null)
            {
                string ruleDefinintionHandlerDescription = handlerDefinition.SelectSingleNode("Description").InnerText;
                XmlNodeList ruleDefinitionHandlerParts = handlerDefinition.SelectNodes("Parts/Part");
                List<string> partsValueList = new List<string>();

                foreach (XmlNode part in ruleDefinitionHandlerParts)
                {
                    string partName = part.Attributes["Name"].Value;

                    if (part.Attributes["Hidden"] == null || part.Attributes["Hidden"].Value != "True")
                    {
                        switch (partName)
                        {
                            case "View":
                                partsValueList.Add(context.viewMviName);
                                break;
                            case "Form":
                                partsValueList.Add(context.formName);
                                break;
                            case "ViewControl":
                            case "FormControl":
                            case "Control":
                                partsValueList.Add(context.controlName);
                                break;
                            case "ItemStates":
                                partsValueList.Add(context.itemState.ToString());
                                break;
                        }
                    }
                    else
                    {
                        partsValueList.Add("");
                    }
                }

                context.HandlerFriendlyName = string.Format(ruleDefinintionHandlerDescription, partsValueList.ToArray());
            }
        }

        private string FormatEventName(string eventResource, string formName, string viewName, string controlName, string eventName)
        {
            return string.Format(eventResource, formName, viewName, controlName, eventName);
        }

        private string FormatConditionName(string conditionResource, string value, Condition condition)
        {
            string conditionPrefix = Resources.RuleHelper.ConditionIf;

            if (condition.Handler.Conditions.IndexOf(condition) > 0)
            {
                conditionPrefix = Resources.RuleHelper.ConditionAnd;
            }

            return string.Format(conditionResource, value, conditionPrefix);
        }

        private string FormatConditionName(string conditionResource, string objName, string value, Condition condition)
        {
            string conditionPrefix = Resources.RuleHelper.ConditionIf;

            if (condition.Handler.Conditions.IndexOf(condition) > 0)
            {
                conditionPrefix = Resources.RuleHelper.ConditionAnd;
            }

            return string.Format(conditionResource, objName, value, conditionPrefix);
        }

        private string FormatConditionName(string conditionResource, string param1, string param2, string value, Condition condition)
        {
            string conditionPrefix = Resources.RuleHelper.ConditionIf;

            if (condition.Handler.Conditions.IndexOf(condition) > 0)
            {
                conditionPrefix = Resources.RuleHelper.ConditionAnd;
            }

            return string.Format(conditionResource, param1, param2, value, conditionPrefix);
        }

        private string FormatConditionName(string conditionResource, string formName, string viewName, string controlName, string value, Condition condition)
        {
            string conditionPrefix = Resources.RuleHelper.ConditionIf;

            if (condition.Handler.Conditions.IndexOf(condition) > 0)
            {
                conditionPrefix = Resources.RuleHelper.ConditionAnd;
            }

            return string.Format(conditionResource, formName, viewName, controlName, value, conditionPrefix);
        }

        private string FormatEventName(string eventResource, string formName, string viewName, string methodName)
        {
            return string.Format(eventResource, formName, viewName, methodName);
        }

        private string FormatEventName(string eventResource, string formName, string viewName)
        {
            return string.Format(eventResource, formName, viewName);
        }

        private void PushToFormAndViewCache(IBaseNamedObject container)
        {
            if (container != null)
            {
                _formAndViewCache[container.Guid] = container;
            }
        }

        private T GetFormOrView<T>(Guid guid)
            where T : IBaseNamedObject
        {
            IBaseNamedObject formOrView = null;

            bool result = _formAndViewCache.TryGetValue(guid, out formOrView);

            if (!result)
            {
                if (typeof(T) == typeof(Form))
                {
                    formOrView = InfoProvider.GetForm(guid);
                }
                else
                {
                    formOrView = InfoProvider.GetView(guid);
                }
                _formAndViewCache.Add(guid, formOrView);
            }

            return (T)formOrView;
        }

        #region Action Collections XML -> API

        private void UpdateActionCollectionsFromMappingsXml(XmlNode ruleAction, WSA.Eventing.Action action)
        {
            CreateMappings(ruleAction, action);
            CreatePropertiesFromMappingsXml(ruleAction, action);
            CreateListenerResults(ruleAction, action);
        }

        private void CreatePropertiesFromMappingsXml(XmlNode ruleConditionPart, Authoring.Eventing.Condition condition)
        {
            string mappingsXmlStr = XmlHelper.GetNodeText(ruleConditionPart.SelectSingleNode("Value"), ".", "<Mappings />");

            if (!string.IsNullOrEmpty(mappingsXmlStr))
            {
                XmlDocument mappingsDoc = XmlHelper.CreateXmlDocument(mappingsXmlStr);
                XmlNodeList mappingsList = mappingsDoc.SelectNodes("Mappings/Mapping");
                foreach (XmlElement propertyMapping in mappingsList)
                {
                    if (propertyMapping.ChildNodes.Count > 0)
                    {
                        XmlNode targetItem = propertyMapping.SelectSingleNode("Item[@ContextType='target']");
                        XmlNode contextItem = propertyMapping.SelectSingleNode("Item[@ContextType='context']");
                        XmlNode valueItem = propertyMapping.SelectSingleNode("Item[@ContextType='value']");
                        string valueXml = String.Empty;
                        if (valueItem != null)
                        {
                            if (valueItem.SelectSingleNode("SourceValue") != null)
                            {
                                valueXml = CreateSourcesFromItems(valueItem.SelectSingleNode("SourceValue"));
                            }
                            else
                            {
                                valueXml = XmlHelper.GetNodeText(valueItem, ".", string.Empty);
                            }
                        }
                        else if (contextItem != null)
                        {
                            //build the source anyways                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  ccccccccccvccccccccccccccccccccccccccc
                            XmlDocument newXml = XmlHelper.CreateXmlDocument("<SourceValue></SourceValue>");
                            XmlNode importNode = newXml.ImportNode(contextItem, true);
                            newXml.DocumentElement.AppendChild(importNode);
                            valueXml = CreateSourcesFromItems(newXml.DocumentElement);
                        }
                        if (string.IsNullOrEmpty(valueXml))
                        {
                            valueXml = propertyMapping.InnerXml;
                        }
                        string propertyValue = String.Empty;

                        propertyValue = targetItem.Attributes["Name"].Value;
                        condition.Properties[propertyValue] = valueXml;
                    }
                }
            }
        }

        private void CreatePropertiesFromMappingsXml(XmlNode ruleAction, Authoring.Eventing.Action action)
        {
            XmlNodeList mappingsList = ruleAction.SelectNodes("Mappings/Mapping[@ActionPropertyCollection = 'Properties']");
            foreach (XmlElement propertyMapping in mappingsList)
            {
                if (propertyMapping.ChildNodes.Count > 0)
                {
                    XmlNode targetItem = propertyMapping.SelectSingleNode("Item[@ContextType='target']");
                    XmlNode contextItem = propertyMapping.SelectSingleNode("Item[@ContextType='context']");
                    XmlNode valueItem = propertyMapping.SelectSingleNode("Item[@ContextType='value']");
                    string valueXml = String.Empty;
                    if (valueItem != null)
                    {
                        if (valueItem.SelectSingleNode("SourceValue") != null)
                        {
                            valueXml = CreateSourcesFromItems(valueItem.SelectSingleNode("SourceValue"));
                        }
                        else
                        {
                            valueXml = XmlHelper.GetNodeText(valueItem, ".", string.Empty);
                        }
                    }
                    else if (contextItem != null)
                    {
                        //build the source anyways                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  ccccccccccvccccccccccccccccccccccccccc
                        XmlDocument newXml = XmlHelper.CreateXmlDocument("<SourceValue></SourceValue>");
                        XmlNode importNode = newXml.ImportNode(contextItem, true);
                        newXml.DocumentElement.AppendChild(importNode);
                        valueXml = CreateSourcesFromItems(newXml.DocumentElement);
                    }
                    if (string.IsNullOrEmpty(valueXml))
                    {
                        valueXml = propertyMapping.InnerXml;
                    }
                    string propertyValue = String.Empty;

                    //if no target exists determine the property name using the root node of the mapping\
                    //filter or order
                    if (targetItem == null)
                    {
                        propertyValue = propertyMapping.FirstChild.Name;
                        switch (propertyValue)
                        {
                            case "Sorters":
                                propertyValue = "Order";
                                break;
                        }
                    }
                    else
                    {
                        propertyValue = targetItem.Attributes["Name"].Value;
                    }
                    action.Properties[propertyValue] = valueXml;
                }
            }
        }

        private void CreateListenerResults(XmlNode ruleAction, Authoring.Eventing.Action action)
        {
            XmlNodeList mappingsList = ruleAction.SelectNodes("Mappings/Mapping[@ActionPropertyCollection = 'Results']");
            //check for result counter - this is to verify that other mappings don't break at the moment - view selection + control population for instance
            if (mappingsList.Count > 0)
            {
                for (int i = 0; i < mappingsList.Count; i++)
                {
                    XmlNode mappingNode = mappingsList[i];
                    CreateListenerResult(mappingNode, action);
                }
            }
        }

        private void CreateListenerResult(XmlNode mappingNode, Authoring.Eventing.Action action)
        {
            Mapping listenerResult = new Mapping();

            XmlNode valueContextType = mappingNode.SelectSingleNode("Item[@ContextType='value']");
            string sourceType = string.Empty;

            if (valueContextType == null)
            {
                XmlNode contextItemNode = mappingNode.SelectSingleNode("Item[@ContextType='context']");
                sourceType = contextItemNode.Attributes["ItemType"].Value;

                if (contextItemNode.Attributes["SubFormID"] != null && !string.IsNullOrEmpty(contextItemNode.Attributes["SubFormID"].Value))
                {
                    listenerResult.SourceSubFormGuid = new Guid(contextItemNode.Attributes["SubFormID"].Value);
                }

                if (contextItemNode.Attributes["SubFormInstanceID"] != null && !string.IsNullOrEmpty(contextItemNode.Attributes["SubFormInstanceID"].Value))
                {
                    listenerResult.SourceSubFormInstanceGuid = new Guid(contextItemNode.Attributes["SubFormInstanceID"].Value);
                }

                if (contextItemNode.Attributes["InstanceID"] != null && !string.IsNullOrEmpty(contextItemNode.Attributes["InstanceID"].Value))
                {
                    listenerResult.SourceInstanceGuid = new Guid(contextItemNode.Attributes["InstanceID"].Value);
                }

                XmlAttribute sourceNameAttribute = contextItemNode.Attributes["Name"];
                if (sourceNameAttribute != null)
                {
                    listenerResult.SourceName = sourceNameAttribute.Value;
                }

                XmlAttribute sourceDisplayNameAttribute = contextItemNode.Attributes["DisplayName"];
                if (sourceDisplayNameAttribute != null)
                {
                    listenerResult.SourceDisplayName = sourceDisplayNameAttribute.Value;
                }

                switch (sourceType)
                {
                    case "MethodReturnedProperty":
                        listenerResult.SourceType = MappingSourceType.ObjectProperty;
                        break;
                    case "ProcessDataField":
                        listenerResult.SourceType = MappingSourceType.WorkflowProcessDataField;
                        listenerResult.SourceID = mappingNode.SelectSingleNode("Item[@ContextType='context']").Attributes["Name"].Value;
                        break;
                    case "ProcessXmlField":
                        listenerResult.SourceType = MappingSourceType.WorkflowProcessXmlField;
                        break;
                    case "ActivityDataField":
                        listenerResult.SourceType = MappingSourceType.WorkflowActivityDataField;
                        break;
                    case "ActivityXmlField":
                        listenerResult.SourceType = MappingSourceType.WorkflowActivityXmlField;
                        break;
                    case "ProcessProperty":
                        listenerResult.SourceType = MappingSourceType.WorkflowProcessProperty;
                        break;
                    case "ActivityProperty":
                        listenerResult.SourceType = MappingSourceType.WorkflowActivityProperty;
                        break;
                    default:
                        listenerResult.SourceType = (MappingSourceType)Enum.Parse(typeof(MappingSourceType), sourceType, true);
                        break;
                }

                switch (listenerResult.SourceType)
                {
                    case MappingSourceType.Control:
                    case MappingSourceType.ViewField:
                    case MappingSourceType.Expression:
                        listenerResult.SourceID = mappingNode.SelectSingleNode("Item[@ContextType='context']").Attributes["Guid"].Value;
                        break;
                    case MappingSourceType.ControlProperty:
                    case MappingSourceType.ControlField:
                        listenerResult.SourceID = mappingNode.SelectSingleNode("Item[@ContextType='context']").Attributes["SourceID"].Value;
                        listenerResult.SourcePath = mappingNode.SelectSingleNode("Item[@ContextType='context']").Attributes["SourcePath"].Value;
                        break;
                    default:
                        listenerResult.SourceID = mappingNode.SelectSingleNode("Item[@ContextType='context']").Attributes["Name"].Value;
                        break;
                }

            }
            else
            {
                if (valueContextType.SelectSingleNode("SourceValue") != null)
                {
                    listenerResult.SourceValue = CreateSourcesFromItems(valueContextType.SelectSingleNode("SourceValue"));
                }
                else
                {
                    listenerResult.SourceValue = valueContextType.InnerXml;
                }

                listenerResult.SourceType = MappingSourceType.Value;
                listenerResult.SourceID = "Sources";
            }

            XmlNode targetItemNode = mappingNode.SelectSingleNode("Item[@ContextType='target']");
            string targetType = targetItemNode.Attributes["ItemType"].Value;

            if (targetItemNode.Attributes["SubFormID"] != null && !string.IsNullOrEmpty(targetItemNode.Attributes["SubFormID"].Value))
            {
                listenerResult.TargetSubFormGuid = new Guid(targetItemNode.Attributes["SubFormID"].Value);
            }

            if (targetItemNode.Attributes["SubFormInstanceID"] != null && !string.IsNullOrEmpty(targetItemNode.Attributes["SubFormInstanceID"].Value))
            {
                listenerResult.TargetSubFormInstanceGuid = new Guid(targetItemNode.Attributes["SubFormInstanceID"].Value);
            }

            if (targetItemNode.Attributes["InstanceID"] != null && !string.IsNullOrEmpty(targetItemNode.Attributes["InstanceID"].Value))
            {
                listenerResult.TargetInstanceGuid = new Guid(targetItemNode.Attributes["InstanceID"].Value);
            }

            XmlAttribute targetNameAttribute = targetItemNode.Attributes["Name"];
            if (targetNameAttribute != null)
            {
                listenerResult.TargetName = targetNameAttribute.Value;
                listenerResult.TargetID = listenerResult.TargetName;
            }

            XmlAttribute targetDisplayNameAttribute = targetItemNode.Attributes["DisplayName"];
            if (targetDisplayNameAttribute != null)
            {
                listenerResult.TargetDisplayName = targetDisplayNameAttribute.Value;
            }

            switch (targetType)
            {
                case "Control":
                case "ViewField":
                    listenerResult.TargetType = (MappingTargetType)Enum.Parse(typeof(MappingTargetType), targetType, true);
                    listenerResult.TargetID = targetItemNode.Attributes["Guid"].Value;
                    break;
                case "MethodRequiredProperty":
                case "MethodOptionalProperty":
                case "ObjectProperty":
                    listenerResult.TargetType = MappingTargetType.ObjectProperty;
                    break;
                case "ProcessDataField":
                    listenerResult.TargetType = MappingTargetType.WorkflowProcessDataField;
                    break;
                case "ProcessXmlField":
                    listenerResult.TargetType = MappingTargetType.WorkflowProcessXmlField;
                    break;
                case "ActivityDataField":
                    listenerResult.TargetType = MappingTargetType.WorkflowActivityDataField;
                    break;
                case "ActivityXmlField":
                    listenerResult.TargetType = MappingTargetType.WorkflowActivityXmlField;
                    break;
                case "ProcessProperty":
                    listenerResult.TargetType = MappingTargetType.WorkflowProcessProperty;
                    break;
                case "ActivityProperty":
                    listenerResult.TargetType = MappingTargetType.WorkflowActivityProperty;
                    break;
                case "ControlProperty":
                    listenerResult.TargetType = MappingTargetType.WorkflowProcessProperty;
                    listenerResult.TargetID = targetItemNode.Attributes["TargetID"].Value;
                    listenerResult.TargetPath = targetItemNode.Attributes["TargetPath"].Value;
                    break;
                case "FieldContext":
                    listenerResult.TargetType = MappingTargetType.ViewSource;
                    listenerResult.TargetID = targetItemNode.Attributes["Guid"].Value;
                    break;
                case "ViewSource":
                    listenerResult.TargetType = (MappingTargetType)Enum.Parse(typeof(MappingTargetType), targetType, true);
                    listenerResult.TargetID = targetItemNode.Attributes["Guid"].Value;
                    break;
                case "FormParameter":
                case "ViewParameter":
                default:
                    listenerResult.TargetType = (MappingTargetType)Enum.Parse(typeof(MappingTargetType), targetType, true);
                    break;
            }

            action.Results.Add(listenerResult);
        }

        private void CreateMappings(XmlNode ruleAction, Authoring.Eventing.Action action)
        {
            XmlNodeList mappingsList = ruleAction.SelectNodes("Mappings/Mapping[@ActionPropertyCollection = 'Parameters']");
            if (mappingsList.Count > 0)
            {
                for (int i = 0; i < mappingsList.Count; i++)
                {
                    XmlNode mappingNode = mappingsList[i];
                    CreateMapping(mappingNode, action);
                }
            }
        }

        private void CreateMapping(XmlNode mappingNode, Authoring.Eventing.Action action)
        {
            Mapping Mapping = new Mapping();

            XmlNode valueContextType = mappingNode.SelectSingleNode("Item[@ContextType='value']");
            if (valueContextType == null)
            {
                XmlNode contextItemNode = mappingNode.SelectSingleNode("Item[@ContextType='context']");
                string sourceType = contextItemNode.Attributes["ItemType"].Value;
                Mapping.SourceType = (MappingSourceType)Enum.Parse(typeof(MappingSourceType), sourceType, true);

                if (contextItemNode.Attributes["SubFormID"] != null && !string.IsNullOrEmpty(contextItemNode.Attributes["SubFormID"].Value))
                {
                    Mapping.SourceSubFormGuid = new Guid(contextItemNode.Attributes["SubFormID"].Value);
                }

                if (contextItemNode.Attributes["SubFormInstanceID"] != null && !string.IsNullOrEmpty(contextItemNode.Attributes["SubFormInstanceID"].Value))
                {
                    Mapping.SourceSubFormInstanceGuid = new Guid(contextItemNode.Attributes["SubFormInstanceID"].Value);
                }

                if (contextItemNode.Attributes["InstanceID"] != null && !string.IsNullOrEmpty(contextItemNode.Attributes["InstanceID"].Value))
                {
                    Mapping.SourceInstanceGuid = new Guid(contextItemNode.Attributes["InstanceID"].Value);
                }

                XmlAttribute sourceNameAttribute = contextItemNode.Attributes["Name"];
                if (sourceNameAttribute != null)
                {
                    Mapping.SourceName = sourceNameAttribute.Value;
                }

                XmlAttribute sourceDisplayNameAttribute = contextItemNode.Attributes["DisplayName"];
                if (sourceDisplayNameAttribute != null)
                {
                    Mapping.SourceDisplayName = sourceDisplayNameAttribute.Value;
                }

                switch (Mapping.SourceType)
                {
                    case MappingSourceType.Control:
                    case MappingSourceType.ViewField:
                    case MappingSourceType.Expression:
                    case MappingSourceType.ViewSource:
                        Mapping.SourceID = contextItemNode.Attributes["Guid"].Value;
                        break;
                    case MappingSourceType.ControlProperty:
                    case MappingSourceType.ControlField:
                        Mapping.SourceID = contextItemNode.Attributes["SourceID"].Value;
                        Mapping.SourcePath = contextItemNode.Attributes["SourcePath"].Value;
                        break;
                    default:
                        Mapping.SourceID = Mapping.SourceName;
                        break;
                }

            }
            else
            {
                if (valueContextType.SelectSingleNode("SourceValue") != null)
                {
                    Mapping.SourceValue = CreateSourcesFromItems(valueContextType.SelectSingleNode("SourceValue"));
                }
                else
                {
                    Mapping.SourceValue = valueContextType.InnerXml;
                }
                Mapping.SourceType = MappingSourceType.Value;
                Mapping.SourceID = "Sources";

                if (valueContextType.Attributes["SourceTemplateId"] != null && !string.IsNullOrEmpty(valueContextType.Attributes["SourceTemplateId"].Value))
                {
                    Mapping.SourceTemplateId = new Guid(valueContextType.Attributes["SourceTemplateId"].Value);
                }
            }

            XmlNode targetItemNode = mappingNode.SelectSingleNode("Item[@ContextType='target']");
            var itemTypeAttribute = targetItemNode.Attributes["ItemType"];
            if (itemTypeAttribute == null)
                return; //don't add listener
            string targetType = itemTypeAttribute.Value;

            if (targetItemNode.Attributes["SubFormID"] != null && !string.IsNullOrEmpty(targetItemNode.Attributes["SubFormID"].Value))
            {
                Mapping.TargetSubFormGuid = new Guid(targetItemNode.Attributes["SubFormID"].Value);
            }

            if (targetItemNode.Attributes["SubFormInstanceID"] != null && !string.IsNullOrEmpty(targetItemNode.Attributes["SubFormInstanceID"].Value))
            {
                Mapping.TargetSubFormInstanceGuid = new Guid(targetItemNode.Attributes["SubFormInstanceID"].Value);
            }

            if (targetItemNode.Attributes["InstanceID"] != null && !string.IsNullOrEmpty(targetItemNode.Attributes["InstanceID"].Value))
            {
                Mapping.TargetInstanceGuid = new Guid(targetItemNode.Attributes["InstanceID"].Value);
            }

            XmlAttribute targetNameAttribute = targetItemNode.Attributes["Name"];
            if (targetNameAttribute != null)
            {
                Mapping.TargetName = targetNameAttribute.Value;
                Mapping.TargetID = Mapping.TargetName;
            }

            XmlAttribute targetDisplayNameAttribute = targetItemNode.Attributes["DisplayName"];
            if (targetDisplayNameAttribute != null)
            {
                Mapping.TargetDisplayName = targetDisplayNameAttribute.Value;
            }

            switch (targetType)
            {
                case "Control":
                case "ViewField":
                    Mapping.TargetType = (MappingTargetType)Enum.Parse(typeof(MappingTargetType), targetType, true);
                    Mapping.TargetID = targetItemNode.Attributes["Guid"].Value;
                    break;
                case "MethodRequiredProperty":
                    Mapping.TargetType = MappingTargetType.ObjectProperty;
                    Mapping.IsRequired = true;
                    break;
                case "MethodOptionalProperty":
                case "ObjectProperty":
                    Mapping.TargetType = MappingTargetType.ObjectProperty;
                    break;
                case "FormParameter":
                    Mapping.TargetType = (MappingTargetType)Enum.Parse(typeof(MappingTargetType), targetType, true);
                    break;
                case "ProcessDataField":
                    Mapping.TargetType = MappingTargetType.WorkflowProcessDataField;
                    break;
                case "ProcessXmlField":
                    Mapping.TargetType = MappingTargetType.WorkflowProcessXmlField;
                    break;
                case "ActivityDataField":
                    Mapping.TargetType = MappingTargetType.WorkflowActivityDataField;
                    break;
                case "ActivityXmlField":
                    Mapping.TargetType = MappingTargetType.WorkflowActivityXmlField;
                    break;
                case "ProcessProperty":
                    Mapping.TargetType = MappingTargetType.WorkflowProcessProperty;
                    break;
                case "ActivityProperty":
                    Mapping.TargetType = MappingTargetType.WorkflowActivityProperty;
                    break;
                case "MailProperty":
                    Mapping.TargetType = MappingTargetType.MailProperty;
                    break;
                case "MessageProperty":
                    Mapping.TargetType = MappingTargetType.MessageProperty;
                    break;
                case "ControlProperty":
                    Mapping.TargetType = (MappingTargetType)Enum.Parse(typeof(MappingTargetType), targetType, true);
                    Mapping.TargetID = targetItemNode.Attributes["TargetID"].Value;

                    if (action.ControlGuid == Guid.Empty)
                    {
                        Mapping.TargetPath = mappingNode.SelectSingleNode("Item[(@ContextType='target') and (@TargetPath)]") != null ? targetItemNode.Attributes["TargetPath"].Value : string.Empty;
                    }

                    XmlAttribute targetPathTypeAttribute = targetItemNode.Attributes["TargetPathType"];
                    if (targetPathTypeAttribute != null)
                    {
                        Mapping.TargetPathType = targetPathTypeAttribute.Value;
                    }

                    break;
                case "ControlMethodParameter":
                    Mapping.TargetType = (MappingTargetType)Enum.Parse(typeof(MappingTargetType), targetType, true);
                    Mapping.TargetID = targetItemNode.Attributes["TargetID"].Value;
                    break;
                case "MethodParameter":
                    Mapping.TargetType = (MappingTargetType)Enum.Parse(typeof(MappingTargetType), targetType, true);

                    if (targetItemNode.Attributes["Required"] != null && targetItemNode.Attributes["Required"].Value.ToLowerInvariant() != "false")
                    {
                        Mapping.IsRequired = true;
                    }
                    break;
                default:
                    Mapping.TargetType = (MappingTargetType)Enum.Parse(typeof(MappingTargetType), targetType, true);
                    break;
            }

            action.Parameters.Add(Mapping);
        }

        #endregion Action Collections XML -> API

        private string CreateSourcesFromItems(XmlNode sourceValueNode)
        {
            XmlNodeList items = sourceValueNode.SelectNodes("Item");
            StringBuilder sourceValue = new StringBuilder();

            using (XmlWriter writer = XmlHelper.CreateXmlWriter(sourceValue))
            {
                foreach (XmlNode item in items)
                {
                    string contextType = item.Attributes["ContextType"].Value;
                    string sourceType = string.Empty;
                    string sourceID = string.Empty;
                    string sourceName = string.Empty;
                    string sourceDisplayName = string.Empty;
                    if (contextType == "value")
                    {
                        writer.WriteStartElement("Source");
                        writer.WriteAttributeString("SourceType", "Value");
                        writer.WriteString(item.InnerText);
                        writer.WriteEndElement();
                    }
                    else
                    {
                        sourceType = item.Attributes["ItemType"].Value;
                        MappingSourceType SourceType = (MappingSourceType)Enum.Parse(typeof(MappingSourceType), sourceType, true);

                        if (item.Attributes["SourceName"] != null)
                        {
                            sourceName = item.Attributes["SourceName"].Value;
                        }
                        else if (item.Attributes["Name"] != null)
                        {
                            sourceName = item.Attributes["Name"].Value;
                        }

                        switch (SourceType)
                        {
                            case MappingSourceType.Control:
                            case MappingSourceType.ViewField:
                            case MappingSourceType.Expression:
                                //case MappingSourceType.ResultField:
                                sourceID = item.Attributes["Guid"].Value;
                                break;
                            case MappingSourceType.ObjectProperty:
                                sourceID = item.Attributes["Name"].Value;
                                break;
                            case MappingSourceType.ControlProperty:
                            case MappingSourceType.ControlField:
                                sourceID = item.Attributes["SourceID"].Value;
                                break;
                            case MappingSourceType.SystemVariable:
                                sourceID = sourceName;
                                break;
                            case MappingSourceType.FormParameter:
                            case MappingSourceType.ViewParameter:
                                sourceID = item.Attributes["Name"].Value;
                                break;
                            default:
                                if (item.Attributes["SourceID"] != null)
                                {
                                    sourceID = item.Attributes["SourceID"].Value;
                                }
                                else if (item.Attributes["Name"] != null)
                                {
                                    sourceID = item.Attributes["Name"].Value;
                                }
                                else if (item.Attributes["Guid"] != null)
                                {
                                    sourceID = item.Attributes["Guid"].Value;
                                }
                                break;
                        }

                        writer.WriteStartElement("Source");
                        writer.WriteAttributeString("SourceID", sourceID);
                        writer.WriteAttributeString("SourceType", sourceType);

                        if (!string.IsNullOrEmpty(sourceName))
                        {
                            writer.WriteAttributeString("SourceName", sourceName);
                        }

                        if (item.Attributes["SourceDisplayName"] != null)
                        {
                            writer.WriteAttributeString("SourceDisplayName", item.Attributes["SourceDisplayName"].Value);
                        }

                        XmlAttribute instanceID = item.Attributes["InstanceID"];
                        if (instanceID != null && !string.IsNullOrEmpty(instanceID.Value))
                            writer.WriteAttributeString("SourceInstanceID", instanceID.Value);

                        XmlAttribute subFormID = item.Attributes["SubFormID"];
                        if (subFormID != null && !string.IsNullOrEmpty(subFormID.Value))
                            writer.WriteAttributeString("SourceSubFormID", subFormID.Value);

                        XmlAttribute subFormInstanceID = item.Attributes["SubFormInstanceID"];
                        if (subFormInstanceID != null && !string.IsNullOrEmpty(subFormInstanceID.Value))
                            writer.WriteAttributeString("SourceSubFormInstanceID", subFormInstanceID.Value);

                        XmlAttribute sourcePath = item.Attributes["SourcePath"];
                        if (sourcePath != null && !string.IsNullOrEmpty(sourcePath.Value))
                            writer.WriteAttributeString("SourcePath", sourcePath.Value);

                        XmlAttribute displayPath = item.Attributes["DisplayPath"];
                        if (displayPath != null && !string.IsNullOrEmpty(displayPath.Value))
                            writer.WriteAttributeString("DisplayPath", displayPath.Value);

                        XmlAttribute sourceTemplateId = item.Attributes["SourceTemplateId"];
                        if (sourceTemplateId != null && !string.IsNullOrEmpty(sourceTemplateId.Value))
                            writer.WriteAttributeString("SourceTemplateId", sourceTemplateId.Value);

                        writer.WriteEndElement();
                    }
                }
            }
            return sourceValue.ToString();
        }

        private void CreateControlListenerResults(Authoring.Eventing.Action listener)
        {
            Mapping lr = new Mapping();//(MappingSourceType.Result, listener.ObjectGuid.ToString(), MappingTargetType.ViewControl, listener.ControlGuid.ToString());
            lr.SourceType = MappingSourceType.Result;
            lr.SourceID = listener.ObjectGuid.ToString();
            lr.TargetType = MappingTargetType.Control;
            lr.TargetID = listener.ControlGuid.ToString();
            lr.TargetInstanceGuid = listener.InstanceGuid;
            lr.TargetSubFormInstanceGuid = listener.SubFormInstanceGuid;
            lr.TargetSubFormGuid = listener.SubFormGuid;

            listener.Results.Add(lr);
        }

        private string TransformListenerToDisplayName(string listenerValue)
        {
            string displayName = string.Empty;
            switch (listenerValue)
            {
                case "Synchronous":
                    displayName = Resources.RuleHelper.Synchronous;
                    break;
                case "Asynchronous":
                    displayName = Resources.RuleHelper.Asynchronous;
                    break;
                case "Single":
                    displayName = Resources.RuleHelper.Single;
                    break;
                case "Parallel":
                    displayName = Resources.RuleHelper.Parallel;
                    break;
            }
            return displayName;
        }

        #region Action Collections API -> XML

        private XmlNode LookUpSettings(XmlNode configurationNode, string settingResultName, Context context)
        {
            XmlDocument returnDocument = new XmlDocument();
            XmlNode settingsConfiguration = null;

            string resutlNamePart = "not(@ResultName)";
            if (!String.IsNullOrEmpty(settingResultName))
                resutlNamePart = String.Format("@ResultName={0}", XmlHelper.XPathParameterEncode(settingResultName));
            string xpath = String.Format("RuleDefinitions/Settings/Setting[{0}]/Collections", resutlNamePart);

            settingsConfiguration = ruleDefinition.DocumentElement.SelectSingleNode(xpath);

            XmlNode configurationNodeSettings = configurationNode.SelectSingleNode("Collections");
            if (configurationNodeSettings != null)
            {
                // use current node instead
                settingsConfiguration = configurationNodeSettings;
            }

            return settingsConfiguration;
        }

        private bool FilterCollection(XmlNode collection, string propertyValue)
        {
            XmlNodeList includeFilters = collection.SelectNodes("IncludeFilters/IncludeFilter");
            foreach (XmlNode includeFilter in includeFilters)
            {
                XmlAttribute attribute = includeFilter.Attributes["ApiIdentifier"];
                if (attribute != null)
                {
                    if (propertyValue == attribute.Value)
                        return true;
                }
            }
            if (includeFilters.Count != 0)
                return false;

            XmlNodeList excludeFilters = collection.SelectNodes("ExcludeFilters/ExcludeFilter");
            foreach (XmlNode excludeFilter in excludeFilters)
            {
                XmlAttribute attribute = excludeFilter.Attributes["ApiIdentifier"];
                if (attribute != null)
                {
                    if (propertyValue == attribute.Value)
                        return false;
                }
            }

            return true;

        }


        /// <summary>
        /// Adds mappings to the mappings node as specified by this particular confugrationNode node
        /// </summary>
        /// <param name="mappingsNode">The mappings node to add mappings to</param>
        /// <param name="configurationNode">The configuration node that shows which mappings to add</param>
        /// <param name="context">The context containing the action with the collections containing the mapping values</param>
        private void AddMappingNode(XmlNode mappingsNode, XmlNode configurationNode, Context context)
        {
            string settingResultName = "";
            XmlAttribute resultNameAttribute = configurationNode.Attributes["ResultName"];
            if (resultNameAttribute != null)
                settingResultName = resultNameAttribute.Value;
            XmlNode settingsCollections = LookUpSettings(configurationNode, settingResultName, context);


            XmlNodeList collections = settingsCollections.SelectNodes("Collection");
            foreach (XmlNode collection in collections)
            {
                string collectionType = collection.Attributes["Type"].Value;
                switch (collectionType)
                {
                    case "Parameters":
                        //Listener Parameters
                        for (int x = 0; x < context.Action.Parameters.Count; x++)
                        {
                            Mapping lp = context.Action.Parameters[x];
                            if (FilterCollection(collection, lp.TargetID))
                                CreateMappingXMLFromActionParameter(mappingsNode.OwnerDocument, mappingsNode, lp, settingResultName);
                        }
                        //Listener Parameters
                        break;
                    case "Results":
                        //Listener Results
                        for (int x = 0; x < context.Action.Results.Count; x++)
                        {
                            Mapping lr = context.Action.Results[x];
                            if (FilterCollection(collection, lr.SourceID))
                            {
                                CreateMappingXMLFromActionResult(mappingsNode.OwnerDocument, mappingsNode, lr, settingResultName);
                            }
                        }
                        //Listener Results
                        break;
                    case "Properties":
                        //Action Properties
                        int propertyCount = context.Action.Properties.Count;
                        for (int x = 0; x < propertyCount; x++)
                        {
                            WSA.Property prop = context.Action.Properties[x];
                            if (FilterCollection(collection, prop.Name))
                            {
                                CreateMappingXMLFromRuleItemProperty(mappingsNode.OwnerDocument, mappingsNode, prop, settingResultName);
                            }
                        }
                        //Action Properties
                        break;
                }
            }
        }

        /// <summary>
        /// Uses the ruledefinition xml document to populate the XML mappings for an action
        /// </summary>
        /// <param name="actionNode">The action node that will be updated with mappings if they exist</param>
        /// <param name="context">The rules context from where the current action details are retrieved</param>
        private void BuildMappingXML(XmlNode actionNode, Context context)
        {
            XmlNodeList configurationNode = null;
            configurationNode = ruleDefinition.DocumentElement.SelectNodes(String.Format("RuleDefinitions/Actions/Action[@Name='{0}']/Configurations/Configuration/Settings/Setting", context.RuleActionName));
            XmlNode mappingsNode = actionNode.OwnerDocument.CreateElement("Mappings");

            foreach (XmlNode node in configurationNode)
            {
                AddMappingNode(mappingsNode, node, context);
            }

            if (mappingsNode.ChildNodes.Count > 0)
                actionNode.AppendChild(mappingsNode);

        }

        /// <summary>
        /// Uses the ruledefinition xml document to populate the XML mappings for an action
        /// </summary>
        /// <param name="xmlDoc">The action node that will be updated with mappings if they exist</param>
        /// <param name="context">The rules context from where the current action details are retrieved</param>
        private XmlNode BuildMappingXMLForParts(XmlDocument xmlDoc, Context context)
        {
            XmlNode mappingsNode = xmlDoc.CreateElement("Mappings");
            AddMappingNodeForParts(mappingsNode, context);

            return mappingsNode;
        }

        /// <summary>
        /// Adds mappings to the mappings node as specified by this particular confugrationNode node
        /// </summary>
        /// <param name="mappingsNode">The mappings node to add mappings to</param>
        /// <param name="context">The context containing the action with the collections containing the mapping values</param>
        private void AddMappingNodeForParts(XmlNode mappingsNode, Context context)
        {
            int propertyCount = context.Condition.Properties.Count;
            for (int x = 0; x < propertyCount; x++)
            {
                WSA.Property prop = context.Condition.Properties[x];

                if (!prop.Name.Equals("Location") && !prop.Name.Equals("Name"))
                {
                    CreateMappingXMLFromRuleItemProperty(mappingsNode.OwnerDocument, mappingsNode, prop);
                }
            }
        }

        private void AnnotateMapping(XmlNode mappingsNode, XmlNode contextItemNode, XmlNode targetItemNode, WSF.ValidationResult validation)
        {
            if ((validation.Status & WSF.ValidationStatus.Error) == WSF.ValidationStatus.Error ||
                (validation.Status & WSF.ValidationStatus.Missing) == WSF.ValidationStatus.Missing ||
                (validation.Status & WSF.ValidationStatus.Warning) == WSF.ValidationStatus.Warning)
            {
                bool mappingInvalid = false;

                foreach (WSF.ValidationError vMsg in validation.Messages)
                {
                    ValidationMessageParts message = new ValidationMessageParts(vMsg);
                    XmlNode nodeToAnnotate = null;

                    if (message.RefStatus == ReferenceStatus.Error || message.RefStatus == ReferenceStatus.Missing
                        || (message.RefStatus == ReferenceStatus.Warning))
                    {
                        if (message.RefStatus == ReferenceStatus.Warning && (message.RefType == ReferenceType.Form || message.RefType == ReferenceType.View))
                        {
                            continue;
                        }

                        mappingInvalid = true;

                        if (message.ReferenceAs.Contains("Source")) // "ParameterMappingSource", "ResultMappingSource"
                        {
                            nodeToAnnotate = contextItemNode;
                        }
                        else // message.ReferenceAs.Contains("Target") // "ParameterMappingTarget", "ResultMappingTarget"
                        {
                            nodeToAnnotate = targetItemNode;
                        }

                        if (nodeToAnnotate != null)
                        {
                            if (nodeToAnnotate.Attributes["Invalid"] == null)
                            {
                                nodeToAnnotate.Attributes.Append(nodeToAnnotate.OwnerDocument.CreateAttribute("Invalid"));
                            }

                            nodeToAnnotate.Attributes["Invalid"].Value = "true";

                            if (nodeToAnnotate.Attributes["ValidationMessages"] == null)
                            {
                                nodeToAnnotate.Attributes.Append(nodeToAnnotate.OwnerDocument.CreateAttribute("ValidationMessages"));
                            }

                            nodeToAnnotate.Attributes["ValidationMessages"].Value = vMsg.Message;
                        }
                    }
                }

                if (mappingInvalid)
                {
                    if (mappingsNode.Attributes["Invalid"] == null)
                    {
                        mappingsNode.Attributes.Append(mappingsNode.OwnerDocument.CreateAttribute("Invalid"));
                    }

                    mappingsNode.Attributes["Invalid"].Value = "true";
                }
            }
        }

        /// <summary>
        /// Create a mapping node from a property on an action
        /// </summary>
        /// <param name="ruleInstance">The owner xml document that contains the action and the mappings</param>
        /// <param name="mappingsNode">The mappings node to add this mapping to</param>
        /// <param name="property">The property used to retrieve the mapping values from</param>
        /// <param name="resultName">Specifies which tab / settings widget will get this mapping</param>
        private XmlNode CreateMappingXMLFromRuleItemProperty(XmlDocument ruleInstance, XmlNode mappingsNode, WSA.Property property, string resultName = "")
        {
            if (!String.IsNullOrEmpty(property.Value))
            {
                XmlNode mappingNode = ruleInstance.CreateElement("Mapping");
                mappingsNode.AppendChild(mappingNode);

                if (!string.IsNullOrEmpty(resultName))
                {
                    mappingNode.Attributes.Append(ruleInstance.CreateAttribute("Type"));
                    mappingNode.Attributes["Type"].Value = resultName;

                    mappingNode.Attributes.Append(ruleInstance.CreateAttribute("ActionPropertyCollection"));
                    mappingNode.Attributes["ActionPropertyCollection"].Value = "Properties";
                }

                XmlDocument xSourceValue = new XmlDocument();
                xSourceValue.PreserveWhitespace = true;
                if (property.Value.StartsWith("<Source"))
                {
                    string xmlSource = string.Format("<Sources>{0}</Sources>", property.Value);
                    XmlHelper.LoadXmlDocument(xSourceValue, xmlSource);
                }
                else
                {
                    XmlElement el = xSourceValue.CreateElement("Sources");
                    el.AppendChild(xSourceValue.CreateTextNode(property.Value));
                    xSourceValue.AppendChild(el);
                }

                XmlNode contextItemNode = null, targetItemNode = null;

                XmlNode source = xSourceValue.SelectSingleNode(".//Sources[Source]");
                if (source != null)
                {
                    contextItemNode = ruleInstance.CreateElement("Item");
                    targetItemNode = ruleInstance.CreateElement("Item");

                    contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("ContextType"));
                    contextItemNode.Attributes["ContextType"].Value = "value";

                    targetItemNode.Attributes.Append(ruleInstance.CreateAttribute("ContextType"));
                    targetItemNode.Attributes["ContextType"].Value = "target";

                    contextItemNode.Attributes["ContextType"].Value = "value";

                    contextItemNode.InnerXml = CreateItemsFromSources(source, ruleInstance);

                    xSourceValue = null;

                    targetItemNode.Attributes.Append(ruleInstance.CreateAttribute("Name"));
                    targetItemNode.Attributes["Name"].Value = property.Name;


                    mappingNode.AppendChild(contextItemNode);
                    mappingNode.AppendChild(targetItemNode);
                }
                else
                {
                    if (property.Value.TrimStart().StartsWith("<Filter") || property.Value.TrimStart().StartsWith("<Sorters"))
                    {
                        mappingNode.InnerXml = property.Value;
                        // As this xml is just copied over, add Invalid='true' to annotated elements as required
                        XmlNodeList nodesInError = mappingNode.SelectNodes("//*[contains(@ValidationStatus, 'Missing') or contains(@ValidationStatus, 'Error') or contains(@ValidationStatus, 'Warning')]");
                        foreach (XmlNode nodeInError in nodesInError)
                        {
                            nodeInError.Attributes.Append(ruleInstance.CreateAttribute("Invalid"));
                            nodeInError.Attributes["Invalid"].Value = "true";
                        }
                    }
                    else
                    {
                        contextItemNode = ruleInstance.CreateElement("Item");
                        targetItemNode = ruleInstance.CreateElement("Item");

                        contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("ContextType"));
                        contextItemNode.Attributes["ContextType"].Value = "value";

                        targetItemNode.Attributes.Append(ruleInstance.CreateAttribute("ContextType"));
                        targetItemNode.Attributes["ContextType"].Value = "target";

                        contextItemNode.Attributes["ContextType"].Value = "value";

                        contextItemNode.AppendChild(ruleInstance.CreateTextNode(property.Value));

                        xSourceValue = null;

                        targetItemNode.Attributes.Append(ruleInstance.CreateAttribute("Name"));
                        targetItemNode.Attributes["Name"].Value = property.Name;


                        mappingNode.AppendChild(contextItemNode);
                        mappingNode.AppendChild(targetItemNode);
                    }
                }

                AnnotateMapping(mappingsNode, contextItemNode, targetItemNode, property.Validation);

                return mappingNode;
            }
            return null;
        }

        /// <summary>
        /// Create a mapping node from a parameter on an action
        /// </summary>
        /// <param name="ruleInstance">The owner xml document that contains the action and the mappings</param>
        /// <param name="mappingsNode">The mappings node to add this mapping to</param>
        /// <param name="lp">The parameter used to retrieve the mapping values from</param>
        /// <param name="resultName">Specifies which tab / settings widget will get this mapping</param>
        /// <returns></returns>
        private XmlNode CreateMappingXMLFromActionParameter(XmlDocument ruleInstance, XmlNode mappingsNode, Authoring.Eventing.Mapping lp, string resultName)
        {
            XmlNode mappingNode = ruleInstance.CreateElement("Mapping");
            mappingsNode.AppendChild(mappingNode);
            mappingNode.Attributes.Append(ruleInstance.CreateAttribute("Type"));
            mappingNode.Attributes["Type"].Value = resultName;
            mappingNode.Attributes.Append(ruleInstance.CreateAttribute("ActionPropertyCollection"));
            mappingNode.Attributes["ActionPropertyCollection"].Value = "Parameters";

            XmlNode contextItemNode = ruleInstance.CreateElement("Item");
            XmlNode targetItemNode = ruleInstance.CreateElement("Item");

            contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("ContextType"));
            contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("ItemType"));
            contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("InstanceID"));
            contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("SubFormID"));
            contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("SubFormInstanceID"));

            contextItemNode.Attributes["ContextType"].Value = "context";
            contextItemNode.Attributes["ItemType"].Value = GetMappingSourceTypeForXML(lp.SourceType);
            contextItemNode.Attributes["InstanceID"].Value = lp.SourceInstanceGuid.ToString();
            contextItemNode.Attributes["SubFormID"].Value = lp.SourceSubFormGuid.ToString();
            contextItemNode.Attributes["SubFormInstanceID"].Value = lp.SourceSubFormInstanceGuid.ToString();

            if (!string.IsNullOrEmpty(lp.SourceName))
            {
                contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("Name"));
                contextItemNode.Attributes["Name"].Value = lp.SourceName;
            }
            if (!string.IsNullOrEmpty(lp.SourceDisplayName))
            {
                contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("DisplayName"));
                contextItemNode.Attributes["DisplayName"].Value = lp.SourceDisplayName;
            }

            targetItemNode.Attributes.Append(ruleInstance.CreateAttribute("ContextType"));
            targetItemNode.Attributes.Append(ruleInstance.CreateAttribute("ItemType"));
            targetItemNode.Attributes.Append(ruleInstance.CreateAttribute("InstanceID"));
            targetItemNode.Attributes.Append(ruleInstance.CreateAttribute("SubFormID"));
            targetItemNode.Attributes.Append(ruleInstance.CreateAttribute("SubFormInstanceID"));
            targetItemNode.Attributes.Append(ruleInstance.CreateAttribute("TargetPath"));

            targetItemNode.Attributes["ContextType"].Value = "target";
            targetItemNode.Attributes["ItemType"].Value = GetMappingTargetTypeForXML(lp.TargetType);
            targetItemNode.Attributes["InstanceID"].Value = lp.TargetInstanceGuid.ToString();
            targetItemNode.Attributes["SubFormID"].Value = lp.TargetSubFormGuid.ToString();
            targetItemNode.Attributes["SubFormInstanceID"].Value = lp.TargetSubFormInstanceGuid.ToString();
            targetItemNode.Attributes["TargetPath"].Value = lp.TargetPath;

            if (!string.IsNullOrEmpty(lp.TargetName))
            {
                targetItemNode.Attributes.Append(ruleInstance.CreateAttribute("Name"));
                targetItemNode.Attributes["Name"].Value = lp.TargetName;
            }
            if (!string.IsNullOrEmpty(lp.TargetDisplayName))
            {
                targetItemNode.Attributes.Append(ruleInstance.CreateAttribute("DisplayName"));
                targetItemNode.Attributes["DisplayName"].Value = lp.TargetDisplayName;
            }

            AnnotateMapping(mappingsNode, contextItemNode, targetItemNode, lp.Validation);

            switch (lp.SourceType)
            {
                case MappingSourceType.Control:
                case MappingSourceType.ViewField:
                case MappingSourceType.Expression:
                case MappingSourceType.ViewSource:
                    //case MappingSourceType.ResultField:
                    contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("Guid"));
                    contextItemNode.Attributes["Guid"].Value = lp.SourceID;
                    break;
                case MappingSourceType.Value:
                    contextItemNode.Attributes["ContextType"].Value = "value";
                    contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("SourceTemplateId"));
                    contextItemNode.Attributes["SourceTemplateId"].Value = lp.SourceTemplateId.ToString();

                    XmlDocument xSourceValue = new XmlDocument();
                    xSourceValue.PreserveWhitespace = true;
                    string xmlSource = "<Sources>" + lp.SourceValue + "</Sources>";
                    XmlHelper.LoadXmlDocument(xSourceValue, xmlSource);
                    if (xSourceValue.SelectSingleNode("Sources/Source") != null)
                    {
                        contextItemNode.InnerXml = CreateItemsFromSources(xSourceValue.SelectSingleNode("Sources"), ruleInstance);
                    }
                    else if (!string.IsNullOrEmpty(lp.SourceValue))
                    {
                        contextItemNode.InnerXml = lp.SourceValue;
                    }
                    xSourceValue = null;
                    contextItemNode.Attributes.Remove(contextItemNode.Attributes["ItemType"]);
                    break;
                case MappingSourceType.ControlProperty:
                case MappingSourceType.ControlField:
                    contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("SourceID"));
                    contextItemNode.Attributes["SourceID"].Value = lp.SourceID;
                    contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("SourcePath"));
                    contextItemNode.Attributes["SourcePath"].Value = lp.SourcePath;
                    break;
                default:
                    contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("Name"));
                    contextItemNode.Attributes["Name"].Value = lp.SourceID;
                    break;
            }

            switch (lp.TargetType)
            {
                case MappingTargetType.Control:
                case MappingTargetType.ViewField:
                    targetItemNode.Attributes.Append(ruleInstance.CreateAttribute("Guid"));
                    targetItemNode.Attributes["Guid"].Value = lp.TargetID;
                    break;
                case MappingTargetType.ControlProperty:
                case MappingTargetType.ControlMethodParameter:
                    targetItemNode.Attributes.Append(ruleInstance.CreateAttribute("TargetID"));
                    targetItemNode.Attributes["TargetID"].Value = lp.TargetID;

                    if (!string.IsNullOrEmpty(lp.TargetPathType))
                    {
                        targetItemNode.Attributes.Append(ruleInstance.CreateAttribute("TargetPathType"));
                        targetItemNode.Attributes["TargetPathType"].Value = lp.TargetPathType;
                    }
                    break;
                default:
                    targetItemNode.Attributes.Append(ruleInstance.CreateAttribute("Name"));
                    targetItemNode.Attributes["Name"].Value = lp.TargetID;
                    break;
            }

            mappingNode.AppendChild(contextItemNode);
            mappingNode.AppendChild(targetItemNode);
            return mappingNode;
        }

        private string GetMappingTargetTypeForXML(MappingTargetType targetType)
        {
            switch (targetType)
            {
                case MappingTargetType.WorkflowProcessDataField:
                case MappingTargetType.WorkflowProcessXmlField:
                case MappingTargetType.WorkflowProcessProperty:
                case MappingTargetType.WorkflowActivityDataField:
                case MappingTargetType.WorkflowActivityXmlField:
                case MappingTargetType.WorkflowActivityProperty:
                    return targetType.ToString().Substring(8);
                default:
                    return targetType.ToString();
            }
        }

        private string GetMappingSourceTypeForXML(MappingSourceType sourceType)
        {
            switch (sourceType)
            {
                case MappingSourceType.WorkflowProcessDataField:
                case MappingSourceType.WorkflowProcessXmlField:
                case MappingSourceType.WorkflowProcessProperty:
                case MappingSourceType.WorkflowActivityDataField:
                case MappingSourceType.WorkflowActivityXmlField:
                case MappingSourceType.WorkflowActivityProperty:
                    return sourceType.ToString().Substring(8);
                default:
                    return sourceType.ToString();
            }
        }

        /// <summary>
        /// Create a mapping node from a result on an action
        /// </summary>
        /// <param name="ruleInstance">The owner xml document that contains the action and the mappings</param>
        /// <param name="mappingsNode">The mappings node to add this mapping to</param>
        /// <param name="lr">The result used to retrieve the mapping values from</param>
        /// <param name="resultName">Specifies which tab / settings widget will get this mapping</param>
        private XmlNode CreateMappingXMLFromActionResult(XmlDocument ruleInstance, XmlNode mappingsNode, Authoring.Eventing.Mapping lr, string resultName)
        {
            XmlNode mappingNode = ruleInstance.CreateElement("Mapping");
            mappingsNode.AppendChild(mappingNode);

            mappingNode.Attributes.Append(ruleInstance.CreateAttribute("ActionPropertyCollection"));
            mappingNode.Attributes["ActionPropertyCollection"].Value = "Results";

            mappingNode.Attributes.Append(ruleInstance.CreateAttribute("Type"));
            mappingNode.Attributes["Type"].Value = resultName;

            XmlNode contextItemNode = ruleInstance.CreateElement("Item");
            XmlNode targetItemNode = ruleInstance.CreateElement("Item");

            contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("ContextType"));
            contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("ItemType"));
            contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("InstanceID"));
            contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("SubFormID"));
            contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("SubFormInstanceID"));

            contextItemNode.Attributes["ContextType"].Value = "context";
            contextItemNode.Attributes["ItemType"].Value = GetMappingSourceTypeForXML(lr.SourceType);
            contextItemNode.Attributes["InstanceID"].Value = lr.SourceInstanceGuid.ToString();
            contextItemNode.Attributes["SubFormID"].Value = lr.SourceSubFormGuid.ToString();
            contextItemNode.Attributes["SubFormInstanceID"].Value = lr.SourceSubFormInstanceGuid.ToString();

            if (!string.IsNullOrEmpty(lr.SourceName))
            {
                contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("Name"));
                contextItemNode.Attributes["Name"].Value = lr.SourceName;
            }
            if (!string.IsNullOrEmpty(lr.SourceDisplayName))
            {
                contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("DisplayName"));
                contextItemNode.Attributes["DisplayName"].Value = lr.SourceDisplayName;
            }

            targetItemNode.Attributes.Append(ruleInstance.CreateAttribute("ContextType"));
            targetItemNode.Attributes.Append(ruleInstance.CreateAttribute("ItemType"));
            targetItemNode.Attributes.Append(ruleInstance.CreateAttribute("InstanceID"));
            targetItemNode.Attributes.Append(ruleInstance.CreateAttribute("SubFormID"));
            targetItemNode.Attributes.Append(ruleInstance.CreateAttribute("SubFormInstanceID"));
            targetItemNode.Attributes.Append(ruleInstance.CreateAttribute("TargetPath"));

            targetItemNode.Attributes["ContextType"].Value = "target";
            targetItemNode.Attributes["ItemType"].Value = GetMappingTargetTypeForXML(lr.TargetType);
            targetItemNode.Attributes["InstanceID"].Value = lr.TargetInstanceGuid.ToString();
            targetItemNode.Attributes["SubFormID"].Value = lr.TargetSubFormGuid.ToString();
            targetItemNode.Attributes["SubFormInstanceID"].Value = lr.TargetSubFormInstanceGuid.ToString();
            targetItemNode.Attributes["TargetPath"].Value = lr.TargetPath;

            if (!string.IsNullOrEmpty(lr.TargetName))
            {
                targetItemNode.Attributes.Append(ruleInstance.CreateAttribute("Name"));
                targetItemNode.Attributes["Name"].Value = lr.TargetName;
            }
            if (!string.IsNullOrEmpty(lr.TargetDisplayName))
            {
                targetItemNode.Attributes.Append(ruleInstance.CreateAttribute("DisplayName"));
                targetItemNode.Attributes["DisplayName"].Value = lr.TargetDisplayName;
            }

            AnnotateMapping(mappingsNode, contextItemNode, targetItemNode, lr.Validation);

            switch (lr.SourceType)
            {
                case MappingSourceType.Control:
                case MappingSourceType.ViewField:
                case MappingSourceType.Expression:
                    contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("Guid"));
                    contextItemNode.Attributes["Guid"].Value = lr.SourceID;
                    break;
                case MappingSourceType.Value:
                    contextItemNode.Attributes["ContextType"].Value = "value";
                    XmlDocument xSourceValue = new XmlDocument();
                    xSourceValue.PreserveWhitespace = true;
                    string xmlSource = "<Sources>" + lr.SourceValue + "</Sources>";
                    XmlHelper.LoadXmlDocument(xSourceValue, xmlSource);
                    if (xSourceValue.SelectSingleNode("Sources/Source") != null)
                    {
                        contextItemNode.InnerXml = CreateItemsFromSources(xSourceValue.SelectSingleNode("Sources"), ruleInstance);
                    }
                    else if (!string.IsNullOrEmpty(lr.SourceValue))
                    {
                        contextItemNode.InnerXml = lr.SourceValue;
                    }

                    xSourceValue = null;
                    contextItemNode.Attributes.Remove(contextItemNode.Attributes["ItemType"]);
                    break;
                case MappingSourceType.ControlProperty:
                case MappingSourceType.ControlField:
                    contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("SourceID"));
                    contextItemNode.Attributes["SourceID"].Value = lr.SourceID;
                    contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("SourcePath"));
                    contextItemNode.Attributes["SourcePath"].Value = lr.SourcePath;
                    break;
                default:
                    contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("Name"));
                    contextItemNode.Attributes["Name"].Value = lr.SourceID;
                    break;
            }

            switch (lr.TargetType)
            {
                case MappingTargetType.Control:
                case MappingTargetType.ViewField:
                case MappingTargetType.ViewSource:
                    targetItemNode.Attributes.Append(ruleInstance.CreateAttribute("Guid"));
                    targetItemNode.Attributes["Guid"].Value = lr.TargetID;
                    break;
                case MappingTargetType.ControlProperty:
                case MappingTargetType.ControlMethodParameter:
                    targetItemNode.Attributes.Append(ruleInstance.CreateAttribute("TargetID"));
                    targetItemNode.Attributes["TargetID"].Value = lr.TargetID;
                    break;
                default:
                    targetItemNode.Attributes.Append(ruleInstance.CreateAttribute("Name"));
                    targetItemNode.Attributes["Name"].Value = lr.TargetID;
                    break;
            }

            mappingNode.AppendChild(contextItemNode);
            mappingNode.AppendChild(targetItemNode);

            return mappingNode;
        }

        private string CreateItemsFromSources(XmlNode sourceValueNode, XmlDocument ruleInstance)
        {
            XmlNode contextSourceValueNode = ruleInstance.CreateElement("SourceValue");

            XmlNodeList sources = sourceValueNode.SelectNodes("Source");
            foreach (XmlNode source in sources)
            {
                string sourceID = source.Attributes["SourceID"] != null ? source.Attributes["SourceID"].Value : string.Empty;
                string sourceType = source.Attributes["SourceType"] != null ? source.Attributes["SourceType"].Value : "Value";

                XmlNode contextItemNode = ruleInstance.CreateElement("Item");
                contextSourceValueNode.AppendChild(contextItemNode);

                contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("ContextType"));

                contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("ItemType"));

                contextItemNode.Attributes["ContextType"].Value = "context";
                contextItemNode.Attributes["ItemType"].Value = sourceType;

                XmlAttribute sourceName = source.Attributes["SourceName"];
                if (sourceName != null && !string.IsNullOrEmpty(sourceName.Value))
                {
                    contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("Name"));
                    contextItemNode.Attributes["Name"].Value = sourceName.Value;
                }

                XmlAttribute sourceDisplayName = source.Attributes["SourceDisplayName"];
                if (sourceDisplayName != null && !string.IsNullOrEmpty(sourceDisplayName.Value))
                {
                    contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("DisplayName"));
                    contextItemNode.Attributes["DisplayName"].Value = sourceDisplayName.Value;
                }

                XmlAttribute sourceInstanceID = source.Attributes["SourceInstanceID"];
                if (sourceInstanceID != null && !string.IsNullOrEmpty(sourceInstanceID.Value))
                {
                    contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("InstanceID"));
                    contextItemNode.Attributes["InstanceID"].Value = sourceInstanceID.Value;
                }

                XmlAttribute sourceSubFormID = source.Attributes["SourceSubFormID"];
                if (sourceSubFormID != null && !string.IsNullOrEmpty(sourceSubFormID.Value))
                {
                    contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("SubFormID"));
                    contextItemNode.Attributes["SubFormID"].Value = sourceSubFormID.Value;
                }

                XmlAttribute sourceSubFormInstanceID = source.Attributes["SourceSubFormInstanceID"];
                if (sourceSubFormInstanceID != null && !string.IsNullOrEmpty(sourceSubFormInstanceID.Value))
                {
                    contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("SubFormInstanceID"));
                    contextItemNode.Attributes["SubFormInstanceID"].Value = sourceSubFormInstanceID.Value;
                }

                XmlAttribute displayPath = source.Attributes["DisplayPath"];
                if (displayPath != null && !string.IsNullOrEmpty(displayPath.Value))
                {
                    contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("DisplayPath"));
                    contextItemNode.Attributes["DisplayPath"].Value = displayPath.Value;
                }

                XmlAttribute sourceTemplateId = source.Attributes["SourceTemplateId"];
                if (sourceTemplateId != null && !string.IsNullOrEmpty(sourceTemplateId.Value))
                {
                    contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("SourceTemplateId"));
                    contextItemNode.Attributes["SourceTemplateId"].Value = sourceTemplateId.Value;
                }

                // Add Invalid='true' to all decendant Items as well and not just the wrapping Item
                XmlAttribute validationStatusAttr = source.Attributes["ValidationStatus"];
                if (validationStatusAttr != null)
                {
                    WSF.ValidationStatus status = (WSF.ValidationStatus)Enum.Parse(typeof(WSF.ValidationStatus), validationStatusAttr.Value, true);
                    XmlAttribute validationMessagesAttr = source.Attributes["ValidationMessages"];

                    if ((status & WSF.ValidationStatus.Missing) == WSF.ValidationStatus.Missing
                    || (status & WSF.ValidationStatus.Error) == WSF.ValidationStatus.Error
                    || ((status & WSF.ValidationStatus.Warning) == WSF.ValidationStatus.Warning))
                    {
                        contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("Invalid"));
                        contextItemNode.Attributes["Invalid"].Value = "true";

                        if (validationMessagesAttr != null)
                        {
                            contextItemNode.Attributes.Append(contextItemNode.OwnerDocument.CreateAttribute("ValidationMessages"));
                            contextItemNode.Attributes["ValidationMessages"].Value = validationMessagesAttr.Value;
                        }
                    }
                }


                MappingSourceType lp = (MappingSourceType)Enum.Parse(typeof(MappingSourceType), sourceType, true);

                switch (lp)
                {
                    case MappingSourceType.Control:
                    case MappingSourceType.ViewField:
                    case MappingSourceType.Expression:
                        //case MappingSourceType.ResultField:
                        contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("Guid"));
                        contextItemNode.Attributes["Guid"].Value = sourceID;
                        break;
                    case MappingSourceType.Value:
                        contextItemNode.Attributes["ContextType"].Value = "value";
                        string sourceValue = source.InnerXml;
                        if (!string.IsNullOrEmpty(sourceValue))
                            contextItemNode.InnerXml = sourceValue;
                        contextItemNode.Attributes.Remove(contextItemNode.Attributes["ItemType"]);
                        break;
                    case MappingSourceType.ControlProperty:
                    case MappingSourceType.ControlField:
                        contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("SourceID"));
                        contextItemNode.Attributes["SourceID"].Value = source.Attributes["SourceID"].Value;
                        contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("SourcePath"));
                        contextItemNode.Attributes["SourcePath"].Value = source.Attributes["SourcePath"].Value;
                        break;
                    default:
                        contextItemNode.Attributes.Append(ruleInstance.CreateAttribute("Name"));
                        contextItemNode.Attributes["Name"].Value = sourceID;
                        break;
                }
            }

            return contextSourceValueNode.OuterXml;
        }

        #endregion Action Collections API -> XML

        private BaseAPIConnection GetConnection()
        {
            if (_connection == null)
            {
                BaseAPI api = new BaseAPI();
                api.CreateConnection();
                api.Connection.Open(_connectionString);
                _connection = api.Connection;
                _closeConnection = true;
            }

            return _connection;
        }

        private WSC.FormsClient GetFormsClient()
        {
            if (_wsClient == null)
            {
                _wsClient = new WSC.FormsClient();
                _wsClient.Connection = GetConnection();
            }
            return _wsClient;
        }

        private WSM.FormsManager GetFormsManager()
        {
            if (_wsManager == null)
            {
                _wsManager = new WSM.FormsManager();
                _wsManager.Connection = GetConnection();
            }
            return _wsManager;
        }

        private Event GetEvent(IStateContainer formOrView, Context context)
        {
            // -- SFIID TODO: temp fix to remove when Events have an SFIID
            Guid instanceIdToUse = context.InstanceGuid;
            if (context.SubformInstanceGuid != Guid.Empty)
            {
                instanceIdToUse = context.SubformInstanceGuid;
            }
            // -- end
            Event userEvent = null;
            if (formOrView != null)
            {
                foreach (State state in formOrView.States)
                {
                    foreach (Event @event in state.Events)
                    {
                        if (@event.DefinitionGuid == context.TargetEventGuid && @event.EventType == EventType.User && @event.InstanceGuid.Equals(instanceIdToUse) && @event.SubFormGuid.Equals(context.SubformGuid))
                        {
                            userEvent = @event;
                            break;
                        }
                    }
                    if (userEvent != null) { break; }
                }
            }

            return userEvent;
        }

        private Event GetEvent(BaseObject obj)
        {
            if (obj == null)
                return null;

            Event evt = obj.Parent as Event;

            if (evt != null)
                return evt;

            return GetEvent(obj.Parent);
        }

        private string evaluateExpression(XmlNode currentNode, bool ignoreGrouping)
        {
            string result = string.Empty;
            try
            {
                if (currentNode.Name == "Item")
                {
                    if (currentNode.Attributes["SourceType"].Value == "Value")
                    {
                        result = currentNode.InnerText;
                    }
                    else
                    {
                        if (currentNode.Attributes["Name"] != null)
                        {
                            result = currentNode.Attributes["Name"].Value;
                        }
                        else if (currentNode.SelectSingleNode("Display") != null)
                        {
                            result = currentNode.SelectSingleNode("Display").Value;
                        }
                    }
                }
                else if (currentNode.Name == "Left" || currentNode.Name == "Right")
                {
                    result = evaluateExpression(currentNode.ChildNodes[0], false);
                }
                else if (currentNode.ChildNodes.Count > 0)
                {
                    if (!ignoreGrouping)
                    {
                        result += "(";
                    }
                    result += evaluateExpression(currentNode.ChildNodes[0], false);
                    result += " " + currentNode.Name;
                    if (currentNode.ChildNodes.Count > 1)
                    {
                        result += " " + evaluateExpression(currentNode.ChildNodes[1], false);
                    }
                    if (!ignoreGrouping)
                    {
                        result += ")";
                    }
                }
            }
            catch
            {
            }
            return result;
        }

        private XmlNode BuildPartNode(XmlDocument ruleInstance, string partValue, string partDisplay, string partName)
        {
            XmlNode partNode = ruleInstance.CreateElement("Part");
            XmlNode partValueNode = ruleInstance.CreateElement("Value");
            XmlNode partDisplayNode = ruleInstance.CreateElement("Display");

            partNode.Attributes.Append(ruleInstance.CreateAttribute("Name"));
            partNode.Attributes["Name"].Value = partName;

            partValueNode.AppendChild(ruleInstance.CreateCDataSection(partValue));
            partDisplayNode.AppendChild(ruleInstance.CreateCDataSection(partDisplay));

            partNode.AppendChild(partValueNode);
            partNode.AppendChild(partDisplayNode);
            return partNode;
        }

        private XmlNode BuildPartDataNode(XmlDocument ruleInstance, string instanceID, string subFormID, string name, Guid? guid, string itemType, XmlNode parentNode)
        {
            XmlNode dataNode = parentNode.SelectSingleNode("Data");
            if (dataNode == null)
            {
                dataNode = ruleInstance.CreateElement("Data");
                parentNode.AppendChild(dataNode);
            }

            XmlNode dataItemNode = ruleInstance.CreateElement("Item");
            XmlNode dataItemNameNode = ruleInstance.CreateElement("Name");
            XmlNode dataItemInstanceNode;

            if (!string.IsNullOrEmpty(instanceID))
            {
                dataItemInstanceNode = ruleInstance.CreateElement("InstanceID");
                dataItemNode.AppendChild(dataItemInstanceNode);

                dataItemInstanceNode.AppendChild(ruleInstance.CreateCDataSection(instanceID));
            }

            if (!string.IsNullOrEmpty(subFormID))
            {
                dataItemNode.Attributes.Append(ruleInstance.CreateAttribute("SubFormID"));
                dataItemNode.Attributes["SubFormID"].Value = subFormID;
            }

            if (guid != null && !guid.Equals(Guid.Empty))
            {
                dataItemNode.Attributes.Append(ruleInstance.CreateAttribute("Guid"));
                dataItemNode.Attributes["Guid"].Value = guid.ToString();
            }

            dataItemNode.Attributes.Append(ruleInstance.CreateAttribute("ItemType"));
            dataItemNode.Attributes["ItemType"].Value = itemType;

            dataItemNode.AppendChild(dataItemNameNode);
            dataNode.AppendChild(dataItemNode);

            return dataNode;
        }

        private XmlNode BuildPartDataNode(XmlDocument ruleInstance, string instanceID, string subFormID, string name, Guid? guid, string itemType, string dataType, XmlNode parentNode)
        {
            XmlNode dataNode = parentNode.SelectSingleNode("Data");
            if (dataNode == null)
            {
                dataNode = ruleInstance.CreateElement("Data");
                parentNode.AppendChild(dataNode);
            }

            XmlNode dataItemNode = ruleInstance.CreateElement("Item");
            XmlNode dataItemNameNode = ruleInstance.CreateElement("Name");
            XmlNode dataItemInstanceNode;

            if (!string.IsNullOrEmpty(instanceID))
            {
                dataItemInstanceNode = ruleInstance.CreateElement("InstanceID");
                dataItemNode.AppendChild(dataItemInstanceNode);

                dataItemInstanceNode.AppendChild(ruleInstance.CreateCDataSection(instanceID));
            }

            if (!string.IsNullOrEmpty(subFormID))
            {
                dataItemNode.Attributes.Append(ruleInstance.CreateAttribute("SubFormID"));
                dataItemNode.Attributes["SubFormID"].Value = subFormID;
            }

            if (!string.IsNullOrEmpty(dataType))
            {
                dataItemNode.Attributes.Append(ruleInstance.CreateAttribute("SubType"));
                dataItemNode.Attributes["SubType"].Value = dataType;
            }

            if (guid != null && !guid.Equals(Guid.Empty))
            {
                dataItemNode.Attributes.Append(ruleInstance.CreateAttribute("Guid"));
                dataItemNode.Attributes["Guid"].Value = guid.ToString();
            }

            dataItemNode.Attributes.Append(ruleInstance.CreateAttribute("ItemType"));
            dataItemNode.Attributes["ItemType"].Value = itemType;

            dataItemNode.AppendChild(dataItemNameNode);
            dataNode.AppendChild(dataItemNode);

            return dataNode;
        }

        private XmlNode BuildObjectPartDataNode(XmlDocument ruleInstance, Guid objectId, string objectName, string objectDisplayName, XmlNode parentNode)
        {
            XmlNode dataNode = parentNode.SelectSingleNode("Data");
            if (dataNode == null)
            {
                dataNode = ruleInstance.CreateElement("Data");
                parentNode.AppendChild(dataNode);
            }

            XmlNode dataItemNode = ruleInstance.CreateElement("Item");

            dataItemNode.Attributes.Append(ruleInstance.CreateAttribute("Name"));
            dataItemNode.Attributes["Name"].Value = objectName;

            dataItemNode.Attributes.Append(ruleInstance.CreateAttribute("DisplayName"));
            dataItemNode.Attributes["DisplayName"].Value = objectDisplayName;

            dataItemNode.Attributes.Append(ruleInstance.CreateAttribute("ID"));
            dataItemNode.Attributes["ID"].Value = objectId.ToString();

            dataItemNode.Attributes.Append(ruleInstance.CreateAttribute("ItemType"));
            dataItemNode.Attributes["ItemType"].Value = "Object";

            dataNode.AppendChild(dataItemNode);

            return dataNode;
        }

        private XmlNode BuildPartSubFormDataNode(XmlDocument ruleInstance, string subFormID, string instanceID, string name, string itemType, string subFormTargetID, XmlNode parentNode)
        {
            XmlNode dataNode = parentNode.SelectSingleNode("Data");
            if (dataNode == null)
            {
                dataNode = ruleInstance.CreateElement("Data");
                parentNode.AppendChild(dataNode);
            }

            XmlNode dataItemNode = ruleInstance.CreateElement("Item");
            XmlNode dataItemNameNode = ruleInstance.CreateElement("Name");
            XmlNode dataItemInstanceNode;

            dataItemNode.Attributes.Append(ruleInstance.CreateAttribute("SubFormID"));
            dataItemNode.Attributes.Append(ruleInstance.CreateAttribute("ID"));
            dataItemNode.Attributes["SubFormID"].Value = subFormID;
            dataItemNode.Attributes["ID"].Value = subFormTargetID;

            if (!string.IsNullOrEmpty(instanceID))
            {
                dataItemInstanceNode = ruleInstance.CreateElement("InstanceID");
                dataItemNode.AppendChild(dataItemInstanceNode);

                dataItemInstanceNode.AppendChild(ruleInstance.CreateCDataSection(instanceID));
            }

            dataItemNameNode.AppendChild(ruleInstance.CreateCDataSection(name));

            dataItemNode.Attributes.Append(ruleInstance.CreateAttribute("ItemType"));
            dataItemNode.Attributes["ItemType"].Value = itemType;

            dataItemNode.AppendChild(dataItemNameNode);
            dataNode.AppendChild(dataItemNode);

            return dataNode;
        }

        private XmlNode BuildPartSubFormDataNode(XmlDocument ruleInstance, string subFormID, string instanceID, string subFormInstanceID, string name, string itemType, string subFormTargetID, XmlNode parentNode)
        {
            XmlNode dataNode = parentNode.SelectSingleNode("Data");
            if (dataNode == null)
            {
                dataNode = ruleInstance.CreateElement("Data");
                parentNode.AppendChild(dataNode);
            }

            XmlNode dataItemNode = ruleInstance.CreateElement("Item");
            XmlNode dataItemNameNode = ruleInstance.CreateElement("Name");

            dataItemNode.Attributes.Append(ruleInstance.CreateAttribute("SubFormID"));
            dataItemNode.Attributes.Append(ruleInstance.CreateAttribute("InstanceID"));
            dataItemNode.Attributes.Append(ruleInstance.CreateAttribute("SubFormInstanceID"));
            dataItemNode.Attributes.Append(ruleInstance.CreateAttribute("ID"));
            dataItemNode.Attributes["SubFormID"].Value = subFormID;
            dataItemNode.Attributes["InstanceID"].Value = instanceID;
            dataItemNode.Attributes["SubFormInstanceID"].Value = subFormInstanceID;
            dataItemNode.Attributes["ID"].Value = subFormTargetID;

            dataItemNameNode.AppendChild(ruleInstance.CreateCDataSection(name));

            dataItemNode.Attributes.Append(ruleInstance.CreateAttribute("ItemType"));
            dataItemNode.Attributes["ItemType"].Value = itemType;

            dataItemNode.AppendChild(dataItemNameNode);
            dataNode.AppendChild(dataItemNode);

            return dataNode;
        }

        private XmlNode BuildPartSubFormDataNode(XmlDocument ruleInstance, string subFormID, string instanceID, XmlNode parentNode)
        {
            XmlNode dataNode = parentNode.SelectSingleNode("Data");
            if (dataNode == null)
            {
                dataNode = ruleInstance.CreateElement("Data");
                parentNode.AppendChild(dataNode);
            }

            XmlNode dataItemNode = ruleInstance.CreateElement("Item");
            XmlNode dataItemNameNode = ruleInstance.CreateElement("Name");
            XmlNode dataItemInstanceNode;

            dataItemNode.Attributes.Append(ruleInstance.CreateAttribute("SubFormID"));
            dataItemNode.Attributes.Append(ruleInstance.CreateAttribute("ID"));
            dataItemNode.Attributes["SubFormID"].Value = subFormID;

            if (!string.IsNullOrEmpty(instanceID))
            {
                dataItemInstanceNode = ruleInstance.CreateElement("InstanceID");
                dataItemNode.AppendChild(dataItemInstanceNode);

                dataItemInstanceNode.AppendChild(ruleInstance.CreateCDataSection(instanceID));
            }

            dataItemNode.AppendChild(dataItemNameNode);
            dataNode.AppendChild(dataItemNode);

            return dataNode;
        }

        private string ResolveMviName(Context context)
        {
            string mviName = context.View != null ? context.View.DisplayName : context.viewName;
            Dictionary<Guid, int> viewsDictionary = new Dictionary<Guid, int>();
            Guid viewGuid = context.View != null ? context.View.Guid : context.viewGuid;

            if (context.Form != null && viewGuid != Guid.Empty)
            {
                foreach (Panel panel in context.Form.Panels)
                {
                    foreach (Area area in panel.Areas)
                    {
                        foreach (AreaItem item in area.Items)
                        {
                            if (item.ViewGuid != Guid.Empty)
                            {
                                if (viewsDictionary.ContainsKey(item.ViewGuid))
                                {
                                    viewsDictionary[item.ViewGuid]++;
                                }
                                else
                                {
                                    viewsDictionary.Add(item.ViewGuid, 1);
                                }
                            }
                        }
                    }
                }

                bool found = false;
                if (viewsDictionary.ContainsKey(viewGuid) && viewsDictionary[viewGuid] > 1)
                {
                    for (int i = 0; i < context.Form.Panels.Count && !found; i++)
                    {
                        Panel panel = context.Form.Panels[i];
                        for (int j = 0; j < panel.Areas.Count && !found; j++)
                        {
                            Area area = panel.Areas[j];
                            for (int k = 0; k < area.Items.Count && !found; k++)
                            {
                                AreaItem item = area.Items[k];
                                if (item.ViewGuid != Guid.Empty && (item.Guid == context.SubformInstanceGuid || (context.SubformInstanceGuid == Guid.Empty && item.Guid == context.InstanceGuid)))
                                {
                                    string title = item.Properties["Title"];
                                    if (!string.IsNullOrEmpty(title))
                                    {
                                        mviName = String.Format(Resources.FormsHelper.ViewInstanceDisplayFormat, mviName, title);
                                    }
                                    else
                                    {
                                        mviName = String.Format(Resources.FormsHelper.ViewInstanceDisplayFormat, mviName, item.Properties["ControlName"]);
                                    }

                                    found = true;
                                }
                            }
                        }
                    }
                }
            }

            context.viewMviName = mviName;
            return context.viewMviName;
        }

        private Control ResolveControl(Context context, ControlCollection controls, Event ev)
        {
            string sourceDisplayName = context.controlGuid.Equals(ev.SourceGuid) ? ev.SourceName : string.Empty;
            WSF.ValidationResult validation = ev.Validation;
            if (string.IsNullOrEmpty(sourceDisplayName))
            {
                if (context.Condition != null) // callstack is from BuildContext(LogicalExpression, Condition)
                {
                    PropertyExpression controlExpression = GetExpressionBySourceTypeFromOperands(context, PropertyExpressionSourceType.Control);
                    if (controlExpression != null)
                    {
                        sourceDisplayName = controlExpression.SourceDisplayName;
                        validation = controlExpression.Validation;
                    }
                }
                else if (context.Action != null) // callstack is from BuildContext(Action)
                {
                    WSA.Property actionProperty = GetPropertyByName(context.Action.Properties, "ControlID");
                    if (actionProperty != null)
                    {
                        sourceDisplayName = actionProperty.DisplayValue;
                        validation = actionProperty.Validation;
                    }
                }
            }

            return ResolveControl(context, controls, validation, sourceDisplayName);
        }

        private Control ResolveControl(Context context, ControlCollection controls, Authoring.Eventing.Action action)
        {
            context.controlGuid = !context.controlGuid.Equals(Guid.Empty) ? context.controlGuid : action.ControlGuid;
            return ResolveControl(context, controls, action.Validation, string.Empty);
        }

        private Control ResolveControl(Context context, ControlCollection controls, PropertyExpression expression)
        {
            WSF.ValidationResult validation = null;
            string sourceDisplayName = string.Empty;
            if (expression != null)
            {
                validation = expression.Validation;
                sourceDisplayName = expression.SourceDisplayName;
            }
            return ResolveControl(context, controls, validation, sourceDisplayName);
        }

        private Control ResolveControl(Context context, ControlCollection controls, WSF.ValidationResult validation, string defaultControlName)
        {
            context.Control = controls[context.controlGuid];
            if (context.Control == null)
            {
                ValidationMessageParts messageParts = GetValidationMessageParts(validation, ReferenceType.Control, defaultControlName);
                context.controlName = !string.IsNullOrEmpty(messageParts.RefDisplayName) ? messageParts.RefDisplayName : messageParts.RefName;
            }
            else
            {
                context.controlName = !string.IsNullOrEmpty(context.Control.DisplayName) ? context.Control.DisplayName : context.Control.Name;
            }
            return context.Control;
        }

        private void ResolveFormViewControlName(Event ev, Guid controlGuid, Context context, Guid subformGuid)
        {
            if (subformGuid == Guid.Empty)
            {
                if (ev.View != null)
                {
                    ResolveView(context, ev);
                    ResolveControl(context, context.View != null ? context.View.Controls : new ControlCollection(null), ev);
                }
                else
                {
                    ResolveForm(context, ev);
                    ResolveFormView(context);
                    ResolveControl(context, context.View != null ? context.View.Controls : new ControlCollection(null), ev);
                }
            }
            else
            {
                GetSubFormAction(context, subformGuid, ev);
                if (context.SubItemAction.FormGuid == Guid.Empty)
                {
                    ResolveExternalView(context, context.SubItemAction);
                    ResolveControl(context, context.View != null ? context.View.Controls : new ControlCollection(null), ev);
                    context.EventFriendlyName = GetEventFriendlyNameForSubForm(context.SubItemAction);
                }
                else
                {
                    ResolveExternalForm(context, context.SubItemAction);
                    ResolveFormView(context);
                    ResolveControl(context, context.View != null ? context.View.Controls : new ControlCollection(null), ev);
                    context.EventFriendlyName = GetEventFriendlyNameForSubForm(context.SubItemAction);
                }
            }
        }

        private View ResolveView(Context context, Event ev)
        {
            context.View = ev.View;
            if (context.View != null)
            {
                context.viewGuid = context.View.Guid;
                context.viewName = context.View.DisplayName;
                context.viewSystemName = context.View.Name;
                context.viewType = context.View.Type.ToString();
            }
            else
            {
                context.viewGuid = Guid.Empty;
                context.viewName = string.Empty;
                context.viewSystemName = string.Empty;
            }

            ResolveMviName(context);
            return context.View;
        }

        /// <summary>
        /// This function will return the event for the specified action. Could not use the event as the action might sometimes be on another context
        /// </summary>
        private View ResolveView(Context context, Authoring.Eventing.Action action)
        {
            return ResolveView(context, GetEvent(action));
        }

        private View ResolveExternalView(Context context, Authoring.Eventing.Action actionToUse = null)
        {
            if (actionToUse == null)
            {
                actionToUse = context.SubItemAction != null ? context.SubItemAction : context.Action;
            }

            context.View = InfoProvider.GetView(actionToUse.ViewGuid);
            if (context.View == null)
            {
                ValidationMessageParts messageParts;
                if ((actionToUse.Validation.Status & WSF.ValidationStatus.Error) == WSF.ValidationStatus.Error)
                {
                    WSA.Property view = GetPropertyByName(actionToUse.Properties, "ViewID");
                    messageParts = GetValidationMessageParts(view.Validation, ReferenceType.View, string.Empty);
                }
                else
                {
                    messageParts = GetValidationMessageParts(actionToUse.Validation, ReferenceType.View, string.Empty);
                }

                context.viewName = messageParts.RefDisplayName;
                context.viewGuid = messageParts.RefGuid;
                context.viewSystemName = messageParts.RefName;
            }
            else
            {
                context.viewName = context.View.DisplayName;
                context.viewGuid = context.View.Guid;
                context.viewSystemName = context.View.Name;
                context.viewType = context.View.Type.ToString();
            }
            ResolveMviName(context);

            return context.View;
        }

        private View ResolveFormView(Context context, WSF.ValidationResult validationResult = null)
        {
            ReferenceType[] referenceTypeArray = new ReferenceType[3] { ReferenceType.View, ReferenceType.ViewInstance, ReferenceType.SubForm };
            context.View = null;
            if (context.Form != null)
            {
                foreach (Panel instancePanel in context.Form.Panels)
                {
                    foreach (Area instanceArea in instancePanel.Areas)
                    {
                        foreach (AreaItem instanceAreaItem in instanceArea.Items)
                        {
                            if (context.SubformInstanceGuid == instanceAreaItem.Guid || (context.SubformInstanceGuid == Guid.Empty && instanceAreaItem.Guid == context.InstanceGuid))
                            {
                                context.viewGuid = instanceAreaItem.ViewGuid;
                                context.View = InfoProvider.GetView(instanceAreaItem.ViewGuid);
                                if (context.View == null)
                                {
                                    if (validationResult == null)
                                    {
                                        validationResult = instanceAreaItem.Validation;
                                    }

                                    ValidationMessageParts messageParts = GetValidationMessageParts(validationResult, referenceTypeArray, instanceAreaItem.ViewDisplayName);
                                    context.viewName = messageParts.RefDisplayName;
                                    context.viewSystemName = messageParts.RefName;
                                }
                                else
                                {
                                    context.viewName = context.View.DisplayName;
                                    context.viewSystemName = context.View.Name;
                                    context.viewType = context.View.Type.ToString();
                                }
                                ResolveMviName(context);
                                return context.View;
                            }
                        }
                    }
                }
            }

            // TFS 759795 - deleted View Instance may still be able to be resolved if the View has not been deleted.
            // The action may have a ViewID property
            if (context.View == null && context.Action != null)
            {
                // do not assign tmp to context.View because it does not exist on the Form
                View tmp = InfoProvider.GetView(context.Action.ViewGuid);
                if (tmp != null)
                {
                    context.viewName = tmp.DisplayName;
                    context.viewGuid = tmp.Guid;
                    context.viewSystemName = tmp.Name;
                    context.viewType = tmp.Type.ToString();
                    ResolveMviName(context);
                }
            }

            if (context.View == null)
            {
                ValidationMessageParts messageParts = GetValidationMessageParts(validationResult, referenceTypeArray, string.Empty);
                context.viewName = messageParts.RefDisplayName;
                context.viewGuid = messageParts.RefGuid;
                context.viewSystemName = messageParts.RefName;
                ResolveMviName(context);
            }

            return context.View;
        }

        private View ResolveFormView(Context context, HandlerFunction fn)
        {
            View view = ResolveFormView(context, fn.Validation);
            if (context.viewGuid.Equals(Guid.Empty))
            {
                ReferenceType[] referenceTypeArray = new ReferenceType[3] { ReferenceType.View, ReferenceType.ViewInstance, ReferenceType.SubForm };
                ValidationMessageParts messageParts = GetValidationMessageParts(fn.Validation, referenceTypeArray, string.Empty);
                context.viewName = messageParts.RefDisplayName;
                context.viewSystemName = messageParts.RefName;
                context.viewGuid = !string.IsNullOrEmpty(context.viewName) ? messageParts.RefGuid : Guid.Empty;
                ResolveMviName(context);
            }
            return view;
        }

        private ViewParameter ResolveViewParameter(Context context, Guid sourceGuid, WSF.ValidationResult validationResult)
        {
            context.parameterGuid = sourceGuid;
            if (context.View != null && context.View.Parameters.Contains(context.parameterGuid))
            {
                context.viewParameter = context.View.Parameters[context.parameterGuid];
                context.parameterName = context.viewParameter.Name;
                context.parameterDisplayName = context.viewParameter.Name;
            }
            else
            {
                ValidationMessageParts messageParts = GetValidationMessageParts(validationResult, ReferenceType.ViewParameter, string.Empty);
                context.parameterName = messageParts.RefName;
                context.parameterDisplayName = messageParts.RefDisplayName;
            }

            return context.viewParameter;
        }

        private ViewParameter ResolveViewParameter(Context context, string sourceName)
        {
            context.parameterName = sourceName;
            if (context.View != null && context.View.Parameters.Contains(context.parameterName))
            {
                context.viewParameter = context.View.Parameters[context.parameterName];
                context.parameterGuid = context.viewParameter.Guid;
                context.parameterDisplayName = context.viewParameter.Name;
            }

            return context.viewParameter;
        }

        private FormParameter ResolveFormParameter(Context context, Guid sourceGuid, WSF.ValidationResult validationResult)
        {
            context.parameterGuid = sourceGuid;
            if (context.Form != null && context.Form.Parameters.Contains(context.parameterGuid))
            {
                context.formParameter = context.Form.Parameters[context.parameterGuid];
                context.parameterName = context.formParameter.Name;
                context.parameterDisplayName = context.formParameter.Name;
            }
            else
            {
                ValidationMessageParts messageParts = GetValidationMessageParts(validationResult, ReferenceType.FormParameter, string.Empty);
                context.parameterName = messageParts.RefName;
                context.parameterDisplayName = messageParts.RefDisplayName;
            }

            return context.formParameter;
        }

        private FormParameter ResolveFormParameter(Context context, string sourceName)
        {
            context.parameterName = sourceName;
            if (context.Form != null && context.Form.Parameters.Contains(context.parameterName))
            {
                context.formParameter = context.Form.Parameters[context.parameterName];
                context.parameterGuid = context.formParameter.Guid;
                context.parameterDisplayName = context.formParameter.Name;
            }

            return context.formParameter;
        }

        private Form ResolveForm(Context context, Event ev)
        {
            context.Form = ev.Form;
            if (context.Form == null)
            {
                context.formGuid = Guid.Empty;
                context.formName = string.Empty;
                context.formSystemName = string.Empty;
            }
            else
            {
                context.formGuid = context.Form.Guid;
                context.formName = context.Form.DisplayName;
                context.formSystemName = context.Form.Name;
            }
            return context.Form;
        }

        private Form ResolveExternalForm(Context context, Event ev)
        {
            context.Form = InfoProvider.GetForm(ev.SourceGuid);

            if (context.Form == null)
            {
                ValidationMessageParts messageParts = GetValidationMessageParts(ev.Validation, ReferenceType.Form, string.Empty); // what is default name?
                context.formName = messageParts.RefDisplayName;
                context.formGuid = messageParts.RefGuid;
                context.formSystemName = messageParts.RefName;
            }
            else
            {
                context.formName = context.Form.DisplayName;
                context.formGuid = context.Form.Guid;
                context.formSystemName = context.Form.Name;
            }

            return context.Form;
        }

        private Form ResolveExternalForm(Context context, Authoring.Eventing.Action actionToUse = null)
        {
            if (actionToUse == null)
            {
                actionToUse = context.SubItemAction != null ? context.SubItemAction : context.Action;
            }

            context.Form = InfoProvider.GetForm(actionToUse.FormGuid);

            if (context.Form == null)
            {
                ValidationMessageParts messageParts;
                if ((actionToUse.Validation.Status & WSF.ValidationStatus.Error) == WSF.ValidationStatus.Error)
                {
                    WSA.Property form = GetPropertyByName(actionToUse.Properties, "FormID");
                    messageParts = GetValidationMessageParts(form.Validation, ReferenceType.Form, string.Empty);
                }
                else
                {
                    messageParts = GetValidationMessageParts(actionToUse.Validation, ReferenceType.Form, string.Empty);
                }

                context.formName = messageParts.RefDisplayName;
                context.formGuid = messageParts.RefGuid;
                context.formSystemName = messageParts.RefName;
            }
            else
            {
                context.formName = context.Form.DisplayName;
                context.formGuid = context.Form.Guid;
                context.formSystemName = context.Form.Name;
            }

            return context.Form;
        }

        private Panel ResolvePanel(Context context, Authoring.Eventing.Action action)
        {
            if (context.Form != null)
            {
                context.Panel = context.Form.Panels[action.PanelGuid];
            }

            if (context.Panel != null)
            {
                context.panelGuid = context.Panel.Guid;
                context.panelName = context.Panel.DisplayName;
            }
            else
            {
                Authoring.Property prop = GetPropertyByName(action.Properties, "PanelID");
                ValidationMessageParts messageParts = GetValidationMessageParts(prop.Validation, ReferenceType.Panel, string.Empty);
                context.panelName = messageParts.RefDisplayName;
                context.panelGuid = messageParts.RefGuid;
            }

            return context.Panel;
        }
        #endregion
        #endregion
    }

    internal static class RuleExtensionMethods
    {
        internal static Event GetByDefinitionGuid(this EventCollection items, Guid definitionGuid)
        {
            if (items == null)
            {
                return null;
            }

            foreach (Event evt in items)
            {
                if (evt.DefinitionGuid.Equals(definitionGuid) && evt.EventType == EventType.User)
                {
                    return evt;
                }
            }

            return null;
        }

        internal static T GetByDefinitionGuid<P, T>(this BaseCollection<P, T> items, Guid definitionGuid)
            where P : BaseObject
            where T : BaseObject, IPredecessorSortable<T>
        {
            if (items == null)
            {
                return default(T);
            }

            foreach (T item in items)
            {
                if (item.DefinitionGuid == definitionGuid)
                {
                    return item;
                }
            }

            return default(T);
        }
    }
}
