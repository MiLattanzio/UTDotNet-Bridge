using UTDotNet.Diagnostics;

namespace UTDotNet.Tests;

public class DiagnosticsTests
{
    private sealed class FakeRunner(params CommandResult[] results) : ICommandRunner
    {
        private readonly Queue<CommandResult> _results = new(results);
        public List<string[]> Calls { get; } = [];
        public Task<CommandResult> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Calls.Add(arguments.ToArray()); return Task.FromResult(_results.Dequeue()); }
    }
    private static CommandResult Ok(string output) => new(true, 0, output, "");
    [Fact]
    public async Task MissingToolIsUnknownNotMissingMaliit()
    {
        var report = await new SessionBusDiagnostics(new FakeRunner(new CommandResult(false, null, "", "missing gdbus"))).InspectAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ProbeAvailability.Unknown, report.Availability);
        Assert.StartsWith("Unknown", report.MaliitStatus);
    }
    [Fact]
    public async Task InaccessibleBusDoesNotCrash()
    {
        var report = await new SessionBusDiagnostics(new FakeRunner(new CommandResult(true, 1, "", "AccessDenied"))).InspectAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ProbeAvailability.Unavailable, report.Availability);
        Assert.Contains("AccessDenied", report.Errors);
    }
    [Fact]
    public async Task MissingMaliitDoesNotMeanKeyboardUnavailable()
    {
        var report = await new SessionBusDiagnostics(new FakeRunner(Ok("(['org.freedesktop.DBus', ':1.5'],)"))).InspectAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ProbeAvailability.Available, report.Availability);
        Assert.Contains("does not rule out", report.MaliitStatus);
    }
    [Fact]
    public async Task IntrospectionUsesVerifiedPathAndUniqueOwner()
    {
        var runner = new FakeRunner(Ok("(['org.maliit.server'],)"), Ok("(':1.25',)"), Ok("<node><interface name='org.maliit.Server.Address'/></node>"));
        var report = await new SessionBusDiagnostics(runner).InspectAsync(TestContext.Current.CancellationToken);
        Assert.Contains("/org/maliit/server/address", runner.Calls[2]);
        Assert.Contains(":1.25", runner.Calls[2]);
        Assert.DoesNotContain("org.maliit.server", runner.Calls[2]);
        Assert.NotNull(Assert.Single(report.Services).Introspection);
    }
    [Fact]
    public async Task VanishingServiceIsReportedWithoutActivation()
    {
        var runner = new FakeRunner(Ok("(['org.maliit.server'],)"), new(true, 1, "", "NameHasNoOwner"));
        var report = await new SessionBusDiagnostics(runner).InspectAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, runner.Calls.Count);
        Assert.Contains("NameHasNoOwner", Assert.Single(report.Services).Error!);
    }
    [Fact]
    public async Task MalformedReplyIsUnknown()
    {
        var report = await new SessionBusDiagnostics(new FakeRunner(Ok("unexpected garbage"))).InspectAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ProbeAvailability.Unknown, report.Availability);
    }
    [Fact]
    public async Task MissingExecutableIsHandled() => Assert.False((await new CommandRunner().RunAsync("utdotnet-nonexistent-command", [], TestContext.Current.CancellationToken)).Started);

    [Fact]
    public async Task TimeoutTerminatesProcess()
    {
        if (!OperatingSystem.IsLinux()) return;
        var result = await new CommandRunner(TimeSpan.FromMilliseconds(100)).RunAsync("sleep", ["10"], TestContext.Current.CancellationToken);
        Assert.Contains("timed out", result.Error);
        Assert.Null(result.ExitCode);
    }
    [Fact]
    public async Task CallerCancellationPropagates()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CommandRunner().RunAsync("sleep", ["10"], cancellation.Token));
    }
    [Fact]
    public async Task OutputIsBounded()
    {
        if (!OperatingSystem.IsLinux()) return;
        var result = await new CommandRunner(outputLimit: 8).RunAsync("printf", ["123456789012345"], TestContext.Current.CancellationToken);
        Assert.Equal(8, result.Output.Length);
        Assert.Contains("limit exceeded", result.Error);
    }
}
