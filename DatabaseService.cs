using Npgsql;
using System.Data;

public static class DatabaseService
{
    private static readonly string _connectionString;

    static DatabaseService()
    {
        _connectionString = AppSettings.PostgresConnectionString;
    }

    public static NpgsqlDataReader GetDataReader(string query)
    {
        var connection = new NpgsqlConnection(_connectionString); // Make sure not to use 'using' here to avoid disposing the connection
        connection.Open();
        if (connection.State != ConnectionState.Open)
        {
            throw new NpgsqlException("Could not connect to database");
        }
        var cmd = new NpgsqlCommand(query, connection);
        return cmd.ExecuteReader();
    }

    public async static Task<int> ExecuteNonQuery(string query, params (string, object)[] values)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        if (connection.State != ConnectionState.Open)
        {
            throw new NpgsqlException("Could not connect to database");
        }
        using var cmd = new NpgsqlCommand(query, connection);
        foreach (var (name, value) in values)
        {
            cmd.Parameters.AddWithValue(name, value);
        }
        return await cmd.ExecuteNonQueryAsync();
    }
}
