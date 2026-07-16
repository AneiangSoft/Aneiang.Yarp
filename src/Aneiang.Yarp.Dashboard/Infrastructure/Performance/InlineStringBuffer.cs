using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Performance;

/// <summary>
/// Inline string buffer storing up to 20 UTF-16 characters (40 bytes) without heap allocation.
/// Falls back to external memory for longer strings.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 40)]
public readonly struct InlineStringBuffer
{
    private readonly ulong _data0;  // 8 bytes
    private readonly ulong _data1;  // 8 bytes
    private readonly ulong _data2;  // 8 bytes
    private readonly ulong _data3;  // 8 bytes
    private readonly uint _data4;   // 4 bytes
    private readonly byte _length;  // 1 byte
    private readonly byte _hasExternal; // 1 byte
    // 2 bytes padding

    public int Length => _length;
    public bool HasExternalStorage => _hasExternal != 0;

    public InlineStringBuffer(ReadOnlySpan<char> text)
    {
        _length = (byte)Math.Min(text.Length, 20);
        
        if (text.Length <= 20)
        {
            _hasExternal = 0;
            // Pack characters into ulong fields
            Span<byte> bytes = stackalloc byte[40];
            var byteCount = Encoding.UTF8.GetBytes(text, bytes);
            
            _data0 = byteCount > 0 ? BinaryPrimitives.ReadUInt64LittleEndian(bytes) : 0;
            _data1 = byteCount > 8 ? BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(8)) : 0;
            _data2 = byteCount > 16 ? BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(16)) : 0;
            _data3 = byteCount > 24 ? BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(24)) : 0;
            _data4 = byteCount > 32 ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(32)) : 0;
        }
        else
        {
            _hasExternal = 1;
            // Store hash for external lookup
            _data0 = (ulong)text.GetHashCode();
            _data1 = _data2 = _data3 = 0;
            _data4 = 0;
        }
    }

    public override string ToString()
    {
        if (_hasExternal != 0)
            return $"[External:{_data0}]";
            
        Span<byte> bytes = stackalloc byte[36];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, _data0);
        if (_length > 8) BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(8), _data1);
        if (_length > 16) BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(16), _data2);
        if (_length > 24) BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(24), _data3);
        if (_length > 32) BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(32), _data4);
        
        return Encoding.UTF8.GetString(bytes.Slice(0, _length));
    }
}
