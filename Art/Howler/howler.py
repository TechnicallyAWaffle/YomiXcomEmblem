# The Howler: a lean quadruped hunter, canine, with light cybernetics (a plated spine, a braced hind leg, a metal jaw
# and glowing eyes). Run by Art/common/build_unit.py. It has its own skeleton (SKELETON): the Fighter's 15 bones laid
# out on all fours (arms = front legs, legs = hind legs, the spine level), plus a tail. No Block clip: it can't guard.
# Clips: the shared set (built here for four legs), plus ravenous_jaws (a crouch and a lunging bite) and
# howling_lament (sitting back, head thrown up).

def _leg(f, r, u0, u1, fe, f1=None):
    return (V(f, r, u0), V(f if f1 is None else f1, r, u1))

SKELETON = {
    'hips': (V(-0.32, 0, 0.6), V(-0.32, 0, 0.72)),
    'spine': (V(-0.3, 0, 0.66), V(0.28, 0, 0.72)),
    'head': (V(0.3, 0, 0.76), V(0.58, 0, 0.84)),
    'upper_arm.L': (V(0.22, -0.12, 0.62), V(0.24, -0.12, 0.35)), 'forearm.L': (V(0.24, -0.12, 0.35), V(0.22, -0.12, 0.1)),
    'hand.L': (V(0.22, -0.12, 0.1), V(0.3, -0.12, 0.03)),
    'upper_arm.R': (V(0.22, 0.12, 0.62), V(0.24, 0.12, 0.35)), 'forearm.R': (V(0.24, 0.12, 0.35), V(0.22, 0.12, 0.1)),
    'hand.R': (V(0.22, 0.12, 0.1), V(0.3, 0.12, 0.03)),
    'thigh.L': (V(-0.32, -0.1, 0.6), V(-0.2, -0.1, 0.36)), 'shin.L': (V(-0.2, -0.1, 0.36), V(-0.34, -0.1, 0.1)),
    'foot.L': (V(-0.34, -0.1, 0.1), V(-0.26, -0.1, 0.03)),
    'thigh.R': (V(-0.32, 0.1, 0.6), V(-0.2, 0.1, 0.36)), 'shin.R': (V(-0.2, 0.1, 0.36), V(-0.34, 0.1, 0.1)),
    'foot.R': (V(-0.34, 0.1, 0.1), V(-0.26, 0.1, 0.03)),
}
EXTRA_BONES = {'tail': (V(-0.4, 0, 0.72), V(-0.72, 0, 0.66), 'hips')}


def build_mesh():
    m = MeshBuilder()
    def b(f, r, u, hf, hr, hu, g, mat='Body'):
        c = V(f, r, u); m.cbox((c.x, c.y, c.z), (hr, hf, hu), g, mat)
    b(0.08, 0, 0.7, 0.24, 0.14, 0.13, 'spine')                       # chest and back
    b(0.2, 0, 0.62, 0.1, 0.12, 0.1, 'spine')                         # deep chest
    b(-0.28, 0, 0.68, 0.13, 0.12, 0.1, 'hips')                       # haunches
    b(-0.02, 0, 0.845, 0.26, 0.06, 0.018, 'spine', 'Metal')          # back plating
    for f in (-0.16, -0.04, 0.08, 0.2): b(f, 0, 0.87, 0.025, 0.035, 0.02, 'spine', 'Metal')   # ridges
    b(0.36, 0, 0.8, 0.08, 0.075, 0.07, 'head')                       # neck
    b(0.46, 0, 0.86, 0.08, 0.085, 0.075, 'head')                     # skull
    b(0.6, 0, 0.82, 0.08, 0.05, 0.045, 'head')                       # snout
    b(0.58, 0, 0.765, 0.08, 0.048, 0.018, 'head', 'Metal')           # metal jaw
    b(0.68, 0, 0.83, 0.012, 0.03, 0.025, 'head', 'Dark')             # nose
    for r in (-1, 1):
        b(0.42, 0.05 * r, 0.97, 0.025, 0.02, 0.06, 'head')            # ears
        b(0.53, 0.07 * r, 0.88, 0.015, 0.012, 0.012, 'head', 'Glow')  # eyes
    def side(s, k, X):
        r = -0.12 if s == 'L' else 0.12
        b(0.23, r, 0.48, 0.055, 0.045, 0.15, 'upper_arm.' + s)
        b(0.23, r, 0.22, 0.04, 0.035, 0.13, 'forearm.' + s)
        b(0.27, r, 0.04, 0.065, 0.045, 0.035, 'hand.' + s)
        rh = -0.1 if s == 'L' else 0.1
        b(-0.26, rh, 0.47, 0.09, 0.05, 0.15, 'thigh.' + s)
        b(-0.28, rh, 0.22, 0.04, 0.035, 0.13, 'shin.' + s)
        b(-0.27, rh, 0.04, 0.065, 0.045, 0.035, 'foot.' + s)
    sym(side)
    b(-0.28, 0.1 + 0.04, 0.24, 0.05, 0.012, 0.1, 'shin.R', 'Metal')   # the braced hind leg
    b(-0.28, 0.1 - 0.04, 0.24, 0.05, 0.012, 0.1, 'shin.R', 'Metal')
    b(-0.56, 0, 0.69, 0.16, 0.025, 0.025, 'tail')
    b(-0.7, 0, 0.67, 0.03, 0.02, 0.02, 'tail', 'Metal')
    return m.finish()


def FP(side, f=0.0, r=0.0, u=0.0):
    """A front paw's wrist on the ground (world): where it stands at rest, moved by f / r / u."""
    return V(0.22 + f, (-0.12 if side == 'L' else 0.12) + r, 0.1 + u)

REL = {'L': V(-0.06, -0.12, -0.62), 'R': V(-0.06, 0.12, -0.62)}   # a front wrist at rest, from the chest's top


def Q(up=0.0, fwd=0.0, pitch=0.0, yaw=0.0, tilt=0.0, spine=0.0, sy=0.0, head=0.0, hy=0.0, tail=-10.0, ty=0.0,
      fl=(0, 0, 0), fr=(0, 0, 0), hl=(0, 0, 0), hr=(0, 0, 0), plant=True, arms=None):
    """A pose on all fours. fl / fr: front paws (f, r, u) from rest, on the ground (plant) or, with plant False, from
    where the wrist hangs (relative to the chest). hl / hr: hind paws from rest. tail: + lifts it."""
    s = {'hips': {'off': V(fwd, 0, up), 'yaw': yaw, 'lean': pitch, 'tilt': tilt},
         'spine': {'lean': spine, 'yaw': sy}, 'head': {'lean': head, 'yaw': hy},
         'tail': {'lean': -tail, 'yaw': ty},
         'leg.L': (stance_foot('L', *hl), V(1, 0, 0)), 'leg.R': (stance_foot('R', *hr), V(1, 0, 0)),
         'foot.L': {'dir': V(1, 0, -0.6)}, 'foot.R': {'dir': V(1, 0, -0.6)},
         'hand.L': {'dir': V(1, 0, -0.7)}, 'hand.R': {'dir': V(1, 0, -0.7)}}
    if arms: s.update(arms)
    elif plant:
        s = planted(s, 'L', FP('L', *fl), V(-1, 0, 0.2))
        s = planted(s, 'R', FP('R', *fr), V(-1, 0, 0.2))
    else:
        s['arm.L'] = (REL['L'] + V(*fl), V(-1, 0, 0.2)); s['arm.R'] = (REL['R'] + V(*fr), V(-1, 0, 0.2))
    return s


def build_clips():
    keys('Idle', 0, 60, [(0, Q()), (15, Q(up=-0.012, head=4, tail=-6, ty=8)), (30, Q(head=-3, hy=8, tail=-10)),
                         (45, Q(up=-0.012, head=2, hy=-6, tail=-6, ty=-8)), (60, Q())])
    # A gallop-trot: diagonal pairs, the body pitching a little each stride.
    def stride(ph):
        a, b2 = 0.2, -0.16
        lift = 0.14
        if ph == 0: return Q(up=-0.02, pitch=-3, head=6, fl=(a, 0, 0), hr=(a, 0, 0), fr=(b2, 0, 0.02), hl=(b2, 0, 0.02), tail=-2)
        if ph == 1: return Q(up=0.03, pitch=2, head=2, fl=(0.02, 0, 0), hr=(0.02, 0, 0), fr=(0.04, 0, lift), hl=(0.0, 0, lift), tail=4)
        if ph == 2: return Q(up=-0.02, pitch=-3, head=6, fr=(a, 0, 0), hl=(a, 0, 0), fl=(b2, 0, 0.02), hr=(b2, 0, 0.02), tail=-2)
        return Q(up=0.03, pitch=2, head=2, fr=(0.02, 0, 0), hl=(0.02, 0, 0), fl=(0.04, 0, lift), hr=(0.0, 0, lift), tail=4)
    fresh('Run', 0, 12)
    for i, fr in enumerate((0, 3, 6, 9, 12)): key('Run', fr, stride(i % 4))

    # Dashes: a bound forward, a spring back, a hop to the right.
    bound = lambda air: Q(up=0.06 * air, pitch=-8 + 6 * air, head=8, fl=(0.34, 0, 0.18 * air), fr=(0.3, 0, 0.14 * air),
                          hl=(-0.3, 0, 0.12 * air), hr=(-0.26, 0, 0.1 * air), tail=10)
    keys('DashForward', 0, 6, [(0, Q(up=-0.06, pitch=4)), (2, bound(0.0)), (6, bound(1.0))])
    spring = lambda air: Q(up=0.04 * air - 0.02, pitch=-6, head=-12, fl=(-0.1, 0, 0.12 * air), fr=(-0.12, 0, 0.1 * air),
                           hl=(0.12, 0, 0.0), hr=(0.12, 0, 0.0), tail=-20)
    keys('DashBack', 0, 6, [(0, Q(up=-0.05)), (2, spring(0.0)), (6, spring(1.0))])
    hop = lambda air: Q(up=0.05 * air - 0.03, tilt=10, yaw=10, hy=-15, fl=(0, 0.12, 0.14 * air), fr=(0, 0.18, 0.1 * air),
                        hl=(0, 0.1, 0.1 * air), hr=(0, 0.16, 0.08 * air), tail=6, ty=-20)
    keys('DashSide', 0, 6, [(0, Q(up=-0.05)), (2, hop(0.0)), (6, hop(1.0))])

    # Reactions.
    keys('HitLight', 0, 8, [(0, Q()), (2, Q(up=-0.03, fwd=-0.05, yaw=14, head=-14, hy=-18, tail=-20)), (8, Q())])
    keys('HitHeavy', 0, 14, [(0, Q()), (3, Q(up=-0.06, fwd=-0.14, pitch=-8, yaw=22, head=-22, hy=-25, tail=-30, hl=(0.08, 0, 0))),
                             (8, Q(up=-0.1, fwd=-0.08, pitch=4, head=4, tail=-20)), (14, Q())])
    keys('Interrupted', 0, 10, [(0, Q()), (3, Q(up=-0.04, tilt=12, yaw=16, head=-10, hy=20, tail=-24)),
                                (6, Q(up=-0.03, tilt=-8, yaw=-6, head=-4, hy=-14)), (10, Q())])
    stun = lambda t: Q(up=-0.16, pitch=6, tilt=t, head=22, hy=10 * (1 if t > 0 else -1), tail=-30,
                       fl=(0.06, 0, 0), fr=(0.06, 0, 0), hl=(0.05, 0, 0), hr=(0.05, 0, 0))
    keys('Stun', 0, 30, [(0, stun(6)), (15, stun(-6)), (30, stun(6))])
    slump = Q(up=-0.2, pitch=8, tilt=10, head=20, tail=-30, fl=(0.1, -0.04, 0), fr=(0.1, 0.06, 0))
    down = Q(up=-0.44, fwd=0.02, tilt=78, head=-10, hy=10, tail=-5, ty=15, plant=False,
             fl=(0.12, 0.0, 0.2), fr=(0.16, 0.06, 0.24), hl=(0.12, -0.1, 0.24), hr=(0.16, -0.04, 0.3))
    keys('Death', 0, 24, [(0, Q()), (8, slump), (24, down)])

    # Bite: a crouch on the haunches, head low, then a lunge with the jaws forward.
    coil = Q(up=-0.1, fwd=-0.08, pitch=4, head=10, fl=(0.06, 0, 0), fr=(0.02, 0, 0), hl=(0.06, 0, 0), hr=(0.04, 0, 0), tail=-4)
    lunge = Q(up=0.02, fwd=0.26, pitch=-6, spine=-4, head=-14, fl=(0.42, 0, 0.04), fr=(0.34, 0, 0.08),
              hl=(0.08, 0, 0), hr=(0.04, 0, 0), tail=14)
    snap = merge(lunge, head={'lean': 6, 'yaw': 8})
    for pre in ('', 'ravenous_jaws.'):
        keys(pre + 'Windup', 0, 12, [(0, Q()), (8, coil), (12, coil)])
        keys(pre + 'Strike', 0, 16, [(0, coil), (2, lunge), (5, snap), (9, lunge), (16, Q())])

    # Howling Lament: sits back on the haunches, chest up, head thrown back, and howls.
    sit = Q(up=-0.14, fwd=-0.04, pitch=-14, spine=-4, head=-26, hl=(0.12, 0, 0), hr=(0.12, 0, 0), tail=-25,
            fl=(0.0, 0, 0), fr=(0.0, 0, 0))
    howl = Q(up=-0.13, fwd=-0.04, pitch=-17, spine=-8, head=-60, hl=(0.12, 0, 0), hr=(0.12, 0, 0), tail=-20)
    keys('howling_lament.Windup', 0, 18, [(0, Q()), (12, sit), (18, sit)])
    keys('howling_lament.Strike', 0, 30, [(0, sit), (4, howl), (22, merge(howl, head={'lean': -58, 'yaw': 6})), (30, Q())])


PREVIEW_VIEW = {'ortho': 1.7, 'height': 0.6}
PREVIEW = [('Idle', 0), ('Run', 0), ('Run', 3), ('Run', 6), ('DashForward', 6), ('DashSide', 6),
           ('ravenous_jaws.Windup', 12), ('ravenous_jaws.Strike', 2), ('howling_lament.Strike', 10), ('HitHeavy', 3),
           ('Stun', 0), ('Death', 24)]
