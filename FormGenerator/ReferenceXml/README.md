# K2 Reference XML

This directory stores K2 Designer-exported XML for validating generated rules.

## Structure

- `Visibility/` - Show/hide rules (checkbox toggle, dropdown conditional)
- `Validation/` - Required field validation rules
- `DataTransfer/` - Control-to-control copy and calculated values
- `MultiCondition/` - Compound AND/OR conditions

## How to Add Reference XML

1. Create a form in K2 Designer with the target rule type
2. Export the form XML
3. Extract the `<Events>` section
4. Save to the appropriate subdirectory
5. Use `K2RuleXmlValidator` to compare generated output against the reference
