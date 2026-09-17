using System;
using System.Collections.Generic;

namespace Divinity.GameRules.Movement
{
    public sealed class ClientMovementPredictor
    {
        private readonly ClientPredictionSettings _settings;
        private readonly List<PredictedMoveIntent> _pendingInputs = new List<PredictedMoveIntent>();
        private MovementPosition _predictedPosition;
        private MovementPosition _renderPosition;
        private CardinalDirection _facing;
        private ulong _lastAckSequence;
        private ulong _lastAppliedSequence;
        private ulong _lastSnapshotId;

        public ClientMovementPredictor(ClientPredictionSettings settings)
        {
            _settings = settings ?? ClientPredictionSettings.Default;
            _facing = CardinalDirection.South;
        }

        public PredictedMovementState State
        {
            get
            {
                return new PredictedMovementState(
                    _predictedPosition.X,
                    _predictedPosition.Y,
                    _renderPosition.X,
                    _renderPosition.Y,
                    _facing,
                    _lastAckSequence,
                    _lastSnapshotId,
                    _lastAppliedSequence,
                    _pendingInputs.Count);
            }
        }

        public void Reset(double x, double y, CardinalDirection facing)
        {
            _pendingInputs.Clear();
            _predictedPosition = new MovementPosition(x, y);
            _renderPosition = _predictedPosition;
            _facing = facing;
            _lastAckSequence = 0;
            _lastAppliedSequence = 0;
            _lastSnapshotId = 0;
        }

        public PredictionStepResult ApplyLocalInput(PredictedMoveIntent intent)
        {
            if (intent.Sequence <= _lastAckSequence || intent.Sequence <= _lastAppliedSequence)
            {
                return new PredictionStepResult(State, false, "Input sequence is already acknowledged or applied.");
            }

            var previousPredicted = _predictedPosition;
            var step = SimulateIntent(previousPredicted, _facing, intent);
            _predictedPosition = step.Position;
            _facing = step.Facing;
            _renderPosition = new MovementPosition(
                _renderPosition.X + (_predictedPosition.X - previousPredicted.X),
                _renderPosition.Y + (_predictedPosition.Y - previousPredicted.Y));
            _lastAppliedSequence = intent.Sequence;
            _pendingInputs.Add(intent);

            return new PredictionStepResult(State, true, "Input predicted locally.");
        }

        public ReconciliationResult ApplySnapshot(AuthoritativeMovementSnapshot snapshot)
        {
            if (snapshot.SnapshotId <= _lastSnapshotId || snapshot.AckSequence < _lastAckSequence)
            {
                return new ReconciliationResult(State, PredictionCorrectionMode.IgnoredOldSnapshot, 0, 0, true);
            }

            _lastSnapshotId = snapshot.SnapshotId;
            return Reconcile(snapshot.AckSequence, new MovementPosition(snapshot.X, snapshot.Y));
        }

        public ReconciliationResult ApplyCorrection(ulong ackSequence, double authoritativeX, double authoritativeY)
        {
            if (ackSequence < _lastAckSequence)
            {
                return new ReconciliationResult(State, PredictionCorrectionMode.IgnoredOldSnapshot, 0, 0, true);
            }

            return Reconcile(ackSequence, new MovementPosition(authoritativeX, authoritativeY));
        }

        public bool HasPendingSequence(ulong sequence)
        {
            for (var index = 0; index < _pendingInputs.Count; index++)
            {
                if (_pendingInputs[index].Sequence == sequence)
                {
                    return true;
                }
            }

            return false;
        }

        private ReconciliationResult Reconcile(ulong ackSequence, MovementPosition authoritativePosition)
        {
            _lastAckSequence = Math.Max(_lastAckSequence, ackSequence);
            var dropped = DropAcknowledgedInputs(_lastAckSequence);

            _predictedPosition = authoritativePosition;
            for (var index = 0; index < _pendingInputs.Count; index++)
            {
                var step = SimulateIntent(_predictedPosition, _facing, _pendingInputs[index]);
                _predictedPosition = step.Position;
                _facing = step.Facing;
            }

            var error = _renderPosition.DistanceTo(_predictedPosition);
            var mode = PredictionCorrectionMode.None;

            if (error > MovementMath.Epsilon)
            {
                if (error > _settings.SnapDistanceThreshold)
                {
                    _renderPosition = _predictedPosition;
                    mode = PredictionCorrectionMode.Snap;
                }
                else
                {
                    _renderPosition = MovementPosition.Lerp(_renderPosition, _predictedPosition, _settings.SmoothCorrectionFactor);
                    mode = PredictionCorrectionMode.Smooth;
                }
            }

            return new ReconciliationResult(State, mode, error, dropped, false);
        }

        private int DropAcknowledgedInputs(ulong ackSequence)
        {
            var dropped = 0;
            for (var index = _pendingInputs.Count - 1; index >= 0; index--)
            {
                if (_pendingInputs[index].Sequence <= ackSequence)
                {
                    _pendingInputs.RemoveAt(index);
                    dropped++;
                }
            }

            return dropped;
        }

        private static SimulatedPredictionStep SimulateIntent(MovementPosition position, CardinalDirection facing, PredictedMoveIntent intent)
        {
            switch (intent.Mode)
            {
                case PredictedMovementMode.Direction:
                    return SimulateDirection(position, facing, intent.DirectionX, intent.DirectionY);
                case PredictedMovementMode.ClickTarget:
                    return SimulateClickTarget(position, facing, intent.TargetX, intent.TargetY);
                default:
                    return new SimulatedPredictionStep(position, facing);
            }
        }

        private static SimulatedPredictionStep SimulateDirection(MovementPosition position, CardinalDirection facing, double directionX, double directionY)
        {
            var normalized = MovementMath.NormalizeDirection(directionX, directionY);
            if (!normalized.HasMagnitude)
            {
                return new SimulatedPredictionStep(position, facing);
            }

            var nextFacing = MovementMath.ResolveFacing(normalized.X, normalized.Y, facing);
            return new SimulatedPredictionStep(
                new MovementPosition(
                    position.X + normalized.X * MovementTuning.MaxDistancePerMoveIntent,
                    position.Y + normalized.Y * MovementTuning.MaxDistancePerMoveIntent),
                nextFacing);
        }

        private static SimulatedPredictionStep SimulateClickTarget(MovementPosition position, CardinalDirection facing, double targetX, double targetY)
        {
            var deltaX = targetX - position.X;
            var deltaY = targetY - position.Y;
            var normalized = MovementMath.NormalizeDirection(deltaX, deltaY);
            if (!normalized.HasMagnitude)
            {
                return new SimulatedPredictionStep(position, facing);
            }

            var distance = Math.Min(MovementTuning.MaxDistancePerMoveIntent, normalized.OriginalLength);
            var nextFacing = MovementMath.ResolveFacing(normalized.X, normalized.Y, facing);
            return new SimulatedPredictionStep(
                new MovementPosition(position.X + normalized.X * distance, position.Y + normalized.Y * distance),
                nextFacing);
        }

        private readonly struct SimulatedPredictionStep
        {
            public SimulatedPredictionStep(MovementPosition position, CardinalDirection facing)
            {
                Position = position;
                Facing = facing;
            }

            public MovementPosition Position { get; }
            public CardinalDirection Facing { get; }
        }
    }

    public sealed class ClientPredictionSettings
    {
        public static readonly ClientPredictionSettings Default = new ClientPredictionSettings(
            snapDistanceThreshold: 0.75d,
            smoothCorrectionFactor: 0.35d);

        public ClientPredictionSettings(double snapDistanceThreshold, double smoothCorrectionFactor)
        {
            if (snapDistanceThreshold <= 0)
            {
                throw new ArgumentOutOfRangeException("snapDistanceThreshold", "Snap threshold must be positive.");
            }

            if (smoothCorrectionFactor <= 0 || smoothCorrectionFactor >= 1)
            {
                throw new ArgumentOutOfRangeException("smoothCorrectionFactor", "Smooth correction factor must be between 0 and 1.");
            }

            SnapDistanceThreshold = snapDistanceThreshold;
            SmoothCorrectionFactor = smoothCorrectionFactor;
        }

        public double SnapDistanceThreshold { get; }
        public double SmoothCorrectionFactor { get; }
        public double FrameBudgetMs
        {
            get { return 1000d / 60d; }
        }
    }

    public readonly struct PredictedMoveIntent
    {
        private PredictedMoveIntent(
            ulong sequence,
            ulong clientTick,
            PredictedMovementMode mode,
            double directionX,
            double directionY,
            double targetX,
            double targetY)
        {
            Sequence = sequence;
            ClientTick = clientTick;
            Mode = mode;
            DirectionX = directionX;
            DirectionY = directionY;
            TargetX = targetX;
            TargetY = targetY;
        }

        public ulong Sequence { get; }
        public ulong ClientTick { get; }
        public PredictedMovementMode Mode { get; }
        public double DirectionX { get; }
        public double DirectionY { get; }
        public double TargetX { get; }
        public double TargetY { get; }

        public static PredictedMoveIntent Direction(ulong sequence, ulong clientTick, double x, double y)
        {
            return new PredictedMoveIntent(sequence, clientTick, PredictedMovementMode.Direction, x, y, 0, 0);
        }

        public static PredictedMoveIntent ClickTarget(ulong sequence, ulong clientTick, double x, double y)
        {
            return new PredictedMoveIntent(sequence, clientTick, PredictedMovementMode.ClickTarget, 0, 0, x, y);
        }
    }

    public enum PredictedMovementMode
    {
        Direction,
        ClickTarget
    }

    public readonly struct MovementPosition
    {
        public MovementPosition(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }
        public double Y { get; }

        public double DistanceTo(MovementPosition other)
        {
            var deltaX = X - other.X;
            var deltaY = Y - other.Y;
            return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        }

        public static MovementPosition Lerp(MovementPosition from, MovementPosition to, double factor)
        {
            return new MovementPosition(
                from.X + (to.X - from.X) * factor,
                from.Y + (to.Y - from.Y) * factor);
        }
    }

    public readonly struct AuthoritativeMovementSnapshot
    {
        public AuthoritativeMovementSnapshot(ulong snapshotId, ulong ackSequence, double x, double y)
        {
            SnapshotId = snapshotId;
            AckSequence = ackSequence;
            X = x;
            Y = y;
        }

        public ulong SnapshotId { get; }
        public ulong AckSequence { get; }
        public double X { get; }
        public double Y { get; }
    }

    public readonly struct PredictedMovementState
    {
        public PredictedMovementState(
            double predictedX,
            double predictedY,
            double renderX,
            double renderY,
            CardinalDirection facing,
            ulong lastAckSequence,
            ulong lastSnapshotId,
            ulong lastAppliedSequence,
            int pendingInputCount)
        {
            PredictedX = predictedX;
            PredictedY = predictedY;
            RenderX = renderX;
            RenderY = renderY;
            Facing = facing;
            LastAckSequence = lastAckSequence;
            LastSnapshotId = lastSnapshotId;
            LastAppliedSequence = lastAppliedSequence;
            PendingInputCount = pendingInputCount;
        }

        public double PredictedX { get; }
        public double PredictedY { get; }
        public double RenderX { get; }
        public double RenderY { get; }
        public CardinalDirection Facing { get; }
        public ulong LastAckSequence { get; }
        public ulong LastSnapshotId { get; }
        public ulong LastAppliedSequence { get; }
        public int PendingInputCount { get; }
    }

    public readonly struct PredictionStepResult
    {
        public PredictionStepResult(PredictedMovementState state, bool accepted, string message)
        {
            State = state;
            Accepted = accepted;
            Message = message;
        }

        public PredictedMovementState State { get; }
        public bool Accepted { get; }
        public string Message { get; }
    }

    public readonly struct ReconciliationResult
    {
        public ReconciliationResult(
            PredictedMovementState state,
            PredictionCorrectionMode mode,
            double errorDistance,
            int droppedInputCount,
            bool ignored)
        {
            State = state;
            Mode = mode;
            ErrorDistance = errorDistance;
            DroppedInputCount = droppedInputCount;
            Ignored = ignored;
        }

        public PredictedMovementState State { get; }
        public PredictionCorrectionMode Mode { get; }
        public double ErrorDistance { get; }
        public int DroppedInputCount { get; }
        public bool Ignored { get; }
    }

    public enum PredictionCorrectionMode
    {
        None,
        Smooth,
        Snap,
        IgnoredOldSnapshot
    }
}
