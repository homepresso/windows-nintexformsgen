using System;
using System.Collections.Generic;
using System.Xml;
using K2SmartObjectGenerator.Config;

namespace K2SmartObjectGenerator.Utilities
{
    /// <summary>
    /// Factory class for creating reusable K2 form and view controls
    /// </summary>
    public static class ControlFactory
    {
        #region Button Creation

        /// <summary>
        /// Creates a standard button control for forms
        /// </summary>
        public static XmlElement CreateFormButton(XmlDocument doc, string id, string name, string text, ButtonStyle style = ButtonStyle.Default)
        {
            var control = CreateBaseControl(doc, id, "Button", name);

            var properties = doc.CreateElement("Properties");

            // Add text property
            AddPropertyElement(doc, properties, "Text", text);
            AddPropertyElement(doc, properties, "ControlName", name);

            // Add style-specific properties
            ApplyButtonStyle(doc, properties, style);

            control.AppendChild(properties);
            return control;
        }

        /// <summary>
        /// Creates a toolbar button control for views
        /// </summary>
        public static XmlElement CreateViewToolbarButton(XmlDocument doc, string id, string name, string text, ToolbarButtonType buttonType = ToolbarButtonType.Add)
        {
            var control = CreateBaseControl(doc, id, "ToolBarButton", name);

            var properties = doc.CreateElement("Properties");

            AddPropertyElement(doc, properties, "Text", text);
            AddPropertyElement(doc, properties, "ControlName", name);
            AddPropertyElement(doc, properties, "ButtonType", buttonType.ToString());

            // Add toolbar button specific properties
            ApplyToolbarButtonProperties(doc, properties, buttonType);

            control.AppendChild(properties);
            AddDefaultStyles(doc, control);

            return control;
        }

        /// <summary>
        /// Creates a view button control (non-toolbar)
        /// </summary>
        public static XmlElement CreateViewButton(XmlDocument doc, string id, string name, string text)
        {
            var control = CreateBaseControl(doc, id, "Button", name);

            var properties = doc.CreateElement("Properties");

            AddPropertyElement(doc, properties, "Text", text);
            AddPropertyElement(doc, properties, "ControlName", name);

            control.AppendChild(properties);
            AddDefaultStyles(doc, control);

            return control;
        }

        #endregion

        #region Other Control Types

        /// <summary>
        /// Creates a label control
        /// </summary>
        public static XmlElement CreateLabel(XmlDocument doc, string id, string name, string text)
        {
            var control = CreateBaseControl(doc, id, "Label", name);

            var properties = doc.CreateElement("Properties");
            AddPropertyElement(doc, properties, "Text", text);
            AddPropertyElement(doc, properties, "ControlName", name);

            control.AppendChild(properties);
            AddDefaultStyles(doc, control);

            return control;
        }

        /// <summary>
        /// Creates a text input control
        /// </summary>
        public static XmlElement CreateTextInput(XmlDocument doc, string id, string name, string defaultValue = "")
        {
            var control = CreateBaseControl(doc, id, "TextBox", name);

            var properties = doc.CreateElement("Properties");
            AddPropertyElement(doc, properties, "ControlName", name);

            if (!string.IsNullOrEmpty(defaultValue))
            {
                AddPropertyElement(doc, properties, "DefaultValue", defaultValue);
            }

            control.AppendChild(properties);
            AddDefaultStyles(doc, control);

            return control;
        }

        /// <summary>
        /// Creates a dropdown/choice control
        /// </summary>
        public static XmlElement CreateDropdown(XmlDocument doc, string id, string name, List<string> options = null)
        {
            var control = CreateBaseControl(doc, id, "DropDownList", name);

            var properties = doc.CreateElement("Properties");
            AddPropertyElement(doc, properties, "ControlName", name);

            // Add options if provided
            if (options != null && options.Count > 0)
            {
                AddDropdownOptions(doc, properties, options);
            }

            control.AppendChild(properties);
            AddDefaultStyles(doc, control);

            return control;
        }

        /// <summary>
        /// Creates a row container control
        /// </summary>
        public static XmlElement CreateRow(XmlDocument doc, string id, string name)
        {
            var control = CreateBaseControl(doc, id, "Row", name);

            var properties = doc.CreateElement("Properties");
            AddPropertyElement(doc, properties, "ControlName", name);

            control.AppendChild(properties);
            AddDefaultStyles(doc, control);

            return control;
        }

        /// <summary>
        /// Creates a cell container control
        /// </summary>
        public static XmlElement CreateCell(XmlDocument doc, string id, string name)
        {
            var control = CreateBaseControl(doc, id, "Cell", name);

            var properties = doc.CreateElement("Properties");
            AddPropertyElement(doc, properties, "ControlName", name);

            control.AppendChild(properties);
            AddDefaultStyles(doc, control);

            return control;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Creates base control structure with ID, Type, Name, and DisplayName
        /// </summary>
        private static XmlElement CreateBaseControl(XmlDocument doc, string id, string type, string name)
        {
            var control = doc.CreateElement("Control");
            control.SetAttribute("ID", id);
            control.SetAttribute("Type", type);

            XmlHelper.AddElement(doc, control, "Name", name);
            XmlHelper.AddElement(doc, control, "DisplayName", name);

            return control;
        }

        /// <summary>
        /// Adds a property element to the properties container
        /// </summary>
        private static void AddPropertyElement(XmlDocument doc, XmlElement properties, string name, string value)
        {
            var prop = doc.CreateElement("Property");
            XmlHelper.AddElement(doc, prop, "Name", name);
            XmlHelper.AddElement(doc, prop, "Value", value);
            XmlHelper.AddElement(doc, prop, "DisplayValue", value);
            properties.AppendChild(prop);
        }

        /// <summary>
        /// Applies button style properties
        /// </summary>
        private static void ApplyButtonStyle(XmlDocument doc, XmlElement properties, ButtonStyle style)
        {
            switch (style)
            {
                case ButtonStyle.Primary:
                    AddPropertyElement(doc, properties, "CssClass", "btn-primary");
                    break;
                case ButtonStyle.Secondary:
                    AddPropertyElement(doc, properties, "CssClass", "btn-secondary");
                    break;
                case ButtonStyle.Success:
                    AddPropertyElement(doc, properties, "CssClass", "btn-success");
                    break;
                case ButtonStyle.Danger:
                    AddPropertyElement(doc, properties, "CssClass", "btn-danger");
                    break;
                case ButtonStyle.Default:
                default:
                    // No additional styling
                    break;
            }
        }

        /// <summary>
        /// Applies toolbar button specific properties
        /// </summary>
        private static void ApplyToolbarButtonProperties(XmlDocument doc, XmlElement properties, ToolbarButtonType buttonType)
        {
            switch (buttonType)
            {
                case ToolbarButtonType.Add:
                    AddPropertyElement(doc, properties, "IconClass", "fa-plus");
                    break;
                case ToolbarButtonType.Edit:
                    AddPropertyElement(doc, properties, "IconClass", "fa-edit");
                    break;
                case ToolbarButtonType.Delete:
                    AddPropertyElement(doc, properties, "IconClass", "fa-trash");
                    break;
                case ToolbarButtonType.Save:
                    AddPropertyElement(doc, properties, "IconClass", "fa-save");
                    break;
                case ToolbarButtonType.Cancel:
                    AddPropertyElement(doc, properties, "IconClass", "fa-times");
                    break;
            }
        }

        /// <summary>
        /// Adds dropdown options to properties
        /// </summary>
        private static void AddDropdownOptions(XmlDocument doc, XmlElement properties, List<string> options)
        {
            var optionsElement = doc.CreateElement("Options");

            foreach (var option in options)
            {
                var optionElement = doc.CreateElement("Option");
                optionElement.SetAttribute("Value", option);
                optionElement.SetAttribute("Text", option);
                optionsElement.AppendChild(optionElement);
            }

            properties.AppendChild(optionsElement);
        }

        /// <summary>
        /// Adds default styles to control
        /// </summary>
        private static void AddDefaultStyles(XmlDocument doc, XmlElement control)
        {
            var styles = doc.CreateElement("Styles");
            var style = doc.CreateElement("Style");
            style.SetAttribute("IsDefault", "True");
            styles.AppendChild(style);
            control.AppendChild(styles);
        }

        #endregion
    }

    #region Enums

    /// <summary>
    /// Button visual styles
    /// </summary>
    public enum ButtonStyle
    {
        Default,
        Primary,
        Secondary,
        Success,
        Danger
    }

    /// <summary>
    /// Toolbar button types
    /// </summary>
    public enum ToolbarButtonType
    {
        Add,
        Edit,
        Delete,
        Save,
        Cancel,
        Custom
    }

    #endregion
}