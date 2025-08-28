namespace DungeonWorld.Game.Shared;

public readonly struct Point : IEquatable<Point>
{
    public int X { get; }
    public int Y { get;}

    public Point(int x, int y)
    {
        X = x;
        Y = y;
    }

    public static Point operator -(Point a, Point b)
    {
        return new(a.X - b.X, a.Y - b.Y);
    }

    public static Point operator +(Point a, Point b)
    {
        return new(a.X + b.X, a.Y + b.Y);
    }

    public bool Equals(Point other) => other.X.Equals(X) && other.Y.Equals(Y);

    public static bool operator ==(Point a, Point b) => a.Equals(b);
    public static bool operator !=(Point a, Point b) => !a.Equals(b);

}