using Godot;

namespace MechSim;

/// <summary>Destroy machines of the mission: a group of them, or those named one by one.</summary>
public partial class DestroyObjective : Objective
{
    [Export] public string Group = "";
    [Export] public Godot.Collections.Array<NodePath> Units = new();

    int _down, _of;
    public override string Progress => _of > 1 ? $"{_down}/{_of}" : "";

    protected override void Check(float dt)
    {
        var list = Mission.Current.UnitsOf(this, Group, Units);
        _of = list.Count;
        _down = 0;
        foreach (var unit in list)
            if (unit.Destroyed) _down++;
        if (_of > 0 && _down == _of) Complete();
    }
}
