using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// The first explosion, the first muzzle flash, the first spark of a battle each used to stall the
/// game for a moment while the engine put together the shaders of their particles; the first hit on a
/// machine's part did the same while the part's shape was worked out for the mark. All of that is done
/// here once, while the battle loads: a copy of every effect (without its scripts or sounds) is shown
/// out of sight for a few frames, and the parts of the machines on the field are measured.
/// </summary>
public partial class Warmup : Node3D
{
    /// <summary>Effects that are not needed on every battle, or that do things of their own when made.</summary>
    static readonly string[] Skip = { "nuke", "weather", "snowfall", "sand_wind", "shell_arc", "collapse_rumble", "headlight" };
    const string Folder = "res://scenes/fx";

    int _frames = 4;

    public static void Run(Node parent)
    {
        var warm = new Warmup { Name = "Warmup", Position = new Vector3(0f, -2000f, 0f) };
        parent.AddChild(warm);
        foreach (string file in DirAccess.GetFilesAt(Folder))
        {
            // (In an exported game the scenes are listed under another name.)
            string name = file.TrimSuffix(".remap");
            if (!name.EndsWith(".tscn") || System.Array.Exists(Skip, s => name.Contains(s))) continue;
            if (GD.Load<PackedScene>($"{Folder}/{name}") is not { } scene) continue;
            var original = scene.Instantiate();
            // Without its scripts nothing of it runs; the sounds are taken out.
            var copy = original.Duplicate((int)DuplicateFlags.Groups);
            original.Free();
            foreach (var sound in Geo.Collect<AudioStreamPlayer3D>(copy)) sound.Free();
            warm.AddChild(copy);
        }
        foreach (var unit in Units.All)
            if (unit is Node node) Impacts.Prepare(node);
    }

    public override void _Process(double delta)
    {
        if (--_frames <= 0) QueueFree();
    }
}
