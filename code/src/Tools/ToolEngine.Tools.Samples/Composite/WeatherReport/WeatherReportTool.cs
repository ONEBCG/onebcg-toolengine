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
        "Fetches current weather for a city, converts the temperature to the requested unit " +
        "(Celsius or Fahrenheit), and returns a ready-to-display summary string. " +
        "Preferred over weather.current when the user wants Fahrenheit or a formatted summary.";

    public override ToolSchema InputSchema => ToolSchema.For<WeatherReportInput>(
        description:   "The city to report on and the desired temperature unit for the output.",
        whenToUse:     "Use when the user asks for weather in Fahrenheit, or when a pre-formatted " +
                       "summary sentence is needed. Handles phrasing like 'weather in X in Fahrenheit', " +
                       "'give me a weather summary for X', 'what's it like in X in °F'. " +
                       "Internally calls weather.current then converts temperature via math.calculate.",
        whenNotToUse:  "Do not use when the user only needs the Celsius reading — " +
                       "weather.current is more direct. Do not call for multiple cities in one request; " +
                       "call this tool once per city.",
        examples:
        [
            new("Full weather report for London in Fahrenheit",
                new WeatherReportInput("London", "fahrenheit"),
                new WeatherReportOutput("London", 59.4, "fahrenheit",
                    "Partly cloudy", 72, 18.5, "London: 59.4°F, Partly cloudy, 72% humidity.")),
            new("Weather summary for Tokyo in Celsius",
                new WeatherReportInput("Tokyo", "celsius"),
                new WeatherReportOutput("Tokyo", 25.0, "celsius",
                    "Clear sky", 55, 12.0, "Tokyo: 25.0°C, Clear sky, 55% humidity."))
        ],
        new ToolParameter("city", "string",
                          "Specific city name. Examples: 'London', 'New York', 'Paris', 'Tokyo'. " +
                          "Must be a city, not a region or country."),
        new ToolParameter("temperatureUnit", "string",
                          "Temperature unit for the output: celsius or fahrenheit. Defaults to fahrenheit.",
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
