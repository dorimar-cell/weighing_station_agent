using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

namespace WeighingStationAgent.Weighing;

/// <summary>Рассылает поток веса всем подключённым браузерам.</summary>
public sealed class WeightBroadcaster
{
    private sealed class Client
    {
        public required WebSocket Socket { get; init; }
        public SemaphoreSlim SendLock { get; } = new(1, 1);
    }

    private readonly ConcurrentDictionary<Guid, Client> _clients = new();
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public int ClientCount => _clients.Count;

    public Guid Add(WebSocket socket)
    {
        var id = Guid.NewGuid();
        _clients[id] = new Client { Socket = socket };
        return id;
    }

    public void Remove(Guid id)
    {
        _clients.TryRemove(id, out _);
    }

    public async Task BroadcastAsync(WeightSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (_clients.IsEmpty)
        {
            return;
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(snapshot, _json);

        foreach (var (id, client) in _clients)
        {
            if (client.Socket.State != WebSocketState.Open)
            {
                Remove(id);
                continue;
            }

            await client.SendLock.WaitAsync(cancellationToken);
            try
            {
                await client.Socket.SendAsync(
                    payload,
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                Remove(id);
            }
            finally
            {
                client.SendLock.Release();
            }
        }
    }
}
