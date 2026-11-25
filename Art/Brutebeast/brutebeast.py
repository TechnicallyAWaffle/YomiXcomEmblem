# The Brutebeast: a disproportionate, monstrous humanoid on small legs, its right arm hugely swollen and long enough to
# walk on: it goes about on its knuckles, leaning on that arm. Its own skeleton (SKELETON): short legs, a hunched
# forward spine, a low head, a small left arm and a long right one. Run by Art/common/build_unit.py.
# Clips: the shared set (its standing pose and run with the right fist on the ground), plus ravage (Ravage, and its
# follow-up: the great arm raised high, then smashed down) and charge (Charge: head and shoulder down, then the rush).

SKELETON = {
    'hips': (V(0, 0, 0.6), V(0, 0, 0.74)),
    'spine': (V(0, 0, 0.74), V(0.12, 0, 1.26)),
    'head': (V(0.2, 0, 1.2), V(0.36, 0, 1.36)),
    'upper_arm.L': (V(0.06, -0.3, 1.18), V(0.08, -0.32, 0.92)), 'forearm.L': (V(0.08, -0.32, 0.92), V(0.08, -0.32, 0.68)),
    'hand.L': (V(0.08, -0.32, 0.68), V(0.08, -0.32, 0.6)),
    'upper_arm.R': (V(0.06, 0.44, 1.2), V(0.08, 0.5, 0.72)), 'forearm.R': (V(0.08, 0.5, 0.72), V(0.1, 0.52, 0.24)),
    'hand.R': (V(0.1, 0.52, 0.24), V(0.12, 0.52, 0.08)),
    'thigh.L': (V(0, -0.15, 0.6), V(0.03, -0.16, 0.34)), 'shin.L': (V(0.03, -0.16, 0.34), V(0, -0.16, 0.08)),
    'foot.L': (V(0, -0.16, 0.08), V(0.14, -0.16, 0.03)),
    'thigh.R': (V(0, 0.15, 0.6), V(0.03, 0.16, 0.34)), 'shin.R': (V(0.03, 0.16, 0.34), V(0, 0.16, 0.08)),
    'foot.R': (V(0, 0.16, 0.08), V(0.14, 0.16, 0.03)),
}


def build_mesh():
    m = MeshBuilder()
    def b(f, r, u, hf, hr, hu, g, mat='Body'):
        c = V(f, r, u); m.cbox((c.x, c.y, c.z), (hr, hf, hu), g, mat)
    b(0, 0, 0.66, 0.14, 0.2, 0.1, 'hips')
    b(0.04, 0.04, 0.98, 0.2, 0.3, 0.22, 'spine')                       # barrel chest, heavier on the right
    b(0.0, 0.14, 1.2, 0.2, 0.26, 0.12, 'spine')                         # the hump over the right shoulder
    b(0.1, 0.0, 1.22, 0.12, 0.14, 0.08, 'spine')
    for f, r, u in ((-0.12, 0.0, 1.28), (-0.14, 0.18, 1.32), (-0.16, -0.12, 1.2)):   # spurs of bone along its back
        c = V(f, r, u); m.diamond((c.x, c.y, c.z), 0.05, 0.1, 'spine', 'Metal')
    b(0.3, 0, 1.24, 0.1, 0.09, 0.09, 'head')                            # small, low head
    b(0.4, 0, 1.2, 0.04, 0.08, 0.04, 'head', 'Metal')                   # tusked jaw
    b(0.4, -0.05, 1.27, 0.01, 0.015, 0.012, 'head', 'Glow')
    b(0.4, 0.05, 1.27, 0.01, 0.015, 0.012, 'head', 'Glow')
    # The great right arm, and its fist.
    b(0.07, 0.47, 0.96, 0.15, 0.15, 0.25, 'upper_arm.R')
    b(0.09, 0.51, 0.48, 0.13, 0.13, 0.25, 'forearm.R')
    b(0.09, 0.51, 0.64, 0.14, 0.14, 0.05, 'forearm.R', 'Metal')         # a band biting into it
    b(0.11, 0.52, 0.14, 0.15, 0.16, 0.12, 'hand.R')
    # The small left arm.
    b(0.07, -0.31, 1.05, 0.045, 0.045, 0.13, 'upper_arm.L'); b(0.08, -0.32, 0.8, 0.04, 0.04, 0.12, 'forearm.L')
    b(0.08, -0.32, 0.63, 0.045, 0.04, 0.04, 'hand.L')
    def legs(s, k, X):
        r = -0.16 if s == 'L' else 0.16
        b(0.02, r, 0.47, 0.08, 0.07, 0.14, 'thigh.' + s); b(0.02, r, 0.21, 0.06, 0.055, 0.13, 'shin.' + s)
        b(0.06, r, 0.04, 0.12, 0.07, 0.04, 'foot.' + s)
    sym(legs)
    return m.finish()


def FIST(f=0.0, r=0.0, u=0.0):
    """The right knuckles on the ground (world), ahead and out to the side, moved by f / r / u."""
    return V(0.36 + f, 0.6 + r, 0.08 + u)

def S(drop=0.0, lean=12, yaw=-12, f=0.0, fist=(0.0, 0.0, 0.0), plant=True, arm=None):
    s = {
        'hips': {'off': V(f, 0, -0.04 - drop), 'yaw': yaw, 'lean': 8},
        'spine': {'yaw': -6, 'lean': lean, 'tilt': -8},
        'head': {'yaw': 10, 'lean': -lean - 14},
        'leg.L': (stance_foot('L', 0.06 + f, -0.04), V(1, -0.2, 0)),
        'leg.R': (stance_foot('R', -0.08 + f, 0.02), V(1, 0.3, 0)),
        'arm.L': (V(0.18, -0.34, -0.58 - drop * 0.5), V(-0.4, -1, 0)), 'hand.L': {'dir': V(0.4, 0, -1)},
        'hand.R': {'dir': V(0.3, 0, -1), 'fwd': V(1, 0, 0)},
    }
    if arm: s.update(arm)
    elif plant: s = planted(s, 'R', FIST(*fist), V(-0.6, 1, 0.2))
    else: s['arm.R'] = (V(0.2, 0.5, -0.9), V(-0.6, 1, 0.2))
    return s


def build_clips():
    keys('Idle', 0, 60, [(0, S()), (20, S(drop=0.03, lean=14)), (40, S()), (60, S())])
    # A lope: the fist planted ahead, the body swinging past it on short legs, the fist swung on again.
    lope = [S(drop=0.02, lean=18, fist=(0.3, 0, 0), f=-0.06), S(drop=-0.03, lean=22, fist=(0.1, 0, 0.0), f=0.08,
            arm=None),
            S(drop=0.02, lean=24, fist=(-0.2, 0, 0.0), f=0.16), S(drop=-0.02, lean=16, f=0.04, plant=False,
            arm={'arm.R': (V(0.36, 0.44, -0.62), V(-0.6, 1, 0.2))})]
    lope[0] = merge(lope[0], leg_L=(stance_foot('L', -0.14, -0.04), V(1, 0, 0)), leg_R=(stance_foot('R', 0.12, 0.02, 0.12), V(1, 0, 0)))
    lope[2] = merge(lope[2], leg_L=(stance_foot('L', 0.24, -0.04, 0.1), V(1, 0, 0)), leg_R=(stance_foot('R', 0.0, 0.02), V(1, 0, 0)))
    fresh('Run', 0, 16)
    for i, fr in enumerate((0, 4, 8, 12, 16)): key('Run', fr, lope[i % 4])
    dashes(lambda **k: S(**k))
    reactions(lambda **k: S(**k))
    down = {'hips': {'off': V(-0.3, 0, -0.52), 'yaw': 20, 'lean': -80}, 'spine': {'lean': -4}, 'head': {'lean': -10, 'yaw': 25},
            'leg.L': (stance_foot('L', 0.14, -0.06, 0.0), V(0, 0, 1)), 'leg.R': (stance_foot('R', 0.1, 0.08, 0.04), V(0, 0, 1)),
            'arm.L': (V(0.02, -0.4, -0.06), V(-1, 0, -0.2)), 'arm.R': (V(0.1, 0.6, -0.4), V(-1, 0, -0.2))}
    keys('Death', 0, 26, [(0, S()), (8, S(drop=0.1, lean=30)), (26, down)])
    guard = S(drop=0.08, lean=20, arm={'arm.R': (V(0.46, 0.06, -0.3), V(-0.4, 1, -0.2)), 'hand.R': {'dir': V(0.2, -0.3, 1), 'fwd': V(1, 0, 0)}})
    keys('Block', 0, 6, [(0, S()), (3, guard), (6, guard)])
    # Ravage (its follow-up too, and the shared strike): the great arm swung up overhead, then smashed down ahead.
    raised = S(drop=-0.06, lean=-6, yaw=10, arm={'arm.R': (V(-0.06, 0.34, 0.56), V(-0.4, 1, 0.3)), 'hand.R': {'dir': V(-0.2, 0.2, 1), 'fwd': V(1, 0, 0)}})
    smash = S(drop=0.14, lean=36, yaw=-20, f=0.1, fist=(0.36, -0.2, 0.0))
    for pre in ('', 'ravage.', 'ravage_followup.'):
        keys(pre + 'Windup', 0, 18, [(0, S()), (14, raised), (18, raised)])
        keys(pre + 'Strike', 0, 20, [(0, raised), (3, smash), (11, smash), (20, S())])
    # Charge: head and shoulder lowered behind the fist, then the rush, the great arm braced in front.
    brace = S(drop=0.12, lean=34, yaw=-30, fist=(0.0, -0.1, 0.0))
    rush = S(drop=0.06, lean=40, yaw=-25, f=0.1, plant=False,
             arm={'arm.R': (V(0.6, 0.1, -0.5), V(-0.4, 1, 0)), 'hand.R': {'dir': V(1, 0, -0.3), 'fwd': V(0, 0, 1)}})
    rush = merge(rush, leg_L=(stance_foot('L', 0.26, -0.04, 0.1), V(1, 0, 0)), leg_R=(stance_foot('R', -0.26, 0.02, 0.06), V(1, 0, 0)))
    keys('charge.Windup', 0, 18, [(0, S()), (12, brace), (18, brace)])
    keys('charge.Strike', 0, 24, [(0, brace), (4, rush), (18, rush), (24, S())])


PREVIEW_VIEW = {'ortho': 2.4, 'height': 0.9}
PREVIEW = [('Idle', 0), ('Run', 0), ('Run', 4), ('Run', 8), ('Block', 6), ('ravage.Windup', 18), ('ravage.Strike', 4),
           ('charge.Windup', 18), ('charge.Strike', 8), ('HitHeavy', 3), ('Stun', 0), ('Death', 26)]
