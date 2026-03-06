using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
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
            File.WriteAllText("thorium_basenetworkable_kill_error.txt", ex.ToString());
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
            TickTimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            TickIntervalMs = Time.deltaTime * 1000f,
            PosX = pos.x,
            PosY = pos.y,
            PosZ = pos.z,
            SnapshotType = SnapshotTypeEnums.EntityKill,
            MouseDX = 0,
            MouseDY = 0,
            AimYaw = 0,
            AimPitch = 0,
            EventType = "entity_kill",
            EventData = new Dictionary<string, string>
            {
                ["prefabID"] = entity.prefabID.ToString(),
                ["netID"] = id.ToString(),
                ["owner"] = entity.OwnerID.ToString()
            }
        };

        AntiCheatSnapshotProcessor.Enqueue(steamId, snapshot);
    }
}