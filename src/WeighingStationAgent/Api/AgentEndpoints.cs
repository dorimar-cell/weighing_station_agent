using System.Net.WebSockets;
using Microsoft.Extensions.Options;
using WeighingStationAgent.Configuration;
using WeighingStationAgent.Storage;
using WeighingStationAgent.Terminals;
using WeighingStationAgent.Weighing;

namespace WeighingStationAgent.Api;

public sealed record SimulateTruckRequest(double WeightKg);

public static class AgentEndpoints
{
    public static void MapAgentEndpoints(this WebApplication app)
    {
        app.MapGet("/health", (
            WeightMonitor monitor,
            OutboxStore outbox,
            TerminalProvider terminals,
            WeightBroadcaster broadcaster,
            IOptions<AgentOptions> options) =>
        {
            var current = monitor.Current;
            return Results.Ok(new
            {
                status = "ok",
                service = "weighing-station-agent",
                stationId = options.Value.StationId,
                stationName = options.Value.StationName,
                platform = options.Value.Platform,
                terminal = terminals.Device.Name,
                terminalType = options.Value.Terminal.Type,
                connected = current?.Connected ?? false,
                weightKg = current?.WeightKg ?? 0,
                stable = current?.Stable ?? false,
                measuredAt = current?.MeasuredAt,
                pendingOutbox = outbox.Count(),
                wsClients = broadcaster.ClientCount,
            });
        });

        app.MapGet("/api/weight", (WeightMonitor monitor) =>
            monitor.Current is { } snapshot
                ? Results.Ok(snapshot)
                : Results.NoContent());

        app.MapGet("/api/measurement", (WeightMonitor monitor, MeasurementSigner signer) =>
        {
            if (monitor.Current is not { } snapshot)
            {
                return Results.Json(
                    new { error = "Нет данных от терминала" },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Ok(signer.Sign(snapshot));
        });

        app.MapPost("/api/measurement/capture", async (
            HttpContext context,
            WeightMonitor monitor,
            MeasurementSigner signer,
            OutboxStore outbox,
            IOptions<AgentOptions> options,
            CancellationToken cancellationToken) =>
        {
            if (!IsAuthorized(context, options.Value))
            {
                return Unauthorized();
            }

            if (monitor.Current is not { } snapshot)
            {
                return Results.Json(
                    new { error = "Нет данных от терминала" },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            if (!snapshot.Stable)
            {
                return Results.Json(
                    new { error = "Вес не стабилен", snapshot.WeightKg },
                    statusCode: StatusCodes.Status409Conflict);
            }

            var measurement = signer.Sign(snapshot);
            var file = await outbox.EnqueueAsync(measurement, cancellationToken);

            return Results.Ok(new { file, measurement });
        });

        app.MapPost("/api/terminal/zero", async (
            HttpContext context,
            TerminalProvider terminals,
            IOptions<AgentOptions> options,
            CancellationToken cancellationToken) =>
        {
            if (!IsAuthorized(context, options.Value))
            {
                return Unauthorized();
            }

            await terminals.Device.ZeroAsync(cancellationToken);
            return Results.Ok(new { status = "zeroed", terminal = terminals.Device.Name });
        });

        app.MapPost("/api/simulate/truck", (
            SimulateTruckRequest request,
            HttpContext context,
            TerminalProvider terminals,
            IOptions<AgentOptions> options) =>
        {
            if (!IsAuthorized(context, options.Value))
            {
                return Unauthorized();
            }

            if (terminals.Emulator is not { } emulator)
            {
                return Results.BadRequest(new { error = "Эмулятор не активен" });
            }

            emulator.SetTarget(request.WeightKg);
            return Results.Ok(new { status = "target-set", request.WeightKg });
        });

        app.MapPost("/api/simulate/clear", (
            HttpContext context,
            TerminalProvider terminals,
            IOptions<AgentOptions> options) =>
        {
            if (!IsAuthorized(context, options.Value))
            {
                return Unauthorized();
            }

            if (terminals.Emulator is not { } emulator)
            {
                return Results.BadRequest(new { error = "Эмулятор не активен" });
            }

            emulator.SetTarget(0);
            return Results.Ok(new { status = "cleared" });
        });

        app.MapGet("/api/outbox", (OutboxStore outbox) => Results.Ok(new
        {
            pending = outbox.Count(),
            items = outbox.ReadLast(20),
        }));

        app.MapPost("/api/outbox/flush", (
            HttpContext context,
            OutboxStore outbox,
            IOptions<AgentOptions> options) =>
        {
            if (!IsAuthorized(context, options.Value))
            {
                return Unauthorized();
            }

            return Results.Ok(new
            {
                flushed = 0,
                pending = outbox.Count(),
                note = "Отправка на сервер появится после подключения go_weighing_service",
            });
        });

        app.MapGet("/api/config", (IOptions<AgentOptions> options) => Results.Ok(new
        {
            options.Value.StationId,
            options.Value.StationName,
            options.Value.Platform,
            terminal = new
            {
                options.Value.Terminal.Type,
                options.Value.Terminal.Host,
                options.Value.Terminal.Port,
                options.Value.Terminal.SerialPortName,
                options.Value.Terminal.BaudRate,
            },
            options.Value.PollIntervalMs,
            options.Value.StableWindowMs,
            options.Value.StableDeltaKg,
            options.Value.MinWeightKg,
        }));

        app.Map("/ws/weight", async (
            HttpContext context,
            WeightBroadcaster broadcaster,
            WeightMonitor monitor) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var clientId = broadcaster.Add(socket);

            try
            {
                if (monitor.Current is { } snapshot)
                {
                    var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                        snapshot,
                        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

                    await socket.SendAsync(
                        payload,
                        WebSocketMessageType.Text,
                        endOfMessage: true,
                        context.RequestAborted);
                }

                var buffer = new byte[1024];
                while (socket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
                {
                    var result = await socket.ReceiveAsync(buffer, context.RequestAborted);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // клиент отключился
            }
            finally
            {
                broadcaster.Remove(clientId);
                if (socket.State == WebSocketState.Open)
                {
                    await socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "closed",
                        CancellationToken.None);
                }
            }
        });
    }

    private static bool IsAuthorized(HttpContext context, AgentOptions options)
    {
        return context.Request.Headers.TryGetValue("X-Station-Token", out var token)
            && token == options.StationToken;
    }

    private static IResult Unauthorized() =>
        Results.Json(
            new { error = "Неверный станционный токен" },
            statusCode: StatusCodes.Status401Unauthorized);
}
