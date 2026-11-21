using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace UTDotNet.Diagnostics;

public sealed record DiagnosticEvent(DateTimeOffset Timestamp, string Category, string Message, LogLevel Level)
{
    public override string ToString() => $"{Timestamp:HH:mm:ss.fff} [{Level}] {Category}: {Message}";
}

/// <summary>Bounded, thread-safe history. Subscribers run on the caller's thread.</summary>
public sealed class DiagnosticLog(int capacity = 300, ILogger? logger = null)
{
    private readonly int _capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    private readonly ILogger _logger = logger ?? NullLogger.Instance;
    private readonly Queue<DiagnosticEvent> _events = new();
    private readonly object _gate = new();
    public event Action<DiagnosticEvent>? EntryAdded;

    /// <summary>Do not include user-entered text, passwords or surrounding text in messages.</summary>
    public void Write(string category, string message, LogLevel level = LogLevel.Information)
    {
        var entry = new DiagnosticEvent(DateTimeOffset.UtcNow, category, message, level);
        lock (_gate)
        {
            _events.Enqueue(entry);
            while (_events.Count > _capacity) _events.Dequeue();
        }
        _logger.Log(level, "{Category}: {Message}", category, message);
        EntryAdded?.Invoke(entry);
    }

    public IReadOnlyList<DiagnosticEvent> Snapshot()
    {
        lock (_gate) return _events.ToArray();
    }
}
