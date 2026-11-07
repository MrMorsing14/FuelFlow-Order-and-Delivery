using FuelFlow.Domain.Common;

namespace FuelFlow.Domain.Scheduling;

/// <summary>A delivery region: a named range of postal codes, e.g. "Copenhagen" = 1000-2999.</summary>
public sealed class RegionRange
{
    public string Name { get; set; } = "";
    public int From { get; set; }
    public int To { get; set; }
}

/// <summary>Bound from configuration (section "Delivery").</summary>
public sealed class DeliveryOptions
{
    public const string SectionName = "Delivery";

    public List<RegionRange> Regions { get; set; } = [];
}

/// <summary>Maps postal codes to delivery regions. Built once at startup; invalid configuration stops the app.</summary>
public sealed class RegionMap
{
    private readonly List<RegionRange> _ranges;

    public RegionMap(IEnumerable<RegionRange> ranges)
    {
        _ranges = [.. ranges.Select(r => new RegionRange { Name = r.Name?.Trim() ?? "", From = r.From, To = r.To })];

        if (_ranges.Count == 0)
        {
            throw new DomainValidationException("At least one delivery region must be configured (Delivery:Regions).");
        }

        foreach (var range in _ranges)
        {
            if (range.Name.Length == 0 || range.From > range.To || range.From < 1000 || range.To > 9999)
            {
                throw new DomainValidationException($"Delivery region '{range.Name}' is invalid: it needs a name and 1000 <= From <= To <= 9999.");
            }
        }

        if (_ranges.Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != _ranges.Count)
        {
            throw new DomainValidationException("Delivery region names must be unique.");
        }

        var ordered = _ranges.OrderBy(r => r.From).ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].From <= ordered[i - 1].To)
            {
                throw new DomainValidationException($"Delivery regions '{ordered[i - 1].Name}' and '{ordered[i].Name}' overlap.");
            }
        }
    }

    public IReadOnlyList<RegionRange> Regions => _ranges;

    /// <summary>The region a postal code belongs to, or null if it falls in no configured range.</summary>
    public string? RegionOf(PostalCode postalCode)
    {
        var number = postalCode.AsNumber();
        return _ranges.FirstOrDefault(r => number >= r.From && number <= r.To)?.Name;
    }

    /// <summary>Finds a region by name, ignoring case. Returns the configured spelling.</summary>
    public RegionRange? Find(string? name) =>
        _ranges.FirstOrDefault(r => string.Equals(r.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));
}
