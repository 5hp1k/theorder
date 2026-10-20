using Godot;

namespace MechSim;

/// <summary>
/// The instruments of a cockpit. Sits on the "Cockpit" node of a mech (the interior shown in first-person
/// view) and shows everything the HUD shows, inside the cab, so that the interface can be switched off
/// altogether (key I) and the battle fought from the cockpit alone:
///   * three screens on its "Dash": damage and heat on the left, the radar in the middle, weapons and
///     throttle on the right;
///   * a head-up display projected on a combiner glass in front of the pilot: the sight, the compass
///     and the torso, the target, the squad, the objectives and the clock, the warnings and the
///     messages — each where the HUD has it, so the sight still lies over what the guns aim at. It
///     comes on when the HUD's own instruments are switched off.
/// The screens show the very instruments of the HUD — copies of them, drawn into small viewports — so
/// whatever the HUD can show, the cockpit shows. While the cockpit is in view the HUD may leave out what
/// its screens show (<see cref="Hud.Mode.Cockpit"/>).
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

    [ExportGroup("Head-up display")]
    /// <summary>How far in front of the eye the combiner's picture is laid, metres (nearer than any of the cab).</summary>
    [Export] public float HudDistance = 0.4f;
    /// <summary>Its pixels as a share of the window's.</summary>
    [Export(PropertyHint.Range, "0.3,1")] public float HudScale = 0.75f;
    /// <summary>How brightly it glows, and how much of it shows (it is light on glass).</summary>
    [Export] public float HudGlow = 1.6f;
    [Export(PropertyHint.Range, "0,1")] public float HudOpacity = 0.85f;

    // The HUD's instruments on each place (names of the HUD's children, with where they go in shares of
    // the screen; the head-up display keeps the HUD's own layout).
    static readonly (string, Rect2)[] LeftDash = { ("PlayerDoll", new Rect2(0.04f, 0.06f, 0.6f, 0.88f)), ("HeatGauge", new Rect2(0.7f, 0.06f, 0.26f, 0.88f)) };
    static readonly (string, Rect2)[] MiddleDash = { ("Minimap", new Rect2(0.03f, 0.03f, 0.94f, 0.94f)) };
    static readonly (string, Rect2)[] RightDash = { ("WeaponList", new Rect2(0.03f, 0.04f, 0.94f, 0.46f)), ("ThrottleGauge", new Rect2(0.08f, 0.52f, 0.5f, 0.44f)), ("JumpGauge", new Rect2(0.7f, 0.52f, 0.2f, 0.44f)) };
    static readonly string[] HeadUp = { "Reticle", "TorsoIndicator", "TargetPanel", "SquadCommand", "ObjectiveList", "TimerLabel", "StatusText", "MissileWarning", "NukeWarning", "BoundsWarning", "Scoreboard" };

    /// <summary>True while the player's cockpit is projecting its head-up display.</summary>
    public static bool HeadUpOn { get; private set; }

    bool _built;
    float _refreshWait;
    readonly System.Collections.Generic.List<SubViewport> _views = new();
    // Labels the HUD fills in itself (the clock, the machine's name), and their copies.
    readonly System.Collections.Generic.List<(Label from, Label to)> _labels = new();
    SubViewport _hudView;
    Control _hudLayout;
    MeshInstance3D _combiner;

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
        _refreshWait -= (float)delta;
        if (_refreshWait > 0f) return;
        _refreshWait = Mathf.Max(_refreshWait + 1f / Mathf.Max(RefreshRate, 1f), 0f);
        foreach (var view in _views) view.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        foreach (var (from, to) in _labels)
        {
            to.Text = from.Text;
            // (the clock only in a battle against time; the HUD hides the machine's name where the cockpit shows it)
            to.Visible = from.Name != "TimerLabel" || from.Visible;
        }
    }

    void Build()
    {
        _built = true;
        var dash = GetNodeOrNull<Node3D>("Dash");
        Control hud = Hud.Instance;
        if (hud == null) return;

        if (dash != null)
            for (int column = -1; column <= 1; column++)
            {
                var screen = Screen(hud, new Vector2I(Resolution, Resolution), column < 0 ? LeftDash : column > 0 ? RightDash : MiddleDash);
                Place(screen, dash, "Screen" + (column < 0 ? "L" : column > 0 ? "R" : "C"), new Transform3D(Basis.Identity, new Vector3(column * ScreenPitch, 0.02f, 0.012f)), Vector2.One * ScreenSize);
            }
        BuildHeadUp(hud);
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
        // The instruments keep the HUD's layout on a page as big as the window, drawn smaller into the viewport.
        _hudLayout = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _hudView.AddChild(_hudLayout);
        foreach (string name in HeadUp)
            if (Copy(hud, name) is { } copy) _hudLayout.AddChild(copy);

        var glass = new ShaderMaterial { Shader = GD.Load<Shader>("res://materials/cockpit_hud.gdshader") };
        glass.SetShaderParameter("picture", _hudView.GetTexture());
        glass.SetShaderParameter("glow", HudGlow);
        glass.SetShaderParameter("opacity", HudOpacity);
        _combiner = new MeshInstance3D
        {
            Name = "HeadUp",
            Mesh = new QuadMesh { Size = Vector2.One },
            MaterialOverride = glass,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_combiner);
        DrawnOver(_combiner);
    }

    /// <summary>
    /// On while the HUD's own instruments are switched off: laid square across the view, just in front of
    /// the eye and as big as the view there, so every symbol lies where the HUD would have drawn it.
    /// </summary>
    void UpdateHeadUp()
    {
        if (_combiner == null) return;
        var cam = GetViewport().GetCamera3D();
        bool on = Hud.Off && !StoryOverlay.InCutscene && FreeCamera.Current == null && cam != null && Game.Cockpit is { FirstPerson: true };
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
        float height = 2f * HudDistance * Mathf.Tan(Mathf.DegToRad(cam.Fov) * 0.5f);
        if (cam.KeepAspect == Camera3D.KeepAspectEnum.Width) height *= window.Y / window.X;
        var view = cam.GlobalTransform.Orthonormalized();
        _combiner.GlobalTransform = new Transform3D(view.Basis * Basis.FromScale(new Vector3(height * window.X / window.Y, height, 1f)), view.Origin - view.Basis.Z * HudDistance);
    }
}
