using System;
using HarmonyLib;
using ThoriumRustMod.HarmonyPatches.Utility;
using ThoriumRustMod.Models;
using ThoriumRustMod.Services;

namespace ThoriumRustMod.HarmonyPatches.BaseCombatEntity_Patch;

internal static class BaseCombatEntity_Die_Patch
{
    [HarmonyPrefix]
    private static void Prefix(BaseCombatEntity __instance, HitInfo info)
    {
        try
        {
            if (!DataHandler.IsConfigured || __instance == null || info == null)
                return;

            if (__instance is BasePlayer)
                return;

            var initiator = info.InitiatorPlayer;
            if (initiator == null)
                return;

            var killerSteamId = Helpers.GetSteamIdOrZero(initiator);
            if (killerSteamId == 0)
                return;

            var timestampMs = PlayerSnapshot.GetUnixTimestampMsCached();
            if (IsAnimal(__instance))
            {
                PlayerServerStatsTracker.RecordAnimalKill(killerSteamId, timestampMs);
            }
            else if (IsNpc(__instance))
            {
                PlayerServerStatsTracker.RecordNpcKill(killerSteamId, timestampMs);
            }
        }
        catch
        {
        }
    }

    private static bool IsAnimal(BaseCombatEntity entity)
    {
        var shortName = entity.ShortPrefabName ?? string.Empty;
        var typeName = entity.GetType().Name;

        return typeName.Contains("Animal", StringComparison.OrdinalIgnoreCase) ||
               shortName.Contains("bear", StringComparison.OrdinalIgnoreCase) ||
               shortName.Contains("wolf", StringComparison.OrdinalIgnoreCase) ||
               shortName.Contains("boar", StringComparison.OrdinalIgnoreCase) ||
               shortName.Contains("stag", StringComparison.OrdinalIgnoreCase) ||
               shortName.Contains("chicken", StringComparison.OrdinalIgnoreCase) ||
               shortName.Contains("horse", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNpc(BaseCombatEntity entity)
    {
        if (IsAnimal(entity))
            return false;

        var shortName = entity.ShortPrefabName ?? string.Empty;
        var typeName = entity.GetType().Name;

        return typeName.Contains("Scientist", StringComparison.OrdinalIgnoreCase) ||
               typeName.Contains("NPC", StringComparison.OrdinalIgnoreCase) ||
               shortName.Contains("scientist", StringComparison.OrdinalIgnoreCase) ||
               shortName.Contains("murderer", StringComparison.OrdinalIgnoreCase) ||
               shortName.Contains("bandit", StringComparison.OrdinalIgnoreCase) ||
               shortName.Contains("tunneldweller", StringComparison.OrdinalIgnoreCase) ||
               shortName.Contains("scarecrow", StringComparison.OrdinalIgnoreCase);
    }
}
