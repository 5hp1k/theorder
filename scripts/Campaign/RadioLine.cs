using Godot;

namespace MechSim;

/// <summary>
/// One thing said over the radio (or in a cutscene): who says it, the words — always shown as a
/// subtitle — and, if there is one, the recording. A line without a recording is shown for a time to
/// read it in, with a click of the radio.
/// </summary>
[GlobalClass]
public partial class RadioLine : Resource
{
    /// <summary>Who speaks: their callsign and portrait. Or, for someone without a profile, the name below.</summary>
    [Export] public PilotProfile Speaker;
    [Export] public string SpeakerName = "";
    [Export] public Texture2D Portrait;
    [Export(PropertyHint.MultilineText)] public string Text = "";
    /// <summary>The recording; none — the subtitle alone.</summary>
    [Export] public AudioStream Voice;
    /// <summary>Seconds the subtitle stays up; 0 — as long as the recording, or a time to read the words.</summary>
    [Export] public float Hold;
    /// <summary>Seconds of silence after it before the next line.</summary>
    [Export] public float Pause = 0.3f;

    public string Who => Speaker != null ? Speaker.Label : SpeakerName;
    public Texture2D Face => Portrait ?? Speaker?.Portrait;

    /// <summary>How long the subtitle stays up.</summary>
    public float Duration
    {
        get
        {
            if (Hold > 0f) return Hold;
            if (Voice != null) return (float)Voice.GetLength() + 0.4f;
            return Mathf.Max(2.5f, 1.2f + Text.Length * 0.06f);
        }
    }
}
