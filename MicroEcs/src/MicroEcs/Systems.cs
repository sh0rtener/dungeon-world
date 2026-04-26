namespace MicroEcs;

/// <summary>
/// Per-frame context handed to systems. Bundles the world, delta time, and a frame counter
/// so systems don't have to capture them separately.
/// </summary>
public readonly struct UpdateContext(World world, float deltaTime, long frame)
{
    public World World { get; } = world;
    public float DeltaTime { get; } = deltaTime;
    public long Frame { get; } = frame;
}

/// <summary>
/// Marker interface for systems. A system is the "S" of ECS — pure logic that operates over
/// queries to transform component data. Systems hold no entity state of their own.
/// </summary>
public interface ISystem
{
    /// <summary>Called once when the system is added to a group.</summary>
    void OnCreate(World world) { }

    /// <summary>Called every frame from <see cref="SystemGroup.Update"/>.</summary>
    void OnUpdate(in UpdateContext ctx);

    /// <summary>Called when the owning group is disposed. Default no-op.</summary>
    void OnDestroy(World world) { }
}

/// <summary>
/// Convenience base class — systems usually only need OnUpdate, so this lets you skip the
/// empty default-method overrides on the interface.
/// </summary>
public abstract class SystemBase : ISystem
{
    public virtual void OnCreate(World world) { }
    public abstract void OnUpdate(in UpdateContext ctx);
    public virtual void OnDestroy(World world) { }
}

/// <summary>
/// An ordered collection of systems that all share a world. <see cref="Update"/> ticks them
/// in registration order; groups can be nested for finer-grained scheduling.
/// </summary>
public sealed class SystemGroup : SystemBase
{
    private readonly List<ISystem> _systems = new();
    private World? _world;
    private long _frame;

    public string Name { get; }

    public SystemGroup(string name = "Default") { Name = name; }

    public IReadOnlyList<ISystem> Systems => _systems;

    /// <summary>Register a system. <see cref="ISystem.OnCreate"/> fires immediately if the group is already attached to a world.</summary>
    public T Add<T>(T system) where T : ISystem
    {
        _systems.Add(system);
        if (_world is not null) system.OnCreate(_world);
        return system;
    }

    public override void OnCreate(World world)
    {
        _world = world;
        foreach (var s in _systems) s.OnCreate(world);
    }

    public override void OnUpdate(in UpdateContext ctx)
    {
        // Re-bind the world for any systems added after OnCreate fired the first time.
        _world ??= ctx.World;
        foreach (var s in _systems) s.OnUpdate(in ctx);
    }

    public override void OnDestroy(World world)
    {
        foreach (var s in _systems) s.OnDestroy(world);
    }

    /// <summary>
    /// Convenience: tick the whole group once with the given delta time. Manages the frame counter
    /// internally so callers don't have to.
    /// </summary>
    public void Update(World world, float deltaTime)
    {
        _world ??= world;
        var ctx = new UpdateContext(world, deltaTime, _frame++);
        OnUpdate(in ctx);
    }
}
