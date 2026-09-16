using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using WpfMessageBox = System.Windows.MessageBox;

namespace W1sleyEdition;

public partial class SetupWindow : Window
{
    private readonly SystemCompatibilityService _scanner = new();
    private readonly PowerModeService _powerModes = new(new ActionLogger());
    private PowerMode _selectedMode = PowerMode.StablePerformance;
    private bool _scanCompleted;

    public SetupWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RunScanAsync();
        UpdateModeSelection();
    }

    private async Task RunScanAsync()
    {
        ScanAgainButton.IsEnabled = false;
        CompleteButton.IsEnabled = false;
        ScanProgress.Visibility = Visibility.Visible;
        ScanTitleText.Text = "Donanım ve Windows denetleniyor…";
        SetupStatusText.Text = "Salt okunur sistem taraması çalışıyor…";
        try
        {
            var items = await _scanner.ScanAsync();
            ScanItemsControl.ItemsSource = items;
            _scanCompleted = true;
            ScanTitleText.Text = $"{items.Count} sistem kontrolü tamamlandı";
            SetupStatusText.Text = items.Any(item => item.State is "Uyarı" or "İncele")
                ? "Kurulum hazır • sarı kayıtları uygulama içinde gözden geçirebilirsin"
                : "Kurulum hazır • temel kontroller başarılı";
        }
        catch (Exception ex)
        {
            _scanCompleted = true;
            ScanItemsControl.ItemsSource = new[]
            {
                new SetupScanItem { Category = "TARAMA", Title = "Tarama kısmen tamamlandı", Detail = ex.Message, State = "Uyarı", Accent = "#F2AE5D" }
            };
            ScanTitleText.Text = "Sistem taraması kısmen tamamlandı";
            SetupStatusText.Text = "Kuruluma devam edebilirsin; ayrıntılı kontroller ana panelde bulunur";
        }
        finally
        {
            ScanProgress.Visibility = Visibility.Collapsed;
            ScanAgainButton.IsEnabled = true;
            CompleteButton.IsEnabled = _scanCompleted;
        }
    }

    private async void ScanAgainButton_Click(object sender, RoutedEventArgs e) => await RunScanAsync();
    private void AggressiveSetupButton_Click(object sender, RoutedEventArgs e) => SelectMode(PowerMode.AggressivePerformance);
    private void FullSetupButton_Click(object sender, RoutedEventArgs e) => SelectMode(PowerMode.FullPerformance);
    private void StableSetupButton_Click(object sender, RoutedEventArgs e) => SelectMode(PowerMode.StablePerformance);

    private void SelectMode(PowerMode mode)
    {
        _selectedMode = mode;
        UpdateModeSelection();
    }

    private void UpdateModeSelection()
    {
        if (AggressiveSetupButton is null) return;
        var normal = new SolidColorBrush(MediaColor.FromRgb(27, 30, 37));
        var border = new SolidColorBrush(MediaColor.FromRgb(53, 57, 68));
        var selected = (SolidColorBrush)FindResource("AccentBrush");
        var selectedBorder = (SolidColorBrush)FindResource("AccentBrightBrush");
        foreach (var option in new[]
        {
            (Button: AggressiveSetupButton, Mode: PowerMode.AggressivePerformance),
            (Button: FullSetupButton, Mode: PowerMode.FullPerformance),
            (Button: StableSetupButton, Mode: PowerMode.StablePerformance)
        })
        {
            option.Button.Background = option.Mode == _selectedMode ? selected : normal;
            option.Button.BorderBrush = option.Mode == _selectedMode ? selectedBorder : border;
        }
        SelectedModeDescriptionText.Text = PowerModeService.Description(_selectedMode);
    }

    private async void CompleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_scanCompleted) return;
        if (PowerModeService.RequiresWarning(_selectedMode))
        {
            var answer = WpfMessageBox.Show(
                PowerModeService.Description(_selectedMode) + "\n\nBu mod yine de başlangıç modu olarak uygulansın mı?",
                "Agresif Performans Onayı", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
        }

        CompleteButton.IsEnabled = false;
        SetupStatusText.Text = $"{PowerModeService.Label(_selectedMode)} uygulanıyor…";
        try
        {
            await _powerModes.SetModeAsync(_selectedMode);
        }
        catch (Exception ex)
        {
            try
            {
                AppPaths.EnsureCreated();
                File.AppendAllText(Path.Combine(AppPaths.LogsDirectory, "setup.log"),
                    $"[{DateTimeOffset.Now:O}] Güç modu başlangıçta uygulanamadı (donanım/ilke kısıtlaması): {ex.Message}\r\n");
            }
            catch { }
        }

        try
        {
            var marker = new { completedAt = DateTimeOffset.Now, version = 1, selectedMode = _selectedMode.ToString() };
            File.WriteAllText(AppPaths.SetupCompletedPath, JsonSerializer.Serialize(marker));
        }
        catch { }

        SetupStatusText.Text = "Kurulum tamamlandı! W1SLEY Edition açılıyor…";
        await Task.Delay(250);
        DialogResult = true;
    }

    private void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var marker = new { completedAt = DateTimeOffset.Now, version = 1, selectedMode = PowerMode.StablePerformance.ToString() };
            File.WriteAllText(AppPaths.SetupCompletedPath, JsonSerializer.Serialize(marker));
        }
        catch { }
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
