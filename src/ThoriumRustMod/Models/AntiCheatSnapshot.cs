using System.Collections.Generic;
using Facepunch;

namespace ThoriumRustMod.Models;

public class AntiCheatSnapshot : Pool.IPooled
{
    public long SteamId { get; set; }
    public List<PlayerSnapshot> Snapshots { get; set; } = new(64);

    public void EnterPool()
    {
        SteamId = 0;
        Snapshots.Clear();
    }

    public void LeavePool() { }
}