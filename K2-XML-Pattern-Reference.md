# K2 SmartForms XML Pattern Reference

> Captured from K2 Designer (`k2trial.safalo.com/designer/`) on 2026-03-08.
> All patterns validated against live K2 Designer XML output (SourceCode.Forms Version 31).

---

## 1. Element Hierarchy

```
Events
  └─ Event (Type="User")
       ├─ Name ("OnChange" | "Init" | etc.)
       ├─ Properties
       │    ├─ ViewID  (Name, DisplayValue, NameValue, Value)
       │    ├─ RuleFriendlyName
       │    └─ Location (= view display name)
       └─ Handlers
            └─ Handler
                 ├─ Properties
                 │    ├─ HandlerName ("IfLogicalHandler")
                 │    └─ Location (= "view"  ← lowercase!)
                 ├─ Conditions
                 │    └─ Condition
                 │         ├─ Properties
                 │         │    └─ Location (= "View"  ← PascalCase)
                 │         └─ Expressions
                 │              └─ <ExpressionElement>  (Equals, Contains, etc.)
                 │                   ├─ Item (left – Control)
                 │                   └─ Item (right – Value)  [except unary]
                 └─ Actions
                      └─ Action
                           ├─ Properties
                           │    ├─ Location (= "View"  ← PascalCase)
                           │    ├─ ControlID  (visibility only)
                           │    └─ ViewID
                           └─ Parameters
                                └─ Parameter
                                     └─ SourceValue (xml:space="preserve")
```

---

## 2. Location Casing Rules

| Context       | Value              | Example                          |
|---------------|--------------------|----------------------------------|
| Event         | view display name  | `"forma"`, `"formb"`             |
| Handler       | `"view"`           | Always lowercase                 |
| Condition     | `"View"`           | Always PascalCase                |
| Action        | `"View"`           | Always PascalCase                |

---

## 3. ViewID Property Element Order

K2 Designer always emits ViewID sub-elements in this order:

```xml
<Property>
  <Name>ViewID</Name>
  <DisplayValue>forma</DisplayValue>
  <NameValue>forma</NameValue>
  <Value>a1b2c3d4-...</Value>
</Property>
```

Order: **Name → DisplayValue → NameValue → Value**

---

## 4. Condition Types

### 4.1 Simple Conditions

Simple conditions use `InnerText` directly on the right Item element.

| Condition Name                      | Expression Element | Items |
|-------------------------------------|--------------------|-------|
| `SimpleEqualControlCondition`       | `<Equals>`         | 2     |
| `SimpleNotEqualControlCondition`    | `<NotEquals>`      | 2     |
| `SimpleBlankControlCondition`       | `<IsBlank>`        | 1     |
| `SimpleNotBlankControlCondition`    | `<IsNotBlank>`     | 1     |

**Simple Equals example:**
```xml
<Condition ID="..." DefinitionID="...">
  <Properties>
    <Property><Name>Location</Name><Value>View</Value></Property>
  </Properties>
  <Expressions>
    <Equals>
      <Item SourceType="Control" SourceID="ctrl-guid" SourceName="MyCheckbox"
            SourceDisplayName="MyCheckbox" DataType="Text" />
      <Item SourceType="Value" DataType="Text">true</Item>
    </Equals>
  </Expressions>
</Condition>
```

### 4.2 Advanced Conditions

Advanced conditions use `<SourceValue xml:space="preserve">` as a **child element** of the right Item.

| K2 UI Label          | Expression Element      | Items |
|----------------------|-------------------------|-------|
| Equals               | `<Equals>`              | 2     |
| Not Equals           | `<NotEquals>`           | 2     |
| Contains             | `<Contains>`            | 2     |
| Starts With          | `<StartsWith>`          | 2     |
| Ends With            | `<EndsWith>`            | 2     |
| Greater Than         | `<GreaterThan>`         | 2     |
| Less Than            | `<LessThan>`            | 2     |
| Greater Than Equals  | `<GreaterThanEquals>`   | 2     |
| Less Than Equals     | `<LessThanEquals>`      | 2     |
| Is Empty             | `<IsBlank>`             | 1     |
| Is Not Empty         | `<IsNotBlank>`          | 1     |

**Advanced Contains example:**
```xml
<Condition ID="..." DefinitionID="...">
  <Properties>
    <Property><Name>Location</Name><Value>View</Value></Property>
  </Properties>
  <Expressions>
    <Contains>
      <Item SourceType="Control" SourceID="ctrl-guid" SourceName="NAME2TextBox"
            SourceDisplayName="NAME2TextBox" DataType="Text" />
      <Item SourceType="Value" DataType="Text">
        <SourceValue xml:space="preserve">TestContains</SourceValue>
      </Item>
    </Contains>
  </Expressions>
</Condition>
```

**Advanced IsBlank (unary) example:**
```xml
<Expressions>
  <IsBlank>
    <Item SourceType="Control" SourceID="ctrl-guid" SourceName="NAME2TextBox"
          SourceDisplayName="NAME2TextBox" DataType="Text" />
  </IsBlank>
</Expressions>
```

> **Key distinction:** Simple conditions → right Item uses `InnerText`.
> Advanced conditions → right Item uses `<SourceValue>` child element.

---

## 5. Compound Conditions (AND / OR)

### AND Logic (nested)
Multiple AND conditions use **nested `<And>` elements** — NOT `<Composite>`.

```xml
<Expressions>
  <And>
    <Contains>...</Contains>
    <And>
      <StartsWith>...</StartsWith>
      <And>
        <EndsWith>...</EndsWith>
        <NotEquals>...</NotEquals>
      </And>
    </And>
  </And>
</Expressions>
```

For N conditions, there are N-1 nested `<And>` wrappers.

### OR Logic (nested)
Same pattern with `<Or>` elements:

```xml
<Expressions>
  <Or>
    <Equals>...</Equals>
    <Or>
      <Contains>...</Contains>
      <StartsWith>...</StartsWith>
    </Or>
  </Or>
</Expressions>
```

---

## 6. Action Types

### 6.1 Visibility Transfer (Show/Hide)

```xml
<Action ID="..." DefinitionID="..." Type="Transfer" ExecutionType="Synchronous">
  <Properties>
    <Property><Name>Location</Name><Value>View</Value></Property>
    <Property>
      <Name>ControlID</Name>
      <DisplayValue>EMAILTextBox</DisplayValue>
      <NameValue>EMAILTextBox</NameValue>
      <Value>ctrl-guid</Value>
    </Property>
    <Property>
      <Name>ViewID</Name>
      <DisplayValue>forma</DisplayValue>
      <NameValue>forma</NameValue>
      <Value>view-guid</Value>
    </Property>
  </Properties>
  <Parameters>
    <Parameter SourceType="Value" TargetID="isvisible"
               TargetDisplayName="EMAILTextBox" TargetType="ControlProperty">
      <SourceValue xml:space="preserve">true</SourceValue>
    </Parameter>
  </Parameters>
</Action>
```

**Notes:**
- Has `ControlID` property (unlike data transfer)
- `TargetID="isvisible"` for show/hide
- `TargetType="ControlProperty"`
- No `SourceID` attribute on Parameter

### 6.2 Data Transfer (Control-to-Control)

```xml
<Action ID="..." DefinitionID="..." Type="Transfer" ExecutionType="Synchronous">
  <Properties>
    <Property><Name>Location</Name><Value>View</Value></Property>
    <Property>
      <Name>ViewID</Name>
      <DisplayValue>forma</DisplayValue>
      <NameValue>forma</NameValue>
      <Value>view-guid</Value>
    </Property>
  </Properties>
  <Parameters>
    <Parameter SourceID="src-ctrl-guid" SourceName="SourceCtrl"
               SourceDisplayName="SourceCtrl" SourceType="Control"
               TargetID="tgt-ctrl-guid" TargetName="TargetCtrl"
               TargetDisplayName="TargetCtrl" TargetType="Control" />
  </Parameters>
</Action>
```

**Notes:**
- No `ControlID` property (unlike visibility)
- All source/target info is on Parameter attributes

### 6.3 Data Transfer (Value/Literal)

```xml
<Parameters>
  <Parameter SourceID="Sources" SourceType="Value"
             TargetID="tgt-ctrl-guid" TargetName="TargetCtrl"
             TargetDisplayName="TargetCtrl" TargetType="Control">
    <SourceValue xml:space="preserve">literal value here</SourceValue>
  </Parameter>
</Parameters>
```

**Notes:**
- `SourceID="Sources"` for Value-type transfers
- `SourceType="Value"`
- Literal value in `<SourceValue>` child element

### 6.4 ShowMessage (Validation)

```xml
<Action ID="..." DefinitionID="..." Type="ShowMessage" ExecutionType="Synchronous">
  <Properties>
    <Property><Name>Location</Name><Value>View</Value></Property>
    <Property><Name>MessageLocation</Name><Value>Popup</Value></Property>
    <Property><Name>HeadingIsLiteral</Name><Value>False</Value></Property>
    <Property><Name>BodyIsLiteral</Name><Value>False</Value></Property>
  </Properties>
  <Parameters>
    <Parameter SourceID="Sources" SourceType="Value"
               TargetID="Title" TargetName="Title" TargetType="MessageProperty">
      <SourceValue xml:space="preserve">
        <Source SourceType="Value">Validation</Source>
      </SourceValue>
    </Parameter>
    <!-- Similar for Size, Type, Heading, Body -->
  </Parameters>
</Action>
```

---

## 7. Visibility Rule Pattern (Toggle)

K2 Designer creates **two handlers** for checkbox-based show/hide:

```
Event: OnChange (source = checkbox)
  ├─ Handler 1: IfLogicalHandler
  │    ├─ Condition: source = "true"
  │    └─ Action: set target isvisible = "true"
  └─ Handler 2: IfLogicalHandler
       ├─ Condition: source = "false"
       └─ Action: set target isvisible = "false"
```

---

## 8. Event Element Attributes

```xml
<Event ID="guid" DefinitionID="guid" Type="User"
       SourceID="ctrl-guid" SourceType="Control"
       SourceName="MyControl" SourceDisplayName="MyControl"
       IsExtended="True">
```

- `Type="User"` for user-defined rules (not `"System"`)
- `IsExtended="True"` always present
- `SourceID` = the trigger control's GUID

---

## 9. Handler Element Attributes

```xml
<Handler ID="guid" DefinitionID="guid">
  <Properties>
    <Property><Name>HandlerName</Name><Value>IfLogicalHandler</Value></Property>
    <Property><Name>Location</Name><Value>view</Value></Property>
  </Properties>
```

- Location is always **lowercase** `"view"`

---

## 10. Code Files Reference

| File | Purpose |
|------|---------|
| `K2RuleBuilderBase.cs` | Shared XML element creation (events, handlers, actions) |
| `K2CompoundConditionBuilder.cs` | All condition/expression XML builders |
| `K2VisibilityRuleBuilder.cs` | Visibility show/hide rule builder |
| `K2DataTransferRuleBuilder.cs` | Data transfer rule builder |
| `K2ValidationRuleBuilder.cs` | Required-field validation rule builder |
| `ViewRulesBuilder.cs` | Legacy rule builder (direct XML construction) |

### Available Condition Builder Methods

```csharp
// Simple conditions (InnerText pattern)
BuildEqualsCondition(doc, controlId, controlName, compareValue)
BuildNotEqualsCondition(doc, controlId, controlName, compareValue)
BuildIsBlankCondition(doc, controlId, controlName)
BuildIsNotEmptyCondition(doc, controlId, controlName)

// Advanced conditions (SourceValue child pattern)
BuildContainsCondition(doc, controlId, controlName, compareValue, dataType)
BuildStartsWithCondition(doc, controlId, controlName, compareValue, dataType)
BuildEndsWithCondition(doc, controlId, controlName, compareValue, dataType)
BuildGreaterThanCondition(doc, controlId, controlName, compareValue, dataType)
BuildLessThanCondition(doc, controlId, controlName, compareValue, dataType)
BuildGreaterThanEqualsCondition(doc, controlId, controlName, compareValue, dataType)
BuildLessThanEqualsCondition(doc, controlId, controlName, compareValue, dataType)

// Compound conditions
BuildAdvancedAndCondition(doc, List<XmlElement> subExpressions)
BuildAdvancedOrCondition(doc, List<XmlElement> subExpressions)
BuildCompoundCondition(doc, conditions)  // wraps in nested Or

// Raw expression element (for use inside compound builders)
CreateAdvancedExpression(doc, expressionType, controlId, controlName, compareValue, dataType)
```

---

## 11. Changes Made (2026-03-08)

### K2CompoundConditionBuilder.cs
- Added 7 new expression builders: Contains, StartsWith, EndsWith, GreaterThan, LessThan, GreaterThanEquals, LessThanEquals
- Added `BuildAdvancedAndCondition` and `BuildAdvancedOrCondition` with nested element pattern
- Added `CreateAdvancedExpression` for raw expression elements
- Fixed `BuildCompoundCondition` to use nested `<Or>` instead of `<Composite>`

### K2RuleBuilderBase.cs
- Fixed Handler Location: `"View"` → `"view"` (lowercase)
- Fixed ViewID property order in `CreateEventElement`: Name → DisplayValue → NameValue → Value
- Fixed ViewID property order in `CreateVisibilityTransferAction`
- Fixed ViewID property order in `CreateDataTransferAction`
- Added `SourceID="Sources"` default for Value-type transfers

### ViewRulesBuilder.cs
- Fixed ViewID property order at 3 locations (lines ~926, ~1126, ~1275): DisplayValue before NameValue
