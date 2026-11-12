using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using WeighingStationAgent.Configuration;

namespace WeighingStationAgent.Weighing;

/// <summary>HMAC-SHA256 по каноническому представлению измерения.</summary>
public sealed class MeasurementSigner
{
    private readonly byte[] _key;

    public MeasurementSigner(IOptions<AgentOptions> options)
    {
        _key = Encoding.UTF8.GetBytes(options.Value.SigningKey);
    }

    public SignedMeasurement Sign(WeightSnapshot snapshot)
    {
        var operationId = Guid.NewGuid().ToString("D");
        var weight = snapshot.WeightKg.ToString("F3", CultureInfo.InvariantCulture);
        var measuredAtMs = snapshot.MeasuredAt.ToUnixTimeMilliseconds()
            .ToString(CultureInfo.InvariantCulture);

        var payload = string.Join(
            '|',
            operationId,
            snapshot.StationId,
            snapshot.Platform.ToString(CultureInfo.InvariantCulture),
            snapshot.Terminal,
            weight,
            snapshot.Stable ? "1" : "0",
            measuredAtMs,
            snapshot.Sequence.ToString(CultureInfo.InvariantCulture));

        using var hmac = new HMACSHA256(_key);
        var checksum = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();

        return new SignedMeasurement(
            operationId,
            snapshot.StationId,
            snapshot.Platform,
            snapshot.Terminal,
            snapshot.WeightKg,
            snapshot.Stable,
            snapshot.MeasuredAt,
            snapshot.Sequence,
            checksum);
    }
}
