namespace WeighingStationAgent.Weighing;

/// <summary>Подписанное измерение: сервер сможет проверить, что вес не подменён.</summary>
public sealed record SignedMeasurement(
    string OperationId,
    string StationId,
    int Platform,
    string Terminal,
    double WeightKg,
    bool Stable,
    DateTimeOffset MeasuredAt,
    long Sequence,
    string Checksum);
