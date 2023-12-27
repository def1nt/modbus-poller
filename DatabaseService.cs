using Npgsql;
using System.Data;

public class DatabaseService
{
    private readonly string _connectionString;
    private readonly NpgsqlConnection _connection;
    private static DatabaseService? _instance;

    private DatabaseService(string connectionString)
    {
        _connectionString = connectionString;
        _instance = this;
        _connection = new NpgsqlConnection(_connectionString);
    }

    public static DatabaseService GetInstance()
    {
        _instance ??= new DatabaseService(AppSettings.PostgresConnectionString);
        return _instance;
    }

    public NpgsqlDataReader GetDataReader(string query)
    {
        _connection.Open();
        if (_connection.State != ConnectionState.Open)
        {
            throw new NpgsqlException("Could not connect to database");
        }
        var cmd = new NpgsqlCommand(query, _connection);
        return cmd.ExecuteReader();
    }
}
