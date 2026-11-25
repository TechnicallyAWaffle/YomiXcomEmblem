# The Skirmisher (the Eviscerim): a light, quick humanoid with a short blade in each hand, a scarf and light plates.
# No look was asked for; this one is read from its kit (Flowing Death's chained cuts, Mental Currents' evasion).
# Run by Art/common/build_unit.py. Clips: the shared set (shared Windup / Strike: a crossing double cut, its basic
# Strike), plus flowing_death (Flowing Death: wound low, then a spinning cut with both blades).

def build_mesh():
    m = MeshBuilder()
    humanoid(m, w=0.85, mats={'foot': 'Dark', 'hand': 'Dark'})
    def plates(s, k, X):
        m.box(X(0.2, 0.34), (-0.065, 0.065), (0.95, 1.08), 'forearm.' + s, 'Metal')
        m.box(X(0.03, 0.17), (-0.09, -0.065), (0.52, 0.66), 'thigh.' + s, 'Metal')
    sym(plates)
    m.box((-0.13, 0.13), (-0.12, -0.1), (1.16, 1.4), 'spine', 'Metal')               # chest plate
    m.box((-0.13, 0.13), (-0.12, 0.12), (1.38, 1.46), 'spine', 'Dark')                # scarf
    m.box((0.04, 0.12), (0.1, 0.13), (1.02, 1.42), 'spine', 'Dark')                   # its tail down the back
    m.box((-0.1, 0.1), (-0.125, -0.1), (1.5, 1.56), 'head', 'Dark')                   # face wrap
    m.box((-0.11, 0.11), (-0.02, 0.12), (1.6, 1.74), 'head')                          # tied-back hair
    held_blade(m, 'R', 0.4, width=0.022, grip=0.12, guard=0.04)
    held_blade(m, 'L', 0.4, width=0.022, grip=0.12, guard=0.04)
    return m.finish()


def S(drop=0.0, lean=12, yaw=-10, f=0.0):
    return {
        'hips': {'off': V(f, 0, -0.12 - drop), 'yaw': yaw, 'lean': 8},
        'spine': {'yaw': 0, 'lean': lean},
        'head': {'yaw': -yaw, 'lean': -lean - 4},
        'leg.R': (stance_foot('R', 0.16 + f, 0.08), V(1, 0.3, 0)),
        'leg.L': (stance_foot('L', -0.16 + f, -0.1), V(1, -0.3, 0)),
        'arm.R': (V(0.3, 0.18, -0.32 - drop * 0.5), V(-0.2, 1, -0.5)), 'hand.R': {'dir': V(0.4, 0.2, -1), 'blade': V(1, -0.1, 0.35)},
        'arm.L': (V(0.2, -0.22, -0.36 - drop * 0.5), V(-0.2, -1, -0.5)), 'hand.L': {'dir': V(0.4, -0.2, -1), 'blade': V(0.8, 0.3, -0.1)},
    }


def build_clips():
    keys('Idle', 0, 40, [(fr, S(drop=d)) for fr, d in ((0, 0), (10, 0.025), (20, 0), (30, 0.025), (40, 0))])
    def arms(ph):
        a = 0.12 if ph in (0, 1) else -0.08
        return {'arm.R': (V(0.06 - a, 0.2, -0.36), V(-0.4, 1, -0.3)), 'hand.R': {'dir': V(0.4, 0.2, -1), 'blade': V(-0.6, 0, -0.4)},
                'arm.L': (V(0.06 + a, -0.2, -0.36), V(-0.4, -1, -0.3)), 'hand.L': {'dir': V(0.4, -0.2, -1), 'blade': V(-0.6, 0, -0.4)}}
    run_cycle(S, arms, stride=1.15, lean=24)
    dashes(S)
    reactions(S)
    cross = merge(S(drop=0.06), arm_R=(V(0.3, 0.02, -0.04), V(-0.2, 1, -0.4)), hand_R={'dir': V(0, -0.5, 1), 'blade': V(0.3, -0.7, 0.6)},
                  arm_L=(V(0.3, -0.02, -0.06), V(-0.2, -1, -0.4)), hand_L={'dir': V(0, 0.5, 1), 'blade': V(0.3, 0.7, 0.6)})
    keys('Block', 0, 6, [(0, S()), (3, cross), (6, cross)])
    # A crossing double cut: both blades raised out wide, then scissored across in front.
    wide = merge(S(drop=0.04, lean=6), arm_R=(V(0.1, 0.4, 0.0), V(-0.3, 1, -0.2)), hand_R={'dir': V(0, 0.6, 0.8), 'blade': V(0.4, 0.4, 0.8)},
                 arm_L=(V(0.1, -0.4, 0.0), V(-0.3, -1, -0.2)), hand_L={'dir': V(0, -0.6, 0.8), 'blade': V(0.4, -0.4, 0.8)})
    scissor = merge(S(drop=0.1, lean=20, f=0.1), arm_R=(V(0.46, -0.16, -0.32), V(-0.2, 1, -0.3)), hand_R={'dir': V(1, -0.4, -0.3), 'blade': V(0.5, -0.8, -0.3)},
                    arm_L=(V(0.46, 0.16, -0.28), V(-0.2, -1, -0.3)), hand_L={'dir': V(1, 0.4, -0.3), 'blade': V(0.5, 0.8, -0.3)},
                    leg_R=(stance_foot('R', 0.4, 0.08), V(1, 0.3, 0)))
    for pre in ('', 'skirmisher_strike.'):
        keys(pre + 'Windup', 0, 8, [(0, S()), (6, wide), (8, wide)])
        keys(pre + 'Strike', 0, 12, [(0, wide), (2, scissor), (6, scissor), (12, S())])
    # Flowing Death: wound up low and twisted, then a full spin with both blades out.
    wound = merge(S(drop=0.2, lean=26, yaw=50), spine={'yaw': 30, 'lean': 20},
                  arm_R=(V(-0.1, 0.34, -0.28), V(-0.4, 1, 0)), hand_R={'dir': V(-0.4, 0.4, -0.8), 'blade': V(-0.6, 0.6, -0.3)},
                  arm_L=(V(0.3, 0.1, -0.3), V(-0.2, -1, -0.3)), hand_L={'dir': V(0.6, 0.3, -0.6), 'blade': V(0.2, 0.9, -0.2)})
    spin = lambda y: merge(S(drop=0.12, lean=10, yaw=y), spine={'yaw': 0, 'lean': 6}, head={'yaw': 0, 'lean': -10},
                           arm_R=(V(0.0, 0.56, -0.08), V(0, 0.3, -1)), hand_R={'dir': V(0, 1, 0), 'blade': V(0.3, 0.9, 0.1)},
                           arm_L=(V(0.0, -0.56, -0.08), V(0, -0.3, -1)), hand_L={'dir': V(0, -1, 0), 'blade': V(-0.3, -0.9, 0.1)})
    keys('flowing_death.Windup', 0, 14, [(0, S()), (10, wound), (14, wound)])
    keys('flowing_death.Strike', 0, 16, [(0, wound), (2, spin(0)), (4, spin(-90)), (6, spin(-180)), (8, spin(-270)), (10, spin(-350)), (16, S())])


PREVIEW = [('Idle', 0), ('Run', 0), ('Block', 6), ('Windup', 8), ('Strike', 2), ('flowing_death.Windup', 14),
           ('flowing_death.Strike', 2), ('flowing_death.Strike', 6), ('DashForward', 6), ('HitHeavy', 3), ('Stun', 0), ('Death', 24)]
