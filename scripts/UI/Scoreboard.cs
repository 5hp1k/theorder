using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// The battle table, shown while Tab is held: the player and every machine of the lance with their
/// kills, losses and state, then the other side's tally. (One line per pilot: the same table will
/// do for a network game.)
/// </summary>
public partial class Scoreboard : HudWidget
{
    [Export] public float RowHeight = 24f;

    public override void _Process(double delta)
    {
        Visible = Input.IsActionPressed("scoreboard") && Seat != null;
        base._Process(delta);
    }

    protected override void Paint()
    {
        var own = new List<ITarget>();
        int enemies = 0, enemiesAlive = 0, enemyKills = 0;
        foreach (var unit in Units.All)
        {
            if (unit.Team == Team.Player) own.Add(unit);
            else
            {
                enemies++;
                if (!unit.IsDestroyed) enemiesAlive++;
                enemyKills += Score.Of(unit).Kills;
            }
        }
        own.Sort((a, b) => (b == Game.PlayerUnit).CompareTo(a == Game.PlayerUnit));

        float height = 96f + own.Count * RowHeight + 46f;
        var box = new Rect2(0f, 0f, Size.X, height);
        DrawRect(box, new Color(0f, 0.04f, 0.02f, 0.82f));
        DrawRect(box, Dim, false, 1.5f);
        Text("ТАБЛИЦА БОЯ", new Vector2(0f, 30f), 22, Amber, HorizontalAlignment.Center, Size.X);
        float x0 = 24f, xKills = Size.X - 330f, xLost = Size.X - 240f, xState = Size.X - 150f;
        Text("Пилот · машина", new Vector2(x0, 62f), 13, Dim);
        Text("Уничтожил", new Vector2(xKills, 62f), 13, Dim);
        Text("Потерян", new Vector2(xLost, 62f), 13, Dim);
        Text("Состояние", new Vector2(xState, 62f), 13, Dim);
        DrawLine(new Vector2(x0, 70f), new Vector2(Size.X - 24f, 70f), Dim, 1f);
        int total = 0;
        for (int i = 0; i < own.Count; i++)
        {
            var unit = own[i];
            var line = Score.Of(unit);
            total += line.Kills;
            float y = 92f + i * RowHeight;
            bool me = unit == Game.PlayerUnit;
            Color col = unit.IsDestroyed ? Red : me ? Amber : Green;
            Text(me ? $"ВЫ · {unit.DisplayName}" : unit.DisplayName, new Vector2(x0, y), 15, col);
            Text(line.Kills.ToString(), new Vector2(xKills, y), 15, col);
            Text(line.Losses.ToString(), new Vector2(xLost, y), 15, col);
            Text(unit.IsDestroyed ? "уничтожен" : $"{Mathf.RoundToInt(unit.Integrity * 100f)}%", new Vector2(xState, y), 15, col);
        }
        float foot = 92f + own.Count * RowHeight + 12f;
        DrawLine(new Vector2(x0, foot - 14f), new Vector2(Size.X - 24f, foot - 14f), Dim, 1f);
        Text($"Наши: уничтожено {total}", new Vector2(x0, foot + 6f), 15, Green);
        Text($"Противник: в строю {enemiesAlive} из {enemies}, уничтожил {enemyKills}", new Vector2(x0, foot + 28f), 15, Red);
    }
}
