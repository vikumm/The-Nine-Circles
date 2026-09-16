namespace Divinity.GameRules.Movement;

public static class MovementMath
{
    private const double Epsilon = 0.000001d;

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

public readonly record struct MovementVector(double X, double Y, double OriginalLength)
{
    public bool HasMagnitude => OriginalLength > 0;
}

public enum CardinalDirection
{
    North,
    South,
    East,
    West
}
