using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;

namespace W1sleyEdition;

public sealed class SystemMonitorService
{
    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;
    private DateTime _lastGpuQuery = DateTime.MinValue;
    private GpuData _gpu = new("Algılanmadı", null, null, null, null, null, null, null);

    public async Task<SystemSnapshot> CaptureAsync()
    {
        var cpu = ReadCpuUsage();
        var memory = ReadMemory();
        var mhzTask = Task.Run(ReadCpuMhz);
        if (DateTime.UtcNow - _lastGpuQuery > TimeSpan.FromSeconds(5))
        {
            _lastGpuQuery = DateTime.UtcNow;
            _gpu = await ReadNvidiaGpuAsync();
        }
        var mhz = await mhzTask;

        return new SystemSnapshot
        {
            CpuPercent = cpu,
            CpuMhz = mhz,
            CpuTemperature = null,
            CpuTemperatureSource = "CPU sensörü firmware tarafından paylaşılmıyor",
            MemoryPercent = memory.Percent,
            UsedMemoryMb = memory.UsedMb,
            TotalMemoryMb = memory.TotalMb,
            AvailableMemoryMb = memory.AvailableMb,
            GpuName = _gpu.Name,
            GpuPercent = _gpu.Usage,
            GpuTemperature = _gpu.Temp,
            GpuMemoryUsedMb = _gpu.Used,
            GpuMemoryTotalMb = _gpu.Total,
            GpuCoreClockMhz = _gpu.CoreClock,
            GpuMemoryClockMhz = _gpu.MemoryClock,
            GpuPowerWatts = _gpu.PowerWatts
        };
    }

    public static (double Percent, long UsedMb, long TotalMb, long AvailableMb) ReadMemory()
    {
        var status = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(status)) return (0, 0, 0, 0);
        var total = (long)(status.TotalPhys / 1024 / 1024);
        var available = (long)(status.AvailPhys / 1024 / 1024);
        return (status.MemoryLoad, total - available, total, available);
    }

    private double ReadCpuUsage()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        var idleValue = idle.ToUInt64();
        var kernelValue = kernel.ToUInt64();
        var userValue = user.ToUInt64();
        if (_previousKernel == 0)
        {
            _previousIdle = idleValue;
            _previousKernel = kernelValue;
            _previousUser = userValue;
            return 0;
        }

        var idleDelta = idleValue - _previousIdle;
        var kernelDelta = kernelValue - _previousKernel;
        var userDelta = userValue - _previousUser;
        var total = kernelDelta + userDelta;
        _previousIdle = idleValue;
        _previousKernel = kernelValue;
        _previousUser = userValue;
        return total == 0 ? 0 : Math.Clamp((1d - idleDelta / (double)total) * 100d, 0, 100);
    }

    private static int ReadCpuMhz()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\cimv2",
                "SELECT ProcessorFrequency, PercentProcessorPerformance FROM Win32_PerfFormattedData_Counters_ProcessorInformation WHERE Name='_Total'");
            using var results = searcher.Get();
            var sample = results.Cast<ManagementObject>().FirstOrDefault();
            if (sample is not null)
            {
                var baseMhz = Convert.ToDouble(sample["ProcessorFrequency"] ?? 0);
                var performancePercent = Convert.ToDouble(sample["PercentProcessorPerformance"] ?? 0);
                var estimatedMhz = baseMhz * performancePercent / 100d;
                if (estimatedMhz is >= 400 and <= 5000) return (int)Math.Round(estimatedMhz);
            }
        }
        catch { }

        return ReadCpuMhzFromPowerInformation();
    }

    private static int ReadCpuMhzFromPowerInformation()
    {
        var count = Environment.ProcessorCount;
        var size = Marshal.SizeOf<ProcessorPowerInformation>();
        var buffer = Marshal.AllocHGlobal(size * count);
        try
        {
            var status = CallNtPowerInformation(11, IntPtr.Zero, 0, buffer, (uint)(size * count));
            if (status != 0) return 0;
            var maximum = 0u;
            for (var index = 0; index < count; index++)
            {
                var item = Marshal.PtrToStructure<ProcessorPowerInformation>(buffer + index * size);
                maximum = Math.Max(maximum, item.CurrentMhz);
            }
            return (int)maximum;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static async Task<GpuData> ReadNvidiaGpuAsync()
    {
        try
        {
            var result = await ProcessRunner.RunAsync("nvidia-smi.exe", "--query-gpu=name,utilization.gpu,temperature.gpu,memory.used,memory.total,clocks.current.graphics,clocks.current.memory,power.draw --format=csv,noheader,nounits");
            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output)) return new("Algılanmadı", null, null, null, null, null, null, null);
            var values = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].Split(',').Select(value => value.Trim()).ToArray();
            return values.Length >= 8
                ? new(values[0], ParseInt(values[1]), ParseInt(values[2]), ParseLong(values[3]), ParseLong(values[4]), ParseInt(values[5]), ParseInt(values[6]), ParseDouble(values[7]))
                : new(values[0], null, null, null, null, null, null, null);
        }
        catch { return new("Algılanmadı", null, null, null, null, null, null, null); }
    }

    private static int? ParseInt(string value) => int.TryParse(value, out var parsed) ? parsed : null;
    private static long? ParseLong(string value) => long.TryParse(value, out var parsed) ? parsed : null;
    private static double? ParseDouble(string value) => double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private sealed record GpuData(string Name, int? Usage, int? Temp, long? Used, long? Total, int? CoreClock, int? MemoryClock, double? PowerWatts);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
        public readonly ulong ToUInt64() => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessorPowerInformation
    {
        public uint Number;
        public uint MaxMhz;
        public uint CurrentMhz;
        public uint MhzLimit;
        public uint MaxIdleState;
        public uint CurrentIdleState;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint CallNtPowerInformation(int informationLevel, IntPtr inputBuffer, uint inputBufferSize, IntPtr outputBuffer, uint outputBufferSize);
}
