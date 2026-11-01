using System;
using System.Collections;
using System.Collections.Generic;
using Npgsql;
using TicketSales.Server.Configuration;
using Xunit;

namespace TicketSales.Tests;

public class ServerSettingsTests
{
    [Fact]
    public void Load_WithoutEnvironment_UsesLocalDefaults()
    {
        var settings = ServerSettings.Load([], new Hashtable());

        Assert.Equal("Local", settings.Mode);
        Assert.Equal(5050, settings.Port);
        Assert.Equal("localhost", new NpgsqlConnectionStringBuilder(settings.PrimaryConnectionString).Host);
        Assert.Equal(5433, new NpgsqlConnectionStringBuilder(settings.PrimaryConnectionString).Port);
        Assert.Equal(5434, new NpgsqlConnectionStringBuilder(settings.ReplicaConnectionString).Port);
    }

    [Fact]
    public void Load_DockerEnvironment_UsesServiceNamesInsteadOfLocalhost()
    {
        var settings = ServerSettings.Load([], DockerEnvironment());

        var primary = new NpgsqlConnectionStringBuilder(settings.PrimaryConnectionString);
        var replica = new NpgsqlConnectionStringBuilder(settings.ReplicaConnectionString);
        Assert.Equal("Docker", settings.Mode);
        Assert.Equal(6060, settings.Port);
        Assert.Equal("postgres-primary", primary.Host);
        Assert.Equal(5432, primary.Port);
        Assert.Equal("postgres-replica", replica.Host);
        Assert.Equal("secret-from-env", primary.Password);
        Assert.Equal(3, settings.DbConnectAttempts);
        Assert.Equal(TimeSpan.FromMilliseconds(100), settings.DbRetryDelay);
    }

    [Fact]
    public void Load_CommandLineArguments_OverrideEnvironment()
    {
        var settings = ServerSettings.Load(["7070"], DockerEnvironment());

        Assert.Equal(7070, settings.Port);
    }

    [Theory]
    [InlineData("TICKETSALES_PORT", "abc")]
    [InlineData("TICKETSALES_PORT", "70000")]
    [InlineData("DB_PRIMARY_PORT", "0")]
    [InlineData("DB_CONNECT_ATTEMPTS", "-1")]
    public void Load_InvalidValue_ThrowsConfigurationExceptionWithParameterName(string name, string value)
    {
        var environment = DockerEnvironment();
        environment[name] = value;

        var ex = Assert.Throws<ConfigurationException>(() => ServerSettings.Load([], environment));
        Assert.Contains(name, ex.Message);
        Assert.Contains(value, ex.Message);
    }

    [Fact]
    public void Describe_DoesNotExposePassword()
    {
        var description = ServerSettings.Load([], DockerEnvironment()).Describe();

        Assert.Contains("postgres-primary", description);
        Assert.DoesNotContain("secret-from-env", description);
    }

    private static Hashtable DockerEnvironment() => new(new Dictionary<string, string>
    {
        ["APP_MODE"] = "Docker",
        ["TICKETSALES_PORT"] = "6060",
        ["DB_PRIMARY_HOST"] = "postgres-primary",
        ["DB_PRIMARY_PORT"] = "5432",
        ["DB_REPLICA_HOST"] = "postgres-replica",
        ["DB_REPLICA_PORT"] = "5432",
        ["DB_PASSWORD"] = "secret-from-env",
        ["DB_CONNECT_ATTEMPTS"] = "3",
        ["DB_RETRY_DELAY_MS"] = "100"
    });
}
