using System.Globalization;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Screens;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Library;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Graphics.Backgrounds;
using SDVXEditor.Game.Graphics.UserInterface;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Songs;

namespace SDVXEditor.Game.Screens.SongSelect;

/// <summary>Tabs of the dark-red quick filter row.</summary>
public enum SongSelectTab
{
    None,
    Collections,
    RecentlyEdited,
    ByCharter,
    MyCharts,
    NoGrouping,
}

/// <summary>
/// Song select after reference 01 with the SDVX mapping: info header top-left, group/sort/filter top-right,
/// details panel left of the blue rail, the song wheel right, footer with Back and Edit/Play. Right-click a
/// panel for the options overlay (06). Double-click or Enter opens the editor.
/// <para>
/// Data comes from the <see cref="LibraryManager"/> (editor songs, <c>--songs</c>, the launcher's Nautica folder);
/// files dropped onto the window are imported through it. Without a manager (or with explicit items) the
/// screen shows those items, e.g. <see cref="FakeSongLibrary"/>.
/// </para>
/// </summary>
public partial class SongSelectScreen : Screen
{
    private const float header_height = 112;
    private const float left_width = 530;
    private const float left_header_height = 205;
    private const float footer_height = 125;
    private const float rail = 4;
    private const float wheel_left = 1030;
    private const float wheel_top = 150; // below group/sort/search

    /// <summary>Top edge of the wheel's visible area: the search line's bottom plus a small gap.</summary>
    public const float wheel_clip_top = 160;

    public readonly Bindable<SongGrouping> Grouping = new Bindable<SongGrouping>();
    public readonly Bindable<SongSort> Sort = new Bindable<SongSort>();
    public readonly Bindable<string> Search = new Bindable<string>(string.Empty);
    public readonly Bindable<QuickFilter> Filter = new Bindable<QuickFilter>();

    /// <summary>The highlighted tab, derived from grouping + filter.</summary>
    public readonly Bindable<SongSelectTab> ActiveTab = new Bindable<SongSelectTab>();

    private readonly IReadOnlyList<ISongListItem>? fixedItems;
    private IReadOnlyList<ISongListItem> items = Array.Empty<ISongListItem>();

    private BackgroundLayer background = null!;
    private SongWheel wheel = null!;
    private ChartOptionsOverlay options = null!;
    private SongPreviewPlayer preview = null!;
    private ToastStack toasts = null!;
    private FillFlowContainer header = null!;
    private FillFlowContainer details = null!;
    private SpriteText statusText = null!;
    private SpriteIcon statusIcon = null!;
    private Container copyBadge = null!;
    private SupportArtistButton supportButton = null!;
    private string? supportTitle;

    /// <summary>"Support the Artist" of the selected song (hidden while it has no sources).</summary>
    public SupportArtistButton SupportButton => supportButton;
    private SpriteText estimateText = null!;
    private SpriteText libraryStatus = null!;
    private SkillRadarDisplay radar = null!;
    private LevelBadgeRow levelBadges = null!;
    private Container volumeHost = null!;
    private VolumeOverlay? volume;
    private double volumeUsedAt;

    /// <summary>Song folder to select as soon as the library lists it (after an import or a fork).</summary>
    private string? pendingSelectFolder;

    private string? editedFolder;
    private EditorSession? editingSession;
    private int busy;

    public SongWheel Wheel => wheel;
    public ChartOptionsOverlay Options => options;
    public ToastStack Toasts => toasts;
    public SongPreviewPlayer Preview => preview;
    public SkillRadarDisplay Radar => radar;
    public LevelBadgeRow LevelBadges => levelBadges;
    /// <summary>The volume overlay while it is in use (it is created on the first Alt+wheel and dropped once hidden).</summary>
    public VolumeOverlay? Volume => volume;

    /// <summary>True while an import, delete or export runs.</summary>
    public bool IsBusy => busy > 0;

    /// <summary>Where the last export went (smoke test / UI).</summary>
    public string? LastExportPath { get; private set; }

    /// <summary>Folder of the last successful import.</summary>
    public string? LastImportedFolder { get; private set; }

    /// <summary>True once the library has been listed (always true for fixed items).</summary>
    public bool LibraryLoaded => manager == null || (manager.State.Value == LibraryLoadState.Ready && listedOnce);

    private bool listedOnce;

    [Resolved]
    private osu.Framework.Game game { get; set; } = null!;

    [Resolved]
    private GameHost host { get; set; } = null!;

    [Resolved(canBeNull: true)]
    private LibraryManager? manager { get; set; }

    [Resolved]
    private SDVXEditor.Game.Graphics.UserInterface.Dialog.DialogOverlay dialogs { get; set; } = null!;

    private IBindable<bool> gameActive = null!;

    /// <param name="items">Fixed items instead of the library (previews, tests); null = the real library.</param>
    public SongSelectScreen(IReadOnlyList<ISongListItem>? items = null)
    {
        fixedItems = items;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            background = new BackgroundLayer(),

            // wheel first so header/footer draw over panels scrolling past; it spans the full height so the
            // selected panel sits at the vertical centre of the screen
            // ... but clipped below the search line: panels scrolling up used to pass under the search bar, which then
            // hid the first card's title and a group header's counts (audit B3). The wheel itself keeps the full
            // height (negative margin), so the selection still sits at the screen's vertical centre.
            new Container
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Top = wheel_clip_top }, // masking clips to the bounds, not the padding
                Child = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    Padding = new MarginPadding { Left = wheel_left },
                    Child = wheel = new SongWheel { Margin = new MarginPadding { Top = -wheel_clip_top }, TopInset = wheel_clip_top },
                },
            },

            createLeftPanel(),
            createChartPanel(),
            createTopChrome(),
            createFooter(),
            libraryStatus = new SpriteText
            {
                Anchor = Anchor.BottomRight,
                Origin = Anchor.BottomRight,
                Position = new osuTK.Vector2(-480, -18), // in the footer, left of PLAY
                Font = EditorFont.Regular(18),
                Colour = Colour4.White.Opacity(0.7f),
                Shadow = true,
            },
            // bottom left, under the details: on the right they covered the wheel's cards (audit U15 / P12)
            toasts = new ToastStack
            {
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                Position = new osuTK.Vector2(16, -footer_height - 16),
            },
            // Alt+wheel anywhere but over the wheel's panels: volume (the editor's side areas do the same)
            new AltWheelVolumeLayer(isVolumeArea, adjustVolume),
            volumeHost = new Container { RelativeSizeAxes = Axes.Both },
            options = new ChartOptionsOverlay(),
            preview = new SongPreviewPlayer(),
        };

        background.Dim.Value = 0.35f;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        wheel.Selected.BindValueChanged(e => selectionChanged(e.NewValue));
        wheel.Confirmed += openEditor;
        wheel.ContextRequested += options.Open;
        levelBadges.ChartClicked += c => wheel.SelectByKey(c.ChartKey);
        options.ActionChosen += onAction;

        Grouping.BindValueChanged(_ => refresh());
        Sort.BindValueChanged(_ => refresh());
        Search.BindValueChanged(_ => refresh());
        Filter.BindValueChanged(_ => refresh());

        gameActive = game.IsActive.GetBoundCopy();
        gameActive.BindValueChanged(e =>
        {
            // the user may have added or removed songs in Explorer or the launcher meanwhile
            if (e.NewValue && this.IsCurrentScreen())
                checkForChanges();
        });

        if (fixedItems != null || manager == null)
        {
            items = fixedItems ?? FakeSongLibrary.Create();
            listedOnce = true;
            refresh();
            wheel.SnapToSelection();
            return;
        }

        manager.SongsChanged += onLibraryChanged;
        manager.Status.BindValueChanged(e =>
        {
            scanStatus = e.NewValue;
            updateCount();
        }, true);
        manager.State.BindValueChanged(e =>
        {
            // the scan publishes its songs before it reports Ready, so the list is complete here
            if (e.NewValue == LibraryLoadState.Ready) listedOnce = true;
            updateEmptyMessage();
        }, true);

        if (manager.LauncherProblem is string problem)
            toasts.Show(problem, ToastKind.Error);

        // first entry: show whatever is published already, then scan (the cache makes that cheap)
        onLibraryChanged();
        _ = manager.ScanAsync();
    }

    // ---- data --------------------------------------------------------------

    private void onLibraryChanged()
    {
        if (manager == null) return;

        items = LibraryChartItem.FromLibrary(manager.Songs, manager.Library.Roots);

        bool first = wheel.Items.Count == 0;
        refresh();

        if (pendingSelectFolder != null && wheel.SelectByKey(pendingSelectFolder))
            pendingSelectFolder = null;

        if (first) wheel.SnapToSelection();
    }

    private void refresh()
    {
        wheel.SetGroups(SongListQuery.Build(items, Grouping.Value, Sort.Value, Search.Value, Filter.Value));
        ActiveTab.Value = currentTab();
        updateEmptyMessage();
        updateCount();
    }

    /// <summary>The footer's counter text (smoke test).</summary>
    public string CountText => libraryStatus.Text.ToString();

    private string scanStatus = string.Empty;

    /// <summary>Counts what the wheel lists right now; while a scan runs its progress shows instead.</summary>
    private void updateCount()
    {
        if (scanStatus.StartsWith("Scanning", StringComparison.Ordinal))
        {
            libraryStatus.Text = scanStatus;
            return;
        }

        libraryStatus.Text = items.Count == 0 ? string.Empty
            : LibraryCountText.Format(wheel.Items.Select(i => i.SongKey).Distinct(StringComparer.OrdinalIgnoreCase).Count(), wheel.Items.Count,
                items.Select(i => i.SongKey).Distinct(StringComparer.OrdinalIgnoreCase).Count(), items.Count);
    }

    private void updateEmptyMessage()
    {
        string q = Search.Value.Trim();
        wheel.EmptyMessage = Filter.Value == QuickFilter.Collections ? "No collections yet — collections are not available in this version."
            : manager?.State.Value == LibraryLoadState.Loading && items.Count == 0 ? "Loading the library…"
            : q.Length > 0 ? $"No charts match \"{q}\"."
            : Filter.Value == QuickFilter.MyCharts ? "No editor songs yet — drop a song folder, .zip, chart or audio file here."
            : Filter.Value == QuickFilter.RecentlyEdited ? $"Nothing edited in the last {SongListQuery.RecentDays} days."
            : "No songs yet — drop a song folder, .zip, .ksh/.kson or an audio file onto the window.";
    }

    private SongSelectTab currentTab() => Filter.Value switch
    {
        QuickFilter.Collections => SongSelectTab.Collections,
        QuickFilter.RecentlyEdited => SongSelectTab.RecentlyEdited,
        QuickFilter.MyCharts => SongSelectTab.MyCharts,
        _ => Grouping.Value switch
        {
            SongGrouping.ByCharter => SongSelectTab.ByCharter,
            SongGrouping.NoGrouping => SongSelectTab.NoGrouping,
            _ => SongSelectTab.None,
        },
    };

    /// <summary>A click on the dark-red tab row.</summary>
    public void SelectTab(SongSelectTab tab)
    {
        switch (tab)
        {
            case SongSelectTab.Collections:
                Filter.Value = QuickFilter.Collections;
                break;

            case SongSelectTab.RecentlyEdited:
                Filter.Value = QuickFilter.RecentlyEdited;
                break;

            case SongSelectTab.MyCharts:
                Filter.Value = QuickFilter.MyCharts;
                break;

            case SongSelectTab.ByCharter:
                Filter.Value = QuickFilter.None;
                Grouping.Value = SongGrouping.ByCharter;
                break;

            case SongSelectTab.NoGrouping:
                Filter.Value = QuickFilter.None;
                Grouping.Value = SongGrouping.NoGrouping;
                break;
        }

        ActiveTab.Value = currentTab();
    }

    private void checkForChanges()
    {
        if (manager == null) return;

        _ = manager.CheckForChangesAsync().ContinueWith(t =>
        {
            if (t.IsFaulted) Logger.Error(t.Exception, "library change check failed");
        });
    }

    // ---- selection ---------------------------------------------------------

    protected override void Update()
    {
        base.Update();
        fitChartPanel();
    }

    private void selectionChanged(ISongListItem? item)
    {
        header.Clear();
        details.Clear();

        levelBadges.Show(item, item == null ? Array.Empty<ISongListItem>() : items.Where(i => i.SongKey == item.SongKey));
        showRadar(item);

        if (item == null)
        {
            preview.Stop();
            copyBadge.FadeOut(100);
            supportButton.Sources.Value = Array.Empty<string>();
            statusText.Text = items.Count == 0 ? "Library empty" : "Nothing selected";
            return;
        }

        background.Source.Value = item.FolderPath != null
            ? new ImageBackgroundSource(item.BackgroundPath, item.JacketPath, item.Hue)
            : GradientBackgroundSource.FromHue(item.Hue);

        if (this.IsCurrentScreen())
            preview.Play(item);

        string length = UiFormat.Duration(item.LengthMs);
        string bpm = item.BpmMax <= 0 ? "?" : Math.Abs(item.BpmMin - item.BpmMax) < 0.01 ? UiFormat.Trim(item.BpmMax) : $"{UiFormat.Trim(item.BpmMin)}-{UiFormat.Trim(item.BpmMax)}";

        header.AddRange(new Drawable[]
        {
            // the levels are the ∇ triangles beside the radar now; the corner keeps the remaining stats
            headerText($"{item.Artist} - {item.Title}", EditorFont.Regular(34)),
            headerText($"Charted by {(item.Charter.Length > 0 ? item.Charter : "unknown")}", EditorFont.Regular(24)),
            text($"Length: {length}  BPM: {bpm}  Objects: {item.Chips + item.Holds + item.Lasers}", EditorFont.Bold(23)),
            text($"Chips: {item.Chips}  Holds: {item.Holds}  Lasers: {item.Lasers}", EditorFont.Regular(23)),
            estimateText = text(estimateLine(item.EstimatedLevel, null), EditorFont.Regular(18)),
        });

        requestEstimate(item);

        bool launcher = item.Source == SongSource.Launcher;
        statusIcon.Icon = launcher ? FontAwesome.Solid.Lock : FontAwesome.Solid.Heart;
        statusText.Text = launcher ? "Launcher library (read-only)" : "Editor song";
        copyBadge.FadeTo(launcher ? 1 : 0, 100);

        details.AddRange(new Drawable[]
        {
            // "Added" = when the folder came into the library; the chart file's own date is often older (a copied
            // file keeps its mtime), so it only counts as an edit when it is newer (audit U14)
            row("Added:", item.Created == DateTime.MinValue ? "-" : UiFormat.DateTime(item.Created)),
            row("Last edit:", lastEdit(item), gapAfter: true),
            row("Folder:", item.Folder),
            row("Source:", launcher ? "Launcher" : "Editor", gapAfter: true),
            row("Charter:", item.Charter.Length > 0 ? item.Charter : "-", EditorColours.Osu.Link),
            row("Illustrator:", item.Illustrator.Length > 0 ? item.Illustrator : "-", EditorColours.Osu.Link, gapAfter: true),
            // the old "Tags:" row was synthesised from file names (audit U13); the chart file is real information
            row("Chart file:", item.ChartFile.Length > 0 ? item.ChartFile : "-"),
        });

        // the song's sources (Song Setup → Sources): a quiet pill under the details, hidden without links
        supportButton.Sources.Value = item.SongSources;
        supportTitle = item.Title;
    }

    private static string lastEdit(ISongListItem item)
    {
        if (item.Updated == DateTime.MinValue) return "-";
        if (item.Created != DateTime.MinValue && item.Updated <= item.Created.AddMinutes(1)) return "not edited since";
        return UiFormat.DateTime(item.Updated);
    }

    private void requestEstimate(ISongListItem item)
    {
        if (manager == null || item is not LibraryChartItem chart || chart.EstimatedLevel != null) return;

        manager.EstimateAsync(chart.Song, chart.Chart).ContinueWith(t =>
        {
            var result = t.IsCompletedSuccessfully ? t.Result : null;

            Schedule(() =>
            {
                chart.EstimatedLevel = result?.EstimatedLevel;
                if (wheel.Selected.Value != chart || estimateText == null) return;

                estimateText.Text = estimateLine(result?.EstimatedLevel, result);
            });
        });
    }

    private static string estimateLine(double? level, Core.LevelEstimation.DifficultyResult? result) => $"Estimated level: {formatEstimate(level, result)}";

    private void showRadar(ISongListItem? item)
    {
        string? path = item == null ? null : RadarCache.ChartPath(item);
        if (path == null)
        {
            radar.Show(null, null, item == null ? "" : "no chart file");
            return;
        }

        if (RadarCache.Peek(path) is { } done)
        {
            radar.Show(done, path);
            return;
        }

        radar.Show(null, path, "computing…");
        RadarCache.GetAsync(path).ContinueWith(t =>
        {
            var values = t.IsCompletedSuccessfully ? t.Result : null;
            Schedule(() =>
            {
                if (wheel.Selected.Value == null || RadarCache.ChartPath(wheel.Selected.Value) != path) return;
                radar.Show(values, path, values == null ? "radar unavailable" : null);
            });
        });
    }

    /// <summary>
    /// Alt+wheel outside the wheel. The overlay exists only while it is used: its meters follow the volume settings, and
    /// one left bound on the idle song select would react to setting changes from other threads (the smoke test's
    /// settings restore at shutdown) with a transform off the update thread.
    /// </summary>
    private void adjustVolume(int direction)
    {
        if (volume == null)
            volumeHost.Add(volume = new VolumeOverlay { Y = -footer_height });

        volume.Adjust(direction);
        volumeUsedAt = Time.Current;
        Scheduler.AddDelayed(dropIdleVolume, 3000);
    }

    private void dropIdleVolume()
    {
        if (volume == null || Time.Current - volumeUsedAt < 2900) return;
        if (volume.IsHovered || volume.Alpha > 0)
        {
            Scheduler.AddDelayed(dropIdleVolume, 1000);
            return;
        }

        volumeHost.Remove(volume, true);
        volume = null;
    }

    /// <summary>
    /// True where Alt+wheel changes the volume: everywhere except over the wheel's panels (header and footer
    /// included, so the bottom-right corner works like the editor's side areas).
    /// </summary>
    private bool isVolumeArea(osuTK.Vector2 screenSpace)
    {
        var p = ToLocalSpace(screenSpace);
        bool overWheel = p.X >= wheel_left - 10 && p.Y >= wheel_top && p.Y <= DrawHeight - footer_height;
        return !overWheel;
    }

    private static string formatEstimate(double? level, Core.LevelEstimation.DifficultyResult? result) => Core.LevelEstimation.EstimateText.Short(level, result);

    // ---- actions -----------------------------------------------------------

    /// <summary>Opens the selected chart in the editor (Enter, double-click, EDIT button).</summary>
    public void EditSelected()
    {
        if (wheel.Selected.Value != null) openEditor(wheel.Selected.Value);
    }

    private void openEditor(ISongListItem item)
    {
        if (!this.IsCurrentScreen()) return;

        options.Close();

        if (item.FolderPath == null)
        {
            this.Push(new EditorScreen());
            return;
        }

        EditorSession session;

        try
        {
            session = EditorSession.Load(item.FolderPath, item.Slot.ToCore());
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException or FormatException)
        {
            toasts.Show($"Cannot open {item.Folder}: {e.Message}", ToastKind.Error);
            return;
        }

        if (item.IsReadOnly && item is LibraryChartItem chart && manager != null)
        {
            var song = chart.Song;
            session.ForkBeforeSave = () => manager.EnsureWritable(song);
            toasts.Show("Launcher song — edits are saved as a copy in your editor songs.", ToastKind.Info);
        }

        editedFolder = item.FolderPath;
        editingSession = session;
        // osu!: the editor opens where the song select music was playing.
        session.StartTrackTimeMs = preview.CurrentKey == item.SongKey ? preview.StopImmediately() : null;
        preview.StopImmediately();
        this.Push(new EditorScreen(session));
    }

    [Resolved(canBeNull: true)]
    private Overlays.Options.OptionsOverlay? optionsOverlay { get; set; }

    /// <summary>The footer's "Options" button: the options overlay (like Ctrl+O).</summary>
    public void OpenEditorOptions() => optionsOverlay?.Open();

    /// <summary>Opens the right-click overlay for the selected chart (smoke test).</summary>
    public void OpenOptionsForSelected()
    {
        if (wheel.Selected.Value != null)
            options.Open(wheel.Selected.Value);
    }

    private void onAction(ChartAction action, ISongListItem item)
    {
        switch (action)
        {
            case ChartAction.Edit:
                openEditor(item);
                break;

            case ChartAction.Collections:
                toasts.Show("Collections are not available yet.", ToastKind.Info);
                break;

            case ChartAction.DeleteRefused:
                toasts.Show($"\"{item.Title}\" belongs to the launcher library and cannot be deleted here. Remove it in the launcher.", ToastKind.Error);
                break;

            case ChartAction.Delete:
                _ = delete(item);
                break;

            case ChartAction.OpenFolder:
                if (item.FolderPath != null && Directory.Exists(item.FolderPath))
                    host.OpenFileExternally(item.FolderPath);
                else
                    toasts.Show("This song has no folder on disk.", ToastKind.Error);
                break;

            case ChartAction.ExportKson:
                _ = Export(item, SongExportFormat.Kson);
                break;

            case ChartAction.ExportKsh:
                _ = Export(item, SongExportFormat.Ksh);
                break;

            case ChartAction.ExportVox:
                _ = Export(item, SongExportFormat.Vox);
                break;

            case ChartAction.ExportZip:
                _ = Export(item, SongExportFormat.Zip);
                break;

            case ChartAction.ExportSvz:
                _ = Export(item, SongExportFormat.Svz);
                break;
        }
    }

    private async Task delete(ISongListItem item)
    {
        if (manager == null || item is not LibraryChartItem chart)
        {
            toasts.Show("Only library songs can be deleted.", ToastKind.Error);
            return;
        }

        var toast = toasts.Show($"Deleting \"{item.Title}\"…", ToastKind.Progress);
        busy++;

        try
        {
            await manager.DeleteAsync(chart.Song);
            toast.Finish($"Moved \"{item.Title}\" to the recycle bin.", ToastKind.Success);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            toast.Finish($"Could not delete \"{item.Title}\": {e.Message}", ToastKind.Error);
        }
        finally
        {
            busy--;
        }
    }

    /// <summary>Exports the song of <paramref name="item"/> into the export folder; reports the result path in a toast.</summary>
    public async Task Export(ISongListItem item, SongExportFormat format)
    {
        if (manager == null || item is not LibraryChartItem chart)
        {
            toasts.Show("Only library songs can be exported.", ToastKind.Error);
            return;
        }

        string what = format switch
        {
            SongExportFormat.Kson => ".kson",
            SongExportFormat.Ksh => ".ksh",
            SongExportFormat.Vox => "VOX",
            SongExportFormat.Svz => ".svz",
            _ => ".zip",
        };

        var toast = toasts.Show($"Exporting \"{item.Title}\" as {what}…", ToastKind.Progress);
        busy++;

        try
        {
            string path = await manager.ExportAsync(chart.Song, format, p => Schedule(() => toast.Set($"Exporting \"{item.Title}\" as {what}: {p}", ToastKind.Progress)));
            LastExportPath = path;
            // the file name, not the whole temp path, and a click opens the folder (audit P12)
            toast.Finish($"Exported {Path.GetFileName(path.TrimEnd('\\', '/'))} - click to open the folder", ToastKind.Success);
            string? folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            if (folder != null) toast.Clicked = () => host.OpenFileExternally(folder);
        }
        catch (Exception e)
        {
            Logger.Error(e, $"export of {item.Folder} as {what} failed");
            toast.Finish($"Export as {what} failed: {readable(e)}", ToastKind.Error);
        }
        finally
        {
            busy--;
        }
    }

    // ---- drag & drop -------------------------------------------------------

    /// <summary>
    /// Imports dropped paths one after another (folders, .zip, .ksh/.kson, audio) and selects the last
    /// imported song. Each file gets a progress toast that turns into its result or a readable error.
    /// </summary>
    /// <param name="existing">
    /// What to do when a folder of the same name already is an editor song. Null = ask (Replace / Keep both /
    /// Cancel, osu!'s "update the set"); the smoke test passes a policy or answers the dialog itself.
    /// </param>
    public async Task ImportFiles(IReadOnlyList<string> paths, ExistingSongPolicy? existing = null)
    {
        if (manager == null)
        {
            toasts.Show("Importing needs the song library.", ToastKind.Error);
            return;
        }

        busy++;

        try
        {
            foreach (string path in paths)
            {
                string name = Path.GetFileName(path.TrimEnd('\\', '/'));
                ExistingSongPolicy policy = existing ?? ExistingSongPolicy.Rename;
                if (existing == null && await manager.FindImportConflictAsync(path) is { } taken)
                {
                    var answer = await askImportConflict(taken);
                    if (answer == null)
                    {
                        toasts.Show($"Import of {name} cancelled.", ToastKind.Info);
                        continue;
                    }

                    policy = answer.Value;
                }

                var toast = toasts.Show($"Importing {name}…", ToastKind.Progress);

                try
                {
                    var result = await manager.ImportAsync(path, new SongImportOptions { Existing = policy });
                    var song = result.Song;
                    string message = result.AlreadyInLibrary
                        ? $"\"{song.Title}\" is already in the library."
                        : $"Imported \"{song.Title}\" ({song.Charts.Count} chart{(song.Charts.Count == 1 ? "" : "s")}).";
                    if (result.Manifest is { } manifest)
                        message += $" Package from SDVX Chart Editor {manifest.EditorVersion}" + (manifest.CollabNotes.Length > 0 ? $": {manifest.CollabNotes}" : ".");
                    if (result.Warnings.Count > 0)
                        message += " " + string.Join(" ", result.Warnings);

                    toast.Finish(message, ToastKind.Success);
                    LastImportedFolder = song.FolderPath;
                    pendingSelectFolder = song.FolderPath;

                    // clear filters that would hide the new song
                    if (Filter.Value is QuickFilter.Collections || (Filter.Value is QuickFilter.MyCharts && song.Source != Core.Library.SongSource.Editor))
                        Filter.Value = QuickFilter.None;
                    if (Search.Value.Length > 0)
                        Search.Value = string.Empty;

                    if (wheel.SelectByKey(song.FolderPath))
                        pendingSelectFolder = null;
                }
                catch (Exception e) when (e is SongImportException or IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
                {
                    Logger.Log($"import of {path} failed: {e}");
                    toast.Finish($"Could not import {name}: {readable(e)}", ToastKind.Error);
                }
            }
        }
        finally
        {
            busy--;
        }
    }

    /// <summary>Replace / Keep both / Cancel for an import whose folder name is taken. Null = cancel.</summary>
    private Task<ExistingSongPolicy?> askImportConflict(string folderName)
    {
        var done = new TaskCompletionSource<ExistingSongPolicy?>();

        void push()
        {
            var dialog = new ImportConflictDialog(folderName);
            dialog.Closed += () => done.TrySetResult(dialog.Choice);
            dialogs.Push(dialog);
        }

        // the dialog overlay is global; song select itself may be suspended under the editor (not updating), so
        // its own scheduler is not used when we are on the update thread already (awaits resume there)
        if (osu.Framework.Development.ThreadSafety.IsUpdateThread) push();
        else Schedule(push);
        return done.Task;
    }

    private static string readable(Exception e)
    {
        var inner = e is AggregateException a ? a.GetBaseException() : e;
        string msg = inner.Message.Trim();
        return msg.Length > 0 ? msg : inner.GetType().Name;
    }

    // ---- screen lifecycle --------------------------------------------------

    public override void OnEntering(ScreenTransitionEvent e)
    {
        this.FadeInFromZero(200);
        preview.Play(wheel.Selected.Value);
    }

    public override void OnResuming(ScreenTransitionEvent e)
    {
        this.FadeIn(200);

        // after editing: a launcher song may have been forked, select the copy; refresh what changed on disk
        if (editingSession?.Song?.FolderPath is string folder && editedFolder != null
            && !string.Equals(Path.GetFullPath(folder), Path.GetFullPath(editedFolder), StringComparison.OrdinalIgnoreCase))
        {
            pendingSelectFolder = Path.GetFullPath(folder);
        }

        editingSession = null;
        editedFolder = null;
        checkForChanges();

        if (pendingSelectFolder != null && wheel.SelectByKey(pendingSelectFolder))
            pendingSelectFolder = null;

        preview.Play(wheel.Selected.Value);
    }

    public override void OnSuspending(ScreenTransitionEvent e)
    {
        this.FadeOut(200);
        preview.StopImmediately();
    }

    public override bool OnExiting(ScreenExitEvent e)
    {
        preview.StopImmediately();
        return base.OnExiting(e);
    }

    protected override void Dispose(bool isDisposing)
    {
        if (manager != null) manager.SongsChanged -= onLibraryChanged;
        base.Dispose(isDisposing);
    }

    private static string groupName(SongGrouping g) => g switch
    {
        SongGrouping.BySongFolder => "By song folder",
        SongGrouping.ByCharter => "By charter",
        SongGrouping.BySource => "By source",
        SongGrouping.ByLevel => "By level",
        _ => "No grouping",
    };

    private static string sortName(SongSort s) => s switch
    {
        SongSort.ByArtist => "By artist",
        SongSort.ByCharter => "By charter",
        SongSort.ByLevel => "By level",
        SongSort.ByDateEdited => "By date modified",
        _ => "By title",
    };
}
