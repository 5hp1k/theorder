using Godot;

namespace MechSim;

/// <summary>When machines of the mission are destroyed: all of a group or list, or a number of them.</summary>
public partial class DestroyedTrigger : Trigger
{
    [Export] public string Group = "";
    [Export] public Godot.Collections.Array<NodePath> Units = new();
    /// <summary>How many must be down; 0 — all of them.</summary>
    [Export] public int Count;

    protected override bool Test()
    {
        var list = Mission.Current.UnitsOf(this, Group, Units);
        int down = 0;
        foreach (var unit in list)
            if (unit.Destroyed) down++;
        return list.Count > 0 && down >= (Count > 0 ? Count : list.Count);
    }
}
