using System.Text.Json;

namespace Replane;

/// <summary>
/// A parsed Server-Sent Event.
/// </summary>
public sealed record SseEvent
{
    /// <summary>Event type (e.g., "init", "config_change").</summary>
    public string? EventType { get; init; }

    /// <summary>Parsed JSON data from the event.</summary>
    public JsonElement? Data { get; init; }

    /// <summary>Raw data string if JSON parsing failed.</summary>
    public string? RawData { get; init; }

    /// <summary>Optional event ID.</summary>
    public string? Id { get; init; }

    /// <summary>Optional retry interval in milliseconds.</summary>
    public int? Retry { get; init; }
}

/// <summary>
/// Incremental SSE parser that handles partial data.
/// </summary>
public sealed class SseParser
{
    private string _buffer = "";
    private string? _eventType;
    private readonly List<string> _dataLines = [];
    private string? _eventId;
    private int? _retry;

    /// <summary>
    /// Feed a chunk of data to the parser and yield complete events.
    /// </summary>
    public IEnumerable<SseEvent> Feed(string chunk)
    {
        _buffer += chunk;

        while (_buffer.Contains('\n'))
        {
            var newlineIndex = _buffer.IndexOf('\n');
            var line = _buffer[..newlineIndex];
            _buffer = _buffer[(newlineIndex + 1)..];

            // Remove optional carriage return
            if (line.EndsWith('\r'))
            {
                line = line[..^1];
            }

            // Empty line signals end of event
            if (string.IsNullOrEmpty(line))
            {
                if (_dataLines.Count > 0)
                {
                    yield return EmitEvent();
                }
                continue;
            }

            // Skip comments
            if (line.StartsWith(':'))
            {
                continue;
            }

            // Parse field
            string field;
            string value;
            var colonIndex = line.IndexOf(':');
            if (colonIndex >= 0)
            {
                field = line[..colonIndex];
                value = line[(colonIndex + 1)..];
                // Remove single leading space from value if present
                if (value.StartsWith(' '))
                {
                    value = value[1..];
                }
            }
            else
            {
                field = line;
                value = "";
            }

            switch (field)
            {
                case "event":
                    _eventType = value;
                    break;
                case "data":
                    _dataLines.Add(value);
                    break;
                case "id":
                    _eventId = value;
                    break;
                case "retry":
                    if (int.TryParse(value, out var retry))
                    {
                        _retry = retry;
                    }
                    break;
            }
        }
    }

    private SseEvent EmitEvent()
    {
        var dataStr = string.Join("\n", _dataLines);

        JsonElement? jsonData = null;
        string? rawData = null;

        try
        {
            using var doc = JsonDocument.Parse(dataStr);
            jsonData = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            rawData = dataStr;
        }

        var evt = new SseEvent
        {
            EventType = _eventType,
            Data = jsonData,
            RawData = rawData,
            Id = _eventId,
            Retry = _retry
        };

        // Reset for next event
        _eventType = null;
        _dataLines.Clear();
        // Note: id and retry persist across events per SSE spec

        return evt;
    }

    /// <summary>
    /// Reset the parser state.
    /// </summary>
    public void Reset()
    {
        _buffer = "";
        _eventType = null;
        _dataLines.Clear();
        _eventId = null;
        _retry = null;
    }
}

/// <summary>
/// Helper methods for SSE parsing.
/// </summary>
public static class SseHelper
{
    /// <summary>
    /// Parse an SSE stream from string chunks.
    /// </summary>
    public static IEnumerable<SseEvent> ParseStream(IEnumerable<string> chunks)
    {
        var parser = new SseParser();
        foreach (var chunk in chunks)
        {
            foreach (var evt in parser.Feed(chunk))
            {
                yield return evt;
            }
        }
    }

    /// <summary>
    /// Parse an SSE stream from async string chunks.
    /// </summary>
    public static async IAsyncEnumerable<SseEvent> ParseStreamAsync(
        IAsyncEnumerable<string> chunks,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var parser = new SseParser();
        await foreach (var chunk in chunks.WithCancellation(cancellationToken))
        {
            foreach (var evt in parser.Feed(chunk))
            {
                yield return evt;
            }
        }
    }
}
