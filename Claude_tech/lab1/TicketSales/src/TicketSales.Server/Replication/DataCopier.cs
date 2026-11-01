using System.Collections.Generic;
using System.Linq;
using Npgsql;

namespace TicketSales.Server.Replication;

public static class DataCopier
{
    public static readonly string[] Tables = { "events", "seats", "customers", "tickets", "payments" };

    public static List<string> CopyTicketRows(NpgsqlConnection source, NpgsqlConnection target, int ticketId)
    {
        var filters = new (string Table, string Where)[]
        {
            ("customers", "id = (SELECT customer_id FROM tickets WHERE id = @id)"),
            ("seats", "id = (SELECT seat_id FROM tickets WHERE id = @id)"),
            ("tickets", "id = @id"),
            ("payments", "ticket_id = @id")
        };

        var copied = new List<string>();
        using var transaction = target.BeginTransaction();
        foreach (var (table, where) in filters)
        {
            var rows = ReadRows(source, table, where, ticketId);
            foreach (var row in rows)
            {
                Upsert(target, transaction, table, row);
            }

            copied.Add($"{table}: {rows.Count}");
        }

        ResetSequences(target, transaction);
        transaction.Commit();
        return copied;
    }

    public static void CopyAll(NpgsqlConnection source, NpgsqlConnection target)
    {
        using var transaction = target.BeginTransaction();

        using (var truncate = target.CreateCommand())
        {
            truncate.Transaction = transaction;
            truncate.CommandText = $"TRUNCATE {string.Join(", ", Tables)} RESTART IDENTITY";
            truncate.ExecuteNonQuery();
        }

        foreach (var table in Tables)
        {
            foreach (var row in ReadRows(source, table, "TRUE", null))
            {
                Upsert(target, transaction, table, row);
            }
        }

        ResetSequences(target, transaction);
        transaction.Commit();
    }

    public static List<string> FindDifferences(NpgsqlConnection primary, NpgsqlConnection replica)
    {
        var differences = new List<string>();

        foreach (var table in Tables)
        {
            var (primaryCount, primaryHash) = TableFingerprint(primary, table);
            var (replicaCount, replicaHash) = TableFingerprint(replica, table);
            if (primaryHash != replicaHash)
            {
                differences.Add($"{table}: Primary {primaryCount} строк, Replica {replicaCount} строк — содержимое отличается");
            }
        }

        var primarySeats = ReadSeatStatuses(primary);
        var replicaSeats = ReadSeatStatuses(replica);
        foreach (var seatId in primarySeats.Keys.Union(replicaSeats.Keys).OrderBy(id => id))
        {
            var primaryStatus = primarySeats.GetValueOrDefault(seatId, "нет");
            var replicaStatus = replicaSeats.GetValueOrDefault(seatId, "нет");
            if (primaryStatus != replicaStatus)
            {
                differences.Add($"место {seatId}: Primary = {primaryStatus}, Replica = {replicaStatus}");
            }
        }

        return differences;
    }

    private static List<Dictionary<string, object>> ReadRows(NpgsqlConnection connection, string table, string where, int? id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM {table} WHERE {where} ORDER BY id";
        if (id is not null)
        {
            command.Parameters.AddWithValue("id", id.Value);
        }

        var rows = new List<Dictionary<string, object>>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var row = new Dictionary<string, object>();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.GetValue(i);
            }

            rows.Add(row);
        }

        return rows;
    }

    private static void Upsert(NpgsqlConnection connection, NpgsqlTransaction transaction, string table, Dictionary<string, object> row)
    {
        var columns = row.Keys.ToList();
        var updates = columns.Where(c => c != "id").Select(c => $"\"{c}\" = EXCLUDED.\"{c}\"");

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO {table} ({string.Join(", ", columns.Select(c => $"\"{c}\""))})
            VALUES ({string.Join(", ", columns.Select((_, i) => $"@p{i}"))})
            ON CONFLICT (id) DO UPDATE SET {string.Join(", ", updates)}
            """;
        for (var i = 0; i < columns.Count; i++)
        {
            command.Parameters.AddWithValue($"p{i}", row[columns[i]]);
        }

        command.ExecuteNonQuery();
    }

    private static void ResetSequences(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        foreach (var table in Tables)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                $"SELECT setval(pg_get_serial_sequence('{table}', 'id'), COALESCE((SELECT MAX(id) FROM {table}), 0) + 1, false)";
            command.ExecuteScalar();
        }
    }

    private static (long Count, string Hash) TableFingerprint(NpgsqlConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*), md5(COALESCE(string_agg(t::text, '|' ORDER BY id), '')) FROM {table} t";
        using var reader = command.ExecuteReader();
        reader.Read();
        return (reader.GetInt64(0), reader.GetString(1));
    }

    private static Dictionary<int, string> ReadSeatStatuses(NpgsqlConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, status FROM seats";
        var result = new Dictionary<int, string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result[reader.GetInt32(0)] = reader.GetString(1);
        }

        return result;
    }
}
