using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Screens;
using osu.Framework.Testing;
using SDVXEditor.Game.Configuration;
using SDVXEditor.Game.Graphics.UserInterface.Dialog;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Screens.SongSelect;
using SDVXEditor.Game.Tutorial;

namespace SDVXEditor.Game;

/// <summary>Tutorial wiring: the manager, its overlay above the dialogs, the code-side anchors and the first-start hint.</summary>
public partial class EditorGame
{
    /// <summary>The interactive tutorial (Help → Tutorial…, song select "Tutorial").</summary>
    public TutorialManager Tutorial { get; private set; } = null!;

    public TutorialOverlay TutorialOverlay { get; private set; } = null!;

    internal ScreenStack TutorialScreens => screenStack;

    /// <summary>The timing tab page of an editor (it has no public handle).</summary>
    internal static Drawable? TimingContentFor(EditorScreen? e) => e?.ChildrenOfType<Screens.Edit.Timing.TimingTabContent>().FirstOrDefault();
    internal DialogOverlay TutorialDialogs => dialogOverlay;

    private void setupTutorial()
    {
        Tutorial = new TutorialManager(this);
        TutorialOverlay = new TutorialOverlay(Tutorial);
        Tutorial.Overlay = TutorialOverlay;
        Add(Tutorial);
        Add(TutorialOverlay); // last child: above the dialogs, in window pixels like them

        registerTutorialAnchors();
        Schedule(showFirstStartHint);
    }

    /// <summary>Anchors that already have a public handle: registered here, so the UI files need no edit.</summary>
    private void registerTutorialAnchors()
    {
        EditorScreen? editor() => screenStack.CurrentScreen as EditorScreen;

        TutorialAnchors.RegisterResolver("editor.playfield", () => editor()?.Playfield);
        TutorialAnchors.RegisterResolver("editor.timeline", () => editor()?.Timeline);
        TutorialAnchors.RegisterResolver("editor.tabs", () => editor()?.ChildrenOfType<EditorTabStrip>().FirstOrDefault());
        TutorialAnchors.RegisterResolver("editor.menu", () => editor()?.MenuBar);
        TutorialAnchors.RegisterResolver("editor.bottombar", () => editor()?.BottomBar);
        TutorialAnchors.RegisterResolver("editor.bottombar.time", () => editor()?.BottomBar.TimeDisplay);
        TutorialAnchors.RegisterResolver("editor.bottombar.collab", () => editor()?.BottomBar.CollabButton);
        TutorialAnchors.RegisterResolver("editor.bottombar.bookmark-add", () => editor()?.BottomBar.AddBookmarkButton);
        TutorialAnchors.RegisterResolver("compose.toolbox", () => editor()?.Toolbox);
        TutorialAnchors.RegisterResolver("compose.laser-side", () => editor()?.Toolbox.SideToggle);
        TutorialAnchors.RegisterResolver("camera.lanes", () => editor()?.CameraContent.Lanes);
        TutorialAnchors.RegisterResolver("camera.preview", () => editor()?.CameraContent.Preview);
        TutorialAnchors.RegisterResolver("songselect.radar", () => (screenStack.CurrentScreen as SongSelectScreen)?.Radar);
        TutorialAnchors.RegisterResolver("songselect.wheel", () => (screenStack.CurrentScreen as SongSelectScreen)?.Wheel);
        TutorialAnchors.RegisterResolver("timing.metronome", () => TutorialContext.FindText(TimingContentFor(editor()), "Metronome clicks"));
        TutorialAnchors.RegisterResolver("timing.setup-panel", () => TutorialContext.FindText(TimingContentFor(editor()), "Timing Setup Panel"));
        TutorialAnchors.RegisterResolver("options.search", () => OptionsOverlay.IsOpen.Value ? OptionsOverlay.SearchBox : null);
    }

    private Bindable<bool>? tutorialHintShown;

    /// <summary>First start (not in a smoke run): one card pointing at the song select "Tutorial" button.</summary>
    private void showFirstStartHint()
    {
        if (Options.SmokeTest) return;

        tutorialHintShown = config.GetBindable<bool>(EditorSetting.TutorialHintShown);
        if (tutorialHintShown.Value) return;

        if (screenStack.CurrentScreen is not SongSelectScreen { IsLoaded: true })
        {
            Scheduler.AddDelayed(showFirstStartHint, 500);
            return;
        }

        tutorialHintShown.Value = true;
        Tutorial.ShowHint("New to charting?",
            "The tutorial charts a real song with you, step by step (8 chapters, about 18 minutes) — from the audio file to a finished, tested chart. Every chapter can also be run on its own.\n\n"
            + "You can open it any time with the Tutorial button down here, or Help → Tutorial… in the editor.",
            "songselect.tutorial");
    }
}
