namespace MechSim;

/// <summary>
/// Something in a mission scene that a trigger, an objective or a cutscene can set going by naming it
/// in its list of steps: an objective is given, a trigger armed, a machine brought onto the field, a
/// cutscene played, lines said over the radio, the mission ended.
/// </summary>
public interface IStep
{
    void Run();
}
