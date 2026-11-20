using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JsonOptions>(o => o.SerializerOptions.PropertyNamingPolicy = null);
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(o =>
{
    o.Cookie.Name = ".WS01-01.Session";
    o.Cookie.HttpOnly = true;
    o.Cookie.IsEssential = true;
    o.IdleTimeout = TimeSpan.FromMinutes(30);
});

var app = builder.Build();

app.UseSession();
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Instance"] = Environment.MachineName;
    await next();
});

const string ResultKey = "RESULT";
const string StackKey = "STACK";

Stack<int> LoadStack(ISession s)
{
    var json = s.GetString(StackKey);
    var items = json is null ? new List<int>() : JsonSerializer.Deserialize<List<int>>(json)!;
    return new Stack<int>(items);
}

void SaveStack(ISession s, Stack<int> stack) =>
    s.SetString(StackKey, JsonSerializer.Serialize(stack.Reverse().ToList()));

object State(ISession s)
{
    var result = s.GetInt32(ResultKey) ?? 0;
    var stack = LoadStack(s);
    return new { RESULT = result + (stack.Count > 0 ? stack.Peek() : 0) };
}

app.MapGet("/stack", (HttpContext ctx) => Results.Json(State(ctx.Session)));

app.MapPost("/stack", (HttpContext ctx, ResultBody body) =>
{
    ctx.Session.SetInt32(ResultKey, body.RESULT);
    return Results.Json(State(ctx.Session));
});

app.MapPut("/stack", (HttpContext ctx, [Microsoft.AspNetCore.Mvc.FromQuery(Name = "ADD")] int add) =>
{
    var stack = LoadStack(ctx.Session);
    stack.Push(add);
    SaveStack(ctx.Session, stack);
    return Results.Json(State(ctx.Session));
});

app.MapDelete("/stack", (HttpContext ctx) =>
{
    var stack = LoadStack(ctx.Session);
    if (stack.Count == 0)
        return Results.Json(new { error = "stack is empty" }, statusCode: StatusCodes.Status409Conflict);
    stack.Pop();
    SaveStack(ctx.Session, stack);
    return Results.Json(State(ctx.Session));
});

app.Run();

record ResultBody(int RESULT);
