using Divinity.GameGateway.Protocol;
using Google.Protobuf;
using Microsoft.AspNetCore.Http;

namespace Divinity.GameGateway;

public static class GatewayApp
{
    public static WebApplication Build(WebApplicationBuilder builder)
    {
        var app = builder.Build();

        app.MapGet("/healthz", () => new
        {
            service = GameGatewayInfo.ComponentName,
            status = GameGatewayInfo.Status
        });

        app.MapPost("/protocol/v1/client-hello", async (HttpRequest request, HttpResponse httpResponse, CancellationToken cancellationToken) =>
        {
            var body = await ProtocolV1Handler.ReadBoundedBodyAsync(request.Body, request.ContentLength, cancellationToken);
            var response = body.TooLarge
                ? ProtocolV1Handler.CreatePayloadTooLargeResponse()
                : ProtocolV1Handler.HandleClientEnvelope(body.Payload);

            httpResponse.StatusCode = (int)response.StatusCode;
            httpResponse.ContentType = "application/x-protobuf";
            await httpResponse.Body.WriteAsync(response.Envelope.ToByteArray(), cancellationToken);
        });

        return app;
    }
}
