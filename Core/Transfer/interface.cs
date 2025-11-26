using System.Reflection;

namespace OpenCrossoutProtocol.TRealizer
{
    internal interface ICrossSerializator
    {
        //inside dict
        //void WriteNewVarInt(BitWriter writer, ulong value);
        //void WriteVarInt(BitWriter writer, VarInt varInt, Endianness endian);
        //void WriteOneObjectArray(BitWriter writer, Array array);
        void WriteOneObject(BitWriter writer, object value);
        void WriteDictionary(BitWriter writer, object dict);
        void WriteFloat(BitWriter writer, float value);
        void WriteInt64(BitWriter writer, long value);
        void WriteInt32(BitWriter writer, int value);
        void WriteInt16(BitWriter writer, short value);
        void WritePackedInt64(BitWriter writer, long packedInt);
        void WriteString(BitWriter writer, string str);
        void WriteArray(BitWriter writer, Array array, Type arrayType, FieldInfo field, object obj);
    }
    internal interface ICrossDeserializator
    {
        object ReadOneObject(BitReader reader);
        Dictionary<string, object> ReadDictionary(BitReader reader);
        float ReadFloat(BitReader reader);
        long ReadInt64(BitReader reader);
        int ReadInt32(BitReader reader);
        short ReadInt16(BitReader reader);
        int ReadPacketInt64(BitReader reader);
        string ReadString(BitReader reader);
        object ReadArray(BitReader reader, Type arrayType, FieldInfo field, object obj);
    }
}