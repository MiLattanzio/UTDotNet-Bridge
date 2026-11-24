using System.Buffers.Binary;
using System.Diagnostics;
using Tmds.DBus.Protocol;
using UTDotNet.Diagnostics;
using UTDotNet.UbuntuTouch;

namespace UTDotNet.Tests;

public class ContentHubClipboardTests
{
    // Little-endian Content Hub table + text/plain + UTF-8 "luna è 🌙".
    private static byte[] NativeFixture => Convert.FromHexString(
        "01000000140000000a0000001e0000000c000000746578742f706c61696e6c756e6120c3a820f09f8c99");

    [Fact]
    public void NativeFixtureAndUnicodeRoundTrip()
    {
        Assert.Equal("luna è 🌙", ContentHubTextCodec.Decode(NativeFixture));
        Assert.Equal(NativeFixture, ContentHubTextCodec.Encode("luna è 🌙"));
        const string multiline = "Caffè, città, 日本語\nseconda riga\r\n🌙";
        Assert.Equal(multiline, ContentHubTextCodec.Decode(ContentHubTextCodec.Encode(multiline)));
        Assert.Equal("", ContentHubTextCodec.Decode(ContentHubTextCodec.Encode("")));
    }

    [Theory]
    [InlineData(0, -1)]
    [InlineData(0, 17)]
    [InlineData(0, int.MaxValue)]
    [InlineData(4, -1)]
    [InlineData(4, 0)]
    [InlineData(8, int.MaxValue)]
    [InlineData(12, int.MaxValue)]
    [InlineData(16, -1)]
    [InlineData(16, int.MaxValue)]
    public void MalformedNativeRangesAreRejected(int offset, int value)
    {
        var bytes = NativeFixture;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
        Assert.Null(ContentHubTextCodec.Decode(bytes));
    }

    [Fact]
    public void TruncatedNonTextInvalidUtf8AndOversizedDataAreRejected()
    {
        for (var length = 0; length < NativeFixture.Length; length++)
            Assert.Null(ContentHubTextCodec.Decode(NativeFixture.AsSpan(0, length)));
        var nonText = NativeFixture; nonText[20] = (byte)'X';
        Assert.Null(ContentHubTextCodec.Decode(nonText));
        var invalidUtf8 = NativeFixture; invalidUtf8[^1] = 0xff;
        Assert.Null(ContentHubTextCodec.Decode(invalidUtf8));
        Assert.Null(ContentHubTextCodec.Decode(new byte[ContentHubTextCodec.MaximumBytes + 1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => ContentHubTextCodec.Encode(new string('x', ContentHubTextCodec.MaximumBytes)));
    }

    [Fact]
    public async Task SessionBusProtocolUsesCallerIdentityAndObservesRejectionTimeoutAndCancellation()
    {
        using var daemon = Process.Start(new ProcessStartInfo("dbus-daemon")
        {
            ArgumentList = { "--session", "--nofork", "--print-address=1", "--address=unix:tmpdir=/tmp" },
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        })!;
        try
        {
            var address = await daemon.StandardOutput.ReadLineAsync(TestContext.Current.CancellationToken);
            Assert.NotNull(address);
            using var server = new DBusConnection(address);
            await server.ConnectAsync();
            var handler = new FakeContentHub(); server.AddMethodHandler(handler);
            await server.RequestNameAsync("com.lomiri.content.dbus.Service");
            var log = new DiagnosticLog();
            var clipboard = new ContentHubClipboard(log, address, "test.app_main_1", TimeSpan.FromMilliseconds(400));
            Assert.True(await clipboard.WriteTextAsync("luna è 🌙", TestContext.Current.CancellationToken));
            Assert.Equal("test.app_main_1", handler.AppId);
            Assert.Equal(NativeFixture, handler.Data);
            Assert.Equal("luna è 🌙", await clipboard.ReadTextAsync(TestContext.Current.CancellationToken));
            handler.Accept = false;
            Assert.False(await clipboard.WriteTextAsync("rejected", TestContext.Current.CancellationToken));
            Assert.Equal("luna è 🌙", await clipboard.ReadTextAsync(TestContext.Current.CancellationToken));
            handler.NoReply = true;
            Assert.Null(await clipboard.ReadTextAsync(TestContext.Current.CancellationToken));
            Assert.False(await clipboard.WriteTextAsync("timed out", TestContext.Current.CancellationToken));
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            Assert.False(await clipboard.WriteTextAsync("cancelled", cancelled.Token));
            Assert.Null(await clipboard.ReadTextAsync(cancelled.Token));
            Assert.DoesNotContain(log.Snapshot(), entry => entry.Message.Contains("luna") || entry.Message.Contains("🌙"));
        }
        finally
        {
            if (!daemon.HasExited) daemon.Kill();
            await daemon.WaitForExitAsync(TestContext.Current.CancellationToken);
        }
    }

    private sealed class FakeContentHub : IPathMethodHandler
    {
        public string Path => "/";
        public bool HandlesChildPaths => false;
        public bool Accept { get; set; } = true;
        public bool NoReply { get; set; }
        public string? AppId { get; private set; }
        public byte[] Data { get; private set; } = [];
        public ValueTask HandleMethodAsync(MethodContext context)
        {
            if (NoReply) return ValueTask.CompletedTask;
            var reader = context.Request.GetBodyReader();
            if (context.Request.MemberAsString == "CreatePaste")
            {
                Assert.Equal("ssayas", context.Request.SignatureAsString);
                AppId = reader.ReadString(); Assert.Equal("", reader.ReadString());
                var data = reader.ReadArrayOfByte();
                Assert.Equal(new[] { "text/plain" }, reader.ReadArrayOfString());
                if (Accept) Data = data;
                using var writer = context.CreateReplyWriter("b"); writer.WriteBool(Accept);
                context.Reply(writer.CreateMessage());
            }
            else
            {
                Assert.Equal("GetLatestPasteData", context.Request.MemberAsString);
                Assert.Equal("", reader.ReadString());
                using var writer = context.CreateReplyWriter("ay"); writer.WriteArray(Data);
                context.Reply(writer.CreateMessage());
            }
            return ValueTask.CompletedTask;
        }
    }
}
