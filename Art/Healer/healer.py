# The Healer: a robed humanoid with a tall staff in the right hand, a ring at its head. Run by Art/common/build_unit.py.
# Clips: the shared set (shared Windup / Strike: the staff raised and brought down), plus deluge_of_creation (Deluge
# of Creation: the staff lifted high and held while it charges, then planted, the free hand spread) and repel (Repel:
# the staff drawn back, then swept round in a ring).
#
# The staff runs along the right fist's front, both ways (a long pole through the grip); 'blade' aims it.

GX, GZ = FIST['R']


def build_mesh():
    m = MeshBuilder()
    humanoid(m, w=0.9, skip=('thigh', 'torso'))
    def robe(s, k, X):
        m.box(X(0.0, 0.2), (-0.12, 0.12), (0.16, 0.9), 'thigh.' + s)                 # the robe's split skirt
        m.box(X(0.19, 0.35), (-0.075, 0.075), (0.88, 1.02), 'forearm.' + s)           # wide sleeves
    sym(robe)
    m.box((-0.2, 0.2), (-0.12, 0.12), (1.02, 1.44), 'spine')
    m.box((-0.22, 0.22), (-0.13, 0.13), (1.32, 1.46), 'spine')                         # mantle
    m.box((-0.05, 0.05), (-0.135, -0.125), (1.0, 1.3), 'spine', 'Gold')                # stole
    m.box((-0.12, 0.12), (-0.08, 0.13), (1.5, 1.76), 'head')                           # hood
    # A ring standing over the head (four bars and a gem).
    cy, cz, r, w = 0.04, 1.86, 0.11, 0.015
    m.box((-r, r), (cy - w, cy + w), (cz + r - w, cz + r + w), 'head', 'Gold')
    m.box((-r, r), (cy - w, cy + w), (cz - r - w, cz - r + w), 'head', 'Gold')
    m.box((r - w, r + w), (cy - w, cy + w), (cz - r, cz + r), 'head', 'Gold')
    m.box((-r - w, -r + w), (cy - w, cy + w), (cz - r, cz + r), 'head', 'Gold')
    # The staff: a long pole through the fist, a crook of slabs and a glowing gem at its head.
    m.taper(0.6, -1.0, (GX, GZ), 0.018, 0.018, 0.016, 0.016, 'hand.R', 'Gold')
    m.cbox((GX, -1.04, GZ), (0.03, 0.05, 0.03), 'hand.R', 'Metal')
    m.cbox((GX, -1.1, GZ + 0.07), (0.015, 0.02, 0.09), 'hand.R', 'Metal')
    m.cbox((GX, -1.18, GZ + 0.1), (0.015, 0.06, 0.015), 'hand.R', 'Metal')
    m.diamond((GX, -1.16, GZ), 0.045, 0.06, 'hand.R', 'Glow')
    return m.finish()


STAFF = {'arm.R': (V(0.16, 0.24, -0.4), V(-0.3, 1, 0)), 'hand.R': {'dir': V(1, 0, -0.1), 'blade': V(0.05, 0.05, 1)}}

def S(drop=0.0, lean=4, yaw=8, f=0.0):
    return plus({
        'hips': {'off': V(f, 0, -0.04 - drop), 'yaw': yaw, 'lean': 2},
        'spine': {'yaw': 0, 'lean': lean},
        'head': {'yaw': -yaw, 'lean': -lean},
        'leg.L': (stance_foot('L', 0.1 + f, -0.04), V(1, -0.2, 0)),
        'leg.R': (stance_foot('R', -0.1 + f, 0.04), V(1, 0.2, 0)),
        'arm.L': (V(0.2, -0.16, -0.36 - drop * 0.5), V(-0.4, -1, 0)), 'hand.L': {'dir': V(0.6, 0.2, -0.7)},
    }, STAFF)


def build_clips():
    keys('Idle', 0, 60, [(fr, S(drop=d)) for fr, d in ((0, 0), (20, 0.02), (40, 0), (60, 0))])
    run_cycle(S, lambda ph: plus({'arm.L': (V(0.06 + (0.1 if ph in (0, 1) else -0.08), -0.2, -0.36), V(-0.4, -1, 0))},
                                 {'arm.R': (V(0.18, 0.22, -0.34), V(-0.3, 1, 0)), 'hand.R': {'dir': V(1, 0, -0.1), 'blade': V(0.5, 0.05, 0.85)}}),
              stride=0.9, lean=12)
    dashes(S)
    reactions(S)
    guard = merge(S(drop=0.06, lean=10), arm_R=(V(0.3, 0.16, -0.12), V(-0.2, 1, -0.3)), hand_R={'dir': V(0.2, 0, 1), 'blade': V(0, -1, 0.15)},
                  arm_L=(V(0.3, -0.18, -0.1), V(-0.2, -1, -0.3)), hand_L={'dir': V(0.2, 0, 1)})
    keys('Block', 0, 6, [(0, S()), (3, guard), (6, guard)])
    raised = merge(S(drop=-0.02, lean=-6), arm_R=(V(0.12, 0.2, 0.26), V(-0.2, 1, 0.2)), hand_R={'dir': V(0.3, 0, 1), 'blade': V(0.25, 0, 1)},
                   arm_L=(V(0.2, -0.3, 0.0), V(0, -1, -0.3)), hand_L={'dir': V(0.2, -0.6, 0.8), 'fwd': V(1, 0, 0)}, head={'yaw': 0, 'lean': -16})
    planted = merge(S(drop=0.1, lean=14, f=0.04), arm_R=(V(0.36, 0.14, -0.26), V(-0.2, 1, -0.2)), hand_R={'dir': V(1, 0, 0), 'blade': V(0.2, 0, 1)},
                    arm_L=(V(0.3, -0.36, -0.1), V(0, -1, -0.3)), hand_L={'dir': V(0.4, -0.8, 0.2), 'fwd': V(0, 0, 1)})
    for pre in ('', 'deluge_of_creation.'):
        keys(pre + 'Windup', 0, 16, [(0, S()), (12, raised), (16, raised)])
        keys(pre + 'Strike', 0, 20, [(0, raised), (4, planted), (12, planted), (20, S())])
    # Repel: the staff drawn back over the shoulder, then swept round low.
    back = merge(S(drop=0.06, yaw=40), spine={'yaw': 20, 'lean': 4}, arm_R=(V(-0.06, 0.3, -0.1), V(-0.4, 1, 0)),
                 hand_R={'dir': V(-0.2, 0.4, 0.9), 'blade': V(-0.5, 0.6, 0.6)}, arm_L=(V(0.12, 0.1, -0.2), V(-0.2, -1, -0.3)))
    swept = merge(S(drop=0.14, yaw=-35, lean=16), spine={'yaw': -20, 'lean': 16}, arm_R=(V(0.4, -0.18, -0.3), V(-0.2, 1, -0.3)),
                  hand_R={'dir': V(0.8, -0.4, -0.2), 'blade': V(0.3, -0.95, -0.1)}, arm_L=(V(0.0, -0.3, -0.3), V(-0.2, -1, 0)))
    keys('repel.Windup', 0, 14, [(0, S()), (10, back), (14, back)])
    keys('repel.Strike', 0, 18, [(0, back), (3, swept), (9, swept), (18, S())])


PREVIEW = [('Idle', 0), ('Run', 0), ('Block', 6), ('deluge_of_creation.Windup', 16), ('deluge_of_creation.Strike', 6),
           ('repel.Windup', 14), ('repel.Strike', 4), ('DashSide', 6), ('HitHeavy', 3), ('Stun', 0), ('Death', 8), ('Death', 24)]
