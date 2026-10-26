using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// The mission editor (scenes/editor/mission_editor.tscn), in the manner of Operation Flashpoint's and
/// ArmA's: the map as it is in the game, flown over with a free camera, and on it the mission — the
/// player's start, machines of either side (each with its type, pilot, weapons and paint), objectives,
/// triggers, radio lines, cutscenes shot from the editor's own camera, endings — wired to one another by
/// lists of steps. A mission is a <see cref="MissionDoc"/>: saved in the player's library, exported and
/// imported as one file, tried out at once with "Проверить" (back here when it ends).
///
/// The panels: on the left what can be added and a list of everything in the mission; on the right the
/// properties of what is selected (MissionEditor.Inspector.cs); the dialogs — the mission's settings,
/// characters, weapons, paint — in MissionEditor.Dialogs.cs.
/// </summary>
public partial class MissionEditor : Node3D
{
    public const string ScenePath = "res://scenes/editor/mission_editor.tscn";
    const string MenuScene = "res://scenes/ui/main_menu.tscn";
    const string GameScene = "res://scenes/game.tscn";

    /// <summary>The mission to open when the editor comes up (null: the last one edited, or a new one).</summary>
    public static MissionDoc Opening;

    MissionDoc _doc;
    bool _dirty;
    string _mapLoaded = "";
    EditorCamera _camera;
    Node3D _world, _gizmos;
    Label _title, _status;
    Tree _objects;

    // What is selected: one of the mission's elements (a UnitDoc, an ObjectiveDoc…, the PlayerDoc), and
    // for a cutscene the shot.
    object _sel;
    int _shot = -1;
    // What a click on the map will put there ("unit", "objective:reach", "trigger:area", "player", "shot").
    string _placing;
    bool _dragging, _rotating;
    readonly Dictionary<object, Node3D> _marks = new();
    // What the mouse is over, and how each mark is lit now (0 — not, 1 — under the mouse, 2 — selected).
    object _hover;
    readonly Dictionary<object, int> _lit = new();
    readonly Dictionary<object, Aabb> _bounds = new();
    Node3D _selGizmo;
    // New props face the way the last one was turned (a row of walls goes on as it was begun).
    float _lastPropYaw;
    static readonly Material Selected = GD.Load<Material>("res://materials/editor_selected.tres");
    static readonly Material Hovered = GD.Load<Material>("res://materials/editor_hover.tres");

    public override void _Ready()
    {
        GameConfig.EnsureLoaded();
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _camera = GetNode<EditorCamera>("Camera");
        _world = GetNode<Node3D>("World");
        _gizmos = GetNode<Node3D>("Gizmos");
        _title = GetNode<Label>("%Title");
        _status = GetNode<Label>("%Status");
        _objects = GetNode<Tree>("%Objects");

        GetNode<Button>("%BtnNew").Pressed += AskNew;
        GetNode<Button>("%BtnOpen").Pressed += AskOpen;
        GetNode<Button>("%BtnSave").Pressed += Save;
        GetNode<Button>("%BtnExport").Pressed += () => GetNode<FileDialog>("%ExportDialog").PopupCentered();
        GetNode<Button>("%BtnImport").Pressed += () => GetNode<FileDialog>("%ImportDialog").PopupCentered();
        GetNode<Button>("%BtnSettings").Pressed += OpenSettings;
        GetNode<Button>("%BtnPilots").Pressed += OpenPilots;
        GetNode<Button>("%BtnTest").Pressed += TryOut;
        GetNode<Button>("%BtnExit").Pressed += () =>
        {
            if (_dirty) GetNode<ConfirmationDialog>("%ExitDialog").PopupCenteredClamped(new Vector2I(420, 140), 0.9f);
            else Leave();
        };
        var exit = GetNode<ConfirmationDialog>("%ExitDialog");
        exit.Confirmed += () => { Save(); Leave(); };
        exit.AddButton(Loc.T("Выйти без сохранения"), true, "discard");
        exit.CustomAction += action => { if (action == "discard") Leave(); };
        GetNode<FileDialog>("%ExportDialog").FileSelected += path =>
            Say(MissionLibrary.Export(_doc, path) ? Loc.T("Миссия сохранена в файл: ") + path : Loc.T("Не удалось записать файл"));
        GetNode<FileDialog>("%ImportDialog").FileSelected += path =>
        {
            var doc = MissionLibrary.Import(path);
            if (doc == null) Message(Loc.T("Это не файл миссии."));
            else
            {
                Load(doc);
                Say(Loc.T("Миссия загружена: ") + doc.Title);
            }
        };
        _objects.ItemSelected += () =>
        {
            if (_objects.GetSelected()?.GetMetadata(0).Obj is Holder { Element: { } obj } && obj != _sel) Select(obj, -1);
        };
        SetupPalette();
        SetupDialogs();
        // (The settings, the language among them, may have been read only just now.)
        if (Loc.English)
        {
            foreach (var control in Geo.Collect<Control>(GetNode("UI"))) Loc.Apply(control);
            foreach (var window in Geo.Collect<Window>(GetNode("UI"))) window.Title = Loc.T(window.Title);
            foreach (var dialog in Geo.Collect<AcceptDialog>(GetNode("UI")))
            {
                dialog.OkButtonText = Loc.T(dialog.OkButtonText);
                dialog.DialogText = Loc.T(dialog.DialogText);
            }
            foreach (var dialog in Geo.Collect<ConfirmationDialog>(GetNode("UI"))) dialog.CancelButtonText = Loc.T(dialog.CancelButtonText);
        }

        var kept = MissionLibrary.All();
        var doc = Opening ?? (kept.Count > 0 ? kept[0] : null);
        Opening = null;
        Load(doc ?? MissionLibrary.Create("destroy", "desert"));
    }

    // ── The mission as a whole ──────────────────────────────────

    void Load(MissionDoc doc)
    {
        _doc = doc;
        _dirty = false;
        _sel = null;
        _shot = -1;
        _placing = null;
        LoadMap(true);
        RebuildMarks();
        RefreshObjects();
        ShowInspector();
        RefreshTitle();
    }

    void Changed(bool rebuildList = false)
    {
        _dirty = true;
        RefreshTitle();
        if (rebuildList) RefreshObjects();
    }

    void RefreshTitle() => _title.Text = (_doc.Title.Length > 0 ? _doc.Title : Loc.T("Без названия")) + " — " + Loc.T(MissionLibrary.TypeName(_doc.Type)) + (_dirty ? " *" : "");

    void Save()
    {
        MissionLibrary.Save(_doc);
        _dirty = false;
        RefreshTitle();
        Say(Loc.T("Сохранено"));
    }

    void Leave()
    {
        CampaignRun.BackToMissions = true;
        GetTree().ChangeSceneToFile(MenuScene);
    }

    /// <summary>Plays the mission now; when it ends, the editor comes back with it.</summary>
    void TryOut()
    {
        Save();
        CampaignRun.Custom = _doc.Copy();
        CampaignRun.Mission = MissionBuilder.Info(_doc);
        CampaignRun.FromEditor = true;
        GameConfig.Save();
        LoadingScreen.Start(GetTree(), GameScene);
    }

    void Say(string text)
    {
        _status.Text = text;
        GetTree().CreateTimer(4.0).Timeout += () => { if (IsInstanceValid(_status) && _status.Text == text) _status.Text = ""; };
    }

    void Message(string text)
    {
        var dialog = GetNode<AcceptDialog>("%Message");
        dialog.DialogText = text;
        dialog.PopupCentered();
    }

    // ── The map ─────────────────────────────────────────────────

    /// <summary>Puts the mission's map up (with its hour and weather); keeps the camera unless it is the first time.</summary>
    void LoadMap(bool resetCamera)
    {
        foreach (Node child in _world.GetChildren()) child.QueueFree();
        // (The map's lighting takes the hour from the mission being played: this one, while it comes up.)
        var was = CampaignRun.Mission;
        CampaignRun.Mission = new MissionInfo { TimeOfDay = _doc.TimeOfDay };
        var map = GD.Load<PackedScene>(Catalog.Find(Catalog.Maps, _doc.Map).Path).Instantiate<Node3D>();
        Game.SetWeather(map, _doc.Weather);
        map.AddChild(new BoundsWall { Name = "BoundsWall" });
        _world.AddChild(map);
        CampaignRun.Mission = was;
        _mapLoaded = _doc.Map;
        if (resetCamera) _camera.Focus(new Vector3(_doc.Player.X, Ground(_doc.Player.X, _doc.Player.Z), _doc.Player.Z), 160f);
        // The ground's height is known once the terrain has been made: put the marks on it then.
        Callable.From(RebuildMarks).CallDeferred();
    }

    float Ground(float x, float z) => MapTerrain.Instance is { } t && IsInstanceValid(t) ? t.HeightAt(x, z) : 0f;

    /// <summary>The point of the ground under the mouse, if the mouse is over the ground.</summary>
    bool GroundUnder(Vector2 screen, out Vector3 point)
    {
        point = default;
        Vector3 from = _camera.ProjectRayOrigin(screen), dir = _camera.ProjectRayNormal(screen);
        // Marched along the ray until it is under the ground, then narrowed down.
        float step = 3f, t = 0f, prev = 0f;
        for (int i = 0; i < 2500; i++, t += step)
        {
            Vector3 p = from + dir * t;
            if (p.Y < Ground(p.X, p.Z))
            {
                float lo = prev, hi = t;
                for (int k = 0; k < 16; k++)
                {
                    float mid = (lo + hi) * 0.5f;
                    Vector3 m = from + dir * mid;
                    if (m.Y < Ground(m.X, m.Z)) hi = mid; else lo = mid;
                }
                point = from + dir * hi;
                return true;
            }
            prev = t;
            step = Mathf.Min(step * 1.02f, 12f);
        }
        return false;
    }

    // ── Marks on the map ────────────────────────────────────────

    static readonly Color Ours = new(0.35f, 0.65f, 1f), Theirs = new(1f, 0.35f, 0.3f), Goal = new(1f, 0.82f, 0.3f), Trig = new(0.75f, 0.45f, 1f), Film = new(0.4f, 1f, 0.75f);

    void RebuildMarks()
    {
        foreach (var mark in _marks.Values) mark.QueueFree();
        _marks.Clear();
        _lit.Clear();
        _bounds.Clear();
        Mark(_doc.Player);
        foreach (var u in _doc.Units) Mark(u);
        foreach (var p in _doc.Props) Mark(p);
        foreach (var o in _doc.Objectives) Mark(o);
        foreach (var t in _doc.Triggers) Mark(t);
        foreach (var c in _doc.Cutscenes) Mark(c);
        Highlight();
    }

    void Remark(object element)
    {
        if (_marks.Remove(element, out var old)) old.QueueFree();
        _lit.Remove(element);
        _bounds.Remove(element);
        Mark(element);
        Highlight();
    }

    /// <summary>The element's mark: a machine's model, a ring for a place, the path of a cutscene's camera.</summary>
    void Mark(object element)
    {
        var mark = new Node3D();
        switch (element)
        {
            case PlayerDoc p:
            {
                string id = p.UnitId.Length > 0 ? p.UnitId : GameConfig.MechId;
                mark.AddChild(UnitPreview.Build(id, p.UnitId.Length > 0 ? p.Loadout : GameConfig.LoadoutFor(id), null, false));
                mark.AddChild(Ring(6f, Ours));
                mark.AddChild(Tag(Loc.T("ИГРОК"), Ours, 14f));
                Place(mark, p.X, p.Z, p.Yaw);
                break;
            }
            case UnitDoc u:
            {
                mark.AddChild(UnitPreview.Build(u.UnitId, u.Loadout, MissionBuilder.PaintOf(u.Paint), u.Team != "player"));
                Color c = u.Team == "player" ? Ours : Theirs;
                mark.AddChild(Ring(5f, c));
                string who = _doc.Pilot(u.PilotId)?.Label;
                mark.AddChild(Tag(u.Name + (who != null ? $" ({who})" : "") + (u.SpawnAtStart ? "" : Loc.T(" · позже")), c, 13f));
                Place(mark, u.X, u.Z, u.Yaw);
                break;
            }
            case PropDoc pr:
                if (Catalog.Prop(pr.Kind) is { } kind && ResourceLoader.Exists(kind.Path))
                    mark.AddChild(GD.Load<PackedScene>(kind.Path).Instantiate<Node3D>());
                else
                    mark.AddChild(Pole(Colors.Gray));
                Place(mark, pr.X, pr.Z, pr.Yaw);
                break;
            case ObjectiveDoc o:
                if (o.Kind == "reach") mark.AddChild(Ring(o.Radius, Goal, true));
                else mark.AddChild(Pole(Goal));
                mark.AddChild(Tag(Loc.T("Цель: ") + o.Name, Goal, 8f));
                Place(mark, o.X, o.Z, 0f);
                break;
            case TriggerDoc t:
                if (t.Kind == "area") mark.AddChild(Ring(t.Radius, Trig, true));
                else mark.AddChild(Pole(Trig));
                mark.AddChild(Tag(Loc.T("Триггер: ") + t.Name, Trig, 8f));
                Place(mark, t.X, t.Z, 0f);
                break;
            case CutsceneDoc c:
                for (int i = 0; i < c.Shots.Count; i++)
                {
                    var shot = c.Shots[i];
                    var cam = CameraMark(Film);
                    cam.Transform = MissionBuilder.ShotTransform(shot);
                    cam.SetMeta("shot", i);
                    mark.AddChild(cam);
                    var tag = Tag($"{c.Name} · {i + 1}", Film, 1.2f);
                    tag.Position = new Vector3(shot.X, shot.Y, shot.Z);
                    mark.AddChild(tag);
                }
                if (c.Shots.Count > 1) mark.AddChild(Path(c.Shots, Film));
                break;
            default:
                mark.QueueFree();
                return;
        }
        _gizmos.AddChild(mark);
        _marks[element] = mark;
    }

    void Place(Node3D mark, float x, float z, float yaw) => mark.Transform = new Transform3D(new Basis(Vector3.Up, yaw), new Vector3(x, Ground(x, z), z));

    static StandardMaterial3D Flat(Color c, float alpha = 1f) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        AlbedoColor = new Color(c, alpha),
        Transparency = alpha < 1f ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        NoDepthTest = false,
    };

    static Node3D Ring(float radius, Color c, bool area = false)
    {
        var holder = new Node3D { Name = "Ring" };
        holder.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = Mathf.Max(radius - 0.6f, 0.1f), OuterRadius = radius, Rings = 48, RingSegments = 6 },
            MaterialOverride = Flat(c), Position = Vector3.Up * 0.5f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        if (area)
            holder.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = 8f, RadialSegments = 48, CapTop = false, CapBottom = false },
                MaterialOverride = Flat(c, 0.12f), Position = Vector3.Up * 4f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        return holder;
    }

    static Node3D Pole(Color c) => new MeshInstance3D
    {
        Name = "Ring",
        Mesh = new CylinderMesh { TopRadius = 0.4f, BottomRadius = 0.4f, Height = 8f, RadialSegments = 8 },
        MaterialOverride = Flat(c), Position = Vector3.Up * 4f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };

    static Label3D Tag(string text, Color c, float height) => new()
    {
        Name = "Tag",
        Text = text,
        Modulate = c,
        OutlineModulate = Colors.Black,
        OutlineSize = 10,
        FontSize = 36,
        PixelSize = 0.0009f,
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        NoDepthTest = true,
        FixedSize = true,
        Position = Vector3.Up * height,
    };

    static Node3D CameraMark(Color c)
    {
        var cam = new Node3D { Name = "Shot" };
        cam.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.9f, 0.7f, 1.2f) }, MaterialOverride = Flat(c), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        cam.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.15f, BottomRadius = 0.5f, Height = 0.8f, RadialSegments = 8 },
            MaterialOverride = Flat(c), Transform = new Transform3D(new Basis(Vector3.Right, Mathf.Pi / 2f), new Vector3(0f, 0f, -0.95f)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        return cam;
    }

    static MeshInstance3D Path(List<ShotDoc> shots, Color c)
    {
        var mesh = new ImmediateMesh();
        mesh.SurfaceBegin(Mesh.PrimitiveType.LineStrip);
        foreach (var s in shots) mesh.SurfaceAddVertex(new Vector3(s.X, s.Y, s.Z));
        mesh.SurfaceEnd();
        return new MeshInstance3D { Mesh = mesh, MaterialOverride = Flat(c), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    }

    /// <summary>
    /// What is selected glows and has a pulsing ring, a beam and a marker over it (and an arrow the way it
    /// faces, if it can be turned); what is under the mouse glows faintly. Labels follow suit.
    /// </summary>
    void Highlight()
    {
        foreach (var (element, mark) in _marks)
        {
            int state = element == _sel ? 2 : element == _hover ? 1 : 0;
            if (_lit.TryGetValue(element, out int was) && was == state) continue;
            _lit[element] = state;
            foreach (var geometry in Geo.Collect<GeometryInstance3D>(mark))
            {
                if (geometry is Label3D tag)
                {
                    tag.OutlineModulate = state == 2 ? new Color(0.95f, 0.7f, 0.1f) : state == 1 ? new Color(0.35f, 0.45f, 0.55f) : Colors.Black;
                    tag.FontSize = state == 2 ? 46 : 36;
                }
                else if (geometry.Name != "Ring" && geometry.GetParent()?.Name != "Ring")
                    geometry.MaterialOverlay = state == 2 ? Selected : state == 1 ? Hovered : null;
            }
        }

        _selGizmo?.QueueFree();
        _selGizmo = null;
        if (_sel == null || WhereIs(_sel, _shot) == null) return;
        var (radius, height) = SizeOf(_sel);
        var bright = new Color(1f, 0.85f, 0.25f);
        _selGizmo = new Node3D { Name = "Selection" };
        var ring = new MeshInstance3D
        {
            Name = "Pulse",
            Mesh = new TorusMesh { InnerRadius = radius, OuterRadius = radius + Mathf.Clamp(radius * 0.06f, 0.35f, 2f), Rings = 64, RingSegments = 6 },
            MaterialOverride = OnTop(bright, 0.95f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Position = Vector3.Up * 0.4f,
        };
        _selGizmo.AddChild(ring);
        _selGizmo.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.6f, Height = 70f, RadialSegments = 12, CapTop = false, CapBottom = false },
            MaterialOverride = Flat(bright, 0.28f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Position = Vector3.Up * 35f,
        });
        _selGizmo.AddChild(new MeshInstance3D
        {
            Name = "Marker",
            Mesh = new CylinderMesh { TopRadius = 1.3f, BottomRadius = 0f, Height = 2.4f, RadialSegments = 4 },
            MaterialOverride = OnTop(bright, 1f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Position = Vector3.Up * (height + 4f),
        });
        if (Turnable(_sel))
        {
            // The way it faces: a shaft out of the ring and a head on it.
            var facing = new Node3D { Name = "Facing", Position = Vector3.Up * 0.5f };
            float length = radius + 4f;
            facing.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.5f, 0.15f, length) },
                MaterialOverride = OnTop(bright, 0.9f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Position = new Vector3(0f, 0f, -length * 0.5f),
            });
            facing.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0f, BottomRadius = 1.4f, Height = 2.6f, RadialSegments = 3 },
                MaterialOverride = OnTop(bright, 0.9f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Transform = new Transform3D(new Basis(Vector3.Right, -Mathf.Pi / 2f), new Vector3(0f, 0f, -length - 1.2f)),
            });
            _selGizmo.AddChild(facing);
        }
        _gizmos.AddChild(_selGizmo);
        FollowSelection(0f);
    }

    /// <summary>The selection's ring and marker move with what is selected, and breathe.</summary>
    void FollowSelection(float time)
    {
        if (_selGizmo == null || WhereIs(_sel, _shot) is not { } at) return;
        bool shot = _sel is CutsceneDoc;
        _selGizmo.Position = shot ? at : at - Vector3.Up * 4f;
        float beat = 1f + 0.06f * Mathf.Sin(time * 5f);
        _selGizmo.GetNode<Node3D>("Pulse").Scale = new Vector3(beat, 1f, beat);
        var marker = _selGizmo.GetNode<Node3D>("Marker");
        marker.Position = new Vector3(0f, SizeOf(_sel).height + 4f + 0.8f * Mathf.Sin(time * 3f), 0f);
        marker.Rotation = new Vector3(0f, time * 1.5f, 0f);
        if (_selGizmo.GetNodeOrNull<Node3D>("Facing") is { } facing) facing.Rotation = new Vector3(0f, YawOf(_sel, _shot), 0f);
    }

    public override void _Process(double delta) => FollowSelection((float)(Time.GetTicksMsec() / 1000.0));

    static StandardMaterial3D OnTop(Color c, float alpha)
    {
        var m = Flat(c, alpha);
        m.NoDepthTest = true;
        m.RenderPriority = 2;
        return m;
    }

    /// <summary>How wide and how tall the selection's ring and marker are for an element.</summary>
    (float radius, float height) SizeOf(object element)
    {
        switch (element)
        {
            case PlayerDoc: return (7.5f, 14f);
            case UnitDoc: return (6.5f, 14f);
            case ObjectiveDoc o: return (o.Kind == "reach" ? o.Radius + 1f : 3f, 9f);
            case TriggerDoc t: return (t.Kind == "area" ? t.Radius + 1f : 3f, 9f);
            case CutsceneDoc: return (2f, 2f);
            case PropDoc p:
                var box = BoundsOf(p);
                return (Mathf.Max(new Vector2(box.Position.X, box.Position.Z).Length(), new Vector2(box.End.X, box.End.Z).Length()) + 1f, box.End.Y);
        }
        return (4f, 8f);
    }

    /// <summary>A prop's box, in its own space (for picking it anywhere on it and for sizing its ring).</summary>
    Aabb BoundsOf(object element)
    {
        if (_bounds.TryGetValue(element, out var box)) return box;
        box = new Aabb(new Vector3(-1f, 0f, -1f), new Vector3(2f, 3f, 2f));
        if (_marks.TryGetValue(element, out var mark))
        {
            bool first = true;
            var inverse = mark.GlobalTransform.AffineInverse();
            foreach (var visual in Geo.Collect<VisualInstance3D>(mark))
            {
                if (visual is Label3D || !visual.IsVisibleInTree()) continue;
                var part = (inverse * visual.GlobalTransform) * visual.GetAabb();
                if (part.Size.LengthSquared() < 0.0001f) continue;
                box = first ? part : box.Merge(part);
                first = false;
            }
        }
        return _bounds[element] = box;
    }

    static bool Turnable(object element) => element is PlayerDoc or UnitDoc or PropDoc or CutsceneDoc;

    /// <summary>Where an element stands (for picking it with the mouse and for going to it).</summary>
    Vector3? WhereIs(object element, int shot = -1) => element switch
    {
        PlayerDoc p => new Vector3(p.X, Ground(p.X, p.Z) + 4f, p.Z),
        UnitDoc u => new Vector3(u.X, Ground(u.X, u.Z) + 4f, u.Z),
        PropDoc p => new Vector3(p.X, Ground(p.X, p.Z) + 4f, p.Z),
        ObjectiveDoc o => new Vector3(o.X, Ground(o.X, o.Z) + 4f, o.Z),
        TriggerDoc t => new Vector3(t.X, Ground(t.X, t.Z) + 4f, t.Z),
        CutsceneDoc c when c.Shots.Count > 0 => c.Shots[Mathf.Clamp(shot, 0, c.Shots.Count - 1)] is var s ? new Vector3(s.X, s.Y, s.Z) : null,
        _ => null,
    };

    // ── Selecting, moving, placing ──────────────────────────────

    void Select(object element, int shot)
    {
        _sel = element;
        _shot = shot;
        _placing = null;
        Highlight();
        ShowInspector();
        SelectInList();
    }

    /// <summary>What is under the mouse: the nearest element (or cutscene shot) within a few pixels.</summary>
    (object element, int shot) Pick(Vector2 screen)
    {
        object best = null;
        int bestShot = -1;
        float bestD = 28f * 28f;
        void Try(object e, Vector3 at, int shot)
        {
            if (_camera.IsPositionBehind(at)) return;
            float d = _camera.UnprojectPosition(at).DistanceSquaredTo(screen);
            if (d < bestD) { bestD = d; best = e; bestShot = shot; }
        }
        Try(_doc.Player, WhereIs(_doc.Player).Value, -1);
        foreach (var u in _doc.Units) Try(u, WhereIs(u).Value, -1);
        foreach (var o in _doc.Objectives) Try(o, WhereIs(o).Value, -1);
        foreach (var t in _doc.Triggers) Try(t, WhereIs(t).Value, -1);
        foreach (var p in _doc.Props) Try(p, WhereIs(p).Value, -1);
        foreach (var c in _doc.Cutscenes)
            for (int i = 0; i < c.Shots.Count; i++) Try(c, new Vector3(c.Shots[i].X, c.Shots[i].Y, c.Shots[i].Z), i);
        // A prop is big: anywhere on its ground counts (the last put down first, as it is on top).
        if (best == null && _doc.Props.Count > 0 && GroundUnder(screen, out var ground))
            for (int i = _doc.Props.Count - 1; i >= 0; i--)
            {
                var p = _doc.Props[i];
                if (!_marks.TryGetValue(p, out var mark)) continue;
                var local = mark.GlobalTransform.AffineInverse() * ground;
                var box = BoundsOf(p);
                if (local.X >= box.Position.X && local.X <= box.End.X && local.Z >= box.Position.Z && local.Z <= box.End.Z) return (p, -1);
            }
        return (best, bestShot);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } click)
        {
            if (click.Pressed)
            {
                if (_placing != null && GroundUnder(click.Position, out var at))
                {
                    PlaceNew(at);
                    GetViewport().SetInputAsHandled();
                    return;
                }
                var (element, shot) = Pick(click.Position);
                if (click.AltPressed && _sel != null && Turnable(_sel))
                {
                    // Alt: turned to face where the mouse goes, wherever the press was.
                    _rotating = true;
                }
                else if (element != null)
                {
                    if (element != _sel || shot != _shot) Select(element, shot);
                    // A cutscene's shot keeps its height: it is moved only from the panel.
                    _dragging = element is not CutsceneDoc;
                }
                else if (_sel != null) Select(null, -1);
                GetViewport().SetInputAsHandled();
            }
            else if (_dragging || _rotating)
            {
                _dragging = _rotating = false;
                ShowInspector();
            }
        }
        else if (@event is InputEventMouseMotion turn && _rotating && _sel != null && GroundUnder(turn.Position, out var toward))
        {
            if (WhereIs(_sel, _shot) is { } from && new Vector2(toward.X - from.X, toward.Z - from.Z).LengthSquared() > 1f)
                SetYaw(_sel, Mathf.Atan2(-(toward.X - from.X), -(toward.Z - from.Z)));
            GetViewport().SetInputAsHandled();
        }
        else if (@event is InputEventMouseMotion motion && _dragging && _sel != null && GroundUnder(motion.Position, out var to))
        {
            MoveTo(_sel, to.X, to.Z);
            GetViewport().SetInputAsHandled();
        }
        else if (@event is InputEventMouseButton { Pressed: true } wheel && (wheel.ShiftPressed || wheel.CtrlPressed)
                 && wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown && _sel != null && Turnable(_sel))
        {
            float step = Mathf.DegToRad(wheel.CtrlPressed ? 1f : 7.5f);
            Turn(_sel, wheel.ButtonIndex == MouseButton.WheelUp ? step : -step);
            GetViewport().SetInputAsHandled();
        }
        else if (@event is InputEventMouseMotion hover && !_dragging && !_rotating)
        {
            var (under, _) = Pick(hover.Position);
            if (under != _hover)
            {
                _hover = under;
                Highlight();
            }
        }
        else if (@event is InputEventKey { Pressed: true, Echo: false } key && GetViewport().GuiGetFocusOwner() is not (LineEdit or TextEdit or Godot.SpinBox))
        {
            if (key.Keycode == Key.Escape) { _placing = null; Select(null, -1); Say(""); }
            else if (key.Keycode == Key.Delete && _sel != null) Remove(_sel, _shot);
            else if (key.Keycode == Key.D && key.CtrlPressed && _sel != null) Duplicate(_sel);
            else if (key.Keycode == Key.S && key.CtrlPressed) Save();
            else if (key.Keycode == Key.F && _sel != null && WhereIs(_sel, _shot) is { } at) _camera.Focus(at);
            else if (key.Keycode is Key.R or Key.T && !key.CtrlPressed && _sel != null && Turnable(_sel)) Turn(_sel, Mathf.DegToRad(key.Keycode == Key.R ? 15f : -15f));
            else return;
            GetViewport().SetInputAsHandled();
        }
    }

    void MoveTo(object element, float x, float z)
    {
        switch (element)
        {
            case PlayerDoc p: p.X = x; p.Z = z; break;
            case UnitDoc u: u.X = x; u.Z = z; break;
            case PropDoc pr: pr.X = x; pr.Z = z; break;
            case ObjectiveDoc o: o.X = x; o.Z = z; break;
            case TriggerDoc t: t.X = x; t.Z = z; break;
            default: return;
        }
        if (_marks.TryGetValue(element, out var mark)) mark.Position = new Vector3(x, Ground(x, z), z);
        Changed();
    }

    void Turn(object element, float by) => SetYaw(element, YawOf(element, _shot) + by);

    float YawOf(object element, int shot = -1) => element switch
    {
        PlayerDoc p => p.Yaw,
        UnitDoc u => u.Yaw,
        PropDoc p => p.Yaw,
        CutsceneDoc c when c.Shots.Count > 0 => c.Shots[Mathf.Clamp(shot < 0 ? _shot : shot, 0, c.Shots.Count - 1)].Yaw,
        _ => 0f,
    };

    /// <summary>Turns an element to face that way: its mark on the map and the panel's field follow.</summary>
    void SetYaw(object element, float yaw)
    {
        yaw = Mathf.Wrap(yaw, -Mathf.Pi, Mathf.Pi);
        switch (element)
        {
            case PlayerDoc p: p.Yaw = yaw; break;
            case UnitDoc u: u.Yaw = yaw; break;
            case PropDoc pr: pr.Yaw = _lastPropYaw = yaw; break;
            case CutsceneDoc c when c.Shots.Count > 0:
                c.Shots[Mathf.Clamp(_shot, 0, c.Shots.Count - 1)].Yaw = yaw;
                Remark(c);
                break;
            default: return;
        }
        if (element is not CutsceneDoc && _marks.TryGetValue(element, out var mark)) mark.Rotation = new Vector3(0f, yaw, 0f);
        if (element == _sel && IsInstanceValid(_yawSpin)) _yawSpin?.SetValueNoSignal(Mathf.Round(Mathf.RadToDeg(yaw)));
        Changed();
    }

    // ── The palette ─────────────────────────────────────────────

    // What can be added besides machines: label, what it makes.
    static readonly (string label, string what)[] Additions =
    {
        ("Цель: уничтожить", "objective:destroy"), ("Цель: дойти", "objective:reach"),
        ("Цель: защитить", "objective:protect"), ("Цель: продержаться", "objective:survive"),
        ("Триггер: зона", "trigger:area"), ("Триггер: таймер", "trigger:timer"),
        ("Триггер: уничтожены", "trigger:destroyed"), ("Рация", "radio"),
        ("Катсцена", "cutscene"), ("Концовка", "ending"),
        ("Старт игрока", "player"),
    };

    void SetupPalette()
    {
        var kind = GetNode<OptionButton>("%UnitKind");
        foreach (var e in Catalog.PlayerUnits) kind.AddItem(Loc.T(e.Name));
        kind.Selected = 0;
        var team = GetNode<OptionButton>("%UnitTeam");
        team.AddItem(Loc.T("Враг"));
        team.AddItem(Loc.T("Свои"));
        GetNode<Button>("%BtnAddUnit").Pressed += () => StartPlacing("unit", Loc.T("Щёлкните по карте, куда поставить машину (Esc — отмена)"));
        var group = GetNode<OptionButton>("%PropGroup");
        var prop = GetNode<OptionButton>("%PropKind");
        foreach (var (name, _) in Catalog.Props) group.AddItem(Loc.T(name));
        void FillProps(long g)
        {
            prop.Clear();
            foreach (var e in Catalog.Props[g].Items) prop.AddItem(Loc.T(e.Name));
            prop.Selected = 0;
        }
        group.ItemSelected += FillProps;
        group.Selected = 0;
        FillProps(0);
        GetNode<Button>("%BtnAddProp").Pressed += () => StartPlacing("prop", Loc.T("Щёлкните по карте, куда поставить объект (Esc — отмена)"));
        var palette = GetNode<GridContainer>("%Palette");
        foreach (var (label, what) in Additions)
        {
            var button = new Button { Text = Loc.T(label), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true };
            palette.AddChild(button);
            button.Pressed += () => Add(what);
        }
    }

    void StartPlacing(string what, string hint)
    {
        _placing = what;
        Say(hint);
    }

    /// <summary>Things with a place on the map are put down with a click; the rest appear at once.</summary>
    void Add(string what)
    {
        switch (what)
        {
            case "objective:reach" or "objective:destroy" or "objective:protect" or "trigger:area" or "player":
                StartPlacing(what, Loc.T("Щёлкните по карте, где это будет (Esc — отмена)"));
                return;
        }
        // Not tied to a place: in front of the camera, where it can be found again.
        if (!GroundUnder(GetViewport().GetVisibleRect().Size * 0.5f, out var at))
            at = _camera.GlobalPosition - _camera.GlobalBasis.Z * 60f;
        _placing = what;
        PlaceNew(at);
    }

    void PlaceNew(Vector3 at)
    {
        string what = _placing ?? "";
        _placing = null;
        object made = null;
        switch (what.Split(':')[0])
        {
            case "unit":
            {
                var entry = Catalog.PlayerUnits[Mathf.Max(GetNode<OptionButton>("%UnitKind").Selected, 0)];
                bool ours = GetNode<OptionButton>("%UnitTeam").Selected == 1;
                var u = new UnitDoc { Name = _doc.FreeName(Loc.T(entry.Name)), UnitId = entry.Id, Team = ours ? "player" : "enemy", X = at.X, Z = at.Z, Yaw = _camera.Yaw + Mathf.Pi };
                _doc.Units.Add(u);
                made = u;
                // Several in a row: the button stays down until Esc.
                _placing = "unit";
                break;
            }
            case "prop":
            {
                var items = Catalog.Props[Mathf.Max(GetNode<OptionButton>("%PropGroup").Selected, 0)].Items;
                var p = new PropDoc { Kind = items[Mathf.Clamp(GetNode<OptionButton>("%PropKind").Selected, 0, items.Length - 1)].Id, X = at.X, Z = at.Z, Yaw = _lastPropYaw };
                _doc.Props.Add(p);
                made = p;
                _placing = "prop";
                break;
            }
            case "player":
                _doc.Player.X = at.X;
                _doc.Player.Z = at.Z;
                made = _doc.Player;
                break;
            case "objective":
            {
                string kind = what.Split(':')[1];
                var o = new ObjectiveDoc { Name = _doc.FreeName(Loc.T("Цель")), Kind = kind, X = at.X, Z = at.Z, Text = Loc.T(kind switch { "reach" => "Дойти до точки", "protect" => "Защитить своих", "survive" => "Продержаться", _ => "Уничтожить врага" }) };
                _doc.Objectives.Add(o);
                made = o;
                break;
            }
            case "trigger":
            {
                var t = new TriggerDoc { Name = _doc.FreeName(Loc.T("Триггер")), Kind = what.Split(':')[1], X = at.X, Z = at.Z };
                _doc.Triggers.Add(t);
                made = t;
                break;
            }
            case "radio":
            {
                var r = new RadioDoc { Name = _doc.FreeName(Loc.T("Рация")) };
                r.Lines.Add(new LineDoc { SpeakerName = Loc.T("Штаб"), Text = Loc.T("Текст реплики") });
                _doc.Radio.Add(r);
                made = r;
                break;
            }
            case "cutscene":
            {
                // (the first one opens the mission; later ones play when a step names them)
                var c = new CutsceneDoc { Name = _doc.FreeName(Loc.T("Катсцена")), AtStart = _doc.Cutscenes.Count == 0 };
                c.Shots.Add(ShotFromCamera(0f));
                _doc.Cutscenes.Add(c);
                made = c;
                break;
            }
            case "ending":
            {
                var e = new EndingDoc { Name = _doc.FreeName(Loc.T("Концовка")) };
                _doc.Endings.Add(e);
                made = e;
                break;
            }
        }
        if (made == null) return;
        Remark(made);
        Changed(true);
        string again = _placing;
        Select(made, made is CutsceneDoc ? 0 : -1);
        _placing = again;
        if (_placing == "unit") Say(Loc.T("Ещё щелчок — ещё одна машина; Esc — хватит"));
        if (_placing == "prop") Say(Loc.T("Ещё щелчок — ещё один; R / T — повернуть; Esc — хватит"));
    }

    ShotDoc ShotFromCamera(float time) => new()
    {
        X = _camera.GlobalPosition.X, Y = _camera.GlobalPosition.Y, Z = _camera.GlobalPosition.Z,
        Yaw = _camera.Yaw, Pitch = _camera.Pitch, Fov = _camera.Fov, Time = time,
    };

    void Duplicate(object element)
    {
        object made = null;
        switch (element)
        {
            case UnitDoc u:
                var json = System.Text.Json.JsonSerializer.Serialize(u);
                var copy = System.Text.Json.JsonSerializer.Deserialize<UnitDoc>(json);
                copy.Name = _doc.FreeName(StemOf(u.Name));
                copy.X += 12f;
                _doc.Units.Add(copy);
                made = copy;
                break;
            case PropDoc p:
                var twin = new PropDoc { Kind = p.Kind, X = p.X, Z = p.Z, Yaw = p.Yaw };
                // Next to it, along the way it faces sideways (a wall goes on as a wall).
                var side = new Basis(Vector3.Up, p.Yaw) * Vector3.Right * (BoundsOf(p).Size.X + 0.2f);
                twin.X += side.X;
                twin.Z += side.Z;
                _doc.Props.Add(twin);
                made = twin;
                break;
            default:
                return;
        }
        Remark(made);
        Changed(true);
        Select(made, -1);
    }

    /// <summary>A name without the number at its end ("Гончая3" → "Гончая").</summary>
    static string StemOf(string name)
    {
        int end = name.Length;
        while (end > 0 && char.IsDigit(name[end - 1])) end--;
        return end > 0 ? name[..end] : name;
    }

    /// <summary>Takes an element (or a cutscene's shot) out of the mission, and out of every list that named it.</summary>
    void Remove(object element, int shot)
    {
        if (element is PlayerDoc) return;
        if (element is CutsceneDoc c && shot >= 0 && c.Shots.Count > 1)
        {
            c.Shots.RemoveAt(shot);
            Remark(c);
            Changed();
            Select(c, Mathf.Min(shot, c.Shots.Count - 1));
            return;
        }
        string name = NameOf(element);
        switch (element)
        {
            case UnitDoc u: _doc.Units.Remove(u); break;
            case PropDoc pr: _doc.Props.Remove(pr); break;
            case ObjectiveDoc o: _doc.Objectives.Remove(o); break;
            case TriggerDoc t: _doc.Triggers.Remove(t); break;
            case RadioDoc r: _doc.Radio.Remove(r); break;
            case CutsceneDoc cs: _doc.Cutscenes.Remove(cs); break;
            case EndingDoc e: _doc.Endings.Remove(e); break;
        }
        Rename(name, null);
        if (_marks.Remove(element, out var mark)) mark.QueueFree();
        _lit.Remove(element);
        _bounds.Remove(element);
        if (_hover == element) _hover = null;
        Changed(true);
        Select(null, -1);
    }

    static string NameOf(object element) => element switch
    {
        UnitDoc u => u.Name, ObjectiveDoc o => o.Name, TriggerDoc t => t.Name, RadioDoc r => r.Name, CutsceneDoc c => c.Name, EndingDoc e => e.Name, _ => null,
    };

    /// <summary>An element has a new name (null: it is gone): every list of steps and machines follows.</summary>
    void Rename(string from, string to)
    {
        if (from == null) return;
        void Fix(List<string> list)
        {
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i] == from)
                {
                    if (to == null) list.RemoveAt(i);
                    else list[i] = to;
                }
        }
        foreach (var o in _doc.Objectives) { Fix(o.Units); Fix(o.OnDone); Fix(o.OnFailed); }
        foreach (var t in _doc.Triggers) { Fix(t.Units); Fix(t.Steps); }
        foreach (var c in _doc.Cutscenes) Fix(c.OnFinished);
        Fix(_doc.OnWon);
    }

    // ── The list of everything ──────────────────────────────────

    void RefreshObjects()
    {
        _objects.Clear();
        var root = _objects.CreateItem();
        TreeItem Group(string title)
        {
            var item = _objects.CreateItem(root);
            item.SetText(0, Loc.T(title));
            item.SetSelectable(0, false);
            item.SetCustomColor(0, new Color(0.95f, 0.75f, 0.3f));
            return item;
        }
        void Item(TreeItem parent, string text, object element)
        {
            var item = _objects.CreateItem(parent);
            item.SetText(0, text);
            item.SetMetadata(0, Variant.From((GodotObject)new Holder(element)));
        }
        var player = Group("Игрок");
        Item(player, Loc.T("Старт игрока"), _doc.Player);
        var ours = Group("Свои");
        var theirs = Group("Враг");
        foreach (var u in _doc.Units) Item(u.Team == "player" ? ours : theirs, $"{u.Name}  [{Loc.T(Catalog.Find(Catalog.PlayerUnits, u.UnitId).Name)}]" + (u.Group.Length > 0 ? $"  · {u.Group}" : ""), u);
        if (_doc.Props.Count > 0)
        {
            var props = Group("Объекты");
            for (int i = 0; i < _doc.Props.Count; i++)
                Item(props, $"{i + 1}. " + Loc.T(Catalog.Prop(_doc.Props[i].Kind)?.Name ?? _doc.Props[i].Kind), _doc.Props[i]);
        }
        var goals = Group("Цели");
        foreach (var o in _doc.Objectives) Item(goals, o.Name, o);
        var triggers = Group("Триггеры");
        foreach (var t in _doc.Triggers) Item(triggers, t.Name, t);
        var radio = Group("Рация");
        foreach (var r in _doc.Radio) Item(radio, r.Name, r);
        var films = Group("Катсцены");
        foreach (var c in _doc.Cutscenes) Item(films, c.Name, c);
        var ends = Group("Концовки");
        foreach (var e in _doc.Endings) Item(ends, e.Name + (e.Success ? Loc.T(" (победа)") : Loc.T(" (поражение)")), e);
        SelectInList();
    }

    void SelectInList()
    {
        for (var group = _objects.GetRoot()?.GetFirstChild(); group != null; group = group.GetNext())
            for (var item = group.GetFirstChild(); item != null; item = item.GetNext())
                if (item.GetMetadata(0).Obj is Holder h && h.Element == _sel)
                {
                    if (!item.IsSelected(0)) item.Select(0);
                    _objects.ScrollToItem(item);
                    return;
                }
        _objects.DeselectAll();
    }

    /// <summary>A mission element held in a tree item's metadata (which takes only Godot objects).</summary>
    sealed partial class Holder : GodotObject
    {
        public readonly object Element;
        public Holder(object element) => Element = element;
    }
}
