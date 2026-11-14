using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace W1sleyEdition;

public partial class MainWindow : Window
{
    private readonly ActionLogger _logger = new();
    private readonly PowerModeService _powerModes;
    private readonly SystemMonitorService _monitor = new();
    private readonly ProcessOptimizationService _optimizer;
    private readonly StartupManagerService _startup;
    private readonly BenchmarkService _benchmark;
    private readonly DefenderService _defender;
    private readonly SystemHealthService _health = new();
    private readonly AppCleanerService _appCleaner;
    private readonly GameProfileService _gameProfiles;
    private readonly GameDiscoveryService _gameDiscovery = new();
    private readonly CrashDetectorService _crashDetector;
    private readonly UndoService _undo;

    private readonly NetworkOptimizationService _netOpt;
    private readonly MemoryOptimizationService _memOpt;
    private readonly WindowsOptimizationService _winOpt;
    private HotKeyManagerService? _hotkeys;

    private readonly DispatcherTimer _monitorTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _profileTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly ObservableCollection<ProcessItem> _processes = [];
    private readonly ObservableCollection<StartupItem> _startupItems = [];
    private readonly ObservableCollection<GameProfile> _profiles = [];
    private readonly ObservableCollection<DiscoveredGame> _discoveredGames = [];
    private readonly ObservableCollection<HealthItem> _healthItems = [];
    private readonly ObservableCollection<AppxItem> _apps = [];
    private readonly ObservableCollection<ActionLogEntry> _logs = [];
    private readonly ObservableCollection<CrashEventItem> _crashEvents = [];
    private readonly ObservableCollection<WindowsOptimizeTweakItem> _winTweaks = [];
    private readonly ICollectionView _appsView;
    private readonly Forms.NotifyIcon _trayIcon = new();
    private readonly Forms.ContextMenuStrip _trayMenu = new();
    private readonly Forms.ToolStripMenuItem _trayStatusItem = new("Hazır") { Enabled = false };
    private readonly Forms.ToolStripMenuItem _trayProfilesMenu = new("Oyun Profilleri");
    private CancellationTokenSource? _benchmarkCancellation;
    private CancellationTokenSource? _securityScanCancellation;
    private bool _monitorBusy;
    private bool _profilePollBusy;
    private bool _gameScanBusy;
    private bool _appScanBusy;
    private bool _crashScanBusy;
    private bool _isClosing;
    private DateTimeOffset _lastTemperatureWarning = DateTimeOffset.MinValue;

    public MainWindow()
    {
        InitializeComponent();
        _powerModes = new PowerModeService(_logger);
        _optimizer = new ProcessOptimizationService(_logger);
        _startup = new StartupManagerService(_logger);
        _benchmark = new BenchmarkService(_logger);
        _defender = new DefenderService(_logger);
        _appCleaner = new AppCleanerService(_logger);
        _netOpt = new NetworkOptimizationService(_logger);
        _memOpt = new MemoryOptimizationService(_logger);
        _winOpt = new WindowsOptimizationService(_logger);
        _gameProfiles = new GameProfileService(_powerModes, _optimizer, _netOpt, _logger);
        _crashDetector = new CrashDetectorService(_logger);
        _undo = new UndoService(_powerModes, _startup, _netOpt, _logger);

        ProcessesGrid.ItemsSource = _processes;
        StartupGrid.ItemsSource = _startupItems;
        ProfilesGrid.ItemsSource = _profiles;
        DiscoveredGamesList.ItemsSource = _discoveredGames;
        HealthGrid.ItemsSource = _healthItems;
        _appsView = CollectionViewSource.GetDefaultView(_apps);
        _appsView.Filter = FilterApp;
        AppsGrid.ItemsSource = _appsView;
        LogsGrid.ItemsSource = _logs;
        CrashEventsGrid.ItemsSource = _crashEvents;
        WindowsOptimizeGrid.ItemsSource = _winTweaks;
        foreach (var tweak in _winOpt.CreateDefaultTweaks()) _winTweaks.Add(tweak);
        ProfileModeCombo.ItemsSource = new[]
        {
            new ModeOption("Agresif Performans — Aggressive", PowerMode.AggressivePerformance),
            new ModeOption("Tam Performans — Enabled", PowerMode.FullPerformance),
            new ModeOption("Kararlı Performans — Efficient Enabled", PowerMode.StablePerformance)
        };
        ProfileModeCombo.SelectedValue = PowerMode.StablePerformance;
        ProfileDnsCombo.ItemsSource = new[]
        {
            new DnsOption("DNS'i değiştirme", GameDnsMode.Unchanged),
            new DnsOption("Cloudflare — 1.1.1.1", GameDnsMode.Cloudflare),
            new DnsOption("Google — 8.8.8.8", GameDnsMode.Google),
            new DnsOption("Otomatik — DHCP", GameDnsMode.Automatic)
        };
        ProfileDnsCombo.SelectedValue = GameDnsMode.Unchanged;

        _logger.EntryWritten += (_, entry) => Dispatcher.BeginInvoke(() =>
        {
            _logs.Insert(0, entry);
            UpdateLogSummary();
        });
        _monitorTimer.Tick += MonitorTimer_Tick;
        _profileTimer.Tick += ProfileTimer_Tick;
        _gameProfiles.SessionChanged += GameProfiles_SessionChanged;

        try { _trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!); }
        catch { _trayIcon.Icon = System.Drawing.SystemIcons.Application; }
        var appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
        _trayIcon.Text = $"W1SLEY Edition v{appVersion}";
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreFromTray);
        BuildTrayContextMenu();
        _trayIcon.ContextMenuStrip = _trayMenu;

        Loaded += Window_Loaded;
        SourceInitialized += Window_SourceInitialized;
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            _hotkeys = new HotKeyManagerService(this, _logger);
            _hotkeys.ToggleWindowRequested += () => Dispatcher.Invoke(ToggleWindowState);
            _hotkeys.CleanRamRequested += async () => await CleanRamAsync();
            _hotkeys.GameBoostRequested += async () => await OneClickGameBoostAsync();
            _hotkeys.RegisterAll();
        }
        catch { }
    }

    private static readonly (string Title, string Subtitle)[] NavigationPages =
    [
        ("Ana Panel", "Sistem performansını anlık izle ve güvenle yönet."),
        ("RAM ve İşlemler", "Arka plan yükünü incele, güvenli süreçleri seçerek hafiflet."),
        ("Mini Benchmark", "Seçili güç modunun kısa ve güvenli performans ölçümünü yap."),
        ("Başlangıç", "Windows ile açılan uygulamaları tek noktadan yönet."),
        ("Oyun Profilleri", "Oyunlara özel performans ve arka plan davranışı oluştur."),
        ("Güvenlik ve Sağlık", "Tehdit, aygıt, disk ve kritik Windows olay kontrollerini çalıştır."),
        ("Uygulama Temizleyici", "İsteğe bağlı Windows paketlerini kontrollü biçimde kaldır."),
        ("Log ve Geri Al", "Yapılan değişiklikleri incele ve desteklenen işlemleri geri al."),
        ("Ağ Ayarları Paneli", "Oyun ping, TCP latency ve DNS optimizasyon ayarları."),
        ("Kısayol Ayarları", "Küresel kısayol tuşlarını (Hotkeys) kişiselleştir."),
        ("Çökme Dedektörü", "Beklenmeyen kapanma, mavi ekran, WHEA ve ekran sürücüsü olaylarını incele."),
        ("Windows Optimize", "Arka planda boşta RAM ve kaynak tüketen gereksiz Windows servislerini ve ayarlarını kapat.")
    ];

    private void NavigationButton_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.RadioButton item ||
            !int.TryParse(item.Tag?.ToString(), out var index) ||
            index < 0 || index >= NavigationPages.Length ||
            MainTabs is null || PageTitleText is null || PageSubtitleText is null) return;

        MainTabs.SelectedIndex = index;
        PageTitleText.Text = NavigationPages[index].Title;
        PageSubtitleText.Text = NavigationPages[index].Subtitle;
        if (index == 6 && IsLoaded && _apps.Count == 0) _ = ScanAppsAsync(false);
        if (index == 10 && IsLoaded && _crashEvents.Count == 0) _ = RefreshCrashEventsAsync(false);
        if (index == 11 && IsLoaded) _ = RefreshWinTweaksAsync(false);
    }

    private void ToggleWindowState()
    {
        if (IsVisible && WindowState != WindowState.Minimized)
        {
            Hide();
            _trayIcon.Visible = true;
        }
        else
        {
            RestoreFromTray();
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton != System.Windows.Input.MouseButton.Left) return;
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        try { DragMove(); }
        catch (InvalidOperationException) { }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (MaximizeWindowButton is null) return;
        MaximizeWindowButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        MaximizeWindowButton.ToolTip = WindowState == WindowState.Maximized ? "Geri yükle" : "Büyüt";
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        DashboardNav.IsChecked = true;
        AppVersionText.Text = $"  v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0"}";
        SetStatus("Sistem bilgileri yükleniyor…");
        var loadedProfiles = _gameProfiles.Load();
        var repairedIcons = false;
        foreach (var profile in loadedProfiles)
        {
            if ((string.IsNullOrWhiteSpace(profile.IconPath) || !File.Exists(profile.IconPath)) && File.Exists(profile.ExecutablePath))
            {
                profile.IconPath = _gameDiscovery.GetIconPath(profile.ExecutablePath);
                repairedIcons = true;
            }
            AddProfile(profile);
        }
        if (repairedIcons) _gameProfiles.Save(_profiles);
        var pendingRiskProfiles = _profiles.Count(profile =>
            profile.Mode == PowerMode.AggressivePerformance && !profile.RiskAcknowledged);
        if (pendingRiskProfiles > 0)
            ProfileStatusText.Text = $"{pendingRiskProfiles} Agresif Performans profili, yeniden onaylanana kadar otomatik uygulanmayacak.";
        RefreshTrayProfiles();
        RefreshProcesses();
        RefreshStartup();
        RefreshLogs();
        await RefreshPowerModeAsync();
        await RecoverStaleNetworkBackupsAsync();
        await RefreshActiveDnsAsync();
        await CaptureSystemAsync();
        await RefreshCrashEventsAsync(true);
        _monitorTimer.Start();
        _profileTimer.Start();
        _trayIcon.Visible = true;
        SetStatus("Hazır");
        _ = ScanGamesAsync(false);
        _ = RefreshWinTweaksAsync(false);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_gameProfiles.IsActive)
        {
            if (!_isClosing)
            {
                var answer = MessageBox.Show("Bir oyun profili aktif. Tepsiye küçültürsen izleme ve otomatik geri yükleme devam eder. Tepsiye küçültülsün mü?", "W1SLEY Edition", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                if (answer == MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    MinimizeToTray();
                    return;
                }
                if (answer == MessageBoxResult.Cancel) { e.Cancel = true; return; }
            }
            try { Task.Run(_gameProfiles.StopAsync).GetAwaiter().GetResult(); }
            catch { }
        }
        if (_isClosing)
        {
            _hotkeys?.Dispose();
            _monitorTimer.Stop();
            _profileTimer.Stop();
            _benchmarkCancellation?.Cancel();
            _securityScanCancellation?.Cancel();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        else
        {
            e.Cancel = true;
            Hide();
            _trayIcon.Visible = true;
        }
    }

    private async void MonitorTimer_Tick(object? sender, EventArgs e) => await CaptureSystemAsync();

    private async Task CaptureSystemAsync()
    {
        if (_monitorBusy) return;
        _monitorBusy = true;
        try
        {
            var snapshot = await _monitor.CaptureAsync();
            CpuUsageText.Text = $"{snapshot.CpuPercent:N0}%";
            CpuMhzText.Text = snapshot.CpuMhz > 0 ? $"Canlı {snapshot.CpuMhz / 1000d:N2} GHz • {snapshot.CpuMhz:N0} MHz" : "Canlı GHz okunamadı";
            CpuProgress.Value = snapshot.CpuPercent;
            CpuTempText.Text = snapshot.CpuTemperature is double cpuTemp ? $"{cpuTemp:N0} °C" : "-- °C";
            CpuTempSourceText.Text = snapshot.CpuTemperature is null ? snapshot.CpuTemperatureSource : $"CPU • {snapshot.CpuTemperatureSource}";
            RamUsageText.Text = $"{snapshot.MemoryPercent:N0}%";
            RamDetailText.Text = $"{snapshot.UsedMemoryMb / 1024d:N1} / {snapshot.TotalMemoryMb / 1024d:N1} GB kullanılıyor";
            RamAvailableText.Text = $"{snapshot.AvailableMemoryMb / 1024d:N1} GB kullanılabilir";
            RamProgress.Value = snapshot.MemoryPercent;
            GpuNameText.Text = snapshot.GpuName;
            GpuUsageText.Text = snapshot.GpuPercent is int gpu ? $"{gpu}%" : "--%";
            GpuClockText.Text = snapshot.GpuCoreClockMhz is int coreClock ? $"Çekirdek {coreClock:N0} MHz" : "Çekirdek -- MHz";
            GpuVramText.Text = snapshot.GpuMemoryUsedMb is long used && snapshot.GpuMemoryTotalMb is long total
                ? $"VRAM {used / 1024d:N1} / {total / 1024d:N1} GB"
                : "VRAM -- / -- GB";
            GpuProgress.Value = snapshot.GpuPercent ?? 0;
            GpuTempText.Text = snapshot.GpuTemperature is int temp ? $"{temp} °C" : "-- °C";
            GpuPowerText.Text = snapshot.GpuPowerWatts is double power ? $"GPU {power:N1} W" : "GPU -- W";
            GpuMemoryClockText.Text = snapshot.GpuMemoryClockMhz is int memoryClock ? $"Bellek {memoryClock:N0} MHz" : "Bellek -- MHz";
            CheckTemperatureWarnings(snapshot);

            if (WindowsOptimizationService.AutoRamGuardianEnabled)
            {
                if ((snapshot.MemoryPercent >= 80 || snapshot.AvailableMemoryMb <= 2048) &&
                    (DateTimeOffset.Now - WindowsOptimizationService.LastAutoRamPurge).TotalSeconds >= 60)
                {
                    WindowsOptimizationService.LastAutoRamPurge = DateTimeOffset.Now;
                    _ = Task.Run(() =>
                    {
                        WindowsOptimizationService.PurgeMemoryWorkingSets();
                        _logger.Log("Akıllı RAM Bekçisi", $"RAM kullanımı %{snapshot.MemoryPercent:N0} (Kullanılabilir: {snapshot.AvailableMemoryMb / 1024d:N1} GB) eşiğine ulaştı; sistem çalışma kümeleri ve Standby önbelleği otomatik süpürüldü.", true, null);
                    });
                }
            }
        }
        catch { }
        finally { _monitorBusy = false; }
    }

    private async Task RefreshPowerModeAsync()
    {
        var mode = await _powerModes.GetCurrentModeAsync();
        HeaderModeText.Text = PowerModeService.Label(mode);
        CpuModeTargetText.Text = mode switch
        {
            PowerMode.AggressivePerformance => "Aggressive • en hızlı boost tepkisi",
            PowerMode.FullPerformance => "Enabled • dinamik tam boost",
            PowerMode.StablePerformance => "Efficient Enabled • verimlilik odaklı boost",
            _ => "Özel Windows güç ayarı algılandı"
        };
        StatusDot.Fill = mode == PowerMode.AggressivePerformance
            ? (System.Windows.Media.Brush)FindResource("WarningBrush")
            : (System.Windows.Media.Brush)FindResource("SuccessBrush");
        UpdatePowerModeButtons(mode);
        UpdateTrayStatus(mode);
    }

    private void UpdatePowerModeButtons(PowerMode selectedMode)
    {
        var buttons = new[]
        {
            (Button: AggressiveModeButton, Mode: PowerMode.AggressivePerformance),
            (Button: FullModeButton, Mode: PowerMode.FullPerformance),
            (Button: StableModeButton, Mode: PowerMode.StablePerformance)
        };
        foreach (var item in buttons)
        {
            var selected = item.Mode == selectedMode;
            item.Button.Background = selected
                ? (System.Windows.Media.Brush)FindResource("AccentBrush")
                : new SolidColorBrush(System.Windows.Media.Color.FromRgb(27, 30, 37));
            item.Button.BorderBrush = selected
                ? (System.Windows.Media.Brush)FindResource("AccentBrightBrush")
                : new SolidColorBrush(System.Windows.Media.Color.FromRgb(53, 57, 68));
            item.Button.Opacity = selected ? 1 : 0.82;
        }
    }

    private async void AggressiveModeButton_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "Agresif Performans işlemciyi boost frekansına daha hızlı taşır; güç tüketimini, sıcaklığı ve fan sesini artırabilir. Bazı BIOS/işlemci kombinasyonlarında kararsızlığa yol açabilir. Devam edilsin mi?",
            "Agresif Performans Onayı", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        await ApplyModeAsync(PowerMode.AggressivePerformance);
    }

    private async void FullModeButton_Click(object sender, RoutedEventArgs e) => await ApplyModeAsync(PowerMode.FullPerformance);
    private async void StableModeButton_Click(object sender, RoutedEventArgs e) => await ApplyModeAsync(PowerMode.StablePerformance);

    private async Task ApplyModeAsync(PowerMode mode)
    {
        SetModeButtons(false);
        SetStatus($"{PowerModeService.Label(mode)} uygulanıyor…");
        try
        {
            await _powerModes.SetModeAsync(mode);
            await RefreshPowerModeAsync();
            SetStatus($"Etkin mod: {PowerModeService.Label(mode)}");
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SetModeButtons(true); }
    }

    private void SetModeButtons(bool enabled)
    {
        AggressiveModeButton.IsEnabled = enabled;
        FullModeButton.IsEnabled = enabled;
        StableModeButton.IsEnabled = enabled;
    }

    private void RefreshProcesses()
    {
        _processes.Clear();
        foreach (var item in _optimizer.GetProcesses())
        {
            item.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(ProcessItem.IsSelected)) UpdateProcessSummary();
            };
            _processes.Add(item);
        }
        OptimizeSummaryText.Text = $"{_processes.Count(item => item.IsSelected)} önerilen arka plan işlemi seçili.";
        UpdateProcessSummary();
        if (_processes.Count > 0) Dispatcher.BeginInvoke(() => ProcessesGrid.ScrollIntoView(_processes[0]), DispatcherPriority.Loaded);
    }

    private void UpdateProcessSummary()
    {
        if (ProcessTotalText is null) return;
        var selected = _processes.Where(item => item.IsSelected && !item.IsProtected).ToList();
        var recommended = _processes.Count(item => item.IsRecommended);
        ProcessTotalText.Text = _processes.Count.ToString("N0");
        ProcessRecommendedText.Text = recommended.ToString("N0");
        ProcessSelectedMemoryText.Text = $"{selected.Sum(item => item.MemoryMb):N0} MB";
        ProcessProtectedText.Text = _processes.Count(item => item.IsProtected).ToString("N0");
        ProcessSelectionText.Text = selected.Count == 0
            ? "Şu an kapatılmak üzere seçilmiş süreç yok."
            : $"{selected.Count} süreç seçili • tahmini {selected.Sum(item => item.MemoryMb):N0} MB çalışma belleği kullanıyor";
    }

    private void RefreshProcessesButton_Click(object sender, RoutedEventArgs e) => RefreshProcesses();
    private void SelectAllProcessesButton_Click(object sender, RoutedEventArgs e) { foreach (var item in _processes) item.IsSelected = !item.IsProtected; UpdateProcessSummary(); }
    private void ClearProcessSelectionButton_Click(object sender, RoutedEventArgs e) { foreach (var item in _processes) item.IsSelected = false; UpdateProcessSummary(); }

    private async void OptimizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, QuickOptimizeButton)) RefreshProcesses();
        ProcessesGrid.CommitEdit();
        var selected = _processes.Where(item => item.IsSelected && !item.IsProtected).ToList();
        if (selected.Count > 0)
        {
            var answer = MessageBox.Show($"W1SLEY {selected.Count} arka plan işlemini kapatacak. Devam edilsin mi?", "Akıllı Hızlandırma", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
        }
        SetStatus("Gereksiz arka plan yükü hafifletiliyor…");
        try
        {
            var result = await _optimizer.OptimizeAsync(selected);
            OptimizeSummaryText.Text = $"{result.Closed} uygulama kapandı • {result.FreedMb} MB RAM kazanıldı";
            SetStatus("Hızlandırma tamamlandı");
            RefreshProcesses();
            await CaptureSystemAsync();
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async Task CleanRamAsync(bool force = false)
    {
        if (force)
        {
            var answer = MessageBox.Show(
                "Zorla RAM temizliği bütün erişilebilir süreçlerin çalışma kümelerini ve Windows standby önbelleğini boşaltır. RAM yüzdesi geçici düşebilir; uygulamalar yeniden belleğe alınırken mikro takılma ve disk sayfalaması oluşabilir. Devam edilsin mi?",
                "Zorla RAM Temizliği", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
        }

        SetStatus(force ? "Çalışma kümeleri ve standby RAM zorla boşaltılıyor…" : "Güvenli RAM bakımı çalışıyor…");
        try
        {
            long freedMb = force
                ? await _memOpt.ForcePurgeWorkingSetsAndStandbyListAsync()
                : await _memOpt.RunSafeMaintenanceAsync();
            RefreshProcesses();
            await CaptureSystemAsync();
            MessageBox.Show(
                force
                    ? $"Zorla RAM temizliği tamamlandı.\n\nÖlçülen çalışma kümesi farkı: ~{freedMb} MB"
                    : $"Güvenli RAM bakımı tamamlandı.\n\nYalnızca W1SLEY belleği düzenlendi; ölçülen fark: ~{freedMb} MB",
                "W1SLEY Edition", MessageBoxButton.OK, MessageBoxImage.Information);
            SetStatus(force ? "Zorla RAM temizliği tamamlandı" : "Güvenli RAM bakımı tamamlandı");
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async Task OneClickGameBoostAsync()
    {
        RefreshProcesses();
        var selected = _processes.Where(item => item.IsSelected && !item.IsProtected).ToList();
        var answer = MessageBox.Show(
            $"Oyun Modu şunları uygulayacak:\n\n• Tam Performans\n• {selected.Count} önerilen arka plan işlemini kapatma\n• Bütün süreç çalışma kümelerini ve standby RAM'i zorla boşaltma\n\nZorla RAM boşaltma sonrasında önbellek yeniden oluşurken kısa süreli mikro takılma veya sayfalama olabilir. Devam edilsin mi?",
            "Oyun Modu Onayı", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        SetStatus("Oyun Modu Başlatılıyor…");
        try
        {
            await _powerModes.SetModeAsync(PowerMode.FullPerformance);
            var optimization = await _optimizer.OptimizeAsync(selected);
            long forcedFreedMb = await _memOpt.ForcePurgeWorkingSetsAndStandbyListAsync();
            RefreshProcesses();
            await CaptureSystemAsync();
            await RefreshPowerModeAsync();
            MessageBox.Show($"🚀 OYUN MODU ETKİNLEŞTİRİLDİ\n\n• Güç Profili: Tam Performans\n• Kapatılan arka plan uygulaması: {optimization.Closed}\n• Zorla RAM farkı: ~{forcedFreedMb} MB", "W1SLEY Edition Game Boost", MessageBoxButton.OK, MessageBoxImage.Information);
            SetStatus("Oyun Modu — Tam Performans etkin");
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async void OneClickGameBoost_Click(object sender, RoutedEventArgs e) => await OneClickGameBoostAsync();
    private async void CleanRamButton_Click(object sender, RoutedEventArgs e) => await CleanRamAsync();
    private async void ForceCleanRamButton_Click(object sender, RoutedEventArgs e) => await CleanRamAsync(force: true);

    private async void ApplyNetworkLatency_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "TCP ACK ve NoDelay ayarları yalnızca etkin Ethernet/Wi-Fi bağdaştırıcısına yazılacak. Önceki değerler Log ve Geri Al bölümüne kaydedilecek. Devam edilsin mi?",
            "Ağ Gecikme Ayarı", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        bool ok = await _netOpt.ApplyLatencyOptimizationAsync();
        MessageBox.Show(ok ? "TCP Latency ve Ack Frequency ayarları uygulandı, doğrulandı ve geri alma kaydı oluşturuldu." : "Ağ ayarları uygulanamadı veya doğrulanamadı.", "Ağ Optimizasyonu", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Error);
    }

    private async void CloudflareDns_Click(object sender, RoutedEventArgs e)
    {
        SetStatus("Cloudflare DNS (1.1.1.1) uygulanıyor…");
        try
        {
            var ok = await _netOpt.SetDnsAsync("1.1.1.1", "1.0.0.1");
            if (!ok) { ShowError("Cloudflare DNS uygulanamadı veya sonuç doğrulanamadı."); return; }
            await _netOpt.FlushDnsCacheAsync();
            await RefreshActiveDnsAsync();
            SetStatus("Cloudflare DNS doğrulandı");
        }
        finally
        {
            await RefreshPowerModeAsync();
        }
    }

    private async void GoogleDns_Click(object sender, RoutedEventArgs e)
    {
        SetStatus("Google DNS (8.8.8.8) uygulanıyor…");
        try
        {
            var ok = await _netOpt.SetDnsAsync("8.8.8.8", "8.8.4.4");
            if (!ok) { ShowError("Google DNS uygulanamadı veya sonuç doğrulanamadı."); return; }
            await _netOpt.FlushDnsCacheAsync();
            await RefreshActiveDnsAsync();
            SetStatus("Google DNS doğrulandı");
        }
        finally
        {
            await RefreshPowerModeAsync();
        }
    }

    private async void ResetDhcpDns_Click(object sender, RoutedEventArgs e) => await DisableDnsInternalAsync();
    private async void DisableDns_Click(object sender, RoutedEventArgs e) => await DisableDnsInternalAsync();

    private async Task DisableDnsInternalAsync()
    {
        SetStatus("Özel DNS kapatılıyor…");
        try
        {
            bool ok = await _netOpt.ResetDnsToDhcpAsync();
            await _netOpt.FlushDnsCacheAsync();
            await RefreshActiveDnsAsync();
            MessageBox.Show(ok ? "Özel DNS kapatıldı, varsayılan (DHCP) otomatik DNS'e dönüldü ve önbellek temizlendi." : "DNS sıfırlama işlemi tamamlanamadı.", "DNS Ayarları", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Error);
        }
        finally
        {
            await RefreshPowerModeAsync();
        }
    }

    private async Task RefreshActiveDnsAsync()
    {
        var active = await _netOpt.GetActiveDnsAsync();
        UpdateDnsButtonStyles(active);
    }

    private void UpdateDnsButtonStyles(string mode)
    {
        if (CloudflareDnsBtn is null || GoogleDnsBtn is null || ResetDhcpDnsBtn is null || ActiveDnsStatusText is null) return;

        var accentBrush = (System.Windows.Media.Brush)FindResource("AccentBrush");
        var secBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(32, 35, 43));
        var borderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(50, 54, 65));

        CloudflareDnsBtn.Background = mode == "Cloudflare" ? accentBrush : secBrush;
        CloudflareDnsBtn.BorderBrush = mode == "Cloudflare" ? (System.Windows.Media.Brush)FindResource("AccentBrightBrush") : borderBrush;

        GoogleDnsBtn.Background = mode == "Google" ? accentBrush : secBrush;
        GoogleDnsBtn.BorderBrush = mode == "Google" ? (System.Windows.Media.Brush)FindResource("AccentBrightBrush") : borderBrush;

        ResetDhcpDnsBtn.Background = (mode == "DHCP" || mode == "Otomatik") ? accentBrush : secBrush;
        ResetDhcpDnsBtn.BorderBrush = (mode == "DHCP" || mode == "Otomatik") ? (System.Windows.Media.Brush)FindResource("AccentBrightBrush") : borderBrush;

        ActiveDnsStatusText.Text = mode switch
        {
            "Cloudflare" => "Aktif DNS: Cloudflare (1.1.1.1 / 1.0.0.1)",
            "Google" => "Aktif DNS: Google (8.8.8.8 / 8.8.4.4)",
            "DHCP" or "Otomatik" => "Aktif DNS: Otomatik (DHCP Varsayılan)",
            _ => $"Aktif DNS: Özel ({mode})"
        };
    }
    private async void FlushDns_Click(object sender, RoutedEventArgs e) => await _netOpt.FlushDnsCacheAsync();

    private void SaveHotkeys_Click(object sender, RoutedEventArgs e)
    {
        if (_hotkeys == null) return;
        var mod = (ModifierCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Alt";
        var config = new HotKeyConfig
        {
            Modifier = mod,
            ToggleWindowKey = ToggleWindowKeyText.Text.Trim().ToUpper(),
            CleanRamKey = CleanRamKeyText.Text.Trim().ToUpper(),
            GameBoostKey = GameBoostKeyText.Text.Trim().ToUpper()
        };
        var saved = _hotkeys.SaveConfig(config);
        MessageBox.Show(
            saved ? "Kısayol tuşları güncellendi." : "Kısayollar kaydedilemedi. Tuşların farklı ve başka bir uygulama tarafından kullanılmıyor olduğundan emin ol.",
            "W1SLEY Edition", MessageBoxButton.OK, saved ? MessageBoxImage.Information : MessageBoxImage.Error);
    }

    private async void BenchmarkButton_Click(object sender, RoutedEventArgs e) => await RunBenchmarkAsync(15);
    private async void LongBenchmarkButton_Click(object sender, RoutedEventArgs e) => await RunBenchmarkAsync(60);

    private async Task RunBenchmarkAsync(int seconds)
    {
        var currentMode = await _powerModes.GetCurrentModeAsync();
        if (PowerModeService.RequiresWarning(currentMode))
        {
            var answer = MessageBox.Show(
                $"{PowerModeService.Label(currentMode)} açık. {seconds} saniyelik CPU testi güç tüketimini ve sıcaklığı hızla yükseltebilir. Soğutmanın yeterli olduğundan emin misin?",
                "Agresif Benchmark Onayı", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
        }

        _benchmarkCancellation = new CancellationTokenSource();
        BenchmarkButton.IsEnabled = false;
        LongBenchmarkButton.IsEnabled = false;
        CancelBenchmarkButton.IsEnabled = true;
        BenchmarkModeText.Text = seconds > 20 ? "UZUN ÖLÇÜM ÇALIŞIYOR" : "HIZLI ÖLÇÜM ÇALIŞIYOR";
        BenchmarkScoreText.Text = "0%";
        BenchmarkResultText.Text = $"{seconds} saniyelik CPU ölçümü başladı.";
        var displayProgress = new Progress<double>(value =>
        {
            BenchmarkProgress.Value = value;
            BenchmarkScoreText.Text = $"{value:N0}%";
        });
        try
        {
            var result = await _benchmark.RunAsync(seconds, displayProgress, _benchmarkCancellation.Token);
            BenchmarkModeText.Text = "ÖLÇÜM TAMAMLANDI";
            BenchmarkScoreText.Text = $"{result.Score:N0} K";
            BenchmarkResultText.Text = $"işlem/sn • {result.Workers} iş parçacığı • {result.Seconds} saniye tamamlandı";
        }
        catch (OperationCanceledException)
        {
            BenchmarkModeText.Text = "TEST DURDURULDU";
            BenchmarkScoreText.Text = "—";
            BenchmarkResultText.Text = "Ölçüm durduruldu.";
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally
        {
            BenchmarkButton.IsEnabled = true;
            LongBenchmarkButton.IsEnabled = true;
            CancelBenchmarkButton.IsEnabled = false;
            _benchmarkCancellation?.Dispose();
            _benchmarkCancellation = null;
        }
    }

    private void CancelBenchmarkButton_Click(object sender, RoutedEventArgs e) => _benchmarkCancellation?.Cancel();

    private void RefreshStartup()
    {
        _startupItems.Clear();
        foreach (var item in _startup.GetItems()) _startupItems.Add(item);
        UpdateStartupSummary();
        if (_startupItems.Count > 0) Dispatcher.BeginInvoke(() => StartupGrid.ScrollIntoView(_startupItems[0]), DispatcherPriority.Loaded);
    }

    private void UpdateStartupSummary()
    {
        if (StartupTotalText is null) return;
        StartupTotalText.Text = _startupItems.Count.ToString("N0");
        StartupEnabledText.Text = _startupItems.Count(item => item.IsEnabled).ToString("N0");
        StartupDisabledText.Text = _startupItems.Count(item => !item.IsEnabled).ToString("N0");
        StartupManageableText.Text = _startupItems.Count(item => item.CanToggle).ToString("N0");
    }

    private void RefreshStartupButton_Click(object sender, RoutedEventArgs e) => RefreshStartup();

    private void StartupGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (StartupSelectionText is null) return;
        StartupSelectionText.Text = StartupGrid.SelectedItem is not StartupItem item
            ? "Bir öğe seçerek başlangıç durumunu değiştirebilirsin."
            : item.CanToggle
                ? $"{item.Name} seçildi • kullanıcı başlangıç girdisi güvenle değiştirilebilir"
                : $"{item.Name} seçildi • sistem girdisi salt okunur";
    }

    private async void ToggleStartupButton_Click(object sender, RoutedEventArgs e)
    {
        if (StartupGrid.SelectedItem is not StartupItem item) { ShowInfo("Önce bir başlangıç girdisi seç."); return; }
        if (!item.CanToggle) { ShowInfo("Bu sistem başlangıç girdisi salt okunur."); return; }
        try
        {
            if (item.IsEnabled) await _startup.DisableAsync(item);
            else await _startup.EnableAsync(item);
            RefreshStartup();
            SetStatus($"{item.Name} başlangıç ayarı değiştirildi");
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void AddProfile(GameProfile profile)
    {
        profile.PropertyChanged += (_, _) =>
        {
            _gameProfiles.Save(_profiles);
            RefreshTrayProfiles();
        };
        _profiles.Add(profile);
    }

    private async Task ScanGamesAsync(bool userInitiated)
    {
        if (_gameScanBusy) return;
        _gameScanBusy = true;
        ScanGamesButton.IsEnabled = false;
        GameScanStatusText.Text = "Oyunlar taranıyor…";
        try
        {
            var games = await _gameDiscovery.ScanAsync();
            _discoveredGames.Clear();
            foreach (var game in games) _discoveredGames.Add(game);
            GameScanStatusText.Text = games.Count == 0 ? "Oyun bulunamadı." : $"{games.Count} oyun bulundu.";
        }
        catch { }
        finally
        {
            ScanGamesButton.IsEnabled = true;
            _gameScanBusy = false;
        }
    }

    private async void ScanGamesButton_Click(object sender, RoutedEventArgs e) => await ScanGamesAsync(true);
    private void SelectAllGamesButton_Click(object sender, RoutedEventArgs e) { foreach (var game in _discoveredGames) game.IsSelected = true; }

    private void AddSelectedGamesButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = _discoveredGames.Where(game => game.IsSelected).ToList();
        if (selected.Count == 0) return;
        foreach (var game in selected)
        {
            if (_profiles.Any(p => p.ExecutablePath.Equals(game.ExecutablePath, StringComparison.OrdinalIgnoreCase))) continue;
            AddProfile(new GameProfile { Name = game.Name, ExecutablePath = game.ExecutablePath, IconPath = game.IconPath, Mode = PowerMode.StablePerformance, RiskAcknowledged = true, DnsMode = GameDnsMode.Unchanged, BackgroundProcesses = "Widgets,OneDrive,ms-teams", RestoreClosedApps = true });
        }
        _gameProfiles.Save(_profiles);
        ProfilesGrid.Items.Refresh();
        RefreshTrayProfiles();
    }

    private void NewProfileButton_Click(object sender, RoutedEventArgs e)
    {
        ProfilesGrid.SelectedItem = null;
        ProfileNameBox.Text = "Yeni Oyun";
        ProfilePathBox.Text = "";
        ProfileBackgroundBox.Text = "Widgets,OneDrive,ms-teams";
        ProfileModeCombo.SelectedValue = PowerMode.StablePerformance;
        ProfileDnsCombo.SelectedValue = GameDnsMode.Unchanged;
        ProfileRestoreCheckBox.IsChecked = true;
        SetProfileIcon(null);
    }

    private void BrowseGameButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Oyun uygulaması (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog() == true)
        {
            ProfilePathBox.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(ProfileNameBox.Text) || ProfileNameBox.Text == "Yeni Oyun")
                ProfileNameBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
            SetProfileIcon(_gameDiscovery.GetIconPath(dialog.FileName));
        }
    }

    private void SaveProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(ProfilePathBox.Text)) { ShowInfo("Geçerli bir oyun .exe dosyası seç."); return; }
        var selectedMode = ProfileModeCombo.SelectedValue is PowerMode mode ? mode : PowerMode.StablePerformance;
        if (PowerModeService.RequiresWarning(selectedMode))
        {
            var answer = MessageBox.Show(
                $"Bu profil oyun açıldığında otomatik olarak {PowerModeService.Label(selectedMode)} uygulayacak. Güç tüketimi ve sıcaklık artabilir; donanım uyumluluğu kullanıcı sorumluluğundadır. Profil kaydedilsin mi?",
                "Agresif Oyun Profili Onayı", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
        }
        var profile = ProfilesGrid.SelectedItem as GameProfile ?? new GameProfile();
        profile.Name = string.IsNullOrWhiteSpace(ProfileNameBox.Text) ? Path.GetFileNameWithoutExtension(ProfilePathBox.Text) : ProfileNameBox.Text.Trim();
        profile.ExecutablePath = ProfilePathBox.Text.Trim();
        profile.Mode = selectedMode;
        profile.RiskAcknowledged = true;
        profile.DnsMode = ProfileDnsCombo.SelectedValue is GameDnsMode dnsMode ? dnsMode : GameDnsMode.Unchanged;
        profile.IconPath = _gameDiscovery.GetIconPath(profile.ExecutablePath);
        profile.BackgroundProcesses = ProfileBackgroundBox.Text.Trim();
        profile.RestoreClosedApps = ProfileRestoreCheckBox.IsChecked == true;
        if (!_profiles.Contains(profile)) AddProfile(profile);
        _gameProfiles.Save(_profiles);
        ProfilesGrid.Items.Refresh();
        ProfilesGrid.SelectedItem = profile;
        ProfilesGrid.ScrollIntoView(profile);
        SetProfileIcon(profile.IconPath);
        RefreshTrayProfiles();
    }

    private void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesGrid.SelectedItem is not GameProfile profile) return;
        var answer = MessageBox.Show($"'{profile.Name}' oyun profili silinsin mi?", "Profili Sil", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        _profiles.Remove(profile);
        _gameProfiles.Save(_profiles);
        RefreshTrayProfiles();
        NewProfileButton_Click(sender, e);
    }

    private void ProfilesGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ProfilesGrid.SelectedItem is not GameProfile profile) return;
        ProfileNameBox.Text = profile.Name;
        ProfilePathBox.Text = profile.ExecutablePath;
        ProfileModeCombo.SelectedValue = profile.Mode;
        ProfileDnsCombo.SelectedValue = profile.DnsMode;
        ProfileBackgroundBox.Text = profile.BackgroundProcesses;
        ProfileRestoreCheckBox.IsChecked = profile.RestoreClosedApps;
        SetProfileIcon(profile.IconPath);
    }

    private async void LaunchProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesGrid.SelectedItem is not GameProfile profile) { ShowInfo("Önce başlatılacak oyun profilini seç."); return; }
        await LaunchProfileAsync(profile, minimizeToTray: true);
    }

    private async Task LaunchProfileAsync(GameProfile profile, bool minimizeToTray)
    {
        if (PowerModeService.RequiresWarning(profile.Mode))
        {
            var answer = MessageBox.Show(
                $"'{profile.Name}' {PowerModeService.Label(profile.Mode)} kullanacak. Bu mod daha yüksek sıcaklık ve güç tüketimi oluşturabilir. Oyun şimdi başlatılsın mı?",
                "Agresif Oyun Profili", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
        }

        SetStatus($"{profile.Name} hazırlanıyor…");
        try
        {
            var message = await _gameProfiles.LaunchAsync(profile);
            ProfileStatusText.Text = message;
            await RefreshPowerModeAsync();
            await RefreshActiveDnsAsync();
            if (minimizeToTray) MinimizeToTray();
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async void ProfileTimer_Tick(object? sender, EventArgs e)
    {
        if (_profilePollBusy || (!_gameProfiles.IsActive && ProfileMonitoringCheckBox.IsChecked != true)) return;
        _profilePollBusy = true;
        try
        {
            var message = await _gameProfiles.PollAsync(_profiles);
            if (!string.IsNullOrWhiteSpace(message))
            {
                ProfileStatusText.Text = message;
                SetStatus(message);
                await RefreshPowerModeAsync();
            }
        }
        catch (Exception ex)
        {
            ProfileStatusText.Text = "Profil uygulanamadı: " + ex.Message;
            SetStatus("Oyun profili uygulanamadı");
        }
        finally { _profilePollBusy = false; }
    }

    private void MinimizeToTrayButton_Click(object sender, RoutedEventArgs e) => MinimizeToTray();

    private void MinimizeToTray()
    {
        _trayIcon.Visible = true;
        Hide();
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        _trayIcon.Visible = true;
    }

    private void BuildTrayContextMenu()
    {
        var openItem = new Forms.ToolStripMenuItem("W1SLEY Edition'ı Aç");
        openItem.Click += (_, _) => Dispatcher.BeginInvoke(new Action(RestoreFromTray));

        var powerMenu = new Forms.ToolStripMenuItem("Güç Modu");
        AddTrayModeItem(powerMenu, "Kararlı Performans", PowerMode.StablePerformance);
        AddTrayModeItem(powerMenu, "Tam Performans", PowerMode.FullPerformance);
        AddTrayModeItem(powerMenu, "Agresif Performans", PowerMode.AggressivePerformance);

        var memoryMenu = new Forms.ToolStripMenuItem("RAM Bakımı");
        var safeMemory = new Forms.ToolStripMenuItem("Güvenli Bakım");
        safeMemory.Click += (_, _) => Dispatcher.BeginInvoke(new Action(async () => await CleanRamAsync()));
        var forceMemory = new Forms.ToolStripMenuItem("Zorla Boşalt…");
        forceMemory.Click += (_, _) => Dispatcher.BeginInvoke(new Action(async () => await CleanRamAsync(force: true)));
        memoryMenu.DropDownItems.Add(safeMemory);
        memoryMenu.DropDownItems.Add(forceMemory);

        var exitItem = new Forms.ToolStripMenuItem("Çıkış");
        exitItem.Click += (_, _) => Dispatcher.BeginInvoke(new Action(() => { _isClosing = true; Close(); }));

        _trayMenu.Items.Add(openItem);
        _trayMenu.Items.Add(_trayStatusItem);
        _trayMenu.Items.Add(new Forms.ToolStripSeparator());
        _trayMenu.Items.Add(powerMenu);
        _trayMenu.Items.Add(memoryMenu);
        _trayMenu.Items.Add(_trayProfilesMenu);
        _trayMenu.Items.Add(new Forms.ToolStripSeparator());
        _trayMenu.Items.Add(exitItem);
        _trayMenu.Opening += (_, _) => RefreshTrayProfiles();
    }

    private void AddTrayModeItem(Forms.ToolStripMenuItem parent, string label, PowerMode mode)
    {
        var item = new Forms.ToolStripMenuItem(label);
        item.Click += (_, _) => Dispatcher.BeginInvoke(new Action(async () => await ApplyTrayModeAsync(mode)));
        parent.DropDownItems.Add(item);
    }

    private async Task ApplyTrayModeAsync(PowerMode mode)
    {
        if (PowerModeService.RequiresWarning(mode))
        {
            var answer = MessageBox.Show(
                $"{PowerModeService.Label(mode)} güç tüketimini ve sıcaklığı artırabilir; bazı sistemlerde kararsızlık oluşturabilir. Uygulansın mı?",
                "Agresif Performans Onayı", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
        }
        await ApplyModeAsync(mode);
        ShowTrayNotification("Güç modu değişti", PowerModeService.Label(mode), Forms.ToolTipIcon.Info);
    }

    private void RefreshTrayProfiles()
    {
        if (_trayProfilesMenu is null) return;
        _trayProfilesMenu.DropDownItems.Clear();
        var enabledProfiles = _profiles.Where(profile => profile.Enabled && File.Exists(profile.ExecutablePath)).ToList();
        if (enabledProfiles.Count == 0)
        {
            _trayProfilesMenu.DropDownItems.Add(new Forms.ToolStripMenuItem("Başlatılabilir profil yok") { Enabled = false });
            return;
        }
        foreach (var profile in enabledProfiles)
        {
            var item = new Forms.ToolStripMenuItem($"{profile.Name}  •  {profile.ModeLabel}");
            item.Click += (_, _) => Dispatcher.BeginInvoke(new Action(async () => await LaunchProfileAsync(profile, minimizeToTray: true)));
            _trayProfilesMenu.DropDownItems.Add(item);
        }
    }

    private void UpdateTrayStatus(PowerMode mode)
    {
        _trayStatusItem.Text = _gameProfiles.IsActive
            ? $"Aktif oyun: {_gameProfiles.ActiveProfileName}"
            : $"Mod: {PowerModeService.Label(mode)}";
    }

    private void GameProfiles_SessionChanged(object? sender, GameSessionChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ProfileStatusText.Text = e.Detail;
            SetStatus(e.Started ? $"{e.ProfileName} oyun profili etkin" : $"{e.ProfileName} oturumu tamamlandı");
            _trayStatusItem.Text = e.Started ? $"Aktif oyun: {e.ProfileName}" : $"Geri yüklendi: {e.ModeLabel}";
            ShowTrayNotification(
                e.Started ? $"{e.ProfileName} algılandı" : $"{e.ProfileName} kapandı",
                e.Started ? $"Uygulanan mod: {e.ModeLabel}" : "Önceki güç, DNS ve uygulama durumu geri yüklendi.",
                e.Started ? Forms.ToolTipIcon.Info : Forms.ToolTipIcon.None);
        }));
    }

    private void ShowTrayNotification(string title, string message, Forms.ToolTipIcon icon)
    {
        _trayIcon.Visible = true;
        _trayIcon.BalloonTipTitle = title.Length > 63 ? title[..63] : title;
        _trayIcon.BalloonTipText = message.Length > 255 ? message[..255] : message;
        _trayIcon.BalloonTipIcon = icon;
        _trayIcon.ShowBalloonTip(5000);
    }

    private void CheckTemperatureWarnings(SystemSnapshot snapshot)
    {
        if (DateTimeOffset.Now - _lastTemperatureWarning < TimeSpan.FromMinutes(10)) return;
        if (snapshot.CpuTemperature is double cpu && cpu >= 95)
        {
            _lastTemperatureWarning = DateTimeOffset.Now;
            ShowTrayNotification("Kritik CPU sıcaklığı", $"CPU {cpu:N0} °C ölçüldü. Yükü azaltıp soğutmayı kontrol et.", Forms.ToolTipIcon.Warning);
        }
        else if (snapshot.GpuTemperature is int gpu && gpu >= 88)
        {
            _lastTemperatureWarning = DateTimeOffset.Now;
            ShowTrayNotification("Kritik GPU sıcaklığı", $"GPU {gpu:N0} °C ölçüldü. Yükü azaltıp soğutmayı kontrol et.", Forms.ToolTipIcon.Warning);
        }
    }

    private async void QuickSecurityScanButton_Click(object sender, RoutedEventArgs e) => await RunSecurityScanAsync(false);
    private async void FullSecurityScanButton_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show("Tam tarama uzun sürebilir ve tarama sırasında CPU/disk kullanımı artabilir. Şimdi başlatılsın mı?", "Tam Güvenlik Taraması", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes) await RunSecurityScanAsync(true);
    }
    private async void CancelSecurityScanButton_Click(object sender, RoutedEventArgs e)
    {
        _securityScanCancellation?.Cancel();
        await _defender.CancelScanAsync();
    }

    private async Task RunSecurityScanAsync(bool fullScan)
    {
        _securityScanCancellation?.Dispose();
        _securityScanCancellation = new CancellationTokenSource();
        QuickSecurityScanButton.IsEnabled = false;
        FullSecurityScanButton.IsEnabled = false;
        CancelSecurityScanButton.IsEnabled = true;
        DefenderProgress.Visibility = Visibility.Visible;
        DefenderResultText.Text = "Tarama çalışıyor…";
        try
        {
            var json = await _defender.StartScanAsync(fullScan, _securityScanCancellation.Token);
            DefenderResultText.Text = "Tarama tamamlandı.";
        }
        catch (Exception ex) { DefenderResultText.Text = "Hata: " + ex.Message; }
        finally
        {
            QuickSecurityScanButton.IsEnabled = true;
            FullSecurityScanButton.IsEnabled = true;
            CancelSecurityScanButton.IsEnabled = false;
            DefenderProgress.Visibility = Visibility.Collapsed;
        }
    }

    private async void HealthButton_Click(object sender, RoutedEventArgs e)
    {
        HealthButton.IsEnabled = false;
        try
        {
            _healthItems.Clear();
            foreach (var item in await _health.ScanAsync()) _healthItems.Add(item);
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { HealthButton.IsEnabled = true; }
    }

    private async void RefreshCrashEventsButton_Click(object sender, RoutedEventArgs e) => await RefreshCrashEventsAsync(false);

    private async Task RefreshCrashEventsAsync(bool notify)
    {
        if (_crashScanBusy) return;
        _crashScanBusy = true;
        if (CrashRefreshButton is not null) CrashRefreshButton.IsEnabled = false;
        try
        {
            var events = await _crashDetector.ScanAsync();
            _crashEvents.Clear();
            foreach (var item in events) _crashEvents.Add(item);
            UpdateCrashSummary();

            if (notify)
            {
                var critical = _crashDetector.GetUnnotifiedCritical(events);
                if (critical is not null)
                {
                    ShowTrayNotification("Çökme Dedektörü", $"{critical.Title} • {critical.TimeLabel} • Mod: {critical.PowerMode}", Forms.ToolTipIcon.Warning);
                    _crashDetector.MarkNotified(critical);
                }
            }
        }
        catch (Exception ex)
        {
            if (CrashStatusText is not null) CrashStatusText.Text = "Olaylar okunamadı: " + ex.Message;
        }
        finally
        {
            if (CrashRefreshButton is not null) CrashRefreshButton.IsEnabled = true;
            _crashScanBusy = false;
        }
    }

    private void UpdateCrashSummary()
    {
        if (CrashTotalText is null) return;
        CrashTotalText.Text = _crashEvents.Count.ToString("N0");
        CrashCriticalText.Text = _crashEvents.Count(item => item.Severity == "Kritik").ToString("N0");
        CrashWheaText.Text = _crashEvents.Count(item => item.Provider.Contains("WHEA", StringComparison.OrdinalIgnoreCase)).ToString("N0");
        var latest = _crashEvents.OrderByDescending(item => item.Timestamp).FirstOrDefault();
        CrashLastText.Text = latest?.TimeLabel ?? "Kayıt yok";
        CrashStatusText.Text = latest is null
            ? "Son 30 günde ilgili çökme veya donanım olayı bulunmadı."
            : $"Son olay: {latest.Title} • {latest.PowerMode}";
    }

    private void OpenEventViewerButton_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("eventvwr.msc") { UseShellExecute = true }); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async void ScanAppsButton_Click(object sender, RoutedEventArgs e) => await ScanAppsAsync(true);

    private async Task ScanAppsAsync(bool userInitiated)
    {
        if (_appScanBusy) return;
        _appScanBusy = true;
        ScanAppsButton.IsEnabled = false;
        try
        {
            _apps.Clear();
            foreach (var app in await _appCleaner.ScanAsync())
            {
                app.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(AppxItem.IsSelected)) UpdateAppSummary(); };
                _apps.Add(app);
            }
            _appsView.Refresh();
            UpdateAppSummary();
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { ScanAppsButton.IsEnabled = true; _appScanBusy = false; }
    }

    private bool FilterApp(object item)
    {
        if (item is not AppxItem app) return false;
        var search = AppSearchBox?.Text.Trim() ?? "";
        if (search.Length > 0 && !app.DisplayName.Contains(search, StringComparison.CurrentCultureIgnoreCase) && !app.Name.Contains(search, StringComparison.OrdinalIgnoreCase)) return false;
        if (RemovableOnlyCheckBox?.IsChecked == true && app.IsProtected) return false;
        if (RecommendedOnlyCheckBox?.IsChecked == true && !app.IsRecommended) return false;
        return true;
    }

    private void AppSearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => _appsView?.Refresh();
    private void AppFilter_Changed(object sender, RoutedEventArgs e) => _appsView?.Refresh();

    private void UpdateAppSummary()
    {
        if (AppTotalText is null) return;
        AppTotalText.Text = _apps.Count.ToString("N0");
        AppProtectedText.Text = _apps.Count(app => app.IsProtected).ToString("N0");
        AppRemovableText.Text = _apps.Count(app => !app.IsProtected).ToString("N0");
        AppSelectedText.Text = _apps.Count(app => app.IsSelected && !app.IsProtected).ToString("N0");
    }

    private void SelectRecommendedAppsButton_Click(object sender, RoutedEventArgs e) { foreach (var app in _apps) app.IsSelected = app.IsRecommended && !app.IsProtected; UpdateAppSummary(); }
    private void SelectAllAppsButton_Click(object sender, RoutedEventArgs e) { foreach (var app in _apps) app.IsSelected = !app.IsProtected; UpdateAppSummary(); }
    private void ClearAppSelectionButton_Click(object sender, RoutedEventArgs e) { foreach (var app in _apps) app.IsSelected = false; UpdateAppSummary(); }

    private async void RemoveAppsButton_Click(object sender, RoutedEventArgs e)
    {
        AppsGrid.CommitEdit();
        var selected = _apps.Where(app => app.IsSelected && !app.IsProtected).ToList();
        if (selected.Count == 0) { ShowInfo("Kaldırılabilir en az bir uygulama seç."); return; }
        var answer = MessageBox.Show(
            $"{selected.Count} Windows uygulaması mevcut kullanıcı hesabından kaldırılacak. Bu işlem W1SLEY tarafından otomatik geri alınamaz; gerekirse Microsoft Store üzerinden yeniden kurman gerekir. Devam edilsin mi?",
            "Uygulamaları Kaldır", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        RemoveAppsButton.IsEnabled = false;
        try
        {
            var result = await _appCleaner.RemoveAsync(selected);
            await ScanAppsAsync(false);
            var errorText = result.Errors.Count == 0 ? "" : $"\n\nBaşarısız: {result.Errors.Count}\n{string.Join("\n", result.Errors.Take(5))}";
            MessageBox.Show($"Kaldırılan uygulama: {result.Removed}{errorText}", "Uygulama Temizleyici", MessageBoxButton.OK,
                result.Errors.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { RemoveAppsButton.IsEnabled = true; }
    }

    private void RefreshLogs()
    {
        _logs.Clear();
        foreach (var item in _logger.ReadRecent()) _logs.Add(item);
        UpdateLogSummary();
    }

    private void UpdateLogSummary()
    {
        if (LogTotalText is null) return;
        LogTotalText.Text = _logs.Count.ToString("N0");
        LogSuccessText.Text = _logs.Count(item => item.Success).ToString("N0");
        LogUndoableText.Text = _logs.Count(item => item.CanUndo).ToString("N0");
        LogFreedMemoryText.Text = $"{_logs.Sum(item => item.MemoryFreed ?? 0):N0} MB";
    }

    private void RefreshLogsButton_Click(object sender, RoutedEventArgs e) => RefreshLogs();

    private void LogsGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (LogSelectionText is null) return;
        LogSelectionText.Text = LogsGrid.SelectedItem is not ActionLogEntry entry ? "Bir işlem seç." : $"{entry.Action} seçildi";
    }

    private async void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        if (LogsGrid.SelectedItem is not ActionLogEntry entry) { ShowInfo("Önce bir log kaydı seç."); return; }
        if (!entry.CanUndo) { ShowInfo("Bu işlem otomatik geri alınamıyor."); return; }
        var answer = MessageBox.Show($"'{entry.Action}' işlemi önceki ayarına geri döndürülsün mü?", "Geri Alma Onayı", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        try
        {
            await _undo.UndoAsync(entry);
            RefreshLogs();
            RefreshStartup();
            await RefreshPowerModeAsync();
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void OpenLogsButton_Click(object sender, RoutedEventArgs e)
    {
        AppPaths.EnsureCreated();
        Process.Start(new ProcessStartInfo(AppPaths.LogsDirectory) { UseShellExecute = true });
    }

    private void SetProfileIcon(string? iconPath)
    {
        if (ProfileIconImage is null) return;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = !string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath)
                ? new Uri(Path.GetFullPath(iconPath), UriKind.Absolute)
                : new Uri("pack://application:,,,/Assets/w1sley-logo-256.png", UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            ProfileIconImage.Source = image;
        }
        catch
        {
            ProfileIconImage.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/w1sley-logo-256.png", UriKind.Absolute));
        }
    }

    private async Task RecoverStaleNetworkBackupsAsync()
    {
        if (File.Exists(AppPaths.DpiDnsBackupPath))
        {
            if (await RestoreDnsBackupAsync(AppPaths.DpiDnsBackupPath))
                _logger.Log("DNS kurtarma", "Önceki oturumdan kalan DNS yedeği geri yüklendi.", true, null);
        }
        if (File.Exists(AppPaths.WireSockDnsBackupPath))
        {
            if (await RestoreDnsBackupAsync(AppPaths.WireSockDnsBackupPath))
                _logger.Log("DNS kurtarma", "Önceki oturumdan kalan DNS yedeği geri yüklendi.", true, null);
        }
    }

    private async Task<bool> RestoreDnsBackupAsync(string backupPath)
    {
        if (!File.Exists(backupPath)) return true;
        try
        {
            var snapshot = await File.ReadAllTextAsync(backupPath);
            var restored = await _netOpt.RestoreDnsStateAsync(snapshot);
            if (restored) File.Delete(backupPath);
            return restored;
        }
        catch (Exception ex)
        {
            _logger.Log("DNS geri yükleme", ex.Message, false, null);
            return false;
        }
    }

    private async Task RefreshWinTweaksAsync(bool showMessage)
    {
        try
        {
            SetStatus("Windows ayarları ve servisleri taranıyor…");
            await _winOpt.RefreshStatusesAsync(_winTweaks);
            var mem = SystemMonitorService.ReadMemory();
            var freeGb = mem.AvailableMb / 1024.0;
            var totalGb = mem.TotalMb / 1024.0;
            if (WinOptFreeRamText != null)
                WinOptFreeRamText.Text = $"{freeGb:N1} GB ({Math.Round(freeGb / totalGb * 100)}%)";
            var applied = _winTweaks.Count(t => t.IsApplied);
            if (WinOptAppliedCountText != null)
                WinOptAppliedCountText.Text = $"{applied} / {_winTweaks.Count} Aktif";
            if (WinOptStatusText != null)
                WinOptStatusText.Text = applied == _winTweaks.Count
                    ? "Tüm optimizasyonlar devrede. Sistem maksimum hafiflikte çalışıyor."
                    : $"{_winTweaks.Count - applied} adet optimize edilebilir arka plan yükü bulundu.";
            SetStatus("Windows ayarları güncellendi.");
            if (showMessage) ShowInfo("Windows servisleri ve arka plan ayarları güncellendi.");
        }
        catch (Exception ex)
        {
            if (showMessage) ShowError(ex.Message);
        }
    }

    private void SelectAllWinTweaks_Click(object sender, RoutedEventArgs e)
    {
        foreach (var tweak in _winTweaks) tweak.IsSelected = true;
    }

    private void ClearWinTweaks_Click(object sender, RoutedEventArgs e)
    {
        foreach (var tweak in _winTweaks) tweak.IsSelected = false;
    }

    private async void RefreshWinTweaks_Click(object sender, RoutedEventArgs e)
    {
        await RefreshWinTweaksAsync(true);
    }

    private async void ApplyWinTweaks_Click(object sender, RoutedEventArgs e)
    {
        var selectedCount = _winTweaks.Count(t => t.IsSelected);
        if (selectedCount == 0)
        {
            ShowInfo("Lütfen uygulanacak en az bir ayar seçin.");
            return;
        }

        SetStatus("Windows optimize ediliyor ve boşta RAM temizleniyor…");
        try
        {
            var (applied, freedMb) = await _winOpt.ApplyOptimizationsAsync(_winTweaks);
            await RefreshWinTweaksAsync(false);
            RefreshProcesses();
            await CaptureSystemAsync();
            SetStatus($"Optimizasyon tamamlandı! Boşaltılan RAM: {freedMb} MB");
            ShowInfo($"Windows Optimizasyonu Başarılı!\n\n• Uygulanan ayar: {applied} adet\n• Açılan Boşta RAM: {freedMb:N0} MB\n\nArka plandaki gizli yükler sonlandırıldı ve bellek rahatlatıldı.");
        }
        catch (Exception ex)
        {
            ShowError($"Optimizasyon sırasında hata: {ex.Message}");
        }
    }

    private async void RevertWinTweaks_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "Seçili Windows optimizasyonları orijinal varsayılan Windows ayarlarına geri döndürülecek. Devam edilsin mi?",
            "W1SLEY Edition",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        SetStatus("Windows ayarları varsayılana geri yükleniyor…");
        try
        {
            var count = await _winOpt.RevertAllAsync(_winTweaks);
            await RefreshWinTweaksAsync(false);
            SetStatus($"{count} ayar orijinal haline döndürüldü.");
            ShowInfo($"{count} adet Windows ayarı ve servisi orijinal varsayılan ayarlara başarıyla döndürüldü.");
        }
        catch (Exception ex)
        {
            ShowError($"Geri yükleme hatası: {ex.Message}");
        }
    }

    private void AutoRamGuardianCheck_Changed(object sender, RoutedEventArgs e)
    {
        WindowsOptimizationService.AutoRamGuardianEnabled = AutoRamGuardianCheck?.IsChecked == true;
        _logger.Log("Akıllı RAM Bekçisi", WindowsOptimizationService.AutoRamGuardianEnabled ? "Otomatik RAM koruması açıldı (Eşik: %80 RAM)." : "Otomatik RAM koruması kapatıldı.", true, null);
    }

    private void RestartExplorer_Click(object sender, RoutedEventArgs e)
    {
        WindowsOptimizationService.RestartExplorer();
        SetStatus("Windows Gezgini yeniden başlatıldı.");
    }

    private void SetStatus(string text)
    {
        if (FooterStatusText != null) FooterStatusText.Text = text;
    }
    private static void ShowInfo(string message) => MessageBox.Show(message, "W1SLEY Edition", MessageBoxButton.OK, MessageBoxImage.Information);
    private void ShowError(string message)
    {
        SetStatus("Hata: " + message);
        MessageBox.Show(message, "W1SLEY Edition", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private sealed record ModeOption(string Label, PowerMode Value);
    private sealed record DnsOption(string Label, GameDnsMode Value);
}
