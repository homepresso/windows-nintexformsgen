using System;
using System.Xml;
using K2SmartObjectGenerator.Utilities;

namespace K2SmartObjectGenerator.Utilities
{
    /// <summary>
    /// Builds K2 Format XML elements from InfoPath formatting information
    /// </summary>
    public static class K2FormatBuilder
    {
        /// <summary>
        /// Creates a K2 Format XML element from InfoPath formatting properties
        /// </summary>
        /// <param name="doc">XML document to create elements in</param>
        /// <param name="datafmt">InfoPath datafmt string</param>
        /// <param name="boundProp">InfoPath boundProp string</param>
        /// <returns>K2 Format XML element or null if no formatting needed</returns>
        public static XmlElement CreateFormatElement(XmlDocument doc, string datafmt, string boundProp = null)
        {
            // Parse InfoPath format information
            var formatInfo = InfoPathFormatParser.ParseDataFormat(datafmt, boundProp);
            if (formatInfo == null)
                return null;

            // Create Format element
            XmlElement formatElement = doc.CreateElement("Format");

            // Set format type
            string formatType = InfoPathFormatParser.ToK2FormatType(formatInfo);
            if (!string.IsNullOrEmpty(formatType))
            {
                formatElement.SetAttribute("Type", formatType);
            }

            // Set culture
            if (!string.IsNullOrEmpty(formatInfo.Culture))
            {
                formatElement.SetAttribute("Culture", formatInfo.Culture);
            }

            // Set negative pattern
            string negativePattern = InfoPathFormatParser.ToK2NegativePattern(formatInfo);
            if (!string.IsNullOrEmpty(negativePattern))
            {
                formatElement.SetAttribute("NegativePattern", negativePattern);
            }

            // Set currency symbol for currency formats
            if (formatInfo.Type?.ToLower() == "currency" && !string.IsNullOrEmpty(formatInfo.CurrencySymbol))
            {
                formatElement.SetAttribute("CurrencySymbol", formatInfo.CurrencySymbol);
            }

            // Set the format pattern as inner text
            string formatPattern = InfoPathFormatParser.ToK2FormatPattern(formatInfo);
            if (!string.IsNullOrEmpty(formatPattern))
            {
                formatElement.InnerText = formatPattern;
            }

            return formatElement;
        }

        /// <summary>
        /// Creates a complete Style element with Format for a control
        /// </summary>
        /// <param name="doc">XML document to create elements in</param>
        /// <param name="datafmt">InfoPath datafmt string</param>
        /// <param name="boundProp">InfoPath boundProp string</param>
        /// <returns>K2 Style XML element with Format, or null if no formatting needed</returns>
        public static XmlElement CreateStyleWithFormat(XmlDocument doc, string datafmt, string boundProp = null)
        {
            XmlElement formatElement = CreateFormatElement(doc, datafmt, boundProp);
            if (formatElement == null)
                return null;

            // Create Style element
            XmlElement styleElement = doc.CreateElement("Style");
            styleElement.SetAttribute("IsDefault", "True");

            // Add Format element to Style
            styleElement.AppendChild(formatElement);

            return styleElement;
        }

        /// <summary>
        /// Adds formatting to an existing control's Styles element
        /// </summary>
        /// <param name="controlElement">The K2 control XML element</param>
        /// <param name="datafmt">InfoPath datafmt string</param>
        /// <param name="boundProp">InfoPath boundProp string</param>
        /// <returns>True if formatting was applied, false otherwise</returns>
        public static bool ApplyFormattingToControl(XmlElement controlElement, string datafmt, string boundProp = null)
        {
            if (controlElement == null || string.IsNullOrEmpty(datafmt))
                return false;

            XmlDocument doc = controlElement.OwnerDocument;
            XmlElement styleWithFormat = CreateStyleWithFormat(doc, datafmt, boundProp);

            if (styleWithFormat == null)
                return false;

            // Find or create Styles element
            XmlElement stylesElement = controlElement.SelectSingleNode("Styles") as XmlElement;
            if (stylesElement == null)
            {
                stylesElement = doc.CreateElement("Styles");
                controlElement.AppendChild(stylesElement);
            }

            // Check if there's already a default style
            XmlElement existingDefaultStyle = stylesElement.SelectSingleNode("Style[@IsDefault='True']") as XmlElement;
            if (existingDefaultStyle != null)
            {
                // Add Format to existing style
                XmlElement formatElement = CreateFormatElement(doc, datafmt, boundProp);
                if (formatElement != null)
                {
                    existingDefaultStyle.AppendChild(formatElement);
                }
            }
            else
            {
                // Add new style with format
                stylesElement.AppendChild(styleWithFormat);
            }

            return true;
        }

        /// <summary>
        /// Updates DataType property for numeric controls based on InfoPath boundProp
        /// </summary>
        /// <param name="controlElement">The K2 control XML element</param>
        /// <param name="boundProp">InfoPath boundProp string</param>
        /// <returns>True if DataType was updated, false otherwise</returns>
        public static bool UpdateDataTypeFromBoundProp(XmlElement controlElement, string boundProp)
        {
            if (controlElement == null || string.IsNullOrEmpty(boundProp))
                return false;

            // Extract data type from boundProp
            string dataType = null;
            if (boundProp.StartsWith("xd:"))
            {
                string infopathType = boundProp.Substring(3).ToLower();
                switch (infopathType)
                {
                    case "num":
                        dataType = "Number";
                        break;
                    case "date":
                        dataType = "Date";
                        break;
                    case "string":
                    case "text":
                        dataType = "Text";
                        break;
                }
            }

            if (string.IsNullOrEmpty(dataType))
                return false;

            // Find DataType property and update it
            XmlNodeList properties = controlElement.SelectNodes("Properties/Property[@Name='DataType']");
            foreach (XmlElement property in properties)
            {
                // Update DisplayValue and Value
                XmlElement displayValue = property.SelectSingleNode("DisplayValue") as XmlElement;
                if (displayValue != null)
                {
                    displayValue.InnerText = dataType.ToLower();
                }

                XmlElement value = property.SelectSingleNode("Value") as XmlElement;
                if (value != null)
                {
                    value.InnerText = dataType.ToLower();
                }

                Console.WriteLine($"        Updated DataType property to '{dataType}' based on boundProp '{boundProp}'");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Determines if a control should have formatting applied based on its type and properties
        /// </summary>
        /// <param name="controlType">K2 control type (TextBox, etc.)</param>
        /// <param name="datafmt">InfoPath datafmt string</param>
        /// <param name="boundProp">InfoPath boundProp string</param>
        /// <returns>True if formatting should be applied</returns>
        public static bool ShouldApplyFormatting(string controlType, string datafmt, string boundProp)
        {
            // Only apply formatting to TextBox controls (input fields)
            if (controlType != "TextBox")
                return false;

            // Apply if we have datafmt or numeric boundProp
            if (!string.IsNullOrEmpty(datafmt))
                return true;

            if (!string.IsNullOrEmpty(boundProp) && boundProp.StartsWith("xd:"))
            {
                string infopathType = boundProp.Substring(3).ToLower();
                return infopathType == "num" || infopathType == "date";
            }

            return false;
        }
    }
}