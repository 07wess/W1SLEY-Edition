using Microsoft.Win32;
using System.Text.Json;

namespace W1sleyEdition;

public sealed class StartupManagerService(ActionLogger logger)
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly ActionLogger _logger = logger;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public IReadOnlyList<StartupItem> GetItems()
    {
        var items = new List<StartupItem>();
        ReadRegistry(Registry.CurrentUser, "HKCU", true, items);
        ReadRegistry(Registry.LocalMachine, "HKLM", false, items);
        foreach (var entry in ReadDisabled())
        {
            items.Add(new StartupItem
            {
                Name = entry.Name,
                Command = entry.Command,
                Location = entry.Location,
                IsEnabled = false,
                CanToggle = entry.Location == "HKCU"
            });
        }
        return items.OrderByDescending(item => item.IsEnabled).ThenBy(item => item.Name).ToList();
    }

    public async Task DisableAsync(StartupItem item)
    {
        if (!item.CanToggle || item.Location != "HKCU") throw new InvalidOperationException("Bu başlangıç girdisi yönetici veya sistem tarafından korunuyor.");
        using var key = Registry.CurrentUser.OpenSubKey(RunPath, true) ?? throw new InvalidOperationException("Başlangıç anahtarı açılamadı.");
        var value = key.GetValue(item.Name)?.ToString() ?? item.Command;
        var disabled = ReadDisabled().Where(entry => !entry.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        disabled.Add(new DisabledStartupEntry { Name = item.Name, Command = value, Location = "HKCU" });
        WriteDisabled(disabled);
        key.DeleteValue(item.Name, false);
        await _logger.LogAsync(new ActionLogEntry
        {
            Action = "Başlangıç kapatıldı",
            Detail = item.Name,
            Success = true,
            UndoType = "startup-enable",
            UndoData = new Dictionary<string, string> { ["name"] = item.Name, ["command"] = value }
        });
    }

    public async Task EnableAsync(StartupItem item)
    {
        var entry = ReadDisabled().FirstOrDefault(value => value.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase));
        if (entry is null) throw new InvalidOperationException("Yedeklenmiş başlangıç komutu bulunamadı.");
        using var key = Registry.CurrentUser.CreateSubKey(RunPath, true);
        key?.SetValue(entry.Name, entry.Command, RegistryValueKind.String);
        var disabled = ReadDisabled().Where(value => !value.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        WriteDisabled(disabled);
        await _logger.LogAsync(new ActionLogEntry
        {
            Action = "Başlangıç açıldı",
            Detail = item.Name,
            Success = true,
            UndoType = "startup-disable",
            UndoData = new Dictionary<string, string> { ["name"] = item.Name, ["command"] = entry.Command }
        });
    }

    public void RestoreWithoutLogging(string name, string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunPath, true);
        key?.SetValue(name, command, RegistryValueKind.String);
        WriteDisabled(ReadDisabled().Where(value => !value.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList());
    }

    public void DisableWithoutLogging(string name, string command)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunPath, true);
        key?.DeleteValue(name, false);
        var disabled = ReadDisabled().Where(value => !value.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        disabled.Add(new DisabledStartupEntry { Name = name, Command = command, Location = "HKCU" });
        WriteDisabled(disabled);
    }

    private static void ReadRegistry(RegistryKey hive, string location, bool canToggle, ICollection<StartupItem> target)
    {
        try
        {
            using var key = hive.OpenSubKey(RunPath, false);
            if (key is null) return;
            foreach (var name in key.GetValueNames())
            {
                target.Add(new StartupItem
                {
                    Name = name,
                    Command = key.GetValue(name)?.ToString() ?? "",
                    Location = location,
                    IsEnabled = true,
                    CanToggle = canToggle
                });
            }
        }
        catch { }
    }

    private List<DisabledStartupEntry> ReadDisabled()
    {
        if (!File.Exists(AppPaths.StartupBackupPath)) return [];
        try { return JsonSerializer.Deserialize<List<DisabledStartupEntry>>(File.ReadAllText(AppPaths.StartupBackupPath)) ?? []; }
        catch { return []; }
    }

    private void WriteDisabled(List<DisabledStartupEntry> entries)
    {
        AppPaths.EnsureCreated();
        File.WriteAllText(AppPaths.StartupBackupPath, JsonSerializer.Serialize(entries, _jsonOptions));
    }
}
