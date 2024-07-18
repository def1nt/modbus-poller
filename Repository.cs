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
    public DatabaseRepository(CancellationToken token = default)
    {
        _token = token;
    }

    public async Task SaveData(MachineData data)
    {
        // Using http to connect to OpenTSDB instance
        var client = new HttpClient();
        client.BaseAddress = new Uri(AppSettings.VictoriaMetricsURL);
        Data _data = new()
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
            _ = int.TryParse(data.Data.FirstOrDefault(x => x.Codename == "program_counter")?.Value, out int wash_cycle);
            _ = int.TryParse(data.Data.FirstOrDefault(x => x.Codename == "errors")?.Value, out int errorCode);
            if (errorCode != 0)
            {
                var errCount = await DatabaseService.ExecuteScalar<long>($"""
                SELECT count(*) FROM device_errs 
                WHERE
                device_unique_id={(long)data.DeviceID}  
                AND err_id={errorCode}
                AND w_cycle={wash_cycle}
                AND COALESCE(program_name,'')='{data.programName}' 
                AND COALESCE(program_step,'')='{data.stepName}'
                """);

                if (errCount == 0)
                {
                    await DatabaseService.ExecuteNonQuery("""
                    INSERT INTO device_errs (device_unique_id, err_id, w_cycle, added_at, program_name, program_step)
                    VALUES (@DeviceID, @Error, @Cycle, @Timestamp, @program_name, @program_step)
                    """,
                        ("DeviceID", (long)data.DeviceID),
                        ("Error", errorCode),
                        ("Cycle", int.Parse(data.Data.FirstOrDefault(x => x.Codename == "program_counter")?.Value ?? "0")),
                        ("Timestamp", DateTime.UtcNow),
                        ("program_name", data.programName),
                        ("program_step", data.stepName)
                    );
                }
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
                progress = 0;
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

            if (autoControl != 1) return;

            _ = int.TryParse(data.Data.FirstOrDefault(x => x.Codename == "operating_hours")?.Value, out int total_operating_time);
            _ = int.TryParse(data.Data.FirstOrDefault(x => x.Codename == "water_spent_total")?.Value, out int total_water_consumption);
            _ = int.TryParse(data.Data.FirstOrDefault(x => x.Codename == "power_spent_total")?.Value, out int total_power_consumption);
            _ = double.TryParse(data.Data.FirstOrDefault(x => x.Codename == "weight")?.Value, System.Globalization.CultureInfo.GetCultureInfo("en-US"), out double cur_weight);
            await DatabaseService.ExecuteNonQuery("""
            INSERT INTO device_math_metrics (device_unique_id, wash_cycle, total_operating_time, total_water_spent, total_power_spent, cur_program_id, cur_program_name, cur_weight, added_at)
            VALUES (@DeviceID, @WashCycle, @TotalOperatingTime, @TotalWaterSpent, @TotalPowerSpent, @currentProgramID, @ProgramName, @CurWeight, @Timestamp)
            """,
                ("DeviceID", (long)data.DeviceID),
                ("WashCycle", wash_cycle),
                ("TotalOperatingTime", total_operating_time),
                ("TotalWaterSpent", total_water_consumption),
                ("TotalPowerSpent", total_power_consumption),
                ("currentProgramID", currentProgramID),
                ("ProgramName", data.programName),
                ("CurWeight", (int)(cur_weight * 10)),
                ("Timestamp", DateTime.UtcNow)
            );

            var msList = new List<string>() { "ms1_spent", "ms2_spent", "ms3_spent", "ms4_spent", "ms5_spent", "ms6_spent", "ms7_spent", "ms8_spent", "ms9_spent" };
            int[] msInts = new int[9];
            for (int i = 0; i < msList.Count; i++)
            {
                var ms = msList[i];
                _ = int.TryParse(data.Data.FirstOrDefault(x => x.Codename == ms)?.Value, out msInts[i]);
            }
            if (msInts.All(m => m == 0)) return; // Если моющие средства все по нулям - мы их не записываем

            _ = int.TryParse(data.Data.FirstOrDefault(x => x.Codename == "current_step")?.Value, out int stepNumber);

            int c = await DatabaseService.ExecuteScalar<int>($"SELECT COUNT (*) FROM device_cleaners WHERE device_unique_id = {(long)data.DeviceID} AND wash_cycle = {wash_cycle} AND step_number = {stepNumber}");
            if (c == 0)
            {
                await DatabaseService.ExecuteNonQuery("""
                INSERT INTO device_cleaners (device_unique_id, wash_cycle, ms1, ms2, ms3, ms4, ms5, ms6, ms7, ms8, ms9, added_at, step_number)
                VALUES (@DeviceID, @wash_cycle, @ms1, @ms2, @ms3, @ms4, @ms5, @ms6, @ms7, @ms8, @ms9, @DateTime, @StepNumber)
                """,
                    ("DeviceID", (long)data.DeviceID),
                    ("wash_cycle", wash_cycle),
                    ("ms1", msInts[0]),
                    ("ms2", msInts[1]),
                    ("ms3", msInts[2]),
                    ("ms4", msInts[3]),
                    ("ms5", msInts[4]),
                    ("ms6", msInts[5]),
                    ("ms7", msInts[6]),
                    ("ms8", msInts[7]),
                    ("ms9", msInts[8]),
                    ("DateTime", DateTime.UtcNow),
                    ("StepNumber", stepNumber)
                );
            }
            else
            {
                await DatabaseService.ExecuteNonQuery("""
                UPDATE device_cleaners SET ms1 = @ms1, ms2 = @ms2, ms3 = @ms3, ms4 = @ms4, ms5 = @ms5, ms6 = @ms6, ms7 = @ms7, ms8 = @ms8, ms9 = @ms9, added_at = @DateTime
                WHERE device_unique_id = @DeviceID AND wash_cycle = @wash_cycle AND step_number = @StepNumber
                """,
                    ("DeviceID", (long)data.DeviceID),
                    ("wash_cycle", wash_cycle),
                    ("ms1", msInts[0]),
                    ("ms2", msInts[1]),
                    ("ms3", msInts[2]),
                    ("ms4", msInts[3]),
                    ("ms5", msInts[4]),
                    ("ms6", msInts[5]),
                    ("ms7", msInts[6]),
                    ("ms8", msInts[7]),
                    ("ms9", msInts[8]),
                    ("DateTime", DateTime.UtcNow),
                    ("StepNumber", stepNumber)
                );
            }

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
