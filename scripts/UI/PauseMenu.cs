using Godot;

namespace MechSim;

/// <summary>In-battle pause panel (Esc), also shown when the clock runs out. The buttons are in game.tscn;
/// "Настройки" swaps them for the settings page (settings_panel.tscn), "Группы оружия" for the page where
/// each weapon is put on one of the six triggers (a row of weapon_group_row.tscn per weapon).</summary>
public partial class PauseMenu : Control
{
    /// <summary>One weapon's row on the weapon groups page: its name and six buttons G1..G6.</summary>
    [Export] public PackedScene GroupRowScene;

    Label _title, _tally;
    Button _resume;
    Control _buttons, _settings, _groups;
    VBoxContainer _groupList;

    public override void _Ready()
    {
        _title = GetNode<Label>("%PauseTitle");
        _tally = GetNode<Label>("%PauseTally");
        _resume = GetNode<Button>("%BtnResume");
        _resume.Pressed += () => Game.Instance.SetPaused(false);
        GetNode<Button>("%BtnRestart").Pressed += () =>
        {
            GetTree().Paused = false;
            LoadingScreen.Start(GetTree());
        };
        GetNode<Button>("%BtnMenu").Pressed += () => Game.Instance.QuitToMenu();
        // Settings: the same page as in the main menu, shown in place of the buttons.
        _buttons = GetNode<Control>("Center/Box");
        _settings = GetNode<Control>("%SettingsBox");
        _groups = GetNode<Control>("%GroupsBox");
        _groupList = GetNode<VBoxContainer>("%GroupList");
        GetNode<Button>("%BtnSettings").Pressed += () => ShowPage(_settings);
        GetNode<Button>("%BtnSettingsBack").Pressed += () => ShowPage(_buttons);
        GetNode<Button>("%BtnGroups").Pressed += () => ShowPage(_groups);
        GetNode<Button>("%BtnGroupsBack").Pressed += () => ShowPage(_buttons);
        VisibilityChanged += Refresh;
    }

    void ShowPage(Control page)
    {
        _buttons.Visible = page == _buttons;
        _settings.Visible = page == _settings;
        _groups.Visible = page == _groups;
        if (page == _groups) FillGroups();
    }

    void Refresh()
    {
        ShowPage(_buttons);
        var game = Game.Instance;
        bool debrief = game is { MissionOver: true };
        bool over = debrief || game is { TimeUp: true };
        _title.Text = Loc.T(debrief ? (game.MissionWon ? "МИССИЯ ВЫПОЛНЕНА" : "МИССИЯ ПРОВАЛЕНА") : over ? "ВРЕМЯ ВЫШЛО" : "ПАУЗА");
        // When the time is up: what the player's side, the player and each lancemate destroyed.
        // After a mission: the debriefing, what was given for it, and the tally.
        _tally.Visible = over;
        if (over) _tally.Text = debrief ? Debriefing(game) : Loc.T(Score.Summary());
        _resume.Visible = !over;
        GetNode<Button>("%BtnRestart").Text = Loc.T(debrief ? "Переиграть миссию" : "Начать заново");
        // In a network game only the host starts the battle again (and takes everyone along).
        GetNode<Button>("%BtnRestart").Visible = !Net.Online || Net.IsHost;
        string back = CampaignRun.FromEditor ? "В редактор" : CampaignRun.Custom != null ? "К своим миссиям" : "К кампании";
        GetNode<Button>("%BtnMenu").Text = Loc.T(debrief ? (game.MissionWon && CampaignRun.Custom == null ? "Дальше" : back) : CampaignRun.FromEditor ? "В редактор" : "В главное меню");
        // Only something with weapons has groups to set.
        GetNode<Control>("%BtnGroups").Visible = !over && Game.Cockpit?.Weapons is { Weapons.Count: > 0 };
    }

    static string Debriefing(Game game)
    {
        var info = game.MissionInfo;
        var text = new System.Text.StringBuilder();
        if (!game.MissionWon && Mission.Current is { Reason.Length: > 0 } mission) text.Append(Loc.T(mission.Reason)).Append("\n\n");
        if (game.MissionWon && info.Debriefing.Length > 0) text.Append(Loc.T(info.Debriefing)).Append("\n\n");
        if (game.Rewarded)
        {
            var given = new System.Collections.Generic.List<string>();
            foreach (string id in info.GrantUnits) given.Add(Loc.T(Catalog.Find(Catalog.PlayerUnits, id).Name));
            foreach (string id in info.GrantWeapons) given.Add(Loc.T(Catalog.Find(Catalog.Weapons, id).Name));
            if (given.Count > 0) text.Append(Loc.T("В ангар поступило: ")).Append(string.Join(", ", given)).Append('\n');
            var pilots = new System.Collections.Generic.List<string>();
            foreach (var pilot in info.GrantPilots)
                if (pilot != null) pilots.Add(pilot.Label);
            if (pilots.Count > 0) text.Append(Loc.T("В звено зачислены: ")).Append(string.Join(", ", pilots)).Append('\n');
            if (given.Count + pilots.Count > 0) text.Append('\n');
        }
        text.Append(Loc.T(Score.Summary()));
        return text.ToString();
    }

    /// <summary>A row for every weapon the player's machine carries, its group pressed in.</summary>
    void FillGroups()
    {
        foreach (Node row in _groupList.GetChildren())
        {
            _groupList.RemoveChild(row);
            row.QueueFree();
        }
        if (Game.Cockpit?.Weapons is not { } system || GroupRowScene == null) return;
        foreach (Weapon weapon in system.Weapons)
        {
            var row = GroupRowScene.Instantiate<Control>();
            _groupList.AddChild(row);
            row.GetNode<Label>("Name").Text = Loc.T(weapon.DisplayName);
            var picks = new ButtonGroup();
            for (int g = 1; g <= WeaponSystem.GroupCount; g++)
            {
                var button = row.GetNodeOrNull<Button>($"Groups/G{g}");
                if (button == null) continue;
                button.ButtonGroup = picks;
                button.ButtonPressed = weapon.Group == g;
                int group = g;
                button.Pressed += () => Regroup(weapon, group);
            }
        }
    }

    static void Regroup(Weapon weapon, int group)
    {
        if (!IsInstanceValid(weapon) || weapon.Group == group) return;
        weapon.Group = group;
        if (Game.PlayerUnit is not Node unit) return;
        // Kept for the next battle in this machine, as if chosen in the hangar…
        if (Catalog.IdByPath(unit.SceneFilePath) == GameConfig.MechId && Hardpoint.Of(unit).Count > 0)
        {
            GameConfig.Loadouts[GameConfig.MechId] = Hardpoint.Current(unit);
            GameConfig.Save();
        }
        // …and the other computers of a network game fire the same weapons on the same buttons.
        NetLink.I?.SendGroups(unit);
    }
}
