using System.Net;
using FuelFlow.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace FuelFlow.Api.IntegrationTests;

public class ErrorHandlingTests(FuelFlowApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task UnhandledException_Returns500ProblemDetailsWithoutInternals()
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync("/test/boom", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldNotContain("secret internal detail");
        body.ShouldNotContain("InvalidOperationException");
    }

    [Fact]
    public async Task TryHandleAsync_ClientAbortedRequest_IsSwallowedWithoutLoggingAnError()
    {
        var problems = Substitute.For<IProblemDetailsService>();
        var logger = Substitute.For<ILogger<GlobalExceptionHandler>>();
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();
        var context = new DefaultHttpContext { RequestAborted = aborted.Token };

        var handled = await new GlobalExceptionHandler(problems, logger)
            .TryHandleAsync(context, new OperationCanceledException(), TestContext.Current.CancellationToken);

        handled.ShouldBeTrue();
        logger.ReceivedCalls().ShouldBeEmpty();
        await problems.DidNotReceive().TryWriteAsync(Arg.Any<ProblemDetailsContext>());
    }
}
