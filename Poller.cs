using System.Globalization;
using System.Net.Sockets;
using Security;
using Modbus;

public sealed class Poller
{
    private const ushort IDLocation = 0x1400;
    private const ushort ProgramNameLocation = 0x1B58;
    private const ushort StepNameLocation = 0x1B6C;
    private readonly CancellationToken token = default;
    private readonly IRepository _repository;
    private readonly TcpClient _tcpClient;
    private readonly NetworkStream _stream;
    private MachineParameters? machineParameters;
    private MachineData? machineData;
    private DeviceInfo? _deviceInfo;
    private int retries = 1;
    private readonly int maxRetries = 3;

    public Poller(TcpClient tcpClient, CancellationToken token = default)
    {
        this.token = token;
        _repository = RepositoryFactory.GetRepository("opentsdb");
        _tcpClient = tcpClient;
        _stream = _tcpClient.GetStream();
    }

    public async Task RunAsync()
    {
        try
        {
            await AuthenticateDevice();
            Console.WriteLine($"{DateTime.Now} - Client from {_tcpClient.Client.RemoteEndPoint} authenticated as {_deviceInfo!.DeviceName} with ID {_deviceInfo.DeviceID}");
            if (_deviceInfo!.Active == false) await InfiniteLoop();
            machineParameters = new(_deviceInfo!.SeriesID, _deviceInfo.PLCVersion);
            while (token.IsCancellationRequested == false)
            {
                var machineData = await Poll();
                await _repository.SaveData(machineData);
                await Task.Delay(5000);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"{DateTime.Now} - Poller.RunAsync() {e.GetType()} exception: {e.Message} from {machineData?.DeviceID}");
            if (Environment.GetEnvironmentVariable("DEBUG") is not null)
                Console.WriteLine($"Trace: {e.StackTrace}");
        }
        finally
        {
            await Task.Delay(10000);
            if (_tcpClient.Connected) _tcpClient.Close();
        }
    }

    private async Task<ResponsePacket> SendReceiveAsync(RequestPacket request)
    {
        byte[] buffer = new byte[256];
        int bytesRead;
        try
        {
            await _stream.WriteAsync(request.Data).AsTask().WaitAsync(TimeSpan.FromMilliseconds(1000), CancellationToken.None);
            bytesRead = await _stream.ReadAsync(buffer.AsMemory(0, 256), token).AsTask().WaitAsync(TimeSpan.FromMilliseconds(1000), CancellationToken.None);
        }
        catch (TimeoutException)
        {
            if (retries > 0)
            {
                retries -= 1;
                return await SendReceiveAsync(request);
            }
            else
            {
                throw new TimeoutException($"Modbus request timed out: {request.FunctionCode} at {request.Address}");
            }
        }
        ResponsePacket response = new();
        if (bytesRead != 0)
        {
            response.SetData(buffer.Take(bytesRead).ToArray());
            if (response.ExceptionCode != 0)
                throw new Exception($"Modbus exception code: {response.ExceptionCode} {request.FunctionCode} at {request.Address}");
        }
        return response;
    }

    private async Task AuthenticateDevice()
    {
        RequestPacket request = new(1, 3, IDLocation, 4);
        var response = await SendReceiveAsync(request);
        if (response.Data.Length != 4)
        {
            throw new System.Security.SecurityException($"Device authentication failed: invalid response length");
        }
        var series = response.Data[0];
        var id = response.Data[1];
        var secret = response.Data[2];
        var plcversion = response.Data[3];
        // combining series and id into one 32-bit integer for device id
        var deviceID = (uint)series << 16 | id;
        if ((_deviceInfo = Authenticator.AuthenticateDevice(deviceID, secret)) is null)
        {
            throw new System.Security.SecurityException($"Device authentication failed with credentials: " + deviceID + " " + secret);
        }
        _deviceInfo = _deviceInfo with { PLCVersion = plcversion };
    }

    private async Task<MachineData> Poll()
    {
        if (_deviceInfo is null) throw new System.Security.SecurityException($"Device authentication failed: device info is null");
        machineData ??= new(_deviceInfo.DeviceID);
        RequestPacket request = new();
        for (int i = 0; i < machineParameters?.Parameters.Count; i++)
        {
            var parameter = machineParameters.Parameters[i];
            if (DateTime.Now - parameter.LastPoll < TimeSpan.FromSeconds(parameter.PollInterval))
                continue;

            request.SetData(1, parameter.Function, StringToUShort(parameter.Address), 1); // TODO: Implement crutch for f1 and f3 on some addresses
            var response = await SendReceiveAsync(request);
            if (response.Data.Length == 0) continue;
            RegisterData registerData = new()
            {
                Timestamp = DateTime.Now,
                Name = parameter.Name,
                Value = (response.Data[0] * parameter.Multiplier).ToString(CultureInfo.GetCultureInfo("en-US")),
            };
            var existingDataIndex = machineData.Data.FindIndex(x => x.Name == registerData.Name);
            if (existingDataIndex != -1)
            {
                machineData.Data[existingDataIndex] = registerData;
            }
            else
            {
                machineData.Data.Add(registerData);
            }

            parameter.LastPoll = DateTime.Now;
            machineParameters.Parameters[i] = parameter;
        }
        machineData.programName = await GetString(ProgramNameLocation, 16);
        machineData.stepName = await GetString(StepNameLocation, 8);
        await LogCounters();
        if (retries < maxRetries) retries += 1;
        return machineData;
    }

    private async Task<string> GetString(ushort address, ushort count)
    {
        RequestPacket request = new(1, 3, address, count);
        var response = await SendReceiveAsync(request);
        if (response.Data.Length == count)
        {
            return ToUTFString(response.Data);
        }
        return string.Empty;
    }

    private static string ToUTFString(ushort[] ushorts)
    {
        byte[] bytes = new byte[32];
        string programNameString = string.Empty;
        int length = Math.Min(ushorts.Length, 16);
        for (int i = 0; i < length; i++) // Parsing over 16 ushorts splitting them into 2 bytes and converting them from ISO 8859-5 to utf-16 by adding 0xFEFF_0360
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

    private async Task LogCounters() // TODO: Temporary, remove later
    {
        Console.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Console.WriteLine(machineData?.DeviceID + ": " + machineData?.programName);
        Console.WriteLine(machineData?.DeviceID + ": " + machineData?.stepName);
        Console.WriteLine();

        await DebugLog(0x1B69, 1, "запусков");
        await DebugLog(0x04BC, 1, "шаг");
        await DebugLog(0x2A8, 1, "килограмм");

        Console.WriteLine();
    }

    private async Task DebugLog(ushort address, ushort count, params string[] name) => await DebugLog(address, 3, count, name);
    private async Task DebugLog(ushort address, byte function, ushort count, params string[] name)
    {
        RequestPacket request = new(1, function, address, count);
        var response = await SendReceiveAsync(request);
        if (response.Data.Length == 0) return;
        for (int i = 0; i < count; i++)
        {
            Console.WriteLine($"{machineData?.DeviceID + ": "}{address + i:X2}: {response.Data[i]} {name.ElementAtOrDefault(i) ?? string.Empty}");
        }
    }

    private static ushort StringToUShort(string address)
    {
        return ushort.Parse(address, NumberStyles.HexNumber);
    }

    private Task InfiniteLoop() => Task.Run(() => { while (token.IsCancellationRequested == false) { Thread.Sleep(1000); } });
}
