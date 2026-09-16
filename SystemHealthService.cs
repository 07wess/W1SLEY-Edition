using System.Text.Json;

namespace W1sleyEdition;

public sealed class SystemHealthService
{
    private sealed class HealthPayload
    {
        public HealthDevice[] Devices { get; set; } = [];
        public HealthDisk[] Disks { get; set; } = [];
        public HealthEvent[] Events { get; set; } = [];
    }

    private sealed class HealthDevice { public string FriendlyName { get; set; } = ""; public string Status { get; set; } = ""; public int Problem { get; set; } }
    private sealed class HealthDisk { public string FriendlyName { get; set; } = ""; public string HealthStatus { get; set; } = ""; public string OperationalStatus { get; set; } = ""; }
    private sealed class HealthEvent { public string ProviderName { get; set; } = ""; public int Id { get; set; } public string LevelDisplayName { get; set; } = ""; public string Message { get; set; } = ""; }

    public async Task<IReadOnlyList<HealthItem>> ScanAsync(CancellationToken cancellationToken = default)
    {
        const string script = "$d=@(Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object {$_.Status -ne 'OK'} | Select-Object -First 25 FriendlyName,Status,@{n='Problem';e={[int]$_.Problem}});" +
                              "$k=@(Get-PhysicalDisk -ErrorAction SilentlyContinue | Select-Object FriendlyName,@{n='HealthStatus';e={[string]$_.HealthStatus}},@{n='OperationalStatus';e={[string]($_.OperationalStatus -join ', ')}});" +
                              "$e=@(Get-WinEvent -FilterHashtable @{LogName='System';StartTime=(Get-Date).AddHours(-24);Level=1,2} -ErrorAction SilentlyContinue | Where-Object {$_.ProviderName -match 'WHEA|Disk|Ntfs|stornvme|Kernel-Power|Display'} | Select-Object -First 20 ProviderName,Id,LevelDisplayName,@{n='Message';e={($_.Message -replace '\\s+',' ').Trim()}});" +
                              "[pscustomobject]@{Devices=$d;Disks=$k;Events=$e} | ConvertTo-Json -Depth 5 -Compress";
        var result = await ProcessRunner.RunPowerShellAsync(script, cancellationToken);
        if (result.ExitCode != 0) throw new InvalidOperationException("Sistem sağlığı bilgileri okunamadı: " + result.Error.Trim());
        var payload = JsonSerializer.Deserialize<HealthPayload>(result.Output, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new HealthPayload();
        var items = new List<HealthItem>();
        items.AddRange(payload.Disks.Select(disk => new HealthItem { Category = "Disk", Status = disk.HealthStatus, Detail = $"{disk.FriendlyName} — {disk.OperationalStatus}" }));
        items.AddRange(payload.Devices.Select(device => new HealthItem { Category = "Aygıt", Status = device.Status, Detail = $"{device.FriendlyName} (Kod {device.Problem})" }));
        items.AddRange(payload.Events.Select(item => new HealthItem { Category = "Olay", Status = $"{item.LevelDisplayName} / {item.Id}", Detail = $"{item.ProviderName}: {item.Message}" }));
        if (items.Count == 0) items.Add(new HealthItem { Category = "Genel", Status = "İyi", Detail = "Sorunlu aygıt, disk veya son 24 saatte ilgili kritik olay bulunmadı." });
        return items;
    }
}
