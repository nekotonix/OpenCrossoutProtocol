namespace OpenCrossoutProtocol.NewRealizer
{
    [AttributeUsage(AttributeTargets.Field)]
    public class EndiannessAttribute : Attribute
    {
        public Endianness Endian { get; }
        public EndiannessAttribute(Endianness endian) { Endian = endian; }
    }

    public class nullterminatedString { public required string Value { get; set; } }
    public class PackedUInt64_4 { public ulong Value { get; set; } }
    public class VarInt { public int Value { get; set; } }
    public class NewVarInt { public long Value { get; set; } }
    public class HexInt { public uint Value { get; set; } }
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


    public static class TSS
    {
        public static Endianness BasicEndianess = Endianness.Little;
        public const byte OBJTYPE_NULL = 0;
        public const byte OBJTYPE_UINT32 = 1; //bool in new
        public const byte OBJTYPE_UINT64 = 2; //varint in new
        public const byte OBJTYPE_FLOAT = 3;
        public const byte OBJTYPE_TSTRING = 4; //nullterminated string
        public const byte OBJTYPE_DICTIONARY = 5;
        public const byte OBJTYPE_ARRAY = 6;

        //Для самых новых версий, isNew = true
        public static bool isNew = true; // <<<------
        //для 0.10 isNew = false, Endianess = Big, структуры 1
        //для 2.7.10 isNew = false, Endianess = Big, структуры 2
        //для 2.17.10 isNew = true, Endianess = Little, структуры 3
    }
}