using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using UTDotNet.Core;
using UTDotNet.Diagnostics;

namespace UTDotNet.Avalonia;

/// <summary>Opt-in observer for one TextBox. Does not handle events or replace Avalonia's IME.</summary>
public sealed class TextInputAdapter : IDisposable
{
    private readonly TextBox _textBox;
    private readonly DiagnosticLog _log;
    private readonly string _id;
    private TextInputMethodClient? _client;
    private TextPresenter? _presenter;
    private bool _disposed;
    public InputRequest Request { get; private set; }
    public bool IsFocused => _textBox.IsFocused;

    public TextInputAdapter(TextBox textBox, string id, InputRequest request, DiagnosticLog log)
    {
        ArgumentNullException.ThrowIfNull(textBox);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(log);
        _textBox = textBox;
        _id = id;
        _log = log;
        Request = request.Normalize();
        ApplyHints();
        _textBox.GotFocus += OnGotFocus;
        _textBox.LostFocus += OnLostFocus;
        _textBox.AddHandler(InputElement.PointerPressedEvent, OnPointer, RoutingStrategies.Tunnel, handledEventsToo: true);
        _textBox.AddHandler(InputElement.TextInputEvent, OnTextInput, RoutingStrategies.Tunnel, handledEventsToo: true);
        _textBox.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        _textBox.AddHandler(InputElement.TextInputMethodClientRequestedEvent, OnClientRequested, RoutingStrategies.Bubble, handledEventsToo: true);
        _textBox.PropertyChanged += OnPropertyChanged;
        _textBox.AttachedToVisualTree += OnAttached;
        _textBox.DetachedFromVisualTree += OnDetached;
        ObservePresenter();
    }

    /// <summary>Applies framework hints only. Native layout/correction support remains backend-dependent.</summary>
    public void UpdateRequest(InputRequest request)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Request = request.Normalize();
        ApplyHints();
        Write("InputTypeChanged", $"{Request.InputType}; hints applied, native effect unconfirmed");
    }

    private void ApplyHints()
    {
        TextInputOptions.SetContentType(_textBox, Request.InputType switch
        {
            InputType.Password => TextInputContentType.Password,
            InputType.Email => TextInputContentType.Email,
            InputType.Numeric => TextInputContentType.Number,
            InputType.Phone => TextInputContentType.Digits,
            InputType.Url => TextInputContentType.Url,
            InputType.Search => TextInputContentType.Search,
            _ => TextInputContentType.Normal
        });
        TextInputOptions.SetMultiline(_textBox, Request.Multiline);
        TextInputOptions.SetIsSensitive(_textBox, Request.InputType == InputType.Password);
        TextInputOptions.SetAutoCapitalization(_textBox, Request.AutoCapitalization);
        TextInputOptions.SetShowSuggestions(_textBox, Request.AutoCorrect);
        TextInputOptions.SetReturnKeyType(_textBox, Request.InputType == InputType.Search ? TextInputReturnKeyType.Search : TextInputReturnKeyType.Default);
    }

    private bool Sensitive => Request.InputType == InputType.Password || _textBox.PasswordChar != '\0' || TextInputOptions.GetIsSensitive(_textBox);
    private void Write(string category, string detail) => _log.Write(category, $"{_id}: {detail}");
    private void OnGotFocus(object? sender, FocusChangedEventArgs e)
    {
        ObservePresenter();
        Write("InputTypeChanged", $"active hints={Request.InputType}; native effect unconfirmed");
        Write("FocusGained", $"method={e.NavigationMethod}; type={Request.InputType}; visibility=Unknown");
    }
    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        DetachClient();
        Write("FocusLost", "context no longer focused; visibility=Unknown");
    }
    private void OnPointer(object? sender, PointerPressedEventArgs e) => Write("PointerPressed", $"{e.Pointer.Type}; alreadyFocused={IsFocused}");
    private void OnTextInput(object? sender, TextInputEventArgs e) => Write("TextInput", Sensitive ? "sensitive content redacted" : $"characters={e.Text?.Length ?? 0}; source may be key or IME commit");
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Never log character keys: even names can reconstruct a password.
        if (!Sensitive && e.Key is Key.Back or Key.Delete or Key.Enter or Key.Escape)
            Write("EditingKey", e.Key.ToString());
    }
    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.TextProperty)
            Write("TextChanged", Sensitive ? "sensitive content redacted" : $"length={_textBox.Text?.Length ?? 0}");
        else if (!Sensitive && (e.Property == TextBox.SelectionStartProperty || e.Property == TextBox.SelectionEndProperty))
            Write("SelectionChanged", $"start={_textBox.SelectionStart}; end={_textBox.SelectionEnd}");
    }
    private void OnClientRequested(object? sender, TextInputMethodClientRequestedEventArgs e)
    {
        DetachClient();
        _client = e.Client;
        Write("InputContext", _client is null ? "client not observable at this route" : $"client provided; preedit={_client.SupportsPreedit}; surrounding={_client.SupportsSurroundingText}");
        if (_client is null) return;
        _client.InputPaneActivationRequested += OnPaneRequested;
        _client.ResetRequested += OnReset;
        _client.CursorRectangleChanged += OnCursorChanged;
    }
    private void OnPaneRequested(object? sender, EventArgs e) => Write("FrameworkKeyboardRequest", "activation requested by client; visibility=Unknown");
    private void OnReset(object? sender, EventArgs e) => Write("InputContextReset", "framework reset requested");
    private void OnCursorChanged(object? sender, EventArgs e)
    {
        if (!Sensitive) Write("CursorChanged", "input client cursor rectangle changed");
    }
    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e) => ObservePresenter();
    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e) { DetachClient(); DetachPresenter(); }
    private void ObservePresenter()
    {
        var presenter = _textBox.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault();
        if (ReferenceEquals(presenter, _presenter)) return;
        DetachPresenter();
        _presenter = presenter;
        if (_presenter is not null) _presenter.PropertyChanged += OnPresenterChanged;
    }
    private void OnPresenterChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextPresenter.PreeditTextProperty) Write("PreeditChanged", "framework presenter changed; content redacted");
    }
    private void DetachPresenter()
    {
        if (_presenter is not null) _presenter.PropertyChanged -= OnPresenterChanged;
        _presenter = null;
    }
    private void DetachClient()
    {
        if (_client is null) return;
        _client.InputPaneActivationRequested -= OnPaneRequested;
        _client.ResetRequested -= OnReset;
        _client.CursorRectangleChanged -= OnCursorChanged;
        _client = null;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DetachClient();
        DetachPresenter();
        _textBox.GotFocus -= OnGotFocus;
        _textBox.LostFocus -= OnLostFocus;
        _textBox.RemoveHandler(InputElement.PointerPressedEvent, OnPointer);
        _textBox.RemoveHandler(InputElement.TextInputEvent, OnTextInput);
        _textBox.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        _textBox.RemoveHandler(InputElement.TextInputMethodClientRequestedEvent, OnClientRequested);
        _textBox.PropertyChanged -= OnPropertyChanged;
        _textBox.AttachedToVisualTree -= OnAttached;
        _textBox.DetachedFromVisualTree -= OnDetached;
    }
}
