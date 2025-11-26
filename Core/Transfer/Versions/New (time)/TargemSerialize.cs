using OpenCrossoutProtocol;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.Numerics;
using System.Reflection;
using System.Text;

namespace OpenCrossoutProtocol.NewRealizer
{
    public static class Serializer
    {
        public static byte[] Serialize<T>(T obj)
        {
            using var stream = new MemoryStream();
            using var writer = new BitWriter(stream);
            Serialize(obj, writer);
            writer.Flush();
            return stream.ToArray();
        }

        private static void Serialize<T>(T obj, BitWriter writer)
        {
            var fields = typeof(T)
                .GetFields(BindingFlags.Public | BindingFlags.Instance)
                .OrderBy(f => f.MetadataToken);

            foreach (var field in fields)
            {
                if (!ShouldWriteField(field, obj))
                    continue;

                var value = field.GetValue(obj);
                WriteValue(writer, value, field, obj);
            }
        }


        private static bool ShouldWriteField(FieldInfo field, object obj)
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

        private static void WriteValue(BitWriter writer, object value, FieldInfo field, object obj)
        {
            Type type = field.FieldType;
            Endianness endianness = field.GetCustomAttribute<EndiannessAttribute>()?.Endian ?? TSS.BasicEndianess;
            //byte+bit
            if (type == typeof(byte)) { writer.WriteByte((byte)value); return; }
            if (type == typeof(sbyte)) { writer.WriteSByte((sbyte)value); return; }
            if (type == typeof(Bit)) { writer.WriteBit((Bit)value); return; }
            //U-nums
            if (type == typeof(ushort) || type == typeof(UInt16)) { WriteUInt16(writer, (ushort)value, endianness); return; }
            if (type == typeof(uint) || type == typeof(UInt32)) { WriteUInt32(writer, (uint)value, endianness); return; }
            if (type == typeof(ulong) || type == typeof(UInt64)) { WriteUInt64(writer, (ulong)value, endianness); return; }
            //S-nums
            if (type == typeof(short) || type == typeof(Int16)) { WriteInt16(writer, (short)value, endianness); return; }
            if (type == typeof(int) || type == typeof(Int32)) { WriteInt32(writer, (int)value, endianness); return; }
            if (type == typeof(long) || type == typeof(Int64)) { WriteInt64(writer, (long)value, endianness); return; }
            //Packed
            if (type == typeof(PackedUInt64_4)) { WritePackedInt64(writer, (PackedUInt64_4)value); return; }
            if (type == typeof(VarInt)) { WriteVarInt(writer, (VarInt)value, endianness); return; }
            if (type == typeof(NewVarInt)) { WriteNewVarInt(writer, ((NewVarInt)value).Value); return; }
            if (type == typeof(HexInt)) { WriteHexInt(writer, ((HexInt)value)); return; }
            //str
            if (type == typeof(nullterminatedString)) { WriteTerminatedString(writer, (nullterminatedString)value); return; }
            if (type == typeof(string)) { WriteStringNT(writer, (string)value); return; }
            //arrs
            if (type == typeof(object)) { WriteOneObject(writer, value); return; }
            if (type == typeof(byte[])) { WriteByteArray(writer, (byte[])value, endianness, field, obj); return; }
            if (type.IsArray) { WriteArray(writer, (Array)value, type, endianness, field, obj); return; }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>)) { WriteDictionary(writer, value); return; }

            // Рекурсивная сериализация классов
            if (type.IsClass)
            {
                var method = typeof(Serializer).GetMethod("Serialize", BindingFlags.NonPublic | BindingFlags.Static);
                var genericMethod = method.MakeGenericMethod(type);
                genericMethod.Invoke(null, new object[] { value, writer });
                return;
            }

            throw new NotSupportedException($"Type {type.Name} is not supported");
        }
        private static void WriteHexInt(BitWriter writer, HexInt integer)
        {
            byte[] src = BitConverter.GetBytes(integer.Value);
            Array.Reverse(src); //endian
            byte tmp = src[0];
            src[0] = src[2];
            src[2] = tmp;
            writer.WriteBytes(src);
        }

        private static void WriteArray(BitWriter writer, Array array, Type arrayType, Endianness endianness, FieldInfo field, object obj)
        {
            int length = array.Length;
            bool writeLength = true;

            var dynamicAttr = field.GetCustomAttribute<DynamicLengthAttribute>();
            var fixedAttr = field.GetCustomAttribute<FixedAttribute>();

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

                writeLength = false;
            }
            else if (fixedAttr != null)
            {
                if (fixedAttr.Length > 0)
                {
                    if (array.Length != fixedAttr.Length)
                        throw new InvalidDataException($"Invalid array length. Expected {fixedAttr.Length}, got {array.Length}");

                    writeLength = false;
                }
                else
                {
                    WriteInt32(writer, array.Length, endianness);
                    writeLength = false;
                }
            }
            if (writeLength)
            {
                WriteVarInt(writer, new VarInt { Value = array.Length }, endianness);
            }
            Type elementType = arrayType.GetElementType();
            foreach (var element in array)
            {
                SerializeElement(writer, element, elementType);
            }
        }

        private static void WriteByteArray(BitWriter writer, byte[] byteArray, Endianness endianness, FieldInfo field, object obj)
        {
            // Определение длины
            int length = byteArray.Length;
            bool writeLength = true;

            var dynamicAttr = field.GetCustomAttribute<DynamicLengthAttribute>();
            var fixedAttr = field.GetCustomAttribute<FixedAttribute>();

            if (dynamicAttr != null)
            {
                // Вычисление длины на основе других полей
                var sourceValue = Convert.ToInt32(obj.GetType()
                    .GetField(dynamicAttr.SourceField)
                    .GetValue(obj));

                length = dynamicAttr.Operation switch
                {
                    LengthOperationType.BitCount => BitOperations.PopCount((uint)sourceValue),
                    LengthOperationType.Divide => sourceValue / dynamicAttr.Operand,
                    _ => throw new InvalidOperationException()
                };

                writeLength = false;

                if (byteArray.Length != length)
                    throw new InvalidDataException($"Byte array length mismatch. Expected {length}, got {byteArray.Length}");
            }
            else if (fixedAttr != null)
            {
                if (fixedAttr.Length > 0)
                {
                    if (byteArray.Length != fixedAttr.Length)
                        throw new InvalidDataException($"Invalid byte array length. Expected {fixedAttr.Length}, got {byteArray.Length}");

                    writeLength = false;
                }
                else
                {
                    // Длина будет записана как 4-байтовое значение
                    WriteInt32(writer, byteArray.Length, endianness);
                    writeLength = false;
                }
            }

            // Запись длины если требуется
            if (writeLength)
            {
                WriteVarInt(writer, new VarInt { Value = byteArray.Length }, endianness);
            }

            // Непосредственная запись байтов
            writer.WriteBytes(byteArray);
        }

        private static void SerializeElement(BitWriter writer, object element, Type elementType)
        {
            MethodInfo method = typeof(Serializer).GetMethod("Serialize", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo genericMethod = method.MakeGenericMethod(elementType);
            genericMethod.Invoke(null, new object[] { element, writer });
        }

        private static void WriteTerminatedString(BitWriter writer, nullterminatedString str)
        {
            var bytes = Encoding.UTF8.GetBytes(str.Value);
            writer.WriteBytes(bytes);
            writer.WriteByte(0); // Null terminator
        }

        private static void WriteStringNT(BitWriter writer, string str)
        {
            WriteTerminatedString(writer, new nullterminatedString { Value = str });
        }

        private static void WritePackedInt64(BitWriter writer, PackedUInt64_4 packedInt)
        {
            ulong value = packedInt.Value;
            if (value == 0)
            {
                writer.WriteByte(0);
                return;
            }

            byte[] buffer = new byte[8];
            byte flags = 0;

            // Заполняем buffer и flags в обратном порядке (от старшего байта к младшему)
            for (int i = 0; i < 8; i++)
            {
                // Сдвигаем на (7-i)*8 чтобы получить байты от старшего к младшему
                byte byteVal = (byte)((value >> ((7 - i) * 8)) & 0xFF);
                if (byteVal != 0)
                {
                    flags |= (byte)(1 << i);  // Устанавливаем бит в соответствии с позицией
                    buffer[i] = byteVal;
                }
            }

            writer.WriteByte(flags);

            // Записываем только ненулевые байты в прямом порядке (от старшего к младшему)
            for (int i = 0; i < 8; i++)
            {
                if ((flags & (1 << i)) != 0)
                {
                    writer.WriteByte(buffer[i]);
                }
            }
        }

        private static void WriteBool(BitWriter writer, bool value)
        {
            writer.WriteByte(value ? (byte)1 : (byte)0);
        }

        private static void WriteUInt16(BitWriter writer, ushort value, Endianness endian)
        {
            var bytes = BitConverter.GetBytes(value);
            if (endian == Endianness.Big)
                Array.Reverse(bytes);
            writer.WriteBytes(bytes);
        }

        private static void WriteUInt32(BitWriter writer, uint value, Endianness endian)
        {
            var bytes = BitConverter.GetBytes(value);
            if (endian == Endianness.Big)
                Array.Reverse(bytes);
            writer.WriteBytes(bytes);
        }

        private static void WriteUInt64(BitWriter writer, ulong value, Endianness endian)
        {
            var bytes = BitConverter.GetBytes(value);
            if (endian == Endianness.Big)
                Array.Reverse(bytes);
            writer.WriteBytes(bytes);
        }

        private static void WriteVarInt(BitWriter writer, VarInt varInt, Endianness endian)
        {
            /*if (TSS.isNew)
            {
                WriteNewVarInt(writer, varInt.Value);
                return;
            }*/
            int value = varInt.Value;
            if (value <= byte.MaxValue)
            {
                writer.WriteBit(0);
                writer.WriteByte((byte)value);
            }
            else if (value <= short.MaxValue)
            {
                writer.WriteBit(1);
                writer.WriteBit(0);
                var bytes = BitConverter.GetBytes((short)value);
                writer.WriteBytes(bytes);
            }
            else
            {
                writer.WriteBit(1);
                writer.WriteBit(1);
                var bytes = BitConverter.GetBytes(value);
                writer.WriteBytes(bytes);
            }
        }

        private static void WriteInt16(BitWriter writer, short value, Endianness endian) //place in one method?
        {
            var bytes = BitConverter.GetBytes(value);
            if (endian == Endianness.Big)
                Array.Reverse(bytes);
            writer.WriteBytes(bytes);
        }

        private static void WriteInt32(BitWriter writer, int value, Endianness endian) //place in one method?
        {
            var bytes = BitConverter.GetBytes(value);
            if (endian == Endianness.Big)
                Array.Reverse(bytes);
            writer.WriteBytes(bytes);
        }

        private static void WriteInt64(BitWriter writer, long value, Endianness endian) //place in one method?
        {
            var bytes = BitConverter.GetBytes(value);
            if (endian == Endianness.Big)
                Array.Reverse(bytes);
            writer.WriteBytes(bytes);
        }

        private static void WriteFloat32(BitWriter writer, float value, Endianness endian) //place in one method?
        {
            var bytes = BitConverter.GetBytes(value);
            if (endian == Endianness.Big)
                Array.Reverse(bytes);
            writer.WriteBytes(bytes);
        }

        private static void WriteDictionary(BitWriter writer, object dict)
        {
            if (dict is IDictionary<string, object> genericDict)
            {
                WriteUInt32(writer, (uint)genericDict.Count, TSS.BasicEndianess);
                foreach (var entry in genericDict)
                {
                    WriteStringNT(writer, entry.Key);
                    WriteOneObject(writer, entry.Value);
                }
            }
            else if (dict is IDictionary nonGenericDict)
            {
                WriteUInt32(writer, (uint)nonGenericDict.Count, TSS.BasicEndianess);
                foreach (DictionaryEntry entry in nonGenericDict)
                {
                    WriteStringNT(writer, (string)entry.Key);
                    WriteOneObject(writer, entry.Value);
                }
            }
            else
            {
                throw new ArgumentException("Object is not a dictionary", nameof(dict));
            }
        }
        private static void WriteCtArray(BitWriter writer, Array array)
        {
            WriteUInt32(writer, (uint)array.Length, TSS.BasicEndianess);
            foreach (var element in array)
            {
                WriteOneObject(writer, element);
            }
        }

        private static void WriteNewVarInt(BitWriter writer, long value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            ulong convValue = BitConverter.ToUInt64(bytes);


            if (convValue < 0x80)
            {
                writer.WriteByte(bytes[0]);
                return;
            }

            byte prefix;
            int byteIndex;
            int bytesCount;
            byte mask;

            if (convValue < 0x4000) // 16^3 = 16384
            {
                byteIndex = 1;
                mask = 0x7F; // 01111111
                prefix = 0x80; // 10000000
                bytesCount = 1;
            }
            else if (convValue < 0x200000) // 2^21 = 2097152
            {
                byteIndex = 2;
                mask = 0x3F; // 00111111
                prefix = 0xC0; // 11000000
                bytesCount = 2;
            }
            else if (convValue < 0x10000000) // 2^28 = 268435456
            {
                byteIndex = 3;
                mask = 0x1F; // 00011111
                prefix = 0xE0; // 11100000
                bytesCount = 3;
            }
            else if (convValue < 0x800000000L) // 2^35 = 34359738368
            {
                byteIndex = 4;
                mask = 0x0F; // 00001111
                prefix = 0xF0; // 11110000
                bytesCount = 4;
            }
            else if (convValue < 0x40000000000L) // 2^42 = 4398046511104
            {
                byteIndex = 5;
                mask = 0x07; // 00000111
                prefix = 0xF8; // 11111000
                bytesCount = 5;
            }
            else if (convValue < 0x2000000000000L) // 2^49 = 562949953421312
            {
                byteIndex = 6;
                mask = 0x03; // 00000011
                prefix = 0xFC; // 11111100
                bytesCount = 6;
            }
            else if (convValue < 0x100000000000000L) // 2^56 = 72057594037927936
            {
                writer.WriteByte(0xFE);
                byte[] zbuffer = new byte[7];
                Array.Copy(bytes, 0, zbuffer, 0, 7);
                writer.WriteBytes(zbuffer);
                return;
            }
            else
            {
                writer.WriteByte(0xFF);
                writer.WriteBytes(bytes);
                return;
            }

            byte prefixByte = (byte)((bytes[byteIndex] & mask) | prefix);
            writer.WriteByte(prefixByte);

            byte[] buffer = new byte[bytesCount];
            Array.Copy(bytes, buffer, bytesCount);
            writer.WriteBytes(buffer);
        }


        private static void WriteOneObject(BitWriter writer, object value)
        {
            if (value == null)
            {
                writer.WriteByte(TSS.OBJTYPE_NULL);
                return;
            }
            Int64 valueZ = 0;
            Type type = value.GetType();
            try
            {
                switch (value)
                {
                    case uint or int or short or ushort or byte:
                        if (!TSS.isNew)
                        {
                            writer.WriteByte(TSS.OBJTYPE_UINT32);
                            WriteUInt32(writer, Convert.ToUInt32(value), TSS.BasicEndianess);
                        }
                        else
                        {
                            writer.WriteByte(TSS.OBJTYPE_UINT64);
                            valueZ = Convert.ToInt64(value);
                            WriteNewVarInt(writer, Convert.ToInt64(valueZ));
                        }
                        break;
                    case long:
                        writer.WriteByte(TSS.OBJTYPE_UINT64);
                        if (!TSS.isNew) WriteInt64(writer, Convert.ToInt64(value), TSS.BasicEndianess);
                        else WriteNewVarInt(writer, BitConverter.ToInt64(BitConverter.GetBytes((long)value)));
                        break;
                    case ulong:
                        writer.WriteByte(TSS.OBJTYPE_UINT64);
                        if (!TSS.isNew) WriteUInt64(writer, Convert.ToUInt64(value), TSS.BasicEndianess);
                        else WriteNewVarInt(writer, BitConverter.ToInt64(BitConverter.GetBytes((ulong)value)));
                        break;
                    case bool: //2.27+
                        writer.WriteByte(TSS.OBJTYPE_UINT32);
                        WriteBool(writer, (bool)value);
                        break;
                    case float or double:
                        writer.WriteByte(TSS.OBJTYPE_FLOAT); //TSS.isNew не менял
                        WriteFloat32(writer, Convert.ToSingle(value), TSS.BasicEndianess);
                        break;
                    case string s:
                        writer.WriteByte(TSS.OBJTYPE_TSTRING);
                        WriteStringNT(writer, s);
                        break;
                    /*case BsonArray bsonArray: //mongod
                        writer.WriteByte(TSS.OBJTYPE_ARRAY);
                        WriteCtArray(writer, bsonArray.Values.ToArray());
                        break;*/
                    case ExpandoObject expando: //mongod
                        writer.WriteByte(TSS.OBJTYPE_DICTIONARY);
                        var dict = (IDictionary<string, object>)expando;
                        WriteDictionary(writer, dict);
                        break;
                    case IDictionary dictZ: //mongod
                        writer.WriteByte(TSS.OBJTYPE_DICTIONARY);
                        WriteDictionary(writer, dictZ);
                        break;
                    case Array array:
                        writer.WriteByte(TSS.OBJTYPE_ARRAY);
                        WriteCtArray(writer, array);
                        break;
                    case IList list: //mongod
                        writer.WriteByte(TSS.OBJTYPE_ARRAY);
                        var arr = new object[list.Count];
                        list.CopyTo(arr, 0);
                        WriteCtArray(writer, arr);
                        break;

                    default:
                        Console.WriteLine($"Unsupported type: {value.GetType().Name}");
                        throw new NotSupportedException($"Object type {value.GetType().Name} is not supported");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Pizdos");
            }
        }
    }
}