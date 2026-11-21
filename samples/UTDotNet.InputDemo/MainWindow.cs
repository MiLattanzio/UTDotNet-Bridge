using System.Collections.ObjectModel;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.Styling;
using Microsoft.Extensions.Logging;
using UTDotNet.Avalonia;
using UTDotNet.Core;
using UTDotNet.Diagnostics;
using UTDotNet.UbuntuTouch;

namespace UTDotNet.InputDemo;

/// <summary>Phone-sized diagnostic harness with opt-in native Maliit input on Ubuntu Touch.</summary>
public sealed class MainWindow : Window
{
    private readonly DiagnosticLog _log = new();
    private readonly IPlatformInputService _input = new UnsupportedPlatformInputService();
    private readonly MaliitInputBridge? _maliit;
    private readonly List<IDisposable> _inputRegistrations = [];
    private readonly SessionBusDiagnostics _busDiagnostics;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<TextInputAdapter> _adapters = [];
    private readonly ObservableCollection<string> _events = [];
    private readonly TextBox _report = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontSize = 13 };
    private readonly TextBlock _status = Label("Direct OSK: Unsupported · Visibility: Unknown");
    private readonly List<(string Test, ComboBox Result, TextBox Notes)> _checks = [];
    private PlatformInfo _platform = new PlatformDetector().Detect();
    private SessionBusReport? _bus;
    private TextBox? _lastField;
    private TextInputAdapter? _lastAdapter;
    private bool _closed;
    private bool _scanning;
    private readonly TextBox _standard;
    private readonly DockPanel _root;
    private readonly ScrollViewer _inputScroll;
    private readonly StackPanel _header;
    private readonly Button _export;
    private InputArea _inputArea;
    public DiagnosticLog Log => _log;

    public MainWindow(SessionBusDiagnostics? busDiagnostics = null, MaliitInputBridge? inputBridge = null)
    {
        _busDiagnostics = busDiagnostics ?? new SessionBusDiagnostics();
        if (inputBridge is not null ||
            (_platform.MobileEnvironment == LinuxMobileEnvironment.UbuntuTouch && !Program.Arguments.Contains("--no-maliit")))
        {
            _maliit = inputBridge ?? new MaliitInputBridge(_log);
            _input = _maliit;
            _maliit.InputAreaChanged += ApplyInputArea;
            _status.Text = "Maliit ready · Tap a field";
        }
        Title = "UTDotNet · Input diagnostics";
        Width = 760; Height = 850; MinWidth = 320; MinHeight = 240;
        if (_platform.MobileEnvironment == LinuxMobileEnvironment.UbuntuTouch)
        {
            var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
            Width = screen is null ? 360 : screen.WorkingArea.Width / screen.Scaling;
            Height = screen is null ? 700 : screen.WorkingArea.Height / screen.Scaling;
            Position = screen?.WorkingArea.Position ?? default;
            WindowStartupLocation = WindowStartupLocation.Manual;
            WindowState = WindowState.Maximized;
        }
        FontSize = 16;
        FontFamily = new FontFamily("DejaVu Sans");
        Styles.Add(new Style(selector => selector.OfType<ComboBoxItem>())
        {
            Setters = { new Setter(MinHeightProperty, 48d) }
        });
        ConfigurePhoneTextEditing(_report);
        _log.EntryAdded += OnEntryAdded;
        var inputs = new StackPanel { Spacing = 12, Margin = new Thickness(16) };
        inputs.Children.Add(Label("Tap a field. Typed text is not logged."));
        _standard = AddField(inputs, "Standard text", new() { AutoCorrect = true, AutoCapitalization = true });
        AddField(inputs, "Password", new() { InputType = InputType.Password });
        AddField(inputs, "Numeric", new() { InputType = InputType.Numeric });
        AddField(inputs, "Email", new() { InputType = InputType.Email });
        AddField(inputs, "Multiline", new() { Multiline = true, AutoCorrect = true });
        AddField(inputs, "Search", new() { InputType = InputType.Search });
        AddField(inputs, "Phone", new() { InputType = InputType.Phone });
        AddField(inputs, "URL", new() { InputType = InputType.Url });
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(Button("Show keyboard", RequestKeyboardAsync));
        buttons.Children.Add(Button("Hide keyboard", DismissKeyboardAsync));
        buttons.Children.Add(Button("Input dialog", OpenDialogAsync));
        inputs.Children.Insert(1, buttons);
        inputs.Children.Add(Label(_maliit is null ? "Native bridge disabled. Input hints depend on the backend." : "Native Maliit input enabled. Keyboard layout and suggestions depend on the system."));

        var system = new DockPanel { Margin = new Thickness(12) };
        var refresh = Button("Refresh diagnostics", RefreshDiagnosticsAsync);
        DockPanel.SetDock(refresh, Dock.Top); system.Children.Add(refresh);
        var systemExport = Button("Copy diagnostic report", CopyReportAsync);
        DockPanel.SetDock(systemExport, Dock.Bottom); system.Children.Add(systemExport); system.Children.Add(_report);
        var eventList = new ListBox { ItemsSource = _events, FontSize = 12 };
        var checks = BuildChecks();
        _inputScroll = new ScrollViewer { Content = inputs };
        var tabs = new TabControl { ItemsSource = new[]
        {
            Tab("Input", _inputScroll),
            Tab("System", system),
            Tab("Events", eventList),
            Tab("Checks", new ScrollViewer { Content = checks })
        }};
        var root = _root = new DockPanel();
        var header = _header = new StackPanel { Spacing = 5, Margin = new Thickness(16) };
        header.Children.Add(new TextBlock { Text = "UTDotNet Input", FontSize = 22, FontWeight = FontWeight.SemiBold });
        header.Children.Add(_status);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var export = _export = Button("Copy diagnostic report", CopyReportAsync);
        DockPanel.SetDock(export, Dock.Bottom); root.Children.Add(export); root.Children.Add(tabs);
        Content = root;
        UpdateReport();
        Opened += OnOpened;
        Closed += OnClosed;
        SizeChanged += (_, _) =>
        {
            ApplyInputArea(this, _inputArea);
            _log.Write("Geometry", $"client={ClientSize}; scale={RenderScaling}; occlusion=Unknown");
        };
        Activated += (_, _) => _log.Write("Lifecycle", "Window activated");
        Deactivated += (_, _) => _log.Write("Lifecycle", "Window deactivated; OSK visibility=Unknown");
    }

    private static TextBlock Label(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };
    private static TabItem Tab(string title, Control content) => new()
    { Header = title, Content = content, FontSize = 16, MinHeight = 48, Padding = new Thickness(10, 8) };
    private Button Button(string text, Func<Task> action)
    {
        var button = new Button { Content = text, Margin = new Thickness(4), MinHeight = 48, Focusable = false };
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try { await action(); }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (Exception e) { _log.Write("Error", $"{text}: {e.GetType().Name}: {e.Message}", LogLevel.Error); }
            finally { if (!_closed) button.IsEnabled = true; }
        };
        return button;
    }

    private TextBox AddField(StackPanel panel, string name, InputRequest request, List<TextInputAdapter>? owned = null)
    {
        panel.Children.Add(Label(name));
        var field = new TextBox { Name = name.Replace(" ", ""), PlaceholderText = name, MinHeight = request.Multiline ? 112 : 48,
            AcceptsReturn = request.Multiline, TextWrapping = request.Multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            PasswordChar = request.InputType == InputType.Password ? '●' : '\0' };
        var adapter = new TextInputAdapter(field, name, request, _log);
        field.CuttingToClipboard += (_, _) => _log.Write("ClipboardAction", $"{name}: Cut requested; content redacted");
        field.CopyingToClipboard += (_, _) => _log.Write("ClipboardAction", $"{name}: Copy requested; content redacted");
        field.PastingFromClipboard += (_, _) => _log.Write("ClipboardAction", $"{name}: Paste requested; content redacted");
        ConfigurePhoneTextEditing(field, owned is not null);
        (owned ?? _adapters).Add(adapter);
        if (_maliit is not null)
        {
            var registration = _maliit.Register(field, request);
            _inputRegistrations.Add(registration);
            if (owned is not null) field.DetachedFromVisualTree += (_, _) => { registration.Dispose(); _inputRegistrations.Remove(registration); };
        }
        field.GotFocus += (_, _) => { _lastField = field; _lastAdapter = adapter; };
        panel.Children.Add(field);
        return field;
    }

    private void ConfigurePhoneTextEditing(TextBox field, bool disposeOnDetach = false)
    {
        if (_platform.MobileEnvironment != LinuxMobileEnvironment.UbuntuTouch) return;
        var menu = new PhoneTextMenu(field);
        _inputRegistrations.Add(menu);
        var clipboard = new TextClipboardBridge(field, _log);
        _inputRegistrations.Add(clipboard);
        if (disposeOnDetach) field.DetachedFromVisualTree += (_, _) =>
        { clipboard.Dispose(); menu.Dispose(); _inputRegistrations.Remove(clipboard); _inputRegistrations.Remove(menu); };
    }

    private async Task RequestKeyboardAsync()
    {
        _log.Write("KeyboardRequest", "Explicit show requested by user");
        if (_lastField is null || _lastAdapter is null)
        { _status.Text = "Select an input field first · Visibility: Unknown"; return; }
        var request = _lastAdapter.Request;
        var focused = _lastField.Focus();
        _log.Write("FocusRequest", $"last field focused={focused}; keyboard activation unconfirmed");
        ShowResult(await _input.ShowKeyboardAsync(request, _lifetime.Token));
    }
    private async Task DismissKeyboardAsync()
    {
        _log.Write("KeyboardDismissalRequest", "Explicit hide requested by user");
        ShowResult(await _input.HideKeyboardAsync(_lifetime.Token));
    }
    private void ShowResult(InputOperationResult result)
    {
        _status.Text = result.Status == InputOperationStatus.RequestedUnconfirmed
            ? "Request sent · Visibility unknown" : $"OSK {result.Status} · Visibility unknown";
        _log.Write("KeyboardResult", $"{result.Status}: {result.Detail}");
    }

    private async Task OpenDialogAsync()
    {
        var owned = new List<TextInputAdapter>();
        var fields = new StackPanel { Spacing = 12, Margin = new Thickness(16) };
        var dialog = new Window { Title = "Dialog input test", Width = Math.Min(420, Bounds.Width), Height = 330,
            FontSize = FontSize, FontFamily = FontFamily,
            Content = new ScrollViewer { Content = fields }, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var field = AddField(fields, "Dialog text", new() { AutoCorrect = true }, owned);
        fields.Children.Add(Button("Close dialog", () => { dialog.Close(); return Task.CompletedTask; }));
        dialog.Opened += (_, _) => field.Focus();
        _log.Write("Dialog", "Opening native dialog window");
        try { await dialog.ShowDialog(this); }
        finally
        {
            foreach (var adapter in owned) adapter.Dispose();
            _lastField = _standard; _lastAdapter = _adapters[0];
            if (!_closed) _standard.Focus();
            _log.Write("Dialog", "Closed; observers detached");
        }
    }

    private StackPanel BuildChecks()
    {
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(16) };
        panel.Children.Add(Label("Record only tests you actually execute. Results and notes are included when copying the report; they are not saved automatically."));
        foreach (var test in new[] { "Tap text field / OSK", "Password / OSK", "Numeric mode", "Email mode", "Multiline editing", "Search mode",
            "Dismiss OSK", "Retap focused field", "Switch fields", "Tap ordinary button", "Dialog input", "Composition / preedit", "Backspace",
            "Selection", "Clipboard paste", "Rotate / resize", "Keyboard occlusion", "Suspend / resume" })
        {
            panel.Children.Add(Label(test));
            var result = new ComboBox { ItemsSource = new[] { "Unverified", "Pass", "Fail" }, SelectedIndex = 0, MinHeight = 48, HorizontalAlignment = HorizontalAlignment.Stretch };
            result.DropDownOpened += (_, _) => _log.Write("CheckMenu", $"{test}: opened");
            result.DropDownClosed += (_, _) => _log.Write("CheckMenu", $"{test}: closed");
            result.SelectionChanged += (_, _) => _log.Write("CheckResult", $"{test}: {result.SelectedItem}");
            var notes = new TextBox { PlaceholderText = "Observation / device details", MinHeight = 48 };
            ConfigurePhoneTextEditing(notes);
            if (_maliit is not null) _inputRegistrations.Add(_maliit.Register(notes, new()));
            panel.Children.Add(result); panel.Children.Add(notes);
            _checks.Add((test, result, notes));
        }
        return panel;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        var descriptor = TryGetPlatformHandle()?.HandleDescriptor;
        _platform = _platform with { DisplayBackend = descriptor switch
        {
            "XID" => "X11 (observed XID; XWayland not established)",
            "wl_surface" => "Wayland (observed wl_surface)",
            null when Program.Arguments.Contains("--wayland") => "Wayland (explicit backend initialized; native handle unavailable)",
            null => "Unknown (no native handle)",
            _ => $"Native handle: {descriptor}"
        }};
        _log.Write("Startup", $"{_platform.DisplayBackend}; Avalonia={typeof(Application).Assembly.GetName().Version}");
        UpdateReport();
        try
        {
            await RefreshDiagnosticsAsync();
            if (Program.Arguments.Contains("--smoke")) await RunSmokeAsync();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            _log.Write("Error", error.ToString(), LogLevel.Error);
            if (Program.Arguments.Contains("--smoke")) (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown(2);
        }
    }
    private async Task RefreshDiagnosticsAsync()
    {
        if (_scanning) return;
        _scanning = true;
        try
        {
            _bus = await _busDiagnostics.InspectAsync(_lifetime.Token);
            if (_closed) return;
            UpdateReport();
            _log.Write("DBus", $"Session bus={_bus.Availability}; {_bus.MaliitStatus}");
        }
        finally { _scanning = false; }
    }
    private void UpdateReport() => _report.Text = EnvironmentReport.Format(_platform, _bus, typeof(Application).Assembly.GetName().Version?.ToString(),
        _maliit is null ? "not installed" : $"Maliit peer bridge; connected={_input.IsAvailable}") + $"\nWindow: {ClientSize}; rendering scale={RenderScaling:g}\n";

    private void ApplyInputArea(Window window, InputArea area)
    {
        if (window.Content is not Control content) return;
        var inset = 0d;
        // A zero area also arrives while the window's presentation source is being detached.
        // Clearing the inset needs no native screen coordinates in that case.
        if (area.Width > 0 && area.Height > 0)
        {
            var origin = window.PointToScreen(default);
            var viewport = new PixelRect(origin, PixelSize.FromSize(window.ClientSize, window.RenderScaling));
            inset = MaliitLayout.BottomInset(area, viewport, window.RenderScaling);
        }
        if (window == this)
        {
            _inputArea = area;
            // Preserve touch targets and text size when the landscape OSK leaves little height.
            // Export remains available on the System tab when the surrounding chrome is hidden.
            var hasRoom = window.ClientSize.Height - inset >= 360;
            _header.IsVisible = hasRoom;
            _export.IsVisible = hasRoom;
        }
        var margin = content.Margin;
        content.Margin = new Thickness(margin.Left, margin.Top, margin.Right, inset);
        Dispatcher.UIThread.Post(() =>
        {
            if (!_closed && _lastField is { IsFocused: true } field) field.BringIntoView();
        }, DispatcherPriority.Loaded);
    }
    private async Task CopyReportAsync()
    {
        var clipboard = Clipboard;
        if (clipboard is null) { _log.Write("Clipboard", "Unavailable", LogLevel.Warning); return; }
        var report = new StringBuilder(_report.Text);
        report.AppendLine("\nManual test matrix:");
        foreach (var check in _checks) report.AppendLine($"{check.Test}: {check.Result.SelectedItem}; {check.Notes.Text}");
        report.AppendLine("\nRecent events:");
        foreach (var entry in _log.Snapshot()) report.AppendLine(entry.ToString());
        if (_platform.MobileEnvironment == LinuxMobileEnvironment.UbuntuTouch)
        {
            if (!await ClipboardTextWriter.TryWriteAsync(clipboard, report.ToString(), _log, _lifetime.Token)) return;
        }
        else await clipboard.SetTextAsync(report.ToString());
        _log.Write("Clipboard", "Diagnostic report copied");
    }
    private void OnEntryAdded(DiagnosticEvent entry)
    {
        if (Program.Arguments.Contains("--trace")) Console.WriteLine(entry);
        void Add()
        {
            if (_closed) return;
            _events.Insert(0, entry.ToString());
            while (_events.Count > 300) _events.RemoveAt(_events.Count - 1);
        }
        if (Dispatcher.UIThread.CheckAccess()) Add(); else Dispatcher.UIThread.Post(Add);
    }
    private async Task RunSmokeAsync()
    {
        await Task.Delay(350, _lifetime.Token);
        if (!_standard.Focus()) throw new InvalidOperationException("Smoke: field focus failed.");
        _standard.Text = "Local smoke test";
        await RequestKeyboardAsync();
        await DismissKeyboardAsync();
        await Task.Delay(250, _lifetime.Token);
        if (!_log.Snapshot().Any(e => e.Category == "TextChanged")) throw new InvalidOperationException("Smoke: text event missing.");
        var screenshotIndex = Array.IndexOf(Program.Arguments, "--screenshot");
        if (screenshotIndex >= 0 && screenshotIndex + 1 < Program.Arguments.Length)
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)Bounds.Width, (int)Bounds.Height), new Vector(96, 96));
            bitmap.Render(this);
            bitmap.Save(Program.Arguments[screenshotIndex + 1], PngBitmapEncoderOptions.Default);
        }
        Console.WriteLine(_report.Text);
        Console.WriteLine("SMOKE PASS: window opened, focus/text diagnostics and OSK request results observed. Interactive input is a separate test.");
        Close();
    }
    private void OnClosed(object? sender, EventArgs e)
    {
        _closed = true;
        _lifetime.Cancel();
        _maliit?.Dispose();
        foreach (var registration in _inputRegistrations) registration.Dispose();
        _log.EntryAdded -= OnEntryAdded;
        foreach (var adapter in _adapters) adapter.Dispose();
        _lifetime.Dispose();
    }
}
