static class AppSettings
{
    internal static readonly string VictoriaMetricsURL = "http://192.168.105.12:4242/api/put";
    internal static readonly string PostgresConnectionString = "Host=192.168.105.12;Username=postgres;Password=sqladmin;Database=cloud_vmz";
    internal static readonly System.Net.IPAddress ListenOn = System.Net.IPAddress.Any;
}
