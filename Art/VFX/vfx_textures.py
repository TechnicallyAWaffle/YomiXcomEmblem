# Generates the combat VFX textures into Assets/Resources/VFX/Tex (see Art/VFX/README.md).
# Channels: R = edge band (where an ink shape shows its colour), G = dissolve noise, B = R, A = the shape.
# Needs Python 3 with numpy and pymupdf (pip install numpy pymupdf). Run from anywhere: python vfx_textures.py
import sys, os, math
import numpy as np
import pymupdf as fitz

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'Assets', 'Resources', 'VFX', 'Tex')
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(7)

def value_noise(h, w, cells, octaves=4):
    out = np.zeros((h, w)); amp = 1.0; tot = 0.0
    for o in range(octaves):
        c = cells * (2 ** o)
        g = rng.random((c + 1, c + 1))
        y = np.linspace(0, c, h, endpoint=False); x = np.linspace(0, c, w, endpoint=False)
        yi = y.astype(int); xi = x.astype(int); yf = y - yi; xf = x - xi
        yf = yf * yf * (3 - 2 * yf); xf = xf * xf * (3 - 2 * xf)
        a = g[yi][:, xi]; b = g[yi][:, xi + 1]; cc = g[yi + 1][:, xi]; d = g[yi + 1][:, xi + 1]
        top = a + (b - a) * xf[None, :]; bot = cc + (d - cc) * xf[None, :]
        out += (top + (bot - top) * yf[:, None]) * amp; tot += amp; amp *= 0.5
    out /= tot
    return (out - out.min()) / max(1e-6, out.max() - out.min())

def blur(a, r):
    if r <= 0: return a
    k = np.ones(2 * r + 1) / (2 * r + 1)
    a = np.apply_along_axis(lambda m: np.convolve(m, k, mode='same'), 0, a)
    return np.apply_along_axis(lambda m: np.convolve(m, k, mode='same'), 1, a)

def edge_band(shape, width):
    """1 near the shape's edge (inside), fading to 0 deeper in: where an ink shape shows its colour."""
    inner = shape.copy()
    for _ in range(width): inner = np.minimum(inner, np.minimum.reduce([np.roll(inner, 1, 0), np.roll(inner, -1, 0), np.roll(inner, 1, 1), np.roll(inner, -1, 1)]))
    return np.clip(shape - inner, 0, 1)

def save(name, shape, rim=None, noise=None):
    h, w = shape.shape
    if rim is None: rim = np.ones_like(shape)
    if noise is None: noise = value_noise(h, w, 6)
    rgba = np.dstack([rim, noise, rim, shape])
    rgba = (np.clip(rgba, 0, 1) * 255 + 0.5).astype(np.uint8)
    rgba = rgba[::-1]  # Unity reads PNG top-down like any image; keep the drawing's "up" up
    pm = fitz.Pixmap(fitz.csRGB, w, h, rgba.tobytes(), 1)
    pm.save(os.path.join(OUT, name + '.png'))
    print('wrote', name, w, h)

def grid(h, w):
    y, x = np.mgrid[0:h, 0:w].astype(np.float64)
    return (x + 0.5) / w * 2 - 1, (y + 0.5) / h * 2 - 1   # -1..1, y up after the flip in save

def polygon_mask(h, w, pts):
    """Even-odd fill of a polygon given in -1..1 coordinates."""
    X, Y = grid(h, w)
    inside = np.zeros((h, w), bool)
    n = len(pts)
    for i in range(n):
        x1, y1 = pts[i]; x2, y2 = pts[(i + 1) % n]
        cond = ((y1 > Y) != (y2 > Y)) & (X < (x2 - x1) * (Y - y1) / ((y2 - y1) + 1e-12) + x1)
        inside ^= cond
    return inside.astype(np.float64)

# ---- ink shard: a long jagged spike pointing up (stretched along its travel)
def shard():
    h, w = 256, 64
    pts = [(0.0, 1.0)]
    for i in range(1, 9):
        t = i / 9; y = 1 - 2 * t
        pts.append((0.15 + 0.75 * t ** 0.8 * (0.7 + 0.5 * rng.random()), y))
    pts.append((0.0, -1.0))
    for i in range(8, 0, -1):
        t = i / 9; y = 1 - 2 * t
        pts.append((-(0.15 + 0.75 * t ** 0.8 * (0.7 + 0.5 * rng.random())), y))
    # Lying along its length, tip to the right: a stretched particle draws its texture's U along its motion.
    h, w = 64, 256
    pts = [(y, x) for (x, y) in pts]
    s = blur(polygon_mask(h, w, pts), 1)
    save('shard', s, edge_band((s > 0.5).astype(float), 5) * 0.9 + 0.1 * s)

# ---- ink splat: a blob with spikes and flung droplets
def splat():
    n = 256
    X, Y = grid(n, n)
    r = np.hypot(X, Y); a = np.arctan2(Y, X)
    edge = 0.42 + 0.05 * np.sin(a * 5 + 1) + 0.04 * np.sin(a * 11 + 2)
    spikes = np.zeros_like(a)
    for k in range(14):
        c = rng.random() * 2 * np.pi; wdt = 0.04 + 0.05 * rng.random(); ln = 0.2 + 0.35 * rng.random()
        d = np.angle(np.exp(1j * (a - c)))
        spikes = np.maximum(spikes, ln * np.clip(1 - np.abs(d) / wdt, 0, 1) ** 1.5)
    shape = (r < edge + spikes).astype(float)
    for k in range(18):
        c = rng.random() * 2 * np.pi; rr = 0.6 + 0.3 * rng.random(); s = 0.015 + 0.035 * rng.random()
        shape = np.maximum(shape, (np.hypot(X - rr * np.cos(c), Y - rr * np.sin(c)) < s).astype(float))
    shape = blur(shape, 1)
    save('splat', shape, edge_band((shape > 0.5).astype(float), 7))

# ---- radiant rays: thin lines of random length from the centre, and a small hot core (golden bursts)
def rays():
    n = 256
    X, Y = grid(n, n)
    r = np.hypot(X, Y); a = np.arctan2(Y, X)
    s = np.zeros_like(r)
    for k in range(70):
        c = rng.random() * 2 * np.pi; ln = 0.35 + 0.6 * rng.random() ** 0.7
        d = np.abs(np.angle(np.exp(1j * (a - c)))) * r   # distance from the ray's line
        wdt = 0.004 + 0.006 * rng.random()
        line = np.clip(1 - d / wdt, 0, 1) * np.clip(1 - r / ln, 0, 1) ** 0.6
        s = np.maximum(s, line)
    s = np.maximum(s, np.clip(1 - r / 0.12, 0, 1) ** 1.5)
    save('rays', np.clip(s * 1.3, 0, 1))

# ---- brush ring: a rough, broken annulus (shockwaves, telegraph edges)
def ring():
    n = 256
    X, Y = grid(n, n)
    r = np.hypot(X, Y); a = np.arctan2(Y, X)
    nz = value_noise(n, n, 5)
    th = 0.06 + 0.035 * np.sin(a * 3 + 0.5) + 0.03 * (nz - 0.5)
    band = np.clip(1 - np.abs(r - 0.86) / th, 0, 1)
    gaps = np.clip((np.sin(a * 2 + 0.7) * 0.5 + 0.5) * 3 - 0.2, 0, 1)   # two thin breaks
    streak = 0.75 + 0.25 * np.sin(a * 90 + nz * 10)                     # bristle marks along it
    shape = np.clip(band * 1.6, 0, 1) * gaps * streak
    save('ring', shape)

# ---- sigil: a rune circle (windup telegraphs and big moments)
def sigil():
    n = 512
    X, Y = grid(n, n)
    r = np.hypot(X, Y); a = np.arctan2(Y, X)
    def line_ring(rad, w): return np.clip(1 - np.abs(r - rad) / w, 0, 1)
    s = np.maximum(line_ring(0.95, 0.02), line_ring(0.86, 0.013))
    s = np.maximum(s, line_ring(0.5, 0.013))
    # tick marks between the outer rings
    ticks = (np.abs(np.angle(np.exp(1j * a * 48))) < 0.25) & (r > 0.86) & (r < 0.95)
    s = np.maximum(s, ticks.astype(float))
    # glyphs: little strokes and dots in the band between 0.5 and 0.86
    for k in range(16):
        c = k / 16 * 2 * np.pi + 0.1; rr = 0.68
        cx, cy = rr * np.cos(c), rr * np.sin(c)
        kind = k % 4
        dx, dy = X - cx, Y - cy
        if kind == 0: g = np.clip(1 - np.abs(np.hypot(dx, dy) - 0.05) / 0.013, 0, 1)
        elif kind == 1: g = ((np.abs(dx * np.cos(c) + dy * np.sin(c)) < 0.07) & (np.abs(-dx * np.sin(c) + dy * np.cos(c)) < 0.013)).astype(float)
        elif kind == 2: g = (np.hypot(dx, dy) < 0.022).astype(float)
        else: g = ((np.abs(dx) + np.abs(dy) < 0.06) & (np.abs(dx) + np.abs(dy) > 0.045)).astype(float)
        s = np.maximum(s, g)
    # an inverted triangle and a star of lines inside
    for k in range(3):
        c1 = -np.pi / 2 + k * 2 * np.pi / 3; c2 = c1 + 2 * np.pi / 3
        p1 = np.array([0.5 * np.cos(c1), 0.5 * np.sin(c1)]); p2 = np.array([0.5 * np.cos(c2), 0.5 * np.sin(c2)])
        d = p2 - p1; L = np.linalg.norm(d); u = d / L
        t = np.clip((X - p1[0]) * u[0] + (Y - p1[1]) * u[1], 0, L)
        dist = np.hypot(X - (p1[0] + u[0] * t), Y - (p1[1] + u[1] * t))
        s = np.maximum(s, np.clip(1 - dist / 0.013, 0, 1))
    s = np.maximum(s, line_ring(0.12, 0.016))
    save('sigil', np.clip(s, 0, 1))

# ---- stroke (tiles along its length): a brushy hatched stroke for lines, trails and beams
def stroke():
    h, w = 64, 256
    y = np.linspace(-1, 1, h)[:, None] * np.ones((1, w))
    x = np.linspace(0, 1, w, endpoint=False)[None, :] * np.ones((h, 1))
    s = np.zeros((h, w))
    for k in range(30):   # bristle strands across the width
        cy = rng.uniform(-0.85, 0.85); wd = rng.uniform(0.04, 0.11)
        phase = rng.random(); freq = rng.integers(1, 4)
        strength = 0.55 + 0.45 * (0.5 + 0.5 * np.sin(2 * np.pi * (x * freq + phase)))
        s = np.maximum(s, np.clip(1 - np.abs(y - cy - 0.05 * np.sin(2 * np.pi * (x * 2 + phase))) / wd, 0, 1) * strength)
    body = np.clip(1 - np.abs(y) / 0.95, 0, 1) ** 0.5
    s = np.clip(s * 1.2, 0, 1) * body
    save('stroke_tile', s)

# ---- halo: a thin bright ellipse ring with a soft glow (seen from the side: a halo over a head)
def halo():
    n = 256
    X, Y = grid(n, n)
    r = np.hypot(X, Y)
    core = np.clip(1 - np.abs(r - 0.8) / 0.06, 0, 1)
    glow = np.clip(1 - np.abs(r - 0.8) / 0.2, 0, 1) ** 2 * 0.6
    save('halo', np.clip(core + glow, 0, 1))

# ---- glyphs for status markers: 4 x 1 strip (chevron up, chevron down, eye, diamond cross)
def glyphs():
    n = 128
    tiles = []
    X, Y = grid(n, n)
    def seg(p1, p2, wdt):
        p1 = np.array(p1); p2 = np.array(p2); d = p2 - p1; L = np.linalg.norm(d); u = d / L
        t = np.clip((X - p1[0]) * u[0] + (Y - p1[1]) * u[1], 0, L)
        return np.clip(1 - np.hypot(X - (p1[0] + u[0] * t), Y - (p1[1] + u[1] * t)) / wdt, 0, 1)
    up = np.maximum.reduce([seg((-0.6, -0.15), (0, 0.45), 0.12), seg((0.6, -0.15), (0, 0.45), 0.12),
                            seg((-0.6, -0.65), (0, -0.05), 0.1), seg((0.6, -0.65), (0, -0.05), 0.1)])
    tiles.append(up)
    tiles.append(up[::-1].copy())   # chevrons down
    r = np.hypot(X, Y)
    lid = np.maximum(np.clip(1 - np.abs(np.hypot(X, Y + 0.55) - 1.0) / 0.07, 0, 1) * (np.abs(X) < 0.82),
                     np.clip(1 - np.abs(np.hypot(X, Y - 0.55) - 1.0) / 0.07, 0, 1) * (np.abs(X) < 0.82))
    eye = np.maximum(lid, (r < 0.22).astype(float))
    eye = np.maximum(eye, np.clip(1 - np.abs(r - 0.34) / 0.04, 0, 1))
    tiles.append(eye)
    dia = np.clip(1 - np.abs(np.abs(X) + np.abs(Y) - 0.6) / 0.08, 0, 1)
    dia = np.maximum.reduce([dia, seg((0, -0.9), (0, 0.9), 0.06), seg((-0.9, 0), (0.9, 0), 0.06)])
    tiles.append(dia)
    save('glyphs', np.clip(np.hstack(tiles), 0, 1))

# ---- soft dot (dust and smoke puffs)
def dot():
    n = 64
    X, Y = grid(n, n)
    r = np.hypot(X, Y)
    nz = value_noise(n, n, 3)
    save('puff', np.clip(1 - r, 0, 1) ** 1.5 * (0.7 + 0.3 * nz))

for f in (shard, splat, rays, ring, sigil, stroke, halo, glyphs, dot): f()
