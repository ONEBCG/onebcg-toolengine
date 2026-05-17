namespace ToolEngine.Tools.Samples.Composite.WeatherReport;

using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Schema;
using ToolEngine.Tools.Abstractions.Base;
using ToolEngine.Tools.Samples.Api.Weather;
using ToolEngine.Tools.Samples.Logic.Calculator;

/// <summary>
/// MCP name: "weather.report"
/// Composite tool — fetches weather via WeatherTool then converts temperature
/// via CalculatorTool. Demonstrates CompositeToolBase and the InvokeAsync helper.
/// </summary>
public sealed class WeatherReportTool
    : CompositeToolBase<WeatherReportInput, WeatherReportOutput>
{
    public WeatherReportTool(IToolExecutor executor) : base(executor) { }

    public override string Namespace   => "weather";
    public override string Name        => "report";
    public override string Version     => "v1";
    public override string Description =>
        "Fetches current weather and converts temperature to the requested unit.";

    public override ToolSchema InputSchema => ToolSchema.For<WeatherReportInput>(
        description:   "City name and desired output temperature unit.",
        whenToUse:     "Call when the user wants a weather summary with temperature in a " +
                       "specific unit (especially Fahrenheit, since weather.current returns Celsius).",
        whenNotToUse:  "Do not call if the user only needs Celsius — use weather.current directly. " +
                       "Do not call for multiple cities — call once per city.",
        examples:
        [
            new("Weather in London in Fahrenheit",
                new WeatherReportInput("London", "fahrenheit"),
                new WeatherReportOutput("London", 59.4, "fahrenheit",
                    "Partly cloudy", 72, 18.5, "London: 59.4°F, Partly cloudy, 72% humidity."))
        ],
        new ToolParameter("city",            "string", "City name"),
        new ToolParameter("temperatureUnit", "string", "celsius or fahrenheit",
                          Required: false, Default: "fahrenheit", Nullable: true,
                          Enum: ["celsius", "fahrenheit"]));

    public override ToolSchema OutputSchema => ToolSchema.For<WeatherReportOutput>(
        description:   "Enriched weather report with converted temperature.",
        whenToUse:     "Always returned on success.",
        whenNotToUse:  "N/A",
        examples:      [],
        new ToolParameter("city",            "string",  "Resolved city"),
        new ToolParameter("temperature",     "number",  "Temperature in requested unit"),
        new ToolParameter("unit",            "string",  "The temperature unit"),
        new ToolParameter("condition",       "string",  "Weather description"),
        new ToolParameter("humidityPercent", "integer", "Humidity %"),
        new ToolParameter("windKph",         "number",  "Wind speed km/h"),
        new ToolParameter("summary",         "string",  "Human-readable summary"));

    public override async Task<ToolResponse<WeatherReportOutput>> ExecuteAsync(
        ToolRequest<WeatherReportInput> request,
        CancellationToken ct = default)
    {
        // Step 1: get weather via weather.current
        var weatherResult = await InvokeAsync<WeatherInput, WeatherOutput>(
            request.TenantId,
            childNamespace: "weather",
            childName:      "current",
            childVersion:   "v1",
            new WeatherInput(request.Input.City),
            ct);

        if (!weatherResult.Success)
            return ToolResponse<WeatherReportOutput>.Fail(
                request.CorrelationId, weatherResult.Error!);

        var weather   = weatherResult.Data!;
        double finalTemp = weather.TemperatureCelsius;
        string unit      = "celsius";

        // Step 2: convert temperature if Fahrenheit requested
        if (request.Input.TemperatureUnit.Equals(
                "fahrenheit", StringComparison.OrdinalIgnoreCase))
        {
            // Step 2: convert temperature via math.calculate
            var convResult = await InvokeAsync<CalculatorInput, CalculatorOutput>(
                request.TenantId,
                childNamespace: "math",
                childName:      "calculate",
                childVersion:   "v1",
                new CalculatorInput(weather.TemperatureCelsius * 9.0 / 5.0, 32, "add"),
                ct);

            if (convResult.Success)
            {
                finalTemp = convResult.Data!.Result;
                unit      = "fahrenheit";
            }
        }

        var summary = $"{weather.City}: {finalTemp:F1}°{char.ToUpper(unit[0])}, " +
                      $"{weather.Condition}, {weather.HumidityPercent}% humidity.";

        return ToolResponse<WeatherReportOutput>.Ok(
            request.CorrelationId,
            new WeatherReportOutput(
                weather.City, finalTemp, unit,
                weather.Condition, weather.HumidityPercent,
                weather.WindKph, summary));
    }
}
