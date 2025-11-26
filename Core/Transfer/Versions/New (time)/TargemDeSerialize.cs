using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Text;
namespace OpenCrossoutProtocol.NewRealizer
{
    //мб RakNet::BitStream кстати
    public static class Deserializer
    {
        public static T Deserialize<T>(byte[] data) where T : new()
        {
            using var stream = new MemoryStream(data);
            using var reader = new BitReader(stream);
            return Deserialize<T>(reader);
        }

        private static T Deserialize<T>(BitReader reader) where T : new()
        {
            var obj = new T();
            var fields = typeof(T)
                .GetFields(BindingFlags.Public | BindingFlags.Instance)
                .OrderBy(f => f.MetadataToken);

            foreach (var field in fields)
            {
                if (!ShouldReadField(field, obj))
                    continue;

                var value = ReadValue(reader, field, obj);
                field.SetValue(obj, value);
            }
            return obj;
        }
        private static bool ShouldReadField(FieldInfo field, object obj)
        {
            var conditionAttr = field.GetCustomAttribute<ConditionAttribute>();
            if (conditionAttr == null) return true;

            var dependentField = obj.GetType().GetField(conditionAttr.DependentField);
            if (dependentField == null)
                throw new InvalidOperationException($"Field {conditionAttr.DependentField} not found.");

            object dependentValue = dependentField.GetValue(obj);
            object requiredValue = Convert.ChangeType(conditionAttr.Value, dependentValue.GetType());

            int comparison = Comparer.Default.Compare(dependentValue, requiredValue);
            return conditionAttr.Operator switch
            {
                ComparisonOperator.GreaterThan => comparison > 0,
                ComparisonOperator.GreaterThanOrEqual => comparison >= 0,
                ComparisonOperator.LessThan => comparison < 0,
                ComparisonOperator.LessThanOrEqual => comparison <= 0,
                ComparisonOperator.Equal => comparison == 0,
                ComparisonOperator.NotEqual => comparison != 0,
                _ => throw new NotSupportedException()
            };
        }


        private static object ReadValue(BitReader reader, FieldInfo field, object obj)
        {
            Type type = field.FieldType;
            Endianness endianness = field.GetCustomAttribute<EndiannessAttribute>()?.Endian ?? TSS.BasicEndianess;

            if (type == typeof(byte)) return reader.ReadByte();
            if (type == typeof(sbyte)) return reader.ReadSByte();
            if (type == typeof(Bit)) return reader.ReadBit_rBit();

            if (type == typeof(ushort) || type == typeof(UInt16)) return ReadUInt16(reader, endianness);
            if (type == typeof(uint) || type == typeof(UInt32)) return ReadUInt32(reader, endianness);
            if (type == typeof(ulong) || type == typeof(UInt64)) return ReadUInt64(reader, endianness);

            if (type == typeof(short) || type == typeof(Int16)) return ReadInt16(reader, endianness);
            if (type == typeof(int) || type == typeof(Int32)) return ReadInt32(reader, endianness);
            if (type == typeof(long) || type == typeof(Int64)) return ReadInt64(reader, endianness);

            if (type == typeof(PackedUInt64_4)) return ReadPacketInt64(reader, endianness);
            if (type == typeof(VarInt)) return ReadVarInt(reader, endianness);
            if (type == typeof(NewVarInt)) return new NewVarInt { Value = ReadNewVarInt(reader) };
            if (type == typeof(HexInt)) return ReadHex(reader);

            if (type == typeof(nullterminatedString)) return ReadTerminatedString(reader);
            if (type == typeof(string)) return ReadStringNT(reader);

            //arrs
            if (type == typeof(object)) return ReadOneObject(reader, field);
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>)) return ReadDictionary(reader, field);
            if (type.IsArray) return ReadArray(reader, type, endianness, field, obj);

            if (type.IsClass)
            {
                try
                {
                    MethodInfo method = typeof(Deserializer).GetMethod("Deserialize", BindingFlags.NonPublic | BindingFlags.Static);
                    MethodInfo genericMethod = method.MakeGenericMethod(type);
                    return genericMethod.Invoke(null, new object[] { reader });
                }
                catch
                {
                    Console.WriteLine($"FATAL: {type.Name} IS NOT SUPPORTED");
                    throw;
                }
            }

            throw new NotSupportedException($"Type {type.Name} is not supported");
        }


        private static long ReadNewVarInt(BitReader reader)
        {
            byte firstByte = reader.ReadByte();

            if (firstByte < 0x80)
                return firstByte;

            long value = 0;
            int bytesToRead = 0;

            if ((firstByte & 0xC0) == 0x80)     // 10xxxxxx (8 бит)
            {
                value = (long)((ulong)(firstByte & 0x3F) << 8);
                bytesToRead = 1;
            }
            else if ((firstByte & 0xE0) == 0xC0) // 110xxxxx (16 бит)
            {
                value = (long)((ulong)(firstByte & 0x1F) << 16);
                bytesToRead = 2;
            }
            else if ((firstByte & 0xF0) == 0xE0) // 1110xxxx (24 бита)
            {
                value = (long)((ulong)(firstByte & 0x0F) << 24);
                bytesToRead = 3;
            }
            else if ((firstByte & 0xF8) == 0xF0) // 11110xxx (32 бита)
            {
                value = (long)((ulong)(firstByte & 0x07) << 32);
                bytesToRead = 4;
            }
            else if ((firstByte & 0xFC) == 0xF8) // 111110xx (40 бит)
            {
                value = (long)((ulong)(firstByte & 0x03) << 40);
                bytesToRead = 5;
            }
            else if ((firstByte & 0xFE) == 0xFC) // 1111110x (48 бит)
            {
                value = (long)((ulong)(firstByte & 0x01) << 48);
                bytesToRead = 6;
            }
            else
            {
                if (firstByte == 0xFE)
                    bytesToRead = 7;
                else if (firstByte == 0xFF)
                    bytesToRead = 8;
                else
                    throw new InvalidOperationException("Invalid varint prefix");
            }

            byte[] extraBytes = reader.ReadBytes(bytesToRead);
            long extraValue = BytesToInt64LittleEndian(extraBytes, bytesToRead);
            return value | extraValue;
        }

        private static long BytesToInt64LittleEndian(byte[] bytes, int count)
        {
            long result = 0;
            for (int i = 0; i < count; i++)
                result |= (long)bytes[i] << (8 * i);

            return result;
        }


        //смержил байтэрэй и остальные
        private static object ReadArray(BitReader reader, Type arrayType, Endianness endianness, FieldInfo field, object obj)
        {
            int length = 0;

            //Dynamic
            var dynamicAttr = field.GetCustomAttribute<DynamicLengthAttribute>();
            if (dynamicAttr != null)
            {
                var sourceValue = Convert.ToInt32(obj.GetType()
                    .GetField(dynamicAttr.SourceField)
                    .GetValue(obj));

                length = dynamicAttr.Operation switch
                {
                    LengthOperationType.BitCount => BitOperations.PopCount((uint)sourceValue),
                    LengthOperationType.Divide => sourceValue / dynamicAttr.Operand,
                    _ => throw new InvalidOperationException()
                };
            }
            //Fixed
            else
            {
                var fixedAttr = field.GetCustomAttribute<FixedAttribute>();
                if (fixedAttr != null)
                {
                    length = fixedAttr.Length > 0
                        ? fixedAttr.Length
                        : ReadInt32(reader, endianness);
                }
                else
                {
                    VarInt varLength = ReadVarInt(reader, endianness);
                    length = varLength.Value;
                }
            }

            //byte[]
            Type elementType = arrayType.GetElementType();
            if (elementType == typeof(byte))
            {
                return reader.ReadBytes(length);
            }
            Array array = Array.CreateInstance(elementType, length);
            for (int i = 0; i < length; i++)
            {
                array.SetValue(DeserializeElement(reader, elementType), i);
            }
            return array;
        }

        private static object DeserializeElement(BitReader reader, Type elementType)
        {
            MethodInfo method = typeof(Deserializer).GetMethod("Deserialize", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo genericMethod = method.MakeGenericMethod(elementType);
            return genericMethod.Invoke(null, new object[] { reader });
        }
        private static nullterminatedString ReadTerminatedString(BitReader reader)
        {
            var bytes = new List<byte>();
            byte b;
            while ((b = reader.ReadByte()) != 0)
                bytes.Add(b);
            return new nullterminatedString { Value = Encoding.UTF8.GetString(bytes.ToArray()) };
        }

        private static string ReadStringNT(BitReader reader) //Гарантирует чтение строки с нулём терминатором
        {
            return ReadTerminatedString(reader).Value;
        }

        private static PackedUInt64_4 ReadPacketInt64(BitReader reader, Endianness endian)
        {
            byte flags = (byte)reader.ReadByte();
            if (flags == 0) return new PackedUInt64_4 { Value = 0 };
            byte[] buffer = new byte[8];
            for (int bit = 0; bit < 8; bit++)
            {
                if ((flags & (1 << bit)) != 0)
                {
                    int nextByte = reader.ReadByte();
                    if (nextByte == -1)
                        throw new InvalidDataException("Недостаточно данных");
                    buffer[bit] = (byte)nextByte;
                }
            }

            //сборка числа в Big Endian
            if (endian == Endianness.Little)
                Array.Reverse(buffer);
            ulong result = 0;
            for (int i = 0; i < 8; i++)
            {
                result |= (ulong)buffer[i] << (i * 8);
            }
            return new PackedUInt64_4 { Value = result };
        }

        private static HexInt ReadHex(BitReader reader)
        {
            byte[] tempBuffer = reader.ReadBytes(4);
            byte tmp = tempBuffer[0];
            tempBuffer[0] = tempBuffer[2];
            tempBuffer[2] = tmp;
            Array.Reverse(tempBuffer);
            return new() { Value = BitConverter.ToUInt32(tempBuffer) };
        }

        private static uint ReadUInt32(BitReader reader, Endianness endian)
        {
            var bytes = reader.ReadBytes(4);
            if (endian == Endianness.Big)
                Array.Reverse(bytes);
            return BitConverter.ToUInt32(bytes, 0);
        }

        private static ushort ReadUInt16(BitReader reader, Endianness endian)
        {
            var bytes = reader.ReadBytes(2);
            if (endian == Endianness.Big)
                Array.Reverse(bytes);
            return BitConverter.ToUInt16(bytes, 0);
        }
        private static VarInt ReadVarInt(BitReader reader, Endianness endian)
        {
            /*if (TSS.isNew)
            {
                ulong nvi = ReadNewVarInt(reader);
                return new VarInt { Value = nvi };
            }*/

            int val = 0;
            byte fbit = reader.ReadBit();
            if (fbit == 0) { val = (int)reader.ReadByte(); }
            else
            {
                byte sbit = reader.ReadBit();
                if (sbit == 0)
                {
                    val = BitConverter.ToInt16(reader.ReadBytes(2));
                }
                else
                {
                    val = BitConverter.ToInt32(reader.ReadBytes(4));
                }
            }
            return new VarInt { Value = val };
        }
        private static ulong ReadUInt64(BitReader reader, Endianness endian)
        {
            var bytes = reader.ReadBytes(8);
            if (endian == Endianness.Big)
                Array.Reverse(bytes);
            return BitConverter.ToUInt64(bytes, 0);
        }

        private static short ReadInt16(BitReader reader, Endianness endian)
        {
            var bytes = reader.ReadBytes(2);
            if (endian == Endianness.Big)
                Array.Reverse(bytes);
            return BitConverter.ToInt16(bytes, 0);
        }
        private static int ReadInt32(BitReader reader, Endianness endian)
        {
            var bytes = reader.ReadBytes(4);
            if (endian == Endianness.Big)
                Array.Reverse(bytes);
            return BitConverter.ToInt32(bytes, 0);
        }
        private static long ReadInt64(BitReader reader, Endianness endian)
        {
            var bytes = reader.ReadBytes(8);
            if (endian == Endianness.Big)
                Array.Reverse(bytes);
            return BitConverter.ToInt64(bytes, 0);
        }
        private static float ReadFloat32(BitReader reader, Endianness endian)
        {
            var bytes = reader.ReadBytes(4);
            if (endian == Endianness.Big)
                Array.Reverse(bytes);
            return BitConverter.ToSingle(bytes, 0);
        }


        private static Dictionary<string, object> ReadDictionary(BitReader reader, FieldInfo field)
        {
            UInt32 elemsCount = ReadUInt32(reader, TSS.BasicEndianess);
            Dictionary<string, object> resultDictionary = new Dictionary<string, object>();
            string tempKey;
            object tempValue;

            for (int objNum = 1; objNum <= elemsCount; objNum++)
            {
                tempKey = ReadStringNT(reader);
                tempValue = ReadOneObject(reader, field);

                resultDictionary[tempKey] = tempValue;
            }
            return resultDictionary;
        }
        private static object[] ReadCtArray(BitReader reader, FieldInfo field)
        {
            uint count = ReadUInt32(reader, TSS.BasicEndianess);
            object[] array = new object[count];
            for (int i = 0; i < count; i++)
            {
                array[i] = ReadOneObject(reader, field);
            }
            return array;
        }
        private static int ReadBool(BitReader reader, Endianness endian)
        {
            var bl = reader.ReadByte();
            if (bl > 0)
                return 1;
            return 0;
        }


        private static object ReadOneObject(BitReader reader, FieldInfo field) //1 элемент с типом данных
        {
            byte objectType = reader.ReadByte();
            object objectValue = TSS.OBJTYPE_NULL;

            if (objectType == TSS.OBJTYPE_NULL) return 1;
            //Новая версия - bool
            if (objectType == TSS.OBJTYPE_UINT32) objectValue = ReadBool(reader, TSS.BasicEndianess);
            if (TSS.isNew && objectType == TSS.OBJTYPE_UINT64) objectValue = ReadNewVarInt(reader); //Для 0.13+
            else if (objectType == TSS.OBJTYPE_UINT64) objectValue = ReadUInt64(reader, TSS.BasicEndianess); //Для <0.13
            if (objectType == TSS.OBJTYPE_FLOAT) objectValue = ReadFloat32(reader, TSS.BasicEndianess);
            if (objectType == TSS.OBJTYPE_TSTRING) objectValue = ReadStringNT(reader);
            if (objectType == TSS.OBJTYPE_DICTIONARY) objectValue = ReadDictionary(reader, field);
            if (objectType == TSS.OBJTYPE_ARRAY) objectValue = ReadCtArray(reader, field);
            return objectValue;
        }
    }
}
/*
ТАРГЕЙ ПАКЕТ
ЧИСЛО В БАЙТЫ
 static byte[] ConvertToBytes(ulong number)
    {
        // 1. Конвертируем число в little-endian байты
        byte[] buffer = BitConverter.GetBytes(number);
        
        // 2. Собираем индексы ненулевых байтов
        List<byte> nonZeroBytes = new List<byte>();
        byte flags = 0;
        for (int i = 0; i < 8; i++)
        {
            if (buffer[i] != 0)
            {
                flags |= (byte)(1 << i); // Устанавливаем бит флага
                nonZeroBytes.Add(buffer[i]);
            }
        }

        // 3. Формируем итоговый массив: флаг + ненулевые байты
        byte[] result = new byte[1 + nonZeroBytes.Count];
        result[0] = flags;
        nonZeroBytes.CopyTo(result, 1);
        
        return result;
    }

    static void Main()
    {
        ulong number = 111052660998144;
        byte[] bytes = ConvertToBytes(number);
        
        Console.Write("Результат: ");
        foreach (byte b in bytes)
        {
            Console.Write($"0x{b:X2} "); // Вывод: 0x2C 0x69 0x76 0x65
        }
    }






БАЙТЫ В ЧИСЛО
static ulong ReadNumber(byte[] data)
    {
        using (MemoryStream stream = new MemoryStream(data))
        {
            byte flags = (byte)stream.ReadByte();
            byte[] buffer = new byte[8];
            for (int bit = 0; bit < 8; bit++)
            {
                if ((flags & (1 << bit)) != 0)
                {
                    int nextByte = stream.ReadByte();
                    if (nextByte == -1)
                        throw new InvalidDataException("Недостаточно данных");
                    buffer[bit] = (byte)nextByte;
                }
            }

            //сборка числа
            ulong result = 0;
            for (int i = 0; i < 8; i++)
            {
                result |= (ulong)buffer[i] << (i * 8);
            }
            return result;
        }
    }

    static void Main()
    {
        // Пример входных данных: 2C 69 76 65
        byte[] inputData = { 0x2C, 0x69, 0x76, 0x65 };
        ulong result = ReadNumber(inputData);
        Console.WriteLine(result); // Вывод: 111052660998144
    }
 * */