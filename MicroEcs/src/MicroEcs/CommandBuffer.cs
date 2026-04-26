namespace MicroEcs;

/// <summary>
/// Records structural changes (entity create/destroy, component add/remove) so they can be
/// applied later, in one batch. Necessary because mutating archetypes during a query iteration
/// would invalidate the chunks the query is currently walking.
/// 
/// Typical pattern:
/// <code>
/// var cb = new CommandBuffer(world);
/// world.Query(query).ForEach&lt;Health&gt;((ref Health h) => { if (h.Value &lt;= 0) cb.Destroy(...); });
/// cb.Playback();
/// </code>
/// </summary>
public sealed class CommandBuffer
{
    private readonly World _world;
    private readonly List<ICommand> _commands = new();

    public CommandBuffer(World world) { _world = world; }

    public int Count => _commands.Count;

    public void Create<T1>(in T1 c1) where T1 : struct
        => _commands.Add(new CreateCommand<T1>(c1));

    public void Create<T1, T2>(in T1 c1, in T2 c2) where T1 : struct where T2 : struct
        => _commands.Add(new CreateCommand<T1, T2>(c1, c2));

    public void Destroy(Entity e) => _commands.Add(new DestroyCommand(e));

    public void Add<T>(Entity e, in T c) where T : struct
        => _commands.Add(new AddCommand<T>(e, c));

    public void Set<T>(Entity e, in T c) where T : struct
        => _commands.Add(new SetCommand<T>(e, c));

    public void Remove<T>(Entity e) where T : struct
        => _commands.Add(new RemoveCommand<T>(e));

    /// <summary>Apply every queued command, in order, then clear the buffer.</summary>
    public void Playback()
    {
        foreach (var cmd in _commands) cmd.Apply(_world);
        _commands.Clear();
    }

    /// <summary>Discard queued commands without applying them.</summary>
    public void Clear() => _commands.Clear();

    // ---------- internal command types ----------

    private interface ICommand { void Apply(World w); }

    private sealed class DestroyCommand(Entity e) : ICommand
    {
        public void Apply(World w) => w.Destroy(e);
    }

    private sealed class CreateCommand<T1>(T1 c1) : ICommand where T1 : struct
    {
        public void Apply(World w) => w.Create(c1);
    }

    private sealed class CreateCommand<T1, T2>(T1 c1, T2 c2) : ICommand
        where T1 : struct where T2 : struct
    {
        public void Apply(World w) => w.Create(c1, c2);
    }

    private sealed class AddCommand<T>(Entity e, T c) : ICommand where T : struct
    {
        public void Apply(World w)
        {
            if (w.IsAlive(e)) w.Add(e, c);
        }
    }

    private sealed class SetCommand<T>(Entity e, T c) : ICommand where T : struct
    {
        public void Apply(World w)
        {
            if (w.IsAlive(e)) w.Set(e, c);
        }
    }

    private sealed class RemoveCommand<T>(Entity e) : ICommand where T : struct
    {
        public void Apply(World w)
        {
            if (w.IsAlive(e)) w.Remove<T>(e);
        }
    }
}
