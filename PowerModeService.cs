using Microsoft.Win32;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace W1sleyEdition;

public sealed record PowerStateSnapshot(int BoostMode, int MaximumFrequencyMhz);

public sealed class PowerModeService(ActionLogger logger)
{
    private const string LegacyRunOnceName = "W1SLEYEditionSafeRecovery";
    private const string SafeRecoveryTaskName = "W1SLEY Edition Safe Recovery";
    private readonly ActionLogger _logger = logger;

    public async Task<PowerMode> GetCurrentModeAsync()
    {
        var boostIndex = await ReadAcValueAsync("PERFBOOSTMODE");
        var maximumFrequency = await ReadAcValueAsync("PROCFREQMAX");
        return boostIndex is int boost && maximumFrequency == 0
            ? FromBoostIndex(boost)
            : PowerMode.Custom;
    }

    public async Task SetModeAsync(PowerMode mode, bool writeLog = true)
    {
        if ((int)mode is < 0 or > 6)
            throw new InvalidOperationException("Güç modu geri yüklenebilir bir Windows boost değeri değil.");

        var previousState = await CaptureStateAsync() ??
            throw new InvalidOperationException("Mevcut AC güç ayarları yedeklenemedi; değişiklik uygulanmadı.");
        var previous = previousState.MaximumFrequencyMhz == 0
            ? FromBoostIndex(previousState.BoostMode)
            : PowerMode.Custom;
        var requiresSafeRecovery = mode == PowerMode.AggressivePerformance;
        if (requiresSafeRecovery) await ArmSafeRecoveryAsync();

        try
        {
            await ApplyPowerValuesAsync(mode);
            if (!await VerifyModeAsync(mode))
                throw new InvalidOperationException("Güç komutları tamamlandı ancak etkin değerler doğrulanamadı.");

            // Güvenli bir mod gerçekten doğrulandıktan sonra kurtarma görevi kaldırılır.
            if (!requiresSafeRecovery) await DisarmSafeRecoveryAsync();
        }
        catch
        {
            // Agresif moda geçiş yarıda kalırsa görev silinmez; sonraki oturumda
            // Kararlı Performans'ı uygulamayı yeniden dener.
            throw;
        }

        if (writeLog)
        {
            await _logger.LogAsync(new ActionLogEntry
            {
                Action = "Güç modu",
                Detail = $"{Label(previous)} → {Label(mode)}",
                Success = true,
                UndoType = "power-state",
                UndoData = new Dictionary<string, string>
                {
                    ["boost"] = previousState.BoostMode.ToString(),
                    ["maximumFrequency"] = previousState.MaximumFrequencyMhz.ToString()
                }
            });
        }
    }

    public async Task<PowerStateSnapshot?> CaptureStateAsync()
    {
        var boost = await ReadAcValueAsync("PERFBOOSTMODE");
        var maximumFrequency = await ReadAcValueAsync("PROCFREQMAX");
        return boost is int boostValue && maximumFrequency is int maximumFrequencyValue
            ? new PowerStateSnapshot(boostValue, maximumFrequencyValue)
            : null;
    }

    public async Task RestoreStateAsync(PowerStateSnapshot state)
    {
        if (state.BoostMode is < 0 or > 6 || state.MaximumFrequencyMhz < 0)
            throw new InvalidOperationException("Geri yüklenecek güç durumu geçersiz.");

        var requiresSafeRecovery = state.BoostMode == (int)PowerMode.AggressivePerformance;
        if (requiresSafeRecovery) await ArmSafeRecoveryAsync();
        await ApplyAcValuesAsync(state.BoostMode, state.MaximumFrequencyMhz);
        var verified = await CaptureStateAsync();
        if (verified != state)
            throw new InvalidOperationException("Önceki AC güç ayarları uygulandı ancak doğrulanamadı.");
        if (!requiresSafeRecovery) await DisarmSafeRecoveryAsync();
    }

    public async Task ArmSafeRecoveryAsync()
    {
        AppPaths.EnsureCreated();
        RemoveLegacyRunOnce();

        var executable = Environment.ProcessPath;
        var userSid = WindowsIdentity.GetCurrent().User?.Value;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable) || string.IsNullOrWhiteSpace(userSid))
            throw new InvalidOperationException("Güvenli kurtarma görevi için uygulama veya kullanıcı kimliği bulunamadı.");

        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        var document = new XDocument(
            new XDeclaration("1.0", "utf-16", null),
            new XElement(ns + "Task", new XAttribute("version", "1.4"),
                new XElement(ns + "RegistrationInfo",
                    new XElement(ns + "Description", "Agresif güç modundan sonraki ilk oturumda Kararlı Performans uygular.")),
                new XElement(ns + "Triggers",
                    new XElement(ns + "LogonTrigger",
                        new XElement(ns + "Enabled", "true"),
                        new XElement(ns + "UserId", userSid))),
                new XElement(ns + "Principals",
                    new XElement(ns + "Principal", new XAttribute("id", "Author"),
                        new XElement(ns + "UserId", userSid),
                        new XElement(ns + "LogonType", "InteractiveToken"),
                        new XElement(ns + "RunLevel", "HighestAvailable"))),
                new XElement(ns + "Settings",
                    new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(ns + "DisallowStartIfOnBatteries", "false"),
                    new XElement(ns + "StopIfGoingOnBatteries", "false"),
                    new XElement(ns + "AllowHardTerminate", "true"),
                    new XElement(ns + "StartWhenAvailable", "true"),
                    new XElement(ns + "RunOnlyIfNetworkAvailable", "false"),
                    new XElement(ns + "AllowStartOnDemand", "true"),
                    new XElement(ns + "Enabled", "true"),
                    new XElement(ns + "Hidden", "true"),
                    new XElement(ns + "ExecutionTimeLimit", "PT5M"),
                    new XElement(ns + "Priority", "4")),
                new XElement(ns + "Actions", new XAttribute("Context", "Author"),
                    new XElement(ns + "Exec",
                        new XElement(ns + "Command", executable),
                        new XElement(ns + "Arguments", "--recover-safe"),
                        new XElement(ns + "WorkingDirectory", Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory)))));

        var xmlSettings = new XmlWriterSettings { Encoding = Encoding.Unicode, Indent = true };
        using (var writer = XmlWriter.Create(AppPaths.RecoveryTaskXmlPath, xmlSettings)) document.Save(writer);
        var create = await ProcessRunner.RunAsync("schtasks.exe",
            $"/Create /TN \"{SafeRecoveryTaskName}\" /XML \"{AppPaths.RecoveryTaskXmlPath}\" /F");
        if (create.ExitCode != 0)
            throw new InvalidOperationException("Güvenli kurtarma görevi oluşturulamadı: " + CleanError(create));

        var query = await ProcessRunner.RunAsync("schtasks.exe", $"/Query /TN \"{SafeRecoveryTaskName}\"");
        if (query.ExitCode != 0)
            throw new InvalidOperationException("Güvenli kurtarma görevi oluşturuldu ancak doğrulanamadı: " + CleanError(query));
    }

    public async Task DisarmSafeRecoveryAsync()
    {
        RemoveLegacyRunOnce();
        var result = await ProcessRunner.RunAsync("schtasks.exe", $"/Delete /TN \"{SafeRecoveryTaskName}\" /F");
        if (result.ExitCode != 0 && !TaskDoesNotExist(result))
            throw new InvalidOperationException("Güvenli kurtarma görevi kaldırılamadı: " + CleanError(result));
        try { if (File.Exists(AppPaths.RecoveryTaskXmlPath)) File.Delete(AppPaths.RecoveryTaskXmlPath); } catch { }
    }

    public async Task<bool> RecoverSafeModeAsync()
    {
        try
        {
            await ApplyPowerValuesAsync(PowerMode.StablePerformance);
            if (!await VerifyModeAsync(PowerMode.StablePerformance))
                throw new InvalidOperationException("Kararlı Performans değerleri uygulanamadı veya doğrulanamadı.");

            await _logger.LogAsync(new ActionLogEntry
            {
                Action = "Güvenli güç kurtarması",
                Detail = "Windows oturum açılışında Kararlı Performans doğrulanarak uygulandı.",
                Success = true
            });
            await DisarmSafeRecoveryAsync();
            return true;
        }
        catch (Exception ex)
        {
            await _logger.LogAsync(new ActionLogEntry
            {
                Action = "Güvenli güç kurtarması",
                Detail = "Kararlı Performans otomatik uygulanamadı.",
                Success = false,
                Error = ex.Message
            });
            return false;
        }
    }

    public async Task<(bool Success, string Detail)> TestSafeRecoveryTaskAsync()
    {
        try
        {
            await ArmSafeRecoveryAsync();
            var query = await ProcessRunner.RunAsync("schtasks.exe", $"/Query /TN \"{SafeRecoveryTaskName}\" /XML");
            var valid = query.ExitCode == 0 &&
                        query.Output.Contains("--recover-safe", StringComparison.OrdinalIgnoreCase) &&
                        query.Output.Contains("HighestAvailable", StringComparison.OrdinalIgnoreCase);
            return valid
                ? (true, "Yönetici yetkili oturum açma görevi oluşturuldu ve eylemi doğrulandı.")
                : (false, "Görev sorgulandı ancak eylem veya yetki seviyesi doğrulanamadı: " + CleanError(query));
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
        finally
        {
            try { await DisarmSafeRecoveryAsync(); } catch { }
        }
    }

    private static async Task ApplyPowerValuesAsync(PowerMode mode)
    {
        await ApplyAcValuesAsync((int)mode, 0);
    }

    private static async Task ApplyAcValuesAsync(int boostIndex, int maximumFrequencyMhz)
    {
        var commands = new[]
        {
            $"/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX {maximumFrequencyMhz}",
            $"/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE {boostIndex}",
            "/setactive SCHEME_CURRENT"
        };

        foreach (var command in commands)
        {
            var result = await ProcessRunner.RunAsync("powercfg.exe", command);
            if (result.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error) ? "Güç modu uygulanamadı." : result.Error.Trim());
        }
    }

    private static async Task<bool> VerifyModeAsync(PowerMode mode)
    {
        var boost = await ReadAcValueAsync("PERFBOOSTMODE");
        var maximumFrequency = await ReadAcValueAsync("PROCFREQMAX");
        return boost == (int)mode && maximumFrequency == 0;
    }

    private static void RemoveLegacyRunOnce()
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\RunOnce", true);
        key?.DeleteValue(LegacyRunOnceName, false);
    }

    private static bool TaskDoesNotExist(ProcessResult result)
    {
        var text = result.Output + " " + result.Error;
        return text.Contains("cannot find", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("bulamıyor", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("bulunamadı", StringComparison.OrdinalIgnoreCase);
    }

    private static string CleanError(ProcessResult result) =>
        string.IsNullOrWhiteSpace(result.Error) ? result.Output.Trim() : result.Error.Trim();

    private static async Task<int?> ReadAcValueAsync(string setting)
    {
        var result = await ProcessRunner.RunAsync("powercfg.exe", $"/query SCHEME_CURRENT SUB_PROCESSOR {setting}");
        if (result.ExitCode != 0) return null;
        // powercfg satır adlarını Windows görüntüleme diline göre yerelleştirir.
        // AC ve DC değerleri çıktının son iki onaltılık değeridir; bu yüzden etiket
        // metnine bağlanmadan sondan ikinci değeri okuyarak tüm dilleri destekleriz.
        var values = Regex.Matches(result.Output, @"0x([0-9a-fA-F]{8})");
        return values.Count >= 2
            ? Convert.ToInt32(values[^2].Groups[1].Value, 16)
            : null;
    }

    public static string Label(PowerMode mode) => mode switch
    {
        PowerMode.AggressivePerformance => "Agresif Performans — Aggressive",
        PowerMode.FullPerformance => "Tam Performans — Enabled",
        PowerMode.StablePerformance => "Kararlı Performans — Efficient Enabled",
        PowerMode.Disabled => "Windows güç ayarı — Boost Disabled",
        PowerMode.EfficientAggressive => "Windows güç ayarı — Efficient Aggressive",
        PowerMode.AggressiveAtGuaranteed => "Windows güç ayarı — Aggressive At Guaranteed",
        PowerMode.EfficientAggressiveAtGuaranteed => "Windows güç ayarı — Efficient Aggressive At Guaranteed",
        _ => "Özel Windows güç ayarı"
    };

    public static string Description(PowerMode mode) => mode switch
    {
        PowerMode.AggressivePerformance => "İşlemci boost'a en hızlı biçimde çıkar. En yüksek güç tüketimi ve sıcaklık; bazı BIOS/işlemci kombinasyonlarında kararsızlık oluşturabilir.",
        PowerMode.FullPerformance => "Boost etkin kalır ve Windows işlemci frekansını yüke göre yönetir. Oyun için güçlü ve dengeli seçenektir.",
        PowerMode.StablePerformance => "Boost verimlilik odaklı yönetilir. Daha düşük boşta tüketim, sıcaklık ve fan sesi için önerilen varsayılandır.",
        _ => "Windows tarafından veya başka bir araçla ayarlanmış özel işlemci boost davranışı."
    };

    public static bool RequiresWarning(PowerMode mode) => mode == PowerMode.AggressivePerformance;

    public static PowerMode FromBoostIndex(int boostIndex) =>
        boostIndex is >= 0 and <= 6 ? (PowerMode)boostIndex : PowerMode.Custom;
}
