using Godot;

namespace MechSim;

/// <summary>
/// The instrument panel of a cockpit. Sits on the "Cockpit" node of a mech (the interior shown in
/// first-person view) and puts three screens on its "Dash": damage and heat on the left, the radar in
/// the middle, weapons and throttle on the right. The screens show the very instruments of the HUD —
/// copies of them, drawn into small viewports — so whatever the HUD can show, the cockpit shows.
/// While the cockpit is in view the HUD leaves those instruments out.
/// </summary>
public partial class CockpitDisplays : Node3D
{
    /// <summary>True while some cockpit is showing its screens.</summary>
    public static bool Active => Engine.GetProcessFrames() - _shownFrame < 3;

    // Every mech has one of these, only the player's is ever in view: the last frame any of them was.
    static ulong _shownFrame = ulong.MaxValue / 2;

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

    bool _built;
    float _refreshWait;
    readonly System.Collections.Generic.List<SubViewport> _views = new();

    public override void _Process(double delta)
    {
        // Only the player's own cockpit is ever shown; nothing is built for the rest.
        if (!IsVisibleInTree()) return;
        _shownFrame = Engine.GetProcessFrames();
        if (!_built) Build();
        _refreshWait -= (float)delta;
        if (_refreshWait > 0f) return;
        _refreshWait = Mathf.Max(_refreshWait + 1f / Mathf.Max(RefreshRate, 1f), 0f);
        foreach (var view in _views) view.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
    }

    void Build()
    {
        _built = true;
        var dash = GetNodeOrNull<Node3D>("Dash");
        Control hud = FindHud(GetTree().Root);
        if (dash == null || hud == null) return;

        // What goes on each screen, and where (in shares of the screen).
        Screen(dash, hud, -1, ("PlayerDoll", new Rect2(0.04f, 0.06f, 0.6f, 0.88f)), ("HeatGauge", new Rect2(0.7f, 0.06f, 0.26f, 0.88f)));
        Screen(dash, hud, 0, ("Minimap", new Rect2(0.03f, 0.03f, 0.94f, 0.94f)));
        Screen(dash, hud, 1, ("WeaponList", new Rect2(0.03f, 0.04f, 0.94f, 0.46f)), ("ThrottleGauge", new Rect2(0.08f, 0.52f, 0.5f, 0.44f)), ("JumpGauge", new Rect2(0.7f, 0.52f, 0.2f, 0.44f)));
    }

    void Screen(Node3D dash, Control hud, int column, params (string name, Rect2 area)[] instruments)
    {
        var view = new SubViewport
        {
            Size = new Vector2I(Resolution, Resolution),
            TransparentBg = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
            Disable3D = false,
        };
        AddChild(view);
        _views.Add(view);
        view.AddChild(new ColorRect { Color = new Color(0.01f, 0.035f, 0.02f), Size = view.Size });
        foreach (var (name, area) in instruments)
        {
            if (hud.GetNodeOrNull<Control>(name) is not { } original) continue;
            var copy = (Control)original.Duplicate();
            copy.Visible = true;
            copy.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
            copy.Position = area.Position * Resolution;
            copy.Size = area.Size * Resolution;
            view.AddChild(copy);
        }

        // (A shader of its own, so that it is drawn over the map with the rest of the cab: see Mech.DrawOver.)
        var glass = new ShaderMaterial { Shader = GD.Load<Shader>("res://materials/cockpit_screen.gdshader") };
        glass.SetShaderParameter("picture", view.GetTexture());
        glass.SetShaderParameter("glow", Glow);
        var screen = new MeshInstance3D
        {
            Name = "Screen" + (column < 0 ? "L" : column > 0 ? "R" : "C"),
            Mesh = new QuadMesh { Size = Vector2.One * ScreenSize },
            Position = new Vector3(column * ScreenPitch, 0.02f, 0.012f),
            MaterialOverride = glass,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        dash.AddChild(screen);
        // Drawn over the map if the console it is set in already is (the screens are made after it).
        if (dash.GetNodeOrNull<GeometryInstance3D>("Console")?.GetInstanceShaderParameter("in_front") is { VariantType: Variant.Type.Float } over)
            screen.SetInstanceShaderParameter("in_front", over);
    }

    static Control FindHud(Node from)
    {
        if (from is Hud hud) return hud;
        foreach (Node child in from.GetChildren())
            if (FindHud(child) is { } found) return found;
        return null;
    }
}
