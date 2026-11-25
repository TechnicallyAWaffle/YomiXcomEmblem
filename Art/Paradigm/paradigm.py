# The Paradigm Caster: "not an inch of their skin left unblemished". A robed caster whose bare skin (head, chest, arms,
# hands) is all Etched, with a halo frame behind the head. No weapon. Run by Art/common/build_unit.py.
# Clips: the shared set (shared Windup / Strike: a two-handed cast), plus spark (Spark: hand drawn back by the ear,
# then snapped forward) and cataclysm (Cataclysm: a long channel with both arms raised, then brought down).
import numpy as np


def build_mesh():
    m = MeshBuilder()
    def side(s, k, X):
        t = (0, 1, 2, 3) if s == 'L' else (4, 5, 6, 7)
        m.box(X(0.04, 0.16), (-0.065, 0.065), (0.48, 0.88), 'thigh.' + s)
        m.box(X(0.05, 0.15), (-0.055, 0.055), (0.1, 0.48), 'shin.' + s)
        m.box(X(0.045, 0.155), (-0.15, 0.065), (0.0, 0.08), 'foot.' + s)
        m.box(X(0.0, 0.21), (-0.125, 0.125), (0.14, 0.56), 'thigh.' + s)            # the robe's split lower half
        m.box(X(0.215, 0.325), (-0.055, 0.055), (1.14, 1.42), 'upper_arm.' + s, 'Etched', [t[0], t[1], t[0], t[1]])
        m.box(X(0.22, 0.32), (-0.05, 0.05), (0.88, 1.14), 'forearm.' + s, 'Etched', [t[2], t[3], t[2], t[3]])
        m.box(X(0.214, 0.326), (-0.056, 0.056), (0.93, 0.97), 'forearm.' + s, 'Metal')   # bands
        m.box(X(0.214, 0.326), (-0.056, 0.056), (1.0, 1.03), 'forearm.' + s, 'Metal')
        m.box(X(0.228, 0.312), (-0.045, 0.045), (0.77, 0.885), 'hand.' + s, 'Etched', [t[3]])
    sym(side)
    m.box((-0.21, 0.21), (-0.13, 0.13), (0.52, 0.98), 'hips')                            # robe skirt
    m.box((-0.17, 0.17), (-0.1, 0.1), (1.0, 1.44), 'spine', 'Etched', [0, 5, 2, 7])
    m.box((-0.19, 0.19), (-0.115, 0.115), (0.94, 1.12), 'spine')                         # sash / robe top
    for x in (-0.14, 0.06):                                                               # stole hanging over the chest
        m.box((x, x + 0.08), (-0.125, -0.1), (0.7, 1.44), 'spine')
        m.box((x, x + 0.08), (0.1, 0.125), (0.95, 1.44), 'spine')
    m.box((-0.2, 0.2), (-0.12, 0.12), (1.4, 1.46), 'spine')                              # stole over the shoulders
    m.box((-0.105, 0.105), (-0.115, 0.1), (1.46, 1.7), 'head', 'Etched', [6, 1, 4, 3])
    # Halo frame behind the head: four thin bars in a square, standing upright.
    cy, cz, r, w = 0.2, 1.64, 0.2, 0.018
    m.box((-r, r), (cy - w, cy + w), (cz + r - w, cz + r + w), 'head', 'Metal')
    m.box((-r, r), (cy - w, cy + w), (cz - r - w, cz - r + w), 'head', 'Metal')
    m.box((r - w, r + w), (cy - w, cy + w), (cz - r, cz + r), 'head', 'Metal')
    m.box((-r - w, -r + w), (cy - w, cy + w), (cz - r, cz + r), 'head', 'Metal')
    m.diamond((0.0, cy, cz), 0.04, 0.07, 'head', 'Metal')
    return m.finish()


# ---- the etching pattern: 8 tiles of script, rings, spirals and eyes (x, y in 0..1 over a 128 x 256 tile, y up)
def _px(x, y): return x * 128.0, y * 256.0
def _hash(a, b): return np.mod(np.sin(a * 12.9898 + b * 78.233) * 43758.5453, 1.0)
def t_squares(x, y):
    X, Y = _px(x, y); cy = np.where(Y < 128, 64, 192)
    return np.mod(np.maximum(abs(X - 64), abs(Y - cy)), 16) < 5
def t_script(x, y, cw=32, ch=32):
    X, Y = _px(x, y); cx, cy = np.floor(X / cw), np.floor(Y / ch); u, v = np.mod(X, cw) / cw, np.mod(Y, ch) / ch
    h = _hash(cx, cy); h2 = _hash(cy + 3.1, cx + 7.7)
    bar = (abs(u - 0.5) < 0.09) & (v > 0.15) & (v < 0.85)
    hor = (abs(v - (0.25 + 0.5 * h2)) < 0.08) & (u > 0.15) & (u < 0.85)
    dia = (abs(u - v) < 0.1) & (u > 0.15) & (u < 0.85)
    hook = (abs(u - 0.2) < 0.09) & (v > 0.5) & (v < 0.85)
    return np.where(h < 0.33, bar | hor, np.where(h < 0.66, dia | hook, bar | dia))
def t_spiral(x, y):
    X, Y = _px(x, y); dx, dy = X - 64, Y - 128; r = np.hypot(dx, dy); th = np.arctan2(dy, dx)
    return (np.mod(r - th * 15 / (2 * np.pi), 15) < 5) & (r < 60) | ((r > 70) & (np.mod(Y, 20) < 4))
def t_eyes(x, y):
    X, Y = _px(x, y); cy = np.round((Y - 43) / 85) * 85 + 43
    e = ((X - 64) / 54) ** 2 + ((Y - cy) / 26) ** 2
    return ((e > 0.75) & (e < 1.0)) | (np.hypot(X - 64, Y - cy) < 11)
def t_chevrons(x, y):
    X, Y = _px(x, y); return np.mod(Y + abs(X - 64), 24) < 7
def t_rings(x, y):
    X, Y = _px(x, y); c = 42.67; u, v = np.mod(X, c) - c / 2, np.mod(Y, c) - c / 2
    r = np.hypot(u, v); a = np.arctan2(v, u); gap = np.mod(a + _hash(np.floor(X / c), np.floor(Y / c)) * 6.28, 6.28) < 1.2
    return (abs(r - 13) < 3) & ~gap | (r < 3)
def t_hatch(x, y):
    X, Y = _px(x, y); return (np.mod(X + Y, 22) < 4) | (np.mod(X - Y, 22) < 4)
def t_columns(x, y): return t_script(x, y, 21.3, 26)
ETCHINGS = [t_squares, t_script, t_spiral, t_eyes, t_chevrons, t_rings, t_hatch, t_columns]


OPEN = {'hand.L': {'dir': V(0.3, -0.45, -1)}, 'hand.R': {'dir': V(0.3, 0.45, -1)}}
def S(drop=0.0, lean=4, yaw=0, f=0.0, spread=0.0):
    return plus({
        'hips': {'off': V(f, 0, -0.04 - drop), 'yaw': yaw, 'lean': 2},
        'spine': {'yaw': 0, 'lean': lean},
        'head': {'yaw': -yaw, 'lean': -lean + 4},
        'leg.L': (stance_foot('L', 0.06 + f, -0.02), V(1, -0.1, 0)),
        'leg.R': (stance_foot('R', -0.06 + f, 0.02), V(1, 0.1, 0)),
        'arm.L': (V(0.12, -0.26 - spread, -0.4), V(-0.2, -1, -0.4)),
        'arm.R': (V(0.12, 0.26 + spread, -0.4), V(-0.2, 1, -0.4)),
    }, OPEN)


def build_clips():
    keys('Idle', 0, 80, [(0, S()), (20, S(drop=-0.015, spread=0.02)), (40, S()), (60, S(drop=-0.015, spread=0.02)), (80, S())])
    run_cycle(S, lambda ph: plus({'arm.L': (V(-0.06, -0.3, -0.42), V(-1, -0.5, 0)), 'arm.R': (V(-0.06, 0.3, -0.42), V(-1, 0.5, 0))},
                                 {'hand.L': {'dir': V(-0.4, -0.3, -1)}, 'hand.R': {'dir': V(-0.4, 0.3, -1)}}),
              stride=0.7, lean=10, lift=0.6)
    dashes(S)
    reactions(S)
    cross = lambda d: merge(S(drop=0.08 + d, lean=12), head={'yaw': 0, 'lean': -2},
                            arm_L=(V(0.27, 0.09, -0.02), V(0.3, -1, -0.7)), arm_R=(V(0.24, -0.1, -0.06), V(0.3, 1, -0.7)))
    keys('Block', 0, 6, [(0, S()), (3, cross(0.02)), (6, cross(0.0))])

    # Shared cast: gather between the palms at the chest, then push both hands out.
    gather = merge(S(drop=0.04, lean=8), hips={'off': V(-0.04, 0, -0.08), 'yaw': 0, 'lean': 2},
                   arm_L=(V(0.2, -0.06, -0.2), V(-0.2, -1, -0.6)), arm_R=(V(0.2, 0.06, -0.2), V(-0.2, 1, -0.6)),
                   hand_L={'dir': V(0.3, 0.8, 0.3)}, hand_R={'dir': V(0.3, -0.8, 0.3)})
    push = merge(S(drop=0.06, lean=12), hips={'off': V(0.08, 0, -0.1), 'yaw': 0, 'lean': 6},
                 arm_L=(V(0.52, -0.1, -0.12), V(0, -1, -0.3)), arm_R=(V(0.52, 0.1, -0.12), V(0, 1, -0.3)),
                 hand_L={'dir': V(0.3, 0, 1)}, hand_R={'dir': V(0.3, 0, 1)})
    keys('Windup', 0, 15, [(0, S()), (15, gather)])
    keys('Strike', 0, 16, [(0, gather), (3, push), (8, push), (16, S())])

    # Spark: the right hand drawn back beside the ear, the left reaching out to sight, then the right snaps forward.
    drawn = merge(S(drop=0.03, yaw=25), hips={'off': V(-0.04, 0, -0.07), 'yaw': 25, 'lean': 0}, head={'yaw': -25, 'lean': 2},
                  arm_R=(V(-0.06, 0.24, 0.02), V(-0.4, 1, -0.2)), hand_R={'dir': V(0.2, 0, 1)},
                  arm_L=(V(0.42, -0.12, -0.12), V(0, -1, -0.4)), hand_L={'dir': V(1, 0, 0.2)})
    snap = merge(S(drop=0.04, yaw=-20, lean=8), hips={'off': V(0.08, 0, -0.08), 'yaw': -20, 'lean': 6}, head={'yaw': 20, 'lean': -4},
                 leg_L=(stance_foot('L', 0.2, -0.04), V(1, -0.1, 0)),
                 arm_R=(V(0.56, 0.04, -0.02), V(0, 1, -0.3)), hand_R={'dir': V(1, 0, 0.35)},
                 arm_L=(V(-0.04, -0.3, -0.36), V(-1, -0.4, 0)))
    keys('spark.Windup', 0, 15, [(0, S()), (10, drawn), (15, drawn)])
    keys('spark.Strike', 0, 16, [(0, drawn), (2, snap), (8, snap), (16, S())])

    # Cataclysm: a long channel, arms rising out to the sides and up over the head, then a crash down.
    rising = merge(S(drop=-0.03, lean=-4), hips={'off': V(0, 0, 0.0), 'yaw': 0, 'lean': -2}, head={'yaw': 0, 'lean': -14},
                   arm_L=(V(0.0, -0.52, -0.06), V(-1, 0, -0.5)), arm_R=(V(0.0, 0.52, -0.06), V(-1, 0, -0.5)),
                   hand_L={'dir': V(0, -1, 0.4)}, hand_R={'dir': V(0, 1, 0.4)})
    overhead = merge(S(drop=-0.06, lean=-8), hips={'off': V(0, 0, 0.03), 'yaw': 0, 'lean': -4}, head={'yaw': 0, 'lean': -26},
                     arm_L=(V(0.04, -0.2, 0.48), V(0, -1, 0)), arm_R=(V(0.04, 0.2, 0.48), V(0, 1, 0)),
                     hand_L={'dir': V(0.1, 0.2, 1)}, hand_R={'dir': V(0.1, -0.2, 1)})
    crash = merge(S(drop=0.26, lean=26), hips={'off': V(0.1, 0, -0.3), 'yaw': 0, 'lean': 22}, head={'yaw': 0, 'lean': -30},
                  leg_L=(stance_foot('L', 0.3, -0.06), V(1, -0.3, 0)), leg_R=(stance_foot('R', -0.3, 0.06, 0.02), V(1, 0.3, 0)),
                  arm_L=(V(0.42, -0.16, -0.56), V(0, -1, 0)), arm_R=(V(0.42, 0.16, -0.56), V(0, 1, 0)),
                  hand_L={'dir': V(0.4, 0, -1)}, hand_R={'dir': V(0.4, 0, -1)})
    keys('cataclysm.Windup', 0, 36, [(0, S()), (12, rising), (26, overhead), (36, merge(overhead, hips={'off': V(0, 0, 0.05), 'yaw': 0, 'lean': -4}))])
    keys('cataclysm.Strike', 0, 30, [(0, overhead), (4, crash), (18, crash), (30, S())])


PREVIEW = [('Idle', 0), ('Run', 0), ('Run', 4), ('Block', 6), ('Windup', 15), ('Strike', 4),
           ('spark.Windup', 15), ('spark.Strike', 3), ('cataclysm.Windup', 12), ('cataclysm.Windup', 36),
           ('cataclysm.Strike', 5), ('DashSide', 6), ('HitHeavy', 3), ('Stun', 0), ('Death', 8), ('Death', 24)]
