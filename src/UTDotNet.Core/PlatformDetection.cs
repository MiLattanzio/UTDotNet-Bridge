using System.Runtime.InteropServices;

namespace UTDotNet.Core;

public enum LinuxMobileEnvironment { Unknown, DesktopLinux, UbuntuTouch }
public enum DetectionConfidence { Low, Medium, High }

/// <summary>Captured facts, injectable so detection can be tested without a device.</summary>
public sealed record PlatformSnapshot
{
    public bool IsLinux { get; init; }
    public string OperatingSystem { get; init; } = "Unknown";
    public string Architecture { get; init; } = "Unknown";
    public string Runtime { get; init; } = "Unknown";
    public string OsRelease { get; init; } = "";
    public string SystemImageChannel { get; init; } = "";
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<string> Errors { get; init; } = [];
}

/// <summary>Detection is evidence-based; display endpoints do not identify an application's backend.</summary>
public sealed record PlatformInfo(
    string OperatingSystem, string Distribution, LinuxMobileEnvironment MobileEnvironment,
    string Session, string DisplayBackend, string Architecture, string Runtime,
    DetectionConfidence Confidence, IReadOnlyList<string> Evidence,
    IReadOnlyDictionary<string, string> Environment, IReadOnlyList<string> Errors);

public interface IPlatformDetector
{
    PlatformInfo Detect();
}

/// <summary>Reads a small allowlist, never the full process environment.</summary>
public static class PlatformProbe
{
    private static readonly string[] Variables =
    [
        "XDG_SESSION_TYPE", "XDG_CURRENT_DESKTOP", "XDG_SESSION_DESKTOP", "DESKTOP_SESSION",
        "DISPLAY", "WAYLAND_DISPLAY", "MIR_SOCKET", "MIR_SERVER_FILE", "QT_QPA_PLATFORM",
        "QT_IM_MODULE", "GTK_IM_MODULE", "AVALONIA_IM_MODULE", "XMODIFIERS",
        "MALIIT_FORCE_DBUS_CONNECTION", "DBUS_SESSION_BUS_ADDRESS"
        , "GRID_UNIT_PX", "AVALONIA_SCREEN_SCALE_FACTORS", "AVALONIA_GLOBAL_SCALE_FACTOR"
    ];

    public static PlatformSnapshot Capture()
    {
        var errors = new List<string>();
        string Read(string path)
        {
            try
            {
                using var reader = File.OpenText(path);
                var buffer = new char[65536];
                var count = reader.ReadBlock(buffer, 0, buffer.Length);
                return new string(buffer, 0, count);
            }
            catch (FileNotFoundException) { return ""; }
            catch (DirectoryNotFoundException) { return ""; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { errors.Add($"{path}: {e.GetType().Name}"); return ""; }
        }
        var environment = new Dictionary<string, string>();
        foreach (var name in Variables)
        {
            if (System.Environment.GetEnvironmentVariable(name) is { Length: > 0 } value)
                environment[name] = name == "DBUS_SESSION_BUS_ADDRESS" ? "<set; address redacted>" : value;
        }
        return new PlatformSnapshot
        {
            IsLinux = System.OperatingSystem.IsLinux(), OperatingSystem = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(), Runtime = RuntimeInformation.FrameworkDescription,
            Environment = environment, Errors = errors,
            OsRelease = System.OperatingSystem.IsLinux() ? Read("/etc/os-release") : "",
            SystemImageChannel = System.OperatingSystem.IsLinux() ? Read("/etc/system-image/channel.ini") : ""
        };
    }
}

public sealed class PlatformDetector(Func<PlatformSnapshot>? capture = null) : IPlatformDetector
{
    public PlatformInfo Detect() => Detect((capture ?? PlatformProbe.Capture)());

    /// <summary>Lomiri, Mir, Maliit, ARM64 or Linux alone never prove Ubuntu Touch.</summary>
    public static PlatformInfo Detect(PlatformSnapshot snapshot)
    {
        var release = ParseKeyValues(snapshot.OsRelease);
        string Env(string key) => snapshot.Environment.GetValueOrDefault(key, "");
        var id = release.GetValueOrDefault("ID", "");
        var distribution = release.GetValueOrDefault("PRETTY_NAME", id.Length > 0 ? id : "Unknown");
        var desktops = string.Join(':', Env("XDG_CURRENT_DESKTOP"), Env("XDG_SESSION_DESKTOP"), Env("DESKTOP_SESSION"))
            .Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool HasDesktop(string name) => desktops.Contains(name, StringComparer.OrdinalIgnoreCase);
        var lomiri = HasDesktop("Lomiri") || HasDesktop("Unity8") || HasDesktop("ubuntu-touch");
        var image = ParseKeyValues(snapshot.SystemImageChannel, allowColon: true);
        var channel = image.GetValueOrDefault("channel", "");
        var touchImage = channel.StartsWith("ubports/", StringComparison.OrdinalIgnoreCase)
                         || channel.StartsWith("ubuntu-touch/", StringComparison.OrdinalIgnoreCase)
                         || (image.GetValueOrDefault("base") == "system-image.ubports.com" && channel.Length > 0);
        var touchDistribution = id.Equals("ubuntu-touch", StringComparison.OrdinalIgnoreCase)
                                || id.Equals("ubports", StringComparison.OrdinalIgnoreCase)
                                || (id == "ubuntu" && release.GetValueOrDefault("VARIANT_ID") == "touch");
        var evidence = new List<string>();
        var result = LinuxMobileEnvironment.Unknown;
        var confidence = DetectionConfidence.Low;
        if (!snapshot.IsLinux) evidence.Add("Not Linux; Ubuntu Touch integration is disabled.");
        else
        {
            if (lomiri) evidence.Add("Lomiri/Ubuntu Touch session token; also possible on desktop Linux.");
            if (touchImage) evidence.Add("Ubuntu Touch/UBports system-image channel or update server.");
            if (touchDistribution || (touchImage && (lomiri || id == "ubuntu")))
            {
                result = LinuxMobileEnvironment.UbuntuTouch;
                confidence = touchDistribution || lomiri ? DetectionConfidence.High : DetectionConfidence.Medium;
                evidence.Add(touchDistribution ? "Explicit Ubuntu Touch distribution ID or Ubuntu VARIANT_ID=touch." : "System image corroborated by distribution/session.");
            }
            else if (!lomiri && !touchImage && new[] { "GNOME", "KDE", "XFCE", "Cinnamon", "MATE", "LXQt", "Unity" }.Any(HasDesktop))
            {
                result = LinuxMobileEnvironment.DesktopLinux;
                confidence = DetectionConfidence.Medium;
                evidence.Add("Recognized desktop session without Ubuntu Touch image evidence.");
            }
            else evidence.Add("Insufficient evidence; Linux or installed mobile components alone are not decisive.");
        }
        if (Env("DISPLAY").Length > 0) evidence.Add("X11 endpoint advertised; XWayland not established.");
        if (Env("WAYLAND_DISPLAY").Length > 0) evidence.Add("Wayland endpoint advertised; application backend not established.");
        return new(snapshot.OperatingSystem, distribution, result, Env("XDG_SESSION_TYPE") is { Length: > 0 } session ? session : "Unknown",
            "Unknown (not observed)", snapshot.Architecture, snapshot.Runtime, confidence, evidence,
            snapshot.Environment, snapshot.Errors);
    }

    internal static Dictionary<string, string> ParseKeyValues(string text, bool allowColon = false)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('#') || trimmed.StartsWith('[')) continue;
            var separator = trimmed.IndexOf('=');
            if (allowColon && trimmed.IndexOf(':') is var colon && colon > 0 && (separator < 0 || colon < separator)) separator = colon;
            if (separator <= 0) continue;
            var value = trimmed[(separator + 1)..].Trim();
            if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
                value = value[1..^1];
            values[trimmed[..separator].Trim()] = value;
        }
        return values;
    }
}
