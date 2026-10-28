using System.Globalization;

namespace SDVXEditor.Core.Charts;

/// <summary>
/// The note-script language of <c>#SCRIPT_DEFINE</c> (converter CLAUDE.md §6 / docs/VOX_EFFECTS.md): one statement per
/// line, <c>&lt;var&gt; = &lt;expr&gt; [when &lt;expr&gt; &lt;op&gt; &lt;expr&gt;]</c>; assignable <c>$targetStep</c>,
/// <c>$offsetX</c>, <c>$offsetY</c>, readable also <c>$currentStep</c>; numbers with an optional <c>f</c>; <c>+ - * /</c>,
/// parentheses; <c>== != &lt; &lt;= &gt; &gt;=</c>. The grammar is the official scripts' (1751, 2330) — the DLL's
/// expression parser itself was not read, so this is deliberately strict. Same rules as ksh2vox's <c>script_errors</c>.
/// </summary>
public static class VoxScriptSyntax
{
    public static readonly string[] Assignable = { "$targetStep", "$offsetX", "$offsetY" };
    public static readonly string[] Readable = { "$targetStep", "$offsetX", "$offsetY", "$currentStep" };
    private static readonly string[] comparisons = { "==", "!=", "<=", ">=", "<", ">" };

    /// <summary>Splits a statement into tokens, or returns null with <paramref name="error"/> set.</summary>
    public static List<string>? Tokenize(string line, out string? error)
    {
        error = null;
        var toks = new List<string>();
        string s = line.Trim();
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            string? tok = null;
            foreach (string v in Readable.Append("when"))
                if (string.CompareOrdinal(s, i, v, 0, v.Length) == 0 && (i + v.Length >= s.Length || !char.IsLetterOrDigit(s[i + v.Length])))
                {
                    tok = v;
                    break;
                }

            if (tok == null && (char.IsDigit(c) || c == '.'))
            {
                int j = i;
                while (j < s.Length && char.IsDigit(s[j])) j++;
                if (j < s.Length && s[j] == '.')
                {
                    j++;
                    while (j < s.Length && char.IsDigit(s[j])) j++;
                }
                if (j < s.Length && s[j] == 'f') j++;
                tok = s[i..j];
                if (tok == "." || tok == ".f") tok = null;
            }

            if (tok == null)
                foreach (string op in comparisons)
                    if (string.CompareOrdinal(s, i, op, 0, op.Length) == 0)
                    {
                        tok = op;
                        break;
                    }

            if (tok == null && "()+-*/=".Contains(c)) tok = c.ToString();
            if (tok == null)
            {
                error = $"unexpected '{s[i..Math.Min(s.Length, i + 12)]}'";
                return null;
            }

            toks.Add(tok);
            i += tok.Length;
        }

        return toks;
    }

    /// <summary>Null when <paramref name="line"/> is a valid statement, else the reason.</summary>
    public static string? Check(string line)
    {
        if (line.Any(ch => ch is '\r' or '\n')) return "line break";
        var toks = Tokenize(line, out string? err);
        if (toks == null) return err;
        if (toks.Count < 3 || !Assignable.Contains(toks[0]) || toks[1] != "=")
            return "a statement starts with $targetStep, $offsetX or $offsetY and '='";
        var rest = toks.Skip(2).ToList();
        int w = rest.IndexOf("when");
        if (w >= 0 && rest.LastIndexOf("when") != w) return "only one 'when'";
        var expr = w >= 0 ? rest.Take(w).ToList() : rest;
        if (!exprOk(expr)) return "the value is not an arithmetic expression";
        if (w >= 0)
        {
            var cond = rest.Skip(w + 1).ToList();
            var ops = cond.Select((t, i) => (t, i)).Where(x => comparisons.Contains(x.t)).ToList();
            if (ops.Count != 1 || !exprOk(cond.Take(ops[0].i).ToList()) || !exprOk(cond.Skip(ops[0].i + 1).ToList()))
                return "'when' needs one comparison (== != < <= > >=)";
        }

        return null;
    }

    private static bool isValue(string t) => t.StartsWith('$') || char.IsDigit(t[0]) || t[0] == '.';

    private static bool exprOk(List<string> ts)
    {
        int depth = 0;
        bool wantValue = true;
        foreach (string x in ts)
        {
            if (x == "(")
            {
                if (!wantValue) return false;
                depth++;
            }
            else if (x == ")")
            {
                if (wantValue || depth == 0) return false;
                depth--;
            }
            else if (x is "+" or "-" or "*" or "/")
            {
                if (wantValue && x != "-") return false;
                wantValue = true;
            }
            else if (isValue(x))
            {
                if (!wantValue) return false;
                wantValue = false;
            }
            else return false;
        }

        return depth == 0 && !wantValue;
    }
}

/// <summary>
/// Runs note scripts for the preview: per frame, for a note at <c>targetStep</c> (its own tick, 48 per quarter) and the
/// play position <c>currentStep</c>, the statements in order give the step the note is drawn at and its offsets
/// (<c>$offsetX</c> 0.178 ≈ one BT lane). Invalid statements are skipped.
/// </summary>
public static class VoxScriptEval
{
    /// <summary>Steps per kson pulse: VOX ticks are 48 per quarter, pulses 240.</summary>
    public const double StepsPerPulse = 48.0 / Timebase.PulsesPerQuarter;

    public readonly record struct Result(double TargetStep, double OffsetX, double OffsetY);

    public static Result Run(IEnumerable<VoxScript> scripts, double targetStep, double currentStep)
    {
        var vars = new Dictionary<string, double> { ["$targetStep"] = targetStep, ["$offsetX"] = 0, ["$offsetY"] = 0, ["$currentStep"] = currentStep };
        foreach (var s in scripts)
            foreach (string line in s.Lines)
                runLine(line, vars);
        return new Result(vars["$targetStep"], vars["$offsetX"], vars["$offsetY"]);
    }

    private static void runLine(string line, Dictionary<string, double> vars)
    {
        if (VoxScriptSyntax.Check(line) != null) return;
        var toks = VoxScriptSyntax.Tokenize(line, out _)!;
        var rest = toks.Skip(2).ToList();
        int w = rest.IndexOf("when");
        var expr = w >= 0 ? rest.Take(w).ToList() : rest;
        if (w >= 0)
        {
            var cond = rest.Skip(w + 1).ToList();
            int op = cond.FindIndex(t => t is "==" or "!=" or "<" or "<=" or ">" or ">=");
            double a = eval(cond.Take(op).ToList(), vars), b = eval(cond.Skip(op + 1).ToList(), vars);
            bool ok = cond[op] switch
            {
                "==" => a == b,
                "!=" => a != b,
                "<" => a < b,
                "<=" => a <= b,
                ">" => a > b,
                _ => a >= b,
            };
            if (!ok) return;
        }

        vars[toks[0]] = eval(expr, vars);
    }

    private static double eval(List<string> ts, Dictionary<string, double> vars)
    {
        int i = 0;
        double v = sum(ts, ref i, vars);
        return double.IsFinite(v) ? v : 0;
    }

    private static double sum(List<string> ts, ref int i, Dictionary<string, double> vars)
    {
        double v = product(ts, ref i, vars);
        while (i < ts.Count && ts[i] is "+" or "-")
        {
            bool plus = ts[i++] == "+";
            double r = product(ts, ref i, vars);
            v = plus ? v + r : v - r;
        }

        return v;
    }

    private static double product(List<string> ts, ref int i, Dictionary<string, double> vars)
    {
        double v = unary(ts, ref i, vars);
        while (i < ts.Count && ts[i] is "*" or "/")
        {
            bool mul = ts[i++] == "*";
            double r = unary(ts, ref i, vars);
            v = mul ? v * r : r == 0 ? 0 : v / r;
        }

        return v;
    }

    private static double unary(List<string> ts, ref int i, Dictionary<string, double> vars)
    {
        if (ts[i] == "-")
        {
            i++;
            return -unary(ts, ref i, vars);
        }

        if (ts[i] == "(")
        {
            i++;
            double v = sum(ts, ref i, vars);
            i++; // ')'
            return v;
        }

        string t = ts[i++];
        if (t.StartsWith('$')) return vars.GetValueOrDefault(t);
        return double.Parse(t.TrimEnd('f'), NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
