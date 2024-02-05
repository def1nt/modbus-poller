public record RegisterInfo
{
    public string Address;
    public byte Function;
    public string Name;
    public uint PollInterval;
    public DateTime LastPoll;
    public double Multiplier;
    public int Version;

    public RegisterInfo()
    {
        Address = "";
        Function = 3;
        Name = "";
        PollInterval = 0;
        LastPoll = DateTime.MinValue;
        Multiplier = 1.0;
        Version = 0;
    }

    public override string ToString() => $"Address: {Address}\nFunction: {Function}\nName: {Name}\nPoll interval: {PollInterval}\nMultiplier: {Multiplier}\n";
}

public record RegisterData
{
    public string Name;
    public string Value;
    public DateTime Timestamp;

    public RegisterData()
    {
        Name = "";
        Value = "";
        Timestamp = DateTime.Now;
    }

    public override string ToString() => $"Name: {Name}\nValue: {Value}\nTimestamp: {Timestamp}\n";
}

public sealed class MachineParameters
{
    public int Series;
    public int Version;
    public List<RegisterInfo> Parameters;

    public MachineParameters(int series, int version = 0)
    {
        Series = series;
        Version = version;
        IRegisterInfoProvider modelParametersProvider = new SQLRegisterInfoProvider(Series, Version);
        Parameters = new List<RegisterInfo>();
        Parameters = modelParametersProvider.GetParameters()
                                            .GroupBy(p => p.Name)
                                            .Select(g => g.FirstOrDefault(p => p.Version == Version) ?? g.First(p => p.Version == 0))
                                            .ToList();
    }

    public override string ToString()
    {
        var result = new System.Text.StringBuilder();
        result.AppendLine($"Model: {Series}");
        foreach (var parameter in Parameters)
        {
            result.AppendLine(parameter.ToString());
        }
        result.AppendLine();
        return result.ToString();
    }
}

public sealed class MachineData
{
    public ulong DeviceID;
    public string Model;
    public string programName;
    public string stepName;
    public List<RegisterData> Data;

    public MachineData(ulong deviceId)
    {
        DeviceID = deviceId;
        Model = string.Empty;
        programName = string.Empty;
        stepName = string.Empty;
        Data = new List<RegisterData>() { };
    }

    public override string ToString()
    {
        var result = new System.Text.StringBuilder();
        result.AppendLine($"Model: {DeviceID}");
        result.AppendLine($"Program name: {programName}");
        result.AppendLine($"Step name: {stepName}");
        foreach (var data in Data)
        {
            result.AppendLine(data.ToString());
        }
        result.AppendLine();
        return result.ToString();
    }
}
