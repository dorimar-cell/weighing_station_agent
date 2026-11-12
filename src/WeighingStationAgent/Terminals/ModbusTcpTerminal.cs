using System.Buffers.Binary;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using WeighingStationAgent.Configuration;

namespace WeighingStationAgent.Terminals;

/// <summary>
/// Modbus TCP: вес — 32-битный float в holding-регистрах 0–1.
/// Карта соответствует легаси-настройке «Оборудование ModbusTCP».
/// </summary>
public sealed class ModbusTcpTerminal : ITerminalDevice
{
    private readonly TerminalOptions _options;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private TcpClient? _client;
    private NetworkStream? _stream;
    private ushort _transactionId;
    private DateTimeOffset _lastConnectAttempt = DateTimeOffset.MinValue;

    public ModbusTcpTerminal(TerminalOptions options, ILogger logger)
    {
        _options = options;
        _logger = logger;
    }

    public string Name => $"ModbusTcp {_options.Host}:{_options.Port}";
    public bool IsConnected => _client?.Connected == true;

    public async Task<double?> ReadWeightKgAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!await EnsureConnectedAsync(cancellationToken))
            {
                return null;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(1500));

            var stream = _stream!;
            var transaction = ++_transactionId;

            var request = new byte[12];
            BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(0), transaction);
            BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(2), 0);
            BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(4), 6);
            request[6] = _options.UnitId;
            request[7] = 0x03; // read holding registers
            BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(8), _options.Register);
            BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(10), 2);

            await stream.WriteAsync(request, timeout.Token);

            var header = new byte[9];
            await stream.ReadExactlyAsync(header, timeout.Token);

            var responseTransaction = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(0));
            if (responseTransaction != transaction || header[7] != 0x03)
            {
                throw new IOException("Некорректный ответ Modbus");
            }

            var byteCount = header[8];
            if (byteCount != 4)
            {
                throw new IOException($"Неожиданная длина данных Modbus: {byteCount}");
            }

            var data = new byte[4];
            await stream.ReadExactlyAsync(data, timeout.Token);

            var raw = ((uint)data[0] << 24) | ((uint)data[1] << 16) | ((uint)data[2] << 8) | data[3];
            var value = BitConverter.Int32BitsToSingle((int)raw);

            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0)
            {
                return null;
            }

            // Легаси-терминал отдаёт значение в тоннах (tb_Weight показывает 99.999).
            // Внутренний контракт агента — килограммы; множитель настраивается.
            return Math.Round(value * _options.ModbusWeightMultiplier, 1);
        }
        catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException)
        {
            _logger.LogWarning(exception, "Modbus: ошибка чтения веса");
            ResetConnection();
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task ZeroAsync(CancellationToken cancellationToken)
    {
        // В легаси обнуление Modbus-терминала не реализовано отдельной командой.
        _logger.LogInformation("Modbus: команда обнуления не поддерживается");
        return Task.CompletedTask;
    }

    private async Task<bool> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_client?.Connected == true && _stream != null)
        {
            return true;
        }

        if (DateTimeOffset.UtcNow - _lastConnectAttempt < TimeSpan.FromSeconds(10))
        {
            return false;
        }
        _lastConnectAttempt = DateTimeOffset.UtcNow;

        ResetConnection();

        try
        {
            var client = new TcpClient { NoDelay = true };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(3000));

            await client.ConnectAsync(_options.Host, _options.Port, timeout.Token);
            _client = client;
            _stream = client.GetStream();
            _logger.LogInformation("Modbus: подключено к {Host}:{Port}", _options.Host, _options.Port);
            return true;
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException)
        {
            _logger.LogWarning("Modbus: не удалось подключиться к {Host}:{Port}: {Message}",
                _options.Host, _options.Port, exception.Message);
            ResetConnection();
            return false;
        }
    }

    private void ResetConnection()
    {
        _stream?.Dispose();
        _client?.Dispose();
        _stream = null;
        _client = null;
    }

    public ValueTask DisposeAsync()
    {
        ResetConnection();
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}
