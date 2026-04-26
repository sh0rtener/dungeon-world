using System.Runtime.CompilerServices;

namespace MicroEcs;

/// <summary>
/// A built query bound to a <see cref="World"/> and a <see cref="QueryDescription"/>.
/// Provides the high-throughput iteration entry points that fan out over matching archetypes
/// and chunks, calling user delegates with <c>ref</c> components.
/// </summary>
public readonly struct Query
{
    private readonly World _world;
    private readonly QueryDescription _description;

    internal Query(World world, QueryDescription description)
    {
        _world = world;
        _description = description;
    }

    // ------------------------------------------------------------------
    // ForEach with refs to components
    // ------------------------------------------------------------------

    /// <summary>Run <paramref name="action"/> on every matching entity, passing a <c>ref</c> to its <typeparamref name="T1"/>.</summary>
    public void ForEach<T1>(QueryAction<T1> action) where T1 : struct
    {
        foreach (var arch in _world.MatchingArchetypes(_description))
        {
            foreach (var chunk in arch.Chunks)
            {
                if (chunk.Count == 0) continue;
                var s1 = chunk.GetSpan<T1>();
                for (int i = 0; i < s1.Length; i++)
                    action(ref s1[i]);
            }
        }
    }

    /// <summary>ForEach over two ref components.</summary>
    public void ForEach<T1, T2>(QueryAction<T1, T2> action)
        where T1 : struct where T2 : struct
    {
        foreach (var arch in _world.MatchingArchetypes(_description))
        {
            foreach (var chunk in arch.Chunks)
            {
                if (chunk.Count == 0) continue;
                var s1 = chunk.GetSpan<T1>();
                var s2 = chunk.GetSpan<T2>();
                int n = s1.Length;
                for (int i = 0; i < n; i++)
                    action(ref s1[i], ref s2[i]);
            }
        }
    }

    /// <summary>ForEach over three ref components.</summary>
    public void ForEach<T1, T2, T3>(QueryAction<T1, T2, T3> action)
        where T1 : struct where T2 : struct where T3 : struct
    {
        foreach (var arch in _world.MatchingArchetypes(_description))
        {
            foreach (var chunk in arch.Chunks)
            {
                if (chunk.Count == 0) continue;
                var s1 = chunk.GetSpan<T1>();
                var s2 = chunk.GetSpan<T2>();
                var s3 = chunk.GetSpan<T3>();
                int n = s1.Length;
                for (int i = 0; i < n; i++)
                    action(ref s1[i], ref s2[i], ref s3[i]);
            }
        }
    }

    /// <summary>ForEach over four ref components.</summary>
    public void ForEach<T1, T2, T3, T4>(QueryAction<T1, T2, T3, T4> action)
        where T1 : struct where T2 : struct where T3 : struct where T4 : struct
    {
        foreach (var arch in _world.MatchingArchetypes(_description))
        {
            foreach (var chunk in arch.Chunks)
            {
                if (chunk.Count == 0) continue;
                var s1 = chunk.GetSpan<T1>();
                var s2 = chunk.GetSpan<T2>();
                var s3 = chunk.GetSpan<T3>();
                var s4 = chunk.GetSpan<T4>();
                int n = s1.Length;
                for (int i = 0; i < n; i++)
                    action(ref s1[i], ref s2[i], ref s3[i], ref s4[i]);
            }
        }
    }

    /// <summary>ForEach with the <see cref="Entity"/> handle alongside one component ref.</summary>
    public void ForEachWithEntity<T1>(QueryActionWithEntity<T1> action) where T1 : struct
    {
        foreach (var arch in _world.MatchingArchetypes(_description))
        {
            foreach (var chunk in arch.Chunks)
            {
                if (chunk.Count == 0) continue;
                var entities = chunk.Entities;
                var s1 = chunk.GetSpan<T1>();
                for (int i = 0; i < s1.Length; i++)
                    action(entities[i], ref s1[i]);
            }
        }
    }

    /// <summary>ForEach with the entity and two component refs.</summary>
    public void ForEachWithEntity<T1, T2>(QueryActionWithEntity<T1, T2> action)
        where T1 : struct where T2 : struct
    {
        foreach (var arch in _world.MatchingArchetypes(_description))
        {
            foreach (var chunk in arch.Chunks)
            {
                if (chunk.Count == 0) continue;
                var entities = chunk.Entities;
                var s1 = chunk.GetSpan<T1>();
                var s2 = chunk.GetSpan<T2>();
                for (int i = 0; i < s1.Length; i++)
                    action(entities[i], ref s1[i], ref s2[i]);
            }
        }
    }

    // ------------------------------------------------------------------
    // Chunk-level iteration (lowest overhead, lets users write tight loops manually)
    // ------------------------------------------------------------------

    /// <summary>
    /// Iterate every matching chunk. Hand the user a chunk reference so they can grab
    /// whatever spans they need and write the inner loop themselves — useful for SIMD/vectorized code.
    /// </summary>
    public void ForEachChunk(Action<Chunk> action)
    {
        foreach (var arch in _world.MatchingArchetypes(_description))
            foreach (var chunk in arch.Chunks)
                if (chunk.Count > 0) action(chunk);
    }

    /// <summary>Total number of matching entities (sums over every matching archetype).</summary>
    public int Count()
    {
        int total = 0;
        foreach (var arch in _world.MatchingArchetypes(_description))
            total += arch.EntityCount;
        return total;
    }
}

// ---------- ref-friendly delegate definitions ----------
// These mirror Action<T...> but pass each component by ref so user code can mutate in place.

public delegate void QueryAction<T1>(ref T1 c1) where T1 : struct;
public delegate void QueryAction<T1, T2>(ref T1 c1, ref T2 c2) where T1 : struct where T2 : struct;
public delegate void QueryAction<T1, T2, T3>(ref T1 c1, ref T2 c2, ref T3 c3)
    where T1 : struct where T2 : struct where T3 : struct;
public delegate void QueryAction<T1, T2, T3, T4>(ref T1 c1, ref T2 c2, ref T3 c3, ref T4 c4)
    where T1 : struct where T2 : struct where T3 : struct where T4 : struct;

public delegate void QueryActionWithEntity<T1>(Entity e, ref T1 c1) where T1 : struct;
public delegate void QueryActionWithEntity<T1, T2>(Entity e, ref T1 c1, ref T2 c2)
    where T1 : struct where T2 : struct;
