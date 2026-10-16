"""The cockpit computer's voices, as in MechWarrior: each spoken by Piper (offline text to speech), then made
to sound like it comes over the cockpit's own speaker.
    python3 tools/audio/voice_lines.py <folder with the voice models> [pack ...]
Needs the piper-tts and numpy packages; the voices are at huggingface.co/rhasspy/piper-voices — only ones
whose recordings are free to use:
  betty   en_US-ljspeech-high   LJ Speech, public domain
  cori    en_GB-cori-high       LibriVox, public domain
  joe     en_US-joe-medium      CC0
Writes audio/voice/<pack>/<line>.wav; a line is named for the VoiceLine under CockpitVoice in
scenes/ui/hud.tscn that says it (scripts/UI/CockpitVoice.cs says when). Each pack has its pack.tres
(VoicePack: the recordings by line) and is listed in audio/voice/voices.tres — a new pack needs both.
With no packs named it makes them all.
"""
import io, os, sys, wave
import numpy as np
from piper import PiperVoice, SynthesisConfig

OUT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'audio', 'voice'))
RATE = 44100


def speak(voice, text, speed=1.0):
    buf = io.BytesIO()
    with wave.open(buf, 'wb') as w:
        voice.synthesize_wav(text, w, syn_config=SynthesisConfig(length_scale=1.0 / speed, noise_scale=0.5, noise_w_scale=0.6))
    buf.seek(0)
    with wave.open(buf) as w:
        rate = w.getframerate()
        a = np.frombuffer(w.readframes(w.getnframes()), '<i2').astype(np.float64) / 32768
    # to the game's rate
    return np.interp(np.arange(0, len(a), rate / RATE), np.arange(len(a)), a)


def band(a, lo, hi):
    f = np.fft.rfftfreq(len(a), 1 / RATE)
    gain = 1 / np.sqrt(1 + (lo / np.maximum(f, 1)) ** 4) / np.sqrt(1 + (f / hi) ** 4)
    return np.fft.irfft(np.fft.rfft(a) * gain, len(a))


def trim(a, floor=0.01):
    loud = np.nonzero(np.abs(a) > floor * np.abs(a).max())[0]
    return a[max(loud[0] - 200, 0):loud[-1] + 2000]


def cockpit(a):
    """The cockpit speaker: a narrow band, driven a little hard, with a faint doubling that makes it
    sound like a machine talking, and a click as the channel opens."""
    a = trim(a)
    a = band(a, 320, 3800)
    a = np.tanh(a / np.abs(a).max() * 2.2)
    d = int(0.011 * RATE)
    a = a + 0.35 * np.concatenate([np.zeros(d), a[:-d]])
    a = band(a, 300, 4200)
    click = np.random.default_rng(5).standard_normal(int(0.006 * RATE)) * np.linspace(1, 0, int(0.006 * RATE)) * 0.25
    a = np.concatenate([click, np.zeros(int(0.03 * RATE)), a, np.zeros(int(0.08 * RATE))])
    return a / np.abs(a).max() * 0.8


def save(pack, name, a):
    os.makedirs(os.path.join(OUT, pack), exist_ok=True)
    with wave.open(os.path.join(OUT, pack, name + '.wav'), 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes((np.clip(a, -1, 1) * 32767).astype('<i2').tobytes())
    print('%-6s %-15s %.2f s' % (pack, name, len(a) / RATE))


ENGLISH = {
    'TargetDestroyed': 'Target destroyed.',
    'Startup': 'Reactor online. Sensors online. Weapons online. All systems nominal.',
    'HeatCritical': 'Warning. Heat level critical.',
    'Shutdown': 'Reactor shutdown.',
    'Incoming': 'Warning. Incoming missile.',
    'DamageCritical': 'Warning. Damage critical.',
    'WeaponLost': 'Weapon destroyed.',
    'AmmoOut': 'Ammunition depleted.',
    'LeftArm': 'Left arm destroyed.',
    'RightArm': 'Right arm destroyed.',
    'LeftLeg': 'Left leg destroyed.',
    'RightLeg': 'Right leg destroyed.',
    'LeftTorso': 'Left torso destroyed.',
    'RightTorso': 'Right torso destroyed.',
    'Emp': 'Warning. Electromagnetic pulse. Systems offline.',
    'SystemsOnline': 'Systems online.',
    'OutOfBounds': 'Warning. Leaving combat area.',
}
# pack: (model, lines, speed)
PACKS = {
    'betty': ('en_US-ljspeech-high.onnx', ENGLISH, 1.08),
    'cori': ('en_GB-cori-high.onnx', ENGLISH, 1.05),
    'joe': ('en_US-joe-medium.onnx', ENGLISH, 1.05),
}
for pack in sys.argv[2:] or PACKS:
    model, lines, speed = PACKS[pack]
    voice = PiperVoice.load(os.path.join(sys.argv[1], model))
    for name, text in lines.items():
        save(pack, name, cockpit(speak(voice, text, speed)))
