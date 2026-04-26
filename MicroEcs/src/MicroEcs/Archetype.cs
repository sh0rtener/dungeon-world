using System.Runtime.CompilerServices;

namespace MicroEcs;

/// <summary>
/// One archetype = one unique combination of component types. Stores entities in a list of
/// fixed-capacity <see cref="Chunk"/>s. All entities in an archetype share the exact same shape,
/// which is what lets queries iterate component data linearly with no per-entity branching.
/// </summary>
public sealed class Archetype
{
    /// <summary>The component types in this archetype, sorted by <see cref="ComponentType.Id"/>.</summary>
    public ComponentType[] ComponentTypes { get; }

    /// <summary>Bit set of component ids — used for fast query matching.</summary>
    public BitSet Signature { get; }

    /// <summary>Per-component-type lookup: column index inside a chunk, indexed by component id (-1 if absent).</summary>
    private readonly int[] _idToColumn;

    private readonly List<Chunk> _chunks = new();
    private readonly int _chunkCapacity;

    /// <summary>Total number of entities across all chunks of this archetype.</summary>
    public int EntityCount
    {
        get
        {
            int sum = 0;
            foreach (var c in _chunks) sum += c.Count;
            return sum;
        }
    }

    /// <summary>Read-only view of this archetype's chunks.</summary>
    public IReadOnlyList<Chunk> Chunks => _chunks;

    // Cache of "if I add component X to an entity in this archetype, where does it go?"
    // Keyed by ComponentType.Id; value is the destination archetype.
    internal readonly Dictionary<int, Archetype> AddEdges = new();
    internal readonly Dictionary<int, Archetype> RemoveEdges = new();

    internal Archetype(ComponentType[] sortedTypes, int chunkCapacity = Chunk.DefaultCapacity)
    {
        ComponentTypes = sortedTypes;
        _chunkCapacity = chunkCapacity;
        Signature = new BitSet();
        foreach (var t in sortedTypes) Signature.Set(t.Id);

        // Build an id->column index for O(1) lookup by component id.
        int maxId = 0;
        foreach (var t in sortedTypes) if (t.Id > maxId) maxId = t.Id;
        _idToColumn = new int[maxId + 1];
        Array.Fill(_idToColumn, -1);
        for (int i = 0; i < sortedTypes.Length; i++)
            _idToColumn[sortedTypes[i].Id] = i;
    }

    /// <summary>True when this archetype has a column for component id <paramref name="componentId"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Has(int componentId)
        => componentId < _idToColumn.Length && _idToColumn[componentId] >= 0;

    /// <summary>
    /// Returns the column index for component id <paramref name="componentId"/>, or -1 if this
    /// archetype does not contain that component.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int IndexOf(int componentId)
        => componentId < _idToColumn.Length ? _idToColumn[componentId] : -1;

    /// <summary>Allocate a slot for <paramref name="entity"/>, creating a new chunk if necessary.</summary>
    internal (Chunk chunk, int slot) Allocate(Entity entity)
    {
        // Search from the back — newer chunks are more likely to have free slots.
        for (int i = _chunks.Count - 1; i >= 0; i--)
        {
            var c = _chunks[i];
            if (!c.IsFull) return (c, c.Allocate(entity));
        }
        var fresh = new Chunk(this, _chunkCapacity);
        _chunks.Add(fresh);
        return (fresh, fresh.Allocate(entity));
    }

    /// <summary>Drop empty trailing chunks. Optional cleanup; safe to skip.</summary>
    public void TrimEmptyChunks()
    {
        for (int i = _chunks.Count - 1; i >= 0; i--)
        {
            if (_chunks[i].Count == 0) _chunks.RemoveAt(i);
            else break;
        }
    }
}
