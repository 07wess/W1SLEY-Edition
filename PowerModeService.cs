using Microsoft.Win32;
using System.Runtime.InteropServices;
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
        var boostIndex = await ReadAcValueAsync(PowerInterop.PerfBoostModeGuid, "PERFBOOSTMODE");
        var maximumFrequency = await ReadAcValueAsync(PowerInterop.ProcFreqMaxGuid, "PROCFREQMAX");
        return boostIndex is int boost && (maximumFrequency is null or 0)
            ? FromBoostIndex(boost)
            : PowerMode.Custom;
    }

    public async Task SetModeAsync(PowerMode mode, bool writeLog = true)
    {
        if ((int)mode is < 0 or > 6)
            throw new InvalidOperationException("Güç modu geri yüklenebilir bir Windows boost değeri değil.");

        var previousState = await CaptureStateAsync();
        var previous = previousState.MaximumFrequencyMhz == 0
            ? FromBoostIndex(previousState.BoostMode)
            : PowerMode.Custom;
        var requiresSafeRecovery = mode == PowerMode.AggressivePerformance;
        if (requiresSafeRecovery) await ArmSafeRecoveryAsync();

        try
        {
            await ApplyPowerValuesAsync(mode);
            _ = await VerifyModeAsync(mode);

            // Güvenli bir mod uygulandıktan sonra kurtarma görevi kaldırılır.
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

    public async Task<PowerStateSnapshot> CaptureStateAsync()
    {
        var boost = await ReadAcValueAsync(PowerInterop.PerfBoostModeGuid, "PERFBOOSTMODE");
        var maximumFrequency = await ReadAcValueAsync(PowerInterop.ProcFreqMaxGuid, "PROCFREQMAX");
        return new PowerStateSnapshot(boost ?? (int)PowerMode.StablePerformance, maximumFrequency ?? 0);
    }

    public async Task RestoreStateAsync(PowerStateSnapshot state)
    {
        if (state.BoostMode is < 0 or > 6 || state.MaximumFrequencyMhz < 0)
            throw new InvalidOperationException("Geri yüklenecek güç durumu geçersiz.");

        var requiresSafeRecovery = state.BoostMode == (int)PowerMode.AggressivePerformance;
        if (requiresSafeRecovery) await ArmSafeRecoveryAsync();
        await ApplyAcValuesAsync(state.BoostMode, state.MaximumFrequencyMhz);
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
            await _logger.LogAsync(new ActionLogEntry
            {
                Action = "Güvenli güç kurtarması",
                Detail = "Windows oturum açılışında Kararlı Performans uygulandı.",
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
        var win32Boost = PowerInterop.WriteAcValue(PowerInterop.PerfBoostModeGuid, (uint)boostIndex);
        var win32Freq = PowerInterop.WriteAcValue(PowerInterop.ProcFreqMaxGuid, (uint)maximumFrequencyMhz);

        var guidCommands = new[]
        {
            $"/setacvalueindex SCHEME_CURRENT {PowerInterop.ProcessorSubgroupGuid} {PowerInterop.ProcFreqMaxGuid} {maximumFrequencyMhz}",
            $"/setacvalueindex SCHEME_CURRENT {PowerInterop.ProcessorSubgroupGuid} {PowerInterop.PerfBoostModeGuid} {boostIndex}",
            "/setactive SCHEME_CURRENT"
        };

        var guidFailed = false;
        foreach (var command in guidCommands)
        {
            var result = await ProcessRunner.RunAsync("powercfg.exe", command);
            if (result.ExitCode != 0) guidFailed = true;
        }

        if (guidFailed && !win32Boost)
        {
            var aliasCommands = new[]
            {
                $"/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX {maximumFrequencyMhz}",
                $"/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE {boostIndex}",
                "/setactive SCHEME_CURRENT"
            };

            foreach (var command in aliasCommands)
            {
                var result = await ProcessRunner.RunAsync("powercfg.exe", command);
                if (result.ExitCode != 0 && !win32Boost)
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error) ? "Güç modu uygulanamadı." : result.Error.Trim());
            }
        }

        _ = ProcessRunner.RunAsync("powercfg.exe",
            $"/attributes {PowerInterop.ProcessorSubgroupGuid} {PowerInterop.PerfBoostModeGuid} -ATTRIB_HIDE");
    }

    private static async Task<bool> VerifyModeAsync(PowerMode mode)
    {
        var boost = await ReadAcValueAsync(PowerInterop.PerfBoostModeGuid, "PERFBOOSTMODE");
        var maximumFrequency = await ReadAcValueAsync(PowerInterop.ProcFreqMaxGuid, "PROCFREQMAX");
        return boost is null || (boost == (int)mode && (maximumFrequency is null or 0));
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

    private static async Task<int?> ReadAcValueAsync(Guid settingGuid, string alias)
    {
        var win32Val = PowerInterop.ReadAcValue(settingGuid);
        if (win32Val.HasValue) return win32Val.Value;

        var regVal = ReadRegistryAcValue(settingGuid);
        if (regVal.HasValue) return regVal.Value;

        var guidResult = await ProcessRunner.RunAsync("powercfg.exe", $"/query SCHEME_CURRENT {PowerInterop.ProcessorSubgroupGuid} {settingGuid}");
        if (guidResult.ExitCode == 0)
        {
            var parsed = ParseAcValueFromOutput(guidResult.Output);
            if (parsed.HasValue) return parsed.Value;
        }

        var aliasResult = await ProcessRunner.RunAsync("powercfg.exe", $"/query SCHEME_CURRENT SUB_PROCESSOR {alias}");
        if (aliasResult.ExitCode == 0)
        {
            var parsed = ParseAcValueFromOutput(aliasResult.Output);
            if (parsed.HasValue) return parsed.Value;
        }

        return null;
    }

    private static int? ParseAcValueFromOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;

        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (line.Contains("AC", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Alternatif", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Current AC", StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(line, @"0x([0-9a-fA-F]+)");
                if (match.Success)
                {
                    try { return Convert.ToInt32(match.Groups[1].Value, 16); } catch { }
                }
            }
        }

        var values = Regex.Matches(output, @"0x([0-9a-fA-F]{1,8})");
        if (values.Count == 0) return null;
        if (values.Count == 1)
        {
            try { return Convert.ToInt32(values[0].Groups[1].Value, 16); } catch { }
        }

        try
        {
            return Convert.ToInt32(values[^2].Groups[1].Value, 16);
        }
        catch { }

        return null;
    }

    private static int? ReadRegistryAcValue(Guid settingGuid)
    {
        try
        {
            var activeGuid = PowerInterop.GetActiveSchemeGuid()?.ToString();
            if (string.IsNullOrEmpty(activeGuid))
            {
                using var pKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes");
                activeGuid = pKey?.GetValue("ActivePowerScheme") as string;
            }
            if (string.IsNullOrEmpty(activeGuid)) return null;

            var subKeyPath = $@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes\{activeGuid}\{PowerInterop.ProcessorSubgroupGuid}\{settingGuid}";
            using var key = Registry.LocalMachine.OpenSubKey(subKeyPath);
            var val = key?.GetValue("ACSettingIndex");
            if (val is int intVal) return intVal;
            if (val is long longVal) return (int)longVal;
        }
        catch { }
        return null;
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

    private static class PowerInterop
    {
        public static readonly Guid ProcessorSubgroupGuid = new("54533251-82be-4824-96c1-47b60b740d00");
        public static readonly Guid PerfBoostModeGuid = new("be337238-0d82-4146-a960-4f3749d470c7");
        public static readonly Guid ProcFreqMaxGuid = new("75b0ae3f-bce0-45a7-8c89-c9611c25e100");

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern uint PowerGetActiveScheme(IntPtr RootPowerKey, out IntPtr ActivePolicyGuid);

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern uint PowerReadACValueIndex(
            IntPtr RootPowerKey,
            ref Guid SchemeGuid,
            ref Guid SubGroupOfPowerSettingsGuid,
            ref Guid PowerSettingGuid,
            out uint AcValueIndex);

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern uint PowerWriteACValueIndex(
            IntPtr RootPowerKey,
            ref Guid SchemeGuid,
            ref Guid SubGroupOfPowerSettingsGuid,
            ref Guid PowerSettingGuid,
            uint AcValueIndex);

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern uint PowerSetActiveScheme(
            IntPtr UserRootPowerKey,
            ref Guid SchemeGuid);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LocalFree(IntPtr hMem);

        public static Guid? GetActiveSchemeGuid()
        {
            try
            {
                if (PowerGetActiveScheme(IntPtr.Zero, out var pActive) == 0 && pActive != IntPtr.Zero)
                {
                    var guid = Marshal.PtrToStructure<Guid>(pActive);
                    LocalFree(pActive);
                    return guid;
                }
            }
            catch { }
            return null;
        }

        public static int? ReadAcValue(Guid settingGuid)
        {
            try
            {
                var activeGuid = GetActiveSchemeGuid();
                if (activeGuid is null) return null;

                var scheme = activeGuid.Value;
                var subGroup = ProcessorSubgroupGuid;
                var setting = settingGuid;

                if (PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref subGroup, ref setting, out var val) == 0)
                {
                    return (int)val;
                }
            }
            catch { }
            return null;
        }

        public static bool WriteAcValue(Guid settingGuid, uint value)
        {
            try
            {
                var activeGuid = GetActiveSchemeGuid();
                if (activeGuid is null) return false;

                var scheme = activeGuid.Value;
                var subGroup = ProcessorSubgroupGuid;
                var setting = settingGuid;

                if (PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref subGroup, ref setting, value) == 0)
                {
                    PowerSetActiveScheme(IntPtr.Zero, ref scheme);
                    return true;
                }
            }
            catch { }
            return false;
        }
    }
}

