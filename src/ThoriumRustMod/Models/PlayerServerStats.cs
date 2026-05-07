using System;
using System.Collections.Generic;

namespace ThoriumRustMod.Models;

public sealed class PlayerServerStats
{
    public long SteamId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public long TotalPlaytimeMs { get; set; }
    public int SessionCount { get; set; }
    public int BulletsFired { get; set; }
    public int ShotsHit { get; set; }
    public int Headshots { get; set; }
    public int PlayerKills { get; set; }
    public int PlayerDeaths { get; set; }
    public float FurthestKillDistance { get; set; }
    public long WoodGathered { get; set; }
    public long StoneGathered { get; set; }
    public long MetalGathered { get; set; }
    public long SulfurGathered { get; set; }
    public long ScrapCollected { get; set; }
    public int CropsPlanted { get; set; }
    public int CropsHarvested { get; set; }
    public int ExplosivesUsed { get; set; }
    public int BuildingBlocksPlaced { get; set; }
    public int BuildingBlocksBroken { get; set; }
    public int NpcsKilled { get; set; }
    public int AnimalsKilled { get; set; }
    public long UpdatedAtUnixMs { get; set; }

    internal bool IsOnline { get; set; }
    internal long LastSeenUnixMs { get; set; }
    internal float LastAttackTime { get; set; }
    internal long LastEmittedPlaytimeSeconds { get; set; }
    internal bool Dirty { get; set; }
    internal Dictionary<string, int> WeaponUsage { get; } = new(StringComparer.Ordinal);

    public long TotalPlaytimeSeconds => TotalPlaytimeMs / 1000;

    public string PreferredWeapon
    {
        get
        {
            if (WeaponUsage.Count == 0)
                return string.Empty;

            string preferred = string.Empty;
            var preferredCount = 0;

            foreach (var pair in WeaponUsage)
            {
                if (pair.Value <= preferredCount)
                    continue;

                preferred = pair.Key;
                preferredCount = pair.Value;
            }

            return preferred;
        }
    }

    public PlayerServerStats CloneForTransport()
    {
        return new PlayerServerStats
        {
            SteamId = SteamId,
            DisplayName = DisplayName,
            TotalPlaytimeMs = TotalPlaytimeMs,
            SessionCount = SessionCount,
            BulletsFired = BulletsFired,
            ShotsHit = ShotsHit,
            Headshots = Headshots,
            PlayerKills = PlayerKills,
            PlayerDeaths = PlayerDeaths,
            FurthestKillDistance = FurthestKillDistance,
            WoodGathered = WoodGathered,
            StoneGathered = StoneGathered,
            MetalGathered = MetalGathered,
            SulfurGathered = SulfurGathered,
            ScrapCollected = ScrapCollected,
            CropsPlanted = CropsPlanted,
            CropsHarvested = CropsHarvested,
            ExplosivesUsed = ExplosivesUsed,
            BuildingBlocksPlaced = BuildingBlocksPlaced,
            BuildingBlocksBroken = BuildingBlocksBroken,
            NpcsKilled = NpcsKilled,
            AnimalsKilled = AnimalsKilled,
            UpdatedAtUnixMs = UpdatedAtUnixMs
        };
    }
}
