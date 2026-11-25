# Combat VFX

The art direction comes from the reference board:
- **Ink:** hard-edged black shards, splats and spikes with coloured edges, which erode away instead of fading.
- **Light:** thin radiant lines.
- **Rings and sigils:** brushy painted rings, and rune circles.
- **Speed:** colour-split smears.

Everything is drawn from code, driven by the battle's timeline events, so effects stay in step with playback, slow
motion and replays.

## Where things are

| What | Where |
|---|---|
| The shader (every VFX material uses it) | `Assets/Resources/VFX/ApotheomVfx.shader` |
| The textures | `Assets/Resources/VFX/Tex/*.png` |
| The texture generator | `Art/VFX/vfx_textures.py` |
| Texture import settings | `Assets/Scripts/Editor/VfxTextureImport.cs` (linear, uncompressed, `*_tile` repeats) |
| Materials, particle systems, decals and billboards | `Assets/Scripts/Presentation/VfxKit.cs` |
| The ink look: hits, rays, rings, sigils, smears, status icons | `Assets/Scripts/Presentation/CombatVfx.Ink.cs` |
| Which effect plays on which event, and the 9 ability effect types | `Assets/Scripts/Presentation/CombatVfx.cs` (`OnEvent`, `PlayResolve`, `DrawWindups`, `DrawStatuses`) |

## The shader's three modes (made in `VfxKit.Material`)

- **Ink:** alpha-blended. The shape is near-black with the effect's colour on its edges (texture R), and it erodes
  away by the texture's noise (G) as its alpha drops. Used for shards, splats and ground stains.
- **Glow:** additive and boosted past the bloom threshold. Used for rays, sparks, sigils, halos and bright strokes.
- **Paint:** alpha-blended in the effect's colour. Used for brush rings, dark stroke underlays, dust and status rings.

## Textures

Every texture packs R = edge band, G = dissolve noise, and A = shape.

| Texture | Used for |
|---|---|
| `shard` | Ink shards thrown off hits and deaths |
| `splat` | Ink bursts and ground stains |
| `rays` | Radiant line bursts |
| `ring` | Brush rings |
| `sigil` | Rune circles |
| `stroke_tile` | Brushy lines, repeated along the line |
| `halo` | Halos |
| `glyphs` | Status icons, 4 cells: faster, slower, watching, held |
| `puff` | Dust and sparks |

**To change a texture:** edit `vfx_textures.py` and run it. You need Python with numpy and pymupdf
(`pip install numpy pymupdf`). Or paint over the PNG directly in an image editor, keeping the channel layout.

## What plays where

- **Each ability's effect:** its `Visuals` on the ActionDefinition asset (an effect type and a colour) picks one of the
  9 types: Tracer, Bolt, Slash, Beam, Shockwave, Chain, Heal, Buff, Lob.
- **Hits:** shards, a splat, and rays for a heavy or critical hit. Crits are gold and weakpoint breaks are gold.
- **Defence:** blocks and shields get a pale flash, parries a white burst and halo, and dodges and dashes a cyan and
  magenta smear.
- **Unravel and death:** a violet ink burst for an Unravel, and black ink with crimson edges and a stain for a death.
- **Windups:** a rune sigil under the unit that turns faster as it charges, orange for heavy attacks. Area attacks show
  a painted edge plus a sigil filling the area.
- **Statuses:** a brush ring at the feet, glyphs over the head (faster, slower, watching, held), and a halo for
  important statuses. The list of which status shows which glyph is `StatusGlyphs` in `CombatVfx.Ink.cs`.

## Performance

The effects are built to stay cheap:
- one shader and a handful of shared materials;
- pooled particle systems, decal quads and line renderers;
- capped particle counts, and nothing allocated per effect beyond small arrays.

Without `Resources/VFX` everything falls back to the old plain lines and dots.

## Previewing

`CombatVfx.PreviewEvent`, `PreviewResolve` and `PreviewLearn` play any effect on demand: from a debug tool, or from
the Unity CLI while the game runs. `base_pass_preview.png` shows frames from the base pass.
