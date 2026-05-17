namespace ToolEngine.Tools.Samples.Api.Weather;

public sealed record WeatherInput(
    string City,
    string Unit = "celsius");  // "celsius" | "fahrenheit"
