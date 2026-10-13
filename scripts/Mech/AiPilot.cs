using Godot;

namespace MechSim;

/// <summary>
/// Computer pilot for mechs and vehicles. Patrols around its spawn; when it detects an enemy (sensor
/// range + line of sight, or by being shot) it closes to its preferred range and circles, turning the
/// turret to keep its guns on the target, backs off when overheating, and goes to the last known
/// position when it loses contact. Bombers fly attack runs instead. The numbers are on the node in
/// ai_pilot.tscn. A pilot of the player's squad (it has a Leader) also takes orders: see SquadOrder.
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

    /// <summary>What a pilot of the player's lance has been told to do (the orders of MechWarrior 4).</summary>
    public enum SquadOrder
    {
        /// <summary>Stay in formation on the leader, fighting whatever comes near.</summary>
        Follow,
        /// <summary>Go for one target until it is destroyed, then back into formation.</summary>
        Attack,
        /// <summary>Stand where it is and fight from there.</summary>
        Hold,
        /// <summary>Hunt on its own, like any other machine.</summary>
        Free,
        /// <summary>Stay by one machine and fight off whoever goes for it.</summary>
        Defend,
        /// <summary>Go to a place and hold it.</summary>
        GoTo,
        /// <summary>Power down where it stands.</summary>
        Shutdown,
        /// <summary>Go to the nearest repair bay, get mended, come back.</summary>
        Repair,
    }

    public static readonly string[] OrderNames = { "В СТРОЮ", "АТАКУЕТ", "СТОИТ", "ОХОТА", "ПРИКРЫВАЕТ", "ИДЁТ В ТОЧКУ", "ЗАГЛУШЕН", "НА РЕМОНТ" };

    /// <summary>The player's machine, for a pilot of the squad; null for everybody else.</summary>
    public IDrivable Leader;
    /// <summary>Where it rides in the formation, behind and beside the leader (in the leader's own axes).</summary>
    public Vector3 FormationSlot;
    public SquadOrder Order { get; private set; } = SquadOrder.Follow;
    public ITarget OrderTarget { get; private set; }
    /// <summary>Told not to shoot (it still aims).</summary>
    public bool HoldFire { get; set; }
    Vector3 _orderPoint;
    /// <summary>The machine this pilot drives.</summary>
    public IDrivable Machine => Unit;
    /// <summary>How far from the leader a pilot in formation will chase a target before it comes back.</summary>
    [Export] public float Leash = 170f;

    /// <summary>Gives the pilot an order: with a target for attack and defend, with a place for "go to".</summary>
    public void Command(SquadOrder order, ITarget target = null, Vector3 point = default)
    {
        if (order is SquadOrder.Attack or SquadOrder.Defend && (target == null || target.IsDestroyed)) order = SquadOrder.Follow;
        if (order == SquadOrder.Repair)
        {
            var bay = RepairBay.Nearest(Here);
            if (bay == null) order = SquadOrder.Follow;
            else point = bay.GlobalPosition;
        }
        Order = order;
        OrderTarget = order is SquadOrder.Attack or SquadOrder.Defend ? target : null;
        _orderPoint = point;
        if (order == SquadOrder.Hold) _home = Here;
        if (order is SquadOrder.GoTo or SquadOrder.Repair) _home = point;
        _pickTimer = 0f;
        if (_state != State.Engage || order is SquadOrder.Shutdown or SquadOrder.Repair or SquadOrder.GoTo) BackToPatrol();
    }

    /// <summary>Standing its ground rather than manoeuvring: held, sent somewhere, or going for repairs.</summary>
    bool Anchored => Leader != null && Order is SquadOrder.Hold or SquadOrder.GoTo or SquadOrder.Repair;

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
    /// <summary>Share of the sensor range the pilot still sees to with the electronics knocked out.</summary>
    [Export(PropertyHint.Range, "0,1")] public float BlindSight = 0.3f;
    [ExportGroup("Nuclear bomb")]
    /// <summary>How many hostile machines must stand together before the bomb is worth dropping on them.</summary>
    [Export] public int NukeCluster = 3;
    /// <summary>They count as together within this many metres of one another.</summary>
    [Export] public float NukeClusterRadius = 150f;
    /// <summary>None of its own side may be this close to where the bomb would go.</summary>
    [Export] public float NukeSafeRadius = 330f;
    /// <summary>Not in the first seconds of a battle: both sides get to fight first.</summary>
    [Export] public float NukeNotBefore = 60f;
    [ExportGroup("")]

    /// <summary>The computer pilots have had their one bomb for this battle.</summary>
    public static bool NukeSpent;

    Vector3 _nukeAim;
    bool _nukeRun;
    float _nukeTimer;
    bool _brawler;
    Vehicle _plane;

    Vector3 Here => Unit.GlobalPosition;

    public override void Attach(IDrivable unit)
    {
        base.Attach(unit);
        _home = unit.GlobalPosition;
        foreach (var w in unit.Weapons.Weapons) _brawler |= w.Kind == WeaponKind.Melee && w.Damage > 20f;
        unit.Weapons.MeleeInReachOnly = true;
        unit.Weapons.HoldStrategic = true;
        if (unit is Vehicle { Kind: Vehicle.MoveKind.Plane } plane) _plane = plane;
        _strafeSign = GD.Randf() < 0.5f ? -1f : 1f;
        _wait = GD.Randf() * 3f;
        PickWaypoint();
        // Lamps on in the dark.
        Headlight.Set(unit.Body, MapLighting.Dark);
    }

    // ── Brain ───────────────────────────────────────────────────

    public override void Think(float dt)
    {
        if (Unit.Offline) return;
        PickTarget(dt);
        bool alive = _target != null && !_target.IsDestroyed;
        Perceive(dt, alive);
        // Missile lock, earned the same way the player earns it: by holding the sight on the target.
        // (With its electronics burnt out by a pulse there is no lock to be had.)
        bool onTarget = alive && !Emp.Jammed(Unit) && Units.FromCollider(Unit.AimCollider) == _target;
        if (Emp.Jammed(Unit)) _lock = 0f;
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
                // In formation it doesn't let itself be drawn too far from the leader.
                else if (Leader != null && Order is SquadOrder.Follow or SquadOrder.Defend && FlatDistance(Leader.GlobalPosition) > Leash) BackToPatrol();
                else if (Leader != null && Order is SquadOrder.Shutdown or SquadOrder.Repair) BackToPatrol();
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
        if (Leader != null && (HoldFire || Order is SquadOrder.Shutdown or SquadOrder.Repair && NeedsWork() && FlatDistance(_home) < 20f))
            Unit.Fire1 = Unit.Fire2 = Unit.Fire3 = false;
    }

    /// <summary>Whom to hunt: the player, or with a free-for-all whoever hostile is nearest.</summary>
    void PickTarget(float dt)
    {
        _pickTimer -= dt;
        if (_pickTimer > 0f && _target is Node node && IsInstanceValid(node) && !_target.IsDestroyed) return;
        _pickTimer = 1.5f;

        // Told to attack something: that, until it is down.
        if (Leader != null && Order == SquadOrder.Attack)
        {
            if (OrderTarget is Node ordered && IsInstanceValid(ordered) && !OrderTarget.IsDestroyed)
            {
                if (_target != OrderTarget)
                {
                    _sinceSeen = 0f;
                    _lastKnown = OrderTarget.GlobalPosition;
                    _lock = 0f;
                }
                _target = OrderTarget;
                return;
            }
            Command(SquadOrder.Follow);
        }

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
                // Sensors dead (a pulse): only what the pilot's own eyes make out.
                && Here.DistanceTo(_target.GlobalPosition) < SensorRange * (Emp.Jammed(Unit) ? BlindSight : 1f)
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
        if (Leader != null && Order == SquadOrder.Shutdown)
        {
            Unit.Throttle = Mathf.MoveToward(Unit.Throttle, 0f, Unit.ThrottleRate * 3f * dt);
            Unit.TargetYaw = Unit.TargetPitch = 0f;
            return;
        }
        if (Leader != null && Order == SquadOrder.Repair && !NeedsWork() && FlatDistance(_home) < 14f)
            Command(SquadOrder.Follow); // mended: back to the lance
        if (Leader != null && Order is SquadOrder.Follow or SquadOrder.Attack or SquadOrder.Defend)
        {
            // In formation: make for its place by the leader (or by the one it guards), keeping up.
            Vector3 place = Leader.GlobalPosition + new Basis(Vector3.Up, Leader.HullYaw) * FormationSlot;
            if (Order == SquadOrder.Defend && OrderTarget is Node guarded && IsInstanceValid(guarded) && !OrderTarget.IsDestroyed)
                place = OrderTarget.GlobalPosition + FormationSlot.Normalized() * 18f;
            float away = FlatDistance(place);
            Unit.TargetYaw = 0f;
            Unit.TargetPitch = 0f;
            if (away > 9f) Steer(place - Here, Mathf.Clamp(away / 45f, 0.35f, 1f), dt);
            else
            {
                Unit.Throttle = Mathf.MoveToward(Unit.Throttle, 0f, Unit.ThrottleRate * dt);
                Scan();
            }
            return;
        }
        if (Anchored)
        {
            if (FlatDistance(_home) > (Order == SquadOrder.Repair ? 4f : 12f)) Steer(_home - Here, Order == SquadOrder.Hold ? 0.6f : 1f, dt);
            else
            {
                Unit.Throttle = Mathf.MoveToward(Unit.Throttle, 0f, Unit.ThrottleRate * 2f * dt);
                Scan();
            }
            return;
        }
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

        if (Anchored)
        {
            // Holding: fight from where it stands, turning the hull towards the enemy.
            if (FlatDistance(_home) > 15f) Steer(_home - Here, 0.6f, dt);
            else
            {
                Steer(toTarget, 0f, dt);
                Unit.Throttle = Mathf.MoveToward(Unit.Throttle, 0f, Unit.ThrottleRate * dt);
            }
        }
        else if (dist > 1f)
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

    /// <summary>
    /// Whether there is something worth the nuclear bomb: a knot of hostile machines with none of this
    /// pilot's own side near it. Looked for now and then, and only while the bomb is aboard, allowed in
    /// the battle's settings and not yet used by any computer pilot.
    /// </summary>
    void LookForNukeTarget(float dt)
    {
        Unit.Weapons.ReleaseStrategic = false;
        _nukeTimer -= dt;
        if (_nukeTimer > 0f) return;
        _nukeTimer = 1f;
        _nukeRun = false;
        if (!GameConfig.BotNukes || NukeSpent || Unit.Time < NukeNotBefore || Emp.Jammed(Unit) || Unit.Weapons.ReadyStrategic == null) return;

        int best = 0;
        foreach (var middle in Units.All)
        {
            if (middle.IsDestroyed || !Units.Hostile(Unit, middle)) continue;
            Vector3 sum = Vector3.Zero;
            int count = 0;
            foreach (var other in Units.All)
            {
                if (other.IsDestroyed || !Units.Hostile(Unit, other) || other.GlobalPosition.DistanceTo(middle.GlobalPosition) > NukeClusterRadius) continue;
                sum += other.GlobalPosition;
                count++;
            }
            if (count <= best) continue;
            Vector3 aim = sum / count;
            bool ownNear = false;
            foreach (var other in Units.All)
                ownNear |= other != Unit && !other.IsDestroyed && !Units.Hostile(Unit, other) && other.GlobalPosition.DistanceTo(aim) < NukeSafeRadius;
            if (ownNear) continue;
            best = count;
            _nukeAim = aim;
        }
        _nukeRun = best >= NukeCluster;
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

        LookForNukeTarget(dt);
        Vector3 goal = _nukeRun ? _nukeAim : hunting ? _lastKnown : _home;
        Vector3 to = goal - Here;
        to.Y = 0f;
        float dist = to.Length();
        _egress -= dt;
        if (_egress > 0f) return; // flying out straight before the next turn

        Vector3 forward = -Unit.Body.GlobalTransform.Basis.Z;
        float angle = forward.SignedAngleTo(to.Normalized(), Vector3.Up);
        Unit.TurnInput = Mathf.Clamp(angle * 2f, -1f, 1f);
        if (_nukeRun)
        {
            // The bomb comes down on a parachute, almost straight: let go a second's flight short of the
            // place, then straight on at full power to be far away when it lands.
            float carry = Mathf.Abs(Unit.Speed) * 0.9f;
            if (Mathf.Abs(angle) < 0.3f && dist < carry + 40f && Unit.Weapons.ReadyStrategic is { } bomb)
            {
                Unit.Weapons.ReleaseStrategic = true;
                Unit.Fire1 |= bomb.Group == 1;
                Unit.Fire2 |= bomb.Group == 2;
                Unit.Fire3 |= bomb.Group == 3;
                NukeSpent = true;
                _nukeRun = false;
                _egress = 9f;
                Unit.TurnInput = 0f;
            }
            return;
        }
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

    bool NeedsWork() => Unit switch
    {
        Mech mech => mech.NeedsRepair || mech.Repairing,
        Vehicle vehicle => vehicle.NeedsRepair || vehicle.Repairing,
        _ => false,
    };

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
