# Fighter build helpers, run inside Blender (Scripting tab, or exec(open(path).read())).
# Not needed to edit the Fighter by hand: the .blend is the source. This is how its mesh and keyed poses were made,
# kept so poses can be re-made the same way (two-bone IK for arms and legs, directions for everything else).
#
# Character frame: F = forward (Blender -Y), R = the character's right (Blender -X), U = up (+Z). V(f, r, u) -> Blender.
import bpy, bmesh, math
from mathutils import Vector, Matrix, Quaternion

RIG, BODY = 'FighterRig', 'FighterBody'
WEAPONS = ('sword', 'dagger.L', 'dagger.R')


def V(f=0.0, r=0.0, u=0.0):
    return Vector((-r, -f, u))


def char_rot(yaw=0.0, lean=0.0, tilt=0.0):
    """yaw: turn to the right, lean: forward, tilt: toward the right (degrees), as a Blender-space rotation."""
    return (Matrix.Rotation(math.radians(-yaw), 3, 'Z') @ Matrix.Rotation(math.radians(lean), 3, 'X')
            @ Matrix.Rotation(math.radians(-tilt), 3, 'Y'))


# ---------------------------------------------------------------- mesh

def build_mesh():
    """Rebuild FighterBody: boxes rigidly weighted to one bone each; gauntlets; etched arms; the summoned weapons."""
    body = bpy.data.objects[BODY]
    me = body.data
    bm = bmesh.new()
    dl = bm.verts.layers.deform.verify()
    uvl = bm.loops.layers.uv.verify()
    groups = {g.name: g.index for g in body.vertex_groups}
    mats = {m.name: i for i, m in enumerate(me.materials)}

    def quad_uv(face, tile):
        n = face.normal
        if tile is None or abs(n.z) > 0.5:
            for l in face.loops: l[uvl].uv = (0.001, 0.001) if tile is None else (((tile % 4) + 0.5) / 4.0, ((tile // 4) + 0.5) / 2.0)
            return
        right = (-n).cross(Vector((0, 0, 1))).normalized()
        hs = [l.vert.co.dot(right) for l in face.loops]; zs = [l.vert.co.z for l in face.loops]
        h0, h1, z0, z1 = min(hs), max(hs), min(zs), max(zs)
        tx, ty = tile % 4, tile // 4
        for l, h, z in zip(face.loops, hs, zs):
            u = (h - h0) / max(1e-6, h1 - h0); v = (z - z0) / max(1e-6, z1 - z0)
            l[uvl].uv = ((tx + 0.02 + 0.96 * u) / 4.0, (ty + 0.02 + 0.96 * v) / 2.0)

    def solid(corners, group, mat, tiles=None):
        """corners: 8 points, index = x + 2y + 4z (0/1 per axis). tiles: pattern per side face, else None."""
        vs = [bm.verts.new(c) for c in corners]
        for v in vs: v[dl][groups[group]] = 1.0
        quads = [(0, 2, 6, 4), (1, 5, 7, 3), (0, 4, 5, 1), (2, 3, 7, 6), (0, 1, 3, 2), (4, 6, 7, 5)]
        fs = []
        for q in quads:
            f = bm.faces.new([vs[i] for i in q]); f.material_index = mats[mat]; fs.append(f)
        bmesh.ops.recalc_face_normals(bm, faces=fs)
        side = 0
        for f in fs:
            f.normal_update()
            if tiles is None: quad_uv(f, None)
            elif abs(f.normal.z) > 0.5: quad_uv(f, tiles[0])
            else: quad_uv(f, tiles[side % len(tiles)]); side += 1
        return vs

    def box(mn, mx, group, mat='Body', tiles=None):
        return solid([Vector((mx[0] if i & 1 else mn[0], mx[1] if i & 2 else mn[1], mx[2] if i & 4 else mn[2])) for i in range(8)], group, mat, tiles)

    for s, x in (('L', 1), ('R', -1)):
        def X(a, b): return (min(a * x, b * x), max(a * x, b * x))
        ua = X(0.21, 0.33); fa = X(0.215, 0.325); hd = X(0.205, 0.335); cf = X(0.202, 0.338)
        et = (0, 1, 2, 3) if s == 'L' else (4, 5, 6, 7)
        box((ua[0], -0.06, 1.14), (ua[1], 0.06, 1.42), 'upper_arm.' + s, 'Etched', [et[0], et[1], et[0], et[1]])
        box((fa[0], -0.055, 0.96), (fa[1], 0.055, 1.14), 'forearm.' + s, 'Etched', [et[2], et[3], et[2], et[3]])
        box((cf[0], -0.068, 0.87), (cf[1], 0.068, 0.985), 'forearm.' + s, 'Metal')     # gauntlet cuff
        box((hd[0], -0.065, 0.765), (hd[1], 0.065, 0.88), 'hand.' + s, 'Metal')        # gauntlet fist
        th = X(0.025, 0.175); sh = X(0.04, 0.16)
        box((th[0], -0.08, 0.48), (th[1], 0.08, 0.88), 'thigh.' + s)
        box((sh[0], -0.065, 0.08), (sh[1], 0.065, 0.48), 'shin.' + s)
        box((sh[0], -0.17, 0.0), (sh[1], 0.07, 0.08), 'foot.' + s)
    box((-0.17, -0.1, 0.88), (0.17, 0.1, 1.06), 'hips')
    box((-0.21, -0.12, 1.04), (0.21, 0.12, 1.46), 'spine')
    box((-0.12, -0.13, 1.47), (0.12, 0.11, 1.73), 'head')

    # Curved sword (Rend The Flesh) on 'sword', in the right fist; the blade runs forward from it and sweeps up.
    cx, cz = -0.27, 0.825
    box((cx - 0.014, -0.1, cz - 0.016), (cx + 0.014, 0.08, cz + 0.016), 'sword', 'Metal')            # grip
    box((cx - 0.03, -0.12, cz - 0.07), (cx + 0.03, -0.09, cz + 0.07), 'sword', 'Metal')              # guard
    n = 6
    prev = None
    for i in range(n):
        t = i / (n - 1)
        y = -0.12 - 0.86 * t
        zc = cz + 0.2 * t * t
        h = 0.10 * (1.0 - 0.25 * t) if i < n - 1 else 0.02
        w = 0.009 if i < n - 1 else 0.004
        ring = [bm.verts.new((cx + dx, y, zc + dz)) for dx, dz in ((-w, -h * 0.35), (w, -h * 0.35), (w, h * 0.65), (-w, h * 0.65))]
        for v in ring: v[dl][groups['sword']] = 1.0
        if prev:
            for k in range(4):
                f = bm.faces.new((prev[k], prev[(k + 1) % 4], ring[(k + 1) % 4], ring[k])); f.material_index = mats['Metal']
        else:
            f = bm.faces.new(ring); f.material_index = mats['Metal']
        prev = ring
    f = bm.faces.new(list(reversed(prev))); f.material_index = mats['Metal']

    # Daggers (Phantom Strike), one per fist, blades forward.
    for s, dx in (('L', 0.27), ('R', -0.27)):
        box((dx - 0.012, -0.09, cz - 0.03), (dx + 0.012, -0.07, cz + 0.03), 'dagger.' + s, 'Metal')     # guard
        b0, b1 = -0.09, -0.36
        solid([Vector((dx + (0.006 if i & 1 else -0.006), b1 if i & 2 else b0,
                       cz + (1 if i & 4 else -1) * (0.005 if i & 2 else 0.024))) for i in range(8)], 'dagger.' + s, 'Metal')

    for f in bm.faces:
        if f.material_index != mats.get('Etched') and not any(l[uvl].uv.length > 0.01 for l in f.loops):
            for l in f.loops: l[uvl].uv = (0.001, 0.001)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.to_mesh(me)
    bm.free()
    me.update()
    return len(me.vertices), len(me.polygons)


# ---------------------------------------------------------------- posing

def _bones(rig):
    order = []
    def walk(b):
        order.append(b)
        for c in b.children: walk(c)
    for b in rig.data.bones:
        if b.parent is None: walk(b)
    return order


def _two_bone(S, T, a, b, pole):
    d = T - S
    dist = max(1e-4, min(d.length, (a + b) * 0.999))
    u = d.normalized()
    w = pole - u * pole.dot(u)
    w = w.normalized() if w.length > 1e-5 else Vector((0, -1, 0))
    ca = max(-1.0, min(1.0, (a * a + dist * dist - b * b) / (2 * a * dist)))
    E = S + a * (ca * u + math.sqrt(max(0.0, 1 - ca * ca)) * w)
    Tr = S + u * dist
    return (E - S).normalized(), (Tr - E).normalized()


def solve(spec):
    """spec -> {bone: (basis Quaternion, location Vector)}.
    spec keys:
      hips:  {'off': V(...), 'yaw','lean','tilt'}   the pelvis, in the world
      spine, head: {'yaw','lean','tilt'}             relative to the parent's posed frame
      arm.L / arm.R: (wrist target, elbow pole)      target relative to the chest (V offsets from the spine's top, upright)
      leg.L / leg.R: (ankle target, knee pole)       target in the world (V, from the ground under the hips' rest)
      hand.L / hand.R / foot.X / <weapon>: {'dir': V, 'fwd': V} world aims (fwd: where the side that faced forward at
                                           rest goes), or {'yaw','lean','tilt'} relative to the parent
    """
    rig = bpy.data.objects[RIG]
    rest = {b.name: b.matrix_local.copy() for b in rig.data.bones}
    R3 = {k: m.to_3x3() for k, m in rest.items()}
    world = {}      # posed 4x4, armature space
    out = {}
    aims = {}
    # Limb IK first needs the chest: pose the torso, then solve arms and legs into directions.
    def posed(b, basis_rot, loc=Vector()):
        M_basis = Matrix.Translation(loc) @ basis_rot.to_matrix().to_4x4()
        if b.parent:
            M = world[b.parent.name] @ rest[b.parent.name].inverted() @ rest[b.name] @ M_basis
        else:
            M = rest[b.name] @ M_basis
        world[b.name] = M
        return M

    def default_rot(b):
        if b.parent:
            return world[b.parent.name].to_3x3() @ R3[b.parent.name].inverted() @ R3[b.name]
        return R3[b.name]

    def delta(name):
        return world[name].to_3x3() @ R3[name].inverted()

    for b in _bones(rig):
        n = b.name
        s = spec.get(n, {})
        D = default_rot(b).normalized()
        loc = Vector()
        if n == 'hips':
            target = char_rot(s.get('yaw', 0), s.get('lean', 0), s.get('tilt', 0)) @ R3[n]
            off = s.get('off', Vector())
            loc = R3[n].inverted() @ off
        elif n in aims or 'dir' in s or 'blade' in s:
            d, fwd = aims.get(n, (None, None)) if n in aims else (None, None)
            if 'dir' in s: d = s['dir']
            if 'fwd' in s: fwd = s['fwd']
            if 'blade' in s:   # the held weapon points exactly along 'blade'; the hand as near 'dir' as that allows
                fwd = s['blade'].normalized()
                hint = (d if d is not None else D.col[1]).normalized()
                y = hint - fwd * hint.dot(fwd)
                d = y if y.length > 1e-4 else fwd.orthogonal()
            d = d.normalized()
            y0 = D.col[1].normalized()
            target = y0.rotation_difference(d).to_matrix() @ D
            if fwd is not None:
                front = target @ (R3[n].inverted() @ V(1, 0, 0))
                fp = front - d * front.dot(d); wp = fwd - d * fwd.dot(d)
                if fp.length > 1e-5 and wp.length > 1e-5:
                    ang = fp.normalized().angle(wp.normalized())
                    if fp.cross(wp).dot(d) < 0: ang = -ang
                    target = Matrix.Rotation(ang, 3, d) @ target
        elif any(k in s for k in ('yaw', 'lean', 'tilt')):
            par = delta(b.parent.name) if b.parent else Matrix.Identity(3)
            target = par @ char_rot(s.get('yaw', 0), s.get('lean', 0), s.get('tilt', 0)) @ R3[n]
        else:
            target = D
        basis = (D.inverted() @ target).to_quaternion()
        posed(b, basis, loc)
        out[n] = (basis, loc)

        if n == 'spine':   # now the chest is known: arm IK targets
            top = world['spine'] @ Vector((0, rig.data.bones['spine'].length, 0))
            chest = delta('spine')
            for side in ('L', 'R'):
                if 'arm.' + side not in spec: continue
                tgt, pole = spec['arm.' + side]
                ub, fb = rig.data.bones['upper_arm.' + side], rig.data.bones['forearm.' + side]
                Sx = world['spine'] @ rest['spine'].inverted() @ ub.head_local
                du, df = _two_bone(Sx, top + chest @ tgt, ub.length, fb.length, chest @ pole)
                aims['upper_arm.' + side] = (du, None); aims['forearm.' + side] = (df, None)
        if n == 'hips':
            for side in ('L', 'R'):
                if 'leg.' + side not in spec: continue
                tgt, pole = spec['leg.' + side]
                tb, sb = rig.data.bones['thigh.' + side], rig.data.bones['shin.' + side]
                Hx = world['hips'] @ rest['hips'].inverted() @ tb.head_local
                dt, ds = _two_bone(Hx, tgt, tb.length, sb.length, pole)
                aims['thigh.' + side] = (dt, None); aims['shin.' + side] = (ds, None)
                if 'foot.' + side not in spec:
                    aims['foot.' + side] = (V(0.95, 0, -0.32), None)
    return out


def stance_foot(side, f=0.0, r=0.0, u=0.0):
    """An ankle target: where that foot's ankle is at rest (x = +-0.1, height 0.08) moved by f / r / u."""
    return V(f, r, u) + Vector((0.1 if side == 'L' else -0.1, 0, 0.08))


def key(action_name, frame, spec, weapons=None):
    """Key the pose 'spec' into the action at 'frame'. weapons: {'sword': 1, ...} scales (missing = 0, hidden)."""
    rig = bpy.data.objects[RIG]
    if rig.animation_data is None: rig.animation_data_create()
    act = bpy.data.actions.get(action_name) or bpy.data.actions.new(action_name)
    act.use_fake_user = True
    if rig.animation_data.action != act:
        rig.animation_data.action = act
        if hasattr(rig.animation_data, 'action_suitable_slots') and rig.animation_data.action_slot is None:
            slots = rig.animation_data.action_suitable_slots
            if slots: rig.animation_data.action_slot = slots[0]
    pose = solve(spec)
    for name, (q, loc) in pose.items():
        pb = rig.pose.bones[name]
        prev = pb.rotation_euler.copy()
        pb.rotation_mode = 'XYZ'
        pb.rotation_euler = q.to_euler('XYZ', prev)
        pb.keyframe_insert('rotation_euler', frame=frame)
        if name == 'hips':
            pb.location = loc
            pb.keyframe_insert('location', frame=frame)
    weapons = weapons or {}
    for w in WEAPONS:
        if w not in rig.pose.bones: continue
        sc = weapons.get(w, 0.0)
        rig.pose.bones[w].scale = (sc, sc, sc)
        rig.pose.bones[w].keyframe_insert('scale', frame=frame)
    return act
