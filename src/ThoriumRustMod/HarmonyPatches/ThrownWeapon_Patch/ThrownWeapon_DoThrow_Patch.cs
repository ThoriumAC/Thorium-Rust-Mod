using System;
using ThoriumRustMod.Core;
using ThoriumRustMod.HarmonyPatches.Utility;
using ThoriumRustMod.Models;
using ThoriumRustMod.Services;

namespace ThoriumRustMod.HarmonyPatches.ThrownWeapon_Patch;

internal static class ThrownWeapon_DoThrow_Patch
{
    [HarmonyLib.HarmonyPrefix]
    private static void Prefix(ThrownWeapon __instance)
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

            PlayerServerStatsTracker.RecordExplosiveUsed(
                steamId,
                player.displayName,
                __instance.GetItem()?.info?.shortname,
                1,
                PlayerSnapshot.GetUnixTimestampMsCached());
        }
        catch (Exception ex)
        {
            Log.Error("Error in ThrownWeapon.DoThrow patch: " + ex);
        }
    }
}
