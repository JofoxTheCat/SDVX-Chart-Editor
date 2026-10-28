using osu.Framework.Graphics;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osuTK.Input;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Formats.Kson;
using SDVXEditor.Core.Library;
using SDVXEditor.Game.Graphics.UserInterface;
using SDVXEditor.Game.Overlays;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Screens.Edit.Dialogs;
using SDVXEditor.Game.Screens.SongSelect;

namespace SDVXEditor.Game;

/// <summary>
/// Smoke-test part <c>sources</c> (with <c>--open</c>, on a scratch copy of the song): song sources and the artist
/// credits, all through real mouse / key input injected into the root input manager (the screens behind
/// <see cref="SmokeInput"/>, the dialogs beside it). Song Setup → Sources: a non-music link is refused with the
/// message shown, two music links are added (tracking parameters stripped), one more is added and removed again, OK
/// writes them as one undo step; Ctrl+Z / Ctrl+Y; Ctrl+S saves them and copies them into the song's other .kson
/// difficulty; reopening that difficulty shows them. The menu bar's Support the Artist opens the source list and the
/// confirmation, and "Open in browser" is recorded instead of starting a browser (<see cref="ExternalLinks.RecordOnly"/>).
/// Menu bar → Thanks opens the credits (a link there asks first; Cancel opens nothing). Then the copy is imported into
/// the library: song select shows Support the Artist for it (popup, Cancel) and the footer's Thanks opens the credits
/// (Esc closes).
/// </summary>
public partial class EditorGame
{
    private List<(Action Act, string? Shot)> songSourceSteps(string songFolder, int? slot, string? shots)
    {
        string work = Path.Combine(smokeWorkRoot(shots), "smoke-sources", Path.GetFileName(songFolder.TrimEnd('\\', '/')));

        const string soundcloud = "https://soundcloud.com/fax0508";
        const string youtube = "https://youtu.be/dQw4w9WgXcQ";

        EditorScreen ed() => screenStack.CurrentScreen as EditorScreen ?? throw new InvalidOperationException($"smoke test (sources): editor not open (got {screenStack.CurrentScreen?.GetType().Name})");
        SongSelectScreen select() => screenStack.CurrentScreen as SongSelectScreen ?? throw new InvalidOperationException($"smoke test (sources): song select not current (got {screenStack.CurrentScreen?.GetType().Name})");
        SongSetupDialog setup() => dialogOverlay.Current as SongSetupDialog ?? throw new InvalidOperationException("smoke test (sources): Song Setup not open");
        ChoiceDialog choice(string title) => dialogOverlay.Current is ChoiceDialog { IsLoaded: true } d && d.Title == title
            ? d
            : throw new InvalidOperationException($"smoke test (sources): dialog \"{title}\" not open (got {dialogOverlay.Current?.Title ?? "none"})");
        bool choiceOpen(string title) => dialogOverlay.Current is ChoiceDialog { IsLoaded: true } d && d.Title == title;
        CreditsDialog credits() => dialogOverlay.Current as CreditsDialog ?? throw new InvalidOperationException("smoke test (sources): Thanks not open");

        (Action, double)[] combo(Key modifier, Key key) => new (Action, double)[]
        {
            (() => inputKey(modifier, true), 30),
            (() => inputKey(key, true), 30),
            (() => inputKey(key, false), 30),
            (() => inputKey(modifier, false), 80),
        };

        IReadOnlyList<string> sources() => ed().Session.Chart.Editor.SongSources;

        string? siblingPath = null;
        int siblingSlot = -1, openedSlot = -1;
        int openedBefore = 0;
        string? importedFolder = null;
        Task<SongImportResult>? import = null;

        return new List<(Action, string?)>
        {
            // ---- a copy with no sources and a second .kson difficulty (the save copies the sources there)
            (() =>
            {
                if (Directory.Exists(work)) Directory.Delete(work, true);
                Directory.CreateDirectory(work);
                foreach (string f in Directory.EnumerateFiles(songFolder))
                    File.Copy(f, Path.Combine(work, Path.GetFileName(f)));

                var song = SongFolder.Load(work);
                var opened = (slot is int s ? song[s] : null) ?? song.Charts[^1];
                openedSlot = opened.Slot;

                foreach (var c in song.Charts.Where(c => c.Format == ChartFormat.Kson && c.Chart.Editor.SongSources.Count > 0))
                {
                    var k = KsonReader.ReadFile(c.FilePath);
                    k.Editor.SongSources = Array.Empty<string>();
                    KsonWriter.WriteFile(k, c.FilePath);
                }

                var sibling = song.Charts.FirstOrDefault(c => c != opened && c.Format == ChartFormat.Kson);
                if (sibling == null)
                {
                    var free = NewDifficulty.FreeSlots(song.Charts.Select(c => c.Slot));
                    if (free.Count == 0)
                    {
                        // five charts, none a kson besides the opened one: make room
                        var drop = song.Charts.Last(c => c != opened);
                        File.Delete(drop.FilePath);
                        free = new[] { drop.Slot };
                    }

                    var copy = NewDifficulty.Create(opened.Chart, free[0], copyNotes: true);
                    string file = Path.Combine(work, NewDifficulty.FileName(free[0], Directory.EnumerateFiles(work).Select(Path.GetFileName).OfType<string>()));
                    KsonWriter.WriteFile(copy, file);
                    siblingPath = file;
                    siblingSlot = free[0];
                }
                else
                {
                    siblingPath = sibling.FilePath;
                    siblingSlot = sibling.Slot;
                }

                var session = EditorSession.Load(work, openedSlot);
                expect(session.Song!.SongSources.Count == 0 && session.Chart.Editor.SongSources.Count == 0, "the scratch copy starts without sources");
                if (SmokeInput != null) SmokeInput.UseParentInput = true;
                pushAndWait(new EditorScreen(session));
                report($"sources: working on a copy in {work}; sibling difficulty {Path.GetFileName(siblingPath)}");
            }, null),
            (() =>
            {
                expect(!ed().SupportButton.HasLinks && ed().SupportButton.Alpha < 0.01f, "no Support the Artist button without sources");
                expect(Links.RecordOnly, "a smoke run never opens a browser");
                openedBefore = Links.Opened.Count;
                // the Song Setup tab header, clicked
                inputSequence("open Song Setup", clickAt(() => centreOf(textIn(ed(), EditorTab.SongSetup.DisplayName().ToLowerInvariant()))));
                waitUntil(() => dialogOverlay.Current is SongSetupDialog { IsLoaded: true }, "Song Setup to open", 5000);
            }, null),
            (() => inputSequence("the Sources tab", clickAt(() => centreOf(textIn(setup(), "Sources")), () =>
                expect(setup().SourceInput.IsPresent && setup().Edit.SongSources.Count == 0, "the Sources page is shown, empty"))), null),

            // ---- a link to a site that is not a music site: refused, with the reason under the field
            (() =>
            {
                var d = setup();
                inputSequence("click into the link field", clickAt(() => centreOf(d.SourceInput)));
            }, null),
            (() =>
            {
                setup().SourceInput.Text = "https://example.com/my-song";
                inputSequence("Add (invalid)", clickAt(() => centreOf(setup().SourceAddButton), () =>
                {
                    var d = setup();
                    expect(d.Edit.SongSources.Count == 0, "the invalid link was not added");
                    expect(d.SourceMessage.Contains("not a music site", StringComparison.Ordinal), $"the refusal says why ({d.SourceMessage})");
                    expect(d.SourceInput.Text == "https://example.com/my-song", "the refused link stays in the field to fix");
                }));
            }, "90-sources-refused"),

            // ---- two valid links (tracking parameters dropped, a bare youtu.be upgraded to https)
            (() =>
            {
                setup().SourceInput.Text = soundcloud + "?si=abc123&utm_source=clipboard";
                inputSequence("Add (SoundCloud)", clickAt(() => centreOf(setup().SourceAddButton), () =>
                {
                    var d = setup();
                    expect(d.Edit.SongSources.SequenceEqual(new[] { soundcloud }), $"SoundCloud added, normalised ({string.Join(", ", d.Edit.SongSources)})");
                    expect(d.SourceMessage.Length == 0 && d.SourceInput.Text.Length == 0, "message and field cleared after a valid link");
                }));
            }, null),
            (() =>
            {
                setup().SourceInput.Text = "youtu.be/dQw4w9WgXcQ";
                inputSequence("Add (YouTube)", clickAt(() => centreOf(setup().SourceAddButton), () =>
                    expect(setup().Edit.SongSources.SequenceEqual(new[] { soundcloud, youtube }), $"YouTube added ({string.Join(", ", setup().Edit.SongSources)})")));
            }, null),
            (() =>
            {
                setup().SourceInput.Text = "https://open.spotify.com/track/4uLU6hMCjMI75M1A2tKUQC";
                inputSequence("Add (Spotify)", clickAt(() => centreOf(setup().SourceAddButton), () =>
                    expect(setup().Edit.SongSources.Count == 3 && setup().SourceRemoveButtons.Count == 3, "three rows, each with a remove button")));
            }, "91-sources-three"),
            (() => inputSequence("remove the Spotify row", clickAt(() => centreOf(setup().SourceRemoveButtons[2]), () =>
            {
                expect(setup().Edit.SongSources.SequenceEqual(new[] { soundcloud, youtube }), "the third row removed");
                expect(sources().Count == 0, "nothing reaches the chart before OK");
            })), null),
            (() => inputSequence("OK", clickAt(() => centreOf(textIn(setup(), "OK")), () =>
            {
                expect(!dialogOverlay.HasOpenDialog, "OK closes Song Setup");
                expect(sources().SequenceEqual(new[] { soundcloud, youtube }), "OK wrote the two sources");
                expect(ed().Session.IsDirty, "the chart is dirty");
                expect(ed().SupportButton.HasLinks && ed().SupportButton.LinkCount == 2, "the menu bar shows Support the Artist");
            })), "92-editor-support-button"),

            // ---- undo / redo
            (() => inputSequence("Ctrl+Z", combo(Key.ControlLeft, Key.Z).Append(((Action)(() =>
            {
                expect(sources().Count == 0, "Ctrl+Z removes the sources again (one undo step)");
                expect(!ed().SupportButton.HasLinks, "the support button hides with them");
            }), 0.0)).ToArray()), null),
            (() => inputSequence("Ctrl+Y", combo(Key.ControlLeft, Key.Y).Append(((Action)(() =>
                expect(sources().SequenceEqual(new[] { soundcloud, youtube }) && ed().SupportButton.HasLinks, "Ctrl+Y brings them back")), 0.0)).ToArray()), null),

            // ---- save: the file and the other difficulty get them
            (() => inputSequence("Ctrl+S", combo(Key.ControlLeft, Key.S).Append(((Action)(() =>
            {
                var e = ed();
                expect(!e.Session.IsDirty, "saved");
                expect(KsonReader.ReadFile(e.Session.ChartPath!).Editor.SongSources.SequenceEqual(new[] { soundcloud, youtube }), "the saved kson holds the sources");
                expect(e.Session.LastSourceSyncFiles.Any(f => string.Equals(Path.GetFullPath(f), Path.GetFullPath(siblingPath!), StringComparison.OrdinalIgnoreCase)),
                    $"the other difficulty was updated ({string.Join(", ", e.Session.LastSourceSyncFiles.Select(Path.GetFileName))})");
                expect(KsonReader.ReadFile(siblingPath!).Editor.SongSources.SequenceEqual(new[] { soundcloud, youtube }), "the other difficulty's file holds them");
                report($"sources: Song Setup refused example.com, added SoundCloud + YouTube, removed Spotify, OK / Ctrl+Z / Ctrl+Y, Ctrl+S → {Path.GetFileName(e.Session.ChartPath)} + {Path.GetFileName(siblingPath)} - ok");
            }), 0.0)).ToArray()), null),

            // ---- reopen the other difficulty: the sources are there (save + reload)
            (() =>
            {
                inputSequence("Esc leaves the saved chart", keys(() => expect(screenStack.CurrentScreen is SongSelectScreen, "Esc on a saved chart leaves the editor"), Key.Escape));
            }, null),
            (() => pushAndWait(new EditorScreen(EditorSession.Load(work, siblingSlot))), null),
            (() =>
            {
                expect(ed().Session.Chart.Editor.SongSources.SequenceEqual(new[] { soundcloud, youtube }), "the reopened other difficulty has the sources");
                expect(ed().SupportButton.HasLinks, "and shows Support the Artist");
                var d = ed().OpenSongSetup();
                waitUntil(() => d.IsLoaded, "Song Setup on the reopened chart", 5000);
            }, null),
            (() =>
            {
                expect(setup().Edit.SongSources.SequenceEqual(new[] { soundcloud, youtube }), "Song Setup lists them after the reload");
                setup().Cancel();
            }, null),

            // ---- Support the Artist in the editor: two links → list → confirmation → recorded, no browser
            (() =>
            {
                inputSequence("Support the Artist (menu bar)", clickAt(() => centreOf(ed().SupportButton)));
                waitUntil(() => choiceOpen(ExternalLinks.SupportTitle), "the source list", 5000);
            }, null),
            (() =>
            {
                var d = choice(ExternalLinks.SupportTitle);
                expect(d.Labels.Any(l => l.StartsWith("SoundCloud", StringComparison.Ordinal)) && d.Labels.Any(l => l.StartsWith("YouTube", StringComparison.Ordinal)),
                    $"the list names the services ({string.Join(" | ", d.Labels)})");
            }, "93-support-popup"),
            (() =>
            {
                var d = choice(ExternalLinks.SupportTitle);
                inputSequence("pick SoundCloud", clickAt(() => centreOf(d.Button(d.Labels.First(l => l.StartsWith("SoundCloud", StringComparison.Ordinal))))));
                waitUntil(() => choiceOpen(ExternalLinks.ConfirmTitle), "the link confirmation", 5000);
            }, null),
            (() => expect(choice(ExternalLinks.ConfirmTitle).IsPresent, "the confirmation names the domain"), "94-link-confirm"),
            (() => inputSequence("Open in browser", clickAt(() => centreOf(choice(ExternalLinks.ConfirmTitle).Button(ExternalLinks.OpenLabel)), () =>
            {
                expect(!dialogOverlay.HasOpenDialog, "the confirmation closed");
                expect(Links.Opened.Count == openedBefore + 1 && Links.Opened[^1] == soundcloud, $"the SoundCloud link was 'opened' (recorded: {string.Join(", ", Links.Opened)})");
                report("sources: menu bar Support the Artist → list (SoundCloud, YouTube) → confirmation → recorded, no browser - ok");
            })), null),

            // ---- menu bar → Thanks: the credits; a link asks first, Cancel opens nothing; Close
            (() =>
            {
                var item = ed().MenuBar.ChildrenOfType<Menu.DrawableMenuItem>().First(d => d.Item.Text.Value.ToString() == "Thanks");
                inputSequence("menu bar → Thanks", clickAt(() => centreOf(item)));
                waitUntil(() => dialogOverlay.Current is CreditsDialog { IsLoaded: true }, "the Thanks window", 5000);
            }, "95-thanks"),
            (() =>
            {
                var d = credits();
                expect(CreditsDialog.Credits.Sections.Any(s => s.Title == "Playtesters"), "the Thanks list has its sections");
                expect(d.LinkButtons.ContainsKey(soundcloud), "the tutorial song's artist is linked");
                inputSequence("a Thanks link", clickAt(() => centreOf(d.LinkButtons[soundcloud])));
                waitUntil(() => choiceOpen(ExternalLinks.ConfirmTitle), "the confirmation for a Thanks link", 5000);
            }, null),
            (() => inputSequence("Cancel the link", clickAt(() => centreOf(choice(ExternalLinks.ConfirmTitle).Button(ExternalLinks.CancelLabel)), () =>
            {
                expect(dialogOverlay.Current is CreditsDialog, "back on the Thanks window");
                expect(Links.Opened.Count == openedBefore + 1, "Cancel opened nothing");
            })), null),
            (() => inputSequence("Close Thanks", clickAt(() => centreOf(textIn(credits(), "Close")), () =>
            {
                expect(!dialogOverlay.HasOpenDialog, "Close closes Thanks");
                report("sources: menu bar → Thanks opens the credits, a link asks first (Cancel = nothing opened), Close - ok");
            })), null),

            // ---- song select: the imported copy shows Support the Artist; the footer's Thanks
            (() =>
            {
                import = Library.ImportAsync(work, new SongImportOptions { Existing = ExistingSongPolicy.Rename });
                waitUntil(() => import.IsCompleted, "importing the copy into the library", 60_000);
            }, null),
            (() =>
            {
                expect(import is { IsCompletedSuccessfully: true }, $"the copy was imported ({import?.Exception?.GetBaseException().Message})");
                importedFolder = import!.Result.Song.FolderPath;
                expect(import.Result.Song.SongSources.SequenceEqual(new[] { soundcloud, youtube }), "the library song carries the sources");
                inputSequence("Esc to song select", keys(null, Key.Escape));
                waitUntil(() => screenStack.CurrentScreen is SongSelectScreen { IsLoaded: true }, "song select", 10_000);
            }, null),
            (() => waitUntil(() => select().LibraryLoaded && (string.Equals(select().Wheel.Selected.Value?.SongKey, importedFolder, StringComparison.OrdinalIgnoreCase) || select().Wheel.SelectByKey(importedFolder!)) && select().SupportButton.HasLinks && select().SupportButton.Alpha > 0.99f,
                "song select to show the imported song with Support the Artist", 30_000), null),
            (() =>
            {
                expect(select().SupportButton.LinkCount == 2, "song select offers both links");
                var card = select().SupportButton;
                expect(card.DrawHeight >= SupportArtistButton.CARD_HEIGHT - 0.5f && card.DrawWidth > 400,
                    $"song select's Support the Artist is the full-width card ({card.DrawWidth:0}x{card.DrawHeight:0})");
                expect(card.SubtitleText.Contains("2 links"), $"the card says how many links it offers (\"{card.SubtitleText}\")");
            }, "96-songselect-support"),
            (() =>
            {
                inputSequence("Support the Artist (song select)", clickAt(() => centreOf(select().SupportButton)));
                waitUntil(() => choiceOpen(ExternalLinks.SupportTitle), "the source list from song select", 5000);
            }, null),
            (() => inputSequence("Cancel the list", clickAt(() => centreOf(choice(ExternalLinks.SupportTitle).Button(ExternalLinks.CancelLabel)), () =>
            {
                expect(!dialogOverlay.HasOpenDialog && Links.Opened.Count == openedBefore + 1, "Cancel closes the list, nothing opened");
            })), null),
            (() =>
            {
                inputSequence("footer → Thanks", clickAt(() => centreOf(select().ThanksButton)));
                waitUntil(() => dialogOverlay.Current is CreditsDialog { IsLoaded: true }, "Thanks from song select", 5000);
            }, null),
            (() => inputSequence("Esc closes Thanks", keys(() =>
            {
                expect(!dialogOverlay.HasOpenDialog && screenStack.CurrentScreen is SongSelectScreen, "Esc closes Thanks, song select stays");
                report("sources: song select shows Support the Artist for the imported copy (list, Cancel), footer → Thanks, Esc - ok");
            }, Key.Escape)), null),
        };
    }
}
