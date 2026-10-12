using Godot;

namespace MechSim;

/// <summary>
/// Computer pilot for mechs and vehicles. Patrols around its spawn; when it detects an enemy (sensor
/// range + line of sight, or by being shot) it closes to its preferred range and circles, turning the
/// turret to keep its guns on the target, backs off when overheating, and goes to the last known
/// position when it loses contact. Bombers fly attack runs instead. The numbers are on the node in
/// ai_pilot.tscn.
/// </summary>
public partial class AiPilot : MechPilot
{
    [Export] public float SensorRange = 300f;
    /// <summary>Distance it tries to fight at.</summary>
    [Export] public float PreferredRange = 140f;
    [Export] public float PatrolRadius = 70f;
    [Export] public float PatrolThrottle = 0.4f;
    /// <summary>Aim wander in metres per 100 m of distance; grows while moving.</summary>
    [Export] public float AimSpread = 2.6f;
    /// <summary>Above this heat it stops firing group 1 and opens the distance.</summary>
    [Export] public float CautiousHeat = 0.6f;
    /// <summary>Seconds it keeps fighting from memory after losing sight of the target.</summary>
    [Export] public float MemoryTime = 8f;

    enum State { Patrol, Engage, Search }

    State _state;
    ITarget _target;
    Vector3 _home, _waypoint, _lastKnown;
    float _wait, _sightTimer, _pickTimer, _sinceSeen = 999f, _searchTime;
    bool _seesTarget;
    float _strafeSign = 1f, _strafeTimer, _stuck;
    Vector3 _aimOffset, _aimOffsetGoal;
    float _aimTimer, _jumpLeft, _egress, _lock;

    /// <summary>Seconds the guns must stay on the target before its missiles will home.</summary>
    [Export] public float LockTime = 1.8f;
    bool _brawler;
    Vehicle _plane;

    Vector3 Here => Unit.GlobalPosition;

    public override void Attach(IDrivable unit)
    {
        base.Attach(unit);
        _home = unit.GlobalPosition;
        foreach (var w in unit.Weapons.Weapons) _brawler |= w.Kind == WeaponKind.Melee;
        if (unit is Vehicle { Kind: Vehicle.MoveKind.Plane } plane) _plane = plane;
        _strafeSign = GD.Randf() < 0.5f ? -1f : 1f;
        _wait = GD.Randf() * 3f;
        PickWaypoint();
    }

    // ── Brain ───────────────────────────────────────────────────

    public override void Think(float dt)
    {
        if (Unit.Offline) return;
        PickTarget(dt);
        bool alive = _target != null && !_target.IsDestroyed;
        Perceive(dt, alive);
        // Missile lock, earned the same way the player earns it: by holding the sight on the target.
        bool onTarget = alive && Units.FromCollider(Unit.AimCollider) == _target;
        _lock = Mathf.Clamp(_lock + (onTarget ? dt / LockTime : -dt * 1.5f), 0f, 1f);
        Unit.LockTarget = _lock >= 1f ? _target : null;

        if (_plane != null)
        {
            FlyBomber(dt, alive && _sinceSeen < MemoryTime);
            return;
        }

        switch (_state)
        {
            case State.Patrol:
                if (alive && _sinceSeen < 0.5f) _state = State.Engage;
                else Patrol(dt);
                break;

            case State.Engage:
                if (!alive) BackToPatrol();
                else if (_sinceSeen > MemoryTime)
                {
                    _state = State.Search;
                    _searchTime = 0f;
                }
                else Engage(dt);
                break;

            case State.Search:
                _searchTime += dt;
                if (alive && _seesTarget) _state = State.Engage;
                else if (!alive || _searchTime > 25f || FlatDistance(_lastKnown) < 15f || Unit.Blocked) BackToPatrol();
                else
                {
                    Steer(_lastKnown - Here, 0.7f, dt);
                    Scan();
                }
                break;
        }
    }

    /// <summary>Whom to hunt: the player, or with a free-for-all whoever hostile is nearest.</summary>
    void PickTarget(float dt)
    {
        _pickTimer -= dt;
        if (_pickTimer > 0f && _target is Node node && IsInstanceValid(node) && !_target.IsDestroyed) return;
        _pickTimer = 1.5f;

        ITarget best = null;
        float bestDist = float.MaxValue;
        foreach (var other in Units.All)
        {
            if (other.IsDestroyed || !Units.Hostile(Unit, other)) continue;
            float d = other.GlobalPosition.DistanceSquaredTo(Here);
            // The one already being fought keeps a head start, so it doesn't dither between two.
            if (other == _target) d *= 0.6f;
            if (d < bestDist)
            {
                best = other;
                bestDist = d;
            }
        }
        if (best != _target)
        {
            _sinceSeen = 999f;
            _lock = 0f;
        }
        _target = best;
    }

    void Perceive(float dt, bool alive)
    {
        _sinceSeen += dt;
        _sightTimer -= dt;
        if (_sightTimer <= 0f)
        {
            _sightTimer = 0.2f;
            _seesTarget = alive
                && Here.DistanceTo(_target.GlobalPosition) < SensorRange
                // Terrain and rocks block sensors.
                && !Phys.Ray(Unit.Body.GetWorld3D().DirectSpaceState, Unit.AimCenter, _target.AimCenter, Phys.LayerWorld, Phys.NoExclude, out _);
        }
        if (_seesTarget && alive)
        {
            _sinceSeen = 0f;
            _lastKnown = _target.GlobalPosition;
        }
    }

    public override void OnHit()
    {
        // Being shot gives the shooter away, wherever it is.
        if (_target == null || _target.IsDestroyed) return;
        _sinceSeen = 0f;
        _lastKnown = _target.GlobalPosition;
    }

    void Patrol(float dt)
    {
        if (_wait > 0f)
        {
            // Stand and sweep the turret around.
            _wait -= dt;
            Unit.Throttle = Mathf.MoveToward(Unit.Throttle, 0f, Unit.ThrottleRate * dt);
            Scan();
            return;
        }
        Unit.TargetYaw = 0f;
        Unit.TargetPitch = 0f;
        Steer(_waypoint - Here, PatrolThrottle, dt);
        if (FlatDistance(_waypoint) < 10f || Unit.Blocked)
        {
            _wait = (float)GD.RandRange(2.0, 5.0);
            PickWaypoint();
        }
    }

    void Engage(float dt)
    {
        Vector3 toTarget = _lastKnown - Here;
        toTarget.Y = 0f;
        float dist = toTarget.Length();
        bool hot = Unit.Heat > CautiousHeat;

        // Angle between "straight at the target" and where the hull goes:
        // close in obliquely, circle at the preferred range, peel away when too close or too hot.
        float angle, throttle;
        if (_brawler && !hot)
        {
            // Hand weapon: go straight in and stay on top of the target.
            angle = dist > 18f ? 0.12f : 1.2f;
            throttle = dist > 18f ? 1f : 0.5f;
        }
        else if (dist > PreferredRange * 1.35f && !hot)
        {
            angle = 0.35f;
            throttle = 1f;
        }
        else if (dist < PreferredRange * 0.55f || hot)
        {
            angle = 1.65f;
            throttle = 1f;
        }
        else
        {
            angle = 1.35f;
            throttle = 0.75f;
        }

        // Change circling direction now and then, and whenever something is in the way.
        _strafeTimer -= dt;
        _stuck = Unit.Blocked ? _stuck + dt : Mathf.Max(0f, _stuck - dt);
        // Jump jets get it over whatever it's stuck against.
        if (Unit.HasJumpJets && _stuck > 0.35f && Unit.JumpFuel > 0.6f) _jumpLeft = 1.3f;
        _jumpLeft -= dt;
        Unit.Jump = _jumpLeft > 0f;
        if (_strafeTimer <= 0f || _stuck > 0.6f)
        {
            _strafeSign = -_strafeSign;
            _strafeTimer = (float)GD.RandRange(6.0, 12.0);
            _stuck = 0f;
        }

        if (dist > 1f)
            Steer(toTarget.Rotated(Vector3.Up, angle * _strafeSign), throttle, dt);

        if (_sinceSeen > 1.5f)
        {
            // Lost sight: keep the guns on where the target was.
            AimAt(_lastKnown + Vector3.Up * 4f);
            return;
        }

        // Imperfect gunnery: the aim point wanders around the target, more at range and on the move.
        _aimTimer -= dt;
        if (_aimTimer <= 0f)
        {
            _aimTimer = (float)GD.RandRange(0.5, 1.1);
            float spread = AimSpread * dist / 100f * (1f + Mathf.Abs(Unit.Speed) / Unit.MaxForwardSpeed);
            _aimOffsetGoal = new Vector3((float)GD.RandRange(-1.0, 1.0), (float)GD.RandRange(-0.7, 0.7), (float)GD.RandRange(-1.0, 1.0)) * spread;
        }
        _aimOffset = _aimOffset.Lerp(_aimOffsetGoal, 1f - Mathf.Exp(-3f * dt));
        AimAt(_target.AimCenter + _aimOffset);

        // Shoot only when the guns are actually on the target (this is also the line-of-fire check).
        if (Units.FromCollider(Unit.AimCollider) == _target)
        {
            Unit.Fire1 = !hot;
            Unit.Fire2 = dist < 400f;
            // Machine guns and hand weapons; the weapons themselves refuse a swing out of reach.
            Unit.Fire3 = dist < 250f;
        }
    }

    /// <summary>Bomber: run in over the target, drop, fly on, come back round.</summary>
    void FlyBomber(float dt, bool hunting)
    {
        Unit.Throttle = 1f;
        var terrain = MapTerrain.Instance;
        float ground = terrain?.HeightAt(Here.X, Here.Z) ?? 0f;
        float altitude = Here.Y - ground;
        // Hold the cruise altitude.
        Unit.TargetPitch = Mathf.Clamp((_plane.CruiseAltitude - altitude) * 0.02f, -0.3f, 0.3f);

        Vector3 goal = hunting ? _lastKnown : _home;
        Vector3 to = goal - Here;
        to.Y = 0f;
        float dist = to.Length();
        _egress -= dt;
        if (_egress > 0f) return; // flying out straight before the next turn

        Vector3 forward = -Unit.Body.GlobalTransform.Basis.Z;
        float angle = forward.SignedAngleTo(to.Normalized(), Vector3.Up);
        Unit.TurnInput = Mathf.Clamp(angle * 2f, -1f, 1f);
        if (!hunting)
        {
            if (dist < 60f) _egress = 5f;
            return;
        }

        // Bombs keep the plane's speed: release early by however far they travel while falling.
        float lead = Mathf.Abs(Unit.Speed) * Mathf.Sqrt(2f * Mathf.Max(altitude, 1f) / 20f);
        bool linedUp = Mathf.Abs(angle) < 0.25f;
        if (linedUp && dist < lead + 25f && dist > lead - 35f) Unit.Fire2 = true;
        if (linedUp && Units.FromCollider(Unit.AimCollider) == _target) Unit.Fire1 = true;
        if (dist < 30f) _egress = 6f;
    }

    void BackToPatrol()
    {
        _state = State.Patrol;
        _wait = 1f;
        PickWaypoint();
    }

    // ── Controls ────────────────────────────────────────────────

    /// <summary>Turns the hull towards a world direction and works the throttle.</summary>
    void Steer(Vector3 direction, float throttle, float dt)
    {
        direction.Y = 0f;
        if (direction.LengthSquared() < 1e-4f) return;
        Vector3 forward = -Unit.Body.GlobalTransform.Basis.Z;
        float angle = forward.SignedAngleTo(direction.Normalized(), Vector3.Up); // positive = to the left
        Unit.TurnInput = Mathf.Clamp(angle * 2.5f, -1f, 1f);
        // Slow down for sharp turns instead of running wide.
        float want = throttle * (Mathf.Abs(angle) > 1.2f ? 0.4f : 1f);
        Unit.Throttle = Mathf.MoveToward(Unit.Throttle, want, Unit.ThrottleRate * dt);
    }

    /// <summary>Turns and elevates the turret so the weapons point at a world position.</summary>
    void AimAt(Vector3 point)
    {
        Vector3 local = new Basis(Vector3.Up, Unit.HullYaw).Inverse() * (point - Unit.AimAnchor.GlobalPosition);
        Unit.TargetYaw = Mathf.Clamp(Mathf.Atan2(-local.X, -local.Z), -Unit.MaxTwist, Unit.MaxTwist);
        Unit.TargetPitch = Mathf.Clamp(Mathf.Atan2(local.Y, new Vector2(local.X, local.Z).Length()), Unit.MinPitch, Unit.MaxPitch);
    }

    void Scan()
    {
        Unit.TargetYaw = Mathf.Sin(Unit.Time * 0.6f) * Mathf.Min(0.9f, Unit.MaxTwist);
        Unit.TargetPitch = 0f;
    }

    void PickWaypoint()
    {
        float angle = GD.Randf() * Mathf.Tau;
        float dist = Mathf.Sqrt(GD.Randf()) * PatrolRadius;
        _waypoint = _home + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * dist;
    }

    float FlatDistance(Vector3 point)
    {
        Vector3 d = point - Here;
        d.Y = 0f;
        return d.Length();
    }

    public override void OnRespawn()
    {
        _state = State.Patrol;
        _sinceSeen = 999f;
        _seesTarget = false;
        _wait = 2f;
        PickWaypoint();
    }
}
