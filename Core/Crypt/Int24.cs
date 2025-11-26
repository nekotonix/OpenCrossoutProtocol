namespace OpenCrossoutProtocol;

[Serializable]
public struct UInt24 : IEquatable<UInt24>, IComparable<UInt24>, IFormattable
{
    private const uint MaxValue = 0x00FFFFFF; // 16,777,215
    private uint _value;

    public UInt24(uint value)
    {
        _value = value & MaxValue;
    }

    public static explicit operator UInt24(uint value) => new UInt24(value);
    public static implicit operator uint(UInt24 value) => value._value;
    public static explicit operator int(UInt24 v) { return (int)v._value; }
    public static explicit operator UInt24(int v) { return new UInt24((uint)v); }
    public static UInt24 operator +(UInt24 a, UInt24 b) => new UInt24(a._value + b._value);
    public static UInt24 operator -(UInt24 a, UInt24 b) => new UInt24(a._value - b._value);
    public static UInt24 operator *(UInt24 a, UInt24 b) => new UInt24(a._value * b._value);
    public static UInt24 operator /(UInt24 a, UInt24 b) => new UInt24(a._value / b._value);
    public bool Equals(UInt24 other) => _value == other._value;
    public override bool Equals(object obj) => obj is UInt24 other && Equals(other);
    public override int GetHashCode() => (int)_value;
    public int CompareTo(UInt24 other) => _value.CompareTo(other._value);

    public override string ToString() => _value.ToString();
    public string ToString(string format, IFormatProvider formatProvider)
        => _value.ToString(format, formatProvider);

    public static UInt24 Parse(string s) => new UInt24(uint.Parse(s));
    public static bool TryParse(string s, out UInt24 result)
    {
        bool success = uint.TryParse(s, out uint value);
        result = new UInt24(value);
        return success;
    }
}

[Serializable]
public struct Int24 : IEquatable<Int24>, IComparable<Int24>, IFormattable
{
    private const int MinValue = -8_388_608;   // 0x800000 (24 bits)
    private const int MaxValue = 8_388_607;    // 0x7FFFFF (24 bits)
    private int _value;

    public Int24(int value)
    {
        _value = (value << 8) >> 8; // Sign-extend and mask
    }

    public static explicit operator Int24(int value) => new Int24(value);
    public static implicit operator int(Int24 value) => value._value;

    public static Int24 operator +(Int24 a, Int24 b) => new Int24(a._value + b._value);
    public static Int24 operator -(Int24 a, Int24 b) => new Int24(a._value - b._value);
    public static Int24 operator *(Int24 a, Int24 b) => new Int24(a._value * b._value);
    public static Int24 operator /(Int24 a, Int24 b) => new Int24(a._value / b._value);

    public bool Equals(Int24 other) => _value == other._value;
    public override bool Equals(object obj) => obj is Int24 other && Equals(other);
    public override int GetHashCode() => _value;
    public int CompareTo(Int24 other) => _value.CompareTo(other._value);

    public override string ToString() => _value.ToString();
    public string ToString(string format, IFormatProvider formatProvider)
        => _value.ToString(format, formatProvider);

    public static Int24 Parse(string s) => new Int24(int.Parse(s));
    public static bool TryParse(string s, out Int24 result)
    {
        bool success = int.TryParse(s, out int value);
        result = new Int24(value);
        return success;
    }
}