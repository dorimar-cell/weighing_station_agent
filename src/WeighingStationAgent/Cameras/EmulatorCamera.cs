using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using Microsoft.Extensions.Options;
using WeighingStationAgent.Configuration;

namespace WeighingStationAgent.Cameras;

/// <summary>
/// Эмулятор камеры: рисует кадр 1280×720 с весом, станцией и временем.
/// Позже заменяется RTSP/Hikvision-драйвером без изменения API.
/// </summary>
public sealed class EmulatorCamera
{
    private readonly CameraOptions _options;
    private readonly Random _random = new();

    public EmulatorCamera(IOptions<AgentOptions> options)
    {
        _options = options.Value.Camera;
    }

    public Task<CameraFrame> CaptureAsync(
        double weightKg,
        string stationName,
        CancellationToken cancellationToken)
    {
        var width = Math.Clamp(_options.Width, 320, 3840);
        var height = Math.Clamp(_options.Height, 240, 2160);

        using var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);

        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(Color.FromArgb(24, 28, 36));

        using var accent = new SolidBrush(Color.FromArgb(64, 156, 255));
        graphics.FillRectangle(accent, 0, 0, width, 8);

        using var titleFont = new Font("Arial", 28, FontStyle.Bold, GraphicsUnit.Pixel);
        using var weightFont = new Font("Consolas", 96, FontStyle.Bold, GraphicsUnit.Pixel);
        using var smallFont = new Font("Arial", 20, FontStyle.Regular, GraphicsUnit.Pixel);
        using var white = new SolidBrush(Color.White);
        using var muted = new SolidBrush(Color.FromArgb(160, 170, 185));

        var capturedAt = DateTimeOffset.Now;
        var weight = (weightKg / 1000).ToString("0.000", CultureInfo.InvariantCulture);

        graphics.DrawString(stationName, titleFont, white, 32, 32);
        graphics.DrawString("EMULATOR CAMERA", smallFont, accent, width - 260, 40);
        graphics.DrawString($"{weight} т", weightFont, white, 32, height / 2 - 90);

        graphics.DrawString(
            capturedAt.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture),
            smallFont,
            muted,
            32,
            height - 60);

        graphics.DrawString(
            "кадр сгенерирован Station Agent",
            smallFont,
            muted,
            width - 420,
            height - 60);

        // Лёгкий шум, чтобы кадры отличались.
        for (var index = 0; index < 40; index++)
        {
            var x = _random.Next(width);
            var y = _random.Next(height);
            using var noise = new SolidBrush(Color.FromArgb(18, 255, 255, 255));
            graphics.FillRectangle(noise, x, y, 2, 2);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Jpeg);

        var fileName = $"camera_{capturedAt:yyyyMMdd_HHmmss_fff}.jpg";
        return Task.FromResult(new CameraFrame(fileName, "image/jpeg", stream.ToArray(), capturedAt));
    }
}
