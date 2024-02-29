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
    Utils.LogUtils.LogException(e, "tcpListener.Start()");
}
finally
{
    Console.WriteLine("Closing listener...");
    tcpListener.Stop();
}
