namespace MechSim;

/// <summary>
/// What the menus offer: mechs, vehicles, maps, weapons (paint schemes are in <see cref="PaintJob"/>). To add one, make its scene or
/// material and list it here.
/// </summary>
public static class Catalog
{
    public enum Kind { Mech, Infantry, Ground, Water, Air }

    /// <param name="Kind">What sort of unit it is (decides how and where it is spawned).</param>
    public record Entry(string Id, string Name, string Path, Kind Kind = Kind.Mech);

    public static readonly Entry[] Mechs =
    {
        new("ostrich", "Страус", "res://scenes/mech/ostrich.tscn"),
        new("hound", "Гончая", "res://scenes/mech/hound.tscn"),
        new("ballista", "Баллиста", "res://scenes/mech/ballista.tscn"),
        new("panther", "Пантера", "res://scenes/mech/panther.tscn"),
        new("hades", "Аид", "res://scenes/mech/hades.tscn"),
        new("gladiator", "Гладиатор", "res://scenes/mech/gladiator.tscn"),
        new("cleaver", "Секач", "res://scenes/mech/cleaver.tscn"),
        new("agni", "Агни", "res://scenes/mech/agni.tscn"),
        new("executioner", "Палач", "res://scenes/mech/executioner.tscn"),
    };

    public static readonly Entry[] Vehicles =
    {
        new("tank", "Танк", "res://scenes/vehicles/tank.tscn", Kind: Kind.Ground),
        new("apc", "БТР", "res://scenes/vehicles/apc.tscn", Kind: Kind.Ground),
        new("amphibian", "Танк-амфибия", "res://scenes/vehicles/amphibian.tscn", Kind: Kind.Ground),
        new("spg", "Самоходная гаубица", "res://scenes/vehicles/spg.tscn", Kind: Kind.Ground),
        new("mlrs", "Реактивная установка", "res://scenes/vehicles/mlrs.tscn", Kind: Kind.Ground),
        new("ship", "Сторожевой корабль", "res://scenes/vehicles/ship.tscn", Kind: Kind.Water),
        new("helicopter", "Вертолёт", "res://scenes/vehicles/helicopter.tscn", Kind: Kind.Air),
        new("bomber", "Бомбардировщик", "res://scenes/vehicles/bomber.tscn", Kind: Kind.Air),
    };

    public const string InfantryId = "infantry";
    /// <summary>
    /// Whether the menu offers the foot soldier with his suit of power armour. Switched off for now: the
    /// scenes and the code are all still there, this only takes the entry out of the list.
    /// </summary>
    public const bool InfantryEnabled = false;
    /// <summary>What the player can start as: any mech or vehicle (or on foot, see above).</summary>
    public static readonly Entry[] PlayerUnits = Join(Mechs,
        !InfantryEnabled ? System.Array.Empty<Entry>() : new[]
        {
            new Entry(InfantryId, "Пехота + силовая броня", "res://scenes/infantry/power_armor.tscn", Kind.Infantry),
        }, Vehicles);

    public static readonly Entry[] Maps =
    {
        new("desert", "Пустыня", "res://scenes/maps/desert.tscn"),
        new("winter", "Зима", "res://scenes/maps/winter.tscn"),
        new("city", "Город", "res://scenes/maps/city.tscn"),
        new("coast", "Побережье", "res://scenes/maps/coast.tscn"),
    };

    public static readonly Entry[] Weapons =
    {
        new("small_laser", "Малый лазер", "res://scenes/weapons/small_laser.tscn"),
        new("medium_laser", "Средний лазер", "res://scenes/weapons/medium_laser.tscn"),
        new("large_laser", "Большой лазер", "res://scenes/weapons/large_laser.tscn"),
        new("flamethrower", "Огнемёт", "res://scenes/weapons/flamethrower.tscn"),
        new("ppc", "Протонная пушка (ППЦ)", "res://scenes/weapons/ppc.tscn"),
        new("machine_gun", "Пулемёт", "res://scenes/weapons/machine_gun.tscn"),
        new("autocannon", "Автопушка", "res://scenes/weapons/autocannon.tscn"),
        new("heavy_cannon", "Тяжёлая пушка", "res://scenes/weapons/heavy_cannon.tscn"),
        new("gauss", "Гаусс-пушка", "res://scenes/weapons/gauss.tscn"),
        new("howitzer", "Гаубица", "res://scenes/weapons/howitzer.tscn"),
        new("srm", "Ракеты ближнего боя", "res://scenes/weapons/srm.tscn"),
        new("lrm", "Ракеты дальнего боя (10)", "res://scenes/weapons/lrm.tscn"),
        new("rocket_battery", "Реактивная батарея (12)", "res://scenes/weapons/rocket_battery.tscn"),
        new("fist", "Штурмовой кулак", "res://scenes/weapons/fist.tscn"),
        new("mace", "Булава", "res://scenes/weapons/mace.tscn"),
        new("hatchet", "Топор", "res://scenes/weapons/hatchet.tscn"),
        new("bomb_rack", "Бомбы", "res://scenes/weapons/bomb_rack.tscn"),
        new("emp_rack", "ЭМИ-бомбы", "res://scenes/weapons/emp_rack.tscn"),
        new("nuke_rack", "Тактическая ядерная бомба", "res://scenes/weapons/nuke_rack.tscn"),
    };

    /// <summary>The hangar's first list: mechs, machines on the ground (and on the water), aircraft.</summary>
    public enum Group { Mechs, Ground, Air }
    public static readonly string[] GroupNames = { "Мехи", "Наземная техника", "Воздушная техника" };
    public static Group GroupOf(Entry e) => e.Kind switch { Kind.Mech => Group.Mechs, Kind.Air => Group.Air, _ => Group.Ground };

    static readonly System.Collections.Generic.Dictionary<string, int> Tonnes = new();

    /// <summary>A mech's mass in tonnes, read from its scene without building the machine.</summary>
    public static int Tonnage(Entry e)
    {
        if (Tonnes.TryGetValue(e.Id, out int t)) return t;
        t = Mech.DefaultTonnage;
        var state = Godot.GD.Load<Godot.PackedScene>(e.Path)?.GetState();
        for (int i = 0; state != null && i < state.GetNodePropertyCount(0); i++)
            if (state.GetNodePropertyName(0, i) == "Tonnage") t = (int)state.GetNodePropertyValue(0, i);
        return Tonnes[e.Id] = t;
    }

    static Entry[] Join(params Entry[][] lists)
    {
        var all = new System.Collections.Generic.List<Entry>();
        foreach (var list in lists) all.AddRange(list);
        return all.ToArray();
    }

    public static Entry Find(Entry[] list, string id)
    {
        foreach (var e in list)
            if (e.Id == id) return e;
        return list[0];
    }

    public static int IndexOf(Entry[] list, string id)
    {
        for (int i = 0; i < list.Length; i++)
            if (list[i].Id == id) return i;
        return 0;
    }

    /// <summary>Catalog id of a mech's or vehicle's scene.</summary>
    public static string IdByPath(string path)
    {
        foreach (var e in PlayerUnits)
            if (e.Path == path) return e.Id;
        return Mechs[0].Id;
    }

    /// <summary>Catalog id of a weapon scene path, or null.</summary>
    public static string WeaponIdByPath(string path)
    {
        foreach (var e in Weapons)
            if (e.Path == path) return e.Id;
        return null;
    }
}
