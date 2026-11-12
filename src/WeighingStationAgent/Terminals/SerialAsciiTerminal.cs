using System.Globalization;
using System.IO.Ports;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using WeighingStationAgent.Configuration;

namespace WeighingStationAgent.Terminals;

/// <summary>
/// Универсальный последовательный драйвер: отправляет команду запроса и
/// разбирает число из строки регуляркой. Для конкретных моделей (BT-009,
/// MeraVesy, Midl, CAS и т.д.) добавляются отдельные драйверы по протоколам.
/// </summary>
public sealed class SerialAsciiTerminal : ITerminalDevice
{
    private readonly TerminalOptions _options;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private SerialPort? _port;

    public SerialAsciiTerminal(TerminalOptions options, ILogger logger)
    {
        _options = options;
        _logger = logger;
    }

    public string Name => $"Serial {_options.SerialPortName} @ {_options.BaudRate}";
    public bool IsConnected => _port?.IsOpen == true;

    public async Task<double?> ReadWeightKgAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!EnsureOpen())
            {
                return null;
            }

            if (!string.IsNullOrEmpty(_options.RequestCommand))
            {
                _port!.Write(_options.RequestCommand);
            }

            var line = await Task.Run(() => _port!.ReadLine(), cancellationToken);
            var match = Regex.Match(line ?? string.Empty, _options.LineRegex, RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                return null;
            }

            var text = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
            text = text.Replace(',', '.');

            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return null;
            }

            return Math.Round(value * _options.Scale, 1);
        }
        catch (Exception exception) when (exception is TimeoutException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Serial: ошибка чтения веса");
            Close();
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ZeroAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (EnsureOpen() && !string.IsNullOrEmpty(_options.ZeroCommand))
            {
                _port!.Write(_options.ZeroCommand);
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Serial: ошибка обнуления");
            Close();
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool EnsureOpen()
    {
        if (_port?.IsOpen == true)
        {
            return true;
        }

        Close();

        try
        {
            var port = new SerialPort(
                _options.SerialPortName,
                _options.BaudRate,
                ParseParity(_options.Parity),
                _options.DataBits,
                ParseStopBits(_options.StopBits))
            {
                ReadTimeout = 1000,
                WriteTimeout = 500,
                NewLine = "\n",
            };

            port.Open();
            _port = port;
            _logger.LogInformation("Serial: порт {Port} открыт", _options.SerialPortName);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.LogWarning(exception, "Serial: не удалось открыть {Port}", _options.SerialPortName);
            Close();
            return false;
        }
    }

    private void Close()
    {
        try
        {
            if (_port?.IsOpen == true)
            {
                _port.Close();
            }
        }
        catch
        {
            // порт уже мог быть закрыт устройством
        }

        _port?.Dispose();
        _port = null;
    }

    private static Parity ParseParity(string value) =>
        Enum.TryParse<Parity>(value, true, out var parity) ? parity : Parity.None;

    private static StopBits ParseStopBits(string value) =>
        Enum.TryParse<StopBits>(value, true, out var stopBits) ? stopBits : StopBits.One;

    public ValueTask DisposeAsync()
    {
        Close();
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}
