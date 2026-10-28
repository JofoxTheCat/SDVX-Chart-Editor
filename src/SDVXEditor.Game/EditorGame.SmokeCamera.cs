using System.Security.Cryptography;
using osu.Framework.Screens;
using osu.Framework.Testing;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Formats.Kson;
using SDVXEditor.Core.Formats.Vox;
using SDVXEditor.Game.Configuration;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Screens.Edit.Camera;
using SDVXEditor.Game.Screens.Edit.Components;
using SDVXEditor.Game.Screens.Edit.Compose;
using SDVXEditor.Game.Screens.Edit.Input;

namespace SDVXEditor.Game;

/// <summary>
/// Smoke-test part for the camera and effects tabs (with <c>--open</c>): on its own copy of the song,
/// adds camera points (incl. a curve and a slam), tilt, a spin, a background, VOX extras, an FX effect
/// assignment and a custom chip sound (a wav generated here), checks undo/redo, playback of the chip
/// sound, saves, reloads and compares.
/// </summary>
public partial class EditorGame
{
    private List<(Action Act, string? Shot)> cameraEffectsSteps(string songFolder, int? slot, string? shots, double interval)
    {
        string work = Path.Combine(smokeWorkRoot(shots),
            "smoke-song-camera", Path.GetFileName(songFolder.TrimEnd('\\', '/')));
        string wavSource = Path.Combine(Path.GetDirectoryName(work)!, "smoke-chip.wav");

        EditorScreen ed() => screenStack.CurrentScreen as EditorScreen ?? throw new InvalidOperationException("smoke test: editor not open");

        string? originalHash = null, originalPath = null;
        string before = "", afterCamera = "", afterEffects = "";
        long slamY = 0, beat = 240, holdY = 0, chipY = 0, stretchY = 0, anchorY = 0;
        int slamDir = 1, cameraSteps = 0, effectSteps = 0, keySoundsBefore = 0;
        string chipSound = "";
        string? beforeVoxVersion = null, voxConverterDir = null, previousConverterDir = null;
        Note? hold = null, chip = null;

        CamPointer ptr(long pulse, CamLane lane, double value) =>
            new CamPointer(ed().Session.PulseToMs(pulse), lane, CameraLaneEditor.FxOf(lane, value));

        int undoCount() => ed().Session.Commands.History.Count();

        var steps = new List<(Action, string?)>
        {
            (() =>
            {
                if (Directory.Exists(work)) Directory.Delete(work, true);
                Directory.CreateDirectory(work);
                foreach (string f in Directory.EnumerateFiles(songFolder))
                    File.Copy(f, Path.Combine(work, Path.GetFileName(f)));
                writeTestWav(wavSource);

                var session = EditorSession.Load(work, slot);
                originalPath = session.ChartPath!;
                originalHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(originalPath)));
                var c = session.Chart;

                // the slam nearest to the middle of the chart that has no spin/swing of the chart's own near it (the spin
                // placed below must snap onto it, not grab an existing pattern); without one, add a laser with a slam after the end
                long mid = c.LastEventPulse() / 2;
                var patternYs = CameraEvaluator.Patterns(c.Camera).Select(x => x.Pattern.Y).ToList();
                var slams = c.Notes.Lasers.SelectMany(l => l).SelectMany(s => s.Points.Where(p => p.IsSlam).Select(p => (Y: s.Y + p.Ry, Dir: p.Vf > p.V ? 1 : -1)))
                             .Where(x => !patternYs.Any(py => Math.Abs(py - x.Y) < 960)).OrderBy(x => Math.Abs(x.Y - mid)).ToList();
                if (slams.Count > 0)
                    (slamY, slamDir) = slams[0];
                else
                {
                    slamY = session.Timing.MeasureStart(session.Timing.MeasureAt(c.LastEventPulse()) + 2);
                    session.Commands.Execute(new AddLaserSectionCommand(0, new LaserSection(slamY - 480, new[] { new LaserPoint(0, 0), new LaserPoint(480, 0, 1), new LaserPoint(960, 1) })));
                    slamDir = 1;
                    session.Commands.MarkSaved();
                }

                // an empty stretch of the camera lanes for the clicks below (charts often carry camera work): no zoom_bottom,
                // zoom_top or tilt point from a beat before it to a beat after its fourth beat, so every click lands on empty
                // lane space and the Alt-drag segment runs between the test's own two points (an existing point between
                // them, or under the Alt-click, made the drag move that point instead). It starts on a measure start - a
                // grid line for every divisor - as close as possible before the slam; else after all events and camera points.
                var camYs = c.Camera.Body.ZoomBottom.Select(p => p.Y).Concat(c.Camera.Body.ZoomTop.Select(p => p.Y)).Concat(c.Camera.Tilt.Select(t => t.Y)).ToList();
                int slamMeasure = session.Timing.MeasureAt(slamY);
                int? stretchMeasure = null;
                for (int d = 0; d < 400 && stretchMeasure == null; d++)
                {
                    foreach (int m in new[] { slamMeasure - 1 - d, slamMeasure - 1 + d })
                    {
                        if (m < 1) continue;
                        long y = session.Timing.MeasureStart(m), b = session.Timing.BeatLength(m);
                        if (camYs.Any(cy => cy >= y - b && cy <= y + 5 * b)) continue;
                        stretchMeasure = m;
                        break;
                    }
                }

                int sm = stretchMeasure ?? session.Timing.MeasureAt(Math.Max(c.LastEventPulse(), camYs.DefaultIfEmpty(0).Max())) + 2;
                stretchY = session.Timing.MeasureStart(sm);
                beat = session.Timing.BeatLength(sm);

                // a second empty zoom_bottom stretch for the anchor part (EditorGame.SmokeCameraAnchors.cs), at least three
                // measures away from the first; else after everything
                var zbYs = c.Camera.Body.ZoomBottom.Select(p => p.Y).ToList();
                int? anchorMeasure = null;
                for (int d = 0; d < 400 && anchorMeasure == null; d++)
                {
                    foreach (int m in new[] { sm + 3 + d, sm - 3 - d })
                    {
                        if (m < 1) continue;
                        long y = session.Timing.MeasureStart(m), b = session.Timing.BeatLength(m);
                        if (zbYs.Any(cy => cy >= y - b && cy <= y + 5 * b)) continue;
                        anchorMeasure = m;
                        break;
                    }
                }

                anchorY = session.Timing.MeasureStart(anchorMeasure ?? sm + 6);
                before = KsonWriter.Write(c);
                report($"camera/effects: copy in {work}, slam at {slamY} ({(slamDir > 0 ? "right" : "left")}), camera clicks in the empty stretch from {stretchY} (measure {sm + 1}, {camYs.Count} camera points in the chart), anchors from {anchorY}");
                pushAndWait(new EditorScreen(session));
            }, null),
            (() =>
            {
                var e = ed();
                e.SelectTab(EditorTab.Camera);
                e.Playback.CurrentTime.Value = e.Session.PulseToMs(stretchY + 2 * beat);
            }, null),
        };

        steps.AddRange(cameraAnchorInputSteps(ed, () => anchorY, shots)); // EditorGame.SmokeCameraAnchors.cs

        steps.AddRange(new List<(Action, string?)>
        {
            (() =>
            {
                var e = ed();
                var s = e.Session;
                var le = e.CameraContent.LaneEditor;
                int h0 = undoCount();
                before = KsonWriter.Write(s.Chart); // after the anchor part: the undo-all below goes back to here
                e.Playback.CurrentTime.Value = s.PulseToMs(stretchY + 2 * beat);
                var zb = s.Chart.Camera.Body.ZoomBottom;
                GraphPoint? at(long y) => zb.FirstOrDefault(p => p.Y == y);

                // click adds a zoom_bottom point at the snapped pulse, value from x
                long y1 = stretchY, y2 = y1 + 2 * beat, y3 = y2 + beat;
                expect(le.PointerDown(ptr(y1, CamLane.ZoomBottom, 0), PointerButton.Left, false, false), "zoom_bottom click starts a drag");
                le.PointerUp();
                expect(at(y1) is { V: 0 }, $"the click at {y1} added a zoom_bottom point with value 0 there (snapped elsewhere?)");

                // drag it to 120 (one undo step)
                expect(le.PointerDown(ptr(y1, CamLane.ZoomBottom, 0), PointerButton.Left, false, false), "grab the point");
                le.PointerDrag(ptr(y1, CamLane.ZoomBottom, 60));
                le.PointerDrag(ptr(y1, CamLane.ZoomBottom, 120));
                le.PointerUp();
                expect(at(y1) is { V: 120 }, $"dragged to 120 ({at(y1)?.V})");
                e.TriggerAction(EditorAction.Undo);
                expect(at(y1) is { V: 0 }, "undo of the drag");
                e.TriggerAction(EditorAction.Redo);
                expect(at(y1) is { V: 120 }, "redo of the drag");

                // second point, then Alt-drag between them shapes the pair's ease curve (the quick handle for a straight pair)
                le.PointerDown(ptr(y2, CamLane.ZoomBottom, -60), PointerButton.Left, false, false);
                le.PointerUp();
                int ia = zb.IndexOf(at(y1)!);
                expect(ia + 1 < zb.Count && zb[ia + 1].Y == y2 && zb[ia + 1].V == -60, "second zoom_bottom point right after the first");
                int countBefore = zb.Count;
                expect(le.PointerDown(ptr((y1 + y2) / 2, CamLane.ZoomBottom, 0), PointerButton.Left, false, true), "Alt-drag starts on the segment");
                expect(le.SelectedAnchorY == y1 && zb.Count == countBefore, $"Alt-click on the segment selects its start anchor, adds nothing (selected {le.SelectedAnchorY})");
                le.PointerDrag(ptr(y1 + (y2 - y1) / 4, CamLane.ZoomBottom, -40));
                le.PointerUp();
                var a = at(y1);
                expect(a?.Curve is { IsLinear: false } && a.V == 120 && zb.Count == countBefore, $"Alt-drag set a curve on the segment and moved nothing (curve {a?.Curve?.ToString() ?? "none"})");

                // Shift-click: a slam (instant jump) to 200
                le.PointerDown(ptr(y3, CamLane.ZoomBottom, 200), PointerButton.Left, true, false);
                le.PointerUp();
                var slamPoint = zb.SingleOrDefault(p => p.Y == y3) ?? throw new InvalidOperationException($"smoke test: Shift-click at {y3} added no point");
                expect(slamPoint.Vf == 200, "Shift-click made an instant jump to 200");

                // zoom_top and rotation, a manual tilt and an auto mode
                var zt = s.Chart.Camera.Body.ZoomTop;
                long y4 = y1;
                le.PointerDown(ptr(y4, CamLane.ZoomTop, -80), PointerButton.Left, false, false);
                le.PointerUp();
                var tilt = s.Chart.Camera.Tilt;
                long y5 = y1 + beat;
                le.PointerDown(ptr(y5, CamLane.Tilt, 0.5), PointerButton.Left, false, false);
                le.PointerUp();
                expect(tilt.Any(t => t.Y == y5 && !t.Value.IsAuto && Math.Abs(t.Value.Manual - 0.5) < 1e-9), "manual tilt 0.5");
                long y6 = y5 + 2 * beat;
                le.SetTiltMode(TiltMode.Bigger, y6);
                expect(tilt.Any(t => t.Y == y6 && t.Value.Auto == TiltMode.Bigger), "auto tilt bigger");

                // spin on the slam (snaps to its pulse and direction)
                le.PatternKind.Value = CamPatternKind.Spin;
                le.PointerDown(ptr(slamY + 10, CamLane.Patterns, 0), PointerButton.Left, false, false);
                le.PointerUp();
                var spin = s.Chart.Camera.Spin.FirstOrDefault(p => p.Y == slamY);
                expect(spin != null && spin.Direction == slamDir && CameraCommands.HasMatchingSlam(s.Chart, spin), "spin snapped onto the slam");

                // the anchor menu deletes, undo restores (the menu itself is clicked with the mouse in the anchor part)
                var menu = le.MenuAt(ptr(y4, CamLane.ZoomTop, -80));
                expect(menu?.FirstOrDefault(i => i.Label == "Delete anchor") is { Run: not null }, "the anchor menu offers Delete anchor");
                menu!.First(i => i.Label == "Delete anchor").Run!();
                expect(!zt.Any(p => p.Y == y4), "Delete anchor deleted the zoom_top point");
                e.TriggerAction(EditorAction.Undo);
                expect(zt.Any(p => p.Y == y4), "undo restored it");

                cameraSteps = undoCount() - h0;
                afterCamera = KsonWriter.Write(s.Chart);
                var st = new CameraEvaluator(s.Chart).Evaluate(y3);
                expect(st.ZoomBottom == 200, "the evaluator sees the jump");
                e.Playback.CurrentTime.Value = s.PulseToMs(slamY - beat / 2);
                report($"camera: click/drag/undo, curve (Alt), slam (Shift), zoom_top, manual + auto tilt, spin on slam, menu delete - {cameraSteps} undo steps - ok");
            }, null),
            (() => expect(ed().CameraContent.Preview.LastQuadCount > 50 && ed().CameraContent.Lanes.LastQuadCount > 20, "preview and lanes draw"), "30-camera-tab"),
            (() =>
            {
                var e = ed();
                var cam = e.CameraContent;
                int h0 = undoCount();
                cam.View.Value = 1;
                cam.Field("bg1").Commit("cyber");
                cam.Field("bg2").Commit("night");
                cam.Field("layer").Commit("layer.gif");
                cam.Field("layer_ms").Commit("1200");
                cam.Field("movie").Commit("movie.mp4");
                cam.Field("movie_ms").Commit("-250");
                cam.Field("bg_no").Commit("63");
                cam.AddPostEffectAtCursor("RadicalBlur");
                var c = e.Session.Chart;
                expect(c.Bg.LegacyBg.SequenceEqual(new[] { "cyber", "night" }), "background images set");
                expect(c.Bg.Layer is { Filename: "layer.gif", DurationMs: 1200 }, "layer set");
                expect(c.Bg.Movie is { Filename: "movie.mp4", OffsetMs: -250 }, "movie set");
                expect(c.VoxExtras.BgNo == 63 && c.VoxExtras.PostEffects.Count == 1, "bg_no and post effect set");
                cameraSteps += undoCount() - h0;
                afterCamera = KsonWriter.Write(c);
                report("background: bg images, layer, movie, VOX bg_no, post effect - ok");
            }, "31-camera-background"),
            (() =>
            {
                ed().CameraContent.View.Value = 2;
                // the badges started the session's converter detection when the camera tab loaded: wait for its answer
                waitUntil(() => VoxConverter.Current.Value.State is ConverterDetectState.Detected or ConverterDetectState.Failed, "the converter detection", 60_000);
            }, "31b-camera-export"),
            (() =>
            {
                // the badges follow the detected ksh2vox version: below 0.17.0 (or unknown) the VOX column is all ✗
                var d = VoxConverter.Current.Value;
                beforeVoxVersion = d.Version;
                var badges = ed().CameraContent.ChildrenOfType<ExportBadge>().ToList();
                expect(badges.Count > 20, $"export badges in the camera tab ({badges.Count})");
                // only loaded badges: one in a view that was never shown (the effects view's bg_no picker) binds the
                // detection when that view loads, and still reports "?" until then
                foreach (string id in new[] { "zoom_top", "tilt_manual", "bg_no" })
                    expect(badges.Any(b => b.FeatureId == id && b.IsLoaded), $"a loaded {id} badge in the camera tab");

                foreach (var b in badges.Where(b => b.IsLoaded && b.FeatureId is "zoom_top" or "tilt_manual" or "bg_no"))
                {
                    expect(b.ConverterVersion == d.Version, $"badge {b.FeatureId} uses the detected version ({b.ConverterVersion ?? "?"} vs {d.Version ?? "?"})");
                    if (!ExportSupport.MapsToVox(d.Version))
                        expect(b.Shown.Vox == Support.No, $"badge {b.FeatureId}: vox ✗ with ksh2vox {d.Version ?? "?"}");
                }
                expect(ed().CameraContent.ExportMatrix.HeaderText.Contains(ExportMatrixPanel.VoxHeader(d), StringComparison.Ordinal), "the export table names the detected converter");

                // then point the smoke storage at a 0.17.0+ converter (put back with every other setting at the end)
                voxConverterDir = smokeConverterDir();
                if (voxConverterDir == null)
                {
                    report($"camera export badges: ksh2vox {d.Version ?? "?"} ({d.State}); no ksh2vox >= {ExportSupport.VoxMappingVersion} found for the switch check (set {SmokeConverterVariable}) - skipped");
                    return;
                }

                previousConverterDir = config.Get<string>(EditorSetting.VoxConverterDir);
                config.SetValue(EditorSetting.VoxConverterDir, voxConverterDir);
                waitUntil(() => VoxConverter.Current.Value is { State: ConverterDetectState.Detected } c && ExportSupport.MapsToVox(c.Version),
                    $"the converter in {voxConverterDir} to be detected as >= {ExportSupport.VoxMappingVersion}", 60_000);
            }, null),
            (() =>
            {
                if (voxConverterDir == null) return;
                var d = VoxConverter.Current.Value;
                var cam = ed().CameraContent;
                // loaded badges only (see above: a never-shown view's badges bind the detection when they load)
                var badges = cam.ChildrenOfType<ExportBadge>().Where(b => b.IsLoaded).ToList();
                ExportBadge one(string id) => badges.FirstOrDefault(b => b.FeatureId == id) ?? throw new InvalidOperationException($"smoke test: no {id} badge");
                expect(badges.All(b => b.ConverterVersion == d.Version), $"every badge switched to ksh2vox {d.Version}");
                expect(one("zoom_top").Shown.Vox == Support.Partial && one("zoom_top").VoxTooltip.Contains("±100 clamped"), $"zoom_top: vox ~ with the clamp tooltip ({one("zoom_top").VoxTooltip})");
                expect(one("tilt_manual").Shown.Vox == Support.Partial, "tilt (manual): vox ~");
                // from 0.20.0 post effects are "~": they depend on a background with its own effect stages
                var postVox = ExportSupport.MapsEffectsToVox(d.Version) ? Support.Partial : Support.Yes;
                expect(one("bg_no").Shown.Vox == Support.Yes && one("post_effect").Shown.Vox == postVox,
                    $"bg_no: vox ✓, post effects: vox {postVox} (ksh2vox {d.Version})");
                expect(one("zoom_side").Shown.Vox == Support.No, "zoom_side stays vox ✗");
                expect(cam.ExportMatrix.HeaderText.Contains($"VOX via ksh2vox {d.Version}", StringComparison.Ordinal), $"the export table header names ksh2vox {d.Version} ({cam.ExportMatrix.HeaderText})");
                string change = ExportSupport.MapsToVox(beforeVoxVersion) ? "already mapped before" : "zoom_top ✗ -> ~, bg_no ✗ -> ✓";
                report($"camera export badges: ksh2vox {beforeVoxVersion ?? "?"} -> {d.Version} ({voxConverterDir}): {change}, header \"VOX via ksh2vox {d.Version}\" - ok");

                // put the setting back here, on the update thread: the snapshot restore runs while the game is disposed,
                // and the options' text box bound to this setting must not be changed from that thread
                config.SetValue(EditorSetting.VoxConverterDir, previousConverterDir ?? string.Empty);
            }, "31c-camera-export-mapped"),
            (() =>
            {
                var e = ed();
                var s = e.Session;
                // undo everything done in the camera tab, then redo it
                for (int i = 0; i < cameraSteps; i++) e.TriggerAction(EditorAction.Undo);
                expect(KsonWriter.Write(s.Chart) == before, "undoing every camera/background step restores the chart");
                for (int i = 0; i < cameraSteps; i++) e.TriggerAction(EditorAction.Redo);
                expect(KsonWriter.Write(s.Chart) == afterCamera, "redo brings every camera/background edit back");
                e.CameraContent.View.Value = 0;
                report($"camera undo/redo: {cameraSteps} steps back to the original and forward again - ok");
            }, null),

            // ---- effects tab
            (() =>
            {
                var e = ed();
                var s = e.Session;
                var c = s.Chart;
                e.SelectTab(EditorTab.Effects);

                hold = c.Notes.Fx.SelectMany(l => l).Where(n => n.IsHold).OrderBy(n => Math.Abs(n.Y - slamY)).FirstOrDefault();
                chip = c.Notes.Fx.SelectMany(l => l).Where(n => !n.IsHold).OrderBy(n => Math.Abs(n.Y - slamY)).FirstOrDefault();
                long end = s.Timing.MeasureStart(s.Timing.MeasureAt(c.LastEventPulse()) + 3);
                var add = new List<NoteRef>();
                if (hold == null) add.Add(new NoteRef(NoteKind.Fx, 0, hold = new Note(end, 2 * beat)));
                if (chip == null) add.Add(new NoteRef(NoteKind.Fx, 1, chip = new Note(end + 3 * beat)));
                if (add.Count > 0) s.Commands.Execute(new AddNotesCommand(add, "smoke FX notes"));
                holdY = hold.Y;
                chipY = chip.Y;
                e.Playback.LengthMs = Math.Max(e.Playback.LengthMs, s.PulseToMs(Math.Max(holdY, chipY) + 8 * beat));
                e.Playback.CurrentTime.Value = s.PulseToMs(holdY - beat);
            }, null),
            (() =>
            {
                var e = ed();
                var s = e.Session;
                var fx = e.EffectsContent;
                int h0 = undoCount();
                before = KsonWriter.Write(s.Chart);

                int lane(Note n) => s.Chart.Notes.Fx[0].Contains(n) ? 0 : 1;
                s.Selection.Clear();
                s.Selection.Add(new NoteRef(NoteKind.Fx, lane(hold!), hold!));
                fx.AddDef("retrigger", "smoke_rt");
                fx.SetDefParam("rate", "80%");
                expect(fx.AssignToSelectedHolds("smoke_rt") == 1 && hold!.Effects.Single().Name == "smoke_rt", "effect assigned to the FX hold");
                fx.RenameDef("smoke_rt8");
                expect(hold!.Effects.Single().Name == "smoke_rt8", "rename follows into the hold");

                chipSound = fx.ImportSound(wavSource) ?? throw new InvalidOperationException("smoke test: import failed");
                expect(File.Exists(Path.Combine(work, chipSound)), "the wav was copied into the song folder");
                s.Selection.Clear();
                s.Selection.Add(new NoteRef(NoteKind.Fx, lane(chip!), chip!));
                fx.Field("sound_volume").Commit("80");
                expect(fx.AssignSoundToSelectedChips() == 1 && chip!.KeySound == new KeySoundRef(chipSound, 0.8), "chip sound assigned at 80 %");

                keySoundsBefore = e.Hitsounds.KeySoundsPlayed;
                expect(fx.PreviewSound(chipSound), "preview plays the imported sound");
                expect(fx.PreviewSound("clap"), "preview plays a built-in");

                fx.SetFilterAtCursor("low_pass_filter");
                fx.Field("filter_gain").Commit("70");
                fx.Field("slam_volume").Commit("40");
                expect(s.Chart.Audio.LaserEffects.PulseEvents.ContainsKey("low_pass_filter"), "laser filter switch set");

                effectSteps = undoCount() - h0;
                afterEffects = KsonWriter.Write(s.Chart);
                for (int i = 0; i < effectSteps; i++) e.TriggerAction(EditorAction.Undo);
                expect(KsonWriter.Write(s.Chart) == before, "undoing every effects step restores the chart");
                for (int i = 0; i < effectSteps; i++) e.TriggerAction(EditorAction.Redo);
                expect(KsonWriter.Write(s.Chart) == afterEffects, "redo brings every effects edit back");
                s.Selection.Add(new NoteRef(NoteKind.Fx, lane(hold!), hold!));
                fx.SelectDef("smoke_rt8");
                report($"effects: def + param + assign + rename, chip sound import ({chipSound}) + assign + preview, laser filter/gain/slam volume, {effectSteps} undo steps back and forward - ok");
            }, "32-effects-tab"),
            (() =>
            {
                // the chip sound plays when the chip passes the judgement line
                var e = ed();
                keySoundsBefore = e.Hitsounds.KeySoundsPlayed;
                e.Hitsounds.Enabled.Value = true;
                e.Playback.CurrentTime.Value = e.Session.PulseToMs(chipY) - 400;
                e.Playback.IsPlaying.Value = true;
                waitUntil(() => e.Hitsounds.KeySoundsPlayed > keySoundsBefore, "the custom chip sound to play during playback", 5000);
            }, null),
            (() =>
            {
                var e = ed();
                e.Playback.IsPlaying.Value = false;
                expect(e.Hitsounds.KeySoundsPlayed > keySoundsBefore, "the custom chip sound played during playback");

                e.TriggerAction(EditorAction.Save);
                expect(!e.Session.IsDirty, "saved");
                string saved = e.Session.ChartPath!;
                expect(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(originalPath!))) == originalHash || saved == originalPath, "the original chart file is untouched");

                var again = SongFolder.Load(work).Charts.Single(c => string.Equals(c.FilePath, saved, StringComparison.OrdinalIgnoreCase)).Chart;
                expect(KsonWriter.Write(again) == KsonWriter.Write(e.Session.Chart), "reloaded chart equals the edited chart (kson text)");
                expect(again.Camera.Body.ZoomBottom.Any(p => p.Vf == 200) && again.Camera.Body.ZoomBottom.Any(p => p.Curve is { IsLinear: false }), "slam and curve survived");
                expect(again.Camera.Spin.Any(p => p.Y == slamY), "spin survived");
                expect(CameraPaths.Runs(again.Camera, CamPathLane.ZoomBottom).FirstOrDefault() is { Curved: true } zr
                       && zr.Stored!.Path.Anchors.Any(x => zr.Y + x.Ry == anchorY && x.Type == LaserCurveType.Bezier), "the zoom_bottom anchors (Bezier) survived save + reload");
                expect(again.Bg.Layer?.Filename == "layer.gif" && again.VoxExtras.BgNo == 63 && again.VoxExtras.PostEffects.Count == 1, "background and VOX extras survived");
                expect(again.Notes.Fx.SelectMany(l => l).Any(n => n.Y == holdY && n.Effects.Any(x => x.Name == "smoke_rt8")), "hold effect survived");
                expect(again.Notes.Fx.SelectMany(l => l).Any(n => n.Y == chipY && n.KeySound?.Name == chipSound), "chip sound survived");
                expect(again.Audio.FxEffects.Defs.Any(d => d.Name == "smoke_rt8" && d.Params["rate"] == "80%"), "definition survived");
                report($"camera/effects save: {Path.GetFileName(saved)} reloaded identical, key sounds played {e.Hitsounds.KeySoundsPlayed} - ok");

                string file = Path.Combine(Path.GetDirectoryName(work)!, "smoke-report.txt");
                File.WriteAllText(file, smokeReport.ToString());
            }, null),
            (() => ed().Exit(), null),
        });
        return steps;
    }

    /// <summary>Environment variable: a ksh2vox folder (0.17.0+) for the camera part's export badge switch check.</summary>
    public const string SmokeConverterVariable = "SDVXEDITOR_SMOKE_CONVERTER";

    /// <summary>
    /// The converter for the badge switch check: <see cref="SmokeConverterVariable"/>, else a <c>SDVX-Chart-Converter</c>
    /// checkout beside the editor's (found upwards from the app); null when none of them is 0.17.0 or newer.
    /// </summary>
    private static string? smokeConverterDir()
    {
        string? dir = Environment.GetEnvironmentVariable(SmokeConverterVariable) is { Length: > 0 } env
            ? env
            : PythonLocator.FindUpwards(AppContext.BaseDirectory, "SDVX-Chart-Converter/ksh2vox.py") is { } hit ? Path.GetDirectoryName(hit) : null;
        if (dir == null || !File.Exists(Path.Combine(dir, "ksh2vox.py"))) return null;

        // a cheap look at __version__ so an old checkout is skipped instead of failing the run
        var m = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(Path.Combine(dir, "ksh2vox.py")), "^__version__\\s*=\\s*\"([^\"]+)\"", System.Text.RegularExpressions.RegexOptions.Multiline);
        return m.Success && ExportSupport.MapsToVox(m.Groups[1].Value) ? Path.GetFullPath(dir) : null;
    }

    /// <summary>A 90 ms 16-bit mono test click (two sine partials), own work.</summary>
    private static void writeTestWav(string path)
    {
        const int rate = 44100;
        int n = rate * 90 / 1000;
        using var w = new BinaryWriter(File.Create(path));
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + n * 2);
        w.Write("WAVEfmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(rate);
        w.Write(rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8.ToArray());
        w.Write(n * 2);
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / rate;
            double v = Math.Exp(-t * 45) * (0.6 * Math.Sin(2 * Math.PI * 880 * t) + 0.3 * Math.Sin(2 * Math.PI * 1320 * t));
            w.Write((short)(v * short.MaxValue * 0.8));
        }
    }
}
