namespace RegLens.Domain.Chunking;

/// <summary>Reads chapter numbers such as "IX" or "XI".</summary>
public static class RomanNumeral
{
    public static int? ToInt(string numeral)
    {
        ArgumentNullException.ThrowIfNull(numeral);

        var total = 0;
        for (var i = 0; i < numeral.Length; i++)
        {
            var value = ValueOf(numeral[i]);
            if (value == 0)
            {
                return null;
            }

            var next = i + 1 < numeral.Length ? ValueOf(numeral[i + 1]) : 0;
            total += value < next ? -value : value;
        }

        return total > 0 ? total : null;
    }

    private static int ValueOf(char c) => c switch
    {
        'I' => 1,
        'V' => 5,
        'X' => 10,
        'L' => 50,
        'C' => 100,
        _ => 0,
    };
}
