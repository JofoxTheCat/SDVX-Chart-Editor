# SDVX Chart Editor — Architecture

A standalone, native chart editor for SOUND VOLTEX-style charts (4 BT lanes,
2 FX lanes, 2 lasers) whose look and workflow follow the **osu!stable mania
editor**, with a song select that feels like SOUND VOLTEX ∇ (NABLA).

Decisions (Johannes, 2026-10-25):

| Topic | Decision |
|---|---|
| Stack | C# / .NET 9 + **osu-framework** (NuGet `ppy.osu.Framework`). Native, not web-based. |
| Repo | `D:\SDVX-DEV\SDVX-Chart-Editor` (own repo, `JofoxTheCat/SDVX-Chart-Editor` once Johannes pushes). |
| Launcher | Not embedded. The launcher **starts** the editor (Plugins & Tools); the editor reads the launcher config to show the songs the launcher downloaded (`nautica_charts_path` in `sdvx_rpc_config.json`). |
| VOX export | ksh2vox is a **dependency** (git submodule `external/SDVX-Chart-Converter`). The editor calls its converter functions itself — no launcher, no addon install needed. |
| Playtest | Own SDVX playfield (perspective track, lasers, camera, scroll speed, judgement), native — osu! F5 style, starts at the current position. |
| Layout | Timing tab, F6 timing window, song setup, song select and the timeline waveform follow Johannes' reference screenshots. |

Reference material lives outside this repo and is never committed
(screenshots, layout notes, chart format model, difficulty estimation, NABLA
design tokens).

---

## 1. Solution layout

```
SDVX-Chart-Editor/
  SDVXEditor.sln
  src/
    SDVXEditor.Core/          net9.0 classlib, NO framework dependency
      Charts/                 chart model (superset of kson), timing math
      Formats/Kson/           kson read/write (System.Text.Json)
      Formats/Ksh/            ksh read/write
      Formats/Vox/            VoxReader (read-only, v10/12/13) + VOX export = Python host call (see §5)
      Library/                song library scan, import (folders, .zip), song-source links (MusicLinks, SongSourceSync)
      Credits/                Thanks list model (CreditsFile; the JSON is embedded in SDVXEditor.Resources/Credits)
      Difficulty/             strain model + level estimate (namespace SDVXEditor.Core.LevelEstimation, see §7)
      Patterns/               pattern analysis (features for radar + level), additive tree models (§7b, docs/RADAR_MODEL.md)
      Radar/                  classic radar (radar.py port) + fitted radar model (§7a)
      Editing/                undoable commands, selection, clipboard
      Audio/Effects/          audio effect evaluation + DSP (pure C#; §16)
      Input/                  BindingTable<TAction> (defaults + overrides, conflicts, swap) and KeyComboText
    SDVXEditor.Game/          osu-framework Game: screens, drawables, audio
      Audio/                  EffectAudioHost: the effect DSP on the song's BASS stream (§16)
      Screens/SongSelect/
      Screens/Edit/           Compose · Timing · Camera/FX · Song Setup
      Screens/Play/           playtest
      Graphics/               osu!-style UI primitives, theme
      Overlays/Options/       options overlay (Ctrl+O): sections, search, key binding editor
    SDVXEditor.Desktop/       exe entry point, CLI args, single-instance
    SDVXEditor.Resources/     own fonts/textures/shaders/samples (no osu! or Konami assets)
  tests/
    SDVXEditor.Core.Tests/    NUnit: format round-trips against real charts
  python/
    vox_host.py               long-lived host: imports ksh2vox, NDJSON over stdio
  external/
    SDVX-Chart-Converter/     git submodule (ksh2vox.py, radar.py, template_v13.vox)
  tools/
    fetch_python.ps1          downloads the Windows embeddable Python into build output
    DifficultyCalibrator/     C# console: fits the level + radar models from the user's OWN install
  docs/
```

**Core has no osu-framework dependency**, so formats, timing and difficulty
are testable headless and reusable (e.g. by the launcher later).

## 2. Chart model

Internal model = superset of kson (see `reference/notes/chart-format-model.md`).
Time base is kson's **240 pulses per quarter note**; ksh and VOX resolutions are
derived on export. Everything is in pulses; milliseconds come only from the
tempo map (`TimingMap`: BPM changes + time signatures + stops). Scroll speed
(osu! "SV") is a separate curve and never changes timing.

## 3. Screens (osu! mapping)

| osu!stable | here |
|---|---|
| Song select | NABLA-styled wheel; drag & drop of folders / .zip / .ksh / .kson / audio creates or imports a song |
| compose | vertical scrolling playfield: LASER-L · FX-L · BT-A..D · FX-R · LASER-R; tools Select / Chip / Hold / Laser |
| design → **camera** | zoom/tilt/shift/rotate curves, background, post effects |
| timing | BPM / offset / base scroll speed, metronome lamps, tap BPM, button to the F6 window |
| hitsound → **effects** | FX/laser effect assignment and parameters, custom FX chip samples |
| song setup | title/artist/charter/illustrator, level + difficulty slot, jacket, background, audio, preview |
| Test (F5) | playtest from current position, Esc back to the editor |
| Timing window (F6) | BPM, time signature, scroll-speed and stop points in one list, with ramp/scale/shift tools |

Bottom bar: time, %, play/pause/stop, playback rate (25/50/75/100 %; 150 % removed, the active rate has a glowing underline),
summary timeline with coloured markers, bookmark buttons (add / remove / previous / next, all through the
undo stack as `SetBookmarkCommand`) and a Collab button (bookmark list). A click on the time opens the
jump-to-time dialog, which also takes modding timestamps (Core `ChartTimestamp`: format, tolerant parser and
resolution to `NoteRef` / `LaserPointRef` with missing / ambiguous items reported). The scrubbable length
(`EditorClock.LengthMs`) is the loaded audio's length, extended to the last object when the chart outlasts the
audio (that stretch is shaded "no audio"); without audio the chart end + 2 s, at least 10 s. Setting `LengthMs`
only raises a minimum (smoke tests). The waveform is drawn in the top timeline: by default `SpectralWaveform`
(DJ-style RGB columns: height = loudest 10 ms RMS, colour = band balance from Core `SpectralAnalysis` /
`SpectralPalette`, analysed once per audio file in the background by `SpectralAnalysisCache`), or the framework's
green `WaveformGraph` (Options → Graphics → Editor, `WaveformStyle` / `WaveformOpacity`).

## 4. Library & launcher

- Library roots: the editor's own songs folder **plus** `nautica_charts_path`
  read from the launcher config (`--launcher-config <path>`, otherwise looked up
  next to the launcher). Launcher songs are opened read-only-by-default: editing
  one forks it into the editor's folder so the launcher's import map (keyed on
  folder name, see the converter's CLAUDE.md §3a) is never disturbed.
- The launcher starts the editor as a Tools entry; no IPC is required.
- Config lookup (`LauncherConfig.Locate`): `--launcher-config`, else the cwd,
  the editor folder and up to three parents, then `LauncherConfig.DevLocations`.
  The first existing file decides; one that does not parse is reported, never
  skipped (the bridge's `config_unreadable` rule).
- Forks are named `<folder> (edit)` and carry `.sdvxeditor-fork.json`
  (source path), so a second edit reuses the fork. Scans are cached in
  `library-cache.json` next to the songs folder, keyed on the same
  name+size+mtime fingerprint as ksh2vox's import map.
- Drag & drop (`SongImporter`): folder, `.zip` (one top folder or flat;
  1 GiB / 5000 entries / 200× above 1 MiB, zip-slip refused, `__MACOSX`
  ignored), `.ksh`/`.kson` (its folder), audio (new song, empty EXH chart at
  120 BPM, title/artist from ID3v2 / Vorbis comments / "Artist - Title").

- Game side (`SDVXEditor.Game/Songs/LibraryManager.cs`, a Component cached in `EditorGame`): every
  call into the non-thread-safe `SongLibrary` runs on the thread pool behind one gate; results are
  published to the update thread (`Songs`, `SongsChanged`, `State`). Scan on song select entry, a
  `DetectChanges` pass on window focus and after the editor closes. `--songs` is an extra root; in
  `--smoke-test` it (or a temp folder) is the editor root, so the test never writes into real songs.
- Song select: `SongListQuery` (filter → group → osu!-style sets → sort) feeds the virtualised
  `SongWheel` (only near-viewport panels exist). Jackets/backgrounds load through
  `Graphics/Textures/ImageTextureCache` (ImageSharp decode + downscale off-thread, cached per
  path+mtime; large variants LRU-disposed). Background = chart `bg` image, else the jacket blurred.
  Window file drops (`IWindow.DragDrop`, one call per file, batched 100 ms) go to `SongImporter`.
  A launcher song opened for editing gets `EditorSession.ForkBeforeSave`, which calls
  `SongLibrary.EnsureWritable` on the first save; delete refuses launcher songs and sends editor
  songs to the recycle bin. Exports go to `Exports\` beside the editor songs folder, never data_mods.
- Song select chart panel (between details and wheel): the selected chart's **skill radar**
  (`SkillRadarDisplay`, hexagon NOTES top → PEAK → TSUMAMI → TRICKY → HAND-TRIP → ONE-HAND clockwise, rim = 200,
  the top of the official music_db range; values from Core `Radar/`, computed off-thread once per chart file
  version by `RadarCache`) and the song's difficulties as **∇ level triangles** (`LevelBadgeRow` / `LevelBadge`:
  NOV · ADV · EXH · INF-class · MXM, empty outline for a missing slot, the selected one larger with a glow, click
  selects). Both are drawn with `QuadBatch` triangles; geometry and colours were measured on copies of the game's
  own artwork (`ver06/radar.ifs`, `ver07/psd_level.ifs`) — no Konami image or font is in the repo. Footer: EXIT.
  Alt+wheel anywhere but over the wheel's panels (footer and bottom-right corner included) changes the volume
  through a `VolumeOverlay` that exists only while in use.

## 5. VOX export

The editor writes the chart as `.kson` into a temp song folder and asks
`python/vox_host.py` — a bundled Windows embeddable Python running the
submodule's `ksh2vox` — to convert it. The host imports `ksh2vox` and calls
its functions directly (`convert_song` …), one long-lived process, JSON lines
over stdin/stdout, stdout reserved for protocol (same rules as the launcher
bridge). On Linux the system `python3` is used.

- Python: setting > `python/runtime/python.exe` (`tools/fetch_python.ps1`,
  git-ignored) > `py -3` / `python` on PATH; each candidate is probed with
  `--version` (3.10+). Started with `-E`, not `-I` (that hides user-site Pillow).
- Converter: setting > the submodule. The submodule is GitHub v0.12.0: it has
  no import map and no `progress=`, so only explicit-id exports work with it
  and the exporter warns below 0.16.0.
- Staging: one `<slot>.kson` per chart (filename carries the slot, kson cannot
  say MXM) plus copies of audio/jacket, in `%TEMP%/SDVXEditor/vox-*/<song folder
  name>` — the folder name is ksh2vox's import-map key, so it stays stable.
  Non-integer levels are floored (ksh2vox reads `int()`), the preview window
  is written explicitly, and kson 1.0 `time_sig [idx,[n,d]]` becomes
  `{idx,n,d}` because ksh2vox 0.16.0 cannot read the spec form.

## 6. Legitimacy

No osu! skin/resources and no Konami files in the repo. osu-framework is MIT.
The look is rebuilt from drawables, not copied textures. Difficulty
coefficients are fitted locally from the user's own install by
`tools/DifficultyCalibrator`; only the resulting numbers (`coefficients.json`,
`radar_model.json`: weights, tree split thresholds, leaf values) are embedded in Core, never charts,
per-song data or excerpts.

## 7. Level estimation

`SDVXEditor.Core.LevelEstimation` (folder `Difficulty/`; the namespace is not
`…Core.Difficulty` because that would shadow the `Charts.Difficulty` type in
every `SDVXEditor.Core.*` file).

- `DifficultyFeatures.Extract(Chart)` — osu!mania-style strain (a family of decay bases,
  per-hand / button / knob streams, 400 ms section peaks), density windows,
  laser load (travel, slams, reversals, wide, both-lasers), chords, jacks,
  hold nesting, tempo. Pattern features (§7b) join them as `pat.<name>`.
- `DifficultyEstimator.Default.Estimate(chart)` → `DifficultyResult`
  (`EstimatedLevel` one decimal, `Band` ±, `PerSectionStrain` for the strain
  graph, `Features`, `TopContributors` incl. a "pattern trees" entry, `LowConfidenceReasons`).
  Model = standardised ridge (+ residual gradient-boosted trees over strain, pattern and slot
  features, coefficient schema 2) → isotonic post-map → residual band per level; slot-aware (default)
  and slot-free variants. The pattern analysis only runs when the loaded model has pattern trees.
- Coefficients: embedded `Difficulty/coefficients.json`; if it is missing the
  estimator uses a built-in 4-term fallback (`FallbackConstants`, band ±2,
  flagged low-confidence).
- Shipped 2026-10-26: slot-aware = ridge + pattern trees + isotonic (CV MAE 0.423, was 0.490 refitted /
  0.501 before; fan charts 0.88, was 0.97), slot-free = ridge + isotonic refitted (0.557; trees were
  better on CV, 0.468, but not on the 24 fan charts). Full numbers: docs/RADAR_MODEL.md §5.2.

## 7a. Skill radar

Two implementations, same `SkillRadar` result:

- **Fitted model (shown in song select).** `SkillRadarModel.Default` → `PatternAnalysis` features → one
  additive model per axis from the embedded `Radar/radar_model.json` (linear base + gradient-boosted trees,
  evaluated in C#, no ML runtime) → integer on the music_db scale 0..200. CV MAE vs the official music_db
  radar: NOTES 0.1, PEAK 2.0, TSUMAMI 2.3, TRICKY 10.7, HAND-TRIP 2.8, ONE-HAND 3.2 (r ≥ 0.97 except
  TRICKY 0.83). Without the resource it falls back to the classic one. `RadarCache.Compute` is the single
  entry the song-select panel and the smoke check use.
- **Classic** `SDVXEditor.Core.Radar.RadarCalculator`: a line-by-line port of ksh2vox's `radar.py` 0.1.0
  (itself kshoot-tools' `radar.ts` formulas, MIT, + reimplemented kshoot.js statistics).
  `RadarInput.FromChart(chart, RadarSource)` rebuilds radar.py's `parsed` view from the editor model — 48 ticks
  per beat, ksh positions floored and kson ones rounded half-to-even, a ksh slam re-expanded to its two lines
  1/32 apart, the ksh header `t=` as an extra BPM event, kson's end rule incl. `end_pulse` — so
  `RadarCalculator.ComputeFile` gives radar.py's numbers on the same file (`RadarTests` runs both on every local
  library chart: 25/25 identical). Kept unchanged as the parity reference for ksh2vox; against music_db its error
  is large on several axes (`OfficialChartsAgainstMusicDb`, explicit test).

## 7b. Pattern analysis and fitted models

`SDVXEditor.Core.Patterns` — one feature implementation for both models, specified in
`docs/RADAR_MODEL.md` §2 so a Python port can reproduce it:

| Piece | Role |
|---|---|
| `PatternChart.From(chart)` | time view: notes (s), laser sections with points, judged units (`Gameplay/TickRules`), length rule |
| `HandModel` | lane → hand (default BT A/B + FX L + knob L left); the calibrator's `--hands` compares alternatives |
| `PatternAnalysis.Analyze` | 85 named features in a fixed order: volume, chord peaks (1.25 s chords = PEAK), jacks / trills / streams, rhythm grid, hold nesting, laser time / movement / slams / far side, one-hand and hand-trip notes (knob of the other / own hand active, moving), tempo, stops, scroll, camera |
| `AdditiveModel`, `RegressionTree`, `LinearTerm` | `intercept + Σ linear + Σ trees`, clamp; trees as forward-pointing arrays (`x ≤ t` → left) |
| `ModelJson` | indented model files with one tree per line |

`VoxReader` feeds the official charts: it honours `#BEAT RESOLUTION` (44 official files) and reads the
camera coarsely (CAM_RotX/CAM_Radi/Tilt tweens, laser spin ids) — analysis only.

Re-fit everything: `dotnet run --project tools/DifficultyCalibrator -c Release -- "<game dir>"
--core src/SDVXEditor.Core --nautica "<fan chart dir>"` (song-grouped 5-fold CV, per slot / level /
VOX version, temporal hold-out on song ids ≥ 2100, fan charts, hand-model check with `--hands`;
`--dry-run` prints without writing).

## 7. Compose tab internals

| Piece | Role |
|---|---|
| `EditorSession` | song folder, active chart, `CommandStack`, `TimingMap`, `Selection`, audio. Save = kson; a ksh is saved as `<stem>.kson` beside it, never overwritten. |
| `EditorClock` | transport over the song `Track` (tempo = rate, pitch kept); free-running past the audio. |
| `ComposeEditor` | the tools, independent of drawables: pointer down/drag/up in (audio ms, x). Drags are one transaction (rollback + re-execute per step, commit on release). The smoke test drives the same methods. osu!mania chip <-> hold: the hold tool on an existing chip / hold drags its length from its start (`DragMode.Resize`, Core `SetNoteLengthCommand` in place, `OverlapRule.CanResize`; back to the start = chip, no change = no undo step); the chip tool on a hold makes it a chip. |
| `ComposePlayfield` | rendering + mouse → `ComposeEditor`. Everything visible is one `QuadBatch` (a single draw node), so a dense MXM costs ~0.05 ms per frame to build. |
| `PlayfieldLayout` | lane geometry; laser 0..1 = centre of laser lane L → centre of laser lane R, wide = twice that range. |
| `DensityColumn`, `TimelineRuler`, `SummaryMarkers` | minimap, waveform + ruler, bottom markers. |
| `SpectralWaveform` + Core `SpectralAnalysis` / `SpectralPalette` | timeline waveform: BASS decode → LR4 crossovers at 200 Hz / 4 kHz, 1 ms blocks (peak, 10 ms RMS, band energy prefix sums); one `QuadBatch`, refilled only when the view changes. |
| Core `LaserEditing` | pure laser shape edits (new section, move point / merge to slam, insert point), unit-tested. |
| Core `LaserPath*` | osu!lazer-style anchors + curve types per section, evaluation, baking, kson storage — see §13. |

Zoom is one value (`EditorScreen.Zoom`): the timeline and the playfield apply their own px/ms factor to it.

## 8. Timing tab, F6 and Song Setup

Every edit goes through the session's `CommandStack` (Core `Editing/TimingCommands.cs`).

| Piece | Role |
|---|---|
| `TimingCommands` | add / remove / change (identity kept, list re-sorted) of BPM, time-signature (per measure, any denominator), scroll-speed (incl. jump `vf` and curve) and stop points; offset, preview, music volume/file; `SetBaseScrollSpeed` (multiplies the whole graph, osu! slider-velocity semantics); `Resnap`. |
| `TimingDraft` + `TimingCommands.Apply` | a working copy of the timing that remembers each point's original; `Apply` diffs it into one command and remaps objects per `TimingChangeOptions`. |
| `RemapPulsesCommand` | moves every pulse of chosen chart parts (`RetimeTargets`) through a monotonic map; plan computed on first execute, exact undo; laser/graph points collapsing onto one pulse merge into a slam/jump. |
| `ScrollSpeedTools` | Property Transformer: linear/exponential ramp (N steps or per snap tick, smooth or stepped), scale values, shift in time, copy/paste. Pure. |

"When applying timing changes…" in a pulse model:

- **Move notes with the grid** (tab checkbox 1, default on): notes keep measure/beat — nothing to do.
  Off = **keep time**: `KeepTimeMap` puts each object at the pulse of its old ms on the new tempo map,
  snapped to the F6 divisor when "Snap notes" is on. Objects whose time still maps onto their own
  pulse are left alone. Later BPM points keep their pulses, so their time moves; the bar alignment of
  notes after a changed segment can shift — re-snap is the user's call. A note pushed before pulse 0 is clamped to 0.
- **Scale**: moving a BPM point stretches the objects between its neighbouring BPM points (piecewise linear).
- **Adjust bookmarks and preview** (tab checkbox 2): scroll points, stops, camera/effect curves, VOX rows,
  bookmarks and the preview keep their musical position; off = keep their time.

The timing tab steppers: a click / scroll notch = one undo step, a drag = one transaction (rollback +
re-execute). BPM edits the BPM point at the current time; tap tempo is applied with Enter or the
"Use tapped BPM" button after 4 taps. The metronome lamps follow the chart's beat while playing
(downbeat green); clicks are synthesised (`MetronomeSamples`). Bookmarks live in the chart
(`Editor.Bookmarks`, written through on Ctrl+B) so timing edits can move them.

F6 edits a `TimingDraft`; OK = one undo step, Cancel discards. Song Setup edits a
`SongSetupViewModel` copy; OK = one `CompositeCommand`. A jacket/background picked outside the
song folder is copied into it on OK (the copy itself is not undone). The level estimate is shown
with its band; "Use estimate" only fills the field. The window title shows `*` while the chart is dirty.

## 9. Camera and effects tabs

Camera (F2, replaces osu!'s "design") and effects (Shift+F2, replaces "hitsound"). All edits go
through the `CommandStack` (Core `Editing/CameraCommands.cs`, `Editing/EffectCommands.cs`).

| Piece | Role |
|---|---|
| Core `CameraEvaluator` / `CameraState` / `CameraProjection` (`Charts/CameraState.cs`) | camera at a pulse (graphs with curves and jumps, tilt manual/auto incl. laser-driven auto input, spin/half-spin/swing roll) and a pinhole projection of the lane with unnamed-sdvx-clone's geometry (lane 12 widths long, judgement line 0.9 below / 0.9 ahead of the camera, 1.5° base tilt, 60° fov, USC zoom_top / zoom_bottom / zoom_side formulas; image shifted so the line sits at `JudgeScreenY`). Object lengths along the lane are world lengths (`CameraProjection.Along`). Approximations are documented on the classes; the playtest renderer is meant to reuse them. |
| Core `VoxExtrasInfo` (`Charts/VoxExtras.cs`) + `VoxEffects.cs` / `VoxEffectEvaluator.cs` / `VoxScripts.cs` | VOX game effects: `bg_no`, `#POSTEFFECT` rows (stage, instance, tick or ms length), raw `#SPCONTROLER` base rows, game commands (LaneY, BIL_RotZ, Morphing2, BAR/BAROFF, SpecialN), `#BPM OPTION`, note scripts. Only what the ∇ DLL reads is offered. kson `impl["sdvx-editor"].vox`, ksh `bg_no=` / `lane_toggle=` / `//#vox_…` lines; ksh2vox 0.20.0 writes all of it. Full description: **docs/VOX_EFFECTS.md**. |
| `EffectLanes` / `EffectLaneEditor` / `CameraTabContent.Effects.cs` | the camera tab's **effects** view (view 3; the effect lanes replace the camera lanes while it is shown): post lanes per stage with one column per effect, LaneY / BIL_RotZ / Morph2 tween lanes, colours, bar lines, assets, scripts; inspector, presets, game background, BPM option, scripts. Every gesture is one `SetVoxExtras` undo step. |
| Game `Screens/Play/PlayfieldEffects` | preview of the game effects in the playtest and the camera preview: ColorConversion exact per vertex colour (stage 1 playfield, stage 0 background, HUD untouched), the rest approximated (ghost / overlay `QuadBatch`es, offsets); game-only effects are labelled. |
| Core `ExportSupport` | the camera/background/effect (+ scroll speed / stop) rows of the export loss matrix; the UI badges (`ExportBadge`) read it. The VOX column depends on the ksh2vox version: `For(version)` gives `Legacy` (below 0.17.0 or unknown: every VOX cell ✗) or `Mapped` (0.17.0+: camera, tilt, FX effects, laser filters, scroll speed/stops and the VOX extras, `~` where the converter clamps or drops cases); `VoxNote` is the tooltip saying what is lost. Tests prove the ksh claims with the real writer/reader. |
| `CameraLaneEditor` | lane tools in (ms, lane, x fraction). Graph lanes and manual tilt are anchor paths (§13a): click add + drag (a placement chain auto-types like a laser), drag anchor, Shift jump, Ctrl+click insert, Alt-drag ease of a straight pair, right click = `ContextMenuRequested` (anchor / segment type, delete). Auto tilt modes and patterns keep point tools (right click deletes; patterns snap to a slam — kson ignores one without a matching slam, such patterns are framed red). Selection = lane + anchor pulse (anchors are replaced on every edit). Drags are one transaction. |
| `CameraLanes` | 7 lanes (5 graphs, tilt, patterns) on the compose time axis (same judgement line, px/ms, zoom); one `QuadBatch`. Anchors in type colours where the lane is hovered / selected / being placed, control polygon of curved runs, next-segment preview while placing; the anchor menu is `LaserContextMenuItem.CreateMenu` in the screen's `PopupLayer`. |
| `CameraPreview` | 2.5D perspective lane with notes/lasers, driven by `CameraEvaluator` + `CameraProjection`. |
| `CameraTabContent` | read-only compose playfield (`ComposePlayfield.ReadOnly`) · lanes · preview + inspector / background & post effects / export matrix. |
| `EffectsTabContent` | FX note list near the cursor (shared selection), effect definitions with kson parameters, assign to holds, parameter changes, laser filter switches / gain / slam volume / peaking delay, custom chip sounds (import via `KeySoundFiles.Import` into `EditorSession.EnsureWritableFolder()`, assign per chip or to all chips without one, preview). |
| `HitsoundPlayer` | plays an FX chip's key sound instead of the tick (song-folder file or a synthesised stand-in for the built-ins); `PlayKeySound` is also the preview. With "play effects" on, slams play at the chart's slam volume (`ApplySlamVolume`, §16). |

## 10. Playtest (F5)

osu!'s test mode for SDVX: plays the **in-memory** chart (nothing is saved; a toast warns when the chart is
dirty and the HUD shows "UNSAVED CHANGES") from a 2 s (real time) lead-in before the editor position, at the
editor's playback rate and with the universal audio offset. Esc shows the result summary, a second Esc /
Enter returns to the editor at the time the test stopped. The song `Track` is borrowed from the editor's
`EditorClock` and handed back on exit.

| Piece | Role |
|---|---|
| Core `Gameplay/TickRules` | hold/laser tick placement and slams (rules + sources on the class: RemyWiki, unnamed-sdvx-clone `Scoring.cpp`). 1/16 note below 256 BPM, 1/8 from 256, 1/4 from 512 (BPM at the object start); holds: no tick on the start, ticks while < end − interval, one mid tick if none fit; lasers: split at slams, `floor(len/interval)` ticks from each part start, none on a slam, every slam one unit. |
| Core `Gameplay/PlayableChart` | chips, hold ticks, laser ticks, slams per lane with song ms; units before the start are `Skipped` (drawn, never counted). |
| Core `Gameplay/GameplayJudge` | the rules engine, device-free: `Press/Release/MoveLaser(…, songMs)` + `Update(songMs)`; chips CRITICAL/NEAR/ERROR with early/late, hold ticks need the button down, laser cursor snaps onto a new section and locks while within the leniency and the input turns the laser's way (USC-style), slams need a turn in their direction (−½ … +1 slam window), autoplay. `SimulateAutoplay` for tests. |
| Core `Gameplay/ScoreProcessor` | score to 10,000,000 (2/1/0 points per unit), EX score (chips 5/4/2, ticks and slams 2), chain, effective-rate gauge (USC `GaugeNormal`; cannot fail), early/late, grade, clear mark. |
| Core `Gameplay/JudgementWindows` | real-time windows (default ±46 / ±92 / ±138 ms, slam 84 ms; S-CRITICAL 23 ms is an assumption), `ForRate(rate)` = song-time windows (w × rate, so the real-time windows stay the same at any rate), `Parse("46,92,138,84")`. |
| Core `Gameplay/PlayBindings` | the bindings line in editor.ini (`bt-a=D, …, laser-r-right=P, start=Enter, back=Escape`; `|` for alternatives, `JoyN` for controller buttons). |
| `Screens/Play/PlaytestClock` | song clock that may start below 0 (lead-in), only moves forward, follows the track (tempo = rate, track = song time + offset). |
| `Screens/Play/PlaytestPlayfield` | perspective track in one `QuadBatch`: every vertex through `CameraProjection.Project` with `CameraEvaluator.Evaluate(pulse)` per frame (same camera as the camera tab preview), distance = `TimingMap.VisualPosition` × hi-speed (scroll speed, stops). Lane = 8 beats / hi-speed; hi-speed = mod speed (BPM × HS, editor.ini, F3/F4 or −/+) / `HiSpeedBasis` (`std_bpm` / ksh `to=`, else the most common tempo by duration up to the last note, `TimingMap.ModeBpm`, as USC M-mod). Only the visible pulse range is visited (binary search per lane). |
| `Screens/Play/PlaytestScreen` / `PlaytestHud` / `PlaytestResults` | devices → judge (keys, joystick buttons, joystick axes with wrap-around, mouse X/Y in mouse mode with the cursor hidden+confined = framework relative mode), sounds (on-hit `HitsoundPlayer.PlaySlot` / `PlayKeySound`; autoplay and assist tick use the clock-driven player, which starts samples ahead of time), HUD, results. Tab autoplay, Enter pause, ` retry. |

Settings (editor.ini): `PlaytestBindings`, `PlaytestModSpeed` (600), `PlaytestAutoplay`, `PlaytestKnobSensitivity`
(1.0 = half a knob turn / 300 mouse px per laser width; negative inverts), `PlaytestKnobAxes` ("0,1"),
`PlaytestMouseKnobs`, `PlaytestKeyLaserSpeed` (4 widths/s), `PlaytestWindows` ("46,92,138,84"),
`PlaytestHitsounds`, `PlaytestAssistTick`. All of them are edited in the options overlay (§12).

## 11. Collab: bookmarks, reference difficulty, .svz, laser reach

| Piece | Role |
|---|---|
| Core `Bookmark` / `BookmarkList` (`Charts/Chart.cs`) | bookmark = pulse + label + colour (`#rrggbb` or null = blue). A `List<PulseValue<Bookmark>>`, so timing commands remap it like other pulse values. kson `impl["sdvx-editor"].bookmarks` `[y, label]` / `[y, label, "#rrggbb"]` (0.1.0 readers still get the label); ksh `//#bookmark|#rrggbb|label` comment lines at the bookmark's chart line, read back by `KshReader`. KSM treats `//` lines as comments; ksh2vox only matches chart lines, `t=` and `beat=` at a line start, so it skips them — verified: inserting 790 such lines into the four Jumpy Star .ksh leaves `convert_chart` output identical. VOX cannot carry bookmarks. |
| Core `SetBookmarkCommand` | rename / recolour / delete / add, addressed by pulse (Ctrl+B still writes outside the undo stack; undo stays right). |
| `BookmarkTools` + `BookmarkListDialog` | Edit → Bookmarks…: list (time, beat, label, colour), rename (Enter), 8 colour swatches, delete, add at cursor, jump (double click). Markers: bottom summary (bookmark colour, label on hover) and top timeline (coloured line + flag, label on hover). |
| `EditorScreen.Collab.cs` | File → Open Difficulty for Reference: a view-only `EditorSession.CreateReference` + a `ComposeEditor` with the Select tool and `LockNotes` (select only, no move/delete) + a greyed `ComposePlayfield` in `ComposeTabContent.ReferenceContainer`, right of the main one (same clock, zoom, scroll). Selections are exclusive (selecting in one clears the other); Ctrl+C copies the reference selection when the main one is empty (`EditorChartActions.ReferenceSelection`), Ctrl+V pastes into the main chart at the cursor. Esc / Close Reference closes it. Main chart left, reference right, each with its wide-laser overhang (see the next row). |
| Core `SvzPackage` | `.svz` export/manifest, see `docs/SVZ_FORMAT.md`; import is `SongImporter` (kind `Svz`, same zip safety). `SongImporter.PlannedFolderName` / `WouldReplaceExisting` let song select ask Replace / Keep both (`ImportConflictDialog`) before importing. `--import <file>` imports on start. |
| `PlayfieldLayout.LASER_REACH` / `WIDE_REACH` / `OVERHANG` | Normal lasers: the laser tool acts one BT lane (`LASER_REACH`, = the side margin `SIDE`) beyond the lanes and clamps to 0/1 there. Wide lasers use the true 2x scale everywhere, as in game: 0..0.25 / 0.75..1 lie past the lanes, up to `WIDE_REACH` (160 design px = two BT lanes) beyond each edge; drawing, placing, dragging and hit-testing all use that one mapping. Each playfield's masking container is `FIELD_WIDTH` = playfield + `OVERHANG` (100 px) on both sides, so wide lasers are not clipped and every tool receives hover and clicks there (`ComposePlayfield.ReceivePositionalInputAt`; it was the laser tool only, which left wide anchors out there unreachable in select mode). Shading while the laser tool is active: darker = normal reach, lighter = wide reach. Layout: without a reference the main playfield is centred; with one, density (100) \| main \| reference = 1632 of 1920 design px, main in the left half, reference in the right half, clear of the toolbox and toggle bar (the smoke test checks the rectangles). |

Smoke test (`--smoke-test --open <song>`, `EditorGame.SmokePlaytest.cs`): on a copy of the song, autoplay runs at the
densest notes, the laser-heaviest part, a whole-lane stress run (hi-speed 0.016) and a part with camera edits added
for the test; each must score > 0 with no ERROR; then the result summary and the return to the editor at the stop
time. The stress run's HUD reads `HI-SPEED 0.016 FORCED` — its screenshots are deliberately compressed. `SDVXEDITOR_SMOKE_PLAYTEST_AT=<ms>[,…]`
adds runs that autoplay to those song times, pause, screenshot (`65-playtest-at-<ms>`) and log the scroll geometry. `SDVXEDITOR_SMOKE_ONLY=playtest` runs only this part (any comma list of `songselect,walk,options,ui-input,bottombar,collab,editing,camera,effects,playtest,menus,sources,tutorial,features`; unset = all). Part `effects` (`EditorGame.SmokeEffects.cs`): the game effects through real input on the effect lanes, a preset, the inspector, a note script, undo/redo, the camera preview and a playtest showing the negative and the lane slide, save + reload and a ksh round trip.

Smoke runner (`EditorGame.cs`): steps run one after another (not on a fixed timetable); a step that starts something
asynchronous (an export, an import, a screen push, an audio seek, a playtest) calls `waitUntil(condition, what, timeout)`
and the next step waits for it instead of relying on padding steps. A failure is a `SmokeTestException` naming the
part and step; `Program` prints `SMOKE PASSED` / `SMOKE FAILED` as the last line and exits with 0 / 1, with a 20 s
watchdog on disposal. Every run starts from default editor settings: the smoke storage's editor.ini is deleted before
the config manager reads it (`resetSmokeSettings`; a killed run once left `EditorBindings = tab-timing=F8`, a muted
master volume and BT-A on A behind). The smoke root input manager (`SmokeUserInputManager`) hovers without the OS
cursor and claims `CursorInWindow` for an injected wheel turn or button press: `UserInputManager` drops both while the
OS says the cursor is outside the window, which made the F6 BPM wheel check fail ("bpm 100 -> 100") whenever the user's
own cursor was elsewhere — reproduced by setting the flag to false before the wheel. Screens must not seek a track from their async `load()`: `TrackBass.Seek` waits for the audio
thread, and with the game stopping meanwhile that wait held the load lock and disposal deadlocked (EditorScreen and
PlaytestScreen now hand the track over in `LoadComplete`).

## 11a. Song sources, Support the Artist, Thanks

| Piece | Role |
|---|---|
| Core `EditorInfo.SongSources` (`Charts/Chart.cs`) | song-level list of URL strings, stored verbatim, replaced as a whole (a `SetValueCommand` undoes it). kson `impl["sdvx-editor"].song_sources` = `["https://…", …]` (written only when non-empty; an array with a non-string goes to `Extra` and round-trips raw). ksh: `//#source|<url>` comment lines (`SongSourceLine`), written at pulse 0 after the first bar line — ksh2vox's header loop would take a `//` line with `=` (a YouTube `?v=`) as a header key, its body loop skips `//` lines; read back in header and body. A URL with a control character cannot be one ksh line and is dropped (KshWriter.KnownLosses). Verified: 72 such lines in the 24 library .ksh leave ksh2vox 0.19.0 and the submodule's 0.17.0 output (VOX + radar input) identical. |
| Core `SongFolder.SongSources` / `LibrarySong.SongSources` | the song's list = the first chart (file order) with a non-empty list; the library cache stores it (cache version 3). |
| Core `SongSourceSync.WriteToSiblings` | called by `EditorSession.SaveAs` when the saved list differs from the one loaded/saved last: every other `.kson` of the song is read fresh, gets the list and is written through a temp file (unchanged files are not touched; a `.ksh` is never rewritten). `EditorSession.setChart` gives a difficulty without its own list the song's, so Song Setup and the buttons always show the song-level value. |
| Core `Library/MusicLinks` | the allow-list (`Services`: name + hosts, subdomains match, narrow hosts first so `music.youtube.com` is YouTube Music), `Validate` (https only — bare and `http://` links are upgraded, other schemes refused —, no user info / port / IP literal, ≤ 2048 chars, no whitespace; host lower-cased via `IdnHost`, tracking parameters `si`, `utm_*`, `fbclid`, `gclid`, `igshid`, `feature`, `pp` … removed), `Openable` (valid, normalised, deduplicated — the only thing a button ever opens), `DisplayHost`, `ShortForm`. `SongSourceTests`. |
| Song Setup → Sources (`SongSetupDialog`, `SongSetupViewModel.SongSources` / `AddSource`) | link field + Add (Enter commits too), the refusal under it in red, one row per source (service, short link, ✕), a Support the Artist button to try the list. OK adds "Change song sources" to the Song Setup `CompositeCommand`. |
| Game `Overlays/ExternalLinks` (cached by `EditorGame`) | `Confirm(url)`: `ChoiceDialog` "Open external link" naming the domain — Open in browser (`GameHost.OpenUrlExternally`) / Copy link / Cancel; refuses anything but absolute https. `ShowSupport(sources)`: one link → its confirmation, several → "Support the Artist" list (service — short link) → confirmation. `RecordOnly` in smoke runs: `Opened` records instead of opening. |
| Game `SupportArtistButton` | heart + "Support the Artist"; hidden and not clickable while no source is openable. Dark pill under song select's details (`SongSelectScreen.SupportButton`, from `ISongListItem.SongSources`), light link at the right end of the editor's menu bar (`EditorScreen.SupportButton`, follows `Session.ChartChanged`), and on the Sources page. |
| Thanks: Core `Credits/CreditsFile` + `CreditsDialog` | `credits.json` (embedded, `SDVXEditor.Resources.Credits.credits.json`): `{ title, intro, sections: [ { title, entries: [ { name, role, link } ] } ] }`; forgiving parser (entries without a name and sections without entries skipped, non-https links dropped). Opened by the top-level menu item **Thanks** (no submenu; the menu sweep clicks it) and song select's footer button; links go through `ExternalLinks.Confirm`. |

Smoke part `sources` (`EditorGame.SmokeSongSources.cs`, needs `--open`): on a copy with a second .kson difficulty,
all through the root input manager — Song Setup tab click, Sources tab, example.com refused with the message, a
SoundCloud link with tracking parameters and a bare youtu.be added (normalised), a third added and removed with its
✕, OK; Ctrl+Z / Ctrl+Y; Ctrl+S writes the kson and the other difficulty; Esc, the other difficulty reopened shows
the list (chart, button, Song Setup); menu-bar Support the Artist → list → SoundCloud → confirmation → Open
(recorded, no browser); menu bar → Thanks, a link → Cancel (nothing opened), Close; the copy imported into the
library, song select shows Support the Artist for it (list, Cancel); footer → Thanks, Esc.

## 12. Options overlay and key bindings

| Piece | Role |
|---|---|
| Core `Input/KeyComboText` | settings text of one combination: modifiers canonical (`Ctrl+Shift+Alt+Super`), then the key name (framework `InputKey` / osuTK `Key` / `JoyN`); `none` = deliberately unbound. Core never resolves names, it only canonicalises and compares case-insensitively. |
| Core `Input/BindingTable<TAction>` | one binding family: the default table + per-action overrides (an override may be empty). `Rebind(action, slot, combo, Keep/Swap/ClearOther)`, `FindConflicts`, `AllConflicts`, `Reset`, `ResetAll`; settings text = only the overrides, `action-id=Combo\|Combo` with kebab-case ids. `Changed` after every change. Tested in `KeyBindingTests`. |
| Core `Gameplay/PlayBindings` | the playtest family on top of a `BindingTable<PlayAction>`; its settings line stays complete (`bt-a=D, …`), `back=none` unbinds, an empty value still keeps the default. `Reserved`: keys the playtest screen handles before any binding (Tab, F3/F4, -/+, `). |
| `Screens/Edit/Input/EditorKeyBindings` | `Defaults` (the stable-style table), `Table` (overrides from editor.ini `EditorBindings`, applied by `Apply`), `All` (in effect), `Changed`. `EditorKeyBindingContainer` reloads its mappings on `Changed`, the menu bar rebuilds its shortcut column, Help → Keyboard Shortcuts lists every action with its current keys ("(unbound)"). `IsFixed`: Alt+wheel up/down (routed by what the cursor is over) cannot be rebound. `ShowOptions` = Ctrl+Shift+O (Ctrl+O stays "open difficulty", as in stable). |
| `EditorGame.Options.cs` | editor.ini `EditorBindings` ↔ `EditorKeyBindings.Table` both ways, `ShowFps` → `FrameStatistics`, `SongsFolder` / `LauncherConfigPath` feed the library when the command line leaves them out, `GlobalHotkeys` (first child of the screen layer = last to see a key: Ctrl+O opens the options only when no screen used the key). In a smoke run a `ManualInputManager` wraps the screen layer (`SmokeInput`: passes the window input through until a part switches it to injected input, as the options part does) and the root input manager is a `UserInputManager` that always handles hover (the framework one computes hover only while the real OS cursor is inside the window, so injected clicks of an unattended run landed nowhere), and every setting is snapshotted at start and put back before editor.ini / framework.ini are written (on top of the separate `SDVXEditor-smoke` storage). |
| `Overlays/Options/OptionsOverlay` (+ `.Sections.cs`) | the osu!stable panel: slides in from the left, section sidebar, search over every row (label, keywords, section and subsection titles), takes all keys while open. Values are bound straight to the config bindables (held in `keepAlive`: bindings are weak both ways, a collected copy would silently detach a control). Export shows the session's `VoxConverterDetector` (Game `Configuration/`: one `VoxExporter.GetInfoAsync` per session, started by the first export badge or the first options opening, again on a converter/Python path change or "Detect converter") and flags ksh2vox below 0.16.0. The export badges and the camera tab's export table follow the same detection: until it answers, or when it fails, they show the conservative values with "vox?"; the table header names the version ("VOX via ksh2vox 0.17.0"). Tooltips: a framework `TooltipContainer` wraps the screen layer. `LibraryManager.ExportRoot` honours `ExportFolder` (ignored in smoke runs). |
| `Overlays/Options/KeybindRows` | `KeybindRow<TAction>` (label, two `KeyBindButton`s, ↺), `KeybindScope` (editor: combos with modifiers, middle/side mouse buttons, wheel; playtest: single keys, `JoyN`, reserved keys refused). Capture: a click on a slot makes the overlay route the next key / mouse button / wheel notch / controller button to the row; Esc cancels, Delete/Backspace (no modifier) clears — so plain Delete/Backspace cannot be captured (reset the row instead). A combination used by another action of the same family opens Swap / Clear other / Cancel instead of applying. |
| Core `Input/KeyboardLayout` + Game `Input/KeyboardLayoutService` | **Keyboard layout.** osu-framework 2026.807.0 key events carry only the physical key: `KeyDownEvent.Key` is `SDL3Extensions.ToKey(SDL_KeyboardEvent)`, a `switch` on `scancode`; there is no keycode or character field (`TextInput` is a separate text stream). The framework does know the layout for *labels*: `SDL3ReadableKeyCombinationProvider.GetReadableKey` asks `SDL_GetKeyFromScancode`, and `IWindow.KeymapChanged` / `ReadableKeyCombinationProvider.KeymapChanged` fire on `SDL_EVENT_KEYMAP_CHANGED`. The editor uses the same call: binding texts name letters A–Z **logically** (the key labelled Z), everything else physically. `EditorKeyBindingContainer.DefaultKeyBindings` maps each combination through `KeyboardLayoutService.ToPhysical` (QWERTZ: Ctrl+Z → physical Ctrl+Y), the options capture stores `ToLogical` of the pressed key, `Describe` labels punctuation with the layout's character (QWERTZ Slash → "-"), `DescribePhysical` labels playtest keys (positions, like osu! gameplay). `Refresh` runs at start (after SDL video init — before it SDL answers with the US map), on `KeymapChanged` and on window focus; a change raises `EditorKeyBindings.Changed` (containers reload, menus relabel). Smoke runs `Simulate(Qwerty)`. Measured (sdl probe + game log, Windows layout 00000407): Y→`z`, Z→`y`, Semicolon→`ö`, Slash→`-`. No Win32 fallback was needed; `IKeyboardLayoutSource` exists for tests. Limits: a letter no position types (Cyrillic layouts) stays on its US position; a letter-named position that types no letter while its letter sits elsewhere (AZERTY physical M) cannot be captured. Framework text boxes keep the framework's own (physical) Ctrl+Z/Ctrl+A. |
| Game `Input/WheelInput` | **Shift+wheel.** osu-framework 2026.807.0 turns a notched vertical wheel turn into a horizontal one while Shift is held (`MouseScrollRelativeInput.Apply`: `if (!IsPrecise && Delta.X == 0 && ShiftPressed) Delta = (Delta.Y, 0)`, read from the IL), so Shift+wheel up arrives as `ScrollDelta (1, 0)` and as `MouseWheelLeft` for key bindings. `Delta` / `Direction` count the X axis as the wheel while Shift is held (positive = up = left, the framework's own convention); every wheel handler uses them (NumericUpDown ×10 with Shift, OsuStepper ×10, SnapSlider, options sliders and wheel capture, song wheel). `WithShiftWheelAlias` gives each Shift+wheel binding a twin on the horizontal key (`EditorKeyBindingContainer.DefaultKeyBindings`), so Shift+wheel seeks four ticks again. Measured with a scroll-handler log in a smoke run: injected `(0, 1)` + Shift reached `NumericUpDown` / `OsuStepper` as `(1; 0)` and changed nothing before the fix. |
| `EditorGame.Window.cs` | Window mode: the framework's three (`Windowed`, `Borderless` = `SDL3WindowsWindow.SetBorderless`: `SDL_SetWindowBordered(false)` + display bounds, +1 px width under GL/Vulkan against driver-forced exclusive mode; `Fullscreen` exclusive). The framework binds Alt+Enter and F11 to `CycleMode` (W → B → F) in the prioritised `FrameworkActionContainer`, whose handler is not virtual, so `EditorUserInputManager` (the game's root `UserInputManager`, `CreateUserInputManager`) swallows those two key presses and calls `ToggleWindowMode` (W ⇄ B; F → W). First run defaults to Borderless (`Program.FirstRun`). |

Smoke part `menus` (`EditorGame.SmokeMenus.cs`, needs `--open`; works on its own copy of the song, dropping charts from
the copy until two slots are free): `EditorScreen.MenuLeaves()` lists every leaf as "File > Export Song > kson…"; a
scheduler-driven sweep clicks each through `SmokeInput` (top-level item, submenus — only items of menus whose
`State` is Open, a closing submenu still fades — then the leaf), waits for a changed fingerprint (toast count, menu
actions run, dialogs, tab, tool, rate, dim, window mode, chart version, selection, bookmarks, reference, options,
screen) plus a known effect where one exists, and cleans up (dialogs closed, playtest left, toggles and window
mode put back). Dialog buttons are clicked through the root input manager (the `DialogOverlay` sits outside
`SmokeInput`). Then: new difficulty copy + empty (Song Setup open, chart file in the copy with the expected object
count), Ctrl+O chooser, Revert confirmation, QWERTZ simulation (Ctrl + physical Y undoes, Ctrl + physical Z redoes,
labels and capture), Alt+Enter / F11 injected into the root input manager, Exit prompt (Cancel / Discard).
Esc like osu! (real Esc presses through the root input manager, `SmokeInput` passing through): an open menu, an open
drop-down list, a dialog, the reference view and the options each close with one Esc and the editor stays; with
nothing open and unsaved changes Esc asks Save / Discard / Cancel (Esc on that prompt = Cancel); on a saved chart
Esc leaves at once. Who takes the key is the topmost thing: `Menu`, `PopupMenuList`, the dialog, the options
search box; only when none of them does, the editor binding `Exit` runs (`EditorScreen.handleAction`: laser draft →
reference → `ExitWithPrompt`).

Smoke part `options` (`EditorGame.SmokeOptions.cs`, no `--open` needed), all through input injected into `SmokeInput` (mouse
moves/clicks, key presses; injected input is processed on the next frame, so each step checks the previous step's
effect): Ctrl+O on song select, sidebar to Input and Export (waits for the converter detection), search, rebind the
Timing tab F3 → F8 by capture, F1 conflict → Cancel, Ctrl+Shift+K captured and cleared with Backspace, Tab refused
and A bound for BT-A; then in a new editor F8 opens Timing, F3 no longer does, F2 still opens Camera, Help shows F8;
in the playtest A holds BT-A and D does not; Ctrl+Shift+O reopens the options, the rows' ↺ restore both, F3 works
again; the two binding settings are put back.

## 13. Laser paths (osu!lazer slider editing)

A laser section is edited as an osu!lazer slider path; the chart model keeps working on points.

| Piece | Role |
|---|---|
| Core `LaserAnchor` / `LaserPath` (`Charts/LaserPath.cs`) | anchors (`Ry`, `V`, `Vf` = slam, `Type` straight / perfect curve / Bezier or null = inherit, `Auto`, kson `Ease`). A segment starts at the first anchor, at every typed anchor and at every slam (a hard break: the segment before ends at `V`, the next starts at `Vf`). `Effective`: 2 anchors are always straight, a perfect curve needs exactly 3 anchors (otherwise Bezier, osu! rule). `ApplyAutoTypes`: auto segment starts get 2 → straight, 3 → perfect curve, 4+ → Bezier; an auto start (not first, not a slam) with 3+ anchors after a curved segment becomes Bezier ("two curves in a row meet at a Bezier anchor"). |
| Core `LaserPathBaker` | evaluation and baking. Curve plane: one laser width = `TIME_SCALE` = 480 pulses (half a 4/4 measure), so a perfect curve is a true circle there; Bezier = de Casteljau over all anchors of the segment. |
| Core `LaserPaths` | link to the section: `ToSection` (normalise, rebase, bake, attach the path unless trivial = all straight), `ValidPath` (trusted only while a fingerprint of the points equals the bake's), `AnchorsOf` (valid path, else one straight anchor per point with its kson curve), `PointsFor(section, options)` (format bakes), `TryAttach` (file read). |
| Core `LaserPathEditing` / `LaserPathCommands` | pure edits (cycle / set type, add anchor while placing incl. slam on the last pulse (`IsSlamClick`), insert, move strictly between neighbours — either slam end, `slamEnd` = the value is `Vf`, both move the pulse —, delete, `DeleteSlamEnd`) and their undoable commands — each a `ReplaceLaserSectionsCommand` from the old section object to a freshly baked one, so undo restores the exact object. |
| Core `LaserPlacement` (`Editing/LaserPlacement.cs`) | the draft of the laser tool's click placement (lane, width, start pulse, anchors); `Preview` / `Result`: a lone slam anchor (two clicks on one beat) becomes a **lone-slam section** — one point `V → Vf`, no tail, like a slam dragged on one beat (`LaserEditing.NewSection`). `LaserPath.IsSectionShape`: two or more anchors, or one slam anchor; a single plain anchor is no laser (deleting one end of a lone slam removes the section). Lives in Core so the placement rules are unit-tested. |
| Core `LaserSlamEndRef` / `Selection.SlamEnds` | one end of a slam point (`End` false = start `V`, true = landing `Vf`), selected on its own; see "Slam ends" below. |
| `ComposeEditor` | laser tool: drag = straight section (as before), click = `LaserPlacement` (click adds, click on an anchor cycles its type unless the click makes a slam of the last anchor, right click places a last anchor at the cursor and finishes, Esc cancels, leaving the tool finishes); curved paths are hit and dragged on their anchors, slam ends of any section too. Select tool: `HoveredLaser` / `HoveredAnchor` (with the end of a slam), `ActiveLaser` (the clicked section while it, or one of its slam ends, is selected): anchors drag, a slam end is selected and dragged on its own, Ctrl+click inserts (on a slam end: toggles it), right click raises `ContextMenuRequested` with `LaserMenuItem`s. |
| `ComposePlayfield` | outline + anchors for the hovered (`ComposeEditor.AnchorPreview`: select and laser tool, editor.ini `ShowLaserAnchorsOnHover`, off = outline only) / active laser, anchors for curved paths with the laser tool, the placement preview (draft + pointer as next anchor); the menu is a `PopupMenuList` of `LaserContextMenuItem`s opened in the screen's `PopupLayer` under a zero-size anchor at the click. |
| `ComposeToolbox` | laser tile badge "L" / "R" in the laser colour, `LaserSideToggle` (L \| R) under it. |

**Drags and controls (0.1.0).** A press on any anchor / point drags it at once (`ComposeEditor.selectDown`; a laser
inside a bigger selection moves the selection instead, `movesSelection`). Drags of existing points / anchors keep the
grab offset and are free (`dragTarget`: time on `FREE_DRAG_PULSES` = 5, value raw) unless the drag-snap key
(`EditorToolState.DragSnapModifier`, Shift) is held; a point inserted by the press keeps snapping (`dragSnapped`).
Bezier control anchors (`LaserPath.IsControl`) move with `MoveAnchor(..., controlLimits: PlayfieldLayout.ControlLimits)`;
`Normalize` leaves them unsorted / unclamped while the structural anchors are ordered and they stay strictly inside
their segment, otherwise it falls back to the old sort + clamp (unsorted input is tried on a copy first, so the first
anchor of an unsorted list is never typed before the sort). Right click while placing = `placeLastAt` + finish.
Width toggles: `LaserEditing.WithWidth(keepPosition)`; W / tile / menu → `EditorScreen.ToggleWideLaser` →
`EditorChartActions.SetSelectionWidth` (keep position), Ctrl+W keep values, laser tool Ctrl+Shift(+right) click.

**Classic lasers.** `EditorAction.ClassicLaserHold` (default F) is a held key, read by the editor screen's per-frame
poll (`pollModifierKeys`: every key of a binding down, extra modifiers allowed) into `EditorToolState.ClassicLaserHeld`
through a second `HoldModifierLatch` (another key / action uses it up). `ComposeEditor.ClassicLasers` switches the value
grid (`ClassicLaser.Snap`: 1/10, wide 1/20) for every laser click and drag and, while placing, the click rules: Core
`ClassicLaser.IsFinishClick` (last point again = finish) and `ClassicLaser.Add` (slam, else a plain straight point; the
point before it becomes an explicit straight start, the new last point stays an auto start so anchor clicks continue
typed). Source of the rules: kson-rs `editor/src/tools/laser.rs` (see CHANGELOG).

**Slam ends.** The model keeps a slam as one anchor / point with `V` and `Vf` (kson `[v, vf]`, ksh two lines 1/32
apart, VOX two rows on one position — nothing changed in the formats). The editor shows and edits its two ends as two
points: hit tests already returned the end (`AnchorHit.Vf`), now hover frames, selection (`Selection.SlamEnds`,
`LaserSlamEndRef` on the baked slam point, which every slam anchor has), drags and deletes follow it. A press on an
end selects just that end (Ctrl toggles; an already selected pair drags together and stays selected); the drag is
`LaserPathEditing.MoveAnchor` through the anchors for plain sections too (one anchor per point), so either end moves
the slam in time, strictly between its neighbours. `SelectionOps.Delete` edits sections with selected ends through
their anchors (`DeleteSlamEnd`, both ends = `DeleteAnchor`, fewer than two anchors = the section goes), so curved
paths keep their types; move / copy / mirror take the end's whole section (`Selection.Sections`), timestamps write
the slam once (`Selection.TouchedLaserPoints`). Box selection selects a slam whole when both ends lie in the box,
otherwise the end that does. Before 0.1.0's fix the first two placement clicks on one beat made a one-anchor slam
draft that previewed as a single point and was dropped on right click (a section needed two anchors), and every
hover / selection highlight covered both ends at once.

**Monotone rule.** Each segment is sampled densely (Bezier 32 per degree, arcs up to 512 by sweep). A sample is kept
only if it lies strictly after the last kept one and strictly before the segment's end anchor; the end anchor is
always kept; values are clamped to 0..1. A curve that turns back in time is therefore cut: the kept samples are
joined straight from the last forward sample to the next one ahead, so the laser stays continuous and never needs
a hidden slam. Anchors themselves never share a pulse (slams are one anchor with `Vf`).

**Baking.** Straight segments emit one point per anchor (exact; a kept ease curve stays). With kson curves allowed a
3-anchor Bezier emits one point with kson curve `a = (t1−t0)/(t2−t0)`, `b = (v1−v0)/(v2−v0)` — the kson curve *is*
that quadratic Bezier, so it is exact (needs `v0 ≠ v2`). Everything else: the start anchor plus grid samples
chosen Douglas–Peucker style until the polyline is within `LaserBakeOptions.TOLERANCE` = 0.004 (a fifth of a ksh
value step) of the path. Options: `Kson` (chart, kson, VOX) = kson curves, 5-pulse grid (one VOX tick, so ksh2vox
gives every sample its own tick); `Ksh` = no curves (ksh2vox treats a ksh curve as already baked by K-Shoot),
40-pulse grid, no sample within 1/32 of an anchor and within 2/32 after a slam (two ksh points within 1/32 read back
as a slam). The budget is grid-limited where the path itself moves faster than the grid (the steep start of a short
arc cut by the monotone rule); `MaxError` measures it, the smoke test reports it.

**Formats.** kson: points = bake; `impl["sdvx-editor"].laser_paths` = per lane `[[y, [[ry, v | [v, vf], type | null,
auto?, [a, b]?], …]], …]`; on read a path is attached only if it bakes to the section's points (1e-6), otherwise it
is dropped and counted in `Chart.StaleLaserPaths`. ksh: the ksh bake. VOX: the kson staging carries the kson curves,
ksh2vox 0.17.0 bakes them every 3 ticks (`_kson_point_curve`, `CURVE_STEP`) and passes sampled points through — no
double baking. Older converters ignore kson curves (straight segments).

**What makes a path stale** (falls back to straight anchors on the baked points, shape kept): the plain point tools
on a section (only for sections without a curved path), timing remaps that move its points, toggle slam, reverse.
Mirror re-bakes the mirrored anchors and keeps the types; move/copy/paste/wide-toggle keep the path (relative pulses).


## 13a. Camera anchor paths

The camera curves reuse the laser path model (§13) instead of a copy of it.

| Piece | Role |
|---|---|
| Core `PathRange` (`Charts/LaserPath.cs`) | the value axis of a `LaserPath`: lasers 0..1 clamped (`PathRange.Laser`, bit-identical to before), a camera lane its view range unclamped. The curve plane maps the range to one unit (and `TIME_SCALE` = 480 pulses to one unit), so a perfect curve is a circle as drawn in the lane. `LaserPath.Range` (not serialised), used by `Normalize`, `LaserPathBaker` and `LaserPathEditing.MoveAnchor`. |
| Core `CameraPaths` / `CamRun` / `CameraAnchorPath` (`Charts/CameraPaths.cs`) | a **run** is the camera counterpart of a laser section: a graph lane is one run (all points), manual tilt one run per stretch of manual values that interpolate into each other (a head point switching auto → manual and a tail point switching manual → auto are part of the run; their modes are kept on rebuild). `Runs`, `RunAt`, `TargetFor` (which run a new point joins: the one it interpolates from or into, else a new one), `Replace` (normalise, rebase, bake with `KsonOptions`, store unless trivial → `SetCameraLaneCommand`), `TryAttach`, `GraphFor` / `TiltFor` (format bakes), `ValidPaths`. Stored paths live in `CameraInfo.Paths` and are trusted only while a fingerprint of the run's points equals their bake, exactly like `LaserPaths.ValidPath`. |
| Core `SetCameraLaneCommand` | replaces one lane's point list (graph or tilt) and its stored paths; undo puts the exact old point objects and paths back. |
| Core `CameraPathEditing` / `CameraChain` / `CameraPathCommands` (`Editing/CameraPathEditing.cs`) | `Add` (fresh click = auto-typed segment start; a click right after the chain's tail turns the tail into an inherit control point unless it is the chain start, a jump or explicitly typed — so the chain is one auto segment, 2 straight / 3 perfect / 4+ Bezier, and a chain in the middle of a graph ends at its last click), `SetJump`, `SetEase` (only a plain straight pair), `Delete` (down to an empty run). Type cycling, explicit types, Ctrl+click insert and moves are `LaserPathEditing`'s. `CameraChain` is the placement in progress: the run's anchors as the last click left them, auto flags included (an all-straight path is not stored, so the chart alone would lose them), valid while the run still bakes to it. |

**Bake bounds** (`CameraPaths.Tolerance`, maximum value error against the anchors' curve): zoom_top / zoom_bottom /
zoom_side 0.5 units (range ±300; zoom_top 100 ≈ 12.7° pitch → ≤ 0.07°), center_split 0.5 (±200; 100 = one BT lane
of gap), rotation_deg 0.1°, manual tilt 0.005 (±1 = a laser at the edge, 10° per unit → ≤ 0.05° roll).
`KsonOptions`: a 3-anchor Bezier = one exact kson curve `[a, b]`, otherwise Douglas–Peucker samples on the 5-pulse
grid (one VOX tick). `KshOptions`: no curves, 10-pulse grid (ksh graph lines have no 1/32 slam rule; ksh2vox reads
ksh camera curves as straight lines, so a curved run is written as points). Monotone rule, jumps (= slams) and
the Douglas–Peucker thinning are §13's.

**Formats.** kson: graph / tilt points = bake; `impl["sdvx-editor"].camera_paths` = `{ "<lane>": [[y, [anchor, …]],
…] }` per lane name (`zoom_top` … `rotation_deg`, `tilt`), anchors as in `laser_paths`, pulses relative to y (the
run's first point). On read a path is attached only when it bakes to the run's points (1e-6), else dropped and
counted in `Chart.StaleCameraPaths`; unknown lane names are kept. ksh: `GraphFor` / `TiltFor` with `KshOptions`
(old eased straight pairs keep their `*_curve` line). VOX: the kson staging carries the bake; ksh2vox turns a kson
curve into tweens itself.

**Stale** (fallback: one straight anchor per point, shape kept): the inspector-free point commands
(`EditGraphPointCommand` etc., still used by tests), timing remaps that move camera points, `SetTiltMode` on a
pulse inside a manual run.

## 14. Tutorial

Interactive chaptered tutorial (Help → Tutorial…, song select footer); full description in `docs/TUTORIAL.md`.
Core `Tutorial/` holds the song definition (`tutorial.json`, embedded in SDVXEditor.Resources with the audio), the
chapter starting charts, ghost targets and checks — pure and NUnit-tested. Game `Tutorial/`: `TutorialManager`
(Component cached on `EditorGame`, per-frame step checks), `TutorialOverlay` (added after the `DialogOverlay`, so it
draws above dialogs in window pixels; only its card takes input), chapters as data, `TutorialAnchors` (named
spotlight targets: one-line registrations in UI code, resolvers for public parts in `EditorGame.Tutorial.cs`). Chapter
songs are copies "Tutorial (Chapter N)" in the editor songs folder (`TutorialWorkspace`, refuses to delete anything
not named so). Smoke part `tutorial` (`EditorGame.SmokeTutorial.cs`): each step's `Demo` is replayed as real input.

## 15. Chart check, side panels, time shift (2026-10-27 batch)

Items from the K-Shoot Editor v2 comparison that Johannes approved.

| Piece | Role |
|---|---|
| Core `Validation/ChartCheck.cs` | the engine: `IChartCheck` (rule: `Id`, `Category`, `Run(ChartCheckContext)` → `ChartIssue`s), `ChartIssue` (rule id, `IssueSeverity` Info / Warning / Error, message, pulse or null, `Notes` / `LaserPoints` to select on "jump to", category), `ChartCheckContext` (chart, tempo map, optional audio length and song folder; `Where(pulse)` = "m:b", `SlamPulses`), `ChartChecker` (`Rules`, `Register`, `Run` → `ChartCheckReport` sorted by position; a rule that throws becomes one `<id>.crashed` error, it never breaks the run). |
| Core `Validation/ChartRules.cs` | the built-in rules: `timing` (BPM ≤ 0, missing BPM, signature denominators not dividing 960, stop length, std_bpm outside the BPM range, disp_bpm characters), `notes` (negative, overlaps, touching holds = one ksh hold, effects on chips, empty chart), `laser` (width, empty / one-point sections — a lone slam is valid —, first point, point order, values outside 0..1, overlapping sections, stale laser / camera paths), `slam` (warnings instead of K-Shoot Editor's silent suppression: slams ≤ 1/32 apart on one lane, a moving segment ≤ 1/16 right after a slam, narrow slams < 0.1 lane width — our threshold, the kson spec ties `vol_auto` to width without a number), `effect-use` (FX hold effects / laser filters / parameter changes naming no definition or preset, slam sounds and camera spins on no slam — kson ignores both —, orphan chip sounds), `meta` (level, title, gauge total 1..99, non-ASCII romanised fields and file names, missing files, no audio), `audio` (objects after the audio end, with the chart offset), `format` (laser values between the 51 ksh steps, objects off the VOX 1/192-measure grid, stop lengths ksh/VOX round). |
| **Adding rules** | write a `public sealed class … : IChartCheck` with a public parameterless constructor anywhere in SDVXEditor.Core (e.g. `Core/Validation/EffectChecks.cs` for the VOX effect rules) — `ChartChecker.Rules` finds it by reflection, no list to edit; ids must be unique (a duplicate id is ignored), issue ids are `<rule id>.<what>`. Code outside Core (the Game) calls `ChartChecker.Register(rule)`. Set `Pulse` so the row can jump, and `Notes` / `LaserPoints` so it selects. Rules only read the chart. |
| Game `Panels/SidePanel` + `ChartCheckPanel` / `InspectorPanel` / `LayersPanel` | non-modal dark panels in the compose tab's right column (`EditorScreen.Features.cs`, left of the toggle bar), each toggled by a key and View menu item; hidden panels take no input. The chart check re-runs 250 ms after every edit while open; a row click seeks and selects (`EditorScreen.JumpToIssue`). Ctrl+S and File → Export run the check and append the summary to their toast — never blocking (`LastSaveCheck`). |
| Core `Editing/Inspector.cs` / `LaserInspector.cs` | `NoteInspector` (summary with `InspectorValue<T>` = common / mixed; `SetStart` moves the group so the earliest lands on the pulse, `SetLength`, `SetLane`; all checked against `OverlapRule`), `InspectorText` (position "m:b+p" or a pulse, length pulses / "1.5b" / "1/4" of a beat), `LaserInspector` (the anchors behind selected points and slam ends — baked curve samples are left alone —; position, start value V, landing value Vf, curve type, width keep-values; edits through `LaserPathCommands.Replace`, so curve types survive; a lone slam cannot lose its slam). |
| Core `Editing/TimeShift.cs` | Insert Blank / Delete Range: `RemapPulsesCommand` over `RetimeTargets.All` + `TempoShiftCommand` (BPM points after the cursor move, one on it stays; delete keeps the tempo of the range end at its start; time signatures move only for whole measures on a bar line, `TimeShiftResult.SignaturesMoved`). Delete removes notes starting in the range, sections lying inside it and bookmarks that would collide. One `CompositeCommand`; tests prove insert + delete = identity on every library kson. |
| Core `Editing/SlamAndHoldAssignments.cs` | `SlamSounds` (labels ↔ kson names, `Set` → `SetSlamSoundCommand`, undo restores the whole slam-event table), `HoldEffects` (`Of`, `Next` = definitions, presets, none; `Assign` = one `SetHoldEffectsCommand`), `LaserSwap` (lane change, values and paths kept, refused on overlap). `ComposeEditor.NewHoldEffect` / `NewSlamSound` (set by the screen from the toolbox) give new FX holds / the slams of new sections their effect / sound in the same undo step; Ctrl+click on an FX hold with an FX tool cycles its effect. |
| Layers (`Panels/LayerVisibility`) | per `EditorLayer` (BT, FX, laser L / R, timing markers, bookmarks, grid): `ComposePlayfield.Layers` skips drawing, `ComposeEditor.Layers` skips hit tests, hover and box selection; the timeline and the bottom summary hide bookmarks. "only" applies to the object layers. |
| Loop (`EditorClock.Loop`) | (start, end) ms; `Update` jumps back at the end (`LoopWraps`), starting playback outside the range starts at its start; the timeline shades it green. Shift+Space picks selection → bookmarks around the cursor → measure. |
| Snap divisors | `TimingMap.SnapDivisors` (= `EditorColours.Divisor.Steps`) adds 1/64, 1/96, 1/192; their grid lines are rounded to the 240-per-beat pulses (`Snap` already rounded 1/32). Timeline and playfield thin out ticks per snap level below 4 px, so the finest levels appear only when zoomed in. |
| Gauge (Core `ScoreProcessor`, `GaugeType`) | Normal (as before), Easy (our definition: halved ERROR damage), Hard (USC `GaugeHard`, read from `Main/src/Gauge.cpp` + `Main/include/Gauge.hpp`: start 100 %, gains × 12/21, chip ERROR −9 %, tick ERROR −2.25 %, damage × clamp(1 − 2·(0.3 − g), 0.5, 1); `Failed` at 0). editor.ini `PlaytestGauge`, Options → Playtest, F2 in the playtest cycles and restarts; a failed run stops with the result "TRACK CRASH (hard gauge)". |
| Laser entry / exit pieces | rendering only, after USC (`Track.cpp` draws `laser_entry` for every segment without a predecessor and `laser_exit` after the last segment only when it is a slam; `LaserTrackBuilder` sizes them from the default skin's 50×256 / 50×200 px textures at two laser widths: 2.56 / 2 band widths). `ComposePlayfield.ENTRY_LENGTH` / `EXIT_LENGTH`, drawn fading away from the laser in compose and playtest; not hit-tested (smoke-checked). |

Lone slams: a section may be a single slam point (`LaserPath.IsSectionShape`); see §13 and `LoneSlamTests`.

## 16. Audio effect preview (DSP)

The effects tab's **play effects** checkbox (editor.ini `PlayEffects`, default off) puts the chart's audio effects on
the song while it plays: FX hold effects, laser filters and the slam volume, in the editor and in the playtest.

| Piece | Role |
|---|---|
| Core `Audio/Effects/EffectValue.cs` | kson value syntax: `EffectValue` (length `1/N` / decimal = fraction of a whole note, tempo-synced; `ms` / `s`; samples; `%` / `1/N` rates; Hz / kHz; dB; on/off), `EffectValueSet` = `Off>OnMin-OnMax` with the spec's shorthands; `Sample(on, v, bpm)`. |
| Core `Audio/Effects/EffectSpecs.cs` | `EffectKind`, `ParamId`, the parameters and defaults per type (the spec's via `EffectCatalog`; the "implementation-dependent" filter ones are K-Shoot Mania v2's: peaking 50–9000 Hz / bandwidth 1.2 oct, low-pass 15000–800 Hz / q 3.6, high-pass 100–2200 Hz / q 5.0). `EffectParams` = the resolved numbers of one slot. |
| Core `Audio/Effects/EffectTimeline.cs` | immutable snapshot of a chart's effects; `Evaluate(songMs, LiveEffectInput, EffectFrame)` fills three slots (FX-L, FX-R, laser bus) without allocating. Rules after K-Shoot Mania v2 (`AudioEffectMain.cpp`, `AudioEffectUtils.cpp`): last `long_event` of the hold; definition < `param_change` < the event's overrides; one instance per effect (left lane wins); laser v = max(left, 1 − right) of the lanes with a section, sampled `peaking_filter_delay` earlier for the peaking filter (unstated = 40 ms, the ksh default); filter = last `filtertype` switch, default `peaking_filter`; legacy `filter_gain` = parameter changes of the built-in filters (peaking gain, high-pass q 2–8, low-pass q 2–5.2). Triggers: gate / wobble at bar lines; retrigger / echo on the `update_period` grid (below a whole note: from every bar line; longer: whole periods of measures from the start), `update_trigger` switches and, with `off>on`, the press; sidechain on the `period` grid; the rest at the activation. Live input (playtest): a hold counts while its button is down or within `GraceMs` (30 ms + the render lead) of its start; a laser while its cursor is locked. Preview: a forced effect for a window (laser filters swept 0 → 1 → 0). |
| Core `Audio/Effects/Dsp/*` | `EffectRack` (the chain: FX-L → FX-R → laser; evaluates the timeline every 64 frames, 3 ms cross-fade when a slot turns on/off, an input history of 12 s for retrigger loops; a jump of more than `MAX_SLIP_FRAMES` (4096) = seek resets the effects, a smaller slip only re-anchors the time — MP3/OGG decoding hands over ~50-frame overlaps for a few reads after a seek, and below 100 % rate BASS_FX's tempo stream re-reads ~50 of every 392 source frames, so stateful effects (filters, delay lines) are approximate there; counters for the smoke test), `Biquad` (RBJ cookbook), `FilterCurve` (K-Shoot's laser cutoff / gain tables, sampled), one `EffectDsp` per type (below). Pure C#, no framework — the offline render tests run it on generated buffers. |
| Game `Audio/EffectAudioHost` | attaches a BASS `DSPPROC` to the song's **source** decode stream (osu-framework `TrackBass.activeStream` is the BASS_FX tempo stream; `BassFx.TempoGetSource`), so the rack sees song-time samples at any playback rate. The song time of a buffer is `BASS_POS_DECODE` − buffer length (in a DSPPROC the decode position is the buffer's end — measured with a probe on a decode stream), i.e. effects are sample-accurate on the music regardless of the output latency; hitsound lead and the universal offset act on the clock and are untouched. Off = `ChannelRemoveDSP` (no cost). Rebuilds the timeline after edits (≤ every 60 ms). `Preview(name, laser)` = the effects tab buttons. `switch_audio` files are decoded by BASS in the background and resampled to the song's format. Float and 16-bit streams. |
| Playtest | `PlaytestScreen.EffectAudio` (set by the editor): FX buttons (`GameplayJudge.Held`), laser lock (`Locked`), autoplay = chart-driven. The audio thread renders about `LeadMs` ahead of what is heard, so a live press is heard that much later (the grace window covers on-time presses); autoplay is exact. |

Effects (sources: kson spec "Audio effects & parameter list", ksmaudio `src/AudioEffect/DSP/*.cpp`, unnamed-sdvx-clone
`Audio/src/DSP.cpp`; our own code):

| Effect | Implementation |
|---|---|
| Retrigger | KSM: the audio from the last trigger looped every `wave_length`, first `rate` of each loop audible (12-frame fade, then silence); read from the input history. `wave_length` 0 = bypass (spec). |
| Gate | KSM exact: cycle from the bar line, open for `rate`, 12-frame fade-out, gain lerp(1, gate, mix). |
| Echo | feedback delay of `wave_length`, y = x + feedback·y[−L], cleared at each trigger (KSM shares its code with retrigger; its exact read is not public). |
| SideChain | KSM exact: linear attack down to 0, hold, release up, gain lerp(env, 1, 1/ratio), on the `period` grid. |
| TapeStop | KSM exact speed curve ((speed·100+1)·(2.5^(−n/8000)+0.5)/500000 per 44.1 kHz frame), delay-line read at that speed (linear interpolation), fade below speed 0.25. |
| BitCrusher | sample-and-hold every `reduction` samples (fractional), as KSM / USC. |
| PitchShift | own two-tap delay-line shifter (sin²/cos² windows over `chunk_size`), ≤ 16 ms latency; `overlap` unused. KSM/USC use their own / SoundTouch. |
| Phaser | `stage` all-pass biquads (q), log sweep `freq_1`↔`freq_2` by a triangle LFO of `period` from the press, right channel offset `stereo_width`, feedback, high shelf `hi_cut_gain` at the band centre; lerp(x, chain, mix) (50 % = full notch depth). |
| Flanger | delay `delay`…`delay + depth` samples by a triangle LFO of `period`, feedback, R offset `stereo_width`, wet = (x + delayed)·`vol`. |
| Wobble | KSM exact LFO shape (sin-shaped triangle from the bar line), cutoff lerp(freq_2, freq_1, lfo), low-pass q. |
| Peaking / low-pass / high-pass filter | KSM: v eased ≤ 0.01 per frame, mapped in table space between `freq` and `freq_max`; peaking gain = table dB (max 34 dB) × `gain`, bandwidth in octaves, dry below 100 Hz with a 50 ms release; low-pass dry above 14.8 kHz, high-pass below 200 Hz. The tables are interpolated from 10–16 sampled entries. |
| SwitchAudio | the other file's PCM replaces the song while active (same position). |
| Slam volume | `HitsoundPlayer.ApplySlamVolume`: slam sample volume = min(1, 2 × `key_sound.laser.vol`) (50 % = unchanged); `vol_auto` is not applied. |

Tests: `AudioEffectTests` (value syntax, tempo sync, overrides, parameter changes, triggers, live gating, laser value,
mirroring, peaking delay, filter switches + filter gain, curves), `AudioEffectRenderTests` (offline renders: bit-identical
without an effect, LPF −29 dB high band, HPF on 100 Hz, peaking +16 dB at its cutoff, gate gaps at 4 per second,
retrigger repeats, sidechain 1/ratio, bitcrusher hold length, pitch shift octave, tape stop, echo repeats at the
feedback level, wobble sweep, player gating, seeks, cost ≈ 75× real time for three slots). Smoke part `dsp`.

