namespace ToolEngine.Tools.Samples.Api.Weather;

using System.Net.Http;
using System.Text.Json;
using ToolEngine.Core.Abstractions.Security;
using ToolEngine.Core.Domain.Common;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Schema;
using ToolEngine.Tools.Abstractions.Base;

/// <summary>
/// MCP name: "weather.current"
/// API tool — calls wttr.in to get current weather.
/// Demonstrates ApiToolBase with Zero Trust ISecretVault and typed deserialization.
/// </summary>
public sealed class WeatherTool : ApiToolBase<WeatherInput, WeatherOutput>
{
    private static readonly JsonSerializerOptions _json = new()
        { PropertyNameCaseInsensitive = true };

    public WeatherTool(IHttpClientFactory factory, ISecretVault vault)
        : base(factory, vault) { }

    public override string Namespace   => "weather";
    public override string Name        => "current";
    public override string Version     => "v1";
    public override string Description =>
        "Returns real-time weather conditions for a named city: temperature, sky description, " +
        "relative humidity, and wind speed.";

    public override ToolSchema InputSchema => ToolSchema.For<WeatherInput>(
        description:   "The city to look up and the preferred temperature unit (default: celsius).",
        whenToUse:     "Use when the user asks about current weather, temperature, sky conditions, " +
                       "humidity, or wind speed for a specific city. Handles phrasing such as " +
                       "'what's the weather in X', 'how hot is it in X', 'is it raining in X', " +
                       "'what's the temperature in X right now'.",
        whenNotToUse:  "Do not call for weather forecasts or historical weather data. " +
                       "Do not call with a region or country name (e.g. 'Europe', 'UK') — the " +
                       "city must be specific. If the city name is ambiguous (e.g. 'Springfield'), " +
                       "ask the user to clarify before calling.",
        examples:
        [
            new("Get current weather for London in Celsius",
                new WeatherInput("London", "celsius"),
                new WeatherOutput("London", 15.2, "Partly cloudy", 72, 18.5)),
            new("Get current temperature in Tokyo in Fahrenheit",
                new WeatherInput("Tokyo", "fahrenheit"),
                new WeatherOutput("Tokyo", 77.0, "Clear sky", 55, 12.0))
        ],
        new ToolParameter("city", "string",
                          "Specific city name. Examples: 'London', 'New York', 'Tokyo', 'Sydney'. " +
                          "Do not pass country or region names."),
        new ToolParameter("unit", "string",
                          "Temperature unit for the response: celsius (default) or fahrenheit.",
                          Required: false, Default: "celsius", Nullable: true,
                          Enum: ["celsius", "fahrenheit"]));

    public override ToolSchema OutputSchema => ToolSchema.For<WeatherOutput>(
        description:   "Current weather conditions.",
        whenToUse:     "Always returned on success.",
        whenNotToUse:  "N/A",
        examples:      [],
        new ToolParameter("city",               "string",  "Resolved city name"),
        new ToolParameter("temperatureCelsius", "number",  "Temperature in °C"),
        new ToolParameter("condition",          "string",  "Weather description"),
        new ToolParameter("humidityPercent",    "integer", "Relative humidity 0-100"),
        new ToolParameter("windKph",            "number",  "Wind speed km/h"));

    public override async Task<ToolResponse<WeatherOutput>> ExecuteAsync(
        ToolRequest<WeatherInput> request,
        CancellationToken ct = default)
    {
        // Zero Trust: fetch API key scoped to this invocation
        // In dev, ISecretVault returns a stub. In prod, fetches from Azure Key Vault.
        var secret = await Vault.GetSecretAsync(
            Namespace, Name, "WEATHER_API_KEY", request.CorrelationId, ct);

        if (secret.IsExpired)
            return ToolResponse<WeatherOutput>.Fail(
                request.CorrelationId,
                ToolError.FromError(Error.SecretExpired("WEATHER_API_KEY"), 500));

        var city = Uri.EscapeDataString(request.Input.City);
        var http  = CreateClient();

        // Note: actual wttr.in doesn't require an API key — this demonstrates the pattern
        http.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", secret.Value);

        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await http.GetAsync($"/{city}?format=j1", ct);
        }
        catch (HttpRequestException ex)
        {
            return ToolResponse<WeatherOutput>.Fail(
                request.CorrelationId,
                ToolError.Internal($"Weather API unreachable: {ex.Message}"));
        }

        if (!httpResponse.IsSuccessStatusCode)
            return ToolResponse<WeatherOutput>.Fail(
                request.CorrelationId,
                ToolError.Internal($"Weather API returned {(int)httpResponse.StatusCode}."));

        var json = await httpResponse.Content.ReadAsStringAsync(ct);
        WeatherApiResponse? api;
        try { api = JsonSerializer.Deserialize<WeatherApiResponse>(json, _json); }
        catch { api = null; }

        if (api?.CurrentCondition is not { Count: > 0 } conditions)
            return ToolResponse<WeatherOutput>.Fail(
                request.CorrelationId,
                ToolError.NotFound($"No weather data found for '{request.Input.City}'."));

        WeatherApiResponse.WeatherCondition cond = conditions[0];
        // C5 — WeatherDesc itself can be null for malformed/empty API responses.
        var conditionText = cond.WeatherDesc?.FirstOrDefault()?.Value ?? "Unknown";
        var output = new WeatherOutput(
            request.Input.City,
            double.TryParse(cond.TempC,    out var t) ? t : 0,
            conditionText,
            int.TryParse(cond.Humidity,    out var h) ? h : 0,
            double.TryParse(cond.WindKmph, out var w) ? w : 0);

        return ToolResponse<WeatherOutput>.Ok(request.CorrelationId, output);
    }
}
