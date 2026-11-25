# The Monarch: a twisted, disfigured, vaguely humanoid thing with four wings (their own bones). Lopsided: a swollen
# left shoulder and long arm, a hunch, a head grown off to one side, a glowing rent down its chest. Run by
# Art/common/build_unit.py. Clips: the shared set, plus divine_javelin (Divine Javelin: an arm reared back, then a
# throw), incineration (Incineration: rising, wings wide, both hands thrust out for the beam) and horde_of_memories
# (Horde of Memories: arms and wings flung up and out).

EXTRA_BONES = {
    'wingU.L': (V(-0.12, -0.12, 1.42), V(-0.3, -0.7, 1.9), 'spine'), 'wingU.R': (V(-0.12, 0.12, 1.42), V(-0.3, 0.7, 1.9), 'spine'),
    'wingD.L': (V(-0.12, -0.1, 1.2), V(-0.28, -0.6, 0.95), 'spine'), 'wingD.R': (V(-0.12, 0.1, 1.2), V(-0.28, 0.6, 0.95), 'spine'),
}


def build_mesh():
    m = MeshBuilder()
    humanoid(m, w=1.0, skip=('head', 'torso', 'upper_arm', 'forearm'))
    def b(f, r, u, hf, hr, hu, g, mat='Body'):
        c = V(f, r, u); m.cbox((c.x, c.y, c.z), (hr, hf, hu), g, mat)
    b(0, 0, 1.24, 0.11, 0.2, 0.2, 'spine')
    b(-0.08, -0.08, 1.42, 0.12, 0.17, 0.1, 'spine')                       # the hunch, off to the left
    b(-0.05, -0.24, 1.42, 0.12, 0.12, 0.12, 'spine')                      # swollen left shoulder
    b(-0.12, 0.08, 1.3, 0.06, 0.08, 0.16, 'spine', 'Dark')                # a growth on its back
    b(0.11, 0, 1.24, 0.01, 0.03, 0.16, 'spine', 'Glow')                   # the rent down its chest
    b(0.06, 0.1, 1.58, 0.1, 0.09, 0.1, 'head')                            # head, grown off to the right
    b(0.12, 0.06, 1.63, 0.04, 0.02, 0.03, 'head', 'Glow')                 # one eye, too large
    b(0.12, 0.14, 1.58, 0.02, 0.015, 0.015, 'head', 'Glow')
    for i, (f, r) in enumerate(((0.0, 0.04), (-0.04, 0.12), (0.02, 0.18))):   # a crown of horns, uneven
        m.diamond((V(f, r, 1.7).x, V(f, r, 1.7).y, 1.72 + 0.03 * i), 0.025, 0.07 + 0.03 * i, 'head', 'Gold')
    # Arms: the left long and heavy, the right withered.
    b(0, -0.28, 1.24, 0.08, 0.08, 0.16, 'upper_arm.L'); b(0, -0.28, 0.96, 0.07, 0.07, 0.15, 'forearm.L')
    b(0, -0.28, 0.79, 0.07, 0.06, 0.07, 'hand.L', 'Dark')
    b(0, 0.27, 1.28, 0.04, 0.04, 0.14, 'upper_arm.R'); b(0, 0.27, 1.01, 0.035, 0.035, 0.13, 'forearm.R')
    # Four wings: tattered slabs fanning down each bone.
    # Each wing: overlapping panels stepping out from the back (upper pair rising, lower pair hanging), ragged:
    # alternate panels Dark, and of uneven lengths.
    def wing(name, sgn, panels):
        for i, (r0, r1, u0, u1) in enumerate(panels):
            f = -0.16 - 0.03 * i
            x0, x1 = sorted((-r0 * sgn, -r1 * sgn))
            m.box((x0, x1), (-f - 0.012, -f + 0.012), (u0, u1), name, 'Dark' if i % 2 else 'Body')
    up = ((0.1, 0.34, 1.3, 1.62), (0.3, 0.52, 1.42, 1.86), (0.48, 0.72, 1.55, 1.98), (0.66, 0.8, 1.7, 1.92))
    lo = ((0.1, 0.3, 0.98, 1.24), (0.26, 0.48, 0.86, 1.16), (0.44, 0.62, 0.8, 1.06))
    wing('wingU.L', -1, up); wing('wingU.R', 1, up); wing('wingD.L', -1, lo); wing('wingD.R', 1, lo)
    return m.finish()


def wings(up=0.0, down=0.0, flap=0.0):
    """up / down: the upper / lower pair spread out (+) or folded (-), degrees; flap: both raised (+)."""
    return {'wingU.L': {'yaw': up, 'tilt': -flap}, 'wingU.R': {'yaw': -up, 'tilt': flap},
            'wingD.L': {'yaw': down, 'tilt': -flap * 0.5}, 'wingD.R': {'yaw': -down, 'tilt': flap * 0.5}}

def S(drop=0.0, lean=26, yaw=10, f=0.0, w=(0, 0, 0)):
    return plus({
        'hips': {'off': V(f, 0, -0.1 - drop), 'yaw': yaw, 'lean': 10, 'tilt': -6},
        'spine': {'yaw': 8, 'lean': lean, 'tilt': 12},
        'head': {'yaw': -20, 'lean': -lean - 8, 'tilt': -18},
        'leg.L': (stance_foot('L', 0.12 + f, -0.08), V(1, -0.3, 0)),
        'leg.R': (stance_foot('R', -0.1 + f, 0.08), V(1, 0.5, 0)),
        'arm.L': (V(0.2, -0.32, -0.6 - drop * 0.5), V(-0.4, -1, 0)), 'hand.L': {'dir': V(0.4, -0.2, -1)},
        'arm.R': (V(0.14, 0.2, -0.34), V(-0.3, 1, -0.3)), 'hand.R': {'dir': V(0.6, 0, -0.6)},
    }, wings(*w))


def build_clips():
    keys('Idle', 0, 60, [(0, S(w=(0, 0, 0))), (20, S(drop=0.03, w=(8, 4, 8))), (40, S(w=(-4, -2, -4))), (60, S(w=(0, 0, 0)))])
    run_cycle(lambda **k: S(**k), lambda ph: plus({'arm.L': (V(0.1 + (0.16 if ph in (0, 1) else -0.1), -0.34, -0.58), V(-0.4, -1, 0)),
                                                   'arm.R': (V(0.14, 0.2, -0.34), V(-0.3, 1, -0.3))}, wings(-10, -6, 10 if ph % 2 else -6)),
              stride=0.9, lean=22)
    dashes(lambda **k: S(**k), forward_arms=plus(upper(S()), wings(-20, -10, 20)))
    reactions(lambda **k: S(**k))
    shield = merge(S(drop=0.06, w=(25, 20, -10)), arm_L=(V(0.32, 0.0, -0.1), V(-0.2, -1, -0.3)), hand_L={'dir': V(0, 0.4, 1), 'fwd': V(1, 0, 0)})
    keys('Block', 0, 6, [(0, S()), (3, shield), (6, shield)])
    # Divine Javelin (and the shared throw): the long arm reared back high, then a throw.
    rear = merge(S(drop=0.02, yaw=40, lean=10, w=(10, 5, 15)), arm_L=(V(-0.2, -0.3, 0.3), V(-0.6, -1, 0)), hand_L={'dir': V(-0.3, 0, 1), 'fwd': V(1, 0, 0)})
    throw = merge(S(drop=0.1, yaw=-25, lean=34, f=0.08, w=(-15, -8, -10)), arm_L=(V(0.62, 0.1, -0.1), V(-0.2, -1, -0.3)), hand_L={'dir': V(1, 0.1, -0.2), 'fwd': V(0, 0, -1)})
    for pre in ('', 'divine_javelin.'):
        keys(pre + 'Windup', 0, 12, [(0, S()), (10, rear), (12, rear)])
        keys(pre + 'Strike', 0, 14, [(0, rear), (3, throw), (7, throw), (14, S())])
    # Incineration: it straightens (as near as it can), wings out wide, both hands thrust at the target.
    rising = merge(S(drop=-0.04, lean=6, w=(40, 30, 25)), head={'yaw': -10, 'lean': -4, 'tilt': -10},
                   arm_L=(V(0.2, -0.36, -0.1), V(-0.2, -1, -0.3)), arm_R=(V(0.2, 0.3, -0.06), V(-0.2, 1, -0.3)))
    beam = merge(S(drop=0.0, lean=14, w=(50, 40, 10)), head={'yaw': -10, 'lean': -12, 'tilt': -10},
                 arm_L=(V(0.62, 0.0, -0.1), V(-0.2, -1, -0.3)), hand_L={'dir': V(0, 0, 1), 'fwd': V(1, 0, 0)},
                 arm_R=(V(0.5, 0.04, -0.06), V(-0.2, 1, -0.3)), hand_R={'dir': V(0, 0, 1), 'fwd': V(1, 0, 0)})
    keys('incineration.Windup', 0, 22, [(0, S()), (16, rising), (22, rising)])
    keys('incineration.Strike', 0, 30, [(0, rising), (4, beam), (24, merge(beam, spine={'yaw': 12, 'lean': 16, 'tilt': 10})), (30, S())])
    # Horde of Memories: arms and all four wings flung up and wide.
    gather = merge(S(drop=0.12, lean=36, w=(-30, -20, -20)), arm_L=(V(0.2, 0.06, -0.3), V(-0.2, -1, -0.3)), arm_R=(V(0.2, -0.04, -0.26), V(-0.2, 1, -0.3)))
    flung = merge(S(drop=-0.04, lean=-4, w=(55, 45, 45)), head={'yaw': 0, 'lean': -30, 'tilt': -10},
                  arm_L=(V(0.0, -0.5, 0.32), V(0, -1, 0.2)), hand_L={'dir': V(0, -0.5, 1)}, arm_R=(V(0.0, 0.4, 0.3), V(0, 1, 0.2)), hand_R={'dir': V(0, 0.5, 1)})
    keys('horde_of_memories.Windup', 0, 30, [(0, S()), (14, gather), (30, flung)])
    keys('horde_of_memories.Strike', 0, 20, [(0, flung), (8, merge(flung, spine={'yaw': 8, 'lean': -8, 'tilt': 12})), (20, S())])


PREVIEW = [('Idle', 0), ('Run', 0), ('Block', 6), ('divine_javelin.Windup', 12), ('divine_javelin.Strike', 3),
           ('incineration.Windup', 22), ('incineration.Strike', 10), ('horde_of_memories.Windup', 30),
           ('DashForward', 6), ('HitHeavy', 3), ('Stun', 0), ('Death', 24)]
