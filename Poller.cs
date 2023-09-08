using System.Globalization;
using System.Net.Sockets;

public sealed class Poller
{
    private readonly CancellationToken token = default;
    private readonly IRepository _repository;
    private readonly Security _security;
    private readonly TcpClient _tcpClient;
    private readonly NetworkStream _stream;
    private DeviceInfo? _deviceInfo;

    public Poller(TcpClient tcpClient, CancellationToken token = default)
    {
        this.token = token;
        _repository = RepositoryFactory.GetRepository("opentsdb");
        _security = new();
        _tcpClient = tcpClient;
        _stream = _tcpClient.GetStream();
    }

    public async Task RunAsync()
    {
        try
        {
            await GetRemoteID(); // TODO: This, also, does authentication, so it should be called AuthenticateDevice()
            while (token.IsCancellationRequested == false)
            {
                var machineData = await Poll();
                await _repository.SaveData(machineData);
                await Task.Delay(7000);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Poller.RunAsync() {e.GetType()} exception: {e.Message}");
            if (Environment.GetEnvironmentVariable("DEBUG") is not null)
                Console.WriteLine($"Trace: {e.StackTrace}");
        }
        finally
        {
            await Task.Delay(10000);
            if (_tcpClient.Connected) _tcpClient.Close();
        }
    }

    private async Task<ResponsePacket> SendReceiveAsync(RequestPacket request) // TODO: What if there is no response?
    {
        int bytesRead;
        byte[] buffer = new byte[256];

        await _stream.WriteAsync(request.Data).AsTask().WaitAsync(TimeSpan.FromMilliseconds(1000), CancellationToken.None);
        bytesRead = await _stream.ReadAsync(buffer.AsMemory(0, 256), token).AsTask().WaitAsync(TimeSpan.FromMilliseconds(1000), CancellationToken.None);

        var response = new ResponsePacket(Packet.PacketType.Response);
        if (bytesRead != 0)
        {
            response.SetData(buffer.Take(bytesRead).ToArray());
        }
        return response;
    }

    private async Task GetRemoteID()
    {
        RequestPacket request = new(Packet.PacketType.Request);
        request.SetData(1, 3, 0x1400, 3); // TODO: Register address may change!
        var response = await SendReceiveAsync(request);
        var series = response.Data[0];
        var id = response.Data[1];
        var secret = response.Data[2];
        if ((_deviceInfo = _security.AuthenticateDevice(series, id, secret)) is null)
        {
            throw new System.Security.SecurityException("Device authentication failed");
        }
    }

    private async Task<MachineData> Poll()
    {
        MachineData machineData = new(_deviceInfo!.DeviceID); // stub, use real ID later
        MachineParameters machineParameters = new(_deviceInfo!.DeviceID); // stub, use real ID later
        foreach (var parameter in machineParameters.Parameters)
        {
            RequestPacket request = new(Packet.PacketType.Request);
            request.SetData(1, 3, StringToUShort(parameter.Address), 1);
            var response = await SendReceiveAsync(request);
            RegisterData registerData = new()
            {
                Timestamp = DateTime.Now,
                Name = parameter.Name,
                Value = (response.Data[0] * parameter.Multiplier).ToString(CultureInfo.GetCultureInfo("en-US")), // TODO: Possible null reference
            };
            machineData.Data.Add(registerData);
        }
        machineData.programName = await GetCurrentProgramName();
        machineData.stepName = await GetCurrentStepName();
        Console.WriteLine(machineData.programName);
        Console.WriteLine(machineData.stepName);
        await GetCounters();
        return machineData;
    }

    private async Task<string> GetCurrentProgramName()
    {
        RequestPacket request = new(Packet.PacketType.Request);
        request.SetData(1, 3, 0x1B58, 16);
        var response = await SendReceiveAsync(request);
        if (response.Data.Length == 16)
        {
            return ToUTFString(response.Data);
        }
        return string.Empty;
    }

    private async Task<string> GetCurrentStepName()
    {
        RequestPacket request = new(Packet.PacketType.Request);
        request.SetData(1, 3, 0x1B6C, 8);
        var response = await SendReceiveAsync(request);
        if (response.Data.Length == 8)
        {
            return ToUTFString(response.Data);
        }
        return string.Empty;
    }

    private string ToUTFString(ushort[] ushorts)
    {
        byte[] bytes = new byte[32];
        string programNameString = string.Empty;
        for (int i = 0; i < ushorts.Length; i++) // Parsing over 16 ushorts splitting them into 2 bytes and converting them from ISO 8859-5 to utf-16 by adding 0xFEFF_0360
        {
            byte[] bytes_t = BitConverter.GetBytes(ushorts[i]);
            bytes[i * 2] = bytes_t[0];
            bytes[i * 2 + 1] = bytes_t[1];
        }
        for (int i = 0; i < 32; i++)
        {
            if (bytes[i] == 0x00) break;
            var c = (char)bytes[i];
            if (bytes[i] >= 0xA1 && bytes[i] <= 0xF1) // We're in a cyrillic range
            {
                c = (char)(c + 0xFEFF_0360);
            }
            programNameString += c;
        }
        return programNameString;
    }

    private async Task GetCounters() // TODO: Temporary, remove later
    {
        RequestPacket request = new(Packet.PacketType.Request);
        request.SetData(1, 3, 0x1B68, 1);
        var response = await SendReceiveAsync(request);
        Console.WriteLine($"0x1B68: {response.Data[0]} номер программы");
        request.SetData(1, 3, 0x1B69, 1);
        response = await SendReceiveAsync(request);
        Console.WriteLine($"0x1B69: {response.Data[0]} запусков");
        request.SetData(1, 3, 0x04BC, 1);
        response = await SendReceiveAsync(request);
        Console.WriteLine($"0x04BC: {response.Data[0]} шаг");

        request.SetData(1, 3, 0x141C, 1);
        response = await SendReceiveAsync(request);
        Console.WriteLine($"0x141C: {response.Data[0]} секунд");

        request.SetData(1, 3, 0x141E, 1);
        response = await SendReceiveAsync(request);
        Console.WriteLine($"0x141E: {response.Data[0]} часов");
    }

    private ushort StringToUShort(string address)
    {
        return ushort.Parse(address, System.Globalization.NumberStyles.HexNumber);
    }
}
