using System.Globalization;

namespace SDVXEditor.Core.Audio.Effects;

/// <summary>Value type of an effect parameter (kson spec "Audio effects &amp; parameter list", value types).</summary>
public enum ParamKind
{
    /// <summary><c>1/N</c> or a decimal = fraction of a whole note (tempo-synced); <c>Nms</c> / <c>Ns</c> = fixed time.</summary>
    Length,

    /// <summary><c>Nsamples</c>, samples at 44100 Hz.</summary>
    Sample,

    /// <summary><c>on</c> / <c>off</c> → 1 / 0.</summary>
    Switch,

    /// <summary><c>1/N</c>, <c>N%</c> or a decimal 0..1.</summary>
    Rate,

    /// <summary><c>NHz</c> or <c>NkHz</c>.</summary>
    Freq,

    /// <summary>Semitones, −48..48.</summary>
    Pitch,

    Int,
    Float,

    /// <summary><c>NdB</c>.</summary>
    Db,

    /// <summary>Only in definitions; not a number.</summary>
    Filename,
}

/// <summary>How an <see cref="EffectValue"/> turns into a number.</summary>
public enum ValueUnit
{
    /// <summary>Already the final number (rate 0..1, Hz, samples, dB, semitones, 0/1).</summary>
    Plain,

    /// <summary>Fraction of a whole note (4 beats): seconds = value × 4 × 60 / BPM (KSM's <c>kResolution4</c> base).</summary>
    WholeNotes,

    /// <summary>Seconds (from <c>ms</c> / <c>s</c>).</summary>
    Seconds,
}

/// <summary>One parsed parameter value. Lengths stay symbolic until a BPM is known (<see cref="Resolve"/>).</summary>
public readonly record struct EffectValue(double Number, ValueUnit Unit = ValueUnit.Plain)
{
    public static readonly EffectValue Zero = new(0);

    /// <summary>The number: lengths in seconds at <paramref name="bpm"/>, everything else as parsed.</summary>
    public double Resolve(double bpm) => Unit switch
    {
        ValueUnit.WholeNotes => bpm > 0 ? Number * 240.0 / bpm : 0,
        _ => Number,
    };

    /// <summary>
    /// Parses one value of <paramref name="kind"/>. Returns false on malformed text (the caller then keeps the default,
    /// like K-Shoot ignores an invalid parameter).
    /// </summary>
    public static bool TryParse(string? text, ParamKind kind, out EffectValue value)
    {
        value = Zero;
        if (text == null) return false;
        string s = text.Trim();
        if (s.Length == 0) return false;

        switch (kind)
        {
            case ParamKind.Length:
                if (s.EndsWith("ms", StringComparison.OrdinalIgnoreCase))
                    return num(s[..^2], out double ms) && set(ms / 1000, ValueUnit.Seconds, out value);
                if (s.EndsWith("s", StringComparison.OrdinalIgnoreCase) && !s.EndsWith("samples", StringComparison.OrdinalIgnoreCase))
                    return num(s[..^1], out double sec) && set(sec, ValueUnit.Seconds, out value);
                return fraction(s, out double frac) && set(frac, ValueUnit.WholeNotes, out value);

            case ParamKind.Sample:
                if (s.EndsWith("samples", StringComparison.OrdinalIgnoreCase)) s = s[..^7];
                return num(s, out double smp) && set(smp, ValueUnit.Plain, out value);

            case ParamKind.Switch:
                if (s.Equals("on", StringComparison.OrdinalIgnoreCase)) return set(1, ValueUnit.Plain, out value);
                if (s.Equals("off", StringComparison.OrdinalIgnoreCase)) return set(0, ValueUnit.Plain, out value);
                return false;

            case ParamKind.Rate:
                if (s.EndsWith('%'))
                    return num(s[..^1], out double pct) && set(pct / 100, ValueUnit.Plain, out value);
                return fraction(s, out double rate) && set(rate, ValueUnit.Plain, out value);

            case ParamKind.Freq:
                if (s.EndsWith("kHz", StringComparison.OrdinalIgnoreCase))
                    return num(s[..^3], out double khz) && set(khz * 1000, ValueUnit.Plain, out value);
                if (s.EndsWith("Hz", StringComparison.OrdinalIgnoreCase))
                    return num(s[..^2], out double hz) && set(hz, ValueUnit.Plain, out value);
                return num(s, out double f) && set(f, ValueUnit.Plain, out value);

            case ParamKind.Db:
                if (s.EndsWith("dB", StringComparison.OrdinalIgnoreCase)) s = s[..^2];
                return num(s, out double db) && set(db, ValueUnit.Plain, out value);

            case ParamKind.Pitch:
            case ParamKind.Int:
            case ParamKind.Float:
                return num(s, out double n) && set(n, ValueUnit.Plain, out value);

            default:
                return false;
        }
    }

    private static bool set(double n, ValueUnit unit, out EffectValue value)
    {
        value = new EffectValue(n, unit);
        return !double.IsNaN(n) && !double.IsInfinity(n);
    }

    private static bool num(string s, out double v)
        => double.TryParse(s.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out v);

    /// <summary><c>1/N</c>, <c>N/M</c> or a decimal.</summary>
    private static bool fraction(string s, out double v)
    {
        int slash = s.IndexOf('/');
        if (slash < 0) return num(s, out v);
        v = 0;
        if (!num(s[..slash], out double a) || !num(s[(slash + 1)..], out double b) || b == 0) return false;
        v = a / b;
        return true;
    }
}

/// <summary>
/// A parameter's <c>Off&gt;OnMin-OnMax</c> triple (kson spec). <c>Off</c> alone sets all three; <c>Off&gt;OnMin</c> sets
/// OnMax = OnMin; <c>OnMin-OnMax</c> sets Off = OnMin. While an FX hold is active the value is OnMin; a laser moves it
/// from OnMin (laser value 0) to OnMax (1); otherwise it is Off.
/// </summary>
public readonly record struct EffectValueSet(EffectValue Off, EffectValue OnMin, EffectValue OnMax)
{
    public static EffectValueSet Constant(EffectValue v) => new(v, v, v);

    /// <summary>The value for an inactive (<paramref name="on"/> false) or active effect at laser value <paramref name="v"/>.</summary>
    public double Sample(bool on, double v, double bpm)
    {
        if (!on) return Off.Resolve(bpm);
        double a = OnMin.Resolve(bpm);
        if (OnMax == OnMin) return a;
        double b = OnMax.Resolve(bpm);
        return a + (b - a) * Math.Clamp(v, 0, 1);
    }

    public static bool TryParse(string? text, ParamKind kind, out EffectValueSet set)
    {
        set = default;
        if (string.IsNullOrWhiteSpace(text) || kind == ParamKind.Filename) return false;
        string s = text.Trim();

        string? offText = null, onText = s;
        int gt = s.IndexOf('>');
        if (gt >= 0)
        {
            offText = s[..gt];
            onText = s[(gt + 1)..];
        }

        // the range separator is the first '-' that is not a sign (a sign sits at the start of a part)
        int dash = -1;
        for (int i = 1; i < onText.Length; i++)
        {
            if (onText[i] != '-') continue;
            char prev = onText[i - 1];
            if (prev == 'e' || prev == 'E') continue; // no scientific notation in the spec, but never split one
            dash = i;
            break;
        }

        string minText = dash < 0 ? onText : onText[..dash];
        string? maxText = dash < 0 ? null : onText[(dash + 1)..];

        if (!EffectValue.TryParse(minText, kind, out var onMin)) return false;
        var onMax = onMin;
        if (maxText != null && !EffectValue.TryParse(maxText, kind, out onMax)) return false;

        EffectValue off;
        if (offText != null)
        {
            if (!EffectValue.TryParse(offText, kind, out off)) return false;
            if (maxText == null && gt < 0) onMax = onMin;
        }
        else if (maxText != null)
            off = onMin; // OnMin-OnMax: Off = OnMin
        else
            off = onMin; // a single value: all three

        set = new EffectValueSet(off, onMin, onMax);
        return true;
    }
}
