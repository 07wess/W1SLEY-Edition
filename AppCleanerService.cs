using System.Text.Json;
using System.Text.RegularExpressions;

namespace W1sleyEdition;

public sealed class AppCleanerService(ActionLogger logger)
{
    private readonly ActionLogger _logger = logger;

    private static readonly string[] ProtectedFragments =
    [
        "WindowsStore", "DesktopAppInstaller", "SecHealthUI", "ShellExperienceHost", "StartMenuExperienceHost",
        "Windows.CBS", "Windows.Search", "VCLibs", "UI.Xaml", "NET.Native", "MicrosoftWindows.Client",
        "WindowsAppRuntime", "StorePurchaseApp", "AAD.BrokerPlugin", "AccountsControl", "LockApp",
        "ApplicationCompatibilityEnhancements", "GamingServices", "GamingApp", "XboxIdentityProvider",
        "DolbyAudio", "WindowsTerminal", "WebExperience", "Client.WebExperience", "MicrosoftEdge"
    ];

    private static readonly string[] RecommendedFragments =
    [
        "BingNews", "BingWeather", "GetHelp", "Getstarted", "MicrosoftSolitaireCollection", "MixedReality",
        "People", "YourPhone", "Clipchamp", "WindowsMaps", "OfficeHub", "Todos", "PowerAutomateDesktop",
        "MicrosoftTeams", "MSTeams", "OutlookForWindows", "DevHome", "FeedbackHub", "549981C3F5F10"
    ];

    private static readonly Dictionary<string, string> FriendlyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AppleInc.iTunes"] = "iTunes",
        ["Microsoft.GamingApp"] = "Xbox",
        ["Microsoft.GamingServices"] = "Xbox Gaming Services",
        ["Microsoft.BingSearch"] = "Bing Search",
        ["Microsoft.WindowsCalculator"] = "Calculator",
        ["Microsoft.WindowsTerminal"] = "Windows Terminal",
        ["Microsoft.YourPhone"] = "Phone Link",
        ["Microsoft.MicrosoftSolitaireCollection"] = "Microsoft Solitaire",
        ["Microsoft.WindowsMaps"] = "Windows Maps",
        ["Microsoft.GetHelp"] = "Get Help",
        ["Microsoft.Getstarted"] = "Windows Tips",
        ["Microsoft.ZuneMusic"] = "Media Player",
        ["Microsoft.ZuneVideo"] = "Movies & TV",
        ["Microsoft.Windows.Photos"] = "Microsoft Photos",
        ["Clipchamp.Clipchamp"] = "Clipchamp"
    };

    public async Task<IReadOnlyList<AppxItem>> ScanAsync()
    {
        const string script = "$a=@(Get-AppxPackage -PackageTypeFilter Main,Bundle -ErrorAction SilentlyContinue | Where-Object {-not $_.IsFramework} | Select-Object Name,PackageFullName,InstallLocation,NonRemovable); ConvertTo-Json -InputObject $a -Depth 4 -Compress";
        var result = await ProcessRunner.RunPowerShellAsync(script);
        if (result.ExitCode != 0) throw new InvalidOperationException("Windows uygulamaları okunamadı: " + result.Error.Trim());
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(result.Output) ? "[]" : result.Output);
        var elements = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray().ToArray()
            : [document.RootElement];

        var list = new List<AppxItem>();
        foreach (var group in elements.GroupBy(element => ReadString(element, "Name"), StringComparer.OrdinalIgnoreCase))
        {
            var name = group.Key;
            if (string.IsNullOrWhiteSpace(name)) continue;
            var preferred = group
                .OrderByDescending(element => ReadString(element, "PackageFullName").Contains("_neutral_", StringComparison.OrdinalIgnoreCase))
                .First();
            var fullName = ReadString(preferred, "PackageFullName");
            var nonRemovable = group.Any(element => element.TryGetProperty("NonRemovable", out var property) && property.ValueKind == JsonValueKind.True);
            var protectedFragment = ProtectedFragments.FirstOrDefault(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase));
            var isProtected = nonRemovable || protectedFragment is not null;
            var isRecommended = !isProtected && RecommendedFragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase));
            var (category, glyph) = Categorize(name);
            list.Add(new AppxItem
            {
                Name = name,
                DisplayName = ToFriendlyName(name),
                Publisher = GetPublisher(name),
                Category = category,
                CategoryGlyph = glyph,
                Version = ReadVersion(fullName),
                PackageFullName = fullName,
                InstallLocation = ReadString(preferred, "InstallLocation"),
                IsProtected = isProtected,
                IsRecommended = isRecommended,
                ProtectionReason = nonRemovable
                    ? "Windows tarafından korunuyor"
                    : isProtected
                        ? "Sistem, oyun veya çalışma bileşeni"
                        : isRecommended
                            ? "İsteğe bağlı uygulama"
                            : "Kullanıcı kararıyla kaldırılabilir"
            });
        }
        return list
            .OrderBy(item => item.IsProtected)
            .ThenByDescending(item => item.IsRecommended)
            .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<(int Removed, List<string> Errors)> RemoveAsync(IEnumerable<AppxItem> apps)
    {
        var removed = 0;
        var errors = new List<string>();
        foreach (var app in apps.Where(item => item.IsSelected && !item.IsProtected))
        {
            var escaped = app.PackageFullName.Replace("'", "''");
            var result = await ProcessRunner.RunPowerShellAsync($"Remove-AppxPackage -Package '{escaped}' -ErrorAction Stop");
            if (result.ExitCode == 0)
            {
                removed++;
                await _logger.LogAsync(new ActionLogEntry
                {
                    Action = "Windows uygulaması kaldırıldı",
                    Detail = app.DisplayName,
                    Success = true
                });
            }
            else
            {
                errors.Add($"{app.DisplayName}: {result.Error.Trim()}");
                await _logger.LogAsync(new ActionLogEntry { Action = "Uygulama kaldırma", Detail = app.DisplayName, Success = false, Error = result.Error.Trim() });
            }
        }
        return (removed, errors);
    }

    private static string ToFriendlyName(string name)
    {
        if (FriendlyNames.TryGetValue(name, out var friendly)) return friendly;
        var value = name.Split('.').LastOrDefault() ?? name;
        value = Regex.Replace(value, "([a-z0-9])([A-Z])", "$1 $2");
        value = Regex.Replace(value, "([A-Z]+)([A-Z][a-z])", "$1 $2");
        return value.Replace('_', ' ').Trim();
    }

    private static string GetPublisher(string name)
    {
        var prefix = name.Split('.').FirstOrDefault() ?? "Uygulama";
        return prefix.Equals("Microsoft", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)
            ? "Microsoft"
            : Regex.Replace(prefix, "([a-z])([A-Z])", "$1 $2");
    }

    private static string ReadVersion(string packageFullName)
    {
        var parts = packageFullName.Split('_');
        return parts.Length > 1 && Version.TryParse(parts[1], out var version) ? version.ToString() : "—";
    }

    private static (string Category, string Glyph) Categorize(string name)
    {
        if (ContainsAny(name, "Gaming", "Xbox", "Solitaire")) return ("Oyun", "\uE7FC");
        if (ContainsAny(name, "Video", "Audio", "Music", "Photo", "Media", "Codec", "Dolby", "AV1", "HEIF", "Webp")) return ("Medya", "\uE8B2");
        if (ContainsAny(name, "Teams", "Outlook", "Phone", "People", "Mail")) return ("İletişim", "\uE8BD");
        if (ContainsAny(name, "Windows", "MicrosoftWindows", "SecHealth", "Store", "AppInstaller", "Compatibility")) return ("Windows", "\uE770");
        if (!name.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)) return ("Üçüncü taraf", "\uE7B8");
        return ("Araç", "\uE71D");
    }

    private static bool ContainsAny(string value, params string[] fragments) =>
        fragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static string ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) ? value.GetString() ?? "" : "";
}
