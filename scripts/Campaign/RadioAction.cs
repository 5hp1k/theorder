using Godot;

namespace MechSim;

/// <summary>Lines said over the radio, one after another (subtitles; the recordings where there are any).</summary>
public partial class RadioAction : Node, IStep
{
    [Export] public RadioLine[] Lines = System.Array.Empty<RadioLine>();

    public void Run()
    {
        NetLink.I?.MissionStep(this, NetLink.StepRadio);
        Say();
    }

    /// <summary>The lines themselves (on a client of a network game, when the host's are said).</summary>
    public void Say()
    {
        foreach (var line in Lines)
            if (line != null) Radio.Say(line);
    }
}
