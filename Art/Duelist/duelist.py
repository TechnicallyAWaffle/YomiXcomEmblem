# The Duelist: a lean fencer in a long split coat, one long straight blade in the right hand (always drawn).
# Run by Art/common/build_unit.py with unit_tools.py's helpers in scope. Clips: the shared set, plus
# finite_blade (Finite Blade: a long reaching lunge), gouge (Gouge: dash and slice through), and
# unsigned_rebuttal (Parry: blade raised across the body, then a riposte flick).

GX, GZ = -0.27, 0.825      # right fist centre (Blender x, z) at rest


def build_mesh():
    m = MeshBuilder()
    def side(s, k, X):
        m.box(X(0.035, 0.165), (-0.07, 0.07), (0.48, 0.88), 'thigh.' + s)
        m.box(X(0.045, 0.155), (-0.06, 0.06), (0.1, 0.48), 'shin.' + s)
        m.box(X(0.04, 0.16), (-0.17, 0.07), (0.0, 0.1), 'foot.' + s, 'Metal')
        m.box(X(0.035, 0.2), (0.07, 0.11), (0.4, 0.88), 'thigh.' + s)              # coat tail, back
        m.box(X(0.05, 0.19), (-0.11, -0.07), (0.62, 0.88), 'thigh.' + s)           # coat front flap
        m.box(X(0.215, 0.325), (-0.055, 0.055), (1.14, 1.42), 'upper_arm.' + s)
        m.box(X(0.22, 0.32), (-0.05, 0.05), (0.9, 1.14), 'forearm.' + s)
        m.box(X(0.212, 0.328), (-0.06, 0.06), (0.9, 1.0), 'forearm.' + s, 'Metal')   # bracer
        m.box(X(0.225, 0.315), (-0.052, 0.052), (0.775, 0.89), 'hand.' + s)
    sym(side)
    m.box((0.19, 0.36), (-0.085, 0.085), (1.34, 1.47), 'upper_arm.L', 'Metal')        # one pauldron, the off side
    m.box((-0.16, 0.16), (-0.095, 0.095), (0.86, 1.06), 'hips')
    m.box((-0.17, 0.17), (-0.1, 0.1), (0.9, 0.96), 'hips', 'Metal')                     # belt
    m.box((-0.19, 0.19), (-0.11, 0.11), (1.04, 1.44), 'spine')
    m.box((-0.12, 0.12), (-0.02, 0.135), (1.4, 1.56), 'spine')                         # high collar
    m.box((-0.07, 0.07), (-0.125, -0.11), (1.2, 1.36), 'spine', 'Metal')                # the sigil plate
    m.box((-0.11, 0.11), (-0.12, 0.1), (1.46, 1.71), 'head')
    m.box((-0.115, 0.115), (-0.06, 0.115), (1.6, 1.74), 'head')                         # swept-back hair
    # The blade, along the right fist's front (-Y): pommel, grip, guard, a long straight blade to a point.
    m.cbox((GX, 0.085, GZ), (0.02, 0.02, 0.02), 'hand.R', 'Metal')
    m.taper(0.07, -0.09, (GX, GZ), 0.014, 0.016, 0.014, 0.016, 'hand.R', 'Metal')
    m.cbox((GX, -0.1, GZ), (0.018, 0.012, 0.07), 'hand.R', 'Metal')
    m.taper(-0.11, -1.0, (GX, GZ), 0.008, 0.024, 0.006, 0.018, 'hand.R', 'Metal')
    m.taper(-1.0, -1.12, (GX, GZ), 0.006, 0.018, 0.002, 0.002, 'hand.R', 'Metal')
    return m.finish()


# ---- the stance: right foot and blade forward, side-on (turned left), off hand back at the hip
EN_GARDE = {'hand.R': {'dir': V(0.3, 0, -1), 'blade': V(1, 0.08, 0.22)}}
def S(drop=0.0, lean=6, yaw=-30, f=0.0):
    return plus({
        'hips': {'off': V(f, 0, -0.07 - drop), 'yaw': yaw, 'lean': 3},
        'spine': {'yaw': -5, 'lean': lean},
        'head': {'yaw': -yaw + 5, 'lean': -lean},
        'leg.R': (stance_foot('R', 0.22 + f, 0.02), V(1, 0.2, 0)),
        'leg.L': (stance_foot('L', -0.2 + f, -0.06), V(1, -0.3, 0)),
        'arm.R': (V(0.3, 0.14, -0.3 - drop * 0.5), V(-0.2, 1, -0.6)),
        'arm.L': (V(-0.08, -0.2, -0.4), V(-0.4, -1, 0)),
        'hand.L': {'dir': V(-0.3, 0.2, -1)},
    }, EN_GARDE)

TRAIL = {'arm.R': (V(-0.06, 0.2, -0.4), V(-1, 0.3, 0)), 'hand.R': {'dir': V(-0.2, 0, -1), 'blade': V(-1, 0.15, -0.35)}}
PARRY = {'arm.R': (V(0.3, 0.02, -0.08), V(0, 1, -0.6)), 'hand.R': {'dir': V(0.2, -0.3, -1), 'blade': V(0.25, -0.35, 1)},
         'arm.L': (V(0.05, -0.25, -0.35), V(-0.3, -1, 0))}


def build_clips():
    keys('Idle', 0, 60, [(fr, S(drop=d)) for fr, d in ((0, 0), (15, 0.02), (30, 0), (45, 0.02), (60, 0))])
    def arms(ph):
        free = [{'arm.L': (V(0.2, -0.14, -0.25), V(-0.3, 0, -1))}, {'arm.L': (V(0.04, -0.14, -0.32), V(-0.6, 0, -1))},
                {'arm.L': (V(-0.2, -0.16, -0.34), V(-1, 0, 0.1))}, {'arm.L': (V(0.04, -0.14, -0.32), V(-0.6, 0, -1))}][ph]
        return plus(free, TRAIL)
    run_cycle(S, arms, stride=1.05, lean=20)
    dashes(S, forward_arms=plus(upper(S()), TRAIL))
    reactions(S)
    parry = lambda d=0.0: plus(merge(S(drop=0.06 + d, lean=4, yaw=-15), head={'yaw': 20, 'lean': -6}), PARRY)
    keys('Block', 0, 6, [(0, S()), (3, parry(0.02)), (6, parry())])
    keys('unsigned_rebuttal.Windup', 0, 6, [(0, S()), (3, parry(0.02)), (6, parry())])
    riposte = plus(merge(S(yaw=-40, lean=10), hips={'off': V(0.12, 0, -0.1), 'yaw': -40, 'lean': 6}),
                   {'arm.R': (V(0.5, 0.2, -0.12), V(0, 1, -0.4)), 'hand.R': {'dir': V(1, 0.2, -0.2), 'blade': V(0.85, 0.5, -0.1)}})
    keys('unsigned_rebuttal.Strike', 0, 14, [(0, parry()), (3, riposte), (8, riposte), (14, S())])

    # Shared windup / strike: draw the blade back low to the left hip, then a flat cut across to the right.
    drawn = plus(merge(S(drop=0.05, yaw=-50, lean=10), hips={'off': V(-0.04, 0, -0.12), 'yaw': -50, 'lean': 4}),
                 {'arm.R': (V(0.12, -0.18, -0.36), V(0, 1, -0.6)), 'hand.R': {'dir': V(0.2, -0.3, -1), 'blade': V(-0.75, -0.35, -0.15)},
                  'arm.L': (V(0.1, -0.3, -0.3), V(0, -1, -0.3))})
    cut = plus(merge(S(drop=0.06, yaw=10, lean=12), hips={'off': V(0.1, 0, -0.13), 'yaw': 10, 'lean': 8}),
               {'arm.R': (V(0.4, 0.34, -0.14), V(0, 1, -0.4)), 'hand.R': {'dir': V(0.6, 0.4, -0.5), 'blade': V(0.4, 1, 0.05)},
                'arm.L': (V(-0.05, -0.3, -0.3), V(-0.3, -1, 0))})
    keys('Windup', 0, 12, [(0, S()), (12, drawn)])
    keys('Strike', 0, 14, [(0, drawn), (3, cut), (7, cut), (14, S())])

    # Finite Blade: coil back with the point aimed, then a long lunge thrust that rises.
    coil = plus(merge(S(drop=0.1, yaw=-35, lean=2), hips={'off': V(-0.1, 0, -0.15), 'yaw': -35, 'lean': 0}),
                {'arm.R': (V(-0.02, 0.22, -0.08), V(-0.3, 1, -0.4)), 'hand.R': {'dir': V(0, 0.3, -1), 'blade': V(1, 0.05, 0.05)}})
    lunge = plus(merge(S(drop=0.14, yaw=-45, lean=14),
                       hips={'off': V(0.3, 0, -0.22), 'yaw': -45, 'lean': 10},
                       leg_R=(stance_foot('R', 0.75, 0.04), V(1, 0.2, 0)),
                       leg_L=(stance_foot('L', -0.35, -0.08), V(1, -0.3, 0)),
                       arm_L=(V(-0.25, -0.3, -0.3), V(-1, -0.2, 0))),
                 {'arm.R': (V(0.6, 0.04, -0.08), V(0, 1, -0.3)), 'hand.R': {'dir': V(1, 0, -0.3), 'blade': V(1, 0.02, 0.0)}})
    rise = merge(lunge, hand_R={'dir': V(0.6, 0, 0.6), 'blade': V(0.6, 0.1, 0.8)}, arm_R=(V(0.5, 0.06, 0.1), V(0, 1, -0.3)))
    keys('finite_blade.Windup', 0, 6, [(0, S()), (6, coil)])
    keys('finite_blade.Strike', 0, 18, [(0, coil), (2, lunge), (5, lunge), (8, rise), (12, rise), (18, S())])

    # Gouge: low crouch with the blade reversed behind, a dash with it trailing, and a cut through and past.
    crouch = plus(merge(S(drop=0.16, yaw=-10, lean=20), hips={'off': V(0, 0, -0.24), 'yaw': -10, 'lean': 20},
                        head={'yaw': 10, 'lean': -32},
                        leg_R=(stance_foot('R', 0.24, 0.04), V(1, 0.2, 0)), leg_L=(stance_foot('L', -0.3, -0.06, 0.02), V(1, -0.2, 0))),
                  {'arm.R': (V(-0.12, 0.22, -0.34), V(-1, 0.3, 0)), 'hand.R': {'dir': V(-0.3, 0, -1), 'blade': V(-1, 0.2, -0.15)},
                   'arm.L': (V(0.2, -0.1, -0.3), V(0.2, -0.3, -1))})
    def dash_pose(air):
        return {'hips': {'off': V(0.1, 0, -0.1 + 0.06 * air), 'yaw': 0, 'lean': 26},
                'spine': {'yaw': 0, 'lean': 18}, 'head': {'yaw': 0, 'lean': -34},
                'leg.R': (stance_foot('R', -0.5 + 0.06 * air, 0, 0.12 + 0.18 * air), V(1, 0, 0)),
                'leg.L': (stance_foot('L', 0.28, 0, 0.3 + 0.12 * air), V(1, 0, 0)),
                **upper(crouch)}
    through = plus(merge(S(drop=0.14, yaw=40, lean=16), hips={'off': V(0.16, 0, -0.22), 'yaw': 40, 'lean': 12},
                         head={'yaw': -40, 'lean': -20},
                         leg_R=(stance_foot('R', 0.4, -0.06), V(1, 0, 0)), leg_L=(stance_foot('L', -0.3, -0.04, 0.04), V(1, -0.2, 0))),
                   {'arm.R': (V(0.36, -0.36, -0.22), V(0.2, 1, -0.4)), 'hand.R': {'dir': V(0.4, -0.5, -0.6), 'blade': V(0.35, -1, -0.15)},
                    'arm.L': (V(-0.1, -0.32, -0.3), V(-1, -0.3, 0))})
    keys('gouge.Windup', 0, 9, [(0, S()), (5, crouch), (9, crouch)])
    keys('gouge.Dash', 0, 6, [(0, crouch), (2, dash_pose(0.0)), (6, dash_pose(1.0))])
    keys('gouge.Strike', 0, 16, [(0, dash_pose(1.0)), (3, through), (10, through), (16, S())])


PREVIEW = [('Idle', 0), ('Run', 0), ('Run', 4), ('Block', 6), ('Windup', 12), ('Strike', 4),
           ('finite_blade.Windup', 6), ('finite_blade.Strike', 4), ('finite_blade.Strike', 10), ('gouge.Windup', 9),
           ('gouge.Dash', 6), ('gouge.Strike', 5), ('unsigned_rebuttal.Strike', 5), ('DashSide', 6), ('HitHeavy', 3),
           ('Stun', 0), ('Death', 8), ('Death', 24)]
