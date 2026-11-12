using Microsoft.Extensions.Options;
using WeighingStationAgent.Configuration;

namespace WeighingStationAgent.Cameras;

/// <summary>Выбирает драйвер камеры. Пока поддерживается только эмулятор.</summary>
public sealed class CameraProvider
{
    public EmulatorCamera Emulator { get; }
    public string Type { get; }

    public CameraProvider(IOptions<AgentOptions> options, ILogger<CameraProvider> logger)
    {
        Type = options.Value.Camera.Type;
        Emulator = new EmulatorCamera(options);
        logger.LogInformation("Камера: {Type} ({Width}×{Height})",
            Type,
            options.Value.Camera.Width,
            options.Value.Camera.Height);
    }

    public Task<CameraFrame> CaptureAsync(
        string cameraId,
        double weightKg,
        string stationName,
        CancellationToken cancellationToken)
    {
        if (cameraId is "1" or "emulator" or "default")
        {
            return Emulator.CaptureAsync(weightKg, stationName, cancellationToken);
        }

        throw new InvalidOperationException($"Камера {cameraId} не настроена");
    }
}
