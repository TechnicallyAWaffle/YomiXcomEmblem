# The Strategist: a tall figure in a long coat and shoulder mantle with an angular crown, carrying the artifact - a
# long scepter topped with a gem - and three Makina shards floating at its back. Run by Art/common/build_unit.py.
# Clips: the shared set (a measured walk for Run), plus scepter_of_tyranny (Scepter of Tyranny: the scepter pointed and
# thrust forward) and one construct clip - scepter raised high, then planted - shared by the four Makina abilities
# (karraerms_hedron, monge_amperes_perfection, schalys_convextion, termouls_monolith).
#
# The scepter runs along the right fist's front ('blade' aims its head); its butt reaches 0.9m back from the grip.

GX, GZ = -0.27, 0.825
CONSTRUCTS = ('karraerms_hedron', 'monge_amperes_perfection', 'schalys_convextion', 'termouls_monolith')


def build_mesh():
    m = MeshBuilder()
    def side(s, k, X):
        m.box(X(0.035, 0.165), (-0.07, 0.07), (0.48, 0.88), 'thigh.' + s)
        m.box(X(0.045, 0.155), (-0.06, 0.06), (0.1, 0.48), 'shin.' + s)
        m.box(X(0.04, 0.16), (-0.17, 0.07), (0.0, 0.1), 'foot.' + s, 'Metal')
        m.box(X(0.02, 0.21), (0.07, 0.115), (0.22, 0.88), 'thigh.' + s)              # long coat, back
        m.box(X(0.04, 0.2), (-0.115, -0.07), (0.3, 0.88), 'thigh.' + s)              # long coat, front
        m.box(X(0.215, 0.325), (-0.055, 0.055), (1.14, 1.42), 'upper_arm.' + s)
        m.box(X(0.21, 0.33), (-0.06, 0.06), (0.88, 1.14), 'forearm.' + s)          # wide cuffs
        m.box(X(0.225, 0.315), (-0.05, 0.05), (0.775, 0.89), 'hand.' + s, 'Metal')
    sym(side)
    m.box((-0.17, 0.17), (-0.1, 0.1), (0.84, 1.06), 'hips')
    m.box((-0.18, 0.18), (-0.11, 0.11), (0.9, 0.95), 'hips', 'Metal')
    m.box((-0.19, 0.19), (-0.11, 0.11), (1.04, 1.44), 'spine')
    m.box((-0.31, 0.31), (-0.14, 0.14), (1.34, 1.48), 'spine')                         # mantle
    m.box((-0.24, 0.24), (0.11, 0.15), (1.0, 1.4), 'spine')                            # mantle, hanging at the back
    m.box((-0.03, 0.03), (-0.125, -0.11), (1.06, 1.34), 'spine', 'Metal')              # clasp line
    m.box((-0.11, 0.11), (-0.12, 0.1), (1.46, 1.71), 'head')
    m.box((-0.12, 0.12), (-0.125, 0.105), (1.68, 1.73), 'head', 'Metal')                # crown band
    for x, y, h in ((-0.09, -0.1, 0.12), (0.09, -0.1, 0.12), (0.0, -0.11, 0.17), (-0.09, 0.08, 0.09), (0.09, 0.08, 0.09)):
        m.box((x - 0.018, x + 0.018), (y - 0.015, y + 0.015), (1.72, 1.72 + h), 'head', 'Metal')
    # Floating Makina shards behind the shoulders and over the head (they move with the chest).
    m.diamond((0.34, 0.22, 1.72), 0.05, 0.1, 'spine')
    m.diamond((-0.34, 0.22, 1.72), 0.05, 0.1, 'spine')
    m.diamond((0.0, 0.3, 1.98), 0.06, 0.12, 'spine')
    # Scepter: butt cap, long shaft, collar, a gem head in a cage.
    m.cbox((GX, 0.9, GZ), (0.025, 0.02, 0.025), 'hand.R', 'Metal')
    m.taper(0.9, -0.78, (GX, GZ), 0.016, 0.016, 0.016, 0.016, 'hand.R', 'Metal')
    m.cbox((GX, -0.8, GZ), (0.04, 0.025, 0.04), 'hand.R', 'Metal')
    for dx, dz in ((0.05, 0), (-0.05, 0), (0, 0.05), (0, -0.05)):
        m.cbox((GX + dx, -0.92, GZ + dz), (0.01, 0.12, 0.01), 'hand.R', 'Metal')
    m.solid([Vector(c) for c in _gem(GX, -0.93, GZ)], 'hand.R')
    return m.finish()


def _gem(x, y, z):
    """A box gem turned 45 degrees about its long axis (the scepter's)."""
    r, l = 0.05, 0.1
    pts = []
    for i in range(8):
        a = (i & 1) * 2 - 1; b = ((i >> 2) & 1) * 2 - 1; far = (i >> 1) & 1
        u, w = a * r, b * r
        pts.append((x + (u - w) * 0.707, y - l if far else y + l, z + (u + w) * 0.707))
    return pts


STAFF = {'arm.R': (V(0.17, 0.14, -0.46), V(-0.3, 1, 0)), 'hand.R': {'dir': V(1, 0, -0.3), 'blade': V(0.04, 0, 1)}}
BEHIND = {'arm.L': (V(-0.16, -0.1, -0.5), V(0, -1, 0.3)), 'hand.L': {'dir': V(-0.2, 0.6, -1)}}

def S(drop=0.0, lean=2, yaw=10, f=0.0):
    return plus({
        'hips': {'off': V(f, 0, -0.03 - drop), 'yaw': yaw, 'lean': 1},
        'spine': {'yaw': -5, 'lean': lean},
        'head': {'yaw': -yaw + 5, 'lean': -lean - 3},
        'leg.L': (stance_foot('L', 0.08 + f, -0.03), V(1, -0.1, 0)),
        'leg.R': (stance_foot('R', -0.08 + f, 0.03), V(1, 0.1, 0)),
    }, plus(STAFF, BEHIND))


def build_clips():
    keys('Idle', 0, 80, [(0, S()), (20, S(drop=0.012)), (40, S()), (60, S(drop=0.012)), (80, S())])
    def arms(ph):
        sw = (0.07, 0.0, -0.07, 0.0)[ph]
        return plus(BEHIND, {'arm.R': (V(0.2 + sw, 0.14, -0.44), V(-0.3, 1, 0)),
                             'hand.R': {'dir': V(1, 0, -0.3), 'blade': V(0.25 + sw * 2, 0, 1)}})
    run_cycle(S, arms, stride=0.6, lean=6, lift=0.5, frames=20)
    dashes(S)
    reactions(S)
    ward = lambda d: merge(S(drop=0.08 + d, lean=8), head={'yaw': 0, 'lean': -4},
                           arm_R=(V(0.26, 0.2, -0.16), V(-0.2, 1, -0.6)), hand_R={'cdir': V(0.2, 0, -1), 'cblade': V(0.05, -1, 0.15)},
                           arm_L=(V(0.27, -0.22, -0.12), V(-0.2, -1, -0.6)), hand_L={'cdir': V(0.2, 0, -1)})
    keys('Block', 0, 6, [(0, S()), (3, ward(0.02)), (6, ward(0.0))])

    # Shared windup / strike and Scepter of Tyranny: the head lowered to point at the target, then thrust forward.
    aim = merge(S(drop=0.03, yaw=20, lean=4), hips={'off': V(-0.03, 0, -0.06), 'yaw': 20, 'lean': 2},
                arm_R=(V(0.3, 0.16, -0.08), V(0, 1, -0.5)), hand_R={'dir': V(0.3, 0, -1), 'blade': V(1, 0, 0.45)},
                arm_L=(V(0.1, -0.32, -0.32), V(0, -1, -0.2)), hand_L={'dir': V(0.4, -0.6, -1)})
    thrust = merge(S(drop=0.06, yaw=-10, lean=10), hips={'off': V(0.12, 0, -0.09), 'yaw': -10, 'lean': 6},
                   leg_L=(stance_foot('L', 0.28, -0.04), V(1, -0.1, 0)),
                   arm_R=(V(0.55, 0.1, 0.0), V(0, 1, -0.4)), hand_R={'dir': V(0.3, 0, -1), 'blade': V(1, -0.05, 0.15)},
                   arm_L=(V(-0.1, -0.36, -0.3), V(-1, -0.4, 0)))
    for p in ('', 'scepter_of_tyranny.'):
        keys(p + 'Windup', 0, 10, [(0, S()), (10, aim)])
        keys(p + 'Strike', 0, 16, [(0, aim), (3, thrust), (8, thrust), (16, S())])

    # The constructs: scepter raised straight up overhead, the left hand opened toward the spot, then planted hard.
    raise_ = merge(S(drop=-0.02, yaw=5, lean=-4), hips={'off': V(0, 0, -0.01), 'yaw': 5, 'lean': -2}, head={'yaw': 0, 'lean': -12},
                   arm_R=(V(0.06, 0.18, 0.42), V(0, 1, 0)), hand_R={'dir': V(0.3, 0, -1), 'blade': V(0.02, 0, 1)},
                   arm_L=(V(0.44, -0.18, -0.06), V(0, -1, -0.3)), hand_L={'dir': V(1, 0, 0.2)})
    plant = merge(S(drop=0.16, yaw=0, lean=16), hips={'off': V(0.08, 0, -0.18), 'yaw': 0, 'lean': 12}, head={'yaw': 0, 'lean': -16},
                  leg_L=(stance_foot('L', 0.24, -0.06), V(1, -0.2, 0)), leg_R=(stance_foot('R', -0.26, 0.06, 0.02), V(1, 0.2, 0)),
                  arm_R=(V(0.36, 0.12, -0.3), V(-0.2, 1, -0.3)), hand_R={'dir': V(1, 0, -0.3), 'blade': V(0.12, 0, 1)},
                  arm_L=(V(0.16, -0.5, -0.12), V(0, -1, -0.3)), hand_L={'dir': V(0.2, -1, 0.1)})
    keys('construct.Windup', 0, 18, [(0, S()), (12, raise_), (18, raise_)])
    keys('construct.Strike', 0, 26, [(0, raise_), (4, plant), (16, plant), (26, S())])
    for c in CONSTRUCTS:
        copy_action('construct.Windup', c + '.Windup')
        copy_action('construct.Strike', c + '.Strike')
    for n in ('construct.Windup', 'construct.Strike'):
        bpy.data.actions.remove(bpy.data.actions[n])


PREVIEW = [('Idle', 0), ('Run', 0), ('Run', 5), ('Block', 6), ('Windup', 10), ('Strike', 4),
           ('termouls_monolith.Windup', 18), ('termouls_monolith.Strike', 5), ('DashForward', 6), ('DashSide', 6),
           ('HitHeavy', 3), ('Stun', 0), ('Death', 8), ('Death', 24)]
