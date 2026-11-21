using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace UTDotNet.Diagnostics;

public sealed record CommandResult(bool Started, int? ExitCode, string Output, string Error)
{
    public bool Succeeded => Started && ExitCode == 0 && Error.Length == 0;
}

/// <summary>Small process boundary for read-only diagnostics and deterministic failure tests.</summary>
public interface ICommandRunner
{
    Task<CommandResult> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

/// <summary>No shell, bounded output, timeout, and child cleanup on caller cancellation.</summary>
public sealed class CommandRunner(TimeSpan? timeout = null, int outputLimit = 65536) : ICommandRunner
{
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(3);
    private readonly int _outputLimit = outputLimit > 0 ? outputLimit : throw new ArgumentOutOfRangeException(nameof(outputLimit));

    public async Task<CommandResult> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);
        using var process = new Process { StartInfo = new ProcessStartInfo(executable)
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true } };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.StartInfo.Environment["LC_ALL"] = "C";
        try { process.Start(); }
        catch (Exception e) when (e is Win32Exception or IOException or UnauthorizedAccessException)
        { return new(false, null, "", $"Cannot start {executable}: {e.Message}"); }

        var stdout = ReadBoundedAsync(process.StandardOutput, deadline.Token);
        var stderr = ReadBoundedAsync(process.StandardError, deadline.Token);
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(deadline.Token), stdout, stderr).ConfigureAwait(false);
            var output = await stdout.ConfigureAwait(false);
            var error = await stderr.ConfigureAwait(false);
            if (output.Truncated || error.Truncated)
                return new(true, process.ExitCode, output.Text, "Diagnostic output limit exceeded; result is incomplete.");
            return new(true, process.ExitCode, output.Text, error.Text.Trim());
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            // Observe both stream tasks even when the process was cancelled.
            try { await Task.WhenAll(stdout, stderr).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            cancellationToken.ThrowIfCancellationRequested();
            return new(true, null, "", $"Diagnostic command timed out after {_timeout.TotalSeconds:g} seconds.");
        }
    }

    private async Task<(string Text, bool Truncated)> ReadBoundedAsync(StreamReader reader, CancellationToken token)
    {
        var buffer = new char[2048];
        var result = new StringBuilder();
        var truncated = false;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
        {
            var kept = Math.Min(count, _outputLimit - result.Length);
            result.Append(buffer, 0, kept);
            truncated |= kept < count;
        }
        return (result.ToString(), truncated);
    }
}
