using System;
using Godot;

namespace MechSim;

/// <summary>BattleTech hit locations. Left/right are the mech's own left/right.</summary>
public enum Section
{
    Head,
    CenterTorso,
    LeftTorso,
    RightTorso,
    LeftArm,
    RightArm,
    LeftLeg,
    RightLeg,
}

/// <summary>
/// Per-section armour and internal structure with BattleTech rules:
/// armour absorbs first, then structure; excess damage transfers inward
/// (arm/leg → side torso → centre torso); losing a side torso takes its arm with it;
/// losing the head, the centre torso or both legs destroys the mech.
/// </summary>
public sealed class DamageModel
{
    public const int Count = 8;

    public static readonly string[] ShortNames = { "ГОЛ", "ЦТ", "ЛТ", "ПТ", "ЛР", "ПР", "ЛН", "ПН" };

    //                                          Head  CT   LT   RT   LA   RA   LL   RL
    static readonly float[] DefaultArmor =     { 18f, 46f, 32f, 32f, 24f, 24f, 32f, 32f };
    static readonly float[] DefaultStructure = {  9f, 31f, 21f, 21f, 14f, 14f, 21f, 21f };

    readonly float[] _maxArmor, _maxStructure, _armor, _structure;

    /// <summary>Raised for the section that actually absorbed damage (after transfer).</summary>
    public event Action<Section> Hit;
    /// <summary>The last of a section's armour is gone: its structure is exposed.</summary>
    public event Action<Section> ArmorLost;
    public event Action<Section> SectionLost;
    public event Action Killed;

    public bool Dead { get; private set; }

    public DamageModel(float scale = 1f)
    {
        _maxArmor = new float[Count];
        _maxStructure = new float[Count];
        _armor = new float[Count];
        _structure = new float[Count];
        for (int i = 0; i < Count; i++)
        {
            _maxArmor[i] = DefaultArmor[i] * scale;
            _maxStructure[i] = DefaultStructure[i] * scale;
        }
        Reset();
    }

    public void Reset()
    {
        Array.Copy(_maxArmor, _armor, Count);
        Array.Copy(_maxStructure, _structure, Count);
        Dead = false;
    }

    public bool IsDestroyed(Section s) => _structure[(int)s] <= 0f;
    public float ArmorFraction(Section s) => _armor[(int)s] / _maxArmor[(int)s];
    public float StructureFraction(Section s) => _structure[(int)s] / _maxStructure[(int)s];
    /// <summary>Armour + structure left in the section, 0..1.</summary>
    public float Fraction(Section s)
        => (_armor[(int)s] + _structure[(int)s]) / (_maxArmor[(int)s] + _maxStructure[(int)s]);

    /// <summary>Armour + structure left on the whole mech, 0..1.</summary>
    public float TotalFraction
    {
        get
        {
            float cur = 0f, max = 0f;
            for (int i = 0; i < Count; i++)
            {
                cur += _armor[i] + _structure[i];
                max += _maxArmor[i] + _maxStructure[i];
            }
            return cur / max;
        }
    }

    public void Apply(Section s, float amount)
    {
        if (Dead || amount <= 0f) return;
        // A section that's already gone passes the hit inward.
        while (IsDestroyed(s))
        {
            if (!TransfersTo(s, out s)) return;
        }

        int i = (int)s;
        Hit?.Invoke(s);

        float absorbed = Mathf.Min(_armor[i], amount);
        bool hadArmor = _armor[i] > 0f;
        _armor[i] -= absorbed;
        amount -= absorbed;
        if (hadArmor && _armor[i] <= 0f) ArmorLost?.Invoke(s);
        if (amount <= 0f) return;

        absorbed = Mathf.Min(_structure[i], amount);
        _structure[i] -= absorbed;
        amount -= absorbed;
        if (_structure[i] > 0f) return;

        Destroy(s);
        if (!Dead && amount > 0f && TransfersTo(s, out Section inner))
            Apply(inner, amount);
    }

    void Destroy(Section s)
    {
        _armor[(int)s] = 0f;
        _structure[(int)s] = 0f;
        SectionLost?.Invoke(s);

        if (s == Section.LeftTorso && !IsDestroyed(Section.LeftArm)) Destroy(Section.LeftArm);
        if (s == Section.RightTorso && !IsDestroyed(Section.RightArm)) Destroy(Section.RightArm);

        bool fatal = s == Section.Head || s == Section.CenterTorso
                     || (IsDestroyed(Section.LeftLeg) && IsDestroyed(Section.RightLeg));
        if (fatal && !Dead)
        {
            Dead = true;
            Killed?.Invoke();
        }
    }

    static bool TransfersTo(Section s, out Section inner)
    {
        switch (s)
        {
            case Section.LeftArm:
            case Section.LeftLeg:
                inner = Section.LeftTorso;
                return true;
            case Section.RightArm:
            case Section.RightLeg:
                inner = Section.RightTorso;
                return true;
            case Section.LeftTorso:
            case Section.RightTorso:
                inner = Section.CenterTorso;
                return true;
            default:
                inner = s;
                return false;
        }
    }
}
