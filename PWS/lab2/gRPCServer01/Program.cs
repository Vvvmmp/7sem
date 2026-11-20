using Microsoft.AspNetCore.Server.Kestrel.Core;
using gRPCServer01;

var builder = WebApplication.CreateBuilder(args);

var port = int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var p) ? p : 5001;
builder.WebHost.ConfigureKestrel(o => o.ListenAnyIP(port, l => l.Protocols = HttpProtocols.Http2));

builder.Services.AddGrpc();

var app = builder.Build();
app.MapGrpcService<CalculatorService>();
app.MapGet("/", () => "gRPCServer01: use a gRPC client (service calc.Calculator).");

app.Run();
