namespace JSONRPCServer;

public static class Procedures
{
    public static double Sum(double x, double y) => x + y;

    public static double Sub(double x, double y) => x - y;

    public static double Mul(double x, double y) => x * y;

    public static double Div(double x, double y) =>
        y == 0
            ? throw new JsonRpcException(JsonRpcHandler.DivisionByZero, "Division by zero")
            : x / y;

    public static int Fact(int x)
    {
        if (x < 0)
            throw new JsonRpcException(JsonRpcHandler.InvalidParams, "Invalid params", "x must be >= 0");

        int result = 1;
        try
        {
            for (int i = 2; i <= x; i++)
                result = checked(result * i);
        }
        catch (OverflowException)
        {
            throw new JsonRpcException(JsonRpcHandler.IntOverflow, "Result exceeds int range",
                $"{x}! > {int.MaxValue}");
        }
        return result;
    }
}
