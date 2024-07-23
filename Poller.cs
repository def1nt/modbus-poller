using System.Globalization;
using System.Net.Sockets;
using Security;
using Modbus;
using Utils;
using System.Security;

public sealed class Poller
{
    private const ushort IDLocation = 0x1400;
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
        _repository = RepositoryFactory.GetRepository(IRepository.RepositoryType.Database);
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
            LogUtils.LogException(e, $"Poller.RunAsync() {e.GetType()} from {machineData?.DeviceID}");
            if (e is SecurityException)
                await Task.Delay(30000);
        }
        finally
        {
            if (_tcpClient.Connected) _tcpClient.Close();
        }
    }

    private async Task<ResponsePacket> SendReceiveAsync(RequestPacket request)
    {
        byte[] buffer = new byte[256];
        int bytesRead;
        CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(2000));
        try
        {
            await ClearStreamAsync(_stream);
            await _stream.WriteAsync(request.Data, cts.Token);
            bytesRead = await _stream.ReadAsync(buffer.AsMemory(0, 256), cts.Token);
        }
        catch (OperationCanceledException)
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
            // trying to detect if the packet was duplicated because of previous read timeout
            if (bytesRead % 4 == 0 && buffer.Take(bytesRead / 4).SequenceEqual(buffer.Skip(bytesRead * 3 / 4).Take(bytesRead / 4)))
            {
                bytesRead /= 4;
            }
            else if (bytesRead % 2 == 0 && buffer.Take(bytesRead / 2).SequenceEqual(buffer.Skip(bytesRead / 2).Take(bytesRead / 2)))
            {
                bytesRead /= 2;
            }
            else if (bytesRead % 3 == 0 && buffer.Take(bytesRead / 3).SequenceEqual(buffer.Skip(bytesRead * 2 / 3).Take(bytesRead / 3)))
            {
                bytesRead /= 3;
            }
            response.SetData(buffer.Take(bytesRead).ToArray());
            if (response.ExceptionCode != 0)
                throw new Exception($"Modbus exception code: {response.ExceptionCode} {request.FunctionCode} at {request.Address}");
        }
        return response;
    }

    private static async Task ClearStreamAsync(Stream stream)
    {
        byte[] buffer = new byte[1024]; // Buffer size can be adjusted as needed
        while (stream is NetworkStream ns && ns.DataAvailable)
        {
            await stream.ReadAsync(buffer);
        }
    }

    private async Task AuthenticateDevice()
    {
        RequestPacket request = new(1, 3, IDLocation, 4);
        ResponsePacket response;
        try
        {
            response = await SendReceiveAsync(request);
        }
        catch (TimeoutException)
        {
            throw new SecurityException("Device authentication failed: could not get ID data from client");
        }
        if (response.Data.Length != 4)
        {
            throw new SecurityException($"Device authentication failed: invalid response length");
        }
        var series = response.Data[0];
        var id = response.Data[1];
        var secret = response.Data[2];
        var plcversion = response.Data[3];
        // await DebugLog(IDLocation, 4, "series", "id", "secret", "plcversion");
        // combining series and id into one 32-bit integer for device id
        var deviceID = (uint)series << 16 | id;
        if ((_deviceInfo = Authenticator.AuthenticateDevice(deviceID, secret)) is null)
        {
            throw new SecurityException($"Device authentication failed: wrong credentials: " + deviceID + " " + secret);
        }
        _deviceInfo = _deviceInfo with { PLCVersion = plcversion };
    }

    private async Task<MachineData> Poll()
    {
        if (_deviceInfo is null) throw new SecurityException($"Device authentication failed: device info is null");
        machineData ??= new(_deviceInfo.DeviceID);
        PollerProxy proxy = new(machineParameters!, _stream);
        for (int i = 0; i < machineParameters?.Parameters.Count; i++)
        {
            var parameter = machineParameters.Parameters[i];
            if (DateTime.Now - parameter.LastPoll < TimeSpan.FromSeconds(parameter.PollInterval))
                continue;
            var (type, length) = StringUtils.DecodeTypeFromString(parameter.Type);

            ushort[] Data = await proxy.GetData(machineParameters.Parameters[i]);

            if (Data.Length == 0) continue;
            RegisterData registerData = new()
            {
                Timestamp = DateTime.Now,
                Name = parameter.Name,
                Codename = parameter.Codename,
                Value = type switch
                { // Remember: word order is reversed in modbus
                    Type t when t == typeof(ushort) => (Data[0] * parameter.Multiplier).ToString(CultureInfo.GetCultureInfo("en-US")),
                    Type t when t == typeof(short) => (((short)Data[0]) * parameter.Multiplier).ToString(CultureInfo.GetCultureInfo("en-US")),
                    Type t when t == typeof(uint) => (RegisterUtils.CombineRegisters(Data[1], Data[0]) * parameter.Multiplier).ToString(CultureInfo.GetCultureInfo("en-US")),
                    Type t when t == typeof(int) => (((int)RegisterUtils.CombineRegisters(Data[1], Data[0])) * parameter.Multiplier).ToString(CultureInfo.GetCultureInfo("en-US")),
                    Type t when t == typeof(bool) => (Data[0] & 1).ToString(CultureInfo.GetCultureInfo("en-US")),
                    Type t when t == typeof(string) => StringUtils.ASCIIBytesToUTFString(Data),
                    Type t when t == typeof(byte) => Data.Reverse().Aggregate("", (s, c) => s + c.ToString()), // String would be LE without Reverse; ReadFunction was 1, Data is bit flags, filler implementation
                    _ => (Data[0] * parameter.Multiplier).ToString(CultureInfo.GetCultureInfo("en-US"))
                }
            };
            var existingDataIndex = machineData.Data.FindIndex(x => x.Codename == registerData.Codename);
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
        machineData.programName = machineData.Data.FirstOrDefault(p => p.Codename == "program_name")?.Value ?? "Unknown";
        machineData.stepName = machineData.Data.FirstOrDefault(p => p.Codename == "step_name")?.Value ?? "Unknown";
        // await LogCounters();
        if (retries < maxRetries) retries += 1;
        await Task.CompletedTask;
        return machineData;
    }

    private async Task LogCounters() // TODO: Temporary, remove later
    {
        Console.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Console.WriteLine(machineData?.DeviceID + ": " + machineData?.programName);
        Console.WriteLine(machineData?.DeviceID + ": " + machineData?.stepName);
        Console.WriteLine();

        await Task.CompletedTask;
        // await DebugLog(0x04BC, 1, "шаг");
        // await DebugLog(0x20A, 1, "номер программы");
        // Console.WriteLine();
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

    private Task InfiniteLoop() => Task.Run(async () => { while (token.IsCancellationRequested == false) { await Task.Delay(1000); } });
}
