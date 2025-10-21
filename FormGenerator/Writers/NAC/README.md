# Nintex Workflow Form Converter

A C# WPF desktop application for converting legacy form definitions to Nintex Workflow (Nintex Automation Cloud) format. This tool is designed to facilitate migration efforts by providing a user-friendly interface to transform forms from various source formats into the Nintex form-definition.json format.

## Features

### Core Functionality
- **Batch Conversion**: Convert multiple form files simultaneously
- **Folder Processing**: Process entire folders containing form files
- **Progress Tracking**: Real-time progress indication during conversion

### Supported Input Formats
- Legacy form definitions with JSON
- Forms with Views, Controls, Sections, and Data definitions
- Various control types including:
  - Text fields (single and multi-line)
  - Date/time pickers
  - Dropdown/choice controls
  - Checkboxes
  - File uploads
  - Repeating sections
  - And more...

### Output Format
- Nintex Workflow form-definition.json format
- Compatible with Nintex Workflow
- Includes proper translations and control mappings
- Maintains form structure and relationships

## Installation

### Prerequisites
- .NET 6.0 or later
- Windows 10/11

### Build Instructions
1. Clone or download the repository
2. Open the solution in Visual Studio 2022 or later
3. Restore NuGet packages
4. Build the solution
5. Run the application

### Dependencies
- Newtonsoft.Json (13.0.3)
- MaterialDesignThemes (4.9.0)
- MaterialDesignColors (2.1.4)

## Usage

### Getting Started
1. Launch the application
2. Add source files using "Add Files" or "Add Folder" buttons
3. Review the files in the source list
4. Click "Convert" to process all files
5. View results in the output section

### File Management
- **Add Files**: Select individual JSON files for conversion
- **Add Folder**: Process all JSON files in a folder and subfolders
- **Remove Files**: Click the X button next to any file to remove it
- **Clear Output**: Clear the output file list

### Output Management
- Converted files are saved to `Desktop\NintexForms\`
- Each file is named with `_nintex.json` suffix
- Click the folder icon to open output files
- Use "Open Output Folder" to navigate to the output directory

## Configuration

### Output Directory
The default output directory is `Desktop\NintexForms\`. This can be modified in the `MainWindow.xaml.cs` file:

```csharp
_outputDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "NintexForms");
```

### Control Mappings
Control type mappings can be customized in the `FormConverter.cs` file. The application includes mappings for:
- TextField → textbox
- TextArea → multilinetext
- DatePicker → datetime
- DropDown → choice
- CheckBox → boolean
- And many more...

## Architecture

### Project Structure
```
NWConverter/
├── Models/
│   ├── FormDefinition.cs      # Nintex form definition models
│   └── SourceForm.cs          # Source form models
├── Services/
│   └── FormConverter.cs       # Core conversion logic
├── MainWindow.xaml            # Main application window
├── PreviewWindow.xaml         # Form preview window
└── README.md                  # This file
```

### Key Components

#### FormConverter Service
- Handles the transformation from source format to Nintex format
- Maps control types and properties
- Generates unique IDs and variable names
- Manages translations and form structure

#### Models
- **FormDefinition**: Represents the Nintex form structure
- **SourceForm**: Represents the input form structure
- Comprehensive property mappings for all form elements

#### UI Components
- **MainWindow**: Primary application interface
- **PreviewWindow**: Form structure preview and export
- Material Design theming throughout

## Conversion Process

### 1. Input Parsing
- Reads JSON source files
- Validates form structure
- Extracts control definitions and properties

### 2. Control Mapping
- Maps source control types to Nintex widgets
- Converts data types and formats
- Preserves control properties where possible

### 3. Structure Generation
- Creates Nintex form definition structure
- Generates unique IDs for all controls
- Sets up proper variable bindings

### 4. Translation Management
- Creates translation keys for all text elements
- Maintains label and description mappings
- Supports multi-language form definitions

### 5. Output Generation
- Serializes to Nintex JSON format
- Applies proper formatting and indentation
- Validates output structure

## Troubleshooting

### Common Issues

#### "Invalid form format" Error
- Ensure source files follow the expected JSON structure
- Check that files contain valid JSON syntax
- Verify that required form properties are present

#### Conversion Failures
- Review error messages for specific control type issues
- Check file permissions for output directory
- Ensure sufficient disk space for output files

#### UI Issues
- Verify .NET 6.0 runtime is installed
- Check Windows Forms dependencies are available
- Ensure Material Design themes are properly loaded

### Debug Mode
Enable detailed logging by modifying the conversion process in `FormConverter.cs`:

```csharp
// Add logging statements
Console.WriteLine($"Converting control: {sourceControl.Name}");
```

## Contributing

### Development Setup
1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests for new functionality
5. Submit a pull request

### Code Style
- Follow C# coding conventions
- Use meaningful variable and method names
- Add XML documentation for public methods
- Include error handling for all external operations

## License

This project is provided as-is for internal use. Please ensure compliance with your organization's licensing requirements.



## Version History

### v1.0.0
- Initial release
- Basic form conversion functionality
- Material Design UI
- Preview and export features
- Batch processing support
