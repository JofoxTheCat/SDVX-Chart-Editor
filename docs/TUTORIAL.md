# Interactive tutorial

Two chaptered, hands-on tutorials for complete beginners. Open them with **Help → Tutorial…** in the editor, the
**Tutorial** button in the song select footer, or the one-time "New to charting?" card on first start. The chapter
list has a tab per tutorial — the new one first:

| Tutorial | Song | Length | Song folder | Ticks (editor.ini) |
|---|---|---|---|---|
| **Tutorial (new, [REDACTED] by Fax)** — "main" in code | Fax – "[REDACTED] (Cut Ver.)", 235 BPM, 53.8 s | 95 steps, ≈ 18¾ min | `Tutorial` | `TutorialMainProgress` |
| **Tutorial (classic)** — temporary | synthesised placeholder, 150 → 175 BPM, ~26 s | 69 steps, ≈ 10 min | `Tutorial (classic)` | `TutorialProgress` |

The classic tutorial stays, unchanged and fully playable, until Johannes has collected the feedback on it and the
mapper has played it once (see "Removing the classic tutorial" below).

Early steps show exactly what to do (ghost notes to copy), later steps only give a pattern or a count, the last ones
check rules on a section the user maps alone. Every chapter can be run on its own.

## Main tutorial — chapters

Johannes played it on 2026-10-27 ("explain more, more details, fix the highlighting"); since then every feature a
step touches is explained on its own card, dense steps are split into **numbered tasks** (the card ticks them off one
by one and the spotlight moves to the current one), and every card uses the card markup (keys, UI names and the
editor's colours in colour, bullets). Steps marked *info* end with Next; the others end by themselves.

| # | Chapter | Steps | Min | Steps (tasks in brackets) |
|---|---|---|---|---|
| 0 | Song Setup & Timing | 18 | 3.25 | Welcome *info* · create a song from audio · EDIT · the editor at a glance *info* (every tab with its key) · Song Setup (open · Charter) · difficulty and level (Difficulty page · Level 5, exact) · keep the changes (Jacket/Audio explained, OK) · Timing tab · BPM **exactly 235** · offset **682 ± 1 ms** · metronome (tick · play · stop) · open F6 · add a speed point (box says Speed · +) · put it at the drop (Offset 16001 ± 1 ms · tick Jump to · ×1.25 exactly) · "When applying timing changes…" *info* · Property Transformer *info* · OK · save |
| 1 | Chips, Holds and FX | 17 | 3.0 | the playfield *info* (lane colours) · the toolbox *info* (every tool + key) · pick BT Chip · beat snap 1/1 (white/red/blue/purple in colour) · right-click delete · chips (bar 1) · holds (BT Hold · drag) · hold modifier (BT Chip · Ctrl+drag) · resize (Select · drag the end) · FX chips (tool · click) · FX hold (tool · drag) · select (tool · box) · copy · paste (move the line · paste) · mirror / undo / redo · your turn (bar 6) · save |
| 2 | Lasers | 10 | 2.5 | lasers *info* · the laser tool (tool · side) · straight laser · slam (side · click placement) · curves (side · 3 anchors + right-click) · anchors (Select · hover) · anchor types (select · right-click anchor 1 · Bezier) · wide lasers (deselect · tool · Wide Laser · side · drag) · your turn (deselect · Wide off · map bars 12–13) · save |
| 3 | Navigating | 10 | 2.0 | timeline *info* · mini map *info* · Alt+wheel zoom · slow it down (50 % · play · pause · 100 %) · bookmarks (add · next) · collab list (open · close) · jump to a time (click the time · type the drop) · modding timestamps (Select · box · copy) · open a timestamp (click the time · paste + Enter) · **extra (skippable):** reference difficulty (File · Open Difficulty for Reference · ADV · Esc) |
| 4 | Camera & Background | 14 | 2.75 | camera tab · the camera lanes *info* (every column, the badges) · zoom at the drop (**numbered camera ghost rings 1→2→3** in zoom_bottom, a labelled band over the drop) · camera anchor types (right-click anchor 1 · Bezier) · the preview (play · pause) · manual tilt (a ghost ring) · **automatic tilt** (normal / bigger / biggest / zero / keep explained, click bigger) · a spin on a slam (pick spin · ring 1 at the slam · drag its end to ring 2) · background view · the background preview *info* (what K-Shoot shows vs. what the game plays) · K-Shoot backgrounds *info* (image / layer / movie) · game background and post effects *info* (bg_no, post effects, SPCONTROLER rows) · what each format keeps (export view) · save |
| 5 | Effects & Sounds | 10 | 1.75 | effects tab · select the FX hold ("select FX in view" — the hold turns blue, the list shows it) · pick an effect (flanger — retrigger/gate/wobble/echo presets are silent without a wave length; the effects explained) · assign it · laser filters *info* (peaking / low-pass / high-pass / bitcrusher, live playback) · a filter switch (low_pass_filter · at cursor) · the switch list *info* (position, filter, the x) · **hear the effects** (tick "play effects during playback" · "preview laser filter") · FX chip sounds (snare · as default) · save |
| 6 | Test & Difficulty | 8 | 2.0 | your own section (bars 16–19: ≥ 12 objects, ≥ 1 laser, 1/8 grid) · test it (F5 · Tab autoplay; keys) · results (Esc · Esc) · how hard (F4 · Difficulty page) · use the estimate (Use estimate · **OK**) · save · back to song select · the skill radar *info* (the whole radar with its six labels) |
| 7 | Save, Export & Share | 8 | 1.5 | export for K-Shoot Mania (File · Export Song · ksh…) · .svz (File · Export Song · .svz) · Support the Artist *info* · all shortcuts (open · close) · **back to song select** · options (Ctrl+O — in song select, like osu! · search "hitsound") · key bindings (clear the search · Input · close) · that's it *info* |

Chapter 7 ends in song select on purpose: in the editor Ctrl+O is Open Difficulty and the options are Ctrl+Shift+O,
in song select Ctrl+O opens the options (Johannes: "man sollte bei dem tutorial step also einmal wieder zurück
gehen"). Should the user open Open Difficulty by mistake anyway, the card says what that window is and how to close it.

Checks are exact where the step names a value (BPM 235, offset 682 ± 1 ms, the speed point at 16001 ± 1 ms with
×1.00 → ×1.25, level 5, the camera anchors on their rings, the tilt point, the spin at the slam and one bar long, the
effect on THE FX hold, the filter switch at the laser); the progress line says what is still wrong ("230 — needs 235
(5 too low)").

The budget lives in Core `Tutorial/Main/MainTutorialPlan.cs` (steps and minutes per chapter; target 18¾ min, ceiling
20): the chapters take their minutes from it, the smoke part `tutorial` fails when a chapter's step count differs,
`MainTutorialTests.MainPlanFitsItsBudget` keeps the total in range. Estimates: ~10–12 s per information step, ~10 s per
task plus reading, 30–60 s per group of ghost notes, 1–1.5 min per free-mapping section.

## Card markup, tasks, spotlight areas

- **Markup** (`TutorialRichText`): `{key:Action}` = the binding in effect, drawn as a key; `[k]Shift[/k]` a key name;
  `[ui]BT Chip[/ui]` a button / tool / field; `[b]…[/b]` bold; `[c=ff5050]…[/c]` a colour (snap and laser colours);
  a blank line = paragraph, a line starting with "• " = a bullet with a hanging indent.
- **Tasks** (`TutorialStep.Tasks`, `TutorialTask`): numbered actions of one step, each with its own text, spotlight
  and condition; a ticked task stays ticked; the step is done when all are (unless it has its own `Done`).
- **Spotlight targets**: `Area` (a screen rectangle: `TutorialContext.PlayfieldRegion` for a chart stretch,
  `CameraColumn` for one camera lane column, `DeepRect` for a drawable plus everything it draws outside its box — the
  skill radar's labels), else `Target` (a drawable), else `Anchor` (a name). Tasks have the same three.
- **Zones** (`Zone` + `ZoneLabel`): a labelled yellow band over the compose playfield and over the camera lanes.
- **Camera ghosts** (`CameraGhosts`): numbered rings in the camera lanes, green once the chart has that anchor.

## The song (main tutorial)

**Fax – "[REDACTED] (Cut Ver.)"**, shipped as `src/SDVXEditor.Resources/Tutorial/Main/tutorial.mp3` (the file
Johannes received, unchanged: 320 kbps stereo MP3, 53.76 s, no ID3 tag and no LAME/Xing header — so the importer
names the dropped file's song after the file, "Tutorial", and BASS and ffmpeg decode it with the same zero start).
**Used with the artist's permission; it is not covered by the editor's licence** and may not be reused from this
repository. Artist link: <https://soundcloud.com/fax0508> — written into every tutorial chart as kson
`impl["sdvx-editor"].song_sources`, so "Support the Artist" shows it.

Timing — **235 BPM, first downbeat 681.6 ms → offset 682 ms** (onset analysis, numpy on the decoded audio):

- 235 BPM is unambiguous: the beat-grid score is 2.7–3.1 at 235 against ≤ 1.5 at ±0.5 BPM.
- Two grids half a beat apart fit the onsets: A = 43.0 + n·255.32 ms and B = 170.7 + n·255.32 ms. Every 8th-note slot
  over 8-beat cycles, per band: the snare/noise band (1.5–6 kHz) peaks on every second B beat (slots 3, 7, 11, 15:
  1.00 / 0.96 / 0.94 / 0.92 against ≤ 0.6 elsewhere) — a backbeat on B — and the kick band follows B on beats 1 and 3
  in the drop. **B is the beat grid; A is the off-beat.**
- The snare only fixes the phase to two beats: the downbeat is 170.7 or 681.6 (+ n·1021.28). The song structure
  decides: the first sound of the song (682.1 ms, strength 0.64) and the drop (bass in from 16 000.5 ms = 681.3 +
  60 beats) both land on bar starts of the 681.6 grid; on the 170.7 grid both would sit on beat 3, and its "bar 1"
  would start in silence (RMS 0.006). So bar 1 starts at 681.6 ms.
- Johannes estimated "probably like 638" — 43.6 ms (≈ 1/6 beat) earlier, i.e. on neither grid; most likely the
  attack start of the first hit seen in a waveform. **To confirm by ear** (metronome on, chapter 0 does exactly that).

Sections (0-based measures in `tutorial.json`, 1-based bars in the cards): intro bars 1–7 (no drums), the fill
bar 8 (hits on beats 2 and 3), build-up bars 9–15 (light bass), the drop bars 16–47, the last hit bar 48 (beat 3,
49.2 s), then the tail rings out to 53.8 s. Preview from the drop (16 001 ms).

## The chart, and swapping in the mapper's chart

The main tutorial rebuilds ONE chart, the **solution**: `src/SDVXEditor.Resources/Tutorial/Main/solution.kson` when
that file exists, otherwise the placeholder built in code (Core `MainTutorialCharts.Placeholder`: quarter notes and a
few eighths, half-bar holds, simple lasers, a four-bar pattern in the drop, a slam every eight bars, the ×1.25 speed
jump at the drop). Each chapter starts from the solution with the parts the user rebuilds in this and later chapters
cut out (plus a stray chip and a too-short hold to fix in chapter 1); camera work appears from chapter 5 on, effects
from chapter 6 on. The ghost targets are **read from the solution** inside the exercise windows of `tutorial.json`.

When the mapper's chart arrives:

1. Save it as kson (the editor's Ctrl+S does) and copy it to `src/SDVXEditor.Resources/Tutorial/Main/solution.kson`.
   It must keep one tempo of 235 BPM and 4/4 throughout (`MainTutorialCharts.Solution` refuses anything else); title,
   artist, offset, bookmarks and sources are taken from `tutorial.json`, so they need not match. Put the mapper's name
   into `"charter"` in `tutorial.json` (and the Thanks list).
2. Run `dotnet test --filter MainTutorialTests`. `SolutionFitsEveryExercise` lists every window that does not hold
   what its exercise needs (`MainTutorialCharts.Validate`). Either move the window in `tutorial.json` (`exercises`) to
   bars of the chart that do, or ask the mapper for that pattern there.
3. Read the cards of the steps below once more: a few say what the music does ("the music hits on every beat of the
   first bar") or name a side ("Switch to laser R") — the side and counts are taken from the chart, the words about
   the music are not.

| Window (`tutorial.json`) | Default (bars, 1-based) | Step(s) | Needs in the solution |
|---|---|---|---|
| `chips` | bar 1 | ch 1 "Place chips", "Select", ch 3 "Modding timestamps" | 2–8 BT chips on whole beats (snap is 1/1 in that step) |
| (paste = chips + 1 bar) | bar 2 | ch 1 "Copy and paste", "Mirror" | — (targets = bar 1 copied and mirrored; the solution's bar 2 BT chips are cut out in chapter 1) |
| `holds` | bar 3 | ch 1 "Holds" | 1–4 BT holds |
| `fxChips` | bar 4 | ch 1 "FX chips" | 1–4 FX chips |
| `fxHold` | bar 5 | ch 1 "An FX hold", ch 5 "An effect on an FX hold" | 1–2 FX holds |
| `chipPractice` | bar 6 | ch 1 "Your turn" (free) | anything (cleared in chapter 1) |
| `holdModifier` | bar 7, beats 1–2 | ch 1 "The hold modifier" | a BT hold |
| `holdResize` | bar 7, beats 3–4 | ch 1 "Resize a hold" | a BT hold ≥ ½ beat (starts at ¼ of its length) |
| `slam` | bar 8 | ch 2 "A slam", ch 4 "A spin on a slam" | a normal-width laser with a slam |
| `straightLaser` | bar 9 | ch 2 "Lasers", "A straight laser", ch 5 "Laser filters" | a 2-point laser without a slam |
| `curveLaser` | bar 10 | ch 2 "Curves", "Anchors", "Anchor types" | a laser drawn as a 3-anchor curve |
| `wideLaser` | bar 11 | ch 2 "Wide lasers" | a wide laser |
| `laserPractice` | bars 12–13 | ch 2 "Your turn" (free) | anything (cleared in chapters ≤ 2) |
| `finalPractice` | bars 16–19 | ch 6 "Your own section" (free) | anything (cleared in chapters ≤ 6) |
| `drop`, `dropSpeed` | bar 16, ×1.25 | ch 0 F6 steps, ch 3 "Type a time", ch 4 zoom + camera anchors | — (the solution should carry the same speed jump, or set `dropSpeed`) |

No ghost target may lie in a free-mapping window (`Validate` says so). Without `solution.kson` nothing changes: the
placeholder is used.

## One song folder per tutorial

Each tutorial works in ONE folder of the editor songs folder: `Tutorial` (main), `Tutorial (classic)`. Chapter 0
removes it and stages the audio as `<editor data>\Tutorial files\Tutorial.mp3` (classic: `Tutorial (classic).ogg`) for
the drop — the import creates the folder again under that name. Every later chapter rewrites the folder's chart files
(`*.kson`, `*.ksh`) in place to its starting charts and keeps everything else (a jacket the user added). The audio is
written once (the classic chapter 0 copy named after the folder is replaced by the template's `tutorial.ogg`).

A marker `.sdvxeditor-tutorial.json` in the folder records the tutorial, the chapter and SHA-256 hashes of the charts
it wrote. The tutorial only ever writes into or deletes a folder whose marker names it (or, without a marker, that
holds nothing but that tutorial's byte-identical audio and charts); a user's own song called "Tutorial" is refused
with a message.

Starting a chapter asks first ("Start chapter N from its beginning? … Replace / Cancel") when that would throw away
the user's work: unsaved changes in the tutorial song, or saved changes (the hashes differ) / chart files the tutorial
did not write. It does **not** ask when the user simply moves on — "Next chapter" (not even for unsaved changes: the
finished chapter's result is superseded), or Start on the chapter after the one the folder holds — because replacing
the previous chapter's result with the next starting chart is the point; and
Start on the chapter the folder already holds continues it without rewriting anything.

Migration: on start-up the editor removes the per-chapter folders of the first version, `Tutorial (Chapter N)`, but
only those that hold nothing but the classic tutorial's audio (byte-identical) and `.kson` charts, and the staged
`Tutorial files\Tutorial (Chapter 0).ogg` when it is that audio. Anything else is left alone and logged.

## How it works

| Piece | Role |
|---|---|
| Game `Tutorial/TutorialCourse.cs` | a tutorial: name, chapters, budget, song folder, embedded template, progress key, how its chapter charts are built. `TutorialCourses.All` = main, classic. |
| Game `Tutorial/TutorialWorkspace.cs` | the template (embedded `SDVXEditor.Resources/Tutorial/…`) → the tutorial's one song folder; marker, state (`FolderState`), the migration. |
| Game `Tutorial/TutorialManager.cs` | chapter list, start (+ the "Replace?" question) / reset, the current step, per-frame condition check (auto-advance after a short tick), Back (review mode), Skip, Exit, "Take me back", progress per tutorial in editor.ini, `TutorialHintShown`. |
| Game `Tutorial/TutorialChapterListDialog.cs` | the chapter list with a tab per tutorial. |
| Game `Tutorial/TutorialOverlay.cs` / `TutorialPanel.cs` | above the dialogs: dimmed spotlight with an animated outline and a bobbing arrow, ghost objects (green once placed, red ✕ to delete), the free-mapping zone, the instruction card. |
| Game `Tutorial/TutorialChapters.cs` | the chapter lists and the helpers both tutorials share. |
| Game `Tutorial/Main/TutorialChapters.Main*.cs` | the main tutorial's chapters as data: text (`{key:Action}` shows the binding in effect), target, condition, progress line, requirement + restore, ghosts / zone, and the smoke test's scripted user (`Demo`). |
| Core `Tutorial/Main/MainTutorialSong.cs`, `MainTutorialCharts.cs`, `MainTutorialPlan.cs` | the main song's definition (tutorial.json), the solution / placeholder / chapter charts / targets / validation, the budget. NUnit: `MainTutorialTests`. |
| Core `Tutorial/TutorialChecks.cs` | ghost targets (`TutorialTarget`) and the checks both tutorials use (`IsPlaced`, `OffGrid`, `CountSlams` …). |
| Game `Tutorial/TutorialAnchors.cs` | named spotlight targets. |

Anchors: `compose.tool.{select,btchip,bthold,fxchip,fxhold,laser}`, `compose.toggle.{grid,wide,slam,lock,mirror,hit}`,
`compose.divisor`, `timing.bpm`, `timing.offset`, `timing.base-speed`, `songselect.tutorial` (registered in the UI
files); `editor.playfield`, `editor.timeline`, `editor.tabs`, `editor.menu`, `editor.bottombar(.time/.collab/.bookmark-add)`,
`compose.toolbox`, `compose.laser-side`, `camera.lanes`, `camera.preview`, `timing.metronome`, `timing.setup-panel`,
`songselect.radar`, `songselect.wheel`, `options.search` (resolvers). Controls inside dialogs are found by their text.

Robustness: a step's requirement is checked every frame; when the user is elsewhere (other tab, dialog closed,
editor left, playtest) the card says how to get back and offers "Take me back". Esc keeps its normal meaning
everywhere — the tutorial never takes a key.

## Removing the classic tutorial

When Johannes says so, delete / edit exactly these:

- delete Game `Tutorial/TutorialChapters.Setup.cs`, `TutorialChapters.Compose.cs`, `TutorialChapters.More.cs`;
- delete Core `Tutorial/TutorialSong.cs` (`TutorialSongDefinition`), `TutorialCharts.cs`, `TutorialPlan.cs`; move the
  four constants the shared code uses (`Measure`, `Beat`, `At`) into `MainTutorialSong` (they already forward there)
  and `TutorialChecks`, and drop `ITutorialSong`'s classic implementation;
- delete `src/SDVXEditor.Resources/Tutorial/tutorial.json` and `tutorial.ogg`, and `tools/TutorialSongGen`;
- `TutorialChapters.cs`: the `Classic` list; `TutorialCourse.cs`: `TutorialCourses.Classic` and its entry in `All`;
  `TutorialContext.Song` (the classic definition); `TutorialWorkspace.Definition` and, in `MigrateLegacy`, the classic
  audio comparison (keep the method only while old installs may still have `Tutorial (Chapter N)` folders);
- tests: `TutorialTests.cs` (classic), the classic run in `EditorGame.SmokeTutorial.cs` (`fullRun(classic)` and its
  folder check), the `classic-*` screenshots;
- `EditorSetting.TutorialProgress` may stay (old ticks) or go; the chapter list then shows a single tab (it still
  works with one course);
- docs: the classic rows above and the classic section below.

## Classic tutorial (temporary)

8 chapters, 69 steps, ≈ 10 minutes, on the synthesised placeholder `tutorial.ogg` (made by `tools/TutorialSongGen`,
no samples, **CC0**): first beat at 300 ms, 150 BPM, bars 1–2 intro, 3–6 main, 7–8 a quiet breakdown, a tempo
change to 175 BPM at bar 9, bars 9–16 the fast part, ~26 s. Its definition is `src/SDVXEditor.Resources/Tutorial/tutorial.json`
(Core `TutorialSongDefinition`), its charts and checks Core `TutorialCharts`, its budget Core `TutorialPlan`
(`TutorialTests.PlanFitsTheTenMinuteBudget`), its chapters Game `TutorialChapters.Setup/Compose/More.cs`:

| # | Chapter | Steps | Min |
|---|---|---|---|
| 0 | Song Setup & Timing | 16 | 2 |
| 1 | Chips, Holds and FX | 12 | 1.5 |
| 2 | Lasers | 8 | 1.75 |
| 3 | Navigating | 8 | 1 |
| 4 | Camera & Background | 7 | 1 |
| 5 | Effects & Sounds | 5 | 0.75 |
| 6 | Test & Difficulty | 8 | 1.25 |
| 7 | Save, Export & Share | 5 | 0.75 |

It now shares the one-folder rule (`Tutorial (classic)`); nothing else about it changed.

## Smoke test

`SDVXEDITOR_SMOKE_ONLY=tutorial` (no `--open` needed; use `SDVXEDITOR_SMOKE_STORAGE=<name>` for a separate storage):
first the migration on fake `Tutorial (Chapter N)` folders (own one removed, one with a foreign file kept, the staged
audio removed), then a scripted beginner runs **both** tutorials — the main one from the song select footer button,
the classic one from its tab — every chapter with real input (screen-layer clicks/drags/keys/wheel through the smoke
`ManualInputManager`, the card and dialogs through the root input manager, key presses resolved from the current
bindings) and Next on information steps. Typing into text boxes (and pasting the timestamp) is the one stand-in (the
OS text stream cannot be injected). Checked on the way: the off-track hint + "Take me back", Back (review mode does
not jump forward), the ticks per tutorial under their own editor.ini keys, every chapter has exactly the steps its plan
budgets, after every chapter exactly one song folder per tutorial (never `Tutorial (Chapter N)`), the marker, a single
chapter (4) started from Help → Tutorial…, Reset on chapter 1 asking "Replace?" first (chapter 4's saved camera work
would go) and restoring chapter 1's start, Exit tutorial, and at the end exactly `Tutorial` + `Tutorial (classic)` and
at most one staged audio per tutorial.

Highlighting is checked too: every click, drag and wheel turn of a step's scripted user must land inside the spotlight
the step (or its current task) shows, and a step that names a target must have it on screen when it starts — so a
spotlight on the wrong control fails the run. `SDVXEDITOR_TUTORIAL_SHOTS=all` (with `SDVXEDITOR_SCREENSHOT_DIR`) saves a
screenshot of every step and of every task change (`tutorial-all-<course>-c<chapter>-<step>-<title>[-t<task>].png`).
