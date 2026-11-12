namespace WeighingStationAgent.Configuration;

/// <summary>Настройки станции и оборудования (appsettings.json → Agent).</summary>
public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    public string StationId { get; set; } = Guid.Empty.ToString();
    public string StationName { get; set; } = "Весовая";
    public int Platform { get; set; } = 1;

    /// <summary>Локальный адрес API/WebSocket. Наружу не публикуется.</summary>
    public string ListenUrl { get; set; } = "http://127.0.0.1:8095";

    public string StationToken { get; set; } = "dev-token";
    public string SigningKey { get; set; } = "dev-signing-key-change-me";

    public int PollIntervalMs { get; set; } = 500;
    public int StableWindowMs { get; set; } = 2000;
    public double StableDeltaKg { get; set; } = 20;
    public double MinWeightKg { get; set; } = 50;

    public string OutboxPath { get; set; } = "data/outbox";

    public TerminalOptions Terminal { get; set; } = new();
}

public sealed class TerminalOptions
{
    /// <summary>Emulator | ModbusTcp | SerialAscii.</summary>
    public string Type { get; set; } = "Emulator";

    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 502;
    public byte UnitId { get; set; } = 1;
    public ushort Register { get; set; }
    /// <summary>Множитель Modbus: легаси-терминал отдаёт тонны, умножаем на 1000.</summary>
    public double ModbusWeightMultiplier { get; set; } = 1000;

    public string SerialPortName { get; set; } = "COM1";
    public int BaudRate { get; set; } = 9600;
    public string Parity { get; set; } = "None";
    public int DataBits { get; set; } = 8;
    public string StopBits { get; set; } = "One";

    /// <summary>Команда запроса веса для SerialAscii (например, "W1gX").</summary>
    public string RequestCommand { get; set; } = "";
    /// <summary>Команда обнуления для SerialAscii.</summary>
    public string ZeroCommand { get; set; } = "Z\r";
    public string LineRegex { get; set; } = @"([-+]?\d+(?:[.,]\d+)?)";
    public double Scale { get; set; } = 1;
}
