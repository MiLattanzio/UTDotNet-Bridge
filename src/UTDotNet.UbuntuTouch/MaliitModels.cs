using Tmds.DBus.Protocol;
using UTDotNet.Core;

namespace UTDotNet.UbuntuTouch;

/// <summary>Pixel rectangle in the coordinate system reported by Maliit.</summary>
public readonly record struct InputArea(int X, int Y, int Width, int Height);

/// <summary>Immutable focus state. Sensitive text is removed again at the transport boundary.</summary>
public sealed record MaliitWidgetState(InputRequest Request, string SurroundingText, int Cursor, int Anchor,
    ulong WindowId, InputArea CursorRectangle, bool Sensitive = false)
{
    public bool IsSensitive => Sensitive || Request.InputType == InputType.Password;
    public string SelectedText => IsSensitive ? "" : SurroundingText[Math.Clamp(Math.Min(Cursor, Anchor), 0, SurroundingText.Length)..Math.Clamp(Math.Max(Cursor, Anchor), 0, SurroundingText.Length)];

    /// <summary>Verified against Maliit MInputContext::getStateInformation and Qt 5.15 InputMethodHint.</summary>
    public Dictionary<string, VariantValue> ToProperties(bool focused = true)
    {
        var input = Request.Normalize();
        var hints = (!input.AutoCapitalization ? 0x4 : 0) | (!input.AutoCorrect ? 0x40 : 0) | (input.Multiline ? 0x400 : 0);
        hints |= input.InputType switch { InputType.Numeric => 0x20000, InputType.Phone => 0x100000,
            InputType.Email => 0x200000, InputType.Url => 0x400000, _ => 0 };
        if (IsSensitive) hints |= 0x1 | 0x2 | 0x4 | 0x40;
        var properties = new Dictionary<string, VariantValue>
        {
            ["focusState"] = focused,
            ["contentType"] = input.InputType switch { InputType.Numeric => 1, InputType.Phone => 2, InputType.Email => 3, InputType.Url => 4, _ => 0 },
            ["hiddenText"] = IsSensitive,
            ["predictionEnabled"] = !IsSensitive && input.AutoCorrect,
            ["autocapitalizationEnabled"] = !IsSensitive && input.AutoCapitalization,
            ["maliit-inputmethod-hints"] = hints,
            ["enterKeyType"] = input.InputType == InputType.Search ? 5 : input.Multiline ? 1 : 0,
            ["hasSelection"] = !IsSensitive && Cursor != Anchor,
            ["winId"] = WindowId,
            ["cursorRectangle"] = new Struct<int, int, int, int>(CursorRectangle.X, CursorRectangle.Y, CursorRectangle.Width, CursorRectangle.Height),
            ["toolbarId"] = 0
        };
        // Omit, rather than mask, surrounding text for passwords. Never expose secret length/offsets.
        if (!IsSensitive)
        {
            properties["surroundingText"] = SurroundingText;
            properties["cursorPosition"] = Math.Clamp(Cursor, 0, SurroundingText.Length);
            properties["anchorPosition"] = Math.Clamp(Anchor, 0, SurroundingText.Length);
        }
        return properties;
    }
}

/// <summary>Wire callbacks. Text payloads must never be written to diagnostics.</summary>
public abstract record MaliitEvent;
public sealed record CommitText(string Text, int ReplacementStart, int ReplacementLength, int CursorPosition) : MaliitEvent;
public sealed record PreeditText(string Text, int ReplacementStart, int ReplacementLength, int CursorPosition, int FormatCount) : MaliitEvent;
public sealed record InputKey(int Type, int Key, int Modifiers, string Text, bool Repeat, int Count, byte RequestType) : MaliitEvent;
public sealed record InputSelection(int Start, int Length) : MaliitEvent;
public sealed record InputAreaChanged(InputArea Area) : MaliitEvent;
public sealed record InputHidden : MaliitEvent;
public sealed record InputActivationLost : MaliitEvent;
public sealed record InputDisconnected : MaliitEvent;

/// <summary>One connection belongs to one focus lease; disposing it invalidates all its callbacks.</summary>
public interface IMaliitSession : IDisposable
{
    /// <summary>Apply the callback before acknowledging it. Must not await another peer call; returns current safe state.</summary>
    Func<MaliitEvent, Task<MaliitWidgetState?>>? DispatchAsync { get; set; }
    bool IsConnected { get; }
    Task OpenAsync(MaliitWidgetState state, CancellationToken cancellationToken);
    Task UpdateAsync(MaliitWidgetState state, CancellationToken cancellationToken);
    Task ShowAsync(CancellationToken cancellationToken);
    Task HideAsync(CancellationToken cancellationToken);
    Task ResetAsync(CancellationToken cancellationToken);
}
