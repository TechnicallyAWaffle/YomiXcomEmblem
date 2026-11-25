# The Fighter's keyed poses (run after fighter_tools.py, inside Blender). Each action is rebuilt from scratch.
# Frames at 30 fps. Directions in the character's frame: V(forward, right, up).
# Ability clips are named <ability id>.<phase>: Windup, Dash (optional), Strike (the hit and its recovery).
import bpy
from mathutils import Vector

def fresh(name, start, end):
    old = bpy.data.actions.get(name)
    if old: bpy.data.actions.remove(old)
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    act.use_frame_range = True
    act.frame_start, act.frame_end = start, end
    rig = bpy.data.objects[RIG]
    rig.animation_data.action = None
    for pb in rig.pose.bones:
        pb.rotation_euler = (0, 0, 0); pb.location = (0, 0, 0); pb.scale = (1, 1, 1)
    return name

def mirror(spec):
    """The same pose, left for right."""
    def mv(v): return Vector((-v.x, v.y, v.z))
    out = {}
    for k, val in spec.items():
        k2 = k[:-1] + ('R' if k.endswith('L') else 'L') if k.endswith(('.L', '.R')) else k
        if isinstance(val, tuple): out[k2] = (mv(val[0]), mv(val[1]))
        else:
            d = dict(val)
            for a in ('yaw', 'tilt'):
                if a in d: d[a] = -d[a]
            for a in ('off', 'dir', 'fwd', 'blade'):
                if a in d: d[a] = mv(d[a])
            out[k2] = d
    return out

def merge(base, **over):
    s = dict(base)
    for k, v in over.items(): s[k.replace('_', '.')] = v
    return s

def guard(drop=0.0, lean=8, yaw=25, f=0.0):
    """The boxer stance: orthodox, left foot and fist forward, turned a little to the right, knees bent."""
    return {
        'hips': {'off': V(f, 0, -0.08 - drop), 'yaw': yaw, 'lean': 4},
        'spine': {'yaw': 5, 'lean': lean},
        'head': {'yaw': -yaw - 5, 'lean': -lean - 2},
        'leg.L': (stance_foot('L', 0.17 + f, -0.04), V(1, -0.2, 0)),
        'leg.R': (stance_foot('R', -0.2 + f, 0.06), V(1, 0.3, 0)),
        'arm.L': (V(0.24, -0.08, -0.06 - drop * 0.5), V(0.2, -0.25, -1)),
        'arm.R': (V(0.16, 0.06, -0.08 - drop * 0.5), V(0.2, 0.3, -1)),
    }

def idle():
    n = fresh('Idle', 0, 60)
    for fr, d in ((0, 0.0), (15, 0.025), (30, 0.0), (45, 0.025), (60, 0.0)):
        key(n, fr, guard(drop=d))

# ---- run: full tilt, leaning in, arms pumping close to the body
def run_contact():   # right foot reaching forward, left pushing off behind; left arm forward
    return {
        'hips': {'off': V(0.04, 0, -0.08), 'yaw': -8, 'lean': 18},
        'spine': {'yaw': 14, 'lean': 20},
        'head': {'yaw': -6, 'lean': -24},
        'leg.R': (stance_foot('R', 0.36, 0, 0.02), V(1, 0, 0)),
        'leg.L': (stance_foot('L', -0.42, 0, 0.24), V(1, 0, 0)),
        'arm.L': (V(0.26, -0.12, -0.2), V(-0.3, 0, -1)),
        'arm.R': (V(-0.2, 0.15, -0.34), V(-1, 0, 0.1)),
    }
def run_pass():      # right foot under the body, left knee driving high
    return {
        'hips': {'off': V(0.04, 0, -0.02), 'yaw': 0, 'lean': 18},
        'spine': {'yaw': 0, 'lean': 20},
        'head': {'yaw': 0, 'lean': -24},
        'leg.R': (stance_foot('R', -0.08, 0, 0.0), V(1, 0, 0)),
        'leg.L': (stance_foot('L', 0.12, 0, 0.44), V(1, 0, 0)),
        'arm.L': (V(0.06, -0.12, -0.3), V(-0.6, 0, -1)),
        'arm.R': (V(0.02, 0.14, -0.3), V(-0.6, 0, -1)),
    }
def run():
    n = fresh('Run', 0, 16)
    for fr, p in ((0, run_contact()), (4, run_pass()), (8, mirror(run_contact())), (12, mirror(run_pass())), (16, run_contact())):
        key(n, fr, p)

# ---- block: forearms crossed in an X in front of the face, braced
def block_pose(drop):
    return merge(guard(drop=drop, lean=14, yaw=10),
                 head={'yaw': -12, 'lean': -4},
                 arm_L=(V(0.27, 0.09, -0.02), V(0.3, -1, -0.7)),
                 arm_R=(V(0.24, -0.1, -0.06), V(0.3, 1, -0.7)))
def block():
    n = fresh('Block', 0, 6)
    key(n, 0, guard())
    key(n, 3, block_pose(0.1))
    key(n, 6, block_pose(0.07))

# ---- dashes (0.2 s: 6 frames). Forward: lean in and spring off the back foot.
def dash_fwd_pose(air, arms=None):
    p = {
        'hips': {'off': V(0.08, 0, -0.06 + 0.08 * air), 'yaw': 5, 'lean': 22},
        'spine': {'yaw': 5, 'lean': 16},
        'head': {'yaw': -8, 'lean': -30},
        'leg.R': (stance_foot('R', -0.5 + 0.06 * air, 0, 0.12 + 0.18 * air), V(1, 0, 0)),
        'leg.L': (stance_foot('L', 0.26, 0, 0.3 + 0.12 * air), V(1, 0, 0)),
        'arm.L': (V(0.28, -0.1, -0.18), V(-0.2, -0.2, -1)),
        'arm.R': (V(-0.26, 0.12, -0.36), V(-1, 0.2, 0)),
    }
    if arms: p.update(arms)
    return p
def dash_forward():
    n = fresh('DashForward', 0, 6)
    key(n, 0, guard(drop=0.06, lean=18))
    key(n, 2, dash_fwd_pose(0.0))
    key(n, 6, dash_fwd_pose(1.0))

def dash_back():   # kick off the front foot, guard up
    n = fresh('DashBack', 0, 6)
    key(n, 0, guard(drop=0.06))
    for fr, air in ((2, 0.0), (6, 1.0)):
        key(n, fr, merge(guard(lean=-2, yaw=15),
            hips={'off': V(-0.08, 0, -0.08 + 0.1 * air), 'yaw': 15, 'lean': -6},
            leg_L=(stance_foot('L', 0.46 - 0.1 * air, -0.02, 0.0 + 0.16 * air), V(1, 0, 0)),
            leg_R=(stance_foot('R', -0.22, 0.04, 0.3 - 0.05 * air), V(1, 0, 0))))

def dash_side():   # to the character's right: kick off the left foot, leaning into it
    n = fresh('DashSide', 0, 6)
    key(n, 0, guard(drop=0.06))
    for fr, air in ((2, 0.0), (6, 1.0)):
        key(n, fr, merge(guard(yaw=15),
            hips={'off': V(0, 0.1, -0.08 + 0.1 * air), 'yaw': 15, 'lean': 6, 'tilt': 16},
            spine={'yaw': 5, 'lean': 8, 'tilt': 6},
            head={'yaw': -20, 'lean': -8, 'tilt': -14},
            leg_L=(stance_foot('L', 0.05, -0.5 + 0.12 * air, 0.0 + 0.2 * air), V(1, -0.3, 0)),
            leg_R=(stance_foot('R', -0.05, 0.28, 0.26), V(1, 0.3, 0))))

# ---- the placeholder for abilities without clips of their own: cock the right fist, then a straight right
def windup():
    n = fresh('Windup', 0, 15)
    key(n, 0, guard())
    key(n, 15, merge(guard(drop=0.04, yaw=40),
        hips={'off': V(-0.05, 0, -0.12), 'yaw': 40, 'lean': 2},
        arm_R=(V(-0.04, 0.12, -0.06), V(-0.3, 1, -0.6))))
def strike():
    n = fresh('Strike', 0, 12)
    punch = merge(guard(yaw=-10),
        hips={'off': V(0.1, 0, -0.12), 'yaw': -10, 'lean': 8},
        spine={'yaw': -10, 'lean': 10},
        head={'yaw': 20, 'lean': -12},
        arm_R=(V(0.58, -0.04, -0.06), V(0, 1, -0.4)))
    key(n, 0, merge(guard(drop=0.04, yaw=40), hips={'off': V(-0.05, 0, -0.12), 'yaw': 40, 'lean': 2},
                    arm_R=(V(-0.04, 0.12, -0.06), V(-0.3, 1, -0.6))))
    key(n, 3, punch)
    key(n, 6, punch)
    key(n, 12, guard())

# ---- Rend The Flesh (slash): a curved sword appears in the right hand, raised over the shoulder, cut down diagonally
SWORD_UP = {'hand.R': {'dir': V(-0.1, 0.25, 1), 'blade': V(-0.6, 0.45, 0.65)}}
def slash_raised():
    p = merge(guard(drop=0.04, yaw=40),
        hips={'off': V(-0.06, 0, -0.12), 'yaw': 40, 'lean': -2},
        spine={'yaw': 10, 'lean': -4},
        head={'yaw': -45, 'lean': 0},
        arm_R=(V(-0.02, 0.18, 0.22), V(0, 1, 0.1)),
        arm_L=(V(0.32, -0.16, -0.14), V(0, -1, -0.6)))
    p.update(SWORD_UP)
    return p
def slash_low():
    return merge(guard(yaw=-28),
        hips={'off': V(0.12, 0, -0.2), 'yaw': -28, 'lean': 16},
        spine={'yaw': -16, 'lean': 16},
        head={'yaw': 40, 'lean': -20},
        leg_L=(stance_foot('L', 0.36, -0.12), V(1, -0.3, 0)),
        leg_R=(stance_foot('R', -0.34, 0.12, 0.04), V(1, 0.3, 0)),
        arm_R=(V(0.36, -0.32, -0.56), V(0, 1, 0)),
        arm_L=(V(-0.1, -0.3, -0.3), V(-1, -0.3, -0.2)),
        hand_R={'dir': V(0.45, -0.55, -0.7), 'blade': V(0.25, -1, -0.35)})
def slash_windup():
    n = fresh('slash.Windup', 0, 10)
    key(n, 0, guard(), {'sword': 0.0})
    key(n, 3, merge(slash_raised(), hips={'off': V(-0.03, 0, -0.1), 'yaw': 30, 'lean': 0}), {'sword': 1.15})
    key(n, 6, slash_raised(), {'sword': 1.0})
    key(n, 10, slash_raised(), {'sword': 1.0})
def slash_strike():
    n = fresh('slash.Strike', 0, 18)
    mid = merge(guard(yaw=5),
        hips={'off': V(0.06, 0, -0.16), 'yaw': 5, 'lean': 10},
        spine={'yaw': 0, 'lean': 10},
        head={'yaw': -5, 'lean': -14},
        arm_R=(V(0.42, 0.02, -0.12), V(0, 1, -0.2)),
        hand_R={'dir': V(1, -0.1, 0.1), 'blade': V(0.35, -0.75, -0.55)})
    key(n, 0, slash_raised(), {'sword': 1.0})
    key(n, 2, mid, {'sword': 1.0})
    key(n, 4, slash_low(), {'sword': 1.0})
    key(n, 11, slash_low(), {'sword': 1.0})
    key(n, 14, merge(guard(drop=0.04)), {'sword': 0.9})
    key(n, 17, guard(), {'sword': 0.0})
    key(n, 18, guard(), {'sword': 0.0})

# ---- Phantom Strike: two daggers, a low crouch with arms crossed; the dash; an X cut down and out
DAGGERS = {'dagger.L': 1.0, 'dagger.R': 1.0}
def ps_crouch():
    return merge(guard(drop=0.14, lean=18, yaw=5),
        hips={'off': V(0.02, 0, -0.24), 'yaw': 5, 'lean': 22},
        head={'yaw': -5, 'lean': -34},
        leg_L=(stance_foot('L', 0.2, -0.06), V(1, -0.2, 0)),
        leg_R=(stance_foot('R', -0.28, 0.06, 0.02), V(1, 0.2, 0)),
        arm_R=(V(0.1, -0.2, -0.06), V(1, 0.2, -1)),
        arm_L=(V(0.14, 0.2, -0.03), V(1, -0.2, -1)),
        hand_R={'dir': V(1, -0.2, 0), 'blade': V(-0.4, -0.3, 0.85)},
        hand_L={'dir': V(1, 0.2, 0), 'blade': V(-0.4, 0.3, 0.85)})
CROSSED = lambda: {k: v for k, v in ps_crouch().items() if k in ('arm.L', 'arm.R', 'hand.L', 'hand.R')}
def ps_windup():
    n = fresh('phantom_strike.Windup', 0, 8)
    key(n, 0, guard(), {})
    key(n, 3, ps_crouch(), {'dagger.L': 1.15, 'dagger.R': 1.15})
    key(n, 5, ps_crouch(), DAGGERS)
    key(n, 8, ps_crouch(), DAGGERS)
def ps_dash():
    n = fresh('phantom_strike.Dash', 0, 6)
    key(n, 0, ps_crouch(), DAGGERS)
    key(n, 2, dash_fwd_pose(0.0, CROSSED()), DAGGERS)
    key(n, 6, dash_fwd_pose(1.0, CROSSED()), DAGGERS)
def ps_strike():
    n = fresh('phantom_strike.Strike', 0, 16)
    out = merge(guard(drop=0.12, lean=16, yaw=0),
        hips={'off': V(0.06, 0, -0.22), 'yaw': 0, 'lean': 16},
        head={'yaw': 0, 'lean': -26},
        leg_L=(stance_foot('L', 0.24, -0.1), V(1, -0.3, 0)),
        leg_R=(stance_foot('R', -0.26, 0.1, 0.02), V(1, 0.3, 0)),
        arm_R=(V(0.22, 0.42, -0.56), V(-0.3, 1, 0)),
        arm_L=(V(0.22, -0.42, -0.56), V(-0.3, -1, 0)),
        hand_R={'dir': V(1, 0, 0), 'blade': V(0.35, 0.85, -0.4)},
        hand_L={'dir': V(1, 0, 0), 'blade': V(0.35, -0.85, -0.4)})
    key(n, 0, dash_fwd_pose(1.0, CROSSED()), DAGGERS)
    key(n, 3, out, DAGGERS)
    key(n, 10, out, DAGGERS)
    key(n, 13, guard(drop=0.04), {'dagger.L': 0.9, 'dagger.R': 0.9})
    key(n, 15, guard(), {})
    key(n, 16, guard(), {})

# ---- reactions
def hit_light():
    n = fresh('HitLight', 0, 8)
    key(n, 0, guard())
    key(n, 2, merge(guard(lean=-6, yaw=30), hips={'off': V(-0.06, 0, -0.08), 'yaw': 30, 'lean': -6}, head={'yaw': -30, 'lean': -18}))
    key(n, 8, guard())
def hit_heavy():
    n = fresh('HitHeavy', 0, 14)
    key(n, 0, guard())
    recoil = merge(guard(lean=-16, yaw=35),
        hips={'off': V(-0.14, 0, -0.06), 'yaw': 35, 'lean': -12},
        head={'yaw': -20, 'lean': -22},
        arm_L=(V(0.0, -0.36, -0.28), V(-0.3, -1, -0.3)),
        arm_R=(V(-0.06, 0.36, -0.3), V(-0.3, 1, -0.3)))
    key(n, 3, recoil)
    key(n, 8, merge(guard(drop=0.06, lean=10), hips={'off': V(-0.08, 0, -0.14), 'yaw': 25, 'lean': 8}))
    key(n, 14, guard())
def interrupted():
    n = fresh('Interrupted', 0, 10)
    key(n, 0, guard())
    key(n, 3, merge(guard(lean=-4), hips={'off': V(-0.04, 0.05, -0.08), 'yaw': 25, 'lean': -4, 'tilt': 12},
                    arm_L=(V(0.12, -0.3, -0.35), V(0, -1, -0.5)), arm_R=(V(0.06, 0.3, -0.36), V(0, 1, -0.5))))
    key(n, 6, merge(guard(), hips={'off': V(0, -0.04, -0.08), 'yaw': 25, 'lean': 2, 'tilt': -8}))
    key(n, 10, guard())
def stun_pose(tilt):
    return merge(guard(drop=0.08, lean=24, yaw=15),
        hips={'off': V(0, 0, -0.16), 'yaw': 15, 'lean': 14, 'tilt': tilt},
        head={'yaw': -10, 'lean': 24},
        arm_L=(V(0.06, -0.08, -0.5), V(-0.2, -0.3, -1)),
        arm_R=(V(0.02, 0.08, -0.52), V(-0.2, 0.3, -1)))
def stun():
    n = fresh('Stun', 0, 30)
    for fr, t in ((0, 5), (15, -5), (30, 5)):
        key(n, fr, stun_pose(t))

def death_hide_weapons():
    """Death keeps its earlier keys; the weapons just stay hidden in it."""
    rig = bpy.data.objects[RIG]
    act = bpy.data.actions['Death']
    rig.animation_data.action = act
    for fr in (int(act.frame_range[0]), int(act.frame_range[1])):
        for w in WEAPONS:
            rig.pose.bones[w].scale = (0, 0, 0)
            rig.pose.bones[w].keyframe_insert('scale', frame=fr)

POSES = [idle, run, block, dash_forward, dash_back, dash_side, windup, strike, slash_windup, slash_strike,
         ps_windup, ps_dash, ps_strike, hit_light, hit_heavy, interrupted, stun, death_hide_weapons]

def build_all():
    for old in ('Walk', 'Dash'):
        a = bpy.data.actions.get(old)
        if a: bpy.data.actions.remove(a)
    for p in POSES: p()
    bpy.data.objects[RIG].animation_data.action = bpy.data.actions['Idle']
