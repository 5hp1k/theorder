using Godot;

namespace MechSim;

/// <summary>
/// How a machine is painted: a colour, optionally a pattern over it in two more colours, how worn and
/// dirty it is, how glossy, and a second, plain colour for some of its parts. (An enemy's paint may
/// instead be a ready-made camouflage material, <see cref="Camo"/>.) Made from the player's choices in the hangar
/// (<see cref="GameConfig.PlayerPaint"/>) or from the enemy's colours.
/// </summary>
public sealed class PaintJob
{
    public static readonly string[] SchemeNames = { "Весь в камуфляже", "Руки (башня)", "Ноги", "Руки и ноги", "Корпус" };

    public static readonly string[] PatternNames = { "Без узора", "Пятна", "Цифровой", "Тигровые полосы", "Осколочный", "Косые полосы", "Крап" };

    /// <param name="Pattern">Index into <see cref="PatternNames"/>.</param>
    public record Preset(string Name, Color Base, int Pattern, Color A, Color B);

    /// <summary>Ready-made colour schemes the hangar offers; choosing one just fills in the colours and the pattern.</summary>
    public static readonly Preset[] Presets =
    {
        new("Пустынный", new(0.74f, 0.62f, 0.42f), 1, new(0.5f, 0.4f, 0.26f), new(0.86f, 0.77f, 0.58f)),
        new("Лесной", new(0.25f, 0.32f, 0.16f), 1, new(0.13f, 0.17f, 0.1f), new(0.36f, 0.3f, 0.19f)),
        new("Зимний", new(0.86f, 0.88f, 0.9f), 1, new(0.58f, 0.62f, 0.66f), new(0.32f, 0.35f, 0.38f)),
        new("Городской", new(0.46f, 0.48f, 0.5f), 2, new(0.27f, 0.29f, 0.31f), new(0.66f, 0.67f, 0.68f)),
        new("Тигровый", new(0.55f, 0.45f, 0.25f), 3, new(0.12f, 0.1f, 0.07f), new(0.3f, 0.33f, 0.16f)),
        new("Осколочный", new(0.35f, 0.38f, 0.3f), 4, new(0.2f, 0.22f, 0.18f), new(0.55f, 0.5f, 0.38f)),
        new("Флектарн", new(0.3f, 0.34f, 0.2f), 6, new(0.12f, 0.14f, 0.09f), new(0.45f, 0.3f, 0.18f)),
        new("Предупреждающий", new(0.9f, 0.7f, 0.1f), 5, new(0.08f, 0.08f, 0.08f), new(0.08f, 0.08f, 0.08f)),
        new("Ржавый", new(0.45f, 0.25f, 0.15f), 1, new(0.3f, 0.15f, 0.1f), new(0.55f, 0.36f, 0.2f)),
        new("Серый", new(0.4f, 0.42f, 0.44f), 0, new(0.3f, 0.3f, 0.3f), new(0.2f, 0.2f, 0.2f)),
        new("Чёрный", new(0.05f, 0.05f, 0.055f), 0, new(0.1f, 0.1f, 0.1f), new(0.2f, 0.2f, 0.2f)),
        new("Белый", new(0.9f, 0.9f, 0.88f), 0, new(0.6f, 0.6f, 0.6f), new(0.4f, 0.4f, 0.4f)),
    };

    /// <summary>The plain painted armour everything else is made from.</summary>
    const string BasePaint = "res://materials/camo_desert.tres";

    /// <summary>A ready-made camouflage material (materials/camo_*.tres) to use as it is; null = paint by the colours below.</summary>
    public Material Camo;
    /// <summary>The colour of the paint.</summary>
    public Color Base = new(0.74f, 0.62f, 0.42f);
    /// <summary>Index into <see cref="PatternNames"/>, and the pattern's two colours.</summary>
    public int Pattern;
    public Color PatternA = new(0.5f, 0.4f, 0.26f), PatternB = new(0.86f, 0.77f, 0.58f);
    /// <summary>The second colour, for the parts the scheme picks out.</summary>
    public Color Second = new(0.33f, 0.36f, 0.2f);
    /// <summary>Index into <see cref="SchemeNames"/>: which parts are in the second colour.</summary>
    public int Scheme;
    /// <summary>0..1, or below zero to leave the material's own.</summary>
    public float Wear = -1f, Dirt = -1f, Gloss = -1f;
    /// <summary>Size of the pattern's shapes, as a multiple of the usual.</summary>
    public float PatternSize = 1f;
    /// <summary>Tint of the canopy glass; null = as the model has it.</summary>
    public Color? Glass;
    /// <summary>Emblem plates are scaled by this.</summary>
    public float InsigniaSize = 1f;

    enum Zone { Body, Arms, Legs }

    /// <summary>
    /// Repaints everything under the root that wears <paramref name="armor"/>; returns the material
    /// the armour wears now (debris and the like take their look from it).
    /// </summary>
    public Material Apply(Node root, Material armor)
    {
        if (armor == null) return armor;
        Material main, second;
        var source = Camo ?? GD.Load<Material>(BasePaint);
        if (source is ShaderMaterial shader)
        {
            var copy = (ShaderMaterial)shader.Duplicate();
            if (Camo == null)
            {
                // Paint by colour: no ready-made texture, the pattern (if any) is drawn by the shader.
                copy.SetShaderParameter("paint_tex", default(Variant));
                copy.SetShaderParameter("paint_color", Base);
                copy.SetShaderParameter("pattern", Pattern);
                copy.SetShaderParameter("pattern_a", PatternA);
                copy.SetShaderParameter("pattern_b", PatternB);
                copy.SetShaderParameter("pattern_size", Mathf.Max(PatternSize, 0.1f));
            }
            if (Wear >= 0f) copy.SetShaderParameter("wear", Wear);
            if (Dirt >= 0f) copy.SetShaderParameter("dirt", Dirt);
            if (Gloss >= 0f) copy.SetShaderParameter("paint_roughness", Mathf.Lerp(0.85f, 0.15f, Gloss));
            main = copy;
            // The second colour: the same paint, plain.
            var plain = (ShaderMaterial)copy.Duplicate();
            plain.SetShaderParameter("paint_tex", default(Variant));
            plain.SetShaderParameter("pattern", 0);
            plain.SetShaderParameter("paint_color", Second);
            second = plain;
        }
        else main = second = source;

        foreach (var mesh in Geo.Collect<MeshInstance3D>(root))
        {
            if (mesh.MaterialOverride == armor)
                mesh.MaterialOverride = InSecond(ZoneOf(root, mesh)) ? second : main;
            else if (Glass is { } glass && mesh.MaterialOverride is StandardMaterial3D pane && pane.ResourcePath.EndsWith("canopy.tres"))
            {
                var tinted = (StandardMaterial3D)pane.Duplicate();
                tinted.AlbedoColor = glass;
                mesh.MaterialOverride = tinted;
            }
            else if (mesh.Name.ToString().StartsWith("Insignia"))
                mesh.Scale = Vector3.One * InsigniaSize;
        }
        return main;
    }

    bool InSecond(Zone zone) => Scheme switch
    {
        1 => zone == Zone.Arms,
        2 => zone == Zone.Legs,
        3 => zone != Zone.Body,
        4 => zone == Zone.Body,
        _ => false,
    };

    /// <summary>Arms and legs are told by the names of the nodes a part hangs under (a vehicle's turret counts as arms).</summary>
    static Zone ZoneOf(Node root, Node part)
    {
        for (Node n = part; n != null && n != root; n = n.GetParent())
        {
            string name = n.Name;
            if (name.StartsWith("Leg") || name.StartsWith("Thigh") || name.StartsWith("Shin") || name.StartsWith("Foot")) return Zone.Legs;
            if (name.StartsWith("Arm") || name.StartsWith("UpperArm") || name.StartsWith("Turret")) return Zone.Arms;
        }
        return Zone.Body;
    }
}
