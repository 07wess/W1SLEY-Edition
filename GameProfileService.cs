using System.Diagnostics;
using System.Text.Json;

namespace W1sleyEdition;

public sealed class GameProfileService(
    PowerModeService powerModes,
    ProcessOptimizationService optimizer,
    NetworkOptimizationService network,
    ActionLogger logger)
{
    private readonly PowerModeService _powerModes = powerModes;
    private readonly ProcessOptimizationService _optimizer = optimizer;
    private readonly NetworkOptimizationService _network = network;
    private readonly ActionLogger _logger = logger;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private ActiveSession? _active;

    public bool IsActive => _active is not null;
    public string ActiveProfileName => _active?.Profile.Name ?? "";
    public event EventHandler<GameSessionChangedEventArgs>? SessionChanged;

    public IReadOnlyList<GameProfile> Load()
    {
        if (!File.Exists(AppPaths.ProfilesPath)) return [];
        try
        {
            var profiles = JsonSerializer.Deserialize<List<GameProfile>>(File.ReadAllText(AppPaths.ProfilesPath)) ?? [];
            foreach (var profile in profiles)
            {
                if (profile.Mode is not (PowerMode.AggressivePerformance or PowerMode.FullPerformance or PowerMode.StablePerformance))
                    profile.Mode = PowerMode.StablePerformance;
            }
            return profiles;
        }
        catch { return []; }
    }

    public void Save(IEnumerable<GameProfile> profiles)
    {
        AppPaths.EnsureCreated();
        File.WriteAllText(AppPaths.ProfilesPath, JsonSerializer.Serialize(profiles, _jsonOptions));
    }

    public async Task<string> LaunchAsync(GameProfile profile)
    {
        await _sessionGate.WaitAsync();
        try
        {
            ValidateProfile(profile);
            if (_active is not null) throw new InvalidOperationException($"Önce aktif '{_active.Profile.Name}' oturumunun bitmesini bekle.");

            var processName = Path.GetFileNameWithoutExtension(profile.ExecutablePath);
            Process? existing = null;
            try { existing = Process.GetProcessesByName(processName).FirstOrDefault(); } catch { }
            if (existing is not null)
            {
                await BeginSessionAsync(profile, existing, "W1SLEY başlatıcısı mevcut oyuna bağlandı");
                return $"{profile.Name} zaten açıktı; profil mevcut oyuna uygulandı.";
            }

            await BeginSessionAsync(profile, null, "W1SLEY başlatıcısından açıldı");
            return $"{profile.Name} W1SLEY üzerinden başlatıldı.";
        }
        finally { _sessionGate.Release(); }
    }

    public async Task<string?> PollAsync(IEnumerable<GameProfile> profiles)
    {
        if (!await _sessionGate.WaitAsync(0)) return null;
        try
        {
            if (_active is not null)
            {
                if (!IsRunning(_active.ProcessId))
                {
                    var name = _active.Profile.Name;
                    await EndSessionInternalAsync();
                    return $"{name} kapandı; güç, DNS ve uygulama durumu geri yüklendi.";
                }
                return null;
            }

            foreach (var profile in profiles.Where(profile => profile.Enabled && File.Exists(profile.ExecutablePath)))
            {
                if (profile.Mode == PowerMode.AggressivePerformance && !profile.RiskAcknowledged)
                    continue;
                var processName = Path.GetFileNameWithoutExtension(profile.ExecutablePath);
                Process? process = null;
                try { process = Process.GetProcessesByName(processName).FirstOrDefault(); } catch { }
                if (process is null) continue;
                await BeginSessionAsync(profile, process, "Otomatik algılandı");
                return $"{profile.Name} algılandı; oyun profili uygulandı.";
            }
            return null;
        }
        finally { _sessionGate.Release(); }
    }

    public async Task StopAsync()
    {
        await _sessionGate.WaitAsync();
        try { await EndSessionInternalAsync(); }
        finally { _sessionGate.Release(); }
    }

    private async Task BeginSessionAsync(GameProfile profile, Process? existingProcess, string source)
    {
        ValidateProfile(profile);
        var previousState = await _powerModes.CaptureStateAsync() ??
            throw new InvalidOperationException("Oyun öncesi AC güç durumu yedeklenemedi; profil uygulanmadı.");
        string? dnsSnapshot = null;
        List<(string Name, string Path)> closedApps = [];
        var powerApplied = false;

        try
        {
            if (profile.DnsMode != GameDnsMode.Unchanged)
                dnsSnapshot = await _network.CaptureDnsStateAsync();

            await _powerModes.SetModeAsync(profile.Mode);
            powerApplied = true;
            await ApplyProfileDnsAsync(profile.DnsMode);

            var names = profile.BackgroundProcesses.Split(',', ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var candidates = _optimizer.GetProcesses()
                .Where(item => names.Any(name => name.Equals(item.Name, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            foreach (var item in candidates) item.IsSelected = true;
            var optimization = await _optimizer.OptimizeAsync(candidates);
            closedApps = optimization.ClosedApps;

            var gameProcess = existingProcess ?? Process.Start(new ProcessStartInfo
            {
                FileName = profile.ExecutablePath,
                WorkingDirectory = Path.GetDirectoryName(profile.ExecutablePath) ?? AppContext.BaseDirectory,
                UseShellExecute = true
            });
            if (gameProcess is null) throw new InvalidOperationException("Oyun işlemi başlatılamadı.");
            await Task.Delay(350);
            if (gameProcess.HasExited) throw new InvalidOperationException("Oyun işlemi başlatıldıktan hemen sonra kapandı.");
            try { gameProcess.PriorityClass = ProcessPriorityClass.High; } catch { }
            var gameProcessId = gameProcess.Id;
            gameProcess.Dispose();

            _active = new ActiveSession(profile, gameProcessId, previousState, dnsSnapshot, closedApps, DateTimeOffset.Now);
            var detail = $"{source}; {PowerModeService.Label(profile.Mode)}; DNS: {profile.DnsLabel}; {optimization.Closed} uygulama kapatıldı";
            await _logger.LogAsync(new ActionLogEntry { Action = "Oyun oturumu başladı", Detail = $"{profile.Name}; {detail}", Success = true });
            SessionChanged?.Invoke(this, new GameSessionChangedEventArgs(true, profile.Name, PowerModeService.Label(profile.Mode), detail));
        }
        catch (Exception ex)
        {
            if (dnsSnapshot is not null) try { await _network.RestoreDnsStateAsync(dnsSnapshot); } catch { }
            if (powerApplied) try { await _powerModes.RestoreStateAsync(previousState); } catch { }
            if (profile.RestoreClosedApps) RestoreApplications(closedApps);
            await _logger.LogAsync(new ActionLogEntry { Action = "Oyun başlatıcı", Detail = profile.Name, Success = false, Error = ex.Message });
            throw;
        }
    }

    private async Task EndSessionInternalAsync()
    {
        var session = _active;
        _active = null;
        if (session is null) return;

        var errors = new List<string>();
        if (session.DnsSnapshot is not null)
        {
            try
            {
                if (!await _network.RestoreDnsStateAsync(session.DnsSnapshot)) errors.Add("DNS geri yüklenemedi");
                else await _network.FlushDnsCacheAsync();
            }
            catch (Exception ex) { errors.Add("DNS: " + ex.Message); }
        }
        try { await _powerModes.RestoreStateAsync(session.PreviousPowerState); }
        catch (Exception ex) { errors.Add("Güç modu: " + ex.Message); }

        if (session.Profile.RestoreClosedApps) RestoreApplications(session.ClosedApps);
        var duration = DateTimeOffset.Now - session.StartedAt;
        var detail = $"{session.Profile.Name}; süre {duration.ToString(@"hh\:mm\:ss")}; önceki güç/DNS ve uygulamalar geri yüklendi";
        if (errors.Count > 0) detail += "; uyarı: " + string.Join(" | ", errors);
        await _logger.LogAsync(new ActionLogEntry { Action = "Oyun oturumu bitti", Detail = detail, Success = errors.Count == 0, Error = errors.Count == 0 ? null : string.Join(" | ", errors) });
        var previousMode = session.PreviousPowerState.MaximumFrequencyMhz == 0
            ? PowerModeService.FromBoostIndex(session.PreviousPowerState.BoostMode)
            : PowerMode.Custom;
        SessionChanged?.Invoke(this, new GameSessionChangedEventArgs(false, session.Profile.Name, PowerModeService.Label(previousMode), detail));
    }

    private async Task ApplyProfileDnsAsync(GameDnsMode mode)
    {
        var success = mode switch
        {
            GameDnsMode.Cloudflare => await _network.SetDnsAsync("1.1.1.1", "1.0.0.1", createUndo: false),
            GameDnsMode.Google => await _network.SetDnsAsync("8.8.8.8", "8.8.4.4", createUndo: false),
            GameDnsMode.Automatic => await _network.ResetDnsToDhcpAsync(createUndo: false),
            _ => true
        };
        if (!success) throw new InvalidOperationException("Oyun profili DNS ayarı uygulanamadı veya doğrulanamadı.");
        if (mode != GameDnsMode.Unchanged) await _network.FlushDnsCacheAsync();
    }

    private static void ValidateProfile(GameProfile profile)
    {
        if (!File.Exists(profile.ExecutablePath)) throw new FileNotFoundException("Oyun .exe dosyası bulunamadı.", profile.ExecutablePath);
        if (profile.Mode == PowerMode.AggressivePerformance && !profile.RiskAcknowledged)
            throw new InvalidOperationException("Agresif Performans profili yeniden kaydedilip onaylanmadan çalıştırılamaz.");
    }

    private static void RestoreApplications(IEnumerable<(string Name, string Path)> apps)
    {
        foreach (var app in apps.Where(app => File.Exists(app.Path)))
        {
            try { Process.Start(new ProcessStartInfo(app.Path) { UseShellExecute = true }); } catch { }
        }
    }

    private static bool IsRunning(int processId)
    {
        try { using var process = Process.GetProcessById(processId); return !process.HasExited; }
        catch { return false; }
    }

    private sealed record ActiveSession(
        GameProfile Profile,
        int ProcessId,
        PowerStateSnapshot PreviousPowerState,
        string? DnsSnapshot,
        List<(string Name, string Path)> ClosedApps,
        DateTimeOffset StartedAt);
}

public sealed class GameSessionChangedEventArgs(bool started, string profileName, string modeLabel, string detail) : EventArgs
{
    public bool Started { get; } = started;
    public string ProfileName { get; } = profileName;
    public string ModeLabel { get; } = modeLabel;
    public string Detail { get; } = detail;
}
