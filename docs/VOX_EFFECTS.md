# VOX game effects

Everything the editor authors for the game's VOX format beyond notes and camera: post effects, lane commands,
game-asset rows, `#BPM OPTION` and note scripts. Exported by **ksh2vox 0.20.0** (older converters keep only `bg_no`,
stage-0 post effects and the raw `sp_controller` rows).

**Only what the game executes is offered.** The rule set comes from reverse-engineering the VOX parser and its
consumers in the game's DLL (game version 2026071400) and a survey of all 8,602 official charts. The game *parses*
many more SPCONTROLER names, then never reads them:

| Never read (not offered, ksh2vox drops them with a warning) | Why it matters |
|---|---|
| `LaneX`, `GEO_RotZ`, `GEO_RotX`, `Tilt_Mag`, `3D_Scroll`, `Morphing0/1/3`, `HudY`, `AIRL/AIRR_ScaX`, `AIRL/AIRR_PosX`, `CAM_PosY/PosZ`, `CAM_rotX`, `BgSpeed`, `BGROT`, `3DBG_*`, `EXBG_*` (SPC), `3DNotes`, `2DNotes` | would be invisible: `zoom_side`, `center_split` and `rotation_deg` therefore stay unrepresentable |
| SpecialN `LANEMODEL0..3`, `LANEDISP`, `SIDE`, `ADJRVZ`, `EXBGROTF`, `BGDISP`, `BIL_H`, `EFFECTL/R_*`, `EXBG_Frame/Speed`, `3DBG_Blend`, `4ObjMode` | same; the official tutorial chart itself says ADJRVZ lost its meaning |

`AIRL_ScaX` / `AIRR_ScaX` / `Realize` remain in the converter template's base rows (inert / parse-time), unchanged.

## Model (Core `Charts/VoxExtras.cs`, `VoxEffects.cs`, `VoxEffectEvaluator.cs`, `VoxScripts.cs`)

| Piece | Role |
|---|---|
| `VoxExtrasInfo` | `BgNo`, `PostEffects`, `SpController` (raw base rows that *replace* the template's), `Commands` (game commands, *appended*), `RepresentativeBpm`, `ConstantScroll`, `Scripts`, `Scripted` |
| `PostEffectRow` | `Y`, `Name`, `Param`, `From`, `To`, `Length` (pulses) or `LengthMs`, `Stage`, `Index`. The official typo `ChromaticAbberation` is normalised to `ChromaticAberration`. |
| `VoxCommandRow` | `Y`, `Name`, `Type`, `Length` (pulses), `P1..P4` as **VOX tokens** (`1024.00`, `d-45.00`, `LANECOL`, `xFFFFFFFF`, `i0`) |
| `PostEffects` / `PostEffectPreset` | the 8 effects in game-id order, parameters with ranges and official values, preview support; presets (Negative flash, Grayscale/Sepia section, Hue sweep, Blur hit, RGB split hit, Shake, Noise burst, CRT section, Glow fade) |
| `VoxCommands` | the readable commands (LaneY, BIL_RotZ, Morphing2, BAROFF, BAR, SpecialN kinds) with value kinds and the ids seen in official charts as pickers; `Dead` |
| `VoxToken` | token parsing (`f` float, `i` int bits, `x` hex, `d` degrees, names) and formatting |
| `GameBackgrounds` | every official `bg_no` (id, songs, first song id, "post effects" = an official post-effect song uses it). Ids only, no game asset. |
| `VoxEffectEvaluator` | values at a song time, with the game's rules (below) |
| `ColorConversionMath` | the game's five ColorConversion shaders as a per-colour function |
| `VoxScriptSyntax` / `VoxScriptEval` | statement validation (same rules as ksh2vox `script_errors`) and evaluation for the preview |
| Editing `VoxEffectEditing` | pure edits; every UI action = one `CameraCommands.SetVoxExtras` (exact undo) |
| Validation `EffectChecks` | chart-check rules `effect.vox.*` (post effects on a background without stages, unknown effect/parameter, dead/unknown command, bad token, BAROFF mid-measure, Morphing2 experimental, script problems) |

Timing remaps (`RemapPulsesCommand`) move post-effect and command rows with `VoxExtras`, scripted-note entries with
`NoteBound`.

## Semantics (what the game does, as far as known)

* **#POSTEFFECT** `pos type len Effect stage index Param from to` (exactly 9 fields). `type` is parsed, never read
  (written `2`). `len` = ticks or `<n>ms`; `0` = instant. The parameter tweens from → to, then holds. `stage` =
  post-effect stage of the song's **background** (0 = background "BgEffect", 1 = "Overall", the lane); `index` = the
  instance of that effect in the stage.
* **Stages exist only where the background defines them** — every official post-effect chart has its own
  `bg_no` (92–118, one on 5). On `bg_no 0` post effects may show nothing (in-game test pending). The effects view and
  the chart check warn.
* **ColorConversion** Mode 0 grayscale, 1 sepia, 2 negative (1 − rgb), 3 negative + complement (brightness inverted,
  hue kept), 4 hue rotation (Strength = degrees 0..360); Strength 0..100 %, ≤ 0 skips the pass;
  `out = raw·(1−p) + dest·p`.
* **SPCONTROLER** rows `pos Name type dur p1 p2 p3 p4` — **exactly 8 whitespace fields** or the game drops the row.
  Tweens start from p1 when `type & 0xF == 2`, else from the current value. LaneY: value/1024, 0 shown, ±1024 moved
  out (direction to be tested). BIL_RotZ: `d` degrees (target to be tested). Morphing2: ×0.125, shape unknown.
  BAROFF ON/OFF works per measure; BAR forces a bar line. LANECOL / LANECLEARCOL `xAARRGGBB`, instant.
* **#BPM OPTION** `RepresentativeBpm <f>` (the tempo the hi-speed is based on), `ConstantScroll 0|1`, after `#BPM INFO`.
* **Scripts** `#SCRIPT_DEFINE` `@SCRIPTSTART id` … `@SCRIPTEND`, `#SCRIPTED_TRACKn` rows `pos id [id…]` attach to the
  note starting exactly there on track n (1 laser L, 2 FX-L, 3..6 BT, 7 FX-R, 8 laser R). Steps are 1/48 beat.

## Storage

### kson — `impl["sdvx-editor"].vox`

```json
{
  "bg_no": 105,
  "post_effect": [[y, "ColorConversion", "Strength", 0, 100, 480, 1, 0], [y, "RandomShake", "none", 20, 0, 0, 0, 0, 500]],
  "sp_controller": [[y, "Realize", 3, 0, 17.12, 60.12, 110.12, 0]],
  "commands": [[y, "LaneY", 2, 240, "0.00", "1024.00", "0.00", "0.00"], [y, "SpecialN", 0, 0, "LANECOL", "xFFFFFFFF", "i0", "i0"]],
  "bpm_option": {"RepresentativeBpm": 174, "ConstantScroll": true},
  "scripts": [[7, ["$offsetX = $offsetX + 0.178 when ( $targetStep - $currentStep ) > 48"], "slide in"]],
  "scripted": [[y, 3, [7]]]
}
```

`post_effect` rows: `[y, name, param, from, to, length]` + `[stage, index]` when not 0/0 + `[length_ms]` when set
(0.17 readers take the first six). Pulses are kson pulses (240 per quarter).

### ksh

* Header `bg_no=<n>`.
* `lane_toggle=<len>` (K-Shoot's own option, len in 1/192 of a measure) for a LaneY row that is a plain toggle
  (shown → `0 → 1024`, hidden → `1024 → 0`, type 2); read back alternating.
* Comment lines **after the first `--`** (never in the header: ksh2vox's header loop reads a `//…=…` line as a key),
  at the row's own chart line; K-Shoot skips them:

```
//#vox_pe|stage|index|len|Effect|Param|from|to      len = ticks (1/192 measure) or "<n>ms"
//#vox_sp|Name|type|len|p1|p2|p3|p4
//#vox_bpm|RepresentativeBpm|174
//#vox_script|id|statement                          one line per statement
//#vox_scripted|track|id[,id…]
```

Lost in ksh: script names, row lengths finer than 1/192 measure, the raw `sp_controller` base rows.

## Editor (camera tab → **effects** view, `Screens/Edit/Camera/`)

| Piece | Role |
|---|---|
| `EffectLanes` + `EffectLaneEditor` | the effect lanes on the compose time axis: post · background, post · lane (one column per effect: CA CC RB SN CRT LUM FL SH), LaneY, BIL_RotZ, Morph2, colours (lane / fill), bar lines, assets, scripts. Click adds (post: the column's effect, CC adds Mode + Strength with the brush mode; tween lanes from the value there to the pointer; bars ON/OFF per measure, Shift = BAR), drag right after = length (+ end value), drag a row's start = move, its end = length, right click deletes. One undo step per gesture. |
| `CameraTabContent.Effects.cs` | game background picker + warning, inspector of the selected row (value slots with official-id pickers, stage, instance, beats / ms, type), brushes, presets, `#BPM OPTION`, note scripts (templates, edit, assign to the compose selection) |
| Song Setup → VOX export | the same `bg_no` list |

## Preview (playtest F5 and the camera preview)

| Effect | Preview |
|---|---|
| ColorConversion (all modes) | **exact**: the game's shader maths per vertex colour (`ColorConversionMath`); stage 1 → playfield, stage 0 → background; HUD untouched |
| ChromaticAberration, RadicalBlur, LuminousEffect | approximate: additive ghost copies of the playfield (scaled red/green copies, scaled copies about the centre, a brightened copy) |
| SimpleNoise, CrtMonitorEffect, RandomShake | approximate: noise specks, scan lines + dark edges, a random offset |
| LaneY, LANECOL, LANECLEARCOL | approximate: track slid by value/1024 of the screen height, lane colours multiplied, a fill over the lanes |
| BAROFF / BAR | exact (bar lines hidden / added) |
| Note scripts | `$targetStep` and `$offsetX` on chips (playtest) |
| SetFrameLabel, BIL_RotZ, Morphing2, SpecialN assets (FRAME, SE, SE2, LIVE2D, NEMSYS, SIDEBG, EXMC_*, EXBG*, RENDER_PRESET, DISABLE_PRE_EFFECT_LINE) | game only (labelled in the inspector) |

## In-game tests still open

1. Any post effect on bg 0 / a common background vs. its own (e.g. 105).
2. Which stage covers the lane vs. the background; is the HUD untouched?
3. LaneY direction (+1024 / −1024); Morphing2 shape; BIL_RotZ target.
4. ConstantScroll; DISABLE_PRE_EFFECT_LINE / RENDER_PRESET.
