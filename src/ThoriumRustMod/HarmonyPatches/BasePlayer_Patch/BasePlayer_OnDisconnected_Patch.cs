using System;
using HarmonyLib;
using ThoriumRustMod.HarmonyPatches.Utility;
using ThoriumRustMod.Models;
using ThoriumRustMod.Services;

namespace ThoriumRustMod.HarmonyPatches.BasePlayer_Patch;

internal static class BasePlayer_OnDisconnected_Patch
{
    [HarmonyPrefix]
    private static void Prefix(BasePlayer __instance)
    {
        try
        {
            if (!DataHandler.IsConfigured || __instance == null) return;

            DataHandler.SessionEventCount++;
            var SessionEventBuffer = DataHandler.SessionEventBuffer;
            BinaryEventWriter.WriteInt64(SessionEventBuffer, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            BinaryEventWriter.WriteBool(SessionEventBuffer, false);
            BinaryEventWriter.WriteString(SessionEventBuffer, __instance.UserIDString ?? string.Empty);
            BinaryEventWriter.WriteString(SessionEventBuffer, __instance.displayName ?? string.Empty);

            var steamId = Helpers.GetSteamIdOrZero(__instance);
            if (steamId == 0) return;

            var pos = __instance.transform.position;
            var combat = CombatData.Get();
            combat.Weapon = string.Empty;
            AntiCheatSnapshotProcessor.Enqueue(steamId,
                PlayerSnapshot.Create(pos, __instance, SnapshotTypeEnums.Leave, combat));

            AntiCheatSnapshotProcessor.CleanupPlayer(steamId);
        }
        catch
        {
        }
    }
}