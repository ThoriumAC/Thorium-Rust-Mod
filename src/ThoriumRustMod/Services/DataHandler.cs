using System.IO;
using ThoriumRustMod.Config;
using UnityEngine;

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
    private static float _lastCheck;

    public static bool IsConfigured
    {
        get
        {
            var now = Time.realtimeSinceStartup;
            if (now - _lastCheck > 5f)
            {
                _isConfigured = ThoriumConfigService.HasValidToken;
                _lastCheck = now;
            }
            return _isConfigured;
        }
    }

    public static void Reset()
    {
        RpcEventBuffer?.Dispose();
        KillEventBuffer?.Dispose();
        SessionEventBuffer?.Dispose();
        CombatEventBuffer?.Dispose();
        EntityEventBuffer?.Dispose();
        RpcEventBuffer = new MemoryStream(65536);
        KillEventBuffer = new MemoryStream(16384);
        SessionEventBuffer = new MemoryStream(4096);
        CombatEventBuffer = new MemoryStream(16384);
        EntityEventBuffer = new MemoryStream(65536);
        RpcEventCount = 0;
        KillEventCount = 0;
        SessionEventCount = 0;
        CombatEventCount = 0;
        EntityEventCount = 0;
        _isConfigured = false;
        _lastCheck = 0f;
    }
}