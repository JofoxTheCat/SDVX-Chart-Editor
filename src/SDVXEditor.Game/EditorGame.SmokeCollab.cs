using System.Globalization;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osuTK;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Formats.Ksh;
using SDVXEditor.Core.Formats.Kson;
using SDVXEditor.Core.Library;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Screens.Edit.Components;
using SDVXEditor.Game.Screens.Edit.Compose;
using SDVXEditor.Game.Screens.Edit.Dialogs;
using SDVXEditor.Game.Screens.Edit.Input;
using SDVXEditor.Game.Screens.SongSelect;

namespace SDVXEditor.Game;

/// <summary>
/// Smoke-test part for the collab features (with <c>--open</c>), on a scratch copy of the song: a labelled,
/// coloured bookmark (list dialog, hover labels, kson + ksh round trip), .svz export and re-import (through the
/// Replace / Keep both dialog → Keep both), a reference difficulty (box select there, Ctrl+C, Ctrl+V into the main
/// chart, Esc closes), and laser points placed and hit from outside the lane area.
/// </summary>
public partial class EditorGame
{
    private List<(Action Act, string? Shot)> collabSteps(string songFolder, int? slot, string? shots, double interval)
    {
        string work = Path.Combine(smokeWorkRoot(shots),
            "smoke-collab", Path.GetFileName(songFolder.TrimEnd('\\', '/')));

        EditorScreen ed() => screenStack.CurrentScreen as EditorScreen ?? throw new InvalidOperationException("smoke test (collab): editor not open");

        long bookmarkY = 0, p0 = 0, beat = 240;
        Task<string?>? export = null;
        Task? reimport = null;
        Task<SongImportResult>? firstImport = null;
        string? firstName = null;
        string? svzPath = null;
        int libraryBefore = 0;
        int mainNotesBefore = 0, refNotesBefore = 0, copied = 0;

        int notes(Chart c) => c.Notes.Bt.Concat(c.Notes.Fx).Sum(l => l.Count);
        PlayfieldPointer at(long pulse, float x) => ed().Playfield.PointerAt(pulse, x);
        string inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        Action mainInputMarker = () => { }, referenceInputMarker = () => { };
        var steps = new List<(Action, string?)>
        {
            (() =>
            {
                if (Directory.Exists(work)) Directory.Delete(work, true);
                Directory.CreateDirectory(work);
                foreach (string f in Directory.EnumerateFiles(songFolder))
                    File.Copy(f, Path.Combine(work, Path.GetFileName(f)));
                var session = EditorSession.Load(work, slot);
                if (session.Charts.Count < 2)
                {
                    // a one-difficulty song: the copy gets a second difficulty (the same chart saved for another slot, the
                    // name decides the slot) so the reference-difficulty steps below have something to open
                    var copy = KsonReader.Read(KsonWriter.Write(session.Chart));
                    int other = session.Chart.Meta.DifficultySlot == 1 ? 2 : 1;
                    copy.Meta.DifficultySlot = other;
                    string file = Path.Combine(work, Difficulty.ShortName(other) + ".kson");
                    File.WriteAllText(file, KsonWriter.Write(copy));
                    session = EditorSession.Load(work, slot ?? session.Chart.Meta.DifficultySlot);
                    report($"collab: {Path.GetFileName(songFolder)} has one difficulty - the copy got {Path.GetFileName(file)} as the reference difficulty");
                }

                expect(session.Charts.Count >= 2, "collab smoke needs a song with at least two difficulties");
                pushAndWait(new EditorScreen(session));
                report($"collab: working on a copy in {work}");
            }, null),

            (mainInputMarker, null), // compose tools through the real input path, no reference (EditorGame.SmokeComposeInput.cs)

            // ---- bookmarks with label and colour
            (() =>
            {
                var e = ed();
                var s = e.Session;
                e.Playback.CurrentTime.Value = s.PulseToMs(s.Timing.MeasureStart(8));
                e.TriggerAction(EditorAction.AddBookmark);
                bookmarkY = s.Chart.Editor.Bookmarks.Single().Y;
                expect(e.BookmarkTools.Set(bookmarkY, new Bookmark("Part B - mapper2", "#ff66aa")), "bookmark renamed + recoloured");
                long second = e.BookmarkTools.AddAtCursor();
                expect(second == bookmarkY, "adding at the same spot reuses the bookmark");
                e.Playback.CurrentTime.Value = s.PulseToMs(s.Timing.MeasureStart(16));
                e.BookmarkTools.AddAtCursor("Part C - mapper1", "#4fdb4f");
                expect(s.Chart.Editor.Bookmarks.Count == 2 && e.Bookmarks.Count == 2, "two bookmarks, the ms list follows the chart");
                expect(s.IsDirty, "a bookmark edit makes the chart dirty");
                e.OpenBookmarkList();
            }, null),
            (() =>
            {
                var dialog = dialogOverlay.Current as BookmarkListDialog ?? throw new InvalidOperationException("smoke test: bookmark list not open");
                expect(dialog.List.Items.Count == 2, "the list shows both bookmarks");
                dialog.List.Select(dialog.List.Items[0]);
                expect(dialog.LabelBox.Text == "Part B - mapper2", "selecting a row fills the label field");
            }, "20-bookmark-list"),
            (() =>
            {
                var dialog = (BookmarkListDialog)dialogOverlay.Current!;
                dialog.JumpToSelected();
                dialog.Accept();
                var e = ed();
                expect(Math.Abs(e.Playback.CurrentTime.Value - e.Session.PulseToMs(bookmarkY)) < 1, "jump to the bookmark");
                e.Ruler.SetHover(e.Ruler.DrawWidth / 2); // the current time is drawn in the ruler's centre
                e.Summary.ShowHoverAt((float)(e.Session.PulseToMs(bookmarkY) / e.Playback.LengthMs * e.Summary.DrawWidth));
                expect(e.Summary.HoverLabel == "Part B - mapper2", $"bottom summary shows the label on hover ({e.Summary.HoverLabel ?? "none"})");
            }, null),
            (() =>
            {
                var e = ed();
                expect(e.Ruler.BookmarkHoverLabel == "Part B - mapper2", $"top timeline shows the label on hover ({e.Ruler.BookmarkHoverLabel ?? "none"})");
                e.TriggerAction(EditorAction.Save);
                var reread = KsonReader.ReadFile(e.Session.ChartPath!);
                expect(reread.Editor.Bookmarks.At(bookmarkY)?.Value == new Bookmark("Part B - mapper2", "#ff66aa"), "label and colour saved in kson");
                var viaKsh = KshReader.Read(KshWriter.Write(reread));
                expect(viaKsh.Editor.Bookmarks.Select(b => b.Value).SequenceEqual(reread.Editor.Bookmarks.Select(b => b.Value)), "bookmarks survive a ksh export (//#bookmark lines)");
                e.Ruler.SetHover(null);
                report("collab: 2 labelled/coloured bookmarks, list dialog, hover labels (summary + timeline), kson save, ksh round trip - ok");
            }, "20b-bookmark-hover"),

            // ---- .svz export and re-import
            (() =>
            {
                export = ed().ExportSvz();
                waitUntil(() => export.IsCompleted, "the .svz export", 60_000);
            }, null),
            (() =>
            {
                expect(export is { IsCompletedSuccessfully: true }, $"the .svz export finished ({export?.Exception?.GetBaseException().Message})");
                svzPath = export!.Result;
                expect(svzPath != null && File.Exists(svzPath), "the .svz file exists");
                using (var zip = System.IO.Compression.ZipFile.OpenRead(svzPath!))
                {
                    string top = Path.GetFileName(work);
                    expect(zip.Entries.All(x => x.FullName.StartsWith(top + "/", StringComparison.Ordinal)), "one top folder in the package");
                    expect(zip.GetEntry(top + "/" + SvzManifest.FileName) != null, "manifest in the package");
                }

                // first import (keep both if the song select smoke already dropped this song); then the same package
                // again through song select → name clash → Replace / Keep both dialog
                firstImport = Library.ImportAsync(svzPath!, new SongImportOptions { Existing = ExistingSongPolicy.Rename });
                waitUntil(() => firstImport.IsCompleted, "the first .svz import", 60_000);
            }, null),
            (() =>
            {
                expect(firstImport is { IsCompletedSuccessfully: true }, $"the .svz imported ({firstImport?.Exception?.GetBaseException().Message})");
                expect(firstImport!.Result.Kind == SongImportKind.Svz && firstImport.Result.Manifest != null, "imported as a package with its manifest");
                firstName = firstImport.Result.Song.FolderName;
                libraryBefore = Library.Songs.Count(x => x.Source == Core.Library.SongSource.Editor);
                var songSelect = ed().GetParentScreen() as SongSelectScreen ?? throw new InvalidOperationException("smoke test: song select not under the editor");
                reimport = songSelect.ImportFiles(new[] { svzPath! });
                waitUntil(() => dialogOverlay.Current is ImportConflictDialog || reimport.IsCompleted, "the Replace / Keep both dialog for the second import", 30_000);
            }, null),
            (() =>
            {
                var dialog = dialogOverlay.Current as ImportConflictDialog ?? throw new InvalidOperationException("smoke test: Replace / Keep both dialog not shown for a second import");
                dialog.KeepBoth();
                waitUntil(() => reimport!.IsCompleted, "the second import", 60_000);
            }, null),
            (() =>
            {
                expect(reimport is { IsCompletedSuccessfully: true }, "the second import finished");
                var songSelect = ed().GetParentScreen() as SongSelectScreen ?? throw new InvalidOperationException("smoke test: song select not under the editor");
                var names = Library.Songs.Where(x => x.Source == Core.Library.SongSource.Editor).Select(x => x.FolderName).ToList();
                string name = Path.GetFileName(work);
                expect(names.Count == libraryBefore + 1 && names.Contains(name), $"re-import kept both ({string.Join(", ", names)})");
                var copy = Library.Songs.First(x => string.Equals(x.FolderPath, songSelect.LastImportedFolder, StringComparison.OrdinalIgnoreCase));
                expect(copy.FolderName != firstName && copy.FolderName.StartsWith(name, StringComparison.Ordinal), $"the second import got its own folder ({copy.FolderName})");
                var song = SongFolder.Load(copy.FolderPath);
                expect(song.Charts.Any(c => c.Chart.Editor.Bookmarks.Any(b => b.Value.Label == "Part B - mapper2")), "the re-imported copy carries the labelled bookmark");
                expect(!File.Exists(Path.Combine(copy.FolderPath, SvzManifest.FileName)), "no manifest left in the imported folder");
                report($"collab: exported {Path.GetFileName(svzPath)} ({new FileInfo(svzPath!).Length} bytes), imported as '{firstName}', re-imported through the Replace / Keep both dialog as '{copy.FolderName}' - ok");
            }, null),

            // ---- reference difficulty
            (() =>
            {
                var e = ed();
                var other = e.Session.Charts.First(c => c != e.Session.ActiveChart);
                expect(e.OpenReference(other), "reference opened");
                var s = e.ReferenceSession!;
                refNotesBefore = notes(s.Chart);
                mainNotesBefore = notes(e.Session.Chart);

                // view a stretch with notes in the reference
                var first = s.Chart.Notes.Bt.SelectMany(l => l).OrderBy(n => n.Y).Skip(Math.Min(40, refNotesBefore / 3)).First();
                e.Playback.CurrentTime.Value = s.PulseToMs(first.Y) + 200;
            }, null),
            (() =>
            {
                var e = ed();
                var content = e.ComposeContent;
                var main = content.PlayfieldContainer.ScreenSpaceDrawQuad.AABBFloat;
                var refq = content.ReferenceContainer.ScreenSpaceDrawQuad.AABBFloat;
                var density = content.DensityContainer.ScreenSpaceDrawQuad.AABBFloat;
                var screen = e.ScreenSpaceDrawQuad.AABBFloat;
                expect(refq.Left >= main.Right && density.Right <= main.Left + 1, "density | main | reference side by side, no overlap");
                expect(density.Left >= screen.Left && refq.Right <= screen.Right, inv($"the layout fits the window ({density.Left:0}..{refq.Right:0} of {screen.Width:0})"));
                expect(main.Centre.X < screen.Centre.X && refq.Centre.X > screen.Centre.X, "main chart on the left, reference on the right");
                var fieldWidth = content.ReferenceContainer.ScreenSpaceDrawQuad.Width;
                var playWidth = e.ReferencePlayfield!.ScreenSpaceDrawQuad.Width;
                expect(fieldWidth > playWidth + 1, "the reference keeps overhang space for wide lasers too");
                expectClearOfToolbars(e, main, "main playfield + overhang");
                expectClearOfToolbars(e, refq, "reference playfield + overhang");

                // box-select a measure of the reference (same tools as the mouse), then click one note
                var s = e.ReferenceSession!;
                var rc = e.ReferenceCompose!;
                long from = s.Timing.MeasureStart(s.Timing.MeasureAt((long)s.MsToPulse(e.Playback.CurrentTime.Value - 150)));
                long to = s.Timing.MeasureStart(s.Timing.MeasureAt(from) + 1) - 1;
                var pf = e.ReferencePlayfield!;
                expect(rc.PointerDown(pf.PointerAt(from, 1), PointerButton.Left, false, false), "box select starts in the reference");
                rc.PointerDrag(pf.PointerAt(to, PlayfieldLayout.TOTAL_WIDTH - 1), false);
                rc.PointerUp(pf.PointerAt(to, PlayfieldLayout.TOTAL_WIDTH - 1));
                copied = s.Selection.Count;
                expect(copied > 0, "box selection in the reference");
                expect(e.Session.Selection.IsEmpty, "selecting in the reference clears the main selection");
            }, "21-reference-view"),
            (() =>
            {
                var e = ed();
                var s = e.ReferenceSession!;
                // locked: dragging a selected reference note does not move it
                var n = s.Selection.Notes.FirstOrDefault();
                if (n != null)
                {
                    float x = PlayfieldLayout.LaneLeft(n.Kind, n.Lane) + PlayfieldLayout.LaneWidth(n.Kind) / 2;
                    var rc = e.ReferenceCompose!;
                    var pf = e.ReferencePlayfield!;
                    long y0 = n.Note.Y;
                    if (rc.PointerDown(pf.PointerAt(y0, x), PointerButton.Left, false, false))
                    {
                        rc.PointerDrag(pf.PointerAt(y0 + 960, x), false);
                        rc.PointerUp(pf.PointerAt(y0 + 960, x));
                    }

                    expect(s.Chart.Notes.Lane(n.Kind, n.Lane).Contains(n.Note) && n.Note.Y == y0, "the reference is read-only (a drag does not move)");
                    rc.PointerDown(pf.PointerAt(n.Note.Y, x), PointerButton.Right, false, false);
                    expect(notes(s.Chart) == refNotesBefore, "right click does not delete in the reference");
                }

                e.TriggerAction(EditorAction.Copy);
                expect(EditorSession.Clipboard is { IsEmpty: false }, "Ctrl+C copied from the reference");
                expect(e.LastToast.Contains("reference"), "the toast says it came from the reference");

                // paste into the main chart in an empty stretch after its last event
                var main = e.Session;
                int m = main.Timing.MeasureAt(main.Chart.LastEventPulse()) + 4;
                p0 = main.Timing.MeasureStart(m);
                beat = main.Timing.BeatLength(m);
                e.Playback.LengthMs = main.PulseToMs(p0 + 40 * beat);
                e.Playback.CurrentTime.Value = main.PulseToMs(p0);
                e.TriggerAction(EditorAction.Paste);
                int pastedNotes = EditorSession.Clipboard!.Notes.Count;
                expect(notes(main.Chart) == mainNotesBefore + pastedNotes, $"Ctrl+V pasted the reference pattern into the main chart ({pastedNotes} notes)");
                expect(notes(s.Chart) == refNotesBefore, "the reference chart is unchanged");
                report($"collab: reference {s.Chart.Meta.DifficultyDisplayName} opened beside {main.Chart.Meta.DifficultyDisplayName}, box-selected {copied}, read-only checked, copied and pasted {pastedNotes} notes + {EditorSession.Clipboard.Lasers.Count} laser sections - ok");
            }, "21b-reference-pasted"),
            (referenceInputMarker, null), // the same with the reference open, plus a click in the reference
            (() =>
            {
                var e = ed();
                e.TriggerAction(EditorAction.Undo);
                expect(notes(e.Session.Chart) == mainNotesBefore, "undo removed the paste");
                e.TriggerAction(EditorAction.Exit);
                expect(e.ReferenceSession == null && screenStack.CurrentScreen == e, "Esc closes the reference, not the editor");
                expect(!e.ComposeContent.ReferenceOpen, "layout back to one playfield");
            }, null),

            // ---- laser points from outside the lane area
            (() =>
            {
                var e = ed();
                var s = e.Session;
                e.SelectTool(EditorTool.Laser);
                e.Tools.LaserSide.Value = 0;
                var c = e.Compose;
                float outsideL = PlayfieldLayout.LANES_LEFT - PlayfieldLayout.LASER_REACH / 2;
                float outsideR = PlayfieldLayout.LANES_RIGHT + PlayfieldLayout.LASER_REACH / 2;

                // normal laser: clamps to 0 / 1 in the LASER_REACH margin
                long y = p0 + 16 * beat;
                expect(c.PointerDown(at(y, outsideL), PointerButton.Left, false, false), "laser drag starts outside the lanes");
                c.PointerDrag(at(y + 2 * beat, outsideR), false);
                c.PointerUp(at(y + 2 * beat, outsideR));
                var sec = s.Chart.Notes.Lasers[0].Single(x => x.Y == y);
                expect(sec.Points[0].V == 0 && sec.Points[^1].V == 1, inv($"normal laser clamps to 0 / 1 outside ({sec.Points[0].V}, {sec.Points[^1].V})"));

                // wide: true 2x scale, 0.05 and 0.95 lie past the lanes (and past the playfield drawable)
                float x05 = PlayfieldLayout.LaserX(0.05, true), x95 = PlayfieldLayout.LaserX(0.95, true);
                expect(x05 < PlayfieldLayout.LANES_LEFT - PlayfieldLayout.LASER_REACH && x95 > PlayfieldLayout.LANES_RIGHT + PlayfieldLayout.LASER_REACH, inv($"wide 0.05 / 0.95 lie beyond the lanes at true scale (x {x05:0} / {x95:0})"));
                foreach (float x in new[] { x05, x95, PlayfieldLayout.LaserX(0, true) - PlayfieldLayout.LASER_HALF_WIDTH, PlayfieldLayout.LaserX(1, true) + PlayfieldLayout.LASER_HALF_WIDTH })
                {
                    var sp = e.Playfield.ToScreenSpace(new Vector2(x, e.Playfield.DrawHeight / 2));
                    expect(e.Playfield.ReceivePositionalInputAt(sp), inv($"the laser tool receives clicks at x {x:0} (wide overhang)"));
                    expect(!e.Density.ScreenSpaceDrawQuad.Contains(sp), inv($"x {x:0} is not over the density column"));
                    expect(e.ComposeContent.PlayfieldContainer.ScreenSpaceDrawQuad.Contains(sp), inv($"x {x:0} lies in the playfield's overhang space"));
                }
                expectClearOfToolbars(e, e.ComposeContent.PlayfieldContainer.ScreenSpaceDrawQuad.AABBFloat, "centred playfield + overhang");

                long w = y + 4 * beat;
                expect(c.PointerDown(at(w, x05), PointerButton.Left, true, true), "wide laser drag starts at 0.05");
                c.PointerDrag(at(w + 2 * beat, x95), true);
                c.PointerUp(at(w + 2 * beat, x95));
                var wide = s.Chart.Notes.Lasers[0].Single(x => x.Y == w);
                expect(wide.Width == 2 && Math.Abs(wide.Points[0].V - 0.05) < 1e-4 && Math.Abs(wide.Points[^1].V - 0.95) < 1e-4,
                    inv($"wide laser 0.05 -> 0.95 placed past both edges ({wide.Points[0].V}, {wide.Points[^1].V})"));

                // hit-testing points and the path out there
                var hitL = c.LaserPointAt(at(w, x05));
                var hitR = c.LaserPointAt(at(w + 2 * beat, x95));
                expect(hitL is { } hl && hl.Section == wide && hl.Index == 0, "the wide point at 0.05 is hit");
                expect(hitR is { } hr && hr.Section == wide && hr.Index == wide.Points.Count - 1, "the wide point at 0.95 is hit");
                long early = w + beat / 8;
                expect(c.LaserSegmentAt(at(early, PlayfieldLayout.LaserX(LaserMath.ValueAt(wide, early)!.Value, true))) is { } seg && seg.Section == wide, "the wide path past the edge is hit");

                // screenshot: the wide laser crossing both overhangs, ghost on the right overhang (wide mode on)
                e.Compose.WideLaser.Value = true;
                e.Playback.CurrentTime.Value = s.PulseToMs(w + beat) - 500;
                c.PointerMove(at(w + 3 * beat, PlayfieldLayout.LaserX(0.97, true)));
                report(inv($"collab: normal laser clamps 0/1 {PlayfieldLayout.LASER_REACH:0} px outside; wide laser 0.05 -> 0.95 at true scale (x {x05:0} .. {x95:0}, lanes {PlayfieldLayout.LANES_LEFT:0}..{PlayfieldLayout.LANES_RIGHT:0}), hit-tests ok, overhang clear of density/toolbars"));
            }, "22-laser-outside"),
            (() =>
            {
                var e = ed();
                e.TriggerAction(EditorAction.Undo);
                e.TriggerAction(EditorAction.Undo);
                e.Compose.WideLaser.Value = false;
                e.SelectTool(EditorTool.Select);
                e.Exit();
            }, null),
        };

        // compose tools through the real input path, borderless at the screen size and windowed 1600x900, without and
        // with a reference open (EditorGame.SmokeComposeInput.cs)
        steps.InsertRange(steps.FindIndex(x => x.Item1 == mainInputMarker) + 1, composeInputAtSizes(ed, "no reference", false));
        steps.InsertRange(steps.FindIndex(x => x.Item1 == referenceInputMarker) + 1, composeInputAtSizes(ed, "reference open", true));
        return steps;
    }

    /// <summary>A playfield's area (with its wide-laser overhang) must not lie under the compose toolbox or toggle bar.</summary>
    private static void expectClearOfToolbars(EditorScreen e, osu.Framework.Graphics.Primitives.RectangleF field, string what)
    {
        foreach (var bar in e.ChildrenOfType<ComposeToolbox>().Cast<osu.Framework.Graphics.Drawable>().Concat(e.ChildrenOfType<ComposeToggleBar>()))
        {
            var r = bar.ScreenSpaceDrawQuad.AABBFloat;
            expect(!r.IntersectsWith(field), inv2($"{what} ({field.Left:0}..{field.Right:0}) is clear of the {bar.GetType().Name} ({r.Left:0}..{r.Right:0})"));
        }

        static string inv2(FormattableString f) => f.ToString(CultureInfo.InvariantCulture);
    }
}
