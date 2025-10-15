using System;
using System.Collections.Generic;
using System.Xml;

namespace K2SmartObjectGenerator.Utilities
{
    /// <summary>
    /// Fluent builder for creating XML elements with properties and styles
    /// </summary>
    public class XmlElementBuilder
    {
        private readonly XmlDocument _doc;
        private readonly string _elementName;
        private readonly Dictionary<string, string> _attributes = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _childElements = new Dictionary<string, string>();
        private readonly List<XmlElement> _childNodes = new List<XmlElement>();
        private readonly Dictionary<string, string> _properties = new Dictionary<string, string>();
        private bool _addDefaultStyles = false;

        private XmlElementBuilder(XmlDocument doc, string elementName)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _elementName = elementName ?? throw new ArgumentNullException(nameof(elementName));
        }

        /// <summary>
        /// Creates a new XML element builder
        /// </summary>
        public static XmlElementBuilder Create(XmlDocument doc, string elementName)
        {
            return new XmlElementBuilder(doc, elementName);
        }

        /// <summary>
        /// Adds an attribute to the element
        /// </summary>
        public XmlElementBuilder WithAttribute(string name, string value)
        {
            _attributes[name] = value;
            return this;
        }

        /// <summary>
        /// Adds a child element with text content
        /// </summary>
        public XmlElementBuilder WithChildElement(string name, string value)
        {
            _childElements[name] = value;
            return this;
        }

        /// <summary>
        /// Adds a child XML element
        /// </summary>
        public XmlElementBuilder WithChild(XmlElement child)
        {
            _childNodes.Add(child);
            return this;
        }

        /// <summary>
        /// Adds a property to the Properties collection
        /// </summary>
        public XmlElementBuilder WithProperty(string name, string value)
        {
            _properties[name] = value;
            return this;
        }

        /// <summary>
        /// Enables default styles for the element
        /// </summary>
        public XmlElementBuilder WithDefaultStyles()
        {
            _addDefaultStyles = true;
            return this;
        }

        /// <summary>
        /// Builds the XML element
        /// </summary>
        public XmlElement Build()
        {
            var element = _doc.CreateElement(_elementName);

            // Add attributes
            foreach (var attr in _attributes)
            {
                element.SetAttribute(attr.Key, attr.Value);
            }

            // Add child elements
            foreach (var child in _childElements)
            {
                XmlHelper.AddElement(_doc, element, child.Key, child.Value);
            }

            // Add properties if any
            if (_properties.Count > 0)
            {
                var propertiesElement = _doc.CreateElement("Properties");
                foreach (var prop in _properties)
                {
                    AddPropertyElement(propertiesElement, prop.Key, prop.Value);
                }
                element.AppendChild(propertiesElement);
            }

            // Add child nodes
            foreach (var child in _childNodes)
            {
                element.AppendChild(child);
            }

            // Add default styles if requested
            if (_addDefaultStyles)
            {
                AddDefaultStyles(element);
            }

            return element;
        }

        private void AddPropertyElement(XmlElement properties, string name, string value)
        {
            var prop = _doc.CreateElement("Property");
            XmlHelper.AddElement(_doc, prop, "Name", name);
            XmlHelper.AddElement(_doc, prop, "Value", value);
            XmlHelper.AddElement(_doc, prop, "DisplayValue", value);
            properties.AppendChild(prop);
        }

        private void AddDefaultStyles(XmlElement element)
        {
            var styles = _doc.CreateElement("Styles");
            var style = _doc.CreateElement("Style");
            style.SetAttribute("IsDefault", "True");
            styles.AppendChild(style);
            element.AppendChild(styles);
        }
    }

    /// <summary>
    /// Common XML building patterns
    /// </summary>
    public static class XmlBuilderExtensions
    {
        /// <summary>
        /// Creates a control element with standard attributes
        /// </summary>
        public static XmlElementBuilder CreateControl(this XmlDocument doc, string id, string type, string name)
        {
            return XmlElementBuilder.Create(doc, "Control")
                .WithAttribute("ID", id)
                .WithAttribute("Type", type)
                .WithChildElement("Name", name)
                .WithChildElement("DisplayName", name)
                .WithProperty("ControlName", name);
        }

        /// <summary>
        /// Creates a row element with standard structure
        /// </summary>
        public static XmlElementBuilder CreateRow(this XmlDocument doc, string name)
        {
            return XmlElementBuilder.Create(doc, "Row")
                .WithChildElement("Name", name)
                .WithChildElement("DisplayName", name)
                .WithProperty("ControlName", name)
                .WithDefaultStyles();
        }

        /// <summary>
        /// Creates a cell element
        /// </summary>
        public static XmlElementBuilder CreateCell(this XmlDocument doc, string name)
        {
            return XmlElementBuilder.Create(doc, "Cell")
                .WithChildElement("Name", name)
                .WithChildElement("DisplayName", name)
                .WithProperty("ControlName", name);
        }

        /// <summary>
        /// Creates a property element
        /// </summary>
        public static XmlElement CreateProperty(this XmlDocument doc, string name, string value, string displayValue = null)
        {
            return XmlElementBuilder.Create(doc, "Property")
                .WithChildElement("Name", name)
                .WithChildElement("Value", value)
                .WithChildElement("DisplayValue", displayValue ?? value)
                .Build();
        }

        /// <summary>
        /// Creates an event element with standard structure
        /// </summary>
        public static XmlElementBuilder CreateEvent(this XmlDocument doc, string sourceId, string sourceType, string sourceName, string eventName = "OnClick")
        {
            return XmlElementBuilder.Create(doc, "Event")
                .WithAttribute("ID", Guid.NewGuid().ToString())
                .WithAttribute("DefinitionID", Guid.NewGuid().ToString())
                .WithAttribute("Type", "User")
                .WithAttribute("SourceID", sourceId)
                .WithAttribute("SourceType", sourceType)
                .WithAttribute("SourceName", sourceName)
                .WithAttribute("SourceDisplayName", sourceName)
                .WithAttribute("IsExtended", "True")
                .WithChildElement("Name", eventName);
        }

        /// <summary>
        /// Creates an action element
        /// </summary>
        public static XmlElementBuilder CreateAction(this XmlDocument doc, string actionType, string targetId = null)
        {
            var builder = XmlElementBuilder.Create(doc, "Action")
                .WithAttribute("ID", Guid.NewGuid().ToString())
                .WithAttribute("DefinitionID", Guid.NewGuid().ToString())
                .WithAttribute("Type", actionType);

            if (!string.IsNullOrEmpty(targetId))
            {
                builder.WithAttribute("TargetID", targetId);
            }

            return builder;
        }
    }
}