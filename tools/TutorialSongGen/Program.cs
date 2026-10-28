using System.Diagnostics;
using System.Text.Json;

// Placeholder tutorial song, everything synthesised (no samples): 4/4, offset + BPM, one tempo change, a quiet
// breakdown, as tutorial.json says. Measures 0-1 intro (four-on-the-floor kick, hats), main (kick, snare on 2 and 4,
// hats, bass, lead), breakdown (pad, soft lead, a riser into the drop), speed-up (full beat, 16th hats), ringing tail.

string root = args.Length > 0 ? args[0] : findRepo();
string dir = Path.Combine(root, "src", "SDVXEditor.Resources", "Tutorial");
var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "tutorial.json")),
    new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }).RootElement;

double offsetMs = json.GetProperty("offsetMs").GetDouble();
double bpm = json.GetProperty("bpm").GetDouble();
int speedMeasure = json.GetProperty("speedUp").GetProperty("measure").GetInt32();
double speedBpm = json.GetProperty("speedUp").GetProperty("bpm").GetDouble();
int bdFrom = json.GetProperty("breakdown").GetProperty("from").GetInt32();
int bdTo = json.GetProperty("breakdown").GetProperty("to").GetInt32();
int measures = json.GetProperty("measures").GetInt32();
string audio = json.GetProperty("audio").GetString()!;

const int rate = 44100;

double timeAt(int measure, double beat)
{
    double beats = measure * 4 + beat;
    double change = speedMeasure * 4;
    double t = offsetMs / 1000 + Math.Min(beats, change) * 60 / bpm;
    if (beats > change) t += (beats - change) * 60 / speedBpm;
    return t;
}

double end = timeAt(measures, 0) + 2.5;
var buf = new float[(int)(end * rate)];
var rng = new Random(573);

void add(double t, int length, Func<int, double, float> sample)
{
    int start = (int)(t * rate);
    for (int i = 0; i < length && start + i < buf.Length; i++)
        if (start + i >= 0) buf[start + i] += sample(i, (double)i / rate);
}

void kick(double t, float gain = 0.9f) => add(t, rate / 3, (i, s) =>
{
    double phase = 2 * Math.PI * (45 * s + 80 * (1 - Math.Exp(-s * 30)) / 30);
    return gain * (float)(Math.Sin(phase) * Math.Exp(-s * 9) + (i < 60 ? 0.3 * (1 - i / 60.0) : 0));
});

void snare(double t, float gain = 0.5f)
{
    double lp = 0;
    add(t, rate / 4, (i, s) =>
    {
        double n = rng.NextDouble() * 2 - 1;
        lp += 0.35 * (n - lp);
        return gain * (float)((n - lp) * Math.Exp(-s * 18) * 0.9 + Math.Sin(2 * Math.PI * 190 * s) * Math.Exp(-s * 25) * 0.5);
    });
}

void hat(double t, float gain = 0.16f)
{
    double lp = 0;
    add(t, rate / 20, (i, s) =>
    {
        double n = rng.NextDouble() * 2 - 1;
        lp += 0.6 * (n - lp);
        return gain * (float)((n - lp) * Math.Exp(-s * 70));
    });
}

void crash(double t, float gain = 0.25f)
{
    double lp = 0;
    add(t, rate * 2, (i, s) =>
    {
        double n = rng.NextDouble() * 2 - 1;
        lp += 0.5 * (n - lp);
        return gain * (float)((n - lp) * Math.Exp(-s * 2.2));
    });
}

void tone(double t, double seconds, double freq, float gain, int shape, double attack = 0.005, double release = 0.05)
{
    int len = (int)(seconds * rate);
    double lp = 0;
    add(t, len + (int)(release * rate), (i, s) =>
    {
        double ph = freq * s % 1.0;
        double raw = shape switch
        {
            0 => Math.Sin(2 * Math.PI * ph), // sine
            1 => ph < 0.5 ? 1 : -1, // square
            2 => 2 * ph - 1, // saw
            _ => 4 * Math.Abs(ph - 0.5) - 1, // triangle
        };
        lp += (shape == 2 ? 0.08 : 0.3) * (raw - lp);
        double env = Math.Min(1, s / attack) * (i < len ? 1 : Math.Max(0, 1 - (s - seconds) / release));
        return gain * (float)(lp * env);
    });
}

double hz(int midi) => 440 * Math.Pow(2, (midi - 69) / 12.0);

int[] roots = { 45, 41, 48, 43 }; // A2 F2 C3 G2: Am F C G, one chord per bar
int[][] chords = { new[] { 57, 60, 64 }, new[] { 53, 57, 60 }, new[] { 55, 60, 64 }, new[] { 55, 59, 62 } };
int[] leadA = { 69, 72, 76, 74, 72, 69, 67, 69 };
int[] leadB = { 76, 74, 72, 74, 76, 79, 76, 74 };

for (int m = 0; m < measures; m++)
{
    bool intro = m < 2, breakdown = m >= bdFrom && m < bdTo, fast = m >= speedMeasure;
    int chord = m % 4;
    double beatLen = fast ? 60 / speedBpm : 60 / bpm;

    if (breakdown)
    {
        foreach (int n in chords[chord]) tone(timeAt(m, 0), beatLen * 4, hz(n), 0.07f, 0, attack: 0.4, release: 0.3);
        for (int b = 0; b < 4; b += 2) tone(timeAt(m, b), beatLen * 1.6, hz(leadA[(m * 2 + b) % 8] + 12), 0.05f, 3, release: 0.2);

        if (m == bdTo - 1)
        {
            // riser into the speed-up: noise swelling over the last two beats
            double t0 = timeAt(m, 2), len = timeAt(m, 4) - t0;
            double lp = 0;
            add(t0, (int)(len * rate), (i, s) =>
            {
                double n = rng.NextDouble() * 2 - 1;
                lp += (0.05 + 0.6 * s / len) * (n - lp);
                return (float)(lp * 0.35 * (s / len));
            });
        }

        continue;
    }

    for (int b = 0; b < 4; b++)
    {
        if (intro || fast || b % 2 == 0) kick(timeAt(m, b));
        if (!intro && b % 2 == 1) snare(timeAt(m, b));
    }

    int hats = fast ? 16 : 8;
    for (int h = 0; h < hats; h++) hat(timeAt(m, h * 4.0 / hats), h % (hats / 4) == 0 ? 0.2f : 0.12f);

    if (!intro || m == 1)
    {
        for (int e = 0; e < 8; e++)
            tone(timeAt(m, e * 0.5), beatLen * 0.42, hz(roots[chord] + (fast && e % 2 == 1 ? 12 : 0)), intro ? 0.12f : 0.2f, 2);
    }

    if (!intro)
    {
        var lead = fast ? leadB : leadA;
        for (int e = 0; e < 8; e++)
        {
            if (!fast && e % 4 == 3) continue; // breathing space in the main part
            tone(timeAt(m, e * 0.5), beatLen * (fast ? 0.45 : 0.8), hz(lead[e] + (chord == 1 ? -3 : 0)), 0.09f, 1);
        }
    }

    if (m == speedMeasure || m == 2) crash(timeAt(m, 0));
}

crash(timeAt(measures, 0), 0.3f);
foreach (int n in chords[0]) tone(timeAt(measures, 0), 1.5, hz(n), 0.08f, 0, attack: 0.01, release: 1.0);
kick(timeAt(measures, 0));

// normalise to about -1 dBFS, 16-bit mono WAV
float peak = buf.Max(Math.Abs);
float k = 0.89f / Math.Max(peak, 1e-6f);
string wav = Path.Combine(dir, Path.ChangeExtension(audio, ".wav"));
using (var w = new BinaryWriter(File.Create(wav)))
{
    w.Write("RIFF"u8.ToArray());
    w.Write(36 + buf.Length * 2);
    w.Write("WAVEfmt "u8.ToArray());
    w.Write(16);
    w.Write((short)1);
    w.Write((short)1);
    w.Write(rate);
    w.Write(rate * 2);
    w.Write((short)2);
    w.Write((short)16);
    w.Write("data"u8.ToArray());
    w.Write(buf.Length * 2);
    foreach (float f in buf) w.Write((short)Math.Clamp(f * k * 32767, -32768, 32767));
}

Console.WriteLine($"wrote {wav} ({end:0.0} s, first beat {offsetMs} ms, {bpm} -> {speedBpm} BPM at measure {speedMeasure + 1})");

string ogg = Path.Combine(dir, audio);
if (Path.GetExtension(ogg).Equals(".wav", StringComparison.OrdinalIgnoreCase)) return;

try
{
    var p = Process.Start(new ProcessStartInfo("ffmpeg", $"-y -hide_banner -loglevel error -i \"{wav}\" -map_metadata -1 -c:a libvorbis -q:a 4 \"{ogg}\"") { UseShellExecute = false })!;
    p.WaitForExit();
    if (p.ExitCode == 0)
    {
        File.Delete(wav);
        Console.WriteLine($"wrote {ogg} ({new FileInfo(ogg).Length / 1024} KiB)");
    }
    else Console.WriteLine("ffmpeg failed; the .wav is kept");
}
catch (System.ComponentModel.Win32Exception)
{
    Console.WriteLine("ffmpeg not found; the .wav is kept (encode it yourself or point tutorial.json's audio at it)");
}

static string findRepo()
{
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        if (File.Exists(Path.Combine(d.FullName, "SDVXEditor.sln"))) return d.FullName;
    return Directory.GetCurrentDirectory();
}
