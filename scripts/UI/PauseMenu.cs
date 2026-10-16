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
        bool over = Game.Instance is { TimeUp: true };
        _title.Text = Loc.T(over ? "ВРЕМЯ ВЫШЛО" : "ПАУЗА");
        // When the time is up: what the player's side, the player and each lancemate destroyed.
        _tally.Visible = over;
        if (over) _tally.Text = Loc.T(Score.Summary());
        _resume.Visible = !over;
        // Only something with weapons has groups to set.
        GetNode<Control>("%BtnGroups").Visible = !over && Game.Cockpit?.Weapons is { Weapons.Count: > 0 };
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
