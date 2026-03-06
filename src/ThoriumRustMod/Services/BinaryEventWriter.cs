using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace ThoriumRustMod.Services;

public static class BinaryEventWriter
{
    [ThreadStatic] private static byte[]? _buf;
    private static byte[] Buf => _buf ??= new byte[8];

    public static void WriteBool(Stream stream, bool val)
    {
        stream.WriteByte((byte)(val ? 1 : 0));
    }

    public static void WriteString(Stream stream, string s)
    {
        s ??= string.Empty;
        var bytes = Encoding.UTF8.GetBytes(s);

        WriteInt32Raw(stream, bytes.Length);
        if (bytes.Length > 0)
            stream.Write(bytes, 0, bytes.Length);
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

    public static void WriteVector(Stream stream, Vector3 v)
    {
        WriteSingle(stream, v.x);
        WriteSingle(stream, v.y);
        WriteSingle(stream, v.z);
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