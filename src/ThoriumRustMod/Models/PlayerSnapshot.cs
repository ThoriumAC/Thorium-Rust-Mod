using System;
using Facepunch;
using Network;
using UnityEngine;

namespace ThoriumRustMod.Models;

/// <summary>
/// Represents a single moment in time capturing player state and behavior
/// Used for anti-cheat analysis to detect suspicious movement patterns
/// </summary>
public class PlayerSnapshot : Pool.IPooled
{
    /// <summary>
    /// Creates a snapshot pre-filled with common temporal, positional, and aim fields.
    /// Callers should set additional fields (Vel, IsGrounded, Mouse, CombatData, etc.) as needed.
    /// Uses object pooling to reduce GC pressure.
    /// </summary>
    public static PlayerSnapshot Create(Vector3 position, BasePlayer player, SnapshotTypeEnums type,
        CombatData? combatData = null, Vector3? velocity = null, bool isGrounded = false, InputMessage? inputMessage = null)
    {
        var snapshot = AntiCheatSnapshotProcessor.GetPooledSnapshot();

        // Cache timestamp calculation (expensive operation)
        var now = Time.time;
        snapshot.Tick = (long)(now * 1000);
        snapshot.TickTimestampUnixMs = GetUnixTimestampMs();
        snapshot.TickIntervalMs = Time.deltaTime * 1000f;

        snapshot.PosX = position.x;
        snapshot.PosY = position.y;
        snapshot.PosZ = position.z;

        var eyes = player.eyes;
        if (eyes != null)
        {
            var eulerAngles = eyes.rotation.eulerAngles;
            snapshot.AimYaw = eulerAngles.y;
            snapshot.AimPitch = eulerAngles.x;
        }
        else
        {
            snapshot.AimYaw = 0f;
            snapshot.AimPitch = 0f;
        }

        snapshot.TeamId = player.currentTeam;
        snapshot.SnapshotType = type;
        snapshot.CombatData = combatData;
        snapshot.IsGrounded = isGrounded;
        snapshot.ModelState = player.modelState.flags;
        snapshot.PlayerFlags = (int)player.playerFlags;

        if (inputMessage != null)
        {
            snapshot.MouseDX = inputMessage.mouseDelta.x;
            snapshot.MouseDY = inputMessage.mouseDelta.y;
            snapshot.MouseDZ = inputMessage.mouseDelta.z;
            snapshot.InputButtons = inputMessage.buttons;
        }
        else
        {
            snapshot.MouseDX = 0f;
            snapshot.MouseDY = 0f;
            snapshot.MouseDZ = 0f;
            snapshot.InputButtons = 0;
        }

        if (velocity.HasValue)
        {
            snapshot.VelX = velocity.Value.x;
            snapshot.VelY = velocity.Value.y;
            snapshot.VelZ = velocity.Value.z;
        }
        else
        {
            snapshot.VelX = 0f;
            snapshot.VelY = 0f;
            snapshot.VelZ = 0f;
        }
        
        if (player.lifestate == BaseCombatEntity.LifeState.Dead)
        {
            snapshot.IsDead = true;
        }
        else
        {
            snapshot.IsDead = false;
        }

        snapshot.OnLadder = player.OnLadder();

        snapshot.AverageLatency = Net.sv.GetAveragePing(player.net.connection);
        snapshot.PacketLoss = (long)Net.sv.GetStat(player.net.connection, BaseNetwork.StatTypeLong.PacketLossLastSecond);
        
        return snapshot;
    }

    // Cache for Unix timestamp to avoid DateTimeOffset allocations
    private static long _cachedUnixMs;
    private static float _lastUnixMsUpdate;

    public static long GetUnixTimestampMsCached()
    {
        var now = Time.realtimeSinceStartup;
        if (now - _lastUnixMsUpdate > 0.1f)
        {
            _cachedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _lastUnixMsUpdate = now;
        }
        return _cachedUnixMs;
    }

    private static long GetUnixTimestampMs() => GetUnixTimestampMsCached();

    /// <summary>
    /// Server tick when this snapshot was captured (in milliseconds)
    /// </summary>
    public long Tick { get; set; }

    /// <summary>
    /// Unix timestamp in milliseconds when this tick occurred
    /// </summary>
    public long TickTimestampUnixMs { get; set; }

    /// <summary>
    /// Time interval between ticks in milliseconds
    /// </summary>
    public float TickIntervalMs { get; set; }

    /// <summary>
    /// Player's X position in world coordinates
    /// </summary>
    public float PosX { get; set; }

    /// <summary>
    /// Player's Y position in world coordinates (height)
    /// </summary>
    public float PosY { get; set; }

    /// <summary>
    /// Player's Z position in world coordinates
    /// </summary>
    public float PosZ { get; set; }

    /// <summary>
    /// Player's velocity on the X axis
    /// </summary>
    public float VelX { get; set; }

    /// <summary>
    /// Player's velocity on the Y axis (vertical movement)
    /// </summary>
    public float VelY { get; set; }

    /// <summary>
    /// Player's velocity on the Z axis
    /// </summary>
    public float VelZ { get; set; }

    /// <summary>
    /// Whether the player is currently touching the ground
    /// </summary>
    public bool IsGrounded { get; set; }


    public bool EyesViewMode { get; set; }

    public bool ThirdPersonViewMode { get; set; }

    public float ViewAnglesX { get; set; }
    public float ViewAnglesY { get; set; }
    public float ViewAnglesZ { get; set; }

    public int ModelState { get; set; }
    public int PlayerFlags { get; set; }

    public float EyesPositionX { get; set; }
    public float EyesPositionY { get; set; }
    public float EyesPositionZ { get; set; }
    
    public bool IsDead { get; set; }

    public bool OnLadder { get; set; }

    public float AverageLatency { get; set; }

    public long PacketLoss { get; set; }

    /// <summary>
    /// Logical type of this snapshot (enum). Use this to indicate what kind of event or capture produced the snapshot.
    /// </summary>
    public SnapshotTypeEnums SnapshotType { get; set; } = SnapshotTypeEnums.Unknown;

    /// <summary>
    /// Backwards compatible string representation for any external systems that expect the textual snapshot type.
    /// This returns the string equivalent of the enum (lowercase with underscores to match existing values).
    /// </summary>
    public string SnapshotTypeString
    {
        get
        {
            return SnapshotType switch
            {
                SnapshotTypeEnums.PlayerTick => "player_tick",
                SnapshotTypeEnums.Join => "join",
                SnapshotTypeEnums.Leave => "leave",
                SnapshotTypeEnums.Hurt => "hurt",
                SnapshotTypeEnums.HurtEnv => "hurt_env",
                SnapshotTypeEnums.Die => "die",
                SnapshotTypeEnums.MoveItem => "move_item",
                SnapshotTypeEnums.EntityKill => "entity_kill",
                SnapshotTypeEnums.StashExposed => "stash_exposed",
                SnapshotTypeEnums.StashBuried => "stash_buried",
                SnapshotTypeEnums.StashOpened => "stash_opened",
                SnapshotTypeEnums.StashBuiltOver => "stash_built_over",
                _ => "unknown",
            };
        }
    }

    /// <summary>
    /// Mouse movement delta on the X axis since last tick
    /// </summary>
    public float MouseDX { get; set; }

    /// <summary>
    /// Mouse movement delta on the Y axis since last tick
    /// </summary>
    public float MouseDY { get; set; }

    public float MouseDZ { get; set; }

    public int InputButtons { get; set; }

    /// <summary>
    /// Player's aim yaw angle (horizontal rotation)
    /// </summary>
    public float AimYaw { get; set; }

    /// <summary>
    /// Player's aim pitch angle (vertical rotation)
    /// </summary>
    public float AimPitch { get; set; }

    public ulong TeamId { get; set; }

    public CombatData? CombatData { get; set; }

    // ===== Additional PlayerTick data from ServerMgr.OnPlayerTick =====

    /// <summary>
    /// ID of the currently active/held item
    /// </summary>
    public ulong ActiveItemId { get; set; }

    /// <summary>
    /// ID of parent entity (if mounted on vehicle, horse, etc.)
    /// </summary>
    public ulong ParentId { get; set; }

    /// <summary>
    /// Time delta since last tick in milliseconds (from client)
    /// </summary>
    public uint DeltaMs { get; set; }

    /// <summary>
    /// Water level at player position (0-1 range, where 1 is fully submerged)
    /// </summary>
    public float WaterLevel { get; set; }

    /// <summary>
    /// Direction the model is looking (X component)
    /// </summary>
    public float LookDirX { get; set; }

    /// <summary>
    /// Direction the model is looking (Y component)
    /// </summary>
    public float LookDirY { get; set; }

    /// <summary>
    /// Direction the model is looking (Z component)
    /// </summary>
    public float LookDirZ { get; set; }

    /// <summary>
    /// Player pose type (standing, crouching, etc.)
    /// </summary>
    public int PoseType { get; set; }

    /// <summary>
    /// Inherited velocity from parent object (X component) - e.g., from vehicle
    /// </summary>
    public float InheritedVelocityX { get; set; }

    /// <summary>
    /// Inherited velocity from parent object (Y component)
    /// </summary>
    public float InheritedVelocityY { get; set; }

    /// <summary>
    /// Inherited velocity from parent object (Z component)
    /// </summary>
    public float InheritedVelocityZ { get; set; }

    /// <summary>
    /// Ducking/crouching level (0-1 range)
    /// </summary>
    public float Ducking { get; set; }

    public void EnterPool()
    {
        CombatData?.Return();
        CombatData = null;

        Tick = 0;
        TickTimestampUnixMs = 0;
        TickIntervalMs = 0f;
        PosX = 0f; PosY = 0f; PosZ = 0f;
        VelX = 0f; VelY = 0f; VelZ = 0f;
        IsGrounded = false;
        EyesViewMode = false;
        ThirdPersonViewMode = false;
        ViewAnglesX = 0f; ViewAnglesY = 0f; ViewAnglesZ = 0f;
        ModelState = 0;
        PlayerFlags = 0;
        EyesPositionX = 0f; EyesPositionY = 0f; EyesPositionZ = 0f;
        IsDead = false;
        OnLadder = false;
        AverageLatency = 0f;
        PacketLoss = 0;
        SnapshotType = SnapshotTypeEnums.Unknown;
        MouseDX = 0f; MouseDY = 0f; MouseDZ = 0f;
        InputButtons = 0;
        AimYaw = 0f; AimPitch = 0f;
        TeamId = 0;
        ActiveItemId = 0;
        ParentId = 0;
        DeltaMs = 0;
        WaterLevel = 0f;
        LookDirX = 0f; LookDirY = 0f; LookDirZ = 0f;
        PoseType = 0;
        InheritedVelocityX = 0f; InheritedVelocityY = 0f; InheritedVelocityZ = 0f;
        Ducking = 0f;
    }

    public void LeavePool() { }
}