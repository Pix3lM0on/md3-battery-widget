using BatteryWidget;
using MacroDeck.Plugin.Hosting;

await MacroDeckPlugin.CreatePlugin(args)
    .RegisterIntegration<BatteryWidgetIntegration>()
    .Build()
    .RunAsync();
