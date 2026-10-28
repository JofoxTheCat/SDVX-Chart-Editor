using System.Globalization;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Testing;
using osuTK;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Library;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Graphics.Textures;
using SDVXEditor.Game.Graphics.UserInterface;
using SDVXEditor.Game.Graphics.UserInterface.Dialog;

namespace SDVXEditor.Game.Screens.Edit.Dialogs;

/// <summary>
/// Song Setup (reference 05) with SDVX fields. Edits a copy of the chart's metadata
/// (<see cref="SongSetupViewModel"/>); OK writes every changed field as one undo step, Cancel
/// discards. Picked jacket/background files outside the song folder are copied into it on OK.
/// </summary>
public partial class SongSetupDialog : DialogWindow
{
    private const float label_width = 128;
    private const float row_height = 25;

    private readonly EditorSession session;
    private readonly EditorClock? playback;
    private readonly Action<string>? message;

    public SongSetupViewModel Edit { get; }

    private DialogTabControl tabs = null!;
    private Container jacketPreview = null!;
    private ImageTextureCache? images;

    [Resolved]
    private DialogOverlay overlay { get; set; } = null!;

    [Resolved]
    private IRenderer renderer { get; set; } = null!;

    public SongSetupDialog(EditorSession session, EditorClock? playback = null, Action<string>? message = null)
        : base("Song Setup", new Vector2(400, 430))
    {
        this.session = session;
        this.playback = playback;
        this.message = message;
        Edit = SongSetupViewModel.From(session);
        Edit.RunEstimate(session.Chart);
    }

    public int PageCount => tabs.PageTitles.Count;

    public void ShowPage(int index) => tabs.SelectedIndex.Value = index;

    /// <summary>True when OK changed the chart (smoke test).</summary>
    public bool Applied { get; private set; }

    [BackgroundDependencyLoader]
    private void load()
    {
        images = new ImageTextureCache(renderer);

        tabs = new DialogTabControl
        {
            Position = new Vector2(4, 4),
            Size = new Vector2(392, 372),
        };

        tabs.AddPage("General", createGeneral());
        tabs.AddPage("Difficulty", createDifficulty());
        tabs.AddPage("Audio", createAudio());
        tabs.AddPage("Jacket", createJacket());
        tabs.AddPage("Design", createDesign());
        tabs.AddPage("Advanced", createAdvanced());
        tabs.AddPage("Sources", createSources());

        Body.Children = new Drawable[]
        {
            tabs,
            new DialogButton("OK", Accept)
            {
                IsDefault = true,
                Position = new Vector2(4, 384),
                Size = new Vector2(228, 38),
            },
            new DialogButton("Cancel", Cancel)
            {
                Position = new Vector2(240, 384),
                Size = new Vector2(156, 38),
            },
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        Edit.JacketFile.BindValueChanged(_ => showJacket());
        Edit.JacketImportPath.BindValueChanged(_ => showJacket(), true);
    }

    protected override void OnAccept()
    {
        if (Edit.StandardBpmError is { } bpmError)
            message?.Invoke("Song Setup: " + bpmError + " - kept the old value");

        List<IEditCommand> cmds;
        try
        {
            cmds = Edit.BuildCommands(session.Chart, session.EnsureWritableFolder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            message?.Invoke("Song Setup: could not copy the picked file: " + e.Message);
            return;
        }

        if (cmds.Count == 0)
        {
            message?.Invoke("Song Setup: nothing changed");
            return;
        }

        session.Commands.Execute(new CompositeCommand("Song Setup", cmds));
        Applied = true;
        message?.Invoke($"Song Setup: {cmds.Count} field{(cmds.Count == 1 ? "" : "s")} changed (one undo step)");
    }

    protected override void Dispose(bool isDisposing)
    {
        images?.Dispose();
        base.Dispose(isDisposing);
    }

    // ---- pages -------------------------------------------------------------

    private Drawable createGeneral()
    {
        var difficulty = new ComboBox<string>(SongSetupViewModel.VariantNames.Where(n => n.Length > 0))
        {
            Editable = true,
            RelativeSizeAxes = Axes.X,
        };
        difficulty.Text.BindTo(Edit.DifficultyName);

        return new Container
        {
            RelativeSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                new GroupBox("Song and Chart Metadata")
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 238,
                    Child = rows(
                        ("Artist", text(Edit.Artist)),
                        ("Romanised Artist", text(Edit.ArtistYomigana)),
                        ("Title", text(Edit.Title)),
                        ("Romanised Title", text(Edit.TitleYomigana)),
                        ("Charter", text(Edit.Charter)),
                        ("Illustrator", text(Edit.Illustrator)),
                        // the slot lives on the Difficulty page only (audit U11: it was on both pages)
                        ("Display name", difficulty),
                        ("Information", text(Edit.Information))),
                },
                // a grey hint, not a red "quick note" in format jargon that read like an error (audit U11)
                new TextFlowContainer(t =>
                {
                    t.Font = EditorFont.Dialog();
                    t.Colour = EditorColours.Dialog.TextDisabled;
                })
                {
                    Y = 246,
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Padding = new MarginPadding { Horizontal = 6 },
                    Text = "Romanised: the name in Latin letters, used for sorting and search (yomigana). "
                           + "Display name: the difficulty's own name, e.g. GRV or HVN for the 4th slot. "
                           + "Which slot (NOV ... MXM) and the level are on the Difficulty page.",
                },
            },
        };
    }

    private Drawable slotBox()
    {
        var slot = new ComboBox<int>(new[] { 0, 1, 2, 3, 4 })
        {
            Width = 160,
            Formatter = s => s switch
            {
                0 => "NOV (novice)",
                1 => "ADV (advanced)",
                2 => "EXH (exhaust)",
                3 => "INF (4th slot)",
                _ => "MXM (maximum)",
            },
        };
        slot.Current.BindTo(Edit.Slot);
        return slot;
    }

    private Drawable createDifficulty()
    {
        var estimate = new TextFlowContainer(t =>
        {
            t.Font = EditorFont.Dialog();
            t.Colour = EditorColours.Dialog.Text;
        })
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
        };

        Edit.Estimate.BindValueChanged(e =>
        {
            var r = e.NewValue;
            if (r?.EstimatedLevel is not double l)
            {
                estimate.Text = "No estimate (empty chart?)";
                return;
            }

            // plain words only: no feature ids, no "training range" (audit B6)
            double max = Core.LevelEstimation.EstimateText.GameMaxLevel;
            string text = l > max
                ? $"About 20+: harder than the game's highest level ({max:0})."
                : $"About {Core.LevelEstimation.EstimateText.Level(SongSetupViewModel.SnapLevel(l))}, "
                  + $"likely between {Difficulty.FormatLevel(Math.Max(1, r.Low ?? l))} and {Core.LevelEstimation.EstimateText.Level(r.High ?? l)}.";
            if (r.TopContributors.Count > 0) text += "\nMostly from: " + Core.LevelEstimation.EstimateText.Drivers(r) + ".";
            if (r.LowConfidence) text += "\nLess certain: " + string.Join("; ", Core.LevelEstimation.EstimateText.Reasons(r)) + ".";
            estimate.Text = text;
        }, true);

        return new Container
        {
            RelativeSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                new GroupBox("Difficulty")
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 90,
                    Child = rows(
                        ("Slot", slotBox()),
                        ("Level", new NumericUpDown { Width = 80, Decimals = 1, Increment = 0.1, Formatter = Difficulty.FormatLevel, Current = Edit.Level })),
                },
                new GroupBox("Estimated level")
                {
                    Y = 98,
                    RelativeSizeAxes = Axes.X,
                    Height = 150,
                    Children = new Drawable[]
                    {
                        estimate,
                        new DialogButton("Use estimate", UseEstimate)
                        {
                            Anchor = Anchor.BottomLeft,
                            Origin = Anchor.BottomLeft,
                            Size = new Vector2(120, 24),
                        },
                        DialogText.Label("fills the Level box - OK saves it").With(t =>
                        {
                            t.Anchor = Anchor.BottomLeft;
                            t.Origin = Anchor.BottomLeft;
                            t.Position = new Vector2(128, -5);
                            t.Colour = EditorColours.Dialog.TextDisabled;
                        }),
                    },
                },
            },
        };
    }

    /// <summary>Copies the estimate (one decimal) into the level field. Never writes the chart by itself.</summary>
    public void UseEstimate()
    {
        if (Edit.Estimate.Value?.EstimatedLevel is double l)
            Edit.Level.Value = Math.Clamp(SongSetupViewModel.SnapLevel(l), 1, 20);
    }

    private Drawable createAudio()
    {
        var files = folderFiles(SongFolder.AudioExtensions);
        if (Edit.AudioFile.Value.Length > 0 && !files.Contains(Edit.AudioFile.Value)) files.Insert(0, Edit.AudioFile.Value);
        if (files.Count == 0) files.Add(string.Empty);

        var audio = new ComboBox<string>(files) { RelativeSizeAxes = Axes.X, Formatter = f => f.Length == 0 ? "(none)" : f };
        audio.Current.Value = Edit.AudioFile.Value;
        audio.Current.BindValueChanged(e => Edit.AudioFile.Value = e.NewValue);

        return new Container
        {
            RelativeSizeAxes = Axes.Both,
            Child = new GroupBox("Music")
            {
                RelativeSizeAxes = Axes.X,
                Height = 186,
                Child = rows(
                    ("Audio file", audio),
                    ("Offset (ms)", new NumericUpDown { Width = 100, Current = Edit.AudioOffset }),
                    ("Preview start (ms)", new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 23,
                        Children = new Drawable[]
                        {
                            new NumericUpDown { Width = 100, Current = Edit.PreviewStart, Increment = 100 },
                            new DialogButton("Use Current Time", () => Edit.PreviewStart.Value = Math.Max(0, Math.Round(playback?.CurrentTime.Value ?? 0)))
                            {
                                X = 106,
                                Size = new Vector2(120, 23),
                            },
                        },
                    }),
                    ("Preview length (ms)", new NumericUpDown { Width = 100, Current = Edit.PreviewLength, Increment = 1000 }),
                    ("Volume (%)", new NumericUpDown { Width = 100, Current = Edit.Volume, Increment = 5 })),
            },
        };
    }

    private Drawable createJacket() => new Container
    {
        RelativeSizeAxes = Axes.Both,
        Child = new GroupBox("Jacket")
        {
            RelativeSizeAxes = Axes.X,
            Height = 262,
            Children = new Drawable[]
            {
                rows(("Image file", imagePicker(Edit.JacketFile, Edit.JacketImportPath))),
                jacketPreview = new Container
                {
                    Position = new Vector2(label_width, 34),
                    Size = new Vector2(180),
                    Masking = true,
                    CornerRadius = 1, // radius 0 draws no border
                    BorderThickness = 1,
                    BorderColour = EditorColours.Dialog.ControlBorder,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.FromHex("dcdcdc") },
                        DialogText.Label("no jacket").With(t =>
                        {
                            t.Anchor = Anchor.Centre;
                            t.Origin = Anchor.Centre;
                            t.Colour = EditorColours.Dialog.TextDisabled;
                        }),
                    },
                },
                DialogText.Label("a picked file is copied into the song folder on OK").With(t =>
                {
                    t.Position = new Vector2(0, 222);
                    t.Colour = EditorColours.Dialog.TextDisabled;
                }),
            },
        },
    };

    private Drawable createDesign()
    {
        // every bg_no of the official music_db (ids and the first song's id only, no game asset; Core GameBackgrounds)
        var bgNos = new[] { -1 }.Concat(SDVXEditor.Core.Charts.GameBackgrounds.All.Select(b => b.Id)).ToArray();
        var bg = new ComboBox<int>(bgNos.Contains(Edit.BackgroundNo.Value) ? bgNos : bgNos.Append(Edit.BackgroundNo.Value).ToArray())
        {
            Width = 420,
            Formatter = n => n < 0 ? "converter default (0)" : SDVXEditor.Core.Charts.GameBackgrounds.Find(n)?.Label ?? $"bg {n}",
        };
        bg.Current.BindTo(Edit.BackgroundNo);

        return new Container
        {
            RelativeSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                new GroupBox("Background (editor / KSM)")
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 60,
                    Child = rows(("Image file", imagePicker(Edit.BackgroundFile, Edit.BackgroundImportPath))),
                },
                new GroupBox("VOX export")
                {
                    Y = 68,
                    RelativeSizeAxes = Axes.X,
                    Height = 96,
                    Children = new Drawable[]
                    {
                        rows(("Background style", bg)),
                        new TextFlowContainer(t =>
                        {
                            t.Font = EditorFont.Dialog();
                            t.Colour = EditorColours.Dialog.TextDisabled;
                        })
                        {
                            Y = 30,
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Text = "Game background (music_db bg_no; ksh header bg_no=, written by ksh2vox). Post effects are defined per background: "
                                   + "the ones marked \"post effects\" are the official post-effect songs' own; on others (0 above all) they may not show.",
                        },
                    },
                },
            },
        };
    }

    /// <summary>Index of the Advanced page (smoke test).</summary>
    public const int AdvancedPage = 5;

    /// <summary>Advanced page fields (smoke test).</summary>
    public DialogTextBox DisplayBpmBox { get; private set; } = null!;

    public DialogTextBox StandardBpmBox { get; private set; } = null!;
    public DialogCheckBox NarrowSlamBox { get; private set; } = null!;

    private Drawable createAdvanced()
    {
        var c = session.Chart;
        int unknown = c.Extra.Count + c.Compat.KshUnknownMeta.Count + c.Compat.KshUnknownOption.Count + c.Compat.KshUnknownLine.Count;
        var inv = CultureInfo.InvariantCulture;
        NarrowSlamBox = new DialogCheckBox("Reduce the volume of narrow slams (vol_auto)");
        NarrowSlamBox.Current.BindTo(Edit.ReduceNarrowSlamVolume);

        return new Container
        {
            RelativeSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                new GroupBox("Song select and hi-speed")
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 184,
                    Child = rows(
                        ("Display BPM", DisplayBpmBox = text(Edit.DisplayBpm).With(t => t.PlaceholderText = "derived (e.g. 120-240)")),
                        ("Standard BPM", StandardBpmBox = text(Edit.StandardBpm).With(t => t.PlaceholderText = "automatic (hi-speed basis)")),
                        ("Genre icon (2:1)", imagePicker(Edit.IconFile, Edit.IconImportPath)),
                        ("Title image", imagePicker(Edit.TitleImageFile, Edit.TitleImageImportPath)),
                        ("Artist image", imagePicker(Edit.ArtistImageFile, Edit.ArtistImageImportPath)),
                        ("Slam sounds", NarrowSlamBox)),
                },
                new GroupBox("About this file (read-only)")
                {
                    Y = 190,
                    RelativeSizeAxes = Axes.X,
                    Height = 146,
                    Child = rows(
                        ("File", DialogText.Label(session.ChartPath != null ? Path.GetFileName(session.ChartPath) : "(scratch chart)")),
                        ("Format", DialogText.Label(FormatLine(c))),
                        ("Kept", DialogText.Label(KeptLine(unknown, c.Editor.Bookmarks.Count, c.Editor.Comments.Count))),
                        ("Game effects", DialogText.Label(VoxLine(c.VoxExtras.PostEffects.Count, c.VoxExtras.SpController.Count))),
                        ("Gauge total", DialogText.Label(c.GaugeTotal == 0 ? "automatic" : c.GaugeTotal.ToString(inv)))),
                },
            },
        };
    }

    /// <summary>"kson 1, ksh 171, saved by SDVX Chart Editor 0.1.0" — parts that are unknown are left out (audit U12).</summary>
    public static string FormatLine(Chart c)
    {
        var parts = new List<string> { "kson " + c.FormatVersion.ToString(CultureInfo.InvariantCulture) };
        if (c.Compat.KshVersion.Length > 0) parts.Add("ksh " + c.Compat.KshVersion);
        if (c.Editor.AppName.Length > 0) parts.Add($"saved by {c.Editor.AppName} {c.Editor.AppVersion}".TrimEnd());
        return string.Join(", ", parts);
    }

    public static string KeptLine(int unknown, int bookmarks, int comments)
    {
        static string n(int v, string one, string many) => v == 1 ? $"1 {one}" : $"{v} {many}";
        var parts = new List<string>();
        if (bookmarks > 0) parts.Add(n(bookmarks, "bookmark", "bookmarks"));
        if (comments > 0) parts.Add(n(comments, "comment", "comments"));
        if (unknown > 0) parts.Add(n(unknown, "setting this editor does not use (kept as is)", "settings this editor does not use (kept as is)"));
        return parts.Count == 0 ? "nothing extra" : string.Join(", ", parts);
    }

    /// <summary>What a chart imported from a .vox brings along: post effects and camera/background rows.</summary>
    public static string VoxLine(int postEffects, int cameraRows)
    {
        if (postEffects == 0 && cameraRows == 0) return "none (only charts from a .vox carry them)";
        static string n(int v, string one, string many) => v == 1 ? $"1 {one}" : $"{v} {many}";
        return $"{n(postEffects, "post effect", "post effects")}, {n(cameraRows, "camera/background row", "camera/background rows")}";
    }

    // ---- song sources --------------------------------------------------------

    /// <summary>Index of the Sources page (smoke test).</summary>
    public const int SourcesPage = 6;

    /// <summary>The link field of the Sources page (smoke test types into it).</summary>
    public DialogTextBox SourceInput { get; private set; } = null!;

    public DialogButton SourceAddButton { get; private set; } = null!;

    /// <summary>The message under the link field: why a link was refused, empty otherwise.</summary>
    public string SourceMessage { get; private set; } = string.Empty;

    /// <summary>The remove buttons of the listed sources, in list order (smoke test).</summary>
    public IReadOnlyList<DialogIconButton> SourceRemoveButtons => sourceList.ChildrenOfType<DialogIconButton>().ToList();

    /// <summary>"Support the Artist" under the list: tries the links as they are now (before OK).</summary>
    public SupportArtistButton SourcesSupportButton { get; private set; } = null!;

    private FillFlowContainer sourceList = null!;
    private TextFlowContainer sourceMessage = null!;

    private void setSourceMessage(string text)
    {
        SourceMessage = text;
        sourceMessage.Text = text;
    }

    /// <summary>Adds what the link field holds (Add button / Enter in the field); a refused link shows why and stays in the field.</summary>
    public void AddSourceFromInput()
    {
        var result = Edit.AddSource(SourceInput.Text);
        if (result.Ok)
        {
            SourceInput.Text = string.Empty;
            setSourceMessage(string.Empty);
        }
        else
        {
            setSourceMessage(result.Error ?? "That link cannot be used.");
        }
    }

    private Drawable createSources()
    {
        SourceInput = new DialogTextBox
        {
            RelativeSizeAxes = Axes.X,
            PlaceholderText = "https://soundcloud.com/artist/track",
            LengthLimit = MusicLinks.MaxLength,
        };
        SourceInput.OnCommit += (_, _) => AddSourceFromInput();

        var page = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Child = new GroupBox("Song sources (credit the artist)")
            {
                RelativeSizeAxes = Axes.X,
                Height = 336,
                Child = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 6),
                    Children = new Drawable[]
                    {
                        new TextFlowContainer(t =>
                        {
                            t.Font = EditorFont.Dialog();
                            t.Colour = EditorColours.Dialog.TextDisabled;
                        })
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Text = "Where the song can be heard or bought. Music sites only: "
                                   + string.Join(", ", MusicLinks.Services.Select(x => x.Name))
                                   + ". Shown as \"Support the Artist\" and saved for every difficulty.",
                        },
                        new Container
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 23,
                            Children = new Drawable[]
                            {
                                new Container
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Padding = new MarginPadding { Right = 64 },
                                    Child = SourceInput,
                                },
                                SourceAddButton = new DialogButton("Add", AddSourceFromInput)
                                {
                                    Anchor = Anchor.TopRight,
                                    Origin = Anchor.TopRight,
                                    Width = 58,
                                },
                            },
                        },
                        sourceMessage = new TextFlowContainer(t =>
                        {
                            t.Font = EditorFont.Dialog();
                            t.Colour = EditorColours.Dialog.NoteRed;
                        })
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                        },
                        new Container
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 128,
                            Masking = true,
                            CornerRadius = 1,
                            BorderThickness = 1,
                            BorderColour = EditorColours.Dialog.ListBorder,
                            Children = new Drawable[]
                            {
                                new Box { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Dialog.Window },
                                new BasicScrollContainer
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Child = sourceList = new FillFlowContainer
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        AutoSizeAxes = Axes.Y,
                                        Direction = FillDirection.Vertical,
                                        Padding = new MarginPadding(3),
                                        Spacing = new Vector2(0, 2),
                                    },
                                },
                            },
                        },
                        SourcesSupportButton = new SupportArtistButton(SupportButtonStyle.Light) { SongTitle = () => Edit.Title.Value },
                    },
                },
            },
        };

        Edit.SongSources.BindCollectionChanged((_, _) => showSources(), true);
        return page;
    }

    private void showSources()
    {
        sourceList.Clear();
        foreach (string url in Edit.SongSources.ToList())
        {
            var check = MusicLinks.Validate(url);
            sourceList.Add(new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = 24,
                Children = new Drawable[]
                {
                    DialogText.Label(check.Ok ? check.Service!.Name : "(not allowed)", bold: true).With(t =>
                    {
                        t.Anchor = Anchor.CentreLeft;
                        t.Origin = Anchor.CentreLeft;
                        t.X = 3;
                        if (!check.Ok) t.Colour = EditorColours.Dialog.NoteRed;
                    }),
                    DialogText.Label(MusicLinks.ShortForm(url, 42)).With(t =>
                    {
                        t.Anchor = Anchor.CentreLeft;
                        t.Origin = Anchor.CentreLeft;
                        t.X = 96;
                    }),
                    new DialogIconButton(FontAwesome.Solid.Times, EditorColours.Dialog.RemoveRed, () => Edit.SongSources.Remove(url))
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        Size = new Vector2(20),
                    },
                },
            });
        }

        if (Edit.SongSources.Count == 0)
        {
            sourceList.Add(new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = 24,
                Child = DialogText.Label("No sources yet.").With(t =>
                {
                    t.Colour = EditorColours.Dialog.TextDisabled;
                    t.Anchor = Anchor.CentreLeft;
                    t.Origin = Anchor.CentreLeft;
                    t.X = 3;
                }),
            });
        }

        SourcesSupportButton.Sources.Value = Edit.SongSources.ToArray();
    }

    // ---- jacket preview ------------------------------------------------------

    private string? jacketPath()
    {
        if (Edit.JacketImportPath.Value is { } picked) return picked;
        if (Edit.SongFolder == null || Edit.JacketFile.Value.Length == 0) return null;
        return SongFolder.ResolveImage(Edit.SongFolder, Edit.JacketFile.Value, out _);
    }

    private void showJacket()
    {
        string? path = jacketPath();
        if (path == null || images == null) return;

        images.GetAsync(path, new ImageVariant(360)).ContinueWith(t =>
        {
            if (!t.IsCompletedSuccessfully || t.Result is not { } texture) return;
            Schedule(() =>
            {
                while (jacketPreview.Count > 2) jacketPreview.Remove(jacketPreview[^1], true);
                jacketPreview.Add(new Sprite { RelativeSizeAxes = Axes.Both, FillMode = FillMode.Fill, Texture = texture });
            });
        }, TaskScheduler.Default);
    }

    // ---- helpers -----------------------------------------------------------

    private List<string> folderFiles(IEnumerable<string> extensions)
    {
        if (Edit.SongFolder == null || !Directory.Exists(Edit.SongFolder)) return new List<string>();
        var ext = extensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Directory.EnumerateFiles(Edit.SongFolder).Where(f => ext.Contains(Path.GetExtension(f)))
                        .Select(Path.GetFileName).OfType<string>().OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>An image of the song folder (combo) or any file on disk (Browse...).</summary>
    private Drawable imagePicker(Bindable<string> file, Bindable<string?> import)
    {
        var files = folderFiles(SongFolder.ImageExtensions);
        if (file.Value.Length > 0 && !files.Contains(file.Value)) files.Insert(0, file.Value);
        files.Insert(0, string.Empty);

        var combo = new ComboBox<string>(files) { RelativeSizeAxes = Axes.X, Formatter = f => f.Length == 0 ? "(none)" : f };
        combo.Current.Value = file.Value;
        combo.Current.BindValueChanged(e =>
        {
            file.Value = e.NewValue;
            import.Value = null;
        });
        import.BindValueChanged(e =>
        {
            if (e.NewValue != null) combo.Text.Value = Path.GetFileName(e.NewValue) + " (copy in)";
        });

        return new Container
        {
            RelativeSizeAxes = Axes.X,
            Height = 23,
            Children = new Drawable[]
            {
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Right = 70 },
                    Child = combo,
                },
                new DialogButton("Browse...", () => overlay.Push(new FilePickerDialog("Pick an image", Edit.SongFolder,
                    SongFolder.ImageExtensions, path => import.Value = path)))
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Width = 66,
                },
            },
        };
    }

    private static DialogTextBox text(Bindable<string> value)
    {
        var box = new DialogTextBox { RelativeSizeAxes = Axes.X };
        box.Current.BindTo(value);
        return box;
    }

    /// <summary>Label column (bold, right-aligned) + field column, one row per pair.</summary>
    private static Drawable rows(params (string Label, Drawable Field)[] items)
    {
        var flow = new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 2),
        };

        foreach (var (label, field) in items)
        {
            field.Anchor = Anchor.CentreLeft;
            field.Origin = Anchor.CentreLeft;

            flow.Add(new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = row_height - 2,
                Children = new Drawable[]
                {
                    new Container
                    {
                        Width = label_width - 8,
                        RelativeSizeAxes = Axes.Y,
                        Child = DialogText.Label(label, bold: true).With(t =>
                        {
                            t.Anchor = Anchor.CentreRight;
                            t.Origin = Anchor.CentreRight;
                        }),
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Left = label_width - 4 },
                        Child = field,
                    },
                },
            });
        }

        return flow;
    }
}

/// <summary>A file picker over the whole disk (osu-framework's file selector) in a dialog window.</summary>
public partial class FilePickerDialog : DialogWindow
{
    private readonly string? initialPath;
    private readonly string[] extensions;
    private readonly Action<string> picked;
    private BasicFileSelector selector = null!;

    public FilePickerDialog(string title, string? initialPath, string[] extensions, Action<string> picked)
        : base(title, new Vector2(560, 420))
    {
        this.initialPath = initialPath;
        this.extensions = extensions;
        this.picked = picked;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Body.Children = new Drawable[]
        {
            new Container
            {
                Position = new Vector2(4, 4),
                Size = new Vector2(552, 366),
                Child = selector = new BasicFileSelector(initialPath, extensions) { RelativeSizeAxes = Axes.Both },
            },
            new DialogButton("OK", Accept) { IsDefault = true, Position = new Vector2(4, 376), Size = new Vector2(360, 38) },
            new DialogButton("Cancel", Cancel) { Position = new Vector2(372, 376), Size = new Vector2(184, 38) },
        };
    }

    protected override void OnAccept()
    {
        if (selector.CurrentFile.Value is { Exists: true } f)
            picked(f.FullName);
    }
}
