using System;
using System.Reflection;
using Facepunch.Rust;
using HarmonyLib;
using Network;
using ThoriumRustMod.HarmonyPatches.Analytics_Patch;
using ThoriumRustMod.HarmonyPatches.BaseLauncher_Patch;
using ThoriumRustMod.HarmonyPatches.BaseCombatEntity_Patch;
using ThoriumRustMod.HarmonyPatches._OnRpcMessage_Patch;
using ThoriumRustMod.HarmonyPatches.BaseNetworkable_Patch;
using ThoriumRustMod.HarmonyPatches.Planner_Patch;
using ThoriumRustMod.HarmonyPatches.BasePlayer_Patch;
using ThoriumRustMod.HarmonyPatches.BaseProjectile_Patch;
using ThoriumRustMod.HarmonyPatches.ServerMgr_Patch;
using ThoriumRustMod.HarmonyPatches.ThrownWeapon_Patch;

namespace ThoriumRustMod.Core;

internal static class ThoriumPatchRegistry
{
    private static Harmony? _harmony;

    public static string? LastFailedPatch { get; private set; }

    public static bool ApplyAll(Harmony harmony)
    {
        _harmony = harmony;
        LastFailedPatch = null;
        var rpcMessageType = AccessTools.TypeByName("RPCMessage");

        return
            Apply("ServerMgr.OnRPCMessage",
                () => AccessTools.Method(typeof(ServerMgr), nameof(ServerMgr.OnRPCMessage), new[] { typeof(Message) }),
                prefix: new HarmonyMethod(typeof(BaseNetworkable_OnRpcMessage_Patch), "Prefix")) &&

            Apply("BaseNetworkable.Kill",
                () => AccessTools.Method(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill),
                    new[] { typeof(BaseNetworkable.DestroyMode), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(PatchBaseNetworkableKill), "Prefix")) &&

            Apply("BaseNetworkable.Kill.EntityEvent",
                () => AccessTools.Method(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill),
                    new[] { typeof(BaseNetworkable.DestroyMode), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(Azure_OnEntityDestroyed_Patch), "OnEntityDestroyed"),
                required: false) &&

            Apply("Planner.DoBuild",
                () => AccessTools.Method(typeof(Planner), "DoBuild",
                    new[] { typeof(Construction.Target), typeof(Construction) }),
                postfix: new HarmonyMethod(typeof(Planner_DoBuild_Patch), "Postfix"),
                required: false) &&

            Apply("BaseCombatEntity.Die",
                () => AccessTools.Method(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Die), new[] { typeof(HitInfo) }),
                prefix: new HarmonyMethod(typeof(BaseCombatEntity_Die_Patch), "Prefix")) &&

            Apply("BasePlayer.Die",
                () => AccessTools.Method(typeof(BasePlayer), nameof(BasePlayer.Die), new[] { typeof(HitInfo) }),
                prefix: new HarmonyMethod(typeof(BasePlayer_Die_Patch), "Prefix")) &&

            Apply("BasePlayer.Hurt",
                () => AccessTools.Method(typeof(BasePlayer), nameof(BasePlayer.Hurt), new[] { typeof(HitInfo) }),
                prefix: new HarmonyMethod(typeof(BasePlayer_Hurt_Patch), "Prefix")) &&

            Apply("BasePlayer.GiveItem",
                FindBasePlayerGiveItemMethod,
                prefix: new HarmonyMethod(typeof(BasePlayer_GiveItem_Patch), "Prefix"),
                required: false) &&

            Apply("BaseProjectile.CLProject",
                () => rpcMessageType == null ? null : AccessTools.Method(typeof(BaseProjectile), "CLProject", new[] { rpcMessageType }),
                prefix: new HarmonyMethod(typeof(BaseProjectile_CLProject_Patch), "Prefix"),
                required: false) &&

            Apply("BaseLauncher.SV_Launch",
                () => rpcMessageType == null ? null : AccessTools.Method(typeof(BaseLauncher), "SV_Launch", new[] { rpcMessageType }),
                prefix: new HarmonyMethod(typeof(BaseLauncher_SV_Launch_Patch), "Prefix"),
                required: false) &&

            Apply("ThrownWeapon.DoThrow",
                () => rpcMessageType == null ? null : AccessTools.Method(typeof(ThrownWeapon), "DoThrow", new[] { rpcMessageType }),
                prefix: new HarmonyMethod(typeof(ThrownWeapon_DoThrow_Patch), "Prefix"),
                required: false) &&

            Apply("BasePlayer.OnDisconnected",
                () => AccessTools.Method(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected)),
                prefix: new HarmonyMethod(typeof(BasePlayer_OnDisconnected_Patch), "Prefix")) &&

            Apply("BasePlayer.PlayerInit",
                () => AccessTools.Method(typeof(BasePlayer), nameof(BasePlayer.PlayerInit)),
                postfix: new HarmonyMethod(typeof(BasePlayer_PlayerInit_Patch), "Postfix")) &&

            Apply("ServerMgr.Initialize",
                () => AccessTools.Method(typeof(ServerMgr), nameof(ServerMgr.Initialize)),
                postfix: new HarmonyMethod(typeof(Patch_OpenConnection), "Postfix")) &&

            Apply("BasePlayer.OnReceiveTick",
                () => AccessTools.Method(typeof(BasePlayer), "OnReceiveTick", new[] { typeof(PlayerTick), typeof(bool) }),
                postfix: new HarmonyMethod(typeof(BasePlayer_OnReceiveTick_Patch), "Postfix"));
    }

    public static void UnpatchAll()
    {
        try { _harmony?.UnpatchAll(_harmony.Id); }
        catch (Exception ex) { Log.Warning($"[PatchRegistry] Error unpatching: {ex.Message}"); }
    }

    private static bool Apply(string name, Func<MethodInfo?> getOriginal,
        HarmonyMethod? prefix = null, HarmonyMethod? postfix = null, bool required = true)
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
            if (!required)
            {
                Log.Warning($"[PatchRegistry] Skipping optional patch {name}: {ex.Message}");
                return true;
            }

            LastFailedPatch = name;
            Log.Error($"[PatchRegistry] Failed to patch {name}: {ex.Message}");
            return false;
        }
    }

    private static MethodInfo? FindBasePlayerGiveItemMethod()
    {
        foreach (var method in typeof(BasePlayer).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!string.Equals(method.Name, nameof(BasePlayer.GiveItem), StringComparison.Ordinal))
                continue;

            var parameters = method.GetParameters();
            if (parameters.Length < 2 || parameters[0].ParameterType != typeof(Item))
                continue;

            var reasonType = parameters[1].ParameterType;
            if (reasonType.IsEnum || reasonType.Name.Contains("GiveItemReason", StringComparison.Ordinal))
                return method;
        }

        return null;
    }
}
