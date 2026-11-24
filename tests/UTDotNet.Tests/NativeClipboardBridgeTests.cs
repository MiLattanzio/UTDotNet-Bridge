using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Threading;
using UTDotNet.Avalonia;
using UTDotNet.Core;
using UTDotNet.Diagnostics;

namespace UTDotNet.Tests;

public class NativeClipboardBridgeTests
{
    [AvaloniaFact]
    public async Task NativeCopyCutAndPastePreserveUnicodeSelectionAndUndo()
    {
        var field = new TextBox { Text = "ciao mondo", AcceptsReturn = true };
        var window = new Window { Content = field }; window.Show();
        var native = new FakeClipboard();
        using var bridge = new TextClipboardBridge(field, new(), native);
        try
        {
            field.Focus(); Dispatcher.UIThread.RunJobs(); Assert.True(window.IsActive);
            field.SelectionStart = 5; field.SelectionEnd = 10; field.Copy();
            Assert.Equal("mondo", native.Text);
            native.Text = "caffè\n🌙";
            field.Paste(); await Drain();
            Assert.Equal("ciao caffè\n🌙", field.Text);
            field.Undo(); Assert.Equal("ciao mondo", field.Text);
            field.SelectionStart = 5; field.SelectionEnd = 10; field.Cut(); await Drain();
            Assert.Equal("ciao ", field.Text); Assert.Equal("mondo", native.Text);
            field.Undo(); Assert.Equal("ciao mondo", field.Text);
            native.Accept = false;
            field.SelectAll(); field.Cut(); await Drain(); Assert.Equal("ciao mondo", field.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("unchanged")]
    [InlineData("text")]
    [InlineData("selection")]
    [InlineData("readonly")]
    [InlineData("blur")]
    [InlineData("detach")]
    [InlineData("dispose")]
    [InlineData("deactivate-reactivate")]
    public async Task LateNativePasteOnlyChangesOriginalActiveEditor(string change)
    {
        var field = new TextBox { Text = "old text" }; var other = new TextBox();
        var window = new Window { Content = new StackPanel { Children = { field, other } } }; window.Show();
        var read = new TaskCompletionSource<string?>();
        using var bridge = new TextClipboardBridge(field, new(), (_, _) => Task.FromResult(true), _ => read.Task);
        try
        {
            field.Focus(); Dispatcher.UIThread.RunJobs(); field.SelectAll(); field.Paste();
            switch (change)
            {
                case "text": field.Text = "edited"; break;
                case "selection": field.SelectionStart = field.SelectionEnd = 0; break;
                case "readonly": field.IsReadOnly = true; break;
                case "blur": other.Focus(); break;
                case "detach": window.Content = null; break;
                case "dispose": bridge.Dispose(); break;
                case "deactivate-reactivate":
                    // The pinned headless backend has no deactivation API. Simulate
                    // its notification in this test only; production uses public events.
                    typeof(WindowBase).GetMethod("HandleDeactivated",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, null);
                    Assert.False(window.IsActive);
                    window.Activate(); field.Focus(); Dispatcher.UIThread.RunJobs();
                    break;
            }
            read.SetResult("new native text"); await Drain();
            Assert.Equal(change == "unchanged" ? "new native text" : change == "text" ? "edited" : "old text", field.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task UnavailableNativeDataNeverUsesStaleX11OrDeletesSelection()
    {
        var field = new TextBox { Text = "retain me" };
        var window = new Window { Content = field }; window.Show();
        var native = new FakeClipboard();
        using var bridge = new TextClipboardBridge(field, new(), native);
        try
        {
            field.Focus(); Dispatcher.UIThread.RunJobs(); field.SelectAll();
            await window.Clipboard!.SetTextAsync("stale X11");
            field.Paste(); await Drain(); Assert.Equal("retain me", field.Text);
            native.Text = ""; field.Paste(); await Drain(); Assert.Equal("retain me", field.Text);
            native.Text = "new text"; field.IsReadOnly = true;
            var reads = native.Reads; field.Paste(); await Drain();
            Assert.Equal(reads, native.Reads); Assert.Equal("retain me", field.Text);
        }
        finally { await window.Clipboard!.ClearAsync(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task PasswordAllowsPasteButNotCopyOrCutAndMaxLengthIsRespected()
    {
        var field = new TextBox { Text = "secret", PasswordChar = '●', MaxLength = 8 };
        var window = new Window { Content = field }; window.Show();
        var native = new FakeClipboard { Text = "abcdefghijk" }; var log = new DiagnosticLog();
        using var bridge = new TextClipboardBridge(field, log, native);
        try
        {
            field.Focus(); Dispatcher.UIThread.RunJobs(); field.SelectAll();
            field.Copy(); field.Cut(); await Drain();
            Assert.Equal("abcdefghijk", native.Text); Assert.Equal("secret", field.Text);
            field.Paste(); await Drain(); Assert.Equal("abcdefgh", field.Text);
            Assert.DoesNotContain(log.Snapshot(), e => e.Message.Contains("secret") || e.Message.Contains("abcdef"));
        }
        finally { window.Close(); }
    }

    private static async Task Drain() => await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

    private sealed class FakeClipboard : ITextClipboard
    {
        public string? Text { get; set; }
        public bool Accept { get; set; } = true;
        public int Reads { get; private set; }
        public Task<bool> WriteTextAsync(string text, CancellationToken token = default)
        { if (Accept) Text = text; return Task.FromResult(Accept); }
        public Task<string?> ReadTextAsync(CancellationToken token = default)
        { Reads++; return Task.FromResult(Text); }
    }
}
