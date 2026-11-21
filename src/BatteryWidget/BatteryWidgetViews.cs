using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BluetoothBattery;
using MacroDeck.Sdk.Batteries;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Previews;

namespace BatteryWidget;

internal static class BatteryWidgetViews
{
    private const int MaxVisibleDevices = 64;

    public static IUiSession Display(UiSurface surface, IBatteryApi batteries, BatteryWidgetOptions options, bool sample)
    {
        var selected = options.SelectedIds.ToHashSet(StringComparer.Ordinal);
        var devices = new UiState<IReadOnlyList<BatteryDeviceSnapshot>>(sample
            ? [new() { Id = "preview:laptop", LocalId = "laptop", ProviderId = "preview", Name = "Laptop",
                Kind = BatteryDeviceKinds.Laptop, LevelPercent = 76, IsConnected = true }]
            : []);
        var root = Overview(devices, options, selected.Count > 0);
        IReadOnlyList<BatteryDeviceSnapshot> Read() => batteries.Devices.Where(d => selected.Count == 0 || selected.Contains(d.Id)).ToArray();
        return sample ? new BatteryCatalogSession(surface, root) : new BatteryCatalogSession(surface, root, batteries, () => devices.Value = Read());
    }

    [UiPreview("Battery overview", Profile = UiPreviewProfiles.Widget)]
    public static UiElement Preview() => Overview(new([
        new() { Id = "sample:laptop", LocalId = "laptop", ProviderId = "sample", Name = "Laptop",
            Kind = BatteryDeviceKinds.Laptop, LevelPercent = 80, IsConnected = true },
        new() { Id = "sample:phone", LocalId = "phone", ProviderId = "sample", Name = "Phone",
            Kind = BatteryDeviceKinds.Phone, LevelPercent = 43, IsConnected = true, ChargeState = BatteryChargeState.Charging }
    ]), new(default), false);

    [UiPreview("Battery widget icon", Profile = UiPreviewProfiles.Widget)]
    public static UiElement IconPreview() => Overview(new([
        new() { Id = "sample:laptop", LocalId = "laptop", ProviderId = "sample", Name = "Laptop",
            Kind = BatteryDeviceKinds.Laptop, LevelPercent = 100, IsConnected = true }
    ]), new(JsonSerializer.SerializeToElement(new
    {
        ringStyle = "gauge", gaugeAngle = "270", ringThickness = 10, showName = false
    })), false);

    private static UiElement Overview(UiState<IReadOnlyList<BatteryDeviceSnapshot>> devices, BatteryWidgetOptions options, bool hasSelection) =>
        new UiStack
        {
            Key = "overview", Padding = UiSize.Capped(.04, 8),
            Children =
            [
                new UiWhen { Key = "empty", Condition = () => devices.Value.Count == 0,
                    Content = () => Caption("message", !hasSelection ? "No batteries reported" : "Selected batteries unavailable") },
                new UiWhen { Key = "has-batteries", Condition = () => devices.Value.Count > 0,
                    Content = () => new UiGrid
                    {
                        Key = "grid", Fill = true, MinCellSize = UiSize.Capped(.6, 100), Gap = UiSize.Capped(.03, 8),
                        Columns = UiValue.From(() => (int)Math.Ceiling(Math.Sqrt(Math.Min(devices.Value.Count, MaxVisibleDevices)))),
                        Children = [new UiRepeat<BatteryDeviceSnapshot>
                        {
                            Key = "cards", Items = UiValue.From<IReadOnlyList<BatteryDeviceSnapshot>>(() => devices.Value.Take(MaxVisibleDevices).ToArray()),
                            KeySelector = device => DeviceKey(device.Id),
                            Template = (device, key) => Card(device, key, options)
                        }]
                    } },
                new UiWhen { Key = "overflow", Condition = () => devices.Value.Count > MaxVisibleDevices,
                    Content = () => Caption("remaining", UiText.From(() => $"+{devices.Value.Count - MaxVisibleDevices} batteries")) }
            ]
        };

    public static IUiSession Configure(UiSurface surface, IBatteryApi batteries, BatteryWidgetOptions options)
    {
        var devices = new UiState<IReadOnlyList<BatteryDeviceSnapshot>>([]);
        var selected = new UiState<IReadOnlyList<string>>(options.SelectedIds);
        var flows = new UiState<JsonElement>(options.Flows);
        var root = new UiWidgetConfiguration
        {
            Key = "config",
            Properties = new UiWidgetProperties
            {
                Key = "properties",
                Children = [new UiTabs
                {
                    Key = "settings-tabs",
                    Children =
                    [
                        AppearanceTab(options),
                        DevicesTab(devices, selected)
                    ]
                }]
            },
            Editor = new UiWidgetEditor
            {
                Key = "editor",
                Children = [new UiActionsListEditor { Key = "flows", Binding = Bind.To(flows), CanRun = true }]
            }
        };
        return new BatteryCatalogSession(surface, root, batteries, () => devices.Value = batteries.Devices.ToArray());
    }

    private static UiTab DevicesTab(UiState<IReadOnlyList<BatteryDeviceSnapshot>> devices, UiState<IReadOnlyList<string>> selected) =>
        new()
        {
            Key = "devices", Label = "Devices",
            Children =
            [
                new UiProse { Key = "selection-help", Text = "Leave every device off to show all batteries." },
                // The rows are editor controls only. Macro Deck saves the updated array from this bound field.
                new UiMultiSelectInput
                {
                    Key = "selectedDeviceIds", Binding = Bind.To(selected),
                    VisibleWhen = new UiVisibleWhen { ParameterName = "showName", Values = [] }
                },
                new UiRepeat<DeviceChoice>
                {
                    Key = "device-rows",
                    Items = UiValue.From<IReadOnlyList<DeviceChoice>>(() => DeviceChoices(devices.Value, selected.Value)),
                    KeySelector = device => DeviceKey(device.Id),
                    Template = (device, key) => new UiBooleanInput
                    {
                        Key = key, Label = device.Name, Description = device.Source, Transient = true,
                        Binding = Bind.Custom(() => selected.Value.Contains(device.Id, StringComparer.Ordinal),
                            enabled => selected.Value = enabled
                                ? selected.Value.Append(device.Id).Distinct(StringComparer.Ordinal).ToArray()
                                : selected.Value.Where(id => id != device.Id).ToArray())
                    }
                },
                new UiWhen
                {
                    Key = "no-devices", Condition = () => devices.Value.Count == 0 && selected.Value.Count == 0,
                    Content = () => new UiProse { Key = "no-batteries", Text = "Macro Deck has not reported any batteries yet." }
                }
            ]
        };

    private sealed record DeviceChoice(string Id, string Name, string Source);

    private static IReadOnlyList<DeviceChoice> DeviceChoices(IReadOnlyList<BatteryDeviceSnapshot> devices, IReadOnlyList<string> selected)
    {
        var current = devices.Select(device => device.Id).ToHashSet(StringComparer.Ordinal);
        return devices.Select(device => new DeviceChoice(device.Id, device.Name,
                string.IsNullOrWhiteSpace(device.ProviderName) ? device.ProviderId : device.ProviderName))
            .OrderBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(device => device.Source, StringComparer.OrdinalIgnoreCase)
            .Concat(selected.Where(id => !current.Contains(id)).Select(id =>
                new DeviceChoice(id, "Unavailable device", id.Split(':', 2)[0])))
            .ToArray();
    }

    private static string DeviceKey(string id) =>
        "device-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)))[..12];

    private static UiTab AppearanceTab(BatteryWidgetOptions options)
    {
        var ring = options.Ring;
        var colors = options.Colors;
        var ringStyle = new UiState<string>(ring.Gauge ? "gauge" : "circle");
        var colorMode = new UiState<string>(colors.Single ? "single" : "thresholds");
        var thresholds = new UiState<UiThresholds>(colors.Thresholds);
        var lineColor = Color("lineColor", "Ring color", colors.Line, "#4caf50");
        return new UiTab
        {
            Key = "appearance", Label = "Appearance", Children =
            [
                new UiHeading { Key = "ring-heading", Text = "Ring" },
                new UiChoiceInput { Key = "ringStyle", Label = "Ring style", Binding = Bind.To(ringStyle),
                    Options = UiValue.Of<IReadOnlyList<UiOption>>([UiOption.Of("circle", "Full circle"), UiOption.Of("gauge", "Gauge")]) },
                new UiChoiceInput { Key = "gaugeAngle", Label = "Gauge angle",
                    Binding = Bind.To(new UiState<string>(ring.GaugeAngle == 180 ? "180" : "270")),
                    Disabled = UiValue.From(() => ringStyle.Value != "gauge"),
                    Options = UiValue.Of<IReadOnlyList<UiOption>>([UiOption.Of("180", "180°"), UiOption.Of("270", "270°")]) },
                Toggle("roundedEnds", "Rounded ends", ring.RoundedEnds),
                new UiNumberInput { Key = "ringThickness", Label = "Ring thickness (%)",
                    Binding = Bind.To(new UiState<double>(ring.Thickness)), Min = 1, Max = 12, Step = .5,
                    ShowSlider = true, SupportsReset = true, DefaultValue = 5.5 },
                Color("interiorColor", "Interior background", colors.Interior, "transparent"),
                Color("trackColor", "Unfilled track", colors.Track, "#42464c"),
                new UiChoiceInput { Key = "lineColorMode", Label = "Ring color mode", Binding = Bind.To(colorMode),
                    Options = UiValue.Of<IReadOnlyList<UiOption>>([UiOption.Of("single", "Single color"), UiOption.Of("thresholds", "Battery thresholds")]) },
                new UiWhen { Key = "line-color", Condition = () => colorMode.Value == "single", Content = () => lineColor },
                new UiWhen { Key = "threshold-colors", Condition = () => colorMode.Value != "single", Content = () => new UiThresholdsInput
                {
                    Key = "batteryThresholds", Label = "Battery colors and thresholds", Min = 0, Max = 100, Step = 1, Unit = "%",
                    Binding = Bind.Custom(() => thresholds.Value, value =>
                    {
                        if (value is null || WidgetColorOptions.ValidThresholds(value))
                            thresholds.Value = value ?? WidgetColorOptions.DefaultThresholds;
                    }),
                    FixedCount = true, MaxCount = 3, SupportsReset = true, DefaultValue = WidgetColorOptions.DefaultThresholds
                } },
                Color("chargingColor", "Charging ring color", colors.Charging, "#2196f3"),
                new UiHeading { Key = "labels-heading", Text = "Icons and labels" },
                Color("iconColor", "Icon color", colors.Icon, ""),
                Toggle("showName", "Show device name", options.ShowName),
                Position("namePosition", "Device name position", options.NameInside),
                Color("nameColor", "Device name color", colors.Name, ""),
                Toggle("showPercentage", "Show percentage", options.ShowPercentage),
                Position("percentagePosition", "Percentage position", !options.PercentageBelow),
                Color("percentageColor", "Percentage color", colors.Percentage, "")
            ]
        };
    }

    private static UiBooleanInput Toggle(string key, string label, bool value) =>
        new() { Key = key, Label = label, Binding = Bind.To(new UiState<bool>(value)) };

    private static UiChoiceInput Position(string key, string label, bool inside) => new()
    {
        Key = key, Label = label, Binding = Bind.To(new UiState<string>(inside ? "inside" : "below")),
        Options = UiValue.Of<IReadOnlyList<UiOption>>([UiOption.Of("inside", "Inside"), UiOption.Of("below", "Below")])
    };

    private static UiColorInput Color(string key, string label, string value, string fallback) => new()
    {
        Key = key, Label = label, Binding = Bind.To(new UiState<string>(value)), SupportsReset = true,
        DefaultValue = fallback, AllowTransparent = true
    };

    private static UiElement Card(BatteryDeviceSnapshot device, string key, BatteryWidgetOptions options)
    {
        var level = device.IsConnected && device.LevelPercent is >= 0 and <= 100 ? device.LevelPercent : null;
        var charging = device.IsConnected && device.ChargeState == BatteryChargeState.Charging;
        var reading = level is { } value ? $"{value}%" : "—";
        var status = device.IsConnected ? level is null ? "battery unavailable" : reading : "disconnected";
        if (charging) status += ", charging";
        var colors = options.Colors;
        var ring = options.Ring;
        var color = charging ? colors.Charging : colors.BatteryColor(level);
        var icon = IconFor(device.Kind);
        var content = new List<UiElement>
        {
            new UiIcon
            {
                Key = "icon", Icon = icon, Color = OptionalColor(colors.Icon), Size = UiSize.FromParent(.28, .18), MainSize = UiSize.FromParent(.30, .2),
                RequiredComponentVersion = UiIcons.VersionOf(icon),
                Fallback = new UiIcon { Key = "fallback", Icon = UiIcons.Power, Color = OptionalColor(colors.Icon), Size = UiSize.FromParent(.28, .18) }
            }
        };
        var below = new List<UiElement>();
        if (options.ShowPercentage && device.IsConnected)
            (options.PercentageBelow ? below : content).Add(BatteryText("percentage", reading, colors.Percentage, .18));
        if (options.ShowName)
            (options.NameInside ? content : below).Add(BatteryText("name", ShortName(device.Name), colors.Name, .14));

        var layers = new List<UiElement>();
        if (colors.Interior != "transparent")
            layers.Add(new UiTransform
            {
                Key = "interior", Zoom = 1 - 2 * ring.Fraction,
                Children = [new UiShape { Key = "background", Shape = UiComponentShapes.Circle, Color = colors.Interior }]
            });
        layers.Add(ring.Render((level ?? 0) / 100d, color, colors.Track, fallbackThickness: ring.Fraction * .8));
        layers.Add(new UiStack
        {
            Key = "inside", Align = UiComponentAlignments.Center, Justify = UiComponentJustify.Center, Children = content
        });
        var wheel = new UiLayer
        {
            Key = "wheel", Fill = below.Count > 0 ? UiValue.Of(true) : UiValue.None<bool>(), Children = layers
        };
        var card = new UiStack
        {
            Key = "card", Align = UiComponentAlignments.Center, Gap = UiSize.FromParent(.025, .015),
            Children = [wheel, .. below]
        };
        return new UiModifier
        {
            Key = key, Opacity = device.IsConnected ? 1 : .55,
            AccessibilityLabel = $"{ShortName(device.Name)}, {status}", Child = below.Count == 0 ? wheel : card
        };
    }

    private static UiTextRun BatteryText(string key, string text, string color, double size) => Caption(key, text) with
    {
        Color = OptionalColor(color), MainSize = UiSize.FromParent(size + .025, .13),
        Size = UiSize.FromParent(size, .13), MinSize = default
    };

    private static UiValue<string> OptionalColor(string color) =>
        color.Length == 0 ? UiValue.None<string>() : UiValue.Of(color);

    private static UiTextRun Caption(string key, UiText text) => new()
    {
        Key = key, Text = text, Align = UiComponentAlignments.Center, MaxLines = 1,
        Size = UiSize.Capped(.12, 17), MinSize = UiSize.Capped(.065, 10)
    };

    private static string ShortName(string name) => name.Length <= 32 ? name : name[..(char.IsHighSurrogate(name[30]) ? 30 : 31)] + "…";

    private static string IconFor(string kind) => kind switch
    {
        BatteryDeviceKinds.Phone => UiIcons.DevicePhone,
        BatteryDeviceKinds.Tablet => UiIcons.DeviceTablet,
        BatteryDeviceKinds.Laptop => UiIcons.DeviceLaptop,
        BatteryDeviceKinds.Watch => UiIcons.DeviceWatch,
        BatteryDeviceKinds.Headphones => UiIcons.DeviceHeadphones,
        BatteryDeviceKinds.Controller => UiIcons.DeviceGamepad,
        BatteryDeviceKinds.Mouse => UiIcons.DeviceMouse,
        BatteryDeviceKinds.Keyboard => UiIcons.DeviceKeyboard,
        BatteryDeviceKinds.Speaker => UiIcons.DeviceSpeaker,
        _ => UiIcons.Battery
    };
}

internal sealed class BatteryCatalogSession : IUiSession
{
    private readonly UiView _view;
    private readonly IBatteryApi? _batteries;
    private readonly Action? _refresh;
    private readonly object _gate = new();
    private bool _disposed;
    public event EventHandler? Changed;
    public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

    public BatteryCatalogSession(UiSurface surface, UiElement root, IBatteryApi? batteries = null, Action? refresh = null)
    {
        _view = new(surface, root);
        _view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        _view.HandlerFaulted += (_, fault) => Faulted?.Invoke(this, new(fault.Exception.Message, fault.Exception));
        _batteries = batteries;
        _refresh = refresh;
        if (_batteries is not null)
        {
            _batteries.Changed += Refresh;
            Refresh(null, null);
        }
    }

    private void Refresh(object? sender, BatteryDevicesChangedEventArgs? args)
    {
        lock (_gate)
        {
            if (!_disposed) _refresh?.Invoke();
        }
    }

    public UiTree BuildTree() => _view.Tree;
    public IReadOnlyList<UiPatch> DrainPatches() => _view.DrainPatches();
    public void Dispatch(UiEvent uiEvent) => _view.Dispatch(uiEvent);

    public ValueTask DisposeAsync()
    {
        if (_batteries is not null) _batteries.Changed -= Refresh;
        lock (_gate)
        {
            _disposed = true;
            _view.Dispose();
        }
        return ValueTask.CompletedTask;
    }
}
