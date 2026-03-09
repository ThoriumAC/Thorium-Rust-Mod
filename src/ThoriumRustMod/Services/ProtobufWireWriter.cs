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
        var len = (int)ms.Length;
        var result = new byte[len];
        if (ms.TryGetBuffer(out var seg))
            Buffer.BlockCopy(seg.Array!, seg.Offset, result, 0, len);
        else
        {
            ms.Position = 0;
            ms.Read(result, 0, len);
        }
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

    [ThreadStatic]
    private static byte[]? _strBuf;

    public void WriteString(string? value)
    {
        value ??= string.Empty;
        if (value.Length == 0)
        {
            WriteVarint(0u);
            return;
        }

        var maxBytes = Encoding.UTF8.GetMaxByteCount(value.Length);
        if (_strBuf == null || _strBuf.Length < maxBytes)
            _strBuf = new byte[Math.Max(maxBytes, 256)];

        var count = Encoding.UTF8.GetBytes(value, 0, value.Length, _strBuf, 0);
        WriteVarint((uint)count);
        _stream.Write(_strBuf, 0, count);
    }

    [ThreadStatic]
    private static byte[]? _numBuf;

    public void WriteNumericString(ulong value)
    {
        if (value == 0)
        {
            WriteVarint(1u);
            _stream.WriteByte((byte)'0');
            return;
        }

        _numBuf ??= new byte[20]; // max digits for ulong
        var pos = 20;
        var v = value;
        while (v > 0)
        {
            _numBuf[--pos] = (byte)('0' + (v % 10));
            v /= 10;
        }
        var len = 20 - pos;
        WriteVarint((uint)len);
        _stream.Write(_numBuf, pos, len);
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

    public void WriteBytes(byte[] value, int length)
    {
        if (value == null || length <= 0)
        {
            WriteVarint(0u);
            return;
        }
        WriteVarint((uint)length);
        _stream.Write(value, 0, length);
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

    public void WriteEmbeddedMessage<TState>(int fieldNumber, TState state, Action<ProtobufWireWriter, TState> write)
    {
        WriteTag(fieldNumber, ProtobufWireType.LengthDelimited);
        var ms = RentStream();
        var innerWriter = new ProtobufWireWriter(ms);
        write(innerWriter, state);
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
