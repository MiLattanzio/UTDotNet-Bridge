using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using Avalonia.Threading;
using UTDotNet.Core;
using UTDotNet.Diagnostics;

namespace UTDotNet.Avalonia;

/// <summary>
/// Opt-in Copy/Cut handling for Ubuntu Touch X11. Use on the UI thread and dispose
/// with the field. The optional text clipboard supplies native Copy/Cut/Paste;
/// without it, Paste remains Avalonia's normal operation.
/// </summary>
public sealed class TextClipboardBridge : IDisposable
{
    private readonly TextBox _field;
    private readonly DiagnosticLog _log;
    private readonly Func<string, CancellationToken, Task<bool>> _write;
    private readonly Func<CancellationToken, Task<string?>>? _read;
    private readonly CancellationTokenSource _lifetime = new();
    private int _revision;
    private bool _busy;
    private bool _disposed;

    public TextClipboardBridge(TextBox field, DiagnosticLog log)
        : this(field, log, (text, token) => TopLevel.GetTopLevel(field)?.Clipboard is { } clipboard
            ? ClipboardTextWriter.TryWriteAsync(clipboard, text, log, token) : Task.FromResult(false)) { }

    /// <summary>Use an explicit clipboard, such as ContentHubClipboard on Ubuntu Touch.</summary>
    public TextClipboardBridge(TextBox field, DiagnosticLog log, ITextClipboard clipboard)
        : this(field, log, clipboard.WriteTextAsync, clipboard.ReadTextAsync) { }

    internal TextClipboardBridge(TextBox field, DiagnosticLog log, Func<string, CancellationToken, Task<bool>> write,
        Func<CancellationToken, Task<string?>>? read = null)
    {
        _field = field; _log = log; _write = write; _read = read;
        field.CopyingToClipboard += OnCopy;
        field.CuttingToClipboard += OnCut;
        if (read is not null) field.PastingFromClipboard += OnPaste;
        field.PropertyChanged += OnChanged;
        field.LostFocus += OnLostFocus;
        field.DetachedFromVisualTree += OnDetached;
    }

    private bool Sensitive => _field.PasswordChar != '\0' || TextInputOptions.GetIsSensitive(_field);
    private async void OnCopy(object? sender, RoutedEventArgs e)
    { if (!e.Handled) { e.Handled = true; await ExecuteAsync(false); } }
    private async void OnCut(object? sender, RoutedEventArgs e)
    { if (!e.Handled) { e.Handled = true; await ExecuteAsync(true); } }

    private async void OnPaste(object? sender, RoutedEventArgs e)
    {
        if (e.Handled) return;
        e.Handled = true;
        if (_disposed || _busy || _read is null || !_field.CanPaste || !_field.IsEffectivelyEnabled) return;
        if (TopLevel.GetTopLevel(_field) is not Window window || !window.IsActive) return;
        var revision = _revision;
        void Deactivated(object? s, EventArgs args) => _revision++;
        window.Deactivated += Deactivated;
        _busy = true;
        try
        {
            var text = await _read(_lifetime.Token);
            // Empty/unavailable/non-text data must not delete the selected text, and
            // failures must not fall back to the stale X11 clipboard.
            if (string.IsNullOrEmpty(text)) return;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed || _revision != revision || !_field.CanPaste || !_field.IsEffectivelyEnabled ||
                    !_field.IsFocused || !window.IsActive || TopLevel.GetTopLevel(_field) != window)
                {
                    _log.Write("ClipboardPaste", "Editor changed while reading; editor unchanged.");
                    return;
                }
                _field.SelectedText = text; // Public editing path applies MaxLength, newline rules and undo.
                _log.Write("ClipboardPaste", "Native clipboard text inserted; content redacted.");
            }, DispatcherPriority.Input);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error)
        { _log.Write("ClipboardPaste", $"Operation unconfirmed: {error.GetType().Name}; content redacted."); }
        finally { window.Deactivated -= Deactivated; _busy = false; }
    }

    private async Task ExecuteAsync(bool cut)
    {
        if (_disposed || _busy || Sensitive || !_field.IsEffectivelyEnabled || (cut ? !_field.CanCut : !_field.CanCopy)) return;
        var window = TopLevel.GetTopLevel(_field) as Window;
        if (window is null || !window.IsActive) return;
        var revision = _revision;
        void Deactivated(object? s, EventArgs args) => _revision++;
        window.Deactivated += Deactivated;
        var text = _field.SelectedText;
        _busy = true;
        try
        {
            if (!await _write(text, _lifetime.Token) || !cut) return;
            // A menu may return focus after its Click handler. Never edit inline while
            // that transfer is still in progress, even if the clipboard completed inline.
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed || _revision != revision || Sensitive || !_field.CanCut || !_field.IsEffectivelyEnabled ||
                    !_field.IsFocused || !window.IsActive || TopLevel.GetTopLevel(_field) != window)
                {
                    _log.Write("ClipboardCut", "Editor changed while copying; source text retained.");
                    return;
                }
                _field.SelectedText = ""; // Public editing API preserves Avalonia's undo stack.
                _log.Write("ClipboardCut", "Selection removed after confirmed clipboard write; content redacted.");
            }, DispatcherPriority.Input);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error)
        { _log.Write("ClipboardCut", $"Operation unconfirmed: {error.GetType().Name}; content redacted."); }
        finally { window.Deactivated -= Deactivated; _busy = false; }
    }

    private void OnChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.TextProperty || e.Property == TextBox.SelectionStartProperty ||
            e.Property == TextBox.SelectionEndProperty || e.Property == TextBox.IsReadOnlyProperty ||
            e.Property == TextBox.PasswordCharProperty || e.Property == TextInputOptions.IsSensitiveProperty)
            _revision++;
    }
    private void OnLostFocus(object? sender, FocusChangedEventArgs e) => _revision++;
    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e) => _revision++;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel();
        _field.CopyingToClipboard -= OnCopy; _field.CuttingToClipboard -= OnCut;
        _field.PastingFromClipboard -= OnPaste;
        _field.PropertyChanged -= OnChanged; _field.LostFocus -= OnLostFocus;
        _field.DetachedFromVisualTree -= OnDetached;
        _lifetime.Dispose();
    }
}
