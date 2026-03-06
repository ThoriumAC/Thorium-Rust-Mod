using HarmonyLib;

namespace ThoriumRustMod.HarmonyPatches.ServerMgr_Patch;

internal class Patch_OpenConnection
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        ThoriumLoader.OnServerStarted();
    }
}