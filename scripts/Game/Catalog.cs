using System.Linq;

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
        new("agni", "Агни", "res://scenes/mech/agni.tscn"),
        new("executioner", "Палач", "res://scenes/mech/executioner.tscn"),
        new("mauler", "Громила", "res://scenes/mech/mauler.tscn"),
        new("direwolf", "Лютоволк", "res://scenes/mech/direwolf.tscn"),
        new("mantis", "Мантис", "res://scenes/mech/mantis.tscn"),
    };

    public static readonly Entry[] Vehicles =
    {
        new("tank", "Танк", "res://scenes/vehicles/tank.tscn", Kind: Kind.Ground),
        new("laser_tank", "Лазерный танк", "res://scenes/vehicles/laser_tank.tscn", Kind: Kind.Ground),
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
        new("forest", "Лес", "res://scenes/maps/forest.tscn"),
        new("base", "Военная база", "res://scenes/maps/base.tscn"),
        new("arena", "Арена", "res://scenes/maps/arena.tscn"),
    };

    /// <summary>What the mission editor can put on a map, in its groups (Id is the scene's file name).</summary>
    public static readonly (string Group, Entry[] Items)[] Props =
    {
        ("Укрытия", Scenes("base", ("jersey_barrier", "Бетонный блок"), ("sandbags", "Мешки с песком"), ("base_wall", "Стена базы"), ("barrier_wall", "Бронированный бруствер"), ("concrete_block", "Бетонный пилон"),
            ("bunker", "Бетонный бункер"), ("container_green", "Контейнер (зелёный)"), ("container_blue", "Контейнер (синий)"), ("container_red", "Контейнер (красный)"))),
        ("Постройки", Scenes("base", ("barracks", "Казарма (здание)"), ("hangar", "Ангар"), ("hq", "Штаб"), ("warehouse", "Складское здание"),
            ("control_tower", "Диспетчерская вышка"), ("watchtower", "Вышка охраны"), ("radar", "Радарная станция"), ("fuel_tank", "Топливный бак"))),
        ("Город", Scenes("city", ("house_small_a", "Дом малый А"), ("house_small_b", "Дом малый Б"), ("house_mid_a", "Дом средний А"),
            ("house_mid_b", "Дом средний Б"), ("office", "Офисное здание"), ("tower_a", "Башня А"), ("tower_b", "Башня Б"), ("bus_stop", "Автобусная остановка"),
            ("street_lamp", "Уличный фонарь"), ("traffic_light", "Светофор"))),
        ("Техника", Scenes("base", ("army_truck", "Армейский грузовик")).Concat(Scenes("city", ("car_sedan_black", "Легковой седан"), ("car_suv_silver", "Внедорожник"),
            ("car_pickup_blue", "Пикап (синий)"), ("car_van_white", "Фургон (белый)"), ("car_bus", "Городской автобус"), ("car_police", "Полицейская машина"), ("car_ambulance", "Машина скорой помощи"), ("car_fire", "Пожарная машина"))).ToArray()),
        ("Природа", Scenes("props", ("rock_large", "Скала большая"), ("rock_medium", "Скала средняя"), ("rock_small", "Скала малая"),
            ("stone_large", "Камень большой"), ("stone_medium", "Камень средний"), ("stone_small", "Камень малый"), ("tree_oak", "Дерево: дуб"),
            ("tree_birch", "Дерево: берёза"), ("pine_tall", "Дерево: сосна"), ("fir_tall_green", "Дерево: ель"), ("palm_tall", "Дерево: пальма"), ("cactus_saguaro", "Кактус-сагуаро"))),
        ("Арена", Scenes("arena", ("pylon", "Пилон-обелиск"), ("rampart", "Крепостная стена"), ("bastion", "Бастион"), ("ruins", "Руины колоннады"))),
        ("Прочее", Scenes("props", ("oil_rig", "Нефтекачалка"), ("repair_bay", "Ремонтная площадка"))),
    };

    static Entry[] Scenes(string folder, params (string id, string name)[] items)
    {
        var list = new Entry[items.Length];
        for (int i = 0; i < items.Length; i++) list[i] = new Entry(items[i].id, items[i].name, $"res://scenes/{folder}/{items[i].id}.tscn");
        return list;
    }

    /// <summary>A prop by its id; null if there is none.</summary>
    public static Entry Prop(string id)
    {
        foreach (var (_, items) in Props)
            foreach (var e in items)
                if (e.Id == id) return e;
        return null;
    }

    public static readonly Entry[] Weapons =
    {
        new("small_laser", "Малый лазер", "res://scenes/weapons/small_laser.tscn"),
        new("medium_laser", "Средний лазер", "res://scenes/weapons/medium_laser.tscn"),
        new("large_laser", "Большой лазер", "res://scenes/weapons/large_laser.tscn"),
        new("small_pulse_laser", "Малый пульс-лазер", "res://scenes/weapons/small_pulse_laser.tscn"),
        new("medium_pulse_laser", "Средний пульс-лазер", "res://scenes/weapons/medium_pulse_laser.tscn"),
        new("large_pulse_laser", "Большой пульс-лазер", "res://scenes/weapons/large_pulse_laser.tscn"),
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

    /// <summary>Machines taken out of the game, and what stands in for them where an old save, mission or
    /// setting still asks for them.</summary>
    static readonly System.Collections.Generic.Dictionary<string, string> Retired = new() { ["cleaver"] = "mauler", ["gladiator"] = "direwolf" };

    public static Entry Find(Entry[] list, string id)
    {
        if (id != null && Retired.TryGetValue(id, out string now)) id = now;
        foreach (var e in list)
            if (e.Id == id) return e;
        return list[0];
    }

    public static int IndexOf(Entry[] list, string id)
    {
        if (id != null && Retired.TryGetValue(id, out string now)) id = now;
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
