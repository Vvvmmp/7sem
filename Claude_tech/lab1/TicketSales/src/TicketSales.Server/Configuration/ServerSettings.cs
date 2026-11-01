using System;
using System.Collections;
using Npgsql;

namespace TicketSales.Server.Configuration;

public class ServerSettings
{
    public string Mode { get; private init; } = "Local";
    public int Port { get; private init; } = 5050;
    public int ServerTimeDelayMs { get; private init; }
    public string PrimaryConnectionString { get; private init; } = string.Empty;
    public string ReplicaConnectionString { get; private init; } = string.Empty;
    public int DbConnectAttempts { get; private init; } = 10;
    public TimeSpan DbRetryDelay { get; private init; } = TimeSpan.FromSeconds(2);

    public static ServerSettings Load(string[] args, IDictionary environment)
    {
        string? Env(string name) => environment[name] as string is { Length: > 0 } value ? value.Trim() : null;

        var dbName = Env("DB_NAME") ?? "ticketsales";
        var dbUser = Env("DB_USER") ?? "ticketsales";
        var dbPassword = Env("DB_PASSWORD") ?? "ticketsales";

        var primary = BuildConnectionString(
            Env("DB_PRIMARY_HOST") ?? "localhost", ParsePort("DB_PRIMARY_PORT", Env("DB_PRIMARY_PORT") ?? "5433"), dbName, dbUser, dbPassword);
        var replica = BuildConnectionString(
            Env("DB_REPLICA_HOST") ?? "localhost", ParsePort("DB_REPLICA_PORT", Env("DB_REPLICA_PORT") ?? "5434"), dbName, dbUser, dbPassword);

        return new ServerSettings
        {
            Mode = Env("APP_MODE") ?? "Local",
            Port = args.Length > 0
                ? ParsePort("port (аргумент 1)", args[0])
                : ParsePort("TICKETSALES_PORT", Env("TICKETSALES_PORT") ?? "5050"),
            ServerTimeDelayMs = args.Length > 1
                ? ParseInt("serverTimeDelayMs (аргумент 2)", args[1], 0, 60_000)
                : ParseInt("SERVER_TIME_DELAY_MS", Env("SERVER_TIME_DELAY_MS") ?? "0", 0, 60_000),
            PrimaryConnectionString = args.Length > 2 ? args[2] : primary,
            ReplicaConnectionString = args.Length > 3 ? args[3] : replica,
            DbConnectAttempts = ParseInt("DB_CONNECT_ATTEMPTS", Env("DB_CONNECT_ATTEMPTS") ?? "10", 1, 1000),
            DbRetryDelay = TimeSpan.FromMilliseconds(ParseInt("DB_RETRY_DELAY_MS", Env("DB_RETRY_DELAY_MS") ?? "2000", 0, 600_000))
        };
    }

    public string Describe() =>
        $"Mode={Mode}, Port={Port}, Primary=[{Mask(PrimaryConnectionString)}], Replica=[{Mask(ReplicaConnectionString)}], " +
        $"DbConnectAttempts={DbConnectAttempts}, DbRetryDelay={DbRetryDelay.TotalMilliseconds:F0}ms";

    private static string BuildConnectionString(string host, int port, string database, string user, string password) =>
        new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Database = database,
            Username = user,
            Password = password,
            Timeout = 2
        }.ConnectionString;

    private static string Mask(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        return $"Host={builder.Host};Port={builder.Port};Database={builder.Database};Username={builder.Username}";
    }

    private static int ParsePort(string name, string value) => ParseInt(name, value, 1, 65535);

    private static int ParseInt(string name, string value, int min, int max)
    {
        if (!int.TryParse(value, out var result) || result < min || result > max)
        {
            throw new ConfigurationException($"Некорректный параметр {name}='{value}': ожидается целое число от {min} до {max}");
        }

        return result;
    }
}
