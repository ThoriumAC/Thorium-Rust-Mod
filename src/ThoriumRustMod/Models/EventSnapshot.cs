using System.Collections.Generic;

namespace ThoriumRustMod.Models;

/// <summary>
/// A lightweight snapshot for events that don't provide full player state (entity kills, stash events, etc.)
/// Inherits from PlayerSnapshot so it can be queued in the AntiCheatSnapshotProcessor.
/// </summary>
public class EventSnapshot : PlayerSnapshot
{
    public string EventType { get; set; } = string.Empty;

    public Dictionary<string, string> EventData { get; set; } = new();

    // Pre-stored numeric fields to avoid ToString() allocations during serialization.
    // The serializer checks these before falling back to EventData dictionary.
    public uint EntityPrefabId { get; set; }
    public long EntityNetId { get; set; }
    public ulong EntityOwnerId { get; set; }
    public bool HasEntityKillData { get; set; }

    public void SetEntityKillData(uint prefabId, long netId, ulong ownerId)
    {
        EntityPrefabId = prefabId;
        EntityNetId = netId;
        EntityOwnerId = ownerId;
        HasEntityKillData = true;
    }
}