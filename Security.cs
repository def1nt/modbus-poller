// TODO: Everything in this file will change
// TODO: Make this into a separate namespace

using Npgsql;

public sealed class Security
{
    public DeviceInfo CheckDeviceID(string deviceID) // This checks if the machine with provided ID exists in DB and is active and allowed
    {
        if (deviceID != "VMZ-1")
        {
            throw new Exception("Invalid device ID");
        }

        return new DeviceInfo("777777777777", "VMZ-1", "Active", "VMZ-1");
    }

    private void GetMachineData(string deviceID) // This gets the data from the machine with provided ID
    {
        var connString = "Host=192.168.105.12;Username=postgres;Password=sqladmin;Database=cloud_vmz";
        using var conn = new NpgsqlConnection(connString);
        // TODO
    }

    public DeviceInfo? AuthenticateDevice(ushort series, ushort id, ushort secret)
    {
        foreach (var device in GetDeviceList())
        {
            if (device.Item1 == series && device.Item2 == id && device.Item3 == secret)
            {
                return new("777777777777", "VMZ-1", "Active", "VMZ-1"); // TODO: Return device itself
            }
        }
        return null;
    }

    public IEnumerable<(ushort, ushort, ushort)> GetDeviceList()
    {
        yield return (1, 1, 124); // Stub
        yield return (1, 2, 125); // Stub
    }

}
public record DeviceInfo(string DeviceID, string DeviceName, string DeviceStatus, string DeviceType);
