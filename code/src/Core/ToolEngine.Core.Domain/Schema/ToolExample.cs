namespace ToolEngine.Core.Domain.Schema;

/// <summary>
/// A concrete input/output pair shown in the tool schema.
/// Agents use examples to understand how to construct valid inputs.
/// Per Anthropic 2025 guidance: include at least one example per tool.
/// </summary>
public sealed record ToolExample(
    string Description,    // "Convert Celsius to Fahrenheit"
    object Input,          // { "leftOperand": 37, "rightOperand": 1.8, "operator": "multiply" }
    object ExpectedOutput  // { "result": 66.6, "expression": "37 × 1.8 = 66.6" }
);
