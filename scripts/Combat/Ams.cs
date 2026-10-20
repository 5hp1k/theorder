using Godot;

namespace MechSim;

/// <summary>
/// Anti-missile system (scenes/weapons/ams.tscn, put on a machine's torso, or fitted in the hangar): a
/// small fast gun that by itself fires at missiles coming at the machine — those locked on it, and any
/// other that would pass close — and brings down some of them. It needs no pilot and no fire group;
/// it runs out of rounds, and an electromagnetic pulse knocks it out with the rest of the electronics.
/// </summary>
public partial class Ams : Node3D
{
    /// <summary>How far off it engages a missile, metres.</summary>
    [Export] public float Range = 170f;
    /// <summary>Seconds between bursts.</summary>
    [Export] public float Interval = 0.3f;
    /// <summary>Chance a burst brings the missile down.</summary>
    [Export(PropertyHint.Range, "0,1")] public float HitChance = 0.45f;
    /// <summary>A missile not locked on the machine is only a threat if it would pass this close, metres.</summary>
    [Export] public float ThreatRadius = 9f;
    [Export] public int MaxAmmo = 80;
    [Export] public Color TracerColor = new(1f, 0.75f, 0.35f);

    public int Ammo { get; private set; }
    ITarget _owner;
    float _wait;
    AudioStreamPlayer3D _sound;

    /// <summary>The anti-missile system on a machine, if it has one.</summary>
    public static Ams Of(Node unit) => unit == null ? null : Geo.Collect<Ams>(unit) is { Count: > 0 } list ? list[0] : null;

    public override void _Ready()
    {
        Ammo = MaxAmmo;
        _sound = GetNodeOrNull<AudioStreamPlayer3D>("FireSound");
        for (Node n = GetParent(); n != null; n = n.GetParent())
            if (n is ITarget t) { _owner = t; break; }
    }

    /// <summary>Full magazine again (a repair, a new life).</summary>
    public void Rearm() => Ammo = MaxAmmo;

    public override void _PhysicsProcess(double delta)
    {
        _wait -= (float)delta;
        if (_wait > 0f || Ammo <= 0 || _owner == null || _owner.IsDestroyed || Emp.Jammed(_owner)) return;
        var missile = Threat();
        if (missile == null) return;
        _wait = Interval;
        if (!Cheats.Refills(_owner)) Ammo--;
        Vector3 from = GlobalPosition, at = missile.GlobalPosition;
        Node fx = GetTree().CurrentScene ?? GetParent();
        TimedFx.Tracer(fx, from, at, TracerColor, 0.05f);
        TimedFx.Flash(fx, from, TracerColor, 0.35f, 0.05f);
        _sound?.Play();
        if (GD.Randf() < HitChance) missile.ShootDown();
    }

    /// <summary>The nearest missile in reach that is coming for the machine.</summary>
    Missile Threat()
    {
        Vector3 me = _owner.AimCenter;
        Missile best = null;
        float bestDistance = Range;
        foreach (var m in Missile.Flying)
        {
            if (!IsInstanceValid(m) || m.IsQueuedForDeletion() || !m.Interceptable || m.Shooter == _owner) continue;
            Vector3 to = me - m.GlobalPosition;
            float d = to.Length();
            if (d >= bestDistance) continue;
            if (m.Target != _owner)
            {
                // Not homing on us: only if its course runs close by, ahead of it.
                float along = to.Dot(m.Heading);
                if (along <= 0f || (to - m.Heading * along).Length() > ThreatRadius) continue;
            }
            best = m;
            bestDistance = d;
        }
        return best;
    }
}
