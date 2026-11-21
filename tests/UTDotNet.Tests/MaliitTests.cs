using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Threading;
using UTDotNet.Avalonia;
using UTDotNet.Core;
using UTDotNet.Diagnostics;
using UTDotNet.UbuntuTouch;

namespace UTDotNet.Tests;

public class MaliitTests
{
    [Theory]
    [InlineData("24", LinuxMobileEnvironment.UbuntuTouch, 3d)]
    [InlineData("16", LinuxMobileEnvironment.UbuntuTouch, 2d)]
    [InlineData("24", LinuxMobileEnvironment.DesktopLinux, null)]
    [InlineData("NaN", LinuxMobileEnvironment.UbuntuTouch, null)]
    [InlineData("0", LinuxMobileEnvironment.UbuntuTouch, null)]
    [InlineData("1000", LinuxMobileEnvironment.UbuntuTouch, null)]
    public void GridScaleIsBoundedAndOnlyForUbuntuTouch(string grid, LinuxMobileEnvironment environment, double? scale) =>
        Assert.Equal(scale, PhoneDisplayScale.FromGridUnit(grid, environment));

    [Fact]
    public void SensitiveContextNeverSendsTextOrOffsets()
    {
        var state = new MaliitWidgetState(new() { InputType = InputType.Password, AutoCorrect = true }, "secret", 6, 0, 1, default);
        var properties = state.ToProperties();
        Assert.DoesNotContain("surroundingText", properties.Keys);
        Assert.DoesNotContain("cursorPosition", properties.Keys);
        Assert.DoesNotContain("anchorPosition", properties.Keys);
        Assert.False(properties["predictionEnabled"].GetBool());
        Assert.False(properties["autocapitalizationEnabled"].GetBool());
        Assert.Equal("", state.SelectedText);
    }

    [Fact]
    public void KeyboardInsetUsesPhysicalIntersectionAndDoesNotDoubleShrink()
    {
        var keyboard = new InputArea(0, 1800, 1224, 900);
        Assert.Equal(300, MaliitLayout.BottomInset(keyboard, new PixelRect(0, 0, 1224, 2700), 3));
        Assert.Equal(0, MaliitLayout.BottomInset(keyboard, new PixelRect(0, 0, 1224, 1800), 3));
        Assert.Equal(0, MaliitLayout.BottomInset(keyboard, new PixelRect(1400, 0, 1224, 2700), 3));
        // FP5 landscape: reserve the entire measured OSK, even with less than 200 DIP left.
        Assert.Equal(232, MaliitLayout.BottomInset(new(0, 456, 2700, 696), new PixelRect(0, 0, 2700, 1152), 3));
    }

    private static (Window Window, TextBox Field, TextInputMethodClient Client) Editor(string text = "")
    {
        var field = new TextBox { Text = text, AcceptsReturn = true };
        var window = new Window { Content = field }; window.Show(); field.Focus();
        var args = new TextInputMethodClientRequestedEventArgs { RoutedEvent = InputElement.TextInputMethodClientRequestedEvent };
        field.RaiseEvent(args);
        return (window, field, args.Client!);
    }

    [AvaloniaFact]
    public void NativeCommitReplacesSelectionAndSupportsUndo()
    {
        var (window, field, client) = Editor("hello world");
        try
        {
            field.SelectionStart = 6; field.SelectionEnd = 11;
            MaliitTextEditor.Commit(field, client, new("Italia", 0, 0, -1));
            Assert.Equal("hello Italia", field.Text);
            field.Undo();
            Assert.Equal("hello world", field.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PreeditIsSeparateFromTextAndCommitReplacesRelativeRange()
    {
        var (window, field, client) = Editor("helo");
        try
        {
            field.CaretIndex = 4;
            MaliitTextEditor.Preedit(field, client, new("è", 0, 0, 1, 1));
            Assert.Equal("helo", field.Text);
            MaliitTextEditor.Commit(field, client, new("hello", -4, 4, -1));
            Assert.Equal("hello", field.Text);
            MaliitTextEditor.HandleKey(field, new(6, 0x01000003, 0, "", false, 1, 0));
            Assert.Equal("hell", field.Text);
            MaliitTextEditor.HandleKey(field, new(6, 0x01000004, 0, "\r", false, 1, 0));
            Assert.EndsWith("\n", field.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void EmptyCommitCanDeleteAndSignalOnlyKeyDoesNotEdit()
    {
        var (window, field, client) = Editor("abc");
        try
        {
            field.CaretIndex = 3;
            MaliitTextEditor.Commit(field, client, new("", -1, 1, -1));
            Assert.Equal("ab", field.Text);
            MaliitTextEditor.HandleKey(field, new(6, 0x01000003, 0, "", false, 1, 1));
            Assert.Equal("ab", field.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task FocusSwitchRejectsOldCallbacksAndRestoresFrameworkIme()
    {
        var first = new TextBox(); var second = new TextBox();
        var window = new Window { Content = new StackPanel { Children = { first, second } } };
        var sessions = new List<FakeSession>();
        var log = new DiagnosticLog();
        using var bridge = new MaliitInputBridge(log, () => { var s = new FakeSession(); sessions.Add(s); return s; });
        InputArea lastArea = default;
        bridge.InputAreaChanged += (_, area) => lastArea = area;
        using var a = bridge.Register(first, new()); using var b = bridge.Register(second, new());
        window.Show();
        try
        {
            first.Focus(); Dispatcher.UIThread.RunJobs();
            var old = sessions.Last();
            var oldCallback = old.DispatchAsync!;
            var keyboardArea = new InputArea(0, 500, 400, 300);
            await oldCallback(new InputAreaChanged(keyboardArea));
            Assert.False(InputMethod.GetIsInputMethodEnabled(first));
            second.Focus(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(keyboardArea, lastArea);
            Assert.True(old.Disposed);
            Assert.True(InputMethod.GetIsInputMethodEnabled(first));
            await oldCallback(new CommitText("stale", 0, 0, -1));
            await sessions.Last().DispatchAsync!(new CommitText("fresh", 0, 0, -1));
            Assert.Null(first.Text);
            Assert.Equal("fresh", second.Text);
            Assert.DoesNotContain(log.Snapshot(), e => e.Message.Contains("fresh") || e.Message.Contains("stale"));
            await sessions.Last().DispatchAsync!(new PreeditText("pending", 0, 0, -1, 0));
            await bridge.HideKeyboardAsync(TestContext.Current.CancellationToken);
            Assert.Equal("freshpending", second.Text);
            Assert.Equal(KeyboardVisibility.Unknown, bridge.Visibility);
            Assert.Equal(1, sessions.Last().HideRequests);
            await sessions.Last().DispatchAsync!(new PreeditText("word", 0, 0, -1, 0));
            first.Focus(); Dispatcher.UIThread.RunJobs();
            Assert.Equal("freshpendingword", second.Text);
            Assert.Null(first.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task KeyboardMeasurementSurvivesDialogHandoffWithoutAnotherAreaCallback()
    {
        var field = new TextBox(); var dialogField = new TextBox();
        var parent = new Window { Content = field }; var dialog = new Window { Content = dialogField };
        var sessions = new List<FakeSession>();
        using var bridge = new MaliitInputBridge(new(), () => { var s = new FakeSession(); sessions.Add(s); return s; });
        using var a = bridge.Register(field, new()); using var b = bridge.Register(dialogField, new());
        var reported = new Dictionary<Window, InputArea>();
        bridge.InputAreaChanged += (window, area) => reported[window] = area;
        parent.Show();
        try
        {
            field.Focus(); Dispatcher.UIThread.RunJobs();
            var area = new InputArea(0, 600, 400, 300);
            await sessions.Last().DispatchAsync!(new InputAreaChanged(area));
            dialog.Show(parent); dialogField.Focus(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(area, reported[dialog]);
            dialog.Close(); field.Focus(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(area, reported[parent]);
        }
        finally { dialog.Close(); parent.Close(); }
    }

    internal sealed class FakeSession : IMaliitSession
    {
        public Func<MaliitEvent, Task<MaliitWidgetState?>>? DispatchAsync { get; set; }
        public bool IsConnected { get; private set; }
        public bool Disposed { get; private set; }
        public int HideRequests { get; private set; }
        public Task OpenAsync(MaliitWidgetState state, CancellationToken cancellationToken) { IsConnected = true; return Task.CompletedTask; }
        public Task UpdateAsync(MaliitWidgetState state, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ShowAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task HideAsync(CancellationToken cancellationToken) { HideRequests++; return Task.CompletedTask; }
        public Task ResetAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Dispose() { Disposed = true; IsConnected = false; }
    }
}
