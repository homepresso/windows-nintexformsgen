using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using K2SmartObjectGenerator.Config;
using K2SmartObjectGenerator.Utilities;

namespace K2SmartObjectGenerator
{
    /// <summary>
    /// Generates K2 Forms from JSON form definitions
    /// </summary>
    public class FormGenerator
    {
        private readonly ServerConnectionManager _connectionManager;
        private readonly string _formTheme;
        private readonly SmartObjectGenerator _smartObjectGenerator;
        private readonly GeneratorConfiguration _config;

        public FormGenerator(
            ServerConnectionManager connectionManager,
            string formTheme,
            SmartObjectGenerator smartObjectGenerator,
            GeneratorConfiguration config)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _formTheme = formTheme ?? "Default";
            _smartObjectGenerator = smartObjectGenerator ?? throw new ArgumentNullException(nameof(smartObjectGenerator));
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// Generates Forms from JSON form definition using the provided view titles
        /// </summary>
        public void GenerateFormsFromJson(string jsonContent, Dictionary<string, string> viewTitles)
        {
            if (string.IsNullOrWhiteSpace(jsonContent))
                throw new ArgumentException("JSON content cannot be null or empty", nameof(jsonContent));

            if (viewTitles == null)
                throw new ArgumentNullException(nameof(viewTitles));

            var formData = JObject.Parse(jsonContent);

            foreach (var form in formData.Properties())
            {
                var formName = form.Name.Replace(" ", "_");
                var formDefinition = form.Value as JObject;

                if (formDefinition == null)
                    continue;

                CreateForm(formName, formDefinition, viewTitles);
            }
        }

        /// <summary>
        /// Attempts to cleanup existing forms
        /// </summary>
        public void TryCleanupExistingForms(string jsonContent)
        {
            try
            {
                var formData = JObject.Parse(jsonContent);
                foreach (var form in formData.Properties())
                {
                    var formName = form.Name.Replace(" ", "_");
                    DeleteFormIfExists(formName);
                }
            }
            catch
            {
                // Suppress errors during cleanup
            }
        }

        private void CreateForm(string formName, JObject formDefinition, Dictionary<string, string> viewTitles)
        {
            var formDefData = formDefinition["FormDefinition"] as JObject;
            if (formDefData == null)
                return;

            var displayName = formName.Replace("_", " ");

            // Get relevant views for this form
            var formViews = viewTitles.Keys
                .Where(viewName => viewName.StartsWith(formName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (formViews.Count == 0)
                return;

            // Note: Actual K2 form creation would use K2 Forms Authoring API
            // This is a simplified stub that creates form metadata

            // Get all SmartObjects associated with this form
            var allSmartObjects = new List<string> { formName };
            allSmartObjects.AddRange(SmartObjectViewRegistry.GetChildSmartObjects(formName));

            var lookupSmo = $"{formName}_Lookups";
            if (SmartObjectViewRegistry.SmartObjectExists(lookupSmo))
            {
                allSmartObjects.Add(lookupSmo);
            }

            // Register the form
            SmartObjectViewRegistry.RegisterForm(formName, formViews, allSmartObjects);
        }

        private void DeleteFormIfExists(string formName)
        {
            try
            {
                var mgmtServer = _connectionManager.ManagementServer;
                if (mgmtServer?.Connection == null || !mgmtServer.Connection.IsConnected)
                    return;

                // Note: Actual K2 form deletion would use K2 Forms Management API
                // This is a stub for now
            }
            catch
            {
                // Suppress errors during cleanup
            }
        }
    }
}
