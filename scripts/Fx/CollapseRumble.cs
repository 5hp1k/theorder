using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// The roar of a building coming down (scenes/fx/collapse_rumble.tscn): one looping sound that is fed
/// by every block that goes, grows with how fast they are going, follows where they are going, and dies
/// away a moment after the last one — instead of a click for every block.
/// </summary>
public partial class CollapseRumble : AudioStreamPlayer3D
{
    /// <summary>Blocks a second at which it is at its loudest.</summary>
    [Export] public float FullRate = 14f;
    /// <summary>How much quieter a single block is than a whole collapse, dB.</summary>
    [Export] public float QuietDb = -22f;
    /// <summary>Seconds it takes to swell and to die away.</summary>
    [Export] public float Attack = 0.25f;
    [Export] public float Release = 1.6f;
    /// <summary>It deepens as it grows: pitch for one block, and for a full collapse.</summary>
    [Export] public Vector2 Pitch = new(1.15f, 0.85f);

    /// <summary>Most of these sounding at once; past that, blocks feed the nearest one.</summary>
    public const int Most = 5;
    /// <summary>Blocks further than this from every roar get one of their own (if there is room).</summary>
    public const float Reach = 60f;

    static PackedScene _scene;
    static readonly List<CollapseRumble> Sounding = new();

    float _fed, _level, _loudDb;
    Vector3 _towards;

    /// <summary>A block has gone here.</summary>
    public static void Feed(Node parent, Vector3 at)
    {
        CollapseRumble nearest = null;
        float best = float.MaxValue;
        foreach (var r in Sounding)
        {
            float d = r.GlobalPosition.DistanceSquaredTo(at);
            if (d < best) { best = d; nearest = r; }
        }
        if (nearest == null || (best > Reach * Reach && Sounding.Count < Most))
        {
            _scene ??= GD.Load<PackedScene>("res://scenes/fx/collapse_rumble.tscn");
            nearest = _scene.Instantiate<CollapseRumble>();
            nearest.Position = at;
            parent.AddChild(nearest);
            Sounding.Add(nearest);
        }
        nearest._fed += 1f;
        nearest._towards = at;
    }

    public static void Reset() => Sounding.Clear();

    public override void _Ready()
    {
        _loudDb = VolumeDb;
        _towards = GlobalPosition;
        VolumeDb = -60f;
        // (Not all of them start at the same place in the loop.)
        Play((float)GD.RandRange(0.0, 4.0));
    }

    public override void _ExitTree() => Sounding.Remove(this);

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        // What has been fed drains away at the full rate: a trickle keeps it low, a flood fills it.
        _fed = Mathf.Max(0f, Mathf.Min(_fed, FullRate) - FullRate * 0.5f * dt);
        float want = Mathf.Clamp(_fed / (FullRate * 0.5f), 0f, 1f);
        _level = want > _level ? Mathf.MoveToward(_level, want, dt / Attack) : Mathf.MoveToward(_level, want, dt / Release);
        GlobalPosition = GlobalPosition.Lerp(_towards, 1f - Mathf.Exp(-2f * dt));
        VolumeDb = _level <= 0.001f ? -60f : _loudDb + Mathf.Lerp(QuietDb, 0f, Mathf.Sqrt(_level));
        PitchScale = Mathf.Lerp(Pitch.X, Pitch.Y, _level);
        if (_level <= 0f && _fed <= 0f) QueueFree();
    }
}
