using System.Text.RegularExpressions;

namespace UTDotNet.Diagnostics;

public enum ProbeAvailability { Unknown, Available, Unavailable }
public sealed record BusServiceReport(string Name, string ObjectPath, string? Introspection, string? Error);
public sealed record SessionBusReport(ProbeAvailability Availability, IReadOnlyList<string> RelatedNames,
    IReadOnlyList<BusServiceReport> Services, IReadOnlyList<string> Errors)
{
    public string MaliitStatus => Availability != ProbeAvailability.Available ? "Unknown (bus not inspected)" :
        RelatedNames.Any(n => n.Contains("maliit", StringComparison.OrdinalIgnoreCase))
            ? "Related bus name observed; input transport and keyboard visibility unverified"
            : "No Maliit name observed; does not rule out peer-only or compositor-mediated input";
}

/// <summary>Best-effort session-bus inventory using optional gdbus, never a keyboard controller.</summary>
public sealed class SessionBusDiagnostics(ICommandRunner? runner = null)
{
    private readonly ICommandRunner _runner = runner ?? new CommandRunner();

    public async Task<SessionBusReport> InspectAsync(CancellationToken cancellationToken = default)
    {
        var namesResult = await CallAsync("org.freedesktop.DBus", "/org/freedesktop/DBus",
            "org.freedesktop.DBus.ListNames", [], cancellationToken).ConfigureAwait(false);
        if (!namesResult.Succeeded)
            return new(namesResult.Started ? ProbeAvailability.Unavailable : ProbeAvailability.Unknown, [], [],
                [Failure(namesResult)]);
        var names = ParseNames(namesResult.Output);
        if (names is null) return new(ProbeAvailability.Unknown, [], [], ["Unrecognized gdbus ListNames response."]);
        var related = names.Where(n => new[] { "maliit", "lomiri", "unity8", "ibus", "fcitx" }
            .Any(part => n.Contains(part, StringComparison.OrdinalIgnoreCase))).Order(StringComparer.Ordinal).ToArray();
        var services = new List<BusServiceReport>();
        foreach (var name in related.OrderByDescending(n => n.Contains("maliit", StringComparison.OrdinalIgnoreCase)).Take(8))
        {
            // Resolve to a unique owner so a race cannot auto-activate a stopped service during introspection.
            var owner = await CallAsync("org.freedesktop.DBus", "/org/freedesktop/DBus",
                "org.freedesktop.DBus.GetNameOwner", [name], cancellationToken).ConfigureAwait(false);
            var match = Regex.Match(owner.Output, @"^\(':\d+\.\d+',?\s*\)\s*$");
            var path = name == "org.maliit.server" ? "/org/maliit/server/address" : "/";
            if (!owner.Succeeded || !match.Success)
            { services.Add(new(name, path, null, "Cannot resolve live owner: " + Failure(owner))); continue; }
            var uniqueName = owner.Output.Split('\'')[1];
            var info = await _runner.RunAsync("gdbus", ["introspect", "--session", "--dest", uniqueName,
                "--object-path", path, "--xml"], cancellationToken).ConfigureAwait(false);
            services.Add(new(name, path, info.Succeeded ? info.Output : null, info.Succeeded ? null : Failure(info)));
        }
        return new(ProbeAvailability.Available, related, services,
            related.Length > 8 ? ["Introspection limited to eight services; full related-name list retained."] : []);
    }

    private Task<CommandResult> CallAsync(string destination, string path, string method, string[] arguments, CancellationToken token) =>
        _runner.RunAsync("gdbus", ["call", "--session", "--dest", destination, "--object-path", path,
            "--method", method, .. arguments], token);

    private static string Failure(CommandResult result) => result.Error.Length > 0 ? result.Error : $"Command failed or returned unexpected data (exit {result.ExitCode}).";

    private static string[]? ParseNames(string value)
    {
        var list = Regex.Match(value.Trim(), @"^\(\s*(?:@as\s+)?\[(?<names>[^\]]*)\],?\s*\)$");
        if (!list.Success) return null;
        if (string.IsNullOrWhiteSpace(list.Groups["names"].Value)) return [];
        var items = list.Groups["names"].Value.Split(',').Select(s => s.Trim()).ToArray();
        if (items.Any(s => !Regex.IsMatch(s, @"^'[A-Za-z_:][A-Za-z0-9_.:\-]*'$"))) return null;
        return items.Select(s => s[1..^1]).ToArray();
    }
}
