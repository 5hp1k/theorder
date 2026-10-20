using Godot;

namespace MechSim;

/// <summary>
/// The campaign's page of the main menu (scenes/ui/campaign_page.tscn): the missions in their order —
/// those done, the next one, those still closed — and the briefing of the one picked: where, what for,
/// the objectives, the lance that goes out, the pilots of the roster; the machine the player takes
/// (chosen in the campaign's hangar, unless the mission gives one). A mission already done can be
/// played again; it gives nothing new.
/// </summary>
public partial class CampaignPage : VBoxContainer
{
    /// <summary>One pilot: portrait, callsign and name, skills (and machine, in the lance).</summary>
    [Export] public PackedScene PilotRowScene;

    /// <summary>The menu starts the battle; it also opens the hangar.</summary>
    public System.Action<MissionInfo> Launch;
    public System.Action OpenHangar;

    ItemList _missions;
    Label _campaignTitle, _title, _location, _briefing, _objectives, _lanceHeader, _unit;
    TextureRect _image;
    VBoxContainer _lance, _roster;
    Button _start;
    MissionInfo _shown;

    public override void _Ready()
    {
        _missions = GetNode<ItemList>("%Missions");
        _campaignTitle = GetNode<Label>("%CampaignTitle");
        _title = GetNode<Label>("%MissionTitle");
        _location = GetNode<Label>("%Location");
        _briefing = GetNode<Label>("%Briefing");
        _objectives = GetNode<Label>("%Objectives");
        _lanceHeader = GetNode<Label>("%LanceHeader");
        _unit = GetNode<Label>("%UnitLabel");
        _image = GetNode<TextureRect>("%Image");
        _lance = GetNode<VBoxContainer>("%Lance");
        _roster = GetNode<VBoxContainer>("%Roster");
        _start = GetNode<Button>("%BtnStartMission");

        _missions.ItemSelected += index => Show(CampaignState.Campaign.Missions[index]);
        _start.Pressed += () =>
        {
            if (_shown == null) return;
            CampaignState.MarkStarted();
            Launch?.Invoke(_shown);
        };
        GetNode<Button>("%BtnCampaignHangar").Pressed += () => OpenHangar?.Invoke();
        var confirm = GetNode<ConfirmationDialog>("%Confirm");
        GetNode<Button>("%BtnNewCampaign").Pressed += () => confirm.PopupCentered(new Vector2I(460, 160));
        confirm.Confirmed += () =>
        {
            // The hangar's live loadouts are the campaign's: hand them back, start afresh, take the new ones.
            GameConfig.LeaveCampaign();
            CampaignState.NewGame();
            GameConfig.EnterCampaign();
            Refresh();
        };
    }

    /// <summary>Shows the campaign as it stands now, the next mission picked.</summary>
    public void Refresh()
    {
        CampaignState.EnsureLoaded();
        var campaign = CampaignState.Campaign;
        _missions.Clear();
        if (campaign == null)
        {
            _campaignTitle.Text = Loc.T("Кампания не найдена: ") + CampaignState.DefaultCampaign;
            _start.Disabled = true;
            return;
        }
        _campaignTitle.Text = Loc.T(campaign.Title);
        for (int i = 0; i < campaign.Missions.Length; i++)
        {
            var mission = campaign.Missions[i];
            string mark = CampaignState.IsDone(mission) ? "✓ " : CampaignState.IsOpen(mission) ? "▶ " : "   ";
            _missions.AddItem(mark + $"{i + 1}. " + Loc.T(mission.Title));
            _missions.SetItemDisabled(i, !CampaignState.IsOpen(mission));
        }
        var pick = CampaignState.Next ?? (campaign.Missions.Length > 0 ? campaign.Missions[^1] : null);
        if (pick != null) _missions.Select(campaign.IndexOf(pick));
        Show(pick);
    }

    void Show(MissionInfo mission)
    {
        _shown = mission;
        var campaign = CampaignState.Campaign;
        bool fresh = !CampaignState.Started;
        if (mission == null)
        {
            _title.Text = Loc.T(CampaignState.Finished ? "Кампания пройдена" : "");
            _location.Text = _briefing.Text = _objectives.Text = "";
            _image.Visible = false;
            _start.Disabled = true;
            return;
        }
        _title.Text = Loc.T(mission.Title);
        _location.Text = Loc.T(mission.Location);
        _image.Texture = mission.BriefingImage;
        _image.Visible = mission.BriefingImage != null;
        // Before the first mission, the campaign's own word goes first.
        _briefing.Text = Loc.T(fresh && campaign.Description.Length > 0 ? campaign.Description + "\n\n" + mission.Briefing : mission.Briefing);
        var lines = new System.Collections.Generic.List<string>();
        foreach (string objective in mission.Objectives) lines.Add("• " + Loc.T(objective));
        _objectives.Text = string.Join("\n", lines);

        Clear(_lance);
        _lanceHeader.Visible = mission.Lance.Length > 0;
        foreach (var member in mission.Lance)
            if (member?.Pilot != null)
                AddPilot(_lance, member.Pilot, Catalog.Find(Catalog.PlayerUnits, member.UnitId).Name);
        Clear(_roster);
        foreach (var pilot in CampaignState.Roster()) AddPilot(_roster, pilot, null);

        bool forced = mission.PlayerUnit.Length > 0;
        string unit = Catalog.Find(Catalog.PlayerUnits, forced ? mission.PlayerUnit : GameConfig.MechId).Name;
        _unit.Text = Loc.T(forced ? $"Машина по заданию: {unit}" : $"Ваша машина: {unit} (выбирается в ангаре кампании)");
        _start.Disabled = !CampaignState.IsOpen(mission);
        _start.Text = Loc.T(fresh ? "Начать кампанию" : CampaignState.IsDone(mission) ? "Переиграть миссию" : "Начать миссию");
    }

    void AddPilot(VBoxContainer list, PilotProfile pilot, string machine)
    {
        var row = PilotRowScene.Instantiate<Control>();
        list.AddChild(row);
        var portrait = row.GetNode<TextureRect>("Portrait");
        portrait.Texture = pilot.Portrait;
        portrait.Visible = pilot.Portrait != null;
        string name = pilot.Callsign.Length > 0 && pilot.Name.Length > 0 ? $"«{pilot.Callsign}» — {pilot.Name}" : pilot.Label;
        if (machine != null) name += $", {machine}";
        row.GetNode<Label>("Words/Name").Text = Loc.T(name);
        var skills = new System.Collections.Generic.List<string>();
        int[] values = pilot.Skills;
        for (int i = 0; i < values.Length; i++) skills.Add($"{PilotProfile.SkillNames[i]} {values[i]}");
        row.GetNode<Label>("Words/Info").Text = Loc.T(string.Join("  ·  ", skills));
    }

    static void Clear(Node list)
    {
        foreach (Node child in list.GetChildren())
        {
            list.RemoveChild(child);
            child.QueueFree();
        }
    }
}
