using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// A field repair bay (scenes/props/repair_bay.tscn): a shed with sliding doors. The first machine of
/// the player's side that stops inside with damage or spent ammunition is taken in hand: the doors
/// close on it, it is powered down for RepairTime seconds, and it comes out whole and rearmed. A bay
/// works once: after that its beacon goes red and it is off the radar. Aircraft can't use it.
/// </summary>
public partial class RepairBay : Node3D
{
    /// <summary>How close to the middle of the floor a machine has to stop, metres.</summary>
    [Export] public float Radius = 7f;
    [Export] public float RepairTime = 14f;
    /// <summary>How far each door leaf slides to shut (along its own X, towards the middle), and how long it takes.</summary>
    [Export] public float DoorTravel = 9.3f;
    [Export] public float DoorTime = 2.2f;

    /// <summary>Bays that can still be used.</summary>
    public static readonly List<RepairBay> All = new();

    Node3D _doorL, _doorR, _ready, _spent;
    Light3D _workLight;
    Vector3 _openL, _openR;
    ITarget _patient;
    bool _used;
    int _tick;

    public override void _EnterTree() => All.Add(this);
    public override void _ExitTree() => All.Remove(this);

    public override void _Ready()
    {
        _workLight = GetNodeOrNull<Light3D>("WorkLight");
        _doorL = GetNodeOrNull<Node3D>("DoorL");
        _doorR = GetNodeOrNull<Node3D>("DoorR");
        _ready = GetNodeOrNull<Node3D>("BeaconReady");
        _spent = GetNodeOrNull<Node3D>("BeaconSpent");
        if (_doorL != null) _openL = _doorL.Position;
        if (_doorR != null) _openR = _doorR.Position;
        if (_spent != null) _spent.Visible = false;
    }

    void Doors(bool shut)
    {
        var tween = CreateTween().SetParallel().SetTrans(Tween.TransitionType.Sine);
        if (_doorL != null) tween.TweenProperty(_doorL, "position", _openL + (shut ? _doorL.Basis.X * DoorTravel : Vector3.Zero), DoorTime);
        if (_doorR != null) tween.TweenProperty(_doorR, "position", _openR - (shut ? _doorR.Basis.X * DoorTravel : Vector3.Zero), DoorTime);
    }

    /// <summary>The bay nearest to a point, or null if the map has none.</summary>
    public static RepairBay Nearest(Vector3 from)
    {
        RepairBay best = null;
        float bestDist = float.MaxValue;
        foreach (var bay in All)
        {
            float d = bay.GlobalPosition.DistanceSquaredTo(from);
            if (d < bestDist)
            {
                best = bay;
                bestDist = d;
            }
        }
        return best;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (++_tick % 10 != 0) return;
        if (_patient != null)
        {
            // Working: when the machine is done (or lost), the doors open and the bay is finished.
            bool busy = _patient is Node node && IsInstanceValid(node) && !_patient.IsDestroyed
                        && (_patient is Mech { Repairing: true } || _patient is Vehicle { Repairing: true });
            if (busy) return;
            _patient = null;
            Doors(false);
            if (_workLight != null) _workLight.Visible = false;
            return;
        }
        if (_used) return;
        foreach (var unit in Units.All)
        {
            if (unit.Team != Team.Player || unit.IsDestroyed) continue;
            Vector3 d = unit.GlobalPosition - GlobalPosition;
            d.Y = 0f;
            if (d.Length() > Radius) continue;
            bool taken = false;
            switch (unit)
            {
                case Mech mech when !mech.Repairing && mech.NeedsRepair && Mathf.Abs(mech.Speed) < 1.5f:
                    mech.BeginRepair(RepairTime);
                    taken = true;
                    break;
                case Vehicle vehicle when !vehicle.IsAircraft && !vehicle.Repairing && vehicle.NeedsRepair && Mathf.Abs(vehicle.Speed) < 1.5f:
                    vehicle.BeginRepair(RepairTime);
                    taken = true;
                    break;
            }
            if (!taken) continue;
            // One machine, once.
            _patient = unit;
            _used = true;
            All.Remove(this);
            Doors(true);
            if (_workLight != null) _workLight.Visible = true;
            if (_ready != null) _ready.Visible = false;
            if (_spent != null) _spent.Visible = true;
            break;
        }
    }
}
