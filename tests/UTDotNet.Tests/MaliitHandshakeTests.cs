using System.Buffers.Binary;
using UTDotNet.UbuntuTouch;

namespace UTDotNet.Tests;

public class MaliitHandshakeTests
{
    private static byte[] Frame(byte type, uint serial)
    {
        var bytes = new byte[16]; bytes[0] = (byte)'l'; bytes[1] = type; bytes[3] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), serial);
        return bytes;
    }

    [Fact]
    public async Task InitializationCallbacksWaitForHandlerAndKeepTheirOrder()
    {
        var first = Frame(1, 1); var second = Frame(1, 2); var hello = Frame(3, 3); var later = Frame(1, 4);
        using var stream = new MaliitHandshakeStream(new DuplexInput([.. first, .. second, .. hello, .. later]));
        await stream.WriteAsync("BEGIN\r\n"u8.ToArray(), TestContext.Current.CancellationToken);
        var buffer = new byte[16];
        // Small reads must also preserve framing.
        await stream.ReadExactlyAsync(buffer.AsMemory(0, 7), TestContext.Current.CancellationToken);
        await stream.ReadExactlyAsync(buffer.AsMemory(7), TestContext.Current.CancellationToken);
        Assert.Equal(hello, buffer);
        var waiting = stream.ReadAsync(buffer, TestContext.Current.CancellationToken).AsTask();
        Assert.False(waiting.IsCompleted);
        stream.ReleaseCallbacks();
        Assert.Equal(16, await waiting);
        Assert.Equal(first, buffer);
        await stream.ReadExactlyAsync(buffer, TestContext.Current.CancellationToken);
        Assert.Equal(second, buffer);
        await stream.ReadExactlyAsync(buffer, TestContext.Current.CancellationToken);
        Assert.Equal(later, buffer);
    }

    [Fact]
    public async Task DisposeAbortsWaitingForRegistration()
    {
        using var stream = new MaliitHandshakeStream(new DuplexInput(Frame(3, 1)));
        await stream.WriteAsync("BEGIN\r\n"u8.ToArray(), TestContext.Current.CancellationToken);
        var buffer = new byte[16];
        await stream.ReadExactlyAsync(buffer, TestContext.Current.CancellationToken);
        var waiting = stream.ReadAsync(buffer, TestContext.Current.CancellationToken).AsTask();
        stream.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }

    [Fact]
    public async Task OversizedInitializationIsRejectedBeforeAllocation()
    {
        var header = Frame(1, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), uint.MaxValue);
        using var stream = new MaliitHandshakeStream(new DuplexInput(header));
        await stream.WriteAsync("BEGIN\r\n"u8.ToArray(), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(() => stream.ReadAsync(new byte[16], TestContext.Current.CancellationToken).AsTask());
    }

    private sealed class DuplexInput(byte[] input) : MemoryStream(input)
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
