"""Makes the sounds that are built from the game's other sounds and from synthesis, not cut from recordings:
    python3 tools/audio/derive_sounds.py
  nuke            the nuclear explosion: explosion_big slowed right down, over a long roll of thunder
  siren_loop      an air-raid siren (two rotors a minor third apart), winding up and down
  masonry_1..3    a block of a building breaking away: a thud and a slide of rubble (from collapse)
  collapse_loop   a building coming down, as a loop: a deep roar with rubble pouring through it
  missile_warning two quick pips and a pause, looped while missiles locked on the player are in the air
  gauss           the gauss rifle: the coils' whine dropping away, a hard crack (the cannon's, sped up)
                  and the barrel ringing after it
Run build_sounds.py first if explosion_big.wav or collapse.wav are missing. Needs numpy."""
import os, wave
import numpy as np

OUT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'audio'))
RATE = 44100
rng = np.random.default_rng(7)


def load(name):
    with wave.open(os.path.join(OUT, name + '.wav')) as w:
        a = np.frombuffer(w.readframes(w.getnframes()), '<i2').astype(np.float64) / 32768
        return a.reshape(-1, w.getnchannels()).mean(1) if w.getnchannels() > 1 else a


def save(name, a):
    with wave.open(os.path.join(OUT, name + '.wav'), 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes((np.clip(a, -1, 1) * 32767).astype('<i2').tobytes())
    print('%-16s %.2f s' % (name, len(a) / RATE))


def pitch(a, factor):
    return np.interp(np.arange(0, len(a) - 1, factor), np.arange(len(a)), a)


def lowpass(a, hz, order=1):
    f = np.fft.rfftfreq(len(a), 1 / RATE)
    return np.fft.irfft(np.fft.rfft(a) / (1 + (f / hz) ** (2 * order)), len(a))


def highpass(a, hz):
    return a - lowpass(a, hz)


def norm(a, level=0.9):
    return a / max(np.abs(a).max(), 1e-9) * level


def brown(n):
    a = np.cumsum(rng.standard_normal(n))
    return highpass(a, 18)


def fade(a, head=0.004, tail=0.3):
    a = a.copy()
    h, t = int(head * RATE), int(len(a) * tail)
    a[:h] *= np.linspace(0, 1, h)
    a[-t:] *= np.linspace(1, 0, t) ** 2
    return a


def loop(a, cross):
    """Runs the end into the beginning so it repeats without a seam."""
    x = int(cross * RATE)
    t = np.linspace(0, 1, x)
    out = a[:len(a) - x].copy()
    out[:x] = out[:x] * np.sqrt(t) + a[len(a) - x:] * np.sqrt(1 - t)
    return out


def put(out, a, at, gain=1.0):
    i = int(at * RATE)
    a = a[:len(out) - i]
    out[i:i + len(a)] += a * gain


# ── Nuclear explosion ──
big = load('explosion_big')
n = 16 * RATE
out = np.zeros(n)
t = np.arange(n) / RATE
put(out, norm(pitch(big, 0.7)), 0, 0.9)                     # the crack
put(out, norm(lowpass(pitch(big, 0.33), 600)), 0.05)        # the body, deep
put(out, norm(lowpass(pitch(big, 0.2), 220)), 0.6, 0.9)     # and deeper, later
roll = lowpass(brown(n), 90)
put(out, norm(roll * (1 - np.exp(-t / 0.25)) * np.exp(-t / 4.5) * (1 + 0.35 * np.sin(2 * np.pi * 0.7 * t + 1) * np.exp(-t / 6))), 0, 0.9)
out[-3 * RATE:] *= np.linspace(1, 0, 3 * RATE) ** 2
save('nuke', norm(out, 0.95))

# ── Siren ──
n = 6 * RATE
t = np.arange(n) / RATE
# Winds up quickly, runs down slowly, as a rotor does; one whole cycle is the loop.
sweep = np.where(t < 2.4, 1 - np.exp(-t / 0.8), (1 - np.exp(-3.0)) * np.exp(-(t - 2.4) / 2.2))
hz = 300 + 420 * sweep
phase = 2 * np.pi * np.cumsum(hz) / RATE
voice = lambda p: sum(np.sin(p * k) / k ** 1.2 for k in range(1, 7))
siren = (voice(phase) + 0.8 * voice(phase * 1.2)) * (0.45 + 0.55 * sweep)
siren = lowpass(siren, 2600)
save('siren_loop', norm(loop(np.concatenate([siren, siren[:int(0.25 * RATE)]]), 0.25), 0.7))

# ── Masonry ──
rocks = load('collapse')


def grain(length, low=0.45, high=0.9):
    """A piece of the rock-fall recording, slowed: one lump of rubble landing."""
    start = rng.integers(0, len(rocks) - int(length * RATE * high) - 1)
    return pitch(rocks[start:start + int(length * RATE * high)], rng.uniform(low, high))


for i in range(3):
    n = int(1.5 * RATE)
    t = np.arange(n) / RATE
    out = np.zeros(n)
    # The break: a dull knock with nothing bright in it, the weight of the thing…
    knock = np.sin(2 * np.pi * np.cumsum(38 + 55 * np.exp(-t / 0.05)) / RATE) * np.exp(-t / 0.16)
    put(out, knock, 0, 0.6)
    put(out, lowpass(rng.standard_normal(n), 700) * np.exp(-t / 0.05), 0, 0.6)
    # …then what it was made of, sliding and settling.
    for k in range(5):
        put(out, fade(norm(lowpass(grain(0.7), 2600 - 300 * k)), 0.01, 0.6), 0.03 + k * 0.13 + rng.uniform(0, 0.08), 1.2 - 0.15 * k)
    save('masonry_%d' % (i + 1), norm(fade(out, 0.002, 0.45), 0.9))

# ── A building coming down ──
n = int(6.5 * RATE)
out = np.zeros(n)
roar = lowpass(brown(n), 110) + 0.35 * lowpass(brown(n), 320)
t = np.arange(n) / RATE
out += norm(roar) * 0.75 * (1 + 0.25 * np.sin(2 * np.pi * 0.37 * t) + 0.15 * np.sin(2 * np.pi * 1.3 * t + 2))
for k in range(70):
    g = fade(norm(lowpass(grain(rng.uniform(0.5, 1.2), 0.35, 0.8), rng.uniform(1200, 3500))), 0.03, 0.5)
    put(out, g, rng.uniform(0, 6.5 - 1.3), rng.uniform(0.25, 0.6))
out += lowpass(highpass(rng.standard_normal(n), 2500), 6000) * 0.01   # dust and grit in the air
# (Squashed, so that no one lump stands out of the roar and it can be played loud.)
save('collapse_loop', norm(loop(np.tanh(norm(out, 1.0) * 3.5), 0.5), 0.85))


# ── Gauss rifle ──
n = int(1.6 * RATE)
t = np.arange(n) / RATE
out = np.zeros(n)
# The coils firing one after another down the barrel: a whine that drops away in a few hundredths.
sweep = np.sin(2 * np.pi * np.cumsum(np.interp(t, [0, 0.12], [4200, 700])) / RATE) * np.exp(-t / 0.05)
put(out, sweep, 0.0, 0.35)
# The slug leaving: a sharp crack, the cannon's report played fast and thinned out at the bottom.
crack = highpass(pitch(load('cannon'), 1.6), 220)[:int(0.9 * RATE)]
put(out, fade(norm(crack), 0.001, 0.6), 0.012, 0.9)
put(out, fade(norm(highpass(rng.standard_normal(int(0.05 * RATE)), 1800)), 0.0005, 0.9), 0.012, 0.5)
# The barrel ringing: a few tones that are not in tune with each other, as struck steel.
ring = sum(np.sin(2 * np.pi * hz * t) * np.exp(-t / d) * g for hz, d, g in ((517, 0.55, 1.0), (1263, 0.35, 0.6), (2391, 0.2, 0.4), (3810, 0.12, 0.25)))
put(out, ring, 0.015, 0.12)
save('gauss', norm(fade(out, 0.0005, 0.4), 0.92))

# ── Missile warning ──
# Missiles locked on the player are in the air: two quick high pips, then a breath; played as a loop.
n = int(0.42 * RATE)
t = np.arange(n) / RATE
out = np.zeros(n)
pip = np.sin(2 * np.pi * 1750 * t[:int(0.065 * RATE)]) + 0.3 * np.sin(2 * np.pi * 3500 * t[:int(0.065 * RATE)])
pip = fade(np.sign(pip) * np.abs(pip) ** 0.6, 0.003, 0.15)
put(out, pip, 0.0, 0.8)
put(out, pip, 0.11, 0.8)
save('missile_warning', norm(out, 0.6))
