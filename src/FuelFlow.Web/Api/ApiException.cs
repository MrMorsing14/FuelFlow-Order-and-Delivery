namespace FuelFlow.Web.Api;

/// <summary>An error answer from the API (a ProblemDetails body), or a network failure (Status 0).</summary>
public sealed class ApiException(int status, string message, IReadOnlyDictionary<string, string[]>? fieldErrors = null) : Exception(message)
{
    public int Status { get; } = status;

    /// <summary>Validation messages per field, e.g. "postalCode" -> ["Postal code must be 4 digits"].</summary>
    public IReadOnlyDictionary<string, string[]> FieldErrors { get; } = fieldErrors ?? new Dictionary<string, string[]>();
}
