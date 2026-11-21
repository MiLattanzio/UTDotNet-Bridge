using UTDotNet.Core;
using UTDotNet.Diagnostics;

namespace UTDotNet.Tests;

public class CoreTests
{
    private static PlatformSnapshot Linux(string desktop = "", string release = "ID=ubuntu\nPRETTY_NAME=Ubuntu", string image = "") => new()
    { IsLinux = true, OsRelease = release, SystemImageChannel = image, Environment = new Dictionary<string, string> { ["XDG_CURRENT_DESKTOP"] = desktop } };

    [Theory]
    [InlineData("Lomiri")]
    [InlineData("Unity8")]
    [InlineData("ubuntu-touch")]
    [InlineData("")]
    public void LinuxOrLomiriAloneDoesNotEnableIntegration(string desktop) =>
        Assert.Equal(LinuxMobileEnvironment.Unknown, PlatformDetector.Detect(Linux(desktop)).MobileEnvironment);

    [Theory]
    [InlineData("ubuntu:GNOME")]
    [InlineData("KDE")]
    [InlineData("XFCE")]
    public void DesktopSessionRemainsDesktop(string desktop) =>
        Assert.Equal(LinuxMobileEnvironment.DesktopLinux, PlatformDetector.Detect(Linux(desktop)).MobileEnvironment);

    [Fact]
    public void RealFairphoneOsReleaseIsRecognizedWithoutSessionVariables()
    {
        var result = PlatformDetector.Detect(Linux(release: "ID=ubuntu\nVARIANT=Touch\nVARIANT_ID=touch\nPRETTY_NAME=\"Ubuntu 24.04.5 LTS\""));
        Assert.Equal(LinuxMobileEnvironment.UbuntuTouch, result.MobileEnvironment);
        Assert.Equal(DetectionConfidence.High, result.Confidence);
        Assert.Equal("Ubuntu 24.04.5 LTS", result.Distribution);
    }
    [Fact]
    public void RealSystemImageColonSyntaxIsRecognized() =>
        Assert.Equal(LinuxMobileEnvironment.UbuntuTouch, PlatformDetector.Detect(Linux(image:
            "[service]\nbase: system-image.ubports.com\nchannel: 24.04-2.x/arm64/android9plus/stable\nversion_detail: rootfs=build" )).MobileEnvironment);

    [Fact]
    public void GenericSystemImageAndMaliitAreNotProof()
    {
        var snapshot = Linux(image: "[service]\nbase: example.org\nchannel: stable");
        Assert.Equal(LinuxMobileEnvironment.Unknown, PlatformDetector.Detect(snapshot with
        { Environment = new Dictionary<string, string> { ["QT_IM_MODULE"] = "maliit" } }).MobileEnvironment);
    }
    [Fact]
    public void NonLinuxCannotBeUbuntuTouch() => Assert.Equal(LinuxMobileEnvironment.Unknown,
        PlatformDetector.Detect(Linux(release: "ID=ubuntu-touch") with { IsLinux = false }).MobileEnvironment);

    [Fact]
    public void BothDisplayVariablesDoNotEstablishBackend()
    {
        var info = PlatformDetector.Detect(Linux() with { Environment = new Dictionary<string, string>
            { ["DISPLAY"] = ":0", ["WAYLAND_DISPLAY"] = "wayland-0" } });
        Assert.StartsWith("Unknown", info.DisplayBackend);
        Assert.Contains(info.Evidence, e => e.Contains("XWayland not established"));
    }
    [Fact]
    public void PasswordRequestsDisablePredictionAndMultiline()
    {
        var input = new InputRequest { InputType = InputType.Password, Multiline = true, AutoCorrect = true, AutoCapitalization = true };
        var result = input.Normalize();
        Assert.False(result.AutoCorrect || result.AutoCapitalization || result.Multiline);
        Assert.True(input.AutoCorrect);
    }
    [Fact]
    public async Task UnsupportedRequestsNeverReportSuccessOrVisibility()
    {
        var service = new UnsupportedPlatformInputService();
        var token = TestContext.Current.CancellationToken;
        var results = new[] { await service.ShowKeyboardAsync(new(), token), await service.HideKeyboardAsync(token), await service.SetInputTypeAsync(InputType.Email, token) };
        Assert.False(service.IsAvailable);
        Assert.All(results, result => { Assert.Equal(InputOperationStatus.Unsupported, result.Status); Assert.Equal(KeyboardVisibility.Unknown, result.Visibility); });
    }
    [Fact]
    public async Task CancelledRequestIsNotSilentlyIgnored()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new UnsupportedPlatformInputService().HideKeyboardAsync(cancellation.Token));
    }
    [Fact]
    public void InvalidInputTypeIsRejected() => Assert.Throws<ArgumentOutOfRangeException>(() => new InputRequest { InputType = (InputType)999 }.Normalize());

    [Fact]
    public void LogHistoryIsBoundedAndKeepsLatestEvents()
    {
        var log = new DiagnosticLog(2);
        log.Write("test", "one"); log.Write("test", "two"); log.Write("test", "three");
        Assert.Equal(new[] { "two", "three" }, log.Snapshot().Select(e => e.Message));
    }
}
