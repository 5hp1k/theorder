using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// The controls of a cockpit (put in by <see cref="CockpitDisplays"/>): the side-stick, the throttle and
/// the pedals move as the machine is driven, and the switches and buttons on the consoles and the panel
/// can be worked with the mouse. Key F (interact) frees the cursor in the cab: whatever control is under
/// it lights up, a click works it — it presses the game's own action, so it does just what its key does —
/// and the right mouse button held down looks round. F again (or a click on nothing with the right
/// button) takes the mouse back to the guns.
/// The controls are the meshes of the cab named "Sw_&lt;action&gt;" (toggle switches: their lever flips about
/// its "axis" metadata) and "Btn_&lt;action&gt;" (push buttons: pressed in along their "push" metadata), made
/// by the cab's model (tools/blender/walker.py, cockpit()). Buttons with the "softkey" metadata are the keys
/// along the bottom of the panel's screens: what they do is written on the screen above them
/// (<see cref="CockpitDisplays"/>, <see cref="KeyLabels"/>), lit while it is on; the rest have a placard.
/// </summary>
public partial class CockpitControls : Node3D
{
    /// <summary>The cursor is free in the cab: the mouse works the controls, not the guns.</summary>
    public static bool CursorMode { get; private set; }

    /// <summary>How far the throttle swings each way, the stick tilts, the pedals travel (radians, metres).</summary>
    [Export] public float ThrottleSwing = 0.8f;
    [Export] public float StickTilt = 0.55f;
    [Export] public float PedalTravel = 0.07f;
    /// <summary>How long a clicked button stays pressed in, seconds.</summary>
    [Export] public float PressTime = 0.35f;
    /// <summary>How near the cursor must be to a control's middle, pixels.</summary>
    [Export] public float PickRadius = 30f;

    // What each control's placard says.
    static readonly Dictionary<string, string> Placards = new()
    {
        ["toggle_lights"] = "ФАРЫ", ["counter_rotate"] = "КОНТРПОВОРОТ", ["toggle_hud"] = "ИНДИКАЦИЯ", ["kneel"] = "ПРИСЕСТЬ",
        ["target_nearest"] = "БЛИЖ. ЦЕЛЬ", ["target_next"] = "СЛЕД. ЦЕЛЬ", ["target_friendly"] = "ЦЕЛЬ: СВОЙ", ["target_reticle"] = "ЦЕЛЬ В ПРИЦЕЛЕ",
        ["radar_mode"] = "РЕЖИМ РЛС", ["zoom"] = "УВЕЛИЧЕНИЕ", ["full_stop"] = "ПОЛНЫЙ СТОП", ["center_torso"] = "ТОРС ПРЯМО",
        ["cab_light"] = "СВЕТ КАБИНЫ", ["glass_heat"] = "ОБОГРЕВ", ["night_vision"] = "ПНВ", ["thermal_vision"] = "ТЕПЛОВИЗОР",
        ["engine"] = "ДВИГАТЕЛЬ", ["reactor"] = "РЕАКТОР",
    };

    /// <summary>What the screens write over their soft keys (short: five to a screen).</summary>
    public static readonly Dictionary<string, string> KeyLabels = new()
    {
        ["toggle_lights"] = "ФАРЫ", ["cab_light"] = "СВЕТ", ["glass_heat"] = "ОБОГР", ["night_vision"] = "ПНВ", ["thermal_vision"] = "ТЕПЛО",
        ["target_nearest"] = "БЛИЖ", ["target_next"] = "СЛЕД", ["target_friendly"] = "СВОЙ", ["target_reticle"] = "ПРИЦ", ["radar_mode"] = "РЛС",
        ["counter_rotate"] = "КОНТР", ["kneel"] = "СЕСТЬ", ["full_stop"] = "СТОП", ["engine"] = "ДВИГ", ["reactor"] = "РЕАКТ", ["coolant"] = "ОХЛ",
        ["squad_attack"] = "АТАКА", ["squad_follow"] = "СТРОЙ", ["squad_goto"] = "ТОЧКА", ["squad_hold"] = "СТОЙ", ["squad_fire"] = "ТИХО",
    };

    /// <summary>The action of the control under the cursor, if any (the screens light its key's label).</summary>
    public static string Hovered { get; private set; }
    /// <summary>The action of the soft key pressed last, and when (the screens flash its label).</summary>
    public static string LastPressed { get; private set; }
    public static ulong LastPressedAt { get; private set; }

    class Control3D
    {
        public MeshInstance3D Node;
        public string Action;
        public bool Switch, SoftKey;
        public Vector3 Axis, Push;
        public Transform3D Rest;
        public bool On;
        public float Pressed;
        public MeshInstance3D Placard;
        public GeometryInstance3D Led;
    }

    readonly List<Control3D> _controls = new();
    Node3D _stick, _throttle, _pedalL, _pedalR;
    Transform3D _stickRest, _throttleRest, _pedalLRest, _pedalRRest;
    float _stickX, _stickY, _lastYaw, _lastPitch;
    Control3D _hover;
    Material _glow;
    AudioStreamPlayer _click;
    string _release;

    public override void _Ready()
    {
        var cab = GetParent<Node3D>();
        _glow = GD.Load<Material>("res://materials/cockpit_hover.tres");
        _click = new AudioStreamPlayer { Stream = GD.Load<AudioStream>("res://audio/ui_click.wav"), VolumeDb = -8f, Bus = "Master" };
        AddChild(_click);
        foreach (var mesh in Geo.Collect<MeshInstance3D>(cab))
        {
            string name = mesh.Name;
            bool sw = name.StartsWith("Sw_"), btn = name.StartsWith("Btn_");
            if (!sw && !btn) continue;
            var c = new Control3D
            {
                Node = mesh,
                Action = name[(sw ? 3 : 4)..],
                Switch = sw,
                Axis = mesh.HasMeta("axis") ? (Vector3)mesh.GetMeta("axis") : Vector3.Right,
                Push = mesh.HasMeta("push") ? (Vector3)mesh.GetMeta("push") : Vector3.Up,
                Rest = mesh.Transform,
                SoftKey = mesh.HasMeta("softkey"),
            };
            if (!c.SoftKey) c.Placard = Placard(c);
            c.Led = cab.FindChild("Led_" + c.Action, true, false) as GeometryInstance3D;
            _controls.Add(c);
        }
        _stick = cab.GetNodeOrNull<Node3D>("Stick");
        _throttle = cab.GetNodeOrNull<Node3D>("Throttle");
        _pedalL = cab.GetNodeOrNull<Node3D>("PedalL");
        _pedalR = cab.GetNodeOrNull<Node3D>("PedalR");
        if (_stick != null) _stickRest = _stick.Transform;
        if (_throttle != null) _throttleRest = _throttle.Transform;
        if (_pedalL != null) _pedalLRest = _pedalL.Transform;
        if (_pedalR != null) _pedalRRest = _pedalR.Transform;
    }

    /// <summary>A stencilled label beside the control, lying on its panel.</summary>
    /// <summary>
    /// A stencilled label beside the control, lying on its panel: the text drawn once into a small picture,
    /// laid on a quad as paint (materials/cockpit_decal.gdshader) — part of the cab like the rest of it, so
    /// whatever stands in front of it hides it.
    /// </summary>
    MeshInstance3D Placard(Control3D c)
    {
        if (!Placards.TryGetValue(c.Action, out string text)) return null;
        text = Loc.T(text);
        Vector3 n = c.Push.Normalized();
        // (a switch's lever stands up off its panel: the console's top, or the instrument panel's face)
        if (c.Switch) n = c.Node.GetParent().Name == "Dash" ? Vector3.Back : Vector3.Up;
        // Flat on the panel, read from the pilot's seat: on a console behind the control, on the panel under it;
        // on the glare shield in front of it, read straight on.
        bool flat = Mathf.Abs(n.Y) > 0.7f;
        bool front = c.Node.HasMeta("placard") && (string)c.Node.GetMeta("placard") == "front";
        var pixels = new Vector2I(Mathf.Max(64, text.Length * 26 + 16), 48);
        var view = new SubViewport { Size = pixels, TransparentBg = true, Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Once };
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Size = pixels,
        };
        label.AddThemeFontSizeOverride("font_size", 34);
        label.AddThemeColorOverride("font_color", Colors.White);
        view.AddChild(label);
        AddChild(view);
        var paint = new ShaderMaterial { Shader = GD.Load<Shader>("res://materials/cockpit_decal.gdshader") };
        paint.SetShaderParameter("picture", view.GetTexture());
        float height = 0.016f;
        var decal = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(height * pixels.X / pixels.Y, height) },
            MaterialOverride = paint,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        decal.Transform = front
            ? new Transform3D(new Basis(Vector3.Right, -Mathf.Pi / 2f), c.Rest.Origin + new Vector3(0f, -0.0045f, 0.05f))
            : flat
            ? new Transform3D(new Basis(Vector3.Right, -Mathf.Pi / 2f), c.Rest.Origin + new Vector3(c.Rest.Origin.X < 0f ? 0.075f : -0.075f, 0.0015f, 0f))
            : new Transform3D(Basis.Identity, c.Rest.Origin + new Vector3(0f, -0.036f, c.Switch ? -0.0025f : 0.0015f));
        if (flat && !front)
            // (sideways along the console, read from the seat)
            decal.Rotate(Vector3.Up, c.Rest.Origin.X < 0f ? -Mathf.Pi / 2f : Mathf.Pi / 2f);
        c.Node.GetParent().AddChild(decal);
        // (drawn in front of the map with the rest of the cab, if the cab already is: see Mech.DrawOver)
        if (GetParent()?.GetNodeOrNull<GeometryInstance3D>("Frame")?.GetInstanceShaderParameter("in_front") is { VariantType: Variant.Type.Float } over)
            decal.SetInstanceShaderParameter("in_front", over);
        return decal;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (_release != null)
        {
            // (the press of a clicked control is let go the frame after)
            Input.ParseInputEvent(new InputEventAction { Action = _release, Pressed = false });
            _release = null;
        }
        var pilot = Game.Cockpit as PlayerPilot;
        var unit = pilot?.Unit;
        bool seated = IsVisibleInTree() && pilot != null && pilot.FirstPerson && unit != null && !unit.IsDestroyed;
        if (CursorMode && (!seated || Input.MouseMode != Input.MouseModeEnum.Visible || StoryOverlay.InCutscene)) SetCursor(false);
        if (!seated) return;

        // The stick follows the hand on the mouse — how fast the torso is being twisted and tipped — and
        // the turning; the throttle the speed asked for; the pedals the turn.
        float yawRate = dt > 0f ? Mathf.AngleDifference(_lastYaw, unit.TargetYaw) / dt : 0f;
        float pitchRate = dt > 0f ? (unit.TargetPitch - _lastPitch) / dt : 0f;
        _lastYaw = unit.TargetYaw;
        _lastPitch = unit.TargetPitch;
        float k = 1f - Mathf.Exp(-dt * 9f);
        _stickX = Mathf.Lerp(_stickX, Mathf.Clamp(-yawRate * 1.1f - unit.TurnInput * 0.8f, -1f, 1f), k);
        _stickY = Mathf.Lerp(_stickY, Mathf.Clamp(-pitchRate * 1.4f, -1f, 1f), k);
        // (and jolts a little with every footfall)
        float jolt = Mathf.Sin(unit.Time * 37f) * Mathf.Min(unit.Shake, 1f) * 0.08f;
        if (_stick != null) _stick.Transform = _stickRest * new Transform3D(new Basis(Vector3.Forward, _stickX * StickTilt) * new Basis(Vector3.Right, _stickY * StickTilt + jolt), Vector3.Zero);
        if (_throttle != null) _throttle.Transform = _throttleRest * new Transform3D(new Basis(Vector3.Right, -unit.Throttle * ThrottleSwing), Vector3.Zero);
        float turn = unit.TurnInput * PedalTravel;
        if (_pedalL != null) _pedalL.Transform = _pedalLRest * new Transform3D(new Basis(Vector3.Right, -turn * 6f), new Vector3(0f, 0f, -turn));
        if (_pedalR != null) _pedalR.Transform = _pedalRRest * new Transform3D(new Basis(Vector3.Right, turn * 6f), new Vector3(0f, 0f, turn));

        foreach (var c in _controls)
        {
            if (c.Switch)
            {
                c.On = State(c, pilot, unit) ?? c.On;
                float angle = c.On ? -0.5f : 0.5f;
                c.Node.Transform = c.Rest * new Transform3D(new Basis(c.Axis.Normalized(), angle), Vector3.Zero);
                c.Led?.SetInstanceShaderParameter("lit", c.On ? 1f : 0f);
            }
            else
            {
                bool was = c.Pressed > 0f;
                c.Pressed = Mathf.Max(0f, c.Pressed - dt);
                // Pressed in (most of the way at once, out again slowly), and lit while it is.
                float depth = Mathf.Clamp(c.Pressed / PressTime * 1.6f, 0f, 1f) * 0.012f;
                c.Node.Transform = c.Rest.Translated(-c.Push.Normalized() * depth);
                if (was && c.Pressed <= 0f && c != _hover) c.Node.MaterialOverlay = null;
            }
        }

        var hover = CursorMode ? Pick(GetViewport().GetMousePosition()) : null;
        if (hover != _hover)
        {
            if (_hover != null) Light(_hover, false);
            _hover = hover;
            if (_hover != null) Light(_hover, true);
        }
    }

    /// <summary>Whether any of the lance is doing this (null: there is no lance).</summary>
    static bool? Squad(System.Func<AiPilot, bool> doing)
    {
        if (Game.Instance?.Squad is not { Count: > 0 } squad) return null;
        foreach (var pilot in squad)
            if (IsInstanceValid(pilot) && doing(pilot)) return true;
        return false;
    }

    /// <summary>The lance's orders by their keys, numbered as in the list of orders (SquadCommand).</summary>
    static readonly Dictionary<string, int> SquadOrders = new() { ["squad_attack"] = 0, ["squad_follow"] = 2, ["squad_fire"] = 3, ["squad_goto"] = 4, ["squad_hold"] = 5 };

    /// <summary>What a soft key's label says now (the coolant key counts the flushes left).</summary>
    public static string LabelOf(string action)
    {
        string text = Loc.T(KeyLabels.TryGetValue(action, out string t) ? t : action);
        if (action == "coolant" && Game.Cockpit is PlayerPilot { Unit: Mech mech }) text += " " + mech.Coolant;
        return text;
    }

    /// <summary>Whether the function of this action is on now, for the player's machine (null: it is no switch, or the game cannot tell).</summary>
    public static bool? StateOf(string action) => Game.Cockpit is PlayerPilot { Unit: { } unit } pilot ? State(action, pilot, unit) : null;

    static bool? State(Control3D c, PlayerPilot pilot, IDrivable unit) => State(c.Action, pilot, unit);

    /// <summary>Where a switch really stands, if the game knows (else it keeps the position it was flicked to).</summary>
    static bool? State(string action, PlayerPilot pilot, IDrivable unit) => action switch
    {
        "engine" => unit is Mech engined ? engined.EngineOn : null,
        "reactor" => unit is Mech powered ? powered.ReactorOn : null,
        "squad_attack" => Squad(p => p.Order == AiPilot.SquadOrder.Attack),
        "squad_follow" => Squad(p => p.Order == AiPilot.SquadOrder.Follow),
        "squad_goto" => Squad(p => p.Order == AiPilot.SquadOrder.GoTo),
        "squad_hold" => Squad(p => p.Order == AiPilot.SquadOrder.Hold),
        "squad_fire" => Squad(p => p.HoldFire),
        "toggle_lights" => Geo.Collect<Headlight>(unit.Body) is { Count: > 0 } lights ? lights[0].On : null,
        "counter_rotate" => pilot.CounterRotate,
        "toggle_hud" => Hud.Shown != Hud.Mode.Off,
        "zoom" => pilot.Zoomed,
        "kneel" => unit is Mech mech ? mech.Kneel : null,
        "cab_light" => (pilot.Unit.CockpitInterior?.GetNodeOrNull<OmniLight3D>("CabLight")?.Visible),
        "glass_heat" => (pilot.Unit.CockpitInterior?.GetNodeOrNull<CockpitGlass>("Glass")?.Heating),
        "night_vision" => VisionFx.Mode == VisionFx.Sight.Night,
        "thermal_vision" => VisionFx.Mode == VisionFx.Sight.Thermal,
        _ => null,
    };

    void Light(Control3D c, bool on)
    {
        Hovered = on ? c.Action : null;
        c.Node.MaterialOverlay = on ? _glow : null;
        if (c.Placard?.MaterialOverride is ShaderMaterial paint)
        {
            paint.SetShaderParameter("tint", on ? new Color(0.55f, 1f, 0.6f) : new Color(0.86f, 0.86f, 0.78f));
            paint.SetShaderParameter("glow", on ? 0.8f : 0f);
        }
    }

    Control3D Pick(Vector2 mouse)
    {
        var cam = GetViewport().GetCamera3D();
        if (cam == null) return null;
        Control3D best = null;
        float bestD = PickRadius * PickRadius;
        foreach (var c in _controls)
        {
            Vector3 at = c.Node.GlobalPosition;
            if (cam.IsPositionBehind(at)) continue;
            float d = cam.UnprojectPosition(at).DistanceSquaredTo(mouse);
            if (d < bestD)
            {
                bestD = d;
                best = c;
            }
        }
        return best;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        var pilot = Game.Cockpit as PlayerPilot;
        if (!IsVisibleInTree() || pilot == null || !pilot.FirstPerson || StoryOverlay.InCutscene) return;
        // The cab's own light and the glass heater have no other place to be worked from.
        if (@event.IsActionPressed("cab_light") && GetParent()?.GetNodeOrNull<OmniLight3D>("CabLight") is { } lamp)
        {
            // (the screens' glow is turned up a little in the dark by CockpitDisplays)
            lamp.Visible = !lamp.Visible;
            return;
        }
        if (@event.IsActionPressed("glass_heat") && GetParent()?.GetNodeOrNull<CockpitGlass>("Glass") is { } glass)
        {
            glass.Heating = !glass.Heating;
            return;
        }
        if (@event.IsActionPressed("interact"))
        {
            SetCursor(!CursorMode);
            GetViewport().SetInputAsHandled();
            return;
        }
        if (!CursorMode) return;
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click)
        {
            if ((Pick(click.Position) ?? _hover) is { } c) Work(c);
            GetViewport().SetInputAsHandled();
        }
        else if (@event is InputEventMouseMotion motion && Input.IsMouseButtonPressed(MouseButton.Right))
        {
            pilot.LookBy(motion.Relative);
            GetViewport().SetInputAsHandled();
        }
    }

    void Work(Control3D c)
    {
        if (c.Switch) c.On = !c.On;
        else
        {
            c.Pressed = PressTime;
            c.Node.MaterialOverlay = _glow;
        }
        if (SquadOrders.TryGetValue(c.Action, out int order)) SquadCommand.GiveAll(order);
        else
        {
            Input.ParseInputEvent(new InputEventAction { Action = c.Action, Pressed = true });
            _release = c.Action;
        }
        LastPressed = c.Action;
        LastPressedAt = Time.GetTicksMsec();
        _click.Play();
    }

    void SetCursor(bool on)
    {
        CursorMode = on;
        Input.MouseMode = on ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
        if (!on && _hover != null)
        {
            Light(_hover, false);
            _hover = null;
        }
        if (!on && Game.Cockpit is PlayerPilot pilot) pilot.SuppressFireUntilRelease();
    }

    public override void _ExitTree()
    {
        if (CursorMode) CursorMode = false;
        Hovered = null;
    }
}
