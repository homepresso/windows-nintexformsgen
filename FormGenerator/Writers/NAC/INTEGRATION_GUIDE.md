# NWConverter Integration Guide

## Table of Contents

1. [Overview](#overview)
2. [Prerequisites](#prerequisites)
3. [Integration Approaches](#integration-approaches)
4. [Quick Start](#quick-start)
5. [Step-by-Step Integration](#step-by-step-integration)
6. [API Reference](#api-reference)
7. [Model Structures](#model-structures)
8. [Control Type Mappings](#control-type-mappings)
9. [Code Examples](#code-examples)
10. [Error Handling](#error-handling)
11. [Advanced Usage](#advanced-usage)
12. [Troubleshooting](#troubleshooting)

---

## Overview

**NWConverter** is a C# application that converts legacy form definitions to Nintex Workflow Cloud format. The core conversion logic is contained in reusable service classes that can be easily integrated into other C# applications.

### What It Does

- Converts legacy form JSON structures to Nintex `form-definition.json` format
- Maps control types (TextField → textbox, DatePicker → datetime, etc.)
- Handles multi-view forms with automatic view processing
- Generates repeating sections and group controls
- Creates translation structures for multi-language support
- Manages variable ID generation with the `se_` prefix convention
- Processes grid-based layouts and converts to Nintex row/column structure

### Key Benefits

- **Reusable Core Logic**: FormConverter service can be used standalone
- **Well-Defined Models**: Strong typing with comprehensive data models
- **Flexible Output**: Support for single forms or batch processing
- **View-Specific Conversion**: Generate separate forms for each view

---

## Prerequisites

### Required Components

1. **.NET Framework**
   - .NET 6.0 or higher
   - For library-only integration, WPF support is not required

2. **NuGet Packages**
   ```xml
   <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
   ```

3. **Source Files** (from NWConverter project)
   - `Services/FormConverter.cs` - Core conversion engine
   - `Models/FormDefinition.cs` - Nintex output models
   - `Models/SourceForm.cs` - Source input models

### Optional Dependencies

- **MaterialDesignThemes** (4.9.0) - Only needed if integrating UI components
- **MaterialDesignColors** (2.1.4) - Only needed if integrating UI components

---

## Integration Approaches

### Approach 1: Copy Service Classes (Recommended)

**Best for**: Full control and customization

1. Copy the three core files into your project:
   - `Services/FormConverter.cs`
   - `Models/FormDefinition.cs`
   - `Models/SourceForm.cs`

2. Install Newtonsoft.Json NuGet package

3. Use the FormConverter service directly in your application

**Pros**:
- Full control over the code
- Easy to customize mappings and logic
- No external assembly dependencies

**Cons**:
- Need to manually sync updates
- Code duplication

### Approach 2: Build as Class Library

**Best for**: Multiple consuming applications

1. Create a new Class Library project (e.g., `NWConverter.Core`)
2. Move service and model classes to the library
3. Remove WPF dependencies (MainWindow, PreviewWindow, App)
4. Reference the library in your application

**Pros**:
- Reusable across multiple projects
- Centralized updates
- Cleaner separation of concerns

**Cons**:
- Requires refactoring the existing project
- Additional project to maintain

### Approach 3: Embed as NuGet Package

**Best for**: Enterprise distribution

1. Build NWConverter.Core as a class library
2. Package as NuGet package
3. Publish to internal NuGet feed
4. Reference via package manager

**Pros**:
- Version management
- Easy distribution
- Professional deployment

**Cons**:
- Requires NuGet infrastructure
- Most complex setup

---

## Quick Start

### Minimal Integration (5 Minutes)

```csharp
using NWConverter.Services;
using NWConverter.Models;
using Newtonsoft.Json;

class Program
{
    static void Main()
    {
        // 1. Create converter
        var converter = new FormConverter();

        // 2. Load source form
        var sourceJson = File.ReadAllText("legacy-form.json");
        var sourceForm = JsonConvert.DeserializeObject<SourceForm>(sourceJson);

        // 3. Convert
        var nintexForm = converter.ConvertForm(sourceForm);

        // 4. Save
        var outputJson = JsonConvert.SerializeObject(nintexForm, Formatting.Indented);
        File.WriteAllText("nintex-form.json", outputJson);

        Console.WriteLine("Conversion complete!");
    }
}
```

---

## Step-by-Step Integration

### Step 1: Add Files to Your Project

1. Create folders in your project:
   ```
   YourProject/
   ├── Services/
   └── Models/
   ```

2. Copy these files:
   ```
   NWConverter/Services/FormConverter.cs     → YourProject/Services/
   NWConverter/Models/FormDefinition.cs      → YourProject/Models/
   NWConverter/Models/SourceForm.cs          → YourProject/Models/
   ```

3. Update namespaces if needed:
   ```csharp
   // Change from:
   namespace NWConverter.Services;

   // To:
   namespace YourProject.Services;
   ```

### Step 2: Install Dependencies

Using Package Manager Console:
```powershell
Install-Package Newtonsoft.Json -Version 13.0.3
```

Or using .NET CLI:
```bash
dotnet add package Newtonsoft.Json --version 13.0.3
```

Or add to your `.csproj`:
```xml
<ItemGroup>
  <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
</ItemGroup>
```

### Step 3: Create a Conversion Service

```csharp
using YourProject.Services;
using YourProject.Models;
using Newtonsoft.Json;

namespace YourProject.Services
{
    public class FormMigrationService
    {
        private readonly FormConverter _converter;
        private readonly string _outputDirectory;

        public FormMigrationService(string outputDirectory)
        {
            _converter = new FormConverter();
            _outputDirectory = outputDirectory;
            Directory.CreateDirectory(_outputDirectory);
        }

        /// <summary>
        /// Converts a single form file to Nintex format
        /// </summary>
        public string ConvertSingleForm(string sourceFilePath)
        {
            try
            {
                // Read source JSON
                var json = File.ReadAllText(sourceFilePath);
                var sourceForm = JsonConvert.DeserializeObject<SourceForm>(json);

                if (sourceForm?.FormDefinition == null)
                {
                    throw new Exception("Invalid form format");
                }

                // Set filename if not present
                if (string.IsNullOrEmpty(sourceForm.FileName))
                {
                    sourceForm.FileName = Path.GetFileNameWithoutExtension(sourceFilePath);
                }

                // Convert
                var nintexForm = _converter.ConvertForm(sourceForm);

                // Generate output filename
                var outputFileName = $"{sourceForm.FileName}_nintex.json";
                var outputPath = Path.Combine(_outputDirectory, outputFileName);

                // Serialize and save
                var outputJson = JsonConvert.SerializeObject(nintexForm, Formatting.Indented);
                File.WriteAllText(outputPath, outputJson);

                return outputPath;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to convert {sourceFilePath}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Converts all forms in a directory
        /// </summary>
        public Dictionary<string, string> ConvertBatch(string sourceDirectory)
        {
            var results = new Dictionary<string, string>();
            var jsonFiles = Directory.GetFiles(sourceDirectory, "*.json", SearchOption.AllDirectories);

            foreach (var file in jsonFiles)
            {
                try
                {
                    var outputPath = ConvertSingleForm(file);
                    results[file] = outputPath;
                }
                catch (Exception ex)
                {
                    results[file] = $"ERROR: {ex.Message}";
                }
            }

            return results;
        }

        /// <summary>
        /// Converts a specific view from a form
        /// </summary>
        public string ConvertSpecificView(string sourceFilePath, string viewName)
        {
            var json = File.ReadAllText(sourceFilePath);
            var sourceForm = JsonConvert.DeserializeObject<SourceForm>(json);

            if (sourceForm?.FormDefinition?.Views == null)
            {
                throw new Exception("No views found in source form");
            }

            var targetView = sourceForm.FormDefinition.Views
                .FirstOrDefault(v => v.ViewName.Equals(viewName, StringComparison.OrdinalIgnoreCase));

            if (targetView == null)
            {
                throw new Exception($"View '{viewName}' not found");
            }

            var formName = sourceForm.FileName ?? Path.GetFileNameWithoutExtension(sourceFilePath);
            var nintexForm = _converter.ConvertFormForView(sourceForm, targetView, formName);

            var outputFileName = $"{formName}_{viewName}_nintex.json";
            var outputPath = Path.Combine(_outputDirectory, outputFileName);

            var outputJson = JsonConvert.SerializeObject(nintexForm, Formatting.Indented);
            File.WriteAllText(outputPath, outputJson);

            return outputPath;
        }
    }
}
```

### Step 4: Use in Your Application

```csharp
class Program
{
    static void Main()
    {
        var service = new FormMigrationService(@"C:\ConvertedForms");

        // Single file conversion
        var outputPath = service.ConvertSingleForm(@"C:\SourceForms\TravelRequest.json");
        Console.WriteLine($"Converted to: {outputPath}");

        // Batch conversion
        var results = service.ConvertBatch(@"C:\SourceForms");
        foreach (var kvp in results)
        {
            Console.WriteLine($"{kvp.Key} → {kvp.Value}");
        }
    }
}
```

---

## API Reference

### FormConverter Class

**Namespace**: `NWConverter.Services`

#### Constructor

```csharp
public FormConverter()
```

Initializes a new instance with default control mappings, widget mappings, and data type mappings.

#### Methods

##### ConvertForm

```csharp
public FormDefinition ConvertForm(SourceForm sourceForm)
```

Converts an entire source form to Nintex format.

**Parameters**:
- `sourceForm` - The source form object to convert

**Returns**: `FormDefinition` - Complete Nintex form definition

**Throws**:
- `Exception` - If form structure is invalid or conversion fails

**Example**:
```csharp
var converter = new FormConverter();
var nintexForm = converter.ConvertForm(sourceForm);
```

##### ConvertFormForView

```csharp
public FormDefinition ConvertFormForView(
    SourceForm sourceForm,
    SourceView view,
    string formDisplayName
)
```

Converts a specific view from a source form.

**Parameters**:
- `sourceForm` - The complete source form
- `view` - The specific view to convert
- `formDisplayName` - Display name for the generated form

**Returns**: `FormDefinition` - Nintex form definition for the specific view

**Example**:
```csharp
var firstView = sourceForm.FormDefinition.Views[0];
var nintexForm = converter.ConvertFormForView(sourceForm, firstView, "Travel Request");
```

---

## Model Structures

### SourceForm (Input Format)

```csharp
public class SourceForm
{
    public string? FileName { get; set; }                    // Form file name
    public SourceFormDefinition? FormDefinition { get; set; } // Main definition
}

public class SourceFormDefinition
{
    public List<SourceView> Views { get; set; }              // Form views
    public List<SourceDataItem> Data { get; set; }           // Data definitions
    public List<SourceDynamicSection> DynamicSections { get; set; }
    public SourceMetadata? Metadata { get; set; }
}

public class SourceView
{
    public string ViewName { get; set; }                     // e.g., "view1.xsl"
    public List<SourceControl> Controls { get; set; }        // Controls in view
    public List<SourceSection> Sections { get; set; }        // Section groupings
}

public class SourceControl
{
    public string Name { get; set; }                         // Control name
    public string Type { get; set; }                         // "TextField", "DatePicker", etc.
    public string? Label { get; set; }                       // Display label
    public string? Binding { get; set; }                     // Data binding
    public string? GridPosition { get; set; }                // "1A", "2B", etc.
    public bool IsReadOnly { get; set; }                     // Read-only flag
    public bool IsRequired { get; set; }                     // Required flag
    public List<DataOption> DataOptions { get; set; }        // Choice options
    public RepeatingSectionInfo? RepeatingSectionInfo { get; set; }
}

public class SourceDataItem
{
    public string ColumnName { get; set; }                   // Variable name
    public string DisplayName { get; set; }                  // Display name
    public string Type { get; set; }                         // Data type
    public List<ValidValue> ValidValues { get; set; }        // Choice values
    public bool IsRepeating { get; set; }                    // Repeating field
}
```

### FormDefinition (Output Format)

```csharp
public class FormDefinition
{
    public string Id { get; set; }                           // Form GUID
    public string Name { get; set; }                         // Form name
    public int Version { get; set; }                         // Always 26
    public string FormType { get; set; }                     // "startform"
    public Theme Theme { get; set; }                         // Theme settings
    public PageSettings PageSettings { get; set; }           // Page configuration
    public TranslationSettings TranslationSettings { get; set; }
    public Contract Contract { get; set; }                   // Version contract
    public List<Row> Rows { get; set; }                      // Form rows
    public Dictionary<string, Dictionary<string, string>> Translations { get; set; }
}

public class Row
{
    public string Id { get; set; }                           // Row ID
    public string PageName { get; set; }                     // Parent page
    public List<Control> Controls { get; set; }              // Controls in row
    public List<string> ControlsIds { get; set; }            // Control ID list
}

public class Control
{
    public string Id { get; set; }                           // Control ID
    public string Widget { get; set; }                       // "textbox", "datetime", etc.
    public string DataType { get; set; }                     // "string", "date", etc.
    public ControlProperties Properties { get; set; }        // All properties
}

public class ControlProperties
{
    public string Name { get; set; }                         // Control name
    public string Title { get; set; }                        // Translation key
    public bool? ReadOnly { get; set; }                      // Read-only state
    public bool? Required { get; set; }                      // Required state
    public string? ConnectedVariableId { get; set; }         // Variable binding
    public List<ChoiceOption>? Options { get; set; }         // For choice controls
    // ... many more properties
}
```

---

## Control Type Mappings

### Control Types (Source → Nintex Widget)

| Source Type | Nintex Widget | Data Type | Description |
|-------------|---------------|-----------|-------------|
| TextField | textbox | string | Single-line text input |
| TextArea | multilinetext | string | Multi-line text area |
| RichText | multilinetext | string | Rich text editor |
| DatePicker | datetime | date | Date picker control |
| DropDown | choice | string | Dropdown list |
| Choice | choice | string | Radio buttons or checkboxes |
| CheckBox | boolean | boolean | Single checkbox |
| Number | number | number | Numeric input |
| Currency | currency | number | Currency input |
| Email | email | string | Email address field |
| FileUpload | file-upload | string | File upload control |
| Signature | signature | string | Signature pad |
| PeoplePicker | people-picker-core | string | User/group picker |
| RepeatingTable | repeating-section | collection | Repeating data section |
| Group | group-control | object | Control grouping |
| Image | image | string | Image display |
| Barcode | barcode | string | Barcode scanner |
| Geolocation | geolocation | string | GPS location |
| DataLookup | data-lookup | string | External data lookup |
| Button | button | string | Action button |
| Label | richtext-label | string | Static label |
| Space | space | string | Layout spacer |

### Data Type Mappings

| Source Data Type | Nintex Data Type |
|------------------|------------------|
| Text | string |
| WholeNumber | number |
| Decimal | number |
| Date | date |
| DateTime | datetime |
| Boolean | boolean |
| YesNo | boolean |
| Choice | string |
| MultiChoice | collection |
| Hyperlink | string |
| Email | string |
| User | string |
| File | string |
| Calculated | string |

---

## Code Examples

### Example 1: Simple Console Application

```csharp
using System;
using System.IO;
using NWConverter.Services;
using NWConverter.Models;
using Newtonsoft.Json;

namespace FormConverterConsole
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: FormConverter <input-file> <output-file>");
                return;
            }

            var inputPath = args[0];
            var outputPath = args[1];

            try
            {
                // Load source form
                var json = File.ReadAllText(inputPath);
                var sourceForm = JsonConvert.DeserializeObject<SourceForm>(json);

                // Convert
                var converter = new FormConverter();
                var nintexForm = converter.ConvertForm(sourceForm);

                // Save
                var outputJson = JsonConvert.SerializeObject(nintexForm, Formatting.Indented);
                File.WriteAllText(outputPath, outputJson);

                Console.WriteLine($"✓ Successfully converted {inputPath} to {outputPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Error: {ex.Message}");
                Environment.Exit(1);
            }
        }
    }
}
```

### Example 2: ASP.NET Core Web API

```csharp
using Microsoft.AspNetCore.Mvc;
using NWConverter.Services;
using NWConverter.Models;
using Newtonsoft.Json;

[ApiController]
[Route("api/[controller]")]
public class FormConversionController : ControllerBase
{
    private readonly FormConverter _converter;

    public FormConversionController()
    {
        _converter = new FormConverter();
    }

    [HttpPost("convert")]
    public IActionResult ConvertForm([FromBody] SourceForm sourceForm)
    {
        try
        {
            var nintexForm = _converter.ConvertForm(sourceForm);
            return Ok(nintexForm);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("convert-file")]
    public async Task<IActionResult> ConvertFile(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file uploaded");

        try
        {
            using var reader = new StreamReader(file.OpenReadStream());
            var json = await reader.ReadToEndAsync();
            var sourceForm = JsonConvert.DeserializeObject<SourceForm>(json);

            var nintexForm = _converter.ConvertForm(sourceForm);

            return File(
                System.Text.Encoding.UTF8.GetBytes(
                    JsonConvert.SerializeObject(nintexForm, Formatting.Indented)
                ),
                "application/json",
                $"{sourceForm.FileName}_nintex.json"
            );
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("views/{formId}")]
    public IActionResult GetFormViews(string formId)
    {
        // Implementation to retrieve and list available views
        // This would depend on your data storage mechanism
        return Ok();
    }
}
```

### Example 3: Windows Service

```csharp
using System.ServiceProcess;
using NWConverter.Services;
using NWConverter.Models;
using Newtonsoft.Json;

public class FormConversionService : ServiceBase
{
    private FileSystemWatcher _watcher;
    private FormConverter _converter;
    private string _inputFolder = @"C:\FormConversion\Input";
    private string _outputFolder = @"C:\FormConversion\Output";

    protected override void OnStart(string[] args)
    {
        _converter = new FormConverter();

        _watcher = new FileSystemWatcher(_inputFolder, "*.json");
        _watcher.Created += OnNewFileDetected;
        _watcher.EnableRaisingEvents = true;

        Directory.CreateDirectory(_inputFolder);
        Directory.CreateDirectory(_outputFolder);
    }

    private void OnNewFileDetected(object sender, FileSystemEventArgs e)
    {
        try
        {
            // Wait for file to be fully written
            Thread.Sleep(1000);

            var json = File.ReadAllText(e.FullPath);
            var sourceForm = JsonConvert.DeserializeObject<SourceForm>(json);

            var nintexForm = _converter.ConvertForm(sourceForm);

            var outputFileName = Path.GetFileNameWithoutExtension(e.Name) + "_nintex.json";
            var outputPath = Path.Combine(_outputFolder, outputFileName);

            var outputJson = JsonConvert.SerializeObject(nintexForm, Formatting.Indented);
            File.WriteAllText(outputPath, outputJson);

            // Archive processed file
            var archiveFolder = Path.Combine(_inputFolder, "Processed");
            Directory.CreateDirectory(archiveFolder);
            File.Move(e.FullPath, Path.Combine(archiveFolder, Path.GetFileName(e.Name)));

            EventLog.WriteEntry($"Converted {e.Name} successfully", EventLogEntryType.Information);
        }
        catch (Exception ex)
        {
            EventLog.WriteEntry($"Error converting {e.Name}: {ex.Message}", EventLogEntryType.Error);
        }
    }

    protected override void OnStop()
    {
        _watcher?.Dispose();
    }
}
```

### Example 4: Azure Function

```csharp
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using NWConverter.Services;
using NWConverter.Models;
using Newtonsoft.Json;

public class FormConversionFunction
{
    private readonly FormConverter _converter;

    public FormConversionFunction()
    {
        _converter = new FormConverter();
    }

    [Function("ConvertForm")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        try
        {
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var sourceForm = JsonConvert.DeserializeObject<SourceForm>(requestBody);

            var nintexForm = _converter.ConvertForm(sourceForm);
            var responseJson = JsonConvert.SerializeObject(nintexForm, Formatting.Indented);

            var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json");
            await response.WriteStringAsync(responseJson);

            return response;
        }
        catch (Exception ex)
        {
            var errorResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
            await errorResponse.WriteStringAsync(JsonConvert.SerializeObject(new { error = ex.Message }));
            return errorResponse;
        }
    }
}
```

### Example 5: Batch Processing with Progress Reporting

```csharp
using NWConverter.Services;
using NWConverter.Models;
using Newtonsoft.Json;

public class BatchFormConverter
{
    public event EventHandler<ConversionProgressEventArgs> ProgressChanged;

    private readonly FormConverter _converter;

    public BatchFormConverter()
    {
        _converter = new FormConverter();
    }

    public async Task<BatchConversionResult> ConvertBatchAsync(
        string[] inputFiles,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        var result = new BatchConversionResult();
        var totalFiles = inputFiles.Length;

        for (int i = 0; i < totalFiles; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var file = inputFiles[i];

            try
            {
                var json = await File.ReadAllTextAsync(file, cancellationToken);
                var sourceForm = JsonConvert.DeserializeObject<SourceForm>(json);

                var nintexForm = _converter.ConvertForm(sourceForm);

                var outputFileName = $"{Path.GetFileNameWithoutExtension(file)}_nintex.json";
                var outputPath = Path.Combine(outputDirectory, outputFileName);

                var outputJson = JsonConvert.SerializeObject(nintexForm, Formatting.Indented);
                await File.WriteAllTextAsync(outputPath, outputJson, cancellationToken);

                result.SuccessfulConversions++;
                result.ConvertedFiles.Add(outputPath);

                OnProgressChanged(new ConversionProgressEventArgs
                {
                    CurrentFile = file,
                    ProcessedCount = i + 1,
                    TotalCount = totalFiles,
                    PercentComplete = (double)(i + 1) / totalFiles * 100
                });
            }
            catch (Exception ex)
            {
                result.FailedConversions++;
                result.Errors.Add(new ConversionError
                {
                    FileName = file,
                    ErrorMessage = ex.Message
                });
            }
        }

        return result;
    }

    protected virtual void OnProgressChanged(ConversionProgressEventArgs e)
    {
        ProgressChanged?.Invoke(this, e);
    }
}

public class ConversionProgressEventArgs : EventArgs
{
    public string CurrentFile { get; set; }
    public int ProcessedCount { get; set; }
    public int TotalCount { get; set; }
    public double PercentComplete { get; set; }
}

public class BatchConversionResult
{
    public int SuccessfulConversions { get; set; }
    public int FailedConversions { get; set; }
    public List<string> ConvertedFiles { get; set; } = new();
    public List<ConversionError> Errors { get; set; } = new();
}

public class ConversionError
{
    public string FileName { get; set; }
    public string ErrorMessage { get; set; }
}

// Usage
var converter = new BatchFormConverter();
converter.ProgressChanged += (sender, e) =>
{
    Console.WriteLine($"Progress: {e.PercentComplete:F1}% ({e.ProcessedCount}/{e.TotalCount})");
};

var files = Directory.GetFiles(@"C:\Forms", "*.json");
var result = await converter.ConvertBatchAsync(files, @"C:\Output");

Console.WriteLine($"Success: {result.SuccessfulConversions}, Failed: {result.FailedConversions}");
```

---

## Error Handling

### Common Exceptions

#### 1. Invalid Form Structure

```csharp
try
{
    var nintexForm = converter.ConvertForm(sourceForm);
}
catch (Exception ex) when (ex.Message.Contains("Invalid form format"))
{
    // Handle missing or null FormDefinition
    Console.WriteLine("Source form is missing required FormDefinition structure");
}
```

#### 2. JSON Deserialization Errors

```csharp
try
{
    var sourceForm = JsonConvert.DeserializeObject<SourceForm>(json);
}
catch (JsonException ex)
{
    Console.WriteLine($"Invalid JSON format: {ex.Message}");
    // Log the problematic JSON for debugging
}
```

#### 3. File I/O Errors

```csharp
try
{
    var json = File.ReadAllText(filePath);
}
catch (FileNotFoundException)
{
    Console.WriteLine($"File not found: {filePath}");
}
catch (UnauthorizedAccessException)
{
    Console.WriteLine($"Access denied: {filePath}");
}
catch (IOException ex)
{
    Console.WriteLine($"I/O error: {ex.Message}");
}
```

### Validation Before Conversion

```csharp
public bool ValidateSourceForm(SourceForm sourceForm, out string errorMessage)
{
    errorMessage = null;

    if (sourceForm == null)
    {
        errorMessage = "Source form is null";
        return false;
    }

    if (sourceForm.FormDefinition == null)
    {
        errorMessage = "FormDefinition is missing";
        return false;
    }

    if (sourceForm.FormDefinition.Views == null || !sourceForm.FormDefinition.Views.Any())
    {
        errorMessage = "No views found in form definition";
        return false;
    }

    var hasControls = sourceForm.FormDefinition.Views
        .Any(v => v.Controls != null && v.Controls.Any());

    var hasData = sourceForm.FormDefinition.Data != null &&
                  sourceForm.FormDefinition.Data.Any();

    if (!hasControls && !hasData)
    {
        errorMessage = "Form has no controls or data definitions";
        return false;
    }

    return true;
}

// Usage
if (!ValidateSourceForm(sourceForm, out var error))
{
    Console.WriteLine($"Validation failed: {error}");
    return;
}

var nintexForm = converter.ConvertForm(sourceForm);
```

### Comprehensive Error Handling Example

```csharp
public ConversionResult ConvertWithErrorHandling(string inputPath, string outputPath)
{
    var result = new ConversionResult { InputFile = inputPath };

    try
    {
        // Step 1: Read file
        if (!File.Exists(inputPath))
        {
            result.Success = false;
            result.ErrorMessage = "Input file does not exist";
            return result;
        }

        var json = File.ReadAllText(inputPath);

        // Step 2: Deserialize
        SourceForm sourceForm;
        try
        {
            sourceForm = JsonConvert.DeserializeObject<SourceForm>(json);
        }
        catch (JsonException ex)
        {
            result.Success = false;
            result.ErrorMessage = $"JSON parsing error: {ex.Message}";
            return result;
        }

        // Step 3: Validate
        if (!ValidateSourceForm(sourceForm, out var validationError))
        {
            result.Success = false;
            result.ErrorMessage = $"Validation failed: {validationError}";
            return result;
        }

        // Step 4: Convert
        var converter = new FormConverter();
        FormDefinition nintexForm;

        try
        {
            nintexForm = converter.ConvertForm(sourceForm);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = $"Conversion failed: {ex.Message}";
            result.StackTrace = ex.StackTrace;
            return result;
        }

        // Step 5: Serialize
        string outputJson;
        try
        {
            outputJson = JsonConvert.SerializeObject(nintexForm, Formatting.Indented);
        }
        catch (JsonException ex)
        {
            result.Success = false;
            result.ErrorMessage = $"JSON serialization error: {ex.Message}";
            return result;
        }

        // Step 6: Write output
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            File.WriteAllText(outputPath, outputJson);
        }
        catch (IOException ex)
        {
            result.Success = false;
            result.ErrorMessage = $"File write error: {ex.Message}";
            return result;
        }

        result.Success = true;
        result.OutputFile = outputPath;
        result.ControlsConverted = nintexForm.Rows.Sum(r => r.Controls.Count);

        return result;
    }
    catch (Exception ex)
    {
        result.Success = false;
        result.ErrorMessage = $"Unexpected error: {ex.Message}";
        result.StackTrace = ex.StackTrace;
        return result;
    }
}

public class ConversionResult
{
    public bool Success { get; set; }
    public string InputFile { get; set; }
    public string OutputFile { get; set; }
    public string ErrorMessage { get; set; }
    public string StackTrace { get; set; }
    public int ControlsConverted { get; set; }
}
```

---

## Advanced Usage

### Custom Control Mappings

The FormConverter uses internal dictionaries for control mappings. To customize these mappings, you can modify the FormConverter class:

```csharp
// In FormConverter.cs constructor, modify mappings:

public FormConverter()
{
    _controlTypeMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["TextField"] = "textbox",
        ["DatePicker"] = "datetime",
        // Add your custom mappings:
        ["CustomWidget"] = "custom-nintex-widget",
        ["SpecialDatePicker"] = "datetime-range",
    };

    // Similarly for widget and data type mappings
}
```

### Extending the Converter

Create a derived class to add custom behavior:

```csharp
public class ExtendedFormConverter : FormConverter
{
    // Add logging
    public FormDefinition ConvertFormWithLogging(SourceForm sourceForm, ILogger logger)
    {
        logger.LogInformation($"Starting conversion for {sourceForm.FileName}");

        var result = base.ConvertForm(sourceForm);

        logger.LogInformation($"Converted {result.Rows.Count} rows with " +
                            $"{result.Rows.Sum(r => r.Controls.Count)} controls");

        return result;
    }

    // Add custom post-processing
    public FormDefinition ConvertFormWithCustomRules(SourceForm sourceForm)
    {
        var result = base.ConvertForm(sourceForm);

        // Apply custom business rules
        ApplyCustomValidation(result);
        AddCustomMetadata(result);

        return result;
    }

    private void ApplyCustomValidation(FormDefinition form)
    {
        // Add custom validation rules
        foreach (var row in form.Rows)
        {
            foreach (var control in row.Controls)
            {
                if (control.Widget == "email")
                {
                    control.Properties.Pattern = @"^[a-zA-Z0-9._%+-]+@company\.com$";
                }
            }
        }
    }

    private void AddCustomMetadata(FormDefinition form)
    {
        // Add custom metadata
        form.Name = $"[MIGRATED] {form.Name}";
    }
}
```

### Multi-View Processing

```csharp
public class MultiViewConverter
{
    private readonly FormConverter _converter = new FormConverter();

    public Dictionary<string, FormDefinition> ConvertAllViews(SourceForm sourceForm)
    {
        var results = new Dictionary<string, FormDefinition>();

        if (sourceForm?.FormDefinition?.Views == null)
            return results;

        foreach (var view in sourceForm.FormDefinition.Views)
        {
            // Skip summary/filter views
            if (view.ViewName?.ToLower().Contains("summary") == true ||
                view.ViewName?.ToLower().Contains("filter") == true)
            {
                continue;
            }

            var formName = sourceForm.FileName ?? "UnknownForm";
            var nintexForm = _converter.ConvertFormForView(sourceForm, view, formName);

            results[view.ViewName] = nintexForm;
        }

        return results;
    }

    public void SaveAllViews(SourceForm sourceForm, string outputDirectory)
    {
        var views = ConvertAllViews(sourceForm);

        foreach (var kvp in views)
        {
            var fileName = $"{sourceForm.FileName}_{kvp.Key}_nintex.json";
            var outputPath = Path.Combine(outputDirectory, fileName);

            var json = JsonConvert.SerializeObject(kvp.Value, Formatting.Indented);
            File.WriteAllText(outputPath, json);
        }
    }
}
```

### Performance Optimization for Large Batches

```csharp
public class ParallelFormConverter
{
    public async Task<List<ConversionResult>> ConvertBatchParallel(
        string[] inputFiles,
        string outputDirectory,
        int maxDegreeOfParallelism = 4)
    {
        var results = new ConcurrentBag<ConversionResult>();

        await Parallel.ForEachAsync(
            inputFiles,
            new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism },
            async (file, ct) =>
            {
                var converter = new FormConverter(); // Thread-safe instance
                var result = new ConversionResult { InputFile = file };

                try
                {
                    var json = await File.ReadAllTextAsync(file, ct);
                    var sourceForm = JsonConvert.DeserializeObject<SourceForm>(json);
                    var nintexForm = converter.ConvertForm(sourceForm);

                    var outputFileName = $"{Path.GetFileNameWithoutExtension(file)}_nintex.json";
                    var outputPath = Path.Combine(outputDirectory, outputFileName);

                    var outputJson = JsonConvert.SerializeObject(nintexForm, Formatting.Indented);
                    await File.WriteAllTextAsync(outputPath, outputJson, ct);

                    result.Success = true;
                    result.OutputFile = outputPath;
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.ErrorMessage = ex.Message;
                }

                results.Add(result);
            }
        );

        return results.ToList();
    }
}
```

### Conversion with Metadata Tracking

```csharp
public class ConversionMetadata
{
    public string SourceFile { get; set; }
    public string OutputFile { get; set; }
    public DateTime ConversionDate { get; set; }
    public int SourceControlCount { get; set; }
    public int OutputControlCount { get; set; }
    public int ViewsProcessed { get; set; }
    public TimeSpan ConversionDuration { get; set; }
    public string FormVersion { get; set; }
}

public class TrackedFormConverter
{
    private readonly FormConverter _converter = new FormConverter();

    public (FormDefinition Form, ConversionMetadata Metadata) ConvertWithMetadata(
        string sourceFilePath,
        string outputDirectory)
    {
        var stopwatch = Stopwatch.StartNew();

        var json = File.ReadAllText(sourceFilePath);
        var sourceForm = JsonConvert.DeserializeObject<SourceForm>(json);

        var nintexForm = _converter.ConvertForm(sourceForm);

        stopwatch.Stop();

        var metadata = new ConversionMetadata
        {
            SourceFile = sourceFilePath,
            OutputFile = Path.Combine(outputDirectory, $"{sourceForm.FileName}_nintex.json"),
            ConversionDate = DateTime.UtcNow,
            SourceControlCount = sourceForm.FormDefinition.Views
                .Sum(v => v.Controls?.Count ?? 0),
            OutputControlCount = nintexForm.Rows.Sum(r => r.Controls.Count),
            ViewsProcessed = sourceForm.FormDefinition.Views.Count,
            ConversionDuration = stopwatch.Elapsed,
            FormVersion = nintexForm.Version.ToString()
        };

        return (nintexForm, metadata);
    }

    public void SaveWithMetadata(string sourceFilePath, string outputDirectory)
    {
        var (form, metadata) = ConvertWithMetadata(sourceFilePath, outputDirectory);

        // Save form
        var formJson = JsonConvert.SerializeObject(form, Formatting.Indented);
        File.WriteAllText(metadata.OutputFile, formJson);

        // Save metadata
        var metadataPath = Path.ChangeExtension(metadata.OutputFile, ".metadata.json");
        var metadataJson = JsonConvert.SerializeObject(metadata, Formatting.Indented);
        File.WriteAllText(metadataPath, metadataJson);
    }
}
```

---

## Troubleshooting

### Issue: "Invalid form format (no FormDefinition found)"

**Cause**: The source JSON doesn't have the expected structure

**Solutions**:
1. Verify JSON structure matches `SourceForm` model
2. Check if the root object contains `FormDefinition` property
3. Ensure Views array is not empty

```csharp
// Debug: Print JSON structure
var obj = JsonConvert.DeserializeObject<dynamic>(json);
Console.WriteLine($"Root keys: {string.Join(", ", ((IDictionary<string, object>)obj).Keys)}");
```

### Issue: Controls Not Appearing in Output

**Cause**: Controls may be filtered out or grid positions are invalid

**Solutions**:
1. Check grid position format (should be like "1A", "2B")
2. Verify control types are in the mapping dictionary
3. Check if view name contains "summary" or "filter" (these are skipped)

```csharp
// Debug: Log control processing
foreach (var control in view.Controls)
{
    Console.WriteLine($"Processing {control.Name} (Type: {control.Type}, Grid: {control.GridPosition})");
}
```

### Issue: Missing Translations

**Cause**: Translation keys are generated but not populated

**Solutions**:
1. Ensure source controls have `Label` property set
2. Check TranslationSettings.DefaultLanguage is "en"
3. Verify translation dictionary is initialized

```csharp
// Manually add translations if missing
if (!nintexForm.Translations.ContainsKey("en"))
{
    nintexForm.Translations["en"] = new Dictionary<string, string>();
}
```

### Issue: Repeating Sections Not Converted

**Cause**: RepeatingSectionInfo is missing or data items not marked as repeating

**Solutions**:
1. Check if SourceControl has RepeatingSectionInfo populated
2. Verify SourceDataItem.IsRepeating is set to true
3. Ensure repeating controls are grouped correctly

```csharp
// Debug: Check repeating section detection
var repeatingControls = view.Controls
    .Where(c => c.RepeatingSectionInfo != null)
    .ToList();
Console.WriteLine($"Found {repeatingControls.Count} repeating controls");
```

### Issue: Performance Slow for Large Batches

**Solutions**:
1. Use parallel processing (see Advanced Usage)
2. Process files in smaller batches
3. Increase memory allocation if needed
4. Use async/await for I/O operations

```csharp
// Monitor memory usage
var before = GC.GetTotalMemory(false);
// ... conversion ...
var after = GC.GetTotalMemory(true);
Console.WriteLine($"Memory used: {(after - before) / 1024 / 1024} MB");
```

### Issue: Special Characters in Output

**Cause**: Encoding issues during serialization

**Solutions**:
```csharp
// Use UTF8 encoding explicitly
var json = JsonConvert.SerializeObject(form, Formatting.Indented);
File.WriteAllText(outputPath, json, Encoding.UTF8);
```

### Debugging Tips

#### Enable Detailed Logging

```csharp
public class DebugFormConverter : FormConverter
{
    private readonly ILogger _logger;

    public DebugFormConverter(ILogger logger)
    {
        _logger = logger;
    }

    public new FormDefinition ConvertForm(SourceForm sourceForm)
    {
        _logger.LogInformation("=== Starting Conversion ===");
        _logger.LogInformation($"Form: {sourceForm.FileName}");
        _logger.LogInformation($"Views: {sourceForm.FormDefinition.Views.Count}");

        foreach (var view in sourceForm.FormDefinition.Views)
        {
            _logger.LogInformation($"  View: {view.ViewName} ({view.Controls?.Count ?? 0} controls)");
        }

        var result = base.ConvertForm(sourceForm);

        _logger.LogInformation($"Output Rows: {result.Rows.Count}");
        _logger.LogInformation($"Output Controls: {result.Rows.Sum(r => r.Controls.Count)}");
        _logger.LogInformation("=== Conversion Complete ===");

        return result;
    }
}
```

#### Capture Conversion Statistics

```csharp
public class ConversionStatistics
{
    public static void PrintStats(SourceForm source, FormDefinition output)
    {
        Console.WriteLine("\n=== Conversion Statistics ===");
        Console.WriteLine($"Source Views: {source.FormDefinition.Views.Count}");
        Console.WriteLine($"Source Data Items: {source.FormDefinition.Data?.Count ?? 0}");
        Console.WriteLine($"Source Controls: {source.FormDefinition.Views.Sum(v => v.Controls?.Count ?? 0)}");
        Console.WriteLine($"\nOutput Rows: {output.Rows.Count}");
        Console.WriteLine($"Output Controls: {output.Rows.Sum(r => r.Controls.Count)}");
        Console.WriteLine($"Translation Keys: {output.Translations["en"].Count}");

        var widgetCounts = output.Rows
            .SelectMany(r => r.Controls)
            .GroupBy(c => c.Widget)
            .OrderByDescending(g => g.Count());

        Console.WriteLine("\nWidget Distribution:");
        foreach (var group in widgetCounts)
        {
            Console.WriteLine($"  {group.Key}: {group.Count()}");
        }
        Console.WriteLine("============================\n");
    }
}
```

---

## Support and Resources

### Getting Help

1. **Check the code**: Review `FormConverter.cs` for implementation details
2. **Inspect models**: Review `FormDefinition.cs` and `SourceForm.cs` for structure
3. **Test with samples**: Use the original WPF app to see expected behavior
4. **Enable logging**: Add detailed logging to track conversion flow

### Best Practices

1. **Always validate input** before conversion
2. **Use try-catch** for robust error handling
3. **Log conversion metrics** for monitoring
4. **Test with sample data** before production use
5. **Keep backups** of original forms
6. **Version control** your customizations
7. **Document custom mappings** if you modify the converter

### Performance Guidelines

- **Single forms**: Direct conversion is fine
- **Batch < 100 files**: Sequential processing is acceptable
- **Batch > 100 files**: Use parallel processing
- **Very large forms**: Monitor memory usage

### Version Compatibility

- **Nintex Form Version**: Currently outputs version 26
- **Contract Version**: v3
- **.NET Target**: Compatible with .NET 6.0+
- **JSON Library**: Newtonsoft.Json 13.0.3

---

## Appendix

### Complete Minimal Example

```csharp
// Program.cs
using NWConverter.Services;
using NWConverter.Models;
using Newtonsoft.Json;

var converter = new FormConverter();
var json = File.ReadAllText("source.json");
var source = JsonConvert.DeserializeObject<SourceForm>(json);
var nintex = converter.ConvertForm(source);
File.WriteAllText("output.json", JsonConvert.SerializeObject(nintex, Formatting.Indented));
```

### Sample Source Form JSON

```json
{
  "FileName": "Sample Form.xsn",
  "FormDefinition": {
    "Views": [
      {
        "ViewName": "view1.xsl",
        "Controls": [
          {
            "Name": "EmployeeName",
            "Type": "TextField",
            "Label": "Employee Name",
            "GridPosition": "1A",
            "Binding": "EmployeeName",
            "IsRequired": true
          },
          {
            "Name": "StartDate",
            "Type": "DatePicker",
            "Label": "Start Date",
            "GridPosition": "2A",
            "Binding": "StartDate"
          }
        ]
      }
    ],
    "Data": [
      {
        "ColumnName": "EmployeeName",
        "DisplayName": "Employee Name",
        "Type": "TextField"
      },
      {
        "ColumnName": "StartDate",
        "DisplayName": "Start Date",
        "Type": "DatePicker"
      }
    ]
  }
}
```

### Project Structure Reference

```
YourProject/
├── Services/
│   └── FormConverter.cs          # Copy from NWConverter
├── Models/
│   ├── FormDefinition.cs         # Copy from NWConverter
│   └── SourceForm.cs             # Copy from NWConverter
├── YourIntegrationCode.cs
└── YourProject.csproj
    └── <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
```

---

**Document Version**: 1.0
**Last Updated**: 2025-10-20
**Compatible with**: NWConverter v1.0

For questions or issues, please refer to the source code or create an issue in the project repository.
