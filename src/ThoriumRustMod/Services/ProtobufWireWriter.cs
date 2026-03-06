using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ThoriumRustMod.Services;

internal enum ProtobufWireType
{
    Varint = 0,
    Fixed64 = 1,
    LengthDelimited = 2,
    Fixed32 = 5,
}

/// <summary>
/// Minimal Protocol Buffers wire-format writer (no external serializers).
/// Implements: varint, fixed32, length-delimited, embedded messages.
/// </summary>
internal sealed class ProtobufWireWriter
{
    private readonly Stream _stream;

    [ThreadStatic]
    private static Stack<MemoryStream>? _streamPool;

    private static MemoryStream RentStream()
    {
        _streamPool ??= new Stack<MemoryStream>();
        if (_streamPool.Count > 0)
        {
            var ms = _streamPool.Pop();
            ms.SetLength(0);
            return ms;
        }
        return new MemoryStream(512);
    }

    private static void ReturnStream(MemoryStream ms)
    {
        _streamPool ??= new Stack<MemoryStream>();
        _streamPool.Push(ms);
    }

    public ProtobufWireWriter(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    public static byte[] BuildMessage(Action<ProtobufWireWriter> write)
    {
        var ms = RentStream();
        var w = new ProtobufWireWriter(ms);
        write(w);
        var result = ms.ToArray();
        ReturnStream(ms);
        return result;
    }

    public void WriteTag(int fieldNumber, ProtobufWireType wireType)
    {
        if (fieldNumber <= 0) throw new ArgumentOutOfRangeException(nameof(fieldNumber));
        var tag = (uint)((fieldNumber << 3) | (int)wireType);
        WriteVarint(tag);
    }

    public void WriteVarint(ulong value)
    {
        while (value >= 0x80)
        {
            _stream.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }
        _stream.WriteByte((byte)value);
    }

    public void WriteVarint(uint value) => WriteVarint((ulong)value);

    public void WriteInt64(long value) => WriteVarint((ulong)value);

    public void WriteUInt64(ulong value) => WriteVarint(value);

    public void WriteInt32(int value) => WriteVarint((uint)value);

    public void WriteUInt32(uint value) => WriteVarint(value);

    public void WriteBool(bool value) => WriteVarint(value ? 1u : 0u);

    public void WriteFixed32(float value)
    {
        Span<byte> buf = stackalloc byte[4];
        MemoryMarshal.Write(buf, ref value);
        _stream.Write(buf);
    }

    public void WriteString(string? value)
    {
        value ??= string.Empty;
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteVarint((uint)bytes.Length);
        if (bytes.Length > 0)
            _stream.Write(bytes, 0, bytes.Length);
    }

    public void WriteBytes(byte[]? value)
    {
        if (value == null || value.Length == 0)
        {
            WriteVarint(0u);
            return;
        }
        WriteVarint((uint)value.Length);
        _stream.Write(value, 0, value.Length);
    }

    public void WriteEmbeddedMessage(int fieldNumber, Action<ProtobufWireWriter> write)
    {
        WriteTag(fieldNumber, ProtobufWireType.LengthDelimited);
        var ms = RentStream();
        var innerWriter = new ProtobufWireWriter(ms);
        write(innerWriter);
        var len = (int)ms.Length;
        WriteVarint((uint)len);
        if (len > 0)
        {
            if (ms.TryGetBuffer(out var seg))
                _stream.Write(seg.Array!, seg.Offset, len);
            else
            {
                ms.Position = 0;
                ms.CopyTo(_stream);
            }
        }
        ReturnStream(ms);
    }
}
