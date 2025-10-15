using System;
using System.Collections.Generic;
using K2SmartObjectGenerator; // Add this to access ViewGenerator.ControlMapping

namespace K2SmartObjectGenerator.Utilities
{
    public static class ControlMappingService
    {
        private static Dictionary<string, Dictionary<string, ViewGenerator.ControlMapping>> _viewControlMappings
            = new Dictionary<string, Dictionary<string, ViewGenerator.ControlMapping>>();

        public static void RegisterViewControls(string viewName, Dictionary<string, ViewGenerator.ControlMapping> controls)
        {
            _viewControlMappings[viewName] = controls;
            Console.WriteLine($"    ControlMappingService: Registered {controls.Count} controls for view '{viewName}'");

            // Log first few controls for debugging
            int count = 0;
            foreach (var control in controls)
            {
                if (count++ < 3)  // Show first 3 controls
                {
                    Console.WriteLine($"      - Field: {control.Key}, Control: {control.Value.ControlName}, Type: {control.Value.ControlType}");
                }
            }
            if (controls.Count > 3)
            {
                Console.WriteLine($"      ... and {controls.Count - 3} more controls");
            }
        }

        public static Dictionary<string, ViewGenerator.ControlMapping> GetViewControls(string viewName)
        {
            if (_viewControlMappings.ContainsKey(viewName))
            {
                var controls = _viewControlMappings[viewName];
                Console.WriteLine($"    ControlMappingService: Retrieved {controls.Count} controls for view '{viewName}'");
                return controls;
            }

            Console.WriteLine($"    ControlMappingService: No controls found for view '{viewName}'");
            Console.WriteLine($"      Available views: {string.Join(", ", _viewControlMappings.Keys)}");
            return null;
        }

        public static void Clear()
        {
            _viewControlMappings.Clear();
            Console.WriteLine("    ControlMappingService: Cleared all control mappings");
        }

        public static int GetTotalMappedViews()
        {
            return _viewControlMappings.Count;
        }

        public static List<string> GetMappedViewNames()
        {
            return new List<string>(_viewControlMappings.Keys);
        }

        public static void DumpAllMappings()
        {
            Console.WriteLine("\n    === ControlMappingService Full Dump ===");
            Console.WriteLine($"    Total views registered: {_viewControlMappings.Count}");
            foreach (var view in _viewControlMappings)
            {
                Console.WriteLine($"    View: '{view.Key}' has {view.Value.Count} controls");
                foreach (var control in view.Value)
                {
                    Console.WriteLine($"      - {control.Key}: {control.Value.ControlName} ({control.Value.ControlType})");
                }
            }
            Console.WriteLine("    === End Dump ===\n");
        }
    }
}