using HarmonyLib;
using ThoriumRustMod.HarmonyPatches.Utility;
using ThoriumRustMod.Models;
using ThoriumRustMod.Services;

namespace ThoriumRustMod.HarmonyPatches.BasePlayer_Patch;

internal static class BasePlayer_OnReceiveTick_Patch
{
    [HarmonyPostfix]
    private static void Postfix(BasePlayer __instance, PlayerTick msg)
    {
        try
        {
            if (!DataHandler.IsConfigured) return;

            var steamId = Helpers.GetSteamIdOrZero(__instance);
            if (steamId == 0) return;

            var ind = __instance.ActivePlayerInd;
            var cached = ind >= 0 ? BasePlayer.CachedStates[ind] : BasePlayer.CachedState.Default;

            var pos = __instance.ServerPosition;
            var velocity = __instance.estimatedVelocity;
            var viewAngles = __instance.viewAngles;

            var snapshot = PlayerSnapshot.CreateFromTick(pos, __instance, steamId, in cached,
                CombatData.FromPlayerCached(__instance, in cached), velocity, msg.inputState, viewAngles);

            PlayerServerStatsTracker.RecordTick(__instance, steamId, snapshot.CombatData, snapshot.TickTimestampUnixMs);

            snapshot.EyesViewMode = (cached.PlayerFlags & BasePlayer.PlayerFlags.EyesViewmode) != 0;
            snapshot.ThirdPersonViewMode = (cached.PlayerFlags & BasePlayer.PlayerFlags.ThirdPersonViewmode) != 0;
            snapshot.ViewAnglesX = viewAngles.x;
            snapshot.ViewAnglesY = viewAngles.y;
            snapshot.ViewAnglesZ = viewAngles.z;
            snapshot.EyesPositionX = cached.EyePos.x;
            snapshot.EyesPositionY = cached.EyePos.y;
            snapshot.EyesPositionZ = cached.EyePos.z;

            snapshot.ActiveItemId = msg.activeItem.Value;
            snapshot.ParentId = msg.parentID.Value;
            snapshot.DeltaMs = msg.deltaMs;

            snapshot.WaterLevel = cached.WaterFactor;

            var modelState = msg.modelState;
            if (modelState != null)
            {
                var lookDir = modelState.lookDir;
                snapshot.LookDirX = lookDir.x;
                snapshot.LookDirY = lookDir.y;
                snapshot.LookDirZ = lookDir.z;
                snapshot.PoseType = modelState.poseType;
                var inheritedVel = modelState.inheritedVelocity;
                snapshot.InheritedVelocityX = inheritedVel.x;
                snapshot.InheritedVelocityY = inheritedVel.y;
                snapshot.InheritedVelocityZ = inheritedVel.z;
                snapshot.ClientModelStateFlags = modelState.flags;
            }

            snapshot.WaterFactor = cached.WaterFactor;
            snapshot.IsSwimming = cached.IsSwimming;
            snapshot.IsDiving = cached.WaterFactor > 0.75f;

            AntiCheatSnapshotProcessor.Enqueue(steamId, snapshot);
        }
        catch
        {
        }
    }
}
