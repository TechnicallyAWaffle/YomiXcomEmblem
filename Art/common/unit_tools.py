# Shared build helpers for the unit models, run inside Blender (see build_unit.py, which runs a unit's script with these).
# Generalized from Art/Fighter/fighter_tools.py: the same 15-bone rig, boxes rigidly weighted to one bone each,
# two-bone IK for limbs and aim directions for hands and held weapons.
#
# Character frame: F = forward (Blender -Y), R = the character's right (Blender -X), U = up (+Z). V(f, r, u) -> Blender.
# Blender coordinates in the mesh code: +X is the character's LEFT, -Y is forward, +Z up.
import bpy, bmesh, math, os
from mathutils import Vector, Matrix, Quaternion

CFG = {'rig': 'FighterRig', 'body': 'FighterBody', 'weapons': (), 'ankles': None}
TEMPLATE_WEAPONS = ('sword', 'dagger.L', 'dagger.R')   # the Fighter's summoned-weapon bones, removed from new units


def V(f=0.0, r=0.0, u=0.0):
    return Vector((-r, -f, u))


def char_rot(yaw=0.0, lean=0.0, tilt=0.0):
    """yaw: turn to the right, lean: forward, tilt: toward the right (degrees), as a Blender-space rotation."""
    return (Matrix.Rotation(math.radians(-yaw), 3, 'Z') @ Matrix.Rotation(math.radians(lean), 3, 'X')
            @ Matrix.Rotation(math.radians(-tilt), 3, 'Y'))


def rig_obj(): return bpy.data.objects[CFG['rig']]


# ---------------------------------------------------------------- setting up a unit from the Fighter's file

def make_unit(name, skeleton=None, extra=None):
    """In the open Fighter.blend: rename the rig and body for 'name', drop the Fighter's weapon bones and every action.
    skeleton: {bone: (head, tail)} new rest positions (Blender coordinates, e.g. V(...)) for a body of other proportions
    (a beast on all fours, a giant); extra: {bone: (head, tail, parent)} bones of its own (wings, tentacles), each with a
    vertex group, posed by {'yaw','lean','tilt'} relative to its parent like the spine."""
    rig, body = bpy.data.objects['FighterRig'], bpy.data.objects['FighterBody']
    rig.name, rig.data.name = name + 'Rig', name + 'Armature'
    body.name, body.data.name = name + 'Body', name + 'Mesh'
    CFG.update(rig=rig.name, body=body.name, weapons=())
    if rig.animation_data: rig.animation_data.action = None
    for a in list(bpy.data.actions): bpy.data.actions.remove(a)
    for o in bpy.context.view_layer.objects: o.select_set(False)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode='EDIT')
    for n in TEMPLATE_WEAPONS:
        eb = rig.data.edit_bones.get(n)
        if eb: rig.data.edit_bones.remove(eb)
    for b in rig.data.edit_bones: b.use_connect = False
    for n, (hd, tl) in (skeleton or {}).items():
        b = rig.data.edit_bones[n]; b.head = hd; b.tail = tl
    for n, (hd, tl, parent) in (extra or {}).items():
        b = rig.data.edit_bones.new(n); b.head = hd; b.tail = tl; b.parent = rig.data.edit_bones[parent]
    bpy.ops.object.mode_set(mode='OBJECT')
    for n in TEMPLATE_WEAPONS:
        g = body.vertex_groups.get(n)
        if g: body.vertex_groups.remove(g)
    for n in (extra or {}):
        if body.vertex_groups.get(n) is None: body.vertex_groups.new(name=n)
    CFG['ankles'] = {s: rig.data.bones['shin.' + s].tail_local.copy() for s in ('L', 'R')}
    for pb in rig.pose.bones:
        pb.rotation_mode = 'XYZ'; pb.rotation_euler = (0, 0, 0); pb.location = (0, 0, 0); pb.scale = (1, 1, 1)
    return rig, body


# ---------------------------------------------------------------- mesh

class MeshBuilder:
    """Boxes and prisms, each weighted 100% to one bone. Materials by slot name (Body = team colour, Metal, Etched =
    team colour with the unit's etching pattern; Etched faces get a tile of the 4x2 pattern each)."""

    def __init__(self):
        self.body = bpy.data.objects[CFG['body']]
        self.me = self.body.data
        self.bm = bmesh.new()
        self.dl = self.bm.verts.layers.deform.verify()
        self.uvl = self.bm.loops.layers.uv.verify()
        self.groups = {g.name: g.index for g in self.body.vertex_groups}
        self.mats = {m.name: i for i, m in enumerate(self.me.materials)}
        self.etched = []

    # Slots past Body / Metal / Etched, made on first use. In Unity (UnitLooks): Dark is near black (smoke, shadow),
    # Glow the team's colour, glowing, Gold a warm gold, Bone a pale ivory (masks, bone, pale flesh), Eye a bright white
    # (eyes). Their colours here are only for the previews.
    SLOT_COLOURS = {'Dark': (0.08, 0.07, 0.09, 1), 'Glow': (1.0, 0.5, 0.45, 1), 'Gold': (0.9, 0.72, 0.3, 1),
                    'Bone': (0.9, 0.86, 0.74, 1), 'Eye': (0.98, 0.98, 0.98, 1)}

    def _mat(self, name):
        if name not in self.mats:
            mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
            mat.diffuse_color = self.SLOT_COLOURS.get(name, (0.8, 0.8, 0.8, 1))
            self.me.materials.append(mat)
            self.mats[name] = len(self.me.materials) - 1
        return self.mats[name]

    def _uv(self, face, tile):
        uvl = self.uvl
        n = face.normal
        if tile is None or abs(n.z) > 0.5:
            for l in face.loops:
                l[uvl].uv = (0.001, 0.001) if tile is None else (((tile % 4) + 0.5) / 4.0, ((tile // 4) + 0.5) / 2.0)
            return
        right = (-n).cross(Vector((0, 0, 1))).normalized()
        hs = [l.vert.co.dot(right) for l in face.loops]; zs = [l.vert.co.z for l in face.loops]
        h0, h1, z0, z1 = min(hs), max(hs), min(zs), max(zs)
        tx, ty = tile % 4, tile // 4
        for l, h, z in zip(face.loops, hs, zs):
            u = (h - h0) / max(1e-6, h1 - h0); v = (z - z0) / max(1e-6, z1 - z0)
            l[uvl].uv = ((tx + 0.02 + 0.96 * u) / 4.0, (ty + 0.02 + 0.96 * v) / 2.0)

    def solid(self, corners, group, mat='Body', tiles=None):
        """corners: 8 points, index = x + 2y + 4z (0/1 per axis). tiles: pattern tile per side face (Etched)."""
        bm = self.bm
        vs = [bm.verts.new(c) for c in corners]
        for v in vs: v[self.dl][self.groups[group]] = 1.0
        quads = [(0, 2, 6, 4), (1, 5, 7, 3), (0, 4, 5, 1), (2, 3, 7, 6), (0, 1, 3, 2), (4, 6, 7, 5)]
        fs = []
        for q in quads:
            f = bm.faces.new([vs[i] for i in q]); f.material_index = self._mat(mat); fs.append(f)
        bmesh.ops.recalc_face_normals(bm, faces=fs)
        if mat == 'Etched' and tiles is None:
            tiles = [len(self.etched) % 8, (len(self.etched) + 3) % 8]
        side = 0
        for f in fs:
            f.normal_update()
            if tiles is None: self._uv(f, None)
            elif abs(f.normal.z) > 0.5: self._uv(f, tiles[0])
            else: self._uv(f, tiles[side % len(tiles)]); side += 1
        if mat == 'Etched': self.etched.append(group)
        return vs

    def box(self, x, y, z, group, mat='Body', tiles=None):
        """x, y, z: (min, max) ranges in Blender coordinates."""
        return self.solid([Vector((x[i & 1], y[(i >> 1) & 1], z[(i >> 2) & 1])) for i in range(8)], group, mat, tiles)

    def cbox(self, c, half, group, mat='Body', tiles=None):
        """A box by centre and half sizes (Blender coordinates)."""
        return self.box((c[0] - half[0], c[0] + half[0]), (c[1] - half[1], c[1] + half[1]),
                        (c[2] - half[2], c[2] + half[2]), group, mat, tiles)

    def taper(self, y0, y1, c, w0, h0, w1, h1, group, mat='Metal', up=0.0):
        """A box along Y (forward is -Y) from y0 to y1 around centre c = (x, z), its cross-section w x h tapering;
        'up' raises the far end (a curve-free sweep)."""
        cx, cz = c
        pts = []
        for i in range(8):
            far = (i >> 1) & 1
            w, h = (w1, h1) if far else (w0, h0)
            pts.append(Vector((cx + (w if i & 1 else -w), y1 if far else y0, cz + (up if far else 0.0) + (h if i & 4 else -h))))
        return self.solid(pts, group, mat)

    def diamond(self, c, r, h, group, mat='Metal'):
        """An octahedron (a gem / shard): centre c, half-width r, half-height h, Blender coordinates."""
        bm = self.bm
        cx, cy, cz = c
        ring = [bm.verts.new((cx + dx, cy + dy, cz)) for dx, dy in ((r, 0), (0, r), (-r, 0), (0, -r))]
        top = bm.verts.new((cx, cy, cz + h)); bot = bm.verts.new((cx, cy, cz - h))
        for v in ring + [top, bot]: v[self.dl][self.groups[group]] = 1.0
        fs = []
        for k in range(4):
            a, b = ring[k], ring[(k + 1) % 4]
            fs.append(bm.faces.new((a, b, top))); fs.append(bm.faces.new((b, a, bot)))
        for f in fs: f.material_index = self._mat(mat)
        bmesh.ops.recalc_face_normals(bm, faces=fs)
        for f in fs:
            for l in f.loops: l[self.uvl].uv = (0.001, 0.001)

    def finish(self):
        bm = self.bm
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        bm.to_mesh(self.me)
        bm.free()
        self.me.update()
        return len(self.me.vertices), len(self.me.polygons)


def sym(fn):
    """fn(side, s, X) for each side: side 'L'/'R', s = +1 (left, +X) / -1, X(a, b) = the (min, max) range a..b mirrored."""
    for side, s in (('L', 1.0), ('R', -1.0)):
        fn(side, s, lambda a, b, s=s: (min(a * s, b * s), max(a * s, b * s)))


# ---------------------------------------------------------------- posing (as in fighter_tools.py, plus chest-frame aims)

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
      hips:  {'off': V(...), 'yaw','lean','tilt'}   the pelvis, in the world
      spine, head: {'yaw','lean','tilt'}             relative to the parent's posed frame
      arm.L / arm.R: (wrist target, elbow pole)      target relative to the chest (V offsets from the spine's top, in the
                                                     chest's turned frame)
      leg.L / leg.R: (ankle target, knee pole)       target in the world (V, from the ground under the hips' rest)
      hand.X / foot.X: {'dir': V, 'fwd': V} world aims (dir: where the bone points; fwd: where the side that faced
                       forward at rest goes); 'blade': V aims the held weapon (which runs along that forward side) exactly,
                       the hand as near 'dir' as that allows. 'cdir' / 'cfwd' / 'cblade': the same in the chest's frame.
                       Or {'yaw','lean','tilt'} relative to the parent.
    """
    rig = rig_obj()
    rest = {b.name: b.matrix_local.copy() for b in rig.data.bones}
    R3 = {k: m.to_3x3() for k, m in rest.items()}
    world = {}
    out = {}
    aims = {}

    def posed(b, basis_rot, loc=Vector()):
        M_basis = Matrix.Translation(loc) @ basis_rot.to_matrix().to_4x4()
        M = (world[b.parent.name] @ rest[b.parent.name].inverted() @ rest[b.name] @ M_basis) if b.parent else rest[b.name] @ M_basis
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
        s = dict(spec.get(n, {}))
        if any(k in s for k in ('cdir', 'cfwd', 'cblade')) and 'spine' in world:
            ch = delta('spine')
            for k in ('dir', 'fwd', 'blade'):
                if 'c' + k in s: s[k] = ch @ s.pop('c' + k)
        D = default_rot(b).normalized()
        loc = Vector()
        if n == 'hips':
            target = char_rot(s.get('yaw', 0), s.get('lean', 0), s.get('tilt', 0)) @ R3[n]
            loc = R3[n].inverted() @ s.get('off', Vector())
        elif n in aims or 'dir' in s or 'blade' in s:
            d, fwd = aims.get(n, (None, None))
            if 'dir' in s: d = s['dir']
            if 'fwd' in s: fwd = s['fwd']
            if 'blade' in s:
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

        if n == 'spine':
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
    CFG['world'] = world
    return out


def planted(spec, side, target, pole):
    """spec with arm.<side> set so that wrist reaches 'target' in the world (a paw or a knuckle on the ground), its
    elbow toward 'pole' (world). Arm targets are otherwise relative to the chest, so they move with the body."""
    s = {k: v for k, v in spec.items() if k != 'arm.' + side}
    solve(s)
    rig = rig_obj()
    w = CFG['world']
    top = w['spine'] @ Vector((0, rig.data.bones['spine'].length, 0))
    chest = w['spine'].to_3x3() @ rig.data.bones['spine'].matrix_local.to_3x3().inverted()
    inv = chest.inverted()
    out = dict(spec)
    out['arm.' + side] = (inv @ (target - top), inv @ pole)
    return out


def stance_foot(side, f=0.0, r=0.0, u=0.0):
    """An ankle target: where that foot's ankle is at rest (x = +-0.1, height 0.08) moved by f / r / u."""
    a = CFG['ankles'][side] if CFG.get('ankles') else Vector((0.1 if side == 'L' else -0.1, 0, 0.08))
    return V(f, r, u) + a


def fresh(name, start, end):
    old = bpy.data.actions.get(name)
    if old: bpy.data.actions.remove(old)
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    act.use_frame_range = True
    act.frame_start, act.frame_end = start, end
    rig = rig_obj()
    if rig.animation_data is None: rig.animation_data_create()
    rig.animation_data.action = None
    for pb in rig.pose.bones:
        pb.rotation_euler = (0, 0, 0); pb.location = (0, 0, 0); pb.scale = (1, 1, 1)
    return name


def key(action_name, frame, spec, weapons=None):
    """Key the pose 'spec' into the action at 'frame'. weapons: {'bone': scale} for summoned-weapon bones."""
    rig = rig_obj()
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
    for w in CFG['weapons']:
        if w not in rig.pose.bones: continue
        sc = weapons.get(w, 0.0)
        rig.pose.bones[w].scale = (sc, sc, sc)
        rig.pose.bones[w].keyframe_insert('scale', frame=frame)
    return act


def keys(name, start, end, frames):
    """fresh(name) and key each (frame, spec) in 'frames'."""
    fresh(name, start, end)
    for fr, spec in frames: key(name, fr, spec)


def copy_action(src, *names):
    """Copies of action 'src' under other names (several abilities sharing one set of clips)."""
    for n in names:
        old = bpy.data.actions.get(n)
        if old: bpy.data.actions.remove(old)
        c = bpy.data.actions[src].copy(); c.name = n; c.use_fake_user = True


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
            for a in ('off', 'dir', 'fwd', 'blade', 'cdir', 'cfwd', 'cblade'):
                if a in d: d[a] = mv(d[a])
            out[k2] = d
    return out


def merge(base, **over):
    """A copy of 'base' with parts replaced: keyword arm_L means 'arm.L'."""
    s = dict(base)
    for k, v in over.items(): s[k.replace('_', '.')] = v
    return s


def plus(base, extra):
    s = dict(base); s.update(extra); return s


def upper(spec):
    """Only the arms and hands of a pose (to carry a weapon hold into a leg / body pose)."""
    return {k: v for k, v in spec.items() if k.split('.')[0] in ('arm', 'hand')}


# ---------------------------------------------------------------- shared clips built from a unit's stance
# S(drop=0.0, lean=None, yaw=None, f=0.0) -> the unit's standing pose; 'hold' = upper(S()) unless given.

def run_cycle(S, arms=None, stride=1.0, lean=18, lift=1.0, frames=16, name='Run'):
    """A run (or walk, with a short stride and low lean). arms(phase) -> arm/hand specs for phases 0-3 (contact R,
    pass, contact L, pass); None: pumping arms, as the Fighter's."""
    sl = lean / 18.0
    def contact():
        return {'hips': {'off': V(0.04 * sl, 0, -0.08), 'yaw': -8 * stride, 'lean': lean},
                'spine': {'yaw': 14 * stride, 'lean': 20 * sl}, 'head': {'yaw': -6 * stride, 'lean': -24 * sl - 0.0},
                'leg.R': (stance_foot('R', 0.36 * stride, 0, 0.02), V(1, 0, 0)),
                'leg.L': (stance_foot('L', -0.42 * stride, 0, 0.24 * lift), V(1, 0, 0))}
    def passing():
        return {'hips': {'off': V(0.04 * sl, 0, -0.08 + 0.06 * lift), 'yaw': 0, 'lean': lean},
                'spine': {'yaw': 0, 'lean': 20 * sl}, 'head': {'yaw': 0, 'lean': -24 * sl},
                'leg.R': (stance_foot('R', -0.08 * stride, 0, 0.0), V(1, 0, 0)),
                'leg.L': (stance_foot('L', 0.12 * stride, 0, 0.44 * lift * min(1.0, stride + 0.2)), V(1, 0, 0))}
    pumping = [
        {'arm.L': (V(0.26, -0.12, -0.2), V(-0.3, 0, -1)), 'arm.R': (V(-0.2, 0.15, -0.34), V(-1, 0, 0.1))},
        {'arm.L': (V(0.06, -0.12, -0.3), V(-0.6, 0, -1)), 'arm.R': (V(0.02, 0.14, -0.3), V(-0.6, 0, -1))}]
    pumping += [mirror(pumping[0]), mirror(pumping[1])]
    poses = [contact(), passing(), mirror(contact()), mirror(passing())]
    q = frames // 4
    fresh(name, 0, frames)
    for i, fr in enumerate((0, q, 2 * q, 3 * q, frames)):
        ph = i % 4
        p = dict(poses[ph]); p.update(arms(ph) if arms else pumping[ph])
        key(name, fr, p)


def dashes(S, hold=None, forward_arms=None):
    """DashForward / DashBack / DashSide (0.2 s each), arms keeping 'hold' (forward: 'forward_arms' if given)."""
    hold = upper(S()) if hold is None else hold
    def fwd_pose(air):
        p = {'hips': {'off': V(0.08, 0, -0.06 + 0.08 * air), 'yaw': 5, 'lean': 22},
             'spine': {'yaw': 5, 'lean': 16}, 'head': {'yaw': -8, 'lean': -30},
             'leg.R': (stance_foot('R', -0.5 + 0.06 * air, 0, 0.12 + 0.18 * air), V(1, 0, 0)),
             'leg.L': (stance_foot('L', 0.26, 0, 0.3 + 0.12 * air), V(1, 0, 0))}
        p.update(forward_arms or hold); return p
    keys('DashForward', 0, 6, [(0, S(drop=0.06)), (2, fwd_pose(0.0)), (6, fwd_pose(1.0))])
    back = lambda air: plus(merge(S(),
        hips={'off': V(-0.08, 0, -0.08 + 0.1 * air), 'yaw': 15, 'lean': -6},
        leg_L=(stance_foot('L', 0.46 - 0.1 * air, -0.02, 0.16 * air), V(1, 0, 0)),
        leg_R=(stance_foot('R', -0.22, 0.04, 0.3 - 0.05 * air), V(1, 0, 0))), hold)
    keys('DashBack', 0, 6, [(0, S(drop=0.06)), (2, back(0.0)), (6, back(1.0))])
    side = lambda air: plus(merge(S(),
        hips={'off': V(0, 0.1, -0.08 + 0.1 * air), 'yaw': 15, 'lean': 6, 'tilt': 16},
        spine={'yaw': 5, 'lean': 8, 'tilt': 6}, head={'yaw': -20, 'lean': -8, 'tilt': -14},
        leg_L=(stance_foot('L', 0.05, -0.5 + 0.12 * air, 0.2 * air), V(1, -0.3, 0)),
        leg_R=(stance_foot('R', -0.05, 0.28, 0.26), V(1, 0.3, 0))), hold)
    keys('DashSide', 0, 6, [(0, S(drop=0.06)), (2, side(0.0)), (6, side(1.0))])
    return fwd_pose


def reactions(S, hold=None, limp=None):
    """HitLight, HitHeavy, Interrupted, Stun, Death. 'hold': arms kept through the light reactions; 'limp': arms for the
    stun (default: hanging in front)."""
    hold = upper(S()) if hold is None else hold
    keys('HitLight', 0, 8, [(0, S()),
        (2, plus(merge(S(), hips={'off': V(-0.06, 0, -0.08), 'yaw': 30, 'lean': -6}, spine={'yaw': 0, 'lean': -6},
                       head={'yaw': -30, 'lean': -18}), hold)), (8, S())])
    recoil = merge(S(), hips={'off': V(-0.14, 0, -0.06), 'yaw': 35, 'lean': -12}, spine={'yaw': 0, 'lean': -14},
                   head={'yaw': -20, 'lean': -22},
                   arm_L=(V(0.0, -0.36, -0.28), V(-0.3, -1, -0.3)), arm_R=(V(-0.06, 0.36, -0.3), V(-0.3, 1, -0.3)))
    keys('HitHeavy', 0, 14, [(0, S()), (3, recoil),
        (8, merge(S(drop=0.06), hips={'off': V(-0.08, 0, -0.14), 'yaw': 25, 'lean': 8})), (14, S())])
    keys('Interrupted', 0, 10, [(0, S()),
        (3, merge(S(), hips={'off': V(-0.04, 0.05, -0.08), 'yaw': 25, 'lean': -4, 'tilt': 12},
                  arm_L=(V(0.12, -0.3, -0.35), V(0, -1, -0.5)), arm_R=(V(0.06, 0.3, -0.36), V(0, 1, -0.5)))),
        (6, merge(S(), hips={'off': V(0, -0.04, -0.08), 'yaw': 25, 'lean': 2, 'tilt': -8})), (10, S())])
    limp = limp or {'arm.L': (V(0.06, -0.1, -0.5), V(-0.2, -0.3, -1)), 'arm.R': (V(0.02, 0.1, -0.52), V(-0.2, 0.3, -1))}
    stun = lambda t: plus(merge(S(drop=0.08), hips={'off': V(0, 0, -0.16), 'yaw': 15, 'lean': 14, 'tilt': t},
                                spine={'yaw': 0, 'lean': 22}, head={'yaw': -10, 'lean': 24}), limp)
    keys('Stun', 0, 30, [(0, stun(5)), (15, stun(-5)), (30, stun(5))])
    stagger = merge(S(drop=0.1), hips={'off': V(-0.12, 0, -0.26), 'yaw': 10, 'lean': -8},
                    spine={'yaw': 0, 'lean': -10}, head={'yaw': 0, 'lean': -30},
                    arm_L=(V(0.1, -0.35, -0.2), V(0, -1, -0.3)), arm_R=(V(0.06, 0.35, -0.22), V(0, 1, -0.3)))
    down = {'hips': {'off': V(-0.6, 0, -0.76), 'yaw': 0, 'lean': -86},
            'spine': {'lean': -4}, 'head': {'lean': -12, 'yaw': 25},
            'leg.L': (stance_foot('L', 0.18, -0.08, 0.0), V(0, 0, 1)), 'leg.R': (stance_foot('R', 0.12, 0.1, 0.06), V(0, 0, 1)),
            'arm.L': (V(0.02, -0.52, -0.06), V(-1, 0, -0.2)), 'arm.R': (V(-0.04, 0.5, -0.12), V(-1, 0, -0.2))}
    keys('Death', 0, 24, [(0, S()), (8, stagger), (24, down)])


# ---------------------------------------------------------------- etching patterns, export, previews

def make_etchings(path, tiles):
    """A 512x512 pattern of 4x2 tiles (128x256 each): dark lines on white. tiles: 8 functions (x, y numpy arrays in
    0..1, y up) -> bool 'ink' mask."""
    import numpy as np
    W, H = 512, 512
    img = np.ones((H, W, 4), np.float32)
    ys, xs = np.mgrid[0:256, 0:128].astype(np.float32)
    x = (xs + 0.5) / 128.0; y = (ys + 0.5) / 256.0
    for t, fn in enumerate(tiles):
        tx, ty = t % 4, t // 4
        ink = fn(x, y)
        border = (xs < 3) | (xs > 124) | (ys < 3) | (ys > 252)
        val = np.where(ink | border, 0.2, 1.0).astype(np.float32)
        img[ty * 256:(ty + 1) * 256, tx * 128:(tx + 1) * 128, 0] = val
        img[ty * 256:(ty + 1) * 256, tx * 128:(tx + 1) * 128, 1] = val
        img[ty * 256:(ty + 1) * 256, tx * 128:(tx + 1) * 128, 2] = val
    im = bpy.data.images.new(os.path.basename(path), W, H, alpha=True)
    im.pixels.foreach_set(img.ravel())
    im.filepath_raw = path; im.file_format = 'PNG'; im.save()
    bpy.data.images.remove(im)


def export_fbx(path):
    rig, body = bpy.data.objects[CFG['rig']], bpy.data.objects[CFG['body']]
    for o in bpy.context.view_layer.objects: o.select_set(False)
    rig.select_set(True); body.select_set(True)
    bpy.context.view_layer.objects.active = rig
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'},
                             axis_forward='-Z', axis_up='Y', apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True,
                             mesh_smooth_type='FACE', add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True,
                             bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
                             bake_anim_simplify_factor=0.0)


def preview(out_png, shots, size=320, cols=6, ortho=2.6, height=1.35):
    """Workbench renders of (action, frame) pairs from the front-left, in a grid, to one PNG (for checking poses)."""
    import numpy as np
    sc = bpy.context.scene
    rig = rig_obj()
    cam = bpy.data.objects.get('PreviewCam') or bpy.data.objects.get('Camera')
    # A fixed three-quarter front view, framing a 2m figure.
    cam.location = Vector((-2.6, -3.6, height)); cam.rotation_euler = (math.radians(86), 0, math.radians(-36))
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = ortho
    sc.camera = cam
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'MATERIAL'
    sc.display.shading.show_cavity = True
    sc.render.resolution_x = sc.render.resolution_y = size; sc.render.resolution_percentage = 100
    sc.render.film_transparent = False
    for o in bpy.data.objects:
        if o.type == 'MESH' and o.name != CFG['body']: o.hide_render = True
    tmp = os.path.join(os.path.dirname(out_png), '_shot.png')
    rows = (len(shots) + cols - 1) // cols
    sheet = np.ones((rows * size, cols * size, 4), np.float32)
    for i, (act, fr) in enumerate(shots):
        rig.animation_data.action = bpy.data.actions[act]
        sc.frame_set(int(fr))
        sc.render.filepath = tmp
        bpy.ops.render.render(write_still=True)
        im = bpy.data.images.load(tmp)
        px = np.array(im.pixels[:], np.float32).reshape(size, size, 4)
        bpy.data.images.remove(im)
        r, c = rows - 1 - i // cols, i % cols
        sheet[r * size:(r + 1) * size, c * size:(c + 1) * size] = px
    im = bpy.data.images.new('sheet', cols * size, rows * size, alpha=True)
    im.pixels.foreach_set(sheet.ravel()); im.filepath_raw = out_png; im.file_format = 'PNG'; im.save()
    bpy.data.images.remove(im)
    if os.path.exists(tmp): os.remove(tmp)


# ---------------------------------------------------------------- stock parts (the standard skeleton's proportions)

FIST = {'L': (0.27, 0.825), 'R': (-0.27, 0.825)}   # each fist's centre (Blender x, z) at rest


def humanoid(m, w=1.0, mats=None, skip=()):
    """A plain body of boxes on the standard skeleton: legs, arms, hips, torso and head, each part's width scaled by 'w'
    (0.8: lithe, 1.3: heavy). mats: {part: slot} ('thigh', 'shin', 'foot', 'upper_arm', 'forearm', 'hand', 'hips',
    'torso', 'head'), default Body. skip: parts left out (a unit drawing its own)."""
    mats = mats or {}
    M = lambda part: mats.get(part, 'Body')
    def side(s, k, X):
        if 'thigh' not in skip: m.box(X(0.1 - 0.065 * w, 0.1 + 0.065 * w), (-0.07 * w, 0.07 * w), (0.48, 0.88), 'thigh.' + s, M('thigh'))
        if 'shin' not in skip: m.box(X(0.1 - 0.055 * w, 0.1 + 0.055 * w), (-0.06 * w, 0.06 * w), (0.1, 0.48), 'shin.' + s, M('shin'))
        if 'foot' not in skip: m.box(X(0.1 - 0.06 * w, 0.1 + 0.06 * w), (-0.17, 0.07), (0.0, 0.1), 'foot.' + s, M('foot'))
        if 'upper_arm' not in skip: m.box(X(0.27 - 0.055 * w, 0.27 + 0.055 * w), (-0.055 * w, 0.055 * w), (1.14, 1.42), 'upper_arm.' + s, M('upper_arm'))
        if 'forearm' not in skip: m.box(X(0.27 - 0.05 * w, 0.27 + 0.05 * w), (-0.05 * w, 0.05 * w), (0.88, 1.14), 'forearm.' + s, M('forearm'))
        if 'hand' not in skip: m.box(X(0.225, 0.315), (-0.05, 0.05), (0.775, 0.885), 'hand.' + s, M('hand'))
    sym(side)
    if 'hips' not in skip: m.box((-0.16 * w, 0.16 * w), (-0.095 * w, 0.095 * w), (0.86, 1.06), 'hips', M('hips'))
    if 'torso' not in skip: m.box((-0.18 * w, 0.18 * w), (-0.1 * w, 0.1 * w), (1.04, 1.44), 'spine', M('torso'))
    if 'head' not in skip: m.box((-0.1, 0.1), (-0.115, 0.1), (1.46, 1.7), 'head', M('head'))


def held_blade(m, side, length, width=0.022, grip=0.15, guard=0.06, mat='Metal', point=0.1):
    """A straight blade along a fist's front (forward, -Y), standard skeleton: pommel, grip, guard, blade, point."""
    gx, gz = FIST[side]
    g = 'hand.' + side
    m.cbox((gx, grip * 0.55, gz), (0.018, 0.018, 0.018), g, mat)
    m.taper(grip * 0.45, -0.08, (gx, gz), 0.014, 0.016, 0.014, 0.016, g, mat)
    if guard > 0: m.cbox((gx, -0.09, gz), (0.016, 0.012, guard), g, mat)
    m.taper(-0.1, -0.1 - length, (gx, gz), 0.007, width, 0.006, width * 0.8, g, mat)
    m.taper(-0.1 - length, -0.1 - length - point, (gx, gz), 0.006, width * 0.8, 0.002, 0.002, g, mat)


def etch_tiles(style=0):
    """Eight etching tiles (for make_etchings / a unit's ETCHINGS) from a small library of patterns: script, rings,
    chevrons, a spiral, eyes, a lattice, veins and dots. 'style' picks which variant of each (so two units differ)."""
    import numpy as np
    def px(x, y): return x * 128.0, y * 256.0
    def h(a, b): return np.mod(np.sin(a * 12.9898 + b * 78.233 + style * 3.7) * 43758.5453, 1.0)
    def script(x, y):
        X, Y = px(x, y); cw = 26 + 6 * style; cx, cy = np.floor(X / cw), np.floor(Y / cw)
        u, v = np.mod(X, cw) / cw, np.mod(Y, cw) / cw; k = h(cx, cy)
        bar = (abs(u - 0.5) < 0.09) & (v > 0.15) & (v < 0.85); hor = (abs(v - (0.3 + 0.4 * k)) < 0.08) & (u > 0.15) & (u < 0.85)
        dia = (abs(u - v) < 0.1) & (u > 0.15) & (u < 0.85)
        return np.where(k < 0.5, bar | hor, dia | (bar & (v > 0.5)))
    def rings(x, y):
        X, Y = px(x, y); cy = np.round((Y - 64) / 128) * 128 + 64
        return np.mod(np.hypot(X - 64, Y - cy), 14 + 2 * style) < 4
    def chevrons(x, y):
        X, Y = px(x, y); return np.mod(Y + abs(X - 64) * (0.8 + 0.3 * style), 28) < 7
    def spiral(x, y):
        X, Y = px(x, y); dx, dy = X - 64, Y - 128; r = np.hypot(dx, dy); th = np.arctan2(dy, dx)
        return (np.mod(r - th * 16 / (2 * np.pi), 16) < 5) & (r < 62)
    def eyes(x, y):
        X, Y = px(x, y); cy = np.round((Y - 43) / 85) * 85 + 43; e = ((X - 64) / 52) ** 2 + ((Y - cy) / 24) ** 2
        return ((e > 0.72) & (e < 1.0)) | (np.hypot(X - 64, Y - cy) < 10)
    def lattice(x, y):
        X, Y = px(x, y); return (np.mod(X + Y, 32) < 4) | (np.mod(X - Y, 32) < 4)
    def veins(x, y):
        X, Y = px(x, y); w = 64 + 30 * np.sin(Y / (18 + 4 * style)) + 10 * np.sin(Y / 7.0)
        return (abs(X - w) < 4) | (abs(X - w * 0.5) < 3) | (abs(X - (128 - w * 0.4)) < 3)
    def dots(x, y):
        X, Y = px(x, y); return np.hypot(np.mod(X, 22) - 11, np.mod(Y + np.floor(X / 22) * 11, 22) - 11) < 4
    lib = [script, rings, chevrons, spiral, eyes, lattice, veins, dots]
    return [lib[(i + style) % 8] for i in range(8)]
