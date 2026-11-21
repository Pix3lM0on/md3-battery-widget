using System.Globalization;
using System.Text.Json;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;

namespace BluetoothBattery;

public sealed record WidgetRingOptions(bool Gauge = false, int GaugeAngle = 270, bool RoundedEnds = true, double Thickness = 4.5)
{
    public double Fraction => Thickness / 100;
    public double Start => Gauge ? -GaugeAngle / 2d : 0;
    public double End => Gauge ? GaugeAngle / 2d : 360;
    public static WidgetRingOptions Read(JsonElement data)
    {
        bool Get(string key, out JsonElement value) { value = default; return data.ValueKind == JsonValueKind.Object && data.TryGetProperty(key, out value); }
        return new(Get("ringType", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "gauge",
            Get("gaugeAngle", out var angle) && angle.ValueKind == JsonValueKind.String && angle.GetString() == "180" ? 180 : 270,
            Get("roundedEnds", out var rounded) && rounded.ValueKind is JsonValueKind.True or JsonValueKind.False ? rounded.GetBoolean() : true,
            Get("ringThickness", out var thickness) && thickness.ValueKind == JsonValueKind.Number && thickness.TryGetDouble(out var t) && t is >= 1 and <= 12 ? t : 4.5);
    }

    public UiElement Render(double level, string color, string track, string key = "ring", double? fallbackThickness = null)
    {
        if (RoundedEnds) return new UiGauge { Key = key, StartAngle = Start, EndAngle = End, Level = level,
            LevelColor = color, TrackColor = track, Thickness = UiSize.FromParent(Fraction, fallbackThickness ?? 0.035) };
        // The beta.16 gauge always has round caps. Native stroked paths use flat caps.
        // A centered square prevents the normalized path from stretching on wide/tall widgets.
        var paths = new List<UiElement> { Arc("t", End - Start, track) };
        if (level > 0) paths.Add(Arc("f", (End - Start) * level, color));
        return new UiStack { Key = key, Align = UiComponentAlignments.Center, Justify = UiComponentJustify.Center,
            Children = [new UiModifier { Key = "q", MainSize = UiSize.FromParent(1), Frame = new UiFrame { AspectRatio = 1 },
                Child = new UiLayer { Key = "a", Children = paths } }] };
    }
    private UiShape Arc(string key, double sweep, string color) => new() { Key = key, Shape = UiComponentShapes.Path,
        Path = ArcPath(Start, sweep, (1 - Fraction) / 2), StrokeColor = color, StrokeWidth = UiSize.FromParent(Fraction) };
    public static string ArcPath(double start, double sweep, double radius)
    {
        static string N(double n) => n.ToString("0.#####", CultureInfo.InvariantCulture);
        string Point(double a) => N(.5 + radius * Math.Sin(a * Math.PI / 180)) + " " + N(.5 - radius * Math.Cos(a * Math.PI / 180));
        string Arc(double a, double s) => $"A{N(radius)} {N(radius)} 0 {(s > 180 ? 1 : 0)} 1 {Point(a)}";
        return "M" + Point(start) + (sweep >= 360 ? Arc(start + 180, 180) + Arc(start + 360, 180) : Arc(start + sweep, sweep));
    }
}
