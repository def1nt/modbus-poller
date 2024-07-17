using System.Text.Json;
using Modbus;
using Utils;
using System.Net.Sockets;

/// <summary>
/// A proxy object that can do an actuall polling of the device connected to the appropriate network stream.
/// It bundles close address sequences together to poll them in a single packet.
/// All received data is then cached in a Bundle object and can be returrned to caller without extra network requests.
/// </summary>
public sealed class PollerProxy
{
    private readonly MachineParameters? machineParameters;

    private List<(int addr, byte code)> addresses = new();
    private readonly List<Bundle> addressBundles = new();
    private readonly NetworkStream _stream;

    Dictionary<(int addr, byte code), ushort> data = new();

    public PollerProxy(MachineParameters machineParameters, NetworkStream stream)
    {
        this.machineParameters = machineParameters ?? throw new ArgumentNullException(nameof(machineParameters));
        _stream = stream;
        ExtractAddresses();
        Bundle(1);
        Bundle(3);
        ExtendBundles();
    }

    private void ExtractAddresses()
    {
        if (machineParameters == null) return;

        foreach (var parameter in machineParameters.Parameters)
        {
            (int, byte) addr = (StringUtils.HexStringToUShort(parameter.Address), parameter.Function);
            addresses.Add(addr);
        }
        addresses = addresses.OrderBy(a => a.addr).ToList();
    }

    private void Bundle(int f)
    {
        int previous = 0;

        foreach (var (addr, code) in addresses.Where(a => a.code == f))
        {
            if (addr - previous <= 9)
            {
                addressBundles[^1].Add(addr);
            }
            else if (addr > previous + 1)
            {
                Bundle newBundle = new() { addr };
                newBundle.functionCode = f;
                addressBundles.Add(newBundle);
            }
            previous = addr;
        }
    }

    private void ExtendBundles()
    {
        foreach (var bundle in addressBundles)
        {
            int first = bundle[0];
            int last = bundle[^1];

            int lastLenght = StringUtils.DecodeTypeFromString(machineParameters!.Parameters.Find(x => StringUtils.HexStringToUShort(x.Address) == last)?.Type ?? "").length;

            int length = last - first + lastLenght;
            bundle.Length = length;
        }
    }

    private async Task PollBundle(Bundle bundle)
    {
        // Goind through bundles, querying data from first address with length of bundle
        // Receiving data and putting each int16 into a data dictionary with address and function as key
        int first = bundle[0];
        byte code = (byte)bundle.functionCode;
        RequestPacket request = new(1, code, (ushort)first, (ushort)bundle.Length);
        var result = await SendReceiveAsync(request);
        if (result.Data.Length < bundle.Length)
        {
            throw new InvalidOperationException("Not enough data received");
        }
        for (int i = 0; i < result.Data.Length; i++)
        {
            if (data.ContainsKey((first + i, code))) data[(first + i, code)] = result.Data[i];
            else data.Add((first + i, code), result.Data[i]);
        }
        bundle.Stale = false;
    }

    public async Task<ushort[]> GetData(RegisterInfo registerInfo)
    {
        // Looking to which bundle the address belongs
        Bundle? bundle = addressBundles.Find(x => x.Contains(StringUtils.HexStringToUShort(registerInfo.Address)) && x.functionCode == registerInfo.Function);
        if (bundle is null)
        {
            throw new Exception($"Address not found in any bundle: {registerInfo.Address}");
        }
        else if (bundle.Stale)
        {
            await PollBundle(bundle);
        }

        // Everything is fine, look for data and return it
        var size = StringUtils.DecodeTypeFromString(registerInfo.Type).length;
        var first = StringUtils.HexStringToUShort(registerInfo.Address);
        byte code = registerInfo.Function;
        ushort[] data = new ushort[size];
        for (int i = 0; i < size; i++)
        {
            data[i] = this.data[(first + i, code)];
        }
        return data;
    }

    private async Task<ResponsePacket> SendReceiveAsync(RequestPacket request, int retries = 0)
    {
        if (retries < 0)
            throw new TimeoutException($"Modbus request timed out: {request.FunctionCode} at {request.Address}");

        byte[] buffer = new byte[256];
        int bytesRead;
        CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(3000));
        try
        {
            await _stream.WriteAsync(request.Data, cts.Token);
            bytesRead = await _stream.ReadAsync(buffer.AsMemory(0, 256), cts.Token);
        }
        catch (OperationCanceledException)
        {
            return await SendReceiveAsync(request, retries - 1);
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

    public override string ToString()
    {
        return addresses.Aggregate("", (s, b) => s + b.addr + ", ") + "\n" + addressBundles.Aggregate("", (b, s) => s + b.ToString()) + $"\nOriginally addresses: {addresses.Count}; After bundling bundles: {addressBundles.Count}\n";
    }
}

public class Bundle : List<int>
{
    public int functionCode = 3;
    public int Length = 0;
    public bool Stale
    {
        get
        {
            if (stale) return true;
            else
            {
                if ((DateTime.Now - lastUpdate) > TimeSpan.FromSeconds(10))
                {
                    return stale = true;
                }
                else
                {
                    return false;
                }
            }
        }
        set
        {
            stale = value;
            if (stale == false) lastUpdate = DateTime.Now;
        }
    }
    private bool stale = true;
    private DateTime lastUpdate;
    public Bundle() : base() { }

    public override string ToString()
    {
        return $"Function: {functionCode},\tLength: {Length},\tBundle: {JsonSerializer.Serialize(this)}\n";
    }
}
