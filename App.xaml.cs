using System.Threading;
using System.Windows;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;

namespace W1sleyEdition;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        AppPaths.EnsureCreated();
        if (e.Args.Any(arg => arg.Equals("--recover-safe", StringComparison.OrdinalIgnoreCase)))
        {
            base.OnStartup(e);
            var recovered = Task.Run(() => new PowerModeService(new ActionLogger()).RecoverSafeModeAsync()).GetAwaiter().GetResult();
            Shutdown(recovered ? 0 : 3);
            return;
        }
        if (e.Args.Any(arg => arg.Equals("--test-safe-recovery-task", StringComparison.OrdinalIgnoreCase)))
        {
            base.OnStartup(e);
            var result = Task.Run(() => new PowerModeService(new ActionLogger()).TestSafeRecoveryTaskAsync()).GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(AppPaths.LogsDirectory, "safe-recovery-task-test.txt"),
                $"{DateTimeOffset.Now:O}|{result.Success}|{result.Detail}");
            Shutdown(result.Success ? 0 : 4);
            return;
        }
        if (e.Args.Any(arg => arg.Equals("--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            base.OnStartup(e);
            try
            {
                Task.Run(SelfTestService.RunAsync).GetAwaiter().GetResult();
                Shutdown(0);
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(AppPaths.LogsDirectory, "self-test-error.txt"), ex.ToString());
                Shutdown(2);
            }
            return;
        }

        _singleInstanceMutex = new Mutex(true, "W1SLEYEdition.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("W1SLEY Edition zaten çalışıyor.", "W1SLEY Edition", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += App_DispatcherUnhandledException;
        base.OnStartup(e);
        var showSetup = e.Args.Any(arg => arg.Equals("--setup", StringComparison.OrdinalIgnoreCase)) ||
                        !File.Exists(AppPaths.SetupCompletedPath);
        if (showSetup)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var setupWindow = new SetupWindow();
            if (setupWindow.ShowDialog() != true)
            {
                Shutdown();
                return;
            }
        }

        MainWindow = new MainWindow();
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            AppPaths.EnsureCreated();
            File.AppendAllText(Path.Combine(AppPaths.LogsDirectory, "ui-errors.log"), $"[{DateTimeOffset.Now:O}] {e.Exception}\r\n\r\n");
        }
        catch { }
        MessageBox.Show("Arayüz işlemi tamamlanamadı fakat W1SLEY Edition açık tutuldu. Ayrıntı hata günlüğüne yazıldı.", "W1SLEY Edition", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
