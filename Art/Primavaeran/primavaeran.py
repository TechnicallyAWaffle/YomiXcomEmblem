# The Primavaeran: a humanoid etched all over, every surface of its body covered in the pattern, wearing only a
# loincloth and a thin metal circlet. Run by Art/common/build_unit.py. Clips: the shared set (shared Windup / Strike:
# a cast), plus tear_apart_reality (Tear Apart Reality: hands pressed together, slowly pulled apart, then flung wide)
# and recurrent_repel (Recurrent Repel: drawn in, then a burst outward).

ETCHINGS = etch_tiles(4)


def build_mesh():
    m = MeshBuilder()
    humanoid(m, w=0.9, mats={p: 'Etched' for p in ('thigh', 'shin', 'foot', 'upper_arm', 'forearm', 'hand', 'hips', 'torso', 'head')})
    m.box((-0.17, 0.17), (-0.1, 0.1), (0.9, 0.98), 'hips', 'Dark')                   # loincloth
    m.box((-0.07, 0.07), (-0.11, -0.09), (0.62, 0.92), 'hips', 'Dark')
    m.box((-0.11, 0.11), (-0.125, 0.11), (1.62, 1.66), 'head', 'Metal')               # circlet
    m.diamond((0.0, -0.13, 1.64), 0.025, 0.04, 'head', 'Glow')
    for z in (1.18, 1.3):                                                             # glowing seams in the pattern
        m.box((-0.16, 0.16), (-0.105, -0.095), (z, z + 0.015), 'spine', 'Glow')
    return m.finish()


def S(drop=0.0, lean=2, yaw=0, f=0.0):
    return {
        'hips': {'off': V(f, 0, -0.03 - drop), 'yaw': yaw, 'lean': 1},
        'spine': {'yaw': 0, 'lean': lean},
        'head': {'yaw': -yaw, 'lean': -lean - 4},
        'leg.L': (stance_foot('L', 0.04 + f, -0.04), V(1, -0.2, 0)),
        'leg.R': (stance_foot('R', -0.04 + f, 0.04), V(1, 0.2, 0)),
        'arm.L': (V(0.06, -0.3, -0.48 - drop * 0.5), V(-0.4, -1, 0)), 'hand.L': {'dir': V(0.1, -0.3, -1), 'fwd': V(1, 0, 0)},
        'arm.R': (V(0.06, 0.3, -0.48 - drop * 0.5), V(-0.4, 1, 0)), 'hand.R': {'dir': V(0.1, 0.3, -1), 'fwd': V(1, 0, 0)},
    }


def build_clips():
    keys('Idle', 0, 80, [(0, S()), (20, merge(S(drop=0.02), head={'yaw': 12, 'lean': -6})), (40, S()),
                         (60, merge(S(drop=0.02), head={'yaw': -12, 'lean': -2})), (80, S())])
    run_cycle(S, None, stride=0.9, lean=10)
    dashes(S)
    reactions(S)
    ward = merge(S(drop=0.04), arm_L=(V(0.3, 0.06, -0.08), V(-0.2, -1, -0.3)), hand_L={'dir': V(0, 0.3, 1), 'fwd': V(1, 0, 0)},
                 arm_R=(V(0.3, -0.06, -0.08), V(-0.2, 1, -0.3)), hand_R={'dir': V(0, -0.3, 1), 'fwd': V(1, 0, 0)})
    keys('Block', 0, 6, [(0, S()), (3, ward), (6, ward)])
    # Shared cast: one hand raised, then swept forward.
    up = merge(S(drop=0.02, yaw=15), arm_R=(V(0.1, 0.22, 0.24), V(-0.3, 1, 0)), hand_R={'dir': V(0, 0, 1), 'fwd': V(1, 0, 0)})
    sweep = merge(S(drop=0.06, yaw=-10, lean=10), arm_R=(V(0.52, 0.02, -0.2), V(-0.2, 1, -0.3)), hand_R={'dir': V(1, 0, -0.2), 'fwd': V(0, 0, -1)})
    keys('Windup', 0, 12, [(0, S()), (12, up)])
    keys('Strike', 0, 14, [(0, up), (3, sweep), (8, sweep), (14, S())])
    # Tear Apart Reality: palms together before it, then pulled slowly apart as if prying something open; flung wide.
    pressed = merge(S(drop=0.04, lean=8), arm_L=(V(0.34, -0.02, -0.12), V(-0.2, -1, -0.2)), hand_L={'dir': V(0.3, 0, 1), 'fwd': V(0, 1, 0)},
                    arm_R=(V(0.34, 0.02, -0.12), V(-0.2, 1, -0.2)), hand_R={'dir': V(0.3, 0, 1), 'fwd': V(0, -1, 0)})
    prying = merge(S(drop=0.1, lean=4), head={'yaw': 0, 'lean': -10},
                   arm_L=(V(0.3, -0.34, -0.06), V(-0.2, -1, -0.2)), hand_L={'dir': V(0.3, -0.3, 1), 'fwd': V(0, 1, 0)},
                   arm_R=(V(0.3, 0.34, -0.06), V(-0.2, 1, -0.2)), hand_R={'dir': V(0.3, 0.3, 1), 'fwd': V(0, -1, 0)})
    flung = merge(S(drop=0.0, lean=-10), spine={'yaw': 0, 'lean': -12}, head={'yaw': 0, 'lean': -24},
                  arm_L=(V(0.04, -0.56, 0.1), V(0, -0.3, -1)), hand_L={'dir': V(0, -1, 0.4), 'fwd': V(1, 0, 0)},
                  arm_R=(V(0.04, 0.56, 0.1), V(0, 0.3, -1)), hand_R={'dir': V(0, 1, 0.4), 'fwd': V(1, 0, 0)})
    keys('tear_apart_reality.Windup', 0, 60, [(0, S()), (12, pressed), (52, prying), (60, prying)])
    keys('tear_apart_reality.Strike', 0, 24, [(0, prying), (4, flung), (16, flung), (24, S())])
    # Recurrent Repel: hunched in with arms crossed, then thrown open in a burst.
    crossed = merge(S(drop=0.12, lean=24), head={'yaw': 0, 'lean': 10},
                    arm_L=(V(0.22, 0.08, -0.2), V(-0.2, -1, -0.4)), hand_L={'dir': V(0.2, 0.8, 0.3)},
                    arm_R=(V(0.22, -0.08, -0.16), V(-0.2, 1, -0.4)), hand_R={'dir': V(0.2, -0.8, 0.3)})
    keys('recurrent_repel.Windup', 0, 14, [(0, S()), (10, crossed), (14, crossed)])
    keys('recurrent_repel.Strike', 0, 18, [(0, crossed), (3, flung), (10, flung), (18, S())])


PREVIEW = [('Idle', 0), ('Run', 0), ('Block', 6), ('Windup', 12), ('tear_apart_reality.Windup', 12),
           ('tear_apart_reality.Windup', 60), ('tear_apart_reality.Strike', 6), ('recurrent_repel.Windup', 14),
           ('DashSide', 6), ('HitHeavy', 3), ('Stun', 0), ('Death', 24)]
