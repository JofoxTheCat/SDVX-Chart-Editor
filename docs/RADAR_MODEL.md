# Skill radar and level model — feature and model specification

Status 2026-10-26. Implementation: `src/SDVXEditor.Core/Patterns/` (features, model evaluation),
`src/SDVXEditor.Core/Radar/RadarModel.cs` (radar), `src/SDVXEditor.Core/Difficulty/` (level),
`tools/DifficultyCalibrator/` (fitting). This document is the contract a port (e.g. ksh2vox's Python) has
to reproduce; the C# code is the reference implementation.

Both model files are **fitted on the user's own game install** and contain only aggregate numbers
(regression weights, tree split thresholds on the features below, leaf values) — no chart, no per-song
value. Regenerate both with one command:

```
dotnet run --project tools/DifficultyCalibrator -c Release -- "<game dir>" --core src/SDVXEditor.Core \
    --nautica "<fan chart dir>" [--hands]
```

`--core` writes `Radar/radar_model.json` and `Difficulty/coefficients.json`; `--dry-run` only prints the
validation; `--radar-only` / `--level-only` run one half (≈ 35 s / 2 min on 16 cores).

---

## 1. Inputs

The models work on the editor's `Chart` (any source: ksh, kson, VOX). Everything is computed from

- BT lanes A–D (lane 0–3) and FX L/R (lane 4/5): notes with start pulse `y` and length (0 = chip);
- laser sections per side (0 = left knob, 1 = right): start pulse, width 1/2, points `(ry, v, vf?)`
  with values 0..1 (0 = left edge), `vf` = value after a slam on that point;
- BPM changes, stops, scroll-speed graph, chart end pulse;
- camera graphs (zoom top/bottom/side, rotation), manual tilt, spins.

240 pulses per quarter note. A VOX file is read by `VoxReader` (tick = 5 pulses, or
`240 / BEAT RESOLUTION` pulses when the file declares `#BEAT RESOLUTION`; 44 official files use 144–1680),
and its camera rows are read coarsely: `CAM_RotX` / `CAM_Radi` tweens `pos name 2 dur from to …` become
zoom_top / zoom_bottom points `(pos, from·u)`, `(pos+dur, to·u)` with u = 100 / −100, `Tilt` tweens
become manual tilt points, a laser point with field 4 ≠ 0 adds one spin.

## 2. Pattern features (`PatternAnalysis.Analyze`)

### 2.1 Conventions

- **Time**: `t(p)` = seconds of pulse `p` through the BPM map with audio offset 0
  (`TimingMap.PulseToMs / 1000`). Stops do not move time (kson semantics).
- **Length** `D` (feature `dur`): `t(end pulse)` when the chart has an end pulse and
  `t(end) ≥ 0.5 · t(last object end)`, else `t(last object end)`; at least 1 s. Last object end = max over
  note ends and laser section ends.
- **Per second** (`*_ps`): count / D. **Per minute** (`*_pm`): count / D · 60. **Fractions** (`*_frac`):
  as stated per feature.
- **Notes** are sorted by (pulse, lane). A **chord** is the set of notes on the same pulse (bit mask
  over lanes), its time is `t(pulse)`.
- **Windows**: "peak over w" = the maximum, over every event time `t_i`, of the number of events in
  `(t_i − w, t_i]`.
- **Hand model** (`HandModel.Default`): lane → hand `[0, 0, 1, 1, 0, 1]` (BT A, BT B, FX L left; BT C,
  BT D, FX R right); knob side s is hand s. Verified on the official charts: alternatives (FX swapped,
  B/C swapped, all inverted) give worse correlation for ONE-HAND (0.905 / 0.897 / 0.750 vs 0.927) and
  worse CV for HAND-TRIP (5.6 vs 3.0).
- **Judged units**: chips, hold ticks, laser ticks and slams after `Gameplay/TickRules` (USC / RemyWiki).
- Every value is a finite double (NaN/∞ → 0). Constants: jack gap 0.12 s, stream gap 0.15 s, burst gap
  0.07 s, direction hysteresis 0.05, slam-near window 60 pulses (1/16 note).

### 2.2 Feature list (in this order)

| # | name | definition |
|---|---|---|
| 1 | `dur` | D (s) |
| 2–7 | `chips_ps`, `holds_ps`, `bt_chips_ps`, `fx_chips_ps`, `bt_holds_ps`, `fx_holds_ps` | counts / D |
| 8 | `hold_time_frac` | Σ hold durations (s, all lanes) / D |
| 9 | `chain_ps` | judged units / D |
| 10–11 | `hold_ticks_ps`, `laser_ticks_ps` | tick counts / D |
| 12 | `chord_ps` | chords / D |
| 13–14 | `chord2_frac`, `chord3_frac` | chords with ≥ 2 / ≥ 3 notes / chords |
| 15 | `split_chord_frac` | chords with notes of both hands / chords |
| 16 | `btfx_chord_frac` | chords with a BT and an FX note / chords |
| 17–20 | `peak_chords_{0.5,1.25,2.5,5}` | peak chords over w, divided by w |
| 21–22 | `peak_notes_{1.25,2.5}` | peak notes (each note an event, time = start) over w, / w |
| 23 | `peak_units_2.5` | peak judged units over 2.5 s, / 2.5 |
| 24–26 | `chords_1.25_p{90,75,50}` | the windowed chord counts (w = 1.25) at every chord, sorted ascending, value at index ⌊p·(n−1)⌋, / 1.25 |
| 27 | `kde_chords_0.5` | max over chord times x of Σ_k exp(−½((t_k−x)/σ)²) / (σ√2π), σ = 0.5, sum over chords with \|t_k − x\| ≤ 4σ |
| 28–29 | `span_chords_{8,16}` | K / min_i(t_{i+K} − t_i), the min at least 0.05 s; with ≤ K chords: (n−1)/max(0.05, t_last − t_first); 0 for < 2 chords |
| 30–31 | `hand_peak_max`, `hand_peak_min` | per hand: chord times that contain a note of that hand, peak over 1.25 s / 1.25; max and min over the hands |
| 32 | `hand_balance` | min(notes of hand 0, of hand 1) / max(1, max(…)) |
| 33–36 | `jack_ps`, `jack_inner_ps`, `jack_outer_ps`, `jack_fx_ps` | a note whose previous note in the same lane started 0 < gap ≤ 0.12 s before; inner = lanes 1,2, outer = 0,3, fx = 4,5 |
| 37 | `trill_ps` | per hand, over the chords containing that hand: a chord with exactly one note of the hand in lane x, the previous such event in lane y ≠ x and the one before in lane x, both gaps ≤ 0.15 s, counts 1 (a chord with two notes of the hand resets) |
| 38 | `stream_frac` | chords inside runs of ≥ 8 chords with consecutive gaps ≤ 0.15 s / chords |
| 39 | `burst_frac` | chords whose gap to the previous chord is ≤ 0.07 s / chords |
| 40 | `irregular_frac` | over consecutive chord gap pairs (g1, g2), both in (0, 1] s: share with \|log2(max/min) − round(·)\| > 0.1 |
| 41–43 | `offgrid16_frac`, `triplet_frac`, `odd_frac` | notes with pulse mod 60 ≠ 0; of those, pulse mod 40 = 0 (triplet) or ≠ 0 (odd); / notes |
| 44 | `nested_ps` | chips inside a hold of another lane (hold start < t − 1 ms and hold end > t + 1 ms) / D |
| 45–46 | `nested_same_hand_ps`, `nested_fx_hold_ps` | same, the hold is of the chip's hand / is an FX hold |
| 47 | `nested_frac` | nested chips / max(1, chips) |
| 48 | `laser_frac` | length of the union of all section intervals [t(start), t(end)] / D |
| 49 | `laser_both_frac` | length of (union of left) ∩ (union of right) / D |
| 50 | `laser_moving_frac` | union of segment intervals whose value changes (\|v_{i+1} − after_i\| > 1e−6, t_{i+1} > t_i) / D |
| 51 | `laser_wide_frac` | Σ wide section durations / Σ section durations (0 without lasers) |
| 52 | `laser_sections_ps` | sections / D |
| 53 | `laser_travel_ps` | Σ \|v_{i+1} − after_i\| over segments (slams excluded) / D |
| 54 | `laser_dirchg_ps` | per section, the value sequence v_0, (after_0 if slam), v_1, … run through the hysteresis reversal counter (below) / D |
| 55–56 | `slam_ps`, `slam_travel_ps` | slams (points with vf ≠ v) / D; Σ \|vf − v\| / D |
| 57 | `laser_speed_peak` | max over segment end times of the summed segment travel in (t − 1, t] |
| 58 | `slam_peak_2` | peak slams over 2 s, / 2 |
| 59 | `laser_far_frac` | Σ seconds a section is on its far half (left: value > 0.5, right: < 0.5), linear per segment / D |
| 60 | `laser_crossed_frac` | seconds both lasers are active and left value > right value (32 midpoint samples per overlapping pair) / D |
| 61–66 | `onehand_ps`, `onehand_chip_ps`, `onehand_hold_ps`, `onehand_moving_ps`, `onehand_chord_ps`, `onehand_peak_2.5` | notes at a time t where a section of the **other** hand's knob is active (start ≤ t ≤ end, ±1e−6); `moving` = that section moves at t (segment containing t changes value, or a slam within 60 pulses); `chord` = distinct pulses; `peak` = peak over 2.5 s of such notes / 2.5 |
| 67–72 | `handtrip_ps`, `handtrip_chip_ps`, `handtrip_moving_ps`, `handtrip_far_ps`, `handtrip_chord_ps`, `handtrip_peak_2.5` | the same for a section of the note's **own** hand's knob; `far` = that knob is on its far half at t |
| 73 | `knob_both_notes_ps` | notes while both knobs are active / D |
| 74 | `knob_chord_frac` | distinct pulses of one-hand or hand-trip notes / chords |
| 75 | `bpm_changes` | BPM rows (sorted, > 0) that differ from the previous by > 0.01 |
| 76 | `bpm_logratio` | ln(max/min) over the BPM values that are in effect for > 0 s inside [0, D] |
| 77 | `bpm_beatlen_pm` | Σ \|60/b_i − 60/b_{i−1}\| over those changes / D · 60 |
| 78 | `bpm_offmain_frac` | 1 − (seconds at the most used BPM / seconds inside [0, D]) |
| 79–80 | `stops_pm`, `stop_frac` | stops per minute; Σ stop seconds / D |
| 81 | `scroll_changes_pm` | scroll-speed points whose value differs from the previous value-after (start 1), plus points with a jump, per minute |
| 82–84 | `cam_travel_ps`, `cam_zoom_ps`, `cam_tilt_ps` | zoom = Σ \|Δ\| over zoom top/bottom/side (÷100) and rotation (÷90), incl. jumps; tilt = Σ \|Δ\| of consecutive manual tilt values (auto tilt resets); travel = zoom + tilt; all / D |
| 85 | `spins_pm` | spins + half spins + swings per minute |

**Hysteresis reversal counter** (used for #54): `dir = 0, anchor = ext = s_0`; for each next x: if
`dir = 0` and \|x − anchor\| ≥ 0.05 → `dir = sign(x − anchor)`, `ext = x`; if `dir > 0`: x > ext → ext = x,
else if ext − x ≥ 0.05 → count, dir = −1, ext = x; mirror for `dir < 0`.

Values that depend on VOX-only data (none) or on the camera (82–85) are deliberately **not used by the radar
model** (see §4.4); the level model uses them.

## 3. Model format

### 3.1 Additive model (`Patterns/TreeModel.cs`, used by every radar axis)

```json
{ "features": ["dur", "chips_ps", …],   // names the trees index into
  "intercept": 1.23,
  "linear": [ {"feature": "chips_ps", "transform": "id", "weight": 16.7}, … ],
  "trees": [ {"f":[3,-1,…], "t":[0.42,0,…], "l":[1,0,…], "r":[2,0,…], "v":[0,0.13,…]}, … ],
  "min": 0, "max": 200, "form": "linear+trees" }
```

Evaluation, in this order:

1. `y = intercept`.
2. For each linear term: `y += weight · T(x[feature])`, `T` = identity or `log1p` = ln(1 + max(0, x)).
   A missing feature contributes nothing.
3. Build `x` over `features` (missing = 0). For each tree: start at node 0; while `f[i] ≥ 0` go to
   `l[i]` if `x[f[i]] ≤ t[i]` else `r[i]`; add `v[i]` of the leaf. Children always point forward.
   The learning rate is already folded into `v`.
4. Clamp to `[min, max]`.
5. Radar only: round half away from zero to an integer.

### 3.2 Radar file (`Radar/radar_model.json`)

`{ schema: 1, generated, generator, training_charts, hand_model, note, metrics: {axis: {cv_mae, cv_r,
holdout_mae, holdout_r, classic_mae, classic_r}}, axes: {notes, peak, tsumami, tricky, hand_trip,
one_hand: additive model} }`. An empty chart (no notes and no lasers) is all zero. Without the file
the editor shows the classic radar (`RadarCalculator`, the radar.py port, unchanged).

### 3.3 Level file (`Difficulty/coefficients.json`, schema 2)

Per design (`slot_free`, `slot_aware`):

1. inputs = `DifficultyFeatures.Extract` (87 strain/density features, see that class) ∪
   `slot_1..slot_4` one-hot (slot-aware only; NOV = baseline) ∪ every pattern feature as `pat.<name>`;
2. `raw = intercept + Σ weight · (T(x) − mean) / sd` over `terms` (standardised ridge) `+ Σ trees`
   over `tree_features` (schema 2; absent = ridge only, schema 1 files still load);
3. `level = isotonic(raw)`: piecewise-linear through `(isotonic_x, isotonic_y)`, slope 1 beyond both
   ends; clamp to `[min_level, max_level]`; round to one decimal;
4. band = `half_width` of the last `band` bucket with `from ≤ level` (80th percentile of out-of-fold
   \|residual\|), ×1.5 when a low-confidence reason applies.

## 4. What the axes measure (findings on the official charts)

Correlation analysis over 8,573 charts with a music_db radar (8,602 VOX files; v10 5,556, v12 2,681,
v13 336). Community descriptions (RemyWiki / BEMANIWiki) were the starting hypotheses; the data
confirmed or replaced them:

- **NOTES = volume per second, holds counted by length.** `NOTES ≈ −0.55 + 16.72 · chips/s
  + 16.36 · hold seconds / D` — a hold counts like one chip per second it lasts. Linear alone: CV MAE
  0.10, r 1.000; that is the shipped form (no trees). radar.py's (16.663·notes/s + 38.798)/1.5 has the
  right slope for chips but treats holds as one note and adds an offset → MAE 13.
- **PEAK = local chord density over ~1.25 s**, with chords (simultaneous notes) counting once:
  `peak_chords_1.25` alone r 0.990; a 1.0 s / 2 s window or counting notes is clearly worse. Linear on
  `peak_chords_1.25` + `peak_notes_2.5`, trees on the rest (chord size, nesting): MAE 2.0.
- **TSUMAMI = time with a knob in hand**, then movement: `laser_frac` alone r 0.92; travel, slams,
  both-knob time and direction changes take it to r 0.987.
- **ONE-HAND = buttons of one hand while the other hand turns its knob**, and the knob has to be
  *moving* (or slam within a 1/16) — `onehand_moving_ps` is the strongest single input. Static knob time
  counts much less.
- **HAND-TRIP = buttons on the knob hand's own side while that knob moves** (the other hand has to
  cross over). Same "moving" rule; `handtrip_moving_ps` carries 78 % of the tree gain.
- **TRICKY = gimmicks**: BPM changes (log ratio, beat-length changes, time off the main BPM), camera
  work, wide lasers, inner jacks, slam density, both knobs with notes. It also depends on the era:
  for songs of the first two game versions TRICKY is 0 on 79–94 % of NOV–EXH charts while later
  charts of the same content are not, so part of it is not recoverable from the chart.

### 4.4 Camera features are excluded from the radar

On official charts camera work grows with difficulty (median `cam_zoom_ps` 0 on NOV, 0.085 on MXM), so
trees learn "camera ⇒ hard chart". Fan charts use the camera at any difficulty: with camera features a
fan NOV came out TRICKY 67, above the same song's MXM (64). Excluding `cam_*` costs TRICKY CV MAE
8.5 → 10.7 (r 0.857 → 0.825) and gives ordered, plausible fan values (NOV 5–29, MXM 40–115).
`--radar-exclude ""` restores them. The level model keeps them: on the fan charts it is better with
them (slot-aware MAE 0.88 vs 0.93).

## 5. Results (2026-10-26, `--seed 1`, 5-fold CV grouped by song)

### 5.1 Radar (music_db scale 0..200; classic = radar.py port on the same charts)

| axis | classic MAE / r | new CV MAE / r | hold-out (song id ≥ 2100, n 1,152) MAE / r | VOX v10 / v12 / v13 MAE | form |
|---|---|---|---|---|---|
| NOTES | 13.4 / 0.996 | **0.10 / 1.000** | 0.14 / 1.000 | 0.09 / 0.12 / 0.23 | linear |
| PEAK | 28.4 / 0.920 | **2.00 / 0.996** | 2.21 / 0.994 | 1.97 / 1.99 / 2.63 | linear+trees |
| TSUMAMI | 27.7 / 0.434 | **2.34 / 0.987** | 2.14 / 0.985 | 2.47 / 2.11 / 2.04 | linear+trees |
| TRICKY | 24.4 / 0.567 | **10.7 / 0.825** | 12.6 / 0.802 | 10.1 / 11.6 / 13.1 | trees |
| HAND-TRIP | 42.6 / 0.787 | **2.81 / 0.972** | 2.91 / 0.982 | 2.81 / 2.78 / 3.03 | trees |
| ONE-HAND | 10.5 / 0.935 | **3.22 / 0.989** | 3.22 / 0.992 | 3.27 / 3.06 / 3.61 | linear+trees |

Targets r ≥ 0.9 and MAE well below classic: met on five axes; **TRICKY resists** (r 0.825; 0.857
with camera features, see §4.4). Errors grow with the slot (MXM/INF have the largest values).

Fan charts (24 .ksh + 1 .kson twin, no official radar): the ksh/kson twin of ABYSS MOTION differs by ≤ 2
on every axis; all values stay within 0..200; NOTES, PEAK, TRICKY and ONE-HAND rise NOV → MXM within
every song (NOV TRICKY 5–29, MXM 40–115). Exception worth knowing: Jumpy Star NOV has TSUMAMI 55 > its
ADV's 47 — that NOV is mostly long, wide knob sections, which is what the axis measures. Correlation with
the classic radar on these charts: NOTES 1.00, PEAK 0.96, TSUMAMI 0.71, TRICKY 0.79, HAND-TRIP 0.74,
ONE-HAND 0.90.

### 5.2 Level

| model | CV MAE slot-aware / slot-free | hold-out slot-aware / slot-free | fan charts slot-aware / slot-free |
|---|---|---|---|
| previous file (ridge + isotonic) | 0.501 / 0.578 (old run) | — | 0.97 / 0.91 |
| ridge + isotonic, refitted | 0.490 / 0.557 | 0.468 / 0.530 | 1.00 / 0.98 |
| ridge + pattern trees + isotonic | **0.423 / 0.468** | **0.385 / 0.433** | **0.88** / 1.00 |

Written: **slot-aware = ridge + pattern trees + isotonic** (better on CV, hold-out and fan charts),
**slot-free = ridge + isotonic refitted** (trees better on CV but not on the 24 fan charts: 1.00 vs
0.98 / 0.91). Rule: per design, trees only when CV is better *and* the fan-chart MAE is not worse than
both the refitted ridge model and the file being replaced.

Slot-aware per slot (CV): NOV 0.386, ADV 0.532 (was 0.619), EXH 0.418, INF 0.419, MXM 0.313.
Bias by official level, slot-aware: level 8 +0.29 (was +0.45), 9 +0.32 (+0.40), 20 −0.31 (−0.36),
21 −0.61 (−0.64, n = 18); level 1 +1.77 (n = 25, joke/placeholder charts). The top end is still
compressed — trees cannot extrapolate and the ridge base under-shoots there.

## 6. Notes for a port

- Feature order and names are part of the spec; models index trees by position in `features`.
- Thresholds were placed between adjacent distinct training values and rounded to 6 significant digits
  when that keeps them strictly between, so ordinary float differences in a port only flip a split
  for values that coincide with a training value to ~6 digits.
- `PatternChart` uses the chart's own laser points; ksh slams must be reconstructed the way
  `KshReader` does (1/32 rule with the dense-movement exception) for ksh input to match.
- The calibrator is deterministic (fixed seeds, 64-bin histograms, row/column sampling from
  `System.Random(seed)`); a port does not need to train, only to evaluate.
