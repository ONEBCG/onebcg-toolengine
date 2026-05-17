namespace ToolEngine.Tools.Samples.Api.Weather;

using System.Text.Json.Serialization;

/// <summary>Partial model matching wttr.in ?format=j1 response.</summary>
internal sealed class WeatherApiResponse
{
    [JsonPropertyName("current_condition")]
    public List<WeatherCondition> CurrentCondition { get; set; } = [];

    [JsonPropertyName("nearest_area")]
    public List<AreaInfo> NearestArea { get; set; } = [];

    public sealed class WeatherCondition
    {
        [JsonPropertyName("temp_C")]        public string           TempC       { get; set; } = "";
        [JsonPropertyName("weatherDesc")]   public List<ValueWrap>  WeatherDesc { get; set; } = [];
        [JsonPropertyName("humidity")]      public string           Humidity    { get; set; } = "";
        [JsonPropertyName("windspeedKmph")] public string           WindKmph    { get; set; } = "";
    }

    public sealed class AreaInfo
    {
        [JsonPropertyName("areaName")] public List<ValueWrap> AreaName { get; set; } = [];
    }

    public sealed record ValueWrap([property: JsonPropertyName("value")] string Value);
}
