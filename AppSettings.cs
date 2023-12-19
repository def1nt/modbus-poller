static class AppSettings // TODO: Discover these from environment variables
{
    internal static readonly string VictoriaMetricsURL = "http://192.168.105.35:4242/api/put";
    internal static readonly string PostgresConnectionString = "Host=192.168.105.35:5433;Username=postgres;Password=mysimplepassword;Database=vmz_cloud";
    internal static readonly System.Net.IPAddress ListenOn = System.Net.IPAddress.Any;
}
