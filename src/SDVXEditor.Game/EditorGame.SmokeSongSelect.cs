using osu.Framework.Logging;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osuTK;
using osuTK.Input;
using SDVXEditor.Core.Radar;
using SDVXEditor.Game.Configuration;
using SDVXEditor.Core.Library;
using SongSource = SDVXEditor.Core.Library.SongSource;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Screens.SongSelect;
using SDVXEditor.Game.Songs;

namespace SDVXEditor.Game;

/// <summary>
/// --smoke-test, song select part: real library (if any), selection, search, grouping, quick filters, a
/// simulated file drop, the launcher delete refusal, a kson export, and the editor opened from the wheel.
/// <para>
/// The smoke test runs with its own editor root (<c>--songs</c>, else a temp folder; see
/// <see cref="LibraryManager.FromStartup"/>), so the import and export never touch the user's songs. The
/// dropped folder is <c>SDVXEDITOR_SMOKE_DROP</c>, else a temp copy of the first library song.
/// </para>
/// </summary>
public partial class EditorGame
{
    /// <summary>Environment variable: folder/zip/chart/audio file the smoke test drops onto song select.</summary>
    public const string SmokeDropVariable = "SDVXEDITOR_SMOKE_DROP";

    private bool songSelectSmokeDone;

    /// <param name="Waiting">On a timeout: what the step was still waiting for (state for the failure message).</param>
    private sealed record SmokeStep(string Name, Func<bool> Ready, Action Act, string? Shot = null, double TimeoutMs = 30_000, Func<string?>? Waiting = null);

    /// <summary>The footer counter matches what the wheel lists (audit B4).</summary>
    private void checkCounter(SongSelectScreen s, string when)
    {
        int songs = s.Wheel.Items.Select(i => i.SongKey).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        expect(s.CountText.StartsWith(songs == 1 ? "1 song" : $"{songs} ", StringComparison.Ordinal) || s.CountText.StartsWith($"{songs} of ", StringComparison.Ordinal),
            $"the song counter {when} counts the {songs} listed song(s) ('{s.CountText}')");
    }

    /// <summary>Details rows: every value stays right of its label inside the left panel (audit B2).</summary>
    private void checkDetailsLayout(SongSelectScreen s)
    {
        foreach (var row in s.ChildrenOfType<SongSelectScreen.DetailRow>())
        {
            var label = row.Label.ScreenSpaceDrawQuad;
            var value = row.Value.ScreenSpaceDrawQuad;
            expect(value.TopLeft.X >= label.TopRight.X + 1,
                $"the {row.Label.Text} value starts right of its label (value from x {value.TopLeft.X:0}, label ends at {label.TopRight.X:0})");
            // coordinator finding: the radar column covered the right-aligned values
            float radarLeft = s.ChartPanel.ScreenSpaceDrawQuad.TopLeft.X;
            expect(value.TopRight.X <= radarLeft, $"the {row.Label.Text} value ends before the radar column (value to x {value.TopRight.X:0}, radar from x {radarLeft:0})");
        }
    }

    /// <summary>No wheel panel is drawn under the search line (audit B3): the wheel area is clipped there.</summary>
    private void checkWheelClip(SongSelectScreen s)
    {
        var clip = s.Wheel.Parent!.ScreenSpaceDrawQuad;
        float top = s.ToScreenSpace(new Vector2(0, SongSelectScreen.wheel_clip_top)).Y;
        expect(Math.Abs(clip.TopLeft.Y - top) < 1.5f, $"the wheel's visible area starts below the search line (y {clip.TopLeft.Y:0} vs {top:0})");
    }

    private void runSongSelectSmoke(string? shots, Action then)
    {
        double interval = shots != null ? 900 : 250;

        SongSelectScreen ss() => screenStack.CurrentScreen as SongSelectScreen
                                 ?? throw new InvalidOperationException($"smoke test: expected song select, got {screenStack.CurrentScreen?.GetType().Name ?? "nothing"}");

        int total = 0;
        string? dropPath = null;
        string? importedFolder = null;
        string? launcherFolder = null, launcherFingerprint = null, forkFolder = null;
        bool dropInsideRoot = false, conflictAnswered = false, editingFromLibrary = false;
        double volumeBefore = 0, volumeOriginal = 0;
        int volumeDirection = 1;

        // a drop finished: imported (LastImportedFolder set) or refused with an error toast
        bool dropSettled()
        {
            var s = ss();
            if (dialogOverlay.Current is ImportConflictDialog conflict)
            {
                // the editor root already has a song of that name (an earlier run with the same --songs): keep both
                Logger.Log("smoke: the drop met an editor song of the same name - answering Keep both", LoggingTarget.Runtime, LogLevel.Important);
                conflictAnswered = true;
                conflict.KeepBoth();
                return false;
            }

            // the new song is selected once the library has rescanned (SongSelectScreen.pendingSelectFolder →
            // onLibraryChanged); right after the import that rescan may still be on its way (Canto IX, Keep both: the
            // check ran in the same frame as the import's end and saw the old song selected)
            return !s.IsBusy && (s.Toasts.LastMessage.StartsWith("Could not", StringComparison.Ordinal)
                                 || s.LastImportedFolder is string imported
                                 && string.Equals(s.Wheel.Selected.Value?.FolderPath, imported, StringComparison.OrdinalIgnoreCase));
        }

        var steps = new List<SmokeStep>
        {
            new SmokeStep("library loads", () => screenStack.CurrentScreen is SongSelectScreen s && s.LibraryLoaded, () =>
            {
                var s = ss();
                total = s.Wheel.Items.Count;
                bool launcherSongs = Library.Library.Roots.Any(r => r.Source == SongSource.Launcher && Directory.Exists(r.Path)
                                                                       && Directory.EnumerateDirectories(r.Path).Any());
                Logger.Log($"smoke: library lists {total} charts of {Library.Songs.Count} songs (launcher root with songs: {launcherSongs})", LoggingTarget.Runtime, LogLevel.Important);
                expect(!launcherSongs || total > 0, "the launcher library has song folders but the wheel is empty");
            }, "s01-library"),

            new SmokeStep("select", () => true, () => ss().Wheel.SelectSetRelative(2)),

            new SmokeStep("selected is centred", () => true, () =>
            {
                var s = ss();
                if (s.Wheel.Selected.Value == null) return;

                float off = s.Wheel.SelectedOffsetFromCentre;
                expect(Math.Abs(off) < 4, $"selected panel is {off:0} px off the wheel centre");
            }, "s02-selected"),

            new SmokeStep("radar and level triangles", () => radarSettled(ss()), () => checkRadarAndLevels(ss()), "s02b-radar-levels", 30_000, () => radarWaiting(ss())),
            new SmokeStep("next song", () => true, () => ss().Wheel.SelectSetRelative(1)),
            new SmokeStep("radar of the next song", () => radarSettled(ss()), () => checkRadarAndLevels(ss()), "s02c-radar-levels", 30_000, () => radarWaiting(ss())),
            new SmokeStep("another song", () => true, () => ss().Wheel.SelectSetRelative(1)),
            new SmokeStep("radar of another song", () => radarSettled(ss()), () => checkRadarAndLevels(ss()), "s02d-radar-levels", 30_000, () => radarWaiting(ss())),

            new SmokeStep("alt+wheel bottom-right", () => true, () =>
            {
                // real input: Alt held, one wheel notch over the footer's bottom-right corner (outside the wheel)
                var s = ss();
                var im = SmokeInput ?? throw new InvalidOperationException("smoke test: no smoke input manager");
                im.UseParentInput = false;
                volumeBefore = volumeOriginal = config.Get<double>(EditorSetting.VolumeMaster);
                volumeDirection = volumeBefore >= 0.95 ? -1 : 1;
                im.MoveMouseTo(s.ToScreenSpace(new Vector2(s.DrawWidth - 40, s.DrawHeight - 30)));
                Scheduler.AddDelayed(() =>
                {
                    im.PressKey(Key.AltLeft);
                    im.ScrollVerticalBy(volumeDirection);
                    im.ReleaseKey(Key.AltLeft);
                }, 60);
            }),

            new SmokeStep("volume changed", () => Math.Abs(config.Get<double>(EditorSetting.VolumeMaster) - volumeBefore) > 0.001, () =>
            {
                var s = ss();
                double now = config.Get<double>(EditorSetting.VolumeMaster);
                expect(Math.Abs(now - (volumeBefore + volumeDirection * 0.05)) < 0.011, $"Alt+wheel bottom-right moved master volume one 5 % step ({volumeBefore:0.00} -> {now:0.00})");
                expect(s.Volume?.Alpha > 0, "the volume overlay shows");
                s.Volume!.Adjust(0); // keep it up for the screenshot (same value)

                // over the wheel the wheel keeps the scroll: no volume change
                var im = SmokeInput!;
                volumeBefore = now;
                im.MoveMouseTo(s.Wheel.ScreenSpaceDrawQuad.Centre);
                Scheduler.AddDelayed(() =>
                {
                    im.PressKey(Key.AltLeft);
                    im.ScrollVerticalBy(volumeDirection);
                    im.ReleaseKey(Key.AltLeft);
                }, 60);
            }, "s02e-volume", 5_000, () => $"master volume still {config.Get<double>(EditorSetting.VolumeMaster):0.00}"),

            new SmokeStep("volume restored", () => true, () =>
            {
                double now = config.Get<double>(EditorSetting.VolumeMaster);
                expect(Math.Abs(now - volumeBefore) < 0.001, $"Alt+wheel over the wheel leaves the volume alone ({volumeBefore:0.00} -> {now:0.00})");
                config.SetValue(EditorSetting.VolumeMaster, volumeOriginal);
                expect(config.Get<double>(EditorSetting.VolumeMaster) == volumeOriginal, "the master volume setting is restored");
                SmokeInput!.UseParentInput = true;
                Logger.Log($"smoke: volume put back to {config.Get<double>(EditorSetting.VolumeMaster):0.00}", LoggingTarget.Runtime, LogLevel.Important);
            }),

            new SmokeStep("search", () => true, () =>
            {
                var s = ss();
                var sel = s.Wheel.Selected.Value;
                if (sel == null) return;

                string term = sel.Title.Split(' ', StringSplitOptions.RemoveEmptyEntries).OrderByDescending(w => w.Length).First();
                s.Search.Value = term;
                expect(s.Wheel.Items.Count > 0 && s.Wheel.Items.All(i => SongListQuery.Matches(i, term)), $"search \"{term}\" lists only matches");
                expect(s.Wheel.Items.Any(i => i.SongKey == sel.SongKey), $"search \"{term}\" keeps the song it came from");
                checkCounter(s, "after a search"); // audit B4: the counter ignored the search
            }, "s03-search"),

            new SmokeStep("group by source", () => true, () =>
            {
                var s = ss();
                s.Search.Value = string.Empty;
                expect(s.Wheel.Items.Count == total, "clearing the search lists everything again");
                s.Grouping.Value = SongGrouping.BySource;
                expect(total == 0 || s.Wheel.Groups.Count >= 1, "grouping by source makes groups");
                expect(s.Wheel.Items.Count == total, "grouping keeps every chart");
            }, "s04-group-source"),

            // audit U24: a group header scrolled away is pinned at the top (one frame later, after the wheel's update)
            new SmokeStep("group by source shows its group", () => true, () =>
            {
                var s = ss();
                expect(total == 0 || s.Wheel.StickyGroupTitle != null || s.Wheel.LivePanelCount > 0,
                    "a group header (sticky or in the list) says which group the songs are in");
                checkWheelClip(s);
            }),

            new SmokeStep("group by level", () => true, () =>
            {
                var s = ss();
                s.Grouping.Value = SongGrouping.ByLevel;
                expect(s.Wheel.Items.Count == total, "level grouping keeps every chart");
            }, "s04b-group-level"),

            new SmokeStep("my charts tab", () => true, () =>
            {
                var s = ss();
                s.Grouping.Value = SongGrouping.BySongFolder;
                s.SelectTab(SongSelectTab.MyCharts);
                expect(s.Wheel.Items.All(i => i.Source == Screens.SongSelect.SongSource.Editor), "My charts lists editor songs only");
                s.SelectTab(SongSelectTab.NoGrouping);
                expect(s.Grouping.Value == SongGrouping.NoGrouping && s.Filter.Value == QuickFilter.None, "No grouping resets grouping and filter");
                expect(s.Wheel.Items.Count == total, "No grouping lists everything");
            }),

            new SmokeStep("drop", () => true, () =>
            {
                dropPath = Environment.GetEnvironmentVariable(SmokeDropVariable) is { Length: > 0 } p ? p : tempCopyOfFirstSong();
                if (dropPath == null)
                {
                    Logger.Log("smoke: nothing to drop (empty library, no SDVXEDITOR_SMOKE_DROP)", LoggingTarget.Runtime, LogLevel.Important);
                    return;
                }

                // a song folder lying directly in a library root is not copied: song select selects the existing song
                string full = normPath(dropPath);
                dropInsideRoot = Directory.Exists(full) && Library.Library.Roots.Any(r => string.Equals(normPath(r.Path), Path.GetDirectoryName(full), StringComparison.OrdinalIgnoreCase));
                Logger.Log($"smoke: dropping {dropPath}{(dropInsideRoot ? " (already a library song)" : "")}", LoggingTarget.Runtime, LogLevel.Important);
                onDragDrop(dropPath); // the same path a real window drop takes
            }),

            new SmokeStep("drop imported", () => dropPath == null || dropSettled(), () =>
            {
                if (dropPath == null) return;

                var s = ss();
                expect(!s.Toasts.LastMessage.StartsWith("Could not", StringComparison.Ordinal), "import failed: " + s.Toasts.LastMessage);
                importedFolder = s.LastImportedFolder!;
                expect(string.Equals(s.Wheel.Selected.Value?.FolderPath, importedFolder, StringComparison.OrdinalIgnoreCase),
                    $"the imported song is selected (selected: {s.Wheel.Selected.Value?.FolderPath ?? "nothing"})");

                if (dropInsideRoot)
                {
                    expect(string.Equals(normPath(importedFolder), normPath(dropPath), StringComparison.OrdinalIgnoreCase),
                        $"a drop of a library song selects that song instead of copying it ({importedFolder})");
                    expect(s.Toasts.LastMessage.Contains("already in the library", StringComparison.Ordinal), "the toast says the song is already in the library: " + s.Toasts.LastMessage);
                }
                else
                    expect(importedFolder.StartsWith(Library.Library.EditorRoot, StringComparison.OrdinalIgnoreCase), "imports land in the editor root");

                checkCounter(s, "after the drop import"); // audit B4: it said "3 songs" with 4 listed
                checkDetailsLayout(s);
                Logger.Log($"smoke: drop -> {importedFolder}{(conflictAnswered ? " (name clash answered with Keep both)" : "")}", LoggingTarget.Runtime, LogLevel.Important);
            }, "s05-dropped", 60_000, () => $"busy {ss().IsBusy}, last imported {ss().LastImportedFolder ?? "none"}, selected {ss().Wheel.Selected.Value?.FolderPath ?? "nothing"}, dialog {dialogOverlay.Current?.GetType().Name ?? "none"}, last toast \"{ss().Toasts.LastMessage}\""),

            new SmokeStep("launcher delete refused", () => true, () =>
            {
                var s = ss();
                var launcher = s.Wheel.Items.FirstOrDefault(i => i.Source == Screens.SongSelect.SongSource.Launcher);
                if (launcher == null) return;

                s.Wheel.Selected.Value = launcher;
                s.OpenOptionsForSelected();
                s.Options.Press(2);
                expect(!s.Options.IsOpen, "Delete on a launcher song closes the overlay");
                expect(s.Toasts.LastMessage.Contains("launcher", StringComparison.OrdinalIgnoreCase), "Delete on a launcher song is refused with a message");
                expect(Directory.Exists(launcher.FolderPath!), "the launcher song is still there");
            }, "s06-delete-refused"),

            new SmokeStep("export page", () => true, () =>
            {
                var s = ss();
                if (importedFolder != null) s.Wheel.SelectByKey(importedFolder);
                s.OpenOptionsForSelected();
                s.Options.Press(5);
                expect(s.Wheel.Selected.Value == null || s.Options.Page == OptionsPage.Export, "5 opens the export page");
                // audit B7: the popup's title lies on an opaque band, not over the song header's white text
                var band = s.Options.HeaderBand.ScreenSpaceDrawQuad;
                var q = s.Options.Question.ScreenSpaceDrawQuad;
                expect(band.Contains(q.TopLeft) && band.Contains(q.BottomRight) || s.Wheel.Selected.Value == null, "the popup's question sits on its own dark band");
            }, "s06b-export-menu"),

            new SmokeStep("export kson", () => true, () =>
            {
                var s = ss();
                if (importedFolder == null || s.Wheel.Selected.Value == null)
                {
                    s.Options.Close();
                    return;
                }

                s.Options.Press(1); // .kson
            }),

            new SmokeStep("exported", () => !ss().IsBusy, () =>
            {
                var s = ss();
                if (importedFolder == null) return;

                expect(s.LastExportPath != null && Directory.Exists(s.LastExportPath), "kson export wrote a folder: " + s.Toasts.LastMessage);
                expect(Directory.EnumerateFiles(s.LastExportPath!, "*.kson").Any(), "the export holds .kson charts");
                expect(!s.LastExportPath!.Contains("data_mods", StringComparison.OrdinalIgnoreCase), "exports never go into data_mods");
            }, "s06c-exported", 60_000),

            new SmokeStep("edit from library", () => true, () =>
            {
                var s = ss();
                if (importedFolder != null) s.Wheel.SelectByKey(importedFolder);
                editingFromLibrary = s.Wheel.Selected.Value != null;
                if (!editingFromLibrary)
                {
                    Logger.Log("smoke: the library is empty and nothing was dropped - skipping the editor-from-library steps", LoggingTarget.Runtime, LogLevel.Important);
                    return;
                }

                s.EditSelected();
            }),

            new SmokeStep("editor open", () => !editingFromLibrary || screenStack.CurrentScreen is EditorScreen, () =>
            {
                if (!editingFromLibrary) return;

                var editor = (EditorScreen)screenStack.CurrentScreen;
                if (importedFolder != null)
                {
                    expect(string.Equals(editor.Session.Song?.FolderPath, importedFolder, StringComparison.OrdinalIgnoreCase), "the editor opened the selected song");
                    expect(!editor.Session.SavesAsCopy, "an editor song is saved in place");
                }
            }, "s07-editor-from-library"),

            new SmokeStep("back", () => true, () =>
            {
                if (editingFromLibrary) ((EditorScreen)screenStack.CurrentScreen).Exit();
            }),

            new SmokeStep("back at song select", () => screenStack.CurrentScreen is SongSelectScreen, () =>
            {
                var s = ss();
                if (importedFolder != null)
                    expect(string.Equals(s.Wheel.Selected.Value?.FolderPath, importedFolder, StringComparison.OrdinalIgnoreCase), "the song stays selected after the editor");
            }, "s08-back"),

            new SmokeStep("edit a launcher song", () => true, () =>
            {
                var s = ss();
                var launcher = s.Wheel.Items.FirstOrDefault(i => i.Source == Screens.SongSelect.SongSource.Launcher);
                if (launcher == null) return;

                launcherFolder = launcher.FolderPath;
                launcherFingerprint = SongLibrary.Fingerprint(launcherFolder!);
                s.Wheel.Selected.Value = launcher;
                s.EditSelected();
            }),

            new SmokeStep("launcher song saves as a copy", () => launcherFolder == null || screenStack.CurrentScreen is EditorScreen, () =>
            {
                if (launcherFolder == null) return;

                var editor = (EditorScreen)screenStack.CurrentScreen;
                expect(editor.Session.SavesAsCopy, "a launcher song is opened with fork-on-save");
                // the fork lands in the smoke test's own editor root; the launcher folder is only read
                string saved = editor.Session.Save();
                string fork = Path.GetDirectoryName(saved)!;
                expect(fork.StartsWith(Library.Library.EditorRoot, StringComparison.OrdinalIgnoreCase), $"the save went into the editor root ({saved})");
                expect(!editor.Session.SavesAsCopy, "the fork is made once");
                expect(SongLibrary.Fingerprint(launcherFolder) == launcherFingerprint, "the launcher folder is untouched");
                forkFolder = fork;
                editor.Exit();
            }),

            new SmokeStep("fork selected", () => forkFolder == null || (screenStack.CurrentScreen is SongSelectScreen s && !s.IsBusy
                                                                       && string.Equals(s.Wheel.Selected.Value?.FolderPath, forkFolder, StringComparison.OrdinalIgnoreCase)), () => { }, "s09-fork-selected", 30_000),
        };

        runSmokeSteps(steps, 0, interval, shots, then);
    }

    /// <summary>The radar shows the selected chart's values (or there is no chart file to compute them from).</summary>
    private static bool radarSettled(SongSelectScreen s)
    {
        var sel = s.Wheel.Selected.Value;
        if (sel == null) return true;
        string? path = RadarCache.ChartPath(sel);
        return path == null || (s.Radar.ChartPath == path && s.Radar.Values != null);
    }

    private static string radarWaiting(SongSelectScreen s)
        => $"selected {s.Wheel.Selected.Value?.ToString() ?? "nothing"}, radar shows {s.Radar.ChartPath ?? "nothing"} = {s.Radar.Values?.ToString() ?? "no values"}";

    private static void checkRadarAndLevels(SongSelectScreen s)
    {
        expect(s.ExitLabel == "EXIT", $"the footer's left button reads EXIT (\"{s.ExitLabel}\")");

        var sel = s.Wheel.Selected.Value;
        if (sel == null) return;

        // radar: the selected chart's values, the same as a fresh computation
        if (RadarCache.ChartPath(sel) is string path)
        {
            var shown = s.Radar.Values!.Value;
            var fresh = RadarCache.Compute(path);
            expect(shown == fresh, $"the radar shows the selected chart's values ({shown} vs {fresh})");
            expect(sel.Chips + sel.Holds == 0 || shown.Notes > 0, "a chart with notes has a NOTES value");
            Logger.Log($"smoke: radar {Path.GetFileName(path)}: {shown}", LoggingTarget.Runtime, LogLevel.Important);
        }

        // the radar's bounds hold all six labels with their values (a spotlight around it must enclose NOTES and TRICKY),
        // and no label crowds another or the hexagon
        var box = s.Radar.ScreenSpaceDrawQuad.AABBFloat;
        var hex = s.Radar.HexagonScreenCorners;
        var blocks = s.Radar.LabelBlocks.Select(b => b.ScreenSpaceDrawQuad.AABBFloat).ToList();
        for (int i = 0; i < blocks.Count; i++)
        {
            var r = blocks[i];
            expect(box.Contains(r.TopLeft) && box.Contains(r.BottomRight), $"radar label {i} lies inside the radar's bounds ({r} in {box})");
            for (int j = i + 1; j < blocks.Count; j++)
                expect(!r.IntersectsWith(blocks[j]), $"radar labels {i} and {j} do not overlap");
            expect(hex.All(c => !r.Inflate(-1).Contains(c)), $"radar label {i} keeps off the hexagon's corners");
        }

        // triangles: NOV ADV EXH INF-class MXM, each of the song's charts with its level, the selected one highlighted
        var badges = s.LevelBadges.Badges;
        expect(badges.Count == 5, $"five level triangles ({badges.Count})");
        for (int i = 0; i < badges.Count; i++)
            expect(badges[i].Slot.ToCore() == i, $"triangle {i} is slot {i} ({badges[i].Slot})");

        var songCharts = s.Wheel.Items.Where(i => i.SongKey == sel.SongKey).ToList();
        foreach (var chart in songCharts)
        {
            var badge = badges[chart.Slot.ToCore()];
            expect(badge.Item != null, $"{chart.Slot.ShortName()} has a filled triangle");
            if (badge.Item!.ChartKey == chart.ChartKey)
                expect(badge.LevelText == Core.Charts.Difficulty.FormatLevel(chart.DeclaredLevel), $"{chart.Slot.ShortName()} shows level {badge.LevelText}");
        }
        foreach (var badge in badges.Where(b => b.Item == null))
            expect(songCharts.All(c => c.Slot.ToCore() != badge.Slot.ToCore()), $"{badge.Slot} is empty only when the song has no such chart");

        var selected = badges.Where(b => b.IsSelected).ToList();
        expect(selected.Count == 1 && selected[0].Item!.ChartKey == sel.ChartKey, "exactly the selected chart's triangle is highlighted");
        Logger.Log($"smoke: levels {string.Join(" ", badges.Select(b => $"{b.Slot.ShortName()} {b.LevelText}{(b.IsSelected ? "*" : "")}"))}", LoggingTarget.Runtime, LogLevel.Important);
    }

    private void runSmokeSteps(List<SmokeStep> steps, int index, double interval, string? shots, Action then)
    {
        if (index >= steps.Count)
        {
            then();
            return;
        }

        var step = steps[index];
        double waited = 0;

        string name = $"songselect {index + 1}/{steps.Count} {step.Name}";

        void attempt()
        {
            bool ready = false;
            runSmokeAction(name + " (ready?)", () => ready = step.Ready());
            smokeStepsRun--; // the readiness poll is not a step of its own

            if (!ready)
            {
                waited += 100;
                if (waited > step.TimeoutMs)
                    throw new SmokeTestException($"[{name}] timed out after {step.TimeoutMs / 1000:0} s" + (step.Waiting?.Invoke() is { } why ? $": {why}" : ""));

                Scheduler.AddDelayed(attempt, 100);
                return;
            }

            Logger.Log($"smoke: [{name}]");
            runSmokeAction(name, step.Act);

            if (step.Shot != null && shots != null)
                Scheduler.AddDelayed(() => pendingScreenshots.Add(saveScreenshot(Path.Combine(shots, step.Shot + ".png"))), interval * 0.8);

            Scheduler.AddDelayed(() => runSmokeSteps(steps, index + 1, interval, shots, then), interval);
        }

        Scheduler.AddDelayed(attempt, 100);
    }

    private static string normPath(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>A temp copy of the first library song (read from the library, never written), to drop.</summary>
    private string? tempCopyOfFirstSong()
    {
        var song = Library.Songs.FirstOrDefault(s => s.Source == SongSource.Launcher) ?? Library.Songs.FirstOrDefault();
        if (song == null) return null;

        string dest = Path.Combine(Path.GetTempPath(), "SDVXEditor-smoke-drop", Guid.NewGuid().ToString("N")[..8], song.FolderName);
        SongLibrary.CopyDirectory(song.FolderPath, dest);
        return dest;
    }
}
