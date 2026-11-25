# The Sniper: a humanoid with a long sniper rifle, in a long coat and a visored helmet. Run by Art/common/build_unit.py.
# Clips: the shared set (shared Windup / Strike: shoulder the rifle and fire), plus sniper_rifle_shot and
# sniper_marking_shot (standing shots) and sniper_snipe (Snipe: down on one knee, a long aim, a heavy recoil).
# The rifle runs along the right fist's front; rifle() places both hands on it (as the Sharpshooter's).

GX, GZ = FIST['R']
FORE = 0.4


def build_mesh():
    m = MeshBuilder()
    humanoid(m, w=0.95, mats={'hand': 'Dark', 'foot': 'Dark'}, skip=('head',))
    def coat(s, k, X):
        m.box(X(0.02, 0.19), (0.07, 0.11), (0.46, 0.9), 'thigh.' + s)            # coat tails
        m.box(X(0.03, 0.18), (-0.105, -0.075), (0.5, 0.88), 'thigh.' + s)
    sym(coat)
    m.box((-0.2, 0.2), (-0.11, 0.11), (1.1, 1.44), 'spine')
    m.box((-0.12, -0.02), (-0.13, -0.1), (1.12, 1.34), 'spine', 'Metal')        # ammo pouches
    m.box((0.02, 0.12), (-0.13, -0.1), (1.12, 1.34), 'spine', 'Metal')
    m.box((-0.11, 0.11), (-0.12, 0.11), (1.46, 1.72), 'head', 'Metal')         # helmet
    m.box((-0.1, 0.1), (-0.14, -0.11), (1.56, 1.63), 'head', 'Glow')           # visor
    m.box((0.06, 0.11), (-0.02, 0.04), (1.7, 1.84), 'head', 'Metal')           # antenna
    # A long rifle: stock, receiver, bipod, a very long barrel with a brake, a big scope.
    m.box((GX - 0.026, GX + 0.026), (0.06, 0.32), (GZ - 0.07, GZ + 0.03), 'hand.R', 'Dark')
    m.box((GX - 0.032, GX + 0.032), (-0.36, 0.06), (GZ - 0.02, GZ + 0.065), 'hand.R', 'Metal')
    m.taper(-0.36, -1.22, (GX, GZ + 0.025), 0.014, 0.014, 0.012, 0.012, 'hand.R', 'Metal')
    m.cbox((GX, -1.22, GZ + 0.025), (0.024, 0.04, 0.024), 'hand.R', 'Metal')
    m.box((GX - 0.024, GX + 0.024), (-0.26, 0.02), (GZ + 0.085, GZ + 0.135), 'hand.R', 'Dark')
    m.box((GX - 0.03, GX + 0.03), (-0.3, -0.26), (GZ + 0.08, GZ + 0.14), 'hand.R', 'Glow')   # the scope's lens
    m.box((GX - 0.01, GX + 0.01), (-0.5, -0.46), (GZ - 0.18, GZ - 0.02), 'hand.R', 'Metal')   # folded bipod
    return m.finish()


def rifle(grip, aim, hand=V(0.2, 0, -1), lift=0.0):
    a = aim.normalized()
    return {'arm.R': (grip, V(-0.2, 1, -0.7)), 'arm.L': (grip + a * FORE + V(0, 0, lift), V(-0.2, -1, -0.7)),
            'hand.R': {'cdir': hand, 'cblade': a}, 'hand.L': {'cdir': hand, 'cblade': a}}

CARRY = rifle(V(0.1, 0.12, -0.36), V(0.7, -0.55, 0.45))

def S(drop=0.0, lean=4, yaw=20, f=0.0):
    return plus({
        'hips': {'off': V(f, 0, -0.05 - drop), 'yaw': yaw, 'lean': 2},
        'spine': {'yaw': 4, 'lean': lean},
        'head': {'yaw': -yaw - 4, 'lean': -lean},
        'leg.L': (stance_foot('L', 0.12 + f, -0.04), V(1, -0.2, 0)),
        'leg.R': (stance_foot('R', -0.12 + f, 0.04), V(1, 0.3, 0)),
    }, CARRY)

def aim_pose(drop=0.0):
    return plus(merge(S(drop=drop + 0.04, yaw=35), hips={'off': V(0, 0, -0.09 - drop), 'yaw': 35, 'lean': 4},
                      spine={'yaw': -30, 'lean': 4}, head={'yaw': -2, 'lean': 6, 'tilt': 8}),
                rifle(V(0.14, 0.1, -0.1), V(1, 0, 0)))

def recoil_pose(drop=0.0, kick=1.0):
    p = aim_pose(drop)
    p.update(rifle(V(0.06, 0.12, -0.06), V(1, 0, 0.35 * kick)))
    p['hips'] = {'off': V(-0.05 * kick, 0, -0.09 - drop), 'yaw': 35, 'lean': 0}
    p['spine'] = {'yaw': -32, 'lean': -5 * kick}
    return p

def kneel(p):
    return merge(p, hips={'off': V(-0.02, 0, -0.42), 'yaw': 30, 'lean': 6},
                 leg_L=(stance_foot('L', 0.34, -0.06), V(1, -0.2, 0)),
                 leg_R=(stance_foot('R', -0.42, 0.06, 0.06), V(1, 0.2, -0.3)), foot_R={'dir': V(-0.25, 0, -1)})


def build_clips():
    keys('Idle', 0, 60, [(fr, S(drop=d)) for fr, d in ((0, 0), (20, 0.02), (40, 0), (60, 0))])
    run_cycle(S, lambda ph: rifle(V(0.1, 0.1, -0.3), V(0.7, -0.6, 0.35)), stride=0.95, lean=16)
    dashes(S)
    reactions(S)
    guard = lambda d: plus(merge(S(drop=0.1 + d, lean=16, yaw=10), head={'yaw': -10, 'lean': -2}),
                           rifle(V(0.2, 0.2, -0.06), V(0, -1, 0.15), hand=V(0.3, 0, 1)))
    keys('Block', 0, 6, [(0, S()), (3, guard(0.03)), (6, guard(0.0))])
    for pre in ('', 'sniper_rifle_shot.', 'sniper_marking_shot.'):
        keys(pre + 'Windup', 0, 12, [(0, S()), (7, aim_pose(0.02)), (12, aim_pose())])
        keys(pre + 'Strike', 0, 16, [(0, aim_pose()), (2, recoil_pose()), (8, aim_pose()), (16, S())])
    keys('sniper_snipe.Windup', 0, 30, [(0, S()), (12, kneel(aim_pose())), (30, kneel(merge(aim_pose(), head={'yaw': -2, 'lean': 10, 'tilt': 10})))])
    keys('sniper_snipe.Strike', 0, 26, [(0, kneel(aim_pose())), (2, kneel(recoil_pose(kick=1.6))), (14, kneel(aim_pose())), (26, S())])


PREVIEW = [('Idle', 0), ('Run', 0), ('Block', 6), ('Windup', 12), ('Strike', 2), ('sniper_snipe.Windup', 30),
           ('sniper_snipe.Strike', 2), ('DashBack', 6), ('HitHeavy', 3), ('Stun', 0), ('Death', 8), ('Death', 24)]
