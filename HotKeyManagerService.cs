using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace W1sleyEdition;

public sealed class HotKeyConfig
{
    public string ToggleWindowKey { get; set; } = "W";
    public string CleanRamKey { get; set; } = "R";
    public string GameBoostKey { get; set; } = "B";
    public string Modifier { get; set; } = "Alt";
}

public sealed class HotKeyManagerService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly Window _window;
    private readonly ActionLogger _logger;
    private HwndSource? _source;
    private bool _hookAttached;
    private readonly string _configPath;

    public HotKeyConfig CurrentConfig { get; private set; } = new();

    public event Action? ToggleWindowRequested;
    public event Action? CleanRamRequested;
    public event Action? GameBoostRequested;

    public HotKeyManagerService(Window window, ActionLogger logger)
    {
        _window = window;
        _logger = logger;
        _configPath = Path.Combine(AppPaths.DataDirectory, "hotkeys.json");
        LoadConfig();
    }

    public bool RegisterAll()
    {
        var handle = new WindowInteropHelper(_window).Handle;
        if (!_hookAttached)
        {
            _source = HwndSource.FromHwnd(handle);
            if (_source is null) return false;
            _source.AddHook(HwndHook);
            _hookAttached = true;
        }

        UnregisterAll();
        var registered = TryRegisterConfig(handle, CurrentConfig);
        if (!registered)
        {
            UnregisterAll();
            _logger.Log("Kısayollar", "Kısayollardan biri başka bir uygulama tarafından kullanıldığı için tüm kısayollar pasif bırakıldı.", false, null);
        }
        return registered;
    }

    public void UnregisterAll()
    {
        var handle = new WindowInteropHelper(_window).Handle;
        UnregisterHotKey(handle, 101);
        UnregisterHotKey(handle, 102);
        UnregisterHotKey(handle, 103);
    }

    public bool SaveConfig(HotKeyConfig config)
    {
        if (!ValidateConfig(config, out var error))
        {
            _logger.Log("Kısayollar", error, false, null);
            return false;
        }

        var previous = CurrentConfig;
        try
        {
            CurrentConfig = config;
            var handle = new WindowInteropHelper(_window).Handle;
            UnregisterAll();
            if (!TryRegisterConfig(handle, config))
            {
                UnregisterAll();
                CurrentConfig = previous;
                TryRegisterConfig(handle, previous);
                _logger.Log("Kısayollar", "Seçilen kısayollardan biri başka bir uygulama tarafından kullanılıyor.", false, null);
                return false;
            }

            AppPaths.EnsureCreated();
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
            _logger.Log("HotKeys", $"Kısayol tuşları güncellendi: {config.Modifier}+{config.ToggleWindowKey}, {config.Modifier}+{config.CleanRamKey}, {config.Modifier}+{config.GameBoostKey}", true, null);
            return true;
        }
        catch (Exception ex)
        {
            CurrentConfig = previous;
            _logger.Log("HotKeys", $"Kısayol kaydetme hatası: {ex.Message}", false, null);
            return false;
        }
    }

    private bool TryRegisterConfig(IntPtr handle, HotKeyConfig config)
    {
        var modifier = GetModifierFlag(config.Modifier);
        var first = RegisterKey(handle, 101, modifier, config.ToggleWindowKey);
        var second = RegisterKey(handle, 102, modifier, config.CleanRamKey);
        var third = RegisterKey(handle, 103, modifier, config.GameBoostKey);
        return first && second && third;
    }

    private static bool ValidateConfig(HotKeyConfig config, out string error)
    {
        var keys = new[] { config.ToggleWindowKey, config.CleanRamKey, config.GameBoostKey };
        if (keys.Any(key => string.IsNullOrWhiteSpace(key) || !Enum.TryParse<Key>(key, true, out _)))
        {
            error = "Kısayol alanlarında geçerli birer tuş kullanılmalı.";
            return false;
        }
        if (keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != keys.Length)
        {
            error = "Üç işlev için birbirinden farklı kısayol tuşları seçilmeli.";
            return false;
        }
        error = "";
        return true;
    }

    private void LoadConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                var loaded = JsonSerializer.Deserialize<HotKeyConfig>(json);
                if (loaded != null) CurrentConfig = loaded;
            }
        }
        catch { }
    }

    private bool RegisterKey(IntPtr hWnd, int id, uint mod, string keyStr)
    {
        if (Enum.TryParse<Key>(keyStr, true, out var key))
        {
            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            return RegisterHotKey(hWnd, id, mod, vk);
        }
        return false;
    }

    private uint GetModifierFlag(string mod)
    {
        return mod.ToLower() switch
        {
            "ctrl" or "control" => MOD_CONTROL,
            "shift" => MOD_SHIFT,
            "win" => MOD_WIN,
            _ => MOD_ALT
        };
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            switch (id)
            {
                case 101:
                    ToggleWindowRequested?.Invoke();
                    handled = true;
                    break;
                case 102:
                    CleanRamRequested?.Invoke();
                    handled = true;
                    break;
                case 103:
                    GameBoostRequested?.Invoke();
                    handled = true;
                    break;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        if (_source != null && _hookAttached)
        {
            _source.RemoveHook(HwndHook);
            _hookAttached = false;
        }
        _source = null;
    }
}
