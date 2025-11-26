using System.Runtime.InteropServices;
using System.Security.Cryptography;
namespace OpenCrossoutProtocol;
internal static class ChaChaHelper
{
    public static byte[] EncryptChaCha20(byte[] plainText, byte[] nonce, byte[] key, uint counter)
    {
        return ProcessChaCha20(plainText, nonce, key, counter);
    }
    public static void EncryptChaCha20(byte[] plainText, byte[] nonce, byte[] key, uint counter, out byte[] data)
    {
        data = ProcessChaCha20(plainText, nonce, key, counter);
    }

    public static byte[] DecryptChaCha20(byte[] cipherText, byte[] nonce, byte[] key, uint counter)
    {
        return ProcessChaCha20(cipherText, nonce, key, counter);
    }
    public static void DecryptChaCha20(byte[] cipherText, byte[] nonce, byte[] key, uint counter, out byte[] data)
    {
        data = ProcessChaCha20(cipherText, nonce, key, counter);
    }

    //логика
    private static byte[] ProcessChaCha20(byte[] input, byte[] nonce, byte[] key, uint ic)
    {
        ValidateParameters(nonce, key);

        byte[] output = new byte[input.Length];

        int result = crypto_stream_chacha20_ietf_xor_ic(
            output,
            input,
            input.Length,
            nonce,
            ic,
            key
        );

        if (result != 0) throw new CryptographicException("Ошибка обработки ChaCha20");
        return output;
    }

    private static void ValidateParameters(byte[] nonce, byte[] key)
    {
        if (nonce == null || nonce.Length != 12)
            throw new ArgumentException("Nonce должен быть 12 байт");

        if (key == null || key.Length != 32)
            throw new ArgumentException("Ключ должен быть 32 байта");
    }

    //урааа мы это сделали
    [DllImport("libsodium", CallingConvention = CallingConvention.Cdecl)]
    private static extern int crypto_stream_chacha20_ietf_xor_ic(
        byte[] output,
        byte[] input,
        long inputLength,
        byte[] nonce,
        uint ic,
        byte[] key
    );
}