using System;
using HarmonyLib;
using ThoriumRustMod.Core;
using ThoriumRustMod.HarmonyPatches.Utility;
using ThoriumRustMod.Models;
using ThoriumRustMod.Services;
using UnityEngine;

namespace ThoriumRustMod.HarmonyPatches.BaseNetworkable_Patch;

internal static class PatchBaseNetworkableKill
{
    [HarmonyPrefix]
    private static void Prefix(BaseNetworkable __instance)
    {
        try
        {
            if (!DataHandler.IsConfigured) return;
            OnEntityKill(__instance);
        }
        catch (Exception ex)
        {
            Log.Error("Error in OnEntityKill: " + ex);
        }
    }

    private static void OnEntityKill(BaseNetworkable networkable)
    {
        if (networkable is not BaseEntity entity) return;
        if (entity == null || entity.net == null) return;

        var id = Helpers.TryExtractNetId(entity);

        var ownerId = entity.OwnerID;
        if (ownerId <= 0) return;

        var steamId = (long)ownerId;
        var pos = entity.transform.position;
        var snapshot = new EventSnapshot
        {
            Tick = (long)(Time.time * 1000),
            TickTimestampUnixMs = PlayerSnapshot.GetUnixTimestampMsCached(),
            TickIntervalMs = Time.deltaTime * 1000f,
            PosX = pos.x,
            PosY = pos.y,
            PosZ = pos.z,
            SnapshotType = SnapshotTypeEnums.EntityKill,
            EventType = "entity_kill",
        };
        snapshot.SetEntityKillData(entity.prefabID, id, ownerId);

        AntiCheatSnapshotProcessor.Enqueue(steamId, snapshot);
    }
}