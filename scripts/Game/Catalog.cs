namespace MechSim;

/// <summary>
/// What the menus offer: mechs, vehicles, maps, weapons, paint schemes. To add one, make its scene or
/// material and list it here.
/// </summary>
public static class Catalog
{
    public enum Kind { Mech, Infantry, Ground, Water, Air }

    /// <param name="Kind">What sort of unit it is (decides how and where it is spawned).</param>
    public record Entry(string Id, string Name, string Path, string Description = "", Kind Kind = Kind.Mech);

    public static readonly Entry[] Mechs =
    {
        new("ostrich", "Страус", "res://scenes/mech/ostrich.tscn",
            "Мех, средний, 50 т. Сбалансированный: 50 км/ч, два лазера и автопушка в руках."),
        new("hound", "Гончая", "res://scenes/mech/hound.tscn",
            "Мех, лёгкий, 30 т. Очень быстрый (80 км/ч) и вёрткий, с прыжковыми двигателями, но броня тонкая."),
        new("ballista", "Баллиста", "res://scenes/mech/ballista.tscn",
            "Мех, тяжёлый, 65 т. Ракетная платформа: две пусковые установки с наведением и два лазера в корпусе. Медленный, толстая броня."),
        new("cleaver", "Секач", "res://scenes/mech/cleaver.tscn",
            "Мех, средний, 45 т. Рукопашник: топор в правой руке (группа 3), автопушка и пулемёт в левой, лазер в груди, прыжковые двигатели."),
    };

    public static readonly Entry[] Vehicles =
    {
        new("tank", "Танк", "res://scenes/vehicles/tank.tscn",
            "Гусеничный, башня крутится на 360°. Тяжёлая пушка и пулемёт. Крепкий, но в воде вязнет.", Kind.Ground),
        new("apc", "БТР", "res://scenes/vehicles/apc.tscn",
            "Колёсный бронетранспортёр: быстрый, с автопушкой и пулемётом, броня слабая.", Kind.Ground),
        new("amphibian", "Танк-амфибия", "res://scenes/vehicles/amphibian.tscn",
            "Лёгкий танк, который плавает: реки и море ему не преграда.", Kind.Ground),
        new("ship", "Сторожевой корабль", "res://scenes/vehicles/ship.tscn",
            "Ходит только по воде (лучше всего на «Побережье»): орудийная башня, ракеты и пулемёт.", Kind.Water),
        new("helicopter", "Вертолёт", "res://scenes/vehicles/helicopter.tscn",
            "Пробел — вверх, Shift — вниз. Подвижный пулемёт и ракеты.", Kind.Air),
        new("bomber", "Бомбардировщик", "res://scenes/vehicles/bomber.tscn",
            "Летит всегда: W/S — тяга, A/D — разворот, мышь вверх-вниз — набор и снижение. Курсовая пушка и бомбы (группа 2).", Kind.Air),
    };

    public const string InfantryId = "infantry";
    /// <summary>What the player can start as: any mech or vehicle, or on foot.</summary>
    public static readonly Entry[] PlayerUnits = Join(Mechs,
        new[]
        {
            new Entry(InfantryId, "Пехота + силовая броня", "res://scenes/infantry/power_armor.tscn",
                "Вы на своих двоих с винтовкой; рядом стоит силовая броня: малый лазер, пара ракет ближнего боя, пулемёт и прыжковый ранец. Подойдите и нажмите F.", Kind.Infantry),
        }, Vehicles);

    public static readonly Entry[] Maps =
    {
        new("desert", "Пустыня", "res://scenes/maps/desert.tscn", "Дюны, кактусы, перекати-поле, песчаная буря, река и озёра."),
        new("winter", "Зима", "res://scenes/maps/winter.tscn", "Снег, ели, валуны, метель, замёрзшая река."),
        new("city", "Город", "res://scenes/maps/city.tscn", "Тесные кварталы, небоскрёбы, улицы, машины. Всё рушится, если пройти насквозь."),
        new("coast", "Побережье", "res://scenes/maps/coast.tscn", "Зелёный лес, река и море с нефтяными вышками. Здесь есть где развернуться кораблям и амфибиям."),
    };

    public static readonly Entry[] Weapons =
    {
        new("medium_laser", "Средний лазер", "res://scenes/weapons/medium_laser.tscn"),
        new("large_laser", "Большой лазер", "res://scenes/weapons/large_laser.tscn"),
        new("autocannon", "Автопушка", "res://scenes/weapons/autocannon.tscn"),
        new("lrm", "Ракеты (10)", "res://scenes/weapons/lrm.tscn"),
        new("machine_gun", "Пулемёт", "res://scenes/weapons/machine_gun.tscn"),
        new("hatchet", "Топор", "res://scenes/weapons/hatchet.tscn"),
    };

    public static readonly Entry[] Camos =
    {
        new("desert", "Пустынный", "res://materials/camo_desert.tres"),
        new("winter", "Зимний", "res://materials/camo_winter.tres"),
        new("forest", "Лесной", "res://materials/camo_forest.tres"),
        new("urban", "Городской", "res://materials/camo_urban.tres"),
        new("rust", "Ржавый", "res://materials/camo_rust.tres"),
    };

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

    /// <summary>Catalog id of a weapon scene path, or null.</summary>
    public static string WeaponIdByPath(string path)
    {
        foreach (var e in Weapons)
            if (e.Path == path) return e.Id;
        return null;
    }
}
