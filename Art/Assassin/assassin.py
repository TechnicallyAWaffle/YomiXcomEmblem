# The Assassin: a lithe humanoid, hooded and masked, a dagger in the right hand. Run by Art/common/build_unit.py.
# Clips: the shared set (shared Windup / Strike: a quick stab, its basic Strike), plus hidden_strike (Hidden Strike: a
# low crouch with the dagger drawn back, a sprint, an upward stab).

def build_mesh():
    m = MeshBuilder()
    humanoid(m, w=0.82, mats={'hand': 'Dark', 'foot': 'Dark'})
    m.box((-0.15, 0.15), (-0.09, 0.09), (0.9, 0.96), 'hips', 'Dark')              # sash
    m.box((-0.06, 0.12), (-0.1, -0.08), (0.62, 0.92), 'hips', 'Dark')             # its tail
    m.box((-0.12, 0.12), (-0.105, 0.12), (1.47, 1.76), 'head')                    # hood
    m.box((-0.09, 0.09), (-0.125, -0.1), (1.48, 1.58), 'head', 'Dark')            # mask
    m.box((-0.07, 0.07), (-0.13, -0.12), (1.6, 1.63), 'head', 'Glow')              # eye slit
    m.box((-0.17, 0.17), (0.08, 0.12), (1.0, 1.42), 'spine', 'Dark')              # short cloak
    m.box((0.2, 0.33), (-0.065, 0.065), (0.95, 1.06), 'forearm.L', 'Metal')       # bracer
    held_blade(m, 'R', 0.26, width=0.026, grip=0.12, guard=0.03)
    return m.finish()


KNIFE = {'hand.R': {'dir': V(0.4, 0.1, -1), 'blade': V(1, 0, 0.15)}}

def S(drop=0.0, lean=14, yaw=-20, f=0.0):
    return plus({
        'hips': {'off': V(f - 0.02, 0, -0.14 - drop), 'yaw': yaw, 'lean': 10},
        'spine': {'yaw': 0, 'lean': lean},
        'head': {'yaw': -yaw, 'lean': -lean - 6},
        'leg.R': (stance_foot('R', 0.2 + f, 0.06), V(1, 0.3, 0)),
        'leg.L': (stance_foot('L', -0.2 + f, -0.1), V(1, -0.3, 0)),
        'arm.R': (V(0.3, 0.16, -0.36 - drop * 0.5), V(-0.2, 1, -0.5)),
        'arm.L': (V(0.2, -0.22, -0.3), V(-0.3, -1, -0.2)),
        'hand.L': {'dir': V(0.6, -0.2, -0.7)},
    }, KNIFE)


def build_clips():
    keys('Idle', 0, 60, [(fr, S(drop=d)) for fr, d in ((0, 0), (15, 0.02), (30, 0), (45, 0.02), (60, 0))])
    run_cycle(S, lambda ph: plus({'arm.R': (V(0.06 + (0.12 if ph in (2, 3) else -0.1), 0.18, -0.36), V(-0.4, 1, -0.3)),
                                  'arm.L': (V(0.06 + (0.12 if ph in (0, 1) else -0.1), -0.18, -0.36), V(-0.4, -1, -0.3))}, KNIFE),
              stride=1.15, lean=24)
    dashes(S)
    reactions(S)
    guard = merge(S(drop=0.06), arm_L=(V(0.28, -0.04, -0.04), V(-0.2, -1, -0.4)), hand_L={'dir': V(0.2, 0.6, 0.8)},
                  arm_R=(V(0.22, 0.08, -0.16), V(-0.2, 1, -0.4)), hand_R={'dir': V(0, -0.5, 1), 'blade': V(0.3, -0.9, 0.3)})
    keys('Block', 0, 6, [(0, S()), (3, guard), (6, guard)])
    # A quick stab: the dagger drawn back to the hip, then driven forward.
    drawn = merge(S(drop=0.04, yaw=-30), arm_R=(V(-0.02, 0.24, -0.34), V(-0.4, 1, 0)), hand_R={'dir': V(0.6, 0.2, -1), 'blade': V(1, 0, 0)})
    stab = merge(S(drop=0.08, yaw=0, lean=24, f=0.12), arm_R=(V(0.52, 0.04, -0.2), V(-0.2, 1, -0.3)), hand_R={'dir': V(1, 0, -0.2), 'blade': V(1, -0.05, 0.05)},
                 leg_R=(stance_foot('R', 0.42, 0.04), V(1, 0.3, 0)))
    for pre in ('', 'assassin_strike.'):
        keys(pre + 'Windup', 0, 10, [(0, S()), (8, drawn), (10, drawn)])
        keys(pre + 'Strike', 0, 12, [(0, drawn), (2, stab), (6, stab), (12, S())])
    # Hidden Strike: down low in a coil, a sprint in, and an upward stab under the ribs.
    coil = merge(S(drop=0.24, lean=30, yaw=-35), hips={'off': V(-0.06, 0, -0.38), 'yaw': -35, 'lean': 20},
                 leg_R=(stance_foot('R', 0.3, 0.1), V(1, 0.3, 0)), leg_L=(stance_foot('L', -0.34, -0.12, 0.04), V(1, -0.3, -0.4)),
                 arm_R=(V(-0.1, 0.24, -0.3), V(-0.6, 1, 0)), hand_R={'dir': V(0.4, 0.2, -1), 'blade': V(0.8, 0, -0.5)})
    sprint = plus(merge(S(drop=0.1, lean=26), hips={'off': V(0.1, 0, -0.16), 'yaw': 0, 'lean': 26},
                        leg_R=(stance_foot('R', 0.42, 0, 0.1), V(1, 0, 0)), leg_L=(stance_foot('L', -0.46, 0, 0.24), V(1, 0, 0))),
                  {'arm.R': (V(-0.08, 0.2, -0.32), V(-0.6, 1, 0)), 'hand.R': {'dir': V(0.4, 0.2, -1), 'blade': V(0.8, 0, -0.5)}})
    upper_stab = merge(S(drop=0.06, lean=6, yaw=-10, f=0.1), arm_R=(V(0.46, 0.06, 0.02), V(-0.3, 1, -0.6)),
                       hand_R={'dir': V(0.6, 0, 0.6), 'blade': V(0.6, -0.05, 0.8)}, leg_R=(stance_foot('R', 0.4, 0.06), V(1, 0.3, 0)))
    keys('hidden_strike.Windup', 0, 20, [(0, S()), (10, coil), (20, merge(coil, head={'yaw': 30, 'lean': -36}))])
    keys('hidden_strike.Dash', 0, 6, [(0, coil), (3, sprint), (6, sprint)])
    keys('hidden_strike.Strike', 0, 16, [(0, sprint), (3, upper_stab), (8, upper_stab), (16, S())])


PREVIEW = [('Idle', 0), ('Run', 0), ('Run', 4), ('Block', 6), ('Windup', 10), ('Strike', 3),
           ('hidden_strike.Windup', 20), ('hidden_strike.Dash', 6), ('hidden_strike.Strike', 4), ('DashSide', 6), ('Stun', 0), ('Death', 24)]
