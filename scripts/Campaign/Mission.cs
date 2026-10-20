using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// Root of a campaign mission's scene (campaign/missions/*.tscn). Its child "Map" is the map the
/// mission is played on (an instance of one of scenes/maps/*.tscn); a "PlayerSpawn" marker of its own,
/// if it has one, is where the player starts instead of the map's. Everything else in it is the
/// mission: machines (<see cref="MissionUnit"/>), objectives (<see cref="Objective"/>), triggers
/// (<see cref="Trigger"/>), radio lines (<see cref="RadioAction"/>), cutscenes (<see cref="Cutscene"/>),
/// endings (<see cref="EndMission"/>), wired to one another by lists of node paths ("steps").
/// The mission is won when every required objective is done (and none failed), lost when one fails or
/// the player's machine is destroyed.
/// </summary>
public partial class Mission : Node3D
{
    public static Mission Current { get; private set; }

    /// <summary>Steps run when the mission is won (an outro cutscene, a last word over the radio).</summary>
    [Export] public Godot.Collections.Array<NodePath> OnWon = new();
    /// <summary>Seconds between the end and the debriefing (cutscenes started on the end are waited for).</summary>
    [Export] public float EndDelay = 3f;

    public enum Outcome { Playing, Won, Lost }
    public Outcome State { get; private set; }
    /// <summary>Why it was lost.</summary>
    public string Reason { get; private set; } = "";
    public bool Begun { get; private set; }
    /// <summary>A client of a network game: the host plays the mission, this one only shows what the host says.</summary>
    public bool Mirrored { get; private set; }
    public float Clock { get; private set; }

    public readonly List<Objective> Objectives = new();
    readonly List<MissionUnit> _units = new();
    readonly List<Trigger> _triggers = new();
    float _endWait;

    public Node3D Map => GetNodeOrNull<Node3D>("Map");

    public override void _EnterTree() => Current = this;

    public override void _ExitTree()
    {
        if (Current == this) Current = null;
    }

    /// <summary>The player and the lance are on the field: the mission starts.</summary>
    public void Begin()
    {
        Collect(this);
        Begun = true;
        foreach (var unit in _units)
            if (unit.SpawnAtStart) unit.Spawn();
        foreach (var objective in Objectives)
            if (objective.StartActive) objective.Activate(quiet: true);
        foreach (var trigger in _triggers)
            if (trigger.StartArmed) trigger.Arm();
    }

    /// <summary>A client of a network game: the objectives are known (to be shown), nothing runs here.</summary>
    public void Mirror()
    {
        Collect(this);
        Begun = true;
        Mirrored = true;
    }

    /// <summary>A client: the host says it is over.</summary>
    public void NetEnd(bool won, string reason)
    {
        State = won ? Outcome.Won : Outcome.Lost;
        Reason = reason ?? "";
        if (Game.Instance is { MissionOver: false } game) game.EndMission(won);
    }

    void Collect(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child == Map) continue;
            if (child is MissionUnit unit) _units.Add(unit);
            if (child is Objective objective) Objectives.Add(objective);
            if (child is Trigger trigger) _triggers.Add(trigger);
            Collect(child);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Begun || Mirrored) return;
        float dt = (float)delta;
        if (State != Outcome.Playing)
        {
            // The debriefing once the ending's cutscene, if any, is over.
            if (Cutscene.Playing != null) return;
            _endWait -= dt;
            if (_endWait <= 0f && Game.Instance is { } game && !game.MissionOver) game.EndMission(State == Outcome.Won);
            return;
        }
        // A cutscene that holds the battle holds the mission's clocks too (its timers, a "hold out" objective).
        if (Cutscene.Holding) return;
        Clock += dt;
        foreach (var trigger in _triggers) trigger.Tick(dt);
        foreach (var objective in Objectives) objective.Tick(dt);
        if (State != Outcome.Playing) return;

        if (Game.AllPlayersDown())
        {
            Lose(Net.Online ? "Все машины игроков уничтожены" : "Ваша машина уничтожена");
            return;
        }
        bool all = true, any = false;
        foreach (var objective in Objectives)
        {
            if (!objective.Required) continue;
            any = true;
            if (objective.State == Objective.Status.Failed)
            {
                Lose("Не выполнено: " + objective.Text);
                return;
            }
            if (objective.State != Objective.Status.Done && !(objective.Standing && objective.State == Objective.Status.Active)) all = false;
        }
        if (any && all) Win();
    }

    public void Win()
    {
        if (State != Outcome.Playing) return;
        State = Outcome.Won;
        foreach (var objective in Objectives)
            if (objective.Standing && objective.State == Objective.Status.Active) objective.Settle();
        _endWait = EndDelay;
        Run(this, OnWon);
    }

    public void Lose(string reason)
    {
        if (State != Outcome.Playing) return;
        State = Outcome.Lost;
        Reason = reason ?? "";
        _endWait = EndDelay;
    }

    /// <summary>The machines named by a group and a list of paths (relative to <paramref name="from"/>).</summary>
    public List<MissionUnit> UnitsOf(Node from, string group, Godot.Collections.Array<NodePath> paths)
    {
        var list = new List<MissionUnit>();
        if (!string.IsNullOrEmpty(group))
            foreach (var unit in _units)
                if (unit.Group == group) list.Add(unit);
        if (paths != null)
            foreach (var path in paths)
                if (from.GetNodeOrNull(path) is MissionUnit unit && !list.Contains(unit)) list.Add(unit);
        return list;
    }

    /// <summary>Sets going each step named in the list (paths relative to <paramref name="from"/>).</summary>
    public static void Run(Node from, Godot.Collections.Array<NodePath> steps)
    {
        if (steps == null) return;
        foreach (var path in steps)
        {
            if (from.GetNodeOrNull(path) is IStep step) step.Run();
            else GD.PushWarning($"{from.Name}: \"{path}\" is not a mission step");
        }
    }
}
