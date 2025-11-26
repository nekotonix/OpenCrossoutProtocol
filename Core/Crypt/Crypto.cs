using Sodium;
using System.Buffers.Binary;
namespace OpenCrossoutProtocol;
public class Crypto
{
    private readonly KeyPair ed25519_keyPair_server;
    private readonly KeyPair x25519_keypair_server;
    //keyPair.PublicKey
    //keyPair.PrivateKey

    //client
    public byte[] ed25519_public_client = new byte[32];
    public byte[] ed25519_client_sign = new byte[64];
    public byte[]? x25519_public_client = null;

    //chacha
    private byte[]? chachaKeyClient = null; //для расшифровки данных от клиента
    private byte[]? chachaKeyServer = null; //для шифровки данных для клиента

    public static readonly byte[] NOENC_HEADER = new byte[4] { 0xAE, 0xAE, 0xAE, 0xAE };
    private static readonly byte[] POLY_HEADER_NULLS = new byte[4];
    private static readonly uint CHACHA_COUNTER_F_POLY = 0;
    private static readonly uint CHACHA_COUNTER_F_DATA = 1;
    private static readonly uint CHACHA_COUNTER_F_TYPE = 0;
    private static readonly byte[] CHACHA_KEY_EXTENSION_BYTES = new byte[4];
    private static readonly byte[] POLY_KEYGEN_DATA = new byte[32];
    public static readonly uint TCP_COMPRESSFROM = 512;

    public Crypto(byte[]? pubkey = null, byte[]? privkey = null)
    {
        if (pubkey == null || privkey == null)
        {
            ed25519_keyPair_server = PublicKeyAuth.GenerateKeyPair();
        }
        else
        {
            /*if (privateEdKey.Length != 64)
            {
                throw new Exception("PROVIDED PRIVATE KEY DOES NOT HAVE LENGHT OF 64 BYTES.");
            }
            byte[] publicEdKey = PublicKeyAuth.ExtractEd25519PublicKeyFromEd25519SecretKey(privateEdKey);*/
            //ed25519_keyPair_server = PublicKeyAuth.GenerateKeyPair(seed);
            ed25519_keyPair_server = new KeyPair(pubkey, privkey);
        }
        x25519_keypair_server = PublicKeyBox.GenerateKeyPair();
    }

    public byte[] GetServerPublicEd25519()
    {
        return ed25519_keyPair_server.PublicKey; //гениальный ход
    }
    public byte[] GetServerPublicX25519()
    {
        return x25519_keypair_server.PublicKey; //гениальный ход
    }

    public byte[] ed25519_sign(byte[] dataToSign)
    {
        return PublicKeyAuth.SignDetached(dataToSign, ed25519_keyPair_server.PrivateKey);
    }

    public bool ed25519_verifDetached(byte[] signature, byte[] data)
    {
        return PublicKeyAuth.VerifyDetached(signature, data, ed25519_public_client);
    }

    public byte[] GetCombinedDataForVerif()
    {
        if (x25519_public_client == null) throw new Exception("CLIENT KEY NOT FOUND");
        return GetServerPublicEd25519()
            .Concat(x25519_public_client)
            .ToArray();
    }

    private static byte[] XorByteArrays(byte[] array1, byte[] array2)
    {
        if (array1.Length != array2.Length)
        {
            throw new ArgumentException("Byte arrays must be of the same length.");
        }

        byte[] result = new byte[array1.Length];

        for (int i = 0; i < array1.Length; i++)
        {
            result[i] = (byte)(array1[i] ^ array2[i]);
        }

        return result;
    }

    public bool CalculateChachaKeys() //Правильно калькулируются
    {
        try
        {
            if (x25519_public_client == null) return false;
            byte[] tempShared = ScalarMult.Mult(x25519_keypair_server.PrivateKey, x25519_public_client);

            chachaKeyClient = XorByteArrays(tempShared, x25519_keypair_server.PublicKey);
            chachaKeyServer = XorByteArrays(tempShared, x25519_public_client);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public byte[] GeneratePoly1305Mac(byte[] polyKey, byte[] headers, byte[] dataBytes)
    {
        if (headers.Length != 12) { return Array.Empty<byte>(); }
        byte[] combinedData = new byte[headers.Length + dataBytes.Length];
        Buffer.BlockCopy(headers, 0, combinedData, 0, headers.Length);
        Buffer.BlockCopy(dataBytes, 0, combinedData, headers.Length, dataBytes.Length);
        byte[] mac = OneTimeAuth.Sign(combinedData, polyKey);
        byte[] truncatedMac = new byte[4];
        Array.Copy(mac, 0, truncatedMac, 0, 4);

        return truncatedMac;
    }

    private List<byte> cachedPrevious = new List<byte>();
    private bool isDrobbled = false;
    private ulong zPartsCount = 0;

    public (List<message_basic> Messages, ulong FinalCounter) decryptMultipleMessagesClient(ulong initialCounter, byte[] messagesBytes, int? messageLength = null)
    {
        List<message_basic> messages = new();
        uint offset = 0;
        ulong currentCounter = initialCounter;
        initialCounter += 1; //Так как одно уже схавали

        messagesBytes = cachedPrevious.Concat(messagesBytes).ToArray();
        cachedPrevious.Clear();
        int _messageLength = (messageLength == null) ? messagesBytes.Length : (int)messageLength;

        while (offset < _messageLength)
        {

            if (offset + 12 > messagesBytes.Length)
            {
                throw new InvalidOperationException("Insufficient data for message headers.");
            }
            byte[] dataSizeBytes = new byte[4];
            Array.Copy(messagesBytes, offset + 4, dataSizeBytes, 0, 3);
            dataSizeBytes[3] = 0x00;
            uint dataSize = BinaryPrimitives.ReadUInt32LittleEndian(dataSizeBytes); //Int24!!!!!!

            uint totalMessageLength = 12 + dataSize;
            if (offset + totalMessageLength > messagesBytes.Length)
            {
                cachedPrevious = messagesBytes.Skip((int)offset).ToList();
                isDrobbled = true; //!!ДРОБЛЕНИЕ НЕ ДОЛЖНО БЫТЬ ЗДЕСЬ!! СТОИТ ВЫНЕСТИ ОТСЮДА
                zPartsCount += 1;
                if (messages.Count() < 1) initialCounter -= 1;
                return (messages, currentCounter - initialCounter);
            }
            byte[] currentMessage = new byte[totalMessageLength];
            Array.Copy(messagesBytes, offset, currentMessage, 0, totalMessageLength);
            if (isDrobbled)
            {
                currentCounter -= zPartsCount;
            }
            message_basic? message = decryptSingleMessageClient(currentCounter, currentMessage);
            cachedPrevious = new List<byte>();
            if (message == null) //временный костыль
            {
                bool isHad = false;
                ulong checkingDepth = 4;
                //Console.Write($" {currentCounter} ");
                ulong zcurrentCounter = currentCounter - checkingDepth / 2;
                ulong i22 = 1;
                for (; i22 <= checkingDepth; i22++)
                {
                    message = decryptSingleMessageClient(zcurrentCounter + i22, currentMessage);
                    //Console.Write($" {zcurrentCounter + i22} ");
                    if (message != null)
                    {
                        Logger.logl($"You've invalid counter {currentCounter}, but valid is {zcurrentCounter + i22}", (byte)Logger.LogType.DebugInfo);
                        Logger.logl($"I'm fixed it for you, but be careful, it is dangerous.", (byte)Logger.LogType.DebugInfo);
                        //stranger
                        isHad = true;
                        break;
                    }
                }
                if (!isHad) Logger.logl("Valid counter not found?? possible corrupted data", (byte)Logger.LogType.Error);
                else currentCounter = zcurrentCounter + i22;
                //throw new InvalidOperationException("Failed to decrypt message.");
            }

            if (message != null && message.dataSize != dataSize)
            {
                throw new InvalidOperationException("Decrypted data size does not match header value.");
            }
            if (message != null) messages.Add(message);
            currentCounter += 1;
            zPartsCount = 0;
            offset += totalMessageLength;
            isDrobbled = false;
        }
        return (messages, currentCounter - initialCounter);
    }


    public static Dictionary<string, int> FLAG_BITS = new Dictionary<string, int>
    {
        { "lz4compressed", 6 },
        //TODO: выяснить другие
    };

    public static bool GetBit(byte number, int bitIndex)
    {
        if (bitIndex < 0 || bitIndex >= 8)
            throw new ArgumentOutOfRangeException(nameof(bitIndex), "Bit position must be between 0 and 7.");

        return (number & (1 << bitIndex)) != 0;
    }
    public static byte SetBit(byte value, int bitPosition, bool set)
    {
        if (bitPosition < 0 || bitPosition >= 8)
            throw new ArgumentOutOfRangeException(nameof(bitPosition), "Bit position must be between 0 and 7.");

        byte mask = (byte)(1 << bitPosition);
        return set ? (byte)(value | mask) : (byte)(value & ~mask);
    }

    public message_basic? decryptSingleMessageClient(ulong counter, byte[] message)
    {
        //хедеры
        byte[] poly_mac_first4bytes, message_type_c, data_c, data_size_b, data_flags_b;
        UInt24 data_size;
        byte data_flags;
        bool isPolyValid = true;
        using (var stream = new MemoryStream(message))
        using (var reader = new BinaryReader(stream))
        {
            poly_mac_first4bytes = reader.ReadBytes(4);
            data_size_b = reader.ReadBytes(3);
            data_size = new UInt24(BitConverter.ToUInt32(data_size_b.Append((byte)0x00).ToArray()));
            data_flags_b = reader.ReadBytes(1);
            data_flags = data_flags_b[0];

            message_type_c = reader.ReadBytes(4);
            data_c = reader.ReadBytes((int)(uint)data_size); // Явное преобразование в int
        }
        if (poly_mac_first4bytes.SequenceEqual(NOENC_HEADER))
        {
            return new message_basic
            {
                polySign = poly_mac_first4bytes,
                dataSize = data_size,
                flags = data_flags,
                requestType = BinaryPrimitives.ReadUInt16LittleEndian(message_type_c.AsSpan(0, 2)),
                requestChannel = message_type_c[2],
                requestCache = message_type_c[3],
                data = data_c,
                isPolyValid = isPolyValid,
            };
        }
        if (chachaKeyClient == null)
        {
            if (!CalculateChachaKeys())
            {
                Logger.logl("DECRYPTOR Отсутствуют ключи", (byte)Logger.LogType.Error);
                return null;
            }
            ;
        }
        //Верификация милкшейка
        byte[] polyNonce = CHACHA_KEY_EXTENSION_BYTES.Concat(BitConverter.GetBytes(ulong.MaxValue - counter)).ToArray();
        if (chachaKeyClient == null) return null;
        byte[] polyKey = ChaChaHelper.EncryptChaCha20(POLY_KEYGEN_DATA, polyNonce, chachaKeyClient, CHACHA_COUNTER_F_POLY);
        byte[] headers = POLY_HEADER_NULLS.Concat(data_size_b).Concat(data_flags_b).Concat(message_type_c).ToArray();
        byte[] polyMac = GeneratePoly1305Mac(polyKey, headers, data_c);
        if (!polyMac.SequenceEqual(poly_mac_first4bytes))
        {
            Logger.logl("WARN: Неверная хеш-сумма пакета (потеря данных? неправильный nonce?)", (byte)Logger.LogType.Warning);
            isPolyValid = false; //устарело
            return null;
        }
        ;

        //расшифровочка
        byte[] Nonce = BitConverter.GetBytes(counter); //!!counter не от содиумов, а от запросиков
        byte[] type = ChaChaHelper.DecryptChaCha20(message_type_c, CHACHA_KEY_EXTENSION_BYTES.Concat(Nonce).ToArray(), chachaKeyClient, CHACHA_COUNTER_F_TYPE);
        byte[] data = ChaChaHelper.DecryptChaCha20(data_c, CHACHA_KEY_EXTENSION_BYTES.Concat(Nonce).ToArray(), chachaKeyClient, CHACHA_COUNTER_F_DATA);

        ushort reqType = BinaryPrimitives.ReadUInt16LittleEndian(type.AsSpan(0, 2));
        byte reqChannel = type[2];
        byte reqCache = type[3]; //idk, possible cache or size is u24 (u16 in current realization)

        //флаги
        if (GetBit(data_flags, FLAG_BITS["lz4compressed"]) == true) //flag.lz4compressed
        {
            byte[] data_p = data;
            LZ4Helper.Decompress(data_p, out data);
            //Logger.logl("!Decompressed LZ4", (byte)Logger.LogType.DebugInfo);
        }
        ///////////////////////////////////
        return new message_basic
        {
            polySign = poly_mac_first4bytes,
            dataSize = data_size,
            flags = data_flags,
            requestType = reqType,
            requestChannel = reqChannel,
            requestCache = reqCache,
            data = data,
            isPolyValid = isPolyValid,
        };
    }


    public byte[]? cryptSingleMessageClient(ulong counter, message_basic messageToEncrypt)
    {
        if (messageToEncrypt.cryptLevel == 0 || messageToEncrypt.polySign.SequenceEqual(NOENC_HEADER))
        {
            return prepareSingleMessageClient(messageToEncrypt);
        }

        if (messageToEncrypt.data.Length > TCP_COMPRESSFROM)
        {
            byte[] compressed;
            byte[] bytes_size = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes_size, (uint)messageToEncrypt.dataSize);
            compressed = bytes_size.Concat(LZ4Helper.CompressBlock(messageToEncrypt.data)).ToArray();

            messageToEncrypt.data = compressed;
            messageToEncrypt.dataSize = new UInt24((uint)compressed.Length); // Явное преобразование
            messageToEncrypt.flags = SetBit(messageToEncrypt.flags, FLAG_BITS["lz4compressed"], true);
        }

        //msg type
        byte[] msgType = BitConverter.GetBytes(messageToEncrypt.requestType)
            .Append(messageToEncrypt.requestChannel)
            .Append(messageToEncrypt.requestCache)
            .ToArray();
        byte[] nonceData = CHACHA_KEY_EXTENSION_BYTES.Concat(BitConverter.GetBytes(counter)).ToArray();
        if (chachaKeyServer == null)
        {
            Logger.logl("CRYPTOR ОТСУТСТВУЮТ КЛЮЧИ (СЕРВЕР) Если это хендшейк используйте cryptLevel=0 в msg_basic", (byte)Logger.LogType.Error);
            return null;
        }
        byte[] msgType_c = ChaChaHelper.EncryptChaCha20(msgType, nonceData, chachaKeyServer, CHACHA_COUNTER_F_TYPE);
        //msg data
        byte[] data_c = ChaChaHelper.EncryptChaCha20(messageToEncrypt.data, nonceData, chachaKeyServer, CHACHA_COUNTER_F_DATA);
        //datasize
        byte[] msgSize_andFlags_b = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(msgSize_andFlags_b, (uint)messageToEncrypt.dataSize);
        msgSize_andFlags_b[3] = messageToEncrypt.flags;
        //poly
        byte[] noncePoly = CHACHA_KEY_EXTENSION_BYTES.Concat(BitConverter.GetBytes(ulong.MaxValue - counter)).ToArray();
        byte[] polyKey = ChaChaHelper.EncryptChaCha20(POLY_KEYGEN_DATA, noncePoly, chachaKeyServer, CHACHA_COUNTER_F_POLY);
        byte[] headersTemp = POLY_HEADER_NULLS.Concat(msgSize_andFlags_b).Concat(msgType_c).ToArray();
        byte[] polySign = GeneratePoly1305Mac(polyKey, headersTemp, data_c);
        //хедеры 12 байт
        byte[] headers = polySign.Concat(msgSize_andFlags_b).Concat(msgType_c).ToArray();

        return headers.Concat(data_c).ToArray();
    }

    /// <summary>
    /// prepare message for sending without encryption
    /// </summary>
    private byte[] prepareSingleMessageClient(message_basic messageToEncrypt)
    {
        byte[] msgSize_andFlags_b = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(msgSize_andFlags_b, (uint)messageToEncrypt.dataSize);
        msgSize_andFlags_b[3] = messageToEncrypt.flags;

        byte[] headers = NOENC_HEADER
            .Concat(msgSize_andFlags_b)
            .Concat(BitConverter.GetBytes(messageToEncrypt.requestType))
            .Append(messageToEncrypt.requestChannel)
            .Append(messageToEncrypt.requestCache)
            .ToArray();

        return headers.Concat(messageToEncrypt.data).ToArray();
    }

}

public class message_basic
{
    public byte[] polySign = new byte[4];
    public UInt24 dataSize;
    public byte flags = 0x00;
    public UInt16 requestType;
    public byte requestChannel;
    public byte requestCache = 0x00;
    public byte[] data;
    public bool isPolyValid;

    public ushort cryptLevel = 1;

    /// <summary>
    /// Этот конструктор предназначен для внутренникодового взаимодействия. Лучше его не дёргать ибо автофикса за вами не будет, в том числе длинны
    /// </summary>
    public message_basic() { }

    /// <summary>
    /// Базовый конструктор
    /// </summary>
    /// <param name="data">Сырые данные (use TRealizer.Serializer.Serialize)</param>
    /// <param name="requestChannel">Канал для путешествия данных</param>
    /// <param name="requestType">Тип сообщения</param>
    /// <param name="flags">Флаги. Например 0x80 - хендшейк (необязательно)</param>
    /// <param name="cryptLevel">Уровень шифрования. Если происходит этап хендшейка необходимо установить 0</param>
    public message_basic(byte[] data, byte requestChannel, UInt16 requestType, byte flags = 0x00, ushort cryptLevel = 1)
    {
        if (cryptLevel == 0)
            this.polySign = Crypto.NOENC_HEADER;
        this.data = data;
        this.requestChannel = requestChannel;
        this.requestType = requestType;
        this.flags = flags;
        this.dataSize = (UInt24)data.Length;
    }

}