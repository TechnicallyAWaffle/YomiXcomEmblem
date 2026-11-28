# Unit models and animations

Source files live here, outside `Assets/`, so Unity doesn't try to import `.blend` files. Each unit gets a folder:

| What | Where |
|---|---|
| Blender source (model, rig, every animation) | `Art/<Unit>/<Unit>.blend`, e.g. `Art/Fighter/Fighter.blend` |
| What Unity uses (exported) | `Assets/Models/<Unit>/<Unit>.fbx` |
| Etching pattern (optional) | `Assets/Models/<Unit>/<Unit>Etchings.png` |
| Animator Controller (made automatically) | `Assets/Models/<Unit>/<Unit>.controller` |

`<Unit>` is the unit's **Display Name** (Fighter, Duelist, Tank...). That is how Unity matches the model to the unit.

## The Fighter file

- `FighterRig`: the armature. 15 body bones (hips, spine, head, upper_arm / forearm / hand .L/.R, thigh / shin / foot
  .L/.R) and 3 weapon bones: `sword` (right hand), `dagger.L`, `dagger.R`.
- `FighterBody`: one mesh of boxes (208 vertices); each box is weighted 100% to one bone. Material slots:
  - `Body`: becomes the team's colour in Unity.
  - `Metal`: the gauntlets and the weapons.
  - `Etched`: the upper arms and forearms. In Unity it is the team's colour with the etching pattern's dark lines on it.
    The pattern is `FighterEtchings.png`, 8 tiles of 128x256; each side of an arm box shows one tile (UV-mapped).
- **Summoned weapons:** the weapons are part of the mesh, on their own bones. Every action keys their **scale**:
  0 hides a weapon, 1 shows it. A weapon appears when an ability's windup scales it up, and is gone once its strike
  scales it back to 0. Any new action must key the weapon bones' scale too (0 if it uses none). Otherwise Unity
  shows them at full size.
- **Facing:** the character looks down Blender's **-Y** (front view). Keep it that way; the export turns it into
  Unity's forward.
- **Animations** are **Actions**, one per clip. In Blender, open the **Action Editor** (Dope Sheet > Action Editor),
  pick the action from the drop-down, and edit its keys. 30 fps.
- **Optional scripts:** `fighter_tools.py` and `fighter_poses.py` are the scripts the mesh and the poses were made
  with: two-bone IK for limbs and aim directions for hands and weapons. You don't need them to edit by hand. Re-running
  `build_all()` rebuilds every action and overwrites hand edits.
- **Backup:** `Fighter_v1_backup.blend` is the first version, with the sword. Delete it if you don't need it.

## The other classes (Duelist, Tank, Sharpshooter, Paradigm, Strategist)

These are basic models, built by script from the Fighter's rig: the same 15 body bones (so the same tools and pose
code work), each with its own build, gear and weapon. `class_models_preview.png` shows them all in Unity (rows:
Idle, Run, Block, signature windup, signature strike, Death).

| Unit | Look | Its own clips (besides the shared set) |
|---|---|---|
| Duelist | Lean fencer in a split coat, one long straight blade | `finite_blade` (Finite Blade), `gouge` (Gouge, with a `.Dash`), `unsigned_rebuttal` (Parry; also its `Block`) |
| Tank | Heavy plate, flanged mace, tower shield | `mace_strike` (Crush), `shockwave` (Tremors). Shared Windup / Strike: a shield bash |
| Sharpshooter | Hood, short cape, long scoped rifle held two-handed | `rifle_shot` (Fire Rifle), `snipe` (Precision Shot, kneeling) |
| Paradigm | Robed caster, every bit of skin Etched (`ParadigmEtchings.png`), halo frame | `spark` (Spark), `cataclysm` (Cataclysm) |
| Strategist | Long coat, mantle, crown, gem-topped scepter, three Makina shards | `scepter_of_tyranny`, and one construct clip under all four Makina abilities |

Unlike the Fighter's summoned weapons, these weapons are always out: they're weighted straight to the hand bone, so
there are no weapon bones and nothing to key for them. A held weapon runs along the fist's front, so a pose aims it
with the hand's `blade` direction. The Tank's shield hangs from the left fist: the hand's `dir` is the shield's long
axis and its `fwd` is where the shield faces.

**The scripts:**
- `common/unit_tools.py`: the shared helpers. Boxes weighted to bones (`MeshBuilder`), IK posing (`solve`, `key`),
  aims in the chest's frame (`cdir` / `cblade`), and shared Run, dash and reaction clips built from each unit's stance.
  It also generates etching patterns, exports the FBX, and renders preview sheets.
- `<Unit>/<unit>.py`: that unit's mesh (`build_mesh`), its stance and its clips (`build_clips`).
- `common/build_unit.py`: builds one unit from scratch and exports it:
  ```
  blender -b Art/Fighter/Fighter.blend --python Art/common/build_unit.py -- Duelist [preview.png]
  ```
  This **overwrites** `Art/<Unit>/<Unit>.blend` and the FBX. Hand edits made in the `.blend` are lost. Once you
  start editing a unit by hand, export from Blender instead (below), and stop using the script for that unit.

## The enemies

Built the same way (`Art/<Unit>/<unit>.py`, `common/build_unit.py`). Each has the shared clips (Block left out for the
Howler, which can't guard) and clips for its own abilities:

| Unit | Look | Its own clips |
|---|---|---|
| Assassin | Lithe, hooded and masked, a dagger | `assassin_strike`, `hidden_strike` (with a `.Dash`) |
| Brutebeast | Short legs, a huge right arm it knuckle-walks on (own skeleton) | `ravage`, `ravage_followup`, `charge` |
| Caster | Tabard and cowl, etched arms | `kinetic_blast`, `distort_space` |
| Exemplar | Plate with gold trim, longsword, two wings (wing bones) | `spark_the_flame`, `intrepid_advance` / `light_the_pyre` (with `.Dash`), `split_apart_the_sky` |
| Healer | Robed, a tall staff, a ring over its head | `deluge_of_creation`, `repel` |
| Monarch | Twisted and lopsided, four wings (wing bones) | `divine_javelin`, `incineration`, `horde_of_memories` |
| Primavaeran | Etched all over | `tear_apart_reality`, `recurrent_repel` |
| Sniper | Long coat, visored helmet, a long rifle | `sniper_rifle_shot`, `sniper_marking_shot`, `sniper_snipe` (kneeling) |
| Wraith | Dark and smoky, long claws, churning tatters (wisp bones) | `rend` |
| Zola | A quarter taller (own skeleton), a huge banded, bolted head | `stomp`, `mental_overload` |
| Skirmisher (Eviscerim) | Light plates, a short blade in each hand (read from its kit: no look was given) | `skirmisher_strike`, `flowing_death` |
| Howler | A canine quadruped with a plated spine, metal jaw, glowing eyes (own skeleton: on all fours, plus a tail) | `ravenous_jaws`, `howling_lament` |
| Matriarch | A floating jellyfish: a bell of mismatched slabs and gems round a glowing core, six tentacles (own skeleton, plus two tentacle bones) | `needlestorm`, `carapace`, `captivate` |

## The Forsaken: two models

The Forsaken has a model for each form. `Art/Forsaken` is the **Composed** form: a tall, gaunt dark figure floating half a
metre up, a pale spiral face, bird-skull masks on its cloak, bone crests, a glowing orb, a gold halo, smoke tendrils
(`wisp.*` bones). `Art/ForsakenDiverged` is the **Diverged** form: a floating larva of pale segments bound in black,
eyes everywhere (some on stalks), a black crown with two fans (`crown.*`), four tentacle arms (the arm and leg bones),
ribbons (`ribbon.*`) and a twisting tail (`tail.*`). Build each with `build_unit.py` (`-- Forsaken`, `-- ForsakenDiverged`).

| Form | Its own clips |
|---|---|
| Composed | `divulge`, `fray_the_mind`, `wretched_visions`, `simplicity`, `neurosis.Windup` (the change, clutching its head) |
| Diverged | `devour`, `rampage`, `blind_rampage`, `unleash`, `howling_end`, `berserk.Windup`, `regression.Windup` (curling in) |

**How Unity uses them:** a model in `Assets/Models/<Unit>Diverged/` is that unit's Diverged form. On import,
`UnitModelImport` puts it in the unit's **Diverged Model** and **Diverged Animations** fields, not Model. In battle,
`UnitLooks` builds both models under the view and shows the Composed one. On each form change, `ForsakenForm` swaps
which model is shown and which Animator the view plays.


**What the toolkit adds for them** (`common/unit_tools.py`):
- `SKELETON = {bone: (head, tail)}` in a unit's script lays the same 15 bones out differently (a beast on all fours, a
  giant, a floating bell); `EXTRA_BONES = {bone: (head, tail, parent)}` adds bones of its own (wings, tentacles,
  tatters), posed with `{'yaw', 'lean', 'tilt'}` relative to their parent. Unity plays any skeleton (Generic rigs).
- `planted(spec, side, target, pole)`: a hand placed at a point in the world (a paw, a knuckle on the ground) rather
  than relative to the chest.
- `humanoid(m, w, mats, skip)` and `held_blade(m, side, length)`: a plain body and a straight blade, for the standard
  skeleton. `etch_tiles(style)`: eight etching tiles from a small pattern library.
- Material slots past Body / Metal / Etched: `Dark` (near black: smoke, shadow), `Glow` (the team's colour, glowing:
  eyes, cores, claw tips), `Gold`, `Bone` (pale ivory: masks, bone, pale flesh) and `Eye` (bright white). Unity's
  `UnitLooks` maps them by name.
- `PREVIEW_VIEW = {'ortho': ..., 'height': ...}` zooms the preview sheet for a small or tall unit.

Unit models are imported Read/Write (`UnitModelImport`): the tactical view (Alt) draws their edges from the mesh.

## The clips Unity plays

Shared clips, for every unit:

| Action name | When it plays | Loops |
|---|---|---|
| `Idle` | standing (the Fighter: a light boxer stance) | yes |
| `Run` (or `Walk`) | moving | yes |
| `Windup` | an ability without a windup clip of its own; stretched to fit the windup's length (an ability with no windup but a `.Windup` clip of its own, like a Transcended Parry, plays that at normal speed) | no |
| `Strike` | that ability landing | no |
| `Block` | raising Block; held about a second | no |
| `DashForward` / `DashBack` / `DashSide` | a dash or dodge, by where it goes against where the unit faces (see below) | no |
| `HitLight` / `HitHeavy` | taking a light / heavy hit (an Unravel plays `HitHeavy`) | no |
| `Interrupted` | its windup broken (this also cancels an ability's own windup) | no |
| `Stun` | while stunned | yes |
| `Death` | going down; holds its last frame | no |

**How dashes pick a clip:**
- **Forward:** within 50° of the way the unit faces. The unit turns to face where it goes.
- **Back:** more than 130° away. The unit keeps facing the way it was, so it hops backward.
- **Side:** anything else, also keeping the unit's facing. `DashSide` is animated going to the character's right;
  a dash to the left plays it mirrored.
- **A dodge in place** (no dash) plays `DashSide`.

**An ability's own clips** are named `<ability id>.<phase>`. The ability id is the `Id` field on its
ActionDefinition asset; Rend The Flesh's is `slash`. Make any of these phases:

| Phase | When it plays |
|---|---|
| `<id>.Windup` | its windup, stretched to fit the windup's length and held until it lands or is interrupted |
| `<id>.Dash` | for an ability that dashes (Phantom Strike): when it goes, then straight into `.Strike` |
| `<id>.Strike` | the hit and its recovery; back to `Idle` / `Run` when it ends |

An ability without its own clips uses the shared `Windup` and `Strike`. The Fighter has `slash.Windup`,
`slash.Strike` (Rend The Flesh), `phantom_strike.Windup`, `phantom_strike.Dash` and `phantom_strike.Strike`.

The names must match exactly (case too). Any of them can be left out; extra actions are imported but not played.

## Editing and updating

1. Open `Art/<Unit>/<Unit>.blend` and edit the mesh, rig or actions. Save.
2. **File > Export > FBX (.fbx)**, to `Assets/Models/<Unit>/<Unit>.fbx` (overwrite it), with:
   - Limit to: **Selected Objects**: select the rig and the mesh first. Object Types: **Armature** and **Mesh**.
   - Transform: Forward **-Z Forward**, Up **Y Up**, Apply Scalings **FBX All**, **Apply Transform** on.
   - Geometry: Smoothing **Face**.
   - Armature: **Add Leaf Bones** off.
   - Bake Animation: on; **All Actions** on, **NLA Strips** off, **Force Start/End Keying** on, Simplify **0**.
   - Save these as an Operator Preset (the + by the preset list) so the next export is one click.
3. Switch to Unity. It reimports the FBX and the `UnitModelImport` script (`Assets/Scripts/Editor/UnitModelImport.cs`):
   - names the clips after the actions and loops Idle / Run / Stun;
   - rebuilds `<Unit>.controller` from the clips, including a state for each ability clip;
   - sets the unit's `Model`, `Model Animations` and `Model Etchings` fields (on its UnitDefinition asset).

   The Console logs which clips it found. The next battle uses the new version.

**To change the etchings,** edit `Assets/Models/<Unit>/<Unit>Etchings.png` in any image editor. Keep the 4x2 tile
layout, or move the UVs in Blender to match a new layout.

**To tune how a clip plays** (blend times, how long Block holds), change `UnitModelImport.BuildController`. A
hand-edited controller is rebuilt on every import.

**To give a new unit a model,** make `Art/<Unit>/` and export to `Assets/Models/<Unit>/<Unit>.fbx`; the same
import hooks it up.
