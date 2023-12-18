using System.Globalization;
using Npgsql;
using System.Text.Json;

public interface IRegisterInfoProvider
{
    public IEnumerable<RegisterInfo> GetParameters();
}

public sealed class SQLRegisterInfoProvider : IRegisterInfoProvider
{
    private readonly string connectionString;
    private readonly int seriesId;

    public SQLRegisterInfoProvider(int seriesId)
    {
        this.connectionString = AppSettings.PostgresConnectionString;
        this.seriesId = seriesId;
    }

    public IEnumerable<RegisterInfo> GetParameters()
    {
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        using var command = new NpgsqlCommand($"SELECT * FROM public.series_params WHERE series_id = '{seriesId}' AND poll = true", connection);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            yield return new RegisterInfo
            {
                Address = reader.GetString(1),
                Function = reader.GetByte(2),
                PollInterval = (uint)reader.GetInt32(3), // TODO: null check!!!
                Multiplier = reader.GetDouble(4),
                Name = reader.GetString(5)
            };
        }
    }

    public IEnumerable<RegisterInfo> GetOperationalParameters()
    {
        throw new NotImplementedException();
    }

    public IEnumerable<RegisterInfo> GetStaticParameters()
    {
        throw new NotImplementedException();
    }
}

public sealed class JSONRegisterInfoProvider : IRegisterInfoProvider
{
    public JsonDocument jsonDocument;

    public JSONRegisterInfoProvider(string modelId)
    {
        string path = $"{modelId}.json";
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"File {path} not found");
        }
        jsonDocument = JsonDocument.Parse(File.ReadAllText(path));
    }

    public IEnumerable<RegisterInfo> GetParameters()
    {
        var root = jsonDocument.RootElement;
        var array = root.GetProperty("parameters").EnumerateArray();
        foreach (var item in array)
        {
            var poll = item.GetProperty("poll").GetBoolean();
            if (!poll)
            {
                continue;
            }

            var parameter = new RegisterInfo
            {
                Address = GetValueOrNull(item, "address", typeof(string)) as string ?? string.Empty,
                Function = GetValueOrNull(item, "read_function", typeof(byte)) as byte? ?? 0,
                PollInterval = GetValueOrNull(item, "interval", typeof(uint)) as uint? ?? 0,
                Multiplier = GetValueOrNull(item, "multiplier", typeof(double)) as double? ?? 1.0,
                Name = GetValueOrNull(item, "name", typeof(string)) as string ?? string.Empty
            };

            yield return parameter;
        }
    }

    private object? GetValueOrNull(JsonElement jsonElement, string name, Type type)
    {
        if (jsonElement.TryGetProperty(name, out JsonElement element))
        {
            return type switch
            {
                Type t when t == typeof(string) => element.GetString(),
                Type t when t == typeof(int) => element.TryGetInt32(out int value) ? value : (int?)null,
                Type t when t == typeof(uint) => element.TryGetUInt32(out uint value) ? value : (uint?)null,
                Type t when t == typeof(byte) => element.TryGetByte(out byte value) ? value : (byte?)null,
                Type t when t == typeof(double) => element.TryGetDouble(out double value) ? value : (double?)null,
                Type t when t == typeof(bool) => element.GetBoolean(),
                _ => throw new ArgumentException($"Type {type} not supported"),
            };
        }
        return null;
    }

    public IEnumerable<RegisterInfo> GetOperationalParameters()
    {
        throw new NotImplementedException();
    }

    public IEnumerable<RegisterInfo> GetStaticParameters()
    {
        throw new NotImplementedException();
    }

}