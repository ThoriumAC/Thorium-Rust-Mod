using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ThoriumRustMod.HarmonyPatches.Utility;

public static class Helpers
{
    public static long TryExtractNetId(BaseNetworkable? networkable)
    {
        if (networkable is not { net.ID.Value: var value }) return 0;
        return (long)value;
    }

    public static long GetSteamIdOrZero(BasePlayer? player)
    {
        if (player is not { userID._value: var userID }) return 0;
        return (long)userID;
    }

    public static ulong GetSteamIdUlongOrZero(BasePlayer? player)
    {
        return player is not { userID._value: var userID } ? 0 : userID;
    }

    public static void Write(MemoryStream stream, uint value)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, value);
        stream.Write(buf);
    }

    public static void Write(MemoryStream stream, ulong value)
    {
        Span<byte> buf = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(buf, value);
        stream.Write(buf);
    }

    public static void Write(MemoryStream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Write(stream, (uint)bytes.Length);
        stream.Write(bytes, 0, bytes.Length);
    }

    public static void Write(MemoryStream stream, byte[] value)
    {
        Write(stream, (uint)value.Length);
        stream.Write(value, 0, value.Length);
    }

    public static void Write(MemoryStream stream, float value)
    {
        Span<byte> buf = stackalloc byte[4];
        MemoryMarshal.Write(buf, ref value);
        stream.Write(buf);
    }

    public static void WriteCappedBytes(Stream stream, byte[] value, int length)
    {
        if (value.Length > length)
        {
            throw new ArgumentException($"Byte array length exceeds the specified length of {length}.");
        }

        Span<byte> lenBuf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lenBuf, value.Length);
        stream.Write(lenBuf);

        stream.Write(value, 0, value.Length);

        for (var i = value.Length; i < length; i++)
            stream.WriteByte(0);
    }
}