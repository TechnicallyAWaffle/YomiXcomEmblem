# The Forsaken, Composed form (the Diverged form is Art/ForsakenDiverged): a tall, gaunt dark figure floating half a metre
# off the ground, its legs hanging. Its face is a pale scribbled spiral; a cloak of shadow hangs from its shoulders, crowded
# with pale bird-skull masks, pale crests of bone over its shoulders, a glowing orb in its middle ringed by bone shards, a
# gold halo over its head, and long tendrils of smoke trailing from its back (their own bones, churning). Glitch streaks of
# the team's colour run down it. Run by Art/common/build_unit.py.
# Clips: the shared set (a glide rather than a run), plus divulge (a hand flung out), fray_the_mind (a thrust and recoil),
# wretched_visions (arms spread, head thrown back, then the release), simplicity (hands together, still) and neurosis
# (clutching its head as it tears apart). Abilities without their own clips use the shared Windup / Strike (a cast).
import math as _m
from mathutils import Vector

K, LIFT = 1.08, 0.4   # taller than the template, and floating: every rest point is scaled by K and raised LIFT


def P(f, r, u):
    return V(f, r, u * K + LIFT)


SKELETON = {
    'hips': (P(0, 0, 0.9), P(0, 0, 1.05)), 'spine': (P(0, 0, 1.05), P(0, 0, 1.45)), 'head': (P(0, 0, 1.45), P(0, 0, 1.75)),
    # Long arms: the hands hang to the knees.
    'upper_arm.L': (P(0, -0.25, 1.42), P(0, -0.26, 1.08)), 'forearm.L': (P(0, -0.26, 1.08), P(0, -0.26, 0.74)), 'hand.L': (P(0, -0.26, 0.74), P(0, -0.26, 0.6)),
    'upper_arm.R': (P(0, 0.25, 1.42), P(0, 0.26, 1.08)), 'forearm.R': (P(0, 0.26, 1.08), P(0, 0.26, 0.74)), 'hand.R': (P(0, 0.26, 0.74), P(0, 0.26, 0.6)),
    'thigh.L': (P(0, -0.09, 0.88), P(0, -0.09, 0.48)), 'shin.L': (P(0, -0.09, 0.48), P(0, -0.09, 0.08)), 'foot.L': (P(0, -0.09, 0.08), P(0.1, -0.09, 0.0)),
    'thigh.R': (P(0, 0.09, 0.88), P(0, 0.09, 0.48)), 'shin.R': (P(0, 0.09, 0.48), P(0, 0.09, 0.08)), 'foot.R': (P(0, 0.09, 0.08), P(0.1, 0.09, 0.0)),
}
EXTRA_BONES = {   # tendrils of smoke trailing from its back and the hem of its cloak
    'wisp.1': (P(-0.1, -0.12, 1.4), P(-0.55, -0.5, 1.75), 'spine'), 'wisp.2': (P(-0.1, 0.12, 1.4), P(-0.55, 0.5, 1.75), 'spine'),
    'wisp.3': (P(-0.08, -0.1, 0.95), P(-0.5, -0.35, 0.35), 'hips'), 'wisp.4': (P(-0.08, 0.1, 0.95), P(-0.5, 0.35, 0.35), 'hips'),
}


def build_mesh():
    m = MeshBuilder()
    def b(f, r, u, hf, hr, hu, g, mat='Dark'):
        c = P(f, r, u); m.cbox((c.x, c.y, c.z), (hr, hf, hu * K), g, mat)
    def gem(f, r, u, rad, h, g, mat):
        c = P(f, r, u); m.diamond((c.x, c.y, c.z), rad, h, g, mat)
    # Gaunt limbs, all shadow; the legs taper off into nothing.
    def limbs(s, k, X):
        r = -1 if s == 'L' else 1
        b(0, 0.255 * r, 1.25, 0.035, 0.035, 0.17, 'upper_arm.' + s)
        b(0, 0.26 * r, 0.91, 0.03, 0.03, 0.17, 'forearm.' + s)
        b(0.01, 0.26 * r, 0.68, 0.03, 0.025, 0.07, 'hand.' + s)
        for dr in (-0.018, 0.0, 0.018):                                    # long fingers
            b(0.02, 0.26 * r + dr, 0.56, 0.006, 0.006, 0.06, 'hand.' + s)
        b(0, 0.09 * r, 0.68, 0.04, 0.04, 0.2, 'thigh.' + s)
        b(0, 0.09 * r, 0.3, 0.03, 0.03, 0.19, 'shin.' + s)
        b(0.02, 0.09 * r, 0.06, 0.04, 0.02, 0.05, 'foot.' + s)
        b(0.04, 0.09 * r, 0.62, 0.004, 0.025, 0.025, 'thigh.' + s, 'Bone')     # a pale swirl mark on each thigh
        b(0.045, 0.09 * r, 0.62, 0.003, 0.01, 0.01, 'thigh.' + s, 'Dark')
        b(0.04, 0.26 * r, 0.95, 0.004, 0.018, 0.018, 'forearm.' + s, 'Bone')   # and on each forearm
        b(0.03, 0.25 * r, 1.42, 0.06, 0.07, 0.04, 'spine', 'Bone')            # bone crests over the shoulders...
        b(0.0, 0.31 * r, 1.48, 0.05, 0.035, 0.08, 'spine', 'Bone')            # ...rising like the skull-horns on its sides
        b(0.04, 0.31 * r, 1.47, 0.006, 0.015, 0.02, 'spine', 'Dark')          # an empty socket in each
        gem(0.0, 0.34 * r, 1.58, 0.03, 0.08, 'spine', 'Bone')
        b(0.02, 0.245 * r, 1.2, 0.004, 0.004, 0.2, 'upper_arm.' + s, 'Body')   # glitch streaks of colour down each arm
    sym(limbs)
    b(0, 0, 0.97, 0.08, 0.12, 0.08, 'hips')
    b(0, 0, 1.25, 0.09, 0.16, 0.2, 'spine')                                    # the narrow chest
    # The cloak: a hanging mass of shadow from the shoulders to the knees, flaring at the hem, ragged.
    b(-0.02, 0, 1.33, 0.13, 0.23, 0.12, 'spine')
    b(-0.03, 0, 1.08, 0.14, 0.21, 0.12, 'spine')
    b(-0.04, 0, 0.82, 0.15, 0.2, 0.16, 'hips')
    for i, rr in enumerate((-0.17, -0.06, 0.06, 0.17)):
        b(-0.04, rr, 0.6 - 0.05 * (i % 2), 0.12, 0.045, 0.08, 'hips')        # tattered hem
    b(0.14, -0.2, 1.0, 0.004, 0.004, 0.3, 'spine', 'Body')                       # glitch streaks of colour down the cloak
    b(0.14, 0.12, 1.15, 0.004, 0.004, 0.18, 'spine', 'Body')
    b(0.15, 0.04, 0.78, 0.004, 0.004, 0.16, 'hips', 'Body')
    # Bird-skull masks crowding the front of the cloak: a pale skull, two dark sockets, a long beak hooked down.
    def mask(f, r, u, sz, g):
        b(f, r, u, sz * 0.7, sz, sz, g, 'Bone')
        b(f + sz * 0.7, r - sz * 0.4, u + sz * 0.15, 0.004, sz * 0.22, sz * 0.22, g, 'Dark')
        b(f + sz * 0.7, r + sz * 0.4, u + sz * 0.15, 0.004, sz * 0.22, sz * 0.22, g, 'Dark')
        b(f + sz * 0.55, r, u - sz * 1.3, sz * 0.35, sz * 0.3, sz * 0.75, g, 'Bone')
        gem(f + sz * 0.55, r, u - sz * 2.1, sz * 0.25, sz * 0.4, g, 'Bone')
    for f, r, u, sz, g in ((0.14, -0.1, 1.38, 0.05, 'spine'), (0.15, 0.09, 1.32, 0.055, 'spine'), (0.15, -0.16, 1.17, 0.045, 'spine'),
                           (0.16, 0.16, 1.13, 0.05, 'spine'), (0.15, -0.02, 1.48, 0.04, 'spine'), (0.17, -0.09, 0.95, 0.04, 'hips'),
                           (0.17, 0.12, 0.88, 0.035, 'hips'), (0.12, 0.2, 1.4, 0.035, 'spine')):
        mask(f, r, u, sz, g)
    # The glowing orb at its middle, ringed by shards of bone.
    gem(0.19, 0, 1.03, 0.07, 0.08, 'spine', 'Glow')
    for a in range(6):
        t = a * _m.pi / 3.0
        gem(0.2, 0.12 * _m.cos(t), 1.03 + 0.11 * _m.sin(t), 0.02, 0.05, 'spine', 'Bone')
    # The head: hung forward, a pale scribbled spiral for a face (rings of light and dark), a dark hood behind.
    b(0, 0, 1.6, 0.1, 0.09, 0.12, 'head')
    b(-0.03, 0, 1.68, 0.1, 0.1, 0.07, 'head')
    b(0.1, 0, 1.6, 0.005, 0.075, 0.09, 'head', 'Bone')
    b(0.105, 0, 1.6, 0.005, 0.055, 0.067, 'head', 'Dark')
    b(0.11, 0, 1.6, 0.005, 0.036, 0.045, 'head', 'Bone')
    b(0.115, 0, 1.6, 0.005, 0.018, 0.022, 'head', 'Dark')
    b(0.12, 0.004, 1.602, 0.004, 0.007, 0.008, 'head', 'Bone')
    # The gold halo, hovering over its head: a ring of short gold bars.
    for a in range(18):
        t = a * 2 * _m.pi / 18.0
        c = P(0.3 * _m.cos(t) - 0.04, 0.3 * _m.sin(t), 1.98)
        m.cbox((c.x, c.y, c.z), (0.055 * abs(_m.sin(t)) + 0.012, 0.055 * abs(_m.cos(t)) + 0.012, 0.012), 'head', 'Gold')
    # Tendrils of smoke: solid strands, kinked twice, thinning to a point, a streak of colour in each.
    def strand(p0, p1, w0, w1, g, mat):
        axis = (p1 - p0).normalized()
        side = axis.cross(Vector((0, 0, 1)))
        if side.length < 1e-4: side = axis.cross(Vector((1, 0, 0)))
        side.normalize(); up = side.cross(axis).normalized()
        m.solid([(p1 if (i >> 2) & 1 else p0) + side * ((w1 if (i >> 2) & 1 else w0) * (1 if i & 1 else -1))
                 + up * ((w1 if (i >> 2) & 1 else w0) * (1 if (i >> 1) & 1 else -1)) for i in range(8)], g, mat)
    for name, (f0, r0, u0, f1, r1, u1) in (('wisp.1', (-0.1, -0.12, 1.4, -0.55, -0.5, 1.75)), ('wisp.2', (-0.1, 0.12, 1.4, -0.55, 0.5, 1.75)),
                                          ('wisp.3', (-0.08, -0.1, 0.95, -0.5, -0.35, 0.35)), ('wisp.4', (-0.08, 0.1, 0.95, -0.5, 0.35, 0.35))):
        pts = [P(f0 + (f1 - f0) * k + 0.04 * _m.sin(k * 7), r0 + (r1 - r0) * k, u0 + (u1 - u0) * k + 0.05 * _m.sin(k * 5)) for k in (0.0, 0.35, 0.7, 1.0)]
        widths = (0.04, 0.03, 0.02, 0.004)
        for i in range(3):
            strand(pts[i], pts[i + 1], widths[i], widths[i + 1], name, 'Body' if i == 1 else 'Dark')
    return m.finish()


def churn(t, amp=1.0):
    """The tendrils at phase t (0..1): each sways on its own beat."""
    s = lambda k, p: amp * k * _m.sin(2 * _m.pi * (t + p))
    return {'wisp.1': {'yaw': s(18, 0.0), 'lean': s(14, 0.3)}, 'wisp.2': {'yaw': s(18, 0.5), 'lean': s(14, 0.8)},
            'wisp.3': {'yaw': s(22, 0.2), 'lean': s(16, 0.6)}, 'wisp.4': {'yaw': s(22, 0.7), 'lean': s(16, 0.1)}}


def dangle(s, f=0.0, r=0.0, u=0.0):
    """A hanging foot: its rest place (in the air) moved by f / r / u."""
    return stance_foot(s, f, r, u)


def S(drop=0.0, lean=12, yaw=0, f=0.0, t=0.0, head=8):
    """Floating, slumped a little, head hung, arms hanging long, legs dangling; t: the drift's phase."""
    bob = 0.03 * _m.sin(2 * _m.pi * t)
    return plus({
        'hips': {'off': V(f, 0, bob - drop), 'yaw': yaw, 'lean': 4},
        'spine': {'yaw': 3 * _m.sin(2 * _m.pi * t), 'lean': lean, 'tilt': 3 * _m.sin(2 * _m.pi * (t + 0.25))},
        'head': {'yaw': -4 * _m.sin(2 * _m.pi * t), 'lean': head, 'tilt': 6},
        'leg.L': (dangle('L', -0.06 + f, -0.02, bob - drop + 0.02), V(1, -0.2, 0)), 'foot.L': {'dir': V(0.3, 0, -1)},
        'leg.R': (dangle('R', -0.1 + f, 0.02, bob - drop + 0.05), V(1, 0.2, 0)), 'foot.R': {'dir': V(0.3, 0, -1)},
        'arm.L': (V(0.08, -0.3, -0.72 - drop * 0.5), V(-0.3, -1, 0)), 'hand.L': {'dir': V(0.2, -0.1, -1)},
        'arm.R': (V(0.08, 0.3, -0.72 - drop * 0.5), V(-0.3, 1, 0)), 'hand.R': {'dir': V(0.2, 0.1, -1)},
    }, churn(t))


def glide(k, t):
    """Drifting along: leaning into it, legs and tendrils trailing."""
    return merge(plus(S(lean=20, f=0.0, t=t, head=-4), churn(t, 1.5)),
                 hips={'off': V(0, 0, 0.04 * k), 'yaw': 0, 'lean': 16},
                 leg_L=(dangle('L', -0.3, -0.02, 0.12 + 0.03 * k), V(1, -0.2, 0)), leg_R=(dangle('R', -0.34, 0.02, 0.14 - 0.03 * k), V(1, 0.2, 0)),
                 arm_L=(V(-0.12, -0.3, -0.66), V(-0.3, -1, 0)), arm_R=(V(-0.12, 0.3, -0.66), V(-0.3, 1, 0)))


def build_clips():
    fresh('Idle', 0, 60)
    for fr in range(0, 61, 10): key('Idle', fr, S(t=fr / 60.0))
    fresh('Run', 0, 20)
    for fr, k in ((0, 1), (5, 0), (10, -1), (15, 0), (20, 1)): key('Run', fr, glide(k, fr / 20.0))
    dashes(lambda **k: S(**k), forward_arms=plus(upper(S()), churn(0.3, 1.8)))
    reactions(lambda **k: S(**k))
    # Death: the float gives out; it drops and folds onto the ground, the halo with it.
    slump = merge(S(drop=0.1, lean=30, head=30), hips={'off': V(-0.05, 0, -0.2), 'yaw': 10, 'lean': 12})
    down = {'hips': {'off': V(-0.5, 0, -1.2), 'yaw': 10, 'lean': -80}, 'spine': {'lean': -6}, 'head': {'lean': -10, 'yaw': 30},
            'leg.L': (dangle('L', 0.3, -0.08, -0.5), V(0, 0, 1)), 'leg.R': (dangle('R', 0.24, 0.1, -0.45), V(0, 0, 1)),
            'arm.L': (V(0.02, -0.6, -0.1), V(-1, 0, -0.2)), 'arm.R': (V(-0.04, 0.6, -0.14), V(-1, 0, -0.2))}
    keys('Death', 0, 24, [(0, S()), (8, slump), (24, down)])
    guard = merge(S(drop=0.04, lean=20), arm_L=(V(0.3, -0.04, -0.1), V(-0.2, -1, -0.3)), arm_R=(V(0.3, 0.04, -0.1), V(-0.2, 1, -0.3)),
                  hand_L={'dir': V(0, 0.3, 1)}, hand_R={'dir': V(0, -0.3, 1)})
    keys('Block', 0, 6, [(0, S()), (3, guard), (6, guard)])
    # The shared cast (most Composed abilities): the right hand drawn back to the shoulder, then flung toward the target.
    draw = merge(S(lean=4, yaw=-25, head=0, t=0.3), arm_R=(V(-0.1, 0.32, -0.08), V(-0.6, 1, 0)), hand_R={'dir': V(0.2, 0.3, 1)},
                 arm_L=(V(0.2, -0.32, -0.5), V(-0.2, -1, 0)))
    fling = merge(S(lean=18, yaw=20, f=0.05, head=0, t=0.5), arm_R=(V(0.62, 0.12, -0.12), V(-0.2, 1, -0.3)), hand_R={'dir': V(1, 0, 0)},
                  arm_L=(V(-0.1, -0.32, -0.5), V(-0.2, -1, 0)))
    for pre in ('', 'divulge.'):
        keys(pre + 'Windup', 0, 12, [(0, S()), (10, draw), (12, draw)])
        keys(pre + 'Strike', 0, 14, [(0, draw), (3, fling), (8, fling), (14, S())])
    # Fray The Mind: both hands thrust out, and it is thrown back by its own blast.
    thrust = merge(S(lean=20, f=0.06, head=0), arm_L=(V(0.6, -0.08, -0.1), V(-0.2, -1, -0.3)), arm_R=(V(0.6, 0.08, -0.1), V(-0.2, 1, -0.3)),
                   hand_L={'dir': V(1, 0, 0.2)}, hand_R={'dir': V(1, 0, 0.2)})
    thrown = merge(plus(S(lean=-14, f=-0.1, head=-20), churn(0.2, 2.0)), arm_L=(V(0.3, -0.4, -0.2), V(-0.2, -1, 0)), arm_R=(V(0.3, 0.4, -0.2), V(-0.2, 1, 0)))
    keys('fray_the_mind.Windup', 0, 9, [(0, S()), (9, merge(thrust, hips={'off': V(-0.05, 0, 0), 'yaw': 0, 'lean': -4}))])
    keys('fray_the_mind.Strike', 0, 14, [(0, thrust), (3, thrown), (14, S())])
    # Wretched Visions: arms spread wide, head thrown back as the visions build; then it folds in and screams them out.
    spread = merge(plus(S(lean=-10, head=-35, drop=-0.06, t=0.7), churn(0.7, 2.2)), arm_L=(V(0.1, -0.7, 0.1), V(0, -1, 0)), arm_R=(V(0.1, 0.7, 0.1), V(0, 1, 0)),
                   hand_L={'dir': V(0, -1, 0.3)}, hand_R={'dir': V(0, 1, 0.3)})
    burst = merge(plus(S(lean=30, head=20, drop=0.06), churn(0.1, 2.5)), arm_L=(V(0.3, -0.5, -0.4), V(-0.2, -1, 0)), arm_R=(V(0.3, 0.5, -0.4), V(-0.2, 1, 0)))
    keys('wretched_visions.Windup', 0, 30, [(0, S()), (20, spread), (30, merge(spread, head={'lean': -45, 'yaw': 0, 'tilt': 10}))])
    keys('wretched_visions.Strike', 0, 16, [(0, spread), (3, burst), (10, burst), (16, S())])
    # Simplicity: hands brought together before it, head bowed, still.
    still = merge(S(lean=6, head=24), arm_L=(V(0.3, -0.03, -0.3), V(-0.3, -1, 0)), arm_R=(V(0.3, 0.03, -0.3), V(-0.3, 1, 0)),
                  hand_L={'dir': V(0, 0.3, 1)}, hand_R={'dir': V(0, -0.3, 1)})
    keys('simplicity.Windup', 0, 9, [(0, S()), (9, still)])
    keys('simplicity.Strike', 0, 12, [(0, still), (12, S())])
    # NEUROSIS: it clutches its head and convulses as the Diverged form tears out of it.
    clutch = lambda tw: merge(plus(S(lean=34, head=30, drop=0.05), churn(tw, 2.5)), hips={'off': V(0, 0, -0.05), 'yaw': 10 * _m.sin(tw * 9), 'lean': 10, 'tilt': 8 * _m.sin(tw * 7)},
                              arm_L=(V(0.12, -0.12, 0.12), V(-0.2, -1, 0.3)), arm_R=(V(0.12, 0.12, 0.12), V(-0.2, 1, 0.3)),
                              hand_L={'dir': V(0.3, 0.6, 0.6)}, hand_R={'dir': V(0.3, -0.6, 0.6)})
    fresh('neurosis.Windup', 0, 45)
    key('neurosis.Windup', 0, S())
    for i, fr in enumerate(range(6, 46, 6)): key('neurosis.Windup', fr, clutch(i * 0.37))


PREVIEW_VIEW = {'ortho': 3.0, 'height': 1.45}
PREVIEW = [('Idle', 0), ('Idle', 30), ('Run', 5), ('Block', 6), ('Windup', 12), ('Strike', 3),
           ('fray_the_mind.Strike', 3), ('wretched_visions.Windup', 30), ('simplicity.Windup', 9), ('neurosis.Windup', 24), ('HitHeavy', 3), ('Death', 24)]
