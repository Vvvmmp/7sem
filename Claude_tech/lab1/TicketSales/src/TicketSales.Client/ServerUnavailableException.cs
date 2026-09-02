using System;

namespace TicketSales.Client;

public class ServerUnavailableException : Exception
{
    public ServerUnavailableException(string message, Exception inner) : base(message, inner)
    {
    }
}
