var tokenSource = new CancellationTokenSource();
var token = tokenSource.Token;

Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;
    tokenSource.Cancel();
};

var tcpListener = new TCPServer();
try
{
    Console.WriteLine("Starting listener...");
    tcpListener.Start(token);
}
catch (TaskCanceledException)
{
    Console.WriteLine("Task canceled.");
}
catch (Exception e)
{
    Console.WriteLine($"tcpListener.Start() exception: {e.Message}");
    if (Environment.GetEnvironmentVariable("DEBUG") is not null)
        Console.WriteLine($"Trace: {e.StackTrace}");
}
finally
{
    Console.WriteLine("Closing listener...");
    tcpListener.Stop();
}

static class AppSettings
{
    internal static readonly string VictoriaMetricsURL = "http://192.168.105.12:4242/api/put";
    internal static readonly string PostgresConnectionString = "Host=192.168.105.12;Username=postgres;Password=sqladmin;Database=cloud_vmz";
    internal static readonly System.Net.IPAddress ListenOn = System.Net.IPAddress.Any;
}
