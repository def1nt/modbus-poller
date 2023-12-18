// TODO: Everything in this file will change

using System.Data;
using Npgsql;

namespace Security;

public static class Authenticator
{
    public static DeviceInfo? AuthenticateDevice(uint deviceID, ushort secret)
    {
        foreach (var device in GetDeviceList())
        {
            if (device.UniqueID == deviceID && device.Code == secret)
            {
                return device;
            }
        }
        return null;
    }

    private static IEnumerable<DeviceInfo> GetDeviceList()
    {
        using var conn = new NpgsqlConnection(AppSettings.PostgresConnectionString);
        conn.Open();
        if (conn.State != ConnectionState.Open)
        {
            throw new NpgsqlException("Could not connect to database");
        }
        using var cmd = new NpgsqlCommand("SELECT unique_id, code, number, name FROM device", conn);
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            var uniqueID = (uint)reader.GetInt64(0);
            var code = reader.GetInt32(1); // TODO: This is still varchar in the database, hello???
            var number = reader.GetString(2);
            var name = reader.GetString(3);
            yield return new DeviceInfo(uniqueID, code, uniqueID.ToString(), name);
        }
        yield return new DeviceInfo(65537, 124, "777777777777", "ВО-25.22241"); // TODO: Remove this stub
    }
}

public record DeviceInfo(uint UniqueID, int Code, string DeviceID, string DeviceName); // TODO: FOR NOW UniqueID and DeviceID are the same

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
