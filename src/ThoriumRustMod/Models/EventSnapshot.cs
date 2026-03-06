using System.Collections.Generic;

namespace ThoriumRustMod.Models;

/// <summary>
/// A lightweight snapshot for events that don't provide full player state (entity kills, stash events, etc.)
/// Inherits from PlayerSnapshot so it can be queued in the AntiCheatSnapshotProcessor.
/// </summary>
public class EventSnapshot : PlayerSnapshot
{
    /// <summary>
    /// Short event type identifier, e.g. "entity_kill", "stash_exposed"
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Optional additional data about the event (key/value pairs)
    /// Kept as strings for simplicity and to avoid direct dependency on complex types.
    /// </summary>
    public Dictionary<string, string> EventData { get; set; } = new();
}