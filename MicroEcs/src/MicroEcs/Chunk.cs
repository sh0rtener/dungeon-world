using System.Runtime.CompilerServices;

namespace MicroEcs;

/// <summary>
/// A fixed-capacity slab of memory that stores a contiguous block of entities for a single archetype.
/// Components of each type live in their own column array (Structure-of-Arrays layout), so iterating
/// one component type touches only the cache lines for that data — the central cache-friendliness
/// win of an archetype ECS.
/// </summary>
public sealed class Chunk
{
    /// <summary>Default chunk capacity (entities per chunk). Power of two for cheap modulo.</summary>
    public const int DefaultCapacity = 256;

    /// <summary>Per-component-type column arrays. Index matches <see cref="Archetype.ComponentTypes"/>.</summary>
    private readonly Array[] _columns;

    /// <summary>Entities stored in this chunk, in slot order.</summary>
    private readonly Entity[] _entities;

    /// <summary>Capacity (max entities this chunk can hold).</summary>
    public int Capacity { get; }

    /// <summary>Number of entities currently stored.</summary>
    public int Count { get; private set; }

    /// <summary>True when <see cref="Count"/> equals <see cref="Capacity"/>.</summary>
    public bool IsFull => Count == Capacity;

    /// <summary>The archetype this chunk belongs to.</summary>
    public Archetype Archetype { get; }

    internal Chunk(Archetype archetype, int capacity)
    {
        Archetype = archetype;
        Capacity = capacity;
        _entities = new Entity[capacity];

        var types = archetype.ComponentTypes;
        _columns = new Array[types.Length];
        for (int i = 0; i < types.Length; i++)
        {
            // Tags need no storage but we still allocate a 1-element placeholder so column index math
            // stays uniform. Real column lookups go through GetColumn<T> which never indexes a tag.
            _columns[i] = types[i].IsTag
                ? Array.CreateInstance(types[i].Type, 0)
                : Array.CreateInstance(types[i].Type, capacity);
        }
    }

    /// <summary>Get the entity stored at the given slot index.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Entity GetEntity(int slot) => _entities[slot];

    /// <summary>Read-only span over all entities currently in this chunk.</summary>
    public ReadOnlySpan<Entity> Entities => _entities.AsSpan(0, Count);

    /// <summary>Get a writable span over the column for component <typeparamref name="T"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> GetSpan<T>() where T : struct
    {
        var ct = ComponentRegistry.Of<T>();
        int columnIndex = Archetype.IndexOf(ct.Id);
        if (columnIndex < 0)
            throw new InvalidOperationException(
                $"Archetype does not contain component {typeof(T).Name}.");
        if (ct.IsTag)
            throw new InvalidOperationException(
                $"Component {typeof(T).Name} is a zero-sized tag; tags have no column data. " +
                $"Use the tag in QueryDescription filters but don't request its span.");
        return ((T[])_columns[columnIndex]).AsSpan(0, Count);
    }

    /// <summary>Get a ref to the component <typeparamref name="T"/> at the given slot.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetRef<T>(int slot) where T : struct
    {
        var ct = ComponentRegistry.Of<T>();
        int columnIndex = Archetype.IndexOf(ct.Id);
        if (columnIndex < 0)
            throw new InvalidOperationException(
                $"Archetype does not contain component {typeof(T).Name}.");
        if (ct.IsTag)
            throw new InvalidOperationException(
                $"Component {typeof(T).Name} is a zero-sized tag; tags have no per-entity storage.");
        return ref ((T[])_columns[columnIndex])[slot];
    }

    /// <summary>Untyped column accessor — needed for moves between archetypes.</summary>
    internal Array GetColumnUntyped(int columnIndex) => _columns[columnIndex];

    /// <summary>
    /// Reserve a new slot at the end and return its index. Caller is responsible for filling
    /// component data and recording the entity.
    /// </summary>
    internal int Allocate(Entity entity)
    {
        if (Count == Capacity)
            throw new InvalidOperationException("Chunk is full.");
        int slot = Count++;
        _entities[slot] = entity;
        return slot;
    }

    /// <summary>
    /// Remove the entity at <paramref name="slot"/> by swap-and-pop with the last slot.
    /// Returns the entity that was moved into <paramref name="slot"/> (so the world can update its
    /// slot-pointer), or <see cref="Entity.Null"/> if the removed slot was the last one.
    /// </summary>
    internal Entity RemoveSwapBack(int slot)
    {
        int last = Count - 1;
        Entity moved = Entity.Null;

        if (slot != last)
        {
            // Move last slot's data into the removed slot for every column.
            for (int i = 0; i < _columns.Length; i++)
            {
                if (Archetype.ComponentTypes[i].IsTag) continue;
                Array.Copy(_columns[i], last, _columns[i], slot, 1);
            }
            _entities[slot] = _entities[last];
            moved = _entities[slot];
        }

        // Clear the now-unused last slot to drop any references the GC should reclaim.
        for (int i = 0; i < _columns.Length; i++)
        {
            if (Archetype.ComponentTypes[i].IsTag) continue;
            Array.Clear(_columns[i], last, 1);
        }
        _entities[last] = Entity.Null;
        Count--;
        return moved;
    }

    /// <summary>
    /// Copy all components shared with <paramref name="dstArchetype"/> from this chunk's slot
    /// <paramref name="srcSlot"/> to <paramref name="dst"/>'s slot <paramref name="dstSlot"/>.
    /// Used when an entity changes archetype (component added or removed).
    /// </summary>
    internal void CopySharedComponentsTo(int srcSlot, Chunk dst, int dstSlot)
    {
        var srcTypes = Archetype.ComponentTypes;
        var dstTypes = dst.Archetype.ComponentTypes;

        // Both arrays are sorted by ComponentType.Id, so we can merge-walk them in O(n + m).
        int i = 0, j = 0;
        while (i < srcTypes.Length && j < dstTypes.Length)
        {
            int srcId = srcTypes[i].Id;
            int dstId = dstTypes[j].Id;
            if (srcId == dstId)
            {
                if (!srcTypes[i].IsTag)
                    Array.Copy(_columns[i], srcSlot, dst._columns[j], dstSlot, 1);
                i++; j++;
            }
            else if (srcId < dstId) i++;
            else j++;
        }
    }
}
