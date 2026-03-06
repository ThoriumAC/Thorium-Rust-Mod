using HarmonyLib;
using ThoriumRustMod.Core;
using ThoriumRustMod.HarmonyPatches.Utility;
using ThoriumRustMod.Models;
using ThoriumRustMod.Services;
using UnityEngine;

namespace ThoriumRustMod.HarmonyPatches.BasePlayer_Patch;

internal static class BasePlayer_MoveItem_Patch
{
    [HarmonyPrefix]
    private static void Prefix(BaseEntity.RPCMessage msg)
    {
        try
        {
            if (!DataHandler.IsConfigured) return;

            var player = msg.player;

            if (player == null) return;
            if (player.IsAdmin || player.IsDeveloper) return;
            if (DataHandler.RpcEventBuffer.Length > DataHandler.MaxCacheSize) return;
            DataHandler.RpcEventCount++;
            var RpcEventBuffer = DataHandler.RpcEventBuffer;
            BinaryEventWriter.WriteInt32(RpcEventBuffer, 0);
            BinaryEventWriter.WriteUint(RpcEventBuffer, 3041092525u);
            BinaryEventWriter.WriteString(RpcEventBuffer, player.UserIDString);
            BinaryEventWriter.WriteUint(RpcEventBuffer, player.prefabID);
            BinaryEventWriter.WriteSingle(RpcEventBuffer, Time.time);
            BinaryEventWriter.WriteInt32(RpcEventBuffer, Time.frameCount);

            const int itemId = 0;
            BinaryEventWriter.WriteInt32(RpcEventBuffer, itemId);

            var read = msg.read;
            if (read != null)
            {
                var stream = read.stream;
                var buffer = stream._buffer;
                var length = stream._length;

                BinaryEventWriter.WriteCappedBytes(RpcEventBuffer, buffer, length);
            }

            var steamId = Helpers.GetSteamIdOrZero(player);
            var pos = player.transform.position;
            var combat = CombatData.Get();
            combat.Weapon = itemId.ToString();
            var snapshot = PlayerSnapshot.Create(pos, player, SnapshotTypeEnums.MoveItem, 
                combat,
                player.estimatedVelocity, player.IsOnGround());

            AntiCheatSnapshotProcessor.Enqueue(steamId, snapshot);
        }
        catch (Exception ex)
        {
            Log.Exception(ex);
        }
    }
}
