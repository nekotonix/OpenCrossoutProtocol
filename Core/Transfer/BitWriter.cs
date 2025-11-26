namespace OpenCrossoutProtocol;

internal class BitWriter : IDisposable
{
    private Stream stream;
    private byte currentByte;
    private byte mask;

    public BitWriter(Stream stream)
    {
        this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
        currentByte = 0;
        mask = 0x80; // Начальная маска: 10000000
    }

    public void WriteBit(byte bit)
    {
        if (bit > 1)
            throw new ArgumentOutOfRangeException(nameof(bit), "Bit must be 0 or 1.");

        if (mask == 0)
        {
            Flush();
        }

        if (bit == 1)
        {
            currentByte |= mask;
        }

        mask >>= 1;
    }

    public void WriteBit(Bit bit)
    {
        WriteBit(bit.Value);
    }

    public void WriteByte(byte b)
    {
        if (mask == 0x80)
        {
            stream.WriteByte(b);
        }
        else
        {
            for (int i = 7; i >= 0; i--)
            {
                byte bit = (byte)((b >> i) & 1);
                WriteBit(bit);
            }
        }
    }

    public void WriteSByte(sbyte sb)
    {
        WriteByte((byte)sb);
    }

    public void WriteBytes(byte[] bytes)
    {
        foreach (byte b in bytes)
        {
            WriteByte(b);
        }
    }

    public void Flush()
    {
        if (mask != 0x80)
        {
            stream.WriteByte(currentByte);
            currentByte = 0;
            mask = 0x80;
        }
        stream.Flush();
    }

    public void Dispose()
    {
        Flush();
        stream.Dispose();
    }
}