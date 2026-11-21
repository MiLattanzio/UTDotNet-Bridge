using Avalonia.Logging;

namespace UTDotNet.InputDemo;

/// <summary>Expose swallowed clipboard failures without logging clipboard data or exception messages.</summary>
internal sealed class ClipboardTraceSink : ILogSink
{
    public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning && area == LogArea.Control;
    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
        Log(level, area, source, messageTemplate, []);
    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        if (!IsEnabled(level, area)) return;
        var operation = messageTemplate switch
        {
            "Failed to write text to clipboard: {Error}" => "write",
            "Failed to read text from clipboard: {Error}" => "read",
            _ => null
        };
        if (operation is not null)
            Console.Error.WriteLine($"ClipboardFailure: {operation}; exception={propertyValues.OfType<Exception>().FirstOrDefault()?.GetType().Name ?? "Unknown"}; content redacted");
    }
}
