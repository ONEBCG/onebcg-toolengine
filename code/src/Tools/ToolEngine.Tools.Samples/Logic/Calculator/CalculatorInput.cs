namespace ToolEngine.Tools.Samples.Logic.Calculator;

public sealed record CalculatorInput(
    double LeftOperand,
    double RightOperand,
    string Operator);   // "add" | "subtract" | "multiply" | "divide"
