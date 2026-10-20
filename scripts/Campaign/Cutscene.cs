using Godot;

namespace MechSim;

/// <summary>
/// A cutscene played on the battlefield: its own camera (the child "Camera"), moved by its
/// AnimationPlayer ("Animator") — along a Path3D, from key to key, however the scene animates it —
/// with lines said over the top as subtitles. While it plays the interface is hidden, the picture is
/// letterboxed, the player has no controls and (with HoldBattle) every machine holds its fire and
/// stands. Esc skips it (if Skippable). When it ends, the camera goes back to the player and its
/// OnFinished steps run.
/// </summary>
public partial class Cutscene : Node3D, IStep
{
    /// <summary>The one playing now, if any.</summary>
    public static Cutscene Playing { get; private set; }

    /// <summary>Animation of the "Animator" child to play; without one it lasts Length seconds.</summary>
    [Export] public StringName Animation = "play";
    [Export] public float Length = 6f;
    /// <summary>Said in turn from its start.</summary>
    [Export] public RadioLine[] Lines = System.Array.Empty<RadioLine>();
    [Export] public bool Skippable = true;
    /// <summary>Nobody fights or moves while it plays.</summary>
    [Export] public bool HoldBattle = true;
    /// <summary>It lasts at least until its lines have been said.</summary>
    [Export] public bool WaitForLines = true;
    [Export] public Godot.Collections.Array<NodePath> OnFinished = new();

    Camera3D _camera;
    AnimationPlayer _animator;
    float _left;

    public static bool Holding => Playing is { HoldBattle: true };

    public void Run() => Play();

    public void Play()
    {
        if (Playing != null) Playing.Finish();
        _camera = GetNodeOrNull<Camera3D>("Camera");
        _animator = GetNodeOrNull<AnimationPlayer>("Animator");
        if (_camera == null)
        {
            GD.PushWarning($"{Name}: a cutscene needs a Camera3D child named \"Camera\"");
            Mission.Run(this, OnFinished);
            return;
        }
        Playing = this;
        NetLink.I?.MissionStep(this, NetLink.StepCutscene);
        _camera.MakeCurrent();
        _left = Length;
        if (_animator != null && _animator.HasAnimation(Animation))
        {
            _animator.Play(Animation);
            _left = (float)_animator.GetAnimation(Animation).Length;
        }
        Radio.Clear();
        float said = 0f;
        foreach (var line in Lines)
            if (line != null)
            {
                Radio.Say(line);
                said += line.Duration + line.Pause;
            }
        if (WaitForLines) _left = Mathf.Max(_left, said);
        StoryOverlay.Cinematic(true);
    }

    public override void _Process(double delta)
    {
        if (Playing != this) return;
        _left -= (float)delta;
        if (_left <= 0f) Finish();
    }

    /// <summary>A client of a network game: the host's has ended (skipped, or played out first).</summary>
    public void NetFinish() => Finish();

    public void Skip()
    {
        if (Playing != this || !Skippable) return;
        Radio.Clear();
        _animator?.Stop();
        Finish();
    }

    void Finish()
    {
        if (Playing != this) return;
        Playing = null;
        StoryOverlay.Cinematic(false);
        Game.Cockpit?.Camera?.MakeCurrent();
        // (A client of a network game only shows it: what comes after is the host's to set going.)
        if (Mission.Current is { Mirrored: true }) return;
        NetLink.I?.MissionStep(this, NetLink.StepCutsceneEnd);
        Mission.Run(this, OnFinished);
    }

    public override void _ExitTree()
    {
        if (Playing == this) Playing = null;
    }
}
