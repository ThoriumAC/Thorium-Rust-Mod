namespace ThoriumRustMod.Models;

public enum SnapshotTypeEnums
{
    Unknown = 0,
    PlayerTick,
    Join,
    Leave,
    Hurt,
    HurtEnv,
    Die,
    MoveItem,
    EntityKill,
    StashExposed,
    StashBuried,
    StashOpened,
    StashBuiltOver
}