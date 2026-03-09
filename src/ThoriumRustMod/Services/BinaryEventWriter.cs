using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace ThoriumRustMod.Services;

public static class BinaryEventWriter
{
    [ThreadStatic] private static byte[]? _buf;
    private static byte[] Buf => _buf ??= new byte[12];

    [ThreadStatic] private static byte[]? _strBuf;

    public static void WriteBool(Stream stream, bool val)
    {
        stream.WriteByte((byte)(val ? 1 : 0));
    }

    public static void WriteString(Stream stream, string s)
    {
        s ??= string.Empty;
        if (s.Length == 0)
        {
            WriteInt32Raw(stream, 0);
            return;
        }

        var maxBytes = Encoding.UTF8.GetMaxByteCount(s.Length);
        if (_strBuf == null || _strBuf.Length < maxBytes)
            _strBuf = new byte[Math.Max(maxBytes, 256)];

        var count = Encoding.UTF8.GetBytes(s, 0, s.Length, _strBuf, 0);
        WriteInt32Raw(stream, count);
        stream.Write(_strBuf, 0, count);
    }

    public static void WriteUint(Stream stream, uint v)
    {
        var b = Buf;
        b[0] = (byte)v;
        b[1] = (byte)(v >> 8);
        b[2] = (byte)(v >> 16);
        b[3] = (byte)(v >> 24);
        stream.Write(b, 0, 4);
    }

    public static void WriteInt32(Stream stream, int v)
    {
        WriteInt32Raw(stream, v);
    }

    public static void WriteInt32(Stream stream, long v)
    {
        WriteInt32Raw(stream, (int)v);
    }

    public static void WriteInt64(Stream stream, long v)
    {
        var b = Buf;
        b[0] = (byte)v;
        b[1] = (byte)(v >> 8);
        b[2] = (byte)(v >> 16);
        b[3] = (byte)(v >> 24);
        b[4] = (byte)(v >> 32);
        b[5] = (byte)(v >> 40);
        b[6] = (byte)(v >> 48);
        b[7] = (byte)(v >> 56);
        stream.Write(b, 0, 8);
    }

    public static unsafe void WriteSingle(Stream stream, float f)
    {
        var bits = *(int*)&f;
        var b = Buf;
        b[0] = (byte)bits;
        b[1] = (byte)(bits >> 8);
        b[2] = (byte)(bits >> 16);
        b[3] = (byte)(bits >> 24);
        stream.Write(b, 0, 4);
    }

    public static unsafe void WriteVector(Stream stream, Vector3 v)
    {
        var b = Buf;
        var x = *(int*)&v.x;
        b[0] = (byte)x; b[1] = (byte)(x >> 8); b[2] = (byte)(x >> 16); b[3] = (byte)(x >> 24);
        var y = *(int*)&v.y;
        b[4] = (byte)y; b[5] = (byte)(y >> 8); b[6] = (byte)(y >> 16); b[7] = (byte)(y >> 24);
        var z = *(int*)&v.z;
        b[8] = (byte)z; b[9] = (byte)(z >> 8); b[10] = (byte)(z >> 16); b[11] = (byte)(z >> 24);
        stream.Write(b, 0, 12);
    }

    private static void WriteInt32Raw(Stream stream, int v)
    {
        var b = Buf;
        b[0] = (byte)v;
        b[1] = (byte)(v >> 8);
        b[2] = (byte)(v >> 16);
        b[3] = (byte)(v >> 24);
        stream.Write(b, 0, 4);
    }

    public static void WriteCappedBytes(Stream stream, byte[] buf, int length)
    {
        if (buf == null || length <= 0)
        {
            WriteInt32(stream, 0);
            return;
        }
        WriteInt32(stream, length);
        stream.Write(buf, 0, Math.Min(length, buf.Length));
    }

    public static void WriteVector2(Stream stream, Vector2 v)
    {
        WriteSingle(stream, v.x);
        WriteSingle(stream, v.y);
    }
}