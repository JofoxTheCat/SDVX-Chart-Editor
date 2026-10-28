using System.Globalization;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osuTK;
using osuTK.Input;
using SDVXEditor.Core.Audio.Effects;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Game.Graphics.UserInterface;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Screens.Edit.Effects;
using SDVXEditor.Game.Screens.Play;

namespace SDVXEditor.Game;

/// <summary>
/// Smoke part <c>dsp</c> (needs <c>--open</c>): the audio effect DSP (Game <c>Audio/EffectAudioHost</c>) on a copy of the
/// song. A gate FX hold and a laser are added by command in a free stretch; then, with REAL mouse input, the effects
/// tab's "play effects" checkbox is ticked (the DSP must attach to the song), playback runs over the stretch and the
/// rack's counters must show gate and peaking-filter frames with zero DSP exceptions; "preview laser filter" is
/// clicked (low-pass sweep frames); the checkbox is unticked (the DSP must be removed and stay silent while playing);
/// finally a playtest with autoplay must run the gate again through the borrowed track. Smoke runs are muted, so the
/// DSP is checked through its counters, never by ear. The copy is closed without saving.
/// </summary>
public partial class EditorGame
{
    private List<(Action Act, string? Shot)> effectAudioSteps(string songFolder, int? slot, string? shots)
    {
        var im = SmokeInput ?? throw new InvalidOperationException("smoke test: no smoke input manager");
        string inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
        string work = Path.Combine(smokeWorkRoot(shots), "smoke-song-dsp", Path.GetFileName(songFolder.TrimEnd('\\', '/')));
        string? shot(string name) => shots != null ? "75-dsp-" + name : null;
        long p = 0, measure = 960;
        long gateBefore = 0, peakBefore = 0, lpfBefore = 0, callsBefore = 0, seeksBefore = 0, slipsBefore = 0;

        EditorScreen ed() => screenStack.CurrentScreen as EditorScreen ?? throw new InvalidOperationException("smoke test: editor not open");
        PlaytestScreen pt() => screenStack.CurrentScreen as PlaytestScreen ?? throw new InvalidOperationException("smoke test: playtest not open");
        Audio.EffectAudioHost? hostRef = null;
        // the editor's host, also while the playtest is the current screen
        Audio.EffectAudioHost host() => hostRef ??= ed().EffectAudio;
        long frames(EffectKind k) => host().Rack?.FramesByKind[(int)k] ?? 0;
        string state()
        {
            var h = host();
            var r = h.Rack;
            return inv($"attached {h.Attached}, calls {h.DspCalls}, errors {h.DspErrors} {h.LastDspError?.Message}, gate {frames(EffectKind.Gate)}, peaking {frames(EffectKind.PeakingFilter)}, ")
                   + inv($"low-pass {frames(EffectKind.LowPassFilter)}, rebuilds {h.Rebuilds}, lead {h.LeadMs:0} ms, seeks {r?.Seeks}, slips {r?.Slips}, last {r?.LastEndMs:0} ms, screen {screenStack.CurrentScreen?.GetType().Name}");
        }

        (Action, double)[] click(Func<Vector2> to) => new (Action, double)[]
        {
            (() => im.MoveMouseTo(to()), 60), (() => im.PressButton(MouseButton.Left), 50), (() => im.ReleaseButton(MouseButton.Left), 100),
        };
        // scroll the control into view a frame before the click reads its position
        (Action, double)[] reveal(Func<bool, Drawable> d) => new (Action, double)[] { (() => d(true), 400) };
        (Action, double)[] then(params (Action, double)[][] parts) => parts.SelectMany(a => a).ToArray();
        (Action, double)[] check(Action a) => new (Action, double)[] { (a, 0d) };

        // a control of the effects tab, scrolled into view first
        Drawable control(Func<Drawable, bool> match, string what, bool scroll)
        {
            var d = ed().EffectsContent.ChildrenOfType<Drawable>().FirstOrDefault(match)
                    ?? throw new InvalidOperationException($"smoke test: dsp: {what} not found");
            // centred in its column (at the column's edge it sits under the bottom bar)
            if (scroll && d.FindClosestParent<ScrollContainer<Drawable>>() is { } sc)
                sc.ScrollTo(sc.GetChildPosInContent(d) - sc.DisplayableContent / 2, false);
            return d;
        }

        Drawable checkbox(bool scroll = false) => control(d => d is PinkCheckbox c && c.TooltipText.ToString() == EffectsTabContent.PLAY_EFFECTS_TIP, "the play-effects checkbox", scroll);
        Drawable previewLaser(bool scroll = false) => control(d => d is FlatButton b && b.TooltipText.ToString().StartsWith("Sweeps the filter", StringComparison.Ordinal), "the preview laser filter button", scroll);

        void playFrom(long pulse)
        {
            var e = ed();
            e.Playback.IsPlaying.Value = false;
            e.Playback.CurrentTime.Value = Math.Max(0, e.Session.PulseToMs(pulse) - e.Playback.AudioOffset.Value);
            e.Playback.IsPlaying.Value = true;
        }

        return new List<(Action, string?)>
        {
            (() =>
            {
                // the tutorial part (main chapter 5) ticks "play effects" and the setting persists: start from the default
                config.SetValue(SDVXEditor.Game.Configuration.EditorSetting.PlayEffects, false);
                if (Directory.Exists(work)) Directory.Delete(work, true);
                Directory.CreateDirectory(work);
                foreach (string f in Directory.EnumerateFiles(songFolder)) File.Copy(f, Path.Combine(work, Path.GetFileName(f)));
                pushAndWait(new EditorScreen(EditorSession.Load(work, slot)));
            }, null),
            (() => waitUntil(() => ed().Playback.AudioLengthMs > 0, "the song audio to load", 20_000), null),
            (() =>
            {
                var e = ed();
                var s = e.Session;
                var c = s.Chart;
                expect(!e.EffectsContent.PlayEffects.Value && !host().Attached, $"dsp: effects start switched off, no DSP on the song ({state()})");

                // a 3-measure stretch from a quarter of the song (inside the audio); FX-L and laser L there are cleared
                // first (by command, never saved) so the test objects do not overlap the chart's own
                double audioMs = e.Playback.AudioLengthMs;
                int m = Math.Max(1, s.Timing.MeasureAt((long)s.MsToPulse(audioMs * 0.25)));
                p = s.Timing.MeasureStart(m);
                measure = s.Timing.MeasureLength(m);
                long z = s.Timing.MeasureStart(m + 3);
                expect(s.PulseToMs(z) < audioMs - 1000, $"dsp: the stretch lies inside the audio ({audioMs:0} ms)");
                var fxInside = c.Notes.Fx[0].Where(n => n.EndY >= p - measure && n.Y <= z).Select(n => new NoteRef(NoteKind.Fx, 0, n)).ToList();
                if (fxInside.Count > 0) s.Commands.Execute(new RemoveNotesCommand(fxInside));
                foreach (var x in c.Notes.Lasers[0].Where(x => x.EndY >= p - measure && x.Y <= z).ToList())
                    s.Commands.Execute(new RemoveLaserSectionCommand(0, x));

                var hold = new Note(p, measure * 2) { Effects = { new FxEffectEvent("gate", 0, new Dictionary<string, string> { ["wave_length"] = "1/8" }) } };
                s.Commands.Execute(new AddNotesCommand(new[] { new NoteRef(NoteKind.Fx, 0, hold) }, "dsp smoke: gate hold"));
                s.Commands.Execute(new AddLaserSectionCommand(0, new LaserSection(p, new[] { new LaserPoint(0, 0), new LaserPoint(measure * 2, 1) })));
                e.SelectTab(EditorTab.Effects);
                im.UseParentInput = false;
                report(inv($"dsp: test stretch at measure {m + 1} ({s.PulseToMs(p):0} ms), audio {audioMs:0} ms"));
            }, shot("a-objects")),

            // tick "play effects" with the mouse: the DSP goes onto the song
            (() => inputSequence("dsp: tick play effects", then(reveal(checkbox), click(() => checkbox().ScreenSpaceDrawQuad.Centre), check(() =>
            {
                expect(ed().EffectsContent.PlayEffects.Value, $"dsp: the checkbox is ticked (at {checkbox().ScreenSpaceDrawQuad.Centre}, size {checkbox().DrawSize}, mouse {im.CurrentState.Mouse.Position}; {state()})");
                waitUntil(() => host().Attached && host().Rack?.Timeline != null, "the effect DSP to attach to the song", 5000);
            }))), shot("b-ticked")),
            (() =>
            {
                expect(host().Attaches >= 1, $"dsp: attached once ({state()})");
                callsBefore = host().DspCalls;
                gateBefore = frames(EffectKind.Gate);
                peakBefore = frames(EffectKind.PeakingFilter);
                playFrom(p - measure / 4);
                waitUntil(() => frames(EffectKind.Gate) > gateBefore + 44100 && frames(EffectKind.PeakingFilter) > peakBefore + 22050,
                    "the gate and the peaking filter to run on the playing song", 15_000);
            }, null),
            (() =>
            {
                var h = host();
                ed().Playback.IsPlaying.Value = false;
                expect(h.DspErrors == 0, $"dsp: no exception in the audio callback ({state()})");
                expect(h.DspCalls > callsBefore, $"dsp: the callback ran ({state()})");
                expect(h.Rack!.LastLaserFreq > 50, $"dsp: the laser moved the peaking filter's cutoff ({state()})");
                report($"dsp: playback {state()}");
                report("dsp: discontinuities (expected→got frames) " + string.Join(" ", h.Rack!.SeekLog.Take((int)Math.Min(16, h.Rack.Seeks + h.Rack.Slips)).Select(x => $"{x.Expected}→{x.Got}")));
            }, shot("c-played")),

            // at 50 % rate (BASS_FX tempo): no resets beyond the seek that starts it
            (() =>
            {
                ed().Playback.Rate.Value = 0.5;
                gateBefore = frames(EffectKind.Gate);
                seeksBefore = host().Rack!.Seeks;
                slipsBefore = host().Rack!.Slips;
                playFrom(p);
                waitUntil(() => frames(EffectKind.Gate) > gateBefore + 44100, "the gate to run at 50 % rate", 15_000);
            }, null),
            (() =>
            {
                var r = host().Rack!;
                ed().Playback.IsPlaying.Value = false;
                ed().Playback.Rate.Value = 1;
                expect(r.Seeks - seeksBefore <= 2, $"dsp: 50 % rate: only the start resets the effects ({state()})");
                // BASS_FX's tempo re-reads ~50 source frames per 392-frame read below 100 %: those are slips (time re-anchored,
                // no reset), never seeks
                expect(r.Slips > slipsBefore || r.Seeks == seeksBefore, $"dsp: 50 % rate: slips, not resets ({state()})");
                expect(host().DspErrors == 0, $"dsp: no exception at 50 % ({state()})");
                report($"dsp: 50 % rate {state()}");
            }, null),

            // "preview laser filter" (real click): sweeps the picked filter on the song for 2 s
            (() =>
            {
                lpfBefore = frames(EffectKind.LowPassFilter) + frames(EffectKind.PeakingFilter) + frames(EffectKind.HighPassFilter) + frames(EffectKind.BitCrusher);
                ed().Playback.CurrentTime.Value = Math.Max(0, ed().Session.PulseToMs(p - 4 * measure));
                inputSequence("dsp: preview laser filter", then(reveal(previewLaser), click(() => previewLaser().ScreenSpaceDrawQuad.Centre), check(() =>
                {
                    expect(ed().Playback.IsPlaying.Value, $"dsp: the preview starts playback ({state()}; toast '{ed().LastToast}')");
                    waitUntil(() => !ed().Playback.IsPlaying.Value, "the preview to end and stop playback", 10_000);
                })));
            }, null),
            (() =>
            {
                long now = frames(EffectKind.LowPassFilter) + frames(EffectKind.PeakingFilter) + frames(EffectKind.HighPassFilter) + frames(EffectKind.BitCrusher);
                expect(now > lpfBefore + 44100, $"dsp: the preview ran the laser filter ({now - lpfBefore} frames; {state()})");
                expect(host().Input.PreviewName == null, "dsp: the preview cleared itself");
            }, shot("d-preview")),

            // untick: the DSP is removed and the callback stays silent while the song plays
            (() => inputSequence("dsp: untick play effects", then(reveal(checkbox), click(() => checkbox().ScreenSpaceDrawQuad.Centre), check(() =>
            {
                expect(!ed().EffectsContent.PlayEffects.Value, $"dsp: the checkbox is unticked ({state()})");
            }))), null),
            (() =>
            {
                expect(!host().Attached && host().Detaches >= 1, $"dsp: the DSP is off the song ({state()})");
                callsBefore = host().DspCalls;
                playFrom(p);
                waitUntil(() => ed().Playback.TrackTime > ed().Session.PulseToMs(p) + 700, "the song to play on without effects", 5000);
            }, null),
            (() =>
            {
                ed().Playback.IsPlaying.Value = false;
                expect(host().DspCalls == callsBefore, $"dsp: unticked = no DSP call at all ({state()})");
                // on again for the playtest (the setting, as the checkbox would)
                ed().EffectsContent.PlayEffects.Value = true;
                waitUntil(() => host().Attached, "the DSP to attach again", 5000);
            }, null),

            // playtest with autoplay: the borrowed track keeps its DSP, the chart drives the effects
            (() =>
            {
                gateBefore = frames(EffectKind.Gate);
                ed().Playback.CurrentTime.Value = ed().Session.PulseToMs(p + measure / 8);
                ed().Playtest();
                waitForPlaytest();
            }, null),
            (() =>
            {
                pt().SetAutoplay(true, persist: false);
                waitUntil(() => frames(EffectKind.Gate) > gateBefore + 22050, "the gate to run in the playtest (autoplay)", 15_000);
            }, null),
            (() =>
            {
                var a = pt().EffectAudio;
                expect(a != null && a.Input.ChartDriven, "dsp: autoplay = chart-driven effects");
                expect(host().DspErrors == 0, $"dsp: no exception in the playtest ({state()})");
                report($"dsp: playtest {state()}");
                var rk = host().Rack!;
                double busy = rk.ProcessTicks / (double)System.Diagnostics.Stopwatch.Frequency, audio = rk.Frames / (double)rk.SampleRate;
                report(inv($"dsp: cost {busy * 1000:0.0} ms for {audio:0.0} s of audio ({100 * busy / Math.Max(1e-9, audio):0.00} % of real time), worst block {rk.MaxBlockTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency:0.00} ms, {rk.Blocks} blocks"));
                pt().Back();
            }, shot("e-playtest")),
            (() => { pt().Back(); }, null),
            (() =>
            {
                expect(host().Input.ChartDriven, "dsp: back in the editor, effects follow the chart again");
                ed().EffectsContent.PlayEffects.Value = false;
                ed().Session.Commands.MarkSaved();
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(work)!, "smoke-report.txt"), smokeReport.ToString());
                ed().Exit();
            }, null),
        };
    }
}
