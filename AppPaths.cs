namespace W1sleyEdition;

public static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "W1SLEY Edition");

    public static string LogsDirectory { get; } = Path.Combine(DataDirectory, "Logs");
    public static string GameIconsDirectory { get; } = Path.Combine(DataDirectory, "Game Icons");
    public static string ToolsDirectory { get; } = Path.Combine(DataDirectory, "Tools");
    public static string ActionLogPath { get; } = Path.Combine(LogsDirectory, "actions.jsonl");
    public static string ProfilesPath { get; } = Path.Combine(DataDirectory, "profiles.json");
    public static string StartupBackupPath { get; } = Path.Combine(DataDirectory, "startup-backup.json");
    public static string RecoveryScriptPath { get; } = Path.Combine(DataDirectory, "recover-safe.cmd");
    public static string RecoveryTaskXmlPath { get; } = Path.Combine(DataDirectory, "recover-safe-task.xml");
    public static string DpiDnsBackupPath { get; } = Path.Combine(DataDirectory, "dpi-dns-backup.json");
    public static string WireSockDnsBackupPath { get; } = Path.Combine(DataDirectory, "wiresock-dns-backup.json");
    public static string CrashDetectorStatePath { get; } = Path.Combine(DataDirectory, "crash-detector-state.json");
    public static string SetupCompletedPath { get; } = Path.Combine(DataDirectory, "setup-v1.complete");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(GameIconsDirectory);
        Directory.CreateDirectory(ToolsDirectory);
    }
}
