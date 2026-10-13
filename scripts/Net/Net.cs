using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// The state of a network game (the messages themselves are in <see cref="NetLink"/>). How it works:
/// the host simulates everything (as the single-player game does now), every machine has a number that
/// is the same on all computers (<see cref="Register"/>), each client sends its <see cref="PilotInput"/>
/// every tick and the host drives that client's machine with a <see cref="RemotePilot"/>; the host sends
/// back where every machine is.
/// </summary>
public static class Net
{
    public const int DefaultPort = 27800;

    public enum GameMode
    {
        /// <summary>Everyone against everyone.</summary>
        Ffa,
        /// <summary>Two teams.</summary>
        Tdm,
        /// <summary>All the players together against the computer's machines.</summary>
        Pve,
    }

    public static readonly string[] ModeNames = { "Каждый за себя", "Команда на команду", "Вместе против ботов" };

    public static GameMode Mode = GameMode.Pve;
    /// <summary>Destroyed players come back. Without it they watch the others.</summary>
    public static bool Respawns = true;
    /// <summary>This computer decides what is hit and destroyed: always, except on a client of a network game.</summary>
    public static bool Authority => !Online || IsHost;
    /// <summary>Everyone is everyone's enemy (a network free-for-all).</summary>
    public static bool FreeForAll => Online && Mode == GameMode.Ffa;
    /// <summary>The host's clock, as last heard on a client.</summary>
    public static float ClientTimeLeft;
    public static bool ClientTimeUp;
    /// <summary>The host has stopped the battle for everyone.</summary>
    public static bool HostPaused;

    static readonly Dictionary<int, Node> Units = new();
    static int _nextId = 1;

    public static bool Online { get; private set; }
    public static bool IsHost { get; private set; } = true;

    /// <summary>Gives a machine its number (the host hands them out in spawn order, so they match everywhere).</summary>
    public static int Register(Node unit)
    {
        int id = _nextId++;
        Units[id] = unit;
        unit.SetMeta("net_id", id);
        return id;
    }

    public static Node Find(int id) => Units.TryGetValue(id, out Node unit) && GodotObject.IsInstanceValid(unit) ? unit : null;

    /// <summary>A new battle: numbers start again.</summary>
    public static void Reset()
    {
        Units.Clear();
        _nextId = 1;
    }

    /// <summary>Opens this computer to others. Returns false if the port can't be had.</summary>
    public static bool Host(SceneTree tree, int port = DefaultPort, int players = 8)
    {
        var peer = new ENetMultiplayerPeer();
        if (peer.CreateServer(port, players) != Error.Ok) return false;
        tree.GetMultiplayer().MultiplayerPeer = peer;
        Online = true;
        IsHost = true;
        return true;
    }

    /// <summary>Connects to a host.</summary>
    public static bool Join(SceneTree tree, string address, int port = DefaultPort)
    {
        var peer = new ENetMultiplayerPeer();
        if (peer.CreateClient(address, port) != Error.Ok) return false;
        tree.GetMultiplayer().MultiplayerPeer = peer;
        Online = true;
        IsHost = false;
        return true;
    }

    public static void Leave(SceneTree tree)
    {
        tree.GetMultiplayer().MultiplayerPeer = null;
        Online = false;
        IsHost = true;
    }
}
