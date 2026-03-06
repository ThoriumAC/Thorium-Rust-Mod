using System;
using System.IO;

namespace ThoriumRustMod.Services;

internal sealed class ThoriumEventPayload
{
    public byte[]? RpcEventBytes { get; set; }
    public byte[]? KillEventBytes { get; set; }
    public byte[]? SessionEventBytes { get; set; }
    public byte[]? CombatEventBytes { get; set; }
    public byte[]? EntityEventBytes { get; set; }

    public long RpcEventCount { get; set; }
    public long KillEventCount { get; set; }
    public long SessionEventCount { get; set; }
    public long CombatEventCount { get; set; }
    public long EntityEventCount { get; set; }

    public bool HasAnyBytes =>
        (RpcEventBytes != null && RpcEventBytes.Length > 0) ||
        (KillEventBytes != null && KillEventBytes.Length > 0) ||
        (SessionEventBytes != null && SessionEventBytes.Length > 0) ||
        (CombatEventBytes != null && CombatEventBytes.Length > 0) ||
        (EntityEventBytes != null && EntityEventBytes.Length > 0);

    public static ThoriumEventPayload? TryDrainAndReset()
    {
        var packet = DrainStream(DataHandler.RpcEventBuffer);
        var pvp = DrainStream(DataHandler.KillEventBuffer);
        var join = DrainStream(DataHandler.SessionEventBuffer);
        var damage = DrainStream(DataHandler.CombatEventBuffer);
        var entity = DrainStream(DataHandler.EntityEventBuffer);

        var payload = new ThoriumEventPayload
        {
            RpcEventBytes = packet,
            KillEventBytes = pvp,
            SessionEventBytes = join,
            CombatEventBytes = damage,
            EntityEventBytes = entity,

            RpcEventCount = DataHandler.RpcEventCount,
            KillEventCount = DataHandler.KillEventCount,
            SessionEventCount = DataHandler.SessionEventCount,
            CombatEventCount = DataHandler.CombatEventCount,
            EntityEventCount = DataHandler.EntityEventCount,
        };

        ResetStream(DataHandler.RpcEventBuffer);
        ResetStream(DataHandler.KillEventBuffer);
        ResetStream(DataHandler.SessionEventBuffer);
        ResetStream(DataHandler.CombatEventBuffer);
        ResetStream(DataHandler.EntityEventBuffer);


        return payload.HasAnyBytes ? payload : null;
    }

    private static byte[]? DrainStream(MemoryStream? ms)
    {
        if (ms == null)
            return null;

        var length = (int)Math.Min(ms.Length, int.MaxValue);
        if (length <= 0)
            return null;

        // Make an exact-length copy so protobuf length prefix is correct.
        var buf = new byte[length];

        // Prefer GetBuffer for performance when available.
        if (ms.TryGetBuffer(out var segment))
        {
            Array.Copy(segment.Array!, segment.Offset, buf, 0, length);
            return buf;
        }

        var pos = ms.Position;
        ms.Position = 0;
        _ = ms.Read(buf, 0, length);
        ms.Position = pos;
        return buf;
    }

    private static void ResetStream(MemoryStream? ms)
    {
        if (ms == null)
            return;

        ms.SetLength(0);
        ms.Position = 0;
    }
}
