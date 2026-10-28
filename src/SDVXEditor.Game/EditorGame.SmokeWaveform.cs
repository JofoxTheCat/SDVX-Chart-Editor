using System.Globalization;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using SDVXEditor.Core.Audio;
using SDVXEditor.Game.Configuration;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Screens.Edit.Components;

namespace SDVXEditor.Game;

/// <summary>
/// Smoke steps of the <c>editing</c> part for the spectral timeline waveform: the real song's analysis loaded and
/// draws columns of more than one colour, then a generated 3 s file — a 50 Hz burst, a 2 kHz burst and a white-noise
/// burst — drawn by a stand-alone <see cref="SpectralWaveform"/> must give red-, green- and blue-dominant columns
/// over the three bursts.
/// </summary>
public partial class EditorGame
{
    private List<(Action, string?)> spectralWaveformSteps(Func<EditorScreen> ed, string dir)
    {
        string inv(FormattableString f) => f.ToString(CultureInfo.InvariantCulture);
        string wav = Path.Combine(dir, "smoke-spectral.wav");
        Container? box = null;
        SpectralWaveform? synth = null;

        static string dominants(ReadOnlySpan<SpectralWaveform.DrawnColumn> cols)
        {
            int[] n = new int[3];
            foreach (var c in cols) n[c.Value.Dominant]++;
            return $"R {n[0]} / G {n[1]} / B {n[2]}";
        }

        return new List<(Action, string?)>
        {
            (() =>
            {
                var style = config.Get<WaveformStyle>(EditorSetting.WaveformStyle);
                expect(style == WaveformStyle.Spectral, $"the spectral waveform is the default style (editor.ini says {style})");
                waitUntil(() => !ed().Ruler.Spectral.IsLoading, "the spectral waveform analysis of the song", 30_000);
            }, null),
            (() =>
            {
                var sp = ed().Ruler.Spectral;
                expect(sp.Analysis != null, $"the spectral waveform analysed {Path.GetFileName(sp.AudioPath ?? "(no audio)")}");
                var cols = sp.DrawnColumns;
                expect(cols.Length > 100, $"the spectral waveform draws columns ({cols.Length})");
                int distinct = 0;
                int[] n = new int[3];
                foreach (var c in cols) n[c.Value.Dominant]++;
                foreach (int k in n) if (k > 0) distinct++;
                expect(distinct >= 2, $"the song's columns are not all one colour ({dominants(cols)})");
                var a = sp.Analysis!;
                report(inv($"spectral waveform: {Path.GetFileName(sp.AudioPath)} {a.LengthMs / 1000:0.0} s, overall L/M/H {a.Overall.Low:0.000}/{a.Overall.Mid:0.000}/{a.Overall.High:0.000}, {cols.Length} columns on screen: {dominants(cols)} dominant"));
            }, "10b-spectral-waveform"),
            (() =>
            {
                writeSpectralTestWav(wav);
                Add(box = new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 140,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Depth = float.MinValue,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.FromHex("181416") },
                        synth = new SpectralWaveform(),
                    },
                });
                synth.LoadAudio(wav);
                waitUntil(() => synth.IsLoaded && !synth.IsLoading, "the synthetic spectral analysis", 15_000);
            }, null),
            (() =>
            {
                expect(synth!.Analysis != null, "the synthetic wav was analysed");
                // the whole 3 s on screen
                synth.SetView(1500, box!.DrawWidth / 3200f);
            }, null),
            (() =>
            {
                var cols = synth!.DrawnColumns;
                expect(cols.Length > 100, $"the synthetic waveform draws columns ({cols.Length})");

                (string Name, double From, double To, int Want)[] bursts =
                {
                    ("50 Hz kick", 300, 700, 0),
                    ("2 kHz tone", 1300, 1700, 1),
                    ("white noise", 2300, 2700, 2),
                };

                foreach (var (name, from, to, want) in bursts)
                {
                    int[] n = new int[3];
                    foreach (var c in cols)
                        if (c.Ms >= from && c.Ms < to) n[c.Value.Dominant]++;
                    int total = n.Sum();
                    expect(total > 10, inv($"{name}: columns over the burst ({total})"));
                    expect(n[want] == total, inv($"{name}: every column {"RGB"[want]}-dominant (R {n[0]} / G {n[1]} / B {n[2]})"));
                    report(inv($"spectral waveform synthetic {name}: {total} columns, all {"RGB"[want]}-dominant"));
                }

                // silence between the bursts draws nothing
                foreach (var c in cols)
                    expect(bursts.Any(x => c.Ms > x.From - 100 - 30 && c.Ms < x.To + 100 + 30), inv($"no column in the silence ({c.Ms:0} ms)"));
            }, "10c-spectral-synthetic"),
            (() => box?.Expire(), null),
        };
    }

    /// <summary>3 s mono 44.1 kHz: 50 Hz sine 0.2–0.8 s, 2 kHz sine 1.2–1.8 s, white noise 2.2–2.8 s (5 ms fades), silence between.</summary>
    private static void writeSpectralTestWav(string path)
    {
        const int rate = 44100;
        int samples = rate * 3;
        var rng = new Random(1);
        using var w = new BinaryWriter(File.Create(path));
        w.Write("RIFF"u8);
        w.Write(36 + samples * 2);
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(rate);
        w.Write(rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(samples * 2);

        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / rate;
            double v = 0;
            double fade(double a, double b) => Math.Clamp(Math.Min(t - a, b - t) / 0.005, 0, 1);

            if (t >= 0.2 && t < 0.8) v = 0.8 * Math.Sin(2 * Math.PI * 50 * t) * fade(0.2, 0.8);
            else if (t >= 1.2 && t < 1.8) v = 0.5 * Math.Sin(2 * Math.PI * 2000 * t) * fade(1.2, 1.8);
            else if (t >= 2.2 && t < 2.8) v = 0.5 * (rng.NextDouble() * 2 - 1) * fade(2.2, 2.8);

            w.Write((short)Math.Round(v * 32767));
        }
    }
}
