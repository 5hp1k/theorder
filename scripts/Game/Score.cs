using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// Who hit whom, and the tally that comes of it: kills and losses of every machine in the battle (the
/// Tab screen shows them). Whoever deals damage names itself in <see cref="Attacker"/> while it does;
/// whatever takes damage reports it with <see cref="Hit"/>, and its destruction with <see cref="Killed"/>.
/// </summary>
public static class Score
{
    public class Line
    {
        public int Kills, Losses;
        /// <summary>The side the machine fought on (kept here: the machine itself may be gone by the end).</summary>
        public Team Team;
    }

    /// <summary>Set for the moment damage is being dealt.</summary>
    public static ITarget Attacker;
    /// <summary>The player has just hit one of their own side.</summary>
    public static event System.Action FriendlyFire;

    static readonly Dictionary<ITarget, Line> Lines = new();
    static readonly Dictionary<ITarget, ITarget> LastHitBy = new();

    public static void Reset()
    {
        Lines.Clear();
        LastHitBy.Clear();
        Attacker = null;
    }

    public static Line Of(ITarget unit)
    {
        if (!Lines.TryGetValue(unit, out Line line)) Lines[unit] = line = new Line { Team = unit.Team };
        return line;
    }

    /// <summary>Everything destroyed by the machines of one side.</summary>
    public static int KillsOf(Team team)
    {
        int kills = 0;
        foreach (Line line in Lines.Values)
            if (line.Team == team) kills += line.Kills;
        return kills;
    }

    /// <summary>The tally at the end of a battle: what the player's side destroyed, the player, each lancemate.</summary>
    public static string Summary()
    {
        var me = Game.PlayerUnit;
        var text = new System.Text.StringBuilder();
        text.Append($"Уничтожено командой: {KillsOf(me?.Team ?? Team.Player)}");
        if (me != null) text.Append($"\nВами: {Of(me).Kills}");
        if (Game.Instance is { } game)
            foreach (AiPilot pilot in game.Squad)
                if (GodotObject.IsInstanceValid(pilot) && pilot.Unit is { } mate)
                    text.Append($"\n{mate.DisplayName}: {Of(mate).Kills}");
        return text.ToString();
    }

    public static void Hit(ITarget victim)
    {
        if (Attacker == null || victim == null || Attacker == victim) return;
        LastHitBy[victim] = Attacker;
        if (Attacker == Game.PlayerUnit && victim.Team == Attacker.Team) FriendlyFire?.Invoke();
    }

    public static void Killed(ITarget victim)
    {
        Of(victim).Losses++;
        if (LastHitBy.Remove(victim, out ITarget killer) && killer is Node node && GodotObject.IsInstanceValid(node) && Units.Hostile(killer, victim))
            Of(killer).Kills++;
    }
}
