using System.Text.Json;

namespace W1sleyEdition;

public sealed class CrashDetectorService(ActionLogger logger)
{
    private readonly ActionLogger _logger = logger;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public async Task<IReadOnlyList<CrashEventItem>> ScanAsync(int days = 30)
    {
        days = Math.Clamp(days, 1, 90);
        var script =
            "$ids=@(13,14,17,18,19,41,46,153,1001,4101,6008);" +
            $"$start=(Get-Date).AddDays(-{days});" +
            "$items=@(Get-WinEvent -FilterHashtable @{LogName='System';StartTime=$start;Id=$ids} -ErrorAction SilentlyContinue | " +
            "Where-Object {($_.ProviderName -match 'Kernel-Power|EventLog|BugCheck|WER-SystemErrorReporting|WHEA-Logger|Display|nvlddmkm')} | " +
            "Select-Object -First 180 @{n='TimeCreated';e={$_.TimeCreated.ToString('o')}},Id,ProviderName,LevelDisplayName,@{n='Message';e={($_.Message -replace '\\s+',' ').Trim()}});" +
            "ConvertTo-Json -InputObject @($items) -Depth 4 -Compress";

        var result = await ProcessRunner.RunPowerShellAsync(script);
        if (result.ExitCode != 0)
            throw new InvalidOperationException("Çökme olayları okunamadı: " + CleanError(result));

        var payload = JsonSerializer.Deserialize<List<CrashPayload>>(string.IsNullOrWhiteSpace(result.Output) ? "[]" : result.Output, _jsonOptions) ?? [];
        var powerLogs = _logger.ReadRecent(2000)
            .Where(entry => entry.Success && (entry.Action == "Güç modu" || entry.Action == "Güvenli güç kurtarması"))
            .OrderBy(entry => entry.Timestamp)
            .ToList();

        var mapped = payload
            .Select(item => Map(item, FindPowerMode(powerLogs, item.TimeCreated)))
            .Where(item => item is not null)
            .Cast<CrashEventItem>()
            .OrderByDescending(item => item.Timestamp)
            .ToList();
        return CollapseRelatedCrashRecords(mapped);
    }

    public CrashEventItem? GetUnnotifiedCritical(IEnumerable<CrashEventItem> events)
    {
        var latest = events.Where(item => item.Severity == "Kritik").OrderByDescending(item => item.Timestamp).FirstOrDefault();
        if (latest is null) return null;
        var state = LoadState();
        return latest.Timestamp > state.LastNotified ? latest : null;
    }

    public void MarkNotified(CrashEventItem item)
    {
        AppPaths.EnsureCreated();
        File.WriteAllText(AppPaths.CrashDetectorStatePath,
            JsonSerializer.Serialize(new CrashDetectorState { LastNotified = item.Timestamp }, _jsonOptions));
    }

    private CrashDetectorState LoadState()
    {
        try
        {
            if (File.Exists(AppPaths.CrashDetectorStatePath))
                return JsonSerializer.Deserialize<CrashDetectorState>(File.ReadAllText(AppPaths.CrashDetectorStatePath), _jsonOptions) ?? new CrashDetectorState();
        }
        catch { }
        return new CrashDetectorState();
    }

    private static CrashEventItem? Map(CrashPayload payload, string mode)
    {
        if (!DateTimeOffset.TryParse(payload.TimeCreated, out var timestamp)) return null;
        var provider = payload.ProviderName ?? "Windows";
        var message = Normalize(payload.Message);
        var isWhea = provider.Contains("WHEA", StringComparison.OrdinalIgnoreCase) && payload.Id is 17 or 18 or 19 or 46;
        var isDisplay = (provider.Equals("Display", StringComparison.OrdinalIgnoreCase) && payload.Id == 4101) ||
                        (provider.Contains("nvlddmkm", StringComparison.OrdinalIgnoreCase) && payload.Id is 13 or 14 or 153);
        var isUnexpected = (provider.Contains("Kernel-Power", StringComparison.OrdinalIgnoreCase) && payload.Id == 41) ||
                           (provider.Equals("EventLog", StringComparison.OrdinalIgnoreCase) && payload.Id == 6008);
        var isBugCheck = payload.Id == 1001 &&
                         (provider.Contains("BugCheck", StringComparison.OrdinalIgnoreCase) || provider.Contains("WER-SystemErrorReporting", StringComparison.OrdinalIgnoreCase));
        if (!isWhea && !isDisplay && !isUnexpected && !isBugCheck) return null;
        var critical = isUnexpected || isBugCheck || (isWhea && payload.Id is 18 or 46);

        var title = isUnexpected
            ? "Beklenmeyen kapanma / yeniden başlatma"
            : isBugCheck
                ? "Windows hata denetimi (BugCheck)"
                : isWhea
                    ? "Donanım hata kaydı (WHEA)"
                    : isDisplay
                        ? "Ekran sürücüsü sıfırlaması"
                        : "Sistem kararlılık olayı";

        return new CrashEventItem
        {
            Timestamp = timestamp,
            Severity = critical ? "Kritik" : "Uyarı",
            Category = isWhea ? "Donanım" : isDisplay ? "GPU" : isBugCheck ? "Mavi Ekran" : "Güç",
            Provider = provider,
            EventId = payload.Id,
            Title = title,
            Detail = string.IsNullOrWhiteSpace(message) ? "Windows olay günlüğü ayrıntı sağlamadı." : message,
            PowerMode = mode
        };
    }

    private static string FindPowerMode(IReadOnlyList<ActionLogEntry> logs, string timeText)
    {
        if (!DateTimeOffset.TryParse(timeText, out var time)) return "Bilinmiyor";
        var entry = logs.LastOrDefault(item => item.Timestamp <= time);
        if (entry is null) return "Bilinmiyor";
        if (entry.Action == "Güvenli güç kurtarması") return "Kararlı Performans";
        var arrow = entry.Detail.LastIndexOf('→');
        return arrow >= 0 ? entry.Detail[(arrow + 1)..].Trim() : entry.Detail;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var normalized = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 600 ? normalized : normalized[..600] + "…";
    }

    private static IReadOnlyList<CrashEventItem> CollapseRelatedCrashRecords(IEnumerable<CrashEventItem> source)
    {
        var output = new List<CrashEventItem>();
        foreach (var item in source.OrderByDescending(item => item.Timestamp))
        {
            var isShutdownRecord = item.Category is "Güç" or "Mavi Ekran";
            if (!isShutdownRecord)
            {
                output.Add(item);
                continue;
            }

            var relatedIndex = output.FindIndex(existing =>
                (existing.Category is "Güç" or "Mavi Ekran") &&
                (existing.Timestamp - item.Timestamp).Duration() <= TimeSpan.FromMinutes(2));
            if (relatedIndex < 0)
            {
                output.Add(item);
                continue;
            }

            var existing = output[relatedIndex];
            if (CrashRecordPriority(item) > CrashRecordPriority(existing)) output[relatedIndex] = item;
        }
        return output.OrderByDescending(item => item.Timestamp).ToList();
    }

    private static int CrashRecordPriority(CrashEventItem item) => item.Category switch
    {
        "Mavi Ekran" => 3,
        "Güç" when item.Provider.Contains("Kernel-Power", StringComparison.OrdinalIgnoreCase) => 2,
        _ => 1
    };

    private static string CleanError(ProcessResult result) =>
        string.IsNullOrWhiteSpace(result.Error) ? result.Output.Trim() : result.Error.Trim();

    private sealed class CrashPayload
    {
        public string TimeCreated { get; set; } = "";
        public int Id { get; set; }
        public string ProviderName { get; set; } = "";
        public string LevelDisplayName { get; set; } = "";
        public string Message { get; set; } = "";
    }

    private sealed class CrashDetectorState
    {
        public DateTimeOffset LastNotified { get; set; } = DateTimeOffset.MinValue;
    }
}
