using System.Diagnostics;
using Divinity.Contracts.V1;
using Divinity.GameRules.Movement;

var checks = new List<PredictionCheck>
{
    Check("pending inputs are reapplied after authoritative snapshot", PendingInputsReappliedAfterSnapshot()),
    Check("click-to-move target predicts local movement immediately", ClickTargetPredictsImmediately()),
    Check("small error is corrected smoothly", SmallErrorSmooths()),
    Check("large error snaps to authoritative state", LargeErrorSnaps()),
    Check("server Correction wins over local prediction", CorrectionWinsLocalState()),
    Check("diagonal tie preserves previous facing", DiagonalTiePreservesFacing()),
    Check("lost or delayed snapshot does not break local input", DelayedSnapshotDoesNotBreakInput()),
    Check("visual smoke keeps responsive 60 FPS movement budget", VisualSmokeKeepsBudget())
};

foreach (var check in checks)
{
    Console.WriteLine($"{(check.Passed ? "PASS" : "FAIL")} {check.Name}");
}

var failures = checks.Where(check => !check.Passed).ToArray();
if (failures.Length > 0)
{
    Console.Error.WriteLine($"VS-011 prediction tests failed: {failures.Length} check(s) failed.");
    return 1;
}

Console.WriteLine("VS-011 prediction tests passed.");
return 0;

static bool PendingInputsReappliedAfterSnapshot()
{
    var predictor = CreatePredictor();
    predictor.ApplyLocalInput(PredictedMoveIntent.Direction(1, 1, 1, 0));
    predictor.ApplyLocalInput(PredictedMoveIntent.Direction(2, 2, 1, 0));
    predictor.ApplyLocalInput(PredictedMoveIntent.Direction(3, 3, 1, 0));

    var envelope = CreateSnapshotEnvelope(snapshotId: 1, ackSequence: 1, x: MovementTuning.MaxDistancePerMoveIntent, y: 0);
    var result = predictor.ApplySnapshot(FromEnvelope(envelope));
    var state = result.State;
    var expectedX = MovementTuning.MaxDistancePerMoveIntent * 3d;

    return result.DroppedInputCount == 1
        && state.LastAckSequence == 1
        && state.PendingInputCount == 2
        && predictor.HasPendingSequence(2)
        && predictor.HasPendingSequence(3)
        && Near(state.PredictedX, expectedX)
        && Near(state.PredictedY, 0);
}

static bool ClickTargetPredictsImmediately()
{
    var predictor = CreatePredictor();
    var result = predictor.ApplyLocalInput(PredictedMoveIntent.ClickTarget(1, 1, 10, 0));

    return result.Accepted
        && result.State.PendingInputCount == 1
        && Near(result.State.PredictedX, MovementTuning.MaxDistancePerMoveIntent)
        && Near(result.State.RenderX, MovementTuning.MaxDistancePerMoveIntent)
        && Near(result.State.RenderY, 0)
        && result.State.Facing == CardinalDirection.East;
}

static bool SmallErrorSmooths()
{
    var predictor = CreatePredictor();
    predictor.ApplyLocalInput(PredictedMoveIntent.Direction(1, 1, 1, 0));

    var serverX = MovementTuning.MaxDistancePerMoveIntent - 0.10d;
    var result = predictor.ApplySnapshot(new AuthoritativeMovementSnapshot(1, 1, serverX, 0));
    var state = result.State;

    return result.Mode == PredictionCorrectionMode.Smooth
        && result.ErrorDistance > 0
        && Near(state.PredictedX, serverX)
        && state.RenderX < MovementTuning.MaxDistancePerMoveIntent
        && state.RenderX > serverX;
}

static bool LargeErrorSnaps()
{
    var predictor = CreatePredictor();
    for (var sequence = 1UL; sequence <= 10UL; sequence++)
    {
        predictor.ApplyLocalInput(PredictedMoveIntent.Direction(sequence, sequence, 1, 0));
    }

    var result = predictor.ApplySnapshot(new AuthoritativeMovementSnapshot(1, 10, 0, 0));

    return result.Mode == PredictionCorrectionMode.Snap
        && result.State.PendingInputCount == 0
        && Near(result.State.RenderX, 0)
        && Near(result.State.PredictedX, 0);
}

static bool CorrectionWinsLocalState()
{
    var predictor = CreatePredictor();
    predictor.ApplyLocalInput(PredictedMoveIntent.Direction(1, 1, 1, 0));
    predictor.ApplyLocalInput(PredictedMoveIntent.Direction(2, 2, 1, 0));

    var result = predictor.ApplyCorrection(2, 0, 0);

    return result.State.LastAckSequence == 2
        && result.State.PendingInputCount == 0
        && result.Mode == PredictionCorrectionMode.Smooth
        && result.State.PredictedX == 0
        && result.State.RenderX > 0;
}

static bool DiagonalTiePreservesFacing()
{
    var predictor = CreatePredictor(CardinalDirection.South);
    var diagonal = predictor.ApplyLocalInput(PredictedMoveIntent.Direction(1, 1, 1, 1));
    var north = predictor.ApplyLocalInput(PredictedMoveIntent.Direction(2, 2, 0, 1));

    return diagonal.State.Facing == CardinalDirection.South
        && north.State.Facing == CardinalDirection.North;
}

static bool DelayedSnapshotDoesNotBreakInput()
{
    var predictor = CreatePredictor();
    predictor.ApplyLocalInput(PredictedMoveIntent.Direction(1, 1, 1, 0));
    var accepted = predictor.ApplySnapshot(new AuthoritativeMovementSnapshot(1, 1, MovementTuning.MaxDistancePerMoveIntent, 0));
    predictor.ApplyLocalInput(PredictedMoveIntent.Direction(2, 2, 1, 0));
    var before = predictor.State;
    var delayed = predictor.ApplySnapshot(new AuthoritativeMovementSnapshot(1, 1, 0, 0));
    predictor.ApplyLocalInput(PredictedMoveIntent.Direction(3, 3, 1, 0));
    var after = predictor.State;

    return accepted.Mode == PredictionCorrectionMode.None
        && delayed.Ignored
        && before.LastSnapshotId == after.LastSnapshotId
        && after.LastAppliedSequence == 3
        && after.RenderX > before.RenderX;
}

static bool VisualSmokeKeepsBudget()
{
    var predictor = CreatePredictor();
    var previousRenderX = predictor.State.RenderX;
    var stopwatch = Stopwatch.StartNew();

    for (var frame = 1UL; frame <= 60UL; frame++)
    {
        predictor.ApplyLocalInput(PredictedMoveIntent.Direction(frame, frame, 1, 0));
        if (predictor.State.RenderX <= previousRenderX)
        {
            return false;
        }

        previousRenderX = predictor.State.RenderX;
    }

    stopwatch.Stop();
    var averageFrameMs = stopwatch.Elapsed.TotalMilliseconds / 60d;

    return averageFrameMs < ClientPredictionSettings.Default.FrameBudgetMs
        && Near(predictor.State.RenderY, 0)
        && predictor.State.PendingInputCount == 60;
}

static ClientMovementPredictor CreatePredictor(CardinalDirection facing = CardinalDirection.South)
{
    var predictor = new ClientMovementPredictor(ClientPredictionSettings.Default);
    predictor.Reset(0, 0, facing);
    return predictor;
}

static ServerEnvelope CreateSnapshotEnvelope(ulong snapshotId, ulong ackSequence, double x, double y) =>
    new()
    {
        ProtocolVersion = 1,
        AckSequence = ackSequence,
        WorldSnapshot = new WorldSnapshot
        {
            SnapshotId = snapshotId,
            MapId = "training-field-01",
            Entities =
            {
                new EntityState
                {
                    EntityId = "player",
                    Kind = EntityKind.Player,
                    Position = new Vector2 { X = (float)x, Y = (float)y }
                }
            }
        }
    };

static AuthoritativeMovementSnapshot FromEnvelope(ServerEnvelope envelope)
{
    var player = envelope.WorldSnapshot.Entities.Single(entity => entity.Kind == EntityKind.Player);
    return new AuthoritativeMovementSnapshot(
        envelope.WorldSnapshot.SnapshotId,
        envelope.AckSequence,
        player.Position.X,
        player.Position.Y);
}

static bool Near(double actual, double expected) => Math.Abs(actual - expected) < 0.000001d;

static PredictionCheck Check(string name, bool passed) => new(name, passed);

internal readonly record struct PredictionCheck(string Name, bool Passed);
