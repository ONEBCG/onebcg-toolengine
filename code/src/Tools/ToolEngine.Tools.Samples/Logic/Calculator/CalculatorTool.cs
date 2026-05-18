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
    public override string Description =>
        "Evaluates an arithmetic operation on two numbers and returns the numeric result " +
        "together with a human-readable expression string (e.g. '37 × 1.8 = 66.6').";

    public override ToolSchema InputSchema => ToolSchema.For<CalculatorInput>(
        description:   "An arithmetic operation: two numeric operands and the operation to apply.",
        whenToUse:     "Use for any numeric calculation: sums, differences, products, quotients, " +
                       "percentages, ratios, and unit conversions (e.g. °C to °F, miles to km). " +
                       "For multi-step expressions, chain multiple sequential calls. " +
                       "Handles phrasing like 'what is X times Y', 'divide X by Y', 'X plus Y', " +
                       "'convert 37°C to Fahrenheit'.",
        whenNotToUse:  "Do not call for non-numeric input (text, dates, lists). " +
                       "Do not pass a text expression like '3 + 4' as an operand — parse the " +
                       "numbers and operator from the user's request before calling this tool.",
        examples:
        [
            new("Convert 37°C to Fahrenheit — step 1: multiply by 1.8",
                new CalculatorInput(37, 1.8, "multiply"),
                new CalculatorOutput(66.6, "37 × 1.8 = 66.6")),
            new("Calculate a 15% tip on a £42 bill",
                new CalculatorInput(42, 0.15, "multiply"),
                new CalculatorOutput(6.3, "42 × 0.15 = 6.3"))
        ],
        new ToolParameter("leftOperand",  "number", "First number in the operation. " +
                          "Example: 37 for a temperature conversion, 1500 for a salary calculation."),
        new ToolParameter("rightOperand", "number", "Second number in the operation. " +
                          "Example: 1.8 for °C-to-°F scale factor, 0.15 for a 15% rate."),
        new ToolParameter("operator",     "string",  "The arithmetic operation to perform. " +
                          "One of: add, subtract, multiply, divide.",
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
