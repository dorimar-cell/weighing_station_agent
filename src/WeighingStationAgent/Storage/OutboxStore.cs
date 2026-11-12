using System.Text.Json;
using Microsoft.Extensions.Options;
using WeighingStationAgent.Configuration;
using WeighingStationAgent.Weighing;

namespace WeighingStationAgent.Storage;

/// <summary>
/// Файловая очередь измерений: переживает обрыв сети и перезапуск агента.
/// Одно измерение — один JSON-файл. Сервер сможет идемпотентно принять его
/// по operationId.
/// </summary>
public sealed class OutboxStore
{
    private readonly string _directory;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public OutboxStore(IOptions<AgentOptions> options, ILogger<OutboxStore> logger)
    {
        _directory = Path.GetFullPath(options.Value.OutboxPath);
        Directory.CreateDirectory(_directory);
        logger.LogInformation("Очередь измерений: {Directory}", _directory);
    }

    public async Task<string> EnqueueAsync(SignedMeasurement measurement, CancellationToken cancellationToken)
    {
        var fileName = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{measurement.MeasuredAt:yyyyMMddHHmmssfff}_{measurement.Sequence}_{measurement.OperationId}.json");

        var path = Path.Combine(_directory, fileName);
        var tempPath = path + ".tmp";

        await File.WriteAllTextAsync(
            tempPath,
            JsonSerializer.Serialize(measurement, _json),
            cancellationToken);

        File.Move(tempPath, path, overwrite: true);
        return fileName;
    }

    public int Count()
    {
        return Directory.EnumerateFiles(_directory, "*.json").Count();
    }

    public IReadOnlyList<SignedMeasurement> ReadLast(int limit)
    {
        var files = Directory
            .EnumerateFiles(_directory, "*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(Math.Clamp(limit, 1, 200));

        var result = new List<SignedMeasurement>();
        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var measurement = JsonSerializer.Deserialize<SignedMeasurement>(json, _json);
                if (measurement != null)
                {
                    result.Add(measurement);
                }
            }
            catch (Exception)
            {
                // битый файл не должен ломать выдачу очереди
            }
        }

        return result;
    }
}
