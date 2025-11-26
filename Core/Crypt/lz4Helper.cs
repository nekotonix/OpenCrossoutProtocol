using K4os.Compression.LZ4;
namespace OpenCrossoutProtocol;
internal static class LZ4Helper
{
    public static bool Decompress(byte[] input, out byte[] decompressedData)
    {
        decompressedData = Array.Empty<byte>();

        if (input == null || input.Length < 4)
            return false;
        uint decompSize = (uint)(input[0] | (input[1] << 8) | (input[2] << 16) | (input[3] << 24));

        if (decompSize > 0x8000000)
            return false;

        try
        {
            decompressedData = new byte[decompSize];
        }
        catch (OverflowException)
        {
            return false;
        }

        int result = LZDecompress(input, 4, input.Length - 4, decompressedData, (int)decompSize);

        return result == decompSize;
    }

    private static int LZDecompress(byte[] input, int inputOffset, int inSize, byte[] output, int outSize)
    {
        int ip = inputOffset;
        int ipEnd = ip + inSize;
        int op = 0;
        int opEnd = outSize;

        while (ip < ipEnd && op < opEnd)
        {
            byte token = input[ip++];
            int literals = token >> 4;
            if (literals == 15)
            {
                byte nextByte;
                do
                {
                    if (ip >= ipEnd) return -1;
                    nextByte = input[ip++];
                    literals += nextByte;
                } while (nextByte == 0xFF);
            }
            if (op + literals > opEnd || ip + literals > ipEnd)
                return -1;

            Array.Copy(input, ip, output, op, literals);
            op += literals;
            ip += literals;
            if (ip >= ipEnd) break;
            if (ip + 2 > ipEnd) return -1;
            ushort offset = (ushort)(input[ip] | (input[ip + 1] << 8));
            ip += 2;
            int matchLen = (token & 0x0F) + 4;
            if (matchLen == 19)
            {
                byte nextByte;
                do
                {
                    if (ip >= ipEnd) return -1;
                    nextByte = input[ip++];
                    matchLen += nextByte;
                } while (nextByte == 0xFF);
            }
            int matchPos = op - offset;
            if (matchPos < 0 || matchPos >= op)
                return -1;
            while (matchLen > 0 && op < opEnd)
            {
                if (matchPos >= opEnd) return -1;
                output[op++] = output[matchPos++];
                matchLen--;
            }
        }

        return op;
    }

    /////////////////

    public static byte[] CompressBlock(byte[] input, LZ4Level level = LZ4Level.L12_MAX)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));

        int inputLength = input.Length;
        int maxOutputSize = LZ4Codec.MaximumOutputSize(inputLength);
        byte[] output = new byte[maxOutputSize];

        int compressedLength = LZ4Codec.Encode(
            input, 0, inputLength,
            output, 0, output.Length,
            level
        );

        // Trim the output array to the actual compressed size
        byte[] compressed = new byte[compressedLength];
        Buffer.BlockCopy(output, 0, compressed, 0, compressedLength);
        return compressed;
    }

}