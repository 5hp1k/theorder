using Godot;

namespace MechSim;

/// <summary>
/// The main menu's page of the player's own missions (scenes/ui/missions_page.tscn): the list from the
/// library, the chosen one's briefing, and play, edit, delete, export, import, and a new one in the editor.
/// </summary>
public partial class MissionsPage : VBoxContainer
{
    /// <summary>Set by the menu: starts the game with the mission ready in CampaignRun.</summary>
    public System.Action Launch;

    ItemList _list;
    Label _title, _about;
    RichTextLabel _briefing;
    System.Collections.Generic.List<MissionDoc> _docs = new();

    MissionDoc Chosen => _list.GetSelectedItems() is { Length: > 0 } s && s[0] < _docs.Count ? _docs[s[0]] : null;

    public override void _Ready()
    {
        _list = GetNode<ItemList>("%MyMissions");
        _title = GetNode<Label>("%MyTitle");
        _about = GetNode<Label>("%MyAbout");
        _briefing = GetNode<RichTextLabel>("%MyBriefing");
        _list.ItemSelected += _ => Show(Chosen);
        _list.ItemActivated += _ => Play();
        GetNode<Button>("%BtnMyPlay").Pressed += Play;
        GetNode<Button>("%BtnMyEdit").Pressed += () => Edit(Chosen);
        GetNode<Button>("%BtnMyNew").Pressed += () => Edit(null);
        GetNode<Button>("%BtnMyDelete").Pressed += () =>
        {
            if (Chosen is not { } doc) return;
            var ask = GetNode<ConfirmationDialog>("%MyDeleteDialog");
            ask.DialogText = Loc.T("Удалить миссию «") + doc.Title + "»?";
            ask.PopupCentered();
        };
        GetNode<ConfirmationDialog>("%MyDeleteDialog").Confirmed += () =>
        {
            if (Chosen is { } doc) MissionLibrary.Delete(doc.Id);
            Refresh();
        };
        GetNode<Button>("%BtnMyExport").Pressed += () => { if (Chosen != null) GetNode<FileDialog>("%MyExportDialog").PopupCentered(); };
        GetNode<Button>("%BtnMyImport").Pressed += () => GetNode<FileDialog>("%MyImportDialog").PopupCentered();
        GetNode<FileDialog>("%MyExportDialog").FileSelected += path => { if (Chosen is { } doc) MissionLibrary.Export(doc, path); };
        GetNode<FileDialog>("%MyImportDialog").FileSelected += path =>
        {
            var doc = MissionLibrary.Import(path);
            Refresh();
            if (doc == null) { _title.Text = Loc.T("Это не файл миссии."); return; }
            int i = _docs.FindIndex(d => d.Id == doc.Id);
            if (i >= 0) { _list.Select(i); Show(doc); }
        };
        VisibilityChanged += () => { if (Visible) Refresh(); };
        Refresh();
    }

    public void Refresh()
    {
        _docs = MissionLibrary.All();
        _list.Clear();
        foreach (var doc in _docs) _list.AddItem(doc.Title.Length > 0 ? doc.Title : Loc.T("Без названия"));
        if (_docs.Count > 0) _list.Select(0);
        Show(Chosen);
    }

    void Show(MissionDoc doc)
    {
        bool has = doc != null;
        foreach (string b in new[] { "%BtnMyPlay", "%BtnMyEdit", "%BtnMyDelete", "%BtnMyExport" }) GetNode<Button>(b).Disabled = !has;
        if (!has)
        {
            _title.Text = Loc.T("Своих миссий пока нет");
            _about.Text = Loc.T("Создайте миссию в редакторе или импортируйте файл.");
            _briefing.Text = "";
            return;
        }
        _title.Text = doc.Title;
        _about.Text = $"{Loc.T(MissionLibrary.TypeName(doc.Type))} · {Loc.T(Catalog.Find(Catalog.Maps, doc.Map).Name)}" + (doc.Author.Length > 0 ? $" · {doc.Author}" : "") + (doc.Location.Length > 0 ? $"\n{doc.Location}" : "");
        var text = new System.Text.StringBuilder(doc.Briefing);
        var info = MissionBuilder.Info(doc);
        if (info.Objectives.Length > 0)
        {
            text.Append("\n\n").Append(Loc.T("Задачи:"));
            foreach (string o in info.Objectives) text.Append("\n • ").Append(o);
        }
        _briefing.Text = text.ToString();
    }

    void Play()
    {
        if (Chosen is not { } doc) return;
        CampaignRun.Custom = doc.Copy();
        CampaignRun.Mission = MissionBuilder.Info(doc);
        CampaignRun.FromEditor = false;
        Launch?.Invoke();
    }

    void Edit(MissionDoc doc)
    {
        GameConfig.Save();
        MissionEditor.Opening = doc ?? MissionLibrary.Create("destroy", "desert");
        GetTree().ChangeSceneToFile(MissionEditor.ScenePath);
    }
}
