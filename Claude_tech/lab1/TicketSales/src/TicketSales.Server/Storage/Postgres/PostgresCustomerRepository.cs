using System.Collections.Generic;
using Npgsql;
using TicketSales.Shared.Models;

namespace TicketSales.Server.Storage.Postgres;

public class PostgresCustomerRepository : ICustomerRepository
{
    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresCustomerRepository(PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Customer? GetById(int id)
    {
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, full_name, email FROM customers WHERE id = @id";
        command.Parameters.AddWithValue("id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public IReadOnlyList<Customer> GetAll()
    {
        var result = new List<Customer>();
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, full_name, email FROM customers";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(Map(reader));
        }
        return result;
    }

    public Customer? FindByEmail(string email)
    {
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, full_name, email FROM customers WHERE lower(email) = lower(@email)";
        command.Parameters.AddWithValue("email", email);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public Customer Add(Customer entity)
    {
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO customers (full_name, email)
            VALUES (@fullName, @email)
            RETURNING id;
            """;
        command.Parameters.AddWithValue("fullName", entity.FullName);
        command.Parameters.AddWithValue("email", entity.Email);
        entity.Id = (int)command.ExecuteScalar()!;
        return entity;
    }

    public void Update(Customer entity)
    {
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE customers SET full_name = @fullName, email = @email WHERE id = @id";
        command.Parameters.AddWithValue("id", entity.Id);
        command.Parameters.AddWithValue("fullName", entity.FullName);
        command.Parameters.AddWithValue("email", entity.Email);
        command.ExecuteNonQuery();
    }

    private static Customer Map(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        FullName = reader.GetString(1),
        Email = reader.GetString(2)
    };
}
