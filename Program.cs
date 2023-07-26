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
    tcpListener.Start(token);
}
catch (TaskCanceledException)
{
    Console.WriteLine("Task canceled.");
}
catch (Exception e)
{
    Console.WriteLine($"tcpListener.Start() exception: {e.Message}");
}
finally
{
    tcpListener.Stop();
}
