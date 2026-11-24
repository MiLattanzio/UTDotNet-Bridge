namespace UTDotNet.Core;

/// <summary>Explicit, asynchronous plain-text clipboard operations initiated by the user.</summary>
public interface ITextClipboard
{
    /// <summary>Returns true only when the clipboard service accepts the text.</summary>
    Task<bool> WriteTextAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Returns null when text is unavailable or access fails. Does not return cached text.</summary>
    Task<string?> ReadTextAsync(CancellationToken cancellationToken = default);
}
