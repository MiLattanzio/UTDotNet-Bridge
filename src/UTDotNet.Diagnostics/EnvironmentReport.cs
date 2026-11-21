using System.Text;
using UTDotNet.Core;

namespace UTDotNet.Diagnostics;

public static class EnvironmentReport
{
    public static string Format(PlatformInfo platform, SessionBusReport? bus = null, string? avaloniaVersion = null, string inputTransport = "not installed")
    {
        var text = new StringBuilder();
        text.AppendLine($"OS: {platform.OperatingSystem}");
        text.AppendLine($"Distribution: {platform.Distribution}");
        text.AppendLine($"Environment: {platform.MobileEnvironment} (confidence: {platform.Confidence})");
        text.AppendLine($"Architecture: {platform.Architecture} | Runtime: {platform.Runtime}");
        text.AppendLine($"Session: {platform.Session} | Application backend: {platform.DisplayBackend}");
        text.AppendLine($"Avalonia: {avaloniaVersion ?? "not loaded"}");
        text.AppendLine($"Direct OSK transport: {inputTransport} | Visibility: Unknown");
        foreach (var item in platform.Evidence) text.AppendLine($"Evidence: {item}");
        foreach (var item in platform.Environment) text.AppendLine($"{item.Key}={item.Value}");
        foreach (var error in platform.Errors) text.AppendLine($"Probe error: {error}");
        if (bus is not null)
        {
            text.AppendLine($"Session DBus: {bus.Availability}");
            text.AppendLine($"Maliit: {bus.MaliitStatus}");
            foreach (var name in bus.RelatedNames) text.AppendLine($"Observed service: {name}");
            foreach (var error in bus.Errors) text.AppendLine($"DBus diagnostic: {error}");
            foreach (var service in bus.Services)
                text.AppendLine($"{service.Name} {service.ObjectPath}:\n{service.Introspection ?? service.Error}");
        }
        return text.ToString();
    }
}
