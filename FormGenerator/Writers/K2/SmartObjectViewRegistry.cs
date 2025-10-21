using System;
using System.Collections.Generic;
using System.Linq;

namespace K2SmartObjectGenerator
{
    /// <summary>
    /// Central registry for tracking generated SmartObjects, Views, and Forms
    /// </summary>
    public static class SmartObjectViewRegistry
    {
        private static readonly Dictionary<string, SmartObjectInfo> _smartObjects = new();
        private static readonly Dictionary<string, ViewInfo> _views = new();
        private static readonly Dictionary<string, FormInfo> _forms = new();

        public enum SmartObjectType
        {
            Main,
            Child,
            Lookup
        }

        public enum ViewType
        {
            Capture,
            Item,
            List
        }

        #region SmartObject Management

        public static void RegisterSmartObject(string name, SmartObjectType type, string? parentName = null)
        {
            _smartObjects[name] = new SmartObjectInfo
            {
                Name = name,
                Type = type,
                ParentName = parentName
            };
        }

        public static bool SmartObjectExists(string name)
        {
            return _smartObjects.ContainsKey(name);
        }

        public static int GetSmartObjectCount(SmartObjectType? type = null)
        {
            if (type == null)
                return _smartObjects.Count;

            return _smartObjects.Values.Count(s => s.Type == type);
        }

        public static List<string> GetChildSmartObjects(string parentName)
        {
            return _smartObjects.Values
                .Where(s => s.ParentName == parentName && s.Type == SmartObjectType.Child)
                .Select(s => s.Name)
                .ToList();
        }

        #endregion

        #region View Management

        public static void RegisterView(string name, string smartObjectName, ViewType type)
        {
            _views[name] = new ViewInfo
            {
                Name = name,
                SmartObjectName = smartObjectName,
                Type = type
            };
        }

        public static bool ViewExists(string name)
        {
            return _views.ContainsKey(name);
        }

        public static int GetViewCount(ViewType? type = null)
        {
            if (type == null)
                return _views.Count;

            return _views.Values.Count(v => v.Type == type);
        }

        public static List<string> GetViewsForSmartObject(string smartObjectName)
        {
            return _views.Values
                .Where(v => v.SmartObjectName == smartObjectName)
                .Select(v => v.Name)
                .ToList();
        }

        #endregion

        #region Form Management

        public static void RegisterForm(string name, List<string> viewNames, List<string> smartObjectNames)
        {
            _forms[name] = new FormInfo
            {
                Name = name,
                ViewNames = viewNames,
                SmartObjectNames = smartObjectNames
            };
        }

        public static bool FormExists(string name)
        {
            return _forms.ContainsKey(name);
        }

        public static int GetFormCount()
        {
            return _forms.Count;
        }

        #endregion

        #region Utility Methods

        public static void Clear()
        {
            _smartObjects.Clear();
            _views.Clear();
            _forms.Clear();
        }

        #endregion

        #region Internal Models

        private class SmartObjectInfo
        {
            public string Name { get; set; } = string.Empty;
            public SmartObjectType Type { get; set; }
            public string? ParentName { get; set; }
        }

        private class ViewInfo
        {
            public string Name { get; set; } = string.Empty;
            public string SmartObjectName { get; set; } = string.Empty;
            public ViewType Type { get; set; }
        }

        private class FormInfo
        {
            public string Name { get; set; } = string.Empty;
            public List<string> ViewNames { get; set; } = new();
            public List<string> SmartObjectNames { get; set; } = new();
        }

        #endregion
    }
}
