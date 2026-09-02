using System;

namespace TicketSales.Client;

public class NetworkException : Exception
{
    public NetworkException(string message) : base(message)
    {
    }

    public NetworkException(string message, Exception inner) : base(message, inner)
    {
    }
}
