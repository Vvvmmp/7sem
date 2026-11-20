using System.Text.Json;
using System.Text.Json.Nodes;

namespace JSONRPCServer;

public class JsonRpcException(int code, string message, string? data = null) : Exception(message)
{
    public int Code { get; } = code;
    public string? Details { get; } = data;
}

public static class JsonRpcHandler
{
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;

    public const int DivisionByZero = -32000;
    public const int IntOverflow = -32001;

    private static readonly Dictionary<string, (string[] Params, Func<JsonNode?[], JsonNode> Invoke)> Methods =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["SUM"] = (["x", "y"], a => Procedures.Sum(ToDouble(a[0], "x"), ToDouble(a[1], "y"))),
            ["SUB"] = (["x", "y"], a => Procedures.Sub(ToDouble(a[0], "x"), ToDouble(a[1], "y"))),
            ["MUL"] = (["x", "y"], a => Procedures.Mul(ToDouble(a[0], "x"), ToDouble(a[1], "y"))),
            ["DIV"] = (["x", "y"], a => Procedures.Div(ToDouble(a[0], "x"), ToDouble(a[1], "y"))),
            ["FACT"] = (["x"], a => Procedures.Fact(ToInt(a[0], "x"))),
        };

    public static JsonNode? Handle(string body)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return Error(null, ParseError, "Parse error");
        }

        if (root is JsonArray batch)
        {
            if (batch.Count == 0)
                return Error(null, InvalidRequest, "Invalid Request", "Empty batch");

            var responses = new JsonArray();
            foreach (var item in batch)
            {
                var r = HandleSingle(item);
                if (r is not null) responses.Add(r);
            }
            return responses.Count == 0 ? null : responses;
        }

        return HandleSingle(root);
    }

    private static JsonNode? HandleSingle(JsonNode? node)
    {
        if (node is not JsonObject req)
            return Error(null, InvalidRequest, "Invalid Request", "Request must be an object");

        bool isNotification = !req.ContainsKey("id");
        JsonNode? id = req["id"]?.DeepClone();
        if (id is not null && id.GetValueKind() is not (JsonValueKind.String or JsonValueKind.Number))
            return Error(null, InvalidRequest, "Invalid Request", "id must be a string, number or null");

        try
        {
            if (req["jsonrpc"]?.GetValueKind() != JsonValueKind.String || req["jsonrpc"]!.GetValue<string>() != "2.0")
                throw new JsonRpcException(InvalidRequest, "Invalid Request", "jsonrpc must be \"2.0\"");

            if (req["method"]?.GetValueKind() != JsonValueKind.String)
                throw new JsonRpcException(InvalidRequest, "Invalid Request", "method must be a string");

            string methodName = req["method"]!.GetValue<string>();
            if (!Methods.TryGetValue(methodName, out var method))
                throw new JsonRpcException(MethodNotFound, "Method not found", methodName);

            var args = BindParams(req["params"], method.Params, req.ContainsKey("params"));
            var result = method.Invoke(args);

            return isNotification ? null : new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["result"] = result,
                ["id"] = id,
            };
        }
        catch (JsonRpcException e)
        {
            return isNotification ? null : Error(id, e.Code, e.Message, e.Details);
        }
        catch (Exception e)
        {
            return isNotification ? null : Error(id, InternalError, "Internal error", e.Message);
        }
    }

    private static JsonNode?[] BindParams(JsonNode? prms, string[] names, bool present)
    {
        if (!present)
            throw new JsonRpcException(InvalidParams, "Invalid params", $"Expected {names.Length} parameter(s)");

        switch (prms)
        {
            case JsonArray arr:
                if (arr.Count != names.Length)
                    throw new JsonRpcException(InvalidParams, "Invalid params",
                        $"Expected {names.Length} parameter(s), got {arr.Count}");
                return arr.ToArray();

            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key))
                    if (!names.Contains(key))
                        throw new JsonRpcException(InvalidParams, "Invalid params", $"Unknown parameter '{key}'");
                return names.Select(n => obj.ContainsKey(n)
                    ? obj[n]
                    : throw new JsonRpcException(InvalidParams, "Invalid params", $"Missing parameter '{n}'")).ToArray();

            default:
                throw new JsonRpcException(InvalidRequest, "Invalid Request", "params must be an array or an object");
        }
    }

    private static double ToDouble(JsonNode? node, string name) =>
        node is JsonValue v && v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<double>(out var d)
            ? d
            : throw new JsonRpcException(InvalidParams, "Invalid params", $"'{name}' must be a number");

    private static int ToInt(JsonNode? node, string name) =>
        node is JsonValue v && v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<int>(out var i)
            ? i
            : throw new JsonRpcException(InvalidParams, "Invalid params", $"'{name}' must be an int");

    private static JsonObject Error(JsonNode? id, int code, string message, string? data = null)
    {
        var error = new JsonObject { ["code"] = code, ["message"] = message };
        if (data is not null) error["data"] = data;
        return new JsonObject { ["jsonrpc"] = "2.0", ["error"] = error, ["id"] = id };
    }
}
