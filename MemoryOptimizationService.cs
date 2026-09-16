using System.Diagnostics;
using System.Runtime.InteropServices;

namespace W1sleyEdition;

public sealed class MemoryOptimizationService
{
    private readonly ActionLogger _logger;

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("ntdll.dll")]
    private static extern int NtSetSystemInformation(int systemInformationClass, ref int systemInformation, int systemInformationLength);

    public MemoryOptimizationService(ActionLogger logger)
    {
        _logger = logger;
    }

    public async Task<long> RunSafeMaintenanceAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                using var current = Process.GetCurrentProcess();
                var before = current.WorkingSet64;

                // Güvenli seçenek yalnızca W1SLEY'nin kendi yönetilen belleğini toplar.
                // Diğer uygulamaların çalışma kümelerine ve Windows standby önbelleğine dokunmaz.
                GC.Collect(2, GCCollectionMode.Optimized, blocking: true, compacting: false);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Optimized, blocking: true, compacting: false);

                current.Refresh();
                var freedMb = Math.Max(0, before - current.WorkingSet64) / (1024 * 1024);
                _logger.Log("Güvenli RAM bakımı", $"W1SLEY uygulama belleği düzenlendi; sistem önbelleğine ve diğer süreçlere dokunulmadı. Ölçülen fark: {freedMb} MB", true, freedMb);
                return freedMb;
            }
            catch (Exception ex)
            {
                _logger.Log("Güvenli RAM bakımı", $"RAM bakım hatası: {ex.Message}", false, null);
                throw;
            }
        });
    }

    public async Task<long> ForcePurgeWorkingSetsAndStandbyListAsync()
    {
        return await Task.Run(() =>
        {
            long totalFreedBytes = 0;
            try
            {
                foreach (var process in Process.GetProcesses())
                {
                    try
                    {
                        if (!process.HasExited && process.Id > 4)
                        {
                            var before = process.WorkingSet64;
                            if (EmptyWorkingSet(process.Handle))
                            {
                                process.Refresh();
                                totalFreedBytes += Math.Max(0, before - process.WorkingSet64);
                            }
                        }
                    }
                    catch { }
                    finally { process.Dispose(); }
                }

                // SystemMemoryListInformation (80), MemoryPurgeStandbyList (4).
                var purgeCommand = 4;
                var purgeResult = NtSetSystemInformation(80, ref purgeCommand, sizeof(int));
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

                var freedMb = totalFreedBytes / (1024 * 1024);
                var standbyDetail = purgeResult == 0 ? "standby listesi temizlendi" : $"standby temizliği reddedildi (NTSTATUS 0x{purgeResult:X8})";
                _logger.Log("Zorla RAM temizliği", $"Süreç çalışma kümeleri boşaltıldı; {standbyDetail}. Ölçülen çalışma kümesi farkı: {freedMb} MB", true, freedMb);
                return freedMb;
            }
            catch (Exception ex)
            {
                _logger.Log("Zorla RAM temizliği", $"RAM temizlik hatası: {ex.Message}", false, null);
                throw;
            }
        });
    }
}
