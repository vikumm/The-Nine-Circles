using System.Diagnostics;
using System.Diagnostics.Metrics;
using Divinity.Contracts.V1;
using Divinity.GameGateway.Session;

namespace Divinity.GameGateway.Observability;

public static class GatewayTelemetry
{
    public const string ServiceName = "game-gateway";
    public static readonly ActivitySource ActivitySource = new("Divinity.GameGateway");

    private static readonly Meter Meter = new("Divinity.GameGateway", "1.0.0");
    private static readonly Counter<long> MessageCounter = Meter.CreateCounter<long>("divinity.gateway.messages");
    private static readonly Counter<long> RateLimitCounter = Meter.CreateCounter<long>("divinity.gateway.rate_limits");
    private static readonly Counter<long> AuthenticationCounter = Meter.CreateCounter<long>("divinity.gateway.authentications");
    private static readonly Counter<long> DisconnectCounter = Meter.CreateCounter<long>("divinity.gateway.disconnects");
    private static readonly Histogram<double> LatencyHistogram = Meter.CreateHistogram<double>("divinity.gateway.latency_ms");

    public static Activity? StartActivity(
        string name,
        string connectionId,
        string accountPseudonym,
        string? characterId,
        string? mapId,
        string eventType)
    {
        var activity = ActivitySource.StartActivity(name, ActivityKind.Server);
        if (activity is null)
        {
            return null;
        }

        activity.SetTag("service", ServiceName);
        activity.SetTag("connection_id", connectionId);
        activity.SetTag("account_pseudonym", accountPseudonym);
        activity.SetTag("character_id", characterId ?? string.Empty);
        activity.SetTag("map_id", mapId ?? string.Empty);
        activity.SetTag("event_type", eventType);
        return activity;
    }

    public static GatewayStructuredLog CreateLog(
        string environment,
        string connectionId,
        string accountPseudonym,
        string? characterId,
        string? mapId,
        string eventType,
        string result,
        ErrorCode errorCode,
        double latencyMs) =>
        new(
            DateTimeOffset.UtcNow,
            ServiceName,
            environment,
            Activity.Current?.TraceId.ToString() ?? string.Empty,
            connectionId,
            accountPseudonym,
            characterId ?? string.Empty,
            mapId ?? string.Empty,
            eventType,
            result,
            errorCode.ToString(),
            latencyMs);

    public static void RecordMessage(string messageType, string result, double latencyMs = 0)
    {
        MessageCounter.Add(1, new KeyValuePair<string, object?>("message_type", messageType), new KeyValuePair<string, object?>("result", result));
        if (latencyMs > 0)
        {
            LatencyHistogram.Record(latencyMs, new KeyValuePair<string, object?>("operation", messageType));
        }
    }

    public static void RecordAuthentication(bool accepted) =>
        AuthenticationCounter.Add(1, new KeyValuePair<string, object?>("result", accepted ? "accepted" : "rejected"));

    public static void RecordRateLimit(GatewayRateLimitCategory category) =>
        RateLimitCounter.Add(1, new KeyValuePair<string, object?>("category", category.ToString()));

    public static void RecordDisconnect(string reason) =>
        DisconnectCounter.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public static bool ContainsForbiddenSecret(string value) =>
        value.Contains("gt_", StringComparison.OrdinalIgnoreCase)
        || value.Contains("rt_", StringComparison.OrdinalIgnoreCase)
        || value.Contains("access_token", StringComparison.OrdinalIgnoreCase)
        || value.Contains("password", StringComparison.OrdinalIgnoreCase)
        || value.Contains("game_ticket", StringComparison.OrdinalIgnoreCase)
        || value.Contains("reconnect_token", StringComparison.OrdinalIgnoreCase);
}

public sealed record GatewayStructuredLog(
    DateTimeOffset TimestampUtc,
    string Service,
    string Environment,
    string TraceId,
    string ConnectionId,
    string AccountPseudonym,
    string CharacterId,
    string MapId,
    string EventType,
    string Result,
    string ErrorCode,
    double LatencyMs);
