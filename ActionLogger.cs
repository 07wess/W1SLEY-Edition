using System.Text.Json;

namespace W1sleyEdition;

public sealed class ActionLogger
{
    private readonly JsonSerializerOptions _options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly SemaphoreSlim _lock = new(1, 1);

    public event EventHandler<ActionLogEntry>? EntryWritten;

    public void Log(string action, string detail, bool success, long? memoryFreed)
    {
        _ = LogAsync(new ActionLogEntry
        {
            Action = action,
            Detail = detail,
            Success = success,
            MemoryFreed = memoryFreed,
            Timestamp = DateTimeOffset.Now
        });
    }

    public async Task LogAsync(ActionLogEntry entry)
    {
        AppPaths.EnsureCreated();
        var line = JsonSerializer.Serialize(entry, _options) + Environment.NewLine;
        await _lock.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(AppPaths.ActionLogPath, line);
        }
        finally
        {
            _lock.Release();
        }
        EntryWritten?.Invoke(this, entry);
    }

    public IReadOnlyList<ActionLogEntry> ReadRecent(int maximum = 250)
    {
        if (!File.Exists(AppPaths.ActionLogPath)) return [];
        var entries = new List<ActionLogEntry>();
        foreach (var line in File.ReadLines(AppPaths.ActionLogPath).TakeLast(maximum))
        {
            try
            {
                var entry = JsonSerializer.Deserialize<ActionLogEntry>(line, _options);
                if (entry is not null) entries.Add(entry);
            }
            catch { }
        }
        entries.Reverse();
        return entries;
    }
}
