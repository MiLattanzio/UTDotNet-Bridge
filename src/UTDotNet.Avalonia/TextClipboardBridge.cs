using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using Avalonia.Threading;
using UTDotNet.Diagnostics;

namespace UTDotNet.Avalonia;

/// <summary>
/// Opt-in Copy/Cut handling for Ubuntu Touch X11. Use on the UI thread and dispose
/// with the field. Paste remains Avalonia's normal operation; this is not a native clipboard adapter.
/// </summary>
public sealed class TextClipboardBridge : IDisposable
{
    private readonly TextBox _field;
    private readonly DiagnosticLog _log;
    private readonly Func<string, CancellationToken, Task<bool>> _write;
    private readonly CancellationTokenSource _lifetime = new();
    private int _revision;
    private bool _busy;
    private bool _disposed;

    public TextClipboardBridge(TextBox field, DiagnosticLog log)
        : this(field, log, (text, token) => TopLevel.GetTopLevel(field)?.Clipboard is { } clipboard
            ? ClipboardTextWriter.TryWriteAsync(clipboard, text, log, token) : Task.FromResult(false)) { }

    internal TextClipboardBridge(TextBox field, DiagnosticLog log, Func<string, CancellationToken, Task<bool>> write)
    {
        _field = field; _log = log; _write = write;
        field.CopyingToClipboard += OnCopy;
        field.CuttingToClipboard += OnCut;
        field.PropertyChanged += OnChanged;
        field.LostFocus += OnLostFocus;
        field.DetachedFromVisualTree += OnDetached;
    }

    private bool Sensitive => _field.PasswordChar != '\0' || TextInputOptions.GetIsSensitive(_field);
    private async void OnCopy(object? sender, RoutedEventArgs e)
    { if (!e.Handled) { e.Handled = true; await ExecuteAsync(false); } }
    private async void OnCut(object? sender, RoutedEventArgs e)
    { if (!e.Handled) { e.Handled = true; await ExecuteAsync(true); } }

    private async Task ExecuteAsync(bool cut)
    {
        if (_disposed || _busy || Sensitive || (cut ? !_field.CanCut : !_field.CanCopy)) return;
        var window = TopLevel.GetTopLevel(_field) as Window;
        if (window is null) return;
        var revision = _revision;
        var text = _field.SelectedText;
        _busy = true;
        try
        {
            if (!await _write(text, _lifetime.Token) || !cut) return;
            // A menu may return focus after its Click handler. Never edit inline while
            // that transfer is still in progress, even if the clipboard completed inline.
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed || _revision != revision || Sensitive || !_field.CanCut ||
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
        finally { _busy = false; }
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
        _field.PropertyChanged -= OnChanged; _field.LostFocus -= OnLostFocus;
        _field.DetachedFromVisualTree -= OnDetached;
        _lifetime.Dispose();
    }
}
