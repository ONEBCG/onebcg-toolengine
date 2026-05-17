namespace ToolEngine.Tools.Samples.Logic.Calculator;

public sealed record CalculatorOutput(
    double Result,
    string Expression);  // e.g. "42 + 8 = 50"
