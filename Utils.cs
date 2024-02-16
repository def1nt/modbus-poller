using System.Globalization;

namespace Utils;

public static class StringUtils
{
    public static ushort HexStringToUShort(in string address)
    {
        return ushort.Parse(address, NumberStyles.HexNumber);
    }

    public static string ASCIIBytesToUTFString(in ushort[] ushorts)
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

    public static (Type type, int length) DecodeTypeFromString(string type)
    {
        if (type.Contains("string")) return (typeof(string), int.Parse(type.Split('[')[1].Split(']')[0]));
        return type switch
        {
            "uint16" => (typeof(ushort), 1),
            "int16" => (typeof(short), 1),
            "uint32" => (typeof(uint), 2),
            "int32" => (typeof(int), 2),
            "bool" => (typeof(bool), 1),
            _ => (typeof(ushort), 1)
        };
    }
}

public static class RegisterUtils
{
    public static uint CombineRegisters(ushort high, ushort low)
    {
        return (uint)(high << 16 | low);
    }

    public static ulong CombineRegisters(ushort[] registers, int length = 2)
    {
        ulong result = 0;
        for (int i = 0; i < length; i++)
        {
            result |= (ulong)registers[i] << (16 * i);
        }
        return result;
    }
}
