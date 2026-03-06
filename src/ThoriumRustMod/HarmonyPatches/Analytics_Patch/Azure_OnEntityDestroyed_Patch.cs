using Facepunch.Rust;
using HarmonyLib;
using ThoriumRustMod.Services;

namespace ThoriumRustMod.HarmonyPatches.Analytics_Patch;

public class Azure_OnEntityDestroyed_Patch
{
    [HarmonyPrefix]
    public static void OnEntityDestroyed(BaseEntity entity)
    {
        try
        {
            if (!DataHandler.IsConfigured) return;
            if (entity == null || entity.net == null) return;
            if (DataHandler.EntityEventBuffer.Length > DataHandler.MaxCacheSize) return;

            DataHandler.EntityEventCount++;
            var cache = DataHandler.EntityEventBuffer;

            BinaryEventWriter.WriteBool(cache, false);
            BinaryEventWriter.WriteInt64(cache, (long)entity.net.ID.Value);
        }
        catch
        {
        }
    }
}
