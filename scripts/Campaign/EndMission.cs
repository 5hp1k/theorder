using Godot;

namespace MechSim;

/// <summary>Ends the mission: won, or lost for the reason given.</summary>
public partial class EndMission : Node, IStep
{
    [Export] public bool Success = true;
    [Export] public string Reason = "";

    public void Run()
    {
        if (Success) Mission.Current?.Win();
        else Mission.Current?.Lose(Reason);
    }
}
