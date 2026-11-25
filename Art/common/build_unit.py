# Builds a unit's model file from scratch, starting from the Fighter's rig. Run from the project folder:
#   blender -b Art/Fighter/Fighter.blend --python Art/common/build_unit.py -- <Unit> [preview.png]
# It runs Art/common/unit_tools.py and Art/<Unit>/<unit>.py (which defines build_mesh(), build_clips() and optionally
# ETCHINGS and PREVIEW), then saves Art/<Unit>/<Unit>.blend and exports Assets/Models/<Unit>/<Unit>.fbx.
# This overwrites the unit's .blend: hand edits made there are lost. After hand edits, export from Blender instead
# (Art/README.md has the settings).
import bpy, os, sys

args = sys.argv[sys.argv.index('--') + 1:]
unit = args[0]
root = os.path.abspath(os.path.join(os.path.dirname(bpy.data.filepath), '..', '..'))
art = os.path.join(root, 'Art')
g = {'__name__': 'unit_build'}
exec(compile(open(os.path.join(art, 'common', 'unit_tools.py')).read(), 'unit_tools.py', 'exec'), g)
script = os.path.join(art, unit, unit.lower() + '.py')
exec(compile(open(script).read(), script, 'exec'), g)

g['make_unit'](unit, g.get('SKELETON'), g.get('EXTRA_BONES'))
verts, faces = g['build_mesh']()
g['build_clips']()
rig = g['rig_obj']()
rig.animation_data.action = bpy.data.actions['Idle']
bpy.context.scene.frame_set(0)

models = os.path.join(root, 'Assets', 'Models', unit)
os.makedirs(models, exist_ok=True)
if 'ETCHINGS' in g:
    g['make_etchings'](os.path.join(models, unit + 'Etchings.png'), g['ETCHINGS'])
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(art, unit, unit + '.blend'), check_existing=False)
g['export_fbx'](os.path.join(models, unit + '.fbx'))
print('BUILT %s: %d verts, %d faces, actions: %s' % (unit, verts, faces, ', '.join(sorted(a.name for a in bpy.data.actions))))
if len(args) > 1 and 'PREVIEW' in g:
    g['preview'](args[1], g['PREVIEW'], **g.get('PREVIEW_VIEW', {}))
    print('PREVIEW ' + args[1])
