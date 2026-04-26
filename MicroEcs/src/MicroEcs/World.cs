using System.Runtime.CompilerServices;

namespace MicroEcs;

/// <summary>
/// The ECS database. Owns all entities, archetypes, and chunks; exposes the public API for
/// creating/destroying entities, mutating components, and running queries.
/// </summary>
public sealed class World : IDisposable
{
    /// <summary>
    /// Per-entity record kept in a parallel array indexed by entity id.
    /// Stores the entity's current archetype, chunk, and slot inside the chunk.
    /// </summary>
    internal struct EntityRecord
    {
        public Archetype? Archetype;
        public Chunk? Chunk;
        public int Slot;
        public int Version;
        public bool IsAlive;
    }

    private EntityRecord[] _records = new EntityRecord[1024];
    private readonly Stack<int> _freeIds = new();
    private int _nextId;
    private int _entityCount;

    // Archetypes keyed by their signature.
    private readonly Dictionary<BitSet, Archetype> _archetypes = new();

    // The "empty" archetype every entity starts in. Created lazily.
    private readonly Archetype _emptyArchetype;

    /// <summary>Default chunk capacity for new archetypes.</summary>
    public int DefaultChunkCapacity { get; }

    /// <summary>Total number of live entities.</summary>
    public int EntityCount => _entityCount;

    /// <summary>All archetypes currently known to this world.</summary>
    public IReadOnlyCollection<Archetype> Archetypes => _archetypes.Values;

    public World(int defaultChunkCapacity = Chunk.DefaultCapacity)
    {
        DefaultChunkCapacity = defaultChunkCapacity;
        _emptyArchetype = new Archetype([], defaultChunkCapacity);
        _archetypes.Add(_emptyArchetype.Signature, _emptyArchetype);
    }

    // ------------------------------------------------------------------
    // Entity lifecycle
    // ------------------------------------------------------------------

    /// <summary>Create a new entity with no components.</summary>
    public Entity Create()
    {
        int id = _freeIds.Count > 0 ? _freeIds.Pop() : _nextId++;
        EnsureRecordCapacity(id);

        ref var rec = ref _records[id];
        rec.Version++;                  // bump version on slot reuse
        rec.IsAlive = true;
        rec.Archetype = _emptyArchetype;
        var (chunk, slot) = _emptyArchetype.Allocate(new Entity(id, rec.Version));
        rec.Chunk = chunk;
        rec.Slot = slot;

        _entityCount++;
        return new Entity(id, rec.Version);
    }

    /// <summary>Create a new entity carrying a single component.</summary>
    public Entity Create<T1>(in T1 c1) where T1 : struct
    {
        var e = Create();
        Add(e, c1);
        return e;
    }

    public Entity Create<T1, T2>(in T1 c1, in T2 c2) where T1 : struct where T2 : struct
    {
        var e = Create();
        Add(e, c1);
        Add(e, c2);
        return e;
    }

    public Entity Create<T1, T2, T3>(in T1 c1, in T2 c2, in T3 c3) where T1 : struct where T2 : struct where T3 : struct
    {
        var e = Create();
        Add(e, c1);
        Add(e, c2);
        Add(e, c3);
        return e;
    }

    public Entity Create<T1, T2, T3, T4>(in T1 c1, in T2 c2, in T3 c3, in T4 c4)
        where T1 : struct where T2 : struct where T3 : struct where T4 : struct
    {
        var e = Create();
        Add(e, c1);
        Add(e, c2);
        Add(e, c3);
        Add(e, c4);
        return e;
    }

    public Entity Create<T1, T2, T3, T4, T5>(in T1 c1, in T2 c2, in T3 c3, in T4 c4, in T5 c5)
        where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct
    {
        var e = Create();
        Add(e, c1);
        Add(e, c2);
        Add(e, c3);
        Add(e, c4);
        Add(e, c5);
        return e;
    }

    /// <summary>Destroy an entity and free its slot for reuse.</summary>
    public void Destroy(Entity entity)
    {
        if (!IsAlive(entity)) return;

        ref var rec = ref _records[entity.Id];
        var movedEntity = rec.Chunk!.RemoveSwapBack(rec.Slot);

        if (movedEntity.IsValid)
        {
            // The chunk's swap-back moved another entity into our slot — fix its record.
            ref var movedRec = ref _records[movedEntity.Id];
            movedRec.Slot = rec.Slot;
        }

        rec.IsAlive = false;
        rec.Archetype = null;
        rec.Chunk = null;
        rec.Slot = -1;
        _freeIds.Push(entity.Id);
        _entityCount--;
    }

    /// <summary>True if the given entity handle is still valid.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsAlive(Entity entity)
    {
        if (entity.Id < 0 || entity.Id >= _records.Length) return false;
        ref var rec = ref _records[entity.Id];
        return rec.IsAlive && rec.Version == entity.Version;
    }

    // ------------------------------------------------------------------
    // Component mutation
    // ------------------------------------------------------------------

    /// <summary>Add a component to <paramref name="entity"/>. Throws if the component already exists.</summary>
    public void Add<T>(Entity entity, in T component) where T : struct
    {
        ThrowIfNotAlive(entity);
        var ct = ComponentRegistry.Of<T>();
        ref var rec = ref _records[entity.Id];

        if (rec.Archetype!.Has(ct.Id))
            throw new InvalidOperationException(
                $"Entity {entity} already has component {typeof(T).Name}.");

        var dstArchetype = GetArchetypeAfterAdd(rec.Archetype, ct);
        MoveEntity(ref rec, entity, dstArchetype);

        if (!ct.IsTag)
            rec.Chunk!.GetRef<T>(rec.Slot) = component;
    }

    /// <summary>Set a component value, adding it if it does not exist.</summary>
    public void Set<T>(Entity entity, in T component) where T : struct
    {
        ThrowIfNotAlive(entity);
        var ct = ComponentRegistry.Of<T>();
        ref var rec = ref _records[entity.Id];

        if (!rec.Archetype!.Has(ct.Id))
        {
            var dstArchetype = GetArchetypeAfterAdd(rec.Archetype, ct);
            MoveEntity(ref rec, entity, dstArchetype);
        }

        if (!ct.IsTag)
            rec.Chunk!.GetRef<T>(rec.Slot) = component;
    }

    /// <summary>Remove a component from <paramref name="entity"/>.</summary>
    public void Remove<T>(Entity entity) where T : struct
    {
        ThrowIfNotAlive(entity);
        var ct = ComponentRegistry.Of<T>();
        ref var rec = ref _records[entity.Id];

        if (!rec.Archetype!.Has(ct.Id)) return;

        var dstArchetype = GetArchetypeAfterRemove(rec.Archetype, ct);
        MoveEntity(ref rec, entity, dstArchetype);
    }

    /// <summary>True if <paramref name="entity"/> has component <typeparamref name="T"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Has<T>(Entity entity) where T : struct
    {
        if (!IsAlive(entity)) return false;
        return _records[entity.Id].Archetype!.Has(ComponentRegistry.Of<T>().Id);
    }

    /// <summary>Get a ref to a component on <paramref name="entity"/>. Throws if missing.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetRef<T>(Entity entity) where T : struct
    {
        ThrowIfNotAlive(entity);
        ref var rec = ref _records[entity.Id];
        return ref rec.Chunk!.GetRef<T>(rec.Slot);
    }

    /// <summary>Try to get a copy of a component. Returns false if the entity does not have it.
    /// For zero-sized tag components, returns <c>default</c> and <c>true</c> when present.</summary>
    public bool TryGet<T>(Entity entity, out T component) where T : struct
    {
        if (!Has<T>(entity))
        {
            component = default;
            return false;
        }
        var ct = ComponentRegistry.Of<T>();
        component = ct.IsTag ? default : GetRef<T>(entity);
        return true;
    }

    // ------------------------------------------------------------------
    // Internals: archetype graph + entity moves
    // ------------------------------------------------------------------

    /// <summary>Find the destination archetype after adding <paramref name="added"/>, using/filling the edge cache.</summary>
    private Archetype GetArchetypeAfterAdd(Archetype src, ComponentType added)
    {
        if (src.AddEdges.TryGetValue(added.Id, out var cached)) return cached;

        var srcTypes = src.ComponentTypes;
        var dstTypes = new ComponentType[srcTypes.Length + 1];
        // Insert `added` keeping the array sorted by id.
        int j = 0;
        bool inserted = false;
        for (int i = 0; i < srcTypes.Length; i++)
        {
            if (!inserted && added.Id < srcTypes[i].Id)
            {
                dstTypes[j++] = added;
                inserted = true;
            }
            dstTypes[j++] = srcTypes[i];
        }
        if (!inserted) dstTypes[j] = added;

        var dst = GetOrCreateArchetype(dstTypes);
        src.AddEdges[added.Id] = dst;
        dst.RemoveEdges[added.Id] = src;
        return dst;
    }

    private Archetype GetArchetypeAfterRemove(Archetype src, ComponentType removed)
    {
        if (src.RemoveEdges.TryGetValue(removed.Id, out var cached)) return cached;

        var srcTypes = src.ComponentTypes;
        var dstTypes = new ComponentType[srcTypes.Length - 1];
        int j = 0;
        for (int i = 0; i < srcTypes.Length; i++)
            if (srcTypes[i].Id != removed.Id) dstTypes[j++] = srcTypes[i];

        var dst = GetOrCreateArchetype(dstTypes);
        src.RemoveEdges[removed.Id] = dst;
        dst.AddEdges[removed.Id] = src;
        return dst;
    }

    private Archetype GetOrCreateArchetype(ComponentType[] sortedTypes)
    {
        var sig = new BitSet();
        foreach (var t in sortedTypes) sig.Set(t.Id);
        if (_archetypes.TryGetValue(sig, out var existing)) return existing;

        var fresh = new Archetype(sortedTypes, DefaultChunkCapacity);
        _archetypes[fresh.Signature] = fresh;
        return fresh;
    }

    /// <summary>
    /// Move an entity from its current chunk to a new archetype. Copies all components shared
    /// between the source and destination archetypes; new columns in the destination are left
    /// at their default value (the caller writes the new component if needed).
    /// </summary>
    private void MoveEntity(ref EntityRecord rec, Entity entity, Archetype dstArchetype)
    {
        var srcChunk = rec.Chunk!;
        int srcSlot = rec.Slot;

        var (dstChunk, dstSlot) = dstArchetype.Allocate(entity);
        srcChunk.CopySharedComponentsTo(srcSlot, dstChunk, dstSlot);

        var movedEntity = srcChunk.RemoveSwapBack(srcSlot);
        if (movedEntity.IsValid)
        {
            ref var movedRec = ref _records[movedEntity.Id];
            movedRec.Slot = srcSlot;
        }

        rec.Archetype = dstArchetype;
        rec.Chunk = dstChunk;
        rec.Slot = dstSlot;
    }

    // ------------------------------------------------------------------
    // Query API — see Query.cs for the iteration helpers
    // ------------------------------------------------------------------

    /// <summary>Build a query from a description. Cheap; safe to allocate per call, but caching is faster.</summary>
    public Query Query(QueryDescription description) => new(this, description);

    /// <summary>Internal: enumerate archetypes matching a description.</summary>
    internal IEnumerable<Archetype> MatchingArchetypes(QueryDescription desc)
    {
        foreach (var a in _archetypes.Values)
            if (desc.Matches(a)) yield return a;
    }

    // ------------------------------------------------------------------
    // House-keeping
    // ------------------------------------------------------------------

    private void EnsureRecordCapacity(int id)
    {
        if (id < _records.Length) return;
        int newSize = _records.Length * 2;
        while (newSize <= id) newSize *= 2;
        Array.Resize(ref _records, newSize);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfNotAlive(Entity entity)
    {
        if (!IsAlive(entity))
            throw new InvalidOperationException($"Entity {entity} is not alive in this world.");
    }

    public void Dispose()
    {
        // Nothing to release explicitly today — the GC handles arrays — but keep IDisposable so
        // user code can `using var world = new World();` and stay symmetric with Arch's API.
    }
}
