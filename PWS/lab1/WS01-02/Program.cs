using Microsoft.AspNetCore.Http.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JsonOptions>(o => o.SerializerOptions.PropertyNamingPolicy = null);

var app = builder.Build();

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Instance"] = Environment.MachineName;
    await next();
});

const string ResultCookie = "RESULT";
const string StackCookie = "STACK";

var cookieOptions = new CookieOptions { HttpOnly = true, IsEssential = true, Path = "/" };

int LoadResult(HttpRequest req) =>
    int.TryParse(req.Cookies[ResultCookie], out var r) ? r : 0;

List<int> LoadStack(HttpRequest req)
{
    var raw = req.Cookies[StackCookie];
    if (string.IsNullOrEmpty(raw)) return new List<int>();
    return raw.Split('.').Select(int.Parse).ToList();
}

void SaveStack(HttpResponse res, List<int> stack)
{
    if (stack.Count == 0) res.Cookies.Delete(StackCookie);
    else res.Cookies.Append(StackCookie, string.Join('.', stack), cookieOptions);
}

object State(int result, List<int> stack) =>
    new { RESULT = result + (stack.Count > 0 ? stack[^1] : 0) };

app.MapGet("/stack", (HttpContext ctx) =>
    Results.Json(State(LoadResult(ctx.Request), LoadStack(ctx.Request))));

app.MapPost("/stack", (HttpContext ctx, ResultBody body) =>
{
    ctx.Response.Cookies.Append(ResultCookie, body.RESULT.ToString(), cookieOptions);
    return Results.Json(State(body.RESULT, LoadStack(ctx.Request)));
});

app.MapPut("/stack", (HttpContext ctx, [Microsoft.AspNetCore.Mvc.FromQuery(Name = "ADD")] int add) =>
{
    var stack = LoadStack(ctx.Request);
    stack.Add(add);
    SaveStack(ctx.Response, stack);
    return Results.Json(State(LoadResult(ctx.Request), stack));
});

app.MapDelete("/stack", (HttpContext ctx) =>
{
    var stack = LoadStack(ctx.Request);
    if (stack.Count == 0)
        return Results.Json(new { error = "stack is empty" }, statusCode: StatusCodes.Status409Conflict);
    stack.RemoveAt(stack.Count - 1);
    SaveStack(ctx.Response, stack);
    return Results.Json(State(LoadResult(ctx.Request), stack));
});

app.Run();

record ResultBody(int RESULT);
