using Facepunch;

namespace ThoriumRustMod.Models;

public class CombatData : Pool.IPooled
{
    public bool IsAiming;
    public bool IsAttacking;
    public bool IsMounted;
    public BaseEntity? LastTargetId;
    public float LastAttackTimeUnixMs;
    public string? Weapon;

    public static CombatData Get() => Pool.Get<CombatData>();

    public void Return()
    {
        var self = this;
        Pool.Free(ref self);
    }

    public void EnterPool()
    {
        IsAiming = false;
        IsAttacking = false;
        IsMounted = false;
        LastTargetId = null;
        LastAttackTimeUnixMs = 0f;
        Weapon = null;
    }

    public void LeavePool() { }

    public static CombatData FromPlayer(BasePlayer player)
    {
        var activeItem = player.GetActiveItem();
        var data = Get();
        data.IsAiming = player.IsAiming;
        data.IsAttacking = player.IsAttacking();
        data.IsMounted = player.isMounted;
        data.LastTargetId = player.lastDealtDamageTo;
        data.LastAttackTimeUnixMs = player.lastDealtDamageTime;
        data.Weapon = activeItem?.info.shortname;
        return data;
    }
}
