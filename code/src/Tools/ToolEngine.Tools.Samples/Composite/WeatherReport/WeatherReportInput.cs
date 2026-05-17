namespace ToolEngine.Tools.Samples.Composite.WeatherReport;

public sealed record WeatherReportInput(
    string City,
    string TemperatureUnit = "fahrenheit");  // output unit preference
