using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>What is said over the radio: queued lines shown (and heard) one after another.</summary>
public static class Radio
{
    public static void Say(RadioLine line) => StoryOverlay.Instance?.Enqueue(line);
    public static void Clear() => StoryOverlay.Instance?.ClearLines();
}

/// <summary>
/// The story's layer over the battle (scenes/ui/story_overlay.tscn, in game.tscn above the HUD): the
/// subtitle of whoever is speaking, with their portrait; the black bars of a cutscene; the banner of a
/// new, done or failed objective. Shown with the HUD switched off too; the radio is heard on the
/// "Voice" bus, a line without a recording with a click of the radio ("Squelch").
/// </summary>
public partial class StoryOverlay : CanvasLayer
{
    public static StoryOverlay Instance { get; private set; }

    /// <summary>Seconds the bars take to close in and open out.</summary>
    [Export] public float BarTime = 0.6f;
    /// <summary>Seconds an objective's banner stays up.</summary>
    [Export] public float BannerTime = 4f;

    Control _subtitle, _top, _bottom, _banner;
    Label _speaker, _text, _bannerTitle, _bannerText;
    TextureRect _portrait;
    AudioStreamPlayer _voice, _squelch;
    readonly Queue<RadioLine> _lines = new();
    RadioLine _line;
    float _lineLeft, _pause, _bars, _bannerLeft;
    bool _cinematic;

    public static bool InCutscene => Instance is { _cinematic: true };

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public override void _Ready()
    {
        _subtitle = GetNode<Control>("Subtitle");
        _speaker = GetNode<Label>("%Speaker");
        _text = GetNode<Label>("%Line");
        _portrait = GetNode<TextureRect>("%Portrait");
        _top = GetNode<Control>("BarTop");
        _bottom = GetNode<Control>("BarBottom");
        _banner = GetNode<Control>("Banner");
        _bannerTitle = GetNode<Label>("%BannerTitle");
        _bannerText = GetNode<Label>("%BannerText");
        _voice = GetNode<AudioStreamPlayer>("Voice");
        _squelch = GetNode<AudioStreamPlayer>("Squelch");
        _subtitle.Visible = false;
        _banner.Visible = false;
        Bars(0f);
    }

    public void Enqueue(RadioLine line)
    {
        if (line != null) _lines.Enqueue(line);
    }

    public void ClearLines()
    {
        _lines.Clear();
        _line = null;
        _voice.Stop();
        _subtitle.Visible = false;
    }

    public static void Cinematic(bool on)
    {
        if (Instance == null) return;
        Instance._cinematic = on;
    }

    public static void Announce(string title, string text)
    {
        if (Instance == null) return;
        Instance._bannerTitle.Text = Loc.T(title);
        Instance._bannerText.Text = Loc.T(text);
        Instance._banner.Visible = true;
        Instance._bannerLeft = Instance.BannerTime;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        // The bars.
        _bars = Mathf.MoveToward(_bars, _cinematic ? 1f : 0f, dt / Mathf.Max(BarTime, 0.01f));
        Bars(_bars);
        // No banners over a cutscene; they wait until it is over.
        if (_banner.Visible && !_cinematic)
        {
            _bannerLeft -= dt;
            if (_bannerLeft <= 0f) _banner.Visible = false;
        }
        _banner.Modulate = new Color(1f, 1f, 1f, _cinematic ? 0f : Mathf.Clamp(_bannerLeft, 0f, 1f));

        // The radio.
        if (_line != null)
        {
            _lineLeft -= dt;
            if (_lineLeft > 0f) return;
            _pause = _line.Pause;
            _line = null;
            _subtitle.Visible = false;
        }
        if (_pause > 0f)
        {
            _pause -= dt;
            return;
        }
        if (_lines.Count == 0) return;
        _line = _lines.Dequeue();
        _lineLeft = _line.Duration;
        _speaker.Text = Loc.T(_line.Who).ToUpper();
        _speaker.Visible = !string.IsNullOrEmpty(_line.Who);
        _text.Text = Loc.T(_line.Text);
        _portrait.Texture = _line.Face;
        _portrait.Visible = _line.Face != null;
        _subtitle.Visible = true;
        if (_line.Voice != null)
        {
            _voice.Stream = _line.Voice;
            _voice.Play();
        }
        else _squelch.Play();
    }

    void Bars(float k)
    {
        float h = GetViewport().GetVisibleRect().Size.Y * 0.11f * k;
        _top.Visible = _bottom.Visible = k > 0f;
        _top.OffsetBottom = h;
        _bottom.OffsetTop = -h;
    }
}
