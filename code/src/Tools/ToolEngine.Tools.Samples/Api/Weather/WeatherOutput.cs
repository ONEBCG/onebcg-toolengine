namespace ToolEngine.Tools.Samples.Api.Weather;

public sealed record WeatherOutput(
    string City,
    double TemperatureCelsius,
    string Condition,
    int    HumidityPercent,
    double WindKph);
