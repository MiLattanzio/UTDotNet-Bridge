using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using UTDotNet.Avalonia;
using UTDotNet.Core;
using UTDotNet.Diagnostics;
using UTDotNet.UbuntuTouch;

namespace UTDotNet.Tests;

// These use Avalonia's headless clipboard and a fake Maliit peer. Native clipboard
// interoperability and touch-menu behavior require the separate device tests.
public class MaliitClipboardTests
{
    [AvaloniaFact]
    public async Task TouchingPreeditCommitsItBeforeTheTextBoxAttemptsSelection()
    {
        var session = new MaliitTests.FakeSession();
        using var bridge = new MaliitInputBridge(new(), () => session);
        var field = new TextBox();
        using var registration = bridge.Register(field, new() { AutoCorrect = true });
        var window = new Window { Width = 360, Height = 640, Content = field };
        window.Show();
        try
        {
            field.Focus(); Dispatcher.UIThread.RunJobs();
            await session.DispatchAsync!(new UTDotNet.UbuntuTouch.PreeditText("sole", 0, 0, -1, 0));
            Assert.True(string.IsNullOrEmpty(field.Text));
            using var touch = window.TouchBegin(new Point(20, 20));
            Assert.Equal("sole", field.Text);
            window.TouchEnd(touch, new Point(20, 20)); Dispatcher.UIThread.RunJobs();
            field.SelectAll();
            Assert.Equal("sole", field.SelectedText);
            Assert.True(bridge.IsAvailable);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task TouchContextMenuCutRemovesSelection()
    {
        var session = new MaliitTests.FakeSession();
        using var bridge = new MaliitInputBridge(new(), () => session);
        var field = new TextBox { AcceptsReturn = true, MinHeight = 112, FontSize = 16 };
        using var registration = bridge.Register(field, new() { Multiline = true });
        var window = new Window { Width = 360, Height = 640,
            Content = new StackPanel { Margin = new Thickness(16), Children = { field } } };
        window.Show();
        var clipboard = Assert.IsAssignableFrom<IClipboard>(window.Clipboard);
        try
        {
            field.Focus(); Dispatcher.UIThread.RunJobs();
            await session.DispatchAsync!(new CommitText("sole ", 0, 0, -1));
            Tap(field);
            field.SelectAll();
            var flyout = Assert.IsType<MenuFlyout>(field.ContextFlyout);
            flyout.Popup.ShouldUseOverlayLayer = true;
            flyout.ShowMode = FlyoutShowMode.Transient;
            flyout.ShowAt(field);
            Dispatcher.UIThread.RunJobs();
            var cut = flyout.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Cut"));
            Assert.True(cut.IsEffectivelyEnabled);
            var requests = 0;
            field.CuttingToClipboard += (_, _) => requests++;
            Tap(cut);
            Assert.Equal(1, requests);
            Assert.Equal("", field.Text);
            Assert.Equal("sole ", await clipboard.TryGetTextAsync());
            Assert.False(flyout.IsOpen);
            Assert.True(field.IsFocused);
            Assert.True(bridge.IsAvailable);
            Assert.Equal(0, session.HideRequests);

            void Tap(Control control)
            {
                var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
                using var touch = window.TouchBegin(point);
                window.TouchEnd(touch, point);
                Dispatcher.UIThread.RunJobs();
            }
        }
        finally { await clipboard.ClearAsync(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task ClipboardEditsPreserveSelectionUndoAndSubsequentNativeInput()
    {
        var sessions = new List<MaliitTests.FakeSession>();
        var log = new DiagnosticLog();
        using var bridge = new MaliitInputBridge(log, () =>
        {
            var session = new MaliitTests.FakeSession();
            sessions.Add(session);
            return session;
        });
        var source = new TextBox { AcceptsReturn = true };
        var target = new TextBox { AcceptsReturn = true };
        using var sourceRegistration = bridge.Register(source, new() { Multiline = true });
        using var targetRegistration = bridge.Register(target, new() { Multiline = true });
        using var sourceObserver = new TextInputAdapter(source, "source", new(), log);
        using var targetObserver = new TextInputAdapter(target, "target", new(), log);
        var window = new Window { Content = new StackPanel { Children = { source, target } } };
        window.Show();
        var clipboard = Assert.IsAssignableFrom<IClipboard>(window.Clipboard);
        try
        {
            const string text = "ciao mondo\nseconda riga";
            source.Focus(); Dispatcher.UIThread.RunJobs();
            await sessions.Last().DispatchAsync!(new CommitText(text, 0, 0, -1));
            source.SelectionStart = 5; source.SelectionEnd = 10;
            Shortcut(window, PhysicalKey.C);
            Assert.Equal("mondo", await clipboard.TryGetTextAsync());
            Assert.Equal(text, source.Text);

            target.Focus(); Dispatcher.UIThread.RunJobs();
            await sessions.Last().DispatchAsync!(new CommitText("vecchio testo", 0, 0, -1));
            target.SelectionStart = 0; target.SelectionEnd = 7;
            target.Paste(); Dispatcher.UIThread.RunJobs();
            Assert.Equal("mondo testo", target.Text);
            target.Undo();
            Assert.Equal("vecchio testo", target.Text);
            target.Redo();
            Assert.Equal("mondo testo", target.Text);
            var state = await sessions.Last().DispatchAsync!(new CommitText("!", 0, 0, -1));
            Assert.Equal("mondo! testo", target.Text);
            Assert.Equal(target.Text, state!.SurroundingText);
            Assert.Equal(6, state.Cursor);

            source.Focus(); Dispatcher.UIThread.RunJobs();
            source.SelectAll(); source.Copy(); Dispatcher.UIThread.RunJobs();
            target.Focus(); Dispatcher.UIThread.RunJobs();
            target.SelectAll(); target.Paste(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(text, target.Text);

            source.Focus(); Dispatcher.UIThread.RunJobs();
            source.SelectionStart = 5; source.SelectionEnd = 10;
            source.Cut(); Dispatcher.UIThread.RunJobs();
            Assert.Equal("ciao \nseconda riga", source.Text);
            Assert.Equal("mondo", await clipboard.TryGetTextAsync());
            source.Undo();
            Assert.Equal(text, source.Text);
            Assert.DoesNotContain(log.Snapshot(), entry =>
                entry.Message.Contains("mondo") || entry.Message.Contains("vecchio"));
        }
        finally { await clipboard.ClearAsync(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task PasswordCopyAndCutShortcutsDoNotReplaceClipboardOrExposeNativeState()
    {
        var session = new MaliitTests.FakeSession();
        var log = new DiagnosticLog();
        using var bridge = new MaliitInputBridge(log, () => session);
        var field = new TextBox { PasswordChar = '●' };
        var request = new InputRequest { InputType = InputType.Password };
        using var registration = bridge.Register(field, request);
        using var observer = new TextInputAdapter(field, "password", request, log);
        var window = new Window { Content = field };
        window.Show();
        var clipboard = Assert.IsAssignableFrom<IClipboard>(window.Clipboard);
        try
        {
            field.Focus(); Dispatcher.UIThread.RunJobs();
            var state = await session.DispatchAsync!(new CommitText("dummy-secret", 0, 0, -1));
            Assert.Equal("dummy-secret", field.Text);
            await clipboard.SetTextAsync("public-marker");
            field.SelectAll();
            Assert.False(field.CanCopy);
            Assert.False(field.CanCut);
            // Exercise user commands, which honor CanCopy/CanCut. Calling the public
            // Copy()/Cut() methods directly bypasses Avalonia's command enablement.
            Shortcut(window, PhysicalKey.C);
            Shortcut(window, PhysicalKey.X);
            Assert.Equal("dummy-secret", field.Text);
            Assert.Equal("public-marker", await clipboard.TryGetTextAsync());
            Assert.True(state!.IsSensitive);
            Assert.Equal("", state.SurroundingText);
            Assert.Equal(0, state.Cursor);
            Assert.Equal(0, state.Anchor);
            Assert.DoesNotContain(log.Snapshot(), entry => entry.Message.Contains("dummy-secret") ||
                entry.Message.Contains("length=") || entry.Message.Contains("start="));
        }
        finally { await clipboard.ClearAsync(); window.Close(); }
    }

    private static void Shortcut(Window window, PhysicalKey key)
    {
        window.KeyPressQwerty(key, RawInputModifiers.Control);
        window.KeyReleaseQwerty(key, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
    }
}
