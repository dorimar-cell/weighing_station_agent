namespace WeighingStationAgent.Weighing;

/// <summary>Снимок веса, который агент отдаёт в UI и подписывает при сохранении.</summary>
public sealed record WeightSnapshot(
    string StationId,
    int Platform,
    string Terminal,
    double WeightKg,
    bool Stable,
    bool Connected,
    DateTimeOffset MeasuredAt,
    long Sequence);
