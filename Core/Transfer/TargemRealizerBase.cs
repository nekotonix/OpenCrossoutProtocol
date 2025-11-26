namespace OpenCrossoutProtocol.TRealizer
{
    public class PackedUInt64_4 { public ulong Value { get; set; } }
    public class PackedInt64_4 { public long Value { get; set; } }
    public class VarInt { public int Value { get; set; } }
    public class NewVarInt { public ulong Value { get; set; } }
    public enum Endianness
    {
        Little,
        Big
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class FixedAttribute : Attribute
    {
        public int Length { get; }

        public FixedAttribute(int length) => Length = length;
        public FixedAttribute() => Length = -1;
    }
    public enum LengthOperationType
    {
        BitCount,
        Divide
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class DynamicLengthAttribute : Attribute
    {
        public string SourceField { get; }
        public LengthOperationType Operation { get; }
        public int Operand { get; }

        public DynamicLengthAttribute(string sourceField, LengthOperationType operation, int operand = 0)
        {
            SourceField = sourceField;
            Operation = operation;
            Operand = operand;
        }
    }

    public enum ComparisonOperator
    {
        GreaterThan,
        GreaterThanOrEqual,
        LessThan,
        LessThanOrEqual,
        Equal,
        NotEqual
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class ConditionAttribute : Attribute
    {
        public string DependentField { get; }
        public ComparisonOperator Operator { get; }
        public object Value { get; }

        public ConditionAttribute(string dependentField, ComparisonOperator op, object value)
        {
            DependentField = dependentField;
            Operator = op;
            Value = value;
        }
    }
    /// <summary>
    /// Описывает режим работы кодера/екнкодера данных сассаута.
    /// <0.9 => OldClassic
    /// <0.13 => Classic
    /// <2.10 => New + BigEndian
    /// >2.10 => New + LittleEndian (Default New)
    /// </summary>
    public enum TMode
    {
        Classic = 1,
        New = 2,
        OldClassic = 3,
        MostNew = 4, //2.27 //TODO
    }


    public static class TSS
    {
        public const byte OBJTYPE_NULL = 0;
        public const byte OBJTYPE_UINT32 = 1; //bool in new
        public const byte OBJTYPE_UINT64 = 2; //varint in new
        public const byte OBJTYPE_FLOAT = 3;
        public const byte OBJTYPE_TSTRING = 4; //nullterminated string
        public const byte OBJTYPE_DICTIONARY = 5;
        public const byte OBJTYPE_ARRAY = 6;
    }
}