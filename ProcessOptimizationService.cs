using System.Diagnostics;
using System.Runtime.InteropServices;

namespace W1sleyEdition;

public sealed class ProcessOptimizationService(ActionLogger logger)
{
    private readonly ActionLogger _logger = logger;

    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "registry", "secure system", "memory compression", "smss", "csrss", "wininit",
        "services", "lsass", "svchost", "winlogon", "dwm", "explorer", "fontdrvhost", "sihost", "taskhostw",
        "audiodg", "spoolsv", "searchindexer", "startmenuexperiencehost", "shellexperiencehost", "searchhost",
        "securityhealthservice", "securityhealthsystray", "msmpeng", "nissrv", "mpcmdrun", "w1sleyedition",
        "steam", "steamwebhelper", "discord", "brave", "faceit", "faceitservice", "faceitclient", "faceitac",
        "obs64", "nvcontainer", "nvidia share", "rtss", "rtsshookloader64"
    };

    private static readonly HashSet<string> SuggestedBackgroundNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "widgets", "widgetservice", "phoneexperiencehost", "yourphone", "ms-teams", "msteams", "copilot",
        "onedrive", "gamebar", "gamebarftserver", "gamebarpresencewriter", "xboxapp", "xboxpcapp",
        "crossdeviceresume", "mobiledevices", "peopleexperiencehost", "quickassist", "opera_assistant",
        "adobearm", "acrotray", "ccxprocess", "creative cloud", "adobeipcbroker", "linktomylg",
        "epicwebhelper", "curseforge", "overwolfbrowser", "whatsapp", "telegram", "spotify"
    };

    private static readonly HashSet<string> TrimAllowList = new(StringComparer.OrdinalIgnoreCase)
    {
        "widgets", "widgetservice", "phoneexperiencehost", "yourphone", "ms-teams", "msteams", "copilot",
        "onedrive", "gamebar", "gamebarftserver", "xboxapp", "xboxpcapp", "crossdeviceresume", "mobiledevices",
        "peopleexperiencehost", "quickassist", "opera_assistant", "adobearm", "acrotray", "ccxprocess",
        "adobeipcbroker", "epicwebhelper", "curseforge", "overwolfbrowser", "whatsapp", "telegram", "spotify",
        "msedgewebview2"
    };

    public IReadOnlyList<ProcessItem> GetProcesses()
    {
        var currentId = Environment.ProcessId;
        var list = new List<ProcessItem>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == currentId || process.HasExited) continue;
                var hasVisibleWindow = !string.IsNullOrWhiteSpace(process.MainWindowTitle);
                var recommended = SuggestedBackgroundNames.Contains(process.ProcessName);
                var protectedProcess = IsProtected(process.ProcessName) || (hasVisibleWindow && !recommended);
                var item = new ProcessItem
                {
                    ProcessId = process.Id,
                    Name = process.ProcessName,
                    DisplayName = string.IsNullOrWhiteSpace(process.MainWindowTitle) ? process.ProcessName : process.MainWindowTitle,
                    MemoryMb = process.WorkingSet64 / 1024 / 1024,
                    IsProtected = protectedProcess,
                    ProtectionReason = IsProtected(process.ProcessName) ? "Sistem / oyun koruması" : hasVisibleWindow && !recommended ? "Açık pencere korunuyor" : "",
                    IsRecommended = recommended && !protectedProcess,
                    RecommendationReason = recommended && !protectedProcess ? GetRecommendationReason(process.ProcessName) : "",
                    IsSelected = recommended && !protectedProcess
                };
                list.Add(item);
            }
            catch { }
            finally { process.Dispose(); }
        }
        return list.OrderByDescending(item => item.MemoryMb).ToList();
    }

    public async Task<(long FreedMb, int Closed, int Trimmed, List<(string Name, string Path)> ClosedApps)> OptimizeAsync(IEnumerable<ProcessItem> selected)
    {
        var before = SystemMonitorService.ReadMemory().AvailableMb;
        var closed = 0;
        var trimmed = 0;
        var closedApps = new List<(string Name, string Path)>();

        foreach (var item in selected.Where(item => item.IsSelected && !item.IsProtected))
        {
            try
            {
                using var process = Process.GetProcessById(item.ProcessId);
                if (IsProtected(process.ProcessName)) continue;
                var path = "";
                try { path = process.MainModule?.FileName ?? ""; } catch { }
                if (!string.IsNullOrWhiteSpace(path)) closedApps.Add((process.ProcessName, path));
                if (process.CloseMainWindow())
                {
                    if (!process.WaitForExit(1500)) process.Kill(true);
                }
                else process.Kill(true);
                closed++;
            }
            catch { }
        }

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == Environment.ProcessId || IsProtected(process.ProcessName) || !TrimAllowList.Contains(process.ProcessName)) continue;
                if (EmptyWorkingSet(process.Handle)) trimmed++;
            }
            catch { }
            finally { process.Dispose(); }
        }

        GC.Collect(2, GCCollectionMode.Optimized, false, false);
        await Task.Delay(1200);
        var after = SystemMonitorService.ReadMemory().AvailableMb;
        var freed = Math.Max(0, after - before);
        await _logger.LogAsync(new ActionLogEntry
        {
            Action = "Hızlandır",
            Detail = $"{closed} uygulama kapatıldı, {trimmed} süreç hafifletildi",
            Success = true,
            MemoryFreed = freed
        });
        return (freed, closed, trimmed, closedApps);
    }

    public static bool IsProtected(string processName) =>
        ProtectedNames.Contains(processName) || processName.StartsWith("faceit", StringComparison.OrdinalIgnoreCase);

    private static string GetRecommendationReason(string processName)
    {
        if (processName.Contains("widget", StringComparison.OrdinalIgnoreCase) || processName.Contains("phone", StringComparison.OrdinalIgnoreCase) || processName.Contains("mobile", StringComparison.OrdinalIgnoreCase))
            return "İsteğe bağlı Windows arka plan bileşeni";
        if (processName.Contains("gamebar", StringComparison.OrdinalIgnoreCase) || processName.Contains("xbox", StringComparison.OrdinalIgnoreCase))
            return "Oyun çubuğu / Xbox arka plan bileşeni";
        if (processName.Contains("adobe", StringComparison.OrdinalIgnoreCase) || processName is "acrotray" or "ccxprocess")
            return "Güncelleyici / yardımcı arka plan süreci";
        if (processName.Contains("webhelper", StringComparison.OrdinalIgnoreCase) || processName.Contains("overwolf", StringComparison.OrdinalIgnoreCase))
            return "Başlatıcı yardımcı süreci";
        return "Oyun sırasında gerekmeyen kullanıcı uygulaması";
    }

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr processHandle);
}
