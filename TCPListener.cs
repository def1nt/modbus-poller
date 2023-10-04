using System.Net;
using System.Net.Sockets;

public interface ITCPListener
{
    public void Start(CancellationToken token = default);
    public void Stop(CancellationToken token = default);
}

public sealed class TcpListener : System.Net.Sockets.TcpListener
{
    public TcpListener(IPEndPoint localEP) : base(localEP) { }
    public bool IsRunning { get => base.Active; }
}

public sealed class TCPServer : ITCPListener // TODO: Rename
{
    // private readonly CancellationToken _token = default;
    private TcpListener _listener;

    public TCPServer()
    {
        var localEndpoint = new IPEndPoint(AppSettings.ListenOn, 8899);
        _listener = new TcpListener(localEndpoint);
    }

    public void Start(CancellationToken token = default)
    {
        Task task = StartAsync(token);
        task.Wait(CancellationToken.None);
    }

    public async Task StartAsync(CancellationToken token = default)
    {
        _listener.Start();

        Console.WriteLine("Waiting for a connection...");

        while (_listener.IsRunning && token.IsCancellationRequested == false)
        {
            var client = await _listener.AcceptTcpClientAsync(token);
            if (client is null) continue;
            Console.WriteLine($"{DateTime.Now} - Client connected from {client.Client.RemoteEndPoint}");

            var task = HandleClientAsync(client, token); // This task is fire and forget, but dont forget
        }
        _listener.Stop();
        Console.WriteLine($"{DateTime.Now} - Listener stopped");
    }

    public void Stop(CancellationToken token = default)
    {
        _listener.Stop();
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token = default)
    {
        var remoteEndPoint = client.Client?.RemoteEndPoint;
        try
        {
            Poller poller = new(client, token);
            await poller.RunAsync();
        }
        catch (Exception e)
        {
            Console.WriteLine($"HandleClientAsync() {e.GetType()} exception: {e.Message}");
            if (Environment.GetEnvironmentVariable("DEBUG") is not null)
                Console.WriteLine($"Trace: {e.StackTrace}");
        }
        finally
        {
            // Thread.Sleep(10000); // If connection is still intact, wait for 10 seconds before letting client to reconnect
            if (client.Connected) client.Close(); // Handled by Poller, but just in case
            Console.WriteLine($"{DateTime.Now} - Client {remoteEndPoint} disconnected");
        }
    }
}
