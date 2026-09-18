using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Divinity.PlatformApi.Observability;

public static class PlatformTelemetry
{
    public const string ServiceName = "platform-api";
    public static readonly ActivitySource ActivitySource = new("Divinity.PlatformApi");

    private static readonly Meter Meter = new("Divinity.PlatformApi", "1.0.0");
    private static readonly Counter<long> GameTickets = Meter.CreateCounter<long>("divinity.platform.game_tickets");
    private static readonly Counter<long> Characters = Meter.CreateCounter<long>("divinity.platform.characters");

    public static Activity? StartActivity(string name, string eventType, string accountPseudonym)
    {
        var activity = ActivitySource.StartActivity(name, ActivityKind.Server);
        if (activity is null)
        {
            return null;
        }

        activity.SetTag("service", ServiceName);
        activity.SetTag("event_type", eventType);
        activity.SetTag("account_pseudonym", accountPseudonym);
        return activity;
    }

    public static void RecordGameTicket(string result) =>
        GameTickets.Add(1, new KeyValuePair<string, object?>("result", result));

    public static void RecordCharacter(string operation, string result) =>
        Characters.Add(1, new KeyValuePair<string, object?>("operation", operation), new KeyValuePair<string, object?>("result", result));
}
