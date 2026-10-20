namespace MechSim;

/// <summary>
/// The developer console's cheats (see <see cref="DevConsole"/>): they last until switched off or the
/// game is closed, and touch only the player's own machine.
/// </summary>
public static class Cheats
{
    /// <summary>The player's machine takes no damage.</summary>
    public static bool God;
    /// <summary>The player's weapons (and anti-missile gun) never run dry.</summary>
    public static bool InfiniteAmmo;
    /// <summary>The player's mech never heats up.</summary>
    public static bool NoHeat;
    /// <summary>Time is stopped (the scene tree paused; the free camera and the console go on).</summary>
    public static bool TimeStopped;

    static bool Mine(object unit) => unit != null && Game.PlayerUnit != null && ReferenceEquals(unit, Game.PlayerUnit);

    public static bool Spares(object unit) => God && Mine(unit);
    public static bool Refills(object unit) => InfiniteAmmo && Mine(unit);
    public static bool Cools(object unit) => NoHeat && Mine(unit);
}
