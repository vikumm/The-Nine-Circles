using System.Security.Claims;
using Divinity.ContractsProto.GameTickets;
using Divinity.GameRules.Characters;
using Divinity.PlatformApi.Observability;
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
        builder.Services.AddSingleton<ICharacterStore>(_ => CharacterStoreFactory.CreateFromEnvironment());
        builder.Services.AddSingleton<CharacterService>();

        var app = builder.Build();

        app.MapGet("/healthz", () => new
        {
            service = PlatformApiInfo.ComponentName,
            status = PlatformApiInfo.Status,
            issuesGameTickets = PlatformApiInfo.IssuesGameTickets,
            createsKnight = PlatformApiInfo.CreatesKnight,
            exposesTelemetry = PlatformApiInfo.ExposesTelemetry,
            avoidsSecretLogs = PlatformApiInfo.AvoidsSecretLogs
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
                PlatformTelemetry.RecordGameTicket("unauthorized");
                return TypedResults.Unauthorized();
            }

            using var activity = PlatformTelemetry.StartActivity("divinity.platform.game_ticket", "game_ticket_issue", AccountPseudonym(accountId));
            var result = await ticketService.IssueAsync(
                new GameTicketIssueCommand(accountId, request.BuildId, request.ProtocolVersion, request.Nonce),
                cancellationToken);
            PlatformTelemetry.RecordGameTicket(result.Status.ToString());

            if (!result.Success)
            {
                return TypedResults.BadRequest(new GameTicketIssueErrorResponse(result.Status.ToString(), result.Message));
            }

            return TypedResults.Ok(new GameTicketIssueHttpResponse(
                result.GameTicket!,
                result.ExpiresAtUtc!.Value,
                GameTicketDefaults.TimeToLive.TotalSeconds));
        });

        app.MapPost("/characters/knight", async Task<IResult> (
            HttpContext context,
            CharacterCreateHttpRequest request,
            CharacterService characterService,
            CancellationToken cancellationToken) =>
        {
            var accountId = ResolveAccountId(context);
            if (accountId is null)
            {
                PlatformTelemetry.RecordCharacter("create_knight", "unauthorized");
                return TypedResults.Unauthorized();
            }

            using var activity = PlatformTelemetry.StartActivity("divinity.platform.character_create", "character_create", AccountPseudonym(accountId));
            var result = await characterService.CreateKnightAsync(new CreateKnightCommand(accountId, request.Name), cancellationToken);
            PlatformTelemetry.RecordCharacter("create_knight", result.Status.ToString());
            return result.Status switch
            {
                CharacterServiceStatus.Created => TypedResults.Created($"/characters/knight/{result.Character!.CharacterId}", CharacterHttpResponse.FromCharacter(result.Character)),
                CharacterServiceStatus.InvalidName => TypedResults.BadRequest(CharacterHttpErrorResponse.FromResult(result)),
                CharacterServiceStatus.DuplicateName or CharacterServiceStatus.SlotOccupied => TypedResults.Conflict(CharacterHttpErrorResponse.FromResult(result)),
                _ => TypedResults.BadRequest(CharacterHttpErrorResponse.FromResult(result))
            };
        });

        app.MapGet("/characters/knight", async Task<IResult> (
            HttpContext context,
            CharacterService characterService,
            CancellationToken cancellationToken) =>
        {
            var accountId = ResolveAccountId(context);
            if (accountId is null)
            {
                PlatformTelemetry.RecordCharacter("select_knight", "unauthorized");
                return TypedResults.Unauthorized();
            }

            using var activity = PlatformTelemetry.StartActivity("divinity.platform.character_select", "character_select", AccountPseudonym(accountId));
            var result = await characterService.SelectKnightAsync(accountId, cancellationToken);
            PlatformTelemetry.RecordCharacter("select_knight", result.Status.ToString());
            return result.Status == CharacterServiceStatus.Selected
                ? TypedResults.Ok(CharacterHttpResponse.FromCharacter(result.Character!))
                : TypedResults.NotFound(CharacterHttpErrorResponse.FromResult(result));
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

    private static string AccountPseudonym(string accountId) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(accountId))).ToLowerInvariant()[..16];
}

public sealed record GameTicketIssueHttpRequest(string BuildId, uint ProtocolVersion, string Nonce);

public sealed record GameTicketIssueHttpResponse(string GameTicket, DateTimeOffset ExpiresAtUtc, double TtlSeconds);

public sealed record GameTicketIssueErrorResponse(string Code, string Message);

public sealed record CharacterCreateHttpRequest(string Name);

public sealed record CharacterHttpResponse(
    string CharacterId,
    string DisplayName,
    string NormalizedName,
    string Vocation,
    string CatalogVersion,
    KnightStatsHttpResponse Stats,
    string MapId,
    string ChannelId,
    string ContentHash,
    CharacterPositionHttpResponse SafeSpawn)
{
    public static CharacterHttpResponse FromCharacter(CharacterRecord character) =>
        new(
            character.CharacterId,
            character.DisplayName,
            character.NormalizedName,
            character.Vocation,
            character.CatalogVersion,
            KnightStatsHttpResponse.FromStats(character.Stats),
            character.MapId,
            character.ChannelId,
            character.ContentHash,
            new CharacterPositionHttpResponse(character.SafeSpawn.X, character.SafeSpawn.Y));
}

public sealed record KnightStatsHttpResponse(
    int Level,
    int MaxHp,
    int MaxMp,
    int Attack,
    int Defense,
    decimal CriticalChancePercent,
    decimal CriticalMultiplier,
    decimal SpeedUnitsPerSecond,
    int HpRegenPerSecond,
    int MpRegenPerSecond)
{
    public static KnightStatsHttpResponse FromStats(KnightStats stats) =>
        new(
            stats.Level,
            stats.MaxHp,
            stats.MaxMp,
            stats.Attack,
            stats.Defense,
            stats.CriticalChancePercent,
            stats.CriticalMultiplier,
            stats.SpeedUnitsPerSecond,
            stats.HpRegenPerSecond,
            stats.MpRegenPerSecond);
}

public sealed record CharacterPositionHttpResponse(decimal X, decimal Y);

public sealed record CharacterHttpErrorResponse(string Code, string Message)
{
    public static CharacterHttpErrorResponse FromResult(CharacterServiceResult result) =>
        new(result.Status.ToString(), result.Message);
}
