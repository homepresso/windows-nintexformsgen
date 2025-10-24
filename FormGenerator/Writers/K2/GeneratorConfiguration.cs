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
        public LoggingConfiguration Logging { get; set; }

        public GeneratorConfiguration()
        {
            Server = new ServerConfiguration();
            ControlFilters = new ControlFilterConfiguration();
            K2 = new K2Configuration();
            Form = new FormConfiguration();
            View = new ViewConfiguration();
            Logging = new LoggingConfiguration();
        }

        public static GeneratorConfiguration CreateDefault()
        {
            return new GeneratorConfiguration();
        }
    }

    public class ServerConfiguration
    {
        public string DefaultHostName { get; set; } = "localhost";
        public uint DefaultPort { get; set; } = 5555;

        // Aliases for compatibility
        public string HostName
        {
            get => DefaultHostName;
            set => DefaultHostName = value;
        }
        public uint Port
        {
            get => DefaultPort;
            set => DefaultPort = value;
        }
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
        public string DefaultTheme { get; set; } = "_Dynamic";
        public List<string> AvailableThemes { get; set; }
        public string TargetFolder { get; set; } = "Generated";
        public bool ForceCleanup { get; set; } = false;
        public bool UseTimestamp { get; set; } = false;

        // Alias for compatibility
        public string Theme
        {
            get => DefaultTheme;
            set => DefaultTheme = value;
        }

        public FormConfiguration()
        {
            AvailableThemes = new List<string>
            {
                "Lithium",
                "Platinum",
                "SharePoint 2013",
                "Blue Void"
            };
        }

        public bool IsValidTheme(string theme)
        {
            return AvailableThemes?.Any(t =>
                string.Equals(t, theme, StringComparison.OrdinalIgnoreCase)) ?? false;
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

    public class LoggingConfiguration
    {
        /// <summary>
        /// Enable verbose logging (detailed retry messages, validation steps, etc.)
        /// Controlled by "Enable Verbose Logging" checkbox in UI
        /// </summary>
        public bool VerboseLogging { get; set; } = false;

        /// <summary>
        /// Show progress indicators for major operations
        /// Controlled by "Enable Verbose Logging" checkbox in UI
        /// When false, all logging is disabled for maximum performance with large forms
        /// </summary>
        public bool ShowProgress { get; set; } = false;
    }
}