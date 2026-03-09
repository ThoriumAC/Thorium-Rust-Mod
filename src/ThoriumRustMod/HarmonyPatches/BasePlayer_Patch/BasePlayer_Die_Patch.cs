using System;
using HarmonyLib;
using ThoriumRustMod.Core;
using ThoriumRustMod.HarmonyPatches.Utility;
using ThoriumRustMod.Models;
using ThoriumRustMod.Services;
using UnityEngine;

namespace ThoriumRustMod.HarmonyPatches.BasePlayer_Patch;

internal static class BasePlayer_Die_Patch
{
    [HarmonyPrefix]
    private static void Prefix(BasePlayer __instance, HitInfo info)
    {
        try
        {
            if (!DataHandler.IsConfigured) return;

            var victimId = Helpers.GetSteamIdUlongOrZero(__instance);
            if (info == null || __instance == null || victimId == 0UL || __instance.IsSleeping()) return;

            var initiator = info.InitiatorPlayer;
            var initiatorId = Helpers.GetSteamIdUlongOrZero(initiator);
            if (initiator == null || initiatorId == 0UL) return;

            var projectileId = info.ProjectileID;

            var activeItem = initiator.GetActiveItem();
            if (DataHandler.KillEventBuffer.Length > DataHandler.MaxCacheSize) return;

            DataHandler.KillEventCount++;
            var KillEventBuffer = DataHandler.KillEventBuffer;
            var weaponShort = activeItem == null ? "None" : activeItem.info.shortname;
            var action = initiator == __instance ? "Suicide" : weaponShort;
            var distance = Vector3.Distance(__instance.transform.position,
                initiator.transform.position);

            string? boneName = null;
            try
            {
                var skeleton = __instance.skeletonProperties;
                boneName = skeleton?.FindBone(info.HitBone)?.boneName;
            }
            catch
            {
            }

            BinaryEventWriter.WriteInt64(KillEventBuffer, PlayerSnapshot.GetUnixTimestampMsCached());
            BinaryEventWriter.WriteString(KillEventBuffer, __instance.UserIDString);
            BinaryEventWriter.WriteString(KillEventBuffer, initiator.UserIDString);
            BinaryEventWriter.WriteString(KillEventBuffer, __instance.displayName);
            BinaryEventWriter.WriteString(KillEventBuffer, initiator.displayName);
            BinaryEventWriter.WriteString(KillEventBuffer, action);
            BinaryEventWriter.WriteSingle(KillEventBuffer, distance);
            BinaryEventWriter.WriteVector(KillEventBuffer, __instance.transform.position);
            BinaryEventWriter.WriteString(KillEventBuffer, boneName);
            BinaryEventWriter.WriteInt32(KillEventBuffer, projectileId);
            BinaryEventWriter.WriteBool(KillEventBuffer, info.isHeadshot);

            try
            {
                var steamId = unchecked((long)victimId);
                var pos = __instance.transform.position;
                var snapshot = PlayerSnapshot.Create(pos, __instance, SnapshotTypeEnums.Die,
                    CombatData.FromPlayer(initiator), __instance.estimatedVelocity, __instance.IsOnGround());

                AntiCheatSnapshotProcessor.Enqueue(steamId, snapshot);
            }
            catch
            {
            }
        }
        catch (Exception ex)
        {
            Log.Error("Error in Die patch: " + ex);
        }
    }
}