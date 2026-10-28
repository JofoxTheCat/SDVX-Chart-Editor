# Changelog

## 0.1.0 — unreleased

- **UI audit fixes (2026-10-28).** A screenshot audit of every screen found 50 problems (broken / ugly / polish);
  the ones not already handled by the panel, tutorial and Support work are fixed here.
  - **One number, time and date format** (Core `Editing/UiFormat`, README "Numbers, times and dates"): decimal dot
    whatever the Windows locale (the process runs with the framework Locale "en"; a German system used to show
    "327,42" in one widget and "327.42bpm" in the next), `mm:ss:mmm` for positions (playtest results and the hard-gauge
    crash used `m:ss.fff`), `m:ss` for lengths, `yyyy-MM-dd HH:mm` for dates, `10 000 000` for big integers. Input
    fields keep accepting a comma.
  - **Checkboxes are visible when off**: `PinkCheckbox` draws its ring over an always-drawn backing (a masked
    container paints its border only over drawn child pixels, so "Metronome clicks", "Show FPS", "Knobs as mouse",
    "show notes" and "play effects" looked like plain labels when unticked).
  - **Song select**: the radar + level column is scaled to the room between the details and the wheel (its bounds
    with all six labels were wider and covered the right-aligned values — "Tutoria", "Edito", the dates); long detail values (charter, illustrator, folder) are cut with "…" before the label and show the
    full text as a tooltip; header lines stop at the black corner; the wheel is clipped below the search line (the
    bar used to cover the top card and a group header's counts); a group whose header scrolled away stays pinned at
    the top ("By source" looked like it did nothing); the footer counter counts what the wheel lists ("1 of 5 songs ·
    2 charts shown" during a search, right after a drop import — Core `LibraryCountText`); the right-click popup's
    title sits on an opaque band (it was drawn over the white song header); Edit is no longer red like Delete; the
    synthesised "Tags" row (file names) is replaced by "Chart file"; "Imported / Last edit" became "Added / Last edit"
    with "not edited since" when the file is older than the import; a collapsed song shows its first charter + "+N";
    toasts moved bottom-left (they covered the wheel's cards) and the export toast names the file and opens its
    folder on click.
  - **Level estimate in plain words** (Core `LevelEstimation/EstimateText`): no feature ids ("s_0.05_0.1_w") or
    "training range" in Song Setup — "Mostly from: short bursts of notes …", "Less certain: unusual for an official
    chart: song length"; estimates above 20 read "20+" ("About 20+: harder than the game's highest level") (song select, Song Setup, playtest); song select says
    "uncertain" instead of a bare "?".
  - **Options**: a key that conflicts is shown in its slot (orange) while Swap / Clear other / Cancel is asked (the slot
    kept the old key while the text named the new one); the hidden reset icon keeps its column, so key boxes no longer
    jump; empty slots and the Keyboard Shortcuts list both say "not set" (was "—", "None", "(unbound)"); truncated
    labels show the full text as a tooltip (`TruncatingText`); slider values have their own column (the padding's
    `Horizontal` overwrote `Right`); the joystick steppers use the panel's dark style; long folder placeholders are
    cut in the middle.
  - **Dialogs dim what is behind them** (40 %), so a dialog over the timing panel or a dialog over a dialog reads as
    modal. The "song setup" tab lights up while its dialog is open; the camera tab's "effects" view is now
    "screen fx" (the top bar's effects tab is the audio FX page).
  - **Song Setup**: the red "quick note" in format jargon is a grey hint; Slot lives on the Difficulty page only;
    Advanced lost the caption that sat on the group border, the file line shows only known parts ("kson 1, saved by
    …"), "VOX rows … SPCONTROLER" became "Game effects: none (only charts from a .vox carry them)"; a new chart's
    title is "Untitled".
  - **Timing tab**: dark text on the yellow and light-blue buttons, the tools icon is a small corner mark instead of a
    watermark through "Timing Setup Panel", tooltips on the beat lamps, Tap here!, Reset and the metronome pill.
  - **Compose / bottom bar**: the 1/192 divisor value has its own column (the knob sat on it); the "no audio" stretch
    has a readable label only where it fits and the timeline's tooltip explains it and the marker colours; bookmark
    labels show above the timeline (clamped inside it) instead of on the marker lines; the measure:beat readout and
    the density column have tooltips, an empty chart says "no notes yet" there; tool hotkey hints 12.5 px; the
    spectral waveform mixes 40 % grey into every column (a pure tone was a solid #00E000 block); menu bar text 15.5
    (was 12.5, ~8 px on screen) with a 26 px bar; the volume overlay keeps clear of Hit Sounds and its captions grew.
  - **Inspector / chart check**: "at 00:02:000" instead of "pulse 960 · 2,000 s" (pulses in the tooltip), larger text,
    the chart check's help sits under its buttons.
  - **Playtest**: the status line is a two-part block in the left margin (it ran over the track), the chain counter
    sits above the track's far end (it collided with incoming lasers), "pace S" → "score pace S", the hi-speed basis
    in words; results: "RESULT (track crash)" on a crash, a stopped run says "no NEAR or ERROR so far" instead of
    "PERFECT so far" next to "would fail", "judgements" instead of "units", grouped numbers with spaces.
  - **Effects tab**: "No FX notes in view" now says that the old selection was kept; the laser filter switch list
    scrolls in its own box (max 240 px).
  - New difficulty: the toast says when a copy kept the source's level.
  - Tests: `UiTextTests` (formats under a de-DE culture, the counter, estimate wording, every feature of the shipped
    model has words). Smoke: counter after search and drop, detail values right of their labels, the wheel clip,
    the sticky group header, the popup band, the conflict slot and aligned key boxes, the lit song setup tab and the
    dialog backdrop, the divisor value column, the playtest status block / chain position / "20+", unticked
    checkboxes drawing their ring, the capped filter list, the options steppers' dark style, detail values ending before the radar column. `PinkCheckbox` no longer starts
    a transform off the update thread (a setting restored while the game disposes). The `dsp` part resets
    "play effects" first (the tutorial part ticks it and the setting persists; `dsp` failed after `tutorial`).
- **Hear the effects while editing (2026-10-27).** The effects tab's checkbox (was a stub) now plays the chart's audio
  effects on the song during editor playback and in the playtest; editor.ini `PlayEffects` (default off; off = no
  DSP on the audio at all).
  - Core `Audio/Effects`: `EffectValue` / `EffectValueSet` (kson value syntax, tempo-synced lengths = fractions of a
    whole note), `EffectSpecs` (parameters + defaults; open filter defaults from K-Shoot Mania v2), `EffectTimeline`
    (pure evaluation: given chart + song time + player state → the effect and its numbers per slot; K-Shoot v2 rules
    for precedence, triggers, laser value/mirroring, peaking delay, filter gain), `Dsp/*` (`EffectRack` + one DSP per
    kson type: retrigger, gate, flanger, pitch shift, bitcrusher, phaser, wobble, tape stop, echo, side chain, switch
    audio, peaking / low-pass / high-pass filters; laser filter curves from K-Shoot's tables).
  - Game `Audio/EffectAudioHost`: a BASS DSPPROC on the song's source decode stream (before the BASS_FX tempo
    stream), song time from the decode position, so effects sit sample-accurately on the music at any rate and
    latency; hitsound lead and audio offset unchanged. Small decoder position slips (MP3/OGG right after a seek,
    BASS_FX below 100 %) re-anchor the time instead of resetting the effects. Timeline rebuilt after edits.
  - Playtest: FX buttons / laser lock drive the effects (autoplay = the chart), with a grace window of 30 ms + the
    render lead (≈ 40 ms measured) so an on-time press is heard from the hold start.
  - Slam volume (chokkakuvol / kson `key_sound.laser.vol`) on the slam hitsound while effects are on.
  - Effects tab: "preview FX effect" / "preview laser filter" (2 s on the song from the cursor).
  - Tests: `AudioEffectTests` (parameter evaluation), `AudioEffectRenderTests` (offline renders on generated sine /
    noise: LPF −29 dB high band, peaking +16 dB at its cutoff, gate 4 gaps/s, retrigger loop repeats, sidechain
    1/ratio, bitcrusher hold, pitch shift octave, tape stop, echo repeats, wobble sweep, bit-identical without an
    effect, ≈ 75× real time for three slots). Smoke part `dsp` (real clicks on the checkbox and the preview, playback,
    50 % rate, playtest autoplay; counters, zero DSP exceptions; ≈ 0.45 % of real time in the app).
- **Editor UI fixes after the tutorial play-through (2026-10-27).**
  - **Two lasers of one side could start at the same moment** (screenshot: a lone slam 5 pulses before a diagonal).
    Cause: `OverlapRule` refused only a shared pulse, and free drags move in 5-pulse steps, so a drag against the next
    section stopped one VOX tick short of it. Now sections of one lane keep `OverlapRule.MinLaserGap` = 10 pulses
    (1/96 note = 2 VOX ticks = the closest ksh writes two sections at 192 lines) — measured: the 8,602 official .vox
    have no 2-tick gaps (0/1 tick 28× each, filter-switch artefacts), the local library 25 gaps of exactly 10 and none
    smaller. Placement, drawing, drags (with the message "… need a 1/96 gap"), paste, move, mirror, swap and the
    inspector all go through `CanPlace`; an edit of sections that are already closer (from a file) is allowed as long
    as it comes no closer. `RemapPulsesCommand` (delete range, resnap, keep notes in time) joins sections it pushes
    together (`JoinClose`; same pulse = a slam). Chart check: `laser.overlap` also sees a long section reaching past
    several, new warning `laser.gap`. Tests `LaserGapTests` (incl. all library charts).
  - **"select FX in view" did nothing visible**: it selected a measure before to six after the cursor (mostly off
    screen) and with an unchanged selection showed no change. Now it selects the FX notes the playfield on the left
    shows, says what it selected (or "No FX notes in view"), and the selection flashes (`ComposePlayfield.FlashSelection`).
  - **Effects tab reorganised** into groups with one-line explanations and a tooltip on every control: FX notes,
    FX chip sounds (built-in K-Shoot sounds vs imported files; "to selected chips" vs "as default"), FX hold effects
    (1 definitions, 2 assign, 3 parameter changes — its button is now "add at cursor"), laser filters (what each
    filter does; "at cursor" places a switch, each listed row is a switch and its x deletes it), filter gain, slam
    volume, peaking filter delay. The export table is a collapsed section ("what each format keeps").
  - **Background view: preview** like the camera tab's — the image (song-folder file, or a swatch for a KSM preset),
    the animation layer, a still of the movie at its offset, and bg_no as "game background N, first used by song …"
    (Core `MusicDbBackgrounds`, from the game's own music_db.xml via the launcher config). Every field explains itself;
    the export lines name the detected ksh2vox version (the old "Editor data for a later VOX export" / "ksh2vox 0.17.0
    and later" texts are gone).
  - **Spins / swings are bars**: click on the bar selects, the end drags the length (hold-tail hint), the bar moves
    it, right click deletes, one undo step each (`CameraLaneEditor.PatternEndAt`, `PatternTailHint`).
  - **Camera lane highlight**: the hovered / dragged / selected lane is outlined; `CameraLanes.ColumnFor(CamLane)`
    is one drawable per lane column (spotlight target).
  - **Song select**: the skill radar's bounds hold all six labels with their values (NOTES above, TRICKY below) and
    the labels sit further from the hexagon; Support the Artist is a full-width card under the details (heart, label,
    "Listen to or buy this song - N links", hover state; still hidden without a source).
  - Tooltips: `FlatButton`, `PinkCheckbox` and `PanelField` take `TooltipText`; `PanelControls.SubHeader` /
    `Collapsible`. Smoke part `panels` (real input for all of the above); song select / sources smoke check the
    radar bounds and the card.
- **Main tutorial overhaul after Johannes' play-through** ("explain more, more details, fix the highlighting";
  22 screenshots): 75 → 95 steps, ≈ 12¼ → 18¾ min (budget `MainTutorialPlan`, ceiling now 20).
  - **Numbered tasks** (`TutorialStep.Tasks`): a step with several actions lists them ("1 press X, 2 click Y"), ticks
    them off one by one and the spotlight moves to the current one — into dialogs (F6 Offset → "Jump to" → its value;
    Song Setup's Difficulty tab → Level; "Use estimate" → **OK**), menus and submenus (File → Open Difficulty for
    Reference → ADV; File → Export Song → ksh… / .svz), tool tiles before the chart region they are used on.
  - **Spotlight areas**: a step can highlight a screen rectangle — the chart stretch with the ghost notes (the wide
    laser's target no longer sits greyed out), a single camera lane column (never the whole lane panel), panel
    sections (background view), the skill radar with all six labels (`TutorialContext.PlayfieldRegion`,
    `CameraColumn`, `DeepRect`).
  - **Camera ghosts**: numbered rings in the camera lanes (zoom 1 → 2 → 3, the tilt point, the spin and its end),
    green once placed; a labelled yellow band marks the target bars in the playfield and over the camera lanes.
  - **Readable cards**: a small markup (`TutorialRichText`): keys in yellow, UI names in blue, the snap colours
    white / red / blue / purple and the laser colours drawn in their colours, bullets with a hanging indent.
  - **Exact checks** with feedback: BPM exactly 235, offset 682 ± 1 ms ("900 ms — needs 682 (218 too high)"), the
    F6 speed point at 16001 ± 1 ms with ×1.00 → ×1.25, level 5, camera anchors on their rings, the effect on THE FX
    hold, the filter switch at the laser, the spin at the slam and one bar long. The classic tutorial's BPM / offset /
    tempo-change / level checks are exact too.
  - **New explanations**: the toolbox, beat snap colours, the mini map, "When applying timing changes…", the Property
    Transformer, every camera column, automatic tilt modes (normal / bigger / biggest / zero / keep), spin / half
    spin / swing with a draggable end, K-Shoot backgrounds, bg_no / post effects / SPCONTROLER rows, the FX hold
    effects, the four laser filters, the switch list (its "x") and hearing them ("play effects during playback", "preview laser filter"), the background preview, modding timestamps (meant for feedback; paste
    "anywhere you can type text"; the "(Ctrl+V or Ctrl+V)" typo is gone).
  - **Options from song select**: chapter 7 goes back to song select before the options step — in the editor Ctrl+O
    is Open Difficulty; if that window opens by mistake the card says what it is and how to close it.
  - Smoke `tutorial`: every scripted click, drag and wheel turn of a step that names a target must land inside its
    spotlight, and a step's target must be on screen when it starts (highlighting cannot silently regress);
    `SDVXEDITOR_TUTORIAL_SHOTS=all` saves a screenshot of every step and every task. Smoke `tutorial` PASSED in 248 s
    (main 95 steps, 77 scripted, 126 pointer actions checked inside the spotlight; classic 69 / 64 / 56); full smoke
    (all 16 parts, AureoLe) PASSED in 1079 s. The tutorial's chapter rewrite retries for ~2 s when song select still
    reads a chart (a race seen once: "adv.kson is being used by another process").
- **VOX export ids stay above every real song**: with ksh2vox 0.20.1+ the export passes the launcher's plugin
  `music_db.json` **and** the game's `data/others/music_db.xml` as base dbs (found from the launcher config like the
  bridge does: `asphyxia_exe` → `plugins/*` with index.ts + handlers/, `game_exe_path` or spice64.exe beside the
  config); the id floor is the highest real id over both and over the old start id 2900. The plugin json lagged
  behind game build 2026071400 (json 2384, game 2403). `vox_host.py` takes `base_db` as a list, passes the floor to
  `is_up_to_date` and lets `assign_song_id` retire an overtaken id (old db entry + folder); older converters get
  one base (the game's xml) or none. Core `LauncherConfig.GameDir` / `PluginDir` / `BaseMusicDbs`,
  `VoxExportOptions.BaseDbPaths`; tests `ExportIdFloorOverSeveralBaseDbs`, `FindsBaseMusicDbs`.
- **Lone slams** (Johannes, from official data): a laser section may be one slam and nothing else — 118,940 of the
  sections in all 8,602 official .vox are exactly that, 269,499 end on a slam. Two clicks on one beat + right click
  (and a sideways drag on one beat) now give that lone slam, **no tail** (an earlier 0.1.0 build added a one-snap-tick
  tail; nothing adds one any more). Deleting one end of a lone slam removes the section (a single plain point is no
  laser); moving it keeps it alone. Verified: kson `[y, [[0, [v, vf]]]]`; ksh writes the two values 1/32 apart and
  reads back the same lone slam (normal and wide); ksh2vox 0.20.0 emits two rows on one tick with flags 1 / 2 from
  both, the shape of official `0001_albida_muryoku_4i` TRACK1 measure 12. `LoneSlamTests`.
- **Chart check** (Ctrl+Shift+A, was bound but unhandled; Edit → Check Chart): osu! AIMod-like panel of errors,
  warnings and notes with click-to-jump (seeks and selects the objects) that follows your edits. Rules (Core
  `Validation/`): BPM / time signatures / stops / std_bpm / disp_bpm, overlapping notes and laser sections, values
  outside 0..1, objects after the audio end, what ksh rounds (51 laser steps, touching holds, stop lengths) and VOX
  rounds (objects off the 1/192-measure grid), undefined FX effects / laser filters / parameter changes, slam sounds
  and spins on no slam, non-ASCII romanised title / artist and file names, missing files — and, instead of K-Shoot
  Editor's silent placement suppression, **warnings** for slams ≤ 1/32 apart, a ≤ 1/16 moving segment right after a
  slam and narrow slams. Save and export run it and only mention the result in their toast. Other code adds rules
  by implementing `IChartCheck` (found by reflection; ARCHITECTURE §15).
- **Toolbox FX Effect and Slam Sound** are wired: changing them sets the selected FX holds' effect / the selected
  slams' sound (kson `slam_event`; Auto = default), new FX holds and the slams of new lasers get them (same undo step);
  the drop-downs show what the selection has. The FX list is the chart's definitions + presets. **Ctrl+click on an FX
  hold** with an FX tool cycles its effect (Shift+Ctrl backwards), Ctrl+Shift+E does it for the selected holds.
- **Insert blank time / delete range** (Timing menu; Insert = one snap tick, Shift+Insert = one beat, Alt+Insert =
  dialog in snap ticks / beats / measures / pulses, Ctrl+Shift+Delete = the selection's range): everything from the
  cursor moves, BPM points with it (the one on the cursor stays; a delete keeps the tempo of the range end), time
  signatures only for whole measures on a bar line. One undo step; insert + delete is the identity on every library
  chart (test).
- **Swap Laser Colour** (Ctrl+Shift+H, Edit menu): selected lasers change L ↔ R keeping their values and curves.
- **Snap divisors 1/64, 1/96, 1/192** (K-Shoot Editor's finest quantize) on the slider, Ctrl+wheel and the menu, plus
  bindable "set to 1/12 … 1/192" actions; their lines are rounded to the 240-per-beat pulses. Timeline and playfield
  now thin out ticks per snap level (the playfield used to drop all sub-beat lines at once).
- **Inspector** (Ctrl+Shift+I, View menu): exact values of the selection — notes: start (pulse or m:b+pulses), length
  (pulses, 1.5b, 1/4), lane, FX effect; lasers: position, value, slam landing end ("Slam to", both slam ends shown),
  curve type, width. Multi-selections show "(mixed)", Enter applies to all, one undo step; curve samples are left
  alone, a lone slam keeps its slam.
- **Song Setup → Advanced**: display BPM, standard BPM (hi-speed basis), genre icon, title and artist images (picked
  files are copied in) and "Reduce the volume of narrow slams" (kson `vol_auto`, ksh `chokkakuautovol`) — read and
  written before, now editable.
- **Layers** (Ctrl+Shift+U, View menu): show / hide BT, FX, laser L / R, BPM / scroll / stop markers, bookmarks, beat
  grid; "only" per object layer, Show all. Hidden notes and lasers are not drawn and cannot be clicked or box-selected.
- **Loop playback** (Shift+Space, View menu): over the selection, else between the bookmarks around the cursor, else
  the measure; shaded green in the timeline.
- **Playtest gauges**: Easy (halved damage; our definition) and Hard (USC's EXCESSIVE gauge numbers: start 100 %,
  12/21 gains, −9 % per chip ERROR, damage halved near 0; the run stops at 0 % with "TRACK CRASH"). Options →
  Playtest "Gauge", F2 in the playtest cycles and restarts.
- **Laser entry / exit pieces** (Johannes' screenshot): every laser section start shows a short fading piece of laser
  below it at its start value, a section ending on a slam a piece after the landing end — so a slam start or a lone
  slam shows where the laser comes in. Lengths from USC (2.56 / 2 band widths, its default skin's entry / exit
  textures), compose and playtest; pure rendering, not clickable.
- Smoke part **`features`** (generated song, real input) covers all of the above.

- **VOX game effects — everything the game executes, nothing it ignores** (docs/VOX_EFFECTS.md; needs ksh2vox 0.20.0).
  Camera tab → **effects**: effect lanes on the compose time axis (post effects per background stage with one column
  per effect, LaneY / BIL_RotZ / Morphing2 tweens, lane colours, bar lines, game assets, note scripts), inspector with
  the official ids as pickers, presets (Negative flash, Grayscale / Sepia section, Hue sweep, Blur hit, RGB split, Shake,
  Noise burst, CRT, Glow), game background (`bg_no`, every official id with its first song; also in Song Setup) with a
  warning when post effects sit on a background without effect stages, `#BPM OPTION`, note scripts (templates,
  validation, assign to the compose selection). All eight post effects with every parameter, stage 0 / 1, instance
  index, tick or ms lengths. Commands the game parses but never reads (LaneX, GEO_RotZ/RotX, Tilt_Mag, 3D_Scroll,
  Morphing0/1/3, LANEMODEL*, LANEDISP, HudY, AIRL/AIRR_ScaX …) are not offered. Playtest and camera preview:
  ColorConversion exact (the game's shader maths), blur / RGB split / glow / noise / CRT / shake / lane slide / lane
  colours approximated, bar lines exact, scripts move chips; the HUD is never affected. Storage: kson
  `impl["sdvx-editor"].vox`, ksh `bg_no=`, `lane_toggle=` and `//#vox_…` comment lines. Chart check rules
  `effect.vox.*` (Core `Validation/EffectChecks.cs`). Export badges get a 0.20.0 tier. Smoke part `effects`.

- **Tool tiles no longer crowd their edges** (Johannes' screenshot): the hint lines ("Shift: FX", "Ctrl: Hold",
  "F: classic") keep 8 px from the right edge, are 11 px and cut with "…" when too long; the laser tile's L/R badge
  moved from the top-right corner (where it sat under "F: classic") to the right of the icon, and the icon makes room.
- **Laser editing like osu! sliders, part 2** (Johannes, five requests):
  - **Right click while placing puts a last anchor at the cursor, then finishes** (osu!). Only when the cursor lies
    after the draft's last anchor (or makes a slam on its beat); on the last anchor or before it right click just
    finishes, as before. Classic lasers alike. The camera lanes follow the same rule: right click on empty space after
    a chain's tail adds a last anchor there and ends the chain (one undo step), elsewhere it only ends it.
  - **Anchors drag on the first press.** Select tool: a press on any anchor or point of a laser that is not selected
    selects it and drags that anchor at once (Bezier control anchors, the last anchor and slam ends included; a plain
    point stays selected on its own when nothing moved, so Delete still removes just it). When the laser is part of a
    bigger selection (notes or other lasers too), the press moves the whole selection by the pointer's delta from the
    grabbed anchor (it used to snap the section start to the pointer).
  - **No grid when editing existing laser points / anchors.** Drags of existing points and anchors follow the pointer
    by its delta from the press: value unsnapped, time on a 5-pulse grid (1/192 of a 4/4 measure = one VOX tick, the
    finest step kson, ksh and VOX all hold exactly). Hold **Shift** to snap (beat divisor + 1/50 value grid, classic
    lasers 1/10) — rebindable in Options → Input → Compose "Snap while dragging laser anchors" (Shift / Ctrl / Alt;
    editor.ini `DragSnapModifier`); Ctrl keeps freeing the value. Placing new points and the point a click inserts
    keep snapping. Export: kson exact; ksh exact in time (the writer adds lines, at most 192 per 4/4 measure) and
    values rounded to 1/50 (51 ksh steps, as before); VOX via ksh2vox exact (pulses / 5).
  - **Wide toggle on placed lasers.** W / the Wide Laser tile / Compose → Wide Laser with lasers selected switches
    them too (one undo step) and **keeps them where they are on screen** (normal v ↔ wide 0.25 + v/2); their anchors
    then reach the whole wide range. Narrowing clamps what lies beyond the lanes to 0 / 1 (one undo step, the toast
    counts the clamped values). With nothing selected the toggle only sets the width of new lasers. The two K-Shoot
    Editor 2.2 variants ("Toggle Wide (Keep Position)" / "(Keep Values)", strings of its editor.exe) are both there:
    in the laser tool Ctrl+Shift+click on a laser = keep position, Ctrl+Shift+right click = keep values; Ctrl+W
    (Edit → Toggle wide on selected lasers) keeps the values as before. Core `LaserEditing.WithWidth(section, width,
    keepPosition)`.
  - **Bezier control anchors move freely**: out of time order with the other controls (anywhere strictly inside their
    segment) and past the lanes (as far as the playfield's input area reaches: about −0.53..1.53 for normal lasers,
    −0.02..1.02 for wide ones — the overhang is sized for the wide range), drawn and grabbable out there. Only the
    bake is clamped (values to the lane, time monotone, as before); a 3-anchor Bezier whose control lies outside the
    range is sampled instead of written as one kson curve (that curve would leave 0..1). Core: `LaserPath.IsControl`,
    `Normalize` keeps controls in place and unclamped while the other anchors are in order (paths without such
    controls normalise exactly as before, so existing files bake bit-identically), `LaserPathEditing.MoveAnchor(...,
    controlLimits)` (camera lanes pass none and keep the old clamping). kson keeps the free controls in
    `laser_paths` (round trip tested).
  - Tests `LaserFreedomTests` (12) and the existing laser / camera suites (all green); smoke compose-input (all four
    layouts): right click places the last anchor, direct free drag + Shift snap, multi-selection moves together,
    Ctrl+Shift click / right click wide variants, W widens / narrows a selected laser with clamping, Bezier control out
    of order and past the lanes and still grabbable; smoke camera: right click after the chain's tail. Existing smoke
    drags that expect grid positions now hold Shift.

- **A second, longer tutorial on a real song; one song folder per tutorial.** Help → Tutorial… / the song select
  button now list two tutorials on tabs, the new one first: **Tutorial (new, [REDACTED] by Fax)** — Fax's
  "[REDACTED] (Cut Ver.)" (`Resources/Tutorial/Main/tutorial.mp3`, the received file unchanged; used with the
  artist's permission, NOT covered by the editor's licence; source link `https://soundcloud.com/fax0508` written as kson
  `impl["sdvx-editor"].song_sources` into every tutorial chart) — and **Tutorial (classic)**, the first tutorial on
  the CC0 placeholder song, kept unchanged and playable until Johannes has collected its feedback and the mapper has
  played it (docs/TUTORIAL.md lists what to delete then). Timing 235 BPM, offset 682 ms: onset analysis puts the
  backbeat (1.5–6 kHz band) on the grid 170.7 + n·255.32 ms, and the first sound (682.1 ms) and the drop (16 000.5 ms)
  on bar starts of 681.6 + n·1021.28 — Johannes' "probably 638" sits 44 ms early, on neither grid; to confirm by ear.
  The new tutorial: 8 chapters, 75 steps, ≈ 12¼ min (budget Core `MainTutorialPlan`, ceiling 15), adding the hold
  modifier, resizing a hold by its end, the laser anchor menu (Bezier), the camera anchor menu (three clicks = a
  curve, then Bezier), modding timestamps (Ctrl+C → paste into the jump box) and Support the Artist / Song Setup →
  Sources; chapter 0's F6 exercise is a scroll speed jump ×1.25 at the drop (the song keeps one tempo). It rebuilds ONE
  solution chart — `Resources/Tutorial/Main/solution.kson` once the mapper's chart ships, until then a beginner-readable
  placeholder in Core `MainTutorialCharts` — chapter by chapter with the parts still to come cut out; ghost targets are
  read from that chart inside measure windows in `tutorial.json`, so swapping the chart is a file drop plus
  `MainTutorialTests` (which names any window that does not fit). Both tutorials now work in ONE folder each
  (`Tutorial`, `Tutorial (classic)`) instead of "Tutorial (Chapter N)" per chapter: chapter 0 creates it, every later
  chapter rewrites its chart files in place (other files such as a jacket stay); a marker `.sdvxeditor-tutorial.json`
  (tutorial, chapter, chart hashes) makes sure only the tutorial's own folder is ever touched, and starting a chapter
  asks "Replace?" first when unsaved changes or saved edits / extra charts would go — not when simply moving on to the
  next chapter. On start-up the old per-chapter folders and the staged "Tutorial (Chapter 0).ogg" are removed, but
  only when they hold nothing but the classic tutorial's byte-identical audio and .kson charts. Progress ticks per
  tutorial (`TutorialMainProgress` beside the classic `TutorialProgress`). Fixed on the way: F6's "Jump to" on a scroll
  speed point unticked itself at once while its value still equalled the speed (the row reload dropped the jump), so a
  speed jump could not be set up at all. Tests `MainTutorialTests` (13 cases). Smoke `tutorial` replays both tutorials with
  real input: main 75 steps (69 scripted) + classic 69 (64), SMOKE PASSED in 226 s; full smoke (all 12 parts, AureoLe) PASSED in 881 s. Both tutorials' laser steps follow the laser changes: hold F for K-Shoot-style points, right-click on vs. beyond the last point, anchors drag without selecting (Shift snaps), and the wide-laser / practice steps deselect first because W now also widens a selected laser (the demos click empty space first); tutorial smoke again PASSED in 228 s.

- **Classic lasers while F is held** (Johannes: a held key that switches laser editing to the old K-Shoot editor
  behaviour, also just for short stretches, like the hold modifier). The behaviour comes from the kson editor itself
  (Drewol's kson-rs `editor/src/tools/laser.rs`, the editor of `D:\kson-win-portable`; its locale keys `add_laser`,
  `adjust_laser_curve`, `remove_laser` confirmed by a strings scan of the portable exe): every click a point on the
  snapped beat, values on a coarse grid (`floor(10·wide·lane/6)/(10·wide)` = tenths, twentieths for a wide laser),
  straight segments, a second click on the last point's beat with another value = slam, a click on the last point
  again (same beat and value, or the same beat after a slam) finishes, Shift on the first press = wide. Adopted
  while the key is held: plain straight points (Core `ClassicLaser.Add`: the new point and the one before it are
  explicit straight segment starts, so no 2 straight / 3 perfect / 4+ Bezier typing), the 1/10 (wide 1/20) value grid
  for every laser click and drag (rounded to the nearest step rather than floored, so the point lands on the cursor
  ghost; Ctrl = free value as before), slam and finish-by-clicking-the-last-point; right click still finishes, Shift
  still draws wide. Left out: kson-editor's curve handles (segment `a, b` drags — the anchor types cover curves) and
  middle-click delete (right click deletes). The official K-Shoot Editor 2.2.1 (Qt / libkson, strings of its editor.exe,
  not run) has a "Laser Placement" setting (Standard / Detailed / Detailed (Legacy) / Most Detailed, key
  `laserXSnapMode`); the step sizes behind those names are not in its strings, so the classic grid stays the kson
  editor's 1/10 (the normal grid, 1/50, equals KSH's 51 steps). A chain may switch modes between clicks: the last classic point stays an
  auto segment start, so anchor clicks after it are typed by count from there, and an anchor chain before it keeps its
  count. Classic points are plain Straight anchors: an all-classic laser stores no path and writes kson / ksh identical
  to the same points placed one by one. Key: `F`, rebindable in Options → Input → "Editor · Compose" ("Hold: classic
  lasers"). Chosen because no editor binding uses F with or without Ctrl / Shift / Alt (so holding it next to the hold
  modifier, Shift or a playback key can never fire another shortcut — A, D, G, H, N, O, P, Y, B, I all have Ctrl
  combinations here, Q / E / W / S / T / L / M / J / K / Z / X / C / V and Space are taken, Tab is refused by the
  binding UI), it is on the left hand's home row while the right hand stays on the mouse, and osu! has no editor
  shortcut on F. Same latch as the hold modifier (Core `HoldModifierLatch`): another key or an editor action while F
  is down uses it up until it is released; it only counts on the compose tab. The laser tile's second hint line says
  "F: classic" / "classic lasers" while held. Tests `ClassicLaserTests` (9: grid, plain points, export identity, slam
  + finish rules, mixed chains, a point before the start). Smoke compose-input (real input, all four layouts): tile
  hint, F held → 1/10 grid plain points + slam + finish on its end (one undo step), release, F used up by K, a mixed
  anchor / classic / anchor chain, undo.

- **Credit the artists: song sources, Support the Artist, Thanks.**
  - Song Setup → **Sources** (new last tab): links where the song can be heard or bought, as many rows as needed
    (Add / ✕ per row, at most 16). Only music sites are accepted — SoundCloud (incl. on.soundcloud.com), Spotify
    (incl. spotify.link), Deezer, YouTube (incl. youtu.be), YouTube Music, Apple Music, Bandcamp (incl.
    `*.bandcamp.com`), Tidal, Amazon Music, Beatport, Niconico (incl. nico.ms), Audiomack; anything else is refused
    with the reason under the field ("example.com is not a music site …", "Only https:// links …"). Links are
    normalised: https only (a bare `soundcloud.com/x` or `http://` becomes `https://`), host lower-cased, no user
    info / port / IP, share and tracking parameters (`si`, `utm_*`, `fbclid`, `feature` …) dropped. Core
    `Library/MusicLinks` (allow-list with a service name per host, subdomains matched, look-alikes such as
    `youtube.com.example.org` refused); `SongSourceTests`. OK writes the list as part of the Song Setup undo step.
  - Storage (song-level): kson `impl["sdvx-editor"]["song_sources"]` = array of URL strings, ksh comment lines
    `//#source|<url>` (written after the first bar line, where KSM and ksh2vox skip `//` lines even with `=` in a
    URL; read in header and body). A save that changed the list copies it into every other `.kson` difficulty of the
    song (Core `SongSourceSync`; a `.ksh` is never rewritten); readers take the first chart with a non-empty list
    (`SongFolder.SongSources`, `LibrarySong.SongSources`, library cache version 3), and a difficulty without its own
    list shows and saves the song's. Files without sources read as empty and are written byte-identical. Verified:
    72 `//#source` lines in the 24 library .ksh leave ksh2vox 0.19.0 and the submodule's 0.17.0 output (VOX and
    radar input) identical; the library .kson with `song_sources` converts identically too.
  - **Support the Artist**: a quiet pill under the song details in song select and a small heart link at the right
    end of the editor's menu bar (also on the Sources page), shown only when the song has a valid source. One link
    opens its confirmation; several open a small list with the service names first. Every web link goes through an
    osu!-style confirmation naming the domain (Open in browser / Copy link / Cancel, `Overlays/ExternalLinks`); links
    are re-validated before they reach a button, so a chart file cannot make the editor open another site.
  - **Thanks**: a top-level menu bar entry and a footer button on song select open the credits list, read from the
    embedded `SDVXEditor.Resources/Credits/credits.json` (sections with name, role and optional https link; edit the
    names there). Initial content: tutorial song Fax (SoundCloud), tutorial chart and playtesters "to be announced",
    the editor's author and the component licences from About.
  - Smoke: new part `sources` (real input: a refused and two added links, remove, OK, Ctrl+Z / Ctrl+Y, Ctrl+S with
    the copy into the other difficulty, reopen, both Support buttons with the list and the confirmation — the URL
    opener is stubbed, no browser starts — Thanks from the menu bar and the song select footer); `menus` clicks
    Thanks; the walk screenshots the Sources page.

- **Slams are two points again** (Johannes: "since the new update you can't make slams with two points any more — the
  right and left point of a slam count as one"). Cause, reproduced by `SlamEditingTests` before the fix: with the
  anchor-path placement the first two clicks on one beat turned the single anchor into a slam, but a one-anchor draft
  previewed as a single point at the start value (`LaserPlacement.Preview` ignored `Vf`) and right click dropped it
  (`FinishPlacement` needed two anchors); a second click within the 12 px handle radius of the first (a small slam)
  cycled the anchor type instead; hover frames and selection covered both ends of a slam at once (`drawAnchors`
  compared only the anchor index), and the landing end could not move the slam in time. Now:
  - Placing: two clicks on one beat + right click = a lone slam (one point V → Vf, no tail — corrected 2026-10-27, see
    "Lone slams" above), exactly the section dragging a slam gives (`LaserPlacement.Result`, moved to Core). A click on the last anchor's beat with
    another snapped value makes the slam even over that anchor's handle (Ctrl = free value keeps the handle click,
    `LaserPathEditing.IsSlamClick`); the preview shows it. A slam stays a hard break for auto typing (2 straight /
    3 perfect / 4+ Bezier are counted per side, no Bezier joint at a slam).
  - Editing: the start end and the landing end are two handles (the landing end carries a dot in the laser colour),
    hovered, selected (click; Ctrl+click toggles; a box over one end selects that end) and dragged one by one — the
    start end sets `V`, the landing end `Vf`, either moves the slam in time strictly between its neighbours
    (`LaserPathEditing.MoveAnchor`, plain sections now drag slam ends through the anchors too). Delete / right click
    on one end / the anchor menu's "Delete slam start" / "Delete slam end" leave a plain point at the other value
    (`LaserPathEditing.DeleteSlamEnd`); both ends selected or "Delete anchor (both ends)" remove the point (a section
    left with fewer than two anchors goes). Curved paths are edited through their anchors, so they keep their types.
    Move, copy / paste, mirror, nudge and toggle slam act on the end's section; Ctrl+C writes the slam once.
    Model: Core `LaserSlamEndRef`, `Selection.SlamEnds` / `IsEndSelected` / `Touches` / `TouchedLaserPoints`,
    `SelectionOps.Delete` through anchors for sections with selected ends; undo / redo keeps an end selected while
    its point is still a slam. Every change is one undo step.
  - Formats unchanged: one point / anchor with `V` / `Vf` → kson `[v, vf]`, ksh two lines 1/32 apart, VOX two rows on
    one position; a two-click slam writes kson and ksh byte-identical to a dragged one, and a plain section re-baked
    through its anchors writes identical kson / ksh.
  - Tests: `SlamEditingTests` (14: placement, auto typing around slams, slam-click rule, end drags incl. clamping,
    delete one / both ends on plain and curved sections with exact undo, selection toggling, clipboard / move,
    timestamps, format identity). Smoke compose-input (real input, all four layouts): a small slam inside the
    handle radius, two-click slam + preview + right click, hover each end, click / Ctrl+click selection, landing end
    drag, start end drag in time, Delete one end + Ctrl+Z, menu "Delete slam start" + undo, box over one end,
    Ctrl+C timestamp, all undone. The tutorial's slam step mentions the two ends.

- **The cursor is no longer trapped in Borderless** (Johannes: wants to move straight on to the other monitors).
  osu-framework's `ConfineMouseMode` defaults to Fullscreen, which also counts Borderless. New editor setting
  `ConfineMouse` (default Never), bound to the framework setting at start-up, so it also replaces the
  `ConfineMouseMode = Fullscreen` already written to existing framework.ini files; Options → Graphics → Layout
  "Confine mouse cursor" (Never / In Borderless / Fullscreen / Always).
- **Dragging a hold by its end changes its length** (Johannes: grabbing the end moved the whole note). A press within
  7 px of a hold's end (and nearer the end than the start, so a tiny hold keeps its head for moving) resizes it in
  the select tool, and in a BT/FX note tool for holds of its own kind (before, the chip tool turned such a hold into
  a chip; elsewhere on the hold it still does). Same rules as the hold tool's resize: snapped, the start never moves,
  `OverlapRule.CanResize` (an overlap keeps the last length that fitted), `SetNoteLengthCommand`, one undo step
  ("Hold length", "Hold to chip" when dragged onto the start). With several holds selected the grabbed one follows
  the pointer and every other selected hold changes by the same delta, osu!-like; only the grabbed one can become a
  chip, the others stop at one snap tick (Core `HoldTailResize`). Hover hint: a white bar with a ↕ stroke on the
  tail (osu-framework has no per-area OS cursor), the chip ghost hides meanwhile. Also: a hold grabbed on its body
  now moves by the distance dragged; before, its head jumped under the pointer, so grabbing it lower down
  pulled the whole hold forward. Tests `HoldTailResizeTests`; smoke compose-input (real input, all four layouts):
  tail hover hint, longer / undo, shorter, onto the start = chip / undo, body drag moves one unit / undo, two
  selected holds +1 unit together / one undo.
- **Camera curves get laser-style anchors** (Johannes: "the anchor type like with the laser, but in the camera
  menu — currently you can only make linear changes"). zoom_top, zoom_bottom, zoom_side, center_split,
  rotation_deg and manual tilt are anchor paths now, with the laser's types and rules: Straight / Perfect curve /
  Bezier / Inherit, clicks in a row auto-typed like an osu! slider (2 straight, 3 perfect curve, 4+ Bezier, two
  curves in a row meet at a Bezier anchor), a click on a placed anchor cycles its type while adding, right click
  on empty space finishes, Ctrl+click on a curve inserts, right click on an anchor / a curve opens the same menu as
  for lasers, Shift = instant jump (a hard break like a slam), the old Alt-drag ease handle stays for a plain
  straight pair. The graph is a function of time: curves are clamped to be monotone exactly like lasers. Model:
  the laser path code is generalised, not copied — `LaserPath` got a `PathRange` (lasers 0..1 clamped, bake
  bit-identical; a camera lane its view range, unclamped), new Core `CameraPaths` (runs = the camera counterpart
  of laser sections; manual tilt has one run per stretch of manual values, auto mode switches at its ends kept),
  `CameraPathEditing`, `CameraChain` (the placement in progress keeps its auto flags), `SetCameraLaneCommand`
  (undo restores the exact point objects). Stored losslessly in kson `impl["sdvx-editor"].camera_paths`, attached
  on read only while they still bake to the points (`Chart.StaleCameraPaths`). Bake: a 3-anchor Bezier is one
  exact kson curve, everything else samples on the 5-pulse VOX grid within 0.5 units (zoom_*, center_split),
  0.1° (rotation_deg) or 0.005 (tilt); ksh gets plain points (10-pulse grid, ksh2vox reads ksh camera curves as
  straight). Old charts: every point is a straight anchor, their kson curves are kept. UI: anchors in type colours
  in the hovered / selected lane with the control polygon, pink selection frame, next-segment preview while
  placing, the inspector edits the selected anchor, the help note and tutorial chapter 4 mention it. The laser
  menu drawable is shared (`LaserContextMenuItem.CreateMenu`). Tests: `CameraPathTests` (typing, middle chains,
  evaluation per type, monotone clamp, bake bound per lane for kson and ksh, exact kson curve, jumps, old charts,
  tilt runs with auto ends, kson round trip, stale paths, unknown lanes, ksh export, exact undo). Smoke `camera`:
  real input on zoom_bottom — three clicks = perfect curve, hover, click cycles while placing + Ctrl+Z, right click
  → Bezier (one kson curve), control anchor drag, preview follows, kson reload keeps the anchors, ksh export within
  the bound; the existing camera steps go through the anchor model (menu delete instead of right-click delete).

- **Hold modifier in compose** (Johannes: "like Shift: FX, Ctrl or Alt picks the hold variant"). Holding it with
  the BT Chip tool gives BT Hold, with Shift as well FX Hold; with FX Chip it gives FX Hold. A hold tool stays a
  hold tool (holding a key never takes a capability away), Select and Laser are untouched — so Ctrl+click / box
  select, Ctrl = free laser value and Ctrl+click anchor insertion keep their meaning. Options → Input → Compose →
  "Hold modifier": Ctrl (default) or Alt, editor.ini `HoldModifier`. Live feedback like the Shift swap: the chip
  tiles carry a second hint line "Ctrl: Hold" ("now Hold" while it counts), the hold tile lights, and the hover
  ghost of a hold tool shows a one-tick tail. The modifier only counts on its own (Core `HoldModifierLatch`):
  any other key, wheel turn or editor action while it is down (Ctrl+Z/C/V, Ctrl+wheel divisor, Alt+wheel zoom,
  Ctrl+Alt+wheel tool cycling, Alt+Enter …) uses it up until it is released, so Ctrl+Z with the chip tool only
  undoes and leaves no hold ghost, and a click with Ctrl still down after it places a chip. The editor screen
  feeds Ctrl/Alt from key events and a per-frame poll of the input state (keys swallowed by other drawables, SDL's
  key release on focus loss), the playfield once more from the press itself; only on the compose tab, so the
  camera tab's Alt-drag on curve handles is unaffected. Alt alone cannot enter the Windows menu loop: SDL3
  swallows `SC_KEYMENU` unless `SDL_WINDOWS_ENABLE_MENU_MNEMONICS` is 1 (osu-framework never sets it, checked in
  the binaries); Program.cs now sets it to 0 explicitly. Tool tiles: icon slightly lower/smaller so two hint
  lines fit. Tests: `HoldModifierTests` (variant table, latch). Smoke `collab` → compose-input, real input, Ctrl
  and Alt rounds in all four layouts: chip + modifier drag = hold, + Shift = FX hold, FX chip + modifier = FX
  hold, plain click = chip, Ctrl+Z only undoes, used-up modifier → chip, Alt chosen: Ctrl inert, Alt+wheel zoom,
  Alt alone keeps the keyboard (key 3). The Windows menu-loop part cannot be exercised by injected input.

- **Data-driven skill radar and a better level estimate**, fitted on the official charts of the user's own
  install (aggregate numbers only). New Core `Patterns/`: `PatternAnalysis` (85 time-aware features —
  chord peaks, jacks, trills, streams, rhythm grid, hold nesting, laser time / movement / slams / far side,
  one-hand and hand-trip notes with a lane→hand model, tempo, stops, scroll, camera) and an additive
  linear + gradient-boosted-tree model evaluated in C# (`AdditiveModel`). New `Radar/RadarModel.cs`
  (`SkillRadarModel`, embedded `Radar/radar_model.json`); song select shows it, the classic radar.py port stays
  as the fallback and the parity reference (`RadarTests` untouched). Against the official music_db radar
  (5-fold song-grouped CV, 8,573 charts), MAE classic → new: NOTES 13.4 → 0.10, PEAK 28.4 → 2.0,
  TSUMAMI 27.7 → 2.3, TRICKY 24.4 → 10.7, HAND-TRIP 42.6 → 2.8, ONE-HAND 10.5 → 3.2 (r ≥ 0.97 on all but
  TRICKY, 0.83). Findings: NOTES is 16.7 × (chips + hold seconds) per second; PEAK counts chords in 1.25 s;
  ONE-HAND / HAND-TRIP count buttons while the other / the own hand's knob *moves*. Camera features are kept
  out of the radar (official charts add camera with difficulty, fan charts do not — a fan NOV came out
  TRICKY 67). Level: slot-aware = ridge + pattern trees + isotonic, CV MAE 0.490 → 0.423, ADV 0.62 → 0.53,
  hold-out 0.468 → 0.385, fan charts 0.97 → 0.88; slot-free stays ridge + isotonic (trees were better on CV
  but not on the 24 fan charts). Coefficient file schema 2 (residual trees), schema 1 still loads.
  `tools/DifficultyCalibrator` regenerates both files with one command (`--core src/SDVXEditor.Core`) and
  prints CV, per slot / level / VOX version, temporal hold-out, fan charts and a hand-model check.
  Spec for a port: `docs/RADAR_MODEL.md`. Tests: `PatternTests` (feature values, determinism, model format,
  loading, fallback, monotonic sanity).
- **VoxReader honours `#BEAT RESOLUTION`** (ticks per quarter; 44 official files use 144–1680 instead of 48).
  Their notes were read at the wrong positions — e.g. the PEAK outliers of every penoreri chart. It also reads
  the camera coarsely now (CAM_RotX / CAM_Radi / Tilt tweens, laser spin ids), for analysis only.

- **Esc like osu!**: Esc closes the topmost thing first — an open menu, a drop-down list, a dialog, the options, a
  laser draft, the reference view — and only with nothing open leaves the editor, now through the same Save /
  Discard / Cancel prompt as File → Exit when the chart has unsaved changes (Esc on that prompt = Cancel; a saved
  chart leaves at once). Before, Esc left at once and dropped unsaved changes without asking. The tutorial card
  never takes the key. Smoke `menus` checks each of these with real Esc presses through the root input manager
  (who handled the key: `EditorMenu`, `PopupMenuList`, `BookmarkListDialog`, the options search box,
  `ChoiceDialog`, else the editor binding).
- **Shift+wheel works.** osu-framework 2026.807.0 turns a notched vertical wheel turn into a horizontal one while
  Shift is held (`MouseScrollRelativeInput.Apply`: `Delta = (Delta.Y, 0)`, read from the IL). Every wheel handler
  read only `ScrollDelta.Y`, so Shift+wheel did nothing: the ×10 of the osu!-style steppers, and the Shift+wheel
  bindings (seek four snap ticks), which arrived as `MouseWheelLeft/Right`. Evidence: a scroll-handler log in a
  smoke run showed injected `(0, 1)` + Shift reaching `NumericUpDown` and `OsuStepper` as `(1; 0)` with no change.
  New Game `Input/WheelInput` (`Delta` / `Direction`: with Shift the X axis is the wheel, positive = up = left, as
  the framework defines both) used by NumericUpDown (Shift+wheel = ten steps, new), OsuStepper, SnapSlider, the
  options sliders, the options' wheel capture (Shift+wheel is stored as Shift+wheel up/down) and the song wheel;
  `WithShiftWheelAlias` gives every Shift+wheel binding a twin on the horizontal key. Smoke `ui-input` (both window
  modes): Shift+wheel on the F6 BPM field +10, Shift+horizontal −10, the same on the timing tab's BPM stepper (×10
  steps), plain wheel on the stepper, and Shift+wheel over the playfield seeks four snap ticks where the wheel seeks
  one.
- Smoke: **F6 BPM wheel flake** fixed at the root. `UserInputManager` drops a wheel turn (and a button press) while the
  OS reports the cursor outside the window; the injected press claimed `CursorInWindow`, the injected wheel did not,
  so the check failed (`bpm 100 -> 100` on Canto IX, `330 -> 330` on AureoLe) whenever the user's own cursor was
  elsewhere — not a Canto or BPM problem. Reproduced on demand by setting `CursorInWindow = false` before the wheel
  (both window modes failed with exactly that message), passes with the fix. The smoke root input manager now claims
  the flag for each injected wheel turn / press, for that event only.
- Smoke: every run starts from **default editor settings** — the smoke storage's editor.ini is deleted before the
  config manager reads it. A killed run had left `EditorBindings = tab-timing=F8`, master volume 0, BT-A on A and
  ticked tutorial chapters behind for the next run. framework.ini stays (the parts that change window mode / size
  restore them).
- Smoke, two song-dependent expectations made exact: `songselect` "drop imported" waits until the imported song is
  selected (the selection follows the library rescan; with Keep both on Canto IX the check ran in the frame the
  import ended and saw the old song — the product selected it a moment later), and `menus` accepts File → Open
  Difficulty's toast "This song has no other difficulty" on a one-chart song (ABYSS MOTION: ksh + kson of one chart)
  instead of timing out waiting for the chooser.
- Tutorial re-checked against the final behaviour: tool, laser-side and wide-laser keys are `{key:…}` tokens (they
  were hard-coded 1–6 / Q / E / W, wrong after a rebind); chip ↔ hold tip says what the tools do (hold tool drags a
  chip into a hold, chip tool click turns a hold back); Anchors explains hover preview (also with the laser tool,
  Options → "Show laser anchors on hover"), click-to-select before dragging, and completes on hover or selection
  instead of only on the hover preview (which never appears with that option off); Wide lasers mentions the
  overhang and Shift-to-draw-wide; Set the BPM mentions Shift+wheel; Find a setting now needs the search for
  "hitsound" before Esc (closing the options alone completed it) and offers "Take me back"; Back to song select
  describes the new Esc. Checked and unchanged: F6 combo pick + "+", the camera steps against the USC projection
  (zoom_bottom further right = nearer), bottom-bar and menu paths, playtest keys and results.
- **Tutorial trimmed to ≈ 10 minutes** (Johannes): 88 → 69 steps, 13.5 → 10 min, nothing dropped — every removed
  step's essential sentence went into its neighbour. Ch 0 21 → 16 (Song Setup opens and takes the charter in one step;
  the Jacket/Audio pages are explained on "Keep the changes", "When applying timing changes…" on the speed-up step, the
  Property Transformer on "Apply with OK" instead of opening and cancelling it). Ch 1 14 → 12 (tool + beat snap; mirror
  + undo + redo). Ch 3 12 → 8 (50 % + play/pause + 100 %; add a bookmark + jump to the next; open + close the collab
  list; the reference difficulty is one skippable "Extra" step at the end). Ch 4 9 → 7 (camera tab + lanes; background
  + export views). Ch 5 7 → 5 and ch 7 7 → 5: the options are visited once, in ch 7 ("Options": search "hitsound", the
  hitsound skin; "Key bindings": Input page), "Saving" folded into the ksh export step, "That's it!" into "All
  shortcuts". Ch 6 10 → 8 (F5 + Tab autoplay; results + back). Merged steps complete only when both halves happened
  (per-chapter flags in the step context). New Core `Tutorial/TutorialPlan` holds steps and minutes per chapter: the
  chapters read their minutes from it (2 · 1.5 · 1.75 · 1 · 1 · 0.75 · 1.25 · 0.75), the chapter list shows them with
  two decimals, the smoke part `tutorial` fails when a chapter's step count differs, and
  `TutorialTests.PlanFitsTheTenMinuteBudget` keeps the total at 10 ± 0.5 min with every chapter ≥ 6 s per step.
  Smoke `tutorial`: all 69 steps with real input in 95 s (64 scripted actions), plus chapter 4 alone, Reset and Exit.
- Full smoke, every part (songselect, walk, options, ui-input, bottombar, tutorial, collab, editing, camera,
  playtest, menus) on fresh scratch copies, 1080p, muted, right monitor: AureoLe 627 s / 2078 steps, Canto IX
  624 s / 2078 steps, ABYSS MOTION 622 s / 2086 steps — all `SMOKE PASSED`. `dotnet test`: 508 passed, 1 ignored
  (local-data test).

- **Interactive tutorial** (Help → Tutorial…, song select footer "Tutorial", a one-time card on first start): 8
  chapters / 69 steps (≈ 10 min, see the trim above) that map one ≤ 30 s song with a beginner — Song Setup & Timing (incl. F6 and the Property
  Transformer), chips/holds/FX, lasers (slams, curves, anchors, wide), navigating (zoom, rate, bookmarks, collab list,
  time dialog, reference difficulty), camera & background, effects & sounds, test & difficulty (playtest, autoplay,
  level estimate, radar), save/export/share. Spotlight with animated outline and arrow, ghost notes/lasers in the
  playfield, free-mapping zones with rule checks (count, grid, slam), auto-advance, Back (review), Skip step, Exit,
  "Take me back" when the user wandered off; texts show the user's current key bindings. Every chapter runs on its own
  from its prepared state ("Tutorial (Chapter N)" in the editor songs, Reset restores it), progress ticks in editor.ini.
  Core `Tutorial/` (definition, chapter charts, checks; `TutorialTests`), Game `Tutorial/` (engine, overlay, chapters,
  anchor registry). Placeholder song synthesised by `tools/TutorialSongGen` (CC0); swapping it = audio file +
  `tutorial.json` (`docs/TUTORIAL.md`). Smoke part `tutorial`: every chapter with real input, then chapter 4 alone
  from Help → Tutorial…, Reset and Exit.

- Playtest track and camera preview use **unnamed-sdvx-clone's lane geometry** (Johannes: "an USC/SDVX angleichen").
  Numbers read from USC `Track.cpp` / `Camera.cpp` (MIT, landscape), our own code in Core `CameraProjection`:
  lane 12 widths long (`LaneLength` 24 for our 2-wide block; was 9 = 4.5 widths), judgement line 0.9 below / 0.9
  ahead of the camera with USC's 1.5° base tilt, 60° vertical fov (was ≈ 44°), camera pitched like USC (line 5 %
  above the bottom) and the image then shifted so the line stays at `JudgeScreenY` (82 % down; pure translation,
  perspective unchanged). zoom_top = USC `PitchScaleFunc(z/100) × 15°` (was z × 0.15°: 35 → 4.46° instead of 5.25°),
  zoom_bottom = USC's slide along the camera→line ray (`1.65^−z − 1` track widths away for z ≤ 0, `1.65^−(z^1.35)` of
  the distance for z > 0; −95 → ×1.49, was ×1.24), zoom_side = USC `(z/100 × 500/696)/2` track widths (100 → 0.72
  half widths, was 1). Roll stays around the judgement line (USC rolls around the camera axis) and center_split stays
  "100 = one BT lane".
  - Measured, editor = USC to 3 decimals (viewport heights, hi-speed 600/321): lane 0.774 H wide at the line, far end
    9.0 % of that (was 29 %), 1/16 = 0.354 lane widths ahead (was 0.18), the lane spans 0.669 H; AureoLe at 2:24.4
    (zoom_top 34.4, zoom_bottom −93.4): 0.525 H, 13 %, 0.434. The view range is unchanged (8 / hi-speed beats).
  - Object sizes along the lane (chips, slams, measure / judgement lines, cursors, hit flashes, laser heads) are world
    lengths now (`CameraProjection.Along`), the same world sizes as before; the new camera is nearer but wider, so
    at the judgement line they look the same size. Laser segments subdivide finer (64 per lane, max 96); the
    lane-press glow is 1.6 world units (was 2.9, which reached half-way up the longer lane).
  - In-app probe (`SDVXEDITOR_SMOKE_PLAYTEST_AT`, 1080p, mod speed 600), editor before → after (USC):
    AureoLe 1:00 lane 813 → 836 px (836), 1/16 0.18 → 0.354 lane widths (0.354), lane end 238 → 75 px (75);
    AureoLe 2:24.4 1/16 0.198 → 0.434 (0.434), width 649 → 566 px (566); galvanizer MXM 0:30 (zoom_bottom −74)
    0.201 → 0.422 (0.422), 1:00 (zoom_top 18.7, zoom_bottom −32.7) 0.209 → 0.416 (0.416), width 747 → 732 px (732).
  - Camera tab preview: same projection, its window is 800 ms (the playtest's default view, mod speed 600) instead
    of 1800 ms, which USC's perspective would cram into the far quarter.
  - `CameraProjectionTests` (3): lane, zoom and units against an independent re-implementation of USC's transforms;
    `ProjectionIsSymmetricPinnedAndRolls` unchanged and still green.
- Playtest hi-speed basis = the chart's **most common tempo by duration** (up to the last note), like
  unnamed-sdvx-clone's M-mod (`Beatmap::GetModeBPM`); a declared std BPM (ksh `to=`, kson `meta.std_bpm`) still
  wins. Before, it was the tempo at the test's start, so the same mod speed scrolled differently depending on where
  F5 was pressed (Canto IX: 91 at the start, 97 most of the song). Core `TimingMap.ModeBpm`, `HiSpeedBasis`.
  The HUD says what the speed rests on — `HI-SPEED 1.87 (600 ÷ mode BPM 321)` — and a forced hi-speed says so:
  `HI-SPEED 0.016 FORCED (×mode BPM 321 = 5.1)`.
  - Trigger: an "extremely compressed" AureoLe MXM screenshot at 2:24.400 (score 186 567, chain 75). That is
    exactly the smoke test's *stress* run (start 140 341 ms, forced hi-speed 0.016 → ~500 beats on the lane on
    purpose), whose HUD showed `HI-SPEED 0.0 (×BPM 600)` — the 600 was the unused setting. A normal test there: 321
    BPM (constant from 0 to 180.19 s), hi-speed 600/321 = 1.869, 4.28 beats = 0.8 s on the lane, scroll speed 1, no
    stop, camera zoom_top 34.4 / zoom_bottom −93.4 (distance ×1.23), 1/16 = 129 px at the judgement line (1080p).
  - Not a cause, checked: the ksh header `t=35-321` is a display range — the reader keeps it as `DispBpm` only and
    the first chart line's `t=321` is the single tempo point at pulse 0, as in libkson (`InsertBPMChange` skips a
    value with `-`). All 25 Nautica .ksh/.kson files have exactly one BPM point at pulse 0 (6 of them state a range
    in the header plus an inline start tempo). The 35 BPM in the level estimator's `bpm_min` is a real tempo change
    later in the song; its 21.0 for a declared 19 is the clamp of a raw 21.7 flagged low-confidence (157 BPM
    changes, a 6-minute chart outside the training range) — unrelated.
- Smoke `playtest`: `SDVXEDITOR_SMOKE_PLAYTEST_AT=144400[,…]` autoplays to each song time, pauses, screenshots
  (`65-playtest-at-<ms>`) and logs BPM, basis, hi-speed, lane beats, scroll speed, camera and the measured on-screen
  spacing; every run reports its hi-speed and basis. 11 new NUnit tests (`HiSpeedBasisTests`: the `t=35-321` header,
  a ranged header with the first tempo later, inline-over-header, mode by time not beats, end and tie rules, std BPM
  first, every real .ksh has one start tempo, AureoLe MXM basis 321).
- Timeline waveform rebuilt in the reference editor's look, so kicks and snares stand out: `SpectralWaveform` draws
  one bar per 3 design px, mirrored around the centre, colour per column from the spectral balance like DJ software
  (low → red, mid → green, high → blue, mixtures blend, quiet columns dimmer, translucent, ticks on top). Height is
  the loudest 10 ms RMS the column covers, `(level / song reference)^1.6` with a dB floor (−3 dB → 0.57, −20 dB →
  0.13): kicks become lobes with gaps between beats, quiet parts stay visible. Zoomed out a column takes its loudest
  window (a kick stays a spike), zoomed in single transients separate; columns are anchored to time so they scroll
  without shimmer. One `QuadBatch`, refilled only when time / zoom / size / opacity change, no per-frame allocation.
  - Why not the framework's `Waveform`: (read from the 2026.807.0 IL) it keeps per 1 ms point the peak per
    channel, but its low/mid/high intensities come from one BASS FFT1024 per 1024 samples (23 ms, Hann), copied
    to every point in that stretch, as summed bin magnitudes over 20–100 / 100–2000 / 2000–12000 Hz — the low band
    is 2 FFT bins (0–86 Hz). Kicks smear over 23 ms and their body (100–250 Hz) counts as mid.
  - Core `SpectralAnalysis` (own analysis, background, `SpectralAnalysisCache`: BASS float decode, cached per file
    by path + size + mtime, last 4 files): mono mix, Linkwitz-Riley 4th-order crossovers at **200 Hz** and
    **4 kHz**, per ~1 ms block the peak, the energy of each band (prefix sums: any range in O(1)) and a 10 ms RMS.
    A 6:23 song takes ~0.3 s decode + ~0.8 s analysis.
  - Height from RMS, not the sample peak: measured on galvanizer (limited hardcore master) the per-column peak is
    flat between kicks; the 10 ms RMS rises on every kick and snare.
  - Core `SpectralPalette`: band RMS over the column (at least 16 ms — one 50 Hz period), normalised halfway
    (exponent 0.5, ±12 dB max) to the song's own band balance, gains low −1 dB / mid 0 / high +1 dB, then max = 1 and
    squared for saturated hues; brightness 0.4 + 0.6·√level.
  - Calibration (scratch tool on the 8 Nautica songs, decoded with ffmpeg): onsets detected per band (low-band jump
    > 6 dB = "kick", mid + high jump without low = "snare", high-only jump = "hat"), grid search over edges, gains
    and normalisation. 150 Hz left most J-core kicks mid-dominant (their body is 100–250 Hz; beat-synchronous
    profiles of galvanizer / thedevilreincarnate show the low band *ducking* on the beat while mids rise); 200 Hz
    gives kick R-dominant 49 %, snare G 62 % / B 25 % (not red 87 %), hat B 28 %, all loud columns R/G/B 33/54/13 %
    (mostly green like the reference). Full normalisation scored the same on songs but turned a drum-only loop's
    kicks yellow, hence 0.5. Synthetic checks (unit tests): 50 Hz → red, 2 kHz → green, white noise → blue, a
    synthesised kick (150→45 Hz sweep) red, snare (190 Hz + noise) not red, hat (noise > 7 kHz) blue, kick lobes
    > 0.8 high with < 0.35 of that between beats.
- Options → Graphics → Editor: "Waveform style" (Spectral (default) / Classic green) and "Waveform opacity" (20–100 %,
  default 85 %), editor.ini `WaveformStyle` / `WaveformOpacity`. The framework waveform is only decoded while
  Classic is selected. `TimelineRuler.SetWaveform` became `SetAudio(path, classicLoader, length)`.
- Smoke `editing`: the song's spectral analysis loads and draws columns of more than one dominant colour
  (`10b-spectral-waveform`), and a generated 3 s wav (50 Hz burst, 2 kHz burst, white-noise burst) drawn by a
  stand-alone `SpectralWaveform` gives only red-, green- and blue-dominant columns over the three bursts and nothing
  in the silence (`10c-spectral-synthetic`). 6 new NUnit tests (`SpectralAnalysisTests`).

- Song select: **skill radar** like the game's music select — hexagon with NOTES on top, then PEAK, TSUMAMI,
  TRICKY, HAND-TRIP, ONE-HAND clockwise, axis colours and proportions measured on the game's radar artwork, value
  under each label, rim = 200 (official music_db radars run 0–200; larger values sit on the rim). Values: Core
  `Radar/` (`RadarCalculator`, `RadarInput`, `SkillRadar`), a port of ksh2vox `radar.py` 0.1.0 with the same
  formulas and 10..1000 clamp, numerically identical to the Python module on all 25 chart files of the local
  library (24 charts + ABYSS's ksh twin; `RadarTests` runs radar.py live when Python and the converter checkout
  exist). Computed off-thread once per chart file version (`RadarCache`). Informational: against the 8,572
  official charts' music_db radars the approximation errs by MAE 13 (NOTES, r 1.00), 28 (PEAK, r 0.92),
  28 (TSUMAMI, r 0.43), 25 (TRICKY, r 0.57), 43 (HAND-TRIP, r 0.79), 11 (ONE-HAND, r 0.93).
- Song select: the levels are **∇ level triangles** instead of `[EXH 19.0]` / `Slot: … Level: …` in the corner —
  NOV · ADV · EXH · INF-class (the song's own GRV/HVN/VVD/XCD/NBL) · MXM, each a downward triangle with a bevelled
  rim in the difficulty's colour, the name on the top edge and the level inside (tenths from 17), an empty outline
  for a slot the song lacks, the selected chart larger with a glow; a click selects that chart. Rim tones and
  proportions measured on the game's level artwork; drawn with primitives, no game image or font in the repo.
  Length, BPM, objects, chips/holds/lasers and the level estimate stay in the corner.
- Song select: Alt+mouse wheel outside the wheel (bottom-right corner, footer, header, details, radar) changes the
  volume with the editor's `VolumeOverlay` (hovered ring, else master); over the wheel the wheel keeps scrolling.
- Song select footer: "< Back" is now **EXIT**.
- Smoke `songselect`: radar values of three selected charts equal a fresh computation, five triangles in slot
  order with the song's levels and exactly the selected one highlighted, EXIT label, real-input Alt+wheel at the
  bottom-right corner moves master volume one step (overlay visible), over the wheel it does not, setting restored.

- Letter shortcuts follow the keyboard layout. osu-framework 2026.807.0 delivers keys only as physical positions
  (`KeyDownEvent.Key` comes from `SDL_KeyboardEvent.scancode` in `SDL3Extensions.ToKey`; no keycode/character
  field), so on a German QWERTZ keyboard Ctrl+Z was the key labelled Y. Binding texts now name letters logically;
  the editor's key binding container listens on the physical key that types the letter on the active layout
  (Core `KeyboardLayout`, Game `KeyboardLayoutService` reading `SDL_GetKeyFromScancode`, the call the framework
  uses for its own key labels), re-read on SDL's keymap-changed event and on window focus. The options capture
  stores what was typed (QWERTZ physical Y → `Ctrl+Z`), menus/Help label punctuation with the layout's character
  (Ctrl+Slash reads Ctrl+-), hand-written checks (Ctrl+O options, Ctrl+A in lists, Ctrl+C/V in F6) follow suit.
  Playtest keys stay positional, shown with the layout's label. Measured on this machine (layout 00000407): SDL
  maps scancode Y → `z`, Z → `y`, Semicolon → `ö`, Slash → `-` once video is initialised (US before). 5 NUnit tests
  (QWERTY identity, QWERTZ, AZERTY incl. a position that cannot be named, Cyrillic fallback, round trips).
- Menu bar: every item works or is gone. New: **File → Create a New Difficulty** (copy of the current difficulty or
  an empty chart → free slot → written as `nov.kson`/`adv.kson`/… via `EditorSession.EnsureWritableFolder` →
  Song Setup; Core `NewDifficulty`, 4 NUnit tests), **Open Difficulty** chooser (Ctrl+O; switching re-reads the
  folder), Save/Discard/Cancel prompts before switching, creating, Revert and Exit, **Export Song** kson / ksh / VOX /
  .zip / .svz (the song select's `LibraryManager.ExportAsync`), **Open Song Folder**, **About** (version,
  licences), View → Background Dim, Compose → Tool / Snap Divisor / Audio Rate / the five toggles, Camera → zoom /
  tilt point and reset at the cursor, Timing → Time Signature, Move All Notes (snap tick / beat), Edit → invert
  selection, clone, reverse, wide/slam toggles, bookmark navigation. Removed: Show Lane Labels (no such view) and
  Live Mapping Mode (not implemented; its key still answers "not wired yet"). An item whose action shows nothing
  itself toasts its name, so no click is silent. New action `NewDifficulty` (unbound by default, bindable).
- Window mode: Alt+Enter and F11 toggle Windowed ⇄ Borderless. The framework's own binding (prioritised
  `FrameworkActionContainer`, not overridable) cycled Windowed → Borderless → exclusive Fullscreen; the game's root
  input manager (`EditorUserInputManager`) now takes the two keys first. Borderless is the framework's
  fullscreen-sized frameless window (Windows: `SDL_SetWindowBordered(false)` at display size, +1 px under GL/Vulkan
  so drivers never make it exclusive; Alt+Tab friendly); new installs start there. A resizable "borderless window
  of any size" is not offered: the framework has no such mode and drives the border itself on every mode change.
- Smoke part `menus` (with `--open`): all 94 menu leaves clicked with the mouse, each with a checked visible effect
  (dialog, tab, tool, value, window mode, export result, at least a toast) and cleaned up; both new-difficulty
  flows end in Song Setup with a saved chart in the scratch song; Ctrl+O chooser; Revert confirmation; Ctrl+Z /
  Ctrl+Y on a simulated QWERTZ layout (physical Y undoes, physical Z redoes); Alt+Enter / F11 through the root
  input manager; Exit with unsaved changes (Cancel stays, Discard leaves). Smoke runs simulate US QWERTY so their
  injected physical keys mean the same on every machine.

- Bottom bar: the scrubbable length (time %, summary timeline, density column, Ctrl+End) is the audio file's
  length once it is loaded. A 6 s audio showed 30 s: the chart range was floored at 30 s and the clock took the
  longer of that and the track (`EditorClock.LengthMs`, old fallback 119 s). Now: audio length; when the chart's
  last object lies after the audio, the range extends to it and the summary shades that stretch red "no audio";
  without audio the chart end + 2 s (at least 10 s). Setting `LengthMs` only raises a minimum (smoke tests).
- Playback rate 150 % removed (25 / 50 / 75 / 100 %; the clock and Alt+Up stop at 100 %). The active rate gets a
  glowing white underline instead of the arrow that covered the number.
- Bottom-bar buttons: bookmark add / remove nearest / previous / next (tooltips with the current keys) and
  **Collab** (two people) opening the bookmark list. Every bookmark change (Ctrl+B, Ctrl+Shift+B, the buttons,
  the list's rename / colour / delete) now goes through the undo stack (`SetBookmarkCommand`): Ctrl+Z / Ctrl+Y
  work on them. Ctrl+B used to write past the undo stack; the screen's ms list is now a read-only view.
- Click on the time display: "Jump to Time" dialog (osu!stable) with the current time pre-selected (Ctrl+C
  copies it); a typed time (`mm:ss:mmm`, `mm:ss.mmm`, `ss.mmm`, ms) or a pasted timestamp + Enter jumps, Esc
  closes, garbage keeps the dialog open with the reason.
- Modding timestamps (osu!-style): Ctrl+C on a selection also puts
  `01:23:456 (01:23:456|BT-A,01:23:581|FX-L hold,01:24:000|LASER-L 0.25>0.75) - ` on the OS clipboard; pasting
  one into the time dialog jumps there and selects exactly those objects (missing / ambiguous / unreadable items
  are reported). `mm:ss:mmm -` alone jumps. Core `ChartTimestamp` (format, tolerant parser, resolver), 51 NUnit
  test cases (round trip for every object kind, BPM change + offset, tolerant spellings, missing / ambiguous /
  slam-vs-plain disambiguation). Format in README "Modding timestamps".
- Smoke part `bottombar` (no `--open` needed; generated 6 s sine song): length = audio, a note at 10 s extends it
  and shows "no audio", rates without 150 % and the underline following clicks, the four bookmark buttons with
  Ctrl+Z / Ctrl+Y on add / remove / rename, the Collab button, the time dialog (Ctrl+C to the OS clipboard,
  Enter, garbage, Esc), the timestamp of a mixed selection on the OS clipboard and paste-to-select round trip —
  all through real injected mouse / key input. The walk part now expects Ctrl+Z to undo its Ctrl+B bookmark.

- Compose, osu!mania chip <-> hold: the BT/FX hold tool (Shift swap included) pressed on a chip of its kind and
  dragged turns it into a hold from the chip's pulse with the dragged, snapped length; pressed on a hold (head, body
  or tail) it drags that hold's length, and dragged back to the start it leaves a chip. Released without a change
  nothing happens. The BT/FX chip tool clicked on a hold makes it a chip at the hold's start. Each is one undo step
  (Core `SetNoteLengthCommand`: in place, so selections survive; a chip loses FX effects, a shortened hold the
  effects past its end, a hold a chip's key sound; undo restores all). `OverlapRule.CanResize`: a drag into the next
  note stops at the last length that fitted (ending on a chip = touching, allowed). 1 new NUnit test.
- Wide-laser anchors in the overhang can be hovered, clicked, dragged and right-clicked in select mode. Cause: the
  playfield accepted positional input in the 100 px overhang beside its drawable only while the laser tool was
  active (`ComposePlayfield.ReceivePositionalInputAt`), so in select mode the playfield was not in the input queue
  at all there — a wide anchor at 0.04 (x = -58) was drawn but unreachable. Now every tool receives input in the
  overhang (the field container still masks it).
- Laser hover preview (osu!stable slider anchors): hovering a laser in select or laser mode shows its anchors and
  their types without selecting it (`ComposeEditor.AnchorPreview`; the laser tool now hovers plain points too).
  Options → Graphics → Editor "Show laser anchors on hover" (editor.ini `ShowLaserAnchorsOnHover`, default on);
  off = the hover still outlines, only the selected laser shows anchors.
- Smoke `compose-input` (real mouse + Shift input, `EditorGame.SmokeComposeHolds.cs`): BT chip -> hold by drag,
  undo/redo, hold length by body drag, chip tool on a hold, tail back to zero, click keeps a chip, drag stops at the
  next chip, FX the same through Shift; a wide laser starting at 0.04 in the left overhang (0.05 is off the 1/50 value grid): hover preview, right-click
  menu, click select, anchor drag in select and laser mode, normal-laser hover preview in laser mode, setting off =
  no hover anchors.

- Export badges follow the ksh2vox version in use. Core `ExportSupport.For(version)`: below 0.17.0 (or while the
  version is unknown) the VOX column stays all ✗; from 0.17.0 on it shows what the converter now maps - zoom_top /
  zoom_bottom (~, clamped to ±100), tilt auto (~, zero → normal, biggest → bigger) and manual (~, clamped to ±1),
  FX effect definitions / hold effects (~, 12-block table, unmappable types play without effect), laser filter (~,
  peak/LPF/HPF/bitcrush, one per section), bg_no / post effects / SPCONTROLER rows (✓); zoom_side, center_split,
  rotation_deg, spin/swing, bg images/layer/movie, parameter changes, filter gain/delay, slam volume and custom chip
  sounds stay ✗. New rows scroll speed (ksh ✓, VOX 0.17.0 ~: negative speeds become 0) and stops (VOX 0.17.0 ✓).
  Every VOX pill has a tooltip saying what is lost (new `TooltipContainer` around the screen layer).
  The version comes from the new `VoxConverterDetector` (one `VoxExporter.GetInfoAsync` per session, shared with
  the options' Export section, re-run on a converter/Python path change); until it answers or when it fails the
  badges show the conservative values as "vox?". The camera tab's export table names it: "VOX via ksh2vox 0.17.0".
  Smoke `camera`: the badges match the detected version (the submodule's 0.12.0: all ✗), then the smoke storage's
  `VoxConverterDir` points at a 0.17.0+ checkout (`SDVXEDITOR_SMOKE_CONVERTER`, else `SDVX-Chart-Converter` found
  beside the editor) and zoom_top / tilt / bg_no / post effects switch and the table header names 0.17.0.
  7 new NUnit tests (14 cases).
- Laser path editing in the osu!lazer slider style. Core `LaserPath` / `LaserAnchor` (types straight / perfect
  curve / Bezier, inherit anchors, auto typing 2 → straight, 3 → perfect curve, 4+ → Bezier, two curves meet at a
  Bezier anchor), `LaserPathBaker` (time-monotone evaluation: backward loops are cut; bake to kson points with an
  exact kson curve for 3-anchor Beziers, otherwise Douglas–Peucker samples within 0.004 on the 5-pulse VOX tick
  grid; ksh bake on a 1/24 grid, never closer than 1/32 to an anchor), `LaserPaths` (a section's path is trusted
  only while its points are still its bake), `LaserPathEditing` / `LaserPathCommands` (undoable via
  `ReplaceLaserSectionsCommand`). kson stores the anchors in `impl["sdvx-editor"].laser_paths` (a path whose points
  were edited elsewhere is dropped on read); the ksh writer writes the ksh bake for curved paths; mirror keeps the
  curve. Laser tool: click-to-place with live preview, click an anchor to retype, right click finishes, Esc
  cancels (drag still draws a straight section). Select tool: hover outline + anchors, click selects, anchors drag,
  Ctrl+click inserts, right-click menu (anchor type / delete, segment curve type). 25 new NUnit tests.
- The laser tile shows a big L / R badge in the laser colour and has a clickable L | R switch under it.
- Smoke `compose-input`: the laser path steps above through real mouse input (switch, 3 anchors = perfect curve,
  4th = Bezier, retype while placing, finish, hover/select, menu "Straight", anchor drag, kson reload, ksh export
  read-back); the test stretch now fits slow songs (the unit shrinks to 1/2 or 1/4 beat when the playfield cannot
  zoom out far enough — Canto IX failed there before).

- Options overlay (Ctrl+O on song select, the song select "Options" button, editor File → Options… /
  Ctrl+Shift+O), osu!stable style: slide-in panel with section sidebar and type-to-search; General (songs folder,
  launcher config, language placeholder), Graphics (window mode, frame limiter, Show FPS), Audio (volumes,
  universal offset + dialog, hitsound skin, chord layers, per-event mapping), Input, Playtest (mod speed,
  judgement windows, knob sensitivity/axes, mouse knobs, laser key speed, autoplay, hitsounds, assist tick),
  Export (converter folder, Python, export folder, detected ksh2vox version with a warning below 0.16.0). Values
  are saved immediately. New editor.ini keys `EditorBindings`, `ShowFps`, `SongsFolder`, `LauncherConfigPath`,
  `ExportFolder`, `Language`. The song select "Options" button opens these options; chart options stay on
  right-click.
- Rebindable keys: every editor action and every playtest control (two slots each; capture with modifiers,
  middle/side mouse buttons and wheel for the editor, keys and `JoyN` controller buttons for the playtest),
  conflict check per family with Swap / Clear other / Cancel, reset per row and per group. Editor bindings apply
  live (key binding container, menu shortcut column, Help → Keyboard Shortcuts). Core `Input/BindingTable` +
  `KeyComboText` with NUnit tests; `PlayBindings` now sits on the same table (`back=none` unbinds).
- Smoke part `options` (real injected mouse/key input). Smoke runs snapshot every editor/framework setting and
  restore it before the ini files are written.
- Fix: no dropdown could be picked from with the mouse. The popup layer closed the list on mouse *down*
  wherever the press landed, so the item under the cursor was gone before its click (fired on release) -
  every Song Setup / F6 combo box, the song select Group/Sort dropdowns and the editor panel dropdowns.
  A press inside the list now stays with the list. Lists (dialog combo boxes and osu! dropdowns) take
  keyboard focus when open: Up/Down/Home/End/PageUp/PageDown, Enter/Space pick, Esc/Tab close (Enter no
  longer reaches the dialog's OK, Esc no longer its Cancel); the list closes when its control disappears.
- NumericUpDown (F6, Song Setup): ▲/▼ step on press and repeat while held (400 ms, then every 60 ms);
  a press on them can no longer be taken by the text box underneath (a press within the double-click time
  after a click in the field was eaten as the text box's word-select, so the arrow did nothing); Up/Down
  (PageUp/PageDown = 10 steps) step while the field has focus (the dialog swallowed them before).
- F6 "+": a point of the chosen kind that already exists at the playhead (e.g. the scroll speed point most
  kson charts have at 0:00, with the playhead at 0:00) is selected instead, and the dialog says so ("... already
  exists at 00:00:000 (selected). Move the playhead to add another.") - "+" used to return silently and seemed
  broken. Stops follow the same one-per-position rule now (a second stop at the same pulse was added before).
- Dialog list views take keyboard focus on click: Up/Down/Home/End/PageUp/PageDown move the selection,
  Shift extends it, Ctrl+A selects all.
- Smoke part `ui-input`: the UI kit driven by real injected mouse/key input (root input manager, button
  down/up frames apart, real input handlers paused), borderless at screen size and windowed 1600x900:
  dropdowns, combo boxes (list + editable), spinners (click, hold, wheel, keys, after focusing the field,
  double click), check box, radio, tab headers, list click/Ctrl+click/keys, buttons, tool tiles, snap
  slider, osu! stepper, osu! checkbox. `SDVXEDITOR_UIINPUT_SOFT=1` reports failures and runs on.

- Playtest (F5), osu! test-mode style: perspective SDVX track with the camera tab's camera math (zoom,
  tilt incl. auto tilt, rotation, split, spin/swing), scroll speed + stops, hi-speed (F3/F4, -/+);
  keyboard (KSM/USC layout), controller buttons, knobs as joystick axes or mouse X/Y; CRITICAL/NEAR/ERROR
  with early/late, hold and laser ticks and slams after SDVX/USC rules, score, EX score, chain, effective
  rate, autoplay (Tab), result summary; starts 2 s before the editor position at the editor's rate and
  audio offset, returns where it stopped. Rules in Core `Gameplay/` with NUnit tests (incl. autoplay = max
  score on the local Nautica charts and a sample of the user's own official charts).
- Camera tab: curve lanes for zoom_top/bottom/side, center_split, rotation_deg, tilt (manual +
  auto strip) and spin/half-spin/swing; click/drag/Shift slam/Alt curve/right-click delete, all
  undoable. Live 2.5D camera preview from Core `CameraEvaluator`/`CameraProjection`. Background,
  layer, movie, VOX `bg_no`, post effects and SPCONTROLER rows (editor data in kson `impl`).
  Export badges (kson/ksh/vox) from Core `ExportSupport`.
- Effects tab: FX effect definitions + parameters, hold assignment, parameter changes, laser
  filters / gain / slam volume, custom FX chip sounds (import, assign, preview; played by the
  editor's hitsound player).
- Solution scaffold: `SDVXEditor.Core` (empty, no framework dependency),
  `SDVXEditor.Game`, `SDVXEditor.Desktop`, `SDVXEditor.Resources`,
  `tests/SDVXEditor.Core.Tests` (NUnit).
- osu-framework `2026.807.0` (newest release targeting net8.0; usable from net9.0).
- `EditorGame` with a screen stack; window title "SDVX Chart Editor".
- Theme `EditorColours`: osu!-style greys, pink `#ff66aa` highlight, NABLA
  accent set from the launcher's design tokens.
- Placeholder screens: Song Select, Editor (tab strip switchable with F1-F4,
  bottom bar, F5 playtest, Esc back), Playtest (Esc back).
- CLI: `--launcher-config`, `--songs` (stored only), `--smoke-test`.
- Font: Roboto from osu-framework; Aller rejected for licence reasons (`docs/FONTS.md`).
- Fallback fonts for non-Latin titles (`ΛμreoLe=RushØVER=E`, `じゃすと・べりー・くいっくりー！`,
  `～` used to render as `?`): Noto Sans (Latin-ext/Greek/Cyrillic) and Noto Sans JP (all of cp932 —
  JIS X 0208 kanji, kana, symbols, fullwidth forms), Regular + Bold, SIL OFL 1.1, licence shipped as
  `OFL.txt` beside the data. Registered behind Roboto by `EditorFont.AddFallbacks`, so every SpriteText
  falls back by itself; the CJK store is rendered at 0.6x and scaled back via a nested `FontStore`.
  14.6 MiB of glyph pages in `SDVXEditor.Resources/Fonts/`. Generated reproducibly (pinned commits +
  SHA-256, byte-identical output) by `tools/FontGen` (`dotnet run -c Release --project tools/FontGen`).
- `external/SDVX-Chart-Converter` added as git submodule.
- `Formats/Vox/VoxReader`: read-only VOX (v10/12/13): BPM incl. `4-` stops,
  time signatures (192/den ticks per beat), BT/FX chips and holds, lasers with
  slams and wide flag. Parses all 8,602 official files of a local install.
- Level estimation (`Difficulty/`, namespace `LevelEstimation`): strain-model
  features, ridge + isotonic calibration, ± band, per-section strain graph.
  `tools/DifficultyCalibrator` fits it from the user's own install
  (song-grouped CV MAE 0.50 slot-aware / 0.58 slot-free; 24 Nautica fan
  charts MAE 0.97 / 0.91).
- VOX export via ksh2vox: `python/vox_host.py` (long-lived NDJSON host,
  stdout protocol-only, imports ksh2vox + radar.py by file path) and
  `Formats/Vox/{PythonHost,PythonLocator,VoxExporter}`. Charts are staged as
  `nov/adv/exh/inf/mxm.kson`; kson 1.0 `time_sig` is rewritten to the form
  ksh2vox 0.16.0 reads. All 8 Nautica songs convert; the .vox match ksh2vox
  run on the original folders except `#END POSITION` (kson has no end marker).
  `tools/fetch_python.ps1` bundles embeddable Python 3.12.10 + Pillow.
- Song library (`Library/`): `LauncherConfig` (`nautica_charts_path`, BOM ok,
  broken JSON refused), `SongLibrary` (editor + launcher roots, fingerprint
  cache identical to ksh2vox's `folder_fingerprint`, change detection,
  fork-on-edit for launcher songs), `SongImporter` (folder / .zip with the
  launcher bridge's caps and zip-slip checks / .ksh·.kson / audio → new song).
- Compose core (`Screens/Edit/`): `EditorSession` (song folder, charts, active
  chart, `CommandStack`, `TimingMap`, selection, shared clipboard, song audio
  through a framework track store rooted at the song folder). Ctrl+S writes
  `.kson`; a `.ksh` is never overwritten — it is saved as `<stem>.kson` beside
  it (which the folder then prefers). `EditorClock` follows the track
  (drift-corrected), rate 25-150 % via track tempo (pitch kept), seeks snap to
  the divisor while paused.
- Playfield (`Compose/ComposePlayfield`): vertical, upward-scrolling,
  time-linear; LASER-L · BT-A..D · LASER-R with FX under BT-A+B / C+D,
  holds, lasers with curves, slams and wide (2x range), divisor-coloured grid,
  BPM / time-signature / scroll-speed / stop markers in the side margins. Only
  visible objects are drawn, as quads of one `QuadBatch` draw node (no
  drawable per object).
- Tools (`Compose/ComposeEditor`, Core `Editing/LaserEditing`): chip click,
  hold drag, right-click delete, select (click / Ctrl / box / drag-move, snapped
  lane + time), laser draw / insert point / drag point (neighbour pulse = slam),
  Ctrl = free value (default 1/50), Shift = wide, Q / E / 6-again = laser side.
  Every drag is one undo step. `EditorChartActions` replaces the stub: save,
  undo/redo, cut/copy/paste at time, Ctrl+D clone, mirror, reverse, invert
  selection, J/K and Ctrl+arrows, wide/slam toggles, clear, revert, next
  difficulty (Ctrl+O).
- Density column (minimap, click/drag seeks), top timeline ruler with the song
  waveform (framework `WaveformGraph`) and bpm tags, bottom summary markers
  (BPM, scroll speed, bookmarks, preview). One shared zoom for timeline and
  playfield (± buttons, Alt+wheel over either).
- Hitsounds while playing (toggle, synthesised samples — no third-party audio).
- Hitsound sync, measured with a WASAPI loopback of the real editor: a hitsound
  started when the clock reached its note was heard 55–90 ms after the music's
  note (the track position runs ~27 ms, 20–41 ms, behind the audio already on
  its way to the device; a sample needs ~33 ms from `Play()` to the device).
  Hitsounds now start `LeadMs` early (2 × (BASS latency + device buffer) + 6 =
  60 ms here); measured afterwards: 10 ms early on Jumpy Star. Note timing
  itself was right: editor `PulseToMs` equals ksh2vox's timing for every note of
  all 24 Nautica charts, `o=` has the right sign, BASS decodes sample-exact like
  ffmpeg.
- Universal audio offset (`AudioOffset` in editor.ini, osu!-style, per
  machine): the track runs that many ms ahead of the drawn/judged time; positive
  = the sound arrives later than the picture. Timing → "Audio Offset &
  Hitsounds..." (also under View); the tap-along wizard itself is still a stub.
- Hitsounds from the user's own osu! skin, never copied into the repo:
  `HitsoundSkinFolder` = Auto (the `Skin =` line of `osu!.<user>.cfg`, nothing
  else of that file is read), Built-in, a folder under `osu!\Skins` or an
  absolute path; .wav/.ogg/.mp3. `HitsoundMapping` maps BT/FX chip and hold
  head, slam and laser start to a sample (`bt=soft-hitnormal`,
  `fx=drum-hitnormal`, `slam=normal-hitnormal`, `laser=none`); a missing file
  falls back to the synthesised sound with one log line. Notes on one pulse are
  layered like osu!mania chords, up to `HitsoundChordLayers` (4).
- CLI `--open <song folder> [--chart <slot>]`. `--smoke-test --open …` edits,
  undoes, saves and reloads a copy of the song and measures frame times.
- ksh reader: densely sampled laser movement (K-Shoot exports of kson points
  closer than 1/32) is no longer read as runs of tiny slams. A change within
  1/32 is a slam only if no further sub-1/32 change follows it, a sub-1/32
  pair also starts from a held value and has a follower within 1/16 (libkson
  writes shorter slams only then); baked curve samples are never closer than
  1/32. ABYSS MOTION .ksh slams now equal its .kson (lane R 129 → 100); the
  other 23 Nautica charts are unchanged. `KshReadOptions.RecognizeDenseMovement
  = false` restores the plain ksh2vox/libkson pair rule.
- Collab bookmarks: optional label and colour per bookmark (Core `Bookmark`, `SetBookmarkCommand`, undoable,
  marks the chart dirty). Edit → Bookmarks… lists them (rename, 8 colours, delete, add at cursor, jump);
  markers in the bottom summary and the top timeline show their colour and the label on hover. kson
  `impl["sdvx-editor"].bookmarks` `[y, label, "#rrggbb"]`; ksh now keeps them as `//#bookmark|#rrggbb|label`
  comment lines (no longer a ksh loss) — ksh2vox output verified identical with 790 such lines inserted into
  the Jumpy Star charts; VOX cannot carry them.
- `.svz` package (`docs/SVZ_FORMAT.md`, Core `SvzPackage`): one song folder zipped with every chart as .kson
  (ksh originals kept), audio/images/key sounds, and an optional `sdvxeditor.json` manifest. Export from song
  select (Export → ".svz (share / collab)") and the editor (File → Export); import by drop or `--import <file>`
  through `SongImporter` (same zip-slip / size checks; any zip of a song folder works). A name clash asks
  Replace / Keep both / Cancel. No file association is registered.
- File → Open Difficulty for Reference: another chart of the song read-only and greyed right of the compose
  playfield (main chart left, reference right), same clock/zoom/scroll; box/click select there, Ctrl+C, Ctrl+V into the edited chart; Esc closes.
- Laser tool reach: normal lasers can be placed, dragged and hit up to one BT lane (`PlayfieldLayout.LASER_REACH`)
  outside the lanes and clamp to 0/1 there. Wide lasers use the true 2x scale everywhere, like the game: their outer
  quarters (0..0.25, 0.75..1) are drawn and edited past the lanes, up to two BT lanes beyond each edge; every
  playfield keeps that overhang free beside it. The side margins grew from 70 to 80 design px (playfield 540 → 560).
  With a reference open, the main chart sits in the left half and the reference in the right half.
- Fix: in the compose tab every object was placed ~100 px right of the cursor once the playfields got their wide-laser
  overhang padding: the playfield read `MouseEvent.MousePosition`, which is in the parent's (padded container's) space.
  It now converts the screen-space position itself (also the bookmark hover in the timeline and summary). New smoke
  part `compose-input` clicks through the real input path (BT/FX chips, laser + wide laser through both overhangs,
  right-click delete, box select, drag-move, a click in the reference) borderless at screen size and at 1600x900,
  with and without a reference, and asserts the lane / value under the mouse.
