using Godot;

namespace MechSim;

/// <summary>
/// The canopy glass of a mech's cockpit, seen from inside (the "Glass" node under its Cockpit; the look is
/// materials/cockpit_glass.gdshader). Hits on the cab (the Head section) crack it, a star for every so much
/// of the cab's armour lost; it picks up a little dirt as the machine walks and when a blast throws earth
/// up near it; and it takes on what the weather (<see cref="Weather.Current"/>) brings: rain drops running
/// down, snow banking up from the bottom, wind-blown dust. Rain slowly washes dirt and dust off and melts
/// the snow. A repair puts in clean new glass. Only the player's own machine keeps count. With
/// <see cref="GameConfig.CockpitGlassFx"/> off the glass is drawn clean (it still keeps count, so
/// switching it back on shows what is on it by then).
/// </summary>
public partial class CockpitGlass : MeshInstance3D
{
    [ExportGroup("Cracks")]
    /// <summary>Share of the cab's armour and structure lost for each new crack.</summary>
    [Export(PropertyHint.Range, "0.01,1")] public float CrackEvery = 0.1f;
    /// <summary>How far a crack reaches over the glass, metres: smallest and largest.</summary>
    [Export] public Vector2 CrackReach = new(0.18f, 0.42f);
    /// <summary>How much likelier a crack is on the windscreen than on the side and roof panes.</summary>
    [Export] public float FrontWeight = 3f;

    [ExportGroup("Dirt")]
    /// <summary>Dirt gathered per second standing, and per second at full speed.</summary>
    [Export] public float Settle = 0.0006f;
    [Export] public float Kick = 0.004f;
    /// <summary>Dirt from a blast right next to the cab (less further off; none beyond its reach).</summary>
    [Export] public float Splash = 0.25f;
    /// <summary>The most dirt the glass takes on: it never gets more than a little dirty.</summary>
    [Export(PropertyHint.Range, "0,1")] public float MaxDirt = 0.45f;

    [ExportGroup("Weather")]
    /// <summary>Seconds for the glass to get wet in rain, and to dry off after.</summary>
    [Export] public float WetTime = 6f;
    [Export] public float DryTime = 40f;
    /// <summary>Seconds for snow to bank up to its full height, and to melt away when it stops.</summary>
    [Export] public float SnowTime = 120f;
    [Export] public float MeltTime = 90f;
    /// <summary>Seconds for a dust storm to film the glass over fully.</summary>
    [Export] public float DustTime = 150f;
    /// <summary>Share of dirt and dust a full rain washes off per second.</summary>
    [Export] public float Wash = 0.01f;

    const int MaxCracks = 12;

    Mech _mech;
    bool _hooked;
    ShaderMaterial _look;
    readonly Vector4[] _cracks = new Vector4[MaxCracks];
    int _crackCount;
    float _dirt, _dust, _wet, _snow;
    Vector3[] _faces;
    float[] _faceWeights;

    public override void _Ready()
    {
        for (Node n = GetParent(); n != null; n = n.GetParent())
            if (n is Mech mech) { _mech = mech; break; }
        if (_mech == null) return;
        // (The mech makes its damage model in its own _Ready, which comes after this one.)
        Callable.From(() =>
        {
            if (!IsInstanceValid(_mech) || _mech.Damage == null) return;
            _mech.Damage.Hit += OnHit;
            Grime.Splashed += OnSplash;
            _hooked = true;
        }).CallDeferred();
    }

    public override void _ExitTree()
    {
        if (!_hooked) return;
        _hooked = false;
        _mech.Damage.Hit -= OnHit;
        Grime.Splashed -= OnSplash;
    }

    bool Mine => _mech != null && Game.PlayerUnit == _mech;

    /// <summary>New glass, clean (a repair).</summary>
    public void Clean()
    {
        _crackCount = 0;
        _dirt = _dust = _snow = 0f;
        Show();
    }

    void OnHit(Section section)
    {
        if (section != Section.Head || !Mine) return;
        int want = Mathf.Min(MaxCracks, Mathf.FloorToInt((1f - _mech.Damage.Fraction(Section.Head)) / Mathf.Max(CrackEvery, 0.01f)));
        while (_crackCount < want)
        {
            Vector3 at = SpotOnGlass();
            _cracks[_crackCount++] = new Vector4(at.X, at.Y, at.Z, (float)GD.RandRange(CrackReach.X, CrackReach.Y));
        }
        Show();
    }

    void OnSplash(Vector3 at, float radius)
    {
        if (!Mine) return;
        float reach = radius * 3f + 4f;
        float d = GlobalPosition.DistanceTo(at);
        if (d < reach) _dirt = Mathf.Min(_dirt + Splash * (1f - d / reach), MaxDirt);
    }

    public override void _Process(double delta)
    {
        if (!Mine) return;
        float dt = (float)delta;
        var weather = Weather.Current;
        float rain = weather?.Rain ?? 0f, snow = weather?.Snow ?? 0f, dust = weather?.Dust ?? 0f;
        float speed = Mathf.Clamp(Mathf.Abs(_mech.Speed) / Mathf.Max(_mech.MaxForwardSpeed, 0.1f), 0f, 1f);

        _wet = rain > _wet ? Mathf.MoveToward(_wet, rain, dt / WetTime) : Mathf.MoveToward(_wet, rain, dt / DryTime);
        _snow = snow > 0f ? Mathf.MoveToward(_snow, snow, dt / SnowTime) : Mathf.MoveToward(_snow, 0f, dt / MeltTime);
        if (rain > 0f) _snow = Mathf.MoveToward(_snow, 0f, rain * dt / MeltTime);
        _dust = Mathf.MoveToward(_dust, Mathf.Max(dust, _dust), dt / DustTime);
        _dirt = Mathf.Min(_dirt + dt * (Settle + Kick * speed), Mathf.Max(MaxDirt, _dirt));
        float wash = 1f - Wash * _wet * dt;
        _dirt *= wash;
        _dust *= wash;
        Show(speed);
    }

    void Show(float speed = 0f)
    {
        if (_look == null)
        {
            // (Its own copy: only the player's glass is ever drawn with all this on it.)
            if (MaterialOverride is not ShaderMaterial shared) return;
            _look = (ShaderMaterial)shared.Duplicate();
            MaterialOverride = _look;
        }
        bool on = GameConfig.CockpitGlassFx;
        _look.SetShaderParameter("cracks", _cracks);
        _look.SetShaderParameter("crack_count", on ? _crackCount : 0);
        _look.SetShaderParameter("dirt", on ? _dirt : 0f);
        _look.SetShaderParameter("dust", on ? _dust : 0f);
        _look.SetShaderParameter("rain", on ? _wet : 0f);
        _look.SetShaderParameter("snow", on ? _snow : 0f);
        _look.SetShaderParameter("run_speed", 1f + speed * 1.5f);
    }

    /// <summary>A point on the glass picked at random, by area: the windscreen likelier than the rest.</summary>
    Vector3 SpotOnGlass()
    {
        if (_faces == null)
        {
            _faces = Mesh?.GetFaces() ?? System.Array.Empty<Vector3>();
            _faceWeights = new float[_faces.Length / 3];
            for (int i = 0; i < _faceWeights.Length; i++)
            {
                Vector3 a = _faces[i * 3], b = _faces[i * 3 + 1], c = _faces[i * 3 + 2];
                Vector3 normal = (b - a).Cross(c - a);
                // The windscreen faces ahead (-z) as much as up; the side panes sideways, the roof up.
                bool front = Mathf.Abs(normal.Z) > Mathf.Abs(normal.X) && Mathf.Abs(normal.Z) > Mathf.Abs(normal.Y) * 0.5f;
                _faceWeights[i] = normal.Length() * 0.5f * (front ? FrontWeight : 1f);
            }
        }
        if (_faceWeights.Length == 0) return Vector3.Zero;
        float total = 0f;
        foreach (float w in _faceWeights) total += w;
        float pick = GD.Randf() * total;
        int f = 0;
        while (f < _faceWeights.Length - 1 && (pick -= _faceWeights[f]) > 0f) f++;
        float u = GD.Randf(), v = GD.Randf();
        if (u + v > 1f) (u, v) = (1f - u, 1f - v);
        // (Kept a little in from the edges, where the frame would hide it.)
        Vector3 p0 = _faces[f * 3], p1 = _faces[f * 3 + 1], p2 = _faces[f * 3 + 2];
        Vector3 middle = (p0 + p1 + p2) / 3f;
        return middle.Lerp(p0 + (p1 - p0) * u + (p2 - p0) * v, 0.85f);
    }
}
