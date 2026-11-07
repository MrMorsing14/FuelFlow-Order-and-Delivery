namespace FuelFlow.Api.IntegrationTests;

/// <summary>A clock tests can move forward or back. (FakeTimeProvider cannot go back, so it cannot be reset between tests.)</summary>
public sealed class SettableTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Set(DateTimeOffset now) => _now = now;

    public void Advance(TimeSpan by) => _now += by;
}
