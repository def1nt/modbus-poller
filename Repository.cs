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
        try
        {
            foreach (var row in data.Data)
            {
                await DatabaseService.ExecuteNonQuery("""
                INSERT INTO device_metrics (device_id, "Par_name", "Par_value", sended_at) VALUES (@DeviceID, @Name, @Value, @Timestamp)
                """,
                    ("Name", row.Name),
                    ("Value", row.Value),
                    ("Timestamp", row.Timestamp),
                    ("DeviceID", (long)data.DeviceID)
                );
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
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

        try
        {
            _ = int.TryParse(data.Data.FirstOrDefault(x => x.Name == "Ошибки")?.Value, out int errorCode);
            if (errorCode != 0)
            {
                await DatabaseService.ExecuteNonQuery("""
                INSERT INTO device_errs (device_unique_id, err_id, w_cycle, added_at) VALUES (@DeviceID, @Error, @Cycle, @Timestamp)
                """,
                    ("DeviceID", (long)data.DeviceID),
                    ("Error", errorCode),
                    ("Cycle", int.Parse(data.Data.FirstOrDefault(x => x.Name == "Цикл стирки")?.Value ?? "0")),
                    ("Timestamp", DateTime.UtcNow)
                );
            }

            _ = int.TryParse(data.Data.FirstOrDefault(x => x.Name == "Статус: Автоматич_упр")?.Value, out int autoControl);
            if (autoControl != 1) return;

            await DatabaseService.ExecuteNonQuery("DELETE FROM device_cp WHERE unique_id = @DeviceID",
                ("DeviceID", (long)data.DeviceID)
            );

            _ = int.TryParse(data.Data.FirstOrDefault(x => x.Name == "Текущая программа")?.Value, out int currentProgramID);
            await DatabaseService.ExecuteNonQuery("""
            INSERT INTO device_cp (unique_id, program_name, step_name, added_at, program_id)
            VALUES (@DeviceID, @ProgramName, @StepName, @Timestamp, @currentProgramID)
            """,
                ("DeviceID", (long)data.DeviceID),
                ("ProgramName", data.programName),
                ("StepName", data.stepName),
                ("Timestamp", DateTime.UtcNow),
                ("currentProgramID", currentProgramID)
            );

            _ = int.TryParse(data.Data.FirstOrDefault(x => x.Name == "Цикл стирки")?.Value, out int wash_cycle);
            _ = int.TryParse(data.Data.FirstOrDefault(x => x.Name == "Наработка часы")?.Value, out int all_operating_time);
            _ = int.TryParse(data.Data.FirstOrDefault(x => x.Name == "Расход воды всего")?.Value, out int all_water_consumption);
            _ = double.TryParse(data.Data.FirstOrDefault(x => x.Name == "Взвешенное бельё")?.Value, System.Globalization.CultureInfo.GetCultureInfo("en-US"), out double cur_weight);
            _ = int.TryParse(data.Data.FirstOrDefault(x => x.Name == "Время работы программы минуты")?.Value, out int cur_program_time);
            await DatabaseService.ExecuteNonQuery("""
            INSERT INTO device_math_metrics (device_unique_id, wash_cycle, all_operating_time, all_water_consumption, cur_program_name, cur_weight, cur_program_time, added_at, cur_program_id)
            VALUES (@DeviceID, @WashCycle, @AllOperatingTime, @AllWaterConsumption, @ProgramName, @CurWeight, @CurProgramTime, @Timestamp, @currentProgramID)
            """,
                ("DeviceID", (long)data.DeviceID),
                ("WashCycle", wash_cycle),
                ("AllOperatingTime", all_operating_time),
                ("AllWaterConsumption", all_water_consumption),
                ("ProgramName", data.programName),
                ("CurWeight", (int)(cur_weight * 10)),
                ("CurProgramTime", cur_program_time),
                ("Timestamp", DateTime.UtcNow),
                ("currentProgramID", currentProgramID)
            );
        }
        catch (Exception e)
        {
            Console.WriteLine("Error saving data to database: " + e.Message);
            if (Environment.GetEnvironmentVariable("DEBUG") is not null)
                Console.WriteLine($"Trace: {e.StackTrace}");
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
