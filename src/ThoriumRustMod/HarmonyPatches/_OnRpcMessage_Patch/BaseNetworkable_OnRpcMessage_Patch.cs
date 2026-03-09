using System;
using HarmonyLib;
using Network;
using ThoriumRustMod.Services;

namespace ThoriumRustMod.HarmonyPatches._OnRpcMessage_Patch;

internal static class BaseNetworkable_OnRpcMessage_Patch
{
    [HarmonyPrefix]
    private static void Prefix(Message packet)
    {
        try
        {
            if (!DataHandler.IsConfigured) return;

            if (packet.type != Message.Type.RPCMessage) return;

            var readStream = packet.read;
            if (readStream == null) return;

            var savedPosition = readStream.Position;

            BasePlayer? player;
            uint rpcId;
            NetworkableId entityId;
            byte[]? rawBuffer = null;
            int rawLength = 0;

            try
            {
                player = packet.connection?.player as BasePlayer;

                readStream.Position = 1;
                entityId = readStream.EntityID();
                rpcId = readStream.UInt32();

                var payloadStream = readStream.stream;
                if (payloadStream != null && payloadStream._length > 0 && payloadStream._buffer != null)
                {
                    rawBuffer = payloadStream._buffer;
                    rawLength = payloadStream._length;
                }
            }
            catch
            {
                return;
            }
            finally
            {
                readStream.Position = savedPosition;
            }

            if (player != null && ThoriumLoader.RpcInterceptors?.TryGetValue(rpcId, out var action) == true)
            {
                try
                {
                    var entity = BaseNetworkable.serverEntities.Find(entityId) as BaseEntity;
                    action(player, entity);
                }
                catch
                {
                }
            }

            if (DataHandler.RpcEventBuffer.Length > DataHandler.MaxCacheSize) return;

            DataHandler.RpcEventCount++;
            var cache = DataHandler.RpcEventBuffer;

            BinaryEventWriter.WriteInt32(cache, 1);
            BinaryEventWriter.WriteUint(cache, rpcId);
            BinaryEventWriter.WriteString(cache, player?.UserIDString);
            BinaryEventWriter.WriteInt64(cache, (long)entityId.Value);
            BinaryEventWriter.WriteSingle(cache, UnityEngine.Time.time);
            BinaryEventWriter.WriteInt32(cache, UnityEngine.Time.frameCount);
            BinaryEventWriter.WriteVector(cache, player != null ? player.transform.position : UnityEngine.Vector3.zero);
            BinaryEventWriter.WriteInt32(cache, -(rawLength + 1));
            if (rawLength > 0)
                cache.Write(rawBuffer!, 0, rawLength);
        }
        catch
        {
        }
    }
}
