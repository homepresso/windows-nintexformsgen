using System;
using System.Collections.Generic;
using System.Linq;

namespace K2SmartObjectGenerator.Utilities
{
    /// <summary>
    /// Centralized registry for tracking all SmartObjects, Views, and Forms created during generation
    /// </summary>
    public static class SmartObjectViewRegistry
    {
        // Core data structures
        private static Dictionary<string, SmartObjectInfo> _smartObjects = new Dictionary<string, SmartObjectInfo>();
        private static Dictionary<string, ViewInfo> _views = new Dictionary<string, ViewInfo>();
        private static Dictionary<string, FormInfo> _forms = new Dictionary<string, FormInfo>();

        // Relationship mappings
        private static Dictionary<string, List<string>> _smartObjectToViews = new Dictionary<string, List<string>>();
        private static Dictionary<string, string> _viewToSmartObject = new Dictionary<string, string>();
        private static Dictionary<string, List<string>> _formToViews = new Dictionary<string, List<string>>();
        private static Dictionary<string, List<string>> _formToSmartObjects = new Dictionary<string, List<string>>();

        // Special mappings
        private static Dictionary<string, Dictionary<string, List<string>>> _repeatingSectionViews = new Dictionary<string, Dictionary<string, List<string>>>();
        private static Dictionary<string, string> _lookupSmartObjects = new Dictionary<string, string>();

        // Add Item Button mappings
        private static Dictionary<string, AddItemButtonInfo> _addItemButtons = new Dictionary<string, AddItemButtonInfo>();

        #region SmartObject Registration

        /// <summary>
        /// Register a SmartObject that has been created
        /// </summary>
        public static void RegisterSmartObject(string smoName, string smoGuid, SmartObjectType type, string parentSmo = null)
        {
            var smoInfo = new SmartObjectInfo
            {
                Name = smoName,
                Guid = smoGuid,
                Type = type,
                ParentSmartObject = parentSmo,
                CreatedAt = DateTime.Now
            };

            _smartObjects[smoName] = smoInfo;

            if (!_smartObjectToViews.ContainsKey(smoName))
            {
                _smartObjectToViews[smoName] = new List<string>();
            }

            Console.WriteLine($"[Registry] Registered SmartObject: {smoName} (Type: {type}, GUID: {smoGuid})");

            if (!string.IsNullOrEmpty(parentSmo))
            {
                Console.WriteLine($"           Parent: {parentSmo}");
            }
        }

        /// <summary>
        /// Register a lookup SmartObject
        /// </summary>
        public static void RegisterLookupSmartObject(string fieldName, string lookupSmoName, string lookupSmoGuid)
        {
            RegisterSmartObject(lookupSmoName, lookupSmoGuid, SmartObjectType.Lookup);
            _lookupSmartObjects[fieldName] = lookupSmoName;
            Console.WriteLine($"[Registry] Registered Lookup SmartObject: {lookupSmoName} for field: {fieldName}");
        }

        #endregion

        #region View Registration

        /// <summary>
        /// Register a view that has been created
        /// </summary>
        public static void RegisterView(string viewName, string smoName, ViewType type, ViewMetadata metadata = null)
        {
            var viewInfo = new ViewInfo
            {
                Name = viewName,
                SmartObjectName = smoName,
                Type = type,
                Metadata = metadata ?? new ViewMetadata(),
                CreatedAt = DateTime.Now
            };

            _views[viewName] = viewInfo;
            _viewToSmartObject[viewName] = smoName;

            // Add to SmartObject's view list
            if (!_smartObjectToViews.ContainsKey(smoName))
            {
                _smartObjectToViews[smoName] = new List<string>();
            }

            if (!_smartObjectToViews[smoName].Contains(viewName))
            {
                _smartObjectToViews[smoName].Add(viewName);
            }

            Console.WriteLine($"[Registry] Registered View: {viewName}");
            Console.WriteLine($"           SmartObject: {smoName}");
            Console.WriteLine($"           Type: {type}");

            if (metadata != null)
            {
                if (!string.IsNullOrEmpty(metadata.InfoPathViewName))
                    Console.WriteLine($"           InfoPath View: {metadata.InfoPathViewName}");
                if (!string.IsNullOrEmpty(metadata.RepeatingSectionName))
                    Console.WriteLine($"           Repeating Section: {metadata.RepeatingSectionName}");
                if (metadata.PartNumber > 0)
                    Console.WriteLine($"           Part Number: {metadata.PartNumber}");
            }
        }

        /// <summary>
        /// Register views for a repeating section
        /// </summary>
        public static void RegisterRepeatingSectionViews(string formName, string sectionName,
            string itemViewName, string listViewName, string childSmoName)
        {
            if (!_repeatingSectionViews.ContainsKey(formName))
            {
                _repeatingSectionViews[formName] = new Dictionary<string, List<string>>();
            }

            _repeatingSectionViews[formName][sectionName] = new List<string> { itemViewName, listViewName };

            // Register the individual views
            var itemMetadata = new ViewMetadata
            {
                RepeatingSectionName = sectionName,
                IsRepeatingSection = true
            };
            RegisterView(itemViewName, childSmoName, ViewType.Item, itemMetadata);

            var listMetadata = new ViewMetadata
            {
                RepeatingSectionName = sectionName,
                IsRepeatingSection = true
            };
            RegisterView(listViewName, childSmoName, ViewType.List, listMetadata);

            Console.WriteLine($"[Registry] Registered Repeating Section Views for: {sectionName}");
            Console.WriteLine($"           Item View: {itemViewName}");
            Console.WriteLine($"           List View: {listViewName}");
        }

        #endregion

        #region Form Registration

        /// <summary>
        /// Register a form that has been created
        /// </summary>
        public static void RegisterForm(string formName, List<string> viewNames, List<string> smoNames)
        {
            var formInfo = new FormInfo
            {
                Name = formName,
                Views = new List<string>(viewNames),
                SmartObjects = new List<string>(smoNames),
                CreatedAt = DateTime.Now
            };

            _forms[formName] = formInfo;
            _formToViews[formName] = new List<string>(viewNames);
            _formToSmartObjects[formName] = new List<string>(smoNames);

            Console.WriteLine($"[Registry] Registered Form: {formName}");
            Console.WriteLine($"           Views: {string.Join(", ", viewNames)}");
            Console.WriteLine($"           SmartObjects: {string.Join(", ", smoNames)}");
        }

        #endregion

        #region Query Methods

        /// <summary>
        /// Get all views for a SmartObject
        /// </summary>
        public static List<string> GetViewsForSmartObject(string smoName)
        {
            if (_smartObjectToViews.ContainsKey(smoName))
            {
                return new List<string>(_smartObjectToViews[smoName]);
            }
            return new List<string>();
        }

        /// <summary>
        /// Get all views of a specific type for a SmartObject
        /// </summary>
        public static List<string> GetViewsForSmartObject(string smoName, ViewType type)
        {
            var allViews = GetViewsForSmartObject(smoName);
            return allViews.Where(v => _views.ContainsKey(v) && _views[v].Type == type).ToList();
        }

        /// <summary>
        /// Get the SmartObject for a view
        /// </summary>
        public static string GetSmartObjectForView(string viewName)
        {
            if (_viewToSmartObject.ContainsKey(viewName))
            {
                return _viewToSmartObject[viewName];
            }
            return null;
        }

        /// <summary>
        /// Get view info
        /// </summary>
        public static ViewInfo GetViewInfo(string viewName)
        {
            if (_views.ContainsKey(viewName))
            {
                return _views[viewName];
            }
            return null;
        }

        /// <summary>
        /// Get SmartObject info
        /// </summary>
        public static SmartObjectInfo GetSmartObjectInfo(string smoName)
        {
            if (_smartObjects.ContainsKey(smoName))
            {
                return _smartObjects[smoName];
            }
            return null;
        }

        /// <summary>
        /// Get all child SmartObjects for a parent
        /// </summary>
        public static List<string> GetChildSmartObjects(string parentSmoName)
        {
            return _smartObjects.Values
                .Where(smo => smo.ParentSmartObject == parentSmoName)
                .Select(smo => smo.Name)
                .ToList();
        }

        /// <summary>
        /// Get repeating section views for a form
        /// </summary>
        public static Dictionary<string, List<string>> GetRepeatingSectionViews(string formName)
        {
            if (_repeatingSectionViews.ContainsKey(formName))
            {
                return new Dictionary<string, List<string>>(_repeatingSectionViews[formName]);
            }
            return new Dictionary<string, List<string>>();
        }

        /// <summary>
        /// Get views for a specific InfoPath view
        /// </summary>
        public static List<string> GetViewsForInfoPathView(string formName, string infopathViewName)
        {
            var results = new List<string>();

            foreach (var view in _views.Values)
            {
                if (view.Name.StartsWith(formName) &&
                    view.Metadata?.InfoPathViewName == infopathViewName)
                {
                    results.Add(view.Name);
                }
            }

            return results.OrderBy(v =>
            {
                var info = GetViewInfo(v);
                return info?.Metadata?.PartNumber ?? 0;
            }).ToList();
        }

        /// <summary>
        /// Check if a SmartObject exists
        /// </summary>
        public static bool SmartObjectExists(string smoName)
        {
            return _smartObjects.ContainsKey(smoName);
        }

        /// <summary>
        /// Check if a view exists
        /// </summary>
        public static bool ViewExists(string viewName)
        {
            return _views.ContainsKey(viewName);
        }

        /// <summary>
        /// Get lookup SmartObject for a field
        /// </summary>
        public static string GetLookupSmartObject(string fieldName)
        {
            if (_lookupSmartObjects.ContainsKey(fieldName))
            {
                return _lookupSmartObjects[fieldName];
            }
            return null;
        }

        #endregion

        #region Reporting Methods

        /// <summary>
        /// Generate a summary report of all registered objects
        /// </summary>
        public static void GenerateSummaryReport()
        {
            Console.WriteLine("\n========================================");
            Console.WriteLine("SmartObject/View/Form Registry Summary");
            Console.WriteLine("========================================");

            Console.WriteLine($"\nTotal SmartObjects: {_smartObjects.Count}");
            Console.WriteLine($"  Main SmartObjects: {_smartObjects.Values.Count(s => s.Type == SmartObjectType.Main)}");
            Console.WriteLine($"  Child SmartObjects: {_smartObjects.Values.Count(s => s.Type == SmartObjectType.Child)}");
            Console.WriteLine($"  Lookup SmartObjects: {_smartObjects.Values.Count(s => s.Type == SmartObjectType.Lookup)}");

            Console.WriteLine($"\nTotal Views: {_views.Count}");
            Console.WriteLine($"  Capture Views: {_views.Values.Count(v => v.Type == ViewType.Capture)}");
            Console.WriteLine($"  Item Views: {_views.Values.Count(v => v.Type == ViewType.Item)}");
            Console.WriteLine($"  List Views: {_views.Values.Count(v => v.Type == ViewType.List)}");

            Console.WriteLine($"\nTotal Forms: {_forms.Count}");

            Console.WriteLine("\n--- SmartObjects and Their Views ---");
            foreach (var smo in _smartObjects.Values.OrderBy(s => s.Name))
            {
                Console.WriteLine($"\n{smo.Name} ({smo.Type})");
                if (!string.IsNullOrEmpty(smo.Guid))
                {
                    Console.WriteLine($"  GUID: {smo.Guid}");
                }

                var views = GetViewsForSmartObject(smo.Name);
                if (views.Count > 0)
                {
                    Console.WriteLine($"  Views ({views.Count}):");
                    foreach (var viewName in views)
                    {
                        var viewInfo = GetViewInfo(viewName);
                        Console.WriteLine($"    - {viewName} ({viewInfo.Type})");
                    }
                }
                else
                {
                    Console.WriteLine("  No views registered");
                }
            }

            Console.WriteLine("\n--- Forms and Their Components ---");
            foreach (var form in _forms.Values)
            {
                Console.WriteLine($"\n{form.Name}");
                Console.WriteLine($"  Views: {string.Join(", ", form.Views)}");
                Console.WriteLine($"  SmartObjects: {string.Join(", ", form.SmartObjects)}");
            }

            Console.WriteLine("\n========================================\n");
        }

        /// <summary>
        /// Get detailed information for a specific SmartObject
        /// </summary>
        public static string GetSmartObjectDetails(string smoName)
        {
            if (!_smartObjects.ContainsKey(smoName))
            {
                return $"SmartObject '{smoName}' not found in registry";
            }

            var smo = _smartObjects[smoName];
            var details = new System.Text.StringBuilder();

            details.AppendLine($"SmartObject: {smo.Name}");
            details.AppendLine($"  Type: {smo.Type}");
            details.AppendLine($"  GUID: {smo.Guid}");
            details.AppendLine($"  Created: {smo.CreatedAt}");

            if (!string.IsNullOrEmpty(smo.ParentSmartObject))
            {
                details.AppendLine($"  Parent: {smo.ParentSmartObject}");
            }

            var childSmos = GetChildSmartObjects(smoName);
            if (childSmos.Count > 0)
            {
                details.AppendLine($"  Child SmartObjects:");
                foreach (var child in childSmos)
                {
                    details.AppendLine($"    - {child}");
                }
            }

            var views = GetViewsForSmartObject(smoName);
            if (views.Count > 0)
            {
                details.AppendLine($"  Associated Views:");
                foreach (var viewName in views)
                {
                    var viewInfo = GetViewInfo(viewName);
                    details.AppendLine($"    - {viewName} (Type: {viewInfo.Type})");
                }
            }

            return details.ToString();
        }

        #endregion

        #region Add Item Button Registration

        /// <summary>
        /// Register an Add Item Button with its view and control information
        /// </summary>
        public static void RegisterAddItemButton(string buttonName, string buttonControlId, string viewName, string viewId, string targetTableName)
        {
            var buttonInfo = new AddItemButtonInfo
            {
                ButtonName = buttonName,
                ButtonControlId = buttonControlId,
                ViewName = viewName,
                ViewId = viewId,
                TargetTableName = targetTableName,
                CreatedAt = DateTime.Now
            };

            _addItemButtons[buttonName] = buttonInfo;

            Console.WriteLine($"[Registry] Registered Add Item Button: {buttonName}");
            Console.WriteLine($"           Control ID: {buttonControlId}");
            Console.WriteLine($"           View: {viewName} (ID: {viewId})");
            Console.WriteLine($"           Target Table: {targetTableName}");
        }

        /// <summary>
        /// Get Add Item Button info by button name
        /// </summary>
        public static AddItemButtonInfo GetAddItemButtonInfo(string buttonName)
        {
            if (_addItemButtons.ContainsKey(buttonName))
            {
                return _addItemButtons[buttonName];
            }
            return null;
        }

        /// <summary>
        /// Get all Add Item Buttons for a specific view
        /// </summary>
        public static List<AddItemButtonInfo> GetAddItemButtonsForView(string viewName)
        {
            return _addItemButtons.Values
                .Where(button => button.ViewName == viewName)
                .ToList();
        }

        /// <summary>
        /// Get all Add Item Buttons for a specific target table
        /// </summary>
        public static List<AddItemButtonInfo> GetAddItemButtonsForTable(string targetTableName)
        {
            return _addItemButtons.Values
                .Where(button => button.TargetTableName == targetTableName)
                .ToList();
        }

        /// <summary>
        /// Check if an Add Item Button exists
        /// </summary>
        public static bool AddItemButtonExists(string buttonName)
        {
            return _addItemButtons.ContainsKey(buttonName);
        }

        /// <summary>
        /// Get total count of Add Item Buttons
        /// </summary>
        public static int GetAddItemButtonCount()
        {
            return _addItemButtons.Count;
        }

        /// <summary>
        /// Get all Add Item Button mappings
        /// </summary>
        public static Dictionary<string, AddItemButtonInfo> GetAllAddItemButtons()
        {
            return new Dictionary<string, AddItemButtonInfo>(_addItemButtons);
        }

        #endregion

        #region Statistics Methods

        /// <summary>
        /// Get total count of SmartObjects
        /// </summary>
        public static int GetSmartObjectCount()
        {
            return _smartObjects.Count;
        }

        /// <summary>
        /// Get count of SmartObjects by type
        /// </summary>
        public static int GetSmartObjectCount(SmartObjectType type)
        {
            return _smartObjects.Values.Count(s => s.Type == type);
        }

        /// <summary>
        /// Get total count of Views
        /// </summary>
        public static int GetViewCount()
        {
            return _views.Count;
        }

        /// <summary>
        /// Get count of Views by type
        /// </summary>
        public static int GetViewCount(ViewType type)
        {
            return _views.Values.Count(v => v.Type == type);
        }

        /// <summary>
        /// Get total count of Forms
        /// </summary>
        public static int GetFormCount()
        {
            return _forms.Count;
        }

        /// <summary>
        /// Get all lookup field mappings
        /// </summary>
        public static Dictionary<string, string> GetAllLookupMappings()
        {
            return new Dictionary<string, string>(_lookupSmartObjects);
        }

        #endregion

        #region Cleanup Methods

        /// <summary>
        /// Clear all registrations
        /// </summary>
        public static void Clear()
        {
            _smartObjects.Clear();
            _views.Clear();
            _forms.Clear();
            _smartObjectToViews.Clear();
            _viewToSmartObject.Clear();
            _formToViews.Clear();
            _formToSmartObjects.Clear();
            _repeatingSectionViews.Clear();
            _lookupSmartObjects.Clear();
            _addItemButtons.Clear();

            Console.WriteLine("[Registry] All registrations cleared");
        }

        /// <summary>
        /// Remove a specific view from the registry
        /// </summary>
        public static void RemoveView(string viewName)
        {
            if (_views.ContainsKey(viewName))
            {
                // Get the SmartObject this view belongs to
                if (_viewToSmartObject.ContainsKey(viewName))
                {
                    string smoName = _viewToSmartObject[viewName];

                    // Remove from SmartObject's view list
                    if (_smartObjectToViews.ContainsKey(smoName))
                    {
                        _smartObjectToViews[smoName].Remove(viewName);
                    }

                    _viewToSmartObject.Remove(viewName);
                }

                // Remove from views collection
                _views.Remove(viewName);

                // Remove from any forms that reference this view
                foreach (var form in _formToViews)
                {
                    form.Value.Remove(viewName);
                }

                Console.WriteLine($"[Registry] Removed view: {viewName}");
            }
        }

        /// <summary>
        /// Remove a specific SmartObject and all its views
        /// </summary>
        public static void RemoveSmartObject(string smoName)
        {
            if (_smartObjects.ContainsKey(smoName))
            {
                // Remove associated views
                if (_smartObjectToViews.ContainsKey(smoName))
                {
                    var views = _smartObjectToViews[smoName];
                    foreach (var viewName in views)
                    {
                        _views.Remove(viewName);
                        _viewToSmartObject.Remove(viewName);
                    }
                    _smartObjectToViews.Remove(smoName);
                }

                // Remove SmartObject
                _smartObjects.Remove(smoName);

                Console.WriteLine($"[Registry] Removed SmartObject and associated views: {smoName}");
            }
        }

        #endregion

        #region Supporting Classes

        public class SmartObjectInfo
        {
            public string Name { get; set; }
            public string Guid { get; set; }
            public SmartObjectType Type { get; set; }
            public string ParentSmartObject { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        public class ViewInfo
        {
            public string Name { get; set; }
            public string SmartObjectName { get; set; }
            public ViewType Type { get; set; }
            public ViewMetadata Metadata { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        public class FormInfo
        {
            public string Name { get; set; }
            public List<string> Views { get; set; }
            public List<string> SmartObjects { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        public class ViewMetadata
        {
            public string InfoPathViewName { get; set; }
            public string RepeatingSectionName { get; set; }
            public int PartNumber { get; set; }
            public bool IsRepeatingSection { get; set; }
            public string ViewTitle { get; set; }
            public string Category { get; set; }
        }

        public enum SmartObjectType
        {
            Main,
            Child,
            Lookup,
            Consolidated
        }

        public enum ViewType
        {
            Capture,
            Item,
            List,
            Display
        }

        public class AddItemButtonInfo
        {
            public string ButtonName { get; set; }
            public string ButtonControlId { get; set; }
            public string ViewName { get; set; }
            public string ViewId { get; set; }
            public string TargetTableName { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        #endregion
    }
}