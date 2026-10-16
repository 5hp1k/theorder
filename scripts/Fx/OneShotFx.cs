using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// A burst effect (explosion, impact): everything in the scene plays once, then the node frees itself.
/// An OmniLight3D child named "Flash" is faded out over FlashTime.
/// Effects that come by the hundred (muzzle flashes, smoke, sparks, dust) are better taken from
/// <see cref="Spawn"/>: a finished one is kept and played again instead of being freed and loaded anew.
/// </summary>
public partial class OneShotFx : Node3D
{
    [Export] public float Lifetime = 4f;
    [Export] public float FlashTime = 0.35f;

    OmniLight3D _flash;
    float _flashEnergy, _left;
    PackedScene _source;
    Tween _fade;

    public override void _Ready()
    {
        _flash = GetNodeOrNull<OmniLight3D>("Flash");
        if (_flash != null) _flashEnergy = _flash.LightEnergy;
        Begin();
    }

    void Begin()
    {
        _left = Lifetime;
        if (_flash != null)
        {
            _flash.LightEnergy = _flashEnergy;
            _fade?.Kill();
            _fade = CreateTween();
            _fade.TweenProperty(_flash, "light_energy", 0f, FlashTime);
        }
    }

    public override void _Process(double delta)
    {
        _left -= (float)delta;
        if (_left > 0f) return;
        if (_source == null)
        {
            QueueFree();
            return;
        }
        // Back on the shelf till it is wanted again (unless the shelf is full).
        var shelf = Shelf(_source);
        if (shelf.Count >= ShelfSize)
        {
            QueueFree();
            return;
        }
        Visible = false;
        SetProcess(false);
        shelf.Push(this);
    }

    /// <summary>Plays the same effect again from the start, wherever it has been put.</summary>
    void Replay()
    {
        Visible = true;
        SetProcess(true);
        foreach (Node child in GetChildren())
        {
            if (child is GpuParticles3D particles) particles.Restart();
            else if (child is AudioStreamPlayer3D { Autoplay: true } sound) sound.Play();
        }
        Begin();
    }

    // ── Pool ────────────────────────────────────────────────────

    static readonly Dictionary<PackedScene, Stack<OneShotFx>> Shelves = new();
    /// <summary>Most finished effects of one kind kept for later.</summary>
    const int ShelfSize = 48;

    static Stack<OneShotFx> Shelf(PackedScene scene)
    {
        if (!Shelves.TryGetValue(scene, out var shelf)) Shelves[scene] = shelf = new Stack<OneShotFx>();
        return shelf;
    }

    /// <summary>
    /// An effect of this scene playing under the parent at a point: a finished one played again if
    /// there is one, otherwise a new one. Turn or scale it after the call.
    /// With a size, an effect only for the eye (no sound of its own worth hearing) is left out — null —
    /// where nobody would see it: further than <see cref="GameConfig.FxRange"/> (more for big ones), or
    /// out of view and not close by.
    /// </summary>
    public static Node3D Spawn(PackedScene scene, Node parent, Vector3 at, float cullSize = 0f)
    {
        if (scene == null || parent == null || (cullSize > 0f && !Seen(at, cullSize))) return null;
        var shelf = Shelf(scene);
        while (shelf.Count > 0)
        {
            var old = shelf.Pop();
            // (Gone with the battle it was in.)
            if (!IsInstanceValid(old) || old.IsQueuedForDeletion()) continue;
            if (old.GetParent() != parent) old.Reparent(parent, false);
            old.Transform = Transform3D.Identity;
            old.GlobalPosition = at;
            old.Replay();
            old.ShowParticles(cullSize > 0f || Seen(at, 3f));
            return old;
        }
        var node = scene.Instantiate<Node3D>();
        if (node is OneShotFx fx) fx._source = scene;
        parent.AddChild(node);
        node.GlobalPosition = at;
        // One that is heard (an explosion) is always made, but its fire and smoke are only drawn where
        // they can be seen: half a screen of smoke behind the camera still costs the graphics card.
        if (node is OneShotFx made) made.ShowParticles(cullSize > 0f || Seen(at, 3f));
        return node;
    }

    void ShowParticles(bool on)
    {
        foreach (Node child in GetChildren())
            if (child is GpuParticles3D particles) particles.Visible = on;
    }

    /// <summary>Whether an effect of this size here is worth drawing at all.</summary>
    public static bool Seen(Vector3 at, float size = 1f)
        => Lod.Visible(at, Mathf.Max(size, 0.5f) * 4f, GameConfig.FxRange * Mathf.Sqrt(Mathf.Max(size, 0.25f)));

    /// <summary>Forget the shelves (a new battle: what was on them went with the old one).</summary>
    public static void Reset() => Shelves.Clear();
}
