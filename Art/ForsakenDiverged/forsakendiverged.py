# The Forsaken, Diverged form (UnitDefinition.DivergedModel; the Composed form is Art/Forsaken): a floating larva-thing.
# A body of stacked pale segments, seamed with the team's colour, bound round and round by bands of black; eyes everywhere
# (pale, ringed with a glowing iris), some on stalks; a crown of black spines and two black fans spreading from it; four
# long black tentacle arms ending in claws; ribbons of colour trailing; and below, a pale tail twisting away to nothing.
# Its own skeleton (SKELETON): hips and spine are the body, the head its crown; the arms are the upper pair of tentacles and
# the legs the lower pair; the tail, the crown's fans and the ribbons are bones of their own (EXTRA_BONES).
# Run by Art/common/build_unit.py. Clips: the shared set (built here for a floating body, as the Matriarch's), plus the
# shared Windup / Strike (arms rear up, then slam forward: Devour and most of its abilities), rampage (a lunge),
# unleash (arms flung up, then spread in a burst; also Howling End's release), berserk (thrashing) and regression (curling in).
import math as _m

SKELETON = {
    'hips': (V(0, 0, 0.95), V(0, 0, 1.25)), 'spine': (V(0, 0, 1.25), V(0, 0, 1.85)), 'head': (V(0, 0, 1.85), V(0, 0, 2.15)),
    'upper_arm.L': (V(0.02, -0.36, 1.68), V(0.12, -0.6, 1.38)), 'forearm.L': (V(0.12, -0.6, 1.38), V(0.22, -0.74, 1.04)), 'hand.L': (V(0.22, -0.74, 1.04), V(0.27, -0.78, 0.84)),
    'upper_arm.R': (V(0.02, 0.36, 1.68), V(0.12, 0.6, 1.38)), 'forearm.R': (V(0.12, 0.6, 1.38), V(0.22, 0.74, 1.04)), 'hand.R': (V(0.22, 0.74, 1.04), V(0.27, 0.78, 0.84)),
    'thigh.L': (V(0.0, -0.3, 1.18), V(0.08, -0.52, 0.92)), 'shin.L': (V(0.08, -0.52, 0.92), V(0.16, -0.62, 0.6)), 'foot.L': (V(0.16, -0.62, 0.6), V(0.2, -0.65, 0.42)),
    'thigh.R': (V(0.0, 0.3, 1.18), V(0.08, 0.52, 0.92)), 'shin.R': (V(0.08, 0.52, 0.92), V(0.16, 0.62, 0.6)), 'foot.R': (V(0.16, 0.62, 0.6), V(0.2, 0.65, 0.42)),
}
EXTRA_BONES = {
    'tail.1': (V(0, 0, 0.95), V(0.02, 0, 0.68), 'hips'), 'tail.2': (V(0.02, 0, 0.68), V(-0.02, 0, 0.42), 'tail.1'),
    'crown.L': (V(-0.02, -0.12, 2.02), V(-0.04, -0.72, 2.16), 'head'), 'crown.R': (V(-0.02, 0.12, 2.02), V(-0.04, 0.72, 2.16), 'head'),
    'ribbon.L': (V(-0.06, -0.34, 1.62), V(-0.3, -0.6, 1.0), 'spine'), 'ribbon.R': (V(-0.06, 0.34, 1.62), V(-0.3, 0.6, 1.0), 'spine'),
}

# Rest points along each bone (character frame), for the mesh.
_BONES = {n: (h, t) for n, (h, t) in list(SKELETON.items()) + [(k, (v[0], v[1])) for k, v in EXTRA_BONES.items()]}


def build_mesh():
    m = MeshBuilder()

    def b(f, r, u, hf, hr, hu, g, mat='Bone'):
        c = V(f, r, u); m.cbox((c.x, c.y, c.z), (hr, hf, hu), g, mat)

    def slat(p0, p1, w, d, g, mat, roll=0.0, w1=None, d1=None):
        """A block from p0 to p1 (Blender coordinates), w x d across (tapering to w1 x d1), turned 'roll' round its length."""
        axis = (p1 - p0).normalized()
        side = axis.cross(Vector((0, 0, 1)))
        if side.length < 1e-4: side = axis.cross(Vector((1, 0, 0)))
        side.normalize()
        up = side.cross(axis).normalized()
        if roll:
            c, s = _m.cos(roll), _m.sin(roll)
            side, up = side * c + up * s, up * c - side * s
        w1 = w if w1 is None else w1; d1 = d if d1 is None else d1
        pts = []
        for i in range(8):
            far = (i >> 2) & 1
            ww, dd = (w1, d1) if far else (w, d)
            p = p1 if far else p0
            pts.append(p + side * (ww if i & 1 else -ww) + up * (dd if (i >> 1) & 1 else -dd))
        m.solid(pts, g, mat)

    def along(bone, k):
        h, t = _BONES[bone]
        return h + (t - h) * k

    def eye(f, r, u, size, g, look=V(1, 0, 0)):
        """A pale eye, a glowing iris and a black pupil, looking 'look' (mostly forward or out to a side)."""
        size *= 1.5
        b(f, r, u, size, size, size, g, 'Eye')
        lf, lr = look.copy(), None
        dirf, dirr = (-look.y, -look.x)   # back to (f, r)
        n = _m.hypot(dirf, dirr) or 1.0
        dirf, dirr = dirf / n, dirr / n
        b(f + dirf * size, r + dirr * size, u, size * 0.06 + abs(dirr) * size * 0.75, size * 0.06 + abs(dirf) * size * 0.75, size * 0.75, g, 'Glow')   # the iris, wide
        b(f + dirf * size * 1.1, r + dirr * size * 1.1, u, size * 0.06 + abs(dirr) * size * 0.3, size * 0.06 + abs(dirf) * size * 0.3, size * 0.3, g, 'Dark')

    # ---- the body: stacked pale segments, widest in the middle, seamed with colour
    segs = [(1.0, 0.2, 'hips'), (1.15, 0.27, 'hips'), (1.31, 0.33, 'spine'), (1.48, 0.36, 'spine'), (1.65, 0.33, 'spine'), (1.8, 0.26, 'spine')]
    for u, w, g in segs:
        b(0, 0, u, w * 0.82, w, 0.072, g)                 # a cross of two blocks: a rounded-looking ring
        b(0, 0, u, w, w * 0.8, 0.068, g)
        b(w * 0.84, 0, u - 0.07, 0.006, w * 0.6, 0.006, g, 'Body')   # the seam below it, team's colour
    # ---- black bands wound round it (a helix of short slabs, three turns)
    for band, (u0, rise) in enumerate(((1.0, 0.32), (1.35, 0.3), (1.6, 0.28))):
        for i in range(14):
            a = i * 2 * _m.pi / 14.0 + band * 1.3
            u = u0 + rise * i / 14.0
            w = max(s[1] for s in segs if abs(s[0] - u) < 0.12) if any(abs(s[0] - u) < 0.12 for s in segs) else 0.3
            rad = w * 1.02 + 0.012
            p0 = V(_m.cos(a) * rad, _m.sin(a) * rad, u)
            a2 = (i + 1) * 2 * _m.pi / 14.0 + band * 1.3
            p1 = V(_m.cos(a2) * rad, _m.sin(a2) * rad, u + rise / 14.0)
            slat(p0, p1, 0.03, 0.035, 'hips' if u < 1.25 else 'spine', 'Dark')
    # ---- eyes: across the body and big on the crown
    for f, r, u, sz, g, look in ((0.33, -0.12, 1.5, 0.05, 'spine', V(1, 0, 0)), (0.3, 0.16, 1.62, 0.04, 'spine', V(1, 0, 0)),
                                 (0.28, 0.05, 1.33, 0.035, 'spine', V(1, 0, 0)), (0.15, -0.3, 1.47, 0.04, 'spine', V(0.3, -1, 0)),
                                 (0.12, 0.31, 1.4, 0.035, 'spine', V(0.3, 1, 0)), (0.2, -0.15, 1.12, 0.03, 'hips', V(1, 0, 0)),
                                 (0.24, 0.06, 1.85, 0.06, 'head', V(1, 0, 0)), (0.2, -0.16, 1.95, 0.045, 'head', V(1, -0.4, 0)),
                                 (0.2, 0.17, 1.93, 0.04, 'head', V(1, 0.4, 0))):
        eye(f, r, u, sz, g, look)
    # eyes on stalks, ringed, rising from its upper body
    for f, r, u, hgt, sz in ((0.1, -0.28, 1.8, 0.25, 0.045), (0.12, 0.3, 1.78, 0.3, 0.05), (-0.05, -0.1, 1.9, 0.32, 0.04), (0.02, 0.15, 1.92, 0.22, 0.035)):
        slat(V(f, r, u), V(f + 0.03, r * 1.4, u + hgt), 0.008, 0.008, 'head', 'Dark')
        eye(f + 0.03, r * 1.4, u + hgt + sz, sz, 'head', V(1, 0, 0))
        b(f + 0.03, r * 1.4, u + hgt + sz, sz * 1.5, sz * 1.5, 0.005, 'head', 'Body')     # a ring of colour round it
    # ---- the crown: black head, a fan of black spines straight up, two black fans spreading out to the sides
    b(0, 0, 1.97, 0.18, 0.2, 0.1, 'head', 'Dark')
    for i in range(7):
        a = (i - 3) * 0.24
        p0 = V(-0.02, _m.sin(a) * 0.12, 2.04)
        slat(p0, p0 + V(0, _m.sin(a) * 0.4, _m.cos(a) * 0.36), 0.025, 0.04, 'head', 'Dark', w1=0.012, d1=0.03)
    for s in ('L', 'R'):
        sg = -1 if s == 'L' else 1
        h, t = _BONES['crown.' + s]
        slat(h, t, 0.035, 0.035, 'crown.' + s, 'Dark')
        for i in range(6):   # bars fanning out from the spar, from level to steeply up, longest in the middle
            a = _m.radians(-10 + 14 * i)
            base = h + (t - h) * (0.25 + 0.12 * i)
            ln = 0.32 + 0.1 * _m.sin(i / 5.0 * _m.pi)
            tip = base + V(0, sg * _m.cos(a) * ln * 0.55, _m.sin(a) * ln + 0.12)
            slat(base, tip, 0.035, 0.055, 'crown.' + s, 'Dark', w1=0.022, d1=0.05)
    # ---- four tentacle arms, black, tapering, three claws on each
    def arm(s, k, X):
        for top, mid, low in (('upper_arm.', 'forearm.', 'hand.'), ('thigh.', 'shin.', 'foot.')):
            for bone, w0, w1 in ((top, 0.045, 0.036), (mid, 0.036, 0.028), (low, 0.028, 0.02)):
                h, t = _BONES[bone + s]
                slat(h, t, w0, w0, bone + s, 'Dark', w1=w1, d1=w1)
            h, t = _BONES[low + s]
            d = (t - h).normalized()
            sgn = -1 if s == 'L' else 1
            for c in (-1, 0, 1):
                tip = t + d * 0.13 + V(0.06 * c, sgn * 0.03, 0)
                slat(t, tip, 0.012, 0.012, low + s, 'Dark', w1=0.004, d1=0.004)
                b(*(lambda p: (-p.y, -p.x, p.z))(tip), 0.008, 0.008, 0.008, low + s, 'Bone')
    sym(arm)
    # ---- ribbons of colour, trailing
    for s in ('L', 'R'):
        h, t = _BONES['ribbon.' + s]
        mid = h + (t - h) * 0.5 + V(0.04, 0, 0)
        slat(h, mid, 0.07, 0.008, 'ribbon.' + s, 'Body', roll=0.3)
        slat(mid, t, 0.065, 0.008, 'ribbon.' + s, 'Body', roll=-0.4, w1=0.09)
    # ---- the tail: pale, twisting, thinning to a wisp
    for bone, w0, w1, rl in (('tail.1', 0.15, 0.09, 0.5), ('tail.2', 0.09, 0.025, 1.1)):
        h, t = _BONES[bone]
        for i in range(3):
            k0, k1 = i / 3.0, (i + 1) / 3.0
            ww0 = w0 + (w1 - w0) * k0; ww1 = w0 + (w1 - w0) * k1
            slat(h + (t - h) * k0, h + (t - h) * k1, ww0, ww0 * 0.8, bone, 'Bone', roll=rl * (i + 1), w1=ww1, d1=ww1 * 0.8)
    return m.finish()


ARM = {'L': V(0.27, -0.78, -1.01), 'R': V(0.27, 0.78, -1.01)}   # an upper tentacle's wrist at rest, from the spine's top


def M(up=0.0, fwd=0.0, pitch=0.0, yaw=0.0, tilt=0.0, spine=0.0, head=0.0, hy=0.0,
      al=(0, 0, 0), ar=(0, 0, 0), ll=(0, 0, 0), lr=(0, 0, 0), tail=(0.0, 0.0), crown=0.0, rib=0.0, t=0.0):
    """A floating pose. al / ar: the upper tentacles' wrists moved from rest (with the body); ll / lr: the lower ones
    (they follow the body's height); tail: (lean, yaw) of its curl; crown: the fans raised (+, degrees); rib: the
    ribbons swung back; t: phase for the writhing."""
    w = lambda k, p: k * _m.sin(2 * _m.pi * (t + p))
    # held out from the body, curling up (al / ar / ll / lr are on top of this)
    al = (al[0] + 0.05, al[1] - 0.12, al[2] + 0.38); ar = (ar[0] + 0.05, ar[1] + 0.12, ar[2] + 0.38)
    ll = (ll[0] + 0.08, ll[1] - 0.18, ll[2] + 0.28); lr = (lr[0] + 0.08, lr[1] + 0.18, lr[2] + 0.28)
    return {'hips': {'off': V(fwd, 0, up), 'yaw': yaw, 'lean': pitch, 'tilt': tilt},
            'spine': {'lean': spine + w(3, 0.2), 'yaw': w(4, 0.0)}, 'head': {'lean': head, 'yaw': hy + w(5, 0.5)},
            'arm.L': (ARM['L'] + V(*al) + V(w(0.04, 0.1), 0, w(0.05, 0.3)), V(-0.3, -1, 0.2)),
            'arm.R': (ARM['R'] + V(*ar) + V(w(0.04, 0.6), 0, w(0.05, 0.8)), V(-0.3, 1, 0.2)),
            'leg.L': (stance_foot('L', ll[0] + fwd, ll[1], ll[2] + up + w(0.04, 0.4)), V(-0.4, -1, 0)),
            'leg.R': (stance_foot('R', lr[0] + fwd, lr[1], lr[2] + up + w(0.04, 0.9)), V(-0.4, 1, 0)),
            'hand.L': {'dir': V(0.5, -0.4, 0.2)}, 'hand.R': {'dir': V(0.5, 0.4, 0.2)},
            'foot.L': {'dir': V(0.5, -0.5, -0.2)}, 'foot.R': {'dir': V(0.5, 0.5, -0.2)},
            'tail.1': {'lean': tail[0] + w(8, 0.0), 'yaw': tail[1] + w(14, 0.25)}, 'tail.2': {'lean': tail[0] * 0.6 + w(10, 0.5), 'yaw': tail[1] + w(20, 0.75)},
            'crown.L': {'tilt': -crown + w(4, 0.3)}, 'crown.R': {'tilt': crown - w(4, 0.3)},
            'ribbon.L': {'lean': -rib + w(10, 0.1), 'yaw': w(8, 0.4)}, 'ribbon.R': {'lean': -rib + w(10, 0.6), 'yaw': w(8, 0.9)}}


def build_clips():
    fresh('Idle', 0, 60)
    for fr in range(0, 61, 10):
        t = fr / 60.0
        key('Idle', fr, M(up=0.05 * _m.sin(2 * _m.pi * t), t=t))
    glide = lambda k, t: M(up=0.04 * k, pitch=16, spine=6, head=-10, al=(-0.25, -0.06, 0.15 + 0.05 * k), ar=(-0.25, 0.06, 0.15 - 0.05 * k),
                           ll=(-0.35, -0.04, 0.15 - 0.05 * k), lr=(-0.35, 0.04, 0.15 + 0.05 * k), tail=(-30, 0), crown=-10, rib=35, t=t)
    fresh('Run', 0, 20)
    for fr, k in ((0, 1), (5, 0), (10, -1), (15, 0), (20, 1)): key('Run', fr, glide(k, fr / 20.0))
    jerk = lambda d: M(up=0.05, fwd=0.12 * d, pitch=16 * d, al=(-0.22 * d, 0, 0.12), ar=(-0.22 * d, 0, 0.12), ll=(-0.25 * d, 0, 0.14), lr=(-0.25 * d, 0, 0.14),
                       tail=(-30 * d, 0), rib=40 * d)
    keys('DashForward', 0, 6, [(0, M()), (2, jerk(1)), (6, jerk(1))])
    keys('DashBack', 0, 6, [(0, M()), (2, jerk(-1)), (6, jerk(-1))])
    side = M(up=0.05, tilt=18, al=(0, -0.22, 0.12), ar=(0, -0.22, 0.08), ll=(0, -0.22, 0.1), lr=(0, -0.22, 0.08), tail=(0, 30))
    keys('DashSide', 0, 6, [(0, M()), (2, side), (6, side)])
    cross = M(up=-0.04, pitch=8, head=6, al=(0.3, 0.55, 0.45), ar=(0.3, -0.55, 0.45), ll=(0.25, 0.3, 0.3), lr=(0.25, -0.3, 0.3), crown=-15)
    keys('Block', 0, 6, [(0, M()), (3, cross), (6, cross)])
    keys('HitLight', 0, 8, [(0, M()), (2, M(up=0.02, fwd=-0.06, pitch=-8, tilt=6, al=(0.08, 0, 0.06), ar=(0.08, 0, 0.06), tail=(15, 10))), (8, M())])
    keys('HitHeavy', 0, 14, [(0, M()), (3, M(up=0.08, fwd=-0.18, pitch=-20, yaw=15, al=(0.2, -0.1, 0.25), ar=(0.2, 0.1, 0.25),
                                             ll=(0.15, 0, 0.12), lr=(0.15, 0, 0.12), tail=(30, -20), crown=20)),
                             (8, M(up=-0.08, fwd=-0.06, pitch=4)), (14, M())])
    keys('Interrupted', 0, 10, [(0, M()), (3, M(tilt=14, yaw=20, al=(0, -0.12, 0.12), ar=(0, 0.12, 0.12), tail=(0, 25))), (6, M(tilt=-10, yaw=-8)), (10, M())])
    sag = lambda tl: M(up=-0.2, pitch=12, tilt=tl, spine=6, head=10, al=(0.04, 0, -0.1), ar=(0.04, 0, -0.1), ll=(0.03, 0, -0.08), lr=(0.03, 0, -0.08), tail=(10, 0), crown=-20)
    keys('Stun', 0, 30, [(0, sag(8)), (15, sag(-8)), (30, sag(8))])
    # Death: it sinks, tips over, and lies on the ground, its arms splayed.
    keys('Death', 0, 30, [(0, M()), (8, M(up=-0.25, pitch=10, tilt=12)),
                          (30, M(up=-0.85, pitch=10, tilt=70, spine=8, head=14, al=(0.3, -0.35, 0.6), ar=(0.4, 0.35, 0.5),
                                 ll=(-0.3, -0.35, 0.3), lr=(-0.2, 0.4, 0.2), tail=(20, 40), crown=-30))])
    # The shared Windup / Strike (Devour and most of its abilities): the upper arms rear up, then slam down in front.
    rear = M(up=0.1, pitch=-14, spine=-8, head=-10, al=(-0.1, -0.1, 0.9), ar=(-0.1, 0.1, 0.9), ll=(-0.05, -0.05, 0.25), lr=(-0.05, 0.05, 0.25), tail=(-15, 0), crown=25, t=0.3)
    slam = M(up=-0.05, fwd=0.18, pitch=24, spine=10, head=12, al=(0.7, 0.4, 0.3), ar=(0.7, -0.4, 0.3), ll=(0.3, 0, 0.2), lr=(0.3, 0, 0.2), tail=(-35, 0), crown=-10, rib=30, t=0.6)
    for pre in ('', 'devour.'):
        keys(pre + 'Windup', 0, 10, [(0, M()), (8, rear), (10, rear)])
        keys(pre + 'Strike', 0, 12, [(0, rear), (3, slam), (7, slam), (12, M())])
    # Rampage (and Blind Rampage): crouched, arms swept back, then a low lunge with every arm forward.
    coil = M(up=-0.06, pitch=10, al=(-0.35, -0.2, 0.3), ar=(-0.35, 0.2, 0.3), ll=(-0.3, -0.1, 0.2), lr=(-0.3, 0.1, 0.2), tail=(10, 0), crown=-20)
    lunge = M(up=0.02, fwd=0.25, pitch=34, spine=10, al=(0.8, 0.2, 0.55), ar=(0.8, -0.2, 0.55), ll=(0.5, 0.1, 0.5), lr=(0.5, -0.1, 0.5), tail=(-45, 0), rib=50)
    for pre in ('rampage.', 'blind_rampage.'):
        keys(pre + 'Windup', 0, 6, [(0, M()), (6, coil)])
        keys(pre + 'Strike', 0, 12, [(0, coil), (2, lunge), (7, lunge), (12, M())])
    # Unleash (and Howling End's release): every arm flung up, then thrown wide in a burst, the fans and spines raised.
    gather = M(up=0.12, pitch=-6, head=-14, al=(0.0, 0.2, 1.0), ar=(0.0, -0.2, 1.0), ll=(0.0, 0.15, 0.7), lr=(0.0, -0.15, 0.7), crown=35)
    blast = M(up=0.18, pitch=6, head=4, al=(0.2, -0.7, 0.7), ar=(0.2, 0.7, 0.7), ll=(0.1, -0.6, 0.5), lr=(0.1, 0.6, 0.5), crown=45, rib=60, tail=(-20, 0))
    for pre in ('unleash.', 'howling_end.'):
        keys(pre + 'Windup', 0, 12, [(0, M()), (12, gather)])
        keys(pre + 'Strike', 0, 16, [(0, gather), (3, blast), (10, blast), (16, M())])
    # Berserk: thrashing, every arm flailing.
    fresh('berserk.Windup', 0, 20)
    for i, fr in enumerate(range(0, 21, 4)):
        sgn = 1 if i % 2 else -1
        key('berserk.Windup', fr, M(up=0.06, yaw=20 * sgn, tilt=10 * sgn, al=(0.3, -0.3 * sgn, 0.6), ar=(0.3, 0.3 * sgn, 0.6),
                                    ll=(0.2, 0.3 * sgn, 0.3), lr=(0.2, -0.3 * sgn, 0.3), tail=(0, 40 * sgn), crown=30, t=i * 0.2))
    # REGRESSION: it curls in on itself, arms wrapped round, head bowed.
    curl = M(up=-0.1, pitch=30, spine=20, head=25, al=(0.2, 0.55, 0.35), ar=(0.2, -0.55, 0.35), ll=(0.25, 0.35, 0.3), lr=(0.25, -0.35, 0.3), tail=(40, 0), crown=-40)
    keys('regression.Windup', 0, 30, [(0, M()), (24, curl), (30, curl)])


PREVIEW_VIEW = {'ortho': 3.6, 'height': 1.35}
PREVIEW = [('Idle', 0), ('Idle', 30), ('Run', 5), ('Block', 6), ('Windup', 10), ('Strike', 3),
           ('rampage.Strike', 3), ('unleash.Strike', 4), ('berserk.Windup', 8), ('regression.Windup', 30), ('HitHeavy', 3), ('Death', 30)]
