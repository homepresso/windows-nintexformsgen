# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a Windows WPF application called "FormGenerator" that generates SQL database structures from Nintex forms. It's a .NET 8 desktop application that analyzes form definitions and creates corresponding SQL tables, stored procedures, and views.

## Key Architecture

### Core Components

- **Services Layer**: Contains the main business logic
  - `SqlGeneratorService`: Main SQL generation service implementing `ISqlGenerator`
  - `SQLConnectionService`: Handles database connections and deployment
  - `ReusableControlGroupAnalyzer`: Analyzes form control groups

- **Core Models**: Data models for SQL generation
  - `sql.cs`: Contains models for SQL deployment info, form mappings, column mappings, and repeating section mappings

- **Core Interfaces**: Abstraction layer for extensibility
  - `IFormAnalyzer`: Base interface for all form analyzers
  - `ISqlGenerator`: Interface for SQL generation services
  - `IFormRebuilder`: Interface for form rebuilders (K2, NAC, etc.)
  - `IExporter`: Interface for export functionality

- **Analyzers Layer**: Form analysis implementations
  - `InfoPath2013Analyzer`: Analyzes InfoPath 2013 forms (.xsn files)
  - `InfoPathParser`: Core InfoPath form parsing logic
  - `Infopath2013Rules`: Business rules for InfoPath 2013 analysis
  - `InfoPathFormDefinitionExtensions`: Extension methods for form definitions

- **Views**: WPF UI components
  - `MainWindow.xaml`: Main application window with dark theme
  - `MainWindow.xaml.cs`: Main window code-behind with dependency injection setup
  - `MainWindowAnalysisHandlers.cs`: Analysis-related UI handlers
  - `MainWindowGenerationHandlers.cs`: SQL generation UI handlers

- **Converters**: UI converters for WPF data binding
  - `IconConverter.cs`: Converts boolean values to icons

### Architecture Pattern

The application uses a layered architecture with:
- **Views**: WPF UI layer with XAML and code-behind
- **Services**: Business logic layer with dependency injection
- **Analyzers**: Form parsing and analysis layer
- **Core**: Interfaces, models, and abstractions
- **Converters**: UI data binding helpers

### Key Workflows

1. **Form Analysis Pipeline**:
   - File upload → Form type detection → Analyzer selection → Form parsing → Result generation
   - Uses `AnalyzerFactory` for analyzer selection based on file type
   - Supports InfoPath 2013 forms (.xsn files) primarily

2. **SQL Generation Process**:
   - Analysis result → Structure type selection → SQL script generation → Deployment
   - Two structure types: `FlatTables` (default) and `NormalizedQA`
   - Handles repeating sections, lookup tables, and complex form structures

3. **Database Deployment**:
   - Connection establishment → Script execution → Mapping storage
   - Supports SQL Server with authentication options

## Development Commands

### Build and Run
```bash
dotnet build FormGenerator.sln
dotnet run --project FormGenerator
```

### Debug Build
```bash
dotnet build FormGenerator.sln --configuration Debug
```

### Release Build
```bash
dotnet build FormGenerator.sln --configuration Release
```

### Clean Build
```bash
dotnet clean FormGenerator.sln
```

## Dependencies

- **Target Framework**: .NET 8.0 Windows
- **UI Framework**: WPF (Windows Presentation Foundation)
- **Key NuGet Packages**:
  - `Newtonsoft.Json` (13.0.1) - JSON serialization for form data
  - `Microsoft.Data.SqlClient` (6.0.2) - SQL Server connectivity and deployment

## Key Data Models

### SQL Generation Models (`sql.cs`)
- `SqlDeploymentInfo`: Tracks deployment metadata and structure type
- `FormSqlMapping`: Maps form elements to SQL table structures
- `ColumnMapping`: Maps form fields to database columns
- `RepeatingSectionMapping`: Handles repeating form sections as related tables
- `LookupTableMapping`: Manages dropdown/lookup field mappings

### Analysis Models (`IFormAnalyzer.cs`)
- `FormAnalysisResult`: Complete analysis result with form definition and metadata
- `SqlGenerationResult`: SQL generation output with scripts and execution order
- `FormFileInfo`: UI model for file management and status tracking

## Development Notes

- The project uses nullable reference types (`<Nullable>enable</Nullable>`)
- Implicit usings are enabled (`<ImplicitUsings>enable</ImplicitUsings>`)
- The application targets Windows-specific frameworks (WPF)
- Dark theme UI with modern styling and keyboard shortcuts:
  - `Ctrl+Shift+A`: Add Control Command
  - `Delete`: Delete Control Command
  - `F2`: Edit Control Command
  - `Ctrl+Shift+R`: Convert to Repeating Command
- Uses dependency injection pattern in MainWindow constructor
- Form analysis is async/await throughout the application
- Supports both flat table and normalized Q&A SQL generation patterns

## Extension Points

When adding new functionality:

1. **New Form Analyzers**: Implement `IFormAnalyzer` interface in `Analyzers/` folder
2. **New SQL Dialects**: Extend `SqlDialect` enum and update `SqlGeneratorService`
3. **New Export Formats**: Implement `IExporter` interface
4. **New Form Rebuilders**: Implement `IFormRebuilder` for target platforms

The `AnalyzerFactory` handles analyzer registration and selection based on file extensions.

## Enhanced Rule Capture System

The application now includes a comprehensive rule capture system that goes beyond basic rule extraction to provide deep analysis of InfoPath expressions, calculations, and business logic.

### Key Components

- **EnhancedRuleExtractor**: Advanced rule extraction with XPath function parsing and complexity analysis
- **XPathFunctionParser**: Comprehensive parser for InfoPath-specific XPath functions and expressions
- **ExpressionAnalyzer**: Analyzes expression complexity, dependencies, and provides human-readable translations
- **RuleSummaryGenerator**: Generates detailed reports on rule complexity and migration recommendations

### Enhanced Rule Models

- **EnhancedFormRule**: Comprehensive rule representation with complexity scoring, dependencies, and metadata
- **EnhancedExpression**: Detailed expression analysis including function calls, field references, and translation hints
- **ValidationRule**: Enhanced validation with pattern analysis and custom validation support
- **CalculationRule**: Calculation-specific analysis with source field tracking and recalculation triggers
- **BusinessLogicRule**: Groups related rules into logical business units

### Features

1. **XPath Function Support**: Recognizes 50+ InfoPath XPath functions including:
   - Date/time functions (today, now, addDays, formatDate)
   - String functions (concat, substring, contains, normalize-space)
   - Math functions (sum, count, avg, min, max)
   - InfoPath-specific functions (user, userName, role)

2. **Complexity Analysis**: Automatically categorizes rules as Simple, Moderate, Complex, or Advanced based on:
   - Number of field references
   - Function usage
   - Nested conditions
   - Data lookup requirements

3. **Dependency Tracking**: Maps rule relationships and field dependencies for better understanding of business logic flow

4. **Translation Hints**: Provides platform-specific migration guidance for complex expressions

5. **Business Logic Grouping**: Automatically groups related rules into logical business units

### Usage

The enhanced rule system runs automatically during form analysis and provides detailed insights through:

```csharp
// Enhanced rule analysis is integrated into the main analyzer
var analysis = formDef.RuleAnalysis;
Console.WriteLine($"Total rules: {analysis.TotalRules}");
Console.WriteLine($"Complex rules: {analysis.ComplexRules}");

// Generate detailed rule summary
var summaryGenerator = new RuleSummaryGenerator();
var summary = summaryGenerator.GenerateRuleSummary(formDef);
```

This system significantly improves the accuracy and completeness of rule migration planning by providing deep insights into form business logic complexity.