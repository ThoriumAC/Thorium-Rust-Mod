using HarmonyLib;
using ThoriumRustMod.Models;
using ThoriumRustMod.Services;

namespace ThoriumRustMod.HarmonyPatches.Planner_Patch;

internal static class Planner_DoBuild_Patch
{
    [HarmonyPostfix]
    public static void Postfix(BaseEntity __result, Planner __instance)
    {
        try
        {
            if (!DataHandler.IsConfigured) return;
            if (__result == null || __result.net == null) return;
            if (DataHandler.EntityEventBuffer.Length > DataHandler.MaxCacheSize) return;

            var player = __instance.GetOwnerPlayer();
            if (player == null) return;

            PlayerServerStatsTracker.RecordEntityPlaced(player, __result, PlayerSnapshot.GetUnixTimestampMsCached());

            DataHandler.EntityEventCount++;
            var cache = DataHandler.EntityEventBuffer;

            BinaryEventWriter.WriteBool(cache, true);
            BinaryEventWriter.WriteInt64(cache, (long)__result.net.ID.Value);
            BinaryEventWriter.WriteString(cache, player.UserIDString);
            BinaryEventWriter.WriteUint(cache, __result.prefabID);
            BinaryEventWriter.WriteString(cache, __result.ShortPrefabName);
            BinaryEventWriter.WriteVector(cache, __result.ServerPosition);
            BinaryEventWriter.WriteVector(cache, __result.ServerRotation.eulerAngles);
            BinaryEventWriter.WriteVector(cache, __result.CenterPoint());
            BinaryEventWriter.WriteVector(cache, __result.bounds.extents);
        }
        catch
        {
        }
    }
}
