using System.IO;
using ThoriumRustMod.Config;

namespace ThoriumRustMod.Services;

public static class DataHandler
{
    public const long MaxCacheSize = 268_435_456; // 256 MB per buffer

    public static MemoryStream RpcEventBuffer { get; private set; } = new(65536);
    public static MemoryStream KillEventBuffer { get; private set; } = new(16384);
    public static MemoryStream SessionEventBuffer { get; private set; } = new(4096);
    public static MemoryStream CombatEventBuffer { get; private set; } = new(16384);
    public static MemoryStream EntityEventBuffer { get; private set; } = new(65536);

    public static long RpcEventCount { get; set; }
    public static long KillEventCount { get; set; }
    public static long SessionEventCount { get; set; }
    public static long CombatEventCount { get; set; }
    public static long EntityEventCount { get; set; }

    private static bool _isConfigured;

    public static bool IsConfigured
    {
        get => _isConfigured;
        internal set => _isConfigured = value;
    }

    public static void Reset()
    {
        RpcEventBuffer.SetLength(0);
        RpcEventBuffer.Position = 0;
        KillEventBuffer.SetLength(0);
        KillEventBuffer.Position = 0;
        SessionEventBuffer.SetLength(0);
        SessionEventBuffer.Position = 0;
        CombatEventBuffer.SetLength(0);
        CombatEventBuffer.Position = 0;
        EntityEventBuffer.SetLength(0);
        EntityEventBuffer.Position = 0;
        RpcEventCount = 0;
        KillEventCount = 0;
        SessionEventCount = 0;
        CombatEventCount = 0;
        EntityEventCount = 0;
        _isConfigured = false;
    }
}