using SDVXEditor.Core.Charts;

namespace SDVXEditor.Core.Editing;

/// <summary>Which list of <see cref="VoxExtrasInfo"/> a row lives in.</summary>
public enum VoxRowKind
{
    PostEffect,
    Command,
}

/// <summary>A row of the effect lanes, addressed by list and index (rows are replaced on every edit, so never by object).</summary>
public readonly record struct VoxRowRef(VoxRowKind Kind, int Index);

/// <summary>
/// Pure edits of the VOX game effects on a copy of <see cref="VoxExtrasInfo"/>; every UI action builds the new value
/// with these and executes one <see cref="CameraCommands.SetVoxExtras"/> (one undo step, exact undo).
/// </summary>
public static class VoxEffectEditing
{
    /// <summary>Adds <paramref name="rows"/>, keeps the list sorted by time; returns the index of the first added row.</summary>
    public static int AddPostEffects(VoxExtrasInfo vox, IEnumerable<PostEffectRow> rows)
    {
        var added = rows.ToList();
        vox.PostEffects.AddRange(added);
        vox.PostEffects = vox.PostEffects.OrderBy(r => r.Y).ToList();
        return added.Count == 0 ? -1 : vox.PostEffects.IndexOf(added[0]);
    }

    public static int AddCommand(VoxExtrasInfo vox, VoxCommandRow row)
    {
        vox.Commands.Add(row);
        vox.Commands = vox.Commands.OrderBy(r => r.Y).ToList();
        return vox.Commands.IndexOf(row);
    }

    /// <summary>Moves a row to <paramref name="y"/> (≥ 0); returns its new index.</summary>
    public static int Move(VoxExtrasInfo vox, VoxRowRef r, long y)
    {
        y = Math.Max(0, y);
        if (r.Kind == VoxRowKind.PostEffect)
        {
            var row = vox.PostEffects[r.Index];
            row.Y = y;
            vox.PostEffects = vox.PostEffects.OrderBy(p => p.Y).ToList();
            return vox.PostEffects.IndexOf(row);
        }
        else
        {
            var row = vox.Commands[r.Index];
            row.Y = y;
            vox.Commands = vox.Commands.OrderBy(p => p.Y).ToList();
            return vox.Commands.IndexOf(row);
        }
    }

    /// <summary>Sets a row's length in pulses (≥ 0; a post-effect row in ms keeps its ms length).</summary>
    public static void SetLength(VoxExtrasInfo vox, VoxRowRef r, long length)
    {
        length = Math.Max(0, length);
        if (r.Kind == VoxRowKind.PostEffect) vox.PostEffects[r.Index].Length = length;
        else vox.Commands[r.Index].Length = length;
    }

    public static void Delete(VoxExtrasInfo vox, VoxRowRef r)
    {
        if (r.Kind == VoxRowKind.PostEffect) vox.PostEffects.RemoveAt(r.Index);
        else vox.Commands.RemoveAt(r.Index);
    }

    public static long Y(VoxExtrasInfo vox, VoxRowRef r) => r.Kind == VoxRowKind.PostEffect ? vox.PostEffects[r.Index].Y : vox.Commands[r.Index].Y;

    public static long Length(VoxExtrasInfo vox, VoxRowRef r) => r.Kind == VoxRowKind.PostEffect ? vox.PostEffects[r.Index].Length : vox.Commands[r.Index].Length;

    /// <summary>True when <paramref name="r"/> still points at a row.</summary>
    public static bool Valid(VoxExtrasInfo vox, VoxRowRef r) =>
        r.Index >= 0 && r.Index < (r.Kind == VoxRowKind.PostEffect ? vox.PostEffects.Count : vox.Commands.Count);

    /// <summary>
    /// A tween command (LaneY / BIL_RotZ / Morphing2) at <paramref name="y"/> going to <paramref name="to"/> over
    /// <paramref name="length"/> pulses, starting from the value the evaluator has there (explicit from, type 2).
    /// </summary>
    public static VoxCommandRow Tween(string name, long y, long length, double from, double to)
    {
        var info = VoxCommands.Find(name) ?? throw new ArgumentException("not a tween command: " + name);
        var kind = info.Params[0].Kind;
        return new VoxCommandRow(y, name, 2, Math.Max(0, length), VoxToken.Format(from, kind), VoxToken.Format(to, kind),
            VoxToken.Format(0, kind == VoxValueKind.Degrees ? VoxValueKind.Degrees : VoxValueKind.Float),
            VoxToken.Format(0, kind == VoxValueKind.Degrees ? VoxValueKind.Degrees : VoxValueKind.Float));
    }

    /// <summary>The next free script id (official charts use 1.. and 4000..).</summary>
    public static int NextScriptId(VoxExtrasInfo vox) => vox.Scripts.Count == 0 ? 1 : vox.Scripts.Max(s => s.Id) + 1;
}
