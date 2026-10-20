using Godot;

namespace MechSim;

/// <summary>Big centred messages: end of battle, destroyed, shutdown, overheating.</summary>
public partial class StatusText : HudWidget
{
    protected override void Paint()
    {
        float w = Size.X;
        var game = Game.Instance;
        if (Mission.Current is { State: not Mission.Outcome.Playing } mission)
        {
            bool won = mission.State == Mission.Outcome.Won;
            Text(won ? "МИССИЯ ВЫПОЛНЕНА" : "МИССИЯ ПРОВАЛЕНА", new Vector2(0f, 30f), 30, won ? Green : Red, HorizontalAlignment.Center, w);
            if (!won) Text(mission.Reason, new Vector2(0f, 62f), 20, Amber, HorizontalAlignment.Center, w);
        }
        else if (game is { TimeUp: true })
        {
            Text("ВРЕМЯ ВЫШЛО", new Vector2(0f, 30f), 30, Amber, HorizontalAlignment.Center, w);
            Text($"Уничтожено командой: {Score.KillsOf(Game.PlayerUnit?.Team ?? Team.Player)}", new Vector2(0f, 62f), 20, Green, HorizontalAlignment.Center, w);
        }
        else if (game is { Victory: true })
        {
            Text("ВСЕ ПРОТИВНИКИ УНИЧТОЖЕНЫ", new Vector2(0f, 30f), 30, Green, HorizontalAlignment.Center, w);
            Text("Esc — меню", new Vector2(0f, 62f), 20, Amber, HorizontalAlignment.Center, w);
        }
        else if (Seat.IsDestroyed)
        {
            Text($"{Loc.T(Seat.UnitName).ToUpper()}: УНИЧТОЖЕН", new Vector2(0f, 30f), 30, Red, HorizontalAlignment.Center, w);
            Text($"Восстановление через {Mathf.CeilToInt(Seat.RespawnLeft)} с", new Vector2(0f, 62f), 20, Amber, HorizontalAlignment.Center, w);
        }
        else if (Seat.IsShutdown)
        {
            Text("РЕАКТОР ОТКЛЮЧЁН — ПЕРЕГРЕВ", new Vector2(0f, 30f), 30, Red, HorizontalAlignment.Center, w);
            string sub = Seat.RestartProgress > 0f
                ? $"Перезапуск… {Mathf.RoundToInt(Seat.RestartProgress * 100f)}%"
                : $"Остывание до {Mathf.RoundToInt(Seat.RestartHeat * 100f)}%";
            Text(sub, new Vector2(0f, 62f), 20, Amber, HorizontalAlignment.Center, w);
        }
        else if (Jam > 0f)
        {
            if (Mathf.PosMod(Hud.Time, 1f) < 0.7f) Text("ЭМИ — ПРИБОРЫ ОТКЛЮЧЕНЫ", new Vector2(0f, 30f), 26, Red, HorizontalAlignment.Center, w);
            Text($"Приборы оживут через {Mathf.CeilToInt(Jam)} с", new Vector2(0f, 62f), 18, Amber, HorizontalAlignment.Center, w);
        }
        else if (Seat.HasHeat && Seat.Heat > 0.85f && Mathf.PosMod(Hud.Time, 0.6f) < 0.35f)
        {
            Text("ПЕРЕГРЕВ!", new Vector2(0f, 50f), 24, Red, HorizontalAlignment.Center, w);
        }
    }
}
