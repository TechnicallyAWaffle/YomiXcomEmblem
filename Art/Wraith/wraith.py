# The Wraith: a smoky, shadowy humanoid with long claws, its shape never still. Its body is Dark (near black in the
# game), ragged tatters of smoke hang from it on bones of their own (EXTRA_BONES) that churn in every clip, and its
# eyes and claw tips glow. Run by Art/common/build_unit.py. Clips: the shared set, plus rend (Rend: claws raised, then
# a raking swipe), which the shared Windup / Strike copy.
import math as _m

EXTRA_BONES = {
    'wisp.1': (V(-0.08, 0, 1.4), V(-0.3, 0, 0.9), 'spine'),
    'wisp.2': (V(-0.06, -0.1, 0.95), V(-0.3, -0.14, 0.4), 'hips'),
    'wisp.3': (V(-0.06, 0.1, 0.95), V(-0.3, 0.14, 0.4), 'hips'),
    'wisp.4': (V(0, 0, 1.72), V(-0.22, 0, 1.92), 'head'),
}


def build_mesh():
    m = MeshBuilder()
    humanoid(m, w=0.8, mats={p: 'Dark' for p in ('thigh', 'shin', 'foot', 'upper_arm', 'forearm', 'hand', 'hips', 'torso', 'head')}, skip=('foot',))
    def b(f, r, u, hf, hr, hu, g, mat='Dark'):
        c = V(f, r, u); m.cbox((c.x, c.y, c.z), (hr, hf, hu), g, mat)
    def claws(s, k, X):
        r = -0.27 if s == 'L' else 0.27
        for dr in (-0.03, 0.0, 0.03):                                     # three long claws down past each hand
            b(0.03, r + dr, 0.66, 0.008, 0.008, 0.12, 'hand.' + s)
            b(0.03, r + dr, 0.53, 0.006, 0.006, 0.02, 'hand.' + s, 'Glow')
        b(0, -0.1 if s == 'L' else 0.1, 0.12, 0.05, 0.03, 0.06, 'shin.' + s)   # legs trailing off to wisps
    sym(claws)
    b(0.11, -0.04, 1.6, 0.01, 0.015, 0.012, 'head', 'Glow')
    b(0.11, 0.04, 1.6, 0.01, 0.015, 0.012, 'head', 'Glow')
    b(-0.02, 0, 1.74, 0.09, 0.1, 0.05, 'head')                            # hood point
    # Tatters of smoke, offset along each wisp bone, the faint ones Body (team colour) among the dark.
    for name, (f0, r0, u0, f1, r1, u1) in (('wisp.1', (-0.1, 0, 1.3, -0.3, 0, 0.9)), ('wisp.2', (-0.08, -0.1, 0.85, -0.3, -0.14, 0.4)),
                                          ('wisp.3', (-0.08, 0.1, 0.85, -0.3, 0.14, 0.4)), ('wisp.4', (-0.04, 0, 1.76, -0.2, 0, 1.9))):
        for i in range(3):
            k = (i + 0.5) / 3.0
            b(f0 + (f1 - f0) * k, r0 + (r1 - r0) * k, u0 + (u1 - u0) * k, 0.05 - 0.01 * i, 0.07 - 0.015 * i, 0.08, name, 'Body' if i == 1 else 'Dark')
    return m.finish()


def churn(t, amp=1.0):
    """The wisps at phase t (0..1): each swirls on its own beat."""
    s = lambda k, p: amp * k * _m.sin(2 * _m.pi * (t + p))
    return {'wisp.1': {'yaw': s(25, 0.0), 'lean': s(15, 0.3)}, 'wisp.2': {'yaw': s(30, 0.5), 'lean': s(18, 0.1)},
            'wisp.3': {'yaw': s(30, 0.2), 'lean': s(18, 0.7)}, 'wisp.4': {'yaw': s(35, 0.6), 'lean': s(20, 0.4)}}

def S(drop=0.0, lean=20, yaw=0, f=0.0, t=0.0):
    return plus({
        'hips': {'off': V(f, 0, -0.1 - drop), 'yaw': yaw, 'lean': 10},
        'spine': {'yaw': 4 * _m.sin(2 * _m.pi * t), 'lean': lean, 'tilt': 5 * _m.sin(2 * _m.pi * (t + 0.25))},
        'head': {'yaw': -6 * _m.sin(2 * _m.pi * t), 'lean': -lean - 6},
        'leg.L': (stance_foot('L', 0.08 + f, -0.04, 0.02), V(1, -0.2, 0)),
        'leg.R': (stance_foot('R', -0.08 + f, 0.04, 0.02), V(1, 0.2, 0)),
        'arm.L': (V(0.2, -0.3, -0.46 - drop * 0.5), V(-0.4, -1, 0)), 'hand.L': {'dir': V(0.6, -0.2, -0.8)},
        'arm.R': (V(0.2, 0.3, -0.46 - drop * 0.5), V(-0.4, 1, 0)), 'hand.R': {'dir': V(0.6, 0.2, -0.8)},
    }, churn(t))


def build_clips():
    fresh('Idle', 0, 60)
    for fr in range(0, 61, 10): key('Idle', fr, S(drop=0.03 * _m.sin(2 * _m.pi * fr / 60.0), t=fr / 60.0))
    run_cycle(lambda **k: S(**k), lambda ph: plus({'arm.L': (V(-0.1, -0.3, -0.42), V(-0.4, -1, 0)), 'hand.L': {'dir': V(-0.4, -0.2, -0.8)},
                                                   'arm.R': (V(-0.1, 0.3, -0.42), V(-0.4, 1, 0)), 'hand.R': {'dir': V(-0.4, 0.2, -0.8)}},
                                                  churn(ph / 4.0, 1.4)), stride=1.1, lean=28)
    dashes(lambda **k: S(**k), forward_arms=plus(upper(S()), churn(0.3, 1.6)))
    reactions(lambda **k: S(**k))
    guard = merge(S(drop=0.06), arm_L=(V(0.3, -0.02, -0.06), V(-0.2, -1, -0.3)), arm_R=(V(0.3, 0.02, -0.06), V(-0.2, 1, -0.3)),
                  hand_L={'dir': V(0, 0.3, 1)}, hand_R={'dir': V(0, -0.3, 1)})
    keys('Block', 0, 6, [(0, S()), (3, guard), (6, guard)])
    # Rend: both claws raised high and wide, then raked down across.
    raised = merge(S(drop=-0.02, lean=6, t=0.4), arm_L=(V(0.04, -0.36, 0.3), V(0, -1, 0)), hand_L={'dir': V(0.4, -0.2, 0.8)},
                   arm_R=(V(0.04, 0.36, 0.3), V(0, 1, 0)), hand_R={'dir': V(0.4, 0.2, 0.8)})
    rake = merge(S(drop=0.14, lean=34, f=0.1, t=0.6), arm_L=(V(0.42, 0.14, -0.42), V(-0.2, -1, 0)), hand_L={'dir': V(0.6, 0.6, -0.5)},
                 arm_R=(V(0.46, -0.1, -0.4), V(-0.2, 1, 0)), hand_R={'dir': V(0.6, -0.6, -0.5)}, leg_R=(stance_foot('R', 0.3, 0.04), V(1, 0.2, 0)))
    for pre in ('', 'rend.'):
        keys(pre + 'Windup', 0, 10, [(0, S()), (8, raised), (10, raised)])
        keys(pre + 'Strike', 0, 12, [(0, raised), (3, rake), (7, rake), (12, S())])


PREVIEW = [('Idle', 0), ('Idle', 30), ('Run', 0), ('Block', 6), ('rend.Windup', 10), ('rend.Strike', 3),
           ('DashSide', 6), ('HitHeavy', 3), ('Interrupted', 3), ('Stun', 0), ('Death', 8), ('Death', 24)]
