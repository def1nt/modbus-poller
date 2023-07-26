// TODO: Everything in this file will change

using Npgsql;

public sealed class Security
{
    public DeviceInfo CheckDeviceID(string deviceID) // This checks if the machine with provided ID exists in DB and is active and allowed
    {
        if (deviceID != "VMZ-1")
        {
            throw new Exception("Invalid device ID");
        }
        
        return new DeviceInfo("777777777777", "VMZ-1", "192.168.105.199", "00:00:00:00:00:00", "Active", "VMZ-1");
    }

    private void GetMachineData(string deviceID) // This gets the data from the machine with provided ID
    {
        var connString = "Host=192.168.105.12;Username=postgres;Password=sqladmin;Database=cloud_vmz";
        using var conn = new NpgsqlConnection(connString);
        // TODO
    }

}
public record DeviceInfo(string DeviceID, string DeviceName, string DeviceIP, string DeviceMAC, string DeviceStatus, string DeviceType);

