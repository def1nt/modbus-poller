public abstract class Packet
{
    public enum PacketType
    {
        Request,
        Response
    }

    protected byte[] _data;

    public PacketType Kind { get; protected init; }

    public Packet(PacketType kind)
    {
        Kind = kind;
        _data = Array.Empty<byte>();
    }

    protected static ushort CalculateCRC16(byte[] data)
    {
        ushort[] table = new ushort[256];
        const ushort polynomial = 0xA001;

        for (ushort i = 0; i < 256; i++)
        {
            ushort value = i;
            for (int j = 0; j < 8; j++)
            {
                if ((value & 1) == 1)
                {
                    value = (ushort)((value >> 1) ^ polynomial);
                }
                else
                {
                    value >>= 1;
                }
            }
            table[i] = value;
        }

        ushort crc = 0xFFFF;
        for (int i = 0; i < data.Length; i++)
        {
            byte index = (byte)(crc ^ data[i]);
            crc = (ushort)((crc >> 8) ^ table[index]);
        }

        return crc;
    }
}

public sealed class RequestPacket : Packet
{
    public byte[] Data { get => _data; }
    public RequestPacket(PacketType kind) : base(kind)
    {
        if (kind == PacketType.Request)
        {
            _data = new byte[8];
        }
        else
        {
            throw new ArgumentException("Invalid packet type");
        }
    }

    public void SetData(byte UnitID, byte FunctionCode, ushort FirstAdress, ushort NumberOfPoints)
    {
        _data[0] = UnitID;
        if (FunctionCode != 3 && FunctionCode != 1) throw new ArgumentException("Function code must always be 1 or 3");
        _data[1] = FunctionCode;
        byte[] bytes = BitConverter.GetBytes(FirstAdress);
        Array.Reverse(bytes);
        _data[2] = bytes[0];
        _data[3] = bytes[1];
        bytes = BitConverter.GetBytes(NumberOfPoints);
        Array.Reverse(bytes);
        _data[4] = bytes[0];
        _data[5] = bytes[1];
        byte[] data_t = new byte[6];
        Array.Copy(_data, data_t, 6);
        ushort crc = CalculateCRC16(data_t);
        bytes = BitConverter.GetBytes(crc);
        // Array.Reverse(bytes);
        _data[6] = bytes[0];
        _data[7] = bytes[1];
    }
}

public sealed class ResponsePacket : Packet
{
    public uint Length { get => (uint)_data.Length; }
    public byte UnitID
    {
        get
        {
            return _data[0];
        }
    }
    public byte FunctionCode // TODO: use enums
    {
        get
        {
            if (Length < 3)
                throw new ArgumentException("Invalid packet length");
            return _data[1];
        }
    }

    public byte ExceptionCode // TODO: use enums and also check if correct
    {
        get
        {
            if (FunctionCode >= 0x81)
                return _data[2];
            else return 0;
        }
    }

    public byte ByteCount
    {
        get
        {
            if (ExceptionCode == 0)
                return _data[2];
            else return 0;
        }
    }

    public ushort[] Data
    {
        get
        {
            if (Length < 4 || ByteCount == 0) return Array.Empty<ushort>();

            if (FunctionCode == 3)
            {
                ushort[] data = new ushort[ByteCount / 2]; // TODO: check if this line is correct, sometimes throws exception
                byte[] bytes = new byte[2];
                for (int i = 0; i < data.Length; i++)
                {
                    Array.Copy(_data, 3 + i * 2, bytes, 0, 2);
                    Array.Reverse(bytes); // Properly handling endianness
                    data[i] = BitConverter.ToUInt16(bytes);
                }
                return data;
            }
            else if (FunctionCode == 1)
            {
                ushort[] data = new ushort[ByteCount];
                for (int i = 0; i < data.Length; i++)
                {
                    data[i] = _data[3 + i];
                }
                return data;
            }
            return Array.Empty<ushort>();
        }
    }

    public ResponsePacket(PacketType kind) : base(kind)
    {
        if (kind != PacketType.Response)
        {
            throw new ArgumentException("Invalid packet type");
        }
    }

    public void SetData(byte[] bytes)
    {
        _data = new byte[bytes.Length];
        Array.Copy(bytes, _data, bytes.Length);
        if (!CheckCRC())
        {
            throw new ArgumentException("Invalid CRC: " + BitConverter.ToString(_data).Replace("-", " "));
        }
    }

    private bool CheckCRC()
    {
        // Cheching CRC ignoring last 2 bytes (CRC)
        byte[] data = new byte[_data.Length - 2];
        Array.Copy(_data, 0, data, 0, data.Length);
        ushort crc = CalculateCRC16(data);
        byte[] bytes = BitConverter.GetBytes(crc);
        return bytes[0] == _data[^2] && bytes[1] == _data[^1];
    }
}
