// TODO: Everything in this file will change

namespace Security;

public static class Authenticator
{
    public static DeviceInfo? AuthenticateDevice(ulong deviceID, ushort secret)
    {
        if (deviceID == 65537) deviceID = 777777777777; // TODO: Remove this stub
        foreach (var device in GetDeviceList())
        {
            if (device.DeviceID == deviceID && device.Code == secret)
            {
                return device;
            }
        }
        return null;
    }

    private static IEnumerable<DeviceInfo> GetDeviceList()
    {
        var reader = DatabaseService.GetDataReader("SELECT unique_id, code, series_id, name, active FROM device");

        while (reader.Read())
        {
            var deviceID = (ulong)reader.GetInt64(0);
            var code = reader.GetInt32(1);
            var seriesID = reader.GetInt32(2);
            var name = reader.GetString(3);
            var active = reader.IsDBNull(4) || reader.GetBoolean(4);
            yield return new DeviceInfo(deviceID, code, seriesID, 0, name, active);
        }
    }
}

public record DeviceInfo(ulong DeviceID, int Code, int SeriesID, int PLCVersion, string DeviceName, bool Active); // TODO: FOR NOW UniqueID and DeviceID are the same

/*
    id integer NOT NULL DEFAULT nextval('device_id_seq'::regclass),
    name character varying COLLATE pg_catalog."default" NOT NULL,
    series_id integer,
    "number" character varying COLLATE pg_catalog."default" NOT NULL,
    code character varying COLLATE pg_catalog."default" NOT NULL,
    added_at timestamp without time zone,
    location json,
    unique_id bigint NOT NULL,
*/
