using System;
using System.Collections.Generic;
using System.Linq;

namespace K2SmartObjectGenerator.Config
{
    public class GeneratorConfiguration
    {
        public ServerConfiguration Server { get; set; }
        public ControlFilterConfiguration ControlFilters { get; set; }
        public K2Configuration K2 { get; set; }
        public FormConfiguration Form { get; set; }
        public ViewConfiguration View { get; set; }

        public GeneratorConfiguration()
        {
            Server = new ServerConfiguration();
            ControlFilters = new ControlFilterConfiguration();
            K2 = new K2Configuration();
            Form = new FormConfiguration();
            View = new ViewConfiguration();
        }

        public static GeneratorConfiguration CreateDefault()
        {
            return new GeneratorConfiguration();
        }
    }

    public class ServerConfiguration
    {
        public string HostName { get; set; }
        public uint Port { get; set; }
    }

    public class ControlFilterConfiguration
    {
        public List<string> NonRenderableControlTypes { get; set; }
        public List<string> SkippedControlTypesInItemViews { get; set; }
        public List<string> ControlTypesToExcludeFromSmartObjects { get; set; }

        public ControlFilterConfiguration()
        {
            NonRenderableControlTypes = new List<string>
            {
                "repeatingtable",
                "repeatingsection",
                "section",
                "optionalsection"
            };

            SkippedControlTypesInItemViews = new List<string>
            {
                "button"
            };

            ControlTypesToExcludeFromSmartObjects = new List<string>
            {
                "label",
                "button",
                "image"
            };
        }

        public bool IsNonRenderable(string controlType)
        {
            return NonRenderableControlTypes?.Any(t =>
                string.Equals(t, controlType, StringComparison.OrdinalIgnoreCase)) ?? false;
        }

        public bool ShouldSkipInItemView(string controlType)
        {
            return SkippedControlTypesInItemViews?.Any(t =>
                string.Equals(t, controlType, StringComparison.OrdinalIgnoreCase)) ?? false;
        }

        public bool ShouldExcludeFromSmartObject(string controlType)
        {
            return ControlTypesToExcludeFromSmartObjects?.Any(t =>
                string.Equals(t, controlType, StringComparison.OrdinalIgnoreCase)) ?? false;
        }
    }

    public class K2Configuration
    {
        public string SmartBoxGuid { get; set; } = "e5609413-d844-4325-98c3-db3cacbd406d";
        public List<string> SystemFieldsToFilter { get; set; }

        public K2Configuration()
        {
            SystemFieldsToFilter = new List<string>
            {
                "ID",
                "ParentID"
            };
        }

        public bool IsSystemField(string fieldName)
        {
            return SystemFieldsToFilter?.Any(f =>
                string.Equals(f, fieldName, StringComparison.OrdinalIgnoreCase)) ?? false;
        }
    }

    public class FormConfiguration
    {
        public string Theme { get; set; }
        public bool UseTimestamp { get; set; }
        public bool ForceCleanup { get; set; }
        public string TargetFolder { get; set; } = "TestForms";

        public static List<string> AvailableThemes => new List<string>
        {
            "Lithium",
            "Platinum",
            "SharePoint 2013",
            "Blue Void",
            "_Dynamic"
        };

        public static bool IsValidTheme(string theme)
        {
            return AvailableThemes.Any(t =>
                string.Equals(t, theme, StringComparison.OrdinalIgnoreCase));
        }
    }

    public class ViewConfiguration
    {
        public List<string> ViewTypesToExcludeFromFormRules { get; set; }

        public ViewConfiguration()
        {
            ViewTypesToExcludeFromFormRules = new List<string>
            {
                "List",
                "Item"
            };
        }

        public bool ShouldExcludeFromFormRules(string viewName)
        {
            return ViewTypesToExcludeFromFormRules?.Any(type =>
                viewName?.IndexOf(type, StringComparison.OrdinalIgnoreCase) >= 0) ?? false;
        }
    }
}