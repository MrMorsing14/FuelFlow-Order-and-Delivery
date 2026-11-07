using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FuelFlow.Api.Middleware;
using FuelFlow.Application;
using FuelFlow.Domain.Pricing;
using FuelFlow.Infrastructure;
using FuelFlow.Infrastructure.Persistence;
using FuelFlow.Infrastructure.Seeding;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Enums travel as strings ("Private", "Business") in JSON.
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();

// Throttle login/register: each attempt costs a slow password hash and invites password guessing.
// shortcut: partitions by the connection's IP, which is the proxy's IP behind an ingress; add UseForwardedHeaders when the ingress arrives.
builder.Services.AddRateLimiter(options => options.RejectionStatusCode = StatusCodes.Status429TooManyRequests);
builder.Services.AddOptions<RateLimiterOptions>().Configure<IConfiguration>((options, config) =>
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = config.GetValue("RateLimiting:AuthPermitLimit", 10),
            Window = TimeSpan.FromMinutes(1),
        })));
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();

// Inject TimeProvider wherever time is needed; tests swap in a FakeTimeProvider.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<PricingOptions>().BindConfiguration(PricingOptions.SectionName);
builder.Services.AddSingleton(sp => new PriceCalculator(sp.GetRequiredService<IOptions<PricingOptions>>().Value));
builder.Services.AddApplication();
builder.Services.AddInfrastructure();

// "live" has no checks (the process is up). "ready" includes the database, for Kubernetes probes.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<FuelFlowDbContext>("database", tags: ["ready"]);

var app = builder.Build();

// Fail fast on missing configuration instead of at the first request.
if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString(DependencyInjection.ConnectionStringName)))
{
    throw new InvalidOperationException(
        $"Connection string '{DependencyInjection.ConnectionStringName}' is missing. Set ConnectionStrings__{DependencyInjection.ConnectionStringName}.");
}

// Migrations and demo data are applied automatically ONLY in Development.
if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<FuelFlowDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<DevelopmentDataSeeder>().SeedAsync();

    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseExceptionHandler();
app.UseStatusCodePages(); // plain 401/403/404 responses also get a ProblemDetails body
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.Run();
