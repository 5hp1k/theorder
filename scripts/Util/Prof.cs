using System.Diagnostics;
using Godot;

namespace MechSim;

/// <summary>Temporary timing of the per-tick work, printed every few seconds when THEORDER_PROF is set.</summary>
public static class Prof
{
    public static readonly bool On = !string.IsNullOrEmpty(OS.GetEnvironment("THEORDER_PROF"));
    static readonly double[] Sum = new double[10];
    static readonly int[] Count = new int[10];
    static readonly string[] Names = { "mech", "vehicle", "missile", "terrain", "legs/tracks", "weapons", "moveslide", "aim", "think", "hud" };
    static ulong _lastFrame;
    static int _frames;

    public static long Now() => On ? Stopwatch.GetTimestamp() : 0;

    public static void Add(int slot, long since)
    {
        if (!On) return;
        Sum[slot] += (Stopwatch.GetTimestamp() - since) * 1000.0 / Stopwatch.Frequency;
        Count[slot]++;
        ulong frame = Engine.GetPhysicsFrames();
        if (frame == _lastFrame) return;
        _lastFrame = frame;
        if (++_frames % 400 != 0) return;
        string line = "PROF per physics tick, ms:";
        for (int i = 0; i < Sum.Length; i++)
        {
            if (Count[i] > 0) line += $" {Names[i]}={Sum[i] / 400:0.00}(x{Count[i] / 400})";
            Sum[i] = 0;
            Count[i] = 0;
        }
        GD.Print(line);
    }
}

