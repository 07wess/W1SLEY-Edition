using Microsoft.Win32;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace W1sleyEdition;

public sealed class GameDiscoveryService
{
    private static readonly string[] ExcludedExecutableFragments =
    [
        "unins", "uninstall", "crash", "report", "setup", "install", "redist", "vc_redist",
        "easyanticheat", "battleye", "unitycrash", "helper", "updater", "bootstrapper"
    ];

    public Task<IReadOnlyList<DiscoveredGame>> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(cancellationToken), cancellationToken);

    public string GetIconPath(string executablePath) => ExtractIcon(executablePath);

    private IReadOnlyList<DiscoveredGame> Scan(CancellationToken cancellationToken)
    {
        AppPaths.EnsureCreated();
        var games = new Dictionary<string, DiscoveredGame>(StringComparer.OrdinalIgnoreCase);
        ScanSteam(games, cancellationToken);
        ScanEpic(games, cancellationToken);
        ScanRiotGames(games, cancellationToken);
        ScanCommonRoots(games, cancellationToken);
        return games.Values.OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private void ScanRiotGames(IDictionary<string, DiscoveredGame> games, CancellationToken cancellationToken)
    {
        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var valPath = Path.Combine(drive.RootDirectory.FullName, "Riot Games", "VALORANT", "live", "VALORANT.exe");
            if (File.Exists(valPath))
            {
                AddGame(games, "VALORANT", valPath, "Riot Games");
            }
            
            var riotRoot = Path.Combine(drive.RootDirectory.FullName, "Riot Games");
            if (Directory.Exists(riotRoot))
            {
                try
                {
                    foreach (var dir in Directory.GetDirectories(riotRoot))
                    {
                        var name = Path.GetFileName(dir);
                        if (name.Equals("Riot Client", StringComparison.OrdinalIgnoreCase)) continue;
                        var exe = FindBestExecutable(dir, name, cancellationToken);
                        AddGame(games, name, exe, "Riot Games");
                    }
                }
                catch { }
            }
        }
    }

    private void ScanSteam(IDictionary<string, DiscoveredGame> games, CancellationToken cancellationToken)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddSteamRegistryPath(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath", roots);
        AddSteamRegistryPath(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", roots);
        AddIfDirectory(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));

        var libraries = new HashSet<string>(roots, StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            var libraryFile = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraryFile)) continue;
            try
            {
                var text = File.ReadAllText(libraryFile);
                foreach (Match match in Regex.Matches(text, "\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase))
                    AddIfDirectory(libraries, match.Groups[1].Value.Replace("\\\\", "\\"));
            }
            catch { }
        }

        foreach (var library in libraries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var steamApps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamApps)) continue;
            string[] manifests;
            try { manifests = Directory.GetFiles(steamApps, "appmanifest_*.acf", SearchOption.TopDirectoryOnly); }
            catch { continue; }
            foreach (var manifest in manifests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var text = File.ReadAllText(manifest);
                    var name = ReadVdfValue(text, "name");
                    var installDir = ReadVdfValue(text, "installdir");
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(installDir)) continue;
                    var directory = Path.Combine(steamApps, "common", installDir);
                    var executable = FindBestExecutable(directory, name, cancellationToken);
                    AddGame(games, name, executable, "Steam");
                }
                catch (OperationCanceledException) { throw; }
                catch { }
            }
        }
    }

    private void ScanEpic(IDictionary<string, DiscoveredGame> games, CancellationToken cancellationToken)
    {
        var manifestRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(manifestRoot)) return;
        string[] manifests;
        try { manifests = Directory.GetFiles(manifestRoot, "*.item", SearchOption.TopDirectoryOnly); }
        catch { return; }
        foreach (var manifest in manifests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(manifest));
                var root = document.RootElement;
                var name = ReadJsonString(root, "DisplayName");
                var install = ReadJsonString(root, "InstallLocation");
                var launch = ReadJsonString(root, "LaunchExecutable").Replace('/', Path.DirectorySeparatorChar);
                var executable = Path.IsPathRooted(launch) ? launch : Path.Combine(install, launch);
                if (!File.Exists(executable)) executable = FindBestExecutable(install, name, cancellationToken);
                AddGame(games, name, executable, "Epic Games");
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }
    }

    private void ScanCommonRoots(IDictionary<string, DiscoveredGame> games, CancellationToken cancellationToken)
    {
        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed))
        {
            foreach (var folder in new[] { "XboxGames", "Games", "Oyunlar" })
            {
                cancellationToken.ThrowIfCancellationRequested();
                var root = Path.Combine(drive.RootDirectory.FullName, folder);
                if (!Directory.Exists(root)) continue;
                DirectoryInfo[] directories;
                try { directories = new DirectoryInfo(root).GetDirectories(); }
                catch { continue; }
                foreach (var directory in directories.Take(100))
                {
                    var executable = FindBestExecutable(directory.FullName, directory.Name, cancellationToken);
                    AddGame(games, directory.Name, executable, folder == "XboxGames" ? "Xbox" : "Yerel Oyun");
                }
            }
        }
    }

    private void AddGame(IDictionary<string, DiscoveredGame> games, string name, string executable, string source)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(executable) || !File.Exists(executable)) return;
        var key = Path.GetFullPath(executable);
        if (games.ContainsKey(key)) return;
        games[key] = new DiscoveredGame
        {
            Name = name.Trim(),
            ExecutablePath = key,
            Source = source,
            IconPath = ExtractIcon(key)
        };
    }

    private string ExtractIcon(string executablePath)
    {
        if (!File.Exists(executablePath)) return "";
        try
        {
            AppPaths.EnsureCreated();
            var fileName = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(executablePath))).Substring(0, 20) + ".png";
            var iconPath = Path.Combine(AppPaths.GameIconsDirectory, fileName);
            if (File.Exists(iconPath)) return iconPath;
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(executablePath);
            if (icon is null) return "";
            using var bitmap = icon.ToBitmap();
            bitmap.Save(iconPath, System.Drawing.Imaging.ImageFormat.Png);
            return iconPath;
        }
        catch { return ""; }
    }

    private static string FindBestExecutable(string directory, string gameName, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory)) return "";
        var candidates = new List<(string Path, int Score)>();
        var queue = new Queue<(string Directory, int Depth)>();
        queue.Enqueue((directory, 0));
        while (queue.Count > 0 && candidates.Count < 300)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = queue.Dequeue();
            try
            {
                foreach (var file in Directory.EnumerateFiles(current.Directory, "*.exe", SearchOption.TopDirectoryOnly))
                {
                    var baseName = Path.GetFileNameWithoutExtension(file);
                    if (ExcludedExecutableFragments.Any(fragment => baseName.Contains(fragment, StringComparison.OrdinalIgnoreCase))) continue;
                    var score = 100 - current.Depth * 8;
                    if (Normalize(baseName) == Normalize(gameName) || Normalize(baseName) == Normalize(new DirectoryInfo(directory).Name)) score += 200;
                    if (baseName.Contains("win64", StringComparison.OrdinalIgnoreCase) || file.Contains("Binaries", StringComparison.OrdinalIgnoreCase)) score += 20;
                    try { score += (int)Math.Min(80, new FileInfo(file).Length / 1024 / 1024); } catch { }
                    candidates.Add((file, score));
                }
                if (current.Depth >= 4) continue;
                foreach (var child in Directory.EnumerateDirectories(current.Directory).Take(80))
                {
                    var childName = Path.GetFileName(child);
                    if (childName is "_CommonRedist" or "Engine" or "Installer" or "Support") continue;
                    queue.Enqueue((child, current.Depth + 1));
                }
            }
            catch { }
        }
        return candidates.OrderByDescending(candidate => candidate.Score).Select(candidate => candidate.Path).FirstOrDefault() ?? "";
    }

    private static void AddSteamRegistryPath(RegistryKey hive, string keyPath, string valueName, ISet<string> roots)
    {
        try { using var key = hive.OpenSubKey(keyPath); AddIfDirectory(roots, key?.GetValue(valueName)?.ToString()); }
        catch { }
    }

    private static void AddIfDirectory(ISet<string> target, string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) target.Add(Path.GetFullPath(path));
    }

    private static string ReadVdfValue(string text, string key)
    {
        var match = Regex.Match(text, $"\\\"{Regex.Escape(key)}\\\"\\s+\\\"([^\\\"]*)\\\"", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : "";
    }

    private static string ReadJsonString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static string Normalize(string value) => Regex.Replace(value, "[^a-zA-Z0-9]", "").ToLowerInvariant();
}
