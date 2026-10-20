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
    /// <summary>Weapon groups fired, one bit each.</summary>
    public int Fire;
    public bool Jump, Descend;
    /// <summary>A mech's pilot wants it down on one knee.</summary>
    public bool Kneel;

    public static PilotInput Capture(IDrivable unit) => new()
    {
        Throttle = unit.Throttle, Turn = unit.TurnInput, Yaw = unit.TargetYaw, Pitch = unit.TargetPitch,
        Fire = unit.Fire, Jump = unit.Jump, Descend = unit.Descend, Kneel = unit is Mech { Kneel: true },
    };

    public readonly void Apply(IDrivable unit)
    {
        unit.Throttle = Throttle;
        unit.TurnInput = Turn;
        unit.TargetYaw = Yaw;
        unit.TargetPitch = Pitch;
        unit.Fire = Fire;
        unit.Jump = Jump;
        unit.Descend = Descend;
        if (unit is Mech mech) mech.Kneel = Kneel;
    }

    /// <summary>Eight bytes: throttle and steering as signed bytes, the two angles as 16-bit, the buttons as bits (six groups, then jump and descend; then kneel).</summary>
    public const int Size = 8;

    public readonly byte[] Pack()
    {
        var data = new byte[Size];
        data[0] = (byte)(sbyte)Mathf.RoundToInt(Mathf.Clamp(Throttle, -1f, 1f) * 127f);
        data[1] = (byte)(sbyte)Mathf.RoundToInt(Mathf.Clamp(Turn, -1f, 1f) * 127f);
        short yaw = (short)Mathf.RoundToInt(Mathf.Wrap(Yaw, -Mathf.Pi, Mathf.Pi) / Mathf.Pi * 32767f);
        short pitch = (short)Mathf.RoundToInt(Mathf.Clamp(Pitch, -Mathf.Pi / 2f, Mathf.Pi / 2f) / (Mathf.Pi / 2f) * 32767f);
        data[2] = (byte)(yaw & 0xff);
        data[3] = (byte)((yaw >> 8) & 0xff);
        data[4] = (byte)(pitch & 0xff);
        data[5] = (byte)((pitch >> 8) & 0xff);
        data[6] = (byte)((Fire & 63) | (Jump ? 64 : 0) | (Descend ? 128 : 0));
        data[7] = (byte)(Kneel ? 1 : 0);
        return data;
    }

    public static PilotInput Unpack(byte[] data)
    {
        if (data == null || data.Length < Size) return default;
        return new PilotInput
        {
            Throttle = (sbyte)data[0] / 127f,
            Turn = (sbyte)data[1] / 127f,
            Yaw = (short)(data[2] | (data[3] << 8)) / 32767f * Mathf.Pi,
            Pitch = (short)(data[4] | (data[5] << 8)) / 32767f * (Mathf.Pi / 2f),
            Fire = data[6] & 63,
            Jump = (data[6] & 64) != 0, Descend = (data[6] & 128) != 0,
            Kneel = (data[7] & 1) != 0,
        };
    }
}
