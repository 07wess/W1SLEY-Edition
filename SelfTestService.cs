using System.Text.Json;

namespace W1sleyEdition;

public static class SelfTestService
{
    public static async Task RunAsync()
    {
        var logger = new ActionLogger();
        var power = new PowerModeService(logger);
        var monitor = new SystemMonitorService();
        var optimizer = new ProcessOptimizationService(logger);
        var startup = new StartupManagerService(logger);
        var network = new NetworkOptimizationService(logger);
        var profiles = new GameProfileService(power, optimizer, network, logger);
        var cleaner = new AppCleanerService(logger);
        var crashDetector = new CrashDetectorService(logger);
        var compatibility = new SystemCompatibilityService();
        var winOpt = new WindowsOptimizationService(logger);

        var mode = await power.GetCurrentModeAsync();
        _ = await monitor.CaptureAsync();
        await Task.Delay(300);
        var snapshot = await monitor.CaptureAsync();
        var processes = optimizer.GetProcesses();
        var startupItems = startup.GetItems();
        var profileItems = profiles.Load();
        var apps = await cleaner.ScanAsync();
        var crashEvents = await crashDetector.ScanAsync(30);
        var setupChecks = await compatibility.ScanAsync();
        var tweaks = winOpt.CreateDefaultTweaks();
        await winOpt.RefreshStatusesAsync(tweaks);

        var result = new
        {
            timestamp = DateTimeOffset.Now,
            success = true,
            powerMode = PowerModeService.Label(mode),
            cpuMhz = snapshot.CpuMhz,
            cpuTemperature = snapshot.CpuTemperature,
            cpuTemperatureSource = snapshot.CpuTemperatureSource,
            totalMemoryMb = snapshot.TotalMemoryMb,
            gpu = snapshot.GpuName,
            gpuTemperature = snapshot.GpuTemperature,
            gpuCoreClockMhz = snapshot.GpuCoreClockMhz,
            processCount = processes.Count,
            protectedProcessCount = processes.Count(item => item.IsProtected),
            startupCount = startupItems.Count,
            profileCount = profileItems.Count,
            appxCount = apps.Count,
            crashEventCount = crashEvents.Count,
            criticalCrashEventCount = crashEvents.Count(item => item.Severity == "Kritik"),
            setupCheckCount = setupChecks.Count,
            setupWarningCount = setupChecks.Count(item => item.State is "Uyarı" or "İncele"),
            windowsOptimizeTweakCount = tweaks.Count,
            windowsOptimizeAppliedCount = tweaks.Count(t => t.IsApplied),
            actionLogReadable = logger.ReadRecent(1) is not null
        };
        File.WriteAllText(Path.Combine(AppPaths.LogsDirectory, "self-test.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }
}
