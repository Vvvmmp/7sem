using Calc;
using Grpc.Core;
using Grpc.Net.Client;

var address = args.Length > 0 ? args[0] : "http://localhost:5001";
Console.WriteLine($"gRPCClient01 (C#) -> {address}\n");

using var channel = GrpcChannel.ForAddress(address);
var client = new Calculator.CalculatorClient(channel);

void Call(string name, Func<string> rpc)
{
    try
    {
        Console.WriteLine($"{name,-16} = {rpc()}");
    }
    catch (RpcException e)
    {
        Console.WriteLine($"{name,-16} -> ERROR {e.StatusCode}: {e.Status.Detail}");
    }
}

BinaryRequest Req(double x, double y) => new() { X = x, Y = y };

Call("SUM(7.5, 2.5)", () => client.Sum(Req(7.5, 2.5)).Result.ToString());
Call("SUB(7.5, 2.5)", () => client.Sub(Req(7.5, 2.5)).Result.ToString());
Call("MUL(7.5, 2.5)", () => client.Mul(Req(7.5, 2.5)).Result.ToString());
Call("DIV(7.5, 2.5)", () => client.Div(Req(7.5, 2.5)).Result.ToString());
Call("DIV(7.5, 0)", () => client.Div(Req(7.5, 0)).Result.ToString());
Call("FACT(5)", () => client.Fact(new FactRequest { X = 5 }).Result.ToString());
Call("FACT(12)", () => client.Fact(new FactRequest { X = 12 }).Result.ToString());
Call("FACT(13)", () => client.Fact(new FactRequest { X = 13 }).Result.ToString());
Call("FACT(-1)", () => client.Fact(new FactRequest { X = -1 }).Result.ToString());
