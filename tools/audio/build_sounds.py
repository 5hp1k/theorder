"""Cuts the game's sound files out of free recordings (all CC0, from opengameart.org; see README).
    python3 tools/audio/build_sounds.py <dir with the unpacked downloads> [names of the sounds to build]
With names given, only those are written (and the recordings the others need may be missing).
Each output keeps the name the scenes already use. One-shots are trimmed, faded and levelled; loops
are cut from the steady middle of a recording and cross-faded end to start so they repeat cleanly.
Needs ffmpeg and numpy."""
import glob, os, subprocess, sys, wave
import numpy as np

SRC = sys.argv[1]
OUT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'audio'))
RATE = 44100
ONLY = set(sys.argv[2:])


def load(pattern, pick=0):
    files = sorted(glob.glob(os.path.join(SRC, pattern), recursive=True))
    if not files:
        if ONLY:
            return np.zeros(RATE)  # for a sound that isn't being built this time
        raise SystemExit('nothing matches ' + pattern)
    raw = subprocess.run(['ffmpeg', '-v', 'error', '-i', files[pick % len(files)], '-f', 'f32le', '-ac', '1', '-ar', str(RATE), '-'], capture_output=True, check=True).stdout
    return np.frombuffer(raw, np.float32).astype(np.float64)


def trim(a, floor=0.02):
    """Drops the silence before the sound starts."""
    loud = np.nonzero(np.abs(a) > floor * np.abs(a).max())[0]
    return a[max(loud[0] - 64, 0):] if len(loud) else a


def shot(a, seconds, fade=0.25, level=0.9):
    a = trim(a)[:int(seconds * RATE)]
    n = int(len(a) * fade)
    if n:
        a[-n:] *= np.linspace(1, 0, n) ** 2
    return a / max(np.abs(a).max(), 1e-9) * level


def loop(a, seconds, start=0.3, cross=0.35, level=0.7):
    """A stretch from the middle of a recording that runs into its own beginning."""
    n, x = int(seconds * RATE), int(cross * RATE)
    s = int(len(a) * start)
    if s + n + x > len(a):
        s = max(0, len(a) - n - x)
    seg = a[s:s + n + x].copy()
    if len(seg) < n + x:
        seg = np.resize(seg, n + x)
    t = np.linspace(0, 1, x)
    seg[:x] = seg[:x] * np.sqrt(t) + seg[n:n + x] * np.sqrt(1 - t)
    seg = seg[:n]
    return seg / max(np.abs(seg).max(), 1e-9) * level


def pitch(a, factor):
    """Plays slower (factor < 1: deeper and longer) or faster."""
    idx = np.arange(0, len(a) - 1, factor)
    return np.interp(idx, np.arange(len(a)), a)


def lowpass(a, hz):
    k = np.exp(-2 * np.pi * hz / RATE)
    out = np.empty_like(a)
    y = 0.0
    for i, v in enumerate(a):
        y = v + (y - v) * k
        out[i] = y
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = np.zeros(n)
    for p in parts:
        out[:len(p)] += p
    return out / max(np.abs(out).max(), 1e-9) * 0.9


def at(a, seconds):
    """The same sound, starting later."""
    return np.concatenate([np.zeros(int(seconds * RATE)), a])


def save(name, a):
    if ONLY and name not in ONLY:
        return
    with wave.open(os.path.join(OUT, name + '.wav'), 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes((np.clip(a, -1, 1) * 32767).astype('<i2').tobytes())
    print('%-16s %.2f s' % (name, len(a) / RATE))


bang = 'x/25-CC0-bang-sfx/'
fall = 'x/sfx_breaking_and_falling/'
save('explosion_big', mix(shot(pitch(load('Chunky_Explosion.mp3'), 0.8), 3.2, 0.5), shot(lowpass(pitch(load(bang + 'cannon_*.ogg', 1), 0.6), 900), 3.0, 0.6) * 0.8))
save('explosion_small', shot(mix(load(bang + 'bang_*.ogg', 2), lowpass(pitch(load(bang + 'cannon_*.ogg', 0), 0.8), 1500)), 1.3, 0.5))
save('cannon', shot(mix(trim(load('cannon_fire_0.ogg')), trim(pitch(load(bang + 'cannon_*.ogg', 3), 0.85)) * 0.7), 1.1, 0.5))
save('mg', shot(load('x/Prepared_SFX_Library/**/AK-47/*.wav', 0), 0.32, 0.5))
save('hit', shot(mix(trim(load(fall + 'bfh1_metal_hit_*.ogg', 1)), trim(pitch(load('bong1.wav'), 0.7)) * 0.5), 0.6, 0.5))
save('footstep', shot(mix(lowpass(pitch(trim(load('x/100-CC0-SFX_0/slam_*.ogg', 2)), 0.5), 700), pitch(trim(load(fall + 'bfh1_metal_falling_*.ogg', 0)), 0.6) * 0.35), 0.9, 0.5))
rocks = [trim(load(fall + 'bfh1_rock_falling_*.ogg', i)) for i in (4, 7, 1, 5)]
save('collapse', shot(mix(rocks[0], at(pitch(rocks[1], 0.6), 0.35), at(pitch(rocks[2], 0.75), 0.9) * 0.9, at(lowpass(pitch(rocks[3], 0.5), 900), 1.4),
                          at(pitch(trim(load(fall + 'bfh1_breaking_*.ogg', 1)), 0.7), 0.15) * 0.8, at(pitch(rocks[0], 0.45), 1.9) * 0.7), 3.4, 0.35))
save('crack', shot(mix(trim(load(fall + 'bfh1_wood_breaking_*.ogg', 0)), pitch(trim(load(fall + 'bfh1_wood_breaking_*.ogg', 2)), 0.8) * 0.6), 0.9, 0.4))
save('alarm', shot(load('alarm_0.wav'), 0.7, 0.1, 0.6))
save('fire_loop', loop(load('fire.wav'), 4.0, 0.2))
save('wind_loop', loop(load('wind_woosh_loop.ogg'), 6.0, 0.0, 0.5, 0.6))
save('rotor_loop', loop(load('heli_0.ogg'), 2.0, 0.4, 0.2))
save('reactor_loop', loop(mix(load('deep_rumble.ogg'), pitch(load('steamboat_engine.wav'), 0.7)[:RATE * 20] * 0.35), 4.0, 0.3, 0.5))
launch = load('x/launch/launch/launch*.wav')
save('missile_launch', shot(launch, 1.4, 0.5))
save('jet_loop', loop(lowpass(trim(launch), 3500), 2.0, 0.25, 0.3))
# Something heavy going into water: a shell splash with the deep thump of a surface burst under it.
naval = 'x/qubodup-NavalBattleSoundSet-cc0/'
save('splash', shot(mix(trim(load(naval + 'Splash.wav')), lowpass(pitch(trim(load(naval + 'WaterSurfaceExplosion06.wav')), 0.7), 1200) * 0.55), 1.7, 0.45))
save('ui_click', shot(load('x/UI_SFX_Set/click*.wav', 0), 0.12, 0.3, 0.7))

# ── Sounds nobody can record: energy weapons, the lock tone, the axe ───────────────────────────────
rng = np.random.default_rng(7)


def band(a, lo, hi):
    """Keeps the part of a sound between two frequencies (sharp filter, done on the spectrum)."""
    spec = np.fft.rfft(a)
    f = np.fft.rfftfreq(len(a), 1 / RATE)
    spec *= 1 / (1 + (lo / np.maximum(f, 1)) ** 6) / (1 + (f / hi) ** 6)
    return np.fft.irfft(spec, len(a))


def hall(a, seconds=0.9, wet=0.3):
    """Tail of a big space: the sound smeared through a decaying burst of noise."""
    n = int(seconds * RATE)
    tail = rng.standard_normal(n) * np.exp(-np.linspace(0, 7, n))
    tail = band(tail, 200, 5000)
    out = np.convolve(a, tail)[:len(a) + n]
    out *= np.abs(a).max() / max(np.abs(out).max(), 1e-9) * wet
    out[:len(a)] += a
    return out


def laser(seconds, pitch_hz, charge=0.0):
    """A beam weapon: (a whine as it charges,) the crack of the discharge, then the beam itself —
    a thick buzzing tone that sags in pitch — with sizzle on top and a thump underneath."""
    n = int(seconds * RATE)
    t = np.arange(n) / RATE
    # the beam: three slightly detuned sawtooth voices and one an octave up, pitch dropping as it burns
    drop = pitch_hz * (1 + 0.5 * np.exp(-t * 9))
    phase = np.cumsum(drop) / RATE
    beam = sum(2 * ((phase * k) % 1) - 1 for k in (1.0, 1.007, 0.993)) / 3 + 0.4 * (2 * ((phase * 2.01) % 1) - 1)
    beam *= 0.75 + 0.25 * np.sin(2 * np.pi * 57 * t)            # the mains-like flutter of a discharge
    beam = band(beam, pitch_hz * 0.8, 3200)
    sizzle = band(rng.standard_normal(n), 3500, 9000) * (0.5 + 0.5 * np.sin(2 * np.pi * 31 * t) ** 2)
    body = (beam + sizzle * 0.35) * np.exp(-t / (seconds * 0.38)) * np.minimum(1, t / 0.004)
    # the crack: a burst of noise and a tone falling four octaves in a few hundredths of a second
    sweep = np.sin(2 * np.pi * np.cumsum(pitch_hz * 24 * np.exp(-t * 55) + pitch_hz) / RATE) * np.exp(-t * 26)
    crack = band(rng.standard_normal(n), 800, 12000) * np.exp(-t * 60) * 0.9 + sweep * 0.8
    thump = np.sin(2 * np.pi * np.cumsum(55 * np.exp(-t * 7) + 32) / RATE) * np.exp(-t * 8)
    out = body * 0.7 + crack + thump * 0.9
    if charge > 0:
        m = int(charge * RATE)
        tc = np.arange(m) / RATE
        whine = np.sin(2 * np.pi * np.cumsum(pitch_hz * 2 * (1 + 3 * (tc / charge) ** 2)) / RATE) * (tc / charge) ** 2 * 0.45
        whine += band(rng.standard_normal(m), 2000, 8000) * (tc / charge) ** 3 * 0.25
        out = np.concatenate([whine, out])
    out = hall(np.tanh(out * 1.6), 1.0, 0.28)                   # driven a little, then set in a space
    fade = int(0.15 * RATE)
    out[-fade:] *= np.linspace(1, 0, fade)
    return out / np.abs(out).max() * 0.92


save('laser', laser(0.55, 190))
save('large_laser', laser(1.0, 96, charge=0.16))

# lock-on: two clean rising pips
t = np.arange(int(0.09 * RATE)) / RATE
pip = lambda hz: np.sin(2 * np.pi * hz * t) * np.minimum(1, t / 0.004) * np.minimum(1, (0.09 - t) / 0.02)
save('lock', np.concatenate([pip(1320), np.zeros(int(0.04 * RATE)), pip(1760)]) * 0.6)


def blip(hz0, hz1, seconds, buzz=0.25):
    """A short electronic chirp sliding from one pitch to another, with a little square-wave edge."""
    t = np.arange(int(seconds * RATE)) / RATE
    phase = 2 * np.pi * np.cumsum(np.linspace(hz0, hz1, len(t))) / RATE
    wave_ = np.sin(phase) + buzz * np.sign(np.sin(phase))
    return wave_ * np.minimum(1, t / 0.003) * np.minimum(1, (seconds - t) / 0.012)


# a target picked: a quick double chirp, as in MechWarrior 4
save('target_acquire', np.concatenate([blip(900, 1500, 0.045), np.zeros(int(0.025 * RATE)), blip(1300, 2000, 0.05)]) * 0.45)
def beep(hz, seconds, edge=0.35):
    """A short instrument beep: a sine with a little of a square wave's edge, filtered soft."""
    t = np.arange(int(seconds * RATE)) / RATE
    s = np.sin(2 * np.pi * hz * t)
    tone = s + edge * np.tanh(4 * s)
    return tone * np.minimum(1, t / 0.004) * np.minimum(1, (seconds - t) / 0.015)


def gap(seconds):
    return np.zeros(int(seconds * RATE))


def radio(a):
    """Through a cockpit speaker: low and high ends cut, a touch of grit."""
    a = np.convolve(a, np.ones(6) / 6, 'same')
    a = a - np.convolve(a, np.ones(90) / 90, 'same')
    return np.tanh(a * 1.6)


# the target is down — three beeps to choose from (the game plays target_destroyed.wav, now the cockpit's voice
# from voice_lines.py: copy one of these over it for a beep instead)
variants = {
    # a military confirmation: two quick beeps and a lower closing tone
    'target_destroyed_a': radio(np.concatenate([beep(880, 0.06), gap(0.045), beep(880, 0.06), gap(0.045), beep(587, 0.17)])),
    # a falling sweep, as of a contact dropping off the scope
    'target_destroyed_b': radio(np.concatenate([np.sin(2 * np.pi * np.cumsum(np.linspace(1400, 420, int(0.36 * RATE))) / RATE)
                                                * np.minimum(1, np.arange(int(0.36 * RATE)) / 400) * np.linspace(1, 0.2, int(0.36 * RATE))])),
    # a low double buzz, short and dry
    'target_destroyed_c': radio(np.concatenate([beep(330, 0.09, 0.9), gap(0.05), beep(247, 0.16, 0.9)])),
}
for name, a in variants.items():
    save(name, a / np.abs(a).max() * 0.55)

# the radio opening for a line that has no recording (the story's subtitles): a click, a breath of
# static, a short tone, as a channel keys up
hiss = np.random.default_rng(11).standard_normal(int(0.16 * RATE)) * np.linspace(1, 0, int(0.16 * RATE)) ** 2
key = np.concatenate([np.ones(int(0.002 * RATE)) * 0.9, gap(0.01), hiss * 0.5, gap(0.02), beep(1250, 0.05, 0.6) * 0.5])
save('radio_squelch', radio(key) / np.abs(radio(key)).max() * 0.5)

# a falling bomb: a whistle sliding down as it drops, with air rushing past, louder as it nears
t = np.arange(int(3.2 * RATE)) / RATE
f = 1900 * np.exp(-t / 2.6) + 650 + 18 * np.sin(2 * np.pi * 5.5 * t)
ph = 2 * np.pi * np.cumsum(f) / RATE
air = np.convolve(np.random.default_rng(3).standard_normal(len(t)), np.ones(12) / 12, 'same')
whistle = (np.sin(ph) + 0.25 * np.sin(2 * ph) + 0.25 * air * np.sin(ph * 0.5)) * np.minimum(1, t / 0.4) * (0.35 + 0.65 * t / 3.2) * np.minimum(1, (3.2 - t) / 0.05)
save('bomb_whistle', whistle / np.abs(whistle).max() * 0.7)

# the axe: air torn by something heavy — noise swept through a moving filter, with a low rush under it
n = int(0.55 * RATE)
t = np.arange(n) / RATE
noise = rng.standard_normal(n)
swish = sum(band(noise, c * 0.7, c * 1.4) * np.exp(-((t - 0.16 - i * 0.05) / 0.07) ** 2) for i, c in enumerate((500, 900, 1600, 2600)))
swish += band(noise, 60, 220) * np.exp(-((t - 0.2) / 0.12) ** 2) * 1.5
save('swing', swish / np.abs(swish).max() * 0.85)


def ppc(seconds=1.5):
    """A particle cannon: capacitors whining up, the bang of the discharge, then a crackling,
    tearing bolt — arcing noise chopped at an uneven rate over a low growl — and its thunder."""
    charge = 0.22
    m = int(charge * RATE)
    tc = np.arange(m) / RATE
    whine = np.sin(2 * np.pi * np.cumsum(180 * (1 + 9 * (tc / charge) ** 2.5)) / RATE) * (tc / charge) ** 2 * 0.5
    whine += band(rng.standard_normal(m), 3000, 10000) * (tc / charge) ** 4 * 0.3
    n = int(seconds * RATE)
    t = np.arange(n) / RATE
    # arcing: wide noise gated by a jittery square wave, so it spits rather than hisses
    gate_rate = 70 + 50 * np.sin(2 * np.pi * 3.1 * t) + 30 * rng.standard_normal(n).cumsum() / np.sqrt(np.arange(1, n + 1))
    gate = (np.sin(2 * np.pi * np.cumsum(gate_rate) / RATE) > -0.2).astype(float)
    arc = band(rng.standard_normal(n), 1200, 11000) * gate * np.exp(-t * 4.5)
    growl_hz = 62 * (1 + 0.8 * np.exp(-t * 6))
    ph = np.cumsum(growl_hz) / RATE
    growl = sum(2 * ((ph * k) % 1) - 1 for k in (1.0, 1.011, 1.5, 2.02)) / 4
    growl = band(growl, 50, 1800) * np.exp(-t * 3.2) * (0.7 + 0.3 * np.sin(2 * np.pi * 43 * t))
    bang = band(rng.standard_normal(n), 300, 9000) * np.exp(-t * 45) * 1.3
    bang += np.sin(2 * np.pi * np.cumsum(900 * np.exp(-t * 40) + 45) / RATE) * np.exp(-t * 11) * 1.2
    thunder = band(rng.standard_normal(n), 40, 400) * np.exp(-t * 2.4) * np.minimum(1, t / 0.05) * 0.9
    out = np.concatenate([whine, np.tanh((bang + arc * 0.8 + growl * 0.9 + thunder) * 1.7)])
    out = hall(out, 1.3, 0.32)[:int(1.9 * RATE)]
    fade = int(0.25 * RATE)
    out[-fade:] *= np.linspace(1, 0, fade)
    return out / np.abs(out).max() * 0.92


save('ppc', ppc())

# rain: a steady hiss of drops with a soft patter in it and a low wash underneath; loops on itself
n = int(5.5 * RATE)
t = np.arange(n) / RATE
hiss = band(rng.standard_normal(n), 1800, 11000) * (0.85 + 0.15 * np.sin(2 * np.pi * 0.37 * t + 1.0))
patter = band(rng.standard_normal(n) * (rng.random(n) > 0.985), 700, 5000) * 6.0
wash = band(rng.standard_normal(n), 120, 700) * 0.6
save('rain_loop', loop(hiss + patter + wash, 4.0, 0.1, 0.5, 0.6))
