using System.Globalization;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osuTK;
using osuTK.Input;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Formats.Kson;
using SDVXEditor.Core.Gameplay;
using SDVXEditor.Core.Validation;
using SDVXEditor.Game.Configuration;
using SDVXEditor.Game.Graphics.Containers;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Screens.Edit.Components;
using SDVXEditor.Game.Screens.Edit.Compose;
using SDVXEditor.Game.Screens.Edit.Dialogs;
using SDVXEditor.Game.Screens.Edit.Input;
using SDVXEditor.Game.Screens.Edit.Panels;
using SDVXEditor.Game.Screens.Play;

namespace SDVXEditor.Game;

/// <summary>
/// Smoke-test part <c>features</c> (no song needed; a generated 8 s song): the 2026-10-27 batch through real input
/// (<see cref="SmokeInput"/> for the editor, the root input manager for dialogs). Chart check (Ctrl+Shift+A, a row click
/// jumps and selects, the list follows a fix, Ctrl+S notes the result), insert blank tick / beat and the insert-delete
/// dialog (one undo step each, undo restores the kson), swap laser colour, the toolbox FX effect / slam sound drop-downs
/// on a selection and for new objects, Ctrl+click effect cycle, snap 1/192, inspector (note length, slam landing end),
/// layers (only BT: FX neither drawn nor clickable), loop playback wrapping, Song Setup's Advanced fields, the hard
/// gauge failing in the playtest (F2 cycles), and the laser entry piece not being hit-testable.
/// </summary>
public partial class EditorGame
{
    private List<(Action Act, string? Shot)> featureSteps(string? shots)
    {
        string work = Path.Combine(smokeWorkRoot(shots), "smoke-features", "Feature Song");
        var im = SmokeInput ?? throw new InvalidOperationException("smoke test: no smoke input manager");
        EditorScreen ed() => screenStack.CurrentScreen as EditorScreen ?? throw new InvalidOperationException("smoke test (features): editor not open");
        string inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
        Chart chart() => ed().Session.Chart;
        string kson() => KsonWriter.Write(chart());
        string before = "";
        var gaugeBefore = GaugeType.Normal;

        (Action, double)[] click(Func<Vector2> at, MouseButton b = MouseButton.Left) => new (Action, double)[]
        {
            (() => im.MoveMouseTo(at()), 60),
            (() => im.PressButton(b), 50),
            (() => im.ReleaseButton(b), 80),
        };

        (Action, double)[] drag(Func<Vector2> from, Func<Vector2> to) => new (Action, double)[]
        {
            (() => im.MoveMouseTo(from()), 50),
            (() => im.PressButton(MouseButton.Left), 50),
            (() => im.MoveMouseTo(Vector2.Lerp(from(), to(), 0.5f)), 50),
            (() => im.MoveMouseTo(to()), 50),
            (() => im.ReleaseButton(MouseButton.Left), 80),
        };

        (Action, double)[] keys(params Key[] k) =>
            k.Select(x => ((Action)(() => im.PressKey(x)), 30.0))
             .Concat(k.AsEnumerable().Reverse().Select(x => ((Action)(() => im.ReleaseKey(x)), 30.0)))
             .Append(((Action)(() => { }), 80.0)).ToArray();

        (Action, double)[] editorKeys(EditorAction action) => keys(EditorKeyBindings.All.First(b => b.Action == action).Keys.Keys.Select(toKey).ToArray());

        (Action, double)[] rootClick(Func<Vector2> at) => new (Action, double)[]
        {
            (() => inputMoveTo(at()), 60),
            (() => inputButton(true), 50),
            (() => inputButton(false), 80),
        };

        (Action, double)[] typeInto(Func<Drawable> box, string text) =>
            click(() => box().ScreenSpaceDrawQuad.Centre)
                .Append(((Action)(() => ((osu.Framework.Graphics.UserInterface.TextBox)box()).Text = text), 60.0))
                .Concat(keys(Key.Enter)).ToArray();

        (Action, double)[] then(params (Action, double)[][] parts) => parts.SelectMany(a => a).ToArray();
        (Action, double)[] check(Action a) => new (Action, double)[] { (a, 0d) };

        void seek(long pulse) => ed().Playback.CurrentTime.Value = ed().Session.PulseToMs(pulse);
        Vector2 screen(long pulse, float x) => ed().Playfield.ToScreenSpace(new Vector2(x, ed().Playfield.YOfPulse(pulse)));
        Vector2 note(NoteKind kind, int lane, long pulse) => screen(pulse, PlayfieldLayout.LaneLeft(kind, lane) + PlayfieldLayout.LaneWidth(kind) / 2);
        Vector2 laser(long pulse, double v) => screen(pulse, PlayfieldLayout.LaserX(v, false));
        Vector2 popupItem(string text) => this.ChildrenOfType<PopupMenuItem>().First(i => i.ChildrenOfType<SpriteText>().Any(t => t.Text.ToString() == text)).ScreenSpaceDrawQuad.Centre;
        void fillSongSetup()
        {
            var d = ed().OpenSongSetup();
            d.ShowPage(Screens.Edit.Dialogs.SongSetupDialog.AdvancedPage);
            d.DisplayBpmBox.Text = "120-240";
            d.StandardBpmBox.Text = "120";
            d.NarrowSlamBox.Current.Value = true;
        }

        void undoAll() { while (ed().Session.Commands.CanUndo) ed().TriggerAction(EditorAction.Undo); }

        return new List<(Action, string?)>
        {
            (() =>
            {
                if (Directory.Exists(work)) Directory.Delete(work, true);
                Directory.CreateDirectory(work);
                writeSineWav(Path.Combine(work, "feature.wav"), 8);
                KsonWriter.WriteFile(featureChart(), Path.Combine(work, "exh.kson"));
                pushAndWait(new EditorScreen(EditorSession.Load(work)));
            }, null),
            (() =>
            {
                waitUntil(() => ed().Playback.AudioLengthMs > 0, "the feature song's audio", 15_000);
                im.UseParentInput = false;
                before = kson();
            }, null),

            // ---- chart check
            (() => inputSequence("features: Ctrl+Shift+A", editorKeys(EditorAction.ChartCheck)), null),
            (() =>
            {
                var r = ed().ChartCheck.Report;
                expect(ed().ChartCheck.Shown.Value && r != null, "Ctrl+Shift+A opens the chart check panel");
                expect(r!.Issues.Any(i => i.RuleId == "notes.overlap") && r.Issues.Any(i => i.RuleId == "effect.undefined-fx") && r.Issues.Any(i => i.RuleId == "slam.successive"),
                    $"the check finds the seeded overlap, undefined effect and 1/32 slams ({string.Join(", ", r.Issues.Select(i => i.RuleId))})");
                report($"features: chart check - {r.Summary}");
            }, "90-features-chart-check"),
            (() =>
            {
                int row = ed().ChartCheck.RowIssues.ToList().FindIndex(i => i.RuleId == "notes.overlap");
                expect(row >= 0, "the overlap is listed");
                inputSequence("features: click the overlap row", click(() => ed().ChartCheck.Rows[row].ScreenSpaceDrawQuad.Centre));
            }, null),
            (() =>
            {
                var sel = ed().Session.Selection;
                expect(sel.Notes.Count == 2 && sel.Notes.All(n => n.Kind == NoteKind.Bt && n.Lane == 1), $"the row click selected both overlapping notes ({sel.Count})");
                expect(Math.Abs(ed().Playback.CurrentTime.Value - ed().Session.PulseToMs(480)) < 1, "and jumped to them");
                expect(ed().CurrentTab == EditorTab.Compose, "on the compose tab");
            }, "91-features-chart-check-jump"),
            (() => inputSequence("features: fix by Delete", keys(Key.Delete)), null),
            (() => waitUntil(() => ed().ChartCheck.Report?.Issues.All(i => i.RuleId != "notes.overlap") == true, "the list to drop the fixed overlap", 3000), null),
            (() => inputSequence("features: Ctrl+S with warnings", editorKeys(EditorAction.Save)), null),
            (() =>
            {
                expect(ed().LastSaveCheck is { Warnings: > 0 } && ed().LastToast.Contains("chart check", StringComparison.Ordinal), $"saving notes the check in the toast ('{ed().LastToast}')");
                expect(!ed().Session.IsDirty, "the save itself went through (non-blocking)");
                ed().TriggerAction(EditorAction.Undo);
                before = kson();
            }, null),

            // ---- insert / delete time
            (() =>
            {
                seek(1440);
                inputSequence("features: Insert (one snap tick)", editorKeys(EditorAction.InsertBlankTick));
            }, null),
            (() =>
            {
                expect(chart().Notes.Bt[3].Select(n => n.Y).SequenceEqual(new long[] { 1440 + 60, 2400 + 60 }), inv($"Insert moved everything from the cursor by 1/4 beat ({string.Join(",", chart().Notes.Bt[3].Select(n => n.Y))})"));
                expect(chart().Notes.Bt[0][0].Y == 240, "objects before the cursor stay");
                ed().TriggerAction(EditorAction.Undo);
                expect(kson() == before, "one Ctrl+Z restores the chart");
            }, null),
            (() => inputSequence("features: Shift+Insert (one beat)", editorKeys(EditorAction.InsertBlankBeat)), null),
            (() =>
            {
                expect(chart().Notes.Bt[3][0].Y == 1440 + 240 && chart().Beat.Bpm.Count == 1, "Shift+Insert inserted one beat");
                ed().TriggerAction(EditorAction.Undo);
                expect(kson() == before, "undo");
            }, null),
            (() => inputSequence("features: Alt+Insert dialog", editorKeys(EditorAction.InsertBlankCustom)), null),
            (() =>
            {
                var d = dialogOverlay.Current as TimeShiftDialog;
                expect(d != null, "Alt+Insert opens the insert / delete time dialog");
                d!.Amount.Value = 2;
                d.Unit.Value = TimeShiftUnit.Beats;
                inputSequence("features: Delete from cursor", rootClick(() => d.ChildrenOfType<Graphics.UserInterface.Dialog.DialogButton>().First(b => b.ChildrenOfType<SpriteText>().Any(t => t.Text.ToString() == "Delete from cursor")).ScreenSpaceDrawQuad.Centre));
            }, "92-features-time-dialog"),
            (() =>
            {
                expect(dialogOverlay.Current is not TimeShiftDialog, "the dialog closed");
                expect(chart().Notes.Bt[3].Select(n => n.Y).SequenceEqual(new long[] { 2400 - 480 }), inv($"Delete from cursor removed 2 beats: the chip at the cursor went, the later one moved back ({string.Join(",", chart().Notes.Bt[3].Select(n => n.Y))})"));
                ed().TriggerAction(EditorAction.Undo);
                expect(kson() == before, "undo restores it");
                report("features: Insert / Shift+Insert / Alt+Insert dialog delete, one undo step each - ok");
            }, null),

            // ---- swap laser colour (click the laser, Ctrl+Shift+H)
            (() =>
            {
                seek(1920);
                ed().SelectTool(EditorTool.Select);
                inputSequence("features: select laser L", then(click(() => laser(2160, 0.5)), check(() =>
                    expect(ed().Session.Selection.Sections.Any(s => s.Lane == 0), $"a click selected laser L ({ed().Session.Selection.Count})")), editorKeys(EditorAction.SwapLaserColour)));
            }, null),
            (() =>
            {
                expect(chart().Notes.Lasers[0].Count == 0 && chart().Notes.Lasers[1].Any(s => s.Y == 1920 && s.Points[0].Vf == 0.75), "Ctrl+Shift+H moved the section to laser R with its values");
                ed().TriggerAction(EditorAction.Undo);
                expect(kson() == before, "undo");
            }, "93-features-swapped"),

            // ---- toolbox drop-downs on a selection, Ctrl+click cycle, new FX hold
            (() =>
            {
                seek(1440);
                ed().Session.Selection.Clear();
                inputSequence("features: FX effect drop-down", then(click(() => note(NoteKind.Fx, 1, 1600)), click(() => ed().Toolbox.FxEffectDropdown.ScreenSpaceDrawQuad.Centre),
                    click(() => popupItem("Gate"))));
            }, null),
            (() =>
            {
                expect(HoldEffects.Of(chart().Notes.Fx[1].Single()) == "gate", $"the drop-down set the selected FX hold's effect ({HoldEffects.Of(chart().Notes.Fx[1].Single())})");
                ed().TriggerAction(EditorAction.Undo);
                ed().SelectTool(EditorTool.FxHold);
                inputSequence("features: Ctrl+click cycles", then(keys(), new (Action, double)[] { (() => im.PressKey(Key.ControlLeft), 40) }, click(() => note(NoteKind.Fx, 1, 1600)),
                    new (Action, double)[] { (() => im.ReleaseKey(Key.ControlLeft), 60) }));
            }, null),
            (() =>
            {
                string? fx = HoldEffects.Of(chart().Notes.Fx[1].Single());
                expect(fx == EffectCatalog.AvailableNames(chart(), false)[0], $"Ctrl+click with the FX tool gave the hold the first effect ({fx})");
                ed().TriggerAction(EditorAction.Undo);
                ed().Session.Selection.Clear(); // the drop-downs follow the selection; with nothing selected they keep the choice
                inputSequence("features: new FX hold", then(new (Action, double)[] { (() => ed().Toolbox.FxEffect.Value = "retrigger", 80) }, drag(() => note(NoteKind.Fx, 0, 2400), () => note(NoteKind.Fx, 0, 2880))));
            }, null),
            (() =>
            {
                var added = chart().Notes.Fx[0].FirstOrDefault(n => n.Y == 2400);
                expect(added is { IsHold: true } && HoldEffects.Of(added) == "retrigger", $"a new FX hold gets the toolbox effect ({added?.ToString() ?? "none"})");
                ed().TriggerAction(EditorAction.Undo);
                ed().Toolbox.FxEffect.Value = ComposeToolbox.NoEffect;
                ed().SelectTool(EditorTool.Select);
                seek(2880);
                ed().Session.Selection.Clear();
                inputSequence("features: slam sound drop-down", then(click(() => laser(2880, 1)), click(() => ed().Toolbox.SlamSoundDropdown.ScreenSpaceDrawQuad.Centre), click(() => popupItem("Mute"))));
            }, null),
            (() =>
            {
                expect(SlamSounds.At(chart(), 2880) == "slam_mute", $"the slam sound drop-down set the selected lone slam's sound ({SlamSounds.At(chart(), 2880) ?? "default"})");
                ed().TriggerAction(EditorAction.Undo);
                expect(kson() == before, "undo");
                report("features: FX effect / slam sound drop-downs on the selection, Ctrl+click cycle, toolbox effect on a new hold - ok");
            }, "94-features-dropdowns"),

            // ---- snap 1/192
            (() =>
            {
                seek(960);
                ed().Zoom.Value = ed().Zoom.MaxValue;
                for (int i = 0; i < 20; i++) ed().TriggerAction(EditorAction.DivisorNext);
                expect(ed().Compose.Divisor.Value == 192, $"Ctrl+wheel reaches 1/192 ({ed().Compose.Divisor.Value})");
            }, null),
            (() =>
            {
                // audit U4: at 1/192 the knob sat on the "1/192" text — the value has its own column now
                var d = ed().DivisorControl;
                float sliderRight = d.Slider.ScreenSpaceDrawQuad.TopRight.X, valueLeft = d.ValueText.ScreenSpaceDrawQuad.TopLeft.X;
                expect(sliderRight <= valueLeft + 0.5f, $"the divisor slider ends left of its value (slider to x {sliderRight:0}, value from x {valueLeft:0})");
            }, "95-features-snap-192"),
            (() =>
            {
                expect(ed().Compose.PulseAt(ed().Session.PulseToMs(961)) == 961, "1/192 snaps to the nearest pulse grid line");
                ed().TriggerAction(EditorAction.Divisor4);
                ed().Zoom.Value = ed().Zoom.Default;
            }, null),

            // ---- inspector
            (() =>
            {
                seek(960);
                inputSequence("features: inspector", then(editorKeys(EditorAction.ToggleInspector), click(() => note(NoteKind.Bt, 2, 1100))));
            }, null),
            (() =>
            {
                expect(ed().Inspector.Shown.Value && ed().Session.Selection.Notes.Count == 1, "Ctrl+Shift+I shows the inspector with the clicked hold");
                inputSequence("features: inspector length", typeInto(() => ed().Inspector.LengthField.Box, "1/2"));
            }, "96-features-inspector"),
            (() =>
            {
                expect(chart().Notes.Bt[2].Single().Length == 120, $"typing 1/2 set the hold length to half a beat ({chart().Notes.Bt[2].Single().Length})");
                ed().TriggerAction(EditorAction.Undo);
                seek(1920);
                ed().Session.Selection.Clear();
                var s = chart().Notes.Lasers[0][0];
                inputSequence("features: inspector slam end", then(click(() => laser(1920, 0.75)), typeInto(() => ed().Inspector.LaserLandingField.Box, "0.9")));
            }, null),
            (() =>
            {
                var s = chart().Notes.Lasers[0][0];
                expect(Math.Abs((s.Points[0].Vf ?? 0) - 0.9) < 1e-9 && s.Points[0].V == 0.25, $"the slam's landing end is now 0.9 ({s.Points[0]})");
                ed().TriggerAction(EditorAction.Undo);
                expect(kson() == before, "undo");
                ed().Inspector.Shown.Value = false;
                report("features: inspector - hold length 1/2, slam landing end 0.9, one undo step each - ok");
            }, null),

            // ---- layers
            (() => inputSequence("features: layers", then(editorKeys(EditorAction.ToggleLayers), click(() => ed().LayersPanel.OnlyButtons[EditorLayer.Bt].ScreenSpaceDrawQuad.Centre))), null),
            (() =>
            {
                expect(!ed().Layers.IsVisible(EditorLayer.Fx) && ed().Layers.IsVisible(EditorLayer.Bt), "'only' on BT hides FX and lasers");
                seek(1440);
                ed().Session.Selection.Clear();
                inputSequence("features: click a hidden FX hold", click(() => note(NoteKind.Fx, 1, 1600)));
            }, "97-features-layers-bt-only"),
            (() =>
            {
                expect(ed().Session.Selection.IsEmpty, "a hidden FX hold cannot be clicked");
                inputSequence("features: show all", click(() => ed().LayersPanel.ShowAllButton.ScreenSpaceDrawQuad.Centre));
            }, null),
            (() =>
            {
                expect(ed().Layers.AllVisible, "Show all");
                ed().LayersPanel.Shown.Value = false;
            }, null),

            // ---- loop
            (() =>
            {
                seek(240);
                ed().Session.Selection.Clear();
                ed().Session.Selection.Add(new NoteRef(NoteKind.Bt, 0, chart().Notes.Bt[0][0]));
                ed().Session.Selection.Add(new NoteRef(NoteKind.Bt, 3, chart().Notes.Bt[3][0]));
                inputSequence("features: Shift+Space loop", editorKeys(EditorAction.ToggleLoop));
            }, null),
            (() =>
            {
                expect(ed().Playback.Loop.Value != null && ed().Playback.IsPlaying.Value, "Shift+Space loops the selection and plays");
                waitUntil(() => ed().Playback.LoopWraps >= 1 && ed().Playback.CurrentTime.Value < ed().Playback.Loop.Value!.Value.End, "the loop to jump back", 8000);
            }, "98-features-loop"),
            (() => inputSequence("features: loop off", editorKeys(EditorAction.ToggleLoop)), null),
            (() =>
            {
                expect(ed().Playback.Loop.Value == null, "Shift+Space again ends the loop");
                ed().Playback.IsPlaying.Value = false;
            }, null),

            // ---- song setup advanced fields
            (fillSongSetup, "99-features-song-setup-advanced"),
            (() =>
            {
                (dialogOverlay.Current as Screens.Edit.Dialogs.SongSetupDialog)!.Accept();
                var m = chart().Meta;
                expect(m.DispBpm == "120-240" && m.StdBpm == 120 && chart().Audio.KeySound.LaserVolumeAuto == true, $"Song Setup wrote disp_bpm / std_bpm / vol_auto ({m.DispBpm}, {m.StdBpm}, {chart().Audio.KeySound.LaserVolumeAuto})");
                expect(KsonReader.Read(kson()).Meta.DispBpm == "120-240", "and kson keeps them");
                ed().TriggerAction(EditorAction.Undo);
                expect(kson() == before, "one undo step");
            }, null),

            // ---- entry piece: drawn, not hit
            (() =>
            {
                seek(2880);
                var hit = ed().Compose.LaserPointAt(new PlayfieldPointer(ed().Session.PulseToMs(2880) - 25 / ed().Compose.PxPerMs, PlayfieldLayout.LaserX(1, false)));
                expect(hit == null, "the entry piece below the lone slam is not hit-testable");
            }, "9a-features-entry-stub"),

            // ---- hard gauge
            (() =>
            {
                gaugeBefore = config.Get<GaugeType>(EditorSetting.PlaytestGauge);
                config.SetValue(EditorSetting.PlaytestGauge, GaugeType.Normal);
                config.SetValue(EditorSetting.PlaytestAutoplay, false);
                seek(0);
                ed().Playtest();
                waitUntil(() => screenStack.CurrentScreen is PlaytestScreen { IsLoaded: true }, "the playtest", 10_000);
            }, null),
            (() => inputSequence("features: F2 twice", then(keys(Key.F2), keys(Key.F2))), null),
            (() =>
            {
                var pt = (PlaytestScreen)screenStack.CurrentScreen;
                expect(pt.Gauge.Value == GaugeType.Hard && pt.Judge.Score.GaugeType == GaugeType.Hard && pt.Judge.Score.Gauge == 1, "F2 twice: hard gauge, starting full");
                waitUntil(() => pt.ShowingResults && pt.Judge.Score.Failed, "the untouched hard gauge to empty", 30_000);
            }, null),
            (() => { }, "9b-features-hard-gauge-crash"),
            (() =>
            {
                ((PlaytestScreen)screenStack.CurrentScreen).Exit();
                config.SetValue(EditorSetting.PlaytestGauge, gaugeBefore);
                waitUntil(() => screenStack.CurrentScreen is EditorScreen, "back in the editor", 5000);
            }, null),
            (() =>
            {
                undoAll();
                im.UseParentInput = true;
                report("features: layers only-BT + hidden FX not clickable, loop wraps, Song Setup advanced, entry piece not hit, hard gauge fails (F2) - ok");
                ed().Exit();
            }, null),
        };
    }

    /// <summary>120 BPM, 8 s: two overlapping BT-B chips, an FX hold with an undefined effect, slams 1/32 apart, a lone slam.</summary>
    private static Chart featureChart()
    {
        var c = new Chart();
        c.Meta.Title = "Feature Song";
        c.Meta.Artist = "smoke";
        c.Meta.DifficultySlot = 2;
        c.Meta.Level = 5;
        c.Beat.Bpm.Add(new BpmChange(0, 120));
        c.Audio.Bgm.Filename = "feature.wav";
        c.Notes.Bt[0].Add(new Note(240));
        for (long y = 3120; y <= 3720; y += 60) c.Notes.Bt[0].Add(new Note(y)); // enough chips for an untouched hard gauge to empty
        c.Notes.Bt[1].Add(new Note(480));
        c.Notes.Bt[1].Add(new Note(480));
        c.Notes.Bt[2].Add(new Note(960, 480));
        c.Notes.Bt[3].Add(new Note(1440));
        c.Notes.Bt[3].Add(new Note(2400));
        var bad = new Note(480, 480);
        bad.Effects.Add(new FxEffectEvent("nonexistent"));
        c.Notes.Fx[0].Add(bad);
        c.Notes.Fx[1].Add(new Note(1440, 480));
        c.Notes.Lasers[0].Add(new LaserSection(1920, new[] { new LaserPoint(0, 0.25, 0.75), new LaserPoint(30, 0.75, 0.25), new LaserPoint(240, 0.5) }));
        c.Notes.Lasers[1].Add(new LaserSection(2880, new[] { new LaserPoint(0, 1, 0) }));
        return c;
    }
}
