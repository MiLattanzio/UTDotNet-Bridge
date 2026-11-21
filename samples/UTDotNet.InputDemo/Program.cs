using Avalonia;
using System.Globalization;
using UTDotNet.Core;
using UTDotNet.Diagnostics;

namespace UTDotNet.InputDemo;

internal static class Program
{
    public static string[] Arguments { get; private set; } = [];

    [STAThread]
    public static int Main(string[] args)
    {
        Arguments = args;
        ConfigurePhoneScale();
        if (args.Contains("--diagnostics"))
        {
            var report = new SessionBusDiagnostics().InspectAsync().GetAwaiter().GetResult();
            Console.WriteLine(EnvironmentReport.Format(new PlatformDetector().Detect(), report));
            return 0;
        }
        try { return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        catch (Exception e) { Console.Error.WriteLine($"Startup failed: {e}"); return 1; }
    }

    private static void ConfigurePhoneScale()
    {
        var environment = new PlatformDetector().Detect().MobileEnvironment;
        if (environment != LinuxMobileEnvironment.UbuntuTouch ||
            Environment.GetEnvironmentVariable("AVALONIA_SCREEN_SCALE_FACTORS") is not null ||
            Environment.GetEnvironmentVariable("AVALONIA_GLOBAL_SCALE_FACTOR") is not null) return;
        var grid = Environment.GetEnvironmentVariable("GRID_UNIT_PX");
        if (grid is null)
        {
            // Startup occurs before the UI thread exists. ADB shells do not inherit Lomiri's GRID_UNIT_PX.
            var result = new CommandRunner(TimeSpan.FromSeconds(2)).RunAsync("device-info", ["get", "GridUnit"], CancellationToken.None).GetAwaiter().GetResult();
            if (result.Succeeded) grid = result.Output.Trim();
        }
        if (PhoneDisplayScale.FromGridUnit(grid, environment) is { } scale)
        {
            Environment.SetEnvironmentVariable("AVALONIA_SCREEN_SCALE_FACTORS", scale.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine($"Ubuntu Touch scale: {scale:g} (Lomiri grid {grid}px). Explicit Avalonia overrides are respected.");
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>().UsePlatformDetect();
        if (new PlatformDetector().Detect().MobileEnvironment == LinuxMobileEnvironment.UbuntuTouch)
        {
            // Lomiri does not reliably focus independent X11 popup windows.
            // Host dropdowns in the app surface so a touch does not deactivate their owner.
            builder = builder.With(new X11PlatformOptions { OverlayPopups = true });
        }
        if (Arguments.Contains("--wayland")) builder = builder.UseWayland();
        return builder;
    }
}
