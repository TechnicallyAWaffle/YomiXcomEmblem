# The Exemplar: a knight in plate with gold trim, a longsword in the right hand and two wings on its back (their own
# bones, EXTRA_BONES). Run by Art/common/build_unit.py. Clips: the shared set, plus spark_the_flame (Spark the Flame: a
# rising cut), intrepid_advance and light_the_pyre (a lunge: windup, dash, thrust), and split_apart_the_sky (Split
# Apart The Sky: the sword raised overhead, wings thrown wide, then a cleaving blow).

EXTRA_BONES = {'wing.L': (V(-0.12, -0.1, 1.38), V(-0.3, -0.62, 1.62), 'spine'),
               'wing.R': (V(-0.12, 0.1, 1.38), V(-0.3, 0.62, 1.62), 'spine')}


def build_mesh():
    m = MeshBuilder()
    humanoid(m, w=1.12, mats={'shin': 'Metal', 'forearm': 'Metal', 'hand': 'Metal', 'foot': 'Metal'}, skip=('head',))
    def trim(s, k, X):
        m.box(X(0.18, 0.4), (-0.13, 0.13), (1.3, 1.48), 'upper_arm.' + s, 'Metal')       # pauldrons
        m.box(X(0.18, 0.4), (-0.135, 0.135), (1.28, 1.31), 'upper_arm.' + s, 'Gold')
        m.box(X(0.02, 0.19), (-0.12, -0.09), (0.6, 0.86), 'thigh.' + s, 'Metal')         # tassets
        # A wing: three overlapping panels stepping out and up along the bone, a gold edge along the top.
        for (r0, r1, u0, u1, f) in ((0.1, 0.36, 1.1, 1.6, -0.17), (0.32, 0.56, 1.2, 1.74, -0.2), (0.52, 0.74, 1.36, 1.86, -0.23)):
            m.box(X(r0, r1), (-f - 0.015, -f + 0.015), (u0, u1), 'wing.' + s)
            m.box(X(r0, r1 + 0.02), (-f - 0.02, -f + 0.02), (u1 - 0.04, u1), 'wing.' + s, 'Gold')
    sym(trim)
    m.box((-0.22, 0.22), (-0.14, 0.13), (1.06, 1.44), 'spine', 'Metal')                  # breastplate
    m.box((-0.06, 0.06), (-0.15, -0.14), (1.12, 1.4), 'spine', 'Gold')
    m.box((-0.12, 0.12), (-0.14, 0.12), (1.46, 1.76), 'head', 'Metal')                   # helm
    m.box((-0.1, 0.1), (-0.15, -0.14), (1.58, 1.61), 'head', 'Dark')                     # visor slit
    m.box((-0.02, 0.02), (-0.1, 0.16), (1.76, 1.9), 'head', 'Gold')                      # crest
    m.box((-0.18, 0.18), (0.13, 0.16), (0.7, 1.44), 'spine')                             # tabard back
    held_blade(m, 'R', 0.95, width=0.03, grip=0.2, guard=0.1, mat='Metal')
    m.cbox((FIST['R'][0], -0.09, FIST['R'][1]), (0.02, 0.016, 0.1), 'hand.R', 'Gold')
    return m.finish()


WINGS = {'wing.L': {'yaw': 0, 'lean': 0}, 'wing.R': {'yaw': 0, 'lean': 0}}
def wings(spread=0.0, lift=0.0):
    """spread: + opens them out to the sides (degrees); lift: + raises them."""
    return {'wing.L': {'yaw': spread, 'tilt': -lift}, 'wing.R': {'yaw': -spread, 'tilt': lift}}

def S(drop=0.0, lean=4, yaw=-25, f=0.0):
    return plus({
        'hips': {'off': V(f, 0, -0.06 - drop), 'yaw': yaw, 'lean': 2},
        'spine': {'yaw': -5, 'lean': lean},
        'head': {'yaw': -yaw + 5, 'lean': -lean},
        'leg.R': (stance_foot('R', 0.18 + f, 0.04), V(1, 0.2, 0)),
        'leg.L': (stance_foot('L', -0.18 + f, -0.06), V(1, -0.3, 0)),
        'arm.R': (V(0.26, 0.16, -0.34 - drop * 0.5), V(-0.2, 1, -0.6)), 'hand.R': {'dir': V(0.3, 0, -1), 'blade': V(0.7, 0.1, 0.7)},
        'arm.L': (V(0.2, -0.04, -0.3), V(-0.3, -1, -0.2)), 'hand.L': {'dir': V(0.3, 0.2, -1)},
    }, wings(-10, 0))


def build_clips():
    keys('Idle', 0, 60, [(0, S()), (30, plus(S(drop=0.02), wings(-4, 4))), (60, S())])
    run_cycle(S, lambda ph: plus({'arm.R': (V(0.1, 0.2, -0.36), V(-0.4, 1, -0.3)), 'hand.R': {'dir': V(0.4, 0, -1), 'blade': V(0.7, 0.1, 0.7)},
                                  'arm.L': (V(0.06 + (0.1 if ph in (0, 1) else -0.08), -0.24, -0.38), V(-0.4, -1, 0))}, wings(-20, -6)),
              stride=0.95, lean=14)
    dashes(S, forward_arms=plus(upper(S()), wings(-30, -10)))
    reactions(S)
    guard = merge(S(drop=0.06, lean=8), arm_R=(V(0.3, 0.1, -0.06), V(-0.2, 1, -0.4)), hand_R={'dir': V(0, -0.4, 1), 'blade': V(0.2, -0.9, 0.4)},
                  arm_L=(V(0.3, -0.06, -0.12), V(-0.2, -1, -0.3)), wing_L={'yaw': 30, 'tilt': -10}, wing_R={'yaw': -30, 'tilt': 10})
    keys('Block', 0, 6, [(0, S()), (3, guard), (6, guard)])
    # Spark the Flame (and the shared strike): blade low and back, then a rising cut.
    low = merge(S(drop=0.08, yaw=-40), arm_R=(V(0.0, 0.3, -0.38), V(-0.4, 1, 0)), hand_R={'dir': V(0.2, 0.3, -1), 'blade': V(-0.4, 0.4, -0.8)})
    rise = merge(S(drop=0.04, yaw=10, lean=6, f=0.1), arm_R=(V(0.38, -0.06, 0.12), V(-0.2, 1, -0.4)), hand_R={'dir': V(0.6, -0.3, 0.7), 'blade': V(0.5, -0.4, 0.75)},
                 leg_R=(stance_foot('R', 0.38, 0.04), V(1, 0.2, 0)))
    for pre in ('', 'spark_the_flame.'):
        keys(pre + 'Windup', 0, 10, [(0, S()), (8, low), (10, low)])
        keys(pre + 'Strike', 0, 14, [(0, low), (3, rise), (7, rise), (14, S())])
    # Intrepid Advance / Light the Pyre: drawn back, a winged dash, a thrust.
    drawn = plus(merge(S(drop=0.12, lean=14, yaw=-45), arm_R=(V(-0.06, 0.26, -0.24), V(-0.4, 1, 0)), hand_R={'dir': V(0.6, 0.2, -0.7), 'blade': V(1, 0, 0.05)}), wings(-30, 10))
    dash = plus(merge(S(drop=0.1, lean=26), hips={'off': V(0.1, 0, -0.16), 'yaw': -10, 'lean': 22},
                      leg_R=(stance_foot('R', 0.4, 0, 0.1), V(1, 0, 0)), leg_L=(stance_foot('L', -0.46, 0, 0.22), V(1, 0, 0)),
                      arm_R=(V(0.06, 0.24, -0.26), V(-0.4, 1, 0)), hand_R={'dir': V(0.6, 0.2, -0.7), 'blade': V(1, 0, 0.05)}), wings(-55, -15))
    thrust = merge(S(drop=0.14, lean=20, yaw=-5, f=0.14), arm_R=(V(0.6, 0.02, -0.16), V(-0.2, 1, -0.3)), hand_R={'dir': V(1, 0, -0.1), 'blade': V(1, 0, 0)},
                   leg_R=(stance_foot('R', 0.46, 0.04), V(1, 0.2, 0)))
    for pre in ('intrepid_advance.', 'light_the_pyre.'):
        keys(pre + 'Windup', 0, 14, [(0, S()), (10, drawn), (14, drawn)])
        keys(pre + 'Dash', 0, 6, [(0, drawn), (3, dash), (6, dash)])
        keys(pre + 'Strike', 0, 16, [(0, dash), (2, thrust), (8, thrust), (16, S())])
    # Split Apart The Sky: sword high overhead in both hands, wings wide and raised, then cleaved straight down.
    sky = plus(merge(S(drop=-0.02, lean=-8, yaw=0), spine={'yaw': 0, 'lean': -10}, head={'yaw': 0, 'lean': -16},
                     arm_R=(V(0.02, 0.06, 0.42), V(0, 1, 0.2)), hand_R={'dir': V(0, 0, 1), 'blade': V(-0.3, 0, 0.95)},
                     arm_L=(V(0.04, -0.04, 0.36), V(0, -1, 0.2)), hand_L={'dir': V(0, 0, 1)}), wings(35, 30))
    cleave = plus(merge(S(drop=0.24, lean=28, yaw=0, f=0.08), head={'yaw': 0, 'lean': -26},
                        arm_R=(V(0.5, 0.04, -0.4), V(0, 1, -0.2)), hand_R={'dir': V(1, 0, -0.3), 'blade': V(0.6, 0, -0.8)},
                        arm_L=(V(0.44, 0.0, -0.36), V(0, -1, -0.2))), wings(-20, -20))
    keys('split_apart_the_sky.Windup', 0, 18, [(0, S()), (12, sky), (18, sky)])
    keys('split_apart_the_sky.Strike', 0, 20, [(0, sky), (3, cleave), (11, cleave), (20, S())])


PREVIEW = [('Idle', 0), ('Run', 0), ('Block', 6), ('spark_the_flame.Strike', 3), ('intrepid_advance.Dash', 6),
           ('intrepid_advance.Strike', 3), ('split_apart_the_sky.Windup', 18), ('split_apart_the_sky.Strike', 4),
           ('DashForward', 6), ('HitHeavy', 3), ('Stun', 0), ('Death', 24)]
