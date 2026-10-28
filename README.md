# SDVX Chart Editor

A native chart editor for SOUND VOLTEX-style charts (4 BT, 2 FX, 2 lasers)
whose workflow follows the osu!stable mania editor. Built on C# / .NET 9 and
[osu-framework](https://github.com/ppy/osu-framework) (MIT). VOX export goes
through [SDVX-Chart-Converter](https://github.com/JofoxTheCat/SDVX-Chart-Converter)
(git submodule in `external/`).

**Status: 0.1.0, not released yet.** Song select, the full editor (compose, timing,
camera and game effects, song setup), playtest, two tutorials and VOX export work
and are covered by the tests and the smoke run. Nothing has been checked on a
real controller or in the game yet. Changes are listed in `CHANGELOG.md`.

See `docs/ARCHITECTURE.md` for the layout and decisions.

## Build & run

```
git clone --recursive <repo>
dotnet build SDVXEditor.sln
dotnet test SDVXEditor.sln
dotnet run --project src/SDVXEditor.Desktop
```

Requires the .NET 9 SDK. `ppy.osu.Framework` is pinned to `2026.807.0`, the
newest release that still targets net8.0 (later releases require .NET 10).

### Command line

| Argument | Meaning |
|---|---|
| `--launcher-config <path>` | launcher config (`sdvx_rpc_config.json`) to read the song library from |
| `--songs <dir>` | additional song folder |
| `--open <folder>` | open a song folder straight in the editor |
| `--chart <slot>` | with `--open`: difficulty `0`-`4` or `nov/adv/exh/inf/mxm` (default: highest) |
| `--import <file>` | import a `.svz` package (or `.zip`, song folder, chart, audio) into the editor songs on start; a name clash asks Replace / Keep both (`docs/SVZ_FORMAT.md`) |
| `--smoke-test` | open the window, walk all screens, exit with code 0 (1 on any failure; the last output line is `SMOKE PASSED in …` / `SMOKE FAILED …: [part step] reason`). With `--open` it also edits, undoes, saves and reloads a **copy** of that song (next to `SDVXEDITOR_SCREENSHOT_DIR`, else in a per-run folder under %TEMP%\SDVXEditor-smoke) and writes `smoke-report.txt` with frame times. `SDVXEDITOR_SMOKE_ONLY=camera,playtest` runs a subset of `songselect,walk,options,ui-input,bottombar,collab,editing,camera,effects,playtest,menus,sources,tutorial,features,dsp` (`dsp` = the audio effect DSP: checkbox, playback, preview, 50 % rate, playtest; checked through counters, smoke runs are muted) (`features` generates its own 8 s song: chart check, insert / delete time, swap colour, drop-downs, inspector, layers, loop, Song Setup extras, hard gauge) (`tutorial` runs every chapter of both tutorials with real input, see `docs/TUTORIAL.md`; `menus` clicks every menu item with the mouse, runs File → Create a New Difficulty twice, checks Ctrl+Z on a simulated QWERTZ layout and Alt+Enter; `options`, `ui-input` and `bottombar` need no `--open`; `bottombar` generates its own short-audio song and uses the OS clipboard, restoring its text afterwards); `SDVXEDITOR_SMOKE_DROP` names the folder/file dropped onto song select; `SDVXEDITOR_SMOKE_CONVERTER` names a ksh2vox 0.17.0+ folder for the camera part's export-badge switch check (default: a `SDVX-Chart-Converter` checkout beside the editor; skipped when none is found) |

### Release packages

```
git submodule update --init
pwsh tools/fetch_python.ps1
python tools/publish_release.py
```

Builds `release/SDVXEditor-<version>-win-x64.zip` and `release/SDVXEditor-<version>-linux-x64.tar.gz`,
both self-contained (no .NET install needed), each with the converter in `external/SDVX-Chart-Converter`, the
package `README.txt` (`tools/release/README.txt`), `LICENSE.txt` and `THIRD-PARTY-NOTICES.txt`. The Windows
package also carries `python/runtime` (Python + Pillow); Linux uses the system `python3`. ffmpeg is never bundled.
The version comes from `Directory.Build.props` (`--version` overrides it).

## Keys

| Key | Action |
|---|---|
| F1 / F2 / F3 / F4 | Compose / Camera & FX / Timing / Song Setup |
| F5 | Playtest from 2 s before the cursor (in-memory chart, editor rate + audio offset) |
| in the playtest | BT D F J K · FX C M · laser L W/E · laser R O/P · Tab autoplay · F3/F4 or -/+ speed · **F2 gauge normal → easy → hard (restarts; hard = EXCESSIVE, the run stops at 0 %)** · Enter pause · ` retry · Esc results, Esc again back (bindings in editor.ini `PlaytestBindings`, gauge in Options → Playtest) |
| Esc | closes the topmost thing first (dialog, menu, drop-down list, options, laser draft, reference view); with nothing open it leaves the editor, asking Save / Discard / Cancel when the chart has unsaved changes (Esc on that prompt = Cancel). Playtest: results, then back to the editor |
| 1-6 | Select / BT chip / BT hold / FX chip / FX hold / Laser (Shift: BT → FX) |
| Ctrl (or Alt) held | with BT Chip / FX Chip: click or drag places a hold (BT Chip + Shift too: FX hold). The key is the **hold modifier** in Options → Input (Ctrl by default, or Alt). It only counts on its own: once it is part of a shortcut (Ctrl+Z, Ctrl+wheel, Alt+wheel …) it does nothing until released, so an undo never leaves a hold ghost. With a hold tool it changes nothing. The chip tiles say "Ctrl: Hold" / "now Hold" |
| 6 again, Q / E | laser L / R (or click L \| R under the laser tile; the tile shows a big L / R badge) |
| Drag a hold's end | changes its length, start kept (select tool, or a BT/FX tool on its own kind); a white bar with ↕ marks the grab zone (7 px) on hover. Onto the start = chip. With several holds selected, all change by the same delta (only the grabbed one can become a chip, the others stop at one snap tick). The body still moves the hold, now by the distance dragged |
| Right click | delete the object under the cursor (laser tool while placing: finish; select tool on a laser: curve-type menu; on one end of a slam: only that end) |
| Shift while dragging a laser point / anchor | snap it to the beat divisor and the value grid (without it, edits of existing points / anchors are free: 5-pulse time steps, any value). Rebindable: Options → Input → Compose "Snap while dragging laser anchors" |
| W with lasers selected | they become wide / normal with the toggle, keeping their place on screen (narrowing clamps what lies beyond the lanes, the toast says how many); laser tool: Ctrl+Shift+click on a laser = toggle wide keeping its position, Ctrl+Shift+right click = keeping its values; Ctrl+W keeps the values |
| F held | **classic lasers** (K-Shoot / kson editor style) with the laser tool: every click a plain straight point, values on a 1/10 grid (1/20 wide), same beat + another value = slam, a click on the last point again finishes (right click too). Release = osu!-style anchors again, also in the middle of a laser. Rebindable (Options → Input → Editor · Compose); another key while it is down uses it up until released, like the hold modifier. The laser tile says "F: classic" / "classic lasers" |
| Slam ends | the two ends of a slam are two points: hover, click, Ctrl+click or box-select each end on its own; drag the start end (value + time) or the landing end (value + time); Delete removes the selected end (the slam becomes a plain point at the other value; on a lone slam the section goes), both ends selected remove the point |
| Ctrl+Shift+A | **chart check** (osu! AIMod): a panel listing errors / warnings / notes (ranges, overlaps, objects after the audio, what ksh / VOX round, undefined effects, non-ASCII romanised fields, hard slam patterns); a click on a row jumps there and selects the objects; it follows your edits. Saving and exporting run it too and only mention the result in the toast |
| Ctrl+Shift+I / Ctrl+Shift+U | **inspector** (exact values of the selection: note start / length / lane, laser position / value / slam landing end / curve / width; "(mixed)" for differing values, Enter applies to all, one undo step) / **layers** (show or hide BT, FX, laser L / R, markers, bookmarks, grid; "only"; hidden notes cannot be clicked) |
| Insert / Shift+Insert / Alt+Insert | insert one snap tick / one beat of blank time at the cursor / the insert-delete time dialog (snap ticks, beats, measures, pulses); everything from the cursor moves, the audio stays; whole measures on a bar line move the time signatures too. One undo step |
| Ctrl+Shift+Delete | delete the selection's time range (to the snap tick after its end) and pull everything after it back (without a selection: the dialog) |
| Ctrl+Shift+H | swap laser colour: selected lasers change L ↔ R with their values (Mirror flips the values too) |
| Ctrl+Shift+E / Ctrl+click on an FX hold | next FX effect of the selected FX holds / of that hold (FX chip / hold tool; Shift+Ctrl+click backwards). The toolbox **FX Effect** drop-down sets the selected holds' effect and the effect of new FX holds; **Slam Sound** sets the selected slams' sound (Auto / Down / Up / Swing / Mute, kson `slam_event`) and that of new slams |
| Shift+Space | loop playback over the selection, else between the bookmarks around the cursor, else the measure; again = off (green in the timeline) |
| Ctrl+wheel, Shift+1..9 | beat snap divisor 1/1 … 1/48 and the finer 1/64, 1/96, 1/192 (K-Shoot's finest quantize; rounded to the 240-per-beat pulse grid, the chart check notes objects off the VOX 1/48 grid); "set to 1/12 … 1/192" actions are bindable in Options |
| Ctrl+S | save as `.kson` (a `.ksh` is kept; the save goes to `<stem>.kson`) |
| Alt+wheel | over timeline or playfield: zoom (one shared zoom) · elsewhere: volume |
| wheel / Shift+wheel | over the playfield or timeline: seek one / four snap ticks · over a number field or stepper: one / ten steps |
| Ctrl+/ | all shortcuts (shows your current bindings) |
| Ctrl+O | options (song select; in the editor Ctrl+O is "open difficulty", options are Ctrl+Shift+O or File → Options…) |
| Ctrl+B / Ctrl+Shift+B | add a bookmark at the cursor / remove the nearest one (within 2 s); both undoable (Ctrl+Z), also as bottom-bar buttons |
| Alt+Left / Alt+Right | previous / next bookmark (bottom-bar ‹ › buttons) |
| Ctrl+C | copy the selection, and put its modding timestamp on the OS clipboard (see below) |
| Alt+Enter / F11 | Windowed ⇄ Borderless (a frameless window covering the screen; exclusive fullscreen only via Options → Graphics) |

Letter shortcuts follow your **keyboard layout**: Ctrl+Z is the key labelled Z — on a German QWERTZ keyboard that
is the key left of X, not the one next to T — and changes with the active layout (Win+Space) while the editor runs.
Digits, F-keys, arrows and punctuation stay positional and are shown with your layout's label (QWERTZ: Ctrl+/ reads
"Ctrl+-"). Playtest keys (D F J K, C M, W E O P) are positions, like osu!'s gameplay keys, and are shown with the
label your keyboard prints on that position.

## Menu bar

Every item does something visible and shows its current key. **File**: Create a New Difficulty (copy of the current
difficulty or an empty chart with timing/metadata/audio/bookmarks → a free NOV/ADV/EXH/INF/MXM slot → saved as
`nov.kson` … in the song folder (a launcher song is forked first) → Song Setup opens), Open Difficulty (Ctrl+O),
Open Difficulty for Reference, Save, Export Song (kson / ksh / VOX / .zip / .svz into the export folder), Revert to
Saved, Clear All Notes, Test Chart, Open Song Folder, Options, Exit. Switching, creating, reverting and exiting ask
Save / Discard / Cancel when the chart has unsaved changes (Esc too, once nothing else is open). **Edit**: undo/redo,
clipboard, select all / invert, clone, mirror, reverse, nudge, wide/slam toggles, bookmarks. **View**: tabs and
dialogs, background dim, Borderless/Windowed. **Compose**: tools, snap divisor, audio rate, the five toggles.
**Camera**: zoom-top / zoom-bottom / tilt point at the cursor, reset camera. **Timing**: time signature, metronome,
BPM / scroll-speed points, resnap, move all notes by a snap tick or a beat, preview point. **Help**: shortcuts, About
(version and licences). **Thanks**: the credits (tutorial song, chart, playtesters, components) — also the Thanks
button in song select's footer. The list is `src/SDVXEditor.Resources/Credits/credits.json`; edit names there.

## Song sources and Support the Artist

Song Setup → **Sources** takes links where the song can be heard or bought — music sites only (SoundCloud, Spotify,
Deezer, YouTube, YouTube Music, Apple Music, Bandcamp, Tidal, Amazon Music, Beatport, Niconico, Audiomack and their
short links). Other links are refused with the reason; `http://` and bare links become `https://`, tracking
parameters are dropped. OK is one undo step; the list is song-level (saved into every .kson difficulty on Ctrl+S,
ksh: `//#source|<url>` lines). When a song has sources, **Support the Artist** appears under the song details in
song select (a card across the details column: heart, label, how many links) and at the right end of the editor's menu bar: one link opens it, several show a list first, and every
link asks before your browser opens (the dialog names the domain; "Copy link" copies instead).
| click on the time | jump-to-time dialog: current time pre-selected (Ctrl+C copies it), type a time or paste a timestamp, Enter jumps, Esc closes |

## Bottom bar

Time and % (click: jump to time), the summary timeline, bookmark buttons (+ add, − remove nearest, ‹ previous,
› next), **Collab** (two people: the bookmark list with labels and colours), Play / Pause / Stop / Test and the
playback rate 25 / 50 / 75 / 100 % (the active rate is underlined). The scrubbable length is the audio file's
length once it is loaded; when the chart goes on after the audio, the range extends to the last object and the
summary shades the part without audio red ("no audio" where it fits; hovering the timeline explains its marks). Without audio it is the chart end plus 2 s (at least 10 s).

## Modding timestamps

Like osu!, Ctrl+C on a selection also puts a timestamp on the OS clipboard, for modding posts:

```
01:23:456 (01:23:456|BT-A,01:23:581|FX-L hold,01:24:000|LASER-L 0.25>0.75) - 
```

- The leading time is the earliest object; then one item per object, `time|lane[ hold][ value[>value]]`,
  ordered by time then lane. Times are `mm:ss:mmm` (minutes may exceed 59).
- Lanes `BT-A`…`BT-D`, `FX-L`, `FX-R`, `LASER-L`, `LASER-R`. `hold` marks a BT/FX hold. A laser point carries its
  value (0 = left, 1 = right, up to three decimals); a slam `from>to` (also when only one of its ends is selected).
- Pasting a timestamp into the jump-to-time dialog (click on the time) jumps to it and selects exactly those
  objects (compose tab, select tool). An object is found within 1 ms of its written time in its lane (a laser
  point also within 0.001 of its value); objects that are gone are reported as "not found", items that fit
  several objects as "ambiguous" (all of them are selected), unreadable items as "unreadable".
- Accepted as well: `mm:ss:mmm -` or a bare time, `mm:ss.mmm`, `ss.mmm`, plain milliseconds (also inside items,
  osu!'s `12345|…`), lanes spelled `BTA`, `bt a`, `FXL`, `LASER L`, `VOL-L`, `KNOB-R`, `->` for slams, and text
  before the time or after the `-`. Format and parser: Core `Editing/ChartTimestamp.cs` (NUnit-tested).

## Laser editing (osu!lazer slider style)

A laser section is edited like an osu!lazer slider path: its **anchors** carry curve types, the chart gets the
baked points.

- **Draw:** with the laser tool, *drag* on empty time for a straight section (as before), or *click* to start
  placing: every click adds an anchor in time order (time = snapped beat, value = mouse x, wide lasers into the
  overhang). The type follows the count like osu!: 2 anchors straight, 3 perfect curve (circular arc), 4+ Bezier.
  Clicking a placed anchor changes its type (inherit → auto segment start → straight → perfect curve → Bezier);
  where two curves meet at an auto anchor it becomes a Bezier anchor. A click on the last anchor's beat with
  another value makes a slam — even when it is still over that anchor's handle (Ctrl = free value keeps the handle
  click) — and a slam is a hard break for the typing (the curves before and after it are typed separately). Two
  clicks on one beat and a right click make a slam on its own: a **lone slam**, one point `V → Vf` and no tail (the
  same section dragging a slam sideways on one beat gives; official charts have 118,940 such sections). **Right click puts a last anchor at the cursor and finishes** (osu!; on the
  last anchor or before it it only finishes), **Esc cancels**, switching tools finishes.
- **Editing:** a press on any anchor drags it at once, also on a laser that is not selected yet; existing points and
  anchors move freely (5-pulse time steps, any value), **Shift snaps**. Bezier control anchors (the ones off the curve)
  may leave the lanes and cross each other in time — only the laser itself is kept inside 0..1 and moving forward.
- **Classic lasers (hold F).** While F is held, the laser tool places like the K-Shoot / kson editor: plain points
  joined by straight lines (no auto typing), values on a 1/10 grid (1/20 for wide lasers), a second click on the last
  beat makes a slam, a click on the last point again finishes. Releasing F returns to anchors; a laser may mix both —
  the clicks after the last classic point are typed by count from there. Classic points are ordinary Straight
  anchors, so an all-classic laser saves and exports exactly like plain points.
- **Slams are two points.** A slam is stored as one point / anchor with a start and an end value (kson `[v, vf]`,
  ksh two lines 1/32 apart, VOX two rows on one position), but in the editor its **start end** and **landing end**
  are separate handles (the landing end has a small dot in the laser colour): each is hovered, selected (click,
  Ctrl+click, a box over just that end) and dragged on its own — the start end changes the start value, the landing
  end the end value, and either moves the slam in time. Delete with one end selected (or right click on it, or
  "Delete slam start / end" in the anchor menu) leaves a plain point at the other value (on a lone slam the whole
  section goes: a single plain point is no laser); both ends selected (or
  "Delete anchor (both ends)") remove the point. Move, copy / paste, mirror and nudge act on the end's whole section;
  Ctrl+C writes the slam (`from>to`) into the timestamp. Each change is one undo step.
- **Select mode:** hovering a laser outlines it and shows its anchors (white = straight, yellow = perfect curve,
  red = Bezier, small grey = inherit, black dot = auto); a click selects it. The selected laser's anchors drag
  (time + value, snapped; Ctrl = free value); Ctrl+click on its path inserts an anchor. **Right click on an
  anchor:** Inherit / Straight / Perfect curve / Bezier / Delete anchor. **Right click on the path:** curve type
  of that segment / Delete laser. Slams stay two values on one beat and break the path.
- A laser is a function of time: a curve that would turn back in time is cut (see `docs/ARCHITECTURE.md`).
- **Two lasers of one side never touch:** sections of one side keep at least **1/96 note** (10 pulses = 2 VOX ticks
  = end, `-`, start in a 192-line ksh measure) apart — placing, drawing, dragging, pasting, moving and mirroring stop
  or refuse closer, with a message. A drag stops at the last position that keeps the gap. Retiming (delete range,
  resnap, keep notes in time) joins sections it pushes closer into one laser (same pulse = a slam); charts that
  already have closer sections stay editable (an edit may not bring them closer) and the chart check reports them
  (`laser.overlap` error for a shared pulse, `laser.gap` warning below 1/96).
- Saved losslessly in kson (`impl["sdvx-editor"].laser_paths`); other tools see the baked points (kson curve
  `[a, b]` where a 3-anchor Bezier is exactly one, otherwise points within 0.004 of the path). ksh export writes
  baked points only (more than 1/32 apart, so they never read as slams); VOX export passes kson curves to
  ksh2vox 0.17+, which bakes them itself. Old charts: every point is a straight anchor, kson curves are kept.

## Camera editing (anchors, like lasers)

The camera tab's curve lanes — zoom_top, zoom_bottom, zoom_side, center_split, rotation_deg and the manual tilt
values — are edited like laser paths: their points are **anchors** with curve types, the chart gets the baked
points.

- **Click** on empty lane space adds an anchor at the snapped beat (value = mouse x) and keeps dragging it. Clicks
  in a row bend like an osu! slider: 2 anchors straight, 3 a perfect curve, 4+ a Bezier; a click between existing
  anchors only curves its own clicks. While adding, a click (without moving) on an anchor of that curve changes its
  type; **right click on empty space finishes**. A thin line previews the next segment.
- Hovering a lane shows the anchor types (white straight, yellow perfect curve, red Bezier, small grey inherit,
  black dot = auto) and the control polygon of a curve; the selected anchor has a pink frame.
- **Drag an anchor:** time + value (between its neighbours). **Shift:** instant jump (on an anchor: drag its
  target; on empty space: a jump from the current value) — a jump breaks the curve like a slam. **Ctrl+click on a
  curve** inserts an anchor. **Alt-drag** inside a plain straight two-anchor segment still shapes its quick kson
  ease curve.
- **Right click on an anchor:** Inherit / Straight / Perfect curve / Bezier / Delete anchor. **Right click on a
  curve:** the type of that segment. Auto tilt modes keep their own tools (right click deletes).
- **Spins / half spins / swings** are bars in the spin lane, like holds: a click on the start line or the bar selects
  it, the bar drags it (it snaps to a slam within 1/8), **its end drags its length** (hovering the end shows the
  hold-tail resize hint; at least 1/16), right click deletes, the inspector edits direction / length / swing values;
  each change is one undo step.
- The lane under the mouse (or being dragged in, or holding the selection) is outlined pink.
- The inspector edits the selected anchor (value, jump target, ease a/b of a straight pair). Every edit is one undo
  step; the preview follows the curve live.
- Saved losslessly in kson (`impl["sdvx-editor"].camera_paths`); other tools see the baked graph points (one exact
  kson curve `[a, b]` for a 3-anchor Bezier, otherwise samples on the 5-pulse VOX grid within the lane's bound:
  0.5 units for zoom_* and center_split, 0.1° for rotation_deg, 0.005 for tilt). ksh export writes the curve as
  plain points (ksh2vox ignores ksh camera curves). Old charts: every point is a straight anchor, kson curves kept.

## Tutorial

Help → **Tutorial…** (or the **Tutorial** button in the song select footer) opens an interactive, chaptered tutorial
for complete beginners: you map Fax's "[REDACTED] (Cut Ver.)" (235 BPM, used with the artist's permission) in
about twelve minutes. A card explains each step, a yellow spotlight with an arrow shows where to act, translucent
"ghost" notes show what to place; a step ends by itself once it is done. Chapters: 0 Song Setup & Timing (F6),
1 Chips/Holds/FX (hold modifier, resizing holds), 2 Lasers (anchor types), 3 Navigating (modding timestamps),
4 Camera & Background, 5 Effects & Sounds, 6 Test & Difficulty, 7 Save/Export/Share (Support the Artist) — each
runnable on its own, with progress ticks (editor.ini). A second tab keeps the first, shorter tutorial on a
placeholder song ("Tutorial (classic)") for now. Each tutorial works in ONE song folder in your editor songs
("Tutorial", "Tutorial (classic)"); starting a chapter rewrites its charts to that chapter's start (asking first
when your own changes would go). Esc keeps working as usual; "Take me back" returns you to where a step needs you.
Details, the timing analysis and how to swap in the mapper's chart: `docs/TUTORIAL.md`.

## Hearing the effects

Effects tab → **play effects during playback** (saved in editor.ini as `PlayEffects`, off by default): the song
plays with the chart's audio effects — FX hold effects (retrigger, gate, flanger, pitch shift, bitcrusher, phaser,
wobble, tape stop, echo, side chain, switch audio, and the filters used as FX) with their definitions, per-hold
parameters and parameter changes, tempo-synced to the chart's BPM; the laser filters (peaking filter by default,
low-pass, high-pass, bitcrusher) following the laser, with the filter switches, filter gain (pfiltergain) and the
peaking filter delay; and the slam volume (chokkakuvol) on the slam hitsound. The playtest (F5) follows your FX
buttons and lasers (autoplay: the chart). **preview FX effect** / **preview laser filter** play the effect picked in
the drop-down on the song from the cursor for 2 s (a filter is swept 0 → 1 → 0). Behaviour follows K-Shoot Mania v2
(details and sources: `docs/ARCHITECTURE.md` §16). Note: the kson presets retrigger, gate, wobble and echo have
`wave_length` 0 by default, which the spec defines as "bypassed" — assign them with a wave length (ksh
`Retrigger;8` does that) or define your own. Off, nothing is attached to the audio.

## Numbers, times and dates

One format everywhere, whatever the Windows locale says (Core `Editing/UiFormat`): numbers with a decimal **dot**
(`327.42`, `x1.00`, `5.0%`), large integers grouped with spaces (`10 000 000`), positions in a song as osu!'s
`mm:ss:mmm` (bottom bar, F6, bookmarks, playtest results, toasts), lengths as `m:ss`, dates as `yyyy-MM-dd HH:mm`.
The process runs with the "en" culture for this (framework Locale setting). Text fields still accept a comma
(`207,5` = `207.5`). Level estimates above the game's highest level read `20+`; an uncertain estimate says
"uncertain" and Song Setup → Difficulty explains why in plain words.

## Options

Ctrl+O, the song select **Options** button or the editor's File → Options… slide the options panel in from the
left (osu!stable style; the type-to-search box filters every row). Sections: **General** (extra songs folder,
launcher config, language), **Graphics** (window mode Windowed / Borderless / Fullscreen — new installs start
Borderless, Alt+Enter toggles Windowed ⇄ Borderless —, frame limiter, FPS display), **Audio** (volumes,
universal offset + the offset dialog, hitsound skin, chord layers, per-event sample mapping), **Input**
(key bindings), **Playtest** (mod speed, judgement windows, knob sensitivity and joystick axes, mouse knobs,
laser key speed, autoplay, hitsounds, assist tick) and **Export** (VOX converter folder, Python, export folder;
the detected ksh2vox version, flagged below 0.16.0). Every change is saved at once (editor.ini / framework.ini);
songs folder and launcher config apply on the next start.

Key bindings: every editor action (grouped like Help → Keyboard Shortcuts) and every playtest control has two
binding slots. Click a slot and press a key (editor: with modifiers, also middle / side mouse buttons and the
wheel; playtest: keys and controller buttons `JoyN`); Esc cancels, Delete/Backspace clears the slot. A key that
another action of the same family already uses is not applied silently: the slot shows the pressed key in orange and the row offers **Swap** (the other
action gets this slot's old key), **Clear other** or **Cancel**. ↺ resets a row, "Reset to default" a group.
Editor and playtest bindings are separate families. Changes apply live. Alt+wheel is context-dependent and not
rebindable; Tab, F3/F4, -/+ and ` are fixed in the playtest. Stored in editor.ini as `EditorBindings` (only the
changed actions, e.g. `tab-timing=F8, open-difficulty=none`) and `PlaytestBindings`.

## Assets

No osu! skin files and no Konami game files are part of this repository.
Fonts: see `docs/FONTS.md`.

## Licence

The editor's code is MIT-licensed (`LICENSE`). Exceptions:

- `src/SDVXEditor.Resources/Tutorial/Main/tutorial.mp3` is Fax's song, used with the
  artist's permission. It is **not** covered by the MIT licence and may not be reused
  outside this editor (`docs/TUTORIAL.md`).
- The bundled Noto fonts are under the SIL Open Font License 1.1 (`OFL.txt` beside
  them, `docs/FONTS.md`).
- `external/SDVX-Chart-Converter` is a separate repository with its own licence.
