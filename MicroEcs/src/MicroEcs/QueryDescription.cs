namespace MicroEcs;

/// <summary>
/// A declarative description of which archetypes a query should match.
/// Three filters: <see cref="All"/> (all of), <see cref="Any"/> (at least one of),
/// and <see cref="None"/> (none of).
/// </summary>
public sealed class QueryDescription
{
    internal readonly BitSet All = new();
    internal readonly BitSet Any = new();
    internal readonly BitSet None = new();
    internal bool HasAny;

    /// <summary>Match only archetypes that contain every listed component.</summary>
    public QueryDescription WithAll<T>() where T : struct
    {
        All.Set(ComponentRegistry.Of<T>().Id);
        return this;
    }

    public QueryDescription WithAll<T1, T2>() where T1 : struct where T2 : struct
        => WithAll<T1>().WithAll<T2>();

    public QueryDescription WithAll<T1, T2, T3>() where T1 : struct where T2 : struct where T3 : struct
        => WithAll<T1>().WithAll<T2>().WithAll<T3>();

    public QueryDescription WithAll<T1, T2, T3, T4>() where T1 : struct where T2 : struct where T3 : struct where T4 : struct
        => WithAll<T1>().WithAll<T2>().WithAll<T3>().WithAll<T4>();

    /// <summary>Match archetypes that contain at least one of the listed components.</summary>
    public QueryDescription WithAny<T>() where T : struct
    {
        Any.Set(ComponentRegistry.Of<T>().Id);
        HasAny = true;
        return this;
    }

    public QueryDescription WithAny<T1, T2>() where T1 : struct where T2 : struct
        => WithAny<T1>().WithAny<T2>();

    /// <summary>Exclude archetypes that contain any of the listed components.</summary>
    public QueryDescription WithNone<T>() where T : struct
    {
        None.Set(ComponentRegistry.Of<T>().Id);
        return this;
    }

    public QueryDescription WithNone<T1, T2>() where T1 : struct where T2 : struct
        => WithNone<T1>().WithNone<T2>();

    /// <summary>True if <paramref name="archetype"/> satisfies all three filters.</summary>
    internal bool Matches(Archetype archetype)
    {
        var sig = archetype.Signature;
        if (!sig.ContainsAll(All)) return false;
        if (sig.OverlapsAny(None)) return false;
        if (HasAny && !sig.OverlapsAny(Any)) return false;
        return true;
    }
}
