using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// A nuclear explosion (scenes/fx/nuke_explosion.tscn). The looks are all in the scene: the cloud is one
/// particle system whose shader (materials/fx/nuke_cloud.tres) shapes the mushroom; this script plays
/// the parts that are single nodes — the flash, the fireball, the shock front — and does what the bomb
/// does to the battlefield: a blast wave that travels outwards and hurts whatever it reaches, and an
/// electromagnetic pulse (see <see cref="Emp"/>).
/// Children: Cloud (GPUParticles3D), Fireball and Shock (MeshInstance3D spheres of radius 1), Flash and
/// Glow (OmniLight3D), Blind/White (a ColorRect over the whole screen), Boom (AudioStreamPlayer).
/// </summary>
public partial class NukeBlast : Node3D
{
    [ExportGroup("Blast")]
    /// <summary>Damage at ground zero, to every section of a mech and to every other thing. Set by the bomb.</summary>
    [Export] public float Damage = 400f;
    /// <summary>Within this everything takes the full damage.</summary>
    [Export] public float InnerRadius = 70f;
    /// <summary>The blast is spent here.</summary>
    [Export] public float Radius = 320f;
    /// <summary>Speed of the blast wave, m/s: things are hurt (and the bang is heard) when it gets to them.</summary>
    [Export] public float WaveSpeed = 170f;
    /// <summary>How far out the heat sets fire to what the blast breaks, as a fraction of the radius.</summary>
    [Export(PropertyHint.Range, "0,1")] public float BurnReach = 0.8f;
    /// <summary>Share of the things it breaks within that reach that are left burning.</summary>
    [Export(PropertyHint.Range, "0,1")] public float BurnShare = 0.5f;
    /// <summary>Fires it lights on the bare ground within that reach (see <see cref="Burning.Brush"/>, which caps how many burn at once).</summary>
    [Export] public int GroundFires = 40;
    /// <summary>The hole it digs: radius and depth.</summary>
    [Export] public Vector2 Crater = new(34f, 5f);
    /// <summary>The scorched ground it leaves (a Crater scene), and its radius.</summary>
    [Export] public PackedScene ScorchScene;
    [Export] public float ScorchRadius = 60f;

    [ExportGroup("Pulse")]
    [Export] public float EmpRadius = 900f;
    /// <summary>Seconds the electronics of a machine at ground zero stay dead (a third of that at the edge).</summary>
    [Export] public float EmpTime = 30f;

    [ExportGroup("Looks")]
    [Export] public float Lifetime = 64f;
    [Export] public float FireballRadius = 62f;
    /// <summary>Seconds the ball of fire takes to burn out into the cloud.</summary>
    [Export] public float FireballTime = 5f;
    /// <summary>Seconds the white flash on the screen takes to clear.</summary>
    [Export] public float BlindTime = 1.1f;
    /// <summary>Seconds the light of the flash, and the glow of the cloud, take to die.</summary>
    [Export] public float FlashTime = 0.9f;
    [Export] public float GlowTime = 9f;
    /// <summary>Seconds the ground shakes when the wave arrives.</summary>
    [Export] public float ShakeTime = 3.5f;

    /// <summary>Whose bomb it was, for the score.</summary>
    public ITarget Shooter;

    MeshInstance3D _fireball, _shock;
    OmniLight3D _flash, _glow;
    ColorRect _white;
    AudioStreamPlayer _boom;
    float _age, _wave, _flashEnergy, _glowEnergy, _blind, _shake;
    float _riseHeight = 260f, _riseTime = 9f, _capRadius = 110f;
    bool _heard;

    // What the wave has yet to reach, nearest first.
    readonly List<(float dist, Node3D thing)> _ahead = new();
    int _next;

    public override void _Ready()
    {
        _fireball = GetNodeOrNull<MeshInstance3D>("Fireball");
        _shock = GetNodeOrNull<MeshInstance3D>("Shock");
        _flash = GetNodeOrNull<OmniLight3D>("Flash");
        _glow = GetNodeOrNull<OmniLight3D>("Glow");
        _white = GetNodeOrNull<ColorRect>("Blind/White");
        _boom = GetNodeOrNull<AudioStreamPlayer>("Boom");
        if (_flash != null) _flashEnergy = _flash.LightEnergy;
        if (_glow != null) _glowEnergy = _glow.LightEnergy;
        // The fireball and the glow ride up with the cap of the cloud: the shader says how it rises.
        if (GetNodeOrNull<GpuParticles3D>("Cloud") is { ProcessMaterial: ShaderMaterial cloud } puffs)
        {
            cloud.SetShaderParameter("puffs", puffs.Amount);
            _riseHeight = Param(cloud, "rise_height", _riseHeight);
            _riseTime = Param(cloud, "rise_time", _riseTime);
            _capRadius = Param(cloud, "cap_radius", _capRadius);
        }

        Vector3 center = GlobalPosition;
        // Looking at it, and near: blinded. Far off, or with one's back to it: a white wash.
        if (GetViewport().GetCamera3D() is { } camera)
        {
            Vector3 to = center - camera.GlobalPosition;
            _blind = Mathf.Clamp(1.35f - to.Length() / 1400f, 0.2f, 1f);
            if (to.Normalized().Dot(-camera.GlobalTransform.Basis.Z) < 0.2f) _blind *= 0.75f;
        }

        // The ground it scorches catches as the wave passes: scattered over the burnt circle, thicker towards the middle.
        for (int i = 0; i < GroundFires; i++)
        {
            float a = GD.Randf() * Mathf.Tau, d = Crater.X * 0.8f + GD.Randf() * (Radius * BurnReach - Crater.X * 0.8f);
            _kindling.Add((d, center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * d));
        }
        _kindling.Sort((x, y) => x.dist.CompareTo(y.dist));

        Emp.Pulse(center, EmpRadius, EmpTime);
        if (Net.Authority)
        {
            MapTerrain.Instance?.AddCrater(center, Crater.X, Crater.Y);
            Gather(center);
            // Ground zero is gone at once; the rest when the wave gets there.
            _swept = 0f;
        }
        if (ScorchScene != null && MapTerrain.Instance is { } terrain && terrain.WaterDepthAt(center.X, center.Z) <= 0.02f)
        {
            var scorch = ScorchScene.Instantiate<Node3D>();
            scorch.Transform = new Transform3D(new Basis(Vector3.Up, GD.Randf() * Mathf.Tau).Scaled(Vector3.One * ScorchRadius), center);
            GetParent().AddChild(scorch);
        }
        Play(0f);
    }

    static float Param(ShaderMaterial material, string name, float fallback)
    {
        Variant v = material.GetShaderParameter(name);
        return v.VariantType is Variant.Type.Float or Variant.Type.Int ? v.AsSingle() : fallback;
    }

    /// <summary>Height of the middle of the cap above ground zero, as the cloud shader has it.</summary>
    float CapHeight(float t) => _capRadius * 0.25f + _riseHeight * (1f - Mathf.Exp(-t / _riseTime));

    public override void _Process(double delta)
    {
        _age += (float)delta;
        if (_age >= Lifetime)
        {
            QueueFree();
            return;
        }
        Play((float)delta);
    }

    void Play(float dt)
    {
        float t = _age;
        float cap = CapHeight(t);
        for (; _lit < _kindling.Count && _kindling[_lit].dist <= WaveSpeed * t; _lit++)
        {
            Vector3 at = _kindling[_lit].at;
            if (MapTerrain.Instance is { } ground) at.Y = ground.HeightAt(at.X, at.Z) + 0.2f;
            Burning.Brush(GetParent(), at, (float)GD.RandRange(1.0, 2.2));
        }
        if (_fireball != null)
        {
            _fireball.Visible = t < FireballTime;
            float r = FireballRadius * (1f - Mathf.Exp(-t / 0.22f)) * (1f + 0.04f * t);
            _fireball.Transform = new Transform3D(Basis.Identity.Scaled(Vector3.One * Mathf.Max(r, 0.01f)), Vector3.Up * cap);
            _fireball.SetInstanceShaderParameter("heat", Mathf.Exp(-t / (FireballTime * 0.3f)));
            _fireball.SetInstanceShaderParameter("alpha", 1f - Mathf.SmoothStep(FireballTime * 0.35f, FireballTime, t));
        }
        if (_shock != null)
        {
            // The condensation shell on the shock front: out with the wave, gone in a couple of seconds.
            float life = Radius * 1.6f / WaveSpeed;
            _shock.Visible = t < life;
            _shock.Scale = Vector3.One * Mathf.Max(WaveSpeed * t, 1f);
            _shock.SetInstanceShaderParameter("alpha", Mathf.Clamp(1f - t / life, 0f, 1f));
        }
        if (_flash != null)
        {
            _flash.LightEnergy = _flashEnergy * Mathf.Exp(-t / FlashTime);
            _flash.Visible = t < FlashTime * 6f;
        }
        if (_glow != null)
        {
            _glow.Position = Vector3.Up * cap;
            _glow.LightEnergy = _glowEnergy * Mathf.Exp(-t / GlowTime);
            _glow.Visible = t < GlowTime * 4f;
        }
        if (_white != null)
        {
            float a = _blind * (t < 0.12f ? 1f : Mathf.Exp(-(t - 0.12f) / BlindTime));
            _white.Visible = a > 0.004f;
            _white.Color = new Color(_white.Color, a);
        }

        // The wave reaches the player: only now is it heard, and the ground heaves.
        Vector3 ear = Game.PlayerUnit is { } unit ? unit.GlobalPosition : GetViewport().GetCamera3D()?.GlobalPosition ?? GlobalPosition;
        float dist = ear.DistanceTo(GlobalPosition);
        if (!_heard && WaveSpeed * t >= dist)
        {
            _heard = true;
            _shake = ShakeTime;
            if (_boom != null)
            {
                _boom.VolumeDb += Mathf.Lerp(0f, -14f, Mathf.Clamp(dist / (EmpRadius * 1.5f), 0f, 1f));
                _boom.Play();
            }
        }
        if (_shake > 0f && Game.PlayerUnit is IDrivable driven)
        {
            float near = Mathf.Clamp(1.2f - dist / (Radius * 2.5f), 0.15f, 1f);
            driven.Shake = Mathf.Max(driven.Shake, 0.6f * near * _shake / ShakeTime);
        }
        _shake -= dt;
    }

    // ── The blast wave ──────────────────────────────────────────

    /// <summary>Most props the wave may break in one tick; the rest wait for the next (a forest falls over a second or two, not in one frame).</summary>
    const int PropsPerTick = 10;
    readonly List<ChunkedBuilding> _buildings = new();
    readonly List<(float dist, Vector3 at)> _kindling = new();
    int _lit;
    float _swept;

    /// <summary>Everything the wave will get to: machines and soldiers, props, buildings.</summary>
    void Gather(Vector3 center)
    {
        foreach (var unit in Units.All)
            if (unit is Node3D node && !unit.IsDestroyed) Add(center, node, unit.AimCenter);
        foreach (var prop in Destructible.Everyone) Add(center, prop, prop.GlobalPosition);
        _ahead.Sort((a, b) => a.dist.CompareTo(b.dist));
        foreach (var building in ChunkedBuilding.All)
            if (building.GlobalPosition.DistanceTo(center) < Radius + 150f) _buildings.Add(building);
    }

    void Add(Vector3 center, Node3D thing, Vector3 at)
    {
        float dist = at.DistanceTo(center);
        if (dist < Radius) _ahead.Add((dist, thing));
    }

    /// <summary>Whether something broken this far out catches fire: only now and then, and only while there is room for more fires.</summary>
    bool Kindles(float dist) => dist < Radius * BurnReach && Burning.Count < Burning.MaxFires && GD.Randf() < 0.12f;

    /// <summary>Share of the damage that is left this far out.</summary>
    float Falloff(float dist)
    {
        if (dist <= InnerRadius) return 1f;
        float k = Mathf.Clamp(1f - (dist - InnerRadius) / Mathf.Max(Radius - InnerRadius, 1f), 0f, 1f);
        return k * k;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Net.Authority || _swept >= Radius && _next >= _ahead.Count) return;
        _wave += WaveSpeed * (float)delta;
        Vector3 center = GlobalPosition;
        Score.Attacker = Shooter;

        // Buildings go block by block as the front passes through them.
        if (_swept < Radius)
        {
            float to = Mathf.Min(_wave, Radius);
            // Close in, what the blast breaks the heat sets alight: here and there, not every block of a town.
            Burning.Heat = Kindles(to);
            foreach (var building in _buildings)
                // (Nothing is left standing inside the radius: even at its edge the wave still breaks a wall.)
                if (IsInstanceValid(building)) building.BlastShell(center, _swept, to, d => Damage * Mathf.Max(Falloff(d), 0.06f));
            // The woods are flattened as the front goes through them, out to where the blast is still strong.
            ForestField.Flatten(center, Mathf.Min(to, Radius * 0.7f));
            _swept = to;
        }

        int props = 0;
        for (; _next < _ahead.Count && _ahead[_next].dist <= _wave; _next++)
        {
            var (dist, thing) = _ahead[_next];
            if (!IsInstanceValid(thing) || thing.IsQueuedForDeletion()) continue;
            Burning.Heat = Kindles(dist);
            switch (thing)
            {
                case Destructible prop:
                    if (props >= PropsPerTick) goto done;
                    // Only some of a forest that goes down at once gets its puff of dust.
                    Destructible.Quiet = props++ % 4 != 0;
                    // Whatever stands inside the radius goes, however tough: flattened away from ground zero.
                    Vector3 away = prop.GlobalPosition - center;
                    away.Y = 0f;
                    Vector3 place = prop.GlobalPosition;
                    prop.DestroyWhole(away);
                    Destructible.Quiet = false;
                    if (dist < Radius * BurnReach && GD.Randf() < BurnShare) Burning.Brush(GetParent(), place + Vector3.Up * 0.3f, (float)GD.RandRange(0.9, 1.8));
                    break;
                case Mech mech:
                    foreach (var box in Geo.Collect<HitBox>(mech))
                        box.TakeDamage(Damage * Falloff(box.Center.DistanceTo(center)), center);
                    break;
                case IDamageable victim:
                    victim.TakeDamage(Damage * Falloff(dist), center);
                    break;
            }
        }
        done:
        Burning.Heat = false;
        Score.Attacker = null;
    }
}
