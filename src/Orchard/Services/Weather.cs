using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Orchard.Model;

namespace Orchard.Services;

public enum WeatherKind { Clear, PartlyCloudy, Cloudy, Fog, Rain, Snow, Storm }

public sealed record HourlyWeather(DateTime Time, double Temperature, WeatherKind Kind);

public sealed record WeatherInfo(string City, double Temperature, WeatherKind Kind, double High, double Low, bool IsDay,
    IReadOnlyList<HourlyWeather> Hourly, bool Fahrenheit)
{
    public string Condition => Kind switch
    {
        WeatherKind.Clear => IsDay ? "Sunny" : "Clear",
        WeatherKind.PartlyCloudy => "Partly Cloudy",
        WeatherKind.Cloudy => "Cloudy",
        WeatherKind.Fog => "Foggy",
        WeatherKind.Rain => "Rain",
        WeatherKind.Snow => "Snow",
        _ => "Thunderstorms",
    };

    /// <summary>WMO weather interpretation codes, as returned by Open-Meteo.</summary>
    public static WeatherKind FromWmo(int code) => code switch
    {
        0 or 1 => WeatherKind.Clear,
        2 => WeatherKind.PartlyCloudy,
        3 => WeatherKind.Cloudy,
        45 or 48 => WeatherKind.Fog,
        >= 51 and <= 67 or >= 80 and <= 82 => WeatherKind.Rain,
        >= 71 and <= 77 or 85 or 86 => WeatherKind.Snow,
        >= 95 => WeatherKind.Storm,
        _ => WeatherKind.Cloudy,
    };
}

/// <summary>
/// Weather for the widget from Open-Meteo (free, no API key). The city comes from settings; nothing is looked up
/// until the user sets one, and nothing but the city name and its coordinates is ever sent.
/// </summary>
public sealed class WeatherService(OrchardSettings settings)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private DateTime _fetched = DateTime.MinValue;
    private bool _busy;

    public WeatherInfo? Current { get; private set; }
    public event Action? Updated;

    /// <summary>Refreshes at most every 30 minutes; call it as often as you like.</summary>
    public async Task RefreshAsync()
    {
        if (_busy || string.IsNullOrWhiteSpace(settings.WeatherCity) || DateTime.UtcNow - _fetched < TimeSpan.FromMinutes(30)) return;
        _busy = true;
        try
        {
            var city = Uri.EscapeDataString(settings.WeatherCity.Trim());
            using var geo = JsonDocument.Parse(await Http.GetStringAsync($"https://geocoding-api.open-meteo.com/v1/search?name={city}&count=1"));
            if (!geo.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0) return;
            var place = results[0];
            string name = place.GetProperty("name").GetString() ?? settings.WeatherCity;
            string lat = place.GetProperty("latitude").GetDouble().ToString(CultureInfo.InvariantCulture);
            string lon = place.GetProperty("longitude").GetDouble().ToString(CultureInfo.InvariantCulture);
            string unit = settings.Fahrenheit ? "&temperature_unit=fahrenheit" : "";
            var url = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current=temperature_2m,weather_code,is_day" +
                      $"&hourly=temperature_2m,weather_code&daily=temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=2{unit}";
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            Current = Parse(doc.RootElement, name, settings.Fahrenheit);
            _fetched = DateTime.UtcNow;
            Updated?.Invoke();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Log.Write($"weather unavailable: {e.Message}");
        }
        finally
        {
            _busy = false;
        }
    }

    internal static WeatherInfo Parse(JsonElement root, string city, bool fahrenheit)
    {
        var cur = root.GetProperty("current");
        var daily = root.GetProperty("daily");
        var hourly = root.GetProperty("hourly");
        var curTime = DateTime.Parse(cur.GetProperty("time").GetString()!, CultureInfo.InvariantCulture);
        var times = hourly.GetProperty("time").EnumerateArray().Select(t => DateTime.Parse(t.GetString()!, CultureInfo.InvariantCulture)).ToList();
        var temps = hourly.GetProperty("temperature_2m").EnumerateArray().Select(t => t.GetDouble()).ToList();
        var codes = hourly.GetProperty("weather_code").EnumerateArray().Select(t => t.GetInt32()).ToList();
        var next = Enumerable.Range(0, times.Count).Where(i => times[i] > curTime).Take(6)
            .Select(i => new HourlyWeather(times[i], temps[i], WeatherInfo.FromWmo(codes[i]))).ToList();
        return new WeatherInfo(city,
            cur.GetProperty("temperature_2m").GetDouble(),
            WeatherInfo.FromWmo(cur.GetProperty("weather_code").GetInt32()),
            daily.GetProperty("temperature_2m_max")[0].GetDouble(),
            daily.GetProperty("temperature_2m_min")[0].GetDouble(),
            cur.GetProperty("is_day").GetInt32() == 1,
            next, fahrenheit);
    }
}
