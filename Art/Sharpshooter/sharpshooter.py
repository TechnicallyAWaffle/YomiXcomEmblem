# The Sharpshooter: a hooded marksman in a short cape with a long scoped rifle. Run by Art/common/build_unit.py.
# Clips: the shared set (shared Windup / Strike: shoulder the rifle and fire), plus rifle_shot (Fire Rifle) and
# snipe (Precision Shot: drop to one knee, aim, a heavy recoil).
#
# The rifle runs along the right fist's front ('blade'); the left hand holds it further along. rifle() poses both
# arms from where the right hand grips and the way the barrel points, in the chest's frame.

GX, GZ = -0.27, 0.825      # right fist centre (Blender x, z) at rest
FORE = 0.36                 # right grip -> left grip, along the rifle


def build_mesh():
    m = MeshBuilder()
    def side(s, k, X):
        m.box(X(0.035, 0.165), (-0.07, 0.07), (0.48, 0.88), 'thigh.' + s)
        m.box(X(0.045, 0.155), (-0.06, 0.06), (0.1, 0.48), 'shin.' + s)
        m.box(X(0.04, 0.16), (-0.17, 0.07), (0.0, 0.11), 'foot.' + s)
        m.box(X(0.215, 0.325), (-0.055, 0.055), (1.14, 1.42), 'upper_arm.' + s)
        m.box(X(0.22, 0.32), (-0.05, 0.05), (0.88, 1.14), 'forearm.' + s)
        m.box(X(0.222, 0.318), (-0.052, 0.052), (0.775, 0.89), 'hand.' + s, 'Metal')   # gloves
    sym(side)
    m.box((-0.16, -0.04), (-0.085, -0.06), (0.42, 0.54), 'shin.R', 'Metal')              # knee pad (the kneeling knee)
    m.box((-0.16, 0.16), (-0.095, 0.095), (0.86, 1.06), 'hips')
    m.box((0.05, 0.17), (-0.13, -0.08), (0.86, 0.96), 'hips', 'Metal')                  # pouches
    m.box((-0.17, -0.05), (-0.13, -0.08), (0.86, 0.96), 'hips', 'Metal')
    m.box((-0.18, 0.18), (-0.1, 0.1), (1.04, 1.44), 'spine')
    m.box((-0.22, 0.22), (-0.12, 0.13), (1.32, 1.46), 'spine')                          # cape's shoulders
    m.box((-0.2, 0.2), (0.1, 0.135), (0.78, 1.44), 'spine')                             # cape
    m.box((-0.1, 0.1), (-0.12, 0.1), (1.46, 1.7), 'head')
    m.box((-0.135, 0.135), (-0.09, 0.15), (1.48, 1.79), 'head')                         # hood
    m.box((-0.09, 0.09), (-0.135, -0.115), (1.59, 1.64), 'head', 'Metal')                # goggles
    # Rifle along the right fist's front (-Y): stock behind the grip, receiver, long barrel, scope on top.
    m.box((GX - 0.025, GX + 0.025), (0.06, 0.3), (GZ - 0.06, GZ + 0.03), 'hand.R')           # stock
    m.box((GX - 0.03, GX + 0.03), (-0.32, 0.06), (GZ - 0.02, GZ + 0.06), 'hand.R', 'Metal')  # receiver
    m.box((GX - 0.015, GX + 0.015), (-0.06, 0.02), (GZ - 0.08, GZ - 0.02), 'hand.R', 'Metal') # trigger guard
    m.taper(-0.32, -0.98, (GX, GZ + 0.025), 0.013, 0.013, 0.011, 0.011, 'hand.R', 'Metal')     # barrel
    m.box((GX - 0.02, GX + 0.02), (-0.2, 0.02), (GZ + 0.08, GZ + 0.12), 'hand.R', 'Metal')    # scope
    m.box((GX - 0.01, GX + 0.01), (-0.12, -0.06), (GZ + 0.06, GZ + 0.08), 'hand.R', 'Metal')
    return m.finish()


def rifle(grip, aim, hand=V(0.2, 0, -1), lift=0.0):
    """Both arms on the rifle: right wrist at 'grip' (chest frame), barrel along 'aim' (chest frame)."""
    a = aim.normalized()
    return {'arm.R': (grip, V(-0.2, 1, -0.7)), 'arm.L': (grip + a * FORE + V(0, 0, lift), V(-0.2, -1, -0.7)),
            'hand.R': {'cdir': hand, 'cblade': a}, 'hand.L': {'cdir': hand, 'cblade': a}}

LOW_READY = rifle(V(0.12, 0.12, -0.34), V(0.75, -0.55, 0.2))

def S(drop=0.0, lean=6, yaw=20, f=0.0):
    return plus({
        'hips': {'off': V(f, 0, -0.06 - drop), 'yaw': yaw, 'lean': 3},
        'spine': {'yaw': 5, 'lean': lean},
        'head': {'yaw': -yaw - 5, 'lean': -lean},
        'leg.L': (stance_foot('L', 0.14 + f, -0.04), V(1, -0.2, 0)),
        'leg.R': (stance_foot('R', -0.14 + f, 0.04), V(1, 0.3, 0)),
    }, LOW_READY)


def aim_pose(drop=0.0):
    # Chest square-ish to the target (hips turned right, chest back), rifle at the shoulder, cheek down to the scope.
    return plus(merge(S(drop=drop + 0.04, yaw=35), hips={'off': V(0, 0, -0.1 - drop), 'yaw': 35, 'lean': 4},
                      spine={'yaw': -30, 'lean': 4}, head={'yaw': -2, 'lean': 6, 'tilt': 8}),
                rifle(V(0.14, 0.1, -0.1), V(1, 0, 0)))

def recoil_pose(drop=0.0, kick=1.0):
    p = aim_pose(drop)
    p.update(rifle(V(0.08, 0.12, -0.06), V(1, 0, 0.3 * kick)))
    p['hips'] = {'off': V(-0.04 * kick, 0, -0.1 - drop), 'yaw': 35, 'lean': 0}
    p['spine'] = {'yaw': -32, 'lean': -4 * kick}
    return p

def kneel(p):
    """The same upper body, down on the right knee with the left foot planted forward."""
    return merge(p, hips={'off': V(-0.02, 0, -0.42), 'yaw': 30, 'lean': 6},
                 leg_L=(stance_foot('L', 0.34, -0.06), V(1, -0.2, 0)),
                 leg_R=(stance_foot('R', -0.42, 0.06, 0.06), V(1, 0.2, -0.3)),
                 foot_R={'dir': V(-0.25, 0, -1)})


def build_clips():
    keys('Idle', 0, 60, [(fr, S(drop=d)) for fr, d in ((0, 0), (15, 0.02), (30, 0), (45, 0.02), (60, 0))])
    run_cycle(S, lambda ph: rifle(V(0.12, 0.1, -0.3), V(0.7, -0.6, 0.3)), stride=1.0, lean=18)
    dashes(S)
    reactions(S)
    guard = lambda d: plus(merge(S(drop=0.1 + d, lean=16, yaw=10), head={'yaw': -10, 'lean': -2}),
                           rifle(V(0.2, 0.2, -0.06), V(0, -1, 0.15), hand=V(0.3, 0, 1)))
    keys('Block', 0, 6, [(0, S()), (3, guard(0.03)), (6, guard(0.0))])

    for name in ('Windup', 'rifle_shot.Windup'):
        keys(name, 0, 12, [(0, S()), (7, aim_pose(0.02)), (12, aim_pose())])
    for name in ('Strike', 'rifle_shot.Strike'):
        keys(name, 0, 16, [(0, aim_pose()), (2, recoil_pose()), (8, aim_pose()), (16, S())])

    keys('snipe.Windup', 0, 30, [(0, S()), (10, kneel(aim_pose())), (30, kneel(aim_pose()))])
    keys('snipe.Strike', 0, 24, [(0, kneel(aim_pose())), (2, kneel(recoil_pose(kick=1.4))), (12, kneel(aim_pose())), (24, S())])


PREVIEW = [('Idle', 0), ('Run', 0), ('Run', 4), ('Block', 6), ('Windup', 12), ('Strike', 2),
           ('snipe.Windup', 30), ('snipe.Strike', 2), ('DashForward', 6), ('DashBack', 6), ('HitHeavy', 3), ('Stun', 0),
           ('Death', 8), ('Death', 24)]
