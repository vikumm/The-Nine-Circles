using System.Security.Claims;
using Divinity.ContractsProto.GameTickets;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Divinity.PlatformApi;

public static class PlatformApiApp
{
    private const string DevAccountHeader = "X-Divinity-Dev-Account-Id";

    public static WebApplication Build(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IGameTicketStore>(_ => GameTicketStoreFactory.CreateFromEnvironment());
        builder.Services.AddSingleton<GameTicketService>();

        var app = builder.Build();

        app.MapGet("/healthz", () => new
        {
            service = PlatformApiInfo.ComponentName,
            status = PlatformApiInfo.Status,
            issuesGameTickets = PlatformApiInfo.IssuesGameTickets
        });

        app.MapPost("/launcher/game-ticket", async Task<Results<Ok<GameTicketIssueHttpResponse>, UnauthorizedHttpResult, BadRequest<GameTicketIssueErrorResponse>>> (
            HttpContext context,
            GameTicketIssueHttpRequest request,
            GameTicketService ticketService,
            CancellationToken cancellationToken) =>
        {
            var accountId = ResolveAccountId(context);
            if (accountId is null)
            {
                return TypedResults.Unauthorized();
            }

            var result = await ticketService.IssueAsync(
                new GameTicketIssueCommand(accountId, request.BuildId, request.ProtocolVersion, request.Nonce),
                cancellationToken);

            if (!result.Success)
            {
                return TypedResults.BadRequest(new GameTicketIssueErrorResponse(result.Status.ToString(), result.Message));
            }

            return TypedResults.Ok(new GameTicketIssueHttpResponse(
                result.GameTicket!,
                result.ExpiresAtUtc!.Value,
                GameTicketDefaults.TimeToLive.TotalSeconds));
        });

        return app;
    }

    private static string? ResolveAccountId(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            return context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? context.User.FindFirstValue("sub")
                ?? context.User.Identity.Name;
        }

        var allowDevHeader = context.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment()
            && string.Equals(
                Environment.GetEnvironmentVariable("DIVINITY_PLATFORM_API_ALLOW_DEV_AUTH_HEADER"),
                "true",
                StringComparison.OrdinalIgnoreCase);

        if (allowDevHeader && context.Request.Headers.TryGetValue(DevAccountHeader, out var accountId) && !string.IsNullOrWhiteSpace(accountId))
        {
            return accountId.ToString();
        }

        return null;
    }
}

public sealed record GameTicketIssueHttpRequest(string BuildId, uint ProtocolVersion, string Nonce);

public sealed record GameTicketIssueHttpResponse(string GameTicket, DateTimeOffset ExpiresAtUtc, double TtlSeconds);

public sealed record GameTicketIssueErrorResponse(string Code, string Message);
