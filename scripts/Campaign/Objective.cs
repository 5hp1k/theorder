using Godot;

namespace MechSim;

/// <summary>
/// Something the player is told to do. Objectives active from the start are the briefing's; the rest
/// are given later, when a step names them ("new objective" on the HUD). An objective that is not
/// Required is a bonus: the mission is won without it. Steps can follow its success or its failure.
/// </summary>
public abstract partial class Objective : Node3D, IStep
{
    public enum Status { Waiting, Active, Done, Failed }

    [Export(PropertyHint.MultilineText)] public string Text = "";
    [Export] public bool Required = true;
    [Export] public bool StartActive = true;
    [Export] public Godot.Collections.Array<NodePath> OnDone = new();
    [Export] public Godot.Collections.Array<NodePath> OnFailed = new();

    public Status State { get; private set; }
    /// <summary>On a client of a network game: the progress as the host last told it.</summary>
    public string NetProgress = "";
    /// <summary>The progress to show: worked out here, or (a client) as the host has it.</summary>
    public string Shown => Mission.Current is { Mirrored: true } ? NetProgress : Progress;
    /// <summary>Seconds since it was given.</summary>
    protected float Age { get; private set; }

    /// <summary>Holds for as long as nothing goes wrong (protect): met when the rest of the mission is done.</summary>
    public virtual bool Standing => false;
    /// <summary>A place to go: the HUD marks it.</summary>
    public virtual bool Marked => false;
    /// <summary>How far along it is, shown after the text ("2/4", "1:30"); empty — nothing to show.</summary>
    public virtual string Progress => "";

    public void Run() => Activate();

    public void Activate(bool quiet = false)
    {
        if (State != Status.Waiting) return;
        State = Status.Active;
        if (!quiet) StoryOverlay.Announce("НОВАЯ ЦЕЛЬ", Text);
        NetLink.I?.ObjectiveChanged(this, !quiet);
    }

    public void Tick(float dt)
    {
        if (State != Status.Active) return;
        Age += dt;
        Check(dt);
    }

    protected abstract void Check(float dt);

    protected void Complete()
    {
        if (State != Status.Active) return;
        State = Status.Done;
        StoryOverlay.Announce("ЦЕЛЬ ВЫПОЛНЕНА", Text);
        NetLink.I?.ObjectiveChanged(this, true);
        Mission.Run(this, OnDone);
    }

    protected void Fail()
    {
        if (State != Status.Active) return;
        State = Status.Failed;
        StoryOverlay.Announce("ЦЕЛЬ ПРОВАЛЕНА", Text);
        NetLink.I?.ObjectiveChanged(this, true);
        Mission.Run(this, OnFailed);
    }

    /// <summary>The mission is won with this one still holding: it counts as done.</summary>
    public void Settle()
    {
        if (State != Status.Active) return;
        State = Status.Done;
        NetLink.I?.ObjectiveChanged(this, false);
    }

    /// <summary>A client of a network game: the objective is where the host says it is.</summary>
    public void NetSet(Status state, bool announce)
    {
        if (state == State) return;
        State = state;
        if (!announce) return;
        StoryOverlay.Announce(state switch { Status.Done => "ЦЕЛЬ ВЫПОЛНЕНА", Status.Failed => "ЦЕЛЬ ПРОВАЛЕНА", _ => "НОВАЯ ЦЕЛЬ" }, Text);
    }
}
