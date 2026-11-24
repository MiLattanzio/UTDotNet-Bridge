using Tmds.DBus.Protocol;
using UTDotNet.Core;
using UTDotNet.Diagnostics;

namespace UTDotNet.UbuntuTouch;

/// <summary>
/// Plain-text clipboard access through Lomiri Content Hub. Call from the focused
/// application, in response to Copy/Cut/Paste. The service enforces caller focus.
/// No Qt dependency, external helper, background clipboard polling or X11 cache.
/// </summary>
public sealed class ContentHubClipboard : ITextClipboard
{
    private const string Service = "com.lomiri.content.dbus.Service";
    private readonly DiagnosticLog _log;
    private readonly string _appId;
    private readonly string _address;
    private readonly TimeSpan _timeout;

    public ContentHubClipboard(DiagnosticLog log)
        : this(log, DBusAddress.Session ?? "", Environment.GetEnvironmentVariable("APP_ID") ?? "", TimeSpan.FromSeconds(2)) { }

    internal ContentHubClipboard(DiagnosticLog log, string address, string appId, TimeSpan timeout)
    { _log = log; _address = address; _appId = appId; _timeout = timeout; }

    public async Task<bool> WriteTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        return await ExecuteAsync(async connection =>
        {
            var data = ContentHubTextCodec.Encode(text);
            MessageBuffer message;
            using (var writer = connection.GetMessageWriter())
            {
                writer.WriteMethodCallHeader(destination: Service, path: "/", @interface: Service,
                    member: "CreatePaste", signature: "ssayas");
                writer.WriteString(_appId);
                // Empty surface ID selects Content Hub's normal caller-PID focus check,
                // also used by Wayland clients. Never impersonate a shell/Mir surface.
                writer.WriteString("");
                writer.WriteArray(data);
                writer.WriteArray(new[] { "text/plain" });
                message = writer.CreateMessage();
            }
            var accepted = await connection.CallMethodAsync(message,
                static (reply, _) => reply.GetBodyReader().ReadBool()).ConfigureAwait(false);
            _log.Write("NativeClipboard", accepted ? "Write accepted by Content Hub; content redacted." :
                "Write rejected by Content Hub; source text retained.");
            return accepted;
        }, false, cancellationToken).ConfigureAwait(false);
    }

    public Task<string?> ReadTextAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync<string?>(async connection =>
        {
            MessageBuffer message;
            using (var writer = connection.GetMessageWriter())
            {
                writer.WriteMethodCallHeader(destination: Service, path: "/", @interface: Service,
                    member: "GetLatestPasteData", signature: "s");
                writer.WriteString("");
                message = writer.CreateMessage();
            }
            var bytes = await connection.CallMethodAsync(message,
                static (reply, _) => reply.GetBodyReader().ReadArrayOfByte()).ConfigureAwait(false);
            var text = ContentHubTextCodec.Decode(bytes);
            _log.Write("NativeClipboard", text is null ? "No readable plain text returned; editor unchanged." :
                "Plain text received from Content Hub; content redacted.");
            return text;
        }, null, cancellationToken);

    private async Task<T> ExecuteAsync<T>(Func<DBusConnection, Task<T>> action, T unavailable, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(_timeout);
        try
        {
            timeout.Token.ThrowIfCancellationRequested();
            if (!_address.StartsWith("unix:", StringComparison.Ordinal) || _address.Contains(';'))
                throw new InvalidOperationException("A single local session bus is required.");
            using var connection = new DBusConnection(_address);
            await connection.ConnectAsync().AsTask().WaitAsync(timeout.Token).ConfigureAwait(false);
            var pending = action(connection);
            // Disposing the connection on timeout aborts the transport; a late failure
            // is observed and must never trigger a deferred editor mutation.
            _ = pending.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return await pending.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return unavailable; }
        catch (Exception error)
        {
            _log.Write("NativeClipboard", $"Operation unavailable: {error.GetType().Name}; content and error details redacted.");
            return unavailable;
        }
    }
}
