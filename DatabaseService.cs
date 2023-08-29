using Npgsql;

public interface IDatabaseService
{
    public DatabaseService GetInstance(string connectionString);
}

public class DatabaseService : IDatabaseService
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

    public DatabaseService GetInstance(string connectionString)
    {
        _instance ??= new DatabaseService(connectionString);
        return _instance;
    }
}
