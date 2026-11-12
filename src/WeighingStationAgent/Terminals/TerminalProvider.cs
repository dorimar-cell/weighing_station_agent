using Microsoft.Extensions.Options;
using WeighingStationAgent.Configuration;

namespace WeighingStationAgent.Terminals;

/// <summary>Создаёт драйвер терминала по конфигурации.</summary>
public sealed class TerminalProvider : IAsyncDisposable
{
    public ITerminalDevice Device { get; }
    public EmulatorTerminal? Emulator { get; }

    public TerminalProvider(IOptions<AgentOptions> options, ILogger<TerminalProvider> logger)
    {
        var terminal = options.Value.Terminal;

        switch (terminal.Type.Trim().ToLowerInvariant())
        {
            case "modbustcp":
                Device = new ModbusTcpTerminal(terminal, logger);
                break;
            case "serialascii":
                Device = new SerialAsciiTerminal(terminal, logger);
                break;
            default:
                Emulator = new EmulatorTerminal();
                Device = Emulator;
                break;
        }

        logger.LogInformation("Терминал: {Type} ({Name})", terminal.Type, Device.Name);
    }

    public ValueTask DisposeAsync() => Device.DisposeAsync();
}
