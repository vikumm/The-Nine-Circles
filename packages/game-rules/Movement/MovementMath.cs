using System;

namespace Divinity.GameRules.Movement
{
    public static class MovementTuning
    {
        public const double MaxSpeedUnitsPerSecond = 4.5d;
        public const double MoveIntentDeltaSeconds = 0.05d;
        public const double MaxDistancePerMoveIntent = MaxSpeedUnitsPerSecond * MoveIntentDeltaSeconds;
    }

    public static class MovementMath
    {
        public const double Epsilon = 0.000001d;

        public static MovementVector NormalizeDirection(double x, double y)
        {
            var length = Math.Sqrt(x * x + y * y);
            return length <= Epsilon
                ? new MovementVector(0, 0, 0)
                : new MovementVector(x / length, y / length, length);
        }

        public static CardinalDirection ResolveFacing(
            double normalizedX,
            double normalizedY,
            CardinalDirection lastFacing)
        {
            var absX = Math.Abs(normalizedX);
            var absY = Math.Abs(normalizedY);

            if (absX <= Epsilon && absY <= Epsilon)
            {
                return lastFacing;
            }

            if (Math.Abs(absX - absY) <= Epsilon)
            {
                return lastFacing;
            }

            if (absX > absY)
            {
                return normalizedX >= 0 ? CardinalDirection.East : CardinalDirection.West;
            }

            return normalizedY >= 0 ? CardinalDirection.North : CardinalDirection.South;
        }
    }

    public readonly struct MovementVector
    {
        public MovementVector(double x, double y, double originalLength)
        {
            X = x;
            Y = y;
            OriginalLength = originalLength;
        }

        public double X { get; }
        public double Y { get; }
        public double OriginalLength { get; }
        public bool HasMagnitude
        {
            get { return OriginalLength > 0; }
        }
    }

    public enum CardinalDirection
    {
        North,
        South,
        East,
        West
    }
}
