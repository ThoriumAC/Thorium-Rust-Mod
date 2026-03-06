using System;
using HarmonyLib;
using Network;
using ThoriumRustMod.HarmonyPatches.Utility;
using ThoriumRustMod.Models;
using ThoriumRustMod.Services;
using UnityEngine;

namespace ThoriumRustMod.HarmonyPatches.ServerMgr_Patch;

internal static class ServerMgr_OnPlayerTick_Patch
{
    [HarmonyPrefix]
    private static void Prefix(Message packet)
    {
        try
        {
            if (!DataHandler.IsConfigured) return;

            var player = packet.Player();
            if (player == null) return;

            var steamId = Helpers.GetSteamIdOrZero(player);
            if (steamId == 0) return;

            var position = packet.read.Position;

            var playerTick = packet.read.Proto(null as PlayerTick);
            packet.read.Position = position;
            if (playerTick == null) return;

            var inputState = playerTick.inputState;
            var modelState = playerTick.modelState;
            var pos = player.tickInterpolator.EndPoint;
            var eyePos = playerTick.eyePos;
            var velocity = player.estimatedVelocity;
            var viewAngles = player.viewAngles;
            var flags = player.playerFlags;

            var snapshot = PlayerSnapshot.Create(pos, player, SnapshotTypeEnums.PlayerTick,
                CombatData.FromPlayer(player), velocity, player.IsOnGround(), inputState);

            snapshot.EyesViewMode = (flags & BasePlayer.PlayerFlags.EyesViewmode) != 0;
            snapshot.ThirdPersonViewMode = (flags & BasePlayer.PlayerFlags.ThirdPersonViewmode) != 0;
            snapshot.ViewAnglesX = viewAngles.x;
            snapshot.ViewAnglesY = viewAngles.y;
            snapshot.ViewAnglesZ = viewAngles.z;
            snapshot.EyesPositionX = eyePos.x;
            snapshot.EyesPositionY = eyePos.y;
            snapshot.EyesPositionZ = eyePos.z;

            snapshot.ActiveItemId = playerTick.activeItem.Value;
            snapshot.ParentId = playerTick.parentID.Value;
            snapshot.DeltaMs = playerTick.deltaMs;

            snapshot.WaterLevel = modelState.waterLevel;
            var lookDir = modelState.lookDir;
            snapshot.LookDirX = lookDir.x;
            snapshot.LookDirY = lookDir.y;
            snapshot.LookDirZ = lookDir.z;
            snapshot.PoseType = modelState.poseType;
            var inheritedVel = modelState.inheritedVelocity;
            snapshot.InheritedVelocityX = inheritedVel.x;
            snapshot.InheritedVelocityY = inheritedVel.y;
            snapshot.InheritedVelocityZ = inheritedVel.z;

            AntiCheatSnapshotProcessor.Enqueue(steamId, snapshot);
        }
        catch
        {
        }
    }
}