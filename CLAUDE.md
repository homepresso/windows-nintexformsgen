# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a Windows WPF application called "FormGenerator" that analyzes Nintex/InfoPath forms and generates:
- SQL database structures (tables, stored procedures, views)
- K2 SmartForms artifacts
- Nintex Automation Cloud (NAC) form definitions

## Development Commands

```bash
# Build
dotnet build FormGenerator.sln

# Run
dotnet run --project FormGenerator

# Clean build
dotnet clean FormGenerator.sln && dotnet build FormGenerator.sln

# Release build
dotnet build FormGenerator.sln --configuration Release
```

## Dependencies

- **Target Framework**: .NET Framework 4.8 (`net48`)
- **UI Framework**: WPF
- **Key Packages**:
  - `Newtonsoft.Json` (13.0.1)
  - `Microsoft.Data.SqlClient` (5.1.1)
- **K2 SmartForms SDK**: Local DLL references in `Writers/K2/References/`

## Key Architecture

### Layered Structure

```
FormGenerator/
├── Analyzers/Infopath/     # Form parsing (InfoPath 2007/2010/2013, Nintex)
├── Core/
│   ├── Interfaces/         # IFormAnalyzer, ISqlGenerator, IFormRebuilder, IExporter
│   ├── Models/             # sql.cs, EnhancedRuleModels.cs, RuleMappingModels.cs
│   └── Converters/         # WPF data binding converters
├── Services/               # Business logic (SqlGeneratorService, RuleMappingService, etc.)
├── Views/                  # WPF UI (MainWindow split into partial class handlers)
└── Writers/                # Output generators
    ├── K2/                 # K2 SmartForms generation with SDK
    └── NAC/                # Nintex Automation Cloud generation
```

### MainWindow Partial Classes

The MainWindow is split across multiple files for maintainability:
- `MainWindow.xaml.cs` - Core window, file management, AnalyzerFactory
- `MainWindowAnalysisHandlers.cs` - Analysis/display handlers
- `MainWindowGenerationHandlers.cs` - SQL/K2/Nintex generation handlers
- `MainWindowRulesMappingHandlers.cs` - Rule mapping UI handlers

### Key Workflows

1. **Form Analysis**: File upload → `AnalyzerFactory.GetAnalyzer()` → `IFormAnalyzer.AnalyzeFormAsync()` → `FormAnalysisResult` with `InfoPathFormDefinition`

2. **SQL Generation**: `FormAnalysisResult` → `SqlGeneratorService` → SQL scripts for tables, procedures, views
   - Structure types: `FlatTables` (default) or `NormalizedQA`

3. **K2 Generation**: Analysis → `K2GenerationService` → SmartForms artifacts via K2 SDK
   - Uses `FormGenerator.cs`, `ViewGenerator.cs`, `SmartObjectGenerator.cs`

4. **Rule Mapping**: `RuleMappingService.AnalyzeRules()` → Maps InfoPath rules to K2 rule XML

### Extension Points

- **New Analyzers**: Implement `IFormAnalyzer` in `Analyzers/`
- **New Writers**: Add folder under `Writers/` following K2/NAC pattern
- **New SQL Dialects**: Extend `SqlDialect` enum and `SqlGeneratorService`

## Enhanced Rule System

The `EnhancedRuleExtractor` and `RuleMappingService` provide:
- XPath function parsing (50+ InfoPath functions)
- Complexity scoring (Simple/Moderate/Complex/Advanced)
- Automatic K2 rule XML generation
- Validation/conditional/calculation rule mapping

Rule mapping statuses: `Supported`, `PartiallySupported`, `NotSupported`, `RequiresCustomization`, `NotMapped`

## UI Notes

- Dark theme with keyboard shortcuts: `F2` (Edit), `Delete` (Delete), `Ctrl+Shift+A` (Add Control)
- TreeView for form structure with context menu operations
- Tabs: Analysis, Form Structure, SQL Generation, Nintex Generation, K2 Generation, Rules Mapping
