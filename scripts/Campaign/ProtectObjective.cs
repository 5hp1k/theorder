using Godot;

namespace MechSim;

/// <summary>Keep machines of the mission alive (a convoy, a lancemate, a base's guns) until the rest is done.</summary>
public partial class ProtectObjective : Objective
{
    [Export] public string Group = "";
    [Export] public Godot.Collections.Array<NodePath> Units = new();
    /// <summary>How many of them may be lost before it fails.</summary>
    [Export] public int MayLose;

    int _left, _of;
    public override bool Standing => true;
    public override string Progress => _of > 1 ? $"{_left}/{_of}" : "";

    protected override void Check(float dt)
    {
        var list = Mission.Current.UnitsOf(this, Group, Units);
        _of = list.Count;
        int lost = 0;
        foreach (var unit in list)
            if (unit.Destroyed) lost++;
        _left = _of - lost;
        if (lost > MayLose) Fail();
    }
}
