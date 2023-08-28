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
