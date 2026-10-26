using Godot;

namespace MechSim;

/// <summary>
/// MechWarrior-style radar. Up = where the legs point; the wedge is the field of view; red blips are
/// enemies (hollow at the rim = beyond range), the boxed one is the locked target.
/// The radar_mode key cycles: blips only → relief map → live view from above → off.
/// Children in the scene: MapView (SubViewport) with a Camera for the view from above.
/// </summary>
public partial class Minimap : HudWidget
{
    /// <summary>Colour of one's own side on the radar.</summary>
    [Export] public Color Friend = new(0.35f, 0.65f, 1f, 0.95f);

    public enum Mode { Radar, Relief, Satellite, Off }

    /// <summary>World distance from the centre to the rim, metres.</summary>
    [Export] public float Range = 300f;
    [Export] public Mode Current = Mode.Radar;
    /// <summary>Height of the overhead camera above the player.</summary>
    [Export] public float CameraHeight = 250f;
    /// <summary>Redraw the overhead view once per this many frames.</summary>
    [Export] public int UpdateEvery = 3;

    static readonly string[] ModeNames = { "РЛС", "РЕЛЬЕФ", "ВИД СВЕРХУ", "" };

    SubViewport _view;
    Camera3D _camera;
    int _frame;
    readonly Vector2[] _points = new Vector2[48], _uvs = new Vector2[48];

    public override void _Ready()
    {
        base._Ready();
        _view = GetNode<SubViewport>("MapView");
        _camera = GetNode<Camera3D>("MapView/Camera");
        _camera.Size = Range * 2f;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("radar_mode"))
            Current = (Mode)(((int)Current + 1) % 4);
    }

    /// <summary>The frame the radar is drawn in: the player's position and which way is "up".</summary>
    static bool Viewer(out Vector3 position, out Basis basis, out float lookYaw)
    {
        position = default;
        basis = Basis.Identity;
        lookYaw = 0f;
        var seat = Game.Cockpit;
        if (seat == null) return false;
        position = seat.WorldPosition;
        basis = new Basis(Vector3.Up, seat.HullYaw);
        lookYaw = seat.Twist;
        return true;
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        bool live = Current == Mode.Satellite && Jam <= 0f && Viewer(out Vector3 pos, out Basis basis, out _);
        _view.RenderTargetUpdateMode = live && _frame++ % UpdateEvery == 0 ? SubViewport.UpdateMode.Once : SubViewport.UpdateMode.Disabled;
        if (!live) return;
        // Straight down, with the top of the picture towards where the player faces.
        Viewer(out pos, out basis, out _);
        Vector3 forward = -basis.Z;
        _camera.GlobalTransform = new Transform3D(new Basis(forward.Cross(Vector3.Up), forward, Vector3.Up), pos + Vector3.Up * CameraHeight);
    }

    protected override void Paint()
    {
        if (Current == Mode.Off || !Viewer(out Vector3 pos, out Basis basis, out float lookYaw)) return;
        Vector2 c = Size / 2f;
        float r = Mathf.Min(c.X, c.Y) - 12f;

        DrawCircle(c, r, Panel);
        if (Jam > 0f)
        {
            // Burnt out by a pulse: nothing but snow until the set comes back.
            DrawArc(c, r, 0f, Mathf.Tau, 48, Dim, 1.5f, true);
            Static(new Rect2(c - Vector2.One * r * 0.7f, Vector2.One * r * 1.4f));
            if (Mathf.PosMod(Clock, 0.8f) < 0.5f) Text("НЕТ СИГНАЛА", new Vector2(c.X - 80f, c.Y + 5f), 14, Red, HorizontalAlignment.Center, 160f);
            return;
        }
        DrawMap(c, r, pos, basis);
        DrawArc(c, r, 0f, Mathf.Tau, 48, Dim, 1.5f, true);
        DrawArc(c, r * 0.5f, 0f, Mathf.Tau, 32, new Color(Dim, 0.2f), 1f, true);
        DrawLine(c - new Vector2(r, 0f), c + new Vector2(r, 0f), new Color(Dim, 0.2f), 1f);
        DrawLine(c - new Vector2(0f, r), c + new Vector2(0f, r), new Color(Dim, 0.2f), 1f);

        // View wedge. Yaw is positive to the left, screen angles grow clockwise from +X.
        var cam = Seat.Camera;
        if (cam != null)
        {
            float aspect = GetViewportRect().Size.X / Mathf.Max(1f, GetViewportRect().Size.Y);
            float halfFov = Mathf.Atan(Mathf.Tan(Mathf.DegToRad(cam.Fov) * 0.5f) * aspect);
            float mid = -Mathf.Pi / 2f - lookYaw;
            Vector2 Edge(float a) => c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            DrawColoredPolygon(new[] { c, Edge(mid - halfFov), Edge(mid), Edge(mid + halfFov) }, new Color(Green, 0.1f));
            DrawLine(c, Edge(mid - halfFov), Dim, 1f);
            DrawLine(c, Edge(mid + halfFov), Dim, 1f);
        }

        // World → radar: rotate into the viewer's frame; forward (-Z) is up.
        Basis toLocal = basis.Inverse();
        Vector2 Map(Vector3 world, out bool inRange)
        {
            Vector3 l = toLocal * (world - pos);
            var p = new Vector2(l.X, l.Z) / Range;
            inRange = p.LengthSquared() <= 1f;
            if (!inRange) p = p.Normalized();
            return c + p * r;
        }

        // The edge of the battlefield, where it comes within range.
        if (MapTerrain.Instance is { } terrain && IsInstanceValid(terrain))
        {
            float h = terrain.PlayHalf;
            Vector3[] corners = { new(-h, 0f, -h), new(h, 0f, -h), new(h, 0f, h), new(-h, 0f, h) };
            for (int e = 0; e < 4; e++)
            {
                Vector3 a = corners[e], b = corners[(e + 1) % 4];
                const int steps = 48;
                Vector2 last = Map(a, out bool lastIn);
                for (int i = 1; i <= steps; i++)
                {
                    Vector2 p = Map(a.Lerp(b, (float)i / steps), out bool inside);
                    if (inside && lastIn) DrawLine(last, p, new Color(Amber, 0.8f), 2f);
                    last = p;
                    lastIn = inside;
                }
            }
        }

        // North tick on the rim.
        Vector3 n = toLocal * Vector3.Forward;
        Text("С", c + new Vector2(n.X, n.Z).Normalized() * (r + 2f) + new Vector2(-5f, 5f), 12, Green);

        ITarget target = Seat.CanLock ? Seat.Target : null;
        // Repair bays: a green cross.
        foreach (var bay in RepairBay.All)
        {
            Vector2 p = Map(bay.GlobalPosition, out bool near);
            if (!near) continue;
            DrawLine(p + new Vector2(-5f, 0f), p + new Vector2(5f, 0f), Green, 2f);
            DrawLine(p + new Vector2(0f, -5f), p + new Vector2(0f, 5f), Green, 2f);
        }
        // Our own side: blue.
        foreach (var e in Units.All)
        {
            if (e.IsDestroyed || e == Game.PlayerUnit || Game.PlayerUnit == null || Units.Hostile(Game.PlayerUnit, e)) continue;
            Vector2 p = Map(e.GlobalPosition, out bool inRange);
            if (inRange) DrawRect(new Rect2(p - new Vector2(3f, 3f), new Vector2(6f, 6f)), Friend);
            else DrawArc(p, 3f, 0f, Mathf.Tau, 12, Friend, 1f, true);
            if (e == target)
                DrawRect(new Rect2(p - new Vector2(7f, 7f), new Vector2(14f, 14f)), Amber, false, 1.5f);
        }
        foreach (var e in Units.All)
        {
            if (e.IsDestroyed || (Game.PlayerUnit != null ? !Units.Hostile(Game.PlayerUnit, e) : e.Team != Team.Enemy)) continue;
            Vector2 p = Map(e.GlobalPosition, out bool inRange);
            if (inRange) DrawCircle(p, 3.5f, Red);
            else DrawArc(p, 3f, 0f, Mathf.Tau, 12, Red, 1f, true);
            if (e == target)
                DrawRect(new Rect2(p - new Vector2(7f, 7f), new Vector2(14f, 14f)), Amber, false, 1.5f);
        }

        // Own unit.
        DrawColoredPolygon(new[] { c + new Vector2(0f, -7f), c + new Vector2(5f, 6f), c + new Vector2(-5f, 6f) }, Green);
        Text($"{Mathf.RoundToInt(Range)} м", new Vector2(c.X + r * 0.55f, c.Y + r + 8f), 11, Dim);
        Text(ModeNames[(int)Current], new Vector2(0f, c.Y + r + 8f), 11, Dim, HorizontalAlignment.Left);
    }

    /// <summary>The ground under the radar: the relief picture of the map, or the live view from above.</summary>
    void DrawMap(Vector2 c, float r, Vector3 pos, Basis basis)
    {
        Texture2D texture = null;
        var terrain = MapTerrain.Instance;
        if (Current == Mode.Relief) texture = terrain?.MapTexture;
        else if (Current == Mode.Satellite) texture = _view.GetTexture();
        if (texture == null) return;

        for (int i = 0; i < _points.Length; i++)
        {
            float a = i * Mathf.Tau / _points.Length;
            var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            _points[i] = c + d * r;
            if (Current == Mode.Satellite)
            {
                // The camera already turns with the viewer.
                _uvs[i] = new Vector2(0.5f, 0.5f) + d * 0.5f;
            }
            else
            {
                // The map picture is fixed north-up: find the world point under this spot of the radar.
                Vector3 world = pos + basis * new Vector3(d.X, 0f, d.Y) * Range;
                _uvs[i] = new Vector2(world.X, world.Z) / terrain.Size + new Vector2(0.5f, 0.5f);
            }
        }
        DrawColoredPolygon(_points, new Color(1f, 1f, 1f, 0.9f), _uvs, texture);
    }
}
