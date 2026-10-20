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
        if (order is SquadOrder.GoTo or SquadOrder.Repair) _home = Inside(point, 20f);
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
    bool _laid;

    /// <summary>Seconds the guns must stay on the target before its missiles will home.</summary>
    [Export] public float LockTime = 1.8f;
    /// <summary>
    /// Share of a fight at its preferred range a mech's pilot spends down on one knee, shooting from a
    /// standstill, and how long it stays down each time (from, to; seconds).
    /// </summary>
    [Export(PropertyHint.Range, "0,1")] public float KneelShare = 0.4f;
    [Export] public Vector2 KneelSpell = new(4f, 9f);
    [ExportGroup("")]
    /// <summary>How far off the target the sight may wander and still keep the lock building, radians.</summary>
    [Export] public float LockCone = 0.07f;
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
    /// <summary>A ship: keeps to water deep enough for its keel, whatever it is after.</summary>
    Vehicle _ship;
    bool _landbound;
    Vector3 _safeWay;
    float _safeTimer, _aground, _backOff, _sternStuck, _turnAway = 1f;
    bool _ahead;
    /// <summary>Artillery or a rocket launcher: it fires over the ground between, at targets it is told of rather than sees.</summary>
    Vehicle _battery;

    Vector3 Here => Unit.GlobalPosition;

    public override void Attach(IDrivable unit)
    {
        base.Attach(unit);
        _home = unit.GlobalPosition;
        foreach (var w in unit.Weapons.Weapons) _brawler |= w.Kind == WeaponKind.Melee && w.Damage > 20f;
        unit.Weapons.MeleeInReachOnly = true;
        unit.Weapons.HoldStrategic = true;
        unit.Weapons.HoldEmp = !(CampaignRun.Mission?.EnemyEmp ?? GameConfig.BotEmp);
        if (unit is Vehicle { Kind: Vehicle.MoveKind.Plane } plane) _plane = plane;
        if (unit is Vehicle { Kind: Vehicle.MoveKind.Ship } ship) _ship = ship;
        // Tanks and the like drown in deep water: they keep out of it.
        _landbound = unit is Vehicle { Kind: Vehicle.MoveKind.Ground };
        // Artillery and rocket launchers stand off, and are told of targets that far away.
        if (unit is Vehicle { AiRange: > 0f } gunner)
        {
            _battery = gunner;
            PreferredRange = gunner.AiRange;
            SensorRange = Mathf.Max(SensorRange, gunner.AiRange * 1.3f);
        }
        _strafeSign = GD.Randf() < 0.5f ? -1f : 1f;
        _wait = GD.Randf() * 3f;
        PickWaypoint();
        // Lamps on in the dark.
        Headlight.Set(unit.Body, MapLighting.Dark);
    }

    // ── Brain ───────────────────────────────────────────────────

    /// <summary>
    /// Told where an enemy is (a mission's machine sent after the player): it heads for that place and
    /// goes for it, as if it had been shot at from there.
    /// </summary>
    public void Alert(ITarget enemy)
    {
        if (enemy == null || enemy.IsDestroyed) return;
        _target = enemy;
        _pickTimer = 3f;
        _sinceSeen = 0f;
        _lastKnown = enemy.GlobalPosition;
        _home = enemy.GlobalPosition;
        PickWaypoint();
    }

    public override void Think(float dt)
    {
        if (Unit.Offline) return;
        // A cutscene holds the battle: nobody fires or moves.
        if (Cutscene.Holding)
        {
            Unit.Fire = 0;
            Unit.Throttle = 0f;
            Unit.TurnInput = 0f;
            return;
        }
        PickTarget(dt);
        bool alive = _target != null && !_target.IsDestroyed;
        Perceive(dt, alive);
        // Missile lock, earned the same way the player earns it: by holding the sight on the target — near
        // enough, as the guns wander around it (see AimSpread), while it is in sight.
        // (With its electronics burnt out by a pulse there is no lock to be had.)
        bool onTarget = alive && !Emp.Jammed(Unit) && (Units.FromCollider(Unit.AimCollider) == _target || (_battery != null && _laid)
            || _sinceSeen < 0.5f && (Unit.AimPoint - Unit.AimCenter).AngleTo(_target.AimCenter - Unit.AimCenter) < LockCone);
        if (Emp.Jammed(Unit)) _lock = 0f;
        _lock = Mathf.Clamp(_lock + (onTarget ? dt / LockTime : -dt * 1.5f), 0f, 1f);
        Unit.LockTarget = _lock >= 1f ? _target : null;

        if (_plane != null)
        {
            FlyBomber(dt, alive && _sinceSeen < MemoryTime);
            KeepInside(dt);
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
                    Steer(Inside(_lastKnown) - Here, 0.7f, dt);
                    Scan();
                }
                break;
        }
        KeepInside(dt);
        if (Unit is Mech mech)
        {
            if (_state != State.Engage) _kneeling = false;
            mech.Kneel = _kneeling;
        }
        if (Leader != null && (HoldFire || Order is SquadOrder.Shutdown or SquadOrder.Repair && NeedsWork() && FlatDistance(_home) < 20f))
            Unit.Fire = 0;
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
                // Terrain and rocks block sensors (a battery is passed its targets by those who do see them).
                && (_battery != null && !Emp.Jammed(Unit) || !Phys.Ray(Unit.Body.GetWorld3D().DirectSpaceState, Unit.AimCenter, _target.AimCenter, Phys.LayerWorld, Phys.NoExclude, out _));
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
        // (A ship with its bow on a shoal is not "there": it backs off and tries again, see Steer.)
        if (FlatDistance(_waypoint) < 10f || (Unit.Blocked && _ship == null))
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

        Brace(dt, dist, hot, toTarget);

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
            float spread = AimSpread * dist / 100f * (1f + Mathf.Abs(Unit.Speed) / Unit.MaxForwardSpeed) * (_kneeling ? 0.75f : 1f);
            _aimOffsetGoal = new Vector3((float)GD.RandRange(-1.0, 1.0), (float)GD.RandRange(-0.7, 0.7), (float)GD.RandRange(-1.0, 1.0)) * spread;
        }
        _aimOffset = _aimOffset.Lerp(_aimOffsetGoal, 1f - Mathf.Exp(-3f * dt));
        _laid = false;
        if (_battery != null)
        {
            // Indirect fire: the gun is laid on the target's place by the fire control (a howitzer), or the
            // launcher is tipped up to send its rockets over whatever lies between; it fires once the
            // turret has come round, with no need of a line of sight. Not at anything right on top of it.
            Vector3 mark = _target.GlobalPosition + _aimOffset;
            bool lobs = false;
            foreach (var w in Unit.Weapons.Weapons) lobs |= w.Lobbed;
            if (lobs) _battery.AimOverride = mark;
            AimAt(lobs ? mark : _target.AimCenter + Vector3.Up * (dist * 0.3f));
            _laid = Mathf.Abs(Mathf.AngleDifference(Unit.TorsoYaw, Unit.TargetYaw)) < 0.06f;
            Unit.Fire = _laid && dist > 45f && dist < PreferredRange * 1.6f && (lobs || Unit.LockTarget != null) ? WeaponSystem.Bit(1) : 0;
            return;
        }
        AimAt(_target.AimCenter + _aimOffset);

        // Shoot only when the guns are actually on the target (this is also the line-of-fire check).
        if (Units.FromCollider(Unit.AimCollider) == _target)
        {
            int fire = hot ? 0 : WeaponSystem.Bit(1);
            if (dist < Mathf.Max(400f, PreferredRange * 1.4f)) fire |= WeaponSystem.Bit(2);
            // Machine guns and hand weapons; the weapons themselves refuse a swing out of reach.
            // (Groups 4–6 are the player's own; a stock machine has nothing there, but fire them with 3.)
            if (dist < 250f) fire |= WeaponSystem.Bit(3) | WeaponSystem.Bit(4) | WeaponSystem.Bit(5) | WeaponSystem.Bit(6);
            Unit.Fire = fire;
        }
    }

    bool _kneeling;
    float _kneelTimer;

    /// <summary>
    /// A mech fighting at its range now and then stops and goes down on one knee to shoot, steadier and
    /// lower; it gets up when the spell is over, or sooner if it runs hot, loses the target, the enemy
    /// closes in or gets round past where the torso turns.
    /// </summary>
    void Brace(float dt, float dist, bool hot, Vector3 toTarget)
    {
        if (Unit is not Mech mech || _brawler || KneelShare <= 0f)
        {
            _kneeling = false;
            return;
        }
        float off = Mathf.Abs(Mathf.AngleDifference(Mathf.Atan2(-toTarget.X, -toTarget.Z), mech.HullYaw));
        bool fit = !hot && _sinceSeen < 2f && dist > PreferredRange * 0.45f && dist < PreferredRange * 1.5f && off < mech.MaxTwist * 0.85f;
        _kneelTimer -= dt;
        if (_kneeling && !fit)
        {
            _kneeling = false;
            _kneelTimer = (float)GD.RandRange(3.0, 6.0);
        }
        else if (_kneelTimer <= 0f)
        {
            if (_kneeling)
            {
                _kneeling = false;
                // Up and moving for a while: long enough that it is down for its share of the time.
                float mean = (KneelSpell.X + KneelSpell.Y) * 0.5f;
                _kneelTimer = mean * (1f - KneelShare) / Mathf.Max(KneelShare, 0.05f) * (float)GD.RandRange(0.6, 1.4);
            }
            else if (fit && _sinceSeen < 0.5f)
            {
                _kneeling = true;
                _kneelTimer = (float)GD.RandRange(KneelSpell.X, KneelSpell.Y);
            }
            else _kneelTimer = 1f;
        }
        if (_kneeling)
        {
            Unit.Throttle = 0f;
            Unit.TurnInput = 0f;
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
        if (!(CampaignRun.Mission?.EnemyNukes ?? GameConfig.BotNukes) || NukeSpent || Unit.Time < NukeNotBefore || Emp.Jammed(Unit) || Unit.Weapons.ReadyStrategic == null) return;

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
                Unit.Fire |= WeaponSystem.Bit(bomb.Group);
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
        if (linedUp && dist < lead + 25f && dist > lead - 35f) Unit.Fire |= WeaponSystem.Bit(2);
        if (linedUp && Units.FromCollider(Unit.AimCollider) == _target) Unit.Fire |= WeaponSystem.Bit(1);
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
        if (_ship != null)
        {
            // Run aground anyway: go astern for a while, swinging the bow round, then try again.
            _aground = Unit.Blocked ? _aground + dt : Mathf.Max(0f, _aground - dt);
            if (_aground > 0.8f)
            {
                _backOff = 4f;
                _aground = 0f;
                // Swing the bow towards the deeper side.
                _turnAway = DeeperSide();
            }
            if (_backOff > 0f)
            {
                _backOff -= dt;
                // Shoal astern as well: ahead slowly instead, still swinging round — and back again if that grounds too.
                _sternStuck = Unit.Blocked ? _sternStuck + dt : 0f;
                if (_sternStuck > 0.6f)
                {
                    _sternStuck = 0f;
                    _ahead = !_ahead;
                }
                float gear = _ahead ? 0.35f : -Unit.ReverseFraction;
                Unit.Throttle = Mathf.MoveToward(Unit.Throttle, gear, Unit.ThrottleRate * dt);
                Unit.TurnInput = _turnAway;
                if (_backOff <= 0f) _ahead = false;
                return;
            }
            direction = SeaWay(direction, dt);
        }
        else if (_landbound) direction = DryWay(direction, dt);
        Vector3 forward = -Unit.Body.GlobalTransform.Basis.Z;
        float angle = forward.SignedAngleTo(direction.Normalized(), Vector3.Up); // positive = to the left
        Unit.TurnInput = Mathf.Clamp(angle * 2.5f, -1f, 1f);
        // Slow down for sharp turns instead of running wide.
        float want = throttle * (Mathf.Abs(angle) > 1.2f ? 0.4f : 1f);
        // A land machine with water right ahead where it is going now (a turn at speed swings wide):
        // it backs off rather than carry on into it.
        if (_landbound)
        {
            float travel = Unit.Speed >= 0f ? 1f : -1f;
            float stop = Mathf.Max(Mathf.Abs(Unit.Speed) * 1.5f, 6f);
            if (!Dry(Here + forward * travel * stop) || !Dry(Here + forward * travel * stop * 0.5f))
                want = travel > 0f ? -Unit.ReverseFraction * 0.7f : 0.5f;
        }
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

    /// <summary>
    /// The battlefield has an edge, and a machine that stays beyond it is lost: whatever the pilot was
    /// doing, if the machine is out there or will be in a few seconds on its present heading, it turns
    /// back in (still shooting: the turret is its own). Planes, which can't stop, turn well before.
    /// </summary>
    void KeepInside(float dt)
    {
        var terrain = MapTerrain.Instance;
        if (terrain == null) return;
        float line = terrain.PlayHalf;
        Vector3 forward = -Unit.Body.GlobalTransform.Basis.Z;
        bool outside = terrain.OutOfBounds(Here);
        if (_plane != null)
        {
            // A plane turns in a circle of radius v² / (g · tan bank): it has to start the turn that far
            // from the edge, heading out — and slows down for it, the circle shrinking with the speed.
            float v = Mathf.Abs(Unit.Speed);
            float radius = v * v / (_plane.FlightGravity * Mathf.Tan(Mathf.Clamp(_plane.BankAngle, 0.2f, 1.2f)));
            float edge = line - Mathf.Min(radius * 1.1f + 40f, line * 0.8f);
            bool outX = Mathf.Abs(Here.X) > edge && forward.X * Mathf.Sign(Here.X) > -0.2f;
            bool outZ = Mathf.Abs(Here.Z) > edge && forward.Z * Mathf.Sign(Here.Z) > -0.2f;
            if (!outside && !outX && !outZ) return;
            Vector3 home = new Vector3(-Here.X, 0f, -Here.Z);
            float angle = forward.SignedAngleTo(home.Normalized(), Vector3.Up);
            _egress = 0f;
            Unit.TurnInput = Mathf.Clamp(angle * 2f, -1f, 1f);
            Unit.Throttle = Mathf.Abs(angle) > 0.4f ? 0.15f : 1f;
            return;
        }
        float look = Mathf.Max(Mathf.Abs(Unit.Speed) * 2.5f, 10f);
        Vector3 ahead = Here + forward * look * (Unit.Speed < -0.5f ? -1f : 1f);
        float limit = line - 15f;
        if (!outside && Mathf.Abs(ahead.X) < limit && Mathf.Abs(ahead.Z) < limit) return;
        // Square in from whichever edges are near.
        var inward = new Vector3(Mathf.Abs(Here.X) > limit - look ? -Mathf.Sign(Here.X) : 0f, 0f, Mathf.Abs(Here.Z) > limit - look ? -Mathf.Sign(Here.Z) : 0f);
        if (inward == Vector3.Zero) inward = -new Vector3(Here.X, 0f, Here.Z);
        Steer(inward, outside ? 1f : 0.8f, dt);
    }

    /// <summary>A point brought in from the edge of the battlefield, if it was near it or beyond.</summary>
    static Vector3 Inside(Vector3 p, float keepOff = 40f)
    {
        if (MapTerrain.Instance is not { } terrain) return p;
        float limit = terrain.PlayHalf - keepOff;
        return new Vector3(Mathf.Clamp(p.X, -limit, limit), p.Y, Mathf.Clamp(p.Z, -limit, limit));
    }

    void PickWaypoint()
    {
        for (int tries = 0; tries < 12; tries++)
        {
            float angle = GD.Randf() * Mathf.Tau;
            // (A ship ranges further: there is more sea than a patrol circle.)
            float dist = Mathf.Sqrt(GD.Randf()) * PatrolRadius * (_ship != null ? 2.5f : 1f);
            _waypoint = Inside(_home + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * dist);
            if (_ship == null ? !_landbound || Dry(_waypoint) : Navigable(_waypoint)) return;
        }
        // No open water near (a ship that has wandered up a river): make for the open sea.
        _waypoint = _ship != null ? MapTerrain.Instance?.RandomSeaSpot(24f) ?? _home : _home;
    }

    /// <summary>+1 if there is more water to the left of the ship than to its right, -1 otherwise.</summary>
    float DeeperSide()
    {
        var terrain = MapTerrain.Instance;
        if (terrain == null) return 1f;
        Basis b = Unit.Body.GlobalTransform.Basis;
        float left = 0f, right = 0f;
        foreach (float d in new[] { 15f, 30f, 45f })
        {
            Vector3 l = Here - b.X * d, r = Here + b.X * d;
            left += terrain.WaterDepthAt(l.X, l.Z);
            right += terrain.WaterDepthAt(r.X, r.Z);
        }
        return left >= right ? 1f : -1f;
    }

    /// <summary>Deep enough for the ship's keel, and inside the map.</summary>
    bool Navigable(Vector3 p)
    {
        var terrain = MapTerrain.Instance;
        if (terrain == null) return true;
        float edge = terrain.PlayHalf - 10f;
        return Mathf.Abs(p.X) < edge && Mathf.Abs(p.Z) < edge && terrain.WaterDepthAt(p.X, p.Z) > _ship.Draft + 0.8f;
    }

    /// <summary>
    /// The heading a ship takes for the one it wants: that one if the water ahead is deep enough for a
    /// good way, otherwise the nearest to it that is (looked for a few times a second, both ways round).
    /// A target on land is shot at from the water's edge, not run at.
    /// </summary>
    Vector3 SeaWay(Vector3 want, float dt)
    {
        _safeTimer -= dt;
        if (_safeTimer > 0f && _safeWay != Vector3.Zero) return _safeWay;
        _safeTimer = 0.25f;
        want = want.Normalized();
        float look = _ship.BowDistance + Mathf.Max(Mathf.Abs(Unit.Speed) * 3f, 15f);
        foreach (float turn in new[] { 0f, 0.35f, -0.35f, 0.7f, -0.7f, 1.05f, -1.05f, 1.4f, -1.4f, 1.8f, -1.8f, 2.3f, -2.3f, 2.8f, -2.8f, Mathf.Pi })
        {
            Vector3 way = want.Rotated(Vector3.Up, turn);
            bool clear = true;
            for (int k = 1; k <= 4 && clear; k++) clear = Navigable(Here + way * (look * k / 4f));
            if (!clear) continue;
            return _safeWay = way;
        }
        // Hemmed in: back the way it came.
        return _safeWay = Unit.Body.GlobalTransform.Basis.Z;
    }

    /// <summary>Somewhere a machine that can't swim can go: no deeper than its wheels, or a bridge over the water.</summary>
    bool Dry(Vector3 p)
    {
        var terrain = MapTerrain.Instance;
        if (terrain is not { HasWater: true }) return true;
        return terrain.WaterDepthAt(p.X, p.Z) < 0.6f || ChunkedBuilding.DeckAt(p.X, p.Z, Here.Y + 3f, out _);
    }

    /// <summary>The heading a land machine takes for the one it wants: that one if the way ahead is dry, otherwise the nearest that is.</summary>
    Vector3 DryWay(Vector3 want, float dt)
    {
        _safeTimer -= dt;
        if (_safeTimer > 0f && _safeWay != Vector3.Zero) return _safeWay;
        _safeTimer = 0.25f;
        want = want.Normalized();
        float look = Mathf.Max(Mathf.Abs(Unit.Speed) * 2.5f, 12f);
        foreach (float turn in new[] { 0f, 0.35f, -0.35f, 0.7f, -0.7f, 1.05f, -1.05f, 1.4f, -1.4f, 1.8f, -1.8f, 2.3f, -2.3f, 2.8f, -2.8f, Mathf.Pi })
        {
            Vector3 way = want.Rotated(Vector3.Up, turn);
            bool clear = true;
            for (int k = 1; k <= 4 && clear; k++) clear = Dry(Here + way * (look * k / 4f));
            if (clear) return _safeWay = way;
        }
        // Water all round (it has got in already): the shortest way out, whatever it is.
        return _safeWay = want;
    }

    float FlatDistance(Vector3 point)
    {
        Vector3 d = point - Here;
        d.Y = 0f;
        return d.Length();
    }

    public override void OnRespawn()
    {
        _kneeling = false;
        _state = State.Patrol;
        _sinceSeen = 999f;
        _seesTarget = false;
        _wait = 2f;
        PickWaypoint();
    }
}
