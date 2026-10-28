using SDVXEditor.Core.Charts;

namespace SDVXEditor.Core.Validation;

/// <summary>
/// The VOX game effects (docs/VOX_EFFECTS.md): what the game would not show, drop or misread. Mirrors the checks
/// ksh2vox 0.20.0 makes when it writes the rows (it drops the same rows with a warning).
/// </summary>
public sealed class EffectChecks : IChartCheck
{
    public string Id => "effect.vox";
    public string Category => "Effects";

    public IEnumerable<ChartIssue> Run(ChartCheckContext ctx)
    {
        var vox = ctx.Chart.VoxExtras;

        if (vox.PostEffects.Count > 0 && !GameBackgrounds.DefinesPostEffects(vox.BgNo))
        {
            yield return new ChartIssue("effect.vox.post-effect-background", IssueSeverity.Warning,
                $"Post effects on {(vox.BgNo is { } b ? "bg_no " + b : "the default background (bg_no 0)")} may not show in game: effect stages are defined per "
                + "background, and every official post-effect song uses its own (bg_no 92-118, one on 5). Pick one in Song Setup / camera tab → effects.",
                vox.PostEffects.Min(p => p.Y));
        }

        foreach (var p in vox.PostEffects)
        {
            var info = PostEffects.Find(p.Name);
            if (info == null)
                yield return new ChartIssue("effect.vox.unknown-post-effect", IssueSeverity.Error,
                    $"Post effect \"{p.Name}\" at {ctx.Where(p.Y)} does not exist (the game would play it as ChromaticAberration; ksh2vox drops it).", p.Y);
            else if (!info.ParamIsLabel && info.Param(p.Param) == null)
                yield return new ChartIssue("effect.vox.unknown-parameter", IssueSeverity.Warning,
                    $"{p.Name} has no parameter \"{p.Param}\" (at {ctx.Where(p.Y)}); the game ignores the row.", p.Y);
            else if (!VoxToken.IsValid(p.Param))
                yield return new ChartIssue("effect.vox.bad-token", IssueSeverity.Error, $"Post effect parameter \"{p.Param}\" at {ctx.Where(p.Y)} is not one VOX field.", p.Y);
        }

        foreach (var c in vox.Commands)
        {
            string key = c.Key;
            if (VoxCommands.Dead.Contains(key) || VoxCommands.Dead.Contains(c.Name))
                yield return new ChartIssue("effect.vox.dead-command", IssueSeverity.Warning,
                    $"{key} at {ctx.Where(c.Y)} is parsed by the game but never read - it does nothing (ksh2vox drops it).", c.Y);
            else if (VoxCommands.Of(c) == null)
                yield return new ChartIssue("effect.vox.unknown-command", IssueSeverity.Error, $"Unknown SPCONTROLER command {key} at {ctx.Where(c.Y)} (ksh2vox drops it).", c.Y);
            else if (!new[] { c.P1, c.P2, c.P3, c.P4 }.All(VoxToken.IsValid))
                yield return new ChartIssue("effect.vox.bad-token", IssueSeverity.Error,
                    $"{key} at {ctx.Where(c.Y)} has a value that is not one VOX field (the game drops rows without exactly 8 fields).", c.Y);
            else if (c.Name == "BAROFF" && ctx.Timing.MeasureStart(ctx.Timing.MeasureAt(c.Y)) != c.Y)
                yield return new ChartIssue("effect.vox.baroff-mid-measure", IssueSeverity.Info,
                    $"BAROFF at {ctx.Where(c.Y)} is not on a measure start; the game applies it to the whole measure.", c.Y);
            else if (c.Name == "Morphing2")
                yield return new ChartIssue("effect.vox.experimental", IssueSeverity.Info, $"Morphing2 at {ctx.Where(c.Y)}: its shape in game is unknown (experimental).", c.Y);
        }

        var starts = new Dictionary<int, HashSet<long>>
        {
            [1] = ctx.Chart.Notes.Lasers[0].Select(s => s.Y).ToHashSet(),
            [8] = ctx.Chart.Notes.Lasers[1].Select(s => s.Y).ToHashSet(),
            [2] = ctx.Chart.Notes.Fx[0].Select(n => n.Y).ToHashSet(),
            [7] = ctx.Chart.Notes.Fx[1].Select(n => n.Y).ToHashSet(),
        };
        for (int k = 0; k < 4; k++) starts[3 + k] = ctx.Chart.Notes.Bt[k].Select(n => n.Y).ToHashSet();

        foreach (var s in vox.Scripted)
        {
            if (!starts.TryGetValue(s.Track, out var ys) || !ys.Contains(s.Y))
                yield return new ChartIssue("effect.vox.script-no-note", IssueSeverity.Warning,
                    $"A note script on TRACK{s.Track} at {ctx.Where(s.Y)} has no note starting there (ksh2vox drops it).", s.Y);
            foreach (int id in s.Ids.Where(id => vox.Scripts.All(x => x.Id != id)))
                yield return new ChartIssue("effect.vox.script-undefined", IssueSeverity.Error, $"Script {id} used at {ctx.Where(s.Y)} is not defined.", s.Y);
        }

        foreach (var script in vox.Scripts)
        {
            foreach (string line in script.Lines)
            {
                if (VoxScriptSyntax.Check(line) is { } err)
                    yield return new ChartIssue("effect.vox.script-syntax", IssueSeverity.Error,
                        $"Script {script.Id}: \"{line}\" - {err} (ksh2vox drops the script).");
            }
        }
    }
}
