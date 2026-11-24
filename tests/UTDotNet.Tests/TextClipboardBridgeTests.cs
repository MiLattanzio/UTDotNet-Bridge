using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Threading;
using UTDotNet.Avalonia;
using UTDotNet.Diagnostics;
using UTDotNet.InputDemo;

namespace UTDotNet.Tests;

public class TextClipboardBridgeTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingManagerAcknowledgementRequiresExactOwnedData(bool ownsData)
    {
        IAsyncDataTransfer? written = null;
        var acknowledgement = new TaskCompletionSource();
        var log = new DiagnosticLog();
        var result = await ClipboardTextWriter.TryWriteAsync(data =>
        { written = data; return acknowledgement.Task; },
            () => Task.FromResult(ownsData ? written : null), "private selection", log, TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(ownsData, result);
        Assert.False(acknowledgement.Task.IsCompleted);
        acknowledgement.SetException(new IOException("private error details"));
        Assert.Contains(log.Snapshot(), entry => entry.Message.Contains("Platform write faulted"));
        Assert.DoesNotContain(log.Snapshot(), entry => entry.Message.Contains("private"));
    }

    [Fact]
    public async Task StaleOwnedDataDoesNotAuthorizeCut()
    {
        var previous = new DataTransfer(); previous.Add(DataTransferItem.CreateText("same text"));
        var pending = new TaskCompletionSource();
        Assert.False(await ClipboardTextWriter.TryWriteAsync(_ => pending.Task,
            () => Task.FromResult<IAsyncDataTransfer?>(previous), "same text", new(), TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken));
        pending.SetResult();
    }

    [Fact]
    public async Task ClipboardFailureOrUnresponsiveOwnershipCheckDoesNotAuthorizeCut()
    {
        var log = new DiagnosticLog();
        Assert.False(await ClipboardTextWriter.TryWriteAsync(_ => Task.FromException(new IOException("secret")),
            () => throw new InvalidOperationException("Must not query ownership after failure"),
            "secret", log, TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken));
        var pending = new TaskCompletionSource();
        var ownership = new TaskCompletionSource<IAsyncDataTransfer?>();
        Assert.False(await ClipboardTextWriter.TryWriteAsync(_ => pending.Task, () => ownership.Task,
            "secret", log, TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken));
        pending.SetResult(); ownership.SetResult(null);
        Assert.DoesNotContain(log.Snapshot(), entry => entry.Message.Contains("secret"));
    }

    [AvaloniaTheory]
    [InlineData("unchanged")]
    [InlineData("selection")]
    [InlineData("text")]
    [InlineData("readonly")]
    [InlineData("dispose")]
    [InlineData("blur")]
    [InlineData("detach")]
    public async Task DelayedCutOnlyEditsTheOriginalUnchangedSelection(string change)
    {
        var field = new TextBox { Text = "hello world" };
        var other = new TextBox();
        var window = new Window { Content = new StackPanel { Children = { field, other } } }; window.Show();
        var write = new TaskCompletionSource<bool>();
        var finished = new TaskCompletionSource();
        var log = new DiagnosticLog();
        log.EntryAdded += entry => { if (entry.Category == "ClipboardCut") finished.TrySetResult(); };
        using var bridge = new TextClipboardBridge(field, log, (text, _) =>
        { Assert.Equal("world", text); return write.Task; });
        try
        {
            field.Focus(); Dispatcher.UIThread.RunJobs();
            Assert.True(window.IsActive);
            field.SelectionStart = 6; field.SelectionEnd = 11;
            field.Cut();
            Assert.Equal("hello world", field.Text);
            switch (change)
            {
                case "selection": field.SelectionStart = 0; field.SelectionEnd = 5; break;
                case "text": field.Text = "new text"; break;
                case "readonly": field.IsReadOnly = true; break;
                case "dispose": bridge.Dispose(); break;
                case "blur": other.Focus(); break;
                case "detach": window.Content = null; break;
            }
            write.SetResult(true);
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(change == "unchanged" ? "hello " : change == "text" ? "new text" : "hello world", field.Text);
            if (change == "unchanged")
            {
                field.Undo();
                Assert.Equal("hello world", field.Text);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DirectPasswordCommandsCannotBypassBridgeProtection()
    {
        var field = new TextBox { Text = "secret", PasswordChar = '●' };
        var window = new Window { Content = field }; window.Show();
        var writes = 0;
        using var bridge = new TextClipboardBridge(field, new(), (_, _) => { writes++; return Task.FromResult(true); });
        try
        {
            field.Focus(); Dispatcher.UIThread.RunJobs();
            Assert.True(window.IsActive);
            field.SelectAll(); field.Copy(); field.Cut(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, writes);
            Assert.Equal("secret", field.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task OriginalPhoneMenuHasLargeTargetsAndRestoresFocusAfterCutAndPaste()
    {
        var field = new TextBox { Text = "sole", MinHeight = 48, FontSize = 16 };
        using var menuSizing = new PhoneTextMenu(field);
        using var clipboardBridge = new TextClipboardBridge(field, new());
        var window = new Window { Width = 360, Height = 640,
            Content = new StackPanel { Margin = new Thickness(16, 100), Children = { field } } };
        window.Show();
        var clipboard = Assert.IsAssignableFrom<IClipboard>(window.Clipboard);
        try
        {
            field.Focus(); Tap(field); field.SelectAll();
            var menu = Assert.IsType<MenuFlyout>(field.ContextFlyout);
            menu.Popup.ShouldUseOverlayLayer = true;
            menu.ShowAt(field); Dispatcher.UIThread.RunJobs();
            var items = menu.Items.OfType<MenuItem>().ToArray();
            Assert.All(items, item => { Assert.True(item.Bounds.Height >= 48); Assert.True(item.Bounds.Width >= 64); });
            Assert.True(menu.Popup.Child!.Bounds.Width <= 320);
            Tap(items[0]);
            Assert.True(field.IsFocused);
            Assert.Equal("", field.Text);
            Assert.Equal("sole", await clipboard.TryGetTextAsync());
            Assert.False(menu.IsOpen);
            menu.ShowAt(field); Dispatcher.UIThread.RunJobs(); Tap(items[2]);
            Assert.Equal("sole", field.Text);
            Assert.True(field.IsFocused);
            field.PasswordChar = '●'; field.SelectAll(); menu.ShowAt(field); Dispatcher.UIThread.RunJobs();
            Assert.False(items[0].IsEnabled); Assert.False(items[1].IsEnabled);
            Assert.True(items[2].IsEnabled);

            void Tap(Control control)
            {
                var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
                using var touch = window.TouchBegin(point);
                window.TouchEnd(touch, point); Dispatcher.UIThread.RunJobs();
            }
        }
        finally { await clipboard.ClearAsync(); window.Close(); }
    }
}
