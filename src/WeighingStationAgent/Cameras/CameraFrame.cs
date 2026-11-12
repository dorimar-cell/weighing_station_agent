namespace WeighingStationAgent.Cameras;

/// <summary>Кадр с камеры.</summary>
public sealed record CameraFrame(
    string FileName,
    string ContentType,
    byte[] Bytes,
    DateTimeOffset CapturedAt);
