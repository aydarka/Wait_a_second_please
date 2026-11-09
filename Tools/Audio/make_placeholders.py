"""Placeholder sounds for "Wait a second, please" (stage 1).

Usage: python make_placeholders.py <output folder>
Writes mono 16-bit 44.1 kHz WAV files. Every sound is synthesized here, so they are free to use.
Real recordings will replace the gunshots and impacts later; the bullet and slow-motion bed may stay.
"""
import sys
import os
import numpy as np
from scipy import signal
from scipy.io import wavfile

SR = 44100
rng = np.random.default_rng(7)


def t_axis(seconds):
    return np.arange(int(round(seconds * SR))) / SR


def noise(seconds):
    return rng.standard_normal(int(round(seconds * SR)))


def band(x, lo, hi, order=2):
    sos = signal.butter(order, [lo, hi], btype="bandpass", fs=SR, output="sos")
    return signal.sosfilt(sos, x)


def low(x, f, order=2):
    sos = signal.butter(order, f, btype="lowpass", fs=SR, output="sos")
    return signal.sosfilt(sos, x)


def high(x, f, order=2):
    sos = signal.butter(order, f, btype="highpass", fs=SR, output="sos")
    return signal.sosfilt(sos, x)


def decay(seconds, tau, attack=0.002):
    t = t_axis(seconds)
    env = np.exp(-t / tau)
    if attack > 0:
        env *= np.clip(t / attack, 0, 1)
    return env


def sweep(seconds, f0, f1, curve="exp"):
    t = t_axis(seconds)
    if curve == "exp":
        f = f0 * (f1 / f0) ** (t / seconds)
    else:
        f = f0 + (f1 - f0) * t / seconds
    phase = 2 * np.pi * np.cumsum(f) / SR
    return np.sin(phase)


def resonator(impulse_len, freqs, tau):
    t = t_axis(impulse_len)
    out = np.zeros_like(t)
    for i, f in enumerate(freqs):
        out += np.sin(2 * np.pi * f * t + i) * np.exp(-t / tau) / (1 + i * 0.6)
    return out


def place(dst, src, at_seconds, gain=1.0):
    i = int(round(at_seconds * SR))
    n = min(len(src), len(dst) - i)
    if n > 0:
        dst[i:i + n] += src[:n] * gain
    return dst


def fade_edges(x, fade_in=0.002, fade_out=0.01):
    x = x.copy()
    a = int(round(fade_in * SR))
    b = int(round(fade_out * SR))
    if a > 0:
        x[:a] *= np.linspace(0, 1, a)
    if b > 0:
        x[-b:] *= np.linspace(1, 0, b)
    return x


def normalize(x, peak_db=-1.0):
    p = np.max(np.abs(x))
    return x if p == 0 else x / p * 10 ** (peak_db / 20)


def loopify(x, loop_seconds, cross_seconds):
    """x must be at least loop+cross long; returns a seamless loop of loop_seconds."""
    n = int(round(loop_seconds * SR))
    f = int(round(cross_seconds * SR))
    out = x[:n].copy()
    ramp = np.linspace(0, 1, f)
    out[:f] = x[:f] * ramp + x[n:n + f] * (1 - ramp)
    return out


def circular_tail(x, loop_seconds):
    """Folds whatever rings past the loop end back onto its start, so a loop with decaying notes is seamless."""
    n = int(round(loop_seconds * SR))
    out = x[:n].copy()
    tail = x[n:]
    while len(tail) > 0:
        k = min(len(tail), n)
        out[:k] += tail[:k]
        tail = tail[k:]
    return out


def write(folder, name, x, peak_db=-1.0):
    x = normalize(x, peak_db)
    data = np.clip(x, -1, 1)
    wavfile.write(os.path.join(folder, name + ".wav"), SR, (data * 32767).astype(np.int16))


# ---------------------------------------------------------------- bullet

def bullet_air_loop():
    # Air tearing past a bullet, with a 57 Hz flutter: in normal time the rings come too fast to hear one by one, so the loop buzzes.
    L, C = 1.0, 0.2
    t = t_axis(L + C)
    air = band(noise(L + C), 700, 4200, 2)
    whistle = np.sin(2 * np.pi * 1180 * t + 0.004 * np.sin(2 * np.pi * 6 * t) * 2 * np.pi * 60) * 0.12
    am = 0.35 + 0.65 * (0.5 + 0.5 * np.sin(2 * np.pi * 57 * t)) ** 2
    hum = np.sin(2 * np.pi * 114 * t) * 0.18 * am
    return loopify(air * am + whistle + hum, L, C)


def bullet_pulse():
    # One "вуб" per second air ring in slow motion (about 5 a second at the deepest): a short low push with a breath of air.
    d = 0.15
    env = decay(d, 0.032, attack=0.008)
    body = sweep(d, 170, 95) * env
    air = band(noise(d), 260, 950, 2) * decay(d, 0.025, attack=0.006) * 0.8
    return fade_edges(body + air, 0.001, 0.03)


def near_air_loop():
    # The roar right beside the ear: wider band air plus low rumble, fluttering at 9 Hz.
    L, C = 1.0, 0.2
    t = t_axis(L + C)
    air = band(noise(L + C), 300, 6500, 2)
    rumble = low(noise(L + C), 160, 2) * 2.5
    flutter = 0.7 + 0.3 * np.sin(2 * np.pi * 9 * t)
    return loopify((air + rumble) * flutter, L, C)


def near_whump():
    # The pressure push as the bullet crosses the ear.
    d = 0.5
    boom = sweep(d, 70, 42) * decay(d, 0.11, attack=0.004)
    thump = low(noise(d), 380, 2) * decay(d, 0.05, attack=0.003) * 3
    hiss = band(noise(d), 2000, 8000, 2) * decay(d, 0.025, attack=0.002) * 0.5
    return fade_edges(boom + thump + hiss)


def ear_ring():
    d = 1.5
    t = t_axis(d)
    tone = np.sin(2 * np.pi * 3150 * t) + 0.6 * np.sin(2 * np.pi * 3870 * t)
    env = np.clip(t / 0.04, 0, 1) * np.exp(-t / 0.5)
    return fade_edges(tone * env, 0.002, 0.1)


# ---------------------------------------------------------------- guns

def gunshot(d, crack, body_lp, body_tau, boom_from, boom_to, boom_tau, echoes, tail_gain, tail_lp):
    dry = np.zeros(int(round(d * SR)))
    place(dry, high(noise(0.004), 2000) * crack, 0)
    place(dry, low(noise(0.3), body_lp, 2) * decay(0.3, body_tau, 0.0005) * 1.3, 0)
    place(dry, sweep(0.5, boom_from, boom_to) * decay(0.5, boom_tau, 0.002) * 0.9, 0)
    out = dry.copy()
    for delay, gain, lp in echoes:
        place(out, low(dry, lp, 2), delay, gain)
    tail = low(noise(d), tail_lp, 2) * decay(d, 0.3, 0.03) * tail_gain
    place(out, tail, 0.05)
    out = np.tanh(out * 1.5)
    return fade_edges(out, 0.0, 0.08)


def enemy_shot_near():
    return gunshot(1.3, 1.0, 3200, 0.04, 85, 48, 0.12, [(0.11, 0.42, 2600), (0.23, 0.2, 1800)], 0.12, 1600)


def enemy_shot_far():
    return gunshot(1.6, 0.3, 1700, 0.05, 70, 42, 0.14, [(0.13, 0.6, 1500), (0.27, 0.35, 1100), (0.41, 0.18, 900)], 0.2, 1000)


def pistol_shot():
    return gunshot(1.0, 1.4, 4500, 0.03, 120, 70, 0.08, [(0.09, 0.35, 3000), (0.19, 0.15, 2000)], 0.08, 2200)


def click(freqs, tau, length=0.06):
    imp = resonator(length, freqs, tau)
    return fade_edges(imp, 0.0005, 0.005)


def gun_draw():
    d = 0.32
    out = np.zeros(int(round(d * SR)))
    rustle = band(noise(0.12), 1200, 5500, 2)
    rustle *= np.sin(np.linspace(0, np.pi, len(rustle))) ** 2
    place(out, rustle, 0.0, 0.6)
    place(out, click([3200, 5100, 7300], 0.012), 0.12, 1.0)
    place(out, click([2400, 3900, 6100], 0.016), 0.2, 0.9)
    return fade_edges(out)


def gun_holster():
    d = 0.38
    out = np.zeros(int(round(d * SR)))
    place(out, click([2600, 4300], 0.01), 0.01, 0.7)
    rustle = band(noise(0.28), 700, 3800, 2)
    rustle *= np.linspace(1, 0, len(rustle)) ** 1.5
    place(out, rustle, 0.05, 0.5)
    return fade_edges(out)


# ---------------------------------------------------------------- impacts

def enemy_hit():
    d = 0.55
    out = np.zeros(int(round(d * SR)))
    place(out, sweep(0.4, 75, 44) * decay(0.4, 0.09, 0.002), 0)
    place(out, low(noise(0.3), 650, 2) * decay(0.3, 0.05, 0.001) * 2.2, 0)
    cloth = band(noise(0.08), 800, 3200, 2) * decay(0.08, 0.02, 0.001)
    cloth *= 0.6 + 0.4 * (rng.random(len(cloth)) > 0.6)
    place(out, cloth, 0.004, 0.9)
    return fade_edges(np.tanh(out * 1.3))


def pluck(freq, seconds, damping=0.996, brightness=0.5):
    n = int(round(seconds * SR))
    period = int(SR / freq)
    buf = low(rng.uniform(-1, 1, period), 1200 + 4000 * brightness, 1)
    out = np.zeros(n)
    for i in range(n):
        v = buf[i % period]
        out[i] = v
        buf[i % period] = damping * 0.5 * (v + buf[(i + 1) % period])
    return out


def hit_note():
    d = 1.8
    bass = pluck(73.42, d, 0.9975, 0.35)
    bass = low(bass, 900, 2) * decay(d, 0.7, 0.003)
    return fade_edges(bass, 0.001, 0.2)


def sparks_crackle(d, count, lo, hi):
    out = np.zeros(int(round(d * SR)))
    for _ in range(count):
        at = rng.uniform(0, d * 0.7) ** 1.3 / (d * 0.7) ** 0.3
        f = rng.uniform(lo, hi)
        place(out, click([f, f * 1.7], rng.uniform(0.002, 0.006), 0.02), at, rng.uniform(0.2, 0.7))
    return out


def ricochet_metal():
    d = 1.2
    out = np.zeros(int(round(d * SR)))
    ping = resonator(0.8, [2100, 3420, 5630, 7910], 0.18)
    place(out, ping, 0, 0.8)
    t = t_axis(0.9)
    f = 2600 * (900 / 2600) ** (t / 0.9) * (1 + 0.03 * np.sin(2 * np.pi * 18 * t))
    whine = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.clip(t / 0.02, 0, 1) * np.exp(-t / 0.35)
    place(out, whine, 0.02, 0.6)
    place(out, high(sparks_crackle(0.5, 22, 3000, 7000), 1500), 0.01, 0.5)
    return fade_edges(out, 0.0005, 0.1)


def ricochet_concrete():
    d = 1.0
    out = np.zeros(int(round(d * SR)))
    place(out, band(noise(0.15), 500, 3200, 2) * decay(0.15, 0.025, 0.0005), 0, 1.4)
    t = t_axis(0.6)
    f = 1800 * (700 / 1800) ** (t / 0.6)
    whine = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.clip(t / 0.02, 0, 1) * np.exp(-t / 0.2)
    place(out, whine, 0.03, 0.35)
    place(out, sparks_crackle(0.6, 30, 1200, 4500), 0.01, 0.6)
    return fade_edges(out, 0.0005, 0.1)


# ---------------------------------------------------------------- slow motion

def slowmo_enter():
    d = 0.42
    t = t_axis(d)
    x = noise(d)
    out = np.zeros_like(x)
    # A breath drawn in: the band climbs and swells, then stops short.
    for i, (lo, hi) in enumerate([(300, 700), (600, 1400), (1100, 2600)]):
        seg_env = np.clip((t / d) * 3 - i, 0, 1)
        out += band(x, lo, hi, 2) * seg_env
    out *= (t / d) ** 2
    out += np.sin(2 * np.pi * 58 * t) * (t / d) ** 3 * 0.8
    return fade_edges(out, 0.005, 0.012)


def slowmo_exit():
    d = 0.36
    t = t_axis(d)
    x = noise(d)
    out = np.zeros_like(x)
    for i, (lo, hi) in enumerate([(1100, 2600), (600, 1400), (300, 700)]):
        seg_env = np.clip(1 - np.abs((t / d) * 3 - i - 0.5), 0, 1)
        out += band(x, lo, hi, 2) * seg_env
    out *= np.clip(t / 0.01, 0, 1) * np.exp(-t / 0.14)
    return fade_edges(out, 0.002, 0.03)


def slowmo_bed_loop():
    # Low drone under slow motion; every partial makes whole cycles in 4 s so the loop is seamless.
    L = 4.0
    t = t_axis(L)
    swell = 0.75 + 0.25 * np.sin(2 * np.pi * 0.25 * t)
    drone = (np.sin(2 * np.pi * 55 * t) + 0.55 * np.sin(2 * np.pi * 82.5 * t) + 0.35 * np.sin(2 * np.pi * 110.25 * t)) * swell
    air = low(noise(L + 0.5), 220, 2)
    air = loopify(air, L, 0.5) * 0.6
    return drone + air


def heartbeat_loop():
    L = 1.1
    out = np.zeros(int(round(L * SR)) + int(round(0.4 * SR)))
    lub = sweep(0.3, 62, 44) * decay(0.3, 0.06, 0.006) + low(noise(0.3), 120, 2) * decay(0.3, 0.03, 0.004) * 2
    dub = sweep(0.3, 54, 40) * decay(0.3, 0.05, 0.006) + low(noise(0.3), 110, 2) * decay(0.3, 0.025, 0.004) * 1.5
    place(out, lub, 0.0, 1.0)
    place(out, dub, 0.28, 0.7)
    return circular_tail(out, L)


# ---------------------------------------------------------------- tap window

def tap_focus():
    d = 0.65
    out = sweep(d, 140, 36) * decay(d, 0.22, 0.008)
    out += low(noise(d), 300, 2) * decay(d, 0.08, 0.004) * 2
    return fade_edges(out, 0.002, 0.05)


def clock_tick():
    out = resonator(0.07, [2800, 4200], 0.007) + resonator(0.07, [900], 0.012) * 0.6
    return fade_edges(out, 0.0003, 0.008)


def clock_tock():
    out = resonator(0.07, [2200, 3300], 0.008) + resonator(0.07, [700], 0.014) * 0.6
    return fade_edges(out, 0.0003, 0.008)


def tap_miss():
    d = 0.3
    out = resonator(d, [1200, 1900], 0.02) * 0.8 + resonator(d, [150], 0.03)
    return fade_edges(out, 0.0005, 0.03)


# ---------------------------------------------------------------- death

def death_impact():
    d = 0.9
    out = np.zeros(int(round(d * SR)))
    place(out, sweep(0.8, 62, 34) * decay(0.8, 0.2, 0.002), 0, 1.2)
    place(out, high(noise(0.006), 1500) * 1.5, 0)
    place(out, low(noise(0.4), 900, 2) * decay(0.4, 0.08, 0.001) * 2.5, 0)
    return fade_edges(np.tanh(out * 1.8), 0.0, 0.1)


def death_swell():
    d = 1.1
    t = t_axis(d)
    env = np.clip(t / 0.35, 0, 1) ** 2 * np.exp(-np.clip(t - 0.4, 0, None) / 0.3)
    tone = np.sin(2 * np.pi * 41 * t) + 0.6 * np.sin(2 * np.pi * 61.5 * t) + 0.4 * np.sin(2 * np.pi * 82 * t)
    rumble = low(noise(d), 300, 2) * 1.5
    return fade_edges((tone + rumble) * env, 0.002, 0.08)


def piano_note(freq, seconds, gain=1.0):
    t = t_axis(seconds)
    out = np.zeros_like(t)
    for k in range(1, 9):
        fk = freq * k * (1 + 0.0004 * k * k)
        if fk > 9000:
            break
        out += np.sin(2 * np.pi * fk * t) / k ** 1.3 * np.exp(-t / (1.6 / k ** 0.6))
    hammer = band(noise(0.02), 800, 4000, 2) * decay(0.02, 0.004, 0.0005) * 0.3
    out[:len(hammer)] += hammer
    return out * gain * np.clip(t / 0.003, 0, 1)


def death_chord():
    d = 2.6
    out = np.zeros(int(round(d * SR)))
    # A low, dark cluster: D2 A2 F3 C4 Eb4 (minor with a flat ninth).
    for i, f in enumerate([73.42, 110.0, 174.61, 261.63, 311.13]):
        place(out, piano_note(f, d - 0.01 * i, 1.0 - 0.1 * i), 0.01 * i)
    return fade_edges(out, 0.001, 0.3)


# ---------------------------------------------------------------- temporary music

def music_loop():
    # Stand-in noir jazz until the user's reference: walking bass, brushes, a few piano stabs.
    # 120 bpm, 8 bars of 4 in D minor; tails folded back so the loop is seamless.
    beat = 0.5
    bars = 8
    L = bars * 4 * beat
    out = np.zeros(int((L + 2.5) * SR))
    note = lambda name: {"D2": 73.42, "E2": 82.41, "F2": 87.31, "G2": 98.0, "A2": 110.0, "Bb2": 116.54,
                         "C3": 130.81, "C#3": 138.59, "D3": 146.83, "E3": 164.81, "F3": 174.61, "G3": 196.0,
                         "A3": 220.0, "Bb3": 233.08, "C4": 261.63, "D4": 293.66, "E4": 329.63, "F4": 349.23,
                         "G4": 392.0, "A4": 440.0, "C#4": 277.18}[name]
    walk = ["D2", "F2", "A2", "C3", "G2", "Bb2", "D3", "C3", "A2", "C#3", "E3", "G2", "D2", "E2", "F2", "A2",
            "D2", "F2", "A2", "D3", "G2", "A2", "Bb2", "D3", "A2", "G2", "F2", "E2", "D2", "A2", "D3", "C#3"]
    for i, n in enumerate(walk):
        b = pluck(note(n), 0.7, 0.995, 0.4)
        b = low(b, 1100, 2) * decay(0.7, 0.35, 0.004)
        place(out, b, i * beat, 0.9)
    # Brushes: a long swish on 2 and 4, light taps on swung eighths.
    for bar in range(bars):
        for k in range(4):
            at = (bar * 4 + k) * beat
            if k % 2 == 1:
                sw = band(noise(0.35), 2500, 9000, 2) * np.sin(np.linspace(0, np.pi, int(round(0.35 * SR)))) ** 1.5
                place(out, sw, at - 0.05, 0.16)
            tap = high(noise(0.05), 6000) * decay(0.05, 0.012, 0.0005)
            place(out, tap, at, 0.1)
            place(out, tap, at + beat * 0.66, 0.07)
    chords = [["D3", "F3", "A3", "C4"], ["G3", "Bb3", "D4", "F4"], ["A3", "C#4", "E4", "G4"], ["D3", "F3", "A3", "C4"]]
    for bar in range(bars):
        chord = chords[bar % 4]
        at = (bar * 4 + 1.5) * beat
        for j, n in enumerate(chord):
            place(out, piano_note(note(n), 1.4, 0.18), at + 0.008 * j)
    return circular_tail(out, L)


SOUNDS = {
    "bullet_air_loop": bullet_air_loop,
    "bullet_pulse": bullet_pulse,
    "near_air_loop": near_air_loop,
    "near_whump": near_whump,
    "ear_ring": ear_ring,
    "enemy_shot_near": enemy_shot_near,
    "enemy_shot_far": enemy_shot_far,
    "pistol_shot": pistol_shot,
    "gun_draw": gun_draw,
    "gun_holster": gun_holster,
    "enemy_hit": enemy_hit,
    "hit_note": hit_note,
    "ricochet_metal": ricochet_metal,
    "ricochet_concrete": ricochet_concrete,
    "slowmo_enter": slowmo_enter,
    "slowmo_exit": slowmo_exit,
    "slowmo_bed_loop": slowmo_bed_loop,
    "heartbeat_loop": heartbeat_loop,
    "tap_focus": tap_focus,
    "clock_tick": clock_tick,
    "clock_tock": clock_tock,
    "tap_miss": tap_miss,
    "death_impact": death_impact,
    "death_swell": death_swell,
    "death_chord": death_chord,
    "music_loop": music_loop,
}

if __name__ == "__main__":
    folder = sys.argv[1]
    os.makedirs(folder, exist_ok=True)
    for name, make in SOUNDS.items():
        x = make()
        write(folder, name, x, -3.0 if name.endswith("_loop") else -1.0)
        print(f"{name}: {len(x) / SR:.2f} s")
