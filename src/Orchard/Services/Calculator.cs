using System.Globalization;

namespace Orchard.Services;

/// <summary>Tiny arithmetic evaluator so Spotlight can answer "12*(3+4)" inline.</summary>
public static class Calculator
{
    public static bool LooksLikeMath(string text)
    {
        text = text.Trim();
        return text.Length > 1 && text.Any(char.IsDigit) && text.Any(c => "+-*/^%()x×÷".Contains(c)) &&
               text.All(c => char.IsDigit(c) || char.IsWhiteSpace(c) || "+-*/^%().,x×÷".Contains(c) || char.IsLetter(c));
    }

    public static double? TryEvaluate(string text)
    {
        try
        {
            var p = new Parser(text.Replace('×', '*').Replace('÷', '/').Replace("x", "*", StringComparison.Ordinal));
            double v = p.Expression();
            p.SkipWhite();
            return p.AtEnd && double.IsFinite(v) ? v : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static string Format(double v) =>
        Math.Abs(v - Math.Round(v)) < 1e-9 && Math.Abs(v) < 1e15
            ? Math.Round(v).ToString("#,0", CultureInfo.CurrentCulture)
            : v.ToString("#,0.##########", CultureInfo.CurrentCulture);

    private sealed class Parser(string s)
    {
        private int _i;
        public bool AtEnd => _i >= s.Length;

        public void SkipWhite() { while (_i < s.Length && char.IsWhiteSpace(s[_i])) _i++; }

        private bool Eat(char c)
        {
            SkipWhite();
            if (_i < s.Length && s[_i] == c) { _i++; return true; }
            return false;
        }

        public double Expression()
        {
            double v = Term();
            while (true)
            {
                if (Eat('+')) v += Term();
                else if (Eat('-')) v -= Term();
                else return v;
            }
        }

        private double Term()
        {
            double v = Power();
            while (true)
            {
                if (Eat('*')) v *= Power();
                else if (Eat('/')) v /= Power();
                else if (Eat('%')) v %= Power();
                else return v;
            }
        }

        private double Power()
        {
            double b = Unary();
            return Eat('^') ? Math.Pow(b, Power()) : b; // right-associative
        }

        private double Unary()
        {
            if (Eat('-')) return -Unary();
            if (Eat('+')) return Unary();
            return Atom();
        }

        private double Atom()
        {
            if (Eat('('))
            {
                double v = Expression();
                if (!Eat(')')) throw new FormatException("missing )");
                return v;
            }
            SkipWhite();
            int start = _i;
            while (_i < s.Length && char.IsLetter(s[_i])) _i++;
            if (_i > start)
            {
                string name = s[start.._i].ToLowerInvariant();
                if (name == "pi") return Math.PI;
                if (name == "e") return Math.E;
                if (!Eat('(')) throw new FormatException("expected (");
                double arg = Expression();
                if (!Eat(')')) throw new FormatException("missing )");
                return name switch
                {
                    "sqrt" => Math.Sqrt(arg),
                    "sin" => Math.Sin(arg),
                    "cos" => Math.Cos(arg),
                    "tan" => Math.Tan(arg),
                    "ln" => Math.Log(arg),
                    "log" => Math.Log10(arg),
                    "abs" => Math.Abs(arg),
                    _ => throw new FormatException("unknown function"),
                };
            }
            while (_i < s.Length && (char.IsDigit(s[_i]) || s[_i] == '.' || s[_i] == ',')) _i++;
            if (_i == start) throw new FormatException("expected number");
            return double.Parse(s[start.._i].Replace(",", ""), CultureInfo.InvariantCulture);
        }
    }
}
