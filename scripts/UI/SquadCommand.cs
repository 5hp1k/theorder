using Godot;

namespace MechSim;

/// <summary>
/// Lance commands as in MechWarrior 4. A function key picks who the order is for — F1 the first
/// lancemate, F2 the second and so on, and the key after the last one the whole lance — which opens
/// the list of orders; a number key then gives one:
/// 1 attack my target · 2 defend my target · 3 form on me · 4 hold fire (again: fire at will) ·
/// 5 go to the point under my sight · 6 stop · 7 shut down (again: power up) · 8 repair at the nearest bay.
/// Esc, or a few seconds without a choice, closes the list. The lance itself is always listed on the
/// left of the HUD: number, machine, what it is doing, how battered it is.
/// </summary>
public partial class SquadCommand : HudWidget
{
    [Export] public float RowHeight = 18f;
    /// <summary>Seconds the list of orders stays open, and the last order stays written.</summary>
    [Export] public float MenuTime = 6f;
    [Export] public float NoteTime = 3f;

    static readonly Key[] Pick = { Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6, Key.F7, Key.F8, Key.F9 };
    static readonly Key[] Digits = { Key.Key1, Key.Key2, Key.Key3, Key.Key4, Key.Key5, Key.Key6, Key.Key7 };
    static readonly string[] Orders =
    {
        "Атаковать мою цель", "Защищать мою цель", "В строй, за мной", "Не стрелять / огонь по готовности",
        "Идти в точку под прицелом", "Стоять", "Заглушить реактор / запустить",
    };

    /// <summary>-2 = the list is closed, -1 = the whole lance, otherwise the lancemate's index.</summary>
    int _who = -2;

    static SquadCommand _shown;
    /// <summary>The list of orders is open: the number keys give orders, they don't fire weapon groups.</summary>
    public static bool Listening => _shown != null && IsInstanceValid(_shown) && _shown._who != -2;

    public override void _EnterTree() => _shown = this;
    float _menuLeft, _noteLeft;
    string _note = "";

    public override void _UnhandledInput(InputEvent @event)
    {
        var squad = Game.Instance?.Squad;
        if (squad == null || squad.Count == 0 || @event is not InputEventKey { Pressed: true, Echo: false } key) return;

        int pick = System.Array.IndexOf(Pick, key.PhysicalKeycode);
        if (pick >= 0 && pick <= squad.Count)
        {
            _who = pick == squad.Count ? -1 : pick;
            _menuLeft = MenuTime;
            GetViewport().SetInputAsHandled();
            return;
        }
        if (_who == -2) return;
        if (key.PhysicalKeycode == Key.Escape)
        {
            _who = -2;
            GetViewport().SetInputAsHandled();
            return;
        }
        int order = System.Array.IndexOf(Digits, key.PhysicalKeycode);
        if (order < 0) return;
        GetViewport().SetInputAsHandled();
        Give(order);
        _who = -2;
    }

    void Give(int order)
    {
        var squad = Game.Instance.Squad;
        ITarget target = Seat?.Target;
        bool needsTarget = order is 0 or 1;
        if (needsTarget && (target == null || target.IsDestroyed || (order == 0 && target.Team == Team.Player)))
        {
            Say(order == 0 ? "Сначала возьмите цель (E / T / Q)" : "Сначала выберите, кого прикрывать (Y)");
            return;
        }
        string what = Orders[order];
        for (int i = 0; i < squad.Count; i++)
        {
            if ((_who >= 0 && _who != i) || !IsInstanceValid(squad[i])) continue;
            var pilot = squad[i];
            switch (order)
            {
                case 0: pilot.Command(AiPilot.SquadOrder.Attack, target); break;
                case 1: pilot.Command(AiPilot.SquadOrder.Defend, target); break;
                case 2: pilot.Command(AiPilot.SquadOrder.Follow); break;
                case 3:
                    pilot.HoldFire = !pilot.HoldFire;
                    what = pilot.HoldFire ? "Не стрелять" : "Огонь по готовности";
                    break;
                case 4: pilot.Command(AiPilot.SquadOrder.GoTo, null, Seat.AimPoint); break;
                case 5: pilot.Command(AiPilot.SquadOrder.Hold); break;
                case 6:
                    bool down = pilot.Order == AiPilot.SquadOrder.Shutdown;
                    pilot.Command(down ? AiPilot.SquadOrder.Follow : AiPilot.SquadOrder.Shutdown);
                    what = down ? "Запустить реактор, в строй" : "Заглушить реактор";
                    break;
                case 7: pilot.Command(AiPilot.SquadOrder.Repair); break;
            }
        }
        string who = _who < 0 ? "Всем" : $"Звено-{_who + 1}";
        Say(needsTarget ? $"{who}: {what.ToLower()} — {target.DisplayName}" : $"{who}: {what.ToLower()}");
    }

    void Say(string text)
    {
        _note = text;
        _noteLeft = NoteTime;
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        _noteLeft -= (float)delta;
        if (_who != -2 && (_menuLeft -= (float)delta) <= 0f) _who = -2;
    }

    protected override void Paint()
    {
        var squad = Game.Instance?.Squad;
        if (squad == null || squad.Count == 0) return;
        float height = squad.Count * RowHeight + 44f;
        DrawRect(new Rect2(0f, 0f, Size.X, height), Panel);
        Text("ЗВЕНО", new Vector2(8f, 15f), 12, Amber);
        for (int i = 0; i < squad.Count; i++)
        {
            var pilot = squad[i];
            if (!IsInstanceValid(pilot) || pilot.Machine == null) continue;
            float y = 32f + i * RowHeight;
            var unit = pilot.Machine;
            bool down = unit.IsDestroyed, chosen = _who == i || _who == -1;
            Color col = down ? Red : chosen ? Amber : Green;
            if (chosen) DrawRect(new Rect2(2f, y - 13f, Size.X - 4f, RowHeight), new Color(Amber, 0.12f));
            Text($"F{i + 1}", new Vector2(8f, y), 12, Amber);
            Text(unit.DisplayName.Replace($"Звено-{i + 1} ", ""), new Vector2(34f, y), 12, col);
            string doing = down ? "ПОТЕРЯН" : AiPilot.OrderNames[(int)pilot.Order] + (pilot.HoldFire ? " · НЕ СТРЕЛЯЕТ" : "");
            Text(doing, new Vector2(Size.X - 206f, y), 11, down ? Red : Dim);
            var bar = new Rect2(Size.X - 56f, y - 9f, 48f, 7f);
            DrawRect(bar, Dim, false, 1f);
            if (!down) DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * Mathf.Clamp(unit.Integrity, 0f, 1f), bar.Size.Y)), unit.Integrity > 0.4f ? Green : Red);
        }
        Text(_noteLeft > 0f ? _note : $"F1–F{squad.Count} мехвоин · F{squad.Count + 1} всё звено", new Vector2(8f, height - 6f), 11, _noteLeft > 0f ? Amber : Dim);

        if (_who == -2) return;
        // The list of orders, under the lance.
        float top = height + 6f;
        DrawRect(new Rect2(0f, top, Size.X, Orders.Length * 17f + 30f), Panel);
        Text(_who < 0 ? "ПРИКАЗ ВСЕМУ ЗВЕНУ" : $"ПРИКАЗ ЗВЕНУ-{_who + 1}", new Vector2(8f, top + 16f), 12, Amber);
        for (int i = 0; i < Orders.Length; i++)
        {
            Text($"{i + 1}", new Vector2(8f, top + 36f + i * 17f), 13, Amber);
            Text(Orders[i], new Vector2(26f, top + 36f + i * 17f), 13, Green);
        }
    }
}
