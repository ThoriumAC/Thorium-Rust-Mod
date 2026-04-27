using System;
using System.Collections.Generic;
using ThoriumRustMod.Models;

namespace ThoriumRustMod.Services;

internal static class PlayerServerStatsTracker
{
    private const long MaxPlaytimeDeltaMs = 5000;
    private const long PlaytimeEmitIntervalSeconds = 60;

    private static readonly object _sync = new();
    private static readonly Dictionary<long, PlayerServerStats> _stats = new(2048);

    public static void RegisterSessionStart(BasePlayer player, long steamId, long timestampMs)
    {
        if (steamId <= 0 || player == null)
            return;

        lock (_sync)
        {
            var stats = GetOrCreate(steamId);
            stats.DisplayName = player.displayName ?? stats.DisplayName;
            stats.SessionCount++;
            stats.IsOnline = true;
            stats.LastSeenUnixMs = timestampMs;
            stats.UpdatedAtUnixMs = timestampMs;
            stats.Dirty = true;
        }
    }

    public static void RegisterSessionEnd(BasePlayer player, long steamId, long timestampMs)
    {
        if (steamId <= 0)
            return;

        lock (_sync)
        {
            if (!_stats.TryGetValue(steamId, out var stats))
                return;

            stats.DisplayName = player?.displayName ?? stats.DisplayName;
            ApplyPlaytimeDelta(stats, timestampMs);
            stats.IsOnline = false;
            stats.UpdatedAtUnixMs = timestampMs;
            stats.Dirty = true;
        }
    }

    public static void RecordTick(BasePlayer player, long steamId, CombatData? combatData, long timestampMs)
    {
        if (steamId <= 0 || player == null)
            return;

        lock (_sync)
        {
            var stats = GetOrCreate(steamId);
            stats.DisplayName = player.displayName ?? stats.DisplayName;

            if (!stats.IsOnline)
            {
                stats.IsOnline = true;
                if (stats.SessionCount == 0)
                    stats.SessionCount = 1;
            }

            ApplyPlaytimeDelta(stats, timestampMs);
        }
    }

    public static void RecordItemGranted(long steamId, string? displayName, string? itemShortName, int amount, long timestampMs)
    {
        if (steamId <= 0 || amount <= 0 || string.IsNullOrWhiteSpace(itemShortName))
            return;

        lock (_sync)
        {
            var stats = GetOrCreate(steamId);
            stats.DisplayName = displayName ?? stats.DisplayName;

            if (!ApplyGrantedItem(stats, itemShortName, amount))
                return;

            stats.UpdatedAtUnixMs = timestampMs;
            stats.Dirty = true;
        }
    }

    public static void RecordProjectileFired(long steamId, string? displayName, string? weaponShortName, int shotCount, long timestampMs)
    {
        if (steamId <= 0)
            return;

        if (shotCount <= 0)
            shotCount = 1;

        lock (_sync)
        {
            var stats = GetOrCreate(steamId);
            stats.DisplayName = displayName ?? stats.DisplayName;
            stats.BulletsFired += shotCount;
            IncrementWeaponUsage(stats, weaponShortName);
            stats.UpdatedAtUnixMs = timestampMs;
            stats.Dirty = true;
        }
    }

    public static void RecordExplosiveUsed(long steamId, string? displayName, string? itemShortName, int amount, long timestampMs)
    {
        if (steamId <= 0 || amount <= 0 || string.IsNullOrWhiteSpace(itemShortName))
            return;

        if (!IsTrackedExplosiveItem(itemShortName))
            return;

        lock (_sync)
        {
            var stats = GetOrCreate(steamId);
            stats.DisplayName = displayName ?? stats.DisplayName;
            stats.ExplosivesUsed += amount;
            stats.UpdatedAtUnixMs = timestampMs;
            stats.Dirty = true;
        }
    }

    public static void RecordHit(long attackerSteamId, string? weaponShortName, bool isHeadshot, long timestampMs)
    {
        if (attackerSteamId <= 0)
            return;

        lock (_sync)
        {
            var stats = GetOrCreate(attackerSteamId);
            stats.ShotsHit++;
            if (isHeadshot)
                stats.Headshots++;

            IncrementWeaponUsage(stats, weaponShortName);
            stats.UpdatedAtUnixMs = timestampMs;
            stats.Dirty = true;
        }
    }

    public static void RecordPlayerKill(long killerSteamId, string? weaponShortName, float distance, long timestampMs)
    {
        if (killerSteamId <= 0)
            return;

        lock (_sync)
        {
            var stats = GetOrCreate(killerSteamId);
            stats.PlayerKills++;
            if (distance > stats.FurthestKillDistance)
                stats.FurthestKillDistance = distance;

            IncrementWeaponUsage(stats, weaponShortName);
            stats.UpdatedAtUnixMs = timestampMs;
            stats.Dirty = true;
        }
    }

    public static void RecordPlayerDeath(long victimSteamId, long timestampMs)
    {
        if (victimSteamId <= 0)
            return;

        lock (_sync)
        {
            var stats = GetOrCreate(victimSteamId);
            stats.PlayerDeaths++;
            stats.UpdatedAtUnixMs = timestampMs;
            stats.Dirty = true;
        }
    }

    public static void RecordEntityPlaced(BasePlayer player, BaseEntity entity, long timestampMs)
    {
        if (player == null || entity == null)
            return;

        var steamId = (long)player.userID._value;
        if (steamId <= 0)
            return;

        var shortName = entity.ShortPrefabName ?? string.Empty;

        lock (_sync)
        {
            var stats = GetOrCreate(steamId);
            stats.DisplayName = player.displayName ?? stats.DisplayName;

            if (IsBuildingBlock(entity, shortName))
                stats.BuildingBlocksPlaced++;

            if (IsExplosiveEntity(shortName))
                stats.ExplosivesUsed++;

            if (IsCropEntity(shortName))
                stats.CropsPlanted++;

            stats.UpdatedAtUnixMs = timestampMs;
            stats.Dirty = true;
        }
    }

    public static void RecordNpcKill(long killerSteamId, long timestampMs)
    {
        if (killerSteamId <= 0)
            return;

        lock (_sync)
        {
            var stats = GetOrCreate(killerSteamId);
            stats.NpcsKilled++;
            stats.UpdatedAtUnixMs = timestampMs;
            stats.Dirty = true;
        }
    }

    public static void RecordAnimalKill(long killerSteamId, long timestampMs)
    {
        if (killerSteamId <= 0)
            return;

        lock (_sync)
        {
            var stats = GetOrCreate(killerSteamId);
            stats.AnimalsKilled++;
            stats.UpdatedAtUnixMs = timestampMs;
            stats.Dirty = true;
        }
    }

    public static List<PlayerServerStats> CollectDirtyStats(long timestampMs)
    {
        lock (_sync)
        {
            var results = new List<PlayerServerStats>();

            foreach (var stats in _stats.Values)
            {
                if (stats.IsOnline)
                    ApplyPlaytimeDelta(stats, timestampMs);

                var totalPlaytimeSeconds = stats.TotalPlaytimeSeconds;
                var shouldEmitPlaytime = totalPlaytimeSeconds - stats.LastEmittedPlaytimeSeconds >= PlaytimeEmitIntervalSeconds;
                if (!stats.Dirty && !shouldEmitPlaytime)
                    continue;

                stats.UpdatedAtUnixMs = timestampMs;
                stats.LastEmittedPlaytimeSeconds = totalPlaytimeSeconds;
                stats.Dirty = false;
                results.Add(stats.CloneForTransport());
            }

            return results;
        }
    }

    public static void Reset()
    {
        lock (_sync)
        {
            _stats.Clear();
        }
    }

    private static PlayerServerStats GetOrCreate(long steamId)
    {
        if (_stats.TryGetValue(steamId, out var existing))
            return existing;

        var created = new PlayerServerStats
        {
            SteamId = steamId
        };
        _stats[steamId] = created;
        return created;
    }

    private static void ApplyPlaytimeDelta(PlayerServerStats stats, long timestampMs)
    {
        if (stats.LastSeenUnixMs <= 0)
        {
            stats.LastSeenUnixMs = timestampMs;
            return;
        }

        var deltaMs = timestampMs - stats.LastSeenUnixMs;
        if (deltaMs <= 0)
            return;

        if (deltaMs > MaxPlaytimeDeltaMs)
            deltaMs = MaxPlaytimeDeltaMs;

        stats.TotalPlaytimeMs += deltaMs;
        stats.LastSeenUnixMs = timestampMs;
    }

    private static void IncrementWeaponUsage(PlayerServerStats stats, string? weaponShortName)
    {
        if (string.IsNullOrWhiteSpace(weaponShortName))
            return;

        if (stats.WeaponUsage.TryGetValue(weaponShortName, out var count))
            stats.WeaponUsage[weaponShortName] = count + 1;
        else
            stats.WeaponUsage[weaponShortName] = 1;
    }

    private static bool IsBuildingBlock(BaseEntity entity, string shortName)
    {
        if (entity is BuildingBlock)
            return true;

        return shortName.Contains("foundation", StringComparison.OrdinalIgnoreCase) ||
               shortName.Contains("wall", StringComparison.OrdinalIgnoreCase) ||
               shortName.Contains("floor", StringComparison.OrdinalIgnoreCase) ||
               shortName.Contains("roof", StringComparison.OrdinalIgnoreCase) ||
               shortName.Contains("stair", StringComparison.OrdinalIgnoreCase) ||
               shortName.Contains("doorway", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExplosiveEntity(string shortName)
    {
        return shortName.Contains("explosive.timed", StringComparison.OrdinalIgnoreCase) ||
               shortName.Contains("grenade.beancan", StringComparison.OrdinalIgnoreCase) ||
               shortName.Contains("grenade.f1", StringComparison.OrdinalIgnoreCase) ||
               shortName.Contains("satchel", StringComparison.OrdinalIgnoreCase) ||
               shortName.Contains("surveycharge", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCropEntity(string shortName)
    {
        return shortName.Contains("corn", StringComparison.OrdinalIgnoreCase) ||
               shortName.Contains("pumpkin", StringComparison.OrdinalIgnoreCase) ||
               shortName.Contains("potato", StringComparison.OrdinalIgnoreCase) ||
               shortName.Contains("hemp", StringComparison.OrdinalIgnoreCase) ||
               shortName.Contains(".seed", StringComparison.OrdinalIgnoreCase) ||
               shortName.Contains(".clone", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ApplyGrantedItem(PlayerServerStats stats, string itemShortName, int amount)
    {
        switch (itemShortName)
        {
            case "wood":
                stats.WoodGathered += amount;
                return true;
            case "stones":
                stats.StoneGathered += amount;
                return true;
            case "metal.ore":
            case "hq.metal.ore":
                stats.MetalGathered += amount;
                return true;
            case "sulfur.ore":
            case "sulfur":
                stats.SulfurGathered += amount;
                return true;
            case "scrap":
                stats.ScrapCollected += amount;
                return true;
            case "pumpkin":
            case "corn":
            case "potato":
            case "cloth":
                stats.CropsHarvested += amount;
                return true;
        }

        if (itemShortName.Contains("berry", StringComparison.OrdinalIgnoreCase))
        {
            stats.CropsHarvested += amount;
            return true;
        }

        return false;
    }

    private static bool IsTrackedExplosiveItem(string itemShortName)
    {
        return itemShortName.Contains("explosive.timed", StringComparison.OrdinalIgnoreCase) ||
               itemShortName.Contains("explosive.satchel", StringComparison.OrdinalIgnoreCase) ||
               itemShortName.Contains("grenade.beancan", StringComparison.OrdinalIgnoreCase) ||
               itemShortName.Contains("grenade.f1", StringComparison.OrdinalIgnoreCase) ||
               itemShortName.Contains("surveycharge", StringComparison.OrdinalIgnoreCase) ||
               itemShortName.Contains("ammo.rocket", StringComparison.OrdinalIgnoreCase) ||
               itemShortName.Contains("ammo.rifle.explosive", StringComparison.OrdinalIgnoreCase);
    }
}
