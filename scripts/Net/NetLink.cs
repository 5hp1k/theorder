using System.Collections.Generic;
using System.IO;
using Godot;

namespace MechSim;

/// <summary>
/// The wire of a network game; always loaded (an autoload at /root/NetLink), so the same messages
/// exist on every computer whatever scene is showing.
/// The host runs the battle exactly as a single-player game does and is the only one whose shots,
/// collisions and timers count. Every other computer shows the same machines and simulates them too
/// (so they walk, turn and fire smoothly), driven by what the host says:
///   · a client tells the host what its player is doing (<see cref="PilotInput"/>, every tick);
///   · the host tells everyone where each machine is and what its pilot is doing (20 times a second),
///     what damage has been done, who has been put back on the field, the clock and the score.
/// </summary>
public partial class NetLink : Node
{
    public static NetLink I { get; private set; }

    /// <summary>Text for the menu: what the connection is doing.</summary>
    public string Status { get; private set; } = "";
    public event System.Action StatusChanged;

    class Peer
    {
        public string Name = "Пилот", UnitId = "ostrich";
        public string[] Loadout;
        public float[] Paint;
        public byte[] Emblem;
        public bool Ready;
        public int Unit;
    }

    /// <summary>What everyone needs to know to show a machine.</summary>
    public class SpawnInfo
    {
        public int Id, Team, Owner;
        public string UnitId, Name;
        public string[] Loadout;
        public float[] Paint;
        public byte[] Emblem;
        public Node3D Node;
    }

    readonly Dictionary<long, Peer> _peers = new();
    readonly Dictionary<int, SpawnInfo> _units = new();
    readonly Dictionary<(int unit, int section), float> _hurt = new();
    string _myName = "Пилот";
    /// <summary>The name the host plays under.</summary>
    public string HostName => _myName;
    float _stateWait, _hurtWait, _clockWait, _logWait;
    bool _saidHello;
    readonly bool _walk = !string.IsNullOrEmpty(OS.GetEnvironment("THEORDER_NETWALK"));
    readonly bool _log = !string.IsNullOrEmpty(OS.GetEnvironment("THEORDER_NETLOG"));

    public IReadOnlyDictionary<int, SpawnInfo> UnitsById => _units;

    public override void _EnterTree()
    {
        I = this;
        ProcessMode = ProcessModeEnum.Always;
        Multiplayer.PeerDisconnected += OnPeerLeft;
        Multiplayer.ConnectedToServer += OnConnected;
        Multiplayer.ConnectionFailed += () => Fail("Не удалось подключиться");
        Multiplayer.ServerDisconnected += () => Fail("Связь с хостом потеряна");
    }

    public override void _Ready()
    {
        GameConfig.EnsureLoaded();
        Loc.Hook(GetTree());
        // For testing two copies on one computer: THEORDER_NET="host" or "join:127.0.0.1".
        // (THEORDER_COOP="campaign:0" or "custom:<id>": the mission such a test plays together.)
        if (OS.GetEnvironment("THEORDER_COOP") is { Length: > 0 } coop) Net.CoopMission = coop;
        string auto = OS.GetEnvironment("THEORDER_NET");
        if (auto == "host") Callable.From(() => StartHost("Хост", Net.DefaultPort, false)).CallDeferred();
        else if (auto.StartsWith("join:")) Callable.From(() => StartJoin("Клиент", auto[5..], Net.DefaultPort)).CallDeferred();
    }

    void Say(string text)
    {
        Status = text;
        StatusChanged?.Invoke();
        GD.Print("[сеть] ", text);
    }

    void Fail(string text)
    {
        Net.Leave(GetTree());
        _peers.Clear();
        _units.Clear();
        Say(text);
        if (GetTree().CurrentScene is Game game) game.QuitToMenu();
    }

    // ── Starting ────────────────────────────────────────────────

    /// <summary>Opens a game on this computer; the host then waits in the lobby (or, started for a test, goes straight in).</summary>
    public bool StartHost(string name, int port, bool lobby = true)
    {
        _peers.Clear();
        _units.Clear();
        ChatLines.Clear();
        if (!Net.Host(GetTree(), port))
        {
            Say($"Не удалось открыть порт {port}");
            return false;
        }
        _myName = name;
        _port = port;
        Say($"Сервер запущен на порту {port}. Открываем порт на роутере…");
        OpenRouter(port);
        // Shouting on the local network, so that others there can find the game.
        _beacon = new PacketPeerUdp();
        _beacon.SetBroadcastEnabled(true);
        _beacon.SetDestAddress("255.255.255.255", BeaconPort);
        SendRoster();
        if (!lobby)
        {
            PrepareMission();
            LoadingScreen.Start(GetTree());
        }
        return true;
    }

    // ── Being found ─────────────────────────────────────────────

    public const int BeaconPort = 27801;
    PacketPeerUdp _beacon, _ear;
    Upnp _upnp;
    int _port;
    float _beaconWait;
    /// <summary>Games heard on the local network: address → what it said, and when.</summary>
    public readonly Dictionary<string, (string text, int port, double heard)> LanGames = new();
    public event System.Action LanChanged;

    /// <summary>
    /// Asks the router to let the game's port through (UPnP), so that players from the internet can
    /// connect without the host setting anything up by hand. Not every router agrees.
    /// </summary>
    void OpenRouter(int port)
    {
        System.Threading.Tasks.Task.Run(() =>
        {
            string result;
            try
            {
                var upnp = new Upnp();
                if (upnp.Discover() != (int)Upnp.UpnpResult.Success || upnp.GetGateway() is not { } gate || !gate.IsValidGateway())
                    result = "UPnP недоступен. Для игры через интернет пробросьте UDP-порт вручную.";
                else if (upnp.AddPortMapping(port, port, "TheOrder", "UDP") != (int)Upnp.UpnpResult.Success)
                    result = "Роутер не открыл порт. Пробросьте UDP-порт вручную.";
                else
                {
                    _upnp = upnp;
                    result = $"Порт открыт. Внешний адрес: {upnp.QueryExternalAddress()}";
                }
            }
            catch (System.Exception e)
            {
                result = "UPnP: " + e.Message;
            }
            Callable.From(() => { if (Net.Online && Net.IsHost) Say(result); }).CallDeferred();
        });
    }

    /// <summary>Menu: start (or stop) listening for games on the local network.</summary>
    public void Listen(bool on)
    {
        if (!on)
        {
            _ear?.Close();
            _ear = null;
            return;
        }
        if (_ear != null) return;
        _ear = new PacketPeerUdp();
        if (_ear.Bind(BeaconPort) != Error.Ok) _ear = null;
    }

    public override void _Process(double delta)
    {
        if (_beacon != null && Net.Online && Net.IsHost && (_beaconWait -= (float)delta) <= 0f)
        {
            _beaconWait = 1f;
            string map = Catalog.Find(Catalog.Maps, GameConfig.MapId).Name;
            _beacon.PutPacket($"THEORDER|{_port}|{_myName}|{ModeText().Replace("|", "/")}|{map}|{_peers.Count + 1}".ToUtf8Buffer());
        }
        if (_ear == null) return;
        bool changed = false;
        while (_ear.GetAvailablePacketCount() > 0)
        {
            string[] part = _ear.GetPacket().GetStringFromUtf8().Split('|');
            if (part.Length < 6 || part[0] != "THEORDER" || !int.TryParse(part[1], out int port)) continue;
            LanGames[_ear.GetPacketIP()] = ($"{part[2]} · {part[3]} · {part[4]} · игроков: {part[5]}", port, Time.GetUnixTimeFromSystem());
            changed = true;
        }
        // A game that has gone quiet is gone.
        foreach (string address in new List<string>(LanGames.Keys))
            if (Time.GetUnixTimeFromSystem() - LanGames[address].heard > 4.0)
            {
                LanGames.Remove(address);
                changed = true;
            }
        if (changed) LanChanged?.Invoke();
    }

    /// <summary>Connects to a host; the battle starts when the host answers.</summary>
    public bool StartJoin(string name, string address, int port)
    {
        _units.Clear();
        _myName = name;
        if (!Net.Join(GetTree(), address.Trim(), port))
        {
            Say("Неверный адрес");
            return false;
        }
        Say($"Подключение к {address}…");
        return true;
    }

    public void Stop()
    {
        if (!Net.Online) return;
        Net.Leave(GetTree());
        _peers.Clear();
        _units.Clear();
        Roster.Clear();
        _beacon?.Close();
        _beacon = null;
        if (_upnp != null)
        {
            var upnp = _upnp;
            int port = _port;
            _upnp = null;
            System.Threading.Tasks.Task.Run(() => upnp.DeletePortMapping(port, "UDP"));
        }
        GetTree().Paused = false;
        Net.HostPaused = false;
        RosterChanged?.Invoke();
        Say("");
    }

    void OnConnected()
    {
        Say("Подключено. Ждём начала боя…");
        string[] loadout = GameConfig.LoadoutFor(GameConfig.MechId);
        RpcId(1, nameof(Hello), _myName, GameConfig.MechId, loadout ?? System.Array.Empty<string>(), GameConfig.PlayerPaint().Pack(), GameConfig.InsigniaBytes());
    }

    void OnPeerLeft(long id)
    {
        if (!Net.IsHost || !_peers.Remove(id, out Peer peer)) return;
        Say($"{peer.Name} вышел");
        ChatFrom("", $"{peer.Name} вышел");
        SendRoster();
        if (_units.Remove(peer.Unit, out SpawnInfo info) && IsInstanceValid(info.Node)) info.Node.QueueFree();
        Rpc(nameof(Despawn), peer.Unit);
    }

    // ── Joining: client → host → client ─────────────────────────

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void Hello(string name, string unitId, string[] loadout, float[] paint, byte[] emblem)
    {
        if (!Net.IsHost) return;
        long id = Multiplayer.GetRemoteSenderId();
        _peers[id] = new Peer { Name = name, UnitId = unitId, Loadout = loadout.Length > 0 ? loadout : null, Paint = paint, Emblem = emblem.Length <= 200_000 ? emblem : null };
        Say($"{name} подключился");
        ChatFrom("", $"{name} подключился");
        // Battle already on: straight in. Otherwise the newcomer waits in the lobby with everyone else.
        if (GetTree().CurrentScene is Game) SendWelcome(id);
        SendRoster();
    }

    void SendWelcome(long id)
    {
        float left = (GetTree().CurrentScene as Game)?.TimeLeft ?? GameConfig.TimeLimitMinutes * 60f;
        // The mission being played together, if any: the campaign's by its number, one's own as it is.
        string kind = "", data = "";
        if (CampaignRun.Custom != null) (kind, data) = ("custom", CampaignRun.Custom.ToJson());
        else if (CampaignRun.Mission != null && CampaignState.Campaign?.IndexOf(CampaignRun.Mission) is >= 0 and var index) (kind, data) = ("campaign", index.ToString());
        RpcId(id, nameof(Welcome), GameConfig.MapId, (int)Net.Mode, GameConfig.TimeOfDay, GameConfig.WeatherId, GameConfig.TimeLimitMinutes, left, Net.Respawns, kind, data);
    }

    /// <summary>Host: the mission chosen to be played together (Net.CoopMission) is what the battle will be.</summary>
    static void PrepareMission()
    {
        CampaignRun.Mission = null;
        CampaignRun.Custom = null;
        CampaignRun.FromEditor = false;
        string pick = Net.CoopMission ?? "";
        if (pick.StartsWith("campaign:") && int.TryParse(pick[9..], out int index))
        {
            CampaignState.EnsureLoaded();
            if (CampaignState.Campaign is { } campaign && index >= 0 && index < campaign.Missions.Length) CampaignRun.Mission = campaign.Missions[index];
        }
        else if (pick.StartsWith("custom:") && MissionLibrary.All().Find(d => d.Id == pick[7..]) is { } doc)
        {
            CampaignRun.Custom = doc;
            CampaignRun.Mission = MissionBuilder.Info(doc);
        }
        if (CampaignRun.Mission != null) Net.Mode = Net.GameMode.Pve;
    }

    /// <summary>What kind of game it is, for the lobby and the local network.</summary>
    static string ModeText()
    {
        string pick = Net.CoopMission ?? "";
        if (pick.StartsWith("custom:") && MissionLibrary.All().Find(d => d.Id == pick[7..]) is { } doc) return "Миссия: " + doc.Title;
        if (pick.StartsWith("campaign:") && int.TryParse(pick[9..], out int i) && CampaignState.Campaign is { } c && i >= 0 && i < c.Missions.Length) return "Миссия: " + c.Missions[i].Title;
        return Net.ModeNames[(int)Net.Mode];
    }

    /// <summary>Host: the game's settings have changed in the lobby; everyone there is told.</summary>
    public void SettingsChanged()
    {
        if (Net.Online && Net.IsHost && InLobby) SendRoster();
    }

    /// <summary>Who is in the game (the host first), for the lobby.</summary>
    public readonly List<string> Roster = new();
    public event System.Action RosterChanged;
    /// <summary>The host has opened a game and is waiting in the lobby.</summary>
    public bool InLobby => Net.Online && GetTree().CurrentScene is not Game;

    void SendRoster()
    {
        var names = new List<string> { _myName + " (хост)" };
        foreach (Peer peer in _peers.Values) names.Add(peer.Name);
        Rpc(nameof(RosterIs), names.ToArray(), ModeText(), GameConfig.MapId);
        RosterIs(names.ToArray(), ModeText(), GameConfig.MapId);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void RosterIs(string[] names, string mode, string map)
    {
        Roster.Clear();
        Roster.AddRange(names);
        if (!Net.IsHost && GetTree().CurrentScene is not Game) Say($"Лобби: {mode}, «{Catalog.Find(Catalog.Maps, map).Name}». Ждём хоста.");
        RosterChanged?.Invoke();
    }

    /// <summary>Host: out of the lobby and into the battle, taking everyone along.</summary>
    public void BeginBattle()
    {
        if (!Net.Online || !Net.IsHost) return;
        PrepareMission();
        LoadingScreen.Start(GetTree());
    }

    // ── Chat ────────────────────────────────────────────────────

    /// <summary>The last lines of the chat, oldest first.</summary>
    public readonly List<string> ChatLines = new();
    public event System.Action ChatChanged;

    /// <summary>Says something to everyone in the game.</summary>
    public void Chat(string text)
    {
        text = text.Trim();
        if (text == "" || !Net.Online) return;
        if (text.Length > 200) text = text[..200];
        if (Net.IsHost) ChatFrom(_myName, text);
        else RpcId(1, nameof(ChatSend), text);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ChatSend(string text)
    {
        if (Net.IsHost && _peers.TryGetValue(Multiplayer.GetRemoteSenderId(), out Peer peer)) ChatFrom(peer.Name, text.Length > 200 ? text[..200] : text);
    }

    void ChatFrom(string who, string text)
    {
        Rpc(nameof(ChatLine), who, text);
        ChatLine(who, text);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ChatLine(string who, string text)
    {
        ChatLines.Add(who == "" ? $"· {text}" : $"{who}: {text}");
        if (_log) GD.Print("NETCHAT ", ChatLines[^1]);
        if (ChatLines.Count > 40) ChatLines.RemoveAt(0);
        ChatChanged?.Invoke();
    }

    // ── Pause ───────────────────────────────────────────────────

    /// <summary>Host: the battle is stopped (or goes on) for everyone.</summary>
    public void HostPaused(bool paused)
    {
        if (Net.Online && Net.IsHost) Rpc(nameof(PausedIs), paused);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void PausedIs(bool paused)
    {
        GetTree().Paused = paused;
        Net.HostPaused = paused;
    }

    // ── The world: what has been knocked down ───────────────────

    readonly List<(int kind, int a, int b, Vector3 v, float f, float g)> _worldLog = new(), _worldNew = new();
    bool _applying;

    /// <summary>
    /// Host: something of the map has gone — a block of a building (0), a whole prop (1), a part of one
    /// (2), a crater dug (3), a tree of a wood become its own prop (4), a wood flattened by a blast (5). Everyone is told, and whoever joins later is told all of it.
    /// </summary>
    public void World(int kind, int a, int b, Vector3 v, float f, float g = 0f)
    {
        if (!Net.Online || !Net.IsHost || _applying || a < 0) return;
        _worldLog.Add((kind, a, b, v, f, g));
        _worldNew.Add((kind, a, b, v, f, g));
    }

    void SendWorld(long to, List<(int kind, int a, int b, Vector3 v, float f, float g)> events, int from = 0)
    {
        // In packets of a few hundred, so that a long history doesn't make one enormous message.
        for (int start = from; start < events.Count; start += 400)
        {
            int n = Mathf.Min(400, events.Count - start);
            var ints = new int[n * 3];
            var vectors = new Vector3[n];
            var floats = new float[n * 2];
            for (int i = 0; i < n; i++)
            {
                var e = events[start + i];
                ints[i * 3] = e.kind; ints[i * 3 + 1] = e.a; ints[i * 3 + 2] = e.b;
                vectors[i] = e.v;
                floats[i * 2] = e.f; floats[i * 2 + 1] = e.g;
            }
            if (to == 0) Rpc(nameof(WorldIs), ints, vectors, floats);
            else RpcId(to, nameof(WorldIs), ints, vectors, floats);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void WorldIs(int[] ints, Vector3[] vectors, float[] floats)
    {
        _applying = true;
        for (int i = 0; i < vectors.Length; i++)
        {
            int kind = ints[i * 3], a = ints[i * 3 + 1], b = ints[i * 3 + 2];
            switch (kind)
            {
                case 0 when a < ChunkedBuilding.All.Count: ChunkedBuilding.All[a].NetRemove(b); break;
                case 1 when a < Destructible.Everyone.Count: Destructible.Everyone[a].DestroyWhole(vectors[i]); break;
                case 2 when a < Destructible.Everyone.Count && b < Destructible.Everyone[a].GetChildCount():
                    (Destructible.Everyone[a].GetChild(b) as PropPart)?.BreakOff();
                    break;
                case 3: MapTerrain.Instance?.AddCrater(vectors[i], floats[i * 2], floats[i * 2 + 1], true); break;
                case 4 when a < ForestField.All.Count: ForestField.All[a].NetMaterialize(b); break;
                case 5: ForestField.Flatten(vectors[i], floats[i * 2]); break;
            }
        }
        _applying = false;
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void Welcome(string map, int mode, int timeOfDay, int weather, int timeLimit, float timeLeft, bool respawns, string missionKind, string missionData)
    {
        _units.Clear();
        CampaignRun.Mission = null;
        CampaignRun.Custom = null;
        CampaignRun.FromEditor = false;
        if (missionKind == "custom" && MissionDoc.FromJson(missionData) is { } doc)
        {
            CampaignRun.Custom = doc;
            CampaignRun.Mission = MissionBuilder.Info(doc);
        }
        else if (missionKind == "campaign" && int.TryParse(missionData, out int index))
        {
            CampaignState.EnsureLoaded();
            if (CampaignState.Campaign is { } campaign && index >= 0 && index < campaign.Missions.Length) CampaignRun.Mission = campaign.Missions[index];
        }
        GameConfig.MapId = map;
        Net.Mode = (Net.GameMode)mode;
        GameConfig.TimeOfDay = timeOfDay;
        GameConfig.WeatherId = weather;
        GameConfig.TimeLimitMinutes = timeLimit;
        Net.Respawns = respawns;
        Net.ClientTimeLeft = timeLeft;
        Say("Загрузка карты…");
        LoadingScreen.Start(GetTree());
    }

    /// <summary>The client's battle scene is up: it can be told about the machines.</summary>
    public void ClientSceneReady() => RpcId(1, nameof(Ready));

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void Ready()
    {
        if (!Net.IsHost) return;
        long id = Multiplayer.GetRemoteSenderId();
        if (!_peers.TryGetValue(id, out Peer peer) || peer.Ready) return;
        peer.Ready = true;
        if (GetTree().CurrentScene is not Game game) return; // still loading: HostSceneReady picks it up
        Admit(game, id, peer);
    }

    /// <summary>The host's battle scene is up: let in whoever was waiting.</summary>
    public void HostSceneReady(Game game)
    {
        _worldLog.Clear();
        _worldNew.Clear();
        // (machines of a battle before this one, if it has been started again)
        foreach (int stale in new List<int>(_units.Keys))
            if (!IsInstanceValid(_units[stale].Node)) _units.Remove(stale);
        foreach (var (id, peer) in _peers)
        {
            if (peer.Ready && peer.Unit != 0)
            {
                // Was in the battle before it was started again (a mission played over): loads it afresh.
                peer.Ready = false;
                peer.Unit = 0;
            }
            if (peer.Ready) Admit(game, id, peer);
            else SendWelcome(id); // was waiting in the lobby
        }
    }

    void Admit(Game game, long id, Peer peer)
    {
        // Everything that is already on the field, then the newcomer's own machine (to everybody).
        foreach (SpawnInfo info in _units.Values)
            if (IsInstanceValid(info.Node)) RpcId(id, nameof(Spawn), info.Id, info.UnitId, info.Team, info.Owner, info.Name, info.Loadout ?? System.Array.Empty<string>(), info.Node.GlobalTransform, info.Paint ?? System.Array.Empty<float>(), info.Emblem ?? System.Array.Empty<byte>());
        SendWorld(id, _worldLog);
        // …how the mission stands, if one is being played…
        if (Mission.Current is { } mission)
        {
            for (int i = 0; i < mission.Objectives.Count; i++)
                if (mission.Objectives[i].State != Objective.Status.Waiting) RpcId(id, nameof(ObjectiveIs), i, (int)mission.Objectives[i].State, false);
            if (game.MissionOver) RpcId(id, nameof(MissionOverIs), game.MissionWon, mission.Reason);
        }
        // …and the damage they have taken so far.
        foreach (SpawnInfo info in _units.Values)
        {
            if (!IsInstanceValid(info.Node)) continue;
            if (info.Node is Mech mech)
            {
                for (int i = 0; i < DamageModel.Count; i++)
                    if (mech.Damage.Lost((Section)i) > 0.01f) RpcId(id, nameof(Hurt), info.Id, i, mech.Damage.Lost((Section)i));
            }
            else if (info.Node is Vehicle vehicle && vehicle.Hp < vehicle.MaxHp)
                RpcId(id, nameof(Hurt), info.Id, -1, vehicle.MaxHp - Mathf.Max(vehicle.Hp, 0f));
        }
        Node3D unit = game.SpawnForPeer(peer.UnitId, peer.Name, peer.Loadout, (int)id, peer.Paint, peer.Emblem);
        peer.Unit = (int)unit.GetMeta("net_id");
        Say($"{peer.Name} в бою");
    }

    // ── Machines ────────────────────────────────────────────────

    /// <summary>Host: a machine has been put on the field; everyone already in the battle is told.</summary>
    public void Track(Node3D unit, int owner, string[] loadout, float[] paint, byte[] emblem = null)
    {
        int id = Net.Register(unit);
        if (!Net.Online || !Net.IsHost) return;
        var info = new SpawnInfo
        {
            Id = id, Owner = owner, Node = unit, Loadout = loadout, Paint = paint, Emblem = emblem,
            UnitId = Catalog.IdByPath(unit.SceneFilePath), Name = (unit as ITarget)?.DisplayName ?? "",
            Team = (int)((unit as ITarget)?.Team ?? Team.Enemy),
        };
        _units[id] = info;
        foreach (var (peerId, peer) in _peers)
            if (peer.Ready) RpcId(peerId, nameof(Spawn), id, info.UnitId, info.Team, owner, info.Name, loadout ?? System.Array.Empty<string>(), unit.GlobalTransform, paint ?? System.Array.Empty<float>(), emblem ?? System.Array.Empty<byte>());
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void Spawn(int id, string unitId, int team, int owner, string name, string[] loadout, Transform3D at, float[] paint, byte[] emblem)
    {
        if (GetTree().CurrentScene is not Game game || _units.ContainsKey(id)) return;
        Node3D unit = game.SpawnRemote(unitId, (Team)team, name, loadout.Length > 0 ? loadout : null, at, owner == Multiplayer.GetUniqueId(), paint.Length > 0 ? paint : null, emblem);
        unit.SetMeta("net_id", id);
        _units[id] = new SpawnInfo { Id = id, UnitId = unitId, Team = team, Owner = owner, Name = name, Node = unit };
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void Despawn(int id)
    {
        if (_units.Remove(id, out SpawnInfo info) && IsInstanceValid(info.Node)) info.Node.QueueFree();
    }

    // ── Every tick ──────────────────────────────────────────────

    public override void _PhysicsProcess(double delta)
    {
        if (!Net.Online || GetTree().CurrentScene is not Game game) return;
        float dt = (float)delta;
        if (_log && (_logWait -= dt) <= 0f)
        {
            // (THEORDER_NETLOG: what this computer thinks is where, every two seconds.)
            _logWait = 2f;
            string line = $"NETLOG {(Net.IsHost ? "host" : "client")} units={_units.Count}";
            foreach (SpawnInfo info in _units.Values)
                if (IsInstanceValid(info.Node) && info.Node is ITarget t)
                    line += $" | #{info.Id} {info.Name} [{info.UnitId}] team={info.Team} pos=({info.Node.GlobalPosition.X:0},{info.Node.GlobalPosition.Z:0}) hp={t.Integrity:0.00}{(t.IsDestroyed ? " DEAD" : "")}";
            GD.Print(line);
        }
        if (!Net.IsHost)
        {
            // What my pilot is doing.
            // (THEORDER_NETWALK: for tests, the client's machine walks forward by itself.)
            if (_walk && Game.PlayerUnit is IDrivable walker) walker.Throttle = 1f;
            if (_walk && !_saidHello && Game.PlayerUnit != null)
            {
                _saidHello = true;
                Chat("проверка связи");
            }
            if (Game.PlayerUnit is IDrivable mine && !mine.IsDestroyed)
                RpcId(1, nameof(Input), PilotInput.Capture(mine).Pack(), mine.LockTarget is Node locked && locked.HasMeta("net_id") ? (int)locked.GetMeta("net_id") : 0);
            return;
        }
        if ((_stateWait -= dt) <= 0f)
        {
            _stateWait = 0.05f;
            SendState();
        }
        if (_worldNew.Count > 0 && _hurtWait <= dt)
        {
            SendWorld(0, _worldNew);
            _worldNew.Clear();
        }
        if ((_hurtWait -= dt) <= 0f && _hurt.Count > 0)
        {
            _hurtWait = 0.1f;
            foreach (var ((unit, section), amount) in _hurt) Rpc(nameof(Hurt), unit, section, amount);
            _hurt.Clear();
        }
        if ((_clockWait -= dt) <= 0f)
        {
            _clockWait = 1f;
            var tally = new Godot.Collections.Array<int>();
            foreach (SpawnInfo info in _units.Values)
            {
                if (info.Node is not ITarget t) continue;
                var line = Score.Of(t);
                tally.Add(info.Id);
                tally.Add(line.Kills);
                tally.Add(line.Losses);
            }
            Rpc(nameof(Clock), game.TimeLeft, game.TimeUp, tally);
            if (Mission.Current is { Begun: true } mission)
            {
                var progress = new string[mission.Objectives.Count];
                for (int i = 0; i < progress.Length; i++) progress[i] = mission.Objectives[i].Progress;
                Rpc(nameof(ProgressIs), progress);
            }
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    void Input(byte[] data, int lockId)
    {
        if (!Net.IsHost || !_peers.TryGetValue(Multiplayer.GetRemoteSenderId(), out Peer peer)) return;
        if (!_units.TryGetValue(peer.Unit, out SpawnInfo info) || !IsInstanceValid(info.Node)) return;
        var pilot = Game.PilotOf<RemotePilot>(info.Node);
        if (pilot == null) return;
        pilot.Receive(PilotInput.Unpack(data));
        // What that player's missiles are locked on.
        pilot.Lock = lockId != 0 && _units.TryGetValue(lockId, out SpawnInfo target) && IsInstanceValid(target.Node) ? target.Node as ITarget : null;
    }

    void SendState()
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        foreach (SpawnInfo info in _units.Values)
        {
            if (!IsInstanceValid(info.Node) || info.Node is not IDrivable unit) continue;
            Vector3 p = unit.GlobalPosition;
            w.Write((ushort)info.Id);
            w.Write(p.X); w.Write(p.Y); w.Write(p.Z);
            w.Write(unit.HullYaw);
            w.Write(unit.Speed);
            w.Write(PilotInput.Capture(unit).Pack());
        }
        if (stream.Length > 0) Rpc(nameof(State), stream.ToArray());
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    void State(byte[] data)
    {
        using var r = new BinaryReader(new MemoryStream(data));
        while (r.BaseStream.Position + 22 + PilotInput.Size <= data.Length)
        {
            int id = r.ReadUInt16();
            var pos = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            float yaw = r.ReadSingle(), speed = r.ReadSingle();
            PilotInput input = PilotInput.Unpack(r.ReadBytes(PilotInput.Size));
            if (!_units.TryGetValue(id, out SpawnInfo info) || !IsInstanceValid(info.Node)) continue;
            bool mine = info.Node == Game.PlayerUnit as Node;
            if (!mine) Game.PilotOf<RemotePilot>(info.Node)?.Receive(input);
            // My own machine I drive myself and am only pulled back if I have strayed; the others follow the host closely.
            switch (info.Node)
            {
                case Mech mech: mech.NetCorrect(pos, yaw, speed, mine); break;
                case Vehicle vehicle: vehicle.NetCorrect(pos, yaw, speed, mine); break;
            }
        }
    }

    // ── Weapon groups ───────────────────────────────────────────

    /// <summary>The player has put weapons on other triggers (the pause menu): every computer has to know,
    /// since each fires a machine's weapons by the groups it has.</summary>
    public void SendGroups(Node unit)
    {
        if (!Net.Online || unit is not IDrivable machine) return;
        int[] groups = GroupsOf(machine);
        if (Net.IsHost)
        {
            if (unit.HasMeta("net_id")) Regrouped((int)unit.GetMeta("net_id"), groups);
        }
        else RpcId(1, nameof(Regroup), groups);
    }

    static int[] GroupsOf(IDrivable machine)
    {
        var list = machine.Weapons.Weapons;
        var groups = new int[list.Count];
        for (int i = 0; i < list.Count; i++) groups[i] = list[i].Group;
        return groups;
    }

    static void SetGroups(Node unit, int[] groups)
    {
        if (unit is not IDrivable machine) return;
        var list = machine.Weapons.Weapons;
        if (groups.Length != list.Count) return;
        for (int i = 0; i < list.Count; i++) list[i].Group = Mathf.Clamp(groups[i], 1, WeaponSystem.GroupCount);
    }

    /// <summary>Host: a client has regrouped its weapons.</summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void Regroup(int[] groups)
    {
        if (!Net.IsHost || !_peers.TryGetValue(Multiplayer.GetRemoteSenderId(), out Peer peer)) return;
        if (!_units.TryGetValue(peer.Unit, out SpawnInfo info) || !IsInstanceValid(info.Node)) return;
        SetGroups(info.Node, groups);
        Regrouped(peer.Unit, groups);
    }

    /// <summary>Host: passes the new groups on, and keeps them in the loadout those who join later are sent.</summary>
    void Regrouped(int id, int[] groups)
    {
        if (_units.TryGetValue(id, out SpawnInfo info) && IsInstanceValid(info.Node) && Hardpoint.Of(info.Node).Count > 0)
            info.Loadout = Hardpoint.Current(info.Node);
        Rpc(nameof(GroupsAre), id, groups);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void GroupsAre(int id, int[] groups)
    {
        if (!_units.TryGetValue(id, out SpawnInfo info) || !IsInstanceValid(info.Node) || info.Node == Game.PlayerUnit as Node) return;
        SetGroups(info.Node, groups);
    }

    // ── Damage, respawns, the clock ─────────────────────────────

    /// <summary>Host: damage has been done; it is passed on in batches.</summary>
    public void QueueHurt(Node unit, int section, float amount)
    {
        if (!Net.Online || !Net.IsHost || unit == null || !unit.HasMeta("net_id")) return;
        var key = ((int)unit.GetMeta("net_id"), section);
        _hurt[key] = _hurt.GetValueOrDefault(key) + amount;
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void Hurt(int id, int section, float amount)
    {
        if (!_units.TryGetValue(id, out SpawnInfo info) || !IsInstanceValid(info.Node)) return;
        switch (info.Node)
        {
            case Mech mech: mech.Damage.Apply((Section)Mathf.Clamp(section, 0, DamageModel.Count - 1), amount); break;
            case Vehicle vehicle: vehicle.TakeDamage(amount, vehicle.GlobalPosition); break;
        }
    }

    /// <summary>Host: a machine is back on the field.</summary>
    public void HostRespawned(Node3D unit)
    {
        if (Net.Online && Net.IsHost && unit.HasMeta("net_id")) Rpc(nameof(Respawned), (int)unit.GetMeta("net_id"), unit.GlobalTransform);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void Respawned(int id, Transform3D at)
    {
        if (!_units.TryGetValue(id, out SpawnInfo info) || !IsInstanceValid(info.Node)) return;
        switch (info.Node)
        {
            case Mech mech: mech.NetRespawn(at); break;
            case Vehicle vehicle: vehicle.NetRespawn(at); break;
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    void Clock(float timeLeft, bool over, Godot.Collections.Array<int> tally)
    {
        Net.ClientTimeLeft = timeLeft;
        Net.ClientTimeUp = over;
        for (int i = 0; i + 2 < tally.Count; i += 3)
        {
            if (!_units.TryGetValue(tally[i], out SpawnInfo info) || info.Node is not ITarget t) continue;
            var line = Score.Of(t);
            line.Kills = tally[i + 1];
            line.Losses = tally[i + 2];
        }
    }

    // ── A mission played together ───────────────────────────────

    public const int StepRadio = 0, StepCutscene = 1, StepCutsceneEnd = 2;

    /// <summary>Host: a step that shows or says something (radio lines, a cutscene) has run; the others see and hear it too.</summary>
    public void MissionStep(Node step, int what)
    {
        if (!Net.Online || !Net.IsHost || Mission.Current is not { } mission || !mission.IsAncestorOf(step)) return;
        Rpc(nameof(MissionStepIs), mission.GetPathTo(step).ToString(), what);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void MissionStepIs(string path, int what)
    {
        if (Mission.Current is not { Mirrored: true } mission) return;
        switch (mission.GetNodeOrNull(path))
        {
            case RadioAction radio when what == StepRadio: radio.Say(); break;
            case Cutscene cutscene when what == StepCutscene: cutscene.Play(); break;
            case Cutscene cutscene when what == StepCutsceneEnd: cutscene.NetFinish(); break;
        }
    }

    /// <summary>Host: an objective has been given, done or failed.</summary>
    public void ObjectiveChanged(Objective objective, bool announce)
    {
        if (!Net.Online || !Net.IsHost || Mission.Current is not { } mission) return;
        int index = mission.Objectives.IndexOf(objective);
        if (index >= 0) Rpc(nameof(ObjectiveIs), index, (int)objective.State, announce);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ObjectiveIs(int index, int state, bool announce)
    {
        if (Mission.Current is { Mirrored: true } mission && index >= 0 && index < mission.Objectives.Count)
            mission.Objectives[index].NetSet((Objective.Status)state, announce);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    void ProgressIs(string[] progress)
    {
        if (Mission.Current is not { Mirrored: true } mission) return;
        for (int i = 0; i < progress.Length && i < mission.Objectives.Count; i++) mission.Objectives[i].NetProgress = progress[i];
    }

    /// <summary>Host: the mission is over, for everyone.</summary>
    public void MissionOver(bool won, string reason)
    {
        if (Net.Online && Net.IsHost) Rpc(nameof(MissionOverIs), won, reason ?? "");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void MissionOverIs(bool won, string reason)
    {
        if (Mission.Current is { Mirrored: true } mission) mission.NetEnd(won, reason);
    }
}
