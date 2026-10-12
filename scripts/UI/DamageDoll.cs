using Godot;

namespace MechSim;

/// <summary>
/// Damage diagram, scaled to fit the control. For a mech it's the paper doll: fill = armour
/// (green → amber → red), outline = internal structure; a section with no armour blinks red, a destroyed
/// one goes black. For anything without sections it's a single bar of what's left.
/// </summary>
public partial class DamageDoll : HudWidget
{
    /// <summary>Off = the player's own unit, on = the locked target.</summary>
    [Export] public bool ShowTarget;

    // Layout in units: x, y, width, height. Indexed by Section; the mech is seen from behind.
    static readonly Rect2[] Layout =
    {
        new(3.5f, 0f, 2f, 1.6f),     // head
        new(3.5f, 2f, 2f, 4.4f),     // centre torso
        new(1.6f, 2f, 1.7f, 3.8f),   // left torso
        new(5.7f, 2f, 1.7f, 3.8f),   // right torso
        new(0f, 2.2f, 1.4f, 4.6f),   // left arm
        new(7.6f, 2.2f, 1.4f, 4.6f), // right arm
        new(2.2f, 6.7f, 1.9f, 5.3f), // left leg
        new(4.9f, 6.7f, 1.9f, 5.3f), // right leg
    };

    protected override void Paint()
    {
        if (ShowTarget && Seat.Target == null) return;
        DamageModel model = ShowTarget ? Seat.Target.Damage : Seat.Damage;
        if (model == null)
        {
            float left = ShowTarget ? Seat.Target.Integrity : Seat.Integrity;
            Color col = left > 0.5f ? Green : left > 0.25f ? Amber : Red;
            var bar = new Rect2(Size.X * 0.3f, 0f, Size.X * 0.4f, Size.Y);
            DrawRect(bar, Dim, false, 1.5f);
            DrawRect(new Rect2(bar.Position.X + 2f, bar.Position.Y + bar.Size.Y * (1f - left), bar.Size.X - 4f, bar.Size.Y * left), new Color(col, 0.8f));
            return;
        }

        float unit = Mathf.Min(Size.X / 9f, Size.Y / 12f);
        bool blink = Mathf.PosMod(Hud.Time, 0.5f) < 0.3f;
        for (int i = 0; i < DamageModel.Count; i++)
        {
            var s = (Section)i;
            var rect = new Rect2(Layout[i].Position * unit, Layout[i].Size * unit);
            if (model.IsDestroyed(s))
            {
                DrawRect(rect, new Color(0f, 0f, 0f, 0.85f));
                DrawRect(rect, new Color(Red, 0.5f), false, 1f);
                continue;
            }

            float armor = model.ArmorFraction(s);
            Color fill = armor <= 0f ? new Color(Red, blink ? 0.75f : 0.2f)
                : armor < 0.5f ? Red.Lerp(Amber, armor * 2f)
                : Amber.Lerp(Green, armor * 2f - 1f);
            float structure = model.StructureFraction(s);
            Color edge = structure > 0.66f ? Green : structure > 0.33f ? Amber : Red;
            DrawRect(rect, new Color(fill, Mathf.Min(fill.A, 0.8f)));
            DrawRect(rect, edge, false, 1.5f);
        }
    }
}
