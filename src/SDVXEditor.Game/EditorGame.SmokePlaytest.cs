using System.Globalization;
using osu.Framework.Screens;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Gameplay;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Screens.Play;

namespace SDVXEditor.Game;

/// <summary>
/// Smoke-test part for the playtest (with <c>--open</c>): on its own copy of the song, three autoplay runs from the
/// editor — the densest notes, the laser-heaviest part, and a part with camera work added for the test (manual
/// tilt, rotation, zoom) — each checked for score &gt; 0 and no ERROR, then the result summary and the return to
/// the editor at the stop time. Frame and build times go into smoke-report.txt.
/// </summary>
public partial class EditorGame
{
    private List<(Action Act, string? Shot)> playtestSteps(string songFolder, int? slot, string? shots, double interval)
    {
        string work = Path.Combine(smokeWorkRoot(shots),
            "smoke-song-playtest", Path.GetFileName(songFolder.TrimEnd('\\', '/')));

        EditorScreen ed() => screenStack.CurrentScreen as EditorScreen ?? throw new InvalidOperationException($"smoke test: editor not open (got {screenStack.CurrentScreen?.GetType().Name})");
        PlaytestScreen pt() => screenStack.CurrentScreen as PlaytestScreen ?? throw new InvalidOperationException($"smoke test: playtest not open (got {screenStack.CurrentScreen?.GetType().Name})");

        double denseMs = 0, laserMs = 0, cameraMs = 0, stressMs = 0;
        int stressObjects = 0;
        var inv = CultureInfo.InvariantCulture;

        // the 4 s window with the most units of the given kinds; returns its start
        static double busiest(IEnumerable<double> times)
        {
            var t = times.OrderBy(x => x).ToList();
            if (t.Count == 0) return 0;
            int best = 0, j = 0;
            double start = t[0];
            for (int i = 0; i < t.Count; i++)
            {
                while (t[i] - t[j] > 4000) j++;
                if (i - j + 1 > best)
                {
                    best = i - j + 1;
                    start = t[j];
                }
            }
            return start;
        }

        List<(Action, string?)> run(string name, Func<double> at, string? shot, Action? check = null, double? hiSpeed = null)
        {
            var list = new List<(Action, string?)>
            {
                (() =>
                {
                    var e = ed();
                    // the lead-in starts 2 s before the editor position: put the window right after it
                    e.Playback.CurrentTime.Value = at() + PlaytestScreen.LEAD_IN_MS * e.Playback.Rate.Value;
                    e.Playtest();
                    waitForPlaytest();
                }, null),
                (() =>
                {
                    pt().SetAutoplay(true, persist: false);
                    pt().HiSpeedOverride = hiSpeed;
                }, null),
            };

            // let it play ~3.4 s of clock time (the lead-in and the window); a slow build delays, it does not fail
            list.Add((() => waitUntil(() => pt().RunningMs >= 3400, $"playtest {name}: 3.4 s of play", 30_000), null));
            list.Add((() => { }, shot));

            list.Add((() =>
            {
                var p = pt();
                var s = p.Judge.Score;
                report(string.Create(inv,
                    $"playtest {name}: from {p.StartMs:0} ms, ran {p.RunningMs:0} ms real / {p.RunningFrames} frames (mean {p.MeanFrameMs:0.00} ms, max {p.MaxFrameMs:0.0} ms); "
                    + $"playfield build mean {p.Playfield.MeanBuildMs:0.000} ms, max {p.Playfield.MaxBuildMs:0.000} ms, up to {p.Playfield.MaxQuads} quads / {p.Playfield.MaxVisibleObjects} objects per frame; "
                    + $"score {s.Score}, judged {s.Judged}, crit {s.Criticals}, errors {s.Errors}, chain {s.MaxChain}; "
                    + $"hi-speed {p.HiSpeed:0.###}{(p.HiSpeedOverride != null ? " (forced)" : "")} on {p.HiSpeedBasis.Label} {p.HiSpeedBasis.Bpm:0.##}"));
                expect(p.HiSpeedBasis.Bpm > 0, $"playtest {name}: a hi-speed basis BPM ({p.HiSpeedBasis.Bpm})");
                expect(p.RunningMs > 1500, $"playtest {name}: the clock ran ({p.RunningMs:0} ms)");
                expect(s.Judged > 0, $"playtest {name}: something was judged");
                expect(s.Score > 0, $"playtest {name}: score > 0 (got {s.Score})");
                expect(s.Errors == 0, $"playtest {name}: autoplay made no ERROR (got {s.Errors})");
                // audit U17: the status block stays in the left margin (one long line used to run over the track) and the
                // chain sits above the track's far end
                var block = p.Hud.StatusBlock.ScreenSpaceDrawQuad;
                var screen = p.ScreenSpaceDrawQuad;
                expect(block.TopRight.X < screen.TopLeft.X + (screen.TopRight.X - screen.TopLeft.X) * 0.3f,
                    string.Create(inv, $"playtest {name}: the status block ends left of the track (right edge {block.TopRight.X - screen.TopLeft.X:0} of {screen.TopRight.X - screen.TopLeft.X:0} px)"));
                expect(p.Hud.ChainDisplay.ScreenSpaceDrawQuad.BottomLeft.Y < screen.TopLeft.Y + (screen.BottomLeft.Y - screen.TopLeft.Y) * 0.15f,
                    $"playtest {name}: the chain sits above the track");
                expect(!p.Hud.StatusLines.Any(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"est\. 2[1-9]|est\. 20\.[1-9]")), $"playtest {name}: an estimate above 20 reads 20+ ({p.Hud.StatusLines.First()})");
                check?.Invoke();
                p.Back(); // result summary
            }, null));
            return list;
        }

        var steps = new List<(Action, string?)>
        {
            (() =>
            {
                if (Directory.Exists(work)) Directory.Delete(work, true);
                Directory.CreateDirectory(work);
                foreach (string f in Directory.EnumerateFiles(songFolder))
                    File.Copy(f, Path.Combine(work, Path.GetFileName(f)));

                var session = EditorSession.Load(work, slot);
                var pc = new PlayableChart(session.Chart, session.Timing);
                denseMs = busiest(pc.Chips.SelectMany(l => l).Concat(pc.HoldTicks.SelectMany(l => l)).Select(o => o.Ms));
                laserMs = busiest(pc.LaserTicks.SelectMany(l => l).Concat(pc.Slams.SelectMany(l => l)).Select(o => o.Ms));
                cameraMs = busiest(pc.All.Where(o => Math.Abs(o.Ms - denseMs) > 6000 && Math.Abs(o.Ms - laserMs) > 6000).Select(o => o.Ms));
                if (cameraMs == 0) cameraMs = denseMs;

                // stress start: where the most notes and laser sections lie in the next 400 beats (the stretch the lowest
                // hi-speed puts on the lane); the densest 4 s can sit at the very end of a song, with nothing left ahead
                var ys = session.Chart.Notes.Bt.Concat(session.Chart.Notes.Fx).SelectMany(l => l).Select(n => n.Y)
                                .Concat(session.Chart.Notes.Lasers.SelectMany(l => l).Select(sec => sec.Y)).OrderBy(y => y).ToList();
                long span = 400 * Timebase.PulsesPerQuarter;
                long stressY = 0;
                for (int i = 0, j = 0; i < ys.Count; i++)
                {
                    while (j < ys.Count && ys[j] - ys[i] < span) j++;
                    if (j - i > stressObjects)
                    {
                        stressObjects = j - i;
                        stressY = ys[i];
                    }
                }

                stressMs = Math.Max(0, session.PulseToMs(stressY) - 500);

                // camera work for the tilted run, as edits (the session is dirty afterwards: the unsaved warning path)
                long p0 = (long)Math.Max(0, session.MsToPulse(cameraMs));
                var cam = session.Chart.Camera;
                if (!cam.Tilt.Any(t => t.Y == p0)) session.Commands.Execute(new AddTiltPointCommand(new TiltPoint(p0, TiltValue.Of(0.6))));
                if (!cam.Body.RotationDeg.Any(g => g.Y == p0)) session.Commands.Execute(new AddGraphPointCommand(CamGraph.RotationDeg, new GraphPoint(p0, 6)));
                if (!cam.Body.ZoomTop.Any(g => g.Y == p0)) session.Commands.Execute(new AddGraphPointCommand(CamGraph.ZoomTop, new GraphPoint(p0, -60)));
                if (!cam.Body.ZoomBottom.Any(g => g.Y == p0)) session.Commands.Execute(new AddGraphPointCommand(CamGraph.ZoomBottom, new GraphPoint(p0, 60)));

                report(string.Create(inv, $"playtest: copy in {work}, {pc.MaxChain} units, dense at {denseMs:0} ms, lasers at {laserMs:0} ms, camera at {cameraMs:0} ms, stress at {stressMs:0} ms ({stressObjects} notes + laser sections in the next 400 beats)"));
                pushAndWait(new EditorScreen(session));
            }, null),
        };

        // SDVXEDITOR_SMOKE_PLAYTEST_AT=144400[,…]: autoplay through each song time, pause there, screenshot and log the
        // scroll geometry (BPM, hi-speed basis, pixels per beat at the judgement line, camera) — for "the notes look compressed"
        foreach (double probeMs in playtestProbeTimes())
        {
            string tag = probeMs.ToString("0", inv);
            steps.Add((() =>
            {
                var e = ed();
                e.Playback.CurrentTime.Value = Math.Max(0, probeMs - 1500);
                e.Playtest();
                waitForPlaytest();
            }, null));
            steps.Add((() => pt().SetAutoplay(true, persist: false), null));
            steps.Add((() => waitUntil(() => pt().PlayClock.CurrentTime >= probeMs, $"playtest probe: clock at {tag} ms", 30_000), null));
            steps.Add((() => pt().TogglePause(), null));
            steps.Add((() => { }, $"65-playtest-at-{tag}"));
            steps.Add((() =>
            {
                report(pt().DescribeScroll());
                pt().Back(); // result summary
            }, null));
            steps.Add((() => pt().Back(), null)); // editor
        }

        steps.AddRange(run("dense", () => denseMs, "60-playtest-normal"));
        steps.Add((() => pt().Back(), null)); // back to the editor
        steps.AddRange(run("lasers", () => laserMs, "61-playtest-lasers"));
        steps.Add((() => pt().Back(), null));
        // stress: a hi-speed so low that the lane spans ~500 beats, from where the most objects lie ahead: over 300 objects
        // on screen at once, or (a short chart) at least half of what the next 400 beats hold
        steps.AddRange(run("stress (whole lane)", () => stressMs, "64-playtest-stress", () =>
        {
            int want = Math.Min(300, stressObjects / 2);
            expect(pt().Playfield.MaxVisibleObjects > want, $"playtest stress: many objects visible ({pt().Playfield.MaxVisibleObjects}, want > {want}; {stressObjects} in the next 400 beats)");
        }, hiSpeed: 0.016));
        steps.Add((() => pt().Back(), null));

        double stoppedAt = 0;
        steps.AddRange(run("camera", () => cameraMs + 1000, "62-playtest-tilted", () =>
        {
            var st = pt().Playfield.LastState;
            expect(Math.Abs(st.RollDeg) > 1 || st.ZoomTop != 0 || st.ZoomBottom != 0, string.Create(inv, $"playtest camera: tilt/rotation/zoom applied (roll {st.RollDeg:0.0}°, top {st.ZoomTop:0})"));
            stoppedAt = pt().PlayClock.CurrentTime;
        }));
        steps.Add((() =>
        {
            expect(pt().ShowingResults, "playtest: Esc shows the result summary");
        }, "63-playtest-results"));
        steps.Add((() => pt().Back(), null));
        steps.Add((() =>
        {
            var e = ed();
            double now = e.Playback.CurrentTime.Value;
            expect(Math.Abs(now - Math.Max(0, stoppedAt)) < 100, string.Create(inv, $"playtest: the editor resumes where the test stopped ({now:0} vs {stoppedAt:0} ms)"));
            expect(e.Session.AudioPath == null || e.Playback.Track != null, "playtest: the song track is back in the editor");
            report(string.Create(inv, $"playtest: back in the editor at {now:0} ms"));
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(work)!, "smoke-report.txt"), smokeReport.ToString());
            e.Exit();
        }, null));

        return steps;
    }

    /// <summary>Environment variable: comma-separated song times (ms) the playtest part autoplays to, pauses at and screenshots.</summary>
    public const string SmokePlaytestAtVariable = "SDVXEDITOR_SMOKE_PLAYTEST_AT";

    private static IEnumerable<double> playtestProbeTimes() =>
        (Environment.GetEnvironmentVariable(SmokePlaytestAtVariable) ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(v => double.Parse(v, CultureInfo.InvariantCulture));
}
