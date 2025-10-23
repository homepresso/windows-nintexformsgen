using System;
using System.Collections.Generic;
using System.Xml;
using K2SmartObjectGenerator.Config;

namespace K2SmartObjectGenerator.Utilities
{
    /// <summary>
    /// Fluent builder for creating complex button configurations
    /// </summary>
    public class ButtonBuilder
    {
        private readonly XmlDocument _doc;
        private readonly string _id;
        private string _name;
        private string _text;
        private string _type = "Button";
        private ButtonStyle _style = ButtonStyle.Default;
        private readonly Dictionary<string, string> _properties = new Dictionary<string, string>();
        private readonly List<string> _cssClasses = new List<string>();
        private bool _isToolbarButton = false;
        private ToolbarButtonType _toolbarType = ToolbarButtonType.Add;

        private ButtonBuilder(XmlDocument doc, string id)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _id = id ?? throw new ArgumentNullException(nameof(id));
        }

        /// <summary>
        /// Creates a new button builder
        /// </summary>
        public static ButtonBuilder Create(XmlDocument doc, string id)
        {
            return new ButtonBuilder(doc, id);
        }

        /// <summary>
        /// Sets the button name
        /// </summary>
        public ButtonBuilder WithName(string name)
        {
            _name = name;
            return this;
        }

        /// <summary>
        /// Sets the button text
        /// </summary>
        public ButtonBuilder WithText(string text)
        {
            _text = text;
            return this;
        }

        /// <summary>
        /// Sets the button style
        /// </summary>
        public ButtonBuilder WithStyle(ButtonStyle style)
        {
            _style = style;
            return this;
        }

        /// <summary>
        /// Makes this a toolbar button
        /// </summary>
        public ButtonBuilder AsToolbarButton(ToolbarButtonType toolbarType = ToolbarButtonType.Add)
        {
            _isToolbarButton = true;
            _toolbarType = toolbarType;
            _type = "ToolBarButton";
            return this;
        }

        /// <summary>
        /// Adds a custom property
        /// </summary>
        public ButtonBuilder WithProperty(string name, string value)
        {
            _properties[name] = value;
            return this;
        }

        /// <summary>
        /// Adds a CSS class
        /// </summary>
        public ButtonBuilder WithCssClass(string cssClass)
        {
            _cssClasses.Add(cssClass);
            return this;
        }

        /// <summary>
        /// Sets up as an Add button with appropriate styling
        /// </summary>
        public ButtonBuilder AsAddButton()
        {
            WithText("Add")
                .WithStyle(ButtonStyle.Primary)
                .WithProperty("IconClass", "fa-plus");
            return this;
        }

        /// <summary>
        /// Sets up as a Cancel button with appropriate styling
        /// </summary>
        public ButtonBuilder AsCancelButton()
        {
            WithText("Cancel")
                .WithStyle(ButtonStyle.Secondary)
                .WithProperty("IconClass", "fa-times");
            return this;
        }

        /// <summary>
        /// Sets up as a Save button with appropriate styling
        /// </summary>
        public ButtonBuilder AsSaveButton()
        {
            WithText("Save")
                .WithStyle(ButtonStyle.Success)
                .WithProperty("IconClass", "fa-save");
            return this;
        }

        /// <summary>
        /// Sets up as a Delete button with appropriate styling
        /// </summary>
        public ButtonBuilder AsDeleteButton()
        {
            WithText("Delete")
                .WithStyle(ButtonStyle.Danger)
                .WithProperty("IconClass", "fa-trash");
            return this;
        }

        /// <summary>
        /// Builds the button XML element
        /// </summary>
        public XmlElement Build()
        {
            if (string.IsNullOrEmpty(_name))
                throw new InvalidOperationException("Button name is required");

            if (string.IsNullOrEmpty(_text))
                _text = _name; // Default text to name if not specified

            var control = _doc.CreateElement("Control");
            control.SetAttribute("ID", _id);
            control.SetAttribute("Type", _type);

            XmlHelper.AddElement(_doc, control, "Name", _name);
            XmlHelper.AddElement(_doc, control, "DisplayName", _name);

            var properties = _doc.CreateElement("Properties");

            // Add core properties
            AddProperty(properties, "Text", _text);
            AddProperty(properties, "ControlName", _name);

            // Add style-based properties
            ApplyStyleProperties(properties);

            // Add toolbar-specific properties
            if (_isToolbarButton)
            {
                ApplyToolbarProperties(properties);
            }

            // Add custom properties
            foreach (var prop in _properties)
            {
                AddProperty(properties, prop.Key, prop.Value);
            }

            // Add CSS classes
            if (_cssClasses.Count > 0)
            {
                AddProperty(properties, "CssClass", string.Join(" ", _cssClasses));
            }

            control.AppendChild(properties);

            // Add default styles
            AddDefaultStyles(control);

            return control;
        }

        private void AddProperty(XmlElement properties, string name, string value)
        {
            var prop = _doc.CreateElement("Property");
            XmlHelper.AddElement(_doc, prop, "Name", name);
            XmlHelper.AddElement(_doc, prop, "Value", value);
            XmlHelper.AddElement(_doc, prop, "DisplayValue", value);
            properties.AppendChild(prop);
        }

        private void ApplyStyleProperties(XmlElement properties)
        {
            switch (_style)
            {
                case ButtonStyle.Primary:
                    _cssClasses.Add("btn-primary");
                    break;
                case ButtonStyle.Secondary:
                    _cssClasses.Add("btn-secondary");
                    break;
                case ButtonStyle.Success:
                    _cssClasses.Add("btn-success");
                    break;
                case ButtonStyle.Danger:
                    _cssClasses.Add("btn-danger");
                    break;
            }
        }

        private void ApplyToolbarProperties(XmlElement properties)
        {
            AddProperty(properties, "ButtonType", _toolbarType.ToString());

            switch (_toolbarType)
            {
                case ToolbarButtonType.Add:
                    if (!_properties.ContainsKey("IconClass"))
                        AddProperty(properties, "IconClass", "fa-plus");
                    break;
                case ToolbarButtonType.Edit:
                    if (!_properties.ContainsKey("IconClass"))
                        AddProperty(properties, "IconClass", "fa-edit");
                    break;
                case ToolbarButtonType.Delete:
                    if (!_properties.ContainsKey("IconClass"))
                        AddProperty(properties, "IconClass", "fa-trash");
                    break;
                case ToolbarButtonType.Save:
                    if (!_properties.ContainsKey("IconClass"))
                        AddProperty(properties, "IconClass", "fa-save");
                    break;
                case ToolbarButtonType.Cancel:
                    if (!_properties.ContainsKey("IconClass"))
                        AddProperty(properties, "IconClass", "fa-times");
                    break;
            }
        }

        private void AddDefaultStyles(XmlElement control)
        {
            var styles = _doc.CreateElement("Styles");
            var style = _doc.CreateElement("Style");
            style.SetAttribute("IsDefault", "True");
            styles.AppendChild(style);
            control.AppendChild(styles);
        }
    }

    /// <summary>
    /// Extension methods for common button creation patterns
    /// </summary>
    public static class ButtonExtensions
    {
        /// <summary>
        /// Creates a standard Add button for item views
        /// </summary>
        public static XmlElement CreateStandardAddButton(this XmlDocument doc, string id, string name)
        {
            return ButtonBuilder.Create(doc, id)
                .WithName(name)
                .AsAddButton()
                .Build();
        }

        /// <summary>
        /// Creates a standard Cancel button for item views
        /// </summary>
        public static XmlElement CreateStandardCancelButton(this XmlDocument doc, string id, string name)
        {
            return ButtonBuilder.Create(doc, id)
                .WithName(name)
                .AsCancelButton()
                .Build();
        }

        /// <summary>
        /// Creates a toolbar Add button for list views
        /// </summary>
        public static XmlElement CreateToolbarAddButton(this XmlDocument doc, string id, string name)
        {
            return ButtonBuilder.Create(doc, id)
                .WithName(name)
                .AsToolbarButton(ToolbarButtonType.Add)
                .WithText("Add")
                .Build();
        }

        /// <summary>
        /// Creates a custom button with specified properties
        /// </summary>
        public static XmlElement CreateCustomButton(this XmlDocument doc, string id, string name, string text,
            ButtonStyle style = ButtonStyle.Default, params (string key, string value)[] properties)
        {
            var builder = ButtonBuilder.Create(doc, id)
                .WithName(name)
                .WithText(text)
                .WithStyle(style);

            foreach (var (key, value) in properties)
            {
                builder.WithProperty(key, value);
            }

            return builder.Build();
        }
    }
}