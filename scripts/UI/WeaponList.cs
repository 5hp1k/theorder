using Godot;

namespace MechSim;

/// <summary>One row per weapon: group, name, ammo, recharge bar. Rows stack up from the bottom edge.</summary>
public partial class WeaponList : HudWidget
{
    [Export] public float RowHeight = 26f;

    protected override void Paint()
    {
        var list = Seat.Weapons.Weapons;
        float top = Size.Y - list.Count * RowHeight - 14f;
        DrawRect(new Rect2(0f, top, Size.X, Size.Y - top), Panel);
        for (int i = 0; i < list.Count; i++)
        {
            var w = list[i];
            float y = top + 26f + i * RowHeight;
            Color col = w.Destroyed || !w.HasAmmo ? Red : w.IsReady ? Green : Dim;
            Text($"{w.Group}", new Vector2(10f, y), 16, Amber);
            Text(w.DisplayName, new Vector2(32f, y), 16, col);
            if (w.Destroyed)
            {
                Text("УНИЧТОЖЕНО", new Vector2(210f, y), 13, Red);
                continue;
            }
            if (w.MaxAmmo >= 0) Text($"{w.Ammo}", new Vector2(150f, y), 14, col, HorizontalAlignment.Right, 50f);

            var bar = new Rect2(210f, y - 12f, 100f, 10f);
            DrawRect(bar, Dim, false, 1f);
            DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * w.ReadyFraction, bar.Size.Y)), col);
        }
    }
}
