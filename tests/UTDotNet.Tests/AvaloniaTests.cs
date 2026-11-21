using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Input.TextInput;
using Avalonia.Layout;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UTDotNet.Avalonia;
using UTDotNet.Core;
using UTDotNet.Diagnostics;
using UTDotNet.InputDemo;

[assembly: AvaloniaTestApplication(typeof(UTDotNet.Tests.TestAppBuilder))]

namespace UTDotNet.Tests;

public sealed class TestApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public class AvaloniaTests
{
    [AvaloniaFact]
    public void DesktopTextEditingRemainsFunctionalAndContentIsRedacted()
    {
        var field = new TextBox();
        var other = new TextBox();
        var log = new DiagnosticLog();
        using var adapter = new TextInputAdapter(field, "plain", new(), log);
        var window = new Window { Content = new StackPanel { Children = { field, other } } };
        window.Show();
        try
        {
            field.Focus();
            window.KeyTextInput("private text");
            Assert.Equal("private text", field.Text);
            window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, "\b");
            window.KeyRelease(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, "\b");
            Assert.Equal("private tex", field.Text);
            other.Focus();
            Assert.Contains(log.Snapshot(), e => e.Category == "FocusGained");
            Assert.Contains(log.Snapshot(), e => e.Category == "FocusLost");
            Assert.DoesNotContain(log.Snapshot(), e => e.Message.Contains("private"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PasswordLoggingDoesNotExposeContentLengthOrCharacterKeys()
    {
        var field = new TextBox { PasswordChar = '●' };
        var log = new DiagnosticLog();
        using var adapter = new TextInputAdapter(field, "password", new() { InputType = InputType.Password, AutoCorrect = true }, log);
        var window = new Window { Content = field }; window.Show();
        try
        {
            field.Focus(); window.KeyTextInput("top-secret");
            Assert.Equal("top-secret", field.Text);
            Assert.False(TextInputOptions.GetShowSuggestions(field));
            Assert.True(TextInputOptions.GetIsSensitive(field));
            Assert.DoesNotContain(log.Snapshot(), e => e.Message.Contains("top-secret") || e.Message.Contains("length=") || e.Message.Contains("characters="));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AdapterDisposalDetachesTextAndPreeditObservers()
    {
        var field = new TextBox(); var log = new DiagnosticLog();
        var adapter = new TextInputAdapter(field, "field", new(), log);
        var window = new Window { Content = field }; window.Show();
        try
        {
            field.Focus();
            var presenter = field.GetVisualDescendants().OfType<TextPresenter>().Single();
            presenter.PreeditText = "composition";
            Assert.Contains(log.Snapshot(), e => e.Category == "PreeditChanged");
            adapter.Dispose(); adapter.Dispose();
            var count = log.Snapshot().Count;
            field.Text = "after disposal"; presenter.PreeditText = "changed";
            Assert.Equal(count, log.Snapshot().Count);
        }
        finally { adapter.Dispose(); window.Close(); }
    }

    [AvaloniaFact]
    public void ChangingInputRequestUpdatesHintsWithoutEditingTheText()
    {
        var field = new TextBox { Text = "preserved" }; var log = new DiagnosticLog();
        using var adapter = new TextInputAdapter(field, "field", new(), log);
        adapter.UpdateRequest(new() { InputType = InputType.Email });
        Assert.Equal(TextInputContentType.Email, TextInputOptions.GetContentType(field));
        Assert.Equal("preserved", field.Text);
        Assert.Contains(log.Snapshot(), e => e.Category == "InputTypeChanged");
    }

    [AvaloniaFact]
    public async Task DialogFieldReceivesTextAndDisposalStopsObserving()
    {
        var parent = new Window(); parent.Show();
        var field = new TextBox(); var dialog = new Window { Content = field };
        var log = new DiagnosticLog(); using var adapter = new TextInputAdapter(field, "dialog", new(), log);
        var completion = dialog.ShowDialog(parent);
        try
        {
            field.Focus(); dialog.KeyTextInput("dialog input");
            Assert.Equal("dialog input", field.Text);
            Assert.Contains(log.Snapshot(), e => e.Category == "FocusGained");
        }
        finally { dialog.Close(); parent.Close(); }
        await completion;
    }

    [AvaloniaFact]
    public void DemoFitsNarrowViewportAndContainsAllInputCases()
    {
        var window = new MainWindow(new SessionBusDiagnostics(new EmptyBus())) { Width = 360, Height = 640 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var inputs = window.GetVisualDescendants().OfType<TextBox>().ToArray();
            Assert.Contains(inputs, field => field.PasswordChar != '\0');
            Assert.Contains(inputs, field => field.AcceptsReturn);
            Assert.True(window.ClientSize.Width <= 360, $"Client size: {window.ClientSize}");
            Assert.All(inputs, field => Assert.True(field.Bounds.Width <= 360, $"Input bounds: {field.Bounds}"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task FocusedFieldStaysAboveKeyboardAcrossPhoneRotation()
    {
        var session = new MaliitTests.FakeSession();
        using var bridge = new MaliitInputBridge(new(), () => session);
        var window = new MainWindow(new SessionBusDiagnostics(new EmptyBus()), bridge) { Width = 900, Height = 384 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var field = window.GetVisualDescendants().OfType<TextBox>().Single(f => f.Name == "Standardtext");
            field.Text = "preserved across rotation";
            field.Focus(); Dispatcher.UIThread.RunJobs();
            await session.DispatchAsync!(new UTDotNet.UbuntuTouch.InputAreaChanged(new(0, 152, 900, 232)));
            Dispatcher.UIThread.RunJobs();
            AssertVisibleAbove(152);
            window.Width = 360; window.Height = 640;
            Dispatcher.UIThread.RunJobs();
            await session.DispatchAsync!(new UTDotNet.UbuntuTouch.InputAreaChanged(new(0, 400, 360, 240)));
            Dispatcher.UIThread.RunJobs();
            AssertVisibleAbove(400);
            Assert.Equal("preserved across rotation", field.Text);
            void AssertVisibleAbove(double keyboardTop)
            {
                var position = field.TranslatePoint(default, window)!.Value;
                Assert.True(position.Y >= 0 && position.Y + field.Bounds.Height <= keyboardTop + 1,
                    $"Field {position}, {field.Bounds.Size}, keyboard top {keyboardTop}");
                Assert.True(field.Bounds.Height >= 48);
                Assert.Equal(16, field.FontSize);
            }
        }
        finally { window.Close(); }
    }

    private sealed class EmptyBus : ICommandRunner
    {
        public Task<CommandResult> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
            Task.FromResult(new CommandResult(true, 0, "(['org.freedesktop.DBus'],)", ""));
    }
}
