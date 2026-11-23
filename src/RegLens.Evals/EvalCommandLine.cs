using System.Globalization;

namespace RegLens.Evals;

/// <summary><c>&lt;command&gt; [--config name] [--floor x] [--confirm-spend]</c> (spec 0004, R4.8–R4.12).</summary>
public sealed record EvalCommandLine(string Name, EvalConfiguration Config, bool ConfirmSpend, double? Floor)
{
    public static readonly IReadOnlyList<string> Commands = ["check", "retrieval", "answers", "floor", "probe", "report", "judge-sheet"];

    /// <summary>Commands that call the Anthropic API and so need <c>--confirm-spend</c> (P11).</summary>
    public bool Spends => Name is "answers" or "probe";

    public static EvalCommandLine Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Count == 0 || !Commands.Contains(args[0]))
        {
            throw new ArgumentException($"Usage: <{string.Join("|", Commands)}> [--config name] [--floor x] [--confirm-spend]");
        }

        var config = EvalConfiguration.Find("hybrid-tsrank");
        var confirm = false;
        double? floor = null;
        for (var i = 1; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--config" when i + 1 < args.Count:
                    config = EvalConfiguration.Find(args[++i]);
                    break;
                case "--floor" when i + 1 < args.Count
                    && double.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                    && value is >= 0 and <= 1:
                    floor = value;
                    i++;
                    break;
                case "--confirm-spend":
                    confirm = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete option '{args[i]}'.");
            }
        }

        return new EvalCommandLine(args[0], config, confirm, floor);
    }
}
