using Npgsql;
using System.Text;
using System.Text.Json;

public interface IRepository
{
    public Task SaveData(MachineData data);
}

sealed public class FileRepository : IRepository
{
    public FileRepository()
    {
        File.WriteAllText("savedata.txt", string.Empty);
    }

    public async Task SaveData(MachineData data)
    {
        if (!File.Exists("savedata.txt"))
        {
            File.Create("savedata.txt");
        }
        using (var file = new StreamWriter("savedata.txt", true))
        {
            await file.WriteLineAsync(data.ToString());
        }
    }
}

public sealed class DatabaseRepository : IRepository
{
    private readonly CancellationToken _token;
    public DatabaseRepository(CancellationToken token = default)
    {
        _token = token;
    }

    public async Task SaveData(MachineData data)
    {
        var connString = "Host=192.168.105.12;Username=postgres;Password=sqladmin;Database=cloud_vmz";
        await using var conn = new NpgsqlConnection(connString);

        try
        {
            await conn.OpenAsync(_token);

            foreach (var row in data.Data)
            {
                await using (var cmd = new NpgsqlCommand("""
                INSERT INTO device_metrics (device_id, "Par_name", "Par_value", sended_at) VALUES (@DeviceID, @Name, @Value, @Timestamp)
                """, conn))
                {
                    cmd.Parameters.AddWithValue("Name", row.Name);
                    cmd.Parameters.AddWithValue("Value", row.Value);
                    cmd.Parameters.AddWithValue("Timestamp", row.Timestamp);
                    cmd.Parameters.AddWithValue("DeviceID", long.Parse(data.DeviceID));
                    await cmd.ExecuteNonQueryAsync(_token);
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
        finally
        {
            await conn.CloseAsync();
        }
    }
}

public sealed class ConsoleRepository : IRepository
{
    public async Task SaveData(MachineData data)
    {
        Console.WriteLine(data.ToString());
    }
}

public sealed class OpenTSDBRepository : IRepository
{
    private readonly CancellationToken _token;
    private Data _data;
    public OpenTSDBRepository(CancellationToken token = default)
    {
        _token = token;
    }

    public async Task SaveData(MachineData data)
    {
        // Using http to connect to OpenTSDB instance
        var client = new HttpClient();
        client.BaseAddress = new Uri("http://192.168.105.12:4242/api/put");
        _data = new()
        {
            metric = "device" + data.DeviceID,
            value = "",
            tags = new Dictionary<string, string>()
                {
                    { "register", "" },
                }
        };
        foreach (var row in data.Data)
        {
            _data.value = row.Value;
            _data.tags["register"] = row.Name;

            string json = JsonSerializer.Serialize(_data);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var result = await client.PostAsync(client.BaseAddress, content, _token);
            if (!result.IsSuccessStatusCode)
            {
                Console.WriteLine($"Error sending data: {row.Name} {row.Value} {row.Timestamp}");
            }
        }
    }

    struct Data
    {
        public string metric { get; set; }
        public string value { get; set; }
        public Dictionary<string, string> tags { get; set; }
    }
}


public static class RepositoryFactory
{
    public static IRepository GetRepository(string repositoryType)
    {
        return repositoryType switch
        {
            "file" => new FileRepository(),
            "database" => new DatabaseRepository(),
            "opentsdb" => new OpenTSDBRepository(),
            "console" => new ConsoleRepository(),
            _ => throw new ArgumentException("Invalid repository type.")
        };
    }
}
