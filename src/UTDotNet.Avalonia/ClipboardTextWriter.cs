using Avalonia.Input;
using Avalonia.Input.Platform;
using UTDotNet.Diagnostics;

namespace UTDotNet.Avalonia;

/// <summary>Opt-in bounded text writes for Ubuntu Touch's X11 clipboard manager.</summary>
public static class ClipboardTextWriter
{
    /// <summary>
    /// A pending manager acknowledgement is accepted only when Avalonia confirms that
    /// this process still owns the exact data object. This does not establish native
    /// Qt interoperability or persistence after application exit.
    /// </summary>
    public static Task<bool> TryWriteAsync(IClipboard clipboard, string text, DiagnosticLog log,
        CancellationToken cancellationToken = default) =>
        TryWriteAsync(clipboard.SetDataAsync, clipboard.TryGetInProcessDataAsync, text, log,
            TimeSpan.FromMilliseconds(500), cancellationToken);

    internal static async Task<bool> TryWriteAsync(Func<IAsyncDataTransfer, Task> write,
        Func<Task<IAsyncDataTransfer?>> getOwned, string text, DiagnosticLog log, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(text));
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pending = write(data);
            // WaitAsync cannot cancel the platform write. Observe a later fault without
            // retaining an editor or allowing a late completion to mutate its selection.
            _ = pending.ContinueWith(task =>
            {
                _ = task.Exception;
                log.Write("ClipboardWrite", "Platform write faulted; content and error details redacted.");
            }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            try
            {
                await pending.WaitAsync(timeout, cancellationToken);
                log.Write("ClipboardWrite", "Write completed; content redacted.");
                return true;
            }
            catch (TimeoutException)
            {
                var owned = await getOwned().WaitAsync(timeout, cancellationToken);
                if (ReferenceEquals(owned, data))
                {
                    log.Write("ClipboardWrite", "Exact in-process ownership confirmed; manager acknowledgement pending. Native interoperability and persistence unconfirmed.");
                    return true;
                }
                log.Write("ClipboardWrite", "Write timed out without confirmed ownership; source text retained.");
                return false;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return false; }
        catch (Exception error)
        {
            log.Write("ClipboardWrite", $"Write unconfirmed: {error.GetType().Name}; source text retained; content redacted.");
            return false;
        }
    }
}
