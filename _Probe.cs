using Godot;
using MechSim;

// Temporary test harness: not part of the game.
public partial class _Probe : Node
{
    float _t, _seen = -1f;
    int _step;
    Camera3D _cam;
    string _mode = OS.GetEnvironment("PROBE");

    public override void _EnterTree()
    {
        GameConfig.EnsureLoaded();
        GameConfig.MapId = OS.GetEnvironment("PROBE_MAP") is { Length: > 0 } m ? m : "desert";
        GameConfig.MechId = "ostrich";
        GameConfig.EnemyCount = _mode == "botnuke" ? 0 : 1;
        GameConfig.EnemyVehicleCount = _mode == "botnuke" ? 14 : 0;
        GameConfig.EnemyRespawn = false;
        GameConfig.SquadSize = _mode == "botnuke" ? 4 : 0;
        GameConfig.BotNukes = true;
        GameConfig.WeatherId = 0;
        GameConfig.TimeOfDay = 0;
        GameConfig.TimeLimitMinutes = 0;
        GameConfig.Loadouts.Clear();
        AddChild(GD.Load<PackedScene>("res://scenes/game.tscn").Instantiate());
        _cam = new Camera3D { Far = 6000f, Fov = 55f };
        AddChild(_cam);
    }

    public override void _PhysicsProcess(double delta)
    {
        _t += (float)delta;
        if (Game.PlayerUnit is not IDrivable me) return;
        if (_mode == "botnuke")
        {
            if (_step == 0 && _t > 1f)
            {
                _step = 1;
                foreach (var a in Geo.Collect<AiPilot>(this)) a.NukeNotBefore = 3f;
                int planes = 0;
                foreach (var u in Units.All) if (u is Vehicle { Kind: Vehicle.MoveKind.Plane } v) { planes++; GD.Print("  plane ", u.DisplayName, " team ", u.Team, " nuke ", v.Weapons.ReadyStrategic != null); }
                GD.Print("planes: ", planes);
            }
            if (Bomb.Falling.Count > 0 && _seen < 0f)
            {
                _seen = _t;
                var b = Bomb.Falling[0];
                GD.Print($"NUKE DROPPED at t={_t:0.0} by {b.Shooter?.DisplayName} ({b.Shooter?.Team}), {b.GlobalPosition.DistanceTo(me.GlobalPosition):0} m from player, eta {b.TimeToGround:0.0} s, spent={AiPilot.NukeSpent}");
            }
            if (_seen > 0f && Bomb.Falling.Count == 0 && _step == 1) { _step = 2; GD.Print($"landed after {_t - _seen:0.0} s"); }
            return;
        }
        // Smoke over water, fire behind smoke.
        var terrain = MapTerrain.Instance;
        if (_step == 0 && _t > 1f)
        {
            _step = 1;
            Vector3 sea = terrain.RandomSeaSpot(24f) ?? Vector3.Zero;
            sea.Y = terrain.WaterLevel + 0.3f;
            var fire = GD.Load<PackedScene>("res://scenes/fx/wreck_fire.tscn");
            var boom = GD.Load<PackedScene>("res://scenes/fx/explosion.tscn");
            for (int i = 0; i < 3; i++)
            {
                var f = fire.Instantiate<Node3D>();
                AddChild(f);
                f.GlobalPosition = sea + new Vector3(i * 14f - 14f, 0f, i == 1 ? -30f : 0f);
            }
            var e = boom.Instantiate<Node3D>(); AddChild(e); e.GlobalPosition = sea + new Vector3(0f, 2f, 0f);
            _cam.GlobalPosition = sea + new Vector3(0f, 9f, 42f);
            _cam.LookAt(sea + new Vector3(0f, 4f, 0f));
        }
        if (_step >= 1) _cam.MakeCurrent();
    }
}
