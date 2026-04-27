using System;
using ThoriumRustMod.Core;
using ThoriumRustMod.HarmonyPatches.Utility;
using ThoriumRustMod.Models;
using ThoriumRustMod.Services;

namespace ThoriumRustMod.HarmonyPatches.BasePlayer_Patch;

internal static class BasePlayer_GiveItem_Patch
{
    [HarmonyLib.HarmonyPrefix]
    private static void Prefix(Item item, object reason, BasePlayer __instance)
    {
        try
        {
            if (!DataHandler.IsConfigured || __instance == null || item == null)
                return;

            var reasonValue = Convert.ToInt32(reason);
            if (reasonValue != 1 && reasonValue != 2)
                return;

            var steamId = Helpers.GetSteamIdOrZero(__instance);
            if (steamId <= 0)
                return;

            PlayerServerStatsTracker.RecordItemGranted(
                steamId,
                __instance.displayName,
                item.info?.shortname,
                item.amount,
                PlayerSnapshot.GetUnixTimestampMsCached());
        }
        catch (Exception ex)
        {
            Log.Error("Error in GiveItem patch: " + ex);
        }
    }
}
