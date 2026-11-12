using WeighingStationAgent.Api;
using WeighingStationAgent.Cameras;
using WeighingStationAgent.Configuration;
using WeighingStationAgent.Storage;
using WeighingStationAgent.Terminals;
using WeighingStationAgent.Weighing;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AgentOptions>(
    builder.Configuration.GetSection(AgentOptions.SectionName));

var agentOptions = builder.Configuration
    .GetSection(AgentOptions.SectionName)
    .Get<AgentOptions>() ?? new AgentOptions();

builder.WebHost.UseUrls(agentOptions.ListenUrl);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(
            "http://localhost:3001",
            "http://127.0.0.1:3001",
            "http://localhost:3002",
            "http://127.0.0.1:3002")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

builder.Services.AddSingleton<TerminalProvider>();
builder.Services.AddSingleton<CameraProvider>();
builder.Services.AddSingleton<WeightBroadcaster>();
builder.Services.AddSingleton<MeasurementSigner>();
builder.Services.AddSingleton<OutboxStore>();
builder.Services.AddSingleton<WeightMonitor>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<WeightMonitor>());

var app = builder.Build();

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });
app.UseCors();
app.MapAgentEndpoints();

app.Logger.LogInformation(
    "Station Agent: {Station} (platform {Platform}), API {Url}",
    agentOptions.StationName,
    agentOptions.Platform,
    agentOptions.ListenUrl);

app.Run();
