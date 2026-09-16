namespace W1sleyEdition;

public sealed class DefenderService(ActionLogger logger)
{
    private readonly ActionLogger _logger = logger;

    public async Task<string> StartScanAsync(bool fullScan, CancellationToken cancellationToken)
    {
        var status = await ProcessRunner.RunPowerShellAsync("$s=Get-MpComputerStatus; [pscustomobject]@{Enabled=$s.AntivirusEnabled;Running=$s.RealTimeProtectionEnabled} | ConvertTo-Json -Compress", cancellationToken);
        if (status.ExitCode != 0) throw new InvalidOperationException("Windows güvenlik motorunun durumu okunamadı.");

        var scanType = fullScan ? "FullScan" : "QuickScan";
        var scan = await ProcessRunner.RunPowerShellAsync($"Start-MpScan -ScanType {scanType}; $t=Get-MpThreatDetection -ErrorAction SilentlyContinue | Select-Object -First 20 ThreatName,InitialDetectionTime,ActionSuccess; [pscustomobject]@{{Threats=@($t)}} | ConvertTo-Json -Depth 4 -Compress", cancellationToken);
        var success = scan.ExitCode == 0;
        await _logger.LogAsync(new ActionLogEntry
        {
            Action = fullScan ? "Tam güvenlik taraması" : "Hızlı güvenlik taraması",
            Detail = success ? "Tarama tamamlandı" : "Tarama tamamlanamadı",
            Success = success,
            Error = success ? null : scan.Error.Trim()
        });
        if (!success) throw new InvalidOperationException(string.IsNullOrWhiteSpace(scan.Error) ? "Güvenlik taraması başlatılamadı." : scan.Error.Trim());
        return scan.Output;
    }

    public async Task CancelScanAsync()
    {
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender", "MpCmdRun.exe");
        if (!File.Exists(executable)) return;
        try { await ProcessRunner.RunAsync(executable, "-Scan -Cancel"); }
        catch { }
    }
}
