namespace UTDotNet.Core;

public enum InputType { Text, Password, Email, Numeric, Phone, Url, Search }
public enum KeyboardVisibility { Unknown, Hidden, Visible }
public enum InputOperationStatus { Unsupported, Unavailable, RequestedUnconfirmed, Confirmed }

/// <summary>Input hints, not a guarantee of the system keyboard's layout or behavior.</summary>
public sealed record InputRequest
{
    public InputType InputType { get; init; }
    public bool Multiline { get; init; }
    public bool AutoCorrect { get; init; }
    public bool AutoCapitalization { get; init; }

    /// <summary>Passwords must never request prediction or automatic capitalization.</summary>
    public InputRequest Normalize()
    {
        if (!Enum.IsDefined(InputType)) throw new ArgumentOutOfRangeException(nameof(InputType));
        return InputType == InputType.Password ? this with { AutoCorrect = false, AutoCapitalization = false, Multiline = false } : this;
    }
}

public sealed record InputOperationResult(InputOperationStatus Status, KeyboardVisibility Visibility, string Detail);

/// <summary>Direct OSK control. Availability is independent of ordinary framework text input.</summary>
public interface IPlatformInputService
{
    bool IsAvailable { get; }
    KeyboardVisibility Visibility { get; }
    Task<InputOperationResult> ShowKeyboardAsync(InputRequest request, CancellationToken cancellationToken = default);
    Task<InputOperationResult> HideKeyboardAsync(CancellationToken cancellationToken = default);
    Task<InputOperationResult> SetInputTypeAsync(InputType inputType, CancellationToken cancellationToken = default);
}

/// <summary>Honest Phase 1 fallback: no native command is sent and no visibility is inferred.</summary>
public sealed class UnsupportedPlatformInputService : IPlatformInputService
{
    public bool IsAvailable => false;
    public KeyboardVisibility Visibility => KeyboardVisibility.Unknown;
    public Task<InputOperationResult> ShowKeyboardAsync(InputRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        _ = request.Normalize();
        return Result(cancellationToken);
    }
    public Task<InputOperationResult> HideKeyboardAsync(CancellationToken cancellationToken = default) => Result(cancellationToken);
    public Task<InputOperationResult> SetInputTypeAsync(InputType inputType, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(inputType)) throw new ArgumentOutOfRangeException(nameof(inputType));
        return Result(cancellationToken);
    }
    private static Task<InputOperationResult> Result(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(new InputOperationResult(InputOperationStatus.Unsupported, KeyboardVisibility.Unknown,
            "No direct OSK transport installed. Avalonia's normal input handling remains active; no native command sent."));
    }
}
