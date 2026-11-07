using FuelFlow.Application.Dispatch;
using Microsoft.Extensions.Options;

namespace FuelFlow.Api.Scheduling;

public sealed class SchedulingOptions
{
    public const string SectionName = "Scheduling";

    /// <summary>How often confirmed orders are assigned to delivery windows. 0 turns the worker off.</summary>
    public int IntervalMinutes { get; set; } = 5;
}

/// <summary>
/// Runs <see cref="SchedulingService"/> every few minutes. All the logic lives in the service and the scheduler,
/// so it can be tested without this class. Every API replica runs one; the database concurrency checks make that safe.
/// </summary>
public sealed class SchedulingWorker(
    IServiceScopeFactory scopes,
    IOptions<SchedulingOptions> options,
    TimeProvider timeProvider,
    ILogger<SchedulingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minutes = options.Value.IntervalMinutes;
        if (minutes <= 0)
        {
            logger.LogInformation("Scheduling worker is off (Scheduling:IntervalMinutes is {Minutes}).", minutes);
            return;
        }

        // PeriodicTimer + TimeProvider: tests drive the clock instead of waiting.
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes), timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    /// <summary>One scheduling run in its own DI scope (its own DbContext). Never throws: a failed run is retried next time.</summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<SchedulingService>().RunAsync(ct);
            if (result.Scheduled > 0)
            {
                logger.LogInformation("Scheduled {Scheduled} order(s); {Skipped} still waiting.", result.Scheduled, result.Skipped);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Scheduling run failed; it will be retried at the next interval.");
        }
    }
}
