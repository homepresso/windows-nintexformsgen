using System.Xml;

namespace K2SmartObjectGenerator.Utilities
{
    public static class XmlHelper
    {
        public static void AddElement(XmlDocument doc, XmlElement parent, string elementName, string value)
        {
            XmlElement element = doc.CreateElement(elementName);
            if (!string.IsNullOrEmpty(value))
            {
                element.InnerText = value;
            }
            parent.AppendChild(element);
        }
    }
}