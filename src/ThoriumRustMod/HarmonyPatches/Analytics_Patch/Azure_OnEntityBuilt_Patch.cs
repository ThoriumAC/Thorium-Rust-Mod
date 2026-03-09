using Facepunch.Rust;
using HarmonyLib;
using ThoriumRustMod.Services;
using UnityEngine;

namespace ThoriumRustMod.HarmonyPatches.Analytics_Patch;

public class Azure_OnEntityBuilt_Patch
{
    [HarmonyPrefix]
    public static void Prefix(BaseEntity entity, BasePlayer player)
    {
        try
        {
            if (!DataHandler.IsConfigured) return;
            if (entity == null || entity.net == null || player == null) return;
            if (DataHandler.EntityEventBuffer.Length > DataHandler.MaxCacheSize) return;

            DataHandler.EntityEventCount++;
            var cache = DataHandler.EntityEventBuffer;

            BinaryEventWriter.WriteBool(cache, true);
            BinaryEventWriter.WriteInt64(cache, (long)entity.net.ID.Value);
            BinaryEventWriter.WriteString(cache, player.UserIDString);
            BinaryEventWriter.WriteUint(cache, entity.prefabID);
            BinaryEventWriter.WriteString(cache, entity.ShortPrefabName);
            BinaryEventWriter.WriteVector(cache, entity.ServerPosition);
            BinaryEventWriter.WriteVector(cache, entity.ServerRotation.eulerAngles);
            BinaryEventWriter.WriteVector(cache, entity.CenterPoint());
            BinaryEventWriter.WriteVector(cache, entity.bounds.extents);
        }
        catch
        {
        }
    }
}
