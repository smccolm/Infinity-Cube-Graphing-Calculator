namespace GraphCalc.Api;

public sealed class ExpressionEvaluator
{
    private string _text = "";
    private int _pos;
    private double _x;
    private double _y;
    private double _z;

    public double Evaluate(string expression, double x, double y, double z)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            throw new InvalidOperationException("Expression is empty.");
        }

        _text = expression;
        _pos = 0;
        _x = x;
        _y = y;
        _z = z;

        double value = ParseExpression();
        SkipWhite();
        if (_pos < _text.Length)
        {
            throw new InvalidOperationException($"Unexpected text at character {_pos + 1}: '{_text[_pos]}'.");
        }
        return value;
    }

    private double ParseExpression()
    {
        double value = ParseTerm();
        while (true)
        {
            SkipWhite();
            if (Match('+')) value += ParseTerm();
            else if (Match('-')) value -= ParseTerm();
            else return value;
        }
    }

    private double ParseTerm()
    {
        double value = ParsePower();
        while (true)
        {
            SkipWhite();
            if (Match('*')) value *= ParsePower();
            else if (Match('/')) value /= ParsePower();
            else return value;
        }
    }

    private double ParsePower()
    {
        double value = ParseUnary();
        SkipWhite();
        if (Match('^'))
        {
            double exponent = ParsePower();
            value = Math.Pow(value, exponent);
        }
        return value;
    }

    private double ParseUnary()
    {
        SkipWhite();
        if (Match('+')) return ParseUnary();
        if (Match('-')) return -ParseUnary();
        return ParsePrimary();
    }

    private double ParsePrimary()
    {
        SkipWhite();
        if (Match('('))
        {
            double value = ParseExpression();
            Require(')');
            return value;
        }

        if (char.IsDigit(Current) || Current == '.')
        {
            return ParseNumber();
        }

        if (char.IsLetter(Current))
        {
            string ident = ParseIdentifier().ToLowerInvariant();
            SkipWhite();
            if (Match('('))
            {
                var args = new List<double>();
                SkipWhite();
                if (!Peek(')'))
                {
                    while (true)
                    {
                        args.Add(ParseExpression());
                        SkipWhite();
                        if (Match(',')) continue;
                        break;
                    }
                }
                Require(')');
                return ApplyFunction(ident, args);
            }

            return ident switch
            {
                "x" => _x,
                "y" => _y,
                "z" => _z,
                "pi" => Math.PI,
                "e" => Math.E,
                _ => throw new InvalidOperationException($"Unknown name '{ident}'. Use x, y, z, pi, e, or a supported function.")
            };
        }

        throw new InvalidOperationException($"Expected number, variable, function, or '(' at character {_pos + 1}.");
    }

    private double ApplyFunction(string name, List<double> args)
    {
        static void RequireCount(string name, List<double> args, int count)
        {
            if (args.Count != count)
            {
                throw new InvalidOperationException($"Function '{name}' expects {count} argument(s), but got {args.Count}.");
            }
        }

        return name switch
        {
            "sin" => One(name, args, Math.Sin),
            "cos" => One(name, args, Math.Cos),
            "tan" => One(name, args, Math.Tan),
            "atan" => One(name, args, Math.Atan),
            "asin" => One(name, args, Math.Asin),
            "acos" => One(name, args, Math.Acos),
            "sqrt" => One(name, args, Math.Sqrt),
            "abs" => One(name, args, Math.Abs),
            "log" => One(name, args, Math.Log),
            "log10" => One(name, args, Math.Log10),
            "exp" => One(name, args, Math.Exp),
            "floor" => One(name, args, Math.Floor),
            "ceil" => One(name, args, Math.Ceiling),
            "min" => Two(name, args, Math.Min),
            "max" => Two(name, args, Math.Max),
            _ => throw new InvalidOperationException($"Unsupported function '{name}'. Supported: sin, cos, tan, atan, asin, acos, sqrt, abs, log, log10, exp, floor, ceil, min, max.")
        };

        static double One(string name, List<double> args, Func<double, double> fn)
        {
            RequireCount(name, args, 1);
            return fn(args[0]);
        }

        static double Two(string name, List<double> args, Func<double, double, double> fn)
        {
            RequireCount(name, args, 2);
            return fn(args[0], args[1]);
        }
    }

    private double ParseNumber()
    {
        int start = _pos;
        while (char.IsDigit(Current) || Current == '.') _pos++;
        if (Current == 'e' || Current == 'E')
        {
            _pos++;
            if (Current == '+' || Current == '-') _pos++;
            while (char.IsDigit(Current)) _pos++;
        }

        string text = _text[start.._pos];
        if (!double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value))
        {
            throw new InvalidOperationException($"Invalid number '{text}'.");
        }
        return value;
    }

    private string ParseIdentifier()
    {
        int start = _pos;
        while (char.IsLetterOrDigit(Current) || Current == '_') _pos++;
        return _text[start.._pos];
    }

    private char Current => _pos < _text.Length ? _text[_pos] : '\0';

    private void SkipWhite()
    {
        while (char.IsWhiteSpace(Current)) _pos++;
    }

    private bool Match(char c)
    {
        if (Current == c)
        {
            _pos++;
            return true;
        }
        return false;
    }

    private bool Peek(char c) => Current == c;

    private void Require(char c)
    {
        SkipWhite();
        if (!Match(c))
        {
            throw new InvalidOperationException($"Expected '{c}' at character {_pos + 1}.");
        }
    }
}
