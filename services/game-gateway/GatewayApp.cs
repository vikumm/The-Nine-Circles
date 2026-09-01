using Divinity.GameGateway.Protocol;
using Divinity.ContractsProto.GameTickets;
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

        var app = builder.Build();

        app.MapGet("/healthz", () => new
        {
            service = GameGatewayInfo.ComponentName,
            status = GameGatewayInfo.Status,
            consumesGameTickets = GameGatewayInfo.ConsumesGameTickets
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

        return app;
    }
}
