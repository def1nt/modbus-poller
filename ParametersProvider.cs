using System.Text.Json;

public interface IRegisterInfoProvider
{
    public IEnumerable<RegisterInfo> GetParameters();
}

public sealed class SQLRegisterInfoProvider : IRegisterInfoProvider
{
    private readonly int seriesId;
    private readonly int version;

    public SQLRegisterInfoProvider(int seriesId, int version = 0)
    {
        this.seriesId = seriesId;
        this.version = version;
    }

    public IEnumerable<RegisterInfo> GetParameters()
    {
        using var reader = DatabaseService.GetDataReader($"SELECT address, read_function, interval, multiplier, name, codename, version FROM public.series_params WHERE series_id = {seriesId} AND poll = true");
        while (reader.Read())
        {
            yield return new RegisterInfo
            {
                Address = reader.GetString(0),
                Function = reader.GetByte(1),
                PollInterval = reader.IsDBNull(2) ? 0 : (uint)reader.GetInt32(2),
                Multiplier = reader.GetDouble(3),
                Name = reader.GetString(4),
                Codename = reader.GetString(5),
                Version = reader.IsDBNull(6) ? 0 : reader.GetInt32(6)
            };
        }
    }
}

public sealed class JSONRegisterInfoProvider : IRegisterInfoProvider
{
    public JsonDocument jsonDocument;

    public JSONRegisterInfoProvider(int seriesId)
    {
        string path = $"{seriesId}.json";
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

    private static object? GetValueOrNull(JsonElement jsonElement, string name, Type type)
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
}
