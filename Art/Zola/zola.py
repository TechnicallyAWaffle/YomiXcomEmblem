# Zola: a tall humanoid (a quarter taller than the squad: SKELETON) with a disproportionately large head, bound in
# metal bands, plates and bolts. Run by Art/common/build_unit.py. Clips: the shared set, plus stomp (Stomp: a knee
# raised high, then stamped down) and mental_overload (Mental Overload: both hands clutching the head as it hunches,
# then thrown back and wide as the pulse goes out).

K = 1.25   # height scale against the standard skeleton

def _s(p): return Vector((p.x, p.y, p.z * K))

SKELETON = {
    'hips': (V(0, 0, 0.9 * K), V(0, 0, 1.05 * K)), 'spine': (V(0, 0, 1.05 * K), V(0, 0, 1.45 * K)),
    'head': (V(0, 0, 1.45 * K), V(0, 0, 1.45 * K + 0.6)),
}
for s, x in (('L', 0.27), ('R', -0.27)):
    SKELETON['upper_arm.' + s] = (Vector((x, 0, 1.42 * K)), Vector((x, 0, 1.14 * K)))
    SKELETON['forearm.' + s] = (Vector((x, 0, 1.14 * K)), Vector((x, 0, 0.88 * K)))
    SKELETON['hand.' + s] = (Vector((x, 0, 0.88 * K)), Vector((x, 0, 0.78 * K)))
for s, x in (('L', 0.1), ('R', -0.1)):
    SKELETON['thigh.' + s] = (Vector((x, 0, 0.88 * K)), Vector((x, 0, 0.48 * K)))
    SKELETON['shin.' + s] = (Vector((x, 0, 0.48 * K)), Vector((x, 0, 0.08)))
    SKELETON['foot.' + s] = (Vector((x, 0, 0.08)), Vector((x, -0.15, 0.03)))


def build_mesh():
    m = MeshBuilder()
    def side(s, k, X):
        m.box(X(0.03, 0.17), (-0.075, 0.075), (0.48 * K, 0.88 * K), 'thigh.' + s)
        m.box(X(0.04, 0.16), (-0.065, 0.065), (0.1, 0.48 * K), 'shin.' + s)
        m.box(X(0.03, 0.17), (-0.2, 0.08), (0.0, 0.11), 'foot.' + s, 'Metal')
        m.box(X(0.21, 0.33), (-0.06, 0.06), (1.14 * K, 1.42 * K), 'upper_arm.' + s)
        m.box(X(0.215, 0.325), (-0.055, 0.055), (0.88 * K, 1.14 * K), 'forearm.' + s)
        m.box(X(0.205, 0.335), (-0.065, 0.065), (1.0 * K, 1.06 * K), 'forearm.' + s, 'Metal')
        m.box(X(0.22, 0.32), (-0.055, 0.055), (0.76 * K, 0.89 * K), 'hand.' + s)
    sym(side)
    m.box((-0.17, 0.17), (-0.1, 0.1), (0.86 * K, 1.06 * K), 'hips')
    m.box((-0.18, 0.18), (-0.11, 0.11), (1.04 * K, 1.44 * K), 'spine')
    m.box((-0.24, 0.24), (-0.12, 0.12), (1.36 * K, 1.46 * K), 'spine', 'Metal')               # collar holding the head up
    m.box((-0.06, 0.06), (-0.06, 0.06), (1.44 * K, 1.5 * K), 'head')                           # neck
    # The head: huge, banded and bolted.
    hz = 1.5 * K
    m.box((-0.27, 0.27), (-0.26, 0.24), (hz, hz + 0.56), 'head')
    m.box((-0.285, 0.285), (-0.275, 0.255), (hz + 0.1, hz + 0.15), 'head', 'Metal')            # bands
    m.box((-0.285, 0.285), (-0.275, 0.255), (hz + 0.36, hz + 0.41), 'head', 'Metal')
    m.box((-0.05, 0.05), (-0.275, 0.255), (hz + 0.1, hz + 0.58), 'head', 'Metal')              # a strap over the top
    m.box((-0.3, -0.25), (-0.12, 0.08), (hz + 0.18, hz + 0.34), 'head', 'Metal')               # temple plates
    m.box((0.25, 0.3), (-0.12, 0.08), (hz + 0.18, hz + 0.34), 'head', 'Metal')
    for x in (-0.33, 0.33):
        m.cbox((x, -0.02, hz + 0.26), (0.03, 0.03, 0.03), 'head', 'Metal')                     # bolts
    m.box((-0.17, -0.07), (-0.28, -0.26), (hz + 0.22, hz + 0.28), 'head', 'Glow')             # eyes, small for the head
    m.box((0.07, 0.17), (-0.28, -0.26), (hz + 0.22, hz + 0.28), 'head', 'Glow')
    m.box((-0.12, 0.12), (-0.28, -0.26), (hz + 0.04, hz + 0.08), 'head', 'Metal')              # jaw brace
    return m.finish()


def S(drop=0.0, lean=8, yaw=0, f=0.0):
    return {
        'hips': {'off': V(f, 0, -0.04 - drop), 'yaw': yaw, 'lean': 4},
        'spine': {'yaw': 0, 'lean': lean},
        'head': {'yaw': -yaw, 'lean': -lean + 4},
        'leg.L': (stance_foot('L', 0.06 + f, -0.08), V(1, -0.2, 0)),
        'leg.R': (stance_foot('R', -0.06 + f, 0.08), V(1, 0.2, 0)),
        'arm.L': (V(0.08, -0.32, -0.7 - drop * 0.5), V(-0.4, -1, 0)), 'hand.L': {'dir': V(0.2, -0.2, -1)},
        'arm.R': (V(0.08, 0.32, -0.7 - drop * 0.5), V(-0.4, 1, 0)), 'hand.R': {'dir': V(0.2, 0.2, -1)},
    }


def build_clips():
    keys('Idle', 0, 80, [(0, S()), (20, merge(S(drop=0.02), head={'yaw': 10, 'lean': -2, 'tilt': 4})), (40, S()),
                         (60, merge(S(drop=0.02), head={'yaw': -10, 'lean': -6, 'tilt': -4})), (80, S())])
    run_cycle(S, None, stride=0.9, lean=10, frames=20, name='Run')
    dashes(S)
    reactions(S)
    down = {'hips': {'off': V(-0.7, 0, -0.97), 'yaw': 0, 'lean': -86}, 'spine': {'lean': -4}, 'head': {'lean': -8, 'yaw': 25},
            'leg.L': (stance_foot('L', 0.2, -0.08, 0.0), V(0, 0, 1)), 'leg.R': (stance_foot('R', 0.14, 0.1, 0.06), V(0, 0, 1)),
            'arm.L': (V(0.02, -0.62, -0.06), V(-1, 0, -0.2)), 'arm.R': (V(-0.04, 0.6, -0.12), V(-1, 0, -0.2))}
    keys('Death', 0, 28, [(0, S()), (10, merge(S(drop=0.1), hips={'off': V(-0.14, 0, -0.3), 'yaw': 10, 'lean': -8}, head={'lean': -30})), (28, down)])
    guard = merge(S(drop=0.08, lean=14), arm_L=(V(0.36, -0.06, -0.12), V(-0.2, -1, -0.3)), arm_R=(V(0.36, 0.06, -0.12), V(-0.2, 1, -0.3)),
                  hand_L={'dir': V(0, 0.3, 1)}, hand_R={'dir': V(0, -0.3, 1)})
    keys('Block', 0, 6, [(0, S()), (3, guard), (6, guard)])
    # Stomp (and the shared strike): the right knee raised high, then stamped down, the body coming down with it.
    knee = merge(S(drop=-0.02, lean=-2), leg_R=(stance_foot('R', 0.16, 0.1, 0.5), V(1, 0.2, 0)),
                 arm_L=(V(0.1, -0.5, -0.4), V(0, -1, 0)), arm_R=(V(0.1, 0.5, -0.36), V(0, 1, 0)))
    stamp = merge(S(drop=0.18, lean=18), leg_R=(stance_foot('R', 0.24, 0.1), V(1, 0.2, 0)), head={'yaw': 0, 'lean': -6},
                  arm_L=(V(0.2, -0.42, -0.6), V(0, -1, 0)), arm_R=(V(0.2, 0.42, -0.6), V(0, 1, 0)))
    for pre in ('', 'stomp.'):
        keys(pre + 'Windup', 0, 16, [(0, S()), (12, knee), (16, knee)])
        keys(pre + 'Strike', 0, 20, [(0, knee), (3, stamp), (10, stamp), (20, S())])
    # Mental Overload: hands clutching the great head as it hunches over, then thrown back and wide.
    clutch = merge(S(drop=0.16, lean=26), head={'yaw': 0, 'lean': 14},
                   arm_L=(V(0.22, -0.34, 0.2), V(-0.3, -1, -0.4)), hand_L={'dir': V(0.2, 0.5, 0.8)},
                   arm_R=(V(0.22, 0.34, 0.2), V(-0.3, 1, -0.4)), hand_R={'dir': V(0.2, -0.5, 0.8)})
    burst = merge(S(drop=0.0, lean=-14), spine={'yaw': 0, 'lean': -16}, head={'yaw': 0, 'lean': -28},
                  arm_L=(V(-0.04, -0.62, 0.0), V(0, -0.3, -1)), hand_L={'dir': V(0, -1, 0.3)},
                  arm_R=(V(-0.04, 0.62, 0.0), V(0, 0.3, -1)), hand_R={'dir': V(0, 1, 0.3)})
    keys('mental_overload.Windup', 0, 44, [(0, S()), (14, clutch), (30, merge(clutch, head={'yaw': 8, 'lean': 18, 'tilt': 8})), (44, clutch)])
    keys('mental_overload.Strike', 0, 40, [(0, clutch), (4, burst), (28, burst), (40, S())])


PREVIEW_VIEW = {'ortho': 3.0, 'height': 1.5}
PREVIEW = [('Idle', 0), ('Run', 0), ('Block', 6), ('stomp.Windup', 16), ('stomp.Strike', 4), ('mental_overload.Windup', 14),
           ('mental_overload.Strike', 6), ('DashSide', 6), ('HitHeavy', 3), ('Stun', 0), ('Death', 10), ('Death', 28)]
