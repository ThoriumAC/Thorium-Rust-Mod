using System;
using System.Reflection;
using Facepunch.Rust;
using HarmonyLib;
using Network;
using ThoriumRustMod.HarmonyPatches._OnRpcMessage_Patch;
using ThoriumRustMod.HarmonyPatches.Analytics_Patch;
using ThoriumRustMod.HarmonyPatches.BaseNetworkable_Patch;
using ThoriumRustMod.HarmonyPatches.BasePlayer_Patch;
using ThoriumRustMod.HarmonyPatches.ServerMgr_Patch;

namespace ThoriumRustMod.Core;

internal static class ThoriumPatchRegistry
{
    private static Harmony? _harmony;

    public static string? LastFailedPatch { get; private set; }

    public static bool ApplyAll(Harmony harmony)
    {
        _harmony = harmony;
        LastFailedPatch = null;

        return
            Apply("ServerMgr.OnRPCMessage",
                () => AccessTools.Method(typeof(ServerMgr), nameof(ServerMgr.OnRPCMessage), new[] { typeof(Message) }),
                prefix: new HarmonyMethod(typeof(BaseNetworkable_OnRpcMessage_Patch), "Prefix")) &&

            Apply("Analytics.Azure.OnEntityBuilt",
                () => AccessTools.Method(typeof(Analytics.Azure), "OnEntityBuilt"),
                prefix: new HarmonyMethod(typeof(Azure_OnEntityBuilt_Patch), "Prefix")) &&

            Apply("Analytics.Azure.OnEntityDestroyed",
                () => AccessTools.Method(typeof(Analytics.Azure), "OnEntityDestroyed"),
                prefix: new HarmonyMethod(typeof(Azure_OnEntityDestroyed_Patch), "OnEntityDestroyed")) &&

            Apply("BaseNetworkable.Kill",
                () => AccessTools.Method(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill),
                    new[] { typeof(BaseNetworkable.DestroyMode), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(PatchBaseNetworkableKill), "Prefix")) &&

            Apply("BasePlayer.Die",
                () => AccessTools.Method(typeof(BasePlayer), nameof(BasePlayer.Die), new[] { typeof(HitInfo) }),
                prefix: new HarmonyMethod(typeof(BasePlayer_Die_Patch), "Prefix")) &&

            Apply("BasePlayer.Hurt",
                () => AccessTools.Method(typeof(BasePlayer), nameof(BasePlayer.Hurt), new[] { typeof(HitInfo) }),
                prefix: new HarmonyMethod(typeof(BasePlayer_Hurt_Patch), "Prefix")) &&

            Apply("BasePlayer.OnDisconnected",
                () => AccessTools.Method(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected)),
                prefix: new HarmonyMethod(typeof(BasePlayer_OnDisconnected_Patch), "Prefix")) &&

            Apply("BasePlayer.PlayerInit",
                () => AccessTools.Method(typeof(BasePlayer), nameof(BasePlayer.PlayerInit)),
                postfix: new HarmonyMethod(typeof(BasePlayer_PlayerInit_Patch), "Postfix")) &&

            Apply("ServerMgr.OpenConnection",
                () => AccessTools.Method(typeof(ServerMgr), nameof(ServerMgr.OpenConnection)),
                postfix: new HarmonyMethod(typeof(Patch_OpenConnection), "Postfix")) &&

            Apply("ServerMgr.OnPlayerTick",
                () => AccessTools.Method(typeof(ServerMgr), nameof(ServerMgr.OnPlayerTick), new[] { typeof(Message) }),
                prefix: new HarmonyMethod(typeof(ServerMgr_OnPlayerTick_Patch), "Prefix"));
    }

    public static void UnpatchAll()
    {
        try { _harmony?.UnpatchAll(_harmony.Id); }
        catch (Exception ex) { Log.Warning($"[PatchRegistry] Error unpatching: {ex.Message}"); }
    }

    private static bool Apply(string name, Func<MethodInfo?> getOriginal,
        HarmonyMethod? prefix = null, HarmonyMethod? postfix = null)
    {
        try
        {
            var original = getOriginal();
            if (original == null)
                throw new Exception("method not found");

            _harmony!.Patch(original, prefix, postfix);
            Log.Debug(() => $"[PatchRegistry] Patched {name}");
            return true;
        }
        catch (Exception ex)
        {
            LastFailedPatch = name;
            Log.Error($"[PatchRegistry] Failed to patch {name}: {ex.Message}");
            return false;
        }
    }
}
