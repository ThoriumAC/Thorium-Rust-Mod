using System;
using ThoriumRustMod.HarmonyPatches.Utility;
using ThoriumRustMod.Models;

namespace ThoriumRustMod.Services;

/// <summary>
/// Manual Protocol Buffers serializer for the ThoriumBatch payload.
/// </summary>
internal static class ThoriumBatchProtobufSerializer
{
    public static byte[] Serialize(ThoriumBatch batch, ThoriumEventPayload? caches = null)
    {
        if (batch == null) throw new ArgumentNullException(nameof(batch));

        return ProtobufWireWriter.BuildMessage(w => WriteThoriumBatch(w, batch, caches));
    }

    private static void WriteThoriumBatch(ProtobufWireWriter w, ThoriumBatch batch, ThoriumEventPayload? caches)
    {
        // message ThoriumBatch {
        //   int64 start_tick = 1;
        //   int64 end_tick = 2;
        //   // Fields 3-6 removed: server metadata sent separately as initial JSON message
        //   repeated AntiCheatSnapshot snapshots = 8;
        //
        //   // Optional: raw binary caches from DataHandler (produced by various Harmony patches)
        //   bytes packet_cache = 9;
        //   bytes pvp_cache = 10;
        //   bytes join_cache = 11;
        //   bytes damage_cache = 12;
        //
        //   int64 total_packets = 13;
        //   int64 total_pvp_packets = 14;
        //   int64 total_join_packets = 15;
        //   int64 total_damage_packets = 16;
        //   bytes entity_cache = 17;
        //   int64 total_entity_packets = 18;
        // }

        if (batch.StartTick != 0)
        {
            w.WriteTag(1, ProtobufWireType.Varint);
            w.WriteInt64(batch.StartTick);
        }

        if (batch.EndTick != 0)
        {
            w.WriteTag(2, ProtobufWireType.Varint);
            w.WriteInt64(batch.EndTick);
        }


        foreach (var s in batch.Snapshots)
        {
            if (s == null) continue;
            w.WriteEmbeddedMessage(8, s, static (inner, snap) => WriteAntiCheatSnapshot(inner, snap));
        }

        if (caches != null)
        {
            WriteOptionalBytes(w, 9, caches.RpcEventBytes, caches.RpcEventLength);
            WriteOptionalBytes(w, 10, caches.KillEventBytes, caches.KillEventLength);
            WriteOptionalBytes(w, 11, caches.SessionEventBytes, caches.SessionEventLength);
            WriteOptionalBytes(w, 12, caches.CombatEventBytes, caches.CombatEventLength);

            WriteOptionalInt64(w, 13, caches.RpcEventCount);
            WriteOptionalInt64(w, 14, caches.KillEventCount);
            WriteOptionalInt64(w, 15, caches.SessionEventCount);
            WriteOptionalInt64(w, 16, caches.CombatEventCount);

            WriteOptionalBytes(w, 17, caches.EntityEventBytes, caches.EntityEventLength);
            WriteOptionalInt64(w, 18, caches.EntityEventCount);
        }
    }

    private static void WriteAntiCheatSnapshot(ProtobufWireWriter w, AntiCheatSnapshot snapshot)
    {
        // message AntiCheatSnapshot {
        //   int64 steam_id = 1;
        //   repeated PlayerSnapshot snapshots = 2;
        // }

        if (snapshot.SteamId != 0)
        {
            w.WriteTag(1, ProtobufWireType.Varint);
            w.WriteInt64(snapshot.SteamId);
        }

        foreach (var ps in snapshot.Snapshots)
        {
            if (ps == null) continue;
            w.WriteEmbeddedMessage(2, ps, static (inner, snap) => WritePlayerSnapshot(inner, snap));
        }
    }

    private static void WritePlayerSnapshot(ProtobufWireWriter w, PlayerSnapshot s)
    {
        // message PlayerSnapshot {
        //   int64 tick = 1;
        //   int64 tick_timestamp_unix_ms = 2;
        //   float tick_interval_ms = 3;
        //   float pos_x = 4;
        //   float pos_y = 5;
        //   float pos_z = 6;
        //   float vel_x = 7;
        //   float vel_y = 8;
        //   float vel_z = 9;
        //   bool is_grounded = 10;
        //   string stance = 11;
        //   int32 snapshot_type = 12;
        //   float mouse_dx = 13;
        //   float mouse_dy = 14;
        //   float aim_yaw = 15;
        //   float aim_pitch = 16;
        //   uint64 team_id = 17;
        //   CombatData combat_data = 18;
        //   string event_type = 19;
        //   map<string,string> event_data = 20;
        //   bool eyesviewmode = 21;
        //   bool thirdpersonviewmode = 22;
        //   float view_angles_x = 23;
        //   float view_angles_y = 24;
        //   float view_angles_z = 25;
        //   int32 model_state = 26;
        //   int32 player_flags = 27;
        //   float mouse_dz = 28
        //   int32 inputButtons = 29;
        //   float eyes_position_x = 30;
        //   float eyes_position_y = 31;
        //   float eyes_position_z = 32;
        //   
        //   // NEW: Additional PlayerTick data from ServerMgr.OnPlayerTick
        //   uint64 active_item_id = 33;
        //   uint64 parent_id = 34;
        //   uint32 delta_ms = 35;
        //   float water_level = 36;
        //   float look_dir_x = 37;
        //   float look_dir_y = 38;
        //   float look_dir_z = 39;
        //   int32 pose_type = 40;
        //   float inherited_velocity_x = 41;
        //   float inherited_velocity_y = 42;
        //   float inherited_velocity_z = 43;
        //   float ducking = 44;
        //   bool is_dead = 45;
        //   bool on_ladder = 46;
        //   float average_latency = 47;
        //   int64 packet_loss = 48;
        //   float water_factor = 49;
        //   bool is_swimming = 50;
        //   bool is_diving = 51;
        // }
        
        if (s.Tick != 0)
        {
            w.WriteTag(1, ProtobufWireType.Varint);
            w.WriteInt64(s.Tick);
        }

        if (s.TickTimestampUnixMs != 0)
        {
            w.WriteTag(2, ProtobufWireType.Varint);
            w.WriteInt64(s.TickTimestampUnixMs);
        }

        if (Math.Abs(s.TickIntervalMs) > float.Epsilon)
        {
            w.WriteTag(3, ProtobufWireType.Fixed32);
            w.WriteFixed32(s.TickIntervalMs);
        }

        WriteFloatIfNonZero(w, 4, s.PosX);
        WriteFloatIfNonZero(w, 5, s.PosY);
        WriteFloatIfNonZero(w, 6, s.PosZ);
        WriteFloatIfNonZero(w, 7, s.VelX);
        WriteFloatIfNonZero(w, 8, s.VelY);
        WriteFloatIfNonZero(w, 9, s.VelZ);

        if (s.IsGrounded)
        {
            w.WriteTag(10, ProtobufWireType.Varint);
            w.WriteBool(true);
        }

        if (s.SnapshotType != SnapshotTypeEnums.Unknown)
        {
            w.WriteTag(12, ProtobufWireType.Varint);
            w.WriteInt32((int)s.SnapshotType);
        }

        WriteFloatIfNonZero(w, 13, s.MouseDX);
        WriteFloatIfNonZero(w, 14, s.MouseDY);
        WriteFloatIfNonZero(w, 15, s.AimYaw);
        WriteFloatIfNonZero(w, 16, s.AimPitch);

        if (s.TeamId != 0)
        {
            w.WriteTag(17, ProtobufWireType.Varint);
            w.WriteUInt64(s.TeamId);
        }

        if (s.CombatData != null && (s.CombatData.IsAiming || s.CombatData.IsAttacking || s.CombatData.IsMounted ||
            s.CombatData.LastTargetId != null || s.CombatData.LastAttackTimeUnixMs != 0 || 
            !string.IsNullOrEmpty(s.CombatData.Weapon)))
        {
            w.WriteEmbeddedMessage(18, s.CombatData, static (inner, cd) => WriteCombatData(inner, cd));
        }

        if (s is EventSnapshot es)
        {
            if (!string.IsNullOrEmpty(es.EventType))
            {
                w.WriteTag(19, ProtobufWireType.LengthDelimited);
                w.WriteString(es.EventType);
            }

            if (es.HasEntityKillData)
            {
                WriteMapEntry(w, "prefabID", es.EntityPrefabId);
                WriteMapEntry(w, "netID", es.EntityNetId);
                WriteMapEntry(w, "owner", es.EntityOwnerId);
            }
            else
            {
                foreach (var kvp in es.EventData)
                {
                    if (kvp.Key == null || kvp.Value == null) continue;
                    w.WriteEmbeddedMessage(20, kvp, static (entry, kv) =>
                    {
                        entry.WriteTag(1, ProtobufWireType.LengthDelimited);
                        entry.WriteString(kv.Key);
                        entry.WriteTag(2, ProtobufWireType.LengthDelimited);
                        entry.WriteString(kv.Value);
                    });
                }
            }
        }

        if (s.EyesViewMode)
        {
            w.WriteTag(21, ProtobufWireType.Varint);
            w.WriteBool(true);
        }

        if (s.ThirdPersonViewMode)
        {
            w.WriteTag(22, ProtobufWireType.Varint);
            w.WriteBool(true);
        }

        WriteFloatIfNonZero(w, 23, s.ViewAnglesX);
        WriteFloatIfNonZero(w, 24, s.ViewAnglesY);
        WriteFloatIfNonZero(w, 25, s.ViewAnglesZ);
        
        if (s.ModelState != 0)
        {
            w.WriteTag(26, ProtobufWireType.Varint);
            w.WriteInt32(s.ModelState);
        }
        
        if (s.PlayerFlags != 0)
        {
            w.WriteTag(27, ProtobufWireType.Varint);
            w.WriteInt32(s.PlayerFlags);
        }
        
        WriteFloatIfNonZero(w, 28, s.MouseDZ);
        
        if (s.InputButtons != 0)
        {
            w.WriteTag(29, ProtobufWireType.Varint);
            w.WriteInt32(s.InputButtons);
        }
        
        WriteFloatIfNonZero(w, 30, s.EyesPositionX);
        WriteFloatIfNonZero(w, 31, s.EyesPositionY);
        WriteFloatIfNonZero(w, 32, s.EyesPositionZ);
        
        // NEW: Additional PlayerTick data from ServerMgr.OnPlayerTick
        if (s.ActiveItemId != 0)
        {
            w.WriteTag(33, ProtobufWireType.Varint);
            w.WriteUInt64(s.ActiveItemId);
        }
        
        if (s.ParentId != 0)
        {
            w.WriteTag(34, ProtobufWireType.Varint);
            w.WriteUInt64(s.ParentId);
        }
        
        if (s.DeltaMs != 0)
        {
            w.WriteTag(35, ProtobufWireType.Varint);
            w.WriteUInt32(s.DeltaMs);
        }
        
        WriteFloatIfNonZero(w, 36, s.WaterLevel);
        WriteFloatIfNonZero(w, 37, s.LookDirX);
        WriteFloatIfNonZero(w, 38, s.LookDirY);
        WriteFloatIfNonZero(w, 39, s.LookDirZ);
        
        if (s.PoseType != 0)
        {
            w.WriteTag(40, ProtobufWireType.Varint);
            w.WriteInt32(s.PoseType);
        }
        
        WriteFloatIfNonZero(w, 41, s.InheritedVelocityX);
        WriteFloatIfNonZero(w, 42, s.InheritedVelocityY);
        WriteFloatIfNonZero(w, 43, s.InheritedVelocityZ);
        WriteFloatIfNonZero(w, 44, s.Ducking);

        if (s.IsDead)
        {
            w.WriteTag(45, ProtobufWireType.Varint);
            w.WriteBool(true);
        }

        if (s.OnLadder)
        {
            w.WriteTag(46, ProtobufWireType.Varint);
            w.WriteBool(true);
        }

        WriteFloatIfNonZero(w, 47, s.AverageLatency);

        if (s.PacketLoss != 0)
        {
            w.WriteTag(48, ProtobufWireType.Varint);
            w.WriteInt64(s.PacketLoss);
        }
        
        WriteFloatIfNonZero(w, 49, s.WaterFactor);
        
        if (s.IsSwimming)
        {
            w.WriteTag(50, ProtobufWireType.Varint);
            w.WriteBool(true);
        }
        
        if (s.IsDiving)
        {
            w.WriteTag(51, ProtobufWireType.Varint);
            w.WriteBool(true);
        }
    }

    private static void WriteCombatData(ProtobufWireWriter w, CombatData c)
    {
        // message CombatData {
        //   bool is_aiming = 1;
        //   bool is_attacking = 2;
        //   bool is_mounted = 3;
        //   int64 last_target_net_id = 4;
        //   float last_attack_time = 5;
        //   uint32 flags = 6;
        //   string weapon = 7;
        // }

        if (c.IsAiming)
        {
            w.WriteTag(1, ProtobufWireType.Varint);
            w.WriteBool(true);
        }

        if (c.IsAttacking)
        {
            w.WriteTag(2, ProtobufWireType.Varint);
            w.WriteBool(true);
        }

        if (c.IsMounted)
        {
            w.WriteTag(3, ProtobufWireType.Varint);
            w.WriteBool(true);
        }

        var lastTargetNetId = Helpers.TryExtractNetId(c.LastTargetId);
        if (lastTargetNetId != 0)
        {
            w.WriteTag(4, ProtobufWireType.Varint);
            w.WriteInt64(lastTargetNetId);
        }

        if (Math.Abs(c.LastAttackTimeUnixMs) > float.Epsilon)
        {
            w.WriteTag(5, ProtobufWireType.Fixed32);
            w.WriteFixed32(c.LastAttackTimeUnixMs);
        }

        if (!string.IsNullOrEmpty(c.Weapon))
        {
            w.WriteTag(7, ProtobufWireType.LengthDelimited);
            w.WriteString(c.Weapon);
        }
    }

    private static void WriteOptionalBytes(ProtobufWireWriter w, int fieldNumber, byte[]? data, int length)
    {
        if (data != null && length > 0)
        {
            w.WriteTag(fieldNumber, ProtobufWireType.LengthDelimited);
            w.WriteBytes(data, length);
        }
    }

    private static void WriteOptionalInt64(ProtobufWireWriter w, int fieldNumber, long value)
    {
        if (value != 0)
        {
            w.WriteTag(fieldNumber, ProtobufWireType.Varint);
            w.WriteInt64(value);
        }
    }

    private static void WriteFloatIfNonZero(ProtobufWireWriter w, int fieldNumber, float value)
    {
        if (Math.Abs(value) <= float.Epsilon)
            return;

        w.WriteTag(fieldNumber, ProtobufWireType.Fixed32);
        w.WriteFixed32(value);
    }

    private static void WriteMapEntry(ProtobufWireWriter w, string key, ulong value)
    {
        w.WriteEmbeddedMessage(20, (key, value), static (entry, state) =>
        {
            entry.WriteTag(1, ProtobufWireType.LengthDelimited);
            entry.WriteString(state.key);
            entry.WriteTag(2, ProtobufWireType.LengthDelimited);
            entry.WriteNumericString(state.value);
        });
    }

    private static void WriteMapEntry(ProtobufWireWriter w, string key, long value)
    {
        WriteMapEntry(w, key, (ulong)value);
    }

    private static void WriteMapEntry(ProtobufWireWriter w, string key, uint value)
    {
        WriteMapEntry(w, key, (ulong)value);
    }
}