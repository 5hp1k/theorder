using Godot;

namespace MechSim;

/// <summary>
/// A pilot who is somewhere else: drives its machine from the last <see cref="PilotInput"/> that
/// arrived for it (on the server, a client's; on a client, what the server says another machine is
/// doing). If nothing arrives for a while the machine eases to a stop rather than run on.
/// </summary>
public partial class RemotePilot : MechPilot
{
    /// <summary>Seconds without news after which the machine is brought to a halt.</summary>
    [Export] public float Timeout = 0.5f;

    /// <summary>What the far pilot's missiles are locked on, as last heard.</summary>
    public ITarget Lock;

    PilotInput _input;
    float _age;

    public void Receive(PilotInput input)
    {
        _input = input;
        _age = 0f;
    }

    public override void Think(float dt)
    {
        _age += dt;
        if (_age > Timeout)
        {
            _input.Throttle = Mathf.MoveToward(_input.Throttle, 0f, dt);
            _input.Turn = 0f;
            _input.Fire1 = _input.Fire2 = _input.Fire3 = _input.Jump = _input.Descend = false;
        }
        _input.Apply(Unit);
        Unit.LockTarget = Lock is Node node && IsInstanceValid(node) && !Lock.IsDestroyed ? Lock : null;
    }
}
