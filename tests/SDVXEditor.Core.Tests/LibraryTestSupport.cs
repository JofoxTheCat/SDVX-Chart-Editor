namespace SDVXEditor.Core.Tests;

/// <summary>A unique temporary directory, deleted on dispose.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir(string? tag = null)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SDVXEditorTests", (tag ?? "t") + "-" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(Path);
    }

    public string Sub(params string[] parts)
    {
        string p = System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());
        Directory.CreateDirectory(p);
        return p;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>A minimal valid .kson chart (one BT chip, one laser section) for library tests.</summary>
    public static string MinimalKson(string title = "Test Song", int difficulty = 2, int level = 12, string audio = "song.ogg", string jacket = "jacket.png")
        => "{\"format_version\":1,\"meta\":{\"title\":\"" + title + "\",\"artist\":\"Tester\",\"chart_author\":\"Me\",\"difficulty\":" + difficulty +
           ",\"level\":" + level + ",\"disp_bpm\":\"150\",\"jacket_filename\":\"" + jacket + "\"},\"beat\":{\"bpm\":[[0,150]],\"time_sig\":[[0,[4,4]]]}," +
           "\"note\":{\"bt\":[[0,[480,240]],[],[],[960]],\"fx\":[[],[]],\"laser\":[[[0,[[0,0.0],[240,1.0]]]],[]]}," +
           "\"audio\":{\"bgm\":{\"filename\":\"" + audio + "\"}}}";

    /// <summary>A song folder with a chart, a fake audio file and a fake jacket.</summary>
    public static string MakeSong(string parent, string name, string? kson = null)
    {
        string dir = System.IO.Path.Combine(parent, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(System.IO.Path.Combine(dir, "exh.kson"), kson ?? MinimalKson(name));
        File.WriteAllBytes(System.IO.Path.Combine(dir, "song.ogg"), new byte[] { 1, 2, 3 });
        File.WriteAllBytes(System.IO.Path.Combine(dir, "jacket.png"), TinyPng);
        return dir;
    }

    /// <summary>A valid 1x1 PNG (ksh2vox resizes jackets with Pillow and fails on a fake image).</summary>
    public static readonly byte[] TinyPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    public static Dictionary<string, string> Snapshot(string dir)
        => Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                    .ToDictionary(f => System.IO.Path.GetRelativePath(dir, f),
                                  f => Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(File.ReadAllBytes(f))) + "@" + File.GetLastWriteTimeUtc(f).Ticks);
}
