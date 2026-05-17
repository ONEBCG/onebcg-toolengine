namespace ToolEngine.Tools.Samples.Composite.WeatherReport;

public sealed record WeatherReportOutput(
    string City,
    double Temperature,
    string Unit,
    string Condition,
    int    HumidityPercent,
    double WindKph,
    string Summary);
