using System;

namespace TicketSales.Server.Configuration;

public class ConfigurationException : Exception
{
    public ConfigurationException(string message) : base(message)
    {
    }
}
