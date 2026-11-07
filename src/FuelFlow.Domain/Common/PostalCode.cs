namespace FuelFlow.Domain.Common;

/// <summary>Danish postal code: exactly 4 digits, 1000-9999.</summary>
public readonly record struct PostalCode
{
    public string Value { get; }

    private PostalCode(string value) => Value = value;

    public static bool TryCreate(string? raw, out PostalCode postalCode)
    {
        postalCode = default;
        var value = raw?.Trim();
        if (value is not { Length: 4 } || !value.All(char.IsAsciiDigit) || value[0] == '0')
        {
            return false;
        }

        postalCode = new PostalCode(value);
        return true;
    }

    public static PostalCode Create(string? raw) =>
        TryCreate(raw, out var code) ? code : throw new DomainValidationException("Postal code must be 4 digits (1000-9999).");

    public int AsNumber() => int.Parse(Value, System.Globalization.CultureInfo.InvariantCulture);

    public override string ToString() => Value;
}
