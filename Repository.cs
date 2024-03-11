using System.Text;
using System.Text.Json;

public interface IRepository
{
    public Task SaveData(MachineData data);
    enum RepositoryType
    {
        File,
        Database,
        Console
    }
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

public sealed class ConsoleRepository : IRepository
{
    public async Task SaveData(MachineData data)
    {
        await Task.Run(() => Console.WriteLine(data.ToString()));
    }
}

public sealed class DatabaseRepository : IRepository
{
    private readonly CancellationToken _token;
    private Data _data;
    public DatabaseRepository(CancellationToken token = default)
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
            _data.tags["register"] = row.Codename;

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
            _ = int.TryParse(data.Data.FirstOrDefault(x => x.Codename == "errors")?.Value, out int errorCode);
            if (errorCode != 0)
            {
                await DatabaseService.ExecuteNonQuery("""
                INSERT INTO device_errs (device_unique_id, err_id, w_cycle, added_at) VALUES (@DeviceID, @Error, @Cycle, @Timestamp)
                """,
                    ("DeviceID", (long)data.DeviceID),
                    ("Error", errorCode),
                    ("Cycle", int.Parse(data.Data.FirstOrDefault(x => x.Codename == "program_counter")?.Value ?? "0")),
                    ("Timestamp", DateTime.UtcNow)
                );
            }

            var timePassed = new TimeSpan(
                int.Parse(data.Data.FirstOrDefault(d => d.Codename == "program_time_hours")?.Value ?? "0"),
                int.Parse(data.Data.FirstOrDefault(d => d.Codename == "program_time_minutes")?.Value ?? "0"),
                int.Parse(data.Data.FirstOrDefault(d => d.Codename == "program_time_seconds")?.Value ?? "0")
            );
            var timeLeft = new TimeSpan(
                int.Parse(data.Data.FirstOrDefault(d => d.Codename == "time_left_hours")?.Value ?? "0"),
                int.Parse(data.Data.FirstOrDefault(d => d.Codename == "time_left_minutes")?.Value ?? "0"),
                int.Parse(data.Data.FirstOrDefault(d => d.Codename == "time_left_seconds")?.Value ?? "0")
            );
            // Console.WriteLine($"Time passed: {timePassed},\nTime left: {timeLeft}");
            
            int progress;
            try
            {
                checked // Because division by zero is legal with double, but casting to int will overflow
                {
                    progress = (int)(timePassed.TotalSeconds / (timePassed.TotalSeconds + timeLeft.TotalSeconds) * 100);
                }
            }
            catch (Exception e) when (e is OverflowException || e is DivideByZeroException)
            {
                progress = 0;
            }
            _ = int.TryParse(data.Data.FirstOrDefault(x => x.Codename == "automatic_control")?.Value, out int autoControl);
            _ = int.TryParse(data.Data.FirstOrDefault(x => x.Codename == "manual_control")?.Value, out int manualControl);

            var status = autoControl == 1 ? "АВТОМАТИЧЕСКИЙ РЕЖИМ" : manualControl == 1 ? "РУЧНОЙ РЕЖИМ" : "БЕЗДЕЙСТВУЕТ";
            if (autoControl != 1)
            {
                data.programName = "";
                data.stepName = "";
            }

            await DatabaseService.ExecuteNonQuery("DELETE FROM device_cp WHERE unique_id = @DeviceID",
                ("DeviceID", (long)data.DeviceID)
            );

            _ = int.TryParse(data.Data.FirstOrDefault(x => x.Codename == "current_program")?.Value, out int currentProgramID);
            await DatabaseService.ExecuteNonQuery("""
            INSERT INTO device_cp (unique_id, program_name, step_name, added_at, program_id, progress, device_status)
            VALUES (@DeviceID, @ProgramName, @StepName, @Timestamp, @currentProgramID, @progress, @status)
            """,
                ("DeviceID", (long)data.DeviceID),
                ("ProgramName", data.programName),
                ("StepName", data.stepName),
                ("Timestamp", DateTime.UtcNow),
                ("currentProgramID", currentProgramID),
                ("progress", progress),
                ("status", status)
            );

            // _ = int.TryParse(data.Data.FirstOrDefault(x => x.Codename == "program_counter")?.Value, out int wash_cycle);
            // _ = int.TryParse(data.Data.FirstOrDefault(x => x.Codename == "operating_hours")?.Value, out int all_operating_time);
            // _ = int.TryParse(data.Data.FirstOrDefault(x => x.Codename == "water_spent_total")?.Value, out int all_water_consumption);
            // _ = double.TryParse(data.Data.FirstOrDefault(x => x.Codename == "weight")?.Value, System.Globalization.CultureInfo.GetCultureInfo("en-US"), out double cur_weight);
            // _ = int.TryParse(data.Data.FirstOrDefault(x => x.Codename == "program_time_minutes")?.Value, out int cur_program_time);
            // await DatabaseService.ExecuteNonQuery("""
            // INSERT INTO device_math_metrics (device_unique_id, wash_cycle, all_operating_time, all_water_consumption, cur_program_name, cur_weight, cur_program_time, added_at, cur_program_id)
            // VALUES (@DeviceID, @WashCycle, @AllOperatingTime, @AllWaterConsumption, @ProgramName, @CurWeight, @CurProgramTime, @Timestamp, @currentProgramID)
            // """,
            //     ("DeviceID", (long)data.DeviceID),
            //     ("WashCycle", wash_cycle),
            //     ("AllOperatingTime", all_operating_time),
            //     ("AllWaterConsumption", all_water_consumption),
            //     ("ProgramName", data.programName),
            //     ("CurWeight", (int)(cur_weight * 10)),
            //     ("CurProgramTime", cur_program_time),
            //     ("Timestamp", DateTime.UtcNow),
            //     ("currentProgramID", currentProgramID)
            // );
        }
        catch (Exception e)
        {
            Utils.LogUtils.LogException(e, "Error saving data to database");
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
    public static IRepository GetRepository(IRepository.RepositoryType repositoryType)
    {
        return repositoryType switch
        {
            IRepository.RepositoryType.File => new FileRepository(),
            IRepository.RepositoryType.Database => new DatabaseRepository(),
            IRepository.RepositoryType.Console => new ConsoleRepository(),
            _ => throw new ArgumentException("Invalid repository type.")
        };
    }
}
