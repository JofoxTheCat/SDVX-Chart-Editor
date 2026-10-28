using System.Diagnostics;
using System.Globalization;
using osu.Framework.Graphics;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osuTK;
using osuTK.Input;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Game.Graphics.UserInterface;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Screens.Edit.Camera;
using SDVXEditor.Game.Screens.Edit.Components;
using SDVXEditor.Game.Screens.Edit.Compose;
using SDVXEditor.Game.Screens.Edit.Input;
using SDVXEditor.Game.Tutorial;

namespace SDVXEditor.Game;

/// <summary>
/// Smoke part <c>panels</c> (needs <c>--open</c>; 2026-10-27 tutorial feedback), REAL input on a copy of the song:
/// <list type="bullet">
/// <item>a lone slam dragged against a laser's start stops a 1/96 gap short (two sections of one side never touch);
/// a lone slam drawn right after a laser's end is refused;</item>
/// <item>effects tab: "select FX in view" selects what the playfield shows, says so and flashes; with nothing in view
/// it says so; every button has a tooltip; the export table is a collapsed section that opens;</item>
/// <item>camera tab: a spin is a bar — a click on its body selects it, its end drags its length (with the hold-tail
/// hint), the body moves it, right click deletes, Ctrl+Z restores; the hovered lane is highlighted
/// (<see cref="CameraLanes.ColumnFor"/>);</item>
/// <item>background view: preview of a KSM preset, a song-folder image and a movie still; bg_no described; the export
/// lines name the detected converter.</item>
/// </list>
/// Everything is undone; the copy is closed without saving.
/// </summary>
public partial class EditorGame
{
    private List<(Action Act, string? Shot)> panelSteps(string songFolder, int? slot, string? shots)
    {
        var im = SmokeInput ?? throw new InvalidOperationException("smoke test: no smoke input manager");
        string inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
        string work = Path.Combine(smokeWorkRoot(shots), "smoke-song-panels", Path.GetFileName(songFolder.TrimEnd('\\', '/')));
        long p = 0, b = 240;
        int undoBefore = 0;
        string? shot(string name) => shots != null ? "70-panels-" + name : null;

        EditorScreen ed() => screenStack.CurrentScreen as EditorScreen ?? throw new InvalidOperationException("smoke test: editor not open");
        Chart chart() => ed().Session.Chart;
        Vector2 compose(long pulse, double v) => ed().Playfield.ToScreenSpace(new Vector2(PlayfieldLayout.LaserX(v, false), ed().Playfield.YOfPulse(pulse)));
        CameraTabContent cam() => ed().CameraContent;
        CameraLanes lanes() => cam().Lanes;
        Vector2 patternAt(CamPatternKind kind, long pulse) =>
            lanes().ToScreenSpace(new Vector2(CameraLanes.LaneLeft(CamLane.Patterns) + CameraLaneEditor.PatternColumnLeft(kind) + CameraLaneEditor.PATTERN_BAR_WIDTH / 2, lanes().YOfPulse(pulse)));
        Vector2 laneAt(CamLane lane, long pulse) =>
            lanes().ToScreenSpace(new Vector2(CameraLanes.LaneLeft(lane) + CameraLaneEditor.LANE_WIDTH / 2, lanes().YOfPulse(pulse)));

        (Action, double)[] move(Func<Vector2> to) => new (Action, double)[] { (() => im.MoveMouseTo(to()), 80) };
        (Action, double)[] click(Func<Vector2> to, MouseButton button = MouseButton.Left) => new (Action, double)[]
        {
            (() => im.MoveMouseTo(to()), 50), (() => im.PressButton(button), 50), (() => im.ReleaseButton(button), 80),
        };
        (Action, double)[] drag(Func<Vector2> from, Func<Vector2> to) => new (Action, double)[]
        {
            (() => im.MoveMouseTo(from()), 50), (() => im.PressButton(MouseButton.Left), 50),
            (() => im.MoveMouseTo(Vector2.Lerp(from(), to(), 0.3f)), 50), (() => im.MoveMouseTo(Vector2.Lerp(from(), to(), 0.7f)), 50),
            (() => im.MoveMouseTo(Vector2.Lerp(from(), to(), 0.95f)), 50), (() => im.MoveMouseTo(to()), 50),
            (() => im.ReleaseButton(MouseButton.Left), 80),
        };
        (Action, double)[] key(Key k, bool down) => new (Action, double)[] { (() => { if (down) im.PressKey(k); else im.ReleaseKey(k); }, 60) };
        (Action, double)[] then(params (Action, double)[][] parts) => parts.SelectMany(a => a).ToArray();
        (Action, double)[] check(Action a) => new (Action, double)[] { (a, 0d) };
        Vector2 centre(Func<Drawable?> d) => (d() ?? throw new InvalidOperationException("smoke test: control not found")).ScreenSpaceDrawQuad.Centre;

        string lasersR() => string.Join(" ", chart().Notes.Lasers[1].Where(s => s.EndY >= p - 2 * b && s.Y <= p + 8 * b)
                                                     .Select(s => inv($"[{(s.Y - p) / (double)b:0.###}b..{(s.EndY - p) / (double)b:0.###}b]")));
        string state() => inv($"laser R {lasersR()}; toast '{ed().LastToast}'; undo '{ed().Session.Commands.UndoDescription}'");

        // the test objects (added by command; the real input acts on them)
        LaserSection? slam = null, diagonal = null;
        Note? fxChip = null, fxHold = null;
        CamPattern? spin = null;

        return new List<(Action, string?)>
        {
            (() =>
            {
                if (Directory.Exists(work)) Directory.Delete(work, true);
                Directory.CreateDirectory(work);
                foreach (string f in Directory.EnumerateFiles(songFolder)) File.Copy(f, Path.Combine(work, Path.GetFileName(f)));
                pushAndWait(new EditorScreen(EditorSession.Load(work, slot)));
            }, null),
            (() =>
            {
                var e = ed();
                var s = e.Session;
                p = s.Timing.MeasureStart(s.Timing.MeasureAt(s.Chart.LastEventPulse()) + 4);
                b = s.Timing.BeatLength(s.Timing.MeasureAt(p));
                e.Playback.LengthMs = s.PulseToMs(p + 64 * b);
                e.SelectTab(EditorTab.Compose);
                e.Compose.GridSnap.Value = true;
                e.Compose.WideLaser.Value = false;
                e.Compose.Divisor.Value = 4;
                s.Commands.MarkSaved();
                undoBefore = s.Commands.History.Count();
                im.UseParentInput = false;

                // the stretch p - 1 .. p + 7 beats on screen
                float judgeY = e.Playfield.DrawHeight - PlayfieldLayout.JUDGEMENT_FROM_BOTTOM;
                e.Playback.CurrentTime.Value = s.PulseToMs(p - b);
                e.Zoom.Value = Math.Clamp((float)Math.Floor((judgeY - 30) / ((s.PulseToMs(p + 7 * b) - s.PulseToMs(p - b)) * PlayfieldLayout.PX_PER_MS) * 20) / 20, e.Zoom.MinValue, 1f);

                // laser R: a lone slam at p, a diagonal from p + 2 beats to p + 4 beats (the screenshot of the bug)
                slam = new LaserSection(p, new[] { new LaserPoint(0, 0.6, 0) });
                diagonal = new LaserSection(p + 2 * b, new[] { new LaserPoint(0, 0.9), new LaserPoint(2 * b, 0.1) });
                s.Commands.Execute(new AddLaserSectionCommand(1, slam));
                s.Commands.Execute(new AddLaserSectionCommand(1, diagonal));
                e.SelectTool(EditorTool.Select);
                s.Selection.Clear();
                expect(e.Playfield.YOfPulse(p + 6 * b) > 0, inv($"panels: the test stretch is on screen ({state()})"));
            }, shot("a-lasers")),

            // 1. drag the lone slam (its start end) up onto the diagonal's start: it stops 1/96 short, with the message
            (() => inputSequence("panels: drag a slam onto a laser's start", then(drag(() => compose(p, 0.6), () => compose(p + 2 * b, 0.6)), check(() =>
            {
                var s = chart().Notes.Lasers[1].FirstOrDefault(x => x.Points.Count == 1 && x.Points[0].IsSlam && x.Y >= p && x.Y < p + 2 * b);
                expect(s != null && s.Y > p + b, inv($"panels: the slam moved towards the laser ({state()})"));
                expect(s != null && diagonal!.Y - s.EndY >= OverlapRule.MinLaserGap,
                    inv($"panels: it stops at least {OverlapRule.MinLaserGap} pulses (1/96) before the laser's start, never on it or a VOX tick before it ({state()})"));
                expect(ed().LastToast.Contains("1/96 gap"), $"panels: the refusal says why ({state()})");
                expect(chart().Notes.Lasers[1].OrderBy(x => x.Y).Zip(chart().Notes.Lasers[1].OrderBy(x => x.Y).Skip(1)).All(z => z.Second.Y - z.First.EndY >= OverlapRule.MinLaserGap),
                    $"panels: no two R sections closer than the gap ({state()})");
            }))), shot("b-slam-stopped")),

            // a lone slam drawn with the laser tool 1/48 beat after the diagonal's end is refused
            (() =>
            {
                ed().SelectTool(EditorTool.Laser);
                ed().Tools.LaserSide.Value = 1;
                ed().Compose.Divisor.Value = 48;
                long at = diagonal!.EndY + b / 48;
                int before = chart().Notes.Lasers[1].Count;
                inputSequence("panels: a slam right after a laser's end", then(drag(() => compose(at, 0.3), () => compose(at, 0.9)), check(() =>
                {
                    expect(chart().Notes.Lasers[1].Count == before, $"panels: the slam {b / 48} pulses after the laser's end is not added ({state()})");
                    expect(ed().LastToast.Contains("1/96 gap"), $"panels: and the message says why ({state()})");
                })));
            }, null),
            (() =>
            {
                ed().Compose.Divisor.Value = 4;
                ed().SelectTool(EditorTool.Select);
            }, null),

            // 4. effects tab: FX notes in view, "select FX in view"
            (() =>
            {
                var e = ed();
                fxChip = new Note(p + b);
                fxHold = new Note(p + 3 * b, b);
                e.Session.Commands.Execute(new AddNotesCommand(new[] { new NoteRef(NoteKind.Fx, 0, fxChip), new NoteRef(NoteKind.Fx, 1, fxHold) }, "smoke FX"));
                e.Session.Selection.Clear();
                e.SelectTab(EditorTab.Effects);
            }, null),
            (() => inputSequence("panels: select FX in view", then(click(() => centre(() => TutorialContext.FindText(ed().EffectsContent, "select FX in view"))), check(() =>
            {
                var fx = ed().EffectsContent;
                var (from, to) = fx.Playfield.VisiblePulses;
                var visible = chart().Notes.Fx.SelectMany(l => l).Where(n => n.EndY >= from && n.Y <= to).ToHashSet();
                var sel = ed().Session.Selection.Notes.Select(n => n.Note).ToHashSet();
                expect(sel.Contains(fxChip!) && sel.Contains(fxHold!) && sel.SetEquals(visible),
                    inv($"panels: it selects exactly the FX notes the playfield shows ({sel.Count} selected, {visible.Count} visible)"));
                expect(ed().LastToast.StartsWith("Selected", StringComparison.Ordinal), $"panels: it says what it selected ('{ed().LastToast}')");
                expect(fx.Playfield.Flashing, "panels: the selection flashes in the playfield");
            }))), shot("c-select-fx")),
            (() =>
            {
                // far beyond the chart: nothing in view
                ed().Playback.CurrentTime.Value = ed().Session.PulseToMs(p + 40 * b);
            }, null),
            (() => inputSequence("panels: select FX with nothing in view", then(click(() => centre(() => TutorialContext.FindText(ed().EffectsContent, "select FX in view"))), check(() =>
            {
                expect(ed().LastToast.Contains("No FX notes in view"), $"panels: with nothing in view it says so ('{ed().LastToast}')");
                expect(ed().Session.Selection.Notes.Count == 2, "panels: and keeps the selection");
                ed().Playback.CurrentTime.Value = ed().Session.PulseToMs(p - b);
            }))), null),
            (() =>
            {
                var fx = ed().EffectsContent;
                var untipped = fx.ChildrenOfType<FlatButton>().Where(f => f.IsPresent && string.IsNullOrEmpty(f.TooltipText.ToString())).Select(f => f.ChildrenOfType<osu.Framework.Graphics.Sprites.SpriteText>().FirstOrDefault()?.Text.ToString()).ToList();
                expect(untipped.Count == 0, $"panels: every effects button has a tooltip (missing: {string.Join(", ", untipped)})");
                expect(!fx.ExportSection.Open, "panels: the export table starts collapsed");
                // audit B1: an unticked checkbox still draws its ring (it used to be invisible: a plain label)
                foreach (var cb in fx.ChildrenOfType<PinkCheckbox>().Where(c => c.IsPresent && !c.Current.Value))
                    expect(cb.Dot.IsPresent && cb.Dot.ChildrenOfType<osu.Framework.Graphics.Shapes.Box>().Any(b => b.IsPresent && b.Alpha > 0),
                        "panels: an unticked checkbox draws its empty ring");
                // coordinator note: a long switch list scrolls inside its own box
                expect(fx.FilterListHolder.DrawHeight <= Screens.Edit.Effects.EffectsTabContent.FILTER_LIST_MAX + 0.5f, "panels: the laser filter switch list is capped and scrolls");
                expect(TutorialContext.FindText(fx, "at cursor") is { } atCursor && atCursor.ScreenSpaceDrawQuad.Centre.X > fx.ExportSection.ScreenSpaceDrawQuad.TopLeft.X - 1,
                    "panels: the only \"at cursor\" button is the laser filter's (rightmost column)");
            }, shot("d-effects-panel")),
            (() =>
            {
                var section = ed().EffectsContent.ExportSection;
                for (Drawable? d = section.Parent; d != null; d = d.Parent)
                {
                    if (d is osu.Framework.Graphics.Containers.BasicScrollContainer scroll)
                    {
                        scroll.ScrollIntoView(section, animated: false);
                        break;
                    }
                }
            }, null),
            (() => inputSequence("panels: open the export table", then(click(() => centre(() => ed().EffectsContent.ExportSection.HeaderRow)), check(() =>
            {
                expect(ed().EffectsContent.ExportSection.Open, "panels: a click on the heading opens the export table");
            }))), shot("e-effects-export")),

            // 3. camera tab: a spin bar
            (() =>
            {
                var e = ed();
                e.SelectTab(EditorTab.Camera);
                cam().View.Value = 0;
                spin = new CamPattern(p + b, 1, b);
                e.Session.Commands.Execute(new AddPatternCommand(CamPatternKind.Spin, spin));
            }, null),
            (() => inputSequence("panels: click a spin's body", then(click(() => patternAt(CamPatternKind.Spin, p + b + b / 2)), check(() =>
            {
                expect(cam().LaneEditor.SelectedPattern is { } sp && sp.Pattern == spin, "panels: a click on the spin's bar selects it, like a note");
                expect(lanes().HighlightedLane == CamLane.Patterns, $"panels: the spin lane is highlighted ({lanes().HighlightedLane})");
            }))), null),
            (() => inputSequence("panels: hover the spin's end", then(move(() => patternAt(CamPatternKind.Spin, p + 2 * b)), check(() =>
            {
                expect(cam().LaneEditor.PatternTailHint is { } h && h.Pattern == spin, "panels: hovering the end shows the resize hint (like a hold's tail)");
            }))), shot("f-spin-end-hover")),
            (() => inputSequence("panels: drag the spin's end", then(drag(() => patternAt(CamPatternKind.Spin, p + 2 * b), () => patternAt(CamPatternKind.Spin, p + 3 * b)), check(() =>
            {
                expect(spin!.Y == p + b && spin.Length == 2 * b, inv($"panels: dragging the end makes the spin two beats long, start kept (y {(spin.Y - p) / (double)b}b, length {spin.Length / (double)b}b)"));
                expect(ed().Session.Commands.UndoDescription.Contains("spin", StringComparison.OrdinalIgnoreCase), $"panels: one undo step '{ed().Session.Commands.UndoDescription}'");
            }))), shot("g-spin-resized")),
            (() => inputSequence("panels: drag the spin's body", then(drag(() => patternAt(CamPatternKind.Spin, p + b + b / 2), () => patternAt(CamPatternKind.Spin, p + 5 * b + b / 2)), check(() =>
            {
                // (no slam near p + 5 beats: a spin snaps to a slam within 1/8)
                expect(spin!.Y == p + 5 * b && spin.Length == 2 * b, inv($"panels: dragging the body moves the spin four beats, length kept (y {(spin.Y - p) / (double)b}b)"));
            }))), null),
            (() => inputSequence("panels: right click deletes the spin", then(click(() => patternAt(CamPatternKind.Spin, p + 6 * b), MouseButton.Right), check(() =>
            {
                expect(!chart().Camera.Spin.Contains(spin!), "panels: right click on the bar deletes the spin");
                ed().TriggerAction(EditorAction.Undo);
                expect(chart().Camera.Spin.Contains(spin!), "panels: Ctrl+Z brings it back");
            }))), null),
            (() => inputSequence("panels: hover the tilt lane", then(move(() => laneAt(CamLane.Tilt, p + 4 * b)), check(() =>
            {
                expect(lanes().HighlightedLane == CamLane.Tilt, $"panels: the hovered tilt lane is highlighted ({lanes().HighlightedLane})");
                var col = lanes().ColumnFor(CamLane.Tilt).ScreenSpaceDrawQuad;
                var pos = laneAt(CamLane.Tilt, p + 4 * b);
                expect(col.Contains(pos), "panels: ColumnFor(tilt) covers the tilt lane");
            }))), shot("h-lane-highlight")),

            // 2. background view
            (() =>
            {
                cam().View.Value = 1;
                cam().Field("bg1").Commit("desert");
                cam().Field("bg_no").Commit("63");
            }, null),
            (() => waitUntil(() => cam().BgPreview.Parts.Contains("preset") && cam().BgPreview.BgNoText.Contains("game background 63"), "panels: background preview of the preset", 5000), shot("i-bg-preset")),
            (() =>
            {
                string? image = Directory.EnumerateFiles(work).FirstOrDefault(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase));
                expect(image != null, "panels: the song folder has an image");
                cam().Field("bg1").Commit(Path.GetFileName(image!));
                // a two-second test movie, if ffmpeg is installed
                string movie = Path.Combine(work, "smoke-movie.mp4");
                try
                {
                    using var ff = Process.Start(new ProcessStartInfo("ffmpeg", $"-y -loglevel error -f lavfi -i testsrc=size=320x180:rate=10 -t 2 -pix_fmt yuv420p \"{movie}\"")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    });
                    ff?.WaitForExit(20_000);
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    report("panels: no ffmpeg, movie preview not tested");
                }

                if (File.Exists(movie)) cam().Field("movie").Commit("smoke-movie.mp4");
            }, null),
            (() => waitUntil(() => cam().BgPreview.Parts.Contains("image") && (!File.Exists(Path.Combine(work, "smoke-movie.mp4")) || cam().BgPreview.Parts.Contains("movie")),
                "panels: background preview of the image and the movie", 8000), null),
            (() => waitUntil(() => !cam().VoxRowsText.Contains("being detected"), "panels: the converter detection", 90_000), null),
            (() =>
            {
                report($"panels: background preview: {cam().BgPreview.InfoText.Replace("\n", " | ")}");
                report($"panels: export lines: {cam().BgNoExportText} / {cam().VoxRowsText}");
                string? version = VoxConverter.Current.Value.Version;
                expect(version == null ? cam().VoxRowsText.Contains("not found") : cam().VoxRowsText.Contains($"ksh2vox {version}"),
                    $"panels: the export line names the detected converter ({version ?? "not found"}), not a fixed version");
                var (bgNo20, vox20) = CameraTabContent.VoxNotes(new Configuration.ConverterDetection(Configuration.ConverterDetectState.Detected, new Core.Formats.Vox.VoxHostInfo { Raw = new System.Text.Json.Nodes.JsonObject { ["converter_version"] = "0.20.1" } }));
                expect(bgNo20.Contains("ksh2vox 0.20.1") && vox20.Contains("ksh2vox 0.20.1") && bgNo20.Contains("<bg_no>"), $"panels: with ksh2vox 0.20.1 the lines name it ({bgNo20} / {vox20})");
            }, shot("j-bg-image-movie")),

            // everything back
            (() =>
            {
                var e = ed();
                e.SelectTab(EditorTab.Compose);
                while (e.Session.Commands.History.Count() > undoBefore && e.Session.Commands.CanUndo) e.TriggerAction(EditorAction.Undo);
                expect(chart().Notes.Lasers[1].All(s => s != slam && s != diagonal) && !chart().Camera.Spin.Contains(spin!), "panels: undo removed the test objects");
                im.UseParentInput = true;
                e.Session.Commands.MarkSaved();
                e.Exit();
            }, null),
            (() => waitUntil(() => screenStack.CurrentScreen is not EditorScreen, "panels: editor closed", 20_000), null),
        };
    }
}
