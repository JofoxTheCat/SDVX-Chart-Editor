using osu.Framework.Bindables;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.LevelEstimation;
using SDVXEditor.Core.Library;

namespace SDVXEditor.Game.Screens.Edit;

public enum ControlPointKind
{
    /// <summary>Tempo point (red dot, osu! uninherited).</summary>
    Bpm,

    /// <summary>Scroll speed multiplier, osu! "inherited point" (green dot, x1,00).</summary>
    ScrollSpeed,

    /// <summary>Chart stop (blue dot); <see cref="ControlPointRow.Value"/> is the length in ms.</summary>
    Stop,

    /// <summary>Time signature change (grey dot).</summary>
    TimeSignature,
}

/// <summary>
/// One row of a control point list: a view of a chart (or <see cref="TimingDraft"/>) object.
/// <see cref="Source"/> is the <see cref="BpmChange"/> / <see cref="GraphPoint"/> / <see cref="Stop"/> /
/// <see cref="TimeSignature"/> the row shows.
/// </summary>
public sealed class ControlPointRow
{
    public double TimeMs { get; set; }
    public long Pulse { get; set; }
    public ControlPointKind Kind { get; set; }

    /// <summary>BPM, scroll multiplier or stop length (ms) depending on <see cref="Kind"/>.</summary>
    public double Value { get; set; }

    public int Numerator { get; set; } = 4;
    public int Denominator { get; set; } = 4;

    public object? Source { get; set; }

    /// <summary>Rows for every timing object of a beat info, timed with <paramref name="map"/>.</summary>
    public static List<ControlPointRow> Build(BeatInfo beat, Core.Timing.TimingMap map)
    {
        double ms(long pulse) => map.PulseToMs(pulse);

        var rows = new List<ControlPointRow>();
        foreach (var b in beat.Bpm)
        {
            var sig = map.SignatureOfMeasure(map.MeasureAt(b.Y));
            rows.Add(new ControlPointRow { TimeMs = ms(b.Y), Pulse = b.Y, Kind = ControlPointKind.Bpm, Value = b.Bpm, Numerator = sig.Numerator, Denominator = sig.Denominator, Source = b });
        }

        foreach (var p in beat.ScrollSpeed)
            rows.Add(new ControlPointRow { TimeMs = ms(p.Y), Pulse = p.Y, Kind = ControlPointKind.ScrollSpeed, Value = p.ValueAfter, Source = p });
        foreach (var st in beat.Stops)
            rows.Add(new ControlPointRow { TimeMs = ms(st.Y), Pulse = st.Y, Kind = ControlPointKind.Stop, Value = ms(st.Y + st.Length) - ms(st.Y), Source = st });
        foreach (var ts in beat.TimeSignatures)
        {
            long y = map.MeasureStart(ts.MeasureIndex);
            rows.Add(new ControlPointRow
            {
                TimeMs = ms(y), Pulse = y, Kind = ControlPointKind.TimeSignature,
                Numerator = ts.Numerator, Denominator = ts.Denominator, Source = ts,
            });
        }

        return rows.OrderBy(r => r.Pulse).ThenBy(r => r.Kind).ToList();
    }
}

/// <summary>
/// Timing tab + F6 state, bound to the session's chart. The steppers write through
/// <see cref="TimingCommands"/>: a click or a scroll notch is one undo step, a drag of a stepper is
/// one transaction (<see cref="BeginGesture"/> / <see cref="EndGesture"/>). Undo/redo and chart switches
/// come back through <see cref="Refresh"/>.
/// </summary>
public sealed class TimingViewModel
{
    public readonly BindableDouble Bpm = new BindableDouble(120) { MinValue = 1, MaxValue = 1000, Precision = 0.001 };
    public readonly BindableDouble Offset = new BindableDouble { MinValue = -10_000, MaxValue = 3_600_000, Precision = 1 };
    public readonly BindableDouble BaseScrollSpeed = new BindableDouble(1) { MinValue = 0.05, MaxValue = 20, Precision = 0.01 };

    /// <summary>Checked: notes keep their musical position (move in time with the grid). Unchecked: notes keep their time in ms.</summary>
    public readonly BindableBool MoveNotesWithTiming = new BindableBool(true);

    /// <summary>Checked: scroll-speed points, stops, curves, bookmarks and the preview keep their musical position.</summary>
    public readonly BindableBool MoveExtrasWithOffset = new BindableBool(true);

    public readonly BindableBool ScaleNotes = new BindableBool();
    public readonly BindableBool SnapNotes = new BindableBool(true);
    public readonly BindableInt SnapDivisor = new BindableInt(4) { MinValue = 1, MaxValue = 48 };

    /// <summary>Audible metronome ticks while playing (timing tab).</summary>
    public readonly BindableBool MetronomeClicks = new BindableBool();

    /// <summary>Every timing object of the chart, for lists and "next/previous timing point".</summary>
    public readonly BindableList<ControlPointRow> ControlPoints = new BindableList<ControlPointRow>();

    public EditorSession? Session { get; private set; }

    /// <summary>Current editor time in ms (the BPM stepper edits the BPM point active there).</summary>
    public Func<double> CurrentTime { get; set; } = () => 0;

    /// <summary>Runs before a timing edit (the screen writes its bookmarks into the chart so they can be remapped).</summary>
    public Action? BeforeEdit { get; set; }

    public Action<string>? Message { get; set; }

    private bool loading;
    private bool applying;
    private string? gesture;

    public TimingViewModel()
    {
        Bpm.BindValueChanged(e => onUserChange(e.NewValue, e.OldValue, "Change BPM", bpmCommand));
        Offset.BindValueChanged(e => onUserChange(e.NewValue, e.OldValue, "Change offset", offsetCommand));
        BaseScrollSpeed.BindValueChanged(e => onUserChange(e.NewValue, e.OldValue, "Change base scroll speed",
            (c, v) => TimingCommands.SetBaseScrollSpeed(c, v)));
    }

    public TimingChangeOptions Options => new TimingChangeOptions
    {
        MoveNotesWithGrid = MoveNotesWithTiming.Value,
        MoveExtrasWithGrid = MoveExtrasWithOffset.Value,
        ScaleNotes = ScaleNotes.Value,
        SnapNotes = SnapNotes.Value,
        SnapDivisor = SnapDivisor.Value,
    };

    public void Attach(EditorSession session)
    {
        if (Session != null) Session.ChartChanged -= Refresh;
        Session = session;
        session.ChartChanged += Refresh;
        Refresh();
    }

    public void Detach()
    {
        if (Session != null) Session.ChartChanged -= Refresh;
        Session = null;
    }

    /// <summary>Re-reads the values from the chart (after any edit, undo/redo or chart switch).</summary>
    public void Refresh()
    {
        if (Session == null) return;

        var chart = Session.Chart;
        ControlPoints.Clear();
        ControlPoints.AddRange(ControlPointRow.Build(chart.Beat, Session.Timing));

        // while a stepper is being applied, its own value is the truth; do not fight the user
        if (applying) return;

        loading = true;
        Bpm.Value = ActiveBpmPoint()?.Bpm ?? 120;
        Offset.Value = chart.Audio.Bgm.OffsetMs;
        BaseScrollSpeed.Value = TimingCommands.BaseScrollSpeed(chart);
        loading = false;
    }

    /// <summary>The BPM point in effect at the current time (the first one before the chart starts).</summary>
    public BpmChange? ActiveBpmPoint()
    {
        if (Session == null) return null;
        double pulse = Session.MsToPulse(CurrentTime());
        var bpms = Session.Chart.Beat.Bpm;
        return bpms.LastOrDefault(b => b.Y <= pulse) ?? bpms.FirstOrDefault();
    }

    // ---- write-through ------------------------------------------------------

    /// <summary>Starts a stepper drag: everything until <see cref="EndGesture"/> becomes one undo step.</summary>
    public void BeginGesture(string description)
    {
        if (Session == null || gesture != null) return;
        BeforeEdit?.Invoke();
        gesture = description;
        Session.Commands.BeginTransaction(description);
    }

    public void EndGesture()
    {
        if (Session == null || gesture == null) return;
        gesture = null;
        Session.Commands.Commit();
        Refresh();
    }

    private void onUserChange(double value, double old, string description, Func<Core.Charts.Chart, double, IEditCommand?> build)
    {
        if (loading || Session == null || value == old) return;

        applying = true;
        try
        {
            var stack = Session.Commands;
            if (gesture != null)
            {
                // rollback + re-execute from the gesture's start, like a compose drag
                stack.Rollback();
                stack.BeginTransaction(gesture);
                if (build(Session.Chart, value) is { } cmd) stack.Execute(cmd);
                Session.NotifyChanged();
            }
            else
            {
                BeforeEdit?.Invoke();
                if (build(Session.Chart, value) is { } cmd) stack.Execute(cmd);
            }
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            Message?.Invoke(e.Message);
        }
        finally
        {
            applying = false;
        }

        if (gesture == null) Refresh();
    }

    private IEditCommand? bpmCommand(Core.Charts.Chart chart, double bpm)
    {
        var draft = TimingDraft.From(chart);
        var active = ActiveBpmPoint();
        var target = active == null ? null : draft.Beat.Bpm.FirstOrDefault(b => draft.OriginOf(b) == active);
        if (target == null) draft.Beat.Bpm.Insert(0, new BpmChange(0, bpm));
        else target.Bpm = bpm;
        return TimingCommands.Apply(chart, draft, Options, $"Change BPM to {bpm:0.###}");
    }

    private IEditCommand? offsetCommand(Core.Charts.Chart chart, double offset)
    {
        var draft = TimingDraft.From(chart);
        draft.OffsetMs = (long)Math.Round(offset);
        return TimingCommands.Apply(chart, draft, Options, $"Change offset to {draft.OffsetMs} ms");
    }

    /// <summary>Applies an edited draft (F6 OK, tap BPM) as one undo step. False when nothing changed.</summary>
    public bool ApplyDraft(TimingDraft draft, string description, TimingChangeOptions? options = null)
    {
        if (Session == null) return false;
        BeforeEdit?.Invoke();
        var cmd = TimingCommands.Apply(Session.Chart, draft, options ?? Options, description);
        if (cmd == null) return false;
        Session.Commands.Execute(cmd);
        return true;
    }

    /// <summary>Sets the BPM of the point active at the current time (tap tempo).</summary>
    public bool ApplyBpm(double bpm)
    {
        if (Session == null) return false;
        BeforeEdit?.Invoke();
        var cmd = bpmCommand(Session.Chart, bpm);
        if (cmd == null) return false;
        Session.Commands.Execute(cmd);
        return true;
    }

    /// <summary>Ctrl+P / Ctrl+Shift+P: a BPM (copy of the active tempo) or scroll-speed point at the current time, snapped to the pulse.</summary>
    public string? AddPointAtCurrentTime(ControlPointKind kind)
    {
        if (Session == null) return null;
        long pulse = Math.Max(0, (long)Math.Round(Session.MsToPulse(CurrentTime())));
        var beat = Session.Chart.Beat;

        IEditCommand cmd;
        if (kind == ControlPointKind.Bpm)
        {
            if (beat.Bpm.Any(b => b.Y == pulse)) return "A BPM point already exists here";
            cmd = TimingCommands.AddBpm(new BpmChange(pulse, Session.Timing.BpmAt(pulse)));
        }
        else
        {
            if (beat.ScrollSpeed.Any(p => p.Y == pulse)) return "A scroll speed point already exists here";
            cmd = TimingCommands.AddScrollSpeed(new GraphPoint(pulse, Session.Timing.ScrollSpeedAt(pulse) is var v && v > 0 ? v : 1));
        }

        Session.Commands.Execute(cmd);
        return null;
    }

    /// <summary>Ctrl+I: deletes the timing object at or before the current time (never the only BPM point).</summary>
    public string? DeletePointAtCurrentTime()
    {
        if (Session == null) return null;
        double now = CurrentTime() + 0.5;
        var row = ControlPoints.Where(p => p.TimeMs <= now).OrderBy(p => p.TimeMs).LastOrDefault();
        if (row == null || (row.Kind == ControlPointKind.Bpm && Session.Chart.Beat.Bpm.Count <= 1))
            return "Nothing to delete (the only BPM point stays)";

        IEditCommand cmd = row.Source switch
        {
            BpmChange b => TimingCommands.RemoveBpm(b),
            GraphPoint g => TimingCommands.RemoveScrollSpeed(g),
            Stop s => TimingCommands.RemoveStop(s),
            TimeSignature t => TimingCommands.RemoveTimeSignature(t),
            _ => throw new InvalidOperationException("unknown row"),
        };
        Session.Commands.Execute(cmd);
        return null;
    }
}

/// <summary>Song Setup dialog data: a copy of the chart's metadata/audio/jacket/background that OK writes back as one undo step.</summary>
public sealed class SongSetupViewModel
{
    public static readonly IReadOnlyList<string> VariantNames = new[] { "", "GRV", "HVN", "VVD", "XCD", "NBL" };

    public readonly Bindable<string> Artist = new Bindable<string>(string.Empty);
    public readonly Bindable<string> ArtistYomigana = new Bindable<string>(string.Empty);
    public readonly Bindable<string> Title = new Bindable<string>(string.Empty);
    public readonly Bindable<string> TitleYomigana = new Bindable<string>(string.Empty);
    public readonly Bindable<string> Charter = new Bindable<string>(string.Empty);
    public readonly Bindable<string> Illustrator = new Bindable<string>(string.Empty);
    public readonly Bindable<string> Information = new Bindable<string>(string.Empty);

    /// <summary>0 NOV, 1 ADV, 2 EXH, 3 INF-class, 4 MXM.</summary>
    public readonly Bindable<int> Slot = new Bindable<int>(3);

    /// <summary>Display name of the slot ("" = the slot's default, e.g. GRV for an infinite variant).</summary>
    public readonly Bindable<string> DifficultyName = new Bindable<string>(string.Empty);

    public readonly BindableDouble Level = new BindableDouble(1) { MinValue = 1, MaxValue = 20, Precision = 0.1 };
    public readonly Bindable<DifficultyResult?> Estimate = new Bindable<DifficultyResult?>();

    public readonly Bindable<string> AudioFile = new Bindable<string>(string.Empty);
    public readonly BindableDouble AudioOffset = new BindableDouble(0) { MinValue = -10_000, MaxValue = 10_000, Precision = 1 };
    public readonly BindableDouble PreviewStart = new BindableDouble(0) { MinValue = 0, MaxValue = 3_600_000, Precision = 1 };
    public readonly BindableDouble PreviewLength = new BindableDouble(15_000) { MinValue = 1000, MaxValue = 60_000, Precision = 1 };
    public readonly BindableDouble Volume = new BindableDouble(100) { MinValue = 0, MaxValue = 200, Precision = 1 };

    /// <summary>The jacket filename inside the song folder.</summary>
    public readonly Bindable<string> JacketFile = new Bindable<string>(string.Empty);

    /// <summary>A picked jacket outside the song folder; OK copies it in.</summary>
    public readonly Bindable<string?> JacketImportPath = new Bindable<string?>();

    public readonly Bindable<string> BackgroundFile = new Bindable<string>(string.Empty);
    public readonly Bindable<string?> BackgroundImportPath = new Bindable<string?>();

    /// <summary>music_db bg_no for the VOX export; -1 = converter default.</summary>
    public readonly BindableInt BackgroundNo = new BindableInt(-1);

    /// <summary>kson <c>meta.disp_bpm</c> (ksh <c>t=</c> header text): the BPM text song select shows; empty = derived.</summary>
    public readonly Bindable<string> DisplayBpm = new Bindable<string>(string.Empty);

    /// <summary>kson <c>meta.std_bpm</c> (ksh <c>to=</c>): hi-speed basis; empty = automatic.</summary>
    public readonly Bindable<string> StandardBpm = new Bindable<string>(string.Empty);

    /// <summary>kson <c>meta.icon_filename</c>: genre icon (2:1) shown in song select.</summary>
    public readonly Bindable<string> IconFile = new Bindable<string>(string.Empty);
    public readonly Bindable<string?> IconImportPath = new Bindable<string?>();

    /// <summary>kson <c>meta.title_img_filename</c> / <c>artist_img_filename</c>: images instead of the text.</summary>
    public readonly Bindable<string> TitleImageFile = new Bindable<string>(string.Empty);
    public readonly Bindable<string?> TitleImageImportPath = new Bindable<string?>();
    public readonly Bindable<string> ArtistImageFile = new Bindable<string>(string.Empty);
    public readonly Bindable<string?> ArtistImageImportPath = new Bindable<string?>();

    /// <summary>kson <c>audio.key_sound.laser.legacy.vol_auto</c> (ksh <c>chokkakuautovol</c>): narrow slams play quieter.</summary>
    public readonly BindableBool ReduceNarrowSlamVolume = new BindableBool();

    /// <summary>Why the standard BPM text is refused, or null.</summary>
    public string? StandardBpmError => StandardBpm.Value.Trim().Length == 0 || tryBpm(StandardBpm.Value, out _) ? null : "Standard BPM must be a number above 0 (or empty = automatic)";

    private static bool tryBpm(string text, out double bpm) =>
        double.TryParse(text.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out bpm) && bpm > 0 && !double.IsInfinity(bpm);

    /// <summary>Song sources (links to music sites only, see <see cref="MusicLinks"/>). Song-level; OK writes the list.</summary>
    public readonly BindableList<string> SongSources = new BindableList<string>();

    public string? SongFolder { get; private set; }

    private bool snappingLevel;

    /// <summary>
    /// Validates <paramref name="input"/> (<see cref="MusicLinks.Validate"/>) and appends the normalised link. A refused
    /// link, a duplicate or a full list returns a result with the message to show; the list is then unchanged.
    /// </summary>
    public MusicLinkResult AddSource(string? input)
    {
        var r = MusicLinks.Validate(input);
        if (!r.Ok) return r;
        if (SongSources.Contains(r.Url!)) return MusicLinkResult.Fail("That link is already in the list.");
        if (SongSources.Count >= MusicLinks.MaxSources) return MusicLinkResult.Fail($"At most {MusicLinks.MaxSources} sources per song.");
        SongSources.Add(r.Url!);
        return r;
    }

    public SongSetupViewModel()
    {
        // tenths from 17 up, whole levels below: a 0.1 step across 17 lands on the next whole level
        Level.BindValueChanged(e =>
        {
            if (snappingLevel) return;
            double v = e.NewValue;
            double snapped = SnapLevel(v);
            if (v < Difficulty.FirstDecimalLevel && snapped != v && Math.Abs(Math.Abs(v - e.OldValue) - 0.1) < 1e-6)
                snapped = v < e.OldValue ? Math.Floor(v) : Math.Ceiling(v);
            if (snapped == v) return;
            snappingLevel = true;
            Level.Value = snapped;
            snappingLevel = false;
        });
    }

    /// <summary>A level as the game uses it: one decimal from 17 up, a whole number below.</summary>
    public static double SnapLevel(double level) => level >= Difficulty.FirstDecimalLevel
        ? Math.Round(level, 1, MidpointRounding.AwayFromZero)
        : Math.Round(level, MidpointRounding.AwayFromZero);

    public static SongSetupViewModel From(EditorSession session)
    {
        var vm = new SongSetupViewModel();
        vm.Load(session);
        return vm;
    }

    public void Load(EditorSession session)
    {
        var chart = session.Chart;
        var meta = chart.Meta;
        var bgm = chart.Audio.Bgm;
        SongFolder = session.Song?.FolderPath;

        Title.Value = meta.Title;
        TitleYomigana.Value = meta.TitleTranslit;
        Artist.Value = meta.Artist;
        ArtistYomigana.Value = meta.ArtistTranslit;
        Charter.Value = meta.ChartAuthor;
        Illustrator.Value = meta.JacketAuthor;
        Information.Value = meta.Information;
        Slot.Value = Math.Clamp(meta.DifficultySlot, 0, 4);
        DifficultyName.Value = meta.DifficultyName ?? string.Empty;
        snappingLevel = true; // show the chart's level as stored, even a stray tenth below 17
        Level.Value = Math.Clamp(meta.Level, 1, 20);
        snappingLevel = false;

        AudioFile.Value = bgm.Filename.Length > 0 ? bgm.Filename : session.AudioPath != null ? Path.GetFileName(session.AudioPath) : string.Empty;
        AudioOffset.Value = Math.Clamp(bgm.OffsetMs, -10_000, 10_000);
        PreviewStart.Value = Math.Clamp(bgm.PreviewOffsetMs, 0, 3_600_000);
        PreviewLength.Value = Math.Clamp(bgm.PreviewDurationMs, 1000, 60_000);
        Volume.Value = Math.Clamp(Math.Round(bgm.Volume * 100), 0, 200);

        JacketFile.Value = meta.JacketFilename;
        JacketImportPath.Value = null;
        BackgroundFile.Value = chart.Bg.Filename;
        BackgroundImportPath.Value = null;
        BackgroundNo.Value = chart.VoxExtras.BgNo ?? -1;

        DisplayBpm.Value = meta.DispBpm;
        StandardBpm.Value = meta.StdBpm is { } std ? std.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
        IconFile.Value = meta.IconFilename;
        IconImportPath.Value = null;
        TitleImageFile.Value = meta.TitleImgFilename;
        TitleImageImportPath.Value = null;
        ArtistImageFile.Value = meta.ArtistImgFilename;
        ArtistImageImportPath.Value = null;
        ReduceNarrowSlamVolume.Value = chart.Audio.KeySound.LaserVolumeAuto == true;

        // the session already gave a difficulty without its own list the song's (EditorSession.setChart)
        SongSources.Clear();
        SongSources.AddRange(chart.Editor.SongSources);
    }

    /// <summary>Runs the level estimator on the chart (never writes the level).</summary>
    public void RunEstimate(Chart chart)
    {
        try
        {
            Estimate.Value = DifficultyEstimator.Default.Estimate(chart);
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException)
        {
            Estimate.Value = null;
        }
    }

    /// <summary>
    /// The edits as commands (one per changed field). Picked files outside the song folder are copied
    /// into it first (that copy is not undone; undo restores the previous filename).
    /// </summary>
    /// <param name="writableFolder">
    /// Returns the folder files may be copied into: <see cref="EditorSession.EnsureWritableFolder"/>, which forks a
    /// read-only launcher song first. Only called when a picked file actually has to be copied.
    /// </param>
    public List<IEditCommand> BuildCommands(Chart chart, Func<string>? writableFolder = null)
    {
        this.writableFolder = writableFolder;
        var cmds = new List<IEditCommand>();
        var meta = chart.Meta;

        void set<T>(string field, T current, T value, Func<ChartMeta, T> get, Action<ChartMeta, T> setter)
        {
            if (!EqualityComparer<T>.Default.Equals(current, value)) cmds.Add(MetaCommands.Set(field, get, setter, value));
        }

        set("title", meta.Title, Title.Value, m => m.Title, (m, v) => m.Title = v);
        set("title (yomigana)", meta.TitleTranslit, TitleYomigana.Value, m => m.TitleTranslit, (m, v) => m.TitleTranslit = v);
        set("artist", meta.Artist, Artist.Value, m => m.Artist, (m, v) => m.Artist = v);
        set("artist (yomigana)", meta.ArtistTranslit, ArtistYomigana.Value, m => m.ArtistTranslit, (m, v) => m.ArtistTranslit = v);
        set("charter", meta.ChartAuthor, Charter.Value, m => m.ChartAuthor, (m, v) => m.ChartAuthor = v);
        set("illustrator", meta.JacketAuthor, Illustrator.Value, m => m.JacketAuthor, (m, v) => m.JacketAuthor = v);
        set("information", meta.Information, Information.Value, m => m.Information, (m, v) => m.Information = v);
        set("difficulty slot", meta.DifficultySlot, Slot.Value, m => m.DifficultySlot, (m, v) => m.DifficultySlot = v);
        string? name = string.IsNullOrWhiteSpace(DifficultyName.Value) ? null : DifficultyName.Value.Trim();
        set("difficulty name", meta.DifficultyName, name, m => m.DifficultyName, (m, v) => m.DifficultyName = v);
        set("level", meta.Level, Level.Value, m => m.Level, (m, v) => m.Level = v);

        string jacket = importInto(JacketImportPath.Value) ?? JacketFile.Value;
        set("jacket", meta.JacketFilename, jacket, m => m.JacketFilename, (m, v) => m.JacketFilename = v);

        var bgm = chart.Audio.Bgm;
        if (AudioFile.Value != bgm.Filename && !(bgm.Filename.Length == 0 && AudioFile.Value.Length == 0))
            cmds.Add(TimingCommands.SetBgmFilename(AudioFile.Value));
        if ((long)AudioOffset.Value != bgm.OffsetMs) cmds.Add(TimingCommands.SetOffset((long)AudioOffset.Value));
        if ((long)PreviewStart.Value != bgm.PreviewOffsetMs || (long)PreviewLength.Value != bgm.PreviewDurationMs)
            cmds.Add(TimingCommands.SetPreview((long)PreviewStart.Value, (long)PreviewLength.Value));
        double volume = Volume.Value / 100;
        if (Math.Abs(volume - bgm.Volume) > 1e-9) cmds.Add(TimingCommands.SetBgmVolume(volume));

        string bg = importInto(BackgroundImportPath.Value) ?? BackgroundFile.Value;
        if (bg != chart.Bg.Filename)
            cmds.Add(new SetValueCommand<string>("Change background", c => c.Bg.Filename, (c, v) => c.Bg.Filename = v, bg));

        if (!SongSources.SequenceEqual(chart.Editor.SongSources, StringComparer.Ordinal))
        {
            cmds.Add(new SetValueCommand<IReadOnlyList<string>>("Change song sources", c => c.Editor.SongSources,
                (c, v) => c.Editor.SongSources = v, SongSources.ToArray()));
        }

        set("display BPM", meta.DispBpm, DisplayBpm.Value.Trim(), m => m.DispBpm, (m, v) => m.DispBpm = v);
        double? std = tryBpm(StandardBpm.Value, out double stdValue) ? stdValue : null;
        if (StandardBpmError == null) set("standard BPM", meta.StdBpm, std, m => m.StdBpm, (m, v) => m.StdBpm = v);
        set("genre icon", meta.IconFilename, importInto(IconImportPath.Value) ?? IconFile.Value, m => m.IconFilename, (m, v) => m.IconFilename = v);
        set("title image", meta.TitleImgFilename, importInto(TitleImageImportPath.Value) ?? TitleImageFile.Value, m => m.TitleImgFilename, (m, v) => m.TitleImgFilename = v);
        set("artist image", meta.ArtistImgFilename, importInto(ArtistImageImportPath.Value) ?? ArtistImageFile.Value, m => m.ArtistImgFilename, (m, v) => m.ArtistImgFilename = v);

        // unchecked keeps a stated "0" (false) and an unstated value (null) as they were
        bool? volAuto = chart.Audio.KeySound.LaserVolumeAuto;
        bool? wantVolAuto = ReduceNarrowSlamVolume.Value ? true : volAuto == true ? false : volAuto;
        if (wantVolAuto != volAuto)
            cmds.Add(new SetValueCommand<bool?>("Change narrow slam volume", c => c.Audio.KeySound.LaserVolumeAuto, (c, v) => c.Audio.KeySound.LaserVolumeAuto = v, wantVolAuto));

        int? bgNo = BackgroundNo.Value < 0 ? null : BackgroundNo.Value;
        if (bgNo != chart.VoxExtras.BgNo)
            cmds.Add(new SetValueCommand<int?>("Change bg_no", c => c.VoxExtras.BgNo, (c, v) => c.VoxExtras.BgNo = v, bgNo));

        return cmds;
    }

    private Func<string>? writableFolder;

    /// <summary>Copies a picked file into the song folder (unique name) and returns its filename; null when nothing was picked.</summary>
    private string? importInto(string? source)
    {
        if (string.IsNullOrEmpty(source) || SongFolder == null || !File.Exists(source)) return null;

        string full = Path.GetFullPath(source);
        if (string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(SongFolder).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            return Path.GetFileName(full);

        // never into the folder captured at load: a launcher song is forked to an editor copy first
        string folder = writableFolder?.Invoke() ?? SongFolder;
        if (string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(folder).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            return Path.GetFileName(full);

        string name = Path.GetFileName(full);
        string target = Path.Combine(folder, name);
        for (int i = 2; File.Exists(target) && !sameContent(target, full); i++)
        {
            name = $"{Path.GetFileNameWithoutExtension(full)} ({i}){Path.GetExtension(full)}";
            target = Path.Combine(folder, name);
        }

        if (!File.Exists(target)) File.Copy(full, target);
        return name;
    }

    private static bool sameContent(string a, string b)
    {
        var fa = new FileInfo(a);
        var fb = new FileInfo(b);
        return fa.Length == fb.Length && File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
    }
}
