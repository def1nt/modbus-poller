public struct RegisterInfo
{
    public string Address;
    public byte Function;
    public string Name;
    public uint PollInterval;
    public DateTime LastPoll;
    public double Multiplier;

    public RegisterInfo()
    {
        Address = "";
        Function = 3;
        Name = "";
        PollInterval = 0;
        LastPoll = DateTime.MinValue;
        Multiplier = 1.0;
    }

    public readonly override string ToString() => $"Address: {Address}\nFunction: {Function}\nName: {Name}\nPoll interval: {PollInterval}\nMultiplier: {Multiplier}\n";
}

public struct RegisterData
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

    public readonly override string ToString() => $"Name: {Name}\nValue: {Value}\nTimestamp: {Timestamp}\n";
}

public sealed class MachineParameters
{
    public int Series;
    public List<RegisterInfo> Parameters;

    public MachineParameters(int series)
    {
        Series = series;
        IRegisterInfoProvider modelParametersProvider = new SQLRegisterInfoProvider(Series);
        Parameters = new List<RegisterInfo>();
        Parameters.AddRange(modelParametersProvider.GetParameters());
    }

    public override string ToString()
    {
        var result = new System.Text.StringBuilder();
        result.AppendLine($"Model: {Series}");
        foreach (var parameter in Parameters)
        {
            result.AppendLine($"Address: {parameter.Address}");
            result.AppendLine($"Name: {parameter.Name}");
            result.AppendLine($"Poll interval: {parameter.PollInterval}");
            result.AppendLine();
        }
        return result.ToString();
    }
}

public sealed class MachineData
{
    public string DeviceID;
    public string Model;
    public string programName;
    public string stepName;
    public List<RegisterData> Data;

    public MachineData(string deviceId)
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
            result.AppendLine($"Name: {data.Name}");
            result.AppendLine($"Value: {data.Value}");
            result.AppendLine($"Timestamp: {data.Timestamp}");
            result.AppendLine();
        }
        return result.ToString();
    }
}
