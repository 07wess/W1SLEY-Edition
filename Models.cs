using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace W1sleyEdition;

public enum PowerMode
{
    Disabled = 0,
    FullPerformance = 1,
    AggressivePerformance = 2,
    StablePerformance = 3,
    EfficientAggressive = 4,
    AggressiveAtGuaranteed = 5,
    EfficientAggressiveAtGuaranteed = 6,
    Custom = 255
}

public enum GameDnsMode
{
    Unchanged = 0,
    Cloudflare = 1,
    Google = 2,
    Automatic = 3
}

public sealed class ProcessItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public int ProcessId { get; init; }
    public string Name { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public long MemoryMb { get; init; }
    public bool IsProtected { get; init; }
    public string ProtectionReason { get; init; } = "";
    public bool IsRecommended { get; init; }
    public string RecommendationReason { get; init; } = "";

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class StartupItem : INotifyPropertyChanged
{
    private bool _isEnabled;

    public string Name { get; init; } = "";
    public string Command { get; init; } = "";
    public string Location { get; init; } = "";
    public bool CanToggle { get; init; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class DisabledStartupEntry
{
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string Location { get; set; } = "HKCU";
}

public sealed class GameProfile : INotifyPropertyChanged
{
    private bool _enabled = true;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Yeni Oyun";
    public string ExecutablePath { get; set; } = "";
    public string IconPath { get; set; } = "";
    public PowerMode Mode { get; set; } = PowerMode.StablePerformance;
    public bool RiskAcknowledged { get; set; }
    public GameDnsMode DnsMode { get; set; } = GameDnsMode.Unchanged;
    public string BackgroundProcesses { get; set; } = "Widgets,OneDrive,ms-teams,PhoneExperienceHost";
    public bool RestoreClosedApps { get; set; } = true;

    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; OnPropertyChanged(); }
    }

    [JsonIgnore]
    public string ModeLabel => Mode switch
    {
        PowerMode.AggressivePerformance => "Agresif Performans",
        PowerMode.FullPerformance => "Tam Performans",
        PowerMode.StablePerformance => "Kararlı Performans",
        _ => "Özel Güç Ayarı"
    };

    [JsonIgnore]
    public string DnsLabel => DnsMode switch
    {
        GameDnsMode.Cloudflare => "Cloudflare",
        GameDnsMode.Google => "Google",
        GameDnsMode.Automatic => "Otomatik",
        _ => "Değiştirme"
    };

    [JsonIgnore]
    public string IconSource => string.IsNullOrWhiteSpace(IconPath)
        ? "pack://application:,,,/Assets/w1sley-logo-256.png"
        : IconPath;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class DiscoveredGame : INotifyPropertyChanged
{
    private bool _isSelected;

    public string Name { get; init; } = "";
    public string ExecutablePath { get; init; } = "";
    public string Source { get; init; } = "Oyun";
    public string IconPath { get; init; } = "";
    public string IconSource => string.IsNullOrWhiteSpace(IconPath)
        ? "pack://application:,,,/Assets/w1sley-logo-256.png"
        : IconPath;

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class HealthItem
{
    public string Category { get; init; } = "";
    public string Status { get; init; } = "";
    public string Detail { get; init; } = "";
}

public sealed class CrashEventItem
{
    public DateTimeOffset Timestamp { get; init; }
    public string Severity { get; init; } = "Bilgi";
    public string Category { get; init; } = "Windows";
    public string Provider { get; init; } = "";
    public int EventId { get; init; }
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public string PowerMode { get; init; } = "Bilinmiyor";

    [JsonIgnore]
    public string TimeLabel => Timestamp.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");

    [JsonIgnore]
    public string EventLabel => $"{Provider} / {EventId}";
}

public sealed class AppxItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public string Name { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string Category { get; init; } = "";
    public string CategoryGlyph { get; init; } = "\uE71D";
    public string Version { get; init; } = "";
    public string PackageFullName { get; init; } = "";
    public string InstallLocation { get; init; } = "";
    public bool IsProtected { get; init; }
    public bool IsRecommended { get; init; }
    public string ProtectionReason { get; init; } = "";
    public string StatusLabel => IsProtected ? "Korumalı" : IsRecommended ? "Önerilen" : "Kaldırılabilir";

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ActionLogEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
    public bool Success { get; set; }
    public string? Error { get; set; }
    [JsonPropertyName("memoryFreed")]
    public long? MemoryFreed { get; set; }

    // v1 günlükleri memoryFreedMb alanını kullanıyordu. Yalnızca okuma uyumluluğu
    // sağlayan bu köprü, yeni günlüklerde iki alanın birden yazılmasını engeller.
    [JsonPropertyName("memoryFreedMb")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? LegacyMemoryFreedMb
    {
        get => null;
        set { if (value.HasValue && !MemoryFreed.HasValue) MemoryFreed = value; }
    }
    public string? UndoType { get; set; }
    public Dictionary<string, string>? UndoData { get; set; }

    [JsonIgnore]
    public string TimeLabel
    {
        get
        {
            var local = Timestamp.ToLocalTime();
            if (local.Date == DateTime.Today)
                return $"Bugün {local:HH:mm:ss}";
            if (local.Date == DateTime.Today.AddDays(-1))
                return $"Dün {local:HH:mm:ss}";
            return local.ToString("dd.MM.yyyy HH:mm:ss");
        }
    }

    [JsonIgnore]
    public string ResultLabel => Success ? "Başarılı" : "Hata";

    [JsonIgnore]
    public bool CanUndo => !string.IsNullOrWhiteSpace(UndoType);

    [JsonIgnore]
    public string MemoryFreedLabel => MemoryFreed is long value ? $"{value:N0} MB" : "—";
}

public sealed class SystemSnapshot
{
    public double CpuPercent { get; init; }
    public int CpuMhz { get; init; }
    public double? CpuTemperature { get; init; }
    public string CpuTemperatureSource { get; init; } = "Sensör okunamadı";
    public double MemoryPercent { get; init; }
    public long UsedMemoryMb { get; init; }
    public long TotalMemoryMb { get; init; }
    public long AvailableMemoryMb { get; init; }
    public string GpuName { get; init; } = "Algılanmadı";
    public int? GpuPercent { get; init; }
    public int? GpuTemperature { get; init; }
    public long? GpuMemoryUsedMb { get; init; }
    public long? GpuMemoryTotalMb { get; init; }
    public int? GpuCoreClockMhz { get; init; }
    public int? GpuMemoryClockMhz { get; init; }
    public double? GpuPowerWatts { get; init; }
}
