# The Matriarch: a large floating jellyfish whose bell is a mishmash of geometric shapes (slabs, wedges, octahedra
# around a glowing core). Run by Art/common/build_unit.py. Its own skeleton (SKELETON): the hips, spine and head are
# the bell, held 1.2m up; the arms and legs hang from its rim as four tentacles, with two more of its own (EXTRA_BONES).
# Clips: the shared set (built here for a floating body: it drifts rather than walks), plus needlestorm (tentacles
# curled up, then flung wide), carapace (tentacles wrapped close about it) and captivate (front tentacles reaching out,
# then hauling in).

SKELETON = {
    'hips': (V(0, 0, 1.25), V(0, 0, 1.45)),
    'spine': (V(0, 0, 1.45), V(0, 0, 1.95)),
    'head': (V(0, 0, 1.95), V(0, 0, 2.35)),
    'upper_arm.L': (V(0.14, -0.28, 1.32), V(0.15, -0.31, 0.92)), 'forearm.L': (V(0.15, -0.31, 0.92), V(0.14, -0.33, 0.52)),
    'hand.L': (V(0.14, -0.33, 0.52), V(0.13, -0.33, 0.24)),
    'upper_arm.R': (V(0.14, 0.28, 1.32), V(0.15, 0.31, 0.92)), 'forearm.R': (V(0.15, 0.31, 0.92), V(0.14, 0.33, 0.52)),
    'hand.R': (V(0.14, 0.33, 0.52), V(0.13, 0.33, 0.24)),
    'thigh.L': (V(-0.16, -0.26, 1.32), V(-0.17, -0.29, 0.92)), 'shin.L': (V(-0.17, -0.29, 0.92), V(-0.16, -0.31, 0.52)),
    'foot.L': (V(-0.16, -0.31, 0.52), V(-0.15, -0.31, 0.24)),
    'thigh.R': (V(-0.16, 0.26, 1.32), V(-0.17, 0.29, 0.92)), 'shin.R': (V(-0.17, 0.29, 0.92), V(-0.16, 0.31, 0.52)),
    'foot.R': (V(-0.16, 0.31, 0.52), V(-0.15, 0.31, 0.24)),
}
EXTRA_BONES = {'tent.F': (V(0.3, 0, 1.3), V(0.32, 0, 0.5), 'hips'), 'tent.B': (V(-0.3, 0, 1.3), V(-0.32, 0, 0.5), 'hips')}


def build_mesh():
    m = MeshBuilder()
    def b(f, r, u, hf, hr, hu, g, mat='Body'):
        c = V(f, r, u); m.cbox((c.x, c.y, c.z), (hr, hf, hu), g, mat)
    def gem(f, r, u, rad, h, g, mat='Metal'):
        c = V(f, r, u); m.diamond((c.x, c.y, c.z), rad, h, g, mat)
    # The bell: a broad skirt, a body of stacked slabs offset every which way, wedges and gems round its rim.
    b(0, 0, 1.4, 0.46, 0.46, 0.045, 'hips')
    for f, r in ((0.4, 0), (-0.4, 0), (0, 0.4), (0, -0.4)):
        b(f, r, 1.3, 0.12 if r else 0.05, 0.05 if r else 0.12, 0.1, 'hips')                # hanging flaps
    b(0, 0, 1.62, 0.38, 0.36, 0.17, 'spine')
    b(0.05, -0.06, 1.8, 0.3, 0.32, 0.1, 'spine')
    b(-0.08, 0.1, 1.88, 0.22, 0.2, 0.1, 'spine', 'Metal')
    b(0.18, 0.16, 1.72, 0.12, 0.14, 0.14, 'spine', 'Metal')
    b(-0.2, -0.18, 1.6, 0.14, 0.1, 0.16, 'spine', 'Metal')
    gem(0.38, -0.2, 1.62, 0.12, 0.2, 'spine')
    gem(-0.36, 0.24, 1.66, 0.1, 0.24, 'spine')
    gem(0.1, 0.42, 1.55, 0.09, 0.16, 'spine', 'Body')
    gem(-0.12, -0.42, 1.58, 0.1, 0.18, 'spine', 'Body')
    b(0.0, 0.0, 1.55, 0.2, 0.2, 0.1, 'spine', 'Glow')                                        # the core, under the slabs
    gem(0, 0, 1.48, 0.16, 0.12, 'hips', 'Glow')
    # Its crown: a smaller slab, a tilted wedge, a tall spike.
    b(0.02, 0.02, 2.02, 0.16, 0.18, 0.07, 'head')
    m.taper(V(0, 0, 0).y + 0.12, V(0, 0, 0).y - 0.2, (0.06, 2.12), 0.08, 0.04, 0.03, 0.02, 'head', 'Metal', up=0.12)
    gem(-0.04, 0, 2.28, 0.07, 0.2, 'head', 'Metal')
    # Tentacles: a ribbon down each segment, glowing at the tips.
    def tent(s, k, X):
        for bone, (top, bot), w, mat in (('upper_arm.', (1.32, 0.92), 0.045, 'Body'), ('forearm.', (0.92, 0.52), 0.035, 'Body'),
                                          ('hand.', (0.52, 0.22), 0.025, 'Glow')):
            r = -0.31 if s == 'L' else 0.31
            b(0.145, r, (top + bot) / 2, w, w * 1.6, (top - bot) / 2 + 0.02, bone + s, mat)
        for bone, (top, bot), w, mat in (('thigh.', (1.32, 0.92), 0.045, 'Body'), ('shin.', (0.92, 0.52), 0.035, 'Body'),
                                          ('foot.', (0.52, 0.22), 0.025, 'Glow')):
            r = -0.29 if s == 'L' else 0.29
            b(-0.165, r, (top + bot) / 2, w, w * 1.6, (top - bot) / 2 + 0.02, bone + s, mat)
    sym(tent)
    b(0.31, 0, 0.9, 0.06, 0.03, 0.4, 'tent.F')
    b(-0.31, 0, 0.9, 0.06, 0.03, 0.4, 'tent.B')
    b(0.31, 0, 0.46, 0.035, 0.02, 0.06, 'tent.F', 'Glow')
    b(-0.31, 0, 0.46, 0.035, 0.02, 0.06, 'tent.B', 'Glow')
    return m.finish()


ARM = {'L': V(0.14, -0.33, -1.43), 'R': V(0.14, 0.33, -1.43)}   # a front tentacle's tip joint at rest, from the bell's top

def M(up=0.0, fwd=0.0, pitch=0.0, yaw=0.0, tilt=0.0, spine=0.0, head=0.0, hy=0.0,
      al=(0, 0, 0), ar=(0, 0, 0), ll=(0, 0, 0), lr=(0, 0, 0), tf=0.0, tb=0.0, hands=None):
    """A floating pose. al / ar: front tentacle tips (f, r, u) moved from rest, with the bell; ll / lr: the back ones
    (they follow the bell's height too). tf / tb: the front / back tentacles swung forward (+, degrees)."""
    s = {'hips': {'off': V(fwd, 0, up), 'yaw': yaw, 'lean': pitch, 'tilt': tilt},
         'spine': {'lean': spine}, 'head': {'lean': head, 'yaw': hy},
         'arm.L': (ARM['L'] + V(*al), V(0.3, -1, 0)), 'arm.R': (ARM['R'] + V(*ar), V(0.3, 1, 0)),
         'leg.L': (stance_foot('L', ll[0] + fwd, ll[1], ll[2] + up), V(-0.3, -1, 0)),
         'leg.R': (stance_foot('R', lr[0] + fwd, lr[1], lr[2] + up), V(-0.3, 1, 0)),
         'foot.L': {'dir': V(0, 0, -1)}, 'foot.R': {'dir': V(0, 0, -1)},
         'hand.L': {'dir': V(0, 0, -1)}, 'hand.R': {'dir': V(0, 0, -1)},
         'tent.F': {'lean': -tf}, 'tent.B': {'lean': tb}}
    if hands: s.update(hands)
    return s


def drift(t):
    """Idle bobbing: t in 0..1 round the loop."""
    import math as _m
    c, sn = _m.cos(t * 2 * _m.pi), _m.sin(t * 2 * _m.pi)
    return M(up=0.06 * sn, spine=2 * c, head=-2 * c, al=(0.04 * c, -0.03 * sn, 0.05 * sn), ar=(-0.04 * c, 0.03 * sn, 0.05 * sn),
             ll=(-0.04 * c, -0.02, 0.04 * sn), lr=(0.04 * c, 0.02, 0.04 * sn), tf=6 * c, tb=6 * c)


def build_clips():
    fresh('Idle', 0, 60)
    for fr in range(0, 61, 10): key('Idle', fr, drift(fr / 60.0))
    # Gliding along: tilted into the way it goes, tentacles streaming back.
    glide = lambda k: M(up=0.04 * k, pitch=12, spine=4, head=-6, al=(-0.2, -0.04, 0.08 + 0.04 * k), ar=(-0.2, 0.04, 0.08 - 0.04 * k),
                        ll=(-0.3, -0.03, 0.12 - 0.04 * k), lr=(-0.3, 0.03, 0.12 + 0.04 * k), tf=-20 - 6 * k, tb=30 + 6 * k)
    fresh('Run', 0, 20)
    for fr, k in ((0, 1), (5, 0), (10, -1), (15, 0), (20, 1)): key('Run', fr, glide(k))
    jerk = lambda d, t=0.0: M(up=0.05, fwd=0.1 * d, pitch=14 * d, tilt=t, al=(-0.2 * d, 0, 0.1), ar=(-0.2 * d, 0, 0.1),
                              ll=(-0.25 * d, 0, 0.12), lr=(-0.25 * d, 0, 0.12), tf=-25 * d, tb=30 * d)
    keys('DashForward', 0, 6, [(0, M()), (2, jerk(1)), (6, jerk(1))])
    keys('DashBack', 0, 6, [(0, M()), (2, jerk(-1)), (6, jerk(-1))])
    side = M(up=0.05, tilt=16, al=(0, -0.2, 0.1), ar=(0, -0.2, 0.06), ll=(0, -0.2, 0.08), lr=(0, -0.2, 0.06), tf=8, tb=8)
    keys('DashSide', 0, 6, [(0, M()), (2, side), (6, side)])
    keys('Block', 0, 6, [(0, M()), (3, M(up=-0.04, pitch=6, al=(0.22, 0.14, 0.4), ar=(0.22, -0.14, 0.4), tf=40)),
                         (6, M(up=-0.04, pitch=6, al=(0.24, 0.16, 0.42), ar=(0.24, -0.16, 0.42), tf=45))])
    keys('HitLight', 0, 8, [(0, M()), (2, M(up=0.02, fwd=-0.05, pitch=-8, tilt=6, al=(0.08, 0, 0.06), ar=(0.08, 0, 0.06), tf=12)), (8, M())])
    keys('HitHeavy', 0, 14, [(0, M()), (3, M(up=0.08, fwd=-0.16, pitch=-18, yaw=15, al=(0.16, -0.06, 0.16), ar=(0.16, 0.06, 0.16),
                                             ll=(0.12, 0, 0.1), lr=(0.12, 0, 0.1), tf=24, tb=-14)),
                             (8, M(up=-0.08, fwd=-0.06, pitch=4)), (14, M())])
    keys('Interrupted', 0, 10, [(0, M()), (3, M(tilt=14, yaw=20, al=(0, -0.1, 0.1), ar=(0, 0.1, 0.1))), (6, M(tilt=-10, yaw=-8)), (10, M())])
    sag = lambda t: M(up=-0.22, pitch=10, tilt=t, spine=6, head=6, al=(0.04, 0, -0.02), ar=(0.04, 0, -0.02), ll=(0.03, 0, -0.02),
                      lr=(0.03, 0, -0.02), tf=-4, tb=-4)
    keys('Stun', 0, 30, [(0, sag(8)), (15, sag(-8)), (30, sag(8))])
    # Death: the core gutters, it sinks to the ground and its bell tips over, tentacles splayed on the floor.
    keys('Death', 0, 30, [(0, M()), (8, M(up=-0.2, pitch=10, tilt=10, tf=-10, tb=-10)),
                          (30, M(up=-1.1, pitch=8, tilt=58, spine=10, head=12, al=(0.3, -0.3, 0.6), ar=(0.4, 0.3, 0.5),
                                 ll=(-0.3, -0.3, 0.2), lr=(-0.2, 0.4, 0.1), tf=60, tb=50))])

    # Shared windup / strike, and Needlestorm: tentacles curled up under the bell, then flung out wide (the barbs go).
    curl = M(up=0.1, pitch=-4, spine=-4, al=(0.06, 0.12, 0.62), ar=(0.06, -0.12, 0.62), ll=(-0.02, 0.1, 0.5), lr=(-0.02, -0.1, 0.5),
             tf=-30, tb=-30)
    fling = M(up=0.16, pitch=-6, spine=-8, head=-6, al=(0.1, -0.5, 0.5), ar=(0.1, 0.5, 0.5), ll=(-0.2, -0.45, 0.4), lr=(-0.2, 0.45, 0.4),
              tf=70, tb=70)
    for pre in ('', 'needlestorm.'):
        keys(pre + 'Windup', 0, 18, [(0, M()), (12, curl), (18, merge(curl, spine={'lean': -6}))])
        keys(pre + 'Strike', 0, 18, [(0, curl), (3, fling), (9, fling), (18, M())])
    # Carapace: everything drawn in close about the bell, which hunches down over it.
    wrap = M(up=-0.04, pitch=4, spine=10, head=10, al=(0.0, 0.22, 0.5), ar=(0.0, -0.22, 0.5), ll=(0.12, 0.2, 0.42), lr=(0.12, -0.2, 0.42),
             tf=-35, tb=-35)
    keys('carapace.Windup', 0, 4, [(0, M()), (4, wrap)])
    keys('carapace.Strike', 0, 14, [(0, wrap), (4, merge(wrap, hips={'off': V(0, 0, 0.04), 'yaw': 0, 'lean': 2, 'tilt': 0})), (14, M())])
    # Captivate: the front tentacles rise and reach far out at the target, then haul back.
    reach = M(up=0.12, pitch=-10, spine=-6, head=-8, al=(0.72, 0.04, 0.9), ar=(0.72, -0.04, 0.9), ll=(-0.12, 0, 0.1), lr=(-0.12, 0, 0.1),
              tf=60, tb=-10)
    haul = M(up=0.04, pitch=12, spine=8, head=6, al=(0.1, 0.06, 0.42), ar=(0.1, -0.06, 0.42), tf=20, tb=10)
    keys('captivate.Windup', 0, 30, [(0, M()), (18, reach), (30, merge(reach, spine={'lean': -10}))])
    keys('captivate.Strike', 0, 30, [(0, reach), (8, haul), (22, haul), (30, M())])


PREVIEW_VIEW = {'ortho': 3.0, 'height': 1.3}
PREVIEW = [('Idle', 0), ('Idle', 30), ('Run', 0), ('DashSide', 6), ('Block', 6), ('needlestorm.Windup', 18),
           ('needlestorm.Strike', 4), ('carapace.Windup', 4), ('captivate.Windup', 30), ('captivate.Strike', 10),
           ('HitHeavy', 3), ('Death', 30)]
