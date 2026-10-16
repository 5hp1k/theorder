using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// The hangar's card of the chosen machine: a row for each of its figures — speed, armour, cooling,
/// hardpoints and so on — with a bar showing how it stands against the rest of the catalogue. The figures
/// are read from the machine's own scene, so they are always what the game uses. Rows are made from the
/// RowScene prefab (scenes/ui/stat_row.tscn: Caption, Bar, Value).
/// </summary>
public partial class UnitStats : VBoxContainer
{
    [Export] public PackedScene RowScene;

    /// <summary>One line of the card. Share: where the bar stands (0..1), or negative for no bar.</summary>
    public record Stat(string Name, string Value, float Share = -1f);

    static readonly Dictionary<string, List<Stat>> Known = new();
    static float _topSpeed, _topArmor, _topCooling, _topHull;

    /// <summary>Shows the figures of a catalogue entry.</summary>
    public void Show(Catalog.Entry entry)
    {
        foreach (Node child in GetChildren()) child.QueueFree();
        foreach (var stat in Of(entry))
        {
            var row = RowScene.Instantiate<Control>();
            AddChild(row);
            row.GetNode<Label>("Caption").Text = Loc.T(stat.Name);
            row.GetNode<Label>("Value").Text = Loc.T(stat.Value);
            var bar = row.GetNode<ProgressBar>("Bar");
            bar.Visible = stat.Share >= 0f;
            bar.Value = Mathf.Clamp(stat.Share, 0f, 1f) * 100.0;
        }
    }

    static readonly string[] SlotNames = { "лучевых", "баллистических", "ракетных", "универсальных", "ручных", "бомбовых" };

    /// <summary>The figures of a machine (worked out once, from its scene).</summary>
    public static List<Stat> Of(Catalog.Entry entry)
    {
        if (Known.TryGetValue(entry.Id, out var known)) return known;
        if (_topSpeed <= 0f) Survey();
        var stats = new List<Stat>();
        var node = GD.Load<PackedScene>(entry.Path)?.Instantiate();
        if (node is Mech mech)
        {
            string cls = Mech.ClassNames[Mech.WeightClass(mech.Tonnage)];
            stats.Add(new Stat("Класс", $"{cls}, {mech.Tonnage} т"));
            float kmh = mech.MaxForwardSpeed * 3.6f;
            stats.Add(new Stat("Скорость", $"{Mathf.RoundToInt(kmh)} км/ч", kmh / _topSpeed));
            float armor = DamageModel.Total(mech.ArmorScale);
            stats.Add(new Stat("Броня", $"{Mathf.RoundToInt(armor)}", armor / _topArmor));
            stats.Add(new Stat("Теплоотвод", $"{mech.HeatDissipation * 100f:0.#} %/с", mech.HeatDissipation / _topCooling));
            stats.Add(new Stat("Прыжковые двигатели", mech.HasJumpJets ? "есть" : "нет"));
            stats.Add(new Stat("Противоракетная система", Ams.Of(mech) != null ? "есть" : "нет"));
        }
        else if (node is Vehicle vehicle)
        {
            stats.Add(new Stat("Тип", VehicleType(vehicle)));
            float kmh = vehicle.MaxForwardSpeed * 3.6f;
            stats.Add(new Stat("Скорость", $"{Mathf.RoundToInt(kmh)} км/ч", kmh / _topSpeed));
            stats.Add(new Stat("Прочность корпуса", $"{Mathf.RoundToInt(vehicle.MaxHp)}", vehicle.MaxHp / _topHull));
            if (vehicle.GetNodeOrNull("Turret") != null)
                stats.Add(new Stat("Башня", vehicle.MaxTwist >= Mathf.Pi - 0.01f ? "круговая" : $"±{Mathf.RoundToInt(Mathf.RadToDeg(vehicle.MaxTwist))}°"));
        }
        if (node != null)
        {
            var counts = new int[SlotNames.Length];
            int total = 0;
            foreach (var hp in Geo.Collect<Hardpoint>(node))
            {
                counts[(int)hp.Type]++;
                total++;
            }
            if (total > 0)
            {
                var parts = new List<string>();
                for (int i = 0; i < counts.Length; i++)
                    if (counts[i] > 0) parts.Add($"{counts[i]} {SlotNames[i]}");
                stats.Add(new Stat("Точки подвески", string.Join(", ", parts)));
            }
            node.Free();
        }
        Known[entry.Id] = stats;
        return stats;
    }

    static string VehicleType(Vehicle v) => v.Kind switch
    {
        Vehicle.MoveKind.Helicopter => "Вертолёт",
        Vehicle.MoveKind.Plane => "Самолёт",
        Vehicle.MoveKind.Ship => "Корабль",
        Vehicle.MoveKind.Amphibious => "Плавающая, гусеничная",
        _ => v.GetNodeOrNull("Hull/TrackL") != null ? "Гусеничная" : "Колёсная",
    };

    /// <summary>The best figures in the catalogue, for the bars to be measured against.</summary>
    static void Survey()
    {
        _topSpeed = _topArmor = _topHull = 1f;
        _topCooling = 0.01f;
        foreach (var e in Catalog.Mechs)
            if (GD.Load<PackedScene>(e.Path)?.Instantiate() is Mech m)
            {
                _topSpeed = Mathf.Max(_topSpeed, m.MaxForwardSpeed * 3.6f);
                _topArmor = Mathf.Max(_topArmor, DamageModel.Total(m.ArmorScale));
                _topCooling = Mathf.Max(_topCooling, m.HeatDissipation);
                m.Free();
            }
        foreach (var e in Catalog.Vehicles)
            if (GD.Load<PackedScene>(e.Path)?.Instantiate() is Vehicle v)
            {
                // (Aircraft are not measured against ground machines: their speed would flatten every other bar.)
                if (!v.IsAircraft) _topSpeed = Mathf.Max(_topSpeed, v.MaxForwardSpeed * 3.6f);
                _topHull = Mathf.Max(_topHull, v.MaxHp);
                v.Free();
            }
    }
}
