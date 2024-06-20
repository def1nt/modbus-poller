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
        var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        if (connection.State != ConnectionState.Open)
        {
            throw new NpgsqlException("Could not connect to database");
        }
        var cmd = new NpgsqlCommand(query, connection);
        return cmd.ExecuteReader(CommandBehavior.CloseConnection); // This will close the connection when the reader is closed
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

    /// <summary>
    /// Executes query against our current database and returns single value result.
    /// </summary>
    /// <typeparam name="T">Type of the expected value. DO NOT PASS any Nullable types.</typeparam>
    /// <param name="query">A complete SQL query to run.</param>
    /// <returns>Value of type T? in case of reference types and value of type T in case of value types, converted from object? returned by the query.</returns>
    /// <exception cref="NpgsqlException"></exception>
    /// <exception cref="InvalidCastException"></exception>
    public async static Task<T?> ExecuteScalar<T>(string query)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        if (connection.State != ConnectionState.Open)
        {
            throw new NpgsqlException("Could not connect to database");
        }
        using var cmd = new NpgsqlCommand(query, connection);
        var result = await cmd.ExecuteScalarAsync();
        return (T?)Convert.ChangeType(result, typeof(T));
    }
}
