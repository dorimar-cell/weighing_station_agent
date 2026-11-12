using Microsoft.Extensions.Options;
using WeighingStationAgent.Configuration;
using WeighingStationAgent.Terminals;

namespace WeighingStationAgent.Weighing;

/// <summary>
/// Опрашивает терминал, считает стабильность и публикует снимок веса.
/// Стабильность: окно StableWindowMs, разброс не больше StableDeltaKg.
/// </summary>
public sealed class WeightMonitor : BackgroundService
{
    private readonly TerminalProvider _terminals;
    private readonly WeightBroadcaster _broadcaster;
    private readonly AgentOptions _options;
    private readonly ILogger<WeightMonitor> _logger;

    private readonly Queue<(DateTimeOffset At, double WeightKg)> _samples = new();
    private readonly object _samplesSync = new();
    private WeightSnapshot? _current;
    private long _sequence;

    public WeightMonitor(
        TerminalProvider terminals,
        WeightBroadcaster broadcaster,
        IOptions<AgentOptions> options,
        ILogger<WeightMonitor> logger)
    {
        _terminals = terminals;
        _broadcaster = broadcaster;
        _options = options.Value;
        _logger = logger;
    }

    public WeightSnapshot? Current => Volatile.Read(ref _current);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMilliseconds(Math.Max(100, _options.PollIntervalMs));
        using var timer = new PeriodicTimer(interval);

        _logger.LogInformation(
            "Опрос терминала запущен: {Terminal}, интервал {Interval} мс",
            _terminals.Device.Name,
            interval.TotalMilliseconds);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var measured = await _terminals.Device.ReadWeightKgAsync(stoppingToken);
                var now = DateTimeOffset.UtcNow;
                var connected = measured.HasValue;
                var weight = measured ?? _current?.WeightKg ?? 0;

                bool stable;
                if (connected)
                {
                    stable = UpdateStability(now, weight);
                }
                else
                {
                    lock (_samplesSync)
                    {
                        _samples.Clear();
                    }
                    stable = false;
                }

                var snapshot = new WeightSnapshot(
                    StationId: _options.StationId,
                    Platform: _options.Platform,
                    Terminal: _terminals.Device.Name,
                    WeightKg: weight,
                    Stable: stable,
                    Connected: connected,
                    MeasuredAt: now,
                    Sequence: ++_sequence);

                Volatile.Write(ref _current, snapshot);
                await _broadcaster.BroadcastAsync(snapshot, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Ошибка цикла опроса веса");
            }
        }
    }

    private bool UpdateStability(DateTimeOffset now, double weight)
    {
        lock (_samplesSync)
        {
            _samples.Enqueue((now, weight));

            var threshold = now.AddMilliseconds(-_options.StableWindowMs);
            while (_samples.Count > 0 && _samples.Peek().At < threshold)
            {
                _samples.Dequeue();
            }

            if (_samples.Count < 2 || weight < _options.MinWeightKg)
            {
                return false;
            }

            var min = double.MaxValue;
            var max = double.MinValue;
            foreach (var sample in _samples)
            {
                min = Math.Min(min, sample.WeightKg);
                max = Math.Max(max, sample.WeightKg);
            }

            return max - min <= _options.StableDeltaKg;
        }
    }
}
