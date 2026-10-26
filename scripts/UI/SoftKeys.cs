using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// The strip along the bottom of a cockpit screen naming the soft keys under it (put in by
/// <see cref="CockpitDisplays"/>): each label stands over its key, filled in while its function is on,
/// outlined brighter while the cursor is on the key, flashing for a moment when it is pressed.
/// </summary>
public partial class SoftKeys : Control
{
    /// <summary>The keys: their action, and where the key is along the screen (0 left edge, 1 right).</summary>
    public readonly List<(string action, float at)> Keys = new();
    /// <summary>A label's width as a share of the screen's.</summary>
    [Export] public float LabelWidth = 0.18f;

    static readonly Color Green = new(0.45f, 1f, 0.55f), Dark = new(0.01f, 0.035f, 0.02f);

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        float w = Size.X, h = Size.Y;
        int size = Mathf.RoundToInt(h * 0.46f);
        bool flash = Time.GetTicksMsec() - CockpitControls.LastPressedAt < 250;
        foreach (var (action, at) in Keys)
        {
            string text = CockpitControls.LabelOf(action);
            var box = new Rect2(at * w - LabelWidth * w * 0.5f, h * 0.12f, LabelWidth * w, h * 0.7f);
            bool on = CockpitControls.StateOf(action) == true || (flash && CockpitControls.LastPressed == action);
            // The reactor and the engine, while off, blink: they are what has to be pressed to get going.
            if (action is "reactor" or "engine" && CockpitControls.StateOf(action) == false) on = Time.GetTicksMsec() % 1000 < 500;
            bool hover = CockpitControls.Hovered == action;
            if (on) DrawRect(box, Green);
            DrawRect(box, hover ? Colors.White : Green, false, hover ? 3f : 1.5f);
            // a tick down to the key
            DrawLine(new Vector2(at * w, box.End.Y), new Vector2(at * w, h), Green, 2f);
            float width = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
            int fit = width > box.Size.X - 4f ? Mathf.FloorToInt(size * (box.Size.X - 4f) / width) : size;
            DrawString(font, new Vector2(box.Position.X, box.GetCenter().Y + fit * 0.36f), text, HorizontalAlignment.Center, box.Size.X, fit, on ? Dark : Green);
        }
    }
}
