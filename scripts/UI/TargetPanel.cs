using Godot;

namespace MechSim;

/// <summary>
/// MechWarrior-style target window: live view of the locked target, range and integrity, and under it
/// the target's weapons with their state.
/// Children in the scene: TargetView (SubViewport) with a Camera, View (TextureRect), Doll (DamageDoll).
/// The camera shares the game world but only sees the render layer the locked target is switched onto.
/// </summary>
public partial class TargetPanel : HudWidget
{
    /// <summary>Camera distance from the target and height above its centre.</summary>
    [Export] public float ViewDistance = 26f;
    [Export] public float ViewHeight = 5f;
    /// <summary>Redraw the target view once per this many frames.</summary>
    [Export] public int UpdateEvery = 3;
    /// <summary>Height of a row in the list of the target's weapons.</summary>
    [Export] public float WeaponRow = 17f;

    SubViewport _viewport;
    Camera3D _camera;
    TextureRect _view;
    Control _doll;
    int _frame;

    ITarget Target => Jam <= 0f && Hud?.Seat is { CanLock: true } seat ? seat.Target : null;

    public override void _Ready()
    {
        base._Ready();
        _viewport = GetNode<SubViewport>("TargetView");
        _camera = GetNode<Camera3D>("TargetView/Camera");
        _view = GetNode<TextureRect>("View");
        _doll = GetNode<Control>("Doll");
        _view.Texture = _viewport.GetTexture();
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        var t = Target;
        bool has = t != null;
        _view.Visible = has;
        _doll.Visible = has;
        // A second 3D view is costly: refresh it only every few frames, and only where it is seen (the
        // HUD's own while the interface is up, the cockpit's pod screen's while the cockpit is in view).
        bool seen = OnScreen ? CockpitDisplays.Active : IsVisibleInTree();
        bool render = has && seen && _frame++ % UpdateEvery == 0;
        _viewport.RenderTargetUpdateMode = render ? SubViewport.UpdateMode.Once : SubViewport.UpdateMode.Disabled;
        if (!has) return;

        // Look at the target from the player's side, so the window shows the facing you can actually hit.
        Vector3 center = t.AimCenter;
        Vector3 dir = Seat.WorldPosition - t.GlobalPosition;
        dir.Y = 0f;
        dir = dir.LengthSquared() < 1f ? Vector3.Back : dir.Normalized();
        // Small things are looked at from closer.
        float k = t.Damage != null ? 1f : 0.55f;
        _camera.GlobalPosition = center + dir * (ViewDistance * k) + Vector3.Up * (ViewHeight * k);
        _camera.LookAt(center - Vector3.Up * (1.5f * k), Vector3.Up);
    }

    protected override void Paint()
    {
        if (!Seat.CanLock) return;
        var t = Target;
        if (Jam > 0f)
        {
            DrawRect(new Rect2(0f, 0f, Size.X, 30f), Panel);
            Static(new Rect2(0f, 0f, Size.X, 30f), 24);
            Text("ПОМЕХИ — ЦЕЛЬ ПОТЕРЯНА", new Vector2(10f, 20f), 13, Red);
            return;
        }
        if (t == null)
        {
            DrawRect(new Rect2(0f, 0f, Size.X, 30f), Panel);
            Text("НЕТ ЦЕЛИ   E / T / Q", new Vector2(10f, 20f), 13, Dim);
            return;
        }

        PanelBackground();
        Text(t.DisplayName, new Vector2(10f, 20f), 15, Amber);
        float dist = Seat.WorldPosition.DistanceTo(t.GlobalPosition);
        Text($"{Mathf.RoundToInt(dist)} м", new Vector2(Size.X - 110f, 20f), 15, Green, HorizontalAlignment.Right, 100f);
        DrawRect(new Rect2(_view.Position, _view.Size), Dim, false, 1f);
        Text($"Целостность {Mathf.RoundToInt(t.Integrity * 100f)}%", new Vector2(10f, Size.Y - 12f), 14, Green);
        float lockOn = Seat.LockProgress;
        if (lockOn >= 1f) Text("ЗАХВАТ", new Vector2(Size.X - 90f, Size.Y - 12f), 14, Red, HorizontalAlignment.Right, 80f);
        else if (lockOn > 0f) Text($"захват {Mathf.RoundToInt(lockOn * 100f)}%", new Vector2(Size.X - 110f, Size.Y - 12f), 13, Amber, HorizontalAlignment.Right, 100f);

        // What it is armed with, under the window: knocked-out and empty weapons too.
        if (t is not IDrivable { Weapons: { } arms } || arms.Weapons.Count == 0) return;
        var list = arms.Weapons;
        float top = Size.Y + 4f;
        DrawRect(new Rect2(0f, top, Size.X, list.Count * WeaponRow + 8f), Panel);
        for (int i = 0; i < list.Count; i++)
        {
            var w = list[i];
            float y = top + 16f + i * WeaponRow;
            bool empty = !w.HasAmmo;
            Color col = w.Destroyed ? Red : empty ? Amber : Green;
            Text(w.DisplayName, new Vector2(10f, y), 13, col);
            string state = w.Destroyed ? "УНИЧТОЖЕНО" : empty ? "ПУСТО" : w.MaxAmmo >= 0 ? $"{w.Ammo}" : "";
            Text(state, new Vector2(Size.X - 120f, y), 12, col, HorizontalAlignment.Right, 110f);
            if (w.Destroyed) DrawLine(new Vector2(10f, y - 5f), new Vector2(Size.X - 130f, y - 5f), Red, 1f);
        }
    }
}
