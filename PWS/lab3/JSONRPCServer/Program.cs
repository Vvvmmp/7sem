using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using JSONRPCServer;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var jsonOptions = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

app.MapPost("/jsonrpc", async (HttpRequest request) =>
{
    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync();

    JsonNode? response = JsonRpcHandler.Handle(body);

    return response is null
        ? Results.NoContent()
        : Results.Content(response.ToJsonString(jsonOptions), "application/json");
});

app.Run();
