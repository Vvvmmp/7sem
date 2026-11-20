using Calc;
using Grpc.Core;

namespace gRPCServer01;

public class CalculatorService : Calculator.CalculatorBase
{
    public override Task<DoubleReply> Sum(BinaryRequest r, ServerCallContext ctx) =>
        Task.FromResult(new DoubleReply { Result = r.X + r.Y });

    public override Task<DoubleReply> Sub(BinaryRequest r, ServerCallContext ctx) =>
        Task.FromResult(new DoubleReply { Result = r.X - r.Y });

    public override Task<DoubleReply> Mul(BinaryRequest r, ServerCallContext ctx) =>
        Task.FromResult(new DoubleReply { Result = r.X * r.Y });

    public override Task<DoubleReply> Div(BinaryRequest r, ServerCallContext ctx)
    {
        if (r.Y == 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "DIV: division by zero (y = 0)"));
        return Task.FromResult(new DoubleReply { Result = r.X / r.Y });
    }

    public override Task<IntReply> Fact(FactRequest r, ServerCallContext ctx)
    {
        if (r.X < 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"FACT: x must be >= 0 (x = {r.X})"));
        try
        {
            int result = 1;
            for (int i = 2; i <= r.X; i++)
                result = checked(result * i);
            return Task.FromResult(new IntReply { Result = result });
        }
        catch (OverflowException)
        {
            throw new RpcException(new Status(StatusCode.OutOfRange, $"FACT: {r.X}! exceeds int32 range"));
        }
    }
}
