namespace FuelFlow.Domain.Common;

/// <summary>
/// Danish company registration number: 8 digits that pass the modulus-11 check.
/// A readonly record struct is a small immutable value type with value equality
/// (closest Java equivalent: a record, but allocated on the stack).
/// </summary>
public readonly record struct CvrNumber
{
    private static readonly int[] _weights = [2, 7, 6, 5, 4, 3, 2, 1];

    public string Value { get; }

    private CvrNumber(string value) => Value = value;

    public static bool TryCreate(string? raw, out CvrNumber cvr)
    {
        cvr = default;
        var value = raw?.Trim();
        if (value is not { Length: 8 } || !value.All(char.IsAsciiDigit))
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < _weights.Length; i++)
        {
            sum += (value[i] - '0') * _weights[i];
        }

        if (sum % 11 != 0)
        {
            return false;
        }

        cvr = new CvrNumber(value);
        return true;
    }

    public static CvrNumber Create(string? raw) =>
        TryCreate(raw, out var cvr) ? cvr : throw new DomainValidationException("CVR must be a valid 8-digit Danish CVR number.");

    public override string ToString() => Value;
}
