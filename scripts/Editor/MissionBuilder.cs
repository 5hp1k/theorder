using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// Turns a <see cref="MissionDoc"/> into what the game plays: the briefing's <see cref="MissionInfo"/>
/// and a <see cref="Mission"/> scene with its map, machines (<see cref="MissionUnit"/>), objectives,
/// triggers, radio lines, cutscenes and endings — the same nodes a campaign mission is made of, wired
/// by the same lists of steps.
/// </summary>
public static class MissionBuilder
{
    public static MissionInfo Info(MissionDoc doc)
    {
        var pilots = Pilots(doc);
        var info = new MissionInfo
        {
            Id = "custom_" + doc.Id,
            Title = doc.Title,
            Location = doc.Location,
            Briefing = doc.Briefing,
            Debriefing = doc.Debriefing,
            TimeOfDay = doc.TimeOfDay,
            Weather = doc.Weather,
            TimeLimit = doc.TimeLimit,
            EnemyNukes = doc.EnemyNukes,
            EnemyEmp = doc.EnemyEmp,
            PlayerUnit = doc.Player.UnitId ?? "",
        };
        var objectives = new List<string>();
        foreach (var o in doc.Objectives)
            if (o.StartActive && o.Text.Length > 0) objectives.Add(o.Required ? o.Text : Loc.T("Дополнительно: ") + o.Text);
        info.Objectives = objectives.ToArray();
        var lance = new List<LanceMember>();
        foreach (var l in doc.Lance)
            lance.Add(new LanceMember { UnitId = l.UnitId, Loadout = string.Join("|", l.Loadout), Pilot = pilots.GetValueOrDefault(l.PilotId ?? "") });
        info.Lance = lance.ToArray();
        return info;
    }

    /// <summary>The mission's characters as pilots of the game, by their ids.</summary>
    public static Dictionary<string, PilotProfile> Pilots(MissionDoc doc)
    {
        var map = new Dictionary<string, PilotProfile>();
        foreach (var p in doc.Pilots)
        {
            if (string.IsNullOrEmpty(p.Id) || map.ContainsKey(p.Id)) continue;
            var s = p.Skills is { Length: 4 } ? p.Skills : new[] { 50, 50, 50, 50 };
            map[p.Id] = new PilotProfile
            {
                Id = p.Id, Name = p.Name, Callsign = p.Callsign, Bio = p.Bio, Portrait = Portrait(p.Portrait),
                Gunnery = Mathf.Clamp(s[0], 0, 100), Piloting = Mathf.Clamp(s[1], 0, 100), Awareness = Mathf.Clamp(s[2], 0, 100), Aggression = Mathf.Clamp(s[3], 0, 100),
            };
        }
        return map;
    }

    /// <summary>A picture from a PNG in base64; null if there is none, or it is not one.</summary>
    public static Texture2D Portrait(string base64)
    {
        if (string.IsNullOrEmpty(base64)) return null;
        try
        {
            var image = new Image();
            if (image.LoadPngFromBuffer(System.Convert.FromBase64String(base64)) != Error.Ok) return null;
            return ImageTexture.CreateFromImage(image);
        }
        catch (System.FormatException)
        {
            return null;
        }
    }

    public static Mission Build(MissionDoc doc)
    {
        var pilots = Pilots(doc);
        var mission = new Mission { Name = "Mission" };
        var entry = Catalog.Find(Catalog.Maps, doc.Map);
        var map = GD.Load<PackedScene>(entry.Path).Instantiate<Node3D>();
        map.Name = "Map";
        mission.AddChild(map);

        // The ground's height is known before the terrain is built (it is its noise).
        var terrain = Geo.Collect<MapTerrain>(map) is { Count: > 0 } found ? found[0] : null;
        var props = new Node3D { Name = "Props" };
        map.AddChild(props);
        foreach (var p in doc.Props)
        {
            if (Catalog.Prop(p.Kind) is not { } kind || !ResourceLoader.Exists(kind.Path)) continue;
            var prop = GD.Load<PackedScene>(kind.Path).Instantiate<Node3D>();
            prop.Transform = new Transform3D(new Basis(Vector3.Up, p.Yaw), new Vector3(p.X, terrain?.HeightAt(p.X, p.Z) ?? 0f, p.Z));
            props.AddChild(prop);
        }

        var start = new Marker3D { Name = "PlayerSpawn", Transform = At(doc.Player.X, doc.Player.Z, doc.Player.Yaw) };
        mission.AddChild(start);

        foreach (var u in doc.Units)
        {
            var unit = new MissionUnit
            {
                Name = Safe(u.Name),
                UnitId = u.UnitId,
                Team = u.Team == "player" ? Team.Player : Team.Enemy,
                DisplayName = u.DisplayName ?? "",
                Pilot = pilots.GetValueOrDefault(u.PilotId ?? ""),
                Loadout = string.Join("|", u.Loadout),
                Group = u.Group ?? "",
                SpawnAtStart = u.SpawnAtStart,
                PatrolRadius = u.Patrol,
                HuntPlayer = u.HuntPlayer,
                Paint = PaintOf(u.Paint)?.Pack() ?? System.Array.Empty<float>(),
                Transform = At(u.X, u.Z, u.Yaw),
            };
            mission.AddChild(unit);
        }

        foreach (var o in doc.Objectives)
        {
            Objective node = o.Kind switch
            {
                "reach" => new ReachObjective { Radius = o.Radius },
                "protect" => new ProtectObjective { Group = o.Group ?? "", Units = Paths(o.Units), MayLose = o.MayLose },
                "survive" => new SurviveObjective { Seconds = o.Seconds },
                _ => new DestroyObjective { Group = o.Group ?? "", Units = Paths(o.Units) },
            };
            node.Name = Safe(o.Name);
            node.Text = o.Text ?? "";
            node.Required = o.Required;
            node.StartActive = o.StartActive;
            node.OnDone = Paths(o.OnDone);
            node.OnFailed = Paths(o.OnFailed);
            node.Position = new Vector3(o.X, 0f, o.Z);
            mission.AddChild(node);
        }

        foreach (var t in doc.Triggers)
        {
            Trigger node = t.Kind switch
            {
                "timer" => new TimerTrigger { Delay = t.Delay },
                "destroyed" => new DestroyedTrigger { Group = t.Group ?? "", Units = Paths(t.Units), Count = t.Count },
                _ => new AreaTrigger
                {
                    Radius = t.Radius,
                    Who = t.Who switch { "side" => AreaTrigger.Whom.PlayerSide, "enemy" => AreaTrigger.Whom.Enemy, _ => AreaTrigger.Whom.Player },
                },
            };
            node.Name = Safe(t.Name);
            node.StartArmed = t.StartArmed;
            node.Once = t.Once;
            node.Steps = Paths(t.Steps);
            node.Position = new Vector3(t.X, 0f, t.Z);
            mission.AddChild(node);
        }

        foreach (var r in doc.Radio)
            mission.AddChild(new RadioAction { Name = Safe(r.Name), Lines = Lines(r.Lines, pilots) });

        foreach (var c in doc.Cutscenes)
        {
            mission.AddChild(Cutscene(c, pilots));
            // Played at the start: a timer that goes off at once.
            if (c.AtStart)
                mission.AddChild(new TimerTrigger { Name = Safe(c.Name) + "_AtStart", Delay = 0f, StartArmed = true, Steps = Paths(new List<string> { c.Name }) });
        }

        foreach (var e in doc.Endings)
            mission.AddChild(new EndMission { Name = Safe(e.Name), Success = e.Success, Reason = e.Reason ?? "" });

        // (The mission's own list is read from the root: its paths are the names themselves.)
        var won = new Godot.Collections.Array<NodePath>();
        foreach (string step in doc.OnWon) won.Add(new NodePath(Safe(step)));
        mission.OnWon = won;
        return mission;
    }

    static Cutscene Cutscene(CutsceneDoc c, Dictionary<string, PilotProfile> pilots)
    {
        var scene = new Cutscene
        {
            Name = Safe(c.Name),
            Skippable = c.Skippable,
            HoldBattle = c.HoldBattle,
            Lines = Lines(c.Lines, pilots),
            OnFinished = Paths(c.OnFinished),
        };
        var camera = new Camera3D { Name = "Camera", Far = 6000f };
        scene.AddChild(camera);
        if (c.Shots.Count > 0) camera.Transform = ShotTransform(c.Shots[0]);
        if (c.Shots.Count < 2)
        {
            scene.Length = c.Shots.Count == 1 ? Mathf.Max(c.Shots[0].Time, 1f) : 6f;
            if (c.Shots.Count == 1) camera.Fov = c.Shots[0].Fov;
            return scene;
        }
        // The camera from shot to shot: its place, which way it looks and how wide.
        var anim = new Animation();
        int pos = anim.AddTrack(Animation.TrackType.Position3D), rot = anim.AddTrack(Animation.TrackType.Rotation3D), fov = anim.AddTrack(Animation.TrackType.Value);
        anim.TrackSetPath(pos, "Camera");
        anim.TrackSetPath(rot, "Camera");
        anim.TrackSetPath(fov, "Camera:fov");
        foreach (int track in new[] { pos, rot, fov }) anim.TrackSetInterpolationType(track, Animation.InterpolationType.Cubic);
        float time = 0f;
        for (int i = 0; i < c.Shots.Count; i++)
        {
            var shot = c.Shots[i];
            if (i > 0) time += Mathf.Max(shot.Time, 0.1f);
            var at = ShotTransform(shot);
            anim.PositionTrackInsertKey(pos, time, at.Origin);
            anim.RotationTrackInsertKey(rot, time, at.Basis.GetRotationQuaternion());
            anim.TrackInsertKey(fov, time, shot.Fov);
        }
        anim.Length = time;
        var library = new AnimationLibrary();
        library.AddAnimation("play", anim);
        var player = new AnimationPlayer { Name = "Animator" };
        player.AddAnimationLibrary("", library);
        scene.AddChild(player);
        scene.Animation = "play";
        scene.Length = time;
        return scene;
    }

    public static Transform3D ShotTransform(ShotDoc s)
        => new(Basis.FromEuler(new Vector3(s.Pitch, s.Yaw, 0f)), new Vector3(s.X, s.Y, s.Z));

    static RadioLine[] Lines(List<LineDoc> lines, Dictionary<string, PilotProfile> pilots)
    {
        var list = new List<RadioLine>();
        foreach (var l in lines)
        {
            if (l == null || string.IsNullOrWhiteSpace(l.Text)) continue;
            list.Add(new RadioLine { Speaker = pilots.GetValueOrDefault(l.Speaker ?? ""), SpeakerName = l.SpeakerName ?? "", Text = l.Text, Hold = l.Hold });
        }
        return list.ToArray();
    }

    /// <summary>How a machine is painted; null — its side's usual paint.</summary>
    public static PaintJob PaintOf(PaintDoc paint)
    {
        if (paint == null || paint.Preset == -1) return null;
        if (paint.Preset >= 0 && paint.Preset < PaintJob.Presets.Length)
        {
            var p = PaintJob.Presets[paint.Preset];
            return new PaintJob { Base = p.Base, Pattern = p.Pattern, PatternA = p.A, PatternB = p.B, Second = p.A, Wear = 0.4f, Dirt = 0.4f };
        }
        return PaintJob.Unpack(paint.Custom);
    }

    static Transform3D At(float x, float z, float yaw) => new(new Basis(Vector3.Up, yaw), new Vector3(x, 0f, z));

    /// <summary>Paths from one element to the others, its siblings under the mission.</summary>
    static Godot.Collections.Array<NodePath> Paths(List<string> names)
    {
        var list = new Godot.Collections.Array<NodePath>();
        if (names == null) return list;
        foreach (string name in names)
            if (!string.IsNullOrEmpty(name)) list.Add(new NodePath("../" + Safe(name)));
        return list;
    }

    /// <summary>A name as a node may have it (no dots, slashes, colons and the like).</summary>
    public static string Safe(string name)
    {
        if (string.IsNullOrEmpty(name)) return "_";
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (char ch in name) sb.Append(ch is '.' or ':' or '@' or '/' or '"' or '%' ? '_' : ch);
        return sb.ToString();
    }
}
