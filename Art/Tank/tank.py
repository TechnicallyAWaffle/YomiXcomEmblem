# The Tank: heavy plate, a flanged mace in the right hand and a tower shield on the left. Run by
# Art/common/build_unit.py. Clips: the shared set (shared Windup / Strike: a shield bash), plus mace_strike (Crush: an
# overhead blow) and shockwave (Tremors: the mace raised high and slammed into the ground).
#
# The shield hangs from the left fist: its long axis runs along the hand (the hand's 'dir'), and its face is the
# hand's front (the hand's 'fwd'). The mace runs along the right fist's front, like the Fighter's sword ('blade').

GX, GZ = 0.27, 0.825      # left fist centre (Blender x, z) at rest; the right one is at -GX


def build_mesh():
    m = MeshBuilder()
    def side(s, k, X):
        m.box(X(0.02, 0.19), (-0.1, 0.1), (0.48, 0.88), 'thigh.' + s)
        m.box(X(0.03, 0.2), (-0.135, -0.1), (0.6, 0.86), 'thigh.' + s, 'Metal')     # tasset
        m.box(X(0.03, 0.18), (-0.085, 0.085), (0.1, 0.48), 'shin.' + s)
        m.box(X(0.025, 0.185), (-0.105, -0.08), (0.12, 0.46), 'shin.' + s, 'Metal')  # greave
        m.box(X(0.02, 0.19), (-0.2, 0.09), (0.0, 0.11), 'foot.' + s, 'Metal')
        m.box(X(0.19, 0.42), (-0.14, 0.14), (1.28, 1.5), 'upper_arm.' + s, 'Metal')  # pauldron
        m.box(X(0.2, 0.34), (-0.07, 0.07), (1.14, 1.42), 'upper_arm.' + s)
        m.box(X(0.2, 0.34), (-0.075, 0.075), (0.9, 1.14), 'forearm.' + s, 'Metal')   # vambrace
        m.box(X(0.2, 0.34), (-0.075, 0.075), (0.76, 0.9), 'hand.' + s, 'Metal')      # gauntlet
    sym(side)
    m.box((-0.2, 0.2), (-0.12, 0.12), (0.84, 1.06), 'hips', 'Metal')
    m.box((-0.25, 0.25), (-0.15, 0.15), (1.02, 1.46), 'spine')
    m.box((-0.22, 0.22), (-0.175, -0.14), (1.1, 1.44), 'spine', 'Metal')               # breastplate
    m.box((-0.13, 0.13), (-0.15, 0.12), (1.45, 1.75), 'head', 'Metal')                 # helm
    m.box((-0.02, 0.02), (-0.12, 0.16), (1.75, 1.83), 'head')                           # crest
    # Mace (right fist, forward): grip, collar, a heavy flanged head.
    mx = -GX
    m.taper(0.12, -0.5, (mx, GZ), 0.02, 0.02, 0.02, 0.02, 'hand.R', 'Metal')
    m.cbox((mx, -0.5, GZ), (0.045, 0.02, 0.045), 'hand.R', 'Metal')
    m.cbox((mx, -0.63, GZ), (0.075, 0.11, 0.075), 'hand.R', 'Metal')
    m.cbox((mx, -0.63, GZ), (0.12, 0.08, 0.025), 'hand.R', 'Metal')                     # flanges
    m.cbox((mx, -0.63, GZ), (0.025, 0.08, 0.12), 'hand.R', 'Metal')
    # Tower shield on the left fist: face forward (-Y), long axis vertical (along the hand), boss in the middle.
    m.box((GX - 0.19, GX + 0.19), (-0.16, -0.11), (GZ - 0.36, GZ + 0.36), 'hand.L')
    m.box((GX - 0.21, GX + 0.21), (-0.165, -0.1), (GZ + 0.32, GZ + 0.4), 'hand.L', 'Metal')
    m.box((GX - 0.21, GX + 0.21), (-0.165, -0.1), (GZ - 0.4, GZ - 0.32), 'hand.L', 'Metal')
    m.cbox((GX, -0.18, GZ), (0.07, 0.03, 0.07), 'hand.L', 'Metal')
    m.box((GX - 0.03, GX + 0.03), (-0.11, -0.06), (GZ - 0.03, GZ + 0.03), 'hand.L', 'Metal')   # the handle
    return m.finish()


# Shield aims: 'dir' = its long axis, 'fwd' = where its face looks.
SHIELD_UP = {'arm.L': (V(0.3, -0.06, -0.3), V(0.2, -1, -0.6)), 'hand.L': {'dir': V(0.05, 0, -1), 'fwd': V(1, -0.15, 0)}}
SHIELD_HIGH = {'arm.L': (V(0.32, 0.02, -0.1), V(0.2, -1, -0.5)), 'hand.L': {'dir': V(0.15, 0, -1), 'fwd': V(1, 0, 0.1)}}
MACE_REST = {'arm.R': (V(0.05, 0.12, -0.46), V(-0.3, 1, 0)), 'hand.R': {'dir': V(0.4, 0, -1), 'blade': V(0.55, 0.15, 0.8)}}

def S(drop=0.0, lean=6, yaw=15, f=0.0):
    return plus({
        'hips': {'off': V(f, 0, -0.1 - drop), 'yaw': yaw, 'lean': 4},
        'spine': {'yaw': 0, 'lean': lean},
        'head': {'yaw': -yaw, 'lean': -lean},
        'leg.L': (stance_foot('L', 0.16 + f, -0.1), V(1, -0.3, 0)),
        'leg.R': (stance_foot('R', -0.16 + f, 0.08), V(1, 0.3, 0)),
    }, plus(SHIELD_UP, MACE_REST))


def build_clips():
    keys('Idle', 0, 60, [(fr, S(drop=d)) for fr, d in ((0, 0), (20, 0.025), (40, 0), (60, 0))])
    def arms(ph):
        swing = 0.08 if ph in (0, 1) else -0.08
        return plus(SHIELD_UP, {'arm.R': (V(0.02 - swing, 0.13, -0.44), V(-0.3, 1, 0)),
                                'hand.R': {'dir': V(0.4, 0, -1), 'blade': V(0.6, 0.15, 0.75)}})
    run_cycle(S, arms, stride=0.85, lean=12, lift=0.8)
    dashes(S)
    reactions(S)
    brace = lambda d: plus(merge(S(drop=0.1 + d, lean=14, yaw=5), head={'yaw': -5, 'lean': -6},
                                 leg_L=(stance_foot('L', 0.22, -0.12), V(1, -0.3, 0)), leg_R=(stance_foot('R', -0.26, 0.1), V(1, 0.3, 0))),
                           SHIELD_HIGH)
    keys('Block', 0, 6, [(0, S()), (3, brace(0.03)), (6, brace(0.0))])

    # Shared windup / strike: shield drawn back, then driven forward in a bash.
    pull = plus(merge(S(drop=0.06, yaw=25), hips={'off': V(-0.08, 0, -0.14), 'yaw': 25, 'lean': 2}),
                {'arm.L': (V(0.12, -0.12, -0.3), V(-0.2, -1, -0.5)), 'hand.L': {'dir': V(0.05, 0, -1), 'fwd': V(1, -0.3, 0)}})
    bash = plus(merge(S(drop=0.08, yaw=-5, lean=16), hips={'off': V(0.18, 0, -0.16), 'yaw': -5, 'lean': 12},
                      leg_L=(stance_foot('L', 0.4, -0.1), V(1, -0.3, 0))),
                {'arm.L': (V(0.46, 0.0, -0.22), V(0, -1, -0.5)), 'hand.L': {'dir': V(0.1, 0, -1), 'fwd': V(1, 0, 0)}})
    keys('Windup', 0, 12, [(0, S()), (12, pull)])
    keys('Strike', 0, 14, [(0, pull), (3, bash), (7, bash), (14, S())])

    # Crush: the mace raised over the right shoulder, then brought straight down in front.
    raised = merge(S(drop=0.04, yaw=30, lean=-2), hips={'off': V(-0.06, 0, -0.12), 'yaw': 30, 'lean': -2},
                   spine={'yaw': 5, 'lean': -6}, head={'yaw': -35, 'lean': 2},
                   arm_R=(V(-0.04, 0.16, 0.24), V(0, 1, 0.2)), hand_R={'dir': V(-0.2, 0.2, 1), 'blade': V(-0.55, 0.15, 0.8)})
    crush = merge(S(drop=0.14, yaw=-5, lean=22), hips={'off': V(0.14, 0, -0.2), 'yaw': -5, 'lean': 16},
                  head={'yaw': 5, 'lean': -24}, leg_L=(stance_foot('L', 0.36, -0.1), V(1, -0.3, 0)),
                  arm_R=(V(0.44, 0.04, -0.4), V(0, 1, -0.3)), hand_R={'dir': V(1, 0, -0.3), 'blade': V(0.7, -0.05, -0.7)})
    keys('mace_strike.Windup', 0, 15, [(0, S()), (10, raised), (15, raised)])
    keys('mace_strike.Strike', 0, 20, [(0, raised), (3, crush), (10, crush), (20, S())])

    # Tremors: rise up with the mace high overhead, then slam it into the ground in a deep crouch.
    lifted = merge(S(drop=-0.02, yaw=10, lean=-8), hips={'off': V(-0.04, 0, -0.04), 'yaw': 10, 'lean': -6},
                   spine={'yaw': 0, 'lean': -10}, head={'yaw': -10, 'lean': -10},
                   arm_R=(V(0.02, 0.08, 0.5), V(-0.2, 1, 0.2)), hand_R={'dir': V(0, 0, 1), 'blade': V(-0.3, -0.1, 0.95)},
                   arm_L=(V(0.18, -0.28, -0.1), V(0, -1, -0.4)), hand_L={'dir': V(0, -0.3, -1), 'fwd': V(1, -0.6, 0)})
    slam = merge(S(drop=0.3, yaw=0, lean=30), hips={'off': V(0.12, 0, -0.36), 'yaw': 0, 'lean': 24},
                 head={'yaw': 0, 'lean': -30},
                 leg_L=(stance_foot('L', 0.32, -0.14), V(1, -0.4, 0)), leg_R=(stance_foot('R', -0.34, 0.12, 0.02), V(1, 0.4, 0)),
                 arm_R=(V(0.5, 0.0, -0.55), V(0, 1, -0.2)), hand_R={'dir': V(1, 0, -0.4), 'blade': V(0.45, 0, -0.9)})
    keys('shockwave.Windup', 0, 24, [(0, S()), (14, lifted), (24, lifted)])
    keys('shockwave.Strike', 0, 26, [(0, lifted), (4, slam), (16, slam), (26, S())])


PREVIEW = [('Idle', 0), ('Run', 0), ('Run', 4), ('Block', 6), ('Windup', 12), ('Strike', 4),
           ('mace_strike.Windup', 15), ('mace_strike.Strike', 4), ('shockwave.Windup', 24), ('shockwave.Strike', 6),
           ('DashForward', 6), ('DashSide', 6), ('HitHeavy', 3), ('Interrupted', 3), ('Stun', 0), ('Death', 8), ('Death', 24)]
