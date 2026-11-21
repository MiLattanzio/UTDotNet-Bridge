using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using UTDotNet.Core;
using UTDotNet.Diagnostics;
using UTDotNet.UbuntuTouch;

namespace UTDotNet.Avalonia;

/// <summary>Opt-in Maliit bridge for registered TextBoxes. Construct and use only on the UI thread.</summary>
public sealed class MaliitInputBridge : IPlatformInputService, IDisposable
{
    private sealed record Registration(TextBox Field, InputRequest Request);
    private sealed class Lease(Registration registration, TextInputMethodClient client, IMaliitSession session, bool originalIme)
    {
        public Registration Registration { get; } = registration;
        public TextInputMethodClient Client { get; } = client;
        public IMaliitSession Session { get; } = session;
        public bool OriginalIme { get; } = originalIme;
        public CancellationTokenSource Closed { get; } = new();
        public Task Ready { get; set; } = Task.CompletedTask;
        public bool Updating { get; set; }
        public bool Dirty { get; set; }
        public bool Applying { get; set; }
        public volatile bool Resetting;
        public volatile int Epoch;
        public string Preedit { get; set; } = "";
        public string? CompositionBase { get; set; }
        public int CompositionStart { get; set; }
        public int CompositionEnd { get; set; }
    }
    private readonly DiagnosticLog _log;
    private readonly Func<IMaliitSession> _createSession;
    private readonly Dictionary<TextBox, Registration> _fields = [];
    private readonly HashSet<Window> _windows = [];
    private InputArea _lastArea;
    private Lease? _active;
    private Task _pendingDismissal = Task.CompletedTask;
    private int _activationEpoch;
    private bool _disposed;
    public event Action<Window, InputArea>? InputAreaChanged;
    public bool IsAvailable => _active?.Session.IsConnected == true;
    public KeyboardVisibility Visibility => KeyboardVisibility.Unknown;

    public MaliitInputBridge(DiagnosticLog log, Func<IMaliitSession>? createSession = null)
    { _log = log; _createSession = createSession ?? (() => new MaliitSession()); }

    /// <summary>Caller must gate installation on Ubuntu Touch; never auto-enable this on a desktop.</summary>
    public IDisposable Register(TextBox field, InputRequest request)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _fields.Add(field, new(field, request.Normalize()));
        field.GotFocus += OnGotFocus; field.LostFocus += OnLostFocus; field.Tapped += OnTapped;
        field.DetachedFromVisualTree += OnDetached;
        return new RegistrationLifetime(this, field);
    }

    private sealed class RegistrationLifetime(MaliitInputBridge owner, TextBox field) : IDisposable
    { public void Dispose() => owner.Unregister(field); }

    private void Unregister(TextBox field)
    {
        if (_active?.Registration.Field == field) EndLease();
        if (!_fields.Remove(field)) return;
        field.GotFocus -= OnGotFocus; field.LostFocus -= OnLostFocus; field.Tapped -= OnTapped;
        field.DetachedFromVisualTree -= OnDetached;
    }

    private async void OnGotFocus(object? sender, FocusChangedEventArgs e)
    { if (sender is TextBox field) await ActivateSafelyAsync(field); }
    private async void OnTapped(object? sender, TappedEventArgs e)
    { if (sender is TextBox field) await ActivateSafelyAsync(field); }
    private void OnLostFocus(object? sender, FocusChangedEventArgs e)
    {
        if (_active is not { } lease || lease.Registration.Field != sender) return;
        // Let a focus transfer install its new registered field first. Otherwise this is a
        // real blur (including a lock whose focus loss precedes Window.Deactivated) and must hide.
        Dispatcher.UIThread.Post(() =>
        {
            if (_active == lease) _ = DeactivateAsync();
        }, DispatcherPriority.Input);
    }
    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    { if (_active?.Registration.Field == sender) EndLease(); }
    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_active is not { } lease || TopLevel.GetTopLevel(lease.Registration.Field) != sender) return;
        // If focus already moved, the queued blur handler allows an owned dialog to take over.
        if (lease.Registration.Field.IsFocused) _ = DeactivateAsync();
    }
    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not Window window) return;
        OnDeactivated(sender, e);
        window.Deactivated -= OnDeactivated; window.Closed -= OnWindowClosed;
        _windows.Remove(window);
    }

    private async Task ActivateSafelyAsync(TextBox field)
    {
        try { await ActivateAsync(field); }
        catch (OperationCanceledException) when (_disposed || _active?.Registration.Field != field) { }
        catch (Exception error)
        {
            _log.Write("MaliitError", $"Activation failed: {error.GetType().Name}: {error.Message}", LogLevel.Error);
            if (_active?.Registration.Field == field) EndLease();
        }
    }

    private async Task ActivateAsync(TextBox field)
    {
        var epoch = _activationEpoch;
        // An old hide must finish before a new context can show a keyboard.
        await _pendingDismissal;
        if (epoch != _activationEpoch) return;
        if (_disposed || !field.IsFocused || field.IsReadOnly || !_fields.TryGetValue(field, out var registration)) return;
        if (_active?.Registration.Field == field)
        { _ = await ShowKeyboardAsync(registration.Request); return; }
        EndLease();
        if (TopLevel.GetTopLevel(field) is not Window window) return;
        if (_windows.Add(window)) { window.Deactivated += OnDeactivated; window.Closed += OnWindowClosed; }
        var query = new TextInputMethodClientRequestedEventArgs { RoutedEvent = InputElement.TextInputMethodClientRequestedEvent };
        field.RaiseEvent(query);
        if (query.Client is null) { _log.Write("Maliit", "No public Avalonia text-input client available.", LogLevel.Warning); return; }
        var lease = new Lease(registration, query.Client, _createSession(), InputMethod.GetIsInputMethodEnabled(field));
        _active = lease;
        // Maliit does not repeat the area on every context activation when its size is unchanged.
        // Area coordinates are global; reuse the measurement across owned dialogs too.
        InputAreaChanged?.Invoke(window, _lastArea);
        InputMethod.SetIsInputMethodEnabled(field, false);
        lease.Client.SurroundingTextChanged += OnStateChanged;
        lease.Client.SelectionChanged += OnStateChanged;
        lease.Client.CursorRectangleChanged += OnStateChanged;
        lease.Client.ResetRequested += OnResetRequested;
        lease.Session.DispatchAsync = input => DispatchEventAsync(lease, input);
        lease.Ready = OpenSafelyAsync(lease);
        await lease.Ready;
    }

    private async Task OpenSafelyAsync(Lease lease)
    {
        try
        {
            await lease.Session.OpenAsync(BuildState(lease), lease.Closed.Token);
            if (_active != lease) return;
            _log.Write("Maliit", "Peer connected; context activated and OSK request issued; visibility=Unknown.");
        }
        catch (OperationCanceledException) when (lease.Closed.IsCancellationRequested) { }
        catch (Exception e)
        {
            if (_active != lease) return;
            _log.Write("MaliitError", $"Connection failed: {e}", LogLevel.Error);
            EndLease();
        }
    }

    private static MaliitWidgetState BuildState(Lease lease)
    {
        var field = lease.Registration.Field;
        var window = TopLevel.GetTopLevel(field)!;
        var sensitive = field.PasswordChar != '\0' || TextInputOptions.GetIsSensitive(field) || lease.Registration.Request.InputType == InputType.Password;
        var rect = lease.Client.CursorRectangle;
        var position = field.PointToScreen(rect.Position);
        var area = new InputArea(position.X, position.Y, (int)Math.Ceiling(rect.Width * window.RenderScaling), (int)Math.Ceiling(rect.Height * window.RenderScaling));
        return new(lease.Registration.Request, sensitive ? "" : lease.Client.SurroundingText,
            sensitive ? 0 : field.CaretIndex, sensitive ? 0 : field.CaretIndex == field.SelectionStart ? field.SelectionEnd : field.SelectionStart,
            unchecked((ulong)(window.TryGetPlatformHandle()?.Handle ?? 0)), area, sensitive);
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (_active is not { } lease || sender != lease.Client) return;
        lease.Dirty = true;
        if (!lease.Updating) Dispatcher.UIThread.Post(() => UpdateSafelyAsync(lease));
    }
    private async void UpdateSafelyAsync(Lease lease)
    {
        if (_active != lease || lease.Updating) return;
        lease.Updating = true;
        try
        {
            await lease.Ready;
            while (_active == lease && lease.Dirty && lease.Session.IsConnected)
            {
                lease.Dirty = false;
                await lease.Session.UpdateAsync(BuildState(lease), lease.Closed.Token);
            }
        }
        catch (OperationCanceledException) when (lease.Closed.IsCancellationRequested) { }
        catch (Exception e) { Fail(lease, e); }
        finally { lease.Updating = false; }
    }
    private async void OnResetRequested(object? sender, EventArgs e)
    {
        if (_active is not { } lease || sender != lease.Client || lease.Applying || lease.Resetting) return;
        lease.Resetting = true; lease.Epoch++;
        CommitPending(lease, preserveSelection: true);
        try { await lease.Ready; if (_active == lease) await lease.Session.ResetAsync(lease.Closed.Token); }
        catch (OperationCanceledException) when (lease.Closed.IsCancellationRequested) { }
        catch (Exception error) { Fail(lease, error); }
        finally { lease.Resetting = false; }
    }

    private async Task<MaliitWidgetState?> DispatchEventAsync(Lease lease, MaliitEvent input)
    {
        if (lease.Closed.IsCancellationRequested) return null;
        var epoch = lease.Epoch;
        var resetting = lease.Resetting;
        try { return await Dispatcher.UIThread.InvokeAsync<MaliitWidgetState?>(() =>
        {
            if (_disposed || _active != lease) return null;
            if (input is CommitText or PreeditText or InputKey or InputSelection && (resetting || lease.Resetting || epoch != lease.Epoch)) return null;
            ApplyEvent(lease, input);
            return _active == lease ? BuildState(lease) : null;
        }, DispatcherPriority.Input, lease.Closed.Token); }
        catch (OperationCanceledException) when (lease.Closed.IsCancellationRequested) { return null; }
    }
    private void ApplyEvent(Lease lease, MaliitEvent input)
    {
        var field = lease.Registration.Field;
        lease.Applying = true;
        try
        {
            switch (input)
            {
                case CommitText commit:
                    lease.Preedit = "";
                    MaliitTextEditor.Commit(field, lease.Client, commit);
                    _log.Write("MaliitCommit", "Native commit applied; content redacted."); break;
                case PreeditText preedit:
                    MaliitTextEditor.Preedit(field, lease.Client, preedit);
                    lease.Preedit = preedit.Text;
                    lease.CompositionBase = field.Text;
                    lease.CompositionStart = field.SelectionStart;
                    lease.CompositionEnd = field.SelectionEnd;
                    _log.Write("MaliitPreedit", "Native preedit updated; content redacted. Span formatting uses Avalonia default."); break;
                case InputKey key:
                    if (!MaliitTextEditor.HandleKey(field, key)) _log.Write("MaliitUnsupported", "Unsupported native key event.", LogLevel.Warning);
                    else _log.Write("MaliitKey", "Native editing event applied; key content redacted.");
                    break;
                case InputSelection selection:
                    var length = field.Text?.Length ?? 0;
                    field.SelectionStart = Math.Clamp(selection.Start, 0, length);
                    field.SelectionEnd = (int)Math.Clamp((long)selection.Start + selection.Length, 0, length); break;
                case InputAreaChanged area:
                    if (TopLevel.GetTopLevel(field) is Window window) PublishArea(window, area.Area);
                    _log.Write("MaliitArea", $"Reported input area={area.Area}; visibility=Unknown."); break;
                case InputHidden:
                    CommitPending(lease);
                    if (TopLevel.GetTopLevel(field) is Window hiddenWindow) PublishArea(hiddenWindow, default);
                    _log.Write("Maliit", "Server initiated hide; a subsequent tap can reopen the input panel."); break;
                case InputActivationLost:
                case InputDisconnected:
                    _log.Write("Maliit", "Native context lost/disconnected; tap a field to reconnect."); EndLease(); break;
            }
        }
        finally { lease.Applying = false; }
    }

    private void Fail(Lease lease, Exception error)
    {
        if (_active != lease) return;
        _log.Write("MaliitError", $"{error.GetType().Name}: {error.Message}", LogLevel.Error);
        EndLease();
    }

    private void PublishArea(Window window, InputArea area)
    {
        _lastArea = area;
        InputAreaChanged?.Invoke(window, area);
    }

    /// <summary>
    /// On the UI thread, preserve composition and hide native input before releasing its connection.
    /// A later field tap/focus can reopen it. Window deactivation calls this automatically.
    /// </summary>
    public Task DeactivateAsync()
    {
        _activationEpoch++;
        _lastArea = default;
        EndLease(dismissKeyboard: true);
        return _pendingDismissal;
    }

    private void EndLease(bool dismissKeyboard = false)
    {
        if (_active is not { } lease) return;
        _active = null;
        CommitPending(lease);
        lease.Client.SurroundingTextChanged -= OnStateChanged;
        lease.Client.SelectionChanged -= OnStateChanged;
        lease.Client.CursorRectangleChanged -= OnStateChanged;
        lease.Client.ResetRequested -= OnResetRequested;
        lease.Client.SetPreeditText(null, null);
        InputMethod.SetIsInputMethodEnabled(lease.Registration.Field, lease.OriginalIme);
        if (TopLevel.GetTopLevel(lease.Registration.Field) is Window window) InputAreaChanged?.Invoke(window, default);
        if (dismissKeyboard) _pendingDismissal = DismissAndDisposeAsync(lease);
        else { lease.Closed.Cancel(); lease.Session.Dispose(); }
    }

    private async Task DismissAndDisposeAsync(Lease lease)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try
        {
            // Opening may already have queued showInputMethod. Finish it before the terminal hide.
            // Detached callbacks cannot edit the field while the old connection is draining.
            await lease.Ready.WaitAsync(timeout.Token);
            if (lease.Session.IsConnected)
            {
                _log.Write("MaliitLifecycle", "Window inactive; requesting native keyboard dismissal.");
                await lease.Session.HideAsync(timeout.Token);
                _log.Write("MaliitLifecycle", "Hide acknowledged before releasing input context; visibility=Unknown.");
            }
        }
        catch (Exception error)
        {
            _log.Write("MaliitLifecycle", $"Dismissal failed or timed out: {error.GetType().Name}; releasing context.", LogLevel.Warning);
        }
        finally { lease.Closed.Cancel(); lease.Session.Dispose(); }
    }

    private static void CommitPending(Lease lease, bool preserveSelection = false)
    {
        var text = lease.Preedit;
        lease.Preedit = "";
        var field = lease.Registration.Field;
        // An application replacing Text explicitly cancels the previous composition.
        if (text.Length == 0 || field.Text != lease.CompositionBase)
        { lease.Client.SetPreeditText(null, null); return; }
        var start = field.SelectionStart; var end = field.SelectionEnd;
        var changedSelection = preserveSelection && (start != lease.CompositionStart || end != lease.CompositionEnd);
        var wasApplying = lease.Applying;
        lease.Applying = true;
        try
        {
            field.SelectionStart = lease.CompositionStart;
            field.SelectionEnd = lease.CompositionEnd;
            MaliitTextEditor.Commit(field, lease.Client, new(text, 0, 0, -1));
            if (changedSelection)
            {
                var from = Math.Min(lease.CompositionStart, lease.CompositionEnd);
                var to = Math.Max(lease.CompositionStart, lease.CompositionEnd);
                int Adjust(int p) => p <= from ? p : p >= to ? p + text.Length - (to - from) : from + text.Length;
                field.SelectionStart = Adjust(start); field.SelectionEnd = Adjust(end);
            }
        }
        finally { lease.Applying = wasApplying; }
    }

    public Task<InputOperationResult> ShowKeyboardAsync(InputRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = request.Normalize();
        if (_active is { } lease && lease.Registration.Request != normalized)
            return Task.FromResult(new InputOperationResult(InputOperationStatus.Unsupported, Visibility,
                "Requested hints differ from the active field registration."));
        return RequestAsync(true, cancellationToken);
    }
    public Task<InputOperationResult> HideKeyboardAsync(CancellationToken cancellationToken = default) => RequestAsync(false, cancellationToken);
    private async Task<InputOperationResult> RequestAsync(bool show, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var lease = _active;
        if (lease is null) return new(InputOperationStatus.Unavailable, Visibility, "No active Maliit input context.");
        try
        {
            await lease.Ready.WaitAsync(token);
            if (_active != lease || !lease.Session.IsConnected) return new(InputOperationStatus.Unavailable, Visibility, "Input context changed or connection failed.");
            if (show) await lease.Session.ShowAsync(token);
            else
            {
                lease.Resetting = true; lease.Epoch++;
                try
                {
                    CommitPending(lease);
                    await lease.Session.ResetAsync(token);
                    if (_active != lease) return new(InputOperationStatus.Unavailable, Visibility, "Input context changed.");
                    await lease.Session.UpdateAsync(BuildState(lease), token);
                }
                finally { lease.Resetting = false; }
                await lease.Session.HideAsync(token);
            }
            return new(InputOperationStatus.RequestedUnconfirmed, Visibility, "Native request sent; visibility is not inferred from acknowledgement.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) { Fail(lease, e); return new(InputOperationStatus.Unavailable, Visibility, "Maliit request failed; see diagnostics."); }
    }
    public Task<InputOperationResult> SetInputTypeAsync(InputType inputType, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(inputType)) throw new ArgumentOutOfRangeException(nameof(inputType));
        return Task.FromResult(new InputOperationResult(InputOperationStatus.Unsupported, Visibility,
            "Register field-specific InputRequest hints; changing an active registration is not supported yet."));
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; EndLease();
        foreach (var field in _fields.Keys.ToArray()) Unregister(field);
        foreach (var window in _windows) { window.Deactivated -= OnDeactivated; window.Closed -= OnWindowClosed; }
        _windows.Clear(); _lastArea = default;
    }
}
