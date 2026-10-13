using Godot;

namespace MechSim;

/// <summary>
/// A weapon in the hangar that can be picked up with the mouse (scenes/ui/weapon_slot_row.tscn): either
/// one on the shelf of everything available, or one already fitted in a hardpoint. Dropped on a
/// hardpoint it is fitted there; a fitted one dropped back on the shelf is taken off.
/// </summary>
public partial class WeaponChip : PanelContainer
{
    public string WeaponId = "";
    /// <summary>Hardpoint it sits in (and its place there), or -1 on the shelf.</summary>
    public int Point = -1, Index = -1;

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (WeaponId == "") return default;
        // What follows the mouse: the weapon's name on a plate.
        var plate = new PanelContainer { Modulate = new Color(1f, 1f, 1f, 0.85f) };
        plate.AddChild(new Label { Text = "  " + GetNode<Label>("Row/Name").Text + "  " });
        SetDragPreview(plate);
        return new Godot.Collections.Dictionary { ["weapon"] = WeaponId, ["point"] = Point, ["index"] = Index };
    }

    // A weapon dropped on top of another one lands in the hardpoint (or on the shelf) that one is in.
    HardpointDrop Home()
    {
        for (Node n = GetParent(); n != null; n = n.GetParent())
            if (n is HardpointDrop home) return home;
        return null;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data) => Home()?._CanDropData(atPosition, data) ?? false;

    public override void _DropData(Vector2 atPosition, Variant data) => Home()?._DropData(atPosition, data);
}
