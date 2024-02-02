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
        await using var conn = new NpgsqlConnection(AppSettings.PostgresConnectionString);

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
                    cmd.Parameters.AddWithValue("DeviceID", data.DeviceID);
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
        await Task.Run(() => Console.WriteLine(data.ToString()));
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
        client.BaseAddress = new Uri(AppSettings.VictoriaMetricsURL);
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

        await using var conn = new NpgsqlConnection(AppSettings.PostgresConnectionString);
        try
        {
            await conn.OpenAsync(_token);

            int.TryParse(data.Data.Where(x => x.Name == "Ошибки").FirstOrDefault().Value, out int errorCode);
            if (errorCode != 0)
            {
                await using (var cmd = new NpgsqlCommand("""
                INSERT INTO device_errs (device_unique_id, err_id, w_cycle, added_at) VALUES (@DeviceID, @Error, @Cycle, @Timestamp)
                """, conn))
                {
                    cmd.Parameters.AddWithValue("DeviceID", data.DeviceID);
                    cmd.Parameters.AddWithValue("Error", errorCode);
                    cmd.Parameters.AddWithValue("Cycle", int.Parse(data.Data.Where(x => x.Name == "Цикл стирки").FirstOrDefault().Value));
                    cmd.Parameters.AddWithValue("Timestamp", DateTime.UtcNow);
                    await cmd.ExecuteNonQueryAsync(_token);
                }
            }

            int.TryParse(data.Data.Where(x => x.Name == "Статус: Автоматич_упр").FirstOrDefault().Value, out int autoControl);
            if (autoControl != 1) return;

            await using (var cmd = new NpgsqlCommand("""
                DELETE FROM device_cp WHERE unique_id = @DeviceID
                """, conn))
            {
                cmd.Parameters.AddWithValue("DeviceID", data.DeviceID);
                await cmd.ExecuteNonQueryAsync(_token);
            }

            await using (var cmd = new NpgsqlCommand("""
                INSERT INTO device_cp (unique_id, program_name, step_name, added_at) VALUES (@DeviceID, @ProgramName, @StepName, @Timestamp)
                """, conn))
            {
                cmd.Parameters.AddWithValue("DeviceID", data.DeviceID);
                cmd.Parameters.AddWithValue("ProgramName", data.programName);
                cmd.Parameters.AddWithValue("StepName", data.stepName);
                cmd.Parameters.AddWithValue("Timestamp", DateTime.UtcNow);
                await cmd.ExecuteNonQueryAsync(_token);
            }

            int.TryParse(data.Data.Where(x => x.Name == "Цикл стирки").FirstOrDefault().Value, out int wash_cycle);
            int.TryParse(data.Data.Where(x => x.Name == "Наработка часы").FirstOrDefault().Value, out int all_operating_time);
            int.TryParse(data.Data.Where(x => x.Name == "Расход воды всего").FirstOrDefault().Value, out int all_water_consumption);
            double.TryParse(data.Data.Where(x => x.Name == "Взвешенное бельё").FirstOrDefault().Value, System.Globalization.CultureInfo.GetCultureInfo("en-US"), out double cur_weight);
            int.TryParse(data.Data.Where(x => x.Name == "Время работы программы минуты").FirstOrDefault().Value, out int cur_program_time);
            await using (var cmd = new NpgsqlCommand("""
                INSERT INTO device_math_metrics (device_unique_id, wash_cycle, all_operating_time, all_water_consumption, cur_program_name, cur_weight, cur_program_time, added_at)
                VALUES (@DeviceID, @WashCycle, @AllOperatingTime, @AllWaterConsumption, @ProgramName, @CurWeight, @CurProgramTime, @Timestamp)
                """, conn))
            {
                cmd.Parameters.AddWithValue("DeviceID", data.DeviceID);
                cmd.Parameters.AddWithValue("WashCycle", wash_cycle);
                cmd.Parameters.AddWithValue("AllOperatingTime", all_operating_time);
                cmd.Parameters.AddWithValue("AllWaterConsumption", all_water_consumption);
                cmd.Parameters.AddWithValue("ProgramName", data.programName);
                cmd.Parameters.AddWithValue("CurWeight", (int)(cur_weight * 10));
                cmd.Parameters.AddWithValue("CurProgramTime", cur_program_time);
                cmd.Parameters.AddWithValue("Timestamp", DateTime.UtcNow);
                await cmd.ExecuteNonQueryAsync(_token);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("Error saving data to database: " + e.Message);
            if (Environment.GetEnvironmentVariable("DEBUG") is not null)
                Console.WriteLine($"Trace: {e.StackTrace}");
        }
        finally
        {
            await conn.CloseAsync();
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
