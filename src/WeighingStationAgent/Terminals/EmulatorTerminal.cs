namespace WeighingStationAgent.Terminals;

/// <summary>
/// Эмулятор весов для разработки и демонстрации без оборудования.
/// Вес плавно подъезжает к заданной цели и слегка шумит.
/// </summary>
public sealed class EmulatorTerminal : ITerminalDevice
{
    private readonly object _sync = new();
    private readonly Random _random = new();
    private double _target;
    private double _current;
    private DateTimeOffset _lastRead = DateTimeOffset.UtcNow;

    public string Name => "Emulator";
    public bool IsConnected => true;

    /// <summary>Задать целевой вес (0 — разгрузить платформу).</summary>
    public void SetTarget(double weightKg)
    {
        lock (_sync)
        {
            _target = Math.Max(0, weightKg);
            _lastRead = DateTimeOffset.UtcNow;
        }
    }

    public Task<double?> ReadWeightKgAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            var seconds = Math.Max(0.05, (now - _lastRead).TotalSeconds);
            _lastRead = now;

            // Скорость «наезда» на платформу — примерно 8 т/с.
            var maxStep = 8000 * seconds;
            var diff = _target - _current;

            if (Math.Abs(diff) <= maxStep)
            {
                _current = _target;
            }
            else
            {
                _current += Math.Sign(diff) * maxStep;
            }

            _current = Math.Max(0, _current);
            var noise = (_random.NextDouble() - 0.5) * 8; // ±4 кг
            var value = Math.Max(0, _current + noise);

            return Task.FromResult<double?>(Math.Round(value, 1));
        }
    }

    public Task ZeroAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            _target = 0;
            _current = 0;
            _lastRead = DateTimeOffset.UtcNow;
        }
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
