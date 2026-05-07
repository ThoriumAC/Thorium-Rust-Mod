using System.Collections.Generic;
using Facepunch;

namespace ThoriumRustMod.Models;

public class ThoriumBatch : Pool.IPooled
{
    public long StartTick { get; set; }
    public long EndTick { get; set; }
    public List<AntiCheatSnapshot> Snapshots { get; set; } = new();
    public List<PlayerServerStats> PlayerServerStats { get; set; } = new();

    public void EnterPool()
    {
        StartTick = 0;
        EndTick = 0;
        Snapshots.Clear();
        PlayerServerStats.Clear();
    }

    public void LeavePool() { }
}
