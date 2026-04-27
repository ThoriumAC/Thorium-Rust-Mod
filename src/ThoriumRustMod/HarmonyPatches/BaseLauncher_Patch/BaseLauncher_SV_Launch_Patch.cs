using System;
using ThoriumRustMod.Core;
using ThoriumRustMod.HarmonyPatches.Utility;
using ThoriumRustMod.Models;
using ThoriumRustMod.Services;

namespace ThoriumRustMod.HarmonyPatches.BaseLauncher_Patch;

internal static class BaseLauncher_SV_Launch_Patch
{
    [HarmonyLib.HarmonyPrefix]
    private static void Prefix(BaseLauncher __instance)
    {
        try
        {
            if (!DataHandler.IsConfigured || __instance == null)
                return;

            var player = __instance.GetOwnerPlayer();
            if (player == null)
                return;

            var steamId = Helpers.GetSteamIdOrZero(player);
            if (steamId <= 0)
                return;

            var explosiveShortName = __instance.primaryMagazine?.ammoType?.shortname ??
                                     __instance.GetItem()?.info?.shortname;

            PlayerServerStatsTracker.RecordExplosiveUsed(
                steamId,
                player.displayName,
                explosiveShortName,
                1,
                PlayerSnapshot.GetUnixTimestampMsCached());
        }
        catch (Exception ex)
        {
            Log.Error("Error in BaseLauncher.SV_Launch patch: " + ex);
        }
    }
}
