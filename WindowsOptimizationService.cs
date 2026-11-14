using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace W1sleyEdition;

public sealed class WindowsOptimizationService
{
    private readonly ActionLogger _logger;

    private enum WinServiceMode { Automatic = 2, Manual = 3, Disabled = 4 }

    public static bool AutoRamGuardianEnabled { get; set; } = true;
    public static DateTimeOffset LastAutoRamPurge { get; set; } = DateTimeOffset.MinValue;

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("ntdll.dll")]
    private static extern int NtSetSystemInformation(int systemInformationClass, ref int systemInformation, int systemInformationLength);

    public WindowsOptimizationService(ActionLogger logger)
    {
        _logger = logger;
    }

    public List<WindowsOptimizeTweakItem> CreateDefaultTweaks()
    {
        return
        [
            // 1. Arka Plan & RAM
            new WindowsOptimizeTweakItem
            {
                Id = "edge_background",
                Category = "Arka Plan",
                Title = "Edge & WebView2 Arka Planını Durdur",
                Description = "Edge kapalı olsa bile arkada çalışan 6-10 adet gizli işlemi (Startup Boost) kapatır.",
                EstimatedMemoryImpact = "~600 - 1200 MB",
                IsSafe = true
            },
            new WindowsOptimizeTweakItem
            {
                Id = "widgets",
                Category = "Arka Plan",
                Title = "Windows Widget ve Haber Servisini Kapat",
                Description = "Görev çubuğunda haber ve içerik yükleyen widgets.exe arka plan sürecini durdurur.",
                EstimatedMemoryImpact = "~300 - 500 MB",
                IsSafe = true
            },
            new WindowsOptimizeTweakItem
            {
                Id = "background_apps",
                Category = "Arka Plan",
                Title = "UWP Mağaza Arka Plan İznini Kapat",
                Description = "Uygulamaların siz açmadığınız halde arka planda RAM'e yerleşmesini tamamen yasaklar.",
                EstimatedMemoryImpact = "~300 - 600 MB",
                IsSafe = true
            },
            new WindowsOptimizeTweakItem
            {
                Id = "remove_onedrive",
                Category = "Arka Plan",
                Title = "OneDrive'ı Kökten Kaldır & Devre Dışı Bırak",
                Description = "OneDrive'ı kapatır, başlangıçtan siler ve arka plan senkronizasyonunu durdurur.",
                EstimatedMemoryImpact = "~150 - 300 MB",
                IsSafe = true
            },
            new WindowsOptimizeTweakItem
            {
                Id = "sysmain",
                Category = "Servisler",
                Title = "SysMain (Superfetch) RAM Şişmesini Önle",
                Description = "Windows'un boşta duran RAM'i yapay önbellekle doldurup diskte ve RAM'de şişmesini engeller.",
                EstimatedMemoryImpact = "~500 - 1500 MB",
                IsSafe = true
            },
            new WindowsOptimizeTweakItem
            {
                Id = "copilot_fabric",
                Category = "Servisler",
                Title = "Windows Copilot & AI Fabric Motorunu Kapat",
                Description = "Windows 11 Copilot ve yapay zeka altyapı servisinin (WSAIFabricSvc) RAM işgalini sonlandırır.",
                EstimatedMemoryImpact = "~200 - 400 MB",
                IsSafe = true
            },

            // 2. Sistem & Görsel Hız
            new WindowsOptimizeTweakItem
            {
                Id = "classic_context_menu",
                Category = "Sistem",
                Title = "Windows 11 Klasik Hızlı Sağ Tık Menüsü",
                Description = "'Daha fazla seçenek göster' gecikmesini kaldırır; anında açılan hızlı sağ tık menüsünü getirir.",
                EstimatedMemoryImpact = "0 ms Tepki",
                IsSafe = true
            },
            new WindowsOptimizeTweakItem
            {
                Id = "start_menu_clean",
                Category = "Sistem",
                Title = "Başlat Menüsü 'En Son' Dosyaları Gizle",
                Description = "Başlat menüsündeki son açılan dosya ve öneri kalabalığını temizler, menüyü hızlandırır.",
                EstimatedMemoryImpact = "Hızlı Başlat",
                IsSafe = true
            },
            new WindowsOptimizeTweakItem
            {
                Id = "bing_search",
                Category = "Sistem",
                Title = "Başlat Menüsü Bing Web Aramalarını Kapat",
                Description = "Başlat menüsünün anında açılmasını sağlar, arama yaparken internete bağlanıp RAM harcamasını keser.",
                EstimatedMemoryImpact = "~80 - 150 MB",
                IsSafe = true
            },
            new WindowsOptimizeTweakItem
            {
                Id = "window_animations",
                Category = "Görsel Hız",
                Title = "Pencere Animasyonlarını Kapat (0ms Hız)",
                Description = "Pencerelerin açılıp kapanırken yaptığı yavaş animasyonları kapatıp salisede açılmasını sağlar.",
                EstimatedMemoryImpact = "0 ms Geçiş",
                IsSafe = true
            },

            // 3. Oyun & Giriş Gecikmesi & Ses
            new WindowsOptimizeTweakItem
            {
                Id = "ultimate_performance",
                Category = "Güç & Oyun",
                Title = "Nihai Performans (Ultimate) Güç Planı",
                Description = "Windows'un gizli 'Nihai Performans' güç planını açar; işlemci ve donanımı tam güce kilitler.",
                EstimatedMemoryImpact = "Maksimum Güç",
                IsSafe = true
            },
            new WindowsOptimizeTweakItem
            {
                Id = "mmcss_gaming",
                Category = "Oyun & Ping",
                Title = "MMCSS Oyun & Ağ VIP Önceliği (Sıfır Ping Sınırı)",
                Description = "Oyunlara CPU'nun %100 gücünü ve GPU önceliğini verir; ağ kartındaki gecikme kısıtlamasını kaldırır.",
                EstimatedMemoryImpact = "Düşük Ping",
                IsSafe = true
            },
            new WindowsOptimizeTweakItem
            {
                Id = "gamedvr_disable",
                Category = "Oyun & Giriş",
                Title = "GameDVR Arka Plan Kaydını Kapat",
                Description = "Windows'un arka planda sürekli video kaydı almasını durdurur, FPS drop ve input lag'i önler.",
                EstimatedMemoryImpact = "Düşük Gecikme",
                IsSafe = true
            },
            new WindowsOptimizeTweakItem
            {
                Id = "mouse_acceleration",
                Category = "Giriş & Fare",
                Title = "Fare İvmesini Kapat (1:1 Ham Girdi / Raw Input)",
                Description = "Windows'un nişan almayı (aim) bozan işaretçi hassasiyetini kapatır, 1:1 piksel takibi sağlar.",
                EstimatedMemoryImpact = "1:1 Aim",
                IsSafe = true
            },
            new WindowsOptimizeTweakItem
            {
                Id = "audiodg_priority",
                Category = "Ses & Gecikme",
                Title = "Windows Ses Motoru (audiodg) Gecikme Ayarı",
                Description = "Ses gecikmesini (audio latency) ve Discord / oyun esnasında ses motorunun CPU darboğazı yapmasını önler.",
                EstimatedMemoryImpact = "Temiz Ses",
                IsSafe = true
            },

            // 4. Hizmetler & Ağ
            new WindowsOptimizeTweakItem
            {
                Id = "telemetry",
                Category = "Servisler",
                Title = "Windows Deneyim ve Telemetri Veri Toplamayı Kapat",
                Description = "Arkada sürekli çalışan whesvc, DiagTrack ve tanı sistemlerini devre dışı bırakır.",
                EstimatedMemoryImpact = "~100 - 250 MB",
                IsSafe = true
            },
            new WindowsOptimizeTweakItem
            {
                Id = "spooler",
                Category = "Servisler",
                Title = "Yazdırma Biriktiricisi (Spooler) Servisini Durdur",
                Description = "Yazıcı kullanmıyorsanız arka planda boşuna çalışan spoolsv sürecini durdurur.",
                EstimatedMemoryImpact = "~40 - 80 MB",
                IsSafe = true
            },
            new WindowsOptimizeTweakItem
            {
                Id = "delivery_opt",
                Category = "Ağ & Disk",
                Title = "Windows Teslim İyileştirme (P2P Dağıtım) Kapat",
                Description = "Windows güncellemelerinin arka planda torrent gibi internetinizi ve diskinizi kullanmasını önler.",
                EstimatedMemoryImpact = "~50 - 100 MB",
                IsSafe = true
            },
            new WindowsOptimizeTweakItem
            {
                Id = "location_sensors",
                Category = "Servisler",
                Title = "Gereksiz Coğrafi Konum ve Sensörleri Kapat",
                Description = "Masaüstünde kullanılmayan lfsvc (konum) ve SensorService arka plan servislerini durdurur.",
                EstimatedMemoryImpact = "~50 - 120 MB",
                IsSafe = true
            }
        ];
    }

    public async Task RefreshStatusesAsync(IEnumerable<WindowsOptimizeTweakItem> items)
    {
        await Task.Run(() =>
        {
            foreach (var item in items)
            {
                try
                {
                    item.IsApplied = CheckIsApplied(item.Id);
                    item.StatusText = item.IsApplied ? "✓ Optimize Edildi" : "Varsayılan";
                }
                catch
                {
                    item.StatusText = "Bilinmiyor";
                }
            }
        });
    }

    private static bool CheckIsApplied(string id)
    {
        switch (id)
        {
            case "edge_background":
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Edge"))
                {
                    if (key is not null)
                    {
                        var startup = key.GetValue("StartupBoostEnabled");
                        var bg = key.GetValue("BackgroundModeEnabled");
                        if (startup is int s && s == 0 && bg is int b && b == 0) return true;
                    }
                }
                return false;

            case "widgets":
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Dsh"))
                {
                    if (key?.GetValue("AllowNewsAndInterests") is int val && val == 0) return true;
                }
                return false;

            case "background_apps":
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications"))
                {
                    if (key?.GetValue("GlobalUserDisabled") is int val && val == 1) return true;
                }
                return false;

            case "remove_onedrive":
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\OneDrive"))
                {
                    if (key?.GetValue("DisableFileSyncNGSC") is int v && v == 1) return true;
                }
                return false;

            case "sysmain":
                return IsServiceDisabledOrStopped("SysMain");

            case "copilot_fabric":
                return IsServiceDisabledOrStopped("WSAIFabricSvc");

            case "classic_context_menu":
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32"))
                {
                    return key is not null;
                }

            case "start_menu_clean":
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"))
                {
                    return key?.GetValue("Start_TrackDocs") is int d && d == 0;
                }

            case "bing_search":
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Policies\Microsoft\Windows\Explorer"))
                {
                    if (key?.GetValue("DisableSearchBoxSuggestions") is int val && val == 1) return true;
                }
                return false;

            case "window_animations":
                using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop\WindowMetrics"))
                {
                    return key?.GetValue("MinAnimate")?.ToString() == "0";
                }

            case "ultimate_performance":
                var activeScheme = GetActivePowerSchemeGuid();
                return activeScheme.Equals("e9a42b02-d5df-448d-aa00-03f14749eb61", StringComparison.OrdinalIgnoreCase)
                    || activeScheme.Equals("bfccb47b-711a-4618-97ca-7b7741acb2b9", StringComparison.OrdinalIgnoreCase)
                    || activeScheme.Equals("4695be1a-2302-4abd-a42f-d96d7cf792d0", StringComparison.OrdinalIgnoreCase);

            case "mmcss_gaming":
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"))
                {
                    return key?.GetValue("SystemResponsiveness") is int sr && sr == 0 &&
                           key?.GetValue("NetworkThrottlingIndex") is int nti && nti == unchecked((int)4294967295);
                }

            case "gamedvr_disable":
                using (var key = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore"))
                {
                    return key?.GetValue("GameDVR_Enabled") is int g && g == 0;
                }

            case "mouse_acceleration":
                using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse"))
                {
                    return key?.GetValue("MouseSpeed")?.ToString() == "0";
                }

            case "audiodg_priority":
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Audio"))
                {
                    return key?.GetValue("Priority") is int p && p == 6;
                }

            case "telemetry":
                return IsServiceDisabledOrStopped("DiagTrack") && IsServiceDisabledOrStopped("whesvc");

            case "spooler":
                return IsServiceDisabledOrStopped("Spooler");

            case "delivery_opt":
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization"))
                {
                    if (key?.GetValue("DODownloadMode") is int val && val == 0) return true;
                }
                return false;

            case "location_sensors":
                return IsServiceDisabledOrStopped("lfsvc");

            default:
                return false;
        }
    }

    private static bool IsServiceDisabledOrStopped(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
            if (key is null) return true;
            var start = key.GetValue("Start");
            if (start is int s && s == (int)WinServiceMode.Disabled) return true;

            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"query \"{serviceName}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            });
            var output = p?.StandardOutput.ReadToEnd() ?? "";
            return output.Contains("STOPPED");
        }
        catch
        {
            return true;
        }
    }

    private static string GetActivePowerSchemeGuid()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "powercfg.exe",
                Arguments = "/getactivescheme",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            });
            var output = p?.StandardOutput.ReadToEnd() ?? "";
            var parts = output.Split(' ');
            if (parts.Length >= 4) return parts[3].Trim();
        }
        catch { }
        return "";
    }

    public async Task<(int appliedCount, long freedMb)> ApplyOptimizationsAsync(IEnumerable<WindowsOptimizeTweakItem> items)
    {
        var beforeMem = SystemMonitorService.ReadMemory().AvailableMb;
        var count = 0;
        var restartExplorerNeeded = false;

        await Task.Run(() =>
        {
            foreach (var item in items.Where(i => i.IsSelected))
            {
                try
                {
                    ApplyTweak(item.Id);
                    if (item.Id is "classic_context_menu" or "start_menu_clean" or "window_animations")
                        restartExplorerNeeded = true;

                    item.IsApplied = true;
                    item.StatusText = "✓ Optimize Edildi";
                    count++;
                }
                catch (Exception ex)
                {
                    item.StatusText = $"Hata: {ex.Message}";
                }
            }

            PurgeMemoryWorkingSets();

            if (restartExplorerNeeded)
            {
                RestartExplorer();
            }
        });

        var afterMem = SystemMonitorService.ReadMemory().AvailableMb;
        var freedMb = Math.Max(0, afterMem - beforeMem);

        _logger.Log("Windows Optimizasyonu", $"{count} adet sistem, oyun ve arka plan ayarı optimize edildi. Açılan boşta RAM: {freedMb} MB", true, freedMb);

        return (count, freedMb);
    }

    public async Task<int> RevertAllAsync(IEnumerable<WindowsOptimizeTweakItem> items)
    {
        var count = 0;
        var restartExplorerNeeded = false;

        await Task.Run(() =>
        {
            foreach (var item in items)
            {
                try
                {
                    RevertTweak(item.Id);
                    if (item.Id is "classic_context_menu" or "start_menu_clean" or "window_animations")
                        restartExplorerNeeded = true;

                    item.IsApplied = false;
                    item.StatusText = "Varsayılan (Geri Alındı)";
                    count++;
                }
                catch (Exception ex)
                {
                    item.StatusText = $"Hata: {ex.Message}";
                }
            }

            if (restartExplorerNeeded)
            {
                RestartExplorer();
            }
        });

        _logger.Log("Windows Ayarlarını Geri Al", $"{count} adet ayar Windows orijinal varsayılanına döndürüldü.", true, null);
        return count;
    }

    private static void ApplyTweak(string id)
    {
        switch (id)
        {
            case "edge_background":
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Edge"))
                {
                    key.SetValue("StartupBoostEnabled", 0, RegistryValueKind.DWord);
                    key.SetValue("BackgroundModeEnabled", 0, RegistryValueKind.DWord);
                }
                KillProcessesByName("msedge", "msedgewebview2");
                break;

            case "widgets":
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Dsh"))
                {
                    key.SetValue("AllowNewsAndInterests", 0, RegistryValueKind.DWord);
                }
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"))
                {
                    key.SetValue("TaskbarDa", 0, RegistryValueKind.DWord);
                }
                KillProcessesByName("widgets", "widgetservice");
                break;

            case "background_apps":
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications"))
                {
                    key.SetValue("GlobalUserDisabled", 1, RegistryValueKind.DWord);
                }
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy"))
                {
                    key.SetValue("LetAppsRunInBackground", 2, RegistryValueKind.DWord);
                }
                break;

            case "remove_onedrive":
                KillProcessesByName("OneDrive");
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\OneDrive"))
                {
                    key.SetValue("DisableFileSyncNGSC", 1, RegistryValueKind.DWord);
                }
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true))
                {
                    key?.DeleteValue("OneDrive", false);
                }
                var setupPath = Environment.ExpandEnvironmentVariables(Environment.Is64BitOperatingSystem
                    ? @"%SystemRoot%\SysWOW64\OneDriveSetup.exe"
                    : @"%SystemRoot%\System32\OneDriveSetup.exe");
                if (File.Exists(setupPath))
                {
                    try { Process.Start(new ProcessStartInfo { FileName = setupPath, Arguments = "/uninstall", CreateNoWindow = true, UseShellExecute = false })?.WaitForExit(3000); } catch { }
                }
                break;

            case "sysmain":
                SetServiceStartTypeAndStop("SysMain", WinServiceMode.Disabled);
                break;

            case "copilot_fabric":
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot"))
                {
                    key.SetValue("TurnOffWindowsCopilot", 1, RegistryValueKind.DWord);
                }
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Policies\Microsoft\Windows\WindowsCopilot"))
                {
                    key.SetValue("TurnOffWindowsCopilot", 1, RegistryValueKind.DWord);
                }
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"))
                {
                    key.SetValue("ShowCopilotButton", 0, RegistryValueKind.DWord);
                }
                SetServiceStartTypeAndStop("WSAIFabricSvc", WinServiceMode.Disabled);
                KillProcessesByName("copilot");
                break;

            case "classic_context_menu":
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32"))
                {
                    key.SetValue("", "", RegistryValueKind.String);
                }
                break;

            case "start_menu_clean":
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"))
                {
                    key.SetValue("Start_TrackDocs", 0, RegistryValueKind.DWord);
                    key.SetValue("Start_TrackProgs", 0, RegistryValueKind.DWord);
                }
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\Explorer"))
                {
                    key.SetValue("HideRecentlyAddedApps", 1, RegistryValueKind.DWord);
                }
                break;

            case "bing_search":
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Policies\Microsoft\Windows\Explorer"))
                {
                    key.SetValue("DisableSearchBoxSuggestions", 1, RegistryValueKind.DWord);
                }
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Search"))
                {
                    key.SetValue("BingSearchEnabled", 0, RegistryValueKind.DWord);
                    key.SetValue("CortanaConsent", 0, RegistryValueKind.DWord);
                }
                break;

            case "window_animations":
                using (var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop\WindowMetrics"))
                {
                    key.SetValue("MinAnimate", "0", RegistryValueKind.String);
                }
                break;

            case "ultimate_performance":
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = "powercfg.exe", Arguments = "-duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61", CreateNoWindow = true, UseShellExecute = false })?.WaitForExit(2000);
                    Process.Start(new ProcessStartInfo { FileName = "powercfg.exe", Arguments = "/setactive e9a42b02-d5df-448d-aa00-03f14749eb61", CreateNoWindow = true, UseShellExecute = false })?.WaitForExit(2000);
                }
                catch { }
                break;

            case "mmcss_gaming":
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"))
                {
                    key.SetValue("SystemResponsiveness", 0, RegistryValueKind.DWord);
                    key.SetValue("NetworkThrottlingIndex", unchecked((int)4294967295), RegistryValueKind.DWord);
                }
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games"))
                {
                    key.SetValue("GPU Priority", 8, RegistryValueKind.DWord);
                    key.SetValue("Priority", 6, RegistryValueKind.DWord);
                    key.SetValue("Scheduling Category", "High", RegistryValueKind.String);
                    key.SetValue("SFIO Priority", "High", RegistryValueKind.String);
                }
                break;

            case "gamedvr_disable":
                using (var key = Registry.CurrentUser.CreateSubKey(@"System\GameConfigStore"))
                {
                    key.SetValue("GameDVR_Enabled", 0, RegistryValueKind.DWord);
                    key.SetValue("GameDVR_FSEBehaviorMode", 2, RegistryValueKind.DWord);
                }
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameDVR"))
                {
                    key.SetValue("AppCaptureEnabled", 0, RegistryValueKind.DWord);
                }
                break;

            case "mouse_acceleration":
                using (var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Mouse"))
                {
                    key.SetValue("MouseSpeed", "0", RegistryValueKind.String);
                    key.SetValue("MouseThreshold1", "0", RegistryValueKind.String);
                    key.SetValue("MouseThreshold2", "0", RegistryValueKind.String);
                }
                break;

            case "audiodg_priority":
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Audio"))
                {
                    key.SetValue("Priority", 6, RegistryValueKind.DWord);
                    key.SetValue("Scheduling Category", "High", RegistryValueKind.String);
                    key.SetValue("SFIO Priority", "High", RegistryValueKind.String);
                    key.SetValue("Clock Rate", 10000, RegistryValueKind.DWord);
                }
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Pro Audio"))
                {
                    key.SetValue("Priority", 6, RegistryValueKind.DWord);
                    key.SetValue("Scheduling Category", "High", RegistryValueKind.String);
                    key.SetValue("SFIO Priority", "High", RegistryValueKind.String);
                }
                break;

            case "telemetry":
                SetServiceStartTypeAndStop("DiagTrack", WinServiceMode.Disabled);
                SetServiceStartTypeAndStop("dmwappushservice", WinServiceMode.Disabled);
                SetServiceStartTypeAndStop("whesvc", WinServiceMode.Disabled);
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection"))
                {
                    key.SetValue("AllowTelemetry", 0, RegistryValueKind.DWord);
                }
                break;

            case "spooler":
                SetServiceStartTypeAndStop("Spooler", WinServiceMode.Manual);
                break;

            case "delivery_opt":
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization"))
                {
                    key.SetValue("DODownloadMode", 0, RegistryValueKind.DWord);
                }
                SetServiceStartTypeAndStop("DoSvc", WinServiceMode.Manual);
                break;

            case "location_sensors":
                SetServiceStartTypeAndStop("lfsvc", WinServiceMode.Manual);
                SetServiceStartTypeAndStop("SensorService", WinServiceMode.Manual);
                SetServiceStartTypeAndStop("SensrSvc", WinServiceMode.Manual);
                break;
        }
    }

    private static void RevertTweak(string id)
    {
        switch (id)
        {
            case "edge_background":
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Edge", writable: true))
                {
                    key?.DeleteValue("StartupBoostEnabled", false);
                    key?.DeleteValue("BackgroundModeEnabled", false);
                }
                break;

            case "widgets":
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Dsh", writable: true))
                {
                    key?.DeleteValue("AllowNewsAndInterests", false);
                }
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", writable: true))
                {
                    key?.SetValue("TaskbarDa", 1, RegistryValueKind.DWord);
                }
                break;

            case "background_apps":
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", writable: true))
                {
                    key?.SetValue("GlobalUserDisabled", 0, RegistryValueKind.DWord);
                }
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", writable: true))
                {
                    key?.DeleteValue("LetAppsRunInBackground", false);
                }
                break;

            case "remove_onedrive":
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\OneDrive", writable: true))
                {
                    key?.DeleteValue("DisableFileSyncNGSC", false);
                }
                break;

            case "sysmain":
                SetServiceStartTypeAndStart("SysMain", WinServiceMode.Automatic);
                break;

            case "copilot_fabric":
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", writable: true))
                {
                    key?.DeleteValue("TurnOffWindowsCopilot", false);
                }
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Policies\Microsoft\Windows\WindowsCopilot", writable: true))
                {
                    key?.DeleteValue("TurnOffWindowsCopilot", false);
                }
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", writable: true))
                {
                    key?.SetValue("ShowCopilotButton", 1, RegistryValueKind.DWord);
                }
                SetServiceStartTypeAndStart("WSAIFabricSvc", WinServiceMode.Automatic);
                break;

            case "classic_context_menu":
                try
                {
                    Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}", false);
                }
                catch { }
                break;

            case "start_menu_clean":
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", writable: true))
                {
                    key?.SetValue("Start_TrackDocs", 1, RegistryValueKind.DWord);
                    key?.SetValue("Start_TrackProgs", 1, RegistryValueKind.DWord);
                }
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\Explorer", writable: true))
                {
                    key?.DeleteValue("HideRecentlyAddedApps", false);
                }
                break;

            case "bing_search":
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Policies\Microsoft\Windows\Explorer", writable: true))
                {
                    key?.DeleteValue("DisableSearchBoxSuggestions", false);
                }
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Search", writable: true))
                {
                    key?.DeleteValue("BingSearchEnabled", false);
                }
                break;

            case "window_animations":
                using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop\WindowMetrics", writable: true))
                {
                    key?.SetValue("MinAnimate", "1", RegistryValueKind.String);
                }
                break;

            case "ultimate_performance":
                try
                {
                    // Restore to Balanced
                    Process.Start(new ProcessStartInfo { FileName = "powercfg.exe", Arguments = "/setactive 381b4222-f694-41f0-9685-ff5bb260df2e", CreateNoWindow = true, UseShellExecute = false })?.WaitForExit(2000);
                }
                catch { }
                break;

            case "mmcss_gaming":
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"))
                {
                    key.SetValue("SystemResponsiveness", 20, RegistryValueKind.DWord);
                    key.SetValue("NetworkThrottlingIndex", 10, RegistryValueKind.DWord);
                }
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games"))
                {
                    key.SetValue("GPU Priority", 8, RegistryValueKind.DWord);
                    key.SetValue("Priority", 2, RegistryValueKind.DWord);
                    key.SetValue("Scheduling Category", "Medium", RegistryValueKind.String);
                    key.SetValue("SFIO Priority", "Normal", RegistryValueKind.String);
                }
                break;

            case "gamedvr_disable":
                using (var key = Registry.CurrentUser.CreateSubKey(@"System\GameConfigStore"))
                {
                    key.SetValue("GameDVR_Enabled", 1, RegistryValueKind.DWord);
                    key.SetValue("GameDVR_FSEBehaviorMode", 0, RegistryValueKind.DWord);
                }
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameDVR"))
                {
                    key.SetValue("AppCaptureEnabled", 1, RegistryValueKind.DWord);
                }
                break;

            case "mouse_acceleration":
                using (var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Mouse"))
                {
                    key.SetValue("MouseSpeed", "1", RegistryValueKind.String);
                    key.SetValue("MouseThreshold1", "6", RegistryValueKind.String);
                    key.SetValue("MouseThreshold2", "10", RegistryValueKind.String);
                }
                break;

            case "audiodg_priority":
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Audio"))
                {
                    key.SetValue("Priority", 2, RegistryValueKind.DWord);
                    key.SetValue("Scheduling Category", "Medium", RegistryValueKind.String);
                    key.SetValue("SFIO Priority", "Normal", RegistryValueKind.String);
                }
                break;

            case "telemetry":
                SetServiceStartTypeAndStart("DiagTrack", WinServiceMode.Automatic);
                SetServiceStartTypeAndStart("whesvc", WinServiceMode.Automatic);
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", writable: true))
                {
                    key?.DeleteValue("AllowTelemetry", false);
                }
                break;

            case "spooler":
                SetServiceStartTypeAndStart("Spooler", WinServiceMode.Automatic);
                break;

            case "delivery_opt":
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", writable: true))
                {
                    key?.DeleteValue("DODownloadMode", false);
                }
                SetServiceStartTypeAndStart("DoSvc", WinServiceMode.Automatic);
                break;

            case "location_sensors":
                SetServiceStartTypeAndStart("lfsvc", WinServiceMode.Manual);
                break;
        }
    }

    private static void SetServiceStartTypeAndStop(string serviceName, WinServiceMode startMode)
    {
        try
        {
            var startTypeArg = startMode switch
            {
                WinServiceMode.Disabled => "disabled",
                WinServiceMode.Manual => "demand",
                WinServiceMode.Automatic => "auto",
                _ => "demand"
            };

            using var scProc = Process.Start(new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"config \"{serviceName}\" start= {startTypeArg}",
                CreateNoWindow = true,
                UseShellExecute = false
            });
            scProc?.WaitForExit(2000);

            using var stopProc = Process.Start(new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"stop \"{serviceName}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });
            stopProc?.WaitForExit(2000);
        }
        catch { }
    }

    private static void SetServiceStartTypeAndStart(string serviceName, WinServiceMode startMode)
    {
        try
        {
            var startTypeArg = startMode switch
            {
                WinServiceMode.Disabled => "disabled",
                WinServiceMode.Manual => "demand",
                WinServiceMode.Automatic => "auto",
                _ => "auto"
            };

            using var scProc = Process.Start(new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"config \"{serviceName}\" start= {startTypeArg}",
                CreateNoWindow = true,
                UseShellExecute = false
            });
            scProc?.WaitForExit(2000);

            if (startMode != WinServiceMode.Disabled)
            {
                using var startProc = Process.Start(new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = $"start \"{serviceName}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                startProc?.WaitForExit(2000);
            }
        }
        catch { }
    }

    private static void KillProcessesByName(params string[] names)
    {
        foreach (var name in names)
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try
                    {
                        p.Kill();
                        p.Dispose();
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    public static void RestartExplorer()
    {
        try
        {
            KillProcessesByName("explorer");
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", UseShellExecute = true });
        }
        catch { }
    }

    public static void PurgeMemoryWorkingSets()
    {
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (!process.HasExited && process.Id > 4)
                    {
                        EmptyWorkingSet(process.Handle);
                    }
                }
                catch { }
                finally
                {
                    process.Dispose();
                }
            }

            var purgeCommand = 4; // MemoryPurgeStandbyList
            NtSetSystemInformation(80, ref purgeCommand, sizeof(int));
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        }
        catch { }
    }
}
