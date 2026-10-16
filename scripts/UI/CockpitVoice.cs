using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// The cockpit computer's voice, as in MechWarrior 2: it watches the player's machine and speaks up when
/// something happens to it — reactor on, heat, shutdown, a missile coming, a limb or a weapon lost, the
/// ammunition gone, a pulse, leaving the battlefield, a target down. One line at a time; the most urgent of
/// those waiting goes next, and a line that has waited too long is dropped as no longer news. The lines are
/// its <see cref="VoiceLine"/> children (volume, how often, how urgent); the recordings come from the voice
/// chosen in the settings (<see cref="GameConfig.Voice"/>, one of <see cref="Voices"/>), or it keeps quiet.
/// Heard with the interface off too.
/// </summary>
public partial class CockpitVoice : Node
{
    /// <summary>The voices it can speak with (audio/voice/voices.tres).</summary>
    [Export] public VoiceCatalog Voices;
    /// <summary>Seconds a line may wait for its turn before it is dropped.</summary>
    [Export] public float Stale = 3f;
    /// <summary>Share of heat at which it warns, and how far heat must fall before it will again.</summary>
    [Export(PropertyHint.Range, "0,1")] public float HeatWarning = 0.85f;
    [Export(PropertyHint.Range, "0,1")] public float HeatClear = 0.65f;
    /// <summary>Share of the machine left at which damage is critical.</summary>
    [Export(PropertyHint.Range, "0,1")] public float CriticalDamage = 0.3f;
    /// <summary>A waiting line this much more urgent cuts off the one being said.</summary>
    [Export] public int Interrupt = 3;
    /// <summary>Seconds after the machine comes to life before the start-up line.</summary>
    [Export] public float StartupDelay = 0.8f;

    static CockpitVoice _instance;

    readonly Dictionary<string, VoiceLine> _lines = new();
    readonly List<(VoiceLine line, float at)> _waiting = new();
    VoiceLine _speaking;
    float _clock;
    string _voice;

    // What the machine was like a moment ago, to tell when something changes.
    ulong _unit;
    bool _alive, _hot, _shutdown, _jammed, _outside, _critical, _incoming, _settling;
    float _startup = -1f;
    readonly bool[] _sections = new bool[DamageModel.Count];
    readonly List<bool> _weaponLost = new(), _ammoOut = new();

    /// <summary>The line for each section, when it is lost (head and centre torso end the machine anyway).</summary>
    static readonly Dictionary<Section, string> SectionLines = new()
    {
        [Section.LeftArm] = "LeftArm",
        [Section.RightArm] = "RightArm",
        [Section.LeftLeg] = "LeftLeg",
        [Section.RightLeg] = "RightLeg",
        [Section.LeftTorso] = "LeftTorso",
        [Section.RightTorso] = "RightTorso",
    };

    public override void _Ready()
    {
        _instance = this;
        foreach (Node child in GetChildren())
            if (child is VoiceLine line) _lines[line.Name] = line;
    }

    public override void _ExitTree()
    {
        if (_instance == this) _instance = null;
    }

    /// <summary>Has the computer say a line (a child's name), unless it said it just now.</summary>
    public static void Say(string name) => _instance?.Queue(name);

    void Queue(string name)
    {
        if (_settling || !_lines.TryGetValue(name, out var line) || line.Stream == null || _clock - line.SaidAt < line.Repeat) return;
        foreach (var (waiting, _) in _waiting)
            if (waiting == line) return;
        _waiting.Add((line, _clock));
    }

    public override void _Process(double delta)
    {
        _clock += (float)delta;
        if (_voice != GameConfig.Voice) Speak(GameConfig.Voice);
        Watch();

        _waiting.RemoveAll(w => _clock - w.at > Stale);
        if (_waiting.Count == 0) return;
        int next = 0;
        for (int i = 1; i < _waiting.Count; i++)
            if (_waiting[i].line.Priority > _waiting[next].line.Priority) next = i;
        if (_speaking != null && _speaking.Playing)
        {
            if (_waiting[next].line.Priority < _speaking.Priority + Interrupt) return;
            _speaking.Stop();
        }
        _speaking = _waiting[next].line;
        _waiting.RemoveAt(next);
        _speaking.SaidAt = _clock;
        _speaking.Play();
    }

    /// <summary>Puts the voice's recordings on the lines; a line it has none of stays silent.</summary>
    void Speak(string voice)
    {
        _voice = voice;
        _waiting.Clear();
        _speaking?.Stop();
        _speaking = null;
        var pack = Voices?.Find(voice);
        foreach (var (name, line) in _lines)
            line.Stream = pack != null && pack.Lines.TryGetValue(name, out var stream) ? stream : null;
    }

    void Watch()
    {
        // Only a machine has a computer: not a soldier on foot.
        if (Game.PlayerUnit is not IDrivable unit || unit is not GodotObject thing || !IsInstanceValid(thing))
        {
            _unit = 0;
            return;
        }
        bool fresh = thing.GetInstanceId() != _unit;
        _unit = thing.GetInstanceId();
        bool alive = !unit.IsDestroyed;
        // A new machine, or the old one back in the fight: take it as it is now, without a word about it.
        _settling = fresh || alive && !_alive;
        if (_settling) Restart(alive);
        _alive = alive;
        if (!alive)
        {
            _waiting.Clear();
            return;
        }
        if (_startup >= 0f && _clock >= _startup)
        {
            _startup = -1f;
            Say("Startup");
        }

        // Heat, and the reactor shutting down and coming back.
        if (unit.HasHeat)
        {
            if (!_hot && unit.Heat >= HeatWarning && !unit.IsShutdown) Say("HeatCritical");
            _hot = unit.Heat >= (_hot ? HeatClear : HeatWarning);
            if (unit.IsShutdown && !_shutdown)
            {
                // Too late to warn of it.
                _waiting.RemoveAll(w => w.line.Name == "HeatCritical");
                Say("Shutdown");
            }
            else if (!unit.IsShutdown && _shutdown) Say("Startup");
            _shutdown = unit.IsShutdown;
        }

        // A missile locked on: only when the first one of a salvo appears.
        bool incoming = false;
        foreach (var m in Missile.Flying)
            if (IsInstanceValid(m) && m.Interceptable && m.Target == unit)
            {
                incoming = true;
                break;
            }
        if (incoming && !_incoming) Say("Incoming");
        _incoming = incoming;

        // The pulse knocking the electronics out, and them coming back.
        bool jammed = Emp.Jammed(unit);
        if (jammed && !_jammed) Say("Emp");
        else if (!jammed && _jammed) Say("SystemsOnline");
        _jammed = jammed;

        bool outside = Boundary.TimeLeft(unit) >= 0f;
        if (outside && !_outside) Say("OutOfBounds");
        _outside = outside;

        // Damage: limbs and side torsos lost, the machine nearly gone. A repair brings them back.
        if (unit.Damage is { } damage)
            for (int i = 0; i < DamageModel.Count; i++)
            {
                bool lost = damage.IsDestroyed((Section)i);
                if (lost && !_sections[i] && SectionLines.TryGetValue((Section)i, out string line)) Say(line);
                _sections[i] = lost;
            }
        bool critical = unit.Integrity < CriticalDamage;
        if (critical && !_critical) Say("DamageCritical");
        _critical = critical;

        // Weapons shot off, and run dry.
        var weapons = unit.Weapons?.Weapons;
        if (weapons != null) Arsenal(weapons);
        _settling = false;
    }

    void Arsenal(List<Weapon> weapons)
    {
        while (_weaponLost.Count < weapons.Count)
        {
            _weaponLost.Add(false);
            _ammoOut.Add(false);
        }
        for (int i = 0; i < weapons.Count; i++)
        {
            var w = weapons[i];
            if (w.Destroyed && !_weaponLost[i]) Say("WeaponLost");
            bool dry = w.MaxAmmo > 0 && !w.HasAmmo;
            if (dry && !_ammoOut[i] && !w.Destroyed) Say("AmmoOut");
            _weaponLost[i] = w.Destroyed;
            _ammoOut[i] = dry;
        }
    }

    /// <summary>Forgets the machine it had, and says hello shortly.</summary>
    void Restart(bool alive)
    {
        _waiting.Clear();
        _hot = _shutdown = _jammed = _outside = _critical = _incoming = false;
        System.Array.Fill(_sections, false);
        _weaponLost.Clear();
        _ammoOut.Clear();
        _startup = alive ? _clock + StartupDelay : -1f;
    }
}
