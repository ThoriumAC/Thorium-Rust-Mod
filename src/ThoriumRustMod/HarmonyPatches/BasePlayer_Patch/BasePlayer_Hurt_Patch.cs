using System;
using System.IO;
using HarmonyLib;
using ThoriumRustMod.Core;
using ThoriumRustMod.HarmonyPatches.Utility;
using ThoriumRustMod.Models;
using ThoriumRustMod.Services;
using UnityEngine;

namespace ThoriumRustMod.HarmonyPatches.BasePlayer_Patch;

internal static class BasePlayer_Hurt_Patch
{
    [HarmonyPrefix]
    private static void Prefix(BasePlayer __instance, HitInfo info)
    {
        try
        {
            if (!DataHandler.IsConfigured) return;

            var victimId = Helpers.GetSteamIdOrZero(__instance);
            if (victimId == 0L) return;

            var initiator = info.InitiatorPlayer;

            if (initiator == null)
            {
                var majority = (int)info.damageTypes.GetMajorityDamageType();
                if (majority != 10 && majority != 15) return;

                try
                {
                    if (DataHandler.RpcEventBuffer.Length <= DataHandler.MaxCacheSize)
                    {
                        DataHandler.RpcEventCount++;
                        var RpcEventBuffer = DataHandler.RpcEventBuffer;
                        BinaryEventWriter.WriteInt32(RpcEventBuffer, 7);
                        BinaryEventWriter.WriteString(RpcEventBuffer, __instance.UserIDString);
                        BinaryEventWriter.WriteSingle(RpcEventBuffer, Time.time);

                        try
                        {
                            var pos = __instance.transform.position;
                            AntiCheatSnapshotProcessor.Enqueue(victimId,
                                PlayerSnapshot.Create(pos, __instance, SnapshotTypeEnums.HurtEnv,
                                    CombatData.Get()));
                        }
                        catch
                        {
                        }
                    }
                }
                catch (IOException)
                {
                    DataHandler.RpcEventBuffer.SetLength(0);
                    DataHandler.RpcEventBuffer.Position = 0;
                    DataHandler.RpcEventCount = 0;
                }

                return;
            }

            if (initiator == __instance) return;

            try
            {
                if (info.Weapon == null) return;

                var initiatorId = Helpers.GetSteamIdOrZero(initiator);
                if (initiatorId == 0L) return;

                if (info.Weapon.GetItem() == null) return;
                if (DataHandler.CombatEventBuffer.Length > DataHandler.MaxCacheSize) return;

                DataHandler.CombatEventCount++;
                var CombatEventBuffer = DataHandler.CombatEventBuffer;
                var itemid = 0;
                var weaponShortname = string.Empty;
                var isProjectile = false;
                var boneName = string.Empty;

                try
                {
                    var weapon = info.Weapon;
                    var weaponItem = weapon?.GetItem();
                    if (weaponItem != null)
                        weaponShortname = weaponItem.info.shortname;

                    var proj = weapon as BaseProjectile;
                    if (proj != null)
                    {
                        itemid = proj.PrimaryMagazineAmmo.itemid;
                        isProjectile = true;
                    }

                    if (info.HitBone != 0)
                    {
                        var skeleton = __instance.skeletonProperties;
                        var bone = skeleton?.FindBone(info.HitBone);
                        if (bone != null)
                            boneName = bone.boneName ?? string.Empty;
                    }
                }
                catch
                {
                }

                BinaryEventWriter.WriteInt64(CombatEventBuffer, (long)(Time.time * 1000));
                BinaryEventWriter.WriteInt64(CombatEventBuffer, initiatorId);
                BinaryEventWriter.WriteInt64(CombatEventBuffer, victimId);
                BinaryEventWriter.WriteInt32(CombatEventBuffer, itemid);
                BinaryEventWriter.WriteSingle(CombatEventBuffer, info.damageTypes.Total());
                BinaryEventWriter.WriteSingle(CombatEventBuffer, __instance.health);
                BinaryEventWriter.WriteInt32(CombatEventBuffer, info.ProjectileID);
                BinaryEventWriter.WriteSingle(CombatEventBuffer, initiator.Distance(__instance.transform.position));
                BinaryEventWriter.WriteString(CombatEventBuffer, weaponShortname);
                BinaryEventWriter.WriteBool(CombatEventBuffer, isProjectile);
                BinaryEventWriter.WriteBool(CombatEventBuffer, info.isHeadshot);
                BinaryEventWriter.WriteString(CombatEventBuffer, boneName);
                BinaryEventWriter.WriteVector(CombatEventBuffer, info.HitPositionWorld);

                if (isProjectile)
                {
                    BinaryEventWriter.WriteVector(CombatEventBuffer, info.ProjectileVelocity);
                    BinaryEventWriter.WriteSingle(CombatEventBuffer, info.ProjectileDistance);
                }
                else
                {
                    BinaryEventWriter.WriteVector(CombatEventBuffer, Vector3.zero);
                    BinaryEventWriter.WriteSingle(CombatEventBuffer, 0f);
                }

                try
                {
                    var pos = __instance.transform.position;
                    var snapshot = PlayerSnapshot.Create(pos, __instance, SnapshotTypeEnums.Hurt,
                        CombatData.FromPlayer(initiator), __instance.estimatedVelocity, __instance.IsOnGround());

                    AntiCheatSnapshotProcessor.Enqueue(victimId, snapshot);
                }
                catch
                {
                }

            }
            catch (IOException)
            {
                DataHandler.CombatEventBuffer.SetLength(0);
                DataHandler.CombatEventBuffer.Position = 0;
                DataHandler.CombatEventCount = 0;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Error in Hurt patch: " + ex);
        }
    }
}
