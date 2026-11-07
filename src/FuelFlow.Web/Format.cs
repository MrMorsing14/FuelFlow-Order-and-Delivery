using System.Globalization;

namespace FuelFlow.Web;

/// <summary>
/// Danish number and date formatting for display. Uses explicit formats instead of the "da-DK" culture: the browser
/// build does not ship Danish culture data (it would silently fall back to English), and this is the same everywhere.
/// </summary>
public static class Format
{
    private static readonly NumberFormatInfo _danish = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = ".",
    };

    public static string Money(decimal amount) => $"{amount.ToString("N2", _danish)} kr.";

    public static string Litres(int litres) => $"{litres.ToString("N0", _danish)} L";

    /// <summary>In the viewer's own time zone; the API sends UTC.</summary>
    public static string Date(DateTimeOffset value) => value.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    public static string Date(DateOnly value) => value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
}
