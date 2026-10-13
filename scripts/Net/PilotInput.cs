using Godot;

namespace MechSim;

/// <summary>
/// Everything a pilot tells a machine in one tick, as plain data: the whole interface between "who
/// drives" and "what is driven". The game already works this way locally (PlayerPilot and AiPilot only
/// set these values on the machine); packed into a few bytes, the same record is what a client would
/// send to the server every tick, and what <see cref="RemotePilot"/> plays back on the other side.
/// </summary>
public struct PilotInput
{
    public float Throttle, Turn, Yaw, Pitch;
    public bool Fire1, Fire2, Fire3, Jump, Descend;

    public static PilotInput Capture(IDrivable unit) => new()
    {
        Throttle = unit.Throttle, Turn = unit.TurnInput, Yaw = unit.TargetYaw, Pitch = unit.TargetPitch,
        Fire1 = unit.Fire1, Fire2 = unit.Fire2, Fire3 = unit.Fire3, Jump = unit.Jump, Descend = unit.Descend,
    };

    public readonly void Apply(IDrivable unit)
    {
        unit.Throttle = Throttle;
        unit.TurnInput = Turn;
        unit.TargetYaw = Yaw;
        unit.TargetPitch = Pitch;
        unit.Fire1 = Fire1;
        unit.Fire2 = Fire2;
        unit.Fire3 = Fire3;
        unit.Jump = Jump;
        unit.Descend = Descend;
    }

    /// <summary>Nine bytes: throttle and steering as signed bytes, the two angles as 16-bit, the buttons as bits.</summary>
    public readonly byte[] Pack()
    {
        var data = new byte[7];
        data[0] = (byte)(sbyte)Mathf.RoundToInt(Mathf.Clamp(Throttle, -1f, 1f) * 127f);
        data[1] = (byte)(sbyte)Mathf.RoundToInt(Mathf.Clamp(Turn, -1f, 1f) * 127f);
        short yaw = (short)Mathf.RoundToInt(Mathf.Wrap(Yaw, -Mathf.Pi, Mathf.Pi) / Mathf.Pi * 32767f);
        short pitch = (short)Mathf.RoundToInt(Mathf.Clamp(Pitch, -Mathf.Pi / 2f, Mathf.Pi / 2f) / (Mathf.Pi / 2f) * 32767f);
        data[2] = (byte)(yaw & 0xff);
        data[3] = (byte)((yaw >> 8) & 0xff);
        data[4] = (byte)(pitch & 0xff);
        data[5] = (byte)((pitch >> 8) & 0xff);
        data[6] = (byte)((Fire1 ? 1 : 0) | (Fire2 ? 2 : 0) | (Fire3 ? 4 : 0) | (Jump ? 8 : 0) | (Descend ? 16 : 0));
        return data;
    }

    public static PilotInput Unpack(byte[] data)
    {
        if (data == null || data.Length < 7) return default;
        return new PilotInput
        {
            Throttle = (sbyte)data[0] / 127f,
            Turn = (sbyte)data[1] / 127f,
            Yaw = (short)(data[2] | (data[3] << 8)) / 32767f * Mathf.Pi,
            Pitch = (short)(data[4] | (data[5] << 8)) / 32767f * (Mathf.Pi / 2f),
            Fire1 = (data[6] & 1) != 0, Fire2 = (data[6] & 2) != 0, Fire3 = (data[6] & 4) != 0,
            Jump = (data[6] & 8) != 0, Descend = (data[6] & 16) != 0,
        };
    }
}
