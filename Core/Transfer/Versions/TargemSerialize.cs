using System.Collections;
using System.Dynamic;
using System.Numerics;
using System.Reflection;
using System.Text;

namespace OpenCrossoutProtocol.TRealizer
{
    public static class Serializer
    {
        /// <summary>
        /// Convert class to crossout readable format.
        /// </summary>
        /// <typeparam name="T">Not neccesary (only for more readable), class from serializes to byte[]</typeparam>
        /// <param name="obj">Any class with filled parameters</param>
        /// <returns></returns>
        public static byte[] Serialize<T>(T obj, TMode mode = TMode.Classic)
        {
            using var stream = new MemoryStream();
            using var writer = new BitWriter(stream);
            Serialize(obj, writer, mode);
            writer.Flush();
            return stream.ToArray();
        }

        private static void Serialize<T>(T obj, BitWriter writer, TMode mode = TMode.Classic)
        {
            var fields = typeof(T)
                .GetFields(BindingFlags.Public | BindingFlags.Instance)
                .OrderBy(f => f.MetadataToken);

            foreach (var field in fields)
            {
                if (!ShouldWriteField(field, obj))
                    continue;

                var value = field.GetValue(obj);
                WriteValue(writer, value, field, obj, mode);
            }
        }

        //TODO: TMode.MostNew
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

        private static void WriteValue(BitWriter writer, object value, FieldInfo field, object obj, TMode mode)
        {
            Type type = field.FieldType;
            Endianness endianness = (mode == TMode.New ? Endianness.Little : Endianness.Big);
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

            if (type == typeof(float) || type == typeof(double))
            {
                var extrbytes = BitConverter.GetBytes(Convert.ToSingle(value));
                if (endianness == Endianness.Big) extrbytes = extrbytes.Reverse().ToArray();
                writer.WriteBytes(extrbytes);
                return;
            }
            //if(type == typeof(double)) Logger.logl("Type double is not supported. Use float instead");
            //Packed
            if (type == typeof(PackedUInt64_4)) { WritePackedUInt64(writer, (PackedUInt64_4)value, endianness); return; }
            if (type == typeof(PackedInt64_4)) { WritePackedInt64(writer, (PackedInt64_4)value, endianness); return; }
            if (type == typeof(VarInt)) { WriteVarInt(writer, (VarInt)value, endianness); return; }
            if (type == typeof(NewVarInt)) { WriteNewVarInt(writer, ((NewVarInt)value).Value); return; }
            //str
            if (type == typeof(string)) { WriteString(writer, (string)value); return; }
            //arrs
            if (type == typeof(object)) { WriteOneObject(writer, value, mode); return; }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                Type itemType = type.GetGenericArguments()[0];
                Type genericListType = typeof(List<>).MakeGenericType(itemType);
                var list = (IList)Activator.CreateInstance(genericListType);
                foreach (var item in (IEnumerable)value)
                {
                    list.Add(item);
                }
                Array array = Array.CreateInstance(itemType, list.Count);
                list.CopyTo(array, 0);
                WriteArray(writer, array, type, endianness, field, obj, mode);
            }

            if (type == typeof(byte[])) { WriteByteArray(writer, (byte[])value, endianness, field, obj); return; }
            if (type.IsArray) { WriteArray(writer, (Array)value, type, endianness, field, obj, mode); return; }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>)) { WriteDictionary(writer, value, mode); return; }

            // Рекурсивная сериализация классов
            if (type.IsClass)
            {
                //ХЗ как провернуть костыль с типом T
                var methods = typeof(Serializer).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Where(m => m.Name == "Serialize");
                var method = methods.FirstOrDefault(m => m.ReturnType == typeof(void) && m.GetParameters().Length == 3);

                if (method == null)
                    throw new MissingMethodException("Serializer.Serialize method not found");

                var genericMethod = method.MakeGenericMethod(type);
                genericMethod.Invoke(null, new object[] { value, writer, mode }); // Добавляем mode
                return;
            }

            throw new NotSupportedException($"Type {type.Name} is not supported");
        }

        private static void WriteArray(BitWriter writer, Array array, Type arrayType, Endianness endianness, FieldInfo field, object obj, TMode mode)
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
                SerializeElement(writer, element, elementType, mode);
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

        private static void SerializeElement(BitWriter writer, object element, Type elementType, TMode mode)
        {
            var methods = typeof(Serializer).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Where(m => m.Name == "Serialize");
            var method = methods.FirstOrDefault(m => m.ReturnType == typeof(void) && m.GetParameters().Length == 3);

            if (method == null)
                throw new MissingMethodException("Serializer.Serialize method not found");

            var genericMethod = method.MakeGenericMethod(elementType);
            genericMethod.Invoke(null, new object[] { element, writer, mode }); // Добавляем mode
            return;
        }

        private static void WriteString(BitWriter writer, string str)
        {
            var bytes = Encoding.UTF8.GetBytes(str);
            writer.WriteBytes(bytes);
            writer.WriteByte(0); // Null terminator
        }

        private static void WritePackedUInt64(BitWriter writer, PackedUInt64_4 packedInt, Endianness endian)
        {
            ulong value = packedInt.Value;
            if (value == 0)
            {
                writer.WriteByte(0);
                return;
            }

            byte[] buffer = new byte[8];
            byte flags = 0;

            for (int i = 0; i < 8; i++)
            {
                byte byteVal = (byte)((value >> (i * 8)) & 0xFF);
                if (byteVal != 0)
                {
                    flags |= (byte)(1 << i);
                    buffer[i] = byteVal;
                }
            }

            writer.WriteByte(flags);
            for (int i = 0; i < 8; i++)
            {
                if ((flags & (1 << i)) != 0)
                {
                    writer.WriteByte(buffer[i]);
                }
            }
        }
        //ыыы иди нахуй
        private static void WritePackedInt64(BitWriter writer, PackedInt64_4 packedInt, Endianness endian)
        {
            byte[] val = BitConverter.GetBytes(packedInt.Value);
            ulong cval = BitConverter.ToUInt64(val);
            PackedUInt64_4 packedUInt = new PackedUInt64_4 { Value = cval };
            WritePackedUInt64(writer, packedUInt, endian);
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

        private static void WriteDictionary(BitWriter writer, object dict, TMode mode)
        {
            if (dict is IDictionary<string, object> genericDict)
            {
                WriteUInt32(writer, (uint)genericDict.Count, (mode == TMode.New ? Endianness.Little : Endianness.Big));
                foreach (var entry in genericDict)
                {
                    WriteString(writer, entry.Key);
                    WriteOneObject(writer, entry.Value, mode);
                }
            }
            else if (dict is IDictionary nonGenericDict)
            {
                WriteUInt32(writer, (uint)nonGenericDict.Count, (mode == TMode.New ? Endianness.Little : Endianness.Big));
                foreach (DictionaryEntry entry in nonGenericDict)
                {
                    WriteString(writer, (string)entry.Key);
                    WriteOneObject(writer, entry.Value, mode);
                }
            }
            else
            {
                throw new ArgumentException("Object is not a dictionary", nameof(dict));
            }
        }
        private static void WriteCtArray(BitWriter writer, Array array, TMode mode)
        {
            WriteUInt32(writer, (uint)array.Length, (mode == TMode.New ? Endianness.Little : Endianness.Big));
            foreach (var element in array)
            {
                WriteOneObject(writer, element, mode);
            }
        }

        private static void WriteNewVarInt(BitWriter writer, ulong value)
        {
            if (value < 0x80)
            {
                writer.WriteByte((byte)value);
                return;
            }

            byte mask;
            int shift;

            if (value < 0x4000)
            {
                mask = 0x80;
                shift = 8;
            }
            else if (value < 0x200000)
            {
                mask = 0xC0;
                shift = 16;
            }
            else if (value < 0x10000000)
            {
                mask = 0xE0;
                shift = 24;
            }
            else if (value < 0x800000000)
            {
                mask = 0xF0;
                shift = 32;
            }
            else if (value < 0x40000000000)
            {
                mask = 0xF8;
                shift = 40;
            }
            else if (value < 0x2000000000000)
            {
                mask = 0xFC;
                shift = 48;
            }
            else if (value < 0x100000000000000)
            {
                mask = 0xFE;
                shift = 56;
            }
            else
            {
                mask = 0xFF;
                shift = 64;
            }

            byte firstByte = (byte)((value >> shift) & 0xFF);
            firstByte |= mask;
            writer.WriteByte(firstByte);

            ulong remaining = value & ((1UL << shift) - 1);
            byte[] bytes = BitConverter.GetBytes(remaining);
            int byteCount = shift / 8;
            byte[] toWrite = new byte[byteCount];
            Buffer.BlockCopy(bytes, 0, toWrite, 0, byteCount);
            writer.WriteBytes(toWrite);
        }

        private static void WriteOneObject(BitWriter writer, object value, TMode mode)
        {
            if (value == null)
            {
                writer.WriteByte(TSS.OBJTYPE_NULL);
                return;
            }
            UInt64 valueZ = 0;
            Type type = value.GetType();
            try
            {
                switch (value)
                {
                    case uint:
                    case ushort: //без разделения знаковости будет оверфлоу 
                        if (mode == TMode.Classic)
                        {
                            writer.WriteByte(TSS.OBJTYPE_UINT32);
                            WriteUInt32(writer, unchecked((uint)value), (mode == TMode.New ? Endianness.Little : Endianness.Big));
                        }
                        else if (mode == TMode.OldClassic)
                        {
                            writer.WriteByte(TSS.OBJTYPE_NULL);
                            WriteUInt32(writer, unchecked((uint)value), (mode == TMode.New ? Endianness.Little : Endianness.Big));
                        }
                        else
                        {
                            writer.WriteByte(TSS.OBJTYPE_UINT64);
                            valueZ = unchecked((ulong)(long)value);
                            WriteNewVarInt(writer, Convert.ToUInt64(valueZ));
                        }
                        break;
                    case int:
                    case short:
                        if (mode == TMode.Classic)
                        {
                            writer.WriteByte(TSS.OBJTYPE_UINT32);
                            WriteUInt32(writer, unchecked((uint)(int)value), (mode == TMode.New ? Endianness.Little : Endianness.Big));
                        }
                        else if (mode == TMode.OldClassic)
                        {
                            writer.WriteByte(TSS.OBJTYPE_NULL);
                            WriteUInt32(writer, unchecked((uint)(int)value), (mode == TMode.New ? Endianness.Little : Endianness.Big));
                        }
                        else
                        {
                            writer.WriteByte(TSS.OBJTYPE_UINT64);
                            valueZ = unchecked((ulong)(long)value);
                            WriteNewVarInt(writer, Convert.ToUInt64(valueZ));
                        }
                        break;
                    case long:
                        writer.WriteByte(TSS.OBJTYPE_UINT64);
                        if (mode == TMode.Classic || mode == TMode.OldClassic) WriteInt64(writer, Convert.ToInt64(value), (mode == TMode.New ? Endianness.Little : Endianness.Big));
                        else WriteNewVarInt(writer, BitConverter.ToUInt64(BitConverter.GetBytes((long)value)));
                        break;
                    case ulong:
                        writer.WriteByte(TSS.OBJTYPE_UINT64);
                        if (mode == TMode.Classic || mode == TMode.OldClassic) WriteUInt64(writer, Convert.ToUInt64(value), (mode == TMode.New ? Endianness.Little : Endianness.Big));
                        else WriteNewVarInt(writer, BitConverter.ToUInt64(BitConverter.GetBytes((ulong)value)));
                        break;
                    case float:
                    case double:
                        writer.WriteByte(TSS.OBJTYPE_FLOAT); //TSS.isNew не менял
                        WriteFloat32(writer, Convert.ToSingle(value), (mode == TMode.New ? Endianness.Little : Endianness.Big));
                        break;
                    case string s:
                        writer.WriteByte(TSS.OBJTYPE_TSTRING);
                        WriteString(writer, s);
                        break;
                    case ExpandoObject expando: //mongod
                        writer.WriteByte(TSS.OBJTYPE_DICTIONARY);
                        var dict = (IDictionary<string, object>)expando;
                        WriteDictionary(writer, dict, mode);
                        break;
                    case IDictionary dictZ: //mongod
                        writer.WriteByte(TSS.OBJTYPE_DICTIONARY);
                        WriteDictionary(writer, dictZ, mode);
                        break;
                    case Array array:
                        writer.WriteByte(TSS.OBJTYPE_ARRAY);
                        WriteCtArray(writer, array, mode);
                        break;
                    case IList list: //mongod
                        writer.WriteByte(TSS.OBJTYPE_ARRAY);
                        var arr = new object[list.Count];
                        list.CopyTo(arr, 0);
                        WriteCtArray(writer, arr, mode);
                        break;

                    default:
                        Logger.logl($"TREALIZER Unsupported type: {value.GetType().Name}", (byte)Logger.LogType.Error);
                        throw new NotSupportedException($"Object type {value.GetType().Name} is not supported");
                }
            }
            catch (Exception ex)
            {
                Logger.logl($"Pizdos (serialize). {ex}", (byte)Logger.LogType.Error);
            }
        }
    }
}