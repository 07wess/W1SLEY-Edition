using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace W1sleyEdition;

public sealed class WindowsOptimizationService
{
    private readonly ActionLogger _logger;

    private enum WinServiceMode { Automatic = 2, Manual = 3, Disabled = 4 }

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
                Id = "bing_search",
                Category = "Sistem",
                Title = "Başlat Menüsü Bing Web Aramalarını Kapat",
                Description = "Başlat menüsünün anında açılmasını sağlar, arama yaparken internete bağlanıp RAM harcamasını keser.",
                EstimatedMemoryImpact = "~80 - 150 MB",
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
                    item.StatusText = item.IsApplied ? "✓ Optimize Edildi" : "Varsayılan (Çalışıyor)";
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

            case "sysmain":
                return IsServiceDisabledOrStopped("SysMain");

            case "copilot_fabric":
                return IsServiceDisabledOrStopped("WSAIFabricSvc");

            case "telemetry":
                return IsServiceDisabledOrStopped("DiagTrack") && IsServiceDisabledOrStopped("whesvc");

            case "spooler":
                return IsServiceDisabledOrStopped("Spooler");

            case "bing_search":
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Policies\Microsoft\Windows\Explorer"))
                {
                    if (key?.GetValue("DisableSearchBoxSuggestions") is int val && val == 1) return true;
                }
                return false;

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

    public async Task<(int appliedCount, long freedMb)> ApplyOptimizationsAsync(IEnumerable<WindowsOptimizeTweakItem> items)
    {
        var beforeMem = SystemMonitorService.ReadMemory().AvailableMb;
        var count = 0;

        await Task.Run(() =>
        {
            foreach (var item in items.Where(i => i.IsSelected))
            {
                try
                {
                    ApplyTweak(item.Id);
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
        });

        var afterMem = SystemMonitorService.ReadMemory().AvailableMb;
        var freedMb = Math.Max(0, afterMem - beforeMem);

        _logger.Log("Windows Optimizasyonu", $"{count} adet arka plan Windows ayarı ve servisi optimize edildi. Açılan boşta RAM: {freedMb} MB", true, freedMb);

        return (count, freedMb);
    }

    public async Task<int> RevertAllAsync(IEnumerable<WindowsOptimizeTweakItem> items)
    {
        var count = 0;
        await Task.Run(() =>
        {
            foreach (var item in items)
            {
                try
                {
                    RevertTweak(item.Id);
                    item.IsApplied = false;
                    item.StatusText = "Varsayılan (Geri Alındı)";
                    count++;
                }
                catch (Exception ex)
                {
                    item.StatusText = $"Hata: {ex.Message}";
                }
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

            case "sysmain":
                SetServiceStartTypeAndStop("SysMain", WinServiceMode.Disabled);
                break;

            case "copilot_fabric":
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot"))
                {
                    key.SetValue("TurnOffWindowsCopilot", 1, RegistryValueKind.DWord);
                }
                SetServiceStartTypeAndStop("WSAIFabricSvc", WinServiceMode.Disabled);
                KillProcessesByName("copilot");
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

            case "sysmain":
                SetServiceStartTypeAndStart("SysMain", WinServiceMode.Automatic);
                break;

            case "copilot_fabric":
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", writable: true))
                {
                    key?.DeleteValue("TurnOffWindowsCopilot", false);
                }
                SetServiceStartTypeAndStart("WSAIFabricSvc", WinServiceMode.Automatic);
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
