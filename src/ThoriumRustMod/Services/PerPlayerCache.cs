using Network;
using System.Collections.Generic;
using UnityEngine;

namespace ThoriumRustMod.Services;

internal static class PerPlayerCache
{
    private struct NetStatsEntry
    {
        public float Ping;
        public long PacketLoss;
        public float LastUpdate;
    }

    private static readonly Dictionary<long, BaseMovement> _movements = new(1200);
    private static readonly Dictionary<long, NetStatsEntry> _netStats = new(1200);
    private const float NetStatsTtl = 5f;

    public static bool GetAdminCheat(long steamId) =>
        _movements.TryGetValue(steamId, out var m) && m != null && m.adminCheat;

    public static (float ping, long packetLoss) GetNetStats(BasePlayer player, long steamId)
    {
        var now = Time.realtimeSinceStartup;
        if (!_netStats.TryGetValue(steamId, out var entry) || now - entry.LastUpdate > NetStatsTtl)
        {
            var conn = player.net?.connection;
            if (conn != null)
            {
                entry = new NetStatsEntry
                {
                    Ping = Net.sv.GetAveragePing(conn),
                    PacketLoss = (long)Net.sv.GetStat(conn, BaseNetwork.StatTypeLong.PacketLossLastSecond),
                    LastUpdate = now
                };
                _netStats[steamId] = entry;
            }
        }
        return (entry.Ping, entry.PacketLoss);
    }

    public static void Register(BasePlayer player, long steamId)
    {
        if (steamId <= 0) return;
        _movements[steamId] = player.GetComponent<BaseMovement>();
        _netStats.Remove(steamId);
    }

    public static void Unregister(long steamId)
    {
        if (steamId <= 0) return;
        _movements.Remove(steamId);
        _netStats.Remove(steamId);
    }

    public static void Reset()
    {
        _movements.Clear();
        _netStats.Clear();
    }
}
