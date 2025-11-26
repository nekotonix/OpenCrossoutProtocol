namespace OpenCrossoutProtocol;
public class Bit { public byte Value { get; set; } }

internal class BitReader : IDisposable
{
    private Stream stream;
    private byte currentByte;
    private byte mask; //маска для чтения следующего бита

    public Stream BaseStream => stream;

    public BitReader(Stream stream)
    {
        this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
        mask = 0;
    }

    public byte ReadBit()
    {
        if (mask == 0)
        {
            int read = stream.ReadByte();
            if (read == -1)
            {
                throw new EndOfStreamException();
            }
            currentByte = (byte)read;
            mask = 0x80;
        }

        byte bit = (currentByte & mask) != 0 ? (byte)1 : (byte)0;
        mask >>= 1;
        return bit;
    }
    public Bit ReadBit_rBit()
    {
        return new Bit { Value = ReadBit() };
    }

    public byte ReadByte()
    {
        if (mask == 0)
        {
            int read = stream.ReadByte();
            if (read != -1)
            {
                return (byte)read;
            }
        }

        byte result = 0;
        for (int i = 0; i < 8; i++)
        {
            result <<= 1;
            result |= ReadBit();
        }
        return result;
    }
    public sbyte ReadSByte()
    {
        return (sbyte)ReadByte();
    }
    public byte[] ReadBytes(int count)
    {
        byte[] bytes = new byte[count];
        for (int i = 0; i < count; i++)
        {
            bytes[i] = ReadByte();
        }
        return bytes;
    }
    public byte[] ReadBytes(ulong count)
    {
        byte[] bytes = new byte[count];
        for (ulong i = 0; i < count; i++)
        {
            bytes[i] = ReadByte();
        }
        return bytes;
    }
    public void Dispose()
    {
        stream.Dispose();
    }
}