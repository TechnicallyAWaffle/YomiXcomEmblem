# The Caster: a humanoid in a long tabard, its bare arms etched from shoulder to fingertip. Run by
# Art/common/build_unit.py. Clips: the shared set (shared Windup / Strike: a two-handed cast), plus kinetic_blast
# (Kinetic Blast: a palm drawn back, then thrust out) and distort_space (Distort Space: both hands raised and
# wrenched, then thrown down at the target point).

ETCHINGS = etch_tiles(1)


def build_mesh():
    m = MeshBuilder()
    humanoid(m, w=0.9, mats={'upper_arm': 'Etched', 'forearm': 'Etched', 'hand': 'Etched'}, skip=('head',))
    def tabard(s, k, X):
        m.box(X(0.0, 0.18), (-0.105, -0.075), (0.38, 0.9), 'thigh.' + s)          # tabard, front and back
        m.box(X(0.0, 0.18), (0.075, 0.105), (0.38, 0.9), 'thigh.' + s)
        m.box(X(0.2, 0.34), (-0.07, 0.07), (1.36, 1.46), 'upper_arm.' + s, 'Metal')   # shoulder rings
    sym(tabard)
    m.box((-0.17, 0.17), (-0.1, 0.1), (0.88, 0.96), 'hips', 'Metal')               # belt
    m.box((-0.19, 0.19), (-0.115, 0.115), (1.06, 1.44), 'spine')
    m.box((-0.06, 0.06), (-0.125, -0.115), (1.1, 1.38), 'spine', 'Metal')          # a strip of script down the front
    m.box((-0.1, 0.1), (-0.115, 0.1), (1.46, 1.7), 'head')
    m.box((-0.12, 0.12), (-0.07, 0.13), (1.52, 1.78), 'head')                      # cowl
    m.box((-0.08, 0.08), (-0.13, -0.115), (1.57, 1.61), 'head', 'Glow')            # eyes
    return m.finish()


def S(drop=0.0, lean=4, yaw=10, f=0.0):
    return {
        'hips': {'off': V(f, 0, -0.05 - drop), 'yaw': yaw, 'lean': 2},
        'spine': {'yaw': 0, 'lean': lean},
        'head': {'yaw': -yaw, 'lean': -lean},
        'leg.L': (stance_foot('L', 0.12 + f, -0.04), V(1, -0.2, 0)),
        'leg.R': (stance_foot('R', -0.12 + f, 0.04), V(1, 0.3, 0)),
        'arm.L': (V(0.18, -0.24, -0.42 - drop * 0.5), V(-0.4, -1, 0)), 'hand.L': {'dir': V(0.4, -0.1, -1)},
        'arm.R': (V(0.18, 0.24, -0.42 - drop * 0.5), V(-0.4, 1, 0)), 'hand.R': {'dir': V(0.4, 0.1, -1)},
    }


def build_clips():
    keys('Idle', 0, 60, [(fr, S(drop=d)) for fr, d in ((0, 0), (20, 0.02), (40, 0), (60, 0))])
    run_cycle(S, None, stride=0.95, lean=14)
    dashes(S)
    reactions(S)
    ward = merge(S(drop=0.06, lean=10), arm_L=(V(0.34, -0.12, -0.02), V(-0.2, -1, -0.3)), hand_L={'dir': V(0, 0, 1), 'fwd': V(1, 0, 0)},
                 arm_R=(V(0.34, 0.12, -0.02), V(-0.2, 1, -0.3)), hand_R={'dir': V(0, 0, 1), 'fwd': V(1, 0, 0)})
    keys('Block', 0, 6, [(0, S()), (3, ward), (6, ward)])
    # Kinetic Blast (and the shared cast): the right palm drawn back by the ear, then thrust out flat.
    draw = merge(S(drop=0.04, yaw=35), spine={'yaw': 10, 'lean': 0}, arm_R=(V(-0.04, 0.26, 0.0), V(-0.6, 1, -0.3)),
                 hand_R={'dir': V(0, 0, 1), 'fwd': V(1, 0, 0)}, arm_L=(V(0.36, -0.12, -0.18), V(-0.2, -1, -0.3)), hand_L={'dir': V(1, 0, 0), 'fwd': V(0, 0, 1)})
    thrust = merge(S(drop=0.08, yaw=-15, lean=12, f=0.06), spine={'yaw': -10, 'lean': 12}, arm_R=(V(0.56, 0.06, -0.12), V(-0.2, 1, -0.3)),
                   hand_R={'dir': V(0, 0, 1), 'fwd': V(1, 0, 0)}, leg_L=(stance_foot('L', 0.3, -0.04), V(1, -0.2, 0)))
    for pre in ('', 'kinetic_blast.'):
        keys(pre + 'Windup', 0, 14, [(0, S()), (10, draw), (14, draw)])
        keys(pre + 'Strike', 0, 16, [(0, draw), (2, thrust), (7, thrust), (16, S())])
    # Distort Space: both hands raised and twisted against each other, then thrown down and out.
    raised = merge(S(drop=-0.02, lean=-8), spine={'yaw': 0, 'lean': -10}, head={'yaw': 0, 'lean': -14},
                   arm_L=(V(0.12, -0.18, 0.36), V(0, -1, 0.2)), hand_L={'dir': V(0.2, 0.3, 1), 'fwd': V(0.5, 1, 0)},
                   arm_R=(V(0.12, 0.18, 0.36), V(0, 1, 0.2)), hand_R={'dir': V(0.2, -0.3, 1), 'fwd': V(-0.5, 1, 0)})
    twisted = merge(raised, spine={'yaw': 20, 'lean': -10}, hand_L={'dir': V(0.5, 0.3, 1), 'fwd': V(1, 0, 0)}, hand_R={'dir': V(-0.2, -0.5, 1), 'fwd': V(1, 0, 0)})
    thrown = merge(S(drop=0.14, lean=26, f=0.06), spine={'yaw': 0, 'lean': 20}, head={'yaw': 0, 'lean': -20},
                   arm_L=(V(0.5, -0.14, -0.36), V(0, -1, 0)), hand_L={'dir': V(1, 0, -0.6), 'fwd': V(0, 0, -1)},
                   arm_R=(V(0.5, 0.14, -0.36), V(0, 1, 0)), hand_R={'dir': V(1, 0, -0.6), 'fwd': V(0, 0, -1)})
    keys('distort_space.Windup', 0, 30, [(0, S()), (12, raised), (22, twisted), (30, raised)])
    keys('distort_space.Strike', 0, 20, [(0, raised), (3, thrown), (11, thrown), (20, S())])


PREVIEW = [('Idle', 0), ('Run', 0), ('Block', 6), ('kinetic_blast.Windup', 14), ('kinetic_blast.Strike', 3),
           ('distort_space.Windup', 22), ('distort_space.Strike', 4), ('DashSide', 6), ('HitHeavy', 3), ('Stun', 0), ('Death', 24)]
