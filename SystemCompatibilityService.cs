using Microsoft.Win32;
using System.Management;
using System.Security.Principal;

namespace W1sleyEdition;

public sealed class SetupScanItem
{
    public string Category { get; init; } = "SİSTEM";
    public string Title { get; init; } = "Kontrol";
    public string Detail { get; init; } = "";
    public string State { get; init; } = "Bilgi";
    public string Accent { get; init; } = "#9296A1";
}

public sealed class SystemCompatibilityService
{
    public async Task<IReadOnlyList<SetupScanItem>> ScanAsync()
    {
        var items = new List<SetupScanItem>();
        AddWindowsCheck(items);
        AddPrivilegeCheck(items);
        AddHardwareChecks(items);
        await AddPowerCheckAsync(items);
        await AddDefenderCheckAsync(items);
        await AddHealthCheckAsync(items);
        return items;
    }

    private static void AddWindowsCheck(List<SetupScanItem> items)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var product = key?.GetValue("ProductName")?.ToString() ?? "Windows";
        var displayVersion = key?.GetValue("DisplayVersion")?.ToString() ?? "Sürüm bilinmiyor";
        var buildText = key?.GetValue("CurrentBuildNumber")?.ToString() ?? "0";
        _ = int.TryParse(buildText, out var build);
        var supported = OperatingSystem.IsWindows() && build >= 22000;
        var displayProduct = build >= 22000 ? "Windows 11" : product;
        items.Add(new SetupScanItem
        {
            Category = "WINDOWS",
            Title = supported ? "Windows 11 hazır" : "Windows sürümü destek dışı",
            Detail = $"{displayProduct} {displayVersion} • Derleme {buildText}. W1SLEY Edition Windows 11 için tasarlanmıştır.",
            State = supported ? "Hazır" : "Uyarı",
            Accent = supported ? "#42C98A" : "#F2AE5D"
        });
    }

    private static void AddPrivilegeCheck(List<SetupScanItem> items)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        items.Add(new SetupScanItem
        {
            Category = "YETKİ",
            Title = elevated ? "Yönetici yetkisi hazır" : "Yönetici yetkisi gerekli",
            Detail = elevated
                ? "Güç, başlangıç, DNS ve bakım işlemleri için gereken yetki kullanılabilir."
                : "Uygulamayı yönetici olarak yeniden başlatın.",
            State = elevated ? "Hazır" : "Uyarı",
            Accent = elevated ? "#42C98A" : "#F2AE5D"
        });
    }

    private static void AddHardwareChecks(List<SetupScanItem> items)
    {
        try
        {
            using var cpuSearch = new ManagementObjectSearcher("SELECT Name, MaxClockSpeed FROM Win32_Processor");
            using var cpu = cpuSearch.Get().Cast<ManagementObject>().FirstOrDefault();
            var name = cpu?["Name"]?.ToString()?.Trim() ?? "İşlemci algılanamadı";
            var maxClock = Convert.ToInt32(cpu?["MaxClockSpeed"] ?? 0);
            items.Add(new SetupScanItem
            {
                Category = "CPU",
                Title = name,
                Detail = maxClock > 0 ? $"Bildirilen taban/azami saat: {maxClock / 1000d:N2} GHz • {Environment.ProcessorCount} mantıksal işlemci" : $"{Environment.ProcessorCount} mantıksal işlemci",
                State = "Algılandı",
                Accent = "#D12A50"
            });

            using var osSearch = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
            using var os = osSearch.Get().Cast<ManagementObject>().FirstOrDefault();
            var totalKb = Convert.ToInt64(os?["TotalVisibleMemorySize"] ?? 0L);
            var totalGb = totalKb / 1024d / 1024d;
            items.Add(new SetupScanItem
            {
                Category = "RAM",
                Title = totalGb > 0 ? $"{totalGb:N1} GB kullanılabilir fiziksel bellek" : "Bellek bilgisi okunamadı",
                Detail = totalGb >= 8 ? "Oyun ve sistem izleme özellikleri için yeterli." : "8 GB ve üzeri bellek önerilir.",
                State = totalGb >= 8 ? "Hazır" : "Uyarı",
                Accent = totalGb >= 8 ? "#42C98A" : "#F2AE5D"
            });

            using var gpuSearch = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            var gpuNames = gpuSearch.Get().Cast<ManagementObject>()
                .Select(item => item["Name"]?.ToString()?.Trim())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            items.Add(new SetupScanItem
            {
                Category = "GPU",
                Title = gpuNames.Length > 0 ? string.Join(" + ", gpuNames) : "Ekran kartı algılanamadı",
                Detail = "Canlı kullanım ve desteklenen sensör verileri ana panelde gösterilir.",
                State = gpuNames.Length > 0 ? "Algılandı" : "Bilgi",
                Accent = gpuNames.Length > 0 ? "#D12A50" : "#9296A1"
            });
        }
        catch (Exception ex)
        {
            items.Add(new SetupScanItem { Category = "DONANIM", Title = "Donanım özeti kısmen okunabildi", Detail = ex.Message, State = "Bilgi", Accent = "#9296A1" });
        }
    }

    private static async Task AddPowerCheckAsync(List<SetupScanItem> items)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        ProcessResult result;
        try { result = await ProcessRunner.RunAsync("powercfg.exe", "/getactivescheme", timeout.Token); }
        catch (OperationCanceledException) { result = new ProcessResult(1, "", "Güç altyapısı sorgusu zaman aşımına uğradı."); }
        items.Add(new SetupScanItem
        {
            Category = "GÜÇ",
            Title = result.ExitCode == 0 ? "Windows güç altyapısı hazır" : "Güç ayarları okunamadı",
            Detail = result.ExitCode == 0 ? "Boost modu değişiklikleri uygulandıktan sonra doğrulanacak ve günlüğe kaydedilecek." : Clean(result),
            State = result.ExitCode == 0 ? "Hazır" : "Uyarı",
            Accent = result.ExitCode == 0 ? "#42C98A" : "#F2AE5D"
        });
    }

    private static async Task AddDefenderCheckAsync(List<SetupScanItem> items)
    {
        const string script = "$s=Get-MpComputerStatus -ErrorAction Stop; \"$($s.AntivirusEnabled)|$($s.RealTimeProtectionEnabled)|$($s.AntivirusSignatureLastUpdated)\"";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        ProcessResult result;
        try { result = await ProcessRunner.RunPowerShellAsync(script, timeout.Token); }
        catch (OperationCanceledException) { result = new ProcessResult(1, "", "Güvenlik durumu sorgusu zaman aşımına uğradı."); }
        var parts = result.Output.Trim().Split('|');
        var antivirus = result.ExitCode == 0 && parts.Length >= 2 && bool.TryParse(parts[0], out var av) && av;
        var realtime = result.ExitCode == 0 && parts.Length >= 2 && bool.TryParse(parts[1], out var rt) && rt;
        items.Add(new SetupScanItem
        {
            Category = "GÜVENLİK",
            Title = antivirus && realtime ? "Gerçek zamanlı koruma açık" : "Koruma durumunu gözden geçir",
            Detail = result.ExitCode == 0
                ? $"Antivirüs: {(antivirus ? "Açık" : "Kapalı")} • Gerçek zamanlı koruma: {(realtime ? "Açık" : "Kapalı")} • Bu kontrolde virüs taraması başlatılmaz."
                : "Windows güvenlik durumu okunamadı. Ana panelden hızlı veya tam tarama başlatılabilir.",
            State = antivirus && realtime ? "Hazır" : "Uyarı",
            Accent = antivirus && realtime ? "#42C98A" : "#F2AE5D"
        });
    }

    private static async Task AddHealthCheckAsync(List<SetupScanItem> items)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var health = await new SystemHealthService().ScanAsync(timeout.Token);
            var healthy = health.Count == 1 && health[0].Category == "Genel" && health[0].Status == "İyi";
            items.Add(new SetupScanItem
            {
                Category = "SAĞLIK",
                Title = healthy ? "Temel sistem sağlığı iyi" : $"{health.Count} sağlık kaydı incelenmeli",
                Detail = healthy ? health[0].Detail : "Aygıt, disk veya son 24 saat olay ayrıntıları uygulamadaki Güvenlik ve Sağlık bölümünde görülebilir.",
                State = healthy ? "Hazır" : "İncele",
                Accent = healthy ? "#42C98A" : "#F2AE5D"
            });
        }
        catch (OperationCanceledException)
        {
            items.Add(new SetupScanItem { Category = "SAĞLIK", Title = "Sağlık sorgusu zaman aşımına uğradı", Detail = "Ayrıntılı kontrol daha sonra Güvenlik ve Sağlık bölümünden yeniden çalıştırılabilir.", State = "Bilgi", Accent = "#9296A1" });
        }
        catch (Exception ex)
        {
            items.Add(new SetupScanItem { Category = "SAĞLIK", Title = "Sağlık taraması tamamlanamadı", Detail = ex.Message, State = "Bilgi", Accent = "#9296A1" });
        }
    }

    private static string Clean(ProcessResult result) =>
        string.IsNullOrWhiteSpace(result.Error) ? result.Output.Trim() : result.Error.Trim();
}
