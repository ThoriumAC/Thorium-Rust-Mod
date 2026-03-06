using System;
using HarmonyLib;
using ThoriumRustMod.HarmonyPatches.Utility;
using ThoriumRustMod.Models;
using ThoriumRustMod.Services;

namespace ThoriumRustMod.HarmonyPatches.BasePlayer_Patch;

internal static class BasePlayer_PlayerInit_Patch
{
    [HarmonyPostfix]
    private static void Postfix(BasePlayer __instance)
    {
        try
        {
            if (!DataHandler.IsConfigured || __instance == null) return;

            DataHandler.SessionEventCount++;
            var SessionEventBuffer = DataHandler.SessionEventBuffer;

            var ip = __instance.Connection?.ipaddress ?? string.Empty;

            BinaryEventWriter.WriteInt64(SessionEventBuffer, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            BinaryEventWriter.WriteBool(SessionEventBuffer, true);
            BinaryEventWriter.WriteString(SessionEventBuffer, __instance.UserIDString ?? string.Empty);
            BinaryEventWriter.WriteString(SessionEventBuffer, ip);
            BinaryEventWriter.WriteString(SessionEventBuffer, __instance.displayName ?? string.Empty);

            var steamId = Helpers.GetSteamIdOrZero(__instance);
            if (steamId == 0) return;

            var pos = __instance.transform.position;
            var combat = CombatData.Get();
            combat.Weapon = ip;
            AntiCheatSnapshotProcessor.Enqueue(steamId, PlayerSnapshot.Create(pos, __instance, SnapshotTypeEnums.Join, combat));
        }
        catch
        {
        }
    }
}