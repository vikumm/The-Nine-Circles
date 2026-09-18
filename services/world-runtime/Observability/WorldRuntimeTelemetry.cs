using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Divinity.WorldRuntime.Observability;

public static class WorldRuntimeTelemetry
{
    public const string ServiceName = "world-runtime";
    public static readonly ActivitySource ActivitySource = new("Divinity.WorldRuntime");

    private static readonly Meter Meter = new("Divinity.WorldRuntime", "1.0.0");
    private static readonly Counter<long> MovementCorrections = Meter.CreateCounter<long>("divinity.world.movement_corrections");
    private static readonly Counter<long> DamageRejected = Meter.CreateCounter<long>("divinity.world.damage_rejected");
    private static readonly Counter<long> MonsterDeaths = Meter.CreateCounter<long>("divinity.world.monster_deaths");
    private static readonly Counter<long> RewardsGranted = Meter.CreateCounter<long>("divinity.world.rewards_granted");
    private static readonly Counter<long> RewardDuplicatesBlocked = Meter.CreateCounter<long>("divinity.world.reward_duplicates_blocked");
    private static readonly Histogram<double> TickDuration = Meter.CreateHistogram<double>("divinity.world.tick_duration_ms");

    public static Activity? StartActivity(string name, string characterId, string mapId, string eventType)
    {
        var activity = ActivitySource.StartActivity(name, ActivityKind.Internal);
        if (activity is null)
        {
            return null;
        }

        activity.SetTag("service", ServiceName);
        activity.SetTag("character_id", characterId);
        activity.SetTag("map_id", mapId);
        activity.SetTag("event_type", eventType);
        return activity;
    }

    public static void RecordMovementCorrection(string reason) =>
        MovementCorrections.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public static void RecordDamageRejected(string reason) =>
        DamageRejected.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public static void RecordMonsterDeath(string templateId) =>
        MonsterDeaths.Add(1, new KeyValuePair<string, object?>("template_id", templateId));

    public static void RecordReward(string status)
    {
        if (string.Equals(status, "AlreadyGranted", StringComparison.Ordinal))
        {
            RewardDuplicatesBlocked.Add(1);
            return;
        }

        RewardsGranted.Add(1, new KeyValuePair<string, object?>("status", status));
    }

    public static void RecordTick(TimeSpan elapsed, int entities) =>
        TickDuration.Record(elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("entities", entities));
}
