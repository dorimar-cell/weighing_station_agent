namespace WeighingStationAgent.Terminals;

/// <summary>Драйвер весового терминала. Возвращает вес в килограммах.</summary>
public interface ITerminalDevice : IAsyncDisposable
{
    string Name { get; }
    bool IsConnected { get; }

    /// <summary>Текущий вес в кг или null, если связи нет.</summary>
    Task<double?> ReadWeightKgAsync(CancellationToken cancellationToken);

    /// <summary>Обнуление/тарирование, если поддерживается.</summary>
    Task ZeroAsync(CancellationToken cancellationToken);
}
