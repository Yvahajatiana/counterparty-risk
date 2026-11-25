using CounterpartyRisk.Domain.Contracts.Results;

namespace CounterpartyRisk.Domain.Contracts.ReferenceData;

/// <summary>
/// Legal Entity Identifier (ISO 17442): 18 upper-case alphanumeric characters followed by two
/// check digits, valid when the ISO 7064 MOD 97-10 checksum of the whole code equals 1.
/// </summary>
public readonly record struct Lei
{
    public const int Length = 20;

    private Lei(string value)
    {
        Value = value;
    }

    public string Value { get; }

    /// <summary>Validates the format and the check digits; never throws on bad input.</summary>
    public static Result<Lei> Create(string? value)
    {
        if (value is null || value.Length != Length)
        {
            return Error.Validation("lei.invalid_length", "A LEI has exactly 20 characters.");
        }

        for (var i = 0; i < Length; i++)
        {
            var c = value[i];
            var allowed = i < Length - 2 ? char.IsAsciiDigit(c) || char.IsAsciiLetterUpper(c) : char.IsAsciiDigit(c);
            if (!allowed)
            {
                return Error.Validation("lei.invalid_format", "A LEI is 18 upper-case alphanumeric characters and 2 check digits.");
            }
        }

        return Mod97(value) == 1
            ? new Lei(value)
            : Error.Validation("lei.invalid_checksum", "The LEI check digits are invalid.");
    }

    public override string ToString() => Value ?? string.Empty;

    // Letters count as two digits (A = 10 ... Z = 35); the remainder is folded digit by digit,
    // so no big integer is needed.
    private static int Mod97(string value)
    {
        var remainder = 0;
        foreach (var c in value)
        {
            if (char.IsAsciiDigit(c))
            {
                remainder = ((remainder * 10) + (c - '0')) % 97;
            }
            else
            {
                remainder = ((remainder * 100) + (c - 'A' + 10)) % 97;
            }
        }

        return remainder;
    }
}
