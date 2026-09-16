using Microsoft.Win32;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace W1sleyEdition;

public sealed class NetworkOptimizationService
{
    private const string InterfacesKey = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
    private readonly ActionLogger _logger;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public NetworkOptimizationService(ActionLogger logger)
    {
        _logger = logger;
    }

    public async Task<bool> ApplyLatencyOptimizationAsync()
    {
        string? snapshotJson = null;
        try
        {
            snapshotJson = await Task.Run(() =>
            {
                var activeIds = GetActiveAdapters().Select(item => NormalizeId(item.Id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var snapshot = new TcpRegistrySnapshot();
                using var interfaces = Registry.LocalMachine.OpenSubKey(InterfacesKey, writable: true)
                    ?? throw new InvalidOperationException("TCP/IP arayüz kayıtları açılamadı.");
                var targetNames = interfaces.GetSubKeyNames().Where(name => activeIds.Contains(NormalizeId(name))).ToList();
                foreach (var subKeyName in targetNames)
                {
                    using var adapterKey = interfaces.OpenSubKey(subKeyName, writable: true);
                    if (adapterKey is null) continue;
                    BackupDword(snapshot, $@"{InterfacesKey}\{subKeyName}", adapterKey, "TcpAckFrequency");
                    BackupDword(snapshot, $@"{InterfacesKey}\{subKeyName}", adapterKey, "TCPNoDelay");
                }

                using var msmq = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\MSMQ\Parameters", writable: true);
                if (msmq is not null)
                    BackupDword(snapshot, @"SOFTWARE\Microsoft\MSMQ\Parameters", msmq, "TCPNoDelay");

                if (targetNames.Count == 0) throw new InvalidOperationException("Etkin Ethernet/Wi-Fi bağdaştırıcısı için kayıt bulunamadı.");
                var backupJson = JsonSerializer.Serialize(snapshot, _jsonOptions);
                try
                {
                    var changed = 0;
                    foreach (var subKeyName in targetNames)
                    {
                        using var adapterKey = interfaces.OpenSubKey(subKeyName, writable: true);
                        if (adapterKey is null) continue;
                        adapterKey.SetValue("TcpAckFrequency", 1, RegistryValueKind.DWord);
                        adapterKey.SetValue("TCPNoDelay", 1, RegistryValueKind.DWord);
                        if (ReadDword(adapterKey, "TcpAckFrequency") == 1 && ReadDword(adapterKey, "TCPNoDelay") == 1) changed++;
                    }
                    if (msmq is not null) msmq.SetValue("TCPNoDelay", 1, RegistryValueKind.DWord);
                    if (changed != targetNames.Count) throw new InvalidOperationException("Bazı etkin bağdaştırıcılarda kayıt doğrulaması başarısız oldu.");
                    return backupJson;
                }
                catch
                {
                    RestoreTcpSnapshot(snapshot);
                    throw;
                }
            });

            await _logger.LogAsync(new ActionLogEntry
            {
                Action = "Ağ gecikme ayarı",
                Detail = "TCP ACK ve NoDelay yalnızca etkin fiziksel bağdaştırıcılara uygulandı ve doğrulandı.",
                Success = true,
                UndoType = "network-latency",
                UndoData = new Dictionary<string, string> { ["snapshot"] = snapshotJson }
            });
            return true;
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(snapshotJson))
                try { await RestoreLatencyOptimizationAsync(snapshotJson); } catch { }
            _logger.Log("Ağ gecikme ayarı", $"Latency optimizasyon hatası: {ex.Message}", false, null);
            return false;
        }
    }

    public Task<bool> RestoreLatencyOptimizationAsync(string snapshotJson)
    {
        return Task.Run(() =>
        {
            try
            {
                var snapshot = JsonSerializer.Deserialize<TcpRegistrySnapshot>(snapshotJson, _jsonOptions)
                    ?? throw new InvalidOperationException("Ağ yedeği okunamadı.");
                return RestoreTcpSnapshot(snapshot);
            }
            catch { return false; }
        });
    }

    public async Task<bool> FlushDnsCacheAsync()
    {
        try
        {
            var result = await ProcessRunner.RunAsync("ipconfig.exe", "/flushdns");
            var ok = result.ExitCode == 0;
            _logger.Log("DNS önbelleği", ok ? "DNS önbelleği temizlendi ve komut doğrulandı." : $"DNS önbelleği temizlenemedi: {result.Error.Trim()}", ok, null);
            return ok;
        }
        catch (Exception ex)
        {
            _logger.Log("DNS önbelleği", $"DNS önbelleği hatası: {ex.Message}", false, null);
            return false;
        }
    }

    public async Task<bool> SetDnsAsync(string primaryDns, string secondaryDns, bool createUndo = true)
    {
        if (!IsIpv4(primaryDns) || !IsIpv4(secondaryDns))
        {
            _logger.Log("DNS ayarı", "Geçersiz IPv4 DNS adresi reddedildi.", false, null);
            return false;
        }

        var snapshot = await CaptureDnsStateAsync();
        try
        {
            var adapters = GetActiveAdapters();
            if (adapters.Count == 0) throw new InvalidOperationException("Etkin Ethernet/Wi-Fi bağdaştırıcısı bulunamadı.");
            var commands = adapters.Select(adapter =>
                $"Set-DnsClientServerAddress -InterfaceIndex {adapter.InterfaceIndex} -ServerAddresses @('{primaryDns}','{secondaryDns}')");
            var result = await ProcessRunner.RunPowerShellAsync("$ErrorActionPreference='Stop';" + string.Join(";", commands));
            if (result.ExitCode != 0) throw new InvalidOperationException(CleanError(result));

            await Task.Delay(650);
            if (!VerifyDns(adapters.Select(item => item.InterfaceIndex), primaryDns, secondaryDns))
                throw new InvalidOperationException("Komut tamamlandı ancak etkin bağdaştırıcıların DNS sonucu doğrulanamadı.");

            var entry = new ActionLogEntry
            {
                Action = "DNS ayarı",
                Detail = $"DNS doğrulandı: {primaryDns}, {secondaryDns}",
                Success = true
            };
            if (createUndo)
            {
                entry.UndoType = "network-dns";
                entry.UndoData = new Dictionary<string, string> { ["snapshot"] = snapshot };
            }
            await _logger.LogAsync(entry);
            return true;
        }
        catch (Exception ex)
        {
            try { await RestoreDnsStateAsync(snapshot); } catch { }
            _logger.Log("DNS ayarı", $"DNS değiştirilemedi: {ex.Message}", false, null);
            return false;
        }
    }

    public async Task<bool> ResetDnsToDhcpAsync(bool createUndo = true)
    {
        var snapshot = await CaptureDnsStateAsync();
        try
        {
            var adapters = GetActiveAdapters();
            if (adapters.Count == 0) throw new InvalidOperationException("Etkin Ethernet/Wi-Fi bağdaştırıcısı bulunamadı.");
            var commands = adapters.Select(adapter => $"Set-DnsClientServerAddress -InterfaceIndex {adapter.InterfaceIndex} -ResetServerAddresses");
            var result = await ProcessRunner.RunPowerShellAsync("$ErrorActionPreference='Stop';" + string.Join(";", commands));
            if (result.ExitCode != 0) throw new InvalidOperationException(CleanError(result));

            var entry = new ActionLogEntry { Action = "DNS ayarı", Detail = "DNS otomatik (DHCP) ayarına döndürüldü.", Success = true };
            if (createUndo)
            {
                entry.UndoType = "network-dns";
                entry.UndoData = new Dictionary<string, string> { ["snapshot"] = snapshot };
            }
            await _logger.LogAsync(entry);
            return true;
        }
        catch (Exception ex)
        {
            try { await RestoreDnsStateAsync(snapshot); } catch { }
            _logger.Log("DNS ayarı", $"DNS sıfırlanamadı: {ex.Message}", false, null);
            return false;
        }
    }

    public Task<string> CaptureDnsStateAsync()
    {
        return Task.Run(() =>
        {
            var snapshot = new DnsSnapshot();
            using var interfaces = Registry.LocalMachine.OpenSubKey(InterfacesKey);
            foreach (var adapter in GetActiveAdapters())
            {
                var registryName = interfaces?.GetSubKeyNames().FirstOrDefault(name =>
                    NormalizeId(name).Equals(NormalizeId(adapter.Id), StringComparison.OrdinalIgnoreCase));
                string configured = "";
                if (registryName is not null)
                {
                    using var key = interfaces!.OpenSubKey(registryName);
                    configured = key?.GetValue("NameServer")?.ToString()?.Trim() ?? "";
                }
                snapshot.Adapters.Add(new DnsAdapterSnapshot
                {
                    Id = adapter.Id,
                    InterfaceIndex = adapter.InterfaceIndex,
                    Name = adapter.Name,
                    Automatic = string.IsNullOrWhiteSpace(configured),
                    Servers = adapter.Servers
                });
            }
            return JsonSerializer.Serialize(snapshot, _jsonOptions);
        });
    }

    public async Task<bool> RestoreDnsStateAsync(string snapshotJson)
    {
        try
        {
            var snapshot = JsonSerializer.Deserialize<DnsSnapshot>(snapshotJson, _jsonOptions)
                ?? throw new InvalidOperationException("DNS yedeği okunamadı.");
            if (snapshot.Adapters.Count == 0) return true;

            var commands = new List<string>();
            foreach (var adapter in snapshot.Adapters)
            {
                var currentAdapter = GetActiveAdapters().FirstOrDefault(current =>
                    current.Id.Equals(adapter.Id, StringComparison.OrdinalIgnoreCase) || current.Name.Equals(adapter.Name, StringComparison.OrdinalIgnoreCase));
                var interfaceIndex = currentAdapter?.InterfaceIndex ?? adapter.InterfaceIndex;
                if (adapter.Automatic || adapter.Servers.Count == 0)
                {
                    commands.Add($"Set-DnsClientServerAddress -InterfaceIndex {interfaceIndex} -ResetServerAddresses");
                    continue;
                }

                var servers = adapter.Servers.Where(IsIpv4).Distinct().ToList();
                if (servers.Count == 0) return false;
                commands.Add($"Set-DnsClientServerAddress -InterfaceIndex {interfaceIndex} -ServerAddresses @({string.Join(",", servers.Select(ip => $"'{ip}'"))})");
            }

            var result = await ProcessRunner.RunPowerShellAsync("$ErrorActionPreference='Stop';" + string.Join(";", commands));
            return result.ExitCode == 0;
        }
        catch { return false; }
    }

    public Task<string> GetActiveDnsAsync()
    {
        return Task.Run(() =>
        {
            var primary = GetActiveAdapters().SelectMany(item => item.Servers).FirstOrDefault(IsIpv4);
            if (primary is null) return "DHCP";
            if (primary is "1.1.1.1" or "1.0.0.1") return "Cloudflare";
            if (primary is "8.8.8.8" or "8.8.4.4") return "Google";
            return IsPrivateOrLoopback(primary) ? "DHCP" : primary;
        });
    }

    private static List<ActiveAdapter> GetActiveAdapters()
    {
        var result = new List<ActiveAdapter>();
        foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (network.OperationalStatus != OperationalStatus.Up || !IsPhysicalAdapter(network.NetworkInterfaceType)) continue;
            try
            {
                var properties = network.GetIPProperties();
                var ipv4 = properties.GetIPv4Properties();
                if (ipv4 is null || !properties.UnicastAddresses.Any(item => item.Address.AddressFamily == AddressFamily.InterNetwork)) continue;
                result.Add(new ActiveAdapter(
                    network.Id,
                    network.Name,
                    ipv4.Index,
                    properties.DnsAddresses.Where(address => address.AddressFamily == AddressFamily.InterNetwork).Select(address => address.ToString()).ToList()));
            }
            catch { }
        }
        return result;
    }

    private static bool VerifyDns(IEnumerable<int> indices, params string[] expected)
    {
        var current = GetActiveAdapters().ToDictionary(item => item.InterfaceIndex);
        foreach (var index in indices)
        {
            if (!current.TryGetValue(index, out var adapter) || expected.Any(ip => !adapter.Servers.Contains(ip, StringComparer.OrdinalIgnoreCase)))
                return false;
        }
        return true;
    }

    private static bool IsPhysicalAdapter(NetworkInterfaceType type) => type is
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211 or
        NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.FastEthernetT;

    private static string NormalizeId(string value) => value.Trim().Trim('{', '}');
    private static bool IsIpv4(string value) => IPAddress.TryParse(value, out var address) && address.AddressFamily == AddressFamily.InterNetwork;

    private static bool IsPrivateOrLoopback(string value)
    {
        if (!IPAddress.TryParse(value, out var address)) return false;
        if (IPAddress.IsLoopback(address)) return true;
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) || (bytes[0] == 192 && bytes[1] == 168);
    }

    private static string CleanError(ProcessResult result) =>
        string.IsNullOrWhiteSpace(result.Error) ? $"Komut çıkış kodu: {result.ExitCode}" : result.Error.Trim();

    private static void BackupDword(TcpRegistrySnapshot snapshot, string path, RegistryKey key, string name)
    {
        var existed = key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase);
        snapshot.Values.Add(new TcpRegistryValue
        {
            KeyPath = path,
            ValueName = name,
            Existed = existed,
            Value = existed ? ReadDword(key, name) : null
        });
    }

    private static int? ReadDword(RegistryKey key, string name)
    {
        try { return Convert.ToInt32(key.GetValue(name)); }
        catch { return null; }
    }

    private static bool RestoreTcpSnapshot(TcpRegistrySnapshot snapshot)
    {
        foreach (var item in snapshot.Values)
        {
            using var key = Registry.LocalMachine.OpenSubKey(item.KeyPath, writable: true);
            if (key is null) return false;
            if (item.Existed)
                key.SetValue(item.ValueName, item.Value ?? 0, RegistryValueKind.DWord);
            else
                key.DeleteValue(item.ValueName, throwOnMissingValue: false);
        }
        return true;
    }

    private sealed record ActiveAdapter(string Id, string Name, int InterfaceIndex, List<string> Servers);
    private sealed class TcpRegistrySnapshot { public List<TcpRegistryValue> Values { get; set; } = []; }
    private sealed class TcpRegistryValue
    {
        public string KeyPath { get; set; } = "";
        public string ValueName { get; set; } = "";
        public bool Existed { get; set; }
        public int? Value { get; set; }
    }
    private sealed class DnsSnapshot { public List<DnsAdapterSnapshot> Adapters { get; set; } = []; }
    private sealed class DnsAdapterSnapshot
    {
        public string Id { get; set; } = "";
        public int InterfaceIndex { get; set; }
        public string Name { get; set; } = "";
        public bool Automatic { get; set; }
        public List<string> Servers { get; set; } = [];
    }
}
