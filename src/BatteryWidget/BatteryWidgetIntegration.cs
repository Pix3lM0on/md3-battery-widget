using System.Text.Json;
using BluetoothBattery;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Batteries;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Widgets;
using MacroDeck.Ui.Model.Surfaces;

namespace BatteryWidget;

public sealed class BatteryWidgetIntegration(IBatteryApi batteries) : IPluginIntegration, IWidgetTypeProvider, IUiProvider
{
    public const string WidgetType = "battery-overview";
    private string? _registeredType;
    private const string Schema = """
        {"type":"object","properties":{
          "showName":{"type":"boolean"},
          "showPercentage":{"type":"boolean"},
          "ringStyle":{"enum":["circle","gauge"]},
          "gaugeAngle":{"enum":["180","270"]},
          "roundedEnds":{"type":"boolean"},
          "ringThickness":{"type":["number","null"],"minimum":1,"maximum":12},
          "lineColorMode":{"enum":["single","thresholds"]},
          "batteryThresholds":{"type":["object","null"]},
          "lineColor":{"type":["string","null"]},
          "chargingColor":{"type":["string","null"]},
          "trackColor":{"type":["string","null"]},
          "interiorColor":{"type":["string","null"]},
          "iconColor":{"type":["string","null"]},
          "nameColor":{"type":["string","null"]},
          "percentageColor":{"type":["string","null"]},
          "namePosition":{"enum":["inside","below"]},
          "percentagePosition":{"enum":["inside","below"]},
          "selectedDeviceIds":{"type":["array","null"],"items":{"type":"string","minLength":1},"uniqueItems":true},
          "flows":{"type":"array","items":{"type":"object"}}
        }}
        """;

    public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
    [
        new() { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
        new() { Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Exclusive },
        new() { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive }
    ];

    public IReadOnlyList<IActionDefinition> Actions => [];
    public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;
    public Task ShutdownAsync() => Task.CompletedTask;

    public IReadOnlyList<WidgetTypeDescriptor> GetWidgetTypes() =>
        [new(WidgetType, "Battery Overview", "Battery rings for devices reported to Macro Deck.",
            DefaultData: "{}", DataSchema: Schema, HasConfiguration: true) { SupportsFlows = true }];

    public async Task InitializeAsync(IWidgetTypeProviderContext context, CancellationToken cancellationToken = default)
    {
        var registration = await context.RegisterWidgetTypeAsync(GetWidgetTypes()[0], cancellationToken);
        _registeredType = registration.WidgetTypeId;
    }

    public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var surface = request.Surface;
        IUiSession? session = null;
        if (surface.Kind == UiSurfaceKinds.Config &&
            IsValue(surface, UiConfigSurfaceAttributes.EntryPoint, UiConfigEntryPoints.WidgetConfig) &&
            IsWidget(surface, UiConfigSurfaceAttributes.WidgetType))
        {
            surface.Attributes.TryGetValue(UiConfigSurfaceAttributes.WidgetData, out var data);
            session = BatteryWidgetViews.Configure(surface, batteries, new(data));
        }
        else if (surface.Kind is UiSurfaceKinds.Widget or UiSurfaceKinds.Preview &&
                 IsWidget(surface, UiWidgetSurfaceAttributes.WidgetType))
        {
            surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.Data, out var data);
            var sample = surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.Sample, out var value) &&
                value.ValueKind == JsonValueKind.True;
            session = BatteryWidgetViews.Display(surface, batteries, new(data), sample);
        }
        return Task.FromResult(session);
    }

    private bool IsWidget(UiSurface surface, string key) =>
        IsValue(surface, key, WidgetType) || (_registeredType is { } registered && IsValue(surface, key, registered));

    private static bool IsValue(UiSurface surface, string key, string expected) =>
        surface.Attributes.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String &&
        value.GetString() == expected;
}

internal sealed class BatteryWidgetOptions(JsonElement data)
{
    private JsonElement Get(string name) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var value) ? value : default;

    public bool ShowName => Get("showName").ValueKind != JsonValueKind.False;
    public bool ShowPercentage => Get("showPercentage").ValueKind != JsonValueKind.False;
    public bool NameInside => Get("namePosition") is { ValueKind: JsonValueKind.String } name && name.GetString() == "inside";
    public bool PercentageBelow => Get("percentagePosition") is { ValueKind: JsonValueKind.String } percentage && percentage.GetString() == "below";
    public WidgetRingOptions Ring { get; } = WidgetRingOptions.Read(data) with
    {
        Gauge = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("ringStyle", out var style) &&
            style.ValueKind == JsonValueKind.String && style.GetString() == "gauge",
        Thickness = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("ringThickness", out var width) &&
            width.ValueKind == JsonValueKind.Number && width.TryGetDouble(out var thickness) && thickness is >= 1 and <= 12
            ? thickness : 5.5
    };
    public WidgetColorOptions Colors { get; } = WidgetColorOptions.Read(data);
    public JsonElement Flows => Get("flows") is { ValueKind: JsonValueKind.Array } flows
        ? flows.Clone() : JsonSerializer.SerializeToElement(Array.Empty<object>());
    public IReadOnlyList<string> SelectedIds => Get("selectedDeviceIds") is { ValueKind: JsonValueKind.Array } ids
        ? ids.EnumerateArray().Where(id => id.ValueKind == JsonValueKind.String)
            .Select(id => id.GetString()!).Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal).ToArray()
        : [];
}
