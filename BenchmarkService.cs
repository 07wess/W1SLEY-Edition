using System.Diagnostics;

namespace W1sleyEdition;

public sealed class BenchmarkService(ActionLogger logger)
{
    private readonly ActionLogger _logger = logger;

    public async Task<(double Score, int Seconds, int Workers)> RunAsync(int seconds, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        seconds = Math.Clamp(seconds, 10, 60);
        var workers = Math.Max(1, Environment.ProcessorCount / (seconds > 20 ? 3 : 2));
        long iterations = 0;
        var start = Stopwatch.GetTimestamp();
        var endAt = DateTime.UtcNow.AddSeconds(seconds);
        var tasks = Enumerable.Range(0, workers).Select(worker => Task.Run(() =>
        {
            var localIterations = 0L;
            var value = 0.123456789 + worker;
            while (DateTime.UtcNow < endAt && !cancellationToken.IsCancellationRequested)
            {
                for (var index = 1; index <= 5000; index++)
                {
                    value = Math.Sqrt(value * value + index) / (index + 1d) + Math.Sin(value + index);
                }
                localIterations += 5000;
            }
            Interlocked.Add(ref iterations, localIterations);
        }, cancellationToken)).ToArray();

        while (!Task.WhenAll(tasks).IsCompleted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var elapsed = Stopwatch.GetElapsedTime(start).TotalSeconds;
            progress?.Report(Math.Clamp(elapsed / seconds * 100, 0, 100));
            await Task.Delay(250, cancellationToken);
        }
        await Task.WhenAll(tasks);
        progress?.Report(100);
        var elapsedSeconds = Stopwatch.GetElapsedTime(start).TotalSeconds;
        var score = iterations / Math.Max(0.1, elapsedSeconds) / 1000d;
        await _logger.LogAsync(new ActionLogEntry
        {
            Action = seconds > 20 ? "Uzun Benchmark" : "Kısa Benchmark",
            Detail = $"{score:N0} K işlem/sn, {workers} iş parçacığı, {seconds} sn; güç modu değiştirilmedi",
            Success = true
        });
        return (score, seconds, workers);
    }
}
