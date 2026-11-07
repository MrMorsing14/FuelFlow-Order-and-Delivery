namespace FuelFlow.Domain.Common;

internal static class Guard
{
    public static string NotBlank(string? value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new DomainValidationException($"{name} is required.")
            : value.Trim();

    public static int Positive(int value, string name) =>
        value <= 0 ? throw new DomainValidationException($"{name} must be greater than zero.") : value;

    public static decimal Positive(decimal value, string name) =>
        value <= 0 ? throw new DomainValidationException($"{name} must be greater than zero.") : value;
}
