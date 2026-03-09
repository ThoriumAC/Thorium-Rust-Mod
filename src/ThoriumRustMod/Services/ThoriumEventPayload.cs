using System;
using System.Buffers;
using System.IO;

namespace ThoriumRustMod.Services;

internal sealed class ThoriumEventPayload
{
    public byte[]? RpcEventBytes { get; set; }
    public int RpcEventLength { get; set; }
    public byte[]? KillEventBytes { get; set; }
    public int KillEventLength { get; set; }
    public byte[]? SessionEventBytes { get; set; }
    public int SessionEventLength { get; set; }
    public byte[]? CombatEventBytes { get; set; }
    public int CombatEventLength { get; set; }
    public byte[]? EntityEventBytes { get; set; }
    public int EntityEventLength { get; set; }

    public long RpcEventCount { get; set; }
    public long KillEventCount { get; set; }
    public long SessionEventCount { get; set; }
    public long CombatEventCount { get; set; }
    public long EntityEventCount { get; set; }

    public bool HasAnyBytes =>
        RpcEventLength > 0 ||
        KillEventLength > 0 ||
        SessionEventLength > 0 ||
        CombatEventLength > 0 ||
        EntityEventLength > 0;

    public void Return()
    {
        ReturnBuffer(RpcEventBytes);
        ReturnBuffer(KillEventBytes);
        ReturnBuffer(SessionEventBytes);
        ReturnBuffer(CombatEventBytes);
        ReturnBuffer(EntityEventBytes);
        RpcEventBytes = KillEventBytes = SessionEventBytes = CombatEventBytes = EntityEventBytes = null;
        RpcEventLength = KillEventLength = SessionEventLength = CombatEventLength = EntityEventLength = 0;
    }

    private static void ReturnBuffer(byte[]? buf)
    {
        if (buf != null)
            ArrayPool<byte>.Shared.Return(buf);
    }

    public static ThoriumEventPayload? TryDrainAndReset()
    {
        var payload = new ThoriumEventPayload();

        (payload.RpcEventBytes, payload.RpcEventLength) = DrainStream(DataHandler.RpcEventBuffer);
        (payload.KillEventBytes, payload.KillEventLength) = DrainStream(DataHandler.KillEventBuffer);
        (payload.SessionEventBytes, payload.SessionEventLength) = DrainStream(DataHandler.SessionEventBuffer);
        (payload.CombatEventBytes, payload.CombatEventLength) = DrainStream(DataHandler.CombatEventBuffer);
        (payload.EntityEventBytes, payload.EntityEventLength) = DrainStream(DataHandler.EntityEventBuffer);

        payload.RpcEventCount = DataHandler.RpcEventCount;
        payload.KillEventCount = DataHandler.KillEventCount;
        payload.SessionEventCount = DataHandler.SessionEventCount;
        payload.CombatEventCount = DataHandler.CombatEventCount;
        payload.EntityEventCount = DataHandler.EntityEventCount;

        ResetStream(DataHandler.RpcEventBuffer);
        ResetStream(DataHandler.KillEventBuffer);
        ResetStream(DataHandler.SessionEventBuffer);
        ResetStream(DataHandler.CombatEventBuffer);
        ResetStream(DataHandler.EntityEventBuffer);

        return payload.HasAnyBytes ? payload : null;
    }

    private static (byte[]? buf, int length) DrainStream(MemoryStream? ms)
    {
        if (ms == null)
            return (null, 0);

        var length = (int)Math.Min(ms.Length, int.MaxValue);
        if (length <= 0)
            return (null, 0);

        var buf = ArrayPool<byte>.Shared.Rent(length);

        if (ms.TryGetBuffer(out var segment))
        {
            Buffer.BlockCopy(segment.Array!, segment.Offset, buf, 0, length);
            return (buf, length);
        }

        var pos = ms.Position;
        ms.Position = 0;
        _ = ms.Read(buf, 0, length);
        ms.Position = pos;
        return (buf, length);
    }

    private static void ResetStream(MemoryStream? ms)
    {
        if (ms == null)
            return;

        ms.SetLength(0);
        ms.Position = 0;
    }
}
