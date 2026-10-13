using System;
using Godot;

namespace MechSim;

/// <summary>
/// A place in the hangar a weapon can be dropped on: a hardpoint (scenes/ui/hardpoint_row.tscn) or the
/// shelf. The menu decides what it accepts and what dropping does.
/// </summary>
public partial class HardpointDrop : PanelContainer
{
    /// <summary>Weapon id, the hardpoint it comes from (-1 = the shelf) and its place there.</summary>
    public Func<string, int, int, bool> Accepts;
    public Action<string, int, int> Dropped;

    static bool Read(Variant data, out string weapon, out int point, out int index)
    {
        weapon = "";
        point = index = -1;
        if (data.VariantType != Variant.Type.Dictionary) return false;
        var d = data.AsGodotDictionary();
        if (!d.ContainsKey("weapon")) return false;
        weapon = (string)d["weapon"];
        point = (int)d["point"];
        index = (int)d["index"];
        return true;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
        => Read(data, out string weapon, out int point, out int index) && (Accepts?.Invoke(weapon, point, index) ?? false);

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (Read(data, out string weapon, out int point, out int index)) Dropped?.Invoke(weapon, point, index);
    }
}
