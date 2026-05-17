namespace ToolEngine.Tools.Samples.Logic.Calculator;

using ToolEngine.Core.Domain.Common;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Schema;
using ToolEngine.Tools.Abstractions.Base;

/// <summary>
/// MCP name: "math.calculate"
/// Logic tool — pure arithmetic, no I/O, no credentials.
/// </summary>
public sealed class CalculatorTool : LogicToolBase<CalculatorInput, CalculatorOutput>
{
    public override string Namespace   => "math";
    public override string Name        => "calculate";
    public override string Version     => "v1";
    public override string Description => "Performs basic arithmetic on two operands.";

    public override ToolSchema InputSchema => ToolSchema.For<CalculatorInput>(
        description:   "Two numeric operands and an arithmetic operator.",
        whenToUse:     "Call when the user needs to add, subtract, multiply, or divide two numbers. " +
                       "Also use for unit conversions that reduce to arithmetic (e.g. °C to °F).",
        whenNotToUse:  "Do not call for complex expressions with more than two operands. " +
                       "Do not call for string operations or date arithmetic.",
        examples:
        [
            new("Convert 37°C to Fahrenheit (multiply then add)",
                new CalculatorInput(37, 1.8, "multiply"),
                new CalculatorOutput(66.6, "37 × 1.8 = 66.6"))
        ],
        new ToolParameter("leftOperand",  "number", "Left-hand operand"),
        new ToolParameter("rightOperand", "number", "Right-hand operand"),
        new ToolParameter("operator",     "string",  "One of: add, subtract, multiply, divide",
                          Enum: ["add", "subtract", "multiply", "divide"]));

    public override ToolSchema OutputSchema => ToolSchema.For<CalculatorOutput>(
        description:   "Arithmetic result.",
        whenToUse:     "Always returned on success.",
        whenNotToUse:  "N/A",
        examples:      [],
        new ToolParameter("result",     "number", "Computed numeric value"),
        new ToolParameter("expression", "string", "Human-readable expression e.g. '37 + 8 = 45'"));

    public override Task<ToolResponse<CalculatorOutput>> ExecuteAsync(
        ToolRequest<CalculatorInput> request,
        CancellationToken ct = default)
    {
        var (left, right, op) = (
            request.Input.LeftOperand,
            request.Input.RightOperand,
            request.Input.Operator.ToLowerInvariant());

        var result = op switch
        {
            "add"      => Result.Success(left + right),
            "subtract" => Result.Success(left - right),
            "multiply" => Result.Success(left * right),
            "divide"   => right == 0
                              ? Result.Failure<double>(Error.Validation("Division by zero."))
                              : Result.Success(left / right),
            _          => Result.Failure<double>(
                              Error.Validation($"Unknown operator '{op}'."))
        };

        if (result.IsFailure)
            return Task.FromResult(
                ToolResponse<CalculatorOutput>.Fail(
                    request.CorrelationId,
                    ToolError.FromError(result.Error, 400)));

        var symbol = op switch
        {
            "add"      => "+",
            "subtract" => "−",
            "multiply" => "×",
            _          => "÷"
        };

        return Task.FromResult(
            ToolResponse<CalculatorOutput>.Ok(
                request.CorrelationId,
                new CalculatorOutput(
                    result.Value,
                    $"{left} {symbol} {right} = {result.Value}")));
    }
}
