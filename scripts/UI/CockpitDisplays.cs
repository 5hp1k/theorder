using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// The instruments of a cockpit. Sits on the "Cockpit" node of a mech (the interior shown in first-person
/// view) and shows everything the HUD shows on the cab's own displays, so that from the cockpit the battle
/// is fought with nothing drawn over the picture (the HUD's default <see cref="Hud.Mode.Cockpit"/>):
///   * three screens on its "Dash": damage and heat on the left, the radar (and the score, while it is
///     called up) in the middle, weapons and throttle on the right; along the bottom of each, the names
///     of the soft keys under it (<see cref="SoftKeys"/>);
///   * a screen on each side console's pod ("PodL", "PodR"): the target on the left, the objectives, the
///     clock and the squad on the right;
///   * the head-up display, projected on the windscreen ("HeadUp", a sheet just inside it): the sight,
///     the compass and the torso, the warnings and the messages. The picture is collimated — laid out
///     over the whole view and seen only where the glass is — so the sight still lies over what the guns
///     aim at, wherever the pilot looks.
/// All of it runs off the reactor: dark (the battery's red lamp on) while it is down, flickering back as
/// it starts, torn and snowy once the cab has taken a beating.
/// The displays show the very instruments of the HUD — copies of them, drawn into small viewports — so
/// whatever the HUD can show, the cockpit shows. The cab's moving and clickable controls are worked by
/// <see cref="CockpitControls"/>, which this puts in.
/// </summary>
public partial class CockpitDisplays : Node3D
{
    /// <summary>True while some cockpit is showing its screens.</summary>
    public static bool Active => Engine.GetProcessFrames() - _shownFrame < 3;

    // Every mech has one of these, only the player's is ever in view: the last frame any of them was.
    static ulong _shownFrame = ulong.MaxValue / 2;

    [ExportGroup("Dash screens")]
    /// <summary>Side of a screen, metres.</summary>
    [Export] public float ScreenSize = 0.46f;
    /// <summary>Distance between the middles of neighbouring screens.</summary>
    [Export] public float ScreenPitch = 0.56f;
    /// <summary>Pixels along a side of a screen.</summary>
    [Export] public int Resolution = 360;
    /// <summary>How brightly the screens glow.</summary>
    [Export] public float Glow = 1.3f;
    /// <summary>Times a second the screens are redrawn (their instruments change no faster, see HudWidget.RedrawRate).</summary>
    [Export] public float RefreshRate = 30f;

    [ExportGroup("Console pods")]
    /// <summary>Size of a pod's screen, metres, and its pixels.</summary>
    [Export] public Vector2 PodSize = new(0.3f, 0.2f);
    [Export] public Vector2I PodResolution = new(480, 320);

    [ExportGroup("Compass strip")]
    /// <summary>The compass's screen over the combiner (on the cab's "CompassMount" node): how big, and its pixels.</summary>
    [Export] public Vector2 CompassSize = new(0.29f, 0.057f);
    [Export] public Vector2I CompassResolution = new(500, 100);

    [ExportGroup("Head-up display")]
    /// <summary>How far in front of the eye the picture is laid when the cab has no combiner glass of its own, metres.</summary>
    [Export] public float HudDistance = 0.4f;
    /// <summary>Its pixels as a share of the window's.</summary>
    [Export(PropertyHint.Range, "0.3,1")] public float HudScale = 1f;
    /// <summary>How brightly it glows, and how much of it shows (it is light on glass).</summary>
    [Export] public float HudGlow = 1.6f;
    [Export(PropertyHint.Range, "0,1")] public float HudOpacity = 0.85f;

    [ExportGroup("Cab light")]
    [Export] public Color LightColor = new(1f, 0.9f, 0.78f);
    [Export] public float LightEnergy = 1.1f;
    /// <summary>The red lamp on the battery that lights the cab while the reactor is down.</summary>
    [Export] public float EmergencyEnergy = 0.35f;

    [ExportGroup("Interference")]
    /// <summary>Share of the cab's (the head's) health below which the screens start to go bad, and how bad they get when it is gone.</summary>
    [Export(PropertyHint.Range, "0,1")] public float InterferenceFrom = 0.6f;
    [Export(PropertyHint.Range, "0,1")] public float InterferenceMax = 0.85f;
    /// <summary>How much a hit jolts the picture: on the cab itself, anywhere else.</summary>
    [Export] public float HitJolt = 0.9f;
    [Export] public float NearHitJolt = 0.3f;

    // The HUD's instruments on each place (names of the HUD's children, with where they go in shares of
    // the screen).
    // (the panel's screens keep their bottom strip for the soft keys' labels, see SoftKeyStrip)
    static readonly (string, Rect2)[] LeftDash = { ("PlayerDoll", new Rect2(0.03f, 0.04f, 0.56f, 0.78f)), ("HeatGauge", new Rect2(0.6f, 0.04f, 0.22f, 0.78f)), ("CoolantGauge", new Rect2(0.84f, 0.04f, 0.13f, 0.78f)) };
    static readonly (string, Rect2)[] MiddleDash = { ("Minimap", new Rect2(0.09f, 0.02f, 0.82f, 0.82f)), ("Scoreboard", new Rect2(0f, 0f, 1f, 0.85f)) };
    static readonly (string, Rect2)[] RightDash = { ("WeaponList", new Rect2(0.03f, 0.03f, 0.94f, 0.41f)), ("ThrottleGauge", new Rect2(0.08f, 0.46f, 0.5f, 0.38f)), ("JumpGauge", new Rect2(0.7f, 0.46f, 0.2f, 0.38f)) };
    /// <summary>Share of a panel screen's height along its bottom given to the labels of the keys under it.</summary>
    const float SoftKeyStrip = 0.14f;
    // (clear of the strip of key names along the bottom: the panels draw to the edge of their box)
    static readonly (string, Rect2)[] LeftPod = { ("TargetPanel", new Rect2(0.04f, 0.03f, 0.92f, 0.7f)) };
    static readonly (string, Rect2)[] RightPod = { ("ObjectiveList", new Rect2(0.02f, 0.0f, 0.96f, 0.42f)), ("TimerLabel", new Rect2(0.62f, 0.02f, 0.36f, 0.1f)), ("SquadCommand", new Rect2(0.02f, 0.4f, 0.96f, 0.38f)) };
    // On the combiner: the sight over the whole view; the rest is laid inside the glass's outline on screen.
    static readonly string[] HeadUp = { "Reticle", "StatusText", "MissileWarning", "NukeWarning", "BoundsWarning" };
    // The compass with the torso's bearing, on its own strip of a screen on top of the panel.
    static readonly (string, Rect2)[] CompassStrip = { ("TorsoIndicator", new Rect2(0.02f, 0.02f, 0.96f, 0.96f)) };

    /// <summary>True while the player's cockpit is projecting its head-up display.</summary>
    public static bool HeadUpOn { get; private set; }

    bool _built;
    float _refreshWait;
    readonly List<SubViewport> _views = new();
    // Labels the HUD fills in itself (the clock), and their copies.
    readonly List<(Label from, Label to)> _labels = new();
    SubViewport _hudView;
    Control _hudLayout;
    MeshInstance3D _combiner;
    // A combiner glass of the cab's own (else a sheet laid over the whole view in front of the eye).
    bool _glass;
    readonly Dictionary<string, (Control copy, Vector2 size)> _headUp = new();
    Rect2 _glassRect;
    readonly List<ShaderMaterial> _screens = new();
    ShaderMaterial _hudMaterial;
    Mech _mech;
    OmniLight3D _cabLight, _screenGlow, _emergency;
    float _power = 1f, _stripPower = 1f, _jolt;

    public override void _Process(double delta)
    {
        // Only the player's own cockpit is ever shown; nothing is built for the rest.
        if (!IsVisibleInTree())
        {
            if (_combiner != null && _combiner.Visible)
            {
                _combiner.Visible = false;
                HeadUpOn = false;
            }
            return;
        }
        _shownFrame = Engine.GetProcessFrames();
        if (!_built) Build();
        UpdateHeadUp();
        UpdatePower((float)delta);
        _refreshWait -= (float)delta;
        if (_refreshWait > 0f) return;
        _refreshWait = Mathf.Max(_refreshWait + 1f / Mathf.Max(RefreshRate, 1f), 0f);
        foreach (var view in _views) view.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        foreach (var (from, to) in _labels)
        {
            to.Text = from.Text;
            // (the clock only in a battle against time)
            to.Visible = from.Name != "TimerLabel" || from.Visible;
        }
    }

    void Build()
    {
        _built = true;
        Control hud = Hud.Instance;
        if (hud == null) return;

        if (GetNodeOrNull<Node3D>("Dash") is { } dash)
            for (int column = -1; column <= 1; column++)
            {
                var screen = Screen(hud, new Vector2I(Resolution, Resolution), column < 0 ? LeftDash : column > 0 ? RightDash : MiddleDash);
                Place(screen, dash, "Screen" + (column < 0 ? "L" : column > 0 ? "R" : "C"), new Transform3D(Basis.Identity, new Vector3(column * ScreenPitch, 0.02f, 0.012f)), Vector2.One * ScreenSize);
                SoftKeysOn(screen, "Screen" + (column < 0 ? "L" : column > 0 ? "R" : "C"), new Vector2I(Resolution, Resolution));
            }
        foreach (var (name, instruments) in new[] { ("PodL", LeftPod), ("PodR", RightPod), ("CompassMount", CompassStrip) })
        {
            if (GetNodeOrNull<Node3D>(name) is not { } pod) continue;
            Vector3 n = pod.HasMeta("face") ? ((Vector3)pod.GetMeta("face")).Normalized() : Vector3.Back;
            Vector3 u = (Vector3.Up - n * n.Dot(Vector3.Up)).Normalized();
            var face = new Transform3D(new Basis(u.Cross(n), u, n), n * 0.004f);
            bool compass = name == "CompassMount";
            var podView = Screen(hud, compass ? CompassResolution : PodResolution, instruments);
            Place(podView, pod, "Screen", face, compass ? CompassSize : PodSize);
            if (!compass) SoftKeysOn(podView, name, PodResolution);
        }
        BuildHeadUp(hud);

        // A lamp in the roof: the cab is shut in armour, its screens alone would not light it.
        AddChild(_cabLight = new OmniLight3D
        {
            Name = "CabLight",
            Position = new Vector3(0f, 0.7f, -0.25f),
            LightColor = LightColor,
            LightEnergy = LightEnergy,
            OmniRange = 3.2f,
            OmniAttenuation = 0.9f,
            ShadowEnabled = false,
            LightSpecular = 0.3f,
        });
        // …and the screens' own glow on the panel and the consoles.
        AddChild(_screenGlow = new OmniLight3D
        {
            Name = "ScreenGlow",
            Position = new Vector3(0f, -0.3f, -0.7f),
            LightColor = new Color(0.55f, 1f, 0.65f),
            LightEnergy = 0.3f,
            OmniRange = 1.4f,
            ShadowEnabled = false,
            LightSpecular = 0.1f,
        });
        // …and the battery's red lamp, for when the reactor is down and all the rest is dark.
        AddChild(_emergency = new OmniLight3D
        {
            Name = "EmergencyLight",
            Position = new Vector3(0f, 0.75f, 0.2f),
            LightColor = new Color(1f, 0.12f, 0.06f),
            LightEnergy = 0f,
            OmniRange = 2.8f,
            ShadowEnabled = false,
            LightSpecular = 0.2f,
            Visible = false,
        });
        AddChild(new CockpitControls { Name = "Controls" });
        for (Node n = GetParent(); n != null && _mech == null; n = n.GetParent()) _mech = n as Mech;
        if (_mech?.Damage != null) _mech.Damage.Hit += OnHit;
    }

    public override void _ExitTree()
    {
        if (_mech?.Damage != null) _mech.Damage.Hit -= OnHit;
    }

    void OnHit(Section section) => _jolt = Mathf.Max(_jolt, section == Section.Head ? HitJolt : NearHitJolt);

    /// <summary>
    /// The cab's power and the state of its screens: dark, with the red lamp on, while the reactor is down
    /// (switched off, or overheated) or the engine is off, flickering back while they start; torn and snowy when the
    /// cab has been knocked about — worse the less is left of it, and for a moment after each hit.
    /// </summary>
    void UpdatePower(float dt)
    {
        if (_mech == null) return;
        float target = 1f;
        if (_mech.IsDestroyed) target = 0f;
        else if (_mech.IsShutdown)
            target = _mech.ReactorOn && _mech.RestartProgress > 0f ? (GD.Randf() < 0.25f + 0.75f * _mech.RestartProgress ? 1f : 0.1f) : 0f;
        // (the screens hang on the engine's generator as well: dark while it is off, flickering as it spins up)
        else if (!_mech.EngineOn) target = 0f;
        else if (_mech.EngineStarting) target = GD.Randf() < 0.25f + 0.75f * _mech.EngineProgress ? 1f : 0.1f;
        _power = Mathf.MoveToward(_power, target, dt * (target > _power ? 8f : 2.5f));
        _jolt *= Mathf.Exp(-2.5f * dt);
        float worn = Mathf.Clamp((InterferenceFrom - _mech.Damage.Fraction(Section.Head)) / Mathf.Max(InterferenceFrom, 0.01f), 0f, 1f) * InterferenceMax;
        float starting = (_mech.IsShutdown && _mech.RestartProgress > 0f) || (!_mech.IsShutdown && _mech.EngineStarting) ? 0.35f : 0f;
        float noise = Mathf.Clamp(worn + _jolt * (0.5f + worn) + starting, 0f, 1f);
        // The key names along the screens' bottoms hang on the reactor alone (not on the engine's generator).
        float stripTarget = _mech.IsDestroyed ? 0f : _mech.IsShutdown ? (_mech.ReactorOn && _mech.RestartProgress > 0f ? (GD.Randf() < 0.3f + 0.7f * _mech.RestartProgress ? 1f : 0.1f) : 0f) : 1f;
        _stripPower = Mathf.MoveToward(_stripPower, stripTarget, dt * (stripTarget > _stripPower ? 8f : 2.5f));
        foreach (var screen in _screens)
        {
            screen.SetShaderParameter("power", _power);
            screen.SetShaderParameter("strip_power", _stripPower);
            screen.SetShaderParameter("noise", noise);
        }
        _hudMaterial?.SetShaderParameter("power", _power);
        _hudMaterial?.SetShaderParameter("noise", noise * 0.7f);
        if (_cabLight != null) _cabLight.LightEnergy = LightEnergy * _power;
        if (_screenGlow != null) _screenGlow.LightEnergy = (_cabLight?.Visible ?? true ? 0.3f : 0.45f) * _power;
        if (_emergency != null)
        {
            _emergency.Visible = _power < 0.95f;
            // (a slow pulse, as a warning lamp does)
            _emergency.LightEnergy = EmergencyEnergy * (1f - _power) * (0.75f + 0.25f * Mathf.Sin(Time.GetTicksMsec() * 0.004f));
        }
    }

    /// <summary>The names of the soft keys that belong to this screen (their "screen" metadata), along its bottom.</summary>
    void SoftKeysOn(SubViewport view, string screen, Vector2I size)
    {
        var keys = new SoftKeys { Position = new Vector2(0f, size.Y * (1f - SoftKeyStrip)), Size = new Vector2(size.X, size.Y * SoftKeyStrip) };
        foreach (var key in Geo.Collect<MeshInstance3D>(this))
            if (key.HasMeta("softkey") && key.HasMeta("screen") && (string)key.GetMeta("screen") == screen)
                keys.Keys.Add((key.Name.ToString()[4..], (float)key.GetMeta("at")));
        if (keys.Keys.Count == 0) return;
        // (as wide as the room between neighbouring keys allows)
        float gap = 1f;
        for (int i = 0; i < keys.Keys.Count; i++)
            for (int j = i + 1; j < keys.Keys.Count; j++)
                gap = Mathf.Min(gap, Mathf.Abs(keys.Keys[i].at - keys.Keys[j].at));
        keys.LabelWidth = Mathf.Min(0.3f, gap * 0.9f);
        view.AddChild(keys);
        _screens[^1].SetShaderParameter("strip", SoftKeyStrip);
    }

    /// <summary>A viewport with copies of these HUD instruments laid out on it.</summary>
    SubViewport Screen(Control hud, Vector2I size, (string name, Rect2 area)[] instruments)
    {
        var view = new SubViewport
        {
            Size = size,
            TransparentBg = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
            Disable3D = false,
        };
        AddChild(view);
        _views.Add(view);
        view.AddChild(new ColorRect { Color = new Color(0.01f, 0.035f, 0.02f), Size = view.Size });
        foreach (var (name, area) in instruments)
        {
            if (Copy(hud, name) is not { } copy) continue;
            copy.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
            copy.Position = area.Position * (Vector2)size;
            copy.Size = area.Size * (Vector2)size;
            view.AddChild(copy);
        }
        return view;
    }

    /// <summary>A copy of one of the HUD's instruments, for a screen: shown, quiet, deaf to keys.</summary>
    Control Copy(Control hud, string name)
    {
        if (hud.GetNodeOrNull<Control>(name) is not { } original) return null;
        var copy = (Control)original.Duplicate();
        copy.Visible = true;
        copy.Modulate = Colors.White;
        Quiet(copy);
        if (original is Label label && copy is Label shown) _labels.Add((label, shown));
        return copy;
    }

    static void Quiet(Node node)
    {
        if (node is HudWidget widget) widget.OnScreen = true;
        if (node is AudioStreamPlayer sound)
        {
            sound.Autoplay = false;
            sound.VolumeDb = -80f;
        }
        foreach (Node child in node.GetChildren()) Quiet(child);
    }

    /// <summary>A screen in the cab showing this viewport's picture.</summary>
    void Place(SubViewport view, Node3D parent, string name, Transform3D at, Vector2 size)
    {
        // (A shader of its own, so that it is drawn over the map with the rest of the cab: see Mech.DrawOver.)
        var glass = new ShaderMaterial { Shader = GD.Load<Shader>("res://materials/cockpit_screen.gdshader") };
        glass.SetShaderParameter("picture", view.GetTexture());
        glass.SetShaderParameter("glow", Glow);
        _screens.Add(glass);
        var screen = new MeshInstance3D
        {
            Name = name,
            Mesh = new QuadMesh { Size = size },
            Transform = at,
            MaterialOverride = glass,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        parent.AddChild(screen);
        DrawnOver(screen);
    }

    /// <summary>Drawn over the map if the cab already is (the screens are made after it, see Mech.DrawOver).</summary>
    void DrawnOver(GeometryInstance3D part)
    {
        var console = GetNodeOrNull<GeometryInstance3D>("Dash/Console") ?? GetNodeOrNull<GeometryInstance3D>("Frame");
        if (console?.GetInstanceShaderParameter("in_front") is { VariantType: Variant.Type.Float } over)
            part.SetInstanceShaderParameter("in_front", over);
    }

    // ── The head-up display ─────────────────────────────────────

    void BuildHeadUp(Control hud)
    {
        _hudView = new SubViewport
        {
            TransparentBg = true,
            Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
        };
        AddChild(_hudView);
        // The instruments are laid out on a page as big as the window, drawn smaller into the viewport.
        _hudLayout = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _hudView.AddChild(_hudLayout);
        foreach (string name in HeadUp)
            if (Copy(hud, name) is { } copy)
            {
                // (placed by hand, see LayOnGlass)
                copy.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
                _hudLayout.AddChild(copy);
                _headUp[name] = (copy, hud.GetNode<Control>(name).Size);
            }

        var glass = new ShaderMaterial { Shader = GD.Load<Shader>("res://materials/cockpit_hud.gdshader") };
        glass.SetShaderParameter("picture", _hudView.GetTexture());
        glass.SetShaderParameter("glow", HudGlow);
        glass.SetShaderParameter("opacity", HudOpacity);
        // Light on the inside of the glass: drawn after the glass and whatever is on it (rain, frost, mud),
        // not sorted against it by distance — which, as the head turns, put the glass over it.
        glass.RenderPriority = 20;
        _hudMaterial = glass;
        _combiner = GetNodeOrNull<MeshInstance3D>("HeadUp");
        _glass = _combiner != null;
        if (_glass)
        {
            // The cab's own glass: the picture is seen through it where it lies on the screen.
            glass.SetShaderParameter("collimated", true);
            _combiner.MaterialOverride = glass;
            _combiner.Visible = false;
        }
        else
        {
            _combiner = new MeshInstance3D
            {
                Name = "HeadUp",
                Mesh = new QuadMesh { Size = Vector2.One },
                MaterialOverride = glass,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false,
            };
            AddChild(_combiner);
        }
        DrawnOver(_combiner);
    }

    /// <summary>On in the cockpit's own mode of the interface: the picture follows the window's size, and
    /// what goes on the glass is laid inside its outline as the eye sees it now.</summary>
    void UpdateHeadUp()
    {
        if (_combiner == null) return;
        var cam = GetViewport().GetCamera3D();
        bool on = Hud.Shown == Hud.Mode.Cockpit && !StoryOverlay.InCutscene && FreeCamera.Current == null && cam != null && Game.Cockpit is { FirstPerson: true };
        _combiner.Visible = on;
        HeadUpOn = on;
        _hudView.RenderTargetUpdateMode = on ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        if (!on) return;

        Vector2 window = GetViewport().GetVisibleRect().Size;
        var pixels = new Vector2I(Mathf.Max(16, Mathf.RoundToInt(window.X * HudScale)), Mathf.Max(16, Mathf.RoundToInt(window.Y * HudScale)));
        if (_hudView.Size != pixels)
        {
            _hudView.Size = pixels;
            _hudView.CanvasTransform = Transform2D.Identity.Scaled(new Vector2(pixels.X / window.X, pixels.Y / window.Y));
            _hudLayout.Size = window;
        }
        if (_glass)
        {
            LayOnGlass(cam, window);
            return;
        }
        float height = 2f * HudDistance * Mathf.Tan(Mathf.DegToRad(cam.Fov) * 0.5f);
        if (cam.KeepAspect == Camera3D.KeepAspectEnum.Width) height *= window.Y / window.X;
        var view = cam.GlobalTransform.Orthonormalized();
        _combiner.GlobalTransform = new Transform3D(view.Basis * Basis.FromScale(new Vector3(height * window.X / window.Y, height, 1f)), view.Origin - view.Basis.Z * HudDistance);
    }

    /// <summary>The compass along the top of the glass as the eye sees it, the warnings under it, the
    /// messages along its bottom; the sight stays where the HUD draws it (over the aim point).</summary>
    void LayOnGlass(Camera3D cam, Vector2 window)
    {
        var box = _combiner.GetAabb();
        var lo = new Vector2(float.MaxValue, float.MaxValue);
        var hi = new Vector2(float.MinValue, float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = _combiner.GlobalTransform * box.GetEndpoint(i);
            if (cam.IsPositionBehind(corner)) return;
            Vector2 p = cam.UnprojectPosition(corner);
            lo = lo.Min(p);
            hi = hi.Max(p);
        }
        // The glass rocks on the screen with every step (the pilot's head holds still, the cab does not):
        // what is laid on it follows it only slowly, so the symbols stand steady — and quickly when the
        // pilot looks round and the glass really goes elsewhere.
        var now = new Rect2(lo, hi - lo);
        float off = (now.GetCenter() - _glassRect.GetCenter()).Length() / Mathf.Max(now.Size.X, 1f);
        float rate = _glassRect.Size.X < 1f ? 1000f : 1.2f + 40f * Mathf.Clamp(off - 0.12f, 0f, 1f);
        float follow = 1f - Mathf.Exp(-(float)GetProcessDeltaTime() * rate);
        _glassRect = new Rect2(_glassRect.Position.Lerp(now.Position, follow), _glassRect.Size.Lerp(now.Size, follow));
        var glass = _glassRect;
        float y = glass.Position.Y + glass.Size.Y * 0.04f;
        foreach (var (name, (copy, size)) in _headUp)
        {
            if (name == "Reticle")
            {
                copy.Position = Vector2.Zero;
                copy.Size = window;
                continue;
            }
            // As big as it is on the HUD, unless the glass is narrower than that.
            float k = Mathf.Min(1f, glass.Size.X * 0.94f / Mathf.Max(size.X, 1f));
            copy.Size = size;
            copy.Scale = Vector2.One * k;
            float x = glass.GetCenter().X - size.X * k * 0.5f;
            switch (name)
            {
                case "StatusText":
                    copy.Position = new Vector2(x, glass.End.Y - size.Y * k - glass.Size.Y * 0.04f);
                    break;
                default:
                    // the warnings, one at a time as a rule, under the compass
                    copy.Position = new Vector2(x, y);
                    break;
            }
        }
    }
}
