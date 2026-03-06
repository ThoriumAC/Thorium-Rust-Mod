using System;
using System.Buffers;
using HarmonyLib;
using Network;
using ThoriumRustMod.Services;
using UnityEngine;

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
            var rawPacketData = Array.Empty<byte>();
            byte[]? rentedBuffer = null;
            int rawPacketLength = 0;

            try
            {
                player = packet.connection?.player as BasePlayer;

                readStream.Position = 1;
                entityId = readStream.EntityID();
                rpcId = readStream.UInt32();

                var payloadStream = readStream.stream;
                if (payloadStream != null && payloadStream._length > 0 && payloadStream._buffer != null)
                {
                    rawPacketLength = payloadStream._length;
                    rentedBuffer = ArrayPool<byte>.Shared.Rent(rawPacketLength);
                    rawPacketData = rentedBuffer;
                    Buffer.BlockCopy(payloadStream._buffer, 0, rawPacketData, 0, rawPacketLength);
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
            BinaryEventWriter.WriteString(cache, player?.UserIDString ?? string.Empty);
            BinaryEventWriter.WriteInt64(cache, (long)entityId.Value);
            BinaryEventWriter.WriteSingle(cache, Time.time);
            BinaryEventWriter.WriteInt32(cache, Time.frameCount);
            BinaryEventWriter.WriteVector(cache, player != null ? player.transform.position : Vector3.zero);
            BinaryEventWriter.WriteInt32(cache, -(rawPacketLength + 1));
            if (rawPacketLength > 0)
                cache.Write(rawPacketData, 0, rawPacketLength);

            if (rentedBuffer != null)
                ArrayPool<byte>.Shared.Return(rentedBuffer);
        }
        catch
        {
        }
    }
}
