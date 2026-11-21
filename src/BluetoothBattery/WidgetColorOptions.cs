using System.Text.Json;
using MacroDeck.Ui.Config;

namespace BluetoothBattery;

public sealed record WidgetColorOptions
{
    public string Interior { get; init; } = "transparent";
    public string Track { get; init; } = "#42464c";
    public bool Single { get; init; }
    public string Line { get; init; } = "#4caf50";
    public string Charging { get; init; } = "#2196f3";
    public UiThresholds Thresholds { get; init; } = DefaultThresholds;
    public string Icon { get; init; } = "";
    public string Percentage { get; init; } = "";
    public string Name { get; init; } = "";
    public static UiThresholds DefaultThresholds { get; } = new([new("low", "#ef5350"), new("medium", "#ffb300", 11), new("high", "#4caf50", 21)]);
    public string BatteryColor(double? battery) => Single ? Line : Thresholds.ColorAt(battery ?? 0) is { Length: > 0 } color ? color : "#4caf50";
    public static bool ValidThresholds(UiThresholds? value) => value is { Bands.Count: 3 } &&
        value.Bands.Skip(1).All(b => b.From is >= 1 and <= 100 && b.From == Math.Truncate(b.From.Value)) &&
        value.Bands.All(b => Normalize(b.Color, "") is { Length: > 0 });
    public static WidgetColorOptions Read(JsonElement data)
    {
        JsonElement Get(string key) => data.ValueKind == JsonValueKind.Object && data.TryGetProperty(key, out var value) ? value : default;
        string Color(string key, string fallback) => Get(key) is { ValueKind: JsonValueKind.String } v ? Normalize(v.GetString(), fallback) : fallback;
        UiThresholds.TryParse(Get("batteryThresholds"), out var thresholds);
        return new() { Interior = Color("interiorColor", "transparent"), Track = Color("trackColor", "#42464c"),
            Single = Get("lineColorMode") is { ValueKind: JsonValueKind.String } mode && mode.GetString() == "single",
            Line = Color("lineColor", "#4caf50"), Charging = Color("chargingColor", "#2196f3"), Thresholds = ValidThresholds(thresholds) ? thresholds! : DefaultThresholds,
            Icon = Color("iconColor", ""), Percentage = Color("percentageColor", ""), Name = Color("nameColor", "") };
    }
    public static string Normalize(string? value, string fallback)
    {
        if (value == "transparent") return fallback == "transparent" ? "transparent" : "#00000000";
        if (value is not { Length: 4 or 7 or 9 } || value[0] != '#' || !value.AsSpan(1).ToArray().All(Uri.IsHexDigit)) return fallback;
        return value.Length == 4 ? "#" + string.Concat(value.Skip(1).Select(c => new string(c, 2))).ToLowerInvariant() : value.ToLowerInvariant();
    }
}
