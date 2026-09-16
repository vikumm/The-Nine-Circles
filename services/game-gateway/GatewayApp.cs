using Divinity.ContractsProto.GameTickets;
using Divinity.GameGateway.Protocol;
using Divinity.GameGateway.Session;
using Divinity.GameRules.Characters;
using Divinity.WorldRuntime.Map;
using Divinity.WorldRuntime.Movement;
using Google.Protobuf;
using Microsoft.AspNetCore.Http;

namespace Divinity.GameGateway;

public static class GatewayApp
{
    public static WebApplication Build(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IGameTicketStore>(_ => GameTicketStoreFactory.CreateFromEnvironment());
        builder.Services.AddSingleton<GameTicketService>();
        builder.Services.AddSingleton<ICharacterStore>(_ => CharacterStoreFactory.CreateFromEnvironment());
        builder.Services.AddSingleton<CharacterService>();
        builder.Services.AddSingleton(_ => WorldMapCatalog.LoadDefaultAsync().GetAwaiter().GetResult());
        builder.Services.AddSingleton<WorldMovementRuntime>();
        builder.Services.AddSingleton<GatewaySessionManager>();
        builder.Services.AddSingleton<AnonymousHandshakeRateLimiter>();
        builder.Services.AddSingleton<GatewayWebSocketHandler>();

        var app = builder.Build();
        app.UseWebSockets();

        app.MapGet("/healthz", () => new
        {
            service = GameGatewayInfo.ComponentName,
            status = GameGatewayInfo.Status,
            implementsWssHandshake = GameGatewayInfo.ImplementsWssHandshake,
            consumesGameTickets = GameGatewayInfo.ConsumesGameTickets,
            verifiesCharacterOwnership = GameGatewayInfo.VerifiesCharacterOwnership,
            routesGameplayIntents = GameGatewayInfo.RoutesGameplayIntents
        });

        app.MapGet("/protocol/v1/ws", async (HttpContext context, AnonymousHandshakeRateLimiter rateLimiter, GatewayWebSocketHandler handler) =>
        {
            var remoteAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (!rateLimiter.TryAcquire(remoteAddress))
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.Response.WriteAsync("HANDSHAKE_RATE_LIMITED", context.RequestAborted);
                return;
            }

            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("WebSocket upgrade is required.", context.RequestAborted);
                return;
            }

            var connectionId = "conn_" + Guid.NewGuid().ToString("N");
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await handler.HandleAsync(socket, connectionId, context.RequestAborted);
        });

        app.MapPost("/protocol/v1/client-hello", async (HttpRequest request, HttpResponse httpResponse, CancellationToken cancellationToken) =>
        {
            var ticketService = request.HttpContext.RequestServices.GetRequiredService<GameTicketService>();
            var body = await ProtocolV1Handler.ReadBoundedBodyAsync(request.Body, request.ContentLength, cancellationToken);
            var response = body.TooLarge
                ? ProtocolV1Handler.CreatePayloadTooLargeResponse()
                : await ProtocolV1Handler.HandleClientEnvelopeAsync(body.Payload, ticketService, cancellationToken);

            httpResponse.StatusCode = (int)response.StatusCode;
            httpResponse.ContentType = "application/x-protobuf";
            await httpResponse.Body.WriteAsync(response.Envelope.ToByteArray(), cancellationToken);
        });

        app.Lifetime.ApplicationStopping.Register(() =>
        {
            var worldRuntime = app.Services.GetRequiredService<WorldMovementRuntime>();
            worldRuntime.SaveAllCheckpointsAsync("shutdown", CancellationToken.None).GetAwaiter().GetResult();
        });

        return app;
    }
}
